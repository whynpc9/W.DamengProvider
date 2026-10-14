# 当前窗口：issue #16 EF 接入反馈修订（2026-10-14）

用户授权：解决 [issue #16](https://github.com/whynpc9/W.DamengProvider/issues/16)，开 PR、处理审阅后合并。其余开放 issue（#6–#13）为独立功能，本窗口不处理。

| owner | 角色与范围 |
| --- | --- |
| root | 设计、产品与测试代码、文档、实库探针、PR 与审阅处理 |
| review（独立 agent） | 只读代码审查；不运行实库、不加载 secrets |

本窗口 Codex（Sol/Astra）子任务不可用（provider 状态检查超时），按 AGENTS.md 工具阻塞规则由 root 直接实现，独立审查改由另一 agent 承担。实库只使用 `WDM_PROVIDER_TEST`，探针为 [tools/Issue16Probe](../../tools/Issue16Probe/Program.cs)；结果与行为说明见 [issue #16 修订](../compatibility/I16-ef-integration-feedback.md)。

当前发布仍为 `0.1.0-preview.1`；本窗口修订合入 main 后未随该包发布。发布与公开安装验证见[发布记录](../release-0.1.0-preview.1.md)，机器状态见 [progress.json](progress.json)，后续工作见[路线图](../roadmap.md)。
