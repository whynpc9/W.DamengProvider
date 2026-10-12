# TP06 supplemental tool 关键静态审阅 v1

2026-10-09。结论：**changes_requested / static_not_accepted**。确认三项工具阻塞；先完成本轮定点修复和新冻结，再复审、交Low独立验证。没有运行dotnet、Python测试、SDK、consumer或DB，没有修改产品及工具。

输入为 `.local/temporal-native/verification/TP06-supplement-v1/source-manifest.json`，实读SHA256 `bee0409fedc70420d243f8b2076ac8e64ed32f705e569f59bcdc3dff4b5bf9bc`。17份工具文件SHA均已实读并匹配两位High最终交接；owner/root提供的17文件aggregate为 `48426c72055210974479fde7df413a2732ba76b68e5e1646e3d8096c5479581c`。该快照另包含两个v11只读依赖。原design `c4e1f7cd…`、leading addendum `4b378e88…`、observability addendum `f24005af…` 不变。

## R1 / P2：Fetch门允许NextResult opcode44代替真实Fetch

位置：`tools/TemporalCandidateAcceptance/contracts.py:688–697`，特别是`:691`。

`READER_ORDINARY_FETCH` 当前接受 `fetch_opcode in (7,44)`。既有协议及冻结设计区分7=Fetch、44=NextResult；128行结果完全在缓存内、只有44而没有7时，若producer误将44写成fetch receipt，validator仍可接受“已经跨页”。现有 `test_fetch_rowcount_without_wire_receipt` 仅把count改0，不能发现该错误。producer的Readers.cs当前实际数7，缺陷在独立validator边界。

最小修复：此门限定opcode7；NextResult的44只用于独立多结果门。增加从完整成功synthetic control只修改7→44、维持非零count及完整128行的负例，必须因错误opcode拒绝；保留0/缺失Fetch负例。不改产品或扩大行集。

验证：Low在新冻结运行validator合同；随后实库receipt必须确实观察到7且页边界和总行数匹配。synthetic成功不代替实库Fetch。

## R2 / P2：普通NULL路径伪造独立server NULL观察

位置：`Observations.cs:57`、`:81–86`；消费端 `contracts.py:observation` 的server NULL分支。

ReadObservation从时间列ordinal取得nil后，`:82` 在nil时直接令server text=null，没有访问 `ordinal+1` 的TO_CHAR/CAST投影；`:86` 再把相同nil/text写成 `source=server`。因此native NULL及普通reader NULL的“独立服务器NULL”只是W时间列IsDBNull的重述。时间cell错误地被解码为NULL时，该伪造server记录无法发现真实服务器投影仍为非NULL。SequentialObservations已经在NULL路径实际调用ordinal6的IsDBNullAsync，可沿该已实现模式修正。

最小修复：普通路径对server投影单独执行IsDBNull/IsDBNullAsync；NULL时必须核验服务器投影为NULL，并记录实际访问receipt。非NULL继续真实读字符串；不要把时间列nil复制为server事实。validator要求对应真实server-null字段/API/ordinal，添加“时间cell为NULL、服务器投影非NULL”和“服务器投影getter未执行”的定点负例，均须拒绝。保持已知6081例外的现有狭窄范围，不新增异常吞并。

验证：C#离线helper控制应调用修复后的实际采集路径证明server-null getter确实执行；Low运行其新冻结合同及独立validator mutants。实库NULL fresh readback仍需执行并留下独立投影证据。

## R3 / P1：leading观察把statement allocation当Prepare和literal业务响应

位置：`Leading.cs:64`、`:85`、`:96–106`。产品只读依据：`Internal/Legacy/A/T02_02000093.cs:111–130`、`B.Async.cs:461–468`、`A.Async.cs:66–78`。

产品opcode3是AllocateStatement。Prepare和direct SQL共用opcode5，执行/仅prepare由请求里的P4标志区分；prepared Execute使用6/13。当前LeadingExecute只收3/6/13，并以出现3计算reprepare_count：同一statement发生真正的Prepare5会被完全忽略，工具仍可能宣称current_no_reprepare=true，错误地沿用执行前raw。

另LeadingServerControls仅捕获opcode3作为server_response。其无参数literal `ExecuteNonQuery()` 实际业务响应为5；100天→DAY2拒绝时所保存的是此前allocation3（通常SQLCode0）或null，不是该业务拒绝的真实响应。这阻断精确错误号审批，也会把错误响应身份指向无关操作。

最小修复：按实际已知调用上下文分别观察allocation3、Prepare/direct5、prepared Execute6/13。在LeadingExecute已完成明确PrepareAsync之后的目标窗口，额外5至少应使“无未取证reprepare”断言失败；不能忽略或凭opcode3推断没有prepare。literal控制的响应必须关联该次direct业务5及其实际错误code。保留6/13真正发送与响应分离，不能用响应计数冒称发送计数。增加对额外5、只有allocation3、literal真实5错误与错误响应替换的合同负例，并让validator核对实际frame receipt与汇总的一致性。

已经实读 `B.Async.cs:81–82,129–130`：AfterFrameSent在SendFrameAsync/SendAllAsync完成后触发；当前Leading使用ExecuteNonQueryAsync，因此它可以提供实际完成发送receipt。AfterFrame仅是response观察，不能将它的零计数当作独立发送证明。fixed100的current binding未发布例外保持：真实本次fixed raw + 99 control_only binding + writer Overflow路径 + 目标业务发送事实 + fresh状态；不能复制旧binding到current字段。

验证：修复后C#离线合同应通过同一frame分类/采集逻辑验证正确与错误opcode；独立Python validator再验证收据mutants。Low随后用新工具进行原计划窗口。所有未批准leading服务器号仍为pending/failure；本轮不批准任何新错误号。

## 已核对且不扩大本轮范围的部分

- 42 shared / 36 TLS固定case、同候选source/archive/nupkg/DLL/MVID/caps绑定、精确PackageReference和runtime deps检查存在。runner与可执行CLI参数位置、独立cleanup入口匹配；源码读取未发现可确认的C#签名错误，但未编译，编译门仍必须由Low执行。
- 身份检查在Configure/Open前，每次Open验证TEST与physical profile；secret只由原TEST wrapper加载，build环境去掉DAMENG变量。子进程输出丢弃，报告未输出原异常message/SQL/凭据。nonce/完整marker保护、CREATE状态、foreign/known-failure拒绝DROP、独立fresh absence与cleanup失败门保留。
- G1显式UPDATE/CommitAsync/RollbackAsync及fresh值比较不是仅count验证；SequentialAccess分配独立alias并顺序读取，raw在时间getter前捕获；shared O的typed offset oracle与已知generic拒绝分别记录。
- G2是明确显式DbType/profile-Scale的补充生命周期场景，后续reuse仅Value，metadata变更路径不手动Prepare；clone变更原对象后执行克隆前没有修复克隆。完整inferred默认声明轴继续由原v11承担，**不要求为此次补充新增inferred矩阵**。
- fixed leading和advisory/server拒绝保持分支，server错误无默认批准；typed provider-object输入仍未开放。此次不为修工具添加产品hook或重建产品，不扩大BDTA、非默认BufPrefetch/TIME/interval mode、LOB或R3压力。

本轮已有明确阻塞，按root指示先收口交修复。v2复审继续核对修复后的端到端收据与剩余范围，不把以上观察写成完整工具或运行接受。v1所有文件、未执行状态和本报告保留；High修改须形成新冻结，Low继续独立验证，当前正在进行的v11主实库窗口不受新工具修改影响。
