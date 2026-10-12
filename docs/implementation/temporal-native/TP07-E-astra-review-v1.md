# TP07 E runtime v1 静态审阅

2026-10-09。结论：**changes_requested / E-only review / combined_common_closure_pending**。R01/R02须修复，R03补齐独立验收所需证据后再冻结。仅审E的9个已停写文件与必要跨界接口；common/L0尚未first-freeze，本文不接受其代码或组合闭包。未执行SDK、测试、DB或修改产品/工具。

依据 runtime design SHA `0a7363dfc3839428b0725e18a9a2b7961b5715bdf599659239333bb88234d861`、M1已接受mapping-v4及固定W包的实际binding/profile源码。计划case静态展开为shared180（60/12/54/54）、TLS158（52/12/56/38）；这是注册集合，不是运行数。

## R01：原生TIME/INTERVAL拒绝的异常类型写错

`Models.cs`把date/time/interval的reject全部登记为`Overflow_no_business_Execute_fresh_rows0`；`SixPrecisionCases.cs:31–38`只按这个前缀和OverflowException链捕获。

但M1已接受的 `DamengTimeOnlyTypeMapping.ValidatePrecision` 与 `DamengTimeSpanTypeMapping.ValidateFractionalSecondPrecision` 对非整micro值抛 **ArgumentException**。这两个native reject的sync/async共4项会在实际EF mapping阶段按正确旧合同拒绝，却被当前harness当意外失败。DATE非午夜、普通DT/DTO六位量化的Overflow合同不同，不能合并。

修复应在有限registry按kind固定预期类型，保持实际SaveChanges、无业务发送/无持久行检查；不能改mapping异常来迎合harness，也不能放宽为“任何异常都通过”。记录实际有界异常链供独立validator核对，见R03。

## R02：显式Scale7与fixed6合同混层

`TemporalContext`的default7_fixed6模型保默认mapping7，M1已经证明其实际参数**显式Scale7**。`SixPrecisionCases.cs:58–67`却要求micro/NULL普遍成功且binding EncodedScale6；这是把未设置Scale的DateTime2默认策略套到了显式声明。

固定W `DmTemporalBindingResolver.Create` 使用 `scale = requested ?? columnScale ?? defaultScale`，`RequestedScale=7`不会变6；fixed目标的header `WireScale`仍保columnScale6。`DmTemporalCapabilityProfile.ValidateWriteTarget`先核layout支持，再核七位profile；列6量化在随后ValidateWriteValue独立执行。

### 对冻结runtime design的限定解释补充（root已确认）

原design中“request7 fixed6可以wire6成功”的宽泛句子不适用于M1显式Scale7。旧design保留，本补充按接受源码限定如下，不改W/EF映射：

| 实际请求与describe | 正确结果 |
| --- | --- |
| DbType.DateTime2但Scale**未设置**，真实可靠fixed6 | 可按真实列选择6；这才是原TP-A05默认意图/fixed优先边界，不能伪称当前EF显式7属于此格 |
| 显式Scale7，fixed legacy16/23、列6 | NotSupported，包括整micro、非整micro和NULL；能力/布局门先于值量化 |
| 显式Scale7，shared verified7、fixed extended26/27、列6 | EncodedScale7、WireScale6；精确micro成功，非整microOverflow；NULL仍验证目标后可成功 |
| 显式Scale7，TLS verified6（包括fixed extended列6） | 七位关闭，NotSupported；不能声称服务器绝对不支持七位 |

E应从本次真实ParameterDescribe/绑定观察选择这个有限分支，而非model形状或DDL文本推断。不能把任意TypeFlag≠1当已证advisory；应明确ParameterDescribe角色、正确flag/类型/scale/mask。拿不到必需来源或遇集合外tuple即pending/失败，不用猜测补成功。成功分支核真正EncodedScale与WireScale，拒绝分支核真实异常与业务发送/持久状态。显式facet6/store6模型的成功与非整micro拒绝继续独立保留；不要为了让default7_fixed6成功把EF Scale清掉。

## R03：补齐可独立复核的拒绝与mutation证据

1. 当前`expected_refusal.error`只通过common Safe保留外层类型，而`exact_type_in_chain`是硬编码字符串。若EF包装为DbUpdateException，独立validator看不到实际inner类型。须保存有界的真实异常链type及安全number/code（无Message），按registry精确匹配允许wrapper/最终拒绝，排除timeout/cancel/transport等基础设施链；不要仅靠producer声称含某类型。
2. 并发/rollback确实执行时间token、tracked SaveChanges及fresh row/raw oracle，但 `MutationCases`没有每case前后fresh总行数与catalog快照，不能充分支持design所述row-count/schema不变。补最小before/after count=1及本次owned列/键catalog语义快照或hash比较，stale失败后对照winner已提交状态，rollback后对照原状态。组末cleanup结构核验不能冒充每次拒绝后的即时schema收据。只调用已冻结的common只读接口，不抢改common文件。
3. sync拒绝的`business_execute_frames=null/async_send_observed=false`是诚实记录，但registry的统一no-business0文字不应把它算作精确no-send证据。sync标明该证据未观察；实际async拒绝必须是该stage0帧，并以成功async SaveChanges的非零业务帧作正控制验证hook确已工作。不能因为全组Sends为空就接受所有“0发送”。现有trace记录可供独立validator判断，不需要新产品hook。

