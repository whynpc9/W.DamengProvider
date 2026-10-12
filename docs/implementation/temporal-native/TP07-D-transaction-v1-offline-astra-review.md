# TP07-D transaction v1 offline Astra review

结论：**SOURCE_ACCEPTED_IN_SCOPE / OFFLINE_ACCEPTED_IN_SCOPE**。本轮冻结源码、完整 Python 合同、P0 source、N0 native lock、BC 独立 cleanup build 和 shared B0 build/只读 CLI 证明已在各自限定范围内闭合；root随后提交的模型JSON与shared guarded plan静态合同也合规，未发现实质阻塞。D runtime、数据库行为、scaffold/G6/G12、TLS 独立 B0 及 full EF C 均不在本报告接受范围。

本审阅延续 [源码审查](TP07-D-transaction-v1-source-review.md)，其实际 SHA-256 `ef0b185957ea4998e2c101a639456b7256c5706c3941036f2cdbaa09f7c6b858` 未变。依 root 授权，在独立 Low 窗口释放后完成最终证明回读并只新增本报告；未导入项目模块，未执行测试、SDK、CLI、host、数据库或 Git，未修改源码、旧失败、接受证据或协调状态。使用标准库读取文件、JSON、日志及 PE metadata，并核对实际哈希、集合、图和身份。报告写完即冻结停写。

路径以下以 `.local/temporal-native/` 为基准；`verification/` 下均为本轮或明确标注的已冻结历史来源。接受锚点实际字节如下：

| 记录 | SHA-256 |
| --- | --- |
| `root-approvals/TP07-D-transaction-v1-inputs/contracts-acceptance.json` | `8c602ae9a0ebe5d723ad090f6467062e0c36399883438c4f5a988e01ca1d0c11` |
| 同目录 `P0-root-acceptance.json` | `53b8ca4675dc29b25b963cc6047c882350c31c06eba2da7c3c2a9221fde7e51c` |
| 同目录 `N0-root-acceptance.json` | `b89770844f5461d9e085406baa521f4eb8b770dd36c07a621f90927ac6fef49f` |
| 同目录 `BC-root-acceptance.json` | `ca6690f62eb24594bc813e913e029d3281aea40888a0061007248094841fd9dc` |
| 同目录 `shared-B0-approved-execution.json` | `313c03d34bb96b0b505c226fda0019ad5949c20b65fee45a544036959f6ae425` |
| `verification/TP07-D-transaction-v1-shared-build-independent-readback-v2/independent-readback.json` | `88c77feb80105150f44b2735c174c227fa3b94a2b2392dc7c18b18a1a159bda3` |
| 同目录 `artifact-manifest.json` | `7db8962fbf74d478d2d7cbd2f5a8bd79289da239d5dbb196a285da26308a22b9` |
| 同目录 `full-shared-B0-actual-maps.json` | `fa3af024f6ba0a07177e902b7b30163e0f26451d6af303ab1a2ce5af30c3b1f1` |
| 同目录 `actual-ref-hashes.json` | `5e2ef06d6f01164ee2764feea1accbf576b688a647759efadcbfcd8d575f4ce4` |

源码范围保持原接受：D25 当前 source-pack manifest 为 `84d189450f0f080aa6aefc1dc8675a7d11d0d028642fa10cc133c1e663cb47f6`，精确25项，仅 validator/test 两项较旧 pack 更新，其余23项不变，pure16 来源仍为原 `runtime-validate.py` `d9ea3985…`。prep/source门消费完整当前175→178→185来源，原175中173不变、2修改、3新增，原六个 package lock 与 unit-lock provenance保留。pack旁17字段 v4 `source-manifest.json` 仅为历史元数据，不被当作当前三字段 source-pack membership；此事实已由源码审查核实，本报告未重新标记该旧元数据。

完整合同已有实际执行证据。独立回读原 design/preparation 日志及各自哈希、收据与唯一 case ID：分别 `Ran 58 tests` 和 `Ran 16 tests`，均 `OK`、自然退出0，无失败、错误或跳过。其来源是原44/9方法 AST 保留后新增14/7，不以 AST计数替代结果。当前审阅没有重跑合同。

