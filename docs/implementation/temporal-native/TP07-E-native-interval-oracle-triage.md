# TP07 E native interval：服务器文本oracle窄修设计

2026-10-09。状态：**tool_oracle_grammar_mismatch_identified / current_E_text_unobserved / narrow_fix_design_ready**。本轮只读本次E失败报告、接受的TP06压缩原始收据及相关源码；未运行SDK、测试、validator或DB，未修改实现和旧证据。

## 1. 本次失败事实

`TP07-E-v3-runtime-control/verification-summary.json`实读SHA256 `cee396287f52f1ec15dbbd6d66c18e9898f49790a3c106b4f140f0281145677a`；shared/six主报告SHA256 `86eb65fea1fc9b94a89e7ebbfbbb2d0e0f7317239b60999efd4a7bb889e1dc1f`。

shared roundtrip原cohort完整60项main/独立cleanup/validator exit0，issues=[]，这一范围的旧证据保留。six完成24项，在第25项 `EF-R03-shared-native-interval-value-sync`失败：实际SaveChanges返回1，fresh AsNoTracking得到TimeSpan ticks `1838451234560`，实际mapping为INTERVAL DAY(9) TO SECOND(6)。

独立raw已捕获 `0200000003000000040000000500000040E2010096060000`，六个little-endian int32为 `(2,3,4,5,123456,1686)`，即2天3时4分5秒123456微秒，ticks与请求和typed实体相符。失败点是 `native_server_text_finite_format`；attempt仍为attempted，尚未形成completed native oracle。

当前C#先执行Regex Require，之后才保存server_text；所以**本次E实际CAST文本未记录**。不能把历史样本或由raw拼出的字符串写成本次观察，不能断言本次错误只可能由某一个空格/补零差异造成。six主exit1、validator失败及未运行的后续组保持。60个roundtrip对象与25个six对象共85个已独立fresh absent；无本次遗留对象或待恢复动作。

## 2. 已接受同包证据说明当前grammar过窄

本轮实际解压只读的路径是 `docs/implementation/evidence/TemporalNative/TP06-accepted/`：

| 文件 | SHA256 | 已观察case（零起始索引） |
| --- | --- | --- |
| supplement-shared/supplemental.json.gz | `35d0ef8ff28fce435762e55555718c342dd92a14470355ff0e71286d71456fce` | 26 NATIVE_INTERVAL_zero、27 NATIVE_INTERVAL_pos |
| supplement-tls/supplemental.json.gz | `f3a87a858672e7377653aa8d1b295bdf48b4a5b098e07c509f6beebf03a4dd01` | 20 NATIVE_INTERVAL_zero、21 NATIVE_INTERVAL_pos |

两个profile均实际保存了服务器CAST：

```text
INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)
INTERVAL '1 1:0:0.123456' DAY(9) TO SECOND(6)
```

同收据provider get_string分别为补零的 `000000000 00:00:00`、`000000001 01:00:00`形态。二者不是同一个文本合同。接受的 `TemporalCandidateAcceptance/Observations.cs:83–97` 已明确server分支支持1–2位时分秒；只有非server的provider canonical分支强制补零宽度。

当前E C#及Python都对server_text要求HMS恰2位，故拒绝上述已接受服务器文本。C#同时还允许缺header/引号/suffix或缺fraction，Python再以补零fraction解释，范围反而在这些位置太松。本窄修只对本轮**非负DAY9/SECOND6**用例采用精确服务器子集，不把TP06其它leading/sign路径整体移植为E能力。

## 3. 固定语法与数值判定

C#可用如下完整锚定模式，Python使用独立fullmatch及整数解析：

```text
\AINTERVAL '(?<days>[0-9]{1,9}) (?<hours>[0-9]{1,2}):(?<minutes>[0-9]{1,2}):(?<seconds>[0-9]{1,2})\.(?<micro>[0-9]{6})' DAY\(9\) TO SECOND\(6\)\z
```

这里的正值形态包含零，不接纳符号；要求ASCII数字、固定大写关键字、固定空格、完整quotes/header/suffix、六位fraction。允许合法HMS补零或不补零，day长度最多9。**不**保留原optional prefix/suffix、bare time、任意sign或1…6位fraction/ljust fallback；不使用TimeSpan.Parse/文化解析、Trim、任意空白归一化、StartsWith/Contains或“只要包含数字即可”。如下一真实文本不在这个已批准子集，保存新收据并失败，不临时放宽。

