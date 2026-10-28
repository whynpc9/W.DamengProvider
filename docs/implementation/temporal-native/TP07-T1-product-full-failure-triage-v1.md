# TP07 T1 首次完整单位门失败分诊与最小修复设计

> 阅读提示：这是历史测试失败归因与修复记录，原完整门失败不得改写为通过。固定候选、状态和审批仅属于当时；当前驱动行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-11。结论：**full_failed_preserved / W_bridge_counterpart_tests_missing / test_only_revision_required**。本次 12 项失败全部来自原官方驱动的错误号重试断言仍用于 W bridge；它们与 P1 已明确批准的保守重试边界冲突。不是本轮事务 capability adapter 回归，也没有证据要求修改 W 产品。完整门仍失败，不能把 783 项通过接受为 full，也不能将已有 target25 通过扩展为整个 T1 接受。

本轮仅只读实际 TRX、独立收据、原 P1/175/178 源码和 manifest、patch、准备脚本及 TP06 源码 tar；计算文件 hash、解析 JSON/XML，没有运行 SDK、测试、准备工具、validator、程序集、数据库或 secret wrapper。唯一新增文件为本文。以下是供 root 审批的修复设计，不授权 High 开始修改或 Low 开始执行。

## 1. 失败确实来自本轮同一构建

所有相对本地证据路径均位于 `.local/temporal-native/verification/`。

| 证据 | 实际 SHA256 |
| --- | --- |
| `TP07-T1-product-v3-full/manifest.json` | `61234feda70debd44fa17f290f376d0895abf94d18114649c5eded4f77daebd4` |
| `TP07-T1-product-v3-full/results/full.trx` | `f470a92ddb772627a311bf6bd9e2966826c4f960520c63f9c3aa5e375b009271` |
| `TP07-T1-product-v3-full-control/independent-readback.json` | `f4fa0be7bd222b7061a91912fc3c36877606d7818a56ef90636f7c53b6bbbc5a` |
| `safe-failed-tests.json`，同 control 目录 | `c206cc7038bcc5bf1f56dfdd27377708f531386902c8027ef4e3161b2c50224f` |
| `safe-failed-reasons.json`，同 control 目录 | `cac4a3fb4b962f14d3723af9e28c9765c6894d27269d2aba9348e054c4dc22ed` |
| `actual-artifact-hashes.json`，同 control 目录 | `ba975a7aaa7e3facc6bc5a2bb09d04d10d8dda58d594a24fefd7e19b768a17a4` |

manifest 中真实命令是 SDK10.0.401 的 `dotnet test test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --no-build --no-restore`，没有 filter，带单节点、禁共享编译/服务器参数，输出独立 full.trx。SDK 查询自然 exit0；单位测试自然 exit1，耗时 `1.2714515831321478` 秒；无 timeout、cancel 或 termination，进程组最终 absent。`error.code=natural_process_success300` 是执行成功守卫对 exit1 的拒绝，不是 300 秒超时。没有重试。

本 reviewer 直接解析原 TRX：total/executed795，passed783，failed12，notExecuted/aborted/timeout/error 全0，summary outcome Failed。失败全部属于 `DamengExecutionStrategyTests`；该类实际20条中8 passed、12 failed。Low 独立收据与实际 TRX 相符，窗口已 released，DB/probe 未执行。

本 reviewer 逐文件复算新 build/work 的178份产品输入及原 v2 source_root，均匹配 `transaction-capability-manifest.v2.json` 的完整 map。产品 manifest `fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432`，source aggregate `a0c14e430c72ede7deb257fee16e175b9d06b1862abec59f417aedc7bd6f8724`；本轮 build 收据记录 EF provider DLL `0a71e775cfe582d1f17afc89b6e066346678587f6269e3d2b671ddc9b8612547`、unit DLL `0d8f20d7e43926f723cb9aba968413bc64bf373be785515e687c84afb9166119`。

Low 的独立前后比较证明12901保护项、178源、两锁、完整 raw4、158 binary 和原 target25 TRX 未变；本 reviewer 没有重新枚举12901项或重新读取 PE MVID，因此这些保全结论明确归于 Low 收据。target25 原 TRX `d0cd104d7db13afd8238056d9963c611fbf75fa560b6f41c82b63d315d68282f` 的25/25仍只属于原同一构建。

## 2. 实际12条失败与调用链

