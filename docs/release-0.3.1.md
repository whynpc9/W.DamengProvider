# W.DamengProvider 0.3.1

状态：本地候选发布准备与独立验证完成；包内过程实库覆盖仍有明确待验证项。尚未创建 tag 或上传 NuGet。

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

包内过程的真实库验证需要 TEST 的 CREATE PACKAGE 权限；当前任务不变更数据库授权，相关七项保持 `integration_pending`。正式发布前须提交并合入准备修改，在实际发布提交上完成发布准备及相关实库验证；本页不表示已创建 tag、上传 NuGet 或验证公开安装。
