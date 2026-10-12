# TP07 T1 probe v7：两个事务观察 lambda 的局部非空引用

2026-10-11，Astra High 只读设计。建议在 capabilities 与 transaction_SaveChanges 两个 lambda 的首句各声明 `IDbContextTransaction activeTransaction = transaction!;`，随后该 lambda 内全部事务读取使用此局部引用。选择纯 null-flow alias，不增加运行时分支/异常，不改外层事务状态机。root 读取并接受本文后，方可授权 High 编码；本文没有执行源码修改、imports/tests/SDK/DB。

实际最小 tracked 范围为 **host/Program.cs、test_contracts.py、README.md 三文件**。prepare.py 没有 Program/host 硬编码 SHA 或 producer15 aggregate，它按外部 toolpack/probe_source_manifest 实际字节校验，因此无需为此次修复改动 prepare.py。此范围在原“最多四文件”上限内；其余14文件应保持 v6 字节。新本地 handoff、17pack/host9/inputs 制品另行绑定，不修改旧版本。

## 确证失败与历史身份

本轮读取实际 v6 failure、一次诊断安全错误与独立 readback、Program 全部相关 Step、prepare 的实际来源消费、test producer15 pins。当前 tracked17 仍逐项等于 v6 freeze。下列除原始日志由 root/诊断制品绑定外，列出的 JSON 与源码 SHA 均已本轮实际计算匹配。

| 制品 | SHA256 |
| --- | --- |
| v6 host/Program.cs | `0ab8ad1210423b3f5bd3db753fbfdea61d915caeb2bb5bf4758d92f21dd49303` |
| v6 prepare.py（建议原字节保留） | `dd81f63fdee26fa4e50d3515e906bb1b321c0d5cc73712c1c94a989ab908167c` |
| v6 source/source.json | `13238cc00e176488144d426deb8c159623c51a7bd2a03a7c21dd4a289648cf96` |
| v6 native/native.json | `52003b01f66dbafe2ad508f60bdfd218c7fa529fb1d5982dc1f1de4cbf2c05c0` |
| build-control/failure-manifest.json | `c76f49ac669514182225485a04a14fff4dc2b46990aad800e12ae62a4aaaea24` |
| build-control/independent-failed-readback.json | `f32d79af0ef87eb70a3b60669f9e9c925aa64ba0a45f17660f0df8a7399683ae` |
| diagnostic-control/safe-compiler-errors.json | `40b3abf9cb58e5e7e7886abe18917efa5a3bb186a93043d9bf60841fc228b0aa` |
| diagnostic-control/independent-readback.json | `cf9473670bcc879d0dd112ad5b6114bb642e3198aed764086d31d87ff3f1e605` |
| 一次诊断原始日志 | `ba9db18c9d06ac856f51721b7238248e84bd87960ee2a74b51c3f95fa916697d` |
| v6 contracts/verification-summary.json | `587b27882aff32b34fe5138712f0040e653545a46c744d1bca918a4de9069e51` |

本地目录前缀是 `.local/temporal-native/verification/TP07-T1-probe-v6-` 加 source、native、build-control、build-diagnostic-control、contracts。原四条 SDK 命令 exit 为0/0/0/1：两locked restore及provider build成功，最后host build失败；无startup.json/build.json。独立一次诊断保持同冻结host，natural exit 1，bin/obj变化0，17703保护项未变，准确定位两个CS8602：Program.cs(75,1233)、(78,736)，实际列位置均为 `transaction.TransactionId`。

v6原两处Identity(transaction!)消除了先前参数CS8604，但未覆盖捕获的nullable外层transaction在两lambda后续读取中的流状态。该外层变量用于Begin赋值和后续Dispose后reset为null，不能简单改成全方法非nullable或取消nullable检查。此问题不说明产品178或W交易逻辑改变。

v6实际Python66通过、source187/native通过仍保留对应历史意义；失败链原39 partial bin/obj、native immutable raw4与failed live raw4均不修改、不转成新build成功。旧v5失败/诊断/raw证据也原样保留。

## 唯一修复形状

使用已存在的 `Microsoft.EntityFrameworkCore.Storage` using，不新增依赖。两处局部同名但分别在各自lambda作用域，示意为：

```csharp
await Step("capabilities", "actual_getters", () =>
{
    IDbContextTransaction activeTransaction = transaction!;
    // 原有语句顺序不变；此lambda的事务操作全部使用activeTransaction。
});

await Step("transaction_SaveChanges", async ? "SaveChangesAsync" : "SaveChanges", async () =>
{
    IDbContextTransaction activeTransaction = transaction!;
    // 原有语句顺序不变；此lambda的事务操作全部使用activeTransaction。
});
```

以上仅说明局部插入位置，省略号式注释不进入实施代码，原lambda的完整正文/返回值保留。每个现有lambda恰8个外层transaction标识符读取（不计字符串/报告字段）；修订后各有8个activeTransaction读取，外层transaction只在首句初始化读取一次。各原先分散的 `transaction!` 替换为 `activeTransaction`，不再在使用点叠加 `!`。不能只修两处TransactionId而遗漏同lambda其余nullable消费。

