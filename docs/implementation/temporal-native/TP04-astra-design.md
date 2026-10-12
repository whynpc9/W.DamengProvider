# TP-04 参数绑定与原始 metadata 实施设计

日期：2026-10-08。状态：`design_prepared / implementation_not_activated`。TP-03 尚未获得 root 整体接受；本文只准备下一依赖任务，不授权产品修改、构建、DB 或 TP-05 并行开写。TP-03 运行失败及既有证据不回写。

依据：[v2 计划 §4–5](../temporal-dotnet-precision-plan.v2.md)、[TP00 裁决](TP00-astra-design.md)、[advisory 来源补充](TP04-design-notes.md)、[最终 profile 交接](TP01-profile-handoff.final.md)。源码只读基于 `.local/temporal-native/verification/TP03-v5/source/` 的冻结树；source manifest SHA256 `1c2c36a46313c2e96d41552786d9beb5c3c40c6dc04816e7e4d8246e98bad54d`。该身份只是设计输入，不代表 v5 已通过真实候选门。实施前按最终 TP-03 接受版本检查相关 API 是否变化。

**当前 profile 来源待 TP-03 triage 纠正**：root 已指出探针的 logical `ConnProperty` 与产品 `GetConnInstance().ConnProperty` 物理协商值可能不一致，尚未确认实际 encoding/msg scope。本设计不把旧报告里的 logical 默认 gb18030 当成实际物理编码事实，也不新增UTF8白名单。下文“已证七位”只指经独立物理来源复核并由root接受的最终 profile；TP-04激活前须获得纠正后的身份和能力交接。旧报告与失败保持原样。

## 1. 唯一范围与冻结时点

binding owner 唯一写入范围为 `DmParameter.cs`、`DmCommand.cs`、`DmSqlType.cs`、`DmParameterBinding.cs`、`DmCommandPlan.cs`、`DmField.cs`、metadata decoder `T02_02000094.cs` 和本任务新 binding/metadata tests。新增内部记录放在上述归属文件中，不自行扩大到新的 `DmTemporal*.cs` 文件。五个已接受 pure core、连接/Session/Transport、Reader/R3 生命周期及旧 TP-01 工具保持冻结。

两个时点不可合并：

1. `DmCommandPlan.Capture` 在既有 plan gate 内复制用户参数，冻结声明与值；此时可能未 open、未 describe，不能伪造服务器 profile 或最终 CType。
2. `DmCommand.BindParameters` 在原 execution lease、真实物理连接和 statement ownership 检查之后，以当前 describe 与已冻请求生成本次每个服务器参数位置的绑定，再编码。用户值变化只重新验证精度/范围，不按 fraction 尾数重新选择 wire。

已有 `DmParameterMetadata` 记录 DbType、DmSqlType、Source、Precision/Scale 及显式标记，可扩充而不再旁读 public 参数。`ResolveServerParameter` 保持现有 SQL occurrence/distinct 映射；同名参数重复出现时，每个实际服务器描述位置分别绑定，不能以名字合并不同 fixed 列约束。SQL lexer、stream source 去重和 output/cursor/array 守卫不改。

## 2. 最小内部合同

建议以下成员名称作为交接接口冻结；实现可调整内部构造细节，改变语义须回 root。

