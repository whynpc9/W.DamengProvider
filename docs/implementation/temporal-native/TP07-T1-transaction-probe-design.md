# TP07 T1：四例事务能力 activation probe 设计

2026-10-10。**design_only / product_artifacts_pending / SDK_DB_not_authorized**。本报告只读既有 C#/Python helper、固定 EF10.0.12 XML 接口及 [失败归因](TP07-E-generic-v1-runtime-failure-triage.md)；后者实际 SHA256 为 `efa0dfdf098ac8a300cf34a80742067478a5600b30fdfd955d30123299e0ae71`。没有实现、导入执行项目工具、SDK、tests、wrapper、DB、secret访问，未改旧source、status或证据。

## 1. 当前 pending 与目标

High正在新隔离EF source实施正式 transaction factory/adapter：三个产品文件加一个unit文件及增量patch。新EF完整source预计178项，但**实际patch、文件清单、manifest、准确计数、受审服务类型、编译DLL/MVID尚未交付**，不能把178当已接受事实。下列也全部pending：新overlay的native graph/lock/cache、provider与probe构建、离线DI/producer-validator合同、toolpack/readiness、runtime approval、四例实库及独立接受。旧W包保持不变；旧E第二轮在TLS `transaction_SaveChanges`失败与291 owned objects全部absent保留为历史事实。

probe只证明新overlay正式DI事务能力传播与四个固定显式事务路径，决定能否进入下游重新绑定门。它不替代L0、E338、D、完整C或发布接受。不能关闭AutoSavepointsEnabled、替换测试context的事务factory、吞NotSupported、让TLS绕过W savepoint门，或用裸ADO UPDATE代替EF SaveChanges。

## 2. 固定四例与业务序列

公共runner一次执行完整固定顺序，不接受profile/case/group/filter/SQL/resume/continue参数。每例独立进程、一个新nonce Basic表、一行D/Z成对值：

| 序号/ID | API | profile/scale | 原DT与DTO local ticks | DTO offset | 更新量子 | 期望能力（策略，不是观测） |
| --- | --- | --- | --- | --- | --- | --- |
| 0 / T1-shared-sync-pair | sync | shared / 7 | 639204129161234560 | +330分钟 | +1 tick | EF与W均true |
| 1 / T1-shared-async-pair | async | shared / 7 | 同上 | 同上 | +1 tick | EF与W均true |
| 2 / T1-tls-sync-pair | sync | tls / 6 | 同上 | 同上 | +10 ticks | EF与W均false |
| 3 / T1-tls-async-pair | async | tls / 6 | 同上 | 同上 | +10 ticks | EF与W均false |

固定ID=1、N初值=`first`、临时值=`rollback`；D为Unspecified、Z保存local ticks/offset/UTC三者，不替换为UTC映射。独立validator按整数常量重建期望，不能信任producer报告中的expected。两列均非NULL；此probe不额外增加NULL、并发、异常业务或第五实库case。

每例顺序必须有独立started/completed/failed步骤：

