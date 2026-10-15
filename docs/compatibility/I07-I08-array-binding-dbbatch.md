# 数组绑定与 DbBatch（issue #7、#8）

本页记录 [issue #7](https://github.com/whynpc9/W.DamengProvider/issues/7) 与 [issue #8](https://github.com/whynpc9/W.DamengProvider/issues/8) 的实现。改动在 `0.1.0-preview.1` 之后，尚未随已发布包提供。实库证据只覆盖 shared DM 8.1.5.60 / UTF-8 / 显式 `PlaintextAllowed` 的 `WDM_PROVIDER_TEST`。

## 1. 协议表征

- 达梦 EXECUTE 请求本身携带行数和逐行参数值：参数描述（类型、精度、小数位）每个参数只发送一次，随后是 N 行值。驱动沿用这一请求，不使用 FLDR/BulkCopy。连接选项 `BatchContinueOnError=false`、`BatchAllowMaxErrors=0` 是唯一允许值（非默认值在连接串解析时拒绝），因此服务器在首个失败行即停止。
- 响应只返回整条语句的总影响行数，没有逐行计数；错误回执不指出失败行号。实测失败为**语句级原子**：任一行失败时本次数组中的所有行都不生效。
- 协议没有"多条不同 SQL 一次提交"的请求。DbBatch 因此逐条执行，往返次数与逐条执行相同（见第 3 节）。

## 2. 数组绑定（`DmCommand.ArrayBindCount`）

用法：设置 `ArrayBindCount = N`，每个参数的 `Value` 为长度 N 的一维数组（`int[]`、`string[]`、`object[]`、`byte[][]` 等），然后调用 `ExecuteNonQuery`/`ExecuteNonQueryAsync`。返回值为服务器报告的总影响行数。

| 规则 | 行为 |
| --- | --- |
| 适用语句 | 仅 INSERT、UPDATE、DELETE、MERGE（以服务器 Prepare 返回的语句类型判定）。SELECT、匿名块、过程调用在发送 EXECUTE 前拒绝（`NotSupportedException`），会话保持可用 |
| 适用调用 | `ExecuteNonQuery`（同步/异步）和 `Prepare`。`ExecuteReader`、`ExecuteScalar` 在使用连接前拒绝 |
| 数组形状 | 每个参数必须是一维数组且长度等于 `ArrayBindCount`（设置后 `byte[]` 也按行解释，二进制列应传 `byte[][]`）；否则、`ArrayBindCount < 0`、无参数均在本地拒绝（`InvalidOperationException`）。`ArrayBindCount = 0` 表示普通执行，此时数组值仍按原规则拒绝；空批次不可表示 |
| 行值 | 执行开始时逐行快照，之后修改源数组不影响本次执行。`byte[]` 行值复制。Stream/TextReader、已有 LOB 对象及未明确支持的对象类型拒绝 |
| 类型 | 显式 `DbType`/`DmSqlType` 作用于所有行。否则按非 NULL 行推断，所有非 NULL 行必须映射到同一 CLR 类型（如 `object[] { 1, "2" }` 拒绝）；NULL 行沿用该类型。全部为 NULL 且服务器描述不可靠时按普通参数规则拒绝 |
| 描述一致性 | 服务器给出固定列描述（如 INSERT 目标列）时，每行按列类型编码。只有建议性描述（如 `? + 0` 表达式）时，若各行导出的小数位或长度不同（例如 1.5 与 1.25），或首行为 NULL 而后续行与服务器描述不一致（常见于字符串、二进制），在发送前拒绝；设置 `Precision`/`Scale`/`Size` 后可执行 |
| LOB | CLOB/BLOB 值须能在行内发送（小于协商的 `lobOffRowLen`）；更大的值在发送 EXECUTE 前拒绝，应逐条执行 |
| 失败 | 服务器错误（如 `-6602` 主键冲突、`-6169` 值过长）：本次数组没有任何行生效；autocommit 下无提交，事务内之前的修改保留且事务保持 Active，由调用方提交或回滚；会话按既有白名单保持可用。驱动不自动重试 |
| 超时/取消 | 与普通命令相同：连接进入 Broken，`OperationOutcome=Unknown`。实测服务器可能在客户端放弃后仍完成整个数组（autocommit 下整批生效），也可能不生效，但不会只生效一部分。不重放 |

**行为变化。** MERGE 的 `RecordsAffected` 改为服务器报告的行数（普通执行与数组执行一致）；此前单条 MERGE 返回 -1。

**实库（sync/async）。** 5 行 INSERT 覆盖 NULL、DECIMAL(18,4)、中文 VARCHAR、空串、TIMESTAMP(6) 七位小数、空/16 字节 VARBINARY、行内 CLOB，新连接逐值读回一致。UPDATE（3 行，其中 1 行无匹配）返回 2；DELETE 范围（一行删 2 行、一行删 0 行）返回 2；MERGE 数组返回 2；单条 MERGE 返回 1。主键冲突在第 3 行时新连接只看到预置行；事务内值过长后事务 Active，提交后只保留之前的修改；回滚后无效果。数组锁等待的超时与取消：执行期间新连接看不到任何修改，最终状态为全部或全无。上述 14 类拒绝场景均不产生数据，连接保持 Open。

## 3. DbBatch

`DmConnection.CanCreateBatch`、`DmClientFactory.CanCreateBatch` 为 true；提供 `DmBatch`、`DmBatchCommand`、`DmBatchCommandCollection`，以及 `DmConnection.CreateBatch()`、工厂 `CreateBatch()`/`CreateBatchCommand()`、`DmDataSource.CreateBatch()`。

| 方面 | 行为 |
| --- | --- |
| 执行方式 | 按集合顺序逐条执行；每条命令就是一次普通 `DmCommand` 执行（相同的参数快照、绑定、错误恢复和语句清理）。**不减少往返次数**，也不是原生批次 |
| 预检 | 执行前对所有命令捕获计划：空批次、无连接、非 Text 命令、输出参数、标记与参数不匹配、事务内的事务控制语句等在第一条执行前拒绝 |
| 连接所有权 | 整个批次（含 Reader 存续期间，包括命令之间）独占连接；其他命令和 Commit/Rollback 在此期间被拒绝，此时 Dispose 事务会中止物理连接，应先关闭批次 Reader。批次与 `DmBatchCommand` 的属性、命令集合和参数在执行期间不可修改 |
| 超时 | `Timeout` 默认取连接的 CommandTimeout，0 为不限。`ExecuteNonQuery` 的整个批次共用一个期限；Reader 与普通 Reader 一样，每次读取/前进获得新的期限 |
| 失败 | 在第一个失败命令处停止，后续命令不执行。`DbException.BatchCommand` 指向失败命令（取消异常为 `OperationCanceledException`，不带该属性）。无隐式事务：autocommit 下已完成的命令已提交；需要原子性时设置 `Transaction`，失败后事务保持 Active |
| 计数 | 每个 `DmBatchCommand.RecordsAffected` 在该命令完成后设置；查询、未完成或未执行的命令为 -1。`ExecuteNonQuery` 与 Reader 的 `RecordsAffected` 为已完成命令计数之和，没有计数时为 -1 |
| Reader | 按命令顺序给出结果集，没有结果集的命令不产生结果集。下一条命令在前一条结果耗尽时才执行。关闭 Reader 时仍执行剩余命令（与 SqlClient/Npgsql 一致），执行失败时 `Close` 抛出该异常。出错后 `Read`/`NextResult` 抛出 `InvalidOperationException`，关闭时不再执行；取消或连接失效后关闭也不再执行且不抛出。支持 `SequentialAccess`、`CloseConnection` |
| 取消 | `DmBatch.Cancel()` 或异步 token 与 `DmCommand.Cancel` 相同：连接 Broken，进行中命令结果未知，不重放。落在两条命令之间的取消使下一条命令以取消失败、不发送 |
| 其他 | `ExecuteScalar` 返回第一个结果集首行首列，并执行其余命令。`Prepare` 只做本地预检，语句在执行时准备。数据源批次每次执行打开并归还一个连接；有 Reader 时由 Reader 结束归还，先释放批次不影响之后释放 Reader，同步与异步均适用 |

**实库（sync/async）。** 6 条命令（INSERT、UPDATE、SELECT、DELETE、COUNT、空 SELECT）的 Reader 依次给出 3 个结果集，逐命令计数为 `1,2,-1,1,-1,-1`，总计 4。第二条 INSERT 主键冲突时：`BatchCommand` 为第 2 条，计数 `1,-1,-1`，新连接只见第一条的行，连接可继续使用；事务内同样停止，事务 Active，回滚后无效果；Reader 路径在前进时报告同一归属。提前关闭 Reader 后剩余 INSERT 已执行；Reader 打开时调用 `Cancel()` 后关闭不抛出，连接 Broken，后续 INSERT 未执行；连续 50 个批次后连接正常。锁等待超时：`DmTimeoutException` 归属第 2 条，第 1 条已提交、第 3 条未执行；取消同样，第 2 条最终全部或全无。通过 `DbConnection`/`DbProviderFactory` 基类型和 `DmDataSource.CreateBatch()` 执行成功。

## 4. 性能对照

远程 TEST 实例，同一 4 列 INSERT（INT、DECIMAL(18,4)、VARCHAR、TIMESTAMP(6)）：

| 行数 | 数组绑定 | 复用同一 Command 逐行执行 |
| --- | --- | --- |
| 1000 | 56 ms，5 次发送，分配 2.2 MB | 11.9 s，5000 次发送，分配 147 MB |
| 5000 | 133 ms，5 次发送，分配 11 MB | 56.7 s，25000 次发送，分配 734 MB |

两表内容逐行相同。3 条命令的 DbBatch 与逐条执行同为 15 次发送。数值取决于网络延迟，只用于说明往返数量级。

## 5. 验证记录

- 实库探针 `tools/BatchProbe`：`scripts/with-dameng-test.sh dotnet tools/BatchProbe/bin/net10.0/BatchProbe.dll [array|batch|perf]`，先核对身份为 `WDM_PROVIDER_TEST`，对象名带本轮随机后缀并在结束时逐个删除，新连接确认残留为 0。array、batch、perf 各段最终运行退出码 0。
- 包消费者：`dotnet pack` 得到 `0.1.0-batch.local` 本地包，同一探针源码仅以 PackageReference 编译，加载的程序集与包内 DLL 哈希一致；array 与 batch 全部场景通过，清理残留 0。
- 离线：12 个测试项目全部通过（含新增 `ArrayBindingAndBatchTests`）。issue #16 实库探针对候选源码回归通过。
- 独立代码审查（只读）发现的取消后关闭抛出、出错后仍可前进执行后续命令、命令间取消丢失等问题已修正，并在上述最终候选上重跑离线、实库与包消费检查。
- PR #19 review 修复（2026-10-15）：数据源批次先释放、Reader 后释放时，连接清理跳过已释放批次的属性设置，避免 `ObjectDisposedException`。独立代码复核通过；独立执行 Release `PoolTests` 308/308、`CommandTests` 31/31 通过，均退出 0。新增离线用例覆盖批次与 Reader 的 4 个同步/异步释放组合、Reader 重复释放，并确认物理连接关闭、池容量归零。构建 `tools/BatchProbe` 后，执行 `scripts/with-dameng-test.sh dotnet tools/BatchProbe/bin/net10.0/BatchProbe.dll batch` 退出 0：新 W 驱动身份为 `WDM_PROVIDER_TEST`，四种释放组合均读到 1、无释放异常，容量为 1 的池均可继续执行查询；`accepted=true`，本轮创建的 9 个对象全部删除，新连接确认残留为 0。本轮实库只重跑受影响的 batch 区段。
- 已知环境现象：远程实例上长时间逐行循环偶发 `SocketException: ConnectionReset`（`WDM_TRANSPORT`，Receive），在未修改的基线上同样复现；性能对照的逐行基线遇到时在自己的表上清空后重跑一次并记录。
