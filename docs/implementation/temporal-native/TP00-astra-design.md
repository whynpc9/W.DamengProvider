# TP-00 Astra 设计与 R3 新基线审阅

日期：2026-10-08。审阅基线：`fe89bd44abe99c1608499f71f4634a13e732c826`。输入为冻结的 [v2 计划](../temporal-dotnet-precision-plan.v2.md)、[Kimi 处置](../reviews/temporal-kimi-review-disposition.md) 和当前 [ownership](../active-ownership.md)。本文件只给设计及静态源码结论，没有运行 dotnet、数据库或读取 secrets，不代表 TP-01/02 或 A/B/C 验收通过。

结论：v2 在 R3 合并后可实施，严格无损、Precision 非零拒绝、稳定默认选择、getter 分层均不需要回退。TP-01 和 TP-02 可在 TP-00 收口后分别推进。真正需要先固定的是公开 raw helper 单位契约和 metadata 来源保存点；其余未知属于明确的 profile 取证门，不应阻塞纯核心实现。

当前 R3 状态以仓库 `evidence/T19/v23/root-final-readback.json` 的 `v23_full_defined_R3_matrix_accepted` 为准；其中包含 shared 接受。旧记忆中的 T17 pending 不适用于当前基线。R3 历史通过不等于新专项候选通过。

## 1. 根裁决及最小范围更正

| ID | 结论 / 建议 | 源码依据 | 验收标准 |
| --- | --- | --- | --- |
| D01 | **root 已批准**：TP-04 增加 `Internal/Legacy/Dm/DmField.cs` 和 `Internal/Legacy/A/T02_02000094.cs` 对应 metadata decode 的窄范围；只保存来源，不改变 I/O 或生命周期 | `T02_02000094.cs:286–324` 读参数 describe；`:375–398` 读结果列后清除 scale 的 0x1000/0x2000 并改为 mask；Command 才保存已太晚 | raw scale、CType、precision、mask 来源能跨 decode/bind/reprepare 回读，sync/async 一致，原非时间 metadata 行为不变 |
| D02 | **root 已批准选项 B**：公开legacy12-byte输入/输出统一微秒；public decoder遇非整微秒拒绝；内部Reader改走typed ns/ticks核心，保留完整七位。选项A保留分裂输出但无法保证buffer组合安全，详见§3 | `DmDateTime.cs:437,531–595,680–705,902–924,1094–1279,1285–1510` | 单独 golden 检查每个入口；禁止按数值大小猜单位或自动识别微秒/ticks；声明兼容修正，不宣称全部旧 helper 行为完全不变 |
| D03 | **root 已批准**：8/9位分别记录layout、read实测、write实测。已存在且layout/metadata语义明确的合法读通道继续精确GetString、CLR非整tick拒绝，不新增全局版本白名单；仅确实未知格式/字段语义或新扩展按能力拒绝，TLTZ仍有独立强制profile门 | v2 §4.1/§5；`DmTemporalCodec.cs:80–101` 当前写最高7、读fraction校验可到9 | pure tests 可覆盖任意合法ns，不据此开启服务器能力；TP-01分开取证：O写失败、旧W写失败、服务器能力不可混同，不能因单一驱动写失败关闭已合法读列 |

D02 已由root裁决，不需要新增公开API，也不加入有损模式；兼容变化必须列入本专项发布说明。不能在编码时自行改变[7..9]单位。

## 2. 内部类型及模块接口

以下名称是建议接口，可在不改变责任边界下调整。新增文件均以 `DmTemporal*` 命名，使 TP-02 的文件范围明确。

