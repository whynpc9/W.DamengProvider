# TP07 T1 probe v3：V2-R01 定点复审

2026-10-10。结论：**scoped_source_accepted / V2-R01_closed / execution_gates_pending**。实际完整17源与交接身份一致，仅validator/test两文件发生获准变更；未发现该定点修复剩余的确定源码阻塞。此接受只到新probe源码范围，不表示Python合同、C#编译/单位、native、build、readiness或runtime通过。

本轮只读源码、JSON、文件SHA与AST；未导入或运行项目模块、tests、validator、SDK、host、wrapper、DB，未读取secret、未修改源码、旧报告/证据或status，仅新增本报告。nullable/reference接口风险仍按原独立元数据检查及真实编译门处理，没有将尚未确认的风险写成已发生的编译失败。

## 1. 精确接受身份与旧证据保持

| v3输入 | 实算SHA256 |
| --- | --- |
| validate.py | `25d416d2f4a646499b4956d0c35608f593bdab1957a924755c6f60cb71898e0e` |
| test_contracts.py | `83115f3063c65cf252e65eedaaf19e392b7d22175507215f03717c467c68a352` |
| 两文件aggregate | `4f07cf6f97665678e6146b4703c1cb70829b294283f1cee2aecb46601d952f2b` |
| 未变producer15 aggregate | `3d22e651aaf4802bd651ea3c377107a194b58e15b39797ff29ccfa474bf11b96` |
| **完整17源aggregate** | **`dfa05185b1e61977999a22599ecb075d2b4b46bfc9d0649c7795ce80e82750f2`** |

路径基准为 `tools/DownstreamAcceptance/TP07/transaction-capability-probe/`；aggregate以该目录相对路径排序，串联 `digest + "  " + path + "\n"` 的UTF-8字节实算。其余15项逐文件SHA与 [v2完整表](TP07-T1-probe-source-review-v2.md)相同。本轮独立比较实际live与Low保存的v2全部17项，差异恰为上表两文件。

旧v2报告实算仍为 `a64934090af4c5edd62fbd5839f816bfb0647b2955a27d24be7d799595365c8d`；Low v2 `source-manifest.json`仍为 `b0cf6450c2ac2c52b6977c6c43d0460fbd442383aea8c88542772de600aef3d2`，其17项不可变copy全部逐项符合原manifest。旧toolpack `2028d26c32ec8de6521aee7fa53ad31f67b3454a501290cdfc412290cb6992a2`作为新合同的只读producer来源fixture，不冒称v3 toolpack。v1未曾完整冻结的历史限制不变，未补造旧source。

收尾时Low已完成真实v3完整17 snapshot和单独九host源副本；本审阅另行逐项核snapshot/live17与manifest相等、九源字节与其新manifest相等。路径均以 `.local/temporal-native/verification/` 为根：

| v3实际artifact | 实算SHA256 |
| --- | --- |
| TP07-T1-probe-tools-v3/source-manifest.json | `dbf8424b5b7e1a057d46d845dc1cf8af321dff5c0efd6a439a1e1724528d8785` |
| TP07-T1-probe-tools-v3/toolpack-manifest.json | `913d710ce6ff149d0a29919f9b26c9fe092e526620baaa9c39eb9e6c02e3c7f2` |
| TP07-T1-probe-tools-v3/host-source-manifest.json | `df8c164e5774ac9157492d7d9aaf4ca50dddcf53cafd8c877c9a8fc19e30d818` |
| TP07-T1-probe-tools-v3-control/static-readback.json | `9bb7d88504bfabe7b9bd853a6341fb6289faba30a509e97dc6653c5c4270986a` |

九源manifest指向新v3/probe-source受控根，root可据实际新Ref签inputs；九host内容未变没有被冒称为新SDK事件。这些均是静态来源证据。

## 2. V2-R01 实际关闭

本轮对v2/v3 AST和文本diff核实：旧validator只有 `toolpack_contract` 函数改变，新增纯 `policy_contract`；其余既有函数/类AST完全不变。toolpack_contract完成真实17项文件/hash/执行位置检查后，把实际读取policy传给新函数，再继续核五项helper来源。没有绕过toolpack入口、扩大root、改旧module.__file__或调用producer来给独立validator提供结论。

新policy_contract的顶层精确键集合与实际policy JSON完全相等。旧字段仍独立固定比较，11项新增字段各自通过递归、类型严格的exact核对：required_input_keys、probe_source_manifest、native_hash_domains、termination_reap_budget_seconds、process_receipt_keys、termination_reasons、termination_unknown_suffix、transaction_event_binding_fields、connection_binding_sources、binding_callbacks、offline_registry。未知key、缺key、错类型、数组换序都不会被忽略；没有additionalProperties或兼容旧12-key inputs。

