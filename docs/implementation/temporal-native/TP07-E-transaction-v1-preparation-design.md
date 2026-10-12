# NEW E transaction v1：有限准备与来源兼容设计

2026-10-11。结论：**design_ready_for_root_review**。最小实施范围为新 ignored preparation 三文件、`ef-validate.py` 的有限 NEW L0 加载分支及 `test_ef_contracts.py` 的对应合同；新 E 静态快照保持原 24 成员，运行 toolpack 保持 8 成员，独立 NEW L0 六文件 pack 单独完整绑定。没有运行或通过声明；本文只读实际源码、JSON、字节/hash/AST，没有执行项目模块、Git、tests、SDK、DB，唯一写入为本文。root 全文接受后才可授权 High 编码。

## 已确认输入与阻塞

| 冻结输入 | 实际 SHA-256 |
| --- | --- |
| 最新旧 preparation-v5 `prepare-and-build.py` | `c40f7f77cd0bd82fe3f4333ca74a1437908bacebdbae6c2f62bfcddf901af843` |
| 旧 E-tools-v11 source24 / toolpack8 | `fc353753262029f25d0b6d1d54b186ec83e4c9ff40fe709d1058b1e93849df4d` / `a2c5592e0f30808c37cfb83e23d27717f1224ae491f6061b327c520f48207d3f` |
| 当前 E v11 policy | `2181388ea81edac2c0b0d5e43d0394430a2a495ca33a1903e801ddaf6588857d` |
| E9 manifest / aggregate | `deb47b37037cb588785e87f7bff83984fe80facf29cb08ca9abf62e46c9b0a9a` / `d3e2227ed642e40d420ff0791dded586fb646ef26c6bb4c5faaad80bf9d41a43` |
| product-v3 manifest / source178 aggregate | `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8` / `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c` |
| transaction patch / source-only approval | `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f` / `66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f` |
| NEW L0 v2 source185 / lock186 | `4f50b6188f13b7718f6dbabdc853ab63868c6ac5a36456cf690d012e91695faf` / `6982a1b12187c1f9ed18810222b5d8347528d4d758822b6697f8f4b5b074ecbb` |
| NEW L0 startup / six-file toolpack | `a1cc53af8ef27db498777cda211975e941270ec903fb830d23387c780cd237a5` / `d4220ee78441af4d79b5ced3d4d10fe2cd3e1b6b391efa0dd337a9cc3e092170` |
| NEW L0 run / independent validation | `2c76e364215ca32067286f8c75759279fe56905569d859a49ef013b3ae6e6da9` / `266a3054f496cb23d6f1d738e26d90e6b2f6e83fdde3c60328568328d63dfa1b` |
| NEW L0 native readback | `de4bcbdffe022961faf571927334f6b87321368ece6cee8148eed1c2b78605cb` |
| NEW L0 final Astra acceptance | `e7d352b60d477d54278d11a6ef5d5bbc4810f22cad119d01f0e006e33d81e3f5` |

最新旧脚本是 v5，不是 v4；与 v4 `2c3cc3e3…` 仅 doc/E9 pin/template folder 三处不同。其六 PIN、五 FOLDERS、182/191/193、旧 helper 全文件 pin 及旧 L0 readback 字段均不能接受新 L0，不能只生成新 inputs 后执行旧脚本。

E 侧还有两个明确来源兼容阻塞。旧 E8 pack 固定旧 `runtime-validate.py`，与当前及 NEW L0 d422 的 `f1729710045f9f77dc369c183b1d53bc552fbaa5d107609eab747efebc85f5a2` 不同，先使 pack/shared-source equality 失败。其次 `original_l0_validator` 固定 `manifest.parent/source`，但实际 NEW L0 run 2c76 的六个 before/after 输入路径均为当前 workspace 的 `tools/...`；必然在 `original_L0_source_path_hash_binding` 失败。强行从 snapshot 执行会使新增 product-origin 合同的自然 ROOT 指向错误位置。两处均须正式重绑，不能改 ROOT 或复制伪造原运行路径。

## 来源集合与语义

已实际重新哈希 canonical 175、product178、L0 prepared185 完整物理集合；product 43 字段中的 19 个嵌套 Ref 及 L0 的十项 lineage artifacts 也全部实读匹配。

