# W.DamengProvider 0.3.1

状态：2026-10-28 已发布至 NuGet 与 GitHub；公开安装和签名验证通过。包内过程实库覆盖仍有明确待验证项。

## 变更范围

基于 PR #27（合并提交 `9c1eb577c97b0fe1be234ec78578d7381ae0d80b`）的五项修复：

- 修正远端 BLOB/CLOB 截断长度，避免长度被窄化。
- 修正公开 BLOB Stream 的读取、定位、写入、长度及释放契约。
- 顺序读取时的空值检查不再消耗当前字段的值。
- DbBatch Reader 保留 provider-specific 类型和值。
- 安全解析过程标识符并原子替换推导参数；元数据失败保留原参数，函数返回值与事务绑定保持一致。

目标框架与 0.3.0 相同：受限 netstandard2.1，以及 net6.0/net8.0/net9.0/net10.0。旧资产的 TLS、DbBatch、DateOnly/TimeOnly 限制不变。

## 验证

候选身份：`main@9c1eb577c97b0fe1be234ec78578d7381ae0d80b` 加本次发布准备 diff（项目版本 0.3.1、文件版本 0.3.1.0，程序集版本保持 0.1.0.0，README 增加准备说明）。包中 RepositoryCommit 指向合并基线，不单独代表尚未提交的准备修改。

2026-10-28 完整准备入口退出 0：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.3.1 --commit "$(git rev-parse HEAD)" --output .local/release-0.3.1
```

- 发布工具测试 23/23 通过。
- 四现代目标 52 份 TRX，共 **10094/10094**；net6.0 为 2525，其余三个目标各 2523，失败、跳过、中止均为 0。
- 五资产组及 25 个托管程序集、依赖/许可证/包身份检查通过；八个独立精确版本包消费者和 28 次首次初始化通过。
- 独立只读审查已覆盖五项产品修复及新增测试，未发现发布阻断。
- NuGet 公开源依赖审计未报告已知漏洞；不据此扩大产品安全声明。

准备证据位于 ignored `.local/release-0.3.1/nuget-7wy013op/`，顶层 `package-path.txt` 与 `manifest-path.txt` 指向本次候选，命令与退出码保存在 `commands.jsonl`。本次结果不借用历史分支或 0.3.0 验证。

独立验证另完成 13 条命令，均退出 0，证据位于 `.local/release-0.3.1/independent-validation/`（`commands.json`、`validation-summary.json`、`offline-summary.json`）：

- 实际 .NET Core 3.1.32 的 Linux x64/arm64 各 8 组、174 断言通过，精确包消费者选中同一候选的 netstandard2.1 资产。
- 现代四运行时的 shared/严格 TLS 共 **8/8** smoke 通过，实际 runtime 为 6.0.36/8.0.20/9.0.9/10.0.12。全部验证 `WDM_PROVIDER_TEST` 用户及同名 Schema、参数/Reader/时间/LOB/事务；本轮精确对象均由新连接确认 `owned_table_absent`，提交/回滚完成且连接关闭。明文显式 PlaintextAllowed，TLS RequireTls 并验证证书；本机无 CRL CA 显式 NoCheck，产品默认 Online 不变。
- 从候选精确 PackageReference 消费的 `DeriveParametersProbe --standalone-only` 通过 9 项独立过程、函数、带引号名称、拒绝后复用与事务场景；加载 DLL 与候选 net10 资产逐字节一致。4 个本轮精确对象全部清理，`cleanup_verified=true`。

候选包 SHA256：`8e1135b1e7931de24a3b47e43091c086b9f7a9044f7609a196631080d46d13de`。旧目标本轮仅完成离线包探针，未进行 Core 3.1 实库检查；不扩大到完整下游 EF、长期压力或其他服务器 profile。

包内过程的真实库验证需要 TEST 的 CREATE PACKAGE 权限；当前任务不变更数据库授权，相关七项保持 `integration_pending`。以上为准备阶段候选的历史记录；正式提交及公开验证见下文。

## 正式发布与公开验证

- 发布提交：`a71ef401084eacfdb9593d10e1e245a005d43d57`；[tag v0.3.1](https://github.com/whynpc9/W.DamengProvider/tree/v0.3.1)。
- [普通 CI 40631308138](https://github.com/whynpc9/W.DamengProvider/actions/runs/40631308138) 和 [tag 发布 40631797644](https://github.com/whynpc9/W.DamengProvider/actions/runs/40631797644) 均成功；各 52 份 TRX，共 **10094/10094**，失败、跳过、中止均为 0。tag 的五资产组、八现代消费者、28 次初始化和实际 Core 3.1 x64 174 断言通过。Actions 现代 runtime 为 6.0.36/8.0.33/9.0.22/10.0.14。
- 正式提交的本地完整 `prepare` 及独立验证再次通过：同包现代 8 次 shared/TLS smoke、9 项独立过程推导及精确清理，Core 3.1 双架构各 174 断言。正式本地包 SHA256 为 `a0eb396b79958cbe2a4b8d552de2e56c42435aefa156d5711e6f5430094038f4`；记录在 ignored `.local/release-0.3.1-final/nuget-9_s0irwl/` 和 `independent-validation/`。本地结果不冒充 Actions 载荷实库结果。
- [GitHub Release](https://github.com/whynpc9/W.DamengProvider/releases/tag/v0.3.1) 为正式版；[NuGet 0.3.1](https://www.nuget.org/packages/W.DamengProvider/0.3.1) 已公开。上传日志明确返回 Created 和 Your package was pushed；等待索引传播后完成公开检查，没有重复上传。

Actions 原包 SHA256：`f422a186eea7c2920216550486571f3b61da0692f5b3a55385294b0ae984a8f2`。

公开签名包 SHA256：`c0641bf1e77cc8da6bb924c03ee8c172f504187bac4d4e04784a60221661318c`。

`dotnet nuget verify --all` 退出 0；公开包仅新增 `.signature.p7s`，其余 ZIP 项与 Actions 原包逐字节相同。仅使用 nuget.org 唯一源及各自全新缓存，以精确 `[0.3.1]` 安装八个现代消费者与一个 Core 3.1 消费者；已核对缓存签名包、所选资产和加载 DLL。现代四 runtime 为 6.0.36/8.0.20/9.0.9/10.0.12，包消费/文化回退和 28 次首次初始化通过；实际 Core 3.1.32 x64/arm64 各 174 断言通过。本次公开检查 29 条命令均退出 0；证据在 ignored `.local/release-0.3.1-final/public/`，Actions 原制品在 `actions/`。

公开正式签名载荷独立实库验证 **11 条命令全部退出 0**：四现代 runtime 的 shared/严格 TLS **8/8**，每份报告均核对上方公开包 SHA256、`WDM_PROVIDER_TEST` 用户及 Schema，提交/回滚完成、连接关闭，精确本轮表由新连接确认 `owned_table_absent`。唯一 nuget.org 源、全新缓存的参数推导消费者核对签名包和 net10 加载 DLL，9 项独立过程场景通过，4 个本轮精确对象清理确认。记录在 `public-integration/commands.json` 和 `validation-summary.json`。

独立审查和独立验证均通过。包内过程相关 7 项继续为 `integration_pending`（TEST 无 CREATE PACKAGE 权限），本轮未运行旧目标实库、完整下游 EF 或长期压力；旧资产 TLS 不支持。历史 0.3.0 的旧目标实库结果不归入本次发布验收。
