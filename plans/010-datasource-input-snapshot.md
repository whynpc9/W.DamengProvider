# Plan 010 / F16：数据源调用在连接等待前冻结输入

> 本计划在独立 Worktree 承接已验证的 009 单次执行上下文和 008 形状预检。

## 状态

- 状态 DONE。已通过 [PR #33](https://github.com/whynpc9/W.DamengProvider/pull/33) 合并为 `adf4734`。优先级 P1，工作量 M–L，风险 MED。
- Planned at：`963ffbc`，2026-11-08。
- Depends on：`008-array-shape-preflight.md`、`009-datasource-batch-cancellation.md`，均须行为验证完成；006/007不是实现前置条件。
- 实施分支 `codex/datasource-input-snapshot` 先基于已验证的 008、009 实现。两项依赖合并后，010 转到最新 `main` 复验并创建独立 PR。

## 目标与当前状态

当前 `PublicApi/DmDataSourceCommand.cs:26–35` 直接暴露内部command属性与参数。Begin仅设置包装层active；ExecuteNonQueryAsync先Open再调用command.ExecuteNonQueryAsync：

```csharp
DmDataSourceExecution execution = Begin(cancellationToken);
await execution.OpenAsync().ConfigureAwait(false);
return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
```

所以调用已开始且Task仍在等待池容量时，SQL、参数和集合还可以修改，原byte[]/char[]也尚未复制。batch同样先Open再Capture。目标是公开调用同步部分就建立不可变输入，并保持至执行结束/Reader关闭。

必须保留的当前设计：

- `docs/compatibility/T08-command-reader.md:7–10`：固定SQL、参数元数据/值和超时；数组复制；活动Reader占用执行资源。
- `DmCommand.cs:88–120` 的 EnterPlan 不可原样前移：其中 negotiated msgVersion 检查会把尚未Open的合法流式LOB当成profile=0拒绝；Open后再次调用原公开Execute又会重复进入gate。
- `Internal/Execution/DmCommandPlan.cs:166–175,179–202` 同时保存原output参数身份和快照。不能简单Clone整个command后执行，否则OUT/INOUT不再发布到原对象。
- `DmParameter.cs:1067–1091` 对byte[]/char[]深复制，对显式LOB流只捕获调用方对象/类型，不读、不关闭调用方流。`DmParameterCollection.cs:26–43` 与 Parameter.BeginMutation 已通过所属gate拦截修改，优先复用。
- `DmBatchExecution.Capture` 与 Acquire 已分开；Capture冻结各source command gate，保存所有step计划；Cancel/Acquire/TakePlan保留009已验证的取消身份。
- `DmConnection` 的owned构造器已绑定source设置，尚未Open也能取得配置。提前固定Timeout值，不提前启动command网络执行deadline，不把pool wait预算并入CommandTimeout。

## Scope

允许修改：
- `src/W.DamengProvider/PublicApi/DmDataSourceCommand.cs`：普通shortcut及batch的本地捕获/执行接线，复用009 owner。
- `src/W.DamengProvider/PublicApi/DmCommand.cs`：分离本地捕获和协商后检查，内部入口消费同一个已捕获plan，必要的Prepare core提取。
- `src/W.DamengProvider/PublicApi/DmBatch.cs`、`Internal/Execution/DmBatchExecution.cs`：capture/acquire接线，不改顺序执行、错误归属或计数语义。
- `src/W.DamengProvider/Internal/Execution/DmDataSourceExecution.cs`：保存/转交/释放本轮已捕获plan所需最小生命周期调整，不重写009取消算法。
- `src/W.DamengProvider/Internal/Execution/DmCommandPlan.cs`：仅必要的捕获所有权接线，必须保留008全参数shape预检。
- `tests/W.DmProvider.PoolTests/DataSourceInputSnapshotTests.cs`（新增，固定类名）。
- `tests/W.DmProvider.CommandTests/DataSourcePlanHandoffTests.cs`（新增，仅本地捕获/协商后检查的必要覆盖）。
- 009新增的 `DataSourceBatchLifecycleTests.cs`、`DataSourceProtocolFixture.cs`：必要扩展，保留所有009断言。
- `tests/W.DmProvider.PoolTests/DataSourceTests.cs`：只有内部fake注入 seam 因新执行路径不再适用时，换为现有真实synthetic channel并保留原生命周期断言。

只读复用：CommandPlanTests、StoredProcedureBindingTests、ArrayBindingAndBatchTests、R3ShortcutCancellationTests、T25ShortcutLobOwnershipTests、现有输入LOB夹具。
不改：DmParameter/ParameterCollection公共行为及输入白名单、Session/Pool调度、传输协议、过程事务限制、LOB编码、F08清理、发布/依赖。如果需改只读文件的产品设计，先报告具体原因，不扩大成通用命令重构。

## 漂移与前置检查

```sh
git status --short
git diff --stat 963ffbc..HEAD -- src/W.DamengProvider/PublicApi/DmDataSourceCommand.cs src/W.DamengProvider/PublicApi/DmCommand.cs src/W.DamengProvider/PublicApi/DmBatch.cs src/W.DamengProvider/Internal/Execution/DmBatchExecution.cs src/W.DamengProvider/Internal/Execution/DmDataSourceExecution.cs src/W.DamengProvider/Internal/Execution/DmCommandPlan.cs tests/W.DmProvider.PoolTests tests/W.DmProvider.CommandTests/DataSourcePlanHandoffTests.cs
```

同时读working/staged diff和008/009的实际验证记录。上述基线摘录在009之后有预期差异：batch已先CreateConnection登记owner再Open；这不触发停止。008/009尚未完成或没有相关验证时先完成它们，不能在两个并行分支上猜测接口。
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

1. **建立等待期间的快照回归。** 使用 `[Collection("Pool API hooks")]`、SyntheticPoolHandshake占满source容量；对command nonquery/scalar/reader/prepare与batch三种执行入口启动sync/async调用，等待Waiting==1后修改属性、已取得的parameter/collection。新类至少包含 WaitingMutation、BufferSnapshot、ReleaseAfterFailure、ReaderLifetime 测试名片段，供过滤。
   - 普通SQL、类型、timeout；Parameter.Value/Name/DbType/Direction；collection Add/Remove/Clear；batch Timeout、子SQL、command collection均应立即拒绝。
   - 修改调用前传入的byte[]/char[]仍是调用者合法操作，但释放池容量后，捕获plan及实际参数编码应使用原内容。不能只检查setter拒绝或fake override的预设返回。
   - **Verify**：`wdm_test PoolTests net10.0 --filter FullyQualifiedName~DataSourceInputSnapshotTests`；009基线应在输入冻结断言红灯，不能因fixture无法执行SQL而错误归因。finally释放屏障和连接，等待所有任务退出。

2. **拆开本地捕获与协商后验证。** 在DmCommand内保留一个复用的本地捕获阶段：ValidateSupportedCommand、gate.Enter、Capture、原output映射、SQL/metadata/value/timeout快照。创建并绑定本次关闭connection后，在任何Open await之前捕获；该connection不再经公开setter重绑定。把msgVersion流式输入等必须依赖协商结果的检查移到Open成功后、业务发送前；正常直接command入口也走相同两阶段以免行为分叉。
   - 内部handoff应接受具体DmCommandPlan身份并验证只消费一次，不能Open后重新读m_Paras或释放再捕获。继续使用既有ExecuteCompleteCoreAsync、ExecuteDbDataReaderCoreAsync及同步等价core，必要时提取Prepare core，不复制一套协议执行器。
   - 计划取消在尚无lease时可被记录，保持009上下文绑定语义。AfterPlanCaptured等既有测试hook不能成为生产逻辑依赖。
   - **Verify**：`wdm_test CommandTests net10.0 --filter "FullyQualifiedName~DataSourcePlanHandoffTests|FullyQualifiedName~CommandPlanTests|FullyQualifiedName~StoredProcedureBindingTests"` 全绿；新handoff测试必须实际执行非零用例。

3. **接入普通shortcut四类入口。** Begin在短状态锁中登记owner，完整快照在受gate保护的流程中完成；锁内不得进行网络、取消回调或流读取。Open成功后消费同一plan，Open失败/取消释放plan和gate；执行成功交付Reader后，plan由原Reader关闭路径释放。保持OUT/INOUT原参数身份与原子发布，流式输入捕获不读、不Dispose、不重试。
   - `DataSourceTests` 的fake仅用于原有wrapper生命周期测试；若内部入口绕过fake virtual方法，可改成真实synthetic channel等价测试，不删除那些断言，不为fake维持第二套生产路径。
   - **Verify**：`wdm_test PoolTests net10.0 --filter "FullyQualifiedName~DataSourceInputSnapshotTests|FullyQualifiedName~DataSourceTests|FullyQualifiedName~T25ShortcutLobOwnershipTests"` 全绿；支持profile的真实流式输入仍能走到上传，不支持profile在业务发送前拒绝。

4. **接入batch的Capture→Open→Acquire。** 基于009提前拥有的关闭connection，在Open前冻结batch gate、各source gate和每步plan；输入是调用时内容，包括原byte[]/char[]。Open后只acquire当前session lease并执行已捕获steps。留意同一子command被加入两次：现有去重gate进入规则保留。source RecordsAffected与BatchCommand错误归属仍指原对象。
   - batch Prepare保持纯本地预检，不新增Open。每个失败阶段释放本轮未转交的plans/gates，Reader成功后不能提前解冻。不能通过暂时退出gate来设置connection、重新捕获或绕过公开mutation守卫。
   - **Verify**：`wdm_test PoolTests net10.0 --filter "FullyQualifiedName~DataSourceInputSnapshotTests|FullyQualifiedName~DataSourceBatchLifecycleTests|FullyQualifiedName~R3ShortcutCancellationTests"` 全绿；`wdm_test CommandTests net10.0 --filter "FullyQualifiedName~ArrayShapePreflightTests|FullyQualifiedName~ArrayBindingAndBatchTests"` 保留008行为。

5. **组合审查和串行验证。** 独立审查者跟踪每份plan、gate、owner、connection从创建到释放的单一拥有者；独立验证者在源码稳定后运行：

```sh
wdm_verify PoolTests CommandTests LobTests
```

必须逐目标核对退出码与测试数。F16在009之后改共享生命周期，因此这里重跑F07全矩阵有实际回归理由；不额外重跑无关历史数据库矩阵。若实现改变了原生async调用core的取消/错误恢复分支，补跑AsyncTests四目标；若只是同一core的plan接线，不机械扩展全部项目。

## 验收矩阵

| 场景 | 必须观察 |
| --- | --- |
| 等待连接期间属性/参数/集合修改 | command四入口、batch三入口×sync/async立即拒绝 |
| caller持有源byte[]/char[]并修改 | 原数组仍可修改；最终真实plan/编码使用调用时内容，command和batch均覆盖 |
| Open失败、取消、捕获失败 | gate/plan释放，未disposed命令后续可改可重用，零遗留容量 |
| Reader交付到关闭之间 | 原参数/集合继续冻结，Reader关闭后解锁；重复close安全 |
| 支持/不支持协商profile的流式输入 | 捕获不读/关流；支持profile成功进入真实上传，不支持profile保持拒绝 |
| 普通过程OUT/INOUT | 原parameter身份、旧输出保留/完整输出原子发布不变 |
| timeout | 调用时固定配置；等待池的预算与Open/执行预算仍分离 |
| batch重复source、异常归属/计数、Prepare | gate去重、原source身份、本地Prepare和顺序执行保留 |
| 009全部取消/Dispose矩阵 | token、旧身份隔离、零发送、Reader与物理容量归还全部继续通过 |
| 不经source的普通command/batch | 原plan/clone/类型推断/取消/输出合同不回退 |

## Done criteria

- [ ] public入口在第一次Open阻塞/await前已固定SQL、metadata/value/timeout；原buffer内容隔离有真实编码证据。
- [ ] 本轮同一plan只消费一次，无公开执行的二次Enter、无capture后重绑定Connection。
- [ ] 所有失败与Reader转交路径的gate/plan/owner最终释放，原output对象和caller流所有权不变。
- [ ] 008新增形状测试与009完整生命周期矩阵保留并通过。
- [ ] Pool/Command/Lob四目标及netstandard2.1构建退出0；必要Async回归已按实际diff决定并记录。
- [ ] 独立审查/验证完成，索引010与009组合复验记录更新；仅计划Scope有代码改动。

## 维护重点与专属停止条件

本地快照是配置稳定性边界，连接后的验证是服务器能力边界，不把两者混成一次必须有socket的操作。新参数类型必须决定是复制值还是捕获调用方资源，并保持输出原对象映射。若无法不读流就捕获，或需要重写取消/池调度协议，先报告具体冲突，不用放宽profile检查、fake返回值或提前关闭Reader使测试通过。
## 交付与停止条件

- 分支使用 `codex/` 前缀；不提交或推送用户已有工作，不创建 PR/发布。需要提交时保持一个逻辑修复一条 conventional commit，例如 `fix: preserve ...`，不要夹带其他计划。
- 实现者完成后由独立审查者核对差异，独立验证者在源码不变时执行受影响测试。记录实际提交或未提交 diff、命令、退出码、测试数及未覆盖范围；不建立额外冻结/哈希体系。
- 修改期间保留原有 `.agents/`、`.claude/`、`skills-lock.json` 和计划材料。只允许本计划 Scope 中的产品/测试文件，以及自己的计划状态和 `plans/README.md` 对应行变化。
- 源码漂移时先比较本计划摘录与实际控制流；合理行号漂移不阻塞。已由前置计划引入的预期变化正常承接，行为假设失效或范围需要扩大时暂停并说明最小调整。
- 新失败先归因产品、测试、工具或基线；可修正参数正常重跑，不以盲目重试或放宽断言求通过。需要新权限、真实服务器实验或改变未明确的协议语义时停止报告，不自行扩权。
- 验证缺失如实标记 pending，不把未运行项目写成通过。完成后更新本计划与索引状态；协调者维护索引时由其更新。
