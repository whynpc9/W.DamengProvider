# EF Core 接入反馈的驱动修订（issue #16）

本页记录 [issue #16](https://github.com/whynpc9/W.DamengProvider/issues/16) 的处理结果。修订在 `0.1.0-preview.1` 之后，已随 [0.2.0](../release-0.2.0.md) 发布。实库证据只覆盖 shared DM 8.1.5.60 / UTF-8 / 显式 `PlaintextAllowed` 的 `WDM_PROVIDER_TEST`；其他 profile 维持原保守行为。

## 1. 传输失败统一为 `DmException`

驱动自身 socket I/O（TCP 连接、发送、接收，含 TLS 之上的流）抛出的 `SocketException`/`IOException`，在公开边界包装为 `DmException`，原异常保留为 `InnerException`：

| 字段 | 值 |
| --- | --- |
| `ErrorKind` / `DriverErrorCode` | `Transport` / `WDM_TRANSPORT` |
| `Phase` | 实际失败阶段：`Connect`、`Send`、`Receive` 等 |
| `OperationOutcome` | 请求开始发送前为 `NotSent`；发送开始后为 `Unknown` |
| `ConnectionReusable` | 始终 `false`；连接进入 Broken 或打开失败 |
| `TransactionOutcome` | 本地事务 Active/Committing/RollingBack 时为 `OutcomeUnknown` |
| `Number` | 0，不伪造服务器错误号 |

覆盖 `Open`/`OpenAsync`（同步多端点路径保留原聚合消息，并把最后一个 socket 失败作为 `InnerException`）、`ExecuteNonQuery`/`ExecuteReader`/`ExecuteScalar`（同步与异步）、Reader 读取与关闭、事务控制、更换已准备命令的 `Connection`。TLS 握手中对端重置或 EOF 也按 Transport 报告，`InnerException` 为保留原 I/O 原因的 `AuthenticationException`；证书校验失败仍是认证失败，不归为 Transport。同步 LOB 消息路径与同步多端点连接原先产生的驱动通信错误（`ECNET_COMMUNITION_ERROR`）在公开边界重新包装：保留原消息文本，`Number` 为 0，`InnerException` 为原 socket/I/O 异常。调用方传入的流式 LOB 输入抛出的 `IOException` 不是传输失败，仍按原异常抛出。超时、取消、提交结果未知的既有类型不变。`DmDiagnostics.FormatException` 对这类异常输出 `code=WDM_TRANSPORT`。

**行为变化。** 依赖 `catch (SocketException)`/`catch (IOException)` 的调用方应改为检查 `DmException.ErrorKind == DmErrorKind.Transport` 或 `InnerException`。

## 2. 事务内只含 DML 的匿名块

Active 本地事务中，以下形状不再被发送前拒绝：一个 `BEGIN … END;` 块，块内每条语句以 `INSERT`、`UPDATE`、`DELETE`、`MERGE`、`SELECT` 或 `NULL` 开头，`END` 后无标签、无其他语句。判定使用既有词法扫描，字符串、引号标识符、注释和括号内的内容不作为语句头；只由引号标识符或标点组成的语句（例如 `"P"(1)`）视为非 DML；`BEGIN` 及块内每条语句的首词前不能有任何其他记号（含 `<<label>>`），`END` 后不能有标签（含引号标签）。以 `<<label>>` 开头的语句在 Active 事务中一律拒绝（原栅栏也未识别带标签的块，本次一并补上）。SQL 原文不改写。

仍在发送前拒绝（`NotSupportedException`，会话和事务保持 Active）：`DECLARE` 段、嵌套块、`IF`/`LOOP` 等控制流、`EXCEPTION` 处理段、过程调用（含 `CALL`/`EXEC`）、`EXECUTE IMMEDIATE`、`COMMIT`/`ROLLBACK`/`SAVEPOINT`/`SET TRANSACTION`、DDL、`END` 带标签、块后接其他语句。块外的普通语句判定不变。

需要其他 PL/SQL 结构时，可把块放在事务外（autocommit）执行，或拆成多条普通 DML 命令放在同一事务；EF 迁移中可对该 `Sql()` 使用 `suppressTransaction: true`。块里调用的函数或触发器与单条 DML 一样由服务器执行，驱动不分析其内部。

**实库（8.1.5.60，sync/async）。** 事务内块执行后回滚：新连接看不到修改；提交：修改与插入可见。块中第二条 INSERT 违反主键（`-6602`）时，块的第一条 INSERT 在事务内和新连接中都不可见，块前的同事务修改保留，会话按既有白名单保持可用；回滚后新连接无任何效果。四类控制块均在发送前拒绝且事务仍 Active。

## 3. 8/9 位时间列的 typed NULL 与 CLR 值

`Scale` 为 8 或 9（或超过当前 profile 七位上限）的时间参数，值为 `null`/`DBNull` 时不再拒绝：

- 有可靠 fixed describe（例如 Prepare 后的 `TIMESTAMP(9)` 列）时，沿用服务器描述的类型与小数位声明；这一放宽只在已验证七位 profile（8.1.5.60 / UTF-8）开放，其他 profile 的 fixed 声明仍按原规则检查；
- 无 fixed describe 时，声明采用当前 profile 支持的小数位（已验证 extended profile 最高 9，其他 profile 为 legacy 6）；默认推断仍为七位。

`DATE`、`INTERVAL`、`TIMESTAMP WITH LOCAL TIME ZONE` 的 NULL 判定不变。后续修订已开放该 extended profile 的非 NULL CLR `DateTime` / `DateTimeOffset` 以 Scale 8/9 无损写入；实际值仍为 100ns tick 精度，例如 `.1234567` 写为 `.123456700`。文本和旧 helper 的八九位写入仍关闭。列精度不足等绑定拒绝在确认未发送业务 Execute、绑定期间无网络往返且成功清理句柄后保留会话；取消、超时、清理失败或不确定状态仍使会话失效。详情见[时间精度](TP-temporal-precision.md)。

**首次修订实库记录（历史行为）。** 文本与 Prepare 路径、sync/async：INSERT 中 `TIMESTAMP(9)`、`TIMESTAMP(8)`、`TIMESTAMP(9) WITH TIME ZONE` 三列 NULL 写入成功，同语句六位值逐位读回；EF 式 `UPDATE … SET TS = :p`（NULL，Scale 9）成功。当时非 NULL Scale 9 仍拒绝；后续修订见本页末尾验证记录。

## 4. Broken 后的错误消息

连接因先前失败进入 Broken 后，后续命令或执行的 `InvalidOperationException` 消息说明连接因先前操作失败而失效、需要关闭重开，并提示查看先前异常的 `FailureInfo`。实库：`SELECT SLEEP(1) FROM DUAL`（服务器返回 `-4015`）后连接 Broken，下一条命令得到上述消息。

准备阶段现仅在已验证 `8.1.5.60` profile、完整严格错误回执、当前会话/事务所有权一致且失败句柄成功关闭时，允许 `-2007`、`-2106`、`-4015` 后继续使用会话。显式 Prepare 与参数化执行中的隐式 Prepare、同步与异步使用同一策略；其他错误仍保守处理。无参数的直接执行按执行阶段白名单判断，因此上面的直接 `SELECT SLEEP(1)` 示例仍是 Broken 消息验证。详见[错误恢复说明](server-error-recovery.md)。

## 5. 公开能力描述 `DmConnection.ServerCapabilities`

打开的连接提供只读快照 `DmServerCapabilities`。每个标志调用与对应操作相同的判定，可在发送前选择路径：

| 属性 | 含义 | 8.1.5.60 | 其他版本 |
| --- | --- | --- | --- |
| `ServerVersion` / `IsMeasuredProfile` | 登录时服务器自报版本；是否为本版本已实测 profile | `8.1.5.60` / true | 原值 / false |
| `SupportsSavepoints` | `DmTransaction.SupportsSavepoints` 及用户保存点 SQL | true | false |
| `SupportsReadUncommitted` / `SupportsSerializable` | BeginTransaction 接受的额外隔离级别 | true | false |
| `SupportsStatementErrorRecovery` | 白名单内语句级服务器错误后会话与事务继续可用 | true | false |
| `SupportsTableDdlInTransaction` | 事务内单条 TABLE DDL，隐式提交报告为 CompletedExternally | true | false |
| `MaxTemporalWriteScale` | 非 NULL CLR TIMESTAMP（含 TZ）参数最大小数位声明，值仍为 100ns；TIME 为 6 | 9（UTF-8 / message 21；默认推断仍 7） | 6 |
| `TemporalProfile` | 时间证据 profile 标识 | `TP01-v11-shared-physical` | 8.1.4.6 GB18030 为 `TP01-v11-tls-physical`，其余 `CompatibilityLegacyUnverified` |

连接未打开时读取该属性抛 `InvalidOperationException`。

**新 profile 的接纳流程。** 驱动不按版本前缀或范围推断能力。新增 profile 时，用专用 TEST 账号在目标服务器上运行相应探针并保留原始结果：错误恢复（[ErrorRecoveryProbe](../../tools/ErrorRecoveryProbe/Program.cs)）、事务与保存点（[TransactionProbe](../../tools/TransactionProbe/Program.cs)）、隔离级别（[IsolationProbe](../../tools/IsolationProbe)）、时间精度（[TemporalPrecisionProbe](../../tools/TemporalPrecisionProbe)）。各能力的证据满足既有判定后，再在对应判定中加入该 profile，并更新本页和兼容表。不同能力可以分别开放。进度见 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11)。

