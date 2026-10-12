# TP07 EF 时间精度验收与最小映射修正设计

2026-10-08。状态：**design_prepared / implementation_not_activated**。依据 [v2计划](../temporal-dotnet-precision-plan.v2.md) §4/6/9 与 [TP00 EF交接](TP00-astra-design.md) §9。本次仅通过 git object 读取固定 EF 源码及既有 W 下游工具，未修改 EF 工作区、切分支、运行 dotnet/DB 或产包。TP06 最终候选身份尚待交付，本文不预填包版本或验收结果。

## 1. 固定输入、隔离与交付范围

EF 源为 `5863d3755dbd2d333ac359bf1c0a789762adbaed`，tree `b8f99168225a3742675ae594e8735f481a591fd5`。本次只读 `git archive --format=tar <pin>` 的 SHA256 为 `668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8`。原 `/Users/wanghongyi/Projects/dameng-entityframework-core` 有用户未提交工作，只读其 object database，不读取工作树版本冒充 pin，不 stash/reset/checkout/build，不把其 bin/obj 或 secrets 拷入验收。

激活后优先使用新 `.local/temporal-native/TP07/<revision>/`：保存只读 base archive，分别解出源码快照、High overlay 工作目录和 Low 重建目录。manifest 记录 before/after 全部 add/change/delete/hash、patch与锁文件、工具身份；在另一新目录从 base+overlay 重放，逐文件一致后冻结。需要永久 EF 提交时，另在新路径建立该 pin 的 detached worktree/独立分支，由 root 指派独立 EF owner，仍不触原 dirty checkout。

固定 pin 的 `global.json` 要求 SDK `10.0.401 / latestPatch`；中央 EF 包范围 `[10.0.12,11.0.0)`，实际版本以该 pin 各项目 lock 为准，不能借 restore 升级。W 的 SDK10.0.203 是另一门。只允许在隔离 copy 把官方驱动依赖改为 TP06 **精确版本**的 W PackageReference；先独立 lock 变化，再 locked restore。禁止 W ProjectReference、重新打一个不同 W 包或引用旧 TP03/R3 包凑齐 C 门。

W 仓库本次 PR 只承载 TP07 工具、可重放 bridge/temporal overlay、合同测试输入、脱敏证据及兼容/迁移交接文档。EF 永久产品改动必须成为独立 EF 提交/PR，列明 base pin、patch SHA、验证候选，并按 EF 仓库单独 review/merge；W PR 接受不等于 EF 集成已发布。此前另行规划的双驱动公开 API 不作为该 pin 已有能力，不在本任务顺带完成其架构。当前不发布 W/NuGet/EF 包，不改业务历史表。

已读 pin 的 `AGENTS.md`、`skills/dameng-sql/SKILL.md`、`skills/dameng-ef-migrations/SKILL.md`：保留中文文档、明确类型拒绝、DDL隐式提交、当前Schema精确清理、区分SQL单元与实库结果。那些 skill 的建用户/表空间示例不扩大本任务权限；本任务仅现有 WDM_PROVIDER_TEST，无 SA/admin/all launcher。

## 2. pin 中已确认的事实

以下路径相对 EF repo，均来自指定 commit，而非当前 checkout：

