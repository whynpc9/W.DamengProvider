# Supplement v5 interval 文本通道根因与v6限定设计

> 阅读提示：这是历史失败归因与工具修复记录，不是当前产品缺陷结论或操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。结论：**已确认工具server文本guard误拒绝真实显示格式；产品零值写入/解码/getter在该前缀观察中精确**。root批准v6只对工具I/J的DAY TO SECOND文本分通道校验：W值文本保持规范补零，server显示允许受限的一至两位时分秒。本文不关闭完整interval/TP06门，不修改产品public parse/format，不回写旧失败。

本次仅回读原始报告和源码，无build/tests/validator/DB运行，没有修改产品或工具。

## 1. 原始事实与精确根因

`TP06-supplement-v5-shared-candidate/supplemental.json`实读SHA256 `b7e3b71a9ca7d1b30732bbcba7d7238d0b2c6ef5d241e63ba1f9dc75a6348e3a`；`TP06-supplement-v5/independent-runtime-summary.json`实读SHA256 `df03afb41c2ec3ddc132a24e510d6b60ebc0f7a11603ca0f5ec1f6c56a01c422`。

NATIVE_INTERVAL_zero已取得：ResultColumn CType21/width24/packed1686/mask0、actual cell长度24，raw为 `000000000000000000000000000000000000000096060000`，SHA `8eeec204e09cb4a7e35e0d81c4645bdd0962b2696f0ec7208f2a62c8aad7db96`。独立按六个little-endian int读，其前五个分量全0，第六个0x696=DAY(9) TO SECOND(6)。generic实际返回System.TimeSpan/ticks0；default/provider为DmIntervalDT。

两个通道的真实返回不同且数值相同：

| 通道 | 实际文本 | 实际结果 |
| --- | --- | --- |
| W GetString | `INTERVAL '000000000 00:00:00.000000' DAY(9) TO SECOND(6)` | 56字符；hash `de042e037a898ddb3fd3fa1b29b5f45152ffd3609fe3dfba767578434ce57ca1`；ValueTextGuard通过 |
| server CAST(VARCHAR) | `INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)` | 45字符；hash `49a6b8304f47f44ea1a1a7755100e85570bfe8c0ed1858b4b806f75de8e7e5b7`；ServerStringGet完成，ServerTextGuard失败 |

16个step中前15个完成，最终固定code为ReadServerTextGuardFailed。因此本次已排除raw位置/长度、zero typed投影及W值格式作为失败点。C# SafeNativeText对小时/分钟/秒强制两位，拒绝了已返回的server单数字表示。Fields虽然支持一至两位，却在guard之后，未执行到。此处是直接运行事实，不再是v4阶段的猜测。

当前Python parse_server_text也强制两位，且同时用于server text与W get_string；只把它统一改为一至两位会错误放松W规范格式。另现有工具的prefix/suffix分别可选，限定词未与实际packed leading绑定；这些不是已接受实库bare合同。root已确认不保留残缺wrapper或未观测bare宽免。

## 2. root批准的双通道有限语法

只用于本工具CType21、subtype6、fraction6、leading2或9的I/J列。实际packed须为0x626或0x696，并与原始ResultColumn、effective metadata及payload声明一致；不能从待解析文字反向改变这些约束。

| 规则 | W value/GetString | server显示/CAST |
| --- | --- | --- |
| 完整外层 | 必须为 `INTERVAL '…' DAY(L) TO SECOND(6)` | 同样必须完整；不接受bare或残缺prefix/suffix |
| DAY数字 | 恰好L个ASCII数字，按声明补足前导0 | 1…L个ASCII数字，可保留原已能接受的补零显示 |
| 时、分、秒 | 每段恰好2个ASCII数字 | 每段1或2个ASCII数字 |
| 小数秒 | 必须小数点+恰好6个ASCII数字 | 相同，不补齐、截断或舍入 |
| 数值域 | days小于10^L，hours0…23，minutes/seconds0…59，microseconds0…999999 | 相同，以整数解析并检查 |
| 限定词 | DAY(L)与SECOND(6)必须逐项等于实际packed；只允许DAY TO SECOND | 相同，不忽略错误type/leading/fraction |
| 符号 | 产品规范：负数在引号内单个`-`，非负无符号；与独立expected/raw一致 | 引号内保留既有单个可选`+`/`-`/无符号语法；符号仍须与raw和expected数值一致。负零不得变成普通零蒙混通过 |
| 空白/拼写 | 保持规范的大写关键字及固定ASCII空格 | 相同；本次只变数字显示宽度，不增加Trim、任意空白、关键字大小写或引号外符号宽免 |

