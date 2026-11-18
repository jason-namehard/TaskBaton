# Contributing to TaskBaton (formerly Agent Relay)

欢迎提交可复现的缺陷、宿主接入证据和范围明确的改进。缺陷报告请包含版本、平台、复现步骤、预期与实际行为，以及脱敏后的错误码。演示模式可用于不含认证的复现。

## 本地开发

使用 .NET 10 SDK。Windows GUI/通知项目单独构建，CLI、MCP 与核心 solution 可在 Windows/Linux 验证：

```powershell
dotnet restore --locked-mode
dotnet test -c Release --no-restore
pwsh ./scripts/check-licenses.ps1
pwsh ./scripts/package.ps1 -Flavor Portable
```

Windows 额外执行：

```powershell
dotnet restore src/TaskBaton.Notify/TaskBaton.Notify.csproj --locked-mode
pwsh ./scripts/test-desktop.ps1
pwsh ./scripts/check-licenses.ps1 -IncludeDesktop
pwsh ./scripts/package.ps1 -Flavor Windows
```

确定性测试不调用模型。`verify-real-agents.ps1` 需要已有登录态并消耗模型额度，应由开发者明确运行，不能放入普通 CI。不要提交认证、真实配置、账本、迁移备份或包含私人任务的日志。

## 变更约定

业务契约放在共享 Core，CLI/MCP/GUI 仅处理各自输入输出。保留方向权限、显式完成、幂等、未知投递停止重发，以及配置先预览再批准的边界。修复需有针对真实失败的回归验证；文件名和代码标识符用英文。

协议改动需要附带一致性用例（conformance case）：修改 [`protocol/HANDOFF.md`](protocol/HANDOFF.md)、schema 或校验行为时，在 `protocol/conformance/` 增加或更新可执行样本，并在 `protocol/conformance/manifest.json` 登记。可移植交接契约以 `protocol/HANDOFF.md` 为准；本机账本与状态机见 [`docs/PROTOCOL.md`](docs/PROTOCOL.md)。

`README.md` 是英文主文件，`README.zh.md` 是独立简体中文版，两者顶部互相链接。修改安装步骤、能力或限制时同步两份 README；正文各自保持单一语言，命令、参数与 JSON 字段保留原样。现有深层文档和本贡献指南以中文为主，沿用所在文件的语言；新文档按读者选择语言并提供入口链接，不要求中英逐段混排。

贡献应为可合法授权的原创代码，或明确记录允许复用的来源、修订与许可，并保留版权及必要声明。仅改名、换写法不能替代许可。新依赖需更新 lockfile、第三方说明和相应许可全文。

## 发布

版本统一在 `Directory.Build.props`。更新 CHANGELOG 和 `docs/releases/<tag>.md` 后，CI 验证 Windows/Linux 并生成两种分发包。通过后推送匹配版本的 `v...` 标签，标签工作流在测试、许可检查和包检查通过后发布预览 Release，见 [发布流程](docs/RELEASING.md)。
