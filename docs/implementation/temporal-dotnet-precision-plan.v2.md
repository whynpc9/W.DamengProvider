# .NET 原生时间精度改造计划

日期：2026-10-07。版本：v2，已根据 Kimi Code 对 v1 的 8 项意见完成一轮主审阅修订。状态：design_review_revised / implementation_not_started；未构建、未连接数据库、未改变现有验收状态。Kimi 没有二次审阅 v2，本版本不标记为 Kimi 复审通过。

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
- `INTERVAL YEAR TO MONTH` 无固定 ticks 对应关系，保持专用类型；TIME WITH TIME ZONE 没有直接对应的 .NET 原生类型，保留现有 1900-01-01 锚点的兼容投影和已验证范围，不引入新的任意日期映射。
- TIMESTAMP WITH LOCAL TIME ZONE（TLTZ）单列契约：保留已经验证的会话/数据库时区转换语义；必须保留 `mask`、`0x1000` 等原始 metadata 的来源。识别出 TLTZ 但对应 profile/格式未经验证时，读写两侧明确 NotSupportedException，不能当普通 TIMESTAMP 返回。当前普通时间戳七位证据不自动开放七位 TLTZ。
- 默认严格精确模式；本专项不加入自动截断、四舍五入、饱和到 MaxValue、特殊哨兵或全局连接精度开关。显式有损转换由业务在赋值前完成，未来若产品化则单独设计。
- 保留现有公开类型与签名，优先替换内部实现；不借精度改造开放输出参数、游标、批量/FLDR、数组绑定等尚未开放能力。审核这些入口的能力守卫，防止绕过 codec。
- 不改变 SQL 字面量、CAST、服务器函数的固有计算规则；原始 SQL 由服务器执行，不改写 SQL 以补偿精度。EF literal generator 则属于下游契约范围。
- 不在驱动中自动 ALTER TABLE，不自动加载 SA，不升级数据库实例，不修改实例参数或系统时区。

## 4. CLR 与数据库类型契约

### 4.1 标准表示

纯内部 codec 接受原生 CLR 类型，使用整数日历字段、秒内 ticks、偏移分钟及带符号 interval 分量。DateTime 有效域为 .NET 范围，秒内 ticks 为 0–9,999,999；不将整个 DateTime ticks 乘 100 转为纳秒，以免溢出。仅秒内部分乘 100，纳秒上限为 999,999,900。

解析 wire 时先保留协议原始字段和单位，再校验并转为 CLR；不先截掉纳秒尾数。八、九位值若纳秒不能整除 100，CLR getter 抛 OverflowException。暂不新增公开纳秒时间类型。

分层投影规则是强制契约：合法且符合列 scale 的 wire 值，`GetString` / `GetFieldValue<string>` 按该入口现有适用类型规则处理；其中已有的 `GetString` 时间格式化必须直接从原始纳秒字段返回精确文本（含声明精度的前导/尾随零和 offset），不能先经过 DateTime。`GetFieldValue<string>` 不因此新增原本不支持的时间到 string 转换，依旧只接受 GetValue 实际为 string 的列。`GetValue`、当前时间列的 `GetProviderSpecificValue` 及对应 CLR typed/generic getter，对不可表示的纳秒抛 OverflowException；不将 provider-specific getter 改成 string。`GetFieldType` 保留原 CLR 映射，schema/NumericScale 报真实列 scale=8/9，GetDataTypeName 维持真实服务端类型名称。无法作为 CLR 值读取不妨碍合法的精确 GetString 通道。

### 4.2 精度规则

对于 scale s=0…6，ticks 必须能整除 `10^(7-s)`；s=7 可承载全部 CLR ticks。列声明 s=8/9 与“CLR 需要八/九位输入”不是一回事：CLR 七位值可以精确嵌入更高精度列，但该链路必须单独通过 profile 验证后才开放。scale 位域必须先按具体类型解码；interval subtype/leading 位域不能被统一 mask 破坏。

