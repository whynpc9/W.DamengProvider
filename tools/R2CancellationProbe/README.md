# 精确包取消与超时探针

这是历史 T14 验收入口，用于验证指定包的取消、超时、连接失效和数据库最终状态。普通发布不需要运行；复现时使用原精确版本和 feed。

项目为独立 `net10.0` PackageReference 消费者，不引用产品项目、官方驱动或可替换 DLL。每个候选都核对版本、包/net10 资产/加载 DLL SHA、MVID 和 deps 来源，runner 前后确认 nupkg 未变。

## 运行环境

由指定验证者执行：

```text
eng/t14.sh shared|tls <exact-version> <absolute-package-feed>
```

- 主验收为共享 `8.1.5.60`，显式 `PlaintextAllowed` / mode 0。
- 本机 TLS `8.1.4.6` 验证相同边界，使用 `RequireTls` / mode 1、既有 CA/客户端证书和本机无 CRL 的 `NoCheck`。
- 所有连接先核验 TEST 用户和同名 Schema；只读取对应 wrapper，不修改容器、服务器或系统信任。

每轮只创建 `T14_<随机24位>` 表，DDL 前先写包含精确表名的安全 checkpoint。若进程被上限终止，必须根据 checkpoint 用新 TEST 连接清理唯一对象，不能做全 Schema 清理。

## 必需场景

### 发送后取消与超时

一个事务锁住自己的行，另一物理连接的参数化 UPDATE 在数字 `AfterFrameSent` hook 确认 execution opcode 6/13 已发送后，分别通过用户 token、`Command.Cancel` 或客户端总期限终止。

三项都必须满足：连接 Broken、事务和操作 OutcomeUnknown、不可复用、首因和异常类型正确、只执行一次且不自动重连。释放锁后由新 TEST 连接确认未提交值不存在；不宣称服务器立即停止。

UPDATE 必须包含真实 Int32 参数 `:val`，确保进入参数化 prepared execution；仅 PrepareAsync 的 opcode 5 不能满足该发送条件。报告只记录固定阶段、连接数和数字 opcode，不记录 SQL 或参数值。

### 其他取消边界

- 预取消 Command/Commit：零发送，连接 Active/可复用，新 token 查询成功。
- 无活动操作时 Cancel：no-op。
- ExecuteReader 返回后取消旧 token：不影响新 Read token 的缓存行和实际 Fetch。
- idle Reader Cancel：异常类型和原因稳定。
- 显式重开后，旧 Cancel/Dispose 不影响新会话。
- Commit opcode 8 发送后、读取前取消：Unknown 优先且不重放，新连接按实际 0/1 记录。
- 已验证成功响应的 BeforeControlAck 后取消：仍 Committed，新连接为 1。
- 未知事务结果之后，独立 fixture reset 不重试事务。

### 8/16 等待连接观察（PER-01，仅 shared）

每个 waiter 等待不同的本轮行，全部 prepared UPDATE 已发送且 Task pending 后，开启 250 ms 采样窗口。窗口只用于观察，不单独证明非阻塞。

记录 ThreadPool.ThreadCount、GetAvailableThreads、托管分配、耗时、完成吞吐和执行次数。拒绝“每个 waiter 占一个忙 worker”及“8→16 增加 8 个忙 worker”的观测，但不设任意吞吐倍数，也不推广到所有负载。结束取消 waiter、释放 owner，并逐行确认最终为 0。

## 输出与期限

报告只含安全分类、CLR 类型、数字 opcode/count、受控指标和 FailureInfo 枚举/符号码，不包含 SQL、异常消息/堆栈、连接配置、环境变量或私钥。

所有异步等待使用公开命令期限和工具 30 秒等待上限；整个进程上限 1200 秒，包含多连接准备和独立回读，单次调用预算不变。缺连接或任何必需条件都不算通过，完整结论还需结合独立 barrier、虚拟时钟、首因单元测试和代码审查。
