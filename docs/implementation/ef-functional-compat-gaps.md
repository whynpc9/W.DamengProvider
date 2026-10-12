# EF 功能套件兼容缺口（非时间精度的 46 项）

日期：2026-10-12～13。基线：分支 `t3/ef-functional-compat-gaps`，父提交 `5dcc3c8`（时间精度专项）。起点是 [TP07/TP08 收口](temporal-native/TP07-TP08-closure.md) §3.3 的 46 项共同失败。本报告只记数字、布尔和生成的对象名，不含连接信息。

## 1. 结论

| 组 | 数量 | 判定 | 处理 |
| --- | ---: | --- | --- |
| 1 服务器错误后继续用同一连接 | 20 | **W 缺口**：官方驱动下会话可用且已实证状态同步 | W：按实测错误号保留会话（§2.1） |
| 2 无 CREATE SCHEMA | 12 | 环境 | 用户选择临时授权：单次运行后已撤销（§4） |
| 3 遗留 DISKSPACE LIMIT 表 | 5 | 组 1 连锁 | 组 1 修复后自动消失 |
| 4 触发器 `:NEW`/`:OLD` | 6 | **W 缺口** | W：无参数命令不做标记绑定（§2.2） |
| 5 RU/Serializable SaveChanges | 2 | **EF 测试断言的是官方驱动缺陷** | EF 测试补丁（§3） |
| 6 TZ 列 `GetValue` 类型 | 1 | W 有意差异（计划 §4.5） | EF 测试补丁（§3） |

实库结果（shared 8.1.5.60，`WDM_PROVIDER_TEST`，显式 PlaintextAllowed，过滤同 TP07：排除需 SA 的 `DamengMigrationScriptFunctionalTests` 与 CapabilityProbe）：

| 运行 | W 包 | 授权 | 通过 / 总数 |
| --- | --- | --- | --- |
| TP07 基线 | `0.1.0-temporal.20261008144915` | 无 | 187 / 233 |
| A | 中间包 `…1012150453` | 无 | 217 / 234 |
| B | 中间包 `…1012155836` | 临时 CREATE SCHEMA | 230 / 234 |
| C | 最终包，警告补丁前 | 无（已撤销） | 216 / 234 |
| **D（最终）** | `0.1.0-efcompat.20261012165201` | 无（已撤销） | **219 / 234** |

D 的 15 项全部是环境权限：14 项 CREATE SCHEMA（`-5512`），1 项 CREATE MATERIALIZED VIEW（`-5622`）。其中 12 项已在 B（临时授权）通过；另 2 项 `ExhaustedSequence…` 在 B 因 `-7069` 尚未入白名单而失败，该号已由探针实测加入；用户只批准一次授权运行，未再授权重跑这 2 项，它们在最终包上的 EF 级正向结果**未验证**。组 1/3/4/5/6 的 44 项在 D 全部通过，`DamengTemporalPrecisionMigrationFunctionalTests` 通过。脱敏逐项结果：[A](evidence/EFCompat/ef-functional-runA-no-schema-grant.json)、[B](evidence/EFCompat/ef-functional-runB-temporary-schema-grant.json)、[D](evidence/EFCompat/ef-functional-runD-final-no-grant.json)。

233→234：新增 `DamengTemporalPrecisionMigrationFunctionalTests` 1 项；组 5 两项改名（断言改变，见 §3）。

## 2. W 产品变更

### 2.1 语句级服务器错误保留会话

**证据。** 新探针 [tools/ErrorRecoveryProbe](../../tools/ErrorRecoveryProbe/Program.cs) 同源编译为官方 `DM.DmProvider 8.3.1.47463`（O）与 W。每个场景对 sync/async、autocommit 与本地事务（SQL 在事务内合法时）分别在新连接上触发错误，并检查：

1. 同连接下一条语句执行成功，且 `SESSID()` 与错误前相同（同一服务器会话）；
2. 新连接读回失败语句无任何效果（含 1000 行中途失败的 INSERT、先 DML 后 RAISE 的块、部分行触发器拒绝）；
3. 另一会话 `LOCK TABLE … IN EXCLUSIVE MODE NOWAIT` 成功（无遗留锁/未结束事务）；
4. autocommit：随后写入对新连接立即可见；事务内：错误前写入对新连接仍不可见（未被提交），提交后错误前后两行都在（未被回滚）。

| 证据 | 场景数 | 全部 4 项成立 |
| --- | ---: | ---: |
| [O，最终矩阵](evidence/EFCompat/probe7-Official.json) | 82 | 82 |
| [W HEAD `5dcc3c8`](evidence/EFCompat/probe4-W-head.json)（72 场早期矩阵） | 72 | 2（仅事务内 -2106）；其余 70 场连接 Broken |
| [W 最终候选](evidence/EFCompat/probe7-W-candidate.json) | 82 | 82 |

两端不论在哪个驱动下，服务器侧结果相同：语句原子失败、不影响会话或外层事务。W HEAD 的 Broken 是纯客户端保守，EF 测试的假设与 ADO.NET 常规用法一致；改测试为换新连接会掩盖这一差异，故修 W。

