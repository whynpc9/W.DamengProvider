# TP07 E-v6：九对象观察接受与精确两表恢复设计

2026-10-10。结论：**exact_nine_readonly_observation_accepted / exact_two_recovery_design_ready / recovery_not_executed / independent_absence_closure_pending**。本轮只读实际inspection/control/build收据并核原引用及加载文件字节；未运行SDK、自测、DB、探测或恢复，未改源码/旧证据，仅新增本文。

## 已接受的有限事实

实际v2 readonly host在180秒外部界限内自然exit0/host0、无timeout，耗时29.43961816607043秒。15个phase全部completed：Open4.4318649秒、身份2.5998861秒、两条batch2.1131359/2.509644秒、两表各四项ownership查询1.3381451–2.9512602秒、Dispose0.0055004秒。user/current schema均为TEST，shared8.1.5.60/message21/utf-8/encoding-override-empty，typed PlaintextAllowed。没有DDL、业务rowcount或数据修改。

精确九entry同原ledger顺序，实际只有下列两项table_presence=1/PK_presence=1，marker精确匹配、Basic四列、唯一ENABLED PK(ID,position1)已观察；另七项表及各自精确PK均absence-observed。七项只是这次只读连接观察，不是整个任务的独立最终closure。

| 唯一可进入后续DROP门的表 | 原完整nonce | PK |
| --- | --- | --- |
| `TPL_431D133DF3E4440895268E1B` | `431D133DF3E4440895268E1B67EBFFF6` | `TP07PK_431D133DF3E4440895268E1B` |
| `TPL_63FCD302EB57437F9132DD37` | `63FCD302EB57437F9132DD37BCF7D9CB` | `TP07PK_63FCD302EB57437F9132DD37` |

两表实际列均为ID INT/null precision/scale0/NOT NULL/4bytes；D TIMESTAMP/null/scale7/nullable/9bytes；Z DATETIME WITH TIME ZONE/null/scale7/nullable/11bytes；N VARCHAR/null/scale0/nullable/32bytes。原main两项SaveChanges_insert各记录affected_entities1；**这次没有fresh rowcount，不宣称当前仍恰1行，更不能套用旧单表恢复的row0门**。只凭已确认本次自建对象的所有权清理测试表，不为此增加业务DML或rowcount探测。

## 必须绑定的实际输入

以下路径相对 `.local/temporal-native/verification/`，triage/设计另按repo文档路径绑定：

| 输入 | SHA256 |
| --- | --- |
| TP07-E-v6-owned-inspection-v2-runtime/inspection.json | `d38c55df8e0c9d1b9ebcdd0dfa9550ff8d1b75f6ae7477ba0a6ac061a28545fe` |
| TP07-E-v6-owned-inspection-v2-runtime-control/verification-summary.json | `b413d202777686097e0230a978f90a618516e2e136878e80b9689773076ab30c` |
| TP07-E-v6-owned-inspection-v2-build/build-manifest.json | `c647680804108ca1cc347b21777e4d52c976947f2cefceb8653f0da69910d82b` |
| TP07-E-v6-runtime/shared/roundtrip/main/ownership.json | `d37b870c9d3c49b8c7d91e76f470775207fbbe2af51cfa45e31f2df74e5c946d` |

恢复工具在任何环境凭据读取/认证前，固定实际inspection完整SHA、九entry身份/状态与七absent两present分割，绑定control自然退出及工具身份；同时保留v2 `input_artifacts`中的完整11项原引用和其固定hash：identity、capabilities、package、ledger、original-main/group/run/failure/approval/startup、triage。必须核原完整九对象ledger，禁止新建empty/subset/synthetic ledger。设计本文也作为外部冻结输入；工具不能根据任意producer“成功”字符串选择写目标。

