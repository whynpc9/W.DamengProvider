# TP06 Astra最终证据接受

2026-10-09。结论：**TP06 A/B同包ADO.NET范围接受，建议root关闭TP06并激活TP07**。既定主矩阵、补充G1/G2/G3及离线合同已共同满足本阶段门，未发现剩余阻塞。**C门/EF、PR合并和发布未接受；production_release_accepted=false。** 各原始制品中的full_temporal_acceptance=false等历史旗保持原值，不回写。

本次只回读现有证据、制品哈希、source与lineage，未运行SDK、测试、validator、consumer或数据库，也未修改产品/工具。

## 1. 唯一产品身份

| 身份 | 值 |
| --- | --- |
| W版本 | 0.1.0-temporal.20261008144915 |
| source manifest | 8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f |
| source archive | 836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441 |
| nupkg | da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d |
| 实际产品DLL | 843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980 |
| MVID | aa23e673-957c-4b1e-bf0f-291ff60077e6 |

source manifest实际包含388项；本次逐项对当前文件做哈希回读，全部一致，archive/nupkg及v8两环境实际DLL亦实读匹配。Low的archive逐字节、实际PackageReference资产/MVID和运行前后无漂移收据完整。fe89是基线/producer标签，不能替代此含未提交实现的完整source身份。

主shared、主TLS和v8 Supplement使用同一W包；不同工具修订与工具DLL单独归属，未与旧v23/TP03包拼接。产品源码v7离线门是源码级证据；v8工具离线及全部本阶段DB consumer另行核验实际候选包，并未把两者DLL说成相同。官方O保持固定8.3.1.47463/net9资产；TLS无O连接接受。

## 2. 已完成门及关键哈希

| 门 | 实际结果 |
| --- | --- |
| 主shared | core260/metadata334/extended106，共700条；v12受审lineage重判candidate_profile_accepted |
| 主TLS | 新采集core131/metadata168/extended75，共374条；v12默认candidate入口接受 |
| Supplement v8 shared | 新完整42 cases；runner/strict validator/独立cleanup退出0，issues=[] |
| Supplement v8 TLS | 新完整36 cases；runner/strict validator/独立cleanup退出0，issues=[] |
| Supplement v8离线 | Python187=validator172+runner15，全通过零skip；真实C#56项通过，实际候选资产匹配 |
| 主verifier v12离线 | 76项Python合同通过，零skip |
| 已接受产品源码v7离线 | binding57/57、reader368/368、reader async10/10、相关时间703/703、完整Type798/798、Command16/16、Async134/134、R3 diagnostics83/83、Transport115/115 |

case/观察计数与重叠测试套件分别报告，不相加冒充独立测试总数。

| 最终证据 | 实读SHA256 |
| --- | --- |
| Supplement v8 runtime summary | a70b2aa209e937168de1b899a6bc75fbd194bb6e66e78550c6a4282bc5c6ae22 |
| Supplement v8 offline summary | 9cf23145db1916546038ace4ee138d4ba8e44eec3d15ca1894a0af156604ee2c |
| Supplement shared validator | b2cf2edeab8b551c2337a54e68c779ac34ef30f1fd50137be9748dc078c0bcfb |
| Supplement TLS validator | bcfb9c12c4c63d63b09efd2f7f7657fd309b4447fe2fcf25a5fbf8a7d047148e |
| 主shared v12重判结果 | 8ec9ae583983ac13f385d6c07cb8d73ebf149c382d81e4f3bd6e6196343ed453 |
| 主shared lineage sidecar | 0817ace39b4e1706b70bdb654b0cb195aaef65041feac95ede8ee00fa9797ceb |
| 主TLS candidate结果 | cf98d22a722d1339f5f1b4bedc9d51a6855c46cb84469143e51397ec6825af13 |

最新summary位于 `.local/temporal-native/verification/TP06-supplement-v8/`；两profile报告分别位于同级 `TP06-supplement-v8-shared-candidate/` 和 `TP06-supplement-v8-tls-candidate/`。两份原始supplemental report SHA为 `2bbb6151ea802156c4dcb23b0a651b9a02f1a1a56783cbad11f462e046218b98`、`ecc75f46e843f46c4547f887cf6d72fdb3a6566d8ac7bf7b8c22724f0e9369a1`；runner/report/cleanup/ownership/final共十项全部按summary逐项实读匹配。

主shared按 [主矩阵接受](TP06-main-matrix-acceptance.md) 保留旧producer和新verifier身份：root lineage `895075207cb116acd7d2dfef31b3c5996f41e3790ad9ab4a6c05457154a07a67`、rules `41757213185e451a0780e3b5fd2c156487b4efe2be87982484fc6600dfe7b276`与sidecar输出hash实读吻合；20项旧输入包括原失败全部重读匹配。主v12离线/重判summary `d66f4efc3a50f8081b336e0de08670a0d75a6a7f911535c0017823457f4a69fe`、TLS runtime summary `a82def4346d4f17afbe0a5db42e9361d469f5658bf968fd7a7fa20038152aaa3`亦匹配。Supplement v8是全新两profile完整运行，没有旧报告overlay。

