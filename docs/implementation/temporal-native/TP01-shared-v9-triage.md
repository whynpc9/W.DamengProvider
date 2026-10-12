# TP-01 shared v9：官方泛型 NULL 表征限定 triage

结论：**validator 合同缺口；产品、producer 和已保存数据库观察均不修改。** 只在已确认 NULL 的 O 泛型 getter 上接纳真实安全 `System.InvalidCastException` 表征；W 的6081合同和基础设施错误拒绝保持。本文静态读取/枚举JSON，未调用validator、unittest、dotnet或DB。

## 冻结事实

目录 `.local/temporal-native/verification/TP01-v9-shared-baseline`：

| 文件 | SHA256 |
| --- | --- |
| `metadata.json` | `34bf2af8f2fba17954f865a46fadd9b5203af5d35427a8f43341a3274de30372` |
| `characterization-validation.json` | `4f0538d5eabd780251083b6656ba84a6108122a5978ab5b3f2934322fa47528f` |
| `verification-summary.json` | `02c3acc78675007acfa72314144875c71019832718926f05eba01c6ab9d09039` |

三lane完成700条观察并cleanup退出0，原始348个raw decode与server匹配是该次观察事实，不等于最终gate通过。最终首错仍保留为：

```text
O:bind_inferred_sunset_p7_advisory_null:baseline_NULL:O:generic:NULL_receipt:object_required
```

独立遍历整个metadata，不仅检查首错：共有 **231个 O confirmed-NULL oracle**，initial/replacement/reprepare各77。7类declaration每类均advisory9、fixed24；所有已产生的O typed-null oracle均含null_context，没有遗漏context的样本。未执行或被参数拒绝而没有oracle的case不计作getter观察。

231个全部具有：expected_dbnull/value_is_dbnull/server_format_is_dbnull/read_completion=true；visible cell is_null=true，length/hash=null。实际getter形式统一：

| getter | 231个实际形式 |
| --- | --- |
| GetValue | `outcome=null,type=System.DBNull`，无receipt |
| GetString | `returned-null`，无receipt |
| typed | `Dm.DmException`6081，`unverified_driver_error`，有真实known-null receipt |
| generic | `System.InvalidCastException`，`user_rejection`，无receipt |

全部generic错误的number=null、verified_server_response=false、failure_kind/phase/code/outcome=null。该错误是原始实际getter结果，不应替换为Dm6081或伪造receipt。

## 根因与最小修复

`temporal-precision.validate.py:243–253` 的 official_null_value 对除GetValue外的所有error无条件调用typed_null_receipt；后者要求provider异常6081和receipt。因此O泛型把DBNull转成值类型导致的安全InvalidCast被错误拒绝。`null_oracle:263–292` 已有真实NULL上下文及cell检查，适合放置这个窄分支。

允许修改仅限validator及其独立合同测试（必要shape测试输入），不改Program/OfflineContracts、Safe/CheckInfra、产品泛型getter、cleanup或原报告。建议把新分支局限于null_oracle已完成上下文与缓存核验后的O调用点，或给official_null_value显式传入已验证上下文；不要让脱离该上下文的helper调用自动宽松。

新分支必须同时满足：

1. reader严格为O；getter严格为`GetFieldValue<DateTime>`或`GetFieldValue<DateTimeOffset>`；outcome=error。
2. expected NULL、同reader/ordinal0的IsDBNull及server格式NULL、visible cached NULL均真实成立；最后仍要求后续Read完成。优先复用已有null_oracle核验，新增O分支不得绕开或隐藏已可观察的cell。
3. error.type精确为System.InvalidCastException，failure_class精确user_rejection，number=null、verified=false、failure_kind/phase/code/outcome全null；known_null_receipt为空。
4. 仍调用原failure(required_type=InvalidCastException)；显式空structured字段检查进一步拒Unknown/伪Server等，不能只靠一个类名放行。原error完整保留。

typed/GetString的O6081分支、O已有返回null/empty/default表征和GetValue策略不因本修复变更。W始终走原typed_null_receipt；非NULLgeneric仍按正常精确/类型规则，不适用此分支。不能全局把InvalidCast、6081或Transport视为可接受。

## 必要独立合同

以现有成功artifact为基准，先断言基准accepted，再把O confirmedNULL的generic替换成上述实际安全错误，验证仍为baseline characterization、candidate_acceptance=false。另覆盖候选artifact中的O对照观察可以保留这一官方表征，同时W的要求不变；不能把O实际行为当作W产品合同。

负例至少逐项修改reader=W、getter=typed/GetString、缺/false NULL context、ordinal或reader不一致、cache非NULL/隐藏、Read未完成、错误type/number、verified server、Transport/Timeout/Unknown/outcomeUnknown、infra failure_class、伪receipt。只将目标generic错误替换为安全InvalidCast仍不能让W通过。负例setup/基准成功断言置于目标异常捕获之外，保留既有NULL与infra全部测试。

没有发现需要继续扩展O NULL返回形式的第二项问题；231个已观测getter矩阵一次覆盖上述两类异常和两类返回结果。

## 后续运行与历史保留

默认新v10完整offline→新shared→单独获批TLS即可，旧v9失败及证据原样保留。虽然固定v9 raw可在数学上供新validator重判，但现有接受链会核验冻结validator/hash；不要替换旧build policy、改旧manifest或新增宽松绕过框架来制造旧gate通过。

如root未来单独授权只读再裁决，结果必须是新制品，明确双重绑定旧v9 report/runner/tool hashes与新validator hash，并标为重新判读、非原执行通过；本次无需为节省DB重跑开发该机制。新v10规范流程更小、更清楚。

本文冻结后停止写入；修复及新运行由root分别派原High和Low。
