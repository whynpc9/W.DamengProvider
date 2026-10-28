# 构建与验证入口

本目录包含当前发布脚本和历史专项复现脚本。日常发布只需要先看[发布指南](../docs/release.md)，不要按文件名依次执行全部 `T*.sh`。

## 当前发布入口

### 完整发布准备

```sh
bash eng/release.sh prepare --tag <tag> --commit "$(git rev-parse HEAD)"
```

将 `<tag>` 替换为与项目版本一致的发布标签。该命令会：

1. 配置任务专用 CLI home 和单节点构建。
2. 在 .NET 6/8/9/10 实际运行时执行十三套离线测试。
3. 检查同一个 NuGet 包的五组目标框架资产和依赖。
4. 验证四个现代目标框架的独立包消费和首次初始化。

它不会上传 NuGet。完整参数、实库 smoke 和发布步骤见[发布指南](../docs/release.md)。

### .NET Core 3.1 包检查

```sh
bash eng/legacy-package.sh .local/nuget-release linux/amd64
```

该命令消费同一个包，在实际 .NET Core 3.1.32 容器中检查受限目标框架。CI 和 tag 发布都会调用；支持范围及 arm64 命令见[发布指南](../docs/release.md)。

### 局部检查

- `release.sh pack-smoke`：只执行打包和包消费，适合定位打包问题，不能代替完整 `prepare`。
- `release.sh version <tag>`：只校验并提取 SemVer。

## 历史专项复现入口

`t04.sh` 至 `t19.sh`、`temporal-precision.py` 及对应 `T*.md` 用于复现特定问题或历史样本。有些工具固定 `net10.0`、旧包版本或下游工程：

- 不代表当前四目标发布矩阵。
- 不构成发布前必须全部执行的清单。
- 运行前先阅读对应文档，确认版本、输入和输出范围。
- 实库操作仍遵守 [AGENTS.md](../AGENTS.md) 的 TEST 身份和对象清理规则。

`t17-profile.sh` 已退役，固定返回 `legacy_profile_retired` 和退出码 69；保留它只为明确拒绝旧调用，不恢复旧环境。

专项材料中的冻结、owner、批准和测试结果只解释当时任务。当前能力见[文档导航](../docs/implementation/README.md)，后续工作见[路线图](../docs/roadmap.md)。
