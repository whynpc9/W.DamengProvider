# TP07 T1 probe 对产品 v3 的有限兼容设计

2026-10-11。结论：**finite_product_v3_compatibility_design_ready / product_source_acceptance_and_new_root_approval_pending / no_implementation_or_execution_authorized**。本轮以实际冻结产品 v3 为下一工具版唯一当前产品，不先实施 v2 兼容。已冻结的 [v2 缺口设计](TP07-T1-probe-product-v2-compatibility-design.md) SHA `6072f4ff72aaa549e63ae4371e57f5a1ffc06bba53d3d7681dfaeed33ad2458e` 保持原样。

实际 manifest 是 **43字段**，不是任务最初描述的44；root已确认以实际内容为准，不补虚构字段。实际源仍178，probe source/locked闭包仍187/188。新 retry49/full824 清单是静态预期，不是新的 discovery/TRX 通过结果。本报告不代替另案产品源审、完整patch重建或单位行为审查。

本轮只读实际完整manifest、父引用、全量178 map、静态case inventories与795历史rows，并核对当前probe相关固定源码身份；只新增本文。未 import/exec 工具、运行SDK/tests/DB、读secret、改产品/工具/status/旧证据。产品 v3 源接受、Low新freeze和root七字段批准的实际新Ref尚待另案产生；本文不虚构这些路径/SHA，也不开编码或运行窗口。

## 当前事实与已确定的兼容范围

| 实际对象 | SHA256 |
| --- | --- |
| transaction-capability-manifest.v3.json | `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8` |
| transaction-capability.v3.patch | `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f` |
| README-transaction-capability.v3.md | `5863a9158ac88e643a534e6ab17f7ba478a70fb908626a324e840464af5e973f` |
| 实际178源aggregate | `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c` |
| DamengExecutionStrategyTests.cs | `cb7b6114490fc1fd34fc3c839e2fdc24d1eaeffdf8ed8b5ac6f81215e9fbfe2f` |

实际source_root为本workspace下 `.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source`；本轮枚举178实际文件逐项等于manifest。相对父fcb1的178 map，177项不变，仅既有 `test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs` 变化。相对原base175，173项不变、修改DI和retry unit两项，新增adapter/factory/transaction unit三项。五个patch header与manifest.patch_paths完全对应；完整hunk重建和产品测试语义留给另案source审。

当前probe v4的prepare仍是 `7824e133…`、validate `25d416d2…`、test_contracts `475e11f2…`、README `552e9878…`；policy `1a11cbba…`、run `3273ab44…`。旧task/32字段/旧三gates假设仍存在，新v3还改变source scope、base修改文件数与父provenance，因此不能只改task后缀或称整个probe改动为pin-only。现有test的v4 producer15 aggregate也必须随真实producer源码更新。

## 唯一当前合同：43字段及精确值

实际task只能是 `TP07-T1 transaction-capability and W retry-contract test isolated EF overlay v3`。下一pack（建议v5）仅接实际当前400123 manifest，拒绝932f/fcb1作为当前产品，拒绝任意task前缀、revision范围、两scope任选或未知字段。父v2只作有界来源读取。

完整顶层键集如下，High必须用exact shape检查；`int`不接受bool/float，bool不接受0/1，数组类型和顺序按各有限合同保持：

```text
EF_relational_API SDK_executed W added_files aggregate_format
base_approved_source base_lineage base_lineage_artifacts base_prepared_source
base_source_count base_source_hashes database_executed full_unit_inventory
modified_files namespace_boundary old_L0_E_D_cohorts_reused_as_new_acceptance
old_parent_source_approval_reused_for_revision patch_application_base patch_paths
planned_full_unit_cases planned_retry_unit_cases planned_retry_unit_methods
planned_transaction_unit_cases planned_unit_cases planned_unit_methods
required_next_gates required_source_approval_scope retry_contract_inventory
revision schema_version source_aggregate_sha256 source_count source_hashes
source_root static_checks_only status task tests_executed
transaction_capability_patch transaction_probe_or_E_observation_implemented
unchanged_base_file_count unit_delta_from_parent unit_revision_parent
```

