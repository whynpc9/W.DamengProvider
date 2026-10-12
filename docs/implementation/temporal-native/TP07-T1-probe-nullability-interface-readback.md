# TP07 T1 probe：固定 reference nullable 接口回读

2026-10-10。结论：**source_nullable_contract_gap_confirmed / compilation_not_executed / narrow_source_revision_required**。冻结 producer15 的四个 ADO string setter 与两个 scalar 完成回调确有 nullable 注解差异；这是实际 reference 元数据确认的源接口风险，不是已观察到的编译失败，也不是 CLR 找不到 override 方法。本文仅记录修订前实物，未执行 SDK、程序集、编译、tests、validator、DB、wrapper 或读取 secret。

观察时 producer15 聚合 SHA256 为 `3d22e651aaf4802bd651ea3c377107a194b58e15b39797ff29ccfa474bf11b96`，逐项 SHA 与冻结值相同。v3 只有 validator 修订，本文的两个 host 源仍为冻结 producer 字节；不改旧 v2/v3 snapshot、旧 proof/status、产品178或任一 probe 源。

## 1. 输入实物与读取边界

[Astra v2](TP07-T1-probe-source-review-v2.md) SHA256 `a64934090af4c5edd62fbd5839f816bfb0647b2955a27d24be7d799595365c8d` 的第106行提出本接口风险。此次直接读取如下安装实物的 PE/ECMA-335 元数据：

| 实物 | SHA256 | MVID / 位置 |
| --- | --- | --- |
| .NET10 reference `System.Data.Common.dll` | `e563226dce5e8d2f1ecf24b13a37d8948e42208d7b93bc9812475074382f2303` | `2a30cb39-8185-44e9-ad93-629b6ce26632`；`/Users/wanghongyi/.dotnet/packs/Microsoft.NETCore.App.Ref/10.0.12/ref/net10.0/System.Data.Common.dll` |
| EF10.0.12 `Microsoft.EntityFrameworkCore.Relational.dll` | `a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d` | `03b92316-8856-455e-a1c0-0602583c8738`；`/Users/wanghongyi/.nuget/packages/microsoft.entityframeworkcore.relational/10.0.12/lib/net10.0/Microsoft.EntityFrameworkCore.Relational.dll` |
| .NET10 `System.Data.Common.xml`，上述 ref 同目录 | `13e07e558f15386211e58eb577ab93edd2df4f5f497030fa8efe8e17eee7cb0c` | property member entries 第1154、1516、3145、3161行 |
| EF10.0.12 Relational XML，上述 DLL 同目录 | `fa350c8d3bdd67f183f7c4a2582a3a5ca9fc1c2a10c991bcb69c94796b3132d5` | `DbCommandInterceptor.ScalarExecuted` / `ScalarExecutedAsync`，第1891、1900行 |

使用临时纯 Python 字节读取：从 PE section 映射 CLR metadata root，读取 `#~` 的 row counts/index widths、`#Strings`、`#Blob`、`#GUID`；解析 TypeDef/MethodDef/Param/Property/PropertyMap/CustomAttribute，并通过 CustomAttributeType 与 MemberRefParent 解析真实 attribute 构造类型。以下 token、signature 与 attribute blob 均来自原文件，未调用 `Assembly.Load`、reflection、IL 或 dotnet。file offset 是整个 DLL 的十进制字节位置，不是 RVA。

XML member ID 会把 `object` 与 `object?` 都表示为 `System.Object`，且不会列出这些 setter 的 AllowNull，因此不能仅靠 XML 声称 nullable 签名相同。本次以实际 custom attributes 判定。

## 2. 四个 ADO setter 的 AllowNull

三个声明 TypeDef 均有 `System.Runtime.CompilerServices.NullableContextAttribute(1)`，payload `0100010000`，位于 ref DLL offset `132917`：DbConnection `0x02000099` / CA `0x0c000294`；DbCommand `0x02000097` / CA `0x0c00028b`；DbParameter `0x020000a5` / CA `0x0c0002ca`。因此 string getter 的默认注解为非空。

| 实际 property / Property token | setter MethodDef | value Param token / sequence | AllowNull CustomAttribute token |
| --- | --- | --- | --- |
| `DbConnection.ConnectionString` / `0x1700019f` | `0x060008f7` | `0x08000955` / 1 | `0x0c0006b3` |
| `DbCommand.CommandText` / `0x17000189` | `0x060008a3` | `0x08000922` / 1 | `0x0c00069d` |
| `DbParameter.ParameterName` / `0x170001d0` | `0x06000a10` | `0x08000a58` / 1 | `0x0c0006fb` |
| `DbParameter.SourceColumn` / `0x170001d4` | `0x06000a18` | `0x08000a5c` / 1 | `0x0c0006fc` |

四个 value 参数的 attribute 构造类型都解析为 `System.Diagnostics.CodeAnalysis.AllowNullAttribute`，原 payload 均为 `01000000`，共享 blob offset `124406`：custom attribute prolog `01 00`，无 fixed 参数、named 参数计数0。getter MethodDef 分别是 `0x060008f6`、`0x060008a2`、`0x06000a0f`、`0x06000a17`。getter signature `20000e`、setter signature `2001010e`、property signature `28000e`，其中 `0e` 是 STRING。

