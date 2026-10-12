# TP06 supplemental tool v4 独立静态验收

2026-10-09。结论：**static_accepted_for_independent_offline_verification**。v4按已批准的 [runtime triage](TP06-supplement-v3-runtime-triage.md) 修正工具恢复合同，当前无剩余确认的静态阻塞。可交Low完整离线验证；通过后仍由root安排新的完整DB窗口。此次只读代码/快照，没有运行SDK、Python测试、consumer或DB，没有修改产品/工具。

## 冻结与来源pin

快照 `.local/temporal-native/verification/TP06-supplement-v4/source-manifest.json` 实读SHA256 `b013baaa29921d7e15cf80d55b86bbbd286e2ac926d22d2cbea9cc741b23b7bc`。17份工具SHA均已实读匹配交接；root/Low提供17文件aggregate `158feb9ca2d6a913d3b487b81442e50ded9b2bd676f04e6e371ea8b9d7579e88`，另两个v11依赖保持。

关键身份为Cases.cs `0360d2c3887daf50b6cc08437701ce3fe30aa605c65cde88ae4576497c0fa507`；contracts.py `35eccb43bcb32adb8ff820ee594b584592f88b21cb13bddeb21de94c3695a722`；validate.py `b1b895d24eceeb735aa7da79075aa48b86432fdd4388ed57889a3263b3674076`（v3未变）；test_validator_contracts.py `d9137ee05e20a91e5550cfe2a2068a7fdd8f1bafb7e9b7413cec3c9eae63165b`；case-manifest `7402ae46829936122db8657c0813fdb70fd6c0d7ef84b8faed6c2d0eaefcdf89`。新recovery设计pin为 `9a9a25433b0631ac7a825df9358d81185da9336b2c57057484665f1f670ce980`。

已独立逐行比较v3→v4 Cases：成功SQL文本、输入值/声明、成功G2同步API与setter顺序不变；变化为目标窗口采集、按stage.api执行获批负例、恢复调用及退休后下一独立stage连接管理。因此**批准G2 SQL来源pin ce37…→0360…的迁移**，仅表示此工具来源变化受审，不扩大原G2 read tuple集合或宣称新实测能力。manifest的case/vector集合仍42 shared/36 TLS；只改获批negative API及设计/源码pin。

## 调用、收据与独立校验

- **目标拒绝窗口**：BeginTarget在实际执行调用前记录公共State、API、真实参数数量、connection ID及发送基点；EndTarget在finally记录返回状态与现有AfterFrameSent的实际数字帧。同步成功路径明确标unobservable_sync_path，不冒称发送零。validator仅对期望本地写拒绝要求异步API、Open前态、精确异常、execute未返回、参数数量1，以及由实际帧重算的6/13业务发送0。3/5的先前allocation/prepare不被清零为NotSent；原Outcome缺失仍保持null。
- **Open分支**：实际旧连接SELECT1必须成功且保持Open；原ID、实际API和scalar1受校验。不能在Open分支失败后自动改走退休宽免。
- **Broken/Closed分支**：旧连接ExecuteScalarAsync必须受控InvalidOperation拒绝、前后状态相同、全部发送增量0；不得记为same_connection成功。随后调用实际command.DisposeAsync和connection.DisposeAsync，最终旧连接Closed，再新OpenAsync取得不同ID/TEST身份，真实SELECT1与fresh原row/值/schema观察齐全。validator核完整步骤顺序、完成状态、ID及各数值，不只信recovery.completed。
- **失败留痕**：Recover先发布attempt与steps容器，各Step先写attempted，再写completed或failed，catch保存Safe error及failed_step。fresh验证失败仍进入fresh Dispose，原失败step记录保留；总completed不会因最终Dispose成功变true。
- **无业务重放**：恢复路径仅SELECT与Dispose/Open；FreshOnConnection核原owned row、原值及实际列metadata，FreshOfficial保留允许的O oracle。G2失败的fresh控制仍是原独立SELECT控制，不重执行被拒绝的DML。拒绝后fresh schema字段与实际W observation metadata交叉比较，原值/行数仍严格。

fresh live adapter只调用已有公开API，OpenAsync照旧验证物理profile、TEST user/schema；没有修改Session、反射写产品状态、复活旧连接或新增产品hook。追加State记录仅为公共枚举。AfterFrame与AfterFrameSent继续分别表示响应和完成发送。

## 后继stage与API范围

ASYNC_REJECT_THEN_QUERY的fraction仍使用control的同一连接/row。fraction若退休且恢复已完成，RequiresNewIndependentConnection确认旧对象Closed后，才显式新建连接承担既有scale7_null stage，记录准确origin和previous ID；若仍Open则继续原连接。validator要求对应ID分支，不能暗中重开原对象或随意替换Open连接。旧rc已在恢复流程Dispose，外层finally仅关闭当前实际rc，不增加业务写入。

manifest与真实调用一致：NATIVE_DATE_REJECT_TIME及INTERVAL min/max/tick的reject改ExecuteNonQueryAsync；TLS G2 dt/dto.tick及tls_default_explicit_reject改ExecuteReaderAsync。原本已异步的负例不变，成功G2仍ExecuteReader；没有增加Prepare。主矩阵的同步拒绝覆盖归属保持。

## 原门与安全边界

恢复例外由冻结expected.exception限定到本地写拒绝；leading的verified server分支要求scope=verified_server_response、same_open及旧连接成功控制，未批准错误号继续pending。reader mismatch仍核合法前向读取和原连接查询，不能用写拒绝fresh恢复替代。fixed leading的本次未发布binding例外、真实raw/99 control_only以及实际发送/响应身份未放宽。

R1的Fetch7、R2真实server-null projection、R3准确3/5/6/13分类和R4有限shared read alternatives保持。parameter/EncodedScale、clone/reset/reopen、实际physical/root设计pin、raw/schema/cell/独立整数decode/FF9/GetString严格门，以及完整报告通过后才发布observed结果的规则未变。source/archive/nupkg/DLL/MVID、TEST wrapper、ownership marker、原始错误安全化与独立cleanup边界保持。

新增assertion_code只对工具自己的ToolAssertionException且Enum.IsDefined的固定ToolAssertionCode输出；外部异常不会从Message提取代码，旧Safe字段继续受限。未发现新的静态API/序列化形状错配。G2 fresh reader在EOF后读取列metadata时，产品ReadOwnedAsync不清除m_ColInfo，读取发生于Dispose前，合法所有权顺序成立。

## 验证交接

原112项validator方法保留并新增28项状态/发送/ID/原值/中断mutants；C#沿实际Recover/RequiresNewIndependentConnection helper新增12项至36项，含Open/Broken/Closed控制、错误状态/发送/异常/fresh ID、scalar、中途失败、server不可退休及后继stage来源。runner与独立validator精确核36个ID集合。140项Python和36项C#均尚未由本review运行；静态接受不等于这些检查通过。

Low应从本次新冻结执行完整离线门，任何失败保留并交回High新冻结；通过后按root授权的新shared/TLS完整运行验证实际State、发送、恢复和原值。v3原失败、旧离线与主矩阵证据继续保留，不回写通过。产品包保持原同候选；只有完整Supplement及其cleanup也接受后，才可由root关闭TP06。
