# TP07 Dpreparation-v6 离线链验收

2026-10-09。结论：**scoped_offline_chain_accepted / D_runtime_pending_E_model_gate**。本次重要任务验收只读复核 Low 已执行的新 P0/N0/B0/BC 和只读 CLI 证据；未运行 SDK、测试、CLI、wrapper、D host、数据库或 cleanup。未发现阻塞本轮离线链接受的缺陷。完整 D、完整 EF-C、合并及发布均未因此接受。

验收依据为仓库 AGENTS.md、status.json 的 TP-07.D 与 E 当前记录，以及 [准备 v6 静态复核](TP07-D-preparation-astra-rereview-v6.md)、[native 图归因](TP07-D-native-graph-triage.md)、[D-v5 编译修复复核](TP07-D-astra-rereview-v5.md)。status.json 原有准备授权字段不是本轮执行结果；本报告不修改 status、ownership、源码或旧证据。

## 冻结身份与实际证据

下表路径以 `.local/temporal-native/` 为根。所有所列哈希均由本次只读实算核对。

| 证据 | SHA256 |
| --- | --- |
| `root-approvals/TP07-D-preparation-v6-inputs/inputs.json` | `18f66c72050417457aca5157ce9df85b8baf7f722f6ed5ee38087665c0b72b70` |
| `verification/TP07-D-preparation-v6-freeze/source-manifest.json` | `8ba11d74eff1c9c6557c3b8471d6afd85bee1dc53a1bdbbacf7afb0aba8dcf9f` |
| `verification/TP07-D-tools-v4/source-pack-manifest.json` | `124cbc1897846a4bc23d433ff71ca2baa3942780cb26363f2f7ba890a38336c0` |
| `verification/TP07-D-preparation-v6-offline/verification-summary.json` | `02d5fd1b8002492e6bf45ab1ac1e3148c67e559e5e0661015b472e2642711441` |
| `verification/TP07-D-prepared-v3-source/workflow-source.json` | `66c71f2c3085744fc4921d6768d14b2ef5486c33e4e63f57b06086243ed2be7c` |
| `verification/TP07-D-prepared-v3-source/cleanup-source.json` | `3f2bd2596916da0ca8f586d1c1d97a8762bddb50e731c41fd169f03a95afa437` |
| `verification/TP07-D-prepared-v3-lock/lock-manifest.json` | `4ed4ec81737f1bdce1fa01aa0f878e23d985f3402e592a1b352fcae1a3be8478` |
| `verification/TP07-D-prepared-v3-lock/native-proof.json` | `7a404cca0e9cc5d43ae4078659a75137b478ee5c6510ff044c0b17c2c4a5cb97` |
| `verification/TP07-D-prepared-v3-control/conditional-lock-readback.json` | `4dd290ba30486b5338227bca7dd6183117041f3da6488bf2bb297ee54834c2e8` |
| `verification/TP07-D-prepared-v3-build/startup-manifest.json` | `b32715121ae657503c14f9393e53cbba22b381108e5dcb4f6eb4e047fe4e919f` |
| `verification/TP07-D-prepared-v3-build/build-receipt.json` | `75a78fd8189ea9b49274f207c002dc49d18c2e146f5c7de91746ce2b0b6c1fd9` |
| `verification/TP07-D-prepared-v3-cleanup-build/startup-manifest.json` | `1d8876cdd14375e2edbd21bf3ff6977fde9e4718c9e8a2f1d29a9738729918af` |
| `verification/TP07-D-prepared-v3-cleanup-build/build-receipt.json` | `67fb03fb430b12fdc11600bae1e7495168eddfb9f19bee6b8ec09e171ab1f116` |
| `verification/TP07-D-prepared-v3-build/cli-proof.json` | `266aa79643a5c2d7a9e330c072c3410e446474ac6aaf7ac640fcb26cad123e1d` |
| `verification/TP07-D-prepared-v3-control/offline-chain-readback.json` | `a3643541fe05becc884337f43c07aec927ab43b2545c05af5477fa778190611b` |

## 接受的实际结果

Low 的新 9 项 pruning 合同 exit 0，日志哈希与 summary 一致。P0 workflow 193、cleanup 182 项实际文件逐项符合 manifest。N0 新目录、新 native cache 的 SDK 查询及 L0/D 两次 restore 共 3 事件均 exit 0；形成 workflow 195 项源码及新 lock/native proof。

本次独立读取实际锁、assets 和自身 dgspec，并对照原包 records 核验：provider/L0 各 13 个非驱动包、D 精确 24 个；D 固定裁剪九项 Microsoft.Extensions 包，保留包 resolved/contentHash 不变，依赖边仅删除指向该九项的边。provider/L0 原边保持。两份实际 L0/D 锁的覆盖为 D-only 20、L0-only 9、shared 4，并集精确 33。逐项核对 fresh archive、metadata、nuspec、sidecar、原始 archive/sidecar 和实际锁 occurrence；sidecar 匹配完整 ZIP SHA512，metadata contentHash 匹配原 records 与锁，未混淆两种摘要。

provider/L0/D 三份实际锁字节分别仍为 `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4`、`896258992bb91e4289953fef191287f8e6fe496706d6a3df070a85eb5dc5916e`、`678b6a6a5cd173a61e633ed4390b121175e94244b415019ca31281d666798171`，满足 root 条件门。

B0 的 SDK 查询、L0 locked restore/build、D locked restore/build 共 5 个新事件全部 exit 0 且未超时。主构建 source195、125 项 binary、assets、locks、host DLL/deps 及引用证明均与磁盘字节一致。BC 独立目录的 SDK 查询、L0 locked restore/build 共 3 个新事件全部 exit 0 且未超时；source183、25 项 binary 与磁盘一致，仅包含 provider/L0 角色。receipt 的 source、pruning 与 shared-framework 前后哈希一致。

只读 CLI 3 个事件分别为 SDK `--version`、`--list-runtimes`、隔离 tool root 的 `dotnet exec dotnet-ef.dll --version`，均 exit 0。它们证明该 CLI 的版本查询能够执行，不代表 scaffold、migration 或 D host 已执行。SDK 为 10.0.401；AspNetCore 10.0.12 和九项 framework DLL 的证明范围是已安装预期字节，尚无 D host 实际加载观察。

本轮独立核对的 manifest 引用、源码、锁、assets、框架和构建产物共 1,344 次文件哈希比较无差异；另核验 P0 文件、native33 实际内容及 `build-before-input-hashes.json` 的全部 7,129 项旧输入，无漂移。Low 最终回读另外记录 W 产品 388 项无漂移；此处不把它扩张为产品行为重新验收。

旧 N0 失败 summary `TP07-D-prepared-v1-control/verification-summary.json` 仍为 `c2a1d79ce6ca8f5e78dee56ce29412b5b86322142fbee934ee59423d0043169d`；旧 B0 编译失败回读 `TP07-D-prepared-v2-control/build-failure-readback.json` 仍为 `1eacee6b8ffe50ed612f5315cbbfefacc53e6d5d2b213c32a6829ebcc8392c62`。本轮成功没有改写历史失败。

## 后续边界

接受范围只到新的 D 离线准备与构建链。E 模型前置门仍 pending；D runtime activation 不因本报告自动开放。D startup、wrappers、DB、服务器最终状态验证、scaffold/migration、generated consumer 自身 restore/build、G6/G12 均未执行或接受。startup-manifest 的存在只证明构建产物身份，不证明启动。BC 只接受独立清理 host 的离线构建，不证明执行过数据库清理。后续须由 root 按原 DAG 与 activation gate 安排，保持当前冻结证据。
