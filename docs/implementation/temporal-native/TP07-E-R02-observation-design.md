# TP07 E R02：同次 EF 调用的参数来源观察

2026-10-09。状态：**design_ready_for_root_review / implementation_and_runtime_pending**。仅补充 E review R02；本轮只读现有产品、TP06工具和E文件，未运行SDK、测试或DB，未改产品及E工具。旧 runtime design SHA256 `0a7363dfc3839428b0725e18a9a2b7961b5715bdf599659239333bb88234d861`、E review SHA256 `16ac5effd9e574aaebbadf20fbdd3ba034252b732d324a1234c92928114d0de1`保持。M01/M02 mapping-v4 的默认DT/DTO参数为Precision0、**显式Scale7**，不能改成unset来通过本门。

## 已存在的合法观察点

采用固定W包已有的 `W.Dm.Internal.Legacy.A.DmResultProtocolTrace.AfterStatementDecode`，类型为 `Action<short,int,long,long,int,bool,bool>`。这不是新增产品hook；已有 `TransactionProbe/Program.cs` 的 `TraceScope` 使用同一入口。回调参数依次是request opcode、ret statement type、row count、affected、column count、has result set、terminal，只保存有限数值。源码依据：

- `Internal/Legacy/A/T02_02000094.cs:292–344` 的真实参数decoder把原CType/precision/scale/flag及mask保存成immutable `TemporalServerMetadata`，Source=`ParameterDescribe`，ResultFormat=`Unknown`。
- 同文件`:438–476`为Prepare响应decoder。`:475`先调用statement的 `__t02_method_06000898(dmInfo)`，`:476`再调用 `RecordStatement(dmInfo)`。statement的 `H()`此时返回刚安装的同一DmInfo（`T02_02000064.cs:187–225`），因此可在callback内从command仍拥有的statement读取真实raw快照。
- sync `A.D(string)`经wire的prepare分支走该decoder；async `A.PrepareAsync` → `B.AAsync(...,false,...)` 同样走 `c.A(response,statement)`（`A.Async.cs:73–79`、`B.Async.cs:174–196`）。async在同一owned wire lambda内先 `RecordFrame` 再decode，`RecordStatement`的request opcode具有该exchange来源。
- `DmCommand.cs:913–917`、`:1150–1153`均在Prepare返回后才Bind/Execute；`:1708–1760`先清旧binding，完成全部writer后才发布本次binding。local拒绝可以从未发布本次binding。
- `AbortStatementAfterFailedExecution/Async`在EF CommandFailed之前清掉 `m_Stmt`/prepared metadata；此时不能补采来源。`AfterFrame`在decode前触发，单独依赖它也不足以取得本次新describe。

因此最小方案是在**仍有合法owner的Prepare decode回调内立即复制raw数值快照**；之后保存的是该时刻证据，不保留statement供释放后反射，不手工调用Prepare，不改变真实SaveChanges执行链。无需FirstChanceException、trace listener、polling或其他新事件。公共Activity/Meter由后台dispatcher输出completed操作，没有所需metadata与前清理时序，不用于此门。

## E工具的窄实现接口与身份要求

只在E owner的CommandAudit及既有case文件实现；不抢改common、mapping、产品或旧TP06工具。以下命名是建议，证据语义必须保留。

1. `Install`同时独占保存/恢复 `AfterFrame`、`AfterStatementDecode`及现有 `AfterFrameSent`。已有delegate非null则失败，安装失败也恢复已经安装的项。进程运行本门时仅一个command audit窗口；重叠/嵌套不能靠最后一个command覆盖，必须标观察失败。
2. EF `ReaderExecuting[Async]` / `NonQueryExecuting[Async]`建立一个bounded command attempt：stage、API、EF CommandId、工具command/connection reference ID、SQL hash、实际公开参数facet/value以及显式Scale/Precision位。guard assembly/package身份照旧。成功/失败callback关闭该attempt；取消/异常收尾也必须清除，不能把下个操作挂到旧stage。
3. decode callback仅处理此attempt的参数化command。反射读取 `DmInvocation.Current`、其Identity和Lease.Session；读取 `command.m_Stmt`、`command.statementSession`/`statementConnection`，校验statement `f()`回指本command，`G().Session`/command statementSession与current lease session一致，connection也为本command。校验当前invocation非disposed，identity各数值有效。不要仅靠Stage、SQL hash或可复用的server statement ID关联。
4. `AfterFrame`为当前attempt保存有序数值response receipt及同样的current invocation身份；Prepare decode receipt须对应同一invocation最近的opcode5成功响应。opcode3是allocation；opcode5只在本次已确认的参数化Prepare路径表示prepare，不能全局称业务Execute。观察到与窗口不符的response/owner或未知重入即失败。
5. 在decode callback中读取当前 `m_Stmt.H().GetParamsInfo()`，立即复制每个ordinal的原Source、CType、Precision、Scale、TypeFlag、MaskBeforeNormalization、DerivedMask、ResultFormat；记录 `capture_point=after_prepare_decode_before_bind`、sequence、request opcode、server statement ID、工具statement reference ID和完整四项operation identity。raw缺失仍如实记录缺失，不能从effective字段或DDL补齐。
6. 参数必须与本次实际请求匹配。记录server ordinal及实际server parameter name的安全关联，可限定EF生成的参数名或保存hash；结合本次active plan的只读参数绑定关系确认对应的实际参数（含NULL），不能假设参数collection顺序必等于server ordinal。若使用现有 `ResolveServerParameter`只读选择方法，仅允许读取本次active plan，不能新建/改写plan或metadata；异常标observer failed。
7. 成功callback可从仍活reader取得本次published binding；与前述Prepare raw来源逐项关联并核raw不变。失败callback只引用已保存的来源snapshot；若没有合法owner，`current_binding_observed=false/current_binding=null`。不得把pre-bind残留旧binding、控制case binding或计算的EncodedScale填成失败调用的实际binding。

