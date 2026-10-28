# W.DamengProvider 0.3.0 发布记录

> 历史版本记录；当前版本为 [0.3.1](release-0.3.1.md)。本页结果只适用于 0.3.0 及其当时环境。

发布日期：2026-10-17。公开安装、签名和实际运行时验证通过；正式签名包的九次实库 smoke 与精确清理通过，状态为 `published_and_public_install_verified`。

## 变更范围

- PR #26 新增受限 `netstandard2.1` 资产，供 .NET Core 3.1 应用和 Standard 2.1 类库使用。包共五组资产，每组主程序集和四套文化资源，共 25 个托管程序集。
- 旧资产保留 typed 数据源、同步/异步命令、事务、数组绑定和元数据；缺少标准基类成员的接口通过具体 `Dm*` 类型提供。
- 旧资产不提供 TLS、DbBatch、DateOnly/TimeOnly 映射或原生扩展。默认 `RequireTls` 保持拒绝；只有明确允许明文的服务器及显式 `PlaintextAllowed` 才进入明文登录，不能将不支持 TLS 解释为自动降级。
- 同步 DNS 等待也受 deadline / Close 控制；底层 DNS 无法取消时允许晚完成，并观察迟到异常。
- .NET 6/8/9/10 的 TLS、DbBatch 和类型契约保持原范围。旧运行时与上游兼容包的维护限制见 [Core 3.1 说明](compatibility/core31.md)，不扩大为 EF 或官方驱动 API 可直接替换。

## 发布前候选检查（历史证据）

2026-10-17，以 `main@a3125528ee84f775242b26da1e9e56e25b267017` 加本次准备 diff 为候选，包版本为 `0.3.0`，文件版本 `0.3.0.0`，程序集版本保持 `0.1.0.0`。包中 RepositoryCommit 指向基线，不能独自代表未提交差异。

准备入口：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.3.0 --commit "$(git rev-parse HEAD)" --output .local/release-0.3.0
bash eng/legacy-package.sh .local/release-0.3.0 linux/amd64
LEGACY_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:3.1.32-bullseye-slim-arm64v8 \
  bash eng/legacy-package.sh .local/release-0.3.0 linux/arm64
