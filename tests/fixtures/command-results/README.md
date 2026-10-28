# Command 与结果集测试场景

`scenarios.json` 分为合成 `cases` 和 `real_database_probes`。合成用例为 `not_run`，实库过程为 `planned_not_run`；逻辑结果由测试宿主提供，用于验证[Command 与 Reader 行为](../../../docs/compatibility/T08-command-reader.md)，不是数据库输出或协议消息。

实库运行必须使用 TEST wrapper、本轮唯一对象、独立最终状态检查，并只清理本轮对象。fixture 不含连接配置、凭据、证书、报文或 opcode 假设。

## 结果序列模型

`input.results` 是有序逻辑序列：

- `rowset`：列元数据和合成行；空 `rows` 仍是结果集，非 DML 使用 `records_affected: -1`。
- `update_count`：整数影响行数；`null` 表示未知，不能变成 0。
- `error`：测试专用 `sql_code`，验证后续错误不会被吞掉。

`input.operations` 包含 ExecuteReader、Read、NextResult、ExecuteScalar、ExecuteNonQuery、Close 等客户端操作。这里的顺序只约束测试宿主，不声称达梦语句会产生同样序列。

## 覆盖范围与边界

- CMD-01/02：UPDATE/DELETE 的 0/1 影响行数、DML + `SQL%ROWCOUNT`、identity/sequence 键和多个生成列，DML 与回读之间不插入额外探测。
- CMD-03/04：两个结果集、首个空结果集、已知/未知影响行数、ExecuteScalar 无行 `null` 与 SQL NULL `DBNull.Value`，以及前序结果后的错误。
- CMD-04：字符串、注释和引号标识符中的分号、`:p`、`@p`、`?` 不产生绑定或语句拆分；参数值不插值进 SQL。
- CMD-05/06：`SingleRow|CloseConnection`、`SequentialAccess|CloseConnection`；元数据路径未验证时 `SchemaOnly|KeyInfo` 在网络活动前拒绝；提前关闭不发送未开始的副作用操作。
- CMD-07：重复 Prepare/事务/执行保持 CommandText 和当前参数快照；可变字节数组会复制或隔离。
- `EFCoreNextResult` 开关和 `/*EFCOREROWCOUNT*/` 出现次数是独立观察输入，不推断历史交互或推广到任意 DMSQL。

`SequentialAccess` 场景不证明完整流式 LOB；`SchemaOnly|KeyInfo` 的当前已支持范围以[元数据说明](../../../docs/compatibility/I10-metadata.md)为准，本 fixture 中未验证路径仍按发送前拒绝处理。实库多结果探针必须先选择目标服务器已知支持的操作。
