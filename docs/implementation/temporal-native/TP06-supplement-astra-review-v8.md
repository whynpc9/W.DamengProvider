# Supplement v8 TLS read6定点静态验收

2026-10-09。结论：**static_accepted_for_independent_verification**。实现符合 [v7运行triage/v8设计](TP06-supplement-v7-runtime-triage.md) 的两项精确TLS例外，未发现新静态阻塞。可交Low完整离线后在新目录重跑shared42/TLS36；此次无SDK、测试、validator或DB运行，没有修改产品/工具。

Low快照 `.local/temporal-native/verification/TP06-supplement-v8/source-manifest.json` 实读SHA256 `cc0447ed4717fb25ddfa0c3a9637b17fb5a217115946ea20afbedca1e4907c39`，root/Low提供17文件aggregate `ba7dd8d29c0d73a9fbb1c1a5449df469911ebd337bb474eadff13445ad494afc`。与v7逐文件比较仅以下四文件变化，13份producer输入不变：

| 文件 | v8 SHA256 |
| --- | --- |
| contracts.py | f7cde0f78df6d58c561881aadcc2ec4b5fae85fa9e1fc7f36db8a22f0efe4886 |
| validate.py | fbf8be0f35265c5c12554fe805bdabe9fb3add19d3058610b3b0fdc63bd34b36 |
| test_validator_contracts.py | 9bf585eaea732842c1e645f63ae6182e5e4e2872c3175fa336f99c79467eb5a8 |
| case-manifest.json | b2d431d8e5006da3e385ac28fbad64cbdde3c972ada14cf6258d1022d46667e3 |

新增rule仅在exact TLS physical、Cases.cs 0360…、新设计SHA259b358…及decision `root-20261009-supplement-tls-g2-read6-v1`全部匹配时启用。case/stage进一步限定为BIND_METADATA_AUTOREPREPARE_dt.scale3的16/3/8→16/6/8，以及dto.scale3的23/3/10→23/6/10。只进入binding=true且无期望异常的成功stage；没有扩到其它case/stage、TLS7、cross-family、任意scale或固定列DML。

实际read_context仍来自已验report的W_profile、实际冻结Cases文件hash及完整manifest匹配；validate.py分别传入原shared decision/设计pin与新TLS decision/设计pin，两者没有互相替代。新TLS标识缺失或错误不能开启read6；原equal/shared分支保留。producer不提供可自行批准的expected或root bool。

写入参数/Scale3、parameter metadata、EncodedScale3、原值量子、actions/Prepare/API、command/parameter/connection/clone/reset/reopen全部保持原严格检查，发生在read选择之前。所选read6仍进入原完整observation：真实ResultColumn/raw/schema/cell、独立packet decode、typed/default/provider值、offset、FF9及GetString六位尾零均不可省略。结果列表仍在整个报告和生命周期检查通过后才发布；synthetic的observed=false与未观察备选状态保持。

新增6组方法先用本次DT/DTO原始hex/hash、ticks639204129161230000、DTO offset330及两通道文本建立成功控制，再分别破坏physical/SQL/design/decision、write/EncodedScale/actions/量子、source/hash/schema/ticks/offset/FF9/GetString/connection。wrong-case、wrong-stage、shared误用、cross-family及TLS7反例均实际进入新selector相关路径，而非用无关输入错误代替。原166方法保留，现172项Python尚未运行；C#producer及56个实际离线检查完全不变。

Cases0360、SQL/API/case/vector、产品包、v4恢复、interval双通道/单负号、诊断安全输出、ownership/cleanup和其它身份门无变化。v7两环境leading实际fixed DAY2分支的已存事实保持；未走的advisory服务器号pending防线也未放宽，不需为此次规则批准错误号。

不新增lineage或历史报告overlay。v7 shared42接受与TLS原pending都保留，v8通过新freeze/离线后完整重验两profile，再由root汇总TP06；本静态接受不替代新运行结果。
