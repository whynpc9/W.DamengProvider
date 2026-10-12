# TP07 E：固定连续case分窗设计

2026-10-10。结论：**deterministic_25_window_design_ready / implementation_activation_blocked_by_v7_owned_state**。可只改harness编排，将338个既有case按固定15个完整case切成25窗，保留全部业务、来源、oracle和双重清理门。没有需要改变W、EF mapping或common OwnedObjects的设计阻塞；**当前不得实施**，先关闭E-v7精确owned状态。

本轮只读源码、policy与运行收据；用普通JSON读取列出分区，未执行SDK、自测、validator、DB或探测，未修改当前代码，仅新增本文。以下离线partition门是后续必须实际执行的要求，不是本轮通过声明。

## 动机与当前前置门

E-v7约683秒完成60个roundtrip业务case是协调者观察；当前落盘main SHA `e9492e14bac169f78a5571c31348248229c76dacad9c1a34a90d583de62fbea7`确有60个completed，但没有最终cleanup_verified。最终control summary SHA `30ca9602297c19dde0afb76ebd61d5a0d5092bf3202a08e8c6ee28f92f8b1f14`显示main900.027737秒、独立cleanup300.01955秒均deadline kill，cleanup report缺失、validator失败，1086项输入未变；window_released=true，但60对象ledger的最终服务器状态仍unresolved。故“进程窗口释放”已经发生，“owned状态关闭”尚未成立，两者不可合并。

分窗限制单进程累计case、report和待清理对象规模，为原900秒main（包含finally cleanup）及300秒独立cleanup留出空间；它不缩短或移除清理，不修复尚未知的等待根因，也不保证任意延迟下每窗都成功。任何新失败仍保持fail-stop。

## 唯一分区：15例上限、25个固定窗

profiles固定`shared,tls`；每profile组顺序保持当前runner的`roundtrip,six,mutation,generated`。组内沿原policy与C# registry的完整既有顺序，令N为原组数、window_index从0开始，start=15×index、count=min(15,N−start)，index必须小于ceil(N/15)。没有可配置大小、起止位置、case ID列表、筛选、skip、resume或只跑选定组的顶层入口。

| profile/group | 原case数 | 各窗case数 | 半开区间（组内0-based） |
| --- | ---: | --- | --- |
| shared/roundtrip | 60 | 15,15,15,15 | [0,15),[15,30),[30,45),[45,60) |
| shared/six | 54 | 15,15,15,9 | [0,15),[15,30),[30,45),[45,54) |
| shared/mutation | 12 | 12 | [0,12) |
| shared/generated | 54 | 15,15,15,9 | [0,15),[15,30),[30,45),[45,54) |
| tls/roundtrip | 52 | 15,15,15,7 | [0,15),[15,30),[30,45),[45,52) |
| tls/six | 56 | 15,15,15,11 | [0,15),[15,30),[30,45),[45,56) |
| tls/mutation | 12 | 12 | [0,12) |
| tls/generated | 38 | 15,15,8 | [0,15),[15,30),[30,38) |

shared13窗/180例，TLS12窗/158例，总25窗/338例。policy原338个case对象、required_counts和顺序应逐字节或规范化结构比较证明未变；只增加固定window_contract元数据，不能把全局registry替换成某个slice。

## case-local语义及唯一跨case依赖

Roundtrip的同context value→query→NULL reuse都位于单个Run内，不可拆步；mutation的seed、双tracked读、winner/stale、异常、fresh oracle或完整transaction/rollback均留在一个case；generated的DDL/default/HasData/inline/update-expression和DT/DTO双读同样不拆。每case通过Own.Register生成独立nonce表，独立Context/事务生命周期不跨case。Model cache key包含表及完整ModelSpec，窗口进程重启不改变语义。clock默认值只按当次实际值与精度验证，不跨case比较墙钟。

真正需要检查的跨case状态是`CommandAudit.AsyncWriteControls`：six异步拒绝调用`RefusalSendEvidence`，必须在**同进程/同report**已有真实成功EF异步写的发送控制。不能把它宣称完全case-local，也不能把前窗controls导入新窗。当前固定15边界恰好保留依赖：

- six [0,15)全sync，无async正控制需要。
- [15,30)的async段始于索引27 facet6/DT/micro，28为NULL正例，29才是submicro拒绝。
- [30,45)始于facet6/DTO/micro正例30，先于拒绝32及后面的store6/default7_fixed6实际分支。
- [45,54)或TLS[45,56)始于native DATE value45，先于DATE拒绝47；TLS最后advisory54/55仍有本窗native成功控制。

