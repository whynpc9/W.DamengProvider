# TP07 D 实库激活设计

2026-10-10。结论：**design_only / activation_blocked_pending_complete_E_Astra_acceptance_and_window_release**。本报告只读现有工具、18字段 plan 合同与证据；未执行或导入项目脚本，未运行 SDK、测试、CLI、wrapper、DB，未读取 secret，未修改源码、status、ownership 或旧证据。下列命令是未来 Low 窗口的原工具调用模板，不是执行记录；尚不存在的 path/SHA、model gate、checkpoint 和结果均保持待生成。

本次读到的 canonical `status.json` SHA256 为 `4f0aaae779347612bc5371c6b890cd68dca85ce43824b38ef3e250033e7ca327`；`active-ownership.md` 为 `ca6a3198e6b4e2f7ba1166b38f3eef16105c0aad45ed2fd018fa93f71227e4c8`。E 的 bounded_case_windows 仍为 `new25window338case_runtime_authorized`、`e_runtime_accepted=false`，唯一 SDK/DB 窗口属于 e_verify_v9。协调者所报 shared13窗180例通过及 TLS 在跑仅为进度，不能代替完整 E 接受。

## 1. 激活门与最早顺序

1. 等新 E 同 cohort 完成全部 shared180/TLS158、25窗338例，完整 coverage/最终 validator、每窗主清理与独立 exact-ledger 清理通过，服务器对象状态闭合，Low 明确释放 SDK/DB 窗口；Astra 对完整新证据作独立接受。不得从历史失败或局部重判拼接成功矩阵。
2. root 依据已接受 E、L0 与 TP06 实际 JSON 证据，新建每 profile 的接受适配记录及 D model-gate；复核同 W、同 mapping-v5 六项 lineage，并冻结实际 path/SHA。两份 E profile 接受记录和 TLS E closed7 目前不存在接受依据，不提前填写 `accepted_in_scope`。
3. 接受的 P0/N0/BC 在原路径保留。Low 在新 shared B0 目录运行原 `prepare-d.py build`，形成该 profile 的独立 workflow root、CLI manifest、source195、lock、startup/build receipt 与只读 CLI proof；root 审阅实物后签定 shared 的18字段 plan。
4. 建议 shared 先运行独立 `guarded` 阶段，终结并清理；随后 `scaffold → pending审阅 → G6外部构建 → child审阅 → scaffold-consume → pending审阅 → G12外部构建 → child审阅 → scaffold-finalize`。guarded 与 scaffold 没有工具级前后依赖；此选定顺序在生成工作根变更前完成 guarded，不增加验收例或放宽任何门。
5. shared 全部阶段及终结清理接受后，Low 在另一个全新 TLS B0 根执行同样序列。TLS 不继承 shared nonce、模型、生成源码、checkpoint、运行报告或 generated lock。SDK/DB 串行调度，pending期间不得把尚有所有权对象的链交给其他任务并发执行。
6. root/Astra 汇总两个 profile 的全部 stage、真实 CLI/加载身份、生成模型、guard/refusal、精确 oracle 和清理证据后，才给 D 的限定运行接受；仍独立于完整 EF-C、PR、合并与发布。

阶段政策的实际 case 数为 scaffold2、consume3、finalize1；guarded shared13、TLS3，因此 shared19、TLS9 为各自阶段记录数，不能称为28个不同 registry ID。TLS guarded 仅 `DOWN-G05-scope` 与 `R06-TLS6-dt/dto`，不得借 shared 的七位 Up/Down 执行扩张 TLS 范围。

## 2. 每个 profile 的 model_gate 与 closed7