| 位置 | 当前行为与实施意义 |
| --- | --- |
| `Storage/Internal/DamengTypeMappingSource.cs` 默认项及 FindRawMapping | DateTime 为 TIMESTAMP(7)/DbType.DateTime2，DTO 为 DATETIME(7) WITH TIME ZONE/DbType.DateTimeOffset；模型 Precision7。TimeOnly 默认 TIME6，TimeSpan DAY9 SECOND6。新模型本来已有七位声明，不需全局改默认值 |
| 同文件 ParseStoreTypeName/ParseQualifiedTemporalStoreType | 显式 temporal store type/facet 解析已存在，支持时间戳0…9的metadata表示；不能把已有显式6/scaffold6覆盖成7，也不能把metadata可表示8/9当W写能力 |
| `DamengDateTimeOffsetTypeMapping.cs` | reader 方法是 GetString，随后 Invariant DateTimeOffset.Parse；未做非整tick检验。此链必须验证/修复无损性，不等于W typed DTO getter已经被EF调用 |
| `DamengTimeOnlyTypeMapping.cs` / `DamengTimeSpanTypeMapping.cs` | 均调用base.ConfigureParameter后设置Scale；未显式处理参数Precision。TimeSpan模型Precision是leading，Scale是fraction，现代码还用Math.Clamp设置Scale。必须用实际EF依赖创建W参数核验，不凭源码猜base行为 |
| `DamengRelationalConnection.cs`、项目依赖 | using Dm，CreateDbConnection直接new官方DmConnection；只给某个DbContext传W连接不能证明scaffold/CLI/所有服务都走W |
| `Scaffolding/Internal/DamengDatabaseModelFactory.cs` | 从ALL_TAB_COLS DATA_PRECISION/DATA_SCALE构造store type，普通时间scale0也显式保留；TZ、TLTZ与interval分别解释。Catalog DATA_SCALE不是ADO GetSchemaTable NumericScale或wire packed的别名 |
| `Migrations/Internal/DamengMigrationsSqlGenerator.cs` | 普通AlterColumn直接ALTER TABLE…MODIFY；无时间精度降低的数据前置检查。SQL生成成功不证明server无损 |
| `DamengTypeMappingTests.TemporalLiteralsPreserveTheSeventhFractionalSecondDigit` | 仅证明生成的TIMESTAMP literal字符串含七位，不能证明server解析/持久化保七位 |
| `DamengExtendedTypeMappingFunctionalTests` | 有DT/DTO七位往返与interval样例；其中官方DTO raw ADO证据专门断言GetValue为DateTime。W接受必须使用W的DTO合同，不能直接跑旧断言或删掉该观察门 |

## 3. 最小映射合同与必要变更

先由 High 编写离线合同，由 Low 在冻结后用 pin 的真实 EF mapping service 调 `mapping.CreateParameter(new W.Dm.DmCommand(), …)`，覆盖默认、HasPrecision、HasColumnType、clone、NULL。记录安全字段：mapping类型/store type/model Precision/Scale、参数实际类型/DbType/provider类型/Precision/Scale及明确声明状态；不记录连接串。新增用例必须对未修正基线暴露真实不匹配，不能用假的DbParameter替代这一步。

**时间 facet 转译**：DT/DTO的小数秒来自最终mapping Precision，转为参数Scale；TimeOnly同理但最高6；TimeSpan的mapping Precision是leading，仍留store type/列metadata，参数Scale取fraction。时间参数Precision必须未设或0。可在时间专用 ConfigureParameter 调base后显式设Precision0和所需Scale，但先验证模型facet，并完整保留模型Precision含义；这是mapper将模型合同转译成W参数合同，不是command interceptor抹掉任意用户错误Precision。W驱动的非零Precision拒绝及decimal映射均不改。

最小产品范围预计是新增 `DamengDateTimeTypeMapping.cs`，定点接入默认/显式DateTime mapping，及修改 DTO/TimeOnly/TimeSpan 四类时间配置；若 DATE/显式TimeSpan-as-TIME 的真实参数合同需要适配，先向root列出该额外mapping，不能漏掉已可达入口。DATE scale0，日期值不得丢时分秒；裸显式TIMESTAMP/DATETIME与显式(6)的既有语义单列，不能把“未写facet”无条件当模型默认7。显式类型/precision冲突保留EF已定义优先级或明确拒绝，不从值尾数推测。

DT继续DbType.DateTime2；DTO明确Scale7才要求七位，不依赖DTO参数兼容默认6。Scale0…6/7/8/9的写能力仍由W的真实fixed/profile门决定；模型可反向工程8/9不等于本轮开放新写。TimeOnly/interval合法范围先校验，不能用Math.Clamp掩盖非法值。clone保留所选mapping subclass、facet与类型意图；typed NULL同样有正确Scale并走能力门。TLS未证七位时，默认七位模型须明确拒绝，调用方显式六位模型才可进入支持路径，不由EF偷偷回退。

