# TP07 E tools v1：冻结组合静态审阅

2026-10-09。结论：**request_changes，两个确定接口阻塞，暂不接受 E-tools 离线/实库执行。** 本轮只读四个新 Python/JSON 输入及其冻结 C#/L0 依赖，未运行 SDK、测试、validator、runner或 DB，未修改实现。修复限定 Python 工具和相应合同测试；不为适配 validator 改冻结 provider、E 九源或公共 helper。

## 冻结身份

Low `.local/temporal-native/verification/TP07-E-tools-v1/source-manifest.json` 实算 SHA256 `710d8906f47070c5adc7315333c5c9d4c4b0ba70d0c39766e109a4c648f1d114`；toolpack manifest 实算 `d17f0a375e3bf4213951a4bad2363268d60a0a4cb2bd5183d2be6b19fb42dc1e`。24项 live 与 snapshot 均逐项匹配：8个工具输入、E九源及L0七模板，不含D/WIP，也不是完整外部构建闭包。

| 四个新输入 | SHA256 |
| --- | --- |
| `ef-run.py` | `f3f9b7ff24da4a8093bb8ce0f0d44e4553e94c07ac21835c956be0f283fcc71b` |
| `ef-validate.py` | `ec356599a7c12c1d70b00e36cade06d9daf4b31e58cb40500ae32a3a2dcd1d6c` |
| `ef-cases.json` | `60fc24dd1ab21ab208ec0160d50b403eeb6473cb0882b26a3924587f8ad13b7e` |
| `test_ef_contracts.py` | `52d9649ce62b42675a7d4e96a412be7223b5eea25f030d8ba9a25893862bb573` |

其余工具依赖为 `runtime-validate.py=d9ea3985…`、`runtime-cases.json=cbf6752c…`、`managed_identity.py=ca7bb8eb…`、`packed_oracle.py=bc9ed2f5…`。E九源与 [E-v3静态复核](TP07-E-astra-rereview-v3.md) 的 aggregate `28af14bc7faf6586128c58621e672e29f62fb440b3cb5a8a24fd87e850ee573f` 一致。13个计划合同方法未在本轮执行。

## ETOOLS-B01 / P1：依赖检查必然拒绝冻结 provider 的中央版本声明

位置：`ef-validate.py:165–181`，尤其 `:181`。

循环遍历 provider/E/L0 三个项目后，对每一个 W PackageReference 都要求内联 `Version == 0.1.0-temporal.20261008144915`。E/L0模板符合，但冻结 mapping-v5 的 `src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj:17` 是 `<PackageReference Include="W.DmProvider" />`，版本由同根 `Directory.Packages.props:7` 的精确 `[0.1.0-temporal.20261008144915]` 指定。合法外部source/locks/assets因此仍在 `exact_W_PackageReference` 处必定失败，尚未到SDK或DB。

修复：按已冻结的两种项目合同检查——两个新模板的内联精确版本，provider的中央精确版本及其已封存 props；继续核实际 assets 的 exact W package、EF10.0.12与无官方/源码驱动。不能把 provider 改成另一份源码来通过工具，也不能放宽为任意缺省/范围版本。

必要离线合同：使用冻结 provider csproj + Directory.Packages.props 的实际形状作为正例，内联模板作为另一个正例；中央版本缺失、错误/宽范围版本、W ProjectReference、assets中官方驱动/错误候选仍拒绝。应验证同一条外部输入检查路径，而不只测试自造内联项目。

## ETOOLS-B02 / P1：全局 step_id 唯一规则拒绝八个合法 reuse case

位置：`ef-validate.py:259–271`，尤其 `:262`；冻结生产者 `RoundtripCases.cs:17,31,37` 与 `Program.cs:220–231`。

每个 reuse case 在初始值、只改值、只改NULL后各调用一次 `RuntimeHarness.Independent`。该冻结 API 将三个 step 都命名为 `fresh_independent_D` 或三个 `fresh_independent_Z`，另通过递增的 `independent_D/Z_<ordinal>` 属性和有序 `oracle_read_attempts` 保存三次不同结果。validator 的 `steps()` 却在任何分支判定前禁止全部重复 step_id。两种 profile × DT/DTO × sync/async 的八个 reuse case 都会在 `duplicate_operation_step` 失败，哪怕实际执行完全正确；第一组 shared roundtrip 即无法通过。

