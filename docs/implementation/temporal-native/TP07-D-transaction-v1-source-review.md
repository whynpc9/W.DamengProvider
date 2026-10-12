# TP07-D transaction v1 source review

结论：**SOURCE_ACCEPTED_IN_SCOPE**。本次未发现阻止该冻结版本进入独立 Python 合同验证的实质源码问题。此结论仅接受 NEW D transaction-source preparation v1 的静态实现，不表示测试通过、offline build/native/CLI 接受、D runtime 接受或 full EF C 接受。

审阅遵循本仓库 AGENTS.md 和冻结设计 `TP07-D-transaction-v1-preparation-design.md`，设计实际 SHA-256 为 `261661b1d22789e778f42f50f2bd0e6fcc3d6f4b71732ef1166ee2489114caea`。仅读取源码、JSON、patch、实际文件字节，并以标准库进行 AST/哈希/集合核对；没有导入或执行项目模块，没有运行合同测试、SDK、CLI、host、数据库或 Git。唯一新增文件为本报告，写完后冻结停写。设计中 E 最终接受尚待完成的描述属于当时历史，不回写；当前 E 前置依赖已由 root 通知关闭，本报告不重复裁决 E。

审阅对象是两个当前 tracked 文件与五个新 preparation 文件。实际冻结 SHA 如下：

| 文件 | SHA-256 |
| --- | --- |
| `tools/DownstreamAcceptance/TP07/design-validate.py` | `38808d3be5a6bc42bfcf8a825b47e9deb3f9eed188dcd11970e6ce42fcae965b` |
| `tools/DownstreamAcceptance/TP07/test_design_contracts.py` | `41648d07a1dfe1ac1a1959705ae600b189606b61801c90ead242f71c3d96c083` |
| `TP07-D-transaction-preparation-v1/prepare-d.py` | `65696e115fd2a5e772ce31ee7aa1c49fc59ba29c44297ac695625ee6a2d0e4ee` |
| `TP07-D-transaction-preparation-v1/build-generated.py` | `729c99b18bf6b66dd4899d72b87e34e74a0779dd1cba2b28b58baacf3dfdbe31` |
| `TP07-D-transaction-preparation-v1/test_pruning_contracts.py` | `65fcc16a18ebaf67362b87821888fae17ae2362af61a8deed1880c8413b3194b` |
| `TP07-D-transaction-preparation-v1/schema-draft.json` | `22f15f49867a3fda17d20e43ae1ca359aed8980c95c41d1dc4e73fe1b85e5dd6` |
| `TP07-D-transaction-preparation-v1/README.md` | `b069b0e8c3331d7d890310ad4ecfc1a79989ae9f06fedce4f608662765fddc01` |

上述 preparation 短路径相对 `.local/temporal-native/verification/`。独立回读 handoff `freeze-manifest.json` 列出的 63 个实际文件，SHA 全部相符；manifest 自身 SHA 为 `bab27e4c28de00481fc8f1587e85aedc7e9b38f6c83b8b5fdb9e81f90b4dc33a`，implementation report SHA 为 `c0de57a67e1e0b98d023caf85ddf466057e4e506118a71d70a221bce9b4920a3`。High 的 110639 项保护集零漂移是其交接记录；本审阅没有重新遍历该整个保护集，不把该计数改称本审阅的独立全量结果。

新 `TP07-D-transaction-tools-v1/source-pack-manifest.json` 实际 SHA 为 `84d189450f0f080aa6aefc1dc8675a7d11d0d028642fa10cc133c1e663cb47f6`。与原 D-tools-v4 比较，成员路径精确相同，共 25 项，仅上述 validator/test 两项改变，其余 23 项相同。D pack 内 `runtime-validate.py` 仍为 `d9ea39850a3d394f86f2fef54fe8330d732ea3a46d6823da3da74e84b9188d55`；当前仓库新版 runtime validator 的不同 SHA 不被装入该 D pack。原 pure-16 提取算法、policy、process/identity helper、runner、十个 D 模板、七个 L0 模板及 oracle 字节没有借此更新。

