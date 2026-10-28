# W.DamengProvider 0.3.1 发布记录

发布日期：2026-10-28。正式版已发布到 NuGet 和 GitHub，公开安装、包签名及已列出的实库检查均通过。

## 本版修复

本版包含 PR #27 的五项修复：

- 修正远端 BLOB/CLOB 长度被窄化而导致的截断。
- 修正公开 BLOB `Stream` 的读取、定位、写入、长度和释放行为。
- 顺序读取时，检查空值不再消耗当前字段的值。
- `DbBatch` Reader 现在保留驱动特定的类型和值。
- 过程标识符解析和参数推导更加安全：推导失败时保留原参数，函数返回值与事务绑定保持一致。

目标框架与 0.3.0 相同：`net6.0`、`net8.0`、`net9.0`、`net10.0` 和受限的 `netstandard2.1`。旧目标框架仍不支持 TLS、`DbBatch`、DateOnly/TimeOnly 映射。

## 尚未覆盖的范围

- 包内过程的 7 项实库测试仍为 `integration_pending`，原因是 TEST 账号没有 `CREATE PACKAGE` 权限；本次发布未变更数据库授权。
- `netstandard2.1` 本版只完成离线包检查，没有进行 .NET Core 3.1 实库测试。
- 完整下游 EF 矩阵、长期资源压力和其他服务器环境不在本次验证范围内。

## 正式发布信息

| 项目 | 值 |
| --- | --- |
| 发布提交 | `a71ef401084eacfdb9593d10e1e245a005d43d57` |
| 发布标签 | [v0.3.1](https://github.com/whynpc9/W.DamengProvider/tree/v0.3.1) |
| GitHub Release | [W.DamengProvider 0.3.1](https://github.com/whynpc9/W.DamengProvider/releases/tag/v0.3.1) |
| NuGet | [W.DamengProvider 0.3.1](https://www.nuget.org/packages/W.DamengProvider/0.3.1) |
| 普通 CI | [40631308138](https://github.com/whynpc9/W.DamengProvider/actions/runs/40631308138)，成功 |
| 发布工作流 | [40631797644](https://github.com/whynpc9/W.DamengProvider/actions/runs/40631797644)，成功 |
| Actions 原包 SHA-256 | `f422a186eea7c2920216550486571f3b61da0692f5b3a55385294b0ae984a8f2` |
| NuGet 公开签名包 SHA-256 | `c0641bf1e77cc8da6bb924c03ee8c172f504187bac4d4e04784a60221661318c` |

两个工作流分别执行 52 份 TRX，共 **10094/10094** 通过，失败、跳过和中止均为 0。发布工作流还通过五组目标框架资产、八个现代消费者、28 次首次初始化，以及实际 .NET Core 3.1 x64 的 174 项断言。Actions 使用的现代运行时版本为 6.0.36、8.0.33、9.0.22、10.0.14。

## 公开包验证

`dotnet nuget verify --all` 退出 0。公开包相较 Actions 原包只新增 `.signature.p7s`，其余 ZIP 项逐字节相同。

公开验证使用唯一 nuget.org 源、全新缓存和精确版本 `[0.3.1]`：

- 八个现代消费者和一个 .NET Core 3.1 消费者均安装成功，并核对了签名包、所选目标框架和实际加载的 DLL。
- 现代运行时 6.0.36、8.0.20、9.0.9、10.0.12 上的包消费、文化资源回退和 28 次首次初始化通过。
- 实际 .NET Core 3.1.32 的 Linux x64/arm64 各通过 174 项断言。
- 公开检查共 29 条命令，全部退出 0。证据位于本机忽略目录 `.local/release-0.3.1-final/public/`，Actions 原制品位于其 `actions/` 子目录。

公开签名包另完成 **11 条实库验证命令，全部退出 0**：

- 四个现代运行时分别执行共享明文和严格 TLS 检查，共 **8/8** 通过。每份报告均核对公开包 SHA-256、`WDM_PROVIDER_TEST` 用户和同名 Schema，并确认事务结束、连接关闭、本轮表已由新连接验证不存在。
- 参数推导消费者通过 9 项独立过程场景，核对签名包和 `net10.0` 加载 DLL，并确认本轮 4 个对象全部清理。

实库报告位于 `public-integration/commands.json` 和 `validation-summary.json`。本地结果与 Actions 结果分别记录，没有相互替代。

## 发布前验证（历史记录）

准备阶段基于 `main@9c1eb577c97b0fe1be234ec78578d7381ae0d80b`，另含版本和 README 发布修改。项目版本为 0.3.1、文件版本为 0.3.1.0，程序集版本保持 0.1.0.0。包中的 `RepositoryCommit` 指向合并基线，不能单独代表当时尚未提交的修改。

2026-10-28 执行以下命令，完整准备入口退出 0：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.3.1 --commit "$(git rev-parse HEAD)" --output .local/release-0.3.1
```

准备结果：

- 发布工具测试 **23/23** 通过。
- 四个现代目标框架的 52 份 TRX 共 **10094/10094** 通过；`net6.0` 为 2525，其余三个目标各 2523，失败、跳过和中止均为 0。
- 五组目标框架资产及 25 个托管程序集通过依赖、许可证和包身份检查；八个独立精确版本消费者和 28 次首次初始化通过。
- 独立只读审查覆盖五项修复和新增测试，未发现发布阻断。
- NuGet 公开源依赖审计未报告已知漏洞；这只是当时的依赖检查结果，不代表对其他安全问题的保证。

准备证据位于本机忽略目录 `.local/release-0.3.1/nuget-7wy013op/`。`package-path.txt` 与 `manifest-path.txt` 指向本次包，命令和退出码保存在 `commands.jsonl`。

独立验证另执行 13 条命令，全部退出 0，证据位于 `.local/release-0.3.1/independent-validation/`：

- 实际 .NET Core 3.1.32 的 Linux x64/arm64 各通过 8 组、174 项断言，消费者选中同一候选包的 `netstandard2.1` 资产。
- 四个现代运行时的共享明文和严格 TLS 检查共 **8/8** 通过，运行时版本为 6.0.36、8.0.20、9.0.9、10.0.12。每轮均核验 TEST 身份，覆盖参数、Reader、时间、LOB 和事务，并由新连接确认本轮对象已清理。
- `DeriveParametersProbe --standalone-only` 通过 9 项独立过程、函数、带引号名称、拒绝后复用和事务场景；加载 DLL 与候选 `net10.0` 资产一致，本轮 4 个对象全部清理，`cleanup_verified=true`。

明文检查显式使用 `PlaintextAllowed`；TLS 检查使用 `RequireTls` 并验证证书。本机无 CRL 的测试 CA 显式使用 `NoCheck`，产品默认的 `Online` 策略不变。

准备阶段候选包 SHA-256 为 `8e1135b1e7931de24a3b47e43091c086b9f7a9044f7609a196631080d46d13de`。正式提交的本地完整准备和独立验证也再次通过；正式本地包 SHA-256 为 `a0eb396b79958cbe2a4b8d552de2e56c42435aefa156d5711e6f5430094038f4`，证据位于 `.local/release-0.3.1-final/nuget-9_s0irwl/` 和 `independent-validation/`。
