# .NET 原生时间精度改造计划

日期：2026-10-07。版本：v1，待 Kimi Code 独立审阅。状态：design_only；实施未激活，未构建、未连接数据库、未改变现有验收状态。

代码基线：`ed0c8a19d2a3d275eb469960ae56778e6280b5b0`，本工作树初始干净。本文是 T09/T12 时间契约的后续专项设计，不修改 v1 原始规范、历史 evidence 或 T01–T25 进度。实施前须重新核对目标分支、当前文件归属和未提交工作；不能将其他分支的 R3 状态套用到本基线。

## 1. 目标和完成定义

以 .NET 100ns ticks 为 CLR 时间值的统一内部精度，消除数值时间路径的浮点、字符串中转和隐式舍入。目标是：凡输入、目标列、已验证 wire 类型和服务器共同能够精确表示的值，参数写入及读取保持原值；任何不相容的精度、范围和时区语义明确失败。

核心验收是 `DateTime.MaxValue`、`MaxValue.AddTicks(-1)`、`MaxValue.AddTicks(-9)` 在已验证的七位时间戳链路精确往返；六位链路前两项发送业务执行报文前拒绝，第三项精确往返。不能把“没有抛异常”作为通过条件，必须核对服务器值和返回 ticks。

完成分三级记录：A=统一 codec 与既有能力回归；B=已验证 profile 的七位 ADO.NET 完整链路；C=独立 EF 仓库接入及迁移验证。只有 A+B+C 全部门关闭，才可称本专项端到端完成；版本扩展另列 profile，不泛化为所有达梦版本、所有时间类型或所有日期范围。

## 2. 当前证据与实际缺口

| 入口 | 当前事实 | 改造要求 |
| --- | --- | --- |
| `Internal/Types/DmTemporalCodec.cs` | 按 ticks 检查精度；legacy 16/23 最大六位；extended 26/27 的七位仅允许精确版本 8.1.5.60 | 拆开 CLR 精度、列精度、wire 能力及服务器 profile；保持精确拒绝 |
| `Internal/Legacy/Dm/DmDateTime.cs` | 主 getter 用 AddTicks；GetMicroSecond/GetNanoSecond 已为整数；仍有 MathUtil.Round 和字符串转换入口 | 逐入口确认可达性并统一，不通过“搜索结果消失”证明行为完成 |
| `DmTime.cs` / `DmIntervalDT.cs` | TIME 为微秒；interval 已用 BigInteger 处理负值和 MinValue | 复用精度核心，保留类型、leading precision、符号和范围约束 |
| `PublicApi/DmParameter.cs` / `DmSqlType.cs` | DateTime 推断为 DbType.DateTime；DateTime/DateTime2 映射到同一 provider 类型，默认 CType 16 | 默认推断、显式 DbType 和 fixed describe 统一决策；不能仅把 Scale 改成 7 |
| `PublicApi/DmCommand.cs` | fixed describe 保留 16/26、23/27 同族；执行计划冻结参数 | 精度来源也需要可追踪；显式 Scale 不得绕过列精度 |
| `DmSetValue.cs` / `DmGetValue.cs` / `DmDataReader.cs` | CLR 输入先 NormalizeForWire；支持 typed/generic/async getter | 验证所有路径及 provider-specific 值、字符串与 schema metadata 的一致性 |
| `N2DB.cs` / `DB2N.cs` / `OracleDateFormat.cs` | 存在旧格式化、解析、舍入辅助路径 | 划分可达兼容入口与未开放入口，逐一接入或保持明确拒绝 |
| `ExtendedTemporalPrecisionTests` / `FixedTemporalBindingTests` / `TemporalPrecisionProbe` | 已有普通日期第七位 tick、偏移和 O 写/W 读证据 | 未找到上述最大日期三向量专项验收；旧证据不能替代新候选 |

旧 `MathUtil.Round` 在 scale=6 时将末尾 8/9 tick 向上进位，重建 DateTime 可能跨入 10000 年；该机制与用户截图相符，但缺少当时栈，历史原因仍标记为源码推断。当前 CLR 参数路径提前拒绝不精确值，并不等于全部旧公开 helper 已统一。

