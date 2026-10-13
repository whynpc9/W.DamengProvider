# TP07 T1 probe v1：源码与身份接缝审阅

2026-10-10。结论：**changes_requested / source_manifest_gap_design_approved / new_full_freeze_rereview_required**。本轮只读审阅初始17源的producer、独立validator、合成合同及实际helper接口；未运行SDK、tests、validator、host、wrapper或DB，未导入项目模块，未读secret，未改源码、status、ownership或旧证据。仅新增本报告。

初读时15项producer实际aggregate与交接 `c16ad03241f331d5308c7e125486e028315c1bc485b68eb4d608f012a5e30bc1` 相等，格式为按probe目录相对路径排序的 `SHA256  path\n`。独立validator/test两个文件另行交付，AST静态数为17个test方法；这不表示任何合同运行通过。下述问题发现并逐项通知root后，High已开始修订：收尾读到producer15 aggregate变成 `1ee063cd3ff66df68462d06eed126b36a5733c984788a708b092fef1097a4dca`，全17项为 `7481193616a7e972efd036e84fc159b687c9bc950b750d2ef4c82e3729b77137`，test方法变成22。**这两项是修订中观察，不是本报告接受的冻结版本。** 不混合起始v1与部分新源作接受；High修完后需新完整freeze与复核。

证据限制：本review没有创建原v1完整source copy，也未在修订前保存原17项逐文件SHA表；原15 aggregate是在当时实际文件上实算，不是immutable executed artifact。原validator/test两个初版文件未取得独立冻结manifest/hash，不能补造其旧SHA。以下五项能直接确认原受审helper字节，其余源仅按上述当时读取的代码和aggregate描述，后续新冻结必须补齐全部17项实际SHA：

| 原helper相对probe路径 | 实算SHA256（与原来源相同） |
| --- | --- |
| host/common/OwnedObjects.cs | `80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946` |
| host/common/SafeEvidence.cs | `44c5e9b5643a3d33ba348ed8acc51780ccad2f94a5b43cc819f5897c9f9c11ef` |
| host/common/LiteralOracle.cs | `cb1696d1b305471e12c1a953630ee66454c4ab92d505d650e291e7170c44f492` |
| managed_identity.py | `ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac` |
| packed_oracle.py | `bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9` |

| 阻塞 | 精确初版接缝 | 定点所有者范围 |
| --- | --- | --- |
| B01 | inputs缺probe source Ref及snapshot/lineage联结 | producer prepare/run/policy/README；validator validate/test |
| B02 | prepare.native及validator.native_contract将ZIP SHA512当contentHash；feed/cache/nuspec来源缺联结 | producer prepare/policy/README；validator validate/test |
| B03 | validator CORE_PACKAGES错一项；native与runtime deps集合混同 | validator validate/test；producer只同步必要schema说明 |
| B04 | run.internal_host TLS误读generic变量 | producer run；validator合同对应验证 |
| B05 | OfflineContracts及offline_contract对失败release期望0 | producer OfflineContracts；validator validate/test |
| B06 | TransactionEvidence终结时当前Connection为空，却强制非空 | producer TransactionEvidence/OfflineContracts；validator validate/test |
| B07 | prepare.process取消/异常可遗留子进程组 | producer prepare/run；validator validate/test |

## 1. 产品接受与probe接受的边界

产品新source确为178项，实读 `TP07-T1-product-v1-sourcefreeze/transaction-capability-manifest.json` SHA `932f8f41a2d170c74d425c6add913ac1f3a5141fab49835e0311f55353183980`；[产品源码审阅](TP07-T1-product-source-review-v1.md) SHA `09fa721ead3ced44e95168d2dd8c5a422df509489bf6b30bb8c87678458268af`；Low source freeze SHA `30c451f9a3e08898a559c6943f0097f3966eba16a0d252da5231f793af57dc13`、static readback SHA `375f51429285373aff7672fcfd6ce5420cddd30654946f0a645e230c7c952acf`。

上述是产品静态来源与差异接受，产品unit/native/build仍需专门准备和独立Low门。future七键 `TP07-T1-overlay-source-approval`（schema_version/task/status/manifest/exact_paths/exact_count/scope）只签 `source_approved`；不能充当unit、probe offline、运行接受或SDK/DB授权。probe的187源、加唯一host lock后的188路径闭包需要真实新制品验证，不复用旧175/182/191/193计数。

## 2. B01：probe source manifest没有可追溯Ref（阻塞；修复设计已获root批准）

初始 `prepare.py:load_inputs`精确12键，只有lineage的 `probe_source_manifest_sha256`，没有artifact路径；`source()`直接从HERE/host复制9源。runner `input_snapshot` 和validator `inputs_contract/approval_contract/snapshot_contract`均不能定位那个SHA对应的独立manifest。只验证SHA字符串形状或由root另存一个未被引用文件，不能闭合输入链。

