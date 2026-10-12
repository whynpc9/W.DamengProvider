# TP07 D-v4：checkpoint manifest路径定点复核

2026-10-09。结论：**D_v4_CSharp_path_fix_scoped_static_accepted / Python_and_combined_artifact_gate_pending / runtime_not_authorized**。仅只读D-v4相对Low D-v3快照的一行差异及相关保留检查，未执行SDK、构建、测试或DB，未改实现、旧manifest或其它owner文件。

依据 [D外部准备设计](TP07-D-external-preparation-design.md)（SHA256 `3da136b0efd3f4a1aaa5c1e33ca0a21ac22cf90cf0f055fee8c8725d6f12f1df`），C# `ScaffoldCases.ContinuationInputs` 的有限变化集合新增了且仅新增 `source_manifest_path`、`lock_manifest_path` 两个名字；已有对应SHA字段本就允许变更。现在合法G6/G12 child可以指向新的不可变source/lock manifest文件，不再为了满足旧路径相等被迫覆盖parent文件。

完整diff没有改变其它检查：父build/report hash、同一replay root、6/12个真实generated文件及其closure、原lineage/EF provider、所有base source/lock/assets条目及原D/L0 host身份仍保留；新增内容仍限制在批准generated subtree，只有generated consumer可更新。没有修改SQL、参数值、CLI参数、生成路径/顺序、guard gate、cleanup或产品行为。

这项C#改动只解除错误的“路径必须相等”约束，**不独自构成新ref来源验证**。正在修复的Python必须对同一两个path/SHA字段作配套处理，实读child文档hash、当前root、相同source_pack、精确manifest集合和保留base闭包；旧parent文件保持不可变。该Python/外部准备部分仍由独立组合审阅接受，本文没有提前关闭它。

## 文件身份

实读十源与Low `TP07-D-v3`逐项比较，只有以下文件不同，其余九项字节一致（包括GuardedMigrationCases `a2c5b8e…`）：

| 文件 | D-v4 SHA256 |
| --- | --- |
| TP07TemporalDesign/ScaffoldCases.cs | `0195d46f76aa468992e2d81519780aabe5a49c7f5b968b39c9fa9e0a2c0c99b7` |

按repo-relative排序独立重算十源aggregate为 `526d70c49e832104c3ea72b5bd6ac441030210790a899dc77c321b2548f9028f`，与root交接一致。收尾时Low D-v4 snapshot已完成；实读 `TP07-D-v4/source-manifest.json` SHA256为 `85a1f1939418aac83cc8c2e99b3b0d9a158db23ee475d2b8c084465301f34457`，十个snapshot及live文件均逐项匹配，aggregate一致。

[D-v3收据静态接受](TP07-D-astra-rereview-v3.md)及其运行边界继承。当前E清理/真实接受门尚未解除，D runtime继续暂停；外部D准备代码可按既有授权继续编写，但本文不执行它，也不授权SDK/DB或绕过E/L0模型门。首次D运行前必须使用完成C#/Python配套后的新十源与25工具包freeze，不能让checkpoint跨包拼接。报告完成后停止写入。
