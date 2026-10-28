# 诊断与 TLS 资源子项验证

这是历史 T18 验收入口，只覆盖实现、离线和 TLS 资源行为，不代表完整 R3 或发布验收。当时 T17 shared 大值和恢复仍是独立待办，不能由本工具替代。

消费者只使用新冻结的精确 PackageReference，并记录包/DLL SHA/MVID 及工具、fixture 输入哈希。

## 离线首次初始化

运行七个独立进程：normal、ShouldListenTo、InstrumentPublished、Sample、ActivityStarted/Stopped、measurement 抛错。

每个进程先确认 dispatcher 未初始化，再用最小独立 scripted allocation/SET TRANSACTION/ACK channel 调用真实公开 Open/BeginTransaction/CommitAsync，检查：

- `Committed`、单次 Commit 请求、原许可归零。
- 回调失败被隔离。
- AsyncLocal 秘密不流入诊断。

这些是公开 Activity/Meter 与业务 API 的离线协议测试，不是实库。DiagnosticsTests 另覆盖 Commit EOF→Unknown、原 token 首因、慢回调、4096 满队列 drop、10k 租用、多轮 128 registry、格式器和旧 cached getter 范围。没有网络且未明确完成/失败的旧 getter 可以不导出，不强迫 Complete 改变取消语义。

## TLS 资源预算

预算预先固定，不能为了通过而调整：

- warmup 60 秒。
- measurement 600 秒，至少 500 个完整 create/use/close 循环。
- 并发不超过 4。
- 每 5 秒采样，至少 121 点；停止业务后静默 30 秒，再取 3 点（间隔 5 秒）。
- 进程总上限 900 秒，不强制 GC。

每轮使用 40 KiB Blob stream/旧 array、公开 query/Begin/commit 或 rollback，包含 Clear，另有真实 queue-cancel/Clear/Dispose 控制。只使用 TEST/TLS wrapper、本机隔离 `RequireTls` 和无 CRL CA 的显式 `NoCheck`。唯一 `T18R_<20hex>` 表精确清理并由新连接确认不存在。

## 采样与判定

采样 RSS、GC heap/allocated、threads/FD/handle、pool/registry/queue、创建关闭、网络字节和 LOB chunks。平台不支持的指标记为 `null` / `unavailable`，不能填 0。

独立 validator 严格检查 121+3 个样本：

- 相对 warmup 基线：RSS ≤256 MiB、heap ≤128 MiB、thread ≤16、FD ≤16，quiescent FD ≤baseline+8。
- 60 秒窗口后半/前半 median 增量：RSS ≤64 MiB、heap ≤32 MiB。
- 后半 threads/FD 每 100 cycle 线性增长分别 ≤1/1。
- 最终 pool、socket、queue 归零，registry ≤128，queue ≤4096。
- 正常 listener 的 drop/callback failure 为 0。

任一项不满足即非零，不能缩短时长、降低负载或调整阈值。

## 诊断数据边界

业务诊断只使用原子数字和不可变完成记录；listener 通过真实 Activity/Meter 统计固定 instrument 和有限枚举标签。不保存 SQL、参数、用户、Schema、endpoint、locator、异常、TraceState 或 baggage。公开 formatter 只输出安全枚举、代码和数值，未知异常不输出 message/type。

callback/source 初始化失败和慢回调不能影响已确认 Commit 或 Unknown；队列饱和必须明确 drop。每个新连接验证 TEST/Schema，网络 hook 只记录数字并要求异步路径 sync=0；该 hook 不冒充公开诊断。

累计 count 通过 ObservableCounter 读取进程原子值，export queue 只传 span/hist。collector 保存每个固定标签桶的最新累计值，不累加重复 poll；静默期总和必须等于核心真值。late-listener 和溢出恢复不能丢 created/closed 总数。显式 RecordObservableInstruments 的用户回调异常属于调用方，不能要求核心替外部 SDK 捕获一切。固定 15 个 instrument 由源码集合严格验证，未知动态名称或标签拒绝且不写原值。

命令、退出码、源码/包前后 hash、依赖和安全输出均保留。实现冻结后由验证者在单一窗口执行，不能预填通过；超过 900 秒按 checkpoint 的本轮精确名称恢复，旧失败不覆盖。
