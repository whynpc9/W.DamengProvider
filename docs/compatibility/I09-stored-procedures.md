# 存储过程、函数返回值与输出游标

这些能力自 [0.2.0](../release-0.2.0.md) 起提供。协议和实库行为以 DM 8.1.5.60、UTF-8、message version 21 为依据；与官方驱动的对比见[过程对比记录](I09-characterization.md)。

## 调用与参数匹配

设置 `CommandType.StoredProcedure`，`CommandText` 只能是过程或函数标识符，可带一个 Schema 限定：

- 普通名称转为大写引用，双引号名称保留大小写和转义双引号。
- 括号、参数值、额外 SQL、注释和多于两段的名称会拒绝。
- 支持无参过程。

服务器提供参数名时，所有业务参数必须按名匹配：忽略一个 `:` 或 `@` 前缀，大小写不敏感。参数可以在集合中重排；未知名、部分匹配、重复名、参数数量或方向不符均在执行前拒绝。只有服务器描述全部不带名时，才使用集合顺序。

函数只能有一个 `ReturnValue`。它在集合中的位置不影响业务参数顺序，服务器返回槽会独立映射到该参数。

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

`InputOutput` 同时保存输入快照和输出槽。纯 `Output` / `ReturnValue` 的旧 `Value` 不用于类型推断、输入编码或隐式 `Size`。输出类型使用显式 `DbType` / `DmSqlType` 或可靠的服务器描述；建议显式声明，尤其是函数返回值和游标。数据库 NULL 返回 `DBNull.Value`。

显式 `Size > 0` 限制输出字符串字符数或二进制字节数；0 表示不附加此限制，负数拒绝。DECIMAL 的 `Precision` / `Scale` 和时间 `Scale` 都要求结果无损；超限或不精确转换会抛错，不截断，也不发布部分输出。

公开连接配置继续拒绝 `EnRsCache=true`。存储过程不参与遗留的全局结果集缓存，每次调用都会重新执行并获取输出；Reader 自身的行缓冲仍用于正常读取。

## 派生参数

`DmCommandBuilder.DeriveParameters` 要求命令未释放、未执行、`CommandType.StoredProcedure`，且连接已打开。

标识符规则与过程执行一致：普通名称转为大写，双引号名称保留大小写、点号、单引号和转义双引号；非法名称在目录查询前拒绝。目录查询中的名称和对象 ID 均通过参数绑定传入。

### 独立过程与函数

- 未限定名称使用服务器当前 Schema，不依赖连接配置中的 Schema 缓存。
- 两段名称同时检查 `schema.procedure` 和当前 Schema 中的 `package.member`；两者均存在时明确拒绝歧义。
- 独立过程和函数通过 `ALL_OBJECTS` 确认存在，参数按 `ALL_ARGUMENTS.SEQUENCE` 排列。
- 只有对象类型为 `FUNCTION` 时，`POSITION=0` 才识别为 `ReturnValue`，即使该行名为 `V_RET` 且方向为 `OUT`。
- 普通过程的首个参数也可能是 `POSITION=0`，不会因此变成返回值。
- 真实存在的无参过程派生为空集合，缺失对象抛错。

### 包成员元数据

包成员路径先通过 `SYS.SYSPKGPROCINFOS` 确认成员存在且只有一个 `MTDID`，再读取 `SYS.SYSPKGPROCPARAMS` 中的 `IOFLAG`、`TYPE$`、`NAME`、`PARAMID`，按 `PARAMID` 排序。

重载、重复或无法解释的元数据会拒绝。这不增加三段名称或包执行能力；包名称支持仅用于派生元数据。

### 参数替换与失败

派生期间持有命令修改锁，沿用命令超时和适用的事务绑定。每个目录 Reader 在下一次查询前关闭；全部 Reader 关闭、完整参数集转换和校验成功后，才一次性替换参数。

参数集合对象保持不变。成功后旧参数解除归属；查询、转换、重复名或校验失败均保留原参数内容及归属。

**验证限制：** 包路径目前只有离线验证。TEST 账号缺少 `CREATE PACKAGE` 权限，服务器返回 `-5528`，包成员实库验证为 `integration_pending`。

[DeriveParametersProbe](../../tools/DeriveParametersProbe/Program.cs) 的 `--standalone-only`（或 `--skip-package`）会显式跳过包创建和全部包用例，并记录 `not_run`；独立过程、函数、带引号名称、失败后同连接复用和对象清理仍照常验证。无该参数时保留包探针，供已有合法权限的环境使用。

## 输出何时可读

- `ExecuteNonQuery` / `ExecuteScalar`：结果终结且 Reader 清理成功后，才统一写回标量输出。
- `ExecuteReader`：普通结果的 `NextResult` 确认全部终结，或提前 `Close` / `Dispose` 成功后写回。读到某个结果末尾、`SingleRow` 或 `SingleResult` 限制不代表执行终结。
- 带游标的父 Reader：完整执行响应和游标描述验证成功后，先写回受管 `DbDataReader`；标量输出仍等父 Reader 成功关闭后写回。

