# TP07 L0 FF7 adapter 与 constructor helper 维护静态审阅

2026-10-09。结论：**本次限定源码静态接受，无确定阻塞；可由 Low 执行新冻结包的离线合同、构建及实际 C# grammar 自检。** 本报告不接受新 L0 DB 结果，也不把 EXPANSION53 的直接 SQL 证据当成 EF L0 已通过。审阅依据已接受的 [FF7 表达式前置门](TP07-FF7-expression-acceptance.md)、[mapping 变更设计](TP07-FF7-mapping-change-design.md) 和 root 本次明确批准的窄窗。

本轮只读源码、静态 diff、Python AST 和历史文件 SHA；未 import/执行工具模块，未运行 SDK、测试或 DB，未改实现。仅新增本文；原失败、审阅与验收报告保留。

## 1. 冻结身份与改动范围

以下实读 SHA256 与派发冻结值一致：

| 文件，默认位于 `tools/DownstreamAcceptance/TP07/` | SHA256 |
| --- | --- |
| `runtime-templates/TP07LiteralProbe/Program.cs` | `37d4c3085f191abd8dbb04ae9c82adb7f2749f2ba25bacb198e2ede94382f5a6` |
| `runtime-run.py` | `d2c34db7184c8450daae7ce3c81c0b1a3d5e8df642cd42f2989ddb28f5a802ee` |
| `runtime-validate.py` | `d9ea39850a3d394f86f2fef54fe8330d732ea3a46d6823da3da74e84b9188d55` |
| `runtime-cases.json` | `cbf6752ce287247c5667fc309c57ef719f03d377c2caa3bf23ef8ab06ac0bb89` |
| `test_runtime_contracts.py` | `69a1d5d9ac7f1eb73ef37dc1ddd8b57a4c46300a22cade5fb7b559d5d0f6bcdf` |
| `constructor-probe/cases.json` | `2d6c2d8fefc3e647d68d77ea4bec10d5d294f9b6f0a2b90e26ab3ae811d27439` |

七模板按既定排序/hash/路径/LF算法实算 aggregate 为 `e0c551a3466f6681aa80c8044bdd480e5dd08d0b27ca0d8e41bf116738a11e95`，逐文件与新 policy 完全相符；仅 Program 变化，其他六模板保持原接受字节。六文件 Python toolpack 的 helper/decoder 继续原 SHA，仍需 Low 在新证据目录生成完整外部清单并由 root 固定其 SHA。

## 2. FF7 literal 与实际 C# grammar 接线

**四个声明七位的 shared L0 case 只接受 FF7。** 独立 `expected_literal()` 从固定 expected fields、整数 nanoseconds 和原 offset 生成精确全文：DT 为 `TO_TIMESTAMP('…fffffff', 'YYYY-MM-DD HH24:MI:SS.FF7')`，DTO 为 `TO_TIMESTAMP_TZ('…fffffff +05:30', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')`。literal 文本和 SHA 都须匹配；没有旧七位 TIMESTAMP、FF9、动态有效数字或失败 fallback。输入、case registry、shared12/TLS2、scale6/bare expected 均与旧冻结 policy 相同。

`Program.CheckLiteral` 改为三个完整 `\A…\z` 分支：准确七位 DT/DTO constructor、原 0…6 位 TIMESTAMP。格式串、大小写、分隔空格、offset形状均固定，不再用任意 token 字符集合放行。256 长度边界保持；匹配后只以 invariant `DateTime.TryParseExact` 检查日期/时钟是否合法，再核 offset 分钟、[-779,+840] 和 CLR DTO 范围。这里的 CLR parse 是**待执行合成 SQL 的语法/域守卫**，不承担服务端值 oracle；原 raw 整数解码、FF9、typed ticks/offset 仍独立核对。原六位与 bare/TLS 实际生成格式在该有限语法内继续允许。

`--selftest-literal-grammar` 在 Main 第一处分流，早于普通参数解析、CandidateGuard、配置/凭据和连接路径；只读取自身 DLL/deps 身份并调用同一个实际 `CheckLiteral`。固定 **35 项 = 12 接受 + 23 拒绝**，接受包括两种 FF7、−00:30/+14:00，以及 legacy0…6/DTO6；拒绝包括旧七位 TIMESTAMP、未知函数、FF9、六/八位、缺/多空格、错误 format、附加语句/注释、CAST包装/额外参数、无效日期/offset及长度。只有精确 `EvidenceException/LiteralSafety` 算预期拒绝，任意异常不会被当成通过；原 output 已存在不会覆盖它。

