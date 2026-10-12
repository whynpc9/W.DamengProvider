# TP07 T1 probe v3：Python首失败归因

2026-10-10。结论：**synthetic_test_wrong_module_exception_class / actual_SHA_guard_rejected_intended_drift / Python_gate_failed**。本次失败是新增负例捕获了错误模块的异常类，不是实际工作树漂移、临时root错误或validator放过SHA差异。最小修复在 `test_contracts.py` 两处异常类型引用；核心validator的ref/hash_map/SHA门不应修改。

本轮仅回读实际日志、执行收据、前后hash maps和冻结源码，未执行或导入tests、validator、项目模块、SDK、host、wrapper或DB，未读secret、未改源码/旧制品/status；仅新增本文。

## 1. 实际失败及证据身份

Low按原命令运行完整40方法的入口，带unittest `-v -f`，首失败后停止，无重试。实际日志为：

- `test_actual_frozen_current_policy_complete_positive`：ok。
- 第二方法 `test_current17_entry_drift_and_policy_drift_stop_entire_inputs_chain`，首个 `source='README.md'` 子例：ERROR。
- `Ran 2 tests in 0.049s`，`FAILED (errors=1)`，进程自然exit1。0.049秒为unittest报告时间；外层receipt elapsed_seconds为0.12178512499667704，二者不混称。
- 错误类型为 `tp07_v3_real_closed_toolpack.ContractError`，稳定安全code为 `ref_sha_mismatch`。

以下路径以 `.local/temporal-native/verification/` 为根，本轮实算：

| 证据 | SHA256 |
| --- | --- |
| TP07-T1-probe-v3-python/contracts.log | `9784916e60d6631d986554138c61736790b33af44abb060dc3e1e933ffe15ffa` |
| TP07-T1-probe-v3-python/verification-summary.json | `0e4da7886cef8e7f661c55ba011f22d956089a4309844e98aba698e15ca89a8f` |
| TP07-T1-probe-v3-python-control/execution-receipt.json | `e234d8d496066f89a43566bbc3eb6ffa1e58ea6cc437cf5ea06ef88612fcbc12` |
| 同control/before-input-hashes.json | `69fff7f0d2d1229b63fd45f0150877a3d11ad361867aef6d2109e3965e810ca8` |
| 同control/after-input-hashes.json | `69fff7f0d2d1229b63fd45f0150877a3d11ad361867aef6d2109e3965e810ca8` |

两个hash maps各5986项，JSON内容完全相等；本次另实算live完整17源aggregate仍为 `dfa05185b1e61977999a22599ecb075d2b4b46bfc9d0649c7795ce80e82750f2`，validate/test仍分别为 `25d416d2f4a646499b4956d0c35608f593bdab1957a924755c6f60cb71898e0e`、`83115f3063c65cf252e65eedaaf19e392b7d22175507215f03717c467c68a352`。这里“maps相等”是对已记录前后状态的回读，不虚称本审阅重新执行了原Low窗口。

execution receipt明确SDK_executed、DB_executed、host_executed、cancel_control_executed均false。40方法未完成，不记为40通过；第一方法成功和负例触发正确拒绝，也不等于整个Python门接受。

Low最终summary已独立回读实算，记录executed_methods=2、fully_passed_methods=1、errors=1、skipped=0、source_repairs_or_retries=false、window_released=true，与原日志/receipt相符。

## 2. 精确代码原因

冻结 `test_contracts.py` 顶层使用spec_from_file_location将原目录validate.py加载为 `tp07_t1_validator_contract`，变量名为V；该次模块执行定义一个 `V.ContractError` 类对象。

`closed_current_toolpack_fixture(root)`真实复制当前17源到临时root，不伪造HERE。函数末尾再次从复制后的真实路径用spec_from_file_location/exec_module加载validate.py，模块名为 `tp07_v3_real_closed_toolpack`，返回变量validator。第二次执行定义的是另一个 `validator.ContractError` 类对象；两者虽来自相同字节、都继承ValueError，仍不是同一个类，也不存在互相继承关系。

实际栈与冻结代码逐行对应：

