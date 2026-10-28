# ADO.NET 元数据与 SELECT 描述

这些能力自 [0.2.0](../release-0.2.0.md) 起提供。本文先说明当前行为，末尾保留原始验证记录。

## GetSchema / GetSchemaAsync

同步和异步入口使用同一集合定义、SQL、列类型和结果投影。异步入口的查询、分页、LOB 默认值读取和资源清理均使用异步 API；内部 Reader 共用顶层元数据操作，不另开连接。纯内存集合可以直接返回已完成任务，但仍要求连接已打开且没有被其他操作占用。

### 集合名与过滤条件

- 集合名按序号（ordinal）忽略大小写。
- `null` 或空集合名返回 `MetaDataCollections`。
- 未知集合或过滤条件数量超出定义时，在占用连接和发送 SQL 前抛 `ArgumentException`。
- 调用前已取消时返回取消任务，不发送请求。

**所有过滤条件（restriction）都是等值匹配：**

- `null` 表示不限制，空字符串是实际过滤值。
- 调用方数组会复制。
- 值不会转大写，也不会移除反引号或双引号。
- `%`、`_` 不作为 LIKE 通配符，单引号也只是标识符字符。
- 应传服务器目录中的原始名称，而不是 SQL 引用形式。例如对象 SQL 名 `"MiXeD%_"` 的过滤值是 `MiXeD%_`。
- 最终相等比较使用服务器的字符串比较规则，所有值通过命名参数绑定，不拼入 SQL。

返回对象名保留服务器原文。无法可靠获取的目录值返回 `DBNull.Value`。权限不足、目录视图或列缺失等错误会保留数据库异常，不会伪装成空集合，也不会因此获得额外权限。

### 集合、过滤条件与返回列

过滤条件从左到右对应数组下标 0、1、2；`Restrictions.RestrictionNumber` 使用 ADO.NET 的 1 起始序号。

类型缩写：`S` = `System.String`，`I` = `System.Int32`，`L` = `System.Int64`，`B` = `System.Boolean`。

