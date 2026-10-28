# 精确包异步 API 与网络行为探针

这是历史 T13 验收入口，用于检查指定 NuGet 包的异步公开 API 是否真正走异步网络，而不是继承 ADO.NET 的同步回退。普通发布不需要运行；复现时必须使用原精确包和 manifest。

项目只有精确 PackageReference，没有产品 ProjectReference、官方驱动引用或 DLL HintPath。构建必须传 `R2PackageVersion` 和本地 `R2PackageSource`。运行前核对 nupkg、加载 DLL SHA、程序集版本/MVID 和 deps 中的 package 来源。

## 运行方式

由独立验证者通过 [eng/t13.sh](../../eng/t13.sh) 执行。输入为冻结 nupkg，输出写入新的忽略目录，消费者的 bin/obj/cache 相互独立。runner 不打包产品，也不运行其他测试。

- `offline`：不加载连接配置。
- `shared`：只通过 TEST wrapper，显式 `PlaintextAllowed`，验证 STARTUP mode 0。
- `tls`：只通过本机 TLS wrapper，锁定 `127.0.0.1:15236`、`RequireTls`、既有 CA/客户端证书和本机无 CRL 的显式 `NoCheck`，验证 mode 1。

不得关闭证书校验、读取 DEV/admin/bootstrap 配置或修改服务器设置。

```text
R2AsyncProbe.dll offline|shared|tls <repo-root> <package-manifest.json> <safe-report.json>
```

直接调用时也必须遵守仓库构建环境、唯一实库窗口，并确保 manifest 来自同一个包。

## 必需覆盖

所有真实路径均使用 await：Open、Prepare、ExecuteNonQuery/Scalar/Reader、Read、GetFieldValue/IsDBNull、NextResult、Commit/Rollback/Save/Release、Close、Dispose。

- 2048 行 × 1024 字符使用默认 BufPrefetch，ReadAsync 阶段必须观察实际 Fetch opcode 7；如果默认缓存覆盖全部行，应扩大数据规模，不能弱化断言。
- 双 SELECT 必须观察 MORE_RESULT opcode 44。
- 256 KiB BLOB 和 163840 UTF-16 单元 CLOB 使用异步上传和物化，必须观察 GET_LOB_DATA opcode 32；此处不证明 R3 流式 LOB。

每轮只创建 `T13_<随机24位>` 表。提交、回滚、保存点和 Dispose 回滚由新 TEST 连接核验；finally 精确 DROP，并由另一新连接确认不存在。异常只报告 CLR 类型、固定分类和 Number，不输出消息、堆栈、SQL、连接配置或认证数据。

### 保存点按服务器环境判断

- `8.1.5.60`：Save/Rollback-to/Release 必须支持；独立连接验证保留 1 行、回滚后 0 行。
- 独立 TLS `8.1.4.6`：必须报告 `unsupported_for_server_profile`；三个异步保存点入口均精确抛 `NotSupportedException`、没有新增帧、事务 Active 且连接 Open，随后整事务回滚并确认数据为 0。
- 其他环境拒绝验收。

两条路径都完成全部 13 项 case。`package_async_verified` 只表示该环境的异步行为通过；保存点是否支持以 `savepoint_status` 为准，明确拒绝不算成功或 skip。

## 安全负向模式

`security-shared` 验证 `RequireTls` 遇到 mode 0 时在 LOGIN 前拒绝；`security-tls` 验证 TLS mode 1 遇到无关 CA 时认证失败并关闭。

每个负向前后都用正确策略和有效证书建立新 TEST 连接。正控制必须记录 STARTUP 200 与 LOGIN 1；负向只能有 200，LOGIN 编码为 0。还要求单 socket 创建/释放、TLS 成功升级 0、TLS 失败计数 1。安全模式不创建对象、不改变实例，也不重跑正向 13 项。

无关 CA 的 RSA 私钥只存内存，公开证书文件为 0600、父目录 0700，finally 删除；原客户端私钥不复制、不导出、不写系统信任库。

完整 T13 还需要独立的 async-only fake transport barrier 测试和调用链审查；实库结果本身不能证明所有等待都不占同步 worker。
