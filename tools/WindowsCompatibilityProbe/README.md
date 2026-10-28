# Windows 环境与最终状态只读探针

此探针通过精确 PackageReference 消费指定包，记录 Windows 环境、服务器公开信息和最终对象状态。它不创建或删除数据库对象，也不加载管理员配置。

## 构建

必须指定 `R3PackageVersion` 和 `R3PackageSource`，并使用仓库统一的 .NET 环境和单节点参数：

```powershell
dotnet build tools/WindowsCompatibilityProbe/WindowsCompatibilityProbe.csproj -c Release `
  /p:R3PackageVersion=<version> /p:R3PackageSource=<feed> `
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

## 运行

通过 TEST wrapper 启动：

```powershell
scripts/with-dameng-test.ps1 -Command dotnet -CommandArguments @('<probe.dll>', '<report.json>')
```

默认只接受 `WDM_PROVIDER_TEST`。复现 issue #11 的独立测试账号时，需显式传 `-ExpectedUser WDM_PROVIDER_TEST_I11_<8位大写HEX>`。

## 观察内容

- 实际 OS、RID、运行时、服务器版本、编码、消息协议和公开能力。
- 当前 Schema 的对象计数；每次成功连接都查询 USER 和 Schema。
- 元数据使用单独连接查询；权限不足时记录安全失败，不能写成查询成功。
- 在显式明文环境中另用 `RequireTls` 打开连接，确认服务器未协商 TLS 时会被策略拒绝。这个拒绝不代表 TLS 握手成功。

本轮结果和未覆盖范围见[Windows 兼容记录](../../docs/compatibility/I11-windows.md)。
