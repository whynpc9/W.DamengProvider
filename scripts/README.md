# 本地 TEST 配置入口

这些 wrapper 只为子命令加载本机 Git ignored 配置，不构建、不打包，也不发布。不要把 secret 文件放进 CI 或制品。

| 入口 | 用途 |
| --- | --- |
| `with-dameng-test.sh <command> [args...]` | macOS/Linux shared TEST；加载 `.local/secrets/dameng-test.env` 中的 `DAMENG_TEST_CONNECTION_STRING`，要求专用 TEST 账号。 |
| `with-dameng-test.ps1 <command> [args...]` | Windows TEST；按数据解析同一配置格式，运行后恢复进程环境。额外的临时 TEST 身份仅用于 [I11](../docs/compatibility/I11-windows.md) 规定的隔离验证。 |
| `with-dameng-tls-test.sh <command> [args...]` | 隔离本机 TLS TEST；要求 0600 的 `dameng-tls-test.env`，只连接 `127.0.0.1:15236`。 |

wrapper 的配置检查不能代替服务器身份核验；实际命令须先确认 `WDM_PROVIDER_TEST` 用户及同名当前 Schema，再做业务操作，并精确清理本次创建的对象。明文测试须显式选择 `PlaintextAllowed`，TLS 测试须使用 `RequireTls` 并验证证书。

多目标发布包的 shared/TLS 串行 smoke 命令见[发布指南](../docs/release.md#本地实库检查)。配置与证书要求见[本地达梦环境](../docs/implementation/local-dameng.md)及[隔离 TLS 环境](../docs/implementation/local-dameng-tls.md)。缺少配置时记录 `integration_pending`，不要加载管理账号或自动初始化实例。