1. 在任何DDL之前核新host/source/lock/assets、实际W/provider/EF程序集、正式DI factory和TEST物理身份。shared显式PlaintextAllowed；TLS原RequireTls、固定本机目标与CA/NoCheck约束。先持久reserved ledger，确认本nonce对象不存在，再创建唯一Basic表；CREATE返回后保存真实ACK，marker/schema/PK确认。DDL由固定ProbeContext的正式model differ/sql generator生成，只准单个本nonce表，无任意SQL入口；seed用对应API的EF SaveChanges，返回1。
2. 新context/connection作baseline fresh AsNoTracking、总rowcount=1、D/Z/N精确值、owned catalog/marker/PK；另做D/Z原列raw/typed/FF9 oracle。将新连接实际身份记录下来。不能将模型metadata当服务器schema。
3. 通过正式context.Database对应 `BeginTransaction(IsolationLevel.ReadCommitted)` 或 `BeginTransactionAsync(IsolationLevel.ReadCommitted, token)`；记录started及真实返回，后采集factory实例、EF事务、底层DbTransaction、connection。两层SupportsSavepoints在返回后及SaveChanges前实际读取，均与固定profile策略匹配；AutoSavepointsEnabled实际读取为true且没有赋false。能力未知或不一致在更新前失败，仍清理。
4. 在同一context跟踪row，同时更新D/Z及N；对应API EF SaveChanges必须自然返回1。不得把seed/write不同步骤的证据复用。记录实际DbCommand开始/成功、affected语义、参数类型及D/Z ticks/offset、真实command.Transaction/Connection引用绑定。
5. 同一显式事务内执行真正数据库 `AsNoTracking().Single/SingleAsync`，必须有实际query command及同transaction证据，精确读到+1/+10 ticks和N=`rollback`。不得用Local、tracked entity或从入参合成inside结果。
6. 对原IDbContextTransaction执行对应API的**整事务Rollback**，独立started/returned及实际TransactionRolledBack回调必须出现。Dispose引起的回滚不替代本步骤；失败/cancellation/未知结果均pending。
7. 完成事务/context释放后，用另一个新context/connection fresh读取原D/Z、N=`first`、总rowcount=1及原schema/marker/PK。fresh connection必须与原transaction connection为不同对象，pooling关闭；验证TEST物理身份。D/Z分别用原列独立raw/typed/FF9 oracle重算local/UTC/offset及scale；schema前后语义相等。没有fresh或只有生产者bool不能接受。
8. 主finally在显式事务/context处置后调用exact-ledger清理并保存独立于业务结果的收据；runner无论main结果如何，对已存在的可信ledger运行新进程独立cleanup，核每项fresh absent/count0。主finally、独立cleanup及独立cleanup自身finally均逐项覆盖唯一对象。失败仍是失败，不能因清理成功改成probe成功。

主事务API、EF SaveChanges和inside/fresh EF query按sync/async分支；受审OwnedObjects和LiteralOracle目前是同步辅助调用，辅助catalog/独立raw oracle/cleanup保留同步时须明确记录，不宣称整个async case所有辅助I/O均异步。

## 3. 真实事务与事件证据

正式服务从同一context `GetService<IRelationalTransactionFactory>()` 实际解析；记录type/assembly name/DLL SHA/MVID、DI解析来源。Begin返回的adapter实际type及程序集必须匹配新overlay受审类，`Database.CurrentTransaction`须与返回IDbContextTransaction ReferenceEquals。底层对象通过实际 `GetDbTransaction()` 取得，必须为固定W DmTransaction；与TransactionStarted callback的DbTransaction为同一对象，且其Connection与context.Database.GetDbConnection同一对象，IsolationLevel实际ReadCommitted。不要另开连接再观察一个无关W事务。

新collector用进程内ReferenceEquality对象表分配单调object_id，不使用可能碰撞的GetHashCode作为等价证明；序列化真实ReferenceEquals结果和参与对象ID。EF TransactionId、TransactionEventData.TransactionId/ConnectionId/IsAsync共同关联，不能仅以相同stage标签证明sameTx。factory、adapter、W事务类来源分别绑定新provider、原W；底层能力值来自当前实例getter，禁止按profile写实际值。

`TransactionEvidence.cs`注册一个真实DbTransactionInterceptor，同时提供DbCommandInterceptor审计。固定EF10.0.12安装XML已确认如下sync/async接口存在：`TransactionStarted`、`CreatingSavepoint/CreatedSavepoint`、`ReleasingSavepoint/ReleasedSavepoint`、`RollingBackToSavepoint/RolledBackToSavepoint`、`TransactionRollingBack/TransactionRolledBack`、`TransactionFailed`；TransactionEventData含Transaction/TransactionId/ConnectionId/IsAsync。覆盖两种API，pre方法原样返回InterceptionResult，post不替换返回对象、不抑制执行、不调用事务方法。API签名最终由真实编译门核对，不能添加想象的factory.CreateAsync接口。

