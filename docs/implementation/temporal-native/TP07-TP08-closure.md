# TP07/TP08 收口：EF 七位精度实库验收与交付

2026-10-12。本报告接替 [HANDOFF](HANDOFF-2026-10-12.md) 第 7 节待办。按用户对本轮的授权，不再沿用交接中的恢复 adapter/进程回收夹具与多层审批链；下述验证直接以产品行为和实库结果为门。旧 D guarded 失败、恢复工具 v1–v5 及其证据原样保留，不改写成通过。

## 1. 候选身份

| 对象 | 值 |
| --- | --- |
| W 源码 | 工作区与 TP06 接受的 388 文件 `product-source-manifest.json` 逐字节一致（0 差异） |
| W 包 | `0.1.0-temporal.20261008144915`，nupkg `da8349a2…142d`，EF 测试实际加载 DLL `843710be…6980`（即 TP06 同包） |
| EF 产品 | 已接受 product178（manifest v3 `40012339…b3f8`），未改产品源码 |
| EF 测试增量 | [temporal-seven-digit-migration-test.patch](../../../tools/DownstreamAcceptance/TP07/patches/temporal-seven-digit-migration-test.patch)（`8af099f4…f330`）；[w-candidate-locks.patch](../../../tools/DownstreamAcceptance/TP07/patches/w-candidate-locks.patch)（`72a6b473…8711`） |
| SDK | 驱动 10.0.203；EF 按其 pin 10.0.401 |

锁刷新仅影响功能/规范两个测试项目：`DM.DmProvider 8.3.1.47463` 替换为 W 候选，其余全部包的 resolved 版本不变。产品与单元测试项目在 locked mode 下直接 restore 成功。

## 2. 离线门

| 套件 | 结果 |
| --- | --- |
| W TypeTests | 798/798 |
| W Command / Async / Transport / Session / Transaction | 16 / 134 / 115 / 12 / 88 |
| W Configuration / Diagnostics / Lob / Pool / Security / Isolation | 11 / 83 / 258 / 238 / 39 / 78 |
| EF 整方案构建 | 0 error / 0 warning |
| EF 单元测试 | 824/824 |

## 3. 实库门（shared 8.1.5.60，TEST 身份，显式 PlaintextAllowed）

每次连接前经 `scripts/with-dameng-test.sh` 校验 TEST 账号，工具内再核 `USER`/当前 schema 均为 `WDM_PROVIDER_TEST`。未加载 SA/DEV/bootstrap。

### 3.1 D 遗留对象

按原 ledger（`19f71ff4…2b9`）的精确两表处理，未按前缀匹配：

| 表 | 实际观察 | 处理 |
| --- | --- | --- |
| `TPL_6076EC28B6E14C9F99BCB372`（History） | 已不存在 | 无操作 |
| `TPL_D2CE4C6B94EE43AC8DB38518`（GuardedDt） | 存在；空表；ID INT、V TIMESTAMP(6)；精确 `TP07PK_…`/`TP07CK_…` 约束与 `TP07IX_…` 索引，无其他索引 | 结构与 ledger 一致后精确 DROP |

新连接复查两表 count=0，`cleanup_pending` 关闭。

### 3.2 EF 七位精度迁移（新增，替代原 D guarded/scaffold 门）

`DamengTemporalPrecisionMigrationFunctionalTests`：1/1 通过，结束后本次对象 fresh 清单为空。覆盖：

