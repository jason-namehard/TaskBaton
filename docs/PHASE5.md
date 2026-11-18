# Phase 5 — Windows GUI

日期：2026-10-23。`TaskBaton.Desktop` 是原生 WPF 管理界面，CLI、MCP、GUI 共用 `RelayService` 和 SQLite。GUI 不承担任务推理，不创建常驻服务、计划任务、文件 watcher 或周期查询。

`0.1.0-preview.2` 增加六语界面、莫兰迪配色与配置助手；操作见 [配置助手与外观](CONFIGURATION_ASSISTANT.md)。

## 启动

Windows x64 分发包中运行：

```powershell
./gui/TaskBaton.Desktop.exe
# 使用与 CLI/MCP 相同的数据目录：
./gui/TaskBaton.Desktop.exe --home C:/relay-data
```

数据目录优先取 `--home`，其次 `TASKBATON_HOME`、`RELAY_HOME`，再读新旧位置记录，默认 `%LOCALAPPDATA%/TaskBaton`（首次会把旧 `%LOCALAPPDATA%/AgentRelay` 复制过来并保留旧目录）。首次启动创建账本，已有账本增加缺少的表。启动做一次 `RecoverAsync`，可能处理已持久化的运输和会话继续操作；不会重新启动未知执行的模型。运行期间 `Refresh` 只读，`Recover` 为明确的管理操作。

GUI 与通知程序均使用自包含 .NET 运行库；CLI、MCP 仍需要 .NET 10 runtime。Windows 项目单独构建，跨平台 solution 保持 Linux 可用。

## 页面与操作

| 页面 | 已实现的操作 |
| --- | --- |
| Home | Agent 数、非终态任务数、按任务去重的待处理数、TTL；查看待处理任务与复制提示词 |
| Agents | Discover 候选、登记、选择配置文件、纠正路径、Validate；保留宿主版本和指令文件列表 |
| Connections | 方向箭头、等价表格、边详情；分别允许或拒绝单一方向 |
| Inbox | 所有任务、当前尝试、下一动作、运输/执行/继续证据、审计；打开文件、复制、恢复、批准派发、重试、取消；通知已读 |
| Adapters | AMQ、执行器、继续适配器的显式验证；来源、版本、许可和验证记录；配置盘点与迁移 |
| Audit | 时间顺序事件、操作者、任务/尝试和细节；按任务 ID 筛选 |
| Settings | TTL、任务/运输时限、尝试次数；清理预览与批准；通知程序启停 |

登记或纠正路径只修改 Relay 登记，不写宿主文件。Discover 只读候选，不自动登记。宿主配置、CLI、指令文件失效时保留原路径并显示错误；修正后单独 Validate。适配器 Test 会在审阅批准后执行用户指定的程序，验证不调用模型。

## 方向、能力与任务状态

Green 表示已允许且当前手工交接可用；Red 表示明确拒绝；Gray 表示能力不可用或未验证。没有权限记录的方向显示 `Default deny`、灰色虚线与文字，不伪装为明确拒绝。路径不可用时，保留原有 `Permission: allow` 供检查。允许 A→B 不增加 B→A 权限，也不修改宿主原生权限。

手工交接、已验证 AMQ 运输、模型执行与原会话继续分别展示；AMQ 验证不证明模型已执行。超过八个 Agent 时图只画明确配置的方向，表格仍展示全部方向。边可点击选择对应表格行，表格支持键盘选择。

任务详情调用共享核心生成当前提示词，结果过期或缺失时不复制看似有效的结果路径。复制只写剪贴板，不粘贴或执行。结果文件只能在有效登记且未过期时打开。通知已读只改变 Inbox。

`Approve Dispatch` 只批准运输；`Retry` 明确可能重复副作用并创建新尝试，两者均不自动启动模型 worker。`Cancel` 停止 Relay 后续派发，不宣称终止宿主进程。自动执行仍通过 CLI/MCP 的显式执行选项。已过期、未知投递和未知执行保留证据，恢复遵循已有核心契约。

## 迁移审阅

Adapters → Migrations 提供 `Inventory`、`Plan Manifest`、`Apply`、`Validate Proof`、`Rollback` 和 `Recover State`。Manifest 格式和真实新入口证明见 [Phase 4](PHASE4.md)。

选择提案后展示来源、目标、用途、精确前后片段、完整候选与备份位置，以及迁移审计。Apply/Rollback 打开独立审阅窗口；填写理由并勾选本次批准后按钮才可用。取消对话框不写目标配置。应用重新核对原始快照，回滚拒绝覆盖后续修改。旧入口停用仍要求单独提案和已验证的新入口，不由 GUI 绕过。

## 事件刷新

Windows 写事务提交后，以 `RegisterWindowMessage` 与非阻塞 `PostMessage` 发布不含数据正文的提示。GUI 通过 WPF `HwndSource.AddHook` 接收，合并同一轮消息；Home/Inbox 只读更新。编辑页面保留字段和当前迁移预览，显示 `Ledger changed · Refresh`，由用户刷新。

该提示只影响可见界面，不参与任务持久性。不同数据目录的窗口收到同一提示后仍只读取自己的账本；不同桌面会话、权限级别或无法接收提示时可手工 Refresh。窗口关闭后没有 GUI 工作线程维持状态。没有系统调度器时，所有进程退出后的截止时间在下次 Recover 持久化。

## 构建与验证

```powershell
dotnet restore --locked-mode
dotnet test -c Release --no-restore
dotnet restore src/TaskBaton.Notify/TaskBaton.Notify.csproj --locked-mode
pwsh ./scripts/test-desktop.ps1
pwsh ./scripts/check-licenses.ps1 -IncludeDesktop
pwsh ./scripts/publish.ps1 -IncludeDesktop
```

本机 82 项核心回归通过。窗口测试运行真实 WPF 消息循环和控件事件，覆盖七页导航、方向语义、路径纠正及重新验证、剪贴板、通知已读、任务取消、迁移只读预览、取消批准、批准应用及回滚、设置保存/拒绝非法值。独立 CLI 子进程修改账本后，实际窗口消息触发 Home 更新。测试输出 `report.json` 和 Home、Connections、Inbox、Migrations 截图供查看。

脚本使用新的专用目录与虚构宿主配置，不调用模型、不复制认证、不修改现用 Agent 配置。Windows CI 运行该窗口测试，Windows/Linux CI 均继续执行核心测试、锁定恢复、许可检查和打包。

## 已知边界

默认窗口为 1120×780，最小 900×620；长列表和详情滚动，尚无小于该尺寸的单页导航。大规模 Agent 的全方向表格为平方增长，面向本机少量 Agent 协作。跨桌面会话或权限级别不承诺自动 UI 提示送达，手工 Refresh 始终读取同一账本。

通知点击继续使用已有独立任务详情和复制窗口；GUI 的完整任务管理在 Inbox。暂未提供安装器、自动更新、系统调度器或宿主进程中止功能。

## 复用与许可

界面复用平台 [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) 的控件、布局、剪贴板和文件对话框；跨进程提示使用 [PostMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagea) 与 [HwndSource.AddHook](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.hwndsource.addhook)。没有增加第三方 UI 框架、图形库或应用源码。MIT 应用代码与所附运行库分别遵循原有许可，见 [分发条款](../DISTRIBUTION_TERMS.md)。
