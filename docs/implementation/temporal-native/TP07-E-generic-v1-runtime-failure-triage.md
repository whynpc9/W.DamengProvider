# TP07 E-generic-v1：TLS事务保存点能力传播缺口

2026-10-10。结论：**isolated_EF_transaction_capability_adapter_missing / W_savepoint_profile_boundary_preserved / new_EF_overlay_lineage_required**。实际失败在TLS rollback case的`transaction_SaveChanges`，不是显式Rollback步骤或七位参数绑定。已确定隔离EF provider未把底层W的SupportsSavepoints=false传到EF事务层；应修正式provider transaction factory/adapter，不关闭AutoSavepointsEnabled，不扩张W TLS savepoint能力，不当作harness-only修复。

本轮只读实际收据、冻结C#/provider/W源码及当前EF Relational DLL的ECMA元数据/IL字节；只做文件/文档比较，没有加载执行程序集，没有SDK、tests、validator、DB或wrapper调用，没有编辑源码、旧失败或status，仅新增本文。

## 失败的准确位置及已观察边界

失败根`TP07-E-generic-v1-runtime/tls/mutation/window-000`内：DT update、DT concurrency完成，第三例`EF-R02-tls-dt-rollback-sync`失败。main自然exit1/host1、7.660383秒，cleanup自然exit0/host0、3.661981秒，无timeout。validator先因main进程失败报`process_success_no_timeout`；没有完成该窗独立接受。

失败case已完成CREATE、seed_SaveChanges、fresh_state_before_mutation及tracking_query_current；fresh catalog为D TIMESTAMP/scale6/width8、Z TIME ZONE/scale6/width10、N VARCHAR32，rowcount1，原DT ticks`639204129161234560`、N=first。接着`transaction_SaveChanges`记真实单层System.NotSupportedException，未有RuntimeAssertionException/failed_assertion。

本步骤没有任何EF business command attempted/failed记录，没有该步骤ParameterDescribe或协议response；只能看到此前seed的source。不能把seed describe搬到失败update，也不能声称七位codec或某个新raw tuple拒绝了update。seed真实DateTime2请求Scale6、raw DT16/8/6/flag1，另typed NULL DTO23/10/6/flag1。前两case更新/winner实际值是原值+10ticks，stale为+20ticks。冻结MutationCases对TLS rollback也选择+10ticks=`639204129161234570`，这是六位可表示值；没有证据支持“量子误用+1”或时间mapping有误。

`BeginTransaction`调用位于transaction_SaveChanges之前且不在Step中；实际进入该Step配合冻结源码说明Begin已返回，但旧收据没有独立记录transaction对象/SupportsSavepoints，不能假称旧报告已直接捕获该属性。`query_inside_transaction`、显式`Rollback`、fresh_after_rollback都未运行。旧stage下只有一个opcode9 sent-frame；没有足够步骤归属证明时，不把它当成测试显式Rollback已完成或把sync缺发送记录当Execute0。

## 能力不一致的证据链

固定W包`843710be…/aa23e673…`对应冻结源码：

- `DmConnection.IsPublicTransactionIsolationSupported`允许Unspecified/ReadCommitted，Begin把Unspecified转ReadCommitted；其余隔离级别有独立版本门。TLS本地事务与savepoint是两种能力，不能合并为“TLS没有事务”。
- `DmTransaction.SupportsSavepoints`只对server version8.1.5.60返回true，当前TLS物理profile为8.1.4.6/message11/gb18030，因此false。`RequireSavepoints`在false时抛NotSupported；raw SAVEPOINT命令也经`ValidateCommandSql`调用这个门，并非绕过公开能力属性。
- 此门与Temporal VerifiedSix/Seven无关；不会因时间值Scale6合法而允许savepoint，也不应靠打开TLS七位来修复。

当前隔离provider的`DamengServiceCollectionExtensions`注册RelationalConnection等服务后调用TryAddCoreServices，**没有IRelationalTransactionFactory替换、RelationalTransaction子类或SupportsSavepoints适配**；harness也没有ReplaceService该事务路径。保留的Microsoft.EntityFrameworkCore 10.0.12 XML说明AutoSavepointsEnabled默认true，并由EF IDbContextTransaction.SupportsSavepoints控制是否为显式事务内SaveChanges建点。

