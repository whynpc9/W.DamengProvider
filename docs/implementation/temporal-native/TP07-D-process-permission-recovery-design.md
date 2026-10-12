# TP07 D process permission recovery design

状态：**DESIGN_ACCEPTED_IN_SCOPE，实施及实际验证 pending**。本设计采用root选定的保守有限方案：PermissionError表示尚未证实进程组不存在，只有实际ProcessLookupError/ESRCH才证明absence。在原单一2秒回收预算内继续观察，不将权限异常转成成功，不扩大对象、信号动作或预算。

本轮唯一写入为本设计；没有运行或导入helper/tests，没有源码修改、SDK、CLI、host、DB或Git操作。所有旧adapter、fixture、approval、报告及失败证据保持原样。

## 1. 已证明的事实

v4完整fixture中SIGTERM与SIGINT两项观察通过，natural-leader失败。实际安全调用链为 `test_reaping_v4.run_case:133 → recover-v2.process:188 → reap_owned:158 → live:146 → absent:135 → os.killpg(group,0)`，异常类型为PermissionError。它来自存在性probe，不是已证明的TERM/KILL发送失败。

冻结readback：`.local/temporal-native/verification/TP07-D-owned-reader-recovery-v4-offline-control/failure-independent-readback.json`，SHA `caafb54d71fa57e05dcf58e37b9d232b53dbcfed119384cbebc1596e10ad86ab`。natural-leader使用leader54765、child54766、PGID54765；安全异常文件SHA `951c53eb9ece5819547d2886dd1cce362bd40e05e10d00e08673471312e26981`。当次result为null，后续fallback无信号发送，Low最终观察六个记录组均absent。

后续absence不证明异常发生时已absent。原安全记录没有errno数值，不补造EPERM/EACCES数字或内核成因；首次v2 SIGINT失败也不能由本次事实归因。当前只修复对PermissionError类的保守分类与有界流程。

## 2. 唯一产品外监督源码范围

在现有recovery-v1根新增 `adapter/recover-v3.py`，以冻结c7版为基底：`c7e73e4b5f945627dec35897c9775ea50aaf78a59cd91809d6fa44bdc23f3cff`。

允许修改仅为 `absent`、`reap_owned`、`process` 三个函数，以及 `APPROVALS` 常量改指全新 `.local/temporal-native/root-approvals/TP07-D-owned-reader-recovery-v3/`。ROOT仍指原 `TP07-D-owned-reader-recovery-v1` 工作根；不改其他helper、source/lock/guard/policy、SDK命令或业务清理代码。

维护source manifest仍为 `e459f207782f1aa8f1b9cea2b49f55a731be1f33496ecbcf87f56d9857ba29e6`，完整186来源不变，OwnedObjects仍 `07b0fa0857d1322731c6fc9b74feecc16d9a6a08d5352cd3de04651d3833a79c`。W、EF product178、原Literal/CandidateGuard/SafeEvidence和exact2 ledger不变。不得重写已冻结的旧source approval来替换adapter身份。

## 3. probe与receipt语义

`absent(group)`维持原bool接口，可添加内部可选的计数容器参数，以供一次process及其reap共同使用：

| 实际killpg(group,0)结果 | absent返回 | 含义 |
| --- | --- | --- |
| 调用返回 | false | 未确认消失 |
| ProcessLookupError | true | 此次实际ESRCH，确认消失 |
| PermissionError | false | 权限导致未确认；不能当不存在 |
| 其他异常 | 保留原异常行为 | 不用广泛OSError/Exception吞掉新问题 |

每次process创建私有计数状态，传给该process中的所有probe以及对应reap；每次probe捕获PermissionError才将计数加一。最终receipt输出非负整数 `probe_permission_error_count`（或等价明确名称），包括reap前、reap内和最终检查。无全局累积、无不同process混算，不把计数本身当成功依据。

独立调用 `absent(group)`可以不提供计数状态，仍保持只有ESRCH返回true。实际return receipt前的最终probe也必须计入同一状态，不能在填计数后再做未计数probe。字段仅为固定计数，不记录Message、env、locals或猜测errno。

## 4. 回收与发送动作

`reap_owned`仍使用一个monotonic总deadline，预算2秒，保留原最多一次TERM及必要时一次KILL、直接child poll/reap、owned-group判定。不因PermissionError开始新的deadline、另加等待段、重发相同信号或执行进程扫描。

probe的PermissionError返回false，使live逻辑保守继续；持续unknown直到预算收尾仍应输出group_absent=false、cleanup_complete=false。即使直接child已reap，也不能替代其组的实际ESRCH。原process success和fixture assertions继续拒绝该结果。

