# 时间精度历史输入与验收记录

当前实现以[时间精度兼容说明](../../compatibility/TP-temporal-precision.md)和[issue #16 修订](../../compatibility/I16-ef-integration-feedback.md)为入口，当前发布见[发布记录](../../release-0.2.0.md)。

本目录仅保留以下材料：

- 工具按路径/字节读取的原设计与失败解释，以及 manifest 中仍引用的设计标识。为保持这些历史输入可解释，不改写其中的原候选或 hash。
- [物理 profile 决策](TP01-profile-handoff.physical-v11.md)及[来源更正](TP01-profile-origin-correction.md)，解释源码中的 profile 身份。
- [TP06 ADO.NET 接受记录](TP06-acceptance.md)与[TP07/TP08 EF 收口](TP07-TP08-closure.md)，保留各自候选的证据范围。

这些材料中的模型名、root/High/Low、窗口、冻结、approval 与运行权限均是历史记录；不指定当前使用的模型、harness 或人员，不自动授予数据库/发布权限，不新增当前开发审批步骤。原设计中的未实施、停止或待验收状态不代表当前源码状态。

重复审阅、交接、失败重试流水和专项 status.json 已清理，完整历史可从文档清理前的 Git 提交 `3091108` 查询。保留文件中指向 Git 固定提交的链接仅供查阅历史。
