# TP07 D 第一轮冻结源码审阅

2026-10-09。结论：**request_changes，D 源码暂不接受；四项确定执行阻塞。** 本轮仅静态读源码、固定依赖及官方对应版本源码，未运行 SDK、构建、测试或数据库，未修改实现。修复由 D owner 完成，重新封源后再独立复核；本轮不激活 R05/R06 实库窗口。

## 冻结身份与范围

审阅 `tools/DownstreamAcceptance/TP07/runtime-templates/TP07TemporalDesign/` 的十个输入：七个顶层 C#/project 文件与三个 `Migrations/` 文件。按排序后的 `sha256 + "  " + repo-relativepath + LF` 实算 aggregate 为 `a7a44f262d4e963661c3bcd6dff49674306b1b567c7e8dda014a168907c3b6ea`。逐项与 Low `.local/temporal-native/verification/TP07-D-v1/source-manifest.json` 一致；manifest 实算 SHA256 为 `04ca589924400bf1d256ec72219a8e7696edb5a476ec3484f8ef37d0dc24adc9`。

依赖范围为已静态接受的 L0/common 七源，以及 M1-v4 固定 overlay；本轮核对 provider 实现所读目录是 `.local/temporal-native/verification/TP07-M1-v4/source/ef/mapping-source/`。不把 Python runner/validator WIP、E、生成代码、locks/assets、编译或运行结果纳入接受。判定依据包括 runtime-harness、guarded-down 设计、L0 首轮审阅和 M1 验收。

## 必修问题

### D-01 / P1：子进程切换到空 CLI home，使预先准备的 local tool 无法解析

位置：`Program.cs:190–192`；相关新目录约束 `Program.cs:97–100`，调用方 `ScaffoldCases.cs:55–61,140–144,162–166`。

`Child` 把 `DOTNET_CLI_HOME` 强制设为当前新 output 下的 `cli-home`。该 output 在本次入口必须不存在，因此独立执行者此前准备的 exact `dotnet-ef` resolver cache 不可能已经位于该子目录。各 checkpoint 又有自己的新 output。即使 replay 的 `.config/dotnet-tools.json` 正确且 NuGet 包已下载，第一条 `dotnet tool run dotnet-ef` 仍会因当前 home 没有 restored-command cache 而要求 tool restore，尚未进入受验 host。

