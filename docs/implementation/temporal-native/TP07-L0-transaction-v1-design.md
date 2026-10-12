# NEW L0：已批准事务产品178来源接入设计

2026-10-11。设计结论：**只需修改既有三个Python文件，以完整175→178→185→186来源链重跑NEW L0**。T1已由[root接受报告](TP07-T1-acceptance.md)关闭；本设计不重新实施T1、不继承旧L0启动/运行接受，也不开始NEW E338或D。本文仅设计，未执行SDK、tests、项目import、prepare、DB或secret wrapper，唯一写入为本文。

## 1. 文件范围和不变输入

High唯一代码范围：

- `tools/DownstreamAcceptance/TP07/runtime-run.py`
- `tools/DownstreamAcceptance/TP07/runtime-validate.py`
- `tools/DownstreamAcceptance/TP07/test_runtime_contracts.py`

`runtime-cases.json`、七份L0模板、`managed_identity.py`、`packed_oracle.py`保持原字节。六文件TOOLPACK_FILES不增减；不给新helper建立包外import，不修改T1或E/D工具。不升级现有schema_version1，不缩减既有历史字段。source/locks/runtime继续由root分别明确授权原flags及精确命令，不为此次接线新增有限JSON runtime approval系统。

原ROOT计算、单节点FLAGS、环境白名单、30秒git apply进程、SDK300秒、每profile主进程300秒及独立cleanup180秒保持。TEST原wrapper、先身份校验、唯一owned对象和fresh absence、严格TLS、候选W tuple、shared12/TLS2、35场实际编译grammar、literal/raw/typed/FF9/+100ns全部原样。不得以本次来源接线扩大预算、缩减case或放宽精度oracle。

## 2. 固定批准链及新增CLI

现有 `--approved-source` / `--approved-source-manifest` / 对应SHA继续指向**旧批准175**，不能改名义指向178。旧manifest `dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6` 的原unit-lock provenance保持。

新增且仅在prepare阶段必填的六个CLI参数：

| 参数 | 唯一批准输入 |
| --- | --- |
| `--product-manifest` / `--product-manifest-sha256` | `tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json`；`400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8` |
| `--transaction-patch` / `--transaction-patch-sha256` | 同目录 `transaction-capability.v3.patch`；`d13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f` |
| `--product-source-approval` / `--product-source-approval-sha256` | `.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json`；`66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f` |

新输入不是caller任意声明的hash：runner/validator内部各有有限的本轮固定相对路径/SHA合同，实际path/hash同时匹配。产品source_root由签定manifest取得并核为当前workspace下 `.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source`，不另开任意copy-root参数。后续lock/build/run/validator由已绑定source/startup取相同三refs，不再允许CLI替换为另一产品。

产品manifest必须是实际固定43字段、schema1/revision3及已审W tuple；七字段source approval必须精确匹配schema1、原task、status `source_approved`、scope `transaction_capability_and_w_retry_contract_tests_only`、新manifest ref、exact_count178和排序exact_paths。旧scope、旧批准、bool冒充整数、缺/多字段或指向另一manifest均拒绝。不得接受旧source-only批准覆盖新runtime：其作用始终只是证明产品源码范围。

固定manifest完整SHA已绑定全部内容，不需复制T1数百行retry清单算法。仍须独立验证与本次来源接线有关的语义：base批准ref及175map、prepared-base ref `9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a`、bridge/mapping来源、manifest内全部固定path/SHA引用实际未漂移、W tuple、patch五路径及173/2/3精确差异、178真实字节/路径/aggregate。不得仅检查 `source_count>=178` 或作者的changed/unchanged计数。

## 3. prepare必须实际逐层重放

现有runner prepare:169–230是改动点。顺序固定，首个失败停止：

