# 本仓库 agent 规则

## .NET 构建

- 运行 `dotnet` 前设置可写的任务目录 `DOTNET_CLI_HOME`，并设置 `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。
- 构建和测试使用单节点 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`；SDK 支持时添加 `--disable-build-servers`（.NET 6 不支持）。
- 如本地 IPC 报 `SocketException (13): Permission denied`，先关闭 build servers，再用上述参数重试。不要例行清空全局 NuGet 缓存。

## 本地达梦开发实例

- 本项目在现有达梦实例中使用两个专用账号及同名 Schema：`WDM_PROVIDER_TEST` 用于自动化集成测试，`WDM_PROVIDER_DEV` 用于手工开发探测。默认只使用 `WDM_PROVIDER_TEST`。
- 2026-09-30 经用户单独批准，TEST 另有 `SOI` 系统目录只读角色供 EF 迁移使用，`ADMIN_OPTION=N` 已由 TEST 新连接核实；DEV 未变。这不授权普通测试使用 SA，也不授予 DBA/ANY/其他业务 Schema 权限。限定维护见 [T12 目录访问](docs/implementation/maintenance/T12-test-catalog-access.md)。
- 本机连接信息只在被 Git 忽略的 `.local/secrets/dameng-test.env`、`dameng-dev.env`、`dameng-admin.env` 中。不要将连接串、口令、原始认证报文、含凭据的异常或环境变量输出到终端、日志、测试制品、Git 或对话。不要把本地 secrets 复制到 CI。
- 测试命令通过 `scripts/with-dameng-test.sh <command> ...` 加载唯一测试变量 `DAMENG_TEST_CONNECTION_STRING`。手工探测才显式加载 dev secret。正常开发、测试和 agent 任务不得加载 SA 管理凭据；新增用户或变更授权另行明确限定操作。
- 只对当前专用 Schema 内本次测试创建的对象进行清理。不得用 SA 运行测试，不得执行跨 Schema 的 DML/DDL、`DROP USER`、`DROP SCHEMA`、`DROP TABLESPACE` 或实例级设置变更。自动化测试使用唯一对象名并在失败时清理自己的对象；不要并行运行会互相删除固定对象名的测试。
- 每次真实库测试先确认连接身份为 `WDM_PROVIDER_TEST`，并记录测试命令、退出码、服务器侧最终状态。无显式测试连接时只跑离线测试，并报告 `integration_pending`。官方驱动、可编译恢复版和新驱动的测试结果分开记录。
- 新驱动的 TLS/明文模式必须显式配置并验证；不能通过关闭证书校验让测试通过。当前连接凭据的使用方式见 [本地达梦环境](docs/implementation/local-dameng.md)。
- T07 已接通严格 TLS；默认 `RequireTls` 对不满足策略的 STARTUP 模式在 LOGIN 前拒绝。原共享明文 TEST 的回归仍通过 typed W Builder 显式选择 `PlaintextAllowed`，不要把 W 专用键写进 O/R 共用 secret。
- T07 新增隔离本机实例 `wdm-provider-tls-t07`（仅 `127.0.0.1:15236`）；普通 TLS 测试使用 `scripts/with-dameng-tls-test.sh` 和 `.local/secrets/dameng-tls-test.env`，仍先验证 `WDM_PROVIDER_TEST` 身份。无 CRL 的本机 CA 测试显式 NoCheck；产品默认 Online 不变。详情见 [本机 TLS 环境](docs/implementation/local-dameng-tls.md)。
- 新增 `dameng-tls-bootstrap.env` 只供本机实例初始化工具使用，不供普通测试加载；不得替换或读取原 SA secret。模式矩阵只操作 manifest 精确锁定的本机容器 ID、镜像和 bind，结束必须恢复 mode 1 并实际登录确认；禁止据此变更共享远程实例。
- 证书/私钥和新 secret 只存 ignored 本地目录，私钥/secret 为 0600，父目录为 0700；日志不得输出凭据或密钥。macOS PFX 使用 .NET 临时磁盘 keychain，不宣称完全不落盘；不写入系统信任库。

