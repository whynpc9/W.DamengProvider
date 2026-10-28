# TP07 首个 mapping 窗口限定决定

> 阅读提示：这是 EF 时间映射的历史范围决策，不是当前 EF 适配指南或执行授权。固定候选、状态和审批仅属于当时；当前驱动行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。状态：**root_scope_accepted / implementation_and_runtime_pending**。依据固定5863 EF的 `.local/temporal-native/TP07/v1/base-source`、已接受bridge及TP06的W合同。仅源码读取；P1的14项实际参数观察仍pending，本文不预填其Precision/Scale结果，不运行SDK/DB或修改产品。

## 1. 小数精度默认与DATE

| mapping来源 | 参数fraction决定 | 保持的模型/DDL语义 |
| --- | --- | --- |
| 默认CLR DateTime/DTO | 现有显式mapping Precision7→Scale7 | 仍TIMESTAMP(7)/DATETIME(7) WITH TIME ZONE，不改为动态profile默认 |
| 显式TIMESTAMP(n)/DATETIME(n)/TZ，或最终有效Precision=n | 用有效n，保留0；不得用`n==0`当未设 | 保留原显式声明、facet/clone；冲突按实际EF mapping最终结果核，不从值尾数猜 |
| 显式裸TIMESTAMP/DATETIME/对应TZ且**最终facet=null** | root批准的明确兼容政策：Scale6 | 保留裸StoreType与null模型facet，不偷偷把DDL/模型改(7)。须在支持profile补目录/默认精度runtime观察；不是对所有DM实例的缺省值宣言 |
| TimeOnly裸TIME、facet=null | Scale6；显式合法0…6照原值 | TIME仍是日内时钟，7+不开放 |
| DateTime→DATE | DbType.Date、Scale0、参数Precision0 | 非午夜参数与literal都明确拒绝；不取`.Date`、不截时分秒/小数。NULL保声明，合法午夜保持日期 |
| 默认TimeSpan→INTERVAL | fraction取Scale或6；leading取模型Precision或既有store声明 | leading不是参数Precision；不把DAY9当小数9，不用Clamp吞非法值 |

上述时间ConfigureParameter转译仅处理provider自己的mapping：有效模型facet先验证，再生成W参数Precision未设/0及Scale；W驱动非零时间Precision拒绝仍保留，decimal不受影响。DATE非午夜可在mapping阶段提前Overflow；即使提前拒绝，W自己的DATE严格门也不能移除。DATE literal必须在生成字符串前同样检查，不能仅依赖之后不存在的参数绑定。

裸alias `DATETIME2`/`SMALLDATETIME`在pin中也指向同一DateTimeStoreType；如首窗会触达其最终facet=null路径，按同一显式兼容政策列用例，不顺手扩大其服务器支持声明。TLTZ metadata可映射不代表其值可用，继续现有关闭合同。

## 2. TimeSpan→TIME：显式拒绝

pin `DamengTypeMappingSource.cs:150` 的TIME注册为 `[TimeOnlyStoreType, new TimeSpanTypeMapping("TIME", DbType.Time)]`。默认TimeSpan注册和CLR-only路径另指向INTERVAL DAY(9) TO SECOND(6)。W `DmTemporalCodec.NormalizeForWrite`明确拒绝TimeSpan写TIME，即使TimeSpan.Zero；已有 `FixedTimeDescribeNeverTreatsTimeSpanDurationAsTimeOfDay` 覆盖该边界。遗留writer中未走新binding的旧分支不能当已接受公开能力。

root批准在W隔离overlay明确 `FindMapping` 抛 `NotSupportedException`，提供可执行建议：日内时间用TimeOnly/TIME，时长用TimeSpan/INTERVAL DAY TO SECOND。只匹配最终请求CLR TimeSpan（含nullable归一后的情况）且store type base **正好TIME**，不匹配TIMESTAMP/INTERVAL，不用`StartsWith("TIME")`。在该明确组合进入fallback前拒绝，防止退到其它转换mapping。