| 持有处 | 不可变内容与消费方式 |
| --- | --- |
| `DmParameterMetadata` / plan 参数副本 | 原 DbType、原 provider 类型、`TypeSource`、两种显式类型标记及原声明值、`ExplicitPrecision/Precision`、`ExplicitScale/Scale`（含显式0）、值类型来源。`Source` 不能代替全部原声明信息 |
| `DmField.TemporalServerMetadata` | nullable `DmTemporalRawMetadata`；仅真实 decoder capture 赋 `ParameterDescribe/ResultColumn`。客户端 `SetTypeFlag(1/2)`、`resetType` 和 `SetScale` 均不能创造服务端来源 |
| `DmField.TemporalBinding` | nullable immutable `DmTemporalParameterBinding`，由 Command 本次绑定创建。至少含原请求、resolved source、raw record 副本、是否 reliable fixed、有效 CType/layout/width、列小数精度、请求小数精度、`EncodedScale`、完整 `WireScale`、zone/mask、profile identity/state/evidence、目标约束 |
| `DmCommandPlan` | 按服务器位置保存本次绑定数组，构造成功后整体发布；不持有 mutable DmField 作为冻结描述，不将服务器来源复制回 public 参数 |
| `DmField.GetTemporalReadDescriptor(actualFormat)` | 供 TP-05 只读消费的值记录：raw/source、当前 CType/width、按类型解释的 fractional scale、interval subtype/leading、zone/mask、实际格式；无用户 Scale、无写 profile 推断 |

`EncodedScale` 表示此次 value encoder 使用的小数位数；`WireScale` 是实际协议参数 scale（interval 为 subtype/leading/fraction 的完整 packed 值），两者不能在 interval 上混用。fixed 时间戳的 `DmField.GetScale()` 仍是服务器列语义精度；显式请求可能不同，不能为了让工具观察到 EncodedScale 而篡改列描述。

已有 `DmTemporalWriteTarget(CType, Layout, Format, ColumnScale, EncodedScale, RequestedScale, ReliableFixed, Mask)` 可直接承载 DATE/TIME/TS/TZ 约束；profile 使用现有 `DmTemporalCapabilityProfile`。对于 advisory，binding 的公开内部 `ColumnScale` 为 null；适配既有 target 的非 nullable ColumnScale 时可填选定 scale，但必须 `ReliableFixed=false`，不得据该占位数值声称存在列约束。INTERVAL 单列记录 packed 语义，不调用不支持 CType21 的 `ForCType`。

工具反射 `TemporalServerMetadata.HasServerSource` 与 `TemporalBinding.EncodedScale` 必须读到产品实际使用的状态。没有真实 capture/成功建立绑定时返回 null/unobservable；不为通过 probe 添加常量、推测来源或另写一份不参与编码的影子记录。

## 3. raw capture 与按类型解释

参数 decoder 位于 `T02_02000094.A(b,int,ref DmInfo)`；现有读取顺序为 CType、precision、scale、nullable、flags/TypeFlag，再读名称等字段，末尾有 numeric9/scale129 归一化。对**全部 ParameterDescribe**，先在局部变量保留原三字段及真实 TypeFlag；完整 record 读完后、任何 precision/scale/mask 归一化之前发布 raw snapshot。原 CType2/precision32767/scale0/flag2 等 generic advisory 也必须捕获。

结果 decoder `A(b,int,DmInfo,DmConnInstance,bool)` 只给时间相关 CType14/15/16/20/21/22/23/26/27 创建 raw record；原始字段取本次报文读取值（含带标志的 scale），在现有16/26清0x1000/0x2000前保存。普通 numeric/text/LOB 结果不增加时间语义。ResultColumn 的 TypeFlag 为结果列模式0，不从 parameter flag 复制。

协议未发送独立 mask 时 `MaskBeforeNormalization=0`；`DerivedMask` 仅来自该 CType 合法的标志解释。ParameterDescribe 的 ResultFormat 为 Unknown/不适用。结果 metadata 在明确拥有实际响应格式证据时可记录该格式，否则保持 Unknown；不能从新 field 默认 `isBdta=false` 推称已经观察 ordinary row。TP-05 读取 descriptor 时传入原 Reader 已知的实际结果格式，raw 未观测格式与实际 row 格式分开。

