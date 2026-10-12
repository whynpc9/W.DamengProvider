# TP07 T1产品源v3：W重试合同测试源码审阅

2026-10-11。结论：**T1_W_retry_counterpart_source_accepted / new_build_retry49_transaction25_full824_pending**。新单测试文件实现了[完整门失败分诊](TP07-T1-product-full-failure-triage-v1.md)要求的W公共分类、错误号非充分条件和非空override拒绝边界；未发现需继续修改源码的阻塞项。此结论仅接受源码，不接受尚未执行的编译、49/25/824场测试、异步重试或runtime，不授权运行。

本轮只读新旧178源、原批准175、manifest/完整patch/README、历史失败TRX及实际依赖包XML，并独立计算hash、在内存逐hunk重建patch、解析静态测试声明和TRX集合。没有运行SDK、tests、prepare/validator或项目模块import，没有执行程序集、DB/wrapper或读取secret。唯一新增文件为本文；旧失败、源码和状态未修改。

## 冻结身份与完整来源

新root：`.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source`。

| 输入 | SHA256 |
| --- | --- |
| `DamengExecutionStrategyTests.cs` | `cb7b6114490fc1fd34fc3c839e2fdc24d1eaeffdf8ed8b5ac6f81215e9fbfe2f` |
| 178源aggregate | `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c` |
| `transaction-capability.v3.patch` | `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f` |
| `transaction-capability-manifest.v3.json` | `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8` |
| `README-transaction-capability.v3.md` | `5863a9158ac88e643a534e6ab17f7ba478a70fb908626a324e840464af5e973f` |

实际目录恰178文件，逐项hash和声明排序格式的aggregate均匹配新manifest。与父v2 manifest `fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432` 相比，只有retry unit一个文件变化，其余177不变，包括事务25场unit `f4f689c8c2285ed4665880d65f0ccbd22424862dc1ac9eef1fc0cc69b44dad1d`、DI/adapter/factory、原retry三个产品文件、props/global和两锁。

本 reviewer 逐项读取原 `TP07-L0-tools-v3/approved-EF-source` 的175字节并与base map核对，再在内存匹配完整patch各hunk的原行及行数、重建结果。五个patch路径恰为原DI、retry unit、两个事务产品及事务unit；重建后完整178字节hash等于新map。相对175为173不变、2修改、3新增；相对父v2的单文件差异没有被误当作完整交付patch。最初本reviewer的内存读取器未跳过Git diff/index头而停止，调整纯文本读取器后完成重建；没有调用磁盘patch或修改任一输入。

