# TP07 E-v6：roundtrip 与清理超时的有限分诊

2026-10-09。结论：**runtime_wait_regression_unlocalized / exact_nine_owned_objects_absence_pending / E_runtime_failed_stop**。本轮只读新旧收据、冻结 runner/C# 和既有恢复设计；未启动 SDK、测试、validator、DB、网络探测或其他诊断进程，未改源码、旧证据或 ledger。仅新增本报告。

## 已落盘的最终事实

原 runner 已自然到达 `group_failed_stop`，仅启动 shared/roundtrip。其 main 外层900秒、cleanup外层300秒界限保持原值；两个进程组均由原 runner deadline 终止。后续 six/mutation/generated/TLS 未启动。

| 阶段 | 实际秒数 | exit / host exit | 结果 |
| --- | --- | --- | --- |
| v6 shared/roundtrip main | 900.018226 | 124 / -9 | timed_out=true，deadline_kill |
| v6 独立 cleanup | 299.995843 | 124 / -9 | timed_out=true，deadline_kill |
| v6 validator | 0.186208 | 1 / 1 | `process_success_no_timeout` |
| v5 同组 main | 89.718409 | 0 / 0 | 自然完成 |
| v5 同组独立 cleanup | 30.488663 | 0 / 0 | 自然完成 |

v6 `cleanup/report.json`、main/cleanup host receipt 均未落盘，group artifacts只有原 ledger 和 main report；不能宣称服务器最终为空。`input_hashes_before == input_hashes_after`；v5/v6实际 main 的 candidate identity、EF identity和required cases相同。v6 main/cleanup日志均为0字节，不构成没有错误或没有操作的证明。

证据路径以下均相对 `.local/temporal-native/verification/`：

| 文件 | SHA256 |
| --- | --- |
| TP07-E-v6-runtime/run-receipt.json | `2f1853276c77be2d7207be8555fb3c7f6162b5626b51b58aab7393038a364dae` |
| TP07-E-v6-runtime/shared/roundtrip/group-receipt.json | `dfdc24f15603da90c41aed81c423bcf8ec865e5efa29c671eb7215900da5d215` |
| TP07-E-v6-runtime/shared/roundtrip/main/runtime.json | `1a0df6f50c8aef034204ad924917aa1d30e6b6a0ea77802d6f980a44440fcff8` |
| TP07-E-v6-runtime/shared/roundtrip/main/ownership.json | `d37b870c9d3c49b8c7d91e76f470775207fbbe2af51cfa45e31f2df74e5c946d` |
| TP07-E-v6-runtime/shared/roundtrip/validation.json | `321ddeedfede7c63b679af9aab567d1c4f435c2f2eae553c7a00afb6a5e7b7dc` |
| TP07-E-v5-runtime/shared/roundtrip/group-receipt.json | `da532b2b66ae6186c21364d88b430c4c6d22c21e799309e9206fb0f684d8238d` |
| TP07-E-v6-runtime-control/verification-summary.json | `7da50dc85c1d7c989988bcc1aef2a60227f4971765eca9666dbd9c021c3b186d` |

已读Low最终control summary：runner exit1、window_released=true、old617_unchanged=true、无repair/retry；其记录runtime artifact manifest SHA为`fceb22ca16b46e31c2eec6c1e7316da88bec3dd40b0e53341ee2f16f04e2c74c`。这些是外部验证者的收据，本reviewer未再次执行617项检查。

main最新快照有8个completed case及第9个attempted case，共54条EF command事件。第9项 `EF-R01-shared-dt-tail_tick_4-sync` 的CREATE、SaveChanges_insert、fresh_AsNoTracking均已完成；`fresh_independent_D`只有attempted。前8个独立oracle均记录TEST user/schema且完成。该快照未有第9项 `oracle_read_attempts`；因为 `Independent` 在进入oracle前写盘，oracle内部的partialSink只在返回/捕获异常后才由外层写盘，不能据此断定尚未连接或尚未发送查询。

## 源码与环境归类

没有发现能将本次变慢直接归因于v9 Python并发分支的调用路径：新helper只用于mutation validation，而本次停在未变编译cohort的roundtrip main，之后独立cleanup也超时。v9两文件窄变更及冻结身份已由v9源码报告限定；本次没有重新构建。不能因此排除原有driver在特定运行状态下等待，也不能将其直接裁定为新源码回归。

`LiteralOracle.ReadFresh`包含新建连接、TEST身份/物理profile验证、精确本表COUNT、值/FF9读取及reader结束/释放；现有step粒度不足以将等待定位到其中哪一项。主协调者观察到低CPU和持续缓慢进展，这支持优先检查等待路径，但不是网络、服务端锁/负载、连接建立、主机调度或驱动等待的独立归因证据。本报告没有运行栈采样，没有connection target或服务器错误证据，也不补造这些事实。

