# old/new/old 只读健康诊断

这是历史 T14 的辅助诊断，不替代 T14 验收。它用旧→新→旧三个独立运行定位行为差异，只执行只读查询，不创建、修改或删除数据库对象。

消费者只使用精确 PackageReference，锁定旧 T13 DLL `3eaa6a45...` 和当时 T14 DLL `fdea777d...`，不引用或修改产品源码、原 T14Probe、测试或历史证据。

每个 case 使用新 TEST 连接并先核验身份/Schema，shared 显式 `PlaintextAllowed` / mode 0。Connect 20 秒、Command 15 秒不变；30 秒工具等待和进程上限只限制失控执行，不改变产品预算。

## health 三轮对照

```text
eng/t14-diagnostics.sh health <old-exact-version> <old-feed> old-before
eng/t14-diagnostics.sh health <new-exact-version> <new-feed> new
eng/t14-diagnostics.sh health <old-exact-version> <old-feed> old-after
```

health 进程上限 300 秒。每轮七项最小对照：

- Reader 读取首行后显式 Close：无参 SELECT 1、Int32 参数 SELECT、字面量 catalog COUNT 0、VarChar 参数 catalog COUNT 0。
- ExecuteScalarAsync：SELECT 1、字面量 COUNT 0、VarChar 参数 COUNT 0。

目录查询针对已恢复的精确表 `T14_921060586F914E658A7FD736`，只查询自身 USER_TABLES，不再次清理。COUNT 未返回或查询失败都不能当成 0。

Reader 不额外消费 EOF，以区分首行与 Close/Dispose 的网络窗口；Scalar 的内部 Read/Close 通过数字 opcode 和调用快照定位。

每个 await 前写安全进度，阶段固定为 Open、Identity/Query 的 Execute/Read/Fields/Close、Scalar.Execute、Scalar.Command.Close、Connection.Close。首个失败阶段保留，Reader/Command/Connection 的关闭错误单列。

数字 hook 由异步单 writer 写 `probe.json.frames.jsonl`，不阻塞等待磁盘，也不修改取消或期限。旧 DLL 没有 `AfterFrameSent` 时明确 `sent_opcode_hook_supported=false`，只用兼容 `AfterSendBeforeReceive`，RequestOpcode=null，不伪造发送 opcode。

可选字段读取失败只记 null，不制造网络失败。FailureInfo 只读枚举和受控符号码，不输出异常消息、堆栈、SQL、参数值、连接配置或凭据。

## readers 三轮对照

readers 必须先提供 gate JSON，格式为 `health_reports` 数组，按 old-before/new/old-after 顺序包含 lane、绝对 path 和 sha256。工具锁定刚通过的 health-ab-v1 三份报告，并核对七项 case、身份/mode、包/DLL/MVID 和 20/15 秒预算；任何不匹配都在连接前拒绝。

```text
eng/t14-diagnostics.sh readers <exact-version> <feed> old-before <absolute-gate-json>
eng/t14-diagnostics.sh readers <exact-version> <feed> new <absolute-gate-json>
eng/t14-diagnostics.sh readers <exact-version> <feed> old-after <absolute-gate-json>
```

每轮两项新连接只读 DUAL 2048×1024：

- baseline 不取消 Execute token。
- 另一项只在 ExecuteReader 返回后取消旧 token，后续 Read/getter 使用新 token。

保留 Execute 返回、缓存首行、读取/验证行数、失败前进度、实际 Fetch 和最后 sent/response opcode，Close 错误单列。大结果只在实际 Fetch、首末行和每 100 行写 checkpoint，不逐行写报告。

每次 Reader 调用的 15 秒预算不变；readers 进程上限 900 秒，用于完整缓存 getter 循环和 checkpoint，不是提高产品预算。此模式不创建表或执行恢复 DDL。
