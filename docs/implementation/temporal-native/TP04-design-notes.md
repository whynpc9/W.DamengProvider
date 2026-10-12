# TP-04 准备补充：advisory describe 的原始来源

日期：2026-10-08。状态：`design_preparation_only`。结论：采纳root提出的最小更正——**所有ParameterDescribe参数都在真实decoder中保存immutable raw snapshot；ResultColumn仍仅时间相关列保存。** 这补充 [TP00设计](TP00-astra-design.md) 中“普通非temporal不创建snapshot”的范围：该句仅适用于ResultColumn，不能用于ParameterDescribe。原冻结文档不修改；TP-03/04尚未激活。本次没有dotnet、数据库、secrets或产品修改。

## 源码确认与证据边界

1. `Internal/Legacy/A/T02_02000094.cs:286–324` 的参数metadata decoder先读任意原始CType、precision、scale，再从flags得到TypeFlag1/2；没有限制advisory必须是时间CType。decimal9/scale129后续还会修改precision/scale，这也说明capture必须在任何归一化前。
2. `DmParameter.ResolveType:911–931` 只有可靠fixed TypeFlag1才采用ServerDescribe类型；TypeFlag2使用显式类型或CLR推断。`DmCommand.SelectBoundCType:1745–1764` 对mask0且类型不同的advisory调用resetType。`DmField.resetType:468–530` 同时重写CType、precision、scale：例如26变为width9/scale9、27变为width11/scale9，之后绑定还会选有效scale。因此只在“当前类型已是时间”时捕获，会错把客户端选型后的字段当服务器原始字段。
3. 源码中的明确合成样例是 `ResolvedWriterTests.cs:17,73–80`：CType2、TypeFlag2、precision64、scale0；`FixedTemporalBindingTests.cs:70–79` 另有advisory26→16、27→23的改型断言。组合源码路径说明generic/text advisory→CLR时间选型可达，但本次没有获得已验真的服务器CType2→26/27 trace，不将合成fixture冒充实库观测。TP-01后续可按raw/effective字段分别记录实际情况。

## 最小实现规则

- 保持root已批准的TP-04文件范围：`DmField.cs`、共享metadata decoder `T02_02000094.cs`，以及原TP-04绑定/plan文件和对应测试。不改协议请求、I/O、类型兼容规则或非时间值编码。
- 可以继续使用已接受的 `DmTemporalRawMetadata` 记录类型；名称虽带Temporal，`Source=ParameterDescribe`记录允许原CType2等非时间类型。类型定义无需因此修改。它只是时间绑定可能需要的服务器来源，不表示原描述具有时间语义。
- 在每个实际ParameterDescribe record完整读取且TypeFlag已知后、任何precision/scale归一化之前，显式替换 `DmField.TemporalServerMetadata`。保存原CType/precision/scale/typeFlag；协议没有独立mask字段时MaskBeforeNormalization=0，DerivedMask只记录真实适用的解释，不从非时间CType推造TLTZ。ParameterDescribe不是结果row，ResultFormat应Unknown/明确不适用，不能从默认bool猜“已观察ordinary row”。
- `SetCType/SetScale/SetPrecision/resetType`只更改effective字段，绝不自动创建或覆盖server snapshot。TypeFlag2仅表示advisory模式，不能单独证明来源；手工Synthetic字段即使flag2仍无server来源。只有真实decoder capture使Source=ParameterDescribe/HasServerSource=true。
- 结果列继续仅为时间相关CType/装饰类型capture；普通文本/numeric结果列不为此增加时间语义。参数非时间记录保存不会改变其已有numeric129转换、LOB装饰、binary sizing等行为。
- 时间binding保留raw snapshot与effective descriptor两个独立值。generic raw CType2/scale0与effective26/7不同是正常的重新绑定，不得要求相等，也不能将raw scale0当fixed时间列小数约束。fixed约束必须同时确认Source=ParameterDescribe、原TypeFlag1及可靠原时间类型；原generic advisory不能伪装成fixed列。
- 真实执行缺少所需来源时应明确unobservable/拒绝需要来源保证的绑定，不能从已reset的field重建“server raw”。纯synthetic测试可明确传入Synthetic描述，不偷置HasServerSource。

## 生命周期

| 位置 | snapshot行为 |
| --- | --- |
| `DmInfo.SetParaNum:96–105` | 新建DmParameterInternal/DmField数组；初始无server snapshot，不从上一statement复制 |
| Prepare decode `T02_02000094.cs:451–459` | 分配数组后真实解码；每条ParameterDescribe一份原始snapshot |
| `DmField.resetType`及绑定SetScale/SetPrecision | 原raw保持；只有effective字段变化 |
| execute response `T02_02000094.cs:665–666`、async `ResponseCodec.Async.cs:207–208` | `SetParamsInfo`转交原参数数组引用，不是深clone；immutable snapshot随原字段保留，不重新捕获客户端effective字段 |
| 再次真实describe/reprepare | 当前describe显式替换snapshot；清除上一次TLTZ/其他装饰来源。新分配数组自然从无来源开始 |
| `DmParameter.CloneCore:991–1023` / plan用户参数快照 | 复制用户声明、显式标记和value；不把服务器snapshot塞进public DmParameter。新连接/plan依据其自己的describe绑定 |
| 将来确需field复制 | 复制immutable record值，不能共享可变来源容器；没有真实来源的copy也不能变为HasServerSource |

同一个可变DmField可能跨执行被复用，所以绑定冻结时必须复制raw record及effective值，不持有随后可被reset的field作为“冻结描述”。这些规则不要求修改目前仅作数组引用转交的DmInfo代码；若实施发现新的复制点，先向root报告再扩大范围。

## 最小测试建议

1. 合法synthetic prepare响应包含CType2/precision64/scale0/flag2，分别绑定CLR DateTime/DTO；实际decoder snapshot保持原四字段，effective按已批准profile选26/27+7或legacy16/23+6。验证原/有效不同仍合法；不能仅手工调用SetTypeFlag冒充真实decode。
2. 同一输入分别sync/async经过共享decoder，raw字段一致；execute response引用转交后raw不变，后续reset/rebind仍不变。
3. 第二次describe改成fixed26/scale7或plain文本、减少参数数目/换连接，确认旧raw mask、type及来源不泄漏；每个SQL occurrence绑定保留自己的server描述。
4. 手工构造field并设置flag2，再resetType26，不产生HasServerSource；把该effective字段误标raw的负例必须被来源检查拒绝。
5. 原CType9/scale129的precision归一化正例：raw是服务器原值，effective保持原numeric规则；普通非时间参数payload/metadata及结果列行为无变化。LOB/binary原回归保持。
6. generic advisory原scale0不能限制新时间值为整秒；反例要求错误地将其当fixedscale0的实现失败。真正fixed时间列scale0则仍检查列约束。这是来源与语义分离的关键对照。

以上属于TP-04实施前的限定设计修正，需在TP-01/03依赖满足后正式派发；本记录不激活任何产品修改或验收窗口。