```

本轮完整 `prepare` **退出 0**；SDK 10.0.401，macOS 26.6.2 / arm64。四目标 52 份 TRX 共 **9634/9634**，失败、跳过、中止均为 0：

| 目标 | 实际 runtime | 离线测试 | 包消费/文化回退 | 首次初始化 | shared / 严格 TLS |
| --- | --- | ---: | --- | ---: | --- |
| net6.0 | 6.0.36 | 2410/2410 | 通过 | 7/7 | 均通过 |
| net8.0 | 8.0.20 | 2408/2408 | 通过 | 7/7 | 均通过 |
| net9.0 | 9.0.9 | 2408/2408 | 通过 | 7/7 | 均通过 |
| net10.0 | 10.0.12 | 2408/2408 | 通过 | 7/7 | 均通过 |

同一个 `W.DamengProvider.0.3.0.nupkg` 的 25 个托管资产、五组依赖、八个现代独立消费者及 **28/28** 首次初始化通过。结果在 ignored `.local/release-0.3.0/nuget-yubty2d4/`，顶层 package/manifest 指针绑定本次候选。

`eng/legacy-package.sh` 对该包在实际 .NET Core **3.1.32** 的 Linux arm64（本机 VM 原生）与 x64（模拟）均 **退出 0**，各 **8 组、174 断言**。独立包缓存、Standard compile/runtime 资产和消费者实际主 DLL 均核对到同一候选；TLS 的 12 种同步/异步 STARTUP/策略组合、DNS deadline/Close、数据源生命周期、数组/时间边界和诊断通过。日志在 `legacy-q8oLiW/`（arm64）与 `legacy-5jNx9e/`（x64），顶层 `legacy-path-*.txt` 指向各轮结果。六条上游兼容包对 Core 3.1 未支持/未测试的警告保留，不以通过有限探针延长上游维护承诺。

独立验证者串行执行现代 shared/严格 TLS **8/8 退出 0**；另使用既有 NuGet Smoke 的最小 Core 3.1 适配，仅更换哈希/Hex API 和资产映射，以精确 PackageReference 消费同包，在实际 **Core 3.1.32 / Linux arm64** 执行一次 `PlaintextAllowed` shared smoke，**退出 0**。九次均确认服务器用户及当前 Schema 为 `WDM_PROVIDER_TEST`；参数化 Unicode、Reader、时间、BLOB/CLOB、提交/回滚、预取消后的会话可用性与连接释放通过。每次仅删除本轮成功创建的精确 GUID 表，并由新连接确认 `owned_table_absent`，事务完成、连接关闭；没有重试。现代 TLS 使用 `RequireTls`，本机无 CRL 场景显式 NoCheck，产品默认 Online 不变。

现代命令/退出码与安全报告在 `.local/release-0.3.0/integration/`；旧目标适配源码、独立项目/缓存、命令及结果在 `legacy-database/`，其中 `report.json` 明确实际 runtime、Standard 资产和服务器最终状态。旧目标 x64 实库仍未验证；旧资产 TLS 明确不支持，未运行正向 TLS smoke。上述结果不扩大到完整旧目标过程/元数据实库矩阵、下游 EF、长期压力或其他 profile。

发布工具 Python **23/23**，Shell/内嵌 Python/项目 XML、文档本地链接及 `git diff --check` 通过。产品项目执行 `dotnet list ... package --vulnerable --include-transitive --format json --no-restore` **退出 0**，NuGet 公开源未报告已知漏洞；结果在 `dependency-audit.json`。独立审查与验证已复核包、TRX、消费者来源、双架构旧目标及九次实库结果，未留下阻断问题。历史 PR 候选计数保留原归属，不代替本轮结果。

基线 [a312552 的 CI 38988623799](https://github.com/whynpc9/W.DamengProvider/actions/runs/38988623799) 成功；该基线结果仅属准备阶段；正式提交的远端检查见下文。

## 发布配置

普通 CI 与 tag 发布均先运行完整四现代运行时测试、包审计、八个独立消费者及 28 次初始化，再通过同一个 `eng/legacy-package.sh` 验证 Core 3.1 的精确版本包消费。探针使用独立缓存、候选精确源映射，核对 Standard 编译/运行资产和实际消费者 DLL，并保留日志；tag 只有这些检查通过后才上传。当前 tag 配置的旧运行时环境为 Linux x64，不据此宣称所有平台支持。

## 正式发布与公开验证

- 发布提交：`870312e70bd094c7caf37eebf7ef902008f46ec2`；tag：[v0.3.0](https://github.com/whynpc9/W.DamengProvider/tree/v0.3.0)。
- [普通 CI 38992397052](https://github.com/whynpc9/W.DamengProvider/actions/runs/38992397052)与[tag 发布 38992526995](https://github.com/whynpc9/W.DamengProvider/actions/runs/38992526995)均成功。tag 的52份TRX共9634/9634，八个现代消费者、28次初始化及实际 Core 3.1 x64 的174断言通过；Actions实际现代 runtime 为6.0.36/8.0.32/9.0.21/10.0.13。
- 最终提交的 `pack-smoke` 退出 0：五资产组及四现代运行时包消费/28次初始化通过；25 个产品/资源 DLL 与上方9634全量及9次实库候选完全相同。最终提交另在实际 Core 3.1.32 / x64 再次执行174断言通过，结果在 `.local/release-0.3.0-final/nuget-4vt1_yct/` 与 `legacy-dUmdBG/`。
- 公开索引已包含0.3.0；全新消费者与缓存仅使用 nuget.org，从唯一公开源精确安装 `[0.3.0]`。八个现代消费者、28次初始化通过，实际 runtime 为6.0.36/8.0.20/9.0.9/10.0.12。两个旧目标消费者选中 `lib/netstandard2.1`，实际Core3.1.32 / arm64与x64各174断言通过，arm64离线Smoke通过。共10个消费者缓存的签名nupkg及加载的产品DLL均已核对。

Actions 上传原包 SHA256：`3214ca4d2b8d17fb5d10ac329153e13b8c115debff48c3f20e9355cd2e712752`。其20个资源 DLL 与本机候选相同，五个主 DLL 不同；本机实库结果不归给 Actions 载荷，正式载荷直接实库验证见下方结果。

公开包 SHA256：`349fc04e48bb8daeca347570d5338ce19d749e2ebf77aea5951753eb5d7c3614`。`dotnet nuget verify --all` 退出0，NuGet.org仓库签名有效；公开包相较上传原包只新增 `.signature.p7s`，其他ZIP项逐字节一致。公开验证32条命令全部退出0，独立复核了10个sole-nuget.org源配置、新缓存、精确PackageReference、实际资产与运行时日志。结果在 ignored `.local/release-0.3.0-final/public/`（`readback.json`、`package-manifest.json`、`consumers.json`、`legacy-consumer.json`及日志）；Actions原制品在同级 `actions/`。

公开正式签名载荷实库 **9/9退出0，无重试**：现代四目标各shared/严格TLS共8次，实际Core3.1.32 / Linux arm64明文1次。九份报告的包SHA均为上方公开包；均验证USER和当前Schema为 `WDM_PROVIDER_TEST`，参数Unicode、Reader、时间、BLOB/CLOB、提交/回滚、预取消后的会话可用性与释放通过。每轮只删除本次成功创建的精确GUID表，由新连接确认 `owned_table_absent`；事务提交/回滚完成，连接关闭。命令/退出码和安全报告在 `public/integration/` 与 `public/legacy-database/`。TLS配置保持RequireTls与证书验证，本机无CRL CA显式NoCheck，产品默认Online不变。

[GitHub正式Release](https://github.com/whynpc9/W.DamengProvider/releases/tag/v0.3.0)（非预览版）与[NuGet0.3.0](https://www.nuget.org/packages/W.DamengProvider/0.3.0)已公开。独立代码审查、制品/公开安装复核和实库验证均通过；README、发布指南、兼容入口和 `progress.json` 同步当前状态。旧x64实库仍未验证，旧TLS不支持；不扩大到完整旧目标过程/元数据矩阵、下游EF、长时压力或额外profile。