| 类型 / API | 内容与责任 |
| --- | --- |
| `DmTemporalFields` immutable struct | Year/Month/Day/Hour/Minute/Second、`NanosecondsWithinSecond` (0…999,999,999)、`OffsetMinutes?`。这是协议/文本原始语义字段，允许合法服务器但 CLR 无法表示的值；不是 DateTime 包装。不得保留可变 `int[]` 引用 |
| `DmTemporalValue` 或等价 CLR 分量 | CLR Calendar/Time kind、日历字段、`FractionalTicks` (0…9,999,999)、整数 offset。DateTime Kind 不影响编码；DateTimeOffset 保留 local ticks 和 offset |
| `DmTemporalWireLayout` | 明确 DATE3/TIME5/TIME_TZ7/TS8/TS_TZ10/TS9/TS_TZ11；结果格式另枚举，不把未知12/13-byte或 BDTA 自动当同一种 layout |
| `DmTemporalRawMetadata` immutable record | `HasServerSource`, `CType`, `Precision`, `Scale`, `TypeFlag`, `MaskBeforeNormalization`, `DerivedMask`, `ResultFormat`；可附 source kind ParameterDescribe/ResultColumn/Synthetic。原始 mask 若协议没有独立字段，应记录0及从 raw scale 推导的 mask，不能伪造“服务器发送了mask4” |
| `DmTemporalColumnDescriptor` | 原始 metadata + 解码后的 fractional scale、interval subtype/leading precision、普通/TZ/TLTZ/Oracle-date种类；未知位和矛盾组合明确拒绝。numericScale 对外输出语义 scale，raw flags 留内部 |
| `DmTemporalProfile` | exact server version + 类型/layout/结果格式 + read/write scales + default-seven能力 + offset/local/UTC/calendar范围 + TLTZ转换位置 + evidence ID。新写能力与TLTZ等明确门控能力不按版本大小继承；已存在明确layout/metadata的合法读取不新增全局版本阻断 |
| `DmTemporalBinding` immutable record | 原始 DbType、是否显式 DbType/provider type、type source、显式 Precision/Scale（含0）、固定列 descriptor、profile identity、effective CType/wire width/encoded scale、用户 scale 与列 scale 双约束。最终绑定只使用冻结参数及本次连接描述 |
| `DecodeRaw(bytes, layout, descriptor)` | 长度、字段、声明精度校验后返回 raw fields，**不投影 CLR**；不接受模糊单位 buffer |
| `ProjectDateTime/DateTimeOffset/TimeOnly/TimeSpanExact` | 单位转换及 CLR 范围检查；ns%100!=0 则 OverflowException；hour24 的 CLR 时间投影 OverflowException；local/UTC 双范围均检查 |
| `ValidateFractionalTicks(ticks, scale)` | scale0…6 的 quantum=10^(7-scale)，7…9 时 CLR 已可精确嵌入，但是否允许 layout/profile 由上层决定；负 ticks 用余数判断，无 Math.Abs(long.MinValue) |
| `ValidateWireFraction(ns, scale)` | ns 范围 + ns%10^(9-scale)==0；与 ns%100 的 CLR 约束分开，8位尾数1先判值不符合列精度，尾数10/90则合法但非CLR |
| `EncodeExact(fields/value, binding)` / `FormatExact(fields, scale, format)` | 前者严格校验后整数编码；后者直接从ns保留零和offset。两者不经 DateTime.Parse/ToString 浮点中转，不查询当前文化或机器时区 |

只对秒内 ticks 乘100；不得对完整 DateTime ticks 乘100。DateTime 构造及 offset UTC 运算使用 checked/宽中间值，最终统一 OverflowException。interval 保留独立带符号分量和现有 BigInteger 边界策略，绝不把月换为固定 ticks。来源：`DmTemporalCodec.cs:14–101`、`DmDateTime.cs:598–605,910–924`、`DmIntervalDT.cs` 的 `ToTimeSpanExact` 与 encode。

可验证标准：已有 O golden `EA87BBEE81C8000000` 代表指定日期+1tick，不能自行重生成预期；TZ 四个 golden 见 `ExtendedTemporalPrecisionTests.cs:15–49`。增加人工计算的最大日期三向量、负interval/MinValue、非整tick ns向量。新encoder→新decoder只能作为补充性质测试，不能作为唯一正确性证据。

