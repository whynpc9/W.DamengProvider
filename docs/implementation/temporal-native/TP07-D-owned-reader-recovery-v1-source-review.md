# TP07 D owned-reader recovery v1 source review

结论：**SOURCE_BLOCKED**。冻结 C# 的 reader 作用域修复符合设计，但有限 adapter 有两项实质缺陷，当前完整恢复工具不能签发源码接受或进入 SDK/runtime。无 build、host、DB 或 D runtime 接受。

本审查只读取冻结源、JSON、diff、AST 与实际文件哈希。未运行 adapter、导入项目/helper 模块、执行测试/SDK/CLI/数据库或 Git；唯一写入是本报告。旧源码、失败证据和设计保持不变。

## 冻结对象

根：`.local/temporal-native/verification/TP07-D-owned-reader-recovery-v1/`。

| 对象 | SHA-256 |
| --- | --- |
| `adapter/recover.py` | `b3cbef251d03a6db4edf07c21402a535f83680c5dc0c9061b288400d17ea5d65` |
| `source/source-manifest.json` | `e459f207782f1aa8f1b9cea2b49f55a731be1f33496ecbcf87f56d9857ba29e6` |
| `source/repair.patch` | `978f0d73241822d02ebebd442a00e5da2d1b5905b7046a64ac653d33122dc5c4` |
| 修复后 `test/common/OwnedObjects.cs` | `07b0fa0857d1322731c6fc9b74feecc16d9a6a08d5352cd3de04651d3833a79c` |
| `handoff/freeze-manifest.json` | `161c2ddcc667e5ee4652addca1c4bcde0ab0132bcbbbc770131cfeb1480195f8` |
| `handoff/source-approval-template.json` | `e146fd1bf88b8e6bca88de8372ae4f35b41750ed0255ce72c3334f92dbf81b84` |
| `handoff/implementation-report.json` | `b3636acd8cc7196a5348d59de670b4d8d768ab139ecbb218e3c267e9db5d657d` |
| 接受设计 | `7ac011ad9fdca992aca03a140ed53a3c07932bc6638a07896b53949e3893ce43` |
| root 设计接受 | `a3ea0325dc16080fcc41b840440e18774accd96bcfc158dfae79d73aefbddcbc` |

## B1 — 必须回收已创建的进程组，包括中断和 leader 先退出

位置：`adapter/recover.py:135–154`，以及 `:255–265` 的顶层异常处理。

`process()` 用 `start_new_session=True` 启动外层 SDK/wrapper。它只在 `TimeoutExpired` 路径终止子进程；finally 只关闭日志。SIGINT 引发的 KeyboardInterrupt 不属于顶层 `except Exception`，SIGTERM 没有处理器。监督 Python 在这类中断退出时，独立 session 中的 child/后代可以继续执行，包含获准的 DB cleanup host。该缺陷不是业务重试或 marker 问题。

另两条同源故障路径：leader 自然退出但进程组仍有后代时，`group_absent=False` 仅使 `success()` 报失败，不回收残留；超时发送 TERM 后只等待 leader，若 leader 退出而后代忽略 TERM，则不会升级 KILL。记录失败或冻结文件不能替代实际回收。

此外当前 TERM wait 2 秒后再 KILL wait 2 秒，可消耗两个完整等待段，未实现声明的单一 reap_seconds=2 边界。

最小修复应集中于现有监督函数：记录自己创建的 child/组所有权，对正常残留、timeout、可捕获中断和异常走同一有界清理；检查整个拥有的组是否消失，必要时升级 KILL，并 reap 直接 child。保持原 execution deadline 与单一 reap 预算，恢复阶段不得因此扩为重试。internal host 仍属于 wrapper 的组，不能把不属于自身的组当新拥有的组清理。无需新增诊断 host、业务 SQL 或清理策略。

这是静态路径判断；本次没有发送信号、启动测试进程或模拟执行。不可捕获的 OS 强制终止不属于 Python handler 可保证的情况，本 finding 指当前可以处理但遗漏的 SIGINT/SIGTERM/异常和 leader/后代分离路径。

## B2 — dotnet 三字段对象绕过了首执行前的可执行文件哈希验证

位置：`adapter/recover.py:40–46`、`:55–113`、`:158–160`、`:187–188`。

`refs()` 只把恰好 `{path, sha256}` 的对象作为文件引用。冻结 manifest 的 `dotnet` 实际含 `{path, sdk_version, sha256}`，因此此节点被递归为三个标量，未执行 `artifact()`，也未加入 protected。`environment()` 只验证 `which('dotnet')` 的解析路径，随后 build 使用该路径执行 SDK；runtime 也使用相同可执行文件启动 host。源码中没有另一个补充的 dotnet 字节验证。

