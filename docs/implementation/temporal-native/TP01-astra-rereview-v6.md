# TP-01 v6 NULL与fixed observer定点复审

日期：2026-10-08。结论：`static_accepted_for_complete_offline_validation / runtime_pending / database_not_authorized`。本轮限定变化在源码层符合 [shared-v5 triage](TP01-shared-v5-triage.md) 的窄修要求；六项输入SHA匹配，其余七项与v5快照不变。允许root冻结v6后交Low完整离线验证，不表示shared实库已修复或允许直接重试DB。

未运行unittest、dotnet、contracts CLI或数据库，未修改产品、工具源码或旧报告，仅写本文。原v5 shared失败、core260条部分观察、metadata23条后失败及两表已清理事实保持原样。

## 1. 已确认NULL的局部观察

`CaptureAdvisoryOracle`现在取得expected DBNull、同一reader的ordinal0 IsDBNull、两个server格式列NULL状态及真实Cell观察；这些信息仅用于专用ObserveConfirmedNull。普通Observe/Safe/CheckInfra实现没有被全局放宽。

ObserveConfirmedNull始终先调用实际getter。只有上下文有效、允许的typed/GetString getter、对应已加载provider的DmException6081、无真实timeout/socket/cancel/IO/auth等异常或inner链、无verified server response，且FailureInfo缺失或精确匹配既知W本地兜底形状时，才产生局部known_typed_null_rejection receipt。既知形状限定Transport/Fetch/WDM_TRANSPORT/NotSent、CancelSource=None、无unknown事务状态/服务器错误号。GetValue不在异常豁免名单。

可观察cache必须与NULL一致，payload_length/hash为null；明确unobservable允许依赖同reader实际IsDBNull，但不伪造cache内容。错误ordinal/lane、缺证据、矛盾cache、非NULL6081与未知outcome/kind不能得到receipt。后续Read/Dispose/cleanup仍走原错误路径，没有提前Complete、连接复用宣称或重试。

返回的Error仍使用原Safe结果：真实旧W的FailureClass=infra_failure和Transport/Fetch/WDM_TRANSPORT/NotSent均保留，receipt作为另一层上下文结论。没有将原FailureInfo清零或改写为“用户错误”以迎合validator。

已确认NULL下，官方getter实际返回CLR null/空串时直接记录returned-null/returned-string-empty及原type/text，不再让非空时间regex生成伪driver错误。非NULL时间文本仍走原SafeTemporalText。candidate W依旧要求typed/generic/GetString真实6081、GetValue真实DBNull，不允许官方观察形式放宽W合同。

## 2. validator与序列化边界

- 新typed_null_receipt核reader/getter/ordinal、6081、实际调用标志、NULL证据、原provider异常类型、原错误元数据及禁止的infra/unknown条件；没有修改通用failure()来吞Transport。
- null_oracle核顶层同reader NULL上下文和实际cached NULL；W的typed/generic/GetString都要求receipt，GetValue不能用receipt替代DBNull。O值仅作其实际NULL形式的观察，不冒充W正确性。
- 非NULL exact_oracles拒绝非null的known_null_receipt；C#record默认序列化出的`known_null_receipt: null`不会被错误当成有效receipt。新增shape和顶层mutant覆盖这一区别。
- Safe原字段保留；取消/事务/真实inner等额外receipt事实只含安全结构数据，无原message/SQL/连接输出。

真实产出字段在C#record的snake_case序列化与Python消费者之间一致。未发现新增的必需字段仅存在于手写factory、却不能从本次真实producer生成的情况。

## 3. fixed metadata的活Reader观察

`ExecuteParameterPhase`对metadata执行统一公开ExecuteReader，返回后立即调用live和offline共用的CaptureLiveExecutionSnapshot，先取得reader-owned statement、raw/effective metadata及真实EncodedScale，再开始Read/NextResult/drain。快照是观察当次拥有对象，不是从request或已dispose statement补值。

