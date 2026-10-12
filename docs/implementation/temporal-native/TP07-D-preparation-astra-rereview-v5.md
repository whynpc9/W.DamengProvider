# TP07 D 外部准备 v5：单行fixture修复复核

2026-10-09。结论：**DPREP-V4-B01_closed / source_diff_scoped_accepted / Low_snapshot_binding_pending / D_runtime_not_authorized**。按root允许先完成源码差异复核；Low新五文件快照尚未生成，最终接受仅绑定下表精确字节，快照逐项匹配前不得进入离线执行。未运行或导入测试/准备脚本、SDK、DB或网络。

相对v4，唯一Python变化是 `test_pruning_contracts.py.setUp` 将临时根改为 `(Path(self.temporary.name) / 'replay').resolve()`。fixture路径、manifest字典键及expected均由同一规范化根生成，与生产graph validator的Project library路径resolve比较一致；消除macOS `/var` / `/private/var`别名误拒绝。validator及异常捕获完全未改，没有弱化pruning/路径/包图合同。

README如实记录 **9个test_方法**，并明确Low从原批准v5目录discovery；该调用与 `HERE.parents[3]` 的workspace/固定fixture定位一致。freeze/source副本是证据快照，不能直接替换执行目录；Low需核原目录执行前/后字节与新快照一致。原有正反例和graph实现均保持。

| v5文件 | 本次实算SHA256，后续Low快照必须逐项匹配 |
| --- | --- |
| `prepare-d.py`（与v4相同） | `e820119dda9e72740c1cd727bc8a85283ca80774a4461e878900b28e4cf57261` |
| `build-generated.py`（与v4相同） | `4364da01fdecb97dc7063002d730926e45afb4923606ac1b9c69b22565fdfccf` |
| `test_pruning_contracts.py` | `19d8732cbc4ac6ca442e05140925c7e015eea02029be6895618e1e33172204f5` |
| `README.md` | `0c388b3a6c30bbd31c62725c4e8fdb321b70ff3472e5e21bd8ad3ac2cc6e029c` |
| `schema-draft.json`（与v4相同） | `01c15504147b84a9f0a52ef7146e71a7456ef9acafd1b0b5b6db5aaed2785323` |

原v4五文件及snapshot均匹配原manifest `4071ca5e72b54302b3317d7c376d4b056ec18bf445efd397f31d2dcdafe2f076`；[v4报告](TP07-D-preparation-astra-rereview-v4.md)仍为 `31b1635ce35113d14d53a2016f59e88df399d4104bca7beed0910845c37ced12`，原N0失败和partial证明不变。

未发现此单点修复新增阻塞，继承v4对逐项目13/24图、实际锁并集33及框架来源的有限审查。Low快照匹配后，可按root独占窗口执行全部9项离线合同；本报告不是测试通过。新P0/N0真实事件及native证明、B0/BC、未来generated自身图、E前置门、D DB及完整EF-C继续pending。若新快照任一文件不同，须停止并重新核对，不可把本报告绑定到另一个版本。
