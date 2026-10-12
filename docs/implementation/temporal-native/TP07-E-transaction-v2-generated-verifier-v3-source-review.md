# TP07-E transaction v2 stored verifier v3 源码接受

2026-10-11。结论：**SOURCE_ACCEPTED，v2的唯一B01已定点修复，无剩余源码阻塞。** 此结论只允许root继续安排Low静态核验及另行授权的stored-verifier执行，不代表新GeneratedCreate28已经PASS，不接受DB/runtime。此次只读取源码、JSON、AST及文件哈希，未执行verifier/项目模块、Git、tests、SDK、host或DB；唯一写入为本文。

## 冻结身份

目录均在 `.local/temporal-native/verification`：

| 制品 | SHA-256 |
| --- | --- |
| `TP07-E-transaction-v2-generated-verifier-v3-handoff/freeze-manifest.json` | `dde976fdffe6b50fcd5c26203543d878b31b619cca56957e87144144276b252a` |
| `TP07-E-transaction-v2-generated-create-reverify-v3-control/verify-stored-v3.py` | `84884f34fa41f20ac490f7d876cdf8eed25bc086cdde5ce0e1d440f2d2a59dae` |
| v3 `AST-proof.json` | `de2a20ee454a6a2c850db9d4b844f7dff006b89e158bbff940ae6f7aad07147a` |
| `v2-to-v3.patch` | `c6717fd4e0e00566c54c2d575196fad44cb45d3df771cfb4015934e3041842a5` |
| `original-to-v3.patch` | `03a9b18830309b09c2ef92183cebdc618762010f6aede134eb0494f6c84e294f` |

已读最终freeze，脚本与预审候选相同。新reverify-v3目录当前仅此脚本，没有核验结果或gate文件。v2脚本 `14723a96…`、v2 freeze `c5009519…`及 [v2拒绝报告](TP07-E-transaction-v2-generated-verifier-source-review.md) SHA `3da2414bc3ca6e8fb6ed6bae326d55284a6cc1c92f00927835fde56e99fe20e3`保持历史不可变。

## B01闭合及不变判断

本轮独立AST比较确认：相对v2只变两个Constant，即新输出目录n和第11行before-pins读取路径；相对原a76脚本合计十个Constant。仅规范化对应常量后完整AST相等，45个顶层语句及18个assert的body/位置/判断不变，没有新helper或放宽任何predicate。

第11行现在指向真实 `TP07-E-transaction-v2-generated-create-control/before-input-hashes.json`，SHA `73254163e8b1928e70f6964d73d38ee67ad2c30d897e22a30dffa205ff200b47`。`original / absolute_path` 实际解析为这个规范绝对路径。独立读取清单103572项，其中5项为ROOT相对key，其余为绝对key；在本次实际cwd ROOT下全部存在且逐项实际hash匹配。不存在向旧producer目录补文件或制造替代保护清单的行为，B01的缺文件阻塞已消除。

新n限定所有核验写入到新reverify-v3目录；原输出文件名及write函数没有变化。实际ROOT、模块 `__file__`、当前startup `1cadd834…`、pack `c8a00c79…`、policy/partition及完整 `offline_gates` 消费路径保持。原deep `c6f269c5…`、cohort `ec3f90e7…`、28项模型/default/seed/operation/SQL字节比较、N字段、generator/differ身份与IO零判断均沿用v2全文审阅结论，没有新增语义差异。

两份metadata继续使用已审的 `35caa666b10f2b982403c6a8a9db23d87825ec690893504c9dd4023094661225` 和 `493d8e2d219f47cc373be43ffb1cdc5ae786dfc8af44e2de25dbacb43a58d243`，实际字节不变；原两行18键值完整保留，仅有 `argv=command` 的兼容字段。两个commands绝对路径、真实process/profile/report投影及原旧保护证据没有重写。本轮raw-source-pins全部实际hash匹配；High最终103728项历史保护读回为 `07aa8c88ca5aa642a22d90d8e25af81812ddfef8b12a5c061d97472f9c757a71`，记录无漂移。

## 接受范围

可以由root在Low新冻结输入核验后，仅执行v3 stored verifier读取已有producer报告/SQL/程序集元数据；不重跑host，不重建SDK，不执行DB，不复用v2拒绝为成功。只有实际自然完成并读回新的深度28项结果、完整offline gate及before/after保护证据后，才可讨论GeneratedCreate28接受。partition仍按实际338 cases/25 windows与原window_size15合同，不以文字错误改判定。本次不关闭E338/D或扩大产品/runtime接受。本文冻结后停写。
