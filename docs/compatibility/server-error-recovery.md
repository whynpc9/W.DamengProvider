# 语句级服务器错误恢复

本页描述会话保留条件。基础能力已包含在 `0.1.0-preview.1`；后续传输失败包装、Prepare 恢复与能力快照已随 [0.2.0](../release-0.2.0.md) 发布，见 [issue #16 修订](I16-ef-integration-feedback.md)。

## 保留会话的条件

仅服务器版本精确为 `8.1.5.60`、收到完整且严格解析的四段服务器错误体、当前 session/invocation/所有权仍一致，并且后续 STMT_CLOSE 完整确认时，才考虑保留会话。执行阶段按下表判断；Prepare 阶段单独只开放 `-2007`（语法）、`-2106`（无效表/视图）、`-4015`（无效函数）。Prepare 的开放尚未随 `0.1.0-preview.1` 发布。执行阶段的 `-4015` 仍保持 Broken。

执行上下文还必须是本次命令所属的 Active 本地事务，或无本地事务且 `AutoCommit=true`、本地事务状态为 None/Committed/RolledBack/CompletedExternally。Starting、Committing、RollingBack、OutcomeUnknown、无跟踪事务的手动提交模式、其他 profile、取消或期限先胜、清理失败均不提升为可复用。失败句柄绕过语句缓存关闭；关闭失败时保留原服务器异常、错误号与 `ServerReported` 结果，同时将 `ConnectionReusable` 改为 false，并将活动事务结果标为 `OutcomeUnknown`。

| 错误号 | 已表征语义 |
| --- | --- |
| -564 | DISKSPACE LIMIT |
| -2007、-2106、-2124、-2207、-2713 | 语法、对象名、重复对象、序列与 IDENTITY 类型 |
| -5512、-5622 | 缺少 CREATE SCHEMA / MATERIALIZED VIEW 权限 |
| -6169 | 值过长 |
| -6602、-6607、-6609、-6628 | 唯一、外键、非空、父行引用约束 |
| -6804、-7069 | 未绑定参数标记、NOCYCLE 序列耗尽 |
| -20000…-30000 | 已表征的 RAISE_APPLICATION_ERROR 用户范围 |

`-3510` 同时用于其他错误，不在白名单内；越界 RAISE 转成该号时也保持 Broken。其他未知错误号保持 Broken，不能按消息文本猜测可恢复性。恢复与事务状态的实现见 [DmServerErrorRecovery](../../src/W.DamengProvider/Internal/Sessions/DmServerErrorRecovery.cs)及 [DmSession](../../src/W.DamengProvider/Internal/Sessions/DmSession.cs)。

## 参数标记与验证边界

参数集合为空时，命令不进行参数标记绑定，触发器 `:NEW`/`:OLD` 等原文交给服务器；词法安全检查仍保留。服务器拒绝未绑定的 `:X`/`?` 时，只有满足上述全部条件的 `-6804` 才可保留会话。有参数时的命名/位置匹配与未使用参数拒绝不变。

[ErrorRecoveryProbe](../../tools/ErrorRecoveryProbe/Program.cs)用于测量同一会话可继续执行、失败语句无部分效果、无遗留锁、autocommit 可见性及外层事务未被提交/回滚。2026-10-15 的 [Issue16Probe](../../tools/Issue16Probe/Program.cs) 另验证 Prepare 阶段三类错误的 36 个组合（显式/隐式、同步/异步、autocommit/事务提交/回滚），同会话后续命令、原事务数据及新连接最终状态均通过；同轮本地绑定拒绝恢复也通过。历史 EF 套件有 CREATE SCHEMA、CREATE MATERIALIZED VIEW 等权限限制；这些结果不能写成全套通过。新增服务器 profile 或错误号必须补相应正向证据，不因旧阶段通过而自动开放。
