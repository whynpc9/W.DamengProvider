# TP-01 Astra 冻结工具审阅

日期：2026-10-08。状态：`changes_requested / build_and_database_not_run`。本轮只读审阅五个 `tools/TemporalBoundaryProbe` 输入及两个 `eng/temporal-precision.*` 脚本，不改产品、probe、历史证据或其他报告。TP-02有独立验证窗口，其状态不作为本工具的运行证据。

结论：探针的TEST前置、显式传输策略、O/W包内资产与实际加载DLL核对、nonce表及新连接读回方向可用。但当前候选validator有多处“存在或任意失败即算完成”的缺口，source与package也只有同commit并列记录，不能据此宣布candidate_profile_accepted。需要High完成下列限定修复和纯Python负向合同测试，然后重新静态审阅及冻结，才交Low构建/DB。

## 1. 范围与输入指纹

root提供7输入canonical JSON aggregate：`26176e482657d28107ba9041e03584373c82b531da3facfe1da5b75917b9dc2d`。本审阅独立回读以下单文件SHA，aggregate序列化规则留Low按manifest复算。

| 文件 | SHA256 |
| --- | --- |
| tools/TemporalBoundaryProbe/Program.cs | `8c7d3bfe0a82c83f1e747a64b7db75dea9ea70eefe5859d102f880ef61d91b62` |
| tools/TemporalBoundaryProbe/README.md | `0f2ee776996b462034104a285f6a98bd87393a3899818463407050885d027c8a` |
| tools/TemporalBoundaryProbe/TemporalBoundaryProbe.csproj | `64519deb5495bfddb86b1d35d4c77a16d46ac2f79cd9e776d1c16b42585035ed` |
| tools/TemporalBoundaryProbe/global.json | `6863ab1b69640d64946ef783866504ca8dfda9ea05d0c33d013610b64996c7df` |
| tools/TemporalBoundaryProbe/identities.json | `5e73e106a829028a5ec1522774f89e353beb078272689a53395114141522668d` |
| eng/temporal-precision.py | `40126478308f8ffe708dd3b32a002d30e2585554a1d6c36ba87ff67245f94cff` |
| eng/temporal-precision.validate.py | `90d9bbc4f653421b0786fb5102267b82342df5e755be3638969bb613ba60d1fb` |

## 2. TP01-R01：source 与实际候选包没有形成绑定

依据：runner `:112–120` 仅检查source_manifest的commit与identity一致，且 `files` 或 `source_files` 非空；capability `:93` 同样只绑定commit与package SHA。一个只含非空计数 `source_files: 1` 的manifest也能满足检查。validator `:32–41,49–50` 只证明这份任意manifest在本次运行中未变，没有证明加载包来自该源码内容。同HEAD下不同未提交实现可交换，违反TP00同源同包要求。

修复范围：runner、validator、identity输入schema与README；如需在C# report中记录producer identity，可窄改Program。必须采用root/Low已冻结的producer来源记录，把**精确product-source-manifest SHA（及存在时source archive SHA）→唯一package版本/SHA→包内DLL SHA/MVID**绑定起来。candidate capabilities同时绑定这份source-manifest SHA，不能只绑定commit。明确校验manifest结构；`source_files`数目不能冒充源文件digest。

archived_v23必须引用实际历史producer source manifest及其包关系；fe89bd4可以是本轮审阅/比较基线，不可把晚于产包的合并HEAD自动当作原包producer identity。rebuilt baseline用新唯一版本和本次producer记录。原v23包/DLL固定SHA不改，原历史source内容不改。

负向合同：保持HEAD/package不变、替换source文件hash或source archive；只提供同HEAD+非空count；producer记录指向另一包或DLL；capabilities引用另一source hash，均拒绝。正确的旧归档链和新candidate链分别有独立正例。

## 3. TP01-R02：候选核心边界用例只要求存在

依据：validator `:129–130` 只确认Kind三用例key存在；`:145–147` 只要求local/UTC endpoint写入或constructor key之一存在。Program `Attempt:524–529` 会把失败保存为 `status=failed`，这些case依然可以让candidate通过；错误ticks/offset、oracle失败也未被该部分拒绝。`require_exact:240–251` 对主要用例已有数值规则，但未检查DateTime Kind、GetString、metadata/schema与`subsequent_read_completion`。

修复范围：validator为主，Program补充确切expected/分类字段。Kind三用例按profile七位开放/关闭分别必须精确成功或NotSupported；写入不因Kind改变墙钟，W读回Kind=Unspecified。local/UTC构造器前置拒绝必须与静态可构造性匹配，且不得拿不相关constructor case替代真实可构造输入。构造成功后的写入需要明确分类：已批准域内精确成功；profile已知不可表示域按已批准错误拒绝且fresh persisted_rows=0；未知服务端域保留明确未接受分类，不能以任意DmException当范围证据。

