# Windows 兼容验证记录

2026-10-15，在用户指定的远程达梦实例上执行了 Windows 验证，范围来自 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11)。

实现已随 [0.2.0](../release-0.2.0.md) 发布，但本页测试使用的是当时源码和本地包，**不代表 0.2.0 或当前 0.3.1 正式包已在同一 Windows 环境重新验证**。

## 测试环境与隔离

| 项目 | 实际值 |
| --- | --- |
| 客户端 | Windows 10.0.26200，win-x64，.NET 10.0.9 |
| SDK / MSBuild | 10.0.301 / 18.6.4；本机未安装仓库固定的 10.0.401 |
| 服务器登录版本 | 8.1.5.60 |
| `V$VERSION` | DM Database Server 64 V8；DB Version: 0x7000d；03134284604-20260707-335949-20228；Msg Version: 6；Gsu level(5) cnt: 102 |
| 会话字符集 / 协议协商 | UTF-8 / 21；`SF_GET_UNICODE_FLAG()` 为 1。`V$VERSION` 的 Msg Version 文本与协议协商值分开记录 |
| 传输 | 显式 `PlaintextAllowed`，协商 mode 0；默认 `RequireTls` 的真实 Open 按预期拒绝 |
| 源码 | `24e792d` 加当时 Windows TLS、测试和工具修改 |
| 本地包 | `0.1.1-i11.20261015.24e792d.r2`，精确 PackageReference |

原实例已有 `WDM_PROVIDER_TEST`。根据当次单独授权，新建 `WDM_PROVIDER_TEST_I11_7C42A9D1` 及同名默认 Schema，保留原账号和数据。这里的“独立测试数据库”是现有实例内的独立逻辑空间，没有创建物理实例或表空间。

SA 只用于当次账户创建和七项固定授权：CREATE SESSION、TABLE、INDEX、VIEW、SEQUENCE、PROCEDURE、TRIGGER。未授予 DBA、ANY 或 SOI；新账号实际角色只有 PUBLIC。`USER_SYS_PRIVS` 中的服务器自带所有者权限不能全部当成本次 GRANT。

所有业务测试使用新账号，每次成功连接后检查 USER 和当前 Schema，只清理本轮对象。凭据只存 Git 忽略目录，Windows ACL 限制为当前用户；测试入口不读取管理员 secret。

## 实际验证结果

| 范围 | 覆盖内容 | 结果与最终状态 |
| --- | --- | --- |
| 离线行为 | 配置、会话、传输、安全、命令、类型、事务、隔离、异步、池、LOB、诊断 12 个项目 | **2156/2156** 通过；受影响项目修复后重跑，原失败记录保留 |
| 容量管理 | DataSource/direct registry、等待取消和超时、Clear/Dispose、旧 Reader/Transaction/Cancel、关闭后的事务和锁 | 最终包通过；15 创建=15 认证=15 关闭，所有容量计数归零；新连接确认未提交行和锁均无残留 |
| 取消与期限 | 行锁下 token/Command.Cancel/deadline、预取消、Reader token、未知提交、ACK 后取消、8/16 独立连接 | 最终包 10 个场景通过；未知提交不重放，新连接确认无意外业务行 |
| 大值 LOB | 67,109,888 字节 BLOB；各 1,158,260 UTF-16 字符 CLOB/NCLOB；未知长度短读源、hash、64 MiB 拒绝、范围和生命周期、取消 | 最终包 16 个场景通过；25 创建=25 关闭，同步网络调用 0 |
| 时间与错误恢复 | 8/9 位 NULL 和 CLR tick、无损拒绝、事务 DML 块、Prepare/绑定恢复、断链分类 | 当前源码 Issue16Probe 通过；5 张表清理，`fresh_remaining=0`。这是 ProjectReference 验证，与精确包消费者分开记录 |
| 资源观察 | 最多 4 并发；40 KiB BLOB、查询、提交/回滚、Clear；60 秒预热、600 秒测量、30 秒静默 | 测量阶段 17,639 个循环、121 样本，另 3 个静默样本；19,365 创建=关闭，池、registry、队列归零 |
| TLS | 本机合成 CA 下可信证书、PEM/PFX 双向认证；未知 CA、错误主机名、过期/错误用途/缺少客户端证书拒绝 | SecurityTests 41/41。远程达梦 TLS 实库仍待验证，未修改服务器配置或降低证书策略 |

