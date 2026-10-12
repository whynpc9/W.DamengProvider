# TP07 T1 product preparation-v3固定来源修订审查

2026-10-11。结论：**preparation_v3_source_accepted / pin_only_revision_verified / new_source_native_build_target25_full_pending**。新产品 v2 的正式 source-only 批准、178 源及独立 unitfix freeze 已正确绑定；执行算法与已接受 preparation-v2 一致。未发现源码阻塞，最小修复范围：无。此结论只接受准备工具源码，不授权运行任何阶段。

本轮在 root 正式冻结、High 停写后，只读比较新旧三文件，独立解析 AST、计算文件与178源 hash、核对固定引用和批准字段。未 import/exec 准备工具或 identity helper，未运行 SDK、tests、validator、wrapper、DB、取消实验或 source/native/build 阶段；仅新增本文，不修改工具、产品、status、旧证据或外部 EF checkout。

## 冻结身份

原入口为 `.local/temporal-native/verification/TP07-T1-product-preparation-v3/prepare-unit.py`；其 `parents[4]` 实际为本 workspace。独立 freeze 副本不是入口。

| v3 当前文件 | 完整 SHA256 |
| --- | --- |
| prepare-unit.py | `8a0d1a4913ecc34d272515a6178b7a6714dc8b290ca00091e7d44d0a88e9ba22` |
| README.md | `bc634df9b891e6132366f72927be1088b13c70365529b7a56338d735296a042e` |
| schema-draft.json | `62b359e47f1566916dcec4f88979c98d193eba31637786d72aa9f9f50a375d43` |

本轮重算的 v2 三文件仍依次为 `1b4b3e780eb55757b870b94ab5d9f5bb7a329e4ad898f1236aace6fb5280e0cd`、`0f0eb6358751419653435544dcedb590c17e91714a283b0b5bce454d26001428`、`3da2689d7b7586fbbbed335d6419937191f939763a45d2f2a5ec22a4673615e0`，与已接受 v2 审查记录相同。

## 新来源与批准链

本轮对脚本全部12条 PINS 和四条 RAW_BASELINES 实际文件重新计算 SHA，全部匹配。变动限于五个新产品引用、provider lock 的新冻结位置及 product_source folder：

| 固定引用 | 当前 SHA256 |
| --- | --- |
| transaction-capability-manifest.v2.json | `fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432` |
| transaction-capability.v2.patch | `5df9c73a4879fbf7f2cb79da48379a92856e093fecea7df3c31956080b1f3566` |
| root-approvals/TP07-T1-product-source-v2/source-approval.json | `d6cff0a1a92a1b8341b12c6dfaa352028039d8908fffd1b1387254561c3f0486` |
| TP07-T1-product-source-review-v2.md | `eb47339baa20c4bbedf2be7ef644b3b0c2a343a0f00d191745cc24e2e53ebbf5` |
| TP07-T1-product-v2-unitfix-sourcefreeze/source-manifest.json | `a842630f569b6dfbeebaa464133ab4bc17cf42f2166691be337b7ac9483f1236` |

product_source 精确指向 `TP07-T1-product-v2-unitfix-sourcefreeze/ef-source`，没有误用旧 `TP07-T1-product-v2-sourcefreeze` 或旧 v1。独立枚举实际目录恰178文件，每项 hash 同时等于新 product manifest 和新 Low freeze map；按声明排序格式重算 aggregate 为 `a0c14e430c72ede7deb257fee16e175b9d06b1862abec59f417aedc7bd6f8724`，三方一致。provider lock 仅换位置，仍为 `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`；unit lock 仍 `3e4b192f701f7b8579681a112c55b68346a6b2da0d19f56bda4fa021f60967fd`。

新 root approval 实际恰七字段，schema1、`source_approved`、`transaction_capability_overlay_only`、manifest path/hash、exact_count178 和完整排序 exact_paths 均符合未改动守卫；manifest 的 transaction patch 引用也等于新 PINS。source-only 批准不以尚未执行的 units 为前置，依赖不成环；该批准不授予 SDK/tests/probe/runtime 权限。旧 ac8 批准没有被当作新产品批准。

## 执行算法与有限合同未变

独立比较确认：工具文本仅改模块 docstring、PINS 和 FOLDERS。排除这三个顶层节点后的完整 AST 逐节点相同，规范化 AST SHA256 为 `71ec630cf55203074a5231886c2cc81d33855bcabadf43f3efabe14353473ee6`。全部33个函数（包括嵌套函数）AST 及源函数字符串均逐字相同；类、其余顶层语句及入口也包含于完整比较。schema JSON 只修改 task 说明中的 revision2→revision3，任何契约字段均未改。

因此原12 refs/3 folders/6个输入顶层字段、178实际源守卫、五阶段 source/native/build/targeted-test/full-test 与0/2/3/2/2事件数保持。manifest schema 仍2，输入和 source approval schema 仍1；虽然没有改变 manifest schema，前阶段必须同时匹配本次 input SHA、新 product manifest 和当前 tool SHA，不能接入旧 v2 阶段成功结果。

取消路径保持真实 PID/PGID 持久化、SIGINT/SIGTERM/KeyboardInterrupt/BaseException 处理、finally 整组检查、handlers 恢复、300秒执行 deadline 和最多2秒失败 reap；unknown exit/group 不通过，实际 elapsed 不扩预算。原四份 raw baseline 全对象/JSON类型比较、原生 assets/dgspec 关系、完整272项 pruning、有限 work/cache/config/feed 映射不变。49 non-W+W+lowercase Project=51、50档案、新 SDK metadata.contentHash 与独立 ZIP SHA512 sidecar 的区分不变；未放宽锁、包、边或新图生成要求。

targeted 门仍要求固定类实际25条自然 Passed TRX、无 skip/failure/abort；full 门仍要求同一次已审 build、重读 targeted TRX、无 filter 且完整 Passed counters，不能用 exit0 或历史单位结果替代。本次来源改绑不需要扩大算法或重新设计这些已接受门。

## 历史证据与下一阶段

已记录的 `TP07-T1-process-control-v1/control` 八项通过仅保留为未变算法的历史有界执行/取消实证；本审没有重新运行或重标这些收据。既有 unit 300秒实际 deadline 与 probe 1秒 diagnostic 的适用范围保持，后者不是生产180秒接受。因所有执行函数和其余可执行 AST 未变，本 pin-only 修订不要求重复300秒取消实验；这些历史控制也不构成本轮新产品 source/native/build/units 验收。

旧 `TP07-T1-product-v2-build` 首次编译失败和 partial provider 继续保持历史失败/不完整产物身份，README 已明确不能据其接受新产品 build。新源变更来自已审 unit 修正，后续仍须新 source→native→locked build→targeted25→同 build full 的实际证据；旧 native/cache/锁/raw 图只作固定比较基线，不能拼接为本次执行结果。

root 可在 Low 新三文件静态快照与回读后，分别安排新阶段的独占窗口；本报告没有开启这些窗口。新编译、targeted25/full units、probe 编译28与实库、后续 L0/E/D 及运行接受仍 pending。当前无新增源码修复任务，报告完成后停写。
