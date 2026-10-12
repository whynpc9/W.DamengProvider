# TP07 T1 probe 对产品 v2 的有限兼容设计

2026-10-11。结论：**confirmed_product_v2_contract_mismatch / finite_v5_design_ready / implementation_waits_for_settled_product_manifest**。当前 probe v4 会拒绝已批准产品 manifest `fcb1d60f…`，原因是准备入口的旧 task 和独立 validator 的旧32字段/旧三项 gates 合同。应为确定的新产品契约做下一版工具包，不能修改真实产品 manifest 去迎合旧工具。

本轮仅只读完整 prepare.py、validate.py、test_contracts.py、policy、README，并检查 run.py、ProbeIdentity 的消费路径、实际新旧 product manifest、父引用、新 source approval/freeze/review；仅新增本文。未 import/exec probe/validator，未运行 SDK、tests、DB 或取消实验，也未写工具、产品、status、历史证据。

**实施前置状态已变化：**root 在本设计期间通报本轮 full units 实际795场、783通过、12失败，集中于 DamengExecutionStrategyTests，已首失败停止并交独立分诊。本设计没有重新检查或接受那轮测试结果。下文只针对已核实的 fcb1 产品 v2；在 full-unit 修复范围及最终 manifest 确定前，禁止开始本兼容修订。若产出产品 v3，必须先审阅其真实有限字段、task、来源与差异，再明确替换本设计中的当前版本常量；不能使用 task 前缀、任意 revision 或宽松 shape 预先兼容未来版本。

## 只读基线与确定缺口

| 当前 probe v4 文件 | SHA256 |
| --- | --- |
| prepare.py | `7824e133d587122945f564dbea358c04a4fe16a3b39796b9662ff847e348dc5b` |
| validate.py | `25d416d2f4a646499b4956d0c35608f593bdab1957a924755c6f60cb71898e0e` |
| test_contracts.py | `475e11f2cb4741d73c52e2d1148e1bd7d369b67a35df0843f944903b1212163d` |
| README.md | `552e987892aa46ae78f0f831931da86c009d0d0eeee5c1063df4cbce0b1bb1be` |
| probe-policy.json | `1a11cbba01a1b9626151fd99676aadc88f66a8151bfd04fd4dab3713f7d33b35` |
| run.py | `3273ab44027878ba2f216b98725a23257b51786ea161d33b14a6ef2f9bedd5ca` |

1. `prepare.py:188` 的 `finite_new_overlay_manifest` 精确要求无 v2 后缀的旧 task。实际产品 task 为 `TP07-T1 transaction-capability isolated EF overlay v2`，所以纯 source 阶段即拒绝；不能靠重命名输入消除差异。
2. `validate.py:1204` 的 `product_manifest_contract` 只容许旧32字段；实际 v2 恰36字段，新增 `revision`、`unit_revision_parent`、`unit_delta_from_parent`、`old_parent_source_approval_reused_for_revision`。其 fixed task 及 required_next_gates 也仍是旧值。
3. `test_contracts.py` 中 `V4_PRODUCER_OVERRIDES`/`V4_PRODUCER_AGGREGATE` 固定15个 producer 文件。prepare/README 修订后，这条真实文件 fixture 会按设计拒绝新文件；必须在新测试版本增加明确 v5 producer pin，不能删除 hash 门或只更新 validator 而遗漏该固定假设。
4. README 的“future overlay source approval/no such accepted artifact”需要按实际已签 source-only 批准更新；它不能继续引导把已批准178源当未确定旧175源，也不能称 source-only 已通过 probe。

以上为本次确证范围；本报告不扩展到尚未证实的运行或产品问题。

## 当前产品与批准身份

本轮实际重新 hash 下表及 v2 的全部五条 unit_revision_parent 引用，均匹配。

| 角色 | 路径或身份 | SHA256 |
| --- | --- | --- |
| 当前产品 | patches/transaction-capability-manifest.v2.json | `fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432` |
| 当前完整 patch | patches/transaction-capability.v2.patch | `5df9c73a4879fbf7f2cb79da48379a92856e093fecea7df3c31956080b1f3566` |
| 当前 source-only approval | root-approvals/TP07-T1-product-source-v2/source-approval.json | `d6cff0a1a92a1b8341b12c6dfaa352028039d8908fffd1b1387254561c3f0486` |
| 当前独立 freeze | TP07-T1-product-v2-unitfix-sourcefreeze/source-manifest.json | `a842630f569b6dfbeebaa464133ab4bc17cf42f2166691be337b7ac9483f1236` |
| 当前 source review | TP07-T1-product-source-review-v2.md | `eb47339baa20c4bbedf2be7ef644b3b0c2a343a0f00d191745cc24e2e53ebbf5` |
| 父产品 | patches/transaction-capability-manifest.json | `932f8f41a2d170c74d425c6add913ac1f3a5141fab49835e0311f55353183980` |