**必须实际编译执行，不能凭 JSON 旗或 Python regex。** runner build 在 locked restore/build 后运行本次 DLL 的自检；消费自然退出码、实际 argv、日志/报告 SHA、35个固定 case 的原文/hash/期待/实际/错误类型、12/23计数和实际 DLL/MVID/deps。任一不符就不能产出成功 build receipt。run 在 DB 前重核该收据；独立 validator 再次核同一报告、实际二进制和 source/startup 链，grammar report/log 加入运行前后必需输入。自检报告明确 `database_accessed=false`、`candidate_acceptance=false`、`literal_runtime_accepted=false`。

C# 的原 fraction Contains 没有升级为最终 oracle：独立全文 expected 仍决定最终 L0 literal 是否精确。新 Python 四case正例和负例覆盖旧 TIMESTAMP、FF9、少/多位、错日期/offset、空格/format及附加 SQL，负例逐项重算 hash 后仍要求被 `independent_generated_literal` 拒绝。另一新增方法检查实际 grammar artifact 的缺case、错误期待/实际/异常、DLL、范围、退出、timeout、argv和hash变异。

静态 AST 比对结果：旧 **22** 个 Python test 方法均存在且 AST 相同；仅新增上述 **2** 个，合计 **24**。这是源码计数，不是执行通过数。六文件 phase binding、TEST wrapper、dotnet环境与进程组、no-credentials、raw oracle、bare Overflow/0write、owned cleanup 的既有控制未放宽。

## 3. constructor helper 两项哈希维护

对照 `.local/temporal-native/verification/TP07-constructor-v3/source/`：当前 `constructor-probe/cases.json` 的 diff **仅** `frozen_value_validator_sha256` 从 `4fe33e…`→`d9ea39…`、`frozen_value_policy_sha256` 从 `68d24c…`→`cbf675…` 两个 scalar。constructor validator 本体仍为 `52303a4b53b74df34872e88efe8a21e96203b1210f41fbcc6abc8b8c7bea3d1e`；14合同测试本体仍为 `39304119ef10b14711806dae9f61fccf33e9b4b2b43c6c84b5d51b500528646a`。

独立静态审计该 consumer 的 helper 调用与本体依赖；以下 **16** 个纯 helper/支持函数与冻结 ctor-v3 AST 逐项相同：`_pairs`、`cleanup`、`ff9`、`integer`、`load`、`no_credentials`、`oracle`、`packet_decoder`、`policy`、`profile`、`require`、`route`、`runtime_identity`、`sha`、`source_closure`、`ticks`。imports、HERE/ROOT/POLICY_PATH 的 AST 相同；consumer实际读取的 W、EF、candidate manifest SHA、capabilities SHA 与 packed decoder SHA 均未变。constructor 调用纯值/profile/身份/清理 helper，没有调用新 L0 `case`、`expected_literal`、grammar 或六文件 acceptance 入口，因此两项 pin 更新不改变 EXPANSION53 的判定合同。

历史保护也实查：constructor v1/v2/v3 各原 **14 文件**快照逐文件 SHA 均匹配原 manifest，missing/drift0；EXPANSION53 r2 的 summary、validation、main report、cleanup report、execution receipt 五份 SHA 均与其接受报告相同。没有改旧14源包或用新 helper 回写/重新解释旧 runtime 接受。

该维护只允许在**新**源包/证据目录中由 Low 重跑 unchanged constructor **14** 项离线合同，确认新 helper 装配可用；本项不授权新 constructor DB 运行，不把新14合同结果记作旧 EXPANSION53 运行事件。

## 4. 后续验收边界

Low 仍须固定完整新 toolpack/七模板/mapping-v5/source/lock 链，执行 24 项 Python 合同、14项 constructor 维护合同及实际编译的35项 grammar自检，记录真实退出码、DLL/MVID/deps与前后哈希。新的 mapping-v5 M1回归/参数观察、锁图和 startup 证据获接受后，才按原窗口运行 fresh L0 shared12/TLS2。

本次静态接受不抹除原 L0 literal 丢位，不更换 W 包，不开放 TLS 七位、8/9写、TLTZ或其他 E/D 范围。fresh L0 的值、退出、独立清理或 source链任一失败仍保留并停止依赖门；完整 EF C、PR和发布仍未接受。