P0 实际 workflow196、cleanup185 的完整非 bin/obj path→SHA 集合与 manifest相等，完整 P0 producer树427文件也与保留的 producer-final-state相等。workflow由当前 product178、原L0七模板、D十模板、CLI配置一项组成；cleanup为178+7。两者继承同一当前L0十项lineage、W和六origin Ref。原source producer仅执行一次、自然退出0；最初 consumer 的 `KeyError: 'tool_files'` 不被改写为producer失败或隐去。后续只读reader修正与raw1802/High1803/control1817集合差异，已由最终proof明确其仅差历史approval/监督输入；没有重跑P0生产链。P0最终独立读回 SHA 为 `abe2347bd705634fbfabb0e8f16c76ff1f178122cf3bc79f31c64561c22abaab`，root接受的六个直接Ref均实际匹配。P0准备包含33 baseline包记录、W archive及六文件CLI bundle，不声称已完成SDK/native/CLI执行。

N0 实际198源与完整当前 workflow196加L0/D两个host lock相等；provider lock本就在178产品集合内。原3条SDK命令为版本查询与L0/D实际native restore，记录退出0、无超时/终止、每事件300秒预算，日志哈希匹配。native-proof `d8228b06b190ff788539b55bb7b8e6e105159f0cec6a639ca02d06a3257dc134` 和 lock manifest `5f3fd1cc7f8fe4dca2f3b87ec874758dd82dd9bfb0954abc931a9c2ce89bd586` 绑定当前source/lineage/W。独立核对实际cache中的33包：metadata `version`均为整数2，`contentHash`与原记录及每个实际lock occurrence一致；full ZIP SHA512与sidecar相等，且33项均与NuGet contentHash不同，未混用两种hash域。真实lock并集33，D-only20、L0-only9、shared4；provider/L0/D graph分别13/13/24。lock/assets/dgspec实际Ref、包集合、dependency映射及各自replay project路径一致。799项相关producer保护文件实际重hash无漂移。N0独立读回 `cb6b16aaf0ad6b04f4fe9c115c258814df72a081288c85bfdf3673cde70ec947` 与root五个直接Ref均相符。

BC 实际186源等于cleanup185加原L0 host lock；独立root、2 active locks、provider/L0两份assets、13/13 graph正确。原3条SDK事件自然成功，无CLI执行、无DB。实际25个binary文件及两assets均与构建/startup记录相符。以独立标准库PE metadata解析回读四个DLL身份：Literal host MVID `4952eb55-d4cf-4cda-bb0f-8ce934f3e84b`；EF build及host copy同为 `7f6012d5-b50f-4c31-86fe-d8ca6ecd7cc9`；W为 `aa23e673-957c-4b1e-bf0f-291ff60077e6`，哈希全部匹配。775项相关保护文件实际重hash无漂移。

BC 原consumer拒绝原因 `actual cleanup active host assets` 保留于 `TP07-D-transaction-v1-cleanup-build-control/independent-verification-failure.json`（SHA `2fe2d520e71180684180da088c7dfb4464f9a26228576fe42fb160d9cf361a60`）。后续只读consumer按真实两assets及完整native/binary集合核对，没有重跑SDK或更改producer制品。BC最终独立读回 `fd3a43bd82529eea237dfb72c93f2c5b56be2e232b1c58437bac78c69dbbaedb` 与root十个直接Ref均相符。独立cleanup build的接受不表示已运行TEST连接或清理任何runtime ledger。

shared B0 原producer只执行一次。`shared-build-control-v2/execution-receipt.json` 实际 SHA `a40d28d9f30ea386f6c1bc5e0988c57ffe49d048007f47d5379820a75711dcb6`：PID/PGID45537、5.716860916931182秒、自然退出0、timeout=false、termination为空、group_absent=true。更外层executor PID/PGID45343自然退出0；其47.96937112510204秒包含hash监督，不冒充SDK耗时。最初监督器在producer启动前因 `original High supplied inventory covered` 拒绝，原失败 SHA `8c92fb195bc285d380b91a58b0573ad4c1f1f7c52903bb752ff4b901a0b85782` 保留；这是历史journal值与当前授权journal值的比较假设问题，不是首次B0构建失败。v2对非journal历史覆盖及当前journal单独核验，未改变原producer argv/env/1620秒/reap2与5+3事件范围。旧失败后没有第二次producer、SDK或CLI重跑。

shared B0 的核心制品实际字节：

| `verification/TP07-D-transaction-v1-shared-build/` 下文件 | SHA-256 |
| --- | --- |
| `source-manifest.json` | `5ca49bd47804568f35521526c8f073053847b05d0b817bc1ea013a6fa1b6d2c1` |
| `lock-manifest.json` | `d07b78913f18c58c13d23f703bdae1964b16d909cca0b28192ecd34bff849cec` |
| `startup-manifest.json` | `00dde05dd148acc04e77271057ae41dd68b5255b36c9771536f0d7b8d21191d2` |
| `build-receipt.json` | `6ad9231896b30fc934bd3b80ba24abe1596e0e79b4c6b4143a7a0d7937385b3d` |
| `preparation-receipt.json` | `874c944994574df7babd1ef0903b9c512fe8086c2f2682a8dbf8b19309e76e1d` |
| `cli-proof.json` | `21f47070bbb2771a7f34434a9a9c96afbd0e93b9233fe54d8726f81642525511` |
| `cli-tool-manifest.json` | `df3169769f68a55f61ad73b9664e979d9985954d49ae6ff66ced55fe94740e0f` |

