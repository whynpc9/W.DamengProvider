# TP06 fixed leading 拒绝的可观察性补充

> 阅读提示：这是时间精度专项的历史取证设计，不是当前操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-08。状态：**design_accepted_for_tool_implementation / runtime_pending**。补充 [leading 来源裁决](TP06-coverage-addendum.md)，不回写原audit或该裁决。仅放宽“fixed DAY2拒绝必须观察到本次已发布immutable binding”这一不符合产品发布顺序的取证要求；精确异常、真实来源、无业务Execute、fresh状态与服务器错误批准门均不放宽。此次只读源码，没有build/tests/DB或新增产品hook。

## 独立源码依据

`DmCommand.cs:1708–1710` 在本次Bind开始清除plan与field的旧binding。`:1730` 在局部变量创建本次描述，`:1739–1740` 先调用实际writer；只有writer成功后才在`:1741` 保存到待发布数组，全部参数成功后`:1759–1760` 才发布。`DmSetValue.cs:1533–1536` 进入interval编码，`:1606` 将TimeSpan与冻结packed交给DmIntervalDT；其`:51–58` 调用 `DmTemporalIntervalFields.FromTimeSpanExact`，后者`:29–34` 对DAY2的100天抛Overflow。因此该失败本来就没有一个可供反射读取的已发布本次binding；失败清理还可依法释放statement所有权。

成功99天控制的binding是一个已完成调用的事实，不能充当失败100天调用的current binding。执行前ParameterDescribe是独立immutable raw来源，可以合法保留值快照；它不是从失败后的effective field重建的metadata。

## fixed DAY2 的最小充分证据

以下必须同时成立，才可结合已通过v7的冻结binding合同接受本次live fixed-leading拒绝：

1. 已有实际DAY2/SECOND6列和TimeSpan99天写入成功控制；控制通过同一产品command/writer路径，持久化/独立oracle精确，且成功控制实际捕获了已发布 `ReliableFixed=true / CType21 / packed0x626 / leading2 / fraction6`。该记录明确标 `control_only`，带其自身case、statement、physical和包身份，不修改其时间归属。
2. 对100天目标调用，在执行前取得其实际statement的真实 `ParameterDescribe / flag1 / CType21 / rawscale0x626 / masks0`。目标参数本次CLR类型为TimeSpan、精确100天、Scale未设或6、Precision未设或0；合法控制和目标的SQL/参数声明一致，仅替换值。public Prepare可先发生。记录实际command/statement/physical身份；如果执行过程中又换statement或重新describe，必须证明使用了新的对应raw，不能沿用已失效来源。正常statement未变并且只换相同CLR类型值的证据可成立，不要求为了观察而强制重新Prepare。
3. 只包目标执行的catch取得精确 `OverflowException`；该调用业务Execute计数增量0，明确区分此前Prepare等非业务帧。异常不能来自输入构造、oracle、cleanup或测试断言。不得以总发送计数为0代替业务发送边界。
4. fresh独立连接确认原行仍为精确99天、行数和DAY2/SECOND6 schema未变；失败后的合法简单查询及最终cleanup按既定合同通过。
5. 100天的当前binding如未发布或owner已释放，记录 `current_binding_observed=false`、`current_binding=null`、`constraint_evidence=pre_execute_parameter_describe_plus_control`。可以据源码说明其预期发布边界，不能把推论写成实际观察到binding内容。若仍有合法观察点，应如实记录null；如无合法owner，标unobservable，不能持旧statement反射制造可见性。

validator必须分别校验控制记录与目标记录及关联，不允许将control binding复制到current字段；任何current绑定内容与本次raw冲突、身份无法关联、目标发生未取证的reprepare、业务Execute非零、异常不符或fresh状态变化均失败。没有真实fixed DAY2 describe时，这条分支仍不可接受，转入既定advisory/server来源判定；本补充没有把结果列schema提升成参数约束。

advisory100天先成功编码DAY9并进入业务Execute后才可能收到服务器错误，因此可用**已经存在**的只读数值frame观察接口，在当前response和仍合法owner处采集本次field/binding，明确回调时点、statement和调用关联；不新增产品hook、不写内部状态、不在回调执行SQL。若本次advisory binding或真实server response仍不可观察，继续失败，不能援引fixed预发布例外。

## leading错误号的取证节奏

为避免新增partial-run接受语义，默认保留现有完整G1/G2/G3首次运行：若出现尚未批准的advisory服务器leading错误，保存独立控制和安全响应并以pending/failure退出，由root/Astra精确审核，再冻结预期输入重跑受影响门。此前通过的其他case只保留为该冻结输入的事实，不偷偷改原summary旗。

若High已有低成本group选择入口，可先做仅leading的**characterization**来收集shared/TLS各自真实来源和错误号，但其manifest必须明确只包含leading控制与case，独立验证器固定 `candidate_acceptance=false / full_temporal_acceptance=false`；仍检查同包、source/asset/MVID/caps、TEST与精确cleanup。不能因部分case成功走完整candidate分支。无此现成入口无需为该目的增加新模式；本设计不要求额外实现或先执行DB。

任何节奏下，未批准错误号不得通过candidate门；审批依据及作用域仍按上一补充限定。所有功能/工具变化继续由High完成、冻结后整体静态审查、Low独立运行。此文只解决证据可达性，不接受尚未执行的假设。
