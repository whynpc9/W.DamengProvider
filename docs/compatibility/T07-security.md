# TLS 配置与原生库边界

本文说明现代目标框架的 TLS 行为。`netstandard2.1` 不支持 TLS，详见[旧目标框架说明](core31.md)。

## 证书配置

| 强类型 Builder 属性 | 连接串键 | 规则 |
| --- | --- | --- |
| `TlsCaCertificatePath` | `tls_ca_certificate_path` | 可选的 CA 绝对路径；不指定时使用系统信任。自定义 CA 必须有 CA 约束；存在 KeyUsage 时必须允许签发证书。 |
| `TlsClientCertificatePath` | `tls_client_certificate_path` | PFX 或 PEM 客户端证书的绝对路径。 |
| `TlsClientPrivateKeyPath` | `tls_client_private_key_path` | PEM 私钥绝对路径，必须与证书配对；省略时证书文件按 PFX 读取。 |
| `TlsClientCertificatePassword` | `tls_client_certificate_password` | 可选的私钥/PFX 密码，无默认值；不会写入脱敏连接串或错误消息。 |
| `TlsRevocationMode` | `tls_revocation_mode` | 默认 `Online`；只有显式 `NoCheck` 才关闭吊销检查。证书链、用途、有效期和主机名检查不受影响。 |

同名但不带下划线的属性名也按 Builder 别名规则处理。证书文件在 `Open` 时加载，不在构造配置时访问。

旧 `SslFilesPath` / `SslKeyPass` 未恢复；驱动不会按当前目录、用户名或服务端路径搜索证书。

## 连接协商与传输安全

`RequireTls` 会读取 STARTUP，再根据已验证的协议模式升级同一 TCP 连接：

- 未协商 TLS 和 `AUTH_ONLY` 会在 LOGIN 前拒绝。
- `PlaintextAllowed` 允许明文模式 0；如果服务端选择 TLS，仍会严格验证证书。
- 协议模式 1/4 支持完整 TLS；2/3/5/6 和未知值拒绝。
- 在已测 DM 8.1.4.6 中，服务器配置 5 实际返回协议模式 4；不能据此推断其他版本支持模式 5。

业务 I/O 全程使用已验证的 TLS 通道，最低 TLS 1.2。驱动不会重新拨号、设置恒真证书回调，或在认证失败后回退明文。认证和后续初始化共用原连接期限。

## 证书文件与平台差异

macOS 的 PFX 私钥不能使用 `EphemeralKeySet` 导入。本驱动使用 .NET 的临时 keychain 机制，不指定 `PersistKeySet` 或 `MachineKeySet`：

- 使用期间会写入磁盘，最后一个引用释放时由运行时清理。
- 进程异常退出后的清理由运行时和操作系统负责。
- 其他平台使用 `EphemeralKeySet`。
- 输入的 PFX/PEM 文件仍由应用管理。

详见[微软跨平台加密说明](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography#read-a-pkcs12pfx)。

本机测试 CA 没有 CRL，因此对应实库测试显式使用 `NoCheck`；这不是产品默认值，也不代表 `Online` 吊销网络流程已完成实库验证。证书链检索期限受剩余连接预算限制，平台内部检索能否取消仍取决于 .NET 和操作系统。

## 原生库

`dmcyt`、`dmfldr`、`dmcalc` 和第三方加密库的旧直接导入入口保持 `NotSupported`。产品不打包这些二进制，也不提供原生加密、FLDR 或计算能力。

内部预备加载器使用 `NativeLibrary` / `SafeHandle`，只接受受信任的规范绝对路径并检查必需导出；原生回调执行期间持有 `SafeHandle`，避免并发释放导致提前卸载。加载和导出错误不会包含认证材料或原始路径异常。加载器自身的测试不代表上述原生功能已经开放。
