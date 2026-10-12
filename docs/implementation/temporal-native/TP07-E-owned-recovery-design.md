# TP07 E 单个已创建对象的限定恢复设计

2026-10-09。状态：**design_ready / actual_v3_readonly_evidence_observed / frozen_recovery_tool_gates_pending / recovery_not_executed**。本设计只处理本次E失败创建的一个对象；用户已授权清理本次专用Schema内自建对象，满足下列具体证据和工具门后由root安排Low执行，不需再向用户请求同范围权限。本报告不是已执行、已清理或已满足各门的声明；作者未运行SDK、测试或DB，未改产品、工具或历史证据。

## 固定对象与不可变来源

- Schema/user：`WDM_PROVIDER_TEST`；仅原shared物理profile8.1.5.60/message21/utf-8/override-empty，显式PlaintextAllowed。
- Table：`TPL_406DE87001DF440DA7BA4669`；完整nonce：`406DE87001DF440DA7BA466973AC639C`；PK：`TP07PK_406DE87001DF440DA7BA4669`。
- 原ledger SHA：`10f0c6e81f6c1d91f51cf0424b78c41905f4606d2e6e168144e690a4480430d1`，必须仍为单个Basic/scale7、absence-before=true、create_acknowledged=true、creation_state=create_acknowledged、comment_acknowledged=false。
- 原CREATE SQL SHA：`01060cda014a4ebe81fd9d3cdc69fec2103bdcd28a36ca99b450babf54e3476e`；原main SHA：`8b142476d1e42461c16d6235ad298398438994b2e4a664ad91dad72a77a28503`；原E startup SHA：`92f034035c373bf4e9c8329d509035d0f59fd6f79aaa42a695746765e7050469`。原SQL只作来源核对，不重新执行。
- 同一W `0.1.0-temporal.20261008144915`，package `da8349a2…`、DLL `843710be…`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`；沿原identity/capabilities完整SHA及实际加载对象核验，不重建或替换W。

## 实际只读证据门

必须先取得 **v3实际** `inspection_complete` / 自然exit0的报告、外部执行收据与新检查DLL/MVID/deps身份，并在恢复工具冻结/外部批准输入中固定其实际SHA。不得预填结果、用占位hash、以仅命令行approved布尔或旧v2部分报告代替。本轮现已只读回读实际报告 `TP07-E-owned-inspection-v3-runtime/inspection.json`，SHA `d79b7c748bd9dd8a0c86bd46eef5d741c086b89669a370d60e6a258ab0efcfaf`；对应runtime-control summary SHA `46d746428d837c583e64637dc2f751bc0192bcb88c974413f963829c8fc679fd`。报告完整/exit0、TEST/shared身份、presence_before=presence_after=1、total_rows=0及必需目录全部complete。实际检查DLL SHA `4d4abe72c21e37acc0f8867ca48cb2cb2aac734618eeb5ffea4d995d61edf129`、MVID `a62520e5-5d62-41f0-9555-a1a25f720f6b`；它没有执行DROP。

该报告须确认固定对象presence_before=presence_after=1、total_rows=0，TEST身份和原来源链正确；完整记录四列及顺序、type/precision/scale/nullability/byte length、CHAR_LENGTH/CHAR_USED、唯一PK及ID列顺序、comment、index和index列。v3实际再次确认N为VARCHAR2/128bytes/32CHAR/C，ID为INT4，D/Z为scale7、width9/11，comment NULL；唯一PK为固定名称、ID/position1。index目录是 `INDEX33606864/NONUNIQUE/VALID` 与 `INDEX33606865/UNIQUE/VALID`，index列目录只有后者ID/position1。恢复使用这份实际完整收据，不能以猜测代替，也不为未观察到的index列杜撰记录。

trigger保持 `unknown_from_prior_report`，绑定旧v2 `7f76c573…`，absence_proven=false/query_executed=false；不重新查询该目录，不声称无trigger或原错误可恢复。未知trigger本身不否定上述原absence-before+真实CREATE ACK+精确DDL/nonce/PK/当前结构的所有权证据，不据此扩大目录探索。

## 一次性工具与执行前复查

High只在新的ignored恢复工具目录实施；不改共享OwnedObjects、E/W产品、旧ledger、COMMENT或任何历史报告。工具没有任意table/schema、SQL、模式或批量入口；只读输入路径可配置，但对象和原来源SHA固定。

Low先冻结源码、精确包依赖及新编译DLL/MVID/deps、构建收据；Astra审阅实际冻结源码；root核实际v3证据和编译身份后才开放一次恢复窗口。执行仅通过原TEST wrapper，凭据不入argv/日志/制品，所有异常仅安全type/code/number。

任何数据库调用前先核原artifact、实际v3报告和恢复工具/包身份。新建连接后先验证TEST user/current schema和shared物理profile。然后**重新**读取此对象目录和行数，不借用v3旧row0：当前四列、CHAR语义、PK/ID、comment、index目录须符合已批准的精确对象合同，total_rows必须0；任一必需查询失败、结构不符或行数非零就不执行DROP，留下pending。

如果本次fresh查询已经表不存在，则不执行DROP；另用fresh TEST连接核表count0、精确PK不存在，记录 `drop_attempted=false` 和真实absence证据。不得将“已不存在”写成“本工具已删除”。

## 唯一写动作与最终证明

表仍存在且所有门通过时，唯一允许的写动作是代码中固定的：

```sql
DROP TABLE "TPL_406DE87001DF440DA7BA4669"
```

不允许CASCADE、PURGE、其他DROP、ALTER、COMMENT、跨Schema动作、清空/修改数据或重建对象。执行前先写attempt收据；最多一次DROP尝试，无自动重试。DDL返回值按实际记录，不把−1当成未执行，也不能以返回值替代最终absence。

随后用**另一条新TEST连接**重新核身份/物理profile，查询精确表count0和精确PK count0，记录查询与自然退出结果。只有正常DROP完成加独立最终absence，或明确的already-absent无DROP分支，才能标记恢复完成；无最终证明继续pending。

若DROP或后续检查抛异常，保留原错误和失败退出，不重试DROP、不扩大动作。可以另建fresh TEST连接只读核最终表/PK状态；如观察到absence，分别记录 `final_absence_verified=true` 与失败的attempt，不能抹掉原异常或将unknown outcome偷偷改成成功。root可根据新独立状态证据单独判断残留是否已消除。

本次恢复完成只关闭这个旧残留，不接受E时间行为。N辅助列显式VARCHAR(32)的fixture修复、离线真实GeneratedCreate检查、新E冻结/构建与后续E/D实库仍分别推进；原失败 `bc80be3a…`、v2部分目录失败和所有旧证据保持原样。
