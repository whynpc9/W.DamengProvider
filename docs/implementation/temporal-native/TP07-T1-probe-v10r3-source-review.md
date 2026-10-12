# TP07 T1 probe v10r3 源码验收

结论：`source_accepted`，无阻塞。依据冻结设计 `a253646be10330a3af6edf38e1ce1c39262a7666115b06169f4ccfba3b107269`，实际修改严格限于 `test_contracts.py` 的 `(-1.0,'integer')` → `(-1.0,'non_integer_number')`，符合完整 `case_contract → document → safe_tree` 的首拒顺序。

| 实际核对项 | SHA-256 |
| --- | --- |
| v10r3 handoff `freeze-report.json` | `24caa41efb6f8e63d01587cd8ca519d8dbe9031f4aa46fea828a51f068bef350` |
| `one-line.patch` | `af0901af73c23dff0c3e3aea2b4879c6eb42e2bb911e13551be8630af163a02d` |
| 当前 `test_contracts.py` | `86bf229885c82312e93b740199a273c9107ae3cc75c382feeb8798c6cc8aa071` |
| 当前 `validate.py`（未变） | `18a2d0479d61c83a17a36358ae598c19df2bcf1f46b0fc57728801c94ab2e189` |
| 当前 source 17 aggregate | `bf1899160e9e3b733cf77db9882f62b49e4ab5d968049bcab1b76e8845926ad1` |
| producer 15 aggregate（未变） | `ef2940d232e4f7b6eabd8aac6497f68bdf09ef2adbb956f1bb7264752d081c26` |
| host 9 aggregate（未变） | `02e9c4aa31f522c7000750854f6404d10300a8cc55f52c8d63bce4e60e8607df` |

独立重新计算当前三个文件集合哈希，与 handoff 一致；逐字比较当前、frozen-source、prechange-source 和父 v10r2 哈希映射，确认只有上述一次 tuple 替换，其余 16 文件逐字未变。标准库 AST 静态比较确认仍为 76 个方法，仅目标方法改变；原 71 方法及其他 4 个新增方法 AST 全部相同。精确文本差异同时证明所有旧 pin assignments、fixture/helper 与其他源码均保留。

已重新核验 handoff 引用及历史保全表中的实际文件哈希，包括设计、父冻结、patch、AST/前后映射和四个真实 v9 fixture Ref、v10r2 失败 log/summary/readback/artifact map。旧 3 pass / 1 fail / 72 not run 仍为失败历史，未改写或升格。

本审阅仅读取文件并作标准库 AST/hash/JSON 比较，未 import 项目、运行合同测试、SDK 或数据库；唯一写入为本文。`source_accepted` 不等于实际 Python 76 或 runtime 通过。后续仍由 root 分配既有完整 76 验收及依赖链，无新增门；T1 runtime 与整体 TP07 状态未据此改变。