**公开参数与协议字段不混用。** 在类型已确定为时间类型后，显式 `DmParameter.Scale` 先验证 0…9 范围，19/23/255 等直接抛 ArgumentOutOfRangeException；不得将用户 Scale 作 `& 0xF`。该规则在时间绑定层执行，不全局收窄 decimal 等类型的合法 Scale。0…9 内再按 DATE/TIME/INTERVAL/wire/profile 验证：DATE 只接受未设或 0，已验证 TIME/INTERVAL 最多 6，extended 的 8/9 仍有 profile 门。只有来源明确的服务端 packed metadata 可以按格式解码，并校验未知位与 TLTZ 标志。

**时间参数 `Precision` 的产品决策：** 仅 `Scale` 表示小数秒位数；`DmParameter.Precision` 未设或显式 0 表示未请求 Precision 限制，显式非零值统一抛 NotSupportedException，不作 Scale 别名，不忽略，也不写入 wire 字节长度。适用于本专项时间参数，包括 fixed/advisory 路径及有类型 NULL；与 decimal Precision 无关。wire/described precision（例如 9/11 字节）仅由内部布局和服务器 metadata 决定。迁移说明明确：官方惯用的 `Precision=7` 应迁为 W 的 `Scale=7`（类型/profile 仍需支持）；实际 EF 若设置非零时间 Precision，TP-07 必须更新并验收，不通过忽略绕过。旧公开 helper 构造函数中的 `prec` 不是 DmParameter.Precision，按 TP-00 的签名和格式契约逐个适配。

| 目标 | 精确输入 | 拒绝或限制 |
| --- | --- | --- |
| DATE | DateOnly；零时分秒的 DateTime | 不能自动截掉时间；CLR 范围外日期无法作为 DateTime 返回 |
| legacy TIMESTAMP 16 / TZ 23 | 列精度内、最多六位 | 非整微秒拒绝，即使显式 Scale=7 也不升级 wire |
| extended TIMESTAMP 26 / TZ 27 | 已验证 profile，按列 scale 精确承载 | 未验证版本/能力保持拒绝，不按版本号大小推定支持 |
| TLTZ | 仅 profile 已证的 CType、标志、精度和时区转换 | 不按普通 TIMESTAMP/TZ 编码；26 是否允许 TLTZ 待单独取证 |
| TIME / TimeOnly | 已验证六位精度内的日内时间 | 当前不承诺七位；合法 wire 的 hour=24 转 CLR 抛 OverflowException，不取模或进位；是否存在此服务端值另行表征 |
| INTERVAL DAY TO SECOND | 精度、leading precision 和 TimeSpan 范围内的带符号时长 | 不将 months 转 ticks；非整微秒仍拒绝；MinValue 不调用 Math.Abs(long) |

### 4.3 Kind、偏移与历法

普通 TIMESTAMP 存储日历字段：写 DateTime 不隐式转本地时间或 UTC；读回 Kind=Unspecified。保证 ticks/字段，不保证 Kind 往返。DateTimeOffset 同时保持 local ticks 和原 offset，不统一成 UTC；DateOnly/TimeOnly 禁止隐式时区转换。

TLTZ 是上述普通 TIMESTAMP 规则的例外：按其 SQL 类型定义做会话时区与数据库时区转换。已验证 TLTZ 的 DateTime 输入解释为连接声明的会话墙钟（Kind 不触发 OS 时区转换），读回为读取会话墙钟、Kind=Unspecified；不承诺原始写入 offset。转换必须用 checked 整数算术，跨日期边界也不得舍入/饱和。TP-01 核实会话时区来自何处、服务器是否已转换，避免驱动重复平移。profile 未覆盖的 TLTZ 格式/标志保持拒绝；不因当前 helper 只识别 CType16 就擅自给 CType26 套用标志。