1. 第二测试方法727行取得复制模块及真实inputs/toolpack；728行先调用 `validator.inputs_contract(inputs_ref)` 的闭合正例。随后进入README子例，说明该正例已返回，不能把本次failure归咎于fixture初始root/ref不匹配。
2. 730行故意将临时工具目录的README.md追加 `\nsource drift`，保留原pack的README摘要；这是设计要验证的负例，并非源工作树被修改。
3. 732行调用复制模块的inputs_contract。实际栈顺序为该模块的824行inputs_contract → 784行toolpack_contract → 695行hash_map → 151行ref → 49行require。
4. hash_map用pack中的精确相对路径构成 `Path(root)/name`，ref实算被改临时README的字节，发现与pack原digest不同，正确抛出该**复制模块**的ContractError(ref_sha_mismatch)。
5. 732行写的是 `self.assertRaises(V.ContractError)`，它只接受原模块那个类及其子类，捕获不了复制模块同名类，因此unittest把预期的正确拒绝报告为ERROR。

同样的潜在错误还在745行重新计算policy/toolpack/inputs引用的负例：被调用者仍是 `validator.inputs_contract(...)`，但期待类型仍是V.ContractError。本次fail-fast没有运行到745行，不能说第二处也已实际失败；它是同源静态确认的相同接缝错误。

因此日志中临时真实路径、`ref_sha_mismatch`与刻意变异字节彼此吻合。没有证据支持改绝对root、改SHA字段、添加宽松兼容或绕过hash_map。也不应把异常类身份问题归为Python版本、NuGet或.NET问题。

## 3. 最小High修复范围

validator High仍只拥有validate.py/test_contracts.py两文件范围，但本缺陷最小实际写入只需 **test_contracts.py**：

- 732行和745行，针对复制模块调用的两个assertRaises改为 `validator.ContractError`；原来调用V.*的合同继续捕获V.ContractError，不全局替换。
- 可在该测试里明确断言复制类与V.ContractError不是同一类，并核README变异收到稳定code `ref_sha_mismatch`，使模块边界可审；不以类名字符串相等代替异常类型。
- 不改validate.py的ContractError基类、require/ref/hash_map、policy合同、路径或SHA判断。禁止改为assertRaises(Exception/ValueError)、吞所有异常、把正确负例改为期待success、skip或缩小40方法范围。
- fixture仍从实际复制路径导入，保留actual module HERE/__file__核验，继续复算实际Ref；不共享/alias两个ContractError类、不修改sys.modules来假装同一模块、不改假ROOT。

修复后新source/toolpack与独立Low全40原入口重跑；原失败日志、receipt、v3 source snapshot及报告保留原字节。若后续暴露新错误，首失败停止并新归因，不能因为本项原因明确就预宣称40全过。原静态源码接受不抹去本轮真实执行失败。

## 4. nullable接口是独立后续修订

协调者另行告知只读元数据High已确认：OfflineContracts四个ADO属性缺AllowNull，TransactionEvidence的ScalarExecuted/Async应按实际object?签名声明，修复范围是这两个producer host文件。本triage没有加载reference程序集或编译，不把这项独立元数据结论说成实际build失败。

这两个C#文件没有参与本次Python抛错路径；本次在临时README负例的Python异常类捕获处已停止，SDK/host未执行。nullable修订须单列新producer身份、source/9host snapshot、toolpack、复核与受影响编译门，不能混作Python失败原因或直接标单位通过。

合并后续新版本时还要留意fixture的旧v2 producer15来源pin：两个host若真实改变，不能继续要求它们逐字匹配旧v2。该**后续来源重绑定**必须指向已接受的新producer15实际artifact，或对新明确两源SHA作受审有限差异闭包；其余13producer源与五helper继续固定。不能删除冻结源校验，也不要把包含test自身的新完整17-toolpack SHA硬写回同一test源码造成自引用。该问题尚不是本次失败原因；只有新producer字节被批准后才落实相应新pin。

当前结果仍为Python gate failed，compiled28/native/build/readiness/四例runtime及L0/E338/D新overlay验收均pending。主agent可依本明确原因派High定点修改，再新冻结、审阅及独立Low门；本文不授权执行或改变旧证据。
