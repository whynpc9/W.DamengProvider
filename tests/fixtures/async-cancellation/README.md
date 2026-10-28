# 异步取消与期限测试场景

`scenarios.json` 包含逻辑合成输入和行为预期，所有用例为 `not_run`。`session_id`、`session_generation`、`execution_id`、`invocation_id` 用于把操作和取消绑定到正确所有者。

事件顺序只使用单调虚拟时钟或命名 barrier，不依赖固定 sleep。文件不包含达梦请求/响应、取消 opcode、原生 ACK 或服务器停止结果。`send_attempted`、`partial_or_indeterminate`、`validated_success` 都是测试状态标记，不是网络轨迹。

## 覆盖范围

- ASY-01/02：同步方法会抛错的 async-only fake transport；任务在 barrier 前 pending，释放后完成。
- CAN-01/02：预取消零副作用；部分发送/接收后取消会中止捕获会话，不回池、重试或重放。
- CAN-03/04：ExecuteReader 返回后旧令牌失效，Read 使用新令牌；改变会话、代际、执行或调用身份的旧取消不能中止新租用。
- CAN-05：Cancel/Close 两种顺序和成功/取消竞争只释放一次传输；无活动执行时 Cancel 不操作。
- TMO-01/02：Connect 各阶段共用期限；PoolAcquire 与 Connect 不重叠；idle progress 只重置 idle 预算；每次 Read 使用新调用预算，内部 Fetch/heartbeat 不重置总期限；LOB 块共用调用预算；清理使用独立有限预算。
- ERR-01：超时和用户取消保留不同错误类别，记录首个终态原因；Commit 发送后优先 Unknown，绝不重放。

零超时规则只适用于允许零值的公开超时，内部清理始终有限。已确认 Commit ACK 后的迟到取消不会反转为未提交；未确认 ACK 的发送为 OutcomeUnknown。fixture 不声称取消已停止服务器工作，准备过程未构建、连接数据库或调用传输 API。
