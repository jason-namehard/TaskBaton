# TaskBaton（原 Agent Relay）适配器与迁移

状态：AMQ 传输、Codex 只读单次执行、精确会话继续、Windows 通知、stdio MCP 与经审阅的 JSON/TOML 迁移已实现。适配器不得持有独立任务状态机；任务契约见 [PROTOCOL](PROTOCOL.md)。

## 接口与能力

| 操作 | 输入 / 输出与约束 |
| --- | --- |
| `discover` | 返回候选宿主、配置路径、CLI 路径及来源；只读，不安装 |
| `validate` | 验证路径、可执行文件版本、配置格式和目标；返回明确错误，不猜替代路径 |
| `capabilities` | 返回当前平台、主机版本、配置档及目标会话上的能力 |
| `deliver` | 接受任务/尝试、受管请求路径、稳定幂等键；返回 `accepted`、`rejected` 或 `unknown` 及运输凭据 |
| `reconcile` | 根据凭据查询是否已提交；不支持时如实返回未知，禁止以“没查到”代替未投递 |
| `resume` | 对精确 `origin_session` 提交继续消息，独立返回提交证据 |
| `cancel` | 仅在宿主可验证支持时尝试中止执行；取消 Relay 等待不冒充终止宿主 |

能力分别为 `queue_transport`、`cli_dispatch`、`delivery_receipt`、`provider_idempotency`、`session_discovery`、`session_resume`、`live_session_injection`、`completion_callback`、`cancel_execution`。每项报告 `supported`、`unsupported` 或 `unavailable`、原因与证据；另存 `verification_status=unverified|passed|failed|stale`。有能力不等于已获权限。

适配器登记 `adapter_id`、版本、协议版本、仓库及固定提交、许可证、二进制路径、宿主版本范围、验证时间、证据及人工批准记录。使用平台进程参数数组与 stdin，不拼接可执行 shell 字符串。网络返回、配置文件和第三方事件在适配器边界验证；内部契约错误直接报告，不吞成成功空结果。

## 首批适配器

| 标识 | 计划用途 | 自动使用前的要求 |
| --- | --- | --- |
| `manual` | 保存请求、复制给 B；完成后复制继续提示词给 A | 不需要会话能力；不得报告自动交付 |
| `amq-cli` | 复用 `send` / `drain` / receipts / threads | Windows 版本固定，完成本机运输与并发认领测试；AMQ 队列根和会话显式绑定 |
| `codex-exec` | 新启动有界只读非交互任务 | 绑定原生 CLI 路径、版本和配置目录；验证结构化声明和 wrapper 回调 |
| `codex-queue` | 原生 CLI 向精确 Codex thread 提交继续消息 | 版本、配置、UUID、cwd 与会话元数据匹配；原生回执只记为 submitted，消费时机由宿主管理 |
| `codex-exec-resume` | 在原 UUID 的既有 CLI 会话继续一次只读轮次 | 显式选择该模式；验证相同 UUID、turn.completed 和最终回复；不自动替代 queue |
| `claude-print` | 经 keepalive 或原生 CLI 恢复明确 Claude session | 验证 session、cwd、已有 owner、stream-json 提交确认及回调能力；不并发写同一会话 |

AMQ 队列传输和会话唤醒可组合，但能力分别计量：入队成功不意味着 B 正在执行。Phase 2 优先通过 Relay 启动的单次 CLI runner 调用已有 AMQ 命令取件，并在 runner 退出时报告结果；该 runner 只为当前任务存活，不建立常驻收件循环。宿主进程成功退出只能生成执行证据，Relay 终态仍须经过显式 `complete` / `fail` 命令。若没有合法结果文件，runner 提交失败报告。

Phase 2 的 `--execute` 只在用户显式指定时启动一次 worker；`task run` 独立可用。执行唯一键为 `(task_id, attempt_id)`。Codex 返回结构化结果声明，worker 代理提交共享核心的 `complete/fail` 操作，不要求模型拼接 shell 命令。没有声明、声明不合法或缺少轮次完成证据不能算成功；超时保留未知执行状态，不重启 Agent。

执行器把注册配置文件的目录绑定为 `CODEX_HOME`，以 `--ignore-user-config` 复用认证但不加载 config.toml 的 provider、模型与 MCP 设置；新任务使用固定 `read-only`、`ephemeral` 模式。配置目录、CLI 路径或版本变化后需重新验证。会话恢复单独登记为 `resumers`，在任务中保存快照；exec-resume 继续原 session，queue 交由宿主处理。用法和真实验证范围见 [Phase 3](PHASE3.md)。

