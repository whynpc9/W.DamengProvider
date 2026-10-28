# 当前连接生命周期性能基线

此工具通过 ProjectReference 只测试本仓库 W 驱动，不测官方驱动或恢复版。

当前每次租用都新建物理会话，归还时销毁。本工具只建立性能基线，不验证会话重置，不证明物理复用，也不表示 issue #6 的性能目标已完成。

## 构建与运行

从仓库根目录使用 .NET 10 SDK，并与其他共用 `bin/obj` 的构建串行执行：

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

参数格式：`offline|shared|tls [samples=100] [workers=4] [warmup=5]`。

- 每场景样本数 2–10000。
- worker 数 2–32，且不能超过样本数。
- 预热租用数 1–100。
- `shared` 分别测 DataSource 和 direct `Pooling=true`，各执行串行和固定 worker 并发。
- `tls` 只测 DataSource，同时保留 direct pooling 使用显式证书文件时在 socket 创建前拒绝的检查。

## 实库安全与配置

实库配置只来自对应 wrapper。认证前检查用户名，每次连接的第一条查询检查 USER 和当前 Schema。所有 SQL 都是只读 SELECT，不创建对象。

- `shared` 显式 `PlaintextAllowed`，核对协商 mode 0。
- `tls` 只使用本机 `127.0.0.1:15236`、既有客户端证书和 `RequireTls`，核对 mode 1 及每次 TLS 升级。
- 仅本机无 CRL 的 CA 显式 `NoCheck`，不关闭证书验证，也不修改产品默认值。

TLS 实例可用性和许可按[本地 TLS 环境](../../docs/implementation/local-dameng-tls.md)确认，工具不管理实例。

## 测量口径

JSON 记录实际加载 DLL 路径/MVID、数字服务器版本、可读取时的 COMPATIBLE_MODE、样本数和安全错误分类。

- 预热不计入延迟和吞吐量。
- OpenAsync 延迟使用 nearest-rank p50/p95/p99。
- 吞吐量覆盖完整 Open + 身份 SELECT + Close。
- worker 同时释放起跑门，真实 await 异步 Open/Execute/Read/Close，不在完成后拼接任务。
- 池容量等于配置的并发数，不测容量不足时的排队延迟。

COMPATIBLE_MODE 通过专用连接只读查询 `V$DM_INI`。明确的服务器错误会记录错误号和 `compatible_mode: null`，该连接立即结束且不重用；后续测量使用重新验证身份的新连接。传输、超时和取消仍使运行失败。

Created/Disposed/Connected 是内部只读计数器增量。Connected 只表示 TCP 建连成功，不代表认证成功；每个测量租用另做身份查询。最后检查池的 Creating/Leased/Closing/Waiting/Idle/Resetting 全部为零，且 socket 创建/关闭相等。

工具没有写库操作，因此最终状态只记录身份/Schema 确认和未创建对象，不清理其他会话的对象。

## 离线与退出码

`offline` 不读取实库配置、不创建 socket，只检查 Closed 构造和显式证书 direct pooling 拒绝；成功仍输出 `integration_pending`，没有测量数据。

- 0：请求模式成功，包括 offline。
- 3：实库模式缺少 TEST 配置。
- 1：其他失败。

保存调用命令、JSON 和 shell 退出码。失败不重试，不输出原始异常或连接配置。测量结果只描述本次环境和负载；后续复用候选必须在相同环境和参数下重新比较。
