# Issue #12：.NET 8/9/10 多目标

当前源码在同一个 `W.DamengProvider` NuGet 包内生成 `net8.0;net9.0;net10.0`。产品、十二套测试及发布消费者统一 C#12，构建 SDK 仍由 `global.json` 固定为 10.0.401。已发布 `0.1.0-preview.1` 仍仅包含 net10，本次改动未发布，不包含 EF8/9 适配或 .NET 6。

## 资源与依赖

本轮在干净隔离输出中复现了 net8 的四个 MSB3030。SDK 10.0.401 的 `Microsoft.NET.Sdk.targets` 仅对目标 net9 及以上默认设置 `RespectAlreadyAssignedItemCulture=true`；net8 的 `AssignCulture` 因资源文件以文化目录而非文件名后缀命名，将显式 `Culture` 项目的 `WithCulture` 改成 false。卫星生成跳过，但后续复制仍要求四个文件，因而失败。

产品项目显式设置该属性为 true，保留原资源文件、LogicalName 和文化元数据。修复后的干净三目标 Release 构建成功，每目标都有主 DLL 及 en、zh-CN、zh-HK、zh-TW 四个卫星程序集；包消费者还检查实际资源内容、en-US 父文化和 fr-FR 中性回退。

| 目标 | 包内资产 | 运行时 NuGet 依赖 |
| --- | --- | --- |
| net8.0 | 主 DLL + 四个卫星程序集 | `Microsoft.Bcl.Cryptography 10.0.12`，传递依赖 `System.Formats.Asn1 10.0.12` |
| net9.0 | 主 DLL + 四个卫星程序集 | 无 |
| net10.0 | 主 DLL + 四个卫星程序集 | 无 |

加密兼容包提供 net8 的 `X509CertificateLoader`，不改变 CA、主机名、有效期、用途、私钥所有权和释放契约；TLS 产品代码及默认 Online 撤销策略未修改。另将两个格式化调用的空 span 替换为空 object 数组，原 SQL 模板不含格式占位符、生成文本不变。

发布入口按 TFM 检查依赖组、十五个程序集及消费者的 compile/runtime 资产。消费者仅直接引用本次产品包，通过精确源映射从本地 feed 获取产品，从 nuget.org 获取核准的兼容依赖；独立缓存中产品包须与候选一致。消费者禁止跨主版本 roll-forward，并检查实际 runtime 主版本及所加载产品资产。CI 与 tag 发布工作流均安装 8/9 runtime，使用固定 SDK 执行三目标矩阵后才继续现有步骤。

## 本轮环境与候选

2026-10-15，Ubuntu 24.04 / WSL2 / linux-x64，基线 `27058bca95a0d78c689413c252751b3163f28f1b` 加本次未提交 diff（`.local/issue12/candidate.patch` 标识产品、测试、工具、工作流及包内 README 改动）。验证包版本 `0.1.0-preview.2` 仅用于本地消费，未创建 tag 或上传；包的 RepositoryCommit 指向基线，不能单独表示未提交修改。

实际运行时为 .NET 8.0.32、9.0.21、10.0.12。8/9 来自微软 release-metadata 对应 Linux x64 runtime，并核对下载 SHA-512。构建使用 SDK10.0.401；本机最初使用 SDK 符号链接时 VSTest 误选系统 host、找不到 net9，改为 ignored 目录中的 SDK 实体副本后通过。该失败归属测试环境，原记录保留。

实现者先完成 Configuration / Security / Type / Pool 三目标重点回归：每目标分别 12 / 41 / 958 / 304 项，共 3945 项通过，无跳过。独立审阅者核对资源根因、产品与发布改动，修正了缓存路径对大写 SemVer prerelease 的处理；独立全量验证结果在下方记录。

## 完整验证

从仓库根目录执行：

```sh
export DOTNET_ROOT="$PWD/.local/issue12/dotnet"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_COMMAND="$DOTNET_ROOT/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 \
  --commit "$(git rev-parse HEAD)" --output .local/issue12/validation
```

入口为每个 dotnet 子进程设置可写任务 CLI_HOME、禁遥测、单节点、禁 build server 与共享编译。实际命令退出码、TRX、所选资产、runtime、包 manifest 和首次初始化报告保存在 ignored `.local/issue12/`；该路径是本机证据位置，不是公共附件。

独立执行的三运行时十二套离线测试全部通过：

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

36 份 TRX 共 **6471/6471**，失败、跳过、中止均为 0。覆盖包含真实 loopback TLS（CA/PFX/PEM 与证书拒绝）、取消、事务状态和未知结果、时间精度、LOB、容量与资源释放；离线协议/loopback 不计为真实达梦验收。发布工具 Python 23/23、R3 validator Python 50/50 通过。

最终 `prepare` **退出 0**，结果目录为 `.local/issue12/validation/nuget-qyfzc41y/`，顶层日志与退出码为 `prepare-final.log` / `prepare-final.exit`。同一包的十五个托管资产、三个目标的精确依赖组和六个独立消费者全部通过；三个公开 smoke 均确认文化资源查找与回退，各目标的七个首次初始化场景合计 **21/21**。实际加载结果为：