当前 source_root 是 workspace 下 `.local/temporal-native/verification/TP07-T1-transaction-adapter-v2/ef-source`。现有 source_root equality 必须保留：输入 `ef_overlay.source_root` 等于该真实 manifest 值；不得把 unitfix freeze 路径直接替换进去又绕过 equality。Low freeze 是相同178 map 的独立 corroboration，仍可在 root 输入准备前回读，不需要扩大现有13个输入顶层字段或4个 ef_overlay 字段。

当前 aggregate `a0c14e430c72ede7deb257fee16e175b9d06b1862abec59f417aedc7bd6f8724`；父 aggregate `a14b7a244d3811db24fe9f897e07adaa21dd4507a28b27e762d137ccce9896aa`。现有 source review 已接受相对父178仅一个 unit 文件变化；本轮也直接比较两份完整 manifest map，差异恰该单文件。

## 精确36字段合同

下一工具版仅接受明确 root 批准的当前 manifest Ref；fcb1 场景入口应固定当前 manifest 路径/SHA，拒绝将932f父 manifest 当当前输入。解析后的结构仍需独立有限语义检查，不以“SHA存在”替代字段/关系验证；producer 与 validator 分别实现，不相互 import 以共享接受判定。所有整数要求 `type is int`，布尔要求 `type is bool`；缺键、额外键、float/bool冒充整数都失败。

| 类型 | 全部字段及有限值 |
| --- | --- |
| int（7） | schema_version=1；revision=2；base_source_count=175；source_count=178；unchanged_base_file_count=174；planned_unit_methods=4；planned_unit_cases=25 |
| bool（7） | static_checks_only=true；SDK_executed=false；tests_executed=false；database_executed=false；transaction_probe_or_E_observation_implemented=false；old_L0_E_D_cohorts_reused_as_new_acceptance=false；old_parent_source_approval_reused_for_revision=false |
| string（6） | task 为上述确切 v2 字符串；status=`source_frozen_verification_pending`；source_root 为确切当前根；source_aggregate_sha256 为实际重算值；aggregate_format=`sorted relative source paths: sha256 two spaces path newline`；patch_application_base=`exact approved175 EF sources, already including the original bridge and temporal mapping` |
| array[string]（4） | added_files；modified_files；patch_paths；required_next_gates，均逐元素/顺序固定 |
| object（12） | base_prepared_source；base_approved_source；transaction_capability_patch；base_lineage；base_lineage_artifacts；base_source_hashes；source_hashes；namespace_boundary；W；EF_relational_API；unit_revision_parent；unit_delta_from_parent |

上表共36字段，不添加 version 包装字段，也不删除四个新增 metadata。前三个 Ref 对象保持 `{path,sha256}`，实际文件须 canonical absolute、workspace 内、无 symlink、SHA匹配。base_lineage 仍原六键，base_lineage_artifacts 仍恰 `base_archive/bridge_manifest/bridge_patch/mapping_manifest/mapping_patch` 五个 Ref；现有 W 五字段、EF_relational_API 三字段及 namespace_boundary 五字段的精确值保持，包括固定 W 包/DLL/MVID、EF10.0.12 和禁止复制官方 driver。

base_source_hashes 恰175条，source_hashes 恰178条，均为有效有限相对路径→小写64hex；实际根目录无额外源文件、escape 或 bin/obj 混入。base map 相对当前 map 仍174条同 hash，唯一修改是 DI `src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs`；只增加 adapter、factory、新 unit 三个文件。added_files 保持该三项原顺序；modified_files 恰 DI 一项；patch_paths 为四项排序清单。新 patch 仍相对原175的完整增量，绝不能把相对父178的 unit-only 差异作为该 patch。

required_next_gates 必须是实际 v2 的以下五个字符串、此顺序，不容许旧三项、任意包含或 count-only：

```text
Astra v2 unit/source and versioned patch review
Low new immutable178 source/patch reconstruction proof
root new source-only seven-field approval bound to v2 manifest
new pin-only preparation tool/source/native/locked-build chain
Low actual target25 TRX and same-build full-unit TRX
```

这些字符串是冻结产品交接时的计划门，不是可变通过状态；即使后续某门通过，也不把静态 manifest 中的 tests_executed 改为 true。实际门由独立新证据关闭。

## 父版本来源验证

