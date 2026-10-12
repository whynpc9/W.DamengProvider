# TP07 E并发原值参数：ParameterDescribe来源裁决

2026-10-09。状态：**bounded_shared_original_parameter_branch_design_ready / mutation_independent_acceptance_pending**。本轮只读实际八份收据、固定W resolver、E模型/MutationCases及现有validator/tests；未运行SDK、测试、validator或DB，未修改C#/产品/Python或旧证据。

## 1. 事实与失败边界

本次shared roundtrip60与six54均已由原cohort main/cleanup/独立validator完成。mutation12的C# main/cleanup成功，但独立validator在 `approved_fixed_tuple_pending`失败；仍未接受整个mutation组。126个本次owned对象全部独立fresh absent，无需重试DB或处理残留。

| 实际证据（相对 `.local/temporal-native/verification/`） | SHA256 |
| --- | --- |
| TP07-E-v5-runtime/shared/mutation/main/runtime.json | `78237fe1c4b59c7e2e2df97babf7765928b914def2ac6af9bb2795b49efb3d19` |
| 同mutation/validation.json | `3691f2f0c8631f3e68ec287317955175b17beaed85ca1c42de4e0049f78849c3` |
| TP07-E-v5-runtime-control/verification-summary.json | `79695eeaf37773ac9cde55f72ba15aebadfb2015691b6f719dabc108680b59c8` |

四个concurrency case（DT/DTO × sync/async）各含winner/stale两个SaveChanges，共**八个原时间值参数**。本轮逐项回读Prepare response/四项operation identity和published raw，全部同attempt/同次来源相符，没有拿另一command的describe或binding补证据。

统一原值`t0=639204129161234560`；winner的SET值是t0+1tick、N=changed，stale的SET值是t0+2ticks、N=stale，二者original参数都为t0。DTO原offset330，未转UTC替代本地值。

| case/API | winner/stale attempt | 当前时间参数server ordinal / public index | 原时间参数server ordinal / public index |
| --- | --- | --- | --- |
| DT sync | 13 / 15 | 0 / 1 | 3 / 2 |
| DTO sync | 38 / 40 | 1 / 2 | 3 / 3 |
| DT async | 63 / 65 | 0 / 1 | 3 / 2 |
| DTO async | 88 / 90 | 1 / 2 | 3 / 3 |

每个command恰4个参数。DT server顺序为current D、N、ID、original D，public indexes=[1,3,0,2]；DTO顺序为N、current Z、ID、original Z，public indexes=[1,2,0,3]。ID在server ordinal2/public0，value1、raw flag2/CType7/precision4/scale0；N为raw flag1/CType2/precision32/scale0，value为该stage的changed或stale。N/ID都没有temporal binding。

current时间raw为flag1、原时间raw为**flag2**；两者均真实ParameterDescribe/Unknown format、mask0/derived0/scale7，DT CType26/width9、DTO CType27/width11。实际published current为ReliableFixed=true/ColumnScale7；original为**ReliableFixed=false/ColumnScale=null**。两者RequestedScale/EncodedScale/WireScale均7、ExplicitScale=true、profile=VerifiedSeven。四个async stage各有本次实际业务发送，sync发送仍未观察，不把它说成0。

## 2. 产品语义不能合并

固定W `Internal/Types/DmParameterBinding.cs:78–90`只有原Source=ParameterDescribe、TypeFlag1且时间CType才建立可靠fixed；flag2不会产生ColumnScale约束。随后显式request7选择extended类型/编码7，advisory raw仍保留其原始角色，而非改成fixed字段。`Create`最终published record的ReliableFixed和ColumnScale来自这个分支。

所以本次不是产品少填ColumnScale或精度降级，也不能把server raw scale7当成可靠column constraint。全局 `raw_tuple`改为flag∈{1,2}、全局`published`仍强制ReliableFixed=true，或给original补ColumnScale7都是错误修复。fixed6、普通SET、seed、其它group及未知tuple须继续走原严格合同。

## 3. 原值角色如何在没有SQL全文时成立

