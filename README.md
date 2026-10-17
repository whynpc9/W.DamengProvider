# W.DamengProvider

面向达梦（Dameng）数据库的 .NET 6/8/9/10 ADO.NET 数据提供程序，提供异步数据库操作、参数绑定、本地事务、`DmDataSource`、流式 BLOB/CLOB 和标准 .NET 诊断接口。

这是社区维护的 ADO.NET 项目。包名和程序集名为 **W.DamengProvider**，公开命名空间为 **W.Dm**。它与 `DM.DmProvider` 有不同的程序集身份，现有应用和 EF 适配层需要显式更新依赖并重新编译。

## 安装

当前已发布 [`0.3.0`](https://www.nuget.org/packages/W.DamengProvider/0.3.0)；发布状态、身份、公开安装验证和覆盖范围见[发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.3.0.md)。

```sh
dotnet add package W.DamengProvider --version 0.3.0
```

`0.3.0` 提供 `netstandard2.1;net6.0;net8.0;net9.0;net10.0`；现代四目标包含数组绑定与 DbBatch、存储过程/输出游标、异步元数据和 EF 接入反馈修订，旧资产差异见下文。旧版 `0.1.0-preview.1` 仅包含 `net10.0`。构建使用 `global.json` 固定的 SDK 10.0.401，与应用运行时分开管理。各目标的实测结果见[发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.3.0.md)、[多目标兼容记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/I12-multitarget.md)和 [.NET 6 兼容记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/I13-net6.md)。

`net6.0` 依赖官方 `Microsoft.Bcl.TimeProvider 8.0.1`（传递依赖 `Microsoft.Bcl.AsyncInterfaces 8.0.0`），`net8.0` 依赖官方 `Microsoft.Bcl.Cryptography 10.0.12`（传递依赖 `System.Formats.Asn1 10.0.12`）；`net9.0` 和 `net10.0` 无额外运行时 NuGet 依赖。.NET 6 没有 `DbDataSource`，该目标的 `DmDataSource` 只提供同名 typed 成员，不能经 `DbDataSource`/`DbProviderFactory` 多态使用。按[微软生命周期政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，.NET 6 已于 2024-11-12 结束支持，.NET 8/9 支持期均到 2026-11-10；库兼容验证不延长运行时维护期。

本版本包含 [issue #16 修订及后续调整](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/I16-ef-integration-feedback.md)，包括传输失败分类、事务内 DML 块、时间 typed NULL、已验证 profile 的 CLR 时间值 Scale 8/9 无损写入、有限的 Prepare/绑定失败恢复和 `ServerCapabilities`。本版本还支持 [存储过程、标量输出/返回值与受管输出游标](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/I09-stored-procedures.md)。阅读兼容文档时请区分源码行为与包版本。

`0.3.0` 在现有四目标之外新增受限 `netstandard2.1` 资产，供 .NET Core 3.1 应用及 Standard 2.1 类库消费。该资产不提供 TLS、DbBatch 或 DateOnly/TimeOnly 映射；默认 `RequireTls` 不变，仅明确允许明文的服务器与显式 `PlaintextAllowed` 组合可以连接。范围见[旧目标兼容记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/core31.md)，本轮检查见[0.3.0 发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.3.0.md)。已发布 `0.2.0` 不含旧资产。

## 使用

连接串由应用配置或 secret store 提供。默认要求完整会话 TLS，并验证证书；根据服务器配置指定 CA、客户端证书和私钥路径。

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

需要 `DbProviderFactories` 时，可由应用调用 `DbProviderFactories.RegisterFactory("W.DamengProvider", DmClientFactory.Instance)` 注册。

## 支持与限制

- 支持同步及异步 Open、Prepare、Execute、Read、NextResult 和本地事务。取消或超时发生于发送之后时，可能关闭物理连接；未确认的提交通过 `OutcomeUnknown` 报告，不自动重试或重放。
- 默认 `ReadCommitted`。保存点及额外隔离级别依赖服务器 profile；详细边界见[事务兼容说明](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/T10-transactions.md)。
- `DmDataSource` 提供独立容量调度；当前每次租用建立新物理会话、归还即丢弃，尚不提供物理连接复用的性能收益。
- BLOB/CLOB 支持与当前 Reader 行、执行和事务绑定的流式读取。完整物化 API 限制为 64 MiB 返回载荷。流式输入需显式声明 BLOB/CLOB 类型，由调用者管理输入生命周期。
- CLR 时间值保持 100ns tick 精度；服务器或目标列不能精确表示的值会被拒绝。七位小数写入仅在已验证 profile 开放，详见[时间兼容说明](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/TP-temporal-precision.md)。
- 已验证环境包括 DM 8.1.5.60 / UTF-8 和 DM 8.1.4.6 / GB18030 / TLS；其他服务器版本需单独验证。原生加密、批量装载、自动重连、分布式事务及部分旧功能不在支持范围。
- `ActivitySource` 和 `Meter` 的名称继续使用 `W.DmProvider`，便于现有监控配置延续。

完整边界见[R3 兼容说明](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/compatibility/R3.md)。本项目不附带数据库服务端或第三方原生库。

## 开发与发布

发布准备、测试和 tag 规则见[发布指南](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release.md)。本地实库测试仅使用专用 TEST 账号，配置见[本地达梦环境](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/implementation/local-dameng.md)。

后续缺口及已实现能力见[路线图](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/roadmap.md)。开发脚本入口见 [eng](https://github.com/whynpc9/W.DamengProvider/blob/main/eng/README.md) 和 [scripts](https://github.com/whynpc9/W.DamengProvider/blob/main/scripts/README.md)。

## 许可

Apache-2.0。包含上游 DM 的 C# 代码贡献和本项目的修改，版权及第三方说明见 [THIRD-PARTY-NOTICES.md](https://github.com/whynpc9/W.DamengProvider/blob/main/THIRD-PARTY-NOTICES.md) 与 [LICENSE](https://github.com/whynpc9/W.DamengProvider/blob/main/LICENSE)。
