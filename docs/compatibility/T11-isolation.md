# T11 隔离级别与保存点 SQL

当前已发布 `0.1.0-preview.1`；本页描述产品行为及历史阶段证据，正式发布范围见[发布记录](../release-0.1.0-preview.1.md)。

本页描述隔离级别与保存点行为。当前发布状态见 [progress.json](../implementation/progress.json)；历史 T11 结果不能作为当前候选通过的证明。

| 级别 | 公开行为 |
| --- | --- |
| Unspecified | 使用 ReadCommitted |
| ReadCommitted | 维持既有默认行为 |
| ReadUncommitted / Serializable | 仅服务器自报版本精确为 `8.1.5.60` 时允许 |
| RepeatableRead / Snapshot / Chaos / 未知值 | 明确抛 NotSupportedException，不静默降级 |

切换隔离级别使用独立控制 statement。事务配置以本次已验证 SET 响应及当前操作身份确认，不要求连接默认隔离缓存随事务设置改变。该 statement 有界关闭，用户命令使用自己的 statement，不继承空 SQL owner。

活动事务内的用户 Command 必须显式绑定对应 Transaction。已验证的 EF 单条 `ROLLBACK TO "name"` 与带 `SAVEPOINT` 关键字形式均可使用；名称支持双引号转义。包含额外语句、缺失名称或完整事务 ROLLBACK 的 SQL 仍拒绝，应使用标准事务 API。SQL 原文不被驱动改写。

上述能力仅对相应服务器 profile 开放，不表示所有达梦版本或 EF 版本兼容。异步事务与保存点入口已实现，见 [R2](R2.md)；具体测试结果仍归属各自受测版本。源码提供的 ServerCapabilities 快照见 [issue #16 修订](I16-ef-integration-feedback.md)，该 API 未随 0.1.0-preview.1 发布。
