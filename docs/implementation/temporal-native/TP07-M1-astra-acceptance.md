# TP07 M1：M01/M02 限定离线接受

2026-10-09。结论：**M01/M02 scoped_offline_acceptance_recommended**。root可关闭本轮实际mapping参数合同、literal生成静态合同及编译getter调用真实W缓存reader的离线门。完整EF生成shaper、SaveChanges/query实库、literal持久化、scaffold/migration及C门仍未接受。

本次只读核对Low执行汇总、独立readback、TRX、原始observer报告与源码/资产哈希；未重跑SDK、restore、测试、observer或DB，未修改任何旧证据的接受旗。

## 1. 实际结果

九个过程（SDK、unit locked restore/build/discovery、两测试门、observer locked restore/build/run）均退出0且无timeout。实际SDK10.0.401，未重新force-evaluate，无DB/TEST wrapper。

| 运行门 | 实读结果 |
| --- | --- |
| 新时间mapping/materializer/SQL测试 | 51/51，failed/error/skip/abort为0 |
| 受影响旧回归及候选身份guard | 102/102，同样无失败/跳过/中止 |
| 两集合唯一case | 151；两个DefaultsTransferFraction value/null guard重复，不能写成153个独立case |
| actual EF model参数observer | 7种模型×value/null，共14个唯一case |

两份TRX的所有UnitTestResult均Passed，case集合与summary一致，重叠确为声明的两个guard。v3失败的真实model拒绝及caller-owned连接清理测试现包含于51项通过结果；这不升级为原EF context-owned失败初始化诊断路径已修复。

实际observer原字节hash匹配，逐项与summary一致：

| 模型分组（value与NULL相同） | 参数Precision/Scale | 显式位 |
| --- | --- | --- |
| 默认DateTime / DateTimeOffset | 0 / 7 | Precision未设，Scale显式 |
| HasPrecision6 / TIMESTAMP(6) | 0 / 6 | 同上 |
| TimeOnly / INTERVAL TimeSpan | 0 / 6 | 同上；interval leading不成为参数Precision |
| Decimal(12,2) | 12 / 2 | 两者显式 |

这些是实际EF service/mapping.CreateParameter读出的字段，未借清零Precision掩盖P1基线；P1已经观察普通时间Precision本为0未设，修复重点是DT/DTO精确Scale。model/mapping facet及decimal合同保留。

## 2. 同候选、source与历史证据

unit和observer执行目录的完整source hash均与本次记录一致，无漂移。unit实际测试DLL/deps、W与EF provider资产实读hash匹配；observer报告的实际service provider DLL与执行目录资产一致，connection/command/parameter确为W路径。两lane的EF DLL由各自隔离构建产生并分别记录，不把两个EF hash混同为同一制品；W始终是同一已接受包的DLL。

W候选仍为 `0.1.0-temporal.20261008144915`，package SHA `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，实际DLL SHA `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。没有W产品重编/重打包或official fallback。

所有old-input before/after清单相等，现存文件再次实读hash也全部一致；历史NuGet hash语义、project ID大小写、xUnit analyzer及v3 Dispose失败均保留原归属。Low汇总中的`mapping_accepted=false`/`C_accepted=false`是当时等待root审阅的原值，保持不回写。

| 证据 | SHA256 |
| --- | --- |
| `TP07-M1-v4-offline/execution-summary.json` | `95aa7eb702ce1f0ad2a4ed1779cc4637a47c26b783ce615a39e358144b198550` |
| 同目录 `independent-readback.json` | `224bb25afa1195570eca8eb7cda54e123f7c028712af1f1ad66cfbfc59bdef72` |
| 同目录 `unit/tp07-new/tp07-new.trx` | `65014916a615af73c29e170307f3c682cf1ec3718c731e716a03b668186509fd` |
| 同目录affected-regressions TRX | `ca7bd367c91b4542272663b3f9951811c67572eea682e7dde677dd1dfaadff84` |
| 同目录 `observer/observer.log` | `4911343b6f0609ed9fb006fd9d11cb929c89c967658e153d3eda07a4b1c45ce5` |
| `TP07-M1-v4/source-manifest.json` | `d5fb6ee75e36df8afdd635be469b4c83eb1f2e0b2a36709590c5c0b4b781350f` |
| `TP07-M1-v4-graph-reuse/lock-summary.json` | `1a3c687a8419152e2b0ee8590f9b35e6117ff052273e13f1821f1a4d8c7c0f12` |
| 同graph目录 `execution-source-manifest.json` | `a8ae400c8760928b5bb6f2fe6f8d9a4e5f2d55c5853418e32a321f25b12d76f1` |
| [v4静态复核](TP07-mapping-astra-rereview-v4.md) | `e83eadf16dd3a73ddbf38f0fa041632602eef60ddf75168315f2f60277ad8172` |

制品目录相对 `.local/temporal-native/verification/`；mapping patch/manifest保持v4静态报告固定身份。

## 3. 接受边界

本轮证明实际mapping的precision→Scale、clone/NULL/DATE/TimeSpan-TIME拒绝等限定合同，以及DTO生产MethodInfo不走Parse、其编译表达式调用真实W缓存投影、非整tick拒绝及精确文本。高精度raw样本仍为内部手写fixture，没有8/9非整tick服务器样本证明。

这里没有完整EF query生成shaper运行、真实参数写入、默认/seed/literal七位持久化、裸类型目录默认、scaffold或guarded Down实库证据。未跑整个EF unit/functional/CLI全集；官方专属retry/DTO断言也未由本报告一概接受。后续仍须按已批准TP07设计，以同一W候选和隔离EF source完成这些门，才能讨论C与最终交付。完成后停止写入。