`design-validate.py:dependencies` 要求 model-gate 是可读 JSON，经 `{path,sha256}` 引用，至少具有 `schema_version=1`、`task=TP07-D-model-gates`、`status=accepted_in_scope`、正确 `profile`、`W`、`lineage` 与 `accepted_gate_refs`。其中 `accepted_gate_refs` 的键必须恰好为 `L0`、`E`。两个被引用记录各需 `status=accepted_in_scope`、`issues=[]`、同 `W`、同 profile、同 mapping lineage、真实 `evidence_refs`。`artifact()` 会解析每个 evidence ref 为 JSON，不能把 Markdown 接受报告直接放入该数组。

| profile | accepted_gate_refs | seven_closed_refs | 必须保留的范围 |
| --- | --- | --- | --- |
| shared | 新 `L0-shared-accepted.json`；待完整 E 接受后新 `E-shared-accepted.json` | shared 合同不要求该字段；不伪造 closed7 | L0 shared12，仅已接受 FF7 literal 与裸声明六位观察；E 新180例完整范围 |
| tls | 新 `L0-tls-accepted.json`；待完整 E 接受后新 `E-tls-accepted.json` | 键恰好 `E`、`TP06`，分别指向新的 scoped closed7 JSON 接受适配记录 | L0 TLS2仅六位；E 新158例；两个 closed7 记录均须 `profile=tls`、同W、`seven_write=false`、`status=accepted_in_scope`、`issues=[]`、非空真实 `evidence_refs` |

建议把上述新 JSON 与两份 `model-gate-{shared,tls}.json` 放在 `.local/temporal-native/root-approvals/TP07-D-runtime-v1/`，它们当前只是待生成文件名。接受适配记录表达 root/Astra 已作出的限定判断，不修改旧 validator 的历史状态。L0 旧 summary 仍为 `L0_v3_value_contract_valid_pending_root_review`，TP06 TLS validator 为 `candidate_profile_accepted`，二者均不能直接冒充 D 所需的 `accepted_in_scope` 结构。

已存在、可作为适配记录依据的真实 JSON：

| 范围 | 路径（相对 `.local/temporal-native/verification/`）与固定 SHA256 |
| --- | --- |
| L0 两 profile 运行总收据 | `TP07-L0-v3-runtime/run-receipt.json` — `28eb8022d6eabe29ff7e2a589ca7a4d4df811f699cdbcf081b7aaa5357ad37f1` |
| L0 独立验证 | `TP07-L0-v3-runtime-validation/validation.json` — `9767f668d3ff214de78abd10c6c326c2ab233d02eabad769d7b0f19d6e949801` |
| L0 summary | `TP07-L0-v3-runtime-validation/verification-summary.json` — `b3b63c2d3ad2d87d1422eaf06359acfc953dc9c2234e86e8f5843f1921713680` |
| L0 shared main/cleanup | `TP07-L0-v3-runtime/shared/main/report.json` — `025be8d133c6fe050037f60b68d991f21a6bd8ad1b1eee56e92df5f8bca949b4`；`shared/cleanup/report.json` — `ad9243dc6ae8704b77f0dfa375ba6e369dc8fc5b474c04cf5045aaf7fa268921` |
| L0 TLS main/cleanup | `TP07-L0-v3-runtime/tls/main/report.json` — `b6275ea02f848b9e7cea31b0ade612ac20901a1bca56c68fde5429fc42cf46a4`；`tls/cleanup/report.json` — `d6b1a2ba164c9e8c5361d3890e156c8f6259633b541b688f8d270cbcf1f30ec0` |
| TP06 TLS candidate 验证 | `TP06-v12-tls-candidate-validation.json` — `cf98d22a722d1339f5f1b4bedc9d51a6855c46cb84469143e51397ec6825af13` |
| 同候选 capabilities | `TP06-v1-package/capabilities.root-approved.json` — `f10acfd86b5e7158eac0a0aa8cfb972418881837b4bfe63205245d82a08f54fa`；其中 tls.seven_write=false、write_scales仅0至6 |

