# TP06 supplemental tool 静态复审 v3

2026-10-09。结论：**static_accepted_for_independent_offline_verification / R4 closed_static**。v2已关闭的R1–R3保持；当前无剩余确认的静态阻塞，可交Low执行完整离线门。此结论不是编译、测试或实库接受；此次没有运行SDK、Python测试、consumer或DB，没有修改产品/工具。

## 冻结身份

Low快照 `.local/temporal-native/verification/TP06-supplement-v3/source-manifest.json` 实读SHA256 `78d7b42607035b4811805d3e738968281b2e49ccf1f69cf43dd00d6d853fbab3`；root/Low提供的17文件aggregate为 `ec92851169b3825487960e3ace85cabdcfc4d4355fb7631315a403fe9d47c33e`。17份工具文件SHA均已实读，四份变化与最终交接匹配，13份producer/C#/runner输入保持v2身份。

| 变化文件 | v3 SHA256 |
| --- | --- |
| contracts.py | 85e3bb327540da9e9e273518f6dedd14ed4c76387f524621ad62710fd8cf856b |
| validate.py | b1b895d24eceeb735aa7da79075aa48b86432fdd4388ed57889a3263b3674076 |
| test_validator_contracts.py | b71b96c73155d98a9702972884433cedff5afb7d89aae472083bda729ce7f4f3 |
| case-manifest.json | 2235fe2e22915267e41549207a35f0ad9b89e8b0d07cf4a74ca14bd90d24d5b3 |

[结果描述附录](TP06-supplement-result-descriptor-addendum.md) 实读SHA256 `a926f5632dbc6f6b7902b42857be5c9452676d0e5c024999d62909d99e720d5e`。文件保留原建议状态；本轮实施授权来自root随后明确批准的decision `root-20261009-supplement-g2-read-contract-v1`，不回写已冻结设计。manifest只新增此设计SHA、decision ID、Cases.cs SQL来源SHA三个pin，原vectors、42 shared/36 TLS case集合及stage预期未变。

## R4 闭环

`contracts.py:729–789`仍先执行原parameter、parameter_actions、API/Prepare次数、异常、parameter metadata及EncodedScale断言；未通过这些写入/生命周期门不能进入新read selector。clone/reset/reopen、同command/parameter/连接及后续汇总检查不变。

`select_binding_read_contract`只用于G2成功stage，有限集合由冻结write tuple和已批准profile规则先生成，然后以该reader实际ResultColumn选择。equal合同保留；额外分支仅为exact shared physical下16/3或6→26/7/width9、23/3或6→27/7/width11。shared已为26/7、27/7及TLS均不新增其它读备选；没有加入scale0、8/9、跨族、BDTA、TLTZ或任意合法scale的宽免。固定DML、G1/G3/reader与BIND拒绝恢复继续走原独立合同。

`validate.py:99–115,176–203`先核对完整固定manifest、实际文件hash/包/工具/physical及cleanup，再从实际report的W_profile、实际冻结`build/Cases.cs` SHA、确切设计pin和decision构造read_context。Cases.cs仍为 `ce37f5f6419a5b9b42e87d021279a3ace991624d50701f3a40121c6333d1f60f`。单个root_approved bool、producer自报expected或旧ParameterQuery的实测结果不能打开新规则。

选择的read tuple只作为原严格observation校验的输入；ResultColumn raw角色/TypeFlag0、raw/effective CType/scale/width、schema、ordinary cell实际长度/hash、独立整数packet decode、固定输入fields/ticks/offset、server FF9与GetString完整位数继续全检。W及server投影进一步绑定本stage实际connection ID，不能拼另一查询的文本或metadata。结果集合外产生明确 `unverified_result_descriptor`，candidate/full均false，保留write/read tuple供root裁决；集合内证据错误仍失败。

逐stage结果先存局部列表；只有完整report所有case、clone/reopen/事务关联检查通过才发布 `binding_result_observations`。实际运行标observed，synthetic标expected_only/observed=false/runtime_pending；未走到的允许备选保留design_allowed/runtime_pending，不因equal通过而宣称retained7已观察。实际G2 SQL尚须自己的同候选实测，主工具观察不被移植。

## 反例与接线复核

新合同控制覆盖全部equal tuple及shared TS/TZ scale3/6→extended7；后者保留原写Scale/EncodedScale。反例分别变更参数、metadata、root/context/SQL身份、raw角色/单位/hash/长度/字段/offset、schema、typed ticks、server文本和GetString尾零，另有TLS mismatch、8/9、跨族、BDTA/TLTZ、另一connection、fixed DML和拒绝恢复误用备选，以及最终生命周期失败不得发布read结果。成功synthetic控制不计入服务器观察。报告的112个validator测试方法尚未执行。

v3没有改变C#或runner，R1的opcode7、R2实际server-null getter、R3准确3/5/6/13分类及发送/响应分离继续保持；24个实际C#离线check ID与runner/validator完全匹配。既有source/archive/nupkg/DLL/MVID/runtime deps、TEST-only环境、精准ownership/marker、独立cleanup及安全输出门无退化。新增Python接口与调用参数匹配，case-manifest新增字段不改变producer读取的profiles/vectors；没有发现新的静态API/schema阻塞，但实际编译和运行仍待Low。

## 后续门

Low可在此新冻结上运行完整Python合同、实际consumer构建、24项C#离线合同及相应shape/身份核验，保留自然退出码与新制品。任何失败交回归属High修复、新冻结，不能由reviewer自修或重写旧证据。补充DB窗口仍由root明确安排；shared/TLS主矩阵接受不替代Supplement的实际stage、leading错误精确审批、fresh状态和cleanup门。只有补充门也通过后才能由root关闭TP06；EF与发布门继续独立。
