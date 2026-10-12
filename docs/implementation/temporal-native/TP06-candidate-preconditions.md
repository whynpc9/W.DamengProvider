# TP06-v1 同包验收前置决策

2026-10-08。root 已批准本轮测试的能力前置配置；这不是 candidate/full-temporal 验收结论。TP04/TP05-v7 源及工具保持冻结。

唯一候选为 `0.1.0-temporal.20261008144915`，包 SHA256 `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`；实际 DLL SHA256 `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。388 文件 source manifest SHA256 `8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f`，archive SHA256 `836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441`。当前产包来源明确为 fe89bd44 基线上的冻结工作树，不伪称已提交 refactor。

能力来源为已接受 physical-v11，不使用逻辑连接默认值。两profile都要求实际物理协商信息、空编码覆盖以及 ordinary 非 BDTA。未知profile不据版本大小推测七位支持；TLTZ正向、八九位写和非默认模式继续关闭。

| profile | server / message / encoding | 本轮预期写入 scale |
| --- | --- | --- |
| shared | 8.1.5.60 / 21 / utf-8 | 0…7 |
| tls | 8.1.4.6 / 11 / gb18030 | 0…6 |

每个profile只接受其原v11 extended中八个确证服务器拒绝的case/error配对：year_zero和time24_scale6对应-6118；time24_scale7/8/9与time_fraction_scale7/8/9对应-6121。须同时有verified_server_response、实际安全错误字段及精确case ID，不能将其它DmException或超时归为已知不支持。原始报告SHA：shared `375ae551c020f9688c0054dd988892eba162613ab42b8f03641531f70ff599d6`，TLS `9b6327cf417e97927b0b22ce4623689d3e157d91424e074e444617306f52b08a`。

新增root批准配置SHA256 `f10acfd86b5e7158eac0a0aa8cfb972418881837b4bfe63205245d82a08f54fa`，原draft保持原状。配置同时绑定source manifest/head及包SHA。

Sol Low独占shared→TLS串行完整core/metadata/extended窗口，使用原完整validator `--gate candidate`；不能拿TP03窄wiring门替代。各profile先核TEST身份，每lane唯一nonce对象、finally精确清理及fresh connection absence。首失败保留证据并释放，不自修重跑。Astra另只读核验TP-A覆盖；未覆盖的计划必需项须在同一候选上补齐后才能关闭TP06。