## 3. 范围和固定决策

- 包含：DateTime、DateTimeOffset、DateOnly、TimeOnly、TimeSpan/INTERVAL DAY TO SECOND；参数推断、显式类型、prepare/reprepare、普通与支持的结果格式、typed/generic/async getter、GetString、schema metadata、可达兼容 helper，以及下游 EF 时间映射。
- `INTERVAL YEAR TO MONTH` 无固定 ticks 对应关系，保持专用类型；TIME WITH TIME ZONE 没有直接对应的 .NET 原生类型，保持现有公开表示和已验证范围，不假造日期。
- 默认严格精确模式；本专项不加入自动截断、四舍五入、饱和到 MaxValue、特殊哨兵或全局连接精度开关。显式有损转换由业务在赋值前完成，未来若产品化则单独设计。
- 保留现有公开类型与签名，优先替换内部实现；不借精度改造开放输出参数、游标、批量/FLDR、数组绑定等尚未开放能力。审核这些入口的能力守卫，防止绕过 codec。
- 不改变 SQL 字面量、CAST、服务器函数的固有计算规则；原始 SQL 由服务器执行，不改写 SQL 以补偿精度。EF literal generator 则属于下游契约范围。
- 不在驱动中自动 ALTER TABLE，不自动加载 SA，不升级数据库实例，不修改实例参数或系统时区。

## 4. CLR 与数据库类型契约

### 4.1 标准表示

纯内部 codec 接受原生 CLR 类型，使用整数日历字段、秒内 ticks、偏移分钟及带符号 interval 分量。DateTime 有效域为 .NET 范围，秒内 ticks 为 0–9,999,999；不将整个 DateTime ticks 乘 100 转为纳秒，以免溢出。仅秒内部分乘 100，纳秒上限为 999,999,900。

解析 wire 时先保留协议原始字段和单位，再校验并转为 CLR；不先截掉纳秒尾数。八、九位值若纳秒不能整除 100，CLR getter 抛 OverflowException。暂不新增公开纳秒时间类型；字符串路径不能通过已损失精度的 DateTime 拼回原值。

### 4.2 精度规则

对于 scale s=0…6，ticks 必须能整除 `10^(7-s)`；s=7 可承载全部 CLR ticks。列声明 s=8/9 与“CLR 需要八/九位输入”不是一回事：CLR 七位值可以精确嵌入更高精度列，但该链路必须单独通过 profile 验证后才开放。scale 位域必须先按具体类型解码；interval subtype/leading 位域不能被统一 mask 破坏。

| 目标 | 精确输入 | 拒绝或限制 |
| --- | --- | --- |
| DATE | DateOnly；零时分秒的 DateTime | 不能自动截掉时间；CLR 范围外日期无法作为 DateTime 返回 |
| legacy TIMESTAMP 16 / TZ 23 | 列精度内、最多六位 | 非整微秒拒绝，即使显式 Scale=7 也不升级 wire |
| extended TIMESTAMP 26 / TZ 27 | 已验证 profile，按列 scale 精确承载 | 未验证版本/能力保持拒绝，不按版本号大小推定支持 |
| TIME / TimeOnly | 已验证六位精度内的日内时间 | 当前不承诺七位；24:00 不取模为次日零点 |
| INTERVAL DAY TO SECOND | 精度、leading precision 和 TimeSpan 范围内的带符号时长 | 不将 months 转 ticks；非整微秒仍拒绝；MinValue 不调用 Math.Abs(long) |

### 4.3 Kind、偏移与历法

普通 TIMESTAMP 存储日历字段：写 DateTime 不隐式转本地时间或 UTC；读回 Kind=Unspecified。保证 ticks/字段，不保证 Kind 往返。DateTimeOffset 同时保持 local ticks 和原 offset，不统一成 UTC；同时校验服务端允许的偏移与 .NET 的 UTC 可表示范围。DateOnly/TimeOnly 禁止隐式时区转换。

