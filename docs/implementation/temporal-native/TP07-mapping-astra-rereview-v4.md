# TP07 mapping-v4 清理fixture定点复核

2026-10-09。结论：**static_fix_accepted / runtime_pending**。仅复核 [v3 triage](TP07-mapping-v3-triage.md) 的连接所有权修复，未运行SDK、测试或DB，未修改源码。

v3/v4均175文件，唯一变化为 `TP07TemporalMappingTests.TimeSpanTimeAlsoRejectsDuringRealModelConstruction`。外层using创建并拥有真实W连接，内层实际DurationAsTimeContext使用`contextOwnsConnection:false`；原 `Assert.Throws<NotSupportedException>(() => _ = context.Model)` 原样保留。context正常退出后断言Disposed事件计数0，外层真实连接正常Dispose后断言1。

该结构既保持真实模型拒绝，也让调用方明确执行W/Component清理；断言异常时using仍释放资源。没有捕获忽略ObjectDisposedException、mock Dispose或删除model assertion，没有改产品生命周期。新fixture通过也不意味着EF原context-owned失败初始化诊断路径已经修复；原失败边界仍保留。

| 冻结输入 | SHA256 |
| --- | --- |
| `TP07TemporalMappingTests.cs` | `ffaf47a06a0f7848deefa10a30e07afb9f11b18a98920b6d7ad19317e2c0264a` |
| `tools/DownstreamAcceptance/TP07/patches/temporal-mapping.patch` | `0b972736620c58137a66acefe947d3fd8f4ddde524fae4cd3c3719741e29b021` |
| 同目录 `mapping-manifest.json` | `bf8ded5959060658d66053aac99814388b730d142c28562d48746df652e73336` |

实读完整175项after hash与manifest一致，其余174文件含产品/项目/锁与v3完全相同。Low核新执行闭包身份不变后可复用原依赖证明，不重force-evaluate；须实际重跑原51项新测试及尚未执行的后续回归。

保留v3 execution summary SHA `2ddb1d7e67a9e662fbac801b9ecfe4b61933362fe613098c37527388caf188ca`、失败TRX SHA `86e6ceae2d6a2f9852fc50557731f87e8875e8cfcbda920c3f15e91952e50548` 及triage SHA `8fa499118bad6e2cf807b8382b4e1d1941f01619cc9d65635d110f1c94edcb5f`。当前只接受修复静态语义，不记新测试通过。完成后停止写入。
