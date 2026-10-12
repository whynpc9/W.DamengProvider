# TP07 T1 probe v6：编译失败与阶段原始文件引用修订设计

2026-10-11，Astra High 只读分诊。结论：v5 build 有两个独立且已确证的问题，需要新 v6 工具版本；最小修订范围为 prepare.py、validate.py、test_contracts.py、README.md、host/ProbeContext.cs、host/Program.cs 六文件。本设计不授权编码或执行。root 明确派发后由 High 编码、Astra 源审、Low 独立验证。旧 v5 的 17 文件冻结、Python55、source/native、失败 build、诊断及全部旧报告保持不变。

本轮读取源码、实际失败/诊断 JSON、两阶段 raw 快照，使用标准库做文件 hash、JSON 与字段关系比较；未导入项目工具、运行 SDK/tests/probe 或访问数据库。只新增本文。

## 实际证据与两个原因

| 制品 | SHA256 |
| --- | --- |
| v5 source/source.json | `c551e73319a1b2ac3e173f0bf771776b122fa86b461806f637f15043a3765834` |
| v5 native/native.json | `c41983a0b586c7947be1abf70df6ececdf939143976977e69d0a646f339901cb` |
| build-control/failure-manifest.json | `01808b7ee8a546294b7daf58d24b6d42a391b17f34bae42d97db551454d046ba` |
| build-control/independent-readback.json | `ca0058a621e5cebe1d3fd20788f34424140cd585fd1da3ccaecd396b30e0fefe` |
| build-control/raw-native-before-after.json | `bd802d330eaf136ea559a01010a12264357171df3b910294b273c8489166c81e` |
| diagnostic-control/safe-compiler-errors.json | `b7ce720878d8dd17175cc884f7d47ee0569b644efbbb5917dae76ee7db26a562` |
| diagnostic-control/independent-readback.json | `ea4291ae3a5ce55ff84755dfebdbd2ac6ac2c354acecffd0968fb31bdb8d7512` |
| 独立诊断原始编译日志 | `74b80e7ed1ded7795068e01c3e3fdd5a036f3258d404a9fc18373aa4f4e6dc4c` |

表内目录均在 `.local/temporal-native/verification`：分别为 TP07-T1-probe-v5-source、TP07-T1-probe-v5-native、TP07-T1-probe-v5-build-control、TP07-T1-probe-v5-build-diagnostic-control。前七个制品的实际 SHA 在本轮重新计算；日志 hash 由独立诊断 readback 绑定。

原 build 的两个 locked restore 都 exit 0，provider build exit 0，host build exit 1。原 process 丢弃 stdout/stderr，原 failure 不能精确解释编译器原因；root 另授一次 host build 诊断，natural exit 1，未 restore/test/DB，未改变任何 bin/obj，记录三个唯一错误：

| 源位置 | 错误 | 最小处理 |
| --- | --- | --- |
| ProbeContext.cs:12 | CS0108，内部 Entry 属性隐藏 DbContext.Entry | 将该属性改名 OwnedEntry，并同步同文件 ProbeModelCacheKeyFactory 的三处读取；Rows 不变 |
| Program.cs:75，column 964 | CS8604，CandidateGuard.Identity(transaction) | 仅此参数改为 transaction! |
| Program.cs:78，column 467 | CS8604，同一参数 | 仅此参数改为 transaction! |

两处 nullable 修订只表达现有控制流证明：前一 begin_transaction Step 成功赋值 transaction 后才进入 capabilities/write；Step 失败会 throw 并中止后续步骤。根 agent 已确认此范围。它不改变实际观察值、transaction 对象、Step 顺序、异常处理或 null 行为，不增加 guard，不扩大其它 null suppression，不改 NoWarn/编译规则。

第二个问题独立于编译：native.project_graphs 的 assets/dgspec Ref 指向可变 replay/obj 文件，后续 --locked-mode 实际改写了这四文件。原 native.json 字节不变，四 Ref 的目标字节已经漂移。当前 build 成功后构造 startup.assets 时的 verify_ref 会先失败；即使跳过这里，readiness 和 validator.native_contract 仍会拒绝旧 SHA。不能仅修编译，也不能重签旧 native.json。

本轮分别读取 native-control/native-original-raw-snapshot 与 build-control/postbuild-raw-snapshot 的完整六文件，独立确认两 lock 字节相同，四 JSON 只有下面五个新增 bool true：

- provider assets：project.restore.restoreLockProperties.restoreLockedMode。
- host assets：project.restore.restoreLockProperties.restoreLockedMode。
- provider dgspec：其唯一 provider project 的 restore.restoreLockProperties.restoreLockedMode。
- host dgspec：provider、host 两 project 各自的同一字段。

