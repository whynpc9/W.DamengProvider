# TP-01 v2 Astra 整合复审

日期：2026-10-08。结论：`changes_requested / runtime_not_run`。11项冻结输入SHA已逐项回读，与root交接一致；未运行unittest、contracts CLI、dotnet、数据库或读取secrets，仅写本报告。[原R01–R08报告](TP01-astra-review.md)保持不变。

v2已实质补齐来源绑定、独立case预期、重复JSON键与bool/int区分、实际payload观测以及精确owner marker。当前仍有C#样本构造、producer/validator schema和R3生命周期接入的确定缺陷，因此不能交Low启动正式验证窗口。先统一修复以下B01–B07，重新停写及复审；不要用实际DB去发现已知不匹配，也不修改驱动迎合probe。

## 1. B01：非时区样本被条件表达式隐式提升为DateTimeOffset

位置：`Program.cs:487`。

```csharp
static object TemporalSample(bool tz, long fraction)
    => tz ? new DateTimeOffset(...) : Ordinary.AddTicks(fraction);
```

两臂分别是DateTimeOffset和DateTime；.NET定义DateTime→DateTimeOffset的隐式转换，因此此条件表达式有DateTimeOffset自然类型，外围返回object并不能保证false分支装箱成DateTime。非TZ的Parameters/TailStability因此可能绑定成DTO，并引入机器本地offset。API依据已在本机net10 reference XML `System.DateTimeOffset.op_Implicit(System.DateTime)` 回读，无需执行代码；实际行为由Low后续离线C#门验证。

修复：显式if分别return，或在分支显式转object，确保false精确返回DateTime(Unspecified)、true返回DTO(+330分钟)，ticks不变。不修改独立expected以适配错误样本。

必要测试：用真实C# helper分别产生false/true，检查GetType、ticks、Kind/offset；Python手工fixture不能验证C#条件表达式语义。将该测试并入B05要求的离线C# selftest，且在任何配置/身份/DB入口之前执行。

## 2. B02：参数拒绝记录缺少validator要求的顶层persisted_rows

依据：`Program.ParameterCase:613–642`的顶层result没有persisted_rows；实际执行及fresh检查位于 `ExecuteParameterPhase:516–544`，只写到phase_results。validator `validate_binding:403–407`对预期拒绝调用 `reject(case,...,"driver_parameter_rejected")`，其默认persisted=True，`:98`立即要求顶层persisted_rows。因此所有候选Precision/Scale/typed NULL预期拒绝都可因missing字段失败，而不是按真实拒绝结论接受。

手工positive factory的binding payload含顶层persisted_rows，恰好掩盖真实producer不提供该字段的差异。修复两侧选择一个规范：推荐检查initial失败phase的真实持久化/未执行事实，或由producer从该phase明确提升结果；不能凭“预期失败”硬填零。参数setter在SQL执行前失败与已分配row后执行失败分别记录，后者仍fresh count核0。

必要合同：至少一份严格按Program真实层级构造的拒绝正例（top没有未经定义的聚合字段），覆盖fixed/advisory、set阶段与execute阶段；缺失真正必要的phase/persistence观察仍拒绝。保留真实异常类型与failure_class要求。

## 3. B03：把结果列metadata误当fixed参数metadata

依据：`ColumnDescriptor:493–496`返回Reader结果列。现driver的 `T02_02000094.cs:340–398`创建DmColumn并设置CType/precision/scale/mask，但不SetTypeFlag；DmColumn构造函数为空转发，DmField的byte m_TypeFlag默认0。它没有参数describe的fixed=1语义。

validator `validate_binding:401` 将column_metadata按 `metadata(..., candidate=True, fixed=True)` 验证；`validate_metadata:494` 对dual-scale的ColumnDescriptor也传fixed=True，即使baseline也强制TypeFlag1。实际合法结果列flag0将使baseline dual-scale门失败。手工fixture `meta(..., True)`还把这些列错误标成parameter_describe，因而纯Python正例没有发现问题。

修复：明确拆分ResultColumn与ParameterDescribe校验。column_metadata应为result_column来源、结果列flag0（若实际服务器/API有其他明确语义另行证据化），参数before/after snapshot才按fixed1/advisory2判断。固定列约束的可靠性来自该列descriptor与参数fixed mode共同确认，不靠伪造结果列TypeFlag1。

必要合同：actual-shaped列flag0/source ResultColumn正例；把它伪标1/ParameterDescribe的负例；fixed参数仍要求真实ParameterDescribe/flag1，advisory generic raw CType2与effective26/27保持不同且可接受。此区别交接TP-04，禁止为了错误validator修改产品结果列标志。

