# Plan 008 / F17：数组绑定先验证所有形状，再分配行快照

> 执行者：先建立安全的分配增长回归，再做两遍处理；保留有效输入的完整快照语义。

## 状态

- 状态 DONE。已通过 [PR #29](https://github.com/whynpc9/W.DamengProvider/pull/29) 合并为 `0004040`。优先级 P1，工作量 S，风险 LOW。
- Planned at：`963ffbc`，2026-11-08；Depends on：无。
- 建议分支：`codex/008-array-shape-preflight`。
- 与 Plan 010 的关系：两者修改 DmCommandPlan.cs，先完成本计划再交给 010，禁止同文件并行写入。

## 目标与当前状态

`src/W.DamengProvider/Internal/Execution/DmCommandPlan.cs:256–268`：

```csharp
var rows = new DmParameter[count][];
for (int row = 0; row < count; row++) rows[row] = new DmParameter[parameters.do_Count];
for (int index = 0; index < parameters.do_Count; index++) {
    DmParameter parameter = parameters.do_GetParameter(index);
    if (parameter.do_Value is not Array values || values.Rank != 1 || values.Length != count)
        throw new InvalidOperationException(...);
    // 逐行 clone、类型推断和 byte[] 深复制
}
```

当 count 很大而实际参数是标量/短数组时，参数错误先造成大量行数组分配。目标是所有参数的 rank/length 预检在任何按 count 分配或逐值快照之前完成；无效形状成本仅随参数数目增长。

既有 `tests/W.DmProvider.CommandTests/ArrayBindingAndBatchTests.cs:13–69` 用 Capture helper 和 Theory 组织 shape 测试，并验证 NULL 类型继承及 byte[][] 深复制。`DmParameter.CloneForArrayRow` 拒绝 Stream/TextReader，非 NULL 混合推断类型仍拒绝；这些成功/拒绝语义保持。

## Scope 与漂移检查

允许修改：
- `src/W.DamengProvider/Internal/Execution/DmCommandPlan.cs`，仅 CaptureArrayRows 形状预检与必要局部变量。
- `tests/W.DmProvider.CommandTests/ArrayShapePreflightTests.cs`（新增，类名同文件，私有 Capture helper 可沿现有短模式）。

不改：公共 ArrayBindCount API、行数上限、自动分块/重试、绑定协议、类型推断策略、参数集合并发规则、任何依赖或服务器配置。现有 ArrayBindingAndBatchTests 保持只读回归样例。

```sh
git status --short
git diff --stat 963ffbc..HEAD -- src/W.DamengProvider/Internal/Execution/DmCommandPlan.cs tests/W.DmProvider.CommandTests/ArrayShapePreflightTests.cs
git diff -- src/W.DamengProvider/Internal/Execution/DmCommandPlan.cs tests/W.DmProvider.CommandTests/ArrayShapePreflightTests.cs
```

检查 staged diff，预期仍是先分配再检查。
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

1. **建立有界回归，不能用 OOM 当测试。** 新类至少覆盖第一个/第二个参数分别为 scalar、rank>1、length不匹配；第二参数错误时前参数数组形状合法。使用 count=64 与32768，输入数组、command、delegate 在计量区外准备并预热，同一同步线程内用 GC.GetAllocatedBytesForCurrentThread 记录 Capture 抛预期异常的分配，断言：大 count 分配小于小 count 分配加 `32768 * IntPtr.Size`。这是宽松的增长上界，不是平台精确分配字节；旧实现光行数组就应超过它。不要 GC.Collect、计时阈值或 count=int.MaxValue。
   - 另用 count=2 验证全局预检顺序：第一个参数 shape 合法但含禁止的 Stream，第二参数 shape 非法；应先报第二参数的 shape InvalidOperationException，不能先克隆第一个值而报 NotSupportedException。输入 Stream 在测试自行释放。
   - **Verify**：`wdm_test CommandTests net10.0 --filter FullyQualifiedName~ArrayShapePreflightTests`；基线应因增长/预检顺序断言失败，不能是进程 OOM/工具错误。

2. **拆为预检与快照两遍。** 保持 count<0、无参数原检查；第一遍检查每个 Value 是一维 Array 且 Length=count，不分配 rows、不 CloneForArrayRow。第二遍才创建 rows 并沿现有 `values.GetLowerBound(0)+row` 索引克隆、推断与继承 NULL 类型。可保留参数数目级别的引用缓存；不再制造 count 级预检缓存。原异常说明继续指向错误参数。
   - **Verify**：新增测试退出 0；`wdm_test CommandTests net10.0 --filter FullyQualifiedName~ArrayBindingAndBatchTests` 全绿。

3. **确认兼容边界并独立回归。** 新增非零 lower bound 的一维 Array 正常快照、源数组/byte[] 行修改隔离；保留 mixed-type、全NULL、显式类型、rank、scalar、空集合、count=0 普通参数路径的既有行为。合法形状但禁止的 Stream 在形状全部通过后仍按原异常拒绝。
   - **Verify**：

```sh
wdm_verify CommandTests
```

逐目标确认完成，保存新分配测试的实际增长结果；不要因噪声任意提高阈值，先检查计量区是否包含输入准备/JIT/异步切换。

## Done criteria

- [ ] 无效第一/后续参数都在 rows 分配及任何逐值复制前拒绝。
- [ ] 基线增长/顺序回归能红，修复后四目标通过，未靠 OOM 或速度断言。
- [ ] 合法快照、NULL 推断、深复制、非零 lower bound 和原拒绝路径不变。
- [ ] CommandTests 四目标与 netstandard2.1 构建通过；独立审查/验证完成。
- [ ] 交给 010 的 diff 明确列出 CaptureArrayRows 两遍逻辑与新测试，010 必须保留这两个回归。

## 维护重点

后续添加其他 Array 类型时，先校验形状再分配仍是必要顺序。真正的合法大批次依旧可能分配较多内存，本计划没有承诺其分配与 count 无关；不要把错误输入优化演变为任意批量上限。

## 交付与停止条件

- 分支使用 `codex/` 前缀；不提交或推送用户已有工作，不创建 PR/发布。需要提交时保持一个逻辑修复一条 conventional commit，例如 `fix: preserve ...`，不要夹带其他计划。
- 实现者完成后由独立审查者核对差异，独立验证者在源码不变时执行受影响测试。记录实际提交或未提交 diff、命令、退出码、测试数及未覆盖范围；不建立额外冻结/哈希体系。
- 修改期间保留原有 `.agents/`、`.claude/`、`skills-lock.json` 和计划材料。只允许本计划 Scope 中的产品/测试文件，以及自己的计划状态和 `plans/README.md` 对应行变化。
- 源码漂移时先比较本计划摘录与实际控制流；合理行号漂移不阻塞。已由前置计划引入的预期变化正常承接，行为假设失效或范围需要扩大时暂停并说明最小调整。
- 新失败先归因产品、测试、工具或基线；可修正参数正常重跑，不以盲目重试或放宽断言求通过。需要新权限、真实服务器实验或改变未明确的协议语义时停止报告，不自行扩权。
- 验证缺失如实标记 pending，不把未运行项目写成通过。完成后更新本计划与索引状态；协调者维护索引时由其更新。
