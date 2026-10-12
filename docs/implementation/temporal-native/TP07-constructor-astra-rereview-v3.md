# TP07 constructor FF7 v3 定点复核

2026-10-09。结论：**F7-V2-B01_closed / scoped_static_accepted_for_Low_offline_gates / runtime_pending**。只核对v3相对Low冻结v2的validator/test差异、真实producer收据形状及14文件身份；未运行SDK、构建、测试、validator或DB，未修改实现、旧快照和旧接受旗。

`validate.py`新增精确safe异常比较，要求operation.error、case.error、main.failure都是完整且仅含 `type=TP07.Runtime.EvidenceException / code=OracleMismatch / number=null` 的字典。缺项、额外wrapper字段、其它类型/code/number均拒绝。它与固定producer在纯DATEADD值比较失败时实际保存的SafeEvidence对象一致；保留loss与阶段标签但异常被transport/cancel/Dispose覆盖的结果不再进入52项限定分支。

限定分支还锁定 `failed_case_id=F7-A-DT-second1`、`failed_operation_id=F7-A-DT-second1:dateadd_update`，核completed_case_count为严格整数52、characterization_complete/expansion_completed=false、main status=execution_failed/exit1。正常全完成分支要求严格整数53、两个完成旗true、无主失败ID/异常字段；completed operation/case及成功cleanup不能夹带error字段。原先的前52逐项exact、同表typed对照、完整DATEADD前后raw/FF9/typed、源/期望control、92/3/12操作数、六表目录/总行数、独立cleanup和自然进程退出门全部保留。

因此合法窄结果仍为**52项完整可交root审阅，DATEADD/R04与整体53项失败**；独立validator总体exit1/characterization_complete=false，literal_support/full C/product授权均false。不接纳早期loss、partial、缺oracle或任何不符合真实OracleMismatch的异常。本文关闭的是静态缺口，不预接受尚未发生的运行结果。

合同fixture已补三层真实形状的safe错误、last IDs和完成计数；新增两项方法在保留完整loss/正确阶段的条件下逐层变异missing/null/type/code/number/wrapper，以及错误ID、bool冒充整数计数/exit、错误完成旗和隐藏成功异常。原全53正例、原合法最后DATEADD-loss正例、早期/partial/infra/cleanup负例保持。当前14个方法仅静态阅读，未声称14/14执行通过。

## 输入身份

相对Low v2 `source-pack-manifest.json` SHA256 `b96358ab9d2df6cd9b7d66c0762c7a4fa2db242f4ecaee8888905bf09e1677f6`，实读14项中只有以下两项变化，其余12项逐字节相同：

| 文件 | v3 SHA256 |
| --- | --- |
| constructor-probe/validate.py | `52303a4b53b74df34872e88efe8a21e96203b1210f41fbcc6abc8b8c7bea3d1e` |
| constructor-probe/test_contracts.py | `39304119ef10b14711806dae9f61fccf33e9b4b2b43c6c84b5d51b500528646a` |

producer三文件、53-case JSON、八个immutable helper未变；没有扩大SQL、对象、参数、恢复行为或产品范围。[v2报告](TP07-constructor-astra-review-v2.md)的其它限定结论继承。

Low须先完成新v3外部14包freeze与实际离线合同门，按root安排独立source/lock/build/startup身份后再另开DB。即使只改Python，也不得借旧pack manifest绕过v3身份链；若复用未改C#产物，必须由root明确批准并保存其编译来源与本次helper/validator闭包，不伪称重编。v2快照、SCREEN25全部旧结果和原L0失败继续保留。报告完成后停止写入。