## 3. 公开 helper 的 raw 单位契约与必要裁决

类内存在两套历史表示：`dateTime` byte[] 与 `dt` int[]。同一个12-byte数组没有单位tag，而1…999999可同时是合法微秒或ticks，所以任何自动识别都是猜测。prec=8/9/11是历史helper参数，也无法消除该歧义。

**选项A（不推荐）**：原encoder/DateTime构造raw输出微秒、public decoder输出ticks、byte[]构造输入ticks都保持；用对象内部tag修复直接getter。它最少改变已观察输出，但公开数组链组合仍可能静默误解，必须文档说明两种buffer不可组合，无法满足“公开raw通路统一”这一更强目标。

**选项B（root已批准）**：所有公开legacy12-byte buffer定义为微秒；内部Reader完全脱离这种中间表示，使用有单位的raw ns/ticks核心。公开decoder遇extended七位中非整微秒值明确OverflowException，既不丢第七位，也不输出与名称相同却单位不同的数组。这是签名不变的明确兼容变化：public decoder原先已返回ticks的数值改为微秒，原先可通过其12-byte结果读取的非整微秒现在拒绝。完整ADO.NET七位读取必须保持，不可随helper限制退化。

以下契约按root已定案的B执行：

| 入口 | 固定输入/输出单位与适配 |
| --- | --- |
| `DmDateTime(DateTime, prec, scale, flag, tz)`；`DmDateTime(string, prec, flag)` | 生成12-byte [7..9]微秒、[10..11]offset分钟。CLR构造移除Round，严格检查legacy可表示性；文本按原入口整数解析，超微秒不截断。对象getter微秒×10投影，修正现有微秒误当ticks问题 |
| `DmDateTime(byte[], prec)` 的12-byte输入 | [7..9]微秒，范围0…999999，getter显式×10。超范围拒绝，不猜ticks；合法范围内的旧ticks调用方必须按迁移文档转换，不可能自动检测 |
| byte[]构造的packed输入 | 3/5/7/8/9/10/11字节由明确wire布局解析，不按prec猜布局。实例可以保存有单位raw fields供getter精确投影；若GetByteArrayValue要产生legacy12-byte则非整微秒拒绝。未知长度明确拒绝 |
| `DmDateDecodeFast` | packed3→12-byte日期分量，无fraction |
| `DmTimeDecodeFast` / `DmDtDecodeFast` / `DmTimeFromRec4` | 输入packed对应类型或其历史12-byte微秒buffer；输出12-byte微秒。extended9/11先保留ns，ns%1000!=0则Overflow，不截断也不返回ticks；不得继续被内部Reader用作唯一七位路径 |
| `DmdtEncodeFast(byte[])` / ref重载、`DmdttzEncodeFast(byte[])`、`DmTime.TimeEncodeFast/TimeTzEncodeFast(byte[])` | 输入12-byte微秒，先范围与字段检查，再pack；无bitmask截断超限数 |
| 对应legacy `int[]` Fast encoders | 下标6保留现有直接pack的微秒；不是`DmDateTime.dt`的ns。长度及offset sign索引分别检查，不拓展未支持类型 |
| `DmdtEncodeFast2(ref bytes, DateTime)` / `Dmdt2TzEncodeFast2` | CLR秒内ticks×100→extended9/11字节ns。纯helper无server连接，仅表示布局，成功不等于profile开放 |
| `DmDateTime.dt`；`valueOf(string)`；内部`valueOf(bytes,column,connection)` | 下标6固定ns，保留精确string投影；encode先验证declared scale，再精确除1000用于legacy；删除roundHalfup补偿 |
| `DmTime(byte[])` / `GetNano()` | packed7与20-bit fraction微秒，名字不改；GetByteArrayValue/GetTzByteArrayValue均12-byte微秒；DateTime构造无舍入 |
| `DmTimeEncodeTz(...,msec,...)` | 此标量fraction参数当前validator上限9,999,999且直接getter使用ticks，继续定义为ticks。直接经typed构造，不再靠12-byte微秒中转；不是把其标量单位一起改掉 |
| `DmtimeAddByFmt` | 日历字段和fraction ticks整数量算；TLTZ主链应走统一descriptor/typed核心，不能继续依赖此混合legacy算术作为独立时区策略 |

