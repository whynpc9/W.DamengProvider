# TP-00 时间入口、契约矩阵与基线清单

日期：2026-10-08。状态：`inventory_frozen / independent_verification_pending`。当前工作树 `/Users/wanghongyi/.t3/worktrees/W.DmProvider/t3-62747960`，HEAD `fe89bd44abe99c1608499f71f4634a13e732c826`。实施依据是保持原样的 [v2](../temporal-dotnet-precision-plan.v2.md)，原计划基线 `ed0c8a1` 已被当前合并 R3 基线取代。本清单只做静态源码核对、Git archive 与文件哈希；未执行 dotnet、构建、测试、wrapper、数据库或 secrets 读取，没有新的 A/B/C 产品通过结论。

[机器可读矩阵](TP00-contract-matrix.json) 有 99 个 getter × CType/语义格和 113 个公开 helper 方法/构造签名，含基线行指针、单位分组与产品/测试调用 token 清单。token 扫描不是动态调用图，常见同名方法与重载由本页的手工入口分组限定；没有调用 token 的公开 helper 仍是用户可直接调用的兼容入口。设计按 [Astra 冻结裁决](TP00-astra-design.md) 执行，D01/D02/D03 已由 root 批准，不重写 v2 历史文本。

## 1. 身份与历史快照

只读快照目录：`.local/temporal-native/historical-baseline/fe89bd44-tp00-v1/`。目录 0555，文件 0444。内容均为相对仓库路径；没有 ignored 历史运行、secret、认证信息或本地二进制的副本。

| 项目 | 范围 / 身份 |
| --- | --- |
| Git archive | HEAD tracked `src/`、`tests/`、`tools/`、`eng/`、`scripts/`、`.github/` 及根构建/包配置、AGENTS、gitignore；687 文件；无源码未提交差异 |
| archive SHA256 | `ecd2108ce617bdf3e53d8c57a111c3adf8a9fdeb776ad7e9c422923278d78c95` |
| snapshot manifest SHA256 | `192ef698cd764dd033df982f9d4f51281ea5c26c2c7bcd3a93ba9deb37252664` |
| 逐文件来源 | `source-files.json` 保存 687 文件路径、字节数、SHA256；manifest 锁定其 SHA |
| 历史证据 | `historical-evidence-files.json` 保存 tracked `docs/implementation/evidence/` 1546 文件逐项 SHA256，不复制内容；原 evidence 不回写 |
| v1 SHA256 | `06766581dddcb7bca41f427c005ea3a7da256fe1b930224cc04ae31bd86dce3e`，当前 untracked 设计输入，已在 plan-files manifest 记录 |
| v2 SHA256 | `1ae1bf2a0901bf178e525dc8dd82b55610beda921be8e71396d8524b0eb41249`，当前 untracked 设计输入，保持原样 |
| Astra SHA256 | `8199627cd1855d36d626503a4e5fca3cd29cdc49a344e0ec9bc9cffba5a90705`，单独设计文件，不属于原基线 archive |
| EF 后续 pin | `origin/main 5863d3755dbd2d333ac359bf1c0a789762adbaed`，本次只读 rev-parse 核实；原 dirty HEAD `aef25867` 不作为输入，不创建/修改 EF 工作树 |

当前 R3 历史状态由 tracked `evidence/T19/v23/root-final-readback.json` 记录为 `v23_full_defined_R3_matrix_accepted`，包含 shared gate；旧记忆中的 reduced-load/T17 pending 不适用于新基线，也不把 R3 通过当作 TP 通过。历史当前 W 对照身份：`0.1.0-r3.20261007143838`，package SHA `6696a26d89e4caa1b9e7c40a73f024c4df9d8a695da1446bdbecc17757d3bcf9`，DLL SHA `c70f7d4b7bd37187f55483d186d658206232a2e1f60a662530b3a0282e3231e5`。这里只读 tracked manifest，不声称本轮已验真 ignored 包。

