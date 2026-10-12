# TP-03 接线准备设计：公开 helper、严格 writer 与最小 reader 迁移

日期：2026-10-08。状态：`design_prepared / implementation_not_activated`。TP-02-v3 已由Low完成目标56/56及同构建TypeTests218/218并由root接受，详见 [TP-02验收](TP02-acceptance.md)。本设计只读准备；TP-01尚未关闭，当前不得据此修改产品、启动TP-03或宣称任何新实库能力通过。

本文承接 [TP00裁决](TP00-astra-design.md)、[入口清单](TP00-inventory.md) 和113签名的 [机器矩阵](TP00-contract-matrix.json)，使用已冻结TP-02五个模块的实际API。TP-02旧失败、accepted core/tests/报告均不修改；若后续确有core缺陷，须另行定点解冻并重新验证，不能把改core夹进接线补丁。

## 1. 新确认的权威表示与公开别名边界

root已确认：**按入口固定权威表示，每次重新读取；不新增两个表示的优先级、自动镜像或跨调用缓存。**

| 表示与入口 | 权威来源 | 必须保留 |
| --- | --- | --- |
| legacy CLR/string/byte[] constructor；公开GetDate/GetTime/GetTimestamp等byte getter；GetByteArrayValue两重载 | private `dateTime` byte[]及该构造入口明确的layout/单位 | byte[] ctor保存调用者数组，GetByteArrayValue返回同一数组；调用者更改原数组/返回数组后，下次getter/encoder重读当前字节 |
| 默认constructor→valueOf/parseString；内部decode、encode、toDate/toTime/toTimestamp、semantic formatter | public `dt` int[]（[6]为ns）、`dtype`、`scale` | caller更改数组内容、替换整个dt引用、修改dtype/scale后，下次semantic操作必须使用新值；不能继续用上次缓存的DmTemporalFields |
| static legacy int[] Fast encoder | 方法参数int[]，下标6为**微秒** | 与对象public dt的ns数组是不同契约；每次检查当前参数，不猜单位或自动/1000 |
| DmTime(byte[])及字段getter | private `m_Times`保存的packed5/7数组 | 保留传入数组别名；不要constructor解码一次后忽略外部修改 |
| DmIntervalDT的conversion/encode/format | 当次public days/hours/minutes/seconds/fraction，private subtype/precision/sign语义 | 每次捕获并检查当前字段；不可缓存TimeSpan或旧Fields后忽略public mutation |

建议只保存“是否拥有该入口所需表示”和构造时已明确的布局种类等结构信息，不缓存解码值。数组长度不可变，但内容可以变；所有数值消费重新decode/validate。缺少该入口需要的表示时明确InvalidOperationException，不用另一表示补齐、当前日期或零值兜底。这个异常清晰化属于明确兼容变化。

例如 `DmDateTime.valueOf(...).GetTimestamp()` 原来没有有效byte表示，本轮不顺便开放为semantic typed getter。它的数值投影应通过现有内部 `toTimestamp` 路径；公开byte getter缺表示时拒绝。向byte构造对象赋值public dt仅影响原来以dt为源的semantic操作，不覆盖byte getter；反向同理。`parseString`不切换bytegetter权威来源。

**对TP00概括的明确细化：方案B只规定“legacy12-byte”的单位，不要求所有GetByteArrayValue都输出12字节。** `DmDateTime(byte[],prec)` 原来直接保存数组（`:437–441`），两个GetByteArrayValue（`:608–616`）直接返回该数组。传入packed3/5/7/8/9/10/11时，返回值继续保持同引用、同长度、同packed布局；不得为了方案B强制转换为12-byte。只有明确返回legacy12的constructor/DecodeFast等通路统一微秒。packed9/11即使含非整微秒，也可原样取出同一packed数组；真正请求legacy12投影时才Overflow。root已批准该边界，旧TP00文档和证据不回写。

