# 连接串与配置选项

本文说明当前配置解析、单位和默认值。TLS、超时和池化行为分别见[TLS 安全](T07-security.md)、[异步和取消](R2.md)、[数据源与 LOB](R3.md)。

## 支持的连接键

| 标准键 | 旧键或别名 | 输入与默认值 | 状态 |
| --- | --- | --- | --- |
| `server` | `data source`, `host` | Unicode 主机；`host:port` 和 `[IPv6]:port` 会拆分端口，裸 IPv6 不推测末段为端口；默认空 | 支持 |
| `port` | 无 | 1–65535；默认 5236 | 支持 |
| `user` | `userid`, `user id`, `username`, `user name`, `uid` | 原样字符串；默认空 | 支持 |
| `password` | `pwd` | 原样字符串；默认空；诊断输出省略 | 支持 |
| `schema` | 无 | 独立于 catalog；`null`/空串清除。按 .NET `char` 长度限制 128，拒绝 NUL/CR/LF；这不是服务端字节上限声明 | 支持 |
| `language` | 无 | `cn/en/cn_hk/cn_tw` 或 0–3；默认 0 | 支持 |
| `connect_timeout` | `connect timeout`, `connectiontimeout`, `timeout`, `connectTimeout` | 毫秒，内部为 `TimeSpan`；默认 5000 ms | 支持 |
| `command_timeout` | `sessionTimeout`, `commandtimeout`, `session_timeout` | 秒；默认 30，0 表示不限时 | 支持 |
| `conn_pool_timeout` | `connpooltimeout` | 毫秒，内部为 `PoolAcquireTimeout`；默认 5000 ms | 支持 |
| `socketTimeout` | `socket_timeout` | 毫秒，内部为 `ReadIdleTimeout`；默认 0 | 支持 |
| `cleanup_timeout` | `cleanuptimeout` | 毫秒，必须大于 0；默认 5000 ms | 支持 |
| `transport_security` | `transportsecurity` | 默认 `RequireTls`，或显式 `PlaintextAllowed` | 支持 |
| `persist_security_info` | `persistsecurityinfo` | 布尔值；默认 `false` | 支持 |
| `initial_catalog` | `initial catalog`, `initialcatalog` | 空值可清除；非空值拒绝，绝不映射到凭据 | 不支持 |
| `max_message_size` | `maxmessagesize` | 仅允许默认 64 MiB | 固定值 |
| `max_materialized_lob_size` | `maxmaterializedlobsize` | 仅允许默认 64 MiB | 固定值 |
| `lob_chunk_size` | `lobchunksize` | 仅允许默认 32 KiB | 固定值 |

强类型 `ConnectTimeout`、`PoolAcquireTimeout`、`ReadIdleTimeout`、`CleanupTimeout` 属性使用 `TimeSpan`，只接受完整毫秒，且必须能用 32 位毫秒表示。旧 `ConnectionTimeout`、`ConnPoolTimeout`、`SocketTimeout` 的 `int` 签名仍使用毫秒。`DmConnection.ConnectionTimeout` 标准属性按非零毫秒向上取整为秒。

## 旧键和不支持的值

旧 `DmOption` 中其余已知键均为弃用或不支持。只有与当前安全默认值完全相同的显式值可以解析；非默认值和未知键一律拒绝。

常见禁用键包括：

`stmt_pooling`、`pstmt_pooling`、`enable_rs_cache`、`enlist`、`rwSeparate`、`rwHA`、`compress`、`svc_conf_path`/`dm_svc_conf`、`log_dir`、`log_level`、`addressRemap`、`useSkyWalking`。

旧日志目录和服务文件默认路径已改为空，驱动不会加载环境服务文件。`conn_pool_size=0`、`escape_process=false` 等不同于旧默认的值也会拒绝，不会先接受再静默忽略。

## Builder 赋值与失败行为

Builder 构造和 `new ConnectionString` setter 会先在临时对象中验证，失败时保留原配置。

需要注意以下基类行为：

- `DbConnectionStringBuilder.ConnectionString` 在 .NET 10 中不是虚成员。通过基类引用赋值时，会逐项调用虚索引器。
- 同义键重复时，索引器只接受等价值。要修改值，请先 `Remove` 再赋值，或使用强类型属性。
- 索引器直接赋值失败后，Builder 会进入不可消费状态；需要 `Clear` 或一次成功的自身字符串赋值。仅 `Remove` 不会解除该状态。
- 基类赋值遇到 `ArgumentException`/`ArgumentOutOfRangeException` 时，框架实测可回滚原状态；`NotSupportedException` 路径可能留下部分状态，因此不能把基类 setter 当作原子操作。

框架基类解析器还会在到达虚索引器前忽略 `connect_timeout=` 这类空值，派生类无法识别。Builder 自身构造、自身 `new ConnectionString` setter 和 `DmConnectionSettings.Parse` 使用保留空值的有序解析器，会拒绝空数值设置。需要严格验证时，应使用这些入口。
