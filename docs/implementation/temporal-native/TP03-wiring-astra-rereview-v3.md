# TP-03 wiring gate v3 fixture 定点复核

日期：2026-10-08。结论：**限定 fixture 修正静态接受**，可交 Low 新 v3 Python 合同窗口。未运行测试、构建或 DB；运行及真实候选 wiring 接受仍 pending。

唯一变更为 `tools/TemporalWiringAcceptance/contract-fixtures/handwritten-artifact-spec.json` 的 nonce：从 `1234567890ABCDEF1234567890ABCDEF12` 删除末尾 `12`，成为两个 16 字符块组成的合法 32 位大写十六进制值 `1234567890ABCDEF1234567890ABCDEF`。与 v2 源快照逐行比较仅此一处；实读 SHA256 为 `6dfd41160e88ae87fafc492b5bc040b31144033de451b50dd35c2c852eb49ba0`。

其余四输入实读 SHA 与 v2 完全一致：validator `7027df4b572f1aadf82cdbd570d837b24216607453d610dc14e2429eaa49799f`、tests `ebc87843a01f505c507f6eb5fed04420b4391627c0980ddb9f5bab5f2fc92d45`、README `b04307e2e13501aae1d10502e2087b7303f6f94d7895c8ac3f73607f8ea1c98a`、packed fixture `df97ea26457079c0b5a3465d9191078cb3e4ae4e5e49ba7a4be48be31f8ed148`。生产 nonce 正则、独立 fixture 导入守卫、R01/R02、synthetic 旗及全部测试断言保持不变，没有放宽门槛。

已只读核对 v2 失败摘要 `.local/temporal-native/verification/TP03-wiring-v2-contracts/validation-summary.json`，SHA256 `a9056ba020e09bf1431b7b3d053bff5f2ab24b46dcb33eed84c4a82b12ceb1bd`。v2 在 module import 阶段被正确的 32 字符守卫拒绝，exit1、一个 import error、零个合同方法执行；该失败和旧静态报告保留，不回写为通过。v3 需独立运行十个合同方法，不能继承 v2 的静态结论作为运行结果。

本报告完成后停止写入。
