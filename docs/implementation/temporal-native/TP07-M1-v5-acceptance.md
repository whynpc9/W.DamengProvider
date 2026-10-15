# TP07 mapping-v5 离线复验

2026-10-09。root 接受本轮限定离线门；L0 重验、实际 EF 运行与完整 C 门仍待完成。

[FF7 表达式前置门](TP07-FF7-expression-acceptance.md) 已独立通过53项。依[限定变更设计](TP07-FF7-mapping-change-design.md)，mapping-v5 仅修改一个产品 helper 和两个测试文件：声明 fraction7 时固定生成显式 FF7 的 TO_TIMESTAMP/TO_TIMESTAMP_TZ；0…6、bare6、NULL、守卫、参数与 reader 路径保持。没有按值尾零选择 SQL，也没有修改数据库配置或 W 候选包。

Astra [源码审查（历史）](https://github.com/whynpc9/W.DamengProvider/blob/309110800c4e81f795ff96ee006f58bd12f1952d/docs/implementation/temporal-native/TP07-mapping-astra-review-v5.md) 通过后，Sol Low 从 P1 基线独立重放175项源码，验证12项 overlay 路径。相对 v4 只有三文件变化，其余172项、项目/props/global 和六份原锁均未改变。明确继承原49包图证明并覆盖同一 unit lock，原 SDK 恢复事件保留来源，没有重复 force-evaluate。

SDK10.0.401 的九个阶段全部退出0。新测试82/82、受影响回归102/102，均零跳过；两项身份控制重叠，测试并集182项。独立观察器的14项真实模型参数事实全部通过，时间 Scale7/6 与 decimal12/2 合同不变。unit 与 observer 的独立 EF 构建身份分别核对，W DLL 始终是同一已接受候选；未运行数据库或 wrapper。

关键身份：

| 制品 | SHA256 |
| --- | --- |
| 完整 P1→v5 patch | `4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc` |
| mapping manifest | `23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c` |
| Low source manifest | `393d37e1c894625a3d80990850bf60f8b7b6d00bb81c0c6c5bf6e1a87c32e279` |
| 执行 summary | `6874f0f0d10259e162853391b0349d513bc02ef38da398760c6420a9853fec08` |
| 独立 readback | `9cc0a729fa3d5f65bf08e23444bbcd247e56bfb4da3bbea7d0eb932ae0c6573a` |

旧 mapping-v4、原 L0 literal 丢位及 startup schema 失败均保留。此门证明新映射实现与离线回归，不替代新 L0、真正 EF HasData/默认值/查询、scaffold 或迁移实库结果。