L0 限定接受依据另见 [L0 v3 接受报告](TP07-L0-v3-acceptance.md)，SHA `3d4ca42e7f5a28b7a50c233667114d6487d386c729a4ec0d1ef0de0942938533`。TP06 closed7 适配还须追溯 [TP06 Astra 最终接受](TP06-astra-final-acceptance.md)，SHA `7a6263f52c365e6431f10b9ce565da176af4ff459acd8c0e652c480f6e89a97e`，及其中主TLS、Supplement TLS实际拒绝/清理收据，不能仅以 capabilities 的声明布尔值充当真实拒绝验收。

E 新证据目标 cohort=`5ca7c5129f66b1b75052e871eccc937d9a9159b322fa630e2427f9bcbcb1cba8`，新运行目标 `TP07-E-window-v1-runtime`；由最终真实 run receipt、完整 coverage validator、每 profile 窗报告/清理及实际 TLS closed7 case 形成引用。确切输出绝对根和最终 SHA 由 Low 完成后回读，当前不猜测或填写。其 approved-execution.json SHA `dbf8e164456fdde0ae3026b4ab19dda68d1ba3e655b2f4f151de6e3ebe735d51`、离线接受与 readiness 只证明本轮运行授权，不能替代 E 完成证据。

同 W 固定为 `0.1.0-temporal.20261008144915`、nupkg `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`、DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。同 overlay 比较 `ef_commit/base_archive_sha256/bridge_patch_sha256/mapping_manifest_sha256/mapping_patch_sha256/approved_ef_source_manifest_sha256` 六项；直接使用已核实 D/L0/E 各自 lineage 的同项值，不把 runtime-template hash 强行当同值，也不以 EF DLL 因构建根不同而相同作为条件。

## 3. 18字段 plan 的逐项来源

每阶段新建一份不可变 JSON，键集合必须恰好如下18项；没有第19项 host 或任意超时覆盖。模型门和 CLI/environment 不能通过临时变量绕过。

| 字段 | 来源与阶段变化 |
| --- | --- |
| schema_version | 1 |
| task | TP07-D-execution-plan |
| stage | guarded、scaffold、scaffold-consume、scaffold-finalize；失败恢复仅明确 cleanup |
| profile | shared 或 tls |
| source_pack | `TP07-D-tools-v4/source-pack-manifest.json`，SHA `124cbc1897846a4bc23d433ff71ca2baa3942780cb26363f2f7ba890a38336c0` |
| startup_manifest | 当前 profile 新 B0，随后 G6/G12 child manifest 的真实绝对 path/SHA |
| external_build_receipt | 与上述 startup 精确成对的新 B0/G6/G12 receipt |
| cli_tool_manifest | 当前 profile 新 B0/cli-tool-manifest.json；replay_root 必须等于本profile workflow root |
| model_gate_manifest | 本profile新 root 接受记录，当前 pending |
| helper_ast_proof | `verification/TP07-constructor-v4-helper-maintenance/maintenance-receipt.json`，SHA `88424da24c4d68d4561e73afeaa2961f87b5cf8b7173788d4e519a1553b1b7a2`，pure16及传递函数AST闭包必须相等 |
| candidate_manifest | `verification/TP06-v1-package/identities.json`，SHA `e14eb1baca9a3c07b04f988dd3bcdb652048fd1a61211ad8c2cb117a9e0955a3` |
| capabilities | 上述固定 capabilities JSON |
| original_repo | 已接受原 wrapper 根 `/Users/wanghongyi/Projects/W.DmProvider`，不是本 worktree，也不复制 secrets |
| wrapper | 该根下 `scripts/with-dameng-test.sh` 或 `with-dameng-tls-test.sh`；SHA分别 `b2af867921b567045fdeeccb3da490662661976b182e5884d029a738d41e00d8`、`872d722808a7f53415505e2536438d1b644793822b2abeac3cb5b662ac94d706` |
| python | Low 实际解释器的绝对 path/SHA，执行前核定；不可仅写 python3 命令名 |
| parent_checkpoint | guarded/scaffold 为 null；consume/finalize/cleanup 为恰好含 report、execution_receipt 两个 path/SHA引用的对象，来源必须是同profile真实 pending parent |
| cleanup_startup_manifest | 已接受独立 BC startup，SHA `1d8876cdd14375e2edbd21bf3ff6977fde9e4718c9e8a2f1d29a9738729918af` |
| cleanup_build_receipt | 同 BC receipt，SHA `67fb03fb430b12fdc11600bae1e7495168eddfb9f19bee6b8ec09e171ab1f116` |

