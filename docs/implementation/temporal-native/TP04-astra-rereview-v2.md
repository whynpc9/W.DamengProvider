# TP04 两项测试修复定点复审 v2

2026-10-08。结论：**static_accepted_for_independent_verification**；v1 的 TP04-R1、TP04-R2 均 **closed_static**。可交 Low 在统一新冻结上执行独立离线验证。尚未运行构建或测试，不关闭 TP04 runtime、TP05、同包实库或发布门。

本次只审查两份测试相对 `.local/temporal-native/verification/TP04-v1/source/` 的变化和被调用 helper 的可访问性。两项 SHA256 已实读匹配：

| 文件 | v2 SHA256 |
| --- | --- |
| tests/W.DmProvider.TypeTests/TemporalNativeBindingContractTests.cs | 394c295df08ace8bca46775a405a26fa12e614f6077ebe7f6235ed99a146f71b |
| tests/W.DmProvider.TypeTests/TemporalNativeFrozenCodecTests.cs | a07c50bb3948ba144741a720675ae18eb62a3fd653cebc488566d1593069aaaf |

v1 审阅的其余十个文件逐字节与保存的 v1 source 一致，包括九个产品文件和 MetadataContractTests。未修改产品、测试、旧报告或 evidence，未执行 dotnet、DB，也没有将 TP05 纳入此次定点接受。

## R1 闭环

`TemporalNativeBindingContractTests.cs:79–98` 保留四种 offset 的 advisory CLR DateTimeOffset 数值正例与原字面量 golden；文本输入前在 `:93` 重新通过完整 Prepare frame 和共享 decoder 建立 fixed `CType27 / precision11 / scale7 / flag1`。文本 binding 明确断言 ReliableFixed，因此不再必然撞上原 advisory extended 文本守卫，能力边界未扩大。

新增 `:102–113` 的 advisory 文本负例保持 raw CType2/flag2，要求原 NotSupportedException、field binding 不发布且发送计数不增长。异常范围只覆盖 Bind 调用；反射 helper 仍重抛原 InnerException，没有吞错。该计数是直接 Bind fixture 的离线控制，不宣称已经运行完整 Execute invocation 或实库零持久化检查；业务 Execute 位于成功 Bind 后的既有产品顺序没有变化。plan/field 的整体发布仍在绑定循环成功后。

## R2 闭环与 helper 可访问性

`TemporalNativeFrozenCodecTests.cs:173–187` 已删除直接 CaptureTemporalServerMetadata 赋来源。新路径为 `BindingFixture.PrepareFrame` → MemoryStream → `DmFrameReader.Read` → 生产共享 `c.A` → statement 中实际生成的唯一 parameter field。先断言 decoder 产出的完整 raw record、HasServerSource 及 effective type/precision/scale，再供原 writer/descriptor 合同消费。合成报文经过真实 decoder 的来源合同成立；这仍是离线合成输入，不是实库 observed metadata。

检查了 helper 入口：BindingFixture、DescribeField 及 PrepareFrame 均为同一 TypeTests 程序集可访问的 internal；WriterFixture.Statement 也是 internal，decoder 入口 `c.A(b, A)` 可被本测试程序集访问，产品 csproj 已声明 W.DmProvider.TypeTests 的 InternalsVisibleTo。新增 using/LegacyBuffer alias 与现有类型匹配，未发现静态可访问性阻塞。新 Fixed fixture 使用原物理连接构造好的 statement；共享 decoder 为本次 Prepare 新建 DmInfo/参数数组并发布回 statement，不依赖先手工造来源。

与 v1 做完整差异比较：FrozenCodec 的八个 Fact 主体和原断言均未改；只有 using 与 Fixed helper 变化。原冻结 profile 变更反例、数值/文本/NULL 同源、请求与列双 quantum、interval packed、Unknown/ordinary12/TLTZ 拒绝等控制保留。BindingContract 的变化限定为 R1 正例修正和新增负例。

## 下一门

Low 应在 root 统一冻结的当前源码上构建并运行完整相关离线套件，包含四个 offset theory、新 advisory 文本负例和原八个 FrozenCodec Fact。任何编译或运行失败保留新候选证据后交回归属 High；不能把此次静态接受写成运行通过，不能拼接旧 TP03 包作为 TP04/TP05 同包验收。v1 报告与冻结保持原样。
