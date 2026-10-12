# TP07-E transaction v3 整轮最终验收

2026-10-12。结论：**E_LIMITED_VALUE_RUNTIME_ACCEPTED；本次范围内无实质性阻塞，可推进 NEW D 的独立设计与依赖流程。** 接受的是当前冻结工具、同一产品/构建、全新同轮25窗口338例的限定E temporal value/runtime合同。D仍pending，`full_EF_C_accepted=false`，不代表D、完整EF-C、TP07/TP08、发布或合并完成，也不授予后续执行权限。

本审阅仅使用源码、JSON、文件哈希及标准库整数/日期运算读取冻结证据；未导入或执行项目模块、tests、SDK、host、DB或Git。没有修改源码、审批、状态、旧证据或run布尔值。唯一写入为本文。

## 完整冻结链

已全文读取[限定离线接受](TP07-E-transaction-v3-offline-review.md)（SHA `0cfd84eda8ccca7ff04fde8976091f8985548e724c5a2ee172e8afb2900e3a78`）和[readiness接受](TP07-E-transaction-v3-runtime-readiness-review.md)（SHA `f78eba34f88529e3c5341d3180984630d9802e2c5b526f9ef4c4f7655f875532`）。前者接受39/39、新partition与完整GeneratedCreate28消费；后者接受原完整inputs/open_context默认L0检查及历史TLS12语义回归，未把历史失败窗口改为当前成功。

当前source24=`7102998f4fcb860b1e5c40c027ca8c95801238402e29b4b315065b678fa4a75e`、E8=`b48626947bb838f053755acd2a82f0262c8e903d65a50291cbec48751e0f41e5`；validator=`5b534116340abdaa48bfd0555b5a177ee8462e4ff51dbb4846cdd28ec76758ec`、test=`41ca79e7d5db4a3996a288617c713656d5491186d35205d5d66640d4da4498bb`、原runner=`57c3e6aafbdbf3bd33c68e6539227f9472e3318ad6b97d4e996ad450c8cecd86`。产品178、source194/native196、原policy、W与startup `1cadd834dacf071f35355188d549976f2b750af579f442f498630031310eb743`保持；本轮未重建或重包。

真实六字段/12 Ref context审批 `8e1390395dc198e708034bf1c9c924138dff01047417181f65df8d7a492aab76`已逐Ref物理核对。readiness实际文件为 `a27715f4a6643197eede9a1598c7ff5529fc8ce3543c116d58cef67855935068`，独立读回 `d78cc085e5bab0295f0b5012453216b2e062079d0084567733f3d7be58e3c002`，14条制品清单全部重哈希通过。其原默认L0验证与完整transaction/native/offline入口自然通过；不存在假ROOT或关闭L0检查。

后续root的独立整轮授权为 `root-approvals/TP07-E-transaction-v3-runtime/run-command.json` SHA `fada14b8a7b3512668b48c412aff182ab0a09378fc6c099889324f7f2369cf6a`。实际外层argv与它完全相同，授权一次从0执行whole338/25，不是旧context审批文件自身隐含DB授权。

下表路径相对 `.local/temporal-native/verification/`：

| 实际制品 | SHA-256 |
| --- | --- |
| `TP07-E-transaction-v3-runtime/run-receipt.json` | `5905de5a13df036b0553568711957397efa0cf13cf95f306e57b9f1402c6417c` |
| 同目录 `validation.json` | `7ddd3743f8df499c178352965552961f7c5fd5d7e3217c27cf2d23358d723d56` |
| 同目录 `final-validation-process.json` | `98cc40d54f3e1043bc1b55b127a1fdd2bf24c1ae2039e88dbe262e05615ce886` |
| `TP07-E-transaction-v3-runtime-control/receipt.json` | `9d51874dbd67d6c648f086fe928325292a1267a72f77774c3ae3d62814934d43` |
| 同目录 `independent-readback.json` | `0b4de9a86c469ac4ee7123bcc6d72a7cdefe16f813ad15c66c4dd15e200c2e31` |
| 同目录 `actual-artifact-hashes.json` | `303f27a4c74d513c8b8c942486c0726fe44d1209cd054e5390701b617119dfa1` |
| 同目录 `actual-case-inventory.json` | `88f841e14d0fa2e0361bb7ea51c0219e1675daab8e4a1b9562b4a1476a72c606` |

本审阅逐项重哈希全部1350条runtime制品，均一致；668个原输出与immutable-runtime副本逐字相同，原输出mode为0444。0444是文件权限，不是制品数量。

## 同轮覆盖与实际进程

唯一run_id=`67473b60ae41449bb3c36ab3cdf16987`，唯一cohort=`6a46104e155bf18a23f28693fd989a8e8d8f8a2e226ca4509547ddfebaae53e0`。逐个核对八组receipt、25窗口receipt/validation/main及50份E-host receipt：全部同run/cohort/current pack/approval。case ID按policy规定的profile/group/组内顺序精确覆盖338，无重复、遗漏或旧窗口拼接。

