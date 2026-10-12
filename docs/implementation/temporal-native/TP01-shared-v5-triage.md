# TP-01 shared v5 工具失败 triage

日期：2026-10-08。结论：`probe_context_and_observation_defects / narrow_tool_fix_required`。本次定位两项工具缺口：预期NULL getter的本地6081被旧W附带的Transport FailureInfo误触发全局infra停止；fixed DML执行后的statement观察仍假设由command持有，而实际ExecuteNonQuery已完成Reader生命周期。修复只限探针/validator/离线合同，不修改R3诊断、session恢复、pool或产品时间实现。

本次只读原始安全报告、源码及已存在的结果；未运行dotnet、DB或unittest，未读secrets。新报告不覆盖旧shared失败、离线通过结果或历史审阅。

## 1. 原始执行与清理事实

已核对SHA：

| 文件（TP01-v5-shared-baseline下） | SHA256 |
| --- | --- |
| verification-summary.json | `dc5be4305eb92783deef691b683ee62a72bfe5a8596b5c9fa05116bb0d6a0084` |
| core.json | `d2f54cb68d93e90ec0674a5a9fd077881e7155ade9f3ac6ccef2a6fc45573742` |
| metadata.json | `7e99b71507997c2921e5a65be96f4e401059b45930694e4eca9dcced461f91d3` |
| core.cleanup.json | `b1b3ce9e507f273edaf058ffde7d42f4601c5940e05ab626751875bf43495649` |
| metadata.cleanup.json | `854769811a16da5fc6607707cf5c2ac6e50dad95b384ce19b24c1f157e50b837` |

core进程完成260条观察，exit0/独立cleanup0；metadata只完成23条后exit1/独立cleanup0。实际失败stage为 **`O:bind_inferred_s7_punset_fixed_null`**；`last_case=O:bind_inferred_s7_punset_fixed_value`仅是此前最后一个已checkpoint的case，不是失败case。

原错误为W独立reader的 `W.Dm.DmException` / Number6081 / FailureKind Transport / Phase Fetch / ErrorCode WDM_TRANSPORT / Outcome NotSent / VerifiedServerResponse=false。外层stage的O是writer lane，错误来自后续W oracle，不可误说成官方driver抛了W异常。

两张本次owned表均已由普通TEST完成精确清理，并在独立新连接确认absent；没有待恢复对象。extended、最终characterization validator及TLS未运行。core完成是部分baseline观察，不是TP-01/profile接受；v5离线接受不等于这些真实运行分支已验证。

## 2. NULL根因证据

`Program.CaptureAdvisoryOracle`先读`r.IsDBNull(1)`，但随后无条件调用 `Observe(() => r.GetString(0))`。`ReadNullOracle`在fixed NULL写入后复用此路径。Observe的catch无上下文调用CheckInfra，Safe看到FailureKind=Transport就抛ProbeInfrastructureException，整lane结束，预期NULL值观察没有机会入report。

driver `DmDataReader.do_GetString:1063–1077`从当前cache取原始值，值为null即抛ECNET_NULL_VALUE（6081）；`DmGetValue.GetString:352–358`也保留该NULL合同。`DmSession.CreateFailureInfo:647–660`在没有已验证server error、timeout或cancel时采用Transport默认分类，当前invocation没有发送则Outcome=NotSent。因此这个**本地cached NULL转换错误**可带Transport/Fetch/NotSent；不能只看该枚举就判断真实传输失败。

真实错误号/结构化元数据与源码中的首个GetString(NULL)路径一致。此处不需要重连重现，也不需要为了探针修改R3全局FailureInfo。原报告中的infra_failure是当时工具的真实分类结果，继续保留；新工具另记录上下文判断。

## 3. 极窄 known-null 观察方案

新增专用NULL观察入口/receipt，供已证明NULL的oracle分支使用；普通Observe/Safe/CheckInfra保持原逻辑。它必须调用真实getter，不能看到NULL就直接返回“预期6081”。建议以独立结构记录 `observation_class=known_typed_null_rejection` 和NULL上下文，原ErrorInfo各字段原样保留，不将Transport改成Unknown/User或清掉WDM_TRANSPORT。

允许上下文必须同时满足：

1. 当前case明确expected为DBNull（不是只看异常号猜NULL）；记录实际reader_lane/ordinal，而不是外层writer stage。
2. 该活Reader同一ordinal的真实IsDBNull(0)=true；server格式列的IsDBNull与之相符。已有cached cell观测若可用，必须is_null=true且payload/hash为空；矛盾或非NULL证据不能接受。不能仅凭TO_CHAR列为NULL推断原值为NULL。
3. 仍执行精确的GetString/GetDateTime/GetDateTimeOffset或相应generic typed getter；该getter实际抛出已加载对应provider的DmException，Number精确等于6081。GetValue正常合同是DBNull，不将其异常冒充typed-null成功。
4. 不得含任何真实Timeout、OperationCanceled、Socket、IO、Authentication等异常或inner链；不得有已验证server错误、terminal cancel、OutcomeUnknown/Unknown或异常send/receive/commit等阶段。ProbeInfrastructureException不可重新吞掉。
5. FailureInfo缺失的传统官方NULL错误可以按明确本地6081合同观察；若W存在本次已知兜底信息，只允许精确的Transport/Fetch/WDM_TRANSPORT/NotSent且无取消/未知事务结果。其它结构化组合不自动放行，继续原infra/错误处理。所需CancelSource/TransactionOutcome等安全结构化事实可追加记录，不能输出原message。

该例外只绕过此getter的误分类，不提前Complete任何invocation、不标connection reusable、不发重试、不跳过下一次Read/Dispose。后续Read或cleanup真实失败照常终止。不要在Safe中加“Number6081永远不是infra”，不要全局忽略Transport/Fetch/NotSent。

