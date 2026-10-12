# TP07-E transaction v3 TLS6 concurrency source review

结论：`SOURCE_ACCEPTED_IN_SCOPE`。仅接受冻结的 TLS6 并发证据校验与合成合同源码；未发现必须修复的源码阻塞。39 个 unittest 方法为静态计划数（原36＋新增3），本次 `NOT_RUN`。不接受历史失败窗口、当前 E runtime、D、完整 EF-C、TP07/TP08 或发布。

## 冻结输入与独立静态核验

- Handoff：`.local/temporal-native/verification/TP07-E-transaction-v3-TLS-concurrency-implementation-handoff/freeze-manifest.json`，SHA256 `daf20d7a1ec069ef0473d3c199ec845a9eeb0e7b2987a555f103c1cdb3ec1f79`。
- `tools/DownstreamAcceptance/TP07/ef-validate.py`：`5b534116340abdaa48bfd0555b5a177ee8462e4ff51dbb4846cdd28ec76758ec`。
- `tools/DownstreamAcceptance/TP07/test_ef_contracts.py`：`41ca79e7d5db4a3996a288617c713656d5491186d35205d5d66640d4da4498bb`。
- 两文件补丁 `TLS6-concurrency.patch`：`e393c054254169d7c95050356b28886ef927f39242c9c28353453f3bfaeadc65`；`static-AST-proof.json`：`fa6288a48de27062a7bedc36254ded07890143ef7eb9cf2b90d73b20224955b3`。

本次使用标准库文件读取、JSON、哈希及 AST 解析，独立核对了全部 handoff 文件 SHA、当前两源码与冻结副本、原始副本及全部 actual_fixture_input_refs。独立归一化结果：validator 只新增 `tls_concurrency_parameters`，原 `validate_case` 只新增一条 `profile == 'tls' and scale == 6` 的 elif 调用；移除这两处后完整模块 AST 与原版相同。测试仅新增独立 fixture 和三个方法；移除后完整模块 AST 相同，原36方法逐一 AST 相同。`concurrency_parameters`、`raw_tuple`、`published` 的完整函数字节及 AST 均未改变。

High 的保护回读声明107021项全部不变；本审阅核验该 before-map、after-check 与 scope 文件本身的冻结哈希，未重复声称亲自逐项重哈希全部107021项。排除范围仅两目标源码、新handoff及明确列出的两个协调文档。旧最终拒绝报告 `122bca2b69d797a26ca6d9224f846449d48f04a0bf026360388a378b5addf5e0` 和失败 runtime `e01a13acfcaaa25dc2fa2d6a5a734599e077a68fe32837ff5eeff96acdeb2ea1` 实际哈希保持。

## 合同审阅

新 helper（validator第928行起）仅接受四个已注册 TLS/mutation/concurrency DT/DTO sync/async case，scale严格整数6、原ticks严格为639204129161234560、DTO offset330。第1414行的精确分支保留 shared7 原路径及其他原路径；未改变通用 fixed-only 判断。

角色由受控原值、winner＋10/stale＋20和完整四参数映射联合确定，不把SQL哈希当作WHERE证据。DT映射为 current 0/1、N 1/3、ID 2/0、original 3/2；DTO为 N 0/1、current 1/2、ID 2/0、original 3/3。current必须 fixed1，original必须 typed nonfixed2；原始tuple分别严格限定16/8/6或23/10/6、零mask、ParameterDescribe和Unknown格式，拒绝generic、extended7及伪造fixed original。N与ID的raw、值及无temporal binding也同时验证。

校验包含request类型/precision/显式scale/Input、raw整数域和发布前后保留、current column6、original column null/reliable false、encoded/wire/requested全6及VerifiedSix。既有 `source()` 绑定实际EF command、Prepare响应、调用身份、参数序及public请求；新helper追加严格response0/sql0。winner/stale须同SQL形状、不同attempt/EF ID/operation，角色名称映射稳定；async Execute帧必须存在且属于同四元operation。sync不伪造发送观测。

原token、当前/陈旧tracking值、准确并发异常链、fresh winner＋10/N=changed、owned catalog不变，以及独立raw整数解码/FF9/typed读值和DTO offset均继续检查。调用者仍检查seed并在完整report路径执行原ledger、主清理和独立清理合同。helper没有向传入report/item写值，也未改变产品异常、事务、编码或TLS能力边界。

## 合成合同与限制

`tls_concurrency_fixture`（测试第289行起）明确为独立synthetic数据，以冻结TLS观察的域和角色映射组装；未调用或变形shared fixture。其调用的 `parameter`、`raw`、`packet_oracle`、`physical` 等函数签名与实际源码相符；packet仅使用已有测试位运算。正例覆盖DT/DTO×sync/async，直接调用helper和完整 `validate_case`，并确认原 `raw_tuple/published` 仍拒绝此nonfixed original。

两个负例矩阵通过完整case或限定helper检查角色互换、fixed/nonfixed/generic/family/width/scale/mask、request及publication、参数缺失重复、Prepare/EF/operation/async帧、SQL完成与形状一致性、ticks/offset、异常、fresh状态和独立oracle；另检查scope、seed fixed-only、winner调用完成和原owned cleanup。既有 `rejects` 先运行正fixture，再分别复制并变异；没有mock成功运行或SDK/DB。部分负例会在更早的完整命令/数据一致性门拒绝，并不声称每个标签都唯一命中末端断言。此次仅静态审阅，未把这些预计拒绝或正例表述为已通过。

## 后续门

Low仍须独立静态冻结核验及完整39方法实际离线执行。新E8/source24、partition重新验证gate、Generated28 stored验证gate、approval和完整inputs/open_context readiness必须绑定新pack。C#、policy、runner、产品178、source194/native196/startup1cadd及L0v2/W保持原身份；本变更不需要W重包或TP06重跑。

如授权一次历史TLS报告语义回归，只能写新目录并保留旧窗口身份、失败validation及 `runtime_accepted=false`；不得将它当作当前窗口成功。最终仍需root单独授权、全新同轮338例/25窗、原预算和全部精确清理/独立接受。当前源码接受不签发测试或运行授权。

本次唯一新增文件为本报告；未修改源码、旧证据、状态或协调文件，未导入/执行项目模块、运行测试、SDK、DB或Git变更。报告冻结后停写。