| profile | roundtrip | six | mutation | generated | 合计/窗口 |
| --- | ---: | ---: | ---: | ---: | ---: |
| shared | 60 | 54 | 12 | 54 | 180 / 13 |
| tls | 52 | 56 | 12 | 38 | 158 / 12 |

25个main、25个独立cleanup、25个窗口validator全部自然exit0，原900/300/180秒预算、无timeout/termination。最终完整validator自然0/16.909311秒，180秒预算，实际返回`e_value_contract_valid=true`、25窗口、338例、`issues=[]`。原 `validate_run` 重走所有group/window合同、完整值/SQL/清理、唯一owned覆盖及输入不变检查，没有单凭producer完成旗接受。

外层原runner自然0/4565.399195667秒，原总预算34710/reap2；收据记录PID=PGID70785且外部已不存在。必须区分实际schema：窗口main/cleanup/validator的process记录为9字段，50份E-host identity/input/argv收据各14字段，均不提供inner PID/PGID；**不声称对每个内部进程单独完成外部退出核验**。

run原始`status=E_windows_complete`、`e_runtime_accepted=false`均保留。源码第120行初始化false、第170行只更新完成状态，随后执行最终独立validator；故本报告依据实际最终验证与证据作限定接受，不修改producer字段，也不把其false解释成未执行。

## 值、真实来源与清理

所有25份main及cleanup身份绑定同一provider DLL `7e971f09d645500e342688aefb484343a40bc7d0c7fb9713f3bfa18285d069b0`、W DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`、实际E/L0 host及startup。受控EF迁移、SaveChanges、fresh AsNoTracking与独立ADO读取路径均继续由原完整validator消费，未替换为literal/rawSQL生产路径；独立ADO仅承担值/协议观察。

本审阅另对全部报告内1008个raw/typed/FF9**出现项**做独立整数重算（含步骤与独立oracle重复呈现，不称1008个唯一case）：检查原payload SHA/长度，从原始bytes解日期、秒内整数fraction、offset，核对typed ticks及DTO UTC ticks、FF9完整文本；另64个NULL出现项相符。覆盖100ns尾数、边界日期、DTO offset，未按字符串或微秒缩放替代100ns值。native DATE/TIME/INTERVAL的12个非NULLcase按3/5/24字节整数布局计算与expected ticks相同，另12个native NULLcase保持NULL；INTERVAL声明1686没有误当普通scale6。

shared7原严格合同保持；TLS6 concurrency新有限helper只接受已审DT/DTO×sync/async完整current/original角色及raw/publication/协议/fresh状态，不全局放宽fixed tuple。整轮实际已包括曾失败的TLS mutation12例，当前validator在本轮完整通过；这不重标v2旧窗口。

TLS12个default7_fixed6均实际NotSupported拒绝，fresh前后rows0/catalog相同；两advisory7异步拒绝后新连接TEST健康。异步业务Execute0绑定同窗口真实正控制；sync仍诚实记录`sync_send_unobserved`，不伪称同步发送帧已观测为零。TLS未被提升为seven能力；请求/Prepare观察不等于seven业务写入成功。

实际380份generated SQL文件逐项hash与case中的migration输出、已完成迁移步骤相连，CREATE文本存在；最终原`generated_sql_contract`还核实际EF command身份与HasData/inline/update-expression的InsertData操作。generated/default/seed边界、并发异常链、rollback/fresh恢复以及raw typed FF9均由本轮完整case合同接受；clock-default按原报告精度合同处理，没有新加或降低判据。

338个owned table、nonce、primary_key分别全局唯一。逐窗读取main cleanup、独立cleanup及其finally：记录的对象全部TEST用户/同名Schema、`fresh_absent=true`、`fresh_table_count=0`；最终finally恰338个唯一自有对象。原ledger hash绑定及限定对象清理保持，没有跨Schema操作。全部窗口和host before/after均等于同一732输入map，732项本次实际重哈希相符；另核209个run/window Ref出现项。Low外层107198项保护读回无漂移，本审阅不冒称再次遍历全部历史保护文件。

## 历史与后续边界

v2原run仍 `f4c6e89e…/window_failed_stop`，原TLS mutation validation仍 `362333ed…`，两文件本次实际重哈希不变；旧288、旧失败窗口及历史语义回归不计入当前338。两个只读进度reader错误原记录仍在：`consumer-progress-initial-schema-error.json`处理尚未产生receipt，`consumer-progress-path-error.json`处理绝对路径；仅修正reader，无producer改写、retry或额外DB。这些记录不隐去，也不误判为产品执行失败。

没有发现当前E来源链、有限合同、覆盖、值/协议、清理或实际过程真实性方面的实质缺陷。本次E限定接受可以解除其对NEW D设计/前置流程的依赖；D实际实现与验证仍按独立批准范围推进。`D_runtime_support=pending`、`full_EF_C_accepted=false`保持，原预算及全部TLS/交易/重试边界不扩大。本文冻结后停写。
