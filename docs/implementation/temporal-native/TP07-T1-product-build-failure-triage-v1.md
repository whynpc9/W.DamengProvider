# TP07 T1产品首次build失败：单位源严格分析器窄修设计

2026-10-10。结论：**unit_source_compile_analyzer_failure_confirmed / single_unit_file_revision_ready / build_units_pending**。实际4类8个错误全部位于新增`DamengRelationalTransactionCapabilityTests.cs`，不要求修改三个产品DI/adapter/factory逻辑、W、依赖或全局编译规则。待root确认Low完整失败snapshot后，由High在新隔离产品revision仅修该unit文件，再重新绑定source/native/build门。

本轮仅只读compiler log、失败manifest/control和冻结源，计算hash；没有执行SDK、tests、validator、DB或wrapper，没有改源码、旧失败或status，仅新增本文。此前静态源码接受未识别这些严格分析器错误；以本次真实编译事实补足，不把静态API审查当作build通过。

## 实际失败证据

`TP07-T1-product-v2-build/manifest.json` SHA`ece074975c31ec7550fd99d8be5d8ae134e0e373d782109f2745985afaeb5f71`记录SDK0、locked-restore0、build1；三者均自然退出、无timeout/termination，child已reaped、group absent。build耗时2.032874708995223秒，原300秒执行界限保持。`Stop/natural_process_success300`是外层对exit1的拒绝，不是timeout或取消根因。

实际build.log SHA`b5fcbebe85e7eed0aa730fd744920e33a2c0cc3bed1b7b8c7d5ae388d2eb5ef3`；重复summary行去重后恰8 errors。编译使用的unit文件SHA仍为`c7b5520f6a5863af5924f94a6d5a913317a164e31d6a90d18c7fd47f37eb5220`。Directory.Build.props SHA`21806cf673ba37d0eef9d76c316fc37d2cbedb2527101c617e5a722bc913fb67`明确net10.0/C#14、Nullable enable、TreatWarningsAsErrors=true、AnalysisLevel=latest-recommended；这些都是应保持的仓库合同。

Low已保存完整失败control：

| artifact（相对`.local/temporal-native/verification/`） | SHA256 |
| --- | --- |
| TP07-T1-product-v2-build-control/independent-readback.json | `eb7e4f18e54ee3b094305b385298322d774a0d6bd0a4e79e7d94301cac56ec28` |
| 同目录build-artifact-hashes.json | `f7244009ee9ed8800e44baeab6038b75e66bd182c83edea2e7c8c8d1721afeab` |
| 同目录execution-receipt.json | `62c8652a3a856fc9fc9ad7609409a1e844d20b8780dc4a67b2e1134737dc9599` |

独立readback实际记录7892项prior pins不变、source178/两个locks不变、locked raw4完整比较及272项pruning/header保持，repairs_or_retries=false、build_accepted=false、window_released=true，units/host/DB未执行。本reviewer读其实际内容，不冒称再次重跑7892项全集或native validator。失败manifest的四raw refs保持实际输出，不回写为新成功。

## 唯一源文件的四类修正

| 实际位置/错误 | 根因 | 最小修正及必须保留的含义 |
| --- | --- | --- |
| 236:29 CS0108 | TestConnection.Events遮蔽继承自Component的Events | 重命名为`OperationEvents`等明确名称并同步本文件所有读写；不使用`new`掩盖无意隐藏，不丢create/batch/rollback-to/release顺序断言 |
| 31/32/40/84/85:9 EF1001 | 测试有意通过Assert.IsType引用本provider内部基础设施具体类型 | 对这五个必要强类型断言加最小局部`#pragma warning disable EF1001`/立即restore及说明：在固定EF10.0.12下验证正式DI factory/connection/adapter类型；不包住整个文件/类，不影响其它分析器 |
| 181:26 CA1861 | 重复测试调用把常量`new[] { "batch" }`直接传Assert.Equal | 提取本测试类private static readonly字符串数组，断言仍比较完整单事件序列；数组不被测试或fake修改。supported成功/失败数组顺序保持，可同样命名为只读期待字段，但不减少断言 |
| 292:35 CA2215 | TestTransaction.DisposeAsync显式调用Dispose却绕过base.DisposeAsync契约 | 此override没有额外异步语义，删除它继承DbTransaction.DisposeAsync即可；若保留则直接返回base.DisposeAsync，不先重复调用Dispose。继续让Dispose(bool)的一次性计数和base.Dispose(disposing)执行，保留owned1/unowned0断言 |