当前audit仅保存CommandText hash/length，**不能恢复SQL或声称直接观察了WHERE token位置**。禁止手写一个“看起来等价”的UPDATE、重算hash并当成实际SQL来源，也不能以flag2本身推导谓词角色。

可在本轮冻结受控EF流程中用下列联合证据，限定一个 `controlled_EF_original_concurrency_parameter`角色：

1. registry固定group=mutation、shape=concurrency、kind DT/DTO；冻结TemporalContext仅将该kind的D或Z设为IsConcurrencyToken。MutationCases真实先seed t0、两个tracked query都读t0，winner改current为t0+1；stale保OriginalValue=t0而改current为t0+2。实际 `original_temporal_concurrency_token`为t0。
2. 同次真实EF ReaderExecuting[Async]/ReaderExecuted[Async]，不是controlled ADO或raw SQL。完整四个公开参数与server ordinal双射，一对同family时间参数由**不同且精确的值**唯一标识current与original；角色必须先按registry/value求出，再检查flag，不能先找flag2然后宣布它是原值。
3. 真实winner保存1，fresh winner值t0+1/Nchanged；stale command实际完成产生reader，随后EF抛精确DbUpdateConcurrencyException，fresh行/rowcount/schema保留winner。不是driver参数拒绝，不能要求stale无业务发送或无published binding。
4. 同一个case的winner与stale实际SQL hash/length相同，公开parameter-name hash与server/public映射按角色稳定；各自command attempt/operation identity不同且单独核验。hash在这里只连接两次同形EF调用，**不充当SQL语法证明**。

上述“原值并发参数/谓词角色”是冻结模型/EF调用语义、实际参数值与真实并发结果共同支持的有限推论，不是抓取SQL全文后的分类。固定provider继承UpdateAndSelectSqlGenerator，原源码测试 `GeneratedUpdateUsesOriginalConcurrencyAndReadsByKeyWithZeroRowGuard`也体现OriginalValue参数用于condition；本轮只读该测试，不声称新执行。

这一有限角色合同足以为本轮已知原参数选择正确binding分支，**不建立任意SQL的predicate识别器**。如果后续需要直接SQL clause证据、出现值不唯一/额外参数/其它stage/不同shape，当前hash收据不够，应pending回root，不能猜。无需为本次引入C#产品hook或修改SaveChanges。

## 4. 建议Python窄分支的强制条件

保持全局`raw_tuple`和普通`published`语义不变。可增加只由concurrency caller调用的小helper，接收完整report/item/spec，联合验证winner与stale；不要添加能从任意caller打开的全局`allow_flag2`宽松开关。

### 4.1 入口及全参数范围

- 新nonfixed分支只适用 **shared / mutation / concurrency / scale7** 的winner_SaveChanges、stale_SaveChanges，kind只能DT/DTO，值非NULL，DTO offset按实际registry330。TLS predicate本轮没有实际来源，不能预批准flag2 scale6 tuple；其它profile/stage留原strict或pending。
- 每stage只有一个本次真正EF command attempt/completion，api与sync/async相符，EF CommandId、sql hash、actual W connection/command/parameter/reader身份及candidate/source闭包保持。
- 必须调用/保留现有`source()`的同次Prepare capture、最近成功opcode5 response、四项identity、完整server ordinal和public index双射、request与公开参数一致、inout0及name hash检查；不得只抽其中一条raw。
- 每stage恰四参数且无重复角色，按上表固定当前已观察的ordinal/public映射核对两个时间角色、N与ID。N/ID采用本轮精确tuple/value，不把ID的flag2误作时间允许项。winner/stale各角色parameter-name hash和位置稳定，ID恒1，N值严格changed/stale。
- 两时间request为实际同kind DbType（DT DateTime2/provider DateTime，DTO DateTimeOffset/provider DateTimeOffset）、Precision0、Scale7、ExplicitScale=true、Input，原值严格t0，当前值按stage为t0+1或t0+2；DT Kind=Unspecified，DTO本地ticks与原offset同时匹配。NULL、重复旧值、当前/原值交换都失败。

### 4.2 raw与published独立分支

