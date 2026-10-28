# 数组绑定与 DbBatch

数组绑定和 `DbBatch` 自 [0.2.0](../release-0.2.0.md) 起提供。实库验证环境为 DM 8.1.5.60 / UTF-8 / 显式 `PlaintextAllowed`，测试账号为 `WDM_PROVIDER_TEST`。

## 两种批量方式的区别

- **数组绑定**：一次 EXECUTE 请求发送同一条 DML 的多行参数。参数描述只发送一次，随后发送 N 行值，可以显著减少网络往返。
- **DbBatch**：按顺序逐条执行多个命令，往返次数与逐条执行相同。它提供标准批处理 API、统一生命周期和错误归属，但不是服务端的原生多 SQL 批次。

数组绑定不使用 FLDR/BulkCopy。连接选项只接受 `BatchContinueOnError=false`、`BatchAllowMaxErrors=0`，服务器在首个失败行停止。实测失败具有语句级原子性：任一行失败时，本次数组中的所有行都不生效。

响应只返回整条语句的总影响行数，没有逐行计数，错误回执也不指出失败行号。

## 数组绑定：`DmCommand.ArrayBindCount`

设置 `ArrayBindCount = N`，每个参数的 `Value` 使用长度 N 的一维数组，例如 `int[]`、`string[]`、`object[]`、`byte[][]`，然后调用 `ExecuteNonQuery` 或 `ExecuteNonQueryAsync`。返回值为服务器报告的总影响行数。

| 方面 | 行为 |
| --- | --- |
| 适用语句 | 仅 INSERT、UPDATE、DELETE、MERGE，以服务器 Prepare 返回的语句类型为准。SELECT、匿名块和过程调用在发送前拒绝。 |
| 适用调用 | `ExecuteNonQuery`（同步/异步）和 `Prepare`。`ExecuteReader`、`ExecuteScalar` 在使用连接前拒绝。 |
| 数组形状 | 每个参数必须是一维数组且长度等于 `ArrayBindCount`。设置后，`byte[]` 也按行解释；二进制列应传 `byte[][]`。 |
| 无效设置 | 数组长度不一致、`ArrayBindCount < 0`、无参数均在本地拒绝。`ArrayBindCount = 0` 表示普通执行，此时数组值仍按普通参数规则拒绝；空批次不可表示。 |
| 行值快照 | 执行开始时复制逐行值，之后修改源数组不影响本次执行；`byte[]` 行值也会复制。 |
| 不支持的行值 | Stream/TextReader、已有 LOB 对象和未明确支持的对象类型会拒绝。 |
| 类型推断 | 显式 `DbType` / `DmSqlType` 作用于所有行。否则按非 NULL 行推断，所有非 NULL 行必须映射到同一 CLR 类型；NULL 行沿用该类型。 |
| 全 NULL | 全部为 NULL 且服务器描述不可靠时，按普通参数规则拒绝。 |
| 描述一致性 | 固定列描述按目标列编码。只有建议性描述时，如果各行小数位或长度不同，或首行 NULL 导致后续类型不一致，会在发送前拒绝；可显式设置 `Precision` / `Scale` / `Size`。 |
| LOB | CLOB/BLOB 必须能放在行内，即小于协商的 `lobOffRowLen`；更大的值在发送前拒绝，应逐条执行。 |

### 失败、事务与取消

服务器错误（例如 `-6602` 主键冲突、`-6169` 值过长）发生时，本次数组没有任何行生效：

- autocommit 下不提交。
- 事务内之前的修改保留，事务保持 Active，由调用方提交或回滚。
- 符合既有白名单的会话保持可用。
- 驱动不自动重试。

超时或取消与普通命令相同：连接进入 `Broken`，`OperationOutcome=Unknown`。实测服务器可能在客户端放弃后完成整个数组，也可能不生效，但不会只生效一部分；不要重放请求。

**行为变化：** MERGE 的 `RecordsAffected` 使用服务器报告的影响行数，普通执行和数组执行一致；旧实现的单条 MERGE 返回 -1。

### 实库覆盖

同步和异步测试覆盖 5 行 INSERT，包括 NULL、DECIMAL(18,4)、中文 VARCHAR、空串、TIMESTAMP(6) 七位小数、空/16 字节 VARBINARY 和行内 CLOB，并逐值读回一致。

另覆盖：

- UPDATE 3 行，其中 1 行无匹配，返回 2。
- DELETE 范围，一行删 2 行、一行删 0 行，返回 2。
- MERGE 数组返回 2，单条 MERGE 返回 1。
- 主键冲突、事务内值过长、提交和回滚后的最终数据状态。
- 锁等待时的超时和取消，最终状态为全部或全无。
- 14 类本地拒绝场景，均不产生数据，连接保持 Open。

## DbBatch

