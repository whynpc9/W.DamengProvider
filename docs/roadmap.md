# 后续工作

当前公开包为[面向 .NET 10 的 0.1.0-preview.1](release-0.1.0-preview.1.md)。下一版 [0.2.0 正在准备](release-0.2.0.md)，包含 .NET 6/8/9/10；本页区分后续缺口和已合入的未发布能力。历史测试计数只属于原候选。

## 后续缺口

| 方向 | Issue | 当前边界与推进方式 |
| --- | --- | --- |
| 物理连接复用 | [#6](https://github.com/whynpc9/W.DamengProvider/issues/6) | 调查已完成；8.1.5.60 上 TEST 无完整 reset 入口，序列 `CURRVAL` 等会话值不可恢复。继续维持每租约新建、归还销毁，待服务器提供可验证的完整复位能力。见[性能基线与阻塞记录](compatibility/I06-pool-reuse-baseline.md)。 |
| OS 与服务器兼容覆盖 | [#11](https://github.com/whynpc9/W.DamengProvider/issues/11) | 已有 [Windows / win-x64 / DM 8.1.5.60 明文](compatibility/I11-windows.md)与 [Ubuntu WSL2 / linux-x64 离线及包消费](compatibility/I11-linux-validation.md)记录；额外服务器 profile、远程 TLS、系统信任库与真实 CRL/OCSP、长时资源压力仍需按环境补验。当前候选的 macOS 四目标及 shared/隔离 TLS 结果见[发布准备](release-0.2.0.md)。 |
| 存储过程扩展 | [#9](https://github.com/whynpc9/W.DamengProvider/issues/9) | 已实现能力见下方；显式事务组合、游标输入和过程批调用仍拒绝，是否扩展由业务需求和服务器证据决定。 |
| 元数据覆盖 | [#10](https://github.com/whynpc9/W.DamengProvider/issues/10) | 已实现能力见下方；`ReservedWords` 的系统目录访问受 TEST 权限限制，不为测试扩大授权。 |

## 已合入、未发布

| 能力 | 实现与边界 |
| --- | --- |
| .NET 8/9/10（[#12](https://github.com/whynpc9/W.DamengProvider/issues/12)、PR #21）及 .NET 6（[#13](https://github.com/whynpc9/W.DamengProvider/issues/13)、PR #25） | 同包四目标，统一 C#12、SDK 10.0.401。net6 使用 typed `DmDataSource`，无 `DbDataSource` 多态；TLS 不放宽证书策略。兼容依赖与原运行时验证见 [I12](compatibility/I12-multitarget.md)、[I13](compatibility/I13-net6.md)，当前候选结果见[发布准备](release-0.2.0.md)。 |
| 数组绑定与 DbBatch（[#7](https://github.com/whynpc9/W.DamengProvider/issues/7)、[#8](https://github.com/whynpc9/W.DamengProvider/issues/8)，PR #19） | `ArrayBindCount` 单次 EXECUTE 发送多行 INSERT/UPDATE/DELETE/MERGE，失败为语句级原子；DbBatch 在独占连接上逐条执行，报告逐命令计数与失败归属，不减少往返。MERGE 使用服务器影响行数。见 [I07/I08](compatibility/I07-I08-array-binding-dbbatch.md)。 |
| 存储过程、标量输出/返回值与受管 OUT 游标（#9，PR #23） | 严格过程调用、原子输出发布及游标所有权契约，见 [I09](compatibility/I09-stored-procedures.md)。 |
| 异步发现及 Reader 元数据（#10，PR #22） | 统一同步/异步 GetSchema 和 GetColumnSchema；单条 SELECT 的 SchemaOnly/KeyInfo，未知键信息保持未知。见 [I10](compatibility/I10-metadata.md)。 |
| EF 接入反馈修订（[#16](https://github.com/whynpc9/W.DamengProvider/issues/16)，PR #17/#18） | 传输失败 `FailureInfo`、事务内 DML 匿名块、8/9 位 typed NULL、Broken 提示、`ServerCapabilities`；已验证 extended profile 的 CLR 时间值 Scale 8/9 无损写入、有限 Prepare 恢复和本地绑定失败后保留会话。见 [I16](compatibility/I16-ef-integration-feedback.md)。 |
| Windows TLS 私钥处理 | Windows 临时原生私钥导入及真实 Windows 明文验证见 [I11](compatibility/I11-windows.md)；不据此声明所有 Windows TLS profile 已通过。 |

各目标分别核验构建、打包、资源、实际运行时和独立包消费；编译探测或跨主版本 roll-forward 不等于目标运行时验收。驱动兼容范围与[微软运行时维护周期](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)分别判断。

## 项目边界

EF 下游依赖切换、重新编译、各 EF Core 版本适配和 EF 特有补验由下游项目管理。本仓库不把旧权限失败、历史 `not_started`、停止标志或旧候选失败自动转成新的驱动缺陷或发布前置条件。

当前实现见[实现文档索引](implementation/README.md)，开发、验证和 TEST 边界见 [AGENTS.md](../AGENTS.md)。
