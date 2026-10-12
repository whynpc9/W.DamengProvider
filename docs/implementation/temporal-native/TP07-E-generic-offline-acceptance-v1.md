# TP07 E精确generic负例修复：v11/v5新cohort离线验收

2026-10-10。结论：**new_generic_cohort_offline_accepted / new12ref_approval_readonly_joined / readiness_and_runtime_pending / DTO_live_pending**。本范围未发现阻塞。只接受新源码/locks/build、Python合同、实际编译partition与GeneratedCreate28及其独立gate；没有接受DB、D、完整C或TP08。

本reviewer只读文件、JSON、源码和SQL字节，计算hash并核关联；未执行SDK、tests、validator、host、wrapper或DB，未改源码/旧证据/status，仅新增本文。

## source、locks与新编译身份

| 输入（相对`.local/temporal-native/verification/`） | SHA256 |
| --- | --- |
| TP07-E-combined-v5-prepare/source-manifest.json | `9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a` |
| TP07-E-combined-v5-lock/lock-manifest.json | `5442869774a9061370aff3f871f8367950e66f3081e8946ae69d5292cf30684b` |
| TP07-E-combined-v5-build/build-receipt.json | `5bc995cd03cd24357c961edd9b8fad5300e09d7f10d2acab7a014c75d8936a1d` |
| TP07-E-combined-v5-build/startup-manifest.json | `07f4a98353fc55a69ab411753ec2f471c1cbcd819795009b23c6afd617002341` |
| TP07-E-tools-v11/toolpack-manifest.json | `a2c5592e0f30808c37cfb83e23d27717f1224ae491f6061b327c520f48207d3f` |

本轮实读prepared191条map，相对combined-v4只有`test/TP07TemporalRuntime/SixPrecisionCases.cs`变化；其hash为已接受`6acc99f9…`。Program及GeneratedCreate方法、Models/其它case类、common/L0/W/provider175源码保持原字节；不是以新编译结果反推源码未变。新startup中的193项source、3个locks、3个assets、50项binary全部逐项当前hash匹配，v11八工具live hash全匹配。policy为`2181388e…`，E9 aggregate为`d3e2227e…`；338 IDs/值/API/顺序及固定25窗不变。

native13 non-driver records与前cohort相等，三active locks map完全相同，实际文件也hash匹配：provider `77d4c083…`、LiteralProbe `89625899…`、TemporalRuntime `3e39a9de…`。source/lock控制收据分别SHA`399432400199e0ed49de5cc2d1934bcc78f70f2d414bd9bc41f8c3b8de3a19b4`/`1a0f349692cc28d91172f56e448c9d192d13bb3b207b01fdc752611144e99699`保留原cache/feed/archive/contentHash证明。

build实际五个事件为SDK版本、两host各locked-restore/build，均自然exit0/host0、无timeout/termination，SDK10.0.401。独立build readback SHA`b90846dec7928005ac12e6e081e59ef2de93b08c969b736cccbe8dd4435fe49b`与实际build/startup相联。新E host DLL为`8bc5e65584aa6bc4c9136ce73c0f0e795ed9ab99ed8f7d1add79582918353b4a`、MVID`633b1738-6dc6-499b-8c9a-7c096bb140c4`，deps仍`6537a024…`；新LiteralProbe DLL为`02ff00d9…`，其真实身份单独记录，不改历史L0 proof。

当前provider DLL `40af92c923e1b005bdf8182d3a60200eba6292f22cbc48aeff60aa84c5c2404f`、MVID`f39a0146-d836-42fc-8824-109eb899ddaf`；W仍原包`da8349a2…`、DLL`843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`、MVID`aa23e673-957c-4b1e-bf0f-291ff60077e6`。上述当前binary SHA已实读；MVID由Low编译/纯PE/host收据关联，本reviewer未再次执行提取器。

## 实际合同与partition

Python28方法实际exit0、failed/errors/skipped均0，四synthetic正例及134mutation所属两方法完成；单个subtest未另插计数器，此数量来自已冻结有限循环，不能当DTO实库证据。Low合同summary SHA`b9ffba1e050bdca274ed6d651eeb0a889b29b06719d214a6d28fe02638d05cae`保留3434项未变及source4/pack8/source24一致。

新编译`--offline-partition`自然exit0/host0，约0.090046291秒、固定180秒界限；独立partition validator成功。实际报告338个定义、25窗与当前policy字段/descriptor逐项相等，九项非法参数拒绝齐全，same-window async positive-prefix仍受compiled和独立门检查。guard/Context/OwnedObjects/DB构造旗均false。这是当前DLL真实执行，旧partition不替代本次结果。

## 新GeneratedCreate28与服务身份

shared14/TLS14新host均自然exit0/host0、无timeout/termination，分别0.43061520787887275/0.4345641250256449秒，固定180秒上限。原报告路径、命令`--output`及当前startup/DLL绑定一致。

本轮独立文件比较28个case的十项spec/model/列/default/seed/operation/SQL描述与受审v4、v5对应字段，无漂移；34份实际SQL逐项SHA及字节与v4匹配，Low保存verifier另核v5字节。N VARCHAR(32)/maxLength32/非Unicode或native无N的语义保持，case和整report的sync/async open/command观察均0，所有factory connections Closed且W身份一致，每profile29条。没有DB或TEST wrapper。

