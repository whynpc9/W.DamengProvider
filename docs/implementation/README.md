# 文档导航

本页区分当前使用说明、技术参考、开发入口和历史记录。当前版本为 [0.3.1](../release-0.3.1.md)。

产品项目位于 `src/W.DamengProvider`，包与程序集名为 `W.DamengProvider`，公开命名空间为 `W.Dm`。驱动支持 .NET 6/8/9/10，并提供受限的 `netstandard2.1` 目标框架。EF 模型、SQL 翻译和迁移由独立适配项目负责，驱动本身不引用 EF。

## 使用者入口

- [项目 README](../../README.md)：安装、快速开始、官方驱动对比和主要限制。
- [0.3.1 发布记录](../release-0.3.1.md)：本版修复、升级说明和发布验证。
- [路线图与支持缺口](../roadmap.md)：功能限制、待补验证和后续方向。
- [旧目标框架说明](../compatibility/core31.md)：.NET Core 3.1 / Standard 2.1 的支持范围。

## 当前技术参考

| 主题 | 文档 |
| --- | --- |
| 包身份与基础兼容 | [基础兼容](../compatibility/R1.md) |
| 连接串、单位与旧键迁移 | [连接选项](../compatibility/T04-options.md)、[兼容变化](../compatibility/T04-breaking-changes.md) |
| 连接、命令与 Reader 生命周期 | [会话与资源管理](../compatibility/T05-session-ownership.md) |
| 网络传输、超时与 TLS | [传输边界](../compatibility/T06-transport-boundaries.md)、[TLS 安全](../compatibility/T07-security.md) |
| Command 与 Reader | [命令和读取行为](../compatibility/T08-command-reader.md) |
| 批量执行 | [数组绑定与 DbBatch](../compatibility/I07-I08-array-binding-dbbatch.md) |
| 存储过程与输出游标 | [存储过程说明](../compatibility/I09-stored-procedures.md) |
| 元数据 | [异步元数据与 SchemaOnly/KeyInfo](../compatibility/I10-metadata.md) |
| 数据类型与时间精度 | [类型说明](../compatibility/T09-types.md)、[时间精度](../compatibility/TP-temporal-precision.md) |
| 事务、隔离级别与保存点 | [事务](../compatibility/T10-transactions.md)、[隔离级别](../compatibility/T11-isolation.md) |
| 异步与取消 | [异步和取消](../compatibility/R2.md) |
| 数据源、连接容量与流式 LOB | [数据源与 LOB](../compatibility/R3.md) |
| 服务器错误后的连接恢复 | [错误恢复](../compatibility/server-error-recovery.md) |
| 平台与目标框架验证 | [Windows](../compatibility/I11-windows.md)、[Linux](../compatibility/I11-linux-validation.md)、[多目标框架](../compatibility/I12-multitarget.md)、[.NET 6](../compatibility/I13-net6.md) |
| 物理连接复用调查 | [性能基线与会话重置限制](../compatibility/I06-pool-reuse-baseline.md) |

“已验证环境”指文档列出的达梦版本、协议、编码和 TLS 配置组合。一个环境中的结果不会自动扩展到其他组合。

## 开发与发布

- [发布指南](../release.md)：版本、发布准备、实库检查和 tag 流程。
- [构建与验证入口](../../eng/README.md)：发布脚本和历史专项脚本的区别。
- [本地 TEST 配置入口](../../scripts/README.md)：测试命令如何加载本机连接配置。
- [本地达梦环境](local-dameng.md)与[隔离 TLS 环境](local-dameng-tls.md)：测试账号、证书和清理边界。
- [协作约定](active-ownership.md)：共享工作区中的修改与验证方式。
- [仓库规则](../../AGENTS.md)：构建参数、数据库权限和安全要求。

没有显式测试连接配置时，只运行离线检查，并将实库部分记为 `integration_pending`。这表示“尚未完成实库验证”，不是测试通过。

## 历史设计与验证记录

[temporal-native](temporal-native/README.md)、`T*.md`、`reviews/`、各兼容性验证记录和旧版本发布记录用于解释实现来源或复现特定历史结果。

这些文档中的提交、测试数量、任务状态、审批和冻结信息只属于当时版本。当前能力以本页的“当前技术参考”和最新发布记录为准；历史记录不会授予额外的数据库或发布权限。

重复审阅、过期交接和旧进度快照已清理，清理前的文档基线为 `3091108`，需要时可通过 Git 历史查阅。