下列位置均指 `TP07-T1-product-v3-build/work/` 内实际参与构建的源文件。表中异常类型、断言位置直接取原 TRX；没有复制异常消息、连接串或原始认证信息。

| 失败条目 | 实际停止位置与原因 |
| --- | --- |
| `StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(-3003)` | `DamengExecutionStrategyTests.cs:64`，Assert.True 得到 false |
| 同方法 `-3404` | 同上 |
| 同方法 `-6003` | 同上 |
| 同方法 `-6004` | 同上 |
| 同方法 `-6010` | 同上 |
| 同方法 `6001` | 同上 |
| 同方法 `6027` | 同上 |
| 同方法 `6060` | 同上 |
| 同方法 `6089` | 同上 |
| 同方法 `6123` | 同上 |
| `StrategyRetriesAnAdditionalErrorNumber` | 测试:94 → 子类构造:172 → `DamengRetryingExecutionStrategy.cs:87` → `ValidateAdditionalNumbers:120` 抛 `System.NotSupportedException`；Execute/attempts 尚未执行 |
| `EnableRetryOnFailureAppliesCustomSettingsAndCopiesErrorNumbers` | 测试:28/30 → `UseDameng` 泛型:93/非泛型:31 → `DamengDbContextOptionsBuilder.cs:71` → `RejectNonemptyOverrides:83` 抛 `System.NotSupportedException`；列表后续 mutation 与 context/strategy 断言尚未执行 |

安全摘要中后两条 `safe_reason_type=assertion_failure_other` 是有限分类标签，不是实际异常类型；本次 `System.NotSupportedException` 判断依据原 TRX 的 ErrorInfo 和实际产品源调用位置，不从该标签猜测。原 message 保留于 TRX，不作为报告输出。

十条 theory 的完整源调用链为：`CreateDmException(errorNumber)` 用 `DmError(int,string)` 和 `DmException(DmError)` 的非公开构造函数创建实际 W 异常 → `TestShouldRetryOn` → 生产 `DamengRetryingExecutionStrategy.ShouldRetryOn:113–114` → 生产 detector:8–10。该 detector 只接受 `DmException { IsTransient: true }` 且排除 `DmCommitOutcomeUnknownException`，不读取 `Number` 或历史错误号集合。

TRX 实际已到达 Assert.True 并观察 false；若 fixture 的构造函数缺失或反射调用失败，会在进入该断言前抛出不同异常。本次没有 `NullReferenceException`、`MissingMethodException` 或 `TargetInvocationException` 的失败证据。因此“官方 DmException 反射 fixture 形态变化”不成立为这12条的原因。fixture 仍可构造 Number，错误是把 Number 当作安全重放分类。

W 源码证据来自 TP06 已固定 `product-source.tar`（SHA `836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441`），以 tar 字节读取，无解包执行。`PublicApi/DmException.cs` 的 `DmError` 构造仅填错误集合/Data，未设置 FailureInfo、未 override IsTransient；`DmTimeoutException` 与 `DmCommitOutcomeUnknownException` 明确 override false。这三份归档源与本工作区当前源逐字一致，hash分别为 `2c135d63…`、`dd2214ea…`、`3e11a220…`。这里没有把 W 当前没有的 transient classifier 凭空补成运行事实。

## 3. 原官方合同、P1桥、本轮T1的责任分离

原 EF commit 固定 `5863d3755dbd2d333ac359bf1c0a789762adbaed`。实际 `.local/temporal-native/TP07/v1/base-source/` 中旧测试 SHA `7a98fb5c5c83d33530889cc02d47b4ad6890eb2a35b2c01c62034439982ef98b`，与本轮文件仅有 `using Dm;` → `using W.Dm;` 的逐字差异，所有旧断言都保留。

P1 `driver-bridge.patch` SHA `08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5` 明确移除了十个错误号集合及 additional-number OR支路；增加 builder 两入口、strategy 两构造的非空覆盖拒绝。对旧测试的 patch hunk只有命名空间替换。P1 bridge manifest SHA `4750a330f2f1e63a5e40780acf19820b38b86db2c1bce08ff224600a18b8b8b4` 的 pending_contracts 明确保留官方 retry-number 预期作历史源码，并将 W counterpart/safe retry验收列为未来范围。

