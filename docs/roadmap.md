# 后续工作

按当前源码整理。当前已发布 [面向 .NET 10 的驱动预览版 0.1.0-preview.1](release-0.1.0-preview.1.md)；本页列出的扩展均为待办，不是当前包支持声明。GitHub issue 是后续进展入口；源码中已合入但尚未发布的改动单列于下方。

| 方向 | Issue | 当前缺口与推进方式 |
| --- | --- | --- |
| 物理连接复用 | [#6](https://github.com/whynpc9/W.DamengProvider/issues/6) | 当前每租约新建、归还销毁。先取得性能基线与完整 reset 证据，再开放安全复用。 |
| 数组绑定 | [#7](https://github.com/whynpc9/W.DamengProvider/issues/7) | 当前明确拒绝；先表征批量协议、逐行结果与失败语义。 |
| DbBatch | [#8](https://github.com/whynpc9/W.DamengProvider/issues/8) | 复用 #7 协议研究，另行证明多命令、多结果及错误归属。 |
| 存储过程与输出/游标参数 | [#9](https://github.com/whynpc9/W.DamengProvider/issues/9) | 当前仅 Text/Input；扩展前明确输出读取时机及游标生命周期。 |
| 元数据 API | [#10](https://github.com/whynpc9/W.DamengProvider/issues/10) | GetSchemaAsync、SchemaOnly、KeyInfo 尚未开放，按最小权限语义补齐。 |
| OS 与服务器兼容覆盖 | [#11](https://github.com/whynpc9/W.DamengProvider/issues/11) | 按实际环境逐项补实库与资源验证，再扩展错误恢复和时间能力 profile。 |
| .NET 8/9/10 多目标 | [#12](https://github.com/whynpc9/W.DamengProvider/issues/12) | 优先扩展8/9；包括运行测试、包依赖与资源、消费者和发布设施。 |
| .NET 6 兼容层 | [#13](https://github.com/whynpc9/W.DamengProvider/issues/13) | 在 #12 基础上处理数据源 API 差异、TLS、时间 API 和 C#10 语法。 |

建议先推进 .NET 8/9/10；.NET 6 单独完成兼容设计及验证。性能方向先连接复用，再数组绑定与 DbBatch；存储过程和元数据可按实际业务需要独立安排。所有实施均遵循 [AGENTS.md](../AGENTS.md) 的分工与 TEST 边界。

## 已合入、未发布

- [issue #16](https://github.com/whynpc9/W.DamengProvider/issues/16) EF 接入反馈：传输失败的 `FailureInfo`、事务内 DML 匿名块、8/9 位 typed NULL、Broken 提示、公开 `ServerCapabilities` 及若干消息改进，见[修订说明](compatibility/I16-ef-integration-feedback.md)。后续源码修订开放已验证 extended profile 的 CLR 时间值 Scale 8/9 无损写入、有限的 Prepare 错误恢复及本地绑定失败后的会话保留；其他 profile 证据仍随 #11 推进。

## .NET 扩展边界

当前产品仅构建 `net10.0`。扩展到 .NET 8/9/6 时，需要分别完成完整构建/打包、资源程序集、对应运行时、包消费者及 TLS/实库验证；编译探测或 SDK roll-forward 不代表目标运行时已支持。.NET 6 另需处理 DbDataSource 基类、TLS 证书验证与部分时间 API 的兼容，不能通过放宽证书验证通过。

运行时支持周期另见[微软官方政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)，与本驱动的已验证兼容范围分别判断。

## 项目边界

EF 下游依赖切换、重新编译、EF Core 各版本适配和 EF 特有补验由下游项目管理，本仓库不重复建立 issue。历史 EF 报告中的权限失败与未完成正向验证仍保留原结论，不改写为当前驱动缺陷或已通过。

已实现阶段的设计和验收材料见[实现文档索引](implementation/README.md)。历史 `not_started`、停止标志或旧候选失败不自动加入本路线图。
