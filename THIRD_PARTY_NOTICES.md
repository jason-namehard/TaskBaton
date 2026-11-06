# 第三方来源与许可

Agent Relay 的应用代码按本仓库 MIT License 提供。研究对象不是本项目源码的授权来源；没有把 AMQ、Multiagent Handoff、Agents Handoff、AgentBus、ACP Router 或 CC Switch 的应用源码复制进本仓库。设计依据及固定研究提交见 [研究文档](docs/ROADMAP_AND_RESEARCH.md)。

## 分发依赖

Windows 安装器使用 [Inno Setup](https://github.com/jrsoftware/issrc) 构建；`scripts/installer-languages/` 中的简体和繁体中文翻译来自其官方源代码仓库，保留原文件中的译者署名。Inno Setup 的版权和许可见 `licenses/InnoSetup-license.txt`。

实际版本以各项目 `packages.lock.json` 为准。恢复后运行 `pwsh ./scripts/check-licenses.ps1`，读取 NuGet 包自身的 `.nuspec`，检查全部直接和传递依赖的许可声明；不是仅检查顶层包。

| 组件 | 版本 | 许可与版权 | 用途 |
| --- | --- | --- | --- |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.13 | MIT；.NET Foundation and Contributors | SQLite 驱动 |
| System.CommandLine | 2.0.13 | MIT；.NET Foundation and Contributors | 命令行解析及帮助 |
| SQLitePCLRaw.bundle_e_sqlite3 / core / lib.e_sqlite3 / provider.e_sqlite3 | 2.1.13 | Apache-2.0；Copyright 2014–2024 SourceGear, LLC | SQLite 原生桥接及二进制 |
| SQLite | 由 lib.e_sqlite3 包携带 | [上游 public domain 声明](https://www.sqlite.org/copyright.html) | 嵌入式数据库引擎 |
| ModelContextProtocol / ModelContextProtocol.Core | 2.3.0 | 包声明 Apache-2.0；Model Context Protocol a Series of LF Projects, LLC，许可转换中的 MIT 内容另见原文 | stdio MCP 协议、协商、工具与客户端测试 |
| Microsoft.Extensions.Hosting 及实际传递依赖 | 10.0.13；完整版本见 lockfile | MIT；.NET Foundation and Contributors | MCP 服务生命周期、依赖注入及 stderr 日志 |
| Tomlyn | 2.10.1 | BSD-2-Clause；Copyright 2019–2026 Alexandre Mutel | TOML 读取与语法校验 |
| [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) | 0.8.8 | MIT；Copyright (c) 2021 Oleg Stepanischev，许可见 `licenses/ZstdSharp-MIT.txt` | 读取 DSH 的 Zstandard 会话日志，核对探针图片附件的引用 |

许可全文位于 `licenses/`。Microsoft.Data.Sqlite 来源为 [dotnet/efcore](https://github.com/dotnet/efcore)，System.CommandLine 来源为 [dotnet/command-line-api](https://github.com/dotnet/command-line-api)，SQLitePCLRaw 来源为 [ericsink/SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw)。许可证适用于各自组件，不因本项目 MIT 声明而被替换。打包脚本将本文件、项目 LICENSE 和依赖许可全文一起复制到分发目录。

MCP SDK 使用固定 NuGet 修订 [9a327829](https://github.com/modelcontextprotocol/csharp-sdk/tree/9a327829ef066c44a0d737c58bf3c7617d7c701c)，原始 `LICENSE` 和 `THIRD-PARTY-NOTICES.txt` 保留为 `licenses/MCP-SDK-Apache-2.0.txt`、`licenses/MCP-SDK-THIRD-PARTY-NOTICES.txt`，包括原有 MIT 内容及授权转换说明。Tomlyn 使用固定修订 [6cc82dd2](https://github.com/xoofx/Tomlyn/tree/6cc82dd23a2c0aefb858f92a51c1b184e2812103)，原始许可保留为 `licenses/Tomlyn-BSD-2-Clause.txt`。通过包引用使用这些组件，没有复制其应用示例作为本项目业务实现。

## 开发依赖

MSTest.TestFramework / MSTest.TestAdapter 4.0.2、Microsoft.NET.Test.Sdk 18.0.1 用于构建和测试，不随 CLI 应用分发。其传递依赖由许可检查脚本一并列出；本次恢复的全部开发包声明为 MIT 或 Apache-2.0。具体版本和内容记录在测试项目的 lockfile。

.NET SDK 和运行时遵循 [.NET 许可](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) 及其第三方声明。CLI/MCP 为 framework-dependent；WPF GUI 与 Windows 通知程序为 self-contained，打包脚本把实际 .NET Core / Windows Desktop runtime pack 的许可和声明复制到发布包 `licenses/dotnet-runtime/`。GUI 复用 WPF 平台控件与 Win32 消息，不增加第三方 UI 或图形框架。

## 可选 Windows 通知组件

Windows 通知程序使用 [Windows App SDK .NET API](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)，没有复制上游应用源码。使用微软 NuGet 提供的 Foundation `1.8.260803002` 和 Runtime `1.8.260921001`；传递组件包括 Base `1.8.251216001`、InteractiveExperiences `1.8.260708001`，构建工具为 Windows SDK BuildTools `10.0.26100.4654` 与 BuildTools.MSIX `1.7.20250829.1`。

这些 NuGet 二进制/工具使用各自微软软件许可条款，不能因上游仓库源码采用 MIT 就把已分发二进制标为 MIT。原始许可与 NOTICE 保存在 `licenses/nuget/<package>/<version>/`。Windows App SDK 原始条款第 3 节允许随应用分发包放置到输出目录的文件，并规定分发条件；使用和再分发 Windows 二进制须遵守 [分发条款](DISTRIBUTION_TERMS.md) 与原文。BuildTools 仅用于构建，不把 SDK 构建工具放入应用发布目录；其许可来源为包元数据中的 [Windows SDK License](https://aka.ms/WinSDKLicenseURL)。

`check-licenses.ps1 -IncludeDesktop` 额外核对这些包的固定版本及随包许可文本与仓库保留文本一致；未登记的文件许可或版本变化要求重新审阅。微软组件没有被重新许可为本项目代码。

## 外部 AMQ

AMQ 作为用户指定的独立可执行文件，通过公开 CLI 调用，不随本项目发布包捆绑。测试基线为 [AMQ 0.104.0](https://github.com/avivsinai/agent-message-queue/releases/tag/v0.104.0)，上游 MIT，Copyright (c) 2025 Aviv Sinai。若今后捆绑 AMQ，应保留其 LICENSE 并核对其二进制包含的第三方组件声明；当前不把外部 AMQ 的许可误计为本项目独立实现的版权。

新贡献不得复制无明确许可的源码。引入或改写上游代码时，应在变更中记录来源、版本、许可和修改内容，并保留所需声明。仅通过改名或改写语句不能消除原代码的许可义务。

## 外部 Codex CLI

Phase 2 调用用户指定的独立 Codex CLI，通过官方 `exec`、结构化输出和只读沙箱接口集成，不捆绑 CLI、模型权重或账户凭据。Codex 的授权及依赖声明以 [openai/codex](https://github.com/openai/codex) 的对应分发版本为准；本项目没有复制其应用源码。真实 Agent 验证脚本只在开发者显式运行时使用本机已有认证，公共 CI 不读取认证文件或调用模型。
