# TP-01 v10 定点静态复审

结论：**静态接受**，可交Low执行限定的受影响Python合同、source/package身份链和已有实际C# serializer shape复验。未运行测试、validator API、dotnet或DB；不代表v10 shared/TLS已通过。

## 输入与范围

对照 `.local/temporal-native/verification/TP01-v9/manifest.json` 独立计算17项当前hash，确认只有以下两文件变化，其余15项（含producer、runner、packed decoder、旧fixture和diagnostic）完全相同：

| 文件 | SHA256 |
| --- | --- |
| `eng/temporal-precision.validate.py` | `3ff4331e542308763216281b506f15b9bcd44a4a369d326b65c7206d4dedd265` |
| `tools/TemporalBoundaryProbe/test_validator_contracts.py` | `176f8d35d93c00b048f9a21bfd1bbd782946e0c18ca3b442faebe4f55119b256` |

修复只对应 `TP01-shared-v9-triage.md` 的官方泛型NULL表征问题。该报告所列231个O实际NULL观察及v9最终失败保持原样；未将旧失败改写为通过。

## 分支闭合

新增分支位于 `null_oracle` 的generic处理处。进入前已验证expected/value/server-format均NULL、同reader/ordinal0、真实cache cell；新分支额外要求cache可观察，因此不能通过隐藏cache逃过is_null及空payload检查。后续Read完成的检查仍在所有getter之后执行。

W首先进入原typed_null_receipt分支，不受新O逻辑影响。新增逻辑仅对O的generic且实际outcome=error、完整异常类型精确为System.InvalidCastException适用；generic名由既定映射限定为DateTime或DateTimeOffset。错误number必须null、verified_server_response严格false、failure_kind/phase/code/outcome全null、known_null_receipt为空；再调用原failure(required_type=InvalidCastException)，确保user_rejection与infra拒绝均保留。

O typed/GetString/GetValue、6081 receipt、全局failure/Safe、非NULL精度/类型门、source身份、raw oracle和cleanup无变化。原始InvalidCast错误不被改为6081，也没有伪造receipt。不存在用任意failed值或Transport绕过的新入口。

## 合同与验证边界

新增DT/DTO正例先确认完整基准accepted，再替换O confirmed-NULL generic为实际安全错误并要求仍accepted。11组负例涵盖错误getter/W lane、NULL/context/cache条件、infra/Unknown、number、receipt及verified server；setup及基准成功在拒绝断言之外。非NULLW精确getter继续拒绝同类错误；没有宣称O非NULL表征也必须符合W产品返回合同。已有W6081、NULL、infra及shape合同未删改。

静态未见剩余阻塞。按root限定先重跑受影响Python与source链，并用新策略检查冻结producer实际shape；producer源码未变，不必在此步骤先重复构建。随后新的shared runner仍按既定完整身份链自然进行私有build/C# selftest，再跑三lane与新validator。TLS另由root开窗。

不增加旧v9证据重判绕过机制，不替换旧manifest或frozen validator。本文完成即冻结，停止写入。
