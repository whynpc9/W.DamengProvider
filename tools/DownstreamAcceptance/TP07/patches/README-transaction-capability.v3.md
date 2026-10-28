# T1 产品源 v3：W 重试合同测试

> 历史测试合同修订记录：只调整固定 v2 副本中的 W 重试断言，保留旧官方错误号和失败证据；源码修订不等于构建、完整套件或实库通过，也不代表当前 EF 行为。

从已审 v2 manifest `fcb1d60f…` 的实际完整178复制到新隔离 `TP07-T1-transaction-adapter-v3/ef-source`，仅修改既有 `DamengExecutionStrategyTests.cs`。其余177文件逐字不变，包括三个transaction产品文件、transaction unit `f4f689c8…` 的25场、原bridge retry产品、props/global/两locks；W产品、候选包、cache、外部dirty EF checkout和旧源/handOff/批准/失败TRX均不改。

正式triage `TP07-T1-product-full-failure-triage-v1.md` SHA `a97e8c4b…` 说明旧官方错误号合同与P1已批准的W安全边界不同。十个历史code行全部保留，实际DmError/DmException精确构造后核Number、IsTransient=false，再经过生产ShouldRetryOn和Execute证明attempts=1/同异常传播。原五个非瞬态/模糊code、非W Timeout、默认设置和collection-null负例继续保留。原两个非空override成功场景分别改成合法空集合snapshot（调用方修改后才创建context/strategy）及构造阶段NotSupported/业务attempts=0；原-12345/-54321/-77777数据仍用于独立非空拒绝。

新合同覆盖builder collection/延迟enumerable两入口、strategy context及两dependencies实际构造入口的非空拒绝；合法null/empty、默认/自定义count-delay和immutable空snapshot。通过合法protected DmException构造的合成子类仅override公开IsTransient，生产策略true时前两次抛同异常、第三次返回42（attempts=3），false时attempts=1/同异常传播；非W DbException即使IsTransient=true仍一次。合成true只证明公开分类消费，不是W transport/classifier或实际COMMIT丢ACK证据；不写private FailureInfo/Outcome旗，不修改产品detector。

实际sealed DmCommitOutcomeUnknownException(Exception)及DmTimeoutException(DmFailureInfo,Exception)由明确受审内部构造签名fail-fast反射fixture生成，核actual type/sealed/IsTransient=false、cause和production Execute一次同对象传播。timeout的FailureInfo只用其真实八参数构造，不改私有字段。构造缺失不catch/兜底。所有compile/analyzer/Nullable策略原样，无新增抑制。

新完整patch `transaction-capability.v3.patch` 相对原批准175 base的完整五路径diff，静态内存hunk重建精确178：173原base不变、2修改（DI/retry unit）、3新增（adapter/factory/transaction unit）。相对v2仅retry unit变化，不能拿该单文件父差异代替交付patch。manifest v3记录完整map/aggregate、新patch、v2父refs、旧full失败和本triage。新的source-only批准须由root另签scope `transaction_capability_and_w_retry_contract_tests_only`，旧d6cff只作父证据；作者未签任何批准。

静态源码声明/InlineData清单（非C# compiler AST、SDK discovery或运行结果）：21方法/49 retry场，其中原20场逐一保留或适配、新增29场。manifest中的original20/original12映射、new29名单以及原795 TRX-row ref与完整824 planned名单供验证者核验。`planned_unit_cases=25`与planned_transaction_unit_cases仍专指原事务能力类，不改成49或824。

| retry 测试方法 | 静态planned场数 |
| --- | --- |
| EnableRetryOnFailureUsesDefaultDamengStrategy | 1 |
| EnableRetryOnFailureAppliesCustomSettingsAndCopiesEmptyErrorNumbers | 1 |
| StrategyDoesNotRetryUnclassifiedWServerNumbers | 10 |
| StrategyDoesNotRetryNonTransientOrAmbiguousDamengCodes | 5 |
| StrategyDoesNotRetryNonDamengTimeoutException | 1 |
| StrategyRejectsAdditionalErrorNumberBeforeExecutingDelegate | 1 |
| CollectionOverloadRejectsNull | 1 |
| EnableRetryOnFailureMaxRetryCountPreservesDefaultDelay | 1 |
| EnableRetryOnFailureAcceptsNullAdditionalErrorNumbers | 1 |
| CollectionOverloadCopiesEmptyCollectionBeforeCallerMutation | 1 |
| BuilderCollectionOverloadRejectsNonemptyOverrides | 3 |
| BuilderEnumerableOverloadRejectsNonemptyOverrides | 3 |
| ContextConstructorRejectsNonemptyOverrides | 2 |
| DependenciesConstructorsRejectNonemptyOverrides | 6 |
| ContextConstructorAcceptsNullOrCopiesEmptyOverrides | 2 |
| DependenciesConstructorsAcceptNullOrCopyEmptyOverrides | 4 |
| ContextAndDependenciesConstructorsPreserveDefaultAndCustomRetryCounts | 1 |
| SyntheticPublicWClassificationControlsActualRetryAttempts | 2 |
| NonWDbExceptionCannotEnableRetriesEvenWhenTransient | 1 |
| ActualSealedCommitUnknownExceptionIsPropagatedWithoutRetry | 1 |
| ActualSealedWTimeoutExceptionIsPropagatedWithoutRetry | 1 |

原12失败逐条对应：

| 原case | 新plannedcase |
| --- | --- |
| StrategyRetriesAnAdditionalErrorNumber | StrategyRejectsAdditionalErrorNumberBeforeExecutingDelegate |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: 6027) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: 6027) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: -3404) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: -3404) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: 6001) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: 6001) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: -6003) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: -6003) |
| EnableRetryOnFailureAppliesCustomSettingsAndCopiesErrorNumbers | EnableRetryOnFailureAppliesCustomSettingsAndCopiesEmptyErrorNumbers |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: 6089) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: 6089) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: -3003) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: -3003) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: -6010) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: -6010) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: 6060) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: 6060) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: -6004) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: -6004) |
| StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(errorNumber: 6123) | StrategyDoesNotRetryUnclassifiedWServerNumbers(errorNumber: 6123) |

源码冻结后等待独立审查者完整review、验证者新178/完整patch独立snapshot、root新七字段source-only批准。新准备工具还需新pins及严格scope守卫修订，不能称纯pin-only/算法AST完全一致；原五阶段/12refs/3folders/178/51record50archive49nonW/raw4全字段pruning272/锁/300执行与2秒reap边界不变。验证者新source→native→locked build后先完整retry49、再transaction25、同一已审构建无filter full824，正式TRX exact case-set、自然exit0、零skip/failure/abort/timeout，失败保留即停止。

旧target25通过与旧full783/795不能拼接为新接受。之后才能按另行审阅/授权推进新probe兼容工具、source/native/build/compiled28/readiness及四例runtime；L0/E/D均要改绑新178。作者只做文件/hash/diff与静态声明整理，未执行SDK、tests、import项目工具、validator、wrapper、DB或secret，未写运行/验收报告。三handOff与新178完成后冻结停写。
