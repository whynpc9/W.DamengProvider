# TP07 D reaping fixture v3 source review

结论：**SOURCE_BLOCKED**。fixture 的原断言、真实helper和新增持久证据均符合限定范围；但父端取消被转为普通测试失败后，默认 unittest runner仍可能启动后续进程树。Low不得执行本版。此结论不改变此前维护源/adapter的历史源码审查，也不宣称首次SIGINT失败根因已确定。

本轮只读源码、patch、report、AST、实际文件哈希与本机Python3.14 unittest标准库。未运行或导入fixture/helper，未执行测试、SDK、CLI、DB或Git。唯一写入为本报告。

## 冻结身份与核验

根：`.local/temporal-native/verification/TP07-D-owned-reader-recovery-v1/`。

| 对象 | SHA-256 |
| --- | --- |
| `adapter/test_reaping_v3.py` | `c8087d71e072a26fc0eaa9ffb3cbc66b9e471504279708cc9369d3346b3a4a1e` |
| `handoff-v3/freeze-manifest.json` | `941cb5bf0f18a8210a9ae51661e0b546992919e60d974cef31b411a298fec1b9` |
| `handoff-v3/implementation-report.json` | `cc9cbe924df9b556686e6cfe5ee028e9e0770c50e4dd053d0f44449c6fc6c729` |
| `handoff-v3/test_reaping_v3.patch` | `38ba0729ead106a47833956b1cde15bd7d410529e10eef0c01f2b16ec73f6882` |
| `handoff-v3/static-AST-proof.json` | `9777a8bb6f45338ce9a64339ff2c68627abe4ea2733aa3f591cb12669b24c144` |
| 保留真实helper | `c7e73e4b5f945627dec35897c9775ea50aaf78a59cd91809d6fa44bdc23f3cff` |

独立比较确认 `assert_reaped` whole AST与原v2相同；`atomic`、`safe_exception`、`public_environment`、`wait_json`、`group_absent`、`read_if`、`bounded_fallback` 七个函数的AST与已审单SIGINT诊断相同。753项保护refs before/after相同且实际SHA均匹配，9项新freeze成员实际SHA均匹配。历史相对引用按明确workspace root解析，不新增全局绝对路径门槛。

## 唯一阻塞：取消后默认runner继续下一次spawn

`run_case` 的父端SIGINT/SIGTERM handler抛出 `FixtureInterrupted`；`except BaseException` 将其保存为safe error，finally完成限定清理和case冻结，随后统一抛出 `AssertionError`。当前main使用 `unittest.TextTestRunner(verbosity=2)`，默认failfast=false。

本机Python3.14 `unittest/case.py:54–75` 对KeyboardInterrupt会继续抛出，对其他异常则记入测试结果；`subTest` 默认在失败后继续当前method循环。当前suite没有stop标志。因此在SIGTERM子case中，Low外层取消可以被转换成普通失败，随后仍启动SIGINT子case或下一个natural method的新session。外层稍后终止原父组不能代替对新session的所有权清理。

natural case还有同类入口：它在父进程直接调用真实 `V.process`；父端取消可由真实helper消费为 `row.stop_reason='SIGINT'/'SIGTERM'`，进而触发原natural-case断言。该失败同样被转换为AssertionError，默认runner也继续。不能只处理FixtureInterrupted分支而遗漏这一条。

## 已确认的最小闭合方案

root提出仅将现有runner改为 `unittest.TextTestRunner(verbosity=2, failfast=True)`，本轮静态确认此一参数足以覆盖上述两条失败路径：

- 本机 `runner.py:245` 将failfast传给result；`result.py:134–135` 的失败subTest设置stop，普通addFailure也由failfast装饰器设置stop。
- `case.py:566–569` 在失败subTest立即抛出内部 `_ShouldStop`，退出当前method的signal循环；`suite.py:108–109` 随后按shouldStop停止下一个method。
- runner仍正常返回，现有main仍能写suite-summary并冻结，而不需要额外suite框架或改为向外传播KeyboardInterrupt。

建议High按root新revision授权仅增加此参数并冻结v4，保留本版文件。成功路径仍完整执行2methods/3棵树；普通失败也会及时停止，不把未执行后续case记为通过。真实helper、原断言、5秒process deadline及2秒reap均不变。上述为源码方案确认，尚无v4源码或运行接受。

## 其余限定审查结论

fixture在subject加载前校验固定c7 helper SHA，真实调用V.process/V.absent，没有替代算法、SDK、source preflight或业务DB入口。完整测试集合仍为自然leader退出加外部SIGTERM/SIGINT，共两methods、三棵受控树，无重试。

原v2共同断言全部保留：实际PID/PGID、直接child reap、owned group absence、强制清理不得算natural成功、reap预算和elapsed≤2、TERM/KILL。各case独立fresh持久目录、nonce、atomic ready/tree/result/error，退出码断言之前写观察；safe异常只含type、phase、函数/文件basename/行号，不含Message、env或locals。

失败fallback沿用已审诊断，单一2秒预算，只根据本次Popen handle及nonce/current getpgid核对的owned组执行清理。父端取消处理的局部清理本身存在，问题是清理完成后suite继续调度；不需要扩大PID/PGID清理范围解决。

原helper、原fixture、单次诊断和首次失败证据不改，维护source186及PK修复也不变。已有单SIGINT诊断PASS只描述该一次观察，不能替代尚未执行的完整fixture；首次v2失败根因仍未确定，本版没有生产SIGINT bug修复声明。

root与High已收到唯一阻塞及最小方案，Low已获通知不执行v3。下一步限新的fixture revision源码审查；测试、source执行gate、SDK及DB许可仍分别pending。本报告计算SHA后停止写入。