修复：依据固定 shape 解释有序步骤，精确允许 reuse 的该列三次独立读取，其余业务步骤继续唯一；将每次独立读取的 completed step/result、递增 observation和 `oracle_read_attempts` 一一关联到初始值→changed→NULL，并核它们相对对应写入/查询的顺序。不能直接删除唯一性检查、忽略所有 `fresh_independent_*`，也无需改冻结 C# 或新增通用步骤框架。

必要离线合同：覆盖实际 C# 形状的完整 reuse 正例，以及少/多一次读取、交换 changed/NULL、重复旧 oracle、错列、跨case或未完成读取；`test_operation_steps_missing_duplicate_reordered_fake_failed` 的普通业务步骤重复负例继续拒绝。现有两步骤 synthetic 正例没有覆盖生产者三次 Independent 的协议。

## 其余已核对的限定结构

- registry 实读338项且ID唯一：shared180（60/54/12/54）、TLS158（52/56/12/38），按 `roundtrip → six → mutation → generated` 分组运行；各组内部与冻结 C# 的 sync/async、kind、shape及有限输入顺序对应。独立整数ticks/offset来自ef-cases，不使用 producer Expected 或 producer成功旗作为数值oracle。
- runner是run-only，外部审批artifact和toolpack均要求指定SHA；没有 restore/build、任意SQL/case/group运行模式或重试。外部 source/locks/build/startup、两个实际host、三个assets、实际W/EF DLL/MVID、二进制完整目录、before/after输入与TEST wrapper固定，凭据仅在wrapper后的内存环境传入。
- 新v5 L0先行门不只读 accepted旗：比较新mapping lineage，调用冻结L0独立validator重新核两profile完整报告并与保存的validation绑定。E和cleanup host来自同一新replay/provider，不复用旧L0 provider binary冒充本次构建。
- 每组正常或失败返回后按本组精确ledger启动独立L0 cleanup，再独立group validator；主/cleanup/validator非零、timeout、partial或缺case阻止后组。最终validator重验全部8组及源，不将producer `runtime_accepted`直接升级为E接受。输入漂移等外层异常也失败关闭，不能把未执行cleanup记为成功。
- `source()`绑定EF CommandId/attempt/stage、最近Prepare response、四项invocation identity、server ordinal到完整公开参数映射。固定6分支先检查全部4参数（含另一时间列typed NULL），再按server binding顺序确定首个legacy/TLS NotSupported或extended非micro Overflow；成功核实际published binding前后raw一致及Encoded7/Wire6。未知来源/flag/advisory仍拒绝，没有按producer“预期类型”猜分支。
- 拒绝要求精确有界异常链，失败binding保持null/未观察。async拒绝核业务6/13发送0及本进程真实成功EF SaveChangesAsync正控制；sync明确未观察发送。没有用response0代替send0或将controlled ADO称为SaveChanges。
- mutation按固定ticks重核original/current参数、stale的精确DbUpdateConcurrencyException、winner后fresh值/N/总行数/catalog；rollback核事务内值与fresh原值。generated路径绑定实际migration SQL hash/artifact与真实EF command，HasData需InsertDataOperation，generated readback与OriginalValues及fresh raw一致，inline要求零参数实际EF query，DATEADD更新后按固定+1second期待。clock仅报实际值/精度，不推导七位时钟能力。

上述是静态接线审查，不等于这些运行证据已经存在；尤其当前精确TLS `SELECT :v FROM DUAL` 的未知advisory/缺describe仍按 [R02设计](TP07-E-R02-observation-design.md) 和E-v3结论pending/failure，不能为通过338项删除两case或放宽tuple。

## 修复后门槛

两项修复由工具owner完成后另封8-toolpack/24-source，保留本轮manifest和审阅。复核差异与新增针对性合同，再由Low独占执行离线合同。实际构建闭包按175 EF源 + L0七模板 + E九模板=191、另两新项目锁=193，由独立外部准备建立，不能把当前24项工具快照称作编译输入已完备。

E SDK/DB窗口仍须新的v5 L0两profile接受及root协调；实际参数来源、未知TLS分支、raw/oracle、过程退出码及独立cleanup都要真正运行闭环。当前不接受E实库、D、完整EF-C、合并或发布。
