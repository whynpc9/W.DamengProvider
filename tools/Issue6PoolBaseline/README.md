# Issue 6 当前连接生命周期基线

只测本仓库 `ProjectReference` 的 W 驱动，不测官方驱动或恢复版。当前实现每次租用均新建物理会话，归还即丢弃；这份基线不验证 session reset、不证明物理复用，也不宣称 Issue 6 的性能目标完成。

从仓库根目录运行，使用 .NET 10 SDK。构建和其他共享 bin/obj 构建串行安排：

```sh
export DOTNET_CLI_HOME="$PWD/.local/issue6-dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$DOTNET_CLI_HOME"
dotnet build tools/Issue6PoolBaseline/Issue6PoolBaseline.csproj --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false
dotnet tools/Issue6PoolBaseline/bin/Debug/net10.0/Issue6PoolBaseline.dll offline
scripts/with-dameng-test.sh dotnet tools/Issue6PoolBaseline/bin/Debug/net10.0/Issue6PoolBaseline.dll shared 100 4 5
scripts/with-dameng-tls-test.sh dotnet tools/Issue6PoolBaseline/bin/Debug/net10.0/Issue6PoolBaseline.dll tls 100 4 5
```

参数为 `offline|shared|tls [samples=100] [workers=4] [warmup=5]`。每场景样本数 2–10000，固定并发 worker 数 2–32 且不超过样本数，预热租用数 1–100。`shared` 分别测 DataSource 和 direct `Pooling=true`，每种模式分别执行串行和固定 worker 并发；`tls` 只测 DataSource，保留 direct pooling 显式证书文件在 socket 创建前拒绝的合同。

实库仅从对应 wrapper 提供的唯一 TEST 连接变量取配置；认证前检查用户名，每次连接的第一条查询检查 USER 和当前 Schema。所有 SQL 都是只读 SELECT，不创建对象。shared 显式 `PlaintextAllowed` 并核对协商模式 0；tls 仅使用本机 `127.0.0.1:15236`、既有 `.local/t07/certs/client_ssl/WDM_PROVIDER_TEST/` 证书和 `RequireTls`，核对模式 1 及每次 TLS 升级。仅本机无 CRL 的 CA 显式 NoCheck，不关闭证书验证、不修改产品默认值。TLS 实例可用性与许可按[本地 TLS 环境](../../docs/implementation/local-dameng-tls.md)确认，工具不管理实例。

JSON 输出记录实际加载 DLL 路径/MVID、数字服务器版本、可读取时的 COMPATIBLE_MODE、样本数及安全错误分类。每个场景预热不计入延迟和吞吐量；OpenAsync 延迟用 nearest-rank p50/p95/p99，吞吐量覆盖完整 open + 身份 SELECT + close 租用。并发 worker 同时释放起跑门，真实 await 异步 Open/Execute/Read/Close，不用完成后串行拼接任务。池容量等于配置的并发数；此基线不测容量不足时的排队延迟。

COMPATIBLE_MODE 通过专用 profile 连接只读查询 `V$DM_INI`。若该可选元数据查询明确返回服务器错误，记录安全错误编号和 `compatible_mode: null`，立即结束并释放这条连接，不重试；后续基线使用全新且重新验证身份的连接，不继续使用可能已经失效的 profile 会话。传输、超时、取消等失败仍使本次运行失败，元数据不可用时不推测其值。

Created/Disposed/Connected 是内部只读观察计数器的增量，Connected 对应 TCP 建连成功，不能替代认证成功。每个测量租用另外经过身份查询。输出分别包含预热、测量和释放后的 socket 计数；最后在 DataSource Dispose 或对应 direct ClearPool 后报告 Creating/Leased/Closing/Waiting/Idle/Resetting，检查全部为零及 socket 创建/关闭相等。全局计数还包括测量外的服务器 profile 与最终独立身份确认连接。工具没有写库操作，因此最终服务器状态记录为身份/Schema 确认、未创建对象；不检查或清理其他会话的对象。

offline 不读取实库配置、不创建 socket，只检查 Closed 构造与显式证书 direct pooling 拒绝，成功仍输出 `integration_pending`，没有测量数据。退出码：0 为请求模式成功（含 offline），3 为实库模式缺少 TEST 配置，1 为失败。保存调用命令、JSON 和 shell 退出码作为证据；失败不重试、不输出原始异常或连接配置。测量结果只描述本次环境与负载，后续复用候选须在相同环境和参数下另行比较。