`tools/TemporalPrecisionProbe/expected-identities.json` 的旧 W 是早期 R1 `0.1.0-r1.20260930051717`，不能直接拿来当旧 W 新基线或新候选。O 可固定 `DM.DmProvider 8.3.1.47463` 的 net9 资产，package SHA `62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75`、DLL SHA `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b`；Low 在实际加载前重新验证包内资产/加载 DLL/MVID。旧 W 原 v23 包如不存在，从本快照重建唯一 baseline 版本并标记 rebuilt，不冒充原包。

## 2. 可达入口与能力守卫

下面的源行均对应当前 HEAD；新实现改变行号后保留本清单作为历史基线，不回填旧行指针。

| 入口 / 调用链 | 当前行为及范围 | 后续处置 |
| --- | --- | --- |
| `DmParameter.Value → InferFromValue/ResolveType`，`PublicApi/DmParameter.cs:287-317,814-934` | 原生 DateOnly/TimeOnly/DateTime/DTO/TimeSpan 可被 plan snapshot；DateTime 推断 DbType.DateTime；DbType.DateTime2 被映射到同一 provider DateTime | TP-04 保存原始 DbType/来源，按 v2 稳定选择表 |
| `DmCommand.setInputParameter → SetResolvedObject → SetObjectCore → NormalizeForWire`，`DmCommand.cs:1712-1739`；`DmSetValue.cs:1563-1598,1622-1648` | 主 CLR 路径提前拒绝不精确 ticks；有类型 NULL 在 Normalize 前返回；user Scale/Precision 覆盖 codec 参数 | TP-03 严格整数 writer；TP-04 类型确定后、NULL 前校验 metadata；用户与列双 scale |
| `DmSetValue.SetString`，`:961-1011` | 时间字符串走 `DmDateTime.valueOf → encode`；`encode:1842` roundHalfup，不会因主 CLR Normalize 自动统一 | TP-03 受控文本解析、精确范围/scale 校验；不改写业务 SQL |
| `DmSetValue.SetTime(DmTime)` / `SetINTERVALDT`，`:1252-1283,1078-1097` | helper 分支存在，含 ToString/Parse；但 `DmParameter.CopyMutableValue(strictSnapshot):1034-1046` 不允许 DmTime/DmIntervalDT 等可变 provider helper 成为公开执行 plan 输入 | direct helper 兼容测试；保持参数 snapshot 守卫，不新增白名单 |
| `N2DB.fromObject → fromDate/fromTime/fromTimestamp/fromTimeTZ/fromTimestampTZ`，`Dm.util/N2DB.cs:582-810,975-1080` | N2DB 是 internal class；复杂类型 `ComplexTypeData.cs:421`、FLDR `FldrHashCode.cs:61-122` 调用。不是普通 Command 数值时间路径 | 公共 complex 值解码/metadata、FLDR 仍拒绝；保留 direct/internal tests，不借 codec 开放 |
| `DB2N.toDate/toTime/toObject`，`Dm.util/DB2N.cs:553-597,619-663` | internal class；通过 DmDateTime semantic ns/string → Convert.ToDateTime；普通 Reader 用 DmGetValue，不走这里。已有 R3 LOB 直接 DB2N 测试 | TP-05 必要 internal 兼容路径；不能从 public 普通读取泛化 DB2N 结果 |
| `Reader.GetDateTime → GetTimestamp`，`DmDataReader.cs:794-803`；`DmGetValue.cs:559-593` | typed 投影允许 TZ 丢 offset；扩展 26/27 与 legacy 同语义；TLTZ 查 rawscale 标志但来源已被归一化 | typed adapter 替代 raw helper 中间表示；统一 TLTZ descriptor |
| `Reader.GetDateTimeOffset → GetTimestampTZ`，`DmDataReader.cs:2073-2083`；`DmGetValue.cs:550-556` | CType22 明确转发 GetTimeTZ，保持 1900 日期锚点；无 offset 的类型拒绝 | 不能误删 CType22 历史投影 |
| `GetValue/provider/generic/async`，`DmDataReader.cs:1007-1020,1086-1123,1461-1503,1769-1814` | provider-specific 仅 numeric 9/24 特殊；时间同 GetValue。generic 按实际对象 cast，TZ generic DateTime 拒绝；async 时间复用相同缓存转换 | 保留入口投影差异、NULL、错误优先级和 Reader lease |
| `GetString → DmDateTime.valueOf(bytes,column,connection) → decode/toString`，`DmGetValue.cs:402-413`；`DmDateTime.cs:1567-1677` | semantic int[] 保留 ns，直接 formatter；declared scale 校验在 decode；TLTZ 看 mask4 | 精确 8/9 位文本继续可达；仅未知格式/语义或 TLTZ 门拒绝，不新增版本全局读取白名单 |