构造函数/Raw getter签名仍在，若对象来自extended raw fields，CLR getter可保留七位，只有必须输出legacy微秒buffer的入口拒绝非整微秒。不得返回新的13-byte或复用12-byte布局暗藏tag。`GetByteArrayValue`可变数组形态不借此改造成公开新类型。

必须分别测试：构造对象直接getter、construct→GetByteArray→encoder、wire→publicdecoder→bytector→getter（整微秒正例与非整微秒拒绝）、内部typed decoder七位正例。0.123456的buffer预期123456、CLR fraction预期1,234,560ticks；不能只测整秒。现有ExtendedTemporalPrecisionTests的历史official wire golden和历史证据保持不变。建立独立新contract tests明确验证publicdecoder非整微秒拒绝，以及内部typed decoder和public Reader七位成功；当前源码测试如引用已改变的helper，显式迁移到对应typed入口并保留原wire/ticks断言，不将改掉的旧断言冒充未变契约。

公开ctor显式scale7但输出布局只能legacy时能力拒绝，不因为值恰为整微秒偷扩布局。已合法微秒encoder字节保持；原舍入/截断改拒绝、单位误投影修正、非法length/field不泄漏IndexOutOfRangeException均单列兼容说明。

`DmParameter.CopyMutableValue(strictSnapshot):1035–1051`仍不允许可变DmTime/DmIntervalDT provider-helper参数值。direct helper修复不等于加入参数白名单；N2DB复杂/数组路径也不能借接codec解除守卫。

## 4. 绑定决策、metadata 保存与 prepare

保留 v2 §5 全表。尤其：CLR推断 DateTime/DTO 与显式 DbType.DateTime/DTO 不能合并；显式 DateTime2 的默认7请求来自冻结的原始 DbType。`DmParameter.ResolveType` 当前会将两者映射到同一 DmDbType，故仅靠 resolved.Type 不够。`DmParameterMetadata` 已含 DbType、Source、ExplicitPrecision/Scale；复用其冻结逻辑而非引入另一个读取活参数的旁路。

1. PlanCapture只做已知声明类型的用户 Precision/Scale早检与不可变请求快照。resolved type确定后必须再检一次（包括null/DBNull）；不要把 metadata 校验放在 `NormalizeForWire` 的 null早返回之后。
2. fixed describe保留真实CType/列scale/wire宽度；用户scale和列scale分别验证。fixed26列scale6+请求7：值尾数0可过、尾数7拒绝；fixed16+请求7：能力拒绝。不能用explicitScale覆盖列scale。
3. advisory按profile和声明固定选择。`DmField.resetType(26/27)` 当前默认scale9（`:525–530`），所以切换CType后必须明确设置策略选定scale7，不能依赖reset默认。更不能仅给16填Scale7。
4. 拒绝非零时间Precision，但不把0写为wire宽度。wire width取layout/真实describe。用户Scale19/23/255先ArgumentOutOfRangeException；server packedscale独立按具体类型解码，interval leading/subtype不可用全局`&0xF`抹掉。
5. describe和绑定有两个冻结时点：PlanCapture冻结用户意图；Describe后、本次Execute前冻结resolved binding。不可在连接尚未Open或尚无describe时伪造最终server profile。
6. 当前 `StatementInvalid():1573–1591` 比较 `DmParameterMetadata`，且已有物理session所有权核对。保留这些R3行为；只有metadata/profile/session变化才重建，不因输入fraction尾数变换而重选wire。清理/reset/换连接后无旧descriptor或profile泄漏。