[P1静态审阅](TP07-P1-astra-review.md)第1/2节当时已经明确这一范围；[专项设计](TP07-astra-design.md)要求保留 W 安全重试语义、以显式 W counterpart 描述差异，不能删旧测试类掩盖失败。这不是事后为失败寻找放宽理由。

| 文件，路径省略共同项目根 | 本轮 SHA256；在 P1 bridge、175 base、T1 v2 map 与实际 build 中均相同 |
| --- | --- |
| `test/.../DamengExecutionStrategyTests.cs` | `82caac3eca1dde1f3823ae3560d4f0ebb0c9388f18dc2380ad75cfdd9cf23285` |
| `src/.../DamengTransientExceptionDetector.cs` | `41770d853c5926008bb5f2a56cd01a08a2806d4199138ad150f325e9352e9a6e` |
| `src/.../DamengRetryingExecutionStrategy.cs` | `1d0ec9197b20fea42bab0c7eea3ee9db4fcd28cf537ea8a1678fbcb03371014f` |
| `src/.../DamengDbContextOptionsBuilder.cs` | `a14376e3643cd9d625b1ce4176a7dc905e602c316868b60af604b20f9c5674c1` |

T1 原完整patch仅改 DI注册并新增 adapter/factory/unit三个文件，这四路径与上表不相交。新事务 adapter 不在12条失败的源调用链上。当前问题是扩展到完整 EF 单位门时正式暴露 P1 尚未补齐的 W 测试合同，不是需要恢复错误号重放的产品缺陷。

M1-v5 历史 summary SHA `6874f0f0d10259e162853391b0349d513bc02ef38da398760c6420a9853fec08` 的实际命令与两份 TRX均已读回：新映射82/82、affected102/102，两项交叠，并集182；所有命令都有指定 filter，两份实际结果均没有 `DamengExecutionStrategyTests` 行。discovery 成功也不等于执行 full。[M1-v5接受报告](TP07-M1-v5-acceptance.md)未声明完整单位通过，不能拿该历史记录反驳本轮12条或当作它们曾经通过。

## 4. High 最小修复范围与应保留的合同

只允许在**新的隔离 v3 source_root**中修改一个既有文件：

`test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs`

从已审 v2完整178源复制，原目录和失败 build不改。三个 W bridge 产品文件、T1 DI/adapter/factory/25项测试、W产品、项目/props/global/locks均冻结；不移除类、跳过用例、用 filter排除失败、关闭分析器或 Nullable。允许改测试方法名以正确表达 W合同，但在新manifest/README列出原12条到新case的逐条对应关系。

1. **保留原十个错误号数据行**，改成 W明确合同：反射构造后检查实际类型、Number等于输入、IsTransient=false、ShouldRetryOn=false；通过真实生产 strategy.Execute 抛出同一异常，断言 attempts=1、原异常同对象传播。保留原5个非瞬态/模糊错误号、非 W Timeout、默认设置和 collection-null负例。这样证明错误号从未越过公共分类边界，不是简单反转十个布尔断言。
2. 原custom-settings/copy场景改成**合法空集合配置**，继续核maxRetryCount=3、delay=4秒、AdditionalErrorNumbers空且不可变；配置后再修改调用方原空集合，最后创建context/strategy，证明snapshot未变。另核合法null语义。非空集合不再宣称可成功：builder collection入口、builder三参入口，以及strategy context/dependencies实际构造入口各自必须拒绝非空值。保留原 `-12345`、`-54321` mutation与 `-77777` 额外错误号作为可追溯数据，不能只删掉这些输入。
3. 原additional-number执行场景改成验证构造阶段拒绝并且业务委托 attempts=0。另增**独立的公共分类正反控制**：通过 protected DmException构造的测试专用子类只覆写公开 IsTransient，分别为true/false；执行真实 strategy.Execute，以最大2次重试、零延迟、前两次抛同一异常、第三次返回42的委托证明true时3次/返回42，false时1次/原异常传播。再加非W DbException即使IsTransient=true也不得重试，证明 W 类型边界。不得伪造Number OR支路。
4. 明确上述true子类只是**公开分类消费的合成控制**，不是当前 W已产生安全瞬态异常的证据。当前 W没有公开创建 classified-transient 的工厂；不得通过设置私有 FailureInfo/Outcome旗或改W产品来制造“真实classifier已通过”。实际生产 DmCommitOutcomeUnknownException采用明确签名的内部单Exception构造反射fixture，先 fail-fast核ctor/实际sealed类型/IsTransient=false，再经生产 strategy.Execute核只执行一次、同异常传播。可补ExecuteAsync同类正反控制；不得将构造出来的异常说成实际COMMIT丢ACK。真实事务producer语义仍属于既有W事务门和后续T1实库门。

