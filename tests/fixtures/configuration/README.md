# 配置解析测试数据

`cases.json` 提供 [T04 设计](../../../docs/implementation/T04-design.md)和[连接选项](../../../docs/compatibility/T04-options.md)的合成输入与预期断言。准备或检查该文件不会运行解析器、打开 socket 或连接数据库。

端点只使用保留文档名称 `example.invalid` 和 `2001:db8::/32`。账号为 `TEST_SYNTH`，`CANARY_T04` 是脱敏测试的固定合成密码标记；不包含本机连接信息。

## 数据形状

- `parse_success`：连接串和部分 `expected`。预期为公开 Builder 值；超时保留文档单位，Schema 和密码保留原文，`transport_security` 使用枚举名。重复键用例要求等价值可接受。
- `parse_rejected`：预期异常族 `ArgumentException` 或 `NotSupportedException`。存在 `canary` 时，异常文本不得包含它；未知选项用例还要求异常不回显输入键，其值含 canary。
- `unsupported_nondefault`：已知旧选项的非默认值必须明确拒绝。
- `timeout_typed.ticks`：`TimeSpan` tick 数（100ns/tick）；`expected_legacy_milliseconds` 是公开毫秒值，也适用于 `CleanupTimeout`。

覆盖大小写不敏感键和别名、等价/冲突重复值、引号与分号/双引号转义、Unicode 密码和 Schema、带括号 IPv6 和端口冲突、非法数值、InitialCatalog 隔离、脱敏和不支持选项。

连接池容量是 R3 的合法配置；enlistment、HA、读写路由、日志、语句池和结果集缓存仍为负例。`RequireTls` 只表示默认策略，`PlaintextAllowed` 只表示可解析的显式值；这些用例不证明 TLS、异步 I/O 或网络连接成功。
