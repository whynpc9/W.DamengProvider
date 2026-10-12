# TP-05 Reader/schema 实施准备

日期：2026-10-08。状态：`design_prepared / implementation_not_activated`。基于[v2计划](../temporal-dotnet-precision-plan.v2.md)、[TP00的99格矩阵](TP00-contract-matrix.json)、[TP03 G6交接](TP03-astra-design.md)及[TP04来源补充](TP04-design-notes.md)。仅准备实施接口和验收；不运行build/DB，不改产品或既有冻结报告。TP-01实测profile/layout尚未最终交付，下面不预填支持结论。

## 1. 文件归属与启动条件

TP-03验收停写后，reader owner独占：`Internal/Legacy/Dm/DmGetValue.cs`、`Internal/Legacy/Dm.util/DB2N.cs`、`PublicApi/DmDataReader.cs`、`Internal/Legacy/Dm.util/DateUtil.cs`及新reader/async/schema测试。DmDateTime/OracleDateFormat和所有共用codec由codec owner保留，必要修正回交，不能双方同时改。

TP-04独占DmField、metadata decoder、参数/command/plan与DmSqlType。TP-05通过已冻结接口消费raw metadata，不能抢改这些文件。两任务可按原DAG并行，但raw descriptor的形状、角色和生命周期必须先对齐；未取得接口不能靠反射/重建raw flags作为产品旁路。

不改Session/Transport/Pooling/DmInvocation或R3 LOB owner、cleanup receipt；不新开Builder配置、public Reader方法、output/cursor/array/BDTA能力。

## 2. TP-03必须交给reader owner的接口

交接包需有实际方法签名、文件SHA与已验收测试，不只提供“已接codec”文字：

1. **内部原始读取adapter**：输入当前cell bytes、column语义/format及必要profile上下文，返回当次`DmTemporalFields`，fraction明确ns。它接受的是网络/缓存cell，不能走方案B的public DecodeFast→legacy12微秒中间结果。nominal CType与actual length的兼容表必须附TP-01证据；未知格式明确分类，不能猜12-byte单位。
2. **CLR投影与精确文本接口**：数值入口使用TP-02的ProjectDateTime/Offset/Time等整数投影；文本可以直接得到raw ns。合法非整tick值必须能供GetString使用，不能被adapter提前投成DateTime而丢失文本通道。
3. **TLTZ语义状态**：普通/TZ/TLTZ及是否已经db→session转换显式传递，值投影最多转换一次。未证TLTZ拒绝由明确能力门执行，不能有另一个formatter重新按mask转换。
4. **G6已完成路由**：GetTimestamp/GetTimestampTZ/GetTimeTZ、内部GetDate/GetTime和TIME mode分派已脱离public legacy12；七位Reader在TP-03候选内已连续可用。TP-05不得重新接回旧public helper。
5. **共享helper合同**：byte[]与public dt可变表示按入口权威源、public packed数组别名/长度不变；reader不得把public可变helper对象缓存为跨行结果。直接helper需要再改时回codec owner。

root已原则接受未来TP-03新增内部 `DmTemporalLegacyAdapter.cs`，负责明确来源layout、semantic dt/ns与Oracle offset、旧raw形状，且不含profile或I/O；DmDateTime可提供 `DecodeForReader` 薄入口。这里仅记录建议名称，必须等TP-03实际签名/责任分层冻结后消费，不假定文件已实现。codec owner迁移ExtendedTemporalPrecisionTests中两处public decoder的+1tick调用到typed路径时，官方golden保持，新public legacy12拒绝合同另列；reader owner不得借接线删去这两类验证。

若TP-01真实cell长度与G6假设冲突，先暂停依赖该组合的接线、由root/codec owner修adapter并新freeze，不在Reader用substring/除1000等局部补偿掩盖单位错误。

## 3. 具体入口与99格覆盖

99格是**11种getter × 9种列语义**：GetDateTime、GetDateTimeOffset、GetValue、GetProviderSpecificValue、generic DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan/string、GetString；列为DATE14、TIME15、TIME_TZ22、TS16/26、TS_TZ23/27、TLTZ16/26。继续用冻结矩阵的`getter+column`作为case ID，不在此复制第二张目标表。

