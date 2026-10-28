# .NET Core 3.1 / .NET Standard 2.1 支持范围

`netstandard2.1` 目标框架自 [0.3.0](../release-0.3.0.md) 起加入，供 .NET Core 3.1 应用和 Standard 2.1 类库使用。当前发布版本为 [0.3.1](../release-0.3.1.md)，构建仍使用 `global.json` 固定的 SDK 10.0.401 和 C# 12。

## 与现代目标框架的差异

| 功能 | `netstandard2.1` 行为 |
| --- | --- |
| 数据源与 ADO.NET | 保留强类型 `DmDataSource`、同步/异步命令、数组绑定、事务和元数据。旧标准缺少基类成员时，保存点、异步 Schema、`IsTransient` 等通过具体 `Dm*` 类型提供。 |
| `DbBatch` | 不提供相关类型，也不提供工厂或数据源的批处理入口。现代四个目标框架继续支持标准 `DbBatch`。 |
| TLS | **不支持。** 需要 TLS 的路径抛 `PlatformNotSupportedException`。默认 `RequireTls` 不变，不会自动降级为明文。 |
| 日期类型 | 不提供 DateOnly/TimeOnly 映射；保留 DateTime、DateTimeOffset、TimeSpan 的既有语义。TimeSpan 仍用于 INTERVAL，不隐式变成 TIME。 |
| DNS 取消 | 只能取消等待，底层解析可能继续完成；迟到异常需要被观察。建连取消会关闭本次 socket，取消和超时的首因分类保持一致。 |

TLS 限制由目标框架决定，与运行该程序集的实际运行时无关。只有服务器明确允许明文，且应用显式选择 `PlaintextAllowed` 时，才能建立连接：

```csharp
var settings = new DmConnectionStringBuilder(applicationConnectionString)
{
    TransportSecurity = DmTransportSecurity.PlaintextAllowed
};
await using var source = new DmDataSource(settings.ConnectionString);
await using var connection = await source.OpenConnectionAsync();
```

## 依赖

`netstandard2.1` 额外引用：

- `Microsoft.Bcl.TimeProvider 8.0.1`
- `System.Diagnostics.DiagnosticSource 8.0.1`
- `System.Threading.Channels 8.0.0`
- `System.Text.Json 8.0.6`
- `System.Text.Encoding.CodePages 8.0.0`

`net6.0` / `net8.0` 的原有依赖不变。部分兼容包会提示 .NET Core 3.1 未受支持或未经测试；能够恢复和运行受限探针，不代表恢复了上游运行时维护。

官方 `Dm.*` 类型、EF Provider 和原生 BulkCopy 仍需下游单独适配，本目标框架不是官方包的直接替换。

## 当前验证范围

- [0.3.0 正式包](../release-0.3.0.md) 已在实际 .NET Core 3.1.32 的 Linux arm64/x64 上分别通过 174 项离线断言，并完成 Linux arm64 共享 TEST 明文 smoke 和精确清理。
- 0.3.0 的 x64 实库仍为 `integration_pending`；旧目标框架的 TLS 始终不支持。
- [0.3.1](../release-0.3.1.md) 对旧目标框架只完成离线包检查，没有重新进行 .NET Core 3.1 实库验证。

这些结果不覆盖完整的旧目标框架存储过程、元数据实库矩阵、下游 EF 或长期压力。

## 原实现验证（历史记录）

以下记录解释实现过程，结果属于当时的本地候选包，不代表最新发布状态。

### 首轮候选

2026-10-16，基线为 `06616b6` 加当时未提交修改，本地包版本为 `0.2.1-core31.1`，未创建 tag 或上传 NuGet。

```sh
DOTNET_COMMAND=/usr/local/share/dotnet/dotnet DOTNET_ROOT=/usr/local/share/dotnet DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
  python3 -B tools/NuGetRelease/release.py prepare --tag v0.2.1-core31.1 \
  --commit 06616b6dbf8cb4c65d4ddc015858e43345e7f48d --output .local/core31-validation
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
```

结果：

- `prepare` 退出 0；SDK 10.0.401，实际运行时为 6.0.36、8.0.20、9.0.9、10.0.12。
- 13 套测试 × 4 个现代目标框架，52 份 TRX 共 **9634/9634** 通过；`net6.0` 为 2410，其余各 2408。
- 发布工具 23/23 通过；五组目标框架、25 个托管程序集、八个现代消费者和 28 个首次初始化场景通过。
- 结果位于 `.local/core31-validation/nuget-3qius97w`。

`tools/LegacyCompatibilityProbe` 通过独立 PackageReference 消费候选包，确认编译和运行均选中 `lib/netstandard2.1/W.DamengProvider.dll`，四套文化资源齐全。

实际 .NET Core 3.1.32 Debian bullseye 容器中，arm64 原生运行、x64 模拟运行，均未使用 roll-forward。两个架构各通过 **7 组、167 项断言**，覆盖：

- 数据源生命周期和容量归还。
- DateTime/TimeSpan、时间协议值和日期边界。
- 数组快照、NULL 推断、错误长度拒绝、保存点和异步 Schema 具体类型入口。
- 真实 TCP 同步/异步收发、取消、超时和关闭。
- 同步/异步 × STARTUP 0/1/4 × RequireTls/PlaintextAllowed 的 12 种组合。
- 旧批 API 缺失、诊断初始化和队列 flush。

日志位于 `.local/framework-research-20261016/logs/`，独立消费者位于 `.local/core31-package-probe`。六条上游兼容包警告保留。

早期 ProjectReference 探针曾在 arm64  entry DLL 加载时遇到 `BadImageFormatException`，程序尚未进入 Main；同一输出再次读取后通过，最终独立 PackageReference 双架构也通过。首次失败原因未确定，原始日志保留。早期探针对 INTERVAL scale、Normalize 返回类型和预取消状态的错误假设只修正了测试，没有修改产品。

### PR #26 评审修订（2026-10-17）

评审发现旧目标框架的同步 `Open()` 在 DNS 忽略取消令牌时可能超过期限，且 `Close()` 无法解除等待。修复只在 `NETSTANDARD2_1` 同步路径使用既有 `WaitAsync(dnsDeadline.Token)`，现代目标框架不变。

新增同步 pending resolver 的短期限、并发 Close、零 TCP 创建和关闭状态用例。测试观察注入的迟到异常，避免影响其他测试；这不等于声称产品会观察所有迟到异常。

新本地包 `0.2.1-core31.2` 的 `pack-smoke` 退出 0；实际 .NET Core 3.1.32 arm64/x64 各通过 **8 组、174 项断言**。证据位于 `.local/pr-core31/fix-validation/nuget-s0wj6w7b` 和 `.local/pr-core31/package2/logs`。

该修订未重新执行本地完整四运行时套件，当时远端 CI 也尚未对新提交运行，因此不能沿用旧提交的 CI 结果。当时没有真实数据库验证，状态为 `integration_pending`；后续正式包结果以[0.3.0 发布记录](../release-0.3.0.md)为准。
