# TP07 E-v2 静态复核

2026-10-09。结论：**changes_requested / one_compile_blocker / runtime_and_combined_closure_pending**。仅复核E-v2九文件相对Low E-v1快照的五文件变化，以及R01/R02/R03必要产品/common接口。未运行SDK、编译、测试或DB，未修改E、产品、mapping、common或旧证据。R02观察设计为 `TP07-E-R02-observation-design.md`（SHA256 `69bf5c46cba298261f785e954d660c18c795e1de09629b1e6eb5d50d5a859f36`）。

## V2-B01：局部变量与回调参数重名，阻断编译

`CommandAudit.cs:98` 的 `Decoded(short request, ...)` 已声明形参 `request`；同方法for块`:132`又声明 `var request = ...Single(...)`。C#不允许内层局部变量遮蔽外层方法形参，属于 **CS0136**。这次是源码直接判定，未伪称已运行编译得到错误。

窄修只需将`:132–139`的局部tuple改名，例如 `publicMatch`，更新其成员读取；保留输出匿名属性 `request = Parameter(publicMatch.parameter)` 的JSON字段名及外层opcode参数。没有理由扩大hook或产品范围。修复须新freeze并审查差异，再交Low进行组合源码的实际构建；当前v2不能静态接受。

## R01与R03的限定复核结果

- `Models.cs`按kind把TIME/INTERVAL非整micro登记为ArgumentException，DATE非午夜和六位DT/DTO仍为OverflowException。`SixPrecisionCases`从有限registry选择精确leaf，不再合并所有native拒绝类型。
- `RuntimeRefusal`保存最多8层、无循环的真实异常type和SafeEvidence；只接纳精确leaf及精确DbUpdateException包装链，不将timeout/cancel/transport、工具断言或其它inner异常当成预期拒绝。并发的DbUpdateConcurrencyException单独精确匹配。拒绝step仍保持真实failed，独立validator必须按case/step/chain解释，不能全局忽略failed。
- `ConfirmAsyncWrite`要求已成功的EF async callback、同attempt的decode source、至少一个已发布binding，以及四项operation identity相符的真实6/13发送。registry先执行async facet6 micro成功控制，再进入其负例；负例必须在本进程已有control且ObserverFailed=false时取得stage业务增量0。sync明确为 `sync_send_unobserved`，没有用response0代替send0。
- 新 `FreshState`用fresh EF context统计owned表总行数，并消费现有common `Preflight`读实际column目录、核owner marker和唯一同名PK，再保存column数据与catalog hash。这里的PK范围是现有common核验的名字/数量，不声称额外检查了所有约束定义或PK column list。Mutation的winner后/stale前及stale失败后均为1行；rollback前/后均为1行，另由原fresh实体/N和raw oracle证明winner/原值。six拒绝前/后为0行且catalog hash一致，没有拿最终cleanup冒充即时收据。

## R02同次来源和实际分支复核

`CommandAudit`使用固定包已有 `AfterFrame` / `AfterStatementDecode`，保留现有 `AfterFrameSent`，安装前要求无其它订阅，部分安装失败或Dispose恢复原值。没有增加产品hook、手工Prepare、参数facet清零或读释放后statement。

参数化EF Executing建立唯一attempt，保存EF CommandId；完成/失败核command引用、stage和CommandId。decode回调只在request5观察，绑定到最近同四项invocation identity的成功response；从当前 `DmInvocation` / Lease / Session，command的statementSession/statementConnection，以及statement `f()`、`G().Session`、`G().Conn`交叉确认owner。每attempt事件限64、参数限32、名字限128；错误置ObserverFailed并让本门失败，不能吞成通过。

从仍由command拥有的statement `H().GetParamsInfo()`立即复制immutable raw数值，保留Source/TypeFlag/precision/scale/两个mask/ResultFormat。使用本次activePlan的只读 `ResolveServerParameter`取得真实server ordinal到公开参数的对应，再核clone与公开参数facet/value一致；没有凭collection索引、case.Kind或DDL推造来源。一次attempt第二次prepare不在本轮有限合同，明确失败。

