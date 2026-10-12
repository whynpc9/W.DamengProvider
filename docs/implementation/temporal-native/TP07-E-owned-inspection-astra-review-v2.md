# TP07 E 单对象只读检查工具 v2 静态验收

2026-10-09。结论：**scoped_static_accepted_for_Low_build_only / actual_catalog_pending / residual_presence_unknown**。仅接受固定单对象只读检查工具进入 Low 独立冻结/构建门；没有授权执行DB或清理。本文只记录已完成的源码、静态diff和hash审阅；未运行SDK、测试、工具或DB，未修改实现、旧ledger或失败收据。

工具目录：`.local/temporal-native/verification/TP07-E-owned-inspection-v1/`。v1唯一确定编译问题是 `LogLevel.OFF` 缺少实际声明所在的 `W.Dm.Config` 命名空间引用。Low已将原三文件保存在 `TP07-E-owned-inspection-v1-freeze/source/`，原manifest SHA为 `fb23522371501b71f1a2a4fed3a7e4d09d7713cc959c2b278d1dcbfbcc9fb16d`，原Program SHA为 `847a2b89d0a581bcd389e926dda2db851aebdb5e2269f6d53537b5ddf388810e`。

v2与该原快照的实际diff只有一行 `using W.Dm.Config;`，关闭上述源码层编译阻塞；另外两文件不变：

| 文件 | v2 SHA256 |
| --- | --- |
| `Program.cs` | `74ca3474cbff3b758ba7bf76dd51f94759e01ff0c7c976a76435bd0820811a41` |
| `TP07EOwnedInspection.csproj` | `5cf9479a8265d9af823b91aa3b92237862f4926786cfad0476caf89139c5a001` |
| `README.md` | `a7bf34e6e58ae82035afb1cdfdec193de405b634d177442d25982ee21dcfc630` |

Low v2独立快照位于 `TP07-E-owned-inspection-v2-freeze/`，manifest实读SHA为 `d1d848f5b127d789171b2ef72e4f0553bb2c45498424ffdd1184dcfd3466e0f6`；三份snapshot字节逐项匹配上表。v1 manifest继续独立保留，不冒充v2冻结；后续实际执行另需绑定v2源及编译DLL/MVID/deps。

只读对象固定为 `WDM_PROVIDER_TEST.TPL_406DE87001DF440DA7BA4669`，完整nonce `406DE87001DF440DA7BA466973AC639C`，PK `TP07PK_406DE87001DF440DA7BA4669`，无任意table/schema参数。代码在Open前硬核原ledger `10f0c6e81f6c1d91f51cf0424b78c41905f4606d2e6e168144e690a4480430d1`、CREATE SQL `01060cda014a4ebe81fd9d3cdc69fec2103bdcd28a36ca99b450babf54e3476e`、runtime报告 `8b142476d1e42461c16d6235ad298398438994b2e4a664ad91dad72a77a28503` 和E startup `92f034035c373bf4e9c8329d509035d0f59fd6f79aaa42a695746765e7050469`。CREATE文本仅hash读取，绝不执行；absence-before与CREATE ACK、未完成COMMENT的原状态不改。

实际W包/asset/DLL/MVID、candidate/capabilities和外部批准的检查工具DLL/MVID/deps都在连接前检查。只消费原TEST wrapper变量，显式shared PlaintextAllowed、禁池和日志；开连接后先核TEST user/current schema及8.1.5.60/message21/utf-8/override-empty物理身份，再读固定目录。

全部数据库语句为固定SELECT：表presence、四列基础元数据、可选CHAR_LENGTH/CHAR_USED、此表约束/约束列、index/index列、trigger、注释；目录用精确table bind，注释另限定TEST owner。只有presence1才对固定quoted table做COUNT(*)，不读取业务值，最后再读presence。注释仅输出NULL/期待marker相等/有界hash，其他目录值受类型/长度/字符范围限制；不输出异常Message、连接串或认证信息。

可选CHAR查询仅在明确负号server错误且FailureInfo为ServerReported、ConnectionReusable=true、CancelSource=None时记录unavailable；其他失败或不支持值类型导致检查不完整。没有OwnedObjects调用，没有CREATE、COMMENT、ALTER、DROP或DML，不会把硬编码read_only旗当成清理结果。现有主/独立cleanup失败及原summary `bc80be3a19598326852b4be444ffecff578773542fbdbbf2a535d9667df98352` 保持原事实。

当前源码证据为实际DDL `N VARCHAR2(32 CHAR)` 与共享 `VerifyStructure` 的 `DATA_LENGTH==32` 要求不一致；**实际catalog尚未查询，不能断言具体DATA_LENGTH或唯一根因，残留presence仍UNKNOWN。** 若目录确认N是唯一偏差，优先仅将E合成辅助列显式设为`VARCHAR(32)`并先离线核真实GeneratedCreate SQL；这不自动修复或授权删除历史残留，也不要求放宽共享ownership。现有对象恢复须另以实际catalog、原ACK/nonce/PK/DDL等证据审阅。

Low后续build-only沿README固定SDK10.0.203、精确同W PackageReference和单节点/禁server环境约束，完成后冻结新制品。只读DB窗口仍须root另行开启；E/D新DB暂停、完整EF-C pending状态不因本文改变。