- base175 保留原 `approved_ef_source_hashes` 与 `approved_ef_source_manifest_sha256=dc2bfd15…`，表示映射父来源。product178 为其中 173 字节不变、2 修改、3 新增：修改 DI 注册与旧 `DamengExecutionStrategyTests.cs`；新增 transaction adapter、factory、capability tests。已接受的 conservative W retry 行为保持，不能用旧175冒充现产品。
- L0 的 `product_ef_source_hashes` 必须精确等于 4001 manifest 的178 map；其 canonical root 仍为 `TP07-T1-transaction-adapter-v3/ef-source`。source-only approval scope 保持 `transaction_capability_and_w_retry_contract_tests_only`，不推广为 runtime 批准。
- product178 与原 L0/common7 的 `test/` 映射无交叠，严格组成185。E9 实际字节与原 policy一致，与185无交叠；新准备集合为 **194**。provider lock 已在178中，只有 `test/TP07LiteralProbe/packages.lock.json` 和 `test/TP07TemporalRuntime/packages.lock.json` 两个新路径，所以锁后严格 **196**、active locks/assets 各3。这是实际集合推导，尚无新 E prepare/build 产物或通过数。
- 新 E 继承 L0 完整10 lineage/artifacts，并各新增 E template source 一项，精确11项。transaction manifest4001、patchd13f、approval66c 是显式新三项；dc2b仅父来源，不替换成4001。准备 source 保留 `approved_ef_source_hashes`175，并新增/传递 `product_ef_source_hashes`178；禁止删除真实字段、仅改计数或回写旧 manifest。

## 明确实施范围

1. 新 `.local/temporal-native/verification/TP07-E-transaction-v1-preparation/` 下仅 `prepare-and-build.py`、`README.md`、`schema-draft.json`。以最新 v5 为基线，不覆盖旧版本。仍从该原目录入口运行，`parents[4]` 必须自然为真实 workspace；freeze/source 仅作保存。
2. tracked `tools/DownstreamAcceptance/TP07/ef-validate.py`：只修 `original_l0_validator` 中 NEW L0 的有限真实来源选择与验证，及 `inputs` 对该新分支的独立完整来源集合守卫；必要常量/小helper限于下述已冻结 refs。不改 runtime/value/window/native-oracle/cleanup 合同。
3. tracked `tools/DownstreamAcceptance/TP07/test_ef_contracts.py`：保留原28方法 AST，新增下面有限加载合同。方法数由 High 实际声明、Low实际日志确定，不预填通过数。
4. 新 E-tools snapshot/manifest 为数据准备：source24 成员与旧24严格相同，toolpack8 成员也相同。当前旧24唯一已变项是 NEW L0 `runtime-validate.py`；编码后再加入实际 `ef-validate.py`/`test_ef_contracts.py` 变更。E9、L0/common7、`ef-run.py`、policy、managed identity、packed oracle 均字节保持。L0-only `runtime-run.py` 和 `test_runtime_contracts.py` 不在旧E24中，通过独立 d422 六文件真实引用完整保护，不虚称已在E24，也不扩成26。

### 新 preparation 的有限变化

保持 inputs 的五个顶层字段、六个 refs (`l0_source/e_source/l0_lock/l0_lock_readback/origin_graph/l0_toolpack`)、五个 folders 及 dotnet 10.0.401 精确形状。前三 L0 source/lock/readback 与 toolpack 改到上表实际新 refs；E source/folder 改到新冻结 E snapshot，未产生前不虚构 SHA。旧 origin graph `3e6a2ac1…`、原 cache/feed 保持：本次已静态实读13个 non-W 包，archive/feed/version/metadata SHA/contentHash 全与原 proof 匹配。不能把 ZIP SHA512 当 NuGet contentHash。

替换旧计数门为精确 `approved175 + product178 + templates7 → 185` map 等式及物理闭包，再与不交叠 E9 组成194/196；不只检查长度。每 phase 均保护 canonical178、base175、product19 nested refs、L010 artifacts、输入 pack/source 及全部实际复制字节。继承并核对 source/lock/startup 三阶段相同 lineage11 和输入 Ref，预期 new source map 对每路径哈希一致；仅允许两新增 host locks，provider lock固定 `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`，新 literal lock必须等 NEW L0 实际 `896258992bb91e4289953fef191287f8e6fe496706d6a3df070a85eb5dc5916e`。

旧 `non_driver_count/old_inputs_unchanged` 不存在于新 native readback，须消费其真实固定 schema：status=`actual_native_lock186_pass`、匹配6982 lock ref、package_count=14、host_graph_count=15、source186_only_hostlock_delta、full_original_source_contract_and_prepare_receipts_PASS、native_contentHash_threeway、ZIP_sidecar_domain_actual、metadata_version2_all、all_protected_unchanged 均为实际严格值；原13非W graph/版本/dependencies/contentHash、host ProjectReference、W包/DLL/MVID及旧 origin proof仍直接核对，不能只信状态旗。

`HELPERS.process` 改为 d422 冻结 `runtime-run.py` 全文件 SHA `8fef18e90be7f44ac644dc006a590e354da43d0f1f86b89f0dc5eff1ee4ab4ec`，identity helper不变。实际 AST比较确认 `process`/`public_env` 与旧d2c helper完全相同；只重绑文件身份，不修改过程算法或预算。保留 prepare无SDK、lock三命令、build五命令、锁前后不变、三assets、两个startup完整binary闭包、同provider ProjectReference输出、全compile源码声明、fresh输出和输入/旧phase隔离、首失败停止及原receipt。

### NEW L0 加载器有限分支

