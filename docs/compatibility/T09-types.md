# 数据类型与转换规则

本文说明当前类型行为。时间精度和服务器环境限制见[时间精度说明](TP-temporal-precision.md)，流式 LOB 见[数据源与 LOB](R3.md)。

## 参数编码

- CLR 枚举按真实底层值编码；无符号值使用足够宽的协议类型，同时检查显式声明的逻辑范围。不会回绕数值，也不会按枚举名称的序号编码。
- 显式类型不会被建议性的服务器元数据忽略。无类型 NULL 没有可靠描述时，需要设置 `DbType` / `DmSqlType`；`ResetDbType` 清除显式类型来源。
- 通用 `DbType.Binary` / `byte[]` 使用可变长类型；显式驱动 Binary 类型保留固定长度语义和容量检查。
- 不会截断超长值、隐式混写文本与二进制，或把空值转换成 NULL。

## 读取与 CLR 类型

- `W.Dm.DmDecimal` 提供精确的系数和小数位。超出 CLR `decimal` 范围的 DECIMAL 应使用驱动特定 getter；普通 getter 会明确溢出。
- 普通 36 字符列仍返回 `string`。`GetGuid` 是显式转换：字符形式采用 D 格式，二进制采用 CLR 的 16 字节约定。
- `DateOnly` / `TimeOnly` 使用明确的 DATE/TIME 编码；无时区 `DateTime` 返回 `Unspecified`。
- CLR 时间保持 100ns tick。已验证环境可写七位时间戳，其他路径受相应六位限制；无法精确表示的小数或范围会拒绝。INTERVAL 保留正负号和 EF 可解析的限定字面量。
- 泛型整数读取遇到小数或越界会失败；SQL NULL 不会转换成空字符串。同步和异步 getter 使用相同转换规则。

## 未开放类型与入口

未开放的原生 XDEC 输入、复杂类型、命名或可更新 cursor 等旧入口仍明确拒绝；存储过程纯输出/返回值的 XDEC 例外见[存储过程说明](I09-stored-procedures.md)。源码中存在某个类型或分支，不代表该功能已经支持。