| 类型/数量 | 精确字段值与关系 |
| --- | --- |
| int / 11 | schema_version=1、revision=3、base_source_count=175、source_count=178、unchanged_base_file_count=173、planned_unit_methods=4、planned_unit_cases=25、planned_transaction_unit_cases=25、planned_retry_unit_methods=21、planned_retry_unit_cases=49、planned_full_unit_cases=824 |
| bool / 7 | static_checks_only=true；SDK_executed/tests_executed/database_executed/transaction_probe_or_E_observation_implemented/old_L0_E_D_cohorts_reused_as_new_acceptance/old_parent_source_approval_reused_for_revision均false |
| string / 7 | task为上面的唯一v3值；status=`source_frozen_verification_pending`；source_root为actual adapter-v3根；source_aggregate_sha256为2fec；aggregate_format=`sorted relative source paths: sha256 two spaces path newline`；patch_application_base=`exact approved175 EF sources, already including the original bridge and temporal mapping`；required_source_approval_scope=`transaction_capability_and_w_retry_contract_tests_only` |
| array[string] / 4 | added_files为原三新增项；modified_files为DI、retry unit两项；patch_paths为两modified+三added的排序五项；required_next_gates为下列七个原始字符串 |
| object / 14 | 三Ref：base_prepared_source/base_approved_source/transaction_capability_patch；base_lineage/base_lineage_artifacts/base_source_hashes/source_hashes/namespace_boundary/W/EF_relational_API/unit_revision_parent/unit_delta_from_parent/retry_contract_inventory/full_unit_inventory |

source_hashes必须是有效有限相对路径→64hex的178项map，实际根目录完整枚举无额外项，精确等于root批准paths；base_source_hashes仍175。不能让178变179，也不能只接受`>=178`。173+2+3以及父177+1必须由两个实际map推导，不能仅信计数字段。DI path是 `src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs`；其它三个added path仍为Storage/Internal下DamengRelationalTransaction.cs、DamengRelationalTransactionFactory.cs和既有transaction25测试文件。transaction25的hash仍 `f4f689c8c2285ed4665880d65f0ccbd22424862dc1ac9eef1fc0cc69b44dad1d`。

W、EF_relational_API、namespace_boundary、原base six-lineage/five-artifacts精确值沿用已接受原合同，尤其固定W包/DLL/MVID、EF10.0.12、无官方driver复制、无W或生产retry逻辑改动；不新加包或framework依赖。

required_next_gates必须逐字符串、顺序匹配实际七项：

```text
Astra v3 complete178/test-only contract and full five-path patch source review
Low new immutable178 snapshot and independent full patch reconstruction from approved175
root new source-only seven-field approval bound to v3 manifest and transaction_capability_and_w_retry_contract_tests_only scope
new reviewed preparation revision with fixed v3 references and the single exact new source-scope guard
Low new source/native/locked build and source/binary/raw4/lock identity verification
Low actual retry49 TRX, original transaction25 TRX, and same-build unfiltered full824 TRX with complete declared case-set readback
new probe tool/product manifest compatibility revision and new source/native/build/compiled28/readiness before any separately approved four-case runtime
```

这些是静态交接门，不把已完成状态写回manifest；后续成功证据独立保存。`planned_unit_cases=25`是保留的transaction门，不能误当retry49或full824。

## 十个父引用与单文件差异

unit_revision_parent实际恰十键，下表完整列出。每个对象恰`{path,sha256}`，固定为actual400123中的具体路径，canonical absolute、workspace内、无symlink且实际bytes hash相等。本轮十个实际Ref均已hash匹配；不能只检查哈希格式，也不递归运行历史工具。

| 键 | 固定SHA256 |
| --- | --- |
| product_manifest | `fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432` |
| transaction_capability_patch | `5df9c73a4879fbf7f2cb79da48379a92856e093fecea7df3c31956080b1f3566` |
| source_approval | `d6cff0a1a92a1b8341b12c6dfaa352028039d8908fffd1b1387254561c3f0486` |
| source_review | `eb47339baa20c4bbedf2be7ef644b3b0c2a343a0f00d191745cc24e2e53ebbf5` |
| sourcefreeze_manifest | `a842630f569b6dfbeebaa464133ab4bc17cf42f2166691be337b7ac9483f1236` |
| full_failed_TRX | `f470a92ddb772627a311bf6bd9e2966826c4f960520c63f9c3aa5e375b009271` |
| full_failed_manifest | `61234feda70debd44fa17f290f376d0895abf94d18114649c5eded4f77daebd4` |
| full_failure_triage | `a97e8c4b48b0cf5003a24611fcb031505be4f88dfdcb3c3bd8f4547b8f063953` |
| full_independent_readback | `f4fa0be7bd222b7061a91912fc3c36877606d7818a56ef90636f7c53b6bbbc5a` |
| safe_failed_reasons | `cac4a3fb4b962f14d3723af9e28c9765c6894d27269d2aba9348e054c4dc22ed` |

