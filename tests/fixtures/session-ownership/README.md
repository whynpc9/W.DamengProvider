# 会话与资源生命周期测试数据

`scenarios.json` 提供 S03 会话、执行租用、调用和 Reader 生命周期的确定性合成输入，以及候选实库过程。当前行为见[会话与资源管理](../../../docs/compatibility/T05-session-ownership.md)。

## 离线数据

`offline_scenarios` 包含有序交互、身份不匹配、计数器耗尽、网络故障矩阵和嵌套 LOB 等形状。它们都是客户端状态机输入和期望，不表示实现已通过。

快速失败要求异常可赋值给 `InvalidOperationException`、不等待当前占用者且没有网络活动。合成 Read 结果或 `chunk_consumed` 都是模型输入，不是数据库响应。

身份由 session ID、lease generation、execution ID、invocation ID 组成；旧取消向量每次只改变一个分量。溢出值使用十进制字符串，避免 JSON 舍入。当前实现将计数器耗尽报告为 `InvalidOperationException`；溢出不得回绕，相关物理会话必须不可用。Open 或池验证失败最终为 Closed，已打开会话中的失败最终为 Broken。

网络矩阵覆盖 TCP connect、旧版和 MSG 收发、超时设置及当前未引用的 poll/receive 健康探测。每个活动调用点都应归属对应会话；独立 DBAliveCheckThread TCP 探测不共用会话传输。文件不包含协议包、payload 或服务器回复。

LOB 向量模拟 Reader → Stream → Chunk 的子调用：子调用保留根会话、代际和执行身份，使用不同 invocation ID，且不重新获取根执行租用。

## 实库过程

`real_database_probes` 只是过程描述，全部为 `planned_not_run`。实际运行使用 TEST wrapper 和 `WDM_PROVIDER_TEST`，不把本机连接配置写入 fixture；需要 LOB 数据时只创建和清理 TEST 下本轮唯一对象。

实库可验证驱动可见行为，但不能证明内部身份竞争、checked 溢出、零字节快速失败或每个网络入口的异常处理；这些仍需离线和故障注入测试。准备文件未打开连接或运行测试。
