# TP07 FF7 mapping-v5 条件变更设计

> 阅读提示：这是 EF 时间映射的历史条件设计，不是当前 EF 适配指南或修改授权。固定候选、状态和审批仅属于当时；当前驱动行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。**conditional_design_ready / product_patch_not_authorized / mapping_v5_not_activated**。本文件准备可审实施范围，不宣布 constructor 扩展通过，不修改 mapping-v4、W 产品或工具；本轮未运行 SDK、测试或 DB。

激活条件是 [FF7 扩展设计](TP07-constructor-FF7-expansion-design.md) 的前 **52 项 literal 专属门**完整通过独立验收，包含 typed 同值控制、domain、DEFAULT、inline 谓词、assignment、来源与清理，并由 root 明确开启 High 的新 overlay 窄窗。SCREEN25 的普通值成功不足以激活。第 53 项 DATEADD 若单独失败，保留整体失败和独立 R04 blocker；只有 root 明确接受 52 项子范围后才可决定 literal 修复，不能通过此 patch 抹掉 DATEADD 失败。

## 最小产品差异

当前只读基线 `.local/temporal-native/TP07/mapping-v4/work/`。唯一拟改产品文件是 `src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTemporalParameterFacets.cs` 的 `TimestampLiteral`（`:48–64`），当前 SHA256 `62ce2de5ba52392819f3b7927ca388307d326fa0b6ae3b339903c43280c6c029`。新建 mapping-v5 overlay，保留 v4 原源和失败证据。

调用链已经足够：DT mapping `GenerateNonNullSqlLiteral` → helper；DTO mapping 将 `value.DateTime` 与原 `value.Offset` → 同一 helper。两个 mapping 类、mapping registry、parameter Scale/Precision、typed getter、query/migration generator、translator 均无需为此格式修复而改动。

| helper 最终 fraction | 拟定输出 |
| --- | --- |
| 7，无 offset | `TO_TIMESTAMP('2026-07-23 14:15:16.0000001', 'YYYY-MM-DD HH24:MI:SS.FF7')` |
| 7，有 offset | `TO_TIMESTAMP_TZ('2026-07-23 14:15:16.0000001 +05:30', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')` |
| 0…6 | 原 `TIMESTAMP '…'` 格式逐字节保留，包括 fraction0 无小数点、DTO 原有无额外空格的 offset 拼接 |
| bare、Precision=null | 原 `Fraction(..., fallback:6)`，仍走六位格式，非整微秒仍拒绝 |

分支只依据已经解析的声明 fraction；默认7、显式7、clone 后最终7都采用 FF7。`.0000000`、`.1234560` 等整微秒/整秒也必须走 FF7，不按值尾零切回旧 literal、不探测连接模式、不尝试失败 fallback。FF7 始终输出七位 ASCII fraction，不能改成 FF9 或只输出有效数字。

保留当前守卫顺序：fraction>7 的非 NULL literal 继续 `NotSupportedException`；`LiteralDomain`、`ExactTicks` 在构造文本前执行；1582-10-05…14、DTO offset 超出 [-779,+840] 分钟仍按原异常拒绝。TLTZ 的调用方拒绝逻辑不变。NULL 仍由 EF 基类生成 `NULL`，不送进 constructor；DATE/DateOnly、TIME、interval 原路径不变。

文本使用现有 `InvariantCulture` 的四位年份、24 小时制及整数 ticks fraction。DTO 使用原本地时钟和原整数分钟 offset，函数参数内日期与 offset 之间恰好一个空格；sign 取总分钟，绝对小时/余分钟均两位，须覆盖 `-00:30`、`-12:59`、`+14:00`。禁止 `UtcDateTime`、本机时区转换、当前文化的日期/符号/小数点，以及先构造旧 TIMESTAMP literal 再套函数。

## 精确测试变更清单

路径相对隔离 EF 根的 `test/W.EntityFrameworkCore.Dameng.Tests/`。无需新测试文件；在现有 TP07 SQL 文件增加必要断言即可。

| 文件 / 现有方法 | 需要更新的断言 |
| --- | --- |
| `DamengTypeMappingTests.cs:747`，`TemporalLiteralsPreserveTheSeventhFractionalSecondDigit` | 两个完整字符串期待由旧 TIMESTAMP 改为 DT 的 TO_TIMESTAMP FF7、DTO 的 TO_TIMESTAMP_TZ FF7；原 `.0000001` 和 `+08:00` 输入保留。其他 0…6、TIME、interval、mapping facet/metadata 断言不改。 |
| `TP07TemporalSqlTests.cs:13`，`TimestampAndOffsetMaximumLiteralsRetainAllSevenDigits` | 三组 max/max−1/max−9 的 DT/DTO 完整字符串期待改为对应 FF7 constructor；`.9999990` 也必须保持 FF7，不能成为整微秒 fallback。 |
| 同文件 `:98`，`ActualLocalTimezoneMappingRetainsMetadataAndNullButRejectsNonNullLiteral` | 仅其 ordinary DT/TZ 正控制在 fraction7 的两条期待改为 FF7；fraction6 期待保持原样。TLTZ 非 NULL 拒绝、NULL、clone 与 facet 断言全部保留。 |

