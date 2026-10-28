# Supplement v7完整运行与v8 TLS scale3读结果窄裁决

> 阅读提示：这是历史验证与工具修订记录，不是当前待办或操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。结论：**shared完整42项已接受；TLS完整采集36项，validator因两个实际legacy读描述差异仍pending**。证据支持只对exact TLS的两个G2自动Scale3成功stage增加已观察的read6备选。v8仅改validator四文件，随后用原同候选包重新完整运行两profile；不建立旧报告overlay，不回写原pending。

本次只读报告、哈希和原始数值证据；没有运行SDK、测试、validator或DB，没有修改产品/工具。

## 1. 输入与运行状态

- 汇总 `TP06-supplement-v7/independent-runtime-summary.json` 实读SHA `fa88e91e7369d50193abe47f39c80ee8d07b0070754e7b0057c91c4bc8c90e8d`。
- shared report SHA `8a0bbd34ab017dcc77de5f522bfd56e03f6f7fe2728cb51d25a8aaf55db3ce9e`；独立validation SHA `40e9bdf2ebd2fe442fa68dddadfe85c4d48712042f58a5f2425f7e149d65414c`，实读为supplemental_profile_accepted、candidate_acceptance=true、42 cases、issues=[]，runner/validator退出0。
- TLS report SHA `cc786a8ed3d35d3219371f4277de505d124df012f497a342a1c9e34b8f748680`，observed_complete/36 cases、runner退出0；validation实际SHA为 `ed124d396d307e3d09c5d97b006d443a41a06167693a96b0a26c713d405e929f`，退出1，首个issue为dt.scale3的unverified_result_descriptor。它未宣称完整TLS接受。
- 两环境owned tables均已fresh确认不存在。W仍为原 `0.1.0-temporal.20261008144915`、nupkg `da8349a2…`、DLL `843710be…`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。TLS实际profile为8.1.4.6/msg11/gb18030、有效与协商编码相同、override为空、ordinary、TEST身份。TLS frozen `build/Cases.cs`实读SHA仍为 `0360d2c3887daf50b6cc08437701ce3fe30aa605c65cde88ae4576497c0fa507`。

## 2. TLS全部G2与两项精确差异

读取全部G2记录：17个stage中14个成功、3个预期拒绝；14个成功中只有以下两个非equal结果，其余12个write/read tuple相同且为legacy6。拒绝stage的fresh六位控制单独保留，不能当被拒绝的输入成功。

| case | 实际stage结果 |
| --- | --- |
| BIND_REUSE_NO_EXPLICIT_PREPARE_dt | second/micro为16/6→16/6；tick预期拒绝，fresh控制16/6 |
| BIND_METADATA_AUTOREPREPARE_dt | control、scale6为16/6→16/6；**scale3为16/3/width8→16/6/width8** |
| BIND_REUSE_NO_EXPLICIT_PREPARE_dto | second/micro为23/6→23/6；tick预期拒绝，fresh控制23/6 |
| BIND_METADATA_AUTOREPREPARE_dto | control、scale6为23/6→23/6；**scale3为23/3/width10→23/6/width10** |
| BIND_CLONE_RESET_REOPEN | 四个成功stage为16/6→16/6；tls_default_explicit_reject预期拒绝，fresh控制16/6 |

两个scale3 stage均真实ExecuteReader、prepare_calls=0、actions=[Value,Scale,Precision]；用户Scale3/Precision0、effective参数scale3及immutable EncodedScale3保持。参数raw为真实ParameterDescribe/flag2，原同族scale6；结果另有真实ResultColumn/flag0、raw/effective scale6、mask0、schema6、ordinary cell及独立server投影，未把advisory原scale6当写列约束。

