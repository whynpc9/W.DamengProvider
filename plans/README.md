# Improve 审查与修复记录

本目录保存深度审查及实施计划。2026-10-22 首批五项修复已合并，并随 [0.3.1](../docs/release-0.3.1.md) 发布；当前产品能力和待验证项以最新文档为准。

| 日期 | 基线 | 文档 | 状态 |
| --- | --- | --- | --- |
| 2026-10-22 | `aa2ca2d` | [深度审查报告](audit-2026-10-22.md) | 审查完成；2026-10-23 用户批准首批 F01–F05 |
| 2026-11-08 | `963ffbc` | [本轮深度审查报告](audit-2026-11-08.md) | 审查完成，用户选择执行 006–010。进度见下表。 |

最新审查重新核对 F06–F15。F13 扩展到当前发布 smoke 与参数推导探针。F14 的元数据部分已修复，IdleTimeout 和 Windows TLS 私钥说明仍待更新。新增 F16 数据源输入冻结、F17 数组形状预检、F18 远端 CLOB 多字节尾部遗漏。

## 本轮实施顺序与状态

状态使用 TODO / IN PROGRESS / DONE / BLOCKED（附具体原因）。DONE 必须有实际行为验证与独立复核，不以文档已写完代替修复完成。每份计划都包含独立背景、源码摘录、文件边界、Bash 命令、预期结果及停止条件。