### metadata 的最小传递实现

建议在 DmField 增加 nullable immutable `TemporalServerMetadata`，由decoder显式调用 capture方法；不在 `SetScale/SetCType/SetPrecision` 的每次调用中覆盖原始值，否则advisory reset和绑定会毁掉来源。结果列在清0x1000之前捕获raw，并记录清位后的语义mask；参数describe在三字段读入且TypeFlag就绪后捕获。普通非temporal不创建该对象。

`DmInfo.SetParaNum` 创建新 DmParameterInternal，继承 DmField，初始无来源；`DmInfo.SetParamsInfo` 目前是数组引用交接（`:95–130`），应保留其只读来源对象。`resetType`只改变effective字段，不覆盖server snapshot；Synthetic新字段仍无server来源。重新收到describe要显式替换snapshot，不能保留上一次prepare的mask；重新分配columns/params自然清空。若实现新增clone，复制immutable record，禁止共享可变descriptor/数组。

用户参数Clone/CloneForPlan继承 `DmParameter.cs:992–1023` 的DbType和显式标志；不要给public parameter带上server描述。最终binding按SQL occurrence保存，不能按参数名把fixed目标不同的两次occurrence强行合并；`DmParameterBinding.ResolveServerParameter`只负责映射，保持流式source重复绑定拒绝规则。

协议decoder的sync/async共享方法调用应以当前partial类调用图核实；`ResponseCodec.Async.cs:397`调用同一结果metadata `A`，不复制一套规则。测试用合法synthetic response分别走sync/async，断言 raw0x1006→semantic6/mask4，plain6、Oracle-date0x2000、未知位/双标志负例，以及重新describe清除旧标志。保留numeric129转换等非temporal测试。

## 5. TLTZ、历法与未知能力

已确认当前存在真正不一致：`DmGetValue.GetTimestamp:570`查rawscale0x1000且helper只认16；结果decoder已清位并置mask4；`DmDateTime.decode:1671` 的 GetString 路径反而按mask4转换。不能将现状描述为所有getter已有一致TLTZ语义。

由column descriptor统一识别TLTZ，保留raw/semantic字段，并由已验证profile决定转换恰好一次。TP-01分别以相同session、跨日、最大/最小合法域查服务器文本与raw字段；异session timezone先确认当前允许的SQL会话级路径与driver所见时区一致，不通过Builder启用被禁止配置，无法建立该前提就将异时区profile关闭；明确是server还是driver完成db↔session转换。无已证profile的TLTZ读写都NotSupportedException；不是改回普通timestamp。26 TLTZ独立取证，16成功不自动继承。

`DateEncodeFast(byte[]):1539` 相关1582-10-05…14自动转15是可达兼容差异；pure核心按CLR Gregorian fields校验，主链不能悄悄把这些日期折到15。TP-01有证实服务器不保留的域则明确不支持/拒绝，未知域保持分类，不把旧 helper 的calendrical补偿复制进核心。BCE/CLR外年份允许raw文本层与CLR层分开，但无profile不宣传全域支持。

## 6. Getter 与恢复验收

v2 §4.5 的公开矩阵与当前调用图一致，应按该表逐格测；`DmGetValue.GetTimestampTZ:553`显式转发CType22到GetTimeTZ，因此public GetDateTimeOffset对TIME TZ确有1900锚点兼容投影。不要误删。generic DateTime对TZ仍从GetValue得到DTO后InvalidCast，不转用typed GetDateTime。

