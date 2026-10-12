# TP07 T1 probe v10r3 最终 Astra 审阅

结论：`T1_fixed4_source_and_execution_evidence_accepted`，无阻塞。当前冻结源码、Python 76、native/build、compiled offline 28 / 337 assertions、完整 readiness 消费与同一批准 cohort 的四场真实 runtime 证据闭合。本结论限定 T1；不宣布整体 TP07、TP08、PR、full EF C 或 activation 完成，L0/E/D 仍 pending。本文不回写任何原制品中的 `candidate_acceptance`、`full_EF_C_accepted` 或 `activation_accepted`。

## 冻结链

| 制品 | 实际 SHA-256 |
| --- | --- |
| v10r3 source review | `d2347af0fe8e937b8b6c693bb232463ab6aa9979102b1fd15a45a1695dc36f15` |
| source 17 aggregate | `bf1899160e9e3b733cf77db9882f62b49e4ab5d968049bcab1b76e8845926ad1` |
| `validate.py` | `18a2d0479d61c83a17a36358ae598c19df2bcf1f46b0fc57728801c94ab2e189` |
| `test_contracts.py` | `86bf229885c82312e93b740199a273c9107ae3cc75c382feeb8798c6cc8aa071` |
| Python 76 summary / log | `bf1a4ac0e5357bcc07281781f949fd242c25fa9e0c4d9e846cb591adfd2d0c2d` / `078f29f500acc3350b32d4e353a62765e33e982e00301555497aba5037c95e99` |
| source | `c5f2296eecf114b8579d1227911adbfc72c8bd2b6548347bd7c2e334cff3ddd8` |
| native | `2b9b786afb0db40b03b07f6816c74c1a80713a656c7fb01bcaa6d489f5445e65` |
| build / startup | `7ff48a9a8e461c62e3231378206a09d1b1a3bf1fd6a3c7bb68db50b5328640cd` / `8cdc72714fef8304bc4b4c4678e52d4124c37a4f8a1e44353515e7e2bd358466` |
| offline / host report | `9754721571df41c25ba598acc575fc9196439f9c636ec6a1ec01f35bb3ad00e1` / `b0cb37993a95ea4be7a684fe449ced58ae38466436e7f3fd88f014d864d3522b` |
| readiness / independent readback | `db7bc55053dd7c277b0eb25f4c8b4aea547c34603198fa729e818c70e3447923` / `e9d3b0fff9056280da19176d39a7ca2cb18bb839c7e768bc55dbac4d3d220fe5` |
| root runtime approval | `995f01e1ecab40a8acd0a0ba8eb678d2c41b6c09bafb924f66f6dc432c2dd687` |
| runtime matrix | `9835493c7e7497c705b938d52e89574159bd471d2a659e78f20963dbf66b3bd3` |
| final matrix validation | `5ca691c822de60f36dd8efc829c708bdfd89e40b7f964ae166987588b12dfd42` |
| runtime independent readback | `e9d035dcffcea182b9bb034f55ac7d3613f8e7a438a47e2a3169f674c813b5ab` |
| runtime artifact map / four-case summary | `fe831ddffc41888254f72b8c0d5eadccda0958cbb089db23ae497381192e7681` / `4c902b7afba673c70815d30fe68ace9b2978f8350467ae9ff9758f1c8a731a4f` |

阶段证据均位于 `.local/temporal-native/verification/TP07-T1-probe-v10r3-{phase}` 及对应 `-control`；最终矩阵消费证据为 `runtime-control/complete-runtime-immutable/matrix-validation.json`。有限 root 批准位于 `root-approvals/TP07-T1-probe-v10r3-runtime/runtime-approval.json`，真实 16 字段、固定四场、原 TEST wrappers、每 main/cleanup/validator 180 秒及 2 秒 reap，不含扩大授权。

## 前置与源码闭合

独立对照 actual log 的每个 `ok` 与当前 AST 声明，确认原 71 加新 5 的完整 76 方法集合实际通过。offline 阶段另有原 Python 自然 exit 0 receipt，其 stdout/stderr 为 DEVNULL，不冒充第二份具名日志。

