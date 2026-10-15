# I10：ADO.NET 元数据发现与描述

## GetSchema / GetSchemaAsync 集合契约

同步与异步使用同一 `DmSchema.Definitions` 集合表、SQL、列类型及投影。异步执行、逐页读取、LOB 默认值物化及 Reader/Command 清理均使用异步 API；内部 Reader 借用顶层 Metadata lease，不另开连接或申请并发顶层 lease。旧同步 filter 不提供异步回退。纯内存集合可直接返回完成任务，仍遵守连接打开与独占会话要求。

集合名按 ordinal 忽略大小写；`null` / 空集合名返回 `MetaDataCollections`。未知集合和超出声明数量的 restriction 抛 `ArgumentException`，验证发生在取得会话所有权和发送 SQL 之前。预取消返回取消任务且不发送请求。

**所有 restriction 均为等值匹配**：`null` 表示不限制；空字符串作为实际值绑定。调用方数组会复制，值不转大写，不移除反引号或双引号，不进行 LIKE 模式解释；`%`、`_`、单引号是实际标识符字符。应传入服务器目录中的原始名称，而不是 SQL 中的引用语法。例如对象 SQL 名 `"MiXeD%_"` 的 restriction 是 `MiXeD%_`。最终相等比较沿用服务器配置的字符串比较规则。所有值通过命名参数绑定，不拼接进 SQL。

返回的对象名称保留服务器原文。无法可靠得出的目录值为 `DBNull.Value`；目录错误（包括权限不足、缺失视图或列）保留数据库异常，不能解释为空集合。此契约不授予额外权限。

下表的 restriction 从左到右对应数组下标 0、1、2；`Restrictions.RestrictionNumber` 使用 ADO.NET 的 1 起始序号。`S` = `System.String`，`I` = `System.Int32`，`L` = `System.Int64`，`B` = `System.Boolean`。

| 集合 | restriction 顺序 | 返回列（名称: CLR 类型）与语义 |
| --- | --- | --- |
| MetaDataCollections | 无 | CollectionName:S、NumberOfRestrictions:I、NumberOfIdentifierParts:I；包含本表全部 16 个名称。 |
| Restrictions | 无 | CollectionName:S、RestrictionName:S、RestrictionDefault:S、RestrictionNumber:I；Default 为 DBNull，表示不自动补充过滤值。 |
| DataSourceInformation | 无 | 标准 ADO.NET 列，详见下文。读取已连接实例的版本与大小写配置，不发送查询。 |
| DataTypes | 无 | 标准 ADO.NET 类型能力列，详见下文。内存类型表，不对每个服务端配置作运行时能力探测。 |
| ReservedWords | 无 | ReservedWord:S；来自当前服务器 V$RESERVED_WORDS。 |
| Tables | Schema、Table、TableType | TABLE_CATALOG:S、TABLE_SCHEMA:S、TABLE_NAME:S、TABLE_TYPE:S、FILLFACTOR:I、SPACE_LIMIT:I、ROW_COUNT:UInt64。来自可见 ALL_OBJECTS；返回 TABLE_TYPE 为 UTAB 或 VIEW，过滤值 UTAB 映射为目录 TABLE，也接受 TABLE。CATALOG、FILLFACTOR、SPACE_LIMIT、ROW_COUNT 未验证，均 DBNull。 |
| Columns | Schema、Table、Column | TABLE_SCHEMA:S、TABLE_NAME:S、COLUMN_NAME:S、COLUMN_ID:I、COLUMN_TYPE:S、COLUMN_SIZE:I、COLUMN_SCALE:I、NULLABLE:S、DEFAULT_VALUE:S、NUMERIC_PRECISION:I、DATETIME_PRECISION:I、CHARACTER_OCTET_LENGTH:I。来自 ALL_TAB_COLUMNS；语义详见下文。 |
| Users | Name | USERNAME:S；来自 ALL_USERS。 |
| Schemas | Name | NAME:S；来自 SYS.SYSOBJECTS 中的 SCH 对象，沿用现有 TEST 的目录权限。 |
| Databases | Name | 与 Schemas 同义，DataTable.TableName 仍为 Schemas；不代表物理数据库，不执行切库。 |
| Procedures | Schema、Procedure、ProcedureType | PROCEDURE_SCHEMA:S、PROCEDURE_NAME:S、PROCEDURE_TYPE:S；来自 ALL_OBJECTS，限独立 PROCEDURE / FUNCTION，不将包成员伪装为独立过程。 |
| Parameters | Schema、Procedure、Parameter | PROCEDURE_SCHEMA:S、PROCEDURE_NAME:S、PARAMETER_NAME:S、PARAMETER_ID:I、PARAMETER_TYPE:S、PARAMETER_SIZE:I、PARAMETER_SCALE:I、DEFAULT_VALUE:S、NULLABLE:S、PARAMETER_INOUT:Int16；来自 ALL_ARGUMENTS 中非包过程/函数。PARAMETER_ID 为目录 POSITION，函数返回值通常为 0 且参数名可能为 NULL；SIZE 是 DATA_LENGTH 字节长度，SCALE 是目录 DATA_SCALE，NULLABLE 未知。INOUT：IN=0、OUT=1、IN/OUT=2；未知为 DBNull。 |
| Indexes | Schema、Table、Name | INDEX_SCHEMA:S、INDEX_NAME:S、TABLE_NAME:S、UNIQUE:S、INDEX_TYPE:S、TABLE_SCHEMA:S；restriction 的 Schema 作用于表所属 Schema。UNIQUE 为 Y/N/DBNull，表示整个索引是否唯一；INDEX_TYPE 保留服务器分类。 |
| PrimaryKeys | Schema、Table | TABLE_SCHEMA:S、TABLE_NAME:S、COLUMN_NAME:S、KEY_SEQUENCE:I、PK_NAME:S；ALL_CONSTRAINTS 与 ALL_CONS_COLUMNS 中 P 约束，每个主键成员一行，KEY_SEQUENCE 是 1 起始 POSITION。允许不提供 restriction。 |
| IndexColumns | Schema、Table、Index | COLUMN_NAME:S、SORT_ORDER:S、TABLE_SCHEMA:S、TABLE_NAME:S、INDEX_SCHEMA:S、INDEX_NAME:S、ORDINAL_POSITION:I；ALL_IND_COLUMNS 每成员一行，ORDINAL_POSITION 为 1 起始 COLUMN_POSITION；SORT_ORDER 保留目录 ASC/DESC。restriction 的 Schema 指表 Schema。纯目录查询，无临时表、DDL 或动态 SQL。 |
| ForeignKeys | Schema、Table、Name | TABLE_SCHEMA:S、TABLE_NAME:S、COLUMN_NAME:S、FOREIGN_KEY_CONSTRAINT_NAME:S、KEY_SEQUENCE:I、REFERENCED_TABLE_SCHEMA:S、REFERENCED_TABLE_NAME:S、REFERENCED_COLUMN_NAME:S、DELETE_RULE:S。restriction 和 TABLE/COLUMN 列始终指引用方（子表）；REFERENCED_* 指被引用方。复合外键按 POSITION 与被引用约束成员配对，缺少可见的引用端信息保留 DBNull。 |

