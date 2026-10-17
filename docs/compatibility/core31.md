# .NET Core 3.1 / .NET Standard 2.1 兼容原型

本分支在既有 `net6.0;net8.0;net9.0;net10.0` 之外增加 `netstandard2.1` 资产，供保持 Standard 2.1 目标的 UniWeb 类库和 .NET Core 3.1 应用消费。原型尚未发布，已发布的 0.2.0 不含该资产。构建仍使用 global.json 指定 SDK 10.0.401 与 C#12。

## 能力边界

- 旧资产保留 typed `DmDataSource`、同步/异步 ADO.NET 操作、数组绑定及现有事务和元数据实现。保存点、异步 schema、IsTransient 等在旧标准缺少对应基类成员时作为 `Dm*` 具体类型成员提供。
- 旧资产不提供 DbBatch 相关类型及工厂/数据源批处理入口。现有四目标继续提供标准 DbBatch。
- 旧资产不提供 TLS。所有需要 TLS 握手的路径明确抛出 `PlatformNotSupportedException`；默认 `RequireTls` 不变，不能把未支持的 TLS 降级为明文。只有服务器明文模式与显式 `PlaintextAllowed` 组合可以建立明文会话。限制按资产编译，不按运行该资产的 runtime 猜测。
- 旧资产不提供 DateOnly/TimeOnly 映射，保留 DateTime、DateTimeOffset 和 TimeSpan 的既有类型语义。TimeSpan 仍用于 INTERVAL，不隐式变成 TIME。内部日期计算保持公历、闰年、日编号和日期投影规则。
- 旧目标 DNS 只能取消等待；底层解析可能继续完成，迟到异常需观察。建连取消关闭本次 candidate socket，取消与 deadline 的首因分类保持一致。
- `Microsoft.Bcl.TimeProvider 8.0.1`、`System.Diagnostics.DiagnosticSource 8.0.1`、`System.Threading.Channels 8.0.0`、`System.Text.Json 8.0.6`、`System.Text.Encoding.CodePages 8.0.0` 仅为旧资产补足引用；net6/net8 的原依赖保持不变。部分包会提示 Core 3.1 未受支持/未测试，Standard 资产可恢复不等于 runtime 验收。

旧资产连接到已明确允许明文的服务器时，应用需显式设置：

```csharp
var settings = new DmConnectionStringBuilder(applicationConnectionString)
{
    TransportSecurity = DmTransportSecurity.PlaintextAllowed
};
await using var source = new DmDataSource(settings.ConnectionString);
await using var connection = await source.OpenConnectionAsync();
```

现有四目标的 TLS、DbBatch、日期类型和公开基类接口保持原契约。UniWeb 的官方 `Dm.*` 类型、EF Provider 和原生 BulkCopy 仍需下游单独适配，本原型不表示可直接替换官方包。

## 验证

2026-10-16，基线 `06616b6` 加本分支未提交 diff。产品代码稳定后执行以下检查，候选包为本地 `0.2.1-core31.1`，没有提交 tag 或上传 NuGet。

### 现有四目标回归

```sh
DOTNET_COMMAND=/usr/local/share/dotnet/dotnet DOTNET_ROOT=/usr/local/share/dotnet DOTNET_GENERATE_ASPNET_CERTIFICATE=false \
  python3 -B tools/NuGetRelease/release.py prepare --tag v0.2.1-core31.1 \
  --commit 06616b6dbf8cb4c65d4ddc015858e43345e7f48d --output .local/core31-validation
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
```

`prepare` 退出 0；SDK 10.0.401，实际运行时分别为 6.0.36、8.0.20、9.0.9、10.0.12。13 套件 × 4 运行时的 52 份 TRX 共 **9634/9634** 通过，失败/跳过均为 0：net6 2410，其他各 2408。Python 发布工具契约 23/23 通过。五目标主程序集与四文化资源共 25 个 managed assets 的包检查通过，四现代目标的八个独立包消费者、文化回退与 28 个首次初始化场景通过。结果位于 `.local/core31-validation/nuget-3qius97w`。

### 实际 3.1 运行时与包消费

`tools/LegacyCompatibilityProbe` 使用独立临时项目，仅通过 PackageReference 恢复该候选包；assets 确认 compile/runtime 均为 `lib/netstandard2.1/W.DamengProvider.dll`，四文化资源齐全。容器为官方 .NET Core **3.1.32** Debian bullseye 镜像：aarch64 在本机 Linux VM 原生执行，x64 在同机模拟执行；未使用 roll-forward。

两个架构均退出 0，各 **7 组、167 个断言**：

- typed DataSource 打开失败释放、容量归还、释放后拒绝新租约、已交付租约关闭；
- DateTime/TimeSpan 参数语义、timestamp wire、0001/闰日/9999 与日编号/日期投影边界；
- 数组 plan 快照、NULL 同列推断和错误长度拒绝，保存点/异步 schema 具体类型入口；
- 真实 TCP 同步/异步收发、取消、deadline、关闭解除挂起，以及 pending DNS 等待取消/超时；
- 同步/异步 × STARTUP 0/1/4 × RequireTls/PlaintextAllowed 的 12 个组合：拒绝时服务端观察 EOF、无 LOGIN/TLS hello，仅显式明文 + mode 0 进入 LOGIN；
- 旧批 API 缺失、诊断依赖初始化与队列 flush。

恢复/构建与双架构输出：`.local/framework-research-20261016/logs/legacy-probe-package-{restore,build,arm64,amd64}.log`；独立消费者目录 `.local/core31-package-probe`。构建保留六条上游包对 netcoreapp3.1 的“未支持/未测试”警告，不把上述有限运行结果扩大为上游支持承诺。

早期 ProjectReference 探针有一次 aarch64 entry DLL 载入 `BadImageFormatException`，程序尚未进入 Main；同一输出未重编后再次读取，host/container DLL 一致且 167 断言通过，最终独立 PackageReference 双架构也通过。首次载入原因未确定，原始与 readback 日志保留，不能把 bind 可见性推测当作已证实原因。早期 probe 对 INTERVAL scale、Normalize 返回类型和预取消 raw transport 状态的错误假设已按既有合同修正，未因此修改产品。

独立代码审查未发现阻塞问题。CI 新增实际 3.1 linux-x64 包消费步骤；本轮只执行本地等价命令，未运行远程 GitHub Actions。

无真实数据库验证，状态为 `integration_pending`。Linux 发行版与 OpenSSL 依赖需按部署环境核对；本原型不恢复已结束维护的 .NET Core 3.1 运行时支持。
