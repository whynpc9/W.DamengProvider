# TP07 T1 probe v6 六文件源码验收

2026-10-11，Astra High。结论：**source_accepted**，未发现阻塞项，当前没有最小修复要求。接受仅针对以下冻结字节与设计符合性；不表示 Python66、SDK build、compiled28、readiness 或 runtime4 已执行/通过。本轮只新增本文，没有导入项目模块、运行测试/SDK/probe/DB，未修改实现、设计或旧证据。

设计基线为 `TP07-T1-probe-v6-build-failure-design.md`，SHA256 `648c3441a2de4f63deb0c1c6d4347d7a9ef5b07764046a65066b1c580c79698c`。审查包含完整新增 helper/修改函数/新增11测试、实际六文件差异、AST保留关系、真实文件hash、历史raw完整JSON关系及下游消费；标准库静态解析不等于执行待验工具。

## 冻结身份

工具根为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe`。本轮重新计算并匹配所有17文件、producer15、host9及交接：

| 对象 | SHA256 |
| --- | --- |
| prepare.py | `dd81f63fdee26fa4e50d3515e906bb1b321c0d5cc73712c1c94a989ab908167c` |
| validate.py | `66b9f7fb62651dfe012a3652be8fc6f73d14a4528ab56d2c5307743236a741c0` |
| test_contracts.py | `d8927157bca2d9e697244d2b5dee83969f99b4cada77a7bbd53c5cd21918d921` |
| README.md | `74d6964e92a09cd3ab0ffbe013318840b089a79e62388629abe249029a3cb9d0` |
| host/ProbeContext.cs | `5eb5621b2ea6c1ba6d11266a8226d1060f111ec7adb6813afe61d22c61a4bdcc` |
| host/Program.cs | `0ab8ad1210423b3f5bd3db753fbfdea61d915caeb2bb5bf4758d92f21dd49303` |
| actual17 aggregate | `78de8a0afbcbbd9d6ee249c12ee0823b616d2ae0f01cded648980ec9536ff897` |
| producer15 aggregate | `134967c9191728e971fdee8453c9a5af8466a868dbcd6fb17033796f50d08f12` |
| host9 aggregate | `999b50d6a47bf4a36c2d03b45e709030ccd814a426ed3400b64dd7fd2afe4bba` |
| v6 implementation-handoff/freeze-report.json | `c013ce0341325498362a29facfdb5cff54e95144379488f0615f1dd463f2ae19` |
| v6 implementation-handoff/six-file.patch | `4ece90cdc79fd47baf9242df24f755db8121d6541237d672b2a991a99dc89468` |
| v6 prechange/prechange-manifest.json | `cdd02117dbe3c87c5038ae8c0be4f8c8e31069da6f31e7e4fc4925220196cbbf` |

交接/前置目录完整前缀为 `.local/temporal-native/verification/TP07-T1-probe-v6-implementation-handoff` 和 `TP07-T1-probe-v6-prechange`。aggregate均为排序相对文件名的 `SHA256 + two spaces + path + newline` UTF-8字节SHA256；producer15排除validate/test，host9保留host/前缀。

v6 prechange与实际不可变 `TP07-T1-probe-tools-v5/source/tools/DownstreamAcceptance/TP07/transaction-capability-probe` 的17文件逐项等于v5冻结map。本轮只有上述六文件变化，其余11字节不变。

## 窄修边界与AST

两host文件的全文差异已用精确字符串替换关系独立验证：ProbeContext只有内部 `Entry=>entry` 改为 `OwnedEntry=>entry` 和同文件三个 `p.Entry.` getter引用改名；Program只有两处 `CandidateGuard.Identity(transaction)` 改成 `CandidateGuard.Identity(transaction!)`。Rows、模型、Step/observer实际值、nullable/analyzer/NoWarn、事务/保存点、异常路径与其它suppression均不变。前置成功Begin赋值、失败throw中止后续Step的既有控制流支持这两处标注；编译是否成功仍需新SDK实际确认。

原有顶层函数AST变化恰为 prepare.native/build/readiness、validate.approval_contract/native_contract、test.closed_current_toolpack_fixture。其余原函数AST不变，尤其process、CLI/main、source产品契约、offline、run相关消费者及validate.snapshot_contract。TOP仅build字符串增加 `locked_project_graphs`，其它顶层schema不变。

新增producer7 helper为 raw_project_paths、raw_snapshot_files、snapshot_raw_graphs、raw_snapshot_contract、locked_raw_delta_contract、live_lock_contract、stage_raw_contract；新增validator4为 raw_project_paths、raw_snapshot_contract、locked_raw_delta_contract、locked_project_graphs_contract。validator新增import仅标准库copy；没有producer oracle import、ROOT重写或实际module执行。

## 两阶段raw与真实文件闭合

prepare.native在原两个restore都成功后才读取两图的最终实际lock/assets/dgspec，并保留原lock graph、Core13+W、framework/pruning、ZIP/sidecar/nuspec/native metadata/root contentHash校验。snapshot_raw_graphs以原字节复制两assets+两dgspec到native输出的raw-native，复制前后核真实源/快照/Ref四者SHA相等；不序列化改写SDK输出。快照精确枚举四文件及必需父目录，拒绝symlink/特殊项/额外文件或空目录。两个native lock Ref继续指真实replay。

build入口验证native raw快照并通过live_lock_contract读取真实两lock；lock_before不是snapshot自比。原两个locked restore和两个build顺序/命令/预算保持，全部成功后再核真实lock_after等于native固定SHA与before，随后复制最终四raw到raw-locked。失败仍在生成startup/build成功制品之前停止；部分provider DLL或诊断失败不能成为build接受。

新增build.locked_project_graphs恰固定provider/host顺序两行，每行恰project/assets/dgspec三键。没有增添root/count或通用图shape。startup.locks仍为真实replay相对路径→native/live相同lock SHA；startup.assets为固定真实replay相对assets路径→已验证等于live字节的locked快照SHA。未对snapshot路径调用relative_to(replay)，未伪造ROOT/实际项目路径。

阶段根可信来源明确：validator.approval_contract实际调用 `native_contract(native, build, startup, inputs, replay, approval['native'], approval['build'])`，传入外层已读取验证的两阶段Ref；producer.readiness使用实际b.native与br。helper从这些Ref的父目录派生raw-native/raw-locked，固定native.json/build.json/startup.json身份及对应关系，不由待验raw路径自推允许根。fixture helper可接收内存对象用于语义负例；正式外层入口仍从真实阶段Ref加载对应文档。

producer.stage_raw_contract与validator.locked_project_graphs_contract分别校验：同source/inputs、无DB、build→native、startup→native/source/replay、startup文件位于当前build阶段并与实际Ref内容相等；native/locked各四immutable raw、当前两live lock、当前四live raw，以及startup实际路径/hash。lock_before/after严格等于native真实lock Ref。readiness、独立approval/case验链均消费该合同，旧v5缺字段build或跨阶段/跨项目/以live冒充native快照不能通过。

## 完整JSON的唯一有限变化

producer与validator分别实现完整对象对比，native中五个指定restoreLockedMode必须不存在，locked中须为严格bool true。仅在native对象的内存深拷贝中插入expected true，然后对完整locked JSON递归类型严格比较。不能以1、字符串或false替代true，没有删字段/忽略header/pruning，也没有泛化restore子树差异。

五处位置仍为两assets各一处、provider dgspec一处、host dgspec的provider与host各一处。两个实际project的绝对路径按当前replay推导；dgspec的有限项目集和每个restore.projectPath也严格匹配。完整其余JSON相等，因此locked侧不能改变frameworks、pruning、targets、libraries、contentHash或其它header/restore数据。既有native lock/package/Core13+W/root graph/hash domains校验保留，不因完整对比而被替代。

本轮还独立重读历史实际raw：native-original-raw-snapshot/postbuild-raw-snapshot各六文件SHA与原before/after map相等；两lock字节完全相同，四JSON恰五处上述true新增。原报告 `bd802d330eaf136ea559a01010a12264357171df3b910294b273c8489166c81e`、source `c551e73319a1b2ac3e173f0bf771776b122fa86b461806f637f15043a3765834`、native `c41983a0b586c7947be1abf70df6ececdf939143976977e69d0a646f339901cb`、failedbuild `01808b7ee8a546294b7daf58d24b6d42a391b17f34bae42d97db551454d046ba`、safe diagnostics `b7ce720878d8dd17175cc884f7d47ee0569b644efbbb5917dae76ee7db26a562` 以及v5交接 `6887f1a68cbf9dd33a6d232b25179b5ed351661be014ef8a784e83a16c512faf` 均重新hash匹配。此实证是旧失败链的raw关系，不是新build执行证据。

## 原55与新增11的测试源码

原55个test方法以类名+方法名逐个比较AST完全相同；新增ImmutablePhaseRawContracts下11个方法，声明总数66。原fixture helper仅更新当前producer pin与模块版本标签。15producer pin由实际v2 map、原v4两个host override、v5来源和当前prepare/README/ProbeContext/Program四个producer变化重算，精确等于当前实际15，排除validator/test，无自指17/15 hash循环。

新增方法对应如下；它们目前仅为已审源码声明，本审查没有执行：

| 方法后缀 | 覆盖 |
| --- | --- |
| historical_failed_build_raw_full_relation_and_unchanged_locks | 实际旧四raw/五插入与两lock字节正例 |
| controlled_raw_stage_ref_live_positive_boundary_only | 明确标注synthetic的文件边界正例；空SDK events/commands不构成native/build通过 |
| finite_build_and_locked_graph_shapes_N01_to_N09 | build/图字段、2行数量顺序、类型、project/交叉Ref |
| stage_path_substitution_N10_to_N12 | native/live/locked/错阶段/越界路径替换 |
| snapshot_and_live_physical_integrity_N13_to_N16 | symlink、缺失/额外文件/空目录、实际字节漂移 |
| real_live_lock_and_before_after_binding_N17_to_N19 | 仅改live lock、snapshot替before、after SHA漂移 |
| only_live_assets_or_dgspec_drift_N20_N21 | 两项目的真实live raw独立漂移，immutable快照不变 |
| startup_requires_locked_hash_and_replay_relative_keys_N22_N23 | 已重签受控startup仍拒绝旧native SHA/snapshot或错位路径键 |
| same_actual_refs_mode_semantics_N24_to_N28 | 五插入点逐点false/int/string/缺失/native预含标志拒绝 |
| same_actual_refs_all_other_json_fields_rejected_N29_to_N32 | header/restore/framework/pruning/targets/libraries/contentHash/真实项目路径集完整差异拒绝 |
| resigned_controlled_locked_snapshot_and_live_pruning_rejected | 同步修改并重签受控locked快照/live/startup/build，仍因语义pruning差异拒绝 |

N24—N32直接传入实际历史文件加载对象的内存深拷贝，仍验证原Ref字节未变；不先改整文件SHA再以hash失败充语义成功。N22—N23明确重签受控startup以到达映射错误；最后一个测试同步重签受控快照、live、startup、build后要求完整JSON差异错误。N17/N20/N21真正只改控制目录live文件，证明合同检查真实文件而不是两份快照自比。所有临时写入只在测试的受控目录；fixture不导入producer、不改真实历史文件、不宣称SDK接受。

## 保留边界与后续门

其余11文件、policy/run、helper5、其余host7、CLI13输入、10字段SDK receipt、DEVNULL、180秒+2秒reap、fixed4/compiled28及TLS/证书/保存点/清理行为保持。source产品v3/43字段/178与新source-only scope契约的AST不变；source187/locked188路径及计数不扩张，host新增字节由新pack/source链重新绑定。无新包或W版本变更。

run.input_snapshot与validate.snapshot_contract原集合保持，覆盖原顶层阶段Ref、live source/locks/assets、binaries等，**不包含live dgspec**。本文不将新raw合同等同于扩大的runner快照。新的immutable raw及live dgspec通过readiness和独立approval/case完整验链检查；Low需按root设计另保存整个验证窗口全部raw的前后保护证据。runtime approval顶层没有新增字段或运行授权。

下一门是root授权Low对新实际17/9冻结pack执行完整**原55+新11=66**精确方法集合，而非仅跑新增11或拿原55的AST未变充新通过。然后仍需新inputs/toolpack/host9绑定下的source→native→locked build逐阶段实际命令、退出码、真实两锁及八immutable/四live raw、完整binary/MVID/readback；成功后另授compiled28/Python offline/readiness。四场DB runtime仍须独立批准。任何失败保持当前证据并停止，不回写旧v5、跳过字段或重签旧native。

接受范围是源码可进入上述下一验证门，既有失败/部分DLL/旧Python55或产品824结果均不替代新门。完成本文后停止写入。