## 4. B04：probe读取command.m_Stmt不符合R3 Reader所有权

依据：probe `ParameterMetadata:818–826` 与 `EncodingObservation:497–510` 都只读cmd.m_Stmt。driver `DmCommand.ExecuteReaderOwned:947` 在公开ExecuteReader返回前已将m_Stmt置null，语句交给Reader.m_Statement；Reader.CloseOwned再销毁/清除该语句，未把它归还command。

`ExecuteParameterPhase` advisory在`:533–535`取得Reader，随后`:536`再从cmd抓metadata已经不可用；`:539`编码scale同样不可观测。replacement的prepare=false且上轮Reader已关闭时，`:524–525`的before metadata也可能为空。故真实候选将不满足validator的单条before/after snapshot及EncodedScale规则，不能把此失败归咎于未实现TP-04属性。

同组第二个生命周期问题：TailStability在legacy/TLS七位关闭时，以第二个非整微秒值触发预期Overflow，然后立即在同一连接上reprepare（`:582–584`）。R3 `AbortStatementAfterFailedExecution:1435–1462`对这种非recoverable绑定失败清空statement并可能detach物理session；`DmInvocation.ShouldAbortAfterFailure:56–65`没有普遍的“本地参数失败可复用”豁免。probe不能把预期值拒绝后的session复用当作驱动必须满足的新契约。

修复：在**活Reader仍拥有statement时**读取真实参数raw/effective/TemporalBinding快照，before与after来源分别标明。实际R3会因Reader关闭重新prepare的路径必须观察新prepare，而不是复用上次快照假装before；可显式prepare建立可观察点，记录实际行为。失败phase保留原拒绝及可取得的snapshot；若连接/statement已失效，用明确新连接/命令跑恢复后的scale6正例，并记录恢复来源，不能宣称是同一物理statement的reprepare。

不得回写产品m_Stmt、延长Reader租约、绕过Close/Discard或伪造EncodedScale。必要离线C#合同：command无statement而Reader持有statement时取正确对象；Reader关闭后不取stale对象；失败导致session不可用时probe选择恢复而非继续旧owner。validator对这一真实schema建立正反例，保持完整ticks/metadata检查。

## 5. B05：R06目前没有执行C#创建/marker/cleanup逻辑的离线门

静态看，Program现在把完整nonce放入同一次CREATE的VARCHAR(32) DEFAULT；Cleanup要求confirmed/create_inflight、实际DATA_DEFAULT匹配完整nonce后才DROP；known_create_failure、reserved及错误身份不授权DROP。runner的finally/cleanup失败停止规则也已正确改为两个进程都成功才继续。

但 `test_runner_contracts.py:113–185`测试的是Python execute_lane，fake_process直接返回预设code；known_create_failure用例只检查记录的state/exit，不调用C# MarkerMatches/LoadOwnership/Cleanup，也不观察真实C#是否执行DROP。现Program只有run/cleanup入口，没有offline selftest。不能把这些Python通过称为C#清理保护通过。

修复/验证要求：新增小型C# offline selftest或可注入DB操作适配，**共用live调用的真实所有权判定/marker解析/cleanup分支**，不在测试另复制一份判定。入口在Configure/Open/读取任何DAMENG环境前分派，fake接口记录CREATE、marker query、DROP和fresh verify调用；未授权身份不得抵达DROP。随后Low在离线build后、任何DB前运行该门。

至少覆盖：预存对象、CREATE明确失败、CREATE ACK丢失+真marker、marker缺失/错误、同24位前缀但不同完整nonce、错误profile/package/identity、坏/重复key manifest、reserved/inflight/confirmed、cleanup失败与fresh absence失败。B01的实际C#样本类型和B04的owned statement采样也可并入同一离线入口。不得使用真实业务表做这些故障注入。

## 6. B06：TLS七位关闭仍被强制依赖七位列存在

依据：validator `validate_core:334–341`对shared与TLS一律要求D7/Z7和26/27列metadata；Program `WriteCase:392`在参数配置前调用ColumnDescriptor(D7/Z7)。如果TLS服务器拒绝七位DDL，已独立建立的六位表可正常工作，但七位closed请求先因缺列变failed，无法到达要求NotSupported的公开参数能力门。

这与“TLS6严格、TLS7未开放时显式NotSupported；服务器是否具有七位列是独立表征”相冲突。当前手工TLS正例假定D7/Z7都存在，没有覆盖此分支。

