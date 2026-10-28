# 精确包连接容量与生命周期探针

这是历史 T16 验收入口，检查指定包的 DataSource、直接池化、连接销毁和异步网络行为。普通发布不需要运行；复现时使用原精确包。

项目只通过 PackageReference 消费冻结 nupkg，不引用产品项目或官方驱动。版本、包/DLL SHA256、实际加载位置、MVID 和 deps 来源必须一致。

## 运行方式

```text
eng/t16.sh offline|tls|shared <exact-version> <absolute-local-feed>
```

runner 保存安全命令、退出码、前后包 manifest、消费者构建、checkpoint 和独立 validator 结果。运行上限 900 秒；超时必须按 checkpoint 精确恢复本轮对象，不能算通过。

`offline` 检查 DataSource 公共 API、Closed 构造、安全连接串，以及显式证书文件的 direct pooling 在 socket 前拒绝。调度器详细行为由 PoolTests 验证，不用伪握手冒充实库。

实库使用 TEST wrapper，新连接读取 USER 和当前 Schema，固定：

`Pooling=true,MaxPoolSize=1,MaxPoolWaiters=4,PoolAcquireTimeout=5000ms`

Connect/Command 分别 15 秒。TLS 使用本机隔离实例、显式证书和独立 DataSource；shared 分别验证 DataSource 与 direct registry。

## 必需场景

- 等待取消和到期时不创建 socket。
- Clear 保留活动容量域。
- 归还时物理关闭，下一次重新认证。
- 重复关闭安全。
- DataSource Dispose 结束等待者，但允许已交付业务租用完成。
- 快捷 nonreader 的成功、服务器失败和 Reader 持有连接。
- 活动事务关闭后，新连接看不到未提交行。
- 旧 Reader/Transaction/Cancel 不产生新 I/O，新会话可取得原锁。

快捷隐式连接通过 SQL 身份条件和结果验证，不插入额外握手查询。只创建 `T16_T_<20 hex>` 表，finally 精确清理并由新 TEST 连接确认不存在。

活动事务另有正控制：同事务写入 `COUNT=1`，关闭前独立 DataSource observer `COUNT=0`，关闭后新连接仍为 0，再验证原锁释放。

实际 connect/tls/send/receive 通过包内只读 hook 统计有限标签。异步实库必须同步入口为 0，connect/send/receive 异步计数大于 0；TLS 还要求异步 TLS 大于 0。缺 hook 直接失败，不能跳过或用 Task 返回类型冒充异步网络。

## 通过条件与输出

输出只包含受控错误类别、枚举、服务器编号、TEST 身份、数字版本、物理/异步入口计数和最终快照，不包含异常文本、SQL、连接配置或认证内容。

成功要求：实际认证次数=创建 socket=关闭 socket，每种模式准确，所有 owner 的 creating/leased/closing/waiting/idle/resetting 归零。所有租用归还即销毁，`reuse=0`；不得据此宣称完整 reset 或握手性能提升。shared/TLS 分别出报告，offline 仍为 `integration_pending`。
