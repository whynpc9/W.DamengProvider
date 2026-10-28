# TP06 G3 interval leading 来源补充裁决

> 阅读提示：这是时间精度专项的历史设计补充，不是当前操作步骤。固定候选、状态和审批仅属于当时；当前行为见[时间精度说明](../../compatibility/TP-temporal-precision.md)，档案导航见[本目录说明](README.md)。

2026-10-08。状态：**design_accepted_for_tool_implementation / runtime_pending**。本文件只修正冻结 [coverage audit](TP06-coverage-audit.md) 中 `NATIVE_INTERVAL_REJECT_leading` 的无条件“DAY(2)列100天必须Overflow”预期；原audit及所有历史证据不回写。此次只读源码/已存资料，未运行build、tests或DB。

原设计把SQL目标列声明直接等同于驱动拥有的固定参数约束，这一推论不成立。已批准的CType21/flag2/rawscale0/mask0占位规则允许advisory绑定DAY(9) TO SECOND；它不提供DAY(2)列约束。此次没有定位到可核验的原始实库raw21占位JSON，不把已批准设计和合成decoder测试称为实际服务器观察；当前候选须记录本次真实describe。

## 来源和现有合同

- `DmParameterBinding.cs:79–112` 仅在 `Source=ParameterDescribe / TypeFlag1 / temporal CType` 下认定ReliableFixed，读取原packed subtype/leading。无可靠fixed时DT使用subtype6、leading9，默认fraction6，packed为 `0x696`；100天在此参数声明内合法。
- `DmSetValue.cs:1590–1620` 用冻结packed创建DmIntervalDT并编码；`DmIntervalDT.cs:51–58` 按该声明验证TimeSpan。没有读取SQL文本或另查表结构来猜列leading。
- typed参数入口的 `DmParameter.CloneForPlan/CopyMutableValue` 拒绝DmIntervalDT对象。不能为了验收把DAY(2) provider helper对象作为新live参数输入，或绕过plan直接调用内部writer发送。v7 FrozenCodec的已知fixed packed DAY(2)正控与leading溢出负例仍提供离线codec证据，不冒称实库fixed describe。

## `NATIVE_INTERVAL_REJECT_leading` 必须按实际来源分支

两分支均先核验本次nonce表的实际结果列metadata：CType21、DAY TO SECOND、leading2、fraction6；列来源是ResultColumn或准确catalog证据，**不得复制成ParameterDescribe**。先以TimeSpan99天成功写入并由fresh连接独立分量/文本与整数payload证明值精确，再用TimeSpan100天作单个UPDATE负例。记录执行前后行数、原值及schema，成功控制和目标操作的异常范围分开。预期不依赖错误message。

| 本次真实参数describe | 100天的严格预期 | 可接受证据 |
| --- | --- | --- |
| 原Source=ParameterDescribe，flag1，CType21，packed=`0x626`（DAY2/SECOND6），mask0 | 驱动 `OverflowException`；业务Execute未发送，允许此前Prepare/describe已经发送 | 原raw与本次冻结binding的ReliableFixed、leading2/packed相符；安全业务发送receipt、fresh行仍99天、schema仍DAY2/SECOND6；后续合法查询成功 |
| 原Source=ParameterDescribe，flag2，CType21，rawscale0，两个mask均0，已批准的精确占位 | 参数binding必须仍advisory、leading9/packed=`0x696`；不能要求驱动猜DAY2并预抛Overflow。实际DAY2列应由服务器拒绝越界赋值 | 本次实际业务Execute与服务器错误响应、独立服务器leading边界控制、fresh行仍99天、schema不变，且精确错误号已有root/Astra批准；在批准前为pending/failure |
| 其他来源、缺raw、坏packed、不同列声明或不匹配的binding | 不得自动选择上述任一接受分支 | 保留安全原始事实并失败，交root定点裁决；不能普遍允许所有advisory metadata或任意DmException |

99天成功控制也应记录实际参数raw/binding；同一逻辑case内若重新prepare的source变化，100天按其**本次**真实source判断，不能沿用先前source。失败后不改Session或field来制造恢复；执行合法查询并检查fresh状态。无论分支，100天写入成功、发生截断/舍入、旧行改变、schema改变、cleanup失败均为失败。

## advisory服务器拒绝的精确批准流程

新consumer/validator可以实现上述待审分支，但没有默认错误号白名单，也不能以“DmException且负数”“错误号与另一条失败一样”直接置candidate通过。

1. 首次实际遇到占位分支时，保存本候选/物理profile/nonce对象/列metadata/参数raw/binding/业务Execute与server response的安全关联事实，以及实际异常类型和number。只保留安全枚举和数值，禁止原message、连接串或SQL内容。
2. 增加最小独立服务器控制：通过无TimeSpan参数的合法SQL interval值，确认99天→实际DAY2成功、100天→DAY9成功，再记录100天→实际DAY2的真实服务器拒绝；每一步有独立row ID及fresh状态。此控制用于将错误定位到列leading范围，不能把失败CAST或语法错误误认为同一边界。控制失败/未执行不能自动授权参数错误号。
3. root/Astra独立核对：正常controls、准确列leading、两条错误真实来源、同profile和同候选、错误number及失败阶段、业务响应与非transport/auth/cancel/timeout/unknown分类、零数据变化和最终cleanup。随后才能将**此精确case/profile/列声明/来源分支**的错误号及证据SHA写入新的受审工具预期输入。该批准不新增产品能力，也不扩大原v11 caps的unsupported范围。
4. 未批准时consumer报告 `server_leading_response_pending_review`，validator必须非零退出且 `candidate_acceptance=false`；不得用完成characterization或cleanup成功掩盖pending。批准后重新冻结工具/预期输入，再由Low用同候选执行新证据目录的该case及受影响门。旧失败/待审收据保持原样。

若当前实际server并不返回fixed DAY2 describe，live“驱动DAY2预拒绝”应明确为未观察到；以v7已接受的fixed codec合同加上本次受审的advisory/server边界证据分别说明完整leading行为。不能临时改产品从目标表schema推导参数约束，不能假造flag1、改用户Precision作leading、开放provider-object参数或接受未知服务器错误。

TimeSpan原MinValue/MaxValue与1tick的六位不可表示拒绝、整micro邻极值正例及其余G1/G2/G3预期均不受本补充影响。工具完成后仍需整体静态审阅、独立冻结验证和同包shared/TLS实际证据；本文件不是任何实库case接受。