修复：shared/任何root已批准七位profile继续强制完整D7/Z7、七位Min/Max及独立oracle；TLS七位关闭时六位主门始终完整，七位DDL结果单独分类，closed请求采用不依赖未存在七位列的已定义公开参数路径（如advisory DateTime2/DTO+Scale7），包括整微秒输入，并要求NotSupported/无业务持久化。不能用在六位fixed列上失败的wire不兼容冒充profile gate成功，也不能接受七位成功写出。若TLS服务器七位DDL已实际成立，可保留原fixed列拒绝作为附加证据。

同理，TLTZ独立seed在 `validate_extended:660` 即使baseline也传approved=True；当类型DDL成功而某个极值seed被服务器拒绝时，不能把“列存在”当“全域种子必须成功”。baseline应保存具体server/driver/infra分类与未完成读验证状态，不能宣称TLTZ已支持；candidate关闭TLTZ读门至少要有成功独立种子的真实NotSupported对照，确实无法构造的值单列经批准的不支持范围。不得通过任意failed放过主profile七位极值。

必要合同：TLS7 DDL已验证拒绝但六位完整+advisory7 NS的正例；相同payload换成advisory成功/任意DmException/缺六位极值则拒绝；shared删除D7或拒绝Max仍失败。TLTZ seed失败分类与读门未验证不可混写为支持。

## 7. B07：candidate gate接受不同于真实producer的手工schema

这不是否定独立expected：手工expected仍应独立。但目前factory额外提供顶层persisted_rows、把ResultColumn当ParameterDescribe、假定cmd生命周期中metadata始终存在，直接导致B02–B04未被发现。

增加一层**真实producer形状合同**：C# offline模式用实际Report/Metadata/phase序列化代码输出不含DB证据的synthetic报告片段，Python检查其结构与validator入口一致；预期ticks/类型/scale仍来自独立手写表，不能调用producer来生成正确值。标记expected-only，不得将这些JSON用于实际profile验收。保留现有24方法的删/改/failed/共同漂移mutant覆盖，并补B01–B06新增负例。

若仅想用Python shape fixture替代C#序列化，也必须逐字段对照真实producer并由C# selftest验证关键可观察字段能产生；不能继续在factory单方面补字段令positive通过。

## 8. 原R01–R08的当前处置

| 原项 | v2静态结论 |
| --- | --- |
| R01 source→package | producer分开comparison_head与实际2b39fec head，固定241e15 source manifest；精确manifest/archive/package/DLL/MVID关联已建立，真实PE/MVID CLI与local-required门已提供，等待Low运行 |
| R02核心case与独立oracle | 独立core_specs/expected比对、Kind/localUTC/Getter/text/组件规则已补；validate_core额外强制shared主七位及所有六位整微秒成功，cap缩域不能把它们变成通过的拒绝；保留该强门 |
| R03绑定矩阵 | 大部分逐格规则补齐，但受B01–B04和真实schema问题阻塞 |
| R04 TLTZ/8、9位 | 独立server seed与W双侧NS门、高精度以actual server值验证、声明scale文本位数核对已补；B06的闭门/存在性关系仍需修 |
| R05错误分类 | Open包为infra、结构化kind/phase/code与verified-response分类已补，Attempt不再吞infra继续；baseline明确unverified observation而非capability acceptance；没有发现须回退这些规则的理由 |
| R06清理 | live源码静态保护加强、runner finally与双失败停止正确；B05要求真实C#分支离线执行，不能仅接受Python调度模拟 |
| R07布局观测 | Cell读取真实m_RsCache.datas[datasOffset][ordinal+1]和isRsBdta，与当前DmResultSetCache.GetBytes索引一致，不按CType造长度；DATE/TIME/TIME TZ样本已补；raw/effective advisory区分正确，B03/B04需修观察对象/时点 |
| R08严格schema | shared strict loader重复key拒绝、true_int排bool、profile/lane枚举及actual/expected共同漂移负例已补，未运行不能称通过 |

额外交接：root已确认TLTZ关闭只约束适用值getter（typed DateTime、generic DateTime、GetValue、GetString）；metadata/schema观测不得被值能力门挡掉。已验证TLTZ矩阵中不适用的DTO getter仍InvalidCast，不为了probe改产品投影。

metadata validator的nominal CType→normal length表只是本轮允许的布局集合。TP-01若观测到同族但不同实际长度，必须按真实证据分类并由root确认，不能改producer填成normal长度，也不能为验证器要求而改驱动合法旧适配。TP-02明确layout纯核心不受本报告影响。

