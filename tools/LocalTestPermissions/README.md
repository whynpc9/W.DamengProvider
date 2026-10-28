# TEST 系统目录角色维护工具

## 当前只允许只读 inspect

当次固定 SOI 授权已经完成，并由普通 TEST 新连接确认角色存在、`ADMIN_OPTION=N`。原维护工具最初只识别 `NO`，因此保留了后置检查失败记录；后续已补充识别实际返回值 `N`，没有再次连接管理员或重复 GRANT。

**当前不得重跑 execute，不得重复管理员连接、GRANT 或 REVOKE。** `--approval-granted` 只是历史执行选择器，不能代替新的授权。

普通只读检查命令：

```text
scripts/with-dameng-test.sh python3 tools/LocalTestPermissions/run.py inspect
```

## 历史写操作范围

唯一写操作曾固定为：

```sql
GRANT SOI TO "WDM_PROVIDER_TEST"
```

不带 ADMIN OPTION，不接受 SQL、账号、角色、连接串或 secret 路径参数；不授予 DBA/ANY，不修改默认角色、DEV、其他用户或实例配置。工具没有 REVOKE 模式。任何失败都返回安全结果，不扩大权限、不自动重连或重放。

历史 execute 形式如下，**仅为记录，当前不得执行**：

```text
python3 tools/LocalTestPermissions/run.py execute --approval-granted
```

当时只有 execute 能读取固定 `.local/secrets/dameng-admin.env`，并执行以下限制：

- 拒绝路径和项目中的 symlink；文件必须是当前用户拥有的 regular file、0600，父目录为当前用户拥有且 0700，并经 `git check-ignore` 确认忽略。
- 使用 no-follow descriptor 检查，最多读取 64 KiB，不自动改权限。
- 按 shlex 数据解析单个 `DAMENG_ADMIN_CONNECTION_STRING=...` 赋值，可含 export 和注释；不执行 source/eval、变量或命令替换。
- 不读取或复制 TEST、DEV、bootstrap 配置。连接值只传给子进程私有 `WDM_PROVIDER_MAINTENANCE_CONNECTION_STRING`，C# 读取后清除，不写命令行、manifest 或日志。
- 强类型 Builder 和实际 `SELECT USER FROM DUAL` 都必须确认 SA；ConnectTimeout/ReadIdleTimeout/CommandTimeout 为 20 秒，CleanupTimeout 为 5 秒。
- 共享实例显式 `PlaintextAllowed`，不关闭证书校验，不写共享配置新键；不使用事务或修改会话默认角色。

连接后只读 `DBA_ROLE_PRIVS` 的固定 GRANTEE/GRANTED_ROLE。仅角色不存在时执行固定 GRANT，再读回要求恰好一行且 ADMIN_OPTION 为 `N` 或 `NO`。已有无 ADMIN OPTION 的 SOI 可按幂等只读成功；YES/Y、未知值或多行直接拒绝，不覆盖或撤销现有权限。管理员连接随后关闭。

输出只包含固定角色、结果 code、异常 type/数值 provider code 和 proof 布尔值，不包含 Message、Data、连接配置、SQL、认证信息、环境值或子进程原始输出。进程上限 120 秒；超时或断链不能证明 GRANT 未发生，后续必须独立核验状态。

## 构建

```text
python3 tools/LocalTestPermissions/run.py build
```

项目使用仓库 W.DamengProvider 的 ProjectReference。构建使用 .NET 10、临时可写 CLI home、关闭首次体验/遥测、单节点和禁用 build servers，不读取任何 DAMENG 配置。

输出位于忽略目录 `.local/maintenance/LocalTestPermissions/`；manifest 保存四个工具源文件及工具/驱动 DLL、deps/runtimeconfig 的 SHA256。没有构建不能执行，输入或制品变化后必须重新构建。

## 普通 TEST 只读 inspect

inspect 的 Python 分支只读取 wrapper 提供的 `DAMENG_TEST_CONNECTION_STRING`，不打开、读取或 stat 管理员配置，也不调用管理员解析函数。连接值传给子进程私有 `WDM_PROVIDER_TEST_INSPECTION_CONNECTION_STRING`；C# 使用独立的 `inspect-soi-as-test` 方法，不读维护环境变量。

强类型 Builder、实际 USER 和 CURRENT_SCHEMA 都必须是 `WDM_PROVIDER_TEST`，连接/命令 20 秒、清理 5 秒。

除两条身份 SELECT 外，唯一查询固定为：

```sql
SELECT ADMIN_OPTION FROM USER_ROLE_PRIVS WHERE GRANTED_ROLE = 'SOI'
```

不接受 SQL、账号或查询参数，不授予或撤销权限。最多记录 16 行；超出时记录有界 observed count、标记计数不完整并关闭连接。

输出包括声明字段 CLR 类型、各 flag 实际 CLR 类型、行数和完整计数 proof。直接输出只允许 bool、可无损拓宽的有界整数，或长度不超过 8 且严格属于 NO/YES/N/Y/0/1/是/否的字符串；未知值只输出类型和 SHA256，不输出原值或 Message。未知类型和值不会自动获得许可。

`inspection_completed` 只表示只读观察和关闭成功，不表示权限后置条件已验收。SOI 的权限范围和维护依据见[维护文档](../../docs/implementation/maintenance/T12-test-catalog-access.md)。
