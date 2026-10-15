# I09 离线验证记录

## 合并 main 前候选

2026-10-15，验证者独立串行运行 12 个离线项目，共 **2264 / 2264 通过**，最终各进程退出码均为 0。受测源码是 `27058bca95a0d78c689413c252751b3163f28f1b` 上的 I09 工作树实现，随后保存为 `e49c275`；运行期间仅修正下述绕过构造函数或直接构造 prepared 状态的测试夹具，产品源码未变。

这组结果先于合并 main 的数组 / DbBatch 与 .NET 8 / 9 / 10 多目标更新，只证明当时的 net10.0 候选。合并后的完整三目标结果单列如下，不能将本表重复计入。

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

## 合并 main 后三目标回归

2026-10-15，受测产品及测试代码随后保存为 `7f390ed`，运行期间未改动受测源码。独立串行执行 12 个项目的全部三个目标，36 份 TRX 共 **6903 / 6903 通过**，每目标 **2301 / 2301**，失败、跳过、中止均为 0；12 个测试命令退出码均为 0。这轮同时覆盖合入 main 的数组绑定 / DbBatch 与多目标构建，以及新增的过程批调用前置拒绝。

| 项目 | net8.0 | net9.0 | net10.0 | 命令退出码 |
| --- | ---: | ---: | ---: | ---: |
| Command | 131 | 131 | 131 | 0 |
| Type | 965 | 965 | 965 | 0 |
| Transport | 134 | 134 | 134 | 0 |
| Transaction | 135 | 135 | 135 | 0 |
| Async | 134 | 134 | 134 | 0 |
| Pool | 308 | 308 | 308 | 0 |
| Lob | 258 | 258 | 258 | 0 |
| Session | 21 | 21 | 21 | 0 |
| Configuration | 12 | 12 | 12 | 0 |
| Security | 41 | 41 | 41 | 0 |
| Isolation | 78 | 78 | 78 | 0 |
| Diagnostics | 84 | 84 | 84 | 0 |
| 合计 | **2301** | **2301** | **2301** | **0** |

新增 17 个合并边界用例验证：过程数组绑定及批内后续过程命令通过 Reader / NonQuery / Scalar / Prepare 的同步、异步入口一律在连接访问前拒绝；批内先前有效 INSERT 未执行、计数未更新；过程数组 plan 在检查行形状前拒绝，失败后执行 gate 释放。原有数组、DbBatch 用例继续通过。

实际使用 SDK 10.0.401 和 .NET **8.0.32 / 9.0.21 / 10.0.12**。复用 I12 工作树中的微软 runtime 与实体 SDK，不设置跨主版本 roll-forward。环境与版本输出在 `.local/issue9/validation/merged-runtime.txt`，逐项结果在 `merged-summary.json`。每项目目录为 `<Project>-merged-r2`，保存 `command.json`、`test.log`、`result.json` 与按框架区分的 `results_<TFM>_<timestamp>.trx`。

完整命令形式如下（项目名及结果目录按表替换）：

```sh
export DOTNET_ROOT=/home/air/.t3/worktrees/W.DamengProvider/t3-5b93f0cb/.local/issue12/dotnet
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="$PWD/.local/issue9/validation/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$DOTNET_ROOT/dotnet" test tests/W.DmProvider.CommandTests/W.DmProvider.CommandTests.csproj \
  --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false \
  --logger 'trx;LogFilePrefix=results' \
  --results-directory "$PWD/.local/issue9/validation/Command-merged-r2" --verbosity minimal
```

首次 `Command-merged-r1` 构建三目标成功、net8/net10 各 131 项通过，但系统 host 缺少 .NET 9 runtime，net9 testhost 无法启动，命令退出 1。完整原日志及中止 TRX 保留。随后改为复用上述已有本地 SDK/runtime，并从 Command 开始重跑完整矩阵；没有修改产品或测试来规避该环境问题。多目标回归仍属于离线验证，真实数据库的各目标结果另见实库兼容性记录。