1. 真实 migrator 创建 `TIMESTAMP(6)` / `DATETIME(6) WITH TIME ZONE`。六位模型 SaveChanges：整微秒与 `MaxValue-9 tick` 成功；`MaxValue`、`MaxValue-1 tick`、任意非整微秒值抛 `OverflowException`，表中行数不变。
2. migrator 执行 6→7 `AlterColumn`：目录 scale=7；旧行 ticks、offset、服务端 `TO_CHAR(…FF7)` 文本均不变。
3. 七位模型写入 `MaxValue`、`MaxValue-1 tick`、带第七位值、`MinValue+1 tick`（及 +08/-05 offset）：typed getter ticks、`GetFieldValue<DateTimeOffset>` offset、服务端 FF7 文本三方一致；EF materializer 读回一致。
4. EF 参数化 `==`/`>` 谓词区分相邻 tick；update 后精确，事务回滚后恢复。
5. scaffold（`DamengDatabaseModelFactory`）报 `TIMESTAMP(7)` 与 `DATETIME(7) WITH TIME ZONE`。
6. Down 7→6：任一列（含仅 TZ 列）存在非整微秒小数时 guard 抛 -20071，两列仍为 scale7、数据不变、history 仍含 Widen；仅整微秒数据时 Down 成功，回到 scale6，剩余值精确保留。

TLS 8.1.4.6 profile 的七位写保持关闭（TP06 已证六位），本测试不适用于该 profile。

### 3.3 EF 既有功能套件 A/B

同一 EF 源、同一过滤（排除需 SA 的 `DamengMigrationScriptFunctionalTests` 与 capability probe）、同一 TEST 实例串行运行：A=W 候选，B=干净 HEAD `fe89bd4` 的 W（无时间改造）。

| 运行 | 通过 / 总数 |
| --- | --- |
| A 候选 | 187 / 233 |
| B HEAD 对照 | 185 / 233 |

A 的 46 项失败全部同时在 B 失败；仅 A 失败的为 0。B 额外失败的 2 项（DateBucket、DTO 比较）是 HEAD 拒绝 EF 七位参数的旧行为，由本改造修复。共同失败按原因：

| 数量 | 原因 | 归属 |
| --- | --- | --- |
| 20 | 测试故意触发服务器错误后在同一连接继续执行/清理；W 对一般服务器错误保守置 Broken（T05/R3 决策；独立探针对 -2106/-20071/-20001/-6103 在 HEAD 与候选结果相同） | EF 测试与 W 连接策略不兼容，非时间 |
| 12 | TEST 账号无 CREATE SCHEMA 权限 | 环境 |
| 5 | 前述清理失败遗留的带 DISKSPACE LIMIT 表使全 schema scaffold 拒绝 | 连锁 |
| 6 | W 将触发器 DDL 内 `:NEW`/`:OLD` 识别为参数标记（`DmParameterBinding`） | W 既有缺口，非时间 |
| 2 | ReadUncommitted/Serializable 预期 SaveChanges 失败但未失败 | 既有事务行为差异 |
| 1 | TZ 列 `GetValue` 返回 `DateTimeOffset`，官方测试期望 `DateTime` | 已知契约差异（计划 §4.5），HEAD 相同 |

两轮各遗留 32 个测试自建 `EF10_*` 对象（测试自身 cleanup 在 Broken 连接上失败）。均按运行开始后 `USER_OBJECTS.CREATED` 精确列出后逐名 DROP，新连接复查该时间窗内剩余 0。脱敏结果：A（历史记录已归档）、B（历史记录已归档）。

## 4. 结论与限制

- A（codec 与回归）、B（七位 ADO.NET，TP06）、C（EF 七位 SaveChanges/查询/迁移/scaffold/guarded Down）在 shared 8.1.5.60 profile 关闭。兼容说明见 [TP-temporal-precision](../../compatibility/TP-temporal-precision.md)。
- 已知未覆盖：TLS profile 上的 EF 实库功能套件；需 SA 的迁移脚本套件；EF CLI `dotnet ef` 外部生成代码编译（原 D G6）。
- 第 3.3 节的 26 项 W 既有兼容缺口（Broken 策略下的 EF 测试假设、触发器 `:NEW` 标记、隔离级别）不属本专项，应另开任务。
- EF 侧交付仍是 patch/manifest，需在 EF 仓库独立应用；不在本仓库内发布。
