# TP-02 v2 失败独立 triage

日期：2026-10-08。结论：`test_oracle_error_confirmed / correction_pending`。四个失败来自本轮新写的 legacy 手工 golden 漏掉fraction低7位中的 `0x40` 左移后形成的 `0x80`；没有发现该路径的产品编码错误。只需修四个新测试常量及其数学推导注释。原官方golden、产品代码、Luna fixture及v2失败证据均不应改动。

本次只读源码、Low摘要与TRX，并用独立位置整数运算核算；没有调用新旧provider encoder，没有运行dotnet、数据库或读secrets。本报告是新文件，原初审及v2复审报告保留。初审没有逐字节复算这四个新手工golden，漏掉了该预期错误；本次补充完整位域推导，不将先前静态接受改写为运行通过。

## 1. 真实失败记录

已回读并核对：

| 证据 | SHA256 |
| --- | --- |
| `.local/temporal-native/verification/TP02-v2/validation-summary.json` | `d98d115a2d8abd324f870a3f3eecdb8fad06e27d189d6f6888497eb951a54758` |
| `.local/temporal-native/verification/TP02-v2/targeted-results/temporal-native.trx` | `835aa9fa5c390897d90e69358590aca8f84e8ac3626b2e62243285cd4b0a1c61` |

Low摘要记录restore退出0、目标test自然退出1；TRX独立解析为56执行、52通过、4失败，0 skip/notExecuted/timeout/abort。四个失败均为 `TemporalNativeWireTests.IndependentLegacyBitVectorsUseMicroseconds` 的CType15、22、16、23，分别在TIME byte2或TIMESTAMP byte5看到expected `0x00` 与actual `0x80` 差异。DATE14用例通过。完整TypeTests按首失败停止规则没有运行；窗口已释放、输入与历史快照无漂移。这些事实不因本次定位为测试预期错误而改写。

## 2. 独立日期、时间、小数和偏移推导

受测字段为日期2026-07-23、时间14:15:16、fraction123456微秒；带时区布局额外offset+480分钟。纳秒语义字段是123456000，legacy存储单位是微秒，不是ticks。

直接按位域宽度构造位置整数，未执行 `DmTemporalWireCodec.EncodeExact` 或任何旧helper：

```text
dateWord = year + month * 2^15 + day * 2^19
         = 2026 + 7 * 32768 + 23 * 524288
         = 12,290,026
         → little-endian 3 bytes: EA 87 BB

timeWord = hour + minute * 2^5 + second * 2^11 + microseconds * 2^17
         = 14 + 15 * 32 + 16 * 2048 + 123456 * 131072
         = 16,181,658,094
         → little-endian 5 bytes: EE 81 80 C4 03

offsetMinutes = +480 = 0x01E0
              → signed Int16 little-endian: E0 01
```

逐字节拆分可进一步核对：

| 部分 | 计算 | 字节 |
| --- | --- | --- |
| 日期byte0 | 2026=`0x07EA`的低8位 | EA |
| 日期byte1 | 年高7位`0x07`，月7的低1位置bit7 | 87 |
| 日期byte2 | 月剩余3位`7>>1=3`，日23左移3位=`0xB8` | BB |
| 时间byte0 | hour14=`0x0E`，minute15低3位左移5=`0xE0` | EE |
| 时间byte1 | minute15剩余3位=1，second16低5位左移3=`0x80` | 81 |
| 时间byte2 | second16高1位=0；123456=`0x1E240`低7位=`0x40`，左移1=`0x80` | **80** |
| 时间byte3 | `(123456 >> 7) & 0xFF = 964 & 255` | C4 |
| 时间byte4 | `123456 >> 15 = 3`；剩余保留位为0 | 03 |
| TZ尾两字节 | +480分钟，little-endian Int16 | E0 01 |

旧源码可作独立来源交叉核对：`DmTime.cs` 的 `TimeTzEncodeFast(byte[])` 将 `(num & 0x7F) << 1` 放到时间byte2；`DmDateTime.cs:1285–1314` 的 `DmdtEncodeFast(byte[])` 将同样fraction位放到TIMESTAMP byte5。它们均支持上述推导。本次没有运行这些旧helper，也没有依据新encoder的actual输出来倒填expected。

## 3. 四个确切修正常量与旧值含义

仅限 `tests/W.DmProvider.TypeTests/TemporalNativeWireTests.cs:10–13`：

| CType | 布局 | v2错误常量 | 应改为 |
| --- | --- | --- | --- |
| 15 | TIME5 | `EE8100C403` | `EE8180C403` |
| 22 | TIME_TZ7 | `EE8100C403E001` | `EE8180C403E001` |
| 16 | TIMESTAMP8 | `EA87BBEE8100C403` | `EA87BBEE8180C403` |
| 23 | TIMESTAMP_TZ10 | `EA87BBEE8100C403E001` | `EA87BBEE8180C403E001` |

DATE14的 `EA87BB` 保持不变。注释应明确 `123456 & 0x7F = 0x40`，对应packed fraction开始处为 `80 C4 03`，避免只记一个未经展开的hex结论。

旧TIME字节 `EE 81 00 C4 03` 的反解为：hour14、minute15、second16均正确，但fraction为 `0 + 196*128 + 3*32768 = 123392` 微秒。它不是非法帧，而是另一个合法值14:15:16.123392，比目标少64微秒，即640ticks/64000ns。四个旧常量共享同一错误TIME片段，所以日期和offset部分不需要变化；把产品encoder改成输出00会造成真实精度损失。

这些四个常量是TP-02本轮新增的**人工legacy向量**，与已有官方extended golden `EA87BBEE81C8000000` 及其TZ变体来源不同。不能修改 `ExtendedTemporalPrecisionTests`、官方golden、O历史记录或v2失败TRX来使新期望显得一直正确。

## 4. 修复与重验边界

建议root仅授权High修改 `TemporalNativeWireTests.cs` 的上述四个InlineData及一处推导注释；五个case、fields、CType映射、encode/decode双方断言、四个其他测试方法均保留。五个产品核心、FixtureTests的R02修复、Luna JSON/generator、项目文件保持冻结。

High停写后，Astra定点核对差异和独立数学值，再由root建立新freeze；Low从TemporalNative目标门重新执行，成功后完整TypeTests。不得直接沿用v2的52通过或跳过四失败以宣布56通过。新输入结果单独保存，v2仍为52/56失败候选；本次triage本身不是测试运行接受。