新 pack 旁的 `source-manifest.json` 是原 v4 历史元数据的原字节副本，SHA `ed4c58a011c3f4e2e566b579c711aee41b874aee97aaad475729a89b818a15e0`，实际 17 个顶层字段，task 仍为 `TP07-D-tools-v4 source-only preparation`，其中旧 `files` 和 `source_pack_manifest_sha256=124cbc…` 不代表当前成员清单。已审查真实 consumer：prep 固定消费当前 `84d189…` 三字段 source-pack manifest；validator `pack()` 要求精确三字段、25 路径和实际执行字节。上述历史元数据没有被这两条链当作当前 membership 消费，误传会被 schema/pin 门拒绝。因此保留它作为历史来源，不扩大本轮修改范围。

来源审查确认：

* `transaction_contract` 分开执行语义判断，`transaction_origins` 负责真实固定 Ref/物理字节绑定；validator 与 prep 的六个共同来源 helper AST 逐项相同，没有一端独自放宽。
* 已实际读取原 approved175、product178、L0 source185 和 lock186。product 为 43 字段，approval 为 7 字段，L0 为完整 10 项 lineage/10 项 artifacts，source 文档确无 root 字段。输入 root 被固定到 `TP07-L0-transaction-v2-prepare/replay`，没有补造 `replay_root`。
* manifest `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8`、patch `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f`、approval `66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f` 与 L0 source `4f50b6188f13b7718f6dbabdc853ab63868c6ac5a36456cf690d012e91695faf`、lock `6982a1b12187c1f9ed18810222b5d8347528d4d758822b6697f8f4b5b074ecbb` 绑定正确。10 项 artifacts 及 product 内 19 个嵌套 file Ref 的实际 SHA 均相符。
* 原175→当前178确为173不变、2修改、3新增；修改路径为 DI 注册与 retry unit，新增为 transaction、factory、transaction unit 三文件。五个 patch 路径、manifest modified/added 集合、approval scope/current root/nested manifest Ref 相互约束，不能只凭 accepted 布尔或计数替代。product aggregate 固定为 `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c`。
* 六个历史 package lock 保持原哈希，unit lock 与原 approved 文档的 provenance 保留。archive、bridge、mapping、原准备来源及修订父来源均随固定嵌套 Ref 保持可追溯。
* 独立枚举 product root 的178文件、L0 source root 的185文件、L0 lock replay 除 bin/obj 外的186文件，path→SHA 均与各自 manifest 完全一致。185 精确等于 product178 加原七模板；186 仅再加原 L0 host lock。实现将这些实际来源文件和嵌套 Ref 纳入阶段前后保护。

阶段集合和接线符合冻结设计：`d_source_collection` 构造完整 path→SHA 期望集合，再要求声明集合全等；workflow196、cleanup185、N0/B0198、BC186、G6205、G12211 均使用精确集合，不接受 `>=`、同计数替换或额外路径。provider lock 已包含在178中，不重复计数。generated 路径被限定到同一合法 nonce 目录和 `.cs/.csproj` 闭包；生成锁单列，G12 保留原生成锁。D/native lock 的实际值继续来自 native stage 并受原 source/lock/startup/native graph 证据绑定，不造假预定锁值。

`prepare-d.py:245` 的 `frozen_input` 在第266行来源检查后才在第270行首次 `load_module`；来源门本身只读文件，不执行 SDK 或 importer。精确六输入字段、十一 Ref 角色、六 source origin Ref 均保留。source 阶段核两种集合；非 source 阶段读取 P0 时重核 origin，并绑定实际 P0 role root。B0 读取 N0 source parent 后核当前来源、native 增量与完整 lineage/W。prep startup 也再次检查来源。

