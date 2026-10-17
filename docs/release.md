# W.DamengProvider 发布指南

## 版本与身份

- 当前已发布 `0.3.0`，四运行时公开安装及签名验证通过；发布 commit 和覆盖范围见[发布记录](release-0.3.0.md)。以下流程用于后续新版本，不要重新创建已存在的 tag。
- NuGet 包、产品项目和程序集：`W.DamengProvider`；公开命名空间：`W.Dm`。
- 产品源码：`src/W.DamengProvider/W.DamengProvider.csproj`。旧 `W.DmProvider` 包依赖和程序集引用必须更新并重新编译；测试项目及诊断源名称保留原标识。
- 项目默认版本为已发布的 `0.3.0`，见[发布记录](release-0.3.0.md)。后续发布由新的 `v` 开头合法 SemVer tag 决定；以下使用尚未发布的 `v0.3.1` 作为示例，不代表发布计划；使用前先调整项目版本。不能将普通分支推送或 PR 当成发布操作。
- SDK 按 `global.json` 固定为 10.0.401，产品和测试统一 C#12；安装 .NET 6/8/9/10 运行时。包另含受限 `netstandard2.1`，其实际 Core 3.1 检查使用 Docker，不以现代运行时替代。

## 发布前检查

在待发布源码上执行（这些命令不会上传 NuGet）：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.3.1 --commit "$(git rev-parse HEAD)" --output .local/nuget-release
bash eng/legacy-package.sh .local/nuget-release linux/amd64
```

`dotnet` 不在 PATH 时设置 `DOTNET_COMMAND` 为实际可执行文件路径。入口配置任务专用 `DOTNET_CLI_HOME`，关闭首次初始化与遥测，并使用单节点、禁用 build server 和共享编译参数。

该入口在每个现代 TFM 顺序执行十三套离线测试（含 Schema），共 52 份 TRX，要求正式 TRX 无失败、跳过或中止；同一次打包产出五组资产共二十五个托管程序集（每组主 DLL 和 en/zh-CN/zh-HK/zh-TW 四套语言资源）。包检查核对身份、版本、许可证、README、上游声明、仓库 commit 及每个依赖组：现代目标中 net6 允许 `Microsoft.Bcl.TimeProvider 8.0.1`、net8 允许 `Microsoft.Bcl.Cryptography 10.0.12`，net9/10 为空。Standard 资产仅允许 TimeProvider 8.0.1、DiagnosticSource 8.0.1、Channels 8.0.0、Json 8.0.6、Encoding.CodePages 8.0.0；详见[旧目标说明](compatibility/core31.md)。不允许第三方原生资产或意外依赖。

四个现代目标从全新缓存恢复独立 PackageReference 应用：产品精确映射到本次本地 feed，已核准的兼容包及其传递依赖（net6 `Microsoft.Bcl.AsyncInterfaces 8.0.0`、net8 `System.Formats.Asn1 10.0.12`）映射到 nuget.org。源映射另允许 SDK 所需的两个官方 reference pack ID（`Microsoft.NETCore.App.Ref`、`Microsoft.AspNetCore.App.Ref`），它们不属于应用运行依赖。验证所选 TFM 资产及依赖闭包，在对应真实 runtime 执行公开 API、文化资源和回退 smoke，以及七个独立首次初始化场景。消费者限制 roll-forward 在当前主版本内，运行时校验实际主版本，不能以 .NET 10 代替 6/8/9。失败会终止准备，保留结果目录及命令退出码。`pack-smoke` 仅供受影响打包检查，不代替完整 `prepare`。

`legacy-package.sh` 另以精确版本、独立项目/缓存和源映射消费同一个包，核对 compile/runtime 均选中 `lib/netstandard2.1`，且消费者 DLL 与候选包内字节相同，再在实际 .NET Core 3.1.32 容器执行既有 174 断言。日志保留在准备输出下的 `legacy-*`。默认镜像为官方 linux-x64；本机 arm64 可用 `LEGACY_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:3.1.32-bullseye-slim-arm64v8` 和第二参数 `linux/arm64`。探针只用 synthetic/loopback，不证明数据库认证。旧资产不支持 TLS；不能要求其通过现代严格 TLS 正向 smoke，也不能降级证书策略。

本地命令可验证未提交修改，但包中 `RepositoryCommit` 此时仍是传入的 HEAD；它不能单独证明该 commit 包含未提交源码。正式发布前须提交并合入待发布版本，再对该版本重跑准备与相关实库检查。

## 本地实库检查

离线 CI 不含本地数据库凭据，不能代替实际服务器验证。使用本次准备输出指向的同一个包及 consumer，串行运行 shared 和严格 TLS smoke：

```sh
manifest="$(cat .local/nuget-release/manifest-path.txt)"
for tfm in net6.0 net8.0 net9.0 net10.0; do
  smoke="$(cat ".local/nuget-release/smoke-path-$tfm.txt")"
  scripts/with-dameng-test.sh dotnet "$smoke" plaintext "$manifest" "$PWD"
  scripts/with-dameng-tls-test.sh dotnet "$smoke" tls "$manifest" "$PWD"
done
```

运行前同样设置任务专用 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` 和 `DOTNET_CLI_TELEMETRY_OPTOUT=1`；若准备时使用自定义 `DOTNET_COMMAND`，上述命令也须使用该宿主，确保四个 runtime 均可见。

仅使用 `scripts/with-dameng-test.sh` 与 `scripts/with-dameng-tls-test.sh` 加载专用 TEST 配置。

检查必须实际验证 `WDM_PROVIDER_TEST` 用户及同名当前 Schema，再测试参数、Reader、本地事务、时间值和 BLOB/CLOB；仅清理本轮创建的精确唯一对象，并由新连接确认最终对象不存在。shared 明确使用 `PlaintextAllowed`；TLS 使用 `RequireTls` 和证书验证，本机无 CRL 的隔离 CA 可显式 `NoCheck`，不改变产品默认 Online。无连接配置或无法完成正向验证时记录 `integration_pending`，不宣称完整发布验收。

其他服务器版本、完整下游 EF 矩阵和长期资源压力测试需按实际使用场景另行验证；本轮 smoke 不扩大既有支持声明。

## 创建发布 tag

完成源码审查、完整准备和相关实库验证后，确认待发布 commit 已推送且包含全部发布修改。由维护者创建并推送 tag：

```sh
# 示例：仅在决定发布这个新版本且完成检查后执行。
git tag -a v0.3.1 -m 'W.DamengProvider 0.3.1'
git push origin v0.3.1
```

`.github/workflows/publish-nuget.yml` 仅由 `v*` tag push 触发，严格解析版本，在 tag 指向的源码上重新运行四现代运行时发布准备及真实 Core 3.1 包探针，成功后才使用仓库 secret `NUGET_API_KEY` 上传本次已审计包到 nuget.org。该 secret 仅交给上传步骤；不写入源码、制品或本地配置。普通 CI 不上传包。

版本发布后不可覆盖，也不要移动 tag；发布失败应先查看具体失败步骤。上传成功后，从公开源下载该版本，核对 ID/版本/仓库 commit，并在隔离 consumer 中验证安装。NuGet 返回 404 或列表尚未更新时先检查传播情况，不盲目重复上传。

首次改名与预览版发布见 [0.1.0-preview.1](release-0.1.0-preview.1.md)；当前版本验证见 [0.3.0](release-0.3.0.md)。

配置依据：[GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[NuGet package authoring best practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)。
