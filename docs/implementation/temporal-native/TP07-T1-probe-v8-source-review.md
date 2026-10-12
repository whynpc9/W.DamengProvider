# TP07 T1 probe v8 源码验收

2026-10-11，Astra High。结论：**source_accepted**，无阻塞项或最小修复要求。冻结两文件符合设计 `TP07-T1-probe-v8-native-lock-design.md`（SHA256 `afd8cc08a49318d83bbcfbe248a00570b3e8e992f600c4b4d389ad585755d586`）。本结论仅接受源码进入独立验证门，不表示 planned71、SDK/build28、readiness 或 runtime4 已通过，不签发执行批准。

本轮只读完整实际 diff、源码、历史 Ref 与真实文件，以标准库进行 AST/hash/JSON 比较；未导入项目模块、执行 validator/tests/SDK/DB。唯一新增文件为本文，未修改实现、设计、旧证据、approval 或 status。

## 冻结身份与范围

工具目录为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe`；下列 SHA256 均独立重算一致。aggregate 仍为排序相对路径的 `SHA256 + two spaces + path + newline`。

| 对象 | SHA256 |
| --- | --- |
| validate.py | `9c76b716e6b5912b1f8aeb8cf423f84d7823f3e44500adde501604c9f2ba9657` |
| test_contracts.py | `f93d361b513a53e5b5468c738daae16e8bcde16b5849c8f13a6da3512905a31a` |
| actual17 aggregate | `7847e8163c6d1dba78980438bbb4df296d6e1f648d8f4fcd173b1f7162c2cc7b` |
| producer15 aggregate | `d4c64e9034c804b92a896ab0c352c0379979ee3f1674e8f7864755a1ba0b4c3d` |
| host9 aggregate | `0f99c744ac71e3c099a219399068a53e66117a5998812339b986c4551d434d40` |
| implementation-handoff/freeze-report.json | `61523740d73544d4dacd76d16e0ecb04e467b4a31e597298d6dd263b6d5e81a4` |
| implementation-handoff/two-file.patch | `beea8d3f999f761031f42f19bf1d6068950d06f5239a22d5049b80415b6c9b27` |

交接完整前缀为 `.local/temporal-native/verification/TP07-T1-probe-v8-implementation-handoff`。实际 prechange17 逐文件匹配 v7 冻结 map；当前恰 validate/test 两文件不同，其余15字节一致。README、prepare、run、policy、全部host9保持，producer15排除validate/test，原V4/V5/V6/V7 pin assignments及fixture helper均不变，无自指或无必要版本pin扩展。

## 唯一生产改动

独立逐字比较确认，整个 validator 只有一行从统一期望 `2` 改为 `{PROJECTS[0]:2,PROJECTS[1]:1}[g['project']]`。此前 raw 和 native 合同已严格验证固定两项目及顺序，未知、重复或交换身份不能进入任意字典选择。既有 `exact` 首先检查精确类型，bool/float/string不能冒充整数。真实 provider lock SHA `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4` 为 int2，host lock SHA `69c5a714efcac8afe2f046aa65e82e334a05074c63a0bcd4ad45c64147e40dde` 为 int1，本轮重新读回匹配。

锁 exactshape、真实 replay 路径/Ref/hash、完整依赖图、raw/pruning/frameworks、package/contentHash/hash-domain、process、binary/MVID/runtime集合等剩余 validator 字节全部不变。`.nupkg.metadata.version` 仍严格int2。不存在通用版本集合或依据未可信actual自行生成期望。

## 新测试的真实边界

原66个直接test方法的AST完全一致；所有原有函数（含nested）、类和顶层assignment AST也一致。新增两个顶层helper `actual_v7_complete_native_fixture`、`full_native_with_verified_loaded_lock_mutations`，及 `ActualCompleteNativeLockContracts` 的以下五方法，实际声明总数为71，尚未执行：

- `test_real_v7_full_native_contract_reaches_package_binary_and_runtime_gates`
- `test_full_native_same_refs_reject_swapped_versions_and_wrong_types`
- `test_full_native_same_refs_lock_shape_and_dependency_semantics`
- `test_full_native_fixed_project_identity_order_and_cross_lock_path`
- `test_controlled_live_lock_hash_symlink_and_escape_use_physical_checks`

真实fixture有限绑定v7 inputs/source/native/build/startup五个固定Ref，从真实workspace读取，检查跨stage关系并逐文件验证source187；本轮独立重算五Ref及187文件一致，并核对交接另外四个历史Ref。没有ROOT重写、producer oracle、虚构SDK receipt或假native完整正例。

完整正例直接调用真实 `V.native_contract`；四个mock均使用 `wraps=原函数`，仍执行原hash-domain、archive-origin、binary、runtime代码，再分别断言28/28/5/1次调用。W的独立MVID路径仍由完整native原函数执行。计数是待运行断言，不是本轮通过证据。

语义adapter先以原 `read_ref` 检查实际Ref/字节并解析，再核Ref仍完全等于原lock Ref，仅深复制并修改加载的lock对象；其他读路径均走原函数。版本负例覆盖各项目错版本、both1/both2/swapped、0/3/-1、bool、float、string、null，并精确要求 `native_lock_version` 或其类型错误；shape与依赖负例分别要求原 `actual_lock_shape` / `actual_lock_graph`，含W缺失、额外包、contentHash/resolved变化及host Project依赖变化。不会以整文件SHA早拒绝冒充语义覆盖。

未知/倒序/重复project与cross-lock均调用完整native，在原raw固定身份/真实lock路径门拒绝。物理负例使用独立临时受控raw fixture，不使用内存adapter：实际字节漂移、缺文件、symlink和escape分别由原hash/存在性/symlink/root/path合同拒绝。此fixture明确是synthetic raw边界，测试未声称其为真实SDK或完整native成功，也不修改历史锁文件。

## 证据边界与后续门

历史readiness失败Ref `f0baed9410e60a994d0272ad26ddd69a98cb13156647416cc06221b498206082` 保持，已生成readiness `45d6192367a088e134d8ef7baa6aec582c5bf0712f7bedb1fc075d1c191dd13c` 不重新标为接受。原v7 compiled28/337及完整offline合同通过仍仅是原链历史；新真实fixture正例的作用是有限合同兼容性，不是新readiness或runtime授权。

root可在新静态17/host9 pack及inputs绑定后另授Low执行完整71精确集合；随后新source187→native→locked build→compiled28/offline→真实Ref下独立完整native/offline/readiness，失败即停。旧66、原AST不变或旧28通过不能替代新执行。runtime4另行批准。

产品178、W包、产品824及原源码/证据保持，不需重跑产品824。CLI13、process/DEVNULL/180+2、十字段SDK receipt、snapshot字段与覆盖边界、TLS/证书/保存点均未变化。本文完成后停写。
