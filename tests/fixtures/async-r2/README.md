# R2 异步逻辑调度数据

`scenarios.json` 提供[异步和取消说明](../../../docs/compatibility/R2.md)中客户端异步、取消、期限和结果状态的离线合成调度。每个场景均为 `evidence_level: offline_logical_fixture`、`execution_status: not_run`。

场景包含有序 `steps` 和 `expected_outcomes`；Cancel/Close 竞争提供两个 `schedule_variants`，让消费者用 barrier 分别驱动两种顺序。pending gate、逻辑传输计数、合成时钟和身份元组都是测试输入，不是驱动运行记录。

## 覆盖范围

- ASY-01/02：仅异步传输、异步操作 pending 时调用方可继续推进、barrier 控制响应释放、短发送/读取。
- TMO-02：heartbeat 可更新合成 read-idle 预算，但总命令期限保持固定。
- CAN-01/03/04/05：预取消零发送、ExecuteReader 返回后解绑令牌、旧代际取消无效，以及两种 Cancel/Close 顺序。
- ERR-01：Commit 发送后发生传输失败，结果未知且驱动不重放。

文件不包含 SQL、连接配置、凭据、认证材料、协议字节或 opcode 假设。Heartbeat 和传输计数只是抽象测试信号；Commit 未知不断言服务器是否已提交。

## 使用边界

消费测试必须用受控 fake async transport、barrier 和时钟实际执行这些调度，断言观察值，并单独保存测试命令、运行器/源码身份、退出码和结果。fixture 本身不证明任何实现通过；准备过程未打开数据库或 socket。
