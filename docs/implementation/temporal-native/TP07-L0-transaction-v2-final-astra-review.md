# NEW L0 transaction v2 最终限定验收

2026-10-11。结论：**接受本次已批准产品178与新工具包对应的NEW L0 gate：同一轮shared12/TLS2、完整独立validator、实际值与精度合同、14个本轮owned对象清理均通过，无阻塞。** 此接受仅限NEW L0；不接受full EF、NEW E338、D、整个TP07、TP08或release，也不授权后续执行。root仍负责汇总收口和下一依赖窗口。本文只读实际文件、JSON和原始字节并独立计算哈希/整数解码；没有执行程序模块、Git、tests、SDK、DB或额外查询，唯一写入为本文。

## 当前证据与授权绑定

沿用 [offline接受](TP07-L0-transaction-v2-offline-review.md)，SHA `4c2aa043c70edf6a8f7437c644b5a59042950c16bb3d45d1554613a8e12f032d`。本轮runtime与validator实际命令逐项等于root新授权，批准SHA为 `0c25e664f0e7e14e23ec52498751b521a1720fdf6279afad9616f492ead2f623`。

| 绑定制品 | 实际 SHA-256 |
| --- | --- |
| 新 startup | `a1cc53af8ef27db498777cda211975e941270ec903fb830d23387c780cd237a5` |
| 六工具有限pack | `d4220ee78441af4d79b5ced3d4d10fe2cd3e1b6b391efa0dd337a9cc3e092170` |
| runtime `run-receipt.json` | `2c76e364215ca32067286f8c75759279fe56905569d859a49ef013b3ae6e6da9` |
| `independent-validation.json` | `266a3054f496cb23d6f1d738e26d90e6b2f6e83fdde3c60328568328d63dfa1b` |
| Low `independent-readback.json` | `409dc20792192fd0177f5959354b4b8bde17620dfd1088bde8e8834a822ca671` |
| runtime `actual-artifact-hashes.json` | `89063f46d75fd1676509dae035cae5ed9689b3fbf3012264e24363fee4f027a4` |

runtime位于 `.local/temporal-native/verification/TP07-L0-transaction-v2-runtime`，Low控制/不可变副本位于同名 `-control`。本轮独立重新核45项制品、28415项外层before/after及当前实际文件，全匹配；runner内部591项输入before/after及当前实际文件亦全匹配。prepare/lock/build三phase refs和新startup/toolpack均真实绑定，没有拼接旧run成功记录。35项Python、185/186闭包及编译后grammar35的offline接受仍对应同一冻结来源。

四份main/cleanup报告实际加载的新host `f717b1…`、EF provider `6b255d…`和W `843710…`及MVID均与新startup相同；新EF source binding为 `4f50b618…`、lock为 `6982a1b1…`。W包仍为 `da8349a2…`，六工具aggregate仍为 `45cb21fd…`。旧175 provenance没有被替换；178/185/186来源门和十项lineage/artifacts在本次正式validator中消费。

## 同一轮实际结果

正式完整validator自然exit0，约0.204秒，`issues=[]`、`l0_value_contract_valid=true`，对应同一run receipt。runner自然exit0，约240.005秒；外层PID/PGID23407和validator26098均已消失。原内层receipt没有输出PID，保留原限制，不补造。

| profile | 原要求/实际case | main秒 | cleanup秒 | 结果 |
| --- | --- | --- | --- | --- |
| shared | 12/12 | 163.974 | 71.940 | 两进程自然0 |
| TLS | 2/2 | 2.315 | 1.140 | 两进程自然0 |

四进程均无timeout/termination，分别在原main300秒和cleanup180秒内。没有重试、预算扩大、SDK重建或额外DB查询；wrapper及physical profile仍限定TEST，shared为8.1.5.60/message21/utf-8，TLS profile为8.1.4.6/message11/gb18030。实际user/schema均为 `WDM_PROVIDER_TEST`。

实际case_id有序集合严格等于policy的shared12及TLS2。本轮另对11个成功case的literal与ADO control共22组原始payload直接以整数解码，核payload hash/长度、server-source descriptor、raw/typed/FF9及请求local ticks/offset，均一致；不以producer的decoded字段作为独立oracle。

- shared默认映射七位四case的tuple分别为DT `(26,9,7)`、DTO `(27,11,7)`，两种fraction均精确：100ns及123456700ns；`+1 tick`保持为原始100ns、typed请求ticks和FF9 `.000000100`。DTO本地时间/原offset/UTC ticks均一致。literal走批准FF7语法，ADO control仍只是控制路线，不扩大为EF SaveChanges接受。
- shared和TLS的显式六位DT/DTO分别为 `(16,8,6)`、`(23,10,6)`，123456000ns逐项精确。
- shared三个bare微秒case记录实际tuple：TIMESTAMP `(16,8,6)`、DATETIME `(16,8,6)`、DATETIME WITH TIME ZONE `(23,10,6)`。这只是本轮实际catalog/result特征，不证明bare alias支持七位。
- 三个bare tick1 case均在GenerateSqlLiteral本地以 `System.OverflowException`拒绝，generated literal为空、业务写入0、实际fresh rows0，没有伪造成功literal/ADO oracle或将丢失精度接受为通过。

独立validation中三个bare观察仍保留原 `observed_pending_root_review_never_alias_seven_support` 字符串，`bare_alias_seven_support=false`及 `full_EF_C_accepted=false`保持。本文接受有限L0合同，不回写历史观察状态，也不将这些观察推广为七位alias能力。

## 清理与最终范围

同一轮两份ledger合计14个互不重复owned表，逐项对应实际case与nonce，创建前fresh absence、确认创建和本轮来源均成立。main finally、独立cleanup及cleanup finally的对象集合/ledger SHA完全一致；每个对象均有TEST新连接身份、`fresh_absent=true`、`fresh_table_count=0`。独立cleanup看到已由finally移除的对象并再次确认零残留，未跨schema清理。

v1 Git prepare首失败及其缺失receipt原义保持；native独立消费者最初混淆ZIP SHA512/contentHash的错误与修正补充仍保留，未重跑SDK或改旧证据。旧L0历史接受也未被重签为本次证据。

**本次NEW L0 gate可以由root关闭。** 此限定接受不代替NEW E338或D的设计、来源、offline及实际DB门，不代表full EF/TP07/TP08完成，不构成发布授权。本文冻结后停写。