**零事件不能靠 `captured_complete=true` 自证。** 观测包必须含：

- collector_constructed/installed/observation_started/observation_completed的单调sequence；实际collector type、DLL/MVID、引用object_id；从同context `IDbContextOptions` 的 `CoreOptionsExtension.Interceptors` 读回同实例恰好一次的registration证据。该属性已在固定EF XML核实；不以代码里调用AddInterceptors当运行证明。
- 实际TransactionStarted及显式TransactionRollingBack/TransactionRolledBack回调的sameTx链，与业务command executing/executed和inside-query回调完整覆盖；每个callback记录source枚举、callback方法、sequence、context/object/transaction/connection ID、IsAsync、是否完成，error只安全类型/编号。collector异常、无法关联、丢事件、超过固定上限256、结束标记缺失均captured_unknown。
- 全部事务事件保留，seed自身事务/其它context记录单独关联，不在producer中按期望删行。接受时只对明确显式事务的真实ID统计保存点；不能把seed的create/release搬到目标SaveChanges。
- 同冻结collector/host的离线真DI/stub正例证明sync/async保存点callback确实可达；本次shared两个live case提供同binary真实create/release正控制。TLS接受必须联结本次已通过的两个shared case验证引用；没有这些正控制，TLS空集合只能pending。TLS自身还必须有真实begin/fullRollback与business/query callbacks，排除未安装/未运行collector的空结果。

正常实库成功路径的精确事件合同：shared目标SaveChanges中一对Creating/Created、一对Releasing/Released，先create完成、后真实业务、再release完成；RollingBackTo/RolledBackTo均0。TLS六种savepoint callbacks全部0，且同事务业务执行及全Rollback成功；两profile目标事务均无Commit。全Rollback的callback与RollbackToSavepoint分开命名统计。若实际出现rollback-to或数量/顺序不同，保留实际证据并停止，不改期望凑通过。

错误批次触发rollback-to只在离线真DI/stub测试，实库四例不主动制造业务失败。不要捕捉异常后继续SaveChanges或把supports=false/true硬编码为事件产物。`captured_complete`只能由validator结合注册、源/DLL、正控制、起止、回调和sameTx全链推导；producer保留observation状态但不拥有接受权。

## 4. 最小新工具文件归属与复用接缝

probe High只拥有新 `tools/DownstreamAcceptance/TP07/transaction-capability-probe/` 以下**17个源文件**；该精确清单作为首版设计，实际需要变更须先审阅新清单，不能扩大通配符或以任意count通过。不修改旧common、E.Program、E.MutationCases、ef runner/validator、provider产品文件或旧准备工具。产品T1与E mutation receipt分别由原High所有者交付。

| 新文件 | 职责 |
| --- | --- |
| README.md、probe-policy.json | 固定四例、schema/命令/阶段、数值及精确路径清单；policy不含未生成的接受值 |
| prepare.py | 新路径明确的source/native-lock/build/offline/readiness入口；导入无执行副作用；不含DB入口 |
| run.py | 固定4例调度、内部ticket host/cleanup、独立validator、180秒边界、首失败停止及safe收据 |
| validate.py、test_contracts.py | 独立严格JSON/value/event/identity/cleanup/矩阵判定及合成负例；不执行SDK/DB |
| managed_identity.py、packed_oracle.py | 从固定受审来源逐字节复制的PE MVID读取及整数packed decoder；manifest记录原path/SHA |
| host/TP07TransactionCapabilityProbe.csproj | 唯一新assembly `TP07TransactionCapabilityProbe`，net10.0，仅正式新provider ProjectReference与既有EF依赖；无Design/AspNet/官方驱动fallback |
| host/Program.cs | 固定registry、步骤、main/cleanup/offline分入口；无任意SQL/选case/resume |
| host/ProbeContext.cs | 一种Basic表、一行D/Z/N模型，官方DI事务factory不替换；nonce model cache key、受限DDL生成、SaveChanges/query |
| host/TransactionEvidence.cs | 上节真实事务/command collector、对象关联与安全输出；不改产品行为 |
| host/ProbeIdentity.cs | 新host明确身份guard，兼容下列helper所需TP07.Runtime.CandidateGuard接口；新startup/overlay/pin校验 |
| host/OfflineContracts.cs | 真DI/stub事务/批次与两API离线观测门；只在offline入口注册stub，不进入live |
| host/common/OwnedObjects.cs、SafeEvidence.cs、LiteralOracle.cs | 下列三个受审文件的逐字节复制；旧文件零改动 |

