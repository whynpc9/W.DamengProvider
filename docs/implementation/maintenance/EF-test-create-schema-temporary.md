# EF 功能套件临时 CREATE SCHEMA 授权

状态：用户于 2026-10-12 在本任务中明确选择“临时授权”：仅为一次 EF 功能套件运行授予 TEST，运行后立即撤销。本页不含口令或连接串。实际执行与核验结果见下方“执行记录”。

## 原因

EF `DamengReverseEngineeringFunctionalTests` 中 12 项测试以 TEST 身份 `CREATE SCHEMA "EF10_<nonce>"`，在其中建表/序列/外键，最后 `DROP SCHEMA ... CASCADE`。TEST 账号没有该权限，服务器返回 -5512（`没有创建模式权限`）。这些 schema 由 TEST 自己创建和拥有，测试不访问其他用户的 schema。

## 操作

维护工具 [tools/TestSchemaPrivilege](../../../tools/TestSchemaPrivilege/Program.cs) 只接受三个固定参数，各执行一条语句：

```sql
GRANT CREATE SCHEMA TO "WDM_PROVIDER_TEST";    -- grant（管理员连接）
REVOKE CREATE SCHEMA FROM "WDM_PROVIDER_TEST"; -- revoke（管理员连接）
```

`inspect` 使用新的 TEST 连接只读核对自身权限行与自有 `EF10_` schema 数量。

范围：不加 ADMIN OPTION；不授 DBA、ANY 或任何对象权限；不改 DEV、其他用户、表空间或实例配置。管理员 secret 只在 grant/revoke 两次调用中由子 shell 加载，不进入文件、日志或测试进程；测试仍只用 TEST wrapper。

## 执行记录

见 [EF 功能兼容缺口报告](../ef-functional-compat-gaps.md) §4，包含 grant 前后计数、测试运行、revoke 前后计数和 TEST 新连接复查。

## 回滚

revoke 即回滚。若 revoke 失败，TEST 将保留 CREATE SCHEMA，必须再次运行 `revoke` 并以 `inspect` 确认 `create_schema_privileges=0`。
