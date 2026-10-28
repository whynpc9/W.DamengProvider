# Linux 离线与包消费验证记录

2026-10-15，针对 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11) 完成当时源码在 Linux 下的离线和本地包验证。

结果为 `offline_verified`，数据库部分为 `integration_pending`。**本轮没有连接真实达梦数据库。** 最新发布状态见[0.3.1 发布记录](../release-0.3.1.md)。

## 受测环境

| 项目 | 实际值 |
| --- | --- |
| 源码 commit | `24e792db6b4392f707911efd7ef7c9b01f1f14a7` |
| 源码状态 | 执行时工作区干净；验证后仅补文档 |
| OS | Ubuntu 24.04.4 LTS，WSL2 |
| 内核 / 架构 | `5.15.167.4-microsoft-standard-WSL2` / `x86_64` |
| RID / 目标框架 | `linux-x64` / `net10.0` |
| SDK / MSBuild | `10.0.401` / `18.9.11` |
| 实际运行时 | `Microsoft.NETCore.App 10.0.12` |
| 本地验证包 | `W.DamengProvider 0.1.0-preview.2`，未创建 tag 或发布 |
| nupkg SHA-256 | `b18ae84b95d929f2884ec3b0ea1284baf6ead1de7cec019e68605720c457390a` |
| 实际加载产品 DLL SHA-256 | `7825d5e16ae37f96c2a21de463ea3fefd06e7eb58e781a1e5a9cd4877f9bfb3d` |

## 执行命令与结果

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 \
  --commit 24e792db6b4392f707911efd7ef7c9b01f1f14a7 \
  --output .local/issue11-linux/prepare
```

两个命令均退出 0。`prepare` 为 .NET 子进程设置独立可写 CLI home，关闭首次初始化和遥测，并使用单节点、禁用 build server 和共享编译参数。

| 检查 | 实际结果 |
| --- | --- |
| 发布工具 Python 测试 | 15/15 |
| Configuration / Session / Transport / Security | 12 / 21 / 115 / 41，通过 |
| Command / Type / Transaction / Isolation | 16 / 958 / 135 / 78，通过 |
| Async / Pool / Lob / Diagnostics | 134 / 304 / 258 / 84，通过 |
| 正式 TRX 合计 | **2156/2156**，失败、跳过和中止均为 0 |
| Release 打包与包检查 | 通过；纯托管 net10.0 产品 DLL、四套语言资源，无额外运行时依赖或第三方原生资产 |
| 独立 PackageReference 消费 | 通过；唯一输入为本次本地包，消费者使用全新缓存；公开工厂和资源 smoke 退出 0 |
| 首次初始化 | 7/7；正常及诊断回调异常场景均确认单次模拟 Commit ACK、`Committed`、队列归零，无调用方上下文泄露 |

原始证据位于本机忽略目录 `.local/issue11-linux/`，包括环境信息、十二份 TRX、日志、`commands.jsonl`、`package-manifest.json`、包消费和首次初始化报告。这些路径不是公开下载地址。

## 本轮实际覆盖的行为

以下均为本机执行的现有测试。协议 fixture、合成流和 loopback 不计为真实达梦验证。

| 范围 | 成功行为 | 拒绝行为 | 数据库状态 |
| --- | --- | --- | --- |
| 托管包消费 | 精确 PackageReference 加载本次 DLL；工厂和 en/zh-CN/zh-HK/zh-TW 资源可用 | 包身份、资源、依赖和资产白名单的拒绝分支通过 | 未连接数据库、未创建对象 |
| TCP、TLS 与证书 | 真实 loopback TCP；匹配 DNS/IP SAN 和受信 CA 的 TLS；PEM/PFX 客户端证书和双向 TLS | 未知 CA、过期、主机名/EKU 不匹配、缺少客户端证书被拒绝；失败后不能继续明文发送 | 未连接数据库、未创建对象 |
| 取消、超时与事务 | 离线协议下，发送前取消保留活动事务；确认 ACK 得到提交/回滚结果 | 部分发送或响应后取消关闭会话；EOF 后提交为 OutcomeUnknown，禁止重放；旧令牌不污染下一次调用 | 状态仅属于测试协议会话 |
| 容量、LOB、字符与时间 | 容量等待/Clear/Dispose、合成租用、UTF-8/GB18030 分段、合成 LOB 流、已验证环境的时间编码 | 超限或截断帧、非法字符、LOB 生命周期越界和精度损失被拒绝；未知环境保持保守限制 | 未连接数据库、未创建对象 |

TLS loopback 使用无 CRL 的临时 CA，显式 `NoCheck`；CA、主机、有效期和用途检查仍执行，产品默认 `Online` 不变。

相关测试入口：[TLS](../../tests/W.DmProvider.SecurityTests/TlsTransportTests.cs)、[异步 TLS](../../tests/W.DmProvider.SecurityTests/AsyncTlsValidationTests.cs)、[取消](../../tests/W.DmProvider.TransportTests/CancellationCoreTests.cs)、[LOB 编码边界](../../tests/W.DmProvider.LobTests/EncodingBoundaryTests.cs)。native loader 的 fake backend 只证明路径和生命周期逻辑，不证明 Linux 原生库互操作。

## 尚未覆盖

| 场景 | 状态与原因 |
| --- | --- |
| Linux → 共享达梦，显式 PlaintextAllowed | `integration_pending`；当时工作区没有 TEST 配置或显式连接变量 |
| Linux → 达梦严格 RequireTls | `integration_pending`；当时没有 TLS TEST 配置，也未确认实例和证书可用性 |
| 服务器版本、字符集与实际传输策略 | 未测；没有沿用其他环境的信息充当本轮结果 |
| 实库取消、超时、并发容量、LOB、错误恢复和对象清理 | 未执行；不能写成“清理验证通过” |
| 长时间 RSS/heap/thread/FD/队列检查 | 待验证；离线合成测试不代替实库压力 |
| 其他 Linux 发行版、非 WSL 内核、ARM64/其他 RID | 未测；Windows 结果另见[独立记录](I11-windows.md) |

用户随后提供了 macOS 项目的 `.local` 路径，但当前 Linux 主机不存在该目录或 `/Users` 挂载，因此没有读取配置或尝试连接。

后续补验应使用当前 Linux 主机可访问的授权 TEST 配置，对同一实际包先确认 `WDM_PROVIDER_TEST` 用户和同名 Schema，记录服务器版本、字符集和传输策略，再按[本地实库检查](../release.md#本地实库检查)及现有取消、资源、LOB 入口执行。异常和未知事务结果不重试；TLS 实例须先核实有效许可和证书，继续严格验证。

独立复核解析了十二份 TRX、二十个命令退出码，并核对包和实际加载程序集；`R3ResourceProbe/validate.py ... offline` 退出 0。该复核不表示另一位执行者重复运行了全部 .NET 测试。