目录集合按 Schema、对象名及成员序号排序。PrimaryKeys / IndexColumns / ForeignKeys 的成员顺序分别使用目录的 POSITION / COLUMN_POSITION / POSITION；不存在将复合唯一约束每一成员推断为单列唯一的规则。

### Columns 精度、长度及 NULL

- `COLUMN_ID` 是目录的 1 起始列序号。`COLUMN_TYPE` 保留原始 DATA_TYPE。
- `COLUMN_SIZE` 对字符类型表示 CHAR_LENGTH（字符数），对 DECIMAL/DEC/NUMERIC/NUMBER 表示 DATA_PRECISION；其它类型（包括 LOB）的逻辑长度尚未统一验证，返回 DBNull。不会把存储字节数冒充字符长度或精度。
- `CHARACTER_OCTET_LENGTH` 只对字符类型返回 DATA_LENGTH 字节数。LOB 长度未知，不宣称固定最大值或当前值长度。
- `COLUMN_SCALE` 直接反映目录 DATA_SCALE，不混入 wire packed scale。`NUMERIC_PRECISION` 仅对数值类型返回目录 DATA_PRECISION；`DATETIME_PRECISION` 对 TIME/TIMESTAMP/DATETIME 类型返回目录 DATA_SCALE（小数秒位数），非时间类型为 DBNull。列声明精度与 CLR 可精确承载的值域是不同概念，写入及读取仍遵循现有 temporal descriptor / profile。
- `NULLABLE` 保留目录 Y/N；未知为 DBNull。`DEFAULT_VALUE` 原样返回目录默认表达式，空/未知保留 DBNull；异步路径对 LOB 表达式用 `GetFieldValueAsync<object>` 物化。

### 内存集合字段

`DataSourceInformation` 返回：CompositeIdentifierSeparatorPattern:S、DataSourceProductName:S、DataSourceProductVersion:S、DataSourceProductVersionNormalized:S、GroupByBehavior:System.Data.Common.GroupByBehavior、IdentifierPattern:S、IdentifierCase:System.Data.Common.IdentifierCase、OrderByColumnsInSelect:B、ParameterMarkerFormat:S、ParameterMarkerPattern:S、ParameterNameMaxLength:I、ParameterNamePattern:S、QuotedIdentifierPattern:S、QuotedIdentifierCase:System.Data.Common.IdentifierCase、StatementSeparatorPattern:S、StringLiteralPattern:S、SupportedJoinOperators:System.Data.Common.SupportedJoinOperators。产品版本来自当前握手；未经规范化的版本列为 DBNull。

