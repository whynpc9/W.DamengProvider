# TP07-D transaction v1 preparation design

本报告仅固化下一任务的实施设计，不激活 D，不接受 D runtime。只读核对了 D v6 preparation、D validator/合同源、新 L0 transaction v2 来源链及 E transaction v3 实际末轮记录；没有导入项目模块、运行测试、SDK、host、数据库或 Git。旧 D v6 离线接受、旧 source195、旧 P0/N0/BC/B0 和所有失败证据保持原样。

当前 E `validation.json` 的实际 SHA 为 `7ddd3743f8df499c178352965552961f7c5fd5d7e3217c27cf2d23358d723d56`，25 窗/338 例及最终 validator 已通过；最终 Astra/root 接受仍是 D 激活的前置条件。本报告不提前生成 accepted profile adapter。D 实施和验证窗口须由 root 在该依赖闭合后另行派发。

## 1. 唯一实施范围

Tracked 只改两个文件：

* `tools/DownstreamAcceptance/TP07/design-validate.py`：把已存在的 D 来源、startup 和跨 profile 模型来源校验接到当前 transaction product；保持原 runtime/CLI/guard/scaffold/cleanup 判断。
* `tools/DownstreamAcceptance/TP07/test_design_contracts.py`：保留原 44 个测试方法 AST，追加当前来源正例及定向反例。

新 ignored preparation 目录建议 `.local/temporal-native/verification/TP07-D-transaction-preparation-v1/`，只从 `TP07-D-preparation-v6/` 复制并修订五文件：`prepare-d.py`、`build-generated.py`、`test_pruning_contracts.py`、`schema-draft.json`、`README.md`。原目录不改。新 D25 pack 建议 `TP07-D-transaction-tools-v1/source/`；25 个成员中只有上述两个 tracked 文件换为新冻结字节，其他 23 个成员逐项等于原 `TP07-D-tools-v4/source/`。不增加 pack 成员，不改 `design-run.py`、`design-cases.json`、十个 D template、七个 L0 template、EF/C# 或 W 包。

尤其保留旧 D pack 内 `runtime-validate.py` SHA `d9ea39850a3d394f86f2fef54fe8330d732ea3a46d6823da3da74e84b9188d55`，作为原 pure-16 helper 提取来源；不把新版 L0 validator 拷入后冒用旧 pin。原 helper AST proof、提取算法及原 process helper/identity helper/process policy 的字节和用途保持。当前 L0 的完整接受通过真实 Ref 消费，不通过替换这份 helper 建立。

## 2. 当前来源及固定 Ref

下表路径除特别说明外，相对 `.local/temporal-native/verification/`。Ref 均使用实际 canonical path 与完整 SHA；High/Low 不得用表内说明文本代替真实文件。

| 角色 | 实际文件 | SHA-256 |
| --- | --- | --- |
| 原 approved175 | `TP07-L0-tools-v3/approved-EF-source-manifest.json` | `dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6` |
| 当前 L0 source185 | `TP07-L0-transaction-v2-prepare/source-manifest.json` | `4f50b6188f13b7718f6dbabdc853ab63868c6ac5a36456cf690d012e91695faf` |
| 当前 L0 lock186 | `TP07-L0-transaction-v2-lock/lock-manifest.json` | `6982a1b12187c1f9ed18810222b5d8347528d4d758822b6697f8f4b5b074ecbb` |
| 当前 L0 startup | `TP07-L0-transaction-v2-build/startup-manifest.json` | `a1cc53af8ef27db498777cda211975e941270ec903fb830d23387c780cd237a5` |
| 当前 L0 最终验证 | `TP07-L0-transaction-v2-runtime/independent-validation.json` | `266a3054f496cb23d6f1d738e26d90e6b2f6e83fdde3c60328568328d63dfa1b` |
| 当前 L0 run | `TP07-L0-transaction-v2-runtime/run-receipt.json` | `2c76e364215ca32067286f8c75759279fe56905569d859a49ef013b3ae6e6da9` |
| E v3 整轮最终验证 | `TP07-E-transaction-v3-runtime/validation.json` | `7ddd3743f8df499c178352965552961f7c5fd5d7e3217c27cf2d23358d723d56` |
| E v3 整轮 run | `TP07-E-transaction-v3-runtime/run-receipt.json` | `5905de5a13df036b0553568711957397efa0cf13cf95f306e57b9f1402c6417c` |
| E 当前 startup | `TP07-E-transaction-v2-build/startup-manifest.json` | `1cadd834dacf071f35355188d549976f2b750af579f442f498630031310eb743` |
| TP06 TLS 实际验证 | `TP06-v12-tls-candidate-validation.json` | `cf98d22a722d1339f5f1b4bedc9d51a6855c46cb84469143e51397ec6825af13` |

