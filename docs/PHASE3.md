# Phase 3 — Exact Session Continuation

日期：2026-10-22。验证平台为 Windows 11 `10.0.22631`、.NET SDK `10.0.402`、Codex CLI `0.163.0-alpha.11`、AMQ `0.104.0`。

## 交付能力

完成和失败事务同时保存结果、Inbox 与继续 outbox。事务提交后，CLI 或单次 worker 处理继续消息；提交前中断的记录由 `recover` 接管。每个 task/attempt/event 只有一个领取者，读取状态不会启动模型。提交期间中断或超时成为 `unknown`，不会自动再唤醒。

`codex-queue` 通过原生 `queue --thread <UUID>` 把继续提示词交给宿主，回执仅记为 `submitted`。`codex-exec-resume` 显式选择后，通过只读 `exec resume <UUID>` 在原会话运行一次有界轮次；只有 CLI 返回相同 UUID、`turn.completed` 与非空最终回复才记为 `completed`。它不改变 B 的成功/失败记录，也不证明原始项目的所有工作均已完成。

原始绑定包括 Agent、宿主、原生 CLI、版本、配置路径、会话 UUID 和 cwd。`config_profile` 在此阶段表示登记的 `config.toml` 绝对路径，暂不解释为 Codex 命名 profile。派发保存恢复适配器快照；改登记不会把旧任务转向新宿主。继续前检查对应配置目录内唯一的 session 元数据与 cwd，不使用最近会话、窗口标题、会话名称或文件修改时间选择目标。

没有绑定、不支持、会话丢失、配置改变、结果过期或恢复失败时，结果仍保留在 Inbox。Windows 辅助程序使用本地 AppNotificationManager，通知可打开提示词窗口，按钮可复制继续提示词。通知绑定原 task 和 attempt，旧尝试的通知不能复制新尝试的提示词。`prompt open/copy` 也可直接使用。

## 使用

先构建 CLI；Windows 再构建独立辅助程序。核心解决方案继续支持 Windows 与 Ubuntu，通知程序仅在 Windows 构建。

```powershell
dotnet restore --locked-mode
dotnet restore src/TaskBaton.Notify/TaskBaton.Notify.csproj --locked-mode
dotnet test -c Release --no-restore
pwsh ./scripts/check-licenses.ps1 -IncludeDesktop
pwsh ./scripts/publish.ps1 -IncludeDesktop

$relay = './artifacts/taskbaton/taskbaton.dll'
$dataRoot = Join-Path $PWD '.relay'
dotnet $relay --home $dataRoot init
dotnet $relay --home $dataRoot desktop configure --executable ./artifacts/taskbaton/desktop/TaskBaton.Notify.exe
```

CLI 需要 .NET 10 runtime；Windows x64 通知程序自带 .NET 与 Windows App SDK 运行库。首次发送通知时由 App SDK 注册该可执行文件的当前用户 COM 激活入口和应用标识；不注册系统计划任务、服务或 Agent 插件。请保留发布目录位置；移动或删除前在原位置运行 `TaskBaton.Notify.exe --unregister`，移动后在新位置重新登记通知程序。`desktop disable` 停止该数据目录的后续提醒。

为请求方登记精确会话恢复方式，之前需按 README 注册并 validate A 的配置和 CLI 路径：

```powershell
dotnet $relay --home $dataRoot agent verify-resumer --id agent-a --kind codex-queue
```

若 A 是已结束的独立 CLI 会话，并且希望后台继续一轮，显式改用 `--kind codex-exec-resume`。该模式会消耗模型额度，使用注册目录的登录态，以 `--ignore-user-config --sandbox read-only` 调用；不会继承 config.toml 的自定义模型、provider 或 MCP 设置。正在其他宿主中写入的会话可能拒绝恢复，此时查看证据并手工继续。两种模式之间不自动切换。

准备 `origin.json`，只填写已确认的原会话身份：

```json
{
  "host": "codex",
  "session_id": "00000000-0000-0000-0000-000000000001",
  "config_profile": "C:/Users/you/.codex/config.toml",
  "cwd": "C:/work/project"
}
```

派发时添加 `--origin-session ./origin.json`。A 收到等待响应即结束当前轮；B 显式完成/失败后，单次 worker 处理继续 outbox。未提供 origin 或未验证恢复适配器时使用手工提示词与已配置的桌面通知。

