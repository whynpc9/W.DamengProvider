# TP07 T1 product preparation-v2源码复审

2026-10-10。结论：**preparation_v2_source_accepted / v1_two_source_blockers_closed / Low_process_contracts_and_actual_stages_pending**。v1报告`fa0bede2…`的取消回收和raw native图两项源码阻塞已闭合，未发现新增源码阻塞。只接受当前三文件实现；没有执行或接受SDK/native/build/units/DB结果。

本轮只读源码/README/schema及实际旧baseline JSON，计算hash/AST和字节差异；没有import/exec准备工具，没有运行SDK、tests、validator、wrapper、DB或子进程取消实验，没有改源码、产品178、旧proof或status，仅新增本文。probe另案不在本审。

| v2冻结文件 | SHA256 |
| --- | --- |
| prepare-unit.py | `1b4b3e780eb55757b870b94ab5d9f5bb7a329e4ad898f1236aace6fb5280e0cd` |
| README.md | `0f0eb6358751419653435544dcedb590c17e91714a283b0b5bce454d26001428` |
| schema-draft.json | `3da2689d7b7586fbbbed335d6419937191f939763a45d2f2a5ec22a4673615e0` |

v1三源实际仍为`d92dcf28…/b178c212…/19c2e8b5…`，Low v1 source-manifest实际SHA`54109dd6a39f2c51fac016721c7bc3977e049db7989482eabd8c90f848fdcadb`，原changes-requested报告仍`fa0bede25a266eb3431a0117c31858369f45d566164c1dd373ec794cdfaebb2f`。没有以新v2覆盖旧版本。

## 取消回收门

新process以monotonic起点固定300秒执行预算；SDK启动前保存stage started，Popen取得新独立group后立即通过callback落盘真实PID/PGID。启动及finalizing期间signal先记录pending，待child赋值/持久化后进入清理，避免在Popen返回与child保存之间直接跳出而遗失所有权。

SIGINT/SIGTERM、KeyboardInterrupt及其它BaseException统一进入失败路径；finally探测整个group，不因leader已退出就忽略其余成员。需要终止时SIGKILL，最多2秒wait/reap，处理ProcessLookup竞态；之后同时记录实际host exit、child_reaped及group absent/unknown。未确认reap或group absent时exit_code=null，不能提升为成功。execution/reap/total elapsed分开保存，2秒是失败回收预算，不扩大正常300秒执行接受界限。

success要求自然exit0、无timeout/termination/cancellation/error、reaped=true/group absent、未使用reap清理及总elapsed≤300；已取消但host恰自然返回0也不能通过。内外signal handler都在finally恢复，stage_main使用BaseException安全失败收据；没有自动retry或后阶段继续。SIGKILL无法捕获的边界在README/schema明确由Low外部supervisor依据已保存PID/PGID核实/回收后再释放窗口，不声称脚本能保证该情形。

这关闭v1只处理TimeoutExpired而可能留独立SDK组的问题。实际OS时序尚待Low的有界无SDK子进程/故障注入合同证明；本报告不把静态控制流当作取消实验通过。

## 四份实际raw图基线及有限映射

root限定新增的RAW_BASELINES仅四个固定命名路径/SHA，不扩张原inputs12refs/3folders；本轮四份实际文件全部hash匹配：

| 基线（相对workspace） | SHA256 |
| --- | --- |
| .local/temporal-native/verification/TP07-M1-v5-offline/unit/work/src/W.EntityFrameworkCore.Dameng/obj/project.assets.json | `0a3836b4f7c775a191c9737adaeeccb60cc229b252b1f6ad923cf53b8480b40d` |
| .local/temporal-native/verification/TP07-M1-v5-offline/unit/work/test/W.EntityFrameworkCore.Dameng.Tests/obj/project.assets.json | `fa3c18eeae41c921e8946f0b3ceff2c17efb4bcccb630e5754ec5ed9c4d203fd` |
| .local/temporal-native/verification/TP07-M1-v5-offline/unit/work/src/W.EntityFrameworkCore.Dameng/obj/W.EntityFrameworkCore.Dameng.csproj.nuget.dgspec.json | `f60560579f49af1dc22caa454376f135268a8bae12ad0d79920cd7cbbd2e7a54` |
| .local/temporal-native/verification/TP07-M1-v5-offline/unit/work/test/W.EntityFrameworkCore.Dameng.Tests/obj/W.EntityFrameworkCore.Dameng.Tests.csproj.nuget.dgspec.json | `221579ea9f9688168195cad46c0b37ca71381a3f752ed157f1e84c1567c4533d` |