17项=8个工具/说明文件+9个host项目/源码文件。新guard可保留类名 `TP07.Runtime.CandidateGuard` 以供只读副本链接；程序集必须是新probe，不冒用旧host身份。新guard从旧guard定点适配，交付逐行diff：保留W/package/source/physical/TEST/typed transport/安全命令检查，只把host allowlist和startup/新overlay schema改为本probe的精确身份；不得顺手放宽其他门。

真实接口与固定来源：

- 旧CandidateGuard SHA `b5ffd7f0ff7def12b6f7dc0f22324d981eb1f0a20ee6536ddc9ac7bb96e6f493` 的VerifyRuntime只允许四个旧assembly名，不能直接复用或传入另一个旧Assembly来骗过。其Load、BuildConnectionString、OpenTestConnection[Async]、VerifyOpenedConnection、VerifyProvider、VerifyObject、Command接口是新guard的有限兼容接缝；VerifyObject旧role不含transaction，事务类型核验由新collector/guard明确新增，不假报connection role。
- OwnedObjects SHA `80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946`：`RegisterEfTable(caseId, Basic, scale)`；CREATE严格使用BeginExternalCreate → 执行真实DDL → RecordExternalCreateAcknowledged(CreateCompletion) → ConfirmExternalCreate，失败调用FailExternalCreate；`Create(entry)`只支持Literal，**不得给Basic调用它**。`Preflight(entry)`核columns/marker/PK，`LoadForCleanup(guard,path).Cleanup()`对exact ledger新连接fresh absence。
- SafeEvidence SHA `44c5e9b5643a3d33ba348ed8acc51780ccad2f94a5b43cc819f5897c9f9c11ef`：StartStep/CompleteStep/FailStep、Safe、Write、Hash；Safe不输出Message/Stack。Value对普通string只返回type，因此N须另按固定允许值读取实际string并保存安全observed字段，不能把type相等当N相等。未知N保存明确不匹配标记并失败。
- LiteralOracle SHA `cb1696d1b305471e12c1a953630ee66454c4ab92d505d650e291e7170c44f492` 已有 `ReadFresh(guard, table, 1, isOffset, expectedScale, column, partialSink)`；column允许D/Z，直接使用其ordinary cached raw、ResultColumn tuple、FF9/typed独立读取。它只开fresh连接，不能拿它假装inside-transaction读值。
- Python旧runtime-run.py SHA `d2c34db7184c8450daae7ce3c81c0b1a3d5e8df642cd42f2989ddb28f5a802ee` 有public_env/process模式；旧runtime-validate.py SHA `d9ea39850a3d394f86f2fef54fe8330d732ea3a46d6823da3da74e84b9188d55` 有ticks/ff9/profile/oracle/cleanup。只复制经审阅的纯函数/必要闭包到新工具并记录AST/来源，或明确使用无ROOT依赖的固定副本；不能修改module.__file__/旧ROOT伪造执行位置或继承旧source175/host名门。新输出边界显式来自批准workspace_root。

## 5. 有限JSON合同

