# TP07 D tools v1：组合静态审阅

2026-10-09。结论：**request_changes：四项有限问题，当前不接受离线/实库激活。** 本轮仅静态读取冻结D-v3十源、common/L0接口、四个新工具及既定设计，未运行SDK、测试、producer、runner、validator或DB，未修改实现。修复由工具owner完成，保留本轮冻结与报告；不自动扩大到C#、EF或W产品。

## 冻结身份

Low `.local/temporal-native/verification/TP07-D-tools-v1/source-pack-manifest.json` 实算SHA256 `bf766c7b753294245926e66f683e35cea0d291a592e60d2af2c54c2d08a86dd1`；25项source manifest实算 `1ce505df1bd59248b95fb3819e7a95fa4f401e5fe736efd5fa320dc44d3058b1`。25项live与snapshot均逐项匹配，21项policy immutable全部一致。四个新文件按repo-relative排序的aggregate为 `d6ff2d4762886ab74ba7e654afbb90e0f15ea412e29f31c17ac4e2576988d53c`。

| 新输入 | SHA256 |
| --- | --- |
| `design-run.py` | `cf481c49e26dd8d16135b8d332d80f421cd61e57638d3b0d7302a690a2c93ea7` |
| `design-validate.py` | `310eeff4684f97ecca5009c2a0f74f7401ccd1dfaa078db8345f815b2b2fd4fd` |
| `design-cases.json` | `8530f33ae25a25bb4de8402fc31d45763a39e1fba90e3e371afe6dd073e6ff5c` |
| `test_design_contracts.py` | `508c3c1c091881a988e84e4aa323247c2b645b1a5a984b67178ae4e4ef5b6529` |

继承 [D-v3真实读取收据审阅](TP07-D-astra-rereview-v3.md) 的十源aggregate `631b607a3737779f594f7b7d1a3c289a50f70906d754b65de38e839621b56a5e`。policy registry共21个ID，19个计划合同方法仅静态读取，未执行。

## DTOOLS-B01 / P1：checkpoint测试再次混用macOS临时路径别名

位置：`test_design_contracts.py:30–54` 各 `root=Path(temp)`；`design-validate.py:243,248` 与 `generated_hashes:239`。

fixture先用未规范化临时根生成路径及manifest；`continuation` 的 `inside` 返回resolve后的folder，却仍把原root传给 `generated_hashes`。后者对每个规范化path执行 `relative_to(root)`。本机标准库临时父目录实读为 `/var/folders/5d/t544b0wd4yxdytzqnb3nl76c0000gn/T`，resolve后为 `/private/var/folders/5d/t544b0wd4yxdytzqnb3nl76c0000gn/T`，因此正常checkpoint正例会抛ValueError；若干原本期待ContractError的负例也可能在目标断言前报错。

最小修复：各临时fixture从一个规范化root构造全部路径和manifest，保持validator精确路径约束。保留正例及splice/path escape负例，不以捕获任意ValueError或换TMPDIR掩盖未到达实际断言的问题。修复后由Low正常执行全部合同；本轮没有运行测试制造新失败记录。

## DTOOLS-B02 / P1：真正migrator guard的SQL尾换行与独立hash期待不符

位置：`design-validate.py:373–376`；C# `MigrationGuardSql.ForOwnedTable`、`GuardedMigrationCases.VerifiedDownCommands` 与 `DesignAudit.Before`。

Python `guard_sql` 返回以 `END;` 结尾且无尾换行的模板，`guard_order`直接要求真实命令hash等于该模板hash。G02独立控制确实把原模板直接交给Program.Execute；G03/G04则经过真正EF SqlOperation生成。固定Dameng generator的 `Generate(SqlOperation):216–221`调用base；[EF10.0.12 MigrationsSqlGenerator](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore.Relational/Migrations/MigrationsSqlGenerator.cs)使用AppendLine生成该operation，[MigrationCommandListBuilder](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore.Relational/Migrations/MigrationCommandListBuilder.cs)将尾换行保留到command。因此macOS实际Down guard是模板加LF，audit保存此真实字符串的hash，正确G03/G04也会被 `actual_exact_guard_first` 拒绝。

最小修复：独立区分raw控制模板与固定EF生成命令的精确字节期待，并与C# `down_command_order`/实际audit关联；不要把所有SQL空白或字符任意正规化。增加带实际EF末尾LF的正例以及缺guard、内容变化、额外语句、错误顺序负例；G02仍核原raw模板字节。现有synthetic guard命令直接使用Python模板hash，未覆盖真实generator边界。

## DTOOLS-B03 / P1：guard结果/命令/响应与顺序未充分关联，错误收据可以被接受

位置：`design-validate.py:365–381,409–434`。

存在以下直接可由现有条件推导的错误接受路径，不是要求新增协议观察框架：

