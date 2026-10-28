# 时间精度独立预期数据

`expectations.json` 提供 TP-02 纯整数和 CLR 时间规则的确定性输入与预期结果。每个用例都有唯一 ID、`expected_only: true`、`status: "not_run"` 和 `server_evidence: false`，尚未对驱动或服务器执行。

设计依据为 `docs/implementation/temporal-dotnet-precision-plan.v2.md` 第 4、7 节及 `docs/implementation/temporal-native/TP00-astra-design.md` 第 2、3 节。文件不含凭据、数据库输出或伪造服务器字节。`layout_candidate` 只表示目标逻辑布局，不代表字段来自服务器或已经组成协议包。8/9 位输入直接使用整数纳秒，不先构造 `DateTime`。

## 覆盖范围

- CLR `DateTime` 端点、相邻 tick、最后一位数字 0–9，以及闰年和世纪边界的公历转换。
- 使用固定 seed `20261008` 和 JSON 中记录的 64 位 LCG 生成 CLR 全域样本，不依赖 Python hash 随机化或运行时 PRNG。
- Scale 0/3/6/7 的 tick 单位；用户 Scale 未设置/0/3/6/7/8/9/19/23/255；Precision 未设置/0/7/9。范围内的 Scale 用例只断言数值范围，不表示类型、协议布局或服务器环境支持 7/8/9。
- Scale 8 的原始纳秒 10、90、100、非法 1，以及 Scale 9 的 1、99、100。`expected.format` 只含声明的小数位；不能整除 100ns 时 CLR 投影应溢出。Scale 8 非法原始值的异常类型未在设计中确定，保持未决。
- `DateTimeOffset` 偏移 0、+480、-300、+330、-840、-780、-779、+840 分钟，以及 CLR 构造失败和本地/UTC 范围失败。W 写入保护范围 `[-779, 840]` 是当前源码事实，不是服务器全域限制。
- `TimeOnly` 最后 tick、逻辑 TIME hour 24、含零和 Int64 端点的正负 `TimeSpan`，以及 INTERVAL leading precision。
- 旧公开 12 字节微秒单位与内部 100ns tick、原始纳秒的关系。公开缓冲区字段只是逻辑值，不虚构 12 字节 golden；非整微秒公开投影拒绝，内部七位 tick 保持精确。

`ticks`、`fractionalTicks`、`localTicks`、`utcTicks` 均为十进制字符串，避免 JSON 数值舍入；纳秒、Scale、偏移和日历字段使用带单位的 JSON 整数。

## 重新生成

```sh
python3 tests/fixtures/temporal-native/generate_expectations.py
```

生成器只使用 Python 标准库，不访问网络、数据库、.NET 或凭据。历史官方协议 golden 保留在原测试中，不在此重新生成或改标。
