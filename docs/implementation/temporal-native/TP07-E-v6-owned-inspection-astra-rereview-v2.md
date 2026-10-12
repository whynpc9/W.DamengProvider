# TP07 E-v6 exact-nine inspector v2源码验收

2026-10-09。结论：**exact_nine_readonly_inspector_source_accepted / Low_build_selftest_pending / DB_not_authorized_by_this_review**。v1唯一阻塞已关闭，无新增源码阻塞。root可安排Low隔离构建与离线自测；本报告不接受尚未执行的36项结果，也不证明九对象absence。

本轮只读v1/v2三源差异、原复审及triage，计算文件hash与两方法外字节比较；未执行SDK、自测、validator、DB或探测。仅新增本报告。

| 冻结输入 | SHA256 |
| --- | --- |
| v2 Program.cs | `0225e45046a5741a6c10ded4193713b3ae79c9877a9dc4c4924163d878ed2d11` |
| v2 README.md | `cd5f690dc5d3d155b73bc8d1de53099fcfa8b30b2d82b7bb5e7e80bdcfa91e1b` |
| v2 csproj（未变） | `d8fe98f761ef46b8381001c57764e1f77f70f7162fe8ddac99bcf8aa2a2c154a` |
| v1阻塞报告 | `7ea14e28b39f88a5c3ad5ffb68aeb376b9d4379b1cd50040015f75b153a860b7` |
| slow-runtime triage | `9748c14092768acb4ce37a677977b67ae72311f0428b8e17a35ef05dc810748a` |

实际源码仅`Columns`和`SelfTest`两方法变化；去除两方法后的其余Program字节与v1完全一致。README准确描述新增合同。`Facet`接受NULL、int/short/byte及可精确表示为Int32的long/整数decimal，拒绝bool、float/double、字符串、分数decimal和越界值；不赋予ID/N precision/scale非负、null或零语义。四列precision作为原始目录整数保留，D/Z scale仍必须非NULL整数7。四列顺序、类型、nullable、width4/9/11/32及外部marker/PK检查保持。

36项计划自测静态计数成立：原18项保留；新增3个完整四列正例覆盖非零、NULL及混合整数表示（含有符号边界）；12项precision/scale错误类型或范围拒绝；DT scale6和DTO scaleNULL各1项拒绝；N width128拒绝1项。正例是手写合同，不是实际v6目录；旧N128结构没有因facet修正被批准。测试函数仍在认证前分支运行，未读取TEST环境或建立连接。

两方法外字节相同，因此v1已核实的原九entry/原artifact哈希、认证前输入与W/host身份校验、固定SQL及参数绑定、TEST身份fail-closed、安全错误输出、显式PlaintextAllowed、失败无fallback保持。数据库查询仍仅身份SELECT、两条精确九表/PK batch及存在条目的四项目录查询；无DDL/DML/rowcount/SELECT1/trigger或任意范围入口。全量batch返回并完成名称/唯一性/PK归属校验后才形成absence观察；partial phase不是absence证明。batch中途行未单独持久化的原边界保持，不扩大报告语义。

Low下一步仍须核实际包feed、锁/assets/deps、加载W及外部批准host DLL/MVID/deps，执行36项离线检查并生成新artifact；原v1和旧失败保持不动。通过后root才另开只读DB窗口，可沿既定建议对新host设单次180秒外部总上限；该预算不保证39条目录/身份查询最坏时限内全部完成。超时只保留最后started phase与此前完整观察，不自动重试、提高原runner时限或重跑双cleanup。

单连接`inspection_complete_not_independent_closure`仍不关闭独立absence或恢复。任何后续DROP须另按fresh marker/精确结构所有权、每条最多一次和fresh独立absence门执行。本工具没有恢复动作；E-v6失败、D/完整C及TP08之后的LocalClaude门均不因本次源码验收改变。
