# Agent Relay 协议提案

状态：`relay_version: 1`，单次执行、精确会话继续、stdio MCP 与经审阅的配置迁移已实现。跨进程 JSON 使用 UTF-8，时间使用带 `Z` 的 UTC RFC 3339；GUI 按用户时区显示。实际命令见 [README](../README.md)。

## 任务与事件

任务包含 `task_id`、`requester_id`、`recipient_id`、`origin_session`、`adapter_id`、`mode`、`state`、`revision`、`current_attempt_id`、`created_at`、`deadline_at` 和 `request_artifact_id`。UUID 由 Relay 生成；任务 ID 跨重试稳定，每次经明确批准的重新执行分配新的 `attempt_id`。`origin_session` 可为 `null`，此时仅手工继续；非空必须包含 `host`、`session_id`、`config_profile`、`cwd`，不使用“最近会话”替代精确目标。

事件信封示例：

```json
{
  "relay_version": 1,
  "event_id": "8e7262f0-c1d9-4e8d-a1eb-b7aecb03d688",
  "event": "task.completed",
  "task_id": "c01d8e2c-7050-4e48-9e38-92ce56a24a97",
  "attempt_id": "a7816dbc-5c33-4dbb-9abc-24c0ca2ff9ed",
  "sender_id": "agent-b",
  "idempotency_key": "completion-1",
  "occurred_at": "2026-10-22T08:30:00Z",
  "payload": {
    "result_status": "succeeded",
    "artifact_id": "7369a143-3915-4cb8-a10f-277bfc368465",
    "artifact_path": "workspace/c01d8e2c-7050-4e48-9e38-92ce56a24a97/result.md"
  }
}
```

Relay 另写 `recorded_at` 和事务序号，不按客户端时钟决定事件顺序。信封可用相对 `RELAY_HOME` 的路径；Phase 1 本机 CLI 与账本使用完整受管路径。完成前检查文件存在、可读、未过期。`task.completed` 必须提供成功状态和结果文件；`task.failed` 必须提供失败状态、`error_code`、可读说明和失败报告文件。`side_effects_possible` 未报告时为 `null`，显式标注可能产生副作用时为 `true`，不把未知当成无副作用。

| 事件 | 生产者与含义 |
| --- | --- |
| `task.created` | Relay 持久接受任务，尚未投递 |
| `task.dispatched` | 适配器提供宿主/运输接受凭据，不表示 B 已执行 |
| `task.delivery_unknown` | 接受状态无法确认，停止自动重发 |
| `task.acknowledged` | B 接受当前尝试；AMQ drain 回执仅作证据，不能自动替代 B 的 ack |
| `task.started` | B 声明开始处理 |
| `artifact.updated` | 登记文件版本/路径，不推进任务到完成 |
| `task.completed` / `task.failed` | B 对当前尝试提交最终结果 |
| `task.timed_out` | Relay 检查到截止时间，结果未知 |
| `task.cancelled` | 用户停止等待/后续派发，不代表已终止宿主执行 |
| `task.retry_requested` | 用户确认新执行尝试及其重复副作用风险 |
| `execution.scheduled` / `execution.claimed` | 显式启动和领取一次执行，唯一键为任务与尝试 |
| `execution.unknown` / `execution.launch_failed` | 结果未知或已证明 worker 启动失败；不当作执行成功 |
| `session.resume_submitted` / `session.resume_failed` / `session.resume_unknown` | 继续消息提交结果，独立于任务执行结果 |

## 状态机

```mermaid
stateDiagram-v2
    [*] --> created
    created --> awaiting_confirmation: confirm
    created --> awaiting_manual: manual or unavailable
    created --> dispatching: verified auto
    awaiting_confirmation --> dispatching: approve
    awaiting_confirmation --> awaiting_manual: choose manual
    awaiting_manual --> acknowledged: B ack with delivery evidence
    dispatching --> dispatched: accepted
    dispatching --> delivery_unknown: ambiguous timeout or crash
    dispatching --> failed: definite rejection / retry exhausted
    delivery_unknown --> dispatched: reconcile accepted
    delivery_unknown --> created: prove not submitted / approve new attempt
    dispatched --> acknowledged: B ack
    acknowledged --> running: B start
    running --> succeeded: complete
    running --> failed: fail
    failed --> created: approved new attempt
    timed_out --> created: approved new attempt
    timed_out --> succeeded: valid late completion
    succeeded --> [*]
    cancelled --> [*]
```

补充转换规则：所有非终态可在截止时间后转为 `timed_out`，或由用户转为 `cancelled`。当前尝试的合法 `complete` / `fail` 可直接从 `dispatched`、`acknowledged`、`delivery_unknown` 或 `awaiting_manual` 提交，允许省略中间进度；完成回调本身提供执行发生的证据。晚到结果仅在无新尝试、未取消且产物有效时允许 `timed_out → succeeded/failed`，记录 `late_completion`；旧尝试回调一律不覆盖当前尝试。取消后的结果记录为晚到证据，用户检查后决定是否采纳。