所有文档UTF-8、schema_version=1、拒绝duplicate keys、非有限number、bool-as-int、未知key、缺key、非绝对ref、SHA不匹配、symlink越界及重复case/nonce/object。`Ref`精确为 `{path,sha256}`；`Identity`含受审type/name/path/SHA/MVID，actual与expected分开。下述列举即顶层键集合，不能接受additionalProperties。更低层有限enum和字段由High首版policy逐项实现，不支持任意SQL/参数对象。

| JSON/task | 顶层字段与来源 |
| --- | --- |
| inputs / TP07-T1-inputs | schema_version, task, workspace_root, original_repo, sdk, python, W_identity, capabilities, ef_overlay, toolpack, helper_origins, expected_closure；SDK/解释器是path/SHA/version，overlay是新冻结Ref，不能填旧175来源 |
| source / TP07-T1-source | schema_version, task, inputs, toolpack, replay_root, lineage, W, source_hashes, exact_paths, exact_count, role；actual枚举与新批准清单逐项相等 |
| native / TP07-T1-native | schema_version, task, inputs, source, sdk_events, project_graphs, cache_root, archive_records, native_metadata_records, source_before, source_after, database_accessed；只接受真实新native事件 |
| build / TP07-T1-build | schema_version, task, inputs, source, native, locked_restores, commands, startup, binaries, source_before, source_after, lock_before, lock_after, offline_proofs, database_accessed；锁和源码前后一致 |
| startup / TP07-T1-startup | schema_version, task, source, native, replay_root, lineage, W, locks, assets, host, ef_provider, ef_dependencies, source_hashes；host唯一新名，provider与所有依赖真实DLL/MVID/deps绑定 |
| approval / TP07-T1-runtime-approval | schema_version, task, status, inputs, source, native, build, startup, toolpack, offline, readiness, wrappers, ordered_cases, budgets, output_root, original_repo；status只能由root在门全过后签runtime_authorized；ordered_cases精确上述四ID，budgets={main:180,cleanup:180,validator:180} |
| ticket / TP07-T1-case-ticket | schema_version, task, approval, cohort_id, ordinal, case_id, role, startup, predecessor_validation, output, ledger；由固定runner在开始前持久化/ref冻结；main无用户指定ledger，cleanup只消费本例exact ledger；首例无前驱，后例须前例validation成功 |
| case report / TP07-T1-case | schema_version, task, approval, ticket, cohort_id, case_id, profile, api, definition, runtime_identity, services, observer, steps, transactions, commands, events, values, ownership, main_finally, error, status, process_exit_code；任何actual未采集用null+capture_state，不能填expected值 |
| cleanup report / TP07-T1-cleanup | schema_version, task, approval, ticket, runtime_identity, ledger_before, objects, cleanup, finally_cleanup, error, status, process_exit_code；objects集合恰为本例ledger，主/独立receipt分别保留 |
| case receipt / TP07-T1-case-execution | schema_version, task, approval, cohort_id, case_id, ticket, inputs_before, main, ledger, independent_cleanup, validator, inputs_after, status；process记录argv安全模板、cwd、真实exit/elapsed/timeout/termination及report Ref |
| validation / TP07-T1-validation | schema_version, task, approval, case_receipt, case_id, status, issues, missing, captured_complete, checks, positive_control_refs, activation_accepted；每例activation_accepted=false，整体另经root接受 |
| matrix / TP07-T1-matrix | schema_version, task, approval, cohort_id, required_cases, attempted_cases, case_receipts, case_validations, first_failure, input_drift, cleanup_pending, status, candidate_acceptance, full_EF_C_accepted；两接受旗始终false；producer最多completed_pending_review |

避免循环hash：build写完startup之后引用它；startup不引用build；case-receipt先保存main/cleanup部分为不可变execution input，再由validator写新validation，runner另写final case receipt记录二者并明示原被验证receipt Ref，不覆盖被引用版本。validation不能引用尚待自己生成的final receipt哈希。同理ready不引用未来runtime approval；approval引用已完成ready。

