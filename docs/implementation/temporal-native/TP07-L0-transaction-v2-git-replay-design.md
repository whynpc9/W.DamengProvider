# NEW L0 prepare 首失败：Git replay 隔离设计

2026-10-11。本报告只读失败证据及冻结源码，未执行 Git、项目 import、tests、SDK、DB或重试；唯一写入为本文。结论：**失败确定发生在 transaction patch 后的178完整来源检查；精确候选原因是父仓库发现造成 Git 格式 patch 的子目录路径过滤。先由 Low 做一次有限对照确认，再开放最小环境隔离修复。** 不把源码审阅或先前34项合同通过转记为 prepare 接受。

## 已证事实与归因边界

实际 `TP07-L0-transaction-v1-prepare-control` 证据：

| 文件 | SHA-256 |
| --- | --- |
| `failed-independent-readback.json` | `aca5a4134b8de117d77b82fe37378792ebbe4bb3ae3f6b30becb581a4f236d35` |
| `expected-source-differences.json` | `4fb9ecf5efd4ad026114d6528eeb207c4f4a561d413de6a6b0cf2634c156cc51` |
| `partial-source-hashes.json` | `ff98d7ce538fbd7f2b6b7532d1630106f5893dac1711a61508ff95301c6f9543` |
| `actual-artifact-hashes.json` | `eefdc4005f52b4309ad4218f1fbb81ce05515448b7b736f70af715ebaf8cf0b9` |

原 runner `06fbff3637f39039e12fdcc5ad0c785857fa6e94d94d0c4651f6cb90099db8fd` 自然 exit1，约0.304秒，安全错误 `TP07_L0_stopped_exact_source_closure`。本轮独立计算 immutable replay 全部175文件哈希，与 partial map及旧dc2b批准175逐项相等；DI/retry仍旧字节、三个事务新增文件不存在。七模板尚未复制，符合 transaction 后178检查先失败的调用顺序。

六个 check/apply 日志均已创建、零字节；source manifest及prepare receipt不存在。源码路径表明两次 transaction 调用已越过返回状态检查，但原六条命令结构化 receipt未落盘，不能把空日志宣称为独立六命令成功证明。保留自然失败、21471保护项不变及PID/PGID93924已消失的原readback，不补造receipt，不改旧输出。没有SDK/DB/retry。

实际旧bridge/mapping分别有43/12个普通 `+++` 头且没有 `diff --git`；d13f有五个Git格式路径。workspace有 `.git` worktree指针，replay没有 `.git`。`public_env` 不传入任何 `GIT_*`，`process` 原样使用该白名单环境，因此未阻断从replay向父worktree发现仓库。

Git官方文档明确说明仓库子目录调用会忽略目录外的patch路径，且不带index选项的apply可直接作用于非仓库文件。[git-apply手册](https://git-scm.com/docs/git-apply)。上游参考实现中，`parse_git_diff_header` 将Git格式标为 `is_toplevel_relative`，`prefix_patch` 不为它添加cwd前缀，`use_patch` 再按仓库子目录prefix过滤；传统格式会补prefix。全部被过滤时可以自然0，默认verbosity也不打印skipped信息。这正好解释旧175形成而d13f五路径全未改变的组合；忽略规则本身不是此解析分支的原因。[Git v2.50.1 apply.c](https://raw.githubusercontent.com/git/git/v2.50.1/apply.c)

上述是与实际事实相符的明确源码机制，**尚非本机Git进程的独立因果对照**。原receipt未绑定实际Git版本，本轮也没有执行Git；不把参考实现版本当作本机版本。无需泛查产品、W包或重写已批准patch。

## Low 一次性确认窗口

root若授权，Low在全新 ignored scratch创建两个独立旧175副本，起点必须逐文件等于dc2b，且都没有 `.git`。不读取或改动失败producer输出作为运行目录，也不执行完整prepare。两组都只对原签定d13f运行同样的 `git apply --check <absolute-patch>`、`git apply <absolute-patch>`，cwd为各自replay，原30秒deadline/kill/reap；唯一处理差异是第二组传入 `GIT_CEILING_DIRECTORIES=str(replay.parent)`。保全精确argv/cwd、自然退出/timeout/termination、日志SHA、前后全map以及实际解析的Git可执行路径/版本证据；保护旧来源和签定refs不变。

预期对照是：原环境两命令自然0但仍旧175；ceiling环境两命令自然0且准确178，173不变/2修改/3新增。仅此组合确认所述因果与环境修复有效；任一结果不符就保全并停止，不能重复尝试其它变量或自动重跑producer。该四调用诊断只证明d13f路径解释，不代替未来完整六命令重放/185验收。

## 最小修复与接受顺序

确认后High最小修改范围为 `runtime-run.py` 的prepare Git环境接线及 `test_runtime_contracts.py` 的相应有限回归测试。`runtime-validate.py`及其六receipt合同无需修改。只在prepare创建独立环境字典，以原 `public_env(output)` 为基底设置 `GIT_CEILING_DIRECTORIES` 为实际新 `replay.parent` 的规范绝对路径，两处process调用点共同使用它，覆盖bridge/mapping/transaction六次调用。该变量阻止父目录仓库发现；它不会隔离显式 `GIT_DIR`，但现有白名单本来就不继承该变量。不要改变全局环境或公共环境函数。[Git环境变量文档](https://git-scm.com/docs/git#Documentation/git.txt-codeGITCEILINGDIRECTORIEScode)

可以使用一个prepare专用小helper便于测试环境副本、精确ceiling及不污染公共/SDK环境；若采用helper，其输入由当前prepare已规范化output/replay提供，不新增任意CLI覆盖。回归须保留原34方法AST，验证隔离环境只新增该固定key、原公共环境/进程环境未突变；新Low真实六命令和185全map提供完整行为证明。不得用mock成功返回证明patch实际应用。此处不要求新增SDK或DB测试。

所有argv、cwd、日志位置/SHA算法、receipt字段、30秒预算、process/public_env/internal_environment、SDK和runtime流程保持；不创建 `.git`，不加 `--directory/--unsafe-paths/--index`，不改d13f、4001、66c3、产品178、七模板或原175重放。继续按175→签定五路径178→七模板185，锁阶段再实际186；完整来源检查不能放宽为仅count或列出文件存在。

High修复冻结后，Astra源审、Low新六文件pack静态及完整Python自然通过；root再开放全新prepare目录，必须真实六命令receipt及完整185通过后才进入native锁/build/grammar35。原失败和旧34接受保持历史归属；新pack需要新证据，不能复用旧pack接受修复后的源码。runtime授权仍等待新185、真实186、build/startup及既定后续门；NEW E338/D继续等待。本文冻结后停写。
