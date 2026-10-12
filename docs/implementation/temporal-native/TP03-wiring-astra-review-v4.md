# TP-03 wiring v4 物理 profile 来源同步复核

2026-10-08。结论：**限定静态接受 / wiring_tool_static_accepted**，可交 Low 独立运行 synthetic Python 合同。依据已接受的 [physical v11 交接](TP01-profile-handoff.physical-v11.md)，本次将 wiring scope 同步为 shared 实际 `8.1.5.60 / msg21 / utf-8`；不因历史 logical GB 标签追加条件。真实新候选和 TP03 整体仍待独立验收。

对照 `.local/temporal-native/verification/TP03-wiring-v3/source/`，五项冻结输入实读一致：

| 文件（相对 `tools/TemporalWiringAcceptance/`） | SHA256 |
| --- | --- |
| `validate.py` | `dfa2247a233b19eac4a4c94049ab8d562bbb83bd4bd187fad79eb692b6d4b40f` |
| `test_contracts.py` | `31f0babeaa4711d2d24e5375f4fcb17ef09cb79ceaea31b38840a9893ce10da6` |
| `README.md` | `b71f7f8ce30d931ab5dc79ab5caada88225a2d8f1c2c8328d986f1c48f483130` |
| `contract-fixtures/handwritten-artifact-spec.json` | `ecb577bcd124dc0ee42cb79619bbf6c1742d70bb2564a83bea51bb38fe5ed162` |
| `contract-fixtures/packed_literal_cases.json` | `df97ea26457079c0b5a3465d9191078cb3e4ae4e5e49ba7a4be48be31f8ed148` |

validator 只更新 v11 依赖 pin 为 `7b4f23ee1281bfe4e8bc1a5004f39bcab8b5dce27072df1ce4e00d79ef23e72f`，以及 W run、W cleanup、O run 的物理 profile 校验。该依赖实读与 pin 一致；contracts/packed oracle 两 pin 不变。三个实际 profile 均先调用原 v11 `physical_profile`，强制完整字段集合、两个物理 origin、override为空、有效/协商编码一致、TEST身份和ordinary格式，再强制双方编码为规范化 `utf-8`、message21。私有 serverEncoding 字段来源没有被称为原始 STARTUP bytes。

`strict_core`、双 reader metadata/schema、实际 raw/length/hash/expected/FF9/FF7、普通 TS EXTRACT、archive/source/package/MVID、ownership/cleanup、synthetic seam 与 CLI 接受旗的主体无差异。shared/core-only、真实包默认入口及 candidate/full/TP04/TP05=false 边界不变，TLS未加入本门。

正常手写 fixture 改为 v11 物理 UTF-8 profile；shape 独立写出 divergent logical 输入、零 logical reads、物理来源与十个 capture拒绝案例。新增第十一个 unittest 方法，对 W run/W cleanup/O run 各十种 profile变异（共30）及两个 shape变异作负例；沿既有 helper 先接受正常 synthetic control、再重绑制品hash并断言拒绝。原编码错误负例相应改为 GB；nonce仍为两个16字符块组成的32位大写hex，packed fixture未变。未见门槛弱化或新的静态阻塞。

本次未运行 unittest、build、validator API 或 DB，未修改产品及 TP01-v11 的17项工具。十一方法尚待 Low 的新冻结窗口；synthetic成功不替代真实候选生产入口。v2导入失败、v3记录、TP03-v5真实候选失败与旧profile标签继续保留。

本报告完成后停止写入。