本轮直接读取实际new E输出的`Microsoft.EntityFrameworkCore.Relational.dll`，SHA`a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d`：`RelationalTransaction.get_SupportsSavepoints` MethodDef `0x0600090c`的完整IL仅`17 2a`（ldc.i4.1; ret），即无条件true。默认Factory.Create的元数据调用指向RelationalTransaction构造。BatchExecutor.Execute中可见当前事务→SupportsSavepoints→AutoSavepointsEnabled→CreateSavepoint→ModificationCommandBatch.Execute顺序；RelationalTransaction.CreateSavepoint调用SqlGenerationHelper.GenerateCreateSavepointStatement和DbCommand.ExecuteNonQuery。

因此确定存在**EF事务报告true、底层W实际false的适配缺口**。这使显式事务内SaveChanges走自动savepoint而触发W的既定拒绝，且发生在业务batch执行之前，与实际无business attempt/source及NotSupported吻合。旧safe receipt没有动态stack/savepoint SQL文本，故不声称抓到了确切throw栈或该SQL；但能力不一致本身已由当前源/实际binary及固定控制流证实，足以决定修复层级。

## 最小High产品范围：隔离EF overlay，不动dirty checkout

root已接受以下窄方向。从原已批准175文件EF源/bridge/mapping handoff在**新隔离目录**构建新overlay revision；外部dirty EF checkout和旧冻结replay不修改。通过新的交接patch/source manifest表达变更，不能把它塞进harness覆盖并继续称旧overlay已接受。

建议正式provider范围：

1. `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransaction.cs`：新的RelationalTransaction适配，保留基类事务所有权/生命周期；只把SupportsSavepoints委托给构造时实际DbTransaction.SupportsSavepoints，不反射、不按server字符串猜、不缓存共享profile值。
2. 同目录`DamengRelationalTransactionFactory.cs`：通过标准IRelationalTransactionFactory/Create构造适配，原connection、underlying transaction、transactionId、logger、transactionOwned及SQL helper原样传递；不创建旁路connection/transaction。
3. `Extensions/DamengServiceCollectionExtensions.cs`：正式注册该factory再由核心服务补全。sync/async BeginTransaction及外部UseTransaction均应经实际注册factory；不能仅在TemporalContext临时ReplaceService而让普通provider路径仍报告true。
4. provider测试目录新增或扩展有明确文件归属的事务能力/DI回归。测试进入新批准source map；最终文件清单及计数由实际新manifest固定。

修复位于W桥接overlay内，官方driver/base archive和原mapping patch历史不改；新patch须清楚区分事务adapter增量与既有temporal mapping，证明其它官方/bridge边界无混入。原source175若新增文件，计数必然变化：不能继续假装175/182/191/193不变，也不能把守卫泛化成任意数。按实际有限新增文件重新枚举EF base+tests、L0七模板、E九模板及真实locks，更新相应准备/validator精确数量和hash；禁止修改旧manifest。

不改变AutoSavepointsEnabled默认true，不改变AutoTransactionBehavior，不捕获NotSupported后继续/改期望为拒绝，不跳过TLS rollback或改为裸ADO代替EF。W公开SupportsSavepoints=false和raw/API拒绝继续原样，底层事务ReadCommitted正常使用；shared支持时仍保留EF自动savepoint流程。TLS不支持savepoint时EF应按诚实能力避免创建savepoint，但仍在同一实际外部事务执行SaveChanges、读取临时+10ticks值、整事务Rollback及fresh原值证明。

## 精确观察和测试补充

建议harness只作可审计的观测加强：MutationCases在BeginTransaction实际返回后记录EF事务类型/程序集与底层DbTransaction类型/程序集、两个SupportsSavepoints、AutoSavepointsEnabled=true及实际隔离级别。此信息必须来自实际factory产生的对象/API，不是按profile填expected bool；Begin/Rollback明确步骤与错误链也应各自记录。Python独立validator核两层能力一致、同new provider/cohort身份及步骤顺序；原时间参数/raw/列/source、临时值、inside-read、Rollback、fresh原值/rowcount/catalog门都不减。若增此receipt，限定MutationCases、ef-validate、tests及policy pins；Program/common无必要不改。

离线最少门：

- 真实DI解析的factory不是基类default；用支持true/false的底层事务stub分别验证适配能力，并验证同一underlying实例、transactionOwned/lifecycle保留；sync/async连接Begin与UseTransaction适配路径都覆盖。
- 保持AutoSavepointsEnabled=true；能力false时不调用savepoint而仍执行业务batch，true时保留create/release及异常rollback-to行为。不以只mock新getter自身作为全部验证。
- 失败事务仍不能被当作成功；unsupported profile不得反射或绕过W savepoint门；实体参数六位量子/typed NULL/原值与其它mapping回归照常。
- 对实际事务receipt加入身份/能力不一致、begin未完成、业务未执行、缺Rollback/fresh原值等变异；旧partial不能因新adapter判定被回填通过。

