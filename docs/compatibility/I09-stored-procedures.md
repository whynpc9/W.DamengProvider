# 存储过程、函数返回值与输出游标

本页描述 issue #9 的源码契约，不属于已发布的 `0.1.0-preview.1` 包。协议表征以 DM 8.1.5.60、UTF-8、message version 21 为依据；官方驱动观察与 W 候选验证分别记录在 [表征记录](I09-characterization.md)。

## 调用和参数

设置 `CommandType.StoredProcedure`，`CommandText` 只能是过程或函数标识符，可带一个 Schema 限定。普通名称转为大写引用，双引号名称保留大小写及转义双引号。括号、参数值、额外 SQL、注释和多于两段的名称拒绝。无参过程也受支持。

服务器提供参数名时，所有业务参数必须按名匹配：忽略一个 `:` 或 `@` 前缀，大小写不敏感。参数可以在集合中重排；未知名、部分匹配、重复名、参数数量或方向不符均在执行前拒绝。仅服务器描述全部不带名时使用集合顺序。函数只能有一个 `ReturnValue`，它的集合位置不改变业务参数顺序；服务器返回槽始终独立映射到该参数。

```csharp
using System.Data;
using W.Dm;

using var command = connection.CreateCommand();
command.CommandType = CommandType.StoredProcedure;
command.CommandText = "APP.CALCULATE_TOTAL";
command.Parameters.Add(new DmParameter("P_ID", DmDbType.Int32) { Value = 42 });
var total = new DmParameter("P_TOTAL", DmDbType.Decimal)
{
    Direction = ParameterDirection.Output
};
command.Parameters.Add(total);
await command.ExecuteNonQueryAsync();
decimal amount = (decimal)total.Value;
```

`InputOutput` 同时捕获输入快照和输出槽。纯 `Output`/`ReturnValue` 的旧 `Value` 不用于类型推断、输入编码或隐式 Size。输出类型使用显式 `DbType`/`DmSqlType` 或可靠的服务器描述；建议显式声明类型，尤其函数返回值与游标。数据库 NULL 返回 `DBNull.Value`。

显式 `Size > 0` 限制输出字符串字符数或二进制字节数，0 表示不附加此限制，负数拒绝。DECIMAL 的 `Precision`/`Scale` 要求结果可无损表示；时间 `Scale` 也不能导致精度丢失。超限或不精确转换抛错，不截断或发布部分输出。

## 输出可读时机和失败

- `ExecuteNonQuery`/`ExecuteScalar`：结果终结且 Reader 清理成功后，才统一发布本次标量输出。
- `ExecuteReader`：普通结果的 `NextResult` 确认全部终结，或提前 `Close`/`Dispose` 成功后发布。读到某个结果末尾、`SingleRow` 或 `SingleResult` 限制不是执行终结。
- 带游标的父 Reader：完整执行响应及游标描述验证成功后，游标参数先发布受管 `DbDataReader`；标量输出到父 Reader 成功关闭才发布。
- 执行、解码、读取、取消、超时或关闭失败不发布未完成的标量输出；已经提前发布的游标失效，并恢复参数执行前的值。保留的值属于旧执行，不表示本次成功。

执行期间，命令和参数集合保持现有 mutation gate 限制。发布按捕获的原参数对象 identity 进行，不向当前名称查找结果回写。成功关闭后可以重用命令和参数；纯输出的上次 Reader 值不会被作为新输入快照。

提前关闭沿用有限的 `CleanupTimeout`：终结必要的普通结果、关闭子游标和父 statement。无法确认协议对齐时丢弃所属物理会话。关闭失败也使 Reader 进入关闭终态；重复关闭不重新执行过程。

## 游标所有权

仅 `ExecuteReader` 支持 `DmDbType.Cursor`/`RefCursor` 的 `Output` 参数。游标输入、输入输出、函数返回游标以及 NonQuery/Scalar 的游标参数在发送执行前拒绝。

```csharp
using var command = connection.CreateCommand();
command.CommandType = CommandType.StoredProcedure;
command.CommandText = "APP.OPEN_ITEMS";
var items = new DmParameter("P_ITEMS", DmDbType.RefCursor)
{
    Direction = ParameterDirection.Output
};
command.Parameters.Add(items);
await using var parent = await command.ExecuteReaderAsync();
var cursor = (System.Data.Common.DbDataReader)items.Value;
while (await cursor.ReadAsync())
    Console.WriteLine(cursor.GetValue(0));
// parent.DisposeAsync() closes every child cursor, including unread ones.
```

