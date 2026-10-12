# TP-03 core FF lexer 定点复核 v5

2026-10-08。结论：**static_review_accepted / runtime_validation_pending**。本次限定修复与 G5 已批准的整数纳秒模板合同一致，未发现阻塞；可以由 root 冻结后交 Low 重跑。没有执行产品、构建、测试或数据库，没有修改 product/test。

对照 `.local/temporal-native/verification/TP03-v4/source`，OracleDateFormat 的唯一产品差异为 AnalysePattern 在 bare FF 前新增 FF7、FF8、FF9 三个 ASCII token 分支：分别设置 len=7/8/9、加入已有 FElement、推进三个字符。原 FF1…6、bare FF 默认6及其余 token 分支未变。新宽度可到达已有 ns Parse/Format；FF0、FF10/11/99 和 Unicode 数字宽度仍会留下字母数字字符，由原 default 抛6015。

HelperAdapterTests 和 WriterAdapterTests 仅增加测试，没有删除或削弱 v4 三个失败测试的原断言。新增覆盖宽度1…9的独立固定 ns expected、bareFF、短 fraction 右补、leading zero、大小写、FF8/9 原始 sub-tick、非法宽度/Unicode和超宽文本。实际 writer fixture 明确保持 target/requested scale7；FF8/9 的100ns文本有成功控制，10ns/1ns仍要求 Overflow，非法 token 检查 InDataBound=false/InValue=null。异常断言仅包围被测调用，没有扩大 catch 吞掉其他断言。

DmDateTime、DmTime、DmSetValue、DmGetValue、N2DB、DmTemporalCodec、DmTemporalLegacyAdapter、DmTemporalCapabilityProfile、TemporalNativeReaderRouteTests、ExtendedTemporalPrecisionTests 共十个原范围文件与 v4 逐字节相同；DmConnectionStringBuilder 也相同。故本修复不开放八/九位服务器写能力，不改变 profile，也不开放 NLS Builder 设置。

| 本次实读文件 | SHA256 |
| --- | --- |
| src/W.DmProvider/Internal/Legacy/Dm.util/OracleDateFormat.cs | 2e7417b30bad4ae41df21bc31bf77ee980fe51338360d60185ac0627a9987f04 |
| tests/W.DmProvider.TypeTests/TemporalNativeHelperAdapterTests.cs | d6cf03f13aab6c9b7ff81b7e874aab2ea450f642ac15c765c65b44731cb9b08b |
| tests/W.DmProvider.TypeTests/TemporalNativeWriterAdapterTests.cs | ec595222fa110d946b3cb58f9b5cb39d90718868b07a7efccc2dedf1fe5f1627 |

三个哈希均与派发值匹配。v4 的231通过/3失败是旧候选运行证据，后续四门当时未执行；本报告不能将其合并为 v5 或 TP03 通过。v2/v3 编译失败、v4 lexer失败及其源码快照继续保留；先前静态复核漏查 lexer 可达性的事实不回写。运行验收仅由 Low 针对新冻结身份给出。