同一reap的send只额外捕获PermissionError，保存安全记录 `{signal, type}`（type为PermissionError），随后继续既有的有界probe/升级流程。只有信号调用实际成功返回时，才追加到原 `termination` 成功发送数组。ProcessLookupError仍按原处理；其他异常不扩大吞掉范围。不能把PermissionError发送记录伪装成成功TERM/KILL，也不能因权限问题改变目标PGID、使用更高权限或杀不拥有的组。

保留own_group=True只操作本次新session组、own_group=False只操作直接child的边界。正常实际SIGTERM/SIGINT中断、leader残留与安全异常字段的既有语义不变；强制回收仍不得成为natural success。

2秒是包括最终probe、child poll及receipt测量的总上限。root已确定具体收尾边界：总deadline仍为started+2，轮询最多到deadline-0.05，预留50ms给最后真实poll/probe和receipt测量；原0.5秒TERM grace及TERM+必要KILL动作次数不变。unknown可以提前结束并返回false，2秒是最大值而非必须耗尽的等待。若真实调度导致elapsed超过2秒，仍由原测试判失败，不clamp、不模拟时钟、不增tolerance或放宽断言。

## 5. 原完整fixture的最小新版本

新增 `adapter/test_reaping_v5.py`，以冻结v4 `19eb3f55006adda1662b6cc10833c7992e001ff6430ca9eca54c66cfcf009b84` 为基底。只更新被测helper路径/hash到实际新recover-v3，并在fixture自己的 `group_absent` killpg0 probe补充PermissionError→false。

保留failfast=True、原2methods/3棵树、每棵一次、真实V.process/V.absent、5秒process deadline、2秒reap、所有common assertions、持久atomic证据和safe异常规则。fixture的unknown也不能称absence；失败fallback仍只处理nonce/current getpgid绑定的本次组，不改为扫描或放宽身份。

fixture自身发送逻辑或其他函数不预先扩张；若实际新证据证明另一个具体缺陷，再单独审查。成功路径仍必须取得真实自然leader残留回收和实际ESRCH，而不是只通过下面的分类contract。

## 6. 有限PermissionError分类contracts

新增单独小型纯分类测试文件，直接加载并调用实际新helper的函数，不复制absent/reap算法，不搭建通用框架。由Low在独立许可的离线窗口执行；High仅实现与静态检查。

测试在临时mock上下文内替换killpg，必要时用有限内存child/Popen stub，让真实process/reap代码运行；不创建真实进程、不发送真实信号、不调用source preflight、SDK、wrapper或数据库。mock必须涵盖被测路径的所有killpg调用并在退出时恢复，按0/TERM/KILL区分probe与发送。

有限语义覆盖如下，可合理合并成少量methods：

1. killpg0返回或抛PermissionError时absent始终false；实际模拟ProcessLookupError分支才true，PermissionError计数与实际触发次数相符。
2. transient unknown后ESRCH时，只有ESRCH观察使absence成立；不从已reap的直接child替代组absence。
3. TERM/KILL发送抛PermissionError时产生安全 `{signal,type}` 记录，不追加成功termination；每个原动作最多一次，无重试或伪造成功。
4. probe持续PermissionError时，在真实monotonic单一2秒预算内结束为未确认absence/cleanup失败；原success断言拒绝该receipt。若mock发送也被拒绝，应同时保持发送记录真实。

最后一项不mock monotonic/sleep来伪造实际预算，也不进行真实process控制。真实完整fixture另行执行，不被这些contracts替代。若纯contract失败，保存该失败并停止后续执行；不能改原断言来求通过。

## 7. 冻结、许可和后续接受

High在新handoff中冻结新adapter、fixture、有限contracts、实际diff/AST和身份refs。三函数与APPROVALS差异之外的adapter AST应与c7相同；fixture差异只为helper pin和本地probe分类，原assert_reaped AST及failfast保持。旧c7、v4、诊断、失败制品与approval不回写。

Astra源码复审、Low独立静态readback后，root在新approval目录签署实际新adapter ref。旧source approval不覆盖新字节；source186来源可保持同一e459身份。新source入口、SDK三条命令build许可、实际build七refs及runtime exact2许可仍按原顺序分别签发，不能通过更换approval目录跳过验证。

Low先执行有限分类contracts及真实完整fixture，保留各自然退出、实际计数/发送/absence和预算证据；原完整suite须2methods/3树全部实际通过。任何一项失败停止，不启动source/SDK/DB后续步骤。旧v4实际FAIL和首次v2未知原因继续保留，不借新成功修改历史。

本设计不批准实际运行。后续source/SDK/build/runtime各层实际接受仍pending；exact2 TEST对象也仍待真实恢复验证，D/full C不因此接受。

本设计完成计算SHA后停止写入，最终SHA由消息交付。
