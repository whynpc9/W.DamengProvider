# TP07 T1 probe v4：异常类与nullable定点复审

2026-10-10。结论：**scoped_source_accepted / v3_test_exception_boundary_closed / nullable_source_contract_aligned / execution_gates_pending**。实际完整17项仅三文件改变，符合授权范围；未发现本次定点修复剩余的确定源码阻塞。源码接受不替代真实40项Python、28场compiled DI/stub、native/build/readiness或四例runtime。

本轮只读源码diff、JSON、AST、文件SHA和固定reference原字节，没有导入或执行项目脚本、tests、validator、SDK、程序集、wrapper或DB，未读取secret；仅新增本报告。另一路unitprep修订不在本报告范围。

## 1. 实际完整身份

所有工具路径均相对 `tools/DownstreamAcceptance/TP07/transaction-capability-probe/`。

| 输入 | 实算SHA256 |
| --- | --- |
| test_contracts.py | `475e11f2cb4741d73c52e2d1148e1bd7d369b67a35df0843f944903b1212163d` |
| host/OfflineContracts.cs | `359cb5df58362489dba0126d6f0908d7ed1da5ccd553ecb01419d019feeb7a1e` |
| host/TransactionEvidence.cs | `becda80d3e091f18c7288e6277f328b9af49b0aa1f00341a6092fb57211cd82e` |
| 未变validate.py | `25d416d2f4a646499b4956d0c35608f593bdab1957a924755c6f60cb71898e0e` |
| 新producer15 aggregate | `b023a647a0f63533c463b79b2a52b1b04ad3bdb3391f6e0f7428a93832223b0d` |
| **完整17源aggregate** | **`da91fd4e7f8f2337ee49d28681261496388c0f7ba8730e5a4215b9b3e2bcbc25`** |

aggregate格式仍为按probe相对路径排序的 `digest + "  " + path + "\n"` UTF-8字节。与Low保存的v3完整copy逐项比较，改变恰为上述test及两个host；其余14工具文件（包括13 producer与validator）全部相同。五份helper仍与原受审来源逐字节一致。

Low新v4静态制品已形成，本审阅独立核全部17 live/snapshot和9host实际字节与各manifest一致。以下路径以 `.local/temporal-native/verification/` 为根：

| v4实际artifact | 实算SHA256 |
| --- | --- |
| TP07-T1-probe-tools-v4/source-manifest.json | `e8f404a5b3630935f7d063d47b449ec762b3d0b02e7cbfbd5b4aa62676b31bfa` |
| TP07-T1-probe-tools-v4/toolpack-manifest.json | `de95696d7eaab299cf7b4ab12819aa4b30b3e652b708cd14942b98d91982efb3` |
| TP07-T1-probe-tools-v4/host-source-manifest.json | `39a51172d5589fddce186293ec43b26b1c1cfdf1142eb3daebcf5e461a926b0a` |
| TP07-T1-probe-tools-v4-control/static-readback.json | `98251380ef19ffe7958fe61df028f57f1ae3215af3c45d6adb30b2a144d99899` |

九源manifest指向实际新 `TP07-T1-probe-tools-v4/probe-source` 根。旧v3 snapshot17逐项核仍匹配原manifest；没有改其九源manifest或runtime/测试证据。

## 2. Python失败接缝修复

[v3失败归因](TP07-T1-probe-v3-python-failure-triage.md) SHA仍为 `10cff3a7848c5733bc5f4ca60fd6ab4762aa50edcb08e0b4f7acea4161eebf8f`。v4在 `test_current17_entry_drift_and_policy_drift_stop_entire_inputs_chain` 中两个复制模块调用边界，将assertRaises(V.ContractError)改为assertRaises(validator.ContractError)，分别对应临时源漂移与重新计算policy/toolpack引用后的负例。没有扩大到Exception/ValueError，原来调用V.*的合同继续捕获V.ContractError，核心validate.py整文件未动。

AST比较：40个test方法名称集合完全相同；只有上述一个方法body改变，而且文本diff恰为两处异常类型替换。其余39方法AST不变。fixture构造函数另有下面的受审来源pin调整，不把它混称为test方法减少/新增。复制模块仍从实际文件位置import，HERE/__file__自然取该位置，没有改ROOT、alias异常类、跳过SHA或把ref_sha_mismatch改为成功。