`lineage`必须包括原EF commit/base archive、原bridge patch、mapping manifest/patch、新transaction增量patch及其parent overlay、新完整approved EF source manifest、probe toolpack/host source manifest。新provider若仍ProjectReference，不虚构其nupkg；W确为固定PackageReference。DLL身份来自实际文件/PE MVID，runtime再从实际loaded Assembly核对；dependency paths/assets/lock记录同replay，无外部dirty checkout或官方DM fallback。

运行observations：每个step含id、ordinal、started/completed sequence、status、actual API、result或safe error；每个transaction/event含capture_state、type来源、object refs、TransactionId/ConnectionId、IsAsync及callback source；每个command含真实EF CommandId、connection/transaction refs、API、SQL SHA与有限角色、参数安全类型/时间值和完成状态。日志不存SQL全文/连接串/口令/认证帧，事务/命令回调不能以stage expected bool生成actual字段。

未捕获能力、begin未完成、Rollback未返回、fresh缺失、raw oracle未完成、observer未安装/不完整、cleanup unknown、无法验证引用一律pending/rejected，绝不accept。空events数组仅在上一节证据闭合时可解释为实际零。主main/cleanup自然exit0仍需独立validator全合同通过。

## 6. 新source/native/pin的准确计数

原175+新增两产品类+新增unit文件预计178，已有DI注册文件为修改，不额外计新文件。先由产品High给新manifest逐项枚举，root确认只有三产品文件/一个unit差异及独立增量patch；若实际不是178，停并审明确差异，不能改validator为count>=175。

本设计host项目/源码9项；若新EF源实际178且没有路径重叠，probe初始replay精确187项，新增唯一host lock后精确188；provider已有lock仍是原EF源集合的一部分。**187/188目前是设计推导而非已验证数字。** prepare前以实际清单冻结确切值和exact_paths，High禁止使用自动接受任意manifest count的通用化门。离线fixture或toolpack Python17项不偷算进replay源码；生成bin/obj不计source。

probe无Design/AspNet/Specification依赖，预期provider/probe各为已有Core13非W包、原W package，probe加唯一provider Project；最终必须以新实际native lock/assets/自身dgspec、central PackageReferences/framework/pruning字典证明精确ID集合、resolved/contentHash/边和Project路径。13只是受审预期，实际差异即停，不能套D的24/33或E工具49包。原archive可从已冻结local feed读取，但新source图、空任务cache原生restore、metadata/sidecar/实际locks必须新证据；sidecar完整ZIP SHA512与NuGet contentHash分别核验。不能手写锁或改metadata。

new overlay source、probe source、toolpack17、helper来源、两active locks/两assets及全部输出binary都须在新manifest记录真实数量/路径/SHA；如只一host加provider构建，startup host数固定1，不借旧E或L0 assembly。SDK10.0.401、EF10.0.12和既定W package固定；不重打W包，不增加NuGet包以解决测试便利性。

## 7. 离线及Low执行窗口

High不自行SDK/DB；代码停写后root/Astra source review，再由独立Low一次窗口顺序执行：