重新哈希 source/native/build 的 57 个物理 Ref、187 个源文件及 26 个 binary；source 前后、两 live lock 保持一致，provider lock version 2 / host version 1。native 四个 immutable raw 与 build 四个 locked raw 的完整 JSON 差异只有五处 `restoreLockedMode: true` 插入，其余类型、framework/pruning/header/ref 内容保持；locked raw 与 live replay 相同，startup 引用真实 replay。原 native 2 次 restore 和 build 4 次 SDK receipt 均自然 exit 0。Low 的完整原 `native_contract` 与绑定真实 readiness 的完整 `offline_contract` 已实际通过；本审阅未重新执行它们。

已接受的显式 identity transaction 绑定、有限 CREATE nonquery integer `-1` 规则及浮点负例预期修正均保持冻结；没有删 identity、放宽 driver 校验、泛化负数或修改证书/savepoint 策略。四场均绑定同一 source/native/startup/cohort、W DLL `843710be…` / MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`、provider DLL `6d7e985c…` 和 host DLL `275fb974…`。

## 固定四场实际结果

| 固定顺序 | main / cleanup / validator 耗时 ms | case receipt SHA-256 |
| --- | --- | --- |
| shared sync | 35824 / 6065 / 449 | `5196d1cf92d69d9bab8df5538b1a57bf26e87bfb1926daf448387867f5ffe6f8` |
| shared async | 36274 / 6148 / 531 | `9ea9ac90e83a1f019f1592994244f0786eb971c654412d1ddb568ea8feb0cf69` |
| TLS sync | 4389 / 1292 / 627 | `36884ae235432cc641165397af0168524fa2fb45d95f10554596eaeba268e3ac` |
| TLS async | 2898 / 686 / 762 | `e55ef806a30ea667a7401ab82113ea55fd4f31e0acaf34917e707e74795efccb` |

12 个进程均自然 exit 0，无 timeout/termination；四份 validation 均为 passed、12 checks 全 true、issues/missing 为空。每场 main 的 12 steps 全部 completed。实际 CREATE 的完成/error/API/ledger SQL 绑定保留，合法 integer `-1` 不再被旧 false gate 拒绝。

shared 两场两次 capability 读取均 true，7 个 callback 绑定同一 underlying transaction/connection：savepoint create 的 35/36 序列先于业务 command 37/38，release 39/40，最后完整 rollback 47/48。TLS 两场两次读取均 false，实际 business command 35/36 完成，随后 full rollback 43/44，仅 begin/rolling-back/rolled-back 三个 callback，零 savepoint/commit；没有以缺少业务观测代替零保存点结论。sync/async 对应真实 API callback。

四场 inside 行均实际变化：shared 增加 1 tick、TLS 增加 10 ticks，并改变文本值；回滚后 context 7→18、connection 9→20 的 fresh 查询恢复原行。before/after 的 DateTime、DateTimeOffset、offset、FF9、raw payload bytes/hash 一致；raw 捕获保持 temporal projection 前、非 BDTA，shared scale 7 与 TLS scale 6 的实际整数粒度分别成立。

每场 main finally、独立 cleanup 及其 finally 均按本场 ledger 验证 TEST 身份与 fresh absence，最终 owned table count 为 0。case 输入前后快照保持，四场所有 runtime identity 完全相同。独立核对四场 30 个实际 Ref；最终 artifact map 的 108 项全部重新哈希，46 个 runtime 文件与 immutable snapshot 逐字一致，21037 项窗口前后保护映射相同（包含 live dgspec、raw、lock、binary、cache、source 和旧证据）。Low 记录 outer PID/PGID 60871 已不存在；原十字段 child receipt 不提供单独 PID/PGID，因此不声称逐子组外部检查。

## 最终边界

Low 实际完成原完整 `approval_contract` 与 `validate_matrix`，重新消费四场和 shared positive controls；matrix 无 first_failure、input_drift 或 cleanup_pending，ordered four completed。消费者首次把所有返回字段都要求 true 的错误已保存在 `consumer-initial-matrix-result-schema.json`（`f22722ce6b39ed607d5a844fdfa48bfae204997bf2326949e5543173ff9c3cb3`）；修正只按原有限四字段语义接受 true / false / empty list，未改 validator、runtime 或真实证据，没有追加 DB 查询、诊断或重跑。

v8/v9 runtime 首失败、v10 源码拒绝、v10r2 Python 失败及此前 build/readiness 失败继续保留，不追认其通过。产品 178 源、W 和既有 full 824 不变，本轮不重复执行产品 full。Astra 本次只读源码和冻结 JSON/hash/AST，未 import 项目、运行 SDK/测试/数据库；唯一写入为本文，报告完成后停写。
