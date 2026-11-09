# Plan 007 / F06：在缩窄前验证年月间隔的 long 月数

> 执行者：仅修复 long 构造和由它导致的绑定回绕。I02 的 YEAR/MONTH 子类型转换调查不属于本计划。

## 状态

- 状态 DONE。已通过 [PR #30](https://github.com/whynpc9/W.DamengProvider/pull/30) 合并为 `aaea781`。优先级 P1，工作量 S，风险 LOW。
- Planned at：`963ffbc`，2026-11-08；Depends on：无。
- 建议分支：`codex/007-interval-month-range`。

## 目标与当前状态

`src/W.DamengProvider/Internal/Legacy/Dm/DmIntervalYM.cs:36–47`：

```csharp
years = (int)(months / 12);
this.months = (int)(months % 12);
checkSignAndReset();
this.leadScale = leadScale;
if ((double)years > Math.Pow(10.0, this.leadScale) - 1.0 ||
    (double)years < 1.0 - Math.Pow(10.0, this.leadScale))
    DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_TIME_INTERVAL);
```

12×2^32 = 51539607552 月先变为零年，再通过校验。`Internal/Legacy/Dm/DmSetValue.cs:1594–1602` 的 EncodeFrozenInterval 对普通 long 参数调用该构造器，问题不限于直接 new。现有错误使用 ECNET_INVALID_TIME_INTERVAL，保持相同异常家族，不暴露输入值。

类型契约是无法精确表示的值拒绝，不能截断或回绕。验证用 `tests/W.DmProvider.TypeTests/TemporalNativeBindingContractTests.cs:333–427` 的 BindingFixture：真实描述解码 → DmCommand.BindParameters → Packet；不需数据库。沿用 xUnit Theory/InlineData 与 BinaryPrimitives 独立读取固定字段。

## Scope 与漂移检查

允许修改：
- `src/W.DamengProvider/Internal/Legacy/Dm/DmIntervalYM.cs`，仅 long 构造器及其必要私有范围计算。
- `tests/W.DmProvider.TypeTests/IntervalYearMonthRangeTests.cs`（新增，类名同文件）。

不改 string/bytes 构造器、encode/convertTo 子类型规则、DmParameter 快照允许列表、公共类型或依赖；不把 DmIntervalYM 对象变为支持的参数输入。

```sh
git status --short
git diff --stat 963ffbc..HEAD -- src/W.DamengProvider/Internal/Legacy/Dm/DmIntervalYM.cs tests/W.DmProvider.TypeTests/IntervalYearMonthRangeTests.cs
git diff -- src/W.DamengProvider/Internal/Legacy/Dm/DmIntervalYM.cs tests/W.DmProvider.TypeTests/IntervalYearMonthRangeTests.cs
```

预期仍是强转后检查；另检查 staged 修改，保护其他工作。
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

1. **先建立构造器与绑定链回归。** 新类覆盖 ±51539607552、long.MinValue/MaxValue、会得到 Int32.MinValue 年的月数、零及正常正负月份。对可靠 YEAR TO MONTH 描述使用 `new DescribeField(20, 12, 0x190, 1)`（subtype=1，leading=9，fraction=0），参数显式 `DmSqlType = DmDbType.IntervalYearToMonth`；用 BindingFixture.Describe/Bind/Packet，不选择有争议的 YEAR-only 描述。
   - 最大 leading=9 合法值是 ±11999999999 月（999999999年11月），相邻 ±12000000000 应拒绝；另覆盖 leading=1 的 ±119 合法、±120 拒绝。精确 wire payload 的 year/month 符号和 packed scale 用独立整数期望检查。
   - **Verify**：`wdm_test TypeTests net10.0 --filter FullyQualifiedName~IntervalYearMonthRangeTests`；预期基线在回绕输入断言失败，其他正确样本能编码。若有效描述被拒绝，先校对现有描述规则，不放宽产品元数据校验。

2. **保留 long 到检查完成。** 先计算 signed year quotient 和 month remainder；不要 Math.Abs 原始 months（long.MinValue 会溢出）。quotient=months/12 不会等于 long.MinValue，可安全计算其幅值；在符号归一化和 int 赋值前验证前导精度及内部可表示的非负 int 幅值。通过后 checked 缩窄。避免对 int.MinValue 求负后仍为负；非法输入沿用既有错误类型。
   - 使用明确的有界算法以保留构造器可表示输入的既有行为：leadScale<0 按原 ECNET_INVALID_TIME_INTERVAL 拒绝；leadScale==0 的年幅值上限为0；1..9 用 long 整数算出 10^leadScale−1；>=10 的幅值上限为 int.MaxValue。不要按任意 int leadScale 循环求幂，不能让幂计算本身溢出。quotient幅值超过上限则拒绝，remainder幅值最多11；验证后才赋值符号和int字段。保留当前type及scaleForSvr赋值，不处理I02。
   - 普通绑定的 leading 由 `src/W.DamengProvider/Internal/Types/DmParameterBinding.cs:45–53` 限为1..9，advisory默认9（:108–116）；本计划不扩大该范围，也不把公开构造器所有其他leading输入一律改为1..9。新增scale=-1/0/10的构造器特征测试：-1拒绝，0保留不足一年的月份而拒绝整年，10对可表示年幅值保持原值；这些不作为新的服务器编码支持声明。
   - **Verify**：同一新增测试命令退出 0；`wdm_test TypeTests net10.0 --filter FullyQualifiedName~TemporalNativeBindingContractTests` 退出 0。

3. **检查无副作用并回归。** 通过 fixture 断言无效 long 在 Bind/编码前拒绝，无可发送 EXECUTE 载荷生成；同 fixture 随后合法绑定成功，原参数 long 保留。此 fixture 本身不连接数据库，不把它称为实库零发送验证。覆盖正负不足一年（±1/±11）、整年（±12）、跨年（±13）和 scale 边界，独立数值期望不能由待修构造器生成。
   - **Verify**：

```sh
wdm_verify TypeTests
```

各目标逐项核实退出 0，不将缺失运行时记为通过。

## Done criteria

- [ ] 所有原本回绕值、极值均明确拒绝，合法正负边界保持精确字节。
- [ ] 构造器及真实 Describe→Bind→Packet 路径都覆盖；原参数不变，失败后合法绑定可用。
- [ ] TypeTests 四目标及 netstandard2.1 构建全绿；独立审查/验证记录存在。
- [ ] Scope 外无改动，未改变 YEAR/MONTH 子类型转换语义或开放新输入类型。

## 维护重点

内部 years/months 保存幅值，negative 保存符号；缩窄与符号归一化必须一起审查。未来若扩大 leading precision，必须先调整可表示范围和 wire 合同，不能只放宽检查。若修复需要更改 encode/convertTo 或服务器对子类型的含义，停止并单独处理 I02。

## 交付与停止条件

- 分支使用 `codex/` 前缀；不提交或推送用户已有工作，不创建 PR/发布。需要提交时保持一个逻辑修复一条 conventional commit，例如 `fix: preserve ...`，不要夹带其他计划。
- 实现者完成后由独立审查者核对差异，独立验证者在源码不变时执行受影响测试。记录实际提交或未提交 diff、命令、退出码、测试数及未覆盖范围；不建立额外冻结/哈希体系。
- 修改期间保留原有 `.agents/`、`.claude/`、`skills-lock.json` 和计划材料。只允许本计划 Scope 中的产品/测试文件，以及自己的计划状态和 `plans/README.md` 对应行变化。
- 源码漂移时先比较本计划摘录与实际控制流；合理行号漂移不阻塞。已由前置计划引入的预期变化正常承接，行为假设失效或范围需要扩大时暂停并说明最小调整。
- 新失败先归因产品、测试、工具或基线；可修正参数正常重跑，不以盲目重试或放宽断言求通过。需要新权限、真实服务器实验或改变未明确的协议语义时停止报告，不自行扩权。
- 验证缺失如实标记 pending，不把未运行项目写成通过。完成后更新本计划与索引状态；协调者维护索引时由其更新。