1. 新provider正式unit及probe离线真DI/stub：实际UseDameng建立服务、解析新IRelationalTransactionFactory，true/false底层stub两种能力；factory.Create保留同DbTransaction/connection/id/owned及生命周期。sync/async Begin与外部UseTransaction均走正式factory；stub仅离线连接/命令来源，不替换factory/adapter或把getter值mock成预期。连接与命令不得触碰网络/secret，记录实际stub method counters。
2. EF真实SaveChanges/batch路径，AutoSavepointsEnabled仍true：支持true成功create/release，失败批次实际rollback-to；false成功仍执行业务且三个保存点动作均0，失败仍抛原异常且无假成功。从真实interceptor捕获sameTx事件；覆盖两API、transactionOwned true/false、不误Dispose外部transaction、原connection/tx引用。离线执行IO blocker或纯stub证明无真实DB I/O；这与合成validator fixtures分开计数。
3. 合成producer/validator合同：四正例、每类缺步骤/改身份/混profile/错量子/DTO offset/UTC错/N错/schema变/缺fresh/fullRollback误当rollback-to、假factory或DI ReplaceService、底层换对象、command.Transaction错/无事务、IsAsync错、seed事件挪用、pre callback冒充post、TLS空数组但缺collector/正控制/开始结束、collector_unknown却宣告complete、隐瞒failed callback、input漂移、旧host/旧ROOT/newlineage不符、伪造accepted、duplicate key/extra fields/跨root/超时/非零exit、ledger缺项/跨case/无ACK marker清理，以及firstfailure后仍执行后例等必须拒绝。
4. 新source/native锁/build：独立临时CLI_HOME，DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1，所有build/test `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。native restore新空任务cache；以后locked restore/build，保存真实命令、exit、source/lock before/after及DLL/MVID/deps。无全局cache清理，官方驱动与W边界不混。
5. 固定host离线入口一次跑完DI/stub/collector合同；固定Python合同由Low执行，报告实际方法/断言数量，High不预填通过计数。外部Low独立生成source/native/build/startup/toolpack/offline artifacts，root检查实际helper diff和源冻结；ready只读重新核全部refs、精确paths/counts、四例registry/量子/180预算和startup loaded来源，输出ready_pending_runtime，不打开wrapper。
6. 只有root明确签runtime approval并转交唯一窗口后，Low执行新 `run.py run --approval <abs> --approval-sha256 <SHA> --output <fresh-allowed-root>`。内部main/cleanup只接受不可变ticket，不是公开选例接口；每例main180秒、独立cleanup180秒、validator180秒各自独立预算，不共用剩余时间、不延长或重试。新脚本入口与参数由High实现后冻结，本报告并无当前可执行新工具。

建议根为 `.local/temporal-native/verification/TP07-T1-probe-v1-{source,lock,build,offline,readiness,runtime}`，approval独立位于 `.local/temporal-native/root-approvals/TP07-T1-probe-v1/`。所有目录首次不存在，内部结果按 `00-shared-sync` 至 `03-tls-async`；actual cohort由inputs/toolpack/startup/approval内容身份绑定，不照搬E旧cohort。失败保留前缀结果、未执行后例标not_run，无resume；修复后新版本完整四例。

runner超时杀整个进程组、保存真实退出/termination；stdout/stderr丢弃或只经过明确安全转储，禁止写含连接异常的raw日志。main失败/timeout后仍进行本例独立exact-ledger cleanup与validator，不进入下一case。若ledger存在但无法可信核验，不按名称猜清理，标cleanup_pending交root；无ledger且未到创建步骤可标before-DB，未知不得说服务器干净。独立cleanup只运行新host cleanup入口与自身身份门，不运行seed/Begin/SaveChanges，必须在部分main产物丢失时仍能依赖既有approval+startup+已保存ledger工作。

## 8. 下游重新绑定与范围隔离

E mutation receipt补充属于另一个明确High范围：Begin/两层能力、实际事务类型、sameTx/savepoint、显式Rollback步骤和validator/policy合同；本probe不修改E.Program或旧common，不把新probe report拼入旧E failed group。

四例全部成功、无input drift/cleanup pending，并经Astra/root实物接受后仅产生**新overlay T1 transaction activation accepted**。之后仍按新lineage重新执行/接受L0来源与真实literal门；新E source/lock/startup/actual provider、mutation观测、partition338/25、GeneratedCreate28、12引用readiness与完整新run338；D全部准备/模型门/设计时host/实际CLI及G6/G12重新绑定。旧L0/E/D结果保留历史，不因W包相同而被标为新overlay通过。

执行前剩余阻塞：产品T1真实patch/source及准确计数未冻结；probe17项实现与严格schema/collector合同未交付；native图/pin/DLL/MVID/offline/ready均无实际证据；root无SDK/DB执行授权。本报告仅给可实施接缝，不替这些门填写accepted。