## 6. 其他

- **同义键冲突**：`ArgumentException` 消息给出规范键名，并提示先 `Remove("<key>")` 再赋值；消息不含任何值。严格冲突检查不变。
- **`DmCancelSource`**：`User` 为调用传入的 CancellationToken；`Command` 为执行中调用 `DmCommand.Cancel()`（或数据源命令的 Cancel）；`TotalDeadline` 为绝对期限到期，命令、Reader 和事务控制对应 `CommandTimeout`，Open 对应连接超时，清理对应 Cleanup Timeout；`IdleTimeout` 保留，本版本不会报告。
- **明文拒绝**：默认 `RequireTls` 遇到服务器未协商 TLS 时，消息说明可在可信网络中显式设置 `transport_security=PlaintextAllowed`（`DmConnectionStringBuilder.TransportSecurity`）。默认策略和证书校验不变。

## 验证

### 2026-10-15 后续修订

- 受测源码：`fix/temporal-write-and-prepare-recovery`，基线 `f6bde6e` 加本次 diff，尚未发布。独立代码审查发现同步 Prepare 缺少 FailureInfo、清理失败后复用信息未更新及异步回执线程检查问题，修正后复核无剩余具体发现。
- 命令：设置任务专用 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1` 后，以 `dotnet build tools/Issue16Probe/Issue16Probe.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers` 构建，执行 `scripts/with-dameng-test.sh dotnet tools/Issue16Probe/bin/net10.0/Issue16Probe.dll`。最终构建与实库退出码均为 0；先核实身份 `WDM_PROVIDER_TEST`，服务器 `8.1.5.60`，typed builder 显式 `PlaintextAllowed`。
- 高精度与本地拒绝：36/36。TIMESTAMP 与 TIMESTAMP WITH TIME ZONE 的 Scale 8/9、非零第七位、最小/最大日期、偏移；显式/隐式 Prepare、同步/异步、autocommit/事务提交/回滚。拒绝六位列的九位参数后，会话 ID 不变、原行无改变，同一命令修正参数可执行，提交/回滚的新连接读回符合预期。
- Prepare：36/36。`-2007`、`-2106`、`-4015`，同步/异步、显式/隐式、autocommit/事务提交/回滚；错误后同一会话及事务可用，修正同一命令成功，FailureInfo 正确报告服务器错误及可复用性。
- 同轮原有匿名块 8 项、typed NULL 4 项、传输 10 项及直接执行 Broken 消息检查均通过。清理本轮五张精确归属表，新连接确认本轮对象残留为 0。
- 首轮实库退出码为 1：36 个时间场景通过，Prepare 中同步显式 9 项因缺少 FailureInfo 失败；其会话/事务与最终数据行为均符合预期。保留原失败结果，修正后完整实库重测通过。
- 离线：十二套全部通过，2154 项，0 失败、0 跳过。对每个 `tests/W.DmProvider.*Tests/*.csproj` 串行执行 `dotnet test <项目> -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`；Pool 302、Type 958、Session 21、Transaction 135、Async 134、Command 16、Configuration 12、Diagnostics 84、Isolation 78、Lob 258、Security 41、Transport 115，退出码均 0。新增 Prepare 清理成功/失败合成回归覆盖同步、内部及异步入口的完整错误信息和 allocate/prepare/close 顺序。
- Type 首轮 4 项失败来自新增测试中 C# 条件表达式将 DateTime 隐式转换为 DateTimeOffset；只修正两处输入装箱，断言不变，重跑 958/958 通过。此前 Pool 及实库结果对应相同产品源码；原失败日志保留。
- 本机 ignored 证据保存在 `.local/temporal-prepare-fix/`，含构建日志、各套 TRX、`probe-real-first.json`、`probe-real-final.json` 及受测 diff；最终源码核对一致。构建为 0 错误、76 个警告。
- 本轮未运行官方/恢复版驱动、TLS 实库或下游 EF 套件，不将本次 shared 结果扩展到其他 profile 或已发布包。
- [PR #18](https://github.com/whynpc9/W.DamengProvider/pull/18) 首轮 CI 的十二套离线、打包、独立消费者与首次初始化检查通过。后续 Codex review 指出：内部 prepare 缓存持有的失败句柄关闭前还必须从缓存中逐出，否则相同 SQL 会再次取得已关闭句柄；该问题独立修订并补相关回归。公开 `PreparePooling=true` 仍明确拒绝，本次修订不开放该配置。修订按句柄身份移除 prepare 缓存、普通语句队列及活动列表中的引用，批量关闭先快照活动列表再关闭。新增同步/异步内部缓存回归通过，覆盖缓存复用、失败句柄别名逐出、健康项保留、相同 SQL 新建句柄及多句柄批量清理。独立审查复核通过；受影响 Pool/Session/Type/Async/Command 共 1433 项通过（Pool 304、Session 21、Type 958、Async 134、Command 16），无失败或跳过；实库探针重建、重跑退出码均 0，36 个时间及 36 个 Prepare 场景仍通过，五张本轮表清理后新连接残留为 0。证据文件使用 `.local/temporal-prepare-fix/` 下 cache-review 后缀，未覆盖先前记录。

### 首次修订历史验证

- 离线：十二套测试全部通过（2102 项）。新增或调整的测试覆盖匿名块判定的 6 个正例与 34 个拒绝形状（含引号过程名调用、引号及多重标签）、TLS 握手重置与证书拒绝的分类、typed NULL 绑定（advisory 降级、fixed 九位声明、非 NULL 仍拒绝）、能力快照与 gate 一致、传输失败包装与诊断格式、Broken 消息、同义键消息、明文提示。依赖原始 `IOException` 类型的 5 个既有测试改为断言 `DmException` 及其 `InnerException`。
- 实库：[tools/Issue16Probe](../../tools/Issue16Probe/Program.cs)，经 `scripts/with-dameng-test.sh` 加载 TEST，先确认身份为 `WDM_PROVIDER_TEST`；传输失败使用本机 TCP 中继在连接建立后发送 RST，以及无监听端口的拒绝连接。探针只输出布尔、数字、枚举名和生成的对象名；结束时删除本轮创建的三张表，新连接确认同名前缀对象为 0。
