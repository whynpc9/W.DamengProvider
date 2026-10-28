# 本机隔离达梦 TLS 测试实例

此实例用于真实 TLS 互操作测试，与共享 `172.16` 测试实例及 `.local/secrets/dameng-test.env` 完全隔离。流程不访问原管理配置、其他容器或其他数据库实例。

## 固定范围

- 容器名：`wdm-provider-tls-t07`。
- Docker 架构：`linux/amd64`。
- 仅将宿主机 `127.0.0.1:15236` 映射到容器 `5236`。
- 持久文件位于 Git 忽略的 `.local/t07/`。

官方介质 `dm8_20241022_x86_rh6_64_single.tar` 的 SHA-256 为 `97cc976b618e0eb75a20c831fb4e258c74ccc574ffa3e59b187c0c9bb90f019c`。

镜像信息：

- 标签：`dm8_single:dm8_20241022_rev244896_x86_rh6_64`。
- 架构：`linux/amd64`。
- 入口：`/opt/startup.sh`。
- 镜像内 `/opt/dmdbms/bin/server_ssl` 使用 `ca-cert.pem`、`server-cert.pem`、`server-key.pem`。
- 客户端用户目录使用 `ca-cert.pem`、`client-cert.pem`、`client-key.pem`。

镜像已由项目协调人加载。

## 初始化步骤

按顺序执行：

```sh
python3 tools/LocalTlsEnvironment/provision.py inspect
python3 tools/LocalTlsEnvironment/provision.py prepare
python3 tools/LocalTlsEnvironment/provision.py start-plaintext
python3 tools/LocalTlsEnvironment/provision.py create-user
python3 tools/LocalTlsEnvironment/provision.py generate-certs
python3 tools/LocalTlsEnvironment/provision.py enable-tls
```

`status` 和 `verify-tls` 可只读核对容器状态、配置和官方 `disql` 的 TLS TEST 登录。

每一步只作用于上述唯一容器，已有容器不会被覆盖。启动时写入包含精确容器 ID 和镜像 ID 的 `0600` 本地 manifest；后续操作会核对 ID、镜像、端口和三个 bind 路径。

首次容器是在加入 label 前启动的，已用首次启动返回 ID 执行 `adopt-created`，没有删除重建；后续新建容器会带专用 scope label。

### 凭据与证书

脚本为新实例生成随机 SYSDBA bootstrap 密码和 TEST 密码，只写入：

- `.local/secrets/dameng-tls-bootstrap.env`：仅供实例初始化工具使用。
- `.local/secrets/dameng-tls-test.env`：供 TLS TEST wrapper 使用。

文件权限为 `0600`，父目录为 `0700`。CLI 参数、标准输出和 Git 不包含密码、连接串、私钥或认证报文。Docker 与 disql 原始输出只留在进程内；写入忽略目录的阶段日志前会按秘密字面值和凭据行脱敏。

证书由本机 OpenSSL 新建，CA、服务端和客户端私钥只存忽略目录，权限为 `0600`。服务端证书 SAN 包含 `DNS:localhost` 和 `IP:127.0.0.1`，客户端证书 CN 为 `WDM_PROVIDER_TEST`。

先在明文模式创建 TEST，只授予 `CREATE SESSION`、`CREATE TABLE`、`CREATE INDEX`、`CREATE VIEW`、`CREATE SEQUENCE`、`CREATE PROCEDURE`、`CREATE TRIGGER`。之后只在此实例设置静态 `ENABLE_ENCRYPT=1` 和 `MIN_SSL_VERSION=771`（TLS 1.2），并只重启此容器。

官方文档说明 `ENABLE_ENCRYPT=1` 需要双方证书和完整传输加密；配置不完整时服务可能无法启动。参考[参数说明](https://eco.dameng.com/document/dm/zh-cn/pm/physical-storage)和[双因子与模式说明](https://eco.dameng.com/document/dm/zh-cn/pm/identification-authentication.html)。

## 运行 TLS 测试

TLS 探针必须通过 `scripts/with-dameng-tls-test.sh` 加载唯一 TEST 变量。每轮先验证 `WDM_PROVIDER_TEST` 用户和同名 Schema，只操作本轮唯一对象，在 `finally` 中清理，并由独立连接回读最终状态。

共享明文 TEST、官方驱动和新驱动的结果分别记录。证书测试和 loopback `SslStream` 只证明 TLS 组件；真实达梦互操作以此隔离实例的实际连接为准。

如果镜像许可过期、启动失败、证书格式不兼容或 TLS 模式不匹配，应保存脱敏诊断并如实报告。不能关闭验证或修改共享实例来绕过。

## 当前可用性注意

2026-09-29 UTC 的建立记录显示：明文初始化、TEST 登录、证书、`ENABLE_ENCRYPT=1` 和最低 TLS 1.2 均完成。官方 `disql` 使用显式 `SSL_PATH` 连接 TEST 并回读 USER 成功；新驱动随后完成 TLS 业务读写、证书拒绝和模式矩阵。

初始化时镜像日志报告的默认许可到期日为 **2026-10-13**，现已过期。重新验证前必须核对现有实例和有效许可；上述历史登录结果不代表实例当前可用，也不能绕过许可期限。

当时模式 5 配置实际返回协议模式 4，最终已恢复模式 1。详细结果属于已归档的 T07 历史报告。

## 受控模式矩阵

模式矩阵只能通过 `eng/t07.sh matrix` 操作 manifest 中已核 ID 的容器：

- 仅允许临时选择 `ENABLE_ENCRYPT` 1、2、4、5。
- 每次操作核对镜像 ID、端口和三个 bind。
- 每次重启等待官方 TEST 登录就绪后，才运行新驱动探针。
- `finally` 恢复模式 1，并验证官方和新驱动登录。

如果容器在失败模式下退出，恢复路径会在核对同一 ID 和 DATA bind 后，只修改该 `dm.ini` 的唯一 `ENABLE_ENCRYPT` 行，再重启同一容器；离线模拟测试覆盖此分支。

真实矩阵结果：

- 配置 2 → 协议模式 2：拒绝认证阶段 TLS。
- 配置 4 → 协议模式 4：新驱动 TLS 成功，并拒绝未知 CA。
- 配置 5 → 协议模式 **4**：新驱动 TLS 成功。
- 从未观察到协议模式 5，驱动对它保持明确拒绝。

历史失败矩阵保留，不能计入成功。
