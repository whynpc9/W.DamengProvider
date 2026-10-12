# NEW L0 transaction v1 冻结源码审阅

2026-10-11。结论：**本轮三文件符合已批准设计，未发现阻塞源码接受的缺陷；可由 root 安排 Low 的新工具包静态核验及完整 Python 合同执行。** 本结论仅限源码，不批准 prepare、SDK、锁、build、grammar 或数据库 runtime。本轮只读取源码/JSON/补丁并用标准库解析 AST、计算文件哈希；未 import 项目模块、执行 tests/SDK/DB、读取 secret 或修改产品及旧证据，唯一写入为本文。

## 冻结身份与范围

审阅依据为 [设计](TP07-L0-transaction-v1-design.md)，SHA `2540f5cb767a43c488acd97f56eca4c02af60f27e7332862014bc70017e4eeda`。独立读回 High handoff `TP07-L0-transaction-v1-implementation-handoff/freeze-report.json`，SHA `dec94bb07a7f136040964f2badc5a85387dc1926b38769fc040588459343d7c8`；完整三文件 patch SHA `de6c6d4839bdd4380ddc0717183c05455644580e941b2b3a33e19215e00b78fc`。

| 冻结文件 | 实际 SHA-256 |
| --- | --- |
| `tools/DownstreamAcceptance/TP07/runtime-run.py` | `06fbff3637f39039e12fdcc5ad0c785857fa6e94d94d0c4651f6cb90099db8fd` |
| `tools/DownstreamAcceptance/TP07/runtime-validate.py` | `f1729710045f9f77dc369c183b1d53bc552fbaa5d107609eab747efebc85f5a2` |
| `tools/DownstreamAcceptance/TP07/test_runtime_contracts.py` | `b22f7f05adfdc5dcf0535ff26d1772e207dd8295b8295a110a1bb5bc1f25f00a` |

六工具实际文件全部匹配 handoff，aggregate 为 `c356a99780934334e145614171cd880d8c905d65df5bcce11d176891cb610983`。prechange 六份原件匹配原 pack `f1039bace8b06cb46abf33266e7b38e1da8976379d33fa1a35b3b669dcf76856` 的冻结来源。其它三工具、七模板及 handoff 的 225 个保护文件实际哈希均未变化；before/after 清单同为 `361bd710316c58b71dd56f8b9ccc81606038c702d25a3496db06c3112dc934ce`。

独立 AST 比较确认：原 runner 函数只变 `prepare/offline/run_runtime/main`，原 validator 函数只变 `validate_artifact`；原 24 个测试方法全部 AST 相同。两个消费方各自新增 13 个文件内 helper；只规范化 `need/require`、`read/load`、`POLICY/policy()`、`W` 的既有词汇差异后 helper AST 一致，运行时没有相互 import。三文件顶层 imports 均未变，没有 T1 隐含依赖或第七个工具文件。

## 来源链与阶段门

实际读回确认固定三 refs 和产品内固定十九 refs 全部路径/字节哈希匹配。产品 manifest 为完整 43 字段，SHA `400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8`；source approval 为精确七字段，SHA `66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f`，限定 `transaction_capability_and_w_retry_contract_tests_only`。代码拒绝旧 scope、其它批准、缺/多字段和 bool 冒充整数。

旧 dc2b manifest 对应实际完整 175，当前产品对应实际完整 178；独立逐文件比较得到 173 不变、DI/retry 两文件修改、事务 adapter/factory/capability test 三文件新增。178 aggregate 实际为 `2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c`。唯一 d13f patch 的五路径、原 unit-lock provenance、base/prepared lineage 和不变 W tuple 均保留。旧175批准没有被重解释为178批准。

`prepare` 在子进程前核固定来源和物理175/178；保留 archive→bridge→mapping→unit-lock→175等值，再真实执行唯一 transaction check/apply，核178后复制原七模板得到185。不存在整体复制178替代重放。六条 git receipt 独立检查顺序、完整 argv/cwd、自然 exit/host-exit 0、无 timeout/termination、实际固定日志 SHA。六个新 CLI 参数只能绑定有限三 refs。

source 保留旧175 map并新增产品178 map；185 map为178加七模板。lineage 和 lineage_artifacts 分别闭合为十项，旧批准字段仍指 dc2b。lock只可新增 L0 host lock形成186；startup须逐项等于186并带同一新 lineage/refs。runner 在 offline 入口和每次 SDK 前后、runtime 入口及各 main/cleanup 前和 profile 后消费物理来源门；validator 在成功结果处理前独立验证同一来源及六条 prepare 命令，结束再核保护项。原始175/current178、嵌套refs和模板都进入 before/after 保护，不只保护外层JSON。

**build host-lock 顺序有效。** 它先验证已审 lock manifest/source/receipt 关系，再验证 `lock_source` 中实际 host-lock SHA并复制到新 work，随后执行首个 `--version`，因此首次物理门正确看到186。没有根据声明生成假的锁文件；SDK仍按原 version→locked restore→build→grammar顺序执行。lock阶段仅在真实 restore 已产生 host lock时以其实际哈希构造临时来源投影；随后原 native graph/contentHash/archive/W检查全部执行，才能写 lock manifest。该临时投影不构成锁接受。

源码闭包严格；仅 provider/L0 两项目的四个 bin/obj 前缀可作为生成输出，其它源码、锁、props/targets或外部项目输出不能混入。路径门拒绝链接和 root 逃逸。原 ROOT、FLAGS、环境/进程实现、git30秒、SDK300秒、runtime300/cleanup180秒、internal-host、身份/owned-cleanup、TLS与 literal/raw/FF9/+100ns oracle 保持既有职责和实现；本轮没有扩大运行授权。

## 测试充分性与后续接受

新增十个方法以真实批准 refs/物理175/178作为正例；在独立对象副本上检验43/7字段、scope/类型、五路径/173差异、aggregate、185/186与三SHA串线；物理小样本覆盖字节漂移、链接/逃逸和有限生成目录；保护清单负例检查新增refs缺失。旧四条 git receipt明确被拒绝，失败专用六行变异校验 exit/timeout/argv/log 门。阶段 sentinel 测试从有效实际 toolpack进入，故意损坏来源，断言 prepare/lock/build/run和独立validator在进程前由具体来源错误拒绝，没有 mock SDK 成功。纯元数据 fixture中的合成 host-lock hash仅测试关系，不冒充实际锁或startup。

**34 是静态计划数（原24+新10），本报告没有测试通过数。** 当前无须返修。后续依次要求 Low完整Python自然通过、root新prepare授权与实际185/六命令、新native186锁接受、new locked build及实际35项grammar、root针对新startup与新工具包的runtime授权、全新shared12/TLS2原预算执行和独立完整验证，再由Astra/root收口。旧L0报告/启动/成功记录和所有历史失败均保持原义；NEW E338和D仍等待。本文冻结后停止写入。