BC 是 profile 参数化且不可变的独立清理 host；两个 profile 串行可复用它的已接受构建字节，各次清理必须产生新运行/身份/服务器fresh-absence证据。它的根 `TP07-D-prepared-v3-cleanup-build/replay` 与两profile workflow根均分离且不嵌套。若该闭包漂移，须停止并经新 freeze/构建/接受替换，不能现场修旧receipt。

## 4. 可复用项与必须新建项

已接受的 [D v6 离线报告](TP07-D-preparation-v6-offline-acceptance.md) SHA `c0bae3766d6a420ea02fad1c5e2310711dfaf17178cb8ccc5aa9a6fdd9bef6ff`，最终 Low 回读 `TP07-D-prepared-v3-control/offline-chain-readback.json` SHA `a3643541fe05becc884337f43c07aec927ab43b2545c05af5477fa778190611b`。可复用固定 D25、9合同、P0 workflow193/cleanup182及feed/tool原包、N0 source195及native33、精确三lock、BC183/25 binary。原B0195/125 binary及CLI proof作为历史接受和新B0对照保持；不把原B0 replay改造成两个profile共用生成根。

必须新建每profile的 B0 工作目录、实际 locked restore/build与CLI事件、startup/build/CLI manifests、root model gates、18字段plans、before快照、所有 runtime/cleanup/validator 输出、G6/G12 checkpoint与构建。无需在无漂移时重跑已接受 P0/N0；新B0原工具支持消费原P0/N0并复制其锁与cache。新B0不可只复制bin后改路径与收据来冒称构建。

建议新输出（均未创建；如已存在则选下一个版本，禁止覆盖）：

- `.local/temporal-native/verification/TP07-D-runtime-v1-shared-build/replay` 与 `.../TP07-D-runtime-v1-tls-build/replay`：独立 B0 工作根；G6/G12始终在各自同根生成。
- `.local/temporal-native/verification/TP07-D-runtime-v1-<profile>-<stage>`：guarded/scaffold/scaffold-consume/scaffold-finalize 每阶段新输出。
- `.local/temporal-native/verification/TP07-D-runtime-v1-<profile>-G6` 与 `...-G12`：外部phase制品目录，必须在workflow根之外且不嵌套；内部含parent binary snapshot和新的source/lock/startup/build manifests。
- `.local/temporal-native/root-approvals/TP07-D-runtime-v1/`：新适配JSON、model gates与每profile每stage plan。实际 path/SHA由root冻结，本文不创建可执行plan。

## 5. 原工具命令模板

以下仅为调用模板。`<...>` 是必须由真实产物替换并经root审阅的值，不能用占位符生成接受JSON。`W`表示本worktree绝对根，`V=W/.local/temporal-native/verification`，`P=V/TP07-D-preparation-v6`，`T=V/TP07-D-tools-v4/source/tools/DownstreamAcceptance/TP07`，`I=W/.local/temporal-native/root-approvals/TP07-D-preparation-v6-inputs/inputs.json`；`PY`为plan中核定解释器。准备脚本必须从原批准 P 目录调用，其位置校验依赖原层级。

新 profile B0 使用原工具入口，shared完成后再为tls安排：