unit_revision_parent 必须恰以下五键，每个 Ref 必须读实际文件验证，不能只要求64hex：

| 键 | 固定 SHA256 |
| --- | --- |
| product_manifest | `932f8f41a2d170c74d425c6add913ac1f3a5141fab49835e0311f55353183980` |
| transaction_capability_patch | `1752dc64ae88ed6c5402675101d8df594cd514b8cc1c984d3e1af6e6b0cf5af9` |
| source_approval | `ac8ff44fb570050d478190b8c1908f55cc0fbd5cd2d7a78bc1051d230dff6b4b` |
| sourcefreeze_manifest | `30c451f9a3e08898a559c6943f0097f3966eba16a0d252da5231f793af57dc13` |
| build_failure_triage | `2f94ff368decef790fe0f6f1daf8393e0aed04746ad11049bfd1bbd267199015` |

固定相对路径取实际 fcb1 manifest，结合明确 workspace 解析；禁止其它同名副本或跨根路径替代。父 product 的32字段与旧 task 是**仅用于 provenance 的有限父合同**，不成为第二个当前产品分支。父 source_count/map/aggregate178必须一致；父 sourcefreeze map 等于父 map；父七字段 approval.manifest 绑定932f、exact_paths/count绑定父178；父 patch Ref 必须等于父 manifest 的 transaction_capability_patch。triage Markdown 只核固定路径/SHA，不执行或据其声明 build 接受。

新旧 manifest 的 base_source_hashes、base_lineage、base refs、W、EF API、namespace、新增/修改路径、计数和严格构建规则须保持既有关系。两个178 map 的 key set完全相同，177条 hash相同；差异仅 `test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs`。

unit_delta_from_parent 恰六键：changed_files 为上面单一 unit 路径数组；unchanged_source_count 为整数177；global_analyzer_or_build_rules_changed 为布尔false；parent_unit_sha256 为 `c7b5520f6a5863af5924f94a6d5a913317a164e31d6a90d18c7fd47f37eb5220`；new_unit_sha256 为 `f4f689c8c2285ed4665880d65f0ccbd22424862dc1ac9eef1fc0cc69b44dad1d`；fixes 为下列四个原始字符串，顺序固定。两个unit SHA同时等于各自 map 条目，changed_files和177必须从实际两map推导后比较，不能只相信metadata。

```text
TestConnection.OperationEvents avoids inherited Component.Events hiding
private static readonly batch-only expected array
five immediately scoped EF1001 strong-type DI assertions
inherit DbTransaction.DisposeAsync; retain once-only Dispose(bool) counters
```

旧 source approval 只证明父来源；新 root approval 必须是 d6cff 的实际七字段合同并引用fcb1，old_parent_source_approval_reused_for_revision=false不能只作为标签而不验证新批准。旧失败build、partial DLL、旧 probe40 Python和旧 process-control 保持历史身份。

## 从 source 到 runtime 的消费链

工具 inputs/source/startup 的 lineage 仍十键。其中 `approved_ef_source_manifest_sha256` 必须指当前 **fcb1**；product.base_lineage 的同名字段仍 **dc2bfd…**，只表示175原始来源，现有“比较base其它五键、单独校验当前approved字段”的区分是正确的，不应改成整对象相等。`transaction_patch_sha256` 指5df9；`parent_overlay_sha256` 仍指 base_prepared_source 的 `9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a`，不是932f。新增的父产品关系由 unit_revision_parent 表达，不偷换这个既有字段语义。probe_source_manifest_sha256/toolpack_manifest_sha256 必须指新工具包实际snapshot的两个产物。

prepare 的 source 验证新有限合同后，继续从实际根复制178+固定host9，生成恰187源；native只能额外生成新host锁，共188。native/build/offline/readiness的现有task都是阶段自己的 `TP07-T1-*`，没有另一个旧产品task条件；它们通过相同 inputs/source refs、187 hash map 和实际锁/图/二进制传递新身份，不能接旧工具包阶段来凑链。validator 的 approval_contract 调用 product_manifest_contract，是 runtime case/matrix 的独立产品检查点；必须更新该真实函数，不能仅修 source 入口使问题延迟到DB后才暴露。

run.py 不解析产品task/新增字段；ProbeIdentity 消费 startup/source/native/lineage与实际程序集，不直接解析产品manifest，因此本次无须改这两者或host。独立准入必须在 root 签 runtime approval 前完成新产品合同、source/native/build/offline/readiness回读；不能等第一场DB运行后才第一次发现manifest不兼容。现有 stage/raw schema、runtime approval schema、fixed4 registry及28 offline registry保持；本设计不新造“临时 runtime_authorized”来调用离线审阅函数。

