# TP07 E owned模型：N列窄修与真实离线DDL门

2026-10-09。状态：**narrow_design_ready / implementation_and_compiled_gate_pending / no_DB_or_recovery_authorization**。本轮只读现有TemporalContext/Program/guard、模型registry及已保存检查报告；未执行SDK、测试或DB，未修改产品、common、旧证据或遗留对象。

## 1. 已证问题与唯一模型修复

保存的检查报告 `TP07-E-owned-inspection-v2-runtime/inspection.json` SHA256 `7f76c57340774a76318706bc4c60fe0e2795d7508ba509e59a6fe8d2ffde5aff`中，辅助N列实际为VARCHAR2，DATA_LENGTH128、CHAR_LENGTH32、CHAR_USED=C；原生成CREATE采用VARCHAR2(32 CHAR)。这与common对owned辅助列VARCHAR32要求DATA_LENGTH32不一致，导致真实CREATE返回成功后的结构确认失败。该事实不等于整个检查流程或cleanup已成功。

授权修复只在实际 `TemporalContext.OnModelCreating` 的N属性追加显式 `.HasColumnType("VARCHAR(32)")`，保留HasColumnName("N")、HasMaxLength(32)、IsUnicode(false)及原nullable语义。native和DATE_nonmidnight仍按原规则Ignore N，不增加另一种模型。

不修改OwnedObjects的长度检查、W、EF provider/mapping、时间Scale/Precision/值、PK、338个live case及其接受语义；不把128改成允许值来绕过结构守卫。离线门证明的是实际SQL声明符合预期，**不是本机/所有实例实际DATA_LENGTH已为32**；live catalog检查仍由原common完成。

遗留owned表仍由root指定的检查/恢复链单独处理。本文既不执行清理，也不据新模型推断旧表已变更或可安全DROP；E/D runtime仍受恢复与实际前置门约束。

## 2. 最小入口与真实候选身份

采纳High提出的有限接口：现有E Program增加pairflag `--offline-generated-create true`，识别后在构造RuntimeHarness/OwnedObjects、登记ledger或任何live DB调用之前进入独立selftest分支。参数只保留所需profile/candidate/capabilities/output，group不得同时出现；错误/false模式不得fallback到live入口。flag缺省时原live执行路径与registry保持。

selftest仍运行**本次新编译TP07TemporalRuntime程序集**，首先调用原 `CandidateGuard.Load/VerifyRuntime`，按外部startup/source/lock/实际DLL/MVID/deps核同W与EF候选。它们读取已批准的manifest/package/source证据，但不读连接secret或打开DB。不能改成只检查typeof、固定字符串“same package”或跳过真实startup guard。

TemporalContext可增加仅internal的offline bool/固定连接override，作用只限连接配置和阻断interceptor选择：offline分支直接在内存构造固定、非秘密synthetic connection string，**必须在调用guard.BuildConnectionString之前短路**。后者会读取真实DAMENG_TEST_CONNECTION_STRING并按TLS选择证书路径，故不能在离线分支先调用再替换。不要读取/备份/改写全局测试连接环境变量，不接受用户任意连接串CLI参数，不读取secret/TLS私钥，不序列化synthetic连接串。

连接改变不建立另一套provider/model/generator：继续同一UseDameng、同一TemporalContext类型、OnModelCreating、ModelCacheKeyFactory及GeneratedCreate方法，且使用实际provider DI服务。声明的shared/TLS是模型case维度，offline不声称观察了相应物理server profile。正常runtime仍使用原guard连接构造。

## 3. 零数据库动作约束

只在现有Program/TemporalContext文件放必要的小型阻断器，不增加项目、通用框架、mock provider或复制SQL生成逻辑。可分别使用DbConnectionInterceptor和DbCommandInterceptor，共享有限计数：

- sync/async ConnectionOpening在真正打开前递增attempt并抛固定工具异常；Reader/Scalar/NonQuery执行回调同样在调用前失败。
- 正常selftest过程不得主动调用Open/Execute来“试一下阻断器”；只生成模型/operation/command文本。所有opening/execution attempt计数须为0，opened/completed计数为0，实际factory连接在每个case结束均为Closed。
- 明确禁止OwnedObjects构造/注册、OpenTestConnection、Database.OpenConnection、EnsureCreated、Migrate、SaveChanges、ExecuteSqlRaw、command.Execute等。EF阻断器不能声称覆盖任意裸ADO绕行，因此源码中本分支也不能出现这种调用。
- 仅GetDbConnection的实例类型/assembly检查可以保留，不能调用VerifyOpenedConnection（它执行身份SQL）。阻断器异常、模型构造/生成失败、身份失败一律selftest失败并保留安全证据，不标“预期拒绝通过”。

独立Low以公开环境白名单启动新E DLL，不通过TEST wrapper，不注入连接环境变量。报告的database_accessed=false必须由上述执行路径、零计数和Closed状态共同支撑，不能只有硬编码布尔。

## 4. 真实EF生成路径与28项固定矩阵

每case使用唯一固定synthetic table/PK标识符、实际compiled TemporalContext，调用现有：

`IDesignTimeModel → relational model → IMigrationsModelDiffer.GetDifferences(null, …) → IMigrationsSqlGenerator.Generate`，由 `TemporalContext.GeneratedCreate`完成。不复制预期CREATE文本作为“生成结果”，不另建selftest context、替换mapping/generator或为测试加入“旧/新N类型”开关。

