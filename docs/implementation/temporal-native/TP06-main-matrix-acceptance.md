# TP06 主 shared / TLS 同包矩阵接受

2026-10-09。root 接受两个 profile 的主 candidate 矩阵；**TP06 尚未关闭**，补充 G1/G2/G3、TP07 EF 与最终交付仍待各自门。没有发布包或创建/合并 PR。

唯一产品候选 `0.1.0-temporal.20261008144915`：nupkg SHA256 `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，实际 DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。产品源 manifest `8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f`、archive `836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441`；两环境没有重打包。

| profile | core / metadata / extended 观察条数 | 独立候选结论 |
| --- | --- | --- |
| shared 8.1.5.60 / msg21 / utf-8 / empty override | 260 / 334 / 106 | 700条原始观察按 v12 显式 lineage 只读重判，candidate_profile_accepted |
| TLS 8.1.4.6 / msg11 / gb18030 / empty override | 131 / 168 / 75 | v12 新采集374条，默认原 candidate 入口接受 |

以上是工具case记录数，不将多getter和重叠验证相加称为独立测试。两环境所有lane/runner/独立cleanup自然退出0，先核WDM_PROVIDER_TEST身份，新连接确认本次nonce表均不存在。TLS为 RequireTls、本机CA显式NoCheck，未关闭证书验证；O TLS仍关闭。shared七位写、TLS六位及七位拒绝均按既定能力合同，TLTZ正向/八九位写/非默认模式没有开放。

shared v1的900秒core超时（145项部分观察）保留为失败；清理通过。v2仅用工具原支持的1800秒上限，core1552.842秒、metadata1751.101秒完成；extended也完成。原v11 candidate verifier仍因将六位参数编码等同七位结果列而失败，报告未回写，见[独立裁决](TP06-v2-candidate-triage.md)。实际 GetString七位、ResultColumn raw/Schema七位及整数ticks一致，不需要修改产品。

修复仅涉及主validator与其合同测试：[v12 Astra接受](TP06-main-verifier-astra-review-v12.md)。Low从冻结源码运行76项Python合同（validator46、packed6、producer9、runner15），零失败、零跳过。root独立批准的lineage绑定20项旧输入、7项新verifier文件、原失败、原完整工具表、新selfhash、受审补丁与限定规则；旧producer身份与新verifier身份分别记录。新输出在原目录外，原报告、失败和输入前后哈希相同。没有仅为校验规则纠正重新发送shared数据库操作；新规则没有放宽请求/EncodedScale/fixed/core/extended门。

| 核心证据 | SHA256 |
| --- | --- |
| 原v2 shared失败 | a7a3134f237caae8334631389f76d3bccc93f27bfc4d790d8dd557fb4e6588f7 |
| 原v2 runtime summary | 5ee729b855504e034e821def8b908c099c14b627537698f3bc39a60c78f0f18e |
| root lineage | 895075207cb116acd7d2dfef31b3c5996f41e3790ad9ab4a6c05457154a07a67 |
| 新shared candidate report | 8ec9ae583983ac13f385d6c07cb8d73ebf149c382d81e4f3bd6e6196343ed453 |
| 新shared lineage sidecar | 0817ace39b4e1706b70bdb654b0cb195aaef65041feac95ede8ee00fa9797ceb |
| v12独立offline/revalidation summary | d66f4efc3a50f8081b336e0de08670a0d75a6a7f911535c0017823457f4a69fe |
| TLS candidate report | cf98d22a722d1339f5f1b4bedc9d51a6855c46cb84469143e51397ec6825af13 |
| TLS runtime summary | a82def4346d4f17afbe0a5db42e9361d469f5658bf968fd7a7fa20038152aaa3 |

本地证据位于 `.local/temporal-native/verification/TP06-v2-shared-candidate/`、`TP06-v12-shared-revalidation/`、`TP06-main-verifier-v12/` 与 `TP06-v12-tls-candidate/`，相应外部summary/failure保留。完整Source/Package/工具/规则链在这些记录中可追溯。下一依赖满足任务仍为Supplement离线及同包实库验收，不能用本报告替代补充覆盖或提前激活EF实施。
