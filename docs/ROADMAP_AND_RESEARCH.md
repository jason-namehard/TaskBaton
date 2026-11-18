# TaskBaton（原 Agent Relay）路线图与调研

研究日期：2026-10-22。结论基于当日浅克隆的源码、测试文件、依赖声明、许可证和 CI 配置；未安装依赖、执行第三方脚本或运行其测试。测试“存在”与“本机通过”严格区分。文内固定提交供后续复核，不把主分支的未来变化视为本次证据。

## 决策

**建议首选 AMQ 公共 CLI 作为初始运输适配器，Relay 自己实现产品特有的任务账本、方向权限、显式完成回调、截止时间、TTL、恢复通知和迁移批准。** 不重建 AMQ 已有文件队列；不引入 wake、watcher、daemon 或跨机桥接。原生 Windows 运输须在 Phase 1/2 通过本机验证，才启用 `auto`。

建议 `.NET 10` 共享核心、Microsoft.Data.Sqlite、后续 WPF 和 Windows App SDK 通知。AMQ 作为单独二进制调用，避免为了导入其 Go 内部包而 fork。代价是额外运行时和二进制分发；Phase 1 实测资源后再判断，不先承诺内存数字。

## 固定研究基线

| 项目与仓库 | 本次读取的提交 | 许可证 |
| --- | --- | --- |
| [AMQ](https://github.com/avivsinai/agent-message-queue) | `5d1a96480a98d6f88ec9fb903e7bb4d518858619` | MIT，已读 LICENSE |
| [Multiagent Handoff](https://github.com/aznikline/multiagent-handoff) | `4d88608d6a7a1162d858ce8125e98554f585328b` | 未发现 LICENSE 或 pyproject 许可证声明，暂不能确认复用授权 |
| [Agents Handoff](https://github.com/Ryujin-Labs/agents-handoff) | `482ae55ec865a0c40d64af01f2ace7e463b5ed43` | MIT，已读 LICENSE |
| [AgentBus](https://github.com/jahwag/agentbus) | `50add675991526cee85385388907013ca1ebf09e` | MIT，已读 LICENSE |
| [ACP Router](https://github.com/puzige/acp-router) | `e5e49a9ff1b5a1e454d4cd57bcaab69bf04a8881` | MIT，已读 LICENSE |
| [CC Switch](https://github.com/farion1231/cc-switch) | `cb90a2440d1a771d67f46b65b2e0f2ea5106ce85` | MIT，已读 LICENSE |

各仓库 `LICENSE` 可在对应提交树查看；Multiagent Handoff 无许可证的结论仅限这个提交，不代表未来不能取得授权。Phase 0 只引用设计与证据，未复制任何项目源码；未来分发或复制 MIT 代码需保留版权与许可。

## 候选比较与证据

### AMQ — 采用公共 CLI 的首选候选

已核实：文件队列、JSON header 加 Markdown body、临时文件发布、收件与 receipts、线程和 session 路由已在源码及测试中出现。读取位置包括 [internal/fsq](https://github.com/avivsinai/agent-message-queue/tree/5d1a96480a98d6f88ec9fb903e7bb4d518858619/internal/fsq)、[receipt tests](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/internal/receipt/receipt_test.go)、[adapter contract](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/docs/adapter-contract.md)。后者明确不定义全局去重、任务所有权或通用回复路由。

Windows 仅验证子集，不能称全面支持。[INSTALL 平台矩阵](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/INSTALL.md) 和 [CI](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/.github/workflows/ci.yml) 验证独占收件、并发 drain、文件锁、launchapi 兼容面、Codex/Claude keepalive 适配器。全量 Windows 测试未运行且有已知失败；wake、coop exec、终端 supervision 不属于 Windows 契约。

[Issue #891](https://github.com/avivsinai/agent-message-queue/issues/891) 经 GitHub API 读取仍为 open，记录该限制；[PR #1140](https://github.com/avivsinai/agent-message-queue/pull/1140) 已合并，本次提交已包含 Windows `LockFileEx` 实现。这是已进入上游的修复，不是需要本项目另抄的临时补丁；不能据此推断其余 Windows 能力已修复。

会话注入依据 [keepalive 文档](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/docs/amq-keepalive.md)、[codexqueue.go](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/internal/keepalive/adapter/codexqueue.go)、[claudeprint.go](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/internal/keepalive/adapter/claudeprint.go)：Windows 支持直接 `inject` 的契约；`codex-queue` 需要精确 thread 和 active writer，成功是提交消息；`claude-print` 使用既有 session 的 print/resume 与 stream-json 确认，且检查 owner/cwd。二者都不证明任务完成。其现有窗口表现、认证和本机版本仍须实测。

运行已构建 CLI 不要求 Go；从源码构建的 [go.mod](https://github.com/avivsinai/agent-message-queue/blob/5d1a96480a98d6f88ec9fb903e7bb4d518858619/go.mod) 声明 Go 1.26.0、toolchain 1.26.10，并包含 fsnotify、TOML、schema、加密和远端组件相关依赖。Relay 不因二进制包含可选能力就启动这些能力。队列代码主要在 `internal`，首版不直接作为库导入。

仍需 Relay 实现：任务执行状态、方向权限、B 显式完成、端到端幂等、未知投递对账、TTL、Windows 单次时限任务、配置注册、GUI 与迁移。

### Multiagent Handoff — 参考结构，暂不复制代码

已核实：[pyproject](https://github.com/aznikline/multiagent-handoff/blob/4d88608d6a7a1162d858ce8125e98554f585328b/pyproject.toml) 要求 Python ≥3.11，基础 Pydantic/Pydantic Settings；CLI 为 Typer，MCP、FastAPI、Redis、PostgreSQL、S3、crypto 分别为可选依赖，并非本地使用全部强制。存在 CLI/MCP、session parser、SQLite 索引加 JSON 文件与 TTL。

[Claude adapter](https://github.com/aznikline/multiagent-handoff/blob/4d88608d6a7a1162d858ce8125e98554f585328b/src/handoff_relay/adapters/claude_code.py) 的注入写 `CLAUDE.md`、hooks 配置；[session parser](https://github.com/aznikline/multiagent-handoff/blob/4d88608d6a7a1162d858ce8125e98554f585328b/src/handoff_relay/adapters/session_parser.py) 按本地会话文件尽力解析，不是对活跃会话的消息 API。[local store](https://github.com/aznikline/multiagent-handoff/blob/4d88608d6a7a1162d858ce8125e98554f585328b/src/handoff_relay/storage/local_store.py) 是可参考的本地持久化拆分。

测试存在：[test_relay.py](https://github.com/aznikline/multiagent-handoff/blob/4d88608d6a7a1162d858ce8125e98554f585328b/tests/test_relay.py) 与 [integration](https://github.com/aznikline/multiagent-handoff/blob/4d88608d6a7a1162d858ce8125e98554f585328b/tests/test_relay_integration.py) 覆盖存储、模拟会话、注入/清理和服务操作；fixture 测试不能证明真实 Windows 宿主可恢复。CLI 有 shell-hook、zsh/bash 提示及符号链接操作，未发现 Windows CI 证据。

判断：不能仅因支持 FastAPI/Vercel 就认定本地 relay 必须上云，但其整体上下文编排范围大于本项目，且授权不明确。仅参考结构，暂不直接依赖。Relay 仍缺方向权限、精确投递/执行回调、有界重试和真实 Windows 恢复验证。

### Agents Handoff — 借鉴 Markdown 分层，保留独立任务协议

已核实：[core package](https://github.com/Ryujin-Labs/agents-handoff/blob/482ae55ec865a0c40d64af01f2ace7e463b5ed43/packages/core/package.json) 要求 Node ≥20.10、Git，核心运行依赖 `yaml`；CLI、MCP 和宿主插件是薄层。其任务是完成变更后的团队交接，frontmatter、正文、验证和完整导出适合参考。

读取 [validator](https://github.com/Ryujin-Labs/agents-handoff/blob/482ae55ec865a0c40d64af01f2ace7e463b5ed43/packages/core/src/schema/validate.ts)、[validation tests](https://github.com/Ryujin-Labs/agents-handoff/blob/482ae55ec865a0c40d64af01f2ace7e463b5ed43/packages/core/test/validate.test.ts)、[export tests](https://github.com/Ryujin-Labs/agents-handoff/blob/482ae55ec865a0c40d64af01f2ace7e463b5ed43/packages/core/test/export.test.ts) 与 MCP 测试，可见字段、版本、时间戳、路径约束及凭据形态检查。安全边界确有代码/测试，不只 README 声明。

[CI](https://github.com/Ryujin-Labs/agents-handoff/blob/482ae55ec865a0c40d64af01f2ace7e463b5ed43/.github/workflows/ci.yml) 为 Linux/macOS，Node 20/22/24；未列 Windows，release 脚本有 shell 循环。不能把 Node 跨平台直接当 Windows 已测试。没有任务 ACK、重投和执行状态账本。

判断：采用 frontmatter 与正文分层的设计；不直接套用其 software-change schema，也不为了 Markdown 校验引入第二个 Node 核心。后续确需代码复用时保留 MIT 许可。Relay 自行定义最小任务字段、完成回调、幂等和 TTL。

### AgentBus — 采用可靠交付语义，拒绝默认 daemon

已核实：[bus.go](https://github.com/jahwag/agentbus/blob/50add675991526cee85385388907013ca1ebf09e/internal/bus/bus.go) 使用 SQLite 保存消息、收据、去重和审计，`UNIQUE(from_name, client_message_id)` 约束幂等；[bus_test.go](https://github.com/jahwag/agentbus/blob/50add675991526cee85385388907013ca1ebf09e/internal/bus/bus_test.go) 包含 `TestSendIdempotency`、`TestSendRejectsConflictingIdempotencyReuse`、`TestUnackedDeliveryIsStableAndAckIsIdempotent`、`TestOutstandingDeliveryAndAckSurviveRestart`，覆盖重复提交冲突、稳定 delivery ID、重复 ACK 和重启恢复。

其 HTTP/Streamable HTTP MCP 服务依赖 daemon。[go.mod](https://github.com/jahwag/agentbus/blob/50add675991526cee85385388907013ca1ebf09e/go.mod) 为 Go 1.25.13、modernc SQLite、MCP SDK，另有 Azure/OIDC 组件。[数据库锁](https://github.com/jahwag/agentbus/blob/50add675991526cee85385388907013ca1ebf09e/internal/bus/lock_unix.go) 仅提供 Unix build tags，而 `bus.go` 调用该锁；在这个快照内没有 Windows 对应文件，据此推断不能原样作为原生 Windows 构建。未执行编译以确认报错。[CI](https://github.com/jahwag/agentbus/blob/50add675991526cee85385388907013ca1ebf09e/.github/workflows/ci.yml) 为 Ubuntu，发布目标为 Linux/macOS。

判断：采用 ACK、持久去重、未知投递与重启测试思路；不部署其 daemon，不复制其内部总线。Relay 仍需任务语义、单次 CLI 生命周期、Windows 适配、GUI 和配置迁移。

### ACP Router — 后续发现与 ACP 适配参考

这里明确指 [puzige/acp-router](https://github.com/puzige/acp-router)，不与同名 OpenAI-compatible HTTP 代理混淆。读取 [agents.ts](https://github.com/puzige/acp-router/blob/e5e49a9ff1b5a1e454d4cd57bcaab69bf04a8881/src/agents.ts)、[storage.ts](https://github.com/puzige/acp-router/blob/e5e49a9ff1b5a1e454d4cd57bcaab69bf04a8881/src/storage.ts) 和 package.json：有 ACP Registry 发现、CLI 覆盖、jobs/sessions 和恢复记录，Node ≥18、MCP SDK/Zod，默认能提供 npx 启动方案。

仓库未发现测试套件或 Windows 测试 CI，只有 Ubuntu 发布工作流；构建脚本调用 `chmod`。Windows 可用性待验证。其外部代理启动、工作树和持续 job 生命周期大于 Relay 运输接口，不能自动接受 registry 命令或 npx 安装。

判断：Phase 4 参考发现来源与能力映射；Relay 仍需配置路径验证、方向权限、完成回调及迁移审批。暂不引入其 MCP 服务。

### CC Switch — Windows 配置发现与迁移参考

已核实：[config.rs](https://github.com/farion1231/cc-switch/blob/cb90a2440d1a771d67f46b65b2e0f2ea5106ce85/src-tauri/src/config.rs)、[codex_config.rs](https://github.com/farion1231/cc-switch/blob/cb90a2440d1a771d67f46b65b2e0f2ea5106ce85/src-tauri/src/codex_config.rs) 有宿主路径覆盖和配置处理；[mcp_commands tests](https://github.com/farion1231/cc-switch/blob/cb90a2440d1a771d67f46b65b2e0f2ea5106ce85/src-tauri/tests/mcp_commands.rs) 检查 MCP 更新/删除及保留未管理条目，另有 skill、导入导出和 profile 测试。[CI](https://github.com/farion1231/cc-switch/blob/cb90a2440d1a771d67f46b65b2e0f2ea5106ce85/.github/workflows/ci.yml) 包含 Windows，README 提供 Windows 安装包。

依赖 Rust ≥1.85、Tauri 2、React、SQLite 及广泛配置/代理组件。它是完整管理器，不是可直接替代任务队列的小库；不为发现配置而引入完整 GUI/代理栈。后续参考路径修正、备份和未管理条目保留的实现/测试；不调用其写配置或安装功能。Relay 仍需稳定 Agent 身份、方向授权、任务回调及经批准的通信迁移。

## 官方平台能力与限制

- [Codex 非交互文档](https://developers.openai.com/codex/noninteractive) 支持 `codex exec resume <SESSION_ID>`；[App Server](https://developers.openai.com/codex/app-server) 提供 `thread/resume` 再 `turn/start`。这不证明任意本机桌面会话有可访问连接。AMQ 所用 `codex queue` 以其适配器源码为第三方证据，本机命令与 writer 支持必须另验。
- [Claude CLI](https://code.claude.com/docs/en/cli-reference) 提供 print 和按 session 恢复；还提供配置导入 dry-run 能力。原生导入是迁入 Claude 的工具，不是通用 Relay 通信迁移；真实会话 owner、权限与回调仍需核查。
- [Windows Task Scheduler](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create) 支持 `ONCE`、`ONSTART`、`ONLOGON`。建议当前用户单次 deadline 与登录清理；启动触发的运行身份、权限和未登录通知需单独验证，不能用 SYSTEM 的成功运行代替交互用户通知。
- [Windows 通知](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet) 支持桌面 AppNotificationManager，未打包应用可注册点击激活；需相应 runtime/注册，管理员进程通知当前不支持。Phase 3 必须验证 GUI 关闭后的本地通知与点击路径；不引入云推送。

## 阶段路线

| 阶段 | 交付 | 进入下一阶段的依据 |
| --- | --- | --- |
| Phase 0 | 本六份文档、固定源码研究与架构提案 | 用户批准架构及 MVP 边界 |
| Phase 1 | 单一核心、CLI、注册与方向权限、SQLite 任务/事件/outbox、Markdown、TTL、幂等、有限重试与 recover；可选调度器安装提案 | 原生 Windows fixture/故障测试通过；AMQ 本机运输可用；无常驻进程依赖 |
| Phase 2 | 两个已验证真实 CLI Agent 的完整派发及显式完成/失败闭环 | 漏回调、重复、重启、未知投递和过期结果按协议处理 |
| Phase 3 | 精确交互 session 恢复适配；不支持时本地桌面通知、一键复制继续提示词 | 真实宿主验证已提交与执行完成不同；GUI 关闭可恢复提示；无目标漂移 |
| Phase 4 | 适配器迁移、来源和版本登记、备份/批准/回滚、stdio MCP | 同一核心权限/幂等；旧入口仅在验证及批准后停用 |
| Phase 5 | WPF Agents、Connections、Inbox、Adapters、Audit、Settings | 有向图和可访问表格一致；GUI 关闭不影响 CLI 正确性 |

## 验收计划

下表是后续需要执行的具体失败检测，当前没有运行这些测试。

| 场景 | 通过条件 / 失败时处理 |
| --- | --- |
| A→B 允许，B→A 未授权 | A 派发成功；B 新派发拒绝；B 仍可完成自己的任务；失败则修权限操作分类 |
| Windows AMQ 基线 | send/drain、receipt、线程绑定及两进程收件仅一个认领；失败则保持 manual 并定位上游限制 |
| 首个真实闭环 | A 派发后结束轮次，B 生成结果并显式 complete；只接收文件或 receipt 不得成功 |
| B 明确失败 | 保存 failure.md、失败码与通知；不能转成成功空结果 |
| B 退出 / 漏回调 | deadline 后持久化 timed_out，提示人工恢复；不无限等待 |
| 重复派发与回调 | 相同键返回同一记录，不重复执行/通知；不同内容冲突；失败则修唯一约束和状态转换 |
| 外部提交后 CLI 超时 | delivery_unknown，禁止自动重发；只读对账；失败则修 adapter 返回分类 |
| Relay 重启窗口 | 分别在 outbox 提交后、调用期间、完成事务后终止；恢复不丢任务且未知提交不重复执行 |
| 旧尝试与晚到完成 | 新尝试不受旧结果覆盖；无新尝试的有效迟到完成可归档成功 |
| 配置失效与多 session | 不自动换路径、不选最近会话；要求纠正并重验 |
| TTL 与并发完成 | 仅移除到期受管文件；活动过期任务先转人工恢复；完成不能引用正在删除的产物 |
| 调度器缺失/拒绝 | 状态明确为 On Next Command；recover 能补处理；不声称离线即时提醒 |
| 会话不支持/消失/注入失败 | 任务结果保留，Inbox 有提示词；通知可失败但不吞结果；未知提交不重复唤醒 |
| 迁移与回滚 | 批准前无配置写入；新适配器失败时旧入口可用；后续用户修改不被备份覆盖 |
| GUI / MCP 一致性 | 同一任务及权限规则；关闭 GUI 后 CLI 闭环仍可完成 |

## 124 次调用场景专项

规格未提供原始“124-call”事件日志，不能确认历史根因。这里将其定义为必须防止的回归形态：A 为等待结果连续发起 124 次查询、派发或恢复调用，产生无效 token 消耗或重复工作。

预防：派发只返回一次固定等待响应并要求结束轮次；无 `wait-until-done` 轮询工具；只读 status 不触发投递；重复派发与回调由持久键去重；自动运输提交每尝试最多三次且未知状态立即停止；继续消息独立去重。宿主是否真正结束轮次需真实集成验证，Relay 无法仅靠文字强制任意 Agent 停止生成。

回归用例：

1. fixture 连续 124 次以同一键 dispatch，账本仅一个任务和一个有效执行；重启后再提交仍相同。
2. 连续 124 次 status，外部运输调用数为零、没有新增执行和恢复 outbox。
3. 连续 124 次 complete，只有一个最终转换和一条逻辑结果通知。
4. 模拟明确未提交故障，最多三次运输提交；模拟提交后超时，只提交一次并进入 unknown。
5. 真实 A 接收等待响应后结束轮次，直到回调继续或用户提示才产生下一轮；若仍调用工具，修宿主集成/指令并保留 manual 能力，不把增加等待循环作为修法。

## 未决事项与下一步

1. 本机 Codex/Claude 的准确版本、配置档、认证和会话 owner 尚未盘点；Phase 1 先做只读能力验证，Phase 2/3 再获批运行真实任务。
2. AMQ 当前固定源码的 Windows 子集是否足够支持完整运输闭环，及可获得的 release 是否包含所需 Windows 锁修复，需要真实验证；未通过前不启用 auto。
3. 原生 Windows 的单次计划任务、通知注册、CLI runner 退出后回调和分发体积尚未实测。建议在 Phase 1/3 的原型门槛解决，不增设常驻服务回避。

推荐下一开发步骤：用户批准 Phase 0 后，建立 .NET Core/CLI 最小结构和 SQLite 任务契约，先用 manual/fixture 验证持久状态，再验证 AMQ 原生 Windows 运输；通过后推进两个真实 CLI Agent 闭环。
