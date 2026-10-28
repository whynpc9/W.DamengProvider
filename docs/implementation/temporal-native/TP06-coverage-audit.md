# TP06 覆盖审计与最小补充验收设计

> 阅读提示：这是时间精度专项的历史覆盖设计，不是当前操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-08。结论：**现有 v11 三 lane candidate gate 是必要门，但不足以单独关闭 TP06**。在其 shared/TLS 结果均通过后，还需下述三组当前候选包实库合同。此次仅审计和设计，没有运行 build/tests/DB，没有修改产品、现有工具、计划或数据。

## 输入身份与已执行离线证据

当前运行输入为 `TP06-v1-package/frozen-tool-working`；实读确认其 Program.cs、contracts.py、validator 与仓库对应文件逐字节一致。SHA256 分别为 `e01dee6daae69272521df4b2bb2544af02818c438a473713fd7fbfae63ed0ff9`、`f8ad9abddd86e415374b9594954f2264fc5d701e3f603c7a96ed77e14dbca6cb`、`7b4f23ee1281bfe4e8bc1a5004f39bcab8b5dce27072df1ce4e00d79ef23e72f`。下文 P 指 `tools/TemporalBoundaryProbe/Program.cs`，V 指 `eng/temporal-precision.validate.py`；编号是本冻结源码行号。实库三 lane 正在独立窗口运行，本报告不预判运行结果。

同一候选 W 为 `0.1.0-temporal.20261008144915`，nupkg SHA `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL SHA `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。来源 manifest SHA `8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f`、archive SHA `836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441`，均来自 package preparation 记录；补充 consumer 必须自行验证该链。

v7 summary SHA `268c9d65c0ad0e07b700becedb37b9ab0d2d209a5dfab5a21d77ef02c42fbfe7` 已接受。本次回读相关 TRX 的实际 Passed 结果：Binding33、Metadata16、FrozenCodec8、ReaderContract355、ReaderRoute13、Core39、Wire9、WriterAdapter32、HelperAdapter29、Interval7、IntervalAdapter93、Fixture1；另 CommandPlan4、TemporalReaderAsync10、R3InternalPagedRead10。它们提供离线合同与合成传输证据，不称为实库观察。

## TP-A01～A13 映射

