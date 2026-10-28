# EF Core 接入带来的驱动改进

本文说明 [issue #16](https://github.com/whynpc9/W.DamengProvider/issues/16) 带来的行为变化。这些能力自 [0.2.0](../release-0.2.0.md) 起提供。

实库验证环境为 DM 8.1.5.60 / UTF-8 / 显式 `PlaintextAllowed`，账号为 `WDM_PROVIDER_TEST`；其他服务器环境保持保守行为。

## 1. 传输失败统一为 DmException

驱动自身 socket I/O（TCP 连接、发送、接收，包括 TLS 之上的流）产生的 `SocketException` / `IOException`，会在公开 API 边界包装为 `DmException`，原异常保留为 `InnerException`。

| 字段 | 值 |
| --- | --- |
| `ErrorKind` / `DriverErrorCode` | `Transport` / `WDM_TRANSPORT` |
| `Phase` | 实际失败阶段：`Connect`、`Send`、`Receive` 等 |
| `OperationOutcome` | 请求开始发送前为 `NotSent`；发送开始后为 `Unknown` |
| `ConnectionReusable` | 始终 `false`；连接进入 `Broken` 或打开失败 |
| `TransactionOutcome` | 本地事务 Active/Committing/RollingBack 时为 `OutcomeUnknown` |
| `Number` | 0，不伪造服务器错误号 |

覆盖范围包括：

- `Open` / `OpenAsync`；同步多端点路径保留原聚合消息，并将最后一个 socket 失败作为 `InnerException`。
- `ExecuteNonQuery` / `ExecuteReader` / `ExecuteScalar`（同步和异步）。
- Reader 读取和关闭、事务控制、更换已准备命令的 `Connection`。
- TLS 握手中的对端重置或 EOF；此时 `InnerException` 为保留原 I/O 原因的 `AuthenticationException`。

证书校验失败仍是认证失败，不归为 Transport。调用方传入的流式 LOB 输入抛出的 `IOException` 也不是传输失败，会按原异常抛出。

同步 LOB 消息路径和同步多端点连接的旧通信错误 `ECNET_COMMUNITION_ERROR` 会重新包装：保留原消息文本，`Number` 为 0，`InnerException` 为原 socket/I/O 异常。超时、取消和提交结果未知的异常类型不变；`DmDiagnostics.FormatException` 输出 `code=WDM_TRANSPORT`。

**迁移注意：** 依赖 `catch (SocketException)` / `catch (IOException)` 的代码，应改为检查 `DmException.ErrorKind == DmErrorKind.Transport` 或其 `InnerException`。

## 2. 事务内只含 DML 的匿名块

活动本地事务现在可以执行一个 `BEGIN … END;` 块，前提是块内每条语句以以下关键字之一开头，且 `END` 后没有标签或其他语句：

`INSERT`、`UPDATE`、`DELETE`、`MERGE`、`SELECT`、`NULL`。

驱动使用词法扫描判断语句边界：字符串、引号标识符、注释和括号内容不会误作语句头，SQL 原文不会改写。

以下情况仍在发送前抛 `NotSupportedException`，会话和事务保持 Active：

- `DECLARE` 段、嵌套块、`IF` / `LOOP` 等控制流、`EXCEPTION` 段。
- 过程调用（包括 `CALL` / `EXEC`）、`EXECUTE IMMEDIATE`。
- `COMMIT` / `ROLLBACK` / `SAVEPOINT` / `SET TRANSACTION`、DDL。
- `BEGIN` 或块内语句首词前有其他记号，包括 `<<label>>`。
- `END` 带标签，或块后还有其他语句。
- 只由引号标识符或标点组成的语句，例如 `"P"(1)`。

需要其他 PL/SQL 结构时，可在事务外（autocommit）执行，或拆成多条普通 DML 放在同一事务中；EF 迁移可对该 `Sql()` 使用 `suppressTransaction: true`。块里调用的函数或触发器仍由服务器执行，驱动不分析其内部。

**实库结果（8.1.5.60，同步/异步）：** 回滚后新连接看不到修改，提交后修改可见。第二条 INSERT 违反主键 `-6602` 时，块内第一条 INSERT 也不可见，块前的同事务修改保留；符合白名单的会话仍可用。四类控制块均在发送前拒绝，事务保持 Active。

## 3. 8/9 位时间列的 NULL 与 CLR 值

时间参数的 `Scale` 为 8 或 9（或超过当前环境七位上限）时，`null` / `DBNull` 不再一律拒绝：

- 有可靠固定列描述（例如 Prepare 后的 `TIMESTAMP(9)` 列）时，沿用服务器描述。此放宽只在已验证七位环境（8.1.5.60 / UTF-8）开放。
- 没有固定列描述时，声明使用当前环境支持的小数位：已验证扩展环境最高 9，其他环境为兼容上限 6；默认推断仍为七位。
- `DATE`、`INTERVAL`、`TIMESTAMP WITH LOCAL TIME ZONE` 的 NULL 判定不变。

该扩展环境也支持非 NULL CLR `DateTime` / `DateTimeOffset` 以 `Scale=8/9` 无损写入。实际值仍为 100ns tick 精度，例如 `.1234567` 写为 `.123456700`。文本和旧辅助类型的八九位写入仍关闭。

列精度不足等绑定拒绝，只有确认未发送业务 Execute、绑定期间没有网络往返并成功清理句柄时才保留会话；取消、超时、清理失败或状态不确定仍使会话失效。详见[时间精度说明](TP-temporal-precision.md)。

## 4. 连接 Broken 后的错误消息

连接因先前失败进入 `Broken` 后，后续命令或执行的 `InvalidOperationException` 会说明连接已因先前操作失效，需要关闭重开，并提示查看先前异常的 `FailureInfo`。

Prepare 阶段只在已验证 `8.1.5.60` 环境、完整严格错误回执、当前会话和事务归属一致且失败句柄成功关闭时，允许 `-2007`、`-2106`、`-4015` 后继续使用会话。显式 Prepare 与参数化执行中的隐式 Prepare、同步和异步使用同一策略；其他错误仍保守处理。

无参数直接执行按执行阶段白名单判断，因此直接 `SELECT SLEEP(1)` 返回 `-4015` 后仍会进入 `Broken`。详见[错误恢复说明](server-error-recovery.md)。

## 5. DmConnection.ServerCapabilities

打开的连接提供只读快照 `DmServerCapabilities`。每个标志与对应操作使用相同判定，可在发送前选择路径；连接未打开时读取该属性会抛 `InvalidOperationException`。

| 属性 | 含义 | 8.1.5.60 | 其他版本 |
| --- | --- | --- | --- |
| `ServerVersion` / `IsMeasuredProfile` | 登录时服务器自报版本；是否为已实测环境 | `8.1.5.60` / true | 原值 / false |
| `SupportsSavepoints` | `DmTransaction.SupportsSavepoints` 及用户保存点 SQL | true | false |
| `SupportsReadUncommitted` / `SupportsSerializable` | BeginTransaction 接受的额外隔离级别 | true | false |
| `SupportsStatementErrorRecovery` | 白名单内语句级错误后会话和事务继续可用 | true | false |
| `SupportsTableDdlInTransaction` | 事务内单条 TABLE DDL，隐式提交报告为 CompletedExternally | true | false |
| `MaxTemporalWriteScale` | 非 NULL CLR TIMESTAMP（含 TZ）参数最大小数位声明；值仍为 100ns，TIME 为 6 | 9（UTF-8 / message 21；默认推断仍 7） | 6 |
| `TemporalProfile` | 时间验证环境标识 | `TP01-v11-shared-physical` | 8.1.4.6 GB18030 为 `TP01-v11-tls-physical`，其余 `CompatibilityLegacyUnverified` |

### 验证新服务器环境

驱动不会按版本前缀或范围推断能力。新增环境时，应使用专用 TEST 账号运行相应探针并保留原始结果：

- [ErrorRecoveryProbe](../../tools/ErrorRecoveryProbe/Program.cs)：错误恢复。
- [TransactionProbe](../../tools/TransactionProbe/Program.cs)：事务与保存点。
- [IsolationProbe](../../tools/IsolationProbe)：隔离级别。
- [TemporalPrecisionProbe](../../tools/TemporalPrecisionProbe)：时间精度。

各项能力可以分别开放。证据满足既有判定后，再更新产品判定和兼容文档。进度见 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11)。

