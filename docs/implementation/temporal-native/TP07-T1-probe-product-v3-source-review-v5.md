# TP07 T1 probe v5 / product v3 有限兼容源码验收

结论：**source_accepted**。本次冻结四文件符合 `TP07-T1-probe-product-v3-compatibility-design.md`（SHA256 `cf959306d9247add75e2b4f63b93a0ef9e4227f9253e40a67b57adbbb739bc34`）的有限修订范围；未发现阻塞项，当前无最小修复要求。接受只覆盖本报告所列源码字节，不表示新 probe 的 Python55、source/native/build/compiled28/readiness 或四场 runtime 已通过，也不授予运行权限。

2026-10-11，Astra High 独立只读审查。读取冻结四文件、完整变更、实际产品 manifest、source approval、父来源与基线 rows；使用标准库进行 JSON/AST 解析、文件 SHA256、集合与声明关系比较。没有导入或执行 prepare/validate/test 模块，没有运行测试、SDK、恢复、构建、probe 或数据库；唯一新增文件为本文。旧设计、产品、工具和历史证据未修改。

## 冻结身份与改动边界

工具目录为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe`。以下 SHA256 均由本轮实际文件重新计算，并与 High 冻结交接匹配。

| 对象 | SHA256 |
| --- | --- |
| prepare.py | `e131b64d83e1e0b78731ff83b5fdd022ae71645722e58473ae444be93570515c` |
| validate.py | `1e512870c28d43933463fb4921269f7d2bee7ca3948e760828f990b1c06033e5` |
| test_contracts.py | `c99553d826d3f92069c82bb7c4f8aec39a766cf96ca73b48cb85d0c9ad10e68f` |
| README.md | `b2d9cb2c419d4fb412dab02667c48d7fa36d1b6ea96242c92a9ff85100f44adf` |
| actual17 aggregate | `b6e6467184ed8a84147863bb4d3b1bf991f1c3da7e70d65de3ba4a22dbcd2da9` |
| producer15 aggregate | `1ec53727763ee6b2f496356b3241372fbc3b40f001a4b72f3d9235103075162a` |
| implementation-handoff/freeze-report.json | `6887f1a68cbf9dd33a6d232b25179b5ed351661be014ef8a784e83a16c512faf` |
| implementation-handoff/four-file.patch | `cbd9f757b8e0de738c757feefb7fc5ce40c3eccf4ab6799a133935f5a4d2d14b` |
| v5-prechange/prechange-manifest.json | `6ad2e71f1ce70b4e525dc1587452d254408c3d64d7ee113108b58e01bc98a06a` |
| v4/source-manifest.json | `e8f404a5b3630935f7d063d47b449ec762b3d0b02e7cbfbd5b4aa62676b31bfa` |

三个本地目录完整前缀分别为 `.local/temporal-native/verification/TP07-T1-probe-v5-implementation-handoff`、`TP07-T1-probe-v5-prechange`、`TP07-T1-probe-tools-v4`。aggregate 使用排序相对路径的 `SHA256 + two spaces + path + newline` UTF-8 行计算；15 排除 validate.py 和 test_contracts.py。

本轮逐文件确认 v4 实际 `source/tools/DownstreamAcceptance/TP07/transaction-capability-probe` 的 17 文件与 prechange map 相等；当前仅上述四文件变化，其余 13 文件字节不变。prepare 原有函数只改变 `source`；validate 原有函数只改变 `product_manifest_contract` 和 `approval_contract`。其余原函数 AST 不变。test 原有 helper 只改变 `closed_current_toolpack_fixture` 的当前版本 producer pin/模块标签；原 40 个 `test_` 方法 AST 全部逐个相同，新增 15 个，声明合计 55。

## 唯一当前产品与来源合同

实际产品为 `tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json`，SHA256 `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8`。prepare 的 `product_manifest_contract`（548 行）与 validator 对应合同（1547 行）只接受唯一 v3 task/revision 与 **43 字段**，不是 44，也没有 task 前缀、版本 wildcard 或任意同数量对象通道。

两个模块的固定 literal 独立解析后完全相等，并与 actual manifest 逐项比较：39 个固定字段，加 source_hashes、base_source_hashes、retry_contract_inventory、full_unit_inventory 四个有具体语义合同的字段。固定块覆盖全部类型和值：11 int、7 bool、7 string、4 array、14 object 的完整顶层结构；递归比较区分 bool/int/float，数组顺序和对象 exact shape 不放宽。七条 required_next_gates 逐字符串且有序固定；tests/SDK/DB 等静态 false、source_frozen_verification_pending 及 synthetic 非 transport 标志保持。

来源闭合经本轮实际读取确认：

- source_root 必须为 manifest 的 `.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source`，不能替换为 retry-sourcefreeze 副本；实际完整枚举并 SHA 校验恰 178 项，aggregate `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c`。
- 原 approved175 map 同时与实际 approved manifest 及 prepared source map 相等；由 map 求得 173 unchanged、DI/retry unit 两 modified、adapter/factory/transaction unit 三 added。五个 patch_paths 精确固定；完整 patch Ref 为 `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f`，未把 parent178 当 patch base175。
- unit_revision_parent 十个 Ref 的键、路径、SHA 都固定。读取 parent v2 的有限 36 字段/178 map、freeze 和七字段历史 approval，校验父 map、父 patch、父 manifest/freeze/approval 的对应；当前与父路径集完全相同且唯一 retry unit 差异，177 项保持。parent retry `82caac3eca1dde1f3823ae3560d4f0ebb0c9388f18dc2380ad75cfdd9cf23285` → current `cb7b6114490fc1fd34fc3c839e2fdc24d1eaeffdf8ed8b5ac6f81215e9fbfe2f`；transaction25 在双方均为 `f4f689c8c2285ed4665880d65f0ccbd22424862dc1ac9eef1fc0cc69b44dad1d`。
- 本轮读取并重新计算 manifest 内全部 19 个 Ref 的实际字节 SHA，包含十个 parent Ref、三个直接来源 Ref、五个 base artifacts 和 baseline_rows，均匹配。历史 failed TRX/readback/reasons 保持原失败来源，未转成新验收。
- 178 product + 原 9 host = 187 source；只加原 host packages.lock.json 得 188 locked。实际路径数组与三个精确计数均检查，不接受替代路径、179 或 count-only。

三类 lineage 正确区分：工具 `approved_ef_source_manifest_sha256` 指当前 400123；产品 `base_lineage` 同名字段仍为原 approved175 的 `dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6`；工具 `parent_overlay_sha256` 为 prepared175 的 `9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a`。父产品 fcb1 仅由 unit_revision_parent 表示，不能占用当前批准或 prepared base 身份。

## 新审批与执行边界

实际新 source approval 是 `.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json`，SHA256 `66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f`。本轮实际读取恰七键：schema_version、task、status、manifest、exact_paths、exact_count、scope；值分别为 1、TP07-T1-overlay-source-approval、source_approved、当前400123 Ref、排序实际178、178，以及唯一 `transaction_capability_and_w_retry_contract_tests_only`。

prepare.source 和 validator.approval_contract 都固定审批 Ref，分别调用独立 source approval 合同；合同既固定新 scope，也要求 manifest.required_source_approval_scope 相同，因此两者同时降为旧 overlay-only 也拒绝。旧 d6cff 只用于父来源审阅，不充当前批准。source-only 不依赖未来测试通过，不形成循环；runtime 仍需要另一个明确 runtime_authorized 的外部批准，七字段 source approval 无法替代它。

沿 source → native → build → offline28 → readiness → runtime 的现有消费检查，没有发现遗漏的旧当前产品 task/shape/scope 假设：旧 v2 task/overlay-only 只留在固定父来源合同；source 新 helper 完成前置检查后，原复制和 source 输出逻辑保留；runtime approval 新调用取代旧当前 source scope 检查。native/build/offline/readiness、runner、host 只消费原固定 inputs/source/startup/lineage 链，相关原函数 AST 不变。

原 13 输入顶层键、四个 ef_overlay 键、CLI、原 9 host 和 5 helpers、policy/run 字节不变；180 秒 deadline + 2 秒 bounded reap、进程组取消/清理、compiled28/offline 注册表、fixed4 live 注册表未改变。没有新增包、W 包修订、证书/TLS 放宽、savepoint 行为修订或 producer 伪造 ROOT。prep-v4 是另一文件组/另一窗口，本文不重审或修改它。

## 静态用例关系与 Python 契约源码

retry_inventory 九键、21 个有序声明各六键、typed InlineData 和名称渲染均有限固定。本轮独立展开得到唯一 49 名，与 actual planned_test_names/planned_count 相等；两份实现分别推导同一集合，没有由 producer import 向 validator 提供通过 oracle。

baseline_rows SHA `83a36972c1d668de0d471579c1f1d0f65599fec481c7ba682fa980ed5574fd5b` 仍为真实历史795（783 Passed + 12 Failed）。合同检查四字段 row、四字段 method、固定 adapter/codeBase、唯一名称与 execution_id，再提取原 retry20。原20和失败12的原名/outcome/新名按原行顺序一对一映射，失败12为原20的精确 Failed 子序列；20目标唯一并属于49，新29是有序差集。独立关系检查确认剩余775原名 + 新49 = 完整排序824唯一名，原transaction25保留。full_inventory 恰13键，不能加入通过计数、替换等数量名字或遗漏 transaction25。

新增15个测试方法覆盖：actual400123/66c正例；43字段缺失/额外；旧task/v2/future及primitive类型；178/187/188 count与路径；173/2/3/5关系；十parentRef的形状/字节身份/路径/缺失/symlink；父freeze/approval和177单差异；scope共同降级；三类lineage及七gates；retry inventory形状/所有数值bool类型；21声明/InlineData/名称；20/12/29映射；795历史rows；824完整集合及25保留；静态计划不冒充实际接受。

负例使用实际不变 Ref 和真实 fixture 的内存深拷贝，在 semantic helper 上变异，`reject` 要求 ContractError 且明确排除 ref_sha_mismatch。它们并非先改磁盘文件再靠整份产品 SHA 拒绝而“通过”；parent helper 也有真实来源正例及内存关系变异。原 synthetic 输入 fixture 仍只验证 inputs/toolpack，新增 actual fixture 才承担完整产品正例；模块从实际位置加载的既有测试方式保持，无 ROOT monkeypatch。

producer15 固定策略已按实际 v2 toolpack 的15来源重算：保留 v4 两个 host override，只新增当前 prepare/README override；结果与当前15真实文件及 `1ec537…` aggregate 相等。validator/test 不进入 producer15，17完整 pack 由 Low 外部生成，没有测试自身或 README 的自指 SHA 循环。旧v4真实快照及原40结果保留为历史，不能替代新55执行。

## 接受范围与后续门

本次 source_accepted 不附带源码修复要求。root 可以据此在独立窗口授权 Low 对新的实际17 pack、host9 manifest 和这些冻结字节进行 Python55验证；必须留真实命令、退出码、完整结果及前后源身份。本文只确认原40+新15的源码声明与保留关系，没有宣称55已运行或通过。随后仍须由 root 分配新 probe source/native/locked build/compiled28/readiness，并用同一新输入链独立回读；四场 runtime 另行批准。

root 在本次审查过程中通报：新产品 source/native/build 已通过，实际 retry49、same-build transaction25、unfiltered full824 均已由 Low 执行且 root raw exact-set 回读通过；full TRX SHA `418ece01aced69d0cab8120d3bdb0b1db5db6be74c014cd00eaba05b8acbe125`、readback `823dd1997879293a495588b8028ea3b44cda11b7c52503f5d3a32542e72d360e`、full manifest `d4b0e67590498da26930b183be12a6c03dd783b403d92417f574b8cbada20ee2`。这是 root 提供的另案当前门状态，本审查未重跑或独立重审其 TRX。400123 产品 manifest 的 static/planned 字段仍原样保留，不把后续成功回写进静态源证据，更不以这些产品单位结果冒充 probe55/28 或 runtime4 接受。

完成本文后停止写入。
