# 存储过程离线验证记录

> 本文按合并阶段保存历史测试数量、失败原因和修复结果，只适用于各自提交。当前存储过程行为见[存储过程说明](I09-stored-procedures.md)，当前发布结果见[0.3.1 发布记录](../release-0.3.1.md)。

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

## 同步 PR #22 元数据实现后的补验

合并 `origin/main@1dec19e` 后，候选为 `04ffa82`。独立复核后，仅对相交的 Command / Type / Async / Diagnostics 及新增 Schema 套件执行三目标补验；其余八套的产品行为路径没有实质变化，沿用上述三目标结果，不重复计入本轮实际执行数。

新增 6 个 `StoredProcedure` 元数据模式用例，覆盖 `SchemaOnly`、`KeyInfo` 及组合的同步和异步入口：全部本地拒绝、协议发送数为 0、会话保持 Ready、旧输出不变，命令及参数可修改，下一次执行 lease 可取得。全仓检索 Reader 绕过构造函数与 prepared 状态夹具后，既有默认字段初始化完整；新 `ReaderMetadataTests` 原来以 `SELECT 1 FROM DUAL` 作为过程名称构造 plan，改为合法名称 `P`，仍断言 metadata 层拒绝。产品规则与测试断言均未放宽。

| 套件 | net8.0 | net9.0 | net10.0 | 命令退出码 |
| --- | ---: | ---: | ---: | ---: |
| Command | 172 | 172 | 172 | 0 |
| Type | 965 | 965 | 965 | 0 |
| Async | 157 | 157 | 157 | 0 |
| Diagnostics | 85 | 85 | 85 | 0 |
| Schema | 23 | 23 | 23 | 0 |
| 合计 | **1402** | **1402** | **1402** | **0** |

15 份 TRX 共 **4206 / 4206 通过**，失败、跳过、中止均为 0。实际 SDK/runtime 及命令参数与上一节相同，仅项目按本表选择、结果目录改为 `.local/issue9/validation/<Project>-metadata-r1`。每个目录保留精确 `command.json`、`test.log`、`result.json` 与三个框架的 TRX，汇总为 `metadata-summary.json`。例如：

```sh
"$DOTNET_ROOT/dotnet" test tests/W.DmProvider.SchemaTests/W.DmProvider.SchemaTests.csproj \
  --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false \
  --logger 'trx;LogFilePrefix=results' \
  --results-directory "$PWD/.local/issue9/validation/Schema-metadata-r1" --verbosity minimal
```

本轮首次运行五个项目即全部通过。构建 / 测试期间受测产品与测试源码未改；真实数据库补验由独立验证者另行运行，不计入离线 TRX 数。


## PR #23 review：排除遗留全局结果缓存

针对 [r4280173685](https://github.com/whynpc9/W.DamengProvider/pull/23#discussion_r4280173685)，在 `734f67e` 上补充过程缓存排除差异：统一按捕获 plan 的 CommandType 判断，只允许 Text 进入全局缓存查询，以及同步执行、异步执行和 FETCH 的三个写入入口。每 statement 的行缓冲保留。

公开配置实际上已通过 `DmConnectionStringBuilder.Normalize` 的非默认选项检查拒绝 `EnRsCache=true`；本轮不开放该功能。新增测试先验证 typed setter 和连接串两种公共入口继续拒绝，再仅在离线夹具中同时设置 logical/physical 配置快照、注入并恢复静态缓存，以实际覆盖内部遗留路径。预置精确匹配条目时，过程必须走到夹具 transport 的 Send，Text 对照零发送；同步/异步执行和 FETCH 的真实解码帧必须保留局部行数据，但不给过程登记全局条目。静态缓存为空的过程路径也通过。

由实现者外的主 agent 串行运行 Command 与 Async 两套全部三目标，SDK/runtime 与前节相同：

| 项目 | net8.0 | net9.0 | net10.0 | 退出码 |
| --- | ---: | ---: | ---: | ---: |
| Command | 172 | 172 | 172 | 0 |
| Async（含13个缓存用例） | 170 | 170 | 170 | 0 |
| 合计 | **342** | **342** | **342** | **0** |

共 **1026/1026** 通过，零失败、跳过或中止。每项目沿用上述 `dotnet test` 参数及 `LogFilePrefix`，结果目录分别为 `.local/issue9/cache-review/Command` 和 `Async-r3`，包含命令、完整日志、结果 JSON 及各框架 TRX。受测产品在执行期间未变。

保留初始记录：`Async` 首次因新测试把 `DbDataReader` 当作 `DmDataReader` 访问内部字段而编译失败，修为明确类型断言；`Async-r2` 每目标165通过、5失败，揭示最初夹具错误假设公开选项可启用，且只修改了 logical 配置，physical 缓存开关仍关闭。修正这些前置条件后保留全部缓存命中/写入及零发送断言，`Async-r3` 全部通过。产品代码未因这些测试问题改变。

本轮没有执行新的数据库操作。缓存模式不属于公开支持范围，故撤回新增实库缓存探针；此前正常配置的三目标177项实库证据保留，不宣称提供新的缓存能力。
