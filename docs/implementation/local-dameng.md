# 本地达梦开发与测试环境

本文说明项目专用账号、连接配置和使用边界，不包含密码或完整连接串。环境于 2026-09-29 在用户提供的实例上创建并验证。

## 账号与用途

| 用途 | 数据库账号 / 默认 Schema | 本地配置文件 | 使用范围 |
| --- | --- | --- | --- |
| 自动化集成测试 | `WDM_PROVIDER_TEST` | `.local/secrets/dameng-test.env` | 默认可重复实库验证 |
| 手工开发探测 | `WDM_PROVIDER_DEV` | `.local/secrets/dameng-dev.env` | 临时 SQL、协议和兼容性探测 |
| 限定维护 | 用户提供的 SA | `.local/secrets/dameng-admin.env` | 仅用于专用用户创建和明确授权的权限维护 |

达梦通过用户和 Schema 隔离对象；创建用户会建立同名默认 Schema。这里的 TEST 和 DEV 是同一实例中的两个逻辑工作空间，不是两个物理数据库实例或表空间。详见[达梦用户与模式说明](https://eco.dameng.com/document/dm/zh-cn/pm/management-pattern.html)。

两个项目用户的直接系统权限为：

`CREATE SESSION`、`CREATE TABLE`、`CREATE INDEX`、`CREATE VIEW`、`CREATE SEQUENCE`、`CREATE PROCEDURE`、`CREATE TRIGGER`。

2026-09-30 经用户单独批准，仅 TEST 增加 `SOI` 角色，用于 EF 迁移所需的非审计/安全系统目录读取。TEST 新连接已确认 `ADMIN_OPTION=N`，目录查询和迁移锁可用；DEV 未变。

不授予 DBA、`ANY` 权限或其他业务 Schema 的对象权限。维护记录见[TEST 系统目录访问](maintenance/T12-test-catalog-access.md)。FLDR、HA、分布式事务等高级测试需要另行核对权限，不能静默提升账号权限。

## 自动化测试如何加载连接

自动化测试只读取 `DAMENG_TEST_CONNECTION_STRING`。从仓库根目录运行：

```bash
scripts/with-dameng-test.sh <测试命令及参数>
```

脚本加载本机配置后执行命令，不输出连接串。测试项目和独立探针都应通过该入口加载配置；不要把连接串作为命令行参数、写入项目配置或提交到 Git。

手工开发探测才在受控 shell 中显式加载 `.local/secrets/dameng-dev.env`，变量名为 `DAMENG_DEV_CONNECTION_STRING`。普通开发和测试不得加载 SA 配置。

### Windows

```powershell
scripts/with-dameng-test.ps1 -Command <命令> -CommandArguments @(<参数>)
```

PowerShell wrapper 将固定 TEST 文件作为数据解析，结束后恢复进程环境。

2026-10-15 的 issue #11 经单独授权创建 `WDM_PROVIDER_TEST_I11_7C42A9D1` 及同名 Schema，以保留实例已有 TEST。该历史验证需显式传 `-ExpectedUser WDM_PROVIDER_TEST_I11_7C42A9D1`，探针仍逐连接检查 USER/Schema；默认账号规则不变。详见[Windows 记录](../compatibility/I11-windows.md)。

## 本地文件与安全

- `.local/` 已加入 `.gitignore`。
- secret 目录权限为 `0700`，env 文件权限为 `0600`。
- 切换主机或用户时，由环境所有者通过安全渠道重新提供凭据，不从 Git 恢复。
- CI 应使用自己的受保护 secret 和专用测试实例，不复制本机 secret。

## 明文与 TLS

新驱动默认 `RequireTls`，在完整 TLS 传输建立前会拒绝连接。已授权的共享明文测试使用 `eng/t04.sh real`，探针读取原 TEST 配置后，通过强类型 W Builder 显式选择 `PlaintextAllowed` 并设置测试 Schema；不要把 W 专用键写进共用配置。

`PlaintextAllowed` 不允许跳过证书校验。服务器协商不支持的安全模式时仍会拒绝。共享明文结果不能替代[隔离 TLS 实库验证](local-dameng-tls.md)。

## 初始化记录（2026-09-29）

以下只记录当次环境创建，不是当前产品验证结果：

- 使用官方 `DM.DmProvider 8.3.1.47463` 的 `net9.0` 资产，在 .NET 10 临时程序中连接 SA，确认可以创建用户，且创建前没有 `WDM%` 项目用户。
- 创建 TEST 和 DEV，授予上述权限。
- 分别用两个新账号登录，在各自默认 Schema 中建表、插入、查询和删表，均成功；探测表已清理。
- 再次核对两个账号的直接系统权限恰为上述 7 项。TEST 读取 DEV 的探测表被服务器拒绝，该表随后清理。
- 当次未验证 TLS、故障/取消、批处理或新驱动行为，只证明官方驱动的基本连接和 CRUD。

后续测试仍须遵守 [AGENTS.md](../../AGENTS.md)：先检查登录身份，只清理本轮对象，并记录最终数据库状态。官方驱动、恢复版和新驱动的结果分别记录。
