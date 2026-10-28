# 精确包会话重置/销毁探针

这是历史 T15 验收入口，用于验证指定包的销毁策略及哪些会话状态仍无法证明可重置。普通发布不需要运行；复现时使用原精确版本和 feed。

消费者只引用唯一精确 `W.DamengProvider` 包，禁止 ProjectReference 和官方驱动依赖。它不修改产品，也不会凭一次健康检查声称完整会话重置。

## 运行方式

由独立验证者执行：

```text
eng/t15.sh offline|shared|tls <exact-version> <frozen-local-feed>
```

每轮使用独立 bin/obj、NuGet cache 和 CLI home，不修改历史证据或清空全局缓存。实库连接先检查配置用户，再验证服务器 USER、当前 Schema、STARTUP mode 和新 socket。

- shared：显式 `PlaintextAllowed`。
- 本机 TLS：`RequireTls` 和实际证书验证；只有本机无 CRL 的临时 CA 显式 `NoCheck`，产品默认不变。

JSON 包含包 SHA、加载 DLL SHA/MVID、runtime/RID、数字服务器版本、13 项能力清单、命令退出码和新连接清理状态。异常只输出类型、固定分类和数字错误码，不包含消息、SQL、连接配置或认证数据。`LogLevel=OFF`，runtime stderr 不保存。

## 重置调查边界

当前源码中的 `Internal/Legacy/A/A.Async.cs:ResetAsync` 调用 `B.Async.cs:AAsync(A)` 并传入 statement handle，没有完整 session reset 的状态范围、权限或版本证明。探针不会把它当成会话重置，也不猜 opcode 或调用高权限过程。

这只表示当前没有已验证的全量重置，不代表服务器不存在其他能力。

## 必需实库验证

- autocommit 的新连接行可见性、ReadCommitted 事务。
- 未提交写入在独立观察连接不可见。
- 物理 Close 销毁一个 socket；新连接确认未提交写入消失、主键事务锁释放。
- 旧 Reader/Prepared 有成功正控制；物理 Close 后必须拒绝旧对象，且没有新增发送或 socket。
- 每个新连接独立验证身份和 Schema。

临时表按[官方管理表 §9.2.5](https://eco.dameng.com/document/dm/zh-cn/pm/management-table.html)使用唯一名称和 `ON COMMIT PRESERVE ROWS`：旧会话中的行在新物理会话为零，结构保留到最后精确 DROP。创建被拒绝时记录数字错误和 `not_proven`，不猜原因或计为通过。

Schema/isolation/autocommit 只表示新会话基线，未证明同会话 reset。角色/授权、语言、会话变量、session 命名锁和 package/procedure state 保持 `not_proven`；主键事务锁释放不等于 session 锁复位。

时区和 NLS 使用[官方定义语句 §3.14](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)及[官方函数](https://eco.dameng.com/document/dm/zh-cn/pm/function.html)：记录 baseline、设置 +9:00、确认效果后 Close，再由新连接回读 baseline。NLS 日期格式同样处理，但语言未探测，整个 NLS/语言项仍 `not_proven`，另附 date_format 子项。

只有 FailureInfo 明确为 Server/ServerReported、含数字 ServerErrorNumber、ConnectionReusable=true 且连接 Open 时，才允许可选能力拒绝；通信、超时或 Broken 一律失败。

## 固定结论与清理

决策固定为 `session_reset_verified=false`、`reuse_policy=discard`、`measured_reused_sessions=0`。即使只执行 SELECT，也不能宣称安全复用或减少握手。

`offline` 只验证精确包和公开 API，所有能力未证明且 `integration_pending`；实库缺连接返回 3，必需 case、清理或包一致性失败返回非零。

`probe.json` 在阶段切换时持久化安全 checkpoint。所有必需门通过前保持 started、accepted=false。外部 launcher 900 秒截止后终止独立子进程组，留下本轮对象名和 `requires_checkpoint_recovery`；超时不算验收。

恢复必须由独立验证者用新 TEST 连接和精确对象名执行，不查询或删除其他 Schema。普通失败也在 finally 中精确清理：创建前确认随机名不存在，即使 DDL ACK 丢失，也只查并清理本轮该名称；最后由另一新连接确认两个随机对象都不存在。

本文是验证合同，历史结果不能替代当前候选的实际运行。
