# TP07 L0/common 第一轮冻结源码验收

2026-10-09。结论：**七个 C# / project 输入静态接受，可进入与独立 Python runner/validator 的组合合同审查；尚未接受完整工具、编译或任何 DB 结果。** 未发现本次冻结源中的新增必现编译/API/生命周期阻塞。前次四项冻结前反馈已闭环。本次只读源码/固定依赖与哈希，未运行 SDK/tests/DB，未改实现。

## 身份与范围

模板根 `tools/DownstreamAcceptance/TP07/runtime-templates/`；aggregate按 path 排序后 `sha256 + "  " + relativepath + LF` 实算为 **`e83129a1572991a81cb11a3d6a6e7d6f34453ab2e5805b53978de91d0ec69fe1`**。Low `.local/temporal-native/verification/TP07-L0-v1/source-manifest.json` 实读 SHA256 **`66f915aa1a9bd5cb11eb191f5a7f0fd613f9983f639b77650b9dbd30b3794439`**，七项逐一匹配。

| 相对文件 | SHA256 |
| --- | --- |
| TP07LiteralProbe/LiteralCases.cs | 7259b460442d370e6b7a9f95fe313dbd96604f70058c82ee004f948a47ab7b52 |
| TP07LiteralProbe/LiteralOracle.cs | cb1696d1b305471e12c1a953630ee66454c4ab92d505d650e291e7170c44f492 |
| TP07LiteralProbe/Program.cs | 7fb1906a45a346ec7cac5a9a38e558a83fa270c34806dd391aca943bde99c3cd |
| TP07LiteralProbe/TP07LiteralProbe.csproj | f0bdfdd8698fdcf17677c878a7d33947c0dfa0c976fde4d01425e51746489f68 |
| common/CandidateGuard.cs | b5ffd7f0ff7def12b6f7dc0f22324d981eb1f0a20ee6536ddc9ac7bb96e6f493 |
| common/OwnedObjects.cs | 80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946 |
| common/SafeEvidence.cs | 44c5e9b5643a3d33ba348ed8acc51780ccad2f94a5b43cc819f5897c9f9c11ef |

不将仍WIP的Python、E/D调用方或生成源码计入本次接受。M01/M02 mapping-v4由root另行接受；本工具保持同TP06包 `0.1.0-temporal.20261008144915` / nupkg `da8349a2…` / DLL `843710be…` / MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`，无W ProjectReference。

## 前次问题闭环

| 原问题 | 冻结源修正与判断 |
| --- | --- |
| case ID大小写不匹配 | `OwnedObjects.cs:34` 接受ASCII大小写case ID，SQL对象名仍由固定prefix+nonce生成；12/2列表不变 |
| FreshOutput拒绝后覆盖旧report | `Program.cs:17–21,69` 仅成功取得新目录后置outputAcquired并允许报告写入；已有目录失败不能写原report |
| 未知CREATE结果且无marker仍清理 | `OwnedObjects.cs:108–113` nonLiteral无COMMENT时只允许已确认CREATE的create_acknowledged分支；未知create_inflight无marker不DROP。Literal自身CREATE原子写fullnonce默认标记；确认CREATE后COMMENT失败保ACK收据，精确结构核对后清理 |
| 工具/EF identity仅日志未绑定 | `CandidateGuard.cs:49–68,75–81` 无参entry与显式startup入口均强制build manifest SHA，核实际source/locks/assets、startup DLL/MVID/deps与实际EF provider DLL/MVID；未过即在DB前失败。CLI host选startup不再成为是否校验的开关 |

## 核心代码检查

- `LiteralCases.cs:11–18` 是shared12/TLS2，DTO普通日offset330，DT/DTO branch无条件表达式隐式转DTO问题；TLS不借literal开放7。真实EF relational factory经 `UseDameng(string)` 创建W连接，再用真实 `IRelationalTypeMappingSource.FindMapping(IProperty)` 调 `GenerateSqlLiteral`；无拦截器修正参数。
- `Program.cs:38–60` 校默认7/显式6 facet，bare保持null Precision；bare tick1只接受本地Overflow，后续没有literal INSERT且fresh ID1 rows0。默认7的生成、持久化或oracle失败均退出，不替换输入/SQL。参数化控制明确标ADO，不当EF SaveChanges证据；首个literal失败会fail-fast，未运行控制不能冒称完成。
- `LiteralOracle.cs:24–61` 获取真实 `TemporalServerMetadata.Source=ResultColumn`、raw type/width/scale/masks及schema scale；raw cache bytes先于typed getter取得。合法ordinary类型/长度后才输出hex，独立整数date/time/ns解码与严格完整FF9及typed localticks/offset比较，不调用driver codec作expected。未知FF9仅bounded shape/hash，原异常外传，partial attempt未标complete。
- catalog中TZ普通scale直接解释，只有TLTZ按4096编码处理，interval leading/fraction分别检查；不拿catalog DATA_SCALE当wire packed。有限D alter恢复只认可已登记before/target6或7并保marker，不增加第三种猜测状态。E/D真实调用和其完整迁移保护仍需独立审查。
- `CandidateGuard` 先固定包/source archive、deps和实际loaded W，再核实际连接user/schema/physical版本/message/encoding/override；shared显式PlaintextAllowed，TLS保RequireTls/本地CA策略。日志无connection string、认证或任意Exception.Message。`SafeEvidence`只输出自有枚举code和外部type/number。
- `OwnedObjects` 在CREATE前持久ledger与fresh absence；finally清理再fresh核absence。清理失败另留cleanup_error并令退出失败，不把业务失败抹成成功；缺marker/未知CREATE保失败待处理，不扩大删除权限。实际COMMENT/CREATE错误场景仍待离线合同与运行验证。
- `.csproj`仅隔离EF provider ProjectReference和exact W/EF10.0.12包；API类型/公开方法已对照本地固定源码。没有把静态检查称作C#编译通过。

## 组合审查仍须闭合的既定合同

以下是后续Python四文件的必要接线，并非本次已接受运行结论，也不是新能力范围：

1. **literal必须独立精确校验。** `Program.cs:79–81` 的 `LiteralFractionMatches`只是fraction前缀Contains，`generated_literal_match=true`不能成为最终oracle。独立validator必须按固定case整数/offset比较完整literal/精确位数：六位`.123456`不能由`.1234567`冒充，即使服务器截尾后持久值恰好相等。当前受验mapping的输出正确不允许validator信任布尔或producer expected。无需更改产品。
2. **build manifest信任根由独立runner建立。** C#校实际文件与提供的SHA，但bridge/mapping/source/模板等lineage摘要仍须runner对照root固定清单、重放与完整文件集生成并封存；不能由任意自行配对的manifest+CLI SHA宣称root批准。核生成/restore/build前后和实际assets中无driver ProjectReference/官方fallback。
3. **不只看汇总旗。** 独立核固定12/2 case ID、无缺项/重复、raw descriptor与schema scale、catalog声明、raw payload hash/整数、typed/FF9、参数控制、拒绝rows0、每项完整状态、实际process退出码及fresh cleanup。`literal_runtime_accepted`和bare `observed_unverified_bare_declaration`不是全temporal/C门支持声明。
4. Python冻结后合并本七源、最终EF overlay、locks/assets与工具case manifest，再由Astra审查、Low独占离线与fresh L0。缺少任一组合输入时保持combined_tool_pending；default7真实literal损失仍为硬失败，不能沿此静态接受继续后续昂贵DB门。

本轮无须为未开放8/9写、TLTZ值、BDTA或R3扩展增加实现。没有发布/EF-C完成声明。
