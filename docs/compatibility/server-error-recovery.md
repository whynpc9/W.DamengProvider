# 服务器错误后的连接恢复

本文说明哪些服务器错误允许继续使用当前会话。基础能力已包含在 `0.1.0-preview.1`；Prepare 恢复、传输失败包装和能力快照自 [0.2.0](../release-0.2.0.md) 起提供。

## 保留会话的必要条件

只有同时满足以下条件，驱动才会考虑保留会话：

- 服务器版本精确为 `8.1.5.60`。
- 收到完整且能严格解析的四段服务器错误体。
- 仍是同一会话、同一次调用，且资源归属未变。
- 后续 `STMT_CLOSE` 得到完整确认。
- 错误号和执行阶段符合下述白名单。
- 当前处于命令所属的活动本地事务，或没有本地事务且 `AutoCommit=true`、本地事务状态为 None/Committed/RolledBack/CompletedExternally。

Starting、Committing、RollingBack、OutcomeUnknown、无跟踪事务的手动提交模式、其他服务器环境、取消或超时优先、清理失败等情况都不会提升为可复用。

失败句柄会绕过语句缓存关闭。关闭失败时，驱动保留原服务器异常、错误号和 `ServerReported` 结果，但将 `ConnectionReusable` 改为 `false`，并把活动事务结果标为 `OutcomeUnknown`。

## 错误号与阶段

Prepare 阶段只开放 `-2007`（语法）、`-2106`（无效表/视图）、`-4015`（无效函数）。直接执行阶段的 `-4015` 仍会使连接进入 `Broken`。

| 执行阶段错误号 | 已验证含义 |
| --- | --- |
| -564 | DISKSPACE LIMIT |
| -2007、-2106、-2124、-2207、-2713 | 语法、对象名、重复对象、序列与 IDENTITY 类型 |
| -5512、-5622 | 缺少 CREATE SCHEMA / MATERIALIZED VIEW 权限 |
| -6169 | 值过长 |
| -6602、-6607、-6609、-6628 | 唯一、外键、非空、父行引用约束 |
| -6804、-7069 | 未绑定参数标记、NOCYCLE 序列耗尽 |
| -20000…-30000 | 已验证的 RAISE_APPLICATION_ERROR 用户范围 |

`-3510` 同时用于其他错误，不在白名单内；越界 RAISE 转成该错误号时也会保持 `Broken`。其他未知错误号同样保持 `Broken`，不会根据消息文本猜测是否可恢复。

实现位置：[DmServerErrorRecovery](../../src/W.DamengProvider/Internal/Sessions/DmServerErrorRecovery.cs) 和 [DmSession](../../src/W.DamengProvider/Internal/Sessions/DmSession.cs)。

## 参数标记

参数集合为空时，命令不绑定参数标记，触发器 `:NEW` / `:OLD` 等原文交给服务器，词法安全检查仍保留。服务器拒绝未绑定的 `:X` / `?` 时，只有满足上述全部条件的 `-6804` 才可保留会话。有参数时的名称/位置匹配和未使用参数拒绝规则不变。

## 验证边界

[ErrorRecoveryProbe](../../tools/ErrorRecoveryProbe/Program.cs) 用于检查同一会话能否继续执行、失败语句是否无部分效果、是否遗留锁，以及外层事务是否未被提交或回滚。

2026-10-15 的 [Issue16Probe](../../tools/Issue16Probe/Program.cs) 验证了 Prepare 阶段三类错误的 36 个组合，以及同会话后续命令、原事务数据和新连接最终状态。同轮也验证了本地绑定拒绝后的恢复。

历史 EF 套件存在 CREATE SCHEMA、CREATE MATERIALIZED VIEW 等权限限制，不能写成全套通过。新增服务器环境或错误号前，必须补充对应正向证据。
