# TP-01 v5 nonce fixture 定点复审

日期：2026-10-08。结论：`static_accepted_for_full_offline_rerun / runtime_pending / database_not_authorized`。本轮仅核对四项nonce fixture差异及前置断言，确认没有放宽live守卫、cleanup规则、返回码或负例。可以交Low建立新v5冻结输入，重新执行全部离线门。

未运行unittest、dotnet、contracts CLI或数据库，未修改产品、旧报告或失败证据。v4静态审阅没有逐字统计手写nonce长度，漏掉该fixture前置错误；本次用独立字符计数纠正，不将旧静态接受改写为运行通过。

## 1. v4失败保持原结论

已回读 `.local/temporal-native/verification/TP01-v4/offline-evidence/validation-summary.json`，SHA256为 `1be450e7ce4651dfa73f325282f3251b612eb8a72efd7b2458630d74a0428ba3`。Low首portable门自然退出1，52个测试中20失败、1错误、0 skip，其余31没有报告fail/error；local-required、真实source/package CLI、构建、C# offline、实际shape消费及DB均未执行，窗口已释放。

两个“正常fixture”原nonce实际34位，被生产32位校验正确拒绝；runner在ownership前置拒绝后不会进入cleanup/设置creation_state，相关失败及KeyError因此级联。不能据此把live守卫改为34位或无长度限制，也不能把31个未失败测试当成后续业务路径已验证。

## 2. 独立长度和布局核对

| 位置 | v4错误值 | v5正确值 | 独立结论 |
| --- | --- | --- | --- |
| Python runner fixture与C# offline fixture | `ABCDEF0123456789ABCDEF0123456789AB` | `ABCDEF0123456789ABCDEF0123456789` | 两个`ABCDEF0123456789`各16字符，总32；旧尾部额外AB使其34 |
| handwritten artifact JSON | `1234567890ABCDEF1234567890ABCDEF12` | `1234567890ABCDEF1234567890ABCDEF` | 两个`1234567890ABCDEF`各16字符，总32；旧尾部额外12使其34 |

新值均只含`0…9/A…F`。两次修正的前24字符保持不变，故派生table/marker名称不变；完整nonce的余下部分现为8字符，符合128-bit hex合同。C#的same-prefix-wrong-full-nonce负例仍将真实后8位替换为不同的`00000000`，没有退化为正例。

live `Guid.NewGuid().ToString("N").ToUpperInvariant()`本来就是32位，此次未动。共用ValidateOwnership/MarkerMatches/CreateWithLifecycle/CleanupWithOwnership、runner和validator的live32位guard均未动。

## 3. 四项差异核对

- `test_runner_contracts.py`：仅修正常nonce、增加re导入及独立len/uppercase-hex前置检查。所有mock process结果、cleanup失败/超时和异常路径断言保持。
- `OfflineContracts.cs`：仅修正常nonce并加入长度32/uppercase hex前置Need；没有改live/fake共用策略、19个owner分支、样本/statement/恢复/目标列检查。
- `handwritten-artifact-spec.json`：仅nonce字符串改变；源包事实、时间向量、类型/scale及所有expected保持。
- `test_validator_contracts.py`：新增独立字符串类型/长度/字符集检查，在加载及每次构造正常artifact前执行；不调用或放宽production validator来生成正确nonce。原mutant和错误拒绝断言不变。

与v4只读source快照比较，13输入中恰好这四项变化，其余九项保持。前置失败修正仅使测试有机会到达原本想测的逻辑，不证明其后路径必然成功。

| 文件 | v5 SHA256 |
| --- | --- |
| test_runner_contracts.py | `92cb535c00287f741d43b98fde8b0601ad15e47ebf8bfcc5a60288baf1df0d04` |
| OfflineContracts.cs | `541433daf30083632530d21243b65f9b3e02f9794d3868c43e60ba3020135e53` |
| handwritten-artifact-spec.json | `b3c8a68222fbd334b0750d6364ffe7e9c3837fd3f90c93ddd7c39382d442be5c` |
| test_validator_contracts.py | `1d2e4324184aae2dd96a5f462ff2f838f6019d8bf2ee6b6f6ae26d3f2aa34c1c` |

## 4. Low新v5窗口

同意在新freeze后按 [v4离线步骤](TP01-astra-rereview-v4.md) **完整重验**：全部portable→local-required→真实归档source/package CLI→`--offline-only` restore/build和真正C# selftest→Python消费该次实际C# shape。首失败保存并释放，不沿用v4的31个未失败计数，不只重跑20个失败或一个错误；后续门此前均未运行。

实际结果、退出码和首尾输入hash由Low记录；本次只关闭nonce fixture的静态缺陷。DB仍待root另批，旧v1–v4所有报告、快照和v4失败材料继续只读。本报告完成后停止写入。
