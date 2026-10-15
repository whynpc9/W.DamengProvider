# Windows 兼容验证（issue #11）

2026-10-15 在用户指定的远程实例执行，范围来自 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11)。本页记录当前源码及本地测试包，尚未发布，不追溯修改 `0.1.0-preview.1` 的发布验收。

## 环境与隔离

| 项目 | 实际值 |
| --- | --- |
| 客户端 | Windows 10.0.26200，win-x64，.NET 10.0.9 |
| SDK / MSBuild | 10.0.301 / 18.6.4；仓库固定的 10.0.401 本机未安装 |
| 服务器登录版本 | 8.1.5.60 |
| `V$VERSION` | DM Database Server 64 V8；DB Version: 0x7000d；03134284604-20260707-335949-20228；Msg Version: 6；Gsu level(5) cnt: 102 |
| 会话字符集 / 消息协议 | UTF-8 / 21；`SF_GET_UNICODE_FLAG()` 为 1（`V$VERSION` 的 Msg Version 文本另列，不混同协议协商值） |
| 传输 | 显式 `PlaintextAllowed`，实测协商 mode 0；默认 `RequireTls` 的真实 Open 被拒绝 |
| 源码基线 | `24e792d` 加本次 Windows TLS、测试及工具 diff |
| 最终本地包 | `0.1.1-i11.20261015.24e792d.r2`，精确 PackageReference；包与加载 DLL 身份见本轮证据 |

原实例已存在 `WDM_PROVIDER_TEST`。按用户本次“创建独立测试账户/数据库”授权，新建 `WDM_PROVIDER_TEST_I11_7C42A9D1` 及同名默认 Schema，保留原账号及数据。这里的“测试数据库”是现有达梦实例内的独立逻辑空间，没有创建物理实例或表空间。

SA 仅用于账户创建及固定七项授权：CREATE SESSION、TABLE、INDEX、VIEW、SEQUENCE、PROCEDURE、TRIGGER，没有授予 DBA、ANY 或 SOI。新账号实际角色只有 PUBLIC；`USER_SYS_PRIVS` 还列出服务器自带的所有者权限，不能把它们都解释为本次 GRANT。全部业务测试使用新测试账号，连接前检查配置账号，每次成功连接后检查 USER 和当前 Schema；仅清理本轮精确对象。

凭据只存 Git 忽略的 `.local/secrets/`，Windows 目录及文件通过当前用户专属 ACL 限制访问。测试入口不读取管理员 secret。本次测试账户保留供复现，测试表应在最终 fresh 连接中确认清空。

## 实际行为

| 范围 | 成功输入与拒绝输入 | 结果及最终状态 |
| --- | --- | --- |
| 离线合同 | 配置、会话、传输、安全、命令、类型、事务、隔离、异步、池、LOB、诊断共 12 项目 | 2156/2156；受影响项目修复后重跑，原失败记录保留 |
| 容量管理 | DataSource/direct registry、等待取消/超时、Clear/Dispose、旧 Reader/Transaction/Cancel、关闭后的事务与锁 | 最终包通过；15 创建=15认证=15关闭；owner 全零；fresh 未提交行 0、锁释放、精确表不存在 |
| 取消/期限 | 行锁下 token/Command.Cancel/deadline、预取消、Reader token 生命周期、已发送 Commit unknown、已确认 ACK 后取消、8/16 独立连接 | 最终包 10 个场景通过；不重放未知提交；fresh 非零业务行 0、精确表不存在 |
| 大值 LOB | 67,109,888 字节 BLOB；各 1,158,260 UTF-16 字符 CLOB/NCLOB；未知长度短读源、完整 hash、64 MiB 物化拒绝、范围及生命周期拒绝、上传/读取取消 | 最终包 16 个场景通过；25 创建=25关闭，同步网络调用 0；精确表不存在 |
| 时间与错误恢复 | 8/9 位 typed NULL 和 CLR tick 值、不能无损表示的输入、事务内 DML 块/控制块拒绝、Prepare/本地绑定失败后的恢复、网络断链分类 | 最终源码 Issue16Probe 通过；五个精确表已删除，fresh_remaining=0；它是 ProjectReference 验证，和精确包消费者分别记录 |
| 资源观察 | 并发最多 4；40 KiB BLOB、查询、提交/回滚、Clear；60 秒预热、600 秒测量、30 秒静默后 3 次采样，不强制 GC | 最终包通过独立 validator；测量阶段 17,639 个循环、121 样本，另 3 个静默样本；19,365 创建=关闭，池/registry/队列归零；精确表不存在 |
| TLS | 本机合成 CA 下可信证书、PEM/PFX 双向认证；未知 CA、错误主机名、过期/错误用途/缺少客户端证书拒绝、失败无明文回退 | SecurityTests 41/41。远程达梦 TLS 实库仍 pending；未修改其配置或降低证书策略 |

