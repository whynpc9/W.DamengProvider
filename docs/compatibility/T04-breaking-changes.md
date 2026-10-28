# 与旧版驱动相比的配置变化

本文面向从旧驱动迁移到当前 [0.3.1](../release-0.3.1.md) 的开发者。连接键和默认值见[连接选项](T04-options.md)，异步与数据源行为见[异步和取消](R2.md)及[数据源与 LOB](R3.md)。

## 连接与配置

- 连接配置在构造 `DmConnection` 或给 `ConnectionString` 赋值时解析为独立快照。只有 `Closed` 状态可以替换配置；`Open()` 进入网络流程后，配置 setter 会抛 `InvalidOperationException`。
- 不再提供默认管理员账号或密码。缺少 Host、User、Password 时，会在联网前拒绝。
- 默认 `TransportSecurity=RequireTls`，必须通过证书链、主机名和有效期检查。可信的本地明文测试环境必须显式设置 `PlaintextAllowed`；不支持的服务器加密模式会明确拒绝，不会接受任意证书或自动降级。
- `DmConnection.ConnectionTimeout` 返回秒，非零毫秒向上取整；连接串 `connect_timeout` 仍按毫秒输入。`DmCommand.CommandTimeout` 默认 30 秒，0 表示不限时。
- 默认 `PersistSecurityInfo=false`。成功打开后，公开 `ConnectionString` 和 `Password` 不显示密码；关闭或重新打开失败后仍保持脱敏，直到显式重新赋值。克隆和重新打开使用私有快照中的原始认证信息。
- `Schema` 只能在关闭状态设置。非空 Schema 会作为单个双引号标识符执行 `set schema`，保留大小写并转义内部双引号；空 Schema 不发送语句。物理 catalog 切换不支持。
- Builder 的配置读取仍可能返回密码；日志和诊断应使用 `ToRedactedString()`。

## 保留类型但未开放的功能

以下功能不能用非默认值启用：语句缓存、自动重连、读写分离、Enlist、HA、压缩、SkyWalking、服务文件和旧日志选项。

以下入口会明确拒绝：

- `EnlistTransaction`、Reconnect、FLDR、`DmFldrExport` 和两代 BulkCopy。
- 旧 `IFilterInfo.filterHead` 的非 `null` 值和共享 `BaseFilter` 链。
- 非 None 的 `DmTrace.Level`；旧 `DmTrace`、`Logger`、`LogWriter` 输出路径不执行实际操作。
- 原生 `DmFldrDllCall` 已从 public 改为 internal。

`CanCreateDataSourceEnumerator=false`。现代目标框架支持 `DbBatch`，`netstandard2.1` 不提供；详见[数组绑定与 DbBatch](I07-I08-array-binding-dbbatch.md)。

保留某些兼容类型或签名不代表对应功能可用。直接池化和 `DmDataSource` 的当前行为见[数据源与 LOB](R3.md)。
