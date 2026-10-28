# TP06 同包 ADO.NET 最终接受与 EF 激活

> 阅读提示：这是固定候选的历史验收记录，不是当前发布或 EF 兼容结论。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。root 接受 [Astra 最终证据验收（历史）](https://github.com/whynpc9/W.DamengProvider/blob/309110800c4e81f795ff96ee006f58bd12f1952d/docs/implementation/temporal-native/TP06-astra-final-acceptance.md)（SHA256 `7a6263f52c365e6431f10b9ce565da176af4ff459acd8c0e652c480f6e89a97e`），关闭TP06与专项A/B门，激活TP07。C/EF、PR合并、生产发布仍未接受。

同一候选 `0.1.0-temporal.20261008144915` 的388文件源、archive、nupkg、实际DLL/MVID身份保持一致。产品包SHA `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。无产品代码或包的后续漂移。

[主矩阵（历史）](https://github.com/whynpc9/W.DamengProvider/blob/309110800c4e81f795ff96ee006f58bd12f1952d/docs/implementation/temporal-native/TP06-main-matrix-acceptance.md) shared700观察与TLS374观察已接受；补充v8在新目录完整重跑shared42与TLS36，runner、严格validator、独立cleanup全部0，issues为空，TEST身份和fresh owned-table absence均核。补充v8离线187项Python合同与actual C#56检查全过。输入/规则/源前后哈希一致，旧超时、失败、pending和诊断全部保留原归属。

v8 runtime summary SHA `a70b2aa209e937168de1b899a6bc75fbd194bb6e66e78550c6a4282bc5c6ae22`；shared validator `b2cf2edeab8b551c2337a54e68c779ac34ef30f1fd50137be9748dc078c0bcfb`；TLS validator `bcfb9c12c4c63d63b09efd2f7f7657fd309b4447fe2fcf25a5fbf8a7d047148e`。

实际补充覆盖异步事务与fresh值、参数生命周期、原生DATE/TIME/interval、合法SequentialAccess/缓存/真实Fetch/NextResult，以及两个profile fixed DAY2的100天Overflow且业务发送0。写绑定拒绝遵守原R3允许状态；reader转换失败仍要求合法前向读取。TIME/interval七位写、TLTZ值、八九位写和未开放模式没有因此开放；非整tick高精度仅内部fixture，未虚称真实服务器样本。未观察到的alternative仍标未观察。

TP07按 [已准备设计](TP07-astra-design.md) 使用固定EF commit `5863d3755dbd2d333ac359bf1c0a789762adbaed` 的隔离archive/overlay。原EF工作树未提交内容保持，SDK以其10.0.401 pin执行；同一W候选必须贯穿EF runtime及CLI。不得用ProjectReference替换W包、重打另一个包拼接接受，或把本次ADO通过说成EF已通过。

公开证据清单（历史记录已归档）保存67项原始字节导出及各自hash（大报告以确定性gzip压缩），manifest SHA256 `6e97f69aa50254a6d7307571dc5e911a14f6dc13d19d836f4833826dd2f2f561`。包含旧失败、新lineage与两profile完整已接受报告；不包含凭据或候选包二进制。结构化凭据字段/赋值扫描无命中。