raw数组访问器本身不被改为自动正规化器；构造入口校验初始结构，后续数值getter/encoder每次检查mutation后的值。接线不承诺这些公开可变对象可跨线程并发修改，也不新增锁/线程安全机制。

## 2. 已接受核心API的调用方式

| TP-02实际API | TP-03用途与限制 |
| --- | --- |
| `DmTemporalFields(Year,Month,Day,Hour,Minute,Second,NanosecondsWithinSecond,OffsetMinutes?)` | 每次调用的局部immutable快照，不能替代public可变源的长期权威值 |
| `DmTemporalInteger.FromDateTime/FromDateTimeOffset/FromDateOnly/FromTimeOnly` | CLR→raw ns；DateTime Kind不触发转换；DTO保local字段和offset |
| `ValidateUserScale`, `ValidateFractionalTicks`, `ValidateWireFraction`, `FractionalTicksExact` | 用户scale0…9、CLR tick quantum、raw ns量程/scale与ns%100分别校验；服务端packed scale先由可信metadata适配解码，不能把用户值mask后传入 |
| `ProjectDateTime/ProjectDateTimeOffset/ProjectDateOnly/ProjectTimeDateTime/ProjectTimeOffset/ProjectTimeOnly/ProjectTimeSpanExact` | 当前入口对应CLR投影；TIME锚点1900、hour24拒绝、local/UTC双范围；不是写入类型/能力授权 |
| `FormatExact(fields,scale,kind)` | 已知语义的精确default文本；不经DateTime降精度。显式旧格式模板仍走其兼容formatter，不拿显示投影作为数值中转 |
| `DmTemporalWireCodec.DecodeRaw(bytes,explicitLayout,semanticScale)` | 七种**明确packed布局**解码；不能只看nominal CType调用ForCType后忽略实际cell长度/format |
| `EncodeExact(fields,layout,scale)` | 通过值域、layout、scale及profile写门之后的最后纯编码步骤 |
| `FromLegacyMicroseconds(bytes,kind,hasOffset)` / `ToLegacyMicroseconds(fields,kind)` | legacy12微秒适配，非整微秒拒绝；TIME输出会填1900锚点，调用者须按具体旧入口保持raw日期形状，见§4 |
| `DmTemporalIntervalFields.FromTimeSpanExact/Validate/ToTimeSpanExact` | DAY TO SECOND规范分量和宽整数；pure支持scale7…9不是已开放interval wire；legacy writer仍先限定<=6 |
| `DmTemporalRawMetadata` | TP-04来源保存的结构已经存在，本阶段不伪造HasServerSource或根据归一化field反造raw flags |

新的连接/legacy桥接helper优先放在本阶段已归属的DmDateTime/DmSetValue内，使用internal/private方法，不改变这五个accepted core的签名或语义。若代码结构确需新 `DmTemporal*Adapter` 文件，先由root明确纳入codec owner范围，不能让TP-04/05同时写入。

## 3. 修改组 G1：DmDateTime 的 byte 域与公开数值入口

同一codec owner串行完成，先准备私有“读取当前byte表示→Fields”函数，再改各入口。它每次从当前数组读取，没有持久化Fields缓存。

