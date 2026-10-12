# TP-03 core 定点静态复核 v2

2026-10-08。结论：**static_review_accepted / runtime_validation_pending**。v1 三项缺陷在当前冻结源码中均已修复，本次差异未发现新的阻塞项。可以进入 root 安排的 Low 验证窗口；本报告不代表 TP03 总接受或 candidate 实库接受。

对照输入为 `.local/temporal-native/verification/TP03-v1/source` 及 `TP03-core-astra-review-v1.md`。只复核三项修复涉及的五个文件，并核对其余八个原范围文件未变。未运行产品、构建、测试、数据库或任何 validator API；未修改 product/test，未读取 secrets。旧 v1 报告与失败快照保留。

## 缺陷关闭判定

| v1 ID | 静态判定 | 源码与证据 |
| --- | --- | --- |
| TP03-CORE-001 / P1 | 修复成立；运行待验证 | `DmTemporalCodec.cs:44–45` 在 temporal target 分支对 TimeSpan 明确抛 NotSupportedException。调用发生在 SetTime/DateTime(ticks)/InValue 写入之前；CType21 没有 temporal target，继续原 NormalizeForWire interval 路径。TimeOnly 的既有分支不变。 |
| TP03-CORE-002 / P2 | 修复成立；运行待验证 | `DmTemporalLegacyAdapter.cs:192–200` 先按 unitsPerDay 拆日商余，仅将小于一天的余数转纳秒；处理负余数 carry 后合并日期 ordinal。`:203–218` 在 int 年域内二分定位，不引入 CLR 1…9999 限制；`:220–224` 在全部检查后一次提交原数组。 |
| TP03-CORE-003 / P2 | 修复成立；运行待验证 | `OracleDateFormat.cs:795,816–829` 将 parsedFields 交给 CompleteTemporal，在补 TIME anchor 或清 offset 前拒绝 TIME 的源日期字段和普通目标的显式时区字段。判断字段存在性，因此 +00:00 也不能绕过。普通显示 Format 路径没有加入该 numeric 限制。 |

TimeSpan 回归 `TemporalNativeWriterAdapterTests.cs:39–67` 使用真实 DmParameter 的 ResolveType、DmSysTypeConvertion 和反射调用实际 SelectBoundCType，再进入 SetResolvedObject；不是复制一套假的类型选择。两种 TIME CType、scale0/6、零/25h/负值/小数值均有单调用异常断言，且检查 InDataBound=false、InValue=null。TimeOnly 固定 wire golden 和正负 TimeSpan interval 是成功控制。该 fixture 没有 socket，源码可证明拒绝发生在编码前，不能把它描述为已运行的无 Execute 网络证据。

日期算术修复的宽度检查：日内余数绝对值小于 unitsPerDay，换算结果绝对值小于一天纳秒；原 clock 最大24:00，故合并日内量远小于 Int64 极限。年 ordinal 使用 long，int.MinValue/int.MaxValue 的 Gregorian 日数均在 long 域内；超日增量的 checked 加法与最终 int 年域检查发生在数组提交前。StartOfYear 对负年采用与 ValidateDate 相同的整除闰年规则，-1 年结束紧邻 +1 年开始，没有 year0；负年二分区间不跨过0。month/day 从同一闰年规则展开。该检查是源码整数推导，没有执行被测实现。

新增 helper 测试保留 v1 要求的独立固定年月日 expected：+200000000 分钟、int.MaxValue 分钟、-200000000 分钟、+200000 天，另有负日、非零 ns/offset 保持。真实 int 年域两端、long.MaxValue 日增量和 ref 参数失败原子性有断言；BCE→CE 与负闰年有固定 expected。原小增量跨午夜/负纳秒测试不变。

严格模板新增测试包含 TIME/TIME TZ 的日期拒绝、DATE/普通 TIME/普通 timestamp 的显式 offset 拒绝（含 +00:00），以及正常 DATE、TIME、TIME TZ、TS、TSTZ、FF7、-00:30 成功控制。显示模板的选择性字段输出另有正例，未将 numeric 约束错误施加到 display。所有负例是 Xunit 单调用 Throws，未增加包裹成功断言的大范围 catch。

## 冻结身份

五个修改文件与派发的新 SHA256 完全一致：

| 文件 | SHA256 |
| --- | --- |
| src/W.DmProvider/Internal/Types/DmTemporalCodec.cs | 0cbf4250bcdc15cc1556a39c56c2946ad3d9c4006cc4330d0017557e018cc29a |
| src/W.DmProvider/Internal/Types/DmTemporalLegacyAdapter.cs | 9c324a83e7a6ec2f4d411e93870f6fe61c4c1cbc82c3d2c73af00bdc2e8d16b5 |
| src/W.DmProvider/Internal/Legacy/Dm.util/OracleDateFormat.cs | 2172ab88c41fd643fca4f47e40a8521cd7defa621aaf0d1e28cd2ad897b95883 |
| tests/W.DmProvider.TypeTests/TemporalNativeHelperAdapterTests.cs | 392b0fa72a6d6322e880404d39e3b639ee0ca3731ee4a2bbfc077ec10bc3aba7 |
| tests/W.DmProvider.TypeTests/TemporalNativeWriterAdapterTests.cs | 4011be9bcfbad93a89e5a44aa5309c627e9da4615df6764ab9e6906c5f4d0e43 |

DmDateTime、DmTime、DmSetValue、DmGetValue、N2DB、DmTemporalCapabilityProfile、TemporalNativeReaderRouteTests、ExtendedTemporalPrecisionTests 共八文件已与 v1 source 逐字节比较相同，SHA 与 v1 报告一致。v1 对其余已审边界的静态结论继续适用；不重新扩展 interval、TP04 或 TP05 的范围。

本次静态门可关闭。后续以此冻结身份由 Low 执行针对性回归及 root 已规定的受影响 suites/实库门；任何失败均保存本轮输入与证据，再交原 owner 定点修复，不能在冻结窗口自修或改写旧报告。
