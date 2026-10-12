# TP07 T1 probe v8 最终关键审阅及 v9 有限修复设计

2026-10-11，Astra High。结论：**actual_first_failure_preserved；T1 runtime 未接受**。v8 源码、实际71、source/native/build、compiled28/337及完整readiness合同的既有接受证据成立；首场shared-sync在成功Begin后的辅助身份查询失败，后三场未执行。确认probe存在未显式绑定活动事务的身份查询缺陷，修复范围限定下述三文件。T1仍未完成，L0/E/D继续pending，不表示TP07、TP08或PR整体完成。

本轮只读冻结源码、完整合同和实际JSON/hash/log，未导入或执行项目模块、tests、validator、SDK、DB或额外诊断；`.local`只读。唯一新增文件为本文，不修改实现、旧报告、approval或status。运行事实来自Low冻结制品，本轮独立核对原件、引用、源码与哈希。

## 已通过的 v8 前置链

源码review SHA256 `3949c5d88f2765204924da9b05385da76a44096700ecf54ea141d44f56e52ead`；设计 `afd8cc08a49318d83bbcfbe248a00570b3e8e992f600c4b4d389ad585755d586`。当前17文件再次逐项匹配冻结map，aggregate `7847e8163c6d1dba78980438bbb4df296d6e1f648d8f4fcd173b1f7162c2cc7b`，validate `9c76b716e6b5912b1f8aeb8cf423f84d7823f3e44500adde501604c9f2ba9657`、test `f93d361b513a53e5b5468c738daae16e8bcde16b5849c8f13a6da3512905a31a` 未变。

以下均为实际原件SHA256，阶段目录前缀为 `.local/temporal-native/verification/TP07-T1-probe-v8-`；inputs位于同名root-approvals目录。

| 制品 | SHA256 |
| --- | --- |
| inputs/inputs.json | `520f89a610cddaf8aa8a98ec30c20d1a37b87fbcc80de810471cae5eacb624f3` |
| source/source.json | `43f13851fdc628cecd26883a3d8369bd64cf218520c438115a265945c2482f7b` |
| native/native.json | `19650936d9f465218ea1c794ce8de87ba37a0b85db5e9fe1e12c407de6d3e473` |
| build/build.json | `85b02efc94dc10dc73c32b4fe424904d5b3e4227ca5b3df44b1c68976358c19a` |
| build/startup.json | `b37ee6534024945495a0d4286d815620644a1e658581f171470cabdeeff9b0e5` |
| contracts/verification-summary.json | `eec1ba8f884f2f65b76a945e0388ac6aaf657099427f440aea4ad9426beb7a68` |
| contracts/contracts.log | `7e4cecd23e27754f4a4308ed23f936286cfb0a09e48b6b022169513a54fb8cbc` |
| build-control/independent-readback.json | `1cfa3224409265a12055ced9c3e7a25106a214371c44bca98f67adcc03ddc5bc` |
| offline/offline.json | `5ad3e415f20c39494ae8e485d8d04ccaf66b8f6e1f418fcc8e8eaa56629b9177` |
| offline/host-offline.json | `cbad25f4db1d04826f57d6884c4a5516dff34f1093f234a543b036ea9b656e8b` |
| readiness/readiness.json | `d500a8d11d2ef4c14d72d23a42f58d023927d3845439e820d5c9e20916f9aa1d` |
| readiness-control/independent-readback.json | `489a7bfaf3a3df81a4fdd435c57da32b398b64f9c0de59a7d3084587537bccde` |

实际71为原66加新增5的精确AST方法集合，日志逐项成功且71/0fail/0error/0skip。offline阶段又执行原Python测试进程并自然零退出，但stdout/stderr为DEVNULL，没有该阶段方法名日志；不能把早先71日志重标为这次输出。compiled host实际28例、337断言；readiness阶段以真实readiness Ref执行完整native及offline合同通过。

独立静态重算source187、binary26、native/locked八raw、四live raw和两live locks一致；两lock为严格2/1，完整JSON仅五处新增strict true，批准的完整依赖/pruning框架相等。六SDK receipt为自然零退出；各前置阶段before/after保护map相等。保留原进程证据限制：不把十字段child receipt冒称为独立child PID/PGID观测，不把run.snapshot说成覆盖live dgspec；后者由完整raw合同及Low整窗口保护覆盖。

## 冻结 runtime 首错与清理

root有限runtime approval SHA `7ac1f96de15e8d05e3d61b7ae9e1fd1468e1a0766336340d683cc64c54959316` 精确绑定新八Ref、固定四场、每场main/cleanup/validator各180秒及原仓库TEST wrappers；wrapper字节重新核对。完整run/approval/case/matrix合同仍按顺序首错停止，未授权任意SQL、任意case或继续失败后缀。

| 冻结制品 | SHA256 |
| --- | --- |
| runtime/matrix.json | `ee61bacfca4ca43c5971e3957d032bb6d6177761eb4b62ead7f1b435f5ec6330` |
| runtime/00-shared-sync/case-receipt.json | `344ded9ce17ef32e2afdf0aba0f49a4f7a1d64a72f5305820ae5a9d084a0969f` |
| runtime/00-shared-sync/validation.json | `49de0f6a591b7852f33ebe3b637da39d9ce7e44f64f4c09cd6cc4f0891cc2354` |
| runtime/00-shared-sync/main/report.json | `329d072c7b08d437214d71c4aed92b1d38a88d2bcfd9f83b1b42641bc6c453db` |
| runtime/00-shared-sync/cleanup/report.json | `4d4b9d72a65bb35233fa39de2b49b6534e0077c61e5d2f5b79d1764971da3336` |
| runtime/00-shared-sync/main/ownership.json | `315d94b6c1df3d73df630f277a927abd563cd44b24344f2ddcf9080dda16fd49` |
| runtime-control/safe-failure-summary.json | `77fd9a3b61ff1d94d15d9cc4d702aa24f286448fb92bfb4b3013de49d9a3f031` |
| runtime-control/independent-failed-readback.json | `4236958e2d7d84d70578d67c330598bed7a4257ac31f032a422ec386ed514f4b` |
| runtime-control/actual-artifact-hashes.json | `3e4a87895a5d55da5c90cc2742b1e58ab376aee10c135008236341f38f144185` |
| runtime-control/launcher-receipt.json | `0ccc0adc5d85b21e5390131275d153b67c762b3f060d65bb30f9b5d5a859cd57` |

