# Phase 4 — MCP & Reviewed Migrations

日期：2026-10-22。CLI、stdio MCP 和 Windows 通知共用一个核心与 SQLite 账本。

## 交付能力

`relay-mcp` 使用官方 C# SDK，通过 stdio 为一个明确登记的 Agent 暴露七个任务工具。权限、幂等、当前尝试、结果文件和会话继续均调用已有核心。派发可以显式启动有界 worker，收到响应即结束当前轮；MCP 客户端退出后，独立 worker 仍可完成任务与继续原会话。

配置迁移按单个文件、单个明确片段生成提案，支持 JSON 和 TOML。盘点只读取 MCP 名称、命令、参数、URL 和 enabled 状态，不运行发现的命令，不输出 env 值。来源、版本、修订、声明许可、能力、依赖和限制写入独立登记表，不授予自动执行能力。

预览保存原始字节、完整候选与可读的前后片段，不修改目标配置。新入口与已有入口并行加入；`install` 提案不能移除、禁用或替换已有启用 MCP 的命令/参数/URL。应用须显式 `--approve --reason`，并检查目标是否仍与预览基线一致；写入前保存备份，以同目录临时文件发布。UTF-8 BOM、换行和无关内容保持原字节。

旧入口使用单独的 `disable_old` 提案，引用已验证的新迁移。验证要求当前尝试有对应 route-id 的 MCP 派发、后续显式完成、原会话继续提交或完成证据；旧尝试或其他 route 的结果不能替代。每个旧入口分别批准；回滚按相反顺序进行，遇到后续修改停止并给出快照位置，不覆盖用户修改。中断恢复只根据原始/候选字节核对状态，不自动重写配置。

## 构建与启动

```powershell
dotnet restore --locked-mode
dotnet test -c Release --no-restore
pwsh ./scripts/check-licenses.ps1
pwsh ./scripts/publish.ps1

$relay = './artifacts/relay/relay.dll'
$dataRoot = Join-Path $PWD '.relay'
dotnet $relay --home $dataRoot init
dotnet ./artifacts/relay/mcp/relay-mcp.dll --home $dataRoot --agent agent-a
```

已有数据目录先运行 `init` 增加 `adapter_sources`、`migrations`、`migration_events` 表。Agent 须事先 register/validate，方向权限默认拒绝。MCP 不负责注册、改变权限、批准配置或启用旧入口停用；这些管理操作保留在 CLI。stdio 进程由客户端持有，stdin EOF 后退出，不创建 HTTP 服务或常驻 Relay 服务。

以 Codex 配置为例，每个 Agent 使用独立的 MCP 实例，共用数据目录：

```toml
[mcp_servers.agent_relay]
command = "C:/tools/dotnet/dotnet.exe"
args = ["C:/agent-relay/mcp/relay-mcp.dll", "--home", "C:/agent-relay/.relay", "--agent", "agent-a", "--route-id", "<migration UUID>"]
tool_timeout_sec = 240
```

请使用本机已确认的路径，并经迁移预览与批准后修改宿主配置。`--route-id` 为迁移验证绑定的 ID；直接启动时可省略，默认 `direct`，直接调用的任务不能充当某份迁移的验证证据。首次接入或修改后按宿主流程重新加载 MCP 配置。

## MCP 工具

| 工具 | 参数与行为 |
| --- | --- |
| `relay_task_dispatch` | `to/content/idempotency_key`，可选 `mode/execute/timeout_seconds/origin_session`；from 固定为绑定 Agent |
| `relay_task_ack` | `task_id/attempt_id/idempotency_key`；sender 固定为绑定 Agent |
| `relay_task_start` | 同 ack，显式报告开始 |
| `relay_task_complete` | `task_id/attempt_id/idempotency_key/result_path`；提交非空受管结果文件 |
| `relay_task_fail` | 同 complete，另需 `error_code/message`，可选 `side_effects_possible` |
| `relay_task_status` | `task_id`，单次只读，不触发投递、执行或恢复 |
| `relay_prompt_show` | `task_id`，读取交接或继续提示词 |

结果同时提供 text JSON 与 `structuredContent`。`ok=true` 表示操作被接受，仍需查看任务状态；等待、投递未知与接收方声明失败不是重复派发的理由。参数、权限或回调错误返回 `isError=true` 与共享核心的错误码。B 使用宿主的文件工具写受管 `result_path`，再调用 complete/fail，写文件本身不算完成。

`execute=true` 要求 `mode=auto`、已验证的 B 执行器和 AMQ；派发前校验执行时限为 1–1800 秒。继续原 A 的 `origin_session` 格式与 [Phase 3](PHASE3.md) 一致。绑定身份与有向权限适用于本机协作者；宿主能否绕开工具执行其他命令，仍由宿主原生权限控制。

## 迁移流程

先只读盘点用户指定的配置文件：

```powershell
dotnet $relay --home $dataRoot adapter inspect-config --path C:/Users/you/.codex/config.toml --format toml
```

准备 `migration.json`，填写实际来源、固定修订和路径。空 `before` 表示追加到文件末尾；非空必须精确出现一次。`{{migration_id}}` 在创建提案时替换成该提案 UUID，替换后的内容会完整显示在预览中。

```json
{
  "source": {
    "adapter_id": "relay-mcp",
    "version": "0.4.0",
    "repository": "https://github.com/jason-namehard/TaskBaton",
    "revision": "<reviewed commit>",
    "license": "MIT",
    "capabilities": ["stdio_mcp", "completion_callback", "session_resume"],
    "dependencies": [".NET 10"],
    "limitations": ["Local stdio only"]
  },
  "target_path": "C:/Users/you/.codex/config.toml",
  "format": "toml",
  "before": "",
  "after": "\n[mcp_servers.agent_relay]\ncommand = 'C:/tools/dotnet/dotnet.exe'\nargs = ['C:/agent-relay/mcp/relay-mcp.dll', '--home', 'C:/agent-relay/.relay', '--agent', 'agent-a', '--route-id', '{{migration_id}}']\ntool_timeout_sec = 240\n",
  "purpose": "install"
}
```

