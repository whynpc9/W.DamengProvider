# TP07 mapping-v5：FF7 literal 窄修源码验收

2026-10-09。结论：**scoped_source_accepted_for_Low / M1_rerun_pending / L0_rerun_pending / full_EF_C_pending**。未发现本次冻结差异中的确定编译/API、格式或保护边界阻塞。Low 可按 root 排定窗口做独立源重放、构建和离线门；本报告不宣称已经编译或修复后的 literal 已实库通过。本轮仅静态读源与文件哈希，未运行 SDK/tests/DB、受验工具或修改实现。

## 激活与来源

已核 [条件设计](TP07-FF7-mapping-change-design.md) SHA256 `dab10f3276860ffca832b6099cfc4cc0c2d0d48b1eee5d8fa1d597e59e94a696`、[FF7 表达式前置接受](TP07-FF7-expression-acceptance.md) SHA256 `cf61a76832df41526b13e1e602acca9b65c81e607b4fb27dfcdaae00ba501425` 及所指独立 summary `e74a15d26c7c0d50862c180918d5e1d806d42d1672c1e2e271f084975d7eda58`。前置接受是52项 literal门及第53项普通DT DATEADD均完成，且 root 已明确开启本次窄窗；不是凭 SCREEN25 普通值直接改产品。

实读 `.local/temporal-native/TP07/mapping-v5/work/`，与 v4 work 比对均为175个输入，文件集合相同，恰好三个文件改变，其余172个逐字节相同。没有 namespace/type alias 批量替换，没有依赖、项目、锁、registry、parameter、reader 或 translator 变化。

| 固定输入 | 实算 SHA256 |
| --- | --- |
| mapping-v5 `mapping-manifest.json` | `23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c` |
| 公共及隔离副本 `temporal-mapping.patch` | `4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc` |
| mapping-v5 `source-manifest.json` | `5905463c61d8d6fa87c3932a1431b2c1d9b02bb4483bf786ad691b4e5b02737e` |
| 175源 aggregate（排序后 sha、双空格、相对路径、LF） | `7010d960c7d212241937a57ce587cce5e85ad803c94a4449705d2502c0c51a18` |
| `DamengTemporalParameterFacets.cs` | `d9ac6978bebe9fe758226a4ec1d505e89a17d9b998aa5208082cc77e34aa3a3a` |
| `TP07TemporalSqlTests.cs` | `8075233abc9d476e8e16a57902ca1682d51e7b5463b1fb2d4cf82c8bf7eaa752` |
| `DamengTypeMappingTests.cs` | `1395661ea656f5259bcd291307485d7b745ea8313dd6aa707717ec442538d5e8` |

manifest 的175项 after hashes 全部与实文件一致；P1→v5完整 patch 的12条文件路径与 allowlist 一致，区别于本次 v4→v5 的3文件。author 的 static-replay 收据是来源材料，本轮没有自行运行 patch/replay，也不将其当 Low 独立重放结果。旧 v4、历史 L0 丢位及 constructor 收据继续保存，不回写接受旗。

## 产品差异判定

唯一产品差异在 `DamengTemporalParameterFacets.TimestampLiteral:60–66`：fraction7 且 DTO 时先在 clock/offset 间补一个空格，再按 offset 是否存在选择 TO_TIMESTAMP_TZ / TO_TIMESTAMP；两个 format 字符串固定为 FF7，逗号后恰好一个空格。原 0…6 返回语句及其 offset 拼接字节保持。

- 分支只读最终 fraction，未读取值的尾零。整秒、整微秒、tail1…9、默认7、显式7和 clone 最终7均产生 FF7；没有旧 literal/FF9 fallback。
- `fraction > 7`、`LiteralDomain`、`ExactTicks` 的先后顺序与逻辑未移动。8/9非 NULL、历法缺口、offset范围拒绝继续在构造表达式前发生；NULL仍是基类路径。
- 日期与七位 fraction 仍由 InvariantCulture/整数 ticks 输出，DTO sign、小时及分钟仍来自原总分钟。DTO 调用方依然传 `offset.DateTime` 和 `offset.Offset`，没有 UTC/本机时区转换；`-00:30`、`-12:59`、`+14:00` 的格式未被小时符号推导破坏。
- DT/DTO mapping、TLTZ调用方拒绝、DATE/DateOnly、TIME/interval、bare fallback6、参数 Scale/Precision、W typed getter 与 DateTimeMethodTranslator 都是 v4 原字节。W候选仍为 `0.1.0-temporal.20261008144915`，包/DLL/MVID固定不变。

## 测试判定

`DamengTypeMappingTests.TemporalLiteralsPreserveTheSeventhFractionalSecondDigit` 只替换两条七位完整字符串期待，原 `.0000001` 和 DTO `+08:00` 不变。未批量替换 functional probe 中的手写 TIMESTAMP/CAST 或 DATEADD 诊断输入。

`TP07TemporalSqlTests` 的 max/max−1/max−9 和 ordinary7 正控制更新为 FF7；原 ordinary6、六位量化、DATE、历法、TLTZ/clones、offset拒绝与 interval 负例保留。8/9增加 NULL 保持断言，非 NULL拒绝未放宽。新有限断言通过真实 mapping service 覆盖：

- default7、explicit7、7→7及6→7 clone；tail0…9、整微秒以及 NULL，期望为独立完整常量字符串。
- min/min+tick、下一秒整值和 max/max−tick/max−9ticks；DTO极值offset0。
- 当前 Invariant/ar-SA 文化，DTO offset0/+330/−330/−30/−779/+840；断言固定本地时钟及原offset。
- fraction0…6逐档原格式、7→低精度clone、非对齐值拒绝、bare DT/TZ六位及NULL。DT/DTO新测试使用明确object赋值分支，不重引入条件式隐式DTO转换。

现有测试 API、InlineData 类型与 mapping/clone 调用方式静态一致；未发现显见编译阻塞。实际 xUnit discovery/analyzer、全部新旧断言通过及真实参数观察仍由 Low 运行证明，不预报旧51/102为新计数。

## L0 接口与后续门

只读核对当前 `TP07LiteralProbe/Program.cs:84–86`：DT/DTO两条完整FF7 grammar 的函数名、七位数字、DTO空格、逗号空格、固定format与新 helper 输出一致，没有发现格式衔接错误。此处只核接口文本，不将仍需独立审阅/冻结的 L0 工具纳入本产品接受，也没有执行 C# grammar门。

下一步保持既定顺序：Low固定175源/patch/manifest并独立重放构建 → M1新三个TP07类与受影响mapping/query回归、14项参数观察 → 新冻结L0工具合同及同overlay的shared/TLS fresh literal → E/D依赖门。真实 generated default/HasData/inline/query/materializer、cleanup及CLI/migration仍需各自证据。

表达式前置门的DATEADD已通过，只说明那次受控direct SQL；实际EF R04/ExecuteUpdate仍待验证。若后续DATEADD失败，保留为独立问题，不借literal修复更改translator、删case或覆盖旧证据。当前无完整EF-C、PR合并或发布接受声明。
