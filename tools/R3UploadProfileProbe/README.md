# PUT_DATA2 ACK 小范围探针

这是历史 T17 子项验证，只消费固定 T16 包 `0.1.0-r3.t16.20261002143244`，包/DLL SHA 和 MVID 均锁定。它不是流式输入或完整 T17 验收。

## 输入

- 使用已有 `byte[]` / `string` 参数。
- BLOB：131089 字节，独立 `byte_cycle_v1`（seed 17、stride 131）。
- CLOB：合成 `out_row_profile_large`，UTF-8 为 174495 字节。
- 两者都小于完整值物化上限。

## 观察与通过条件

`AfterFrameSent` 只统计 request 26；`DmResultProtocolTrace.AfterFrame` 也只过滤 request 26，记录请求/响应 opcode、SQL status 和 body length。`BeforeDecode` 用 `CurrentRequestOpcode==26` 交叉核对覆盖次数。

不读取响应 body、token、locator 或认证帧。通用 Csi buffer 不代表所有局部 ACK buffer，因此直接使用已有 header 数字轨迹。

成功要求：已发送 26 帧、ACK header 和 BeforeDecode 三个计数相等，状态成功，INSERT/UPDATE 和行存在检查通过。响应 opcode/body length 只报告实测集合，不猜固定值。

每个新 TEST 连接核对身份和同名 Schema，显式选择 TLS/明文；唯一 `T17U_<20hex>` 表精确清理，再由新连接确认不存在。实际网络异步、创建/关闭和模式计数必须配平。

进程上限 90 秒。失败或缺少 ACK 覆盖返回非零/`not_proven`，保留 checkpoint，由验证者精确恢复本轮对象；不重试、不提权。本文只描述验证合同，执行需按原历史窗口安排。
