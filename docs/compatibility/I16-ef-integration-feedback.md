# EF Core 接入反馈的驱动修订（issue #16）

本页记录 [issue #16](https://github.com/whynpc9/W.DamengProvider/issues/16) 的处理结果。修订在 `0.1.0-preview.1` 之后，尚未随已发布包提供。实库证据只覆盖 shared DM 8.1.5.60 / UTF-8 / 显式 `PlaintextAllowed` 的 `WDM_PROVIDER_TEST`；其他 profile 维持原保守行为。

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

覆盖 `Open`/`OpenAsync`（同步多端点路径保留原聚合消息，并把最后一个 socket 失败作为 `InnerException`）、`ExecuteNonQuery`/`ExecuteReader`（同步与异步）、Reader 读取与关闭、事务控制。调用方传入的流式 LOB 输入抛出的 `IOException` 不是传输失败，仍按原异常抛出。超时、取消、提交结果未知的既有类型不变。`DmDiagnostics.FormatException` 对这类异常输出 `code=WDM_TRANSPORT`。

**行为变化。** 依赖 `catch (SocketException)`/`catch (IOException)` 的调用方应改为检查 `DmException.ErrorKind == DmErrorKind.Transport` 或 `InnerException`。

## 2. 事务内只含 DML 的匿名块

Active 本地事务中，以下形状不再被发送前拒绝：一个 `BEGIN … END;` 块，块内每条语句以 `INSERT`、`UPDATE`、`DELETE`、`MERGE`、`SELECT` 或 `NULL` 开头，`END` 后无标签、无其他语句。判定使用既有词法扫描，忽略字符串、引号标识符、注释和括号内的内容；SQL 原文不改写。

仍在发送前拒绝（`NotSupportedException`，会话和事务保持 Active）：`DECLARE` 段、嵌套块、`IF`/`LOOP` 等控制流、`EXCEPTION` 处理段、过程调用（含 `CALL`/`EXEC`）、`EXECUTE IMMEDIATE`、`COMMIT`/`ROLLBACK`/`SAVEPOINT`/`SET TRANSACTION`、DDL、`END` 带标签、块后接其他语句。块外的普通语句判定不变。

需要其他 PL/SQL 结构时，可把块放在事务外（autocommit）执行，或拆成多条普通 DML 命令放在同一事务；EF 迁移中可对该 `Sql()` 使用 `suppressTransaction: true`。块里调用的函数或触发器与单条 DML 一样由服务器执行，驱动不分析其内部。

**实库（8.1.5.60，sync/async）。** 事务内块执行后回滚：新连接看不到修改；提交：修改与插入可见。块中第二条 INSERT 违反主键（`-6602`）时，块的第一条 INSERT 在事务内和新连接中都不可见，块前的同事务修改保留，会话按既有白名单保持可用；回滚后新连接无任何效果。四类控制块均在发送前拒绝且事务仍 Active。

## 3. 8/9 位时间列的 typed NULL

`Scale` 为 8 或 9（或超过当前 profile 七位上限）的时间参数，值为 `null`/`DBNull` 时不再拒绝：

- 有可靠 fixed describe（例如 Prepare 后的 `TIMESTAMP(9)` 列）时，沿用服务器描述的类型与小数位声明；
- 无 fixed describe 时，声明降为当前 profile 已验证的最宽小数位（已验证七位 profile 为 extended 7，其他 profile 为 legacy 6）。

`DATE`、`INTERVAL`、`TIMESTAMP WITH LOCAL TIME ZONE` 的 NULL 判定不变。**非 NULL** 值在 8/9 位仍拒绝，包括能被 100ns 整除的值；这需要新的 profile 证据，见 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11)。该拒绝发生在服务器描述参数之后，当前会使会话 Broken（与原行为相同）。

**实库。** 文本与 Prepare 路径、sync/async：INSERT 中 `TIMESTAMP(9)`、`TIMESTAMP(8)`、`TIMESTAMP(9) WITH TIME ZONE` 三列 NULL 写入成功，同语句六位值逐位读回；EF 式 `UPDATE … SET TS = :p`（NULL，Scale 9）成功；Scale 9 的非 NULL 值被拒绝，新连接确认数据未变。

## 4. Broken 后的错误消息