两 live lock SHA 分别为 provider `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`、host `69c5a714efcac8afe2f046aa65e82e334a05074c63a0bcd4ad45c64147e40dde`。source187 未漂移；host lock 是 source187 之外原有的第188项，不能误说两个 lock 都在 source187 map 内。

## 最小原始文件合同

选择仅复制四个会变化的 raw 文件，保留 native 两个 lock Ref 指向真实 replay。无需把稳定 lock 改成 snapshot Ref，也无需新增计数或改变 source/native 顶层 schema。native.project_graphs 仍为固定 provider、host 顺序的两行，每行仍恰 project/lock/assets/dgspec/lock_dependencies/frameworks 六键。

prepare.native 在两个原 restore 都成功之后，读取最终真实 raw、执行现有两图/锁图/包图/framework/pruning 校验，再以原始字节复制四文件到新 native 输出目录下 `raw-native/<原 replay 相对路径>`。例如 provider assets 为 `raw-native/src/W.EntityFrameworkCore.Dameng/obj/project.assets.json`。不得 JSON 序列化重写、改真实项目绝对路径或移除字段。复制前后真实源与快照 hash 必须相等；快照根完整枚举恰四文件，无 symlink、额外项或跨界路径。图中 assets/dgspec Ref 改为这些新快照；lock Ref 仍是原真实 replay 两路径与原 SHA。

所有 raw 路径只能由固定两个 project 和固定 suffix 推导：packages.lock.json、obj/project.assets.json、obj/<csproj filename>.nuget.dgspec.json。snapshot 根由已经验证的对应阶段 JSON Ref 的父目录加固定 raw-native/raw-locked 推导，不新增可由输入自由选择的 root/path/count。raw 中的项目根仍是实际 source.replay_root；snapshot 目录只是保存字节的位置。

build 顶层只新增一个字段 `locked_project_graphs`，在 validator.TOP['build'] 的 exact shape 同步加入。它恰两行，project 顺序与原两项目一致，每行恰 `{project, assets:Ref, dgspec:Ref}`。此字段不是完整图的手写替代，也不复制 lock_dependencies/frameworks。成功 locked restores/builds 之后，保存最终真实四 raw 到 build 输出的 `raw-locked/<原 replay 相对路径>`，原始字节复制及精确枚举规则同上。失败时不得输出成功 build.json/startup.json，不能把部分 DLL 或捕获诊断当 build 接受。

build 的 lock_before 和 lock_after 保持原两项目→Ref结构，均必须实际读 hash 真实 replay/packages.lock.json 并验证等于 native.lock Ref 的固定路径和 SHA。不得改成 snapshot 对 snapshot 的自比；入口、末尾和独立 readback 都验证两 live lock。两个 map 完全相等且与 native 两 lock 相等；原 source187/hash检查继续执行。

startup schema 保持不变。startup.locks 仍以真实 replay 相对路径映射相同两 lock hash。startup.assets 的键仍是两个真实 replay/obj/project.assets.json 相对路径，值改取已证明与当前 live bytes 相等的 locked_project_graphs.assets SHA。不得对 snapshot 绝对路径调用 relative_to(replay)，不得把 raw-native/raw-locked 地址作为启动宿主文件路径。host、provider、EF DLL/MVID、W 包、全部 binaries、source/native lineage 仍使用真实原链。

## 原始 JSON 的有限差异校验

producer 与 independent validator 各自实现有限对照 helper；validator 不导入 producer oracle。对每个 native/locked raw 文档，先验证 Ref 的阶段路径/真实字节、同一 project 与实际 replay，再独立比较完整 JSON：

1. native 的五个指定位置必须不存在 restoreLockedMode；locked 的对应位置必须存在且 `type(value) is bool`、值为 true，不能是 1、字符串、false 或缺失。
2. native/locked 两份完整对象都保留不变。允许在内存中由 native 深拷贝仅插入这五处 true 来形成 expected locked，然后递归类型严格比较完整实际 locked 对象；不得删字段后比较、过滤 pruning/header、泛化任意 restore 差异。
3. assets 的 project.restore/project.frameworks/libraries/targets 等其它所有数据精确相等；dgspec 根字段、实际项目绝对路径集和各项目其它全部内容精确相等。provider dgspec 恰 provider，host dgspec 恰 provider+host；不接受任意同数量项目。
4. 原 assets/framework pruning、两份 lock_dependencies、Core13+W、无官方/Design/AspNet、实际包 ZIP/sidecar/native metadata/contentHash 与 root origin 的独立合同继续执行；完整比较不能替代它们。

这是从本次两阶段原始文件得到的固定 SDK10.0.401 差异合同。新 v6 若出现额外真实 SDK 差异，应停止并交还分析，不能自动吸收更多字段、忽略大小写/路径、删 pruning 或更改 expected graph。

## readiness 与独立验证的消费修订

