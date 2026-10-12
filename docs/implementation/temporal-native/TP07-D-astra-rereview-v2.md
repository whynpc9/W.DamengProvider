# TP07 D v2：四项阻塞修复复核

2026-10-09。结论：**四项源码阻塞已关闭，接受本轮十源的限定静态范围。** 未发现本次差异新增的确定编译/API/生命周期阻塞。该结论不是构建、工具组合、生成源码或 R05/R06 运行验收。本轮未运行 SDK、测试、数据库或受验执行脚本，未修改实现。

## 来源与差异范围

`tools/DownstreamAcceptance/TP07/runtime-templates/TP07TemporalDesign/` 十个文件按排序后的 `sha256 + "  " + repo-relativepath + LF` 实算 aggregate：`d36f518c01e6359087184c5c34e329e8518770e0d83ca15647e4da0d26fd08e9`，与 author 停写身份一致。

基线为 [D v1 审阅](TP07-D-astra-review-v1.md) 的 Low snapshot。已重算旧 manifest SHA256 仍为 `04ca589924400bf1d256ec72219a8e7696edb5a476ec3484f8ef37d0dc24adc9`，旧 snapshot 十项仍逐一匹配，未回写历史源。v2 Low 独立 snapshot 在本报告形成时尚未就绪；后续必须与上述 aggregate 逐项绑定后才能使用本静态接受。

实际差异仅为 `Program.cs` 的外部准备工具身份/启动接线、`ScaffoldCases.cs` 的工具 preflight 与 continuation 绑定、`GuardedMigrationCases.cs` 的三项修复，以及两个 migration 文件的有效 ID/文件名。两个 migration 的逻辑未改，仅 `[Migration]` ID 与文件名改变；其余五个输入字节未改。

## 四项闭环

| v1 问题 | v2 修复与判断 |
| --- | --- |
| D-01：新空 CLI home 丢失 resolver cache | `Program.cs:188–244,246–276` 从外部 `TP07_CLI_TOOL_MANIFEST` 和 SHA 读取预备 task home、绝对 dotnet host、tool-root、nupkg、六个执行文件；实际子进程为绝对 host 的 `exec <tool-root>/dotnet-ef.dll`，继承准备好的可写 task home。没有 local-tool resolver、全局工具 fallback 或 C# restore/build。`ScaffoldCases.cs:24,225–226` 在 fixture 前/续阶段检查工具清单，并固定父报告的同一 SHA。原故障路径消除。 |
| D-02：DT 条件式被转换为 DTO | `GuardedMigrationCases.cs:141,206,227` 的 DT 分支均明确 `(object)new DateTime(...)`。DT 保留原 boxed DateTime，DTO 普通值 +330、极值 0 不变；固定 literal mapping 和 oracle 未放宽。 |
| D-03：12 位 migration ID 无法解析 | 常量、两个属性与文件名统一为 `20261009000100_Baseline6` / `20261009000200_Upgrade7`。`VerifyMigrationIds`（`:144–150`）实际调用 `IMigrationsAssembly.FindMigrationId` 检查完整 ID 与名字，在 scope control 及 shared/TLS fixture 入口使用；不再仅凭字典键声称迁移可寻址。 |
| D-04：R06 CHECK 被 scaffold factory 拒绝 | `GuardedMigrationCases.cs:293–326` 的快照改为限定本次表的 USER catalog，严格限定 DT/TZ type、scale6/7、nullable、table/history/entry 对应，确认本次 PK/CHECK/index，再逐行 fresh oracle 核 metadata scale（含 NULL）。完整 default/columns/index/constraints/history 与数据比较仍保留。R06 不再调用完整反向工程 factory；R05 的真实 DI/CLI factory 未更换。 |

R06 的 precision 读取只涉及 DT/TZ，不把 TLTZ 的 4096 catalog 编码用于普通 TZ。Up/Down 先完成 `ConfirmExternalAlter`，再调用 Snapshot，因此新的 `actualScale == entry.Scale` 与已登记的迁移状态一致。CHECK/default/index 未为通过测试而删除或重写。

