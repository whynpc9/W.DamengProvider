# TP07 mapping-v3 analyzer 定点复核

2026-10-09。结论：**static_fix_accepted / runtime_pending**。仅复核v2的xUnit2012修复，未运行SDK、测试或DB，未修改源码。

实读v2/v3完整树均175文件，无新增/删除；唯一变化为 `TP07TemporalMappingTests.cs` 将 `Assert.False(libraries.EnumerateObject().Any(predicate))` 改为 `Assert.DoesNotContain(libraries.EnumerateObject(), predicate)`。predicate完整相同，仍以OrdinalIgnoreCase拒绝`DM.DmProvider/`；对该JSON library集合含义等价。没有删除候选包/EF身份断言，没有analyzer suppression或产品变化。

| v3输入 | SHA256 |
| --- | --- |
| `TP07TemporalMappingTests.cs` | `926552e9f8ed9237cf5b51ef80fec7bd77afc88d79ed54b29f8b7dd30ed6edcc` |
| `tools/DownstreamAcceptance/TP07/patches/mapping-manifest.json` | `e24a4ca85191c059aeb89acfc42e6263aeee3fb4c5ab00e8ee20757a63181f4d` |
| 同目录 `temporal-mapping.patch` | `6ce2beb489990798d6241376f561e8975e0efb198aae6c41b2dfafd95a18ef87` |

完整175项after hash与manifest相同；另外174文件包括产品、项目配置与锁均未变。Low可在新执行闭包逐项确认实际依赖/锁/缓存包哈希与原49项证明一致后复用该证明，不为此断言重跑force-evaluate。若执行图发生其它变化则不得直接继承。

v2源码及xUnit2012失败保留；原R01/R02静态闭环继续有效。新构建、目标测试与后续EF运行门仍pending，不能将本次复核计为测试通过。完成后停止写入。
