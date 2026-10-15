# Issue #13：.NET 6 兼容层

当前源码在同一 `W.DamengProvider` 包内生成 `net6.0;net8.0;net9.0;net10.0`。构建 SDK 仍由 `global.json` 固定为 10.0.401，产品与测试继续使用 C#12；已发布 `0.1.0-preview.1` 不含本改动。不包含 .NET Framework/netstandard、Core 3.1 或任何 EF 适配。

PR #25 已合入 main。本页的 Linux 测试及严格 TLS 待验状态属于当时候选；当前发布候选的 macOS 四目标、shared/隔离 TLS 实库检查见 [0.2.0 发布准备](../release-0.2.0.md)。

## 实现方式

沿用 [#12](I12-multitarget.md) 的做法：差异尽量收敛为全目标通用写法，只有平台缺少类型或虚成员时才使用 `#if`。

| 差异 | 处理 |
| --- | --- |
| `TimeProvider`/`ITimer` | 仅 net6 引用官方 `Microsoft.Bcl.TimeProvider 8.0.1`（net6 选用其 netstandard2.0 资产，传递 `Microsoft.Bcl.AsyncInterfaces 8.0.0`）。8.0 系列仍将 net6 视为受支持目标、不发出警告；10.0.x 会对 net6 消费者发出“未测试”构建警告。单调时钟、长超时分片与取消胜者语义不变。 |
| `Stopwatch.GetElapsedTime` | 诊断耗时改为 `TimeProvider.System.GetElapsedTime`，使用同一 `Stopwatch` 时间戳与频率，所有目标结果相同。 |
| `ReadOnlySpan<char>(in char)`、`Interlocked.Read(in)`、`ConditionalWeakTable.TryAdd` | 旧解析代码改用 `MemoryMarshal.CreateReadOnlySpan(ref c, 1)`、`Interlocked.Read(ref)`；传输失败标记改用 `AddOrUpdate`（值恒为同一标记，语义相同）。 |
| `InlineArray5` | 未被引用的反编译残留，已删除。 |
| `DbBatchCommand.CanCreateParameter/CreateParameter` | .NET 8 才是虚成员；net6 在 `DmBatchCommand` 上提供同名非 override 成员。 |
| `X509CertificateLoader` | 没有支持 net6 的 `Microsoft.Bcl.Cryptography` 版本。内部 `DmCertificateLoader` 在 net8+ 直接转发；net6 一次读入文件字节，用 `GetCertContentType` 要求 CA 为证书、客户端文件为 PKCS#12，再由同一字节构造（PFX 字节用后清零）。未实现 `Pkcs12LoaderLimits` 的迭代次数等限制；证书文件属于应用本地配置。 |
| `CertificateChainPolicy` | 见下文 TLS。 |

C# 语法未降为 C#10：SDK 固定为 10.0.401，主构造、集合表达式、`>>>` 等只是编译器特性，生成的 IL 不依赖 .NET 7+ 运行时。唯一依赖运行时类型的 `InlineArray` 已随死代码删除。改用 SDK6 或批量重写语法不会增加兼容性。

## 数据源公开 API 差异

.NET 6 没有 `DbDataSource`，也没有 `DbProviderFactory.CreateDataSource`。没有在 `System` 命名空间仿造这些类型。

| 成员 | net8/9/10 | net6 |
| --- | --- | --- |
| 基类/接口 | `DbDataSource` | `IDisposable`、`IAsyncDisposable` |
| `ConnectionString`（脱敏） | override | 普通属性 |
| `CreateConnection()` / `OpenConnection()` / `OpenConnectionAsync()` | 返回 `DmConnection` | 相同签名 |
| `CreateCommand(string)` / `CreateBatch()` | 继承自基类 | 直接提供，返回 `DbCommand` / `DbBatch` |
| `Dispose()` / `DisposeAsync()` / `ClearPool()` / `Create()` | 有 | 有 |
| `DmClientFactory.CreateDataSource` | override，返回 `DbDataSource` | 非 override，返回 `DmDataSource` |
| 通过 `DbDataSource` 或 `DbProviderFactory` 多态使用 | 支持 | **不支持** |

net6 的 `OpenConnection`/`OpenConnectionAsync` 与 `DbDataSource` 一致：Open 失败时同步或异步释放尚未交付的连接，保留调用方取消令牌。停止 admission、发布门、幂等释放与容量归还由各目标共用的同一实现完成。Pool 套件的数据源用例（释放与发布竞态、排队取消、已发布租约可完成、Reader 持有连接、打开失败释放连接并归还容量、重复释放）在四个目标上运行同一套测试。

## TLS

.NET 7+ 由 `SslStream` 按 `CertificateChainPolicy` 建链。.NET 6 没有该属性，驱动在 `RemoteCertificateValidationCallback` 中按同一个 `X509ChainPolicy` 重新建链：

1. 无证书，或 `SslStream` 报告链错误以外的任何问题（含主机名不匹配），直接拒绝。主机名仍由 `SslStream` 按正确的 `TargetHost` 判定，不自行实现 DNS/IP/SAN 匹配，不设全局回调。
2. 新建 `X509Chain`，复制 TrustMode、CustomTrustStore、吊销模式与 `ExcludeRoot`、`NoFlag`、`UrlRetrievalTimeout` 和 ServerAuth EKU。服务器发送的证书只加入 ExtraStore 帮助建链，System 与 CustomRootTrust 模式下都不会成为信任锚。结果仅由 `Build` 决定。
3. 平台自建链只按系统信任判断且结果被丢弃，因此 net6 对它设 `NoCheck`，避免在握手预算内重复在线吊销查询；策略链按配置执行 Online/NoCheck。Online 无法确认吊销状态时拒绝，不降级。
4. 证书加载、私钥检查、客户端证书与 scope 所有权、macOS 临时 keychain 边界与 net8+ 相同。

策略链在握手回调中构建，与 .NET 7+ 的位置相同；`UrlRetrievalTimeout` 只限制 URL 获取，不保证 `Build` 总时长。net6 另有一处时序差异：`SslStream` 在调用回调前仍会以默认 URL 获取超时自建平台链，驱动无法配置。若服务器缺少中间证书，且叶证书 AIA 地址无响应，握手可能超出连接 deadline 后才失败。结果仍是拒绝，不影响安全判定。

新增四目标 loopback 用例：服务器发送中间证书时可建链到配置根；缺少中间证书失败；服务器带根配置整条链、客户端信任其他 CA 时失败（`SslStreamCertificateContext` 可能不发送自签根，此例主要证明未知 CA 被拒）；默认 Online 遇到无吊销信息的证书失败。net6 专属单元测试直接调用回调：名称不匹配、无证书、错误根均拒绝；平台链 ExtraStore 中的中间证书可完成路径；即使 ExtraStore 含对端根，信任其他 CA 时仍拒绝。针对 net6 做了变异检查：去掉 ExtraStore 复制或将策略吊销改为 NoCheck，相应用例均失败。原有的 CA/PFX/PEM、错误根、域名/IP、过期、错误 EKU、mTLS 缺失证书、握手超时与关闭用例在 net6 上同样运行。

## 测试项目

全部测试项目增加 net6。`Microsoft.NET.Test.Sdk 17.14` 不再包含 net6 test host，net6 条件使用 17.13.0，其他目标不变。测试侧替换 .NET 7+ 便捷 API（`Order`、`ReadExactlyAsync`、PEM 导出、`UnixFileMode`、可释放的 `TcpListener`），断言不变。

时间文本用例中，.NET 6 的 `DateTime` 解析拒绝时钟内含 U+200F 的文本，而 .NET 7+ 接受。驱动在自身完成小数规整后，最终解析仍交给当前文化下的 `Convert.ToDateTime`。因此 net6 改为断言驱动与运行时同样拒绝该输入；其他目标的断言不变。

## 验证

2026-10-15，Ubuntu 24.04 / WSL2 / linux-x64。基线 `c7e359f`，加本次未提交 diff（受测候选为 ignored `.local/issue13/candidate-final.patch`，SHA-256 `3f7bf094d176b250f4dfd5140e9fac1aa4e4f67046fb0e08c3f40e906372eebf`）。构建使用 SDK 10.0.401；运行时为 .NET 6.0.36、8.0.32、9.0.21、10.0.12。验证包版本 `0.1.0-preview.2` 只供本地消费，未创建 tag，也未上传。

### 离线矩阵与包消费

从仓库根目录执行（`DOTNET_ROOT`/`DOTNET_COMMAND` 指向含四个 runtime 的本地 SDK）：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 --commit "$(git rev-parse HEAD)" --output .local/issue13/final
```

发布工具 Python 23/23 通过。`prepare` **退出 0**，52 份 TRX 共 **9626/9626** 通过，失败、跳过、中止均为 0：

| 套件 | net6.0 | net8.0 | net9.0 | net10.0 |
| --- | ---: | ---: | ---: | ---: |
| Configuration | 12 | 12 | 12 | 12 |
| Session | 21 | 21 | 21 | 21 |
| Transport | 134 | 134 | 134 | 134 |
| Security | 47 | 45 | 45 | 45 |
| Command | 172 | 172 | 172 | 172 |
| Type | 965 | 965 | 965 | 965 |
| Transaction | 135 | 135 | 135 | 135 |
| Isolation | 78 | 78 | 78 | 78 |
| Async | 170 | 170 | 170 | 170 |
| Pool | 308 | 308 | 308 | 308 |
| Lob | 258 | 258 | 258 | 258 |
| Diagnostics | 85 | 85 | 85 | 85 |
| Schema | 23 | 23 | 23 | 23 |
| 合计 | **2408** | **2406** | **2406** | **2406** |

net6 Security 多出的两项是只在 net6 运行的回调单元测试。同一个包含二十个托管程序集（四个目标各有主 DLL 和四个语言资源），依赖组见上文。八个独立消费者从全新缓存恢复，各自选中对应 `lib/<tfm>` 资产，在实际 runtime 上通过公开 smoke、文化资源回退和七个首次初始化场景，四目标共 **28/28**。候选 nupkg SHA-256：`e12733551c9fc3137a999bbe3b0b3774a8cfdbdc6cb1cbf07ad0de03ddec4cfe`。

### 实库

shared TEST 经 `scripts/with-dameng-test.sh` 加载，对同一包的四个消费者串行执行 `plaintext` smoke（`PlaintextAllowed`），四个均 **退出 0**。每个目标都确认了服务器侧用户与当前 Schema 为 `WDM_PROVIDER_TEST`，参数、Reader、提交/回滚、时间值、BLOB/CLOB、预取消后复用以及 Reader/连接释放均通过。各目标创建的唯一表 `NUGET_<guid>` 已删除，并经新连接确认不存在。这也补上了 #12 中 net8/9 shared TEST 的待验项。

严格 TLS TEST 在四个目标上仍为 **integration_pending**：本机没有 `dameng-tls-test.env`，未启动或修改 TLS 实例。TLS 行为目前由真实 loopback 握手覆盖，这不等同于实库验收。

### 已处理的验证失败

- net6 首轮离线：TypeTests 中 U+200F 时钟文本 2 例失败，原因是测试的期望值 `Convert.ToDateTime` 在 .NET 6 抛 `FormatException`（运行时差异，见上文）。改为 net6 断言驱动与运行时同样拒绝。
- 第一次完整 prepare（`.local/issue13/validation/`，退出 1）：52 份 TRX 全部通过、打包和 net6 公开 smoke 也通过，随后 `R3ResourceProbe` 消费者因工具源码使用 .NET 7+ 的 `Order` 编译失败。属工具问题，改用 `OrderBy` 后重跑。
- 独立验证者复核上述候选：diff 身份、四目标干净 Release 构建、net6 上 Security/Pool/Transport/Type，以及全部制品与 smoke 报告，均通过。它指出没有测试覆盖数据源打开失败时释放未交付连接，因此补充了四目标用例 `FailedDataSourceOpenDisposesUndeliveredConnectionAndReturnsCapacity`。去掉 net6 的同步/异步释放后该用例失败。该测试变更之后，Pool 在四个 runtime 上各 310/310 通过（候选 `.local/issue13/candidate-post-review.patch`，仅增加该测试与本文档）。
- 独立代码审查无阻塞问题。据其意见，net6 证书加载改为单次读取，补充 ExtraStore 含对端根的回调单元测试，并在文档中写明 net6 平台链的时序差异。上表结果来自审查修改之后的最终候选。

### 未覆盖

严格 TLS 实库；Windows/macOS 上的 net6（本轮只验证 linux-x64）；系统信任库模式只覆盖策略配置，未在隔离系统信任环境中握手；在线吊销仅覆盖“无吊销信息时拒绝”，未覆盖真实 CRL/OCSP 有效与已吊销。

## 运行时生命周期

按[微软 .NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，.NET 6 已于 2024-11-12 结束支持，不再获得安全更新。驱动提供 net6 资产只是兼容，不代表恢复运行时维护；仍在 .NET 6 上运行的应用应尽快规划升级。.NET 8/9 的支持期见 [#12 记录](I12-multitarget.md#运行时生命周期)。