新manifest内19条完整path/SHA引用均实际重算一致，包括原base/bridge/mapping、父manifest/patch/review/approval/freeze、原full失败TRX/manifest/独立readback/safe理由、triage及795行基线。所有旧失败引用仍绑定原失败字节，没有重标为通过。W仍为固定TP06 `0.1.0-temporal.20261008144915`、nupkg `da8349a2…`、DLL `843710be…` / MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`；本轮未改W产品或依赖。

## 原20场、失败12场和新完整集合

独立读取原full TRX `f470a92ddb772627a311bf6bd9e2966826c4f960520c63f9c3aa5e375b009271`，其795行仍是783 passed/12 failed；retry类20行、其中12 failed。新manifest的original20/original12映射与原TRX名称/outcome逐项相同，新端20个名称唯一；映射不删除原case或将旧失败改成旧通过。

独立从新源Fact/Theory/InlineData与参数名形成静态case名单：21方法、49场，全部与manifest声明相同；49减去上述20映射目标恰是29条新增名单。原十个错误号保留，原五个非瞬态/模糊码、非W Timeout、默认设置和collection-null场保留；两个非空override旧成功场分别映射到合法empty snapshot与构造阶段拒绝。

实际原TRX中775条非retry行，加新49条，恰为824个唯一planned名称，并与manifest的完整名单集合相同。transaction25仍是原775中的同一集合、源码未改；`planned_unit_cases=25`、`planned_transaction_unit_cases=25`没有被49/824混用。这些数值是静态清单与历史基线的对账，不是SDK发现数或新TRX运行结果。

## 测试确实消费生产边界

- **Number不是安全重放证明：**十个旧transient码及五个原negative码均经精确W构造生成actual `DmError`/`DmException`，检查Number和IsTransient=false，再由公共生产strategy执行。共用helper先核生产ShouldRetryOn=false，然后明确调用 `Execute<int,int>`；委托真实增加attempts并抛同一对象，最后核attempts=1和同异常传播。原非W Timeout也进入同一生产执行断言，没有仅反转原布尔值。
- **override拒绝完整：**builder的collection入口和三参enumerable入口分别覆盖三个原数据值；后者传yield iterator，未仅测ICollection。context三参构造两个值，加原 `-77777` 构造/委托零次场；dependencies默认及自定义两个入口各三个值。拒绝场不绕过生产构造器，原额外错误号没有作为新的重放OR支路。
- **合法配置与snapshot：**builder collection/三参空集合均先配置、再修改调用方列表、最后才构造context及生产strategy；核空snapshot且不是可写ICollection。null/empty和默认/自定义count-delay覆盖context/dependencies构造，集合原始null拒绝场保留。dependencies的默认overload源码参数虽标非null，转入共享实现时明确允许null；该测试仅对这一边界局部使用 `numbers!`，不关闭Nullable或改变产品签名。
- **公开分类正反控制：**private测试子类调用现有protected DmException构造，仅override公开IsTransient；true时使用真实生产Execute，前两次抛同一对象、第三次返回42，期待attempts=3；false时生产Execute一次并同对象传播。非W DbException即使IsTransient=true也期待一次，证明W类型门。源和README均明确synthetic true不是W传输产生了可安全重试异常的实证。
- **实际sealed拒绝类型：**CommitUnknown精确单Exception内部构造，核actual sealed类型、cause、IsTransient=false及真实Execute一次。Timeout精确八参FailureInfo构造明确Receive/Unknown/TotalDeadline，再以真实DmTimeoutException(info,cause)构造，核info同对象、cause、Number0、IsTransient=false和一次传播。这里是明确的合成构造输入，无私有字段写入、SetFailureInfo反射调用或绕过构造；不把Unknown改成可重试。此fixture不证明实际COMMIT丢ACK或timeout producer路径已运行。

`RequireNonpublicConstructor` 对缺失签名立即抛出明确异常；actual类型用 `Assert.IsType` 核实，没有catch后替代Exception、未初始化对象或私有classifier注入。新增测试没有复制一个本地detector代替生产方法。

**同步/异步边界：**新文件只有同步 `Execute`，`ExecuteAsync`调用为0。原分诊设计将async正反控制列为“可补”，本次最小范围不以其缺失阻断同步counterpart源码接受；不能将这49场描述为同步/异步策略都已覆盖。原事务25场中的异步生命周期也不能代替retry ExecuteAsync测试。若后续需要异步retry合同，应另行明确范围和新冻结身份。

## 静态编译与分析器检查

实际固定EF10.0.12包XML确认生产 `ExecutionStrategy.Execute<TState,TResult>(TState, Func<DbContext,TState,TResult>, Func<DbContext,TState,ExecutionResult<TResult>>)`；本文件正控制可由state/返回值推断int/int，纯throw负控制显式写 `<int,int>`，不存在旧式无返回lambda泛型推断空洞。xunit2.9.3实际XML具有 `Assert.Throws<T>(Action)` 与 `Assert.Throws<T>(Func<object>)`，构造/表达式lambda及helper类型约束 `TException : Exception` 与其API形状相符。

source中使用的provider构造、公开IsTransient、protected DmException构造及sealed内部fixture签名，均与固定产品源一致；此前分诊已从TP06归档核对W源，不通过运行反射验证。`ExecutionStrategyDependencies`在EF Storage公共命名空间，取得方式是标准 `context.GetService<T>()`；未引入 `.Internal`类型断言。实际依赖版本、项目及props不变。

`Directory.Build.props`继续net10.0/C#14、Nullable enable、TreatWarningsAsErrors=true和latest-recommended。新测试没有skip、pragma、SuppressMessage、NoWarn或全局分析器修改；没有新Dispose override、隐藏Events成员或对异常任务作同步阻塞。nullable列表使用guarded `?.Add`，空snapshot先做非null检查，constructor查找用 `?? throw`。静态核验未发现确定编译阻塞；未运行Roslyn/分析器，不能把本段当作实际严格build已通过，实际诊断仍由Low首个新build决定。

## 后续门及批准语义

新manifest要求有限scope `transaction_capability_and_w_retry_contract_tests_only`，作者没有代签root批准，也明确不复用父 `d6cff0a1…`。Low新178/patch独立snapshot后，root可签新的七字段source-only批准，绑定本manifest与178 exact paths；单位运行不是source-only批准前置，依赖不成环。

下一版preparation须绑定新manifest/patch/review/freeze/approval/source folder，并将旧scope严格守卫改为新单一值；这是固定来源及有限scope守卫修订，不能再称纯pin-only或全可执行AST一致。原锁/raw4/包图仍作固定比较基线，旧source/native/build不能拼接为本次执行。原五阶段、源数178、12refs/3folders及取消/300秒/2秒reap边界保持；任何额外算法变更须另审。

新source→native→locked build后，Low先完整retry49、再原transaction25、最后同一新build无filter full824。独立回读必须比较完整case集合、映射后的原795及29新增、实际DLL/deps/W身份、自然exit0、零skip/failure/abort/timeout；不能仅依赖旧工具的full `count>25`守卫，也不能拿旧target25/783passed抵扣新门。出现失败保存并释放窗口，不现场修源或重跑。

本次不需要W新包或重做TP06；若后续为通过测试而要求改W或产品exception contract，应停止并另走新产品/新包/TP06验收。probe及L0/E/D来源改绑、新编译28/实库等仍pending，runtime未批准。最小进一步源码修复范围：无。报告完成后停写。