数据库 BCE、1582 历法转换区间及 CLR 外年份单列兼容矩阵：不能因日期在 CLR 范围内就承诺服务器精确保留。测试通过前按已知限制拒绝或声明不支持；不得让 MaxValue 成功掩盖其他日期范围缺口。

### 4.4 异常与 Reader 状态

值无法精确表示或超过 CLR 范围抛 OverflowException；能力/类型不支持抛 NotSupportedException；显式类型或精度与 fixed metadata 冲突在绑定层明确拒绝。NULL 保持现有 typed NULL 契约。消息不得包含原值、SQL、连接或凭据。

精度失败应发生在业务 Execute 发送前；Prepare/describe 可以已经发生。读取一个合法服务器值但 CLR 无法表示，属于值转换失败；不得擅自将连接判为 Broken。格式损坏、帧截断等仍遵守现有协议故障规则。测试失败 getter 后下一列、下一行及 Dispose/连接清理行为。

## 5. 参数绑定和能力选择

新增内部“时间绑定描述”，冻结以下信息：CLR/provider 类型来源、用户是否显式设置 Scale（包括 0）、可靠服务器类型/scale、有效 wire 类型、profile 能力、实际编码 scale。执行中不能依赖可变原参数重新推导。

决策次序：

1. fixed describe 是实际目标约束；用户显式类型只做语义兼容检查，不能把列的 CType/scale 覆盖掉。
2. 显式 Scale 保留用户意图：低于列精度时按显式精度检查输入，高于列精度时也必须验证列可表示；高于 wire 能力明确拒绝。已知列精度与参数精度分别存储，不用一个 effectiveScale 覆盖两者。
3. 无 fixed describe 时，显式 DbType.DateTime2/Scale=7 及 CLR DateTime 的完整 ticks 诉求，在已验证 profile 上选择 extended wire/七位；没有七位能力时，仅对六位可精确表示值使用已验证 legacy 路径，其他输入明确拒绝。
4. 新默认推断不得按每次值的小数尾数在 16/26 间切换：由 profile 和声明类型决定稳定的类型/默认精度。参数值替换不导致不可预测的 prepare 类型漂移；显式 metadata 改变触发 reprepare。
5. 无类型 NULL 仍需显式类型或可靠 describe。切换连接/服务器 profile、ResetDbType、克隆和重新 Prepare 后重建时间绑定描述。

能力表替代散落的版本判断，但初始仅承接已有明确验证的 profile；键至少包含服务器版本、wire 类型/模式、支持 scale、偏移与范围限制及证据 ID。建连不新增有副作用的探测，也不靠静默重试业务写入侦测能力。新增 profile 必须以独立探测工具完成读写和 metadata 验证。

## 6. 实施任务 DAG 和文件归属

下列为新专项 TP-00…TP-08；它们不是原 v1 T 编号，不改变历史任务状态。每个任务在根审阅和独立验证后关闭，依赖未关闭的任务不能进入实施。共享文件实行单一 owner、分阶段交接；High 不运行 dotnet/DB。

