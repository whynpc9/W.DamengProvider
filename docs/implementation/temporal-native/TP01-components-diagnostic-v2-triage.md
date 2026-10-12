# TP-01 components diagnostic-v2 triage与raw oracle更正

日期：2026-10-08。结论：停止继续叠加CAST。root已接受将**独立解码真实ordinary返回cell的字段证据**用于精确分量核对，结合server FF9/FF7、独立请求expected和O/W交叉读取；原EXTRACT/CAST诊断保留为函数语义表征。此为明确的验收方法更正，不降低精度、不开放新类型，也不把原失败改成通过。

本次只读报告与缓存源码，未执行DB、dotnet或测试，未改工具/产品。原v7三lane失败和本次诊断全部冻结；新的v8实现与运行由root另行安排。

## 1. 诊断真实结论

- 安全摘要SHA：`29f14ccf4787b4f341046a7c144eb2307c8fa3841d2f35fa1aa30cdd317e8f92`。
- 实际报告 `actual-components-diagnostic.json` SHA：`a8b73f521dad194de7fc370854b7e9eaddb5ec492c6ee1eaa3358c1441d5b253`。
- 160记录，O/W各16向量；每lane只有5个整微秒源/投影精确。O其余9个source_not_exact、2个source_rejected；W其余11个source_not_exact。不能用这些已经丢tick的源证明原TSTZ→TS9路径正确。
- W对精确源的Q2 GetValue数值可精确，但SECOND实际CType11/System.Double，GetDecimal为InvalidCast；`DmGetValue.GetBigDecimal:508–521`明确不支持CType11的exact decimal投影。因此把“两种getter都成功”当服务器numeric成功条件也不合适，但修这一点仍不能恢复多数已不精确的CAST源。

Low另对已有诊断hex作只读IEEE754 SECOND解码，补证文件SHA `00c4d665fd9cc588594b6ac4108dd122601c6ae38f794d3ca0122497f87acabd`；CType11/width8原payload保留整微秒样本fraction，W GetDecimal拒绝不是payload没有fraction。该浮点诊断不引入任何时间接受tolerance；非整微秒样本Q0源本身已失真，仍不能归因于EXTRACT。

已证明部分typed存库TSTZ的FF9/typed ticks原本精确；诊断源CAST却不保值，所以不再盲加CAST或把源actual改为新的expected。另一个SS/FF9字符串同样不是独立numeric oracle。

## 2. cache是原始ordinary cell的源码依据

W `DmResultSetCache.FillRows:280–344`在`!isRsBdta`路径按每cell长度直接赋值：普通`datas[j][k]=msg.F(num7)`；65535长长度路径`msg.G()`转`F(d())`；NULL标记另存null。期间没有DmDateTime、numeric/temporal codec或日期正规化。

`Internal/Legacy/A/T02_02000092.cs:240`的F调用A(offset,length)，`:165`的A调用DmConvertion.GetBytes；该helper `DmConvertion.cs:222`只做范围检查和字节复制。`DmResultSetCache.GetBytes:84–88`直接返回`datas[datasOffset][ordinal+1]`；时间转换在随后DmGetValue/getter层执行。

另已将当前W DmResultSetCache.cs与绑定原v23包的producer manifest逐项核对：SHA `dbda075f901f490176915b691fc7e7009f56578f615546928a250d07cf8d33e1`、7666字节均一致。因此这里的FillRows/GetBytes源码事实可追溯到实际W基线身份，不仅是当前工作树的相似实现。

固定O net9恢复源码的 `decompiled/net9.0/Dm/DmResultSetCache.cs:276–329`及DmConvertion.GetBytes同样是ordinary直接复制链。该结论限定当前已固定并在运行中核SHA/MVID的O资产，不推广其它官方版本。BDTA走另一decode链，不纳入此证明。

因此可在任何temporal getter之前，从当前已fetch cell复制受限bytes作独立分量输入。它是协议完成帧拆分后的实际cell，不是encoder输出，也不是由FF9/CLR值反造。复制本身不引入时间转换；不读取认证帧或任意整消息。

## 3. 为什么满足独立oracle目标

保留三个互相约束的来源：①数据库TO_CHAR FF9/FF7；②从实际O/W返回cell字节由独立Python位置整数decoder还原year/month/day/hour/minute/second/ns/offset；③冻结规范请求与W公开typed/generic值。任一值或单位不一致都拒绝，没有tolerance。

