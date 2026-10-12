# TP07 FF7 表达式前置门限定接受

2026-10-09。结论：**FF7_selected_expression_prerequisites_accepted / EXPANSION53_complete / mapping_v5_evidence_activation_gate_satisfied / full_EF_C_pending**。本轮只读实际r2完整证据、固定源码与条件mapping设计，并对原始数值及文件hash作独立回读；未重新运行SDK、测试、validator或DB，未修改产品、工具、旧报告或原接受旗。

本次接受选定FF7表达式的52项literal专属服务器门和第53项普通DT DATEADD上下文门。它满足 [mapping-v5条件变更设计](TP07-FF7-mapping-change-design.md)（SHA256 `dab10f3276860ffca832b6099cfc4cc0c2d0d48b1eee5d8fa1d597e59e94a696`）的证据激活条件；root可明确开启该设计规定的High新overlay窄窗。**原L0尚未修复/重跑，实际EF HasData/默认/inline/ExecuteUpdate及完整C仍未接受。**

## 1. 实际53项与独立数值回读

主进程、独立cleanup及独立validator均自然exit0、无timeout/termination；validator issues=[]，53项全部exact、loss0、not_run0。不是“最后DATEADD失败但52项可用”的例外分支，本次DATEADD也精确成功。

| 组 | 完整exact case | 实际范围 |
| --- | ---: | --- |
| domain | 40 | DT18/DTO22；tail0…9、min/min+tick、max/max−tick/max−9ticks、carry最后fraction/下一整秒、NULL；DTO极值offset0，普通offset0/+330/−330/−779/+840 |
| DEFAULT | 4 | DT/DTO × min+tick/max，同表typed先行，再真正省略V的INSERT触发默认 |
| inline predicate | 6 | DT/DTO × tail7/min+tick/max，每项正谓词count1、相邻tick负谓词count0 |
| assignment UPDATE | 2 | DT/DTO从`.1234560`真实改到`.1234567`，原值和目标typed对照均精确 |
| DATEADD | 1 | 普通DT `.1234567` 加1second，typed源/期望与实际更新后值一致；无Max加法或DTO扩展 |

实际记录92次INSERT、3次UPDATE、12次predicate SELECT；每次都保留command/API/SQL/hash/参数路由与fresh行数、目录、原列oracle。最终两主表39/45行，四default表各2行，共92行；没有用总计替代逐操作收据。

本轮逐项回读107个操作的实际oracle：**103个非NULL raw cell**重新按整数bit位置解释年月日/时分秒/ns/offset，与固定case期望、FF9及typed local ticks/UTC交叉检查，差异0；另4个NULL操作分别为DT/DTO的typed与SQL NULL，DBNull/NULL raw/FF9均一致。未调用driver encoder或重跑producer/validator。

关键结果包括：

- 四个DEFAULT实际省略V写入的FF9为 `0001-01-01 00:00:00.000000100` 或 `9999-12-31 23:59:59.999999900`，DTO保`+00:00`；与各自同表typed行相等。
- 六组predicate实际scalar均为(1,0)，正/负使用同目标row与相邻1tick值，参数count0；它们在更新前完成。
- 两assignment原local ticks为 `639204129161234560`，实际更新后为 `639204129161234567`。
- DATEADD源为 `2026-07-23 14:15:16.123456700`，typed期望及更新后均为 `2026-07-23 14:15:17.123456700`，第七位未丢；本次无独立R04 server-expression blocker。

NULL使用SQL NULL而非调用FF7解析字符串；DEFAULT目录文本仍只是有界hash记录，不声称完成任意default SQL正规化。默认行为的决定证据来自本次CREATE与省略V INSERT后的实际原列值。

## 2. 同候选和修正startup来源

实际physical为WDM_PROVIDER_TEST/同Schema、shared 8.1.5.60/message21/utf-8、ordinary非BDTA。settings继承SCREEN25真实-5504 unknown，本轮没有重新查询、fallback或改设置；不推出IFUN_DATETIME_MODE值，也不将同version/message视为所有实例literal行为保证。

| 运行制品 | 实际身份 |
| --- | --- |
| W package | `0.1.0-temporal.20261008144915`；nupkg SHA `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d` |
| W DLL / MVID | `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980` / `aa23e673-957c-4b1e-bf0f-291ff60077e6` |
| 新constructor startup DLL / MVID | `6aac11bb12bf05de69cc5a72f1eadab6ec4dc8ef84eea1653d60d2adea12ccbf` / `e1e29239-ada9-4c11-9f25-b1d7fee268c1` |
| 实际EF provider DLL / MVID | `4900096be3c8a30a6bc99b3f82484f17d3e2d071ef28cdf369879f133d2dd9a1` / `bb456cf9-ac11-4ec4-9faf-eb67e7b9fcbe` |
| constructor deps | `f3ec91299339bc9a4e751f37b112ba5794a5a0012d80648afe4aa71400a96d0a` |
| 新14文件source pack | `f88e368f94fe89e6a6868a8bb70c6d51345700bed6186c890cacd1eb0d777219` |