prepare.readiness 要同时验证：原 native JSON 与四 immutable native raw Ref；build JSON 与四 immutable locked raw Ref；native 两真实 lock Ref；从 project 推导的四真实 replay raw 文件。真实 locked raw SHA 必须逐项等于对应 build snapshot SHA，并通过上述 native→locked 完整有限比较。startup.locks/assets 必须与这些真实路径和 hash 一致；其余 source、全部 binaries、toolpack、offline、process 成功检查保持。

validator.native_contract 做相同闭合且保持独立实现：native graph 的 lock 仍严格位于 replay；assets/dgspec 改为从已验证 native Ref 推导的 raw-native 固定路径，不能继续要求它们位于 replay。建议 approval_contract 将已验证的 approval.native/approval.build 两 Ref 显式传给 native_contract 或其阶段 raw helper，精确核 native.json/build.json/startup.json 的同阶段关系；producer readiness 使用已有 br/b.native。不得从待验 raw Ref 自己反推允许根。locked_project_graphs 必须 exact 两行/三键/固定顺序、来自该 build Ref 父目录的 raw-locked；逐项核实际 live 四文件与 snapshot，校验五处有限变化，再绑定 startup。拒绝旧 v5 缺字段 build、以 native snapshot 冒充 locked raw、交叉 project Ref、其它阶段 snapshot、路径逃逸/symlink、只重签 snapshot 而不核 live 或 lock。

readiness 生产者检查与 runtime approval/case 的独立 native_contract 验链都需要覆盖新合同。run.py 的原 input_snapshot 与 validate.snapshot_contract 原先共同覆盖顶层阶段 Ref、startup 的 live source/locks/assets、binaries 等，未列 live dgspec；root 已接受本次最小六文件设计保持这两端 snapshot 集合一致且不扩大 run.py/runtime approval 顶层。新的 immutable raw 与 live dgspec 由 readiness 及独立 approval/case 的完整 native_contract 验证；Low 同时将全部 immutable/live raw（含 dgspec）纳入整个验证窗口的独立前后保护证据，不能声称这些文件已经进入原 run before/after snapshot 字典。不要仅修改 validator.snapshot_contract 导致与 runner 的字典不一致。

## 编码边界与验证门

| 文件 | 授权后允许的具体改动 |
| --- | --- |
| prepare.py | native 四 raw 原字节快照；build 有限 locked_project_graphs/真实 locks/正确 startup.assets；readiness 双阶段及 live 闭合；必要局部纯校验/保存 helper |
| validate.py | build exact shape 加单字段；approval_contract 传入现有已验证阶段 Ref；native_contract 的阶段 snapshot/live 路径/完整差异合同及有限 helper |
| test_contracts.py | 六文件对应新 producer15 pins；新增局部来源/双阶段语义正反例，保留原55方法AST及实际失败历史 |
| README.md | 准确说明两阶段 raw、真实锁校验、有限差异、编译窄修及新验收门 |
| host/ProbeContext.cs | Entry→OwnedEntry，仅内部属性和本文件三处 getter |
| host/Program.cs | 两处 CandidateGuard.Identity(transaction!)，无其它观察/流程改动 |

建议局部 helper 名称与职责：producer `raw_project_paths` 只推导有限路径、`snapshot_raw_graphs` 只原字节保存四文件、`locked_raw_delta_contract` 只做完整 JSON 的有限变化校验、`stage_raw_contract` 闭合两阶段 Ref/live/startup；validator 可有独立同职责 `raw_snapshot_contract`、`locked_raw_delta_contract`、`locked_project_graphs_contract`。这些是实施拆分建议，不强制增加文件或方法数量。原函数实际改动限定为 prepare.native/build/readiness，validate.approval_contract/native_contract；测试的既有当前 producer fixture helper 仅更新版本 pin，其余原55方法保持。新增 helper 的实际清单由 High 冻结时完整声明。

其余11工具文件、run/policy、其余host7、helper5、CLI13输入、fixed4 live/compiled28注册表不变；17pack/15producer/9host计数不变。product178/manifest400123/newsourceapproval66c/W包/TLS证书/savepoint/数据库行为不改。新 source187/locked188 是同路径集合上的新 host 两文件字节，不借修复增加依赖、源文件或 schema 计数。

新 producer15 仍排除 validate/test；保留 v4 两个已批准 host override 与 v5 provenance，以实际新 prepare/README/ProbeContext/Program SHA 更新有限 pin。新17/host9/source aggregate由实际文件重新生成。原55方法不能改写成对修订实现的镜像，也不能拿旧55结果充新通过；需要新增的实际方法数以冻结后AST枚举为准，不预填通过数量。

