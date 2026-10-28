# 产品身份测试数据

`contract.json` 记录来自 `docs/implementation/T03-design.md` 和 S02 的身份约定。十个 `core_types` 是最初公开 API 集；`DmDataAdapter` 单列为兼容类型，出现不代表行为已支持或验证。该文件来自源码设计，不表示打包或验收已通过。

`resource_value_sha256` 记录 `message.error` 的官方观察值，来源为 `docs/implementation/evidence/T02-validation.json` 中的 `official_observations.resources`。证据文件 SHA-256 为 `7fe8520b9b030395214601fac3918a7745182cb6e9a9a859d6f95ff42e071919`。

文化集合固定为 `neutral`、`en`、`zh-CN`、`zh-HK`、`zh-TW`，每个 `value_sha256` 都是非空小写 64 位十六进制摘要。官方资产固定为 `DM.DmProvider 8.3.1.47463`，SHA-256 为 `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b`。这些预期来自官方基线，不是从 W 资源生成。

`smoke-cases.json` 包含合成 Unicode 字符串及其 UTF-8 字节的 SHA-256，由 Python `hashlib.sha256(value.encode("utf-8"))` 独立计算。它们只是测试数据，不需要数据库或凭据。