DateTimeOffset 边界分责任：CLR 输入实例已经由 .NET 构造器保证 local 与 UTC ticks 合法；不可能构造的组合仅作为构造器级前置负例，不能声称已发送到驱动。读 wire 时必须由驱动同时检查 local 和 `localTicks - offsetMinutes * TicksPerMinute` 落在 CLR 范围内，使用 checked/宽中间值，失败包装为 OverflowException。服务端 offset 范围从 profile 取；当前 W 守卫是分钟区间 [-779, 840]，即 (-13:00, +14:00]，这是当前代码事实，不是已证实的所有服务器能力。

profile 对服务端范围显式记录 local domain、是否另限制 UTC domain 及其边界、offset domain。已知越界在 Execute 前抛 OverflowException；未知约束不猜测成预检范围，服务器拒绝保留真实 DmException 及安全错误号，记录为尚待表征。TP-01 用能构造的 CLR 边界值及独立服务器原始字段探测确定必测 profile 的结果；仍未知的扩展范围不能标记验收通过。不将所有服务器时间错误泛化重映射为 OverflowException。

数据库 BCE、1582 历法转换区间及 CLR 外年份单列兼容矩阵：不能因日期在 CLR 范围内就承诺服务器精确保留。测试通过前按已知限制拒绝或声明不支持；不得让 MaxValue 成功掩盖其他日期范围缺口。

### 4.4 异常与 Reader 状态

值无法精确表示或超过 CLR 范围抛 OverflowException；能力/类型不支持抛 NotSupportedException；显式类型或精度与 fixed metadata 冲突在绑定层明确拒绝。NULL 保持现有 typed NULL 契约。消息不得包含原值、SQL、连接或凭据。

精度失败应发生在业务 Execute 发送前；Prepare/describe 可以已经发生。读取一个合法服务器值但 CLR 无法表示，属于值转换失败；不得擅自将连接判为 Broken。格式损坏、帧截断等仍遵守现有协议故障规则。测试失败 getter 后下一列、下一行及 Dispose/连接清理行为。

### 4.5 getter 目标矩阵（保持公开投影差异）

这里的“一致”指同一入口在 sync/async、普通/扩展同语义族内遵守冻结契约，不表示所有 getter 都返回同一类型。下表使用 `D`=DateTime(Unspecified)、`O`=DateTimeOffset、`T`=TimeSpan、`X`=InvalidCastException；遇合法值超 CLR 精度/范围时，由 OverflowException 优先表达转换失败。NULL 保持现有 DmException 6081/DBNull 区分。

| 入口 | DATE 14 | TIME 15 | TIME TZ 22 | TS 16/26 | TS TZ 23/27 | 已验证 TLTZ |
| --- | --- | --- | --- | --- | --- | --- |
| GetDateTime | 保持 D 日期零点 | 保持 D，锚定 1900-01-01 | 保持 D 时间投影，丢 offset 的既有行为 | 保持 D | 保持 O.DateTime 投影，不保留 offset | D 会话墙钟 |
| GetDateTimeOffset | X | X | 保持 O，1900-01-01 锚点 | X | 保持 O | X，不伪造原始 offset |
| GetValue / GetProviderSpecificValue | 保持 D | 保持 DbTimeToTimeSpan=false→D / true→T | 保持 O | 保持 D | 保持 O | 保持 D |
| GetFieldValue<DateTime> | D | GetValue 为 D 时 D，否则 X | X | D | X，不能借 typed getter 语义丢 offset | D |
| GetFieldValue<DateTimeOffset> | X | X | O | X | O | X |
| GetFieldValue<DateOnly> | DateOnly | X | X | X | X | X |
| GetFieldValue<TimeOnly> | X | TimeOnly（两种 TIME 模式均支持） | X | X | X | X |
| GetFieldValue<TimeSpan> | X | GetValue 为 T 时 T，否则 X | X | X | X | X |
| GetString | 保持精确日期格式 | 保持精确时间格式 | 保持时间与 offset | 保持精确 scale 文本 | 保持精确文本与 offset | 按已验证会话转换格式 |