| lambda | 应全部改用同一局部引用的操作 |
| --- | --- |
| capabilities | VerifyTransactionService(adapter)、GetDbTransaction、与CurrentTransaction的ReferenceEquals、CandidateGuard.Identity、ObjectId、TransactionId、报告SupportsSavepoints、最终Require的SupportsSavepoints |
| transaction_SaveChanges | 前置Require的两个事务getter、取得underlying、CandidateGuard.Identity、ObjectId、TransactionId、与CurrentTransaction的ReferenceEquals、报告SupportsSavepoints |

局部仅绑定同一个既有对象，不创建wrapper/新事务，不读取另一context替代transaction，不从underlying推算EF对象。保留两次观察的actual getters、对象/GUID/reference关系、underlying/connection/context IDs、read_sequence、字段名及SafeEvidence条件。SaveChanges、tracked行赋值、返回结果及await位置完全保持。

非空依据是现有有限控制流：begin_transaction Step成功完成后才进入capabilities，成功Begin返回事务并赋给外层transaction；Step异常会throw并跳出后续执行。tracking_query成功后才进入transaction_SaveChanges。两lambda的事务观察在各自首个相关await之前完成，其间不赋值/清空外层transaction；outer变量只在更晚dispose Step中清空。局部声明集中表达此证明，避免对捕获可变nullable变量的多处流状态推断。`!`只影响编译期nullable分析，没有运行时null检查。

不采用 `transaction ?? throw ...`、新增SafeEvidence.Require(null判断)、fallback/重建事务或更宽catch。它们会增加原任务未要求的运行时分支/异常行为，不能替代此纯alias修订。若新SDK仍有独立错误，保持失败并重新诊断，不能关闭Nullable、扩NoWarn或禁分析器。

Program的修改边界严格只有这两lambda的首句和16个变量引用。begin_transaction、tracking_query、query_inside_transaction、full_Rollback、dispose_transaction_context、fresh_after、外层声明/赋值/reset与finally逐字保持；txConnection、context、tracked的既有使用及suppression不改。ProbeContext的OwnedEntry修复保持v6字节，不再改名。

## pin 与文件归属

| 文件/制品 | 最小改动 |
| --- | --- |
| host/Program.cs | 上述两局部alias和各8处读取 |
| README.md | 追加v7窄修/新执行门，准确保留v6失败历史 |
| test_contracts.py | 在v6来源之上新增有限V7 producer override，仅当前Program与README SHA；更新producer15 aggregate及当前fixture版本选择/标签 |
| prepare.py | 无内联Program pin，保持v6 `dd81…`；既有toolpack/host9外部Ref消费自动绑定新字节 |
| 新ignored handoff | 新三文件diff、完整17/15/host9实hash、v6父冻结与本设计Ref、66方法AST对比、无执行声明 |

V7_PRODUCER_OVERRIDES应继承V6，而不是重写/删除V4/V5/V6来源；只覆盖Program/README两项。已有v4两个host override、v6 ProbeContext/prepare pins继续继承。producer15排除validate/test，不把当前测试自身SHA放进自身pin。新的17aggregate和host9aggregate由实际完整文件重算，不能复用旧v6值。

保留原66方法名称与AST逐个相同，仅current producer fixture helper的版本pin/标签切换；没有发现需要新增行为测试的缺口，因此不新增测试方法或模拟新场景。不能以原66 AST相同宣称新pack已通过；Low必须运行完整新pack对应66。

其它14tracked文件全部冻结，尤其validate.py、新v6 raw helper/contracts、prepare native/build/readiness、run.py/policy、process/CLI13/SDK10字段receipt/180+2、snapshot字段集、host其它8和helper5。完整JSON五true变化、四native immutable+四locked immutable+四live raw+两live lock/真实startup路径的v6契约均保留，不趁nullable修订重写或放宽。

## 冻结、验证与推进顺序

1. root读取接受本文后仅授权High上述三文件；High不执行SDK/import/tests。新版本冻结前完整声明实际17/15/host9 map、三文件diff、66方法AST、两lambda准确变动与其它14字节不变，然后停止写入。
2. Astra新source review核对该冻结；Low独立生成新实际17pack/host9 snapshot并回读。源审与静态快照不构成build或Python接受。
3. root另授Low执行新完整66，精确方法集合、0fail/error/skip、实际process/前后保护须读回；不只沿用v6 summary。此轮没有新增方法，预期集合仍恰原66。
4. root绑定新toolpack/host9/inputs lineage后，新source187 → native → locked build串行门。产品178 map/manifest400123/newsourceapproval66c保持，host只有Program字节新值；source187与locked188路径/数量不变。新replay/newnative/build从新输入产生，不能仅在旧failed replay覆写Program重编再称整链接受。
5. 新build必须原两locked restore+两build均成功，核完整host/provider/EF/W DLL/MVID及binary map、真实两lock before/after、八immutable/四live raw完整有限关系、source/hash与startup真实路径。Low整窗口独立保护全部raw含dgspec；原run.snapshot不含dgspec的事实不变。
6. 新build实际接受后另授compiled28 + 新Python/offline/readiness；compiled28是实际C#宿主门，Python66不能替代。readiness仅ready_pending_runtime，runtime4仍须root单独授权并按原四场顺序、TEST身份、TLS与精确cleanup独立验收。

产品178、W原包、已接受retry49/transaction25/full824均无源码或包身份改变，**不因本次probe null-flow修订重复产品full824**。这些产品结果也不替代新probe build/compiled28/readiness/runtime。所有v6及更早接受/失败制品与报告按旧hash保留，不重签、不覆盖。

完成本文后停止写入。当前没有新代码或执行接受。
