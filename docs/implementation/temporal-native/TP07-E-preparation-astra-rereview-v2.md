# TP07 E preparation-v2 两项来源更新复核

2026-10-09。结论：**scoped_static_accepted / actual_prepare_lock_build_and_28_model_gate_pending / runtime_not_authorized**。仅只读v2/v1完整diff、schema/README及Low三文件freeze，未执行脚本、SDK、测试或DB，未修改实现或旧证据。

相对已接受v1脚本，实际代码恰两行变化：`PIN.e_source`改为E-v4 manifest路径及SHA `0d469ad85460aa090532f443c11478cc565471204db6694a7ff4448349873ea9`；`FOLDERS.e_templates_root`改为E-v4固定快照目录。九份目标snapshot已实读逐项匹配，aggregate为 `4f102cedfe0e4ae27b3d3dc764e253939537e0a3443a7668efed4e9617ddb3ef`。其它输入、helper、环境、依赖、阶段命令和收据生成逻辑不变。

README/schema仅更新版本/来源说明，并明确新build之后另执行真实E offline GeneratedCreate：shared/TLS各14项，共28项。该门不由preparation脚本自动执行，不用wrapper/测试连接环境；需绑定新startup path/SHA及实际编译候选，保留自然exit和零IO证据。runtime或live DATA_LENGTH接受仍false，不生成runtime approval。原191→193源、三active locks/assets、lock三条/build五条命令及同provider双host合同继承 [v1静态接受](TP07-E-preparation-astra-review-v1.md)。

## 固定身份

| preparation-v2文件 | SHA256 |
| --- | --- |
| prepare-and-build.py | `ca038e1da1cb21b7badc3384b43cd24195bcea5e695ff78080f72b939513b9a5` |
| README.md | `38b47e0c5f0d610db7b4c92f207169963574a0e2c4e8a7ffbf3a3c3c99e0a2aa` |
| schema-draft.json | `241ddefd2181a68ba131e5cea1b4762931e210012baef069096d6f2d9256656c` |

Low `TP07-E-preparation-v2-freeze/source-manifest.json`实读SHA256 `c7f80ee135e854b79904f7de9d96a537f1d49209a85de9eec7ab70dca4ccac93`，三个original/snapshot匹配。E-v4源码自身已有 [b0bd…静态复核](TP07-E-astra-rereview-v4.md)，本文只接受其外部准备来源切换，不重复授予执行/运行结果。

Low仍应调用原批准v2脚本路径并先核hash；freeze/source多一层不能直接作为该脚本的ROOT推导执行入口。README例子保留的combined-v1输出目录当前已存在，执行者必须使用新的互不重叠v2阶段目录；原fresh-output守卫会拒绝旧目录，不能删除或复用旧证据。这是调用路径注意事项，不需要扩大两行代码修复。

root可安排新prepare/lock窗口，实际锁回读后再build，随后独立执行28项compiled gate。当前28项尚未运行；先前遗留对象的实际恢复与独立确认由另一证据链关闭，不由本报告推出。E runtime仍需新身份/离线门及单独批准；不接受完整C或D激活。报告完成后停止写入。
