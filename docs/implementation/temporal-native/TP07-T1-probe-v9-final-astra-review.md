# TP07 T1 probe v9 最终关键审阅及 v10 有限修复设计

2026-10-11，Astra High。结论：**actual_first_validator_failure_preserved；T1 runtime 未接受**。v9实际71、source/native/build、compiled28/337及完整readiness前置门成立；shared-sync主过程全部12步完成、独立清理成功，但validator错误拒绝CREATE的实际整数返回值−1。完整静态对照未发现第二处真实报告/合同不匹配。后三场未执行，不能把首场主过程成功转换为runtime4接受；L0/E/D继续pending，不宣称TP07、TP08或PR整体完成。

本轮只读冻结源码、完整原合同、JSON/hash/log和实际制品，以标准库完成静态引用/数值/字节对照；未导入项目模块、执行validator/tests/SDK/DB或追加诊断。仅新增本文，`.local`只读，旧证据不改。先审已冻结前置链，收到Low最终首错freeze后才审runtime结果。

## v9 前置链与修复作用

源码review `b1bc363a8e14d14d7ec00fe1f5e0c84a1da3ee75d6e642f3acb614675a458912`、High freeze `bd1c82eb198e0256ac3456586470d7d0595c354fb531cba9ce381ca82f4c0008` 对应实际17 aggregate `af3160015d58c0009ecb8c35faa16221a84caf54acfa0058eb45a331856c19bf`，本轮审阅时逐文件重新核实。显式identity transaction修复保持：W事务类型及同connection先核，两个原身份SELECT绑定同一underlying；无事务调用、TEST身份、物理profile和TLS策略未放宽。本次capabilities及后续主过程真实完成，消除了v8所见辅助身份查询漏绑定路径。

下列为实际原件SHA256；阶段目录前缀 `.local/temporal-native/verification/TP07-T1-probe-v9-`，inputs位于root-approvals同名目录。

| 制品 | SHA256 |
| --- | --- |
| contracts/verification-summary.json | `2b57ca8df88699acfc9e13fc5538474a44236f1bff1d1c08745ab4a66817f719` |
| contracts/contracts.log | `c89cf43b686617e4f4e9e01ceca398ae6f28641c8ca0d181b16cb73f749797d3` |
| inputs/inputs.json | `e32edb86cfd6aa6b98546129ea820cd83abe2768646e27fc13dbf1aaccc3c9f0` |
| source/source.json | `217b2df5569412a7e5b6a531190fb799437fa5cabf4f96ee69814a490474e8fd` |
| native/native.json | `1378566f81623f1104e620f068f1516c57df38822cb05095541ad94018e3019a` |
| build/build.json | `34b1c53cc19bcec128011e936e4a060f959d021dd92980f46e5ed2b5cc3b3a5b` |
| build/startup.json | `aca0a286a6aa0f1e9cfc34764a82d35ee6a80b11777a4bbb39dba43236b68322` |
| build-control/independent-readback.json | `73de52737bb42ee6d7a6df9dddd12d4fb6eedbab55a2bcf8239f2db995bc178f` |
| offline/offline.json | `0c6f5fe21328298244bad3051805b288a8803f642c78d843db134abe0a765ab9` |
| offline/host-offline.json | `efc2560459f7eba2d663c6b3acb799fd8c1d5884803a95c58042e5fe0411ee1f` |
| readiness/readiness.json | `13a0c4dc1c1689f32607f81e9a78249d6a5c5e137dc6d2f41aa6133e63439105` |
| readiness-control/independent-readback.json | `49a06a2e4c818f8d5eaa71d44cfb773572155b04b1aad955f01e15a247808103` |

actual71日志逐项名与当前AST精确集合一致，71/0fail/0error/0skip。offline阶段另有原Python进程自然0，但DEVNULL未留下该阶段方法名日志，不能重标早先日志。compiled28/337、完整native及真实readiness Ref下的完整offline合同由Low实际通过。