| 参数角色 | raw要求 | published要求 |
| --- | --- | --- |
| current SET | 原strict fixed flag1；本轮实际DT(26,9)/DTO(27,11)、scale7、masks0、ParameterDescribe/Unknown | binding_available/raw_available=true；ReliableFixed=true、ColumnScale7；Requested/Encoded/Wire=7、ExplicitScale=true、VerifiedSeven |
| original，新增分支 | **精确flag2**；DT(26,9)/DTO(27,11)、scale7、masks0、ParameterDescribe/Unknown | binding_available/raw_available=true；**ReliableFixed=false、ColumnScale必须null**；Requested/Encoded/Wire=7、ExplicitScale=true、VerifiedSeven |

每个published必须按同command completion及相同server ordinal与prepare source对应，并逐字段保留raw Source/CType/Precision/Scale/TypeFlag/Mask/DerivedMask/ResultFormat；不能用另一个stage的binding、缺发布值或推导字段替代。published集合ordinal唯一且覆盖该command真实四参数；非时间N/ID不得伪装出temporal binding。

原来合法fixed原值（flag1）如被观察，仍可以沿原strict fixed合同判定，而不是因新增branch被取消；**flag2只有在明确original角色时才调用新合同**。未知flag、generic CType2、legacy16/23、装饰mask、wrong width/scale均不在这次新增白名单。也不能把flag2原值提升为“可靠列7”来支撑fixed6量化门。

### 4.3 并发结果及跨阶段边界

保留seed、两次tracked原始行、原token、winner SaveChanges affected entity1、fresh winner、stale精确EF异常链、fresh winner不被覆盖、总行数1及catalog前后不变的原门。两个stage都属于成功driver命令完成，因此都应有真实published binding；stale的EF异常不是CommandFailed型local refusal。async存在业务Execute发送不应被判错；sync不新增伪造发送计数。

新helper失败不能被catch并fallback到宽松旧检查；整体mutation仍失败。profile/role/来源缺失保持pending/明确失败，不只根据producer expected字符串选择成功。

## 5. 有限合同与实施范围

预计只需ef-validate.py和对应test_ef_contracts.py，更新新的工具包身份；C#、policy registry/模板pin、SQL、W/EF产品、compiled startup无需变化。若High实现发现必须额外捕获字段，先回root列出确切gap，不自行增加C#角色声明冒充观察。

正例应覆盖DT/DTO、winner/stale及sync/async的真实receipt结构，包含上述不同server/public映射；fixtures标为手写合同，不把它们当新DB。可另对这次已保存八份实际收据执行Low只读重验，但必须产生新验证artifact，不改旧失败。

必要负例：

- 将current改flag2、把original交换为current/NULL/错误ticks或offset，增删时间参数/重复orig角色，缺ID/N或值错误，错误ordinal/public/name关联。
- original flag2却ReliableFixed=true或ColumnScale7；Encoded/Wire/Requested改变，profile换VerifiedSix，raw与published任一来源字段不一致/缺binding。
- 借另case/stage/operation的Prepare或binding、改最新response顺序/EF CommandId，winner/stale错误SQL hash关联（不能写死真实nonce hash作为全局允许列表）。
- 换成TLS、fixed6、seed/update/rollback/roundtrip/reuse/controlled ADO仍带flag2，应拒绝；原strict fixed正例与它们的负例保持。
- stale错异常/错误fresh winner/N/rowcount/catalog，producer成功旗但缺实际completion，均不可接受。

至少一项正例必须先完整通过后再做逐字段mutation，避免测试仅在不相关的缺字段处失败。Low新合同及八份实际readback通过后，root才决定新工具包下的正式runtime验证；不能让只读参数子集重验代替整个mutation接受或追加通用resume框架。

## 6. 接受边界

本报告批准的是**可实施的shared原值参数来源分支设计**，不是现有mutation运行已被接受。旧validator `3691f2…`失败与原main成功旗同时保留，roundtrip60/six54的原cohort事实不改标签；generated/TLS及D/完整C仍等待实际门。TLS原值predicate tuple、其它SQL/参数族和所有未观察profile没有获得推断性批准。等待High明确窄修freeze后再做源码审阅。