1. **CLR legacy constructor**（`:531–595`）：保留已有flag可表达的DATE/TS/TZ语义、12-byte输出形状和prec的helper意义；scale用显式范围检查，legacy小数能力<=6。先检查DateTime在目标scale的tick余数再调用FromDateTime，最终ToLegacyMicroseconds。删除MathUtil.Round；不得给legacy flag偷偷输出9/11 packed或支持scale7。DATE输入有时间必须拒绝，不能通过只拷年月日消失时间。
2. **旧string constructor**（`:443–530`）：保持已支持flag及文本入口，decimal fraction全字符按ASCII整数解析，最多原来支持的语法，不使用double.Parse×1e6。legacy12目标必须整微秒；超精度非零尾数拒绝，不截断、不进位。flag未支持的旧路径明确拒绝，不生成全零buffer冒充值。
3. **byte[] constructor**：认可12-byte微秒以及明确packed3/5/7/8/9/10/11。12-byte检查fraction0…999999，低数值wrong-unit ticks无法自动发现，迁移文档直说输入必须为微秒；prec不作单位tag。未知长度拒绝。packed bytegetter可保留合法七位，因为它直接Fields→CLR，不先变legacy12。
4. **GetDate/GetTime/GetTimestamp/GetTimeTZ/GetTimestampTZ**：byte域每次解码，再按入口投影。GetTime/TimeTZ保留1900锚点；timestamp TZ投影保local/offset。GetDate和GetTime是显式局部投影，不意味着DATE/TIME writer允许丢其它输入分量。缺表示、非法单位范围和超CLR值分别明确失败。
5. **公开decode helpers**：DmDateDecodeFast→legacy12日期；DmTimeDecodeFast/DmDtDecodeFast/DmTimeFromRec4→legacy12微秒。extended ns%1000!=0抛Overflow，输入数组不被改写。合法whole-microsecond结果用正确offset字段位置；12-byte输入已经是微秒，不再乘10保存ticks。内部Reader不能再依赖这些public降到微秒的返回值。
6. **legacy byte[]/ref/int[] Fast encoders**：保持微秒参数单位；长度、calendar/clock、offset、fraction检查在pack之前。保留原来已明确接受的packed passthrough形状时也须验证内容，而不是把任意8字节直接称为合法timestamp；不拿public dt的ns数组自动当微秒。
7. **extended CLR encoders** DmdtEncodeFast2/Dmdt2TzEncodeFast2：保持9/11-byte原能力，以FromDateTime+明确layout EncodeExact连接，不经过legacy12；原官方+1tick和四offset golden不改。helper没有连接，不能据其成功推断profile开放。
8. **GetMicroSecond**（`:598`）：先验证ticks%10==0，再/10；现在直接/10会截掉第七位，必须消除。需要故意只取显示毫秒的旧格式helper不得借此函数进入数值writer。GetNanoSecond只对秒内ticks×100，保持checked。

byte getter的精确文本来自当前byte字段；显式format/日期或时间局部投影按原入口保留。DmDateTime.GetTimeInString/GetTimeTZInString的既有prec==0省略fraction、非零七位fraction显示合同与DmTime的六位显示不同，不机械统一字符串宽度；对不可在该显示合同精确表达的fraction必须拒绝或走已明确的精确格式入口，不能截掉第8/9位。数值writer不得继续调用GetTimeFromTimestamp等显示截取helper。GetTimeFromTimestamp/DateUtil.formatDate等带明确显示投影的旧签名不借此更换整套输出格式；它们不是精确wire oracle，相关非精度历史问题不在本次顺手修复。

## 4. 修改组 G2：DmTime packed5/7兼容

DmTime保存的是TIME packed位域，GetNano名称虽旧，返回单位继续为微秒。byte[] constructor要明确支持5-byte无offset与7-byte带offset；其它长度拒绝，不把12-byte当packed。

- root已裁决：5-byte输入的GetHour/Minute/Second/GetNano正常工作；GetTZ/GetTzByteArrayValue因没有offset明确InvalidCastException，不越界读[5..6]，不自动补offset0。7-byte输入读取完整signed Int16 offset，后续caller改原数组必须生效。此为原越界失败的异常清晰化，不新增隐式timezone支持。
- DateTime constructor、TimeEncodeFast(DateTime)、TimeTzEncodeFast(DateTime,short)在提取微秒前都验证整微秒和目标scale；用小时/分钟/秒明确投影，不将任意日期转换规则带入writer。GetNano本身是raw微秒值，不改成ns或ticks。
- **旧raw日期形状保留**：DmTime.GetByteArrayValue/GetTzByteArrayValue原来12-byte日期槽为0；核心ToLegacyMicroseconds(Time)会填1900。桥接层应仅调整该明确布局的日期槽为原来的零值，不能修改accepted core，也不能连带把DmDateTime.DmTimeDecodeFast历史1900锚点改零。此调整不涉及fraction损失或单位猜测。
- GetByteArrayValue不含offset，12-byte尾两字节只是legacy padding，不能据0推断它是TIME TZ；GetTzByteArrayValue仅对有offset来源保留offset。公开主驱动路径使用typed有无offset，不从padding推断类型；prec==0省略fraction、非零输出六位等display契约单独保持。公开byte构造会持有caller数组，字段getter按当前5/7长度重新解析。
- helper→TIMESTAMP的旧 `DateTime.Parse(t.ToString())` 在DmSetValue/N2DB中不可保留为数值桥：它依赖当天日期。当前公开parameter snapshot不接受DmTime对象，保留该能力守卫；这些内部跨类型分支无已定义日期语义时明确NotSupported，不凭空指定today或新的任意锚点。

