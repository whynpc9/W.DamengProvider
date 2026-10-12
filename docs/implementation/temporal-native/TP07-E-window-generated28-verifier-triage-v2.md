# TP07 GeneratedCreate28：generator/differ身份读取器精确分诊v2

2026-10-10。结论：**independent_service_identity_reader_error_confirmed / stored_output_schema_unchanged / corrected_stored_readback_ready**。确切失败点是独立inline检查器将两个不同service的身份结构统一按provider格式读取；N循环没有错误。本报告取代v1的N循环根因推断，v1文件及失败收据保持不动。

本轮仅读取新/v4/v5报告、冻结C#及新startup/assets/实际DLL字节并计算SHA；没有执行SDK、host、自测、validator、DB或反射加载，没有改源码或输出，仅新增本文。

## 确切失败与不变的两种结构

Low恢复的inline第15行对`actual_generator`和`actual_model_differ`均读取`case[service]['assembly']['dll_sha256']/mvid`，随后把两者都与provider identity比较。前者合法，后者的`assembly`实际是字符串，所以第一次按字符串键索引就产生`TypeError: string indices must be integers`；即使只修访问层级，继续比较provider仍然错误。

新shared/TLS全部28项与已接受v4/v5的对应service结构一致，没有host schema回归：

| service | type与assembly形状 | 正确身份来源 |
| --- | --- | --- |
| actual_generator | `W.EntityFrameworkCore.Dameng.Migrations.Internal.DamengMigrationsSqlGenerator`；assembly是含dll_sha256/mvid/source的dict | 当前startup.ef_provider及实际W.EntityFrameworkCore.Dameng.dll |
| actual_model_differ | `Microsoft.EntityFrameworkCore.Migrations.Internal.MigrationsModelDiffer`；assembly是Assembly.FullName字符串；dll_sha256/mvid在service顶层 | 当前cohort的Microsoft.EntityFrameworkCore.Relational.dll |

冻结Program先调用`guard.VerifyProvider(context.GetService<IMigrationsSqlGenerator>())`产生generator嵌套字典；随后对实际`IMigrationsModelDiffer`的Type单独记录`differ.Assembly.FullName`、该Assembly路径SHA和ModuleVersionId，产生扁平differ字典。两条来源本来就不同，不能用同一provider比较循环处理。

N相关事实在v1仍成立：三个集合为list of dict、N选择结果为dict，语义字段与基线一致；但**不能由这些事实推出此次异常发生在N处理**。确切恢复行已经排除该推断，N检查应保持原有效实现。

## 新cohort身份实读

新startup-manifest SHA为`838a410562f80815f21d47df20c897d30a5cc598c5cbd107db490a78154c7834`。

generator的嵌套assembly实际为当前provider DLL SHA`86eda4a85a296e7012b6a8edc29bab8f42198271d7a9a59b2d64b3eb3f3d29b7`、MVID`3da8eead-5bae-447a-8880-6cc9ef3b4104`，与该startup.ef_provider一致。v4/v5 provider是其他编译身份，不能要求新generator等于旧provider hash。

differ的实际Assembly.FullName是：

```text
Microsoft.EntityFrameworkCore.Relational, Version=10.0.12.0, Culture=neutral, PublicKeyToken=adb9793829ddae60
```

新28项顶层dll_sha256均为`a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d`、mvid均为`03b92316-8856-455e-a1c0-0602583c8738`；v4/v5全部28项也逐项相同。

本轮直接读取新replay的`test/TP07TemporalRuntime/bin/Debug/net10.0/Microsoft.EntityFrameworkCore.Relational.dll`，完整文件SHA确为`a1e4f362…`，与startup.binary_hashes的该精确路径及differ顶层hash相同。新project.assets.json确认`Microsoft.EntityFrameworkCore.Relational/10.0.12`为package；这不是W provider、Microsoft.EntityFrameworkCore.dll或Abstractions。MVID值来自实际host及既有相同binary报告，本轮没有执行metadata helper或反射再次提取；后续独立reader应继续从该精确DLL用既有纯PE metadata reader核其MVID，不能把报告自述当唯一MVID证明。

## 唯一应修的检查逻辑

分开处理两个固定service，不做任意schema兼容或逐service统一provider循环：

1. generator要求service=dict、type精确、assembly=dict；嵌套dll_sha256/mvid等于**新startup.ef_provider**及实际新provider DLL字节/元数据；保留受审的provider来源proof。
2. differ要求service=dict、type精确、assembly为上述完整字符串；读取**顶层**dll_sha256/mvid。定位仅允许新startup replay内E host输出目录的`Microsoft.EntityFrameworkCore.Relational.dll`，先核startup.binary_hashes、当前文件SHA及新assets/lock/deps的10.0.12来源，再以既有纯metadata reader读取MVID与differ顶层比较。不从任意assembly字符串拼接路径，不与provider hash比较，不跳过differ身份。

结构错误、缺字段、错误类型/assembly名称/版本/公钥、hash或MVID不符均失败。可用v4/v5结构作证明和负例参照，但新cohort身份必须来自其实际startup/binary，不用旧provider替代。不改N循环、C#服务捕获、frozen generated28_semantics、policy、toolpack、startup或任何host输出。

## 保存输出的后续门

保留原失败`a217c72d5e81578f12c8b8f1b6f6009a16a1b74cf387156c79ec1389cd4d2540`及v1分诊推断。root可授权Low在新的独立证据revision内实施上述服务身份reader修正，并从保存的shared/TLS新host报告及SQL继续**完整重验**：28case模型/default/seed/operation与实际SQL字节、zeroIO、新编译身份、包/源/进程/pin前后及有限gate schema全部不减。

报告和commands保持原路径/真实hash/原`--output`联结；新revision只写新的independent readback/gate，不改写或复制host输出伪装新执行。无须重跑28host、重建或产品修复。原2778项不变是Low失败收据的事实，重新验证仍须新before/after保全。独立readback和generated gate成功前，runtime批准保持pending。
