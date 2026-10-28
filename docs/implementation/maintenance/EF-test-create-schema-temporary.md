# EF 功能套件临时 CREATE SCHEMA 授权记录

> 这是一次已完成的临时权限维护，不是后续测试的授权。不要重复 grant；当前普通测试仍不得加载管理员配置。

2026-10-12，用户明确批准仅为一次 EF 功能套件运行授予 TEST，并在运行后立即撤销。授权已执行和撤销；TEST 新连接确认权限行与自有临时 Schema 均为 0。本页不含密码或连接串。

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

2026-10-12 的单次运行：TEST 初查 CREATE SCHEMA 权限行 0；限定 grant 后新连接读回权限行 1、ADMIN OPTION 行 0；功能套件通过 230/234。之后限定 revoke，并在清理该轮精确登记的两个自有空 Schema 后，以 TEST 新连接确认权限行 0、自有 EF10_ Schema 0。

这是该历史样本的结果，不证明当前实例状态或当前源码全套通过。剩余 CREATE MATERIALIZED VIEW 权限不在当次授权中，未授予。完整逐轮报告可从 Git 历史查询。

## 回滚

revoke 即回滚。若 revoke 失败，TEST 将保留 CREATE SCHEMA，必须再次运行 `revoke` 并以 `inspect` 确认 `create_schema_privileges=0`。
