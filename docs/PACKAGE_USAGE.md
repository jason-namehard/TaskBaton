# TaskBaton（原 Agent Relay）发布包

本文件同时存在于发布包中（便携包内为 `README.md`，安装版内为 `docs\README.md`）。英文说明见文末 [English](#english)。

## 启动方式

- **Windows 安装版**：从开始菜单启动，程序位于 `%LOCALAPPDATA%\Programs\TaskBaton\app`，文档位于 `docs`。在系统「设置 → 应用 → 已安装的应用」中卸载。
- **便携包**：完整解压到固定目录后运行 `gui\TaskBaton.Desktop.exe`；保留解压目录位置，通知程序需要从这里启动。
- **演示**：`TryDemo.cmd` 使用隔离账本和预设样本，不调用真实 Agent，也不读取你的现用配置。

## 第一次任务

在任务页面选择“新建任务”，再选自动或手动传递。自动传递直接填写工作要求、选择会话；持续验收可选择最终验收方，标准可手动填写或由发起方 Agent 拟定，也可以不设验收次数上限。最终验收方通过且无未解决问题时任务完成。Relay 发送标准并记录 Agent 回执，不独立验证工作质量；自动调用可能产生模型费用。

手动传递先填写工作正文，复制完整提示词给发送方，将返回的 JSON 粘贴并解析创建，随后按看板提示传给当前接收方。需要继续时可多轮往返，整项任务明确完成后才结束。也可以选择已有文件。文件变化不会完成任务，只有有效回执才改变状态。

## Agent 配置

Agent 管理只有一个用户名称；一键配置复制的是独立新建请求，不覆盖当前选中节点。编辑已有节点请使用“编辑 Agent”。CLI 调用配置为分区字段，支持原生 JSON/JSONL/文本适配；导入方案不启动模型。手动 CLI 发送不要求测试；自动投递要求探针（含写文件检查）通过并确认可见，或一次真实交接写回了回执。真实调用可能产生费用；已有会话必须明确选择，执行前核实存储，不能用最近会话替代。

规则安装只在用户确认后修改所选宿主指令文件，常驻一行仍占少量上下文。卸载时可以选择移除当前站点登记的规则，或在设置中移除；便携版也可用 `guidance remove --all`。

## 运行库与入口

Windows 安装版自带运行库，桌面程序、`app\taskbaton.exe` 与 `app\mcp\taskbaton-mcp.exe` 都不需要另外安装 .NET；可在 `app` 目录运行 `.\taskbaton.exe --help`。便携 CLI/MCP 包使用 `dotnet taskbaton.dll` 和 `dotnet mcp/taskbaton-mcp.dll`，需要 .NET 10 runtime；MCP 不实现 `--help`。旧名 `relay.exe`、`tb`/`relay.dll` 和 `mcp/relay-mcp.exe`/`relay-mcp.dll` 作为兼容入口一并保留。发布包不包含目标 Agent、账号或桥接程序。JSON 配置不等于可调用接口，真实 CLI 探针可能产生费用。详细上手见 `GETTING_STARTED.md`。

## 卸载与数据

- 卸载时默认**保留**数据：中转站、账本、偏好和交接规则都不动，重装后可继续使用。
- 可选择**只移除规则**：删除 Relay 写入各宿主指令文件、带标记的段落，账本与偏好保留，你的其他指令不受影响。
- 也可选择**清除账本与偏好**：删除当前站点的 Agent 配置、任务记录和外观偏好；工作区文件和中转站里的其他文件保留，此操作不可撤销。
- 静默卸载保留数据。规则清理只覆盖当前中转站的登记记录；用过的其他站点需要分别处理。删除安装目录或解压目录都不能代替卸载或移除规则。

## 其他

任务详情允许取消、确认删除记录以及人工标记完成。删除不直接删文件，后续仍可按保留期清理；人工完成不验证 Agent 的工作，也不停止已运行的 Agent。CLI 结果不确定时先检查真实会话，不重复发送同一轮。

文件回执位于任务的隐藏 `.relay` 子目录，GUI 打开时观察事件，启动和刷新补读。所有进程关闭后无后台监控。迁移要求没有未完成任务，复制文件与账本后切换，旧站备份不自动清理；无 pending 的迁移校验不会假称成功。

最小化按钮缩到任务栏；只有 × 按“关闭时最小化到托盘”设置隐藏或退出。首次点击 × 询问并记住选择，以后不再询问，设置可修改。托盘“打开”恢复窗口，“退出”始终关闭程序。

设置可选择提示音、试听和测试通知。等待处理、回传和完成等新事件按任务合并提醒；通知程序使用当前包内路径。运行时显示标记为“应用内通知”的右下角提醒；Windows 通知中心可用时静默记录，不重复弹系统横幅，系统记录失败不影响应用内提示。测试通知不创建业务任务或调用模型。

程序代码 MIT。再分发前阅读 `DISTRIBUTION_TERMS.md`、`THIRD_PARTY_NOTICES.md` 和 `licenses/` 中各组件条款。

## English

This file ships with the release: `README.md` inside the portable ZIP and `docs\README.md` in the installer.

**Start it.** The Windows installer places the app in `%LOCALAPPDATA%\Programs\TaskBaton\app` and docs in `docs`, and starts from the Start menu; uninstall from Windows Settings → Apps → Installed apps. The portable ZIP runs `gui\TaskBaton.Desktop.exe` from the folder you extracted to — keep that folder where it is so the notification helper still starts. `TryDemo.cmd` opens an isolated ledger with preset samples and never calls a real Agent.

**First task.** On the Tasks page choose **New task**, then an automatic or manual transfer. An automatic transfer takes the work request and an explicit conversation; review mode lets you pick the final reviewer and either write the acceptance criteria or have the initiating Agent prepare them, with an optional round limit. The task completes when the reviewer approves with no open items. Relay sends the criteria and records the Agent's receipt; it does not verify the work itself, and real calls may incur model charges. A manual transfer copies a complete prompt for the sender, takes the sender's JSON reply back and parses it into a task, then points you to the next Agent. Several turns are fine; the task ends only when it is explicitly complete. File changes never complete a task — only a valid receipt does.

**Agents.** One name per Agent. **One-click setup** creates a separate new request and does not overwrite the selected node; use **Edit Agent** to change an existing one. Calling methods accept native JSON, JSONL and final-text output, and importing one calls no model. Manual CLI sending needs only a registered method and an enabled direction; automatic delivery also needs a passing probe (the Agent writes a marker file and you confirm the session) or one real handoff whose receipt the Agent wrote. Existing conversations must be chosen explicitly and re-checked before resuming; Relay never falls back to the latest one. Handoff rules are written only after you confirm the one-line entry, and removing them preserves your other instructions.

**Runtimes and entry points.** The installer bundles the runtime for the desktop app, `app\taskbaton.exe` and `app\mcp\taskbaton-mcp.exe`; run `.\taskbaton.exe --help` in `app`. The portable CLI/MCP package uses `dotnet taskbaton.dll` and `dotnet mcp/taskbaton-mcp.dll` and needs the .NET 10 runtime; the MCP server does not implement `--help`. The old `relay.exe`, `tb`/`relay.dll`, `mcp/relay-mcp.exe` and `relay-mcp.dll` entry points remain as compatibility shims. No target Agent, account or bridge program is included.

**Uninstall and data.** Uninstall keeps data by default — relay folder, ledger, preferences and handoff rules stay. You can instead remove only the rules Relay wrote into host instruction files, or clear the ledger and preferences for the current site, which deletes Agent configuration, task records and appearance preferences but keeps workspace files; clearing cannot be undone. Silent uninstall keeps data. Rule cleanup covers only rules registered for the current relay folder; other sites need separate handling, and deleting the install or extracted folder does not replace uninstalling or removing rules.

**Other behaviour.** Cancelling a task stops Relay's tracking and further delivery without stopping a running Agent; deleting a task removes its record and files stay until retention cleanup; manually marking complete records a user decision. Receipts live in the task's hidden `.relay` folder and are observed while the desktop app runs. Nothing runs after every process closes, so overdue work waits for the next launch or `taskbaton recover`. Moving the relay folder requires no unfinished task, copies files and the ledger before switching, keeps the old folder as a backup, and never claims a migration that was not started. The minimize button keeps the window on the taskbar; only × follows the **Minimize to tray when closing** setting, asked once and changeable in Settings. Notifications, sounds and the test notification are configured in Settings; the in-app notice is marked "in-app notification" and a silent Windows notification-center entry is recorded when available. Test notifications create no task and call no model.

**License.** The application code is MIT. Read `DISTRIBUTION_TERMS.md`, `THIRD_PARTY_NOTICES.md` and `licenses/` before redistributing the bundled components.