当前 product manifest 是仓库路径 `tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json`，SHA `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8`；signed patch 为同目录 `transaction-capability.v3.patch`，SHA `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f`。source approval 是 `.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json`，SHA `66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f`，唯一 scope 为 `transaction_capability_and_w_retry_contract_tests_only`。

product178 实际根为 `TP07-T1-transaction-adapter-v3/ef-source`，aggregate `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c`。原175中173文件不变，两处修改为 service collection DI 注册及 retry unit；三处新增为 `DamengRelationalTransaction.cs`、`DamengRelationalTransactionFactory.cs`、`DamengRelationalTransactionCapabilityTests.cs`，路径以 manifest/patch 的五项完整路径为准。保留六个历史 package lock 及原 unit-lock provenance，不将 approved175 manifest 改名解释成178。

新 `l0_root` 精确为 `TP07-L0-transaction-v2-prepare/replay`。source manifest 本身没有 root 字段，不得读取或补造一个不存在的 `replay_root`；通过输入 canonical root、已固定 source Ref 及其完整185文件映射建立物理绑定。它的10项 `lineage_artifacts` 已提供 old approved175/base archive/bridge manifest+patch/mapping manifest+patch/runtime-template source，以及上述 transaction manifest/patch/approval。沿这些实际 Ref 读取，不增加顶层散乱输入。