| 类型族 | 解释与约束 |
| --- | --- |
| DATE14 | fractional scale0；用户未设/0，非零合法用户 scale 为能力拒绝；值不得丢时分秒 |
| TIME15/TIME TZ22 | 普通小数精度按自身布局解释；写最高6，TIME7/8/9 不开放。TIME TZ 不新增 CLR 输入转换能力 |
| TS16/26 | 仅已定义0x1000 TLTZ、0x2000 Oracle-date 标志可拆；双标志、未知装饰不能被低位 mask 吞掉。mask4 保留并在值/写能力门拒绝；mask1 保留原完整 timestamp 语义，不投影成 DATE |
| TZ23/27 | 独立 offset 类型；不得套用 TS 的 TLTZ 清位规则；写能力按已有 profile。metadata 本身可报告合法8/9，写门仍关闭 |
| INTERVAL DT21 | 按完整 packed scale 拆 subtype、leading、fraction，拒绝未定义位/非法 subtype/leading；不做通用 `&0xF`。TimeSpan writer 保持 DAY TO SECOND、leading1…9、fraction≤6；其它合法 subtype 只保留既有合同，不借绑定新增开放 |
| INTERVAL YM20 | 不转换成 ticks；保留原 subtype/leading 解释。用户 Scale 不作 leading 别名，非零小数请求不支持；原 public/provider-object 能力守卫保持 |

合法 TLTZ metadata 和 NULL 结果不能因捕获阶段就不可见；写入的 typed NULL 仍必须走关闭 target 门。坏 metadata 结构/未知位不伪装成合法 scale；区分协议格式失败与已知但未开放能力，不将所有异常重映射为 Overflow。

每次真实 describe 显式替换 raw 并清除旧 binding/旧 temporal 装饰；参数数组减少、新 statement 自然不保留旧条目。`resetType/SetScale/SetPrecision/SetCType` 只改 effective，不重写 raw。`DmInfo.SetParamsInfo` 的已有数组引用交接继续沿用，同一个 immutable raw record 不需要深 clone；将来新增 copy 也只能复制记录值。共享 decoder 已由 sync/async 调用，禁止复制另一套捕获逻辑或修改异步 I/O。

numeric129、LOB/BFILE、binary sizing 等原非时间归一化逻辑保留。其原始值可以出现在 ParameterDescribe raw record 中，但不得用它的 scale129套时间精度规则；普通非时间结果不创建 temporal snapshot。

## 4. 用户声明校验与稳定选择

在已明确的时间声明上，PlanCapture 可早检；最终解析为时间目标后 Bind 必须再次检查（包括 inferred string→可靠 fixed temporal、typed NULL）。无类型 NULL 必须靠明确类型或真实 reliable fixed describe 解决；advisory 字段不能把 untyped NULL 变成有类型。

顺序固定：显式 Scale 先验证0…9，19/23/255抛 ArgumentOutOfRangeException；然后非零时间 Precision 抛 NotSupportedException；未设与显式0均不施加 Precision 限制，但两个状态仍分别冻结。随后才按 DATE/TIME/INTERVAL/wire/profile 校验0…9内的能力，再检值。不能在字段还可能最终解析为非时间目标时全局限制 decimal Scale/Precision；不改变 setter 的一般 byte 范围。

显式 DbType 与 provider 类型仍先按既有相容矩阵检查。`DmSqlType.DbTypeToDmSqlType` 可以保留 DateTime/DateTime2 同族映射；默认策略必须读取冻结的原始 DbType，不能从 DmDbType.DateTime 反推。两个显式类型同时设置的 DateTime2 同族交集按 §8 已批准决定执行。

可靠 fixed 时间目标必须同时有真实 `Source=ParameterDescribe`、原 TypeFlag1、可靠原 temporal CType，并按 raw 派生列语义；不能仅看客户端 field 当前 TypeFlag。客户端合成 fixture 可显式持有 Synthetic 描述供纯测试，不能冒充 HasServerSource 或开启未知 profile 的 fixed extended 六位例外。真实执行中缺失 required source 时明确不可观察/拒绝，不从 reset 后字段反造 raw。

