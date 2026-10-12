# NEW L0 transaction v2 offline 接受

2026-10-11。结论：**实际Python35、prepare185、native186、locked build及编译后grammar35的offline证据闭合，无阻塞；root可以据此单独开放本次新startup的原14项runtime窗口。** 本文不接受实际runtime，不关闭NEW L0，不启动NEW E338/D。此次仅读取文件、JSON、AST、哈希及纯PE/ECMA元数据，未执行项目模块、Git、tests、SDK、DB或程序集；唯一写入为本文。

## 本轮固定身份

依据 [v2源码审阅](TP07-L0-transaction-v2-source-review.md)，其SHA为 `edc0a5668bf1f115304c9ad2080377c848e3a12f1c3302cfa335e8261e26e924`。六工具实际aggregate仍为 `45cb21fd852f4714afd354da32ac8ab91e23843187df360ab8dd27b05d145dc7`，本次有限toolpack manifest SHA为 `d4220ee78441af4d79b5ced3d4d10fe2cd3e1b6b391efa0dd337a9cc3e092170`，各阶段及当前文件绑定同一六工具。

| 实际制品 | SHA-256 |
| --- | --- |
| prepare `source-manifest.json` | `4f50b6188f13b7718f6dbabdc853ab63868c6ac5a36456cf690d012e91695faf` |
| prepare `prepare-receipt.json` | `ef3abe1d545e7c476bfc7f26700741abe8b5a3720672fdb1b4f600b34ec17601` |
| lock `lock-manifest.json` | `6982a1b12187c1f9ed18810222b5d8347528d4d758822b6697f8f4b5b074ecbb` |
| build `startup-manifest.json` | `a1cc53af8ef27db498777cda211975e941270ec903fb830d23387c780cd237a5` |
| build `build-receipt.json` | `4acd06ffb80f7cce93e9c73411b2e1f5a638da0fa4992f64beb3f428728223cc` |
| build `literal-grammar/report.json` | `7678c62632706bfaefe6a61312ef236a1f21d24acb6fdbd0f4d7f499575a835f` |

以上目录分别为 `.local/temporal-native/verification/TP07-L0-transaction-v2-{prepare,lock,build}`。完整Low build readback SHA为 `fe3952aea11b694df208369d8c304680bacd943b2cdbfaf37983f56c77149898`；contracts/prepare/lock readback分别为 `aeaa9f27ad2aea4c9a8a042a482feec0b6ab13d7bf7eb6449f593b85b171f291`、`cfa23c9fc3c5b6faa9135c527bede6651008aa9c1913fb50c45153e89e1160de`、`de4bcbdffe022961faf571927334f6b87321368ece6cee8148eed1c2b78605cb`。

## 实际验证与独立读回

**Python与重放。** 本次35个实际唯一测试名逐项等于当前AST测试集合，35 passed、0 failed/errors/skipped，自然exit0。prepare已真实产生六条按bridge/mapping/transaction check/apply排序的receipt；本轮逐条核argv、cwd、日志SHA、整数自然exit/host-exit0、无timeout/termination，均匹配。实际完整185逐文件等于签定产品178加原七模板；旧175 map仍为dc2b来源，准确173不变、DI/retry两修改、三个新增。签定d13f patch、4001产品manifest、66c3有限源码批准和十项lineage/artifacts保持，三阶段所有来源SHA及phase receipt引用一致。旧v1首失败不被此成功覆盖。

**Native锁。** 实际186仅比185增加 `test/TP07LiteralProbe/packages.lock.json`，SHA `896258992bb91e4289953fef191287f8e6fe496706d6a3df070a85eb5dc5916e`；原六个历史锁全部保持，provider锁不变。SDK10.0.401 version与force-evaluate restore两个实际进程自然0。host图15节点为14包加一个provider project；两项目原生assets/dgspec共四文件。

本轮独立核14个实际archive SHA256、metadata SHA256/version2、ZIP字节SHA512/sidecar，并核每包native contentHash在provider lock、host lock和metadata间相等；13非W包的版本/档案另与原cache proof逐项相等，新metadata绑定准确。`consumer-initial-domain-error.json` 保留了Low最初把ZIP sidecar与native contentHash混为同一域的错误；这不是producer失败。`independent-cacheproof13-supplement.json`保留补充核验，未改源码/原锁/旧证据、未重跑SDK。W同包身份没有变化。

**Locked build与编译后grammar。** 四个实际事件依次为version、locked restore、build --no-restore、实际host DLL grammar调用，全部自然0、无timeout/termination，四份日志SHA匹配；launcher约4.354秒，PID/PGID16856已消失。startup实际186与已审lock逐文件一致，两个assets和七锁绑定正确，生成目录之外没有额外来源文件。两项目bin闭包共29文件，20个managed DLL记录；不把host目录单独的文件数混作两项目总数。

grammar35逐例case_id、literal、literal SHA、expected/actual接受值及错误类型/code与固定policy一致：12接受、23预期拒绝、35 passed。工具身份与实际新host一致；`expected_only/database_accessed/candidate_acceptance/literal_runtime_accepted`均为false。它证明实际编译host的grammar合同，不证明任何数据库literal往返。

| 实际执行候选 | DLL SHA-256 | 纯PE/ECMA独立读回 MVID |
| --- | --- | --- |
| L0 host | `f717b1b72dcfcfac1d4fbc7ddb1fe97ff7327151d0e689753dd7021dd7b3fcf2` | `47ede0c7-9017-4a1e-adf3-8f5ac397c91c` |
| EF provider | `6b255d3a5d96c7ca7755dea65f2b4b0650b5744722fae8875ae2bfca1fe743f2` | `7b5f799a-42f7-4d38-bccc-7120a9ee5de4` |
| W | `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980` | `aa23e673-957c-4b1e-bf0f-291ff60077e6` |

W版本仍为 `0.1.0-temporal.20261008144915`，package SHA仍为 `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`。本轮未加载任何程序集。

**保护与证据限度。** 独立重新哈希四阶段artifact maps（9/396/2717/2848项）全部匹配；四阶段before/after保护清单（22445/22446/22844/25564项）全等，当前实际文件也全匹配。Low记录的原独立source/六receipt/grammar消费者检查已完成；本轮另以只读独立字段和物理闭包核对相同关系，没有把producer声明当作唯一依据。原receipt不提供每个内层SDK/Git PID，保留这一既有界限，不补造进程证据。

## 下一窗口边界

本次build receipt仍明确 `runtime_not_authorized=true`、`database_accessed=false`，实际命令只含source/locks批准。可由root针对上述**新startup SHA及d422工具包**另给原runtime flags/命令授权：全新shared12→TLS2、每profile原main300秒及独立cleanup180秒、原TEST wrapper与身份检查、fresh owned objects及精确清理，不拼接或复用旧profile结果，不改变TLS、raw/typed/FF9/+100ns或literal oracle。

实际14项与正式独立validator仍待执行；最终必须核完整before/after、case集合和值、对象清理与新startup绑定后，再由Astra/root限定NEW L0收口。本文只接受offline到可授权runtime的边界，冻结后停写。
