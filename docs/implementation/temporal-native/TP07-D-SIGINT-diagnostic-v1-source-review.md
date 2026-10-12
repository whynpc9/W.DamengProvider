# TP07 D SIGINT diagnostic v1 source review

结论：**SOURCE_ACCEPTED_IN_SCOPE**。此接受仅覆盖一次受控 SIGINT 诊断的源码和有限执行方案；实际诊断仍 pending。它不证明 recovery-v2 离线测试已通过，不确定首次 SIGINT 失败原因，也不接受 SDK build、exact2 DB恢复或 D runtime。

本轮完整读取诊断源码、旧fixture到新诊断的patch、implementation report、AST说明和实际refs；仅使用读文件、标准库AST解析和哈希。没有运行或导入诊断/helper/tests，没有SDK/CLI/host/DB/Git操作。唯一写入为本报告，旧冻结报告与证据不变。

## 冻结来源

根：`.local/temporal-native/verification/TP07-D-owned-reader-recovery-v2-SIGINT-diagnostic-v1/`。

| 对象 | SHA-256 |
| --- | --- |
| `source/diagnose_sigint.py` | `4496af9458aa4d18bbaa9d2d91aa150451d7963a7bfc540319a02dde74442caa` |
| `handoff/freeze-manifest.json` | `f72f441e93585ddb4f9f75d17e74368325efaed31fd40a5b1c3a5bb6125f597b` |
| `handoff/implementation-report.json` | `81b92f65a9dcf9ab7da3e9b21ecb24d113d2464a29a45f609378f0d6f22d991f` |
| `handoff/old-fixture-to-diagnostic.patch` | `59a669c9df83858d67995302191828ad6ea46ad0dfbd7a233c0f0949b0c3e932` |
| `handoff/actual-input-refs.json` | `2a5e37d7ec1777d0d4bba5c23dacd3b3dbb692a99c4cb25229ca7b3558e66ef4` |
| `handoff/static-AST-proof.json` | `11297245dee967177a9b42152aa3866b0ea106dfe6a85b95f51839733d30d712` |
| 实际调用的冻结 recovery-v2 helper | `c7e73e4b5f945627dec35897c9775ea50aaf78a59cd91809d6fa44bdc23f3cff` |
| 保留原 fixture | `8e6c94c0c528eb8684cc7e1034537a31d33f26ddd3c4725200a103572294f1f5` |

原失败 tests.log `1fc64bd928aec4b245ff904271a748bdca90e1b590b80e9a1ae9c2238fb56ed0`、failure readback `382c97ad819f2fed9f6cadda1c09ae257469b287d337b79260f523abafba4d5a` 和 artifact map `16c9943dc628488dbf3c19df8eda9bd507b4ae6d8af14645819acde67a3f2d51` 继续保存为失败历史。

## 诊断对象与证据路径

helper加载前验证固定工作区路径与完整SHA，运行时只调用真实 `V.process` 和 `V.absent`，不调用source preflight、build、runtime或业务清理入口。新fixture保留leader及同组忽略TERM的child结构，真实process deadline仍5秒。源码只有一次 `supervisor.send_signal(SIGINT)`，没有选例重试、重复发信号以求通过或新的SDK/DB调用。

owner、supervisor、child-ready、tree、result和error等JSON都先写独立pending文件，关闭后rename发布。每次使用fresh持久输出目录和nonce，取消原TemporaryDirectory自动删除。supervisor捕获BaseException时保存安全字段，不把异常丢在DEVNULL里。

父端先持久化并输出supervisor PID/PGID/exit、tree、child-ready、result及supervisor安全异常，再断言exit=0。若此前阶段失败，finally仍保存实际已有观察与父端安全异常。这样补足首次失败在exit断言处丢失下层原因的证据缺口，不改变被测helper的SIGINT语义。

`safe_exception`仅记录异常类型、固定阶段、traceback frame的函数名/文件basename/行号；不读取Message、ToString、源代码行、locals、参数或环境值。子进程stdout/stderr仍丢弃，传递环境为原非秘密白名单。报告打印的argv只涉及受控Python、fixture、helper路径/hash和新输出/nonce，不含连接凭据。

## 断言、预算和清理边界

原SIGINT成功条件保留：stop_reason=SIGINT、实际leader PID/PGID、直接child已reap、整个owned group不存在、非natural成功、拥有该组、reap预算2秒且实际elapsed不超过2秒、TERM及KILL都实际记录。没有把supervisor非零或缺失result改成成功，也没有放宽原reap断言。

Low应按handoff给出的单次argv使用独立外层60秒/reap2监督窗口；诊断源码内部的ready等待、supervisor等待和真实helper等待均有限，不循环执行诊断。所有结果只写新输出根，未触及维护source186、原helper、原fixture、ledger或失败证据。

父端失败清理在共同2秒monotonic期限内处理本次拥有的supervisor以及由nonce绑定ready/tree记录的组，发送前通过实际getpgid核对PID/PGID。child-ready也可在tree尚未发布时提供当前child组依据；不使用名称/前缀扫描、现有daemon或任意配置的进程组。直接supervisor用其Popen handle poll/reap。父端可捕获中断在spawn阶段延迟到取得handle，清理期间屏蔽重复可捕获信号，结束恢复原handler。

supervisor内部仍由冻结真实helper负责其新建leader组，父端fallback提供失败后的限定清理。fallback结果保留实际targets、发出的信号、remaining_groups和supervisor_reaped；存在剩余组或未reap不能产生DIAGNOSTIC_PASS。不可捕获OS强制终止不被宣称可恢复。诊断不会清理那两张尚pending的TEST表。

## 独立回读与接受范围

实际核验743个保护refs的before/after相同且SHA均匹配；freeze列出的9个成员均匹配，14个唯一path/SHA引用闭环。source及handoff文件无可写位，目标runtime输出尚不存在。历史相对路径按其明确工作区root解析后核对，没有新增全局absolute-path门槛。

本次未发现实质源码阻塞点。root可按既定分工安排Low一次受控诊断，保存实际退出、异常帧和最终进程状态。若诊断通过，只说明本次受控SIGINT观察符合断言，不能回写首次失败为通过；若失败，应以新安全frames定位下一项最小修复，不预设Python、时序或业务根因。

本报告完成后计算SHA并停止写入，SHA由消息交付。