批准的最小修复为必需新增 `inputs.probe_source_manifest:Ref`，inputs精确13键，旧12键、null、optional fallback均拒绝。新的独立JSON精确七键：

| 键 | 有限合同 |
| --- | --- |
| schema_version | 整数1，拒bool |
| task | `TP07-T1-probe-source-manifest` |
| source_root | 受控新冻结snapshot中probe根的canonical绝对路径；必须位于批准workspace的verification快照，拒旧host root、工作树live root冒充snapshot、symlink及越界 |
| source_hashes | 恰好冻结17源中的9个 `host/` 相对路径→小写SHA256；不含bin/obj，不接受任意第10项 |
| source_count | 整数9 |
| source_aggregate_sha256 | 对source_hashes按完整host/相对路径排序，以 `digest + "  " + path + "\n"` UTF-8串联实算SHA256 |
| aggregate_format | 固定 `sorted relative source paths: sha256 two spaces path newline` |

Low从**新完整17项冻结snapshot**实际生成该artifact，root签inputs的path/SHA。prepare与独立validator分别核七键、9路径、aggregate、snapshot字节；再核toolpack中相同9项hash，映射 `host/x → test/TP07TransactionCapabilityProbe/x` 与expected_closure.host_paths/source187逐项一致。所有source/startup lineage的probe_source_manifest_sha256必须恰等Ref.sha256；不能接受与toolpack相同内容却来源指向旧ROOT的manifest。复制优先从被引用snapshot读取，或对live/toolpack与snapshot逐项相等后复制；来源证据必须仍绑定该snapshot。

runner前后snapshot及独立validator期待snapshot必须加入manifest文件和九个snapshot源的实际绝对path/SHA，不能只加入manifest哈希。root批准的最小文件分工：producer High仅 `prepare.py/run.py/probe-policy.json/README.md`；validator High仅 `validate.py/test_contracts.py`。加missing Ref、错SHA、错lineage、错task/count/path、旧root、snapshot drift、live/toolpack不一致、重复key与extra-key负例及一个完整正例。此次六文件接缝修复不授权审阅者自行改代码，也不自动关闭下述其它问题。

## 3. 其余确定阻塞

以下定位指本轮**初始v1**；修复可能已在进行，仍须新freeze实读关闭。

### B02：native再次混淆ZIP与NuGet摘要；包来源链不完整

`prepare.py:158`要求 `sidecar == full_archive_SHA512 == row.contentHash`；独立validator `native_contract`亦要求 `content_hash == calculated`。实际已接受D native cache中的Microsoft.EntityFrameworkCore 10.0.12，本轮文件只读实算为metadata.contentHash==实际lock.contentHash、sidecar==完整ZIP SHA512，而lock.contentHash!=完整ZIP SHA512。此前 [D sidecar/native设计](TP07-D-native-graph-triage.md) 已保留这一区别，因此v1在真实native输出上会误拒绝，不能要求新NuGet输出配合错误合同。

修复需分别核：metadata.contentHash对批准原record及实际lock；sidecar对完整ZIP SHA512；fresh cache/archive对批准feed归档SHA256与nuspec原字节；actual locks/assets及各自身dgspec引用保持角色/路径/FrameworkReference/pruning/边精确。v1有feed_inventory自检，但fresh cache archive没有明确逐项联结批准feed文件，且未保存nuspec证明；去掉错误相等后不能留下这些来源空档。新native schema若增加有限archive/nuspec/source Ref，producer与validator同步，保留实际旧失败。任何包新增、版本/边/prune变化停审，不通用忽略差异。

### B03：独立validator的Core13集合错误，并将build-only包当runtime依赖

v1 policy的Core13与已接受provider lock（实算SHA `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`）集合完全一致。validator的CORE_PACKAGES却漏 `Microsoft.Extensions.Configuration.Abstractions`、误增 `System.Diagnostics.DiagnosticSource`；`toolpack_contract`因此在任何case验收前就拒绝正确policy。

同一validator还要求host `.deps.json` 的package集合等于Core13+W，包含build-only `Microsoft.EntityFrameworkCore.Analyzers`。已接受同类L0实际deps SHA `f3ec91299339bc9a4e751f37b112ba5794a5a0012d80648afe4aa71400a96d0a` 的package集合恰为Core13减Analyzers再加W，共13；没有其它差异。应固定分开restore/assets Core13+W与runtime Core12+W合同，并由新probe真实锁/assets/deps验证，不能泛化成任意包集合。建议validator High在validate/test范围修复并增加与实际受审图一致的合同；producer不为迁就错误validator更换依赖。

### B04：TLS wrapper变量没有归一化，TLS main/cleanup均会停在入口

