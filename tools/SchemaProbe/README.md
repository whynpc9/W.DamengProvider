# 元数据实库探针

此探针只使用 `DAMENG_TEST_CONNECTION_STRING`，验证认证用户和当前 Schema 都是 `WDM_PROVIDER_TEST`，并通过强类型 W Builder 显式选择 `PlaintextAllowed`。

必须通过现有 TEST wrapper 运行，不要把 secret 复制到工具中，也不要加载管理员配置。

## 构建与运行

```sh
export DOTNET_CLI_HOME=/tmp/wdm-schema-dotnet
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$DOTNET_CLI_HOME"
dotnet build tools/SchemaProbe/SchemaProbe.csproj --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false
scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll --identity-only
scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll
scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll --full
```

如果当前 worktree 没有本地配置，可在 worktree 根目录调用主 checkout 的现有 wrapper；不要复制 secret。原环境使用的示例为：

```sh
/home/air/Projects/W.DamengProvider/scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll --full
```

## 默认模式

观察普通查询、参数化查询、带引号标识符/类型和有副作用函数的同步/异步 `Prepare`。

副作用函数先实际执行一次作为正控制，后续描述操作必须保持计数不变。内部反射只读取已发送请求的 opcode、execute 标志和列描述，不输出 SQL、原始帧或凭据。

## --full

额外检查：

- 所有公开元数据集合和过滤条件。
- 复合主键/外键顺序、视图、decimal/time/LOB 元数据。
- `SchemaOnly` / `KeyInfo` 的同步/异步 Reader 行为；Schema-only 读取和 NextResult 不得发送 Fetch。
- 别名、表达式、JOIN、视图未知键、无约束表负例、缺失对象、取消和会话复用。

目录错误必须报告，不能当成空集合。当前 TEST 无权读取 `V$RESERVED_WORDS`，因此 `ReservedWords` 会分别验证同步/异步返回服务器错误 `-5504`，连接进入 Broken 且不可复用。其他 15 个集合必须返回非空结果，Schema 和值一致；不增加授权。

## 清理与结果

每轮创建唯一对象，记录成功创建的名称，在 `finally` 中按相反顺序精确清理。JSON 包含认证身份、断言、安全错误诊断和这些精确对象的最终目录计数；同时保存命令退出码。

缺少连接或身份检查失败记为 `integration_pending`，不是实库通过。本工具只测试 W 驱动。
