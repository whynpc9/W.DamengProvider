# 实现与测试文档

当前产品项目为 `src/W.DamengProvider`，包与程序集名为 `W.DamengProvider`，公开命名空间为 `W.Dm`，当前源码现代目标框架为 .NET 6/8/9/10（0.2.0 同包四目标）。驱动负责 ADO.NET、协议、传输、事务、类型、取消、容量管理与诊断；EF 模型、SQL 翻译和迁移由独立适配项目负责，驱动生产项目不引用 EF。

源码分支另有尚未发布的受限 `netstandard2.1` 兼容原型，供 .NET Core 3.1 消费，详见[范围与验证](../compatibility/core31.md)。

## 当前实现与发布

当前版本为 [0.2.0](../release-0.2.0.md)，提供四目标、PR #17/#18 的 [issue #16 修订](../compatibility/I16-ef-integration-feedback.md)、[数组绑定与 DbBatch](../compatibility/I07-I08-array-binding-dbbatch.md)、PR #23 的[存储过程与游标](../compatibility/I09-stored-procedures.md)、PR #22 的[异步元数据](../compatibility/I10-metadata.md)。公开安装与实际发布状态见发布记录及 [progress.json](progress.json)，后续缺口见[路线图](../roadmap.md)。前一版本 [0.1.0-preview.1](../release-0.1.0-preview.1.md) 的历史验收不扩大到新版本；历史测试计数只证明各自受测版本。

下一版本 [0.3.0 正在准备](../release-0.3.0.md)，增加受限 Standard 2.1 资产，发布工作流同时检查实际 Core 3.1 包消费；该资产尚未发布，TLS/DbBatch/日期映射差异见[旧目标说明](../compatibility/core31.md)。

| 内容 | 入口 |
| --- | --- |
| 身份与基础兼容 | [基础兼容](../compatibility/R1.md) |
| 配置、单位与别名 | [连接选项](../compatibility/T04-options.md)、[兼容变化](../compatibility/T04-breaking-changes.md) |
| 会话与资源所有权 | [会话契约](../compatibility/T05-session-ownership.md) |
| 传输与安全 | [帧与期限](../compatibility/T06-transport-boundaries.md)、[TLS](../compatibility/T07-security.md) |
| 命令、Reader 与元数据 | [命令契约](../compatibility/T08-command-reader.md)、[异步发现与安全描述](../compatibility/I10-metadata.md)、[数组绑定与 DbBatch](../compatibility/I07-I08-array-binding-dbbatch.md)、[存储过程与游标](../compatibility/I09-stored-procedures.md) |
| 类型与时间精度 | [类型](../compatibility/T09-types.md)、[时间精度](../compatibility/TP-temporal-precision.md) |
| 事务、隔离级别与保存点 | [事务](../compatibility/T10-transactions.md)、[隔离级别](../compatibility/T11-isolation.md) |
| 异步与取消 | [异步契约](../compatibility/R2.md) |
| 数据源、容量调度与流式 LOB | [数据源与 LOB](../compatibility/R3.md) |
| 物理连接复用前置调查 | [issue #6 性能基线与复位阻塞（8.1.5.60 无完整复位，维持销毁）](../compatibility/I06-pool-reuse-baseline.md) |
| 服务器错误恢复 | [恢复条件与白名单](../compatibility/server-error-recovery.md) |
| Windows / win-x64 实库兼容 | [issue #11 Windows 记录](../compatibility/I11-windows.md) |
| .NET 8/9/10 多目标 | [原三目标验证记录](../compatibility/I12-multitarget.md)、[四目标发布记录](../release-0.2.0.md) |
| .NET 6 兼容层 | [数据源 API 差异、TLS 等价验证、时间 API；验证记录](../compatibility/I13-net6.md) |
| Linux 兼容性验证 | [Ubuntu WSL2 / linux-x64 离线与包消费；实库 pending](../compatibility/I11-linux-validation.md) |

## 开发与验证

[AGENTS.md](../../AGENTS.md) 和[协作约定](active-ownership.md)适用于各类开发工具、模型和 harness。[本地达梦环境](local-dameng.md)、[隔离 TLS 环境](local-dameng-tls.md)规定 TEST 身份、secret 与精确对象清理边界；权限维护记录见 [SOI 目录访问](maintenance/T12-test-catalog-access.md)和[临时 Schema 授权记录](maintenance/EF-test-create-schema-temporary.md)。

发布检查使用[发布指南](../release.md)中的 `eng/release.sh prepare`。脚本职责见 [eng 索引](../../eng/README.md)与 [TEST wrapper 索引](../../scripts/README.md)。专项入口见 `eng/T*.md` 和对应工具 README；其中固定旧包、下游 commit、manifest 或 approval 的入口只用于原样本重现，不是新任务的前置流程。没有显式测试连接时只跑离线检查，并报告 `integration_pending`。

## 历史材料

已移除重复审阅、过期窗口交接、旧进度快照和失败重试流水；原内容可从 Git 历史查询。本次清理前的文档基线为 `3091108`。

仍保留的阶段设计、协议字段说明、fixture 来源及 [temporal-native](temporal-native/README.md) 材料用于解释实现或工具输入。其中的日期、测试计数、模型名、冻结和批准状态仅属于原任务，不覆盖当前兼容说明或授予新操作权限。
