# TP-02 v2 Astra 定点复审

日期：2026-10-08。结论：`static_accepted / runtime_pending`。初审 R01、R02 均已在源码层关闭，可由 root 冻结 v2 后交 Sol Low 进入唯一构建/测试窗口。本复审未执行 dotnet、测试、数据库或 secrets 操作；静态接受不等于 TP-02 运行验收关闭。

原 [初审报告](TP02-astra-review.md) 保持不变，SHA256 仍为 `2e6a85b4f26b011b621971239fbde3495c9e2092d1ecf5f80ab15d2e821d7965`。本轮只读检查两处测试修复及范围不变性，仅写本报告；TP-01 probe 不在审阅范围。

## R01：公开 theory 的可访问性问题关闭

`TemporalNativeWireTests.cs:9–16` 现为五个 int CType InlineData（14、15、22、16、23），public theory 方法接收 `int cType, string hex`，在方法内部经 `DmTemporalWireCodec.ForCType` 选择布局。公开签名不再暴露 internal enum，产品 enum 的可访问性未扩大。

五个原 hex golden、对应日期/时间/offset分量以及 encode/decode 两侧断言保持；没有减少case或更改预期。实际编译仍由 Low 确认。

## R02：成功控制与预期异常的边界关闭

`TemporalNativeFixtureTests.cs:29` 直接执行 `Exercise(vector)`，不再用 `Record.Exception` 包裹整个case。异常捕获只在 `AssertTargetThrows` 中作用于传入的单个目标操作；expected解析、JSON字段构造和成功控制都在捕获边界之外。

| 路径 | v2复核 |
| --- | --- |
| scale6负例与scale7 control | `:73–78` 的scale7校验、CLR fields及精确ticks断言先直接执行；只有随后`:80`的指定scale校验进入目标异常断言。scale7错误Overflow现在直接使Fact失败 |
| 合法scale8/9 subtick格式化 | `:93–94`先直接调用FormatExact并比较独立expected文本，随后`:96`单独检查CLR投影的clr_exception。FormatExact错误Overflow不可能再冒充CLR拒绝 |
| 声明scale非法的格式化 | `:87–90`先确认format_rejection分类，再仅对FormatExact检查Overflow；不进入合法文本或CLR分支 |
| DTO构造器与raw投影 | CLR DateTime/offset准备、JSON→fields转换均在目标捕获之外；预期异常只包围DTO构造器或指定CLR投影 |
| currentWWriterGuard表征 | 新核心local/UTC正例先成功，再单独调用旧Normalize守卫并比对当前表征；没有变成新profile能力开关 |
| interval与legacy12负例 | ticks/scale/leading或raw fields准备在外；仅FromTimeSpanExact或ToLegacyMicroseconds目标调用捕获。正例继续直接执行并核对精确数值 |

全16种operation仍有明确消费分支，未知operation继续失败。77条case的唯一ID、expected_only/not_run/server_evidence标志和总数断言保留。没有发现其他成功阶段被同型异常捕获吞掉的问题，也没有为使消费者通过而调整独立fixture预期。

## 输入与不变性核对

本轮回读的修复文件SHA256与High/root提供值一致：

| 文件 | SHA256 |
| --- | --- |
| `TemporalNativeWireTests.cs` | `d2808d61cf3ec392076dc1baebbd0e27fc1c3554d47905ebf0862af1e67d028a` |
| `TemporalNativeFixtureTests.cs` | `6382b6c18c27782085d71c9e1e1dd6c1386826a40feae382ed608dc8112020f5` |

逐项与初审表比对，另八个TP-02实现/测试/项目文件全部SHA相同：五个核心、CoreTests、IntervalTests、TypeTests csproj。Luna JSON、generator、README 三项同样未变，其中JSON仍为 `4eee2002106920423c75a2126ac557b96e51bae090ee8e1af2518a179b62a609`。因此初审对核心整数算法、wire布局/单位、CLR范围、metadata不可变及未接产品入口的静态结论继续成立。

root给出的10文件aggregate为 `17114501942b7c684659e5efc556df2ec47f882522e43f577d506dd4f4187a80`；本次独立复核的是上述逐文件SHA，不把未重新计算的aggregate算法冒称为已独立验证。Low应按其manifest格式重算该aggregate及首尾不变性。

## 正式 Low 开窗建议

同意 root 生成/确认 v2 冻结manifest后按 [初审报告的 Low 方案](TP02-astra-review.md) 执行：SDK10.0.203及指定环境变量、单节点/noReuse/noSharedCompilation/disable-build-servers；先目标 `FullyQualifiedName~TemporalNative`，成功后同一构建产物完整TypeTests回归。预计目标56个xUnit叶子仍不变（Core39、Wire9、Interval7、Fixture1）；Fixture1内有77逻辑向量，数量不与TRX叶子相加，正式计数以实际TRX为准。

首个失败保留自然退出码和输入/日志后释放窗口，再交High；无skip/abort才计通过。验证本次fixture被正确复制到输出目录并核对hash。五核心仍未接线，不需要TEST wrapper或数据库，也不以离线通过宣称七位profile、公开helper迁移、TLTZ或EF已完成。

本报告完成后停止写入；最终TP-02接受须由Low实际结果与root收口决定。
