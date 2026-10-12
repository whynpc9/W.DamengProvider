# TP-03 wiring gate v2 定点复核

日期：2026-10-08。结论：**R01/R02 静态闭环 / wiring_tool_static_accepted**。仅接受新工具子集供 Low 独立执行 Python 合同；真实候选 wiring、TP-03 整体、TP-04/05 及 full temporal 验收仍 pending。未执行 unittest、build 或 DB，未改产品、测试及旧 TP-01 工具。

| 冻结输入（相对 `tools/TemporalWiringAcceptance/`） | 实读 SHA256 |
| --- | --- |
| `validate.py` | `7027df4b572f1aadf82cdbd570d837b24216607453d610dc14e2429eaa49799f` |
| `test_contracts.py` | `ebc87843a01f505c507f6eb5fed04420b4391627c0980ddb9f5bab5f2fc92d45` |
| `README.md` | `b04307e2e13501aae1d10502e2087b7303f6f94d7895c8ac3f73607f8ea1c98a` |
| `contract-fixtures/handwritten-artifact-spec.json` | `0b13adc8a71b93e22c14e3d4cdc32d5641e2a8114690302ff840964c7087c826` |
| `contract-fixtures/packed_literal_cases.json` | `df97ea26457079c0b5a3465d9191078cb3e4ae4e5e49ba7a4be48be31f8ed148` |

与 `.local/temporal-native/verification/TP03-wiring-v1/source/` 比较，validator 仅追加以下两项限定检查，tests 仅追加两方法，README 同步合同；两个 fixture 与 v1 一致。v1 的 changes_requested 报告与快照保留，不回写。

**R01**：`exact_actual_raw` 现对每个必需 O/W oracle 校验目标 CType、semantic scale、width、mask、结果列 type_flag，以及恰好一个 schema scale 等于目标。`candidate=False` 仅省略未来 TP-04 raw-server provenance，随后既有 cell、实际 length、raw bytes/hash/metadata、expected、FF9/FF7 及普通 TS EXTRACT 检查保留。O scale 与 raw scale 同改为 8 的 v1 缺口已由目标 metadata 检查拒绝。没有要求 O getter 模仿 W，也没有改变 O 非必需写入的历史表征。

**R02**：v1 中的 archive 待裁决项已由 root 明确为新 TP-03 candidate 的强制要求。v2 先要求 `product-source.tar` 为实际文件，再校验 producer 与 frozen 的非 null SHA256 均等于实际文件 SHA，并继续原 producer/source/package 身份链。文件缺失或全链 archive SHA 同为 null 不再可接受。此决定及实现仅适用新工具；原 TP-01 baseline 的可选 archive 规则保持冻结。

新增两方法覆盖 O scale/raw scale 联动、type_flag/CType/width/schema 的变异，以及 archive 文件缺失、双侧 null、文件缺失加双 null、全链错误 archive SHA。每个变异先验证正常 synthetic control，再更新相应报告或身份 hash，确保预期失败到达语义门。原八方法与 synthetic seam 强制 false 的接受旗未变，当前共十个 unittest 方法，**尚未运行**。

未发现这次限定差异的新静态阻塞。Low 可按 root 独占窗口运行这十个 Python 合同；通过仍仅为 synthetic policy 控制流验证，之后完整候选须以真实包、实际 source archive 及 shared/core 运行通过默认 CLI 入口。本报告不提前接受仍在修复的主 codec，也不以 interval 子集结果代替整体接受。

本报告完成后停止写入。
