# TP-01 components只读诊断静态审阅

日期：2026-10-08。最终结论：`diagnostic_v2_static_accepted / build_and_database_not_run`。仅审阅Program的独立入口、ComponentOracleDiagnostic和README；原main三lane/validator、产品及v7失败证据未改。允许root按已批准范围交Low新冻结构建和只读诊断，实际结果尚未产生。

## v1缺陷保留

v1新增文件SHA为 `9f6ce9fac7d01ce9bb99a473eb6bdf7ab4c1b2ce98511eb829db990484abc02e`。静态发现：Q0/Q2的exact标志在最后一次Read完成检查之前写入，非infra catch可将status改failed但保留旧true；summary仅合取exact，可能在后续Read失败或存在多余row时仍称Q2_oracle_usable。root接受此缺陷，v1未交Low执行，旧输入由root/Low保留。

## v2极小修复接受

新文件SHA实读：`4eaf7731c869747ab0edafdd70152729188e605a10b54707f328a45e7d9d2393`。summary分别要求Q0a、Q0b、Q2的`status=observed`且`subsequent_read_completion=true`，之后才计算source/offset、cast-local/fraction、Q2-numeric的精确标志。

因此任何一个必要阶段失败/未完成，都不能令Q2_oracle_usable=true；source/cast的incomplete标签、basis及projection classification与新判定一致。basis只声明已证source/cast同local字段，不将Q2失败称为numeric通过。Q1保持独立观察，不成为Q2 usable的前置门，其原错误/量化结果不会被新cast路径覆盖。

## 只读范围与证明链

- Program只增加diagnose-components入口和对应O profile预检，分支位于CheckIdentities/SourceBinding/Configure/Open之后、Reserve之前。仅shared；该分支不创建/读取ownership文件，不执行Cleanup，不引用旧表。
- 新文件所有业务SQL均为SELECT/DUAL：O源公开DTO参数Precision7；W源显式VARCHAR80→TZ7双CAST文本参数。W文本源明确是CAST功能表征，不是typed写入证据。
- 16个规范向量×O/W：四offset×三fraction，及offset0的Max/Max-1/Max-9、闰日末tick。Q0a/Q0b/Q1/Q2每条独立连接，实际TEST/schema/profile检查不绕过；无DDL/DML/session设置或业务schema查询。
- Q0a比较完整local字段/fraction/原offset；Q0b比较同一独立expected的local字段/fraction，不因cast转UTC/session时间而改expected。Q2两种numeric getter的六分量都精确才numeric_exact；不允许tolerance、微秒量化或用另一个TO_CHAR替代numeric结果。
- 实际CType/precision/scale、CLR数值类型、GetDecimal、真实cache长度/hash被记录；hex仅限合成numeric cell及64-byte上限。异常只存安全结构，不输出连接、secret、原message或认证数据。
- 非infra查询错误本地保留，后续对照继续；真实身份/timeout/socket/cancel等仍停止。diagnostic_complete仅代表观察流程完成，不是profile或projection整体接受。

未发现此次限定代码的明显编译/API或SQL组装问题；实际CAST语义、元数据与反射可观察性必须由Low验证，不由静态审阅预判通过。

## 当前三个冻结输入

| 文件 | SHA256 |
| --- | --- |
| Program.cs | `e8261ed3ee38313550571fc023583d1f7c91c9d33cecba0bf71c63e9162614b6` |
| ComponentOracleDiagnostic.cs（v2） | `4eaf7731c869747ab0edafdd70152729188e605a10b54707f328a45e7d9d2393` |
| README.md | `b773cb800644b3d1cd5bedd7b235c75a711fc5ca79f25e365b1fd00107109240` |

Program/README与v1冻结SHA一致。Low应使用新private offline build/selftest产物和其frozen-input身份链，再经原TEST wrapper执行README的diagnose-components命令；unused ownership参数不得产生文件。原v7三lane与failed validator继续只读，诊断成功也不改写它们。

本审阅未执行build、DB或测试。本文保留v1发现及v2结论，完成后停止写入。