TIME注册中移除TimeSpan候选，使声明与行为一致；TimeOnly仍为首项且唯一支持的TIME候选。store-only/scaffold的ClrType=null查询继续得到TimeOnly，默认TimeSpan/显式INTERVAL与TimeOnly裸/0/3/6均保持。已读pin相关测试未发现“TimeSpan→TIME必须成功”的断言；现有TIME(3)模型项为TimeOnly路径，不应受影响。增加直接FindMapping、nullable、模型构建与store-only/scaffold正反例，不能只验证手写helper抛错。

不在本窗增加TimeSpan→TimeOnly自动converter、不取模、不丢天数，也不把成功读内部TIME mode作为新写能力。该拒绝是W overlay明确能力边界，不能悄悄回写官方默认提供程序的公开合同。

## 3. M02：真实W reader与编译getter表达式

源码中W `DmDataReader` 实际是public class，并非sealed，但构造函数为internal；不依赖继承或mock覆盖getter。使用唯一候选PackageReference的真实W reader，test-only反射/`RuntimeHelpers.GetUninitializedObject`建立明确缓存fixture，沿已接受 `TemporalNativeReaderRouteTests` / `TemporalNativeReaderContractTests` 的最小形状设置列、raw descriptor、实际format、cache、statement/physical/session与execution lease。反射每个成员先核存在/类型/可见性，public bdta直接赋值或正确查找；不得重复此前NonPublic-only失误。所有fixture显式退休合成statement并正常释放lease，不进行Open/Execute。

优先DTO mapping选择实际W公开 `GetDateTimeOffset(int)` MethodInfo，并取消旧GetString→Parse表达式；已安装EF10.0.12 XML合同明确允许reader method定义在DbDataReader子类。M02从**实际EF service选出的mapping**读取GetDataReaderMethod与CustomizeDataReaderExpression，构造 `DbDataReader` 参数表达式，按MethodInfo.DeclaringType显式Convert后Call，应用Customize，再Compile成 `Func<DbDataReader,DateTimeOffset>`。不能在测试绕过mapping而直接手写调用W getter后宣称EF接线通过。

断言所选方法非static、ordinal参数/返回DTO正确；最终表达式无GetString/Parse旁路。同一delegate在真实W缓存reader上运行：六/七位正常值、scale8/9且ns100的精确值保本地ticks和原offset；合法非整tick ns1/10/99 Overflow，之后默认reader同列GetString仍精确；nullable表达式加IsDBNull guard返回null，直接typed getter NULL仍按原6081，TLTZ/未知format保持原拒绝。raw输入用独立手写bytes/literals和整数期望，不能用新的mapper/driver encoder生成期望值。

这证明**实际mapping接口的表达式可编译、调用的是W真实投影**，不能冒充完整EF自动生成shaper已运行。后续必须让真实EF query/shaper从该W候选materialize DTO实体/投影（含tracking/no-tracking、NULL、sync/async），在实库门另证；ToQueryString/手写delegate单独成功不等价于此门。若实际EF shaper对该子类method存在限制，停止回root，不自动退回宽松Parse。标准 `DbDataReader.GetFieldValue<DateTimeOffset>` 是可讨论的另一路typed入口，但不在同窗同时维持两条生产读取路径。

## 4. 首窗文件边界与未验证项

沿TP07设计的mapping/source与新tests范围；若复用facet逻辑，新增产品helper限定为隔离EF `Storage/Internal/DamengTemporalParameterFacets.cs`（集中fraction选择/量化/DATE检查，不读连接或设置写能力）。真实reader fixture新增在隔离测试项目 `TP07WReaderFixture.cs`，只用于离线测试，不能进入EF产品程序集或更改W内部API。对应mapping、literal、store-only/scaffold、compiled-getter测试需加入完整overlay/allowlist后再freeze。

P1未修正参数事实、首窗是否编译通过、裸类型在各profile的目录默认、真实EF shaper及全部DB行为均未由本文验证。范围决定不授权现在运行或修改被冻结输入；由root按依赖与High/Low窗口激活。
