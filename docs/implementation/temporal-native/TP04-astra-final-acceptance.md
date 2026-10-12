# TP04 最终离线接受

2026-10-08。结论：**TP04 offline accepted / accepted_for_TP06_candidate_integration**。此前 R1/R2 静态问题、v3 重复属性编译修复及 v5 两项测试修复已完成定点复核；现有 Low 独立运行证据支持关闭 TP04 离线门。建议 root 在 TP05 自身接受完成后激活 TP06 同包集成验收。

本次仅回读最终证据，没有重新扫描产品或运行构建、测试、数据库。实读 `.local/temporal-native/verification/TP04-TP05-v7/validation-summary.json`，SHA256 为 `268c9d65c0ad0e07b700becedb37b9ab0d2d209a5dfab5a21d77ef02c42fbfe7`。source manifest 实读 SHA256 为 `a5bef77bb9af40c78bf9feaff1e490c717049d4084677a1646fbf03c4227b5a2`，388 文件；Low 记录 source/workspace drift 均为空、窗口已释放、无 recovery pending。

九份 TRX 已逐一回读：文件哈希、计数均与摘要一致，各测试命令退出0，全部 executed/passed，无 failed/error/timeout/abort/notExecuted。TP04 binding/metadata/frozen codec 为 **57/57**；相关时间合同703/703、完整 Type798/798、Command16/16、Async134/134、R3 diagnostics83/83、Transport115/115。另两项 TP05 定向门368/368及10/10也在统一证据内，但其功能接受归 TP05 独立报告。本表述不将重叠套件数量相加为独立场景数。

摘要列出的所有测试及产品 DLL 哈希已实读吻合；五个测试输出目录中的产品 DLL 均为 `1642255b16a2e9b4e4bf9c0d4eaa4ee8654acde9e7ebb07f7119b8a7c39c6aaf`。SDK 记录为10.0.203。本次没有产包或实库执行，summary 中待 root 接受旗与旧失败记录保持原值，不回写历史证据。

TP06 必须以当前接受源码生成并锁定同一个新 candidate 包及 DLL 身份，验证真实服务器 metadata 来源、稳定参数选型、三种 scale、typed NULL、完整写读与恢复等集成合同；沿 physical-v11 的 exact scope 和现有 TEST/TLS 约束取证。不能用 TP03 旧包或离线合成报文代替这些实际观察。TP04 的实库 binding acceptance、完整 temporal acceptance 和发布接受仍未成立；八九位写、TLTZ 与未知布局的能力边界不变。所有旧编译失败、v4测试失败及后续 fixture 修复证据继续保留。