`DataTypes` 返回：TypeName:S、ProviderDbType:I（DmDbType 枚举）、ColumnSize:L、CreateFormat:S、CreateParameters:S、DataType:S（CLR 类型全名）、IsAutoincrementable:B、IsBestMatch:B、IsCaseSensitive:B、IsFixedLength:B、IsFixedPrecisionScale:B、IsLong:B、IsNullable:B、IsSearchable:B、IsSearchableWithLike:B、IsUnsigned:B、MaximumScale:Int16、MinimumScale:Int16、IsConcurrencyType:B、IsLiteralsSupported:B、LiteralPrefix:S、LiteralSuffix:S、NativeDataType:S。

类型表保留历史标量类型并补充 DATE、TIME、TIMESTAMP、带时区类型及 BLOB/CLOB 名称；VARCHAR 不再误报固定长度。时间的 MaximumScale 是类型声明的小数秒上限，DATE=0、TIME=6、DATETIME/TIMESTAMP=9，带时区类型对应相同上限。TIME 的 CLR 名称跟随当前 DbTimeToTimeSpan 配置。高精度 timestamp 的写入仍受既有 negotiated profile 和精确值限制；类型表不是绕过这些限制的授权。本文不宣称所有数据库扩展类型和别名已列全。

## Reader 描述路径与验证

当前源码开放单条 `CommandType.Text` SELECT（包括绑定参数、引号标识符、注释和查询表达式）。使用词法扫描检查语句边界与参数，拒绝批量、过程、WITH、SELECT INTO、括号不平衡及未绑定参数。拒绝发生在分配服务器句柄前；不是凭 SELECT 字符串前缀放行。

`SchemaOnly` 发送 prepare 的 opcode 5 / execute=false，验证服务器返回查询列后构建无行缓存的 Reader，不执行业务 SQL。`HasRows`、同步/异步 `Read` 和 `NextResult` 恒为 false；仍须关闭 Reader 以释放描述句柄与会话所有权。参数值不通过该路径上传或执行。SchemaOnly 的 `IsReadOnly` 为 DBNull，因为 prepare 响应没有可更新性字段。

`KeyInfo` 在同一 lease 下串行执行描述、目录预取、业务执行。目录 Reader 在业务执行前关闭，不在活动业务 Reader 上追加查询。单独使用 KeyInfo 正常取数；与 SchemaOnly 组合只描述。

键推断仅适用于可确认的简单单表直接列投影、别名、简单 WHERE / ORDER BY，且所有列具有相符的 schema/table/base-column 来源。目录查询先通过 `ALL_OBJECTS` 确认来源为 TABLE，再将已启用且已验证的 P/U 约束按完整成员分组：主键成员标为 IsKey；仅单列主键/唯一约束标为 IsUnique。复合唯一约束不赋予成员单列唯一性；选择主键的一部分也不保证结果行唯一。可空唯一列保持原 AllowDBNull，不被提升为主键或非空。

别名只有在服务器提供真实 BaseColumnName 且目录确认约束时才继承键属性，显示名不会被当成基列名。视图及无法确认是实体表的来源始终保留未知键属性；确认的实体表即使无约束，也可明确报告 IsKey/IsUnique=false。JOIN、DISTINCT、集合查询、表达式、子查询和缺失来源不推断键，IsKey/IsUnique 保留 DBNull，查询仍可正常执行。GetSchemaTable/GetSchemaTableAsync 只读取已预取的结果，不执行目录 SQL。

所有 Reader 模式共用类型与 temporal descriptor；数值 precision/scale 继续来自同一列描述，时间 NumericScale 使用解释后的 fractional scale。LOB 的未知长度不伪造为 0；Base* 缺失为 DBNull；有完整来源时 IsAliased 比较显示名与基列原文、IsExpression=false，否则两者为 DBNull。切换结果集清空上一结果集的键附加信息。

元数据内部命令允许在 Metadata lease 下进行本地参数绑定；其服务器错误恢复仍使用已有服务器版本和错误号码白名单。权限错误不伪装成空表，未证明可恢复的错误废弃捕获的会话。清理失败会更新原异常的连接不可复用标记；旧操作的取消或清理不关闭重开后的新会话。

## 当前 TEST 权限限制

`ReservedWords` 需要读取 `V$RESERVED_WORDS`，当前 TEST 返回服务器权限错误 -5504。同步/异步都保留此错误并废弃未确认可复用的会话，不返回空集合或用静态词表冒充当前服务器词表。该限制明确保留，未扩大账号授权。`Schemas` / `Databases` 使用现有 SOI 角色可见的系统目录。

