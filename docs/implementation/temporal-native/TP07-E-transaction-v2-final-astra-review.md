# TP07-E transaction v2 首失败最终审阅

2026-10-12。结论：**NOT_ACCEPTED / FIRSTFAILURE_PRESERVED。** 本轮22个实际窗口中21个通过原合同，TLS mutation window-000被原validator拒绝。300个唯一case的main完成不代表300个已验收；后续TLS generated三个窗口/38项及全25窗口最终validator均未运行。不能接受E338、D、TP07/TP08或发布，也不能拼接旧288项。本报告仅只读核对冻结JSON、源码和物理哈希；未执行项目模块、SDK、tests、DB、诊断查询或修复。

## 冻结证据

路径均相对于 `.local/temporal-native/verification/`：

| 制品 | SHA-256 |
| --- | --- |
| `TP07-E-transaction-v2-runtime/run-receipt.json` | `f4c6e89e9aeb55839f7b3b61d79b4c85d3fdcc85e6d3786d05fd3c4b81d3848f` |
| `TP07-E-transaction-v2-runtime-control/independent-readback.json` | `72f4a35b655d9afad6b3e84acf2d57aeeb62f8a36095894cc9f7e683722f6b3f` |
| 同目录 `failure-safe-readback.json` | `4735a941011dcb3a4e7810e69fc509b7d722ec52d05f7a8e11bbe4542860de25` |
| 同目录 `actual-artifact-hashes.json` | `58225e4d87f8db89642338a420123fd2d66b450092a254a3ccced46ade9542aa` |
| `TP07-E-transaction-v2-runtime/tls/mutation/window-000/main/runtime.json` | `e01a13acfcaaa25dc2fa2d6a5a734599e077a68fe32837ff5eeff96acdeb2ea1` |
| `TP07-E-transaction-v2-runtime/tls/mutation/window-000/cleanup/report.json` | `2eea444dc9b7138f22858610cd14251da61e61658157a67fa6850a1e97425999` |
| `TP07-E-transaction-v2-runtime/tls/mutation/window-000/validation.json` | `362333ed3cc4c39f44c8bc06c9bc2d7a177483166af044c74e5bc138c69dfab8` |
| `TP07-E-transaction-v2-runtime/tls/mutation/window-000/window-receipt.json` | `67aec74b05ffb867165ef8cb31565490597779f88e4488b6b4336bda2fc13cd1` |

上述制品及清单全部1158条物理文件已独立重哈希一致；Low保存574份输出快照。原validator仍 `c2cf988e3e0020093047bc310fa3786ef7bffa9c27a837f88bcb6fe9c801d45e`，原runner未改，原policy `2181388e…`、E8 pack `c8a00c79…`、source194/native196/startup `1cadd834…`沿用已接受离线链。

## 前置链与执行边界

[离线接受报告](TP07-E-transaction-v2-offline-review.md) SHA `d1465acf038efc7db14f6017881c22b86d7a37ab64b1c279540e0fa461a35a54`保持接受，不被实库失败回写。新有限审批 `55dfb1a54fdc63e3e4e4b44fd09a84f98e5745e079d440e25beab02882adfbb2`的严格六字段/12实际Ref及dotnet host字节已核对。本轮真实readiness为 `d794089722f9b86db58c2609ef798d15b0b8f2b31456d97c5e3b5f3e077851b2`，独立读回 `526ce0a134ee51fea3084e49216d2a92d13405cc75124ae87dcde67201056baf`。其默认 `check_l0=True` 两次完整inputs/open_context实际通过，原L0结果严格等于既存 `266a3054…`；732实际input pins与12条制品清单已逐项重哈希。此处没有假ROOT、旧startup替代或跳过L0消费。

本轮唯一run_id=`cc65a80b9e204293b87ad695c231dc45`，cohort=`167591e064975bbae596e7aa9e82ce7180712d57b2e2cf1745e533760676e79f`。22份窗口receipt和300个唯一case均同此run/cohort，当前provider为 `7e971f09…`、W为 `843710be…`。shared13个窗口通过，TLS roundtrip/six八个窗口通过；TLS mutation窗口未接受。失败即停，没有补跑后缀、重试或额外查询。

原runner外层自然exit1，4478.392031708秒，无timeout/termination，PID/PGID86687已不存在。main900/cleanup300/validator180、SDK30及reap2预算未放宽。失败窗main自然0/26.050357秒、独立cleanup自然0/6.838487秒；原validation唯一issue=`approved_fixed_tuple_pending`，`e_window_contract_valid=false`、`e_value_contract_valid=false`。producer自己的 `runtime_accepted=true`只表示host内部完成，不能覆盖独立validation和run总结果的false。

## 可确定的失败位置与安全退出

TLS mutation的12项DT/DTO、sync/async update/concurrency/rollback均记录main completed。冻结原始参数证据显示，四个concurrency winner命令的current temporal为flag1，original predicate temporal为flag2，两者request/raw scale均为6；stale命令也有对应original角色。原 `validate_case` TLS分支调用 `parameter_values`（winner/stale），后者把所有SaveChanges temporal参数交给 `raw_tuple`，该函数严格要求flag1，因此实际original flag2到达 `approved_fixed_tuple_pending`。这与冻结失败码一致。shared专用 `concurrency_parameters` 的scale7分支不适用于这个TLS调用，不能把其字面7误写成当前首失败原因。

报告内typed/raw/FF9与fresh状态保留：DT/DTO更新或winner值为 `.123457000`，rollback恢复 `.123456000`；DTO保留+05:30。以上为真实观测，不绕过首拒绝替整个失败窗口补发接受。后续修复设计由独立triage负责；不能全局放宽 `raw_tuple`、改写原证据或据main成功直接接受未完成的下游判定。

实际读取全部22窗口的main finally cleanup、独立cleanup及其finally：每个记录的owned对象均 `fresh_absent=true`、`fresh_table_count=0`，身份为 `WDM_PROVIDER_TEST`/同名Schema；无跨Schema清理。22个main和cleanup均自然0，732窗口输入before/after一致。Low外层103780项保护清单记录无漂移；本审阅重哈希1158条制品并核对22份输入清单等值，没有声称重新逐个重哈希全部历史保护文件。原消费者初始schema错误亦已保留，不是数据库重试。

本轮首失败与清理证据完整，执行窗口已释放。保持E未接受；root须先审阅独立根因设计，再另行安排有限实现与新冻结验收。本报告不授权任何新执行，不关闭D或后续任务，冻结后停写。