fixed 优先保留服务器 CType/layout/width/列精度；显式声明仅校验同族语义相容。无 fixed 时按下表，由声明和实际 profile 一次确定，完全不看 fraction 尾数：

| 声明，Scale 未设 | 已证七位 shared | TLS六位/未知兼容 |
| --- | --- | --- |
| CLR DateTime 推断 | 26/7 | 16/6 |
| CLR DateTimeOffset 推断 | 27/7 | 23/6 |
| 显式 DbType.DateTime 或单独 DmDbType.DateTime | 16/6 | 16/6 |
| 显式 DbType.DateTime2 | 26/7 | NotSupported |
| 显式 DbType.DateTimeOffset 或 DmDbType.DateTimeOffset | 23/6 | 23/6 |

显式 Scale0…6 选相应 legacy 同族与该精度；Scale7 选已证 extended；8/9 当前一律能力拒绝，即使整秒或 NULL。DATE 默认0，TIME 默认6；TimeSpan advisory 保留已定义 DAY(9) TO SECOND(6)布局，不按时长或小数值修改 subtype/leading。`resetType(26/27)` 的现有默认scale9不能当策略输出，必须显式写选定7或请求精度。

未知/null identity 使用已有 CompatibilityLegacyUnverified：保留普通 legacy≤6；只有真实 fixed26/27、列≤6且未请求7才保留窄兼容。不按版本大小继承 shared。显式 DateTime2 遇真实 fixed legacy6 时采用 fixed6而非无 fixed 的默认7请求；显式 Scale7 对 fixed legacy 一律 NS，fixed extended6+用户7仅已证 shared target可通过，且值还必须满足列6。

## 5. 三种 scale 与实际编码

时间戳固定冻结：`RequestedScale = 用户显式值或null`、`ColumnScale = 真实fixed列精度或null`、`EncodedScale = RequestedScale ?? ColumnScale ?? 声明/profile默认`。CType固定于实际 fixed 目标；advisory 则按该 encoded scale稳定选型。field固定列描述不改，用户 Scale 也不被列精度覆盖。

每个非 NULL 值必须独立满足用户 quantum、可靠列 quantum 与编码 quantum。fixed26/scale6+用户7时 .1234560通过、.1234567 Overflow；fixed26/scale7+用户3时 .1230000通过、.1234560 Overflow。低用户精度并不把已存在列标为低精度；六位 fixed wire 无法因请求7升级。语义文本沿同一个 target/profile 门，不能先转 DateTime 丢掉 ns 后校验。

对 DATE/TIME/TS/TZ 直接构造既有 WriteTarget 并使用 `ValidateWriteTarget`，typed NULL 在写 marker 前经过该门；非 NULL 再经过 `NormalizeForWrite/ValidateWriteValue` 和最终 EncodeExact。目标 snapshot 内的 profile 取当前 statement 所属**实际物理连接**的 version/msg/encoding身份，即已有 `GetConnInstance().ConnProperty` 协商来源，并与原session ownership对应；不能使用public/logical连接属性中的默认值、工具 capabilities JSON 或plan创建时未打开连接的默认版本。最终scope白名单由TP-03 triage独立证据和root决定；binding owner不修猜测出的encoding来让profile命中。

INTERVAL DT 另外保存原 packed列scale、subtype、leading、ColumnFractionalScale、RequestedScale、EncodedScale和EncodingPackedScale；列subtype/leading约束不由用户 Scale/Precision改写。fixed interval的 EncodedScale 为真实列fraction，EncodingPackedScale保留列packed声明；用户fraction另行约束，不能让payload内packed声明与fixed协议metadata不一致。advisory则用请求或默认6构成完整encoding packed scale。用户先0…9范围，再7…9能力拒绝；合法≤6请求与列fraction分别约束，不沿用“只要不同就冲突”的旧快捷检查去代替精确双约束。构造编码用原已批准整数路径，低位非零与leading溢出明确失败，不舍入；fixed非DHMS TimeSpan目标保留现有拒绝，不强换subtype。

