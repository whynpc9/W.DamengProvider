# TP07 最小 EF 实库 harness 分组设计

> 阅读提示：这是历史 EF 实库工具设计，不是当前执行指南或运行授权。固定候选、状态和审批仅属于当时；当前驱动行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。状态：**design_ready，runtime_not_accepted**。补充 [TP07 主设计](TP07-astra-design.md)，不替代其 EF-M01/M02、EF-R01…R06 合同。本次只读 pinned source/bridge，未运行 SDK、测试或 DB，未改产品。先交付小型 literal probe，再实施 EF 运行组与 CLI/迁移组；不等待完整套件才能发现 literal 持久化失真。

## 固定依赖与文件归属

EF base `5863d3755dbd2d333ac359bf1c0a789762adbaed`，tree `b8f99168225a3742675ae594e8735f481a591fd5`，archive SHA256 `668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8`。只消费 `.local/temporal-native/TP07/v1/base-source`、已冻结 bridge 与随后冻结的 mapping overlay，在新的 Low replay 中组合。不得修改原 EF checkout、base、已冻结 overlay 或 W 产品。

W 固定 TP06 包 `0.1.0-temporal.20261008144915`，nupkg SHA256 `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL SHA256 `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。每个项目只允许 W **精确 PackageReference**；EF provider 可 ProjectReference 隔离源码项目，不能 ProjectReference W 源或引用官方驱动。沿 pin SDK `10.0.401/latestPatch`，实际 SDK 留证；EF 包解析锁为 `10.0.12`，不利用中央范围升级。

新增模板根 `tools/DownstreamAcceptance/TP07/runtime-templates/`，重放到隔离 EF 根的 `test/TP07…/`。下列是独立项目，不引用原 FunctionalTests/Specification 项目，不调用原 all/admin launcher。

| 阶段 / owner 范围 | 新文件（相对模板根） | 依赖与目的 |
| --- | --- | --- |
| L0，已分配 literal owner | `common/{CandidateGuard.cs,SafeEvidence.cs,OwnedObjects.cs}`；`TP07LiteralProbe/{TP07LiteralProbe.csproj,Program.cs,LiteralCases.cs,LiteralOracle.cs}`；可附自有 README/manifest | console，net10.0，EF provider ProjectReference + exact W。common 文件由此 owner 独占，后续组只 link 消费 |
| E，后续 runtime owner | `TP07TemporalRuntime/{TP07TemporalRuntime.csproj,Program.cs,TemporalContext.cs,Models.cs,CommandAudit.cs,RoundtripCases.cs,MutationCases.cs,SixPrecisionCases.cs,GeneratedValueCases.cs}` | 同最小依赖，link common 与已审 oracle；承载 R01–R04，不引用原完整测试树 |
| D，后续 design owner | `TP07TemporalDesign/{TP07TemporalDesign.csproj,Program.cs,DesignContext.cs,DesignContextFactory.cs,ScaffoldCases.cs,GuardedMigrationCases.cs,MigrationGuardSql.cs}`，以及本项目专用 `Migrations/` | 同 EF/W 依赖，加 exact EF.Design10.0.12；命名连接 host 使用 `FrameworkReference Microsoft.AspNetCore.App` 提供 Generic Host/configuration，记录该 shared framework 身份；无新增宽范围 EF 测试包 |
| 独立执行/验收 owner | 新 `tools/DownstreamAcceptance/TP07/runtime-{run,validate}.py`、`runtime-cases.json`、`test_runtime_contracts.py` | 负责 prepare 后模板重放、逐进程身份、固定 case 集、退出码/独立 oracle/清理判定。不得抢改既有 prepare、mapping 或 common 文件 |

第三组生成的 scaffold 源和 migration 源位于该候选专用生成目录；用小型派生 project 编译，仍 link guard，不新增全量套件。生成代码/SQL在执行前另封 source hash，不能拿其生成前的 freeze 冒称包含生成结果。每个新增项目先解析一次受审 lock，再 locked restore；其他原锁不因这组随意刷新。

## L0：先持久化真实生成 literal

入口：`--profile shared|tls --candidate-manifest <path> --output <new-empty-directory>`。凭据只读现有 wrapper 注入的 `DAMENG_TEST_CONNECTION_STRING`，不接受连接串 CLI 参数，不自行读 secrets。用 typed W builder 加显式 profile transport，再 `UseDameng(string)` 创建 context；从 `context.Database.GetDbConnection()` 证明生产 relational factory 实际返回 W。