独立枚举确认B0实际198非bin/obj源精确等于N0；source/lock/startup/build集合相等，3 active locks、3 assets、125个binary文件实际hash一致。provider/L0/D各自lock、assets、dgspec graph为13/13/24，D仅原九项framework pruning，真实project root正确。B0自己的native cache1595文件与Low完整map相等；33 baseline包metadata整数2、native contentHash/所有lock occurrence和ZIP SHA512域核对正确，W package/hash/metadata/sidecar也独立核实。B0源、lineage/W与N0一致，802项相关producer保护文件及framework/pruning实际inputs无漂移。

原5次SDK为10.0.401版本查询及L0、D各一次locked restore/build，使用原单节点/禁复用/禁共享编译/禁build servers参数。原3次只读CLI为版本、runtime列表、`dotnet exec <P0 tool_root>/dotnet-ef.dll --version`；日志实际确认SDK10.0.401、EF CLI10.0.12、Microsoft.NETCore.App与Microsoft.AspNetCore.App均10.0.12。8条事件均exit_code/host_exit_code=0，无超时/termination，300秒预算，原日志实际SHA相符。每条SDK/CLI原helper未持久化子进程PID/PGID，本报告只确认原producer和outer executor持久化的PID/PGID/group absence，不补造逐子进程身份。CLI六文件同时匹配实际目录与package ZIP字节，原host、local tool manifest、package、两targeting pack Ref与shared framework/hash集合正确；未采用resolver fallback。

B0两host与EF/W实际身份均由本审阅独立PE metadata解析核对：Literal host MVID `a697a683-ee79-430d-a3ce-c576f7c33eaf`、Design host `d65f73dc-cb53-40e4-8b76-ac023b051ba6`；EF build与两个host copies均为 `f02a71ec-5b10-4f29-bc56-173c123864b1`，SHA `b4ad53fc953adf8fa61259dff5c7a8313c1bc212fea8340b56f57f3bec73cf58`；两个W copies均为 `aa23e673-957c-4b1e-bf0f-291ff60077e6`，SHA `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`。deps哈希与对应host记录相符。BC/B0的EF输出身份因构建root不同而不同，不影响同一当前product178来源，也不被强行要求同DLL SHA。

Low最终proof完整闭环另行核实：`artifact-manifest.json`的4050个实际文件引用逐一SHA相符；`actual-ref-hashes.json`的2827引用逐一相符，这两个集合有重叠，不相加冒充独立文件数。枚举B0 producer树2007文件与完整map全等、cache1595与map全等；source198/lock3/assets3/binary125与原manifest/receipt全等。related-before/after90项全等且实际hash相符。完整保护集116677项before/after无漂移属于Low已冻结监督结果；本审阅读取其证明而不重复110k全量扫描，独立实际回读范围为上述4050/2827及相关源/native/graph/log/binary/CLI集合。

来源/模型边界保持当前事实：L0实际validation SHA `266a3054f496cb23d6f1d738e26d90e6b2f6e83fdde3c60328568328d63dfa1b`、run `2c76e364215ca32067286f8c75759279fe56905569d859a49ef013b3ae6e6da9`；E v3 actual validation `7ddd3743f8df499c178352965552961f7c5fd5d7e3217c27cf2d23358d723d56`、run `5905de5a13df036b0553568711957397efa0cf13cf95f306e57b9f1402c6417c`、root限定接受 `3815a8a821633b0f4ff250ed2e59b30371081340d5c8b772b9cb30c9395f08a0` 均实际相符。L0/E/D原六项加transaction三项overlay与W一致。E原producer acceptance=false仍保留，root对25windows/338cases（shared180、TLS158）的接受仅限E值/运行合同；不据此接受D。E registry实际只有roundtrip/six/mutation/generated四group，default7/health是case子集，不能新造额外group。

Root在本报告最终冻结前追加授权审阅 `root-approvals/TP07-D-transaction-v1-runtime/` 的九份纯JSON。该追加审阅没有执行validator模块或运行计划；149个唯一直接Ref实际SHA全部匹配，190次`evidence_refs`引用全部可解析为JSON，Markdown接受记录只位于独立basis Ref，没有误放进JSON证据数组。消费形状与已审源码一致：两个model gate的`accepted_gate_refs`精确为L0/E、同profile/W/九项overlay，accepted记录均`issues=[]`；TLS的`seven_closed_refs`精确为E/TP06，两记录均同W、tls、`seven_write=false`且有真实接受与非空JSON证据。