```powershell
$proposal = dotnet $relay --home $dataRoot migration plan --manifest ./migration.json | ConvertFrom-Json
$id = $proposal.data.plan.migration_id
dotnet $relay --home $dataRoot migration show --id $id

# 操作者审阅准确的路径、来源和前后片段后批准。
dotnet $relay --home $dataRoot migration apply --id $id --approve --reason '已审阅该配置片段'

# 从新入口完成真实派发、显式完成与原会话继续后，提供该任务 ID。
dotnet $relay --home $dataRoot migration validate --id $id --task <proof-task-UUID>
dotnet $relay --home $dataRoot migration audit --id $id
```

停用旧入口需创建第二份 manifest：`purpose=disable_old`、`validated_migration_id=<新入口迁移ID>`，`before/after` 仅描述准确的旧条目变更。例如对已经验证由 Codex 管理的旧 stdio entry，将其唯一的 `enabled = true` 改成 `enabled = false`。再次 plan/show，审阅后单独 apply。配置中的停用在宿主重新加载后生效，不冒充已经终止正在运行的宿主进程。

```powershell
dotnet $relay --home $dataRoot migration rollback --id <旧入口停用迁移ID> --approve --reason '恢复旧入口'
dotnet $relay --home $dataRoot migration rollback --id $id --approve --reason '恢复新入口加入前的配置'
dotnet $relay --home $dataRoot migration recover --id $id
dotnet $relay --home $dataRoot adapter sources
```

迁移快照和备份在 `RELAY_HOME/migrations/<id>/`，不进入任务产物 TTL。备份包含原配置，按本机私有数据保存，不放进 GitHub 或发布包。源版本记录不可被不同内容覆盖；来源、修订或能力发生变化时登记准确的新版本。

## 已核实的结果

本机 80 项回归通过，包括两个独立 stdio 进程、`2025-11-25` 与 `2026-07-28` 协议、中文往返、124 次重复操作、EOF 退出、方向与身份权限、缺少执行器、失败回调、预览不写配置、批准、BOM/换行、后续修改保护、新文件回滚、同文件多个旧入口、旧尝试与其他 route 的证明拒绝、中断恢复、来源版本冲突及新入口保留/逆序回滚要求。

真实 MCP→B→原 A 验证任务为 `888ea4ba-4fd3-401f-b7f8-30683daef79a`，迁移 ID `ba5e71fa-f3b6-46ed-a357-2685cb1dd9fe`。A 会话 `01a16428-7b00-7aa0-bb35-83d2018018cf`，B 会话 `01a1642a-4a0e-7771-94ef-f13a71ac21ee`。从已应用的候选 TOML 启动 MCP，派发约 `0.43 s` 返回；MCP 客户端关闭后，独立 worker 完成 B，原 UUID 的 A 恢复旧上下文并读出 `54`。继续与执行均去重，随后验证新入口、单独停用旧入口并按序回滚，原配置恢复。隔离候选也经本机 Codex `mcp list --json` 识别。

可复现脚本创建专用配置文件和测试会话，会使用本机已有认证并消耗模型额度，不改动宿主现有配置：

```powershell
pwsh ./scripts/verify-real-agents.ps1 -Dotnet dotnet -Codex C:/tools/codex.exe -Amq C:/tools/amq/amq.exe -ConfigPath C:/Users/you/.codex/config.toml -McpMigration
```

普通 CI 不读取登录凭据、不调用模型，执行确定性协议/状态测试、锁定恢复、全部依赖许可检查和打包。Windows 通知辅助程序与原 Phase 3 功能一同分发。

## 已知边界

迁移以单个 JSON/TOML 普通文件为单位，文件链接需指定真实文件再预览；多个文件逐项批准、逆序回滚。遇到后续编辑停止并要求重新预览，不做自动合并。skills、插件脚本和任意适配器源码不自动改写或执行。

来源和能力字段是操作者声明，不替代许可证审阅或运行时 adapter/executor 验证。迁移验证核对 Relay 真实事件与新入口片段，不能证明所有宿主已重新加载配置；每种宿主仍需按其界面/CLI 进行实际接入确认。本机真实验证覆盖当前 Codex、Windows 和 AMQ，未声称其他宿主均完成验证。

无系统计划任务时，所有进程退出后的截止时间仍在下次 `recover` 处理；旧入口的操作系统进程和宿主权限由宿主控制。WPF 管理界面属于 Phase 5。

## 来源与许可

MCP 复用 [官方 C# SDK](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/getting-started.html) `2.3.0`，协议兼容范围通过原生子进程测试确认。TOML 读取和语法校验使用 [Tomlyn](https://github.com/xoofx/Tomlyn) `2.10.1`；写入使用已批准的精确片段，不将整份配置重新序列化。Codex 配置字段依据 [官方 MCP 配置文档](https://learn.chatgpt.com/docs/extend/mcp) 和本机 CLI 读取结果。项目已有 SQLite、子进程、worker、权限与回调代码继续共用。

SDK 的包声明为 Apache-2.0，随附许可还保留授权转换中的 MIT 内容及第三方声明；Tomlyn 为 BSD-2-Clause。完整声明保存在 `licenses/`，见 [第三方说明](../THIRD_PARTY_NOTICES.md)。未复制调研候选项目的应用源码。