main实际25411ms/exit1，原独立cleanup 6110ms/exit0，validator 417ms/exit1；均自然退出、无timeout/signal/reap。验证拒绝码为 `process_natural_exit`，是主过程失败的正确传播，不是又一版本合同错误。前五步runtime_identity/create/seed_SaveChanges/fresh_before/begin_transaction completed；capabilities失败，仅安全类型 `System.InvalidOperationException`、code/number均null，没有异常消息或堆栈。transactions数组为空，仅有真实TransactionStarted事件；不据此接受保存点、后续业务写入、回滚后fresh/raw/FF9或任何TLS行为。

main finally对可信ledger所列本场对象执行drop，并以新TEST连接核实count0；独立cleanup及其finally再次验证fresh_absent/count0。matrix记录cleanup_pending=false/input_drift=false；实际后三目录不存在。12文件失败前缀与immutable副本逐字一致，34制品hash map逐项重算一致；19262保护map前后相同，case自身257项snapshot前后相同。Low记录外层组消失及匹配runtime进程不存在；未声称原child逐一PID/PGID可得。无额外查询、重试或诊断。清理成功不等于T1通过。

## 确认的源码缺陷及 v9 三文件设计

`host/Program.cs:75` 在Begin成功后，已取得并验证 `activeTransaction.GetDbTransaction()` 为W事务，随后调用 `guard.VerifyOpenedConnection(txConnection!)`，再添加transactions观察和访问SupportsSavepoints。`host/ProbeIdentity.cs:101–116` 的VerifyOpenedConnection通过Scalar/Command执行两个用户/schema SELECT，创建DbCommand时没有设置Transaction。

当前W源码与TP06生产来源manifest `8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f` 中三文件SHA逐项相等：DmCommand `34b9983ff8bfc941a2401678f7ed05e845ef2997f004e1dc29aa57b74f90070b`、DmConnection `af7e4475d176a64ee2b32838e59a063bf28a0ca34602567eebb371b909e72b47`、DmTransaction `d654434f6b7811450bffd089866da7f27ed1f2cdaee6751d457bef8ecc5333bf`。DmConnection.CreateDbCommand仅传connection；同步DmCommand.ExecuteScalar在执行前进入BeginValidatedUserExecution（1921起），调用DmTransaction.ValidateCommandBinding（749起）。存在active而selected不是同一实例时，759–760行抛InvalidOperationException，固定源码文本为“Command must use the active local transaction.”。

因此这是可由固定调用路径证明的probe辅助命令绑定缺陷，和实际失败阶段/安全类型一致；上述文本来自源码，不能宣称实际捕获了异常消息或抛出行堆栈。修复前不能将原因归给尚未到达的SupportsSavepoints getter，也不得删身份检查或放宽驱动事务约束。

root后续可限定High只改：

1. **host/ProbeIdentity.cs**：VerifyOpenedConnection接受显式可选 `DbTransaction`（默认null维持无事务调用）。非null时验证W事务类型及 `ReferenceEquals(transaction.Connection,c)`，把同一对象显式传至两个身份SELECT，保证各DbCommand.Transaction在ExecuteScalar前已设置。Scalar/Command只增加传递该可选参数所需最小改动；不反射/自动推断当前事务，不新开替代连接，不吞异常或降级重试。保留两条SQL、TEST身份/schema、物理profile/编码/版本及TLS证书策略。
2. **host/Program.cs**：唯一active身份调用改为传入已验证的 `underlying`。保留v7 alias、顺序、真实观察、Begin/rollback/dispose/finally、保存点与collector语义。全host同类调用已查：runtime_identity/OpenTestConnection/Fresh/LiteralOracle/cleanup均使用无Begin的新连接；没有第二处需扩大的active身份调用。
3. **test_contracts.py**：仅新增有限V9 producer overrides（继承V7，覆盖上述两host SHA）与实际producer15 aggregate，并切换现有fixture pin/label。原71方法AST保持；不增加仅复述实现的AST测试，不改原V4–V7 pins或历史实际fixture。新17和host9 aggregate真实重算，不形成自指。

prepare/validate/run/policy/README及其余14文件不变，v8 strict provider2/host1、metadata2、八raw/四live/两lock合同继续冻结；不扩process/log/receipt、依赖、产品178、W包、证书或保存点。无产品修订依据，不重跑产品824。

root已接受上述静态根因和有限三文件设计，并另行授权High实施v9；本报告不审其浮动文件。待v9源码冻结及独立Astra/root接受后，由Low新静态pack/inputs、完整71、新source187/native/locked build、compiled28/337/offline/完整readiness依次验证，最后单独批准从ordinal0开始的全新fixed4 cohort。实际全四场将检验显式绑定修复；当前不要求额外诊断，也不在失败v8中补跑、resume或重签。全部v5/v6/v7/v8失败和已通过前置证据保持原SHA，本文完成后停写。