**白名单**（[DmServerErrorRecovery.cs](../../src/W.DmProvider/Internal/Sessions/DmServerErrorRecovery.cs)，全部已在上表实测）：

| 号 | 含义 | 号 | 含义 |
| --- | --- | --- | --- |
| -564 | DISKSPACE LIMIT | -5622 | 无 CREATE MATERIALIZED VIEW 权限 |
| -2007 | 语法错误 | -6169 | 值过长 |
| -2106 | 无效表/视图名 | -6602 | 唯一约束 |
| -2124 | 对象名重复 | -6607 | 外键（子插入/父删除） |
| -2207 | 序列不存在（DEFAULT NEXTVAL） | -6609 | NOT NULL |
| -2713 | 不支持的 IDENTITY 类型 | -6628 | 父行仍被引用（UPDATE … NO ACTION） |
| -3510 | RAISE 代码越界 | -7069 | NOCYCLE 序列耗尽 |
| -5512 | 无 CREATE SCHEMA 权限 | -20000…-30000 | `RAISE_APPLICATION_ERROR`（实测 -20000/-20001/-20002/-20003/-20071/-20999/-29999/-30000；-19999/-30001 服务器转为 -3510） |

**不变的门槛。** 仍要求完整四段错误体严格解析且无尾随数据、当前 wire/invocation/线程身份在同一 session gate 内匹配、Query/Reader purpose、execute 而非 prepare、server version `8.1.5.60`，以及之后完整验证的 STMT_CLOSE；关闭失败仍关闭连接。取消/期限先胜时不提升。

**新增的允许上下文。** 原只允许“本语句自己的本地事务 Active”；现增“无本地事务、`AutoCommit=true`、本地事务状态为 None/Committed/RolledBack/CompletedExternally”。仍 Broken（均有离线负例）：白名单外的号（含 -6001、-19999、-30001、-1）、其他 server version（含 TLS profile 8.1.4.6）、`AutoCommit=false` 但无 `DmTransaction`（服务器可能持有客户端不跟踪的隐式事务）、事务 Starting/Committing/RollingBack/OutcomeUnknown、事务 owner 变更。

修改点：`T02_02000094.cs` 的 `ThrowOwnedStatementServerError`、`DmSession.AcceptServerError`、`DmCommand.IsOwnedVerifiedServerError`。离线测试：`ServerErrorClassificationTests` 新增 12 个正例（含 autocommit 与事务）、手动提交/在途状态负例，原“非白名单即 Broken”的参数行改为未实测的号。原 -6602/8.1.5.60 与 -2106/8.1.5.60/autocommit 两行本来断言 Broken，现已由实库证据推翻，删除并由正例替代。

### 2.2 无参数命令不解析标记

`DmParameterBinding.Create` 在参数集合为空时返回零标记绑定：该命令走直接执行，SQL 原文不经绑定，`:NEW`/`:OLD`/`:X`/`?` 由服务器判定。词法扫描仍先执行（未终止引号/注释仍拒绝）；事务语句头守卫使用独立的 `ScanTopLevelStatements`，不受影响；服务器若仍描述出参数，`ValidateServerCount` 仍 fail closed。有参数时严格匹配、混用拒绝、未用参数拒绝不变。

实库（[O](evidence/EFCompat/markers-Official.json) / [W HEAD](evidence/EFCompat/markers-W-head.json) / [W 候选](evidence/EFCompat/markers-W-candidate.json)）：触发器 DDL O 与候选成功且 UPDATE 后 VERSION 1→2，HEAD 客户端 `InvalidOperationException`；无参 `SELECT :X`/`SELECT ?` O 与候选均为服务器 -6804。离线 `ParameterlessMarkerTests` 6 项。

## 3. EF 测试补丁

交付为 patch，不改 EF 仓库。EF 源为 TP07 已接受的 178 文件 product + 时间测试（取自父 worktree）。

- [ef-functional-test-adaptations.patch](../../tools/DownstreamAcceptance/EFCompat/ef-functional-test-adaptations.patch)
  - 组 5：`SaveChangesFailsAfterBeginningReadUncommittedOrSerializable` 断言 `CommandText has no value`。T11 已证明这是官方驱动把隔离控制 statement 借给用户命令的缺陷（[T11](reports/T11.md)），W 有意修复。改为 `SaveChangesCommitsAfterBeginningReadUncommittedOrSerializable`：两级别下跟踪式 update 与 insert 各影响 1 行、提交后新 context 读回精确值，再一次更新回滚后值不变。断言强于原测试（原测试不读回数据）。
  - 组 6：`DateTimeOffsetRawAdoReadbackPreservesServerEvidence` 的 `GetValue` 类型改为 `DateTimeOffset`，其 wall clock、Kind 与服务器文本断言保持，并新增 offset 与 UtcTicks 断言。不改 W。
  - 所有自建 model-cache 服务的 fixture（14 个文件）对 `ManyServiceProvidersCreatedWarning` 改为 Log，沿用套件既有 Math/Migrations fixture 的写法。组 1 修复后更多测试跑到创建 context，全套件单进程内 service provider 超过 20，EF 默认把该警告作为异常；触发的是哪个测试取决于顺序（A 为时间测试、B 为 DateBucket、C 为 3 个字符串测试），单跑不触发。属套件组织问题，不改任何业务断言。