## 固定 CLI 接口的只读核对

本机安装目录 `/Users/wanghongyi/.dotnet/tools/.store/dotnet-ef/10.0.12/dotnet-ef/10.0.12/` 的 nupkg SHA256 为 `5a671dbb0a6a31a0e01ee6157a7d5a11c7de1323d2fcc43c849f4f99064ca247`。只读打开该包和安装文件，确认以下六项位于代码使用的 `tools/net8.0/any/` 外层根，且每项与 nupkg 对应 entry 字节一致：

| 相对 tool-root 路径 | SHA256 |
| --- | --- |
| `DotnetToolSettings.xml` | `03dfac05864b29a3eded2cb8f91146c707abffc76bf66af8e9e8421eb0d428d5` |
| `dotnet-ef.dll` | `663788dc2d64a081c75e24cd493b33ff418dced7c8d2518d9f41193d9a63b8e8` |
| `dotnet-ef.deps.json` | `a83c8a7e532b8df51886d8bfa85c31743c7167c985e8a6bc99225f7e8eebcae0` |
| `dotnet-ef.runtimeconfig.json` | `2f12d184f41a138229b30afb4ee068f453b442ad19a6626de01bdc7df8e45d7a` |
| `tools/net8.0/any/ef.dll` | `f3cdc9c5620e23d9bd12d47cf843669cb317bdc09c93fdc1d297d2c5aa72693d` |
| `tools/net8.0/any/ef.runtimeconfig.json` | `2f12d184f41a138229b30afb4ee068f453b442ad19a6626de01bdc7df8e45d7a` |

ToolSettings 的 Command 正是 `dotnet-ef` / `dotnet-ef.dll` / `dotnet`，无 XML namespace 导致 `Descendants("Command")` 失配的问题。net8 工具目录与本轮 net10 受验项目不冲突；包的 runtimeconfig 自带 Major roll-forward，实际受验 host 仍须满足原 candidate/runtime guard。本次只证明布局与字节，不把本机全局安装本身当作已批准的运行工具：独立准备必须将受审字节放入清单限定 task/tool-root，并固定清单 SHA。

[EF 10.0.12 RootCommand](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/dotnet-ef/RootCommand.cs) 在 `--no-build` 下跳过项目 build，并从外层工具目录选择 `tools/net8.0/any/ef.dll`；其项目 metadata 读取由 [Project.FromFile](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/dotnet-ef/Project.cs) 发起 MSBuild 属性/ResolvePackageAssets 查询。代码传递的 CLI 参数因此与真实工具接口吻合。

外部执行者必须继续固定子进程环境：官方工具自身通过 [Exe.Run](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/dotnet-ef/Exe.cs) 使用名字 `dotnet` 启动 metadata 与内部 ef host。v2 的绝对 host 校验固定的是 C# 发起的首层进程，不能据此宣称后代进程都以绝对路径启动。组合 runner 应让继承 PATH 解析同一批准的 host，并记录实际 SDK/进程身份；这是原 pinned-toolchain 接线的组成部分，不要求另建工具框架。

## 接受边界与后续验证

三 checkpoint 的外部 freeze/build、generated subtree、父 manifest/report SHA、同源 generated closure、真实六位 SaveChanges/query、metadata-only 模型、guard-first Down 与 fixed -20071 边界均保持。没有借此次修复改 common、mapping、W 产品、TLS 七位能力或扩大清理范围。

后续仍需将 D-v2 Low snapshot、外部 CLI-tool manifest、生成源码两次独立构建和 D runner/validator 组合审核。runner 必须独立建立清单信任根，核对新环境变量透传、实际 CLI 身份/退出码、完整 case 集，以及 checkpoint/外部 build 失败后的独立 cleanup。当前 L0 runner 的 D 扩展仍明确 pending；没有把工具文件存在、本次静态闭环或 E 的结果替代 D 实际运行接受。
