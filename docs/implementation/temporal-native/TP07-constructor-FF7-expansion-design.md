# TP07 SCREEN25限定接受与FF7扩展设计

> 阅读提示：这是 EF 时间映射的历史设计与验收档案，不是当前 EF 适配指南。固定候选、状态和审批仅属于当时；当前驱动行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。结论：**SCREEN25_characterization_accepted_in_scope / FF7_expansion_design_ready / product_patch_not_authorized / full_C_pending**。本轮只读实际SCREEN25报告、独立validator、执行/清理收据及固定源码，未运行SDK、测试或DB，未改产品、common、旧工具或旧接受旗。

## 1. SCREEN25实际证据与限定接受

以下路径相对 `.local/temporal-native/verification/`，本轮实读hash均一致：

| 证据 | SHA256 |
| --- | --- |
| TP07-constructor-v1-runtime-validation/verification-summary.json | `79a8bb34c223b72063959deb0a03f39a7722dd4d5768c0c9741bfd9cd61b39a6` |
| TP07-constructor-v1-runtime-validation/validation.json | `6be4d0689c05760b15532968bfe0f7f318e2bbb974d60eeb88828bd508096c6a` |
| TP07-constructor-v1-runtime/main/report.json | `b9eb4e9a14a4ffbca86d590a80eab92cd962a4fe28f2023064c9ccda4abcfd21` |
| TP07-constructor-v1-runtime/cleanup/report.json | `0ab176b61da0c9082381332f576cfd10c1ae7f8ad640bb1b21bf758fa1789c05` |
| TP07-constructor-v1-runtime/execution-receipt.json | `b33b8cb9ca165a0a863f8e9d1a6dbc17096a1b299c07b9379543670beb8f5460` |

25项完整有序、main/独立cleanup/validator均exit0，validator issues=[]。四个同表typed7控制全部exact；D1/D2的DT TO_TIMESTAMP FF7/FF9及Z1/Z2的DTO TO_TIMESTAMP_TZ FF7/FF9各3项exact。B0非整micro的DT/DTO共4项LOSS，D3 CAST非整micro2项LOSS；三个整micro对照exact，总计19 exact、6 LOSS。

回读实际字节：tick1控制及D1/D2为 `EA87BBEE81C8000000`，DTO再带offset `4A01`；B0和D3对应fraction字节为零。`.1234567`的FF构造为 `EA87BBEE817899B70E`，B0/CAST为 `.1234560` 的 `EA87BBEE810094B70E`。原列raw、FF9、typed local ticks/offset/UTC相互一致，独立validator按原始字节决定exact/LOSS，未信producer标签。

运行input before/after相同，旧L0失败 `ed5310c8…`保持。两张nonce表由main finally与独立cleanup确认fresh absent/count0，没有恢复待办。settings白名单query真实返回-5504并记录unknown、无fallback或设置变更；**不认定IFUN_DATETIME_MODE=0**。实际physical仍为shared TEST 8.1.5.60/message21/utf-8，结论仅绑定本次运行条件。

接受的是SCREEN25筛选完成和上述观察事实。原L0仍失败，`literal_support_accepted=false/candidate_acceptance=false/full_EF_C_accepted=false`保持；未测边界值、其它offset、DEFAULT、inline、更新表达式，不能立即更改mapping或开放E/D。下一步仅选择native七位文本的**D1/Z1（FF7）**；FF9是已保留的对照，不同时维护两套产品候选。

## 2. 选定表达式与固定值域

选定表达式为：

```sql
TO_TIMESTAMP('<yyyy-MM-dd HH:mm:ss.fffffff>', 'YYYY-MM-DD HH24:MI:SS.FF7')
TO_TIMESTAMP_TZ('<yyyy-MM-dd HH:mm:ss.fffffff> <signed HH:mm>',
                'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')
```

文本由固定case整数值按Invariant格式生成；正负号、四位年份、七位fraction、DTO原offset全部固定，不使用当前文化、隐式NLS格式或先转换成TIMESTAMP literal。NULL使用SQL `NULL`，typed控制使用显式DbType/Scale7和DBNull；NULL不调用字符串constructor，也不证明函数fraction能力。8/9位、TLTZ、历法缺口、域外offset仍关闭。

令 `O=2026-07-23 14:15:16.0000000`，`M=O+1,234,560 ticks`即`.1234560`。`Max=9999-12-31 23:59:59.9999999`。下表是**非笛卡尔积**的有限值表：

| kind / ID suffix | 输入 | 数量 |
| --- | --- | --- |
| DT `tail0`…`tail9` | `M+n ticks`，n=0…9 | 10 |
| DT `min`,`min_tick`,`max`,`max_tick`,`max_9ticks` | ticks=0、1、Max、Max−1、Max−9 | 5 |
| DT `carry_last`,`carry_next` | `O+9,999,999 ticks`，`O+10,000,000 ticks` | 2 |
| DT `NULL` | typed DBNull / SQL NULL | 1 |
| DTO同上18个suffix | tail/carry使用+330；五个极值使用offset0；NULL无offset值 | 18 |
| DTO `offset_0`,`offset_m330`,`offset_m779`,`offset_p840` | `M+7 ticks`，分别0/−330/−779/+840分钟；+330已由tail7覆盖 | 4 |

