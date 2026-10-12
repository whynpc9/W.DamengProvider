# NEW L0 v2 Git replay 冻结源码审阅

2026-10-11。结论：**限定源码接受，无阻塞缺陷；可以进入 Low 新工具包静态核验和完整 Python 合同验证。** 本轮只读取冻结源码、补丁、JSON和标准库AST/哈希，不执行Git、项目import、tests、SDK或DB；唯一写入为本文。35项仅为静态计划，实际prepare/185/186/build/runtime均未由本文批准。

## 身份与有限差异

设计为 [v2 Git replay 隔离设计](TP07-L0-transaction-v2-git-replay-design.md)，SHA `bd7869a10efd4be9b9b6b3e72510b29da4c44972a703608121a4be40034a6478`。本轮独立读回 handoff SHA `bbf8854e312914db606c221cb47029e3be39116ad3721a33377579e405a60615`、完整 two-file patch SHA `be8e22cefa6092919da0b3665803c3e1b5eb3658f56e3b40583e9e5ab1932b4b`，实际六文件 aggregate `45cb21fd852f4714afd354da32ac8ab91e23843187df360ab8dd27b05d145dc7`。

| 文件 | 实际 SHA-256 |
| --- | --- |
| `runtime-run.py` | `8fef18e90be7f44ac644dc006a590e354da43d0f1f86b89f0dc5eff1ee4ab4ec` |
| `test_runtime_contracts.py` | `19c48fbc90e786aae2e0c563bf951609a1c71cfbf1423f40602928c1ccde2fdf` |
| `runtime-validate.py`（字节不变） | `f1729710045f9f77dc369c183b1d53bc552fbaa5d107609eab747efebc85f5a2` |

独立对比v1冻结实际源码：runner除 `prepare` 外完整module AST相同；test除唯一新增方法外完整module AST相同，原34方法逐项AST相同。其余四个pack成员字节保持；handoff的816个显式保护文件实际hash均匹配且before/after清单相等。所有设计、v1首失败、v2诊断引用均匹配实际SHA，旧证据没有回写。

Low一次性本机Git2.55.0诊断readback实际SHA `61c0caf92e90b98845dcc507d0b6dcd193e8932a128f3aee4e6febb454acca3b`：五个进程自然0；baseline保持完整175，唯一ceiling环境差异产生准确178（173同、2改、3增）；21843保护项不变。该独立对照已确认设计所述父仓库路径过滤原因，但仍不是完整prepare接受。v1失败 `aca5a413…` 保持失败，不补造缺失六receipt。

## 实现与测试判断

prepare仅四处行差异：在 `fresh()` 返回已resolve的输出并创建replay后，复制 `public_env(output)`；将副本唯一新增 `GIT_CEILING_DIRECTORIES` 设置为 `str(replay.parent)`；原两个Git process调用点改传同一局部副本。`fresh`的现有路径规范化确保ceiling是本次输出的规范绝对路径。bridge/mapping内循环覆盖四次，transaction循环覆盖两次，均保留原argv、cwd、日志、30秒deadline与receipt生成。

公共环境白名单本来不继承 `GIT_DIR/GIT_WORK_TREE/GIT_CEILING_DIRECTORIES`；本次不修改白名单、全局os.environ、process、SDK/internal-host环境，也不新建 `.git`。没有新helper、CLI、receipt字段、patch改写或validator改动。原签定d13f/4001/66c3及严格175→178→185→186来源、六命令独立receipt门均保持原实现，不弱化真实文件闭包。

唯一新增方法 `test_prepare_all_six_git_calls_use_copied_exact_ceiling_and_refuse_without_execution` 有明确边界：AST检查两调用点、4+2循环及30秒值；使用真实批准refs和真实archive进入prepare，在首次bridge check调用前由refusal sentinel核对精确env/cwd/argv/log后抛错。测试注入不可信父GIT变量，验证白名单排除、局部副本和公共环境不变；结束确认没有 `.git`、source manifest或prepare receipt。它没有mock任何成功Git返回，也没有声称真实执行六次。基于实际refs走到首调用的行为断言与Low真实诊断互补；完整六次重放由下一实际prepare验证。

本轮不需要返修。后续须新六pack静态接受、全部35项Python自然通过，再由root开放全新prepare目录：实际六receipt、175/178中间门和完整185必须通过。之后按既定新native186锁、locked build、grammar35、startup与runtime明确授权顺序推进。旧34通过不替代新35，诊断178不替代producer185，源码接受不构成SDK或runtime授权。本文冻结后停写。
