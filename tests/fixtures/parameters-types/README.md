# 参数与类型测试场景

`scenarios.json` 分为合成 `cases` 和 `real_database_probes`，遵循[类型说明](../../../docs/compatibility/T09-types.md)。合成用例为 `not_run`，实库过程为 `planned_not_run`，不包含协议字节或预设服务器响应。

## 无损值表示

`UInt64`、有符号边界 tick、decimal 系数和超出 `System.Decimal` 的值都使用十进制字符串。`decimal(38,20)` 使用精确系数字符串和整数 scale，不经过 JSON number 或 `double`。时间值使用带七位小数和明确偏移的 ISO 字符串。孤立代理以 UTF-16 hex `D800` 表示，保证 JSON 本身为合法 UTF-8。

UTF-8 向量包含中文、补充平面 emoji 和组合序列，UTF-16 单元数与编码字节数分开。GB18030 使用独立中文文本，不假设两种编码能表示相同字符。

## 覆盖范围

- TYP-01/02：enum/Flags 底层值、byte/ulong 枚举、符号边界、溢出和非整数数值转换拒绝。
- TYP-03：`decimal(38,20)` 系数/scale、极小符号值、尾零、超出 CLR decimal 精度和显式 scale 溢出不舍入。尾零 scale 只测试本地 `DmDecimal` 解析/格式化；DECIMAL 列读回仍由服务器环境验证。
- TYP-04/05：GUID 形状的 CHAR/VARCHAR(36) 默认仍为文本，只有显式 `GetGuid` 才转换；Unicode 长度、严格编码回退，不用 `?` 替换。
- TYP-06/07：CLR TimeSpan tick 与服务器 INTERVAL 范围分开；DateOnly/TimeOnly、Unspecified DateTime、DateTimeOffset、typed/untyped NULL、空串和空字节数组。
- TYP-08/09：参数标记匹配/重用、重复规范化、命名/位置拒绝、类型来源优先级、类型/Size 导致 Prepare 失效、ResetDbType、同步/异步一致和复杂类型拒绝。

服务器时间和 decimal 结果取决于已验证环境：可表示范围必须精确往返，不可表示范围必须拒绝且不舍入。CLR 解析/构造与服务器验证分开。合成 GetString 只保留可 invariant-parse 的文本，不虚构 INTERVAL 协议或字符串格式。

`ARRAY`、`CLASS`、`GEOMETRY`、`XDEC` 在这些计划中保持明确不支持；当前存储过程纯输出 XDEC 的单独范围见[存储过程说明](../../../docs/compatibility/I09-stored-procedures.md)。实库过程只使用 TEST 和本轮唯一对象。准备 fixture 未构建、连接数据库或编码协议值。
