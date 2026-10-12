# TP01 工具 v11 物理 profile 来源定点静态复核

2026-10-08。结论：**static_review_accepted / runtime_validation_pending**。本次仅接受工具证据来源修正，未授权 UTF-8 产品能力、未重新接受 TP01 基线、未关闭 TP03。没有运行构建、测试、validator API 或数据库；没有修改 product/test。

对照 `.local/temporal-native/verification/TP03-v5-package/frozen-tool-working` 的冻结旧工具，六文件差异符合 root 已限定范围：

- Program 的 O/W Open、报告以及 W cleanup 都经相同 ProfileInfo→CapturePhysicalProfile。只沿 GetConnInstance→physical ConnProperty 读取 version/msgVersion/timezone；公共 ServerVersion 仅用于一致性核对，没有 logical ConnProperty fallback。旧连接级 cache 保存的是完成身份校验后的物理快照。
- `encoding` 来源是 physical ServerEncoding getter，`negotiated_encoding` 来源是 physical 私有 serverEncoding 字段；只做大小写归一，明确 origin 为字段而非原始 STARTUP bytes。仅定点读取 encoding override，空值为本轮前提；缺字段、非空 override、编码不一致、非整数 metadata 或无效 version 均拒绝。没有遍历/输出连接属性字典。
- 新 C# offline fake 调用同一个实际 capture helper：logical 与 physical 的 version/encoding/msg/timezone 故意不一致，正例断言 physical 结果和 LogicalReads=0；十项负例逐次调用拒绝，再分别断言拒绝结果，没有将成功正例置于负例 catch 内。
- Python validator 强制完整字段集合、两个明确 origin、override=true、两种编码相等、类型与范围、TEST身份及精确版本。所有实际 W report、W cleanup、shared O report 都调用该门；C# shape 的来源、零 logical_reads 和十拒绝清单也被独立消费。手写 Python shape/全制品 fixture 增加了来源、缺字段、假 raw STARTUP、override、编码冲突等 mutants，未用产品 capture 生成 expected。
- 原 writer/getter/raw/NULL/metadata、ownership/cleanup、package/source/identity 和精度门的主体无差异。`eng/temporal-precision.py`、`tools/TemporalBoundaryProbe/contracts.py`、`identities.json` 与旧冻结副本逐字节一致。没有产品实现修改。

六文件实读 SHA256 均与派发锁定值相同：

| 文件 | SHA256 |
| --- | --- |
| eng/temporal-precision.validate.py | 7b4f23ee1281bfe4e8bc1a5004f39bcab8b5dce27072df1ce4e00d79ef23e72f |
| tools/TemporalBoundaryProbe/OfflineContracts.cs | 7a93bfbbcdc54a2b88be0baec0f7ed84411e996bfc384bd4914889d29dc1833b |
| tools/TemporalBoundaryProbe/Program.cs | e01dee6daae69272521df4b2bb2544af02818c438a473713fd7fbfae63ed0ff9 |
| tools/TemporalBoundaryProbe/README.md | b7961fdcb46af3829c779f8b67126fb2ecaaf819660e1f5fe083476513f9cf7e |
| tools/TemporalBoundaryProbe/test_producer_shape_contracts.py | 43ee069de10a78c249b4555a473aaf367f3ac9313c7b195c60819cb3ac0aa621 |
| tools/TemporalBoundaryProbe/test_validator_contracts.py | 743cfea2c9f38f2be6d613530dc80d368adc33d0fcbd7aad573fa1755dd78c19 |

本修正不将工具允许观察的编码集合视为产品 verified scope。Low 应对新冻结工具执行受影响 portable/归档/实际 C# shape 门，再使用原 v23 包执行新的 shared 与 TLS 完整基线。取得正确 physical 身份和逐操作7/6位证据并独立接受后，才可修改 compiled profile 的具体 scope 及后续 wiring 依赖哈希。旧 v10 报告、gb18030 来源缺陷标签和 v5 candidate 失败均保留，不回写、不拼接为新候选成功。
