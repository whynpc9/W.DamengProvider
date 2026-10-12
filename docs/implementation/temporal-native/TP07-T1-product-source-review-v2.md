# TP07 T1产品源v2：单unit文件修订验收

2026-10-10。结论：**T1_unit_only_source_revision_accepted / actual_new_build_target25_full_pending**。按首次build分诊`2f94ff36…`核对四类修正，未发现源码阻塞，无需扩大产品逻辑范围。源码接受不等于编译或25场测试通过，也不接受probe/runtime。

本轮只读新旧178源、patch/manifest/README及旧失败收据，计算hash、比较差异并在内存重建patch；未执行SDK、tests、validator、DB、wrapper或磁盘patch应用，没有修改source/status/旧证据，仅新增本文。

## 冻结身份与精确范围

新root为`.local/temporal-native/verification/TP07-T1-transaction-adapter-v2/ef-source`。

| 输入 | SHA256 |
| --- | --- |
| unit：DamengRelationalTransactionCapabilityTests.cs | `f4f689c8c2285ed4665880d65f0ccbd22424862dc1ac9eef1fc0cc69b44dad1d` |
| 完整178源aggregate | `a0c14e430c72ede7deb257fee16e175b9d06b1862abec59f417aedc7bd6f8724` |
| patches/transaction-capability.v2.patch | `5df9c73a4879fbf7f2cb79da48379a92856e093fecea7df3c31956080b1f3566` |
| patches/transaction-capability-manifest.v2.json | `fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432` |
| patches/README-transaction-capability.v2.md | `3e0f6c3a772682ab8de4ae08f68ab1a4a86718d752ee75b24198409fec23009d` |

实际new root恰178文件，完整map逐项hash匹配，aggregate按声明排序格式独立重算一致。原v1完整178文件当前也全部匹配旧map；相对v1仅unit文件变化，177项同字节，包含DI`a407f84a…`、adapter`95ecb94f…`、factory`86333f2c…`、Directory.Build.props及两锁。相对原175 base仍174项不变、一处DI修改、两product和一unit新增，没有新增依赖或W修改。

新patch仍是相对原批准175的**完整交接增量**，本轮逐hunk在内存匹配原文件并重建四个目标，四个重建hash均等于new source map；没有把unit-only父差异误作可直接应用于175的完整patch。新manifest的revision2、具体source_root/map/count178和unit_delta_from_parent/父refs明确；12条展开父/lineage引用实际SHA匹配，old_parent_source_approval_reused_for_revision=false。旧932f manifest/1752 patch/30c451 freeze/ac8 approval仍是历史父证据，不是v2批准。

## 四类窄修成立

- `TestConnection.Events`及其所有读写均改为OperationEvents，已无旧Events成员引用；保留create/batch/rollback-to/release事件值、添加时点和序列比较，解决Component.Events无意遮蔽。
- 单batch期待数组提取为private static readonly `BatchOnlyOperationEvents=["batch"]`，只有完整Assert.Equal使用，没有修改该数组的路径；supported成功/失败期待序列未变。
- 五处必要`Assert.IsType`强类型DI/connection/adapter断言各自单条`#pragma warning disable EF1001`，紧接restore，逐条有固定EF10.0.12基础设施验证意图注释。没有整方法/类/文件抑制，没有其它规则关闭，也没有改成弱FullName字符串检查。
- 删除无额外语义的TestTransaction.DisposeAsync override，继承DbTransaction默认异步释放；原Dispose(bool)的`_disposed`一次性计数和base.Dispose(disposing)保留。没有先手调Dispose再重复base调用；owned1/unowned0和fullRollback之后Dispose计数断言不变。继承路径的真实测试结果仍待Low。

Directory.Build.props字节不变，Nullable enable、TreatWarningsAsErrors=true、latest-recommended分析器及C#14/net10.0保持；csproj/editorconfig、全局NoWarn/依赖没有改动。修复没有降低严格编译门来掩盖错误。

## 25场及行为边界

新旧Fact/Theory/InlineData声明和顺序逐项相同：4方法、24个Theory数据行+1Fact=25场。实际DI sync/async BeginTransaction/UseTransaction、true/false能力、commit/rollback、owned/unowned、同底层transaction、singleton能力隔离的断言均保留。

真实DI IBatchExecutor的synthetic业务路径仍包括支持时create→batch→release，失败时rollback-to→release；不支持时无保存点但仍执行batch；同异常对象、sync/async执行计数、同IRelationalConnection/底层事务、fullRollback恢复work及最终Dispose计数不减。没有删除失败场、改变期望受影响行数或用新getter自身镜像测试替代行为。三个产品适配文件字节相同，继续正式DI工厂、同DbTransaction SupportsSavepoints转发和基类生命周期，不扩张TLS保存点或七位能力。

## 旧编译失败与partial provider继续保留

旧build manifest当前仍`ece074975c31ec7550fd99d8be5d8ae134e0e373d782109f2745985afaeb5f71`，独立readback`eb7e4f18…`、完整artifact manifest`f7244009…`不重标。新增已保存的safe-compiler-readback实际SHA`649ffdfeeec1da9361fadaf6445721232ca572ceff518e38495d123d1816580b`明确窗口释放、子进程组最终absent、未repair/retry或执行units/DB。

该旧失败虽产生provider DLL`12edfa611df90e6b91029b41b385d1e7bfbffa81ceab845836d51a5bbe8d1247`/MVID`a7cc56b3-479c-46b7-9677-ed631f97374a`，收据明确not_complete_build_or_runtime_acceptance=true；它只是partial产物，不能作为v2完整provider/unit构建证明。原4类8 errors及7892项保全事实保持，未拿“产品逻辑没改”跳过新build。

## 下一依赖门

Low可先对v2新178/map/fullpatch做完整静态snapshot/readback；root随后签新的实际七字段source-only approval，绑定v2 manifest及178 exact paths，scope仍transaction_capability_overlay_only，不以旧ac8代新批准，也不要求尚未执行的单位门作为静态批准前置。

准备工具需新pin-only revision接新manifest/sourcefreeze/source_review/approval，继承已接受取消、raw native图、50包/51records、source/native/build/target25/full守卫；新输入和工具source review后由root分别开实际窗口。所有阶段以新source identity重新source→native→locked build→target25 TRX→同build full units，旧cache/锁/graph仅作固定比较基线，不冒充本次新执行。

新真实编译结果及25场/full结果仍pending。probe及后续L0/E/D准备/运行身份需正确改绑新revision；本报告没有审阅或授权这些入口。外部dirty checkout未作本次操作对象，旧产品/失败证据不覆盖。当前最小进一步源码修复范围：无。
