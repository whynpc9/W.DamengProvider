# TP07 E窗口runtime首失败：TLS精确generic advisory负例分诊

2026-10-10。结论：**harness_descriptor_branch_too_narrow_confirmed / exact_generic_advisory_negative_design_ready / new_cohort_required**。真实W调用已抛出预期NotSupportedException；随后harness错误地要求受控`SELECT :v FROM DUAL`必须取得fixed temporal scale7描述，从而中断。没有证据要求修改W产品或EF mapping；root已接受保留原SQL、仅新增精确generic advisory负例分支的方向。未知/任意tuple仍pending，TLS七位能力仍关闭。

本轮只读失败收据、冻结harness/validator及固定W源码路径，独立比较来源/控制/清理字段和hash；未执行SDK、测试、validator、DB或wrapper，没有改源码、旧证据或status，仅新增本文。

## 实际失败及来源链

失败位于`TP07-E-window-v1-runtime/tls/six/window-003`。本窗原11例中的9个native case完成，第10个`EF-R03-tls-dt-advisory7-capability-async`失败，DTO第11例未运行。main自然exit1/host1、15.191965秒，无timeout；独立cleanup自然exit0/host0、7.699724秒。validator因main进程失败先报`process_success_no_timeout`，并未完成本窗业务合同接受。

单次controlled_ADO_ExecuteNonQueryAsync attempt34，EF CommandId=null，真实command/connection/parameter均为固定W。source、failed-command保存副本和item.actual_parameter_describe逐字段相等；请求与public index0参数（去actual身份字段）相等。

| 字段 | 本次实际值 |
| --- | --- |
| SQL | 冻结源码字面量`SELECT :v FROM DUAL`，length19，hash`9f2f394adf3dd11fd309930f58177ff8b355042a1030fe4999c5dcc24a7c26c9` |
| source阶段 | after_prepare_decode_before_bind；request opcode5；capture sequence27 |
| 最近response | sequence26、request opcode5、response opcode0、sql_code0；不存在本attempt更晚的capture前response |
| operation identity | `[83,1,6,13]`，response/source及本阶段发送一致 |
| server/public映射 | 单参数，ordinal0/public_parameter_index0/server_inout_type0；name hashes均实际存在 |
| 原request | DateTime2/provider DateTime、Precision0/ExplicitPrecision=false、Scale7/ExplicitScale=true、Input、Unspecified ticks`639204129160000001` |
| 原raw | available=true、ParameterDescribe、CType2、precision32767、scale0、type_flag2、mask0/derived0、Unknown |
| 实际结局 | 精确System.NotSupportedException；同attempt failed completion，无completed publication；current_binding_observed=false/current_binding=null |

SQL全文不是拦截器抓取内容；这里是已冻结的受控字面量，其hash和length与实际command一致，本轮也独立重算匹配。它不授予任意SQL角色推断能力，更不建立WHERE/concurrency语义。

本阶段sent_frames只有opcode3和5；业务opcode6/13实际为0。报告同窗有6项先前真实异步SaveChanges正控制：DATE/TIME/INTERVAL各value和NULL，各一次业务发送，prepare、完成command和发送收据均在本report，operation identity逐项匹配。这提供Execute0的observer正控制，但item.expected_refusal尚未写入：代码在raw断言处更早停止，不能回填完成字段或声称完整advisory合同已通过。

`fresh_after_TLS_refusal`步骤也尚未执行。该lane在RuntimeHarness.Create中只登记reserved对象、提前返回，不CREATE表；因此没有此advisory业务表fresh rowcount可供宣称。cleanup证明的是精确reserved名字不存在，不是“SELECT已返回0行”或新连接健康步骤完成。Prepare的row_count/affected0也不是业务Execute/SELECT结果。

## 确证根因与产品边界

`SixPrecisionCases.cs`的Advisory在实际调用捕获NotSupported后，调用`RequireRaw(raw, kind, 7, 1)`并要求DT26/9或DTO27/11；当前精确raw却是flag2/scale0/CType2，因此在`ParameterDescribe_tuple_pending_not_accepted`失败。Python的advisory分支同样调用全局raw_tuple(scale7)及fixed extended检查；即使只修C#，独立validator也会继续失败。