## 9. 修后Low顺序与命令模板

当前**不开放**正式Low窗口。High修复范围仍为TP01工具/合同/README及必要新增offline selftest文件，不涉及驱动源码、Builder、wrapper或secrets。两High明确文件归属后一次性修复，停写、Astra/root复审新hash再冻结；11份v2输入只读保留。

修后建议阶段：portable Python → local-required真实归档调度合同 →真实source/package CLI →离线build →真实C# offline selftest →baseline shared三lane及validator →独立baseline TLS三lane及validator。任一失败保留原结果并释放窗口。C# offline入口由High交付具体CLI后加入Low命令清单；尚未存在的入口不能写成已执行。

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/TemporalBoundaryProbe -p 'test_*_contracts.py'

TP01_V23_SOURCE_MANIFEST='<Low冻结的241e15 manifest>' TP01_VERIFIED_FEED='<Low核实feed>' \
  PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/test_runner_contracts.py --local-required

PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/contracts.py \
  --identities tools/TemporalBoundaryProbe/identities.json \
  --source-manifest '<Low冻结的241e15 manifest>' \
  --package '<feed中的原v23 nupkg>' --output '<新目录>/source-package.json'
```

离线restore/build采用global.json 10.0.203，实际host `/usr/local/share/dotnet/dotnet`，设置任务独立可写DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1，单节点/noReuse/noSharedCompilation/disable-build-servers；不在TEST wrapper内构建。工具consumer必须继续使用精确PackageReference，实际加载DLL/MVID门不削弱。

完成offline selftest后再由原仓库wrapper执行runner；公共模板为：

```sh
python3 eng/temporal-precision.py --feed '<verified-feed>' \
  --identities tools/TemporalBoundaryProbe/identities.json --source-manifest '<241e15-manifest>' \
  --repo /Users/wanghongyi/Projects/W.DmProvider --profile shared --output '<new-shared-dir>'
python3 eng/temporal-precision.validate.py '<new-shared-dir>' --gate characterization --output '<new-shared-validation.json>'

python3 eng/temporal-precision.py --feed '<verified-feed>' \
  --identities tools/TemporalBoundaryProbe/identities.json --source-manifest '<241e15-manifest>' \
  --repo /Users/wanghongyi/Projects/W.DmProvider --profile tls --output '<new-tls-dir>'
python3 eng/temporal-precision.validate.py '<new-tls-dir>' --gate characterization --output '<new-tls-validation.json>'
```

路径占位由Low用已冻结真实路径替换，原v23 identity的source_archive_sha256=null不传伪archive；这不是运行结果。候选驱动尚未接线，candidate只能先验证合成合同；真实candidate门等待TP-03/04/05和精确root-approved capabilities。TLS严格CA/client证书与NoCheck revocation限定保持，O TLS继续关闭。

## 10. 关键复审输入SHA

- Program：`3b4f9d5fb2b589bfe89a2ad67f518174f3317bc7fdc8a8515e32d54e60c388ad`
- runner：`fa951c926ddfe3e23af24b2cee7db4fcc5b0a20068ce6f75de6deee4de10d46c`
- validator：`022fb47a96137cb57d4ec7afd4756d248e05a5b0685d0b5400c66530aa371f28`
- contracts：`f8ad9abddd86e415374b9594954f2264fc5d701e3f603c7a96ed77e14dbca6cb`
- test_runner：`b77da1615702a91ce124d34e11359c3018944dc016e09cf68b741075aba18ce3`
- test_validator：`072f4196271f76c70eb590d3a6e24ef384a45d9ac2e1ca989b9839d7b140d775`
- handwritten spec：`b419accbbd9ec0c1e323ad4316837a29a756b691d1c336f151166d90adc6aa03`
- identities：`9f18cdfee3b8bb4fba7f08477514ecc68ba47b205c0d9b1c3d8ba5ae5248063d`
- README：`8fffcb41c8083d73399d912fd4179c0bbf0d57faf892085b77800b14b926b28e`
- csproj/global：与v1原hash一致，分别 `64519deb5495bfddb86b1d35d4c77a16d46ac2f79cd9e776d1c16b42585035ed`、`6863ab1b69640d64946ef783866504ca8dfda9ea05d0c33d013610b64996c7df`。

本报告完成后停止写入；没有将静态检查或手工positive factory等同于C#、DB或profile已通过。
