# TP-03 core 静态验收 v1

2026-10-08。结论：**changes_required**。发现 1 项 P1、2 项 P2；修复后需新冻结、定点复审及 Low 独立验证。本文没有运行产品、dotnet、测试或数据库，没有读取 secrets，没有修改实现。只使用源码调用链、差异、SHA256 和独立整数/日历推导；下面的场景不是声称已经执行的失败测试。

已读 AGENTS.md、TP03-astra-design、TP01-profile-handoff.final、TP00-astra-design/contract-matrix、TP03-interval-decisions。TP00/01/02 接受不变；shared 700/TLS 374 是历史基线，不能当本 candidate 运行结果。interval 实现及其 wiring 由独立 reviewer 负责，本报告不重复验收。TP04 的公开 Precision、默认绑定/原始 DbType 冻结和 TP05 的 schema/完整 getter 矩阵不作为本轮缺陷。

## 发现

### TP03-CORE-001 · P1 · 新 NormalizeForWrite 重新开放 TimeSpan→TIME，有静默丢日风险

- 位置：`src/W.DamengProvider/Internal/Types/DmTemporalCodec.cs:44–46`；下游 `Internal/Legacy/Dm/DmSetValue.cs:1740–1765`。
- 场景：未显式声明类型的 `TimeSpan.FromHours(25)` 参数遇到可靠 fixed TIME(6) describe。`DmParameter.ResolveType:911–927` 选择 server 类型；`DmSysTypeConvertion:18–38` 保留 TimeSpan；`DmCommand:1713–1740` 将其交给 `SetResolvedObject`。新 NormalizeForWrite 的 temporal 分支没有 TimeSpan case，default 原样返回，随后 `new DateTime(timeSpan.Ticks)`→SetTime→TimeEncodeFast 提取日内字段，编码为 **01:00:00**，丢掉一天。CType22 也有相同入口。
- 根因：旧 `NormalizeForWire:90–94` 明确要求 TimeSpan 只能 CType21；新入口以文本延期校验为理由对所有未列出对象返回，却没有保留这个已有限制。其下游 TimeSpan 分支也没有调用新 `ValidateWriteValue`，因此 TIME(0) 的含小数 TimeSpan 还可绕过声明 scale 校验。
- 最小修复：在新 temporal target 分支显式拒绝 TimeSpan，保持旧 NotSupported 合同；CType21 无 temporal target 时继续走现有 interval 路径。无需新增 TIME/TimeSpan 能力，也无需改 TP04 绑定。
- 验收：新增实际 `SetResolvedObject` 正负控制，TIME15/TIME TZ22 对零、25h、负值、含小数 TimeSpan 全部明确 NotSupported；证明 `TimeOnly`→TIME 正例和 TimeSpan→INTERVAL 正例仍有效。至少一例经 public 参数的可靠 describe 选择路径证明不被前置转换挡住。拒绝前不得设置 InValue/发送业务 Execute。

### TP03-CORE-002 · P2 · 日期算术先换成总纳秒，合法大增量在归一化前溢出

- 位置：`src/W.DamengProvider/Internal/Types/DmTemporalLegacyAdapter.cs:187–190`；公开调用 `DmDateTime.cs:132–136,905–907`。
- 场景：从 `2026-07-23 00:00:00` 调用 `AddMinute(...,200000000)`，精确结果为 `2406-10-27 21:20:00`；`AddMinute(...,int.MaxValue)` 的结果为 `6109-08-15 02:07:00`。两者仍在 CLR 日期域，但 `amount * 60000000000` 先超过 Int64。公开 `DmTimeDdateAdd(...,200000)` 的合法结果 `2574-02-20` 同样在日增量乘纳秒时提前 Overflow。负 200000000 分钟结果 `1646-04-17 02:40:00` 也受影响。
- 根因：使用 long 没有避免“完整时长乘纳秒”的中间溢出，违背 G4 先安全归一化再更新数组的要求；错误与最终日期是否可表示无关。
- 最小修复：先对 amount 按 unitsPerDay 拆日商与余数，只把有界日内余数换成纳秒，再合并 nsOfDay/carry；或者使用足够宽的整数。保留同数组返回、ns、offset 与失败原子性，不修改 accepted pure core。
- 验收：上述四个固定 expected 向量，附非零纳秒和原 offset 不变断言；小增量跨午夜/负余数继续通过。对真正溢出的最终 calendar 保持明确异常且数组未部分写入。expected 使用独立固定年月日，不能只用被测 AddClock 的逆运算。

### TP03-CORE-003 · P2 · 严格模板入口先清除源字段，绕过禁止丢日期/时区的检查

- 位置：`src/W.DamengProvider/Internal/Legacy/Dm.util/OracleDateFormat.cs:784–795,816–822`；调用 `DmDateTime.cs:790–799,320–333`。
- 场景一：内部 numeric TIME 模板 `YYYY-MM-DD HH24:MI:SS` 解析 `2026-07-23 14:15:16`。ParseTemporal 仅要求时钟字段齐全，CompleteTemporal 随后直接把已解析日期覆盖为 1900-01-01；dtype 又被设为 TIME，EncodeWithTarget 无法再发现原输入含日期，写成 14:15:16。
- 场景二：普通 TIMESTAMP 模板 `YYYY-MM-DD HH24:MI:SS TZH:TZM` 解析显式 `+05:30`。CompleteTemporal 先将 offset 清为缺省，新 writer 后续检查不到原时区，接受普通 timestamp；非模板路径会正确拒绝该输入。
- 边界：NLS Builder 仍关闭，不要求开放该选项。这里是 TP03 已新增并由 WriterAdapterTests 显式配置覆盖的内部严格数值模板通路；G5 不允许用模板路径绕过源分量保全检查。
- 最小修复：在清理/补 anchor 前利用 parsedFields 检查不兼容源语义，拒绝 TIME 的实际日期字段及非 TZ 目标的显式时区字段。仅对确实缺少日期的 TIME 使用兼容 anchor；不要仅判断 offset 数值是否为零。
- 验收：在已有 WriterFixture 模板入口分别增加上述拒绝例，含 +00:00 亦拒绝；保留正常纯 TIME、普通 timestamp、显式 TZ、-00:30 和完整 FF7 控制。非模板与模板得到相同的字段丢失拒绝语义；不通过 ToString/Parse 中转。

