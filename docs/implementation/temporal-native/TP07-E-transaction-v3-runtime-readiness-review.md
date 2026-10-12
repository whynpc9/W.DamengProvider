# TP07-E transaction v3 runtime readiness review

结论：`OFFLINE_READINESS_ACCEPTED_IN_SCOPE`，历史TLS mutation12语义回归限定接受；当前冻结阶段已具备供root另行授权全新338例/25窗整轮的前置条件，无本次审阅阻塞。此结论不签发runtime命令，不接受旧失败窗口或当前E实库，不关闭D、完整EF-C、TP07/TP08或发布。

本次仅读取冻结文件、JSON、源码与哈希，唯一写入为本报告；未导入或执行项目模块、测试、SDK、host、DB、wrapper或Git。以下实际执行结论来自Low冻结源码、自然退出收据、输出及独立回读的联合证据，不把报告布尔值单独当作执行证明。

## 已接受离线链与当前身份

已完整读取 [v3限定离线接受](TP07-E-transaction-v3-offline-review.md)，实际SHA `0cfd84eda8ccca7ff04fde8976091f8985548e724c5a2ee172e8afb2900e3a78`。其范围包括39/39实际合同、新partition338/25/9项参数拒绝，以及A一次完整GeneratedCreate28 stored消费；不改写其历史上尚待readiness/B执行的状态。

本次再次逐项核对当前E8及source24当前文件/独立快照，全部SHA匹配：pack `b48626947bb838f053755acd2a82f0262c8e903d65a50291cbec48751e0f41e5`，source24 `7102998f4fcb860b1e5c40c027ca8c95801238402e29b4b315065b678fa4a75e`。validator仍 `5b534116340abdaa48bfd0555b5a177ee8462e4ff51dbb4846cdd28ec76758ec`，test仍 `41ca79e7d5db4a3996a288617c713656d5491186d35205d5d66640d4da4498bb`。C#、policy、runner、产品178、E source194/native196、startup `1cadd834dacf071f35355188d549976f2b750af579f442f498630031310eb743`、L0v2和W身份保持。

新context approval `root-approvals/TP07-E-transaction-v3-runtime/approved-execution.json` 的SHA为 `8e1390395dc198e708034bf1c9c924138dff01047417181f65df8d7a492aab76`。其顶层schema与旧approval相同，12个artifacts只替换partition和GeneratedCreate gate，另外10项Ref及其他顶层值完全相同；12个Ref本次全部物理重哈希匹配。新gate分别为 `829e337534d038fcb1a1d9264667ab2889d0877dd90b78834459023a41f30f9e`、`fe61e26eb0f1d3e3eb5e95eafc29b246176cb81645b850865ff6cc697ec14564`。审批文件名不自动代表已授权DB；原scope中runtime授权仍为false。

## 完整原入口readiness

以下路径均相对 `.local/temporal-native/verification/`：

| 实际制品 | SHA256 |
| --- | --- |
| `TP07-E-transaction-v3-runtime-readiness/readiness.json` | `a27715f4a6643197eede9a1598c7ff5529fc8ce3543c116d58cef67855935068` |
| 对应 `-control/independent-readback.json` | `d78cc085e5bab0295f0b5012453216b2e062079d0084567733f3d7be58e3c002` |
| 对应 `-control/actual-artifact-hashes.json` | `d6c3fe6dda2ab82b6cf667a7cb50f6a7846eef4ae3258b87155c764dbd5e1492` |

实际readiness脚本SHA `eae4670f68977eb9a4dd297a07cba3d0bc4ebf6f50ff7698fa4767efda17efad` 使用真实ROOT下原runner与validator，分别调用直接 `inputs(...)` 和 `R.open_context(...)`。两处均不传关闭L0检查参数；原 `inputs(check_l0=True)` 实际调用L0 `validate_artifact`，并要求等于已存 `266a3054f496cb23d6f1d738e26d90e6b2f6e83fdde3c60328568328d63dfa1b`。脚本随后比较两个context的pins/documents/approval并重哈希全部pins，没有mock或替换oracle。

Low以实际Python `-B` 一次运行，自然exit0，2.107130291秒，180秒/reap2，无timeout或termination，收据记录外部PGID已不存在。本次核对control receipt与independent-readback中的receipt完全相同、日志SHA实际匹配；14项artifact map全部实际重哈希通过。readiness的732项输入亦逐项实际SHA匹配，两个immutable-output副本逐字相同。记录的107164项外层保护无漂移；本审阅不冒称重复遍历该全部外层清单。

## 冻结历史12例语义消费

root随后提供的真实三字段 `historical-semantic-inputs.json` 为 `283b136f92597f04e164c4de606f1dd265da1558e9aa48a1666ae25cddccbd21`，引用上述新approval。B v2仍为已审 `102cda9bbf0f6c9be7476c15e85f8bbbf716efabf3aeb7979d1f8b24b618e7ae`，源码审阅见 [B v2 source review](TP07-E-transaction-v3-historical-consumer-v2-source-review.md)，SHA `c44672be2eddfe40293558a5d71e2938fdf7f84e9208d6d51da4411bf6913555`。

实际Low命令仅运行该冻结B脚本和有限inputs，自然exit0，1.789483916秒，180秒/reap2，无timeout/termination，收据记录外部PGID已不存在。根据完整冻结调用链，其先执行真实 `open_context` 默认L0检查，再核旧来源/命令/身份，随后完整 `validate_report` 返回12并执行 `generated_sql_contract`。因此本次接受覆盖TLS DT/DTO×update/concurrency/rollback×sync/async全部12例的历史报告语义、SQL及清理，不是仅挑四个并发case或仅运行新helper。

| 实际制品 | SHA256 |
| --- | --- |
| `TP07-E-transaction-v3-historical-semantic-revalidation/semantic-revalidation.json` | `e177e58a8c3fb6ee1f9b097e0234a29c522a8220391b52a974bc9f8e75533b4b` |
| 同目录before/after-input-hashes（相同） | `0b7c7a397f44635891ca9b47049613a906119c422348a7e554bdce297f4c5851` |
| 对应 `-control/independent-readback.json` | `438858775c11527ca0111085189f2d29bb76cbb49f775bd706ebcf1d62d0e4e0` |
| 对应 `-control/actual-artifact-hashes.json` | `80b345774605d2b83865b898f51aa2d11ebd95458d15e33527e7439794d25ee1` |

本次独立重哈希该15项artifact map和1968项语义输入全部匹配，before=after、三个immutable-output副本逐字相同，control与readback收据相同且日志SHA匹配。外层107180项保护记录无漂移；本审阅未重复全量外层遍历。

旧 `validation.json` 仍为 `362333ed3cc4c39f44c8bc06c9bc2d7a177483166af044c74e5bc138c69dfab8`，旧run仍 `window_failed_stop`。新历史报告正确保留旧run/cohort及 `runtime_accepted=false/current_cohort_accepted=false`；本次语义接受不重标旧窗口，不产生当前运行成功证据。原失败、源码拒绝、consumer初始错误及精确bytecode例外/清理证据均继续保留。

## 后续限定

当前离线、完整readiness和历史语义修复验证已闭合，可提交root单独授权全新同轮338例/25窗。仍须真实执行所有窗口、主清理与独立exact-ledger清理、最终完整独立验证；不能拼接历史窗口。保留原main900秒、cleanup300秒、validator180秒及原runner其他预算/TEST身份和TLS合同，不增加阈值或测试矩阵，不改W包。本报告不执行或授权任何runtime命令；冻结后停写。