## 5. 修改组 G3：semantic dt/ns、文本writer与能力门

### 当次快照和解析

semantic操作先捕获当前dt引用、dtype、scale和前8个必要分量，验证长度；ns来自dt[6]，offset的int.MinValue仍表示缺省/未提供，不能当真实分钟数。Oracle数组可含11槽的sign/default/PM字段；进入Fields之前按其明确格式归一为有符号offset，不全局假定每个int[]都用同样的第8槽符号位。

`parseString`/valueOf保留现有支持的日期分隔、时间、offset语法，使用InvariantCulture和ASCII数字校验，fraction右补至9位整数ns。先局部解析/校验，再写回当前semantic表示；不将text先转DateTime丢掉第8/9位。public dt被替换或改成坏长度/负fraction后，下次操作检查当前值；不得回用旧成功解析缓存。当前parseString会把传入字符串作为异常extraInfo，修改这些入口时去掉原值回显，保留安全异常类型/错误号。

DATE/TIME/TS semantic形状与calendar范围分开验证；无日期TIME不从OS日期填充数值语义；当前semantic parser把缺日期填成1900-01-01，这可保留为兼容存储形状，进入纯TIME Fields时按已明确TIME入口移除该锚点，不能按任意日期猜TIME；无offset普通TS不借OS时区转换。getter/display兼容所需1900锚点只在明确的TIME CLR投影使用。

### strict写入顺序（对CLR和文本同样适用）

```text
已知目标/可信metadata → 明确语义scale与time-zone kind
→ 已批准profile的该类型/格式/write-scale能力
→ 当次源Fields或CLR tick精度/范围校验
→ 用户与列可取得的独立约束检查
→ EncodeExact → 写入DmParamValue
```

普通Command inferred string遇到可靠时间describe，会走 `SetString:961–1011 → valueOf → encode`；旧NormalizeForWire对string直接放行，所以只修CLR Normalize不够。TP-03必须让这一可达文本路径在同一个**write capability**入口校验目标wire/profile和精度，禁止文本绕开8/9、TLTZ或legacy六位门。原SQL literal/CAST/server函数不改写，仍是服务器语义；参数文本与原始SQL不是同一个入口。

`DmTemporalCodec`当前profile规则只在NormalizeForWire的CLR类型分支。建议在现有codec增加/提取一个独立的内部写能力检查，由CLR与semantic text共用；数据取TP-01实际关闭后的root批准profile，禁止读取测试制品作为运行时配置。纯core DecodeRaw/FormatExact成功不授予写能力。修改旧codec必须单列为TP-03接线输入，不修改TP-02五个accepted core文件。

目标scale低于输入且ns不整quantum→Overflow，legacy ns%1000!=0→Overflow；类型/profile未开放（含显式scale7在TLS未证profile、scale8/9未证、TLTZ未证）→NotSupported。不能因为文本值恰好整秒就绕过明确高精度请求。字符串可以作为独立raw表示保留ns并精确格式化，只有进入writer时才应用写profile。