W 保持 `0.1.0-temporal.20261008144915`；package SHA `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`、DLL SHA `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。本范围不需要 TP06 重包；若实施发现产品必须改，停止本范围并另行设计。

## 3. prep 字段与函数接线

`prepare-d.py:frozen_input` 保留精确六顶层字段 `schema_version/task/references/source_pack_root/l0_root/dotnet`，以及原十一 Ref 角色 `source_pack/l0_source/l0_lock/packages/W_package/CLI_package/process_helper/identity_helper/process_policy/design/pruning_design`。新 inputs 将 `design` 指向本报告冻结 SHA、`source_pack` 指向新冻结 D25，L0 两 Ref/root 换为第2节真实值；其余固定原包/工具/SDK/pruning 设计，不引入宽泛授权字段。新的 pack SHA 在两个 tracked 文件冻结及独立 snapshot 后产生，随后再冻结 prep 的该 pin；本报告不猜未来 SHA。

在 `frozen_input` 调用 `load_module` 或任何 SDK 前，增加本地、有限的纯文件来源检查。核 current L0 manifest 的185映射、原175映射、product178映射、7模板、10项 lineage/10项 artifacts；逐 Ref 哈希并核 product43字段、approval7字段及其当前根/固定 scope、manifest nested refs 与五路径 patch 一致。依据实际旧175和当前178哈希映射证明173 unchanged/2 modified/3 added，核 signed patch SHA/路径及物理源码；不能只检查计数，不能把任意 approval 的 `accepted` 布尔当授权。旧175 archive/bridge/mapping/unit-lock 来源保持可追溯。D 消费已被 L0 正式 replay 并验收的185，不再另做 Git replay，也不直接复制 product178来跳过该来源。

`source_document` 保留原 source schema 与六个 `origin_refs`：`source_pack/l0_source/l0_lock/packages/design/pruning_design`，继承 L0 完整10项 lineage。上述 nested artifacts 的路径/SHA随 `l0_source` 可独立还原，并纳入 prep 前后保护。`startup`、每个非source阶段重新读取 P0 source/lock 的位置，均复核相同来源和精确集合；保持已有 source/lock/startup 值相等、binary/deps/MVID、native assets 与 lock 校验。

`main` 只修正固定来源/root及以下精确集合数量，不改变 phase/flags、process helper、SDK argv、退出/超时判断、feed/native graph/pruning。`build-generated.py` 在读取且核对真实 parent report/execution/startup/build receipt 后、首次 SDK 前，调用新 prep 的有限 origin 检查；G6 parent 必须是本 profile B0，G12 parent 必须是本 profile G6。仍核同原 replay root、pending status、ledger nonce、generated snapshot、parent source/lock、精确生成路径，不能凭计数接受另一目录内容。

| 阶段 | 精确源码集合 |
| --- | --- |
| P0 workflow | 196 = product178 + L0模板7 + D模板10 + local tool manifest1 |
| P0 cleanup | 185 = product178 + L0模板7 |
| N0 / B0 | 198 = workflow196 + L0/D两个 host lock |
| BC | 186 = cleanup185 + L0 host lock |
| G6 | 205 = B0198 + 实际generated6文件 + generated lock1 |
| G12 | 211 = G6205 + 第二批实际generated6文件；既有generated lock仍受核验 |

原 provider lock 已在 product集合内，不能再加一次。每项须核完整 path→SHA 集合及合法增量；禁止 `>= count` 或填补假的锁文件。SDK native graph33、provider13/其余20、native contentHash 与 ZIP SHA512 的不同域、原 framework/pruning 常量及 fixed package version保持。重用包档案字节不代表重用旧 native/build 接受。

## 4. 两个 tracked 文件的必要改动

当前 `design-validate.py:lineage` 只验证旧 mapping 与 W，`same_overlay` 只比较六项；`startup:source_lock_documents` 校验列出的 source/lock/startup相等但未独立证明它们来自当前178。仅更新 prep pin 会留下旧195或拼接cleanup/child身份可被解释成当前来源的缺口，因此两个 tracked 文件必须正式修订。

`lineage` 增加固定三项 `transaction_product_manifest_sha256/transaction_patch_sha256/transaction_source_approval_sha256`，值见第2节；`same_overlay` 将这三项加入原六项 `ef_commit/base_archive_sha256/bridge_patch_sha256/mapping_manifest_sha256/mapping_patch_sha256/approved_ef_source_manifest_sha256`，原六项全部保留。D 内部传递完整10项 lineage，跨 L0/E/D 比较这九项；各自 runtime-template hash 不强制相等，跨构建根 EF DLL 也不要求同 SHA。

新增有限、独立的来源 helper，在真实 `startup()` 消费 `source_lock_documents()` 返回值后接入。它从 source `origin_refs.l0_source/l0_lock`核第2、3节固定来源、当前 D25模板和实际源码增量，覆盖 workflow、cleanup、G6、G12；不导入 T1/L0当前 runner 或 validator 当 oracle，不改变原 source_lock_documents/continuation 纯合同的有效域。所有真实启动路径、离线 validator、cleanup、生成 child 都经过 startup，故同一来源检查不能只放在 prepare端。D25自身身份沿原 pack机制核验，不能硬编码 validator 自身SHA造成循环。

原 `dependencies` JSON模型 gate形状不改，现有 `same_overlay` 调用自然消费新增三项。保留原 `runtime/report_contract/guarded/continuation/cli/process_identity/validate_receipt` 行为、exact command/runtime tuple/oracle/cleanup规则。新 helper必须实际连接全部startup入口；只增加未被调用函数或只在测试中核对，不构成修复。

## 5. 模型依赖与 runtime DAG

待 NEW E 最终 Astra/root接受后，为 shared/tls 分别新建原 schema 的 root model adapters：`schema_version=1`、`task=TP07-D-model-gates`、`status=accepted_in_scope`、正确 profile/W/当前 lineage、`accepted_gate_refs` 恰为 `L0/E`。各接受记录须 `issues=[]`、同 profile/W/九项 overlay，并引用第2节 NEW L0 validation266a及 NEW E validation7ddd、各真实 run 和对应 profile 主报告/清理/实际coverage证据。它们是既有接受的有限引用适配，不能把 `E_windows_complete` 或 producer pending flag单独解释成接受。`artifact()` 读取 JSON，因此 Markdown最终审阅报告不直接放入 JSON `evidence_refs` 数组；root审批依据另行保留真实审阅Ref。

TLS `seven_closed_refs` 恰为 E/TP06，两个记录均绑定当前 W、tls、`seven_write=false`、实际接受及非空JSON证据。TP06追实际TLS拒绝、后续健康/清理；E追本轮完整TLS158/整轮338闭合。六位观察不改称七位写入支持。旧 L0/E 接受适配不能只换 status后复用；新Ref必须来自本轮真实证据。最终E审阅尚在进行时不创建该接受记录或运行D。

每个 profile 串行执行：fresh B0 + 真实 CLI proof → guarded（shared13 / TLS3）terminal及精确清理 → scaffold2 pending → 外部G6与独立审阅 → scaffold-consume3 pending → 外部G12与独立审阅 → scaffold-finalize1 terminal及精确清理。shared与tls使用不同 B0/replay/generated根。新 BC 构建接受后可保持字节不可变并供两 profile复用，每次独立cleanup仍实际建立 fresh TEST连接、按本次精确ledger清理并证实服务器 absence。

保留原18字段plan，不加运行捷径；parent checkpoint仅来自同profile真实pending的 report/execution。原 outer1620秒、host25分钟、CLI child3分钟、每SDK300秒及终止/回收机制不变。只有 original TEST wrappers加载凭据；实际SQL/CLI由原runner执行，不手动运行host绕门。全部CLI仍dotnet-ef10.0.12六文件固定、真实 `dotnet exec` 路由、命名连接/no-build/no-OnConfiguring等原合同，禁止工具resolver fallback。无 selected retry或拼接历史替代完整阶段接受。

## 6. 增量测试及最早执行顺序

原44 design方法、原9 pruning方法AST逐项保持；pruning 的 `TP07-D-prepared-v1-lock/replay` 九文件失败fixture和旧L0 Specification-lock fixture仍只是固定离线合同输入，不能称为新native接受。新增类/方法使用真实固定 Ref 加受控语义/物理变异，不把mock成功SDK当结果：

* 正例证明175→178→185及各D精确增量；变异覆盖旧175/182、三项transaction错值/遗漏、approval scope/根错位、patch路径或manifest映射错位、同计数源码替换、额外/缺失路径和锁差异。
* 分别覆盖 startup workflow/cleanup、parent/child G6/G12 传播，以及模型adapter原六项与新增三项；保留来源相同但runtime-template不同的合法跨阶段情形。不能为旧fixture通过放宽当前真实startup来源。
* prep拒绝用首次 process/module/SDK前的 sentinel 证明新来源门先执行；generated阶段在首次SDK前拒绝错误parent。反例必须到达预期判断并核具体失败原因，不能全被初始hash错误遮住。

High冻结两tracked及五prep后，Astra审源码，Low独立静态快照/hash/AST核验；新D25先冻结，prep再固定该SHA与本报告SHA。Low才运行原两套完整Python合同加新增方法，记录实际发现数和自然退出，不预称新总数PASS：

```text
python3 -B -m unittest discover -s <NEW-D25-source>/tools/DownstreamAcceptance/TP07 -p test_design_contracts.py
python3 -B -m unittest discover -s <NEW-prep> -p test_pruning_contracts.py
```

源码/合同接受后按既有CLI顺序准备；以下只是模板，未来SHA必须来自实际冻结文件，不是执行授权：

```text
python3 -B <NEW-prep>/prepare-d.py source --workspace-root <ROOT> --inputs <NEW-inputs> --inputs-sha256 <SHA> --source-reviewed --output <NEW-P0>
python3 -B <NEW-prep>/prepare-d.py lock --workspace-root <ROOT> --inputs <NEW-inputs> --inputs-sha256 <SHA> --source-reviewed --source-stage <NEW-P0> --source-manifest-sha256 <P0-workflow-source-SHA> --output <NEW-N0>
python3 -B <NEW-prep>/prepare-d.py cleanup --workspace-root <ROOT> --inputs <NEW-inputs> --inputs-sha256 <SHA> --source-reviewed --source-stage <NEW-P0> --source-manifest-sha256 <P0-cleanup-source-SHA> --output <NEW-BC>
python3 -B <NEW-prep>/prepare-d.py build --workspace-root <ROOT> --inputs <NEW-inputs> --inputs-sha256 <SHA> --source-reviewed --source-stage <NEW-P0> --source-manifest-sha256 <P0-workflow-source-SHA> --lock-stage <NEW-N0> --lock-manifest-sha256 <N0-lock-SHA> --locks-reviewed --output <NEW-profile-B0>
```

P0→N0逐门接受；BC可在P0接受后单独安排，但窗口仍串行，以上列为确定的顺序。N0原3、B0原5、BC原3 SDK事件和B0原3 CLI只读事件保留。P0 feed/CLI原包可复用已核字节，源/native/build/CLI proof均产生新真实制品。G6/G12保留原 `build-generated.py generated6|generated12`、inputs/prep-script及SHA、profile、parent-report/execution/startup/build-receipt全部path/SHA、`--checkpoint-reviewed --output` 参数；原SDK事件4/3不变。它们只能在相应DB pending checkpoint实际产生并被接受后执行。

Low对新的 source196/185、native198、BC186、各B0198及CLI proof分别作独立哈希/来源/native/纯metadata身份回读；Astra/root接受这些真实offline证据后才审新18字段runtime plan并显式授权下一阶段。原 D v6 offline 接受不能代替本轮。后续G6/G12也必须以实际generated集合、native/build/startup及parent链逐门接受，最后依据各profile完整原合同和fresh精确清理关闭D。

## 7. 尚待事项与边界

尚待 NEW E 最终 Astra/root接受、root派发本报告实施、两tracked/五prep新冻结及上述新D离线链；当前没有 D runtime授权。本报告未发现需要扩大产品/C#/policy/runner范围的来源设计障碍。若实际实现出现新API或合同矛盾，应报告具体阻塞并由root修订范围，不能默默增文件、放宽oracle、延长预算或把旧D证据重新标为当前接受。本报告写完即冻结，不修改任何历史证据与协调状态。
