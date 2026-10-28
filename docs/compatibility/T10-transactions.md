# 本地事务行为

本文说明当前 [0.3.1](../release-0.3.1.md) 的本地事务。隔离级别和保存点的完整限制见[隔离级别与保存点](T11-isolation.md)。

## 开始与使用事务

- 默认隔离级别为 `ReadCommitted`；`Unspecified` 也使用 `ReadCommitted`。其他隔离级别只在已验证的服务器环境中开放，不会静默降级。
- 活动事务中的用户命令必须显式设置 `Transaction`。
- 旧事务不能用于已经重新打开的连接。
- 默认不参与环境事务（ambient transaction）；非空 enlist 会明确拒绝，外部事务状态不会隐式改变本地事务。

## 提交、回滚与释放

提交请求发送后，如果无法确认结果，会抛 `DmCommitOutcomeUnknownException`。驱动不会自动重连或重放；调用方应核对业务最终状态。事务释放后仍可读取 `Outcome`，错误信息不包含凭据。

未提交事务在 `Dispose` 时会尝试有限回滚。清理失败会关闭原物理会话；关闭 socket 不等于服务器已确认回滚。

## 保存点

保存点使用映射名称并有数量上限，只在已验证环境中开放。受支持的单条直接 SQL 与 ADO.NET API 共用保存点跟踪；复合控制语句、完整事务 SQL 和未验证环境会明确拒绝。详见[隔离级别与保存点](T11-isolation.md)。

## DDL 与匿名块

已确认 TABLE DDL 会使事务进入 `CompletedExternally`，不能再用旧事务句柄假定可以回滚。活动事务中，无法确定行为的 DDL 或动态控制语句会在执行前拒绝；DDL 本身不承诺原子回滚。

活动事务可以执行只含普通 DML 的单个 `BEGIN … END;` 块。允许的内部语句为：

`INSERT`、`UPDATE`、`DELETE`、`MERGE`、`SELECT`、`NULL`。

包含 `DECLARE`、控制流、异常处理、过程调用、动态 SQL 或事务控制的块仍会在发送前拒绝。精确规则见[事务内 DML 匿名块](I16-ef-integration-feedback.md#2-事务内只含-dml-的匿名块)。
