# TP-01 shared v7：TSTZ分量oracle不足的限定triage

日期：2026-10-08。结论：`component_oracle_semantics_unresolved / readonly_diagnostic_required`。已有证据足以确认普通七位TSTZ值在若干用例中保留了原fraction/offset，而当前EXTRACT分量通道并非这些值的精确oracle；尚不足以在“服务器EXTRACT语义/返回metadata”与“数值读取”之间最终归因。必须补强独立numeric对照，不能把expected改成整数秒、容许微秒误差或直接跳过分量门。

本次只读全部三lane报告并做整数/Decimal汇总，没有执行dotnet、DB或改工具/产品。原v7 validator失败保持，不重写旧观察。root已另行授权High准备独立只读诊断；本文限定其SQL、证据与接受边界，不自动授权Low运行。

## 1. 完整事实与分布

安全summary SHA：`121929ab8e05c60c275fec0161ad9749506cddb3ca0279da43c7e0dc9497cf2d`。三lane均完成：core260、metadata334、extended106，共700条实际观察；各自进程及独立cleanup退出0，全部owned表fresh absent。最终characterization validator仍失败，首个issue是 `O_to_W:offset_0:oracle:O:server_components_exact`。

| 原报告 | SHA256 |
| --- | --- |
| core.json | `b0118aea2a059f4d14398fb081c036466737151f69b808aaae8ad9200b51677b` |
| metadata.json | `b47eef3d1f3eb4d3178674e020bf7f366c3aa02a5d0fa54bfc3a92c156bee66f` |
| extended.json | `c53fd5a548b1aff17d1e5cd03235cace2c67931cf3ee70c4890e56b9e828055a` |
| characterization-validation.json | `1ae97be5a94234ae78fc12077a9bbaba683ea375ad22f554bf58bb9bd549d792` |

以下按每个writer→reader oracle对象计数（不是用例数），并排除advisory明确未查询EXTRACT的观察：

| lane | 分量等于原expected | 存库值本身已不同于请求 | FF9精确但分量不等 | 分量查询报错 |
| --- | ---: | ---: | ---: | ---: |
| core | 190 | 52 | 78 | 28 |
| metadata | 254 | 2 | 0 | 0 |
| extended | 28 | 0 | 4 | 0 |

core的130个“有分量数组但不精确”=52个真实存库差异+78个FF9精确而分量不精确；另有28个分量查询异常，不能把158全部归入一个原因。52个存库差异均在旧O writer观察中，O/W reader各26；其EXTRACT与**实际server值**相符，仍必须保留相对原请求的精度差异。metadata两条为旧O的dual_scale6_loss写入后实际`.123457`，同样不是本次TSTZ oracle缺陷。

78个core oracle不匹配发生于TSTZ，O/W writer及O/W reader均可见；extended4条是O写scale8/9 TZ后两reader的同类问题。28个EXTRACT查询错误均为-6112，涉及TSTZ `dto7_max`、`dto7_max_minus1`、`local_max_0/330/480/840`、`utc_max_0`，每个ID在两writer×两reader观察重复；W侧记录为verified server response。不能用这些查询错误否认已由FF9/typed读回证实的存库值，但也不能将其改写为numeric通过。

## 2. 为什么当前分量oracle不足

同日期、同查询读取代码的对照：

- 普通TS7 `2026-07-23 14:15:16.0000001`：EXTRACT秒为16.0000001，O/W均如此。
- TSTZ7同local值：FF9为`.000000100`，W typed/generic DTO ticks为639204129160000001且offset准确；EXTRACT秒为16。0/+480/-300/+330分钟均见此现象，不是简单固定偏移差。
- TSTZ整微秒`.1234560`：EXTRACT为16.123456，精确。
- TSTZ尾tick5…9：EXTRACT秒成为16.000001；`.9999999`还可出现秒0而其余分量仍旧年月日时分，或在最大日期报-6112。表现符合内部微秒量化/进位的假设，而不只是“省略显示零”。这仍是推断，尚未记录EXTRACT结果metadata/原始数值cell，不能把推断写成已证明的数据库实现。

`Program.ReadOracle`逐列 `Convert.ToDecimal(componentReader.GetValue(i), InvariantCulture)`，没有本地floor/截断或ToString重解析。W的decimal/scaled-int路径是exact转换；但现报告未保存该查询的CType/precision/scale、GetValue实际CLR类型和raw cell，无法排除服务器表达式返回较低精度numeric metadata或对应解码差异。O/W相同结果是定位线索，不单独证明服务器wire内容。

现`actual_server.fields`来自FF9文本解析，不是额外独立的numeric EXTRACT或原始wire字段证据。FF9、精确DTO和原offset已经足以支持**这些已观测值未在存库/读取时丢tick**；不足以证明旧EXTRACT适合精确分量门。另一个TO_CHAR的SS+FF9也只能增加文本对照，不能冒充独立numeric证据。

## 3. 已交High的最小只读诊断

限定新 `diagnose-components` 入口与辅助文件，复用现CheckIdentities/SourceBinding/Configure/Open，验证TEST/schema/exact shared profile后、Reserve之前分派；不CREATE/INSERT/UPDATE/DELETE/DROP，不改session或实例设置，不引用已经清理的旧table。只对DUAL及合成绑定值查询。保留原三lane及validator不动，诊断结束新输出，不覆盖v7。

优先源：O公开DTO参数，`DbType.DateTimeOffset`、`Precision=7`、Scale未设：

```sql
(SELECT CAST(:v AS DATETIME(7) WITH TIME ZONE) AS Z FROM DUAL) S
```

