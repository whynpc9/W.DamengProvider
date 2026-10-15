# Issue #6：物理连接复用前置调查与基线

2026-10-15，针对 [Issue #6](https://github.com/whynpc9/W.DamengProvider/issues/6) 建立连接性能基线入口，并验证当前销毁策略的状态隔离。本轮是前置工作，**没有实现物理连接复用，Issue #6 尚未完成**。产品继续归还即销毁；没有可启用复用的已验证服务器 profile。

受测产品为 `27058bc`，产品源码未修改。新增工具 [Issue6PoolBaseline](../../tools/Issue6PoolBaseline/README.md) 通过 ProjectReference 测量当前 W 驱动；状态隔离另用现有 R3ResetProbe 消费本地包 `0.1.0-issue6.baseline`。官方驱动、恢复版未参与本轮测试。工具和本文属于该 commit 上的未提交增量，不把产品 commit 当作新增工具的提交身份。

## 完整复位的缺口

独立源码审查确认：

- `Internal/Legacy/A/A.Async.cs` 的 `ResetAsync` 传入 statement handle，进入 `B.Async.cs` 和 `T02_02000093.cs` 的命令 17 编码路径，只是 statement reset。
- 旧 `Internal/Legacy/Dm.util/ConnPoolCache.cs` 的归还仅按事务标志回滚，再执行 `CleanDmConnection()`；后者只清除逻辑连接引用。不能证明 schema、角色、时区/NLS、临时数据、会话变量/锁和包状态已恢复。
- [官方参数文档](https://eco.dameng.com/document/dm/zh-cn/pm/tracking-resolution) 的 `SP_RESET_SESSION_PARA_VALUE` 仅把指定会话级 INI 参数恢复为系统值，没有声明完整 session reset 的状态覆盖。

以上说明当前调查**未找到可证明完整复位的接口**，不等于证明所有达梦版本都不存在该能力。不能用 ROLLBACK、存活 SELECT 或客户端属性恢复替代完整证明，也没有猜测协议 opcode 或调用高权限过程。

后续实现还需拆分物理会话与逻辑租约：当前 `DmSession` 的代次没有生产换租入口，`DmConnInstance.Session` 是固定引用，若沿用同一 Session 对象，旧 command 的引用相等检查不足以隔离新租约。需要连同 reader、LOB、transaction、迟到取消及故障回调进行代次校验；池调度的 Resetting/Idle、Clear epoch、关闭完成计数与 registry 回收也尚未实现。这些风险没有因当前 discard 回归通过而消失。

## 独立验证

构建/测试均设置可写任务目录 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`，串行执行共享 bin/obj 和实库窗口。

```sh
dotnet test tests/W.DmProvider.PoolTests/W.DmProvider.PoolTests.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers --logger 'trx;LogFileName=pool.trx' --results-directory "$PWD/.local/issue6/tests"
dotnet test tests/W.DmProvider.SessionTests/W.DmProvider.SessionTests.csproj -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers --logger 'trx;LogFileName=session.trx' --results-directory "$PWD/.local/issue6/tests"
dotnet pack src/W.DamengProvider/W.DamengProvider.csproj -c Release -o .local/issue6/feed /p:Version=0.1.0-issue6.baseline -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
bash eng/t15.sh shared 0.1.0-issue6.baseline "$PWD/.local/issue6/feed"
```

PoolTests **304/304**、SessionTests **21/21** 通过，测试、pack、T15 各阶段退出码均 0。日志和 TRX 保存在 ignored `.local/issue6/`，T15 原始记录位于 `.local/verification/r3/t15/runs/20261015T061238Z-37165-10066/`。独立审查者检查源码，独立验证者执行上述命令，主 agent 复核结果。

T15 每次连接先核对 `WDM_PROVIDER_TEST` 用户及同名 Schema，服务器数字版本 `8.1.5.60`，typed builder 显式 `PlaintextAllowed`，协商 mode 0。16 个物理连接全部销毁，复用 0；本次唯一表 `T15_T_DDB40243281D41D987A4`、`T15_G_DDB40243281D41D987A4` 最终均经 fresh 连接确认不存在。

- 事务未提交行、事务锁释放、临时表会话行、时区、旧 reader/prepared 对象：验证物理销毁后的隔离。
- autocommit、隔离级别、Schema：仅观察 fresh 会话基线。
- NLS 日期格式：仅子项验证；完整 NLS/语言、角色、会话变量、会话级锁、包/过程状态未证明。
- 所有 `same_session_reset_proven` 均为 false。旧对象被销毁后的拒绝不证明同一物理会话换租后的隔离；本轮也没有补充 LOB 同会话 reset 证明。

T15 输出中的 `reset_source_audit.scope=current_source_and_S09` 是既有探针固定说明；本次独立审查只检查仓库现存源码，没有重新审查独立 S09 副本。

## 性能测量与限制

新工具对 DataSource 和 direct Pooling 各运行串行、4 worker 场景，每场景 5 次预热、100 次测量。OpenAsync 分位数不含身份查询；吞吐量包含 Open、身份 SELECT、Close。它不测池容量不足的排队，不创建数据库对象，也不将 TCP 建连计数冒充认证次数。

```sh
dotnet build tools/Issue6PoolBaseline/Issue6PoolBaseline.csproj --configuration Release -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
dotnet tools/Issue6PoolBaseline/bin/Release/net10.0/Issue6PoolBaseline.dll offline
scripts/with-dameng-test.sh dotnet tools/Issue6PoolBaseline/bin/Release/net10.0/Issue6PoolBaseline.dll shared 100 4 5
```

测试项目使用默认 Debug，探针使用 Release，运行时 `.NET 10.0.12 / linux-x64`。最终受控运行退出 0，实际加载产品 DLL MVID 为 `d5eb99a2-89c1-49dc-aa99-06af7b8f368a`。服务器数字版本 `8.1.5.60`，`COMPATIBLE_MODE` 读取被服务器以 `-5504` 拒绝，记为未知；没有取得完整服务器 build/profile，不能据此指定可复用 profile。

| 场景 | Open p50 / p95 / p99（ms） | 完整租约/s | 测量物理建连/关闭 |
| --- | --- | --- | --- |
| DataSource，串行 | 22.0439 / 27.5088 / 30.7873 | 34.9835 | 100 / 100 |
| DataSource，4 worker | 37.3400 / 50.2452 / 51.9162 | 78.2183 | 100 / 100 |
| direct Pooling，串行 | 23.4026 / 32.5598 / 37.4239 | 32.3454 | 100 / 100 |
| direct Pooling，4 worker | 34.2317 / 46.0298 / 768.8125 | 54.4457 | 100 / 100 |

400 次测量租约均完成；总计含 20 次预热、profile 和最终身份检查的 422 个连接全部关闭，所有池计数归零。最终 fresh 连接确认 TEST 身份/Schema；负载仅 SELECT，未创建对象。复用率为 0。这是一轮指定负载的测量，直接池并发场景存在明显尾延迟，不推导稳定容量或性能提升。

首次运行在可选 `SF_GET_PARA_VALUE` 查询收到服务器 `-5730`，退出 1；测量尚未开始，1 个 socket 已销毁，未创建对象。失败记录保留为 `.local/issue6/baseline-shared.json`。修订后使用 `V$DM_INI`；明确的服务器元数据拒绝会记录为不可用，并丢弃专用 profile 连接，后续测量重新连接并确认身份。没有修改产品错误恢复白名单。

第二轮退出 1：前三个场景完成，第四个 direct Pooling/4 worker 场景出现 `WDM_TRANSPORT / Receive / Unknown`；337 个 socket 全部关闭、各池计数归零、未执行写库，但未走到最终独立身份检查。随后仅增加有界异常类型/socket 数码诊断和实际完成计数，在确认资源排空后进行上述一次受控对照。第三轮通过，**第二轮传输失败未复现，根因未确定，没有宣称已修复或归因于环境**。保留失败和成功记录，不以第三轮成功覆盖失败；该失败仍是后续稳定性调查项。

可审阅的原始安全输出：[首次辅助查询失败](../implementation/evidence/I06-pool-baseline-20261015/baseline-r1-failed.json)、[第二轮传输失败](../implementation/evidence/I06-pool-baseline-20261015/baseline-r2-failed.json)、[第三轮完整基线](../implementation/evidence/I06-pool-baseline-20261015/baseline-r3.json)、[T15 状态与清理](../implementation/evidence/I06-pool-baseline-20261015/discard.json)、[离线及负向检查](../implementation/evidence/I06-pool-baseline-20261015/offline-and-negative.json)。原始日志另保留在 `.local/issue6/`。

离线 smoke 退出 0 且 socket 0；缺 TEST 配置退出 3；合成错误身份在认证前拒绝、退出 1 且 socket 0；样本数越界退出 1。TLS TEST 配置与证书本机缺失，TLS 为 `integration_pending`。

完整复位入口、状态覆盖、同物理会话复用正控制、复位失败拒绝矩阵和复用前后性能比较仍待完成。取得有明确状态范围及适用版本的 reset 能力后，需在 TEST 上逐项污染并验证，才可启用复用；当前基线不能作为缩减验收范围的依据。
