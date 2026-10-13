# TP-02 Astra 纯核心代码审阅

日期：2026-10-08。基线：`fe89bd44abe99c1608499f71f4634a13e732c826`。状态：`changes_requested / runtime_not_run`。本轮只读审阅五个新内部类型、四个新测试类、TypeTests 项目的一行 fixture 复制项及 Luna 三个 fixture 文件；仅写本报告。TP-01 probe 尚未冻结，不属于本审阅。未运行 dotnet、数据库或任何 secrets 操作。

结论：纯整数核心的结构、单位和边界算法与 [TP00 已裁决设计](TP00-astra-design.md) 一致；未发现需要修改这五个产品文件的阻塞缺陷。当前测试交付有两项必须由 High 修复后复审的问题：一个 C# 可访问性编译错误，以及一个会吞掉成功控制阶段异常的 fixture 假阳性（format及scale7 control两处）。不能将当前输入交 Low 用实际编译来撞已知失败。

## 1. 阻塞问题与限定修复范围

### TP02-R01：公开 theory 方法暴露 internal enum，测试项目无法编译

- 位置：`tests/W.DmProvider.TypeTests/TemporalNativeWireTests.cs:14`，`public void IndependentLegacyBitVectorsUseMicroseconds(DmTemporalWireLayout layout, string hex)`。
- 类型声明：`src/W.DamengProvider/Internal/Types/DmTemporalFields.cs:9` 的 `DmTemporalWireLayout` 是 `internal`。公开类的公开方法参数不能比方法可访问性更低，预期编译器报 CS0051。`InternalsVisibleTo` 允许测试使用该类型，但不改变公开签名的可访问性规则。
- 唯一修复文件：`TemporalNativeWireTests.cs`。建议 InlineData 存 CType/int，公开方法接 int，在方法体内调用 `ForCType` 转布局。不可为修测试把产品 enum 改为 public，也不要削减五条独立 legacy golden。
- 复审标准：公开测试签名不再引用 internal 类型；CType14/15/22/16/23 与五个既有 hex/scale/fields 断言原样对应。Low 随后实际编译并执行，才能确定编译和运行成功。

### TP02-R02：fixture 预期失败捕获范围吞掉格式化和 scale7 成功控制阶段

- 位置：`tests/W.DmProvider.TypeTests/TemporalNativeFixtureTests.cs:30–35` 将 `expected.clr_exception` 作为整个 `Exercise` 的预期异常；`:70–77` 的 `format` 分支先调用 `FormatExact`，再比较文本，最后调用 CLR 投影。
- 触发：合法 scale8/ns10、scale8/ns90、scale9/ns1、scale9/ns99 都要求格式化成功而 CLR 投影 Overflow。若 `FormatExact` 错误地直接抛 OverflowException，外层 `Record.Exception` 仍与 `clr_exception` 同名，fixture 会通过，文本比较从未执行。
- 同型问题：`:59–68` 先运行 `scale7_exact_control` 的 ValidateFractionalTicks(...,7) 与精确投影，再执行预期拒绝的scale6。若七位成功控制错误地抛Overflow，外层同样当六位负例成功。root独立检查发现此处，已纳入同一修复项。
- 唯一修复文件：`TemporalNativeFixtureTests.cs`。scale7 control必须在预期失败捕获范围之外完成；合法 format case 必须先成功返回文本并与独立 expected 比较，再单独验证 `ProjectDateTime` 的异常。`format_rejection` 的声明精度非法 case 则单独验证格式化阶段拒绝。可以按 operation 分派断言或返回明确阶段结果，不必改产品代码、fixture JSON 或生成器。
- 复审标准：在静态路径上，scale7成功控制的Overflow不可能通过scale6异常断言，合法格式化阶段的Overflow不可能通过CLR异常断言；合法文本和 CLR 分别有明确检查；ns100 正例检查精确 ticks，scale8/ns1 在格式化阶段拒绝，未识别 operation 仍失败。不得通过把 expected 字段改成更宽松规则修消费者。

`TemporalNativeCoreTests.ValidSubTickFieldsKeepTextAndRejectOnlyClrProjection` 已正确地分阶段覆盖六向量，这降低了同一缺陷逃过全部测试的风险，但不能使有假阳性的独立 fixture 消费者变正确。对其余分支的setup核对：DTO当前guard分支预期外层成功；raw-field读取和legacy12构造对固定JSON的字段范围已知，不包含另一个预期应成功却能以同型异常被吞掉的产品控制操作。所有期待异常应尽量限定到单一受测操作。两项修复应一次性完成，然后 High 停写、Astra/root 复审、重新冻结后再交 Low。