- `GetValue/GetProviderSpecificValue` 对temporal同对象类型；provider-specific仅numeric9/24特殊处理（Reader`:1007–1021`）。合法ns但非整tick必须Overflow，GetString仍直接raw精确文本；generic string不因此新增时间转字符串。
- TIME两配置保留DateTime/TimeSpan输出差异，删除 `DmGetValue:792` 的 GetTimeInString→TimeSpan.ParseExact，只用整数ticks。TimeOnly仍要求CType15；hour24可作为合法raw文本fixture，但CLR投影失败。
- INTERVAL DT：`GetIntervalDtByMode:680–687` 在IntervalMode.DT/ALL或packed scale1574返回TimeSpan，其余DmIntervalDT；provider-specific保持同样分派，generic TimeSpan对DmIntervalDT执行ToTimeSpanExact。YM及其mode继续原规则。
- GetString必须在Scale检查之后format，DateUtil现在substring逻辑本身不能证明无损；例如scale8/ns尾数10合法，scale8/ns尾数1必须拒绝，不能先字符串截掉最后一位。
- 普通和内部紧凑结果格式fixture分别提供layout/单位证据；`dmdtDecodeBdta:1797` 的12/13字节与普通12-byte兼容buffer不可混为一谈。当前Builder Normalize（:1573,1603–1629）拒绝DbTimeToTimeSpan=true、IntervalMode非OFF、IsBdtaRs=true及Time_Zone非默认；两种TIME模式、非默认interval mode、BDTA只保留内部fixture覆盖，不计为公开real支持，也不修改Builder开放。

合法值的CLR转换失败只是当前getter失败：不标Broken，不发送取消/重试，不消耗下一行。普通reader可在失败后同列GetString、下一列、下一行；SequentialAccess在 `GetByteArrayValue:2142–2161` 已前移column ordinal，所以只保证下一列/下一行及Dispose，不承诺倒退读取同列。不得为本专项回滚SequentialAccess游标或复活已关闭LOB flow。

坏长度/帧截断与非法协议字段沿现有protocol错误边界处理；不要把任意IndexOutOfRange/FormatException统包Overflow来隐藏损坏。能力失败NotSupported、CLR精度/域失败Overflow、类型投影InvalidCast、typed NULL维持6081/DBNull。消息不含原值或SQL。

需要的恢复测试：一行合法非整tick时间+普通列+LOB，一行可表示时间；typed失败后继续普通列和下一行；普通模式另测同列GetString；sync/async、close/Dispose、cancellation竞争与多页结果均维持原lease。以真实完整帧注入坏length/field的测试另列，不把它当可恢复合法值。

## 7. R3 不可破坏的边界

| 边界 | 实施约束 / 复核 |
| --- | --- |
| Command plan和stream输入 | `DmParameter.CloneCore:1011` 的FreezeStreamingInput、`DmParameterBinding:59–62` source identity去重不改；temporal规则不得进入CLOB/BLOB兼容分支 |
| invocation/取消/发送 | `DmInvocation.ShouldAbortAfterFailure/TranslateFailure`和精确unsent wire receipt保持；temporal bind拒绝保证无业务Execute发送，但Prepare可已发送，不能把“无Execute”偷换为“整个invocation无SendAttempted” |
| Reader诊断 | 保留 `BeginReaderInvocation/BeginInternalInvocation`、`CompleteReaderInvocationIfSent` 及分页own/borrowed区分；缓存转换不新增success或transport-error span。不要为绕过Overflow去调用业务Complete或cleanup success receipt |
| 池容量与物理关闭 | 不改Session/Transport、物理completion、pool lease或shortcut取消。异常清理仍走已有owner；此专项不重新定义pre-send绑定失败后的session复用策略 |
| LOB与SequentialAccess | 不改row/execution epoch、activeLobFlow生命周期、opaque CLOB长度、严格编码首块失败receipt。temporal getter仅在现有获取raw缓存后做值转换 |

验证按影响选择现有Command/Type/Async/Reader与metadata protocol suites，新增恢复case及R3分页诊断回归。若触动上述共同生命周期而不能证明局部性，先停止扩大补丁、回root设计；不能仅靠temporal单测通过关闭。