`TP07TemporalSqlTests.cs` 当前 SHA256 `190bbb158d6d6872a9f51e29e4aa6c012dd6a88ed59efeb496320dac4ec3febc`；`DamengTypeMappingTests.cs` 当前 SHA256 `093bed9e0e6010c135e9749cd3a76212424477ceeb1d49432830fbd9da9daca7`。

在 `TP07TemporalSqlTests.cs` 补充有限回归断言，均调用真实 mapping service/GenerateSqlLiteral，不直接复制 helper 实现作为 expected：

- 默认7、显式7、`WithPrecisionAndScale(7,null)` 对整秒、整微秒和 tail1…9 都是固定 FF7；min/min+tick/carry/max 由独立完整字符串期待覆盖，DTO 极值 offset0。
- 7位 DTO 正负/零 offset（0、+330、−330、−30、−779、+840）和 `ar-SA` 等当前文化下完整字符串一致；保本地 ticks，不只比较 UTC。用明确 object 分支，避免 DT/DTO 条件式隐式转型。
- fraction0…6 逐档精确数字数目保持原输出；非对齐值继续拒绝。bare TIMESTAMP/DATETIME 与 bare TZ 保留六位、不开放 tick1；7→6 clone 也继续原拒绝。已有六位文化/数值零断言保留。
- NULL、>7非 NULL拒绝、TLTZ/clones、历法缺口、DATE 时钟拒绝与 offset 域外拒绝原断言保留；不以更新字符串为由删除负例。

`TP07TemporalMappingTests.cs` 和 `TP07TemporalMaterializerTests.cs` 无需期待格式修改，仍作为同一候选 M01/M02 回归。`DamengQueryTranslationTests.DateTimeMembersAndIntegralAddsUseDamengFunctions`（`:83`）的 DATEADD/CAST 断言无需改；functional capability probe 里的手写 CAST/DATEADD SQL 是独立诊断输入，不做全树 TIMESTAMP 文本替换。D 的 guarded default 显式取6位，其现有字符串亦无需改。

## 必须配套的 L0 接口更新

这部分属于独立工具 owner 的另行窄窗，不藏进 EF 产品 patch；没有更新就无法用原 L0 验证 FF7。

| 仓库文件 | 精确改动范围 |
| --- | --- |
| `tools/DownstreamAcceptance/TP07/runtime-templates/TP07LiteralProbe/Program.cs:77–78` | `CheckLiteral` 当前虽然列出 TO_TIMESTAMP 名字，却不允许 `YYYY-MM-DD HH24:MI:SS.FF7` 等格式串字母。新增两个**完整锚定且固定格式串**的 FF7 constructor 语法分支，保留既有六位/bare行为；禁止通过允许任意大写字母、SQL token、额外语句或任意函数来消除拒绝。长度边界继续保留。 |
| `tools/DownstreamAcceptance/TP07/runtime-validate.py:263–267` | 仅声明 scale7 的四个 shared L0 case 改为独立算出的 DT/DTO 精确 FF7 expected + SHA；scale6/TLS/bare expected 不变。不得同时接受旧 TIMESTAMP 或 FF9，不能信 producer 的 fraction-Contains 布尔。 |
| `tools/DownstreamAcceptance/TP07/test_runtime_contracts.py` | 保留 `test_six_digit_literal_prefix_is_not_exact_literal_evidence` 原六位正反例；新增7位DT/DTO完整正例，以及旧TIMESTAMP、FF9、少/多位fraction、错误日期/offset、少空格或不同format、附加SQL但重算hash的拒绝例。另以实际 C# CheckLiteral 验证两种准确 FF7 能通过、恶意额外语法拒绝；仅 Python 中复制 regex 不证明 C# 接线。 |

L0 原 case registry 的 shared12/TLS2、输入、parameter control、raw/FF9/typed oracle、业务路由与清理要求不变。修改过的工具另冻结、组合审阅；保留原 L0 丢位报告及 SCREEN25/扩展原收据，不能回写为新格式。

## 复验与交付顺序

root 接受 52 门子范围并开窗 → High 新建 mapping-v5（一个产品文件、上述两个 EF 测试文件）→ 主审源与精确范围 → Low 新 source/overlay/lock/assets/DLL 身份冻结并独立构建 → 重跑 M1 新三个 TP07 类、受影响 mapping/query 回归和14项实际参数观察 → 已冻结的新 L0 工具合同与 shared/TLS fresh L0 → 原依赖顺序 E/D。

W 继续精确包 `0.1.0-temporal.20261008144915`，DLL SHA256 `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`；不因 literal 字符串变更重建 W 或开启新驱动行为。SDK 10.0.401/EF10.0.12 与既有外部执行约束保持，测试新增后由 Low 报告实际计数，不套用旧51/102作为新结果。

直接 constructor 门证明不了 EF HasData/DefaultValue、真正 inline translation、generated readback 或 DATEADD：这些仍由新 overlay 的 L0→E/D 实际生成SQL和 fresh持久值闭环。如第53门/E.DATEADD失败，原报告、阻塞状态与后续单独归因保留；本设计不授权改 translator、换测试值或屏蔽失败，`full_EF_C_accepted=false`保持。
