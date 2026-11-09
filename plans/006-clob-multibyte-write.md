# Plan 006 / F18：完整发送远端 CLOB 的多字节文本

> 本计划修复远端 SetString 的块推进。

## 状态

- 状态 DONE。已通过 [PR #31](https://github.com/whynpc9/W.DamengProvider/pull/31) 合并为 `7bce364`。优先级 P1，工作量 M，风险 MED。
- Planned at：`963ffbc`，2026-11-08。
- Depends on：无。可与 007/008 独立实现，测试串行。
- 建议分支：`codex/006-clob-multibyte-write`。

## 目标与当前状态

默认块大小 32000 时，21333 个 UTF-8 三字节字符共 63999 字节。现有循环只发送两块 31998 字节，最后一个字符未发送，并正常返回较小计数。修复后载荷完整、终结标志准确，位置仍按服务器 ACK 的单位推进。

源码 `src/W.DamengProvider/Internal/Legacy/A/T02_02000091.cs:726–772`：

```csharp
int num6 = num2 / a().MaxLobDataLenPerMsg + 1;
for (int i = 0; i < num6; i++) {
    // 首末标志按 i 设置，随后每块可能缩短。
    num5 = DriverUtil.checkCompleteCharLen(array, num, num5, P_3);
    int num7 = A(P_0, b2, P_1, array, num, num5);
    if (num7 <= 0) return num3;
    P_1 += num7; num3 += num7; num4 += num5; num += num5;
}
```

这是节选，先打开完整方法再修改。公开调用链是 `DmDataReader.GetClob` → `Internal/Legacy/Dm/DmClob.cs` 的两个 SetString 重载 → SetStringOwned → 此 writer。只读检查、Reader 所有权和 locator 更新由既有层处理。`Dm.Config/DmOptionHelper.cs:186` 默认 32000；`DriverUtil.cs:108–121` 保持完整编码字符。

协议参考 `Internal/Legacy/Dm/T02_0200008E.cs`：SET_LOB_DATA opcode=30；ACK result 用于推进 pos、累加返回值，**不是要求等于载荷字节数或 CLR string.Length 的新契约**。保留当前空输入一帧 first|last、完全对齐的整块输入额外零长度 last 帧。保留 ACK<=0 提前返回的行为，不扩展本计划为 ACK 语义重构。

测试范例：`tests/W.DmProvider.LobTests/TruncateWidthTests.cs:158–186` 的 RecordingChannel 包装真实 channel；`ReadStreamTests.cs:144–253` 的 OutputLobFixture 使用真实 LOB 编解码，仅替换字节通道。复用它们，不创建数据库对象或新的协议模拟框架。

## Scope

允许修改：
- `src/W.DamengProvider/Internal/Legacy/A/T02_02000091.cs`，仅 string LOB writer 方法。
- `tests/W.DmProvider.LobTests/ClobWriteBoundaryTests.cs`（新增，类名 ClobWriteBoundaryTests，私有记录辅助器放同文件）。

只读参考：`DmClob.cs`、`T02_0200008E.cs`、`DriverUtil.cs`、`ReadStreamTests.cs`、`TruncateWidthTests.cs`，均按上述目录定位。
不改：byte[] writer、现代 TextReader/Stream 参数上传、SET_LOB_DATA 格式/ACK 解码、公共 API、物理池、配置默认值、任何数据库或发布工具。若现有 fixture 不能复用，优先在新测试内加窄适配，确需修改共享 fixture 时先报告范围。

## 漂移检查

```sh
git status --short
git diff --stat 963ffbc..HEAD -- src/W.DamengProvider/Internal/Legacy/A/T02_02000091.cs tests/W.DmProvider.LobTests/ClobWriteBoundaryTests.cs
git diff -- src/W.DamengProvider/Internal/Legacy/A/T02_02000091.cs tests/W.DmProvider.LobTests/ClobWriteBoundaryTests.cs
```

同时检查 staged diff；不得覆盖已有修改。预期基线 string writer 仍有固定 num6 循环。
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

1. **建立可失败的真实 writer 回归。** 新建 ClobWriteBoundaryTests；`new OutputLobFixture(newLobProfile: true)` 提供 12 字节 rowId，显式将 `fixture.Instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG]` 设为 32000（fixture 默认是 2）。创建 UTF-8 三字节文本，调用公开 SetString，捕获完整请求。不能仅测试自己新写的分块 helper。
   - 用 `Channel.AddRawReply(30, body)` 提供 ACK，`ReleaseAll()` 放行。当前 decoder 读取 30 字节 body：result@0(Int32)、id@4(Int64)、group@12(Int16)、file@14、page@16(Int32)、curFile@20、curPage@22(Int32)、total@26(UInt32)。group 不设 -1，保持远端 locator。
   - 对此固定 LongLob+12字节rowId fixture，请求 flags@65、pos@114(Int64)、length@122(Int32)、payload@126；先与当前 encoder 核对，不将这些偏移泛化为所有协议。
   - 正常 ACK 给正的、可预期服务器单位；断言后续 pos 与返回值跟随 ACK。预置足够回复，让旧实现因缺字节而失败，而不是因缺少回复超时。
   - **Verify**：`wdm_test LobTests net10.0 --filter FullyQualifiedName~ClobWriteBoundaryTests`；预期旧代码只失败于尾部/终结断言，正常边界测试能通过；保留红灯证据。

2. **用实际 offset/remaining 驱动循环。** 每轮先计算可发送字节，再按完整字符向下对齐；有剩余输入时继续，即使此前缩短了块。首标志只出现一次，last 只在真正终结；完整 cap 块之后维持零长度终结帧，空输入维持一帧。保留单块不能形成完整字符时原拒绝、ACK<=0 返回、ACK 推进单位和已有 locator 更新。不要因发送完一个块就重新编码剩余字符串。
   - **Verify**：同一 ClobWriteBoundaryTests 命令退出 0，63999 字节按序恰好发送一次；`git diff --check` 退出 0。

3. **补边界并独立验证。** 两个公开重载（非零 offset/len）、ASCII 短块/正好 cap/两 cap、空串、混合 UTF-8 三/四字节、GB18030（已有编码注册机制）、多轮边界回退；每帧不拆字符，只有末帧 last，原始输入不变。ACK<=0 不继续发送或自动重试；使用合成单位验证不把字节数当字符数。保留只读对象拒绝零发送检查。
   - **Verify**：先局部命令，再依次执行：

```sh
wdm_verify LobTests
```

检查每个目标结果，不以循环退出码掩盖某目标失败；全部必须通过。无需实库即可验收协议载荷遗漏修复，不宣称新增服务器 profile 验收。

## Done criteria

- [ ] 新测试在基线确实捕获缺失尾部，修复后各编码/边界全绿，测试数非零。
- [ ] 载荷拼接等于独立 Encoding 编码的完整输入，flags、offset 与 ACK 计数正确。
- [ ] 未改空/整块终结帧和 ACK<=0 的既有约定。
- [ ] LobTests 四目标及 netstandard2.1 构建退出 0；独立验证记录完成。
- [ ] diff 仅 Scope、计划状态及索引，无凭据或实库副作用。

## 维护重点

以后修改块大小或编码时，必须按调整后的实际字节位置判断是否完成。测试期望字节来自标准 Encoding，不能复用产品分块器生成期望。若服务器 ACK 单位/终结规则与本计划依据冲突，保留证据并报告，不能假定 UTF-16 长度就是服务端字符数。

## 交付与停止条件

- 分支使用 `codex/` 前缀；不提交或推送用户已有工作，不创建 PR/发布。需要提交时保持一个逻辑修复一条 conventional commit，例如 `fix: preserve ...`，不要夹带其他计划。
- 实现者完成后由独立审查者核对差异，独立验证者在源码不变时执行受影响测试。记录实际提交或未提交 diff、命令、退出码、测试数及未覆盖范围；不建立额外冻结/哈希体系。
- 修改期间保留原有 `.agents/`、`.claude/`、`skills-lock.json` 和计划材料。只允许本计划 Scope 中的产品/测试文件，以及自己的计划状态和 `plans/README.md` 对应行变化。
- 源码漂移时先比较本计划摘录与实际控制流；合理行号漂移不阻塞。已由前置计划引入的预期变化正常承接，行为假设失效或范围需要扩大时暂停并说明最小调整。
- 新失败先归因产品、测试、工具或基线；可修正参数正常重跑，不以盲目重试或放宽断言求通过。需要新权限、真实服务器实验或改变未明确的协议语义时停止报告，不自行扩权。
- 验证缺失如实标记 pending，不把未运行项目写成通过。完成后更新本计划与索引状态；协调者维护索引时由其更新。
