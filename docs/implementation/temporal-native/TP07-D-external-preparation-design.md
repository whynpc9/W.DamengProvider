# TP07 D：外部准备、原生依赖证明与分段构建设计

2026-10-09。状态：**bounded_design_ready / two_checkpoint_path_fields_require_narrow_repair / runtime_not_authorized**。只读D-v3十源、当前D工具接口、固定EF/L0源和锁、本地包/CLI归档及SDK targets；未运行SDK、restore、构建、测试、网络或DB，未修改实现、dirty EF checkout、全局缓存或旧证据。E当前真实运行尚未完成，本文不制造E接受记录，也不激活D实库。

基线D十源aggregate `631b607a3737779f594f7b7d1a3c289a50f70906d754b65de38e839621b56a5e`、manifest `d81d9d55…`已有限定静态接受。D tools v1四项Python修复仍需完成、新freeze和复核；本设计不将WIP代码当接受版本。下述两路径修复另需root授权最小C#/policy窗口，最终D十源和25工具包须在**首次D运行之前**一起重新冻结。

## 1. 已证实的checkpoint接口冲突及最小修复

`ScaffoldCases.ContinuationInputs`的父manifest保留字段循环，以及Python `design-validate.continuation`，允许 `source_manifest_sha256`/`lock_manifest_sha256`变化，却不允许对应 **source_manifest_path / lock_manifest_path**变化。另一方面，D `startup()`必须按这两个path/hash读取真实外部manifest。

首次生成6文件、再生成12文件后，source/lock集合发生变化，必须生成新的不可变manifest及新路径。当前组合会强迫沿用旧path并覆盖文件，或者拒绝合法新child；不能通过覆盖旧source/lock文件、latest symlink、改旧startup或借原路径伪装修复。

最小修复仅为：

1. C#与Python的**同一字段allowlist**同时加入上述两个path，继续允许它们配套的两个SHA更新；其它保留字段不扩大。
2. child的两个新ref均须完整path+SHA核验，document的replay_root仍为同一workflow root，source_pack仍为同一最终D25包，source/lock集合只增加本次批准generated subtree及其lock；所有原base source/lock/assets条目值保留。
3. 原parent startup、report、execution receipt、source manifest、lock manifest及其完整字节保持不可变；child另以parent_build_manifest_sha256/parent_report_sha256绑定。新外部build receipt引用新startup，不改旧receipt。
4. 加有限合同：正确新path+SHA正例；只换path不换hash、外来source pack/root、删改原base条目、未批准生成目录、覆盖旧manifest等负例。涉及C#文件仅ScaffoldCases.cs；工具owner更新对应policy immutable hash/新25-pack，不能让父checkpoint跨工具包拼接。

其余已检查的continuation字段没有发现第二个必须改C#的确定阻塞：generated consumer assembly本就允许更新，原D/L0 host保持；6→12只追加migration源码。外部artifact设计仍须避免新造冲突：不要在startup添加会随阶段变化、却不在上述有限mutable集合内的 `phase`、`build_receipt_sha256` 或整棵 `binary_hashes`。这些放入各阶段外部build receipt；startup仅保留当前接口必需字段。原host完整输出与历史generated DLL可另作不可变快照，不能为保留历史而覆盖旧manifest。

## 2. 两个独立replay与精确文件数

root已选择把必需的 `.config/dotnet-tools.json`纳入workflow `source_hashes`。本轮实读：已接受L0 prepared182及原base均没有该文件；不能暗放文件又坚持旧192计数。

| 闭包 | source数量 | 新增/实际active locks | active assets / host |
| --- | ---: | --- | --- |
| workflow准备 | **193** | EF175 + L0七模板 + D十源 + exact CLI配置1；包含原EF源集合原有lock文件 | 尚未生成新assets |
| workflow锁后D0 | **195** | 新L0 lock和D lock各1；active provider/L0/D共3 | 3 assets；TP07LiteralProbe、TP07TemporalDesign |
| generated G6锁后 | **202** | D0 195 +生成6源码 +generated lock1；active4 | 4 assets；加TP07GeneratedConsumer |
| generated G12锁后 | **208** | G6 202 +新增6 migration源码；generated lock应字节不变 | 4 assets；更新generated host身份 |
| protected cleanup准备 | **182** | 仅同mapping-v5 EF175 + L0七模板，无D源码/CLI配置 | 不依赖Design/CLI工作目录 |
| protected cleanup锁后 | **183** | 加L0 lock1；active provider/L0共2 | 2 assets；仅TP07LiteralProbe |

