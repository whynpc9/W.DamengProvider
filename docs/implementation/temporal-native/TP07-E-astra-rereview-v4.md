# TP07 E-v4：N列窄修与真实离线DDL入口静态接受

2026-10-09。结论：**E_v4_scoped_static_accepted_for_Low_build_and_offline_DDL_gate / live_schema_and_runtime_pending**。未发现本次差异的确定编译/API/行为阻塞。只读对照设计 `TP07-E-owned-model-DDL-design.md`（SHA256 `a1356b426a2e6237f3e3324a09584692a09a11f56c2ea8d7fe39fd81e9f907ce`）、E-v3快照、E-v4两C#文件及policy、固定EF10.0.12 API和生成器源码；未运行SDK、测试或DB，未修改实现/旧证据。

## 1. 唯一live模型变化与范围

`TemporalContext.OnModelCreating`只在N属性增加 `HasColumnType("VARCHAR(32)")`，保留原MaxLength32、IsUnicode(false)、nullable及列名。native/DATE_nonmidnight原Ignore N逻辑保持。既有时间映射/precision、主键、seed/default输入和所有其它case文件不变。

`TemporalContext.DatabaseSpec`抽出原Create中的纯转换：default7_fixed6→facet6，DATE_nonmidnight→native，其余Shape不变。live Create与offline共同调用；与原 `@case.Shape`转换等价，因为runtime.ModelSpec.Shape本来就取该case.Shape。没有为fixed6清Scale、降runtime默认7或另建selftest模型。

policy相对E-tools-v5只有 `E_template_hashes`中的Program/TemporalContext两项和派生aggregate变化；profiles/groups/cases/required_counts等所有其它字段相同。因此shared180/TLS158共338个live case及原期待保持；新增offline case不计入live数。common、W、EF provider/mapping及其它七个E源没有变化。

## 2. offline入口与零DB路径

`--offline-generated-create`存在时，在构造RuntimeHarness/OwnedObjects和live逻辑之前分流，要求值严格为true且没有group；错误模式不会掉回live。仍以实际新E startup执行CandidateGuard.Load/VerifyRuntime，核编译来源/source/locks/assets、实际W/EF DLL/MVID/deps。并未降低为typeof检查。

TemporalContext的optional OfflineIoState只影响连接字符串与interceptor选择；offline条件表达式在调用guard.BuildConnectionString之前短路，直接用固定nonsecret synthetic builder在内存创建连接配置，没有测试连接环境读取、secret/TLS文件读取或字符串输出。UseDameng、OnModelCreating、cache key和GeneratedCreate及其真实DI路径保持同一实现；正常runtime参数缺省null，仍走原guard与audit。

两种blocker覆盖EF sync/async ConnectionOpening，以及Reader/Scalar/NonQuery Executing，先记录attempt再抛固定工具错误；本分支没有主动Open/Execute、EnsureCreated/Migrate/SaveChanges、OwnedObjects、wrapper或物理身份SQL。实际factory连接都经W类型/assembly核验，Observe与Evidence检查Closed；计数最终必须全0。离线没有安装wire send hook，故不能把audit.Sends为空单独称协议层no-send证据；本门零DB依据是受审纯生成调用链、before-open blocker、零attempt和实际Closed状态。

只读核对固定EF10.0.12 Relational XML，新增ConnectionOpening[Async]、Reader/Scalar/NonQuery Executing[Async]签名吻合；ColumnOperation的IsUnicode/Precision/Scale与CreateTableOperation集合成员存在。W关闭连接的State读取在Dispose后仍返回Closed，无为此读状态而打开连接的分支。未发现重名局部变量或DT/DTO条件式的隐式类型转换问题：默认/生成literal使用明确object分支，seed switch为object目标类型。

## 3. 28个实际模型门

每个profile调用执行14个固定shape，shared/TLS两个进程合计28：roundtrip、facet6、store6、default7_fixed6、concurrency、native、DATE_nonmidnight、constant_default、HasData、sql_default、NULL_default、inline、update_expression、clock_default。profile在这里表示模型输入，未观察物理server。runtime和DatabaseSpec分别产生真实model记录；sql_default的输入也由实际model mapping.GenerateSqlLiteral生成。

