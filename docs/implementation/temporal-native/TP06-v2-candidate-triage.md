# TP06 v2 candidate validator 失败只读裁决

2026-10-09。结论：**validator_contract_defect / product_change_not_required_for_this_failure**。当前失败把参数编码合同用于独立的结果列合同；W GetString 的七位输出符合本次服务器 ResultColumn 声明。允许工具 owner 在新 revision 中定点修正规则并经 Low 独立验收，但本报告不把旧 candidate failure 改成通过，也不关闭 TP06。

本次仅读取源码/JSON并用独立只读脚本统计字段、计算哈希；未调用 validator/suite、build/tests/DB，未修改产品、工具、旧产物或 TP07 设计。Low 已释放窗口，EF 仍未激活。

## 1. 精确失败与证据

原结果 `.local/temporal-native/verification/TP06-v2-shared-candidate-validation.json`：`validation_failed`，唯一首失败为 `W:inferred_dt_tail_stability:reprepare:oracle:W:GetString:declared_text_precision`，candidate/full旗均false。它是fail-fast结果，不表示后续所有合同已经验证。

实读 `TP06-v2-shared-candidate/metadata.json` 的 `cases[6]`，确认其 case_id=`inferred_dt_tail_stability`、driver_lane=`W`；`phase_results[2]`=`reprepare`：

| 层次 | 实际证据 |
| --- | --- |
| 请求 | requested_scale=6、requested_precision=0；expected ticks=`639204129167654320` |
| 执行参数 | effective CType16、width8、semantic_scale6；EncodedScale6且来源 immutable_temporal_binding；TypeFlag2 advisory |
| 原参数describe | execute前后raw保持 ParameterDescribe/CType26/width9/scale7；不是fixed约束，未被客户端编码决定重写 |
| 结果metadata | ResultColumn/CType26/width9/semantic_scale7，TypeFlag0；raw CType26/precision9/scale7，mask与derived mask0 |
| schema/cell | schema_scales=[7]；ordinary非BDTA，已经fetch的cell长9，SHA=`8c5f1b725a9c8899499f93e5b54a75718e8604c7559f563b1ef44d9f5f8cf9ac` |
| 文本oracle | server FF9=`2026-07-23 14:15:16.765432000`；FF7及W GetString=`2026-07-23 14:15:16.7654320` |
| CLR/状态 | typed/generic/GetValue均返回DateTime、上述相同ticks、Unspecified；后续Read完成，SELECT persisted_rows=0 |

FF9 fields为2026-07-23 14:15:16、ns765432000，与请求及CLR ticks精确一致。初始/替换阶段输入和结果均26/7，替换尾tick保留；reprepare选择legacy6仅改变写参数，服务器表达式仍返回26/7。advisory观察只存cell长度/hash，没有`raw_oracle.synthetic_temporal_hex`，因此本报告不冒称重新独立解码了该9字节packet；已有ResultColumn raw来源与FF9/CLR一致性是本次判定依据，core独立packed oracle门照旧。

runner完成 core260、metadata334、extended106 共700条观察，三lane进程/cleanup均0，清理记录均 `owned_table_absent_fresh_connection`。这些是收集完成事实，不能代替candidate语义validator成功。

## 2. 根因与影响范围

`eng/temporal-precision.validate.py` 与本次冻结 `frozen-input/temporal-precision.validate.py` 完全同SHA。`validate_stability`约第672–680行从预期写参数取ctype/scale并严格核参数metadata与EncodedScale后，直接把该tuple传给`exact_oracles`。后者约第366–378行用同一个scale检查GetString字符数、结果metadata、schema及cell布局。因此六位写参数被错误要求必须返回六位/legacy结果。

`validate_binding` advisory分支约第629–630行同样传`effective["scale"]`/`effective["c_type"]`，存在同因；只修stability单case不足。`tools/TemporalBoundaryProbe/contracts.py:295`的`binding_expectation`本身定义的是稳定**写入选型**，本次不应改变其精度/Precision/类型选择或拒绝规则。

producer `Program.cs:524`的ParameterQuery是 `SELECT V,TO_CHAR(V,…FF9…),TO_CHAR(V,…FF7…) FROM (SELECT :v AS V FROM DUAL)`；没有保证服务器V结果descriptor必须等于客户端发参descriptor。`CaptureAdvisoryOracle:613`分别记录真实result metadata/schema/cell、服务器格式及getter；ActualServer.semantic_scale来自column metadata，并非另一个独立精度源。

