# TP-03 writer/helper 与最小 reader 接线验收

2026-10-08。root 正式接受 TP-03-v6，状态为 **accepted_for_TP04_TP05_handoff**。这不关闭完整时间改造、TP04/05、同包全矩阵、EF 或发布门。

## 输入与审阅

Sol High 完成主 codec、interval 和窄验收工具，Astra High 完成关键设计及定点代码复审，Sol Low 独立执行全部运行验证。当前工作区与冻结的 368 文件源码闭包一致，没有未结束验证窗口或恢复待办。

- [Astra 最终接受](TP03-astra-final-acceptance.md)，SHA256：6def17711e713cb9c1b91ed54caba7279c889a6997807330720ba448f0217771。
- [下一阶段接口交接](TP04-TP05-v6-implementation-brief.md)，SHA256：9315403039f9b49a966cfe29b1967ad29549680a6fac7953c7ff20201af81dbc。
- 源码以 v6 的真实工作树快照、完整文件哈希和 archive 绑定；Git 基线 fe89bd44 不是未提交改造内容的替代身份。
- 实际 profile 使用 [physical-v11 交接](TP01-profile-handoff.physical-v11.md)。旧 logical 默认编码/版本标签的错误、诊断及失败均保留，不能再作为物理能力依据。

## 实际结果

| 门 | 结果 |
| --- | --- |
| 定向 helper/writer/reader/interval 与旧 golden | 261/261 |
| 同构建完整 TypeTests | 386/386 |
| Command / Async / 相关 R3 diagnostics | 16/16、124/124、83/83 |
| wiring-v4 synthetic 合同 | 11/11，仅测试验收控制流 |
| 同包 shared core-only 实库 | 260 场景；runner、core、独立 cleanup、生产 wiring CLI 全部自然退出0，issues为空 |

离线无 skip/fail/error/abort；实际源快照无漂移。实库核验 WDM_PROVIDER_TEST 身份、shared 物理协商值、包/DLL/MVID，以及精确 nonce 对象清理和新连接不存在确认。只运行本阶段 core，未以此冒称 TP04 metadata、TP05 全矩阵或新候选 TLS 接受。

生产 wiring 结果为 synthetic_expected_only=false、tp03_wiring_accepted=true；candidate_acceptance、full_temporal_acceptance、tp04_binding_acceptance、tp05_complete 均保持 false。

## 同包身份

- 版本：0.1.0-tp03.20261008064351。
- source manifest SHA256：7603bf84e5df9b5d61d42cc9fab8cc204ef0e663dae05e9254fc13056db53ee3。
- source archive SHA256：642934e501a805ce867ecb8bd509225b5a11f8efec9211b098d316c997dfa7e7。
- nupkg SHA256：b0af3de1605bf5a066d7ee4af08d806ae2755d56d1f27385be434e04b778c000。
- 实际 DLL SHA256：4836ab40673c76eeaae5f55191d04ac9406fa21921c2d5be4c9e56ccb4e4b1aa；MVID：2ecd26a7-48dc-4dd8-9fe9-e4cab880b252。
- 同包验证摘要 SHA256：416cbdecaaca92bdbe2e3751119fb800ff73f8d862b44bd01275ace47f4609c2。

[公开证据清单](../evidence/TemporalNative/TP03-v6/public-evidence-manifest.json)包含原报告与制品哈希；完整 core JSON 以确定性 gzip 保存，解压内容与原字节一致。旧编译、模板、profile 来源错误及旧 v5 包全部保留，不回写为通过。

## 文件释放与剩余边界

TP04 获得参数、Command/plan、DmField 与共享 decoder 的独占范围；先交付不可变 binding/read descriptor。TP05 在 descriptor 冻结后接入，独占 DmGetValue、DB2N、DateUtil 与必要 DmDataReader 时间分派/schema，保持既有生命周期。

共享 codec 默认继续冻结。root 批准单独 codec owner 在 TP04 接口冻结后，窄改 DmSetValue 与 DmDateTime 文本入口，使数值、文本和 NULL 真正消费同一冻结 profile/target；不得只增加观察用属性。实际签名和双方归属以交接 brief 为准。

113 签名、legacy12 微秒及 packed 别名、整数转换和显式精度拒绝已进入本阶段合同；稳定默认选型、原 DbType/Precision/Scale 来源、完整 raw metadata、schema/99 getter 格、TLS 全候选和 EF 仍须后续任务完成。八九位写、TLTZ、未知格式及业务迁移未因此开放；未发布 NuGet、未创建或合并 PR。
