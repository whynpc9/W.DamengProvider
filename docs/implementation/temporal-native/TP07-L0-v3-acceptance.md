# TP07 L0 v3：mapping-v5 literal 实库限定验收

2026-10-09。结论：**L0_v3_shared12_TLS2_accepted / mapping_v5_literal_persistence_gate_accepted / full_EF_C_pending**。Astra 对 Low 的本次实际运行、冻结源码/制品、完整 case、原始值及清理收据作独立只读回读后，接受该 L0 限定范围。主进程、独立 cleanup 和 validator 全部退出0；14个要求 case 完整，无缺项或跳过。

本轮没有重跑 SDK、测试、producer、validator 或 DB，未改源码、旧报告、状态旗或本次运行制品；仅新增本报告。接受的是最终 mapping-v5 的真实 `FindMapping(IProperty) → GenerateSqlLiteral` 输出通过 W 命令实际持久化与 fresh 原列回读，不等于 EF SaveChanges、HasData/DefaultValue、inline translation、generated readback、scaffold、迁移或完整 C 已完成。

## 1. 实际 case 与独立值回读

| 本次 profile / 组 | 完整结果 | 证据边界 |
| --- | ---: | --- |
| shared 默认 DT7/DTO7，各 tick1 与1234567 | 4 精确 | 真实 mapping 生成准确 FF7 constructor；原 seventh 持久化保留 |
| shared 显式 DT6/DTO6 micro | 2 精确 | 原六位 TIMESTAMP literal，未升级为7 |
| shared 三种 bare micro | 3 精确 | 本次实际观察的六位兼容行为，见下节 |
| shared 三种 bare tick1 | 3 预期本地拒绝 | GenerateSqlLiteral 的 OverflowException；无返回 literal、0业务写入、fresh rows0 |
| TLS 显式 DT6/DTO6 micro | 2 精确 | 本轮 TLS 仅这两项；未执行七位或 bare case |

shared 共12项=9精确+3本地拒绝；TLS共2项精确。11个成功持久化 case 各有 literal 行和明确标注的 ADO 参数控制行，合计 **22个非NULL原列 oracle**。本轮将其 packed bytes 按独立整数位位置重新解读年月日/时分秒/nanoseconds/offset，重新计算 local ticks，再与固定输入、FF9、typed local ticks/UTC/offset对照：**22项差异0**。没有调用产品 codec、producer 或已交付 validator 来作这次回读。

四个默认七位 case 的精确服务端 FF9 为 `.000000100`、`.123456700`，DTO 原 `+05:30` 保留；实际 raw ResultColumn 元数据为 DT `(26,9,7)`、DTO `(27,11,7)`。独立核完整 FF7 literal 字符串与SHA，而非 fraction Contains 或 producer 的布尔声明。已生成SQL确为 `TO_TIMESTAMP(...,'…FF7')` / `TO_TIMESTAMP_TZ(...,'…FF7 TZH:TZM')` 的已审精确空格格式；没有换成手写参数 INSERT 充当 literal 结果。

所有成功 oracle 的 raw source 都是 `ordinary_result_cache_before_temporal_projection`，实际 ResultColumn 原始type/precision/scale与有效type/width/scale一致、mask0、schema scale一致；原始 payload长度/hash、FF9、typed结果和本次 row count均闭合。literal与ADO reader均来自同候选W。ADO控制独立标为 `ado_control_not_EF_SaveChanges`，不冒称 EF 跟踪写入或真实 shaper 验收。

三个 bare tick1 case 实际记录 `System.OverflowException`，`generated_literal=null`、`literal_business_writes=0`、`fresh_rows=0`、`persisted_exact=null`，持久化状态明确不适用；只有已完成的本地生成拒绝与fresh零行收据，没有 unsupported literal/ADO控制执行。

## 2. bare 六位观察的限定结论

| 本次 shared 裸声明 | catalog V scale | literal/ADO 原列 `(c_type,width,scale)` | 可接受描述 |
| --- | ---: | --- | --- |
| `TIMESTAMP` | 6 | `(16,8,6)` | 此实例、此声明与micro输入观察到六位兼容 |
| `DATETIME` | 6 | `(16,8,6)` | 同上 |
| `DATETIME WITH TIME ZONE` | 6 | `(23,10,6)` | 六位兼容且原+330分钟offset保留 |

bare mapping Precision仍为null，沿已冻结 fallback6政策；micro literal精确，tick1在客户端拒绝。**这些 bare tuple 仅接受为本次 shared profile 的六位观察，不是七位类型别名、跨配置通用服务器规范或 TLS bare 支持声明。** 原 producer `observed_unverified_bare_declaration` 与 validator `observed_pending_root_review_never_alias_seven_support` 保持不回写；本报告单独给出限定的观察接受。`bare_alias_seven_support=false` 不变。

## 3. 固定源码、实际加载对象与执行链

实际 shared 为 `WDM_PROVIDER_TEST`/同Schema、8.1.5.60/message21/utf-8；TLS为同TEST身份、8.1.4.6/message11/gb18030；各read/cleanup身份一致，encoding override empty，ordinary非BDTA。实际命令只使用既定 shared/TLS TEST wrappers；已冻结 C# 为 shared显式PlaintextAllowed、TLS RequireTls与原本机CA/client策略，未改变证书验证或实例配置。

