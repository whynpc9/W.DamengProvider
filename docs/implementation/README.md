# 实现与测试文档

当前产品项目为 `src/W.DamengProvider`，包与程序集名为 `W.DamengProvider`，公开命名空间为 `W.Dm`，目标框架为 .NET 10。驱动负责 ADO.NET、协议、传输、事务、类型、取消、容量管理与诊断；EF 模型、SQL 翻译和迁移由独立适配项目负责，驱动生产项目不引用 EF。

## 当前实现与发布

仓库的已发布记录为 [0.1.0-preview.1](../release-0.1.0-preview.1.md)。源码还包含 PR #17 合入的 [issue #16 修订](../compatibility/I16-ef-integration-feedback.md)，这些改动未包含在该包中。机器可读发布状态见 [progress.json](progress.json)，未实现的扩展见[路线图](../roadmap.md)。历史测试计数只证明各自受测版本，不作为当前源码的新验收结果。

| 内容 | 入口 |
| --- | --- |
| 身份与基础兼容 | [基础兼容](../compatibility/R1.md) |
| 配置、单位与别名 | [连接选项](../compatibility/T04-options.md)、[兼容变化](../compatibility/T04-breaking-changes.md) |
| 会话与资源所有权 | [会话契约](../compatibility/T05-session-ownership.md) |
| 传输与安全 | [帧与期限](../compatibility/T06-transport-boundaries.md)、[TLS](../compatibility/T07-security.md) |
| 命令、Reader 与元数据 | [命令契约](../compatibility/T08-command-reader.md) |
| 类型与时间精度 | [类型](../compatibility/T09-types.md)、[时间精度](../compatibility/TP-temporal-precision.md) |
| 事务、隔离级别与保存点 | [事务](../compatibility/T10-transactions.md)、[隔离级别](../compatibility/T11-isolation.md) |
| 异步与取消 | [异步契约](../compatibility/R2.md) |
| 数据源、容量调度与流式 LOB | [数据源与 LOB](../compatibility/R3.md) |
| 服务器错误恢复 | [恢复条件与白名单](../compatibility/server-error-recovery.md) |
| Windows / win-x64 实库兼容 | [issue #11 Windows 记录](../compatibility/I11-windows.md) |

## 开发与验证

[AGENTS.md](../../AGENTS.md) 和[协作约定](active-ownership.md)适用于各类开发工具、模型和 harness。[本地达梦环境](local-dameng.md)、[隔离 TLS 环境](local-dameng-tls.md)规定 TEST 身份、secret 与精确对象清理边界；权限维护记录见 [SOI 目录访问](maintenance/T12-test-catalog-access.md)和[临时 Schema 授权记录](maintenance/EF-test-create-schema-temporary.md)。

发布检查使用[发布指南](../release.md)中的 `eng/release.sh prepare`。专项入口见 `eng/T*.md` 和对应工具 README；其中固定旧包、下游 commit、manifest 或 approval 的入口只用于原样本重现，不是新任务的前置流程。没有显式测试连接时只跑离线检查，并报告 `integration_pending`。

## 历史材料

已移除重复审阅、过期窗口交接、旧进度快照和失败重试流水；原内容可从 Git 历史查询。本次清理前的文档基线为 `3091108`。

仍保留的阶段设计、协议字段说明、fixture 来源及 [temporal-native](temporal-native/README.md) 材料用于解释实现或工具输入。其中的日期、测试计数、模型名、冻结和批准状态仅属于原任务，不覆盖当前兼容说明或授予新操作权限。
