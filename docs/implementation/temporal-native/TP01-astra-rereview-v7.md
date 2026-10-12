# TP-01 v7 单文件静态复审

日期：2026-10-08。结论：`static_accepted_for_complete_offline_rerun / runtime_pending / database_not_authorized`。仅 `tools/TemporalBoundaryProbe/test_validator_contracts.py` 相对TP01-v6冻结源发生变化，SHA256实读为 `0a94288cb63887ac0464c42d75408821e6ca2d9d0d0fe119e3a2d4372b2a0c4e`；其余12项输入逐字节SHA一致。未运行测试、构建、contracts CLI或数据库，未改实现或旧报告。

v6 Low安全摘要实读SHA为 `91902ae91edeebc7ac9395abbf52cb40ed20c1320e6f46074ce99737c96c78f2`：portable57 tests、0 failures、7 subtest errors、0 skip，退出1。七个subtest均未找到旧ID `bind_DateTime_sunset_punset_fixed_value`，在mutation前StopIteration；这些不是七个validator拒绝断言已成功。旧失败保留，不回写。

新ID `bind_DateTime_s7_punset_fixed_value` 确实由独立手写axes中的 `(7, None, True, False)` 生成，contracts.binding_specs也包含该格。`a=self.fresh()`为shared candidate，`a.case`默认W；此格指向D7，explicit DateTime与fixed26同族兼容，输入1,234,560 fractional ticks可被scale7精确表示、Precision未设，故按冻结规则应走成功initial phase。未设scale、未设Precision、fixed非NULL的旧组合未在本工具轴中生成，不能仅替换名称而不核目录。

修复在每次mutation前增加 `self.accepted(a)`，并在取到initial phase后断言status=observed。基准若整体不可接受或目标phase失败，会直接使测试失败，不会靠后续mutant拒绝掩盖。没有新增catch来吞StopIteration，也没有修改producer/validator规则。

七个原mutant和值全部保留：

- after owner改command。
- execution_api改ExecuteNonQuery。
- snapshot_timing改after_dispose。
- drain_completed改false。
- unexpected_rowset改true。
- records_affected改0。
- encoded_scale_observable改false。

每个mutant仍改实际phase、重写report/hash链并调用顶层validator断言拒绝；成功控制因此增强，拒绝门没有削弱。v6已静态闭合的NULL/fixed-observer实现不重开，也不据本次修正宣称其运行通过。

同意root新freeze后交Low完整重验全部离线：portable→local-required→真实source/package CLI→`--offline-only` build/C#策略→实际C#shape的Python消费。不能只跑七个subtest或继承v6未报错路径；首失败保存释放。DB仍需root另批。本文完成后停止写入。