## 6. 其他行为变化

- **同义键冲突**：`ArgumentException` 消息给出规范键名，并提示先 `Remove("<key>")` 再赋值；消息不包含任何配置值。
- **`DmCancelSource`**：`User` 表示调用方令牌，`Command` 表示执行中调用 `Cancel()`，`TotalDeadline` 表示绝对期限到期。Open 使用连接超时，命令、Reader 和事务控制使用 `CommandTimeout`，清理使用 `CleanupTimeout`。`IdleTimeout` 保留但本版本不报告。
- **明文拒绝提示**：默认 `RequireTls` 遇到未协商 TLS 的服务器时，消息会说明可在可信网络中显式设置 `transport_security=PlaintextAllowed`。默认策略和证书校验不变。

## 历史验证记录

以下结果属于对应提交，不替代当前发布记录。

### 2026-10-15 后续修订

受测分支为 `fix/temporal-write-and-prepare-recovery`，基线 `f6bde6e` 加当时修改。构建和实库最终退出码均为 0，服务器为 8.1.5.60，先核实 TEST 身份并显式使用 `PlaintextAllowed`。

- 高精度与本地拒绝 **36/36**：TIMESTAMP 和 TIMESTAMP WITH TIME ZONE 的 Scale 8/9、第七位、日期边界、偏移，以及显式/隐式 Prepare、同步/异步、autocommit/事务提交回滚。
- Prepare **36/36**：`-2007`、`-2106`、`-4015` 的各类组合；错误后同一会话和事务可用，修正同一命令成功，`FailureInfo` 正确。
- 同轮匿名块 8 项、时间 NULL 4 项、传输 10 项和直接执行 Broken 消息检查通过。5 张本轮表均精确清理，新连接确认无残留。
- 首轮实库保留退出码 1：36 个时间场景通过，同步显式 Prepare 的 9 项因缺少 `FailureInfo` 失败；修复后完整重测通过。
- 离线十二套共 **2154 项**通过，0 失败、0 跳过。Pool 302、Type 958、Session 21、Transaction 135、Async 134、Command 16、Configuration 12、Diagnostics 84、Isolation 78、Lob 258、Security 41、Transport 115。
- Type 首轮 4 项失败来自测试中的 C# 条件表达式把 `DateTime` 隐式转换为 `DateTimeOffset`；只修正输入装箱，断言不变，重跑 958/958 通过。
- 本机证据位于忽略目录 `.local/temporal-prepare-fix/`，包括构建日志、TRX、首轮和最终实库报告及受测差异。构建为 0 错误、76 个警告。
- 本轮未运行官方/恢复版驱动、TLS 实库或下游 EF 套件，结果不扩展到其他服务器环境。