`encode:1818–2022`删除roundHalfup，使用当前Fields的整数检查后编码；不再修改 `column.scale/mask`以抹掉0x1000/0x2000。本阶段只在局部变量中识别可信metadata的现有标志；raw来源正式保存/解析由TP-04接管，不能反造丢失的raw来源。TLTZ只有TP-01关闭且root明确批准的转换契约才接入；没有证据时双侧保持NotSupported，不把它编码成普通timestamp。

`SetString`的advisory分支不能在解析后把scale强写6覆盖已有目标约束；已resolved writer参数按给定可信目标消费。尚未resolved且旧代码不能安全判断类型时保留原拒绝，不因strict parser可解析就新开26/27文本类型分支。原来没有支持的字符串→extended入口不自动扩展；一旦可靠describe确立已批准extended目标，若计划需要接通，应在TP-01对应profile和TP-04绑定门中明确确认，不借default switch扩展能力。

时间参数公开Precision拒绝、typed NULL前校验、类型来源稳定选择及所有用户/列双约束最终冻结仍属TP-04。TP-03 writer应保留这类输入信息的接口空间，但不把helper prec当公开Precision，也不借重构DmCommand/DmParameter完成后续任务。当前已能取得fixed目标scale时不得把它覆盖为用户scale后丢掉；任何需要绑定层传递新字段的缺口交TP-04，不能标全绑定契约已完成。

## 6. 修改组 G4：interval与整数日历helper

DmIntervalDT的fraction保持微秒；TimeSpan→DAY TO SECOND路径可复用 `FromTimeSpanExact` 后ns/1000精确转换，且先限制legacy secScale<=6、packed subtype6、leading1…9。TimeSpan.MinValue/MaxValue非整微秒仍拒绝，不能因pure core可承载scale7而开放wire。`millseconds` component constructor维持毫秒→微秒的原单位，用checked乘1000。

不把全部interval subtype强塞规范DayFields：QUA_H/HM/HMS、M/MS、S允许不同leading单位，其公共hours/minutes/seconds可能是大于日内范围的leading值。对这些类型先用宽整数转总时长/规范分量，再按目的subtype重组与leading限制检查；月/年类型不进入此路径。

`convertTo`当前会按半天/半小时/半分钟进位，`convertMSecond:961–982`会舍入；改为精确缩窄：目标未保留的下级分量必须全零，目标小数scale必须整quantum，否则Overflow。不得更改符号或进位以使值可表示。`encode(scale)`按目标转换得到的type/scale与分量一致地编码24字节，禁止只替换public分量却继续写旧scaleForSvr；成功后的既有公开mutation形态需在测试中明确，失败不输出部分编码。

public字段每次重读，不能“修正”用户修改为上次缓存值；signed magnitude语义在constructor/reset明确建立，混合正负或越范围的后续公共字段修改明确拒绝，不能靠Math.Abs/int取反隐藏问题。ToHours/ToMinutes/ToSeconds等明确mutator保留修改字段的行为，改为checked或宽中间值后验证目标int范围，不能unchecked wrap。

DmDateTime.AddNanoSecond/AddSecond/AddMinute/transformTZ使用semantic ns与整数分钟，保留原数组修改/返回约定，防止int加法在归一化前溢出；用宽中间值和负余数处理。`DmTimeEncodeTz`的msec标量保持ticks，直接构造Fields→CLR，不经过统一微秒12-byte；其zero-date自动DateTime.Today+1900等未定义补值不得沿用为数值来源，缺日期明确拒绝并列行为修正。历法predicate的真假含义和公开签名保持，不能借统一核心把CheckDate与DmTimeDataValidate的布尔意义对调。

1582折日、BCE服务器域与TZ恰好一次转换依TP-01。不得把legacy `DateEncodeFast`的10月5…14→15补偿复制进新无损writer；对服务器实际不能精确保留的域明确拒绝/不支持。未取证不能用pure Gregorian成功宣称服务器支持，也不因此删除既有普通Gregorian日期路径。

## 7. 修改组 G5：N2DB与OracleDateFormat窄兼容

