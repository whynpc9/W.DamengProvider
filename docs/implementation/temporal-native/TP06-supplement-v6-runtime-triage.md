# Supplement v6 负interval服务器显示：v7最小裁决

> 阅读提示：这是历史失败归因与工具修复记录，不是当前产品缺陷结论或操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。结论：**真实server显示采用引号外单负号，现工具server guard尚未覆盖；批准仅工具server通道增加此精确形式**。W规范文本、产品public parser/formatter及其它验收门不变。本次只读核验，无SDK、tests、DB或代码修改。

## 实际证据

`TP06-supplement-v6-shared-candidate/supplemental.json`实读SHA256 `5e8d839907817372bfe9ea99c9039602d26872c7bc91de5274173e80bec7a138`；`TP06-supplement-v6/independent-runtime-summary.json`实读SHA256 `8d2f68a153229bcee3a4e18ee765a9f57a7f5d6ab9939c8940d8c31ae9bb82f1`。

NATIVE_INTERVAL_neg的actual ordinary raw为 `FFFFFFFFFFFFFFFF0000000000000000C01DFEFF96060000`，长度24，SHA `1f4635fd456113d5f04b7d24fc3efd3b6dd3ae510b986160c61c95625b971540`。按little-endian整数为day=-1、hour=-1、minute/second=0、microseconds=-123456、packed=0x696；其值为-900001234560 ticks，与实际TimeSpan getter一致。

- W GetString：`INTERVAL '-000000001 01:00:00.123456' DAY(9) TO SECOND(6)`，57字符，hash `57fafeae47ce4d94248ff102710b9980973288058bc4cf17083a609d5572704e`；ValueTextGuard通过。
- server CAST：`INTERVAL -'1 1:0:0.123456' DAY(9) TO SECOND(6)`，46字符，hash `c35bb5bc06cc10a5245f3f051d3688a2177b06c54c48f6d5c98d21cc444fac36`；ServerStringGet完成，随后ReadServerTextGuardFailed。

因此本次失败点和原因已由实际分步证据确定，不是raw/typed/值文本错误。TP03已批准产品public Parse接受外部单负号并拒绝双符号，但那是独立产品合同；本次不改产品，也不据其扩大工具任意literal语法。

## root批准的server单符号规则

在v6完整wrapper、ASCII、DAY(2/9)/SECOND(6)、day/HMS显示宽度、整数范围、六位fraction和实际packed限定词匹配规则上，只新增：

`INTERVAL -'days h:m:s.ffffff' DAY(L) TO SECOND(6)`

`INTERVAL`后仍恰好一个ASCII空格；负号紧邻开引号，不允许外部正号、额外空白、多个负号或改变关键字位置。

server解析分别取得outer-minus与既有inner-sign，然后执行：

1. outer-minus存在时，inner-sign必须为空；即使内外符号相互抵消也拒绝。
2. 没有outer-minus时，保留v6既有引号内单个`+`/`-`/无符号形式；不新增其它符号位置。
3. 整体negative由唯一符号决定，各day/hour/minute/second/microsecond均解析为非负幅值；分量中夹带符号一律拒绝。
4. 负号配全零幅值仍拒绝negative zero。实际符号/分量必须与独立raw解码和固定expected一致，不能只因字符串可解析便通过。

W value通道继续只接受引号内规范负号、DAY按L补零、HMS两位、fraction六位及独立整数canonical精确字符串。外部负号即使数值正确也不得进入W value通道。不得把server文字改写成W形式后再当原文验收。

## 实施与反例

High producer在原13文件授权范围内只窄改server ParseIntervalText的外部单负号识别/冲突检查及必要helper合同；validator owner在原4文件中独立实现相同有限规则。沿真实metadata/packed选择声明的路径保持。非interval parser、Cases.cs 0360…、SQL/API、case/vector集合、恢复、诊断、source/package identity及cleanup不变；manifest只同步新设计pin/必要检查ID。当前server负数synthetic fixture可改用已观察外负号形式，但保留内负号的独立正控；W fixture不变。

最少控制：

- 本次冻结raw与两个literal/hash同时作为正控；独立整数结果必须为-900001234560，server外负号可接受，W相同外负号必须拒绝。
- server原内负号、无符号正数及v6已有正号控制保持；DAY2/9非零负数仅标synthetic expected-only，不冒称实库全部观察。
- `INTERVAL -'-1 …'`、`INTERVAL -'+1 …'`、`INTERVAL --'1 …'`、`INTERVAL +'1 …'`、`INTERVAL - '1 …'`、`-INTERVAL '1 …'`、分量中负号及内/外negative zero全部拒绝。
- 去掉外部负号、反转符号、改任一数字/微秒，或改raw sign/packed、schema、typed ticks，均不能通过完整独立validator。
- 错qualifier、超域、非六位fraction、bare/残缺wrapper、Unicode数字、前后空白等旧v6负例仍拒绝；不为外负号新增Trim或词法宽免。

新freeze经Astra静态复核、Low完整离线后，再由root安排新完整shared/TLS运行。保留v6失败及旧所有报告，不回写通过；leading未批准server号仍pending。其它未观察的server形状继续fail-closed。此裁决只关闭当前已证工具语法缺口，完整Supplement/TP06仍待实际验收。
