# T04 不可变配置、默认值与能力守卫

保留配置解析与快照的设计背景。当前选项及支持范围以[配置说明](../compatibility/T04-options.md)为准，历史阶段状态不作为当前任务队列。

## 配置边界

公开 Builder 可编辑，连接在构造/赋值时取得独立的内部 `DmConnectionSettings`。设置只包含不可变值和只读副本；不得持有可变 Builder、EPGroup 或全局配置引用。属性字典提供给旧协议层时每次复制；协商/会话状态与用户设置分开保存。

`DmConnectionSettings.Parse(string)`、Builder 构造及自身字符串 setter 使用严格、事务性解析。别名统一到语义键，等价重复允许、冲突重复拒绝；保留值的大小写和引用/转义，不把密码和 Schema 转小写。未知键、格式错误和不支持的非默认选项抛固定安全异常，不附原值或原始 inner exception。

.NET 10 的 `DbConnectionStringBuilder.ConnectionString` setter 非虚方法，基类通过虚 `Clear` 和 indexer 逐项写入，无法由派生类统一保证整次赋值原子性。为确保基类赋值也不会绕过冲突验证，indexer 使用严格赋值：同义键重复等价允许，改值需先 `Remove`；typed 属性继续支持替换。自己的字符串 setter 事务性更新。实测基类会回滚 `ArgumentException`（含越界异常）到原有效配置，允许继续消费该原状态；`NotSupportedException` 路径不回滚，必须将 Builder 标为不可消费，直到 Clear 或成功重新赋值。测试分别覆盖两种路径，禁止笼统宣称基类原子。禁止访问基类私有字段或依赖调用栈猜测。

直接 indexer 校验失败也可能进入不可消费状态，不能用后续 Remove 解除；“先 Remove 再改值”指在尝试冲突赋值前操作。typed 属性的预校验失败不破坏旧配置。统一恢复方式为 Clear 或成功的派生字符串整体赋值。

另一个框架限制是基类字符串 setter 会在调用派生索引器前丢弃部分无引号空值，派生类无法判定原文是否包含这些键。自有解析器保留空值并拒绝空数值；连接的字符串入口始终走该严格解析器。文档不得把基类上转型赋值也描述为具有相同的原文保留能力。

## 默认值、单位与暂未实现选项

Host/User/Password 无默认值，认证不完整在任何 LOGIN 前拒绝；缺少 host 也不得偷偷连接 localhost。标准 `DmConnection.ConnectionTimeout` 返回秒，旧 Builder `ConnectionTimeout` 及 connect/pool/socket 字符串键维持毫秒。新 typed TimeSpan 属性按文档精确换算，负值/超出旧层可表示范围/无法保持单位的值拒绝。命令默认 30 秒，显式 0 为无限；无连接的新命令也使用 30。

默认 TransportSecurity=RequireTls，当前已提供完整 TLS。共享明文测试显式选择 PlaintextAllowed，不关闭证书验证或静默降级；安全边界见 [TLS 说明](../compatibility/T07-security.md)。容量调度与流式 LOB 已实现，物理会话复用仍未开放，见 [R3](../compatibility/R3.md)。固定 frame/物化 LOB/chunk 限额与旧选项拒绝规则见配置说明。


## 会话接入与输出

只有 Closed 状态可以替换设置，包括 Schema 等旁路属性。Open 原子选定 snapshot 并标为 Connecting，避免另一个调用在联网期间改配置；失败释放资源并返回明确状态。完整执行租约和 Reader 所有权属于 T05。

成功打开后，PersistSecurityInfo=false 时 ConnectionString 和旧 Password getter 隐去秘密，关闭后仍保持该规则；显式重新赋值按新配置重新计算。重开和 Clone 使用内部 snapshot，不从脱敏串重建认证。Builder 的明确配置读取可取得密码，`ToRedactedString` 和内部设置的字符串表示必须安全；禁止把原始配置用于日志。

停止加载环境服务文件、连接级配置写进程全局字段、对含口令连接串做稳定 hash，以及共享 `filter.next` 链。保留兼容类型时不允许把非空 filterHead 注入当前 ADO 路径；旧日志不再作为活动输出路径。握手与错误处理需保留结构化代码但不输出凭据。

## 验证

离线覆盖解析入口、别名等价与冲突、quoted values、IPv6、Unicode、单位边界、Clear/Remove/失败恢复、安全默认值、能力守卫与脱敏。真实库通过 TEST wrapper 显式选择对应安全模式，验证 CRUD/事务、配置状态、Clone/重开及精确对象清理。分工遵循当前 [AGENTS.md](../../AGENTS.md)。