`succeeded` 不因恢复 A 失败改成 `failed`。恢复消息独立使用 `resume_status=pending|submitted|completed|manual_required|unknown|failed`，用户可随时复制继续提示词。`submitted` 不代表 A 已阅读；`completed` 表示精确 UUID 的一次新轮次已返回完成证据。

Phase 3 继续 outbox 唯一键为 `(task_id, attempt_id, event)`。领取状态为 `in_flight`；恢复时提交 lease 已过期则记为 `unknown` 并停止重发。结果过期、取消或新尝试不会自动唤醒旧任务。`desktop_state` 单独记录 `pending/in_flight/submitted/unavailable/failed/unknown/not_required`，通知失败不覆盖任务或会话状态。通知激活绑定 task 与 attempt，旧尝试的通知不能复制新尝试的提示词。

本阶段 `origin_session.config_profile` 表示注册的 `config.toml` 绝对路径；Codex 命名 profile 尚未接入。恢复适配器在派发时保存快照。只读的 `status`、`task continuations` 和 Inbox 不触发继续；`complete/fail`、单次 worker 及 `recover` 在结果事务后处理待办，`task continue` 可显式处理尚未提交的记录。

## 幂等、重试与时限

- 派发唯一键为 `(requester_id, idempotency_key)`；回调唯一键为 `(sender_id, task_id, attempt_id, idempotency_key)`，`event_id` 也唯一。相同键和相同业务字段返回原结果，不重复写副作用；相同键不同内容返回 `idempotency_conflict`。
- 接收方按 `(task_id, attempt_id)` 去重，收到重复运输消息不得重复执行。两个不同最终结果即使键不同也返回 `state_conflict`；成功结果不可被后来的失败覆盖。
- 默认最多 3 次运输提交尝试，包含首次；仅明确尚未提交或供应方支持可靠幂等时，按 1 秒、2 秒延迟重试。次数持久化，不因 CLI 重启清零。
- `unknown` 不自动重试。对账只读供应方凭据；不能证明未提交时保持未知并提供人工处理。人工重新执行使用新尝试，不覆盖旧证据。
- 建议默认任务截止 30 分钟、单次运输调用上限 30 秒，均可配置且任务创建时固化。调用超时不等于任务失败，也不证明未投递。
- CLI 内有限重试用计时等待，不让 Agent 发起反复工具查询。需要 CLI 退出后的延迟动作时，仅使用已启用的单次系统任务；没有调度器则持久化待办并明确提示下次 `recover`。

## Markdown 产物

每次尝试使用 `workspace/<task_id>/<attempt_id>/`，请求和已接受结果按 `request-<artifact_id>.md`、`result-<artifact_id>.md` 或 `failure-<artifact_id>.md` 独立保存。`result.md` 是交给 B 的结果写入位置，完成时登记该提交文件并生成不可变快照。使用 YAML frontmatter：`relay_version`、`task_id`、`attempt_id`、`sender_id`、`recipient_id`、`artifact_kind`、`created_at`、`expires_at`；正文建议使用 `Summary`、`Instructions`、`Acceptance Criteria` 或 `Result`、`Verification`、`Limitations`。

正文是交接数据，不是自动执行脚本。Relay 不推断任务、不执行嵌入命令。格式借鉴 Agents Handoff 的 frontmatter/正文分层，但该项目的变更交接 schema 不是 Relay 任务 schema；不宣称协议兼容。

## CLI 与 MCP 契约

Phase 1 命令（需要先构建 CLI）：

```text
relay agent discover
relay agent register --id agent-a --config-path <absolute-path>
relay agent validate --id agent-a
relay agent verify-executor --id agent-b
relay agent executors
relay permission set --from agent-a --to agent-b --decision allow
relay task dispatch --from agent-a --to agent-b --request <path> --mode auto --idempotency-key <key>
relay task approve --task <id>
relay task dispatch --from agent-a --to agent-b --request <path> --mode auto --idempotency-key <key> --execute
relay task run --task <id> --attempt <id> --sender agent-b --timeout-seconds 300
relay task execution --task <id>
relay task ack --task <id> --attempt <id> --sender agent-b --idempotency-key <key>
relay task start --task <id> --attempt <id> --sender agent-b --idempotency-key <key>
relay artifact register --task <id> --attempt <id> --kind result --path <path>
relay task complete --task <id> --attempt <id> --sender agent-b --status succeeded --result <path> --idempotency-key <key>
relay task fail --task <id> --attempt <id> --sender agent-b --status failed --error-code <code> --result <path> --idempotency-key <key>
relay task status --task <id> --json
relay task retry --task <id> --reason <text>
relay task cancel --task <id> --reason <text>
relay recover --task <id>
relay inbox list --json
relay prompt show --task <id>
relay artifact inventory
relay cleanup --dry-run
relay cleanup --apply
```