| 身份 | 本次实际值 |
| --- | --- |
| W 包 | `0.1.0-temporal.20261008144915`；nupkg `da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d` |
| W DLL / MVID | `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980` / `aa23e673-957c-4b1e-bf0f-291ff60077e6` |
| EF mapping-v5 manifest / patch | `23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c` / `4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc` |
| 本次 EF provider DLL / MVID | `b09e3d7dd6c6751f40860713c828574ff8ad1aea8d3be8997e3afb511a395398` / `99e59f4a-c9a9-4c81-9b01-7dc7a1fd040e` |
| L0 startup DLL / MVID | `5d45b18a29c8fb6fa3bd7544b1978844c4771bb3cfd121dc5d35329fda9311a2` / `1e5416a2-8f92-4834-ae5f-19ae62e7f849` |
| startup deps | `f3ec91299339bc9a4e751f37b112ba5794a5a0012d80648afe4aa71400a96d0a` |
| startup manifest，schema1 | `30a12bdcfb842bb783c7e28e2c35036f12c88b74715b70e8a5665e40bd4fa800` |
| source manifest / L0 lock manifest | `b894f2c2e82c40c5bb47c14e53b4f552152ee276e537d7ea208f3a5a2bb69b26` / `8e0887ad8239c0bdbc2d7ce37fd2f911dcb500c3524bc8e425f9fedd01f4d9cf` |
| 六文件 toolpack | `f1039bace8b06cb46abf33266e7b38e1da8976379d33fa1a35b3b669dcf76856` |

源码链实读：固定base/bridge/mapping及其原文件SHA正确，prepare182项，lock/build183项，唯一新增本项目锁；13项非驱动锁记录、两个assets。prepare→lock→build→run→main/cleanup host→validator的toolpack、source/lock/startup引用一致。既有SDK日志为10.0.401；锁定restore/no-restore build与实际grammar调用全部自然exit0，原单节点/禁server参数保持。

实际编译的 grammar自检 **35/35**（12接受、23拒绝），逐case文本/hash/状态匹配固定policy，身份为上表本次L0 DLL/MVID/deps；不是合成JSON代替C#运行。两profile的main与独立cleanup实际loaded EF/W/startup身份全部匹配。独立validator命令实际消费同一startup/toolpack SHA，退出0、issues=[]。

本轮重hash运行收据所列 **213项**输入：before=after，当前missing/drift0；覆盖完整编译源、锁、assets、startup/EF/W二进制、工具及helper、阶段收据、lineage和grammar制品。没有沿旧DLL或旧overlay接受新结果。

## 4. 清理、证据与历史边界

14张唯一owned表（shared12/TLS2）均有本次absence-before-create、nonce/结构/CREATE确认收据；main finally、独立cleanup、独立finally三组均精确覆盖所属ledger，每张fresh absent/count0。shared ledger SHA `55d43255c11521201d102acc1bdbeab769c99a8935f417e8a386fcc54fab83d9`，TLS为 `739e08b3d99ab02ecf03fd28cb15069b05ddefe8752467369e1a467704087391`。四个主/清理进程自然退出0，无timeout/termination，无未完成清理。

以下原始证据位于 `.local/temporal-native/verification/`，本轮实读SHA：

| 文件 | SHA256 |
| --- | --- |
| `TP07-L0-v3-runtime-validation/verification-summary.json` | `b3b63c2d3ad2d87d1422eaf06359acfc953dc9c2234e86e8f5843f1921713680` |
| `TP07-L0-v3-runtime/run-receipt.json` | `28eb8022d6eabe29ff7e2a589ca7a4d4df811f699cdbcf081b7aaa5357ad37f1` |
| `TP07-L0-v3-runtime-validation/validation.json` | `9767f668d3ff214de78abd10c6c326c2ab233d02eabad769d7b0f19d6e949801` |
| `TP07-L0-v3-build/build-receipt.json` | `b2e0881cbdba2d7a838dc0fc5e3f116fc65aaa2ab381188bae8dda1936f3e29f` |

仓库内另有 [14项原始JSON归档](../evidence/TemporalNative/TP07-L0-v3/manifest.json)，manifest SHA `33e65cc01f81b51de8b9bca409a0598aec7e8a44c3619e8086806501e2fc9789`；本轮逐项核压缩文件SHA及解压字节与原始来源完全一致，原始收据仍为权威。

旧L0 v2首shared失败报告仍为 `ed5310c8feccfb0705b85a4bda12333a3ffd60a5ae9ea353820e37486757290d`，真实OracleMismatch/exit1不变。SCREEN25、EXPANSION53是各自固定候选/命令的历史证据；EXPANSION53五份主要runtime artifact SHA仍与其接受报告相同，没有用v3 L0改写其状态。原run/summary的待root或fullC=false旗保留，本文作为新的限定验收记录。

本门解除的是 mapping-v5 默认七位实际生成literal持久化的L0阻塞。后续实际EF E组（包括SaveChanges/shaper、HasData/DefaultValue、inline、generated值）和D组（scaffold/迁移/guarded downgrade）仍按原DAG独立验收；本报告不宣称完整C、所有服务器配置、发布或PR完成。
