# TP-01 → TP-03 能力与证据交接

状态：**shared_TLS_evidence_reviewed / TP01_handoff_ready / final_frozen**。2026-10-08。TP-03由root另行激活。

本文件承接冻结草稿 `TP01-profile-handoff.md` 的已批准决策，新增逐项实库证据；不回写草稿、历史失败或运行制品。Astra只读报告并作整数/JSON核对，未执行validator API、测试、构建或DB。shared/TLS均已完成表征并经逐项审阅，root此前shared条件批准的TLS前提已满足。本交接不表示新驱动candidate通过。

## 1. shared 证据身份

目录 `.local/temporal-native/verification/TP01-v10-shared-baseline`。

| 制品 | SHA256 |
| --- | --- |
| characterization-validation.json | `57b662eed842779a51cdb4488ca903dbdef9ba56923df602d411b4d7835f2de0` |
| runner.json | `f741373affc7740c7cf6b277e009ab3546aa82d94bdd26e2f9f9deb60ac7b000` |
| core.json | `b8f675c5ad41272b2807f9f568bd010b4e87c943dce530fb048a84b33cb212d7` |
| metadata.json | `e54284c536627aadf7a994029d95deaca3d0209e1336d271257b667773406b3d` |
| extended.json | `308d5db6114f95e73f0d377986819ef59c6728bd1aad681718396cfdf6949294` |
| verification-summary.json | `f46bd129251c539380fb8912bdb0941fce7d5bdada1963289853720bd58538a0` |
| profile-layout-facts.json | `deafa717709ce7301ac1e6a6bd7706ee03d89b48d2ca8293ab698b9c21ffa4c3` |
| core.cleanup.json | `92cfa27d2acf9222db1e2037e04e7b76087a69b4fe3ec0f59c95371d47ed65a9` |
| metadata.cleanup.json | `5130b7d449091d8a357a693e08feb8d540468ef0983615e4b855c00badf250b5` |
| extended.cleanup.json | `fd21faa64f6c9ee131c922fc8c548d39ae7370090a4c12abaf70f2ba5236927e` |

三lane计数260/334/106，共700，进程与独立cleanup均0，owned_table_absent_fresh_connection。validator为baseline_characterization_accepted、issues=[]；其 **candidate_acceptance、full_temporal_acceptance、baseline_capability_acceptance、wire_layout_verified均false**。本文不改这些旗标，而从下面明确操作提出初始profile批准建议。

actual O/W profile相同：serverVersion **8.1.5.60**、messageVersion **21**、encoding **gb18030**、timezone **480**、ordinary_non_BDTA，用户/Schema均WDM_PROVIDER_TEST。传输为显式PlaintextAllowed；不把该环境名或传输开关作为时间能力键。

实际工具source manifest SHA `10652bfcf39ccea1e90ed04aa908e581412a2d0c4f6ec39b75f081a2b94a6db3`；identity JSON SHA `9f18cdfee3b8bb4fba7f08477514ecc68ba47b205c0d9b1c3d8ba5ae5248063d`。归档W产包source manifest SHA `241e15b52b436a55fe3a299e777b281cee0d6b3c22782a0323de311e1d87d1d5`、source HEAD `2b39fec0fa1bc8974f8e491f9bdb5a6113d14631`；比较HEAD `fe89bd44abe99c1608499f71f4634a13e732c826`不是产包source。

| 实际加载身份 | 包 SHA / DLL SHA / MVID |
| --- | --- |
| O DM.DmProvider 8.3.1.47463，net9 | `62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75` / `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b` / `d909b5af-965f-4cf9-a80a-a6181124e361` |
| W 0.1.0-r3.20261007143838，net10 | `6696a26d89e4caa1b9e7c40a73f024c4df9d8a695da1446bdbecc17757d3bcf9` / `c70f7d4b7bd37187f55483d186d658206232a2e1f60a662530b3a0282e3231e5` / `69e5632e-75c4-4672-b924-57b8ffb58a52` |

两者package_reference_verified与actual_asset_verified均true。profile证据来自该归档基线包，不宣称尚未实现的新codec/绑定已经通过。

## 2. shared 七位、六位和范围：按具体操作批准

