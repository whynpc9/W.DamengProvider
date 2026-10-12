# TP07 E tools v5：原 L0 validator source-root 绑定修复

2026-10-09。结论：**scoped_static_accepted_for_Low_contracts_and_actual_inputs_preflight / E_runtime_pending**。本次窄修已正确处理嵌套 L0 validator 的原执行路径身份，未发现新增确定阻塞。可以开启 Low 的16项离线合同与 `inputs(..., check_l0=True)` 真实冻结输入预检；DB仍是预检通过后的独立窗口。

本轮仅阅读源码、静态diff/AST、冻结清单及历史收据并回读hash；没有import/执行runner或validator，没有运行SDK、测试或DB，没有改实现或旧证据。仅新增本报告。

## 冻结身份与既有失败

Low `.local/temporal-native/verification/TP07-E-tools-v5/` 的24项live/snapshot逐项一致，只有两项相对v4变化，其他22项不变：

| 输入 | SHA256 |
| --- | --- |
| `ef-validate.py` | `5960d5adf097086a595e68e9d30651a86440454139e8b1316232b10ed25e3ef8` |
| `test_ef_contracts.py` | `3e6bcd45ed6ab8556ad5c6a61930f359625e9d29d159aa97edd807e27c77cbf2` |
| E八文件toolpack manifest | `25deb68d775ba624444bad6ff005b93788bb5bb4c4ceb48d459184539bbf71fe` |
| 24-source manifest | `747c7c2f9e0e9d234aa30b867fb1ea013fb852f68e5f83a548e296167075ae0e` |

先前两份失败summary实读仍为 `4936f7e7711f016fc225c7c9118863f0f2842d6366c2d7c6d8a4af722b07e915`、`5091d28a8887eef1be44bc9d5ebdf7280aa50c4b831d0370a82ac791e8c53c9b`，都在输入校验阶段停止，group0、ledger0、DBfalse；没有fresh DB absence探测可宣称。第一次E快照缺少L0专属runner/tests；第二次原repo虽然联合十文件hash正确，仍不等于接受时记录的L0绝对源路径。v5没有改写这两次失败。

已接受 L0 run SHA `28eb8022d6eabe29ff7e2a589ca7a4d4df811f699cdbcf081b7aaa5357ad37f1`，toolpack SHA `f1039bace8b06cb46abf33266e7b38e1da8976379d33fa1a35b3b669dcf76856`，实际绑定 `TP07-L0-tools-v3/source/`。本轮独立检查其六项绝对路径：每项在before/after中只有一个匹配后缀，均是该原snapshot路径，且当前bytes与原hash相同；E八文件与L0六文件交集恰四项，hash全部相同。

## 修复闭环

新 `original_l0_validator()` 只从批准执行清单已经固定SHA的 `l0_toolpack`、`l0_run`引用进入；先核当前E八文件，再核外部L0 manifest的三个顶层键、schema1、固定task、恰好六文件和合法hash。共享四helper须与E pack一致。

source root固定为该外部manifest目录的 `source/`，不搜索latest或从当前工作区补文件。**执行任何L0代码之前**，函数核原run的toolpack身份、before/after一致，以及每个六文件的原绝对路径唯一、路径/hash与原run一致、当前为规范化普通文件及全部bytes匹配。错误E根/原repo根即使文件同hash也不能替代原recorded root。

随后仅compile/exec固定的 `runtime-validate.py` 已核字节，`__file__`设置为其实际原snapshot绝对路径；模块自然计算出的HERE/ROOT必须匹配。没有赋值patch ROOT、改包清单、放宽mandatory input路径、复制L0缺项到E8，或执行任意helper名称。返回前再核外部六文件和manifest；这七个外部路径加入E现有pins，进入运行阶段/结束时的原 `unchanged()` 检查。

`inputs()` 的变化仅是换用此原root loader和补充pins。原 `validate_artifact(l0_run.parent, l0_startup_sha, l0_toolpack_path, l0_toolpack_sha)` 的完整调用、`l0_value_contract_valid`和 **`live == saved validation`** 均保持，mapping/source比较及所有E依赖/二进制检查没有放宽。ef-run及ef-validate正式入口仍调用默认 `check_l0=True`，没有新增CLI跳过开关。后续纯value/profile/raw/cleanup helper也来自这一字节相同的原L0模块。

因此，本次是从正确历史执行根加载完整validator；没有重新认定旧L0证据或把路径漂移合理化。E源、九模板、L0七模板、已构建二进制和执行批准输入不因这项工具修复而需要重建。

## 新合同与实际预检边界

静态AST核对：原15个test方法均保持不变，仅新增 `test_original_L0_six_file_source_root_and_before_exec_mutations`，共16个计划方法。新fixture完全位于临时目录：四个共享文件复制当前受审E字节，两个L0专属文件是固定goldenhash的惰性文本，从不执行，也不从`.local`探测历史文件。

正控检查原snapshot拓扑、真实 `__file__` / ROOT、完整六项pins和模块自身toolpack入口。负控覆盖六文件任意漂移、两L0专属文件缺失、E根/原repo根替代、重复原路径、before/after漂移、漏项/多项/错task、外部重pin后共享hash分歧；通过mock compile确认相应拒绝发生于exec之前。没有用mock把完整L0验收返回值改成通过。

该合同正控证明有限loader接口，不冒称惰性两文件构成真实L0运行证明。**Low仍必须实际消费原 `f103…/28eb…` 输入执行完整 `inputs(check_l0=True)` 预检**，取得嵌套full validator与原saved输出相等的证据。16项合同和真实预检都通过后，root再开放后续DB；本报告没有执行或预判它们的结果。

继承[v4限定接受](TP07-E-tools-astra-rereview-v4.md)以及原E case/三项目图/参数来源/清理合同。E实际运行、D和完整EF-C仍pending；历史pre-DB失败、L0接受和新预检应分别记录。
