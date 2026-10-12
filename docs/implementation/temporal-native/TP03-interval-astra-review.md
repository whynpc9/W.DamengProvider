# TP-03 G4 interval 独立静态审阅

结论：**changes_requested，单项R01：合法旧文本形态被收窄。** 算术、原子提交及公开签名未发现另外阻塞。只审本次冻结两文件；主codec/tool仍在编写，不纳入整体接受。未运行测试、构建或DB，未改产品。

冻结输入：

| 文件 | SHA256 |
| --- | --- |
| `DmIntervalDT.cs` | `e5af375dc4a2194954e945e4d42519bf1835b6e429f318e62b49ba8477c5181e` |
| `TemporalNativeIntervalAdapterTests.cs` | `a172d61ae5dd3d8559b54b8848fa933d1b58d9ac6acdd9a6c3e65bb47ba31df0` |

两SHA均实读一致。五个TP-02 pure core逐字与TP02-v3快照一致，既有TextTemporalContractTests未修改；22个DmIntervalDT公开构造/方法签名保留于113清单，无新增可见类型泄漏。

## R01：Parse拒绝合法旧连续前缀及完整literal外部负号

新 `DmIntervalDT.cs:385–398` 的复合subtype regex强制要求全部末尾字段。基线HEAD的 `SetDayToSecond:414–435` 明确在仅day、day/hour、day/hour/minute时返回，剩余字段默认为0；SetDayToHour/Minute、SetHourToMinute/Second、SetMinuteToSecond也同样允许连续前缀。

具体无损、可表示的旧调用：

| 调用 | 原精确值 | 当前结果 |
| --- | --- | --- |
| 默认 `new DmIntervalDT("1")` | DHMS(2,6)，1day、尾部0 | FormatException |
| 默认 `"1 02"` / `"1 02:03"` | day/hour及可选minute，其余0 | FormatException |
| 显式DH `"1"` / DHM `"1 02"` | 省略的尾部分量0 | FormatException |
| 显式HM `"123"` / HMS `"123:04"` / MS `"123"`，leading3 | 本subtype的leading与已给分量保持，其余0 | FormatException |

另新完整literal regex `:347` 只接受INTERVAL后直接引号。基线显式构造 `new DmIntervalDT("INTERVAL -'1' DAY(2)",QUA_D,2,6)` 的value token是`-'1'`，旧getAbsValue先取负号再去引号，得到-1day；当前新literal regex拒绝。新bare parser `:370–383`已有引号外单符号处理，可复用，不需改算术。

这些不是舍入、超precision或非法字段；root四项裁决未授权删除它们。当前新测试只含写满的复合文本，未保护这些旧形态。

**最小修复范围仍仅这两文件**：Parse允许每个复合subtype的合法连续前缀省略尾部并补0；不能允许空洞、尾随/重复separator、额外组件或把fraction挂到尚未出现的秒。完整literal支持原有引号外单负号，并与引号内符号共用“最多一个符号”验证。保留ASCII、严格声明匹配、leading/scale检查、InvariantCulture和安全异常文本，不恢复旧静默舍入/截断或明显错误分量。

必要新测试：默认DHMS的1/2/3分量；显式DH/DHM/HM/HMS/MS相应前缀；完整literal与引号外负号；缺省precision取显式参数成功；对应坏separator/双符号/多余字段仍拒绝。成功setup与值/ticks/格式断言不放入负例catch。High经root授权修复、停写新SHA后定点复审；原v1报告保留。

## 已审通过的静态部分

- 五分量构造checked毫秒→微秒、默认1574，days100和混合符号拒绝。TimeSpan经已接受宽整数core，Min/Max非整微秒拒绝；邻近可表示值精确。
- 各subtype按自身leading单位检查，不把H/M/S大leading当日内时钟；NormalizedUnchecked使用BigInteger再归一化。ToTimeSpan只在最终ticks范围失败，合法大interval仍可格式化/编码。
- ConvertExact按目标单位的整数quantum要求低位全零，随后重组目标字段及leading/scale；无舍入。encode先完成候选与24-byte输出，再Apply，使type/scale/fields同步；convertTo不改source。reset先解码验证完整wire再Apply，成功清旧残留，失败原对象不变。
- public字段每次Snapshot重读；signed magnitude与fraction微秒单位保持。ToHours/Minutes/Seconds使用宽整数只折一层，在MutationSnapshot允许既定中间形态而不换private subtype；溢出前不赋值。后续严格消费拒绝不符原subtype的形态，符合root已批决策。
- byte构造参数2/6作为明确约束，reset采用wirepacked；普通Reader已传actual precision，DB2N两处默认构造仍按已冻notes留TP-05，未在本轮抢改。
- 新负向断言均围绕单一目标操作，成功构造/状态快照在外；没有宽泛Record.Exception吞成功控制。六个Int32 literal golden独立还原为`-1,-2,-3,-4,-567890,1686`，与DHMS(9,6)一致；现有正向ticks/字段期望不是调用新encoder生成。未见明显C#访问级别/常量溢出编译阻塞，但编译结论仍待Low。

## Low门槛

本v1不交Low试跑。R01修复并静态接受后，与主codec/tool统一候选、统一source冻结，按root唯一窗口先TypeTests的TemporalNativeIntervalAdapterTests和既有TextTemporalContractTests，再同构建完整TypeTests；不得在活跃其它owner期间提前build。其他主任务的probe、身份、实库与整体TP-03验收仍独立执行。

本报告已同步root/interval owner，完成后冻结停止写入。
