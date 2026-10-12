# TP07 constructor SCREEN25 静态审阅

2026-10-09。结论：**scoped_static_accepted_for_Low_preparation_and_offline_gates / runtime_pending / literal_support_not_accepted**。未发现阻断本轮有限characterization的确定代码缺陷。仅只读审阅冻结的六个新增文件、八个既有helper及必要固定W源码；未运行SDK、合同测试、validator、producer或DB，未改实现、旧L0失败或接受旗。

范围沿root批准的SCREEN25裁决：先四个paired typed7控制，再原mapping literal与限定TO_TIMESTAMP/TO_TIMESTAMP_TZ/CAST文本候选；只shared、两张新owned表、普通日三个fraction，不扩极值、tail全集、offset全集、默认/seed上下文，也不进入E/D。设计依据 `TP07-L0-literal-loss-triage.md` SHA256 `28c41d15dc6e85cc9cc9f5a44d62d8a9bdf9e7dcd95bcd1dfbbe17748fe28856`。

## 固定输入与真实写入链

`Cases.cs`和Python独立policy按相同明确顺序展开25项：C0的DT tick1/micro、DTO tick1/micro共4项首先完成，然后B0 DT/DTO、D1/D2/D3 DT、Z1/Z2 DTO各三个fraction（1、1234567、1234560 ticks）。Python重新算日历整数/ticks/ns/UTC与case JSON比对，不只相信producer required IDs；重复、缺失、重排或增加case均失败。

两张表分别为V TIMESTAMP(7)与DATETIME(7) WITH TIME ZONE；所有同kind的control与expression落在**同一新表**，同一个冻结 `LiteralOracle.ReadFresh`直接读取V/TO_CHAR(V,FF9)，没有给control另换读法或把表达式结果CAST后当原列。每case先fresh证明目标ID不存在并取总行数和catalog，写后fresh核该行、总行数增量及相同catalog。两表由现有common nonce ledger/owner marker精确管理，没有跨Schema或旧对象操作。

C0使用同包真实W参数DateTime2/DateTimeOffset、显式Scale7/Precision0，是明确标识的typed ADO控制，未伪称EF SaveChanges。B0从实际EF service选出的mapping调用GenerateSqlLiteral，检查mapping Precision7/store type/实际provider assembly，并保存返回的literal/hash；Python要求其与已冻结mapping-v4的精确字节形状一致。D1/D2/Z1/Z2直接将固定普通字符串传显式FF7/FF9；D3直接CAST字符串为TIMESTAMP(7)，不先经过已丢位的TIMESTAMP literal。没有加入未证TIMESTAMP(7) literal引导符、DATETIME2FROMPARTS、FRAC_SECOND tick运算、浮点日期运算、设置修改或新产品hook。

每个typed控制本身必须exact，否则立即失败。第五项之前再次要求四个前置控制全部完成且exact；Python也逐项独立要求控制为exact，producer的 `paired_controls_exact=true`不能单独授予通过。

## exact、LOSS和错误边界

冻结C# oracle先捕获普通row原始cell，在typed getter之前记录来源/type/width/scale/masks，再交叉FF9和typed local ticks/offset。任何不可观察、布局错误、raw/FF9/typed互相矛盾都抛硬失败。只有三者相互一致但与请求不同，producer才可标`loss`。

Python `observation`先用固定TP06独立packet decoder从raw bytes重建实际tuple，再让冻结value oracle验证descriptor、hash、FF9、typed ticks与DTO原offset/UTC全部一致；最后与独立case期望比较，自己决定exact或loss，并要求producer标签相符。它不使用producer decoded字段或`persisted_exact`旗决定结果。所有control loss、把loss改称exact、raw/FF9/typed不一致、错误offset与scale都被合同明确拒绝。

expression的`server_rejected`只来自实际Execute调用捕获的精确DmException：verified server response、negative number与FailureInfo一致、ServerReported、ConnectionReusable、CanPreserveSessionAfterServerError、CancelSource None同时成立；numeric response须在本次执行窗口属于同command和实际statement引用，并与该exception的VerifiedResponseIdentity对应async send相接。之后须原连接仍Open并实际SELECT1成功、fresh目标rows0、表总数与catalog不变。Python检查内层receipt互相一致及其在全局frames/sends中的唯一成员关系。不把返回SQL错误但旧连接已退役的路径自动纳入可继续分支。

**该分支是有意很窄的。** 固定W `ThrowOwnedStatementServerError`的preserve规则只涵盖特定实际上下文（包括-2106、shared、活动事务等）；本probe不新增事务或放宽它。一般语法/未知函数错误可能导致screen硬停，不能为凑25项把它改成可接受server rejection。`screen_completed=true`只表示25项已按规则采完，包含LOSS也不代表任何literal支持。

settings仅执行冻结的三个参数白名单SELECT，分开保留系统/会话/文件角色；只输出有限数值/枚举，非法cell为空且显式unknown。真实verified readonly server拒绝可使settings保持unknown，timeout/transport等仍失败；没有fallback、grant、SA、session/instance设置修改。unknown不推出IFUN=0，也不用于筛选表达式成功。该辅助query的连接在Settings作用域结束时dispose，之后创建/写入使用新连接；unknown不声称旧连接可复用。如实际错误/Dispose未满足当前严格处理而导致硬停，保留失败交root，不为完成screen预先扩大例外。

