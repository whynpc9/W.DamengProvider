# TP07 E 单对象恢复工具 v1 关键静态验收

2026-10-09。结论：**scoped_source_accepted_for_Low_build_only / recovery_execution_pending**。实际冻结代码符合[限定恢复设计](TP07-E-owned-recovery-design.md)，未发现确定阻塞。允许进入Low独立构建和制品身份门；本报告不代表已编译、已DROP或已恢复。root仍须在实际编译DLL/MVID/deps及来源链获接受后开启单次执行窗口。本轮只读源码与原收据/hash，没有运行SDK、测试、工具或DB，没有修改实现或旧证据。

## 冻结身份

目录 `.local/temporal-native/verification/TP07-E-owned-recovery-v1/` 三项live与Low `TP07-E-owned-recovery-v1-freeze/source/` 实读相同：

| 文件 | SHA256 |
| --- | --- |
| `Program.cs` | `c66bbf8cbdb73cba9176ba85d119769e3410f6aba6414d39210d718452cf7098` |
| `TP07EOwnedRecovery.csproj` | `3ca4fdc2c3d54bbdaf32ec90ee2ae6f5280483635ace08f21c3103ed7da86a97` |
| `README.md` | `51c1bf21df6593786c14961b25788532910d093863c8f51c5ff52eaa474dfe6e` |

Low source manifest实读SHA `b1f82a40b7825c9847c99e11704cf55e3ea378b2ecabeb5d4fcdf6a5db598748`。项目只有精确W `0.1.0-temporal.20261008144915` PackageReference，没有EF/common源码或W ProjectReference；命名空间修正已包含，未见前次LogLevel编译阻塞。

## 实际证据与执行前门

`Inputs` / `ApprovedInspection` 均在Open前执行。固定对象是TEST schema内 `TPL_406DE87001DF440DA7BA4669`，完整nonce `406DE87001DF440DA7BA466973AC639C`、PK `TP07PK_406DE87001DF440DA7BA4669`；没有table/schema/SQL覆盖或恢复模式开关。

原ledger `10f0c6…`、CREATE SQL `01060c…`、E main `8b142…`、E startup `92f034…`、v2部分报告 `7f76…`、candidate/capabilities/package均以完整常量SHA硬核；并核单对象Basic/scale7、absence-before、真实CREATE ACK、未完成COMMENT及原实际E二进制来源。原CREATE文本只hash读取，不重新执行。实际W package asset、loaded DLL/MVID及外部批准的本恢复工具DLL/MVID/deps也必需。

恢复输入硬绑定已经实际回读的v3完整报告 `d79b7c748bd9dd8a0c86bd46eef5d741c086b89669a370d60e6a258ab0efcfaf` 与控制summary `46d746428d837c583e64637dc2f751bc0192bcb88c974413f963829c8fc679fd`，核真实检查host `4d4abe…/a62520e5…`、complete/exit0、TEST/shared、presence1/1、rows0、原artifact链及完整目录。不是占位结果或仅接受布尔。

新非池化连接必须先核TEST user/current schema和shared8.1.5.60/message21/utf-8/override-empty。表存在时，`FreshStructure` 重新读取七组目录：基础列、CHAR语义、constraints、constraint_columns、comment、indexes、index_columns；本恢复门中CHAR查询也必须成功，不使用optional异常豁免。每组当前完整typed rows与固定实际v3 rows序列化全文相等，包含ordinal/name/CLR type/null/value，因而不能只用N长度或PK名称代替完整结构核对。

随后执行固定表COUNT(*)，必须0，并再次确认表count1、精确PK count1。任何目录失败、结构/类型/顺序变化或非零数据行都在DROP前停止。NULLcomment、N128bytes/32CHAR/C，以及实际两index/唯一PKindex列按原真实证据匹配；不制造COMMENT或修改旧ledger以绕过原ownership保护。

trigger仍明确unknown_from_prior_report、absence_proven=false/query_executed=false；没有trigger查询、替代目录探索、unsupported/reusable猜测或驱动恢复改动。该未知不影响已限定对象的原创建与当前结构证据，不据此授予额外删除范围。

## 唯一写动作与结束状态

源代码只有一个DROP执行调用点，文本固定 `DROP TABLE "TPL_406DE87001DF440DA7BA4669"`。无CASCADE/PURGE、其他DDL/DML、SQL输入执行、循环删除或自动重试。调用前先将attempt写到新输出目录的独立`recovery.json`；写收据失败则不会继续该API。记录实际ExecuteNonQuery返回值，包括−1，不把返回值等同最终absence。

- **正常DROP返回：** command/主连接完成dispose后，另开新TEST连接复核身份并读表count0、精确PK count0，才标recovery_complete/exit0。
- **已不存在：** 首条新连接确认表0及PK0，不执行DROP；第二条新TEST连接再次表0/PK0，才标already_absent_verified，drop_attempted=false、recovery_performed=false。
- **调用或dispose异常：** 保留原安全error和exit1，不重试。若另一新TEST连接读到absence，仅单独标attempt_failed_but_fresh_absence_observed，不吞掉原失败；如果最终验证或最终连接dispose也失败，明确pending/exit1。不存在没有独立absence就通过的路径。

输入/输出均不写旧ledger、历史report或COMMON helper。新输出拒绝既存目录，错误只记type/code/number；未见连接串、口令或原始认证输出。代码结构没有调用共享 `OwnedObjects.Cleanup`，所以不会放宽其所有对象的结构规则。

## 后续边界

Low沿原SDK10.0.203、task CLI_HOME/禁遥测、单节点/禁build server与精确同W离线feed完成新构建，再固定source/lock/assets/DLL/MVID/deps和实际退出收据。root核这些身份后才开放本精确对象单次恢复窗口；用户既有清理自建对象授权覆盖该限定动作，无需另向用户重复请求。

本报告没有宣称残留已消失，当前最后实际目录事实仍是该表存在且0行。未来恢复成功只关闭这个残留；E fixture修复、新E冻结/离线GeneratedCreate、后续E/D实库和完整EF-C仍分别验收。原E失败和v2部分检查失败不得回写为成功。