| 集合 | 过滤条件顺序 | 返回列（名称: CLR 类型）与含义 |
| --- | --- | --- |
| MetaDataCollections | 无 | CollectionName:S、NumberOfRestrictions:I、NumberOfIdentifierParts:I；包含本表全部 16 个名称。 |
| Restrictions | 无 | CollectionName:S、RestrictionName:S、RestrictionDefault:S、RestrictionNumber:I；Default 为 DBNull，不自动补过滤值。 |
| DataSourceInformation | 无 | 标准 ADO.NET 列，详见下文。读取已连接实例的版本和大小写配置，不发送查询。 |
| DataTypes | 无 | 标准 ADO.NET 类型能力列，详见下文。使用内存类型表，不对每个服务器配置做运行时探测。 |
| ReservedWords | 无 | ReservedWord:S；来自当前服务器 V$RESERVED_WORDS。 |
| Tables | Schema、Table、TableType | TABLE_CATALOG:S、TABLE_SCHEMA:S、TABLE_NAME:S、TABLE_TYPE:S、FILLFACTOR:I、SPACE_LIMIT:I、ROW_COUNT:UInt64。来自可见 ALL_OBJECTS；TABLE_TYPE 为 UTAB 或 VIEW，过滤值 UTAB 映射为目录 TABLE，也接受 TABLE。CATALOG、FILLFACTOR、SPACE_LIMIT、ROW_COUNT 未验证，均 DBNull。 |
| Columns | Schema、Table、Column | TABLE_SCHEMA:S、TABLE_NAME:S、COLUMN_NAME:S、COLUMN_ID:I、COLUMN_TYPE:S、COLUMN_SIZE:I、COLUMN_SCALE:I、NULLABLE:S、DEFAULT_VALUE:S、NUMERIC_PRECISION:I、DATETIME_PRECISION:I、CHARACTER_OCTET_LENGTH:I。来自 ALL_TAB_COLUMNS。 |
| Users | Name | USERNAME:S；来自 ALL_USERS。 |
| Schemas | Name | NAME:S；来自 SYS.SYSOBJECTS 中的 SCH 对象，沿用当前 TEST 的目录权限。 |
| Databases | Name | 与 Schemas 同义，DataTable.TableName 仍为 Schemas；不代表物理数据库，也不执行切库。 |
| Procedures | Schema、Procedure、ProcedureType | PROCEDURE_SCHEMA:S、PROCEDURE_NAME:S、PROCEDURE_TYPE:S；来自 ALL_OBJECTS，仅含独立 PROCEDURE / FUNCTION，不把包成员当成独立过程。 |
| Parameters | Schema、Procedure、Parameter | PROCEDURE_SCHEMA:S、PROCEDURE_NAME:S、PARAMETER_NAME:S、PARAMETER_ID:I、PARAMETER_TYPE:S、PARAMETER_SIZE:I、PARAMETER_SCALE:I、DEFAULT_VALUE:S、NULLABLE:S、PARAMETER_INOUT:Int16；来自 ALL_ARGUMENTS 中非包过程/函数。PARAMETER_ID 为目录 POSITION，函数返回值通常为 0 且名称可能为 NULL；SIZE 是 DATA_LENGTH 字节数，SCALE 是 DATA_SCALE，NULLABLE 未知。INOUT：IN=0、OUT=1、IN/OUT=2；未知为 DBNull。 |
| Indexes | Schema、Table、Name | INDEX_SCHEMA:S、INDEX_NAME:S、TABLE_NAME:S、UNIQUE:S、INDEX_TYPE:S、TABLE_SCHEMA:S；Schema 过滤条件作用于表所属 Schema。UNIQUE 为 Y/N/DBNull，表示整个索引是否唯一；INDEX_TYPE 保留服务器分类。 |
| PrimaryKeys | Schema、Table | TABLE_SCHEMA:S、TABLE_NAME:S、COLUMN_NAME:S、KEY_SEQUENCE:I、PK_NAME:S；来自 ALL_CONSTRAINTS 与 ALL_CONS_COLUMNS 的 P 约束，每个主键成员一行，KEY_SEQUENCE 是 1 起始 POSITION。允许不提供过滤条件。 |
| IndexColumns | Schema、Table、Index | COLUMN_NAME:S、SORT_ORDER:S、TABLE_SCHEMA:S、TABLE_NAME:S、INDEX_SCHEMA:S、INDEX_NAME:S、ORDINAL_POSITION:I；ALL_IND_COLUMNS 每成员一行，ORDINAL_POSITION 为 1 起始 COLUMN_POSITION；SORT_ORDER 保留 ASC/DESC。Schema 过滤条件指表 Schema。只执行目录查询，无临时表、DDL 或动态 SQL。 |
| ForeignKeys | Schema、Table、Name | TABLE_SCHEMA:S、TABLE_NAME:S、COLUMN_NAME:S、FOREIGN_KEY_CONSTRAINT_NAME:S、KEY_SEQUENCE:I、REFERENCED_TABLE_SCHEMA:S、REFERENCED_TABLE_NAME:S、REFERENCED_COLUMN_NAME:S、DELETE_RULE:S。过滤条件和 TABLE/COLUMN 列始终指引用方（子表）；REFERENCED_* 指被引用方。复合外键按 POSITION 与被引用约束成员配对，缺少可见的引用端信息时保留 DBNull。 |

目录集合按 Schema、对象名和成员序号排序。PrimaryKeys、IndexColumns、ForeignKeys 分别使用目录的 POSITION、COLUMN_POSITION、POSITION；不会把复合唯一约束的每个成员误报为单列唯一。

### Columns 的精度、长度与 NULL

