# TP-03 interval R01 v2 定点复核

日期：2026-10-08。结论：**R01 静态闭环 / interval_subset_static_accepted**。仅接受本次冻结的 interval 子集，TP-03 整体及运行验收仍 pending。未运行 build、tests 或 DB，未审阅仍在编写的主 codec，未修改产品或测试。

| 冻结输入 | 实读 SHA256 |
| --- | --- |
| `src/W.DamengProvider/Internal/Legacy/Dm/DmIntervalDT.cs` | `bcecbd58ab61d740cec7da68cfe6dc9563dcb983c7e89ca1638711fd4cdeb1be` |
| `tests/W.DmProvider.TypeTests/TemporalNativeIntervalAdapterTests.cs` | `9456e9ddbb218cdd83a863110a3e28c5ac663dae77e3ff2a83dd5dfdc8730c26` |

与 `.local/temporal-native/verification/TP03-interval-v1/source/` 的两文件快照逐行比较，产品差异仅为 `Parse` 的完整 literal 捕获及六种复合 subtype 正则；原测试内容未删除或改写，新增 52 个声明用例（尚未执行）。旧 v1 的 changes_requested 报告与快照保留。

六种复合 subtype 使用嵌套可选组，只允许从 leading 开始的连续前缀；缺失尾部分量沿既有 `Number` 分支置零。冒号与后续数字同组，fraction 仅位于实际秒分量后；空洞、尾随/重复 separator、额外字段及提前 fraction 不会由这次改动放行。leading、日内分量和目标精度仍经过原 `ValidateState`，未绕开范围或无损检查。

完整 literal 现将引号及可选外部单负号一并交给原符号解析，恢复 `INTERVAL -'1' DAY(2)`。外部负号使 `signSeen=true`，引号内再次出现正负号即失败；重复外部符号无法匹配完整 literal。引号内单符号仍保留，完整 literal 外部正号没有因本次修复新增。两种 constructor 共用此逻辑；显式声明冲突检查及省略 precision 使用构造参数的规则不变。

新增测试覆盖默认 DHMS 三种前缀、全部六种复合 subtype 的合法前缀、两种 constructor 的完整 literal/内外单负号、缺省 precision 及声明冲突，另有 31 项坏分隔/提前小数/双符号负例。成功案例的 fields、packed scale、独立 ticks 和完整格式均直接断言，不位于负例 catch 中；期望值未由新编码器生成。未发现本次差异引入新的静态阻塞。

Low 仍须等待主 codec 与工具整体停写、新候选冻结和 root 的唯一窗口通知，再执行 interval 新测试、既有 TextTemporalContractTests 及同构建完整 TypeTests；不能把本次静态接受计为 52 项运行通过。

本报告完成后停止写入。
