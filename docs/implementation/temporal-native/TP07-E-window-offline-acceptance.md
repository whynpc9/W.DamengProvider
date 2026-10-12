# TP07 E固定窗口新cohort离线验收

2026-10-10。结论：**new_window_cohort_offline_accepted / runtime_approval_and_readiness_pending / DB_not_executed**。新编译partition338/25及新28模型实际host、修正后的保存输出独立深验和两有限gate均有一致证据；未发现本范围阻塞。root可组装新的12-ref runtime approval，再做readiness；不得复用旧10-ref approval或把本离线接受当成25窗实库通过。

本reviewer只读JSON/源码/SQL/文件字节并比较hash和关联，没有运行SDK、测试、validator、host或DB，没有修改源码和历史输出，仅新增本文。

## 新cohort身份闭合

两个gate与partition validation共同绑定下列实际身份：

| artifact | 路径（相对`.local/temporal-native/verification/`） | SHA256 |
| --- | --- | --- |
| source | TP07-E-combined-v4-prepare/source-manifest.json | `3b81ae40782f3b6459c16e1f8593a9fbe1a070151a7b521b107066ee611d3b65` |
| locks | TP07-E-combined-v4-lock/lock-manifest.json | `35cf38084e53651102b7791adb70edd20d681fd09baaa2e34ffbaf88a1e8aa52` |
| startup | TP07-E-combined-v4-build/startup-manifest.json | `838a410562f80815f21d47df20c897d30a5cc598c5cbd107db490a78154c7834` |
| toolpack | TP07-E-tools-v10/toolpack-manifest.json | `2482466c63a873843fd874747a277211eae0b9802872f700ed2b6325e4d41c23` |

policy为`8836130ae6151fb6194d15bab7226ec5dff7859cc4de06124fd1c19bf5f1c5fa`，window_contract digest为`9937d5f202912ad2dcfe726a0ab42fb9336454e83badd7f7fe4d7b11712920af`。本轮实读new startup所列193项source、3个lock、3个assets、50项binary均与当前文件hash匹配；v10八项工具live hash全部匹配，两gate/validation身份精确相等，所有本轮展开的38条嵌套文件引用也hash匹配。

来源/锁独立readback `TP07-E-combined-v4-source-lock-control/independent-readback.json` SHA`86b062d36ac9a4b9d7787d992732e6705e66cdd8bbe7819ce9f7ef88ebead41d`及build readback `TP07-E-combined-v4-build-control/independent-readback.json` SHA`a0e5205252384fdef4f29cc5624dfd575728f3065d9874815b44486c22e5edbd`保留191/193、native13、原lock字节及实际SDK10.0.401五个build事件证明。source/W/provider175/L0/common未扩展；新Program使用真实新cohort身份，不套用旧startup。

新E host DLL `f073610b3e543c1214ddffbecff0f4f935ecd0483c9d3084a78b4633ba84c236`、MVID`c1f0d790-e38a-40b1-ac1d-f833805b35e1`、deps`6537a024d4a44fc158765ec0aef283c6787861eada77f2d2a658deabea07fbe6`在startup、partition及28模型readback一致。当前DLL/deps字节属于上述binary全量实读；MVID由Low实际编译/纯PE证据及host报告关联，本reviewer未再次运行metadata helper。

## 实际编译partition门

`--offline-partition true`实际新DLL自然exit0/host0，0.09002449992112815秒，无timeout/termination；独立Python验证命令也自然exit0，两个过程均180秒固定上限。不是Python生成期望充当C#输出。

实际report包含原338个完整定义、固定25个连续slice、shared13/TLS12及原组顺序；本轮逐项比对ID/profile/group/shape/kind/ticks/offset/scale/API和policy，窗口descriptor与window_contract精确一致。保存独立结果还覆盖route/expected映射和同窗async正控制前缀，全部338/25有效。九项非法参数拒绝齐全：缺index、负数、越界、非整数、任意size/range/IDs及generated/partition混用。DB、CandidateGuard、OwnedObjects、Context构造旗均false；该门没有连接或执行业务case。

## 新28模型与修正后的独立证明

两个新compiled GeneratedCreate host各14项全部completed，自然exit0/host0、无timeout/termination，shared0.43422645796090364秒、TLS0.3901207922026515秒。commands、profile报告及gate保持原`--output`和startup关联，没有重跑host或复制报告伪装新执行。

本轮逐项比较28个case的runtime/database spec、models、columns、operation、seed和generated_SQL描述与已接受v4深验报告，无差异；34份实际SQL文件逐项读hash及字节，与对应v4基线一致。Low修正后的独立检查同时与v5cohort基线比对，记录全部28项N VARCHAR(32)或native无N、默认/种子/操作/SQL检查通过。真实IO中sync/async open/command次数全部0；每profile29个factory connection都Closed且保持W身份，case级IO也全部为零。

