# TP07 E preparation-v3：E-v5来源定点复核

2026-10-09。结论：**reference_only_scoped_static_accepted / actual_new_build_and_runtime_pending**。仅只读v3相对已接受v2的完整diff和Low三文件快照，未执行脚本、SDK、测试、validator或DB，未修改实现或历史证据。

脚本实际只改两行：E source manifest固定到 `TP07-E-v5/source-manifest.json` SHA `765f9384ba74f633a4d1859b324fa636424c383cd30f5a76390a1bd6e69c9a9d`，template root固定到同一E-v5快照。九模板实读匹配，aggregate为 `aab41c11ebffe5a7ecffbe3ced78fbfd0a786680f4ea6795bd2407d7e889d488`。其它pins、helper、source/lock/build阶段及环境/身份/失败守卫逐字未改。

README/schema只更新来源/版本和17项有限合同说明，保留实际compiled DDL门shared/TLS各14、合计28；不把源码数量当执行结果，不自动执行selftest或产生runtime批准。原191→193源、13包native图证明、三active locks/assets、lock三条/build五条命令和两host同provider合同继续继承 [preparation-v1](TP07-E-preparation-astra-review-v1.md) 与 [v2定点复核](TP07-E-preparation-astra-rereview-v2.md)。新锁/build实际结果仍由Low独立证明，不由“依赖未变”推成已通过。

| preparation-v3文件 | SHA256 |
| --- | --- |
| prepare-and-build.py | `616494d94c1ca0b58cb9e87a10e0eb9adbc6da1cd3dd05ad657c421a99cdf0ee` |
| README.md | `fa10dfd196906012cd605df6184d57b714a06805c965722ddb4d4cbdc41f98b9` |
| schema-draft.json | `e02bf418f30d35ade8bd751519a291ccb50c160fece2fb8336cd249cfb26b08f` |

Low `TP07-E-preparation-v3-freeze/source-manifest.json`实读SHA256 `5eb0b00c68413e156d32f41b3e3eae98bf91856316a66c0f1613ab92ff394861`，三个snapshot与original均匹配。原v2 `ca038…/c7f80…`保留。调用仍使用批准的原v3脚本路径；freeze/source多一层不适合作为其ROOT推导入口。README旧combined-v1输出示例不能直接复用已有目录，Low须选择全新且不重叠的阶段输出，fresh-output守卫不放宽。

[E-v5 interval源码接受](TP07-E-astra-rereview-v5.md)及Low的17项离线结果属于各自证据链，本文未重跑；新的实际interval server_text仍待批准后的真实运行，不能借TP06样本填入旧失败。准备完成后的compiled DDL28、受影响E实库与最终E/D/C门按root独立窗口执行，旧60项roundtrip、失败six及清理收据保持原cohort。报告完成后停止写入。
