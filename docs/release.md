# W.DamengProvider 发布指南

## 版本与身份

- 当前已发布 `0.1.0-preview.1`；发布 commit、验证与公开包哈希见[发布记录](release-0.1.0-preview.1.md)。以下流程用于后续新版本，不要重新创建已存在的 tag。
- NuGet 包、产品项目和程序集：`W.DamengProvider`；公开命名空间：`W.Dm`。
- 产品源码：`src/W.DamengProvider/W.DamengProvider.csproj`。旧 `W.DmProvider` 包依赖和程序集引用必须更新并重新编译；测试项目及诊断源名称保留原标识。
- 默认开发版本为 `0.1.0-preview.1`。发布版本由 `v` 开头的合法 SemVer tag 决定，例如 `v0.1.0-preview.1`、`v0.1.0`；不能将其他分支推送或 PR 当成发布操作。
- SDK 按 `global.json` 固定为 10.0.401，运行目标为 .NET 10。

## 发布前检查

在待发布源码上执行（这些命令不会上传 NuGet）：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.1.0-preview.2 --commit "$(git rev-parse HEAD)" --output .local/nuget-release
```

`dotnet` 不在 PATH 时设置 `DOTNET_COMMAND` 为实际可执行文件路径。入口配置任务专用 `DOTNET_CLI_HOME`，关闭首次初始化与遥测，并使用单节点、禁用 build server 和共享编译参数。

该入口顺序执行十二套离线测试，要求正式 TRX 无失败、跳过或中止；打包并核对 package ID、版本、许可证、README、上游声明、仓库 commit、程序集与四套语言资源、无第三方原生资产或额外运行时 NuGet 依赖；然后从仅含本次包的本地源和全新缓存恢复独立 PackageReference 应用，运行公开 API/资源 smoke 及七个独立首次初始化场景。失败会终止准备，保留结果目录及命令退出码。`pack-smoke` 仅供受影响打包检查，不代替完整 `prepare`。

本地命令可验证未提交修改，但包中 `RepositoryCommit` 此时仍是传入的 HEAD；它不能单独证明该 commit 包含未提交源码。正式发布前须提交并合入待发布版本，再对该版本重跑准备与相关实库检查。

## 本地实库检查

离线 CI 不含本地数据库凭据，不能代替实际服务器验证。使用本次准备输出指向的同一个包及 consumer，串行运行 shared 和严格 TLS smoke：

```sh
smoke="$(cat .local/nuget-release/smoke-path.txt)"
manifest="$(cat .local/nuget-release/manifest-path.txt)"
scripts/with-dameng-test.sh dotnet "$smoke" plaintext "$manifest" "$PWD"
scripts/with-dameng-tls-test.sh dotnet "$smoke" tls "$manifest" "$PWD"
```

仅使用 `scripts/with-dameng-test.sh` 与 `scripts/with-dameng-tls-test.sh` 加载专用 TEST 配置。

检查必须实际验证 `WDM_PROVIDER_TEST` 用户及同名当前 Schema，再测试参数、Reader、本地事务、时间值和 BLOB/CLOB；仅清理本轮创建的精确唯一对象，并由新连接确认最终对象不存在。shared 明确使用 `PlaintextAllowed`；TLS 使用 `RequireTls` 和证书验证，本机无 CRL 的隔离 CA 可显式 `NoCheck`，不改变产品默认 Online。无连接配置或无法完成正向验证时记录 `integration_pending`，不宣称完整发布验收。

其他服务器版本、完整下游 EF 矩阵和长期资源压力测试需按实际使用场景另行验证；本轮 smoke 不扩大既有支持声明。

## 创建发布 tag

完成源码审查、完整准备和相关实库验证后，确认待发布 commit 已推送且包含全部发布修改。由维护者创建并推送 tag：

```sh
# 示例：仅在决定发布这个新版本且完成检查后执行。
git tag -a v0.1.0-preview.2 -m 'W.DamengProvider 0.1.0-preview.2'
git push origin v0.1.0-preview.2
```

`.github/workflows/publish-nuget.yml` 仅由 `v*` tag push 触发，严格解析版本，在 tag 指向的源码上重新运行发布准备，成功后才使用仓库 secret `NUGET_API_KEY` 上传本次已审计包到 nuget.org。该 secret 仅交给上传步骤；不写入源码、制品或本地配置。普通 CI 不上传包。

版本发布后不可覆盖，也不要移动 tag；发布失败应先查看具体失败步骤。上传成功后，从公开源下载该版本，核对 ID/版本/仓库 commit，并在隔离 consumer 中验证安装。NuGet 返回 404 或列表尚未更新时先检查传播情况，不盲目重复上传。

首次改名与发布准备已由 PR #5 合入；`v0.1.0-preview.1` 已完成发布。最终发布包的验证见[发布记录](release-0.1.0-preview.1.md)。

配置依据：[GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、[NuGet package authoring best practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)。