| 计划 | 目标 | 优先级 | 工作量 / 风险 | 依赖 | 状态 |
| --- | --- | --- | --- | --- | --- |
| [006 / F18](006-clob-multibyte-write.md) | 完整发送远端 CLOB 多字节文本 | P1 | M / 中 | 无 | DONE · [#31](https://github.com/whynpc9/W.DamengProvider/pull/31) `7bce364` |
| [007 / F06](007-interval-month-range.md) | 缩窄前验证 long 月数 | P1 | S / 低 | 无 | DONE · [#30](https://github.com/whynpc9/W.DamengProvider/pull/30) `aaea781` |
| [008 / F17](008-array-shape-preflight.md) | 全参数形状预检先于行分配 | P1 | S / 低 | 无 | DONE · [#29](https://github.com/whynpc9/W.DamengProvider/pull/29) `0004040` |
| [009 / F07](009-datasource-batch-cancellation.md) | 批次取消贯通连接等待、握手与执行 | P1 | M / 中 | 无 | DONE · [#32](https://github.com/whynpc9/W.DamengProvider/pull/32) `828b53b` |
| [010 / F16](010-datasource-input-snapshot.md) | 数据源 Open 前冻结 SQL 和输入 | P1 | M–L / 中 | 008、009 | DONE · [#33](https://github.com/whynpc9/W.DamengProvider/pull/33) `adf4734` |

010 的实施估算较审查阶段细化为 M–L，原因是需要保留流式输入的协商后检查、原输出参数身份及单次 plan 转交，不能仅给 wrapper setter 加锁。

### F07/F16 文件归属与交接

| 共享范围 | 009 / F07 负责 | 010 / F16 接续 |
| --- | --- | --- |
| DmDataSourceCommand.cs 中 batch wrapper | 先创建关闭 connection，登记本轮 owner；取消、Dispose、Reader 交付和物理关闭后释放 | 在同一个 owner/connection 生命周期内，Open 前捕获输入；同时覆盖普通 shortcut |
| DmDataSourceExecution.cs | Open token 与分类、绑定具体 batch execution、早到/迟到取消隔离 | 保存/转交/释放本轮 plan，复用取消模型 |
| DmBatch.cs / DmBatchExecution.cs | Begin 时把具体执行交接给本轮取消上下文 | 将 Capture 前移到 Open 前、Acquire 保留在 Open 后，消费同一份计划 |
| DmCommand.cs / DmCommandPlan.cs | 009 不改普通 command 的捕获设计 | 010 分离本地快照与协商后验证；保留008的全参数预检 |
| PoolTests 与共享协议 fixture | 009 建立批次生命周期回归及最小可复用 channel | 010 新增输入冻结/编码回归，保留009所有行为断言 |

按用户要求，每个计划使用单独的 Codex Worktree 会话，模型为 `gpt-6.1-sol`，推理档位 `high`。010 在 008 和 009 的独立验证完成后承接这两项提交，先在本地叠加实现。008 和 009 合并后，010 的实现提交转到最新 `main` 上复验并创建独立 PR。

006–009 均已通过四个目标运行时的独立复验及 `netstandard2.1` 构建。006 的 LobTests 各 333 项，007 的 TypeTests 各 1018 项，008 的 CommandTests 各 220 项，009 的 PoolTests 各 346 项、CommandTests 各 212 项。四个 PR 均通过 GitHub `offline` 检查，Codex 审查与 GitHub review threads 在合并时无未解决项。

010 在 `c559a25` 上独立复验通过 3720 项，四个目标各通过 PoolTests 375 项、CommandTests 222 项、LobTests 333 项。`netstandard2.1` 构建与 GitHub `offline` 检查通过。审查确认取消连接等待时需要保留上一轮 `RecordsAffected`，实现已调整并补回归。两个 T25 竞态测试迁移到真实合成协议通道，保留原断言。合并时没有未解决的审查。

2026-11-09 在 `adf4734` 上补齐五项达梦定向实库验证，最终退出 0。最终轮五张表全部由新连接确认不存在，含诊断和基线对照共清理 15 个精确对象。F06 的直接绑定描述限制及可用入口见[实库验证记录](verification-2026-11-09.md)。

共同生命周期：**创建本轮关闭连接 → 010 本地捕获 → Open → 协商后校验/Acquire → 执行 → Reader 转交或结束 → 物理关闭完成 → 退休 owner**。009 先实现其中不含输入捕获的所有权/取消链；010 不重建第二套 owner。

明确保持：旧 Cancel 只作用于捕获的旧身份；batch 已交付 Reader 后 Dispose batch 不关闭 Reader；batch Prepare 仍本地预检；提前捕获不开始 command 执行 deadline、不读取调用方流、不放宽 negotiated profile 检查。

### 验证顺序

1. 006：新增 CLOB wire 回归先红后绿，再 LobTests 四目标及 Standard 构建。
2. 007：构造器和真实绑定/编码边界先红后绿，再 TypeTests 四目标及 Standard 构建。
3. 008：安全的分配增长/全参数预检回归先红后绿，再 CommandTests 四目标；通过后交出 DmCommandPlan.cs 给010。
4. 009：先无限池等待/握手取消，再 Open→Begin 交接、原 token、旧取消隔离、Dispose/Reader/物理容量；独立执行 PoolTests、CommandTests 四目标和 Standard 构建，记录009阶段结果后交给010。
5. 010：先普通 shortcut 冻结/流式输入，再 batch Capture→Open→Acquire；最后重跑009全矩阵和008回归，独立执行 PoolTests、CommandTests、LobTests 四目标及 Standard 构建。只有实际改变 async core 的取消/恢复分支时再补 AsyncTests 四目标。

006/007/008 可由不同实现者处理各自文件；共享 bin/obj 的构建一律串行。009可独立推进，但010同时等待008和009完成。010改变共享生命周期后重跑009有必要；不要求重复无关历史实库矩阵。所有计划的核心行为均可离线验证，本轮不需要数据库或新增权限。不能把缺失运行时、过滤器零匹配或未运行项目记为通过。

本轮未选择的 F08–F15、I01/I02 及两个方向继续保留在审查报告中；不自动扩展本次实施范围。计划生成阶段未执行上述 .NET 命令。

计划质量检查：已由无此前计划上下文的独立审查者冷读五计划、索引并抽查对应源码/夹具；007 的任意 leadScale 整数上限歧义已修正，复核无剩余阻塞项。五份计划的命令块通过 Bash 语法检查，章节/索引链接/空白检查通过。此结论仅证明计划可执行性，不代表产品修复或运行验证已完成。

2026-10-22 审查覆盖命令/Reader、会话/事务/池、传输/安全、类型/LOB、测试/工具、依赖/发布和架构方向，收录 F01–F14、持续验证改进 F15、协议调查 I01 及两个独立方向。主审重新核对收录项的源码证据；审查阶段未修改产品代码。

## 首批修复

[PR #27](https://github.com/whynpc9/W.DamengProvider/pull/27) 于 2026-10-28 合并为 `9c1eb577c97b0fe1be234ec78578d7381ae0d80b`，基线为 `aa2ca2d8d979f50605aea9f4c217bec3c1fd30c3`，PR head 为 `dbab1c83769a402b20f24de29e84dbe9edb381ba`。详细命令和证据见[修复、验证与合并记录](verification-2026-10-23.md)。

| 计划 | 完成状态 | 依赖 |
| --- | --- | --- |
| [F01：保留远端 LOB 截断长度](001-lob-truncate-width.md) | 已完成，`85bb32c`；Lob 四运行时通过 | 无 |
| [F02：顺序读取判空不消费当前值](002-sequential-null-peek.md) | 已完成，`bdd8249`；Lob/Type 四运行时通过 | 无 |
| [F03：安全完整地派生过程参数](003-derive-parameters.md) | 已完成，`dbab1c8`；独立过程/函数实库 9/9；包内过程实库仍待验证 | 无 |
| [F04：修正公开 BLOB Stream 行为](004-blob-stream-contract.md) | 已完成，`7d8f071`；Lob 四运行时通过 | 与 F01 协调验证 |
| [F05：DbBatch 保留驱动特定值](005-batch-provider-values.md) | 已完成，`3730caa`；Command 四运行时通过 | 无 |

PR 合并时还未发布新包；这些修复随后已包含在 0.3.1 中。包内过程测试因 TEST 缺少 `CREATE PACKAGE` 权限仍为 `integration_pending`，表示尚未完成实库验证，不是通过。

## 已评估但不采用的方向

以下方向没有进入首批实施：物理连接直接复用、旧目标框架的明确功能限制、顺序 `DbBatch`、过程事务限制，以及没有生产可达证据的遗留分支。完整理由见审查报告，后续调查前先核对，避免重复工作。

历史测试数量只证明当时提交；关闭自动依赖审计也不被当作已知 CVE。

本轮考虑并排除的项目、部分解决项和未覆盖范围见[最新报告](audit-2026-11-08.md)。已完成的 001–005 保留原状态，不因新审查重开。
