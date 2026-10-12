# TP07 D owned-reader recovery v2 source review

结论：**SOURCE_ACCEPTED_IN_SCOPE**。v1 报告的 B1（进程回收）和 B2（dotnet 执行前身份）已在 v2 源码中关闭；未发现新的实质阻塞点。接受范围仅为固定186维护源、v2有限 adapter 和两项离线进程测试的源码。测试、SDK build、exact2 DB恢复及 D runtime 均仍 pending。

本轮只读源文件/JSON/diff、解析标准库 AST、计算实际文件哈希；没有导入或执行 adapter/helper/tests，没有运行 SDK/CLI/host/DB/Git。唯一写入为本报告；旧设计、源码、handoff 和 v1 SOURCE_BLOCKED 报告保持不变。

## 本次冻结身份

根仍为 `.local/temporal-native/verification/TP07-D-owned-reader-recovery-v1/`，v2 使用独立文件和 `handoff-v2`。

| 对象 | SHA-256 |
| --- | --- |
| `adapter/recover-v2.py` | `c7e73e4b5f945627dec35897c9775ea50aaf78a59cd91809d6fa44bdc23f3cff` |
| `adapter/test_reaping_v2.py` | `8e6c94c0c528eb8684cc7e1034537a31d33f26ddd3c4725200a103572294f1f5` |
| `handoff-v2/freeze-manifest.json` | `bbcad9922251bd2cd75cf7344eed5f104fc2326394f96fccfdd9c92b5135a675` |
| `handoff-v2/implementation-report.json` | `d9e5f403fe049cc2aa2541e7d574add8751ee15d74dd2f9958340605752b2035` |
| 未签名 `source-approval-template-v2.json` | `31f3647fdd3e87594d08fc88f31b5f60cf33c0dbcdf3ee5c104ef24dbe054bfb` |
| `handoff-v2/recover-v2.patch` | `c810d6b34db446b00277b21064264c74502291f9fb817b14760acdff4be8df2a` |
| `handoff-v2/whole-module-AST-proof.json` | `ccb28b225ea6ca2847556cc30e9e830052d52e870b2cf5a32c63403ce62bd77c` |
| 保留维护 source manifest | `e459f207782f1aa8f1b9cea2b49f55a731be1f33496ecbcf87f56d9857ba29e6` |
| 保留修复后 OwnedObjects | `07b0fa0857d1322731c6fc9b74feecc16d9a6a08d5352cd3de04651d3833a79c` |
| v1 审查报告 | `26172e713debba4b8c0544af9117a19098b097531316008e1a92af13b47cc6ff` |

## B1 关闭：拥有的进程树有界回收

完整阅读 `ProcessInterrupted`、`reap_owned`、`process` 及 runtime 内外层调用。进程创建前安装 SIGINT/SIGTERM handler；Popen 期间暂存收到的信号，捕获 child handle 后再处理中断。timeout、可捕获中断、异常，以及 leader 自然退出但其组仍有后代，均进入已有 child 的统一清理分支。

`reap_owned` 使用单一 monotonic deadline（起点+2秒），先 TERM，至多0.5秒 grace，仍存活则 KILL；随后以 WNOHANG poll 回收直接 child，并检查组是否消失。不存在 v1 的两个完整2秒等待段。清理窗口内忽略重复可捕获信号，完成后恢复原 handlers。

外层仅以自己 `start_new_session=True` 得到的 child PID 作为 PGID 发组信号，leader 已退出也继续检查该拥有的组。内部 host 的 `own_group=False` 路径仅向直接 child 发信号，不杀当前 wrapper/父组；外层 wrapper owner 负责整个组的最终消失。`group_absent`、`child_reaped`、实际 termination、stop_reason、exception_type 和 reap 信息分别保留。

任何 timeout/异常/信号/残留清理均使 natural_exit=false，原 success 判定不把“强制回收成功”冒认自然成功。SDK/host deadline上限仍300，reap仍单一2秒，无生产者重试。超出预算而未确认消失时保留失败事实，不能凭清理动作已发出接受成功；不可捕获的 OS SIGKILL 不在 Python handler 的保证范围内。