## 2. 算法与接口审查结果

| 范围 | 源码依据与静态判断 | 必须维持的接受边界 |
| --- | --- | --- |
| 不可变字段与 metadata | `DmTemporalFields.cs:5` 与 `DmTemporalRawMetadata.cs:8` 为 readonly record struct，仅整数/nullable整数/enum，没有数组或连接引用；metadata 原始 Scale 与 DerivedMask 不互相覆盖 | 尚未接实际 decoder provenance，D01 的 capture/reset/reprepare 必须在 TP-04 完成；本次仅接受结构 |
| 七种 packed layout | `DmTemporalWireCodec.cs:9–28` 明确长度和CType；DecodeRaw先精确检查长度再读字段；未知CType、未知layout拒绝 | 没有12/13-byte BDTA自动入口，不开放未支持结果格式 |
| legacy/extended单位 | WireCodec`:85–98` legacy20-bit fraction验证微秒范围后×1000；extended保留30-bit ns；`:121–128` 只有已验证scale<=6才/1000 | ns不能被当ticks；纯layout能编解码不表示某服务器支持写scale8/9 |
| reserved bits | WireCodec`:88,94` 拒绝extended高1位与legacy高3位；先检查，再合入fraction，避免mask静默吞字段 | 当前纯核心用Overflow表示非法fraction/字段；TP-03/05接线必须区分坏协议与合法CLR转换失败，不可按异常类型一概维持session |
| packed负年份 | WireCodec`:73–74` 是15-bit值0…9999正年、10000…32767映射为-22768…-1；Encode用低15bit还原；year0拒绝 | `-22768`是编码结构边界，不是数据库支持范围；真实profile域、BCE历法仍待TP-01，不能宣传所有该范围可写 |
| 小数scale | Integer`:19–32` 先验证用户0…9；小于7使用10^(7-scale)的ticks余数，scale7…9原生ticks精确嵌入；wire ns独立验证10^(9-scale) | 用户packed19/23/255不mask；server rawscale应先经未来descriptor解码，不直接传此API |
| CLR日历 | Integer`:80–87` 先检查合法Gregorian字段、年份1…9999/hour<24，再在整秒DateTime上AddTicks；只对秒内ticks×100 | 不改DateTime.Kind触发时区转换；1582旧折日未复制；Gregorian pure成功不是server历法承诺 |
| local/UTC双域 | Integer`:111–121` offset限定CLR±840分钟，local先投影，checked整数相减后检查UTC范围 | 这不是W当前writer的[-779,840]守卫；后者仅在独立fixture旧行为表征中调用 |
| TIME与hour24 | Integer`:52–57,97–109` raw hour24仅允许00:00 fraction0；formatter可保留24:00:00，所有CLR TIME投影拒绝，不取模/进位；兼容DateTime锚定1900 | TIME高精度和hour24服务器存在性尚未验证，pure算术不开放连接选项 |
| 精确文本 | Integer`:127–154` 先检查ns/scale再D9取声明位数；InvariantCulture保留前导/尾随零与offset，不经DateTime/浮点 | D03要求已知layout的合法读文本通道保留；此核心没有全局server版本白名单 |
| legacy12适配 | WireCodec`:136–166` 固定微秒；输入>999999拒绝，输出ns非整微秒拒绝；offset是否存在由调用者显式给出，不按prec或数值猜 | 符合D02B；尚未修改任何公开helper。低数值ticks误作微秒无法自动发现，这不是自动检测API |
| interval | `DmTemporalIntervalFields.cs:12–25,29–47` 使用BigInteger获得负数幅度并累计，leading1…9和日时分秒分量分别验证，最后检查Int64域 | TimeSpan.MinValue不取Math.Abs(long)，月不进固定ticks；pure scale7…9不开放legacy interval wire能力 |

`PowerOfTen` 的当前全部产品调用都由scale0…9或leading1…9验证限定，最大中间值为10^9，没有依赖浮点。完整DateTime ticks没有乘100，offset分钟乘ticks/minute也不越Int64。DateTime.MaxValue所在整秒加9,999,999ticks恰落末值，不发生隐式进位。

