# .NET Core 3.1 兼容性探针

此探针检查驱动的 `netstandard2.1` 目标框架，必须在实际 .NET Core 3.1 运行时中执行。它只使用合成握手和 loopback TCP，不读取 secret，也不连接数据库。

退出码 0 表示全部断言通过。异步操作有看门狗限制；每个进程只运行一个实例。

## 构建

使用现代 SDK（C# 12）和仓库统一的单节点参数：

```sh
export DOTNET_CLI_HOME="$PWD/.local/legacy-probe-cli"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build tools/LegacyCompatibilityProbe/LegacyCompatibilityProbe.csproj -c Release -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

项目默认通过 ProjectReference 显式选择 `netstandard2.1`。

如果要检查本地 NuGet 包，将本项目的 `.csproj` 和 `Program.cs` 复制到一个空的忽略目录，使用以下参数恢复和构建：

- `/p:UsePackage=true`
- `/p:PackageVersion=0.3.0`，或本次实际候选版本
- 包源指向候选包目录和 nuget.org

包模式没有产品 ProjectReference，可避免旧项目引用资产残留。检查 `obj/project.assets.json`，确认编译和运行都选择 `lib/netstandard2.1/W.DamengProvider.dll`。运行时还会检查产品的 `TargetFrameworkAttribute`，防止较新的目标框架被误当成通过。

## 运行

在每个需要的架构上，将构建输出及依赖以只读方式挂载到 .NET Core 3.1 Linux 容器，执行：

```sh
dotnet W.DmProvider.LegacyCompatibilityProbe.dll
```

不要启用运行时 roll-forward。x64 和 arm64 的结果分别报告；只用现代 SDK 编译不能证明旧运行时行为。

## 覆盖范围

- 强类型数据源的打开失败清理和生命周期。
- 三行数组计划快照、NULL 类型推断和错误长度拒绝。
- DateTime/TimeSpan 推断、时间戳固定字节和日期边界。
- DNS 等待的取消和期限，包括忽略令牌的 resolver 在同步 Open 中被期限或并发 Close 中断，且尚未创建 TCP socket。
- 同步/异步 loopback 传输、调用方取消、期限和关闭。
- 12 种同步/异步 STARTUP 模式与安全策略组合：只有显式 `PlaintextAllowed` + mode 0 进入 LOGIN；mode 1/4 明确失败，且没有 LOGIN 或 TLS hello。
- 诊断初始化，以及旧批处理 API 确实不存在。

Schema/保存点只检查成员是否存在，不执行协议行为。合成服务器会在观察到 LOGIN 后关闭，因此不能据此宣称数据库认证成功。

当前成功运行报告 8 组、174 项断言。探针会观察自己注入的迟到 DNS 异常，避免未观察任务影响其他测试；这不证明产品的迟到异常观察器已执行。上游兼容包关于 .NET Core 3.1 未支持/未测试的警告仍然保留。

## 发布验证入口

完成 `release.sh prepare` 后运行：

```sh
bash eng/legacy-package.sh .local/nuget-release linux/amd64
```

该入口使用精确候选 PackageReference 和独立缓存，核对包内目标框架及实际消费者 DLL，并保留构建和运行日志。