有界读取父fcb1的36字段/178map及其freeze/approval关系；父freeze map必须等于父map、父approval必须绑定fcb1与父178，父patch必须等于父manifest引用。失败TRX/manifest/readback/reasons是固定历史输入，保留失败身份，不因v3出现而通过。原base175及固定base artifacts保持，不能把父178作为完整patch应用base。

unit_delta_from_parent实际恰九字段：changed_files为retry unit单项数组；unchanged_source_count为int177；parent_unit_sha256=`82caac3eca1dde1f3823ae3560d4f0ebb0c9388f18dc2380ad75cfdd9cf23285`；new_unit_sha256为cb7b；transaction_unit25_sha256为f4f6；W_package_changed/global_analyzer_or_build_rules_changed/private_failure_classification_injected/production_retry_logic_changed均bool false。父/新unit SHA必须分别等于各map对应项，transaction25必须同时存在且同hash，实际两map的全部差异只能为retry unit。

## 静态retry49与full824清单合同

本轮没有读test实现来重新评判重试逻辑，也没有跑测试；只按实际manifest及历史rows检查声明闭合。retry_contract_inventory恰九字段：inventory_kind、new29_planned_test_names、original12_failed_to_new_mapping、original20_to_new_mapping、original_class_case_count、planned_case_count、planned_methods、reflection_fixture_kind、synthetic_true_classification_is_transport_evidence。

其中inventory_kind固定`static source declaration and InlineData inventory; not SDK discovery or execution`，reflection_fixture_kind固定`exact actual nonpublic constructor signatures only; no private flags or fallback`，最后一项bool false；计数固定20/49。planned_methods是以下21个有序方法，每行恰`attribute/inline_data/method/parameters/planned_count/planned_test_names`六键。Fact的parameters/inline_data为空、count1；Theory各参数名称、行顺序、bool/int值及名称渲染按冻结manifest精确核验，不接受任意49个名称。

| 方法（依声明顺序） | 计划场数 |
| --- | --- |
| EnableRetryOnFailureUsesDefaultDamengStrategy | 1 |
| EnableRetryOnFailureAppliesCustomSettingsAndCopiesEmptyErrorNumbers | 1 |
| StrategyDoesNotRetryUnclassifiedWServerNumbers | 10 |
| StrategyDoesNotRetryNonTransientOrAmbiguousDamengCodes | 5 |
| StrategyDoesNotRetryNonDamengTimeoutException | 1 |
| StrategyRejectsAdditionalErrorNumberBeforeExecutingDelegate | 1 |
| CollectionOverloadRejectsNull | 1 |
| EnableRetryOnFailureMaxRetryCountPreservesDefaultDelay | 1 |
| EnableRetryOnFailureAcceptsNullAdditionalErrorNumbers | 1 |
| CollectionOverloadCopiesEmptyCollectionBeforeCallerMutation | 1 |
| BuilderCollectionOverloadRejectsNonemptyOverrides | 3 |
| BuilderEnumerableOverloadRejectsNonemptyOverrides | 3 |
| ContextConstructorRejectsNonemptyOverrides | 2 |
| DependenciesConstructorsRejectNonemptyOverrides | 6 |
| ContextConstructorAcceptsNullOrCopiesEmptyOverrides | 2 |
| DependenciesConstructorsAcceptNullOrCopyEmptyOverrides | 4 |
| ContextAndDependenciesConstructorsPreserveDefaultAndCustomRetryCounts | 1 |
| SyntheticPublicWClassificationControlsActualRetryAttempts | 2 |
| NonWDbExceptionCannotEnableRetriesEvenWhenTransient | 1 |
| ActualSealedCommitUnknownExceptionIsPropagatedWithoutRetry | 1 |
| ActualSealedWTimeoutExceptionIsPropagatedWithoutRetry | 1 |

两种mapping数组行各恰`original_test_name/original_outcome/new_planned_test_name`三string字段。original20按实际历史rows中该类原始顺序逐一对应，旧名、原outcome不可改；原20恰12 Failed+8 Passed，新的20目标名称唯一。original12数组精确等于original20的Failed子序列；new29为29个唯一新名字，与20个目标不相交；21方法展开的49个名字恰为20+29的完整集合，不许漏场/替换成重复名称。原十个旧默认码行改成对应W未分类不重试行，两单独失败分别映射到拒绝自定义额外码与空集合配置；原8个Passed保留名字。