旧实际Python错误保持：contracts.log SHA `9784916e60d6631d986554138c61736790b33af44abb060dc3e1e933ffe15ffa`、verification-summary SHA `0e4da7886cef8e7f661c55ba011f22d956089a4309844e98aba698e15ca89a8f` 均本轮实读未变。它仍是执行2方法、1完整通过、1 error、fail-fast退出1的失败；静态修复不回填原40通过。

## 3. producer15来源重新绑定

testfixture继续只读固定旧v2 toolpack，保留其余13个producer文件的原SHA，仅对 `host/OfflineContracts.cs` 和 `host/TransactionEvidence.cs` 使用明确新SHA overrides。独立重建expected_producer恰15项，排序aggregate必须等 `b023a647…`，随后每个实际source字节逐项与该map相等才复制。这个15项map排除validate.py/test_contracts.py，所以不把包含test自身的17项toolpack SHA硬写回test造成自引用。

本轮实际重算该15项闭包与常量完全相同；不是删除旧来源检查或允许任意新producer。完整临时fixture的当前17项toolpack仍由复制后的实际字节生成，9host与inputs的Ref保持闭合。policy、helper五源、两摘要域、observer/进程有限schema、四例/40方法、offline28 registry均未改。

## 4. nullable修正与固定reference的对应

已读取 [固定接口元数据报告](TP07-T1-probe-nullability-interface-readback.md)，实算SHA `0d05b5238394be41632be5b97fa244885be2d051cfe8bfd81cec8f5ccb4433ed`。本轮另直接读取两个实际DLL：.NET10 reference System.Data.Common SHA `e563226dce5e8d2f1ecf24b13a37d8948e42208d7b93bc9812475074382f2303`；EF10.0.12 Relational SHA `a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d`，均与报告一致。

本次独立核报告所列原字节offset/payload：ref AllowNull 124406=`01000000`、NullableContext 132917=`0100010000`；EF Nullable 2064501=`0100020000`、NullableContext 2064507=`0100010000`、async返回2004848=`01000200000000020000`均匹配。没有执行程序集，也不把这几处payload回读说成重新解析全部token/attribute关联；声明token映射依据该固定元数据报告。

- OfflineContracts只新增using System.Diagnostics.CodeAnalysis，给ConnectionString、CommandText、ParameterName、SourceColumn四个原string自动属性加AllowNull；保留getter非空string、默认值和stub行为。去掉这一using和四个标注后，文件逐字恢复v3原 `f8fa83dee618038bf72eaf10ad634d06985302efdbbec056e7dc91f7ab2bc9bf`。
- TransactionEvidence只把ScalarExecuted返回/result改为object?；ScalarExecutedAsync返回ValueTask<object?>、result为object?并显式ValueTask.FromResult<object?>(result)。End记录及原样返回逻辑不变。逆变换这几处后，文件逐字恢复v3原 `745e1ff14de7bb372695084a56c168749eb06cb41370b34e82e5972350be6792`。

这两处修改与报告确认的base nullable合同一致。没有关Nullable、TreatWarningsAsErrors或添加NoWarn，没有改保存点/事务/SQL/模型/回调分支、参数/值域、source数量、W包或EF产品178。它关闭的是题列六处源码接口差异；仍不等于实际编译PASS或整个host再无任何编译问题。

## 5. 接受范围与下一门

v4源码可交root安排下一次Low独立门。优先用新完整17/toolpack/9host身份运行原完整40方法入口；不从旧失败处resume、不选择剩余38、不修改旧summary。随后按授权完成产品正式unit/native、probe实际编译/28场DI与stub、source187/locked188、DLL/MVID/deps、readiness及四例runtime。

本次Python捕获修复与nullable修复分别有独立原因、diff与身份记录；二者共同进入新v4来源不等于把C#问题当作旧Python错误原因。40Python、28compiled、native/build/runtime均未在本轮执行或接受。L0/E338/D新overlay重新绑定、完整C及发布仍pending；任何后续代码修订需新freeze及受影响复核。
