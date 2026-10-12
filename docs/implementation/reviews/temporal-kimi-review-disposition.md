# 时间精度计划：Kimi review 与一轮修订记录

日期：2026-10-07。状态：plan_review_completed_and_revised；实施未开始。本记录由主审阅者撰写，不是 Kimi 对修订版的复审结论。

## 输入与审阅执行

- 代码基线：`ed0c8a19d2a3d275eb469960ae56778e6280b5b0`。
- 审阅输入：[冻结 v1](../temporal-dotnet-precision-plan.v1.md)，SHA256 `06766581dddcb7bca41f427c005ea3a7da256fe1b930224cc04ae31bd86dce3e`。
- [完整审阅请求](temporal-kimi-review-prompt.txt)限定只读源码/文档，禁止编辑、构建、数据库、读取 secrets 和派生代理。
- 首先尝试 T3 `delegate_task`，provider=`cursor`、model=`kimi-k2.7-code`、clientRequestId=`temporal-native-kimi-review-r1-20261007`。返回 `provider_unavailable`：Cursor 未认证，未创建 taskId/child task。
- 随后使用本机已安装的 **Kimi Code CLI 2.1.1**，采用其默认配置运行；未将 CLI 默认模型冒称为 T3 的 `kimi-k2.7-code`。最初 `--plan -p` 因 CLI 不允许组合而退出 1，尚未开始审阅；去掉 `--plan` 后只读审阅完成，退出码 **0**。未自动安装工具或修改认证配置。
- 实际命令：`kimi -p "$(cat docs/implementation/reviews/temporal-kimi-review-prompt.txt)" --output-format text`；stdout/stderr 写入 ignored `.local/temporal-plan-review/`。保留 [原始 stdout](temporal-kimi-review-r1.txt)，SHA256 `5633c3ed8282e43293ec7606ce65c6cbadf32a41aa912f80b2cc13a65b86cc18`。原始文件含 CLI 进度和完整结论，未改写评审意见；stderr 为本地过程日志，不作为发布文档。

Kimi 对 v1 的结论是 **“修订后可进入实施”**，共 8 项：2 项高、2 项中、1 项中低、2 项低、1 项非阻塞建议。主审阅者核对了相关源码后生成 [v2 实施计划](../temporal-dotnet-precision-plan.v2.md)，只做一轮修订，没有安排第二次 Kimi review。

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
| KIMI-08 重复计划易漂移 | 采纳建议 | 无版本文件改为入口指针，指向唯一当前 v2；v1 永久作为审阅输入并保留哈希；文档入口指向当前版 | 链接/哈希检查；v1 不再编辑；v2 明示尚未被 Kimi 二次审阅 |

上述“采纳/处理”只表示设计意见已进入文档；所有对应代码和测试均未执行，验收项仍待实施。对 KIMI-01 选择拒绝非零时间 Precision，是 root 为避免把公开属性与私有线字节数混用作出的明确设计决策，并非声称 .NET 标准强制这样实现。实施必须附迁移指引和兼容性说明。

## 两项证据校正

1. **KIMI-05 的具体示例不能成为驱动输入。** `new DateTimeOffset(0001-01-01 00:30, +14:00)` 在构造时就因 UTC 超出 .NET 范围失败。不能将其描述为“驱动未预检后由服务器拒绝”的可执行案例。v2 保留其边界研究意义，改用合法 CLR 输入或人工原始 wire 字段，分别测试构造器、驱动 decoder 和真实服务器，不混淆失败责任。
2. **“W 公开七位写链路至今未经端到端验证”不能由旧 round2 README 单独推出。** 该 README 明确只说当轮 O 写/W 读不等于后续新候选公共写入验收；[T12 最终报告](../reports/T12.md)随后记录 fixed 时间/七位 gate 修复后，新候选 EF functional 76/76，并有 [最终证据](../evidence/T12-validation.json)。此次未逐一重放或读取所有 ignored 个案制品，故既不把旧 round2 限制泛化成“至今未验证”，也不将历史普通用例升级为当前候选、最大日期或全部新规则已通过。TP-01/06 对本专项极值及当前候选另行验收。原始 Kimi 意见保持原样，本处记录主审阅者的证据范围纠正。

## 一轮修订之外的收口补充

v2 明确了必需 profile 与独立扩展门的区别、TP-07 发现驱动缺陷后的新包/TP-06 回归回路，以及 EF 模型精度与 ADO.NET 参数 Precision 的不同语义。这些补充用于落实本轮意见，没有扩大为新产品实现。

最终文档检查：冻结 v1 与 Kimi stdout 哈希复核；v2、入口、处置记录的相对链接有效；任务编号 TP-00…08 和验收编号 TP-A01…13 齐全；Git whitespace 检查通过；变更仅限 docs。未运行 dotnet、测试或真实数据库；产品源码、历史 progress/evidence/ownership 未修改。设计交付完成不等于实施激活或发行接受。