## 8. TP-01 oracle 和 source/package 身份

不能复用旧probe源码中的硬编码W包路径或身份作为本轮候选。`tools/TemporalPrecisionProbe/expected-identities.json` 的 W 为早期R1 `0.1.0-r1.20260930051717`；`evidence/T12-validation.json` 最终R1包不同；当前R3 v23又为第三个身份。

建议 TP-01 使用如下独立lane：

1. **O固定**：`DM.DmProvider 8.3.1.47463`，net9资产；包SHA `62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75`，DLL SHA `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b`，MVID `d909b5af-965f-4cf9-a80a-a6181124e361`。来源是tracked旧identity manifest，Low仍需在本轮验真。
2. **旧W基线**：优先使用已归档、身份可核实的v23 `0.1.0-r3.20261007143838`；`root-final-readback.json`记录包SHA `6696a26d89e4caa1b9e7c40a73f024c4df9d8a695da1446bdbecc17757d3bcf9`、DLL SHA `c70f7d4b7bd37187f55483d186d658206232a2e1f60a662530b3a0282e3231e5`、MVID `69e5632e-75c4-4672-b924-57b8ffb58a52`。若原包缺失，Low从冻结fe89bd4 source archive单独构建唯一baseline版本，标记rebuilt baseline，绝不冒充v23原包。早期R1只作额外historical lane，不替代当前旧W对照。
3. **新W候选**：TP-06后独立唯一版本包；probe只PackageReference，不ProjectReference或手工塞DLL绕过；deps/lock实际asset和加载Assembly SHA/MVID必须等于包内资产及candidate manifest。

Low已向root报告官方包与历史run目录可用；本设计未独立验证ignored包存在或内容，不将manifest记录冒充已完成本轮加载验证。原仓库wrapper可启动当前worktree探针，绝不复制secrets；先确认TEST/profile再建nonce owned对象。

O-seed→W-read与W-write→O-read必须结合服务器分量/FF7/FF9字符串oracle；O自身typed getter若边界失败，保留表征并用服务器文本验证，不将其当正确expected。原raw SQL CAST已出现七位literal差异，CAST成功不替代typed write。official Precision7为O lane历史惯用法，W lane分别测Precision拒绝及Scale7正例，不能要求两个驱动使用同一参数配置。

source身份链：freeze manifest（包含产品、测试、工具、锁、构建配置）→唯一Version包→包内DLL→每lane实际加载DLL/MVID；冻结后无产品/测试追加修改。fe89bd4 commit只能标基线，不能代替含未提交新实现的source hash。每轮失败原输入/退出码保留，新freeze新包；A/B/C不得拼接不同hash。

## 9. Precision 拒绝与 EF 交接

Precision非零拒绝是v2明确产品决策，不是ADO.NET标准强制，也不能为兼容EF静默忽略。TP-04必须覆盖Date/Time/DateTime/DateTime2/DTO/Interval typed null、fixed/advisory与clone/reset；decimal照旧。兼容说明给出 `Precision=7`→`Scale=7`（受type/profile/column约束）。

root固定后续EF基线为 `5863d37`，用独立worktree，原dirty `aef25867` 不写。本次通过git object只读核对该pin：

- `DamengTypeMappingSource.cs:58–75` 默认TimeOnly6、DateTime TIMESTAMP(7)/DbType.DateTime2、DTO DATETIME(7) WITH TIME ZONE，均携带model Precision；`:349–371` HasPrecision分支同样生成该facet。
- `DamengDateTimeOffsetTypeMapping` 使用GetString+Invariant Parse，必须测ns拒绝/精确文本以及原offset；它不自动等于generic DTO getter验收。
- `DamengTimeOnlyTypeMapping.ConfigureParameter` 调base后设置Scale，却没有显式清理参数Precision；`DamengTimeSpanTypeMapping`同样调用base后设置provider type和Scale。TP-07必须用该pin实际EF依赖创建DmParameter并记录metadata，不凭源码推定base是否写Precision。

