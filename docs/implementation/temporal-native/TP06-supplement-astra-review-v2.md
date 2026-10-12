# TP06 supplemental tool 静态复审 v2

2026-10-09。结论：**R1–R3 closed_static；R4 changes_requested，整体尚未静态接受**。三项原阻塞已按最小范围修正；本轮补充确认G2仍混用写绑定描述和读结果描述。完成该项窄修复并新冻结后可定点复审，再由Low独立运行。此次没有build、Python测试、consumer、DB或产品/工具修改。

输入快照 `.local/temporal-native/verification/TP06-supplement-v2/source-manifest.json` 实读SHA256为 `fc44c600d81c06fd7ebf01bbfbe0d71cfaa82d7479089009400a5452736039aa`。17份当前工具文件SHA均已实读匹配最终交接；owner/root提供aggregate `679f4e87d0d3d625d95f2bb86ea97ba5736a005028d65063e81f50e031ed9c12`。case-manifest SHA仍为 `39415b392a4ab9f67b4dfba714a6bc3baacb2c0ece2410659e2a24b68759f4fe`，42 shared/36 TLS不变。原v1、三个冻结设计及旧失败保留。

## R1–R3 修复闭环

| finding | 定点复核 | 运行状态 |
| --- | --- | --- |
| R1 Fetch≠NextResult | contracts.py限定fetch_opcode==7；新增只改7→44并保持128行/非零count的负例，另有缺opcode/零count拒绝。多结果44门独立保留 | 待Low执行 |
| R2 独立server NULL | Observations.cs:57的ObserveServerProjection实际调用server投影IsDBNull/Async并检查与temporal NULL一致；ordinary与Sequential NULL共用此helper，receipt区分ordinal1/6。独立validator要求真实调用/正确ordinal/API/NULL结果；缺调用、缺receipt和server非NULL负例齐全 | 待Low执行 |
| R3 protocol分类 | Leading.cs独立区分allocation3、Prepare/direct5、prepared Execute6/13；live与offline共用CollectPreparedFrame/ObservePreparedWindow/CollectDirectFrame/ObserveDirectResponse。额外3或5使source失效；literal要求唯一业务5响应，无法借allocation3承载错误。validator重算actual frame计数并交叉校验发送/响应与operation数字ID、statement、summary和错误code | 待Low执行 |

C#离线门由12扩为24个明确ID：R2检查实际同步/异步getter调用计数、冲突与非NULL文本；R3检查同路径分类/收集、额外5、allocation3及错误/歧义direct响应。runner和独立validator均核验完整有序ID集合，不只信数量。新增Python mutants具有成功控制，未通过扩大catch把目标外失败当作通过。所有synthetic/expected-only标记保持，当前没有执行这些检查。

## R4 / P1：G2 advisory读结果仍强制等于写参数描述

位置：`tools/TemporalCandidateAcceptance/contracts.py:724–746`、`observation:341–435`。

validate_stage先按stage中的 `expected.c_type / expected.scale` 验证parameter_metadata及EncodedScale，此处正确。但`:744`随后把完全相同的tuple传给所有读取observation；observation又据此约束ResultColumn/raw CType、width、scale、schema及GetString声明位数。于是有效写绑定16/6或23/6，只要服务器返回同族extended7结果，就会被验收工具判错，即使输入量子、raw整数、server FF9与typed值全部精确。

producer的 `Cases.cs:BindingStage` 和 `Observations.cs:ReadObservation` 已独立捕获实际ResultColumn，无须把读取字段改写成期望值。缺陷在validator将write contract传入read contract。主工具另有真实不同descriptor的事实，**但补充G2使用的是重复`:v`投影SQL，不能把旧ParameterQuery的实测结果直接当成本G2观察**；本报告只确认分层错误，没有宣称该SQL已经实际触发错判。

与design_next对齐的最小窄修复建议，须由root确认后实施：

- 原 `parameter()`、`binding_metadata()`、EncodedScale、public Scale/Precision、setter/clone/reset/reopen/API与异常合同全部不动。
- 仅对G2成功读阶段选择独立read tuple，再交给原严格observation函数。候选设计集合为write==read；另在当前exact shared physical前提下，允许同族extended7作为**待本G2实测验证**的read备选：实际现有scale3/6写→26/7或27/7。不新增case，不增加写能力，不扩任意0…9或未知profile。
- 选择依据必须是该G2实际ResultColumn raw/source/type/width/scale/schema/cell，并由独立packet decode、server FF9、typed ticks/ns/offset全部核对才能形成observed read tuple。不能仅检查“同family”便通过；产品encoder/formatter不作expected。
- TLS只保留equal分支；其余未在受审集合的tuple标 `unverified_result_descriptor` 并保持candidate=false/pending，交root处理。不将未取证的读描述误报为产品精度错误。固定表DML继续按实际固定列schema严格检查，不应用G2分支。
- 增加完整success control：写描述保持16/6与23/6不变、独立读描述及raw/schema/GetString改为同族extended7且原值精确时，shared备选合同成立；错family、错raw单位/字段、错schema、错FF9、typed值损失、伪造source仍拒绝。TLS mismatch与集合外tuple必须不接受，不能通过删检查修复。

该建议只修补证据解释，不涉及当前产品、main v11/v12工具、capability whitelist、原始实测数据或旧报告；两条工具修复线保持文件归属隔离。补充consumer尚未运行，对其read tuple的真实支持结论仍待后续同候选运行。

## 其余范围收口

已补核runner与native CLI参数、离线/真实入口分离、TEST wrapper和环境处理、同候选source/archive/package/实际DLL/MVID/runtime deps闭环、工具输入必需文件检查、精准ownership/marker、主finally与独立cleanup、fresh状态和safe日志。未发现另一个可确认的运行接口/签名或清理阻塞；这是静态结论，不是编译成功。

FrameSent来自产品现有async发送完成回调，AfterFrame为独立response回调，两者分开；literal同步控制只主张真实response，不伪造发送receipt。fixed100未发布binding的例外及control_only关联没有改成来源重建。advisory服务器leading号仍无默认批准，真实错误控制即使收齐也继续pending/failure。G2显式声明生命周期补充的范围保持，inferred完整矩阵由原主工具承担。Sequential非NULL继续一次性读取各alias，NULL才经过共享server-null helper，不倒读已消费ordinal。

R4实现与复审完成后，Low可先在新冻结执行Python合同、构建及24项实际C#离线门。补充DB必须等待主工具新规则重判和TLS门，并由root另行安排；任何离线通过、已构建consumer或本报告均不能提前激活DB或宣称完整TP06接受。