固定W的`DmParameter.ResolveType`优先使用显式请求类型；flag2 generic描述不会被当作fixed temporal列。`DmCommand`选择temporal参数路径，`DmTemporalBindingResolver.Create`只有ParameterDescribe/flag1且temporal CType才ReliableFixed；nonfixed路径采用请求选定的temporal类型与显式Scale7，随后`DmTemporalCapabilityProfile.ValidateWriteTarget`在TLS physical VerifiedSix对七位demand抛NotSupported，发生在PublishTemporalBinding完成及业务Execute之前。实际请求、合法value、准确NotSupported、无binding及Execute0与这条冻结路径一致；异常本身没有stack，不虚构更细的动态调用栈。

映射产生的DateTime2/Precision0/显式Scale7正确；server返回generic CType2描述不把该请求变成字符串请求。**raw scale0不是temporal列精度，precision32767不是时间wire宽度，flag2不是temporal7成功**；拒绝前没有可发布EncodedScale/WireScale/ColumnScale可供补造。该结论源自本次TLS实际generic tuple和固定resolver，与旧shared并发original flag2分支完全不同，不可互相推广。

不建议改SQL加CAST、建临时TIMESTAMP列或改成fixed UPDATE以取得预想tuple：那会改变原受控lane的语义和来源，不能修正当前报告的错误分类。保留原SQL，新增专门标注的负例来源分支；原fixed temporal7分支继续原严格合同。

## High最小文件范围及新合同

预计仅四文件：`runtime-templates/TP07TemporalRuntime/SixPrecisionCases.cs`、`ef-validate.py`、`test_ef_contracts.py`、`ef-cases.json`。policy只更新受影响E source pin/aggregate及两个advisory case的精确分支合同说明；338个case身份/顺序/值/API、固定15/25分窗不变。Program/RuntimeHarness、Models、CommandAudit、EF mapping、W、common、原L0源不改；如需要新增捕获字段则先回root列确切gap。

新增本lane专用helper/分支，禁止修改全局RequireRaw/raw_tuple/published或增加任意allow_flag2开关。其允许来源只有：

1. 原已批准fixed7 temporal分支：DT `(26,9,7,flag1)`或DTO `(27,11,7,flag1)`及原mask0/Unknown/source合同完全保持。
2. 新`exact_generic_ParameterDescribe_TLS_explicit7_refusal`：只在固定TLS/six/advisory7/async、受控单参数原SQL中，raw**逐字段恰为**`ParameterDescribe/CType2/precision32767/scale0/type_flag2/mask0/derived0/Unknown`且available=true。该分支只证明explicit temporal request7在TLS被拒绝，不证明server temporal7描述、wire写成功或能力升级。

新helper必须先核入口case/registry和真实请求，再分类raw。仅两个固定advisory DT/DTO ID；DT为DateTime2/provider DateTime、Unspecified ticks普通值+1tick，DTO为DateTimeOffset/provider DateTimeOffset、同local ticks/原offset330；Precision0且非显式precision、Scale7显式、Input、非NULL均要求精确一致。BOOL/浮点冒充整数、错kind/offset/flags不能放行。**本次仅DT已实际观察；DTO若新运行返回同精确generic tuple可按该有限合同检查，若不同则pending；不得复制DT收据宣称DTO已观察或接受。**

在C#局部helper及Python独立检查中保持并明确核实：同stage只有一个真实controlled attempt及一个failed completion、同attempt/SQL hash+length、EF ID=null，source捕获点和四项operation identity，最近成功Prepare response与序号，单参数完整ordinal/public双射0→0、inout0、实际name hashes及request/public逐字段相等。可利用既有CommandAudit收据，不需要产品hook；不能只拿raw一行即通过。新分支的source label须由这些检查推导，validator不信producer label。

两种描述路径的结局都必须是精确NotSupported链、真实Execute0和同窗先行成功异步发送控制；错误链、借case/attempt/operation的控制都拒绝。失败command必须无binding字段、current_binding_observed=false/current_binding=null；不得把“缺发布”转成伪造reliable_fixed/column_scale/encoded值。保留实际fresh_after_TLS_refusal新连接查询及TEST/profile身份门；缺step、partial、health失败仍使whole case失败。TLS RequireTls/证书策略不变，不读取SA/DEV，不操作模式或其他Schema。

## 必须补的离线合同与新实库范围

