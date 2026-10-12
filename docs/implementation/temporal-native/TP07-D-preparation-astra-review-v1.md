# TP07 D 外部准备 v1：有限工作流静态审阅

2026-10-09。结论：**request_changes：一个确定consumer接口阻塞。** 本轮仅静态读取设计、四个准备文件、固定锁图及D consumer接口；没有导入或执行准备脚本，没有运行SDK、CLI、tests、DB、cleanup或网络操作。只新增本报告，未触碰E残留对象或恢复流程。

## 冻结身份

设计 [TP07-D-external-preparation-design.md](TP07-D-external-preparation-design.md) 实算SHA256 `3da136b0efd3f4a1aaa5c1e33ca0a21ac22cf90cf0f055fee8c8725d6f12f1df`。Low `.local/temporal-native/verification/TP07-D-preparation-v1-freeze/source-manifest.json` 实算 `ba8330b00e1233c76a3cb8e7fa8b63d736c58864527c2853af4ebe87885649f3`；四项原文件与snapshot逐一匹配：

| 文件，位于 `.local/temporal-native/verification/TP07-D-preparation-v1/` | SHA256 |
| --- | --- |
| `prepare-d.py` | `ea3dce70526dcfd90008c4bacf4d6615e1298d66f8485d34e2261bd8603ee4ac` |
| `build-generated.py` | `a943531dbd88110541c85a86d8857cbea620cf8e99e14414dec41cb909011ae9` |
| `README.md` | `2063ad5efc7ace51a96413a795957f8d35b5233e609b6e9d0ae1596d399883fc` |
| `schema-draft.json` | `98fe2e974c2fff51ba26fd582d61574040db7edf5aa6fe5e0c16f9d51952cada` |

当前D25使用已冻结pack `9c8d6022…` / source manifest `4b4599ad…`，其44项离线结果由既有Low窗口另证；本轮不重复执行或把它扩充为D准备已通过。

## DPREP-B01 / P1：workflow startup遗漏真实CLI framework身份的必需字段

位置：`prepare-d.py:149–160` 的 `startup()`、`:271` D0输出与 `schema-draft.json` 的startup定义；对照冻结consumer `tools/DownstreamAcceptance/TP07/design-validate.py:543`。

准备脚本生成的workflow startup不含 `shared_framework`。consumer在每个实际CLI子进程的 `process_identity` 中读取 `start['shared_framework']['hosting_dll_sha256']`，与C# `typeof(Host).Assembly.Location` 的实际加载文件hash比较。R05 scaffold成功得到首个CLI receipt后仍会因缺字段KeyError而失败，无法完成第一个checkpoint；源码与依赖构建成功也不能补足这个接口。

现有CLI proof仅记录 `--list-runtimes` 和两个targeting pack的FrameworkList.xml，未输出上述实际Hosting DLL身份；不能以“安装了AspNetCore10.0.12”或CLI版本通过取代该字段。

最小修复要求：

1. 从批准dotnet安装根及实际安装的对应shared framework中，读取并固定 `Microsoft.Extensions.Hosting.dll` 的明确路径、版本来源与SHA；将consumer必需的 `shared_framework.hosting_dll_sha256` 写入D0 startup，并在外部构建/CLI证明中绑定该实际文件引用。更新schema/README，说明这是外部expected身份，尚非startup实际加载证据。
2. G6/G12通过原startup保留此稳定字段；不要新增随phase变化的startup标签，也不要把它加入允许任意更换的字段集合。实际CLI的loaded hash仍由现有consumer比较，不修改或绕过该比较。
3. 保护cleanup只需自身L0闭包，不能为修此D/CLI接口反向引入workflow框架或tool bundle依赖。若统一helper输出字段，必须明确两种host角色的必要性与来源。

先以静态schema/consumer读取链及实际文件hash来源复核闭环，再由Low在获批offline窗口做真实准备；本轮不自行查询SDK或执行任何host来填字段。

## 其余有限流程核对

### 固定源与阶段数量

