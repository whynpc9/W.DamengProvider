# TP07 T1 probe v7 源码验收

2026-10-11，Astra High。结论：**source_accepted**，无阻塞项、无最小修复要求。实际三文件符合已接受设计 `TP07-T1-probe-v7-null-flow-design.md`，SHA256 `eea1af007c54732c9d9b7626c6c808f95e1f7f786579fd01279acffd31fcb582`。本次只接受冻结源码进入新验证门，不表示Python66、SDK build、compiled28、readiness或runtime4通过，不签发任何执行批准。

本轮只读完整diff、源码及历史制品，以标准库进行AST/hash/JSON和有限文本比较；未imports项目模块、执行tests/SDK/DB、修改实现/旧报告/status/approval。唯一新增文件为本文。

## 实际冻结身份

工具目录为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe`。以下均独立重算匹配，aggregate格式保持排序相对路径的 `SHA256 + two spaces + path + newline`：

| 对象 | SHA256 |
| --- | --- |
| host/Program.cs | `c132173ae0c8fd25c469311beb3f8441e6ad5ede978e6aadd70d376afd9b7b24` |
| test_contracts.py | `6c50bb49e8ce41a2d0e5d2118ef45117e2204aab62e896977d61db1b5a229bf8` |
| README.md | `8ea71ee040659b2b0b6279d51633dc7be37de5311ea1bec88f680067b406e40f` |
| actual17 aggregate | `611588a4e41cb14a0b936f43fe51b9e1779a4206e14994c04dcb8c579ed498b7` |
| producer15 aggregate | `d4c64e9034c804b92a896ab0c352c0379979ee3f1674e8f7864755a1ba0b4c3d` |
| host9 aggregate | `0f99c744ac71e3c099a219399068a53e66117a5998812339b986c4551d434d40` |
| implementation-handoff/freeze-report.json | `5059ea386339d9291b64f04030a35a85a7ed668cd22ecc268da5885d98a0b0e9` |
| implementation-handoff/three-file.patch | `6843a408bd4ae00c05de82ec6fabc44fcd828a8f3db12105bab14499d7794acd` |
| prechange/prechange-manifest.json | `72afdfc165b700bee0a9074ecdaba89713506b290a397bd9738c1c881b102633` |

后两个目录的完整前缀为 `.local/temporal-native/verification/TP07-T1-probe-v7-implementation-handoff` 与 `TP07-T1-probe-v7-prechange`。实际v6 immutable17及v7 prechange17均逐项等于v6冻结map；当前仅上述三文件变化，其余14字节不变，包括prepare `dd81f63fdee26fa4e50d3515e906bb1b321c0d5cc73712c1c94a989ab908167c`、validate `66b9f7fb62651dfe012a3652be8fc6f73d14a4528ab56d2c5307743236a741c0`、ProbeContext `5eb5621b2ea6c1ba6d11266a8226d1060f111ec7adb6813afe61d22c61a4bdcc`。

## 两lambda的有限证明

Program相对v6仅第75、78行变化。各行精确符合：首句插入 `IDbContextTransaction activeTransaction = transaction!;`，然后将该lambda原有8次transaction标识符读取统一改为activeTransaction并去掉原使用点的分散`!`。本轮区分字符串和标识符独立构造预期文本，与实际新行逐字相等；两行之外全文逐字一致。

每个lambda现在恰1次外层transaction读取（初始化）、8次局部读取；局部不重新赋值。capabilities的adapter验证、underlying获取、CurrentTransaction引用比较、Identity、ObjectId、TransactionId、两处SupportsSavepoints均指同一局部对象。transaction_SaveChanges的前置两个getter、underlying、Identity、ObjectId、TransactionId、CurrentTransaction比较和报告getter同样闭合。role字面量、报告键、metadata、transactions列表、对象ID/GUID/reference绑定及调用顺序原样保留。

非空证明仍来自前置awaited Begin Step成功赋值；该Step失败会throw阻断后续，两个lambda之间没有外层transaction清空。观察位于各自相关await之前，dispose/reset更晚执行。局部alias仅集中表达此控制流，不增加null guard、分支、异常或fallback；没有NoWarn/globalnullable调整。Begin、tracking/query、rollback、dispose/reset、finally以及context/txConnection/tracked的其它源码逐字不变。能否实际编译通过仍由新SDK门确认。

## 测试pin、历史与保留合同

原66个类名+test方法名与AST逐项完全相等，没有新增/删除方法或helper。测试文件原有全部顶层assignment AST相等，包含V4/V5/V6 overrides与aggregate；仅新增V7_PRODUCER_OVERRIDES和V7_PRODUCER_AGGREGATE。V7严格继承V6，仅覆盖Program/README两个实际SHA；其它15来源沿历史继承。独立重算确认该继承map等于当前actual17排除validate/test所得15，不含测试自身，不形成自指hash。

唯一变动的原Python函数为closed_current_toolpack_fixture：切换V7 pin/aggregate、错误标签、docstring和实际加载模块版本标签；原真实模块路径检查与ROOT行为不变。prepare没有内联host SHA且字节保持，通过新外部17pack/host9 Ref绑定新Program。README准确记录v6两个CS8602与v7局部修订，并保留执行门分离。

交接中七个v6历史Ref（source/native/失败/失败readback/diagnostic readback/safe errors/Python66）全部独立hash匹配；另核实际旧source187全部文件、native immutable四raw与两live locks保持对应hash。v5失败manifest `01808b7ee8a546294b7daf58d24b6d42a391b17f34bae42d97db551454d046ba` 和原raw关系报告 `bd802d330eaf136ea559a01010a12264357171df3b910294b273c8489166c81e` 也保持。v6失败 `c76f49ac669514182225485a04a14fff4dc2b46990aad800e12ae62a4aaaea24` 及原实际66 `587b27882aff32b34fe5138712f0040e653545a46c744d1bca918a4de9069e51` 仅作历史，不转成新build或新66通过。

v6全部raw合同原字节保持：native/locked八immutable raw、真实四live raw和两live locks、仅五bool true插入的完整JSON比较、pruning/header/contentHash及startup真实路径检查均不变。process/DEVNULL/10字段receipt/180+2、CLI13、snapshot字段集、compiled28/fixed4、TLS/证书/保存点/cleanup也保持。原snapshot不含live dgspec，仍由阶段合同和Low整窗口独立保护处理；本轮没有扩展或冒称snapshot覆盖。

## 接受后的门

root可在新实际17/host9静态pack完成后授权Low运行完整新66精确集合；66方法AST未变及旧66通过都不能替代这次执行。随后新inputs/toolpack/host9绑定下的新source187→native→locked build须全部真实通过，核两restore/两build、source/lock/raw/binary/MVID及完整独立readback。新build成功后另授compiled28/Python offline/readiness，runtime4再单独批准。

产品178、manifest400123、新sourceapproval66c、W包与产品824不在本次修订范围，无需重复产品full824。它们也不能替代probe的新执行门。没有沿用部分DLL、失败诊断或旧source/native为新链接受。完成本文后停止写入。
