# TP07 L0 v2 实库失败记录

2026-10-09。状态：失败已保留、测试对象已清理，未接受 SQL literal 七位持久化，未执行 TLS/E/D 后续实库门。

Python 合同 22/22、183 项执行源码与新 L0 依赖锁、SDK10.0.401 构建及实际 DLL 身份均已独立通过。shared 首项 `LIT-DT7-tick1` 用实际 EF mapping 生成 `TIMESTAMP '2026-07-23 14:15:16.0000001'`，向实际目录 scale7 的 TIMESTAMP 列插入一行。

新连接直接读取 V，没有 CAST：原始 payload `EA87BBEE8100000000`、FF9 `.000000000` 和 CLR ticks `639204129160000000` 一致，均比请求 `639204129160000001` 少一个 tick。主进程以 OracleMismatch 退出1。该次同表 typed7 配对控制尚未运行，不能用旧 TP06 参数结果代替定位控制。

主 finally 和独立 ledger cleanup 均确认本次唯一对象 fresh absent，独立 cleanup 退出0。只到达 shared 1/12 项；TLS 两项和完整 validator 未运行。没有改参数、连接策略、数据库配置或原断言后重试。

原始 JSON 的确定性 gzip 与原字节哈希见 失败证据 manifest（历史记录已归档）。独立验证 summary SHA256 为 `44472875a8dc7666899d961b6f9456dc67711a1b8a4f51627ae1a7ca7e77dd0d`，主报告为 `ed5310c8feccfb0705b85a4bda12333a3ffd60a5ae9ea353820e37486757290d`。

后续按 [有限表达式探测设计](TP07-L0-literal-loss-triage.md) 另建新对象，先验证同表同读法的 typed7 控制，再观察有官方语法依据的构造表达式。只读观察可能有关的配置，不修改参数或提权。该诊断即使完成也不等于修复成功，不能覆盖本失败或提前接受 C 门。