实际 SDK 文件本次独立读盘 SHA 为 `00c673767ee2538369f307d3c8a0ce06f16bc9e0ecf8ce767b64dd02b2230e98`，与声明一致；这只证明本次审查时的文件，不消除执行入口对后续漂移的遗漏。SDK `--version` 输出检查发生在执行之后，不能替代执行前身份检查。

最小修复是在 `source_preflight` 中显式验证固定 dotnet 对象的路径/版本/hash，并把实际可执行文件加入 protected；可复用 `artifact()` 验证其中 path/sha256 对，不必放宽所有 ref schema。该检查必须保持在首 helper、SDK/process 或 wrapper 调用之前。无需改变候选 W、SDK 版本、预算或 source186。

## 已成立的源码与来源结论

独立计算新 replay 完整186物理 map，与 manifest 一致；与原接受 BC186 比较，唯一变化是 `test/common/OwnedObjects.cs`，185项原字节相同。撤销新增 PK lexical block 后，新文件全文恢复为原 `80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946`。这比只数断言更严格地证明没有夹带政策变化。

该 block 使 PK reader/command 在 Index/Check 查询前 Dispose，完整保留原 PK 单行断言、columns/type/scale、child ACK、marker、exact DROP 与 fresh absence 逻辑。Literal Program、CandidateGuard、SafeEvidence、csproj、locks 和 W/EF 产品文件未变。原 product178 与原 BC186 的物理文件也全部核对通过。

source manifest 明确 `owned_reader_maintenance_fork`、原 BC `71380a…`、新实际 map 和 `old80c→new07b0` 替换，不冒认旧 L0/BC 接受。新 startup 沿用 `TP07-D-cleanup-startup` 协议类型，与未变 C# Guard 兼容；source_kind/maintenance_origin/source approval 和新 source/lock/startup ref 的实质绑定由 adapter 承担。C# runtime_identity 的 W producer source hash 与新维护 `source_manifest_sha256_binding` 角色区分正确。

source approval 模板仍是 `source_approval_template_not_signed`，入口要求独立 root `source_approved` 文件；build/runtime 分别要求单独 execution approval。source preflight 在 passive helper 和 process 调用前；source、build、runtime 的 scope/权限和 actual build 七 refs 有区分。上述 B2 是此 preflight 的具体遗漏，而非整体缺少 preflight。

build 静态配置为 SDK query、locked restore、no-restore build 三条命令，单节点/禁 node reuse 与共享编译，单条上限300。有限13个非W包加固定W、本地 feeds、两项目 graph、NuGet contentHash 与 whole-ZIP SHA512、metadata int2、新 cache 与原锁的来源关系明确。metadata helper `ca7bb8…` 用于实际 PE identity；未改为离线 Assembly.Load。以上是代码意图及接线审查，未产生 build 结果。

runtime 路由固定原 TEST wrapper→内部 host→原 Literal `--cleanup`，shared/候选/只读 ledger/输出与 startup 参数有限。host stdout/stderr 丢弃，SDK 环境由原 public_env 白名单构造；异常输出仅固定 Stop code 或异常类型。未发现 raw exception Message、凭据、任意 SQL 或额外对象输出入口。原 Literal finally 的第二次 Cleanup 保留。

原 ledger 与副本 SHA 均为 `19f71ff40c67768c924b346d726ff0f885791eac4be432c5009f415292aaa9b2`，两份实际权限0444，包含原 exact2。当前状态不会进入会 Save 的 alter_inflight 分支。无 comment 的 GuardedDt 仍严格依赖原 create_acknowledged/ACK 条件，未补 marker、清 ACK、改 ledger 或扩大 schema。成功判定要求 cleanup 与 finally_cleanup 两次 exact2、fresh count0、TEST user/schema 和新 startup/source identity。当前没有这些 runtime 事实，cleanup_pending 仍保持。

## 独立回读范围与后续

本轮回读 handoff 的全部小型说明/JSON、source approval 模板、实际 Python AST、diff 与来源 map；实际核验91个唯一 exact path/SHA refs、314个保护 refs、freeze map 的391个文件，均无漂移。保护 before/after 相同，journal 为原 `f6165e…`；build/run 目录仍不存在。未重复110k全盘快照，也未把 handoff 的自述或 AST 布尔字段当作运行通过。

两项 blocker 已即时发 root/High。修复应由 High 在 root 指定的新冻结 revision 中进行，保留本版 adapter、handoff 与本报告。186 C#修复无需因 adapter 两项缺陷重写。新 revision 需重新静态审查并更新 source approval 的实际 adapter身份；本报告不授权执行任何 SDK/DB。未发现需扩张 marker、case、transaction/W/EF 或业务范围的理由。

本报告写完计算 SHA 后停止写入；最终 SHA 在消息中交付。