新分支仅接受精确 NEW L0 d422 pack Ref 与2c76 run Ref及实际路径，选择真实 workspace ROOT；旧路径合同保留，不将任意 manifest/root/latest 自动判为新来源。先核 pack六成员、运行pack identity、before/after、每个suffix唯一匹配、共同真实root和当前物理hash、E/L0四共享项一致，再从真实原 `runtime-validate.py` 字节编译。`__file__` 必须真实原路径，模块自然 ROOT/HERE 与其一致；禁止编译snapshot后赋ROOT，也不能修改保存run六路径。完整 `oracle.validate_artifact` 仍重验已接受 NEW L0 实际run，并要求返回结果与266a保存validation相同。

`inputs` 对这一新分支须独立验证 NEW E 的 `approved_ef_source_hashes`175 与父dc2b、`product_ef_source_hashes`178 与canonical4001、完整L0/common7/E9无交叠194 map、锁后仅两hostlock196 map，以及继承十项 lineage/artifacts 加 E1 的精确关系。不能只沿用旧六项lineage比较，或只检查src/部分common而遗漏新产品测试文件；不能仅因preparation报告通过而跳过消费者边界。L0原验证已闭合的19嵌套Ref、严格66scope、canonical178和十项来源继续实际物理保护，不删减或改成任意版本/count。新增同Ref语义负例应覆盖transaction三项遗漏/替换、base175被误当178、同计数路径替换及非lock源哈希漂移；旧分支和旧28方法保留。

新增合同至少覆盖：真实 d422/2c76 loader正例（全原hash Ref、六真实字节、自然ROOT）；同Ref受控语义变异中的重复suffix、before/after不一致、混合/错误root；六文件缺失/hash漂移、共享项另签分歧、错误pack/run身份拒绝。负例须在相关边界命中、验证拒绝发生在compile前，不能只靠最外层SHA早拒替代语义。禁止producer oracle、fake ROOT或mock成功 validator。既有旧28方法包含旧snapshot-root fixture，保持AST；新增方法不必复制值合同。

## 冻结后既有验收顺序

1. High冻结新三准备文件、tracked两文件及完整diff/AST/hash/handoff；Astra源码审阅。Low仅新 E24/E8 数据快照和独立L0six实际Refs/原E9/175/178/185闭包静态核验；随后 root 开既有离线合同窗口，完整原28加实际新增集合通过。
2. root绑定新有限 preparation inputs，Low从新原入口依次 prepare194、独立接受后 native lock196、独立接受后 locked build。完整原13图/两host+provider锁、raw assets/dgspec、SDK命令receipt、同W/新provider/新host DLL/MVID/deps及source字节保护均重新实际核验；旧startup不得接受新产品。
3. 用同一新build运行原 compiled partition 338/25 和 shared/TLS GeneratedCreate14+14=28。继续原模型/operation/seed/default/SQL字节、service identity、I/O zero与deep baseline比较，保留 `c6f269c5…`/`ec3f90e7…` 原baseline refs及原 `offline_gates` 完整消费。旧 stored verifier `TP07-E-generic-v1-generated-create-control/verify-stored-v1.py` SHA `a76c814934c6f41fe07aa3b5c81cf75d25e423fe29a3e9fb35f392e725401003` 硬旧startup/pack/输出；后续只能在新control目录作路径/实际SHA有限重绑副本，完整判断语句保持，冻结审阅后由Low执行，不能直接以旧gate代新gate。
4. 全部前置通过后，root才签新 E execution approval：原六顶层字段、12个 artifact refs全部绑定新 source/locks/build/startup/partition/generated gates与真实NEW L0 run/startup/d422/validation，candidate/capabilities/W不变。执行前由原完整 `inputs` 消费（含新真实root loader与完整L0验证），不能绕过新来源门。
5. 唯一实库窗口从0依原固定顺序完整执行 **338 cases / 25 windows，同一run/cohort**；原 main900秒、cleanup300秒、validator180秒及原TEST wrappers、每窗独立exact-ledger cleanup、fresh状态、末尾完整25窗独立validator不变。禁止借旧288补新338、跳过已过窗口、重试或扩大预算。首次失败立即按原runner收集cleanup/validation后停止，冻结失败，不追加查询；只有新全轮完整通过才进入Astra/root有限E验收。

新 prepare、build、startup、pack、offline及runtime SHA现在均待实际生成，不将规划写成通过。旧 E288及TLS window22失败、T1各轮失败、L0 v1首失败继续immutable；原product178/W/full824、T1 actual4和NEW L0 actual14接受不回写。D、TP08、Claude本地review/一轮修复、PR bots与merge仍按原DAG待后续门。本文不授SDK/DB/Git/运行批准；root接受设计后另行明确编码与验证窗口。

本轮以 v11 policy 为唯一现行E语义基线；保留其 cases 298/299 的 TLS refusal 合同和已修 E9 pins，不回退 v10。原338 IDs、25 windows、compiled partition既有15反例、SDK版本30秒与其余预算均保持。