```sh
"<PY>" -B "<P>/prepare-d.py" build \
  --workspace-root "<W>" --inputs "<I>" \
  --inputs-sha256 18f66c72050417457aca5157ce9df85b8baf7f722f6ed5ee38087665c0b72b70 \
  --source-stage "<V>/TP07-D-prepared-v3-source" \
  --source-manifest-sha256 66c71f2c3085744fc4921d6768d14b2ef5486c33e4e63f57b06086243ed2be7c \
  --lock-stage "<V>/TP07-D-prepared-v3-lock" \
  --lock-manifest-sha256 4ed4ec81737f1bdce1fa01aa0f878e23d985f3402e592a1b352fcae1a3be8478 \
  --source-reviewed --locks-reviewed \
  --output "<V>/TP07-D-runtime-v1-<profile>-build"
```

`prepare-d.py`实际SHA `bd1433e90bf90123b536350077b161c2878db9d8142d028d311407c6da6768f3`。本命令新做SDK查询、L0/D各locked restore/build共5事件，另只读CLI3事件。SDK固定10.0.401；脚本设置任务CLI_HOME及两个DOTNET禁用首启/遥测变量，single-node flags含 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`，restore另含 `--disable-parallel`，单SDK事件300秒。不得放宽原标志或追加网络/包升级操作。

每个已批准 runtime stage 运行与独立验证：

```sh
"<PY>" -B "<T>/design-run.py" stage \
  --plan "<absolute-stage-plan.json>" --plan-sha256 "<actual-plan-SHA>" \
  --output "<new-stage-output>"
"<PY>" -B "<T>/design-validate.py" \
  --execution-receipt "<new-stage-output>/execution-receipt.json" \
  --execution-receipt-sha256 "<actual-receipt-SHA>" \
  --plan "<absolute-stage-plan.json>" --plan-sha256 "<actual-plan-SHA>" \
  --output "<new-stage-output>/validation.json"
```

冻结runner SHA `092e976e84f63f4a06a0e4f531d24905d4bcab5f62ddda6b1b69824b171637eb`、validator `6e9fd825b2fb568ef9483218be3c733f0857375742f8ee8b304cef9145741bf8`，本次与工作树文件逐字相同；运行时仍指定冻结 T 路径。runner自行调用plan原wrapper，再由internal-host以绝对dotnet exec运行D或独立L0 cleanup；不得在外层先source secret或绕过runner手调host。凭据仅由wrapper传入子进程内存，不出现在argv、报告或日志。

主报告为 `<stage>/main/design-report.json`，另有execution-receipt、host-main、process-receipt；pending阶段新增 generated-source-snapshot.json及snapshot目录、main/ownership-snapshot.json，并保留精确active ledger；terminal阶段含cleanup/report.json和相应进程收据。外层1620秒、D host25分钟、CLI child3分钟均使用原值，不能照搬 E 的900/300/180窗口。每次实际连接仍须在报告中核WDM_PROVIDER_TEST身份及profile物理属性。

G6/G12由独立Low在root接受实际pending checkpoint后调用：

```sh
"<PY>" -B "<P>/build-generated.py" "<generated6-or-generated12>" \
  --workspace-root "<W>" --inputs "<I>" \
  --inputs-sha256 18f66c72050417457aca5157ce9df85b8baf7f722f6ed5ee38087665c0b72b70 \
  --prep-script "<P>/prepare-d.py" \
  --prep-script-sha256 bd1433e90bf90123b536350077b161c2878db9d8142d028d311407c6da6768f3 \
  --profile "<profile>" --checkpoint-reviewed \
  --parent-report "<parent-main/design-report.json>" --parent-report-sha256 "<actual-SHA>" \
  --parent-execution "<parent/execution-receipt.json>" --parent-execution-sha256 "<actual-SHA>" \
  --parent-startup "<parent-startup-manifest.json>" --parent-startup-sha256 "<actual-SHA>" \
  --parent-build-receipt "<parent-build-receipt.json>" --parent-build-receipt-sha256 "<actual-SHA>" \
  --output "<V>/TP07-D-runtime-v1-<profile>-<G6-or-G12>"
