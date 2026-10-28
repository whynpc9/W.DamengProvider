# Command 与 Reader 行为

本文说明当前 [0.3.1](../release-0.3.1.md) 的命令执行、结果读取和释放行为。存储过程见[存储过程说明](I09-stored-procedures.md)，批量执行见[数组绑定与 DbBatch](I07-I08-array-binding-dbbatch.md)。

## 执行期间的参数和资源

- 执行期间会固定 SQL、参数元数据、参数值和超时。返回的 Reader 持有执行资源直到关闭。
- Reader 活动时可以修改的范围受限；修改 Command、Parameter 或参数集合会立即失败。
- `byte[]` / `char[]` 输入会复制；未明确支持的可变参数对象在执行前拒绝。
- `Clone` 不共享参数的父级关系。

## 结果集与影响行数

- 空结果集会保留。`ExecuteReader` 定位到首个可读结果；`NextResult` 更新完整列信息、类型读取器和缓存。纯 DML Reader 不会伪造数据行。
- `RecordsAffected` 只使用能够明确归属的 DML 计数。纯 SELECT 和无法获得完整 DML 计数的复合语句返回 -1；EF 显式 `SQL%ROWCOUNT` 结果单独返回。
- 累计计数使用 checked `Int64`；公开 `Int32` 无法容纳时抛 `OverflowException`。
- `ExecuteScalar` 对首个可读结果的空集返回 `null`，SQL NULL 返回 `DBNull`。
- `ExecuteNonQuery` 会完成结果遍历后再返回，不会忽略后续服务器错误。

## SchemaOnly、KeyInfo 与顺序读取

支持的行为标志逐位处理。单条 SELECT 的 `SchemaOnly` / `KeyInfo` 见[元数据说明](I10-metadata.md)：描述操作不会执行业务 SQL，不支持的语句在发送前拒绝。

`GetSchemaTable` 只使用当前响应和 `KeyInfo` 预取的本地元数据；未知 `IsKey` / `IsUnique` 为 `DBNull`，getter 不执行辅助目录 SQL。未请求 `KeyInfo` 时不会推断键属性。

`SequentialAccess` 下的有界流式 LOB 见[数据源与 LOB](R3.md)。

## 关闭与清理

- 正常关闭 Reader 使用独立且有限的 `CleanupTimeout`。
- Text 命令提前关闭时使用已验证的 statement-close 请求，不通过额外 `MORE_RESULT` 恢复会话。
- 存储过程提前关闭时，需要在同一清理期限内终结结果、获取输出并关闭子游标和语句。
- 无法确认同步状态时会关闭捕获的物理会话，调用方需要显式重新 `Open`；断开不等于服务器已确认回滚。
- Command 释放后，已有 Reader 可以继续读取并关闭，新命令调用会被拒绝。没有活动 Reader 的已准备语句也使用有限清理期限。

参数编码错误不会触发旧版逐元素数组执行的补救逻辑。复杂类型和非默认 `ComplexTypeToBytes` 仍明确拒绝；Text 命令只接受 Input 参数。