公开配置守卫：`DmConnectionStringBuilder.cs:1559-1629` 未把 `DbTimeToTimeSpan`、`IntervalMode`、`IsBdtaRs`、`Time_Zone` 列入启用项，非默认值明确 NotSupportedException；`DmConnectionSettings.cs:139-159` 生成默认 legacy 配置。默认 TIME→DateTime、IntervalMode.OFF、普通结果格式、默认会话时区。非默认 TIME/interval/BDTA 路径只能内部 fixture 回归，不能写成已开放真实连接能力。禁止为 TP 扩大 Builder 范围；TLTZ 异会话只能在专用 TEST 连接上找到已支持且可恢复/销毁的会话路径，不能更改实例时区。

`DmConnection.cs:1079-1089` 拒绝复杂 metadata，`:1736-1743` 拒绝 FLDR；`DmCommand.cs:1709-1710` 拒绝输出/游标，`DmParameter` direction 与 snapshot 守卫仍生效。批量/数组/重放不因精度统一开放。

## 3. getter × CType 冻结矩阵

`D`=DateTime(Unspecified)，`O`=DateTimeOffset，`T`=TimeSpan，`X`=InvalidCastException。表列是目标，普通格的对象投影与当前源码一致；TLTZ 是需要修复/取证的目标，不是当前已证明一致行为。16/26、23/27 在 JSON 分别列格；TLTZ16/26 也分别列格。

| getter | 14 | 15（false / true） | 22 | 16 / 26 | 23 / 27 | 已验证 TLTZ16 / 26 |
| --- | --- | --- | --- | --- | --- | --- |
| GetDateTime | D 零点 | D 1900 锚点 | D 锚点，丢 offset | D | O.DateTime，丢 offset | D 会话墙钟 |
| GetDateTimeOffset | X | X | O 1900 锚点 | X | O | X |
| GetValue | D | D / T | O | D | O | D 会话墙钟 |
| GetProviderSpecificValue | D | D / T | O | D | O | D 会话墙钟 |
| GetFieldValue<DateTime> | D | D / X | X | D | X | D 会话墙钟 |
| GetFieldValue<DateTimeOffset> | X | X | O | X | O | X |
| GetFieldValue<DateOnly> | DateOnly | X | X | X | X | X |
| GetFieldValue<TimeOnly> | X | TimeOnly 两模式 | X | X | X | X |
| GetFieldValue<TimeSpan> | X | X / T | X | X | X | X |
| GetString | 日期精确文本 | 时间精确文本 | 时间/offset 精确文本 | scale 精确文本 | scale/offset 精确文本 | 会话转换精确文本 |
| GetFieldValue<string> | X | X | X | X | X | X |

合法但不可表示的 ns 或 CLR 域超限，先表达 OverflowException；generic 先计算 GetValue 再 cast，所以高精度值可在不匹配类型拒绝前触发 Overflow，不新增时间→generic string 转换。GetValue/provider NULL 返回 DBNull，generic object/DBNull 返回 DBNull，其余 typed/GetString 保持 DmException 6081。GetFieldValueAsync 的时间通路与 sync 一样；没有新增 typed async API。

GetFieldType 保持 CLR 映射，`DmSqlType.cs:470-531`；schema NumericScale 当前来自 `DmDataReader.cs:1945` 的真实归一化列 scale，8/9 不能改报 7；GetDataTypeName `DmField.cs:409-423` 维持服务端名称。raw flags 单独留内部，不回填到公开 NumericScale。

