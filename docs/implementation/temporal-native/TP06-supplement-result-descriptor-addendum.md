# TP06 Supplement G2 结果 descriptor 分层补充

> 阅读提示：这是时间精度专项的历史设计补充，不是当前操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。状态：**design_recommended / root_decision_pending / runtime_unverified**。本补充只读审阅 Supplement v2 的 G2 BIND 合同与 producer；不修改已冻结 [主工具triage](TP06-v2-candidate-triage.md)，不改变已经派发的主validator v12范围。本轮无源码/工具修改、build/tests/validator/DB运行。

## 1. 现有耦合与证据能力

`tools/TemporalCandidateAcceptance/contracts.py:724–726` 正确用 stage 的 `c_type/scale` 检查实际参数 metadata 和 EncodedScale；`:744` 又把同一tuple传给 `observation`。后者`:380`附近以该tuple要求ResultColumn/raw/schema/GetString全部相等。这与主工具同属“把写参数声明作为结果列声明”的层次混用。

但 Supplement `Cases.cs:76` 的 SQL 为 `SELECT :v,TO_CHAR(:v,'…') FROM DUAL`，与主工具的内层 `SELECT :v AS V` 再外层投影不同。G2还有自动reprepare scale3正例。主工具已观察的shared 6→7结果不能作为这些新SQL/scale3的实测证据。

Supplement `Observations.cs` 已采集同一真实reader的ResultColumn raw/effective metadata、schema、actual cell及原始hex/hash；`contracts.observation`对raw packet作独立整数解码，再核固定输入fields/ticks/offset和服务器TO_CHAR文本，最后核GetString完整精度。它具备在运行时独立判断结果列的证据，不需假设“结果必然等于输入精度”，也不能简单只信某个reported scale。

## 2. 推荐最小裁决：预先限定可接受读结果，运行后再确认观测

建议root批准 Supplement-v3 **独立读合同**，只用于既有G2 BIND成功stage：

| 条件 | 预先允许的read tuple |
| --- | --- |
| 现有合法write/read相等情况 | 保留完全相同的CType/scale/width合同，不强制legacy结果升级 |
| exact shared physical `(8.1.5.60,21,utf-8,override_empty=true)`，G2指定SQL、TS写16/scale3或6 | 额外允许结果26/scale7/width9 |
| 同上，TZ写23/scale3或6 | 额外允许结果27/scale7/width11 |
| shared写26/7或27/7 | 仍为原相等分支，不添加另一能力 |
| TLS或其它profile | 当前只保留相等分支；新SQL的非相等tuple待独立事实及root另行裁决 |

这张表是**design_allowed / runtime_pending**，并非登记已验证server能力。即使6→7与主观察相似，G2自己的6→7也必须运行取证；3→7从未据本补充被称作已观察。现有G2 stage没有scale0，故本次不新增0用例、不扩大case集合；未来若root加入write0，额外shared7备选也必须作为新的待验条件，不能继承3/6成功。任意0…9、cross-family、TLTZ、BDTA、未知profile提升均不在集合中。

仅收集到某个允许tuple还不构成通过：下节全部独立证据成功后，才在新运行证据中记录该stage的`observed_result_tuple`及其rule ID。未实测的允许分支仍标未观察；不能因一次equal成功而宣称retained7分支也已证。这里“retained7”仅为规则名称，不推断服务器内部为什么选择七位。

集合外tuple不能拿来替换expected并继续接受，也不应仅因与write不同就报告“W精度错误”。输出明确的 `unverified_result_descriptor` issue，candidate/full仍false、该门pending，保留实际metadata/packet/文本供root决定：是未覆盖的合法结果布局还是实际错误。即使字段数值暂时吻合，也不能靠人工宽免关闭该门。若集合内tuple的raw/provenance/数值/文本不一致，则按真实合同失败处理。

## 3. 分层接口与不可放宽的断言

不改case中的`expected.c_type/scale`、参数actions、参数声明、EncodedScale、prepare调用数、command/parameter identity、clone/reset/reopen等G2合同。producer仍按原stage执行，不能为了结果为7而把写Scale3改7，也不读取result去修正parameter expected。

