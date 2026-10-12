# TP04 binding 与 codec bridge 静态验收 v1

2026-10-08。结论：**changes_requested / static_not_accepted**。发现两项新测试阻塞；没有以静态推断宣称已经运行失败或通过。TP04 运行门、TP05、最终同包矩阵与发布门均未关闭。

本次依据 AGENTS、TP04-astra-design、TP04-design-notes、TP04-TP05-v6-implementation-brief、TP03-acceptance 及 physical-v11 交接，对 root 提交的 12 文件冻结进行只读审阅。没有修改产品或测试，没有运行 dotnet、build、tests、DB，没有读取 secrets；TP05 动态文件不在正式审阅范围。仅新增本报告。

## 阻塞发现

### TP04-R1：advisory extended 文本正例与既有能力守卫矛盾

位置：`tests/W.DmProvider.TypeTests/TemporalNativeBindingContractTests.cs:81`、`:91–93`；对应产品守卫 `src/W.DmProvider/Internal/Legacy/Dm/DmSetValue.cs:1547–1549`。

该 theory 的四个 offset case 均先通过完整合成 Prepare 帧获得 raw `CType2 / flag2 / scale0`。首个 CLR DateTimeOffset 绑定合法选出 27/7，但不会改变 raw 的 advisory 来源。第二次绑定仍使用该 describe，把 string 显式声明为 DateTimeOffset、Scale7；resolver 生成 `CType27 / ReliableFixed=false`。实际 writer 在解析文本前必抛 `NotSupportedException("Extended temporal text requires reliable fixed metadata.")`，因此四个 case 均无法到达其后 golden 断言。这是可由完整控制流确定的测试矛盾，不是此次实测结果。

此守卫在原 `SetString` 路径亦存在（同文件 `:805–815`），新入口保留它符合不新增可选能力的范围。

最小修复：保留 advisory CLR 数值正例；文本正例前用完整 Prepare 帧重新 describe 为真实 decoder 捕获的 fixed `27 / width11 / scale7 / flag1`，再核对实际 Execute 帧 header 与独立 golden。另明确断言原 advisory extended 文本仍被拒绝且没有发布 binding。不要删除产品守卫来迎合测试。

复验：四种 offset 的数值及 fixed 文本均执行到独立 golden 断言；advisory 文本负例保留原异常，字段 binding 和 plan binding 不发布。Low 在新冻结后运行这四个 theory case 及对应 binding/codec 合同。

### TP04-R2：FrozenCodec fixture 手工标注 ParameterDescribe，绕过服务端来源链

位置：`tests/W.DmProvider.TypeTests/TemporalNativeFrozenCodecTests.cs:168–175`，特别是 `:171`。

`Fixed` 从手工设置的 field 直接调用 `CaptureTemporalServerMetadata(new(Source.ParameterDescribe, ...))`，随后 resolver 根据这个标签认定 `ReliableFixed=true`。这使测试数据在没有经过 metadata decoder 的情况下获得 `HasServerSource=true` 和可靠 fixed 权限。按 TP04 §2–4 的明确合同，客户端合成 field 不能这样制造服务端来源；这些测试因而不能作为原始来源到冻结 binding 的独立证据。

现有 BindingFixture 已有完整 Prepare 帧和 sync/async frame reader 后进入共享生产 decoder 的测试设施，可用于最小修复：让 FrozenCodec fixture 同样通过完整帧调用真实 decoder，取得其生成的 field 和 raw。测试仍须明确属于合成报文离线合同，不称实库观察。纯 read descriptor 的独立值测试也可使用明确 Synthetic 的来源，但不能据其开启可靠 fixed 写门。不要修改产品 resolver 接受 Synthetic 为 reliable 的规则。

复验：所有 FrozenCodec fixed 控制均先经 decoder 建立来源，再执行其既有 mutable field / public request / physical profile 变更反例；保留未知 profile 拒绝、文本 ns 精度拒绝、interval 双 quantum、Unknown format 与 ordinary12 拒绝断言。检查新增测试不再直接赋 ParameterDescribe 来通过来源门。Low 在新冻结后运行完整 FrozenCodec 合同。

## 已核对的实现边界