GetDate/GetTime 在当前基线是内部 DmGetValue helper，不是 DmDataReader 的新增公开方法：GetDate 保持 14/16/23 接受、26/27 拒绝；GetTime 保持 15/16/22/23 接受、26/27 拒绝。这些历史非对称分支不扩大开放范围，重构不得误使公开扩展 getter 绕到拒绝分支；TP-00 记录每个调用点及直接 helper 测试。公开 DmDateTime 的 GetDate/GetTime 仍按自身签名投影，单独固定 raw buffer 单位契约。

`DbTimeToTimeSpan` 两种既有配置路径保留，TIME→TimeSpan 改为整数 ticks 构造，去掉当前 GetTimeInString→ParseExact 中转。若任一配置在连接层被能力守卫限制，维持该限制，只对可达路径作验收，不把源码分支的存在当已开放。INTERVAL 的 GetValue 配置模式和 provider-specific 返回类型也在 TP-00 清单冻结，不为了统一内部 ticks 改写对象形态。

## 5. 参数绑定和能力选择

新增内部“时间绑定描述”，冻结以下信息：原始 DbType（含 DateTime 与 DateTime2 区分）、CLR/provider 类型来源、用户是否显式设置 Precision/Scale（包括 0）、可靠服务器 CType/原始 scale/precision/mask、解码后的列精度与时区种类、有效 wire 类型、profile 能力、实际编码 scale。执行中不能依赖可变原参数重新推导；DateTime/DateTime2 区分来自冻结的原始 DbType，不能从已合并的 DmDbType.DateTime 反推。

决策次序：

1. fixed describe 是实际目标约束；用户显式类型只做语义兼容检查，不能把列的 CType/scale 覆盖掉。
2. 首先按 §4.2 校验用户 Precision/Scale，包括有类型 NULL；显式 Scale 保留用户意图：低于列精度时按显式精度检查输入，高于列精度时也必须验证列可表示；高于 wire 能力明确拒绝。已知列精度与参数精度分别存储，不用一个 effectiveScale 覆盖两者。例：fixed extended 列 scale6、显式 scale7 时，.1234560 可表示而 .1234567 必须拒绝；fixed legacy 列显式 scale7 直接为不支持，即使值是整秒也不升级 wire。
3. 无 fixed describe 时使用下表稳定选择。profile 七位能力指已验证可选 extended/默认七位；显式七位请求在未知 profile 上失败，不因输入恰为整微秒而降级。可在旧 profile 上稳定回落 legacy 的是未显式要求七位的推断/兼容默认。
4. 新默认推断不得按每次值的小数尾数在 16/26 间切换：由 profile 和声明类型决定稳定的类型/默认精度。参数值替换不导致不可预测的 prepare 类型漂移；显式 metadata 改变触发 reprepare。
5. 无类型 NULL 仍需显式类型或可靠 describe。切换连接/服务器 profile、ResetDbType、克隆和重新 Prepare 后重建时间绑定描述。

| 无 fixed describe 的声明来源 | 已验证七位 profile，Scale 未设 | 七位未验证但 legacy 已验证，Scale 未设 | 显式 Scale |
| --- | --- | --- | --- |
| CLR DateTime 推断 | 26/7 | 稳定 16/6，非整微秒拒绝 | 0…6 用 legacy 同精度；7 用已证 extended；8/9 按独立门 |
| CLR DateTimeOffset 推断 | 27/7 | 稳定 23/6，非整微秒拒绝 | 同上，使用 TZ 同族 |
| 显式 DbType.DateTime / DmDbType.DateTime | 保持兼容默认 16/6 | 16/6 | 0…6→16；7→已证 26；8/9 按独立门 |
| 显式 DbType.DateTime2 | 26/7 | NotSupportedException（明确请求原生七位） | 0…6→legacy 精确格式；7→已证 26；8/9 按独立门 |
| 显式 DbType.DateTimeOffset / DmDbType.DateTimeOffset | 保持兼容默认 23/6 | 23/6 | 0…6→23；7→已证 27；8/9 按独立门 |

