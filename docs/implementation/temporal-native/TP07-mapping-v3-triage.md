# TP07 mapping-v3 模型拒绝后的Dispose失败

2026-10-09。结论：**test_ownership_fixture_fix_recommended / product_cleanup_regression_not_established**。此次终止异常来自EF10.0.12初始化失败后的owned-connection诊断清理路径，发生在调用W连接Dispose之前；没有证据要求修改W或时间mapping产品。不得吞掉ObjectDisposedException、删真实model断言或跳过清理。

只读核对 `TP07-M1-v3-offline/unit/tp07-new/tp07-new.trx` SHA `86e6ceae2d6a2f9852fc50557731f87e8875e8cfcbda920c3f15e91952e50548`；51项为50pass/1fail，失败唯一为`TimeSpanTimeAlsoRejectsDuringRealModelConstruction`。execution summary SHA `2ddb1d7e67a9e662fbac801b9ecfe4b61933362fe613098c37527388caf188ca`，SDK/locked restore/build/discovery退出0，目标测试退出1。其它回归、observer、DB未运行。本轮未重跑任何命令。

## 失败链

测试第157行把自己new的未打开DmConnection交给 `UseDameng(..., contextOwnsConnection:true)`，第158行预期模型构建NotSupported，第159行using清理。TRX终止堆栈为 `DbContext.Dispose → ServiceProviderEngineScope.Dispose → RelationalConnection.ResetState → DisposeDbConnection → ConnectionDisposing → Interceptors → 已释放IServiceProvider.GetService`，没有W的Dispose/Close帧。

固定版本EF源码与此一致：DbContext.Dispose释放service scope；RelationalConnection仅在拥有非null连接时进入连接清理，先调用ConnectionDisposing拦截/诊断，然后才调用DbConnection.Dispose。Interceptors对CoreOptionsExtension延迟从service provider取IDbContextOptions，失败初始化路径可能直到scope清理时才首次触发该读取。[EF10.0.12 DbContext](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore/DbContext.cs)、[RelationalConnection](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore.Relational/Storage/RelationalConnection.cs)、[Interceptors](https://raw.githubusercontent.com/dotnet/efcore/v10.0.12/src/EFCore/Diagnostics/Internal/Interceptors.cs)。

这是对实际stack和固定源码的归因，不宣称已复现上游所有provider或已修复EF本身。`contextOwnsConnection:true`本身是合法API；本测试只需要验证模型拒绝，无需同时承担失败初始化的owned-connection诊断边界。终止Dispose异常也可能遮蔽test body原异常，因此当前TRX不能单独证明第158行NotSupported断言已成功；修后必须由Low真正验证。

## 最小修复

只修改该测试的连接所有权：独立`using var connection = new DmConnection()`，传入`contextOwnsConnection:false`。保持实际DurationAsTimeContext及原 `Assert.Throws<NotSupportedException>(() => _ = context.Model)`，用内层using正常Dispose context，再由外层明确拥有的connection Dispose。外层using保证断言失败时也清理W连接，不借清理异常掩盖原失败。

建议利用连接继承的Disposed事件做局部计数正控制：context scope退出后计数仍0，显式connection.Dispose后为1；外层using提供异常安全，W的幂等Dispose避免重复释放。此方式没有mock产品Dispose，没有捕获忽略异常，也不访问数据库。不要为了“预热”interceptors在断言外触发一次model初始化，不修改其它正常测试的公共Context factory，不改RelationalConnection产品或屏蔽EF诊断。

新freeze后Low重跑51项新测试；只有真实model拒绝断言、context正常清理和显式连接清理都完成才关闭本项，再继续原定回归。原v3源码/TRX/summary保持，记录该EF失败初始化清理边界；新fixture通过不代表原context-owned失败路径已被产品修复。依赖与锁若精确不变，可沿已批准身份链复用，不需要另一次force-evaluate。

报告完成后停止写入。