- 原 DbType 与 provider 两种显式声明分别冻结；DateTime2 与兼容 provider DateTime 的默认七位意图不依 setter 顺序。Clone/plan 继续复制用户状态，不把服务器 raw 或 profile 写回 public 参数。Scale 范围校验在非零 Precision 拒绝之前，显式 Precision0 保留状态但不覆盖 wire width。
- 所有 ParameterDescribe 在 numeric129 等归一化前捕获 raw；只有 temporal ResultColumn 增加 snapshot。实际格式独立保留 Unknown，结果来源不伪称已观察 ordinary row。16/26 装饰位严格解释；TLTZ 可被描述但写和值读仍拒绝。唯一 `CType21 / flag2 / rawscale0 / mask0` 占位例外不被解释成合法 packed read descriptor，绑定采用 DAY(9) TO SECOND 的明确请求或默认六位。
- reliable fixed 依赖 ParameterDescribe、原 flag1 与时间 CType；advisory raw 与客户端 effective 字段分开。默认选择依声明与 profile，不读取值的小数尾数。可靠列精度、用户请求精度、编码精度分别保留；interval 的完整 packed 声明不被当作普通 Scale。
- `BindParameters` 在既有 statement/session ownership 检查后捕获一次实际 physical profile；exact identity 包括必填 encoding override empty 条件。实际时间 writer 使用传入 immutable binding 的 Profile/Target；数值、文本与 NULL 均经过同一目标门。新文本 parser 从连接只取得原有 format/language；未增加 NLS 能力或重捕 profile/target。非时间 writer 分支、旧 helper 入口及 provider-object plan snapshot 守卫保留。
- 每个服务器 occurrence 独立创建 binding；循环前清除旧 field binding，全部参数编码成功后才向 plan/field 发布。非 NULL 编码失败不会进入业务 Execute 调用。固定列字段的 scale 未为展示 EncodedScale 而覆盖，interval payload 和 header 消费完整 packed scale。
- read descriptor 是值记录，bridge 依据其 ActualFormat；Unknown、ordinary12 和 TLTZ 仍拒绝。合法八九位 metadata/read schema 不应用写能力门。TP05 的 getter/schema 实现和恢复行为仍由其独立审阅与运行门负责。
- BindingContract 的 Prepare/Execute 帧测试经过共享 decoder 和实际 packet writer，expected golden 是字面量而非调用被测 encoder 生成；反射异常 helper 用 ExceptionDispatchInfo 重抛原异常，没有吞异常。FrozenCodec 直接来源赋值的例外已列为 R2，不能被这些正面结论覆盖。

以上为静态观察，不代替 Low 的完整 Type/Command/Async/Protocol 回归或同包实库验证。close/open 与取消等既有 ownership 机制未改，本次不以 fixture 手工设置 Session 的事实宣称已运行完整连接生命周期。

## 冻结身份

以下 SHA256 已全部实读，均与 root 交接一致；路径相对仓库根目录。

| 文件 | SHA256 |
| --- | --- |
| src/W.DmProvider/PublicApi/DmParameter.cs | 5ef3f35f031410697cd573b7837da3c2762f3fcd206b08aee0b057c79a9894df |
| src/W.DmProvider/PublicApi/DmCommand.cs | 34b9983ff8bfc941a2401678f7ed05e845ef2997f004e1dc29aa57b74f90070b |
| src/W.DmProvider/Internal/Types/DmParameterBinding.cs | 14080eb660f705e0487ad7d535a09949a44154e6b630bc804712b9f61a59996e |
| src/W.DmProvider/Internal/Execution/DmCommandPlan.cs | 35f2bd61c91713176b9e6a6fe1ec566882778462a5797f2e4059359f623f4d78 |
| src/W.DmProvider/Internal/Legacy/Dm/DmField.cs | e2dd0766eb6f533da2b3fd755f383223a1223b01484b01c864706ddc77d804b8 |
| src/W.DmProvider/Internal/Legacy/A/T02_02000094.cs | 3a57ffc343bc455561ef0fdc4d19b44f98a3425e7408e681f80f586e51950df3 |
| tests/W.DmProvider.TypeTests/TemporalNativeBindingContractTests.cs | f6d4d1562791435af9cedb305831ca276366f5dde23b510244c2ad55f3e8f32f |
| tests/W.DmProvider.TypeTests/TemporalNativeMetadataContractTests.cs | c6f1db0a8111bbdead50234af3fdf2cb8e1f2dac6f53e544b45cbdac23992bd5 |
| src/W.DmProvider/Internal/Legacy/Dm/DmSetValue.cs | 965e958e813a4c9198842a0096f65d3aee952aee564c51ef38b75afa9cdba5af |
| src/W.DmProvider/Internal/Legacy/Dm/DmDateTime.cs | 25b597f36dda18ab74c37f4186e71b093f44662c0eea7caf2827126a994b13fd |
| src/W.DmProvider/Internal/Types/DmTemporalLegacyAdapter.cs | 79a7a08988029e900411e07d3ae8515b8e22fc4909f36293cf033f3b1d27a0d1 |
| tests/W.DmProvider.TypeTests/TemporalNativeFrozenCodecTests.cs | 31c565ad44d1904ae0870df1d58b5555141f614d2e05c9cf7ac700da2639917e |

修复交回各测试归属 High；保留本次冻结与报告，修复后更新文件 SHA，再定点复审并由 Low 独立验证。本报告不授权改写旧 TP03/physical-v11 evidence，也不扩大产品能力。
