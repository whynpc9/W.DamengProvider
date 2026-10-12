# TP07 T1事务能力适配：产品源码与交接patch验收

2026-10-10。结论：**T1_transaction_adapter_product_source_accepted / Low_build_units_pending / runtime_activation_pending**。对分诊设计`efa0dfdf…`未发现产品源码或交接阻塞，不需要本轮High再修实现。只接受178源及四文件patch的有限静态合同；四方法25场尚未运行，不接受真实事务、L0/E/D或完整C。

本轮只读源码/manifest/patch/XML及实际EF DLL元数据字节，做hash、差异和内存patch重建；未执行SDK、tests、validator、DB或wrapper，未加载程序集、未应用patch到磁盘、未改source/status/旧证据，仅新增本文。High仍在编写的17-file probe不在本验收范围。

## 实际冻结身份

产品根为`.local/temporal-native/verification/TP07-T1-transaction-adapter-v1/ef-source`。

| 相对产品根的文件 | SHA256 |
| --- | --- |
| src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs | `a407f84a5d564f422f77678ff0b5d2234cef8180909fe43276163097e1e1b3b3` |
| src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransaction.cs | `95ecb94f63972e64dabd6b85dcec7d644978be06fe171b95dc67224a8a9d41cf` |
| src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransactionFactory.cs | `86333f2c171021bc984fbd53ec88e9a99eec19d6658b06fc15eb0ad124f48835` |
| test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs | `c7b5520f6a5863af5924f94a6d5a913317a164e31d6a90d18c7fd47f37eb5220` |

| 正式交接文件（tools/DownstreamAcceptance/TP07/patches/） | SHA256 |
| --- | --- |
| transaction-capability.patch | `1752dc64ae88ed6c5402675101d8df594cd514b8cc1c984d3e1af6e6b0cf5af9` |
| transaction-capability-manifest.json | `932f8f41a2d170c74d425c6add913ac1f3a5141fab49835e0311f55353183980` |
| README-transaction-capability.md | `c2eb086d7d8391e32069776a5216de508bf85bc7844ab660c2496accff0a49dc` |

实际根恰178文件，manifest完整map178逐项hash匹配，按声明格式重算aggregate为`a14b7a244d3811db24fe9f897e07adaa21dd4507a28b27e762d137ccce9896aa`。原175项实读仍匹配批准基线；其中174项在新source字节不变，仅DI文件增加注册，另加两个product类型和一个unit文件。没有删除、额外目录或任意source数量容忍。

本轮把四个patch段逐hunk与精确原175文件内容在内存核对并重建，四个输出hash全部等于新source map；没有执行git apply或写replay。manifest的source_root是明确上述新绝对目录，source_count178、base_source_count175、added/modified路径明确，patch应用基线明确是已含旧bridge/mapping的175源，不是外部dirty checkout或官方原archive。

原approved source `dc2bfd15…`、prepared source `9d1f8cb6…`和base archive/bridge manifest/bridge patch/mapping manifest/mapping patch引用均当前hash匹配；原bridge/mapping artifact未改。外部dirty checkout没有作为本次输入或验收对象，本reviewer未访问/修改它，不能把manifest的未修改声明扩大成对外部所有文件的全集核验。

## 正式DI及能力传播

`AddEntityFrameworkDameng`在TryAddCoreServices之前注册`IRelationalTransactionFactory → DamengRelationalTransactionFactory`，是普通provider路径，不是TemporalContext测试专用ReplaceService。factory继承EF RelationalTransactionFactory，通过注入的dependencies构造基类，Create将connection、实际DbTransaction、transactionId、logger、transactionOwned原样传给adapter，再传同一Dependencies.SqlGenerationHelper。没有创建旁路transaction/connection，没有反射、server/version字符串或profile白名单。

adapter仅保留构造收到的同一DbTransaction引用，`SupportsSavepoints`每次读取该对象属性；不捕获connection的“下一事务”设置、不共享缓存能力。其constructor把六项参数原样交给RelationalTransaction基类，Commit/Rollback/Dispose、异步、transactionOwned、CurrentTransaction清理及savepoint操作实现全部继承，不重写其生命周期。

精确生命周期术语：EF Core 10.0.12的transaction factory是**Singleton**（实际package XML明确），不是Scoped service。当前factory无mutable状态、无scoped服务依赖；每次Create取得调用方实际事务，因此singleton正确。事务实例/连接仍属于各自context操作范围；不应为了“scoped”字样改服务寿命或从单例缓存某连接能力。

