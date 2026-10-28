# 路线图与支持缺口

当前版本为 [0.3.1](release-0.3.1.md)。本页分别列出功能限制和尚未完成的验证，避免把“暂不支持”误解为“已经支持但还没测试”。

## 当前功能限制

| 功能 | 当前行为与原因 |
| --- | --- |
| 物理连接复用（[#6](https://github.com/whynpc9/W.DamengProvider/issues/6)） | 每次租用都会新建物理会话，归还时销毁。DM 8.1.5.60 上没有已验证的完整会话重置方式，序列 `CURRVAL` 等状态无法可靠恢复。详见[连接复用调查](compatibility/I06-pool-reuse-baseline.md)。 |
| 存储过程与显式事务（[#9](https://github.com/whynpc9/W.DamengProvider/issues/9)） | 组合使用会在执行前拒绝。过程内部可能自行提交，当前协议无法可靠反映事务状态。 |
| 游标输入与过程批调用 | 目前明确拒绝；存储过程已支持的参数和输出能力见[存储过程说明](compatibility/I09-stored-procedures.md)。 |
| `netstandard2.1` | 不支持 TLS、`DbBatch`、DateOnly/TimeOnly 映射。完整差异见[旧目标框架说明](compatibility/core31.md)。 |
| 其他企业功能 | 原生加密、批量装载、自动重连和分布式事务不在当前支持范围内。 |

## 尚未完成的验证

| 场景 | 已有结果与待补内容 |
| --- | --- |
| 包内过程的参数推导 | 0.3.1 的独立过程和函数已通过实库验证；包内过程的 7 项测试因 TEST 账号缺少 `CREATE PACKAGE` 权限而仍为 `integration_pending`。详见[发布记录](release-0.3.1.md)。 |
| `ReservedWords` 元数据（[#10](https://github.com/whynpc9/W.DamengProvider/issues/10)） | 系统目录访问受 TEST 权限限制，尚未完成验证；不会仅为测试扩大授权。 |
| Linux 实库（[#11](https://github.com/whynpc9/W.DamengProvider/issues/11)） | Ubuntu WSL2 / linux-x64 已完成离线和包消费验证，实库验证仍待补。 |
| 更多操作系统和服务器环境 | 已有 Windows / win-x64 / DM 8.1.5.60 明文验证；更多服务器版本、远程 TLS、系统信任库以及真实 CRL/OCSP 场景仍需逐项验证。 |
| 长期资源压力与完整 EF 矩阵 | 当前发布检查不覆盖这些场景，需要按实际使用环境单独验证。 |

`integration_pending` 表示对应实库测试尚未完成，不表示测试失败，也不表示功能已经通过。

## 后续工作方向

后续优先级由真实使用需求和可验证的服务器能力决定：

- 在服务器提供完整、可验证的会话重置能力后，再评估物理连接复用。
- 根据业务需求评估存储过程扩展，但不绕过事务和游标的现有限制。
- 补充不同操作系统、达梦版本、编码和 TLS 配置下的验证。
- 继续改进与下游 EF 适配层的兼容性；EF 模型、SQL 翻译和迁移仍由独立项目负责。

## 当前版本已具备的主要能力

- .NET 6/8/9/10，以及受限的 `netstandard2.1` 支持。
- [数组绑定与 DbBatch](compatibility/I07-I08-array-binding-dbbatch.md)。
- [存储过程、输出参数、函数返回值与输出游标](compatibility/I09-stored-procedures.md)。
- [异步元数据与 SELECT SchemaOnly/KeyInfo](compatibility/I10-metadata.md)。
- [时间精度、失败分类和会话恢复改进](compatibility/I16-ef-integration-feedback.md)。
- [Windows TLS 私钥处理](compatibility/I11-windows.md)。

当前能力的完整边界见[文档导航](implementation/README.md)，开发与实库测试规则见 [AGENTS.md](../AGENTS.md)。