合计DT18、DTO22，**40个domain pair case**，ID固定 `F7-V-DT-<suffix>` / `F7-V-DTO-<suffix>`。所有DTO先验证本地ticks、整数offset和UTC ticks处在CLR合法域；不能把极值配非零offset制造另一个未授权域。

每pair在同kind新表、不同ID依次执行**typed同值INSERT→fresh exact→FF7/NULL INSERT→fresh exact**，每个输入都有本次同表/同读法的typed对照。typed控制失败就停止该新run，不能把它归咎于constructor。40pair对应80个INSERT阶段；一对不能合并成一个“已观察值”而丢失route/API/row ID。

## 3. 固定SQL上下文门：13个case

完成40pair后按下列顺序执行；这些都是controlled direct SQL，不冒称实际EF生成SQL/HasData/SaveChanges。

| 固定case集合 | 操作与最小证明 | 数量 |
| --- | --- | --- |
| `F7-DEF-{DT,DTO}-{min_tick,max}` | 独立新表V DEFAULT选定FF7表达式；同表先ID1显式typed同值写入并fresh核，再ID2只INSERT(ID)触发默认。DTO为offset0 | 4 |
| `F7-P-{DT,DTO}-{tail7,min_tick,max}` | 在对应domain的FF7持久行做inline正负谓词：同值应count1，相邻1tick异值应count0；同时fresh原列仍exact，行数/schema不变 | 6 |
| `F7-U-DT`,`F7-U-DTO` | 主表专用新row先typed写`M`，fresh确认，再 `UPDATE … SET V=<FF7(M+7ticks)> WHERE ID=<ownedID>`；affected1、fresh精确且总行数/schema不变。DTO +330 | 2 |
| `F7-A-DT-second1` | 主DT表专用row先typed写`M+7ticks`并fresh核；另row typed写期望`M+7ticks+1second`并fresh核，再原row `SET V=DATEADD(second,1,V)`；affected1，fresh原row应精确等于期望同值control | 1 |

相邻谓词值：tail7用tail6；min_tick用min；max用max_tick。只针对已持久的目标ID，SQL不带值参数；DTO使用同offset，仅改变1tick，避免UTC等价的不同offset掩盖比较损失。另记录执行参数count0、精确SQL/hash和真实scalar结果，不能用SQL文本包含FF7替代执行。NULL已在domain gate覆盖，无新增IS NULL case。

`F7-A-DT-second1`来自实际pin `DamengDateTimeMethodTranslator.cs`：DateTime.AddSeconds的整数常量被转换为int并生成DATEADD(second,…,instance)；现E `GeneratedValueCases.cs:48–55`确实依赖该路径。此处只验证其核心服务器表达式 `DATEADD(second,1,V)`，不宣称已执行EF translator/ExecuteUpdate；后续E仍须保存实际生成SQL。只加普通日的1second，**不对Max加法**，不新增DTO DATEADD能力。

assignment的期望同值typed域已由domain tail7建立，仍记录关联的control case/row。DATEADD输出不是原domain表里的carry_next整秒，故必须有自己的typed期望row；不能漏掉这项对照。

**固定总数：53 logical cases = 40 domain pair + 4 DEFAULT + 6 inline predicate pair + 2 assignment + 1 DATEADD。** 细分为92次INSERT、3次UPDATE、12次predicate SELECT（不计fresh oracle/count/catalog/health/cleanup查询）。完整成功后主DT表39行、主DTO表45行，四default表各2行，共92行；各阶段按ledger核增量，cleanup前总数不能替代每次的fresh收据。

## 4. 对象与common接口范围

最多**6张新nonce表**：两张主值表（DT/DTO），四张default表（DT/DTO×min_tick/max）。所有shape仍为现有 `Literal`、ID/V/owner marker/同名PK原结构；scale7不变，不新增schema/history/index/check/sequence/触发器或存储过程。

主表用现有 `RegisterLiteral/Create`。default表使用现有外部CREATE生命周期：`RegisterLiteral` → `BeginExternalCreate` → 实际ExecuteNonQuery/Async → `RecordExternalCreateAcknowledged`（真实call-returned与SQL hash）→ `ConfirmExternalCreate` → Preflight；V后面只额外加入固定 `DEFAULT <FF7 expression>`。原 `OWN_<nonce> VARCHAR(32) DEFAULT '<fullnonce>'`和PK(ID)保持。**不得直接改entry.CreationState或改common去适配新表。** CREATE异常沿现有未知完成状态规则处理，不因catch自动宣称对象归属。

default表实际DDL形状固定为：