expected也必须来自validator独立冻结case表/逻辑向量，而不是完全相信同一report的expected：当前仅actual与record.expected比较，若两者同时漂移仍可过门。Min/Max及邻接、ordinary、末位0…9、Kind与offset/endpoint需按case ID核对规范输入，server文本/分量也应与该规范值对账。

oracle核对按getter原契约检查实际类型、ticks、offset/Kind；W GetString必须与独立server实际值一致，后续Read必须正常结束。O typed getter已知失败可以继续表征，不能作为W正确性的宽松标准。

负向合同：删除Kind/endpoint case；把其status改failed、ticks加1、offset换0、Kind换Local、server_exact改false、W GetString错误或subsequent_read_completion=false；同时把expected和actual都偏移一tick并伪造server_exact=true；把可构造endpoint换成伪constructor拒绝。candidate均拒绝。baseline保留官方边界失败的分类能力，不被迫满足新W行为。

## 4. TP01-R03：metadata/NULL/双精度矩阵没有严格接受规则

依据：validator `:166–190` 对大多数合法scale/default/fixed/NULL仅允许 `observed` 或 `driver_parameter_rejected` 两种状态，没有对照v2 §5稳定选择；只有19/23/255和部分非零Precision负例较严格。Program `:429–434` 生成的 `dual_scale6_*` 无任何validator断言。`TailStability` 记录的scale6 reprepare和值未被validator核对；second_value仅检查ticks尾数7而非完整expected。fixed case更换Value后没有再次执行，reprepare之后fixed分支也没有执行（`:418–423`），因此不能证明重绑定生效。

修复范围：Program、validator、README。以明确expected表驱动接受，至少落实：

- advisory默认来源：inferred DT/DTO按profile稳定26/27+7或16/23+6；显式DateTime/provider DateTime与显式DTO/provider DTO无Scale保留16/23+6；显式DateTime2默认在七位未开放profile拒绝。不能仅比较前后CType相同，因为“两次都选错”也会通过。
- Scale0/3/6的当前输入是否整quantum、7是否开放、8/9是否单独批准、非法19/23/255按v2对应精确结果/异常判定；TLS七位关闭对advisory、typed NULL等路径也必须NotSupported，即使值为整微秒。当前仅core部分有这个门，不足以覆盖metadata路径。
- NULL区分显式类型NULL、可靠fixed describe解决的NULL与无类型advisory NULL。后者预期拒绝不能被泛称typed-null正例；添加字段标明type source。显式非零Precision在fixed/advisory及typed NULL均验证，0/未设不得污染wire width。
- fixed列CType/scale保持真实来源；explicit user scale与column scale分别记录/核对。actual legacy16/23六位列+请求7必须NotSupported，不能把“值是整微秒”误作开放依据；extended26/27但列scale6的双约束用profile可建立路径时表征，无法建立的布局留后续纯绑定fixture，不伪造实库能力。
- fixed替值使用新的row ID或UPDATE避免PK冲突，并真正执行；reprepare后同样执行并fresh读回。核完整expected值而不是只核尾数；请求metadata变化、有效metadata变化与原raw snapshot分开记录。

README的“Precision here means wire width”须改为：`requested_precision`为公开DmParameter.Precision，`observed width`才是协议宽度；当前代码已有分键，不要在报告解释中混同。

负向合同：所有合法metadata case改成拒绝仍应失败；inferred/default选错CType、Scale或width；first/second值不同但尾数仍7；删dual-scale或reprepare结果；精度0污染width；fixed列被explicitScale覆盖；无类型NULL伪装typed；TLS关闭时advisory scale7成功写出，均拒绝。各允许/拒绝格有正例，不能用“所有拒绝”作为正确实现。

## 5. TP01-R04：TLTZ与8/9位接受遗漏

依据：validator `:222–224` 只要求TLTZ DDL记录存在和cross-session capability_open=false；它不验证已有TLTZ的metadata或任何W写/读拒绝。Program `:478–485`虽收集metadata和写入，但读数据仅在写入成功后才有，若未证W写被拒绝则没有独立种子验证读侧拒绝。候选可错误放行TLTZ并按普通timestamp解码而仍通过。

高精度方面：validator `:194` 整段只在DDL成功时执行，因此即使root明确批准scale8/9，DDL失败也能跳过该承诺；`:210` 只有server_preserves_requested=true才核W读，CAST已改变原请求但返回另一个合法值时，实际合法GetString通道完全没有接受规则。`:196`对higher-scale metadata只有key存在，schema scale/type错误仍可通过。