冻结 `CandidateGuard`关闭连接/statement/prepare pooling；每次Open还执行身份查询，oracle再验证一次身份。`OwnedObjects.Cleanup`对每条entry使用操作连接和fresh absence连接。LiteralProbe cleanup入口调用Cleanup，finally再次调用Cleanup；这会重复核验，是既有潜在延迟放大因素，v5同样存在，尚不是本次根因。原代码设置ConnectTimeout15秒和CommandTimeout12秒；这些设置不证明每个底层阶段都实际受相同墙钟界限约束，也不替代外层总deadline。

runner在finally中的cleanup结束后才保存main/cleanup group收据，所以运行中看到`main:null/cleanup:null`不是没有运行；cleanup程序在两轮Cleanup/finally之后才写最终report，缺少逐entry持久化进度。因此本轮不能用缺报告或ledger未变化判断某个DROP是否执行。没有证据支持提高deadline或再次运行全套。

## 精确待核实对象

恢复范围仅为本次原ledger SHA `d37b870c…`中的9条 `WDM_PROVIDER_TEST` entry；均为Basic/scale7、confirmed、absence-before/CREATE ACK/COMMENT ACK=true。下列九表及各自同后缀`TP07PK_`是最终absence待核实集合，**不是已证明仍存在的残留集合**：

| case后缀（均DT sync） | 精确表名 |
| --- | --- |
| min | `TPL_431D133DF3E4440895268E1B` |
| min_plus_tick | `TPL_63FCD302EB57437F9132DD37` |
| max | `TPL_0C4F99069C654940A6C98511` |
| max_minus_tick | `TPL_1F14652B85744D5984CDC415` |
| max_minus_9ticks | `TPL_920D58B7732E44D09C7F28E5` |
| tail_tick_1 | `TPL_EB6D77B3F5474A55B71A2BAD` |
| tail_tick_2 | `TPL_0D7AB5BCDB4A4F328E817C04` |
| tail_tick_3 | `TPL_678CF064908442948923BB9F` |
| tail_tick_4 | `TPL_3AF5B05F67C14D92A6FA2AA2` |

主进程被杀可能留下已提交DDL/DML；独立cleanup也可能已完成部分DROP。实际残留数未知。不能用最新case序号、cleanup逆序实现或运行秒数猜测已删前缀/后缀；必须逐一新连接核实。

## 最小后续诊断与恢复计划（尚未执行）

1. root先封存本轮上述文件和外部控制summary，确认原runner及child均已退出、测试窗口独占。保持原失败与ledger字节；不把v5成功或v9离线合同通过改作v6通过。
2. 下一窗口仅安排一次有界、串行的TEST只读诊断。建议High在新的ignored目录实现小型一次性direct-ADO W candidate host，绑定原ledger/main及九entry白名单哈希；只允许输出路径和已固定输入路径，不提供任意SQL/Schema/对象参数。原已接受v3 inspector把旧单表、旧ledger哈希和objects.length==1写死，不能安全绑定本九表；禁止生成synthetic ledger骗过旧工具。复用原W nupkg资产、DLL/MVID及candidate/profile证明，保持已接受L0/E common不变，经源码freeze/review、Low构建身份验证后再开DB窗口。

   继续用原TEST wrapper，先核TEST user/current schema；身份不符只输出安全失败码，不输出其他用户名。只在current USER_SCHEMA对九entry绑定名称查询presence/absence及精确PK；存在时才取同表完整列/PK/comment marker结构，rowcount仅在已确认owned的精确存在表上读取。连接建立、身份验证、每个精确查询、释放分别记录安全阶段名及单调时钟耗时，开始/完成及时写新artifact；异常只保留安全type/code，凭据不入日志。此步骤不重新执行roundtrip，不查询其他对象、全库会话/锁视图或实例配置。若首个阶段超时/身份失败，停止并保留边界，不循环探测。
3. 全部已absent时，不DROP；另一个独立TEST连接/进程逐条证明精确表和PK都不存在后，只关闭本次残留状态。若存在，先回读本条nonce、原CREATE/COMMENT ACK、固定PK/ID列及完整Basic列结构，匹配原ledger的confirmed ownership合同。任何不符或必需查询失败都保持pending；旧单表恢复工具硬编码旧对象，不能直接复用其入口，也不能把旧表row0前提套到这批已插入测试行的表。
4. 对已重新证明owned的存在条目，由root另开限定恢复窗口；High若需实现辅助工具，只做新目录、固定这份ledger哈希/九entry白名单的单次操作工具，先freeze/review及Low独立核验。每条最多一次精确DROP、无CASCADE/PURGE/其他DDL/DML；执行前持久化attempt，之后另一新TEST连接检查精确表和PK absence并逐项持久化。已经absent的条目记录`drop_attempted=false`。发生未知结果不自动重试DROP，先只读核最终状态；未获独立absence的条目继续pending。不得重写原ledger或原超时报告。

上述是待root分派的最小诊断/恢复方案，不授权本reviewer执行DB。全过程无SA、DEV、跨Schema、实例模式/权限变更或无关目录查询。若后续阶段耗时证据仍无法定位，先回root决定一个具体待验证假设，不提高deadline或无依据重跑。E-v6失败不改变此前独立接受的cohort事实；E后续组、D及完整C继续pending，LocalClaude门仍在TP08之后。