| 计划项 | 实际 executable / validator 与 v7 离线证据 | TP06 剩余责任 |
| --- | --- | --- |
| A01 极值/末尾tick | P342–381 Core；V466–505强制主七位及整微秒成功、非整微秒拒绝、独立读回；Core/WriterAdapter/Fixture覆盖整数边界 | 等待当前三lane实测，无需复制全极值矩阵 |
| A02 scale/profile/高精度读 | P490–511参数轴、P735–763高scale；V554–708绑定、V828起扩展；Core/Wire/ReaderContract覆盖精确ns和坏声明 | 保留actual-server与internal-only分类；不要求服务器生成其实际不能保留的ns向量，不开放8/9写 |
| A03 Kind/offset/local与UTC | P342–381及ReadOracle445–488；V379–442、466–505；Core与ReaderRoute | 当前三lane足以承担实库必测范围，构造器失败继续单列 |
| A04 DateOnly/TimeOnly/TimeSpan/leading | P791–814只由server表达式seed DATE/TIME/TIME TZ后读取；没有三种原生参数写入，也没有interval实库lane。v7 Core/WriterAdapter/Interval/IntervalAdapter/ReaderContract已覆盖纯合同 | **G3** 补实际参数传输、边界、interval读回；TIME24/>6按v11分类，不新增模式 |
| A05 声明/NULL/稳定绑定/生命周期 | P490–735及V554–708有声明矩阵、真实parameter raw、EncodedScale、replacement/reprepare和独立持久化。P559每阶段无条件Prepare；V630检查实际statement/owner事实，未要求statement ID保持。Binding/Metadata/FrozenCodec/CommandPlan覆盖clone/reset、原声明和冻结 | **G2** 补不手动Prepare的重复执行、自动metadata失效、clone/reset与连接重开代表路径；不重复全部声明轴、不新增并发冻结DB钩子 |
| A06 getter/sync/async/schema | ReadOracle和SimpleLayouts验证同步实际读；355+13+10离线合同含99格、异步/NULL/失败优先级 | **G1/G3** 补真实ExecuteReaderAsync/ReadAsync/GetFieldValueAsync及新原生类型投影；无需把99格全复制到DB |
| A07 普通/紧凑、多行、多结果、SequentialAccess | v11查询是单row或零row；DML drain的NextResult不是多个时间rowset正例。ReaderContract:211直接ApplyResult，Route及Async使用cached rows；R3分页测试是合成channel | **G1-reader** 小规模ordinary跨页/多结果/SequentialAccess。BDTA及非默认mode保持closed/internal-only，不扩大R3/LOB压力 |
| A08 direct/Prepare、sync/async、INSERT/SELECT/UPDATE、commit/rollback | P398–443有显式Prepare及不显式Prepare的INSERT，独立fresh oracles；P869–881只有同步INSERT+Rollback。P无任何Async API、UPDATE、显式Commit | **G1** 必补真实异步UPDATE、显式提交/回滚及不同oracle |
| A09 EF模型与操作 | 非本ADO.NET工具；计划TP07依赖TP06 | 留给TP07，不阻塞TP06为未做EF |
| A10 迁移/scaffold/Down | 同上 | 留给TP07，不在W实库补consumer中实现 |
| A11 culture/text/helper | v7 Core、HelperAdapter、IntervalAdapter、FrozenCodec、ReaderContract的culture/text/旧helper断言实际Passed | 不要求额外culture DB全排列；补consumer预期值不调用产品formatter/encoder生成 |
| A12 CLR外/BCE/1582/坏wire/NULL | P735起server表达式分类及V740/828；v7 Core/Wire/ReaderContract/Async精确拒绝、NULL和恢复 | 保留实际服务器分类；坏wire用离线注入，不向实库注入坏帧 |
| A13 TLTZ/source/session | P770–788、817–838独立seed及读写拒绝，V859–889要求成功seed后的closed读门；跨时区明确closed；Metadata/ReaderContract/Async保raw及NULL | 不要求跨时区正向转换；G2连接重开检查普通已开放绑定失效，不改实例/会话时区 |

## 新 consumer 的共同边界

建议新增独占目录 `tools/TemporalCandidateAcceptance/`，含独立 csproj/Program、固定case manifest、runner、fail-closed validator和工具合同测试；**不修改当前正在运行的v11工具**。High可在独占新文件内编写，源码和工具测试冻结后仍由Low统一串行运行。只用精确PackageReference，同候选shared和TLS两配置；shared可用O独立oracle，TLS仅W+服务器oracle，不宣称O TLS。

可复制v11已经review的最小身份、safe logger、physical profile、TEST身份、nonce/owner-marker及cleanup数据模型；保留其来源注释，并独立审阅新接线。也可让新runner复用只读identity/manifest验证函数，不能放宽v11校验或把主工具产物当新case结果。新工具自身必须验证 source/archive/nupkg/实际加载DLL/MVID/runtime deps、root-approved caps及工具source manifest。public source_commit不是包身份替代。启动即校验caps匹配此包，shared精确utf-8/msg21、TLS精确gb18030/msg11、override为空。

一个独立nonce表即可承载三组：ID、D6/Z6、shared条件D7/Z7、DATE、TIME(6)、INTERVAL DAY(9) TO SECOND(6)、INTERVAL DAY(2) TO SECOND(6)。七位列只在shared承担正向门，TLS的业务值保持六位，七位只测试原本应拒绝的advisory请求。只操作TEST当前Schema的本次对象；DDL在事务测试外完成。沿原精确ownership清理，finally与独立cleanup都核验fresh absence。不要复用其他运行目录的活动表。