本轮独立重算批准八Ref及跨stage绑定、source187、binary26、八immutable raw、四live raw和两live locks；两header严格2/1，完整JSON只新增五处strict true，批准完整lockgraph/pruning/frameworks一致。六SDK自然0，各前置阶段保护map前后相等。process十字段、180+2、CLI13及snapshot覆盖边界未变；不冒称child PID/PGID外部观测或run.snapshot覆盖live dgspec。

## 首错冻结与安全边界

runtime approval SHA `2599cd2b86d4d89609ab0b131b9f05456f4deace5a243692613a5bb314670094` 精确绑定v9新链、固定四场、main/cleanup/validator各180秒、原仓库两个TEST wrappers和新output root；所有引用/原wrapper字节核实。首错停止、既定独立清理与校验顺序保持。

| runtime制品 | SHA256 |
| --- | --- |
| matrix.json | `810e82497cd6d0a474042916f2c8f4df5c322c3f99a01e191dab657ccf7bae14` |
| 00-shared-sync/case-receipt.json | `a2e56fc7487c4994f3463654c94d03a6d27535796680f9b7a84db2dd6b28c051` |
| 00-shared-sync/validation.json | `e897ba365eb9b3f17e96b75cf3a2e76057e4295c74a6e7424d1e36a93517a1f8` |
| 00-shared-sync/main/report.json | `20414e0d1176ceb73cce6305395cb012f507ac0947947c8de5a46c81e922589f` |
| 00-shared-sync/cleanup/report.json | `d6ada506b54a8fa2423ffd7ea9161ea21ab51aaa1eecae90659bac506e4762fc` |
| 00-shared-sync/main/ownership.json | `2c21e9950a56b94e2bd9e005f5fbad5f4ece9e65a25523b251bec414c2a3b069` |

runtime-control的failed readback为 `efbb2cb7f8d3e9af6a1b0b1f3a83cd07055fafad49bd213bb4ae4e52d7289f0d`，artifacts为 `b19ad84d4e11cece36902e6440c4f8813d885d05bca5aecd3576429d91205e4b`，safe summary为 `ba8bb40ddd6aaf4f3ec43a6f1ed299534aea04e5097d77d132d394fd605348b7`，launcher receipt为 `714b5cefee037a69e6adb900c068085326a4758a1f970666edc987fa9767e6b9`。

main实际35834ms/exit0，cleanup6069ms/exit0，validator398ms/exit1；均自然退出，无timeout/signal/reap。validator仅报 `command_affected_rows`。main/finally和独立cleanup/finally均有实际TEST身份、同ledger对象fresh_absent/count0；cleanup_pending=false、input_drift=false。34制品hash逐项重算，12文件失败前缀与immutable副本逐字相同，20072保护map前后相同、case257项snapshot相等，后三目录不存在。Low核外层组消失及匹配runtime进程不存在；未追加查询/重试/诊断，未声称单个child PID/PGID可得。

## 全部后续合同静态对照

原 `events_commands_contract` 对所有nonquery统一要求整数>=0，而实际唯一nonquery为序号9、stage=create、API=NonQueryExecuting、completed=true/error=null、result严格int −1。collector的NonQueryExecuted原样记录驱动返回值；驱动ExecuteNonQueryComplete返回reader.RecordsAffected，负值本身不等于执行失败。实际CREATE的返回成功、服务端目录、ledger ACK及SQL SHA已经另行绑定，不能让错误数值门推翻这些真实观察，也不能删除成功/身份/来源检查。

已完整读取case_contract及其所有相关helper和实际report/cleanup/ledger/startup/receipt；除了上述门，没有发现第二处必然结构差异。逐项核对结果：