N2DB是internal class，普通Command不经其fromObject；主要经复杂类型/FLDR，而公开守卫仍禁止。保持这些守卫，不新增provider object快照白名单。相关数值helper可调用相同严格adapter，去除DateTime.Parse(ToString)和舍入，但不重写其它numeric/LOB分支，也不扩支持的CType集合。

`N2DB.adjustTime`全repo仅有定义，当前未发现调用；不要为了消灭Round名字新增可达通路。可以保留该内部签名但改为已定义ns单位的精度验证，或显式拒绝无单位合同的旧用法；不得继续调用把dt[6]当六位微秒的OracleDateFormat.Round。源码事实与“未公开能力”分别记录。

OracleDateFormat.Parse的FF字段已经写ns（`:488–520`），Round却按六位解释，不能用于numeric writer。保留Parse/Format/SetPattern及nested helper签名；只修改确需的fraction整数解析/单位适配及错误回显。FElement格式化要先按9位ns补零再按显式FFn模板产生文本，不能把ns整数先按len补零导致`.000001`变成`.001000`。显式格式模板本身的日期/时间/fraction显示投影保留，不把该显示文本重新解析回数值；完整GetString对声明scale的精确性由TP-05最终formatter门验收。

当前模板缺省日期/时区取机器当前值（AdjustDateTime）；numeric parameter路径必须在进入模板解析前证明所需字段齐全或明确拒绝，不以系统时间补齐。未启用的NLS配置不能通过Builder打开。现有公开GetDateByString/GetTimestampByString是文本入口，支持语法要在新合同测试冻结；不得让它们成为numeric wire路径，也不得为了避开超精度检查用Convert.ToDateTime默默舍入输入。新Invariant文本解析若缩窄历史culture兼容，列入本任务行为变化，不改成无限制文化猜测。

DB2N文件归TP-05，本阶段不直接修改；共享DmDateTime内部toDate/toTime/toTimestamp改为实时semantic Fields→CLR后，DB2N原调用自然获得整数投影，但其自身type分派/格式和R3 LOB路径不动。DateUtil文件同样留TP-05，TP-03不抢写。

## 8. 修改组 G6：DmGetValue 最小typed路由，七位不可中断

root已批准TP-03 codec owner临时独占DmGetValue中的时间getter路由。须与公开DecodeFast切换**同一候选**完成，不能先接受一个七位Reader回退版本再等TP-05恢复。

建议新增DmDateTime内部静态读取adapter（参数含byte[]、DmField及需要的连接profile/timezone上下文），返回当次 `DmTemporalFields`，完全绕开public legacy12输出。adapter从已确认布局直接DecodeRaw，可信legacy12网络兼容另见§9；字段投影调用现有TP-02 API。

| DmGetValue方法 | 最小替换与保留规则 |
| --- | --- |
| GetTimeTZ | CType22→Fields→ProjectTimeOffset；1900锚点，NULL6081保持 |
| GetTimestampTZ | 22仍转GetTimeTZ；23/27→ProjectDateTimeOffset；其它InvalidCast保持 |
| GetTimestamp | 16/26普通→ProjectDateTime；23/27→DTO.DateTime兼容墙钟投影；14日期零点；15/22时间1900锚点；TLTZ仅证实路径转换一次或明确拒绝 |
| GetDate/GetTime（内部helper） | 原接受集合14/16/23与15/16/22/23保持，26/27旧拒绝不顺便扩大；先选允许语义，再使用同一raw adapter |
| GetObject的TIME true内部配置分支 | 去GetTimeInString→ParseExact，直接ProjectTimeSpanExact；默认false仍DateTime，Builder非默认配置守卫不变 |

不改public Reader generic分派：TZ GetDateTime历史可投影墙钟，GetFieldValue<DateTime>仍从GetValue得到DTO后InvalidCast。GetProviderSpecificValue对时间继续同GetValue；async时间已走同转换路径，不新增I/O或Task.Run。

