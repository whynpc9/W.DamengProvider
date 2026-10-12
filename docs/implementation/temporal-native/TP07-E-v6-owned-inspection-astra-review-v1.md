# TP07 E-v6 exact-nine inspector v1源码复核

2026-10-09。结论：**source_revision_required_non_temporal_catalog_facets**。在下述窄问题修正并重新冻结前，不开启本工具SDK/selftest/DB窗口。本轮仅静态读取源码和既有JSON、计算hash；未执行SDK、自测、DB或探测，仅写本报告。

## 冻结身份与唯一阻塞

Program.cs SHA `7df9aa18aeb12cba3b334859bf7439a8066006fefc902388e88aa680cb98da9a`；csproj `d8fe98f761ef46b8381001c57764e1f77f70f7162fe8ddac99bcf8aa2a2c154a`；README `089cfc31019c78a14362503b78a157cfe7decc82eeee0b4d13690fa07f9902d8`。三项均实读匹配。

`Program.cs:233`的统一`r[2] is null`和非时间列`r[3] is null || Count(r[3]) == 0`，把ID/N的目录DATA_PRECISION/DATA_SCALE变成所有权门。这不是原ledger声明的Basic合同，原`OwnedObjects.VerifyStructure`也不赋予ID/N这些时间精度语义；合法目录观测可能被拒绝，掩盖本次需要定位的实际等待阶段。

证据边界需准确：已保存v5同cohort mutation Basic实际目录是N VARCHAR/32bytes、precision=null、scale0，**本条不声称当前DDL已被观测为scale7**。更早v3只读目录的N为VARCHAR2/128bytes/32CHAR、precision=null、scale7；它说明非时间目录scale不能普遍解释为量化约束，但该旧列宽本来就不满足当前Basic32bytes，不能拿整条旧row作当前结构正例。v6目前没有fresh目录观测。

批准的窄修方向：ID/N precision和scale保留实际nullable typed整数元数据，拒绝字符串、bool、非整数等错误表示，但不强制其值为null/0，不以它们判时间量化。D/Z scale仍必须7；四列名称/顺序、类型、nullable、宽度4/9/11/32、精确marker及唯一PK(ID,position1)全部保持。加入完整当前Basic正例及只改变非时间facet为合法整数/NULL的正例，错误numeric表示、D/Z scale、width/marker/PK负例；这些是离线合同，不是新目录观察。其余SQL、认证、范围、产品源码均不扩展。

## 其余已核对边界

八个原始main/ledger/group/run/failure/approval/startup/triage常量全部匹配当前artifact；candidate identity/capabilities常量也实读匹配。九个case/nonce与原完整ledger逐项同序匹配；拒绝empty/subset/duplicate，不提供任意SQL/Schema/对象或恢复开关。

认证前校验原SHA、引用链、原超时和失败、包内W DLL及实际加载W hash/MVID、外部Low批准的host DLL/deps/MVID。自测分支在读取TEST环境变量前返回。PackageReference精确版本；Low仍须在后续独立构建门核实际assets/lock/deps与feed闭包，本报告不把尚未构建的identity当已通过。

数据库代码只有身份SELECT、两条精确九名称批量presence查询及存在表的四条目录SELECT；无DDL/DML、business rowcount、SELECT1、trigger或其他对象扫描。USER/current schema不匹配时只报安全码，不回显陌生用户名；显式typed PlaintextAllowed、pooling关闭、日志OFF，连接失败无fallback。marker只在精确匹配后存bool，不保存任意comment文本。

absence来自完整返回的批量结果，字段数、最大行数、名称唯一性及PK归属/类型校验后才形成逐对象状态。table无而精确PK有会失败。单连接状态明确为`inspection_complete_not_independent_closure`，不能直接关闭独立absence或恢复任务。每phase开始/结束及对象边界原子持久化；batch rows在整条Query完整返回后持久化，查询中途被杀只能保留started phase，不能声称读到部分batch rows已落盘。连接dispose单独计时，失败会使exit失败；不伪造被杀阶段的elapsed或完成。

18个原计划自测的计数与静态控制流一致，但尚未执行，且窄修须增加上述列facet合同覆盖。v2重新冻结后再由Low独立构建/自测；主reviewer不替代Low。

## 修正后可用的有限诊断预算

建议root为**新只读host单次执行**单独设180秒外部process-group上限，保留ConnectTimeout15/CommandTimeout12；这是有限诊断预算，不能承诺九表全部存在时四目录查询都能完成。全absent路径只需Open、identity、物理profile和两条batch；若仍超时，保留最后started phase及此前完整观察，不自动重试或提高deadline，不重跑原900/300秒runner或双Cleanup。此建议待v2 source/Low gates通过后另开窗口，当前不构成DB授权。

恢复仍需重新证明精确marker/结构所有权、每条最多一次DROP及另一fresh TEST连接的独立absence；本readonly工具没有恢复权限或成功旗。所有旧证据和已接受E/L0/W common保持冻结。LocalClaude门仍在TP08之后。