**DTO materialization**：W bridge优先令mapping选择W DmDataReader公开 typed GetDateTimeOffset（精确MethodInfo及EF表达式cast在离线materializer测试中验证），直接复用driver的ns→tick严格投影/原offset合同。官方默认mapping不因此改成W reader。若EF表达式约束要求保留GetString路线，必须单独审阅严格整数文本投影helper：先证明fraction0…9且ns整除100，再构造本地ticks+offset，不能先DateTimeOffset.Parse后检查已被处理的ticks，也不能仅以字符串位数>7拒绝可表示的尾零。二者只选一个W生产路径，避免一条严格、一条旁路。

DTO离线materializer至少接受`.000000100`/`.123456700`并保offset，拒绝`.000000001`/`.000000010`/`.123456789`；ordinary/TZ墙钟、合法offset与UTC范围、NULL、sync/async均核。无真实8/9服务器样本时只报合成Reader证据，不把它写成实库接受。TLTZ不能借该mapping变成普通TZ，非NULL继续关闭。

**literal与默认值**：DT/DTO的GenerateNonNullSqlLiteral必须与有效声明精度一致；六位值校验不允许截尾，七位Max/Max-1tick/Max-9ticks必须保原数字。若沿用基类先以实际生成literal单元对照；需要修复时只在新时间mapping重写，不全局改SQL generator。DDL DefaultValue、HasData、常量LINQ/内联谓词和DefaultValueSql分开验，NULL默认也单列。Scale8/9 metadata不能借literal旁路宣称开放W新写。

server是否把含七位的literal/CAST/默认表达式保七位，必须在TP07独立持久化后读取raw/FF9并比较；即使typed参数门通过也不能推出该门成功。若服务器literal路径仍丢第七位，该默认/种子/内联入口明确失败或提供经root批准且实测无损的限定表达式；禁止把原值改成整微秒让门通过。时钟函数默认值只按实际返回精度报告，不要求凭空产生七位。

## 4. 消费桥与候选身份

`tools/DownstreamAcceptance/t12_prepare.py`、T12 bridge及T19 guard/overlay范式可复用设计，旧文件、113014 archive、历史expectations和证据一律不回写。新TP07工具使用5863 pin、逐文件allowlist/anchor计数、单独patch ID；不能对新pin盲套旧combined patch或全树静默替换后宣称兼容。

桥的明确范围包括依赖/命名空间、RelationalConnection、scaffold connection、必要provider-specific parameter mapping、design-time/CLI与测试入口。保留W安全retry语义，不恢复官方按错误号任意重试；不在时间验收中扩大Savepoint、DataSource或公开双驱动API。原官方特定DTO返回形态等差异用显式W counterpart和保留的before合同说明，不删整个旧测试类掩盖失败。

每个testhost、audit、CLI子host必须核TP06同一个nupkg/version/packageSHA/DLLSHA/MVID；从实际加载assembly.Location留存身份，校验assets/deps中的PackageReference，driver ProjectReference、旧包、官方fallback均失败。另在真正EF服务拿到的connection/command/parameter/reader处证明其type/assembly来自W；仅typeof(W.Dm.DmConnection)被加载不够。Guard证据包括EF pin/overlay/assembly、实际EF依赖、source/lock、候选包tuple与进程级收据，日志不输出完整SQL参数值或认证信息。

## 5. 独立验收矩阵