## 实施分工与范围控制

- 主 agent 负责设计、范围控制和 code review；使用 **GPT-6.1 Sol High** 负责产品及测试代码，**GPT-6.1 Sol Low**（对应用户的 light 档）执行独立构建和测试验证；复杂或重要的设计及代码由 **GPT-6 Astra High** 验收，Luna Max 负责需要的合成测试数据。当前文件归属和窗口协调见 [active-ownership.md](docs/implementation/active-ownership.md)。
- 子代理任务需明确产品目标、改动边界和验收行为。共享工作区避免交叉修改，保留其他未提交工作；共享 bin/obj 的构建和会相互干扰的实库测试串行安排。产品及测试代码修复由编码 agent 完成，再由验证 agent 复核。
- 主 agent 负责及时 steer 或停止偏离产品目标的子任务。子代理发现工具或流程问题时，应说明原因及更直接的验证方案，不自行扩大任务或把实现选择变成新的验收要求。主 agent 可以调整执行命令、复用已有入口或更换验证方法，无需为普通验证操作建立额外审批层。

## 验证与任务范围

- 验证以本次需求的可观察行为为目标。先明确关键输入、期望输出、拒绝条件和数据库最终状态；执行方法可调整，覆盖要求不得静默降低。
- 优先复用项目现有测试、构建工具和参考实现。先运行受影响测试，再做必要回归；验收前完成项目要求的相关检查。参考实现用于对照，有缺陷的旧行为不能覆盖明确的功能规格。
- 使用最少且充分的证据：测试对应的源码或候选身份、执行命令、实际结果及未覆盖范围。默认不构建递归哈希、审批或冻结体系；确认实际受测版本即可，不重复认证无关历史证据、SDK 文件或依赖缓存。
- 未提交工作用明确 diff 标识；测试期间避免修改受测源码。修改后通知验证者并重跑受影响验证，不使无关历史结果失效。保留原失败记录，不需要为每个小修复制和冻结整套工作区。
- 测试失败先区分产品、测试、工具、环境和基线问题。新出现或原因未确定的失败必须处理；有对照证据的既有问题单独记录，不无限扩大本次任务范围。不存在候选独有的失败不能单独代替相关功能的正向验证。
- 验证工具只是手段。工具连续阻塞时，主 agent 应重新选择更直接的路径，不继续默认派生恢复工具、测试夹具或验证框架。与本次产品行为无关的工具缺陷可以记录后另行处理，不自动成为产品验收前置条件。
- 新增门槛应有具体风险、真实接口要求或已观察到的环境问题作为依据，并优先选择成本更低的方法。不得仅因工具实现方便、审阅建议或历史做法，推导出新的全局强制性执行约束。
- 普通路径表达、文件数量、JSON 字段形状、日志格式和辅助工具版本不作为产品验收条件。仓库相对路径与明确工作目录可以使用，不要求所有引用为绝对路径；需要时确认实际执行文件或加载的程序集即可。
- 修正工具参数或环境问题后可以正常重跑验证，记录原因和实际结果，不要求所有命令只能执行一次。涉及数据库副作用时，先确认上次执行状态和对象归属；不得通过盲目重试、放宽断言或无限延长超时求通过。
- 保留可定位问题的日志与异常，针对凭据限制输出或脱敏，不一律丢弃 stderr 和异常信息。实库验证继续遵守上面的授权身份与精确对象清理边界；仅凭名称前缀或创建时间不能在并行环境中证明对象属于本次测试。
- 保留独立代码审查和测试验证。独立性通过检查实现及实际行为体现，不以重复核验相同元数据替代；辅助脚本的小修只做必要的受影响检查，不默认重跑不相关历史矩阵或生成新的产品候选。
- 历史计划、交接和测试方法提供上下文；当前用户目标和功能规格决定工作范围。实际进度参考 [progress.json](docs/implementation/progress.json) 和对应任务报告，不自动继承旧文档的审批、冻结或 activation gate。改进验证方法时说明覆盖变化，历史 `not_started` 或失败不表示已验收任务需要重做。
