# TP07 D-v5：B0编译错误定点源码复核

2026-10-09。结论：**D_B0_compile_source_findings_closed / scoped_static_accepted_for_independent_rebuild / runtime_pending**。只审本次编译窄修及policy pin，未运行SDK、tests、CLI、DB或cleanup，未修改实现或外部准备脚本。源码问题已对应修复，实际编译通过仍须Low证明。

实际失败回读 `TP07-D-prepared-v2-control/build-failure-readback.json` 实算SHA256仍为 `1eacee6b8ffe50ed612f5315cbbfefacc53e6d5d2b213c32a6829ebcc8392c62`：B0的D编译报三个IDesignTimeModel CS0246和一个Spec.Kind CS8602；原失败、成功的native/准备证据及旧source快照均保留，不把静态修复回写为原build成功。

## 两处代码差异

`GuardedMigrationCases.cs`相对冻结旧源只有：

1. 增加 `using Microsoft.EntityFrameworkCore.Metadata;`。本机固定EF10.0.12的XML类型文档明确IDesignTimeModel位于此namespace，与三个实际报错的GetService调用对应；没有添加包、改API或引入替代模型服务。
2. `Program.Current.Spec.Kind`改为 `Program.Current.Spec!.Kind`。前一断言已访问同一Spec的Table/History；到Kind之间无await、Spec赋值或切换case。该注解只补编译器nullable分析，不改变短路条件、异常路径或运行时校验。原Nullable与TreatWarningsAsErrors策略未改。

没有SQL、guard顺序、参数、值域、read oracle、清理行为或其他业务差异。未发现此定点修复引入新的确定编译/API阻塞；本报告不替代真正编译。

## 精确身份

| 输入 | SHA256 |
| --- | --- |
| `GuardedMigrationCases.cs` | `b633f60af10319be21c7c3e304f43dfe6950d7fd224ab49c84ef006b90386352` |
| `design-cases.json` | `c8cae884d75c763d407452f3b414f9971135adcb64aea9cbd3570eeef50c356f` |
| D十源aggregate（repo-relative排序） | `0860596c14ac66ae8840eaca4dbd707f069d37b12756d1b8d5cb4b7830620521` |
| Low `TP07-D-v5/source-manifest.json` | `ee751e9dc9a5a2f29d5ffdc6a8aef849dc81effdd5c522279fd4b9e03c112ef4` |
| Low `TP07-D-tools-v4/source-pack-manifest.json` | `124cbc1897846a4bc23d433ff71ca2baa3942780cb26363f2f7ba890a38336c0` |
| Low `TP07-D-tools-v4/source-manifest.json` | `ed4c58a011c3f4e2e566b579c711aee41b874aee97aaad475729a89b818a15e0` |

十源及25-pack的live/snapshot均逐项匹配。policy只改Guarded文件的immutable hash一条；其余九模板/23-pack文件不变，旧25项snapshot完整保持。没有混入其他owner的当前WIP。

外部准备脚本仍保持原版本，消费新D25所需pin-only修订另行审阅；不能直接改旧B0/native receipt中的包身份。完成该接线后由root安排Low独立构建，保留新命令、source/locks/DLL/MVID与原失败。BC、真实CLI、G6/G12、D实库及完整EF-C仍未接受。