因此不必修改CommandAudit或SixPrecisionCases，不必添加额外DB正例。离线门须独立核上述“每个可能async拒绝前有同窗确定成功路线”的顺序属性，包含default7_fixed6条件分支与TLS advisory；runtime仍要求**实际**正控制成功/发送/prepare/publication/operation身份，而非仅用静态顺序代替。若未来registry变化破坏该属性，partition门失败回root，不自动改变边界或借旧窗证据。

audit attempt/sequence、artifactOrdinal、generated SQL序号会在每窗重置，这是局部标识；新validator只在所属窗内要求唯一，跨窗以run/window身份隔离，不能误把相同局部数字当跨窗同一操作。已有case内原始元数据、raw、固定/非固定分支、fresh server值和异常语义完全保留。

## 最小源码与policy调整

预计产品/测试改动严格五文件：

1. `runtime-templates/TP07TemporalRuntime/Program.cs`：新增固定window helper，仅从`RuntimeCases.Required(profile,group)`生成全部cases再计算slice；runtime必须有受runner传入的合法window index与run identity，缺失/越界/负数/无效文本或offline flag组合失败。先完成参数/窗口验证再构造guard/owned对象。主循环仍逐个完整调用既有case类，main finally cleanup和原异常语义不改。report增加全组count、固定window identity、完整expected slice定义；原`cases/required_cases/required_case_count`明确表示本窗，不能冒充全组。保留原28-model offline-generated-create路由并禁止混入window参数。
2. `ef-cases.json`：仅加入有限window_contract（version、size15、固定25个descriptor或可验证派生表）及新的Program template hash/E aggregate。原338cases/其它八E源、W/profile/oracle/版本合同保持。
3. `ef-run.py`：顶层仍只能执行全套，嵌套profile→group→固定windows；内部host接收有限window index，start/count/IDs从policy重新算，禁止caller任意提供范围。主窗口main、独立cleanup、validator串行，无并发、无自动重试；window index不是允许用户选择部分case完成全套的入口。
4. `ef-validate.py`：增`window_specs/validate_window`及有限group/final聚合；所有旧case合同继续调用。required_cases从固定全registry按index计算，不信producer的case list、start/count或expected成功旗。明确新schema不接受旧未分窗group作为新window。加强同窗artifact路径/命令/run身份、deadline及整个coverage检查；不放宽source/published/raw/cleanup/L0门。
5. `test_ef_contracts.py`：保留现有合同，增加partition、同窗控制前缀、receipt聚合/跨窗借证据与失败变异。不要为改测试方便改accepted common或L0。

`Models.cs`原registry、四case类、CommandAudit、TemporalContext、E csproj、common三源、LiteralOracle和原L0源均应不变。若High发现额外文件必须改，先回root列确切原因，不偷偷扩大范围。

## 每窗运行、ledger及receipts

全新run根目录由runner独占创建并生成run_id（不是凭据）。固定路径例如`<run>/<profile>/<group>/window-000/main|cleanup`；每窗创建新main owner marker和自己的完整`main/ownership.json`，nonce/table/PK不得与同run任何别窗重复。main finally清理本ledger；外层即使main失败也用原未变LiteralProbe独立cleanup exact同一ledger，路径和hash必须对应本窗。common Cleanup及其finally行为不变，不复用空/subset ledger，不清其他窗。

时限固定main900（含finally）、cleanup300、独立validator180秒；记录实际退出/host code/timeout/termination。receipt不能只满足elapsed≤自报deadline，validator要显式要求原固定上限。外层final aggregator也保持180秒。无ledger时本窗失败，不能伪造cleanup成功。所有失败先走既定有界清理再停止后续窗/组/profile，不因60个业务completed而跳过失败门。

每个window receipt至少绑定run_id、profile/group/index、start/count、精确case ID顺序、window_contract/policy/toolpack、approved execution/startup及输入before/after；main/internal-host/cleanup-host命令也带或绑定同一window身份。cleanup可保持原C#接口，通过父host/window receipt的命令路径、ledger hash绑定，不改旧L0程序。artifact路径必须是本新run下该窗的canonical路径，不接受跨run/跨窗路径、symlink解析后逃逸或将旧文件copy进来但内部run/window字段仍旧的材料。main报告run/window字段与实际命令一致；同窗Prepare/latest response/operation/EF identity和controls仍仅查本窗report。

