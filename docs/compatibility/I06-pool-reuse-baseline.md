# 物理连接复用调查与性能基线

## 当前结论

**当前版本没有物理连接复用。** 每次租用都会新建物理会话，归还时销毁。

2026-10-15 的 [issue #6](https://github.com/whynpc9/W.DamengProvider/issues/6) 调查没有找到能够证明完整会话重置的接口；在 DM 8.1.5.60 上，至少序列 `CURRVAL` 无法恢复。因此不会提供“部分重置后复用”的选项，也不会预先加入默认关闭的复用状态机。

## 为什么现有重置方式不够

源码审查和服务器核查发现：

- `Internal/Legacy/A/A.Async.cs` 的 `ResetAsync` 进入命令 17 编码路径，只是 statement reset，不是完整会话重置。
- 旧 `Internal/Legacy/Dm.util/ConnPoolCache.cs` 归还连接时只按事务标志回滚，再执行 `CleanDmConnection()` 清除逻辑引用，不能证明 Schema、角色、时区/NLS、临时数据、会话变量、锁和包状态已恢复。
- [官方参数文档](https://eco.dameng.com/document/dm/zh-cn/pm/tracking-resolution) 的 `SP_RESET_SESSION_PARA_VALUE` 只把指定会话级 INI 参数恢复为系统值，没有声明完整会话重置范围。
- 恢复版命令表 `Internal/Legacy/Dm/T02_0200008D.cs` 没有会话重置命令。
- `SYS.DBMS_SESSION.RESET_PACKAGE` 在 TEST 下被 `-5505` 权限错误拒绝；用户决定不为此扩大权限。即使组合多个重置步骤，任一步失败也只能销毁会话。
- 同一会话中 `NEXTVAL` 返回 41，执行 `ROLLBACK` 后 `CURRVAL` 仍为 41；新会话读取 `CURRVAL` 得到 `-7147 序列当前值尚未在此会话中定义`。没有找到可恢复该状态的客户端或服务器操作。

这些结果表示“当前调查未找到完整重置能力”，不等于证明所有达梦版本都不存在该能力。不能用 ROLLBACK、存活 SELECT 或客户端属性恢复替代完整证明，也不能猜测协议 opcode 或调用高权限过程。

如果未来要支持复用，还需要区分物理会话和逻辑租用，并对 Reader、LOB、事务、迟到取消和故障回调进行代际检查。池的 Resetting/Idle 状态、Clear 代际、关闭计数和 registry 回收也都需要完整实现；当前销毁策略通过测试，不代表这些风险已经消失。

## 历史验证：销毁策略与状态隔离

当时受测产品为 `27058bc`，产品源码未修改。新增 [Issue6PoolBaseline](../../tools/Issue6PoolBaseline/README.md) 通过 ProjectReference 测量驱动；状态隔离使用 R3ResetProbe 消费本地包 `0.1.0-issue6.baseline`。官方驱动和恢复版未参与测试。

```sh
dotnet test tests/W.DmProvider.PoolTests/W.DmProvider.PoolTests.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers --logger 'trx;LogFileName=pool.trx' --results-directory "$PWD/.local/issue6/tests"
dotnet test tests/W.DmProvider.SessionTests/W.DmProvider.SessionTests.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers --logger 'trx;LogFileName=session.trx' --results-directory "$PWD/.local/issue6/tests"
dotnet pack src/W.DamengProvider/W.DamengProvider.csproj -c Release -o .local/issue6/feed /p:Version=0.1.0-issue6.baseline -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
bash eng/t15.sh shared 0.1.0-issue6.baseline "$PWD/.local/issue6/feed"
```

PoolTests **304/304**、SessionTests **21/21** 通过，测试、pack、T15 均退出 0。证据位于本机忽略目录 `.local/issue6/` 和 `.local/verification/r3/t15/runs/20261015T061238Z-37165-10066/`。

T15 每轮先核实 TEST 身份，服务器 8.1.5.60，显式 `PlaintextAllowed`，协商 mode 0。16 个物理连接全部销毁，复用 0；本轮表 `T15_T_DDB40243281D41D987A4`、`T15_G_DDB40243281D41D987A4` 均由新连接确认不存在。

- 已验证销毁后的隔离：未提交事务行、事务锁、临时表会话行、时区、旧 Reader/Prepared 对象。
- 只观察新会话基线：autocommit、隔离级别、Schema。
- NLS 日期格式只覆盖子项；完整 NLS/语言、角色、会话变量、会话锁和包状态未证明。
- 所有 `same_session_reset_proven` 均为 `false`。旧对象销毁后的拒绝不能证明同一物理会话换租后的隔离。

T15 输出中的 `reset_source_audit.scope=current_source_and_S09` 是探针固定说明；当次独立审查只检查仓库现存源码，没有重新审查独立 S09 副本。

## 性能基线

工具对 DataSource 和 direct Pooling 各运行串行、4 worker 场景，每场景 5 次预热、100 次测量。OpenAsync 分位数不含身份查询；吞吐量包含 Open、身份 SELECT、Close。它不测容量不足的排队，不创建数据库对象，也不把 TCP 建连数当成认证次数。

```sh
dotnet build tools/Issue6PoolBaseline/Issue6PoolBaseline.csproj --configuration Release -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet tools/Issue6PoolBaseline/bin/Release/net10.0/Issue6PoolBaseline.dll offline
scripts/with-dameng-test.sh dotnet tools/Issue6PoolBaseline/bin/Release/net10.0/Issue6PoolBaseline.dll shared 100 4 5
```

探针使用 Release 和 .NET 10.0.12 / linux-x64，实际加载产品 DLL MVID 为 `d5eb99a2-89c1-49dc-aa99-06af7b8f368a`。服务器数字版本为 8.1.5.60；`COMPATIBLE_MODE` 读取被 `-5504` 拒绝，因此记为未知。

| 场景 | Open p50 / p95 / p99（ms） | 完整租用/s | 测量物理建连/关闭 |
| --- | --- | --- | --- |
| DataSource，串行 | 22.0439 / 27.5088 / 30.7873 | 34.9835 | 100 / 100 |
| DataSource，4 worker | 37.3400 / 50.2452 / 51.9162 | 78.2183 | 100 / 100 |
| direct Pooling，串行 | 23.4026 / 32.5598 / 37.4239 | 32.3454 | 100 / 100 |
| direct Pooling，4 worker | 34.2317 / 46.0298 / 768.8125 | 54.4457 | 100 / 100 |

400 次测量均完成；包含预热和最终身份检查在内的 422 个连接全部关闭，池计数归零。复用率为 0。该表只是一轮指定负载的测量，direct Pooling 并发场景存在明显尾延迟，不能据此推导稳定容量或性能提升。

### 保留的失败记录

- 首次运行在可选 `SF_GET_PARA_VALUE` 查询收到 `-5730`，退出 1，测量尚未开始；改用 `V$DM_INI`，元数据拒绝会记录为不可用并丢弃专用连接。产品错误恢复白名单未修改。
- 第二轮前三个场景完成，第四个 direct Pooling/4 worker 场景出现 `WDM_TRANSPORT / Receive / Unknown`；337 个 socket 全部关闭，池计数归零，未执行写库，但未走到最终身份检查。
- 第三轮通过，但第二轮传输失败未复现，根因未确定，不能宣称已修复或归因于环境。失败和成功记录均保留。

原始证据：[首次辅助查询失败](../implementation/evidence/I06-pool-baseline-20261015/baseline-r1-failed.json)、[第二轮传输失败](../implementation/evidence/I06-pool-baseline-20261015/baseline-r2-failed.json)、[第三轮完整基线](../implementation/evidence/I06-pool-baseline-20261015/baseline-r3.json)、[T15 状态与清理](../implementation/evidence/I06-pool-baseline-20261015/discard.json)、[离线及负向检查](../implementation/evidence/I06-pool-baseline-20261015/offline-and-negative.json)。

离线 smoke 退出 0 且 socket 0；缺少 TEST 配置退出 3；合成错误身份在认证前拒绝；样本数越界退出 1。当时缺少 TLS TEST 配置，TLS 为 `integration_pending`。

## 后续重新评估的条件

必须找到明确说明状态范围和适用版本的会话重置能力，并在 TEST 上逐项污染和验证，包括序列等会话值、重置失败拒绝矩阵和复用前后性能比较。当前基线不能作为缩减这些验证要求的依据。