实际有限值与v2 producer政策相同：inputs13键；process10键；execution180秒、reap2秒；五种失败termination基值及 `_reap_unknown` 后缀，自然成功仍单独要求无termination/signal；三个event binding字段、三个来源枚举、Started/Used各sync/async四callback；offline registry为rollback16/factory4/commit8，共28且live_commit_allowed=false。native full-ZIP sidecar与NuGet contentHash两域的文字合同没有改为相等；Core13和runtime Core12+W规则保持。

特别澄清：**policy内嵌probe_source_manifest对象为六成员**（task、required_keys、source_count、source_paths、aggregate_format、source_root_scope），它描述的**独立probe-source artifact为七键**（schema_version、task、source_root、source_hashes、source_count、source_aggregate_sha256、aggregate_format）。v2报告把前者称为七成员的文字不准确，本报告纠正；旧报告及其SHA不修改。v3代码把两层严格分开，host九相对路径/排序、count9、aggregate格式和受控snapshot scope都精确固定。

## 3. 新合同源码覆盖及真实/合成边界

独立AST比较确认旧34个test方法逐字义AST不变；新增六方法，总数40，未运行。六方法分别覆盖实际冻结policy正例、实际当前17-toolpack与inputs链正例、11项missing/value/type/extra、所有新增数组顺序/类型/缺多项、嵌套对象键及整数/bool类型、17文件漂移与重签policy/toolpack后的独立拒绝。

新positive先核实际policy SHA `1a11cbba01a1b9626151fd99676aadc88f66a8151bfd04fd4dab3713f7d33b35`，并从真实ancestor定位只读v2 toolpack及固定SHA，用它交叉核未变producer15。随后复制**实际当前17源字节**和五份已审helper到临时工作根，从那里真实文件路径导入新validator；validator.HERE和__file__必须自然对应这个临时工具目录，没有赋值伪造ROOT或旧模块身份。生成的Ref均对应真实fixture文件字节；独立九host源副本实际来自当前host，toolpack与hostmanifest实际哈希闭合。

fixture里的178产品源、SDK/Python标识、NuGet占位和root批准占位明确标为synthetic input only；它们不执行，也不进入product_manifest/native/build/offline/runtime接受函数。该正例验证的是 **inputs→当前policy→实际17工具源→helper5→九host源Ref** 的完整入口链，不能称为真实产品source、真实SDK/native或runtime全链证明。设计的160组新增有限负变异覆盖上述字段/数组/嵌套类型、17文件漂移和11项重新计算引用后的拒绝，数量是静态计划，不是执行结果或160个已通过测试。

## 4. v1修复与运行安全合同没有回退

producer15全部字节未变，因此v2已审的B01必需Ref/九源/lineage/snapshot联结、B02独立摘要域及cache-feed/nuspec、B04TLS专用变量内存归一、B05失败后finally release、B06实际Started/Used对象绑定与terminal current Connection=null、B07取消/timeout进程组回收及partial收据保持原实现。独立validator中除policy入口外的相应代码AST不变，B03Core13/runtime12区分同样保持。五helper副本本轮再次与原文件逐字相等。

正式DI/factory/adapter身份、ReadCommitted、两次actual capabilities、AutoSavepointsEnabled=true、observer实际options注册、同事务/连接GUID与引用ID、callback真实API、shared同轮正控制和TLS零事件证明、D/Z quantum1/10、fullRollback/fresh/N/schema/raw oracle、exact-ledger双清理、180秒首失败停止与_reap_unknown不可接受条件均没有变化。source接受没有把collector布尔值、合成JSON或空events本身变成运行证据。

## 5. 下一门与接受限制

source范围现可交root安排Low后续独立门；具体调度授权仍由root给出。本报告不授权当前直接运行tests或DB。基于已核实新完整v3 snapshot/toolpack/host-source Ref签root inputs后，仍需处理已单列的fixed-reference nullable/回调签名确认，并由Low真实构建核API与TreatWarningsAsErrors。任何经确认的产品/host/fixture修订都需新freeze及受影响复核，不能沿用本SHA接受不同字节。

**40项Python合同、28场compiled DI/stub、产品正式unit、新native锁/包图、source187/locked188、provider/host DLL/MVID/deps、readiness、四例真实事务probe均仍pending。** source-only产品approval `ac8ff44fb570050d478190b8c1908f55cc0fbd5cd2d7a78bc1051d230dff6b4b`不替代这些门。新overlay的L0、E338、D及完整C重新绑定与接受仍独立，旧失败、partial与清理事实不回写。