| 任务 | 依赖与 activation gate | owner / 唯一文件范围 | 交付与退出条件 |
| --- | --- | --- | --- |
| TP-00 冻结契约与入口清单 | 用户激活实施；重核 branch/head/所有权 | root：专项 docs、证据 manifest；High 只读源代码协助 | 全部公开/兼容入口、异常、metadata、历史值类型/字符串契约清单；基线 source/evidence 哈希；未知项有明确验证任务 |
| TP-01 profile 与独立 oracle | TP-00 | Sol High：新 `tools/TemporalBoundaryProbe/`、`eng/temporal-precision.*`；Sol Low 独占运行 | 对旧 W、官方 O、新测试基线分别记录；证明 6/7/8/9 列实际 CType/scale、输入输出、极值、偏移和历法；不支持的 profile 明确关闭 |
| TP-02 纯整数核心 | TP-00；wire 布局沿用已证格式，新能力等待 TP-01 | Sol High codec owner：`Internal/Types/DmTemporal*` 新核心、TypeTests 新 pure codec tests；Luna Max 仅 `tests/fixtures/temporal-native/` | 正反转换、精度/范围、负 interval、边界随机性质测试；独立 golden 非仅自编自解；Low 离线验证 |
| TP-03 writer 与兼容入口 | TP-01、TP-02 | Sol High codec owner：DmDateTime/DmTime/DmIntervalDT、DmSetValue、N2DB/OracleDateFormat 必要路径及对应测试 | CLR、provider helper、显式文本入口无数值精度绕过；旧函数的可达入口有处置；保持公开签名 |
| TP-04 绑定与 metadata | TP-03 | Sol High binding owner：DmParameter/DmCommand/DmSqlType、DmParameterBinding、DmCommandPlan 必要冻结 metadata、FixedTemporalBindingTests | 推断/显式/fixed/advisory/null/clone/reset/prepare 矩阵通过；双约束 scale 和无 Execute 发送断言 |
| TP-05 reader 与格式兼容 | TP-03；共享 helper 写入已释放 | Sol High reader owner：DmGetValue/DB2N/DmDataReader、必要结果格式适配及测试；DmDateTime 修改交回 codec owner | typed/generic/async/GetValue/provider-specific/schema/GetString 契约通过；错误恢复和多行读取通过 |
| TP-06 ADO.NET 同包验收 | TP-04、TP-05；整体 source freeze | Sol Low：仅新 ignored 运行输出；root：新公开摘要 | 七位极值实库、六位拒绝、服务器实际值、事务回滚、对象清理；Type/Command/Async 回归；源/包/DLL 哈希一致 |
| TP-07 EF 与迁移 | TP-06；独立 EF checkout 和目标版本已固定 | Sol High EF owner：独立 EF worktree 时间 mapping/literal/migrations/scaffolding tests；本仓库仅新交接目录 | 真实候选包 SaveChanges/查询/并发谓词/默认值/更新/回滚、旧六位与新七位模型、迁移和 scaffold 通过 |
| TP-08 文档与交付门 | TP-06、TP-07，review findings 关闭 | root：compatibility、专项报告/状态和发布说明 | A/B/C 证据可追溯；无修改旧失败；发行门独立检查；不由本计划自动授权发布 |

主线 TP-00 → TP-01/02 → TP-03 → TP-04/05 → TP-06 → TP-07 → TP-08。TP-01/02 可并行编写；所有构建/实库由同一 Sol Low 串行窗口执行。每轮失败先释放窗口，High 定点修复后新 freeze，禁止在已接受输入上追加修改。

## 7. 具体验收矩阵