连接因先前失败进入 Broken 后，后续命令或执行的 `InvalidOperationException` 消息说明连接因先前操作失败而失效、需要关闭重开，并提示查看先前异常的 `FailureInfo`。实库：`SELECT SLEEP(1) FROM DUAL`（服务器返回 `-4015`）后连接 Broken，下一条命令得到上述消息。

准备阶段失败仍按保守规则使会话 Broken（issue 建议 (b)）。纳入恢复范围需要先对 prepare 回执做与执行阶段相同的会话同步实测，本次未改变。

## 5. 公开能力描述 `DmConnection.ServerCapabilities`

打开的连接提供只读快照 `DmServerCapabilities`。每个标志调用与对应操作相同的判定，可在发送前选择路径：

| 属性 | 含义 | 8.1.5.60 | 其他版本 |
| --- | --- | --- | --- |
| `ServerVersion` / `IsMeasuredProfile` | 登录时服务器自报版本；是否为本版本已实测 profile | `8.1.5.60` / true | 原值 / false |
| `SupportsSavepoints` | `DmTransaction.SupportsSavepoints` 及用户保存点 SQL | true | false |
| `SupportsReadUncommitted` / `SupportsSerializable` | BeginTransaction 接受的额外隔离级别 | true | false |
| `SupportsStatementErrorRecovery` | 白名单内语句级服务器错误后会话与事务继续可用 | true | false |
| `SupportsTableDdlInTransaction` | 事务内单条 TABLE DDL，隐式提交报告为 CompletedExternally | true | false |
| `MaxTemporalWriteScale` | 非 NULL 的 TIMESTAMP（含 TZ）参数最大小数位；TIME 为 6 | 7（UTF-8 / message 21） | 6 |
| `TemporalProfile` | 时间证据 profile 标识 | `TP01-v11-shared-physical` | 8.1.4.6 GB18030 为 `TP01-v11-tls-physical`，其余 `CompatibilityLegacyUnverified` |

连接未打开时读取该属性抛 `InvalidOperationException`。

**新 profile 的接纳流程。** 驱动不按版本前缀或范围推断能力。新增 profile 时，用专用 TEST 账号在目标服务器上运行相应探针并保留原始结果：错误恢复（[ErrorRecoveryProbe](../../tools/ErrorRecoveryProbe/Program.cs)）、事务与保存点（[TransactionProbe](../../tools/TransactionProbe/Program.cs)）、隔离级别（[IsolationProbe](../../tools/IsolationProbe)）、时间精度（[TemporalPrecisionProbe](../../tools/TemporalPrecisionProbe)）。各能力的证据满足既有判定后，再在对应判定中加入该 profile，并更新本页和兼容表。不同能力可以分别开放。进度见 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11)。

## 6. 其他

- **同义键冲突**：`ArgumentException` 消息给出规范键名，并提示先 `Remove("<key>")` 再赋值；消息不含任何值。严格冲突检查不变。
- **`DmCancelSource`**：`User` 为调用传入的 CancellationToken；`Command` 为执行中调用 `DmCommand.Cancel()`（或数据源命令的 Cancel）；`TotalDeadline` 为绝对期限到期，命令、Reader 和事务控制对应 `CommandTimeout`，Open 对应连接超时，清理对应 Cleanup Timeout；`IdleTimeout` 保留，本版本不会报告。
- **明文拒绝**：默认 `RequireTls` 遇到服务器未协商 TLS 时，消息说明可在可信网络中显式设置 `transport_security=PlaintextAllowed`（`DmConnectionStringBuilder.TransportSecurity`）。默认策略和证书校验不变。

## 验证

- 离线：十二套测试全部通过。新增或调整的测试覆盖匿名块判定的正例与 19 个拒绝形状、typed NULL 绑定（advisory 降级、fixed 九位声明、非 NULL 仍拒绝）、能力快照与 gate 一致、传输失败包装与诊断格式、Broken 消息、同义键消息、明文提示。依赖原始 `IOException` 类型的 5 个既有测试改为断言 `DmException` 及其 `InnerException`。
- 实库：[tools/Issue16Probe](../../tools/Issue16Probe/Program.cs)，经 `scripts/with-dameng-test.sh` 加载 TEST，先确认身份为 `WDM_PROVIDER_TEST`；传输失败使用本机 TCP 中继在连接建立后发送 RST，以及无监听端口的拒绝连接。探针只输出布尔、数字、枚举名和生成的对象名；结束时删除本轮创建的三张表，新连接确认同名前缀对象为 0。
