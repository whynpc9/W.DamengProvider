# TP07 D-v3：真实读取收据单文件复核

2026-10-09。结论：**D_v3_receipt_scoped_static_accepted / combined_tools_and_runtime_pending**。只读原guarded Down设计、D-v2审阅、v2/v3十源快照及必要既有audit/oracle接口；未运行SDK、构建、测试或DB，未修改实现、旧证据或其它owner文件。尚在实施的D四个Python工具不在本报告接受范围。

## 差异与身份

相对Low `TP07-D-v2`，唯一变化为 `TP07TemporalDesign/GuardedMigrationCases.cs`，SHA256 `a2c5b8e774ad2d17415f9901b540aa525887672694a1d4414a5368b75fdf3848`；其它九源逐字节不变。

Low `TP07-D-v3/source-manifest.json`实读SHA256为 `d81d9d55bfe8319d1fd704425a593923588c19ee7e83c8366c21a76f190c4fad`，十个live文件与十个snapshot均逐项匹配。按repo-relative排序的 `sha256 + two spaces + path + LF`独立重算aggregate为 `631b607a3737779f594f7b7d1a3c289a50f70906d754b65de38e839621b56a5e`，与冻结一致。

## 同次实际读取的证据补齐

`CaptureRow`只调用一次现有 `LiteralOracle.ReadFresh(..., partialSink:evidence)`，将返回的TemporalOracleReceipt与该次调用填充的ReadEvidence共同存进RowOracle，并显式记录row ID/table/column V。没有另发一个“等价读取”填入证据，没有从SavedRow重造raw/FF9或给旧结果补来源。

`Snapshot`仍按实际 `SELECT ID ORDER BY ID`的完整行集取样；现先生成RowOracles，再从**同一批receipt**派生SavedRow(Id/IsNull/Ticks/Offset)。原每row存在且metadata scale等于actualScale的检查保留。因此Up前后、Down拒绝前后、成功Down前后及TLS6快照现在都能输出真实原列raw/FF9/typed/metadata/physical/read-attempt证据，NULL也保留实际NULL cell路径。

`Preserved`没有改成比较整个ProtectedState/RowOracles JSON；仍只比较scalar SavedRow序列、受保护目录/default/index/constraint/history语义及既有scale/history例外。新fresh连接/reader的artifact身份、不同调用的read证据字典不参与“值必须相等”断言，因此不会因为真实独立读的身份不同产生假不等，也不会把不同scale的合法迁移误当raw字节必须相同。

## G02四个控制的实际收据

原mutation/guard顺序和输入不变：清本次control表→empty guard；typed NULL row1→NULL guard；typed whole-micro row2→whole_micro guard；typed nonmicro row3→预期拒绝guard。SQL guard、错误码验证、参数值、offset、正常/异常分支接受条件保持；新增的是每次前后fresh只读计数/row oracle，未改变业务SQL或重排操作。

每个control记录before/after总行数、实际identity与指定row的完整RowOracle。empty无row oracle但有真实count；NULL/whole_micro/nonmicro分别覆盖row IDs `[1]`、`[1,2]`、`[1,2,3]`。`guard_call`保存实际ExecuteNonQueryAsync API、同guard SQL hash、返回affected或真实安全异常，以及本次audit Commands/ProtocolFrames前后边界；边界开始在before snapshot之后、结束在after snapshot之前，避免把新oracle读取当成guard调用。既有 `Program.Execute`仍负责StartRaw/EndRaw/FailRaw，verified −20071仍用本次frame窗口验证，并保留原case级verified_error。

这些是可供独立consumer核验的真实收据；最终D validator仍须核四控制期望count为0/1/2/3、前后rows/value不变、每row source/metadata和真实guard命令/错误关联，不能只接受 `whole_micro_NULL_empty_passed=true`。本报告没有审阅或接受尚未冻结的consumer实现，也没有把producer多输出字段等同实库已通过。

## Down门与Up顺序的明确边界

按root澄清，G00/G01/G02是**Down 7→6的前置门，不是Up 6→7的前置门**。当前Fixture先对自有六位表执行真实Up并通过before6/after7 `Preserved`及history/scale核验，再在独立control表运行G00/G01/G02，最后 `controls.Add(kind)`。migration `Upgrade7.Down`检查该kind controls accepted，再添加第一条guard，随后才AlterColumn；这一顺序未被本次修改。

因此不能用原设计末句“控制完成后ALTER测试”的泛读阻止已经具有真实前后保存证明的Up；它约束的是有损7→6 Down。G03仍要求verified −20071、后续ALTER/history mutation0和fresh protected state不变；G04仍只明确删除本次nonmicro row900后，运行同guard-first Down并核六位及history更新。v3没有改变这些门，也没有自动修值、忽略失败、扩大TLS七位或给通用generator加规则。

## 后续范围

[D-v2限定静态接受](TP07-D-astra-rereview-v2.md)其它结论继承。本次新增收据未发现确定API/编译/生命周期阻塞；可交root纳入后续完整D源码/外部CLI工具/四个runner-validator工具的组合审阅。实际SDK构建、R05生成模型及两个checkpoint重建、R06表达式/极值/guard错误/真正migrator Up/Down、独立cleanup与同包C门仍全部另证。Low当前十源freeze不是完整build闭包，也不是运行授权。报告完成后停止写入。
