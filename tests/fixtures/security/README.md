# TLS 与原生资源测试场景

`scenarios.json` 提供 TLS-01 至 TLS-03、NET-04 清理和 NAT-01 的合成输入与期望。所有用例保持 `not_run`；期望成功不代表已经执行握手。

## 服务器配置与协议模式分开

- `server_enable_encrypt` 是受控服务器配置值。
- `negotiated_wire_encrypt` 是客户端实际分派的 STARTUP `Encrypt` 整数。

`mode_catalogue.server_configuration_modes` 记录文档中的服务器配置；`recovered_wire_mode_constants` 记录恢复源码标签和当前 W 保护策略。原 STARTUP 解码将 Encrypt 存入 `ConnProperty.Encrypt`，见[协议安全记录](../../../docs/implementation/T07-protocol-security.md#L7)。

受控 DM 8.1.4.6 镜像实测到配置到协议映射 1→1、2→2、4→4、5→4；配置 5 的 TLS smoke 走的是协议 4 分支。该结果只属于当次环境，保存在 `observed_local_configuration_to_wire_mapping`，不改变其他用例的 `not_run` 状态。详见[协议记录](../../../docs/implementation/T07-protocol-security.md#L11)。

配置 5 的向量因此使用 `server_enable_encrypt: 5, negotiated_wire_encrypt: 4`；没有接受协议 5 的向量。`tls_wire5_unsupported` 验证协议 5 在 TLS 或 LOGIN 前明确拒绝；3、6 和未知模式也不支持。实际分派见[DmHandshakeSecurityGuard](../../../src/W.DamengProvider/Internal/Legacy/A/DmHandshakeSecurityGuard.cs#L20)。

## 证书场景

证书字段用于测试宿主生成临时材料：受信或未知 CA、DNS/IP SAN、EKU 和有效期偏移。fixture 不保存证书、私钥、密码、PFX 或真实路径。保留主机名为 `db.example.test`，文档 IP 为 `192.0.2.44`。

正向用例要求严格证书链和目标主机验证；负向分别覆盖 SAN 不匹配、未知 CA、过期、双向认证缺少客户端证书，以及 Online 下的已吊销状态。

`NoCheck` 只出现在显式配置用例中，仅关闭吊销检查，链和主机验证仍开启；默认是 `Online`。`PlaintextAllowed` 可允许 mode 0，但服务器选择完整 TLS 时仍拒绝无效证书。`RequireTls` 在 LOGIN 前拒绝 wire 0 和 AUTH_ONLY wire 2，TLS 失败不回退明文。

`expected.login_expected` 是布尔预期，不是字节数或已观察 LOGIN。

## 原生资源场景

覆盖不支持的 RID、相对路径、缺失库、缺失导出、初始化失败和幂等释放。`/fixture/` 是测试宿主控制的占位路径，可在隔离测试中创建和删除假库；fixture 不含二进制。缺失导出和初始化失败要求一次获取对应一次释放；查找失败不应产生句柄。

这些向量不证明达梦模式行为或平台原生互操作。T07 验收仍需单独记录命令、退出码、环境和证据级别。