不改 `DmDataReader` 生命周期、缓存取值/SequentialAccess前移规则、BeginReaderInvocation、CompleteReaderInvocationIfSent、分页own/borrowed诊断、LOB flow、Session/Pool/Transport。转换合法值超CLR只失败当前getter，不Broken；坏帧/长度与非法协议字段另依协议边界处理，不因为core同抛Overflow就把所有情况归为可恢复值错误。

GetString当前也经共享DmDateTime semantic decode，所以shared helper的修改必须保持合法8/9 ns文本和mask语义；TP-03不得借最小路由直接重写DmGetValue.GetString分派或完整schema策略。若因TLTZ关闭需要在共享adapter加guard，与v2明确兼容变化一致，并在TP-01 closed证据下单列，不能将普通8/9读门一起关闭。

## 9. TP-01必须回填的layout/profile门

TP-02 `ForCType`只是nominal映射。现旧DmDtDecodeFast按长度兼容8/9/12，DmTimeFromRec4还按10/11定位offset；这些源码分支不是当前服务器实际传输证据。

TP-01→TP-03交接必须提供：每profile的server version、type name/CType、raw/semantic scale、precision/width、mask/原flag来源、ordinary/BDTA状态、**实际temporal cell payload长度**及样本来源（O写/W读或W写/独立server读）、ns/offset/CLR预期、对应安全证据hash。TLS七位未开放写门与能读取明确layout是两个维度。

| 观察/缺口 | 接线规则 |
| --- | --- |
| 普通DATE3、TIME5/TZ7、TS8/TZ10、TS9/TZ11，metadata语义与payload对应有证据 | 显式选对应layout，校验实际长度与semantic scale；writer另检查root批准write能力 |
| nominal16/26或23/27与实际8/9、10/11存在同族兼容差异 | 以TP-01证据登记确切同族组合；不能直接ForCType后拒绝已证合法长度，也不能跨族将任意长度当值 |
| 公共helper的12-byte输入 | 单位已由方案B固定微秒；不需要服务器探测才能定义其公开buffer合同 |
| 网络结果12-byte普通/紧凑兼容 | 不能因为长度同public helper就断言微秒；须有实际format/字段布局/单位来源。若此前可靠测试/证据明确微秒，写专用网络adapter并保留；不能把public decoder改单位后原调用链继续套用 |
| 已存在且明确布局的合法普通8/9位读取，但某server版本未做新实测 | 依D03保留layout/字段校验的读通路，不新增无必要全局version读白名单；未实测范围独立标记，不宣传新能力 |
| 确实未知layout/格式/field语义或新扩展 | 明确NotSupported或协议值错误，不推测单位/丢尾数；若为当前必需主profile未获得的payload证据，TP-03激活/接受继续挂起，不能先删除原适配边界后以“不支持”当回归通过 |
| BDTA / IsBdtaRs非默认 | Builder守卫维持；仅既有内部fixture适配，未知12/13布局不开放公共能力 |
| TLTZ16/26、0x1000/mask4、db/session timezone | 必须回填是否server已转换及恰好一次转换结论；未证双侧NotSupported，不借普通7位能力推定 |
| 新8/9写、TIME>6、calendar/offset扩域 | 必须独立root批准profile；Decode/Format、DDL成功或O单方成功均不足以开放 |

未完成以上交接前，本文件只用于准备修改分组，不是High立即接线的授权。若TP-01只能确认某兼容路径不可观测，root须明确保留的旧适配范围或关闭的新增范围；编码者不能用猜测填表。

## 10. 113签名与验收任务分组

机器矩阵包含DmDateTime57、DmTime19、DmIntervalDT22、DateUtil8、OracleDateFormat7，共113个public方法/constructor声明；其中OracleDateFormat及nested类型本身不是公开provider API。保留方法签名、字段可访问性及已有ref参数/别名合同，不把113误称为113条全部已开放的ADO.NET能力。

