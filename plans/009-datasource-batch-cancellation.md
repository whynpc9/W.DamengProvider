# Plan 009 / F07：取消数据源批次的连接获取及执行

> 本计划建立批次逐次执行所有权，由独立 Worktree 中的 Plan 010 承接。两项分别验证。

## 状态

- 状态 DONE。已通过 [PR #32](https://github.com/whynpc9/W.DamengProvider/pull/32) 合并为 `828b53b`。优先级 P1，工作量 M，风险 MED。
- Planned at：`963ffbc`，2026-11-08；Depends on：无。
- 009 与 010 使用各自的 Worktree 和 PR，010 承接 009 已独立验证的提交。
- 本计划不提前重构参数捕获；010 必须在本项独立验证后接续。已有单次 owner 必须可支持“先创建关闭 connection、后冻结、再 Open”的后续顺序。

## 目标与当前状态

`src/W.DamengProvider/PublicApi/DmDataSourceCommand.cs:370,401–405` 的 DmDataSourceBatch：

```csharp
public override void Cancel() => batch.Cancel();
DmConnection connection = Attach(await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false));
try { return await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false); }
finally { await DetachAsync(connection).ConfigureAwait(false); }
```

Scalar/Reader 和同步路径相同。`PublicApi/DmBatch.cs:91` 只取消已经存在的 activeExecution；Open 期间它还不存在，所以无限池等待、握手或交接中的 Cancel 丢失，后来可能执行 SQL。

可复用的已有机制：

- `Internal/Execution/DmDataSourceExecution.cs:27–64` 在短锁内捕获本轮 plan，锁外调用 CTS/取消；BindPlan 会补发早到的 Command 取消。
- 同文件 Open/OpenAsync/TranslateOpenFailure 保留原 caller token、Command/User 分类和 pool wait 的 NotSent。
- `Internal/Execution/DmBatchExecution.cs:61–71,120–128` 已在 Cancel 设置标志，并在 Acquire/TakePlan 补施加；不要另造第二套执行取消状态机。
- 普通 `DmDataSourceCommand.Begin/End/Release` 先 source.CreateConnection，绑定后 Open；通过 RunAfterPhysicalClose 才释放 active，物理关闭与容量归还顺序不能提前。
- `docs/compatibility/I07-I08-array-binding-dbbatch.md` 明确先 Dispose batch 不影响随后 Dispose Reader；`R3ShortcutCancellationTests.BatchDisposedBeforeReaderStillReleasesConnection` 已覆盖。此契约必须保持。

## Scope 与交接边界

允许修改：
- `src/W.DamengProvider/PublicApi/DmDataSourceCommand.cs`：仅 DmDataSourceBatch 及必要的内部测试 hook；普通 command/reader 不重构。
- `src/W.DamengProvider/Internal/Execution/DmDataSourceExecution.cs`：复用逐次 owner、增加具体 batch execution 的取消绑定；NETSTANDARD2_1 不得引用被排除的 batch 类型。
- `src/W.DamengProvider/PublicApi/DmBatch.cs`：内部绑定/解绑及 Begin 交接，公共执行语义不变。
- `src/W.DamengProvider/Internal/Execution/DmBatchExecution.cs`：仅必要的取消绑定生命周期适配；保留 Capture/Acquire/TakePlan 实现。
- `tests/W.DmProvider.PoolTests/DataSourceBatchLifecycleTests.cs`（新增，固定类名）。
- `tests/W.DmProvider.PoolTests/DataSourceProtocolFixture.cs`（仅如需提取现有协议 channel），以及 `R3ShortcutCancellationTests.cs` 中对应 helper 的最小提取与引用修改，原断言完整保留。

不改 DmCommand/Parameter/CommandPlan 的捕获设计、池调度器、传输协议、事务、F08 Reader 计数清理或发布流程。batch Prepare 保持本地预检，不应为了统一代码获取连接。

交接给010时保留以下接口职责（方法名可按现有风格命名，交付记录写实际符号）：创建一次 owner/closed connection；绑定具体 DmBatchExecution；捕获一次 Cancel；标记 Reader 已交付；非Reader结束/失败关闭；物理关闭后释放 owner。不能向010交付一个每次都重新查 batch.activeExecution 的取消回调。

## 漂移检查

```sh
git status --short
git diff --stat 963ffbc..HEAD -- src/W.DamengProvider/PublicApi/DmDataSourceCommand.cs src/W.DamengProvider/Internal/Execution/DmDataSourceExecution.cs src/W.DamengProvider/PublicApi/DmBatch.cs src/W.DamengProvider/Internal/Execution/DmBatchExecution.cs tests/W.DmProvider.PoolTests/DataSourceBatchLifecycleTests.cs tests/W.DmProvider.PoolTests/DataSourceProtocolFixture.cs tests/W.DmProvider.PoolTests/R3ShortcutCancellationTests.cs
```

另用 `git diff` 和 `git diff --cached` 对相同路径检查已有工作。行号变化不自动阻塞；不覆盖不属于本项的修改。
## 命令与执行环境

以下命令是后续实施/验证命令，本次计划编写未执行 .NET。工作目录固定为仓库根；不要加载任何数据库 secret，不需要实库。

下列 helper 在同一个 Bash 会话定义；它们只是命令简写，不写入仓库脚本。

```sh
cd /Users/wanghongyi/Projects/W.DmProvider
export DOTNET_CLI_HOME="$(mktemp -d /tmp/wdm-plan-dotnet.XXXXXX)"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
wdm_test() {
  dotnet test "tests/W.DmProvider.$1/W.DmProvider.$1.csproj" -c Release -f "$2" --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false "${@:3}"
}
wdm_standard() {
  dotnet build src/W.DamengProvider/W.DamengProvider.csproj -c Release -f netstandard2.1 --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false
}
wdm_verify() {
  local wdm_tfm wdm_suite
  for wdm_tfm in net6.0 net8.0 net9.0 net10.0; do
    for wdm_suite in "$@"; do
      wdm_test "$wdm_suite" "$wdm_tfm" || return $?
    done
  done
  wdm_standard || return $?
  git diff --check
}
```

global.json 固定 SDK 10.0.401；现代测试目标为 net6.0/net8.0/net9.0/net10.0。不要用高版本运行时滚动代替缺失的目标运行时。若实际使用 .NET 6 SDK，不支持 `--disable-build-servers`，需报告 SDK 与仓库要求不符；不清空全局 NuGet 缓存。构建/测试必须串行，不能与其他 agent 共享 bin/obj 并行写入。

每条正向测试命令应退出 0，且选中的测试数量大于零；过滤器零匹配不能算通过。新增回归的初次红灯要保留实际失败记录，区分编译/工具错误和预期产品断言失败；修复后全部转绿。新测试类名按本计划固定，便于直接执行过滤命令。


## 实施步骤

1. **建立满池和交接回归。** 在新类加 `[Collection("Pool API hooks")]`。使用 ConnectionPoolTests.cs 的 SyntheticPoolHandshake、PoolApiSettings（不复写连接内容），先持有 MaxPoolSize=1 的连接。启动 source.CreateBatch 的 nonquery/scalar/reader，等待 Snapshot.Waiting==1 后 Cancel。同步入口仅在测试中 Task.Run 以便观察阻塞，产品不得 Task.Run 包装同步调用。
   - 原例在 `R3ShortcutCancellationTests.cs:31,138,194,250,503,550`，包含无限池等待、握手中、Open/plan/lease 缝隙、caller token、迟到取消和 batch→Reader 释放。
   - 需要 wire 观察时提取其私有 ShortcutWireChannel 的最少功能到上述 test-internal helper；保留真实 DmBatch 与字节编码，不用 fake batch 返回成功证明零 SQL。
   - watchdog 只防挂死：先断言原任务完成，再断言 DmOperationCanceledException/FailureInfo；外层 TimeoutException 不能算取消成功。finally 必须释放屏障、持有连接并等待未结束任务。
   - **Verify**：`wdm_test PoolTests net10.0 --filter FullyQualifiedName~DataSourceBatchLifecycleTests`；基线满池 Cancel 用例应在有界 watchdog 下报告失败而非一直挂起，禁止用无限等待制造基线。

2. **先登记 owner 和关闭连接，再 Open。** 包装层在短锁内检查 disposed/active、创建 source.CreateConnection、在未冻结时赋给内部 batch、登记本次上下文；实际 Open/取消/Dispose 都在锁外。使用复用的 Open token 和错误翻译，三个执行入口 sync/async 一致。Cancel 捕获具体 owner，在锁外取消其 Open。
   - 此步骤可先实现等待/握手取消。**Verify**：新增类中 pool/handshake 子集过滤命令 `wdm_test PoolTests net10.0 --filter "FullyQualifiedName~DataSourceBatchLifecycleTests&FullyQualifiedName~Open"`；新测试名称应含 Open，要求非零执行且通过。

3. **把早到取消交接到具体批次执行。** 内部 DmBatch.Begin 发布本次 DmBatchExecution 时绑定 owner；先前 Cancel 已记录则立即对该对象调用 Cancel。不要保存 `() => batch.Cancel()`，它可能在旧回调恢复时指向新执行。沿用 Acquire/TakePlan 补取消机制。caller token 原样传给各公开异步调用的执行阶段，Reader 返回后不把原调用 token 变成永久 Reader 取消令牌。
   - **Verify**：完整新增类通过；`wdm_test PoolTests net10.0 --filter FullyQualifiedName~R3ShortcutCancellationTests` 全绿，旧/新轮次互不取消。

4. **收齐 Dispose、Reader 和物理归还。** pending Open 的 Dispose/DisposeAsync 终止未交付操作，清理 waiter/连接；异常类型不强求等同 Cancel，但零业务发送/无遗留必须成立。Reader 已交付后 Dispose batch 仅拒绝新调用，不主动关闭 Reader；Reader 关闭委托捕获本次 owner/connection。非Reader结束和失败保留首异常，active 仅在 RunAfterPhysicalClose 完成后释放。重复/并发清理不提前归还容量。
   - **Verify**：`wdm_test PoolTests net10.0 --filter "FullyQualifiedName~DataSourceBatchLifecycleTests|FullyQualifiedName~R3ShortcutCancellationTests|FullyQualifiedName~DataSourceTests"` 全绿。必须包括已有 BatchDisposedBeforeReaderStillReleasesConnection 四组合。

5. **独立验收并交接。** 产品源码稳定后，验证者串行运行：

```sh
wdm_verify PoolTests CommandTests
```

逐目标确认成功。独立审查者重点看“捕获具体身份、锁外回调、Reader 已交付分支、物理关闭后释放”。记录009实际 diff/提交、测试命令与结果、供010复用的符号/fixture；不要求额外冻结产物。

## 验收矩阵

| 场景 | 必须检查 |
| --- | --- |
| 无限满池等待，sync/async×三入口 | Cancel后原任务结束，Waiting=0、没有第二次握手或业务SQL，原持有者仍可用 |
| 握手暂停与物理关闭暂停 | 取消旧握手；物理关闭完成前容量仍占用，无新连接抢入 |
| Open后到Begin/Acquire之间 | 早到Cancel不丢失，NotSent、零业务帧 |
| caller token在等待/握手/执行中取消 | 原token、User分类保留，Command.Cancel则为Command，阶段和发送结果正确 |
| 旧Cancel已捕获后暂停、下一轮开始再恢复 | 新轮次不受影响，旧owner已退休仍安全 |
| pending Open Dispose/DisposeAsync | 有界结束，无新业务帧、waiter/容量最终清零 |
| 已交付Reader时Dispose batch | Reader继续有效，后续关闭恰好归还一次 |
| 重复/并发关闭、正常重复执行 | 不提前归还、不串用取消状态、最终Snapshot.IsQuiescent |
| Prepare/PrepareAsync | 仍本地预检，零握手/SQL |

## Done criteria 与010交接

- [ ] 上表每行有真实 public wrapper 行为断言，而非只测新上下文 helper。
- [ ] 新类及既有Pool/Command四目标通过，受限资产构建通过。
- [ ] 普通 shortcut 不回退；无锁内取消/Dispose；调用token返回Reader后解绑语义保持。
- [ ] 独立审查/验证完成，索引009更新DONE；记录准确受测diff供010承接。
- [ ] 在010开始前交出所有共有文件写权限；010不得重建第二套owner。

## 维护重点与专属停止条件

若修复要求改变池调度器/连接物理关闭语义、取消分类或“batch Dispose不关闭已交付Reader”，先说明实际证据和最小方案。若单纯 wrapper CTS 无法保证 Open→Begin 交接，不能以大概率通过替代身份测试。F16 的输入冻结未完成在本项记录为后续范围，不据此宣称009没有完成取消目标。
## 交付与停止条件

- 分支使用 `codex/` 前缀；不提交或推送用户已有工作，不创建 PR/发布。需要提交时保持一个逻辑修复一条 conventional commit，例如 `fix: preserve ...`，不要夹带其他计划。
- 实现者完成后由独立审查者核对差异，独立验证者在源码不变时执行受影响测试。记录实际提交或未提交 diff、命令、退出码、测试数及未覆盖范围；不建立额外冻结/哈希体系。
- 修改期间保留原有 `.agents/`、`.claude/`、`skills-lock.json` 和计划材料。只允许本计划 Scope 中的产品/测试文件，以及自己的计划状态和 `plans/README.md` 对应行变化。
- 源码漂移时先比较本计划摘录与实际控制流；合理行号漂移不阻塞。已由前置计划引入的预期变化正常承接，行为假设失效或范围需要扩大时暂停并说明最小调整。
- 新失败先归因产品、测试、工具或基线；可修正参数正常重跑，不以盲目重试或放宽断言求通过。需要新权限、真实服务器实验或改变未明确的协议语义时停止报告，不自行扩权。
- 验证缺失如实标记 pending，不把未运行项目写成通过。完成后更新本计划与索引状态；协调者维护索引时由其更新。
