# TP07 E固定15例分窗：五文件源码验收

2026-10-10。结论：**fixed15_exact25_window_source_accepted / Low_new_build_offline_gates_pending / runtime_pending**。对设计`0ed4960f…`及已接受E-tools-v9未发现源码阻塞。root可安排Low隔离新source/build/startup/toolpack及离线门；本报告不表示26个合同、实际partition或28模型门已经执行。

本轮仅只读源码/JSON、计算hash及AST/字节比较，未运行SDK、测试、validator或DB，未修改源码/旧证据，仅新增本文。

## 冻结输入与改动范围

| 输入 | SHA256 |
| --- | --- |
| Program.cs | `20732d23f6deafc5fb06b814c10bcc2b5de46e1c722b89f6aa138957dc90480c` |
| ef-cases.json | `8836130ae6151fb6194d15bab7226ec5dff7859cc4de06124fd1c19bf5f1c5fa` |
| ef-run.py | `57c3e6aafbdbf3bd33c68e6539227f9472e3318ad6b97d4e996ad450c8cecd86` |
| ef-validate.py | `7f1a90051296c2c4d169cb53feac279a23b6ac7bc089707a12d01d650eea20c4` |
| test_ef_contracts.py | `defd1b3a502d2664a3b1831c9456b250fce785a2e4722a67cb5217e430136bc4` |
| policy E9 aggregate | `3e54a9a2b3fca3208af37d00824c750051db4379af6df8e8424f99564108b494` |

相对v9实际24项source snapshot只有这五项变化。policy只有新增window_contract、Program pin和aggregate变化，原338项case、counts及其他policy结构不变；其余八E源pin不变，九项live源均匹配当前policy。GeneratedCreate方法逐字节不变，SHA`1b1f2d399254eec57080b1a56b4d2407f436352e28fab069f191a6ede9ef1353`；从OfflineContext起的其余Program/RuntimeHarness尾部也字节不变。W、mapping、case类、Models、CommandAudit、common和原L0接受源未涉及。

旧validator的validate_case、source、raw_tuple、published、concurrency_parameters、ledger_contract、original_l0_validator、native_oracle、send_refusal AST均保持；改变的是输入门、窗口required_cases、report schema及编排聚合。没有随分窗放宽时间编码、非fixed原值、来源、异常或清理合同。

## 分区与case语义

C# `SelectWindow`从未改RuntimeCases.Required取原序列，固定size15及合法index生成连续slice；Python独立由原policy重建同样descriptor。顺序为shared/TLS，各自roundtrip/six/mutation/generated，13+12=25窗/338例。run顶层禁止profile/group/window/run-id筛选；内部index仅有限已开始窗口，不能变成任意start/count/IDs入口。

reuse、winner/stale、transaction rollback、generated值和native oracle仍由原case Run完整执行，未拆业务步骤。C#与Python均检查six同窗async确定成功前缀；当前[15,30)、[30,45)、[45,54/56)保留原先索引27/30/45的成功控制。runtime的actual发送/Prepare/operation/published正控制门仍在同report执行，静态前缀不替代实际成功，也不借前窗controls。

纯offline partition在guard/env/Context/OwnedObjects/observer之前分支，使用同一个SelectWindow导出真实编译registry和25窗，同时拒绝缺index、非法index、任意size/range/IDs和offline模式混用。独立Python逐定义比较全部case字段及route/expected语义，不只对总数；host DLL/MVID/deps与新startup绑定。不存在用两个Python模拟结果互证而冒充C#执行的批准路径。

## 新输入门及28模型证据

approval仅增加两个有限引用`partition_gate/generated_create_gate`。gate identity绑定新source/lock/startup/toolpack/policy/window contract，**不依赖尚未创建的最终approval**；Low可先执行并验证新编译probe，再由root创建运行approval，无循环批准。

partition gate要求新host实际命令/自然退出、180秒固定界限、当前startup全源/输出字节及保存的独立验证结果；运行inputs重算预期验证结构及case分区。

generated gate要求新shared/TLS各14模型真实报告、两条实际编译host命令、自然退出、zero IO/closed W factory、actual新host和provider身份，并保留受审v4深验基线`c6f269c5…`及后续cohort基线`ec3f90e7…`的精确引用。逐模型runtime/database spec、models、columns、operations、seed、generated_SQL与深验基线比对；每个SQL同时核当前/基线文件SHA及实际字节相等，缺文件或篡改不能用成功旗补齐。

本轮已读取上述实际v4深验报告与shared/TLS原报告：`profiles[profile].report_sha256`、case模型/seed/SQL及io/factory字段形状均与新gate相符；v5cohort基线SHA也实读匹配。新gate所需新增baseline_refs/semantic_comparison字段须由Low新的独立readback真实生成，不修改旧报告。旧28方法不变不意味着新编译免执行此门；旧报告不能替代新的command/report/startup身份。

## 运行、清理与不可拼接的聚合

每次新根目录生成run_id；cohort_id绑定新preparation及批准执行引用。每窗metadata联结run/profile/group/index/start/count/IDs及policy/pack/approval，新main report、internal hosts及window receipt必须一致。路径固定`run/profile/group/window-NNN`，实际命令、日志和五项artifact canonical路径均被核对，解析后symlink逃逸拒绝。

runner逐窗执行main900秒（包含原finally cleanup），无论partial/失败都在原真实ledger存在时执行原LiteralProbe独立cleanup300秒，然后本窗独立validator180秒；失败停止后续窗。没有替代ledger、异步并行、自动retry/resume、条件跳过或清理移出main。validator明确验证900/300/180固定deadline，不接受把自报时限加大。无ledger或缺cleanup artifact无法接受。

每窗保留候选包/源码/实际provider/连接身份、全部case oracle、generated SQL、同次metadata与raw、main cleanup及独立cleanup/finally三组证明。局部attempt/sequence在窗内唯一；跨窗以run/window身份隔离。最末最终validator重新读取所有窗口并比较保存的独立结果，不只相信producer count或窗口成功旗。

八组、25窗顺序和完整338个ID必须精确相等且不重叠；所有window共用同一run/cohort/pack/approval，全部338个nonce/table/PK在新run内唯一。旧schema/group receipt不能通过新schema2窗口合同；旧cohort/group无法拼接补足，也不能用中间窗口resume构成完整run。group/run索引与实际receipt hash、validator自然退出/命令、case结果绑定完整。full_EF_C=false和D pending保留。

## 验证边界与下一门

AST计数为26个test方法，新增5项覆盖固定分区/async前缀、编译partition身份变异、全量coverage/cohort、真实文件hash/path/host/ledger/deadline joins及28模型语义/SQL/IO变异。positive-before-mutation保持；window文件join测试明确仅mock旧business/raw oracle与SQL内容检查，其余真实路径/hash合同执行。它不被宣称完整DB或完整runtime端到端通过；原业务/raw合同继续独立保留。

Low下一步必须按新冻结五源生成新toolpack/source/lock/build/startup及真实DLL/MVID/deps，执行26合同、编译offline partition及其独立验证、原28模型新编译readback；实际gate内容需满足当前有限schema。全部通过后root才创建含两gate的新approval和新25窗runtime目录，固定原900/300/180界限。旧L0自然源根及接受proof、旧cohort失败和所有owned关闭证据保持原样，不用旧startup套新Program。

本验收只批准实现的源码边界；新工具未运行、编译可用性和所有新gate实际通过仍待Low。若实测出现未观测tuple、时延或cleanup失败，继续原fail-stop和精确owned闭环，不因本次分窗源码接受扩大能力、提高时限或跳过后续D/完整C/TP08后LocalClaude门。