绑定描述必须成为实际 writer 输入。Command 完成 target/profile/用户约束后，将既有 `SetResolvedObject` 的时间 Precision override 留空（包括用户显式0），wire width由内部布局/真实describe决定；非时间 precision/size支路照旧。描述对应的 payload scale与协议header scale分别明确，不能用工具所需 EncodedScale 覆盖 fixed field.GetScale。必要 codec 交接见 §8。

## 6. 生命周期与失败边界

| 情况 | 必须行为 |
| --- | --- |
| Value替换，同声明/同CLR类型 | 新 plan 取新值并重验，CType/默认scale保持；整秒→七位、七位→整微秒不按尾数重选。新值不满足fixed/user精度则无业务Execute发送 |
| 推断值类型变化/typed变untyped NULL | 新 metadata/Source据真实声明变化处理；不沿用上次CLR类型掩盖无类型NULL。可靠fixed仍可提供类型 |
| Precision/Scale改动（含未设→0） | 显式标记参加 plan metadata equality；原 `StatementInvalid` 触发 reprepare。不为省prepare把非零时间Precision清0 |
| ResetDbType | 清两显式类型来源并按当前值重推断，保持既有Precision/Scale显式状态；新绑定不继承旧explicit DateTime2/default。未设和显式0各自不混淆 |
| Clone/CloneForPlan | 保留所有用户声明/显式位/值快照与原stream规则；不复制server raw/profile/binding到public参数。原参数后续变化不影响活动plan |
| 再prepare/新describe | 真实decoder替换每个位置raw，清旧mask/binding；减少参数数、generic advisory与fixed互换无旧来源泄漏 |
| 换连接/close-open/换物理session | 复用原session ownership检查使旧statement失效；open后重取实际profile。Command可保存prepared profile identity并与当前协商身份比较，不能把对象引用相同当作物理session相同 |
| Bind失败 | 开始本次绑定前使上次TemporalBinding不可作为当前结果；本次全部参数验证/编码成功后发布完整描述。失败保留原异常，清本次未完成描述，不允许任何业务Execute；旧raw仍来自真实describe，不篡改为合成值 |
| Reader接管statement | 成功描述随实际statement参数保留供live reader观察，不在plan Dispose时提前删除；ownership/close后工具应沿既有所有者路径变为unobservable，不能持旧statement伪造可观察 |

Command 缓存的 profile identity 用于明确失效比较，immutable binding 内不保存可变 connection、field、public parameter引用。每次执行在同一个既有 lease中捕获一次profile并消费；若需要额外的物理session版本标识，使用原所有权而不是新增连接探针或新全局缓存。

失败发生于 Prepare之后仍可能已发送describe；保证的是业务 Execute未发送，不把整个 invocation 的 SendAttempted伪置false。同步/异步共用Bind决策，既有取消赢家、failure translation、pool lease、statement/reader handoff及R3 cleanup receipt不改。不要为时间拒绝补写业务 Complete，也不手工将连接标Broken或恢复旧会话。

## 7. TP-05 与 codec owner 的交接

TP-04先完成并冻结 DmField的raw/descriptor接口和decoder capture；给 TP-05 exact文件SHA与接口说明后，reader owner只读这些文件。TP-05使用raw判定来源、用semantic descriptor报告真实列scale/type/mask，并结合真实row format选布局；不读ParameterBinding来猜结果列精度。8/9 schema仍报实际列精度，CLR不可表示由getter处理，GetString保留精确文本；这些getter实现不归TP-04。

