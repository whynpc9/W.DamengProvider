# Supplement v3 shared 写拒绝恢复失败：归因与工具修复设计

> 阅读提示：这是历史失败归因与工具修复记录，不是当前产品缺陷结论或操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。结论：**consumer恢复合同过强；未确认产品回归，不修改R3或产品**。源码可解释为既有写绑定失败Abort导致旧连接不可用，而非“旧command尚未Dispose仍占租约”。但失败报告没有保存公共State和恢复子步骤，不能把实际Broken状态或具体throw位置写成已观测事实。下述root已批准的工具窄修会在新完整运行中同时补齐状态/发送证据，无需额外独立诊断重跑。

本次仅回读报告、当前源码、fe89基线及冻结设计；无build、tests、validator或DB运行，没有改产品、工具、旧报告。最初通信中把same-connection失败直接称作driver defect的判断证据不足；范围复核后的正式裁决以本文为准。

## 1. 已观察事实及边界

- `TP06-supplement-v3-shared-candidate/supplemental.json` 实读SHA256 `e424328288e7b5566aabc9fc30c218b929ae5c9c594352092dbaad22c4c75ac9`。
- `TP06-supplement-v3/independent-runtime-v2-summary.json` 实读SHA256 `c8622fedceeb4e3e2ebe9f5ee166433e86201181f158dc3707504aa1694cc08c`。来源manifest仍为 `78d7b42607035b4811805d3e738968281b2e49ccf1f69cf43dd00d6d853fbab3`，同候选包/DLL/MVID及shared physical身份符合已冻结输入。
- 前12个TX case记录完成；第13个ASYNC_REJECT_THEN_QUERY的control返回1，fresh W/O/server原值精确。fraction记录了期望的System.OverflowException、user_rejection及affected_rows=0；随后无recovery receipt、无fresh observations。
- 顶层失败为System.InvalidOperationException，安全报告未保存message；`diagnostic_business_send=null`只是无FailureInfo的事实，**不是NotSent或零发送证明**。已失败目标之后的真实连接State、恢复SQL是否进入发送、原值最终是否保持，均没有该stage完整证据，不能补写通过。
- runner主退出1、独立cleanup退出0、无deadline；最终本次owned table fresh不存在，recovery_pending=false。shared补充validator、TLS补充均未运行；此前离线通过与主shared700/TLS374接受不能冲销本次失败。

## 2. 源码定位与原合同

consumer `Cases.cs:DmlStage` 捕获目标Overflow后设置error/affected0，再调用 `Recovery(c)`；Recovery在ScalarAsync成功后才写整份receipt。因此现有空receipt不能区分创建command、执行scalar、转换返回值等细分步骤，旧command的await-using确实尚未结束，但这不等于租约仍未释放。

产品 `DmCommand.ExecuteCompleteAsync:999–1012` 在异常返回给调用者前已释放invocation/lease/plan；`ExecuteReaderOwnedAsync:1150–1153` 在Prepare后、prepared业务Execute前执行Bind。Bind异常进入`:1171`的Abort路径，`:1245–1257`依据ShouldAbortAfterFailure决定Detach并清statement引用。`DmInvocation:56–64`对一般Overflow默认abort；例外只包括带确切owner的local-input receipt及允许保留的verified server response。当前时间Bind没有LOB的local-input receipt。

`DmSession.Detach:822–850`会清active lease/wire并使session Broken，触发OnSessionBroken；`DmConnection:170–195`将公共State设Broken，后续BeginExecution直接InvalidOperation。命令Dispose不能复活已abort的session。该链条高度符合现有失败位置，但实际状态仍须新运行采集，不能把源码推断冒称旧JSON已有事实。

已逐段 `git show fe89bd44` 核对：ShouldAbortAfterFailure、ExecuteReaderOwnedAsync以及AbortStatementAfterFailedExecutionAsync与当前相同；Session/Invocation相对fe89无diff。fe89的Bind→SetResolvedObject→SetObjectCore→NormalizeForWire→ValidateFraction对六位非整微秒同样抛Overflow，随后进入相同Abort路径。本次没有重新运行旧v23包，不宣称取得旧包本场景的新实测。

规范边界也不要求为这条写拒绝复用旧连接：v2计划§4.4明确不得Broken的是**合法server值的读取转换失败**；TP04设计§6要求不改旧failure translation/lease/handoff/R3 cleanup、不清掉历史Prepare的SendAttempted、不伪造业务Complete。R3-design的PR#2条款仅为caller Read/ReadAsync确切未发送wire失败授权狭窄receipt；R3PreSendInputSourceFailureTests正负控制区分该receipt与ACK后/取消后的Broken，不能扩为任意本地Overflow。主v11 `FreshScale6Recovery`和README明确绑定拒绝后使用fresh connection，不要求失败session仍可复用。

因此Supplement无条件Recovery在原连接SELECT1，是超出原允许状态的验收附加条件。应修工具使恢复证据符合原状态合同；不得借此新增产品恢复receipt、改变全局ShouldAbort、补业务Complete或复活旧session。

## 3. root批准的状态分类与强门

只对固定case中**期望的本地写绑定拒绝**（精确Overflow/NotSupported、正确输入/声明）应用此分类；不能应用于任意异常、取消、超时、传输/认证错误或reader值转换失败。