上述是证据闭环窄修，不要求扩大用例总数或修改产品生命周期。真实写拒绝允许旧连接按R3退休，fresh EF连接核状态；不能强迫原context重试或恢复。

## 已检查且可保留的E实现

- Roundtrip真实执行SaveChanges/SaveChangesAsync，新context AsNoTracking materialize；reuse改变同一tracked实体的值与NULL，并执行真正参数化query。通过实体与fresh独立raw/FF9 oracle分别核ticks/offset，没有用ADO INSERT替代EF成功。`scaffold6_followup`明确pending，手写store6不冒充CLI scaffold模型。
- Mutation真实将D或Z设置IsConcurrencyToken，两context先读同版本；winner改变1tick（TLS1micro），stale保原token并期待DbUpdateConcurrencyException，fresh核winner/N未被stale覆盖。rollback事务内只有DML，查询事务内新值，Rollback后fresh核原值。R03补充不改变这些操作。
- Model cache key包括context类型、table/shape/profile/kind/scale/PK/default/seed及designTime，可区分DDL facet6和运行default7模型，不沿用错误cache。它不读连接决定mapping能力。
- Create从实际IDesignTimeModel→MigrationsModelDiffer→MigrationsSqlGenerator生成owned表/seed操作，白名单限制对象与操作；先登记ownership及BeginExternalCreate，实际ExecuteSqlRaw返回后才记录CREATE ACK并Confirm。DDL源码只含固定合成数据/owned标识符。未知CREATE完成状态不能由普通异常自动证明对象归属；最终行为仍依赖待冻结common。
- R04保留实际DefaultValue、HasData、生成literal作为DefaultValueSql、NULL/clock默认、EF.Constant inline查询及ExecuteUpdate表达式。generated当前/原值与fresh实体核对，fresh独立oracle和总行数另核；clock只记录实际precision，不假称七位。极值literal保存与默认ordinary输入分开，均受L0先行门控制。
- CommandAudit不改变参数/消费reader，实际拦截Executing/Executed及公开W连接打开路径，记录安全facet/合成值、SQL hash、实际binding/reader类型。AfterFrameSent只读hook保存stage/session/lease/execution/invocation身份，观察异常不会吞成通过；输出没有任意Exception.Message/连接串/认证帧。
- TLS advisory lane清楚标记“实际EF mapping建参 + 受控ADO execute”，不冒称SaveChanges；固定SQL无用户文本输入。其来源检查与精确拒绝证据须按R02/R03收紧。它不能用DDL失败或fixed6量化拒绝证明七位能力关闭。

producer的`runtime_accepted`仅是本进程自有步骤/清理结果；`candidate_acceptance=false`及独立validator仍是总门，不能将producer expected/definition当独立oracle。case里预期拒绝步骤保留failed状态是事实，validator必须按精确允许的step/type核，不得一概忽略所有failed步骤。

## common/L0及组合闭包待交付

本次只观察CandidateGuard/OwnedObjects/SafeEvidence/LiteralOracle接口满足E调用形状，未对仍在编辑的common/L0给静态接受。它们负责实际候选身份、physical TEST profile、safe序列化、CREATE/marker/cleanup及fresh raw oracle，必须first-freeze后由指定reviewer独立审阅，再与E修订源码一起封完整source/locks。R03所需catalog接口也以该正式交接为准。

Low不得在只冻结E时build/DB；组合静态接受与离线工具合同/身份门后，L0首先执行，L0失败阻断E实库。shared/TLS registry必须由独立validator重建，限定case零跳过、旧失败保存；最终C、scaffold/guarded Down、PR/发布都不由本报告推出。

## 本轮九文件身份

以下相对 `tools/DownstreamAcceptance/TP07/runtime-templates/TP07TemporalRuntime/`：

| 文件 | SHA256 |
| --- | --- |
| CommandAudit.cs | `e1a0febb38e42bc87e2e0d4438d8ba3cfc70319132955c9d470952708264fea2` |
| GeneratedValueCases.cs | `dbbd01fee46eda9b68cd4e1cb3e22fcff199ccad5c603c3a65a1ad66c4eee9d8` |
| Models.cs | `d0d18eceb4f5e62152a74609bb95fcd543ebbee5e1c5931593eb6cc6e725cb68` |
| MutationCases.cs | `091eef3e5b50ec55751e977e753920015f09016e92a1c41cbaa115ca535e9424` |
| Program.cs | `17899cfc72d0df657da4d321e9887d8a64288827157a0c7e472fb8388e15a6a7` |
| RoundtripCases.cs | `b18e96f8624dbdf7fe646463670786d876a47584c6f1a2f06a5196a6df116a32` |
| SixPrecisionCases.cs | `b221cc0325f482a34690ae622915799a07212be794161f4bdb9c3bc3cb58a9dd` |
| TP07TemporalRuntime.csproj | `d8c62b33c117f78519bb8273dc267369989210f390f0d79583875b51a170e874` |
| TemporalContext.cs | `a38ab880a8dddb77697e60445692cb46a975cd8df35f83def1d272a620689e5b` |

原runtime design与本轮源码/evidence保持；此报告含R02的限定解释附录，不覆盖旧设计。报告完成后停止写入，等待root开High窄修窗口。