这关闭了 v1 的具体源码遗漏，不代表本轮已实际验证信号时序或运行结果。

## B2 关闭：dotnet 实际字节在首执行前验证

`source_preflight` 在解析并验证固定源 manifest 后显式要求 dotnet 对象恰为 path/sha256/sdk_version 三字段，版本10.0.401、SHA `00c673767ee2538369f307d3c8a0ce06f16bc9e0ecf8ce767b64dd02b2230e98`，再将 path/sha256 对交给原 `artifact()`。因此 canonical path、实际 hash 和 protected 登记均发生在首次 passive helper、SDK/process、wrapper 或凭据环境访问前。

通用 ref schema没有放宽。独立回读确认该 dotnet路径当前 canonical且实际 SHA相符。原 build/runtime/source许可分离、实际build七Refs、新 source/lock/startup绑定均保留。

## 差异与来源范围

独立 AST 比较：去掉新增 `ProcessInterrupted`/`reap_owned`，把修改的 `process` 定义替换回旧定义，去掉 source_preflight 的两条新增语句后，整模块 AST 与 v1 完全相同（忽略位置属性）。这证明实际差异局限于 B1/B2，没有更改原清理入口、业务政策、SDK命令或来源闭环。

186维护 source map实际核对全部通过。仅原 PK reader block 的 C#维护差异继续成立，185项相对原BC保持；product178、W、EF、Literal Program、CandidateGuard、SafeEvidence、csproj和锁没有新的变更。exact2 ledger仍原 `19f71ff40c67768c924b346d726ff0f885791eac4be432c5009f415292aaa9b2`；create_acknowledged缺comment的原边界、逐对象structure/marker检查及原finally第二次Cleanup保持。

startup仍可用兼容 `TP07-D-cleanup-startup` family标签；新的 maintenance origin/实际源身份由有限 adapter验证，原 C#Guard验证实际 source/locks/assets/W/EF/host。v2 source approval模板与v1只有 adapter ref不同，仍未签署。旧审批不会自动接受新的adapter字节。

## 两项离线进程测试源码

测试通过加载实际 `recover-v2.py` 的定义，直接调用真实 `process/reap_owned` 和安装的 handlers；没有复制被测算法作为替代实现。模块导入不调用其 main/source preflight/helpers/SDK/DB。

第一个方法启动会自然退出的 leader和同组忽略TERM的后代，要求真实helper发现残留、TERM/KILL、有界回收并报告非自然成功。第二个方法串行执行SIGTERM/SIGINT两个subcase，向独立监督进程发实际信号，再验证被测handler清理其所拥有的child组。

fixture仅位于新 `TP07_reaping_v2_` 临时目录，使用当前Python与标准库。其PID/PGID由自身实际进程写出，失败后的安全清理先核对记录child的当前PGID，再处理该组；监督进程按其直接Popen handle回收。测试没有SDK、wrapper、secret或业务SQL入口。每次被测process为5秒上限加同一2秒reap，两个signal subcase串行；测试断言直接child已reap、owned group实际不存在、reap预算/elapsed、TERM和KILL，以及不得自然成功。

两个方法的实际退出码、耗时和组消失证据必须来自后续Low运行。本报告没有把源码中的断言或High的静态布尔值记成测试通过，也不增加新的诊断/全对象预检查门槛。

## 独立回读与后续范围

706个保护refs的before/after相同且实际SHA均匹配；v2 freeze的14成员实际SHA匹配，handoff-v2文件无可写位，freeze自身为单独冻结ref。原维护186物理源再次核对通过，旧392文件的哈希保持，权限调整属于root先前授权的只读冻结。journal仍为`f6165e…`，build/run目录仍不存在。

允许下一步由root按原分工签署v2实际source approval，并安排Low执行这两项标准库离线进程测试及独立readback；SDK build与DB恢复仍需各自明确执行许可。只有真实exact2 fresh count0和TEST身份结果才可关闭遗留对象恢复。此SOURCE接受不覆盖D guarded/scaffold/TLS/full C，也不修改v1报告的历史结论。

本报告完成后计算SHA并停止写入，最终SHA由消息交付。