### 首轮失败与修复

首轮离线为 2121 通过、35 失败：

- 27 个 Pool 失败来自测试中的 Unix-only 绝对路径，改为平台原生路径后 304/304。
- 8 个 Security 失败先修复 Windows loopback 服务端证书夹具；剩余 2 个 mTLS 失败定位为客户端私钥的 Schannel 兼容性。产品修复后 SecurityTests 41/41，负向校验仍通过。

产品修改仅限 Windows TLS 客户端证书加载：

- PFX 使用不带 `PersistKeySet` 的临时原生 key。
- PEM 通过临时内存 PFX 导入同类 key，PFX 缓冲区立即清零，证书随 TLS 生命周期释放。
- Windows 原生 key 可能临时落盘，不能宣称完全不落盘。
- 不写系统信任库，CA、主机名、用途、吊销和协议策略不变，默认吊销仍为 `Online`。

### 资源观察

资源进程实际运行 701.135 秒，总循环 19,361，确认提交 17,167、回滚 2,194：

- RSS 74.1–87.4 MiB，相对基线最大增长 9.27 MiB。
- 托管堆最大增长 2.79 MiB，线程相对基线最多 +4。
- 原预算和十个 60 秒窗口通过。
- Windows Handles 测量阶段 320–344，基线 320，静默阶段三点均为 319；只报告观察值，没有追加任意句柄门槛。
- 诊断队列峰值 13，最终 0，drop/callback failure 为 0，同步网络调用为 0。
- 最终独立连接再次确认 USER/Schema，`USER_TABLES=0`、`USER_OBJECTS=0`。

辅助 Resource 工具 50 项中 48 通过、2 失败；诊断健康 validator 38/38。两个失败来自历史 source-identity 工具的 POSIX executable-bit 断言和 Windows symlink 权限要求，在未修改基线上同样复现。没有放宽断言或提升权限；它们不被当成本次产品行为验收条件。

## 重现入口

构建前设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。本轮工作目录 `.local/windows-i11` 使用独立 `global.json` 选择已安装的 10.0.301，仓库根配置未改。构建和测试使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。

```powershell
& ./scripts/with-dameng-test.ps1 `
  -ExpectedUser WDM_PROVIDER_TEST_I11_7C42A9D1 `
  -Command dotnet -CommandArguments @(
    '<consumer.dll>', 'shared', '<repo>', '<package-manifest.json>', '<report.json>')
```

wrapper 将 secret 的单个赋值作为数据解析，不执行文件内容。省略预期账号时只接受原 `WDM_PROVIDER_TEST`；自定义身份仅接受本次 `WDM_PROVIDER_TEST_I11_<8位大写HEX>` 格式，SA 和其他账号被拒绝。

消费者为 R2CancellationProbe、R3PoolProbe、R3LobProbe、R3ResourceProbe，使用 Release 构建和对应包版本/源参数。manifest 由 `tools/R2AsyncProbe/package_manifest.py` 生成，独立 `validate.py` 读取报告、manifest、实际消费者 DLL 和 `shared`。

WindowsCompatibilityProbe 记录只读服务器信息、默认 TLS 拒绝和最终对象状态。Issue16Probe 使用当前源码 ProjectReference，与精确包消费者分开记录。

原始日志、TRX、checkpoint、命令和退出码位于本机忽略目录 `.local/windows-i11/`。资源进程使用 900 秒外部期限；若被期限终止，应先按 checkpoint 的精确对象名确认归属和最终状态，不能假定 finally 已执行。

可审阅证据位于[本轮证据目录](../implementation/evidence/I11-windows-20261015/)。

## 尚未覆盖

- Windows FD 指标不可用，因此记录 Handles；不能把 `null` 当成 0 或声称 FD 预算通过。
- 固定时长的资源观察不等于小时/天级压力，也不验证物理会话复用。
- 没有运行官方驱动或可编译恢复版。
- 远程达梦 TLS、额外服务器版本/字符集、其他客户端平台，以及正式发布包的 Windows 验证仍需单独执行。
- 本次未扩展错误恢复白名单、TIME/INTERVAL 七位写入或 LOCAL TIME ZONE 能力。
