# TP07 T1 probe v10r3 有限设计审阅

结论：`design_accepted_for_one_test_expectation_fix`。当前 v10r2 Python 76 验收失败；唯一已确认缺陷是新增负例对浮点值的预期错误码错误，validator 无须修改。本次仅静态审阅源码与冻结证据，未 import 项目、运行测试、SDK 或数据库。

## 冻结事实

| 输入 | 实际 SHA-256 |
| --- | --- |
| 当前 `test_contracts.py` | `8c4aa296ecce42bcbe63c015beac2c6f5c6f94ffa2412ad338da15096a636961` |
| 当前 `validate.py` | `18a2d0479d61c83a17a36358ae598c19df2bcf1f46b0fc57728801c94ab2e189` |
| v10r2 `contracts.log` | `00dca8cae1c4b6561d5dcbe1b551a3c1cfb8ae982b5014204afdea488bf58fd9` |
| v10r2 `verification-summary.json` | `f12c519f47d35322c268b9e7ecfea289db4b1ba9eb52791a6dd160f8ac6c2074` |
| v10r2 control `independent-failed-readback.json` | `edb9bb4592eb6ea338b6925ead488a2a11d09f3650aa9a273fce58f9c80938aa` |

证据位于 `.local/temporal-native/verification/TP07-T1-probe-v10r2-contracts` 及对应 `-control`。实际唯一执行自然 exit 1，外层耗时 0.18844766705296934 秒；unittest 报 Ran 4 tests / 0.044s，3 方法通过、1 方法失败、72 方法未运行。第 4 方法 `test_real_v9_create_other_negatives_and_nonintegers_fail_exact_result_gates` 的 `-1.0` 子例在第 1425 行得到 `non_integer_number`，断言预期 `integer`。20254 项保护未变、PID/PGID 44372 已不存在、窗口已释放。没有 SDK/DB、重试或诊断执行。

## 完整调用顺序与有限审计

`case_contract` 先经 `document(report, 'case')` 调用递归 `safe_tree`；浮点值在这里被 `non_integer_number` 拒绝，尚未到达 `events_commands_contract` 的 `integer` 检查。上一轮静态审阅遗漏了这一顶层早拒顺序。

| 负例值 | 首个实际拒绝码 | 现有预期 |
| --- | --- | --- |
| `-1.0` | `non_integer_number` | 需修正 |
| `True`、`False`、`'-1'`、`None` | `integer` | 正确 |
| 整数 `-2`、`-100` | `command_affected_rows` | 正确 |

已核对全部新增 5 方法的完整消费顺序：真实 v9 case 正例执行全部下游合同；stage/API/kind/completion/error 负例保持相应首拒码；缺失 CREATE 到 `actual_create_seed_command_create`，命令或 ledger SQL 哈希变异先到 `CREATE_server_ACK_command_binding`；各非 CREATE stage 的 nonquery `-1` 到 `command_affected_rows`。未发现第二个必然错误预期。前三方法本次实际通过，包括完整历史 case 正例；未执行方法仅有静态结论，不宣称通过。

## 最小修订与既有验收顺序

只允许 `test_contracts.py` 中 `(-1.0,'integer')` 改为 `(-1.0,'non_integer_number')`。保留原 71 方法、其他 4 新增方法 AST、全部 76 方法名称，以及其余 16 文件、validator 的 strict integer/有限 CREATE `-1` 分支、producer 15 与 host 9。不改 `safe_tree`，不绕过完整 `case_contract`，不修改历史 fixture、审批或失败证据。

High 在新 v10r3 handoff 冻结这一单点修改，独立源码复核后由 root 分配 Low 执行既有完整 76 方法门；不能以本轮 3 个通过或局部重跑代替完整门。后续既有链仍按依赖推进，不增加诊断或额外 SDK/DB 门。原 v10/v10r2 失败与 v9 runtime 首失败保留，历史正例兼容通过不追认原 runtime 验收；T1 runtime 尚未接受，L0/E/D 及整体 TP07 状态不变。
