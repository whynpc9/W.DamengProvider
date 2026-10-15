# T1 transaction capability：产品源 v2 单位文件修订

v2 从冻结批准的 v1 全部178项逐文件核hash后复制到新隔离 `TP07-T1-transaction-adapter-v2/ef-source`，仅修 `DamengRelationalTransactionCapabilityTests.cs`。其它177项保持，包括正式adapter/factory/DI、mapping、bridge、两locks和全部编译规则；W产品及候选包不改。

本次修复针对首次实际build的8个compile/analyzer错误：`Events` 全部改为 `OperationEvents`；单batch期待序列提取为private static readonly数组；五处必要的正式DI具体类型 `Assert.IsType` 各自最小局部EF1001 pragma，立即restore并说明EF10.0.12验证意图；删除冗余 `TestTransaction.DisposeAsync`，继承DbTransaction默认异步dispose，继续保留一次性Dispose(bool)计数和base调用。没有全局抑制、弱字符串类型替代或双dispose。

原4方法/25场及InlineData顺序不变：实际DI sync/async Begin/UseTransaction、true/false能力、owned/unowned生命周期、同底层transaction、singleton隔离、真实IBatchExecutor的create→batch→release与失败rollback-to→release、unsupported仍batch执行及full rollback均保留。默认异步dispose与这些场景仍须实际单位验证；本修订不声称编译或测试已通过。

`transaction-capability.v2.patch` 仍相对原批准175源生成，包含相同DI/两个产品类型及修订unit。manifest.v2固定完整178 map、新aggregate/patch identity，并记录v1 manifest/patch/sourcefreeze/旧source-only批准与实际build triage父refs。v1原manifest `932f8f41…`、patch `1752dc64…`、source approval `ac8ff44f…`只作历史父证据，不能作为v2源批准、compiled identity或新单位接受；旧失败exit1/8 errors及snapshot永远保留。

作者仅静态核复制/差异/hash、patch适用性、源码声明/pragma范围及清单关系，未执行SDK、build、tests、validator、DB、wrapper或secret。停止写入后依次等待独立审查者 v2源审查、验证者独立完整178/patch snapshot、root新七字段source-only批准，再以独立pin-only准备revision重新source→实际native→locked build→固定target25 TRX→同build full units。旧cache/locks只提供固定比较基线，不能代替新源实际restore/build证据；SDK10.0.401与原执行界限不变。新的准备脚本、probe/运行输入与事务服务器门另行授权，不在本修订中实施。
