# TP07 D 外部准备 v4：pruning修复静态审阅

2026-10-09。结论：**逐项目pruning/union实现符合有限设计；一个test-only路径阻塞，request_changes。** 未运行或导入准备脚本/合同，未执行SDK、tests、DB、cleanup或网络，未修改受验源码、C#或旧制品。

## 冻结身份

依据 [native graph归因设计](TP07-D-native-graph-triage.md)，SHA256 `5ac2385f55a3afe2bbbf1f82f00f003c13bd576d03493c79f9f8becba032095b`。Low `.local/temporal-native/verification/TP07-D-preparation-v4-freeze/source-manifest.json` 实算SHA256 `4071ca5e72b54302b3317d7c376d4b056ec18bf445efd397f31d2dcdafe2f076`，五项live/snapshot逐项匹配：

| 文件 | SHA256 |
| --- | --- |
| `prepare-d.py` | `e820119dda9e72740c1cd727bc8a85283ca80774a4461e878900b28e4cf57261` |
| `build-generated.py` | `4364da01fdecb97dc7063002d730926e45afb4923606ac1b9c69b22565fdfccf` |
| `test_pruning_contracts.py` | `4a1dd9bcbc41cd91d50db87583b1d666365413e1247161bafb0fe0eafc4f478a` |
| `README.md` | `efd0334347e66fdd3b862ebe15b9d2599843eae85030b6ec65ab57901e9a3123` |
| `schema-draft.json` | `01c15504147b84a9f0a52ef7146e71a7456ef9acafd1b0b5b6db5aaed2785323` |

旧v3原文件与snapshot保持不变。失败summary `c2a1d79c…`和partial native readback `69670a03…`实算未变；没有回写旧N0接受旗。

## DPREP-V4-B01 / P1：合同setUp没有真正规范化临时根

位置：`test_pruning_contracts.py`的 `setUp`（`self.root = Path(self.temporary.name) / 'replay'`），对照 `prepare-d.py.graph_documents` 的actual Project library路径比较。

README和交接称使用canonical temp路径，但实际setUp没有调用resolve；`canonical`只是把原fixture绝对前缀替换成这个未规范化根。validator对asset project library路径执行 `.resolve()` 后，与基于原root拼写的expected_paths比较。本机标准库temp根实读为 `/var/folders/…/T`，resolve为 `/private/var/folders/…/T`，因此provider空Project集合正例可过，L0/D/generated正例会因别名差异在 `actual_project_library_paths` 拒绝，union正例也会在构造其前置图时失败。

最小修复：仅把该setUp的replay根在一处 `.resolve()`，随后所有fixture路径、字典键和expected均来自同一根。不要放宽validator、添加任意路径等价fallback或扩大异常捕获。修复后新freeze并由Low执行全量合同，不能把当前静态推导称作一次已运行失败。

另一个记录差异：AST实数是 **9个test_方法**，不是交接所称10个；Low此次freeze已如实记录。无需为凑数新增方法，后续应按实际discovery报告计数。当前没有执行这些方法。

## pruning与实际接口核对结果

- `graph_documents`按provider/L0/D/generated四个有限角色核自己的assets project与自身dgspec节点，精确net10.0、SdkAnalysisLevel10.0.400、FrameworkReferences及同replay Project路径。provider/L0固定Core prune字典，D/generated固定AspNet字典；完整canonical SHA分别为 `1c2e6921…` / `e4a81679…`，实读失败轮actual assets重算匹配，字典数量为272/412。
- 与原Spec33交集只允许空集或固定九项及 `(,10.0.32767]`，不从缺包动态推断新允许项。实际Package集合精确13/24，W保持包而非Project；保留包resolved/contentHash仍对原源。依赖边只删除指向九项的边，精确核四个父record，其它所有边和约束字符串不变。
- 三个actual fixture的dgspec项目集合、FrameworkReferences、prune字典及assets library Project路径已独立只读核对；九个fixture文件的硬编码SHA全部匹配原失败轮。fixtures保留SDK实际形状，未把它们重标为成功证据。
- 新`graph()`实际读取每个项目自己的lock/assets/dgspec，核批准SDK runtime graph路径并输出引用。generated调用该相同角色函数处理自己的nonce项目，要求两个Project及直接D引用；不能用D旧assets替代。G6/G12原锁、base资产/host、source数量与父binary快照逻辑保持。

## 33包native证明与框架来源

`union_occurrences`从实际L0/D图形成精确33集合，校20 D-only/9 L0-only/4共有，并检查每个实际出现位置的resolved/contentHash和按角色处理的dependencies。每row记录所有lock引用，不任选一锁遮掩另一锁，也不索引或伪造D中不存在的九行。原dependencies与actual dependencies分开保存。

native_proof继续读取fresh NuGet metadata，要求等于原Spec及全部actual lock occurrence；B02的完整archive sidecar校验保持独立，archive/feed/cache/nuspec及原/fresh文件身份检查未删。N0把逐项目graph/pruning证明与原生记录一起留存。

`pruning_framework`固定SDK targets、targeting metadata、runtime deps及九个ref/runtime DLL。只读实算18个DLL均匹配代码pins，版本/提供关系与原设计一致；文件纳入before/after保护，说明仍是installed expected、不是loaded观察。只有D/generated使用该证明，cleanup分支不读取AspNet九文件，原B01 Hosting/cleanup边界未倒退。

外部inputs新增必需 `pruning_design` 精确ref，P0源origin refs同步更新；旧P0缺此来源不能拼入新流程。新P0/N0要求新目录/空cache和真实SDK事件，没有用旧成功restore加新标签绕过旧validator失败，也没有改C#或关闭pruning。

## 合同目标与后续界线

修正temp根后，现有正例会经过真实形状的完整角色检查；负例分别修改框架/range/额外prune/分析级别、非九项缺包/额外包/伪造D九项、非prune边及约束、contentHash、union覆盖/另一锁边和generated Project路径/数量/自身节点。缺generated own-node的KeyError是有意删除必需节点的负例，不等于该shape的成功证明；所有完整正例必须先实际通过。generated正例本身仍是合成形状，不能替代未来native restore。

当前只需test-only单点修复和计数澄清后再短复核。静态上未发现此次pruning/union生产逻辑的其它确定阻塞；仍须Low离线合同、新P0/N0原生证明与后续B0/BC，G6/G12等待真实checkpoint。E/model门、D DB及完整EF-C未由本报告接受。
