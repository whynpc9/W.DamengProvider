# 构建与验证入口

发布准备入口例如 `bash eng/release.sh prepare --tag v0.2.1 --commit "$(git rev-parse HEAD)"`。它配置任务 CLI home 和单节点构建，在 .NET 6/8/9/10 实际运行时执行十三套离线测试，再验证同一个 NuGet 包的四目标资产、依赖、独立包消费及首次初始化。完整用法、实库 smoke 和发布操作见[发布指南](../docs/release.md)。该入口本身不上传包。

`release.sh pack-smoke` 只执行打包和包消费，适合定位打包问题；不能代替完整 `prepare`。`release.sh version <tag>` 只校验并提取 SemVer。

`t04.sh` 至 `t19.sh`、`temporal-precision.py` 及对应 `T*.md` 是专项复现入口。有些工具固定 net10、旧候选版本或下游工程，只适用于各自说明中的样本，不代表当前四目标发布矩阵，也不作为发布前的递归执行清单。运行前阅读对应文档；实库操作仍遵守 [AGENTS.md](../AGENTS.md) 的 TEST 身份和对象所有权边界。

`t17-profile.sh` 已退役，固定返回 `legacy_profile_retired` 和退出码 69；保留用于明确拒绝旧调用，不恢复旧 profile。其他专项材料中的冻结、owner、批准及历史结果只解释原任务。当前源码能力见[实现索引](../docs/implementation/README.md)，后续缺口见[路线图](../docs/roadmap.md)。
