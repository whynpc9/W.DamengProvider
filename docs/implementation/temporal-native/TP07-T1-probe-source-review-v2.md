# TP07 T1 probe v2：完整17源复审

2026-10-10。结论：**changes_requested / v1_B01_B07_repairs_scoped_reviewed / V2-R01_policy_schema_blocked**。v1七项修复已逐项从实际源码核对；本轮仍发现一个确定的producer/validator政策schema不相容，不能给完整源码接受。没有运行SDK、tests、validator、host、wrapper或DB，没有导入项目模块、读取secret或修改源码/旧证据；仅新增本报告。Python仅做文件SHA、JSON读取及AST结构检查。

## 1. 完整冻结身份

实算15项producer aggregate为 `3d22e651aaf4802bd651ea3c377107a194b58e15b39797ff29ccfa474bf11b96`，validator/test两项aggregate为 `66a6097c0d8e7e9622b6cb9930a22f73b818cc102a105ca6a7538e9cdf5993cd`；完整17项aggregate为 **`a6405bed1600d5c1740bd4deaf3e77df077c39027d28b608860a6be6f0bbd1db`**。计算格式均为按probe相对路径排序的 `SHA256  path\n`。review结束前再算仍相同。

Low本轮已保存真正完整不可变copy，本审阅独立逐项核live与copy均匹配manifest17项；另核单独probe-source九项实际字节。以下路径以 `.local/temporal-native/verification/` 为根：

| 实际artifact | 实算SHA256 |
| --- | --- |
| TP07-T1-probe-tools-v2/source-manifest.json | `b0cf6450c2ac2c52b6977c6c43d0460fbd442383aea8c88542772de600aef3d2` |
| TP07-T1-probe-tools-v2/toolpack-manifest.json | `2028d26c32ec8de6521aee7fa53ad31f67b3454a501290cdfc412290cb6992a2` |
| TP07-T1-probe-tools-v2/host-source-manifest.json | `51774509024abaea9112b239d755f15d09f1624dee1b9d3337528ebbb1a4de60` |
| TP07-T1-probe-tools-v2-control/static-readback.json | `31b3f5d2096143094580895b532637cda63bc1289088337b199107d4b9444310` |

完整copy位于 `TP07-T1-probe-tools-v2/source/tools/DownstreamAcceptance/TP07/transaction-capability-probe/`；九源manifest的source_root指独立 `TP07-T1-probe-tools-v2/probe-source/`，其下只有host九项，符合独立validator对整个source_root精确九文件的合同。不能把完整17项根直接作为这个九源根。

| probe相对路径 | 实算SHA256 |
| --- | --- |
| README.md | `552e987892aa46ae78f0f831931da86c009d0d0eeee5c1063df4cbce0b1bb1be` |
| probe-policy.json | `1a11cbba01a1b9626151fd99676aadc88f66a8151bfd04fd4dab3713f7d33b35` |
| prepare.py | `7824e133d587122945f564dbea358c04a4fe16a3b39796b9662ff847e348dc5b` |
| run.py | `3273ab44027878ba2f216b98725a23257b51786ea161d33b14a6ef2f9bedd5ca` |
| validate.py | `4aed683f87eac85f19aa8bd4f5b1e9395dd5569a3b104bd93febfc0aa9f28027` |
| test_contracts.py | `c9f5400f8faaab48b60c08bfbeaf3b3c4444b62037bf15ee92233468932e2de1` |
| managed_identity.py | `ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac` |
| packed_oracle.py | `bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9` |
| host/TP07TransactionCapabilityProbe.csproj | `0f547a37aeb0072660ed02226b04714c02db4a037dcafd466ab8b4211d699846` |
| host/Program.cs | `86e00b9516783711c5d6c7c89f3be053f97bc873f601cbc8c5e93f04231a2b55` |
| host/ProbeContext.cs | `ae0739cd6b42c6d5f7d20831eedd24bb1099807ebee36f00e87b60ba9ce43858` |
| host/TransactionEvidence.cs | `745e1ff14de7bb372695084a56c168749eb06cb41370b34e82e5972350be6792` |
| host/ProbeIdentity.cs | `05d8991a0b18b296328c7925a83e33b56ad941b01f34b05236fbc36a8b386928` |
| host/OfflineContracts.cs | `f8fa83dee618038bf72eaf10ad634d06985302efdbbec056e7dc91f7ab2bc9bf` |
| host/common/OwnedObjects.cs | `80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946` |
| host/common/SafeEvidence.cs | `44c5e9b5643a3d33ba348ed8acc51780ccad2f94a5b43cc819f5897c9f9c11ef` |
| host/common/LiteralOracle.cs | `cb1696d1b305471e12c1a953630ee66454c4ab92d505d650e291e7170c44f492` |