| 入口/位置 | TP-05动作与必须保留 |
| --- | --- |
| DmGetValue GetString `:352–413` | 时间分支消费raw ns并精确格式化，不先GetValue/DateTime；文本/LOB/numeric/ROWID分支不重写 |
| DmGetValue GetTimestamp/TZ/TimeTZ `:543–593` | 复用G6 adapter；typed DateTime在TZ上保留墙钟投影，DTO保原offset，TIME TZ锚定1900 |
| GetObject `:788–812`、GetIntervalDtByMode `:680–699` | 保留TIME配置对应DateTime/TimeSpan、interval mode/packed1574返回形态；不为统一ticks改成新的公开对象 |
| Reader GetFieldValueOwned `:1769–1814` | 保持GetValue先行及exact cast；DateOnly只DATE14，TimeOnly只TIME15；generic DateTime对可表示TZ仍InvalidCast，不能借typed GetDateTime丢offset |
| GetProviderSpecificValue `:1007–1021` | temporal继续与GetValue同形态；仅numeric已有DmDecimal专用分支，不新增“高精度时间返回string” |
| GetFieldValueAsync `:1461–1489`、GetValueOwnedAsync `:1492–1503` | 时间复用同一缓存转换；保留token/termination检查，不Task.Run、不新I/O；LOB async分支保持 |
| DB2N toDate/toTime/toObject `:553–662` | 用G6/raw整数投影替换必要数值string中转；保持该内部helper原类型分派，特别TZ toObject现有DmDateTime对象形态，不强改为公共Reader DTO投影 |

验收每格先用可表示值固定正常返回类型或InvalidCast，再叠加适用的NULL、非法精度/范围和闭门规则。TIME true、IntervalMode非OFF、BDTA仍只有内部fixture；不能在真实连接打开非默认选项。99格与这些overlay的case/TRX计数分开，不把99当所有最终叶子数。

TLTZ正向格以“profile已验证”为前提。若本轮保持关闭，新coverage产物记录closed-refusal已验、open-contract未开放，不把两个正向TLTZ列的目标值伪称已读成功。原TP00矩阵是冻结设计输入，不回填/覆盖其历史not_run字段。

## 4. 精确文本与CLR失败必须分层

decode先保留ns，并检查layout/字段及真实列scale；随后按getter选择文本或CLR投影。scale8的ns10/90、scale9的ns1/99是合法非整tick：GetString必须分别保留全部声明数字与零，GetValue/provider/适用CLR getter抛Overflow。ns100可精确转1tick；scale8的ns1是声明精度非法，不能先截为0再格式化。

`GetFieldValue<string>`保持原exact-cast规则，不成为时间转string的替代入口。generic先GetValue，因此不可表示值可先Overflow，再无机会到InvalidCast；不为测试强行重排。GetString也不授予GetChars/GetBytes的时间转换能力：当前GetChars `:725–727`只接受文本/ROWID类型，其时间InvalidCast守卫保留。

DateUtil.formatMilliSecond按ns/真实scale校验后用Invariant九位补零及声明位数生成文本；精确合法输入的前导零、尾随零及scale0无fraction形态保持。已有formatTZ、formatYear、format2和显式显示模板的符号、空格、日期/时间分隔兼容保留，不顺手修formatDate等无关历史显示行为。非精确声明值由严格校验拒绝属于既定兼容修正，不用Substring隐藏不符合列scale的尾数。

OracleDateFormat文件仍归codec owner：显式FFn显示投影与“默认GetString按声明scale精确输出”必须分清。若当前可达连接格式模板不足以表达真实scale，不能拿其短文本再parse；先报root/codec owner确定精确路径或明确拒绝。被Builder挡住的自定义NLS配置不在本轮开放。已存在明确layout的合法8/9位读取按D03保留，不新增全局server-version白名单；写能力门不应挡合法只读文本。

## 5. schema/raw metadata交互与已批准窄修

TP-04交付immutable原始字段和语义descriptor：ResultColumn仅时间相关capture，原type/precision/packed scale/mask来源与归一化fractional scale分开；ParameterDescribe捕全部原类型是绑定来源规则，不能将参数fixed/advisory标志套到结果列。当前结果列TypeFlag=0，不能为了probe改成1。

GetDataTypeName继续取 `DmField.GetTypeName:409–423` 的真实服务端名称；GetFieldType/ProviderSpecificFieldType保持既有CLR映射，即使某值无法用CLR表示，也不改报string。schema NumericScale报告真实8/9，不截到7，不把0x1000等raw flag塞进公开scale；NumericPrecision/ColumnSize不机械替换成用户DmParameter.Precision或小数位。

