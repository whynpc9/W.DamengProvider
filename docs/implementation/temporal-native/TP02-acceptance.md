# TP-02 纯整数时间核心验收

日期：2026-10-08。状态：`offline_accepted`，TP-02 关闭。Sol High `tp_core` 实施，Luna Max `tp_vectors` 提供 expected-only 合成向量，Astra High `tp_design` 完成设计和代码审阅，Sol Low `tp_verify` 独立执行，root 回读证据后接受。

交付五个新内部模块：不可变纳秒字段、原始 metadata、整数精度/CLR 投影/格式化、明确七种 packed wire 布局及 legacy 微秒适配、宽整数 interval。现有 writer/reader/绑定尚未接线，旧 DmTemporalCodec 未改；公开 helper 单位修正、默认七位选择和 TLTZ 门仍属后续任务。

## 独立验证

SDK 10.0.203，host `/usr/local/share/dotnet/dotnet`。全部命令设置独立可写 CLI_HOME、禁首次体验/遥测、单节点/noReuse/noSharedCompilation/disable-build-servers；330 项只读快照派生独立 build-work。精确命令、TRX 路径、输入输出 SHA 和 scope 见 原始安全汇总（历史记录已归档）。

| 验证 | 退出码 | 结果 |
| --- | --- | --- |
| restore | 0 | SDK/package 依赖恢复成功 |
| TemporalNative 目标测试与编译 | 0 | 56/56，无失败、跳过、中断 |
| 同一构建完整 TypeTests，no-build/no-restore | 0 | 218/218，无失败、跳过、中断 |

56 个目标测试包含一个消费 77 个逻辑向量及 10 个七位成功控制的 Fact，不能另增为 87 个 TRX 测试；218 是包含新增目标测试的完整类型套件，不能与 56 相加宣称唯一测试数。向量覆盖值域、单位、精度、偏移、legacy buffer、.NET 构造器前置错误；合成 fixture 仍标 expected_only/not_run，不把输入文件改写成运行证据。

冻结 manifest SHA `d75017fc838a61428e35c39447b1d67046317622ada643440298fa9ba8e72d0b`；10 个变更源/测试/项目文件汇总 SHA `bf1dfe2eb4425dd886097e9c025283d4f5dc9a165943582e0344971fbc3b4035`；fixture 四份一致 SHA `4eee2002106920423c75a2126ac557b96e51bae090ee8e1af2518a179b62a609`。测试实际执行 DLL 与 provider 输出身份见安全汇总，不冒称已完成 nupkg 或数据库反射加载验收。源首尾、旧快照和 1546 项基线 evidence 哈希无漂移。

可复核：source manifest（历史记录已归档）、targeted TRX（历史记录已归档）、完整 TypeTests TRX（历史记录已归档）。这些是复制后哈希一致的新证据，不覆盖任何历史文件。

## 失败与修复历史

- v1 未运行 .NET；[初审](TP02-astra-review.md)发现测试公开签名暴露 internal enum 及 fixture 外层 catch 可能将合法 format/scale7 控制失败误算通过。只修两个测试文件，[v2 复审](TP02-astra-rereview-v2.md)关闭两项。
- v2 实际 restore/编译成功，目标 52/56；四项新增手工 legacy golden 漏了 fraction 低位 0x80。[独立 triage](TP02-astra-triage-v2.md)用位置整数推导确认旧 00 字节表示123392微秒，正确的123456微秒应为80；产品代码和既有官方 golden 不变。v2 完整 TypeTests 没有运行，其失败输入/TRX 保留在 `.local/temporal-native/verification/TP02-v2/`。
- v3 只修单个测试文件的四条人工常量与注释，经 [Astra 复审](TP02-astra-rereview-v3.md)和本轮 Low 实际验证通过。既有官方黄金字节、Luna fixture 和五个核心文件没有因此修改。

Low 窗口已释放。未连接 DB、未打包、未完成 A/B/C 全门；TP-03 必须等待 TP-01 的 profile/独立 oracle 门关闭，再按 TP00 设计接入产品。未来 core 有定点修改时生成新 freeze 并重跑受影响门，不回写本次接受结果。
