# TP07 D reaping fixture v4 source review

结论：**SOURCE_ACCEPTED_IN_SCOPE**。v3唯一阻塞已由限定的 `failfast=True` 变更关闭。接受仅为fixture源码；实际完整suite、source入口、SDK build及exact2 DB恢复仍pending。

本轮只读取v3/v4实际差异、解析AST及核验冻结文件哈希，没有运行或导入fixture/helper，没有测试/SDK/CLI/DB/Git操作。唯一写入为本报告。

| 冻结对象（recovery-v1根下） | SHA-256 |
| --- | --- |
| `adapter/test_reaping_v4.py` | `19eb3f55006adda1662b6cc10833c7992e001ff6430ca9eca54c66cfcf009b84` |
| `handoff-v4/freeze-manifest.json` | `056b7efe541acbf6b784bc107048a1d27b1879fb436407c92136c157e170cdaf` |
| `handoff-v4/implementation-report.json` | `743c1d32fef32b26080f7d4c3623f0cdc737d900b6ee6a00fd2bca5bd1ada0c8` |

独立实际文本比较确认唯一变化为 `TextTestRunner(verbosity=2)` 增加 `failfast=True`。独立从v4 AST删除这一个keyword后，整个module AST与v3相同；没有其他fixture、assertion、进程监督、证据或helper变更。

该差异落实v3审查已核实的本机Python3.14方案：失败subTest设置result.stop并以内部_ShouldStop退出当前signal循环；suite按shouldStop停止后续method。父端FixtureInterrupted被安全记录/清理后转换为AssertionError，以及natural helper消费父端取消后原断言失败，两条路径均不会再调度下一棵树。runner仍正常返回，现有main保留suite-summary和freeze输出。未增加suite框架或更改真实helper。

成功路径仍为原2methods/3棵树各一次，原共同断言、5秒process deadline、2秒reap、atomic持久证据与安全异常字段全部保留。普通失败也及时停止，未执行的case不能算通过。Low按root批准的单次60秒/reap2窗口执行，实际结果另行核验。

本轮763个保护refs before/after相同且实际SHA全匹配，9项新freeze成员实际SHA全匹配，handoff-v4无可写文件。旧v3阻塞报告和首次v2失败保持历史原样；已有单SIGINT诊断PASS不等同完整suite通过。首次失败根因仍未确定，本变更不宣称修复了生产SIGINT bug，不接受D runtime或full C。

报告完成计算SHA后停止写入。