`DmField.GetTemporalReadDescriptor(actualFormat)`按值返回，不把mutable field传给TP-05作为长期缓存。ResultColumn源码、参数来源两角色严格区分；typed NULL result不强行走写profile。interval的leading与fraction分别暴露给schema消费者，不把packed1574报作普通小数精度。未取得actual format时Unknown明确保留，不因public ordinary默认掩盖未知布局。

共享 DmGetValue、DB2N、DateUtil、DmDataReader由TP-05独占，TP-04不改。TP-05发现缺字段只向binding owner提请求；codec helper需求交codec owner定点改，全部相关owner停写/复核后才新freeze。五个pure核心不因接口方便改签名。

## 8. root 已批准的两个限定接口决定

1. **writer消费冻结描述的窄交接**：v5 `DmSetValue.SetResolvedObject` 目前只接 mutable field及precision/scale，内部重新捕获Target和Profile。root批准codec owner在TP-04接口冻结后，独立窄窗增加内部入口消费已冻profile/target/encodedscale（也可读取field上已冻TemporalBinding），数值和文本共用；binding owner不写DmSetValue/DmTemporalLegacyAdapter。不得仅增加工具可见property而实际writer仍另作选择。窗口由root安排、跨文件互斥、最终同候选整合验证；本设计批准不等于当前激活。
2. **双显式同族声明**：root批准 `DbType.DateTime2 + DmSqlType.DateTime` 相容时保留明确DateTime2默认7意图；provider声明仅限定语义族，单独provider DateTime仍默认6。必须冻结两种显式值/标记，不能因现TypeSource优先provider就丢原DbType；setter顺序不可影响结果，原有不相容双声明拒绝保持。

除此之外按已批准v2/final handoff实施，不扩大默认类型、日期域、8/9写、TLTZ、TIME精度或provider object参数能力。

## 9. 必要测试与接受门

High仅写以下有明确反例的新测试，Low在source冻结后独占运行：

- 声明矩阵：推断DateTime/DTO、显式DateTime/DateTime2/DTO及provider类型；unset/0/1…7/8/9/19/23/255 Scale；unset/0/非零Precision；fixed/advisory、typedNULL/untypedNULL、两setter顺序、clone/reset。decimal Precision/Scale原正例保持。
- 双约束与稳定性：fixed26/6+用户7、fixed26/7+用户3、fixed16+用户7、advisory generic2→26/27；同prepared替换整秒/尾tick/微秒时wire不随值跳变；新metadata/profile/session使旧绑定失效。unknown fixed extended6属明确内部fixture，不能声称本库已观测。
- 来源：合法完整响应走真实sync/async共享decoder，raw CType2/scale0与effective26/7不同；TypeFlag2手工synthetic不变server；raw0x1006与semantic6/mask4、0x2000、普通描述替换、双/未知标志负例；numeric129原值保留且effective numeric行为不变。所有ParameterDescribe、仅temporal ResultColumn的范围有对照。
- 实际编码：从完整Execute帧独立核对CType、header precision/scale、payload整数值及TemporalBinding；不要只断言property或调用被测encoder生成expected。正确控制先成立，负例只包目标操作；typedNULL仍见类型门，精度拒绝断言无业务Execute。
- 生命周期：重复SQL occurrence不同fixed列、少参数reprepare、连接重开/新profile、失败后重设合法参数；同一原R3 ownership路径，没有取消/lease/stream旁路。stream重复绑定、非时间numeric/binary/LOB和既有Command/Async/metadata回归按改动范围由root安排。

静态验收后新freeze，再Low目标binding/metadata测试、相关完整Type/Command/Async/Protocol套件及必要同包门；真实七位选择与binding metadata由TP-06最终同包验收。TP-04与TP-05不能拼旧产品包接受。任何失败保留输入/TRX/退出码后释放窗口，修复交回原High，重新冻结。

本文设计已冻结并停止写入。实施仍须TP-03整体接受、物理profile来源纠正后root重新确认能力交接、最终API/文件SHA核对及明确开窗；不提前关闭任何运行门。