每项调用真正compiled `TemporalContext.GeneratedCreate`，经IDesignTimeModel→IMigrationsModelDiffer→provider IMigrationsSqlGenerator取得operation与command，保存实际service assembly/hash/MVID、生成SQL文件/hash/TransactionSuppressed。未复制预期SQL当结果、替换generator、使用假DbContext或另写fake生产路径。

断言核实际CreateTableOperation的表/列/PK(ID)、ID int非nullable与ValueGeneratedNever；N的VARCHAR(32)/MaxLength/Unicode/nullable/default；D/Z的实际6/7 store type、nullable和默认语义；native列集合与DATE/TIME/INTERVAL类型。default7_fixed6 runtime仍核Precision7，DDL转facet6；DATE runtime/noN与native建表变换均保留可读模型证据。

HasData/inline/update_expression只允许实际一个InsertDataOperation、四列和一行，逐值核ID1、N=seed、DT ticks/kind与DTO本地ticks/offset；其它shape无seed operation。constant/default SQL/NULL/clock分别核对应operation DefaultValue/DefaultValueSql，再查实际SQL列行。默认/seed相关时间helper未更改。

实际SQL检查与已保存失败CREATE格式及固定Dameng generator源码相符：generator直接采用operation.ColumnType并输出NULL/NOT NULL，N行要求精确一个 `"N" VARCHAR(32) NULL,`，原 `VARCHAR2(32 CHAR)`不会通过。ID/PK/时间/native声明与实际seed INSERT数量同时核验，不是仅检查整份SQL含VARCHAR字符串；生成命令还限制数量与长度。所得SQL和operation仍需Low真实运行回读，本文不宣称已产出28份结果。

输出为新目录中的offline-generated-create.json及SQL文件，保存14项/profile的明确caseID/模型/operations/服务身份和零IO证据；runtime/candidate/full C/live_DATA_LENGTH_32_verified始终false。模型/identity/SQL异常导致offline失败，不会被解释成DB能力拒绝通过。实际服务器DATA_LENGTH32仍须新live CREATE与原common目录守卫证明。

## 4. 冻结身份

Low E-v4 `source-manifest.json` SHA256：`0d469ad85460aa090532f443c11478cc565471204db6694a7ff4448349873ea9`；九源aggregate：`4f102cedfe0e4ae27b3d3dc764e253939537e0a3443a7668efed4e9617ddb3ef`。九个live与snapshot本轮全部逐项匹配；相对E-v3仅以下两项改变：

| 文件 | SHA256 |
| --- | --- |
| TP07TemporalRuntime/Program.cs | `f2fcb0e4577f8e4ab43424f6bba12fb4fe854e7fdc8f9a7ce1b50e2cfb5ed248` |
| TP07TemporalRuntime/TemporalContext.cs | `0d0f38393096a83f1ef0b57c8a89823fdf2d6120d3e21f77fa139bc7c7ea6a2e` |
| ef-cases.json | `f1a6600ca9d0c9ade1edf4ce64c7d9ea2fa08cfd4e12a61026eab8878662aad5` |

E-tools-v6 toolpack SHA `3f255d1ce5cbe86e3e5cdeb29df84d51a36a4076334070f565a04383e9228011`，source manifest SHA `9fb6f393e8eb915db0cca6169470100098d7bd249a4acc22a950d88e8e842456`。八项工具包live字节匹配；相对v5仅上述policy hash变化，其余七项不变。原SQL/已失败build/runtime与legacy ownership证据不回写。

## 5. Low与恢复边界

root可安排新E source/preparation输入、实际依赖/locked build身份与两个profile的真实offline入口；Low须记录每进程自然exit、实际14/14模型完成、SQL文件hash、候选身份、零IO计数和所有factory Closed，不以Python语法检查或静态字符串测试代替该C#执行门。本文未执行这些门。

离线报告不是runtime批准，也不修复旧残留表。当前残留对象检查/恢复仍由单独scope负责；其新空表证明或恢复工具不能借本文升级为已清理。恢复与offline模型门通过后，root才另批fresh E运行，原common结构/owner检查继续严格执行。D仍等待实际E/L0前置门。此前R01/R02/R03限定静态结论保持，不将本次N列修复扩大为时间能力或完整C接受。报告完成后停止写入。
