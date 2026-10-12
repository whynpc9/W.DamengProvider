# TP07 T1 限定验收

2026-10-11，root 回读并接受 [Astra 最终报告](TP07-T1-probe-v10r3-final-astra-review.md)（SHA256 `6c6f208858998d40266313c8f11eacd4227999cd3abf729b7a854d7445196054`）。T1 已完成；整体 TP07、full EF C、TP08 和交付仍待后续任务。

已接受的产品 v3 为178文件，包含正式DI注册的事务工厂/适配器及W异常重试合同测试；此前完整824项离线验收见 [产品报告](TP07-T1-product-v3-offline-acceptance.md)。本轮产品与W包未改变，完整824项不重复执行。

探针 v10r3 完整76项Python合同实际通过，构建和完整native合同通过；28项离线宿主合同实际通过，共337断言，完整offline/readiness合同均绑定真实引用。四个实库场景按 shared sync/async、TLS sync/async 的固定顺序同一批准cohort执行，12个主进程/独立cleanup/validator均自然退出0。shared观测保存点正控，TLS零保存点；实际业务修改、显式完整回滚、fresh DateTime/DateTimeOffset/offset/raw/FF9原值读回及TEST身份一致。四场最终owned对象数均0。

root独立核对187回放源、26二进制、native/locked完整JSON仅五处strict true插入、四场receipt/validation真实引用及108项最终制品哈希。21037保护文件在窗口前后不变。原十字段child receipt无单独PID/PGID，外部闭合证据限定为真实outer进程组及运行进程不存在。

最终批准为 `995f01e1ecab40a8acd0a0ba8eb678d2c41b6c09bafb924f66f6dc432c2dd687`，矩阵为 `9835493c7e7497c705b938d52e89574159bd471d2a659e78f20963dbf66b3bd3`，独立读回为 `e9d035dcffcea182b9bb034f55ac7d3613f8e7a438a47e2a3169f674c813b5ab`。证据位于 ignored `.local/temporal-native/verification/TP07-T1-probe-v10r3-*`。

此前全部失败保持原样，包括v8事务查询未绑定、v9 CREATE返回-1误拒、v10新增正例计数和v10r2浮点负例错误预期；不回写历史通过状态。本轮Low消费者对矩阵返回字段的错误假设也单独保全，修订仅作用于消费者且无实库重跑。原runtime制品中的candidate/fullEF/activation标志保持false，其语义不等于本限定T1验收。

下一依赖为当前178文件实现的NEW L0：首先独立验证旧175来源，再重放签定五路径事务补丁并核对178闭包，随后生成全新source/lock/build/runtime证据；不能复用旧175启动清单。之后为NEW E完整338项同轮证据及NEW D，TP08完成后按用户顺序启动新的Claude本地静态审查会话，修订一轮并验证，再创建PR、处理review bots和merge。未发布。