## 身份、外部收据和清理

新增项目路径为 `test/TP07ConstructorProbe/`；AssemblyName沿固定common允许列表使用`TP07LiteralProbe`，只是兼容名称。producer传本次startup assembly给CandidateGuard，实际验证Location字节hash、MVID、deps和实际W/EF assembly，report task固定 `TP07-constructor-screen`。**不能复用旧L0 DLL、旧startup manifest或旧runtime-template manifest。** 新validator要求运行DLL在新constructor项目输出目录、完整新14文件source pack和明确7项compile mapping。

新source pack只包含6新文件+8固定helper；外部manifest由Low/root提供hash，不能以文件自指替代审批。`runtime_template_source`必须指这个新14文件manifest，保留原base/bridge/mapping/EF175来源；新增compile恰为项目三文件、common三文件、LiteralOracle一个，prepare182，新增单个constructor lock后183，provider lock及13项非驱动依赖保持。只有hash匹配不等于重放成功，validator另要求外部prepare独立重放收据、真实SDK10.0.401的lock/build命令日志和退出码、source/lock/assets/deps/binary pre/post及实际运行身份链。

新增代码没有standalone runner。Low后续外部prepare/lock/build/run收据必须完整填充validator要求字段并经root审阅；本文不替代尚未生成的外部收据。尤其构建只针对新项目，设置task DOTNET_CLI_HOME和既定单节点/禁build server参数；不能因为startup名称相同复用旧L0产物。

主进程finally始终尝试ledger cleanup；独立`--cleanup`重新核manifest、ledger hash与两表有限范围，再做cleanup及finally fresh absence。Python要求三份清理收据（主finally、独立cleanup、独立finally）与同一最终ledger/hash绑定，main/独立进程自然退出0；外部协调者即便主程序硬失败/超时也必须安排已授权的独立清理，不能等完整validator成功才清理。现有回调仅在这个独占短生命周期进程内使用，不能将此实现复用于并发/长驻宿主；本轮结束靠进程退出释放静态订阅。

原L0失败主报告SHA `ed5310c8feccfb0705b85a4bda12333a3ffd60a5ae9ea353820e37486757290d`是外部run收据的必需历史输入，并核before/after/current hash。characterization完成/退出0仅表示采证与清理完成，独立结果始终 `candidate_acceptance=false/full_EF_C_accepted=false/literal_support_accepted=false`。原L0失败不能被此屏幕的LOSS记录冲销。

## 冻结文件身份与独立验证边界

六个新文件均实读匹配root交接：

| constructor-probe文件 | SHA256 |
| --- | --- |
| Cases.cs | `883fc3cb0001d1c289d1b8059190daa93cbe9af3780a9ccfe27cb3c738c7b345` |
| Program.cs | `9367619602ffff34047dd822c8ef8963e7fa16b71676b555c1fc3ed8dce02b1f` |
| TP07ConstructorProbe.csproj | `f97350a514c3bd5654205336c04c19f8904e184011f63bdb46ac0c8f5fe6c7b8` |
| validate.py | `f947ee71462ff1d39003eb653bbafdc37380a463f41f591b96998d625f435ce8` |
| test_contracts.py | `091090d49035484ba3b40a20b2939230a51558189af938c63e62325f88f007f1` |
| cases.json | `c4a958cba80fe9ed9405c0def7ad6c2fd5ef41d8d0fa10205df31a01663271c4` |

八个helper（common3、LiteralOracle、managed_identity、packed_oracle、旧runtime-validate/runtime-cases）实读hash均与cases.json固定值一致。它们只复用指定value/identity/cleanup函数，不调用旧L0的完整接受入口；source pack检查也覆盖纯Python helpers以防借未冻结代码判结果。

报告收尾实读Low新freeze：`TP07-constructor-v1/source-pack-manifest.json` SHA256为 `7a909fb21e72fea1aa912b03ac8a5f638bd1a534747f7ad4788806163b16c75a`，恰14项且全部live字节匹配；`source-manifest.json` SHA256为 `319394ab08d24d514900d351c64a7cb4c2e588c874e9aea004753760713bc75b`。本轮只核其身份，独立重放/lock/build及外部收据仍由Low按后续窗口执行。

已静态阅读10项手写合同测试：固定集合、exact/LOSS、控制拒绝、verified server错误关联、raw/FF9/typed/offset、表达式与mapping来源、完整screen/清理/身份、settings unknown、SDK收据、14文件/phase漂移。它们是synthetic形状验证，**本文未执行，不能写成10/10通过**；Low必须在冻结14包上实际执行并保留失败。随后新lock/locked build和startup身份门通过后，root才可另开SCREEN25 DB窗口，首硬失败保留/清理/停止，不扩大输入或自动修复。若screen发现存活表达式，极值/offset/默认等后续门仍须另行批准，产品literal patch尚未授权。
