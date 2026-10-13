# W.DamengProvider

面向达梦（Dameng）数据库的 .NET 10 ADO.NET 数据提供程序，提供异步数据库操作、参数绑定、本地事务、`DmDataSource`、流式 BLOB/CLOB 和标准 .NET 诊断接口。

这是社区维护的预览项目。包名和程序集名为 **W.DamengProvider**，公开命名空间为 **W.Dm**。它与 `DM.DmProvider` 有不同的程序集身份，现有应用和 EF 适配层需要显式更新依赖并重新编译。

## 安装

当前已发布预览版本为 [`0.1.0-preview.1`](https://www.nuget.org/packages/W.DamengProvider/0.1.0-preview.1)（2026-10-13）。发布身份、公开安装验证和覆盖范围见[发布记录](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/release-0.1.0-preview.1.md)。

```sh
dotnet add package W.DamengProvider --version 0.1.0-preview.1
```

运行环境为 .NET 10；构建使用仓库 `global.json` 指定的 SDK。

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

后续功能与版本兼容工作见[路线图](https://github.com/whynpc9/W.DamengProvider/blob/main/docs/roadmap.md)。路线图中的能力尚未随当前包提供。

## 许可

Apache-2.0。包含上游 DM 的 C# 代码贡献和本项目的修改，版权及第三方说明见 [THIRD-PARTY-NOTICES.md](https://github.com/whynpc9/W.DamengProvider/blob/main/THIRD-PARTY-NOTICES.md) 与 [LICENSE](https://github.com/whynpc9/W.DamengProvider/blob/main/LICENSE)。
