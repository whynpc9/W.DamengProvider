# TP05 关键静态审阅 v1

2026-10-08。结论：**changes_requested / R01 编译阻塞**。本轮只读对照 TP03-v6 冻结树、TP05 design/v6 brief、已交 descriptor 与 codec bridge 接口；未运行 build、tests、validator 或数据库，未修改产品或测试。不能将静态计划355/10计为通过。

## R01：DATE fixture 调用不存在的构造函数

`tests/W.DmProvider.TypeTests/TemporalNativeReaderContractTests.cs:418` 的 `Wire` 在 DATE14 分支使用 `new(2026, 7, 23)`，表达式目标类型为 `DmTemporalFields`。实际 `DmTemporalFields` primary constructor 要求 Year、Month、Day、Hour、Minute、Second、NanosecondsWithinSecond 七个必填参数，仅 OffsetMinutes 有默认值。该三参数调用无法编译，阻断整个 TypeTests 候选。

由 reader High 在 root 新窄窗中改为显式午夜字段 `new(2026, 7, 23, 0, 0, 0, 0)` 即可；不要修改已接受 pure core 的构造合同来迁就测试。保留本轮失败源码哈希与报告，修复后交新哈希定点复核，再整体冻结给 Low。这里是源码可确定的编译错误，未声称已取得编译器日志。

## 其余关键审阅结论

- DmGetValue 两构造入口保留兼容签名，旧入口标记 Unknown；新入口保存当次实际格式。Reader 的两个构造函数及 ApplyResult 均从本次 `info.rsBdta` 建立新 DmGetValue，无跨 result 沿用旧格式。每次读取获取按值 descriptor，raw 未观测 format 与实际 row format 分离。Unknown/ordinary12/TLTZ 的拒绝由 codec 接口执行，不猜默认 ordinary 或 legacy12 单位。
- GetString 在原 NULL6081 检查之后读取原始 ns；默认格式直接 FormatExact，合法 scale8/9 的非整 tick 值不先投 CLR。显式模板选择与旧 `connection.getFormat(column)` 按 CType 的分派一致，仍由 OracleDateFormat 执行自身显示投影；Oracle-date mask 默认日期显示保持。DateUtil 仅将原小数格式化改为严格 ns/scale 校验及 invariant 补零，没有修改其它日期显示 helper。
- typed DateTime/DTO、generic exact cast、provider 返回形态、TIME mode 与 interval mode 路由保持。typed DTO 对 TLTZ 仍因原类型分派 InvalidCast；generic 先 GetValue，非 NULL TLTZ 能力 NotSupported 可先于 cast。NULL 路径在能力门前，schema 不应用值能力门。
- DB2N temporal 新重载消费明确 actualFormat，旧签名为 Unknown wrapper；现有非时间支路保持。toDate/toTime 的整数投影与 v6 helper 合同相同，TZ toObject 仍产生 DmDateTime 语义对象并保留 ns/offset。INTERVAL DT 从24字节 payload 的完整 packed 声明读取 leading/fraction，避免默认 constructor 的 leading2/sec6 覆盖实际声明；公共 Reader interval 对象映射没有改写。
- schema 的 temporal NumericScale 使用 descriptor.FractionalScale；interval YM 为0，DT 为实际小数精度，raw/完整 packed 字段仍在内部记录。NumericPrecision/GetPrecision、ColumnSize/GetSize 保持；DataType 给 scale-aware CTypeToSystemType 传入完整 GetScale，包括1574，不传解出的fraction6。非 temporal NumericScale 保持原支路。packed→fraction 是已批准的公开可观察修正。
- 对照 v6，Reader 变更只有三个实际格式接线位置与 schema 两处；Begin/Complete/TranslateFailure、token/取消优先级、lease、LOB、向前 ordinal、Read/NextResult/Dispose/CloseConnection 协调逻辑未改动。未发现新增成功诊断或把转换失败改成连接故障的旁路。

## 测试与证据边界

静态展开新 Type 测试为355、新 Async为10：99正常格及NULL格、54 generic async及NULL格，另含高精度/范围/schema/formatter/DB2N/恢复等附加用例。期望 CLR 值/精确字符串单独写出；packed row 的生成使用已接受 codec，因此这些新 fixture 是 Reader 合同覆盖，不代替 TP03 独立 wire oracle。

已实读 coverage：原矩阵 SHA pin 匹配，99个 case ID 全部唯一，99+24项均 `not_run`，引用的测试方法存在，executed_count=0、candidate_acceptance=false、tp05_complete=false。TLTZ 正向未开放与当前 typed DTO 裁决分别标明，没有覆盖原始 matrix_target。

普通同列精确 GetString、Sequential 向前读取及拒绝倒读、下一缓存行、inline CLOB、预取消原 token、同步/异步 Dispose、CloseConnection 的测试具有明确断言；ApplyResult fixture 调用实际内部发布方法，覆盖 ordinary→BDTA→ordinary 的格式/metadata 替换。它没有经过 public NextResult 的完整协议交互，也没有真实跨页或 in-flight cancel；coverage 正确保留同候选既有 R3 分页/取消/清理与实库门待验，不据缓存 fixture 宣称这些运行事实。

R01之外未发现本轮范围内其它静态阻塞。编译和运行结果、现有 Type/Command/Async/R3 回归、同候选真实服务端 Reader/schema 验收均待新freeze后的 Low 窗口。

## 本轮冻结身份

以下八项实读 SHA256 均与 root 派发值一致；这组身份是 R01 的历史输入。

| 文件 | SHA256 |
| --- | --- |
| `src/W.DmProvider/Internal/Legacy/Dm/DmGetValue.cs` | `fceab580043fe48c18d917e05ceddb58b299980d2be11214c172e7983f0bfbfc` |
| `src/W.DmProvider/Internal/Legacy/Dm.util/DB2N.cs` | `2154d32dfeb958dad6d049f60c7a3443bb649fa4894eae29b89b023bfa626e21` |
| `src/W.DmProvider/Internal/Legacy/Dm.util/DateUtil.cs` | `c2ddedd0b9ed897ad165e23aab7c82e03d0a1372fbd8155234a5ef13782eb668` |
| `src/W.DmProvider/PublicApi/DmDataReader.cs` | `5a0aca8447e323701074ab6be0ea8d17e28e123b48325fcc6ad11e199a94cecd` |
| `tests/W.DmProvider.TypeTests/TemporalNativeReaderRouteTests.cs` | `e8340f56083c9cae5832407ecfc8e35d61b4520de1bfca2aef648814352bd0d4` |
| `tests/W.DmProvider.TypeTests/TemporalNativeReaderContractTests.cs` | `949198126a1f670a2fdfe988b48bae361631e3b360342f0ffd2df9850738ba48` |
| `tests/W.DmProvider.AsyncTests/TemporalNativeReaderAsyncContractTests.cs` | `a2d6a5926cde728db5ffe504cc701330545e50077c694af67b1dee2e503d1534` |
| `docs/implementation/temporal-native/TP05-reader-coverage.json` | `1383ab8736b69e948fbbab2f19b50be10c030f6d1525ac3618d7442adb398802` |

本报告完成后停止写入；TP04 selection/decoder 的整体审阅由指定 reviewer负责，本轮未重复该范围。