| ID | 场景 | 必须断言 |
| --- | --- | --- |
| TP-A01 | MinValue、MinValue+1 tick、MaxValue、MaxValue-1 tick、MaxValue-9 tick；末尾 0…9 tick | 七位已验证范围 ticks 完全一致；六位仅整微秒成功；午夜/闰年/世纪边界无进位或截断 |
| TP-A02 | scale 0…9，legacy/extended，不支持/未知 profile | 精度、能力、范围分别判定；七位 CLR 输入进入 8/9 位列只在独立验证后允许；纳秒尾数 1/99/100 分别拒绝/拒绝/精确 |
| TP-A03 | DateTime Kind 三种，DateTimeOffset 0/+08/-05/+05:30，偏移极限及 UTC 越界组合 | 不隐式转换；TIMESTAMP Kind=Unspecified；offset 和 local ticks 双断言；边界拒绝 |
| TP-A04 | DateOnly 极值；TimeOnly 日内末值；TimeSpan 正负/零/Min/Max/多天；interval leading precision | 不丢日期/时间分量；非整微秒拒绝；负零规范；不溢出或把月视为固定天数 |
| TP-A05 | 显式 DbType.DateTime/DateTime2/provider 类型、Scale=0/6/7、fixed/advisory describe、NULL、同参数换值/重复名称/Clone/ResetDbType/连接切换 | 选定 wire、列 scale 与输入 scale 不混淆；不可表示值在业务 Execute 前失败；无逐行 fallback/业务重放 |
| TP-A06 | GetDateTime/GetDateTimeOffset/GetFieldValue/GetValue/provider-specific/GetString/schema；sync/async | 值、类型、异常一致；GetString 保持前导/尾随零和 offset 契约；NULL 与 DBNull 不混淆；失败 getter 不破坏后续读取 |
| TP-A07 | 已支持普通/紧凑结果格式、多行、多结果、SequentialAccess 与缓存 getter | 单位统一且元数据匹配，跨页仍准确；未开放格式保持守卫，不借机扩大能力 |
| TP-A08 | direct/Prepare、sync/async、真实 INSERT/SELECT/UPDATE、提交与回滚 | server-side FF7/FF9 和分量核对、独立连接读回；读写必须用不同 oracle；服务器表征与驱动结论分开 |
| TP-A09 | EF 新建七位列、现有六位列、参数和 literal、默认/计算表达式、比较与排序、并发 token | SaveChanges/生成 SQL/读回/更新/事务回滚正确；有效精度不因 provider 切换改变；不自动改历史实体哨兵值 |
| TP-A10 | 旧六位列扩到七位、scaffold 后再生成模型、Down 前置检查 | 旧值保持；新精度准确反映；降回六位前发现非整微秒并停止；不提供会静默丢精度的自动 Down |
| TP-A11 | invariant、zh-CN、en-US 等 culture；有效/非法文本；旧 helper 直接调用 | 数值 wire 不经字符串/浮点；格式契约不依赖机器 culture；文本不绕过精度和范围校验 |
| TP-A12 | DateTime CLR 范围外、BCE、1582 转换区间、坏长度/字段/纳秒和 NULL | 精确区分不支持值与损坏协议；不宣称数据库所有值均能用 DateTime 表达 |

合成 fixture 必须携带 case ID、类型、ticks/offset、目标 scale、预期值/异常及 expected_only/not_run 状态；纳秒不精确值使用明确字段向量，不能先经 DateTime 构造。随机测试固定种子并保留最小失败例。无需为本专项启动无关的大 LOB/压力门，但共同代码变更影响到的现有门必须复跑。

## 8. 独立 oracle 与实库运行

纯测试至少包含人工边界算术与历史已验证 wire golden；禁止只验证新 encoder → 新 decoder 自洽。实库使用服务器构造/查询、官方 O 写 → W 读和 W 写 → 独立读三种证据，官方驱动的已知边界失败作为表征保留，不能当作正确性标准。CAST/literal 若自身舍入则单列，不用它代替精确 typed 写入 oracle。

实库先通过 TEST wrapper 验证 `WDM_PROVIDER_TEST` 和目标 profile，再建立 nonce 唯一对象与 ownership manifest。只用当前 schema，DDL 按可能隐式提交处理；事务回滚验证用 DML，不声称回滚能够清除 DDL。finally 精确清理并新连接验证最终状态；失败/超时保留本次对象精确恢复清单，禁止全 schema 清理。