TIME true 模式目前 `DmGetValue.cs:792` 用 GetTimeInString→TimeSpan.ParseExact，目标改整数 ticks。合法 raw hour=24 的存在性未证明；如存在，文本可保留而 CLR 投影 Overflow，不能取模/次日。两配置不改变公开守卫。

INTERVAL DT `DmGetValue.cs:680-687`：IntervalMode.DT/ALL 或 packed scale=1574 返回 T，否则 DmIntervalDT；provider 同 GetValue，generic TimeSpan 对 DmIntervalDT 调 ToTimeSpanExact。YM `:690-699` 在 YM/ALL 返回 total months Int64，否则 DmIntervalYM；不引入固定 ticks。非 OFF 模式仍内部 fixture。

内部 DmGetValue.GetDate 接受 14/16/23，GetTime 接受 15/16/22/23，26/27 直接 helper 保持 X。实际调用点：GetObject 的 `DmGetValue.cs:788,794`；同步响应 `A/T02_02000094.cs:566,563`；异步响应 `A/ResponseCodec.Async.cs:69,66`。后两者属于输出参数响应路径，正常 Command 守卫拒绝，不扩大开放。现 TypeTests 未见这两个内部 helper 的直接矩阵测试，TP-03/05 补独立直接测试，公开扩展 getter 路由不得误落历史拒绝分支。

## 4. 公开 helper 与单位裁决

两种内部表示不能混同：`DmDateTime.dateTime` byte[] 历史 legacy buffer 与 `DmDateTime.dt` int[] semantic ns。字段名 GetNano、msec、millseconds、prec 不能替代单位证据。JSON 113 签名覆盖 DmDateTime、DmTime、DmIntervalDT、DateUtil、OracleDateFormat；没有猜测旧 helper 是统一纳秒。

| 组 / 行指针 | 当前事实 | root D02 B 固定目标 |
| --- | --- | --- |
| DmDateTime(DateTime,..)，`:531-605`；string ctor `:443-530` | CLR ctor Round 后写 12-byte [7..9]微秒；GetMicroSecond=ticks/10；原 helper 可截第七位；prec 非参数 Precision | legacy 12-byte 输出微秒；严格 <=6 小数秒，无舍入/截断；显式7但只有 legacy 输出布局时能力拒绝 |
| DmDateTime(byte[],prec)，`:437-441`；GetNano `:680-688`；getter `:902-924` | byte[] 无单位 tag/归一化，getter 把整数当 ticks；与上一构造输出分裂 | 12-byte 输入 [7..9]微秒 0..999999，getter×10；低值 wrong-unit ticks 不可识别，迁移明确要求调用方转换 |
| DmTimeDecodeFast/DmDtDecodeFast/DmTimeFromRec4，`:1094-1282` | public decoder legacy微秒×10、extended ns/100，返回 12-byte ticks buffer | public 输出统一微秒，extended 非整微秒 ns%1000!=0 Overflow；内部 typed adapter 单独保留七位 |
| DmDateDecodeFast，`:1073-1093` | packed DATE3 → 日期 buffer | legacy 12-byte 日期分量、fraction0 |
| DmdtEncodeFast/DmdttzEncodeFast byte[] 与 ref，`:1285-1380,1440-1510`；DmTime fast encoders `:145-241` | legacy 12-byte fraction直接pack微秒；int[] legacy overload 下标6直接pack微秒 | 保持微秒单位，检查长度/字段/范围，不 mask 截断；int[]不是 DmDateTime.dt semanticns |
| DmdtEncodeFast2/Dmdt2TzEncodeFast2，`:1381-1438` | CLR 秒内 ticks×100→extended9/11纳秒；签名无连接 | 保持精确七位布局 helper；成功不代表服务器profile已开放 |
| packed byte[] constructor 的 3/5/7/8/9/10/11 输入 | 现 getter依赖packed bit路径，无法用prec统一说明单位 | 按明确布局解原始字段，可在对象CLR getter保留七位；若请求输出legacy12必须整微秒；未知长度拒绝，无13字节隐藏tag |
| DmTime，`:28-143` | packed5/7的20-bit fraction微秒；GetNano返回微秒；12-byte输出微秒；prec决定fraction文本是否包含 | 单位/签名不变；去Round、无静默七位丢失 |
| DmTimeEncodeTz(...msec...)，`:390-427` | 标量fraction validator上限9,999,999，直接getter以ticks消费；验证分支异常，不靠名称推断正确性 | 标量仍ticks，typed构造，不经过统一微秒buffer；direct golden和异常表征 |
| DmtimeAddByFmt/transformTZ/AddNanoSecond，`:177-307,2223-2330` | 日历/分钟偏移；semantic AddNanoSecond为ns；旧分支混合buffer getter | checked整数；TLTZ主链使用descriptor，不能以旧helper独立决定转换 |
| semantic parse/valueOf/encode，`:1567-1677,1818-2261` | dt[6]ns，字符串最多9位；encode roundHalfup；toDate/toTime/toTimestamp Convert.ToDateTime string | 受控文本保留；整数范围/列精度；删除roundHalfup，数值投影不经文本 |
| DateUtil.formatMilliSecond，`:13-21`；formatTZ `:24-33` | 9位ns补零再按prec substring；没有自身完整范围检查 | 先校验raw/scale，再格式化精确声明位数、零和offset |
| OracleDateFormat.Parse/Format，`:937-975`；Round `:824-936` | FF字段ns；内部Round按6位并Math.Pow；N2DB.adjustTime `:416-423`调用 | 解析/格式功能保留，数值入口整数严格精度；旧Round单位假设另测，不复用作无损核心 |
| DmIntervalDT，`:83-109,1191-1204` | TimeSpan→符号/微秒分量已用BigInteger；另有component/string/convertTo历史路径 | 保留packed subtype/leading，精确<=6；MinValue非整微秒拒绝；旧路径directgolden，无months→ticks |

