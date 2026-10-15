# 后续工作

按当前源码整理。当前已发布 [面向 .NET 10 的驱动预览版 0.1.0-preview.1](release-0.1.0-preview.1.md)；本页区分待办与未发布源码能力，不是当前包支持声明。GitHub issue 是后续进展入口；源码中已合入但尚未发布的改动单列于下方。

| 方向 | Issue | 当前缺口与推进方式 |
| --- | --- | --- |
| 物理连接复用 | [#6](https://github.com/whynpc9/W.DamengProvider/issues/6) | 阻塞：已取得性能基线；8.1.5.60 上 TEST 无完整 reset 入口，序列 `CURRVAL` 等会话值不可恢复（[记录](compatibility/I06-pool-reuse-baseline.md)）。维持每租约新建、归还销毁，待服务器提供完整复位能力。 |
| 存储过程与输出/游标参数 | [#9](https://github.com/whynpc9/W.DamengProvider/issues/9) | 源码已实现严格过程调用、标量 OUT/INOUT、函数返回和受管 OUT 游标；显式事务组合、游标输入及过程批调用拒绝。见[源码契约](compatibility/I09-stored-procedures.md)，尚未发布。 |
| 元数据 API | [#10](https://github.com/whynpc9/W.DamengProvider/issues/10) | 当前源码补齐异步发现及单条 SELECT 的 SchemaOnly/KeyInfo；权限与支持范围见 [I10](compatibility/I10-metadata.md)，尚未发布。 |
| OS 与服务器兼容覆盖 | [#11](https://github.com/whynpc9/W.DamengProvider/issues/11) | [Windows / win-x64 / DM 8.1.5.60 明文](compatibility/I11-windows.md)与 [Ubuntu WSL2 / linux-x64 离线及包消费](compatibility/I11-linux-validation.md)已有记录；Linux 实库及长时资源、额外服务器 profile 和远程 TLS 等继续按实际环境推进。 |
| .NET 8/9/10 多目标 | [#12](https://github.com/whynpc9/W.DamengProvider/issues/12) | 源码、测试和发布矩阵已扩为三目标；[验证记录](compatibility/I12-multitarget.md)区分离线结果与待补 shared/严格 TLS 实库，尚未发布。 |
| .NET 6 兼容层 | [#13](https://github.com/whynpc9/W.DamengProvider/issues/13) | 源码、测试和发布矩阵已加入 net6：typed 数据源 API（无 `DbDataSource` 多态）、按同一链策略重建的 TLS 验证、官方 TimeProvider 兼容包；[验证记录](compatibility/I13-net6.md)，尚未发布。 |

继续补齐各目标的严格 TLS 实库验收。性能方向优先连接复用；存储过程和元数据可按实际业务需要独立安排。所有实施均遵循 [AGENTS.md](../AGENTS.md) 的分工与 TEST 边界。

## 已合入、未发布

- [issue #7](https://github.com/whynpc9/W.DamengProvider/issues/7)、[#8](https://github.com/whynpc9/W.DamengProvider/issues/8) 数组绑定与 DbBatch：`ArrayBindCount` 以单次 EXECUTE 发送多行 INSERT/UPDATE/DELETE/MERGE，失败为语句级原子；DbBatch 在独占连接上逐条执行，逐命令计数与失败归属，不减少往返。MERGE 的影响行数改为服务器计数。见[说明](compatibility/I07-I08-array-binding-dbbatch.md)。
- [issue #16](https://github.com/whynpc9/W.DamengProvider/issues/16) EF 接入反馈：传输失败的 `FailureInfo`、事务内 DML 匿名块、8/9 位 typed NULL、Broken 提示、公开 `ServerCapabilities` 及若干消息改进，见[修订说明](compatibility/I16-ef-integration-feedback.md)。后续源码修订开放已验证 extended profile 的 CLR 时间值 Scale 8/9 无损写入、有限的 Prepare 错误恢复及本地绑定失败后的会话保留；其他 profile 证据仍随 #11 推进。

## .NET 扩展边界

当前源码构建 `net6.0;net8.0;net9.0;net10.0`，共用 C#12 与固定 SDK 10.0.401。各目标分别验证完整构建/打包、资源程序集、实际运行时和包消费者；TLS/实库尚待授权测试配置。编译探测或跨主版本 runtime roll-forward 不代表目标运行时已支持。.NET 6 的数据源与 TLS 差异见 [I13](compatibility/I13-net6.md)，未通过放宽证书验证实现。

运行时支持周期另见[微软官方政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，与本驱动的已验证兼容范围分别判断。

## 项目边界

EF 下游依赖切换、重新编译、EF Core 各版本适配和 EF 特有补验由下游项目管理，本仓库不重复建立 issue。历史 EF 报告中的权限失败与未完成正向验证仍保留原结论，不改写为当前驱动缺陷或已通过。

已实现阶段的设计和验收材料见[实现文档索引](implementation/README.md)。历史 `not_started`、停止标志或旧候选失败不自动加入本路线图。