W core typed_write中七位63次written（TS26 23、TZ27 40），六位12次written（TS16 6、TZ23 6）。每次affected_rows=1、fresh persisted_rows=1，均有O和W两个读取oracle。共150条保存raw由本次只读位置整数核对：日期/时分秒/ns/offset全部等于请求ticks/offset和actual_server字段；hex重算SHA、actual length与metadata width一致。普通TS的EXTRACT decimal分量另用整数请求分量核对一致。W GetValue/typed/generic全部返回精确ticks和原offset；FF9、FF7及后续Read完成均满足。

| 具体能力 | 可定位case IDs（core，writer=W） | 真实布局及结论 |
| --- | --- | --- |
| TS七位 | `dt7_min`, `dt7_min_plus1`, `dt7_max`, `dt7_max_minus1`, `dt7_max_minus9`, `dt7_ordinary_tick1`, `dt7_tail_0…9`, leap/nonleap/midnight | CType26/scale7/width9/cell9；支持已测Gregorian CLR边界与100ns，不舍入 |
| TZ七位 | 同名 `dto7_*`，`offset_0/480/N300/330/N779/840`及相应local/UTC边界 | CType27/scale7/width11/cell11；packed与FF9保原offset；TSTZ EXTRACT false/-6112仍原样保留，精确证明采用独立packed分量 |
| TS/TZ六位 | `dt6_min`, `dt6_max_minus9`, `dt6_ordinary_micro`, `dt6_century_nonleap`, `dt6_midnight`, `dt6_tail_0`及相同dto6组 | 16/6/8-byte与23/6/10-byte；12次写全部精确；28个非整微秒请求真实Overflow，不把失败降级写入 |
| DateTime Kind | `kind_Unspecified`, `kind_Utc`, `kind_Local` | 保输入ticks，W返回Unspecified，未做机器时区平移 |
| offset保守域 | `offset_N779`, `offset_840`及local_min/utc_max或utc_min/local_max | 已见边界分钟成功；`offset_N840/N780`及各2个可构造边界共6次旧W Overflow。保留[-779,840]，不把旧guard拒绝说成服务器全部不支持 |

示例：W `dt7_max` 两reader均raw `0F27FE77DF39933577`、FF9 `9999-12-31 23:59:59.999999900`、ticks3155378975999999999。W `offset_0`两reader均raw `EA87BBEE81C80000000000`、FF9 `.000000100 +00:00`；W `offset_840`尾字节`4803`对应840分钟。

**shared批准建议**：exact 8.1.5.60/msg21/gb18030/ordinary scope可作为TS26/TZ27七位写和计划新默认选择的服务器能力前提，维持六位兼容及上述保守offset域。local/UTC CLR边界均检查；构造器拒绝的组合不是服务器发送用例。并不据有限样本承诺所有历法范围，也不宣称旧W默认推断已稳定：新推断/Precision拒绝/raw冻结仍由TP-04实现并于TP-06验证。

root已接受此逐case建议，TLS收口后批准上述63次七位/12次六位所对应的服务器能力前提；现§5已满足该条件。不得把该决定改写为总baseline或candidate能力旗已通过。

## 3. layout读表与未开放项

| 范围 | v10实际证据 | 接线决定 |
| --- | --- | --- |
| DATE14 | extended `ordinary_date` O/W，scale0/width3/cell3，server2026-07-23；W正确DateTime/date文本 | Date3已观测普通格式；这里是server-seed读，不冒充DateOnly全部typed写范围已验 |
| TIME15 | `ordinary_time6`，scale6/width5/cell5，12:13:14.123456；W1900锚点DateTime | Time5；原对象形态/纯整数投影保留 |
| TIME TZ22 | `ordinary_time_tz6`，scale6/width7/cell7，+05:30；W DTO/GetValue与typed offset正确，generic DateTime拒绝 | TimeTz7；typed DateTime历史local投影与generic拒绝分开 |
| TS16/23、26/27 | §2实际8/10、9/11；mask0、ordinary | 按actual CType+length+format选择明确layout，不把名义CType当实际长度证据 |
| extended列六位 | metadata `extended_column_scale6_layout=layout_unavailable`，D6真实16 | fixed26/27 scale6双约束保持pure/内部fixture覆盖，不伪称本库实测，不能重标D6 |
| 8/9列 | extended `columns_scale8/9`，O `write_scale8/9`、`write_tz_scale8/9`成功，输入为CLR+1tick；W对应NS | W8/9写继续关闭。26/27读layout明确，合法精确文本不新增全局版本拒绝 |
| 8/9非整tick样本 | 所有 `ns_scale8_{1,10,90,100}` / `ns_scale9_{1,99,100}` SQL CAST实际fraction均0 | 不声称10/90/1/99ns真实存取已测；候选精确GetString/CLR拒绝用既定pure fixture验证，不能以case名字冒充实际值 |
| TLTZ | `tltz_metadata_scale6`为16/6/mask4，scale7为26/7/mask4；旧W可写读；`tltz_cross_session_timezone=closed` | 本轮新合同非NULL适用值读写均NS；metadata/NULL不进非NULL gate，不继承普通26能力 |
| ordinary12、BDTA | 无本轮可接受实际布局证据；BuilderBDTA仍关闭 | 不猜布局，不把public legacy12微秒buffer当server ordinary12；内部fixture不授权新实库格式 |