| 证据 | DT scale3 | DTO scale3 |
| --- | --- | --- |
| 输入及实际typed/default/provider ticks | 639204129161230000 | 639204129161230000 |
| offset | null；DateTime Kind=Unspecified | 330分钟，保本地字段 |
| actual raw | EA87BBEE81F0C003 | EA87BBEE81F0C0034A01 |
| 实读raw SHA | a100bc48c780ecd4e635bec0534695b6217d8a75c24976e4773172c9c9880e0d | 020fa457b96633e76cfdbfcfa0d91457053e1d18feb686fbcd55e69a067dd0eb |
| W GetString | 2026-07-23 14:15:16.123000 | 同值并带+05:30 |
| server FF9 | 2026-07-23 14:15:16.123000000 | 同值并带+05:30 |

独立按legacy位布局核对：date为2026/7/23；time字0x03C0F081EE解出14:15:16及123000微秒，DTO追加little-endian 0x014A=330。原始bytes实际hash已重新核对，fields为123000000 ns，满足用户三位量子；不是精度丢失、七位写入或任意result scale开放。

## 3. leading此次实际走fixed分支

shared与TLS的NATIVE_INTERVAL_REJECT_leading都实际取得ParameterDescribe/flag1/CType21/raw packed1574（DAY2/SECOND6）、mask0。99天control有已发布ReliableFixed=true/leading2绑定，实际写入/读回99天；100天target为OverflowException，business send/response均0，本次binding未发布，fresh原行仍99天。

shared该分支已随完整validator通过。TLS的同分支事实本次也已回读，等待v8完整TLS总门重验，**不是advisory服务器错误待审批分支**；本轮不需要增加或批准任何server错误号。通用未批准advisory号保持pending的防线仍保留，不能把未走到的分支称为已验证。

## 4. root批准方向下的v8最小规则

仅在以下条件全部成立时增加一个read备选，equal原分支继续保留：

1. profile为exact TLS `(8.1.4.6,11,gb18030,encoding_override_empty=true)`，有效/协商编码、physical来源、TEST/ordinary及同包身份均通过既有检查。
2. 实际冻结Cases.cs仍为0360…，SQL/producer未改；成功G2 case/stage精确为 `BIND_METADATA_AUTOREPREPARE_dt.scale3` 或 `BIND_METADATA_AUTOREPREPARE_dto.scale3`。
3. dt的write tuple仍严格16/3/8，只可额外读16/6/8；dto仍严格23/3/10，只可额外读23/6/10。不能把类型升级26/27，不能扩到其它scale/stage/case或错误语义族。
4. parameter、用户Scale3、EncodedScale3、value quantum、actions/Prepare/API/command/parameter/connection及clone/reopen生命周期门全部先按原规则通过。
5. 选择read tuple后仍通过其本次真实ResultColumn raw/effective/schema/cell、独立packet decode、typed/default/provider、server FF9及GetString完整精度检查。不能只凭metadata声称scale6便接受。

新增独立rule ID和本设计pin，并逐stage报告所选write/read tuple及实际observed状态。TLS7写能力、capabilities、shared规则、G1固定DML/G3/reader/拒绝恢复规则都不改；集合外结果继续unverified_result_descriptor/candidate=false。规则名称只描述契约，不推断服务器内部为何返回六位。

## 5. 实施和验证

仅validator owner原四文件（contracts、validate必要接线、tests、manifest）实施；C#producer、runner、Cases源码pin、case/vector/API/SQL、产品包及原evidence不动。不要加入旧报告lineage/overlay或人工重标接受；新freeze经Astra定点复审、Low完整离线后，在新输出目录完整重跑shared42/TLS36并重新独立validate。

最少新增正控包含本次DT/DTO scale3的原始hex/hash、实际read6 metadata/schema、精确值/offset与文本；原write3、EncodedScale3保持。负例包括错误case/stage/SQL来源/physical/override、write被改6、encoded被改6、读结果7或cross-family、raw单位/值/hash、schema、GetString尾零、typed offset/ticks、不同connection文本拼接。现有shared与TLS equal控制保留，新增TLS规则不能误用到固定DML或其它成功stage。synthetic控制不冒称新运行通过。

v7 shared完整接受保留；TLS原pending也原样保留。新v8完整两profile均接受后再由root汇总TP06，不以本次只读审阅替代新规则运行门。