五个helper副本与原来源逐字节相同。producer另外九项与本会话v1期间已读到的对应SHA相同；不据此补造v1完整source snapshot。v1缺少完整copy的历史限制保持。当前实际34个test方法仅由AST计数，未执行；offline28同样只是受审有限registry，并无运行通过声明。

## 2. V2-R01：policy新增11字段，validator仍拒绝它们（确定阻塞）

`validate.py:toolpack_contract`（约751–761行）仍对policy使用v1精确顶层集合，最后一项为core_packages。实际v2 policy在此基础上新增**11项**，所以无论source/native/runtime artifact多完整，`inputs_contract → toolpack_contract → shape(policy,...)`都会先抛 `policy_shape`。本结论来自实际JSON键与AST常量键集合比较，不是运行validator所得异常。

此问题仅需validator High修改 `validate.py` 与 `test_contracts.py`。不要删producer政策字段，不放开additionalProperties，不以忽略未知key或只校验部分字段通过。新增11项必须按以下实际值、数组顺序、整数/bool类型独立核验，并继续严格校验旧字段：

| 字段 | v2精确有限值 |
| --- | --- |
| required_input_keys | `[schema_version, task, workspace_root, original_repo, sdk, python, W_identity, capabilities, ef_overlay, toolpack, helper_origins, expected_closure, probe_source_manifest]` |
| probe_source_manifest | 下节完整七成员合同对象 |
| native_hash_domains | `{archive_sidecar: base64_SHA512_of_actual_full_ZIP_bytes, nuget_content_hash: actual_native_metadata_and_native_lock_equal_root_approved_origin_lock_record}` |
| termination_reap_budget_seconds | 整数2 |
| process_receipt_keys | `[argv, cwd, exit_code, elapsed_ms, timed_out, termination, execution_deadline, termination_reap_budget, termination_reap_ms, signal_number]` |
| termination_reasons | `[deadline_kill, signal_kill, interrupt_kill, exception_kill, launch_failed]` |
| termination_unknown_suffix | `_reap_unknown` |
| transaction_event_binding_fields | `[current_connection_object_id, connection_binding_source, binding_sequence]` |
| connection_binding_sources | `[callback_connection_argument, current_transaction_connection, registered_transaction_binding]` |
| binding_callbacks | `[TransactionStarted, TransactionStartedAsync, TransactionUsed, TransactionUsedAsync]` |
| offline_registry | `{rollback_cases:16, factory_cases:4, commit_cases:8, ordered_blocks:[rollback,factory_Create,commit], case_count:28, terminals:[rollback,commit], counters_include:[full_rollbacks,full_commits], live_commit_allowed:false}` |

`probe_source_manifest`政策对象恰含 task=`TP07-T1-probe-source-manifest`；required_keys=`[schema_version,task,source_root,source_hashes,source_count,source_aggregate_sha256,aggregate_format]`；source_count=9；source_paths为下列有序数组；aggregate_format=`sorted relative source paths: sha256 two spaces path newline`；source_root_scope=`workspace_verification_snapshot`。这里政策对象七成员与它所描述的独立artifact七键不是同一集合，不能混淆。

