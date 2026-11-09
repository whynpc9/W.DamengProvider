# W.DamengProvider 0.3.2 发布记录

发布日期为 2026-11-09。正式包已通过 NuGet Trusted Publishing 上传，公开包签名和安装验证通过。[NuGet](https://www.nuget.org/packages/W.DamengProvider/0.3.2) · [GitHub Release](https://github.com/whynpc9/W.DamengProvider/releases/tag/v0.3.2)

## 本版修复

- 远端 CLOB 写入按编码后的字节数分块，保留跨块的多字节字符。[PR #31](https://github.com/whynpc9/W.DamengProvider/pull/31)
- 在窄化整数前检查年月间隔的月份范围，避免越界值回绕后被写入。[PR #30](https://github.com/whynpc9/W.DamengProvider/pull/30)
- 数组绑定在制作执行快照前检查各参数的形状和长度，提前拒绝无效输入。[PR #29](https://github.com/whynpc9/W.DamengProvider/pull/29)
- `DmDataSource` 批次在等待连接和打开连接期间响应取消。[PR #32](https://github.com/whynpc9/W.DamengProvider/pull/32)
- `DmDataSource` 命令和批次在等待连接前快照执行输入。流式参数仍由调用方管理，等待连接期间不会提前读取输入流。[PR #33](https://github.com/whynpc9/W.DamengProvider/pull/33)

包仍提供 `net6.0`、`net8.0`、`net9.0`、`net10.0` 和受限的 `netstandard2.1` 资产。程序集版本保持 `0.1.0.0`。

## 验证范围

五项修复已在 DM 8.1.5.60、UTF-8、显式明文及 .NET 10 环境下完成定向实库验证。该服务器上，直接绑定到固定年月间隔列时仍可能返回驱动拒绝的描述信息。直接绑定限制已在旧版复现，越界修复的实库验证使用 `CASE` 绑定入口。[实库记录](../plans/verification-2026-11-09.md)

## 发布验证

发布提交为 `19bf539520c93ba568e894000c1fa530a5087dde`，标签为 `v0.3.2`。发布工作流执行 52 套离线测试，**10,606/10,606** 通过。五组目标框架资产、八个现代消费者和 28 次首次初始化通过，实际 .NET Core 3.1.32 的 Linux x64 探针通过 174 项断言。[发布工作流](https://github.com/whynpc9/W.DamengProvider/actions/runs/42558767017) · [main CI](https://github.com/whynpc9/W.DamengProvider/actions/runs/42558762315)

从 nuget.org 下载的公开包通过 `dotnet nuget verify --all`。公开包新增签名文件，其余 ZIP 项与发布工作流制品逐字节一致。全新缓存中通过 nuget.org 单一来源安装精确版本，八个现代消费者和 28 次首次初始化通过。实际 .NET Core 3.1.32 的 Linux x64 和 arm64 各通过 174 项离线断言。

四个现代运行时用公开签名包串行通过 8 次明文和严格 TLS 实库检查，运行时版本为 6.0.36、8.0.20、9.0.9、10.0.12。每轮核对 `WDM_PROVIDER_TEST` 用户及同名 Schema，验证参数、Reader、事务、时间值和 BLOB/CLOB，并由新连接确认本轮测试表已清理。五项定向实库探针使用发布前的同提交本地包通过。

包内过程参数推导仍因 TEST 账号缺少 `CREATE PACKAGE` 权限而标记为 `integration_pending`。旧目标实库、完整下游 EF 矩阵及长期资源压力未纳入本版验证。

| 制品 | SHA-256 |
| --- | --- |
| 发布工作流原包 | `acf423b593e647bb7bcdda08c626706a3021feb0fe817113a5bed497316135a0` |
| NuGet 公开签名包 | `0269a13ab8e4f0ea5a0862521ed59ba1e808ec27b7496dee9618ad58bde32e7c` |
