# TP07-E transaction v3 historical consumer v2 source review

结论：`SOURCE_ACCEPTED_IN_SCOPE`。B01已在新的冻结消费者源码中修复，未发现新的必修阻塞。本结论仅接受源码；历史12例语义消费者尚未由本审阅执行，不接受旧窗口、当前E runtime或完整338/25。本次未读取正在执行的A结果作验收依据。

## 冻结身份

- 新源码：`.local/temporal-native/verification/TP07-E-transaction-v3-historical-consumer-v2-handoff/revalidate-history.py`，SHA256 `102cda9bbf0f6c9be7476c15e85f8bbbf716efabf3aeb7979d1f8b24b618e7ae`。
- source freeze：`8f954dfd44f08a52f1ab891846d90635e33b0a848ed18282c551219abc2bdd41`。
- implementation freeze：`c7712df072db64954bd2584d8d884ee3b6d064769f8256c9a6bfda4fd4f730ec`，位于相邻 `TP07-E-transaction-v3-historical-consumer-v2-implementation-handoff/`。
- `B01.patch`：`c408287417a592875dd243e488f4f813f7525aca80a5dcb71ce37fdd7ea421e5`；AST proof：`a7fe63482c55665dc86cbfba2deb55e1aacd1e6998f8d196b600946c1dc866dd`；actual-command proof：`fdf4f9d7aee8ad930dd3fb12447154aeeddb67e075183d3c790b87b5e881906e`。

独立文件/JSON/哈希/AST读取确认两freeze、所有列出的handoff成员及源码副本匹配。旧源码 `c5ec9f7d…` 和原拒绝报告 `2420a7dd8181a356353bc4e4196c009c529c8a9b948710f16787f1c1f44556d2` 保持原哈希。

## B01修复核验

源码第123行新增仅限 `role == 'main'` 的八项追加：`--group mutation --window-index 0 --run-id OLD_RUN_ID --cohort-id OLD_COHORT_ID`。本次从实际旧approval、window receipt及两个host receipt独立构造期望数组，逐项比较结果为main全部18项相同、cleanup全部19项相同；未执行任何命令。原main收据SHA `4fbd81bb147ec3d36fea2b22f8aec7b404304bc9ac273c2dd7ad01d80fb9821d`、cleanup收据SHA `68ddb28b09c3c05d99bb96d1e89af9708ffc7ad9405d8a96c514962f0a11470a` 均匹配。

另一处变化仅为 `HERE` 指向新的v2 handoff目录，符合真实脚本 `__file__` 检查。独立AST归一化移除该main分支并还原HERE常量后，完整模块与旧源码AST相同；原函数中只有 `verify_history` 有上述新增语句。cleanup分支、完整命令相等断言、`main()`、错误处理、所有Refs和OUTPUT常量均保持；三字段schema字节相同。修复没有截断实际argv、修改旧receipt或放宽身份判断。

## 保持的来源与执行边界

本次再次核对15个OLD_REFS、8个CURRENT_REFS和23个WINDOW_FILES的实际哈希全部匹配。此前审阅的1158历史制品、732项原输入、旧source24快照与两当前源码delta检查代码保持。High的107112项保护前后回读及其scope文件哈希已核验；本次不冒称重复遍历该107112项。

真实ROOT、`__file__`、当前pack/source24、原 `R.open_context` 默认完整L0检查，以及当前context与旧产品/build/L0 Ref相同的约束保持。旧run/cohort/pack/approval与失败validation继续保留，不与新context伪装成同一运行。真实 `validate_report(..., startup, startup_sha, context['oracle'])` 要求12并继续调用 `generated_sql_contract`；不调用E窗口/整轮接受入口，不mock oracle或重写report。

只有所有校验和前后pins通过才创建原定的新历史语义输出目录并独占写入三个文件；该目录在本次读取时不存在。输出继续明确 `runtime_accepted=false/current_cohort_accepted=false`，异常仅打印固定拒绝文字并exit1。没有新增SDK、host、DB、测试或Git调用，也不改变Low外层180秒/reap2预算。

后续须先接受A实际结果，再由root提供真实新approval/有限inputs并单独授权Low运行修复后的B。实际12例结果及完整新338例/25窗仍各自待验收；源码接受不签发这些运行授权。已接受的tracked源码和原39合同不因本次ignored消费者修复而重新执行。

本次唯一写入为本报告；未执行或导入项目代码、测试、SDK、host、DB、Git，未修改旧报告、源码、证据或协调状态。报告冻结后停写。
