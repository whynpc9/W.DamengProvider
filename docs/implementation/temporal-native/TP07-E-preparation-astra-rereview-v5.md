# TP07 E preparation-v5：v11 E9引用及inputs窄修复核

2026-10-10。结论：**reference_only_preparation_source_accepted / SDK_DB_not_authorized_by_this_review**。脚本、新inputs及最终README不存在阻塞。本轮仅只读比较与hash核对，没有执行prepare、freeze/source副本、复制、SDK、测试、validator或DB；只写本文。

| 输入 | SHA256 |
| --- | --- |
| preparation-v5/prepare-and-build.py | `c40f7f77cd0bd82fe3f4333ca74a1437908bacebdbae6c2f62bfcddf901af843` |
| preparation-v5/README.md（最终修正文案） | `e330ddc545cfbab78eabc605dff8454c71c84258fb095e14dda91e4c20702daa` |
| preparation-v5/schema-draft.json | `a85465269f2acc10c0d13bc1fad79636cdc1189f6fa08d5159a0f6c1dc463d34` |
| root-approvals/TP07-E-combined-v5-inputs/inputs.json | `97e02cf047f732dadea249e8b967e1e20d1b1b968689ae68ef83b47bf9bbe319` |

已接受v4脚本当前SHA仍为`2c3cc3e379338bbdca9f6f1e07d4ff722e421f34ec687237494507c8d52cc07a`。v5仅第1/20/28三行变化：doc标签、PIN.e_source切到v11 E9 manifest、FOLDERS.e_templates_root切到同v11 snapshot。其余字节相同；191/193源数量、native13 graph、原L0/mapping/W/cache/feed/SDK/helper refs及所有source/lock/build算法、命令和守卫完全继承，不增加运行lane或能力。

新inputs对v4 inputs只改变`references.e_source`和`folders.e_templates_root`；仍恰6 refs/5 folders，dotnet及其他内容相同。本轮六项引用的实际文件SHA全部匹配。E source实际为`TP07-E-tools-v11/E-source-manifest.json` SHA`deb47b37037cb588785e87f7bff83984fe80facf29cb08ca9abf62e46c9b0a9a`，count/files均9、唯一相对路径map与当前已接受policy E9 pins一致，v11 snapshot九文件逐项实读匹配；按声明格式重算aggregate为`d3e2227ed642e40d420ff0791dded586fb646ef26c6bb4c5faaad80bf9d41a43`，与manifest/policy一致。

原目录`TP07-E-preparation-v5/prepare-and-build.py`绝对路径parents[4]实际等于workspace。只能使用该原路径作后续执行入口；freeze/source副本仅作封存，不适合ROOT推导，不能执行副本或放宽守卫。新fresh输出示例指向combined-v5各阶段，不复用旧输出。

初审发现的README旧v10/Program-only/26项标签已由High在root限定范围内修正并停写。本轮重新实读最终README `e330ddc5…`：标题/输入为v11，末段明确相对v10仅SixPrecisionCases改变、Program与15/25分窗不变、28项合同待Low实际执行。script `c40f7f77…`及schema `a8546526…`重新hash仍原值；没有随文案修正改变实现或inputs。本reviewer没有修改README。

本次准备源接受不是SDK或DB批准，也不改变四文件源码报告`397e0776…`的实际验收边界。root可单独安排Low28项Python合同；新prepare/lock/build仍需root对应窗口，随后同新compiled host的partition338/25和GeneratedCreate28及新12-ref runtime readiness完整通过。不能借旧SDK/host/gate结果接受修改后的SixPrecisionCases，不回写旧失败或status。