```

`build-generated.py`固定SHA `4364da01fdecb97dc7063002d730926e45afb4923606ac1b9c69b22565fdfccf`；不运行D host、CLI或DB。G6 parent startup/build是本profile B0，G12是已接受G6 child；不能用B0替换G12 parent。

## 6. G6/G12 边界与失败停点

| 阶段 | 必须看到的实际结果 | 审阅及继续条件 |
| --- | --- | --- |
| guarded | shared13或TLS3完整；terminal_complete；主清理及独立L0清理成功 | validator stage_validated_in_scope、issues=[]，fresh absence；首失败停，不进入scaffold |
| scaffold | 两次真实CLI scaffold，生成4个C#加csproj/DesignStartup共6源；report=`generated_pending_external_build` | execution=`pending_external_build`；validator同pending、runtime_accepted=false；保留nonce owned对象，核snapshot与ledger；此时不清理、不接受D |
| G6 | 同root的 `test/TP07Generated/<32位大写hex nonce>`；SDK query、首次generated force-evaluate/use-lock-file restore、locked restore、build共4事件；source202 | generated自己实际24包/两个Project、assets/dgspec/pruning/边/hash合规；BuildProjectReferences=false；base195及原3locks/3assets/host/provider不变；root接受新child |
| scaffold-consume | 实际compiled consumer六位micro/NULL模型门通过，再真实生成两套migration、每套3文件；closure12 | report=`snapshot_pending_external_build`、execution pending；新source/ledger snapshot；不得把未编译migration说成已接受 |
| G12 | SDK query、generated locked restore/build共3事件；source208；generated lock字节保持，不force-evaluate | 冻结旧consumer binary snapshot；保留base host/source/locks/assets；自身generated图重新核验；新consumer DLL/MVID/deps与parent引用冻结，root接受child |
| scaffold-finalize | 消费第二次编译的generated assembly、真实两套CLI migration script、snapshot/migration模型核验 | terminal_complete及独立exact-ledger清理；validator stage_validated_in_scope，不自动设置runtime_accepted/candidate_acceptance |

Scaffold及migration C#调用看似 `tool run dotnet-ef -- ...`，但冻结 `Program.Child` 将其转换为已批准绝对host的 `exec <tool_root>/dotnet-ef.dll ...`，不依赖local/global resolver。scaffold使用命名连接 `Name=ConnectionStrings:TP07`、固定provider、两套context/table、`--no-onconfiguring --no-build`；consume为两个context分别 `migrations add ... --no-build`；finalize为两个context分别 `migrations script ... --no-build`。具体nonce/project/output由实际report给出，不手工编造命令来取代原调用链。G6/G12才允许外部编译，CLI内部不隐藏build。

所有阶段首失败停止后续业务动作，保留自然exit、timeout/termination、实际partial report及原失败；runner对terminal且已取得ledger者仍执行其既定独立cleanup。pending后的外部build/审阅失败或放弃：使用同profile、同sourcepack、独立BC的专门 `stage=cleanup` 18字段plan，parent指向最近真实pending report/execution；用上述原runner/validator命令写全新cleanup输出。此路径跳过非cleanup模型门和workflow CLI依赖，不跳过cleanup host、candidate、wrapper、AST与ledger可信度。不能用它新建fixture、跨schema操作或按前缀清扫。

独立cleanup失败、超时或fresh absence未证实即保持 `cleanup_pending`，冻结精确ledger并交root处理，禁止推进另一个profile或重试业务。没有可信ledger的preflight失败记录为before-DB/未知边界，不补造ledger或服务器最终状态。pending成功本身保留owned对象，因此不要为了让当前阶段看似“干净”提前清理；也不要把pending写成accepted。

当前阻塞项为完整E Astra接受与窗口释放、两profile实际model-gate/接受适配引用、TLS E closed7实际证据、两profile新B0/plan及各真实pending后的G6/G12审批。其余现有离线证据是可复用前置输入，不能消除上述运行门。本设计没有签发执行授权。
