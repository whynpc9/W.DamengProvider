# Windows profile / 最终状态观察

只读探针，精确 PackageReference 消费本轮包。构建需指定 `R3PackageVersion`、`R3PackageSource`，沿用仓库的 .NET 环境及单节点参数。

```powershell
dotnet build tools/WindowsCompatibilityProbe/WindowsCompatibilityProbe.csproj -c Release `
  /p:R3PackageVersion=<version> /p:R3PackageSource=<feed> `
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

通过 `scripts/with-dameng-test.ps1` 启动 `dotnet <probe.dll> <report.json>`。默认只接受 `WDM_PROVIDER_TEST`；issue #11 的独立测试账号需在 wrapper 显式传 `-ExpectedUser WDM_PROVIDER_TEST_I11_<8位大写HEX>`。

记录实际 OS/RID/运行时、服务器版本、编码、消息协议、公开能力、当前 Schema 对象计数；成功连接逐次查询 USER 与 Schema。元数据查询单独连接，权限不足时记录安全 failure，不能宣称获取成功。显式明文 profile 下另用 RequireTls 实际打开连接，必须在服务器未协商 TLS 时被策略拒绝。该拒绝不能推导 TLS 握手成功。探针不创建或删除数据库对象，也不加载管理员配置。

本轮结果与已知未覆盖范围见 [Windows 兼容记录](../../docs/compatibility/I11-windows.md)。