这里的source集合仍包含原EF测试等已批准lock文件；active locks只描述当前实际构建项目，不把所有source lock当成active。G6/G12数量是现有生成器按6/12文件与无依赖变化推导的合同；实际不同必须停止，不能删除生成文件或手改lock凑数。

workflow与cleanup用全新且互不包含的路径。例如一个外部任务根下两个兄弟目录 `workflow/replay`、`cleanup/replay`，manifest/日志写各自新的phase artifact目录。dirty EF checkout、mapping work、base-source、旧快照只读；copy按固定manifest逐文件，不复制旧bin/obj，不隐式glob当前工作树。

cleanup必须由其自身182/183 source/lock及**新真实build事件**形成 `TP07-D-cleanup-startup`，不是把旧L0已build收据晚加D标签。可消费已接受L0 native lock字节，但仍在cleanup独立root做locked restore/build并记录新DLL/MVID/deps。cleanup provider输出只需同overlay/W且自身一致，不要求因编译路径不同与workflow EF DLL字节相同。

## 3. Design依赖：33是静态候选，不是强制修图数字

现有49包native证明没有Microsoft.EntityFrameworkCore.Design。本轮从固定原base的
`test/W.EntityFrameworkCore.Dameng.Specification.Tests/packages.lock.json`
（SHA256 `685ecc8feabfd49aca9a1dd8cdb651564e3d30480d4e9b01c3cfae0ea77cc4b9`）以Design10.0.12为根递归读取完整net10.0依赖，得到**33个非驱动package**；它包含当前provider13，重叠项version/contentHash/dependencies均相同。

该原lock中Design是Specification测试包的Transitive依赖，仅把它当不可变图/内容来源。不要恢复整个Specification项目或将其其它测试依赖复制进D。实际D csproj直接引用Design10.0.12并有compile/runtime IncludeAssets，另有Microsoft.AspNetCore.App FrameworkReference，W仍固定候选。

相对已有49包证明，以下**20个新包**必须建立本轮Low原生证据：

| 包 | 版本 |
| --- | --- |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 |
| Microsoft.Extensions.DependencyModel | 10.0.12 |
| Microsoft.Build.Framework | 18.0.2 |
| Microsoft.CodeAnalysis.Common | 5.0.0 |
| Microsoft.CodeAnalysis.CSharp | 5.0.0 |
| Microsoft.CodeAnalysis.Workspaces.Common | 5.0.0 |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.0.0 |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.0.0 |
| Microsoft.CodeAnalysis.Analyzers | 3.11.0 |
| Microsoft.VisualStudio.SolutionPersistence | 1.0.52 |
| Humanizer.Core | 2.14.1 |
| Mono.TextTemplating | 3.0.0 |
| Newtonsoft.Json | 13.0.4 |
| System.CodeDom | 6.0.0 |
| System.Composition | 9.0.0 |
| System.Composition.AttributedModel | 9.0.0 |
| System.Composition.Convention | 9.0.0 |
| System.Composition.Hosting | 9.0.0 |
| System.Composition.Runtime | 9.0.0 |
| System.Composition.TypedParts | 9.0.0 |

本机只读发现这20包的nupkg、`.nupkg.metadata`及sha512 sidecar均在 `/Users/wanghongyi/.nuget/packages/<lower-id>/<version>/`，metadata contentHash与原锁匹配。Design nupkg SHA256为 `10bc05d1d7b79bb88ee5165904c244ca3ce3827ecfcacc0574f98c150baa93ef`。**缓存存在/metadata吻合不等于本轮native restore已验收**；其余19包的原archive SHA256、原lock record、nuspec、sidecar和metadata应由Low落入精确依赖输入清单，不以本报告的文字列表替代包证明。

Low应执行的有限证明：