实际assets version4，provider dgspec format1/restore provider/projects仅provider；unit dgspec format1/restore unit/projects provider+unit。两者每个project只有net10.0，完整pruning map各272项，旧restoreLockProperties实际含字符串restorePackagesWithLockFile=true和布尔restoreLockedMode=true。provider的f605文件只是旧图基线，不是新SDK已产出的结果。

新raw_project_expected仅改变已枚举位置：projectUniqueName/projectPath/outputPath、唯一provider ProjectReference、packagesPath/packageFolders、新restore.config以及单个新feed；force-evaluate移除locked属性，locked模式显式要求true。其它header、project version/name/Style、framework/dependency范围、central组、imports/fallback、SDK路径及pruning内容保持整对象比较。272数量检查不是单独批准门；actual完整JSON还须与固定基线剩余内容一致。字符串/数值/布尔等表示通过JSON序列化比较区分，version/format额外要求整数。

这没有把旧双feed照抄到新执行，也没有删除整个restore/framework节点来绕过差异。package/project libraries与targets随完整assets对象一起核对，raw dgspec每个project subtree还与本次actual assets.project相等；不存在只过滤Package、忽略Project的空缺。若实际新SDK生成未涵盖的额外变化，正确结果仍是失败回root，不能临时扩展归一化或任意count。

## 实际输出与阶段关联

四个旧baseline加入protected输入before/after。每次restore返回后先保存`observed_raw_graph`固定四项actual引用或null，失败/partial也保留存在的原文件观察，missing不当成功。`actual_native`返回raw_graph_baselines/raw_assets/raw_dgspec/raw_graph_mode，实际新四文件进入protected_outputs。

build的locked restore成功后，在build进程启动之前先核source178和完整actual raw/native；build后再次核，后续targeted/full阶段从前manifest重读raw/binary/源/锁，不能只信成功flag。manifest revision2要求依赖阶段也schema2及同script SHA，不兼容旧v1阶段。旧baseline不被复制成新dgspec/metadata，所有实际lock/metadata/raw图仍由SDK生成。

## 原有限合同保持

PINS、FOLDERS、FLAGS、PHASES与v1 AST相同；copy_source、source_bytes、package_records、archive_record、local_feed、environment、trx逐函数字节不变。继续精确178源、source-only七字段/ac8批准、12refs/3folders、native49 non-W+W+lowercase Project=51及50档案；unit lock3e4b192f…/provider77d4c083…、NuGet metadata contentHash与ZIP SHA512 sidecar分开、nuspec/ID/version/边及原archive证明不放宽。SDK固定10.0.401、任务CLI_HOME/flags及无TEST/secret/全局cache清理边界保持。

事件数仍source/native/build/targeted/full为0/2/3/2/2，前manifest实际SHA/命令/source/protected outputs以及分开的SDK/test窗口标志保留。targeted25自然Passed TRX、无skip/abort、正确类和同build；full无filter且完整Passed counters与保存targeted TRX重核保持，不能用exit0替代TRX。source_scope_only静态批准在这些门之前成立，不构成循环依赖，也不自动授权执行。

原目录`.local/temporal-native/verification/TP07-T1-product-preparation-v2/prepare-unit.py`的parents[4]实为workspace；freeze副本不作为入口。输出新鲜且与源/cache/前阶段不重叠的守卫保持，产品178、W/EF patch及旧native数据没有被该修订改写。

## 后续窗口

root可安排Low先保存v2三文件完整snapshot及实际static readback；再按单独批准执行无SDK取消/自然退出/timeout/退出竞态/unknown-reap合同和raw图变异检查。Low独立门完成后，source/native/build/targeted/full仍各按本工具有限依赖与root具体窗口逐门进行，源码接受不是其中任一阶段的执行或结果批准。

当前无新增最小修复任务。真实native图若失败必须保留新原始files/manifest，不修cache/lock来通过；实际单位25及full、probe activation、后续L0/E/D/runtime仍pending。报告完成后停写。