1. 在创建新输出及启动任何git子进程前，读取上述固定refs并执行纯来源合同；核旧175真实闭包、当前178真实闭包及批准。拒绝不安全relative路径、链接替代、额外文件和跨root输入，不读取secret。
2. 保留原安全archive解包→bridge `git apply --check`/apply→mapping前两处允许的P1锁覆盖→mapping check/apply→唯一unit lock覆盖→**完整等于旧批准175**。原approved175身份和每次中间map仍真实核验，不能用复制178取代此链。
3. 只有175等值成立后，第三组label固定为 `transaction`，对唯一签定d13f patch执行原30秒process的check/apply，保存真实两条命令/自然退出码/log SHA。完整prepare现在有三组、六条patch命令。patch hash、manifest.patch ref与CLI三方一致；不得接受任意额外patch或直接复制DI/新增文件。
4. apply后实际完整inventory必须等于manifest178，aggregate按原声明格式重算为 `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c`。相对175准确为：173原路径同hash；modified仅 `src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs` 和 `test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs`；added仅两个Storage/Internal事务adapter/factory和 `test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs`。集合与hash差异必须实际推导，不能只信manifest数组。
5. 再按未改POLICY复制原七模板到原 `test/` 目标路径，要求与产品178不重叠，完整等于178+7=**185**。原模板manifest/SHA和七字节检查保持。每个新ref及来源原件前后不变，之后才能记录prepare完成。