API 链固定为 `context.GetService<IRelationalTypeMappingSource>()` → `FindMapping(IProperty)` → `mapping.GenerateSqlLiteral(value)`。模型分别定义默认 DT/DTO、显式六位、裸 store type，不用手工拼接“等价”literal代替生成结果。不同 model shape/nonce/profile 采用独立 context 类型或完整 `IModelCacheKeyFactory` 键（含 designTime），避免模型缓存串用。主日期用固定合法普通日，DTO offset330；极值在 R01/R04 完整门继续覆盖。

| 固定 case ID | shared 必跑输入与含义 | TLS 范围 |
| --- | --- | --- |
| `LIT-DT7-{tick1,1234567}`、`LIT-DTO7-{tick1,1234567}` | 默认 mapping 生成 `.0000001`、`.1234567`；持久化至显式7列，验证真实 seventh | 不以 literal 绕过本轮 TLS 七位写关闭；此组列为 profile 外，不记成功 |
| `LIT-DT6-micro`、`LIT-DTO6-micro` | `.123456` 精确控制，model/store facet6 | 两项必跑 |
| `LIT-BARE-{timestamp,datetime,tz}-{micro,tick1}` | 裸 TIMESTAMP、DATETIME、DATETIME WITH TIME ZONE；先核冻结 mapping 策略。六位策略的 micro 精确生成/持久化，tick1 应在 GenerateSqlLiteral 本地抛 OverflowException，无返回 literal、无业务写入且 fresh rows0；其他已批准策略再按其真实声明/保存合同验 | 初轮不扩 TLS bare 合同；另需 root 明确 case 才可加入 |

每项实际执行 generated literal 的 INSERT，然后用**新连接**读持久行。另设明确 Scale7/6 的 W 参数化 ADO 控制，标记为 `ado_control`，不能冒称 EF SaveChanges 成功。DT/DTO wall ticks/offset、原始 temporal bytes/独立整数解码、已接受 FF9 文本和行数交叉一致；禁止用一次 CAST projection 当持久化证据。可复制 TP06 `Observations.cs` 已审普通 row/raw/FF9 的最小子集并固定原 source SHA，保持 raw 在 getter 前采、合法 width/type 后输出、整数解析、原 offset；不得 import 整套 consumer 或用 CLR parse/格式化后的值作为独立 oracle。

输出必须分别表示 `generated_literal_match`、`persisted_exact`、`ado_control_exact`、`catalog_contract`。默认7 literal 任何丢位、异常或 oracle 不一致均 L0 硬失败；保存安全证据、清理并停止后续昂贵组，不能改输入成整微秒、改成参数 SQL、改 parser 或忽略失败。bare 按冻结策略分支：六位策略 tick1 的预期本地 Overflow 是通过的拒绝合同，应保留生成调用/精确异常类型、无返回 literal、业务写入0与 fresh rows0 收据；persisted 字段标不适用，不能为了观察损失另行拼接或执行 unsupported literal。真实声明/保存行为仅对允许生成的输入 characterization；尚未决定的语义标 `unverified`，不得默认等价7或宣称 supported。任何实际静默损失仍失败，不能把“已观察”改写成成功。

先 bridge baseline 运行可辅助定位，但最终 L0 必须消费最终 mapping overlay；任何 generator 改动都重新 freeze、fresh run。parent 已交付 P1 基线参数观察，不能替代本门：DT/DTO 的模型 precision7 不自动证明实际参数 Scale7，更不能证明 server literal7。

## E：最小 EF 实际运行组

runtime 接口追加 `--group roundtrip|mutation|six|generated`。case manifest 固定精确输入及适用 profile，不接受命令行任意 SQL/case 或 producer 的 `expected` 替代独立判定。client assigned key/`ValueGeneratedNever` 优先，避免把 IDENTITY 等无关范围引入普通往返。