多个游标拥有各自的服务器 statement 和读取位置，共用父执行 lease。同一物理连接仍只允许一个实际操作，多个游标应串行交替读取。子游标关闭不释放父 lease；父 Reader 关闭、连接关闭/重开、执行失败均使旧游标无法继续读取，包括缓存行。游标 statement 不进入 statement pool。

本次服务器在执行带 OUT 游标的过程后，会将缓存描述改为 INOUT；驱动仅对已确认的 CType 120 接受这一描述变化，输入是驱动自己分配的 statement ID，不开放用户传入游标。普通参数方向校验不受影响。

普通多结果通过父 Reader 的 `Read`/`NextResult` 顺序访问。该服务器不允许同一过程混合普通 SELECT 返回结果与 OUT 游标，执行时保留服务器错误；不要把它当作可用组合。`RecordsAffected` 只累加有证据的 DML 行数，过程等未知计数保持 -1。

## 类型及限制

| 输出类型 | CLR 结果与边界 |
| --- | --- |
| 整数、布尔、浮点 | 对应 CLR 标量；整数范围和转换溢出明确失败 |
| DECIMAL | `decimal`；不能精确表示时失败 |
| XDEC（仅纯输出/返回值） | `DmDecimal`，保留大精度；XDEC 输入/输入输出仍拒绝 |
| DATE、TIME、TIMESTAMP | 现有 CLR 日期/时间表示；保留可表达的 100ns 精度，非 tick 对齐的九位值拒绝 |
| 带时区时间戳 | `DateTimeOffset`，保留偏移和可表达的精度；不推断时区名称 |
| 时间偏移、INTERVAL | `DateTimeOffset`、`DmIntervalDT`、`DmIntervalYM`；DT 小数最多 6 位，YM 不接受非零 Scale，精度损失拒绝 |
| 字符串、二进制 | `string`/`byte[]`，受显式 Size 限制 |
| BLOB、CLOB | 物化为 `byte[]`/`string`，沿用 64 MiB 返回载荷限制，不返回失效 locator |
| 游标 | 受父 Reader 管理的 `DbDataReader`，见上文 |

普通 TIME 输出为 `DateTime`（日期基准沿用 1900-01-01），不会隐式转换为本地时区的 `DateTimeOffset`。本次服务器的纯 OUT YEAR(4) TO MONTH 描述附带无意义的默认小数位 6；仅对该已观察描述归一为 0，并保留原始描述供诊断。

未知复杂类型、ROWID 输出、非单值输出数组及未表征的长度标记拒绝。旧 message version <12 的输出模式未开放。`CommandType.Text` 仍只接受 Input，过程能力不允许绕过 Text 的输出限制。`SchemaOnly`/`KeyInfo` 仅支持独立元数据契约中的单条 SELECT；存储过程与这两个标志的组合在获取执行 lease 前拒绝。

**存储过程与活动显式事务的组合在发送前拒绝。** 实测过程内部 COMMIT 后，官方事务对象仍可调用 Rollback，但业务数据已经提交；当前协议不能可靠反映这类过程事务状态。不要据取消、超时或异常推断过程已经回滚，不自动重试过程；有副作用时由新连接核查结果。没有活动显式事务时，过程内部事务控制由服务器执行。

已整合主线 PR #19 的 DbBatch/数组绑定实现；数组绑定仍拒绝过程，DbBatch 在逐条预检时拒绝非 Text 命令及输出参数，因此不会先执行前序命令再拒绝后续过程。范围不含 EF 存储过程映射、分布式事务或 package 名称解析。

## 验证入口

独立离线回归见 [三目标验证记录](I09-offline-validation.md)，包含 .NET 8/9/10 的 12 套测试及主线批处理整合。

可复用探针见 [StoredProcedureProbe](../../tools/StoredProcedureProbe/README.md)。每次实库运行先核对 `WDM_PROVIDER_TEST` 身份及 Schema，只使用本轮唯一过程、函数、表并由新连接检查最终状态和精确对象清理。官方驱动的 ReturnValue 位置错误、游标缓存生命周期或时间输入降级只作对照，不作为 W 的预期行为。