新增测试使用以下有限范围；方法如何分组由 High 实际实现后列出精确名称，不能预填未来 passed 数。正例有三个角色：历史真实 raw 的五处变化关系；独立控制目录中的阶段 Ref/live 路径闭合（明确标为 synthetic contract fixture）；新源码下实际新阶段执行接受只由 Low 产生。

明确要求32种负面 mutation 模板，按其相关的固定两 project/四 raw/五插入点参数化，不扩大到任意项目或路径：

| 编号 | mutation | 编号 | mutation |
| --- | --- | --- | --- |
| N01 | build 缺 locked_project_graphs | N02 | build 额外未声明字段 |
| N03 | locked 数组少一行 | N04 | 多一行 |
| N05 | 两行逆序 | N06 | 两行重复同一 project |
| N07 | 图行缺字段/多字段 | N08 | 数组或 Ref 错类型 |
| N09 | project 替换/两项目 Ref 交叉 | N10 | native raw Ref 改指 live replay |
| N11 | locked raw Ref 改指 native snapshot | N12 | raw Ref 指其它阶段/越界路径 |
| N13 | snapshot/live raw symlink | N14 | snapshot 缺文件 |
| N15 | snapshot 多文件 | N16 | snapshot 实际字节漂移 |
| N17 | 只改真实 live lock | N18 | lock_before 改用 snapshot Ref |
| N19 | lock_after 与原 native SHA 不同 | N20 | 只改 live assets |
| N21 | 只改 live dgspec | N22 | startup.assets 使用旧 native SHA |
| N23 | startup 键使用 snapshot 路径/错位 live 相对路径 | N24 | lockedMode 为 false |
| N25 | lockedMode 为 int 1 | N26 | lockedMode 为 string true |
| N27 | lockedMode 缺失 | N28 | native 原件预含 lockedMode |
| N29 | 其它 restore/header 字段变化或额外字段 | N30 | frameworks/pruning 变化 |
| N31 | targets/libraries/contentHash 变化 | N32 | dgspec 项目集合/真实项目路径变化 |

N24—N32 必须在内存对象上以相同真实 Ref 调用语义 helper，或同步重签受控测试快照后调用完整合同，证明不是先因整文件 SHA 不符而假通过。N17、N20、N21 必须实际仅改控制目录中的 live 文件，保留 immutable fixture不变，证明没有退化成 snapshot 自比。上述32是有限 mutation 模板数，不是新增 unittest 方法数、执行数或通过数。相关检查还须满足：

- 读取真实 v5 immutable native/postbuild raw，正例证明两 lock 不变及四 JSON 五处 true 的完整关系；明确这些是失败 build 的历史 raw fixture，不能合成新 build acceptance。
- 直接内存语义变异：true→false/int/string/缺失、native预含lockedMode、任意额外 restore/header/pruning/framework/target/library/projectPath/contentHash 差异均拒绝；负例不只靠整体文件 SHA。
- 有限 build shape、locked_project_graphs 少/多/重复/逆序/错类型/交叉项目 Ref；缺失/额外 raw、symlink/越界/错阶段、snapshot bytes 漂移拒绝。
- 控制目录内真实文件正例及负例验证 live lock_before/after 对 native hash；只修改 live lock 而 snapshot 不变仍拒绝；只修改 live assets/dgspec 而两阶段 snapshot 不变仍拒绝；伪造 snapshot 同步重签而改 pruning 仍被完整语义拒绝。
- startup 只接受真实 replay-relative lock/assets 键及 locked SHA；native旧assets SHA、snapshot路径作startup键、对应路径错位都拒绝。原 source187/188闭合、process/compiled28固定测试保持。

High 只能在 root 六文件精确授权后编码，不运行 SDK/tests。冻结后 Astra 检查六文件实际 diff/17与15 pins、原55 AST、新增负例有效性及两处 nullable/Entry 改名边界。Low 新鲜静态17pack/host9回读后，在 root 窗口跑完整新 Python 契约；成功后新输入绑定新 pack/host lineage，再新 source→native→locked build 串行执行并逐阶段独立 readback。原 v5 错误证据和 native 外部快照不可重签或覆盖。

新 build 验收必须有实际两个 locked restore、两个 build 全部成功，真正 host/provider/EF/W binaries/MVID与完整输出集合、真实两锁前后相等、native四快照/locked四快照/最终live四raw的全链闭合。通过后另授 compiled28与新 Python/offline、readiness，四场真实库 runtime 仍须独立批准。旧 provider partial DLL、捕获日志的失败编译、历史55或产品retry49/25/full824均不能替代这些新门。

process 的 DEVNULL、现十字段 receipt、180秒执行+2秒reap、信号/取消/清理算法本轮不变；一次独立300秒上限诊断的日志不改变生产预算，也不要求扩大日志或 receipt schema。完成本文后停止写入，等待 root 的下一明确任务。