W DLL/package/MVID保持原`843710be…/da8349a2…/aa23e673…`。底层TLS SupportsSavepoints=false将被诚实传播，shared=true继续保留自动savepoint；AutoSavepointsEnabled及AutoTransactionBehavior没有被设置或关闭。此实现不开放TLS savepoints、七位时间精度、隔离级别或其它未验证能力。

## EF10.0.12实际API静态核对

本轮读取当前包XML和实际`Microsoft.EntityFrameworkCore.Relational.dll` SHA`a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d`的ECMA表：RelationalTransaction/Factory均非sealed；Transaction constructor公开且六参数形状与代码相符；SupportsSavepoints getter public virtual非final；Factory dependencies getter protected virtual，Create public virtual非final，其五参数签名及返回RelationalTransaction与代码相符。

unit TestBatch涉及的ModificationCommands、RequiresTransaction、AreMoreBatchesExpected、TryAddCommand、Complete、Execute、ExecuteAsync均是该实际版本public abstract可覆写成员，基类构造protected可用。UseDameng(DbConnection)正式重载允许注入test连接，内部类型对unit程序集已有InternalsVisibleTo。未发现静态API签名冲突；真实编译仍必须由Low实际证明，不能把本段等价为build通过。

## 四方法25场的有效覆盖

静态计数为三个Theory各8个InlineData及一个Fact，即4方法/25场，所有fake均在新增unit文件内，无secret/DB连接：

- 8场实际DI BeginTransaction/BeginTransactionAsync：true/false capability × sync/async × commit/rollback；核factory/connection/adapter类型、同underlying/connection、CurrentTransaction、ReadCommitted、非空transactionId、owned实际一次Dispose。
- 8场UseTransaction/UseTransactionAsync：真实DI wrapper、传入transactionId、同caller-owned底层对象；commit/rollback转发，wrapper释放后不Dispose调用方事务。
- 1场共用真实singleton factory但两个不同context/底层事务，分别true/false；改变connection下一事务设置不改变现有事务能力，防止按共享连接状态缓存。
- 8场解析真实DI `IBatchExecutor`（断言类型为EF BatchExecutor），不是mock新getter：执行synthetic业务batch，核实际调用的IRelationalConnection/底层事务、sync/async路径、AutoSavepointsEnabled=true及WhenNeeded。true路径要求create→batch→release，失败要求create→batch→rollback-to→release；false路径仍执行业务但不发savepoint SQL。失败保留同一异常，最后完整Rollback恢复synthetic work，owned Dispose一次。

batch为空ModificationCommands，返回count0是明确的unit构造，业务证据是work counter和events，不假称真实SaveChanges affected1或server状态。TestConnection/Transaction/Command只模拟受控本地行为，SQL比较使用真实provider ISqlGenerationHelper的固定`__EFSavePoint`语句。该覆盖能发现原默认factory错误及能力true/false路径差异，超过镜像getter断言；但不能证明W真实服务器事务或其超时/回滚状态。

## 新source批准与后续门

当前rich transaction manifest是**source_frozen_verification_pending**，不是root source approval。后续仍由root在Low新单位/M1支持证据成立后签有限source approval：沿实际已用七字段形状（schema_version/task/source_hashes/M1_execution_summary_sha256/execution_source_manifest_sha256/original_unit_lock_sha256/unit_lock_sha256）生成新身份，source_hashes必须是完整178 map，执行/单位锁hash必须取新真实收据，不能复用旧175成功或自填reviewed=true。具体task/version须与新有限reader合同同步，不能把rich manifest的附加字段直接当旧七字段批准接受。

Low下一步只按root授权窗口snapshot新178、isolated restore/build、focused25场及完整provider units，核原W/依赖和真实DI输出；需要修复时由High在新明确范围修复、重新freeze并复核，不能在冻结源上由Low补代码。现在没有源码阻塞/最小修复任务。

真实4例事务activation probe及其17文件尚未验收，另做source/inputs/compile/readiness后才可DB；随后按既定新overlay lineage重新走L0、E新source/native/build/partition/GeneratedCreate28/full25、D准备等门。旧L0/E/D proof与旧291absence/real DT/DTO generic事实保留但不能替代新overlay接受。此报告不批准probe/SDK/DB运行、不接受runtime或完整C。