`dispatch` 接受可选 `--deadline-at` 和 `--origin-session <json-file>`；默认截止见上节。`retry` 交互展示当前尝试及副作用风险，非交互须提供明确批准的确认输入；不能从 `unknown` 静默改成可重试。未知投递若经用户批准重新执行，必须创建新尝试并拒绝旧尝试的后续终态覆盖；若已证明未提交，可继续原尝试的剩余运输预算。缺失批准返回待用户操作。`complete` / `fail` 对尚未登记的有效受管结果文件，在同一完成事务中登记产物；`artifact register` 用于提前登记。

`dispatch --execute` 仅用于 `auto`，要求目标执行器已经验证；`confirm` 在 `approve --execute` 时启动。单次 worker 默认执行上限 180 秒，允许显式设为 1–1800 秒。它从 AMQ 领取与运输凭据一致的消息，再触发 `ack/start`。Codex 声明包含 `status`、`result_markdown`、`error_code`、`message`；成功需要非空 Markdown、空错误码、有效会话及 `turn.completed` 证据，随后由 worker 代理提交既有显式完成操作。失败需要错误码、说明和失败报告。stdout/stderr 使用明确的 UTF-8。

执行超时没有有效最终声明时，保存 `execution.state=unknown`，任务仍等待有效结果或整体 deadline。`recover` 对过期 worker 租期同样只标未知，不能重新运行模型。`task retry --approve` 才能创建新尝试，再由用户显式启动新的 worker。`--report-path` 用于单次 worker 将 CLI 响应写入受管报告文件，使父派发命令可立即退出。

JSON 统一返回 `{relay_version, ok, task_id, state, next_action, data, error}`；不适用字段为 `null`。退出码：`0` 操作已持久接受，`2` 参数/格式错误，`3` 权限或验证拒绝，`4` 需要人工操作，`5` 状态/幂等冲突，`6` 投递未知，`1` 其他执行错误。`ok=true` 只代表命令本身完成，不代表任务执行成功；`state` 才表示任务状态。

自动投递成功时固定等待文案：`任务 {task_id} 已交接，正在等待结果文件 {result_path}。请结束当前轮；收到继续消息或用户粘贴提示词后再处理。` 手工模式返回 `awaiting_manual`，明确说明尚需用户把请求提交给 B。

Phase 4 stdio MCP 映射共享核心的 `relay_task_dispatch`、`relay_task_ack`、`relay_task_start`、`relay_task_complete`、`relay_task_fail`、`relay_task_status`、`relay_prompt_show` 七个工具。服务器启动时绑定 Agent，from/sender 不由工具参数提供；读取仅限参与任务。没有无限等待或后台轮询工具。权限、幂等、当前尝试和完成要求与 CLI 相同。

工具结果同时返回文本 JSON 与 structuredContent。错误使用 `isError=true` 和核心错误码；手工等待、投递未知和接收方显式失败仍须按任务状态处理，不自动重新执行。管理权限和配置批准不通过 MCP 暴露。MCP 协议协商、JSON-RPC 和取消由官方 SDK 处理，stdout 仅用于协议，诊断输出到 stderr，EOF 退出。

迁移使用 `planned → applying → applied → validated`，回滚使用 `rolling_back → rolled_back`。中断的 applying/rolling_back 由 `migration recover` 根据精确原始/候选字节判断，内容不匹配则为 unknown，不自动写文件。每份提案保存单一文件的原始、候选和前后片段；apply/rollback 均须显式批准与原因，并拒绝覆盖后续修改。`disable_old` 必须引用已 validated 的新迁移，验证只接受当前尝试中匹配 route-id 的 MCP 派发、后续 task.completed 与 session.resume_submitted/completed；各批准与变化写 migration_events，备份不进入 TTL。

## TTL 与清理

配置 `artifact_ttl_days` 默认 `3`。每份产物在创建时记录 `created_at`、`expires_at`；修改文件不刷新 TTL，新产物单独计算。任务截止时间与文件过期时间独立。

文件清单显示受管产物、明确记录的原件和交付物、任务控制文件及归属未确认的文件。任务引用按账本中的路径展示。仅清理位于当前中转站、已过期、归属确认为 Relay 且没有活跃引用的文件。桌面确认只应用预览中的候选，并在写入事务内复核归属、引用和文件状态。CLI 的 `--dry-run` 列出当前候选，`--apply` 显式执行当次候选的清理。

清理保留任务、事件和幂等键，也保留仍有活跃引用的过期文件。未登记或无法确认归属的 workspace 文件保留。任务删除后保留文件归属记录，以便后续查看和清理明确归属的过期产物。临时文件保留期与账本内容保留期分别计算。