```text
host/OfflineContracts.cs
host/ProbeContext.cs
host/ProbeIdentity.cs
host/Program.cs
host/TP07TransactionCapabilityProbe.csproj
host/TransactionEvidence.cs
host/common/LiteralOracle.cs
host/common/OwnedObjects.cs
host/common/SafeEvidence.cs
```

修复需新增使用**实际当前policy及实际完整17-toolpack**的只读正例，确实进入toolpack_contract/inputs链；现有34个合成方法大多调用各下层合同，不会检出整个当前policy不可接受。另对11项逐项missing/extra/值/顺序/类型漂移及嵌套extra拒绝做负例。可抽取无I/O的独立policy_contract供变异，但实际文件/当前17项正例必须同时覆盖；不能只对手写缺少这些字段的fixture自证。完成后新freeze、独立源码复核，再由Low运行，不能把本报告当作已运行成功。

## 3. v1 B01–B07逐项修复核验

| v1问题 | v2实际代码与结论 |
| --- | --- |
| B01 缺probe source Ref | prepare.load_inputs必需13键；probe_source_manifest严格七键、count9、host精确路径、snapshot字节/aggregate/toolpack/expected_host_paths/lineage均比较；source从被引snapshot复制；runner与独立validator snapshot均加入manifest及九源实际文件。Low已生成独立九源manifest。**接缝实现已关闭，完整可用性仍被R01挡住。** |
| B02 contentHash/ZIP混淆 | prepare.native分别核sidecar→fullZIP SHA512和metadata→actual lock→root origin contentHash；fresh archive对feed_inventory/实际feed SHA256；ZIP唯一nuspec ID/version及安装nuspec字节相同。validator native_hash_domains_contract/archive_origin_contract独立重算相同有限关系，不再要求两个摘要域相等。**原错误已关闭；新native运行仍pending。** |
| B03 Core13与runtime deps | validator CORE_PACKAGES现含Configuration.Abstractions，不含DiagnosticSource，与policy/原受审Core13一致；runtime_package_contract固定Core13减EF.Analyzers再加W。**集合修复关闭；新host实际deps仍待构建回读。** |
| B04 TLS变量 | internal_host由ordinal/profile仅选择generic或TLS专用wrapper变量，要求非空，在子进程内存映射给guard的generic变量；无跨profile fallback，同路径覆盖main/cleanup。**入口修复关闭，未运行wrapper。** |
| B05 failed batch release | OfflineContracts实际counter要求supports时release1，失败真实callback序列为create→rollback-to→release；批次起止sequence在它们之间；独立offline_savepoint_contract同步。**错误期望关闭，真实DI/stub测试未跑。** |
| B06 terminal Connection=null | collector按真实DbTransaction引用建Started/Used binding，核callback connection参数/当前Connection、GUID、context、IsAsync；active关联保留真实current，只有成功RolledBack/Committed当前null才用既有binding；缺binding/错对象/错误callback标captured_unknown。validator独立重建同绑定；stub完成后Connection=null，8个commit正例加上原16 rollback/4factory成28。**源码接缝关闭；实际W终结回调与编译验证仍pending。** |
| B07 cancellation子进程 | process安装并恢复SIGINT/SIGTERM handler；Popen期间延迟signal避免未赋值child；单一180秒deadline；timeout/取消/异常进入finally killpg+reap，reap预算2秒；不确认退出追加_reap_unknown；十字段保存实际结果。runner持久化main/cleanup/validator process部分收据，任何非自然成功停止后例，对可信ledger尝试独立cleanup。**原遗留子进程处理缺口已有有限实现；无DB控制进程测试仍pending。** |

R01不表示这些源码分支没写，也不允许借它跳过全链artifact检查。所有“关闭”仅指本次静态审查对应缺陷实现，不是unit/offline/native/runtime接受。

## 4. 真实DI、观测链与停止边界