时序再次核对：真实ParameterDescribe由 `T02_02000094.cs:324`capture，Prepare decoder`:475`先安装DmInfo，`:476`才RecordStatement；`DmCommand.cs:913–917`和`:1150–1153`随后才Bind/Execute。async Prepare同样走该decoder。`T02_02000093.cs:115`确实编码**request opcode5**；`AfterFrame`参数顺序是request、response、sql code、body length，当前E写法一致。没有依据要求response opcode也等于5，当前保存实际值而不发明相等断言是正确的。

`FixedSixCommandExpectation`核四个实际input参数、两个时间参数及各自raw family/width/source/flag1/scale6语义，保留另一列typed NULL显式Scale7。先校验全部tuple，再按server ordinal找首个拒绝：legacy16/23或TLS为NotSupported；shared extended26/27且该值非整micro为Overflow；只有所有时间参数可成功才期待成功。故不会忽略较早typed NULL的layout拒绝，也不会把显式7算成EncodedScale6。

成功路径从实际返回reader取得published binding，按同attempt/ordinal核EncodedScale7、WireScale6、RequestedScale7、ColumnScale6、VerifiedSeven及前后raw不变，并继续真实实体/fresh独立oracle。失败路径只引用回调时已保存的raw，当前binding明确null/未观察，没有把旧binding或推断值写成观察值。

## TLS仍有明确覆盖缺口，不能在本次关闭

受控ADO lane保持明确标识 `EF_mapping_created_W_parameter_controlled_ADO_execute_not_SaveChanges`，现调用自然ExecuteNonQueryAsync，不以手工Prepare改变路径。只在真实同次ParameterDescribe为已限定fixed7 extended26/27时接受NotSupported，并要求async业务0发送及fresh健康连接。它并未伪造advisory tuple。

有限查证未找到与当前精确SQL **`SELECT :v FROM DUAL` + EF mapping显式7 + TLS profile** 同一声明上下文的已批准advisory tuple。已接受TP06主工具用带TO_CHAR的derived SELECT，supplemental同样含TO_CHAR；不能把其来源直接移植为当前SQL的事实。当前未知advisory、无source、错误tuple均pending/failure，符合root允许的先保存真实收据再裁决边界。若TLS在metadata前拒绝，当前实现也会因缺source失败，不会假称fixed7/advisory已覆盖；它尚未实现设计中的独立profile-only早拒绝接受分支。

因此即使V2-B01修复，这里也只能给E工具的限定静态许可；未知TLS分支仍须L0先通过、root开放Low窗口后取得实际收据。不得为了消除pending跳过case、放宽flag集合、借其它SQL的tuple，或将两项controlled ADO当成实际EF SaveChanges。

## 冻结身份与后续门

实读Low `.local/temporal-native/verification/TP07-E-v2/source-manifest.json` SHA256：`b87a21b7b1a5edb5031a9b36dab391b44d0f1975b999bcf3c0c5403ab1ab4f48`。九个live文件及九个快照文件均逐项匹配manifest；aggregate按runtime-templates相对路径为 `d77ad1be328038e17482a51d0bb9765aa486a149d7b77acbebb6dadc0d893a6f`。

| 文件 | E-v2 SHA256 |
| --- | --- |
| CommandAudit.cs | `3108f1f6b02ef98b870857a454f8af75440303e34a2878f849350eaf90dad993` |
| Models.cs | `a9624b268c2c4fd35e35d30e4af336524aefebeb2189b463903bf529dd261bd6` |
| MutationCases.cs | `842260f11750e72ba742dd03bad9fa255db83c5494c7507035aa94770c82750c` |
| Program.cs | `39d421d1ffb7b0ee5dfddeee839c2ea02d21d5f03ae2481d71143ed0b5da4bdc` |
| SixPrecisionCases.cs | `d7ac87cb3c27bcd3f80322bb804b07eb3ecf5e629d1f14ad4282fafebfa7dbc4` |

其余GeneratedValueCases、RoundtripCases、TemporalContext、csproj与E-v1相同，原审阅范围不扩大。common/L0的其它门和运行由对应owner另证；本报告没有接受Python validator、完整组合source/locks、SDK构建或任何DB结果。新修复freeze后，仍需组合静态闭包与独立构建/合同门，L0先行成功才可执行E实库；最终C、scaffold/guarded Down、PR和发布仍未由此关闭。
