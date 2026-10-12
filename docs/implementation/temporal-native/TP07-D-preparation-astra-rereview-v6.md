# TP07 D 外部准备 v6：编译修复后的pin-only复核

2026-10-09。结论：**pin_only_revision_scoped_static_accepted / ready_for_root_scheduled_Low_preparation / D_runtime_pending**。仅比对身份更新与冻结闭包，未运行或导入准备脚本、tests、SDK、CLI、DB或cleanup。

相对v5，`prepare-d.py`的完整Python diff只有两处常量值：批准D25 source-pack SHA从旧`9c8d6022…`改为 `124cbc1897846a4bc23d433ff71ca2baa3942780cb26363f2f7ba890a38336c0`，source root从`TP07-D-tools-v3/source`改为`TP07-D-tools-v4/source`。其余图、native证明、源/锁计数、SDK命令和构建逻辑逐字不变。

新pack实际25项与snapshot匹配，绑定D-v5十源manifest `ee751e9dc9a5a2f29d5ffdc6a8aef849dc81effdd5c522279fd4b9e03c112ef4`；其Guarded Metadata using/Spec!注解已由 [D-v5代码报告](TP07-D-astra-rereview-v5.md)限定接受，policy仅同步该hash。prepare仍逐项核pack、policy immutable输入及原L0七模板，没有借pin更新放宽来源。

| v6文件 | 实算SHA256 |
| --- | --- |
| `prepare-d.py` | `bd1433e90bf90123b536350077b161c2878db9d8142d028d311407c6da6768f3` |
| `build-generated.py`，与v5相同 | `4364da01fdecb97dc7063002d730926e45afb4923606ac1b9c69b22565fdfccf` |
| `test_pruning_contracts.py`，与v5相同 | `19d8732cbc4ac6ca442e05140925c7e015eea02029be6895618e1e33172204f5` |
| `README.md` | `a48b509b2e2b960e1e2663c0c0d23e1ae024c11b371846a9b0ad3db609904d34` |
| `schema-draft.json` | `e2ffe89886a9c558625dc7b0b36f930ba4018179ddfd72f6aaa0d304feafec9e` |

README/schema只更新D10/D25身份与原批准v6目录调用说明；9个合同及原目录层级约束保持。Low `TP07-D-preparation-v6-freeze/source-manifest.json` 实算SHA256 `8ba11d74eff1c9c6557c3b8471d6afd85bee1dc53a1bdbbacf7afb0aba8dcf9f`，五项live/snapshot全部一致。v5原文件及旧snapshot完整保持。

未发现这项pin-only变更的新增确定阻塞。可由root安排Low消费新身份，实际native/build事件必须是新执行或有单独明确的继承证据；不能把旧source-pack字段换掉后冒称新事件。原B0失败回读 `1eacee6b8ffe50ed612f5315cbbfefacc53e6d5d2b213c32a6829ebcc8392c62`实算未变，仍是失败。此次静态接受不替代D重新编译、BC、真实CLI、G6/G12、模型前置门、D实库或完整EF-C验收。
