# TP07 D owned-reader maintenance design

状态：DESIGN_ONLY。本文件先定义当前 shared exact2 的隔离恢复，再定义后续 D utility 来源修订。它不授权本轮执行 SDK、TEST host 或数据库操作，不表示恢复完成，也不接受 D runtime、scaffold 或 full C。下一依赖是恢复遗留对象；High 实现、Astra 重要源码审查与冻结、Low 独立 build/readback、root 明确恢复运行许可依次进行。

## 1. 已有事实与保留边界

当前 D shared guarded 的主 host 与独立 BC cleanup 均失败，原失败证据保持不变。`OwnedObjects.VerifyStructure` 的 PK command/reader 使用方法级 `using var`，在 reader 尚未 Dispose 时执行同连接的 Index/Check `ExecuteScalar`。冻结 W 的 `Read()` 返回 false 不释放执行 lease，`DmSession.BeginExecution` 在 Busy 状态拒绝下一操作。History 没有 child 查询，而 GuardedDt 有两个已 ACK 的 child，符合本次 ledger 的分叉状态。

主流程的 finally cleanup 可覆盖最初异常；因此上述为实际冻结源码与状态共同支持的故障路径，不冒称已经取得最初异常的运行栈。主报告 10 条 command records 为 5 对 attempted/completed；248 条 protocol frames 为 240 个 SQL code 0 和 8 个 111（W 定义的 empty-result code）。这些记录不提供两对象的最终 absence 证明。

冻结输入均相对于本 worktree：

| 输入 | SHA-256 |
| --- | --- |
| `.local/temporal-native/verification/TP07-D-transaction-v1-shared-guarded/main/ownership.json` | `19f71ff40c67768c924b346d726ff0f885791eac4be432c5009f415292aaa9b2` |
| 同 run `main/design-report.json` | `940a24249faf9eb1bca4ea63629cd6bf167a16fc6f9eb9b34ee31ae26d842290` |
| 同 run `cleanup/report.json` | `55d3a0c45846bcf8f1db2dfd571cd06b37a62e90728736b1c89ef05a38759148` |
| `TP07-D-transaction-v1-shared-guarded-control/failure-independent-readback.json`（verification 下） | `568ef59b426ba79adb78427976b86ee046d557c5a63ff4b073218d529f993e70` |
| 同 control `artifact-manifest.json` | `d60f8541d69213bf1c4a12f28ac6a5fba7bd83feb488133c3d4d857b3a1aa901` |
| `TP07-D-transaction-v1-cleanup-build/source-manifest.json`（verification 下） | `71380a35f01091e357038fc8feb97424593aa08165ce4efe1148f6b6fde3c3fa` |
| 同 BC `lock-manifest.json` | `7bd4286d0d7df3574bdefe2940835bc9962ddc8833dfa0be51aaec0abbd95c92` |
| 同 BC `startup-manifest.json` | `8d69ec7f3d3c41b44b13bbd60bcada502aac1da7cbd1b74680564891723cf982` |
| 同 BC `build-receipt.json` | `68fe559c87f55f928fba12daff4dd655bbc33ddaac568e4fe6373646202b5683` |
| BC/B0/D25 的原 `OwnedObjects.cs` | `80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946` |
| 原 `scripts/with-dameng-test.sh` | `b2af867921b567045fdeeccb3da490662661976b182e5884d029a738d41e00d8` |

所有旧 source/build/run/approval/review 文件和原 ledger 均只读保留。原 SOURCE/OFFLINE_ACCEPTED_IN_SCOPE、L0 和 E v3 的 scoped 接受仍描述各自原哈希，不回写成修复版。恢复成功也不改变本次 guarded failure。

## 2. Exact2 恢复对象

恢复只允许 shared profile 的 `WDM_PROVIDER_TEST` 用户及同名 schema，唯一对象集合为原 ledger 的完整两项，禁止子集、额外项或更换 nonce：

