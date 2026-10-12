# TP07 E tools v3：实际三项目形状复核

2026-10-09。结论：**三种项目依赖接口修复正确；新增一个test-only路径阻塞，request_changes，暂不交Low执行本冻结离线合同。** 本轮仅静态读源、XML/JSON及文件哈希；未运行SDK、tests、validator、runner或DB，未修改实现。v2报告与实际14pass/1error失败保持原样，不回写接受。

## 冻结来源

Low `.local/temporal-native/verification/TP07-E-tools-v3/` 的toolpack manifest实算 SHA256 `f1b2b4f2c687e4f02aa7cb32ebf8be7302673527257dcc89eafbd1b022dc6ade`；24-source manifest实算 `20b786dfe64a5bc9fb55195f178daa3f50a742b584562b33e2c9b083aed24b79`。24项live/snapshot逐项一致。相对v2仅两项改变，另22项字节不变，B02逻辑与测试未改：

| 文件 | SHA256 |
| --- | --- |
| `ef-validate.py` | `8ac6a2a49e39d987b6deb27dd39b78cdb22c800035a221497cb24c3eaa156592` |
| `test_ef_contracts.py` | `7b52baaad9916a282128a9eecee053f2211795d9f87b5b2181adf1fef3a7d53b` |

旧v2源snapshot完整匹配原manifest。本次没有重跑15个计划合同，也不把下述静态推导称作一次新测试失败。

## 已闭合的实际依赖形状

本轮直接逐项读取三个冻结XML，没有再以“都是inline template”概括。`project_dependencies:139–186` 与实际声明匹配：

| 项目 | 实际冻结XML | v3规则 |
| --- | --- | --- |
| provider，csproj SHA `163c17cc…` | 无ProjectReference；W引用无Version；根props中央 `[exact]` | 无reference/host override；保留source pin、中央管理及唯一精确版本检查 |
| E，csproj SHA `d8c62b33…` | 唯一字面相对provider路径；W plain exact | 只接受该路径和plain版本，无属性default |
| L0，csproj SHA `f0bdfdd8…` | 唯一TP07ProviderProject；Condition为`'$(TP07ProviderProject)' == ''`；固定相对default；唯一`Include="$(TP07ProviderProject)"`；W `[exact]` | 精确匹配上述属性、条件、路径、引用与括号版本，不进行通用MSBuild展开 |

v3还要求assets只有net10.0恢复图：provider的projectReferences为空，E/L0唯一key及projectPath均指向同replay的provider绝对路径。只读核对已存在 `TP07-L0-v3-lock/replay/` 的provider/L0实际assets，结构正是该空集/单引用形式。新E构建assets尚待Low产生，本轮不宣称已存在E实际恢复结果。

这解决v2把`$(TP07ProviderProject)`当普通路径、把L0 `[exact]`误当plain版本的两个接口错误。新增负例涵盖缺失/更改/重复属性、未知属性引用、default/condition漂移、ProjectReference override、多/缺引用、版本范围/VersionOverride、E/L0版本形状交换、源hash漂移，以及实际恢复图重定向/错projectPath/额外引用。原中央版本/候选/官方驱动负例保持。规则未放宽为任意属性解析或版本范围。

## ETOOLS-V3-B01 / P1：新assets正例未规范化临时根，默认macOS离线窗口会误拒绝

位置：`test_ef_contracts.py:223–243`，特别`:224,236–240`；对照`ef-validate.py:129,160–162`。

测试在`TemporaryDirectory()`后使用`replay=Path(folder)`，据此生成assets的绝对provider key/projectPath。但生产helper一开始执行`Path(replay).resolve()`，期望引用字符串使用规范化后的根。当前机器只读取得的标准库临时父目录是 `/var/folders/5d/t544b0wd4yxdytzqnb3nl76c0000gn/T`，其resolve结果是 `/private/var/folders/5d/t544b0wd4yxdytzqnb3nl76c0000gn/T`；两者是同位置的不同路径拼写。

因此在默认macOS临时目录下，fixture给E/L0生成的是`/var/.../src/...csproj`，helper期待`/private/var/.../src/...csproj`。provider空reference正例可过，随后的E正例会在`actual_same_replay_provider_reference_no_override`失败，负例集合尚未执行。这是本次新增恢复图fixture的问题，不是包/SDK/数据库错误，也不是需要放宽实际graph身份的理由。

最小修复只在该测试fixture把根先规范化，例如`replay=Path(folder).resolve()`，然后从同一个根写项目、assets和期待引用。不要修改validator的精确同replay引用检查，不要删除本次正例或仅换一个碰巧无别名的TMPDIR来掩盖fixture不一致。修复后单文件新封源，再复核字节差异并让Low正常执行全部15项。

## 后续边界

B02八个reuse case的三次Independent顺序、observation/step/attempt绑定及旧重复负例与v2完全相同。本轮未发现这部分新增回归。

该test-only修复完成前，当前v3源码不作离线执行接受；修复后预计只需单点复核，无需再次扩大MSBuild分析。实际外部193源码/锁闭包、同replay provider/两个host、E的338项运行与独立cleanup仍由Low按root窗口证明。L0-v3限定接受不会自动接受E、D或完整EF-C。
