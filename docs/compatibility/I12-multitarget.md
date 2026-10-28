# .NET 8/9/10 多目标实现与验证

issue #12 最初在同一个 `W.DamengProvider` 包中加入 `net8.0`、`net9.0`、`net10.0`。后续 issue #13 加入 .NET 6，Schema 套件也纳入发布检查；当前版本和四个现代目标框架的结果见[0.3.1 发布记录](../release-0.3.1.md)。

本文保留最初三目标实现的技术背景和验证记录，不代表当前测试数量或待验状态。

## 资源程序集与依赖

最初在干净输出中复现了 net8 的四个 MSB3030。原因是 SDK 10.0.401 的 `Microsoft.NET.Sdk.targets` 只对 net9 及以上默认设置 `RespectAlreadyAssignedItemCulture=true`。net8 的资源文件按文化目录命名，`AssignCulture` 将显式 Culture 项目的 `WithCulture` 改为 false，导致卫星程序集生成被跳过，但后续复制仍要求这些文件。

产品项目显式设置该属性为 `true`，保留资源文件、LogicalName 和文化元数据。修复后，每个目标框架都有主 DLL 和 en、zh-CN、zh-HK、zh-TW 四个卫星程序集；消费者还检查实际资源内容、en-US 父文化和 fr-FR 中性回退。

| 目标框架 | 包内程序集 | 运行时 NuGet 依赖 |
| --- | --- | --- |
| `net8.0` | 主 DLL + 四个卫星程序集 | `Microsoft.Bcl.Cryptography 10.0.12`，传递依赖 `System.Formats.Asn1 10.0.12` |
| `net9.0` | 主 DLL + 四个卫星程序集 | 无 |
| `net10.0` | 主 DLL + 四个卫星程序集 | 无 |

加密兼容包提供 net8 的 `X509CertificateLoader`，不改变 CA、主机名、有效期、用途、私钥生命周期或默认 `Online` 吊销策略。另将两个格式化调用的空 span 改为空 object 数组；原 SQL 模板没有格式占位符，生成文本不变。

发布入口按目标框架检查依赖组、程序集和消费者的编译/运行资产。消费者只直接引用本次产品包，通过精确源映射从本地 feed 获取产品，从 nuget.org 获取核准依赖，并使用独立缓存。消费者禁止跨主版本 roll-forward，还会检查实际运行时主版本和加载的产品 DLL。

## 历史验证：首轮三目标候选

2026-10-15，环境为 Ubuntu 24.04 / WSL2 / linux-x64。基线为 `27058bca95a0d78c689413c252751b3163f28f1b` 加当时修改；本地包 `0.1.0-preview.2` 未创建 tag 或上传。

实际运行时为 .NET 8.0.32、9.0.21、10.0.12，构建使用 SDK 10.0.401。8/9 runtime 来自微软 release-metadata，并核对下载 SHA-512。

本机最初通过 SDK 符号链接运行时，VSTest 误选系统 host、找不到 net9；改用忽略目录中的 SDK 实体副本后通过。这是测试环境问题，原记录保留。

首轮重点回归 Configuration / Security / Type / Pool，每个目标分别 12 / 41 / 958 / 304 项，共 **3945 项**通过，无跳过。独立审阅还修正了缓存路径对大写 SemVer prerelease 的处理。

### 完整验证命令

```sh
export DOTNET_ROOT="$PWD/.local/issue12/dotnet"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_COMMAND="$DOTNET_ROOT/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 \
  --commit "$(git rev-parse HEAD)" --output .local/issue12/validation
```

入口为每个 dotnet 子进程设置可写任务 CLI home，关闭遥测，使用单节点并禁用 build server 和共享编译。证据位于本机忽略目录 `.local/issue12/`。

### 离线结果

| 套件 | net8.0 | net9.0 | net10.0 |
| --- | ---: | ---: | ---: |
| Configuration | 12 | 12 | 12 |
| Session | 21 | 21 | 21 |
| Transport | 116 | 116 | 116 |
| Security | 41 | 41 | 41 |
| Command | 16 | 16 | 16 |
| Type | 958 | 958 | 958 |
| Transaction | 135 | 135 | 135 |
| Isolation | 78 | 78 | 78 |
| Async | 134 | 134 | 134 |
| Pool | 304 | 304 | 304 |
| Lob | 258 | 258 | 258 |
| Diagnostics | 84 | 84 | 84 |
| 合计 | **2157** | **2157** | **2157** |

