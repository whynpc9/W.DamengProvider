# 0.2.0 发布准备

状态：本地发布前检查已通过，等待提交及正式发布；未创建 tag、未上传 NuGet。当前公开版本仍是 [0.1.0-preview.1](release-0.1.0-preview.1.md)。

## 变更范围

- 同包提供 .NET 6/8/9/10，四目标各有主程序集与四套语言资源。net6 的 `DmDataSource` 仅提供 typed API；兼容依赖及 TLS 差异见 [I13](compatibility/I13-net6.md)。
- [数组绑定及 DbBatch](compatibility/I07-I08-array-binding-dbbatch.md)、[存储过程、标量 OUT/INOUT/返回值及受管输出游标](compatibility/I09-stored-procedures.md)。
- [异步元数据发现与 SELECT SchemaOnly/KeyInfo](compatibility/I10-metadata.md)。
- [EF 接入反馈修订](compatibility/I16-ef-integration-feedback.md)，包括高精度时间写入、失败分类及有限会话恢复；[Windows TLS 私钥支持](compatibility/I11-windows.md)。

连接归还仍销毁物理会话，不声明物理复用性能收益。存储过程显式事务、游标输入及过程批调用仍拒绝。运行时版本兼容不等于对应 EF Core 版本已适配；下游 EF 验证由下游管理。

## 本轮检查

2026-10-15，以 `main@c415be4883075fb707e32f2d5918d3a54960b4aa` 加本次发布准备 diff 为候选。默认开发版本调整为 `0.2.0`，文件版本为 `0.2.0.0`，程序集版本继续为 `0.1.0.0`。最终发布版本仍由 tag 决定。

环境：macOS 26.6.2 / osx-arm64，SDK 10.0.401。完整准备命令（退出 0）：

```sh
python3 -B -m unittest discover -s tools/NuGetRelease -p 'test_*.py'
bash eng/release.sh prepare --tag v0.2.0 \
  --commit "$(git rev-parse HEAD)" --output .local/release-0.2.0
```

| 目标 | 实际 runtime | 离线测试 | 包消费 / 资源回退 | 首次初始化 | shared / 严格 TLS |
| --- | --- | ---: | --- | ---: | --- |
| net6.0 | 6.0.36 | 2410/2410 | 通过 | 7/7 | 均通过 |
| net8.0 | 8.0.20 | 2408/2408 | 通过 | 7/7 | 均通过 |
| net9.0 | 9.0.9 | 2408/2408 | 通过 | 7/7 | 均通过 |
| net10.0 | 10.0.12 | 2408/2408 | 通过 | 7/7 | 均通过 |

共 52 份 TRX，**9634/9634**，无失败、跳过或中止；同一包的 20 个托管资产、精确依赖组、八个独立 PackageReference 消费者及 **28/28** 首次初始化场景通过。消费者限制主版本内 roll-forward，并核验实际 runtime 与所加载资产。日志、TRX、命令退出码、包 manifest、消费者资产及首次初始化报告保存在 ignored `.local/release-0.2.0/nuget-r3oeyglx/`；顶层 `package-path.txt` 和 `manifest-path.txt` 指向本次包及 manifest。

发布工具 Python **23/23**、资源验证工具 Python **50/50** 通过；`eng/*.sh` 与 `scripts/*.sh` 的 `bash -n`、本轮文档本地链接、项目 XML、进度 JSON 和 `git diff --check` 通过。另执行产品项目 `dotnet list ... package --vulnerable --include-transitive --format json --no-restore`（同样设置任务 CLI home/禁首启/禁遥测），退出 0，NuGet 公开源未报告已知漏洞；输出为 `.local/release-0.2.0/dependency-audit.json`。

独立审查确认当前 CI / tag 发布入口已安装四个实际 runtime，调用完整四目标 prepare，且上传凭据仅在最后一步可见；本轮无需改写 Actions 或历史专项工具。原 main 基线的 [CI 38743741673](https://github.com/whynpc9/W.DamengProvider/actions/runs/38743741673) 成功，属于该提交，不代表本次未提交修改已在远端执行。

独立验证者通过现有 TEST wrapper 对同一个包的四个消费者串行执行 shared `plaintext` 与隔离实例 `tls`，**8/8 退出 0**，没有重试。每次均确认服务器用户与当前 Schema 为 `WDM_PROVIDER_TEST`，参数化 Unicode、Reader、时间值、BLOB/CLOB、提交/回滚、预取消后的会话可用性及 Reader/连接释放通过。每轮仅删除本次成功创建的精确 GUID 表，并由新连接确认 `final_database_state=owned_table_absent`；最终事务完成、连接关闭。TLS 显式 `RequireTls`，验证本机 CA 和客户端证书；本机无 CRL 场景显式 `NoCheck`，产品默认 Online 不变。各命令、退出码和安全 JSON 报告保存在 `.local/release-0.2.0/integration/commands.jsonl` 及 `net<版本>_<plaintext|tls>.json`。

独立审查与验证已复核当前 diff、52 份 TRX、实际包与 manifest、八个消费者依赖/资产、28 个首次初始化场景和八份实库报告，未留下阻断问题。旧候选结果见各兼容文档，不合并为本轮验收。原 preview.2 准备在离线阶段因用户改定版本主动中断（退出 130，记录在 `.local/release-preparation/`），本轮从 0.2.0 完整重跑，不是产品失败。

本轮实库范围是 shared 与隔离 TLS 的包 smoke，不代表完整存储过程/元数据实库矩阵、下游 EF、长时压力或其他 OS/服务器 profile 的新验收。已有专项结果继续按各自候选和环境解释。

## 正式发布剩余操作

完成候选检查、审查并提交合入后，按[发布指南](release.md)在最终提交重跑准备及相关实库检查，创建并推送新 tag，等待 Actions 上传。随后从公开 NuGet 源独立安装并核对包身份和支持范围，再将本页及 [progress.json](implementation/progress.json) 更新为实际发布状态。