SDK 10.0.401 的 [CLI home 解析](https://raw.githubusercontent.com/dotnet/sdk/v10.0.401/src/Common/CliFolderPathCalculatorCore.cs)、[cache 路径](https://raw.githubusercontent.com/dotnet/sdk/v10.0.401/src/Cli/Microsoft.DotNet.Configurer/CliFolderPathCalculator.cs)、[local-tool resolver](https://raw.githubusercontent.com/dotnet/sdk/v10.0.401/src/Cli/dotnet/CommandFactory/CommandResolution/LocalToolsCommandResolver.cs) 明确组成此链；cache 未命中直接失败，没有本调用中的自动恢复路径。

修复要求：三个阶段沿用独立执行者外部准备并留证的可写 task CLI home，或采用等价的显式外部工具准备合同；仍保持 exact 10.0.12、tool identity、`--no-build` 与禁止 C# 内 restore/build。独立验证应确认干净环境下外部 prepare 一次后，各阶段实际 CLI 均进入 host 并生成 receipt，而不是用全局工具或旧 home 偶然通过。

### D-02 / P1：DT 分支的条件表达式实际装箱为 DTO

位置：`GuardedMigrationCases.cs:139,197,218`。

三处 `object value/expected = kind=="dt" ? new DateTime(...) : new DateTimeOffset(...)` 的两分支先按已有隐式转换选出 `DateTimeOffset`；赋值目标是 object 并不能保留 DT 分支的类型。`GenerateDefault("dt")` 因而把 DTO 交给 TIMESTAMP(6) mapping。固定 `DamengDateTimeTypeMapping.cs:45–48` 的 literal 方法将其强制转换为 DateTime，会抛 `InvalidCastException`，shared 与 TLS 的第一个 DT fixture 都无法开始。其余两处即使单独到达，也会让无 offset 的 DT oracle 与非 null DTO offset 比较失败；极值隐式转换还会引入本地时区语义。

修复要求：在各分支明确保持装箱类型，例如先分别赋给 object，或在 DT 分支显式 `(object)`。不要改 mapping、输入或 oracle。离线复核应实际检查三条调用链的 DT/DTO runtime type；DT literal 与普通/极值 oracle 期望必须保持无 offset，DTO 普通向量仍为 +330、极值仍为 0。

### D-03 / P1：12 位 migration ID 无法被真正 migrator 解析

位置：`GuardedMigrationCases.cs:18,46,57,80,99,118`；两个 migration 的 `[Migration(...)]` 属性。

`202610090001_Baseline6` 和 `202610090002_Upgrade7` 的时间戳只有 12 位。EF 10.0.12 的 [MigrationsIdGenerator](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore.Relational/Migrations/Internal/MigrationsIdGenerator.cs) 以 14 位 `yyyyMMddHHmmss` 判定完整 ID；[MigrationsAssembly.FindMigrationId](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore.Relational/Migrations/Internal/MigrationsAssembly.cs) 对当前非法完整 ID 转而比较截取后的 migration name。因此 `MigrateAsync(BaselineId)` 不会因为字典存在同字符串键就成功，实际目标解析找不到 migration。`ScopeControls` 直接索引 `Migrations[UpgradeId]`，无法覆盖这一错误。

修复要求：统一换为有效 14 位 ID，并同步属性、常量、历史期望与相应文件名。离线复核需通过实际 `IMigrationsAssembly.FindMigrationId` 确认两个完整 ID 和名字的解析，不能仅检查字典键；真实 baseline/Up/Down 后再核专用 history。

### D-04 / P1：R06 状态快照调用反向工程 factory，必然拒绝本次 CHECK

位置：`GuardedMigrationCases.cs:291–296`；fixture CHECK 创建于 `Migrations/202610090001_Baseline6.cs:19,27`。

`Snapshot` 为了解析 V 精度，调用完整 `DamengDatabaseModelFactory.Create`，但 R06 表刻意有本次 CHECK 以验证迁移前后保持。固定 factory 的 `ValidateUnsupportedTableStructures`（`DamengDatabaseModelFactory.cs:819–832`）对选中表的用户 CHECK 明确抛 `NotSupportedException`。修复前面问题后，shared 在第一次 `before6` snapshot、TLS 在六位 snapshot 仍会停止；这是 fixture 与已知 provider 合同冲突，不是可忽略的环境问题。

修复要求：R06 从独立 catalog 读取并严格解释本次 DT/TZ 列 type/scale，再与 fresh raw/FF9/typed oracle 交叉核对；保持 CHECK/default/index/history 保护。R05 仍保留其 scaffold-compatible 表上的真实 DI/CLI factory 门。不得删除 CHECK、放宽 provider 反向工程策略、吞异常或把整个 R06 表伪装成可 scaffold。先离线验证快照路径不再经过拒绝 CHECK 的 factory，再以原 CHECK 存在的 baseline/Up/拒绝/成功完整链验收。

## 已核对的结构与剩余门

- R05 已拆为完整 metadata 与独立六位实际 CLI context。metadata 的 0/6/7/8/9、TZ、interval leading precision、TLTZ 进入实际 model/snapshot/migration SQL 检查，不调用其 SaveChanges；六位 context 实际 SaveChanges/query 微秒与 NULL，再 fresh oracle。没有手写 scaffold-like model 替代。
- 三次入口形成 scaffold → 外部 freeze/build → consume/两次 migrations add → 外部 build → finalize/script/snapshot/cleanup。C# 未内置 build/restore/等待批准。生成 subtree 限定 replay 的 `test/TP07Generated/<nonce>`，continuation 核父 report、build manifest、generated closure，并保留原 lineage/provider/源与锁键。
- `Name=ConnectionStrings:TP07`、`--no-onconfiguring`、内存配置与丢弃 CLI stdout/stderr 避免 raw secret 出现在 argv、生成配置和报告。公共 provider DI factory 使用方式与固定 `DamengDesignTimeTests` 相符。
- Down 保留一块完整 SQL guard，然后实际 AlterColumn；校完整 guard 文本、抑制事务、固定 schema/table 与独立 -20071 server response。真正调用为 `IMigrator.MigrateAsync`，拒绝后核实际命令顺序和 fresh state。SQL 模板自身是否在两种服务器上成立仍需已设计的控制门；本审阅不宣称运行通过。
- TLS guarded 分支只做 baseline6 保存，七位关闭依赖同候选 E/TP06 的独立 evidence join。shared 的 guard vectors、拒绝后状态、whole-micro Down 与 implicit-DDL-commit 边界保持独立。
- CREATE ACK/marker/ledger 与 common 的精确结构清理路径已接线；未知 CREATE 无 ACK/marker 不扩大 DROP。checkpoint 成功时保留 ownership；进程外 independent cleanup、续阶段前失败/外部 build 失败后的 cleanup 必须由后续 D runner 实现。当前代码明确标注 L0 runner 尚不支持 D checkpoint；因此此处仍是组合工具激活前提，不能凭本文件或未来源码静态接受直接运行完整组。

修复后应重新封存本次十源，保留本轮 manifest/report；新 freeze 按同一 source-binding 合同重审。后续仍须独立外部构建、实际 CLI 子进程身份、生成源两次冻结、固定 case 集独立校验、进程退出码与 fresh cleanup，才可讨论限定 D 运行接受。