以上每格还须验证声明类型语义与输入 CLR 类型相容；不按值内容选 wire。不可靠 advisory describe 的精度不能覆盖上述声明规则。可靠 fixed describe 始终优先保留真实 wire/列精度，因此显式 DateTime2 可精确写入六位 fixed 列，但其值仍受该列六位约束；没有 fixed metadata 时的默认七位请求不能套用于未知列。该差异在 API 文档与 TP-A05 单独列例。

能力表替代散落的版本判断，但初始仅承接已有明确验证的 profile；键至少包含服务器版本、wire 类型/模式、支持 scale、偏移与范围限制及证据 ID。建连不新增有副作用的探测，也不靠静默重试业务写入侦测能力。新增 profile 必须以独立探测工具完成读写和 metadata 验证。

## 6. 实施任务 DAG 和文件归属

下列为新专项 TP-00…TP-08；它们不是原 v1 T 编号，不改变历史任务状态。每个任务在根审阅和独立验证后关闭，依赖未关闭的任务不能进入实施。共享文件实行单一 owner、分阶段交接；High 不运行 dotnet/DB。

| 任务 | 依赖与 activation gate | owner / 唯一文件范围 | 交付与退出条件 |
| --- | --- | --- | --- |
| TP-00 冻结契约与入口清单 | 用户激活实施；重核 branch/head/所有权 | root：专项 docs、证据 manifest；High 只读源代码协助 | 完成 §4.5 每格及兼容 helper 调用点/单位清单，原始/语义 metadata 分离；基线 source/evidence 哈希；未知项有明确验证任务 |
| TP-01 profile 与独立 oracle | TP-00 | Sol High：新 `tools/TemporalBoundaryProbe/`、`eng/temporal-precision.*`；Sol Low 独占运行 | 分 O/旧W；6/7/8/9 列 CType/scale、Precision 表征、极值、offset/local/UTC 域、历法；TLTZ 标志/异会话时区；TIME 24:00 与 >6 精度存在性；不支持的 profile 明确关闭 |
| TP-02 纯整数核心 | TP-00；wire 布局沿用已证格式，新能力等待 TP-01 | Sol High codec owner：`Internal/Types/DmTemporal*` 新核心、TypeTests 新 pure codec tests；Luna Max 仅 `tests/fixtures/temporal-native/` | 正反转换、精度/范围、负 interval、边界随机性质测试；独立 golden 非仅自编自解；Low 离线验证 |
| TP-03 writer 与兼容入口 | TP-01、TP-02 | Sol High codec owner：DmDateTime/DmTime/DmIntervalDT、DmSetValue、N2DB/OracleDateFormat 必要路径及对应测试 | CLR、provider helper、显式文本入口无数值精度绕过；旧函数的可达入口有处置；保持公开签名 |
| TP-04 绑定与 metadata | TP-03 | Sol High binding owner：DmParameter/DmCommand/DmSqlType、DmParameterBinding、DmCommandPlan 必要冻结 metadata、FixedTemporalBindingTests | 原始 DbType/Precision/Scale 来源与 TLTZ 标志保留；稳定选择表及 NULL/clone/reset/prepare 矩阵通过；双约束 scale 和无 Execute 发送断言 |
| TP-05 reader 与格式兼容 | TP-03；共享 helper 写入已释放 | Sol High reader owner：DmGetValue/DB2N/DmDataReader、DateUtil 必要精确格式投影、结果格式适配及测试；DmDateTime 修改交回 codec owner | §4.5 矩阵、TLTZ、TIME 两模式整数路径、8/9 位精确 GetString/CLR拒绝、schema、错误恢复和多行读取通过 |
| TP-06 ADO.NET 同包验收 | TP-04、TP-05；整体 source freeze | Sol Low：仅新 ignored 运行输出；root：新公开摘要 | 七位极值实库、六位拒绝、服务器实际值、事务回滚、对象清理；Type/Command/Async 回归；源/包/DLL 哈希一致 |
| TP-07 EF 与迁移 | TP-06；独立 EF checkout 和目标版本已固定 | Sol High EF owner：独立 EF worktree 时间 mapping/literal/migrations/scaffolding tests；本仓库仅新交接目录 | 真实候选包 SaveChanges/查询/并发谓词/默认值/更新/回滚、旧六位与新七位模型、迁移和 scaffold 通过 |
| TP-08 文档与交付门 | TP-06、TP-07，review findings 关闭 | root：compatibility、专项报告/状态和发布说明 | A/B/C 证据可追溯；无修改旧失败；发行门独立检查；不由本计划自动授权发布 |

