# TP04 / TP05 离线验收与同包交接

2026-10-08。root 接受统一 v7 的 TP04、TP05 **offline_accepted**，激活 TP06。同包全实库、TLS 新候选、EF、发布及 PR 合并仍未接受。

Sol High 分别完成 binding/metadata、冻结 writer 消费桥、reader/schema；两位 Astra 对冻结源及定点修复完成复审，Sol Low 独立运行九门。所有相关 owner 已停写。当前工作区与 388 项冻结输入一致，旧编译和测试失败均保留。

| 实际运行门 | passed / executed |
| --- | --- |
| TP04 binding / metadata / frozen codec | 57 / 57 |
| TP05 reader contracts / routes | 368 / 368 |
| TP05 async contracts | 10 / 10 |
| 相关旧 temporal targets | 703 / 703 |
| 完整 Type | 798 / 798 |
| 完整 Command | 16 / 16 |
| 完整 Async | 134 / 134 |
| 相关 R3 diagnostics | 83 / 83 |
| 完整 Transport | 115 / 115 |

全部正式 TRX 自然退出0，无 skip/error/timeout/abort。目标与完整套件有重叠，不将表中数量相加称为独立用例数。99 个 reader 契约 ID 及其 NULL 对照已逐项关联实际 Passed testId；原未运行 coverage 设计输入不回写，运行结论另存 [执行映射](../evidence/TemporalNative/TP04-TP05-v7/TP05-reader-execution-mapping.json)。

- [TP04 Astra 最终接受](TP04-astra-final-acceptance.md) SHA256：2d469bf2f4251edb21ca5262d7edfaa0527eca885525fa6273ee70c4dba56bc4。
- [TP05 Astra 最终接受](TP05-astra-final-acceptance.md) SHA256：196a37c336ce2cf789b1a5b11ae91a594fb35e5e6c112dd986f5ffe232a6e205。
- Low 运行摘要 SHA256：268c9d65c0ad0e07b700becedb37b9ab0d2d209a5dfab5a21d77ef02c42fbfe7。
- 99 格执行映射 SHA256：fe0d7d96d1ac1704f61406991695a7694cb4a82790d383ca5da5cc55765ff7a0。
- 源 manifest SHA256：a5bef77bb9af40c78bf9feaff1e490c717049d4084677a1646fbf03c4227b5a2。

[公开证据清单](../evidence/TemporalNative/TP04-TP05-v7/public-evidence-manifest.json)保存源清单、命令计划与运行摘要。完整本地 TRX 的哈希在摘要中可追溯，实际程序集哈希已核对。

接受范围包含真实共享 decoder 的 raw capture 合同、冻结请求和 profile/target 的 writer 消费、稳定选型和独立精度约束、不可变读 descriptor、精确文本/CLR 拒绝、schema 与恢复的离线合同。缓存行/内部 ApplyResult fixture 不等于真实网络分页或完整 public NextResult 证据；既有相关回归保持通过。

TLTZ 正向、八九位写、非默认 Builder 能力没有开放。非整 tick 的八九位值、未观测的 fixed extended 六位组合及 BDTA 等范围按既定 internal-only/未开放标识保留，不冒称已由本库实测。能力身份采用 physical-v11 的实际协商来源和必填 EncodingOverrideEmpty。

TP06 必须从此精确源码生成新的唯一候选包，核验源/archive/nupkg/实际 DLL/MVID，并让同一包通过 shared/TLS 完整 core、metadata、extended candidate gate及所有清理。不能用旧 TP03 包、旧 logical profile 标签或本次离线通过替代。
