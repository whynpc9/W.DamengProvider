# T1：EF 事务保存点能力适配增量

正式 provider 的 transaction factory 现在创建适配事务，将 `SupportsSavepoints` 转发给构造时传入的同一个 `DbTransaction`。支持能力为 false 时，EF BatchExecutor 跳过自动保存点并继续原事务内的业务 batch；能力为 true 时，保存点及失败 rollback-to 路径保留。事务 ownership、Commit/Rollback/Dispose 生命周期由原 RelationalTransaction 基类实现。没有改 W 包、官方驱动、AutoSavepointsEnabled 或 AutoTransactionBehavior。

新 source 从批准 prepared-v5 的 `approved_ef_source_hashes` 精确复制175项后，仅新增两个 product 类型和一个 unit 文件，并增加一行正式DI注册，实际178项。patch 只作用于已含原 bridge/mapping 的批准175 EF源；不得再应用到外部dirty checkout或已应用该增量的新source。manifest枚举实际178项、175基线及原mapping/bridge引用、新独立patch SHA和namespace边界。原temporal-mapping.patch/mapping-manifest/bridge证据不改。

唯一新增 unit 文件内部定义全部 fake/stub。计划4方法/25场：实际DI的sync/async BeginTransaction与UseTransaction、true/false能力、owned/unowned Commit/Rollback/Dispose、同singleton factory两个不同底层事务，以及真实DI IBatchExecutor的create/release、失败rollback-to、unsupported跳过但业务执行及fullrollback。fake不连接服务器，业务工作计数仅为unit模型，不构成任何达梦事务/实库验收。

作者仅核复制hash、实际EF10.0.12 XML/ECMA元数据、源码/patch/清单范围；未运行SDK、build、tests或DB。需要Astra源审阅后由Low新隔离目录执行：

```text
dotnet restore test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --locked-mode --disable-parallel -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet build test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet test test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~DamengRelationalTransactionCapabilityTests -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet test test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj --no-build --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

Low先设置可写任务DOTNET_CLI_HOME、DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1、DOTNET_CLI_TELEMETRY_OPTOUT=1，核批准SDK/候选feed及原lock，不清cache、不读取secret、不调用TEST wrapper。真实事务activation probe、E观测与各准备链改绑属于后续独立任务。当前源码冻结不表示25项已执行，也不关闭完整E/C门。