| 实际公共状态 | 恢复行为和独立validator要求 |
| --- | --- |
| target前Open，期望本地拒绝后仍Open | 原logical connection执行SELECT1必须成功；实际API/connection ID/结果1完整，随后fresh身份/原行/原值/列schema检查。失败不得自动降为另一条宽免 |
| target前Open，期望本地拒绝后Broken或Closed | 原连接不称可复用。仅执行受控的旧连接拒绝探测：SELECT1的ExecuteScalarAsync必须以既有InvalidOperation拒绝，且该探测所有actual send增量0；记录State前后，不重开原connection。按原路径Dispose旧command/connection后，通过新的TEST连接验证SELECT1、原owned row精确值/行数和必要schema；使用实际不同connection ID，不把fresh成功标same-connection成功 |
| 其它状态组合、错误类型或缺失收据 | 保留安全事实并失败；不得根据异常message猜原因，不手工修状态 |

每个本地拒绝target都必须保存其独立API窗口的真实发送receipt：使用已有AfterFrameSent，区分3 allocation、5 prepare、6/13 prepared业务Execute。目标参数化形状/parameter数量/实际API必须对应，Prepare可已发送；**业务Execute发送必须为0**。AfterFrame只作响应补充，不能代替发送证明，也不能把零业务发送改称整次invocation完全未发。Safe(Error).Outcome为空继续如实保留，不造NotSent。fresh检查仍验证原值而非只看行数；拒绝后不重放原业务DML。

verified server错误不是该本地拒绝例外：按既有允许状态及原同连接成功控制处理，leading的精确来源/错误响应/独立literal控制/未批准号pending门保持。reader mismatch/合法值转换失败仍必须维持原前向列、行与连接恢复合同，不接受Broken后换连接代替。

只采公共 `Connection.State` 的前后枚举、精确target catch、实际API步骤、既有数字发送身份和工具connection ID。无需新增private session状态探针、产品hook或读取任何原始报文/认证数据。

## 4. caller生命周期与仅负例API调整

ASYNC_REJECT_THEN_QUERY的control/fraction/scale7_null当前共用rc。fraction若使rc退休，**下一个独立的scale7_null stage必须显式新建TEST连接**，保同一个本次owned row及原值，记录新connection ID和`new_connection_for_next_independent_stage_after_safe_abort`来源。原rc不得Close/Open复活；这不是重试或重放fraction。若fraction后仍Open，下一stage继续原rc。validator据实际前一stage状态严格判断允许的ID变化，不能无条件允许每stage替换连接。

G2 TLS的尾tick/default声明拒绝及各G3拒绝都在本case最后stage，不需修改前面成功操作的连接/参数身份合同；只改变其拒绝后的恢复记录。G2已通过的clone/reset/reopen关联不受此例外影响。

root明确批准以下**已有negative stage API-only**变化，以利用现有async实际发送回调；case/vector/值/声明/SQL/正常stage不增不减，主矩阵继续承担已有同步拒绝覆盖：

| 既有目标 | 新实际API |
| --- | --- |
| NATIVE_DATE_REJECT_TIME.reject | ExecuteNonQueryAsync |
| NATIVE_INTERVAL_REJECT_min/max/tick.reject | ExecuteNonQueryAsync |
| TLS BIND_REUSE_NO_EXPLICIT_PREPARE_dt/dto.tick | ExecuteReaderAsync |
| TLS BIND_CLONE_RESET_REOPEN.tls_default_explicit_reject | ExecuteReaderAsync |
| 原G1 async拒绝、TIME async拒绝、leading async拒绝 | 原API保持 |

成功G2仍同步。High须按stage.api明确分派，不能给value-only阶段偷偷加Prepare、改Scale或替换parameter。manifest预期和独立validator要同时严格对齐实际API、receipt身份与窗口；不把同步零回调当作发送0。

## 5. 最小工具实施与验证边界

producer在target调用之前、精确catch之后、旧连接拒绝/正常恢复探测前后、fresh查询完成各写固定substep。已有report只在Recovery末尾写receipt的做法应改成先建立attempt记录再填完成字段，保证异常时保留最后完成步骤。

工具断言可新增独立安全code字段，但只来自**自身固定白名单**及类型，不从任意Exception.Message复制。外部异常继续仅Type/Number/既有安全FailureInfo；不能为诊断打印连接、SQL、环境、driver对象或完整stack。未完成step不得伪造完成receipt。

仅新工具revision修改producer/runner必要接线、case API期望、validator及其合同；产品包保持不变。Cases.cs如因API分派/后继stage连接管理改变，G2 read规则的源SHA必须按新freeze更新并由root审核“SQL/成功写合同未变”，不能留旧ce37 pin或取消来源门。112旧validator断言、24实际C#合同、来源/ownership/cleanup及read tuple有限集合全部保留并补状态分类mutants。

新增合同最少覆盖：Open成功恢复；Broken/Closed旧连接受控拒绝且无任何新send；缺State/错状态/错异常/旧连接发送非零拒绝；fresh原值改变、复用旧ID或暗中重开拒绝；退休后下一独立stage必须显式新ID；仍Open却无故换ID拒绝；reader失败不得使用写拒绝fresh例外；sync/async stage/API错配、拒绝target业务发送非零拒绝；中途失败仍保留安全substep。root审核新freeze后，Low先完整离线，再新完整shared/TLS运行，同时采状态验证本次来源推断。

旧v3失败、127项Python/24项C#离线结果及同包主矩阵全部保留原归属。当前无新DB窗口授权；新完整Supplement通过和root最终接受前，TP06仍未关闭。