本轮11项原引用的当前文件SHA全部实读匹配，inspection/control的objects及phases完全一致。实际inspector DLL `e9c6668c23a5b59b1b284cd34ec8cdfc972b5c2206bc7cff12d569240a07be81`、deps `8756a287c2afb27b9fa647d9f36f8eefa46db9f3327a71214ee8d01097a2559a`字节已核；记录MVID `5b32413e-0374-4754-86af-4f4a604c1416`。实际加载W DLL `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`也匹配；W包仍`da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。MVID值来自Low及受审host收据，本轮未启动SDK重新提取。Low control记录651项全部未变，本轮不冒称重新执行651项验算。

## 一次性实现范围与新鲜门

High只在新ignored目录 `TP07-E-v6-owned-recovery-v1`编写Program.cs、csproj、README三文件；不修改已接受E/L0/W common、inspector或原ledger。固定九对象读白名单及上述两对象写白名单，不提供任意SQL/schema/表名、恢复集合、模式或通用resume参数。复用同一W包及字节身份；Low另冻结source、restore/lock/assets、DLL/MVID/deps与离线selftest结果，源码复审后由root开独占恢复窗口。

凭据仅原TEST wrapper内存传递；typed PlaintextAllowed、TEST schema、pooling关、Log OFF、ConnectTimeout15/CommandTimeout12保持。Open后首先确认actual USER/current schema为TEST及原物理profile；失败只记安全type/code/Number，不回显陌生身份或凭据，不fallback/重连重试写操作。

先用实际v2相同的两条精确九名称batch查询fresh table/PK存在状态。原七absence项若重新出现、PK归属不符或table/PK状态矛盾，应在任何DROP前失败，不能扩大到第三表。两个写目标可自然变为already-absent；表和精确PK均无时记录`drop_attempted=false`并跳过，不能写成工具已删除。

对仍存在的两个目标，在同一恢复连接重新完成原四项只读门：精确`TP07_<完整nonce>`comment；四列名称/顺序、类型、nullable及width4/9/11/32；D/Z整数scale7；唯一ENABLED精确PK及唯一ID/position1。使用v2已接受的nullable Int32 facet表示规则；ID/N raw precision/scale保留观察，**不得借“与旧JSON完全相等”重新引入null/0语义门**。字段数/行数、NULL、数值表示、名称及nonce均fail-closed。原inspection只提供来源，不能代替本次fresh门。两项存在目标的preflight全部成功后才允许首个DROP，任一失败不开始写。

## 唯二写语句与结果边界

工具中允许的写语句仅为下面两个固定字符串，各目标本次进程最多一次尝试：

```sql
DROP TABLE "TPL_431D133DF3E4440895268E1B"
DROP TABLE "TPL_63FCD302EB57437F9132DD37"
```

禁止CASCADE、PURGE、DELETE/TRUNCATE/其他DML、COMMENT/ALTER、跨Schema、session设置、SA或实例操作。每次执行前原子落盘该表的attempt、固定SQL SHA和单调开始时间；自然返回后落盘实际返回值、ACK及耗时。ExecuteNonQuery返回−1不是失败判据，也不替代absence。若某DROP报错或外部deadline终止，禁止自动重试该DROP、禁止继续后续写动作；保留真实失败/未知结果。

写流程结束后用**另一条新TEST连接**重新验证身份/物理profile，再batch核全部九表和九精确PK均不存在。正常路径两次DROP或already-absent分支之后都执行这个门；记录完整查询返回再形成absence。遇到写异常，可在原外部预算内做一次同样的新连接只读最终观察，但不能把原失败抹成自然成功，也不能再次DROP。连接Open/identity/query/drop/dispose均写开始/完成及安全耗时；中途被杀的phase保持started，没有完成耗时或推断结果。

外层建议本新工具独立180秒process-group界限。实际只读11条SQL已在约29秒完成，为两表preflight、最多2个DROP和fresh九对象batch提供了具体预算参考；它不证明未来延迟上界。此处不改原runner900/300秒，不盲目重跑双cleanup，超时仍保留partial并停止写。

## 独立关闭与验收门

Low离线合同应先通过完整有效输入/两present基线，再变异inspection hash、身份/九对象集合/两写目标、七项重新出现、table/PK矛盾、marker/列/PK、不合法numeric以及缺fresh收据；显式验证already-absent不DROP、每目标至多一次、任一preflight失败无写、DROP失败无下一写/无重试及partial状态不变成成功。selftest不读取TEST env、不认证、不执行SQL。

实际恢复结束后另起独立进程，使用**已接受、未改字节的v2 readonly host**和新输出目录再次核原完整九entry。只有实际自然完成且九表及九PK全部absence，root才关闭本次残留；恢复host自身的新连接不能取代这一步。若恢复过程失败但独立最终absence成立，应分别保留失败操作事实和残留已消除事实。任一表/PK仍存在或查询未完成继续pending，不用producer成功旗关闭。

本设计批准可实施的精确两表恢复路径，不声称恢复已发生。旧E-v6超时/validator失败和inspection七absent两present均永久保留；残留关闭不接受E-v6行为、不重标v5/v9事实。现有每查询约1.3–3秒的时延说明等待成本真实存在，仍不足以定位服务器、网络或driver根因。E后续组/D/完整C继续依赖各自门；LocalClaude仍在TP08之后。