| 字段 | GuardedDt | History |
| --- | --- | --- |
| case_id | `R06-UP-dt` | `R06-HISTORY-dt` |
| nonce | `D2CE4C6B94EE43AC8DB3851844C74981` | `6076EC28B6E14C9F99BCB37241CF3368` |
| table | `TPL_D2CE4C6B94EE43AC8DB38518` | `TPL_6076EC28B6E14C9F99BCB372` |
| primary_key | `TP07PK_D2CE4C6B94EE43AC8DB38518` | `TP07PK_6076EC28B6E14C9F99BCB372` |
| owner_marker | `OWN_D2CE4C6B94EE43AC8DB38518` | `OWN_6076EC28B6E14C9F99BCB372` |
| index | `TP07IX_D2CE4C6B94EE43AC8DB38518` | null |
| check | `TP07CK_D2CE4C6B94EE43AC8DB38518` | null |
| shape / scale | `GuardedDt` / 6 | `History` / 6 |
| creation_state | `create_acknowledged` | `confirmed` |
| create / comment ACK | true / false | true / true |
| index / check ACK | true / true | false / false |

两项 `absent_before_create` 均为 true。GuardedDt columns 为 ID/INT、V/DT(6)；History 为 MigrationId/VARCHAR150、ProductVersion/VARCHAR32。其余字段、证据和数组次序以原 ledger 的逐字 SHA 为准，不重写 JSON。历史清理逆序先处理 History，它可能已 absent；只接受新连接实际观察，不从 ledger 或主失败推断存在。

## 3. 首选恢复实现：186 来源，仅一处行为改动

新建独立 ignored 根，例如 `.local/temporal-native/verification/TP07-D-owned-reader-recovery-v1/`，在其中分开 source、build、run；root approval 使用独立 `.local/temporal-native/root-approvals/TP07-D-owned-reader-recovery-v1/`。命名可由 root 确定，但禁止复用任何已有输出目录。

从接受的 BC replay 的精确 source186 复制源文件，排除 bin/obj/缓存，逐项验证原 source map。仅修改 `test/common/OwnedObjects.cs` 的 PK 查询作用域：

```csharp
{
    using var keys = CandidateGuard.Command(c,
        "SELECT CONSTRAINT_NAME FROM USER_CONSTRAINTS WHERE TABLE_NAME=:t AND CONSTRAINT_TYPE='P'");
    Add(keys, "t", entry.Table);
    using var r = keys.ExecuteReader();
    SafeEvidence.Require(r.Read() && r.GetString(0) == entry.PrimaryKey && !r.Read(),
        EvidenceCode.SchemaMismatch);
}
// 原 Index/Check 循环原样保留；此处 PK reader 和 command 已 Dispose。
```

此块保留原执行次序、完整 PK 单行断言、columns/type/scale 检查、child 查询、ACK 条件、marker 判定、exact DROP 和 fresh absence 检查。不得修改 W/EF、CandidateGuard、SafeEvidence、Literal Program、csproj、锁、timeout 或 exception policy。原来源 186 中 185 项字节相同，仅 OwnedObjects 的哈希不同；修复后新 SHA 由 High 实现时实际计算，设计不使用虚构预期值。

这是一份 maintenance fork，不是原 L0 source185 或原 BC source186。新 source manifest 必须明确 `task`、`source_kind=owned_reader_maintenance_fork`、原 BC source/lock/startup/build refs、原完整 source map、仅该路径的 before/after SHA、修复 diff ref、本设计 ref、新完整 source map，以及当前 W 和 product178 refs。新 lock/startup manifest 使用新实际来源哈希和实际二进制身份；不能沿用旧 BC task/hash 声称新字节已通过旧接受。

保留原 Literal Program 的入口与 `--cleanup` 分支。有限 adapter 在启动前锁定 `--cleanup`、`--profile shared`、唯一 ledger 副本、candidate/capabilities/build refs、repo、fresh output；禁止任意 argv/SQL/对象输入。原 Literal 的 finally 会再次调用 Cleanup，这是原 host 行为，不新增外层重试。

## 4. 恢复 adapter 与来源许可

High 在新目录实现小型离线准备/构建/运行 adapter，不能把原 `design-run.py` 的 gate 绕开后冒用旧 D startup。该 adapter 的规则如下：

