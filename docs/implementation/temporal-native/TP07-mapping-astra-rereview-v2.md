# TP07 mapping-v2 R01/R02 定点复核

2026-10-09。结论：**R01/R02 closed_static / mapping_static_accepted / runtime_pending**。可由root交Low准备独立执行图锁并重建、运行本候选；没有编译、测试或DB通过声明。

本次只读比较 `.local/temporal-native/TP07/mapping-v2/work` 与保留的v1；175→175无新增/删除，仅DateTime mapping、DTO mapping、共用facet helper、SQL tests及reader fixture五文件变化。当前175文件完整路径/hash与v2 manifest一致。v1其余审阅结论继续适用，未重扫未变范围、未修改源码或运行SDK。

**R01闭环。** fixture现在明确 `currentPos=-1L`、`datasOffset=-1`、reader `m_CurrentRow=-1L`，与W真实字段类型及before-first状态一致。保留一行缓存时，首次Read可以从-1推进到0；下次才进入EOF分支。`TP07TemporalMaterializerTests.cs`与v1逐字节相同，首次Read/ReadAsync、getter结果/错误及终止Read断言没有删除。实际运行与清理仍须Low验证。

**R02闭环。** DateTime及DTO的GenerateNonNullSqlLiteral在值cast、fraction选择与格式化之前调用同一LOCAL TIME ZONE guard。匹配限定时间类型名、可选精度及完整WITH LOCAL TIME ZONE qualifier，大小写/空白明确处理，不把普通TIMESTAMP或WITH TIME ZONE一并关闭。继承的NULL SQL仍不进入NonNull方法；public store-type clone保留subclass，因此两种CLR mapping不能通过换StoreType绕过。

新增测试从真实mapping service取得TLTZ6/7，分别验证StoreType/Precision/DbType metadata、SQL NULL、非NULL NotSupported及precision clone；另从正常DT/DTO mapping调用WithStoreTypeAndSize，验证大小写、空白和DATETIME别名的关闭。ordinary/TZ同精度literal有精确正控制。没有仅用helper负例代替真实mapping，也没有把TLTZ值能力标为开放。

| v2输入 | SHA256 |
| --- | --- |
| `tools/DownstreamAcceptance/TP07/patches/temporal-mapping.patch` | `779d756c995f9fcd37dbb631f9d0ed6dd19b618b96dc872454dc3fd0a793d3b7` |
| 同目录 `mapping-manifest.json` | `01bd21149ac4bacc5e27008346735e900218a6e4b6f16b59e67a2c38d55b5392` |
| DateTime mapping | `8f9c6f4abbf3039fdbdb2543ac7d8526a3e087ee62dd69317af346fd02054ee9` |
| DTO mapping | `8df212fb61e9a0ae88ccebf0b55e5f267eb20511fc06febde1ff46f482d62369` |
| facet helper | `62ce2de5ba52392819f3b7927ca388307d326fa0b6ae3b339903c43280c6c029` |
| SQL tests | `190bbb158d6d6872a9f51e29e4aa6c012dd6a88ed59efeb496320dac4ec3febc` |
| reader fixture | `594ab5d441f4311df5b3978a48999e4b10e3352538fbad22954d3be4a9c98297` |

v1报告/冻结输入保持。W产品与唯一候选包、mapping参数语义、decimal及旧P1证据未改。unit图锁、完整测试、真实EF shaper、literal七位持久化与TP07/C仍是后续独立门；本报告完成后停止写入。