公开投影允许从字段中选日期/时间/墙钟部分，这是getter语义层的责任；写入前必须先应用layout/metadata约束，不得直接拿 `ProjectDateOnly` 代替DATE写入的“无时间分量”校验。TIME legacy12 adapter输出1900锚点，TP-03仍要逐入口保持历史raw日期形状需要，不能把“单位统一”泛化成所有公开raw字节形状完全相同。

## 3. 独立 oracle 与 fixture 消费

JSON 当前 SHA256 为 `4eee2002106920423c75a2126ac557b96e51bae090ee8e1af2518a179b62a609`，静态读取确认77条唯一ID、16种operation。所有记录保持expected_only/not_run/server_evidence=false；ticks/localTicks/utcTicks等以十进制字符串保存。

生成器只使用Python标准库日期ordinal和整数divmod/固定LCG，没有调用新C# codec、数据库或官方程序集；逻辑raw ns向量不先构造成DateTime。这提供与产品实现不同的calendar/ticks计算来源。文档明确JSON未伪造服务器wire或运行结果。

| operation组 | 消费核对 |
| --- | --- |
| project_datetime（13） | 将输入ticks构造CLR后取fields，与JSON独立日历字段及expected ticks分别比较；含Min/Max邻接、闰日/世纪、4个固定seed样本 |
| validate_fractional_ticks（14）、validate_interval_fraction（1） | 调真实核心精度校验；scale6额外7位control验证精确ticks；当前control异常可能被外层吞掉，由R02阻塞修复；负interval不错误构造成DateTime |
| format（7） | ns从逻辑字段进入formatter；覆盖8/9位合法值、非整tick与scale8非法尾数；当前阶段异常问题由R02阻塞，修后接受 |
| validate_user_scale（10）、validate_time_precision（4） | 明确set标记才执行纯整数请求校验；unset向量只验证请求形状，无绑定执行，不能将其计作有类型NULL/fixed/advisory已验收 |
| construct_datetime_offset_and_check_current_guard（8） | 独立expected local/UTC ticks检查新核心，再单列当前旧Normalize guard异常；不是新profile规则，不能让它替代TP-01服务器域取证 |
| construct_datetime_offset（4） | CLR构造器前置负例，未进入驱动；异常归属正确 |
| project_raw_fields_to_datetime_offset（2） | 从原始字段验证UTC越界，未先构造不可能存在的DTO |
| project_timeonly（2） | 最后一tick和raw hour24；前者检查ns/ticks，后者CLR拒绝；不是TIME wire七位开放 |
| project_timespan（5）、validate_interval_leading_precision（2） | 符号和ticks往返、leading边界；手工Min/Max分量在IntervalTests提供独立非自洽oracle |
| decode/encode legacy12、raw ns→ticks、raw ns→legacy12（共6） | 显式构造微秒raw buffer、检查独立ns/ticks及非整微秒拒绝，不猜单位 |

所有operation均有switch分支，未知operation默认抛错。部分预期字段是分类说明而非算法输出（如server_acceptance、decision、signPreserved），不据它们声明实库通过；实际值检查覆盖对应数值语义。fixture用一个Fact循环77逻辑case，因此不能把77直接当77个TRX叶子测试。

既有 `ExtendedTemporalPrecisionTests.cs` 和 `DmTemporalCodec.cs` 没有diff；官方五个extended golden被新CoreTests原样引用，未重生成旧证据。另有手工最大日期三向量、五种legacy布局golden、负fraction/leading/MinValue具体分量；不是仅做新encoder→新decoder自洽。负年份当前主要为BCE round-trip加源码位域核对，不能据此升级数据库负年份支持。

## 4. 产品行为与后续接线边界

源码引用搜索确认五个新类型仅在新核心之间互用，旧产品入口尚未调用。产品项目本身未改public API、writer、reader、profile、pool或diagnostics；TypeTests csproj唯一差异是复制JSON。因此本次不能宣称公开12-byte行为已切换、七位默认已启用、TLTZ已修好或EF兼容已完成。

TP-03必须继续按已批准的最小DmGetValue路由配套切换公开helper；TP-04才捕获raw metadata并冻结绑定；TP-05才完成getter/schema/恢复矩阵。底层raw错误与CLR投影失败在这里分API，后续不能把两个API混作一种错误恢复策略。R3 Reader invocation/物理关闭/LOB owner完全没有进入本次修改范围。

## 5. 独立 Low 验证方案（尚未执行）

