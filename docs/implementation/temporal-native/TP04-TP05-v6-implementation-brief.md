# TP04/TP05 v6 实施交接补充

2026-10-08。仅补充已冻结 [TP04 设计](TP04-astra-design.md) 与 [TP05 设计](TP05-astra-design.md) 的最新输入和接口；产品实施仍由 root 明确开窗。两份旧设计中的“v5 API / physical profile 待确认 / adapter 建议名称”是历史状态，以本 brief 的现状说明为准，既定行为裁决不变。

## 1. 接受输入与 profile 必填事实

产品基线为 [TP03 最终审阅](TP03-astra-final-acceptance.md) 的 v6 manifest/package；能力来源为 [physical-v11](TP01-profile-handoff.physical-v11.md)，不再取旧 `TP01-profile-handoff.final.md` 中 logical 默认编码标签。

当前实际签名（`W.Dm.Internal.Types`）为：

```csharp
internal readonly record struct DmTemporalProfileIdentity(
    string ServerVersion, int MessageVersion, string ServerEncoding,
    bool EncodingOverrideEmpty);
internal static DmTemporalProfileIdentity CaptureIdentity(
    string version, int messageVersion, string encoding, bool encodingOverrideEmpty);
// DmTemporalLegacyAdapter
internal static DmTemporalCapabilityProfile Profile(DmConnection connection);
internal readonly record struct DmTemporalWriteTarget(
    int CType, DmTemporalWireLayout Layout, DmTemporalResultFormat Format,
    int ColumnScale, int EncodedScale, int? RequestedScale, bool ReliableFixed, int Mask);
```

`EncodingOverrideEmpty` 无默认值，必须进入 binding identity/equality/失效比较。仅当前 physical `GetConnInstance().ConnProperty` 的 override 键存在、为 string 且长度0，才有资格读取有效 ServerEncoding；缺失、null、非string、空白或非空同名编码一律 unknown。禁止在新 binding 路径默认传 true、重新读 logical ConnProperty 或按版本大小继承。

精确 scope 为 shared `(8.1.5.60,21,utf-8,true)`→VerifiedSeven；TLS `(8.1.4.6,11,gb18030,true)`→VerifiedSix，其余 CompatibilityLegacyUnverified。TLS tuple 来自 v11 已接受物理基线，本次 TP03 新包未做 TLS 实库验收。unknown 的 legacy≤6 与真实 reliable fixed26/27 六位窄兼容保留。

## 2. 现有 Reader 接口与文件交接

这些接口已经实现，不再是建议名：

```csharp
// DmDateTime
internal static DmTemporalFields DecodeForReader(
    byte[] bytes, DmField column, int ltz = 0, int dbtz = 0);
// DmTemporalLegacyAdapter
internal static DmTemporalFields ReadNetwork(byte[] bytes, DmField field);
// DmTemporalInteger
internal static DateTime ProjectDateTime(DmTemporalFields fields);
internal static DateTimeOffset ProjectDateTimeOffset(DmTemporalFields fields);
internal static DateTime ProjectTimeDateTime(DmTemporalFields fields);
internal static DateTimeOffset ProjectTimeOffset(DmTemporalFields fields);
internal static TimeSpan ProjectTimeSpanExact(DmTemporalFields fields);
internal static string FormatExact(DmTemporalFields fields, int scale, DmTemporalTextKind kind);
```

`DmTemporalFields` 的 fraction 为 `NanosecondsWithinSecond`，offset 为 nullable 分钟；值记录无连接/field 引用。ReadNetwork 直接从 cell 按 CType 与真实 scale 解码；ordinary12 未证而拒绝，内部 BDTA fixture 使用明确 ns 布局。DecodeForReader 当前直接调用 ReadNetwork，ltz/dbtz 参数不触发时区转换；TLTZ 在 adapter 内明确拒绝，不可解释为已经完成 db→session 转换。GetTimestamp/GetTimestampTZ/GetTimeTZ/GetDate/GetTime 已走此 typed 路径；TP05 保留它并完成精确 GetString/schema/99格及恢复合同。

当前 `DmTemporalRawMetadata` 已是 immutable 八字段记录：`(Source, CType, Precision, Scale, TypeFlag, MaskBeforeNormalization, DerivedMask, ResultFormat)`，`HasServerSource` 仅对 ParameterDescribe/ResultColumn 为 true。**DmField.TemporalServerMetadata、TemporalBinding 与 GetTemporalReadDescriptor(actualFormat) 尚属 TP04 待实现接口**；TP05 不能假定已可调用。binding owner 先交付返回类型、各字段单位/nullable 语义、实际 format 与 raw 未观测 format 的区分及文件 SHA，再允许 reader 消费。

