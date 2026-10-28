# R1 审查精确包回归探针

此工具通过精确 PackageReference 消费 `W.DamengProvider` 包，禁止 ProjectReference。用于分别记录旧包基线缺陷和修复候选行为，两个输出不能混用。

## 构建与运行

使用新的隔离包源和输出目录，传 `-p:R1ReviewPackageVersion=<exact-version>`；报告核对实际 DLL informational version，并保存 SHA256/MVID。旧包与修复候选使用同一份冻结工具源码。

所有 dotnet 操作先设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`；restore/build 使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`，支持时添加 `--disable-build-servers`。

```text
scripts/with-dameng-test.sh <dotnet-path> <R1ReviewProbe.dll> baseline|fixed <safe-report.json>
```

只读取 TEST wrapper 的唯一变量；缺少时返回 `integration_pending`、退出码 2。连接显式 `PlaintextAllowed`、`PersistSecurityInfo=false`，连接和命令超时 20 秒。每条新连接在业务操作前验证 USER、当前 Schema 都是 `WDM_PROVIDER_TEST`，ServerVersion 为 8.1.5.60。

## baseline

记录 NULL int、CLOB 长度/部分/零长度/剩余读取、forward gap、Prepare 后解绑和 Reader 占用行为。`baseline_characterized` 只表示缺陷已观察，不代表旧包正确。

基线只执行安全的 `SELECT 11 /* /* */ + 31 -- */\n FROM DUAL`：当前环境下首个注释关闭时返回 42，按嵌套误读时返回 11，两种结果都仍是有效 SELECT。绝不向旧包发送包含 COMMIT 的复合 payload。

## fixed

除上述行为外，还检查活动事务中的 `ROLLBACK TO sp /* /* */ ; COMMIT -- */` 在客户端拒绝，事务保持 Active、插入行仍在；随后显式 Rollback，并由独立连接确认新增行数 0。

如果该 SELECT 实际返回 11，停止 guard 验收并报告需要重新审查当前环境设计。它只证明已验证环境的注释语义，不推广到所有达梦配置。fixed 的所有必需行为必须通过。

## CLOB 与对象清理

CLOB 使用合成文本 `A中e\u0301文`，共 5 个 UTF-16 单元，保持分解组合符。补充平面字符只在离线本地 CLOB 检查，不推断 GB18030 emoji 映射。

空字符串实库结果单独记录；如果服务器映射为 SQL NULL，不算 non-NULL empty CLOB 通过。离线 ReaderReviewTests 独立覆盖本地非 NULL 空 CLOB 长度 0。工具保留物化路径，不宣称流式 LOB；底层 CLOB 长度超过 `int.MaxValue` 时明确拒绝，不返回前缀长度。

DDL 前确认随机唯一表不存在，并用 CreateNew 写 `<report>.ownership.json` 注册精确归属。finally 只清理当前 nonce 对应的唯一表，再由另一新连接核实 USER/Schema/profile 和 USER_TABLES 最终数量 0。宿主中断时，该文件用于人工限定收尾。

不执行跨 Schema 操作、不使用 SA 或固定共享对象。报告只输出结构化状态、异常类型和安全 number，不输出异常消息、SQL、连接串、密码或环境内容。