只有R01/R02修复、High停写、Astra/root重新接受并提供新freeze后才能运行。此前输入快照与本报告中的初审问题保留，不用新文件覆盖旧快照。

SDK：仓库global.json的10.0.203，root已确认host `/usr/local/share/dotnet/dotnet`。Low选择全新的ignored输出目录、保存输入SHA，并设置：

```sh
export DOTNET_CLI_HOME="$PWD/.local/temporal-native/verification/<new-window>/cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

/usr/local/share/dotnet/dotnet restore tests/W.DmProvider.TypeTests/W.DmProvider.TypeTests.csproj --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
/usr/local/share/dotnet/dotnet test tests/W.DmProvider.TypeTests/W.DmProvider.TypeTests.csproj --no-restore --filter 'FullyQualifiedName~TemporalNative' -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers --logger 'trx;LogFileName=tp02-target.trx' --results-directory '.local/temporal-native/verification/<new-window>/target'
/usr/local/share/dotnet/dotnet test tests/W.DmProvider.TypeTests/W.DmProvider.TypeTests.csproj --no-build --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers --logger 'trx;LogFileName=tp02-types.trx' --results-directory '.local/temporal-native/verification/<new-window>/types'
```

以上是待执行模板，`<new-window>`由Low替换并先建可写目录；不能把命令文本当执行证据。源文件若在验证前又变，生成新freeze。不得设置默认TEST连接来跑纯核心；本阶段不需要wrapper或数据库。

按初审源码，目标新增类静态计数为56个xUnit叶子（Core39、Wire9、Interval7、Fixture1），其中Fixture1包含77逻辑向量；实际发现数/执行数以修后TRX为准，无skip/abort才可计通过。之后完整TypeTests验证未接线时既有type/golden回归；本次不要求无关大LOB/DB压力门。若修复扩大到产品路由，则必须退回root另定范围和门。

Low另做fixture hash/复制目标核对、冻结输入首尾hash一致性；生成器可在隔离临时输出重生并与固定JSON比较，不能原地覆盖冻结fixture。记录自然退出码、TRX和安全日志。任何失败停止并释放唯一窗口，不修改断言或原失败材料。

## 6. 初审输入指纹

| 文件（相对对应目录） | SHA256 |
| --- | --- |
| Types/DmTemporalFields.cs | `54067437489f14657444e728f2eda87a04a20c35c588f619c279cccb135e86d2` |
| Types/DmTemporalRawMetadata.cs | `7aae6184c8b12a25e276364743f17081c7a9acd6f92c743046532252c7584cac` |
| Types/DmTemporalInteger.cs | `3b7a1d0e6abf5e1681854df5602f5ff5863ecb7a59470d400915d074e3b7668a` |
| Types/DmTemporalIntervalFields.cs | `850aeb10a0147318b09040e2b77d60c215f088626e3ad325830e614a98d45938` |
| Types/DmTemporalWireCodec.cs | `85f00569743d1d920f48b0078263c8f727f4ed200e4f776b7229ce4cdad1e4ec` |
| TypeTests/TemporalNativeCoreTests.cs | `e146003b07a1f7c5c82e7c98c5e9684949d76138c7ad72a4552119c583254608` |
| TypeTests/TemporalNativeWireTests.cs | `863d963cd6e2349b9498e650bde25a4bfcf69f9eee223cd5fe9665e77570534f` |
| TypeTests/TemporalNativeFixtureTests.cs | `d10c728af7c659ad2debc2b34ef39fe7ad0ec68f8b983ceaf2241cc1c7869c14` |
| TypeTests/TemporalNativeIntervalTests.cs | `7ba385d94e85d550a2ab9fec7f364ef97b42010cb51adda336de689baa51ad0a` |
| TypeTests/W.DmProvider.TypeTests.csproj | `7cded1fbb6b291fea02190302d2c7d86f946a18a19b8b9fd3bb4c3e247e2ccd3` |
| fixtures/temporal-native/expectations.json | `4eee2002106920423c75a2126ac557b96e51bae090ee8e1af2518a179b62a609` |
| fixtures/temporal-native/generate_expectations.py | `03d9ab4e177a7b80ea861551f46a2a947db263a9115ef1d92561b415d33e5e8b` |
| fixtures/temporal-native/README.md | `966245269ee7286668ffc940cec2c577f780f72c65f678dcc37738f387e85345` |

本报告只记录初审及必要修正，不含运行通过结论。