| 当前交接文件 | SHA256 |
| --- | --- |
| `Internal/Types/DmTemporalCapabilityProfile.cs` | `55db8dc195cc443d76911ec571f263feecf414873f969caf0a60b33f241aea90` |
| `Internal/Types/DmTemporalLegacyAdapter.cs` | `072021841c0c02b941c0346890531a5786d42bdd156739ba5148df5b8c3a39fa` |
| `Internal/Types/DmTemporalRawMetadata.cs` | `7aae6184c8b12a25e276364743f17081c7a9acd6f92c743046532252c7584cac` |
| `Internal/Legacy/Dm/DmDateTime.cs` | `661f4822646ab7ccfbbbad082ed389ddc98955a09c5cd516c93f0b490fac85c9` |
| `Internal/Legacy/Dm/DmSetValue.cs` | `d99dd73e5fd32b45e277073d93145b5fb7942758dd5893f48086618b259880ef` |
| `Internal/Legacy/Dm/DmGetValue.cs` | `5427949c8f9a98c3ff0c20db46af198c1713dfd0f2813a1795737f9cb1a1a540` |
| `Internal/Legacy/Dm/DmField.cs` | `bf15fb6e0d542ed0a8178598cfc2759f0251251df8249d21ee394acf88903d31` |
| `Internal/Legacy/A/T02_02000094.cs` | `4637e5cd01a521ef72de98bef5c511b651a1b57fac68e7af907af1b112ef42ab` |

文件路径相对 `src/W.DamengProvider/`；其余 pure core 保持 v6 manifest 身份。

## 3. TP04 writer 窄窗必须覆盖数值及文本

当前入口仍是：

```csharp
// DmSetValue
internal void SetResolvedObject(DmParamValue paraVal, object x, DmConnection conn,
    string typeName, int cType, DmParameterInternal paraInternal,
    int? precisionOverride = null, int? scaleOverride = null);
// DmDateTime
internal byte[] EncodeWithTarget(DmField column, DmConnection connection,
    DmTemporalWriteTarget target);
```

SetResolvedObject 目前从 mutable field/override 重建 target；EncodeWithTarget 又读取 `Profile(connection)`。因此 TP04 先冻结 `DmTemporalParameterBinding` 的真实类型/构造签名、profile/target/EncodedScale/WireScale/interval 字段及发布规则，然后交 codec owner 窄窗贯通实际编码消费。**文本 EncodeWithTarget 的 profile 重捕获也必须覆盖**：可新增接收 frozen profile/target 的内部重载并让本次 command 路径传入；旧 helper 独立入口可保留其原始语义。不能只增加 field property 或只改 SetResolvedObject 入口而让文本另行选择 profile/target。

root 已批准 codec 窄窗覆盖必要 DmDateTime 文本重载。最小消费接口在本 brief 冻结为以下新增入口（尚未实现）：

```csharp
// DmSetValue: binding 是本次全部验证成功后待发布的同一 immutable 描述。
internal void SetResolvedTemporalObject(DmParamValue paraVal, object x,
    DmConnection conn, string typeName, DmParameterInternal paraInternal,
    DmTemporalParameterBinding binding);
// DmDateTime: 不再接收连接或可变 field，不在此重取 profile/target。
internal byte[] EncodeWithTarget(DmTemporalCapabilityProfile profile,
    DmTemporalWriteTarget target);
```

`DmTemporalParameterBinding` 的具体 immutable 声明仍由 binding owner 交付；消费合同必须提供 frozen Profile、有效 CType/precision/EncodedScale/WireScale，以及 DATE/TIME/TS/TZ 的 `DmTemporalWriteTarget`（interval 使用独立 packed 约束，不伪造 target）。入口不再另收可与 binding 冲突的 cType/precisionOverride/scaleOverride。`conn/paraInternal` 仅用于兼容既有调用及非决策上下文，禁止从它们重算本次 temporal profile、fixed 来源或 target。数值、文本、typed NULL 均消费同一 binding；文本重载参数直接来自该 binding 的 Profile/Target。全部绑定和编码成功后仍按 TP04 原规则整体发布，编码失败不能留下半成品 observable binding。

binding owner 不改 DmSetValue/DmDateTime/adapter；codec owner 不改 DmField/Command/plan/decoder。binding 的字段签名与文件 SHA 交接后才实施 codec 窄窗；无绑定 legacy helper 的现有入口和行为保持。完整 Execute 帧需独立验证实际 CType/header/payload 与 binding 一致，NULL、数值、文本同源；interval 保留 packed wire scale 与 EncodedScale 的区别。

## 4. 保持已批准裁决与开窗顺序

- `DbType.DateTime2 + provider DateTime` 两显式声明相容时保留默认7意图，两 setter 顺序一致；单独 provider DateTime 默认6。
- 最终时间目标先校验显式 Scale 0…9，再拒绝非零 Precision；不将时间规则套到 decimal。原始 unset/显式0 分别冻结。
- raw capture 覆盖全部 ParameterDescribe、仅 temporal ResultColumn；真正 decoder 来源、raw/semantic 分离及每次 describe 清旧 binding 规则不变。
- root 接受 TP03 并确认历史 snapshot 后，先开 TP04 的 metadata/binding 接口窗口；冻结只读 descriptor 后才能让 TP05 reader owner 接入。writer codec 消费须另窄窗，相关 owner 停写后统一新 freeze，再由 Low 独立验证。不得用本 brief 或旧 v6 包关闭 TP04/TP05 运行门。

本补充没有发现需要撤销 TP03 接受的缺陷；以上是 TP04/TP05 原定范围中的接口准备。源码仍冻结，本文件完成后停止写入。