真实 validator `startup()` 在原 `source_lock_documents()` 后调用 `startup_origins()`，因此原 source/lock/startup 值全等与物理列出文件检查继续生效，来源门随后约束 workflow、cleanup、G6/G12 的完整映射和精确 active locks，再进入 binary/deps/MVID 检查。`plan()`、runner launch、失败后的独立 cleanup 及最终 receipt 的 cleanup 路径均通过该真实 startup；来源检查不是仅测试中存在的孤立 helper。原 `source_lock_documents` 和 `continuation` AST 未变，其离线纯合同有效域未被真实来源要求破坏。

`build-generated.py` 第64行在第89行首次 SDK 调用之前执行 `generated_parent_origin`。此前已读回并核对真实 parent report/execution/startup/build receipt、profile、pending group/status、原 replay root、generated snapshot、ledger nonce/profile 及 source/lock。新 helper 进一步要求 G6 parent 为无 generated closure/parent-build 字段的 B0，G12 parent 为六文件 G6；parent source/lock/startup 的完整当前 lineage/W/pack/集合绑定后才放行。child 实际生成文件映射继续逐项枚举、与 checkpoint 相等，SDK 后按205/211集合核验，不以另一目录的同数量文件替代。

`lineage()` 增加三项固定 transaction pin；`same_overlay()` 在原六项上增加这三项，缺项或错值均拒绝。原模型依赖 gate schema 与调用方式保留，L0/E adapter 自然消费九项比较。runtime-template hash 仍允许各阶段不同，未新增跨构建根 EF DLL 同 SHA 要求，也没有创建模型接受或 runtime 授权。

测试静态审查：原 `DMutations` 整个类 AST 与 v4 相同（44 方法），原 `PruningContracts` 整个类 AST 与 v6 相同（9 方法），其 fixture、断言及被调用的旧纯合同保持原样。新增分别14和7方法，因此本版本 AST 发现数为58/16，**尚未执行，不记录 PASS**。

新增语义反例直接调用分离的 semantic gate，分别对 scope/root/nested Ref、43/7字段、175/178映射、patch五路径、173/2/3 delta、旧182、同计数替换、缺失/额外路径、L0锁差异、生成目录及九项 overlay 断言具体错误码。prep 反例保留真实磁盘 pin，只在 `read` 返回的文档上做受控变异，故不会被初始字节 hash 不匹配遮住；预期错误分别为 `transaction_approval_scope`、`transaction_product_root`、`transaction_exact_L0_185_mapping`，并断言 module/process sentinel 未触达。正例的首次 module sentinel 则验证完整来源门能够先通过。真实 startup sentinel 验证门在 binary identity 前实际被调用；generated parent 反例结合 adapter 的真实调用次序 AST 断言证明门在首次 SDK 前。上述只是对测试结构与可达性的静态审阅，实际发现数、自然退出和结果仍须独立验证者执行记录。

范围核对未发现 runner/policy/C#/W/EF 产品、包、helper、runtime、guard、CLI/oracle、cleanup 判定的越界修改或放宽。validator 原函数中仅 `lineage/same_overlay/startup` AST 改变；prep 原函数仅 `frozen_input/startup/main` 改变，其余 SDK/process/native graph/pruning 等函数 AST 不变。原 SDK 300秒、outer1620秒、host25分钟、CLI child3分钟及终止/回收约束保留；SDK版本、argv、native33/provider13/其余20、包hash域和原 framework/pruning 规则未扩大。

本报告接受的下一步仅为 root 按既定顺序派发独立完整58/16 Python合同验证。新 P0/N0/BC/B0、CLI proof、G6/G12 和各 profile 的 D runtime 必须继续产生本轮真实证据并逐门审阅。旧 D v6 离线接受、旧 P0/N0/BC/B0、历史失败以及 full EF C pending 均保持原状态，不由本次源码接受替代。