兼容修正必须写进交付说明：public decoder原 ticks buffer→微秒、raw ctor误投影修正、超精度旧Round→拒绝、非法length/field拒绝；不可宣称签名不变就行为全兼容。现官方 wire golden 原样保留；引用改变helper的测试迁移到typed入口并保留原wire/ticks断言，新增public helper独立contract tests。

TP-03 同时做最小 `DmGetValue.cs` 时间 getter 路由到内部 typed adapter，保证 D02 切换不使七位 Reader 回退。范围仅时间转换，不动 Reader 生命周期/诊断/GetString最终策略；TP-03验收停写后整文件交 TP-05。不能等 TP-05 才恢复七位能力。

## 5. 绑定、Precision/Scale 与 metadata 来源

当前参数 `m_SetPrecFlag/m_SetScaleFlag` 包含显式0，setter `DmParameter.cs:320-345` 不做时间校验；clone `:991-1023` 保留 explicitDbTypeValue 和 flags，plan metadata `DmCommandPlan.cs:174-185` 保存 DbType/Source/Precision/Scale，resolved provider却合并DateTime/DateTime2。必须保存原始DbType，不能从DmDbType.DateTime反推。

fixed(typeFlag1)保留服务端CType，16/26和23/27同族兼容；advisory(typeFlag2)默认provider选legacy，resetType (`DmField.cs:468-531`)的时间宽度14=3、15=5、22=7、16=8、23=10，legacy scale6；26/27宽9/11和原scale9不等于已开放九位写入。当前userPrecision直接覆盖codecprec，不可继续把公开Precision混作wire width；当前userScale覆盖fixedscale，缺少单独列精度约束。NULL在 `DmSetValue.cs:1633-1641`早返回，必须在返回前校验已确定时间类型的Precision/Scale。