1. 从上述原锁提取Design有向闭包，与mapping-v5 provider13合并并检查冲突；先冻结每个package的id/version/原dependencies/contentHash、nupkg SHA256及原来源。只复制必要33包和固定W到本task feed，不复制或修改全局缓存。
2. 新workflow专用NuGet cache开始为空；使用local-only baseline/candidate两个feed、clear packageSources及精确packageSourceMapping。W feed只放已接受nupkg；其它feed放所选原始archive。不得手写 `.nupkg.metadata`、替换NuGet contentHash算法或用raw archive SHA512猜native contentHash。
3. 对真实新L0/D csproj执行SDK10.0.401的native force-evaluate/use-lock-file restore。比较**实际生成的**lock resolved/dependencies/contentHash、新cache metadata/sidecar与固定原lock记录；归档zip/nuspec、feed/cache archive SHA256三方相等。保留原命令、退出码与fresh metadata hash。任何新增包、版本/hash/依赖差异先保存actual graph停，不强压成13、33或49。
4. 静态预测D图为33非驱动+W+provider Project（约35条），L0仍13+W+provider Project；generated图另有D Project和provider Project（约36条）。Project项大小写、数量、实际依赖按native结果记录，要求路径/来源真实，不把E的单Project guard照搬到generated。generated显式Design依赖仍需要自身PackageReference，因为D的Design PrivateAssets=all。

若20包某归档缺失/不可信，保留dependency-proof pending，先回root决定精确下载窗口；本文不自行下载。不得清空全局NuGet缓存。Microsoft.AspNetCore.App是FrameworkReference，不伪造NuGet包；本机只读目录可见Microsoft.NETCore.App及AspNetCore.App均10.0.12，Low仍须记录实际host/runtime/targeting pack来源与版本，不能只凭目录名认定加载成功。

## 4. CLI bundle单独固定，不依赖可变CLI home缓存

workflow `.config/dotnet-tools.json`只声明dotnet-ef10.0.12、commands=[dotnet-ef]、rollForward=false，完整字节在首次193源freeze固定。它是CLI声明；执行仍用绝对host `exec <tool_root>/dotnet-ef.dll`，不调用local/global tool resolver。

dotnet-ef nupkg本机只读实证SHA256 `5a671dbb0a6a31a0e01ee6157a7d5a11c7de1323d2fcc43c849f4f99064ca247`。外部Low复制到task_root中的只读package目录；tool_root与可写cli_home是task_root下的**兄弟目录**，不把tool文件放到CLI home的resolver缓存。按既有D policy六项精确hash提取：DotnetToolSettings.xml、dotnet-ef.dll/deps/runtimeconfig及嵌套tools/net8.0/any/ef.dll/runtimeconfig。zip entry从原 `tools/net8.0/any/`前缀截取，保留嵌套路径，不把两层net8目录拍平。

`TP07-D-cli-tool` schema1保持现有字段：replay_root、task_root、cli_home、tool_root、dotnet_host{path,sha256}、local_tool_manifest{path,sha256}、package{path,sha256}、tool_files、可选preparation_cache_files。最后一项默认空集合，不把CLI运行后可能变化的home缓存当长期输入。包nuspec、tool settings入口、六文件zip/落盘字节都核验。

Low可在offline窗口执行绝对host `--version`、`--list-runtimes`及 `exec <tool_root>/dotnet-ef.dll --version`，记录实际版本/exit/log；不能调用scaffold或migrations来“验证安装”，因为它们属于后续runtime。固定官方net8工具runtimeconfig保持原样，实际runtime roll-forward由上述真实调用取证，不改其配置。

之后D runner的PATH必须让官方CLI后代进程解析同一批准dotnet host；DOTNET_ROOT同host根、DOTNET_CLI_HOME为批准task home。cleanup decoupling按DTOOLS-B04修复：清理只核可信manifest的host pin/必要task home与自身startup/build/candidate/ledger，不能因workflow的dotnet-ef文件或local-tool配置损坏而不尝试独立cleanup；main完整CLI校验不放宽。

## 5. 实际外部build顺序与命令形状