High建议的14形状在两个模型profile各运行一次，共**28个独立offline case**：

`roundtrip, facet6, store6, default7_fixed6, concurrency, native, DATE_nonmidnight, constant_default, HasData, sql_default, NULL_default, inline, update_expression, clock_default`。

每个输入及预期在source中冻结，offine case ID与338个live ID分开；不得将28加入live统计或用它们替代shared180/TLS158运行。无需枚举所有live日期向量来证明一个N列声明，但必须覆盖这些实际模型分支。

**runtime模型与建表模型必须分别记录。** 原Create对default7_fixed6使用facet6建表、runtime继续默认7；DATE_nonmidnight使用native表结构、runtime仅DATE映射。selftest须走相同变换，不能直接在default7_fixed6 runtime模型上调用GeneratedCreate后把生成7列谎称fixed6。必要时在现有文件内抽出一个供live Create和selftest共同调用的纯ModelSpec变换；保持原变换字节语义，不复制两个会漂移的规则。另读取runtime模型facets证明显式/默认7及DATE路径未被自测降级。

SQL默认值输入按实际mapping service的GenerateSqlLiteral取得；NULL/clock默认沿原固定常量NULL/CURRENT_TIMESTAMP。使用原合成普通日、shared七位/tls六位精确值和合法offset，HasData/inline/update_expression仍通过真实HasData模型产生InsertDataOperation；不是手写等价seed INSERT来通过。

## 5. 必需独立断言与输出

同时观察真实模型、MigrationOperation和最终MigrationCommand，避免只用字符串contains：

| 对象 | 最小检查 |
| --- | --- |
| N（存在的形状） | model store type及CreateTableOperation.ColumnType精确VARCHAR(32)，MaxLength32、Unicode=false、nullable语义不变；实际CREATE只有一个N声明且为VARCHAR(32)，不含N的VARCHAR2/CHAR长度后缀 |
| PK/ID | 同case确切table/PK、主键仅ID、ID非nullable且ValueGeneratedNever；无新增schema/FK/CHECK/identity/sequence |
| D/Z | 实际模型有效precision与DDL模型精度分别符合输入，type为预期TIMESTAMP(n)/DATETIME(n) WITH TIME ZONE，nullable/default不漂移；不修改时间参数或绑定观察 |
| native/DATE | N确实不存在；native保持ID/A DATE/T TIME(6)/I INTERVAL DAY(9) TO SECOND(6)，DATE_nonmidnight的runtime与DDL结构差异沿原变换记录 |
| defaults | constant_default、sql_default、NULL_default、clock_default分别核真实operation DefaultValue/DefaultValueSql与最终SQL；不能把它们合并成同一种literal路径 |
| seed | HasData/inline/update_expression的实际InsertDataOperation表/列/ID1/N=seed/D/Z值与固定输入一致，并核真实生成INSERT；其它形状不得凭空多seed operation |
| command列表 | 一份真实CreateTableOperation、有限InsertDataOperation，实际命令文本/hash/TransactionSuppressed保留；不执行，不把SqlGenerator成功当服务器接受 |

仅对N声明作精确有界匹配，不全局禁止其它SQL中的CHAR字样或对整份SQL任意去空白。原错误VARCHAR2(32 CHAR)会由这组模型/operation/SQL断言自然拒绝，不需要建立假的负例模型。高层断言遇到陌生输出明确失败，不能在selftest内重写生成SQL。

新输出 `offline-generated-create.json`与有限生成SQL文件位于全新output，包含task/模式、28项case IDs、模型/DDL spec、实际服务/连接/assembly身份、operation快照、command源码/hash、零open/execute计数、Closed状态及实际exit；保留 `runtime_accepted=false/candidate_acceptance=false/full_EF_C_accepted=false`，不得包含ownership ledger、secret、任意异常Message或连接串。原值都是固定合成数据，可为审阅保存，不输出认证配置。

独立消费者须以固定28期望重建基本字段/数量，检查actual C#输出/SQL hash、候选build身份和零DB收据；仅Python重新写一份SQL regex并跑fixture，不证明真实TemporalContext接线。该门在新E真实compiled DLL上由Low执行；源码审阅时不得写成已通过28/28。

## 6. 变更与交付范围

实现只在原E九文件集合中的TemporalContext.cs和Program.cs，外加已授权ef-cases模板hash pin更新；若纯变换helper放现有文件，保持live Create输入/SQL行为除N外一致。不改common、W、EF mapping/provider、其它时间case、DDL cleanup policy或旧失败。

High停写后新E九源freeze及ef-cases pin同步，Astra先核N唯一业务变化和selftest路径；外部准备source引用需绑定新E manifest，不能继续指旧九源hash。Low新source/lock/build收据和真实offline28门通过后，仍须先解决当前遗留对象恢复，再由root另行授权fresh E实库。新CREATE必须重新接受原common目录/owner确认，offline SQL门不能替代这一点。

本设计已审High提议的flag/28模型/固定synthetic连接/拦截器接口；具体源码尚待交接，不提前接受实现或构建。本文不参与旧表清理，不自动激活E/D或完整C。报告完成后等待High限定源码复核。