O是独立对照，不是W合同：TZ GetValue可返回DateTime而generic DTO为InvalidCast；NULL generic为安全InvalidCast、typed为Dm6081，GetString可null；七位部分旧O写值丢tick。所有这些保持原始表征，不能要求W模仿，也不能将O错误推定为server不支持。

## 4. 历法、TIME与产品保守策略

v10 extended再次见 `calendar_1582_10_5/10/14` 的SQL CAST折到15，4/15日期本身保持；这些CAST均丢了输入1tick。这是SQL表达式观察，不是逐日typed writer结论。root已批准含日期参数DATE/TS/TZ在1582-10-05…14统一NS，标 `unverified_calendar_transform_region`；SQL不重写，Reader合法raw仍读，pure核心与无连接公开helper仍Gregorian。

year0表达式真实-6118；BCE表达式有返回但缺era的0001文本不证明BCE原值。semantic BCE writer不开放，CLR不可表示域与raw文本层分开。TIME(7/8/9)表达式-6121、TIME24 scale6 -6118；TIME最高六位、hour24不开放写。pure hour24 fixture可保文本，CLR Overflow，不取模。未知服务器约束保留真实DmException，不全部重映射Overflow。

## 5. TLS profile：六位实证，七位保持关闭

目录 `.local/temporal-native/verification/TP01-v10-tls-baseline`，core131/metadata168/extended75，共374条；各run/cleanup均0、fresh owned表不存在。validator为baseline_characterization_accepted、issues=[]，其candidate/full/baseline capability/wire layout总旗仍全false。

| 制品 | SHA256 |
| --- | --- |
| characterization-validation.json | `d64fe3717e812c8d46f8868cec235c7d79c46c57e9bc9577d71d28a7a8954879` |
| runner.json | `7cdcdd0ccd2e3d37f051c703e94f29c49e857bf0d1a513df9409584f658a5468` |
| core.json | `098e59e055435cbbcdc9594c89220254a62611c1062e2a8124ebf02b9170bc75` |
| metadata.json | `ddbfae81665c159b117e157a84c9f66496075c2edbfc28da5e2b371557ac919a` |
| extended.json | `faee7fe2a13b60ace2b5f2c15db00c5a1414dbe43beee2b157db2799bfdf412c` |
| verification-summary.json | `0e9298324b8beaf2b4c00a3cfc757da1b6fa7635ab045f9e757c172446ff006f` |
| profile-layout-facts.json | `8435d95447d93252a38a6d9577b7425224459579af14539c59cc718f9f05c669` |
| core.cleanup.json | `342119f3a5032f2284215fa17871a23771be03027922f386ec959b90dd8fa502` |
| metadata.cleanup.json | `d3e6c0e074e1ba32621124bd3b263daacc53878359c93289abf40f7d7b24328b` |
| extended.cleanup.json | `c6db528c7304d3d14f42c94272ea511f0552ad032227b214c35515800c2f45b0` |

actual W为 **8.1.4.6/msg21/gb18030/tz480/ordinary_non_BDTA/TEST同名Schema**。transport=`RequireTls_local_CA_explicit_NoCheck`：沿既定本地CA策略，未关闭证书验证。工具source manifest、产包source manifest/HEAD、比较HEAD及O/W包/DLL/MVID均与§1完全相同。O资产加载身份可验证，但O连接为`closed: no_verified_strict_official_tls_configuration`；本profile没有O实库读写证据，不能把shared O结果挪入TLS。