独立prepare固定原EF175+compile7=182；新增constructor lock后183，两个assets，W PackageReference和EF10.0.12身份保持。实读14合同测试原日志为14 tests/OK，未在本轮重跑；lock/build的SDK10.0.401、禁build server/单节点实际命令均exit0且日志来源保留。

原首运行因startup manifest错误写schema2，在CandidateGuard要求schema1处失败：DB未打开、0case、0对象/ledger；失败summary SHA `7f31161f9e87651ec2d78abbf4717be78616a34c45a59fed3fdbee6ba8c471d2`仍在。root另建修正startup SHA `445b7ad00840188e6794e5d5ad81621d4a356ce3665e5f0adc7832eaf4ed7ebc`，与原startup的JSON唯一差异是schema_version 2→1。

修正build receipt SHA `f01c75f627faec5a7852429d35e78c273a5b14e03186a445599117f55a5b4fd6`明确引用旧SDK事件；原commands/logs逐项相同，startup binaries/source/locks/deps未变，**没有声称修正时重新build**。本次r2真实命令使用这个新startup路径/hash，实际加载constructor DLL与其对应。原schema2 startup/原build receipt/原失败全部保存，没有原地改写失败。

本輪另将r2 external receipt所列**4463项input文件重新hash回读**：before/after相同、当前missing0/drift0。包括新源包/编译输入/locks/assets/binaries、历史SCREEN25五份artifact及旧14源快照、原L0失败和修正来源。不存在只依赖producer自报包身份的接受。

## 3. 清理与证据文件

六张本次owned表在main finally、独立cleanup及独立finally三份收据中全部fresh absent/count0；三者绑定同一ledger SHA `8648a76953a452c3d3166247a5bd1347df707cd1dea98c1854ac78e57aa41e48`。没有恢复待办或保留对象。独立cleanup使用同一新startup、精确ledger及TEST wrapper，未加载admin/SA。

以下路径相对 `.local/temporal-native/verification/`，本轮实读身份：

| 文件 | SHA256 |
| --- | --- |
| TP07-constructor-v3-runtime-r2-validation/verification-summary.json | `e74a15d26c7c0d50862c180918d5e1d806d42d1672c1e2e271f084975d7eda58` |
| TP07-constructor-v3-runtime-r2-validation/validation.json | `2e9d4c4deb682ebee70d1ea6aa98dcfabfe7669e0911f26ee005991830d1d3b4` |
| TP07-constructor-v3-runtime-r2/main/report.json | `34173238852c9e172887006cafdc0406fcd37460c11cd58a87131a18942411c4` |
| TP07-constructor-v3-runtime-r2/cleanup/report.json | `d397d5657cbb0e3ff8006d0df1debda35cba5e4b4438abcc9783d81d1b1cb540` |
| TP07-constructor-v3-runtime-r2/execution-receipt.json | `0c93c93486b363310ea151471f422c361bbb868e0a59eb2434ca20a7bd9ce32a` |

原L0失败 `ed5310c8feccfb0705b85a4bda12333a3ffd60a5ae9ea353820e37486757290d`和SCREEN25的B0四项/CAST两项LOSS仍为原事实，不因FF7成功变成通过。各producer/validator原有 `literal_support_accepted=false/full_EF_C_accepted=false/product_patch_authorized=false`等旗保持不回写；本报告单独记录root/Astra的限定前置门接受。

## 4. 可激活的唯一产品方向

已满足条件设计的全部52项literal证据条件，且第53项也通过。root可依 `dab10f…`设计开启**新mapping-v5 overlay**：唯一产品文件 `DamengTemporalParameterFacets.cs`，只调整 `TimestampLiteral` 的最终fraction7分支，DT→TO_TIMESTAMP FF7、DTO→TO_TIMESTAMP_TZ FF7，保原本地时间与offset，日期/offset间一个空格。整秒/整micro但声明7同样走FF7，不按值尾数动态fallback。

0…6、bare fallback6、NULL、>7非NULL拒绝、TLTZ/历法/offset范围守卫、实际参数Scale/Precision、reader/materializer、mapping registry、迁移/query generator、DATEADD translator与W产品均保持既有合同。允许的EF测试期待/有限新增断言和单独L0 CheckLiteral/validator工具窄修仅沿已审条件设计执行；不是全树替换TIMESTAMP或放宽任意SQL字符。

实施仍需High停写、新patch/manifest/source/lock/制品冻结、Astra/root代码审阅、Low实际M1回归/14参数观察和修订后的L0 shared/TLS实库门，再按原DAG进入E/D。本次controlled direct SQL证明不了真正EF HasData、DefaultValue、DefaultValueSql、inline translation、generated值回读或ExecuteUpdate；这些尚未执行。即使服务器DATEADD本次无损，也不跳过后续EF真实翻译链。

所以可解除的是**FF7窄修设计的表达式前置阻塞**，不是宣布产品已修复、原L0已通过、七位literal对所有配置通用、R04/E/D全部接受或完整C/PR/发布完成。报告完成后停止写入。