`DmConnection.CanCreateBatch` 和 `DmClientFactory.CanCreateBatch` 为 `true`。提供 `DmBatch`、`DmBatchCommand`、`DmBatchCommandCollection`，以及连接、工厂和数据源的对应创建方法。

| 方面 | 行为 |
| --- | --- |
| 执行方式 | 按集合顺序逐条执行，每条命令遵循普通 `DmCommand` 的参数快照、绑定、错误恢复和清理规则；不减少往返次数。 |
| 执行前检查 | 在第一条执行前检查空批次、无连接、非 Text 命令、输出参数、标记与参数不匹配、事务内事务控制语句等。 |
| 连接占用 | 整个批次及 Reader 存续期间独占连接；其他命令和 Commit/Rollback 会拒绝。应先关闭批次 Reader，再释放事务。 |
| 执行期间修改 | 批次、子命令、命令集合和参数均不可修改。 |
| 超时 | `Timeout` 默认取连接的 `CommandTimeout`，0 表示不限时。`ExecuteNonQuery` 的整个批次共用一份期限；Reader 的每次读取或前进使用新的期限。 |
| 失败 | 在首个失败命令停止，后续命令不执行。`DbException.BatchCommand` 指向失败命令；取消异常不带该属性。 |
| 事务 | 没有隐式事务。autocommit 下已完成的命令已提交；需要原子性时显式设置 `Transaction`，失败后事务保持 Active。 |
| 计数 | 每个子命令完成后设置 `RecordsAffected`；查询、未完成或未执行的命令为 -1。总计数为已完成命令计数之和，没有计数时为 -1。 |
| Reader | 按命令顺序提供结果集，无结果集的命令不产生结果集。下一条命令在前一条结果耗尽后才执行。 |
| 提前关闭 Reader | 仍执行剩余命令，与 SqlClient/Npgsql 一致；执行失败时 `Close` 抛出该异常。 |
| Reader 出错后 | `Read` / `NextResult` 抛 `InvalidOperationException`，关闭时不再执行剩余命令；取消或连接失效后关闭也不再执行，且不额外抛出。 |
| 其他 | `ExecuteScalar` 返回第一个结果集首行首列，并执行其余命令。`Prepare` 只做本地预检，语句在执行时准备。支持 `SequentialAccess`、`CloseConnection`，以及数据源批次的连接自动租用和归还；先释放批次不影响之后释放 Reader。 |

### 实库覆盖

6 条命令（INSERT、UPDATE、SELECT、DELETE、COUNT、空 SELECT）的 Reader 依次返回 3 个结果集，逐命令计数为 `1,2,-1,1,-1,-1`，总计 4。

另验证：

- 第二条 INSERT 主键冲突时，`BatchCommand` 指向第 2 条，计数为 `1,-1,-1`；autocommit 和显式事务行为符合上表。
- Reader 路径报告相同失败归属；提前关闭后剩余 INSERT 已执行。
- Reader 打开时取消，关闭不再抛出，连接进入 `Broken`，后续 INSERT 未执行。
- 连续 50 个批次后连接正常。
- 锁等待超时和取消均归属第 2 条，第 1 条已提交，第 3 条未执行。
- 可通过 `DbConnection`、`DbProviderFactory` 基类型和 `DmDataSource.CreateBatch()` 执行。

## 性能对照

远程 TEST 实例，同一条 4 列 INSERT（INT、DECIMAL(18,4)、VARCHAR、TIMESTAMP(6)）：

| 行数 | 数组绑定 | 复用同一 Command 逐行执行 |
| --- | --- | --- |
| 1000 | 56 ms，5 次发送，分配 2.2 MB | 11.9 s，5000 次发送，分配 147 MB |
| 5000 | 133 ms，5 次发送，分配 11 MB | 56.7 s，25000 次发送，分配 734 MB |

两表内容逐行相同。3 条命令的 DbBatch 与逐条执行同为 15 次发送。数值取决于网络延迟，只用于说明往返数量级。

## 复现入口与验证记录

实库探针为 `tools/BatchProbe`：

```sh
scripts/with-dameng-test.sh dotnet tools/BatchProbe/bin/net10.0/BatchProbe.dll [array|batch|perf]
```

每轮先核对 `WDM_PROVIDER_TEST` 身份，使用带随机后缀的对象名，结束时逐个删除，并由新连接确认无残留。`array`、`batch`、`perf` 各段最终运行退出码均为 0。

本地包 `0.1.0-batch.local` 的独立 PackageReference 消费验证通过；离线 12 个测试项目全部通过。PR #19 的释放顺序问题修复后，PoolTests 308/308、CommandTests 31/31 通过，相关 9 个实库对象也全部清理。

已知环境现象：远程实例上长时间逐行循环偶发 `SocketException: ConnectionReset`（`WDM_TRANSPORT`，Receive），未修改的基线同样复现。性能对照遇到该问题时，只在自己的表上清空后重跑并记录，不扩大为产品结论。
