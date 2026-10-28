# .NET 6 兼容说明

当前 `W.DamengProvider` 包支持 `net6.0`、`net8.0`、`net9.0`、`net10.0`，并另含受限的 `netstandard2.1`。构建 SDK 由 `global.json` 固定为 10.0.401，产品和测试使用 C# 12。

本文先说明 .NET 6 的当前差异，末尾保留 issue #13 最初实现时的验证记录。最新发布结果见[0.3.1 发布记录](../release-0.3.1.md)。

## 兼容实现与依赖

差异尽量使用所有目标框架共用的写法，只有平台缺少类型或虚成员时才使用条件编译。

| 差异 | .NET 6 的处理 |
| --- | --- |
| `TimeProvider` / `ITimer` | 引用官方 `Microsoft.Bcl.TimeProvider 8.0.1`，使用其 `netstandard2.0` 目标框架文件，并传递依赖 `Microsoft.Bcl.AsyncInterfaces 8.0.0`。8.0 系列支持 net6；10.0.x 会对 net6 消费者发出“未测试”构建警告。 |
| `Stopwatch.GetElapsedTime` | 使用 `TimeProvider.System.GetElapsedTime`，与其他目标共用时间戳和频率。 |
| 新运行时 API | 用 `MemoryMarshal.CreateReadOnlySpan`、`Interlocked.Read(ref)`、`ConditionalWeakTable.AddOrUpdate` 等等价写法替代。 |
| `DbBatchCommand` 虚成员 | .NET 8 才可 override；net6 在 `DmBatchCommand` 上提供同名非 override 成员。 |
| `X509CertificateLoader` | net8+ 直接转发；net6 一次读取文件字节，检查 CA/PKCS#12 类型后由同一字节构造，PFX 字节用后清零。未实现 `Pkcs12LoaderLimits` 的迭代次数等限制。 |
| `CertificateChainPolicy` | 使用下述 TLS 回调兼容实现。 |

未使用的 `InlineArray5` 反编译残留已删除。C# 12 的主构造、集合表达式和 `>>>` 是编译器特性，生成的 IL 不依赖 .NET 7+ 运行时；改用 SDK 6 或批量降级语法不会增加兼容性。

## DmDataSource 的 API 差异

.NET 6 没有 `DbDataSource`，也没有 `DbProviderFactory.CreateDataSource`。驱动没有在 `System` 命名空间仿造这些类型。

| 成员 | net8/9/10 | net6 |
| --- | --- | --- |
| 基类/接口 | `DbDataSource` | `IDisposable`、`IAsyncDisposable` |
| `ConnectionString`（脱敏） | override | 普通属性 |
| `CreateConnection()` / `OpenConnection()` / `OpenConnectionAsync()` | 返回 `DmConnection` | 相同签名 |
| `CreateCommand(string)` / `CreateBatch()` | 继承基类 | 直接提供，返回 `DbCommand` / `DbBatch` |
| `Dispose()` / `DisposeAsync()` / `ClearPool()` / `Create()` | 有 | 有 |
| `DmClientFactory.CreateDataSource` | override，返回 `DbDataSource` | 非 override，返回 `DmDataSource` |
| 通过 `DbDataSource` 或 `DbProviderFactory` 多态使用 | 支持 | **不支持** |

net6 的打开失败释放、调用方取消令牌、停止准入、重复释放和容量归还与其他目标共用同一实现。Pool 套件的数据源用例在四个目标上运行。

## TLS 差异

.NET 7+ 由 `SslStream` 按 `CertificateChainPolicy` 建链。.NET 6 没有该属性，驱动在 `RemoteCertificateValidationCallback` 中使用同一个 `X509ChainPolicy` 重新建链：