主线 TP-00 → TP-01/02 → TP-03 → TP-04/05 → TP-06 → TP-07 → TP-08。TP-01/02 可并行编写；所有构建/实库由同一 Sol Low 串行窗口执行。每轮失败先释放窗口，High 定点修复后新 freeze，禁止在已接受输入上追加修改。

必需 profile 集合在 TP-00 固定：主 profile 8.1.5.60 的普通 26/27 七位、同环境六位回归，以及当前已声明支持且受影响的 TLS/legacy profile。8/9 位、TLTZ 新格式和其他服务器版本是分别门控的扩展项；必须完成存在性/支持性分类，但未开放项可保持明确拒绝，不因它们尚未支持就伪称已支持，也不将扩展研究无限加入主线。TLTZ 旧路径若已对外开放，则其回归或明确兼容变更必须关闭，不能直接以“扩展项”跳过。

TP-07 若发现驱动缺陷，释放 Low 窗口并回到对应 TP-03/04/05 owner；重新冻结、生成新唯一包、重跑受影响的 TP-06 门，再用同一新包重跑 TP-07。任何接受版本变化都更新 A/B/C 证据，不把不同产品哈希拼接为同包验收。

## 7. 具体验收矩阵

| ID | 场景 | 必须断言 |
| --- | --- | --- |
| TP-A01 | MinValue、MinValue+1 tick、MaxValue、MaxValue-1 tick、MaxValue-9 tick；末尾 0…9 tick | 七位已验证范围 ticks 完全一致；六位仅整微秒成功；午夜/闰年/世纪边界无进位或截断 |
| TP-A02 | scale 0…9，legacy/extended，不支持/未知 profile；getter × 高精度列 | 8 位列合法纳秒尾数 10/90/100、9 位列尾数 1/99/100：前两项 GetString 精确而 CLR getter 拒绝，100 精确；8 位列尾数 1 属声明精度不合法，单列协议/值错误；schema 保持 8/9 |
| TP-A03 | DateTime Kind 三种；DateTimeOffset 0/+08/-05/+05:30、-14:00/-13:00/-12:59/+14:00；原始 wire local/UTC 边界 | 不隐式转换；offset/local/UTC 分别断言。可构造的 Min/Max 与偏移组合进入驱动；构造器已拒绝的组合只测前置约束；wire 越 CLR 统一 OverflowException，已知 profile 预检与未知服务器错误分开 |
| TP-A04 | DateOnly 极值；TimeOnly 日内末值；TIME hour=24；TIME(n>6) 存在性；TimeSpan 正负/零/Min/Max/多天；interval leading precision | hour=24 的 CLR 投影为 OverflowException，禁止取模/次日；TIME 两配置的对象类型保持且整数转换；精度超限拒绝，负 interval/leading precision 不变 |
| TP-A05 | 推断/显式 DateTime/DateTime2/DateTimeOffset/provider；Scale 未设及 0/3/6/7/8/9/19/23/255；Precision 未设/0/7/9；fixed/advisory、NULL、克隆、reset、reprepare | 逐格按 §4.2/§5 判定；非法用户 Scale 不 mask，非零时间 Precision 明确拒绝且不污染 wire width；值换尾数 wire 不漂移；无 Execute 发送/逐行 fallback/业务重放 |
| TP-A06 | §4.5 getter × 14/15/22/16/23/26/27/TLTZ；sync/async；GetString/schema | 每格保持/拒绝策略及精度错误一致；TZ 的 GetDateTime 历史投影与 GetFieldValue<DateTime> 拒绝分别断言；不新增内部 GetDate/GetTime 的公开能力；NULL 与后续读取正常 |
| TP-A07 | 已支持普通/紧凑结果格式、多行、多结果、SequentialAccess 与缓存 getter | 单位统一且元数据匹配，跨页仍准确；未开放格式保持守卫，不借机扩大能力 |
| TP-A08 | direct/Prepare、sync/async、真实 INSERT/SELECT/UPDATE、提交与回滚 | server-side FF7/FF9 和分量核对、独立连接读回；读写必须用不同 oracle；服务器表征与驱动结论分开 |
| TP-A09 | EF 新建七位列、现有六位列、参数和 literal、默认/计算表达式、比较与排序、并发 token | SaveChanges/生成 SQL/读回/更新/事务回滚正确；有效精度不因 provider 切换改变；不自动改历史实体哨兵值 |
| TP-A10 | 旧六位列扩到七位、scaffold 后再生成模型、Down 前置检查 | 旧值保持；新精度准确反映；降回六位前发现非整微秒并停止；不提供会静默丢精度的自动 Down |
| TP-A11 | invariant、zh-CN、en-US 等 culture；有效/非法文本；旧 helper 直接调用 | 数值 wire 不经字符串/浮点；格式契约不依赖机器 culture；文本不绕过精度和范围校验 |
| TP-A12 | DateTime CLR 范围外、BCE、1582 转换区间、坏长度/字段/纳秒和 NULL | 精确区分不支持值与损坏协议；不宣称数据库所有值均能用 DateTime 表达 |
| TP-A13 | TLTZ 的 16/候选26、0x1000/mask、跨会话时区、跨日及极值、typed/GetString/metadata | 已验证路径恰好一次转换；原始 flags 不丢；不支持格式读写双侧拒绝；新连接不继承旧绑定；会话变化只操作专用 TEST 连接并恢复/销毁，不改实例时区 |

