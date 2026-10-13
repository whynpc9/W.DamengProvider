# TP05 R01 定点复核 v2

2026-10-08。结论：**R01 closed / static_review_accepted / runtime_validation_pending**。本轮仅复核 [v1 报告](TP05-astra-review-v1.md) 的唯一编译阻塞；未运行 build、tests、validator 或 DB，未修改产品/测试。

`tests/W.DmProvider.TypeTests/TemporalNativeReaderContractTests.cs` 实读 SHA256 为 `30ebb1f0f22b9d1379e86921f35eee4e41b292ed9782ae035f8ce77dbe6f9aa1`。DATE Wire 分支现为 `new(2026, 7, 23, 0, 0, 0, 0)`，明确提供 Hour/Minute/Second/NanosecondsWithinSecond 的零值，与现有七个必填字段的 `DmTemporalFields` 构造合同一致。

新表达式仅出现一次；只将该表达式替换回旧三参数形式后，实算完整文件 SHA256 精确还原 v1 的 `949198126a1f670a2fdfe988b48bae361631e3b360342f0ffd2df9850738ba48`。因此其余测试、断言、fixture、case 数均未变化，不涉及 pure core 签名。

`TP05-reader-coverage.json` 实读仍为 `1383ab8736b69e948fbbab2f19b50be10c030f6d1525ac3618d7442adb398802`；其仅 pin 原矩阵，无需为本次构造修复改写。355/10 是静态计划，99格及24 overlays 仍 not_run，原 v1 证据保留。

v1 其余关键静态审阅结论继续适用。可交 root/Low 统一 v2 冻结候选执行所安排的九门；本报告不等于编译通过、TP05 运行接受或完整时间验收。完成后停止写入。
