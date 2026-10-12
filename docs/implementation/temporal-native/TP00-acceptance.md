# TP-00 设计与基线准备验收

日期：2026-10-08。状态：`readiness_accepted`；TP-00 已关闭，TP-01/02 获准实施。本结论是静态准备门，不是产品或实库验收；A/B/C 仍未通过。

Sol High `tp_inventory` 完成 [入口清单](TP00-inventory.md) 与 [99 格/113 签名矩阵](TP00-contract-matrix.json)，Astra High `tp_design` 完成 [设计审阅](TP00-astra-design.md)，root 采纳 D01/D02/D03：metadata 原始字段在解码处保存、公开 legacy 12-byte 单位统一微秒且内部 Reader 使用 typed ns/ticks、既有明确布局的精确读取不新增全局版本拒绝。TP-03 必须同时接通最小 Reader 时间路由，不能接受七位读取回退。

Sol Low `tp_verify` 独立检查 43 项：687 个冻结 source 输入与 Git/archive/当前字节一致，1546 个历史 evidence 哈希未变；v1/v2/Astra/清单/矩阵身份、getter 格、公开签名、引用范围、能力守卫和文件交接通过。报告为 `.local/temporal-native/verification/TP00-baseline-design-check.json`，SHA256 `7f073a5c6fcd8dd948039e301fed0810259f9e3921d2515a08b93f75ec1e21e9`。归档和 frozen 文档哈希见清单；本轮无 dotnet/DB/secrets 读取。

## 固定 EF pin 与活动引用漂移

TP-00 冻结时选择 EF `5863d3755dbd2d333ac359bf1c0a789762adbaed`。验证时共享原仓库的活动 `origin/main` 已移动到 `8c192814a10645cd0545510ae7d3413ca8f971a1`；root 明确继续使用已选完整 SHA，不能将可变 ref 的漂移自动带入验收。该固定 commit 可达，TP-07 必须按完整 SHA 建立独立 worktree，不使用 dirty `aef25867` 或动态 `origin/main`。

原检查将“活动 ref 必须永久等于 pin”作为断言而报告失败；该要求不是本计划的固定输入身份契约。root 裁决后改为检查固定 commit 可达/后续 checkout 一致，漂移保留为非阻塞环境发现。裁决前原报告保存为 `.local/temporal-native/verification/TP00-baseline-design-check-before-root-EF-disposition.json`，不删除失败历史。冻结的 TP-00 文档保留当时观察，不修改其哈希。

## 下一窗口

TP-01 的 High 仅编写新 probe/runner，由 Low 使用原仓库 TEST/TLS wrapper 串行运行；TP-02 的 High 仅编写纯整数核心和测试，Luna 只提供 expected-only 合成向量。两者可并行写自己的文件，但各自冻结并经 Astra/root 审阅后才交 Low。TP-01 的未知服务器能力由实际证据分类，不能根据纯 codec 测试开放。所有历史快照和失败继续只读。
