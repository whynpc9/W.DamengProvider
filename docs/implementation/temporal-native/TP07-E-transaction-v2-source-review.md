# NEW E transaction v2：B01 源码复审

2026-10-11。结论：**source_accepted，B01 已在本次限定源码范围修复，无新增阻塞**。这是源码接受，36 个合同仍为 planned，尚未由本审阅执行或宣称通过；prepare/native/build、offline338/25与GeneratedCreate28、runtime全25窗仍按原门推进。本文只读源码/JSON/字节并作标准库hash/AST比较，没有 import 项目、tests、SDK、Git、DB；唯一写入为本文。

| 冻结项 | 实际 SHA-256 |
| --- | --- |
| v2 handoff `freeze-manifest.json` | `5cc08b096e44f570c4387fb43eb6d6966d712ddd5d6403f44c57e55a3662f071` |
| `B01.patch` | `c836b16ae504d634413225a1cd4fe1ab734ec1816fe6eb506732f2bf452653db` |
| `ef-validate.py` | `c2cf988e3e0020093047bc310fa3786ef7bffa9c27a837f88bcb6fe9c801d45e` |
| `test_ef_contracts.py` | `b9801dee6ecaf1f8226f974e260cafe4767bfb0449cc0f99185fbfb963c42e16` |
| candidate source24 / toolpack8 | `613214ff5210fe2c084a27d8650d3abbed2034bbe4da309cc9ece57067fc38a2` / `c8a00c79b6f4cb3b4725b8e4df0f7b51351b469e087afe24f8f3d4e36856c792` |

已实际核对上述文件、36项 handoff assets、当前与snapshot的24成员及8项pack映射。prechange确为v1冻结；原34个测试方法AST全部相同，仅新增2方法。validator只新增 `e_transaction_projection`，`inputs` 原两个投影statement精确替换为该helper调用；归一化这处替换后原 `inputs` 逐字一致，其他原函数体全部逐字一致。

helper仅排除 provider、TP07LiteralProbe、TP07TemporalRuntime 三个固定项目下各 `bin/`、`obj/` 共六个带末尾slash的生成前缀。它在每个遍历节点进行目录/文件排除之前拒绝symlink，其他全部文件实际hash进入map，必须完整等于expected196；因此 `foo/bin/...`、其他项目 `obj/...`、普通额外文件及近似前缀不能再隐藏。helper也检查其传入replay根是否symlink；原 `inputs` 调用前的 `.resolve()` 语义未改，不将本修复扩大宣称为原始startup路径所有祖先link的通用拒绝门。

两个新增方法直接消费受控临时目录中的实际字节：194 source来自冻结L0185与E9，再复制两份真实历史lock得到196，仅作消费者fixture，明确不是新SDK证据。正例覆盖六允许生成前缀；负例覆盖非允许bin/obj、普通extra、三种近似前缀，以及普通/生成区域文件和目录symlink（包括整个允许bin目录链接）。错误分别精确到 `E_transaction_actual196_projection` / `E_transaction_projection_symlink`，移除变异后再次调用真实helper恢复正例，没有mock成功或whole-SHA捷径。

两份lock fixture均实际存在并已读取哈希：

- NEW L0 literal lock：`896258992bb91e4289953fef191287f8e6fe496706d6a3df070a85eb5dc5916e`。
- `.local/temporal-native/verification/TP07-E-combined-v5-lock/replay/test/TP07TemporalRuntime/packages.lock.json`：`3e39a9de708242cebd8596d11ca5d5ba0b3b70c5dcc6424d966cee31b7f1e8d8`。

新preparation三文件仍分别为脚本 `7f2323750e0025fe3be2e4faec373df02be786611627a000d79b1d3f96646c66`、README `f22a72824f0c2f70cbc74d4b42e93274f0249f4b2b5eab6a6937c1a6cee18ffb`、schema `9cdea3999aea07a327dc638e7afa56dd0895c1dbf218cc77281a1600c4082a98`。v1 handoff E9 Ref `deb47b37…`、新来源/loader守卫、旧分支、v11 policy、ef-run、全部值oracles及900/300/180/30预算保持。v1拒绝报告 `51df6092…` 与历史证据不回写。

High的100104项保全声明见 `protected-after-check.json`，SHA `a15331ebb3a2e43697a1336121229b29d6219c69da294e8b55b59b3c03c71e8a`；本审阅核对该报告身份，不冒称逐项重算全部历史文件。root可在Low静态快照独立接受后分配既有完整36合同门；本报告不授权执行，不添加额外门。报告完成后停写。