EF1001局部标注是该测试**必须验证具体注册类型**的有限例外，不是生产代码绕过兼容性。优先保留`Assert.IsType<...>`，不要改成只比FullName字符串或去掉DI类型断言来躲避分析器。现有public接口已经承担行为调用，但无法单独证明新factory/adapter确实被注册，不能用弱断言替换它。

不得修改Directory.Build.props、csproj、editorconfig、全局NoWarn/TreatWarningsAsErrors/Nullable/AnalysisLevel或关闭analyzers；不得忽略整个EF1001文件范围，不抑制CS0108/CA1861/CA2215来保留错误实现。若实际新build再报其他具体错误，仍先保存真实失败，再由High定点修，不由Low临时降规则。

## 行为和25场不能缩减

三个Theory各8行加一个Fact继续恰4方法/25场，不改测试名/filter/InlineData组合。sync/async BeginTransaction及UseTransaction、true/false SupportsSavepoints、commit/rollback、owned/unowned Dispose、同singleton factory两个底层事务能力隔离，全部保留。

真实DI IBatchExecutor也保持：支持时create→batch→release，异常时create→batch→rollback-to→release；不支持时仍执行业务且无savepoint命令，保留同一原异常、实际sync/async执行次数/同underlying及最后fullRollback work恢复、Dispose计数。更名/常量提取不得改这些event值、数量或work状态。不通过删除failed分支、把expected count放宽或仅测试getter实现来换取编译绿色。

base.DisposeAsync方案仍需Low实际sync/async生命周期场证明，而不是以“继承默认方法”推断已通过。真实服务器激活门继续独立，不因纯fake处置修复接受TLS或共享事务。

## 新178 revision与versioned交接

High仅从已冻结产品178 map复制到**全新隔离root**，只改该unit文件；其余177源（包括三个产品文件、Directory.Build.props及所有依赖锁）逐项保持。产品三个逻辑hash继续DI`a407f84a…`、adapter`95ecb94f…`、factory`86333f2c…`。外部dirty EF checkout、旧sourcefreeze、SDK失败work和旧manifest不动。

生成新的versioned transaction-capability handoff patch/manifest/README：新patch仍从原批准175 base应用，包含相同DI+两个product以及修正后unit；新manifest固定source_count178、新完整source_root/hashmap/aggregate与新patchSHA。验证“相对v1仅unit变化、相对175仍174不变+DI+3新增”两个关系，不能因计数仍178而复用旧source identity。

保留原manifest`932f8f41…`、patch`1752dc64…`及root source-only approval`ac8ff44f…`。root在Low新静态178/patch readback及源码复审后，签新的七字段source-only JSON绑定新manifest；该批准不需要提前单位结果，也不授权SDK/DB。准备工具如其PINS硬编码旧产品manifest/review/freeze/approval，必须在新工具revision中只更新明确引用并重审，保持已接受v2取消/raw图算法，不在旧脚本上改固定SHA或接受任意ref。

Low按新source→native→build链在新目录重新生成真实证据：source178新map、同SDK10.0.401的实际force-evaluate49 non-W+W+Project51图/两锁/50包/raw4，再新locked restore/build三事件。包/依赖/W预期不变，但actual native/build及阶段tool/source引用必须重新闭合；旧unit graph可作为固定基线，不是本次新source执行的替代结果。不得直接从当前失败build继续compile并把旧source manifest称为新测试源。

新build自然成功并独立核provider/unit DLL/MVID/deps、完整输出与before/after后，才进入固定target25 TRX门及同build full-unit TRX门；原失败的exit1、8个error、raw输出和完整snapshot永久保留。后续probe源/输入和runtime approval也必须改绑新178 identity，即使产品逻辑未变，不拿旧manifest静态成功旗代替新编译测试。

## 授权与停止线

本报告提供单unit文件修复设计，未实施。root先确认Low失败snapshot/readback完整保存并释放窗口，再限定High新revision及交接文件范围；High停写冻结→Astra源码复核→Low新静态/source/native/build→target25/full实际门依次推进。无新W包、无能力扩张、无TEST/SA或DB操作需要因本编译错误触发。