**root已批准的现有不一致修正**：`FillSchemaColumn:1943–1955`目前调用不带scale的CTypeToSystemType；`do_GetFieldType:839–844`调用带真实scale的版本。INTERVAL21/packed1574下GetValue/GetFieldType已返回TimeSpan，但schema DataType仍可能是DmIntervalDT。TP-05只把schema类型选择调用改为同一带真实scale重载，并加对照回归，不修改GetValue对象或开启非OFF配置。DmSqlType本身归TP-04，不需要为此改写其映射表。

metadata/schema API不执行非NULL值能力检查：未知TLTZ仍可观察类型名、scale、raw来源与schema，不能因值NS而连metadata一起封锁。每次NextResult/重新prepare/新连接后消费本轮column descriptor，不能将旧column/raw snapshot缓存到下一result set。raw record immutable可复制，不能把之后会被reset的DmField引用当冻结值。

## 6. TLTZ、NULL与异常优先级

root已确认以下具体边界：

- 未证TLTZ的适用非NULL值路径（typed DateTime、generic DateTime、GetValue/provider、GetString）NotSupported；未来正向开启须TP-01证实CType/format/scale和转换责任，不能继承普通七位profile。
- typed GetDateTimeOffset原类型分派仍对非TZ/TLTZ拒绝InvalidCast；已验证TLTZ的generic DTO正常值也按原矩阵InvalidCast。**closed TLTZ的generic getter先GetValue，能力NS可以先于最后cast**；不为绝对InvalidCast改generic调度。这与高精度generic先Overflow的规则一致。
- NULL优先保持原合同：IsDBNull=true；GetValue/provider和generic object/DBNull返回DBNull；typed/generic具体类型与GetString抛DmException6081。NULL判定不进入非NULL值TLTZ/profile门；不把NULL返回空串、默认日期或NS。schema同样不受值门限制。

近期shared工具看到旧W把本地NULL6081附成Transport/Fetch/NotSent FailureInfo，这是已冻结的观察事实。TP-05不以此修改R3全局诊断策略；产品合同测试要断言真实NULL/6081及后续状态，不把所有Transport enum一律当真实网络故障，也不在产品加“6081永远可恢复”的宽松豁免。TP-01工具的known-null receipt是观察层策略，不复制进driver。

## 7. 向前读取和R3生命周期

合法服务器值超CLR精度/范围只是getter转换失败，不消费下一行、不重试/重放、不主动Broken/取消/释放lease。数值和GetString各自重新从当前cell/原始字段投影，不缓存失败后错误的截断值。

普通reader：失败后同列GetString可取得精确文本，下一列/下一行/NextResult/Dispose正常。SequentialAccess：现 `GetByteArrayValue:2142–2161`在获取field后已前移ordinal；只要求下一列/下一行和清理，不回滚游标、不复活已经关闭的LOB flow，不新增倒读权限。

保留BeginReaderInvocation/BeginInternalInvocation、CompleteReaderInvocationIfSent和TranslateFailure调用位置；缓存无I/O转换不制造success/transport span，不调用业务Complete来“修复”错误。取消/超时优先级、原token、pending physical close与pool permit仍由现有R3协调器处理。帧截断/损坏协议继续走现有协议故障路径；不要包一个catch把全部decode失败都伪装成合法值Overflow或都当连接故障。

必要恢复fixture：两行至少三列（合法非整tick时间、普通值、LOB；下一行可表示时间），覆盖普通/Sequential、sync/generic async、跨页/多结果；失败getter后向前读、metadata读取、Dispose/CloseConnection及取消竞态。测试只触已冻结owner流程；没有需求就不加大LOB压力。

## 8. 接受证据与待回填门

提交新coverage映射，把99个`getter+column` ID与实际测试方法/fixture/适用profile/返回类型或错误对应；支持、明确拒绝、内部-only和未开放各自标明。schema、NULL、恢复、格式culture和DB2N兼容是附加验收组，不靠“99数量齐了”证明全部行为。

TP-01必须回填实际server profile、nominal CType、真实cell length/result format、raw/semantic metadata、ns/offset与独立server oracle；本设计不猜TLS版本的七位read/write能力、TLTZ转换方向、TIME24或8/9写能力。普通已证七位和legacy六位回归必须保持；未开放扩展可明确拒绝，但不能把未知主路径回归当作合理拒绝关闭。

High停写→Astra/root审阅→新freeze→Low串行目标99格/overlay与schema/helper/恢复→相关Type/Command/Async/R3分页诊断回归→root按实测profile安排同包DB门。每轮失败保留证据、释放窗口再定点修复；源码/包身份不能跨轮拼接。TP-05接受以实际运行结果为准，当前仅设计准备完成，实施等待TP-03正式交接。
