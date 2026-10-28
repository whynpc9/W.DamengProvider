# F05：DbBatch Reader 保留驱动特定值

状态：已完成，提交 `3730caaab829e62f9757db290d42961a881f8893`。计划基线为 `aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`，无依赖，修复风险低。

## 完成结果（2026-10-23）

将三个驱动特定读取 API 转发到当前内部 Reader。测试使用实际批次包装器、`DmDataReader` 和原生 decimal codec，覆盖精确 38 位 `DmDecimal`、`DBNull`、数组、同步/异步结果切换、普通 decimal 溢出和 Reader 状态保护。

独立审查通过；CommandTests 在 .NET 6/8/9/10 各通过 212/212，五个目标框架构建通过。完整证据见[验证记录](verification-2026-10-23.md)。

## 问题与证据

`DmBatchDataReader` 转发了普通读取，却遗漏：

- `GetProviderSpecificFieldType`
- `GetProviderSpecificValue`
- `GetProviderSpecificValues`

`DbDataReader` 的默认实现会改用普通值，可能丢失 `DmDecimal` 的完整 38 位精度或抛溢出。`DmDataSourceCommand` 已有直接转发实现可参照。

## 修改范围

- `src/W.DamengProvider/Internal/Execution/DmBatchDataReader.cs`
- `tests/W.DmProvider.CommandTests/BatchProviderSpecificTests.cs`（新增）

## 实施与验证要求

1. 将三个方法转发到当前内部 Reader，保留现有 Reader 状态检查。
2. 基于 ArrayBindingAndBatchTests fixture，使用真实批次 Reader 和原生 decimal codec，覆盖 38 位精确 `DmDecimal`、`DBNull`、数组数量及内容、结果切换和 `GetFieldValue<DmDecimal>` 一致性。普通 decimal 溢出行为不变。
3. 先运行新增测试，再运行完整 CommandTests 和目标框架矩阵；五个目标框架均需构建通过。使用任务专用 CLI home 和仓库统一的单节点参数，共享构建串行安排。
