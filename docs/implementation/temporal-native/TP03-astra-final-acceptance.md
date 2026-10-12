# TP03 最终审阅与依赖交接

2026-10-08。结论：**TP03 scoped acceptance recommended / no remaining review blocker**。core v6、interval R01 v2、wiring v4 的静态闭环与 Low 新冻结运行证据一致，可由 root 接受 TP03 并按文件归属交接 TP04/TP05。这里只读核对既有报告、实际制品、TRX 和接口；未运行 build、tests、validator API 或 DB，未修改产品或旧冻结报告。

## 接受依据

- [core v6](TP03-core-astra-rereview-v6.md)、[interval v2](TP03-interval-astra-rereview-v2.md)、[wiring v4](TP03-wiring-astra-review-v4.md) 对应源码/工具身份及修复范围保持。v6 source manifest 的全部冻结文件与当前文件逐字节哈希一致；package manifest 的产品/测试也无漂移。未重复已完成的全代码扫描。
- Low 离线 target **261/261**、同构建完整 Type **386/386**、Command **16/16**、Async **124/124**、R3 reader/diagnostics **83/83**，退出码均0。实读五份 TRX，哈希及 Counters 与 summary 相同；target 是完整 Type 的子集，不累计为独立总数。wiring synthetic **11 方法**通过，但独立保留 synthetic-only 范围。
- 唯一包 `W.DmProvider 0.1.0-tp03.20261008064351` 在 shared 完成 **260 core cases**；runner/production validator 退出码0，`issues=[]`、`tp03_wiring_accepted=true`、`synthetic_expected_only=false`。O/W 实际加载资产与同包记录匹配；物理身份均为 `8.1.5.60 / msg21 / utf-8 / encoding_override_empty=true`，用户/Schema 为 `WDM_PROVIDER_TEST`。独立新连接确认 `owned_table_absent_fresh_connection`，DB窗口已释放。
- 实读 core、cleanup、runner、validator JSON、产包 source archive/manifest、identities/capabilities 及 nupkg 哈希均与安全 summary 相符。v5 失败 summary 哈希仍匹配，历史失败未被覆盖。

| 核心证据 | SHA256 |
| --- | --- |
| `.local/temporal-native/verification/TP03-v6/validation-summary.json` | `abb89bf3088b5a3cb499f4506e6165af13d9ec0f61bb6582e29b2950ac2fd310` |
| `TP03-v6/source-manifest.json` | `60a812bfa42ff8deab78aea325e50e92b0123bd634d92b5d6dbce08aa9e22206` |
| `TP03-v6-shared-verification-summary.json` | `416cbdecaaca92bdbe2e3751119fb800ff73f8d862b44bd01275ace47f4609c2` |
| `TP03-v6-shared-wiring-validation.json` | `1ed2cba8771d4f272e30be0847d58bc12feca82e381f922359b4deb2cae3caa2` |
| `TP03-v6-package/product-source-manifest.json` | `7603bf84e5df9b5d61d42cc9fab8cc204ef0e663dae05e9254fc13056db53ee3` |
| W nupkg | `b0af3de1605bf5a066d7ee4af08d806ae2755d56d1f27385be434e04b778c000` |

表中简写路径均相对于 `.local/temporal-native/verification/`。候选来自冻结的未提交工作树；base HEAD `fe89bd44` 不是“干净 HEAD 已包含本改造”的证明。实际 W DLL SHA `4836ab40673c76eeaae5f55191d04ac9406fa21921c2d5be4c9e56ccb4e4b1aa`、MVID `2ecd26a7-48dc-4dd8-9fe9-e4cab880b252`。

## 范围与下一窗口

这是 TP03 native helper/codec、typed Reader 接线及 shared core-only wiring 的接受建议。保留原证据中的 `candidate_acceptance=false`、`full_temporal_acceptance=false`、`tp04_binding_acceptance=false`、`tp05_complete=false`。本候选未执行 TLS、metadata/extended lanes，不构成 TP06 同包全门、PR/merge 或发布接受。8/9 新写、TLTZ 值、TIME>6 等能力不因本报告扩大。

root 接受后可释放 `DmGetValue.cs` 给 TP05 reader owner；TP04 独占 DmField/decoder/参数/command/plan。TP05 必须先取得 TP04 immutable raw/read descriptor 的真实签名和文件 SHA，不能抢写 DmField/decoder。codec owner 保留 DmDateTime/DmSetValue/adapter 与共用 helpers，等待 TP04 binding 接口冻结后安排单独窄窗。实施前最小更新见 [v6 实施交接 brief](TP04-TP05-v6-implementation-brief.md)；旧设计及旧证据保持原样。
