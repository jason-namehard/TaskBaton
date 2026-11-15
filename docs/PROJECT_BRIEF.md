# TaskBaton（原 Agent Relay）项目概要

状态：架构已获批准，Phase 2 真实 CLI 派发已实现。研究日期：2026-10-22。当前用法与边界见 [README](../README.md)，验收记录见 [PHASE1](PHASE1.md) 和 [PHASE2](PHASE2.md)。

## 目标与边界

TaskBaton 是 Windows 本机多 Agent 的协调与控制层。请求方负责写任务、接收方负责推理与执行；TaskBaton 负责登记身份、检查有向权限、传递 Markdown、记录任务状态、处理显式回调与恢复。

首个可用版本覆盖同一 Windows 用户下的两个已验证 CLI Agent。核心在 GUI 关闭时仍可登记、派发、确认、完成、失败、查询和清理。支持 `auto`、`confirm`、`manual`；只有当前主机、版本和目标均通过验证的能力可自动使用。缺少能力时持久保存任务并提供手工提示词，不报告投递成功。

本项目不承担模型推理、跨机云协调、分布式队列、工作树管理或代码合并。GitHub 托管代码不构成运行时云依赖。交互会话恢复是必须完成的 Phase 3 交付。

## 核心要求

| 需求 | 约定及权威文档 |
| --- | --- |
| 身份与发现 | 稳定 `agent_id`、已验证配置路径、主机版本、明确目标会话；见 [ADAPTERS](ADAPTERS.md) |
| 有向权限 | A→B 和 B→A 独立；未授权默认拒绝；Relay 路由约束不替代宿主权限 |
| 持久生命周期 | 唯一任务、事件、执行尝试；确认与完成分离；见 [PROTOCOL](PROTOCOL.md) |
| 事件驱动 | 显式 CLI/MCP 命令和回调；文件变化不触发完成；请求方拿到等待响应即结束当前轮 |
| 故障恢复 | 幂等、有限重试、截止时间、未知投递状态、审计、人工继续入口 |
| 临时文件 | 默认保留 `3 days`；每件产物记录创建及过期时间；仅清理 Relay 管理的目录 |
| 迁移 | 盘点、提案、验证、差异、备份、明确批准、切换、回滚；禁止自动执行 Agent 提交的适配器代码 |
| GUI | 紧凑桌面界面、方向图、Inbox、失败恢复与一键复制；见 [UI_SPEC](UI_SPEC.md) |

## 推荐技术路线

建议采用 `.NET 10` 单一核心库，薄 CLI、后续 stdio MCP 和 WPF GUI；用 `Microsoft.Data.Sqlite` 管理一个本地 SQLite 账本。首选通过 AMQ 公共 CLI 复用文件队列，不复制其队列，也不引入其 wake/supervision 体系。[ARCHITECTURE](ARCHITECTURE.md) 定义边界，[ROADMAP_AND_RESEARCH](ROADMAP_AND_RESEARCH.md) 提供源码依据与平台限制。

这一选择便于共用 Windows 配置、通知和 GUI 能力，代价是 .NET 运行时及 AMQ 外部二进制的分发体积；不以未经测量的内存或启动时间作为承诺。.NET 10 当前为受支持 LTS，依据 [Microsoft 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。

## 资源约束

- 默认空闲时没有 Relay 常驻进程、文件监听器或轮询；CLI 完成操作后退出。
- 单个 SQLite 文件加 Markdown 临时工作区；不引入 Redis、PostgreSQL 或 HTTP 常驻服务。
- 截止时间需要在 GUI 关闭时执行，可由用户启用 Windows Task Scheduler 单次任务；启动/登录清理亦须单独启用。未启用时，下次命令显式恢复，不宣称离线即时提醒。
- 仅自动重试已证明未提交的投递；未知是否提交时先核对证据或交由用户处理。
- Phase 1 测量 CLI 冷启动、工作集、磁盘增长和双进程并发；若不满足使用体验再调整，无证据不扩大基础设施。

## MVP 验收与当前交付

MVP 在 Phase 2 完成一次两个真实 CLI Agent 的派发—确认—执行—显式完成闭环，并覆盖失败、漏回调、重复事件和 Relay 重启。Phase 3 补齐原会话恢复；不支持时必须交付桌面通知和可复制继续提示词。具体测试见 [路线图](ROADMAP_AND_RESEARCH.md#验收计划)。

Phase 0 交付六份规划文档；Phase 1 实现 CLI 与共享核心，并验证 AMQ 原生 Windows 收发。后续按路线推进真实 Agent 自动执行与交互会话恢复；宿主配置修改仍需遵守迁移批准流程。