```sql
CREATE TABLE <owned>(
  ID INT NOT NULL,
  V <TIMESTAMP(7) or DATETIME(7) WITH TIME ZONE> DEFAULT <approved FF7 expression>,
  <owner_marker> VARCHAR(32) DEFAULT '<nonce>',
  CONSTRAINT <owned_pk> PRIMARY KEY(ID))
```

common Preflight核原列/PK/owner，不包含V的DATA_DEFAULT。新工具若要记录default目录文本，应只用TEST的 `USER_TAB_COLUMNS`、精确本次TABLE_NAME/COLUMN_NAME='V'的只读query；长度有界，只保存本次固定函数/合成日期内容或hash。独立validator核同一column来源与DDL hash，并以**省略V的实际INSERT/fresh值**作为默认生效的决定证据，不能只看目录字符串。若对文本正规化，只允许已冻结的外层括号/非字符串空白处理，不能删数字或忽略不同函数；陌生表示保存pending，不写通用SQL parser。

DDL具有独立提交语义；不把这些CREATE与数据UPDATE混在一个“回滚成功”断言里。主finally和独立cleanup都按6表ledger取fresh absence；失败时已创建的有限子集也必须独立清理，不能为得到完整6表而继续创建。

## 5. 观察、运行与独立判定

继续使用同W包 `0.1.0-temporal.20261008144915` / DLL SHA `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980` / MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`，mapping-v4与EF175保持；新的工具源码/registry/锁/startup/外部收据另冻结，不能回写SCREEN25 pack或使用旧DLL冒充扩展。可新建专用扩展项目沿已有common host兼容名，但manifest task必须区分本run，不增通用runner/mode系统。

开始先TEST身份/physical profile及源包/assembly/deps门。保持shared原连接策略、普通非BDTA；settings已真实unknown，本轮不重复试错读权限、不加fallback、不改变IFUN/NLS/compatible mode，也不把环境未知用0填充。结论继承“本次实际physical条件”边界。

每次非NULLfresh读取沿冻结原列 `SELECT V,TO_CHAR(V,FF9[+offset])`，不在原列上CAST/函数包装；捕获前getter raw/role/type/width/scale/masks，独立整数解码ns/年月日/offset，交叉FF9、typed local ticks、DTO原offset及UTC。Python期望由冻结整数case表独立算，不调用driver encoder或信producer“exact”字段。NULL由工具专门核rows1/is_null、raw cell为空、typed DBNull、FF9 NULL及真实ResultColumn scale7/type26或27；不要调用只接受DateTime的Exact helper处理DBNull，也不修改冻结oracle。

这次所有选定门都期望exact：**任一typed失败、loss、服务器拒绝、oracle矛盾、未知来源或基础设施错误，保存本case原收据、清理并停**；未到达case保持not_run，不能删case或把screen的LOSS可继续规则偷带入本门。前52项（domain/default/predicate/assignment）是literal专属门，最后1项DATEADD是独立R04 translator上下文门。若前52项全通过而DATEADD单独失败，保持整个53门未完成/失败，并分别报告已完整采得的literal证据与R04阻塞；不把DATEADD丢位归因于FF7 constructor，也不自动修改translator。

新validator独立重建53ID及顺序、每pair两个route/row/同输入关联、6表对象上限与允许shape、阶段增量和最终行数、predicate1/0、default省略V、DATEADD输出control、完整source/lock/binary前后身份及历史文件hash。正反合同至少包含：漏typedpair、极值少1tick、DTO只比较UTC、错误offset符号/分钟、NULL错误当0ticks、FF7参数被FF9/旧literal替换、predicate只看SQL文本、default显式赋V而没触发、DATEADD回写整micro、跨表control、清理缺表以及旧报告/旧DLL借用。synthetic测试只作validator合同，不能当53项实库。

## 6. 产品修复与后续E的门槛

本文件只选择FF7进行下一有限验证，不先改 `DamengTemporalParameterFacets.TimestampLiteral`。**52项literal专属门须先有完整独立接受**，root才可另开High的mapping overlay窄窗，将真正GenerateSqlLiteral输出切换到已接受表达式；修改须重新冻结M1相关期待及L0并由Low复验。DATEADD单独失败时，root可在明确接受52项子范围后决定literal窄修，R04 blocker及整体53门失败仍保持；没有子范围接受和新窗口就不提前改产品。

这里的direct INSERT/DEFAULT/inline/UPDATE不是EF HasData、DefaultValue、DefaultValueSql或SaveChanges；实际EF生成SQL、materializer、默认回读/OriginalValues以及E.DATEADD仍由产品patch后的原L0→E/D顺序验证。未覆盖的NULL默认、自动时钟、任意业务SQL和其它版本/configuration不能外推。即使53门全exact，也只有“选定FF7的限定服务器前置门”接受，`full_EF_C_accepted=false`继续保持。

原B0/CAST六项LOSS与原L0硬失败均作为历史输入继续hash锁定；不通过更改原值、降模型精度、换参数写法或重写旧summary制造通过。没有可接受的完整结果时，沿原triage的明确拒绝策略讨论，不默默截断。