full_unit_inventory恰13字段：added_planned_case_count=29、baseline_count=795、baseline_rows为Ref、inventory_kind固定`actual failed baseline TRX row set plus declared one-to-one test adaptations and static new cases; pending independent SDK discovery/TRX`、non_retry_baseline_count=775、original_retry_class_count=20、planned_full_case_count=824、planned_full_test_names为排序824唯一string数组、requires_actual_total_executed_passed=824、requires_complete_exact_case_set=true、retained_baseline_case_count=795、skip_or_abort_allowed=false、transaction25_unchanged=true。所有数值为int、三个标志为bool。

baseline_rows实际Ref为 `TP07-T1-product-v3-full-control/actual-full-rows.json` SHA `83a36972c1d668de0d471579c1f1d0f65599fec481c7ba682fa980ed5574fd5b`，本轮hash及全数组读取匹配：795行、783 Passed+12 Failed。它作为固定历史row来源，不能改outcome冒充新通过。设计要求核795唯一test_name及execution_id、有限行结构，提取20条retry原行作为上述映射源，其余775名原样保留；`planned_full_test_names == sorted(nonretry775 + retry49)`，其中transaction25的25名字也与原baseline一致。本轮静态关系检查全部成立；full824没有新TRX输出。

这些inventory是产品后续编译/独立discovery与实际retry49、transaction25、同build无filter full824的验收要求；不能只核total=824而忽略名称集合，也不能让静态计划数量满足实际门。probe自身offline28和live4没有变化，不将retry49塞进probe注册表。

## 新source-only scope与三类lineage

待另案实际源审和Low新freeze接受后，由root签新的七字段批准：schema_version=1、task=`TP07-T1-overlay-source-approval`、status=`source_approved`、manifest为当前400123 Ref、exact_count=178、exact_paths为完整排序178、scope唯一`transaction_capability_and_w_retry_contract_tests_only`。这仍是source-only；旧overlay-only scope不足以表示既有retry测试修订，必须拒绝。producer和validator分别以固定新scope检查，同时要求等于manifest.required_source_approval_scope；不能只相互相等而容许两者共同改错。新批准Ref实际path/SHA以root之后签发为准，本文不预填。

source-only批准不要求未来retry49/transaction25/full824先通过；先有合法源批准、再由root开各实际SDK/test窗口，故无循环。完整产品单位验收是root推进probe后续执行的外部门；新source审核本身不授予SDK/probe/runtime。另一个runtime approval必须有独立 `runtime_authorized`，不能重用七字段source批准。

工具inputs/source/startup的十键lineage保持结构：approved_ef_source_manifest_sha256必须指当前400123；product.base_lineage的同名字段仍dc2bfd（base175原始批准），不能整对象相等覆盖当前值。transaction_patch_sha256指d13f；parent_overlay_sha256仍指base_prepared_source `9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a`，不是fcb1；父产品fcb1只由unit_revision_parent表达。probe_source_manifest_sha256/toolpack_manifest_sha256绑定新实际9host/17pack制品。

ef_overlay.source_root必须等于manifest的actual adapter-v3根，不可直接换成将来unitfreeze目录以规避source_root equality。新Low freeze实际178/map/aggregate与400123的对应由root输入准备和独立回读证明；保持原13个输入顶层字段及4个ef_overlay键，不增加伪造的approval字段。

## 下一pack的四文件范围与下游消费

| owner文件 | 具体最小修订 |
| --- | --- |
| prepare.py | source边界的唯一v3 task/43字段/新scope/五patch路径/173base不变/父177不变/十Ref与inventory关系检查；允许局部只读/纯校验辅助函数，source复制、阶段CLI、process/native/build/offline/readiness算法保持 |
| validate.py | product_manifest_contract及其局部辅助合同独立实现同一有限v3；approval_contract中source approval的固定scope也必须改到单一新scope，防止prepare通过而runtime validator仍按旧scope拒绝；其余native/offline/runtime行为不改 |
| test_contracts.py | 新v3真实正例/语义负例、新scope审批门回归、新15 producer pin；保留原40测试及原模块异常类型修复，不拿旧synthetic inputs fixture当完整产品正例 |
| README.md | 明确唯一当前v3、源审批新scope、父失败/静态case inventory与实际结果区别、新pack及必要新窗口 |