1. 无证书，或存在链错误以外的问题（包括主机名不匹配）时直接拒绝。主机名仍由 `SslStream` 按正确的 `TargetHost` 判断，不自行实现 DNS/IP/SAN 匹配，也不设置全局回调。
2. 新建 `X509Chain`，复制 TrustMode、CustomTrustStore、吊销模式、`ExcludeRoot`、`NoFlag`、`UrlRetrievalTimeout` 和 ServerAuth EKU。服务器证书只加入 ExtraStore 帮助建链，不会成为信任锚；结果仅由 `Build` 决定。
3. 平台自建链仅用于系统信任判断且结果被丢弃，因此 net6 对其使用 `NoCheck`，避免重复在线吊销查询；策略链仍按配置执行 `Online` / `NoCheck`。Online 无法确认吊销状态时拒绝，不降级。
4. 证书加载、私钥检查、客户端证书和 macOS 临时 keychain 的生命周期与 net8+ 相同。

策略链在握手回调中构建，与 .NET 7+ 的位置相同。`UrlRetrievalTimeout` 只限制 URL 获取，不保证 `Build` 总时长。net6 还有一个时序差异：`SslStream` 在调用回调前会按默认 URL 获取超时自建平台链，驱动无法配置。服务器缺少中间证书且叶证书 AIA 地址无响应时，握手可能超过连接期限后才失败，但结果仍是拒绝，不影响安全判定。

离线用例覆盖中间证书、未知 CA、主机名、错误用途、缺少客户端证书、无吊销信息和回调变异检查；默认 `Online` 策略保持不变。

## 测试项目差异

`Microsoft.NET.Test.Sdk 17.14` 不再包含 net6 test host，因此 net6 条件使用 17.13.0，其他目标不变。测试侧只替换 .NET 7+ 便捷 API，断言保持不变。

.NET 6 的 `DateTime` 解析会拒绝时钟内含 U+200F 的文本，而 .NET 7+ 接受。驱动最终仍使用当前文化下的 `Convert.ToDateTime`，所以 net6 测试断言驱动与运行时同样拒绝该输入。

## 历史验证记录

以下结果属于 issue #13 当时的本地候选，不是最新发布验证。

2026-10-15，环境为 Ubuntu 24.04 / WSL2 / linux-x64，基线 `c7e359f` 加当时修改。本地包版本 `0.1.0-preview.2`，未创建 tag 或上传。实际运行时为 .NET 6.0.36、8.0.32、9.0.21、10.0.12。

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 --commit "$(git rev-parse HEAD)" --output .local/issue13/final
```

发布工具 23/23 通过，`prepare` 退出 0。52 份 TRX 共 **9626/9626** 通过，失败、跳过和中止均为 0：

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

net6 Security 多出的两项是 net6 专属回调测试。八个独立消费者均选中对应 `lib/<tfm>` 资产，通过公开 smoke、文化回退和共 **28/28** 首次初始化场景。候选包 SHA-256 为 `e12733551c9fc3137a999bbe3b0b3774a8cfdbdc6cb1cbf07ad0de03ddec4cfe`。

共享 TEST 对同一包的四个消费者串行执行明文 smoke，均退出 0。每轮核实 TEST 身份，覆盖参数、Reader、事务、时间和 LOB，并由新连接确认本轮表不存在。

当时的严格 TLS TEST 为 `integration_pending`，原因是没有本机 TLS 配置；该状态只属于当时。后续正式发布结果见[0.2.0](../release-0.2.0.md)和[0.3.1](../release-0.3.1.md)。

### 已处理的验证失败

- net6 首轮 TypeTests 的 2 个 U+200F 用例因运行时差异失败，按上述运行时行为修正测试预期。
- 第一次完整 `prepare` 的 TRX 和打包通过，但 `R3ResourceProbe` 使用 .NET 7+ 的 `Order` 导致编译失败；改用 `OrderBy` 后重跑。
- 独立复核补充了数据源打开失败时释放未交付连接并归还容量的测试。去掉 net6 对应释放逻辑后该测试会失败；补测后 Pool 在四个运行时各 310/310 通过。
- 代码审查后，net6 证书加载改为单次读取，并补充 ExtraStore 含对根证书时的信任检查。

当时未覆盖 Windows/macOS 上的 net6、隔离系统信任库和真实 CRL/OCSP 场景。

## 运行时生命周期

按[微软 .NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，.NET 6 已于 2024-11-12 结束支持，不再获得安全更新。驱动提供 net6 目标框架只是兼容，不代表恢复运行时维护；仍使用 .NET 6 的应用应规划升级。
