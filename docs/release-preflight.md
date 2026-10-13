# W.DamengProvider 发布前检查（2026-10-13）

本报告保留 **发布前** 的本地样本和检查结果：当时尚未创建 tag、上传 NuGet 或运行新 GitHub Actions。随后 `0.1.0-preview.1` 已于 2026-10-13 发布并完成公开安装验证，当前结论见[发布记录](release-0.1.0-preview.1.md)。下文 v1/v2/v3 的包哈希和实库结果仍归属原样本，不替换为正式发布包。

## 最终改动

- GitHub 仓库已更名为 `whynpc9/W.DamengProvider`，本地 origin 和发布文档/包元数据链接同步。

- 产品项目、程序集和 NuGet 包改为 `W.DamengProvider`，公开命名空间仍为 `W.Dm`；测试程序集与诊断源保留原名称。根 README 为 NuGet 展示文档，增加搜索标签、仓库 URL 和发布 commit 元数据。
- 当前工作树移除研究源码、官方样本二进制包、分析报告、恢复工具及历史生成证据；原件保存在 ignored `.local/release-prep-20261013/archive/` 和 `.local/archive/nuget-preparation-historical-inputs/`。Git 历史不重写。保留产品兼容性、TEST 安全指南、Apache 许可证及 DM 上游版权声明。
- 普通 CI 只做检查；`publish-nuget.yml` 只由 `v*` tag push 触发，严格解析 SemVer，从 tag 推导包版本。十二套离线、包内容检查、独立包消费者和七个首次初始化场景通过后，才将原始受测包推往 nuget.org。`NUGET_API_KEY` 仅提供给上传步骤。
- SDK 固定到 10.0.401；修正 Actions 临时目录配置。包描述准确说明容量管理，避免将归还即丢弃的会话管理描述为已实现物理复用。不可继续执行的旧研究模式明确拒绝并指向新发布入口。
- 对照基线 HEAD，305 个产品 C# 源码与资源文件内容均未改变；程序集和包元数据变更要求下游更新依赖并重新编译。

## 实际检查

独立验证由 Sol Low 执行；发布配置及代码由 Astra High 只读审查，无剩余阻断项。基线 HEAD 为 `0c456218fbe9650bfcd3d1291bac804a839e71ed`，受测内容包含本分支未提交的发布准备 diff。

| 检查 | 结果 |
| --- | --- |
| 十二套离线测试 | 2050/2050，正式 TRX 无失败、跳过或中止 |
| 发布 Python 行为检查 | 15/15 |
| 受影响资源工具 Python 回归 | 50/50 |
| 两份 workflow YAML | 解析通过；不代表已在 GitHub 运行 |
| 实际 `.nupkg` 审计 | 包名/版本、Apache 许可、README/归属、repository commit、五个程序集资产、无额外运行时包依赖均符合 |
| 独立消费者 | 唯一本地包源、全新缓存、真实 PackageReference；程序集与四个语言资源 SHA 匹配 |
| 七项首次初始化场景 | 7/7，独立子进程执行 |
| shared 明文 TEST smoke | exit 0，明确 PlaintextAllowed；实际用户/当前 Schema 均 WDM_PROVIDER_TEST |
| 隔离严格 TLS TEST smoke | exit 0，RequireTls/证书验证；本机无 CRL 的 CA 显式 NoCheck |
| 同包数据库业务 | 两环境参数化写入、Reader、事务提交/回滚、Scale=6 DateTime、BLOB/CLOB 往返通过 |
| 数据库最终状态 | 两个本轮 GUID 表均由新连接确认不存在；首轮失败分配的名称另行只读确认 count=0 |
| 当前树静态检查 | git diff --check 通过；无研究关键词和指向已删除研究工具的引用 |

### 首轮失败与修补

v1 完整离线及包消费通过，明文 smoke 首个身份查询使用 `CURRENT_SCHEMA` 时返回服务器 `-2111`，尚未完成身份验证和 CREATE。保留失败日志，不将该轮视为实库通过。随后仅将 smoke 身份 SQL 改成仓库已有的 `SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL`，并修正包描述；产品运行时代码与测试断言未变。

v2 重新 pack、审计并消费实际新包，重跑七项首次初始化及十五项发布检查，再以同包通过两环境实库检查。完整 2050 项离线对应 v1 的相同产品逻辑；没有将 v1 包 SHA 冒充最终包身份。

## v2 实库受测样本与复核入口

- 包：`W.DamengProvider.0.1.0-preview.1.nupkg`
- 包 SHA-256：`56385367967c2d4d1f55bad34316eea3afb5eb84d3c8a09f782786b7ba73308d`
- 产品 DLL SHA-256：`589425493a4b80c05d19a7fd1df2e8139c325c6b1a2823e794b90a35b0de812f`
- 安全结果、命令及退出码：`.local/release-prep-20261013/verification/report-v1.md`、`report-v2.json`、`plaintext-v2.log`、`tls-v2.log`；最终包/manifest/consumer 指针在该目录 `release-v2/`。

关键命令（本机使用 `/Users/wanghongyi/.dotnet/dotnet`）：

```sh
DOTNET_COMMAND=/Users/wanghongyi/.dotnet/dotnet bash eng/release.sh prepare --tag v0.1.0-preview.1 --commit "$(git rev-parse HEAD)" --output .local/release-prep-20261013/verification/release-v1
DOTNET_COMMAND=/Users/wanghongyi/.dotnet/dotnet bash eng/release.sh pack-smoke --tag v0.1.0-preview.1 --commit "$(git rev-parse HEAD)" --output .local/release-prep-20261013/verification/release-v2
```

实库通过对应最终 `release-v2` 指针中的 consumer/manifest，通过专用 TEST wrappers 串行执行 `plaintext` 与 `tls` 模式。未加载管理员凭据、未修改授权、未执行跨 Schema 或实例设置操作。

## 仓库改名后的链接同步

GitHub 仓库与 origin 已改为 `whynpc9/W.DamengProvider`；README 的仓库链接、项目 RepositoryUrl/PackageProjectUrl 和包审计器期望 URL 已同步。仅这些链接变化没有修改产品运行时逻辑；对应 v3 打包及包元数据检查通过，独立包消费、七项初始化、十五项发布 Python 检查全部通过；实库结果仍明确归属上述 v2 样本。v3 产品 DLL SHA 与 v2 完全相同，没有重复执行实库。v3 包 SHA-256 为 `47b481d61a5a5cf4d208742a7d40429d2f3a7318b3a1dcd97b8892c789593dc0`；制品及日志位于 `.local/release-prep-20261013/verification/release-v3/`，汇总见 `verification/report-v3.json`。

## 发布边界

上述本地检查样本中的 repository commit 是检查基线 HEAD；它不能证明该 HEAD 已包含当时未提交改动。后续发布使用包含全部修改的 `a67a9298daaa42b18203a9b405ec05a3357ed4b0`，已通过 CI 和 tag 发布工作流。正式 Actions 包另行完成实库 smoke，公开包另行完成安装及签名验证，详见[发布记录](release-0.1.0-preview.1.md)。

本地准备没有覆盖完整下游 EF 矩阵、600 秒资源压力回归、其他 OS/RID 或其他服务器版本。改名后的 EF 适配层依赖更新及重新编译由下游项目处理。后续公开发布没有自动扩大这些覆盖范围；本报告本身仍只证明所列本地样本的检查。
