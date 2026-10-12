# TP07 T1 probe v9 源码验收

2026-10-11，Astra High。结论：**source_accepted**，无阻塞项、无最小修复要求。冻结三文件符合 `TP07-T1-probe-v8-final-astra-review.md` 中已批准有限设计（SHA256 `7e45673465e0121aefb663f489bae989fa5b7b8ad387cd0d0e6c286de6237fe0`）。仅接受源码进入既定验证门，不表示新71、SDK、compiled28、readiness或runtime4已通过。

本轮只读实现、完整diff和最终冻结交接，以标准库静态AST/hash/JSON比较；未导入项目模块、执行tests/validator/SDK/DB。唯一新增文件为本文，未修改实现、旧证据、approval或status。先审已冻结tracked源码，收到完整handoff停写通知后才核其最终报告。

## 实际冻结身份

工具目录为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe`。下列SHA256独立重算匹配，aggregate保持排序相对路径的 `SHA256 + two spaces + path + newline`。

| 对象 | SHA256 |
| --- | --- |
| host/ProbeIdentity.cs | `8e653b99e827535f7b643383c646d62d19dba6fbfbb1b6151dbbd698a82691ba` |
| host/Program.cs | `45d7db114b6adc095b033df3b701881f4e2d9c94644741d4d1c670c7c759fdb8` |
| test_contracts.py | `8ffd1cf8d4f48470359042c68433cf64bdd655a0ab2a8d0b71b98a5ec789de5e` |
| actual17 aggregate | `af3160015d58c0009ecb8c35faa16221a84caf54acfa0058eb45a331856c19bf` |
| producer15 aggregate | `ef2940d232e4f7b6eabd8aac6497f68bdf09ef2adbb956f1bb7264752d081c26` |
| host9 aggregate | `02e9c4aa31f522c7000750854f6404d10300a8cc55f52c8d63bce4e60e8607df` |
| freeze-report.json | `bd1c82eb198e0256ac3456586470d7d0595c354fb531cba9ce381ca82f4c0008` |
| three-file.patch | `78de2210253b2db0f89b4cf5b5ccc09e89f7275ce2f473c2aeb8d45023be91a1` |
| after-manifest.json | `41bdce60e566972b77bd0125975d8024455bc997129691e780a421cf9c289f56` |
| original71-method-AST.json | `b33112ffa916d71691304ea9aed0cdff28e032f0317aeefc9641e56f7485e7ac` |

交接目录为 `.local/temporal-native/verification/TP07-T1-probe-v9-implementation-handoff`。prechange17逐项匹配v8冻结map；actual恰上述三文件变化，其余14字节相同。最终handoff及历史保存文档中的17个Ref均核对实际字节；当前17最终重读仍匹配。

## 显式绑定修复闭合

ProbeIdentity仅改VerifyOpenedConnection、Scalar、Command三个方法：可选 `DbTransaction? transaction=null` 保留原无事务调用；非null时先验证W事务实际类型/程序集与 `ReferenceEquals(transaction.Connection,c)`，再将同一对象传给两个原身份SELECT。Scalar传递参数，Command在ExecuteScalar前显式设置 `cmd.Transaction`；默认null时不调用setter，原CreateCommand/timeout12/SQL/返回转换保持。没有自动发现当前事务、替换连接、吞异常或重试。

Program全文独立构造预期单次替换后逐字相等：仅把capabilities内 `VerifyOpenedConnection(txConnection!)` 改为传 `underlying`。该对象已由当前activeTransaction.GetDbTransaction取得并经VerifyObject确认；调用仍位于真实Begin之后、原transactions观察与getter检查之前。v7局部alias、事件/命令collector、Begin、SaveChanges、Rollback、dispose及finally全部不变。运行时W驱动继续检查活动事务归属与状态，未放宽其规则。

两条身份SQL、TEST用户/schema、连接open检查、物理版本/协议/编码核验、TLS目标/证书策略及返回shape未删减。其他身份调用仍为原无Begin的新连接场景。修复覆盖实际v8失败的辅助命令遗漏绑定；其真实执行效果仍须新构建和fixed4确认，源码不能代替实库证据。

## pin、测试与证据边界

原71个test方法精确名称集合及AST全部相同，无新增方法或helper。原顶层assignment全部AST相同，包含V4/V5/V6/V7 pins及实际V7完整native fixture Refs；唯一变化的原Python函数是closed_current_toolpack_fixture的pin选择、文案与模块标签。新增V9 overrides继承V7，仅覆盖ProbeIdentity/Program两SHA。以AST字面值独立展开V4→V5→V6→V7→V9并对照真实v2 map，结果恰为当前producer15，aggregate一致；排除validate/test，不自指，不导入producer充当oracle。

prepare、validate、run、policy、README保持v8字节。严格provider lock2/host lock1与metadata2、八raw/四live/两lock、完整graph/pruning/contentHash/binary/MVID、CLI13、十字段receipt/180+2、snapshot覆盖边界、compiled28/fixed4均不变。产品178/W/产品824不在修改范围，无需重跑产品824。v8首错及其清理/失败前缀保持历史身份；旧71、28/337和readiness通过不转换为v9接受。

按既定顺序，由root在新静态17/host9与inputs绑定后分配Low完整71、新source187/native/locked build、compiled28/offline/完整readiness；之后再单独批准从ordinal0开始的新fixed4 cohort，不补跑v8后缀。本次不新增验证门或诊断请求。T1仍未runtime接受，L0/E/D继续pending。本文完成后停写。