每case记录实际API、参数CLR类型、声明、实际profile/source/包、raw与effective元数据（可在合法owner上只读反射）、预期与实际整数分量、结果/异常类型、行数、事务结果、fresh oracle。没有合法观察点时写unobservable并让需要该证据的case失败，不伪造来源。拒绝只包目标操作，先有成功控制；未知异常/缺case/skip/cleanup失败全部失败。只输出安全case IDs和公开合成日期/整数，禁输出凭据、原SQL、异常message。数值预期由手写字段/整数和独立raw decoder生成，不能调用产品encoder/formatter算expected。

## G1：异步事务与ordinary reader（A06/A07/A08）

case IDs按 `{profile}` 和实际 `legacy6` / `shared7` 后缀展开，避免把不支持类型悄悄skip。

- `TX_DIRECT_INSERT_{family}`：不调用Prepare，真实参数INSERT且affected=1；fresh服务器FF9/分量及O/W允许的独立reader验证。**direct仅指调用方未显式Prepare**，不声称协议没有prepare。
- `TX_PREPARED_ASYNC_UPDATE_COMMIT_{family}`：同一行使用显式PrepareAsync，BeginTransaction、ExecuteNonQueryAsync的UPDATE以及显式CommitAsync；新连接确认唯一行的新值。family包含TS/TZ：shared七位值 `.1234567` 和TLS/legacy六位 `.1234560`，TZ保+05:30本地字段及offset，不转换成UTC比较掩盖偏移丢失。
- `TX_ASYNC_UPDATE_ROLLBACK_{family}`：在新事务把上一行更新成不同精确值，affected=1后RollbackAsync；fresh连接仍为已提交值。不能只用count=1判断回滚。
- `ASYNC_REJECT_THEN_QUERY`：合法六位UPDATE控制后，以非整微秒值向fixed legacy6写入，要求Overflow；另typed NULL+不支持Scale7路径要求NS。捕获失败后同一逻辑连接正常执行SELECT 1，并fresh确认原行未变。只有原始操作恢复合同允许的连接状态可用；不手工改Session/替换内部statement掩盖错误。声明无业务Execute须有原诊断发送事实，不把Prepare已发送说成零网络。
- `READER_ORDINARY_{default,sequential}`：对已提交的有限行集调用ExecuteReaderAsync/ReadAsync；按ordinal读取精确typed/generic/GetString/schema，含NULL、TS/TZ。default额外重复cached getter。使用现有允许的SELECT多结果语法，让后一个rowset由TS换TZ（或DATE/TIME），通过NextResultAsync检查新metadata和值；结束、DisposeAsync后同连接SELECT 1成功。加入一个已开放类型的不匹配getter InvalidCast 控制并确认下一列/下一行可读，不需制造服务器subtick值。
- `READER_ORDINARY_FETCH`：用确定性有界行集跨过一次真实Fetch即可，验证页边界前后精确值/行号/总数及实际Fetch receipt；不能只凭行数声称跨页。复用 `R2AsyncProbe/Program.cs:252–295` 的小规模普通VARCHAR padding及opcode7/44只读观察范式，在行中加入精确时间值；**BufPrefetch保持默认**，不启BDTA、不增加LOB/取消压力。以当前读缓冲实际页数设合理上限；未观察Fetch应报告缺口，不无限扩大数据或改产品。

## G2：参数复用、自动失效、clone/reset/新连接（A05）

- `BIND_REUSE_NO_EXPLICIT_PREPARE_{dt,dto}`：同一command/parameter的advisory SELECT先显式Prepare一次，后续调用不再手动Prepare，只换值为整秒、整微秒、末尾tick。shared有效26/27+7稳定且值精确；TLS有效16/23+6稳定，非整微秒拒绝。记录实际statement ID和owner，允许原Reader生命周期合法换statement，不把对象复用冒称物理statement复用。TLS拒绝后恢复如G1。
- `BIND_METADATA_AUTOREPREPARE_{dt,dto}`：成功控制后显式改Scale为3、Precision0，并给可精确表示的`.1230000`，不手动Prepare；新effective legacy type/scale3、raw真实来源和读回同时成立，再恢复6。此项补足v11所有阶段主动Prepare留下的自动失效空白。
- `BIND_CLONE_RESET_REOPEN`：复制一个双显式DateTime2+provider DateTime参数，原参数后续改值/Scale不能改变clone；clone按现有profile正确接受或拒绝。ResetDbType后改为新的明确CLR值重新执行，原显式意图消失；正常关闭/打开无pool连接或更换新连接后再次执行，由新physical identity/raw重新建立绑定。只做代表场景，不重复v11矩阵。TLS显式DateTime2无Scale7兼容默认拒绝必须按原契约；可显式Scale6成功作为其控制。