W六位12次written，具体六组与§2 `dt6/dto6_{min,max_minus9,ordinary_micro,century_nonleap,midnight,tail_0}`对应，fixed D6/Z6，affected_rows与fresh persisted_rows均1。TS16/scale6/8-byte、TZ23/scale6/10-byte各6条。Astra独立还原保存raw整数并核对SHA/长度，12条均等请求、server字段、typed/generic/GetValue，普通TS EXTRACT亦精确；FF9/FF7及Read完成均满足。其余28个非整微秒六位请求为真实Overflow，fresh rows0。

七位请求走不依赖D7/Z7存在性的 `advisory_profile_request`：63次NotSupported，包括 `dt7_min/max/ordinary_micro`、`dto7_max`、`offset_0`等；6个-840/-780相关请求先触旧offset Overflow。全部persisted_rows0、advisory_no_dml。故记录“七位关闭请求被拒”而不是“69项全部NS”，也不从本地旧guard推出服务器不存在七位。新candidate仍须按已决定的能力优先顺序及严格NS合同验证，不能把baseline允许的Overflow表征套到candidate门。

fixed元数据轴真实选D6/Z6：`bind_DateTime2_s7_punset_fixed_value`和`bind_DateTimeOffset_s7_punset_fixed_value`分别observed列16/6、23/6并被参数拒绝；未改列为7。`extended_column_scale6_layout`仍unavailable，不能冒充fixed26/6实测。ordinary_date/time6/time_tz6实际14/3、15/5、22/7均observed，按已知ordinary布局交接。

TLS的W scale8/9 TS/TZ写均NS；TLTZ metadata16/6/mask4及26/7/mask4可观察，但跨session仍closed，不开放其值读写。TLS新七位/8/9/TLTZ能力不因表征accepted获得授权。**批准建议：仅登记本exact scope的既有普通六位能力与七位closed策略；保持范围保守，不扩大服务器支持。**

## 6. TP-03消费合同（已批准，未激活）

沿草稿§1/4/5：新增内部compiled immutable capability profile（必要新文件由root激活），从本次物理连接协商身份Resolve，无SQL探测、无JSON运行时加载、无Builder新开关。至少分清服务器版本与msgVersion/encoding scope、ordinary/type family、write scales/default-seven、local/UTC/offset及历法排除；不以版本大小继承。

`ValidateWriteTarget(profile,target)`先验证元数据/能力，typed NULL也执行；`ValidateWriteValue(profile,target,Fields)`用于非NULL CLR与semantic text。target必须保留requestedScale、columnScale、encodedScale、明确layout/zone与可靠fixed provenance，不能合成一份effectiveScale丢意图。TP-04负责完整冻结；TP-03先提供适配接口，不反造raw来源。

未知/null版本得到CompatibilityLegacyUnverified，保留普通legacy≤6；可靠fixed已有26/27且列≤6、未请求7的既有窄路径也保留。默认仍legacy，advisory/synthetic不能冒fixed；显式/默认原生七位需求NS，fixed6显式7在unknown亦NS；非整微秒Overflow。8/9/TLTZ等关闭策略先于值的整秒特例，文本不能绕门。

已批准shared profile的fixed26/27 scale6+用户7可允许target，但值必须同时满足两精度；legacy16/23 fixed请求7直接NS。所有纯helper按表示合同无连接运行，不承担服务器profile授权。读取按明确布局与元数据校验，TLTZ独立关；TP-03公开legacy微秒切换时typed七位Reader同候选持续有效。DmGetValue只作最小路由，停写后才交TP-05。

新默认及绑定行为、99 getter cells、坏raw/非整tick、异常恢复及新包source身份由TP-03…TP-06后续验证，不在本次baseline表征中预先关闭。

## 7. 收口结论

shared/TLS逐操作证据、身份和清理均齐备，未发现阻止TP-01交接的真实缺口。可由root关闭TP-01并按已冻结设计派TP-03；初始compiled profile仅承接上述已证七位/六位前提和已批准关闭/兼容规则。TP-02纯核心接受不变，TP-03公开helper/七位Reader连续性、TP-04新绑定、TP-05getter矩阵、TP-06新candidate仍各自验收。

8/9非整tick服务器样本标为 **unavailable / internal-protocol-fixture-only**，fixed extended六位标为 **unobserved / internal-fixture-only**；TP-06按真实能力范围收口，不将计划用例名字或纯fixture冒充实库通过，不为扩写能力增加无界探测。v7/v9最终失败、CAST诊断失败及所有旧报告完整保留。

本文件最终冻结，完成后停止写入。