Python decoder不能调用provider公开helper、TP-02/new codec、C# encoder或解析FF9来生成bytes/expected；独立手工golden作为自身正例。server格式文本与独立raw decode还可发现原时间getter的共同单位错误。原v2“独立分量核对”的精度与交叉源目标保留，只把不适用的TSTZ EXTRACT函数从“精确证明”降为真实观察。

这不是宣称多了一个独立SQL计算函数。方法名称明确为`independent_ordinary_packed_fields`；原`server_components_exact`、components/basis/error不覆写、不置true。优先只替换已经证实不适用的TSTZ精确分量门；普通TS原EXTRACT精确要求保持。root后续如扩适用范围需明确记录，不能让raw旁路洗掉已有typed写入或server文本差异。

## 4. 最小证据schema与布局

在真实ReadOracle主temporal ordinal（当前0）取值后、任何temporal getter前复制cell，新增raw证据至少含：

- `source=ordinary_result_cache_before_temporal_projection`、`capture_stage=before_any_temporal_getter`；reader lane/case/row/ordinal由所属oracle绑定。
- observable/is_null/is_bdta/result_format；实际CType、semantic scale、precision/width、mask与已有raw metadata来源分别保留。
- bytes长度、SHA256、uppercase hex；三者必须从同一复制的byte[]取得，hex解码长度及hash由Python重算。仅本次合成temporal字段、明确白名单和最大11字节；拒绝任意类型/整帧导出。
- 不预置一个笼统`raw_cache_verified=true`。采样来源由固定代码/实际身份链和具体字段共同证明；decoder另产方法ID、解码字段及精确比较结论。

确定ordinary布局：DATE14/3字节；TIME15/5；TIME_TZ22/7；TS16/8；TS_TZ23/10；TS26/9；TS_TZ27/11。实际CType/长度/format不符不能按请求类型补齐或猜同族；保留未知分类，先交root依据真实profile裁决。**未知12-byte、BDTA、TLTZ或非零装饰mask本次不解码、不标精确。** NULL也不使用numeric raw oracle，沿原NULL合同。

独立decoder建议用整个little-endian位置整数，而非复制TP-02逐byte函数：

```text
dateWord: year低15位，month bits15..18，day bits19..23
timeWord: hour bits0..4，minute bits5..10，second bits11..16
legacy: fraction bits17..36为微秒，转换ns=微秒*1000，高3保留位必须0
extended: fraction bits17..46为ns，高1保留位必须0
TZ: 尾2字节signed little-endian分钟
```

year按已知15-bit表示解码，但当前接受只针对已证普通CLR日历范围，不藉此宣传BCE历法支持。字段合法性、ns0…999999999、fraction与declared scale的整数quantum、TZ有无及适用范围都须独立检查；raw ns不可先/100截尾再比较。CLR投影检查另按ns%100及local/UTC域执行。

## 5. 独立golden与负例

手工golden至少包含已冻结官方TS9/TZ11的一tick向量、三最大日期向量、legacy微秒TIME/TS/TZ向量。用位置整数手算，例如123456微秒的timeWord为 `14 + 15*2^5 + 16*2^11 + 123456*2^17`，字节必须 `EE8180C403`；保留v2曾误写00的历史，不能重生成expected来自新decoder。

负例：flip第七tick/ns位、offset正负或分钟、日期字段；错误length/hex/hash；legacy/extended单位互换、未知reserved bits；scale8/ns1不合法；缺source/capture marker、把getter后值或FF9派生字段标作raw；BDTA/12-byte/TLTZ/mask非0；把规范expected与typed值一起漂移但raw/server不动；把raw/server一起漂移但规范expected不动。所有这些都必须使适用精确门失败。

synthetic raw样本可由实际C#采样函数的fake cache产生shape，但正确字段仍从独立手算表取得；证明该函数采自cache而非getter。普通正例和同族误标负例都须执行，避免“全部拒绝也通过”。

## 6. 交付边界

root已派v8两High互斥实现：producer限制raw采样/shape，validator侧独立decoder及合同。不修改产品，不复跑这轮CAST诊断；原EXTRACT真实false/error、旧O实际丢tick及dual-scale舍入全保留。新v8必须全offline后再按root授权完成shared/TLS与新validator，不能从v7旧hash直接补造缺失hex。

本次只有cache来源审查和方法裁决完成，没有新DB接受；旧报告/失败不回写。本文完成后停止写入。
