# TP07-E transaction v2 stored verifier 源码审阅

2026-10-11。结论：**SOURCE_REJECTED，唯一阻塞B01为既有保护清单读取路径未迁移；当前v2不得执行或标记GeneratedCreate28接受。** 已完成冻结候选全文、完整patch、AST、实际引用和metadata派生审阅，未发现其它新增判断问题。本轮未执行verifier/项目模块、Git、tests、SDK、host或DB；唯一写入为本文，v2候选及所有旧证据保持不变。

## 冻结候选

- `TP07-E-transaction-v2-generated-verifier-handoff/freeze-manifest.json`：`c5009519d7bd84c7a3c24152e0463dcf51f48a09c6b1501fe121d626cad61e99`。
- `TP07-E-transaction-v2-generated-create-reverify-control/verify-stored-v2.py`：`14723a961ad675f13485634bbc8ef86a1e17696d65bf89a7bc806eafdd474b21`。
- 原verifier：`a76c814934c6f41fe07aa3b5c81cf75d25e423fe29a3e9fb35f392e725401003`；完整patch：`4622562eb72b3dcda068e07436b925563b2271f3f250043c1bb8f8fe5fe8378a`。
- AST proof：`8038dcce67643bf6809b255e4ad2facc8c6081141c306406ab5ba119d507d0b3`。

上述相对目录均在 `.local/temporal-native/verification`。实际脚本与冻结SHA一致，reverify目录当前只有脚本，没有实际28项核验结果。

## B01：保护清单源路径仍指向不存在的producer文件

`verify-stored-v2.py:11` 保留：

```python
pins=json.loads((original/'before-input-hashes.json').read_text())
```

但新 `original` 已指 `TP07-E-transaction-v2-generated-create`，该目录实际不存在 `before-input-hashes.json`。真实既有清单是 `TP07-E-transaction-v2-generated-create-control/before-input-hashes.json`，实际SHA `73254163e8b1928e70f6964d73d38ee67ad2c30d897e22a30dffa205ff200b47`，含103572项；handoff的producer-protection-provenance也引用此文件。因此按当前代码和实际文件状态，执行必在读取pins时触发 `FileNotFoundError`，早于28项深度判断及结果写入。这是源码静态确定的执行阻塞，不是假设的运行失败，也没有实际执行来制造失败制品。

最小修复由root另开High v3范围：只把该读取的一个字符串改为实际producer-control清单的规范绝对路径，保持 `original / absolute_path` 的现有Path运算形式即可；与原版比较合计十处Constant变化。v3另用全新reverify-v3输出目录及handoff，v2源码/metadata/拒绝报告全部不可变。禁止向旧producer输出目录补文件、复制/伪造旧保护清单、删pins检查或改判断逻辑。真实清单含ROOT相对及绝对key；在本次实际cwd ROOT下，原 `h` 对两者均有效，无须为此修改算法。

## 已核且可保留的部分

独立AST比较确认当前恰九处Constant差异；仅规范化这些常量后完整AST相同，45个顶层语句、18个assert及其位置/谓词均保持。差异为新output/producer/startup/pack/partition的七处引用，加两处commands metadata绝对路径；没有新增判断函数、动态可选oracle、SDK/DB命令或宽松成功分支。实际 `cwd` 为当前ROOT；`ef-validate.py` 模块仍设置真实 `__file__`，其ROOT计算未伪造。两个 `original / absolute_metadata_path` 均解析到同一规范metadata文件，不会拼出错误嵌套路径。

metadata `commands.json`实际SHA `35caa666b10f2b982403c6a8a9db23d87825ec690893504c9dd4023094661225`；相对原 `d8ea103841e1765e81a884a76cc96c08dc5247d5aec13cedce62d478d940daa7` 的两行，各自原18个键及全部值完全保持，唯一新增 `argv=command`，成为19键。原profiles `2688220e…`的process逐项等于原18键行；实际shared/tls process receipt的全部原字段及exit/termination/elapsed投影一致。两条真实process、日志、报告引用实际hash均匹配。原 `offline_gates` 明确消费 `original['argv']`，故独立metadata副本是必要接口适配，不是重签或重跑host。

metadata lineage SHA `493d8e2d219f47cc373be43ffb1cdc5ae786dfc8af44e2de25dbacb43a58d243` 正确保留原process/report/profiles来源及 `verifier_executed=false/host_rerun=false/runtime_accepted=false`。原producer与control没有被覆写。raw-source-pins的17项本轮全部实际hash匹配；High冻结历史保护读回 `1a93125bc43f00328fca2018edf186947d3738a765d90f68f0267e26a3cf528f`记录103717项不变，包含原103572保护清单。此保护记录不能修复脚本当前缺失的路径。

旧deep `c6f269c5…`、cohort `ec3f90e7…`、固定28case语义/模型/default/seed/operation/SQL字节比较、N精确VARCHAR(32)、实际generator/differ身份与IO零检查仍原样；新startup `1cadd834…`、pack `c8a00c79…`、partition `93a2ed68…`引用匹配。当前W和已接受E source36/prepare194/native196来源不受本次拒绝影响。partition仍按实际原合同338 cases/25 windows、window_size15理解；不将设计文字中的9改作运行合同。

本报告不宣称新GeneratedCreate28核验PASS，也不授予DB/runtime接受。root待High最小v3修复重新冻结后，再安排源码审阅及Low只执行stored verifier；不得重跑已有host或覆盖任何v2候选/旧结果。本文冻结后停写。
