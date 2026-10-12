# TP06 主 verifier v12 独立静态审阅

2026-10-09。结论：**static_review_accepted / independent_contracts_and_revalidation_pending**。未发现本轮范围内的静态阻塞。root 可据本报告独立制作真实 rules/lineage批准记录，再交Low执行新工具合同及旧不可变观察重判；本报告不是700条candidate已经通过。

本轮只读对照 [冻结triage](TP06-v2-candidate-triage.md)（SHA `1e0926b03501ee8072cddeb7a6e348f01020b3d7539732328701c3702ec2151e`）、v11 producer工具与v12两文件差异，核查Low source/patch身份。未运行测试、validator API/CLI、旧报告重判、build或DB，未修改产品/工具。

## 1. 读写合同修复

- `validate_stability` 与 `validate_binding` 的advisory分支均调用新增 `advisory_result_oracles`；两者原parameter metadata、EncodedScale、请求Precision/Scale、describe保留及statement/ownership断言没有改动。
- 结果先限制TS/TZ正确语义族。写读tuple相等继续合法；不相等只允许 `(16,6)→(26,7)`、`(23,6)→(27,7)`，且必须exact shared物理事实及原ParameterQuery Program SHA。无版本大小继承、任意0…9、TLS/新Supplement SQL例外。
- 结果metadata按read tuple完整核raw ResultColumn来源、effective/raw type/scale/width、mask/TypeFlag、role与ordinary格式；schema随结果列精度校验。非NULLcell仍需实际已fetch来源、布局长度/hash；GetString精确数值/offset与声明位数、FF9/FF7、typed/generic/GetValue和Read完成由原exact_oracles继续验证。
- NULL也先核结果descriptor/schema/format，随后进入原null-cell、DBNull、6081与已知NULL receipt门；不创造NULL payload或文本。fixed列、core写入、extended、TLTZ及独立packed oracle主合同没有修改。
- advisory producer未采集独立raw packet hex的现状保持；新代码没有以合成raw补真实观察。此修复不扩大写能力，不替代Supplement G2自己的新SQL实测。

## 2. 重判身份链与输出

CLI强制 `--revalidation-manifest` 与 `--revalidation-manifest-sha256` 成对；root独立提供的manifest SHA为启用重判模式的边界。默认入口继续执行当前validator与producer冻结validator相同的selfhash规则，没有自动降级。

重判manifest严格限定字段、TP06 scope及20个角色：6个根证据、三lane各report/cleanup/ownership/final共12项、旧failure/runtime summary两项。检查角色集合/数量、实际路径唯一、规范相对路径/根目录包含关系、逐项SHA，root指定旧failure仍须candidate validation_failed且两接受旗false。旧summary与failure由root manifest绑定具体文件hash，不由工具重新生成。

rules manifest固定规则ID、精确physical、两tuple集合、ParameterQuery原Program SHA及triage SHA；root_approved必须真实bool，root_acceptance_id非空。reviewed patch以独立路径/SHA收据绑定，不能通过JSON扩大类型/精度/profile范围。

verifier输入严格为7个唯一文件：新validator、其合同测试、原contracts/packed/Program及两fixture。当前执行文件路径与SHA、实际import依赖路径均校验；contracts/packed必须与原producer冻结版本相同，Program固定原SHA。原producer工具表仍逐项核全部hash，旧validator按其旧policy SHA核验，并保留runner的assets/lock/toolDLL与既有包/source/profile/cleanup验证。

校验后再复核producer 20项、lineage/rules/review patch、新verifier文件、原工具及build资产，变化会使candidate false。结果分别记producer_revision/verifier_revision及旧failure SHA。新结果与 `.lineage.json` sidecar均排他创建；重判输出拒绝进入原producer目录，拒绝已存在结果/sidecar，sidecar绑定结果实际SHA及root manifest SHA。不会为了新selfhash修改旧manifest或伪装原producer。

实际run由root选择全新输出目录，Low核对返回码、结果与sidecar齐全并保留旧输入前后hash。若写文件阶段失败，不能仅凭已存在的部分结果宣称重判完成。root还需把受审patch和这组实际source身份填入批准manifest；测试里的synthetic root receipt不是实际授权记录。

## 3. 合同测试静态覆盖

新增7个test方法；原测试主体未删除。正常control先接受，再做单点变异并重新绑定报告hash，覆盖equal legacy、DT/DTO两个shared例外、NULL、两个advisory调用点，以及尾零/数值/ticks/offset/FF9、raw来源/type/width/flag/schema/mask/BDTA/layout、错误query/profile、EncodedScale与fixed/core不退化。

lineage fixture明确使用synthetic旧policy/归档/批准收据，不宣称是真实产品。它验证默认路径拒绝旧selfhash、显式重判可调用同一 `_validate`，并核旧输入不变。角色/缺失/重复/escape、旧report/cleanup/ownership/archive、producer工具、新self/test/dependency hash及root规则/patch变异均有负例。

CLI测试直接调用实际 `main()`，只替换argv并捕获输出，不mock验证算法；覆盖成对参数、producer目录内输出、已存在结果/sidecar及成功输出SHA收据。重判正控制、语义正控制分别存在；编译/执行事实仍等待Low，不能把源码中这些assert存在计为通过。

## 4. 冻结身份

Low闭包是**17个唯一文件**，两fixture已在原17项内，不是19。逐文件实读workspace与Low冻结source均匹配manifest，15项与v11相同，仅以下2项变化：

| 输入 | SHA256 |
| --- | --- |
| `eng/temporal-precision.validate.py` | `96faba6fe24e488921eeb702bc2c69cccea74316681d34596f6752dab1b3eb14` |
| `tools/TemporalBoundaryProbe/test_validator_contracts.py` | `dfe4ebf1227e373afd140faa939098dacaf2f1c11012419c77d62e76d962efc9` |
| `.local/temporal-native/verification/TP06-main-verifier-v12/source-manifest.json` | `87ca22a0fa4576697cf8397ee71bb55e1c40096b0f82483f937bea7429cb3fb3` |
| 同目录 `v11-to-v12.patch` | `e5a114f21ad4375aa17a73fb7d29c74360d38c864d3da2db75aa6e7b25ae21ad` |

Program/runner/contracts/packed均未改变；不改W包，不触Supplement工具或旧证据。新工具合同、shared报告重判与TLS实际候选门仍分别pending。完成后停止写入。
