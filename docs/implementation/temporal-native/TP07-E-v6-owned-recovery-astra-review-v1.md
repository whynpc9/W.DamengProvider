# TP07 E-v6 精确两表恢复v1源码验收

2026-10-10。结论：**exact_two_owned_recovery_source_accepted / Low_build_selftest_pending / recovery_runtime_pending**。对已批准设计`6211ec8a…`未发现源码阻塞。root可安排Low隔离构建、精确binary身份及63项离线合同验证；本报告不表示已执行恢复，也不替代独立最终absence。

本轮只读三源、相对已接受inspector v2的差异及实际原输入，计算文件SHA并核字段形状；未运行SDK、自测、validator、DB或探测，未修改源码/原证据，仅写本报告。

| 冻结输入 | SHA256 |
| --- | --- |
| recovery-v1 Program.cs | `504c2b4bdc42c6cbdbcfe3f2f19e12f328dcb978c37323d10282b782e1e78020` |
| recovery-v1 README.md | `ed1166b1a423aa13b017d8d473f74b181112e948e5f5274562635811adb399f8` |
| recovery-v1 csproj | `2b657c7c2558983fb4fd529a362ef90f8471b2be56cee8a549e21cdcdb5ecfb5` |
| observation/recovery design | `6211ec8a139b85191d08934c82bfac2eab3b70dba3f61f5c1e6a1fa88b6e134a` |

## 来源和身份门

15个固定input引用的当前文件SHA逐项实读匹配：原11项以及实际inspection`d38c55df…`、control`b413d202…`、build`c6476808…`和设计本文前置版本`6211ec8a…`。完整九entry ledger、原main/group/run/failure、七absent两present分割、prior TEST/profile与prior工具/W身份均被检查。原11项还按路径+hash与prior inspection连接，包括原v2 candidate-feed包路径；不是仅接受同名文件或任意替代ledger。prior build实际字段名与代码吻合，没有收据schema误读。

实际包内DLL及加载W hash/MVID、原E host字节、prior inspector DLL/deps与外部Low批准的新host DLL/deps/MVID在读取TEST环境变量前检查。PackageReference为精确区间；Low仍须核新assets/lock/archive/deps与批准输出，不能用传入自身hash替代独立批准。selftest在认证前返回，无env读取或SQL执行。

## 写动作与控制流

`Selection`先检查完整九对象table/PK状态一致，原七absence任一重新出现即失败。写目标仅固定前两项，已经absent的目标保持`drop_attempted=false`。`WriteSequence`先对全部存在目标完成四项fresh marker/Basic/PK/PK-ID检查，才允许第一个写；随后每个目标的DROP前再执行四项语义复核。任一检查异常直接结束序列，无法进入下一写。

Basic列仍使用已接受v2的nullable Int32 facet表示规则；ID/N raw precision/scale不要求null/0，D/Z scale7及四列顺序、类型、nullable、width4/9/11/32、nonce comment和ENABLED唯一PK(ID,position1)保持。不存在与旧JSON整体相等的隐式facet门，也无row0或fresh row1要求。

`DropSql`只有两个字面量：`TPL_431D133DF3E4440895268E1B`和`TPL_63FCD302EB57437F9132DD37`的精确DROP TABLE。`TakeAttempt`同时限定前两Scope及本进程每表一次；输入没有任意表、SQL、Schema、mode、subset或resume。无CASCADE/PURGE/其他DDL/DML、rowcount、trigger、session/实例变更。

DROP前先原子flush attempt、SQL SHA、开始时间及unknown outcome；自然返回后立即持久化真实返回值和ACK，−1不被误判。`recovery_performed`在进入attempt时保守置true，实际是否收到ACK必须读取该表`drop_outcome/drop_acknowledged`；它本身不证明DDL完成。执行或写收据异常使余下写停止，catch只保留安全错误；没有retry。

## fresh观察与失败保持

操作连接关闭后创建另一个新连接，重新核TEST身份/同一物理profile，再用两条精确九名称batch核全部表和PK。完整合法snapshot且两个集合都空才设fresh_absence_observed；缺查询/partial/null snapshot不能形成absence。写流程报错后普通控制流可做一次该只读观察，但`writesComplete=false`使最终exit仍失败，原异常不会被absence冲掉。finally仅Dispose/报告，无重复认证、查询或DROP；Dispose失败也使exit失败。

阶段开始/完成与耗时原子持久化，未知结果保持未知；batch中途未返回的行不被冒充已完成观察。`independent_absence_closure`始终false。恢复host自然完成仍需另起进程运行未改字节的已接受v2 readonly host、使用新输出，实际九表及九PK全absence后由root关闭残留。旧E-v6失败和所有原收据不回写。

## 离线及运行门

计划63项静态计数成立：原36项；actual inspection正例及hash拒绝、九项实际receipt变异；two/empty selection、七项重现/矛盾/orphan、每表一次、partial与完整absence、全部preflight早于首写、DROP失败无后写/重试、正常写序及精确两个SQL检查。关键路径使用与产品执行同一`WriteSequence/Selection/TakeAttempt`；未将手写receipt标为DB事实。本轮没有执行这些测试。

Low构建/63自测及binary closure通过后，root才开放新工具独占恢复窗口。新180秒外部process-group预算、ConnectTimeout15/CommandTimeout12保持既定方案，不改变原900/300秒runner，不重跑原双cleanup。任何超时只保留partial、停止写并由后续独立只读状态证据裁决。恢复关闭不接受E行为；D/完整C与TP08之后LocalClaude门不变。报告完成后停写。
