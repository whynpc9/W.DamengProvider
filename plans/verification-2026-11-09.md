# 006–010 达梦实库验证记录

2026-11-09 在 `main@adf4734c788ceebb0595d0259e466ecb0d118862` 上完成五项定向实库验证，最终命令退出 0。受测程序集为 `W.DamengProvider`，MVID `02072221-e438-4c5e-bdee-f97744ab3bdb`，运行时 .NET 10.0.12。服务器为 DM 8.1.5.60，UTF-8。连接使用 `scripts/with-dameng-test.sh`，服务器查询确认当前用户和 Schema 均为 `WDM_PROVIDER_TEST`。驱动显式使用 `PlaintextAllowed`，确认 STARTUP 模式为 0。

## 结果与对象清理

| 计划 | 实库结果 | 本轮唯一表名 | 最终状态 |
| --- | --- | --- | --- |
| 006 / F18 | 对可写的行外 CLOB 执行 `SetString`，以 32,000 字节为块上限发送三帧。ACK 返回计数 21,333，事务已提交，新连接逐字符读回 21,333 个字符，共 63,999 UTF-8 字节。 | `IMF18_5BEC0F35181340668FEB` | 新连接确认不存在 |
| 007 / F06 | 通过不强制目标描述的 `INSERT … SELECT … CASE` 路径，正负边界和普通值精确存储。六组越界值均返回 6005，原参数保留，连接保持 Open。观察到语句分配、准备或关闭帧，未发送 EXECUTE。新连接确认拒绝行不存在，同命令改用合法值后成功。最终六行均与独立 interval 字面量一致。 | `IMF06_5BEC0F35181340668FEB` | 新连接确认不存在 |
| 008 / F17 | 首个及后续参数的标量、rank 和长度错误共六组，全部在零网络发送时拒绝，且表内仍为零行。同步正常数组和异步非零下界数组共写入四行，新连接逐值核对。 | `IMF17_5BEC0F35181340668FEB` | 新连接确认不存在 |
| 009 / F07 | 同步和异步 NonQuery、Scalar、Reader 的六组满池调用均按 `Command / PoolWait / NotSent` 取消，零发送且无取消行落库。每组验证持有连接可用和批次可重新执行，新连接读回六行。另在两次真实 STARTUP 回包后发起取消，均只观察到 STARTUP 一帧，未发送 LOGIN 或业务 SQL，容量归零。此时 Open 失败分类为 `Command / Receive / Unknown`，最终表中没有额外行。 | `IMF07_5BEC0F35181340668FEB` | 新连接确认不存在 |
| 010 / F16 | 普通命令与批次共 14 组同步和异步入口，在等待连接时拒绝 62 次 SQL、参数、集合或超时修改。四行读回的是调用时的 `byte[]`、`char[]` 内容。Reader 存续期间仍冻结，关闭后可修改。普通命令和批次各验证一条 8 KiB BLOB 流，等待时未读取，调用方保有流，落库后逐字节一致。 | `IMF16_5BEC0F35181340668FEB` | 新连接确认不存在 |

每张表在 CREATE 前按精确名称查询不存在并登记。每次测试结束后在身份已核验的连接上按登记名称清理，再用另一条新连接查询 `USER_OBJECTS`。最终通过轮为 5/5 张表不存在。合计六轮的 15 个登记对象均有新连接确认不存在的结果，含失败诊断轮及基线对照。CLOB 显式事务已提交，其余写入使用已核对身份的同一 TEST 配置，所有测试连接已释放。

## F06 的描述边界与基线对照

当前服务器对直接 `VALUES`、简单 `INSERT … SELECT` 和带显式 `CAST` 的目标列绑定返回了驱动拒绝的 interval 描述。错误为 `InvalidDataException`，原因 `Interval metadata contains invalid declaration fields.`，连接进入 Broken。每条失败路径均用新连接确认零行落库。实库正向和拒绝场景改用同一张真实 `INTERVAL YEAR(9) TO MONTH` 列的 `CASE` 绑定入口。

用仓库历史提交 `963ffbcd2a6d80ddee04ed8c7b72227a38e40ee6` 单独构建旧版 W 驱动，MVID `9eb988ed-02bd-411a-822a-de054f82d80d`。同一服务器上的直接列绑定复现了同一描述错误和 Broken 状态。经 `CASE` 入口传入 51,539,607,552 月，旧驱动成功写入并读回零年月间隔，当前驱动返回 6005 并保持连接可用。基线仅用于识别既有描述限制和证明回绕差异。直接固定描述的成功路径仍以既有离线夹具验证为准。

## 命令与证据

探针及脱敏日志在 Git 忽略的 `.local/verify-improve-2026-11-09/`。候选探针使用项目引用编译，恢复使用已有的本机 NuGet 缓存。以下命令在仓库根执行。

```bash
export DOTNET_CLI_HOME="$(mktemp -d /tmp/wdm-live-five.XXXXXX)"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
wdm_probe=.local/verify-improve-2026-11-09
dotnet restore "$wdm_probe/Probe.csproj" --source "$HOME/.nuget/packages" --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false
dotnet build "$wdm_probe/Probe.csproj" --no-restore -c Release --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false
scripts/with-dameng-test.sh dotnet "$wdm_probe/bin/Release/net10.0/W.DmProvider.PoolTests.dll" F18 F06 F17 F07 F16
scripts/with-dameng-test.sh dotnet "$wdm_probe/baseline/bin/Release/net10.0/W.DmProvider.PoolTests.dll" F06
```

基线可执行文件使用相同 SDK、缓存和单节点参数，从 `baseline/Probe.csproj` 独立恢复和构建。各轮实际传入的参数、退出码及证据如下。

| 轮次 | 实际参数 | 退出码 | 对象不存在 | 说明 |
| --- | --- | --- | --- | --- |
| `run-01.jsonl` | `F18 F06 F17 F07 F16` | 1 | 5/5 | F17、F07、F16 通过。F18 的异步帧钩子无法观测同步调用，事务未提交。F06 触发直接绑定描述拒绝。 |
| `run-02.jsonl` | `F18 F06` | 1 | 2/2 | F18 改用同步发送计数后通过。F06 连接变 Broken，探针原本要求连接继续 Open。 |
| `run-03.jsonl` | `F06` | 1 | 1/1 | 用新连接核对三个描述拒绝入口均为零行，找到可用的 CASE 入口。越界返回 6005，原零网络帧断言仍失败。 |
| `run-04.jsonl` | `F06` | 0 | 1/1 | 分别观察 EXECUTE 与准备、清理帧，完成六组拒绝、精确读回和复用。 |
| `baseline-f06.jsonl` | `F06` | 0 | 1/1 | 完成基线对照，复现描述拒绝和越界写零。 |
| `run-final.jsonl` | `F18 F06 F17 F07 F16` | 0 | 5/5 | 五项最终通过，包含真实 STARTUP 取消和流式输入补测。 |

`cleanup-summary.json` 汇总 15 个精确对象及逐轮退出码。探针源码、诊断轮源码快照、构建日志、程序集身份和 JSONL 均保存在本地证据目录。覆盖范围为该服务器、UTF-8、共享明文和 .NET 10。严格 TLS、其他运行时的实库组合及其他服务器版本未纳入本次验证。分配增长、迟到取消竞态和流式输入旧协议拒绝仍由合并前的离线回归覆盖。
