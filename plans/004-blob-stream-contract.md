# F04：修正公开 BLOB Stream 行为

状态：已完成，提交 `7d8f071e5da11fb8c2e3e84a05cecaeebfb68dc4`。计划基线为 `aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`，无依赖，与 F01 协调验证，修复风险为中。

独立审查通过；LobTests 在 .NET 6/8/9/10 各通过 322/322，每个运行时包含 12 个公开 Stream 具体测试。

## 问题与证据

`DmBLobStream` 存在多个公开行为错误：`Position` setter 忽略输入、`ReadTimeout` 递归赋值自身、`Seek` 使用一基或钳制位置、`ReadByte` 在 EOF 空结果上取索引、`Dispose` 不关闭。部分 Read/Write 参数错误还可能在 LOB 副作用之后才出现。

## 修改范围

- `src/W.DamengProvider/Internal/Legacy/Dm/DmBLobStream.cs`
- `tests/W.DmProvider.LobTests/LegacyBlobStreamContractTests.cs`（新增）

## 实施方案

1. 使用零基 `Position`/`Seek`，检查溢出和负数，修正 EOF 与同步释放；在 LOB 操作前验证缓冲区和范围，只在成功传输后推进位置。
2. 能力属性反映关闭和只读状态。超时处理不得递归，也不能假装支持无效超时；没有已证明的有效路径时，使用 `CanTimeout=false` 和标准不支持的 timeout 属性。
3. 写入立即生效时，`Flush` 为经检查的无操作。流与底层 LOB 的生命周期分开。
4. 不静默实现大型稀疏分配：未支持的扩展或空洞写入要明确拒绝，同时支持安全的追加、覆盖、截断和 EOF 定位/读取。
5. 使用真实本地 `DmBlob` 流测试 Position、所有 SeekOrigin、空/非空 EOF、无效参数不改变状态、写入/追加/截断、扩展拒绝、只读能力、重复 Dispose/Close 和 timeout 属性。复用 `newInstanceOfLocal`，无需数据库；只有需要证明非法输入零 I/O 时才增加远端 fixture。

## 验证要求

先运行新增测试，再运行 `net10.0` 完整 LobTests，最后执行协调后的目标框架矩阵。使用任务专用 CLI home、禁用遥测和仓库统一的单节点参数，共享构建串行安排。断言真实数据和位置变化，不只看字段 setter。
