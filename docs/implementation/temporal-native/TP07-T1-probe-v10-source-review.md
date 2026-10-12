# TP07 T1 probe v10 源码审阅

2026-10-11，Astra High。结论：**source_rejected**。确认一个新增测试断言错误：完整正例应观察到catalog_contract三次调用，却要求一次。除此之外，完整新增五方法静态核对未发现第二处必然断言/错误码不匹配；生产修订符合有限CREATE设计，无需扩大范围。

本轮只读冻结两文件及交接、历史制品，用标准库AST/hash/JSON对照；未导入项目模块、执行validator/tests/SDK/DB。本文是源码拒绝，**不是实际测试失败记录**；planned76尚未运行。唯一新增文件为本文，未修改实现、旧证据、approval或status。

## 被审冻结身份

设计为 `TP07-T1-probe-v9-final-astra-review.md`，SHA256 `1ef25ec8e85e0571e2d76d7df2f285fe766cf6c6e2b651827f0db2fb66a17752`。工具目录为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe`；交接前缀为 `.local/temporal-native/verification/TP07-T1-probe-v10-implementation-handoff`。

| 对象 | SHA256 |
| --- | --- |
| validate.py | `18a2d0479d61c83a17a36358ae598c19df2bcf1f46b0fc57728801c94ab2e189` |
| test_contracts.py | `f140b68576038bd17ebbb4ba585011e1905e032122194e7cd73d2a5c510c27ef` |
| actual17 aggregate | `9e079b283dec1ab069cfe54f52981e80fefbcc8f55dbd0c7788a846ab8866edb` |
| producer15 aggregate | `ef2940d232e4f7b6eabd8aac6497f68bdf09ef2adbb956f1bb7264752d081c26` |
| host9 aggregate | `02e9c4aa31f522c7000750854f6404d10300a8cc55f52c8d63bce4e60e8607df` |
| freeze-report.json | `f9260b733e7f60bf5da1c0bfabf13d05920f0f0a4a7b88b48b4e037f2aa91469` |
| two-file.patch | `96e004f6bb0ba1688409c5a736082fb9bb0e682d8ac3ce3e9f0435bf1f118994` |

独立重算实际17、15、9及交接Refs一致。prechange17逐项等于v9冻结map，frozen-source17匹配v10声明。恰validate/test两文件变化，其余15字节相同；完整旧test顶层AST序列及原71方法AST保持，新增一个Ref常量、一个helper及含五方法的类，共planned76。

## B01：完整正例catalog调用次数错误

冻结 `test_contracts.py:1416` 中 `self.assertEqual(catalog.call_count,1)` 不符合实际调用图。该测试用 `mock.patch.object(..., wraps=V.catalog_contract)` 包裹原函数，没有替换成假返回；原 `case_contract:654` 对CREATE目录调用一次，随后原 `values_contract` 在before/after两轮中各于586行调用一次。所有wrapper有效期间原函数执行，因此计数必为 **3**。若完整consumer成功返回，现断言仍必失败。

最小修复仅把该断言1改为3。保留wraps、完整case调用、其它计数和所有语义负例；不跳过values、目录或cleanup来迎合计数。原v10 handoff/report/frozen-source不覆盖；待窗口释放后由High按root指定新v10r2目录冻结修订，另案源码验收。本轮不自行修复，也不要求额外DB诊断。

## 其余源码与五方法核对

validator全文恰一行变化：保留 `integer(row['result'])` 的严格类型门，再允许>=0或仅create阶段恰−1。bool/float等在左侧integer调用即拒绝，不能经or分支逃逸。原API/is_async、source/shape、completed/error检查及后续single CREATE、正式context、SQL SHA/ledger ACK绑定全部原字节保持；未广泛接受负数。

actual_v9_complete_case_fixture读取四个固定真实Ref（report/cleanup/ledger/startup），先由原read_ref核实实际字节，参数名精确对应case_contract；本轮四个Ref均重算匹配。无ROOT替换、fake approval/SDK或producer oracle。完整正例直接调用case_contract到底；commands/ledger/values应各一次，catalog应三次，cleanup应三次。所有wraps都执行原函数，B01是计数预期错误而非假正例。

其余四方法逐项对照原控制流：CREATE的−2/−100为command_affected_rows，bool/float/string/null为integer；每个已观测非create阶段改成合法nonquery API及−1后仍到command_affected_rows。未知stage、completed=false、非null error、错误API及reader-kind分别到原command_stage、command_success、command_no_hidden_error_type、command_API、command_result_safe_type。缺CREATE到actual_create_seed_command_create；command/ledger SQL SHA变异都先到CREATE_server_ACK_command_binding（早于report ownership比较），没有被全文件SHA捷径提前挡住。

所有负例只对真实Ref读取后的参数deepcopy作内存变异，历史文件不写。V4–V7/V9 pins、当前producer fixture、实际V7 native fixture保持。v9失败34制品逐项重算一致；原rejected validation/matrix不升格，新fixture仅为历史真制品consumer兼容性验证。

当前唯一阻塞为B01。修正并重新冻结/接受后继续已定完整Python集合及后续链，不新增无关门。产品178/W/824、host9、prepare/run/policy/README、raw/native/build/cleanup/TLS及进程预算均不变。T1仍未runtime接受，L0/E/D继续pending。本文完成后停写。