W只读功能对照可采用显式字符串参数（DbType.String、Size80），避免推断DateTime及legacy微秒writer干扰：

```sql
(SELECT CAST(CAST(:txt AS VARCHAR(80)) AS DATETIME(7) WITH TIME ZONE) AS Z FROM DUAL) S
```

W必须保留内层VARCHAR cast，防止可靠describe把参数直接定成TZ27而与显式String冲突。`:txt`只含固定Invariant合成local日期、7位fraction和原offset。此源是服务器CAST功能诊断，**不是typed参数写入oracle**。O/W源只要未由Q0证明精确保留请求，该case即不可用于证明后续cast路径，不能修改expected为已经损失的源值。

### 四个独立SELECT（每个重新建立同一源）

下列`<SOURCE>`替换为上述完整括号子查询及S别名，参数每条只绑定一次：

```sql
-- Q0a：确认原TSTZ源值、原offset和真实temporal cell
SELECT Z, TO_CHAR(Z,'YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM') FROM <SOURCE>;

-- Q0b：确认显式TS9 cast保留原local字段和完整fraction
SELECT CAST(Z AS TIMESTAMP(9)),
       TO_CHAR(CAST(Z AS TIMESTAMP(9)),'YYYY-MM-DD HH24:MI:SS.FF9') FROM <SOURCE>;

-- Q1：原TSTZ numeric表达式，保留失败/量化事实
SELECT EXTRACT(YEAR FROM Z), EXTRACT(MONTH FROM Z), EXTRACT(DAY FROM Z),
       EXTRACT(HOUR FROM Z), EXTRACT(MINUTE FROM Z), EXTRACT(SECOND FROM Z) FROM <SOURCE>;

-- Q2：候选独立numeric oracle，来源仍是原TSTZ值的显式TS9投影
SELECT EXTRACT(YEAR FROM CAST(Z AS TIMESTAMP(9))),
       EXTRACT(MONTH FROM CAST(Z AS TIMESTAMP(9))),
       EXTRACT(DAY FROM CAST(Z AS TIMESTAMP(9))),
       EXTRACT(HOUR FROM CAST(Z AS TIMESTAMP(9))),
       EXTRACT(MINUTE FROM CAST(Z AS TIMESTAMP(9))),
       EXTRACT(SECOND FROM CAST(Z AS TIMESTAMP(9))) FROM <SOURCE>;
```

Q0a/Q0b/Q1/Q2必须分开，原Q1的-6112不能遮住Q0b/Q2。这里的cast是否保持local而非转UTC/session墙钟本身就是待测问题，不能预先假定或用客户端ToOffset“修正”差异。

最小向量：offset0/+480/-300/+330，各测fraction100ns、123456700ns及整微秒123456000ns；再offset0的Max、Max-1tick、Max-9ticks和2000-02-29 23:59:59.9999999。全为可构造CLR输入；不引入跨schema对象或新增服务器支持范围。

每Q记录实际参数/结果metadata（CType、precision/scale/type name/CLR field type）、GetValue真实CLR类型和精确decimal、单独GetDecimal结果、真实cached payload length/hash（必要hex仅限这些合成temporal/numeric cell）。不只保存Convert后的六个数字；不能按nominal CType填假长度。数字分析使用Decimal/整数，避免统计脚本的二次浮点舍入。

## 4. 严格接受与最小后续修复

Q2作为可用TSTZ numeric oracle必须同时满足：

1. Q0a完整local年月日时分秒/ns及offset等于独立规范输入；source_exact/source_offset_exact为真。
2. Q0b完整年月日时分秒/ns等于Q0a的**同一local字段**，cast_local_exact/cast_fraction_exact为真；不容许UTC转换、session平移、微秒截断、进位或饱和。
3. Q2六个numeric分量分别精确等于独立local输入，秒为`second + fractionalTicks / 10,000,000`，无tolerance。两driver读取结果及实际类型/metadata需分别记录。
4. 原Q1保留其原始值/错误及verified-response事实，不重新标为exact。

如果上述成立，root可批准**仅对已证TSTZ profile/表达式**追加cast-to-TS9 numeric oracle；主报告同时保留original_extract及新的exact_component_oracle，并让validator要求Q0证明与Q2精确结果。普通TS原EXTRACT链不改，旧O存库差异和dual-scale舍入继续相对原expected失败/表征，不能改用actual server值洗成typed写精确。

若Q0b丢fraction或转换时区，或Q2仍量化/报错，该路径关闭，报告diagnostic_inconclusive/unsupported_numeric_oracle；不删除numeric门。可后续单独调查EXTRACT结果的server-side numeric cast/text用于定位值何处丢失，但不能仅用SS+FF9替代要求的独立numeric证据。

本轮extended已完成的TIME>6、TIME24、year0、8/9 DDL与O写/旧W拒绝、TLTZ存在性继续进入独立ledger，不因本诊断追求新支持；尤其不扩大8/9写或TLTZ能力。TLS仍待后续独立任务。

## 5. 交付与下一窗口

High只准备限定三文件诊断，停写后Astra/root审阅精确SQL与report schema。Low按新freeze先离线构建及必要无DB分支检查，再由root明确授权只读diagnostic运行。身份失败、infra错误仍停止；server函数错误安全记录type/number，不输出message或连接。诊断不建对象，no_ownership_objects=true，不进行伪cleanup。

已有v7三表均已清理，无恢复动作。即使新诊断成功，也不把旧v7失败validator改写为通过；后续工具oracle改变须新输入、新运行与独立validator结果。本文完成后停止写入。