执行、解码、读取、取消、超时或关闭失败时，不写回未完成的标量输出。已经写回的游标会失效，并恢复参数执行前的值；保留的旧值不代表本次执行成功。

执行期间不能修改命令和参数集合。输出按捕获的原参数对象写回，不根据当前名称重新查找。成功关闭后可以重用命令和参数；纯输出参数的上次 Reader 值不会作为新输入。

提前关闭使用有限的 `CleanupTimeout`，完成必要的结果终结、子游标和语句清理。无法确认协议对齐时丢弃物理会话。关闭失败后 Reader 仍进入关闭状态，重复关闭不会重新执行过程。

## 输出游标

仅 `ExecuteReader` 支持 `DmDbType.Cursor` / `RefCursor` 的 `Output` 参数。游标输入、输入输出、函数返回游标，以及 NonQuery/Scalar 的游标参数均在发送前拒绝。

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
// 释放 parent 会关闭所有子游标，包括尚未读取的游标。
```

多个游标拥有各自的服务器语句和读取位置，但共用父执行资源。同一物理连接仍只允许一个实际操作，多个游标应串行交替读取：

- 子游标关闭不释放父 Reader 的执行资源。
- 父 Reader 关闭、连接关闭或重开、执行失败都会使旧游标无法继续读取，包括已缓存行。
- 游标语句不进入语句池。

已测服务器在执行带 OUT 游标的过程后，会将缓存描述改为 INOUT。驱动只对已确认的 CType 120 接受这一变化，输入是驱动自己分配的语句 ID，不开放用户传入游标；普通参数方向校验不受影响。

普通多结果通过父 Reader 的 `Read` / `NextResult` 顺序访问。已测服务器不允许同一过程混合普通 SELECT 结果与 OUT 游标，执行时保留服务器错误。`RecordsAffected` 只累加有明确依据的 DML 行数，过程等未知计数保持 -1。

## 输出类型与限制

| 输出类型 | CLR 结果与边界 |
| --- | --- |
| 整数、布尔、浮点 | 对应 CLR 标量；整数范围和转换溢出明确失败 |
| DECIMAL | `decimal`；不能精确表示时失败 |
| XDEC（仅纯输出/返回值） | `DmDecimal`，保留大精度；XDEC 输入/输入输出仍拒绝 |
| DATE、TIME、TIMESTAMP | 现有 CLR 日期/时间表示；保留可表达的 100ns 精度，非 tick 对齐的九位值拒绝 |
| 带时区时间戳 | `DateTimeOffset`，保留偏移和可表达精度，不推断时区名称 |
| 时间偏移、INTERVAL | `DateTimeOffset`、`DmIntervalDT`、`DmIntervalYM`；DT 小数最多 6 位，YM 不接受非零 Scale，精度损失拒绝 |
| 字符串、二进制 | `string` / `byte[]`，受显式 Size 限制 |
| BLOB、CLOB | 物化为 `byte[]` / `string`，沿用 64 MiB 返回载荷限制，不返回失效定位符 |
| 游标 | 受父 Reader 管理的 `DbDataReader` |

普通 TIME 输出为 `DateTime`，日期基准沿用 1900-01-01，不会隐式转换为本地时区的 `DateTimeOffset`。已测服务器的纯 OUT YEAR(4) TO MONTH 描述附带无意义的默认小数位 6；驱动只对该已观察描述归一为 0，并保留原始描述供诊断。

以下情况会拒绝：未知复杂类型、ROWID 输出、非单值输出数组、未验证的长度标记，以及旧 message version <12 的输出模式。`CommandType.Text` 仍只接受 Input 参数。`SchemaOnly` / `KeyInfo` 仅支持[元数据说明](I10-metadata.md)中的单条 SELECT，不能与存储过程组合。

## 事务与批量限制

**存储过程与活动显式事务的组合会在发送前拒绝。** 实测过程内部 COMMIT 后，官方事务对象仍可调用 Rollback，但业务数据已经提交；当前协议不能可靠反映这类过程事务状态。

不要根据取消、超时或异常推断过程已经回滚，也不要自动重试有副作用的过程；应通过新连接核查结果。没有活动显式事务时，过程内部事务控制由服务器执行。

数组绑定仍拒绝存储过程。`DbBatch` 在逐条预检时拒绝非 Text 命令和输出参数，因此不会先执行前序命令再拒绝后续过程。过程执行范围不包含 EF 存储过程映射、分布式事务或包名称解析。

## 验证入口

- [三目标离线验证记录](I09-offline-validation.md)
- [StoredProcedureProbe](../../tools/StoredProcedureProbe/README.md)

每次实库运行先核对 `WDM_PROVIDER_TEST` 身份和 Schema，只使用本轮唯一过程、函数和表，并由新连接确认精确清理。官方驱动的返回值位置、游标缓存生命周期或时间输入降级只用于对比，不作为本驱动的预期行为。
