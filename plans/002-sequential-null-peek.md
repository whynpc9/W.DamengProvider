# F02：顺序读取判空不消费当前值

状态：已完成，提交 `bdd82490592ef1cec40459b938e39a21b50ff364`。计划基线为 `aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`，无依赖，修复风险为中。

独立审查通过；TypeTests 976/976、LobTests 322/322 在 .NET 6/8/9/10 各通过。

## 问题与证据

`DmDataReader.do_IsDBNull` 及异步路径调用 `GetByteArrayValue` 时使用会消费值的默认参数。在 `SequentialAccess` 下，这会推进列序号，导致常见的 `IsDBNull(i)` 后再读取同列类型值或流失败。

## 修改范围

- `src/W.DamengProvider/PublicApi/DmDataReader.cs`
- `tests/W.DmProvider.TypeTests/SequentialNullCheckTests.cs`（新增）
- `tests/W.DmProvider.LobTests/SequentialNullCheckTests.cs`（新增）

## 实施方案

1. 阅读 `GetByteArrayValue` 和活动 LOB 流的生命周期，增加同步/异步共用的非消费判空路径。
2. 保持只进规则：检查后面的列后仍不能返回前面的列；同列判空不能回退或释放已部分读取的 LOB 流。保留取消、Reader 关闭、非法序号和资源归属检查。
3. 复用 ReaderReviewTests 和 LOB fixture，覆盖 NULL/非 NULL 标量与文本、重复判空、同步/异步判空后的 GetFieldValue/GetStream/GetTextReader、部分流读取和后续列回退拒绝。使用真实 Reader 状态，不只 mock helper。

## 验证要求

先运行新增测试，再在 `net10.0` 运行完整 TypeTests 和 LobTests；最终覆盖现代目标框架并构建 `netstandard2.1`。构建使用任务专用 CLI home、禁用遥测和仓库统一的单节点参数，共享构建串行安排。

这是确定性的 Reader 状态回归，不需要数据库修改。完成条件为判空不消费当前值，同时不放松只进和生命周期规则。
