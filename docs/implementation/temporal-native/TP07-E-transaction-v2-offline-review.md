# TP07-E transaction v2 offline 验收

2026-10-12。结论：**OFFLINE_ACCEPTED，无剩余离线阻塞。** 接受实际 source contracts36、prepared194、native196、locked build，以及同一新构建的 compiled partition338/25 和 GeneratedCreate28 完整消费结果。本报告仅作只读源码、JSON、哈希及模型/SQL字节审阅；没有执行项目模块、tests、SDK、host、Git 或 DB。E 的全338实库、25窗口 same-run、完整 runtime inputs/审批消费仍属后续独立任务，不能据此宣称 TP07、TP08、发布或合并完成。

## 冻结证据

下列路径均相对于 `.local/temporal-native/verification/`，所列文件已实际读取、重哈希。

| 制品 | SHA-256 |
| --- | --- |
| `TP07-E-transaction-tools-v2/toolpack-manifest.json` | `c8a00c79b6f4cb3b4725b8e4df0f7b51351b469e087afe24f8f3d4e36856c792` |
| `TP07-E-transaction-v2-contracts/summary.json` | `1091b22c7abf5f32f78a555fa05b1bbb5dd89c7f130f1ad61e8767fc70b40340` |
| `TP07-E-transaction-v2-prepare/source-manifest.json` | `a76691139cb8f84f734341da96528d1cb2f9a97fe405515c249043f3c796b341` |
| `TP07-E-transaction-v2-lock/lock-manifest.json` | `b3adc9727555e6f16d42dd9aa5e3a020464806ba9b966dbea9b3537644abaaac` |
| `TP07-E-transaction-v2-build/startup-manifest.json` | `1cadd834dacf071f35355188d549976f2b750af579f442f498630031310eb743` |
| `TP07-E-transaction-v2-build-control/independent-readback.json` | `e5e9cb1778791ee56f24a66dd94f3d3c6295e873839f24c35021e642044d9cc7` |
| `TP07-E-transaction-v2-partition/partition-gate.json` | `93a2ed688c38ee5fb41794f65c6116ad12f597357aa73ad0c991e7e17b7ee9fd` |
| `TP07-E-transaction-v2-generated-create-reverify-v3-control/independent-readback.json` | `d7b50102712e415db36192c385f09265c0490c701f4c3ec38bde38e9eb03a40e` |
| 同目录 `generated-create-gate.json` | `260ec4403e3aa637d9227b2a2780f83edb3ca46c6be172729c961da018c0c535` |
| 同目录 `gate-function-readback.json` | `04da73b4289800ffcb1a5823aebbdb0bedf4abff3458f8f375ed055f4cfd47f2` |
| `TP07-E-transaction-v2-generated-verifier-v3-execution-control/independent-execution-readback.json` | `241ceb7203fbd2b59ab62e761065bc7d2682418fe264719cdf447b24664f9c5c` |

当前 validator 仍为 `c2cf988e3e0020093047bc310fa3786ef7bffa9c27a837f88bcb6fe9c801d45e`；source24为 `613214ff5210fe2c084a27d8650d3abbed2034bbe4da309cc9ece57067fc38a2`，原28+6+2方法实际36/36、无失败/错误/跳过。已逐条重哈希 contracts/prepare/native/build、partition、GeneratedCreate producer、stored execution 的制品清单，分别9/401/1122/1335/21/92/23项，均一致；这不等同于本审阅重新执行这些门。

## 来源、native 与同构建闭合

实际读取当前 `e_transaction_inputs`、`e_transaction_semantics`、`e_transaction_projection` 并与冻结输出对应。approved175 父来源保留；canonical transaction product178 + L0/common7 + E9 精确组成194，三阶段实际源字节一致。native196仅增加两个host锁；provider原锁未变。新build六个精确项目 bin/obj 前缀之外的完整物理投影恰为196，遍历节点无symlink。lineage11真实Ref、product19 nested Ref均重哈希匹配，canonical产品manifest仍 `40012339…`，source-only approval仍 `66c30dc0…`，真实L0 source185、lock186、run `2c76e364…`、validation `266a3054…`的来源绑定未改签或冒充旧startup。Low build读回记录三个原始E transaction合同在真实source/lock/startup上完整通过；完整runtime `inputs`及实际L0消费仍待后续独立审批门。

native与build各有三lock、六份assets/dgspec原始文件。逐包核对14缓存metadata的version为严格整数2；provider锁header2、两host header1；provider graph14、host graph15。各包原生contentHash在lock/assets/cache metadata三向一致，ZIP archive SHA512另与sidecar一致，两种hash域未混用。source194→196、锁内容、原13非W包来源及W身份保留。native三条SDK、build五条SDK记录均自然0，无timeout/termination；build receipt绑定新provider DLL `7e971f09d645500e342688aefb484343a40bc7d0c7fb9713f3bfa18285d069b0`、原W `843710be…`与新E host `a04de52e3a976c004f50827a5651c5782e4a7f7c91b2af38a9d25e0ee8b28e0e`。不把未记录的各SDK子PID外部查询冒充已记录事实。

## 离线执行与完整消费

新compiled partition和原validator实际自然0，精确338 IDs/25 windows。**原拒绝合同是9项，window_size为15**；已静态核对 Program 的九个test字符串、`validate_partition_report`精确列表以及旧generic真实报告。准备设计中“15反例”是文字计数错误，本报告纠正而保留原冻结设计哈希、原判定及全部九项拒绝，不另造15门。

GeneratedCreate shared/tls两次原host执行自然0，各14项，同一startup/provider/host。两profile各29个factory connections关闭，四类open/command attempt计数为0。独立逐项比较28项模型、operation、seed、default及34份SQL，全部与原deep `c6f269c5…`和cohort `ec3f90e7…`两基线一致；实际generator/differ的28行服务身份绑定当前provider及真实Relational程序集，没有DB或TEST wrapper执行。

经[独立源码审阅](TP07-E-transaction-v2-generated-verifier-v3-source-review.md)冻结的stored verifier `84884f34fa41f20ac490f7d876cdf8eed25bc086cdde5ce0e1d440f2d2a59dae`实际一次自然退出0，27.677040083秒，180秒预算/reap2，PID/PGID81194外部不存在。完整原 `offline_gates` 成功消费78 nested pins，深读回28项、34 SQL、28 services；本审阅另核gate/readback/service中的全部36个Ref出现项及23条最终制品清单实际字节。脚本before/after均 `9216aa34…`、103613项相同；Low外层103741保护记录无漂移。保护结论据实际读回与相同清单，未声称本审阅再逐个重哈希全部历史保护文件。

## 保留的兼容修订与边界

原producer `commands.json`缺少旧consumer必需的`argv`，但真实`command`存在。新独立metadata `35caa666b10f2b982403c6a8a9db23d87825ec690893504c9dd4023094661225`两行与原记录逐字段相同，仅新增`argv=command`；没有覆盖原commands、改变process值或重跑host。stored verifier v2因before-pins路径不存在被静态拒绝；v3仅有限路径/SHA绑定修复后执行，原判断保留。Low归档消费者的局部变量错误也保留，未导致verifier重试。以上均属工具/元数据兼容历史，不是产品、模型或实库失败，更不改写旧E288、TLS失败窗口22及既往T1/L0失败证据。

本轮离线证据已足够接受。后续仅由root建立新12 Ref有限runtime审批，再安排完整真实inputs与same-run25窗口338；不能拼接旧288，也不因本报告放宽900/300/180预算、清理、TLS、交易、重试、raw typed FF9或原oracle。本文冻结后停写。
