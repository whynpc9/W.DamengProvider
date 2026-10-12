# TP07 E 单对象只读检查工具 v3 静态复核

2026-10-09。结论：**scoped_static_accepted_for_new_build_and_separate_readonly_window / recovery_not_authorized**。v3窄改符合root批准边界，无新增确定阻塞。本轮仅静态diff与文件hash回读，没有运行SDK、测试、检查工具或DB，没有修改实现或历史证据；仅新增本文。

Low新快照 `TP07-E-owned-inspection-v3-freeze/` 的manifest实读SHA为 `40086e27d8450590229f5d01811d85db0024aa4b0f9ce9ce3bc267f7ea3d701b`，三文件live/snapshot一致：

| 文件 | SHA256 |
| --- | --- |
| `Program.cs` | `fcc66dd32daba3038c379ab77a08bd42b4b5cf32738abb9ae72fb8357e4aeef0` |
| `TP07EOwnedInspection.csproj` | `5cf9479a8265d9af823b91aa3b92237862f4926786cfad0476caf89139c5a001` |
| `README.md` | `8e11ae3ca58d64dd5e2ac155871e5786a1f244bbbff658269a99adb677d7a006` |

相对[v2静态接受](TP07-E-owned-inspection-astra-review-v2.md)，仅新增强制 `--prior-inspection` 与原部分报告SHA绑定、检查该报告的固定table/task/read_only/failed状态及唯一triggers查询的 `W.Dm.DmException`、null code、number−2111；然后删除原失败的trigger SELECT。其余项目、身份、源码/包输入、只读目录SQL与COUNT/final-presence流程不变。

原v2 report SHA `7f76c57340774a76318706bc4c60fe0e2795d7508ba509e59a6fe8d2ffde5aff` 当前未变，精确全文pin也涵盖其presence_before1。该报告确实观察到N为VARCHAR2、DATA_LENGTH128、CHAR_LENGTH32、CHAR_USED C，而共享ownership期待32字节；ID/D/Z、PK(ID)及NULLcomment有实际目录收据。它同时记录trigger查询−2111及随后外层InvalidOperationException，**未提供可证明原连接可复用的FailureInfo，未取得行数或最终presence**。原失败summary `8712ef733b3842a476f767b918c5002e754415a376c31f41c868976d8a3cdad4` 仍是部分失败，v3不把它改成完成。

新输出明确 `trigger_status=unknown_from_prior_report`，附原报告引用、原安全错误、`absence_proven=false`、`query_executed=false`，没有伪造空rows/row_count0，也不声称view unsupported、verified reusable或驱动状态恢复。它不重试该查询、不探测替代目录、不触碰驱动恢复逻辑。`inspection_complete`只代表新定义的必需只读检查完整，同时trigger未知状态仍显式保留。

新进程仍先核原ledger `10f0c6…`、CREATE SQL `01060c…`、原main `8b142…`、startup `92f034…`、同W候选及新检查DLL/MVID/deps，再在**新TEST/shared连接**上验证用户/schema/物理profile。固定目标仍为 `TPL_406DE87001DF440DA7BA4669`；原目录列/CHAR、约束及PK列、index、comment、存在时精确COUNT(*)和after-presence均实际重读，不能借用旧连接或旧数值。required查询失败仍不能得到完成结果；没有增加DDL/DML、OwnedObjects cleanup或任意table/schema入口。

trigger未知本身不否定原absence-before、CREATE ACK、精确DDL/完整nonce派生table与PK、当前完整结构所形成的所有权证据；不存在必须先扩大查询范围证明无trigger的依据。但**本报告没有接受恢复执行**：仍要等待v3实际行数和最终presence，并另审精确单对象、无CASCADE的恢复方案。N辅助列的fixture修复与历史残留恢复分开，不改旧ledger制造COMMENT或重新解释其原结构。

下一门为Low沿原SDK10.0.203/同W精确包的新build-only，冻结实际DLL/MVID/deps与构建收据；root随后可单独开启固定只读窗口。E/D新DB暂停及完整C pending保持，未授予DROP或其他恢复权限。