合成 fixture 必须携带 case ID、类型、ticks/offset、目标 scale、预期值/异常及 expected_only/not_run 状态；纳秒不精确值使用明确字段向量，不能先经 DateTime 构造。随机测试固定种子并保留最小失败例。无需为本专项启动无关的大 LOB/压力门，但共同代码变更影响到的现有门必须复跑。

## 8. 独立 oracle 与实库运行

纯测试至少包含人工边界算术与历史已验证 wire golden；禁止只验证新 encoder → 新 decoder 自洽。实库使用服务器构造/查询、官方 O 写 → W 读和 W 写 → 独立读三种证据，官方驱动的已知边界失败作为表征保留，不能当作正确性标准。CAST/literal 若自身舍入则单列，不用它代替精确 typed 写入 oracle。

实库先通过 TEST wrapper 验证 `WDM_PROVIDER_TEST` 和目标 profile，再建立 nonce 唯一对象与 ownership manifest。只用当前 schema，DDL 按可能隐式提交处理；事务回滚验证用 DML，不声称回滚能够清除 DDL。finally 精确清理并新连接验证最终状态；失败/超时保留本次对象精确恢复清单，禁止全 schema 清理。

SDK 采用实施时仓库锁定版本（当前 driver 为 10.0.203）；每次设置可写 DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1，使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false` 和 SDK 支持的 `--disable-build-servers`。EF SDK 以独立 checkout 锁定版本为准。Low 记录完整脱敏命令、退出码、计数、profile、source/package/DLL 哈希和最终数据库状态；不输出 secrets、原始认证报文或含凭据异常。

无显式测试连接时只跑离线，状态 `integration_pending`；不同 profile 分别验收。现有 shared 环境 Fetch 超时背景不算本专项通过或失败的自动归因，必须保留当前候选和对照结果。TLS 使用既有严格策略；PlaintextAllowed 仅通过批准的 typed 配置，不能改 O/W 共用 secret。

## 9. EF 及存量迁移策略

独立 EF 仓库不是本次文档编辑的写入范围。实施 TP-07 时固定其 commit、包版本和隔离 worktree，重新检查真实 type mapping；不能假定目前默认一定是六位或七位。

提议对新模型明确采用 TIMESTAMP(7)（以及对应带时区类型），前提是目标 profile 已通过 TP-06。显式 HasPrecision/HasColumnType 优先保留；scaffold 保持数据库真实精度，已有 snapshot 不因驱动升级而批量重写。没有已验证七位能力的目标必须明确配置受支持精度；禁止静默降为六位。

EF 的模型 `HasPrecision(7)` 与 ADO.NET 的 `DmParameter.Precision=7` 不是同一字段契约：时间 mapping 应生成列 scale7，并对参数设置 Scale7；不能把模型数字机械写进 DmParameter.Precision。TLTZ 与带原 offset 的 TZ 分别建模，禁止互换；EF generic getter 与旧 GetString 解析链都按 §4.5 验证。

六位→七位迁移是独立 schema 变更：先只读盘点列、默认/计算表达式、索引与依赖、业务精度约束；在 TEST 唯一表验证 ALTER 的支持、锁和数据保持；真实业务迁移由该应用按自身部署流程执行。此前丢失的第七位不能恢复。迁移 Down 必须先检查七位新增数据是否含非整微秒，有则阻断并要求明确的数据处理方案。

“永不过期”哨兵只在该应用明确选择七位契约后决定是否改回 MaxValue；驱动不把 `.9999990` 自动解释为无限，也不自动替换历史值。数据库函数/默认表达式按服务器实际返回精度测试，不由 EF 类型声明推定其精度。

## 10. 兼容性、风险与回退

- 默认推断由 legacy 向 extended 的切换是可观察行为变化，涉及 metadata、prepare 和服务器重载；必须在 TP-04/06 给出旧/新对照和发布说明，不冒充纯内部重构。
- 显式 DateTime/DateTimeOffset 无 Scale 的默认按 §5 保留 legacy；推断默认及显式 DateTime2 的变化单列。非零时间 Precision 改为拒绝、未验证 TLTZ 拒绝属于兼容变更，必须给出迁移示例；不能仅以“无损”理由省略行为说明。
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

本仓库依据：[T09 设计](T09-design.md)、T09 报告（历史记录已归档）、T12 报告（历史记录已归档）、[七位精度探测说明](../../tools/TemporalPrecisionProbe/README.md)、[当前进度](progress.json)、[文件与窗口规则](active-ownership.md)。

官方 SQL 文档在 2026-10-07 查询时将 TIMESTAMP 列精度列为 0–9、默认 6；这里只用作产品能力研究入口，不替代本地服务器和协议 profile 证据：[日期时间类型](https://eco.dameng.com/document/dm/zh-cn/pm/dm_sql-introduction.html)。

Kimi Code 2.1.1 已对 [冻结 v1](temporal-dotnet-precision-plan.v1.md) 完成独立只读 review，退出码 0，结论为“修订后可进入实施”，提出 8 项意见。主审阅者完成一轮修订；[原始意见](reviews/temporal-kimi-review-r1.txt)、[逐项处置与证据校正](reviews/temporal-kimi-review-disposition.md) 保留审计链。无版本入口仅指向 v2，v1 不作为实施依据。

修订次数：1；Kimi 复审次数：0。修订后的接口策略是 root 的设计决策，不冒称 Kimi 已逐项认可。本文的验收断言仍是实施目标，不是通过结果。TP-00 激活时重新核对源码和 profile，不能将本次静态审阅计为产品 gate。
