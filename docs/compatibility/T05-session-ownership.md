# 连接会话与资源生命周期

本文说明当前实现中的连接占用、对象释放和故障清理。异步与取消见[异步和取消](R2.md)，数据源和流式 LOB 见[数据源与 LOB](R3.md)，事务清理见[事务说明](T10-transactions.md)。

## 调用顺序与独占使用

- 同一物理连接同一时间只能执行一个命令、Reader 或事务控制操作；竞争调用立即失败，不支持 MARS。
- `ExecuteReader` 返回的 Reader 会持续占用执行资源，直到 `Close`/`Dispose`。即使 `Read` 或 `NextResult` 已返回 `false`，仍须关闭 Reader。
- 同一 Reader 或 LOB 的公开读取不能并行；内部 Fetch 和分块读取沿用该执行的上下文。不同连接互不影响。
- `DmDataSource` 和显式直接池化会限制连接容量，但当前每次租用都新建物理会话，归还时销毁，不复用会话。

## 关闭、取消与故障

关闭或释放连接后，属于该会话的 Reader、LOB 和事务会失效。重新打开连接后，旧对象的延迟清理不会关闭新会话。

`Command.Cancel` 取消当前执行或 Reader；没有活动执行时不做任何操作。发送后取消可能使连接进入 `Broken`，驱动不会自动重连或重放请求。

无法确认协议是否同步时，驱动会保守断开。只有满足严格服务器环境、错误回执和事务条件的少量服务器错误可以保留会话，详见[服务器错误恢复](server-error-recovery.md)。

未提交事务在释放时会尝试有限回滚；清理失败会关闭原物理会话。关闭 socket 不等于服务器已确认回滚。

## 推荐释放顺序

通常按 **Reader → Command → Connection** 的顺序释放：

- Reader 持有的语句由 Reader 释放。
- Command 释放后，已有 Reader 仍可继续读取并关闭，但不能执行新的 Command 调用。
- 即使会话失效使 `IsClosed` 提前变为 `true`，也要调用 Reader 的 `Close`/`Dispose`，释放连接租用和命令资源。

## 元数据与未开放入口

`Reader.GetSchemaTable` / `GetSchemaTableAsync` 只读取当前响应及预取的本地元数据，未知键属性返回 `DBNull`，不会执行额外目录查询。

`Connection.GetSchema` / `GetSchemaAsync` 使用独立的元数据执行上下文；支持的集合和权限限制见[元数据说明](I10-metadata.md)。单条 SELECT 的 `SchemaOnly` / `KeyInfo` 行为也在该文档中说明。

`ExecuteXmlReader`、高级复合类型创建，以及会返回额外 Reader 的 `GetKeyCols` / `GetUniqueCols` 等旧入口仍拒绝。底层消息入口的历史检查范围见[协议入口清单](../implementation/T05-wire-inventory.md)，不能替代当前行为说明。