实库必须由Low另开窗口：共享profile验证SupportsSavepoints双true及真实自动savepoint/事务行为；TLS验证双false且AutoSavepointsEnabled仍true，DT/DTO × sync/async四个rollback case实际SaveChanges完成、inside原事务读到+10ticks、显式Rollback返回、fresh读原值/N/rowcount/catalog。初期窄支持探测是新overlay诊断证据，不能拼成E最终338接受；未知能力或与预期不同的真实状态继续pending。

### 在全25窗之前的独立事务能力activation gate

已检索当前正式测试入口：`DamengProviderFunctionalTests.TransactionRollbackDiscardsChanges`只有async普通实体rollback；`SavepointRollbackPreservesEarlierWork`无条件要求显式savepoint；`DamengGeneratedValueReadbackFunctionalTests.CallerOwnedTransactionReadsGeneratedValuesAndRollbackRestoresDatabase(bool async)`有UseTransaction路径，但还涉及identity/trigger/defaults。它们可作为新overlay正式回归的一部分，**原样都不具备两profile、两层能力/类型、TEST物理身份及exact-ledger独立清理的完整有限诊断协议**；不能直接过滤执行后宣称该新gate已完成，也不应把本次定位扩大到trigger等行为。

建议High在新的隔离诊断目录设计固定4例入口：shared/TLS × sync/async，顺序固定，无任意SQL/case选择或resume；每例使用一个独立nonce Basic表和D/Z成对时间值，可在一个完整事务里覆盖DT/DTO。正式DI加载**新provider overlay**，新诊断host source/lock/assets/DLL/MVID/deps和实际provider/W都需独立pin；不向旧E Program增加“只跑这一窗”的后门，不修改原common或旧runner。可复用受审源码中的安全身份、owned登记/cleanup逻辑，但诊断输入/程序集身份单独冻结，不伪造旧E startup名或report。

每例实际顺序和门槛：

1. 原TEST wrapper及对应typed shared/TLS连接策略，先核TEST user/current schema、实际server profile及W/EF service来源；在事务外仅创建本例owned表并seed一行。新连接读取原值/N/rowcount与精确schema，保存基线。
2. 通过对应sync/async EF BeginTransaction实际返回对象；记录DI factory类型/程序集、EF transaction类型/程序集与底层DbTransaction同一对象身份，实际ReadCommitted，AutoSavepointsEnabled=true。两层SupportsSavepoints必须相等；此固定共享profile期待true、TLS期待false，不满足就停止写并清理。
3. 在该事务更新D/Z到profile合法量子（shared+1 tick、TLS+10ticks）和N，实际EF SaveChanges必须返回1；在同一事务内查询精确读到改值。shared需事务拦截器的真实automatic savepoint create/release事件与同一事务关联；TLS不得有这些事件。观察可以采用工具侧DbTransactionInterceptor，只记录安全事件/身份，不绕过或改写事务服务。
4. 显式对应sync/async full Rollback必须自然完成。随后另一新TEST连接/EF context查询原D/Z、原N、rowcount1及catalog未变；绝不能只证明Dispose触发清理或只看Rollback返回值。scope finally清理及独立exact-ledger fresh absence同时完成。

外层建议每例main180秒、独立cleanup180秒、file-only validator180秒，固定最多4例串行、首失败stop，无自动retry；限额属于新小规模诊断，不改原E900/300/180。每阶段先落盘started，再落completion/安全错误/单调耗时；任一unknown包括rollback未确认都不得变成成功。root须在新source复审、真实offline DI/factory/stub tests与编译身份核验后单独批准此DB窗口，当前未授权执行。

四例全部有独立自然退出、能力一致、shared自动savepoint保留/TLS诚实不使用、fullRollback/freshstate、exact cleanup才标`transaction_capability_activation_passed`。该旗只允许推进新overlay的其余L0/E全量门，不接受338case、不替代UseTransaction正式回归、不继承旧run窗口。若不愿新增诊断host，也可在新provider正式功能测试中新增等价固定四例并完整接入相同证据/清理协议；现有原样入口没有这个门，不能仅凭测试名称复用接受。推荐新独立有界gate先定位，避免再次等到全25窗后部才发现同一factory路径缺口。

## 依赖回到哪里

本修复不需要新W包：目前没有W违反已声明能力或六位绑定的证据；不得为跑通测试把TLS SupportsSavepoints直接改true。如果后续新adapter下仍证明W本地ReadCommitted/rollback或能力报告本身错误，则停止本设计路径，单独定义W缺陷、High产品/测试范围、新W包身份、共享/TLS两profile原精度及事务A/B实证和原TP06候选门；新包通过前不得继续复用原W proof称已修复。本轮不授权该产品扩张。

