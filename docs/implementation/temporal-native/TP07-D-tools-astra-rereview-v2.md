# TP07 D tools v2：四项修复与checkpoint文档绑定复核

2026-10-09。结论：**DTOOLS-B01…B04_closed / scoped_static_accepted_for_Low_offline / D_runtime_not_authorized**。四项v1问题及两manifest路径配套修复已在源码层面闭合，未发现本次差异新增的确定接口/执行阻塞。仅静态读源、XML/JSON和哈希；未运行SDK、tests、runner、validator、producer、cleanup或DB，未修改实现，也未操作当前E残留对象。

## 冻结身份与范围

Low `.local/temporal-native/verification/TP07-D-tools-v2/` 的source-pack manifest实算SHA256 `8cce5fd046e26cf816d594992bb43071cd99ac63a4fca44ad12165cf4814fd71`，25-source manifest为 `e34a66f78d9dcef6f5701f7859b165f62e93fd14f2d0050719336fabb01e8647`。25项live/snapshot均逐项一致。

| 四个工具输入 | v2 SHA256 |
| --- | --- |
| `design-run.py` | `092e976e84f63f4a06a0e4f531d24905d4bcab5f62ddda6b1b69824b171637eb` |
| `design-validate.py` | `6e9fd825b2fb568ef9483218be3c733f0857375742f8ee8b304cef9145741bf8` |
| `design-cases.json` | `4f1e3bd310cf08b62d10e28fbdd3c4109b671eea22e37f0fcf436d3ce26420c6` |
| `test_design_contracts.py` | `245141dc2eff005056aa8bd1fd3ba94c1d7fca1061c42b5b1460106007f6f3c4` |

相对v1恰好5项改变：四个工具与已由 [D-v4 C#报告](TP07-D-astra-rereview-v4.md) 接受的 `ScaffoldCases.cs=0195d46f…`。其余20项不变，包括 `GuardedMigrationCases.cs=a2c5b8e…`、common/L0及独立decoder。旧25项snapshot全部匹配；[v1报告](TP07-D-tools-astra-review-v1.md)仍为 `3df7e3da622299890c24fcfd36b8bcfec8c21728db5f4242a2856555e62f90bd`。

## 四项问题闭环

| 原问题 | v2判定 |
| --- | --- |
| B01：macOS临时路径别名 | 所有checkpoint测试入口用 `Path(temp).resolve()`，fixture自身也规范化根，随后项目、generated文件、文档路径和relative计算沿同一根。没有通过捕获任意ValueError或放宽path escape检查掩盖失败。 |
| B02：raw/EF guard尾换行 | `emitted_guard_sql`仅对真实EF迁移路径追加固定macOS LF；G01/G02继续raw模板。G03/G04的实际audit hash与生成 `down_command_order[0]`同时绑定该emitted字节，且要求ordinal0/suppressTransaction。新增raw/EF各自正例、缺/多LF及改变内容/附加SQL负例。 |
| B03：guard关联与顺序不足 | 新 `guard_attempts/guard_outcome`核command/frame连续sequence、精确API/hash/正command ID、唯一attempt→completed或failed。G01核五个真实guard及2成功/3精确−20071；G02核四个不重叠有序command/frame窗口、单一guard、实际终态、affected/error及同command/window的verified response。G03连接实际guard与错误command；G04要求唯一guard→ALTER→history，两个变更均真实completed且SQL hash一致。 |
| B04：cleanup依赖坏CLI | `cleanup_host`只读取批准CLI manifest自身、task home与实际dotnet host pin。显式cleanup plan、`wrapped(role=cleanup)`和internal cleanup host都改用该入口，不读取/要求workflow nupkg、六tool文件或local-tool配置。main仍用完整tool_bundle校验，cleanup自身source/startup/build/candidate/ledger可信度与TEST权限不变。 |

B03已逐项对照冻结D-v3/D-v4真实收据：DesignAudit的raw/EF非查询API名确为 `ExecuteNonQueryAsync`；attempt/completed有SQL hash，failed仅用实际safe error，无需伪造其不存在的API/hash字段；command与ProtocolFrame sequence均按各自列表计数；G02 call边界从before snapshot之后到after snapshot之前。新的consumer按这些字段处理，没有套用E工具的另一套API命名。

G02与普通ProtectedState的RowOracle现都进入 `owned_read_evidence`，核row ID、owned table、原column V、唯一completed read-attempt及其physical identity，再核独立raw/FF9/typed期望。控制门不再只看producer outcome/actual_guard_rejected布尔。G03/G04保留fresh schema/value/default/index/check/history验证、row900显式删除及非原子DDL声明。

B04合同包括坏workflow文件但批准host完整的正例、cleanup自身host漂移仍拒绝，以及workflow gate失败后protected cleanup仍被尝试且主状态保持terminal_failed的runner合同。源码显示fallback清理未调用model gates；其作用限既有ledger，不授予新建D fixture权限。这些合同尚未运行。

## source/lock新路径与不可变父文档

配套依据为 [外部准备设计](TP07-D-external-preparation-design.md)，实算SHA256 `3da136b0efd3f4a1aaa5c1e33ca0a21ac22cf90cf0f055fee8c8725d6f12f1df`；C#配套接受报告实算 `68bd6f0da54f4218ef692f02fce09baadac4e666605f7e3909be6fc96e0357d3`。

Python continuation有限可变集合与C#同步，仅增加 `source_manifest_path`、`lock_manifest_path`，对应SHA此前已经可变。新 `source_lock_documents` 从每一对path/SHA实际读取文档，核schema1、同source_pack/replay_root、完整source_hashes、lineage/W及lock集合与所属startup完全相同；startup和continuation均调用，后者分别校旧parent和新child。保留原base source/locks/assets、provider和非generated host身份的检查未变。

因此child可以引用新不可变文件，同时不能用新路径+旧SHA、外国root/pack、缺项集合或覆盖旧parent过门。新增正例确认两新路径且旧文件hash不变；对应负例实际重算恶意child文档hash后仍要求集合/来源匹配，不只是测试外层SHA变化。generated目录、6/12文件精确closure、parent build/report SHA及原replay root约束继续保留。

## 后续门槛

44个计划合同方法仅由AST静态计数，未执行。可交Low按root排定的离线窗口运行全部合同；任一失败保留新失败收据并停止，不把本静态接受称作测试通过。外部D准备的193/195/202/208工作流与cleanup182/183闭包、原生依赖图及实际构建也仍需独立证明。

18字段plan、21个registry ID、固定profile/stage、mapping-v5与同W pins未扩展。L0/E模型门仍须真实未来接受引用；当前E残留对象的状态与清理属于另一个受控任务，本报告不参与也不据此放行D。即使离线44项通过，R05三checkpoint、R06真实guard/迁移、独立cleanup、D runtime及完整EF-C仍未接受。