fixed DML随后调用共用DrainDmlResults：最多1024条意外row、64个结果步，正常完成后读取RecordsAffected并记录drain_completed/unexpected_rowset；意外rowset或越界按infra结束。using作用域随后Dispose，最后再以fresh连接核row及独立oracle。

validator按execution_api=ExecuteReader、snapshot_timing=live_reader_before_drain及after owner=reader核对，fixed仅保留参数/列语义，不再推出command owner；fixed要求完整drain、无意外rowset及RecordsAffected=1。关闭后的statement不可观察。

Core.WriteCase的`cmd.ExecuteNonQuery()`保持不变，原API极值/拒绝覆盖没有被偷换；事务rollback及DDL原执行也未因此变更。驱动DmCommand、DmDataReader、R3诊断、pool/transport生命周期没有修改。

## 4. 真实C#策略与独立合同

新增offline测试调用同一个ObserveConfirmedNull，通过反射构造当前provider的真实DmError6081/DmException及FailureInfo，再由getter delegate抛出；不是只在JSON写一个“预期错误”。已对照源码确认所用内部constructor、SetFailureInfo和属性存在，实际反射执行仍待Low验证。

测试带实际getter调用计数：W known-null、O null/empty、GetValue DBNull；14类否定上下文均不得得到receipt，包含timeout/socket/cancel inner、unknown、verified server、transaction unknown、GetValue6081。固定DML同样调用真实共用snapshot/drain helper，并检查reader关闭后不可取stale statement。fake DB与合成异常只存在offline路径，未进入真实SQL或凭据流程。

Python新增顶层NULL receipt mutator、O形式与W严格分离、fixed DML执行API/owner/drain/RecordsAffected/encoding负例；shape消费者核对实际C#serializer产物，known W错误必须保留原infra分类，否定分支不得授receipt。手写shape本身不作为C#已经运行的证据。

没有发现本轮增加的明显C#编译/签名可访问性或调用参数问题。反射构造、真实NULL getter表现、DML公开ExecuteReader的数据库行为必须由后续实际门确认；静态接受不能替代这些运行事实。

## 5. Low下一窗口

同意新v6 freeze后完整重验：全部portable Python→local-required→真实241e15 source/package CLI→runner `--offline-only` restore/build及新增C#策略/shape→Python消费该次实际C#shape。首失败保留并释放窗口，不能因v5离线已通过就跳过变化后的这些门。

关键最后两步仍使用：

```sh
python3 eng/temporal-precision.py --feed '<verified-feed>' \
  --identities tools/TemporalBoundaryProbe/identities.json --source-manifest '<241e15-manifest>' \
  --repo /Users/wanghongyi/Projects/W.DmProvider --profile shared --output '<new-v6-offline-dir>' --offline-only
PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/test_producer_shape_contracts.py \
  --shape '<new-v6-offline-dir>/offline-producer-shape.json' --output '<new-v6-shape-validation.json>'
```

Low采用10.0.203及既定环境/单节点参数，记录自然退出、每case结果与输入首尾SHA。全部离线通过后，由root另行批准新的shared全lane尝试；不继承v5 core260为v6通过，不改v5 metadata失败，也没有待恢复表需要操作。

## 6. 本次六项SHA256

| 文件 | SHA256 |
| --- | --- |
| Program.cs | `15a87d6db2073b38c3020eafd2554f29d3ca55920daebf6264f329d06105ce4d` |
| OfflineContracts.cs | `8707e4ef800c4a50f320f1e4e48da681cd59405804a9954e839ae971022c209e` |
| README.md | `41d85f0d35a820cb5b20eb95f60151042c550d1d76267a67bc8a4d2171360c37` |
| validator | `c57f08f8e6880628645a97e39183ec823a391b40b47b787594b0b28477fe744a` |
| test_validator | `70c0228454a8dd9f7242b81646ac31926883ede075b86d647ace434eae07956a` |
| test_producer_shape_contracts | `508626bb3b75990a8b193d187e0168ece2e44834b08826f2f08613c5df10aec6` |

其余七项与v5只读source快照相同。所有旧报告/失败及清理证据保持冻结；本报告完成后停止写入。