每窗validator自然exit0且独立重新检查全部本窗case、main finally cleanup、独立cleanup及其finally、genuine generated SQL、actual identity、源包闭包后，才可进入下一窗。结果旗用`e_window_contract_valid`，不提前标e_group/e_value。

每组group-receipt索引该组全部窗口的receipt hash及独立validation hash，不包含从旧cohort引入的已通过case。组完成要求所有预期窗按序完整；可由最终validator重新验证组聚合，避免额外DB或重复业务。新run-receipt保持八组顺序，并索引全部25窗；新`E_windows_complete`等状态只在最后一窗门成功后写入，旧`E_groups_complete`不能通用放行。

最终validator必须从新run根重新校验25个window及每窗保存的独立结果，得到实际已验证case ID序列；逐组与原full registry精确相等、全局唯一338且无遗漏/重叠/重排。所有窗口共用同一新source/cohort/toolpack/approval/run_id，输入before/after闭合；所有nonce/table/PK全run不重复。不能仅加总count，不能用多个run的窗口拼成338，不允许旧v5/v6/v7已过group补全、从中间窗resume或缺少任一新窗。最终仍full_EF_C=false、D pending。

## 必须实际执行的无DB partition门

新Program提供单独offline partition模式，必须在读取TEST环境、CandidateGuard、Context/OwnedObjects、连接/observer安装之前分支。它实际调用当前编译的`RuntimeCases.Required`和**与runtime同一个window helper**，输出两个profile/八组的完整338个RuntimeCase定义及25个slice（不是只打印硬编码count），输出新的文件并标database_accessed=false。缺window、越界、任意range/ID/size及与GeneratedCreate混用等非法参数在同纯路径拒绝。

Low使用新批准SDK/host执行此编译产物，保存真实command/exit/elapsed、DLL/MVID/deps及输出hash。独立Python从未改变的338项policy重建分区，逐case核ID/profile/group/API/shape/kind/ticks/offset/scale，并按既有合同映射核C# route/expected语义；逐窗核start/count/顺序及同窗async成功前缀。不能仅让两个Python helper互相比对而没有执行C#，不能只检查总数338。

该offline probe和独立验证结果形成新的批准artifact，绑定新startup与toolpack；`inputs`显式要求这一有限gate及其固定输出、自然exit0，不以README或操作者说明代替。生成gate时无需尚未创建的最终运行approval，避免循环依赖：Low先依已冻结source/build/pack执行并验证probe，root再把gate引用写入新运行approval；运行时inputs重新核hash和独立分区结构。

同一**新编译Program**还须重新执行原shared14+TLS14=28-model offline-generated-create门及其既有独立检查；它和partition门不同，不能互相替代或缩减到每窗几个模型。两者均不授权DB。测试至少包括合法25窗/338、缺窗/重复/重排/交叉slice、size16/任意size、错case定义、跨run/pack/startup/ledger/path、旧group借证据、窗口前缀控制缺失、失败cleanup/validator及错误deadline，并先有完整正例再逐字段变异。

## source/build/toolpack与激活顺序

Program改变，必须形成新的E九源manifest/aggregate、同源combined replay/source/lock/build/startup manifest、新Program DLL/MVID及完整两个host依赖目录闭包；禁止把旧E-v5 startup身份套给新代码。csproj/依赖不变仍需走既有锁定restore/build和独立检查。W包/DLL/MVID、EF provider源码与mapping proof不变；原L0 accepted source、自然原source root/toolpack/run/startup/validation引用保持，不迁移、不回写、不重新解释。新combined cleanup-host若重新编译，按新startup真实身份记录，不能据此替换历史L0 proof。

新E pack仍有限原八项工具集合，预期runner/validator/policy/tests四项hash改变，其余共享oracle/runtime-validator/managed identity等保持；新source snapshot中Program改变且policy相应pin更新。所有新artifact置新目录，不更新旧runtime失败或旧toolpack。Low独占构建/测试窗口，High代码freeze→源码复审→Low锁定build/合同/partition/28model gates→review/root新approval→25窗新runtime，依次通过。

**实施激活仍需root先收到E-v7精确60对象的独立absence关闭及窗口释放证据。**当前只满足后者；本设计ready不允许在old owned状态未明时开始修改冻结输入或启动新DB cohort。完成前置后可按此五文件窄范围派发High，不需要借此改变产品行为、提高时限或创建通用续跑框架。