TP-07实施建议为时间专用mapping明确把模型小数秒facet写到Scale，并确保DmParameter.Precision未设或0；interval模型leading precision仍须保留在store type/列metadata，不能将其当参数Precision传入。新增DateTime mapping应同时处理默认、显式HasPrecision/HasColumnType、NULL、literal与clone。不得改decimal mapping。既有HasColumnType六位或scaffold精度不被默认七位覆盖。

最新EF pin应做真实候选包SaveChanges/读取/并发谓词/迁移scaffold；旧113014归档仅用于R3历史回归，不能替代TP-07最新下游。TP-06可先完成ADO A/B；C在Precision映射与EF实际验证前保持pending。

## 10. TP-02 可先实现范围与文件交接

TP-02在TP-00通过后可做§2纯结构、整数精度检查、现有明确layout编解码、CLR投影、format与pure tests。使用既有golden及人工vector；不修改DmSetValue/Command/Reader，不改连接profile默认，不宣称未知8/9/TLTZ/BDTA支持。初期可以保留旧 `DmTemporalCodec.NormalizeForWire` adapter，新的pure核心作为并行内部实现直到TP-03接线；不可同时制造两个最终策略来源。

profile registry的数据由TP-01分类结果填入；未知新写能力/新格式及TLTZ保持关闭，既有明确layout的精确读取保持，不将未完成某server版本实测误写为不允许读。TP-01 probe可与TP-02写入并行，但所有dotnet/DB始终由Low串行独占，编码agent不能用“pure test”例外开窗。

| 交接 | 唯一owner及接口 |
| --- | --- |
| TP-02→TP-03 | codec owner交pure API、单位表、golden和Low离线结果；TP-03接DmDateTime/DmTime/DmIntervalDT/DmSetValue/N2DB/必要OracleDateFormat；root另批准DmGetValue时间getter最小路由改为typed adapter，以保持七位读取；不动Reader生命周期/诊断/GetString最终策略 |
| TP-03→TP-04/05 | codec owner完成并验收最小DmGetValue路由、停止写共享helpers和DmGetValue并给文件SHA；reader owner接整份DmGetValue后再实施TP-05；binding owner独占Parameter/Command/DmSqlType/Binding/Plan及root批准的DmField/metadata decoder；reader owner独占DmGetValue/DB2N/Reader/DateUtil |
| TP-04与TP-05并行条件 | 原始metadata record先定接口；TP-04先完成来源capture接口后冻结给reader使用。DmField及decoder只能binding owner写；reader发现缺字段发请求，不自改。reader的DmDateTime需求回codec owner定点窗口，与reader停写协调 |
| 修改纯核心/API | TP-03/04/05任何owner都不自行侵入另一个owner的DmTemporal文件；由codec owner按明确请求定点改，所有受影响owner重新静态确认，再新freeze |
| TP-06 | 两侧接线完成+Astra/root审阅+所有owner停写，Low才执行新同包验收。首失败保留/释放窗口、定点交回，不修改冻结输入 |

TP-03退出必须证明公开legacy helper契约切换后既有七位Reader连续可用；任何七位Reader回退候选不得接受。最小路由涉及GetTimeTZ/GetTimestampTZ/GetTimestamp/GetDate/GetTime及TIME mode内部调用，由codec owner串行完成，之后交reader owner，禁止两者同写DmGetValue。

TP-00退出需将已裁决D01/D02和D03能力分层与inventory矩阵对齐；文件/证据snapshot由tp_inventory交付。当前没有需要扩大目标或放弃无损策略的技术阻塞。实库未知不写成通过；主profile极值、TLTZ旧可达路径处置、EF Precision兼容与同包身份仍是后续必要门。