本项不包含 EF scaffold 或其它服务器版本的验收；元数据实库验证使用 .NET 10，合入 #12 后的三个目标离线回归见下文；源码改动尚未包含在已发布的 0.1.0-preview.1 包。


## 本次实现与验证记录

候选身份：`27058bc` 加本工作区 issue #10 的未提交 diff。产品与新测试由不同执行者分工实现，再交叉进行独立代码审查；独立验证者执行 Schema/Command 离线测试与实库探针，主执行者完成其余受影响回归。实库记录仅属于新 W 驱动，没有把官方驱动或恢复版结果混用。

验证配置统一为可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`；构建/测试参数均为 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。

离线命令为 `dotnet test tests/W.DmProvider.<Suite>Tests/W.DmProvider.<Suite>Tests.csproj` 加上述参数；执行的套件为 Schema、Command、Async、Diagnostics、Type、Session、Pool、Transaction。8 套共 **1730 项通过、0 失败**（Schema 23、Command 53、Async 151、Diagnostics 85、Type 958、Session 21、Pool 304、Transaction 135），退出码均为 0；[命令与原失败记录](../implementation/evidence/I10-metadata-20261015/offline.json) 保留本轮证据。覆盖成功分页和异步 close 等待、分页中取消、Close/重开竞争、预取消、描述协议与无行导航、清理失败、主键/唯一约束分组及现有类型/会话/事务回归。

实库入口与命令见 [SchemaProbe README](../../tools/SchemaProbe/README.md)。先逐连接核实 WDM_PROVIDER_TEST 身份与 Schema；显式 PlaintextAllowed 使用现有共享 TEST profile，没有读取管理凭据或改变授权。服务器 8.1.5.60：15 个可读集合同步/异步结果相同；ReservedWords 权限拒绝已分别验证。自有带引号表、view、函数、复合外键覆盖名称 `%` / `_` / 单引号、DECIMAL(18,4)、TIMESTAMP(6)、CLOB/BLOB、复合 PK、单列和复合 unique 约束、NULL、别名与复杂来源。

Prepare 的 8 项表征请求均为 opcode 5 / execute=false；副作用函数的实际执行正对照先将计数改为 1，全部描述与 SchemaOnly 检查后仍为 1。Reader Default、SchemaOnly、KeyInfo、组合行为的同步/异步列信息一致，核心 facets 与普通 Reader 一致。精确记录的 5 个本次对象均已清理，服务器剩余数为 0。原始正向结果及权限限制见 [实库记录](../implementation/evidence/I10-metadata-20261015/full.json)。

修复过程保留了失败原因：受限目录最初触发 Metadata lease 参数绑定守卫；现已允许该用途并保持所有权检查。原 Diagnostics 回归发现内部缓存行/结束读取多报两个网络操作，已恢复内部读取的条件诊断，并增加同步/异步断言。新 synthetic fixture 曾将列名长度放错偏移，已修为协议 offset 24；清理失败测试改为验证 Closed/Broken 均不可复用且传输关闭，匹配现有同步/异步清理契约。原记录没有被当作通过结果。

未覆盖：其他服务器版本、TLS profile 的本项完整元数据矩阵，以及 EF scaffold。已有类型/TLS产品默认策略未改变。


### PR #22 review 修正：视图的未知键属性

原 PR `06269bb` 的目录查询仅取得约束，未区分视图与真实无约束表。新增实库回归在原产品 DLL 上复现同步/异步 KeyInfo 与 SchemaOnly|KeyInfo 的 4 项视图未知键断言失败。修复将对象类型与约束放在同一目录查询中：只有确认 TABLE 才投影已知键属性，VIEW 和不可确认来源保留 DBNull；LEFT JOIN 保留真实无约束表的目录行。这没有增加目录往返，也不改变可空性。

新增离线覆盖 VIEW/SYNONYM/不可见来源，即使错误携带约束也不产生已知键声明；真实无约束表保持 false/false。独立代码审查与行为验证记录见 [view-review.json](../implementation/evidence/I10-metadata-20261015/view-review.json)。


PR #21 合入后，PR #22 同步 `main@83c492e`，保留多目标发布工具与 runtime 安装，新增 SchemaTests 也覆盖 net8.0/net9.0/net10.0。合并候选 `f68c7d8` 的 Schema 23、Command 72、Async 151、Diagnostics 85 在三个真实 runtime 各通过，共 993 项；发布工具 23 项通过。初次本机执行缺少系统 net9 runtime，切换到现有 issue12 SDK/runtime 目录后通过，未使用跨主版本 roll-forward。[命令和结果](../implementation/evidence/I10-metadata-20261015/pr-multitarget.json) 保留该环境失败及覆盖边界。