v1 `run.py:internal_host`只要求并读取 `DAMENG_TEST_CONNECTION_STRING`；实际原TLS wrapper SHA `872d722808a7f53415505e2536438d1b644793822b2abeac3cb5b662ac94d706`只export `DAMENG_TLS_TEST_CONNECTION_STRING`，不export generic变量。本轮只读核本worktree与原repo脚本内容/哈希相同，没有运行wrapper或读secret。

producer High应由已批准case profile选择唯一wrapper变量，在internal-host内存环境映射到新guard所需的 `DAMENG_TEST_CONNECTION_STRING`；shared取generic，TLS取TLS专用，禁止fallback到另一环境遗留变量。main与cleanup同规则，不打印值、不读secret文件、不改wrapper。加两个profile及错变量拒绝的受控环境合同。

### B05：offline失败批次错误要求release=0

`host/OfflineContracts.cs`和validator `offline_contract`均以 `supports && !fail`决定期望release=1。已接受产品T1单位源码明确固定EF BatchExecutor在支持保存点、失败批次且连接仍Open时序列为 `create → batch → rollback-to → release`；产品源码审阅也明确保留该finally release。probe stub保持Open，因此其失败路径要求release0与正式EF控制流冲突。

producer High定点修OfflineContracts；validator High同步counter和callback期望，增加真实形状synthetic负例。不能令stub在业务异常后假关连接、跳过release事件、捕获后改成功或改产品EF行为来适配错误期望。四例live正常成功路径仍为shared create/release与rollback-to0、TLS全零，不增加第五live例。

### B06：终结事务的真实Connection=null未建模

`TransactionEvidence.Record`一律以 `ObjectId(tx.Connection)`写connection_object_id；validator对所有callback要求该ID>0并与active transaction连接相同。固定候选W源码 `src/W.DamengProvider/PublicApi/DmTransaction.cs` SHA `d654434f6b7811450bffd089866da7f27ed1f2cdaee6751d457bef8ecc5333bf` 本轮与TP06 package source manifest逐字相等：Valid仅Outcome.Active；do_DbConnection在!Valid时返回null；成功Rollback进入RolledBack并Clear。因此真实 `TransactionRolledBack` callback读当前tx.Connection将为null，正常回滚会被当前合同拒绝；seed若发生隐式commit也有相同终结状态问题。离线StubTransaction一直返回connection，掩盖了这个接缝。

建议producer High在真实TransactionStarted回调建立按DbTransaction**引用对象**键的不可变begin binding，保存真实connection对象、TransactionId/ConnectionId/context/API；active callback仍核当前Connection引用一致。terminal记录当前Connection的真实null和明确的关联来源，另使用先前真实begin binding关联同事务，而不是把当前Connection伪填为旧连接。validator同时验证actual null、已存在begin binding、同底层对象/GUID/context、开始与结束顺序及主动fullRollback返回；未知binding/错GUID/跨tx不得补救。offline stub应模拟完成后Connection=null，UseTransaction场景需真实TransactionUsed回调建立自身binding；对sync/async终结、null提前出现、别的事务复用binding和错误connected值补合同。旧W和common不修改。

有限字段方案：保留event的connection_object_id作为**关联连接**ID，新增 `current_connection_object_id`（实际getter结果，可null）、`connection_binding_source`（仅callback_connection_argument/current_transaction_connection/registered_transaction_binding）、`binding_sequence`（指向真实Started或Used callback序号）。Started/Used以callback实际connection参数建立绑定，核tx.Connection当时同引用；active保存点和RollingBack必须current ID==关联ID，source为current_transaction_connection。成功RolledBack/Committed可记录current=null，source为registered_transaction_binding且binding_sequence严格早于当前事件；若实际仍非空则核同引用并用current来源。无已观察binding不得关联；TransactionFailed、提前null、同对象不同GUID/connection/context均不接受。补TransactionUsed/Async覆盖离线external路径，扩展有限callback集合，不把它冒充Started。业务显式Begin仍只接受Started链。collector/validator的JSON字段同步增加且不做旧字段optional兼容，synthetic必须模拟真实终结null。

### B07：进程取消/父异常没有收掉独立子进程组

v1 `prepare.process`以start_new_session启动子进程，只处理TimeoutExpired；没有BaseException/finally回收仍活跃进程，也未提供SIGTERM安全收尾。若Low中断、父进程异常或取消发生于wait，独立子进程组可继续SDK或DB动作，runner不会进入本例cleanup/receipt闭环。这不影响已定义180秒timeout分支，但不满足首失败/取消后停止后续活动的边界。

producer High在现有prepare/run范围增加有限取消/信号处理、finally group termination/reap、安全partial process receipt和停止后例逻辑；本例可信ledger仍走独立cleanup或明确cleanup_pending。不得复用超时预算无限等待、自动重试或把未确认child退出说成自然成功。Low用无DB短子进程验证timeout、interrupt、SIGTERM/父异常与无遗留child，不运行真实矩阵来验证这一控制逻辑。