修复范围：Program、validator、capability schema/README。普通TS七位和TLTZ不是同一开关，TLTZ在本轮无独立证据时明确关闭；W写端NotSupported且不持久化，读端通过O或独立server种子构造存在的TLTZ值后验证所有适用公共读入口NotSupported，不能因为W不能写就不测W读。若服务器不存在该类型，保留verified server DDL分类；未知错误不得冒充不支持。TLTZ扩展支持需另有root批准的类型/格式/精度证据，不能继承seven_write。

8/9位layout、实际读取与写能力分开：approved write scale必须真的建立相应列并完成精确写入；未批准写为NotSupported。合法server表达式只要成功求值得到可观测实际值，就应以**实际server字段/文本**验证W读取；`server_preserves_requested`只说明CAST是否保留请求，不是跳过读门的开关。实际ns非整tick要求精确GetString+CLR Overflow，整tick要求精确CLR；列metadata/schema必须真实反映CType与8/9，不用输入scale代填。不得借本修复对既有合法读通道加全局server版本拒绝。

负向合同：删除TLTZ读或写、未证TLTZ成功、mask/raw标志错误、将普通TS能力当TLTZ证据；批准8位但DDL失败；CAST返回不同合法值后W GetString错误；schema改成6；非整tick被CLR截断，均拒绝。baseline官方/旧W行为仍作为表征保留，与candidate门分开。

## 6. TP01-R05：基础设施失败与server/driver能力分类不足

依据：Program `WriteCase:287`将parameter/prepare/execute中的所有异常均标driver_write_rejected；`Attempt:528`吞掉所有异常并继续。ServerExpression/DDL的 `server` lane仍由W连接执行，Open身份/profile失败、timeout/transport、本地配置异常都会变为failed case。baseline validator对主要拒绝只要求零row+任意error type，extended部分甚至只查case存在；README宣称基础设施失败停止，但并未实现统一分类。

修复范围：Program、runner、validator。至少分出CLR构造前置、用户metadata/值拒绝、经验证server响应、未验证driver异常、连接/身份/profile/timeout/transport等infra失败。每次Open身份与schema/version不符必须终止lane并精确cleanup，不作为时间能力结论。服务器“不支持”只由真正server响应或独立可核验结果表征，不能由O写失败、W写失败或网络失败推断。

对W可记录现有结构化FailureInfo的安全code/phase/outcome或verified-response标志；不输出原异常message、SQL、connection或认证报文。O无法确认错误来源时标unverified而非server_unsupported。baseline可完成驱动行为的失败观察，但必要profile门尚未知时必须明确pending/unverified，不能给出无条件profile能力通过。

负向合同：把DDL/TLTZ/max-boundary case换成Timeout、错误TEST身份、版本不符、网络错误，不能仍接受candidate或声明server不支持；官方typed拒绝但独立server值可表示时仍保留该两种不同事实。

## 7. TP01-R06：清理与停止条件应闭合

已具备的安全点：每次连接先检查configured TEST及实际USER/schema/version/encoding；table名只来自32hex nonce的固定前缀；Reserve检查absent、CreateNew manifest在CREATE前落盘；cleanup从manifest验证精确名字/profile/package/identity，使用普通TEST且新连接确认absent。TLS限制本机127.0.0.1:15236，严格CA/client cert验证并仅显式NoCheck revocation，没有关闭证书校验；O TLS保持关闭。这些不能削弱。

明确流程缺口：runner `:169–182`记录独立cleanup_exit_code，但是否进入下一lane只看主run的code。如果主run返回0而cleanup进程非零/超时，runner仍继续下一个DB lane并最后写execution_complete。validator随后会拒绝，但停止点已经过晚。修复为任一主run或独立cleanup失败/硬deadline都立即停止并保存精确恢复清单；cleanup应在受控finally中执行，避免正常异常分支跳过已有ownership恢复。cleanup本身的失败不能覆盖主失败。

还需落实pre-existing保护的边界：Program在`:176`先Reserved=true，`:178`才CREATE；若CREATE返回明确“对象已存在”等可证明未由本次创建的失败，outer catch会按Reserved对同名表DROP。虽然nonce正常碰撞极低，不能将“检查时不存在”升级为“任何后续同名对象都属于本次”。请增加创建生命周期/所有权确认；已知创建失败不删除非本次对象，创建ACK丢失时用预先约定可核实的owner标识/结构核实，无法确认则保留恢复清单并明确未清理，禁止猜测DROP。

无需跨schema查询、SA或实例配置。负向合同用fake DB/子进程adapter，不以真实业务表注入风险：建前已存在→无CREATE/无DROP；CREATE碰撞拒绝→不DROP；harddeadline在CREATE前/后/cleanup时分别恢复；错误nonce/profile/package/identity manifest无DROP；cleanup超时→不启动下一lane，保留主退出码与cleanup状态。以上必须先由Low离线执行。

