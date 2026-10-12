# TP03 core 物理 profile 修正定点复核 v6

2026-10-08。结论：**static_review_accepted / runtime_validation_pending**。本次产品修正准确消费 `TP01-profile-handoff.physical-v11.md` 已接受的逐操作物理基线，没有扩大未知能力。可冻结交 Low 全离线及新候选同包 shared 验收。本报告没有运行构建、测试、validator API 或数据库，没有修改 product/test。

对 `.local/temporal-native/verification/TP03-v5/source` 的全部368文件逐字节比较：**365相同、仅下列3文件变化、无缺失**。三个 SHA256 与派发值一致。

| 文件 | SHA256 |
| --- | --- |
| src/W.DmProvider/Internal/Types/DmTemporalCapabilityProfile.cs | 55db8dc195cc443d76911ec571f263feecf414873f969caf0a60b33f241aea90 |
| src/W.DmProvider/Internal/Types/DmTemporalLegacyAdapter.cs | 072021841c0c02b941c0346890531a5786d42bdd156739ba5148df5b8c3a39fa |
| tests/W.DmProvider.TypeTests/TemporalNativeWriterAdapterTests.cs | e11367c6a9dc11c1588b35216fb3c11b04d9a999f3531bfa07fa4a528ba13be8 |

核对结论：

- Profile identity 增加必填 `EncodingOverrideEmpty`，CaptureIdentity 无默认true。实际调用点已传入该事实，immutable identity 可供 TP04 后续冻结。
- Adapter 保持从当前 `GetConnInstance().ConnProperty` 获取单一 physical 对象。仅定点 TryGetValue encoding；值为 string 且长度0才读取 ServerEncoding。非空（包括相同的 utf-8）、空白、缺失、null或非string都不调用该 getter，capture 为 encoding=null/override=false，归入 CompatibilityLegacyUnverified。没有逻辑属性 fallback、产品反射、公开配置变化或字典输出。
- Resolve 精确匹配 `(8.1.5.60,21,utf-8,true)` 为 VerifiedSeven、`(8.1.4.6,11,gb18030,true)` 为 VerifiedSix。旧 shared gb18030、旧 TLS msg21 及其它身份均 unknown；只有编码大小写归一，不按版本大小或编码相似性继承。EvidenceId 与新 physical-v11 交接相符。
- ValidateWriteTarget/ValidateWriteValue 无差异：普通 legacy≤6 和可靠 fixed26/27 六位窄兼容保留；unknown 七位、8/9新写、TLTZ及历法/offset门不变。helper、reader、R3生命周期及 Builder 源文件均未变化。
- WriterFixture 的 logical 与 physical 属性对象现真正独立。回归明确验证 logical 字段被改写不影响实际 profile，以及替换当前 physical 后不继承旧 verified 状态。扩展矩阵包括两真实tuple、旧tuple、邻近版本/msg、不匹配编码和null身份。
- override 八种坏形态在 shared/TLS 上均检查实际 adapter unknown、CLR/text/NULL 七位拒绝，同时保留 legacy6 与 fixedextended6 的 CLR/text 成功控制。原官方 golden、独立精度约束、模板无损、TimeSpan拒绝、历法/offset及高精度关闭断言保留；原成功 fixtures 仅切换至新已证 shared/TLS 身份。

本次没有发现静态阻塞。新测试与共享 fixture 的可编译性/运行结果仍由 Low 新冻结独立验证；不得将本报告或旧v11基线当作新 candidate 成功。旧 TP03-v5 生产失败、v10来源错误报告与所有冻结输入保持原样。
