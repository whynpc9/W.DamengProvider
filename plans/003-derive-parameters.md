# F03：安全完整地派生过程参数

状态：实现和可执行验证已完成，提交 `dbab1c83769a402b20f24de29e84dbe9edb381ba`。包内过程实库验证仍为 `integration_pending`。计划基线为 `aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`，无依赖，修复风险为中。

## 完成结果（2026-10-23）

实现了共享的严格标识符解析、常量参数化目录查询、确定性的独立过程/包解析、完整的 Reader 释放、命令修改锁和原子参数替换。函数 `POSITION=0` 的返回值行为有独立覆盖。

CommandTests 在 .NET 6/8/9/10 各通过 212/212，五个目标框架（含 `netstandard2.1`）构建通过。DM 8.1.5.60 上独立过程/函数实库验证 9/9，通过新 TEST 连接确认 4 个对象全部清理。

包内过程的投影和绑定有离线覆盖；包创建及 6 个依赖实库用例因 TEST 缺少 `CREATE PACKAGE` 权限而未运行，没有变更权限。完整命令和证据见[验证记录](verification-2026-10-23.md)。

## 问题与边界

`DmCommandBuilder.DeriveParameters` 原来把名称拼入目录 SQL，在完成发现前清空调用方参数，并可能留下 Reader 未关闭。

`DmCommand.Dispose` 会有意不关闭持有执行租用的 Reader；`DmProcedureCall` 已实现一段/两段带引号标识符解析，应复用其严格规则，不放宽执行解析。

`DmSchema.Definitions` 使用 `ALL_OBJECTS` 和 `ALL_ARGUMENTS` 查询独立对象。函数返回值是 `POSITION=0`，即使名称为 `V_RET` 且方向为 OUT；普通过程的首个参数也可能是位置 0。现有两段包处理路径必须保留。

## 修改范围

- `src/W.DamengProvider/Internal/Legacy/Dm/DmCommandBuilder.cs`
- `src/W.DamengProvider/Internal/Execution/DmProcedureCall.cs`
- `src/W.DamengProvider/Internal/Execution/DmDerivedParameters.cs`（需要拆分时新增）
- `src/W.DamengProvider/PublicApi/DmParameterCollection.cs`
- `tests/W.DmProvider.CommandTests/DeriveParametersTests.cs`（新增）
- `tests/W.DmProvider.CommandTests/StoredProcedureBindingTests.cs`（仅必要的解析回归）

## 实施与验证要求

1. 所有目录名称和对象 ID 使用参数绑定；非法标识符在目录 I/O 前拒绝。
2. 完整关闭目录 Reader，全部元数据转换和校验成功后一次性替换参数；失败保留原集合及其归属。
3. 覆盖独立过程、函数、包成员、无参、带引号名称、重载/歧义、缺失对象、返回值位置和失败后同连接复用。
4. 先运行新增及 CommandTests，再运行现代目标框架矩阵并构建 `netstandard2.1`。
5. 实库只使用 TEST 和本轮唯一对象，由新连接确认精确清理；缺少包权限时如实记录 `integration_pending`，不扩大授权。
