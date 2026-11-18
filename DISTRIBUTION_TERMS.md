# 二进制分发条款

TaskBaton 应用源码按 [MIT License](LICENSE) 提供。MIT 许可只覆盖本项目有权按 MIT 授权的代码，各依赖保留自己的许可。

可选 Windows 通知程序的发布目录包含微软 Windows App SDK 和 .NET 运行库。使用、复制或再分发该二进制前，须接受并遵守所附微软组件条款及第三方许可；不接受时请勿使用或分发该辅助程序。Windows App SDK 的组件以随附许可允许的应用组件形式提供，不作为独立 SDK 产品再分发。微软条款对相应微软组件和微软的保护不因本项目 MIT 声明而减损。

Windows App SDK 分发条款原文见 `licenses/nuget/microsoft.windowsappsdk.runtime/1.8.260921001/license.txt`，第三方声明见同目录 `NOTICE.txt`。其他构建组件的原始条款在 `licenses/nuget/`。自包含 Windows 发布包的 .NET 运行库许可和第三方声明由打包脚本从实际使用的 runtime pack 复制到 `licenses/dotnet-runtime/`。

WPF 管理界面 `gui/TaskBaton.Desktop.exe` 也附带 .NET 运行库，运行库原始许可与第三方声明同样保存在 `licenses/dotnet-runtime/`。它不依赖 Windows App SDK 通知组件。CLI 可独立于 Windows 组件使用。全部组件来源、版本及用途见 [第三方声明](THIRD_PARTY_NOTICES.md)。