因为EF overlay改变，必须新一轮M1/单位支持门和整条下游证据链，不能复用旧overlay acceptance：

1. 冻结新isolated EF patch/manifest及明确新增源清单，核官方base archive、原bridge/mapping增量及新事务adapter差异。新provider单位/支持测试、M1 graph/bridge映射观察和actual service identity先通过。
2. 新native lock/assets/cache证明；依赖不新增也需对新source graph实读，保留原W字节，不以旧175数量自动通过。
3. 对新EF overlay重新生成L0 source/lock/build/actual literal runtime及独立validation/toolpack/source-root证明。旧L0四refs保留但不能冒充新overlay门；不要通过放宽E/L0 source-lineage相等来复用旧L0结果。
4. 新E source（含必要事务观测）、lock/build/startup/provider DLL及工具pack，Python合同、实际compiled partition338/25、GeneratedCreate28及其独立service/SQL/IO门。新provider编译身份不同，但generated语义/SQL若不应变化仍须对旧受审基线逐字段/字节比较。
5. 新12-ref runtime approval及readiness：原L0字段形状可保留，值必须绑定新overlay实际L0证据；随后完整同run25窗/338cases重新验收，900/300/180及exact-ledger cleanup/fail-stop不变，不拼接旧288。
6. D准备/源/锁/设计时startup、实际scaffold6 CLI模型、迁移及下游门全部改绑新overlay/provider身份；旧D prep仅历史，不据其曾prepare通过就接受新代码。完整C、TP08及LocalClaude顺序不变。

## 新generic负例事实与清理保留

本次TLS/six/window-003独立validator已经真实接受11项，DT和DTO advisory都实际completed，source_branch为精确generic负分支，真实NotSupported、Execute0、各6个同窗控制及fresh TEST/TLS health完成。该新事实有专用readback，不能因后续事务失败撤销为“DTO未观察”，也不允许把它解释为TLS七位写开放。

失败窗三个confirmed ledger对象，本轮直接核main finally、独立cleanup及其finally同名完整覆盖、fresh_absent=true/count0且TEST身份正确。Low最终只读readback重新验21PASS窗288=shared180/TLS108；第22窗2completed+1failed没有独立接受，全部22started窗291个owned对象独立fresh absence/名称唯一，4963项输入不变。没有已知残留或本轮需要额外恢复DDL。

| artifact（相对`.local/temporal-native/verification/`） | SHA256 |
| --- | --- |
| TP07-E-generic-v1-runtime/run-receipt.json | `42d4d1ec0b670e71e7b8fdb5759fc558e0df1efb84b85f4a24337326f63d289a` |
| TP07-E-generic-v1-runtime/tls/mutation/window-000/main/runtime.json | `55b5897648cf7410e33feb91ae1ee72a7afcc3747c68cb00bc3238695d698d8f` |
| 同窗main/ownership.json | `329828d44ce8ef065637d3f0363259353cd484db9870a7893ffc8dbc76a41851` |
| 同窗cleanup/report.json | `37b18a83da84157e4a3e525de4ec655b74fad61178393bf509ec9e79e44a926f` |
| 同窗window-receipt.json | `64b11c1d3c474d68cda94a84791dd2120792ed98487c5f42955516a20197cd04` |
| 同窗validation.json | `3790a58fad5b43cffb31e6a24a651aaf62b402b1a7d5547900a02668afc0b229` |
| TP07-E-generic-v1-runtime-control/independent-readback.json | `8cb1a0855470dd5bf9d75db157d7e8040914d1f8253f9d8387c78d3a879bfca3` |
| TP07-E-generic-v1-runtime-control/verification-summary.json | `cbb7b24640dba89b1d4f8d2e4c8632813e6e422dd9bcd022b4b558eb7abacbf3` |
| TP07-E-generic-v1-runtime-control/advisory-actual-readback.json | `891dc970b6e2c2ccf439b8eaca1cfbc42bded96c861163785a56e96251fb3bed` |
| TP07-E-generic-v1-runtime-control/runtime-artifact-hashes.json | `c951294b7f27181b447eb60eb4a84b4ad82b2b3c9b78ace2836fded2c4bd8cf6` |

主agent可据此给High划定新隔离overlay文件归属，Low独立验证、源码复审后逐门推进。本设计不是已实施/已修复，也不授权重跑或更改源码/DB。现whole-runtime仍失败，旧失败/partial/实际advisory和全部291absence原字节保持。