回调只读有限数值/枚举/引用身份，不执行SQL、consume reader、prepare/retry、改参数或写metadata，不输出SQL全文、异常Message、连接串、frame bytes。callback捕获自身错误后置 `ObserverFailed=true`；正式断言在业务回调外执行，并使候选失败，不能吞成通过。每attempt固定事件/参数上限，超过即失败；结束释放对象引用并恢复hooks。

本次R02 fresh EF SaveChanges应有自然Prepare；不要求把“复用statement、没有Prepare”也扩成新合同。若意外复用且拿不到本次有效来源，不补一次手工Prepare，不以别次snapshot顶替，记录source pending并失败。将来若支持复用，须另证owner、声明和raw未换以及无未观察reprepare，沿TP06既有规则审阅。

## 分支与最小充分证据

来源判断只接纳完整且有同次身份的ParameterDescribe tuple。普通fixed6限定mask与derived mask均0、ResultFormat Unknown、TypeFlag1、Scale6；DT CType16/26，DTO CType23/27。对应precision宽度8/9/10/11须如实记录并按已接受布局核对；异常宽度不静默放行。无source、错误role、未知flag/装饰位/宽度或未批准advisory tuple为pending/failure。不能以 `TypeFlag != 1`统称已证advisory。

| 本次实际请求与来源 | 期望合同 |
| --- | --- |
| 显式Scale7 + fixed legacy16/23、列6 | NotSupported，含整micro、非整micro、typed NULL |
| 显式Scale7 + shared verified7 + fixed extended26/27、列6 | 成功时实际EncodedScale7、WireScale6；整micro/NULL可成功，非整microOverflow |
| 显式Scale7 + TLS verified6 | 七位关闭NotSupported；有describe时如实判fixed/advisory来源，没有则不制造来源 |
| DateTime2且Scale未设置 + 可靠fixed6 | 可以选择EncodedScale6，但不属于当前EF默认mapping参数 |

**整个command判定而非只取case.Kind。** 当前TemporalContext同时映射D和Z；selected值之外的另一个参数仍是显式Scale7的typed NULL。须观察全部时间参数并按真实server绑定顺序定位首个拒绝：较早extended非整micro可能先Overflow，较早legacy（包括NULL）可能先NotSupported。只有所有时间参数都满足成功条件才能期待SaveChanges成功。混合tuple若工具未实现完整有限规则，保留pending/failure，不能只看选中列或任选一个raw样本通过。

拒绝的充分收据是：真实公开EF参数 + 同次decode raw + 精确有界异常链 + async业务6/13发送增量0 + fresh行数/owned schema不变；本次binding未发布不是失败证据缺口。sync发送门继续如R03诚实标未观察，不以response0冒充send0。成功时还须实际published binding和实体/fresh raw oracle闭环。async成功control必须有匹配四项identity的非零6/13帧，以证明hook在该run实际工作。

## TLS在metadata之前拒绝的独立收据

TLS lane不能因没有ParameterDescribe而伪造fixed或advisory tuple。若实际早期拒绝，单独记 `source_branch=profile_only_early_refusal`、`parameter_describe_observed=false`、`current_binding=null`，保留实际调用API/阶段、EF mapping产生的显式Scale7/Precision0及值/NULL、物理verified6身份、真实NotSupported异常链、两个阶段各自async send receipt及新连接健康/状态证据。这里证明有限profile拒绝，**不证明fixed6或advisory metadata分支已运行**。缺失describe本身不能证明拒绝原因；必须排除mapping/observer/非法facet等其它NotSupported来源，不能只凭异常类型提升为已证profile门。

既有受控ADO advisory lane可保留其明确标识与原显式Prepare，但Prepare/Execute分开取证与捕获；Prepare失败不得声称Execute已调用或执行发送0已观察。若要证明特定advisory分支，仍须该实际tuple与对应调用身份，否则该分支pending。此文没有宣称当前源码已必然在Prepare之前作profile拒绝，也不允许为了强行取得metadata修改参数/模型/产品。

## 独立验收与边界

High完成后新冻结，再由指定reviewer核callback时点和所有权链；Low按root窗口运行。独立validator须拒绝：将ResultColumn冒充ParameterDescribe、跨attempt/command/invocation借用、旧statement/reprepare、缺derived mask、错误参数映射、未知tuple、observer failure、prefailed Prepare伪称执行、未发布binding填假EncodedScale，以及只看selected参数忽略typed NULL。正控制需真实SaveChanges的decode→binding→execute→materialize证据；synthetic合同只验证validator形状，不能当live来源。

本设计解决了现有包可观察性路径，但未证明新E实现或任何运行已通过；combined common/L0/E freeze、L0先行门、source/package身份、历史失败保留及独立C门均保持。