## 3. 关键运行边界回读

shared实际physical为8.1.5.60/msg21/utf-8，TLS为8.1.4.6/msg11/gb18030；两者effective/negotiated一致且override为空，所有用户/schema均WDM_PROVIDER_TEST。shared显式PlaintextAllowed；TLS为既定RequireTls、本机CA/显式NoCheck策略，没有关闭证书验证。各主lane和Supplement均完成本次nonce对象的主清理与独立fresh absence；v8无recovery_pending，窗口已释放。

v8两环境写本地拒绝均留实际Open→Broken、allocation/prepare共2帧而业务Execute0、旧连接受控InvalidOperation且全部新发送0、真实Dispose及fresh原值恢复。它符合旧R3写拒绝合同，没有声称旧连接仍可复用；reader不适配此退休例外。

两环境普通reader均实际读取两结果集，NextResult opcode44计数2；default缓存重复getter2次、SequentialAccess不重复消费同alias；不匹配getter后下一列/行继续，Dispose后原连接SELECT1成功。128行ordinary数据在第100行边界观察到一次真实Fetch opcode7，随后完整结束。没有启用非默认BufPrefetch或BDTA。

两环境leading均实际是ParameterDescribe/flag1/raw1574的fixed DAY2：99天control成功并有真实已发布binding；100天Overflow且业务send/response0，fresh仍99天。本次无需服务器错误号审批，不把实际fixed成功门误写为advisory pending；未走的advisory未知号守卫仍保留。

主/补充的write与read descriptor分层均经过独立规则门：shared有限同族extended7，以及TLS只限两个AUTO.scale3阶段的legacy read6；用户Scale3/EncodedScale3和原值量子没有放松。interval值文本规范与server显示分别校验，实际raw/typed/声明交叉核对，v6/v7文本规则没有修改产品格式或数值。历史误拒绝保留为原失败。

## 4. 原计划覆盖与接受限制

| 计划项 | 闭环证据及边界 |
| --- | --- |
| A01 | 主core极值/末尾tick、六位精确与拒绝、边界日历；v7整数/独立golden合同 |
| A02 | 主metadata/extended的实际列精度与服务器表征，加v7完整高精度getter/坏声明合同；**没有宣称8/9位真实subtick已被这些服务器保留** |
| A03 | 主Kind、local/UTC/offset矩阵及独立raw/FF文本；构造器不可构造组合仍单列，保守offset域不扩大 |
| A04 | Supplement真实DateOnly极值、TimeOnly六位日末/超精度拒绝、TimeSpan零/正负/整微秒极限及Min/Max拒绝、fixed leading；TIME24/>6按主服务器表征及离线合同 |
| A05 | 主声明/Precision/Scale/NULL/fixed/advisory；Supplement同参数复用、自动metadata失效、clone/reset/新连接；v7冻结描述/原值快照/无部分binding发布合同 |
| A06 | v7原99格及相关typed/generic/async/NULL/GetString/schema合同；主与Supplement实际已开放类型读写、异步getter和前向恢复 |
| A07 | Supplement真实ordinary分页、多结果、SequentialAccess/cache；未开放compact/BDTA保持guard及内部fixture边界 |
| A08 | 主direct/Prepare/同步写读与回滚；Supplement真实async UPDATE/Commit/Rollback、fresh原值、独立server/O/W oracle及状态恢复 |
| A11 | 已接受v7文化、文本、旧helper直接调用及独立整数合同；Supplement实际interval规范/显示双通道，不借工具规则舍入 |
| A12 | 主服务器历法/BCE/year0表征与v7坏wire/超CLR/NULL/精确异常合同；不宣称数据库全部日期可投影CLR |
| A13 | 主TLTZ raw/metadata、独立seed与值读写拒绝，加v7schema/NULL/flags合同；跨会话时区正向转换仍closed |

八九位写、真实不可整tick的8/9位DB向量、未观察的fixed extended六位组合、TLTZ正向、未知ordinary12、BDTA及非默认TIME/interval模式没有新开放。CAST会改变请求的样本按其实际服务器结果表征，不替代严格typed写入oracle。不得将内部synthetic或没有发生的允许备选称为实库能力。

A09/A10属于TP07的EF/迁移/C门，当前仍pending；它们不是TP06 ADO范围的遗留阻塞。此次不要求额外无关R3/LOB压力维度。TP07必须使用同一受验候选包及独立冻结EF checkout；若发现产品缺陷产生新包，按原DAG重新完成受影响TP06门，不能混不同hash。

## 5. 收口

最新v8的preflight/postflight均通过，旧v3–v7工具源drift为空、历史失败与main接受保持原hash。旧超时、主validator读写层次误判、补充恢复/文本/读tuple失败均保留，未伪装成新成功。根设计与已批准有限修复已经分别通过静态审阅和Low独立运行。

据此建议root接受TP06 A/B同包ADO结论、更新任务进度并激活TP07。EF/C、最终完整交付、PR和发布继续独立；本报告不授权发布，也不将原始full_temporal_acceptance=false旗改写为全面完成。
