# TP07-P1 固定 EF 消费桥

> 历史样本准备工具：只生成固定 EF 提交与固定 W 包的隔离消费桥和未修正映射观察，不执行 restore、构建、测试或数据库。原锁仍引用官方驱动，因此不能宣称可直接 locked restore/compile；后续验证、时间映射接受和数据库授权均属于独立范围。

> 历史样本重现入口：固定候选、manifest、approval 字段和下游提交属于工具输入契约。文中的旧窗口与角色不约束当前模型或 harness，也不授予新的运行权限。当前开发遵循 [AGENTS.md](../../../AGENTS.md)，当前支持范围见实现文档索引。

本目录只准备可重放的 W 消费桥及**未修正映射观察**。不运行 restore、构建、测试或数据库，不代表 TP07/C 接受。原 EF dirty checkout 只用于 `git rev-parse <pin>^{tree}` 和 `git archive <pin>`，不读取其工作树源码、bin/obj 或 secrets。

- EF commit：`5863d3755dbd2d333ac359bf1c0a789762adbaed`；tree：`b8f99168225a3742675ae594e8735f481a591fd5`。
- archive SHA256：`668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8`。
- SDK 保留原 `10.0.401 / latestPatch`；原 EF 中央范围及五份锁保留，实际 EF 锁版本 `10.0.12`。
- 唯一驱动候选：`W.DamengProvider 0.1.0-temporal.20261008144915`，package SHA256 `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，DLL SHA256 `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`。

`bridge-allowlist.json` 逐文件锁住来源哈希、命名空间 anchor 数及桥接 action。`patches/driver-bridge.patch` 从实际 pin 重新生成，没有套用旧 T12/T19 patch。`bridge-manifest.json` 包含完整 before/after 源清单、变更类型、锁/SDK/policy/tool 哈希及未关闭合同；任何额外文件或 anchor 漂移均拒绝。新副本由工具生成同一 overlay 并与冻结 patch、完整 manifest 精确比较。

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/DownstreamAcceptance/TP07/prepare.py \
  --output .local/temporal-native/TP07/<new-revision>
```

输出必须不存在。产物为 `base.tar`、`base-source/`、`bridge-work/`、`candidate-feed/`、`restore.config`、patch、manifest 和准备收据。candidate feed 仅复制上述 W 包；NuGet source mapping 将 W 精确名称绑定本地源，其他依赖仍由 nuget.org 解析。配置在源目录外，验证者需显式传 `--configfile`。准备脚本只读检查 nuspec、DLL hash 和 ECMA-335 MVID，不加载驱动执行代码。

生产桥改动只有明确驱动引用、RelationalConnection/scaffold/provider-specific mapping 的 namespace 及安全 retry 适配。TimeSpan mapping 仅改 `using`；DT/DTO/TimeOnly/TimeSpan 的 facet、ConfigureParameter、materializer、literal/migrations 行为一律保留。CLI 的临时项目与版本查找只转 W 依赖名称。没有新增公开双驱动 API、Savepoint 或 DataSource 能力。

W retry 路径只接受驱动 `IsTransient=true` 且不是 `DmCommitOutcomeUnknownException`；不按错误号重放，非空 override 在 builder/strategy 处明确拒绝。原官方 retry 测试只换 namespace，原错误号断言仍保留，其不相容行为不能伪报通过；后续单独 W counterpart 由 root 开窗。同样保留官方 DTO characterization，不能作为 W 返回形态接受。

## 验证者后续窄窗口

实现者未改变任何锁，也未执行 SDK。原锁仍引用官方驱动，**当前不得宣称 locked restore/compile 可直接通过**。验证者在新的重建副本更新 driver graph 及新 observation 项目锁，逐项证明全部原非驱动包仍为原版本/内容哈希（特别是 EF `10.0.12`），保存 original/new 锁哈希与差异后再 locked restore；不得在 frozen bridge-work 或原 dirty checkout restore。构建使用任务 DOTNET_CLI_HOME、首启/遥测变量及单节点禁共享编译规则。

新 `test/TP07MappingObservation/` 用真实 `IRelationalTypeMappingSource`、生产 EF connection factory 与 `mapping.CreateParameter`，只读取参数字段，不调用 Open/Execute。它输出默认 DT/DTO/TimeOnly/TimeSpan、显式 precision6/store6、decimal 控制组的 value/NULL 参数 Precision/Scale；通过 actual assembly hash/MVID 和 deps 精确包身份证明 W route。输出固定 `temporal_mapping_accepted=false`，要观察未修正的不匹配，不能把进程退出0当作时间映射接受。验证者收据还须绑定本工具 manifest/patch、构建源码、实际 SDK 和 assets/locks；观察工具只报告 SDK source pin，不将 pin 冒充实际编译器运行证据。

后续时间 owner 接管隔离副本前先冻结此桥。完整 EF unit/materializer/functional/CLI/DB guard、TEST identity/nonce ledger/shared/TLS 配置与迁移 Down guard 均属于后续独立范围。禁止原 `all/admin` launcher；此阶段无数据库授权，不复制连接 secrets，不创建对象或变更授权。
