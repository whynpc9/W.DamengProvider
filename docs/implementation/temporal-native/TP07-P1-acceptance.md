# TP07 P1：固定 EF 消费桥与未修正映射基线

2026-10-09，root 接受 P1 的独立桥重放、依赖锁图、构建及 14 项实际参数观察。**这不是 EF 时间语义或实库验收。** TP07 继续进入映射修正。

EF 固定 `5863d3755dbd2d333ac359bf1c0a789762adbaed`；独立源码来自 archive，未修改原有 dirty EF 工作区。桥的 43 项变更保留时间语义，仅替换驱动消费与必要 retry 边界，并增加观察器。W PackageReference 固定 `0.1.0-temporal.20261008144915`，nupkg SHA256 `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`，与 TP06 A/B 相同。

Sol High 实施，Astra High 审阅，Sol Low 在独立目录执行。SDK 10.0.401 的唯一 force-evaluate 成功后，13 个非驱动包的原 lock、新 provider/observer lock 和新 SDK cache metadata contentHash 全部相等；版本与依赖关系无漂移。随后冻结 168 项执行源码，locked restore、无隐式 restore 的 build、直接运行观察器 DLL 均退出 0。只涉及 provider 与 observer 两项目，未构建其它四类图，未调用 TEST wrapper 或数据库。

| 实际 EF 映射 | mapping Precision / Scale | W 参数 Precision / Scale | 显式设置 P / S |
| --- | --- | --- | --- |
| 默认 DateTime | 7 / null | 0 / 0 | false / false |
| HasPrecision(6) DateTime | 6 / null | 0 / 0 | false / false |
| 显式 TIMESTAMP(6) DateTime | 6 / null | 0 / 0 | false / false |
| 默认 DateTimeOffset | 7 / null | 0 / 0 | false / false |
| 默认 TimeOnly | 6 / null | 0 / 6 | false / true |
| 默认 TimeSpan interval | 9 / 6 | 0 / 6 | false / true |
| decimal 控制 | 12 / 2 | 12 / 2 | true / true |

每项 value 与 NULL 各观察一次，共 14 个唯一 ID。真实 EF relational connection factory 返回 W connection，其实际 command 和 mapping.CreateParameter 返回 W 类型；实际加载 W DLL SHA/MVID 与候选一致，deps 中 EF/Relational 均为 10.0.12，无官方驱动回退。Scale 为 0 但未显式设置不等于声明零位精度。此次事实确认缺口是 DateTime/DTO 的 fraction facet 未转译至参数 Scale；不能据此声称已有非零时间 Precision 缺陷。

观察器报告 SHA256 `a2d5f613a2059c0a2a30670bb5fc8cf43bee180dc630c8afdb900deb2573f127`；独立执行 summary `9e2f7b85e8d2be350aefb7c621e7d7e75d23194e96726d141d9c640bf9d08e8d`；执行源码 manifest `fc08ba8e0443b031757a4f01eaf55051c4fa9e2985163f30363df498e1d2a312`。实际 EF provider DLL SHA256 `84288a38c89be60ba9099ba2c030ec60f3c18cc851dc3a58e1d08ec787340027`，MVID `44006fe8-cb6b-4c22-882d-d3ae661411f5`。W 产品源码、原锁、历史失败及 TP06 证据前后哈希不变。

两个协调工具失败完整保留：v1 将 signed nupkg 的整体归档哈希误当 NuGet contentHash，SDK 尚未执行；v2 已恢复成功，但随后对已知 Project ID 作大小写敏感比较而停止，尚未构建。新 v3 仅规范化唯一已知 Project ID，包 ID、版本、依赖、contentHash、实际项目路径均继续精确校验。v4 从独立新副本运行，不回写任一旧失败。

可审计原始 JSON 以确定性 gzip 导出，原字节与制品哈希见 证据 manifest（历史记录已归档）。后续采用 [映射限定决定](TP07-mapping-decisions.md)，实际 EF shaper、数据库持久化、scaffold 与迁移仍待验收。
