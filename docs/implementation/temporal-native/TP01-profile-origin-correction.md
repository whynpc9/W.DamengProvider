# TP-01 profile 来源修正：待重新取证

日期：2026-10-08。状态：physical_profile_revalidation_pending。

TP-03-v5 候选的同包 shared core 完成 260 个场景并清理成功，但七位写入被 NotSupported 拒绝，生产 wiring validator 未通过。定位后发现 TP-01-v10 的 ProfileInfo 混合了两个来源：版本来自实际物理连接，编码和 message version 来自逻辑 ConnProperty。逻辑对象的默认编码不能证明物理连接的协商编码。

对同一确切候选包的只读诊断确认：

| 字段 | logical | physical |
| --- | --- | --- |
| ServerVersion | null | 8.1.5.60 |
| message version | 21 | 21 |
| effective encoding | gb18030 | UTF-8 |
| 内部协商 encoding 字段 | gb18030 | UTF-8 |
| encoding override 为空 | true | true |

实际产品 adapter 从 physical 对象得到 utf-8，因此按原表进入 CompatibilityLegacyUnverified；七位目标被拒绝。诊断仅执行 TEST 身份查询和纯目标检查，没有业务写入、DDL 或对象创建。

诊断原报告 SHA256：5b84c738838a9603796566b145f0bd6a40345ae42c81971ca1e7bd664d81af50；独立验证摘要 SHA256：86703b901aea85112d0968bb3dbc13d0a8d8c002015f4660dd65eb7344268e68。原候选实库失败摘要 SHA256：fb6bcd6aa5f5254528a3e1681c8d6b69d886bc162013aa5c9f0522ec462c9461。

本说明修正此前 [TP01 接受记录](TP01-acceptance.md) 和 [profile 交接](TP01-profile-handoff.final.md) 的物理编码来源判断。旧报告、通过的数值观察、失败和哈希均原样保留；gb18030 标签不得继续作为已验证物理能力的依据。TP-01 的 profile 交接重新取证，TP-03 保持未接受，TP-04/05 不激活。

v11 只修探针来源及对应合同：版本、message version、有效编码和时区来自同一个已打开 physical 对象；协商编码另标为其内部字段来源，不冒称原始 STARTUP 字节。编码覆盖必须为空，有效与协商编码仅大小写归一后相等；缺信息、不一致或 logical fallback 必须失败。先以旧 v23 原包重新完成 shared/TLS 基线，再按实际结果批准精确 scope、修正产品表并生成新候选，不能先扩大编码白名单。

## 后续状态：v11 重新取证已接受

新 shared 700 场景、TLS 374 场景及全部清理/validator 均通过。逐案例能力审阅见 [物理 profile 最终交接](TP01-profile-handoff.physical-v11.md)，公开原报告见 [v11 证据清单](../evidence/TemporalNative/TP01-v11/public-evidence-manifest.json)。上述初始 pending 状态保留为问题发现时点；当前以新交接及专项 status 为准。产品仍须按修正范围生成新候选并重新通过 TP-03 实库门。