| 门 | 必须证明 |
| --- | --- |
| EF-M01 mapping/clone/parameter | 上述facet矩阵含0/6/7/8/9与NULL，模型Precision保留、W参数Precision0/Scale正确；decimal正例原样；两个模型不因EF model cache混用table/precision/profile测试设置 |
| EF-M02 materializer/literal | DTO严格无损、getter真实调用与NULL；DT/DTO literal七位字符及显式六位拒绝超精度；旧文化/offset/DATE/TIME/interval合同不退化 |
| EF-R01 SaveChanges/query | shared经TP06接受的七位模型，DT与DTO min/min+1tick、max/max-1tick/max-9ticks、普通tail1…9；新context AsNoTracking从server读回，比较DateTime ticks与DTO本地ticks+offset，不只DTO Equals/UTC等价 |
| EF-R02 update/concurrency/rollback | 修改七位值并按原值做并发谓词；两context从同版本读取，一方更新1tick后另一方以旧token触发DbUpdateConcurrencyException，不能用int版本替代时间token；事务写入后rollback，新连接确认原值/rowcount，无DDL混入回滚证明 |
| EF-R03 six/default/null | 显式HasPrecision6与HasColumnType(6)、已有scaffold6精确微秒成功；非整微秒及typedNULL能力门独立；默认七位不重写现六位模型/snapshot；TLS六位成功及七位明确关闭，不能把拒绝写成server无七位能力 |
| EF-R04 defaults/literals | 分别验证DefaultValue七位常量、HasData、SQL默认/更新表达式与生成值回读。保存生成SQL/hash、受影响行、独立server oracle与fresh persisted行；失败不得由typed参数成功冲销 |
| EF-R05 scaffold/migration | 仅nonce TEST对象的0/6/7与可观察8/9列、TZ及intervalleading/fraction；目录→DatabaseModel→生成model/snapshot→新迁移SQL保持真实精度；显式6无升级漂移，默认值/索引/约束保留；TLTZ只metadata不授予值能力 |
| EF-R06 Up/Down | 专用表6→7 ALTER前后数据/默认/索引/约束精确保持，插入第七位后Down在任何破坏性DDL前阻断；整微秒控制组仅在guard与ALTER均证实时降回6 |

sync/async SaveChanges与materialization均有真实入口，参数化查询/同context值变化/NULL须观察本次binding；inline常量查询单独查literal路径。shared/TLS分别列适用case与关闭原因；默认7 on TLS不是可跳过的正例。若TP06已实测限制offset/历法域，EF不扩大；主极值正例必须在已接受域内，域外明确拒绝及rows0。

EF-R05不能以GetSchemaTable代替真实scaffold catalog查询。固定factory会枚举当前Schema序列，表过滤不一定屏蔽所有不支持的序列；如遇非本次对象导致拒绝，保留环境边界交root，不临时删业务对象/改factory忽略未知结构。新迁移只能应用生成内容中本次拥有的对象，不能把目录中其它表/序列一起重建。

## 6. Down 的失败关闭规则

pin 的通用AlterColumn不含数据检查，不能声称自动Down安全。默认TP07范围是**专用示例/验收迁移**：Down先执行经独立验证的精确preflight，发现任一非整微秒值即明确失败；先断言表仍scale7、值/索引/默认/历史记录均未改变，再测试整微秒控制。preflight不能用可能已舍入的CAST文本；优先原始字段或已证FF9整数尾数/精确服务端数值表达式，先用tail0…9及极值验证谓词。未完成SQL guard实测时，Down保持不可执行。

业务迁移须停止相关写入并核对象/数据范围，DDL隐式提交意味着应用层先查后改不天然原子；不能把测试中的独占窗口当在线并发安全证明。不给真实历史表自动加列/重写数据，也不将`.9999990`哨兵替成MaxValue。

**root 已裁决**：本任务按 v2 TP-A10 实施/验证受审 guarded sample Down，不新增 provider 对所有时间精度降低的统一禁令。guard必须先于任何DDL，使用独立验证的精确服务端表达式；CAST往返不足以成为guard依据。拒绝后以新连接证明行/schema不变，并在实际部署说明中明确要求写入方静止。通用generator当前无数据guard这一事实继续记录，不能用sample通过声称所有自动Down安全。

## 7. 文件归属、窗口与接受条件

High bridge/tool owner仅写新 `tools/DownstreamAcceptance/TP07/` 的prepare/run/validate/guard/patch清单；High EF owner仅写隔离source中上列时间mapping/source、新temporal unit/functional/materializer/schema/migration tests及必要design-time样例。精确建议文件范围如下，新文件名可在派发时一次冻结：

