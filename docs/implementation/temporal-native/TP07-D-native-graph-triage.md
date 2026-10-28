# TP07 D N0：FrameworkReference pruning归因与有限图校验设计

> 阅读提示：这是历史依赖图失败归因与工具设计，不是当前构建或 EF 适配指南。固定候选、状态和审批仅属于当时；当前驱动行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-09。结论：**D的24包及四组删边由本次明确framework pruning解释；外部准备的逐项目图合同需要修正，原N0仍保留失败。** 本轮只读实际lock/assets/dgspec、新cache和本机SDK/框架文件；未运行SDK、restore、tests、DB、模块导入或网络，未改C#、FrameworkReference、pruning设置、锁或旧证据。

## 1. 实际运行与失败边界

实读 `TP07-D-prepared-v1-control/verification-summary.json` SHA256 `c2a1d79ce6ca8f5e78dee56ce29412b5b86322142fbee934ee59423d0043169d`：P0的workflow193/cleanup182准备完成；N0实际SDK10.0.401查询、L0/D两次force-evaluate restore共3事件全部exit0。随后外部 `actual_package_graph_discrepancy`断言失败，没有发出最终N0 lock manifest或accepted native proof，没有build、CLI、wrapper或DB执行。

实际D lock SHA256 `678b6a6a5cd173a61e633ed4390b121175e94244b415019ca31281d666798171`：24个非驱动Package + W + provider Project，共26条。L0仍为13个非驱动Package + W + provider Project，共15条；provider和L0 lock与原接受字节相同，分别为 `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`、`896258992bb91e4289953fef191287f8e6fe496706d6a3df070a85eb5dc5916e`。

`native-cache-readback.json` SHA256 `69670a03a5c4072ebc8fb0c563dd96bda0747a424da8bae07cdcf4260779a530`明确 `accepted=false`。本轮独立核D/L0实际锁并集33项：全部保留原Spec resolved/contentHash；fresh metadata匹配各自实际锁及原Spec，archive/feed/original归档、zip/cached/original nuspec、原/fresh sidecar字节一致，sidecar独立匹配完整ZIP SHA-512。**这是失败轮的只读回读，不把旧失败改成新restore通过。**

## 2. pruning的实际来源链

本机SDK10.0.401的主文件证据：

- `NuGet.targets:89–104`：满足SDK分析级别及net10目标时默认启用package pruning；本次assets记录SdkAnalysisLevel=`10.0.400`，SDK实际版本仍是10.0.401，两字段含义不同。
- `Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.FrameworkReferenceResolution.targets:57–74`：`AddPrunePackageReferences`在收集prune项前，通过 `GetPackagesToPrune`消费实际FrameworkReferences/targeting packs，输出PrunePackageReference。
- D的actual assets和它自己的dgspec均包含 `Microsoft.AspNetCore.App`（privateAssets=none）及 `Microsoft.NETCore.App`（all）；两者 `packagesToPrune`完整字典相同。它们与原33集合的交集恰为下列九项，每项range均为 **`(,10.0.32767]`**，涵盖原resolved10.0.12。
- L0只有Microsoft.NETCore.App；其assets/dgspec prune字典也相同，但与原33交集为空。不能把D的裁剪规则套给L0/provider。

| 缺失的九个Package ID（均Microsoft.Extensions前缀） | 原resolved |
| --- | --- |
| Caching.Abstractions、Caching.Memory | 10.0.12 |
| Configuration.Abstractions | 10.0.12 |
| DependencyInjection、DependencyInjection.Abstractions | 10.0.12 |
| Logging、Logging.Abstractions | 10.0.12 |
| Options、Primitives | 10.0.12 |

D assets的targets/libraries中确实不再有这九个Package；这不是额外网络包、损坏archive或人为删lock。AspNetCore.Ref 10.0.12的PackageOverrides.txt列出这些ID，FrameworkList.xml列出对应九个ref DLL；实际 `ref/net10.0/*.dll` 和 `shared/Microsoft.AspNetCore.App/10.0.12/*.dll` 均存在并已读取hash。runtime deps将九个文件均声明为assemblyVersion10.0.0.0/fileVersion10.0.1226.42308。此处证明安装/声明提供关系，不声称D已加载这些DLL。

| 关键实际输入 | SHA256 |
| --- | --- |
| D `obj/project.assets.json` | `39ec18204b1b12cd186096ada0d652a084d3580c90a8b5180bc67cb090a0f60e` |
| D `obj/TP07TemporalDesign.csproj.nuget.dgspec.json` | `249e94c380fc273273008343daa97b6150325c779fc4bee2c33e5e08d0dfe6e3` |
| L0 `obj/project.assets.json` | `c2ba7f50236b09bbf168de1b0d280311e2a5722c36fc9d7d3d7cf144e8c97f73` |
| L0 `obj/TP07LiteralProbe.csproj.nuget.dgspec.json` | `b390ea5a6ab52a70af4b7ba05102c0ad70955818bb54b03b2ec08b3aa5aec453` |
| SDK `NuGet.targets` | `ce10b22f349503468546cd445e3e0ed7637e6b759b90a225a6c96ba2759bd0fb` |
| SDK `Microsoft.NET.Sdk.FrameworkReferenceResolution.targets` | `ffd9fabffb90c1a1827ed5523656153f1cd4075a25abf8b55c72da09f013aa13` |
| AspNetCore.Ref `data/PackageOverrides.txt` | `e16e73725152c809effe44f02c8bc568bae0f646ca3a49d3990757998b9a4704` |
| AspNetCore.Ref `data/FrameworkList.xml` | `f2d7a9639bb592d202c767872ae3d30ee2baf4d1a27afd5f9ad83be93a40011e` |
| AspNetCore runtime deps | `016b52cb9f578fb396a4b495d7c7d2f98389825bb1e2c2df78a614c476e1a92f` |

