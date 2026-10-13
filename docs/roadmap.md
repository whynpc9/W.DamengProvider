# 后续工作

2026-10-13 整理。当前已发布 [面向 .NET 10 的驱动预览版 0.1.0-preview.1](release-0.1.0-preview.1.md)；本页列出的扩展均为待办，不是当前包支持声明。GitHub issue 是后续进展入口；本轮已完成文档与发布状态同步，没有为已完成事项创建开放待办。

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

## .NET 扩展调研边界

基线 `a67a929` 的隔离编译中，net9 原源码通过；net8/C#12 初始仅有证书加载与格式化重载四个错误。net8 研究副本使用条件 `Microsoft.Bcl.Cryptography 10.0.12`（传递依赖 `System.Formats.Asn1`）并替换两处空 Span 参数后，C# 错误消除，但 build/pack 因四套语言资源程序集缺失报 MSB3030，原因尚未确定。同一副本覆盖 net9 构建通过。此结果不代表 net8 完整构建、旧运行时执行或实库通过。

net6 首轮19个编译错误不是完整兼容工作量：还需处理不存在的 DbDataSource 基类、CertificateChainPolicy、TimeProvider、部分 Span/Stopwatch API 与语法。其数据源标准基类差异和 TLS 等价验证必须明确。调研日志保存在本机 ignored `.local/framework-research-20261013/logs/`；实施时保留失败记录并重新验证实际候选。

微软 .NET 8/9 支持期均到 2026-11-10，.NET 6 已于 2024-11-12 结束支持；.NET 10 支持至 2028-11-14。驱动兼容范围与微软运行时支持周期分别说明，见[官方政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。

## 项目边界

EF 下游依赖切换、重新编译、EF Core 各版本适配和 EF 特有补验由下游项目管理，本仓库不重复建立 issue。历史 EF 报告中的权限失败与未完成正向验证仍保留原结论，不改写为当前驱动缺陷或已通过。

已实现阶段的设计和验收材料见[实现文档索引](implementation/README.md)。历史 `not_started`、停止标志或旧候选失败不自动加入本路线图。