所有restore/build前设置task可写DOTNET_CLI_HOME、skip-first-time/telemetry flags和隔离NUGET_PACKAGES/HTTP cache。恢复/构建统一 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`，restore另加disable-parallel，全部绝对host；不传TP07ProviderProject覆盖，不使用TEST wrapper、secret或执行startup DLL。

有限阶段建议如下，每阶段使用新日志/manifest目录；不是通用工作流引擎：

- **P0/source + feed/tool准备**：只复制/核193 workflow源和182 cleanup源、archive/feed与CLI6文件；输出source/依赖输入/tool manifests。此步可在E pending时静态准备，但不能填写runtime model-gate accepted。
- **N0/workflow native锁**：SDK查询 + `restore test/TP07LiteralProbe/… --force-evaluate --use-lock-file` + `restore test/TP07TemporalDesign/… --force-evaluate --use-lock-file`，三条真实SDK事件。冻结195源、原生33包证明及两个新lock；provider lock不变。root回读锁图后才能进入build。
- **B0/workflow base构建**：新root从195锁后源复制，顺序SDK查询、L0 locked restore/build、D locked restore/build，共五条SDK事件。实际resolved ProjectReferences与provider project路径一致，W/EF版本/assembly逐一核验；两个host输出中的provider DLL/hash/MVID须与同根provider输出一致。输出D0 startup。
- **BC/protected cleanup构建**：另一个独立root的182源加已接受L0 lock形成183；SDK查询、L0 locked restore、L0 build三条实际事件。输出独立cleanup source/lock/startup/build；不使用Design项目、CLI配置、workflow obj/bin或其build成功旗。它应在任何D runtime前完成，并保持整个workflow期间不变。

生成阶段必须在**原disposable workflow replay root**继续，不能换root，否则C#与Python的continuation都拒绝。每次先冻结parent report/ledger/generated snapshot及父binary快照，锁当前拥有的对象/窗口，再授权单一Low编译；不覆盖旧manifest。

- **G6/build1**：scaffold stage真实产生4个scaffold C# + csproj + DesignStartup，共6文件，parent状态generated_pending_external_build且还未清理。Low读parent report/hash并核目录 `test/TP07Generated/<nonce>`。仅为这个project首次native生成lock（图预计无新包），冻结202；随后该project locked restore/build，生成TP07GeneratedConsumer。base195/source/三lock/三assets、D/L0 host/provider身份必须保持。新增source/lock manifests使用新path/hash，child startup带parent SHA及6文件generated_closure。
- **G12/build2**：scaffold-consume在实际生成模型执行六位微秒/NULL门后产生两套migration，每套3文件；完整generated closure变12。Low冻结父snapshot_pending_external_build report、12文件和旧consumer二进制快照，新增source集合为208。依赖未变，generated lock必须保留；只做locked restore/build，不再force-evaluate或偷偷变包。更新generated consumer身份和12文件closure，其它host/source/lock/assets保持。

建议generated build显式 `/p:BuildProjectReferences=false`，防止已冻结D/L0/provider被递归重建；本机SDK10.0.401 `Microsoft.Common.CurrentVersion.targets`的ResolveProjectReferences路径确有false时GetTargetPath分支（:2135附近），本轮仅只读核对。Low仍须验证真实resolved reference/output与原host字节不变，不能把该flag当完整证据。restore如改变任一base lock/assets也停止，不修改manifest掩盖。

scaffold-finalize真实消费第二次编译的generated assembly并终结清理；guarded为另一个明确stage按既定模型门运行。两者没有外部build隐藏在C#或runner内部。每个profile使用自己可追溯的disposable workflow与checkpoint链；不得把shared生成文件拼接到TLS接受。

## 6. artifact schema：只补当前接口需要的证明

所有引用使用完整绝对path+SHA，父/子文件各自不可变。可直接使用三个有限外部准备模式（source、lock、build）加两次generated build命令，不另建通用协调系统。

| artifact | 必需字段/语义 |
| --- | --- |
| D25 source pack | 当前工具既定schema1/task=`TP07-D source pack`/25项source_hashes；必须在四项工具修复及两路径修复后冻结，不跨checkpoint换包 |
| 每phase source manifest | schema1、source_pack、replay_root、精确source_hashes、lineage/W、来源refs；明确193/195/202/208或cleanup182/183的角色，不用同一个文件更新内容 |
| 每phase lock manifest | schema1、source_pack、replay_root、source parent ref、精确locks/source_hashes/native package proof refs；实际graph和档案哈希，不只计数 |
| workflow startup | schema1/task=`TP07-D-startup`、replay_root、source_pack、lineage/W、ef_provider{name,dll_sha256,mvid}、source_manifest_path/sha256、lock_manifest_path/sha256、source_hashes/locks/assets、startup_assemblies；base有D/L0，generated后加consumer，各row含name、相对path、DLL/MVID/deps SHA |
| cleanup startup | 同上但task=`TP07-D-cleanup-startup`、独立root且仅L0 host；source_pack同本轮D25，source字节来自独立L0闭包，不能依赖workflow生成文件 |
| 外部build receipt | schema1/task=`TP07-D-external-build`、source_pack、startup_manifest{path,sha256}、SDK_version10.0.401、database_accessed=false、locked_restore=true、真实commands及日志path/SHA/exit/timeout/termination；可附独立binary/源锁前后清单，不把晚写receipt当新build事件 |
| continuation child startup追加 | parent_build_manifest_sha256、parent_report_sha256、generated_closure{directory,project,source_hashes}；6或12文件精确匹配parent实际生成；只允许约定source/locks/assets/consumer身份及source/lock path+SHA改变 |
| CLI manifest | 上节固定tool schema；workflow配置文件是193源的一项，包/工具目录在task_root外部有独立hash引用 |

完整输出目录binary hash集合建议放build receipt，不放continuation会要求原值不变的startup顶层。必须保留每次真实generated assembly的bytes/MVID/deps快照；下一次编译替换同路径consumer是批准的唯一host更新，不能因此改写上一次接收报告中的hash。

`source_manifest_path/lock_manifest_path`窄修后的外部验证helper需在交接前核：新文档自身hash、当前root、同source_pack、精确源集合与锁、父manifest/report未变，且原base字节全部仍匹配。当前D `startup()`只按现有字段消费这些ref，外部producer不能用内容随意的manifest仅满足“root/source_pack相同”。把这些检查限定为本轮四种闭包，不引入通用schema兼容fallback。

## 7. 18字段执行plan与实库激活独立

现有 `TP07-D-execution-plan`的18字段保持：schema_version、task、stage、profile、source_pack、startup_manifest、external_build_receipt、cli_tool_manifest、model_gate_manifest、helper_ast_proof、candidate_manifest、capabilities、original_repo、wrapper、python、parent_checkpoint、cleanup_startup_manifest、cleanup_build_receipt。

stage仅scaffold/scaffold-consume/scaffold-finalize/guarded/cleanup；两个续阶段的parent_checkpoint为真实已接受pending report/execution receipt引用，不能synthetic生成。helper_ast_proof沿当前批准的pure16 AST证明（artifact SHA `88424da2…`、AST SHA `8e254c98…`）及最终工具包hash；candidate/capability/W tuple固定，wrapper必须原TEST路径，PATH绑定批准host。

**model_gate_manifest现在不能填accepted。** L0/E必须各profile真实接受、同mapping-v5 overlay与同W，并引用实际证据；TLS另有同候选E/TP06七位关闭边界。当前E真实运行未完成，即使D源码、33包native proof、CLI或所有offline build都通过，D仍不得进入scaffold/guarded DB。cleanup的必要性不授权新建fixture；其权限只限已有ledger精确对象与既有ACK/marker条件。

每个pending checkpoint显式保留本次owned对象，继续必须root批准child source/build/plan；若放弃或后续source/CLI/build失败，独立protected cleanup仍须可运行。DTOOLS-B04修复只隔离非cleanup必需的CLI依赖，不能放宽cleanup本身的host/startup/source/candidate/ledger可信度。

## 8. 下一可实施项与停止边界

先完成现有四项D Python修复，另由root授权**ScaffoldCases两path保留字段 + Python相同allowlist/验证 +对应policy pin/合同**，新十源/25包静态复核；再由High提交有限外部prepare/lock/build脚本和schema给Astra/root审阅。Low可以在独占offline窗口建立20新包native证明与两root构建；不碰外部dirty EF checkout，不运行原all/admin launcher，不复制secrets，不重打W包。

任一包图、source count、旧资产、CLI文件、parent ref或cleanup闭包失败都保存并停，不临时换SDK、忽略hash、改generated模型、手补HasPrecision或借旧build。最终source/lock/build/CLI准备只是D接线前置，不能替代E门、R05真实CLI生成/六位模型消费、R06真实guard/Up/Down和完整C。本文只交付具体可审工作流及一个确定接口阻塞，不生成任何runtime批准。
