# 会话与资源所有权

本页描述当前实现。异步与取消见 [R2](R2.md)，数据源和流式 LOB 见 [R3](R3.md)，事务清理见[事务说明](T10-transactions.md)。

## 调用顺序

- 同一物理连接只能有一个命令、Reader 或事务控制操作；竞争调用立即失败，不提供 MARS。
- ExecuteReader 返回的 Reader 持续占有执行资源，直到 Close/Dispose；Read 或 NextResult 返回 false 后仍须关闭。
- 同一 Reader/LOB 的公开读取不能并行，内部 Fetch/chunk 沿用执行所有权；不同连接独立。
- DmDataSource 和显式直接池化提供容量调度；当前每个租约新建物理会话、归还销毁，不复用会话。

## 关闭、取消与故障

Close/Dispose 使捕获会话的 Reader、LOB 和事务失效；重新 Open 后，旧对象的延迟清理不能关闭新会话。Command.Cancel 取消当前执行或 Reader，无活动执行时不操作；发送后取消可能使连接 Broken，不自动重连或重放。

无法确认协议同步的失败保守断开；少量服务器错误仅在严格 profile、回执及事务条件下保留会话，见[服务器错误恢复](server-error-recovery.md)。未提交事务 Dispose 尝试有限回滚，清理失败关闭原会话；关闭 socket 不等于服务端已确认回滚。

Reader 持有的 statement 由 Reader 释放；通常按 Reader → Command → Connection 释放。Command Dispose 后已有 Reader 可继续消费并关闭，新调用被拒绝。即使会话失效使 IsClosed 提前为 true，也要调用 Reader Close/Dispose 释放 lease 与 plan。

## 元数据与保留入口

Reader.GetSchemaTable/GetSchemaTableAsync 只读当前响应的本地 metadata，未知键属性返回 DBNull，不执行目录查询。同步 Connection.GetSchema 使用独立执行所有权，集合与最小权限覆盖仍有限；GetSchemaAsync 明确不支持，SchemaOnly/KeyInfo 也未开放。

ExecuteXmlReader、高级复合类型创建和返回额外 Reader 的 GetKeyCols/GetUniqueCols 等入口仍拒绝。底层消息所有权入口表见 [wire inventory](../implementation/T05-wire-inventory.md)；历史入口表解释检查范围，不能替代当前行为验证。