| 无fixed的声明 | 已证7且Scale未设 | 仅已证legacy且Scale未设 | 显式Scale目标 |
| --- | --- | --- | --- |
| CLR DateTime推断 | 26/7 | 16/6 | 0..6→16/requested；7→已证26；8/9独立门 |
| CLR DTO推断 | 27/7 | 23/6 | 0..6→23/requested；7→已证27；8/9独立门 |
| 显式DbType.DateTime/DmDbType.DateTime | 16/6 | 16/6 | 同普通TS，显式7才选择已证26 |
| 显式DbType.DateTime2 | 26/7 | NotSupportedException | 0..6→16/requested；7→已证26；8/9独立门 |
| 显式DTO/providerDTO | 23/6 | 23/6 | 同TZ，显式7才选择已证27 |

时间参数用户Scale首先0..9校验；19/23/255→ArgumentOutOfRangeException，不能 `&0xF`。DATE仅未设/0；TIME/INTERVAL目前最多6，0..9内能力不支持→NotSupportedException。非零时间Precision统一NotSupportedException（fixed/advisory/typedNULL都适用）；未设/显式0无Precision限制，与decimal无关。

fixed列实际scale和用户scale分开保存。fixed extended scale6/user7 `.1234560`可表示、`.1234567` Overflow；fixed legacy/user7即使整秒也NotSupported，不升级wire。用户scale更低时仍按用户要求拒绝不精确值；类型语义冲突绑定层明确拒绝。无fixed在profile未知时显式7请求不能整微秒回落。精度拒绝在业务Execute发送前，Prepare/describe可能已发送。

现 `DmTemporalCodec.ValidateFraction:80-88` 对encodedScale>9做低位mask，这是来源未分离缺口；packed server interval由 `DmCommand.ResolveIntervalDayToSecondScale:1780-1804` 分别提取subtype/leading/fraction，不可全局mask损坏位域。兼容helper `prec`维持自身格式签名，不受参数Precision禁用规则机械替换。

TLTZ 原始来源：`A/T02_02000094.cs:374-398`读CType/precision/scale，16/26的raw0x1000被清除并置mask4，0x2000置mask1；raw不是服务器独立mask4字段。`DmGetValue.GetTimestamp:570`只查CType16/rawflag，普通结果归一化后会遗漏；GetString `DmDateTime.decode:1671-1674`看mask4转换。`DmDateTime.encode:1825-1842`还可修改column flags并转换/round。当前不能称TLTZ一致。

root D01：TP-04窄增 `DmField.cs` 和共享metadata decoder `A/T02_02000094.cs` 写范围，仅保存provenance；async复用该decoder，必须synthetic sync/async响应验证，避免两套规则。记录原CType/packedScale/precision/typeFlag、mask-before-normalization及derivedmask、resultformat/source-kind，semantic列scale/timezonekind另存。未知位/双flag/新格式拒绝；reprepare/换连接/profile/reset/clone重建冻结descriptor，不继承旧TLTZ。

会话时区 `DmConnProperty.TimeZone:353` 来自默认连接属性与响应更新 (`A/T02_02000094.cs:780` / `ResponseCodec.Async.cs:322`)；数据库时区登录响应 `T02_02000094.cs:117`。这只确认来源，不证明服务器是否已转墙钟；TP-01要独立raw/servertext证明恰好一次转换。普通Timestamp不因Kind转OS本地或UTC，DTO保localticks/offset。

## 6. R3交互和恢复门

Command detached plan、stream输入FreezeStreamingInput `DmParameter.cs:1011,1026`、binding sourceidentity去重、LOB兼容分支保持。错误“未发送业务Execute”不能改写为invocation从未SendAttempted，Prepare可发报文。值内容改变不能在16/26间漂移；元数据改变按既有prepare失效机制重建。

Reader cachedraw的转换失败不标Broken、不发取消/重试、不消耗下一行；`DmInvocation.TranslateFailure:65-102`没有terminalcause时保留原异常。普通模式测同列GetString、下一列、下一行/Dispose；SequentialAccess `DmDataReader.GetByteArrayValue:2142-2161`已前移ordinal，只保证向前读取和Dispose，不倒退、复活LOB。保留 `BeginReaderInvocation/BeginInternalInvocation/CompleteReaderInvocationIfSent`、跨页own/borrowed lease与诊断归因。

