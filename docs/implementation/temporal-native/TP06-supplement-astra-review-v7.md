# Supplement v7 外部单负号定点静态验收

2026-10-09。结论：**static_accepted_for_independent_verification**。此次只复核 [v6运行triage/v7设计](TP06-supplement-v6-runtime-triage.md) 的有限外负号改动及对应控制；无确认的新静态阻塞。没有运行SDK、Python测试、consumer或DB，没有修改产品/工具。

Low快照 `.local/temporal-native/verification/TP06-supplement-v7/source-manifest.json` 实读SHA256 `bcb2a2c866dcc7d515c89ead45c1023bfde58f3549ba789b379b270b6a91e0a8`。17份工具SHA已实读匹配交接，root/owner提供aggregate `ff77510eb2654e3286b2fa56a8cfc80c430d97218dc9708c34df079b6c006b4a`，两个v11依赖不变。

关键变化为Observations.cs `1385627c9a2ec3c5d147e43e40c9d01935fa9415bec2d743c4f490fd615666fb`、contracts.py `5aeb007695b32cef7dce45f4de537c871714e1c37a434a13d1cdb4ef17a7e20c`、test_validator_contracts.py `db48e72893f0a50a926c108967bfdaae47330f41b6ecc45b24f411e19202f76b`、case-manifest `2c08f624c5c40e8f37876c84dc935bd24bf481ed907000af58fb36647cd56ecc`。validate.py保持b1b895…；Cases.cs保持0360…，原SQL/API/case/vector不变。manifest只新增设计pin `563f52b307cd4692332568174b71e402514b6f3aba7469081f7dfe3c596c1163`。

C#与独立Python均只在完整INTERVAL wrapper中新增紧贴开引号的可选单个`-`。外负号存在时任何inner sign均被拒绝；没有外负号时保留原server内单号合同。整体negative由唯一outer/inner minus确定，其余分量仍只允许非负ASCII数字；double sign、outer plus、额外空白、关键字前置负号、分量内符号和negative zero均不能通过。

value通道额外强制outer为空，保留原规范内负号、补零与独立整数canonical精确比较。即使server外负号文本补齐全部数字，仍不能用于W GetString。DAY/SECOND限定词、实际packed、范围、小数恰6位及完整匹配边界未变化；没有Trim、舍入或符号抵消规则。

新增正控固定本次actual raw、W与server两个literal/hash，独立整数ticks为-900001234560；原server内负号继续有正控，DAY2其它输入明确synthetic。反例覆盖内外冲突、outer plus/双号/空白/分量sign/negative zero、W误用outer，以及去号/改微秒/改day/raw sign后完整validator拒绝。expected不调用产品formatter/encoder。

旧160项Python方法保留，加6项至166；C#同路径helper检查保旧52加4至56，runner/validator精确ID列表同步。尚未执行，不将这些控制或已知负值前缀事实记成完整profile通过。

v6已接受且未变化的非interval路径、实际metadata/raw/typed/schema交叉检查、G2有限read规则、v4恢复、诊断安全输出、同包身份、ownership/cleanup及leading未批准号pending继续有效。本轮未发现静态接口/序列化退化。Low可先完整离线，再按root安排新完整运行；集合外格式仍fail-closed，旧v6及所有历史失败不回写，完整Supplement/TP06仍待实际接受。
