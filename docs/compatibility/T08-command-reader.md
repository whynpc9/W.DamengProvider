# T08 Command 与 Reader 契约变化

当前版本为 `0.2.0`；本页历史阶段证据仍归原候选，当前发布状态与验证范围见[发布记录](../release-0.2.0.md)。

旧 `0.1.0-preview.1` 包只开放 CommandType.Text；`0.2.0` 新增 [StoredProcedure、输出参数及受管游标](I09-stored-procedures.md)。当前发布状态见 [progress.json](../implementation/progress.json)；历史协议表征见 [T08-protocol-results.md](../implementation/T08-protocol-results.md)。

- 执行期间冻结 SQL、参数 metadata/值和超时；返回的 Reader 持有计划直到关闭。修改 Command、Parameter 或参数集合会立即失败。byte[]/char[] 输入复制；未明确支持的可变参数对象在执行前拒绝。Clone 不共享参数父关系。
- 空 rowset 保留。ExecuteReader 定位首个可读结果；NextResult 更新整套列信息、类型读取器和缓存。纯 DML Reader 没有伪造数据行。
- RecordsAffected 只使用已证明归属的 DML 计数。纯 SELECT 及无法获取完整 DML 计数的复合语句返回 -1；EF 显式 SQL%ROWCOUNT 结果单独返回。累计使用 checked Int64，公开 Int32 无法容纳时抛 OverflowException。
- Scalar 对第一可读结果的空集返回 null，SQL NULL 返回 DBNull。NonQuery 完成结果遍历后才返回；后续服务器错误不会作为成功忽略。
- 支持的行为标志逐位处理。当前源码按 [I10 元数据契约](I10-metadata.md) 开放单条 SELECT 的 SchemaOnly/KeyInfo；描述不执行业务 SQL，未支持语句在发送前拒绝。SequentialAccess 下的有界流式 LOB 范围见 [数据源与流式 LOB 说明](R3.md)。
- GetSchemaTable 只使用当前响应与 KeyInfo 提前取得的本地 metadata；未知 IsKey/IsUnique 为 DBNull，getter 不执行辅助目录 SQL。未请求 KeyInfo 时不推断键属性。
- 正常关闭 Reader 使用有限、独立的 CleanupTimeout。Text 提前关闭使用已验证的 statement-close 请求，不通过额外 MORE_RESULT 恢复会话；过程提前关闭需在同一有限清理期限内终结结果并获取输出，随后关闭子游标和 statement。无法确认同步状态的失败路径关闭捕获的物理会话；需显式重新 Open。不能把断开描述为服务端已确认回滚。
- Dispose Command 后，已有 Reader 可继续消费并关闭；新的 Command 调用被拒绝。没有活动 Reader 的 prepared statement 也使用有限清理期限。
- 参数编码错误不再触发旧的逐元素数组执行补救逻辑；复杂类型及 ComplexTypeToBytes 非默认值仍明确拒绝；StoredProcedure、输出参数和 RefCursor 的源码范围见 [I09](I09-stored-procedures.md)，Text 命令仍仅接受 Input。数组绑定（ArrayBindCount）与 DbBatch 的后续开放范围见[数组绑定与 DbBatch](I07-I08-array-binding-dbbatch.md)。

RecordsAffected 和键 metadata 的变化是有意调整。历史包消费测试覆盖 ef-savechanges-v1 的选定 SQL 契约；当前发布候选仍需执行适用的包消费与 EF 回归检查。
