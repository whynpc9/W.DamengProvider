# I09 离线验证记录

## 合并 main 前候选

2026-10-15，验证者独立串行运行 12 个离线项目，共 **2264 / 2264 通过**，最终各进程退出码均为 0。受测源码是 `27058bca95a0d78c689413c252751b3163f28f1b` 上的 I09 工作树实现，随后保存为 `e49c275`；运行期间仅修正下述绕过构造函数或直接构造 prepared 状态的测试夹具，产品源码未变。

这组结果先于合并 main 的数组 / DbBatch 与 .NET 8 / 9 / 10 多目标更新，只证明当时的 net10.0 候选。合并后的受影响回归需另外记录，不能沿用此表宣称多目标验证完成。

| 项目 | 通过 | 失败 | 退出码 | 本地记录目录 |
| --- | ---: | ---: | ---: | --- |
| Command | 99 | 0 | 0 | `Command-r1` |
| Type | 965 | 0 | 0 | `Type-r4` |
| Transport | 133 | 0 | 0 | `Transport-r1` |
| Transaction | 135 | 0 | 0 | `Transaction-r1` |
| Async | 134 | 0 | 0 | `Async-r2` |
| Pool | 304 | 0 | 0 | `Pool-r1` |
| Lob | 258 | 0 | 0 | `Lob-r1` |
| Session | 21 | 0 | 0 | `Session-r1` |
| Configuration | 12 | 0 | 0 | `Configuration-r1` |
| Security | 41 | 0 | 0 | `Security-r1` |
| Isolation | 78 | 0 | 0 | `Isolation-r1` |
| Diagnostics | 84 | 0 | 0 | `Diagnostics-r1` |

每个记录目录均位于被忽略的 `.local/issue9/validation/`，保留完整 `command.json`、`test.log`、`result.json` 和 `results.trx`。命令从仓库根目录执行，项目名与结果目录按上表替换：

```sh
export DOTNET_CLI_HOME="$PWD/.local/issue9/validation/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test tests/W.DmProvider.CommandTests/W.DmProvider.CommandTests.csproj   --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false   --logger 'trx;LogFileName=results.trx'   --results-directory "$PWD/.local/issue9/validation/Command-r1" --verbosity minimal
```

## 保留的初始失败及夹具修复

- `Type-r1`：572 通过、393 失败，退出 1。直接使用 `RuntimeHelpers.GetUninitializedObject` 创建 Reader 的夹具没有初始化新增的 `procedureChildren`，关闭时抛出空引用。
- `Type-r2`：952 通过、13 失败，退出 1。剩余另一处同类夹具导致相同失败。修正后 `Type-r3` 965 通过；全仓搜索其余同类夹具并补默认空数组后，`Type-r4` 965 通过。
- `Async-r1`：128 通过、6 失败，退出 1。`CommandDeadlineTests` / `CommandCancellationTests` 直接注入 prepared 句柄时遗漏 `preparedSql`，触发了 SQL 变化后的重新准备路径，改变测试中预期的取消阶段。补齐与当前命令一致的 prepared SQL 后，`Async-r2` 134 通过。

绕过 Reader 构造函数的四处夹具（`TemporalNativeReaderContractTests`、`TemporalNativeReaderRouteTests`、`ReaderReviewTests`、`TemporalNativeReaderAsyncContractTests`）已全部设置 `procedureChildren` 的构造默认值。修复仅恢复夹具的有效内部状态，未放宽断言或修改产品行为。初始失败与后续重跑日志均原样保留。

## 覆盖边界

本记录覆盖过程解析 / 参数映射 / 输出发布与撤销、Reader 终态及同步原生 Socket 超时分类的离线测试，以及现有 Text 命令、事务、异步取消、池、LOB、会话、安全及诊断回归。离线测试不验证服务器真实过程语义、游标协议或精确对象清理；真实达梦验证由独立验证者另行记录。本轮未加载数据库连接凭据，未执行数据库操作。