首轮离线 2121 pass / 35 fail。27 个 Pool 失败来自测试中的 Unix-only 绝对路径，改为平台原生绝对路径后 304/304；8 个 Security 失败先修复 Windows loopback 服务端夹具证书，剩 2 个 mTLS 失败定位为产品客户端私钥的 Schannel 兼容性。修复后 SecurityTests 41/41，负向校验保持通过。

产品改动仅在 TLS 客户端证书加载：Windows PFX 使用不带 PersistKeySet 的临时原生 key；Windows PEM 经临时内存 PFX 导入同类 key，PFX 缓冲区立即清零，证书沿 TLS scope Dispose。Windows 原生 key 可能临时落盘，不能声称完全不落盘；不写系统信任库，CA/主机名/用途/吊销/协议策略保持原样，默认吊销仍 Online。

资源进程实际 701.135 秒，总循环 19,361，确认提交 17,167、回滚 2,194；RSS 为 74.1–87.4 MiB，相对基线最大增长 9.27 MiB，托管堆最大增长 2.79 MiB，线程相对基线最多 +4，全部原预算及十个 60 秒窗口通过。Windows Handles 测量阶段 320–344，基线 320、静默阶段三点均为 319，仅报告观察值，没有追加任意句柄门槛。诊断队列峰值 13，最终为 0，drop/callback failure 为 0，同步网络调用为 0。最后独立真实连接再次确认 USER/Schema，`USER_TABLES=0`、`USER_OBJECTS=0`。

辅助 Python 工具检查单列：Resource 工具 50 项中 48 pass、2 项失败；仅运行诊断健康 validator 测试为 38/38。两个失败是历史 source-identity 工具的 POSIX executable-bit 断言和创建 symlink 的权限要求；在未修改的 `24e792d` 源文件上单独复现了相同失败（Windows `chmod` 不提供该执行位语义，创建 symlink 返回 WinError 1314）。保留原失败及基线对照，没有放宽断言、提升 Windows 权限或将这些历史工具限制变成本次产品验收前置条件；本次通过原有四个行为 validators 核验实际包、用例、资源和最终状态。

## 重现入口

构建前设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。本轮工作目录为 `.local/windows-i11`，其中独立 `global.json` 选择实际安装的 10.0.301；仓库根 `global.json` 未改。构建/测试参数均为 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。

从仓库根运行新 PowerShell wrapper，可加载唯一 TEST secret 并传入明确预期账号：

```powershell
& ./scripts/with-dameng-test.ps1 `
  -ExpectedUser WDM_PROVIDER_TEST_I11_7C42A9D1 `
  -Command dotnet -CommandArguments @(
    '<consumer.dll>', 'shared', '<repo>', '<package-manifest.json>', '<report.json>')
```

wrapper 将 secret 的单个赋值作为数据解析，不执行文件内容；参数省略时仍只接受原 `WDM_PROVIDER_TEST`。探针覆盖前后身份与 Schema 核验；自定义身份仅接受本次 `WDM_PROVIDER_TEST_I11_<8位大写HEX>` 模式，SA 和其他账号被拒绝。

消费者分别是 R2CancellationProbe、R3PoolProbe、R3LobProbe、R3ResourceProbe。以 Release 构建并指定 `R2PackageVersion`/`R2PackageSource` 或 `R3PackageVersion`/`R3PackageSource`；manifest 使用现有 `tools/R2AsyncProbe/package_manifest.py` 生成，独立 `validate.py` 读取报告、manifest、实际消费者 DLL 和 `shared`。WindowsCompatibilityProbe 另记录只读 profile、默认 TLS 拒绝及最终对象状态。Issue16Probe 是当前源码 ProjectReference，和精确包消费者分别记录。

本轮本地原始日志、TRX、checkpoint、命令及退出码在 ignored `.local/windows-i11/`，没有复制凭据。runtime 使用外部进程期限（资源 900 秒）；若期限终止，先按 checkpoint 的精确对象名确认归属与最终状态，再决定清理，不能把 finally 假定为已执行。

可审阅的安全报告、精确包 manifest、离线首次失败及受影响重跑汇总保存于 [本轮证据目录](../implementation/evidence/I11-windows-20261015/)。R2/R3 的报告通过另一执行者运行独立 validators；产品 TLS diff 也由实现者之外的执行者审阅。Windows 私钥加载修复后重新打包并复测，未把初轮包结果冒充最终包结果。

## 未覆盖范围

Windows FD 指标 unavailable，记录实际 Windows Handles，不能把 null 当作 0 或声称 FD 预算通过。固定资源观察不等于小时/天级压力或物理会话复用验收。没有运行官方驱动或可编译恢复版，本页只有 W 驱动结果。

Linux、macOS 当前候选复测、远程服务器 OS、额外服务器版本/字符集、远程 TLS 实库，以及已发布包的 Windows 验证仍 pending。本次不扩展错误恢复白名单或 TIME/INTERVAL 七位写、LOCAL TIME ZONE 能力，不关闭 issue #11 的其余范围。