```powershell
dotnet $relay --home $dataRoot task continuations --task $task.task_id
dotnet $relay --home $dataRoot task continue --task $task.task_id --timeout-seconds 300
dotnet $relay --home $dataRoot recover --task $task.task_id
dotnet $relay --home $dataRoot prompt open --task $task.task_id
dotnet $relay --home $dataRoot prompt copy --task $task.task_id
```

`task continue` 只处理尚未提交的记录，不重发 `submitted/completed/unknown/failed`。默认每次会话调用最多 180 秒；显式 `task continue` 可设 1–1800 秒。恢复 lease 留有 45 秒用于版本检查和提交结果。任务继续状态与桌面通知状态分列，通知不可用不会覆盖任务完成状态。

## 已核实的本机结果

| 场景 | 结果 |
| --- | --- |
| A→B→原 A 的真实 CLI 闭环 | B 返回 `54` 与任务标记；恢复 A 的同一 UUID，A 读取 B 结果并报告只出现在旧上下文里的原任务码 |
| 后台派发 | 约 `0.57 s` 返回；A 不循环查询 B |
| 重复继续 | 再次运行 `task continue`，只有一次 `session.resume_claimed` |
| 原生 queue | 独立 app-server 持有测试会话时，原生 CLI 返回精确 UUID 的 `Queued message` 回执；记为 `submitted`，未据此声称模型轮次完成 |
| Windows 本地通知 | 系统接受通知 ID，发送进程随后退出 |
| 发送程序退出后的激活 | 通过 Windows 注册的 COM 激活接口启动新进程，传递复制动作，剪贴板内容匹配 task；打开提示词窗口，关闭后进程退出 |
| 无绑定或恢复失败 | 保留结果、Inbox 与复制提示词；桌面状态独立保存 |

真实闭环任务 `d544ba6e-c248-4089-af5c-bb6610c39a8e`，A 会话 `01a163da-e003-7b71-82c0-9715b333fdd6`，B 会话 `01a163dc-b0a4-7732-9228-e81578da773e`。验证原始日志保存在开发者独立工作目录，不加入仓库或发布包。

可复现 CLI 闭环：

```powershell
pwsh ./scripts/verify-real-agents.ps1 -Dotnet dotnet -Codex C:/tools/codex.exe -Amq C:/tools/amq/amq.exe -ConfigPath C:/Users/you/.codex/config.toml -ResumeOrigin
```

普通 CI 不使用模型额度。当前 59 项自动回归覆盖原有任务协议、124 次继续去重、并发领取、恢复崩溃、通知崩溃、旧尝试、取消、过期产物、错误配置/cwd/UUID、队列回执及继续轮次证据。

Windows 桌面验证需交互登录，发送一条测试通知，暂时替换并恢复剪贴板，不调用模型：

```powershell
pwsh -NoProfile -STA -File ./scripts/verify-desktop.ps1 -Dotnet dotnet
```

## 已知边界

Windows `submitted` 表示系统接受通知，不保证横幅可见；免打扰、通知设置、锁屏和管理员进程会限制提醒。冷启动验证覆盖 Windows COM 激活与真实复制操作，未自动操作系统通知中心中的横幅。Ubuntu 保留 Inbox 与文本提示词。

关闭所有进程后，deadline 仍在下一次 `recover` 处理；清理产生的继续记录也在后续 `recover` 处理。未注册 Task Scheduler，不能承诺离线即时提醒。GUI 管理面板、MCP 和适配器迁移属于后续阶段。

仅验证当前 Codex 版本与本地 session 格式。压缩、归档或格式无法识别的 session 转为手工恢复；不复制或修改宿主 session 文件，不替换原会话。队列处理时机由 Codex 宿主管理，不能从成功回执推断 active writer 或执行进度。

## 来源与许可

复用项目已有 SQLite、子进程、产物与回调实现；会话接口依据 [OpenAI 官方非交互文档](https://developers.openai.com/codex/noninteractive) 和本机 CLI 帮助/运行结果。未复制 Codex 或 AMQ 应用源码。

通知复用 [微软 .NET 通知接口](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)、[自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps) 与 [Windows 激活接口](https://learn.microsoft.com/en-us/windows/win32/api/notificationactivationcallback/nn-notificationactivationcallback-inotificationactivationcallback)。应用代码为 MIT；Windows App SDK/SDK 的二进制与构建工具按各自微软条款提供，原始许可和第三方声明保存在 `licenses/nuget/`，详见 [第三方声明](../THIRD_PARTY_NOTICES.md)。