- [w-efcompat-candidate-pins.patch](../../tools/DownstreamAcceptance/EFCompat/w-efcompat-candidate-pins.patch)：中央包版本、五个 lock 的 W 条目（其他包 resolved 不变）、TP07 mapping 测试与 observation 的 DLL SHA-256/MVID 钉定（`1b576a31…f3f7c` / `1b36a175-023b-4e7b-bc8a-253e47c90892`）。

## 4. SA 变更（临时 CREATE SCHEMA）

用户选择“临时授权”。操作、范围与回滚说明见 [维护记录](maintenance/EF-test-create-schema-temporary.md)。实际顺序（证据 `evidence/EFCompat/schema-priv-*.json`）：

| 步骤 | 连接 | 结果 |
| --- | --- | --- |
| 0 inspect | TEST 新连接 | 权限行 0，自有 EF10_ schema 0 |
| 1 `GRANT CREATE SCHEMA TO "WDM_PROVIDER_TEST"` | SA（子 shell 加载） | before 0 → after 1，ACK |
| 2 inspect | TEST 新连接 | 权限行 1，ADMIN OPTION 行 0 |
| 运行 B | TEST | 230/234 |
| 3 inspect | TEST 新连接 | 权限行 1，自有 EF10_ schema 2（见下） |
| 4 `REVOKE CREATE SCHEMA FROM "WDM_PROVIDER_TEST"` | SA | before 1 → after 0，ACK |
| 5 inspect | TEST 新连接 | 权限行 0 |
| 6 清理后 inspect | TEST 新连接 | 权限行 0，自有 EF10_ schema 0 |

未授予 DBA/ANY/对象权限，未触及 DEV、其他用户或实例设置；测试未以 SA 运行。当前 TEST 权限与授权前一致。

## 5. 剩余失败与清理

运行 B（临时授权）的 4 项均已定位：

- `ExhaustedSequenceContinuationIsRejectedBeforeGeneratingInvalidDdl`（2 项）：故意触发的 -7069 不在当时白名单。已用 O 与 W 实测后加入（上表 82 场含此项）。该测试 cleanup 断开时遗留两个 TEST 自有 schema `EF10_EXH_4B168C6F2B0E`、`EF10_EXH_B233CF0F88B4`（各只有一个 `EXHAUSTED_SEQ`）。撤销权限后按精确名先 DROP SEQUENCE 再 `DROP SCHEMA … RESTRICT`（仅 TEST 自有的空 schema），新连接复查 0。
- `FactoryRejectsMaterializedViewPrebuiltTableAndPreservesTableFiltering`：TEST 无 CREATE MATERIALIZED VIEW 权限（-5622）。**环境限制，未授权**：超出用户本次批准的 CREATE SCHEMA 范围。修复后它以服务器错误本身失败，不再遗留对象。
- `DateBucketsRunOnServerInProjectionFilterGroupAndOrder`：上述 `ManyServiceProvidersCreatedWarning`，已由 §3 补丁处理。

运行 A 遗留的 4 张表（-6628 与 -5622 测试，当时白名单尚未含这两个号）按运行开始后 `USER_OBJECTS.CREATED` 精确列出并逐名 DROP，新连接复查 0。探针每轮自清理并新连接复查 0。

运行 C/D 均无遗留对象（新连接按运行开始时间列出 0），TEST 自有 EF10_ schema 0，CREATE SCHEMA 权限行 0。

## 6. 离线门

| 套件 | 结果 |
| --- | --- |
| W TypeTests | 804/804（原 798 + 6） |
| W Pool | 255/255（原 238；删 4 行旧断言，增 21 场） |
| W Transaction / Diagnostics / Isolation / Command / Session | 88 / 83 / 78 / 16 / 12 |
| W Async / Transport / Configuration / Lob / Security | 134 / 115 / 11 / 258 / 39 |
| EF 整方案构建 | 0 error / 0 warning |
| EF 单元测试 | 824/824 |

变基到 `origin/main` `1f04465`（含 PR #3 的 6 个时间修复提交）后重跑：TypeTests 952/952（main 新增 148 项），其余 11 套件计数同上表全部通过；TEST 上变基后 W 探针 82/82、标记检查与候选一致。EF 功能套件 D 在变基前的包上运行，变基引入的 main 改动仅涉及时间编解码，与本报告两处修改无交叉。

## 7. 限制

- 白名单只对 8.1.5.60 profile 成立。TLS 实例（8.1.4.6）未做同样探针，仍保守 Broken。
- 未覆盖的错误号（如死锁、超时、会话级错误）仍 Broken，需要时另行按本探针实测。
- `-5622` 对应测试需 CREATE MATERIALIZED VIEW；`DamengMigrationScriptFunctionalTests` 仍需 SA 通道。两者未授权、未运行。
- EF 侧交付仍是 patch，需在 EF 仓库独立应用。
