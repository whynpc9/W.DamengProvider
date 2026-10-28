# F01–F05 修复与验证记录

> 本文保留各阶段的实际命令、失败和合并结果。首批修复已随 0.3.1 发布；包内过程的 `integration_pending` 仍表示 TEST 权限下尚未完成实库验证，不是通过。

首批五项修复已实施、独立审查并分别提交。四个运行时的四套相关测试共 6,720/6,720 通过；五个目标框架编译通过。DeriveParameters 的 TEST 实库独立过程/函数验证 9/9 通过，包实库验证仍为 `integration_pending`。

## 2026-10-28 PR 与合并记录

用户授权创建 PR、跟进并解决 review 后合并。[PR #27](https://github.com/whynpc9/W.DamengProvider/pull/27) 已于 2026-10-28 11:31:49（Asia/Shanghai）合并到 main，合并提交 `9c1eb577c97b0fe1be234ec78578d7381ae0d80b`，两个父提交为上述基线和 PR head `dbab1c83769a402b20f24de29e84dbe9edb381ba`。本地 main 已 fast-forward 至同一合并提交，既有未跟踪文件保留；隔离分支工作树干净。

合并前重新读取并核对：

- PR head 与本地候选、CI run head、Codex code_review 的 commit_sha 完全相同。
- [CI run 40626240541](https://github.com/whynpc9/W.DamengProvider/actions/runs/40626240541) 成功；发布脚本契约、13 套测试 × 四运行时、包消费者与首次初始化矩阵、实际 .NET Core 3.1 restricted asset 步骤均为 success。这是新增的远端 CI 证据，不改写之前本地验证范围。
- [Codex review summary](https://github.com/whynpc9/W.DamengProvider/pull/27#issuecomment-6459808084) 对当前 head 为 completed，has_any_findings=false、has_unresolved_findings=false；本轮无需修复 review 评论。未请求独立的远端 security_review，不能将 code_review 表述为该项已执行。
- GraphQL 完整核对 reviews、comments、reviewThreads 的 pageInfo，均 hasNextPage=false；reviewThreads.totalCount=0，没有未解决线程。PR 状态 CLEAN/MERGEABLE、非草稿。
- 合并调用带 expected_head_sha，GitHub 返回 merged=true；随后再次读取 PR 确认 MERGED 和实际合并 SHA。

本轮没有修改候选代码、再次执行实库或提升 TEST 权限。包实库限制仍保留在 PR 描述。未创建版本 tag 或发布新 NuGet 包；以上 CI 结果针对 PR 候选，不宣称合并后另行触发的 main 工作流已完成。

## 2026-10-23 初次交付位置和范围

- 工作区：`/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider`
- 分支：`codex/improve-first-five`
- 基线：`aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`
- 最终 HEAD：`dbab1c83769a402b20f24de29e84dbe9edb381ba`
- 共 20 个文件；最终工作树干净、暂存区为空，基线到 HEAD 的 `git diff --check` 通过。
- main 保持基线 SHA；原有 `.agents/`、`.claude/`、`skills-lock.json` 未改动。主审只维护 main 下的 `plans/`，产品与测试由实现代理在隔离工作区修改。
- 初次交付时未推送、创建 PR、合并、发布或变更版本号；2026-10-28 后续 PR/合并状态见上节。其余审查发现未纳入本批。

| 项目 | 修复后的行为 | 提交 |
| --- | --- | --- |
| F01 | BLOB/CLOB truncate 全程保留 Int64；旧协议超 uint32 范围在发送前拒绝，完整 ACK 后才发布状态 | `85bb32cc9e2163c0ee5a93fb99bfdbc001fa9658` |
| F02 | SequentialAccess 的同步/异步 IsDBNull 不消费当前标量或 LOB 内容，继续保持后续列推进约束 | `bdd82490592ef1cec40459b938e39a21b50ff364` |
| F03 | 严格名称解析、目录值参数化、完整 reader 生命周期、确定的元数据投影与参数集合原子替换 | `dbab1c83769a402b20f24de29e84dbe9edb381ba` |
| F04 | BLOB Stream 遵守零起点 Position/Seek、EOF、参数检查、释放及超时属性契约；支持覆盖/追加/截断，明确拒绝增长与稀疏写入 | `7d8f071e5da11fb8c2e3e84a05cecaeebfb68dc4` |
| F05 | Batch reader 转发 provider-specific API，保留 38 位 DmDecimal 和 DBNull，不改变普通 decimal 溢出行为 | `3730caaab829e62f9757db290d42961a881f8893` |

## 离线与编译验证

独立验证代理串行执行共享构建及实库时段。SDK 为 10.0.401；每个测试结果均为退出码 0、失败 0、跳过 0。下表为实际运行测试次数，包含同一套测试在不同运行时重复执行。

| 测试套件 | net6.0 | net8.0 | net9.0 | net10.0 |
| --- | ---: | ---: | ---: | ---: |
| LobTests | 322 | 322 | 322 | 322 |
| TypeTests | 976 | 976 | 976 | 976 |
| CommandTests | 212 | 212 | 212 | 212 |
| AsyncTests | 170 | 170 | 170 | 170 |
| 合计 | 1,680 | 1,680 | 1,680 | 1,680 |

Product Release 编译覆盖 `netstandard2.1/net6.0/net8.0/net9.0/net10.0`，退出码 0。最后一次产品构建有 60 个警告、0 错误；不是零警告构建。最终 probe 编译退出码 0，0 警告、0 错误。未实际运行 .NET Core 3.1；netstandard2.1 编译不能代替该运行时验证。

F03 后期修正只涉及目录投影、相关测试及 probe。Command 四运行时与产品五目标重新验证；复用未受这些修正影响的 Lob/Type/Async 结果。最后一次改动仅为 probe 的包函数断言按 ReturnValue 方向识别返回值，而不要求它位于数组首位；独立复审并重新编译 probe，未重复运行不受影响的 standalone 实库和整个矩阵。

证据目录：[improve-validation](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation)。关键结果：

- [Lob/Type/Async 的 net6/8/9 汇总](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/cross-runtime-summary.json)
- [Command 的 net6/8/9 汇总](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/Command-cross-runtime-summary.json)
- net10 TRX：[Lob](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/results/Lob-net10-corrected.trx)、[Type](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/results/Type-net10-initial.trx)、[Command](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/results/Command-net10-F03-bound.trx)、[Async](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/results/Async-net10-initial.trx)
- [最终五目标编译](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/product-five-targets-F03-final.log)及同名 `.exit`、`.command.json`
- [最终 probe 编译](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/build-probe-final.log)及同名 `.exit`、`.command.json`

## TEST 实库结果与限制

[最终 standalone 实库记录](/Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/.local/improve-validation/derive-real-standalone.log)：DM 8.1.5.60，.NET 10.0.12，加载隔离工作区 probe 输出中的候选 W.DamengProvider.dll。每次打开连接均验证 USER 和当前 schema 为 `WDM_PROVIDER_TEST`，通过 typed builder 显式配置 `PlaintextAllowed`。

9/9 用例通过：IN/OUT/INOUT 过程与实际输出、schema 限定名称、函数返回与实际执行、零参过程、包含点/单引号/转义双引号的合法名称、缺失名称保留原参数、非法名称保留原参数、三段名称拒绝、元数据命令的事务绑定。每个用例之后同连接 SELECT 成功。

实际目录证据显示，DM 过程的第一个普通参数和函数返回行均可为 POSITION=0。产品已使用已确认的对象类型区分，避免把普通过程参数当成 ReturnValue；对应实际行形状已加入回归测试。

只创建和清理本次记录的四个精确随机名称对象。fresh TEST 连接确认四个对象全部不存在，记录中 `cleanup_verified=true`，退出码 0。未读取 SA/DEV 凭据、修改权限或实例配置；未向制品复制 secrets。

包创建在前置诊断实库中遭服务器 `-5528` 拒绝，原因是 TEST 缺少创建包/类权限。最终 standalone 模式明确记录包创建及六个依赖用例共七项 `not_run/integration_pending`；未将它们计入通过的九项。包查询保留原 SYS 目录路径，改为绑定唯一 PKGID/MTDID、按 PARAMID 排序并使用 IOFLAG 判断方向；参数投影、查询绑定、歧义拒绝已有离线覆盖。真实包目录数据、包函数和重载仍未验证，不据此宣称全部包兼容性验收完成。

## 执行命令

工作目录为上述隔离工作区。执行环境：

```sh
export DOTNET_CLI_HOME="$PWD/.local/improve-validation/cli"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_ROLL_FORWARD=Minor
export NUGET_PACKAGES=/Users/wanghongyi/.nuget/packages
```

测试对上表每一 suite/TFM 组合串行执行以下形式，并写独立 TRX；准确 argv 和退出码保存在证据目录。没有清空 NuGet 缓存。

```sh
dotnet test tests/W.DmProvider.<Suite>Tests/W.DmProvider.<Suite>Tests.csproj \
  -f <tfm> -c Release -m:1 /nodeReuse:false /p:UseSharedCompilation=false \
  --disable-build-servers --logger 'trx;LogFileName=<case>.trx' \
  --results-directory .local/improve-validation/results

dotnet build src/W.DamengProvider/W.DamengProvider.csproj -c Release \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers

dotnet build tools/DeriveParametersProbe/DeriveParametersProbe.csproj \
  -c Release -f net10.0 -m:1 /nodeReuse:false /p:UseSharedCompilation=false \
  --disable-build-servers

bash /Users/wanghongyi/Projects/W.DmProvider/scripts/with-dameng-test.sh \
  /usr/local/share/dotnet/dotnet \
  /Users/wanghongyi/.codex/worktrees/improve-first-five/W.DmProvider/tools/DeriveParametersProbe/bin/Release/net10.0/DeriveParametersProbe.dll \
  --standalone-only
```

## 原失败与独立审查

原始失败记录保留在证据目录，没有用最后一次绿色结果覆盖：

- 产品：首轮实库发现 procedure POSITION=0 假设错误；按实际对象类型修正，补充回归并重跑 Command 四运行时、五目标编译及 standalone 实库。
- 测试：畸形 ACK 的预期异常类型与既有 decoder 契约不符、Batch 合成连接缺少 Server、参数绑定测试误以为现有 SQL 会重写成问号。分别修正夹具/预期，保留状态不变、零发送和实际绑定值断言后重跑。
- Probe：修正 DbTransaction/DbParameter 静态类型导致的编译错误；最终独立复审发现并修复包函数返回参数位置假设。最终编译通过。
- 环境：首轮 NuGet TLS EOF 重试后恢复，后续显式复用本机缓存；包权限限制按未验证记录，未改变权限。

F01/F02/F04/F05 经独立静态审查；F03 另有实施前独立边界调查、实施后独立安全审查及最终修正复审。没有遗留已确认的注入绕过、reader 生命周期或参数所有权问题；该结论不替代未执行的真实包集成测试。全部构建、测试和实库执行由独立验证代理完成。

2026-10-23 本地阶段只证明相关功能和限定实库路径；当时没有执行整个仓库全部测试、官方驱动对照、NuGet 消费者验证、远端 CI、PR 审查闭环或发布验收。后续远端 CI、包消费者及 PR 闭环证据见 2026-10-28 一节；官方驱动对照与新版本发布未纳入本轮。