[PR #18](https://github.com/whynpc9/W.DamengProvider/pull/18) 首轮 CI 通过后，审查又发现失败句柄关闭前必须从内部 Prepare 缓存逐出，否则相同 SQL 会再次取得已关闭句柄。修复按句柄身份移除缓存、普通语句队列和活动列表引用，批量关闭先快照活动列表。

新增同步/异步回归覆盖缓存复用、失败句柄逐出、健康项保留、相同 SQL 新建句柄和多句柄清理。受影响 Pool/Session/Type/Async/Command 共 **1433 项**通过（304、21、958、134、16）；实库 36 个时间和 36 个 Prepare 场景仍通过，5 张表无残留。公开 `PreparePooling=true` 仍明确拒绝，本次修订未开放该配置。

### 首次修订

- 离线十二套共 **2102 项**通过。覆盖匿名块正例和拒绝形状、TLS 失败分类、时间 NULL、能力快照、传输包装、Broken 消息、同义键和明文提示。
- 依赖原始 `IOException` 类型的 5 个既有测试改为断言 `DmException` 及其 `InnerException`。
- 实库使用 [Issue16Probe](../../tools/Issue16Probe/Program.cs)，经 TEST wrapper 加载配置。传输失败通过本机 TCP 中继发送 RST和无监听端口模拟；探针只输出布尔、数字、枚举名和生成对象名。结束时删除本轮 3 张表，新连接确认无残留。