## 8. TP01-R07：缺少实际row布局观测，不能直接支撑TP-03接线

依据：Program的Metadata只含CType/width/semantic scale/mask/typeFlag/rawScale（`:29–30,553–562`），没有结果格式及实际单元payload长度。现有旧 `DmDtDecodeFast` 会按8/9/12等长度兼容解码，nominal CType不足以确定返回字节布局；未来只按CType选择TP-02显式layout可能误拒合法结果。

修复范围：Program及报告schema/validator，限已取得的合成temporal单元缓存观测；记录CType、column isBdta/result format、actual cell payload length及必要安全hash/合成字段来源，不读取认证帧，不打开未支持BDTA选项。无法观察时显式unobservable，该路径不能被记为wire-layout verified。候选D01 raw snapshot可观察时也要记录并核对raw CType/precision/scale与derived mask，不能只记录RawScale却放弃原始来源一致性。

此项是TP-01→TP-03的接线门，不要求扩大TP-02纯明确layout核心，也不因此推翻TP-02已完成验证。旧W可保留raw metadata不可观测分类，不能用它给新绑定的来源保存门免检。

## 9. TP01-R08：strict artifact/capability schema需拒绝歧义输入

root独立补充的输入问题已核对：runner/validator直接`json.loads`，重复key会被最后一个值覆盖；validator的`set(write_scales)`接受bool作为整数1；`runner.profile`除shared外全部按TLS分支处理；Python的bool与0/1相等，可绕过schema_version、exit_code、计数等只做值比较的检查。严格接受器不能依赖producer“通常不会这样写”。

修复范围：两个Python脚本及新的合同测试；Program直接读取identity/ownership等权威JSON时同样拒绝重复key，必要时README补schema说明；不扩为一般安全扫描。所有验收JSON使用统一duplicate-key拒绝loader；schema_version、counts、exit codes、scale、ticks/offset及枚举逐字段验证类型/范围，数值必须`type(x) is int`等显式排除bool。profile仅shared/tls，phase及lane也必须有效并与runner/manifest一致。capability seven_write必须是真bool，write_scales无重复且每项真整数0…9，root_approved必须true且evidence/source/package绑定完整。

负向合同：重复root_approved或phase/expected字段、`write_scales=[True,6,7]`、未知profile、False冒充成功exit0、True冒充schema1、boolean ticks/计数、错误report lane/profile、缺失必要字段，均拒绝。报告中预期和实测同时漂移的mutant按R02独立case表拒绝。不要只给loader自身写测试，至少一组完整合法artifact经篡改后实际调用顶层validator并断言validation_failed。

## 10. High修复与Low步骤

High继续仅写这七工具文件；root已允许新增纯Python合同测试，路径限定 `eng/temporal-precision.*` 或 `tools/TemporalBoundaryProbe/`。不改驱动、Builder、TEST wrapper、secrets或旧TemporalPrecisionProbe。将runner/validator拆出可离线调用的纯检查与可mock执行边界，测试不启动dotnet或真实DB。

负向payload应从**独立手写的最小合法baseline/candidate资料**建立，不由同一个producer生成expected后只验证自洽。必须实际构造删字段/删case、wrongticks/type/metadata、failed status、假cleanup、错误source绑定等变体并断言拒绝。保留shared7开放正例、TLS6正例与TLS7关闭NotSupported正例；再把TLS7关闭结果改为成功，确认拒绝。baseline的旧W拒绝应可作为分类记录，不能被candidate validator接受。

修复后High停写，Astra/root复审新输入；Low独占按以下顺序执行，首失败保留并释放：

1. 新Python unittest全部正反合同，冻结7+n输入与producer/source/包身份记录；核SDK10.0.203。High不自跑这些验收。
2. probe离线restore/build，设置可写DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1，单节点/noReuse/noSharedCompilation/disable-build-servers；不在wrapper内build。
3. 先按原仓库wrapper跑新目录的baseline shared三lane，再运行严格characterization validator；身份/owned cleanup及最终fresh absence必须完整。TLS baseline独立目录、W+server oracle，O TLS始终关闭。
4. TP-03/04/05候选未形成前，candidate gate仅跑合成负向合同；不得用baseline包填写candidate通过。真实candidate必须加载root审批且绑定精确source/package的capability manifest，再分别执行shared7严格与TLS6严格/TLS7关闭门。

runner模板仍可用 `python3 eng/temporal-precision.py --feed <verified-feed> --identities <frozen-id> --source-manifest <exact-producer-source> --repo <original-repo> --profile shared --output <new-dir>`；candidate额外传root批准的capabilities。具体新合同测试入口待High交付后由Low确定，不能把本报告中的模板当实际执行结果。

本报告冻结后不再写入。当前TP-01不允许以源码存在、工具成功退出或case记录齐全代替严格profile接受。
