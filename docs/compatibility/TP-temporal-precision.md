# .NET 原生时间精度兼容说明

当前版本为 `0.2.0`；本页历史阶段证据仍归原候选，当前发布状态与验证范围见[发布记录](../release-0.2.0.md)。

本页记录当前时间精度行为和已表征 profile；历史验证结果仅归属原受测版本。EF 适配和迁移属于独立下游项目。

## 行为变化

- CLR 时间值以 100ns tick 为统一精度，数值路径不经浮点或字符串中转，不做隐式舍入、截断或饱和。目标列、wire 类型和服务器 profile 无法精确表示的值在业务 Execute 发送前抛 `OverflowException`；能力未开放抛 `NotSupportedException`。
- 时间参数只用 `Scale` 表示小数秒位数（0…9 先校验，不做位掩码）。时间参数的非零 `Precision` 改为 `NotSupportedException`；官方驱动惯用的 `Precision=7` 应改为 `Scale=7`。decimal 的 Precision/Scale 不受影响。
- 无 fixed describe 时的默认选择按协商 profile 稳定决定，不随值尾数漂移：

| 声明 | 已验证七位 profile | 其他 profile |
| --- | --- | --- |
| 推断 CLR DateTime / DateTimeOffset | TIMESTAMP 26/7、TZ 27/7 | 16/6、23/6，非整微秒拒绝 |
| 显式 DbType.DateTime / DateTimeOffset | 保持兼容默认 16/6、23/6 | 同左 |
| 显式 DbType.DateTime2 | 26/7 | NotSupportedException |
| 显式 Scale 0…6 / 7 / 8…9 | legacy 同精度 / extended 七位 / extended 八九位（CLR 值） | 0…6 legacy；7…9 拒绝 |
| typed NULL，Scale 超过 profile 上限 | extended 声明最高 9 | 声明降为 legacy 6 |

- 可靠 fixed describe 始终优先：显式 DateTime2 写入六位列时仍受该列六位约束。
- typed NULL 不携带小数位：在已验证七位 profile 下，有可靠 fixed describe 时可沿用服务器声明（含 8/9 位列）；其他 profile 仍执行原声明校验，无 fixed describe 时按上表降级声明（未随 0.1.0-preview.1 发布）。
- 已验证 extended profile 支持非 NULL `DateTime` / `DateTimeOffset` 以 `Scale=8/9` 写入 TIMESTAMP / TIMESTAMP WITH TIME ZONE。CLR 值仍只有七位小数精度：例如 `.1234567` 无损写为 `.123456700`；默认推断声明仍为七位。文本与旧 helper 不新增八九位写能力。fixed 列精度与参数 Scale 分别检查，写入六位列不会因参数声明为九位而截断数据。
- 参数绑定在成功描述后、本次业务 Execute 之前失败时，只有确认绑定期间没有 wire 往返、会话及调用所有权一致、且无取消/超时，才允许保留会话并关闭本次句柄。清理失败或协议状态不确定仍使会话 Broken。
- Reader：`GetString` 从原始纳秒字段返回精确文本；8/9 位列中不能整除 100ns 的值，CLR getter 抛 `OverflowException`。schema `NumericScale` 报真实小数位数。TZ 列 `GetValue` 返回 `DateTimeOffset`（官方驱动为 `DateTime`）。
- 公开旧 helper（`DmDateTime`、`DmTime`、`DmIntervalDT` 等）先前舍入的超精度输入现在拒绝；签名不变。
- 保守关闭：TIME/INTERVAL 七位写、文本/旧 helper 的八九位写、非 NULL 的 TIMESTAMP WITH LOCAL TIME ZONE 写、1582-10-05…14 写、BCE 写、TIME hour=24 投影。

## 历史验证与产品 profile 限制

| profile | 七位写 | 证据 |
| --- | --- | --- |
| DM 8.1.5.60 / message 21 / UTF-8（shared，显式 PlaintextAllowed） | 七位值及 Scale 8/9 声明开放 | TP06 A/B 同包；2026-10-12 EF 七位迁移；2026-10-15 [CLR 高精度写入 36 项](I16-ef-integration-feedback.md#2026-10-15-后续修订) |
| DM 8.1.4.6 / message 11 / GB18030（隔离 TLS） | 关闭，六位已验证 | TP06 |
| 其他版本/编码 | 关闭，按 legacy 六位兼容路径 | 未表征 |

## EF 迁移

- 新模型默认 `TIMESTAMP(7)` / `DATETIME(7) WITH TIME ZONE`；`HasPrecision(n)` 生成列 scale n，并把参数 Scale 设为 n，不写 DmParameter.Precision。scaffold 保留数据库真实精度。
- 六位→七位是独立 schema 变更，`ALTER` 保持原值；此前丢失的第七位无法恢复。
- 七位→六位的 Down 必须先检查非整微秒数据。参考 guard（与 ALTER 分开，`suppressTransaction: true`，同一 migration 内所有待降列先检查后再 ALTER）：

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

- 服务器错误后的会话保留受 profile、严格回执、错误号白名单和执行上下文限制（见 [错误恢复说明](server-error-recovery.md)）。无法确认会话可用时需新连接；迁移 guard 拒绝后先检查事务和连接状态，不自动重试或继续清理。

## 回退

回退到前一驱动/EF 组合前，先确认没有七位新数据；有非整微秒数据时保留七位 schema，不自动截断。