prepare旧source approval字典和validate.approval_contract的source_approval精确字典都曾固定`transaction_capability_overlay_only`，这两处必须同时处理；只修product_manifest_contract不足。原source task检查、modified_files singleton和validator unchanged_base174也必须更新为本合同。独立validator仍在approval链里重新读取真实产品来源，root须在DB前完成该合同回读，不能只依赖producer flag。

native/build/offline/readiness读取阶段自己的TP07-T1 task，没有另一个产品v1 task；它们以同inputs、source187、原生图和实际binary refs连接新源。run.py不直接解析产品manifest task/shape，host ProbeIdentity消费startup/source/native/lineage与实际DLL，因此本兼容修订不需改run或9host。policy的178/187/188、17文件/9host、13输入键仍准确；五helper原始字节证明、源根/命令边界不变，policy也无需新增版本字段。其余13文件保持冻结。

新源码187=当前178+固定host9；locked188仅再加host packages.lock.json。原Core13+W/native与Core12+W/runtime、NuGet metadata contentHash/ZIP sidecar区别、TLS/证书、180/180/180+2秒reap、Savepoints/AutoSavepointsEnabled、shared/TLS能力、fixed4顺序、独立cleanup全部保持。本设计不因测试manifest修订引入新依赖、W包或产品行为变化。

独立 product preparation-v4 是另一文件组和另一窗口：它至少有新产品pins及单一source-scope guard变化，不能称纯pin-only；实际retry49/transaction25/full824及完整case-set的产品验证计划由root另案明确。本文四文件授权范围不包括任何product-preparation目录，不允许probe owner顺手修改它。

## 验收与实施顺序

1. 先等待另案Astra产品v3 source acceptance、Low新178/完整五路径patch证明和root新七字段source批准；root明确开编码后才实施四文件。现有source仅冻结，不在本报告中自封source accepted。
2. 直接交付新probe v5（或root指定下一版），不先做v2版本。High给四文件diff与完整17 hash map后停写。新15 producer aggregate排除validator/test，保留v4两个host覆盖值，再叠加当前prepare/README真实新hash；不得删除pin门或把含test自身的17 aggregate嵌回test。实际policy hash继续1a11cbba，旧v4 pack和40结果不覆盖。
3. Astra单次源码验收；Low新17快照、toolpack、新实际host9 manifest回读。host9字节未变可以aggregate同旧，但新artifact路径/Ref须真实，source/native/build等阶段必须用同一新inputs，不混旧pack产物。
4. Low独占无SDK窗口跑原40加新合同。必须实际调用v3产品语义和新scope合同；真实400123及签出的新批准正例通过后，再做内存字段mutation，不能让所有负例仅因manifest整体SHA改变而提前失败。源码/manifest fixture通过真实路径和固定Ref读取，禁止模块ROOT伪装、import producer来制造oracle、删真实字段或假造SDK/runtime通过证明。
5. 强制负例包括：43字段逐项missing/extra；旧task/v2 revision/future revision、wrong primitive type；旧fcb1整体输入；178→179、187/188/count-only；两modified丢retry或多第三项、五patch漏项；父十Ref逐一wrong SHA/path/symlink/missing；父map/approval不对应、第二源差异、unit SHA错误；旧scope、两个scope任选或manifest与approval一起改成wrong scope；toolapproved误用dc2b/fcb1、parent_overlay误用fcb1；七gates变旧五/三项。inventory负例要覆盖49/21/824/795/775计数、方法/InlineData类型和顺序、重复或漏名字、原20不一一对应、原12失败被改Passed、new29与20重叠、full只替换一名字但数量仍824、transaction25集合被换、planned被误作passed、transport_evidence=true。
6. 产品实际retry49/transaction25/同build full824全量门关闭后，root按独立窗口安排probe新source187→native→locked build→实际compiled28+Python→readiness回读；这些真实门不能由manifest计划替代。未改process算法的历史有界取消证明可以保留，不只为manifest revision重复耗时取消实验。
7. runtime仍需单独root批准、TEST身份/profile先验、四固定case/精确cleanup/独立validator与最终接受；本设计不授权SDK、DB或下游L0/E/D。首失败保存并停止，不改旧失败或临时放宽契约。

当前最小阻塞是尚未完成的产品source接受、新freeze和root真实新批准，不是缺少宽松版本兼容。本文完成后停写，等待root明确下一窗口。