| owner | 文件范围 |
| --- | --- |
| W bridge/tool | `tools/DownstreamAcceptance/TP07/{prepare.py,run.py,validate.py,candidate_guard.cs,README.md}`，`patches/{driver-bridge.patch,temporal-mapping.patch,manifest.json}`，对应纯工具合同测试；不改T12/T19 |
| EF mapping（隔离copy） | `src/W.EntityFrameworkCore.Dameng/Storage/Internal/{DamengTypeMappingSource.cs,DamengDateTimeTypeMapping.cs,DamengDateTimeOffsetTypeMapping.cs,DamengTimeOnlyTypeMapping.cs,DamengTimeSpanTypeMapping.cs}`；必要共享时间facet helper须列入同一allowlist |
| EF新测试（隔离copy） | `test/W.EntityFrameworkCore.Dameng.Tests/{TP07TemporalMappingTests.cs,TP07TemporalMaterializerTests.cs,TP07TemporalSqlTests.cs}`；`test/W.EntityFrameworkCore.Dameng.FunctionalTests/{TP07TemporalRoundtripFunctionalTests.cs,TP07TemporalMigrationFunctionalTests.cs,TP07TemporalScaffoldingFunctionalTests.cs,TP07TestStore.cs}` |
| guarded sample/CLI | 隔离copy的新 `test/TP07TemporalConsumer/` 项目，context/factory及 `Migrations/` 中的专用6→7 Up/guarded Down/snapshot；样例源码以TP07模板/patch交付，不改原测试migration/history |
| bridge列明变更 | 隔离copy中各被消费csproj、Directory.Packages.props、对应locks、RelationalConnection、DatabaseModelFactory的driver引用、provider-specific mappings、必要retry适配和上述testhost/CLI guard注入。以实际pin逐文件记录；新增位置先回root，禁止宽泛补丁 |

两者必须先冻结bridge接口/allowlist/lock策略后分窗，不能同时改同一导出目录。现W产品、TP06工具和旧T12/T19保持冻结。scaffold/generator产品只有实证缺陷才另报root开窄窗；guarded sample本身不授权改通用generator。

root/Astra先审设计与代码，High停写→完整source/overlay/锁/case list冻结→Low独占构建/测试/DB；设置任务DOTNET_CLI_HOME及两项首启/遥测变量，单节点禁共享编译/build servers，锁定SDK分别执行。真实门以TEST wrapper加载唯一测试变量；禁止EF原`all/admin`入口读取另一secret。shared显式PlaintextAllowed；TLS沿已有RequireTls/CA策略，不关闭验证。

每case在CREATE前写nonce所有权ledger及fresh absence，先确认WDM_PROVIDER_TEST/同Schema/physical profile；迁移history表也独立命名并拥有，不能碰共享`__EFMigrationsHistory`。对象DDL逐个记录；finally只清本case拥有对象，独立新连接核最终absence与数据状态。超时按ledger恢复，未恢复不进入下一DB门。已有SOI只读能力不等于额外DDL权限，缺权限先报root，不加载SA。

验收报告须列固定case ID/预期类型/精度/错误/实际命令退出码/TRX计数，零skip才可接受已声明必跑集合；没有连接时integration_pending。TP06 A/B与TP07 C各自结论及同候选身份分开，不能用旧EF结果或SQL文本单元测试替代C。失败保留当轮source/包/DB证据并释放窗口，修复重新freeze；只有EF overlay变化也要重跑受影响EF门，若W包变化则重新安排所有依赖的同包门。

启动前root须交付TP06最终exact package tuple与profile证据、批准新bridge allowlist及预算/对象集合。DTO优先采用typed W reader路线；只有实际EF表达式接口阻塞才将严格文本替代方案回root裁决。Down范围已固定为专用guarded示例。信息缺失由owner回root，不向用户重复请求权限。本设计完成不代表任何源码实施或EF runtime门已通过。
