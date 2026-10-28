# 隔离级别与保存点 SQL

本文说明当前 [0.3.1](../release-0.3.1.md) 的事务隔离级别和直接保存点 SQL。

## 隔离级别

| 级别 | 当前行为 |
| --- | --- |
| `Unspecified` | 使用 `ReadCommitted`。 |
| `ReadCommitted` | 默认行为。 |
| `ReadUncommitted` / `Serializable` | 仅服务器自报版本精确为 `8.1.5.60` 时允许。 |
| `RepeatableRead` / `Snapshot` / `Chaos` / 未知值 | 抛 `NotSupportedException`，不静默降级。 |

切换隔离级别使用独立的控制语句。事务配置以本次已验证的 SET 响应和当前操作身份为准，不要求连接默认隔离级别缓存随事务设置改变。控制语句会有界关闭，用户命令使用自己的语句句柄。

## 活动事务中的命令与保存点

活动事务内的用户 Command 必须显式绑定对应的 Transaction。

在已验证环境中，EF 常用的单条 `ROLLBACK TO "name"` 和带 `SAVEPOINT` 关键字的形式均可使用，名称支持双引号转义。SQL 原文不会被驱动改写。

以下 SQL 仍会拒绝：

- 包含额外语句的复合 SQL。
- 缺少保存点名称。
- 完整事务 `ROLLBACK`，应改用标准事务 API。
- 未验证服务器环境中的保存点语句。

异步事务和保存点入口见[异步和取消](R2.md)。打开连接后，可通过 `DmConnection.ServerCapabilities` 读取当前服务器能力快照；该属性说明见[服务器能力](I16-ef-integration-feedback.md)。
