# TP07 E精确generic advisory负例：四文件源码验收

2026-10-10。结论：**exact_generic_TLS_negative_source_accepted / Low_offline_and_new_cohort_pending / DTO_live_pending**。按triage `cf25afd9283f8ec87b0921f33ecebcc7a12986dada5a74068be3d8d58c8702e6`、E-tools-v10及真实失败收据核对，未发现源码阻塞。本报告只接受受限实现，不接受runtime，也不把DTO synthetic合同当实际观察。

本轮只读源码/JSON，计算hash、AST计数及字节差异，未执行SDK、tests、validator、DB或wrapper，未改source、旧证据或status，仅新增本文。

## 冻结范围

| 文件 | 实际SHA256 |
| --- | --- |
| SixPrecisionCases.cs | `6acc99f9d1ba115cb21ca024852398d94f908f2a59aac1c36c584e61e51da540` |
| ef-validate.py | `03943e530a98731c439393d6c940f24e85dae3f9742a2b3d46aaf6095accd9fd` |
| test_ef_contracts.py | `4b5aabf7d6b4793df4196e0ea89bffe40f0f287e9a279348de80abcc2647e5c9` |
| ef-cases.json | `2181388ea81edac2c0b0d5e43d0394430a2a495ca33a1903e801ddaf6588857d` |
| policy E9 aggregate | `d3e2227ed642e40d420ff0791dded586fb646ef26c6bb4c5faaad80bf9d41a43` |

相对v10的24项snapshot只有上述四文件变化。policy只改变两个固定TLS advisory case的contract标签、SixPrecisionCases pin与aggregate；原338 IDs/顺序/profile/group/shape/kind/ticks/offset/scale/API、counts和25窗口descriptor不变。Program/RuntimeHarness、Models、CommandAudit、runner、其它E源、W、mapping/common及L0未变。

全局Python `source/raw_tuple/published/send_refusal`逐函数字节不变；fixed_expectation、shared concurrency helper、offline_gates、coverage及run/window聚合亦未变。C# RequireRaw逐字节不变，NativeFresh及后续尾部不变。原fixed6、fixed7时间族、shared原值分支没有因generic修复被放宽。

## 两端独立的窄分支

C# `AdvisorySourceBranch`和Python `advisory_source_branch`各自限定固定TLS/six/advisory7/async两个ID、Scale7及普通值+1tick；DTO仅原local ticks/offset330。原SQL字面量、length19及hash保留；真实参数仍由EF mapping创建，route仍明确controlled ADO，不称为SaveChanges或temporal成功写。

共同新增的唯一generic tuple是available=true、ParameterDescribe、CType2/precision32767/scale0/flag2、mask0/derived0、Unknown；另要求本次Prepare response opcode0。每个raw数字有严格整数检查，完整对象匹配不允许额外字段、bool/float替换或任意CType2宽度/scale/flag。只有该tuple得到`exact_generic_ParameterDescribe_TLS_explicit7_refusal`标签；否则仍走原strict raw检查及DT26/9或DTO27/11、scale7/flag1 fixed路径，未知值失败，无宽松fallback或全局allow_flag2。

C#核实际CommandText/参数数、单attempt+failed completion、same attempt/SQL/EF ID=null、failed source副本、真实command/connection reference token及完整operation identity。reference token算法与未改CommandAudit的RuntimeHelpers.GetHashCode一致。Python通过原source()独立核Prepare捕获、最近成功response、四项identity、完整ordinal/public关系及request/public相等，再核本lane完整副本。

两端限定单参数ordinal0/public0/inout0及实际public`v`/server`V`name hash；本轮实读v1失败这两个hash分别为`4c94485e…`/`de5a6f78…`，与实现重算相符。真实request/raw/value字段数8/9/3也与新增C#严格shape吻合。DateTime2/provider DateTime或DateTimeOffset/provider DateTimeOffset、Precision0非显式、Scale7显式、Input、非NULL精确值/Kind/offset及实际W route全部要求；不能以raw类型推导请求角色。

原fixed分支在相同来源门下仍只允许既定extended temporal tuple，沒有扩大到legacy或generic“成功”。generic descriptor的scale0/precision32767继续是原始描述，不能形成ColumnScale、EncodedScale/WireScale或TLS七位开放事实。

## 拒绝、发布及完成边界

两端继续要求精确NotSupported链；C#将真实异常与failed command链联结，Python将step/item/expected_refusal/failed command四者联结。failed completion没有binding字段、current_binding_observed=false/current_binding=null；Python也拒绝attempt伪造binding。原全局Execute0证明不改。

新增控制前缀检查要求positive control属于同一固定TLS/six最后窗的先前case，attempt及capture sequence早于advisory；真实source、completed command、published binding、实际发送及operation identity保持关联。Python额外逐条核本advisory发送identity；没有借前窗或未来控制。新label由validator从raw独立导出，不信producer label。

`fresh_after_TLS_refusal`实际新连接查询、TEST/profile健康门完整保留；C#执行旧health路径，Python完整case必须有该completed step和有效health。保存的旧v1报告仍是status=failed，仅有open与failed advisory两个步骤，没有health/expected_refusal/source_branch完成证据；它不会因新增raw分类被追认为通过。旧runtime SHA本轮仍为`fa0a032fe9bd6d7a9502c17f09064efc6702c660e2bbc02dce1cf4be08f9f45d`。

DTO旧run从未执行。源码允许其在新run实际取得同精确generic tuple时检查同一负例合同，或沿原fixed分支；不同实际tuple依然pending。synthetic DTO只验证检查逻辑，不能复制DT实库收据作为DTO验收。

## 合同覆盖与后续门

AST共28个test方法（原26+2）。完整synthetic正例四个：DT/DTO × generic/fixed；每个直接走validate_case，generic还验证全局raw_tuple仍拒绝。新增负例静态计数134：每kind12个raw变异+49个通用变异+1个DT Kind/DTO offset+5个错误入口，合计2×67。原rejects先调用完整正例再变异，避免以无关缺字段制造假拒绝。

覆盖raw各字段/缺失/数字表示、branch标签、SQL/hash/length、参数增减/ordinal/public/name/inout、NULL/值/类型/facet/direction/W route、source/attempt/operation/latest response/opcode、failed副本/异常/completion/binding、Execute1/发送identity、缺正控制/跨窗/未来控制、缺health/错TEST/profile/partial及旧partial整case。这里是源码覆盖计数，**134与四正例尚未执行**；不得报告已通过。

root可在新pack/source快照后安排Low离线合同和新C#构建。SixPrecisionCases改变必须使用新E源manifest/aggregate、source/lock/build/startup及实际DLL/MVID/deps，再执行同新host的compiled partition338/25及GeneratedCreate28独立门，形成新12-ref approval。所有25窗/338case必须新run同cohort完整通过，原900/300/180界限及清理/独立validator不改；旧20窗277例和失败窗9个completed不能拼接补齐。

旧failure/partial/清理及287对象absence证据保留，当前源码接受不是新DB批准、TLS七位能力开放或DTO live接受。D/完整C及TP08后LocalClaude门继续原依赖。报告完成后停写。