有限代码接缝：process初始化child=None，将Popen与wait包入try/except/finally；timeout/KeyboardInterrupt/受控SIGTERM或父异常时，若child仍活跃立即killpg(child.pid,SIGKILL)，随后reap，最终确认poll非None。prepare/run顶层信号handler只转入这个受控取消路径，不能直接os.exit跳过finally。保留原process六字段argv/cwd/exit_code/elapsed_ms/timed_out/termination；失败可exit_code=null并使用有限termination枚举spawn_failed/interrupted/signal_SIGTERM/parent_error/deadline_kill，不序列化外部异常Message。已有自然成功合同仍只接受exit0、无timeout/termination且elapsed<=180000。runner在本例main/cleanup调用边界保存partial收据、设置first_failure，尝试已有可信ledger的独立cleanup或标pending；取消标志禁止下一例。回收等待需有限并单独标失败状态，不把回收耗时变成额外业务执行/重试预算。

## 4. 已核实的正确方向及限制

初始新host明确名为TP07TransactionCapabilityProbe；ProbeIdentity没有伪装旧E/L0 assembly或改旧ROOT，按新startup核source/native Ref、host/provider实际DLL/MVID/deps及W package来源，正式服务类型来自新provider assembly。ProbeContext只替换nonce model-cache key，不替换事务factory；创建前实际DI factory验证，Begin明确ReadCommitted，两次实际EF/底层SupportsSavepoints读取，AutoSavepointsEnabled保持true。

Program固定四例/每例一个Basic表，真实D/Z/N seed与update SaveChanges、inside AsNoTracking query、显式fullRollback、不同fresh connection、前后rowcount/schema、D/Z独立raw/typed/FF9；不是跟踪实体值充当数据库读。OwnedObjects用Basic外部CREATE ACK链而非Literal专用Create。N只允许两种安全业务字符串。catalog/raw/cleanup同步辅助I/O单独标明，没有把它冒称全部async。

TransactionEvidence真实读取CoreOptionsExtension.Interceptors且ReferenceEquals实例只注册一次；object ID表使用ReferenceEqualityComparer。validator将observer显式context registration、起止sequence、实际callback名称/IsAsync、TransactionId/ConnectionId、业务command.Transaction与connection绑在一起，并将seed事件和显式事务区分。两个shared live正控制经execution引用递归重新验证后才可支持TLS空savepoint集合；没有只信producer captured_complete。B06不改变这个正确方向，但必须修正终结属性模型。

独立validator使用本地固定packed decoder和整数FF9/ticks重算；检查cap双读、所有stage、typed D/Z/UTC/offset、raw descriptor、fresh schema/值/独立cleanup，以及immutable execution-input→validation→final case-receipt的非循环引用。main/cleanup/validator180秒与固定prefix failstop、候选/完整C不接受旗存在。B01/B02/B03/B05等artifact或期望错误导致该方向目前仍不可执行接受。

五份helper副本本轮实读均与受审来源逐字一致：OwnedObjects `80c1d2aa…`、SafeEvidence `44c5e9b5…`、LiteralOracle `cb1696d1…`、managed_identity `ca7bb8eb…`、packed_oracle `bc9ed2f5…`。这只是来源核对，不是helper运行验证。C#编译/API/实际callback次序仍需独立Low，不能凭读源码说已通过。

## 5. 独立合同覆盖与下一门

初始17个合成test主要覆盖case结构/值/事件/cleanup、strict JSON/ref、process、matrix prefix、finite187/188 closure，且不导入producer。但它们没有给完整inputs→toolpack→native→build→offline→ready→execution Ref图一个可执行正例；closure fixture甚至用空native graph作形状检查。因此它们不能发现B03的policy实际集合不一致，也不能证明B01独立artifact可定位或B02 native双摘要语义。修复后的新增方法数量不是完成标准。

除各B项针对性负例，应补实际受审包图/归档语义的只读fixture、完整独立refgraph正例及每引用缺失/错SHA/错root/漂移负例。明确synthetic/source-only标签，不把fixture原生restore说成真实native；单位运行必须由Low另窗。probe自己的真DI/stub20场是另一组执行证据，不能拿合成JSON四正例代替。

产品source七键approval、probe source-manifest和所有完整17源码先新freeze；root核文件所有权、Astra重审所有修复及未触及源，随后安排产品unit/native专门门、probe source/native/build/offline/readiness。readiness producer状态不替代独立全部引用/实际offline回读；root只有这些门完成后才签runtime approval。四例runtime完成仍需Astra/root独立接受，新overlay L0/E338/D重新绑定照原DAG执行。当前SDK、tests、DB、wrapper均未因此获准。
