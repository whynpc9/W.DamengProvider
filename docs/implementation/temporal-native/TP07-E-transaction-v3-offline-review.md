# TP07-E transaction v3 限定离线验收

2026-10-12。结论：**OFFLINE_ACCEPTED_IN_SCOPE**。当前冻结validator/test、source24/E8、实际39项合同、新partition验证及新GeneratedCreate完整stored消费闭合，无本轮离线阻塞。未接受E runtime或历史TLS12项：B v2目前仅源码接受，实际历史报告消费、新审批/readiness、全新338项/25窗口均不在本报告接受范围。没有执行项目imports、tests、SDK、host、DB或Git；唯一写入为本文。

## 当前身份与实际合同

| 冻结输入 | SHA-256 |
| --- | --- |
| `tools/DownstreamAcceptance/TP07/ef-validate.py` | `5b534116340abdaa48bfd0555b5a177ee8462e4ff51dbb4846cdd28ec76758ec` |
| 同目录 `test_ef_contracts.py` | `41ca79e7d5db4a3996a288617c713656d5491186d35205d5d66640d4da4498bb` |
| `TP07-E-transaction-tools-v3/source-manifest.json` | `7102998f4fcb860b1e5c40c027ca8c95801238402e29b4b315065b678fa4a75e` |
| 同目录 `toolpack-manifest.json` | `b48626947bb838f053755acd2a82f0262c8e903d65a50291cbec48751e0f41e5` |
| `TP07-E-transaction-v3-contracts-control/summary.json` | `a9aac32cb79a237a370fe17875664ec654d517a153400e35a7a6a948ad44a697` |
| 同目录 `independent-readback.json` | `52eed3adf5c470f2a7e807a29b7424fd9d7a7f0860df64dae6826d64a2acf6cf` |

`TP07-*` 路径均相对于 `.local/temporal-native/verification/`。已读[有限TLS6源码审阅](TP07-E-transaction-v3-TLS-concurrency-source-review.md)，SHA `0b4306fde1ee4498905d5a54c923457740ee7f2a5323af621d57588a25d75142`。原36方法保持，新增3项只验证有限TLS6 concurrency合同；通用 `raw_tuple/published` 与shared7原helper不放宽。新source24的当前文件和独立快照逐项实际SHA相同，E8为其精确原成员子集；C#、runner、policy及其余22项没有改变。

Low完整39方法一次自然0，29.529652291秒，39/39、0失败/错误/跳过，实际方法集合等于AST声明集合；没有把合成fixture结果冒充历史TLS12真实报告或新实库通过。static32、contracts8、partition8、A static7、A execution23条制品清单均由本审阅逐项物理重哈希通过。

## 构建复用与partition

source194、native196、startup `1cadd834dacf071f35355188d549976f2b750af579f442f498630031310eb743`沿用v2已接受构建。此次再次核对build中196份source、3lock、3assets、声明的50个host binary文件及lineage11 Ref，实际字节相同。provider仍 `7e971f09d645500e342688aefb484343a40bc7d0c7fb9713f3bfa18285d069b0`、W仍 `843710be…`；product178和真实L0链保持。没有新SDK构建或host生产执行。

新partition gate `829e337534d038fcb1a1d9264667ab2889d0877dd90b78834459023a41f30f9e`保留原v2 process及host报告 `93a09e53ed6b505e00485eac105dd89ff5c9a70e28e23108d511e6af09859732`，只将工具身份和独立validation绑定当前pack。原validator实际重新消费该报告自然0/0.266690167秒，validation `6fcbad6f281c2b35796ddb27e08ec121d623404bf2ef98f42b498d5d96feabcc`，readback `587b3e99a9dce25f7d0def4c29614f0cda8ccda0bb2a844e661e891a7d1b749d`：338 IDs、25 windows、9个原非法参数拒绝；window_size=15。旧报告没有改签成新host事件。

## 新stored执行及完整gate