source-only approval本身只要源审/独立源证明成立便合法，不以unit25/full通过为前置，因此无循环。实际 unit25及同build full结果是root开启probe后续窗口与最终runtime的外部门；full当前失败须先解决。probe编译28不能替代product单位门，product单位门也不能替代实际probe28。root_runtime approval另具 `runtime_authorized`，与七字段 source_approved 严格分开。

## High 的最小文件范围

最终产品 manifest 固定并获 root 新指派之后，以新 probe 工具包 v5 实施；保留旧v4实际sourcepack、40项结果及失败历史，不能原地覆盖快照或把新tracked源码写回旧包。

| 文件 | 允许修订 |
| --- | --- |
| prepare.py | 在现有source产品检查边界增加/替换有限当前manifest与父版本来源校验，保持source复制、输入/阶段CLI和全部进程、native/build/offline/readiness算法不变；新辅助函数可用于该边界，不能新增CLI或任意版本分支 |
| validate.py | 将 product_manifest_contract 更新为上述当前36字段及五parent refs/实际map差异的独立合同；允许局部纯合同/只读Ref辅助函数，保持approval调用点及其余数据/运行验收函数不变 |
| test_contracts.py | 新增真实当前manifest正例及有限变异负例；更新新15 producer pin/aggregate；保留原40方法和旧v2/v4身份、实际17工具链与不同module ContractError断言，不删已有回归 |
| README.md | 说明新有限产品版本、父来源/当前批准区分、新工具包及待执行门、source-only非runtime含义；旧来源说明保留 |

probe-policy.json无需为版本描述而增加字段，178/187/188、13输入键、source_count_pending说明性布尔、9host/17文件固定清单与当前版本兼容，保持字节不变。run.py、全部host、5个helper、产品178、W、证书/TLS策略、Savepoints/AutoSavepointsEnabled、180/180/180预算与2秒reap均冻结。若最终产品v3超出本报告的确切v2形状，先回root收敛新设计，不能让High自行泛化。

## 源码与独立验收门

1. High完成四文件后停写，给出完整17文件map、新15 producer aggregate及四文件diff；producer pin不含 validate.py/test_contracts.py，避免自引用。v5新增prepare/README覆盖值与原v4 host两覆盖值共同组成15项，原v2 map只作历史固定输入。不得把整个17 aggregate嵌进test自身。新的包版本是证据版本，不要求篡改所有schema/task。
2. Astra源码审阅确认新合同与当前最终manifest一致，Low只读保存新17 snapshot/toolpack和实际host9 manifest；若host字节未改，9项aggregate可相同，但新snapshot路径及artifact Ref必须真实，不能把旧目录重标新包。
3. Low在独占无SDK窗口跑原40方法加新增合同，新增实际manifest正例先通过再作变异：每个新增/原有限键缺失或extra；错误task/revision/type/count；旧3 gates/五gates错序；父五Ref各自missing/wrong SHA/cross-root/symlink；父approval错manifest/paths；新approval复用父；父sourcefreeze/map不匹配；第二处源变化/产品文件变化/单位SHA伪造；changed_files/fixes/177错值；dc2b误作当前approved或932f误作parent_overlay；源aggregate漂移；187/188/host9仍精确。直接在内存改manifest正文来覆盖语义分支，不能让所有负例只因入口整体SHA改变而提前失败。
4. 新有限manifest正例应从明确固定path/SHA的真实JSON读取，使用真实workspace上下文与文件Ref；无prepare import、无替换模块ROOT、无拼装accepted/native/SDK/runtime证明。必要的父Ref负例用明确标注synthetic的临时数据测试局部只读Ref/纯关系函数，不能将synthetic对象交作实际source批准。原closed17 fixture仍只测试inputs/toolpack，不冒称完整product/runtime正例；新测试必须真正调用新版product合同以覆盖本次缺口。
5. 契约通过且产品单位门最终通过后，由root分别开新的纯source187、实际native、locked build、真实编译host28与Python、独立readiness窗口；每门首失败保存停止。原执行函数不变时历史过程控制可继续作该算法既有证明，不仅为产品manifest修订重跑耗时取消实验。实际新包/源/图/二进制/28结果仍必须新生成。
6. 新runtime批准、TEST身份/profile、fixed4运行/cleanup/validator、共同二进制positive controls及最终接受沿原门；本报告不授权上述运行，不提前关闭L0/E/D/TP08。

当前设计完成后停写；下一动作是等待full-unit分诊和最终产品manifest确定，而非启动兼容编码或probe执行。
