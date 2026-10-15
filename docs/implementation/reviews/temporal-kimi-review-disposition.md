# 时间精度设计审阅决策

历史设计日期：2026-10-07。保留有效决策及证据校正，供原设计输入引用。文件名与 KIMI 编号是历史标识，不指定当前模型或工具；当前行为见[时间精度兼容说明](../../compatibility/TP-temporal-precision.md)。

## 逐项处置

| 意见 | 处置 | v2 修改位置和决策 | 后续验收 |
| --- | --- | --- | --- |
| KIMI-01 Precision 契约与用户 Scale 掩码 | 采纳问题；root 明确产品取向 | §4.2、§5：仅 Scale 控制小数位；时间参数 Precision 未设/0 为无请求，非零拒绝，禁止作为 wire width 或别名；用户 Scale 先检查 0…9，19/23/255 不进入 packed metadata 解码；decimal 不受此限制 | TP-A05；TP-07 检查 EF HasPrecision 到参数 Scale 的转换；旧 helper prec 分开冻结 |
| KIMI-02 TLTZ 遗漏 | 采纳 | §3、§4.2–4.3：保留已验证的 SQL 会话时区语义；保留 mask/0x1000 原始字段；profile 未证的格式读写双侧拒绝，不混为普通 TIMESTAMP；明确防止重复转换 | TP-01、TP-A13：16/候选26、异会话时区、跨日极值、metadata/typed/GetString |
| KIMI-03 八九位 getter 分层 | 采纳主要意见，保留现有 API 类型 | §4.1、§4.5：GetString 从原始纳秒精确格式化；CLR GetValue/typed/generic/provider-specific 对非整 tick 抛；schema 报真实精度；不把 provider-specific getter 新改成 string，也不新增 GetFieldValue<string> 的时间转换 | TP-A02/A06：8 位合法尾数 10/90 与 9 位 1/99 精确文本；100ns 精确 CLR；非法列 scale 尾数另测 |
| KIMI-04 默认 wire 选择歧义 | 采纳 | §5 新稳定选择表：推断 CLR 按 profile 稳定选择；显式 DateTime/DateTimeOffset 无 Scale 保留 legacy；DateTime2 默认请求七位；显式 Scale 请求按能力判定；fixed describe 始终约束目标；区分取自冻结原始 DbType | TP-A05：来源×fixed/advisory×profile×Scale；仅替换值不漂移 wire；未知 profile 不静默降级显式七位 |
| KIMI-05 offset/UTC 边界责任 | 部分采纳并校正示例 | §4.3：区分 CLR 构造器前置保证、wire→CLR 双范围、profile 已知服务端约束和未知服务器 DmException；不猜测服务端 UTC domain；记录当前代码 offset [-779,840]，不是通用数据库断言 | TP-01/TP-A03：可构造的输入边界、原始 wire 非法组合、profile 已知预检；构造器负例单列 |
| KIMI-06 TIME 24:00 与高精度存在性 | 采纳 | §4.2、§4.5：合法 wire hour24 到 CLR 为 OverflowException，不取模/进位；TIME>6 的服务器存在性先表征，未知格式不开放；精确文本遵守合法服务端值契约 | TP-01/TP-A04；合成 decoder 测试不冒充服务器支持24:00 |
| KIMI-07 getter 矩阵与 TIME 模式 | 采纳并校正 API 层级 | §4.5 新逐格目标矩阵；保持 TZ 上 GetDateTime 的既有墙钟投影、generic DateTime 对 TZ 拒绝；DbTimeToTimeSpan 两种可达模式保留并改为整数构造；GetDate/GetTime 是内部 helper，不新增公开 Reader API | TP-00 调用点清单；TP-A06 全矩阵；TP-05 的 DateUtil 由 reader owner 负责，共享 DmDateTime 回交 codec owner |
| KIMI-08 重复计划易漂移 | 采纳建议 | 无版本文件改为入口指针，指向唯一当前 v2；v1 作为历史审阅输入由 Git 保留；当前行为入口指向兼容说明 | 链接/哈希检查；历史版本与实际审阅范围分别记录 |

上述“采纳/处理”只表示设计意见已进入文档；当时对应代码和测试尚未执行；后续实现状态见当前兼容说明。对 KIMI-01 选择拒绝非零时间 Precision，是 root 为避免把公开属性与私有线字节数混用作出的明确设计决策，并非声称 .NET 标准强制这样实现。实施必须附迁移指引和兼容性说明。

## 两项证据校正

1. **KIMI-05 的具体示例不能成为驱动输入。** `new DateTimeOffset(0001-01-01 00:30, +14:00)` 在构造时就因 UTC 超出 .NET 范围失败。不能将其描述为“驱动未预检后由服务器拒绝”的可执行案例。v2 保留其边界研究意义，改用合法 CLR 输入或人工原始 wire 字段，分别测试构造器、驱动 decoder 和真实服务器，不混淆失败责任。
2. **“W 公开七位写链路至今未经端到端验证”不能由旧 round2 README 单独推出。** 该 README 明确只说当轮 O 写/W 读不等于后续新候选公共写入验收；T12 最终报告（历史记录已归档）随后记录 fixed 时间/七位 gate 修复后，新候选 EF functional 76/76，并有 最终证据（历史记录已归档）。此次未逐一重放或读取所有 ignored 个案制品，故既不把旧 round2 限制泛化成“至今未验证”，也不将历史普通用例升级为当前候选、最大日期或全部新规则已通过。TP-01/06 对本专项极值及当前候选另行验收。原始 Kimi 意见保持原样，本处记录主审阅者的证据范围纠正。

原始审阅请求、CLI 执行流水及首版计划见 Git 历史；不再保留它们作为现行实施入口。