匹配后分别转换day/hour/minute/second/micro整数，核day 0…999999999、hour 0…23、minute/second 0…59、micro 0…999999；五个数必须与**本次raw**解出的五分量逐项相等，raw第六word必须仍为1686，ResultColumn角色/type21/scale1686/mask与原普通24字节布局守卫保持。再用checked整数计算ticks，与原固定case ticks及raw ticks一致。不能只比较解析成功、文本长度或producer声明相等。

Python必须从raw独立struct.unpack与固定registry重建期望，并用自身有限parser核文本组件/范围和整数ticks；不得消费C#新输出的parsed components作为唯一oracle。新规则只处理interval分支；DATE/TIME现有格式/数值规则、NULL分支、ArgumentException非整micro拒绝、typed写/读取与同候选身份均不改。

## 4. 先保存有限观察，再做语义断言

在读取owned合成列的CAST结果后，先保存本次真实文本的length和UTF-8 SHA256；文本长度在原有限上限内（当前SQL仍CAST为VARCHAR(100)，工具上限不超过120）时保存原字符串并立即写report，之后才进行上述完整grammar和数值断言。不要为了记录方便重写成canonical文本。

超上限时只保存length/hash和明确omitted状态，不截短后当作完整文本，不输出长串，随后失败。NULL保持明确null来源，不拼造空interval。safe诊断记录可以保留格式不匹配的**本次有限owned文本**，但它不授予语义接受；未知异常Message、连接串、认证或任意用户SQL仍不记录。

建议字段为 `server_text`、`server_text_length`、`server_text_sha256`（或等义固定字段），其中字符串必须是本次reader返回值，hash/length由独立consumer重算。失败时attempt仍保留failed/attempted状态及固定assertion；不得因为新诊断字段存在就补写旧failure为completed。

## 5. 最小实施与合同测试

High只在已授权E的 `SixPrecisionCases.cs` native文本观察/interval校验处窄修，配套 `ef-validate.py`、对应合同测试及必要E template/policy hash；是否将纯parser提取为同文件小helper由实现者决定。无新框架、通用日期解析器、产品hook、SQL/query改变或对common/TP06历史parser的修改。现有成功SaveChanges和fresh typed路径不能换成ADO写入或改值。

有限正例必须含接受的历史server文本、一/两位HMS组合、固定当前输入的**synthetic** `INTERVAL '2 3:4:5.123456' DAY(9) TO SECOND(6)`及对应补零形式；这最后两条只能标为手写合同输入，不能成为本次E已观测文本。每个正例先与匹配raw通过，再做负例：

- 改day/hour/minute/second/micro中的任一数但维持合法grammar；必须因raw/expected不符失败。
- hour24、minute/second60、超过9位day、非ASCII数字、3位HMS、fraction5/7位或缺fraction。
- 缺INTERVAL、缺quotes、缺suffix、错误DAY(2)/SECOND(7)、正负号、尾部额外字符/换行或其它格式。
- length/hash错误、用canonical provider文本冒充另一数值的server文本、旧失败没有文本却补derived字段。

实际C# helper如有新增离线调用门须由root另行冻结接口；最小可保持现有项目编译与Python独立正反合同，再由真实six重跑确认来源。不得为避免实测失败新增“parser成功即通过”的旁路。

## 6. 验收边界

新freeze后Astra复核C#/Python语法一致但各自独立整数判定、记录先于Require、policy只改必要pin；Low按root新窗口编译/合同验证并执行受影响six门。未知实际text必须由新run保存，失败不能借历史字符串推定通过。新assembly/sourcepack要有新身份，不能把旧roundtrip60收据换成新cohort的运行结果。

旧cohort roundtrip60已完成且独立validator0的事实保留；failed six的24项完成只是失败run里的局部观察，不是整个six接受。后续复用或重跑由root按影响范围及身份链明确安排，最终mutation/generated/TLS/D与完整C均不从本修复推出。原两类旧CREATE失败/恢复、当前interval失败和85个对象清理证据全部只读保留。本报告不执行新DB或残留恢复，等待High窄修freeze后做源码复核。
