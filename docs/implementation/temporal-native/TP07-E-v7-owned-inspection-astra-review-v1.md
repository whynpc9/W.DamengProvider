# TP07 E-v7完整60对象只读inspector源码验收

2026-10-10。结论：**exact_sixty_readonly_inspector_source_accepted / Low_build_selftest_pending / actual_owned_state_pending**。未发现阻塞项。root可安排Low隔离构建及49项离线合同，实际DB只读窗口仍须通过该门后另开；本次不关闭E-v7 owned状态，不激活分窗实现。

本轮只读三源、相对已接受v6 inspector v2的差异及实际v7 JSON，计算hash、逐项比较完整白名单；未执行SDK、自测、DB或探测，未修改代码/旧证据，仅写本报告。

| 新冻结输入 | SHA256 |
| --- | --- |
| Program.cs | `20c83662633216557feb68ff28eaeb1df694845ec9d9d0f2365aa21864e1cb0f` |
| README.md | `7970ddccfa0aa682a899cae3919b8bc06a27bdaa079790ce974447328bfe3507` |
| csproj | `a36c19f435958a32c64df7c8043a70d892178d157fc5a592e1a4dd7abe6100dc` |

实读Program全部60个case/nonce/table/PK四元组，与原完整ledger `4ffde52b2f02a9607ffc338b1451250b42830bf730a966b150d77ca680e8debe`逐项同序相等，且与main60项owned名字对应。Scope额外校验nonce→TPL/PK/marker关系及case/table/PK唯一；没有synthetic/subset入口。全部confirmed、CREATE/COMMENT ACK实际符合；新增按case的sync/async检查分别为ExecuteSqlRaw/ExecuteSqlRawAsync，与真实ledger吻合。

main `e9492e14…`、group `6a9c6be5…`、run `eb4119b7…`、failure `321ddeed…`、control `30ca9602…`及原approval/startup常量均实读匹配。旧triage参数及v6 ledger/main/group/run常量已移除，改为actual v7 control，核main/cleanup双timeout、validator失败、60项complete及窗口释放/最终状态未定。failure的SHA与v6相同是因为两份失败JSON内容相同；代码仍按run的validation**路径+hash**绑定实际v7文件，不接受旧路径偷换。源码中旧v6表仅用于scope拒绝自测，不在白名单或查询参数内。

认证前原引用链、nupkg资产及实际加载W DLL/MVID、外部Low批准的新host DLL/deps/MVID门保留；原candidate/approval/startup继续固定。`--selftest`分支仍在TEST env读取前结束。精确PackageReference、typed PlaintextAllowed、pooling关、Log OFF、15秒ConnectTimeout/12秒CommandTimeout及安全异常输出不变。陌生USER/current schema不回显，身份/物理profile失败不继续目录查询。

两条batch各仅绑定固定60个表名或PK名，使用当前Schema目录；返回最大60行、固定字段数。`TableNames/KeyNames`拒绝NULL/错误类型、未知名字、重复、错PK所属表/约束类型；`BatchConsistent`要求每个表与PK存在性一致，任一孤立PK或无PK的存在表都不能被判安全。`AllAbsent`只接受两项非NULL且完整验证后的空数组，不以缺失query/部分rows推断absence。单表目录只对presence结果中的固定对象运行，限制4列/1marker/1PK/1PK-ID行。

`Columns`方法与已接受v6 v2逐字节相同：nullable可精确Int32表示的原目录facet，非时间列无null/0语义门；D/Z scale7、四列顺序/类型/nullability及width4/9/11/32保持。精确nonce comment及ENABLED唯一PK(ID,position1)门保留。无DDL/DML、business rowcount、trigger、任意SQL或实例/模式操作；目录comment始终精确匹配后只存bool。

开始/完成phase、完整batch结果及对象边界保持原子flush。batch尚未完整返回时只留下started phase，不能声称已完成absence。all_tables_and_PKs_absent_observed是观察旗，完整自然退出/最终control及独立接受仍须另证；Dispose失败使进程失败。没有自动重试、DB fallback或恢复入口。

49项planned selftests静态计数成立：原36项合同保留，新增13项含完整60 batch正例、61行超界、重复/陌生表、PK归属/类型/字段数错误、矛盾状态、partial/complete/present absence与旧v6对象拒绝；此计数不代表已执行。新增60参数batch的实际可运行性也仍由Low后续实际门证明，不能从源码推断服务端已接受。

Low须冻结新source、精确包feed/lock/assets/archive、host DLL/MVID/deps及实际49项自测收据。通过后root选择有限只读process-group界限；任何超时保留部分证据，不能猜剩余数量或临时减少完整ledger。若实际全部absent，仍依独立证据关闭；若任何present，另按真实观察制定限定恢复。只有原60对象独立闭环后，固定15/25窗设计的实施激活门才打开；W、E、已接受v6源码及旧失败保持不变。