本次已实测的是shared DAY9零值的单数字server显示；其它符号、非零、极值、DAY2以及TLS输入仍须各自原case运行验证。有限语法是工具设计允许条件，不等于提前宣布它们全部实测通过。新格式不在表内仍失败并保留v5安全诊断，不能预先接纳任意VARCHAR。

## 3. 最小实现接口和证据链

producer owner在原授权13文件范围内窄改Observations的interval guard/字段解析及必要Offline/runner ID/说明：

- 保持value与server两个GuardReadText通道，使用该reader实际metadata的packed scale传入interval纯解析helper；I/J不允许缺少声明后猜leading。不要仅以列名I/J或文字中的DAY数字替代实际描述。
- 使用完整匹配、ASCII数字、整数范围检查的独立helper处理interval text；server通道允许表内宽度，value通道保持规范。Fields的interval支路不能先Trim/剥掉任意suffix后忽略限定词，应消费已核对的完整声明/分量。非interval文本路径不变。
- value GetString应和独立整数构造的规范文本精确比较：由expected数值与实际声明生成DAY补零、HMS两位、六位fraction和规范sign，不能调用产品GetDTString/formatter产生expected。
- 值与server文本都保原文取证，不通过格式化“修正”原文后再比较。read_attempts、两channel shape/hash及固定guardcode继续保留；诊断白名单仍只管安全输出，不充当语法许可。

validator owner仅原4文件内窄改：拆分server interval display parser和value canonical检查（可共用明确channel参数的纯helper），每次传入已验证的packed declaration。source=server只用server语法；W get_string必须走value规范，不能继续无区分调用parse_server_text。两者解析出的分量仍分别等于固定输入expected及独立24-byte整数raw；qualifier也必须匹配同一个实际描述。raw/hash/metadata/schema、typed ticks、默认/Provider对象形态、NULL及fresh身份等其它断言保持。

当前synthetic interval get_string曾用bare数值串，不代表实际W API保证；测试fixture应改为独立构造的完整规范文本。server fixture独立表示已观察的unpadded形式及原合法padded形式，不从产品formatter生成。完整observations才参与接受，read_attempts不能替代任一原值oracle。

Cases.cs、SQL、0360来源pin、case/vector/API集合、产品包、v4状态恢复和G2有限read tuple不变。设计pin与新增实际C#检查ID可随新工具freeze同步；不得改旧报告或主矩阵工具。

## 4. 最少正反控制

先建立正控，再只包目标解析/验证调用：

1. 使用本次实读raw与两条完整zero文本，验证server unpadded通过、W同一个unpadded文本仍失败；W规范文字通过。SHA与字段预期独立固定。
2. DAY2/9、正/负/零、非零microsecond及1/2位合法HMS的synthetic parser控制；每例明确只是expected-only，不冒称服务器观察。
3. value少DAY补零、HMS少位、额外`+`、负零、missing/多fraction位全部拒绝；server3位HMS、hour24、minute/second60、超leading天数、缺少或非六位fraction拒绝。
4. 全wrapper必须成立：错DAY leading（2↔9）、SECOND限定非6、YEAR/MONTH或其它subtype、残缺prefix/suffix、bare、引号外sign、双sign、Unicode数字、前后空白/额外内容均拒绝。不把这些错误吞成可表示数值。
5. 单独改文本符号/任一数字/微秒、raw字段/hash/packed、schema或typed ticks，必须仍失败；例如`.000001`与`.000000`不能因padding规则相同而被接受。
6. 独立validator的完整synthetic控制保证W规范与server显示各自使用正确通道；若producer将server parser错用于W value或把文本限定词与raw分离，负例必须失败。

原148项Python与44项C#合同不删减；必要的旧synthetic bare fixture改为准确规范expected是测试输入纠正，不改变case向量或产品能力。新冻结后Astra复审、Low完整离线，再root安排新完整shared/TLS运行。leading未批准错误号仍pending，旧失败及cleanup证据保持。

本次仅批准已明确的工具显示语法修复。若后续真实返回其它形状、限定词或数值，继续fail-closed并据其新证据单独裁决，不自动继承本规则。完整Supplement通过前，TP06仍未关闭。