实际合同是**getter 保持非空 string，setter 允许 null**；不能用把整个 property 改成 `string?` 来代替 AllowNull。冻结 [OfflineContracts.cs](../../../tools/DownstreamAcceptance/TP07/transaction-capability-probe/host/OfflineContracts.cs) SHA256 `f8fa83dee618038bf72eaf10ad634d06985302efdbbec056e7dc91f7ab2bc9bf` 的第97、120、128行含四个无 AllowNull 的非空 string 自动属性，setter 注解因此收窄了 base 允许的输入。

批准的产品 unit 源 SHA256 `c7b5520f6a5863af5924f94a6d5a913317a164e31d6a90d18c7fd47f37eb5220` 的第240、304行已分别给 ConnectionString / CommandText 使用 `[AllowNull] public override string ...`，与上述 fixed ref 一致。本次只读该源，不把这个对照当成该 probe 编译证明。

## 3. EF scalar 完成回调的 nullable 返回与 result

实际声明 TypeDef 是 `Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor` / `0x020002ef`，其 NullableContext(1) attribute token `0x0c000f5f`、payload `0100010000`、EF DLL offset `2064507`。方法没有另一个 NullableContext 覆盖；以下返回/参数的显式 NullableAttribute 覆盖类型默认值。

| 方法 / MethodDef | 实际 CLR signature blob | 返回 Param / Nullable CA | result Param / Nullable CA |
| --- | --- | --- | --- |
| ScalarExecuted / `0x06002019` | `20031c128321128b941c` | sequence0 `0x08002b36` / `0x0c003037`；`0100020000` | sequence3 `0x08002b39` / `0x0c00303b`；`0100020000` |
| ScalarExecutedAsync / `0x0600201c` | `200415118441011c128321128b941c1181a5` | sequence0 `0x08002b42` / `0x0c003045`；`01000200000000020000` | sequence3 `0x08002b45` / `0x0c003048`；`0100020000` |

`0100020000` 解为 NullableAttribute(byte 2)，blob offset `2064501`，即 nullable object。async 返回的 `01000200000000020000` 解为 NullableAttribute(byte[] `[0,2]`)，blob offset `2004848`：外层 ValueTask 为 value type，内部 object 类型参数 nullable。真实合同为：

```csharp
object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result);
ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
    object? result, CancellationToken cancellationToken = default);
```

冻结 [TransactionEvidence.cs](../../../tools/DownstreamAcceptance/TP07/transaction-capability-probe/host/TransactionEvidence.cs) SHA256 `745e1ff14de7bb372695084a56c168749eb06cb41370b34e82e5972350be6792` 第100、101行使用 `object result`，sync 返回 `object`、async 返回 `ValueTask<object>`。两个 result 参数都收窄了真实 nullable 输入；async 泛型返回也未匹配内部 nullable 类型参数。sync 非空返回可以是更严格的输出承诺，但现有实现直接返回 result，改正 nullable 输入后应同时使用 `object?` 返回，继续原样转发实际 null。

这些差异主要在 nullable custom attributes，CLR 的 OBJECT 类型不会因问号变成另一个 runtime 类型。本次没有观察 MissingMethod、编译诊断或执行失败。

## 4. 生效的编译设置与最小待审范围

批准 EF178 的 `Directory.Build.props` SHA256 `21806cf673ba37d0eef9d76c316fc37d2cbedb2527101c617e5a722bc913fb67` 第5、7行为 `Nullable=enable`、`TreatWarningsAsErrors=true`。prepare 将 host 放入 replay/test 下，因此它继承该 props。固定 csproj 同样开启 Nullable；`EnableNETAnalyzers=false` 不等于关闭 C# nullable 检查，NoWarn 只追加 EF1001。元数据差异因此需要在进入 Low 编译门前作窄源修订，不能以关 Nullable、降 warnings 或抑制诊断过门。

最小源范围仅两文件、无新文件/依赖/SQL/行为分支：

1. `host/OfflineContracts.cs`：引入 `System.Diagnostics.CodeAnalysis`；四个现有 string 自动属性各加 `[AllowNull]`，保留 getter 的 string 合同、现有默认值与 stub 行为。若以后改为显式 setter，value 的 null 许可必须正确处理，不能把 nullable value 直接当作非空输入。
2. `host/TransactionEvidence.cs`：完成回调改为 `object? ScalarExecuted(..., object? result)`、`ValueTask<object?> ScalarExecutedAsync(..., object? result, ...)`；仍记录同一个 End callback 并原样返回 result，可显式使用 `ValueTask.FromResult<object?>(result)`。其余回调、B06 current-null/binding、事件 schema、计数与 live 四例不改。

这不是 probe17 全部 nullable 接口或编译的接受；只确定题列六处合同。修订必须新冻结两个 SHA、producer15/pack17 与 host9 来源，Low 之后重新生成新的 host manifest Ref，并保留旧 v3 snapshot。实际编译、28 离线合同及四例实库继续 pending。
