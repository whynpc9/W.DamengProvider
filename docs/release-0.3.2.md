# W.DamengProvider 0.3.2 发布草稿

本版包含五项修复。当前公开版本仍为 [0.3.1](release-0.3.1.md)。

## 本版修复

- 远端 CLOB 写入按编码后的字节数分块，保留跨块的多字节字符。[PR #31](https://github.com/whynpc9/W.DamengProvider/pull/31)
- 在窄化整数前检查年月间隔的月份范围，避免越界值回绕后被写入。[PR #30](https://github.com/whynpc9/W.DamengProvider/pull/30)
- 数组绑定在制作执行快照前检查各参数的形状和长度，提前拒绝无效输入。[PR #29](https://github.com/whynpc9/W.DamengProvider/pull/29)
- `DmDataSource` 批次在等待连接和打开连接期间响应取消。[PR #32](https://github.com/whynpc9/W.DamengProvider/pull/32)
- `DmDataSource` 命令和批次在等待连接前快照执行输入。流式参数仍由调用方管理，等待连接期间不会提前读取输入流。[PR #33](https://github.com/whynpc9/W.DamengProvider/pull/33)

包仍提供 `net6.0`、`net8.0`、`net9.0`、`net10.0` 和受限的 `netstandard2.1` 资产。程序集版本保持 `0.1.0.0`。

## 验证范围

五项修复已在 DM 8.1.5.60、UTF-8、显式明文及 .NET 10 环境下完成定向实库验证。该服务器上，直接绑定到固定年月间隔列时仍可能返回驱动拒绝的描述信息。直接绑定限制已在旧版复现，越界修复的实库验证使用 `CASE` 绑定入口。[实库记录](../plans/verification-2026-11-09.md)

本版的 tag 发布工作流使用 NuGet Trusted Publishing。公开包签名、公开源安装和发布结果在上传后记录。[发布指南](release.md)
