# TP07 E 单对象残留恢复关闭

2026-10-09。结论：**exact_owned_residual_closed / independently_absent / E_runtime_pending**。Astra只读回读实际恢复、独立检查、命令收据及当前文件hash后，接受本次唯一残留 `WDM_PROVIDER_TEST.TPL_406DE87001DF440DA7BA4669` 的清理闭环。本轮未重跑SDK、测试、工具或DB，未改源码、旧ledger或失败证据，仅新增本文。

恢复前新TEST/shared连接的七组完整typed目录rows逐项等于已批准v3报告 `d79b7c74…`；实际表presence1、row count0，固定PK、N128bytes/32CHAR/C及NULLcomment等结构全部匹配。原absence-before/CREATE ACK/nonce/DDL来源链保持原样。

实际只执行一次固定 `DROP TABLE "TPL_406DE87001DF440DA7BA4669"`，SQL SHA `4577980c46517a8549de46586d494b4c381757ba95f0e731face010afc4b114a`；ExecuteNonQuery自然返回−1，无重试、CASCADE或其他DDL/DML。随后另一条已核TEST连接实读**表count0、精确PK count0**，恢复进程自然exit0、无timeout/termination。这里以独立absence证明完成，不以−1返回值代替状态核验。

另一个独立进程使用已接受v3只读检查DLL，再次核TEST user/schema和shared8.1.5.60/message21/utf-8/override-empty，实读presence_before=presence_after=0，自然exit0；它没有执行恢复。两个进程的实际DLL/deps、命令、日志hash均匹配收据：恢复DLL `7f062ada…` / MVID `2c42c8c3-84e9-49e7-a9f0-35869a301e11`，独立检查DLL `4d4abe72…` / MVID `a62520e5-5d62-41f0-9555-a1a25f720f6b`，W仍为同包 `0.1.0-temporal.20261008144915` / DLL `843710be…`。

以下原始证据均位于 `.local/temporal-native/verification/`，本轮实读SHA匹配：

| 文件 | SHA256 |
| --- | --- |
| `TP07-E-owned-recovery-v1-runtime/recovery.json` | `16eec8596581cda397f9828de1c4de86b00f7f2e8404397f532904443d6af02a` |
| `TP07-E-owned-recovery-v1-independent-readonly/inspection.json` | `4449bda66d65b1a882ddfca75ad1322916d16979589e430464e77d79079dd800` |
| `TP07-E-owned-recovery-v1-runtime-control/verification-summary.json` | `09abbbb5b445c88126beba8f2be9ddf8086cd8966e1815d525901a9f7e4a7838` |
| `TP07-E-owned-recovery-v1-runtime-control/commands.json` | `bf122e744290d403f3eabcbd4796cdfa945960c84bfd0113f9263f0c82c2d6f3` |

本轮将before-input清单的 **4158项**当前文件全部重新hash：missing/drift0；Low同时记录W388无漂移。原ledger `10f0c6e8…`、原E失败main `8b142476…`、原CREATE SQL与旧失败summary保持原字节，没有回写旧cleanup成功。原trigger历史仍为unknown_from_prior_report、absence_proven=false；未添加目录探索，也未将它追认为历史无trigger。

此关闭仅说明旧残留已被实际清除并独立确认。原E首case在CREATE后的结构校验即失败，尚未到SaveChanges或时间值往返；清理成功不把该case改成通过。N辅助列fixture修复、离线真实GeneratedCreate、新E冻结/测试/实库与D门仍须独立验收，完整EF-C尚未接受。