独立只读统计本次W成功advisory phase：24项26/7→26/7、16项27/7→27/7；另外**65 phase、35 case**写读tuple不同，其中36项16/6→26/7、29项23/6→27/7。65中60为非NULL、5为NULL；不能称65全被GetString字符长度首门拒绝。NULL原路径先进入null_oracle而跳过非NULL文本检查，NULL6081/DBNull合同继续保留。

## 3. 限定修正规则

新增独立 advisory result 合同层，供上述两个调用点使用。顺序如下：

1. 按原contracts校验请求、实际parameter CType/scale/width、EncodedScale、原ParameterDescribe快照前后不变、statement ownership/prepare/reprepare与持久化事实，**全部保持严格**。
2. 结果tuple必须先属于可接受集合，不能将任意observed scale直接作为expected。保留原来**write tuple==read tuple**的合法分支；只对本次已证 shared exact physical scope `(8.1.5.60,msg21,utf-8,override_empty=true)`、同一已冻结ParameterQuery、candidate W advisory观察额外允许 `(16,6)→(26,7)` 与 `(23,6)→(27,7)`。不存在cross-family、任意scale0…9、TLTZ/BDTA/ordinary12放宽，也不把原本合法legacy16/6结果强制升级26/7。
3. 对选定的结果tuple严格核真实ResultColumn role/TypeFlag0、captured_server_source、raw CType/precision/scale与effective一致、width9或11、mask0/derived0、schema精度、actual row format与cell长度/hash事实；ParameterDescribe不能冒充结果raw。非NULL GetString必须精确等于原请求数值/offset，且字符数恰为**已接受结果列scale**。FF9/FF7、三CLR投影、后续Read照原门校验。
4. NULL同样独立核可用result descriptor/schema，但不制造payload或GetString文本。继续原NULL context、6081 receipt、DBNull与null-cell规则，不以此次修正规避NULL来源门。
5. fixed列、core writes、extended8/9/TLTZ、能力profile、package/source/ownership/cleanup门不改变。advisory result合同不反向赋予七位写能力；TLS未经本候选观察不继承此shared例外。

新合同测试需正常control先成功，再逐一变异：删/增七位尾零；数值相等但文本6/8位；ns/ticks/offset改变；schema/raw_scale冲突；result raw角色换ParameterDescribe/Synthetic；raw CType/width或TypeFlag错；跨TS/TZ族；mask/TLTZ/BDTA/未知布局；结果8/9或错误shared physical身份；parameter encoded_scale改7但声明请求6；原fixed/core字段突变。原equal16/6及23/6控制继续通过，合法shared mismatch DT/DTO与typedNULL都有正控制。不能靠特殊case_id绕过全局精度校验。

## 4. TLS历史只读对照

实读 `TP01-v11-tls-baseline/metadata.json`，SHA=`83f69d9422b256d28f82085f5a874bf0816680875d582d6bbd4a3b76f5c69cfa`，physical为8.1.4.6/msg11/gb18030/empty override。W成功advisory154 phase：89项16/6→16/6、65项23/6→23/6；两个stability的initial/reprepare也均相等六位。

旧baseline包没有新raw metadata capture，raw_source=null、状态unobservable_after_legacy_normalization_not_inferred；它只提供历史effective/result六位观察，不能补造raw provenance，不能代替本候选TLS门。TP06-v2 TLS core/metadata/extended/validator均未运行，保持pending。

## 5. 新verifier重判旧不可变观察的边界

root提出只修validator/合同测试、producer C#及产品完全不变时，Low先离线审新verifier，再对已保存700观察重判；**接受该路线，无需仅为规则修正重复相同DB I/O**。新输出必须进入全新revalidation目录，旧failure、runner、reports、cleanup、source/archive/identities/capabilities和旧工具manifest全部只读。若新规则需要旧记录未采集的事实，或出现真实产品/producer改动、另一个实库缺口，再由root决定最小重跑范围；不能用未采集字段推测通过。

原`_validate`第1096–1098行同时核 frozen contracts、packed oracle以及validator自身与producer工具hash。新verifier不能直接删掉selfhash检查、伪装`__file__`，也不能复制旧目录后重写manifest来让selfhash过关。应新增显式revalidation lineage入口：

