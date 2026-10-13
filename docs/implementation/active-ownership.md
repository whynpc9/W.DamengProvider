# 当前窗口：文档状态同步与 GitHub 路线图（2026-10-13）

用户授权：根据当前项目状态清理文档、同步发布状态，并将已讨论待办分别创建为 GitHub issue；EF 下游接入由下游项目处理。

| owner | 角色与范围 |
| --- | --- |
| root | 当前文档、发布记录、路线图、GitHub issue 与文档交付 |
| docs_audit（Sol High） | 独立只读审查历史与当前状态边界、本地链接 |
| docs_verify（Sol Low） | 独立检查最终文档、JSON、链接、issue 内容；不运行产品构建或实库 |

本轮不改产品、测试、发布脚本或包，不运行数据库。此前发布准备和版本范围调研窗口已结束，旧 owner 不再持有写入或构建窗口。

当前发布为 `0.1.0-preview.1`，tag 指向 `a67a9298daaa42b18203a9b405ec05a3357ed4b0`；发布与公开安装验证见[发布记录](../release-0.1.0-preview.1.md)，机器状态见 [progress.json](progress.json)，后续工作见[路线图](../roadmap.md)。历史本地准备样本保留在[发布前检查](../release-preflight.md)，不覆盖正式发布包身份。
