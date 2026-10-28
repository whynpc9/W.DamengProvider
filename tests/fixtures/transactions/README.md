# 事务与隔离级别测试数据

`scenarios.json` 分为合成 `cases` 和 `real_database_probes`。合成用例使用 `execution_status: not_run`，实库过程使用 `planned_not_run`；两者都不是执行结果。当前行为见[事务说明](../../../docs/compatibility/T10-transactions.md)和[事务设计](../../../docs/implementation/T10-design.md)。

## 数据边界

事务状态、发送标记、ACK、保存点映射和隔离观察都是测试宿主输入，不包含达梦报文、opcode、服务器轨迹或真实事务结果。`validated_success` 也是合成状态，不是抓包。

ReadUncommitted、Serializable、保存点、Release 和 DDL 事务边界是否可用，必须由已识别的服务器环境决定。依赖服务器语义的字段保留为空，由实际探针填写。

## 覆盖范围

- TX-01/02/03：Begin 确认后才 Active；嵌套和事务不匹配拒绝；发送前失败保持 Active；发送后缺失或无效 ACK 进入 OutcomeUnknown、连接失效且不重放；已确认结果不受迟到取消和重复 Dispose 影响。
- TX-01/06：Active Dispose 使用独立有限清理期限；分别覆盖已确认回滚、未知清理、Reader 关闭超时和保留业务异常。
- TX-05：能力检查、特殊名称、NUL/长度预检、重名替换、回滚失效、未验证 Release 语法时的逻辑释放，以及测试侧创建数量上限。
- TX-06/T11：两个独立连接和命名 barrier 验证 ReadCommitted、ReadUncommitted、Serializable；不使用固定 sleep，也不预填读取结果。
- TX-07/08：没有可靠服务器信号时 DDL 结果保持未知；默认不加入环境事务，显式 enlist 拒绝。

保存点名称和创建数量上限是测试参数，不是服务器限制。合成内部名称只是逻辑映射，不包含 SQL；Release 用例不假设服务器存在对应命令。

实库过程必须使用 TEST wrapper、`WDM_PROVIDER_TEST` 和本轮唯一对象，由独立连接验证最终状态并只清理本轮对象。T11 使用 barrier 同步，不按耗时猜测结果。准备 fixture 未构建或连接数据库。