反射fixture只负责生成原有实际类型/Number或sealed CommitUnknown对象；构造不存在必须明确失败，不得catch后改成普通Exception兜底。对Number fixture不增加私有classification注入；对正控制使用合法protected公开继承边界。所有成功、拒绝和次数判断都经过生产EF策略，不自造一个镜像detector。最终新增case数由High冻结列表及Low正式TRX确定，不在设计阶段宣称固定新总数。

**W包决策：**本轮最小修复是EF测试counterpart，W仍使用TP06候选 `0.1.0-temporal.20261008144915`，nupkg `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980` / MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。无须为这个测试修正重打W包；若实现中发现必须改W产品或public exception contract，应立即停下另交产品设计，新源码/新包及TP06整条验收都要重做，绝不能复用843包的接受身份。

## 5. 新完整来源链，不能复用旧批准或仅交单文件diff

新source仍178文件；相对v2仅上述一文件变化，其余177逐字相同。相对原批准175 base为173 unchanged、2 modified（原DI与retry unit）、3 added（adapter/factory/capability unit）。新增正式 `transaction-capability.v3.patch`、`transaction-capability-manifest.v3.json`、`README-transaction-capability.v3.md`；patch须可从原175 base完整重建全部178，完整5个变化路径，不能把本轮test-only父差异错当交付patch。

新manifest记录新完整map/count/aggregate、相对175的变化、相对v2的单文件delta、旧full失败与本文引用、完整父lineage及12条case对应/新增case清单。`planned_unit_cases=25`仍专指事务能力类，不应改成retry类或全集数；另以明确字段记录retry预期与完整测试清单。Low在新目录独立snapshot/内存或隔离patch重建后，Astra/root源码review，root重新签七字段source-only批准，绑定新manifest与178 exact paths。不能继续拿v2的 `d6cff0a1…` 当作新批准。

新增retry合同已超出原scope字面的 `transaction_capability_overlay_only`。建议新有限scope为 `transaction_capability_and_w_retry_contract_tests_only`；root正式批准这一范围后，新的 preparation-v4 绑定新manifest/patch/review/freeze/approval/source folder，并把原第411行的严格scope比较切换到这个**单一新值**，不能接受任意scope或兼容旧批准。这是“新pins+一处有限scope守卫修订”，不能再标纯pin-only/可执行AST完全一致。源数178、source-only非循环批准、原12refs/3folders、五阶段及取消/300秒/reap、51record/50archive/49非W包、完整raw4/pruning272/锁守卫保持；算法其它修改需另外说明和审阅。

新 Low证据与工具审核完成后重新执行 source→native→locked build。旧raw/锁/包图只能作固定比较基线，不能拼接旧source/native/build收据；provider/unit DLL/deps与实际加载W身份均重新记录，不能因为产品CS未变跳过本次构建。旧 v2编译失败、v3 full失败及全部旧source/approval/证据原样保留。

## 6. 独立验证与接受门

Low独占新窗口，先完整重跑修订的 `DamengExecutionStrategyTests`（原20场逐一追溯、覆盖原12失败及所有新控制），再原事务能力类25场，最后在**同一次已审构建**上无filter完整单位门。先后每阶段自然exit0、正式TRX、零skip/failure/abort/timeout；任一失败保存并停止，不现场修源或重跑。

实际795是本次已知完整基线，不能退回 `count>25` 就宣称全套。Low独立回读新完整TRX：原795覆盖全部保留，允许经声明的一对一case改名/合同适配及新增行；其余783基线测试不丢失，原12条都有对应新case，原25事务case仍全部在内。新预期总数应为795加明确新增数，不能靠固定795遗漏新增测试，也不能只看summary总数。准备工具原通用full检查仅 `count>25`，故精确集合核验必须由独立读回显式完成；不以本报告默许降低full接受门。

之后才可继续probe新source/native/build、实际编译28门及其后真实运行，并将L0/E/D来源改绑新178 revision。此报告没有开启probe/数据库窗口，也不接受原部分runtime。报告完成后停写，等待root对上述test-only范围及新scope的审批。
