# W.DamengProvider 实现与测试说明

当前工作树处于 NuGet 发布准备阶段，产品身份与发布材料正在统一；这不表示当前候选已通过发布验收或已经发布。当前状态见 [progress.json](progress.json)，发布操作以仓库发布说明及实际检查结果为准。

`W.DamengProvider` 负责 ADO.NET API、参数与结果类型、物理会话、事务、协议、传输、连接池、取消、超时和驱动诊断。`W.EntityFrameworkCore.Dameng` 作为独立项目负责 EF 模型、SQL 翻译、更新与迁移，驱动生产项目不引用 EF。

## 产品行为与限制

- [R1 基础能力](../compatibility/R1.md)、[R2 异步与取消](../compatibility/R2.md)、[R3 数据源与流式 LOB](../compatibility/R3.md)记录各阶段能力边界。
- [.NET 时间精度兼容说明](../compatibility/TP-temporal-precision.md)记录参数、结果、服务器 profile 和 EF 迁移限制。
- [EF 功能兼容说明](ef-functional-compat-gaps.md)记录当前错误恢复、SQL 参数标记与下游验证边界。
- [时间精度改造计划](temporal-dotnet-precision-plan.md)与相邻设计文档提供实现背景，不替代当前候选的验证。

## 本地测试

[本地达梦环境](local-dameng.md)、[隔离 TLS 环境](local-dameng-tls.md)和 [AGENTS.md](../../AGENTS.md)规定专用 TEST 身份、凭据保护、精确对象清理和实例操作范围。目录读取、临时 Schema 权限和 shared 宿主预算的维护限制见 [maintenance/](maintenance/)。

离线构建或单元测试通过不代表真实达梦集成通过；缺少显式测试连接时记录 `integration_pending`。发布前需对实际候选执行适用检查，并记录命令、退出码、加载程序集和数据库最终状态。

历史任务报告、旧规范包与生成验证制品已移至本机 ignored 归档。保留的历史设计和兼容记录描述各自时点的范围，不能作为当前版本发布通过的证明。