上述SDK路径均位于 `/Users/wanghongyi/.dotnet/sdk/10.0.401/`；ref/runtime分别位于 `.dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.12/` 和 `.dotnet/shared/Microsoft.AspNetCore.App/10.0.12/`。

## 3. 四组边差异精确对应九项pruning

所有24个保留Package的resolved/contentHash与原Spec一致。只有以下四条record的dependencies变化，变化仅为删除表中边；其余依赖名、约束字符串逐字保持，无新增或版本替换：

| 父Package | 被删的Microsoft.Extensions依赖边 |
| --- | --- |
| Microsoft.EntityFrameworkCore | Caching.Memory、Logging |
| Microsoft.EntityFrameworkCore.Relational | Caching.Memory、Configuration.Abstractions、Logging |
| Microsoft.EntityFrameworkCore.Design | Caching.Memory、Configuration.Abstractions、Logging |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | DependencyInjection、Logging、Logging.Abstractions、Options、Primitives（原约束均9.0.0） |

其余缺失节点来自这九项内部的传递关系。不能以“包有相同contentHash”忽略任意边差异，也不能要求D保留已由其显式prune表删除的边。

## 4. 可实施的有限校验规则

保留原Spec Design33作为**原始包/archive证明全集**；修正的是每个实际project的runtime graph期待，而不是把原33集合改成24或修改任何生成锁。

| 实际项目角色 | 精确非驱动Package集合 | Project项 |
| --- | --- | --- |
| provider | 原接受provider13，原边不变 | 0 |
| L0及protected cleanup | 原13，原边不变 | 唯一provider |
| D | 原Spec33减固定九项，精确24 | 唯一provider |
| 未来generated consumer | 预期同D的24，须由自己的实际restore证实 | provider + TP07TemporalDesign，精确2 |

`graph`窄修应按已知project角色执行以下检查，不新增通用MSBuild/版本解析器：

1. 实读本项目assets及自身dgspec project节点，核net10.0、原FrameworkReference集合、同approved replay的真实ProjectReferences、SDK分析级别及两份相同prune metadata。provider/L0与原包全集交集必须为空；D/generated与原33交集必须恰为固定九项及上述range。任何额外prune、未知框架/Project来源都失败，不能动态把所有缺失节点解释为允许裁剪。
2. 实际Package ID集合必须等于该角色精确集合，W仍是原候选包而非Project/官方fallback；不允许额外包或仅核数量。每个保留包resolved/contentHash仍与原源完全相等。
3. D/generated的expected dependencies由原record**仅删除指向这九项的边**得到，再逐字比较实际边集合和约束字符串。L0/provider完全不删边。必须同时核四个预期删边父节点及其余所有不变record，不能通用忽略Microsoft.Extensions前缀。
4. D/generated另外保存对应SDK targets、实际assets/dgspec、targeting metadata与九个ref/runtime DLL的path/SHA证明，以及runtime deps中的版本声明。它们是framework提供关系的依据；不重建NuGet包、不把framework DLL记成该项目的PackageReference，也不推称已经加载。cleanup角色不引入AspNetCore.Hosting/九文件依赖。

## 5. native33证明改为两份实际锁的并集

当前N0实际分布为D-only20、L0-only9、两边共有4，并集精确33。这与原始33+W local feeds和20新包证明目标相容。

`native_proof`不能再对33项一律索引D actual_lock，否则修完graph后仍会在九项上KeyError。它应接收本次实际L0与D lock引用/哈希：

- 每条原始33记录必须至少出现在一份实际锁中；记录它出现的所有project/lock ref，以及各自actual resolved/contentHash/dependencies。两锁并集必须精确等于原33，不以cache存在充当实际锁覆盖。
- fresh NuGet metadata.contentHash必须等于原Spec及所有实际出现的lock record；包archive/feed/cache/nuspec、分离的sidecar完整ZIP校验和原/fresh字节约束继续保留。
- 原dependencies单独保留；每个actual lock的dependencies按该项目pruning角色核验并留证。不要选一个看起来相等的锁掩盖另一个锁的错误边，也不在D锁内合成不存在的九条record。
- Native proof另绑定逐项目graph/pruning证明。九个从D prune的Package仍由本次L0实际native restore覆盖；D使用framework九文件的提供证据单列。W自身仍沿原候选/native内容/loaded DLL门。

G6/G12不可提前借本轮D的assets冒充自己的restore结果。生成项目自身显式Design及AspNetCore引用静态上预期同24，但必须核它自己的prune字典、两个Project及actual lock/edge；G6首次native graph、G12原lock保持和base资产不漂移门继续有效。closure文件数193/195/202/208、cleanup182/183不因Package节点数变化而改动。

## 6. 交付与证据边界

High只改外部prepare/generated图校验、native-proof来源及有限schema/文档，保留C#、FrameworkReference、SDK和原始锁。补有限离线/只读合同：D24正例、L013正例、wrong framework/prune range、缺非九项包、额外包、乱删非prune边、保留边约束变更、任一contentHash漂移、union缺项/虚构D record，以及generated Project路径/数量错误。

新source freeze与审阅完成后，由root决定Low后续窗口。若选择消费本次已成功SDK事件做新的独立图重验，必须明确标为**既有事件复核/复用**，保留原N0失败、partial readback及其hash，不能回写旧summary或声称新restore发生；若安排新N0，则用新的空cache、目录和真实事件。当前报告只交付归因和可审实现设计，不生成accepted native proof、D build或runtime/model批准；B0/BC、G6/G12及完整EF-C仍待原门。