- 原producer完整tools清单仍逐项核旧hash（含旧validator `7b4f…72f`），原runner与build assets/lock/toolDLL/package/source/profile/cleanup等验证不减。
- 独立新verifier manifest绑定新verifier自身、依赖、合同测试、reviewed patch/规则ID的精确SHA及root接受身份；默认原生产验收入口继续要求原selfhash规则。若contracts/packed未改，仍强制等于producer冻结版本；若确需改变contracts，必须显式列入verifier overlay并另审，不允许隐式导入任意checkout模块。
- 新重判记录绑定旧报告/manifest/archive/runner/原失败结果的实际SHA、新verifier manifest SHA和新输出SHA，清楚分开 producer_revision 与 verifier_revision。不能将新verifier说成当时采集用的工具，也不把旧candidate失败原地改成成功。
- Low先运行新validator正负合同，再调用新重判入口；本次仅给设计接受，没有执行该入口。新结果若成功也只关闭其实际shared candidate范围，TLS、补充工具与TP07仍按各自门等待。

### High 窄窗与可审接口

root建议的唯一owner为原High `tp4_binding`，限定写 `eng/temporal-precision.validate.py`、`tools/TemporalBoundaryProbe/test_validator_contracts.py` 及必要的新手写fixture；不得修改 Program.cs、runner、contracts.py、packed_oracle.py、既有失败/报告或producer manifest。此分工与Supplement v2独立审阅隔离。

建议保持原CLI默认行为，新增仅供重判的显式可选参数组：`--revalidation-manifest <new-lineage.json> --revalidation-manifest-sha256 <root-frozen-sha>`，两个参数必须同时出现；`--output` 必须是新文件且在原证据目录之外。无此参数组仍执行原同revision selfhash约束，不能因检测到旧hash而自动切换宽松模式。

新lineage schema至少包含：

- `schema_version=1`、明确`task=TP06`、`scope=shared_advisory_result_tuple_v1`、producer evidence root及root批准规则manifest的路径/SHA。规则manifest绑定exact physical tuple、冻结Program/ParameterQuery来源hash和上述两个允许mismatch；不得接受JSON任意注入额外scale/类型集合。
- `producer_evidence` 精确列出旧runner、frozen manifest、三个lane原report/cleanup/ownership相关收据、原candidate failure及runtime summary的相对路径/SHA；旧product archive/identity/capabilities/包/build资产仍沿原runner/frozen完整门验证。清单重复、路径逃逸、缺失/多余必需lane或hash不符均拒绝。
- `producer_policy_sha256` 为旧validator，`verifier_files`精确列新validator、合同测试及新增fixture的路径/SHA；根批准manifest SHA由Low派发值独立传入，不由待验文件自报可信。selfhash在重判模式改为校验**新verifier_files中的当前执行文件**，同时旧producer中的旧validator照旧工具表校验。contracts与packed必须保持原SHA；禁止环境变量、异常fallback或import monkeypatch替换依赖。
- 输出写独立`producer_revision`/`verifier_revision`，绑定上述manifest、旧failure和每个实际输入hash，记录新判断/issue及既有false边界。输出拒绝覆盖、拒绝落在旧目录；原证据前后哈希由Low复核。新verifier不能修改输入以取得成功。

新增lineage负例须覆盖：只传一个参数、manifest自身SHA错、换旧失败/runner/report、producer工具变更、当前新verifier selfhash错、contracts/packed替换、规则扩大到TLS/跨族/scale8、输出位于旧目录/已存在文件。High停写→Astra审规则及lineage实现→新工具freeze→Low正负合同与旧真实报告重判；没有新运行通过前仍保持原TP06失败状态。

## 6. 本轮证据身份

| 文件 | SHA256 |
| --- | --- |
| `TP06-v2-shared-candidate/metadata.json` | `4f9215a6b6d95920aa87ec35291bf05e1593087cad7ba6900beae7e3abaebd3d` |
| `TP06-v2-shared-candidate/runner.json` | `80c288a1504fd56b581af69d6a73c42d98f65624c872bbbd67a8fa3d765b852f` |
| `TP06-v2-shared-candidate-validation.json` | `a7a3134f237caae8334631389f76d3bccc93f27bfc4d790d8dd557fb4e6588f7` |
| `TP06-v2-runtime-verification-summary.json` | `5ee729b855504e034e821def8b908c099c14b627537698f3bc39a60c78f0f18e` |
| 原冻结/current validator | `7b4f23ee1281bfe4e8bc1a5004f39bcab8b5dce27072df1ce4e00d79ef23e72f` |
| 原冻结/current contracts.py | `f8ad9abddd86e415374b9594954f2264fc5d701e3f603c7a96ed77e14dbca6cb` |

制品相对 `.local/temporal-native/verification/`。原候选W为 `0.1.0-temporal.20261008144915`，package SHA `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL SHA `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。本裁决不要求修改/重打该包；是否存在后续其它失败由新严格validator决定。

报告完成后停止写入。