坏帧/截断/协议字段损坏另测现protocol故障边界，不全部包成Overflow掩盖错误。新增一行合法非整tick+普通列+LOB和下一行可表示时间，sync/async/多页/close/cancellation竞争恢复测试。池 `Internal/Pooling/DmPoolOwner.cs:10,279` 当前不复用session/resetting0，不改变capacity/physicalclose/lease、pre-send failure后是否discard策略。无必要大LOB/压力扩门；影响到共同代码的现Command/Type/Async/Reader/metadata与R3分页诊断门要重跑。

## 7. TP-01/02可执行交接与退出门

必需集合固定为：主8.1.5.60普通26/27七位、同环境六位回归，当前已声明支持且受影响的legacy/TLS profile。每次确认TEST/profile、准确类型/格式，不能把远端metadata复制到TLS。8/9写、TLTZ26和其他版本分别分类；已有明确layout/metadata的合法8/9读取仍精确GetString。旧TLTZ16源代码可达不能以扩展研究跳过，必须回归或完成明确拒绝的兼容处置。

**TP-01**：新建 `tools/TemporalBoundaryProbe/`、`eng/temporal-precision.*`。三lane固定O/旧W/新W身份；本轮初段O/旧W独立取证，候选在同包门后接入。记录Scale未设/0/3/6/7/8/9/19/23/255及Precision未设/0/7/9的实际DbType/CType/typeFlag/width/rawscale/semanticScale/mask来源；分fixed/advisory/typedNULL/prepare/reprepare，保留官方失败作为表征。

探测必须包含Min/Min+1、Max/Max-1/Max-9、local/UTC/offset域、-14/-13/-12:59/+14、BCE/1582、TLTZ16/候选26和TIME hour24/>6存在性。O typedwrite→Wread与W typedwrite→O/服务器分量/FF7/FF9独立读；CAST/literal自身舍入单列，不当typed oracle。只有可构造DTO进入驱动，构造器拒绝组合是前置负例。未知服务器限制保留真实DmException Number，不猜范围。wrapper仅原repo TEST/TLS，启动本worktree绝对路径工具，不复制secret；唯一nonce对象/manifest、finally精确清理、新连接最终absence。没有固定连接时integration_pending。

**TP-02**：只写 `Internal/Types/DmTemporal*` 与新的pure TypeTests；合成数据按另派Luna owner。实施Astra§2 immutable原始ns字段、原生ticks字段、explicitlayout、raw/semanticmetadata类型、checked精度/范围、CLR投影与精确format。只秒内ticks×100，负interval宽整数，local/UTC双边界。沿既有3/5/7/8/9/10/11 wiregolden；0..6quantum、7位所有ticks、8/9合法rawns与ns%100投影约束分开。手算极值和固定随机seed，不仅自编自解。初期保留旧Normalize adapter，接线后只有一个策略来源；无建连、副作用探测、默认binding变化或新能力开放。

TP-03需TP-01/02先关闭；D02 legacy单位和最小DmGetValue routing一起验收，七位Reader回退不能接受。随后codec owner停写共享helpers及整份DmGetValue，交TP-05。TP-04先冻结rawmetadata descriptor接口，reader消费，不交叉写DmField/decoder；共享核心改动回codec定点窗口。所有High无dotnet/DB；Low仅root冻结后独占串行验证，首失败保留输入并释放窗口。

没有待用户再次选择的设计阻塞：root已批准D01/D02/D03。剩余未知均已指派门：TP-01 profile/calendar/TLTZ/time存在性，TP-02 directraw/golden纯核心，TP-04 dualscale/Precision/frozenmetadata，TP-05恢复和精确string，TP-06同包极值，TP-07独立EF pin的model Precision→参数Scale与迁移。未知不写为通过。

**本TP-00关闭条件**：Low独立核对99格/113签名、D01/D02/D03对齐、source/evidence逐SHA和archive范围/HEAD、v1/v2/Astra未变；root静态审阅后关闭。此页与JSON写入已停止，等待独立核查；TP-01/02尚未宣称激活或验收，A/B/C均pending。