generator嵌套`assembly`严格绑定新provider `40af92…/f39a0146…`；model differ是扁平身份，类型`Microsoft.EntityFrameworkCore.Migrations.Internal.MigrationsModelDiffer`、assembly为`Microsoft.EntityFrameworkCore.Relational, Version=10.0.12.0, Culture=neutral, PublicKeyToken=adb9793829ddae60`，DLL`a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d`、MVID`03b92316-8856-455e-a1c0-0602583c8738`。新replay实际Relational DLL SHA与startup和28项service proof一致；它没有被错比成provider。

保存verifier `TP07-E-generic-v1-generated-create-control/verify-stored-v1.py` SHA`a76c814934c6f41fe07aa3b5c81cf75d25e423fe29a3e9fb35f392e725401003`已只读检查：从新replay精确Relational路径及assets来源读取，调用原纯PE reader取MVID，再逐case比较；generator走独立provider比较。其实际成功gate/readback与28条service proof关联，本reviewer没有执行该脚本或gate函数。service proof SHA`c1b6936a44ffa16d576eb45c6dc320f2c1d0f4302bf1df64a501c09c611719ae`，全部service与实际case逐项相同。

## 两份有限gate

| artifact（相对verification） | SHA256 |
| --- | --- |
| TP07-E-generic-v1-partition/host/partition.json | `c0eab418e14fc318545ab023a4e2058aa15b8dc1fd3bd39adb779fec613f633a` |
| TP07-E-generic-v1-partition/validation.json | `eadf9a48b05428a52500b8ceacdf433b14873dd34ecb044abd8035cb81033a2d` |
| **TP07-E-generic-v1-partition/partition-gate.json** | **`c7c2a0a8df0dc3761247b4c9f853e91b9e5eda55d4f42eade65362ef40811c30`** |
| TP07-E-generic-v1-generated-create/shared/offline-generated-create.json | `567b96357dbae3cf5adefd09079111a10362b88d7cf802cdb7b4c657343927ff` |
| TP07-E-generic-v1-generated-create/tls/offline-generated-create.json | `9a83c06194404b827e3279d201de090514dc93aa44e88cc4281ded12a6eb934c` |
| TP07-E-generic-v1-generated-create-control/independent-readback.json | `415d8048eb97dfaa4e139d8b62f6129c7db0dfcc6571e4eb00337651f5080ad1` |
| **TP07-E-generic-v1-generated-create-control/generated-create-gate.json** | **`9055a736e9fafe2f99a5713a716a0112edb1e4eda75d5c69327502fc8e42ffba`** |
| TP07-E-generic-v1-generated-create-control/gate-function-readback.json | `86bb3a1e7b55090fa5f47afa64a4fb919059981c2fa964c4a99b60e3b6050b7e` |
| TP07-E-generic-v1-generated-create-control/verification-summary.json | `b5387fade28e58788b3d9673daa5cd0219338cd1a43a8a63bfea80df5c511094` |

两gate和partition validation六项identity完全相同：当前source/locks/startup/toolpack/policy及window-contract `9937d5f2…`。本轮展开38条文件引用全部当前SHA匹配。原v4深验`c6f269c5…`和v5基线`ec3f90e7…`仅作为既有semantic/SQL基线，不能替代新host身份。冻结offline_gates实际78项nested pin检查通过、host_rerun=false；4947项prior pins前后映射本轮实读相等。全集当前文件不变为Low保存证明，本reviewer另重hash了本报告所列新source/lock/assets/binary/pack/SQL及引用集合，没有冒称执行4947项或78门函数。

## 新12-ref批准及原L0自然根

root新文件`.local/temporal-native/root-approvals/TP07-E-generic-v1-runtime/approved-execution.json` SHA`90bb49cf71affea5a9d6aa62ca13ea05ca22bc0275098e1ef2a24551dc8b5f21`已只读实核：有限12refs齐全且每个实际hash匹配，dotnet host字节及10.0.401固定身份正确。两新增gate分别是上表粗体项；source/locks/build/startup都是combined-v5，未套旧cohort。

L0 run/startup/toolpack/validation四引用与原批准逐项路径+SHA相同。原L0 toolpack仍`f1039bac…`，其自然source根`TP07-L0-tools-v3/source`六文件当前bytes全匹配；原run所记录toolpack身份和before/after保持一致。没有从E新根加载替身L0 validator，也没有改历史L0 proof或ROOT适配。新12-ref文件通过本次静态关联不表示本reviewer执行了inputs/readiness；Low的READY-ONLY结果仍由root单独核实后才开DB窗口。

## 后续与历史边界

旧window-v1首失败报告SHA仍`fa0a032fe9bd6d7a9502c17f09064efc6702c660e2bbc02dce1cf4be08f9f45d`，旧20窗277例、partial/absence、N误推断与generator/differ纠正报告均保留。不把新离线通过回填旧失败，不把synthetic DTO或编译partition当DTO真实拒绝/health证明。

root readiness完成后才能安排同一新run的全部25窗/338例，保留900/300/180界限、每窗main finally、独立exact-ledger cleanup及validator、全量覆盖和fail-stop。DTO仍live pending；TLS能力七位仍关闭。此离线验收不接受任何实库结果，不关闭D/完整C/TP08或后续LocalClaude门。