validator对NULL观察独立核receipt、预期NULL及实际NULL证据、getter身份/错误6081和禁止条件；不能只保留当前`error.number==6081`的宽松判定，也不能修改通用failure函数让其它Transport记录通过。缺少上下文、错误ordinal、非NULL6081、带timeout inner、OutcomeUnknown或server response的mutant全部拒绝。

## 4. O NULL返回null/empty必须真实记录

root另确认：官方GetString(NULL)可能返回CLR null或empty string。当前Observe将getter与SafeTemporalText放进同一try，空串会被正则`+`拒绝，继而把**探针自己的格式校验异常**误写成driver异常。此事实也应在同一个已证明NULL的专用分支处理。

已确认NULL上下文中，真实返回null/empty时原样记录returned-null或returned-string-empty及适当type/text；不替换成伪6081，不调用需要非空文本的时间regex。非NULL时间文本的安全检查不放宽；其它不受支持文本不记录原始异常message。

candidate只对W要求既有NULL合同：GetValue/provider值为DBNull，typed/generic/GetString必须真实6081。O的null/empty/6081保持独立表征，不能要求O冒充W行为，也不能以O空串允许candidate W空串通过。检查当前`exact_oracles(null)`：它对O/W的typed分支同样强制6081且遗漏GetString，修复应明确分lane并补W GetString。

## 5. fixed DML观察缺口的源码与实测

原metadata最后成功的 `O:bind_inferred_s7_punset_fixed_value` 三phase（initial/replacement/reprepare）均status observed、fresh persisted_rows=1、O/W oracle完成；before owner=command、IDs55/56/57，after owner=unobservable、after ID=null、parameter_metadata=[]、encoded_scale_observable=false。这是工具观察时机问题，不是数据写入失败；候选严格metadata门未来不能用这种空快照接受。

W源码同样说明为什么fixed路径不能假设command仍持有statement：`DmCommand.ExecuteNonQueryComplete:555–607`调用ExecuteReaderOwned，取RecordsAffected后finally关闭Reader；ExecuteReaderOwned在公开返回前将command.m_Stmt清空。v3修复只对advisory捕捉了活Reader，fixed仍在ExecuteNonQuery返回后观察，已晚于transfer/dispose。

最小probe方案可行：**仅metadata fixed DML改用公开ExecuteReader**，取得Reader后、推进/关闭前，通过已有SelectOwnedStatement(cmd, reader)捕获真实before→after owner ID、raw/effective metadata及TemporalBinding的EncodedScale；然后正常消费结果/NextResult、读取RecordsAffected并Dispose。`SeekFirstReadableResultOwned`即使跳过DML结果也未把reader.m_Statement清空，statement仍在Reader关闭前可观察。不能让驱动保留command.m_Stmt、不能缓存过期statement或从请求scale伪造EncodedScale。

建议流程：

```text
固定目标与参数 → Prepare并捕获command before
→ public ExecuteReader（execution_api明确记录）
→ 活Reader捕获真实statement/metadata/encoding快照
→ 完整消费已允许结果；记录RecordsAffected和drain完成
→ Dispose
→ fresh连接检查本次row，并按原独立O/W oracle读回
```

该INSERT预计没有业务rowset，unexpected rowset/未完成drain应是工具执行异常，不能当成功。after owner应按实际execution_api核对为reader，不再由fixed=true推导为command；fixed仍表示参数describe/列约束，不是statement所有者。phase记录execution_api、snapshot时点、records_affected/drain事实。

**Core.WriteCase继续公开ExecuteNonQuery**，覆盖原DML API与极值/严格拒绝；只有需查看活执行快照的metadata fixed阶段换API。不能把core也改为ExecuteReader后宣称原ExecuteNonQuery覆盖未变。官方路径同样用公开API，不依赖反射执行私有DML；反射仅观察已拥有的statement和不可变快照。

## 6. 限定修复及独立验证

范围：Program/OfflineContracts中NULL上下文策略、真实getter结果观察、fixed metadata执行观察；validator及独立Python shape/contract tests；必要README。共用政策和序列化必须真实用于live路径，不能在offline测试另复制规则。原R3产品、包、source manifest、TLS/TEST wrappers及能力门不变。

新增离线门至少包含：

- 实际C# getter delegate确被调用；expectedNULL+实际NULL+6081+旧W兜底信息得到known-null receipt，原FailureInfo完全保留。
- 同号但非NULL、expected非NULL、错误ordinal/reader lane、缺/矛盾NULL证据、timeout/socket/cancel inner、Unknown/OutcomeUnknown、verified server error均不能走例外。真实Transport错误仍终止。
- 已确认NULL下官方null/empty原样记录，不生成探针伪异常；candidate W返回null/empty被validator拒绝，GetValue DBNull正例保持。
- fixed DML的before command→after live Reader快照及关闭后不可观察；execution_api/drain/RecordsAffected字段由实际共用执行/采样代码生成，Python按新schema独立核对。
- Python mutation删除known-null上下文、改原错误为timeout、换GetString返回、把fixed after owner伪标command、缺失真实EncodedScale或drain，均拒绝；typed NULL错误与真正infra不能混成一个宽松分支。

High定点修后停写，Astra/root复审新输入；Low先新freeze完整离线（portable/local-required/真实source-package/build/C#策略+实际NULL shape/Python消费），再由root批准新shared窗口。新实库重新完成core/metadata/extended及最终validator，不继承旧core260为新版本通过；TLS仍独立后续门。任一失败保留退出码/证据并释放窗口，不修改已冻结v5结果。

当前清理已完成，没有需执行的恢复动作。本报告只是工具缺陷定位和窄修设计，不是实库重试授权或本专项接受。
