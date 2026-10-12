# TP07 E-tools-v9：shared 并发原值参数分支源码验收

2026-10-09。结论：**bounded_shared_original_parameter_static_accepted / offline_contract_and_mutation_independent_acceptance_pending**。没有源码阻塞项。root 可开启 Low 的离线合同与已保存实际收据只读重验窗口；本报告不接受整个 mutation runtime。

本轮只读 AGENTS、当前 ownership、来源裁决、v8 snapshot、冻结 Python、受控 C# 模型/调用及 v5 实际 JSON；只做文件哈希、AST 解析和收据字段投影。未运行 SDK、测试、validator 或 DB；仅写本报告。21 个合同方法是 AST 计数，不是通过数。

## 冻结输入

| 输入 | SHA256 |
| --- | --- |
| 来源设计 TP07-E-concurrency-parameter-source-triage.md | `18da65c2725358ce56b8fa1d80cf5a5c7145705979c9b2e39d8c903b486846a5` |
| ef-validate.py | `599d2790f93fede5cafe7a4ccd1373529268dc2486fbfe5accec8da0f159f5bd` |
| test_ef_contracts.py | `e2b104e39c0645c0db7c9ba21be7797fcc9ff31071e9c5ae35c8317321aba297` |
| E-tools-v9/toolpack-manifest.json | `86fb483c7184a6039c8f675154f11cae2ef4b13f1b86a3072a63abe8864b5bd7` |
| E-tools-v9/source-manifest.json | `0a00cfe899e13945c272aeb8c59936e3f68262b73639ddfbb9944e8e3d520ae7` |
| E-v5-runtime/shared/mutation/main/runtime.json | `78237fe1c4b59c7e2e2df97babf7765928b914def2ac6af9bb2795b49efb3d19` |

实读 v8 八项工具 snapshot 与原 manifest 一致；当前八项只有上述两 Python 变化。v9 八项工具 snapshot、24 项 source snapshot 及24项 live 均逐项哈希匹配；相对 v8 的24项仍仅这两项变化。没有依据本报告重建未变 C# 的必要。

## 源码裁决

新增 `concurrency_parameters` 只从 shared/scale7 mutation/concurrency caller 进入，并自行收紧到 DT/DTO、registry 精确值及 DTO offset330。它先联结两个 tracked 原始行、原 token、case/definition 和 winner/stale 的不同精确值，再判 raw flag；未用 flag2 推导 original 身份。四角色覆盖整条 command，DT public 映射 `[1,3,0,2]`、DTO `[1,2,0,3]`，N 为 changed/stale、ID 为1，两者不能发布 temporal binding。

每 stage 只有一个真实 EF Reader attempt/completion，调用原 `command_window` 与 `source`，保留 actual W route、EF CommandId、Prepare capture、最近响应、四项 operation identity、完整 ordinal/public 双射、request/public 一致及 name/inout 检查。winner/stale 的 SQL hash/length、各角色名称与位置必须相同，attempt/EF CommandId/operation identity 必须不同；published 四 ordinal 精确覆盖，逐字段联结本次 Prepare raw。没有跨 stage 借 binding 的宽松 fallback。

current 保持 fixed flag1；新增 original flag2 必须为 ParameterDescribe/Unknown、DT `(26,9)` 或 DTO `(27,11)`、scale7、mask/derived0，published 必须 ReliableFixed=false、ColumnScale=null、Requested/Encoded/Wire=7、ExplicitScale=true、VerifiedSeven。原值的合法 fixed1 路由仍要求原 strict tuple 和 fixed publication。全局 `raw_tuple`、`published`、TLS/fixed6 路径以及 seed 的原严格调用没有改动。

helper 同时核对 exact DbUpdateConcurrencyException、fresh winner 时间/N、行数1、catalog 前后相同及独立原始值 oracle；失败直接使 case 失败。async 要求实际业务 Execute 并匹配 operation identity；sync 没有新增“发送0”断言。

SQL 全文未保存；hash 只连接两次同形 EF 调用。原值并发角色仍是冻结模型、调用语义、实际值及结果共同支持的有限推论，不能据此宣称直接观察 WHERE clause。C# `MutationCases` 对 winner SaveChanges 返回值实际断言 `==1`，而真实 step 仅有 `call_returned`、没有 `affected_entities`。新 helper 明确保留这一证据边界，没有访问不存在字段或编造独立 count 收据。

## 实际结构与合同覆盖

逐组读取 DT/DTO × sync/async 的八个保存 stage：attempt 为13/15、38/40、63/65、88/90；每条四参数、public 映射、raw/published 角色均符合新分支。最近 Prepare response 的序号与 operation identity 对应；每对 winner/stale SQL hash/length 相同，实际长135。四个 async stage 各记录一次业务 Execute；sync 无发送观察，不推断为无发送。实际 winner/stale completion 均为 reader 完成，stale 的 EF 异常发生在其后，不能当成 local refusal。

新增三个合同方法，合计21个：四种 DT/DTO sync/async 完整手写正例同时进入 helper 与 `validate_case`，并保留 fixed1 original 正例；`rejects` 先调用完整正例再逐字段变异。负例覆盖角色交换/NULL/ticks/offset、四参数及映射/name、raw tuple 与 published 各字段、跨 case/operation/响应顺序/EF ID/completion、SQL 联结、async 发送及业务结果。DT 特有 Kind、provider、bool precision 和 seed flag2 也有负例；scope 和全局 strict 检查明确保持。手写 fixture 未被标为真实 DB，也未重建 SQL。

本轮静态未发现与保存 v5 JSON 的新增字段形状冲突。Low 仍须实际运行21合同，并为八条保存收据的限定重验生成新 artifact；root 再决定新工具包的正式 runtime 验证。旧 mutation 独立失败 `3691f2f0c8631f3e68ec287317955175b17beaed85ca1c42de4e0049f78849c3` 继续保留；不能以本报告、producer 成功旗或参数子集重验替代整个 mutation 接受。TLS、generated、D 和完整 C 不在本次接受范围。报告完成后停写。