- G02前三个正控制只核producer `outcome="returned"`和窗口中存在attempted guard，不要求该命令实际completed；删除completed收据、保留attempted与outcome仍可通过。nonmicro的verified response只要求在全report、同stage，不限定到`guard_call.frame_start/frame_end`及该guard的command_id；失败响应可借自同stage另一调用。
- G03的 `guard_error` 与 `guard_order` 分别通过但未核二者command_id相同；G04仅要求guard后存在alter和history，`guard → history_mutation → alter`也被接受，违背history在成功ALTER后更新的合同。
- G01仅依据每项`actual_guard_rejected`布尔和数学预期，未独立关联五次guard实际completed/failed与三次−20071响应。C#已实际调用并检查，但其布尔不能取代consumer的命令/响应复核。
- G02的row oracle循环核值，却未使用已存在RowOracle的table/column/read_evidence绑定；改成别表/别次读取收据而保留相同值不会在此分支失败。普通ProtectedState分支已有这类约束，不应在控制门缺失。

最小修复：使用已冻结D-v3现有command ID、sequence、call command/frame边界、status/API及安全error字段，精确核每次guard的attempt→对应completed或failed、同command的verified响应及前后owned RowOracle；G03连接error到那一条guard，G04明确guard→ALTER→history顺序。G01按固定五向量顺序核实际调用结果，不能从producer布尔推导。增加删除completion、错command/frame窗口、借响应、history先于ALTER、跨owned表oracle等有限mutation合同。无需新产品hook或修改SQL语义。

## DTOOLS-B04 / P1：独立cleanup仍被workflow CLI包/配置损坏阻断

位置：`design-run.py:67–75,89–96,160–168`；`design-validate.py:205–217`。

虽然cleanup有独立replay/startup/build闭包，`wrapped(...,role="cleanup")`及cleanup internal-host仍调用完整 `tool_bundle`，检查workflow中的dotnet-ef包、六个tool文件和 `.config/dotnet-tools.json`。显式cleanup plan也执行同样检查。若前一checkpoint已留下owned表，而后续外部准备/CLI阶段因其中一个非cleanup所需文件缺失或hash漂移失败，完好的独立L0 cleanup宿主仍无法启动，只能留下 `cleanup_pending`。

最小修复：cleanup仅从已批准且hash验证的清单取得批准dotnet host/必要task home，验证自身独立startup/build/source/candidate、原TEST wrapper与精确ledger；不依赖执行dotnet-ef所需的workflow包和local-tool配置存在。main的完整CLI校验继续严格保留。可保持18字段plan，从现有可信manifest提取host pin，无需引入工具发现/fallback。增加只损坏workflow tool/config而protected cleanup闭包完好的失败场景合同，证明cleanup仍被尝试，主阶段失败不会被清理成功抹掉。

该问题限定到与L0 cleanup无关的workflow CLI运行文件，不要求在cleanup自身source/host/candidate不可信时强行执行，也不放宽unknown CREATE无ACK/marker的DROP限制。

## 已核对的其余设计接线

- 外部plan为精确18字段，阶段限定scaffold/consume/finalize/guarded/cleanup；source-pack为25项，不以动态latest发现输入。model gates要求同mapping-v5/候选、指定profile的L0及E外部接受引用；TLS额外连接同候选E/TP06七位关闭证据。当前E仍待完成，不能用synthetic adapter提前激活D。
- C#三checkpoint与两个外部build之间，生成目录限定 `test/TP07Generated/<nonce>`，精确6/12源码、父build/report SHA、原lineage/host/source/locks/assets保留；runner复制独立generated/ledger snapshot，pending状态保留对象且不声明runtime接受。没有隐藏restore/build/polling批准。
- CLI bundle固定10.0.12 nupkg及六文件；主入口绝对dotnet exec，PATH解析核同approved host供官方后代进程使用。Name连接与no-onconfiguring沿冻结C#执行，stdout/stderr丢弃，凭据仅wrapper后的内存环境。
- R05保留真实DI catalog和两actualCLI context：全metadata只检查模型/snapshot/SQL，六位模型实际SaveChanges/query微秒与NULL。R06仍是本次单V DT/DTO fixture，先Up保存证明，再G00/G01/G02，最后guard-first Down；TLS只六位保存，不扩七位写。
- ledger精确nonce/schema shape，普通cleanup沿冻结OwnedObjects校marker/ACK/结构并fresh absence；未知CREATE无ACK/marker不可DROP。上述B04修的是启动独立cleanup的依赖，不是扩大其对象权限。

## 修复与复验界线

四项修复后另封新四文件与25-source pack，复核差异并由Low执行更新后的有限合同。应有真实生产者形状的checkpoint、raw guard与EF guard正例，以及guard完成/错误关联、失败后独立cleanup的针对性负例；不再只靠各小函数的自造单字段正例。

本报告不接受D build、R05生成模型、R06实际guard错误码/迁移或完整C。仍须E/L0实际前置门、root排定窗口、每checkpoint独立外部build与source冻结、真实CLI/DB/退出码/清理证据。旧D静态接受不被改写为这些尚未运行的结果。