## 其余静态核对

- DmDateTime 57 个、DmTime 19 个 public 方法/构造声明保持；OracleDateFormat 原 public 声明无删除或签名变化。byte[] 构造与两个 raw accessor 保持引用别名、packed 长度；数值 getter 每次重读。semantic dt 元素/整数组/dtype/scale 每次取值，未发现跨表示缓存或自动镜像。
- legacy12 和 int[] Fast encoder 采用微秒；扩展 public decoder 对非整微秒拒绝；DmGetValue 的 typed 路由同候选直接 DecodeForReader→ns Fields→CLR，保留七位。DmTime packed5/7、GetNano 微秒、raw12 零日期和无 offset 明确拒绝均有静态实现及针对性断言。
- shared exact version/msg21/gb18030 的七位前提、TLS 七位 closed、unknown legacy 及可靠 fixed26/27 六位兼容均有独立 target 逻辑。CLR 与 semantic text 共有 target/value gate；typed NULL 在值早返回前检查能力。fixed column/requested/encoded scales 分开保留；8/9 写、TLTZ、1582 排除策略没有通过整秒例外放开。
- DmGetValue 保留旧 GetDate/GetTime CType 集合、TIME1900、TZ typed 墙钟与 generic 原分派；TIME mode 使用整数 TimeSpan。合法非整 tick 转换失败在 CLR 投影，GetString 经 raw semantic formatter 仍能保留 ns。没有修改 Reader/Session/Transport/Pool/LOB 生命周期文件；本轮缓存恢复 fixture 不充当传输、取消竞争或分页实证。
- N2DB 已去掉 DmTime→timestamp 的 DateTime.Parse(ToString) 数值桥；未扩公开 provider-object 参数白名单或复杂类型守卫。非 temporal 分支不在本次检查范围。
- 测试保留既有独立官方 9/11-byte golden，以及微秒与 mutation 的固定 expected；roundtrip 仅作补充。负例使用 Xunit 单调用 Throws/ThrowsAsync，没有整段 catch 吞掉成功断言。缺少上述三缺陷的回归用例，不能以当前测试范围推定全部严格入口已闭环。

## 实读冻结身份

下列 13 文件 SHA256 全部与派发锁定值相符。只写本报告；未修改任何 product/test 输入。

| 文件（src/W.DamengProvider/ 或 tests/W.DmProvider.TypeTests/） | SHA256 |
| --- | --- |
| Internal/Legacy/Dm/DmDateTime.cs | 661f4822646ab7ccfbbbad082ed389ddc98955a09c5cd516c93f0b490fac85c9 |
| Internal/Legacy/Dm/DmTime.cs | 986d7ffeddccb7529ccd6d15a5e754f5880d30ed1089603db23abd46ba0b97bd |
| Internal/Legacy/Dm/DmSetValue.cs | d99dd73e5fd32b45e277073d93145b5fb7942758dd5893f48086618b259880ef |
| Internal/Legacy/Dm/DmGetValue.cs | 5427949c8f9a98c3ff0c20db46af198c1713dfd0f2813a1795737f9cb1a1a540 |
| Internal/Legacy/Dm.util/N2DB.cs | fb90a4844aa4a488494923d14094bbe9710d555b9ca4063c5c7165264e5fc5b4 |
| Internal/Legacy/Dm.util/OracleDateFormat.cs | 561226eb5a79f752a38d1559f2e4f1e257fab4815f8b79e0eb9fcde1caec60d6 |
| Internal/Types/DmTemporalCodec.cs | 76c2affa949d3243c247e7feb898b216ff92482088df82a507c725c9c26635bc |
| Internal/Types/DmTemporalLegacyAdapter.cs | 57fe342941ce9d8a57512e4322981e61ac1999fce0762cf1bc62a9dba35fdcea |
| Internal/Types/DmTemporalCapabilityProfile.cs | 27ad934f75f68c85f561f2bc013e3e0981c0180d1a5a966cf20f82edf69a8feb |
| TemporalNativeHelperAdapterTests.cs | d3f0f25678dc465807a532dda79fe5999109ba309590ab04161a61c9924d873e |
| TemporalNativeWriterAdapterTests.cs | 25d8ddeb9814ebb5615e79479851c0f2bebcc23f97552015168067655a526889 |
| TemporalNativeReaderRouteTests.cs | 6e59c12d4b781e4a75a8be14c6dca363229f7c130b7500e47095c696bc948f69 |
| ExtendedTemporalPrecisionTests.cs | ed032282151a295c08aaf113f50fd5eb7428bfd38584b44ea2ca059b7259ccee |

本轮可确认以上静态边界及缺陷，**不能关闭 TP03**。由原编码 owner 定点修复并增加回归、重新冻结后，再由独立 reviewer/Low 按新身份接续；旧失败和本报告不回写。