| 消费者目标 | 实际 runtime | 实际产品资产 | 首次初始化 |
| --- | --- | --- | --- |
| net8.0 | .NET 8.0.32 | `lib/net8.0/W.DamengProvider.dll` | 7/7 |
| net9.0 | .NET 9.0.21 | `lib/net9.0/W.DamengProvider.dll` | 7/7 |
| net10.0 | .NET 10.0.12 | `lib/net10.0/W.DamengProvider.dll` | 7/7 |

本地候选 nupkg SHA-256：`e9b1392019dc8e342be620ae3e000134d868c314c1962f328d5977b5fa0f84ef`。源码验证和包消费属于上述未提交候选，不是新的公开发布。

独立验证者另外执行三个 `R3ResourceProbe/validate.py` 离线报告核验，均退出 0；用 `dotnet exec --fx-version 10.0.12` 分别强制启动 net8/net9 smoke，均在 `package_identity` 阶段退出 1，证明不会将错误主版本运行计为通过。精确命令和预期结果保存在 `.local/issue12/independent-extra-checks.json`。独立代码审阅和最终行为验证均未留下待修代码问题。

## PR 合并前同步 main 的补验

创建 PR #21 前同步了 main 的数组绑定/DbBatch 改动（`main@5bcb69e`），受测合并候选为干净提交 `e1a0d2205a2379adfe9ffe0dd9965623dee424b4`。代码未发生合并冲突；文档冲突合并保留双方内容。独立验证者使用上述相同 SDK/runtime 和完整 `prepare` 入口，将输出目录改为 `.local/issue12/pr-validation`，再次执行全部三目标矩阵。

结果 **退出 0**：每目标 **2176/2176**，三目标共 **6528/6528**，零失败、跳过、中止。其中 Command 增至 31 项、Pool 增至 308 项，其他套件计数不变。新增数组绑定和 DbBatch 的代码与测试在 net8/9/10 均通过；同一包的十五个程序集、精确依赖组、六个独立消费者、三个文化资源 smoke 和 **21/21** 首次初始化场景通过。实际 runtime 仍分别为 8.0.32、9.0.21、10.0.12。

本次结果位于 `.local/issue12/pr-validation/nuget-ih9753sj/`，日志与退出码为 `prepare-pr.log` / `prepare-pr.exit`；候选 nupkg SHA-256 为 `b2052ab164a4a722369817fa06017f88dc695251b7e0a3e5c1d87fef717693d1`。此补验覆盖同步 main 后的代码；前述 6471 项结果继续归属原候选。shared/严格 TLS 实库仍为 `integration_pending`。

## 已处理的验证失败

- 干净 net8 构建：四个 MSB3030，按上述 Culture 元数据根因修复；原始日志 `source/first-net8.log`、`source/first-net8-diag.log` 保留。
- 初始 net9 测试：SDK 符号链接使 VSTest 使用系统 host，找不到 net9；改用本地 SDK 实体副本。没有通过跨主版本 roll-forward 求通过。
- 首次完整 prepare：net8 Transport 原测试先取消 socket read，再复用同一 socket 验证 TLS 等待；实际失败为 `NetworkStream` 拒绝已标为未连接的 socket，TLS 尚未开始。测试改为各用独立真实连接验证读取与 TLS 的挂起、取消令牌和 TLS 失败关闭行为，保留覆盖、不修改产品 TLS 策略。修正后 Transport 在三个 runtime 各 116 项通过；原混合测试在 net9/10 的对照执行通过。首次失败记录为 `validation/nuget-g6z_cr7g/net8.0_Transport.trx`，诊断日志为 `source/Transport-net8-diagnosis.log`。
- 第二次 prepare：net8 LOB 测试两处 `Convert.ToHexStringLower` 编译失败；改用 `Convert.ToHexString(...).ToLowerInvariant()` 保持相同 SHA-256 小写摘要断言。原始失败保留在 `prepare-retry.log`；未跳过测试。修正后 LOB 258 项、Diagnostics 84 项在三个 runtime 均通过。

## 实库与未覆盖范围

三个 runtime 的 shared TEST 与严格 TLS TEST 均为 **integration_pending**。本工作树没有 `.local/secrets/dameng-test.env`、`dameng-tls-test.env`，也没有显式测试连接变量；未读取 SA/DEV 配置，未连接数据库、未创建对象。历史 .NET 10 实库结果不代替本候选的 8/9/10 实库验收。

当前包 smoke 已按目标提供相同入口，覆盖身份检查、显式预取消和后续可用性、提交/回滚结果、时间值及 BLOB/CLOB 回读、Reader/连接释放与精确唯一表清理。预取消不等于发送后取消；本轮发送后取消与未知事务结果仅由离线协议测试覆盖。取得当前主机可访问的授权 TEST 配置后，须按[发布指南](../release.md#本地实库检查)串行补验三目标 shared/严格 TLS；先确认 TEST 身份，记录命令、退出码、真实服务器最终状态。不能因离线通过而宣称 Issue #12 的实库验收完成。

本次不扩展服务器/OS/RID、长期资源压力、.NET 6/Core3.1 或 EF8/9 适配范围。

## 运行时生命周期

按 2026-10-15 查阅的[微软 .NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，.NET 8 与 9 的支持期均到 2026-11-10，.NET 10 到 2028-11-14。库对某个 TFM 的兼容不延长微软运行时维护期；应用仍需独立管理运行时升级。兼容包的目标与依赖见[官方 NuGet 页面](https://www.nuget.org/packages/Microsoft.Bcl.Cryptography/10.0.12)。