DI三产品及retry测试已在T1验收，本步骤不改其语义；W package `da8349a2…`、DLL `843710be…` / MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`不变。旧175批准绝不能被写成“本来包含178”。

## 4. 最小manifest扩展和全阶段消费

保持既有顶层schema与字段含义，只增加以下信息：

- prepared source中的 `approved_ef_source_hashes`继续存旧175；新增 `product_ef_source_hashes`，恰为签定产品178map。`source_hashes`为178+固定七模板的185map。
- 现有lineage七字段全保留，新增 `transaction_product_manifest_sha256`、`transaction_patch_sha256`、`transaction_source_approval_sha256`，形成固定十字段。原 `approved_ef_source_manifest_sha256`继续等于dc2b。
- 现有lineage_artifacts七refs全保留，新增 `transaction_product_manifest`、`transaction_patch`、`transaction_source_approval`三个 `{path,sha256}`，形成固定十refs；新增三个SHA各与其ref严格相等。不得额外插入任意helper/文件ref。

这三个新增ref经现有startup继承及run的lineage_artifacts保护路径自然进入before/after哈希。manifest中的固定嵌套来源refs和用于核对的175/178原源，也应加入本次来源保护清单；不得只冻结外层JSON却忽略其读取的真实文件。lock继续通过source_manifest SHA与完整十字段lineage绑定，无须重新发明独立产品批准结构。

runner以少量**文件内纯函数**实现读取固定产品refs、比较175/178差异及计算185/186期望闭包；validator独立实现相同公开合同，不能import runner/T1 prepare作为其oracle。可借鉴已接受T1的严格类型、exact-key、relative-path及source approval判定，但不动态执行或隐藏依赖该脚本；六文件工具包不变。

实际调用位置：prepare入口及apply后；offline的lock/build入口在SDK前、输出manifest/startup前；run入口在原wrapper/DB启动前及结束读回；独立 `validate_artifact` 在读取runtime成功结果之前。每处都核新refs、原175/current178关系和对应闭包，不只比较自报lineage/count。现有internal-host的toolpack/startup hash、环境及child receipt合同保持，外层run在激活前完成新的产品门，C# CandidateGuard继续绑定完整新startup SHA并检查其source/locks/assets；不改变其schema或七份模板。

lock实际只允许增加 `test/TP07LiteralProbe/packages.lock.json`，由185得到**186**。provider及其它历史锁不得变化；沿用原L0最小native图、真实contentHash/档案/依赖检查，不混用T1单位51记录图。build须消费这个新186锁闭包、locked restore后原值不变，产生全新EF provider/L0 DLL、deps、assets及startup，不能复用旧175启动清单或只给旧二进制重签新manifest。

validator在现有three-stage lineage检查处独立要求：源175map等于原dc2b；新增178map等于actual4001；source185 map严格等于178并七模板；lock186仅新增上述host lock；startup map逐项等于lock；三阶段十字段lineage、新十refs及批准相同。对prepare receipt核三组check/apply命令顺序、确切patch路径、自然0/无timeout和真实日志hash，不能仅看 `independent_replay_equal=true`。

保持原完整source检查，不得为了容纳build产物改成“只检查列出的文件”。纯prepare目录实际inventory恰185；有SDK产物时，仅允许原两个实际构建项目的bin/obj生成目录作为输出，源码/锁投影仍恰185或186。额外source、props/targets、helper或其它位置锁均拒绝；assets继续固定原两project集合。

## 5. 合同测试与证据边界

原24个 `test_*` 方法AST及断言保持；新增独立测试类，不通过删case、削弱原body或给原fixture伪造SDK成功来接线。原internal-host等已有测试的有限职责不扩张，新product门在上述外层阶段及独立纯函数测试中验证。

新增正例必须实际读取已批准dc2b/4001/d13f/66c3及真实175/178字节，通过runner与validator的纯合同函数；先证明正例有效，再做以下语义负例。固定ref/hash门和解码后的语义门分开测试：篡改真实ref不能靠重新计算假SHA或mock sha绕过，而语义门可以用已读对象的独立副本注入矛盾以测试真正的关系检查。不得monkeypatch ROOT指向缺失旧基线的临时树或import包外T1代码。

最低负例集合：旧manifest/approval或错误scope；ref/path/hash漂移；43/7字段缺失/多余及bool计数；old175被换成178；base175 map不等dc2b；DI或retry单文件漏apply；任一新增事务文件缺失；173不变路径偷偷修改；多改一文件/多添文件；patch_paths/modified/added集合交换；aggregate错误或实际source字节/路径漂移；七模板替换/重叠；185遗漏产品文件或额外文件；lock除hostlock之外再改一项；startup/lock/source新的三SHA串线；旧startup没有新refs；新增ref未被纳入before/after。可用subtests覆盖有限变异，但失败码和作用阶段须可辨。

这些都是来源/合同单元测试，不运行SDK或DB，也不构成新的prepare/build/runtime接受。若验证阶段入口拒绝前不会执行SDK，可用“调用即失败”的sentinel确认process未被调用；不得mock成功返回后宣称已完成源重放或构建。新增实际git重放留给Low新的prepare窗口。

## 6. 授权与验收顺序

1. root读本文后给High上述三个文件范围。High停写，Astra源码审阅；Low冻结新六工具pack、记录原24方法AST不变及其它三pack成员/七模板原字节，再实际执行全部Python合同。首失败保全后交还High，不能把作者自检算独立接受。
2. 源码和Python接受后，root另开NEW prepare窗口：原175重放加签定五路径，真实六条git命令，新source185/readback。旧L0 report `3d4ca…`、startup `30a12…`和历史runtime仍只读。
3. root读回新185后开放NEW native lock窗口，真实SDK建立186；核原L0依赖、W同包、旧锁及仅新增hostlock。随后root认可新锁并开放NEW locked build，核实际provider/L0 DLL/deps/assets/来源链及原35项实际C# grammar。
4. Astra/Low完成新offline来源接受，root再给**新实际startup SHA和新工具包**的明确runtime授权。按原整轮shared12→TLS2、原300/180预算执行主进程与独立精确cleanup；正式validator独立核产品来源链和全部值合同、原始FF9/raw/+100ns、TEST身份及fresh owned absence。不得resume或拼接旧profile成功记录。
5. Low完整保护项及实际制品回读、Astra最终限定L0验收、root收口之后才启动NEW E338；NEW D继续等其依赖。未到门时不能顺带修改E/D、TP08或发起PR。

185/186及shared12/TLS2/grammar35是本设计的明确预期，不是新运行结果。任何真实失败保留、释放窗口、定点修复再冻结；不把历史通过改写成当前通过。设计完成后停写，等待root据此安排High。