36 份 TRX 共 **6471/6471**，失败、跳过和中止均为 0。覆盖真实 loopback TLS、取消、事务状态、时间精度、LOB 和资源释放；离线协议及 loopback 不计为真实达梦验证。发布工具 23/23、R3 validator 50/50 通过。

最终 `prepare` 退出 0，结果位于 `.local/issue12/validation/nuget-qyfzc41y/`。同一包的 15 个托管程序集、三组依赖和六个独立消费者全部通过；三个公开 smoke 均确认文化资源回退，共 **21/21** 首次初始化场景通过。

| 消费者目标 | 实际运行时 | 实际产品资产 | 首次初始化 |
| --- | --- | --- | --- |
| net8.0 | .NET 8.0.32 | `lib/net8.0/W.DamengProvider.dll` | 7/7 |
| net9.0 | .NET 9.0.21 | `lib/net9.0/W.DamengProvider.dll` | 7/7 |
| net10.0 | .NET 10.0.12 | `lib/net10.0/W.DamengProvider.dll` | 7/7 |

候选包 SHA-256 为 `e9b1392019dc8e342be620ae3e000134d868c314c1962f328d5977b5fa0f84ef`。

独立检查还用 `dotnet exec --fx-version 10.0.12` 强制启动 net8/net9 smoke，两者都在 `package_identity` 阶段按预期退出 1，证明不会把错误主版本运行计为通过。命令保存在 `.local/issue12/independent-extra-checks.json`。

## PR 合并前同步 main 的补验

创建 PR #21 前同步了 main 的数组绑定和 DbBatch 改动（`main@5bcb69e`），受测合并提交为 `e1a0d2205a2379adfe9ffe0dd9965623dee424b4`。

使用相同环境和完整 `prepare` 重跑，输出到 `.local/issue12/pr-validation`：

- 每个目标 **2176/2176**，三目标共 **6528/6528**，零失败、跳过和中止。
- Command 增至 31，Pool 增至 308，其他套件计数不变。
- 15 个程序集、三组依赖、六个消费者、三个文化资源 smoke 和 **21/21** 首次初始化场景通过。
- 候选包 SHA-256 为 `b2052ab164a4a722369817fa06017f88dc695251b7e0a3e5c1d87fef717693d1`。

该结果只覆盖同步 main 后的代码；前述 6471 项仍属于原候选。

## 已处理的验证失败

- **net8 资源构建**：四个 MSB3030，按上述 Culture 元数据原因修复，原日志保留。
- **net9 host**：SDK 符号链接导致找不到 runtime，改用实体 SDK，没有跨主版本 roll-forward。
- **net8 Transport**：原测试先取消 socket read，再复用同一 socket 验证 TLS，`NetworkStream` 在 TLS 开始前就拒绝。测试改为使用独立真实连接，保留取消和关闭断言，产品 TLS 策略不变。
- **net8 LOB**：两处 `Convert.ToHexStringLower` 编译失败，改用 `Convert.ToHexString(...).ToLowerInvariant()`，保持相同摘要断言，没有跳过测试。

## 当时的实库待验状态与后续结果

issue #12 当时没有 TEST 或 TLS 配置，三个运行时的共享和严格 TLS 实库均为 `integration_pending`，未连接数据库或创建对象。

后续 [issue #13](I13-net6.md#历史验证记录) 使用同一 smoke 在 .NET 8.0.32、9.0.21、10.0.12（及 6.0.36）完成共享 TEST 验证。最新共享和严格 TLS 结果以[当前发布记录](../release-0.3.1.md)为准，不能继续沿用本页的历史待验状态。

## 运行时生命周期

按 2026-10-15 查阅的[微软 .NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，.NET 8 与 9 的支持期均到 2026-11-10，.NET 10 到 2028-11-14。驱动兼容某个目标框架，不会延长微软的运行时维护期。

兼容包依赖见[官方 NuGet 页面](https://www.nuget.org/packages/Microsoft.Bcl.Cryptography/10.0.12)。
