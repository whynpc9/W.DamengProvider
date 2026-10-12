# TP07 E-v5：native interval服务器文本oracle窄修复核

2026-10-09。结论：**scoped_static_accepted_for_Low_offline_and_affected_runtime_gates / actual_interval_text_and_full_E_pending**。本轮只读四文件相对E-v4/E-tools-v6的完整diff、既有TP06来源与Low新快照，未运行SDK、测试、validator或DB，未修改实现、旧报告或接受旗。未发现本次范围内确定的编译/API/数值/来源阻塞。

依据triage设计 `TP07-E-native-interval-oracle-triage.md` SHA256 `eef1a7a454e89d5bc5654a4b06a0697c8e95adde3d8baf674caaff794dec7ec1`。root本轮明确允许采用已接受TP06 `ParseIntervalText(server=true)`正值子集的可选**内部+号**，比triage的无符号子集多此一项；不允许负号、外部sign、缺header/suffix或任意其它格式。这是明确范围裁决，不是声称本次失败的实际文本包含+号。

## 1. C#实际变化

`SixPrecisionCases.NativeFresh`只保留本次实际raw intervalWords，新增服务器正interval解析与诊断收据；SQL、typed写/read、普通24字节CType21/packed1686/ResultColumn/mask和原期望ticks不变。DATE/TIME格式及数值规则、NULL路径、非整micro拒绝合同没有改变。

完整grammar现在要求 `INTERVAL '\+?days H:M:S.micro' DAY(9) TO SECOND(6)`：ASCII day1…9位、H/M/S各1…2位、fraction恰6位、固定关键字/空格/quotes/suffix，\A/\z完整锚定。已删除原optional wrapper与可缺/变宽fraction；补零或不补零HMS都可接受，但未放宽成任意字符串。

解析的五个整数分别核day/hour/minute/second/micro范围，并与**同次raw的前五word逐项相等**；再用checked long计算serverTicks，同时等于raw ticks与固定case ticks。因day或运算超出CLR long范围会失败，不能靠溢出接受。旧raw计算没有被变成文本反推，也没有改请求值迎合服务器。

该owned合成interval的CAST字符串读取后，在grammar Require之前保存实际character length、limit120、原文及UTF-8 SHA并立即Write。超过120时只保存length/limit，text/hash均null，随后明确失败，未截断后当完整文本；这是本轮批准的有界诊断行为。新记录不读取/输出认证、连接配置或Exception.Message。失败后仍保持未完成oracle状态，不能因为字段存在升级为通过。

## 2. Python独立判定与合同

`ef-validate.native_oracle`仍自行从24字节做little-endian六int解码，核1686及分量范围、独立整数ticks。interval分支新增重算长度/limit/UTF-8 hash、独立ASCII fullmatch，再把文本五分量逐一与自己解出的raw比较；text ticks==raw ticks==registry期望。它不消费producer parsed fields作为oracle，也不再对缺fraction做ljust补零。

合法文本的ASCII约束使C#字符长度与Python长度一致；非ASCII/额外换行/缺wrapper等均拒绝。NULL提前进入原NULL路径，date/time仍使用原精确匹配，未要求它们伪造interval诊断字段。

新增一个合同方法及手写raw fixture helper，当前总17方法。正例包含已保存TP06 zero/positive server文本，并明确其余2天值、mixed-width/内部+及padded文本是手写合同输入，不是本次E未知文本。负例覆盖各分量合法但与raw不同、H/M/S越界、错误leading/SECOND、fraction缺失/5/7位、过宽day/HMS、符号、非ASCII、缺wrapper、尾换行、长度>120，以及hash/长度/limit/期望ticks/raw不一致。原16方法不变。这里只静态检查，未宣称17/17执行通过。

## 3. 源码与registry范围

E九源仅SixPrecisionCases改变，Program/TemporalContext及其它六文件共八项不变；N VARCHAR(32)与28-model offline入口保持。policy仅更新此一个E template SHA及派生aggregate，全部live case ID/输入/期待和计数逐字段与v6相同：shared180（60/12/54/54）、TLS158（52/12/56/38），总338。

| 文件 | SHA256 |
| --- | --- |
| SixPrecisionCases.cs | `fc8c779dbc279ec3ce423bd0848adc4f3cba92185821696e9065794644c5e9cd` |
| ef-validate.py | `0007fb886c5903309b3e06f8c1397563b4707cd7d55e08ff0601feecec66ffd2` |
| test_ef_contracts.py | `ac599c0f07c3ed4695cd8f48c9549bbda3eb9bb3c91b80ee63b9d76cdb9ae84f` |
| ef-cases.json | `d9e668828a2f62b3b3d933c323dac27b4ed9260e64d00d4f3409e62ada16ecbf` |

Low E-v5九源manifest SHA `765f9384ba74f633a4d1859b324fa636424c383cd30f5a76390a1bd6e69c9a9d`，aggregate独立重算为 `aab41c11ebffe5a7ecffbe3ced78fbfd0a786680f4ea6795bd2407d7e889d488`。E-tools-v7八文件toolpack SHA `725964f1d60d3510309f16a2133563a30d3b0c16de2f58eeeb341efdb8b20ade`，24源manifest SHA `8026dedca39468bc039d8b63aab59ed5b593f24b8dbd625fd0a8f11d15462e90`。九源与24源snapshot逐项实读匹配，八tool live字节匹配；无产品/common/TP06历史parser修改。

## 4. 历史与后续接受边界

旧E失败报告 `86eb65fe…`只证明raw/typed成功及server-text格式门失败，实际server_text缺失仍为缺失；本修复不能反填为某条1位HMS字符串。旧summary `cee39628…`、TP06两个profile已接受CAST与canonical getter的区分、85个对象fresh absent证据均保持。

旧cohort roundtrip60完整main/cleanup/validator0事实保留；不能把它改标签当新E-v5 binary运行结果，也不能把失败six里的24项局部完成当整个six接受。新freeze须经Low实际17合同、受影响C#编译/身份及root排定的新six/runtime门，保存本次真实text及独立parse结果；出现未知形态仍按完整grammar失败，不临时继续放宽。

本报告只接受有限源码修复，可供root开启后续Low窗口；没有自动runtime批准或恢复待办。E其余组、TLS、D前置门及完整C仍需真实独立接受；重跑/限定复用范围由root根据新源/制品身份明确安排。报告完成后停止写入。
