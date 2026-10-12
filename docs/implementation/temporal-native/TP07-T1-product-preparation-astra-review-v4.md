# TP07 T1 preparation-v4 固定来源及有限scope守卫审阅

2026-10-11。结论：**preparation_v4_source_accepted / exact_new_scope_guard_verified / new_source_native_build_retry49_transaction25_full824_pending**。新产品v3、独立freeze及root七字段批准正确绑定，执行差异仅限新scope的严格字串比较。未发现源码阻塞，最小修复范围：无。本报告只接受三份准备工具源码，不授予SDK、tests、probe或runtime窗口。

本轮只读冻结v3/v4三文件及固定来源引用，解析AST/JSON、计算hash、逐项核对178源，没有import或exec项目工具、运行SDK/tests/validator、执行程序集、DB或secret wrapper。没有读取另一owner正在修改的tracked probe实现；唯一新增文件为本文，旧源、失败和状态均未写入。

## 冻结身份

原执行入口为 `.local/temporal-native/verification/TP07-T1-product-preparation-v4/prepare-unit.py`；实际 `parents[4]` 仍等于workspace。freeze/source复制件不是入口，ROOT计算未改。

| 文件 | 实际SHA256 |
| --- | --- |
| `prepare-unit.py` | `941c52cf78348afb6627eee3f089b556a3646cb249fbd1cda48490678a83460f` |
| `README.md` | `7d6a00677c73f4f1de4a8b9b75aa59d2f647153f3907c57d601e85fa47ea4c61` |
| `schema-draft.json` | `c55a2800fa3b0b90b387f03c2970cb250e72bdcec490af5086fa0a7e2558d430` |

基线为已审preparation-v3脚本 `8a0d1a4913ecc34d272515a6178b7a6714dc8b290ca00091e7d44d0a88e9ba22`。实际文本差异恰为模块docstring、五个新产品refs、provider-lock路径、product_source folder及 `stage_main` 一处scope字串。schema只有task说明revision3→4和source_approval/scope两个叶子变化，其余键、值和结构相同。

## 新来源及七字段批准

本 reviewer 以AST中有限字面值、常量引用、字符串拼接及固定rsplit路径表达式解析PINS；未加载脚本或identity helper。全部12条PINS和四条RAW_BASELINES实际SHA均匹配。

| 新引用 | 实际SHA256 |
| --- | --- |
| product manifest v3 | `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8` |
| 完整五路径patch v3 | `d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f` |
| root source-only approval v3 | `66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f` |
| [产品源码审阅v3](TP07-T1-product-source-review-v3.md) | `d0b194b811e278de29292d7a603e97378ca78813868ec604ea6b628200543ccd` |
| retry-sourcefreeze manifest | `0a74c7abb4f233d065c46c41e3bda8ee574b4e212bfcd5f420ddddf947f88f05` |

product_source实际指向 `TP07-T1-product-v3-retry-sourcefreeze/ef-source`。独立枚举恰178文件，每项hash同时等于产品manifest及Low freeze map，声明格式aggregate重算为 `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c`。provider锁仅换目录，仍 `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`；unit锁仍 `3e4b192f701f7b8579681a112c55b68346a6b2da0d19f56bda4fa021f60967fd`。

实际批准恰七字段：schema_version1、task `TP07-T1-overlay-source-approval`、status `source_approved`、scope `transaction_capability_and_w_retry_contract_tests_only`、新manifest精确path/hash、exact_count178及完整排序exact_paths；与源码守卫逐项相符。产品patch引用也与PINS一致。

新守卫只接受上述单一scope，未保留旧 `transaction_capability_overlay_only` 的OR/兼容分支，也未接受任意批准。七字段形状、状态、manifest和actual路径/数量仍同时核对。source-only不以单位已通过为前置，依赖不成环；新批准不代表新SDK/tests/runtime批准，也没有复用旧 `d6cff0a1…`。

## 有限执行差异及未变合同

独立按源码位置逐个比较33个函数（含nested及重复函数名）：仅 `stage_main` 的上述一个字串不同，替换该字串后整个函数源体逐字相同；其余32函数源体及AST均相同。process函数源体独立SHA为 `20244568912c324ae1ad817e999b6b099ffbd04e31242622664e8fd0677fa5c5`，与基线相同。

对完整AST仅排除模块docstring、PINS/FOLDERS两个赋值，并把两个版本各自唯一scope literal规范化为同一标记，余下全部节点相同，包含class、其余顶层语句和入口。该比较确实处理了一个真实执行字串差异，不能描述成pure pin-only或全部执行AST本来就相同。初次独立读取器按函数名去重及不支持固定rsplit时分别停在静态断言/常量解析，改为按源码位置匹配及有限常量展开后完成核验；没有运行目标脚本或修改输入。

保持的原合同包括：12refs、3folders、6个输入顶层字段；178实际源与25事务planned守卫；source/native/build/targeted-test/full-test五阶段、0/2/3/2/2事件数；targeted固定 `FullyQualifiedName~DamengRelationalTransactionCapabilityTests`、正式25条TRX；full无filter、同一已审build、前阶段input/tool/product identity严格匹配。阶段manifest仍schema2，source/input批准仍schema1，旧阶段输出不能据相同schema跨新lineage拼接。

原process实际PID/PGID持久化、SIGINT/SIGTERM/BaseException/finally组检查、handler恢复、300秒执行deadline与最多2秒失败reap全部未变。原单节点FLAGS及环境白名单不变。原raw4全对象/JSON类型比较、有限work/cache/config/feed映射、完整272项pruning、49非W加W加project=51记录、50档案、新SDK metadata.contentHash与ZIP SHA512分别核对、前后锁/hash守卫也未变；未删除或泛化图字段。

原8项process-control只保留为不变算法的历史有界执行/取消实证，不要求重做300秒实验；其中probe诊断1秒不能冒充生产180秒接受。这些历史收据不构成本轮新产品source/native/build/units接受，本reviewer未重新运行它们。

## 后续独立门与接受边界

README明确retry49、事务25、full824仍是静态planned，旧full795/783/12失败及旧target25通过保持历史身份。五阶段中targeted仍仅事务25，没有偷偷改filter或加入第六phase。另行retry49执行需要root明确命令、同一新build身份及Low有界运行/独立证据；README关于复用process函数的说明不是直接执行授权。

full通用函数仍只检查全部实际结果Passed且count>25。因本次准备工具未扩算法，**完整824的精确集合必须由Low独立回读作为额外接受门**：原795经声明的改名/合同映射逐条保留，再加29新case；不能仅靠count>25、exit0或旧783passed接受full。49只覆盖同步public classification，产品源码审阅已明确ExecuteAsync与真实transport/COMMIT运行未覆盖。

Low新三文件snapshot后，root可签本工具新inputs并分别开放source→native→locked build→retry49→事务25→同build无filter full824窗口。每个阶段保持自然退出、真实TRX/实际DLL/deps/W身份和保护项回读；首失败保留并释放，不能自修或重复执行。8项历史控制、旧raw/锁图仅为固定基线，不替代新执行。

probe兼容工具、其后编译28/runtime、L0/E/D改绑均不在本报告范围。当前源码最小进一步修复：无。报告完成后停写。