| 上述runtime目录文件 | SHA-256 |
| --- | --- |
| `model-gate-shared.json` | `b7ebd5eb04d2fcf462074c094771cd0dab77e34e22032b0c4143e990f52b6d7c` |
| `model-gate-tls.json` | `74690b3f1de0f1f5bdd2ae3666878e427632d3d39451036f36000630bac96020` |
| `L0-shared-accepted.json` | `2c720c96e4d3d4f8746a9bf9c046ec4814231232398b1db0dbd09522a5a1d9c4` |
| `L0-tls-accepted.json` | `2c81fd74d5a8529d436b9cf71e3d6f21a31142c7d1ee00426fe4cc8ea539c4de` |
| `E-shared-accepted.json` | `8bf8a6fbb27c324222e8c8f83fad763788daef771339d79302a547ab90a74d16` |
| `E-tls-accepted.json` | `7aea5f4f98f0953512852bb3c8b63da510289487c165041e20e4d56e1df8f00f` |
| `E-tls-seven-closed-accepted.json` | `173ec01c24eaae8ea65198e4f4abecc6e259017854298ffdeee54ac100cbc927` |
| `TP06-tls-seven-closed-accepted.json` | `679ebb57a10871dde42ba7ce2f19f0bb029b8e865a9dd61684e2ba6420149341` |
| `shared-guarded-plan.json` | `e274f06dc86f00f8d4b95957c26a8e0534123985ef2c9de9392e7f4d59ac32a7` |

L0 adapters绑定当前validation266a/run2c76/startupa1cc及同profile main/ownership/cleanup；实际main为shared12、TLS2、process exit0，cleanup均complete/exit0，root正式接受basis为 `98360bef1373af0485b54bca455d929fe5fc79e220d7e47b2bc2912abe893a70`。E adapters绑定当前validation7ddd、whole run5905和root3815，并逐一包含对应profile全部group receipt、window receipt、validation、main/runtime与cleanup/report：shared13窗口60+54+12+54=180，TLS12窗口52+56+12+38=158，同run_id/cohort_id，没有以case子集或历史轮拼接。E root/Astra basis分别为 `a02833133c6c83982d87a486403bdbc744121eafbc1a2394fe4c8eba92af9136` / `c202d85a8dfe4d17281f6ab490d84af9355ee9909b8a90ff97881fc4f2f0c362`，保留原producer false与fullEF false。实际E lineage保留独有`E_template_source_sha256=deb47b…`；其`runtime_template_source_sha256`当前仍为与L0/D相同的`2a3affe…`。九overlay比较允许合法阶段模板差异，没有人为覆盖额外E模板字段。

TLS closed7的E记录引用同一完整TLS158证据，scope保留同步business-send count未观察边界。TP06记录引用v12 TLS实际validator `cf98d22a722d1339f5f1b4bedc9d51a6855c46cb84469143e51397ec6825af13` 与v8 supplement runtime/validation/cleanup/ownership；supplement原summary仍为当时root pending，后来的正式TP06 basis `141c9ac3ece890195c7af69376967bd74e9aa0b472d11dffba98342b84c6b142` 已关闭同包主矩阵及补充验证。未把六位观察、同步发送未观察或早期pending自行改成七位写支持。

shared guarded plan的18字段集合已与validator `plan()`的实际AST required集合精确比较。stage=guarded、profile=shared、parent_checkpoint=null；source pack84d、B0 startup00dd/build6ad9/CLI df316、BC startup8d69/build68fe及model gate b7eb全部绑定实际冻结文件，workflow/cleanup根不同。原helper proof88424d实际纯16 AST相等及transitive closure为true，candidate/capabilities/original TEST wrapper与policy pin一致，Python实际字节匹配。没有第19字段、host替换、timeout延长、retry或runtime捷径。因此这些纯JSON可作为后续原DAG的合规输入，本报告本身不构成执行授权。

源码与本轮P0/N0/BC/shared-B0离线链现已接受，模型适配与shared guarded plan静态审阅完成；未执行TEST连接、SQL/CLI模型流程、guarded、scaffold或cleanup ledger。后续仍按原设计DAG和明确运行授权使用冻结制品，不能继承旧D v6或本报告为D runtime/full EF C接受，也不由本报告扩大预算、产品范围或新增门槛。
