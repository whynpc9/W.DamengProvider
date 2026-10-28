# F01：保留远端 LOB 截断长度

状态：已完成，提交 `85bb32cc9e2163c0ee5a93fb99bfdbc001fa9658`。计划基线为 `aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`，无依赖，修复风险为中。

独立审查通过；LobTests 在 .NET 6/8/9/10 各通过 322/322。最初畸形 ACK 测试预期了错误异常类型，随后按既有 `InvalidDataException` 行为修正并重跑，没有删除状态断言。

## 问题与证据

`DmBlob.TruncateOwned` 和 `DmClob.TruncateOwned` 在调用 `T02_02000091.A` 前把 `long` 长度转成 `int`。`LOB_TRUNCATE` 本身已经保存 `long`，因此 `2^32` 可能在编码前变成 0。成功截断后，只有在确认响应到达时才能更新长度和 locator。

## 修改范围

- `src/W.DamengProvider/Internal/Legacy/Dm/DmBlob.cs`
- `src/W.DamengProvider/Internal/Legacy/Dm/DmClob.cs`
- `src/W.DamengProvider/Internal/Legacy/A/T02_02000091.cs`
- `src/W.DamengProvider/Internal/Legacy/Dm/T02_0200008C.cs`
- `tests/W.DmProvider.LobTests/TruncateWidthTests.cs`（新增）

## 实施方案

1. 跟踪新旧协议写入路径，确认实际有符号/无符号边界，并在整个调用链保留 `long`。
2. 旧协议无法表示的长度在发送或修改 LOB 状态前拒绝；保留负数、只读检查和确认响应后的基准更新。
3. 复用 `OutputLobFixture` 模式，覆盖 BLOB/CLOB 边界值、两种协议环境、无需分配大 LOB 的 `2^32` 回归、拒绝时零请求/零状态变化，以及后续合法使用。

## 验证要求

先运行新增测试，再运行完整 LobTests；最终覆盖已安装的现代目标框架，并构建 `netstandard2.1`。构建前设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`，使用仓库统一的单节点参数。共享构建串行安排。

无需实库：该回归可以通过合成 writer 和 LOB 状态验证。完成条件包括编码长度正确、非法输入在发送前拒绝、原状态不变，以及目标框架矩阵通过。