建议在validator内部增加概念接口 `select_binding_read_contract(profile, expected_stage, result_metadata)`：

1. 只有 `binding=true`、成功stage、规定SQL/producer source身份匹配才调用；先完整执行原parameter/binding_metadata与EncodedScale验证。
2. 先以静态stage预期与root批准profile构造上节有限tuple集合，再从真实ResultColumn选择一个；不是从任意metadata生成允许集合。结果必须与spec的TS/TZ语义族一致。
3. 把所选**read CType/scale**传给现有`observation`，继续严格检查每个字段：raw source=`ResultColumn`、captured_server_source、TypeFlag0、raw/effective CType/scale/width一致、mask/derived mask0、schema scale/type、ordinary非BDTA、实际cell长度/hash与捕获时机。
4. 对原始packet hex按所选result layout/scale独立解码；fields必须等于固定输入的整数fields，FF9独立文本同值；typed、GetValue/provider的既有类型/ticks/offset合同及GetString声明位数全部保留。不能只校字符串数值相等而放过尾零丢失。
5. NULL只使用既有null-cell/6081/DBNull和独立server-null receipt；有descriptor仍核其有限tuple/raw/schema一致性，不要求不存在的payload。不借此次设计扩大NULL门。

固定列DML、G1事务、G3原生/interval与G4 reader继续使用独立物理列schema合同，完全不套G2表达式备选。BIND拒绝后的fresh recovery control也不能冒用被拒绝请求的tuple；当前其自身合法六位输入/TLS合同保持，未来若要扩展另列明确stage与连接来源。W与server观察仍绑定同一stage/row/connection，不能把另一个查询的描述或TO_CHAR文本拼接到本次结果。

## 4. Supplement-v3 范围与验证

与主工具统一的是“写参数严格、读列独立”的分层原则，不共享未验证SQL形状/scale例外，不修改主v12的root批准集合或lineage接口。Supplement v2已冻结，后续只在独立v3 revision中定点改 `tools/TemporalCandidateAcceptance/contracts.py`、validator合同测试及必要手写fixture/design pin；producer C#、runner和case/vector集合原则上不变。manifest如新增规则ID/设计pin须列明变更并更新全部对应冻结hash，不能回写v2证据。

合同测试至少包含：equal16/3、16/6、23/3、23/6及26/7、27/7正常control；G2自己的shared TS/TZ scale3/6→extended7正向synthetic控制；write scale3仍断言EncodedScale3与原数值。负例覆盖参数scale被改、无root批准规则、错误physical/profile/SQL来源、raw角色冒充、raw/effective/schema不一致、raw hash/长度/ticks/offset变化、GetString少/多尾零、结果8/9或跨族/BDTA/TLTZ、TLS套用shared例外。

新synthetic正控制只是证明validator规则可执行，不能计为server观察。root批准→High窄修→Astra静态复核→新source/tool manifest冻结→Low合同测试及真实Supplement各stage验证。实际报告逐stage列write tuple、result tuple、rule与observed/not_observed状态；某个未测备选不阻塞其它确实通过的stage，但绝不能拿未测分支宣称能力覆盖。新的集合外结果保持candidate pending交root，而不是改数据、删case或放宽成任意合法scale。

## 5. 只读输入身份

| 输入 | SHA256 |
| --- | --- |
| `tools/TemporalCandidateAcceptance/contracts.py` | `e2eb49db416ffef3da4306e79318ec5c423f978f0a2da1a1e4e6d15c168b43da` |
| `tools/TemporalCandidateAcceptance/Cases.cs` | `ce37f5f6419a5b9b42e87d021279a3ace991624d50701f3a40121c6333d1f60f` |
| `tools/TemporalCandidateAcceptance/Observations.cs` | `c6c7ee6a4c7db6c685b1f22ec7875f3aa6b7485a62c0522e538bbb1c3284d898` |

已与Supplement独立reviewer `tp4_review` 对齐：其同意上述窄集合、写入/生命周期不变、G2新SQL必须自身取证及集合外pending边界，无异议；其v2中R4等待root批准后的v3实现与定点复核。本文件仍是给root的范围裁决建议，不自行激活修改，禁止将本补充写成Supplement实库已通过。
