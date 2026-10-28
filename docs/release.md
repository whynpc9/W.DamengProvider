# W.DamengProvider 发布指南

当前已发布 [0.3.1](release-0.3.1.md)。本文用于后续新版本发布；不要重新创建已存在的 tag，也不要把普通分支推送或 PR 当成发布。

## 1. 版本与环境

发布前确认：

- NuGet 包、产品项目和程序集名为 `W.DamengProvider`，公开命名空间为 `W.Dm`。
- 产品项目为 `src/W.DamengProvider/W.DamengProvider.csproj`。旧 `W.DmProvider` 引用必须更新并重新编译；测试项目和诊断源名称保留原标识。
- 先更新项目版本并完成验证。正式发布版本由新的、以 `v` 开头的合法 SemVer tag 决定。
- SDK 按 `global.json` 固定为 10.0.401，产品和测试使用 C# 12。
- 安装 .NET 6/8/9/10 运行时；实际 .NET Core 3.1 检查使用 Docker，不能用现代运行时替代。

## 2. 发布前检查

在待发布源码上执行以下命令。它们不会上传 NuGet：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.3.2 --commit "$(git rev-parse HEAD)" --output .local/nuget-release
bash eng/legacy-package.sh .local/nuget-release linux/amd64
```

如果 `dotnet` 不在 PATH 中，将 `DOTNET_COMMAND` 设置为实际可执行文件路径。发布入口会配置任务专用 `DOTNET_CLI_HOME`，关闭首次初始化和遥测，并使用单节点、禁用 build server 和共享编译参数。

### prepare 检查什么

**离线测试：** 在四个现代目标框架上分别执行十三套测试，共 52 份 TRX。失败、跳过或中止都必须为 0。

**包内容与依赖：** 同一次打包生成五组目标框架资产，共 25 个托管程序集；每组包含主 DLL 和 en、zh-CN、zh-HK、zh-TW 四套语言资源。检查包身份、版本、许可证、README、上游声明、仓库 commit 和依赖组：

- `net6.0`：仅允许 `Microsoft.Bcl.TimeProvider 8.0.1`。
- `net8.0`：仅允许 `Microsoft.Bcl.Cryptography 10.0.12`。
- `net9.0` / `net10.0`：无额外运行时依赖。
- `netstandard2.1`：仅允许 TimeProvider 8.0.1、DiagnosticSource 8.0.1、Channels 8.0.0、Json 8.0.6、Encoding.CodePages 8.0.0，详见[旧目标框架说明](compatibility/core31.md)。

不允许第三方原生资产或意外依赖。

**独立消费者：** 四个现代目标框架都从全新缓存恢复 PackageReference 应用：

- 产品精确映射到本次本地 feed。
- 核准的兼容包及传递依赖映射到 nuget.org：`net6.0` 的 `Microsoft.Bcl.AsyncInterfaces 8.0.0`、`net8.0` 的 `System.Formats.Asn1 10.0.12`。
- SDK 所需的 `Microsoft.NETCore.App.Ref`、`Microsoft.AspNetCore.App.Ref` 也可从官方源获取，但它们不属于应用运行依赖。

消费者会核对目标框架和完整依赖闭包，在对应实际运行时执行公开 API、文化资源与回退 smoke，以及七个独立首次初始化场景。roll-forward 只允许当前主版本内；不能用 .NET 10 代替 .NET 6/8/9 的验证。

失败会终止准备并保留结果目录和退出码。`pack-smoke` 只用于定位打包问题，不能代替完整 `prepare`。

### .NET Core 3.1 检查

`legacy-package.sh` 消费同一个包，使用独立项目、缓存和源映射，确认编译和运行都选中 `lib/netstandard2.1`，且消费者 DLL 与包内字节相同。随后在实际 .NET Core 3.1.32 容器中执行既有 174 项断言，日志保存在准备输出下的 `legacy-*`。

默认使用官方 linux-x64 镜像。本机 arm64 可执行：

```sh
LEGACY_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:3.1.32-bullseye-slim-arm64v8 \
  bash eng/legacy-package.sh .local/nuget-release linux/arm64
```

该探针只使用合成数据和 loopback，不证明数据库认证。旧目标框架不支持 TLS，不能为了通过检查而降低证书策略。

本地命令可以验证未提交修改，但此时包中的 `RepositoryCommit` 仍是传入的 HEAD，不能单独证明该 commit 包含这些修改。正式发布前必须提交并合入全部修改，再对最终提交重跑准备和相关实库检查。

## 本地实库检查

离线 CI 没有本机数据库凭据，不能代替真实服务器验证。使用本次准备输出指向的同一个包和消费者，串行运行共享明文与严格 TLS smoke：

```sh
manifest="$(cat .local/nuget-release/manifest-path.txt)"
for tfm in net6.0 net8.0 net9.0 net10.0; do
  smoke="$(cat ".local/nuget-release/smoke-path-$tfm.txt")"
  scripts/with-dameng-test.sh dotnet "$smoke" plaintext "$manifest" "$PWD"
  scripts/with-dameng-tls-test.sh dotnet "$smoke" tls "$manifest" "$PWD"
done
```

运行前同样设置任务专用 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。如果准备时使用了自定义 `DOTNET_COMMAND`，这里也要使用同一宿主，确保四个运行时均可见。

只允许通过 `scripts/with-dameng-test.sh` 和 `scripts/with-dameng-tls-test.sh` 加载专用 TEST 配置。

每轮检查必须：

1. 验证 `WDM_PROVIDER_TEST` 用户和同名当前 Schema。
2. 测试参数、Reader、本地事务、时间值和 BLOB/CLOB。
3. 只清理本轮创建的精确唯一对象，并由新连接确认对象不存在。

共享明文检查显式使用 `PlaintextAllowed`；TLS 检查使用 `RequireTls` 并验证证书。本机无 CRL 的隔离 CA 可显式使用 `NoCheck`，产品默认 `Online` 不变。

缺少连接配置或无法完成正向验证时，将对应项目记录为 `integration_pending`，不能宣称完整发布验收。其他服务器版本、完整下游 EF 矩阵和长期资源压力需要另行验证。

## 4. 创建并推送 tag

完成源码审查、完整准备和相关实库验证后，确认最终 commit 已推送且包含全部发布修改。由维护者创建并推送 tag：

```sh
# 示例：仅在决定发布这个新版本且完成检查后执行。
git tag -a v0.3.2 -m 'W.DamengProvider 0.3.2'
git push origin v0.3.2
```

`.github/workflows/publish-nuget.yml` 只由 `v*` tag push 触发。工作流会解析版本，在 tag 指向的源码上重新运行四个现代运行时的发布准备和真实 .NET Core 3.1 包探针；全部成功后才使用仓库 secret `NUGET_API_KEY` 上传已检查包。

该 secret 只交给上传步骤，不写入源码、制品或本地配置。普通 CI 不上传包。

## 5. 上传后的核对

版本发布后不可覆盖，也不要移动 tag。发布失败时先查看具体失败步骤，不要盲目重复上传。

上传成功后：

1. 从公开源下载该版本。
2. 核对包 ID、版本和仓库 commit。
3. 在隔离消费者中验证公开安装。
4. 如果 NuGet 返回 404 或列表尚未更新，先检查索引传播情况。

首次改名和预览版发布见 [0.1.0-preview.1](release-0.1.0-preview.1.md)，当前版本验证见 [0.3.1](release-0.3.1.md)。

配置参考：[GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[NuGet package authoring best practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)。