generator/differ修正保留了两条不同证据链：generator嵌套assembly属于新provider SHA`86eda4a85a296e7012b6a8edc29bab8f42198271d7a9a59b2d64b3eb3f3d29b7`、MVID`3da8eead-5bae-447a-8880-6cc9ef3b4104`；differ顶层身份属于`Microsoft.EntityFrameworkCore.Relational, Version=10.0.12.0, Culture=neutral, PublicKeyToken=adb9793829ddae60`，DLL SHA`a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d`、MVID`03b92316-8856-455e-a1c0-0602583c8738`。实际new Relational/provider文件hash在startup binary闭包中匹配，assets为Relational/10.0.12 package，28项service proof与实际case逐项相等。

本轮读取已执行`verify-stored.py` SHA`2603da3b7d5e7ceb58631d46cda42c20e988e721f20d43a519d19defd68e7ff8`：它用原受审纯PE `managed_identity.mvid`读取精确new Relational DLL，不加载程序集，并逐case比较顶层MVID；generator单独核当前provider。其自然exit0执行收据SHA`4fdaf91c65f377a780364737e8c43ac041cb6cc2b9e9dbc4ffabd305e162cb11`、service proof及冻结offline_gates成功readback相互支持。metadata reader本身live hash仍匹配v10 pack。本reviewer没有执行该脚本或validator。

## 可供新approval引用的gate与证据

下列路径均相对`.local/temporal-native/verification/`：

| 文件 | SHA256 |
| --- | --- |
| TP07-E-window-v1-partition/host/partition.json | `2f9b20bf166d19cb838999d5d05d856b6ecd52c1d9be66d34a584fba3f7f09b4` |
| TP07-E-window-v1-partition/validation.json | `293780e89f8995a39a7d31d21c6a759f126b3d2ad60380a47c3a0595b1245444` |
| **TP07-E-window-v1-partition/partition-gate.json** | **`8453e954cb3a7fea9ff5acae8386512f3087ca194862df8be5c9d3a987da6a79`** |
| TP07-E-window-v1-generated-create/shared/offline-generated-create.json | `a5a7d65ccf3dd1e8bbba9cefe8aaf2b850cce2afcf6335eb97ff8a1367882660` |
| TP07-E-window-v1-generated-create/tls/offline-generated-create.json | `81acbc1621e8bc7271dca3714402937139410029da0cc4a993070fd2c4e8f5bc` |
| TP07-E-window-v1-generated-create/commands.json | `d9bfd2318805c5842a6d279c3856245e11f0b4827749e17b4782b0982b8da1ed` |
| TP07-E-window-v1-generated-create-reverify-v1/independent-readback.json | `36b4181461d96036f67ef52756beb9c4183188fc3e0220a1c78a9a33c1d875f1` |
| TP07-E-window-v1-generated-create-reverify-v1/service-identity-readback.json | `d2ad487833e51a2b759cdaa9ded7c9d8d171f79a33ac4543a3accfe1f1f37685` |
| **TP07-E-window-v1-generated-create-reverify-v1/generated-create-gate.json** | **`dea8a9ea55d2608ccefd9a742e80e07061bb53511c659bcc8cc7a54942941304`** |
| TP07-E-window-v1-generated-create-reverify-v1/gate-function-readback.json | `2fd5a18c6b91887091891787491137d94bb9174ab9e3b58412b1b5db679e9cd0` |

冻结offline_gates实际检查78项nested pins通过；readback明确host_rerun=false、DB_executed=false、window_released=true，2823项prior pins不变。本轮实读其before/after两个2823项映射完全相等，另独立重hash本报告所列当前source/lock/assets/binary/toolpack和展开证据；不冒称重跑了全部2823文件或78门函数。

## 历史保留和接受边界

原failure-readback SHA仍为`a217c72d5e81578f12c8b8f1b6f6009a16a1b74cf387156c79ec1389cd4d2540`。v1 triage`4aa9b157…`及纠正其根因的v2 triage`2defc66a…`均字节保留；本轮接受新的正确readback，不回写最初TypeError或声称N代码曾被修复。v4/v5基线保留旧provider身份，不借旧host作为新28执行。

本结论只接受**新窗口cohort离线门**。下一步root创建新的有限12-ref批准，将上述partition_gate与generated_create_gate加入新source/locks/build/startup、candidate/capabilities及原L0四项proof；不复用旧approval路径/SHA、不将旧cohortgroup拼接入新run。readiness核全套引用后才可由Low独占运行新的25窗/338case，保持900/300/180固定界限、每窗main finally和独立exact-ledger cleanup/validator及失败停止规则。live数据库、完整E、D/完整C以及TP08后LocalClaude门仍未由本报告接受。
