# TP07 T1 product preparation-v1源码复审

2026-10-10。结论：**changes_requested_process_cancellation_and_native_raw_graph_proof**。有限输入、source-only批准及50包档案边界成立，但取消回收与新native header/dgspec证明存在两个具体阻塞；不得据本版本开启SDK/test阶段。先保留Low完整v1三文件snapshot，再由root限定High修订，不能覆盖旧失败/冻结来源。

本轮仅只读407行脚本、README/schema、实际12refs/原JSON/ZIP/nuspec/sidecar，计算hash和结构比较；没有执行准备工具、SDK、tests、validator、DB或wrapper，没有修改产品/工具源或旧证据，仅新增本文。另一个probe17文件及其40 Python合同不在本审范围。

| v1冻结文件 | SHA256 |
| --- | --- |
| prepare-unit.py | `d92dcf283068172f9d49ee194a27ed36d9725d2ffc0c4255107e3456455fc28a` |
| README.md | `b178c21238a62e5eed571141e286ceb80f3cc28738f4a1d9155ec645e5b795e0` |
| schema-draft.json | `19c2e8b5f9e82082030d61bf1e02a3afb0102cb1aa3eae9592fe271eefbd66fb` |

## 阻塞1：取消不能保证终止并回收SDK子进程

`process`第176–189行以`start_new_session=True`创建独立进程组，但只有`TimeoutExpired`分支调用killpg；KeyboardInterrupt等BaseException会直接退出wait，main第399行仅捕获Exception。父脚本收到SIGTERM时也没有负责清理该独立组的signal/finally协议。因而上层取消脚本后，SDK/test子进程可能仍在运行并继续写work/bin/obj/cache，不能安全宣布窗口已释放。timeout路径SIGKILL后`child.wait()`也没有有限reap界限，killpg退出竞态未处理。

要求High在同三文件范围修正：原300秒正常执行deadline不改；建立可审计的active child pid/pgid记录，SIGINT/SIGTERM/KeyboardInterrupt/BaseException进入统一失败清理，finally对尚存子进程组kill并有界wait/reap，处理ProcessLookup竞态。保留原异常类别/明确cancelled或termination code和实际host退出，不把取消写成completed success，不自动retry。signal handler须恢复，不能污染后续阶段。不可捕获的SIGKILL边界应由Low外部supervisor据已落盘child group信息核实/回收后才释放窗口，不宣称脚本能捕获SIGKILL。

开始与取消/timeout后收据必须保留，main不能在活动SDK未确认结束时仅写失败就返回并放行下一阶段。建议后续独立离线取消合同用受控无SDK子进程或注入process stub覆盖自然退出、timeout、SIGINT/SIGTERM、退出竞态及reap失败；本reviewer不执行这些检查。不得借修此问题提高300秒预算或添加通用续跑。

## 阻塞2：新native仅核包/边，缺实际raw dgspec及header/framework/pruning门

`actual_native`第197–221行正确核新unit/provider lock精确字节、包集合/依赖边/contentHash和50个新cache包，但只读两份project.assets.json的restore.frameworks、projectReferences、libraries/targets包子集。没有读取任何`*.nuget.dgspec.json`，没有核资产header/SDK restore roots/config/sources、project.frameworks原始dependencies/central/pruning语义；第362–365行protected_outputs也不保存raw dgspec。后续阶段因而不能独立复核这部分实际SDK图，和任务要求的完整新native证明不符。

现有原始基线可以明确定位，本轮已实际读取（相对verification）：

| 原M1-v5文件 | SHA256 |
| --- | --- |
| TP07-M1-v5-offline/unit/work/test/W.EntityFrameworkCore.Dameng.Tests/obj/project.assets.json | `fa3c18eeae41c921e8946f0b3ceff2c17efb4bcccb630e5754ec5ed9c4d203fd` |
| 同目录W.EntityFrameworkCore.Dameng.Tests.csproj.nuget.dgspec.json | `221579ea9f9688168195cad46c0b37ca71381a3f752ed157f1e84c1567c4533d` |
| TP07-M1-v5-offline/unit/work/src/W.EntityFrameworkCore.Dameng/obj/project.assets.json | `0a3836b4f7c775a191c9737adaeeccb60cc229b252b1f6ad923cf53b8480b40d` |

实际旧assets version4、raw dgspec format1；dgspec restore入口仅unit项目，projects恰provider和unit，两者frameworks仅net10.0。每个framework的原`packagesToPrune`完整map有272项；这是实际元数据，不是新的包依赖集合，也不能仅凭count272判通过。

新v2须在每次actual restore后保存真实raw assets及dgspec原文件hash/reference到native结果和protected_outputs，后续build/targeted/full阶段重核。有限检查至少包括：

