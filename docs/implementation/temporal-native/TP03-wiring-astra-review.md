# TP-03 wiring gate 独立静态审阅

日期：2026-10-08。结论：**changes_requested，R01：O oracle 的目标 metadata 校验不完整**。另列 source archive 必需性待 root 裁决。仅审新工具冻结输入及其只读依赖；未运行 unittest、build 或 DB，未改旧 17 工具、产品或测试，主 codec 不在本次整合接受范围。

## 冻结输入

以下 SHA256 已实读一致，路径均相对 `tools/TemporalWiringAcceptance/`：

| 文件 | SHA256 |
| --- | --- |
| `validate.py` | `f4b59aae87d74d9b4dbd3f4af73870bcdb24341eebecf5314973f0de4833a7f4` |
| `test_contracts.py` | `9f04cbcb6b29898b3328f9eb02c9a5d0e36c1fad79ce5d4db65eb59d6df479ea` |
| `README.md` | `192b655ace58e09ea2c9c5dce3e24441978bf370b5d0e5fd91724e7552417207` |
| `contract-fixtures/handwritten-artifact-spec.json` | `0b13adc8a71b93e22c14e3d4cdc32d5641e2a8114690302ff840964c7087c826` |
| `contract-fixtures/packed_literal_cases.json` | `df97ea26457079c0b5a3465d9191078cb3e4ae4e5e49ba7a4be48be31f8ed148` |

三个依赖实读与 `PINS` 一致：旧 validator `3ff4331e542308763216281b506f15b9bcd44a4a369d326b65c7206d4dedd265`、contracts `f8ad9abddd86e415374b9594954f2264fc5d701e3f603c7a96ed77e14dbca6cb`、packed oracle `bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9`。本审阅不重新接受或修改 TP-01 历史工具。

## R01：双路 oracle 需要同等的目标 metadata 门

`validate.py:98–105` 的 `exact_actual_raw` 对两路仅调用 `cell`、精确 payload 长度及 `exact_raw_components`。所复用的旧 `exact_oracles(..., candidate=False)` 只在 `reader == W` 分支校验目标 CType/semantic scale/width/mask/type_flag 和 schema scale；O 分支没有同等目标检查。

独立 packed oracle 校验 O 的 raw 与 O 自报 metadata 一致，然后按该 metadata 的 scale 解码，并不核对调用方的目标 scale。静态可达反例：在一个必需的七位成功 oracle 中，只将 O 的 `metadata[0].semantic_scale` 与 `raw_oracle.semantic_scale` 同改为 8，其 100ns 值仍满足 scale8 quantum，bytes、hash、FF9/FF7 和 expected 不变，当前路径仍可接受。O 的 `schema_scales` 或 `type_flag` 单独漂移也未被该路径拒绝。这超出了本阶段唯一免除 TP-04 raw-server provenance 的授权。

最小修复仅在新工具：`exact_actual_raw` 对每个 O/W oracle 显式调用目标 `metadata_list(...,[ctype],[scale],candidate=False)`，并检查单列 schema scale 等于目标；保留当前实际 bytes/hash、格式、packed expected 及普通 TS EXTRACT 强门。新增先接受正常 synthetic control、再重绑报告 hash 的 O scale 联动变异、O type_flag 和 schema scale 负例；O 历史非必需写入的宽松表征不变，O getter 不要求模仿 W CLR 形态。

## source archive 待裁决

新 README 要求真实 candidate 的 source manifest/archive。当前 `validate.py:122–125` 沿旧可选 archive 接口：若文件不存在且 producer 与 frozen 的 archive SHA 同为 null，重绑身份链后仍可通过。现有测试只将 frozen 一侧设 null，能测出不一致，未覆盖完整缺失。

建议 root 明确 TP-03 candidate archive 必需；如确认，仅新 validator 要求文件存在及 producer archive SHA 非 null，并新增删除 archive、producer/frozen 同置 null、相关身份/hash 全部重绑后的负例。无需修改旧工具及旧 baseline 可选行为。此项当前为设计裁决请求，不能将文档宣称的必需性当作代码已经强制。

## 其余静态结论及运行门

- shared/candidate、精确一个 core run、固定 8.1.5.60/msg21/gb18030/ordinary profile、完整 local/UTC/offset 域以及 scales0…7 约束明确；TLS 拒绝。全部 W core specs 的成功或精确 Overflow/CLR constructor 失败独立推导，未通过 baseline 分支接受必需失败；六个 O seeds 和双路 rollback 保留。
- W 公开 typed/generic/GetValue/GetString、FF9/FF7、ordinary TS EXTRACT、双路实际 raw bytes/hash/length/expected 字段及继续 Read 均有强检查；R01 修补不应削弱这些门。
- producer/source manifest、包 SHA/内部 DLL/MVID、实际加载 DLL、assets/lock/deps、offline report/真实 shape receipt、process、ownership nonce/marker 和 fresh cleanup 身份链具备。依赖先验 SHA 后 import，并拒绝旧 loader 的 shadow 路径。
- 内部 package inspector seam 从 Python 参数判断 synthetic，所有注入输出强制 `synthetic_expected_only=true`、`tp03_wiring_accepted=false`；CLI 不提供 seam，默认实包检查拒绝 dummy 包。candidate/full/TP04/TP05 旗始终 false。
- 八个 unittest 方法静态可见正常 control 与变异矩阵；未运行，不声称通过。新修复停写、SHA 复审后，再由 Low 在授权窗口运行离线合同；真实包默认入口及 shared core 实库门仍待完整 TP-03 候选，不由 synthetic 通过替代。

本报告完成后停止写入。