1. 第一层标准库 preflight 验证 root 的 source approval、设计/adapter/修复 diff/source map/原 BC refs、完整 ledger SHA 与 exact2 字段、wrapper、W/EF inputs、SDK/framework/native/helper refs。验证失败在首个项目 helper import、SDK/process、secret wrapper 前结束。原 ledger只读；复制成同字节只读输入，两个位置均按原 `19f71f…` 验证，运行后再次核对。
2. source approval 描述精确一文件维护差异、完整恢复来源、W candidate、原 BC依赖图、预算和唯一 cleanup 操作。它不能把可改路径设为目录/前缀，也不能允许随调用提供的新 hash。外层运行许可单独绑定实际 build/startup/host refs、原 ledger ref 和新副本 ref、adapter SHA、fresh run root。
3. W 固定 `0.1.0-temporal.20261008144915`，nupkg `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。保留 candidate identity `e14eb1baca9a3c07b04f988dd3bcdb652048fd1a61211ad8c2cb117a9e0955a3` 和 caps `f10acfd86b5e7158eac0a0aa8cfb972418881837b4bfe63205245d82a08f54fa`。
4. EF 产品来源仍是接受的 transaction product178，所有项实际一致；保留基底175→178的原 patch/approval 链与历史 locks。新 EF DLL/MVID 从本次隔离 build 读取并绑定，不凭源码相同假定重建 DLL 必须与旧 BC 字节相同。runtime 中 CandidateGuard 按新 manifest 验证实际 W、EF、host/deps/MVID。
5. 使用 BC 原有限 feeds/13-package native closure，锁住原 package contentHash/ZIP bytes、native/cache pruning 规则和 SDK/framework。只复制已核验包与必要 build inputs，不复制 secrets。新 native proof 对自己的物理目录负责；路径改变时重建 proof，不把旧 proof 的旧路径当新事实。
6. 保持纯 metadata helper `managed_identity.py` SHA `ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac`，离线读取 PE metadata，不通过 Assembly.Load 获取身份。host runtime 的正常 assembly loading 仍由原 CandidateGuard 验证。

新 source/approval/manifest 之间采用有向依赖：repair/source manifest 不反向包含最后签发 approval 的 SHA；root approval 引用冻结 manifest，运行许可再引用 build 的真实制品。避免自指 hash。所有 refs 是实际路径与 SHA，检查物理路径 containment、逐字内容及数量，不能只比较声明字段。

## 5. Low 构建与恢复运行

High 冻结源码和 adapter 后，Astra 静态审查确认唯一 PK reader 作用域变更，原 185 项及全部判断 AST/语句保持。无需为此增加镜像实现的测试或重跑业务 migration 来制造失败。Low 的独立离线验证覆盖实际物理 map、ref 闭环、diff、exact2 和有限入口。

Low 获得单独 build 窗口后只执行三条 SDK 命令：SDK version query、原 Literal 项目的 locked restore、`build --no-restore`。沿用接受 SDK `10.0.401`、可写隔离 CLI_HOME、禁 first-time/telemetry、单节点、禁 node reuse/共享编译和 build servers。每条 SDK 上限 300 秒；不得扩大原预算、访问 DB、增加 CLI model 调用或重跑 unit suites。记录命令、退出码、elapsed、日志 SHA、自然退出/进程组终止状态。

build 完成后冻结新 source/lock/assets/native graph、实际 host/deps/runtimeconfig、EF/W DLL 和 startup manifest，再独立 readback。source 集合仍为 186，原两份 active lock 内容保持；二进制与 assets 是新根的真实产物。build 通过仅为恢复 host 的离线接受。

root 单独明确有限 runtime 恢复操作许可后，Low 使用原 TEST wrapper 启动该新 Literal host 一次：`--cleanup` + 原 ledger 的同字节只读副本 + 新 startup manifest。不得从项目默认开发入口启动，不读取 SA/DEV/TLS secret。host 总预算不超过 300 秒，若原授权 cleanup 预算更小则取更小者；不新增 25 分钟窗口、retry 或 timeout 扩张。

原 Cleanup 就是本次最小实际观察与恢复流程：每个 ledger 对象先新开且验证 TEST 身份的连接，查存在；存在才执行原结构/marker/ACK 判断，成功后仅 DROP 该 exact table；随后再新开连接确认 count=0。它逐对象检查后才对该对象删除，并非先对两表完成全量只读扫描的两阶段删除。此设计明确采用原行为，不增加另一套 catalog policy。无 comment 的 GuardedDt 已由原 `create_acknowledged && CreateAcknowledged && !CommentAcknowledged` 分支限定允许；不得人工补 marker、清 ACK 或编辑 ledger。

Low 最终核实主 cleanup 和 finally cleanup 的实际结果、exact2 唯一集合、fresh_table_count=0、每项 TEST 身份，以及自然退出和所有保护输入不变。某表原已 absent 是有效观察，不要求为了恢复而重建它。任一 policy/身份不符立即停止，不尝试前缀清理、跨 schema、SA、DROP USER/SCHEMA 或 migration retry。失败后保留 partial results 与 ledger；没有两项 absence 证据时继续标记 cleanup_pending。

## 6. 最小诊断与异常保留

首选恢复不再运行完整 guarded migration，也不故意以重叠 reader 复现异常。原 Literal 的 `failure` 与 `cleanup_error` 分开保存，足以保留恢复 host 自身的失败类别；主 D 原失败报告保持原样。成功取得 exact2 fresh absence 即可关闭本次遗留对象恢复，不把获取原始 stack 另设成功门槛。

如果恢复再次失败且三字段证据仍不足，下一步另行冻结只读小诊断，由 root 批准再运行。它限 exact2、当前 candidate/TEST 和原 ledger；顺序查询各 reader 必须 Dispose。只允许固定 exception type、code/number 和 stack frame 的 declaring-class/method，禁止 Message、ToString、原始 stack string、参数、SQL value、环境或凭据。反射调用如需解包 TargetInvocationException，也只处理固定方法并记录上述字段。

root 已选择186/only1方案：不新增 RecoveryEntry、StartupObject、全对象预检查阶段或额外诊断 gate。以后若需 D `original_error` observer 改进，作为另一个明确范围审查；本次恢复和下述 D utility 修订均不默认包含该改动。

## 7. 恢复后 D 专属 utility overlay

必须先接受 exact2 恢复，再调度 D 源码修订。W 和 EF产品178、原 L0/E source/build/runtime 及本次失败全部保留。后续新的 D25 和 preparation revision 只把 `test/common/OwnedObjects.cs` 的维护替换纳入 D 来源，不修改旧 L0/E manifest，也不要求 E 因未涉及其原验收源码的 D utility 修订而重跑。

建议最小来源形状为一份冻结 `owned-reader-maintenance-origin.json` 与 root source approval，前者包含：

- 固定 schema/task 与 `scope=D_test_utility_only`，本设计 ref、原 D sourcepack ref、原 L0 source/lock/template refs、transaction product178 ref。
- `replacement` 的精确 replay path `test/common/OwnedObjects.cs`、pack path `tools/DownstreamAcceptance/TP07/runtime-templates/common/OwnedObjects.cs`、before SHA `80c1d2…`、after 实际 SHA、修复 patch ref。
- 明确 `added_paths=[]`、`removed_paths=[]`、仅一个 modified path；W/EF product map 不变。root approval 在外层引用它及 High/Astra source evidence，不反向自指。

新的 D source manifests 的 `origin_refs` 从原固定六项增加唯一 `owned_reader_maintenance` approval ref，解析该 approval 后完整验证 origin manifest 和实际替换源。保留原完整十项 L0 lineage/artifact refs，`runtime_template_source_sha256` 继续标识原基底模板来源；maintenance 独立明确标识其唯一替换。不得用新的模板字节冒称仍等于原 template 全 map。

新 D collection 精确表达为“原 L0185，替换一个 utility 路径，再叠加原 D templates/locks/generated closure”。因无新增路径，workflow196、cleanup185、native198、cleanup-native186、generated6=205、generated12=211 不变；所有 source/lock/startup manifest 的 aggregate/refs 必须随实际 OwnedObjects 哈希变化。product178 完整 map 原样验证，不把 utility 变更伪装为 product178 变更或放弃全 map 检查。

`same_overlay` 原六项 + transaction 三项仍全相等，用于证明 D 与接受 L0/E 的同一产品来源。另在 D→D cleanup/main/generated/parent 之间要求相同 maintenance ref 和 after SHA；L0/E 模型依据不要求具备 D maintenance 字段。新的 D root model adapters 指向原接受 L0/E 证据与新 D startup，保留 actual profile/group/windows 和 producer 失败历史；18 字段 plan 不增加第19个 host/timeout/retry 字段。

## 8. 后续 D 允许修改范围与验收

High 后续任务应限定以下文件，实际最小差异由源码审查确认：

| 范围 | 允许改动 |
| --- | --- |
| tracked `runtime-templates/common/OwnedObjects.cs` | 与恢复版相同的唯一 PK using block；after SHA 一致 |
| tracked `design-cases.json` | 只更新该 immutable source hash；case/window/timeout/SQL/marker/oracle 规则不变 |
| tracked `design-validate.py` | 验证固定维护 origin/approval、完整物理源、唯一替换；D各阶段 maintenance 一致 |
| tracked `test_design_contracts.py` | 有意义的维护来源正/反例，不改原 guard/cleanup/runtime acceptance 断言 |
| 新 preparation revision 的 `prepare-d.py` | pre-import/process/SDK 验证维护 ref；复制原 L0 后只替换指定 utility；新 source/lock/startup refs |
| 新 preparation revision 的 `build-generated.py` | 必要时更新新 prep pin/维护 ref 传递；SDK前验证实际 parent，不能增加来源范围 |
| 新 preparation contract evidence/input/approval | 与实际新源码绑定，旧 preparation 五件套及测试结果保持不变 |

`transaction_origins` 仍物理验证原 L0185/lock186、原 template map、175→178/173same+2modified+3added、全部 transaction patch/approval refs。原“pack所有L0模板相等”规则只改成“除唯一已批准 OwnedObjects 替换外全部相等”，并独立验证该替换 before/after；禁止传一个伪装成旧模板的 pack map 来通过旧断言。`d_source_collection` 只在精确路径应用此映射，再做全 map 与阶段数量比较。startup/source/lock 均从同一新 map 导出，native pruning 与 protected refs 覆盖新维护来源。

新的 D25 仍严格25项；policy只替换单个 immutable digest，不能删除 OwnedObjects 的 pin 或扩大可变集合。原 design-run、runtime-validate（`d9ea3985…`）、pure16 AST/proof、managed identity、CLI、candidate guard、SQL guards、oracles、其余 C# 均保持原字节。若源码审查发现某个上述“必要时”修改并非必要，则保留原字节，不为版本号触碰。

最少反例覆盖：缺失/错误维护 approval，错误before/after，第二路径或 product/W 改动，阶段物理文件仍旧/不同修复，main-cleanup 或 generated-parent maintenance 不一致。反例应更新其外层 envelope 哈希让执行到具体维护 gate，并断言拒绝原因，避免只被早期 stale-file hash 拦截。原58/16测试继续覆盖既有语义；新增数量以实际独立执行为准，不提前宣称通过。

新 D preparation/source/contracts/native/BC/B0 均用新目录、真实 refs 和独立 Low 证据接受，不复用旧 binary 充当新 build。D runtime 在新一轮 root 许可后才执行；仍保持原 SDK/CLI/host/outer 上限及窗口规则，不借维护扩张预算。恢复 host 的成功仅证明 exact2 cleanup，不能替代修订 D guarded、scaffold、TLS 或 full C 的验收。

## 9. 当前交付与停止点

本轮唯一写入为本设计文档；不执行 SDK、导入项目模块、访问数据库或修改源码/旧证据。设计冻结后停止写入，SHA 由外部消息交付。后续最早任务是 High 的独立186恢复 fork 与有限 adapter，完成源码验收及 Low build 后，root 再签发一次 exact2 runtime 恢复许可。