- `COLUMN_ID` 是目录的 1 起始列序号，`COLUMN_TYPE` 保留原始 DATA_TYPE。
- `COLUMN_SIZE` 对字符类型表示 CHAR_LENGTH（字符数），对 DECIMAL/DEC/NUMERIC/NUMBER 表示 DATA_PRECISION；其他类型（包括 LOB）的逻辑长度尚未统一验证，返回 DBNull。
- `CHARACTER_OCTET_LENGTH` 只对字符类型返回 DATA_LENGTH 字节数。LOB 长度未知，不伪造固定最大值或当前值长度。
- `COLUMN_SCALE` 直接反映目录 DATA_SCALE，不混入协议打包的小数位。
- `NUMERIC_PRECISION` 仅对数值类型返回 DATA_PRECISION。
- `DATETIME_PRECISION` 对 TIME/TIMESTAMP/DATETIME 返回 DATA_SCALE（小数秒位数），非时间类型为 DBNull。
- `NULLABLE` 保留目录 Y/N，未知为 DBNull。
- `DEFAULT_VALUE` 原样返回目录默认表达式，空或未知为 DBNull；异步路径用 `GetFieldValueAsync<object>` 读取 LOB 表达式。

列声明精度与 CLR 能精确承载的值域不是同一概念，写入和读取仍遵循[时间精度说明](TP-temporal-precision.md)。

### 内存集合字段

`DataSourceInformation` 返回：CompositeIdentifierSeparatorPattern:S、DataSourceProductName:S、DataSourceProductVersion:S、DataSourceProductVersionNormalized:S、GroupByBehavior:System.Data.Common.GroupByBehavior、IdentifierPattern:S、IdentifierCase:System.Data.Common.IdentifierCase、OrderByColumnsInSelect:B、ParameterMarkerFormat:S、ParameterMarkerPattern:S、ParameterNameMaxLength:I、ParameterNamePattern:S、QuotedIdentifierPattern:S、QuotedIdentifierCase:System.Data.Common.IdentifierCase、StatementSeparatorPattern:S、StringLiteralPattern:S、SupportedJoinOperators:System.Data.Common.SupportedJoinOperators。产品版本来自当前握手；未经规范化的版本列为 DBNull。

`DataTypes` 返回：TypeName:S、ProviderDbType:I（DmDbType 枚举）、ColumnSize:L、CreateFormat:S、CreateParameters:S、DataType:S（CLR 类型全名）、IsAutoincrementable:B、IsBestMatch:B、IsCaseSensitive:B、IsFixedLength:B、IsFixedPrecisionScale:B、IsLong:B、IsNullable:B、IsSearchable:B、IsSearchableWithLike:B、IsUnsigned:B、MaximumScale:Int16、MinimumScale:Int16、IsConcurrencyType:B、IsLiteralsSupported:B、LiteralPrefix:S、LiteralSuffix:S、NativeDataType:S。

类型表保留历史标量类型，并补充 DATE、TIME、TIMESTAMP、带时区类型和 BLOB/CLOB 名称；VARCHAR 不再误报固定长度。时间的 MaximumScale 是类型声明的小数秒上限：DATE=0、TIME=6、DATETIME/TIMESTAMP=9，带时区类型对应相同上限。TIME 的 CLR 名称跟随当前 `DbTimeToTimeSpan` 配置。类型表不会绕过实际写入限制，也不宣称列出所有数据库扩展类型和别名。

## SELECT 的 SchemaOnly 与 KeyInfo

当前支持单条 `CommandType.Text` SELECT，包括绑定参数、引号标识符、注释和查询表达式。驱动用词法扫描检查语句边界和参数，不是只检查 SELECT 前缀。

批量、存储过程、WITH、SELECT INTO、括号不平衡和未绑定参数会在分配服务器句柄前拒绝。

### SchemaOnly

`SchemaOnly` 发送 Prepare opcode 5 / execute=false，根据服务器返回的查询列构建无行 Reader，不执行业务 SQL：

- `HasRows`、同步/异步 `Read` 和 `NextResult` 恒为 `false`。
- 仍须关闭 Reader，释放描述句柄和连接占用。
- 参数值不会通过该路径上传或执行。
- `IsReadOnly` 为 DBNull，因为 Prepare 响应没有可更新性字段。

### KeyInfo

`KeyInfo` 在同一连接占用下串行执行描述、目录预取和业务执行。目录 Reader 在业务执行前关闭，不会在活动业务 Reader 上追加查询。单独使用 `KeyInfo` 会正常取数；与 `SchemaOnly` 组合时只描述。

键推断仅适用于可确认的简单单表直接列投影、别名和简单 WHERE / ORDER BY，且所有列都有一致的 Schema、表和基列来源：