正式ProbeContext只替换model cache key，factory/adapter走产品DI；创建前实际服务身份检查，显式ReadCommitted Begin，两个actual capability读取及AutoSavepointsEnabled=true保持。事务ID、底层对象、connection/context object IDs、DbCommand.Transaction实际来源、inside数据库AsNoTracking结果及新连接fresh结果没有换成期望布尔值。四例固定D/Z量子1/10、DTO offset/local/UTC、fullRollback、前后schema/N/rowcount和raw/typed/FF9保留。

observer注册仍从同context的CoreOptionsExtension.Interceptors取实际相同实例，起止sequence/捕获状态有独立校验。新增TransactionUsed/Async仅为offline external路径建立真实binding；live仍要求Started。`events_binding_contract`依据已出现事件建立映射，拒未知binding、提前null、错GUID/connection/context/API或伪造binding序号。terminal null字段没有被旧关联值覆盖；关联字段明确标来源。shared回调create/release与业务command顺序、rollback-to0；TLS空savepoint事件必须有同事务begin/业务/fullRollback及本轮两个shared正控制与offline引用完整链。不是以producer `observed`自证零。

fixed offline28=16（sync/async×supports×成功/失败×own/external rollback）+4 factory ownership+8（sync/async×supports×own/external commit）。新增commit只在stub离线入口；live无commit许可。独立validator检查有限registry及counter、绑定、actualnull、actual factory来源和callback；synthetic34与compiled offline28是不同证据，不能相加或互相替代。

process自然成功合同严格要求exit0、无termination/signal、reap_ms0、elapsed<=180000；取消/timeout与_reap_unknown均不得通过。2秒只是SIGKILL后的有限reap，不给业务延时，不重试，不延续后例。取消期间可信ledger清理使用独立180秒且有自己收据；清理失败/缺ledger/unknown保留pending。某次producer检查虽返回completed也仍需独立process合同和root最终判断，不能仅用process_succeeded布尔值接受。

native输入为两个明确project的concrete lock_dependencies及assets_frameworks，不以任意数量代替集合；实际locks/asset package IDs/dgspec项目路径被读取并绑定，source187/locked188路径集合精确，W仍package且EF provider为新ProjectReference。该schema已能表达预期Core13角色，不等于新SDK已产生符合它的lock；root仍须对真实native项目引用/框架/pruning/包版本/边及归档来源逐项回读，新差异停审，不补写预期来凑数。

## 5. 编译及执行限制

本轮检查了实际新host名与C#接口接缝，未发现需要伪装旧程序集或放松旧ROOT的理由；五helper旧字节保持。没有编译，所以不宣称当前C#能够通过TreatWarningsAsErrors。额外静态待核风险：host/OfflineContracts中的ConnectionString、CommandText、ParameterName、SourceColumn setter未标AllowNull，而批准EF源Directory.Build.props为Nullable=enable/TreatWarningsAsErrors=true；批准产品unit已有对应AllowNull标注。TransactionEvidence的ScalarExecuted/Async也应与固定EF reference的object/object?签名精确核对。此项已通知root，属于**待固定reference元数据/Low编译核实的风险，不是本轮观察到的编译报错**；不能通过禁用nullable或抑制编译警告过门。

root产品source approval实际SHA `ac8ff44fb570050d478190b8c1908f55cc0fbd5cd2d7a78bc1051d230dff6b4b`只授权178源的静态来源；产品unit/native专门门、probe当前policy修复后的完整source接受、Python34及compiled28、native/build/DLL/MVID/deps/ready均尚未完成。host九源manifest现在已有Low真实artifact，但inputs/runtime approval尚不能用它冒称离线或实库接受。

下一步仅由validator High在validate/test两文件关闭R01并保留v2完整copy；修后新完整freeze，Astra复核政策全链与任何额外代码差异，Low再按root安排执行被授权门。当前结论是scope rejected for execution handoff，SDK/tests/DB/wrapper不因本报告开放，L0/E338/D及完整C仍pending。