- 12步API/顺序/结果完整，正式factory与observer身份/注册对应实际新host/provider；两次capability读保持相同EF14/底层12/连接13/context11及GUID，均ReadCommitted、supports_savepoints=true。
- 7个同事务callback精确为Begin、Create前后、Release前后及完整Rollback前后。顺序24→35/36→业务37/38→39/40→47/48成立；terminal当前connection=null使用原已注册begin绑定。没有commit、rollback-to-savepoint或失败callback。
- 9个command的shape、类型/API、完成/error、参数类型、唯一sequence/GUID及stage归属相符。除CREATE int−1外全部reader result=null；三个事务业务命令绑定同context/connection/transaction/GUID；seed参数为原ticks，事务写参数为原ticks+1。CREATE恰一条、正式pre-DDL context、SQL SHA与ledger创建ACK一致。
- fresh_before/after各两条真实query/count，context/connection分别7/9与18/20，异于事务11/13；inside为ticks+1/rollback，after恢复原ticks/first及row_count1。目录四列、scale7和9/11字节宽度、PK/comment/owned nonce一致。
- 四份D/Z raw hash与宽度/metadata相符，独立按固定bit位置作整数展开得到2026-07-23 14:15:16.123456000，Z偏移330；与FF9、typed、本地/UTC ticks逐项相等。所有raw来自temporal getter前的普通结果缓存；非BDTA。
- 三次清理观察绑定相同可信ledger及精确对象，均fresh0；runtime/startup/W/provider/hash身份相符。receipt真实Ref、原ticket与冻结input快照闭合。没有把后三场或TLS零保存点当作已观测。

上述是完整静态对应，不是执行修订validator的通过结论；原v9validation继续rejected，matrix继续failed。

## v10 已授权的最小两文件设计

root已据静态根因和有限设计授权High只改 `validate.py`、`test_contracts.py`。本报告不读其浮动实现。

validator仅在原nonquery整数门保留严格 `integer` 后，将范围设为“>=0，或恰为−1且stage=create”。现有source/shape、API与is_async一致性、成功完成/error=null先行检查，以及后续single CREATE、step API、正式context、ledger SQL SHA/ACK绑定均保持。特殊值只属于固定CREATE，不允许任意negative，不把reader或其他业务stage的−1转成成功。其余validator字节应保持，不改producer/host/driver或报告原始返回值。

测试保留原71方法AST、历史pins及当前V9 producer fixture。新增有限真实v9 fixture：先验证固定startup、main report、cleanup、ledger及相关原Ref/hash，再直接调用完整 `case_contract(report,cleanup,ledger,startup,startup_sha,ledger_sha,case_id)` 正例，确保commands/events/transactions/values/raw/FF9和末尾cleanup都实际执行。直接调用这一完整consumer无需伪造当前runtime approval；不重签旧validation、不fake ROOT/SDK，也不用producer oracle。此正例只证明历史真实制品与修订合同相容。

语义负例在真实Ref已核验后仅修改加载对象，精确验证：未知负整数拒绝 `command_affected_rows`；bool/float/string/null拒绝原integer门；非create nonquery的−1仍拒绝；错误API、completed=false、非null error及stage变异各由原对应门拒绝。保留CREATE SQL SHA/ledger不匹配负例以证实“−1例外”不能绕过最终创建来源绑定。不得依赖整文件SHA早拒绝充当语义覆盖。新增方法名/总数由High最终实际声明，不预填通过数；不扩大到未确认问题。

README、prepare、run、policy、host9及其他15文件保持，producer15 aggregate继续 `ef2940d232e4f7b6eabd8aac6497f68bdf09ef2adbb956f1bb7264752d081c26`，host9继续 `02e9c4aa31f522c7000750854f6404d10300a8cc55f52c8d63bce4e60e8607df`；仅实际17/toolpack重新绑定。源码冻结及独立接受后按既定新Python完整集合→新source/native/build→compiled28/offline/readiness→另授新ordinal0 fixed4顺序验证，不补跑v9后缀，不增加额外DB诊断门。产品178/W/824、TLS/证书/保存点及process/receipt边界不变。全部旧成功与失败证据原样保留；本文完成后停写。