AMQ `internal/*` 不是可直接导入的公共库。首版复用公共 CLI，不 fork 队列；其 `launchapi` 是启动接口，不是全套消息库。是否增加其他公共库引用，须等首个真实闭环证明需要。

## Agent 注册与路径发现

每个记录包含：`agent_id`（注册后稳定）、显示名、`host`、`host_version`、`cli_path`、`config_profile`、`config_paths`、`instruction_paths`、`session_source`、`adapters`、`validation_status`、`verified_at`、`manual_overrides` 和发现来源。会话 ID 不作为 Agent ID；同一宿主可有多个 Agent。

发现顺序：显式手工覆盖 → 宿主可验证的环境变量/诊断输出 → 已知平台路径候选 → 用户手工选择。候选仅用于展示；读取到目录不等于路径已验证。多配置档或多个候选不自动挑“最新”。配置失效置 `invalid` 并阻止相应自动能力，保留原路径供纠正。

Codex、Claude 的路径及覆盖规则需由宿主版本的官方文档、诊断或实际配置确认；CC Switch 可作为路径覆盖与跨宿主配置的参考，但它管理的 provider/MCP 记录不能直接证明存在 Relay 可调用 Agent。会话历史解析只提供发现线索，不授予会话控制权。

用户修正路径或通信方式后重新验证；CLI 路径、主机版本、配置档或目标改变时，相应验证标记过期。每次自动派发仍检查路径存在和目标匹配；未知新版本不沿用旧版本的自动注入验证。

## 验证与模式选择

验证分为静态检查、隔离 fixture 契约测试和用户同意后的真实无副作用往返。记录测试范围、宿主版本、平台与失败原因；不把 Linux CI 当 Windows 实测。Phase 0 不运行第三方安装器、测试脚本或真实 Agent。

- `auto`：权限允许，且本次所需每项自动能力均 `passed` 才执行。若运输可用、恢复能力不可用，允许派发但预先明确结果需要手工继续。
- `confirm`：持久化 `awaiting_confirmation`，展示准确目标、路径、动作及参数；CLI 不等待人无限输入，非交互调用立即返回待批准。
- `manual`：持久化 `awaiting_manual`，生成请求提示词；B 后续 ack 证明实际收件。

指定 `auto` 但派发能力不满足时转 `awaiting_manual` 并返回人工操作状态；禁止悄悄换主机、会话或执行命令。图中的权限和能力表现见 [UI_SPEC](UI_SPEC.md)。

## 迁移与回滚

Phase 4 的实际用法、manifest、来源登记和验证证据见 [PHASE4](PHASE4.md)。`adapter inspect-config` 只读盘点 JSON/TOML MCP 条目；`adapter sources` 展示声明来源，不等同于运行时能力验证。迁移逐文件、逐片段进行，不自动转译或执行发现的 skills/脚本代码。

1. 只读盘点用户选择的插件、skills、MCP 和脚本，提取命令、配置位置、会话来源、回调及既有权限；命令内容先作为数据存档。
2. 生成适配器提案：能力映射、来源、许可证、依赖、版本、保留项与不支持项。Agent 提交的代码只保存用于审阅，不自动执行。
3. 在独立受管工作区完成审阅和契约测试，再进行用户批准的真实往返；旧入口保持可用。
4. 展示精确配置差异和影响，备份原文件及路径、原版本和恢复方法，再请求用户明确批准应用这份变更。
5. 应用前比较当前内容与预览基线；若用户或宿主已修改，停止并重新展示差异。只修改获批条目，不全文件重建配置。
6. 验证新路径能派发、完成、恢复。确认验证通过且用户明确批准后，才禁用旧通信入口；切换动作逐项审计。
7. 回滚恢复获批条目和旧入口；当前文件有后续修改时展示合并差异，不盲目覆盖。备份不随临时文件三天 TTL 删除。

`disable_route` 仅关闭 Relay 自身有向路由；禁用宿主插件/工具必须走配置变更批准；防止 Agent 绕过需宿主原生权限。UI 必须分别显示这三种效果。

复用候选和许可证证据统一收在 [ROADMAP_AND_RESEARCH](ROADMAP_AND_RESEARCH.md)，此处不承诺尚未完成的自动迁移能力。
