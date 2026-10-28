# W.DamengProvider

W.DamengProvider 是社区维护的达梦数据库 ADO.NET 驱动，支持异步操作、本地事务、存储过程、数组绑定、`DbBatch`、流式 BLOB/CLOB、TLS 和标准 .NET 诊断接口。

包名和程序集名为 **W.DamengProvider**，公开命名空间为 **W.Dm**。它不是官方 `DM.DmProvider` 的直接替换包；迁移时需要更新引用并重新编译，EF 适配层也需要相应调整。

## 安装

当前版本为 [`0.3.1`](https://www.nuget.org/packages/W.DamengProvider/0.3.1)：

```sh
dotnet add package W.DamengProvider --version 0.3.1
```

| 目标框架 | 说明 |
| --- | --- |
| `net9.0`、`net10.0` | 支持现代目标框架的全部功能，无额外运行时 NuGet 依赖。 |
| `net8.0` | 额外依赖 `Microsoft.Bcl.Cryptography 10.0.12`。 |
| `net6.0` | 额外依赖 `Microsoft.Bcl.TimeProvider 8.0.1`。由于 .NET 6 没有 `DbDataSource`，`DmDataSource` 不能通过该基类或 `DbProviderFactory` 多态使用。 |
| `netstandard2.1` | 供 .NET Core 3.1 应用和 Standard 2.1 类库使用；不支持 TLS、`DbBatch`、DateOnly/TimeOnly 映射，且只能显式选择 `PlaintextAllowed` 连接。 |

各目标的验证结果见[发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.3.1.md)。运行时是否仍受微软支持，请单独查看 [.NET 生命周期政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。

## 快速开始

连接串应由应用配置或 secret store 提供。现代目标框架默认要求 TLS 并验证证书；CA、客户端证书和私钥路径需要与服务器配置匹配。

```csharp
using W.Dm;

var settings = new DmConnectionStringBuilder(
    Environment.GetEnvironmentVariable("DAMENG_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Missing database configuration"))
{
    TransportSecurity = DmTransportSecurity.RequireTls
};

await using var source = DmDataSource.Create(settings.ConnectionString);
await using var connection = await source.OpenConnectionAsync();
await using var command = connection.CreateCommand();
command.CommandText = "SELECT 1 FROM DUAL";
var value = await command.ExecuteScalarAsync();
```

需要 `DbProviderFactories` 时，可在应用启动时注册：

```csharp
DbProviderFactories.RegisterFactory("W.DamengProvider", DmClientFactory.Instance);
```

## 与官方驱动对比

如果你在使用官方驱动时遇到过**存储过程参数静默错绑、函数返回值位置受限、过程时间参数被舍入或时区偏移无法保留**，W.DamengProvider 重点解决的就是这些问题。

| 开发痛点 | 官方驱动实测行为 | W.DamengProvider 0.3.1 |
| --- | --- | --- |
| 存储过程参数名写错 | 未知名称会回退为按参数集合位置绑定，可能把值传给错误的参数 | 直接拒绝未知名称，不猜测调用者意图 |
| 函数返回值不放在参数集合首位 | `ReturnValue` 位于不同位置时可能得到不同、甚至错误的结果 | 支持放在集合中的不同位置，返回结果一致 |
| 存储过程需要七位小数秒 | 时间输入会由七位降为六位，例如 `.1234567` 被舍入为 `.123457000` | 已验证的服务器环境下保留 CLR 的 100ns 精度，不做隐式舍入 |
| 读取带时区的时间列 | `GetValue` 返回 `DateTime`，无法通过返回值保留原始偏移 | 返回 `DateTimeOffset`，明确保留时区偏移 |

此外，本驱动还提供贯通网络 I/O 的异步 API、减少逐行往返的数组绑定、标准 `DbBatch` API、流式 BLOB/CLOB、默认严格 TLS，以及 `ActivitySource` 和 `Meter` 诊断接口。

从官方驱动迁移时间参数时，小数秒位数应使用 `Scale`，例如将 `Precision=7` 改为 `Scale=7`。

以上官方行为来自 `DM.DmProvider 8.3.1.47463` 实测；过程相关测试使用 DM 8.1.5.60，不代表所有官方版本。详见[过程对比记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/I09-characterization.md)和[时间精度说明](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/TP-temporal-precision.md)。

## 支持范围与限制

- **异步与取消**：支持同步和异步 Open、Prepare、Execute、Read、NextResult。请求发送后发生取消或超时时，物理连接可能关闭；无法确认结果的提交会报告 `OutcomeUnknown`，驱动不会自动重试。
- **事务**：默认隔离级别为 `ReadCommitted`。保存点和其他隔离级别取决于服务器能力，详见[事务说明](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/T10-transactions.md)。
- **连接管理**：`DmDataSource` 会限制并发连接数，但目前每次租用都会新建物理会话，归还时销毁，因此没有物理连接复用的性能收益。
- **BLOB/CLOB**：支持流式读写。完整物化读取的返回载荷上限为 64 MiB；流式输入需要显式声明 BLOB/CLOB 类型，输入流由调用者管理。
- **时间精度**：CLR 时间值保持 100ns tick 精度；无法被服务器或目标列精确表示的值会被拒绝。七位小数写入只在已验证的服务器环境下开放。
- **已验证环境**：DM 8.1.5.60 / UTF-8，以及 DM 8.1.4.6 / GB18030 / TLS。其他服务器版本需要单独验证。
- **不支持**：原生加密、批量装载、自动重连、分布式事务和部分旧版功能。
- **诊断名称**：`ActivitySource` 和 `Meter` 均使用 `W.DmProvider`，便于沿用现有监控配置。

完整支持边界见[实现与兼容性文档](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/implementation/README.md)。本项目不附带达梦服务端或第三方原生库。

## 文档与开发

- [当前版本发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.3.1.md)
- [路线图与待验证事项](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/roadmap.md)
- [发布指南](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release.md)
- [本地达梦测试环境](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/implementation/local-dameng.md)
- [构建脚本](https://github.com/whynpc9/W.DamengProvider/blob/main/eng/README.md)与[测试配置入口](https://github.com/whynpc9/W.DamengProvider/blob/main/scripts/README.md)

开发构建使用 `global.json` 固定的 .NET SDK 10.0.401，与应用选择的目标运行时分开管理。

## 许可

Apache-2.0。包含上游 DM 的 C# 代码贡献和本项目修改，详见 [THIRD-PARTY-NOTICES.md](https://github.com/whynpc9/W.DamengProvider/blob/main/THIRD-PARTY-NOTICES.md) 与 [LICENSE](https://github.com/whynpc9/W.DamengProvider/blob/main/LICENSE)。
