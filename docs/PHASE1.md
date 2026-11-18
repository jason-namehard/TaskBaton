# Phase 1 — CLI Core

日期：2026-10-22。平台：Windows 11，系统版本 `10.0.22631`；.NET SDK `10.0.402`；AMQ `0.104.0`。

## 交付

共享 `TaskBaton.Core` 与薄 `TaskBaton.Cli`。CLI 实现注册、路径发现/修正、方向权限、设置、适配器验证、派发/批准、确认/开始、成功/失败、产物导入、取消、明确批准的重试、只读查询、审计、持久 Inbox、继续提示词、超时恢复和 TTL 清理。

SQLite 事务保存任务、幂等回调和事件；外部调用在事务之外执行。每个任务有持久运输 outbox、尝试 ID、累计提交次数和 in-flight 租期。未知投递停止重发；执行中断不会因为 CLI 重启重置次数。任务完成与通知同事务保存。

默认没有 Relay 常驻进程、watcher、周期查询或计划任务。当前 `deadline_delivery=on_next_command`；用户调用 `recover` 才持久处理超时。GUI 关闭不影响 CLI 的任务账本或清理能力。

## 验证证据

- `dotnet restore --locked-mode`：通过，锁定依赖可恢复。
- Release 构建和 MSTest：23 项通过；覆盖 124 次重复派发/查询/完成、方向权限、错误发送方、显式失败、超时/晚到结果、新尝试与旧回调、投递未知、三次重试上限、运输中断恢复、并发领取、确认模式、路径失效、TTL 与孤立临时文件、取消后晚到证据、快速完成先于运输返回以及 CLI 输入/退出码。
- 原生 Windows AMQ `init → send → drain`：通过；两个独立 AMQ 进程并发 drain，同一消息仅一个进程取到。使用独立验证队列，未启动 AI 或插件。
- 分进程 CLI 冒烟：真实 AMQ 自动入队后，显式 `ack`、写结果、`complete`，再用独立进程查询为 `succeeded`；运输凭据保存在 outbox。
- NuGet 许可检查：运行和测试项目的全部直接/传递包均为 MIT 或 Apache-2.0；发布目录包含项目许可和依赖许可全文。

GitHub Actions 配置 Windows 与 Ubuntu 的锁定恢复、测试、许可检查和打包。上列运行证据来自本机 Windows，不把尚未完成的远程 CI 当作本机验证。

## 后续交付边界

Phase 2 需要启动两个已验证真实 AI CLI Agent，完成自动取件、执行和回调；当前 AMQ `dispatched` 仅代表队列已接受。

Phase 3 验证精确 session 恢复和 Windows 本地通知。Task Scheduler 可选安装界面需展示将运行的命令、当前用户身份、单次 deadline 或登录清理触发器、数据目录、卸载入口，再经明确启用。当前未创建系统任务，不能承诺所有进程退出后实时提醒。

Phase 4 增加 MCP、通信迁移和备份回滚；Phase 5 实现 GUI。当前许可材料覆盖已分发依赖；未来增加源码或二进制仍需检查其具体来源和许可声明。