至少一个完整手写正例先通过：包含正确固定route/SQL、Prepare/latest-response、单参数映射、请求和精确generic raw、failed completion、NotSupported、Execute0、真实结构的同窗positive-control及fresh-health。标为synthetic合同，不能当新DB收据。DT/DTO分别覆盖，并保留原fixed7正例及严格负例；DTOsynthetic不构成DTO live证据。

保存的真实失败样本可用于tuple/来源分类回读，但作为完整case必须仍失败：它没有expected_refusal/fresh-health完成证据。不能为让旧样本通过而删除这两门。

逐字段负例至少覆盖：CType/precision/scale/flag/mask/derived/resultformat/source/available任一变化或缺失；wrong SQL/hash/length、额外参数、错ordinal/public/inout/name关联、NULL/wrong ticks/DTO offset/type/precision/explicit flags/direction；cross profile/shared/sync/shape/任意ID；借Prepare/latest响应次序/非成功response/operation/attempt；错误异常/成功completion/伪造binding、业务Execute1、无正控制或来自别窗；缺fresh-health/错TEST/profile。未知tuple保持明确pending失败，绝不以NotSupported或producer expected字符串独立放行。

C#变化要求新E源manifest/aggregate、source/lock/build/startup及工具pack；原W/mapping/common/L0保持。离线需源码复审、全部原合同+新增分支变异、真实编译partition338/25及控制前缀、相同新compiled host GeneratedCreate28及独立身份/SQL/IO门，再形成新12-ref批准。新真实验收必须从新run开始完成全部25窗/338例，原900/300/180时限和fail-stop不变；不能只补本窗、跳过前20窗或拼接旧277例。TLS本窗DT/DTO必须真实取得各自tuple、拒绝/Execute0/health证据；后续TLS mutation/generated仍待新run实际门。

## 已完成的清理及历史证据

本轮直接核失败窗10项原ledger：main finally、独立cleanup及其finally均覆盖同10名字，全部fresh_absent=true/count0且TEST身份正确；advisory项creation_state=reserved、CREATE ACK=false，main cleanup dropped=false。没有已知残留，不需新恢复DDL。

Low最终只读readback重新验证20PASS窗277例（shared180/TLS97），21个started窗合计287个精确ledger对象均独立fresh absence、nonce/table/PK唯一，2877项输入不变。本reviewer读了该readback并从run保存结果另计20/277，不将失败窗额外9个completed当独立接受；whole-run=false、最终全量validator未执行，后续TLS mutation/generated未跑。

| artifact（相对`.local/temporal-native/verification/`） | SHA256 |
| --- | --- |
| TP07-E-window-v1-runtime/run-receipt.json | `5ce7bca56c749369fc1a620d7397c4ea781e295d934b740659ae082d843a2703` |
| TP07-E-window-v1-runtime/tls/six/window-003/main/runtime.json | `fa0a032fe9bd6d7a9502c17f09064efc6702c660e2bbc02dce1cf4be08f9f45d` |
| 同窗main/ownership.json | `ac54440c6fce17ec67cc7ba55674fbb604490949d7a29dfcbfecb0d9b191e56c` |
| 同窗cleanup/report.json | `c5385aea5ad18ab104e841fb83c9a71be39707d48fc1f20dbee4408caef667c0` |
| 同窗window-receipt.json | `285b6e5b98935ac0c5831e580425626590faffb28f92bbea11ee256b04829f8e` |
| 同窗validation.json | `3790a58fad5b43cffb31e6a24a651aaf62b402b1a7d5547900a02668afc0b229` |
| TP07-E-window-v1-runtime-control/independent-readback.json | `84083ae8c6abedcc959430db976a025b603d60ab9753b084b6f22ac9983b3685` |
| TP07-E-window-v1-runtime-control/verification-summary.json | `c2887997612076a562d7bb12d69172e0b60719780df0927dec9bc35b58365902` |
| TP07-E-window-v1-runtime-control/runtime-artifact-hashes.json | `b878e3fbc9e52e57f08895f9c58cd9ce463c006d5f1cb817a7c24b2dfeddf861` |

所有旧失败/部分接受/清理及此前分诊继续保留，不改status或回填旧case。源码冻结和独立窗口释放后可按上述四文件窄范围实施；此设计不是产品修复、TLS七位开放、DTO已验收或新runtime批准。D/完整C及TP08之后LocalClaude门不变。