- assets version、project.version/restore projectStyle=PackageReference、projectUniqueName/projectPath/projectName/outputPath归属本次明确project/work，packagesPath归属本次新task cache；originalTargetFrameworks/framework/targetAlias都精确net10.0。
- configFilePaths和sources必须指向本次唯一restore.config及新approved50 local feed，无额外feed、fallback folder、官方driver或其它project。旧基线两个feed不能按旧绝对路径照抄；v2设计就是新单feed，应按明确的新路径规则核。
- 原始framework dependencies、centralPackageVersions/management、frameworkReferences、imports/fallback及完整pruning map按已批准source/SDK/native基线严格比较；只对预先列明的work/cache/config/SDK路径作有限位置映射，不能以删除整个restore/framework节点的方式“归一化”。unit四个直接包及其范围已在固定native_interface明确。
- raw dgspec顶层format/restore/project集合、每project restore/framework/实际ProjectReference边必须与这次两份assets及固定provider+unit graph一致；libraries中的Project条目/targets project引用也需完整核，不仅过滤package项。

这些元数据由SDK自然产生，不手写dgspec/metadata或从旧cache复制来冒充新native。新增基线读取如需进入执行输入，必须用root认可的**明确命名有限ref/固定路径+SHA**，或从已有pin中可证明的引用链取得；不能附任意extra_refs、目录glob信任或“任意count”。当前12refs/3folders已定义的边界不能无声扩张。README/schema应同步新实际raw证据字段及后阶段重核要求。

## 已核对并应保留的正确范围

PINS12项实际当前hash全部匹配；FOLDERS仅product_source/origin_cache/origin_feed，原脚本绝对路径parents[4]实际为workspace。product manifest`932f8f41…`、patch`1752dc64…`、sourcefreeze`30c451f9…`、source-only approval`ac8ff44f…`及source review固定，产品完整178 map与freeze/inventory联结，不接受dirty checkout、L0/E harness或任意数量。原unit lock实际SHA确为`3e4b192f701f7b8579681a112c55b68346a6b2da0d19f56bda4fa021f60967fd`，provider为`77d4c083…`，不是准备时需要手改的占位锁。

原M1-v5 graph`8786770c…`与origin`3e6a2ac1…`的49 non-driver records相等；unit lock真实51=49非W+W+lowercase project。脚本从实际records的resolved version取cache目录，无假定contentHash可以解出ZIP hash。本轮只读核全部50个原archive SHA、SDK metadata contentHash、独立ZIP SHA512/sidecar、解压nuspec字节和ID/version，均与其实际lock/approved记录匹配。new feed复制精确50档案，new cache mkdir初始空，restore.config清源并列精确packageSourceMapping，不能写metadata/lock来通过。

五phase依赖集、有限命令及300秒上限明确：source0事件，native version+force-evaluate restore2事件，build version+locked restore+build3事件，target/full各version+test2事件。每阶段要求前manifest真实SHA/lineage/protected outputs及root reviewed/window flag，失败停止；不把source-only批准当SDK批准。build后真实provider/unit DLL MVID/deps及两输出目录hash、同provider字节/W身份保留；tests固定同build --no-build --no-restore，target固定类filter。

TRX检查有自然Completed、total=executed=passed、无skip/failure/abort、唯一executionId及所有Passed；target还要求25条且同类，full明确无filter命令且结果>25，不能只靠exit0或复用旧target。前target的实际TRX和同build引用在full前重核。以上守卫应保留，不能在两项修复时削减。完整tests/真实native/build仍未执行，静态可用性不等于通过。

## source-only七字段的纠正说明

本工具核的实际七字段批准是`schema_version/task/status/scope/manifest/exact_count/exact_paths`，scope=`transaction_capability_overlay_only`，只证明178产品source/patch静态来源，不表示单位、SDK或runtime接受。该JSON可以且应先于依赖它的native/build/unit门签发。

我此前产品源码报告`09fa721e…`提出“Low实际unit/M1证明后才签source approval”并沿用旧M1七字段的表述，是额外建议，**不是已批准硬设计**，且会造成准备循环；本次明确撤回该前置及旧字段假设。旧报告字节保留，以本说明及root实际有限source-only schema为准。真正native/build/单位/DI/ready仍须在runtime approval之前通过，不能反向借source批准跳门。

## 最小修复范围与停止线

待Low保存完整v1三文件snapshot后，root可仅给High新的准备工具revision三文件范围，修process取消回收和actual_native/raw保护两处，同步README/schema；产品178、transaction patch、root source-only批准、原cache/锁/M1/E/L0证据及probe不动。修后新hash/new source review，root再开source/native独占窗口；本报告changes_requested不授权任何阶段执行，也不允许Low在冻结v1上临时修补。
