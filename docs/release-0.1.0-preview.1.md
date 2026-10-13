# W.DamengProvider 0.1.0-preview.1

2026-10-13 发布。状态：`published_and_public_install_verified`。这是面向 .NET 10 的驱动预览版本，支持边界见 [README](../README.md) 与 [R3 兼容说明](compatibility/R3.md)。

## 发布身份

| 项目 | 值 |
| --- | --- |
| NuGet | [W.DamengProvider 0.1.0-preview.1](https://www.nuget.org/packages/W.DamengProvider/0.1.0-preview.1) |
| tag | [v0.1.0-preview.1](https://github.com/whynpc9/W.DamengProvider/tree/v0.1.0-preview.1) |
| commit | `a67a9298daaa42b18203a9b405ec05a3357ed4b0` |
| 普通 CI | [38338843508：成功](https://github.com/whynpc9/W.DamengProvider/actions/runs/38338843508) |
| tag 发布 | [38339061188：成功](https://github.com/whynpc9/W.DamengProvider/actions/runs/38339061188) |
| 公开 nupkg SHA-256 | `03e7f9161b2d120e29f8e267d25dc62cef3e4f94ba8c032cb774449dcfec07ba` |
| 产品 DLL SHA-256 | `a265cd965ee8dde007d5198a283289a34b3e8516f7e8310f2d43b9a37cd3ab1d` |

## 已完成验证

- tag 工作流：十二套离线 **2050/2050**、发布 Python 检查 **15/15**、七项独立首次初始化 **7/7**，包审计与独立消费者通过，随后上传同一受测包。
- 实际上传的 Actions 包：独立消费者构建及离线 smoke 退出 0；shared 明文和隔离严格 TLS TEST smoke 均退出 0。两环境先确认用户与当前 Schema 为 `WDM_PROVIDER_TEST`，参数、Reader、事务、时间值、BLOB/CLOB 检查通过，新连接确认本轮精确对象不存在。
- 公开 NuGet 包：唯一公开源、全新缓存、精确 PackageReference 安装及离线 smoke 通过，签名验证退出 0。NuGet 新增 `.signature.p7s`；其余 ZIP 项与 Actions 原包逐项相同，实际加载 DLL 哈希一致。公开安装阶段没有重复连接数据库，实库结论属于上述 Actions 同载荷验证。
- 2026-10-13 文档同步时再次从公开源读取 nupkg，核对 ID、版本、repository commit、五个程序集资产和公开包 SHA-256，均与发布回执一致。

Actions 上传前 nupkg SHA-256 为 `71371d9de32a788950c0463f9ba511069bcde6fae21afbccc6e8006b5d015400`，与公开签名后 ZIP 的哈希不同是已核实的签名差异。发布前本地 v1/v2/v3 样本另见[历史检查](release-preflight.md)，其哈希不作为正式包身份。

本机脱敏回执保存在 ignored `.local/release-prep-20261013/published-verification/` 的 `release-receipt.json`、`actions-consumer/report.json`、`public/readback.json` 和 `public-consumer/report.json`；这些是本机证据定位，不是公开仓库下载链接。

## 覆盖边界

本次接受的是上述预览发布范围；不宣称完整下游 EF 矩阵、长时资源压力、额外 OS/RID、其他达梦版本或 .NET 6/8/9 已验收。后续工作见[路线图](roadmap.md)，EF 下游接入由下游项目单独管理。
