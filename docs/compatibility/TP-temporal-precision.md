# 时间类型与精度

本文说明当前 [0.3.1](../release-0.3.1.md) 的时间参数、读取和 EF 迁移行为。这里的“已验证环境”指特定的达梦版本、协议和编码组合；其他环境不会自动获得相同能力。

## 参数精度与无损写入

CLR 时间值统一使用 100ns tick 精度，数值路径不经过浮点或字符串中转，也不会隐式舍入、截断或饱和。

如果目标列、协议类型或服务器环境无法精确表示该值：

- 能够确定无法精确表示时，在业务 Execute 发送前抛 `OverflowException`。
- 当前环境未开放该能力时，抛 `NotSupportedException`。

**时间参数只使用 `Scale` 表示小数秒位数。** 非零 `Precision` 会抛 `NotSupportedException`；从官方驱动迁移时，应将 `Precision=7` 改为 `Scale=7`。DECIMAL 的 `Precision` / `Scale` 不受影响。

## 默认类型与小数位选择

没有可靠的固定列描述时，类型和小数位由当前服务器环境决定，不会随值末尾是否有零而变化：

| 参数声明 | 已验证七位环境 | 其他环境 |
| --- | --- | --- |
| 推断 CLR `DateTime` / `DateTimeOffset` | TIMESTAMP 26/7、TZ 27/7 | 16/6、23/6；非整微秒值拒绝 |
| 显式 `DbType.DateTime` / `DateTimeOffset` | 保持兼容默认 16/6、23/6 | 同左 |
| 显式 `DbType.DateTime2` | 26/7 | `NotSupportedException` |
| 显式 `Scale` 0…6 / 7 / 8…9 | 兼容同精度 / 扩展七位 / 扩展八九位（CLR 值） | 0…6 使用兼容路径；7…9 拒绝 |
| 时间 NULL 的 `Scale` 超过环境上限 | 扩展声明最高 9 | 声明降为兼容上限 6 |

可靠的固定列描述始终优先。例如，显式 `DateTime2` 写入六位列时仍受该列六位精度约束。

时间 NULL 不携带小数位：在已验证七位环境中，有可靠固定列描述时可沿用服务器声明，包括 8/9 位列；其他环境仍执行原声明检查。没有固定列描述时按上表处理。

已验证环境支持非 NULL `DateTime` / `DateTimeOffset` 以 `Scale=8/9` 写入 TIMESTAMP / TIMESTAMP WITH TIME ZONE。CLR 值仍只有七位小数精度，例如 `.1234567` 会无损写为 `.123456700`。默认推断声明仍为七位。

固定列精度与参数 `Scale` 分别检查：写入六位列时，不会因为参数声明为九位而截断数据。

参数绑定在成功描述后、业务 Execute 前失败时，只有确认绑定期间没有网络往返、会话和调用归属一致、没有取消或超时，并成功清理句柄，才允许保留会话。其他情况会使会话进入 `Broken`。

## 读取行为

- `GetString` 根据原始纳秒字段返回精确文本。
- 8/9 位列中不能整除 100ns 的值，CLR getter 会抛 `OverflowException`。
- Schema 的 `NumericScale` 返回真实小数位数。
- 带时区列的 `GetValue` 返回 `DateTimeOffset`；官方驱动对应路径返回 `DateTime`。
- 公开旧辅助类型（`DmDateTime`、`DmTime`、`DmIntervalDT` 等）会拒绝无法精确表示的超精度输入，方法签名不变。

## 已验证环境

| 服务器环境 | 七位写入 | 说明 |
| --- | --- | --- |
| DM 8.1.5.60 / message 21 / UTF-8，显式 `PlaintextAllowed` | 开放七位值及 `Scale` 8/9 声明 | 高精度写入和 EF 迁移已验证 |
| DM 8.1.4.6 / message 11 / GB18030，隔离 TLS | 关闭；六位已验证 | 使用兼容路径 |
| 其他版本或编码 | 关闭；按兼容六位路径处理 | 尚未验证 |

当前仍不开放：TIME/INTERVAL 七位写入、文本和旧辅助类型的八九位写入、非 NULL 的 TIMESTAMP WITH LOCAL TIME ZONE 写入、1582-10-05…14 写入、BCE 写入，以及 TIME hour=24 投影。

## EF 迁移

- 新模型默认使用 `TIMESTAMP(7)` / `DATETIME(7) WITH TIME ZONE`。
- `HasPrecision(n)` 生成列 scale n，并将参数 `Scale` 设为 n，不写入 `DmParameter.Precision`。
- 反向脚手架保留数据库真实精度。
- 六位改为七位是独立的 Schema 变更，`ALTER` 会保持原值，但此前丢失的第七位无法恢复。
- 七位降为六位的 Down 迁移必须先检查非整微秒数据。下面的 guard 应与 `ALTER` 分开执行，并使用 `suppressTransaction: true`；同一迁移中所有待降列都先检查，再执行 `ALTER`。

```sql
DECLARE v_bad BIGINT;
BEGIN
    SELECT COUNT(*) INTO v_bad FROM "T"
     WHERE MOD(TO_NUMBER(TO_CHAR("V", 'FF9')), 1000) <> 0;
    IF v_bad > 0 THEN
        RAISE_APPLICATION_ERROR(-20071, 'Seven-digit fractions would be lost by narrowing to six digits.');
    END IF;
END;
```

服务器错误后的会话保留取决于服务器环境、严格回执、错误号白名单和执行上下文，详见[错误恢复说明](server-error-recovery.md)。无法确认会话可用时应使用新连接；迁移 guard 拒绝后先检查事务和连接状态，不要自动重试或继续清理。

## 回退到旧驱动或旧 EF 组合

回退前先确认没有七位新数据。如果存在非整微秒数据，应保留七位 Schema，不要自动截断。