| 原门 | 真实 API / 最小实现 | 强断言 |
| --- | --- | --- |
| R01 | `SaveChanges`、`SaveChangesAsync`；新 context `AsNoTracking` sync/async query；同 context 后续只改 Value/NULL，实际参数化查询 | DT/DTO min/min+tick、max/max−tick/max−9ticks、tail1…9 按 TP07 已定域；DT ticks，DTO localticks+offset；本次 parameter facet/reader 与 fresh oracle。shared7；TLS 对应可表示六位集合，不能将七位向量截尾后同名通过 |
| R02 | tracked UPDATE；时间列 `IsConcurrencyToken`；两 context 读同版本，一方改变1tick，另一方 stale `SaveChanges`；`BeginTransaction`/rollback | 本次 original/current 时间参数真实绑定，stale 必须 `DbUpdateConcurrencyException`；fresh row/value/schema不变；事务内没有 DDL。TLS用可表示一微秒变化，标清与shared一tick不同 |
| R03 | `HasPrecision(6)`、`HasColumnType(...(6))` 与 scaffold6模型分别 SaveChanges；typed NULL；DateOnly/TimeOnly/TimeSpan最小非NULL/NULL实际 SaveChanges/query | 精确微秒成功，非整微秒明确拒绝且业务Execute0/fresh不变；六位snapshot不漂成7。TLS default7能力拒绝必须采用实际证明 advisory/unfixed 的绑定，或服务器可建且实际 describe 确认的 fixed7；记录 EF 生成参数请求7及本次实际 metadata/binding，不能因DDL失败冒充绑定拒绝。可靠 fixed6 对整微秒请求7可合法选择 wire6 并成功，这是 TP-A05 的 fixed precedence，不算静默回退；fixed6 非整微秒 Overflow 只证明量化拒绝，不证明七位能力关闭 |
| R04 | 实际 migration `DefaultValue`、`HasData`；SQL default及更新表达式；client key+`ValueGeneratedOnAdd` 的 generated readback；实际 inline 查询 | 分别记录实际 EF generated SQL/hash/command route、写入行数及 fresh raw/FF9/typed。默认值/seed的七位持久化依赖 L0；不得用直接参数 INSERT 替代。时钟函数只报真实精度；NULL default单列 |

用 `DbCommandInterceptor` 的 sync/async Executing/Executed 观察实际 command、parameter、reader类型及安全 facet，不修改 Precision/Scale，不消费 EF reader 行。`ReaderExecuted` 收据证明真实 reader 来源，materialization正确性由最终实体+独立oracle证明；精确 typed getter调度由 EF-M02 真实表达式/helper门承担，不能凭实体相等伪称看到了 getter 调用。

TLS default7 的受控 advisory lane可从实际 EF mapping service 创建参数，在 EF 创建的真实 W connection/command 上执行固定参数 SQL；必须观察本次 describe/binding 为 advisory/unfixed 后，才判定七位能力拒绝。该 lane 明确标记为“EF 生成参数 + ADO execute”，不冒称 SaveChanges/EF query 管线全程。已有 TP06 ADO 七位拒绝证据可支撑能力边界，但不能替代此处 EF 参数请求7的实际收据；若选用真正 EF query pipeline，则需证明参数未被 client evaluation 消除、未被 fixed6 重定向。任何 lane 未得到要求的 metadata 来源均 pending，不预设某条 SELECT 必然 advisory，不临时改变已接受 fixed precedence。

本地写拒绝可能按既有 R3 退休连接：不得要求原 context/connection 强行复用或重放 SaveChanges。采公共 State 与实际 async `AfterFrameSent` 只读收据（沿 TP06 已审同包观察路径，不新加产品 hook）；旧连接受控拒绝/无新增I/O后 dispose，fresh TEST 查原值/行数。sync 正常路径保留；需精确 no-send证明的拒绝 case用 async API。合法 reader转换失败仍遵守原前向读取合同，不能套写失败豁免。

## D：CLI、scaffold 与 guarded migration

固定 pin 的 `DamengDatabaseModelFactory` 是 **internal**；新项目不能复制原 friend-test 的直接构造方式。用 public `DamengDesignTimeServices.ConfigureDesignTimeServices(ServiceCollection)`，从 DI 取 `IDatabaseModelFactory`。该路径在 pin 的 `DamengDesignTimeTests` 有实际范式。catalog API lane调用其 `Create(DbConnection, DatabaseModelFactoryOptions)`，传已核身份的真实 W connection；CLI另走原 provider 的 `Create(string,...)`，不能用自制factory替代后声称原服务被验证。

Design host 提供 `Program.CreateHostBuilder` + `IDesignTimeDbContextFactory<DesignContext>`，同一配置函数，禁 sensitive logging。host配置仅内存添加 `ConnectionStrings:TP07`（值来自 wrapper env），移除不必要 logging provider。CLI scaffold参数使用 **`Name=ConnectionStrings:TP07`**，`--no-onconfiguring`，不把 secret 放argv、生成源码、appsettings或任意日志。先离线验证 host配置/命名连接解析链，若 EF CLI不能解析则失败回修，禁止fallback为raw连接串。exact `dotnet-ef10.0.12` 本地tool；显式 restore/build 后所有CLI `--no-build`，生成源码后再显式build。

