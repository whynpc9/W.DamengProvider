# T10 事务契约变化

当前已发布 `0.1.0-preview.1`；本页描述产品行为及历史阶段证据，正式发布范围见[发布记录](../release-0.1.0-preview.1.md)。

本页记录本地事务行为；隔离级别与保存点的完整限制见 [隔离级别说明](T11-isolation.md)。历史验证结果不代表该发布版本已验收。

- 本地事务默认 ReadCommitted；Unspecified 也为 ReadCommitted。其他隔离级别按已验证服务器 profile 明确开放或拒绝，不静默降级。
- Active 事务的用户命令必须设置 Transaction；旧事务不能用于重新 Open 的连接。
- Commit 发送后无法确认结果会抛 `DmCommitOutcomeUnknownException`，不自动重连重放。`Outcome` 在释放后仍可读取；失败类别不包含凭据。
- 未提交事务 Dispose 尝试有限回滚，清理失败关闭捕获的原会话；关闭 socket 不等于已确认回滚。
- 保存点使用映射名称和数量上限，能力仅对已验证 profile 开放。支持的单条用户原始保存点 SQL 与 ADO API 共用保存点跟踪；复合控制、完整事务 SQL 与未验证 profile 明确拒绝。
- 已证 TABLE DDL 会使事务 CompletedExternally；不能再用旧句柄假报回滚。Active 下不确定 DDL/动态控制形态提前拒绝；DDL 本身不承诺原子回滚。
- 只含普通 DML（`INSERT`/`UPDATE`/`DELETE`/`MERGE`/`SELECT`/`NULL`）的单个 `BEGIN … END;` 块可在 Active 事务中执行；含 `DECLARE`、控制流、异常段、过程调用、动态 SQL 或事务控制的块仍提前拒绝。精确规则见 [issue #16 修订](I16-ef-integration-feedback.md#2-事务内只含-dml-的匿名块)（未随 0.1.0-preview.1 发布）。
- Valid 的变更 setter、活动事务 Clear、篡改旧 `_disposeStatus` 均不能改变真实生命周期；相关旧用法需要调整。
- 默认不参与 ambient transaction，非空 enlist 明确不支持；外部 ambient 状态不应隐式影响本地事务。
