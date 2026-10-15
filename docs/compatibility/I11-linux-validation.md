# Issue #11：Linux 验证记录

2026-10-15，针对 [issue #11](https://github.com/whynpc9/W.DamengProvider/issues/11) 完成下述受测源码在 Linux 下的离线与本地包运行验证。结果为 `offline_verified`，数据库部分为 `integration_pending`。本记录不替代 [0.1.0-preview.1 发布回执](../release-0.1.0-preview.1.md)。

## 受测环境与候选

| 项目 | 实际值 |
| --- | --- |
| 源码 commit | `24e792db6b4392f707911efd7ef7c9b01f1f14a7` |
| 源码状态 | 执行时工作区干净；验证后仅补充文档 |
| OS | Ubuntu 24.04.4 LTS，WSL2 |
| 内核 / 架构 | `5.15.167.4-microsoft-standard-WSL2` / `x86_64` |
| RID / 目标框架 | `linux-x64` / `net10.0` |
| SDK / MSBuild | `10.0.401` / `18.9.11` |
| 实际运行时 | `Microsoft.NETCore.App 10.0.12` |
| 本地验证包 | `W.DamengProvider 0.1.0-preview.2`，仅为本次验证版本，未创建 tag 或发布 |
| nupkg SHA-256 | `b18ae84b95d929f2884ec3b0ea1284baf6ead1de7cec019e68605720c457390a` |
| 实际加载产品 DLL SHA-256 | `7825d5e16ae37f96c2a21de463ea3fefd06e7eb58e781a1e5a9cd4877f9bfb3d` |

## 命令与结果

从仓库根目录执行：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 \
  --commit 24e792db6b4392f707911efd7ef7c9b01f1f14a7 \
  --output .local/issue11-linux/prepare
```

两个命令退出码均为 **0**。prepare 为所有 .NET 子进程设置独立可写 `DOTNET_CLI_HOME`、关闭首次初始化和遥测，构建/测试使用 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`。

| 检查 | 实际结果 |
| --- | --- |
| 发布工具 Python 测试 | 15/15 |
| Configuration / Session / Transport / Security | 12 / 21 / 115 / 41，通过 |
| Command / Type / Transaction / Isolation | 16 / 958 / 135 / 78，通过 |
| Async / Pool / Lob / Diagnostics | 134 / 304 / 258 / 84，通过 |
| 正式 TRX 合计 | **2156/2156**，失败、跳过、中止均为 0 |
| Release 打包与包检查 | 通过；纯托管 net10.0 产品 DLL、四套语言资源，无额外运行时 NuGet 依赖或第三方原生资产 |
| 独立 PackageReference 消费 | 通过；唯一输入为本次本地包，各消费者使用全新包缓存；公开工厂与资源 smoke 退出 0 |
| 七个独立进程首次初始化 | 7/7；正常及各诊断回调异常场景均确认单次模拟 Commit ACK、`Committed`、队列归零，无调用方上下文泄露 |

本机原始证据位于 ignored `.local/issue11-linux/`：`environment.json`、`dotnet-info.txt` 以及 `prepare/nuget-nx7w78ye/` 中的十二份 TRX、各阶段日志、`commands.jsonl`、`package-manifest.json`、`package_smoke.log`、`first-init.json`。这些路径定位本机证据，不是公开下载地址。

## 本轮兼容表：成功与拒绝行为

以下均为本机实际执行的现有测试；协议 fixture、合成流与 loopback 不计为真实达梦服务器验证。

| 支持单元 | 成功输入及观察 | 拒绝输入及观察 | 数据库最终状态 |
| --- | --- | --- | --- |
| Ubuntu WSL2 / linux-x64 / .NET 10.0.12：托管包消费 | 精确 PackageReference 加载本次 DLL；工厂、默认与 en/zh-CN/zh-HK/zh-TW 资源可用 | 包检查要求精确身份、资源、依赖及资产白名单；发布工具的拒绝分支测试通过 | 未连接数据库、未创建对象 |
| 同环境：TCP、TLS 与证书 | 真实 loopback TCP；匹配 DNS/IP SAN 与受信 CA 的 TLS；PEM/PFX 客户端证书及双向 TLS | 未知 CA、过期、主机名/EKU 不匹配、缺少客户端证书被拒绝；失败连接关闭且不能继续明文发送；默认 Online 撤销策略断言通过 | 未连接数据库、未创建对象 |
| 同环境：取消、超时与事务 | 离线协议下未发送取消保留 Active 事务；确认 ACK 得到 Commit/Rollback 结果 | 部分发送/响应后取消关闭捕获会话；EOF 后 Commit 为 OutcomeUnknown，禁止重放；期限与旧取消令牌不污染下一次调用 | 未连接数据库、未创建对象；状态仅属于测试协议会话 |
| 同环境：容量、LOB、字符与时间边界 | 容量等待/Clear/Dispose、万次合成租约及 registry churn；UTF-8/GB18030 分段；合成 LOB 流；已验证 profile 的时间编码 | 超限/截断帧、非法字符、LOB 生命周期越界和精度损失被拒绝；未知 profile 维持保守限制 | 未连接数据库、未创建对象 |

TLS loopback 使用无 CRL 的临时测试 CA，显式 `NoCheck`；CA、主机、有效期和用途验证仍执行，产品默认 Online 不变。相关入口见 [TLS 测试](../../tests/W.DmProvider.SecurityTests/TlsTransportTests.cs)、[异步 TLS 测试](../../tests/W.DmProvider.SecurityTests/AsyncTlsValidationTests.cs)、[取消测试](../../tests/W.DmProvider.TransportTests/CancellationCoreTests.cs)、[LOB 编码边界](../../tests/W.DmProvider.LobTests/EncodingBoundaryTests.cs)。native loader 的 fake backend 测试只证明路径与所有权逻辑，不证明 Linux 原生库互操作。

## 尚未覆盖

| 支持单元 / 能力 | 状态与原因 |
| --- | --- |
| Linux → shared 达梦，显式 PlaintextAllowed | `integration_pending`；本工作树无 `.local/secrets/dameng-test.env`，也无显式 `DAMENG_TEST_CONNECTION_STRING` |
| Linux → 达梦严格 RequireTls | `integration_pending`；本工作树无 TLS TEST secret，未验证实例或证书材料可用性 |
| 服务器完整版本、字符集与实际传输策略 | 未测；不沿用历史 shared/TLS profile 信息作为本轮事实 |
| 真实库取消/超时、并发容量、LOB、错误后会话/事务与精确对象清理 | 未执行；最终状态为未连接数据库、未创建对象，不写成“清理验证通过” |
| 当前候选长时 RSS/heap/thread/FD/队列资源检查 | pending；离线万次租约、合成大流和首次初始化测试不代替实库长时压力 |
| 其他 Linux 发行版、非 WSL 内核、ARM64/其他 RID | 本轮未测，分别保留 pending；Windows 验证另见 [独立记录](I11-windows.md) |

用户随后提供了 macOS 项目 `.local` 目录位置；当前 Linux 主机不存在该目录，也没有 `/Users` 挂载，因而未读取其中配置或尝试数据库连接。实库补验需要当前 Linux 主机可访问的授权 TEST 配置与相应网络/证书环境。

没有扩展服务器错误恢复白名单、TIME/INTERVAL 七位写或 LOCAL TIME ZONE 能力。后续取得本机授权 TEST 配置后，应对同一实际包先确认 `WDM_PROVIDER_TEST` 用户及同名当前 Schema，记录服务器完整版本、字符集、传输策略，再按 [本地实库检查](../release.md#本地实库检查) 和现有取消/资源/LOB 入口补验；异常和未知事务结果不重试。TLS 实例须先核实有效许可与证书，继续采用严格证书校验。

本轮命令由主 agent 执行；审查 agent 独立解析十二份 TRX、二十个命令退出码并核对包及实际加载程序集，另执行现有 `R3ResourceProbe/validate.py ... offline`，退出 0。独立复核通过，不宣称有第二位执行者重复运行全部 .NET 测试。