| 组 | 本阶段修改/保留与必要检查 |
| --- | --- |
| DmDateTime57 | G1/G3/G4/G6按入口归类；所有ctor、raw访问、数值getter、Fast编解码、calendar mutator都有指定权威源。未触的显示/predicate签名保持，不顺手改输出语法 |
| DmTime19 | G2全组单位不变，5/7数组兼容、raw12日期形状及alias测试，typed writer不经字符串 |
| DmIntervalDT22 | G4的构造/reset/convert/encode/format/mutator；leading/subtype/sign不抹平；未开放parameter对象仍拒绝 |
| DateUtil8 | 文件归TP-05，TP-03只检查调用不得形成新numeric string中转；不改源码 |
| OracleDateFormat7 | G5必要的内部ns Parse/Format适配，签名不动，FF模式和错误不回显原值；不新开NLS选项 |

新测试至少分为：

1. **公开raw合同**：0.123456=raw123456微秒=CLR1,234,560ticks；decoder已不返回ticks；非整微秒extended→public legacy12拒绝，内部Reader七位成功并列。12-byte低值1固定微秒、>999999拒绝，不能声称检测低值ticks误传。
2. **mutation/aliasing**：byte ctor→getter→修改原数组→getter改变；GetByteArrayValue两重载ReferenceEquals及packed长度保留；semantic dt元素改动/整数组替换/dtype/scale变动实时生效；缺表示不从另一表示取值；两域同时存在时互不镜像；invalid mutation在数值消费时拒绝。
3. **parser/profile防绕过**：CLR与显式文本/推断string+fixed describe对同一小数得到同样无损结果；six尾tick拒绝、未知7/8/9/TLTZ profile显式拒绝、整秒高精度请求仍不降级；原值不进异常文本。原SQL literal作为独立服务器表征，不被重写。
4. **helper/interval边界**：DateTime Max三向量、DateOnly非零时间、DmTime5/7与offset0/负值、GetMicroSecond非整微秒、TimeSpan正负/极值、subtype缩窄下级非零拒绝、scale变化后的实际packedmetadata正确、public字段mutation被检查。
5. **reader连续性**：保留官方golden并把需要完整七位的测试显式迁到内部typed入口；同时新增public decoder新拒绝合同。公开Reader typed/generic/async、ordinary9/11七位、TIME锚点和旧GetDate/GetTime拒绝格无退化；合法转换错误后向前读与清理仍由既有owner处理。

每个测试先证明成功控制，再单独捕获负例异常，避免TP-02初版整段catch假阳性；手工wire expected用位置整数独立推导，不以新encoder产出替代expected。

## 11. 文件交接与接受顺序

激活前：TP-01正式关闭、profile/layout ledger回填、root明确codec owner文件清单；TP-02五core继续冻结。然后由同一High按G1/G2/G3/G4/G5/G6串行或分批准备，但公开helper单位切换与DmGetValue最小typed迁移只能作为同一可验候选接受。

TP-03独占DmDateTime/DmTime/DmIntervalDT/DmSetValue/必要N2DB及OracleDateFormat、旧DmTemporalCodec必要共用write门和DmGetValue最小时间路由；不写DmCommand/DmParameter/DmField/metadata decoder/DateUtil/DB2N/DmDataReader。需要越范围时先回root，不与TP-04/05共享写入。

High停写→Astra/root静态审阅→新freeze→Low单窗口：目标helper/text/mutation/reader tests，完整TypeTests及受影响Command/Async/Reader/R3分页诊断回归；主profile实际七位读取和严格writer改动由root按TP-01证据安排独立TEST验收。失败保存旧输入与TRX后释放窗口，定点修复新freeze。没有必要直接启动无关大LOB压力门，但公共生命周期出现diff即须回root重新确定验证范围。

TP-03根接受后，codec owner停止写DmGetValue并交付文件SHA和typed adapter接口；TP-05取得整个DmGetValue文件，TP-04负责绑定/来源metadata，两者才可按互斥范围并行。任何后续共享helper改动通过codec owner定点窗口，不得在验证冻结中追加。

当前只有设计准备完成；TP-03仍未开始实施，profile未知项及全部运行接受门仍等待后续正式关闭。
