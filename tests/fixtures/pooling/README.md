# 会话重置与连接池测试场景

`scenarios.json` 分为 33 个合成用例和计划中的实库过程，分别标记 `not_run` 和 `planned_not_run`。数据描述状态转换、虚拟时间和 barrier 顺序，不包含协议包、猜测的 reset SQL/opcode、凭据、证书或真实服务器结果。

## 重置策略

默认策略为 `VerifiedResetOrDiscard`。Rollback、`SELECT 1` 或健康检查单独成功，都不能证明会话已恢复基线。

复用必须逐项证明声明可恢复的状态：事务、autocommit、隔离级别、Schema、角色/授权、时区/NLS/语言、临时对象、会话变量/锁、打开的游标/LOB locator、已准备语句，以及适用的包/过程状态。未知 SQL 或任何未验证维度都要求销毁。

重置能力、服务器语法、权限和隔离级别保持为环境输入或 `not_frozen`；fixture 不断言达梦存在 reset opcode 或完整重置机制。合成保存点/identity 上限是测试参数，不是服务器最大值。

## 池与 DataSource 覆盖

- POL-01/02：Min/Max 容量包含 creating/resetting；等待取消/超时、授权竞争、连接/初始化/清理失败，以及许可只释放一次。
- POL-03：完整不可变池身份，密码/Schema 大小写、别名规范化、哈希碰撞、凭据/证书隔离和脱敏诊断。
- POL-04/05/10：未知脏会话不进入 idle；重置失败销毁；实际复用/销毁和验证延迟只由测量得出。
- POL-06/07：租用代际防止旧取消影响新租用；ClearPool 代际安全退休 idle 和已借出会话；DataSource 释放取消等待者，但不无限等待已借出连接。
- POL-08/09：registry churn 有界；CreateCommand 持有连接到 Reader 关闭，成功或失败都只释放一次。
- POL-10：idle 生命周期和维护使用虚拟时间及受控调度器，不为每个连接创建线程。

实库过程使用 TEST wrapper、唯一对象、独立最终状态检查，并只清理探针创建的对象。准备文件未构建、连接数据库或执行池/重置探针。