SDK 采用实施时仓库锁定版本（当前 driver 为 10.0.203）；每次设置可写 DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1，使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false` 和 SDK 支持的 `--disable-build-servers`。EF SDK 以独立 checkout 锁定版本为准。Low 记录完整脱敏命令、退出码、计数、profile、source/package/DLL 哈希和最终数据库状态；不输出 secrets、原始认证报文或含凭据异常。

无显式测试连接时只跑离线，状态 `integration_pending`；不同 profile 分别验收。现有 shared 环境 Fetch 超时背景不算本专项通过或失败的自动归因，必须保留当前候选和对照结果。TLS 使用既有严格策略；PlaintextAllowed 仅通过批准的 typed 配置，不能改 O/W 共用 secret。

## 9. EF 及存量迁移策略

独立 EF 仓库不是本次文档编辑的写入范围。实施 TP-07 时固定其 commit、包版本和隔离 worktree，重新检查真实 type mapping；不能假定目前默认一定是六位或七位。

提议对新模型明确采用 TIMESTAMP(7)（以及对应带时区类型），前提是目标 profile 已通过 TP-06。显式 HasPrecision/HasColumnType 优先保留；scaffold 保持数据库真实精度，已有 snapshot 不因驱动升级而批量重写。没有已验证七位能力的目标必须明确配置受支持精度；禁止静默降为六位。

六位→七位迁移是独立 schema 变更：先只读盘点列、默认/计算表达式、索引与依赖、业务精度约束；在 TEST 唯一表验证 ALTER 的支持、锁和数据保持；真实业务迁移由该应用按自身部署流程执行。此前丢失的第七位不能恢复。迁移 Down 必须先检查七位新增数据是否含非整微秒，有则阻断并要求明确的数据处理方案。

“永不过期”哨兵只在该应用明确选择七位契约后决定是否改回 MaxValue；驱动不把 `.9999990` 自动解释为无限，也不自动替换历史值。数据库函数/默认表达式按服务器实际返回精度测试，不由 EF 类型声明推定其精度。

## 10. 兼容性、风险与回退

- 默认推断由 legacy 向 extended 的切换是可观察行为变化，涉及 metadata、prepare 和服务器重载；必须在 TP-04/06 给出旧/新对照和发布说明，不冒充纯内部重构。
- 公开旧 helper 若原先舍入，统一后会拒绝超精度输入；公开 API 签名不变不代表行为完全兼容。TP-00 清单逐项标明改变与回归证据。
- GetString、GetValue、schema 类型及 interval 格式由下游依赖，不能为了统一 ticks 任意变更；新 codec 与公开投影解耦。
- 七位声明不等于全日期范围、所有偏移、TIME 或 INTERVAL 都支持七位；能力按类型/profile 记录。
- 回退采用已固定的前一驱动/EF 包组合；如已写入七位数据，旧驱动读取能力和六位 schema 回退必须先验证。无法无损降级时保留七位 schema/数据并停止回退，不自动截断。
- 线上发布、业务迁移和扩大服务器支持范围分别过门；本计划及 Kimi review 都不等于运行验收或发布许可。

## 11. 交付物与接受清单

实施交付包括：入口与语义清单、profile 能力证据、codec/绑定/reader 实现及测试、合成 fixture、独立边界探测工具、唯一版本候选包、EF 接入提交/交接文件、迁移与回退说明、新 source/evidence manifests、review 处置记录和兼容矩阵。

- [ ] TP-00…08 逐项关闭，A/B/C 结论分开。
- [ ] 最大日期三向量七位正例和六位正负例经真实库及 EF 通过。
- [ ] 所有可达数值时间入口不再做隐式舍入或浮点/字符串数值中转；文本功能保留受控解析。
- [ ] 没有按数值截断掩盖未知能力，未开放入口没有绕过守卫。
- [ ] 受影响现有测试和包消费门通过，所有失败历史保留。
- [ ] 同一冻结候选的代码、DLL、包、EF 消费者身份一致，数据库最终状态确认。
- [ ] 兼容变化、未验证范围、存量迁移和回退前提写入发行说明。

## 12. 参考与审阅状态

本仓库依据：[T09 设计](T09-design.md)、[T09 报告](reports/T09.md)、[T12 报告](reports/T12.md)、[七位精度探测说明](../../tools/TemporalPrecisionProbe/README.md)、[当前进度](progress.json)、[文件与窗口规则](active-ownership.md)。

官方 SQL 文档在 2026-10-07 查询时将 TIMESTAMP 列精度列为 0–9、默认 6；这里只用作产品能力研究入口，不替代本地服务器和协议 profile 证据：[日期时间类型](https://eco.dameng.com/document/dm/zh-cn/pm/dm_sql-introduction.html)。

Kimi Code 独立 review：待执行。修订次数：0。本文的建议和验收断言是实施目标，不是已完成声明。