执行中freeze已由v7 CommandPlan的AfterPlanCaptured屏障、Binding clone/source及FrozenCodec变更反例覆盖；DateTime/DTO/DateOnly/TimeOnly/TimeSpan均为不可变值。**不为此新建实库并发barrier、修改内部hook或业务重放机制**。需保证consumer不会在执行中偷偷替换参数来制造“freeze验证”。

## G3：原生DATE/TIME/interval实际传输（A04/A06）

- `NATIVE_DATE_{min,max,ordinary,null}`：DateOnly.MinValue/MaxValue/普通日期推断或DbType.Date写入真实DATE列；typed NULL显式Date。fresh服务器年月日、独立packed DATE字段与GetFieldValue<DateOnly>一致；同时保留GetValue/GetDateTime既有零点形态。含时间的DateTime→DATE拒绝作为单独零修改负例。
- `NATIVE_TIME_{zero,last_micro,ordinary,null}`：TimeOnly.MinValue、`new TimeOnly(TimeOnly.MaxValue.Ticks-9)`和普通`.1234560`，实际TIME(6)写读；默认GetValue仍DateTime/1900锚点，GetFieldValue<TimeOnly>精确。`NATIVE_TIME_REJECT_LAST_TICK`对TimeOnly.MaxValue要求Overflow，Scale7即使NULL要求NS；fresh无行/无更新。TIME24和n>6继续采用v11服务器分类，不要求正向插入，不开DbTimeToTimeSpan。
- `NATIVE_INTERVAL_{zero,pos,neg,min_micro,max_micro,null}`：DAY(9) TO SECOND(6)，值为0、±(25小时+1234560ticks)、`long.MinValue+8` ticks、`long.MaxValue-7` ticks；真实TimeSpan参数write以及GetFieldValue<TimeSpan>/Async精确。普通default GetValue/GetProviderSpecificValue按实际declared interval现有对象形态验证，与GetFieldType/schema一致，不强开IntervalMode。typed NULL使用明确IntervalDayToSecond声明。
- `NATIVE_INTERVAL_REJECT_{min,max,tick,leading}`：TimeSpan.MinValue/MaxValue、1tick不满足六位，均Overflow；DAY(2)列先99天正例，再100天Overflow。区分用户fraction、列fraction及packed subtype/leading，header/payload不能只报低4位。独立server interval分量或规范字符串及手写24-byte整数字段解码为oracle；不把DmIntervalDT.ToTimeSpanExact自身当唯一expected。

G3每种family至少一条经异步参数写入/异步generic读回，其余值可同步，避免全矩阵重复；正负/极值/NULL全部保留。失败后检查同连接简单查询和fresh状态，最终精确cleanup。若实际server拒绝某个原计划认为支持的native值，保留原错误并回root裁决，不能把任意DmException登记为新unsupported或修改caps让验收通过。

## 接受条件

新consumer及独立validator冻结并由Low运行：两个profile所有适用必需case齐全、自然退出0、strict assertions通过、同包身份一致、每组最终fresh状态和全部cleanup通过。与既有shared/TLS三lane的candidate_profile gate及v7离线证据共同组成TP06；单个工具成功不单独升格为full_temporal_acceptance。TP07 EF和迁移、TP08交付仍独立。所有历史失败、v11运行产物及本设计输入保持不可变。