1. 通过 `ALL_OBJECTS` 确认来源为 TABLE。
2. 将已启用且已验证的 P/U 约束按完整成员分组。
3. 主键成员标为 `IsKey`；只有单列主键或单列唯一约束才标为 `IsUnique`。

复合唯一约束不会给成员赋予单列唯一性；只选择主键的一部分也不保证结果行唯一。可空唯一列保持原 `AllowDBNull`，不会被提升为主键或非空。

别名只有在服务器提供真实 BaseColumnName 且目录确认约束时才继承键属性。视图和无法确认的实体来源始终保留未知键属性；确认的实体表即使没有约束，也可以明确报告 `IsKey/IsUnique=false`。

JOIN、DISTINCT、集合查询、表达式、子查询和缺失来源不推断键，`IsKey` / `IsUnique` 保持 DBNull，查询仍可正常执行。`GetSchemaTable` / `GetSchemaTableAsync` 只读取预取结果，不执行目录 SQL。

所有 Reader 模式共用类型和时间描述。数值 precision/scale 来自同一列描述，时间 NumericScale 使用解释后的小数秒位数；LOB 未知长度不伪造为 0，Base* 缺失为 DBNull。有完整来源时，`IsAliased` 比较显示名与基列原文，`IsExpression=false`；否则两者为 DBNull。切换结果集会清空上一结果集的键附加信息。

元数据内部命令允许在元数据操作期间进行本地参数绑定。权限错误不伪装成空表；未证明可恢复的错误会废弃会话。清理失败会更新原异常的连接不可复用标记，旧操作的取消或清理不会关闭重开后的新会话。

## 当前权限与验证限制

`ReservedWords` 需要读取 `V$RESERVED_WORDS`，当前 TEST 返回权限错误 `-5504`。同步和异步都会保留该错误并废弃未确认可复用的会话，不返回空集合，也不使用静态词表冒充服务器词表。`Schemas` / `Databases` 使用现有 SOI 角色可见的系统目录。

本项尚未覆盖其他服务器版本、TLS 环境下的完整元数据矩阵或 EF scaffold。

## 历史实现与验证记录

以下结果属于对应提交和环境，不替代当前发布记录。

- 实现基线为 `27058bc` 加 issue #10 的未提交修改。8 套离线测试共 **1730 项通过、0 失败**：Schema 23、Command 53、Async 151、Diagnostics 85、Type 958、Session 21、Pool 304、Transaction 135。构建使用任务专用 CLI home、单节点和禁用共享编译参数。[命令与原失败记录](../implementation/evidence/I10-metadata-20261015/offline.json)。
- DM 8.1.5.60 实库使用显式 `PlaintextAllowed`，先核实 TEST 身份。15 个可读集合的同步/异步结果一致；ReservedWords 权限拒绝分别验证。自有对象覆盖特殊名称、精度、LOB、复合键和复杂来源，5 个对象均精确清理。[实库记录](../implementation/evidence/I10-metadata-20261015/full.json)。
- Prepare 的 8 项请求均为 opcode 5 / execute=false；副作用函数计数在描述前后保持 1。Reader Default、SchemaOnly、KeyInfo 和组合行为的同步/异步列信息一致。
- 历史失败已保留：元数据参数绑定保护、内部缓存行诊断和合成 fixture 偏移问题均在修复后重测，没有把原失败写成通过。
- PR #22 评审发现视图会被误给已知键属性。原 PR `06269bb` 的 4 项视图断言失败后，修复为只有确认 TABLE 才投影已知键属性，VIEW 和不可确认来源保留 DBNull；没有增加目录往返。[审查记录](../implementation/evidence/I10-metadata-20261015/view-review.json)。
- PR #21 合入后，合并候选 `f68c7d8` 的 Schema 23、Command 72、Async 151、Diagnostics 85 在 .NET 8/9/10 各通过，共 993 项；发布工具 23 项通过。初次本机执行缺少系统 net9 runtime，改用现有 SDK/runtime 目录后通过，没有跨主版本 roll-forward。[命令和结果](../implementation/evidence/I10-metadata-20261015/pr-multitarget.json)。