A脚本 `TP07-E-transaction-v3-generated-create-reverify-control/verify-stored-v4.py` SHA `deaa0eb739a5d88c0d6fb32b5e043fe265e521c480651b5e7d6f1fef00ddaeb5`。已读[A源码接受](TP07-E-transaction-v3-gate-tools-source-review.md) SHA `2420a7dd8181a356353bc4e4196c009c529c8a9b948710f16787f1c1f44556d2`，另独立AST比较确认只有5个常量换绑，归一后完整AST相同、45个顶层statement/18个assert保持。

新母清单派生 `d3c4d128…`与原 `73254163…`的103572个key完全相同，仅两项当前validator/test SHA变化；本审阅核实这两个物理字节，Low static另实际核完全部清单。原母map和producer历史未重写。A仍使用真实ROOT/__file__及原完整判断，不导向新fake producer。

Low在固定ROOT实际执行A一次，自然exit0，51.939378792秒，180秒/reap2，无timeout/termination，PGID63128不存在；未执行B、SDK、host、DB或TEST wrapper。关键输出为：

| 制品 | SHA-256 |
| --- | --- |
| 新 `generated-create-gate.json` | `fe61e26eb0f1d3e3eb5e95eafc29b246176cb81645b850865ff6cc697ec14564` |
| 新 `gate-function-readback.json` | `bf1d983514bbff6b7c9cc26434178c47e043079f3f7e33cf4be0ed9a1fed2440` |
| 新 `independent-readback.json` | `d7b50102712e415db36192c385f09265c0490c701f4c3ec38bde38e9eb03a40e` |
| 新 `service-identity-readback.json` | `76d24bd59e27f368ce78821227215f1ad94a352d0225599f0c10af6c31576763` |
| `TP07-E-transaction-v3-generated-create-execution-control/independent-readback.json` | `8c3af14755c894ab157c625a03bc0225a5b44d98ba6bad67a1fb5a4cb0296af5` |
| 同目录 `actual-artifact-hashes.json` | `91118cf957f9b6c2891eafcf0ee2cc2e5f682da9d6e9af479d69e8a605fb6d67` |

前四项位于新reverify-control目录。新gate身份与新partition完全相同、绑定当前 `b4862694…` pack及原startup。完整原 `offline_gates` 实际成功返回78 nested pins。deep/services与旧输出内容SHA相同，是新一次消费生成的相同结果；接受依据包含新脚本receipt、新pack gate和前后保护，不仅是旧文件同hash。

本审阅逐项核对gate/deep/service中的36个Ref出现项；实际28项模型、operation、seed、default及34份SQL与原deep `c6f269c5…`、cohort `ec3f90e7…`两套基线完全相同。28服务行绑定当前provider和实际Relational程序集；两profile各14项，open/command attempt四类计数均0、factory connections关闭。前后输入清单103613项字节相同（`5b105400…`）；Low外层107131项保护记录无漂移。本审阅不冒称再次逐个重哈希全部历史保护清单。

## 保留事项与接受边界

原v2 runtime首失败及所有旧失败不重分类。partition阶段的精确bytecode例外与清理回读保留：仅授权移除 `ef-validate.cpython-314.pyc`，原字节 `d8434af1…`有immutable snapshot，旧制品清单保持；不能笼统写成从未出现文件变化。contracts消费者初始错误、旧A/B源码拒绝及其修复报告继续保留，没有因此重跑host或改原runtime证据。

B v2源码接受报告 SHA `c44672be2eddfe40293558a5d71e2938fdf7f84e9208d6d51da4411bf6913555`只说明源码范围，不说明12项历史消费已实际通过。本审阅未读取并行B static浮动输出，也不签发新审批/readiness/runtime授权。后续按root既定依赖推进；全新338/25仍必须独立同轮验收，不能拼接历史窗口。本文离线接受不关闭E实库、D、TP07/TP08或发布，冻结后停写。