- P0按批准L0182逐文件复制，再加D十源及固定CLI配置形成workflow193；cleanup保持独立182，两root为兄弟。没有复制bin/obj或从WIP glob源码，CLI包/tool_root只读且与可写home分开。
- N0新cache初始空，实际SDK版本查询及L0/D各一次force-evaluate/use-lock-file restore为3个事件；只允许新增两host lock，得到195。B0从锁后源复制到新的replay，SDK+L0 restore/build+D restore/build为5个新事件；CLI SDK/runtimes/version另存3个只读事件，不混称startup执行。
- BC从独立182加原接受L0 lock形成183；SDK+locked restore+build为3个新事件。独立root/provider产物自行核验，不要求与workflow不同编译路径的provider DLL字节相同，不借旧L0 build晚加标签。
- 所有SDK入口使用批准host、可写task CLI home、隔离NuGet/HTTP目录、单节点/no reuse/no shared compilation及disable-build-servers；restore加disable-parallel。环境由冻结public helper白名单产生，无TEST wrapper、secret或TP07ProviderProject覆盖。

### Design依赖与原生内容证明

只读重算原Specification lock `685ecc8f…` 的Design有向闭包，确为33个非驱动包；provider13全部在其中，resolved/dependencies/contentHash交集一致，差集20。这里是静态图核对，**20新包native证明仍pending**。

脚本将原33条record与原锁逐项核对，固定Design archive，核原archive/nuspec/metadata/sidecar引用与内容，再复制33+W到local-only两个feed；配置clear packageSources并按精确ID映射。新cache由NuGet实际恢复，逐项核actual lock、native metadata/sidecar与原锁，以及原/archive→feed→fresh cache字节/nuspec。没有用raw SHA512重造NuGet contentHash，没有写global cache或恢复全Specification项目。实际新增包/版本/依赖差异会停而保留生成的lock和失败收据。

这些判断基于源码，不能提前填写native_restore_proved、实际33图或缓存新鲜性已经验收。Low还须检查真实restore输出及三/四active assets的实际ProjectReferences，特别generated图有D和provider两个Project，不能套单Project模板。

### G6/G12与历史输入

`build-generated.py`只处理真实pending parent，绑定report/execution/startup/build各自外部SHA、同profile/source_pack、active ledger及两种snapshot。生成子树限定同原workflow root的nonce目录，精确6/12源码；先核旧195/202源、active locks/assets、完整旧binary hashes并复制父binary快照。

G6首次native lock后locked restore/build形成202；G12保原generated lock，仅locked restore/build形成208。两者显式BuildProjectReferences=false，仍在restore/build后核原base source/assets/host二进制，不能把该flag本身当保留证明。原generated consumer可更新，其旧字节已有新phase快照；其它host/provider须保留。

新source/lock/startup文档写新外部phase路径，source/lock集合与startup相同，继承old字段仅更新有限集合、generated consumer身份及parent/closure字段；parent文件、报告和ledger加入protected before/after。没有为满足旧路径而覆盖parent manifest，也未在startup增加phase、binary_hashes或build_receipt_sha256。

### consumer与运行权限

除B01外，task名称、source_pack身份、source/lock配对集合、startup assembly字段、build receipt、固定CLI六文件层级及helper pins与当前consumer静态接口对应。实际构建后的DLL/MVID/deps、source hashes和日志内容仍须Low独立核对；AST可解析不等于任何SDK执行成功。

脚本不产生真实18字段execution plan或E/model accepted文件，不运行scaffold/migration/受验startup。现有E前置门仍未解除；准备完成及offline通过都不会授予D DB窗口。当前E对象检查/恢复由独立任务负责，本报告没有清理或状态判定权限。

## 下一步

High仅修B01及配套schema/来源证明后停写、新封四文件，主审consumer接口差异；通过后才由root安排Low的P0/N0和后续锁图review、B0/BC、CLI只读事件。G6/G12必须等待未来真实D pending checkpoint与再次批准。保留本轮冻结，不通过弱化consumer loaded-hash检查或复用旧构建事件凑阶段数。