| 门 | 最小可执行链 | 边界 |
| --- | --- | --- |
| R05 | 创建本次owned、scaffold兼容表；DI factory读取 → CLI `dbcontext scaffold` 仅table allowlist → 编译generated model → `migrations add`/snapshot →生成SQL，比较 catalog→DatabaseModel→model→snapshot/DDL | 0/6/7及已可观察8/9、TZ/interval leading/fraction、TLTZ metadata；不写8/9/TLTZ。显式6不升级。固定factory仍枚举schema全部sequence；遇外部不支持sequence只报环境阻塞，不过滤/删对象。scaffold不支持user CHECK/trigger/computed的已知边界保留，fixture与R04/R06分开 |
| R06 | dedicated baseline6 migration+独立history → Up7 → fresh data/default/index/constraint比对 →插入seventh →guarded Down拒绝 → fresh schema/value/history不变 →清掉本次非micro行后whole-micro正控 Down | 只guarded sample，不修改通用generator。guard为迁移中的第一条非查询状态改变前检查，SQL使用已证FF9整数尾数/精确服务端表达式；先独立用tail0…9/极值证谓词，不用可能舍入的CAST往返。guard SQL未验证前Down不可执行。DDL隐式提交，写入方静止，禁止宣称在线原子降级 |

R06 的 `MigrationGuardSql.cs` 由design owner提交**可审 SQL 模板**及固定错误码，先经静态审查与专用guard控制门；本设计不凭空预批准某条 server错误语法。生成迁移必须确认无任何 ALTER/DROP/history mutation先于guard。运行器不得提供绕guard的Down捷径；应用层预查可留证，但不能替代迁移内已实测 SQL guard。生成迁移只允许本次表/history/index/constraints，其他schema对象一律拒绝执行。

每个 EF CLI子进程需 module initializer candidate guard及退出收据，核 assets/deps/loaded W DLL/MVID，拒绝官方driver与旧包；迁移运行另用真实 connection/command/parameter/reader观察。CLI scaffold原string factory的W构造由冻结源码及原服务注册证明，再结合进程W实际连接/协议活动；不可仅凭 `typeof(W.Dm.DmConnection)` 加载声称发生了W catalog查询。若所选既有诊断无法证明实际活动，先报告 evidence gap，不加产品hook或替换factory。

## profile、安全、清理与最终门

shared 精确 `8.1.5.60/msg21/utf-8/override empty`，W typed builder显式 `PlaintextAllowed`；TLS 精确 `8.1.4.6/msg11/gb18030/override empty`，原 RequireTls/CA/client策略（本地CA显式NoCheck，不关证书验证）。两者先验证 user/current schema均 `WDM_PROVIDER_TEST`。服务端metadata scale与parameter请求scale分别核，不能强求所有advisory query read tuple等于write tuple；只能使用该具体查询实际观察并独立审批的有限合同，不能套用TP06不同SQL的备选集。

CREATE前落ownership ledger、fresh absence、完整nonce和对象allowlist，使用拥有标记；表、index/constraint及migration history一并登记。history专用名称，不动共享`__EFMigrationsHistory`；若需要预建兼容history额外owner标记，SQL模板必须列入本工具审查且保持EF必需两列/约束合同，不能运行时猜表归属。清理只针对本轮确实创建且marker/名称/结构匹配的对象，fresh连接核absence；不凭prefix全删，不用SA，不授额外权限。TLS所需catalog权限如缺失，停门报root。finally清理失败使candidate失败，不能用原业务异常覆盖清理失败。

输出仅白名单身份/facet/合成时间值与bounded raw、固定assertion ID、已脱敏error type/code；不输出任意Exception.Message、连接串、认证包或整段未知SQL。SQL制品仅固定合成fixture与owned标识符，未知动态命令只留shape/hash。逐步骤先记录attempt，再实际call结果，原异常传播；partial receipt不算case完成。

依赖 DAG：`P1已接受 → mapping冻结+EF-M01/M02 → L0（最先DB） → R01/R03 → R02/R04 → R05 → R06 guard控制/Up/Down → 同包C总结`。各组可先独立编译/静态验收，DB窗口由Low独占；L0硬失败后其依赖门不运行。High只写授权文件，停写freeze→Astra审查→Low独立locked restore/build/实际门，源/锁/assets/deps/package/DLL/MVID/输出pre-post一致，旧失败保留。最终C接受需固定case清单零跳过、两profile适用门、实际退出码与fresh清理均闭环；TP06 A/B、EF C、EF PR/发布分别报告。
