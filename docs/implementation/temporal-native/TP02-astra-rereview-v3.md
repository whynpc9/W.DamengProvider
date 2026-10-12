# TP-02 v3 Astra 定点复审

日期：2026-10-08。结论：`static_accepted / runtime_pending`。已核对四个本轮人工legacy golden与 [独立triage](TP02-astra-triage-v2.md) 的位置整数推导一致，可以交Low重新执行目标及完整TypeTests门。未运行dotnet、数据库或读取secrets；v2的52通过/4失败不改写、不继承为v3通过。

唯一修复文件 `tests/W.DmProvider.TypeTests/TemporalNativeWireTests.cs` 当前SHA256为 `bf41a445ce400f65bd425dad1b88876870c810d4fedad7297843c9f358320980`，与High/root交接一致。四常量为：

| CType | v3人工golden |
| --- | --- |
| 15 | `EE8180C403` |
| 22 | `EE8180C403E001` |
| 16 | `EA87BBEE8180C403` |
| 23 | `EA87BBEE8180C403E001` |

DATE14仍为 `EA87BB`。注释明确date位置整数、time位置整数及fraction低7位 `0x40 << 1 = 0x80`、offset480→E001，且标明这些是人工向量。上述数值由triage独立算术推导而来，不是复制新encoder运行输出。五条case、CType映射、fields及encode/decode双方断言保持；官方extended golden未动。

逐文件SHA回读确认除WireTests外，另九个TP-02源码/测试/项目文件与v2接受输入相同；其中FixtureTests仍为 `6382b6c18c27782085d71c9e1e1dd6c1386826a40feae382ed608dc8112020f5`，R02的分阶段异常边界保持。Luna JSON、generator、README三项与初审完全一致，JSON仍为 `4eee2002106920423c75a2126ac557b96e51bae090ee8e1af2518a179b62a609`。五个产品核心没有修改。

root提供的10文件aggregate为 `bf1dfe2eb4425dd886097e9c025283d4f5dc9a165943582e0344971fbc3b4035`；本轮独立核对逐文件SHA，aggregate依Low冻结manifest规则验证。

同意root释放Sol Low唯一窗口，以新v3 freeze先跑 `FullyQualifiedName~TemporalNative`（静态预计56叶子，包含一个77逻辑case的fixture Fact），成功后同一构建完整TypeTests。使用10.0.203、可写CLI_HOME与单节点/noReuse/noSharedCompilation/disable-build-servers规则；首失败保存并释放窗口。原v1/v2审阅和失败材料继续只读，正式通过以v3 TRX及首尾hash为准。

本报告完成后停止写入；没有新增阻塞问题。
