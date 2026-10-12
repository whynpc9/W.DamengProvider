# TP07 E-tools-v8：TIME FF9单位换算定点复核

2026-10-09。结论：**Python_unit_fix_scoped_static_accepted / six_independent_acceptance_pending**。本轮只读当前实际TIME收据、v7/v8两文件差异及Low新freeze；未运行SDK、测试、validator或DB，未改源码/旧证据。无需为这一Python修复重建未变C#。

## 实际证据与根因

E-v4 runtime shared/six主报告SHA256 `7a517fb108837eecd9fbaa6580ee6cd0eff37ce67e7540e94cfc8ede23d44924`完成54项，producer/main及cleanup成功，但旧独立validator SHA `ffe37a6894bbd48259194c55e3acfc60bde59d3ca11874ed2a25d3133a014986`失败于 `native_TIME_FF9`。summary SHA `68bbb7a4b85f6b4a82e3abd19f4a62ca28467dcad500cf824a6420c10fcdffba`及失败旗保持；C#完成不等于独立six门已接受。

sync/async的native-time-value实际收据相同：raw `EE8180C403`、CType15、wire scale6，原列typed TimeOnly ticks为 `513161234560`，服务器FF9为 `14:15:16.123456000`。独立按原bit位置回读得到14时15分16秒、**123456微秒**。

单位关系为1微秒=10 ticks=1000纳秒。因此raw→ticks的既有`micro*10`正确；raw→FF9的fraction应为`micro*1000`，即123456000。旧Python `micro*100`错误产生12345600并补为九位`.012345600`，比实际值小10倍。C#使用ticks余数×100转纳秒，其源单位本来是100ns tick，**原C#是正确的**，不应跟着改乘数。

## 窄修与合同

`ef-validate.py`相对v7实际只改TIME精确FF9期待的乘数100→1000；raw解码、范围/保留bit、ticks公式、最终registry相等、interval/date/NULL、来源、runtime/cleanup接受及runner策略均未改。没有放宽字符串位数、忽略原断言或增加恢复/续跑框架。

新增第18个合同方法以已保存的上述raw/text/ticks元组作正例；原`rejects` helper首先实际调用正例，再运行变异。新增负例包括原错误十倍小fraction、十倍大/错误位数、不同数值/小时、wrapper/尾换行、ticks不符，以及手写hour24/minute60/second60/micro1000000/保留bit异常raw。最后一项同时改变raw和FF9为自洽的123457微秒，仍须在独立ticks与固定期望处失败。已静态核算这些raw变异确实落在所标域，未执行合同；旧17个方法不变。

## 冻结身份

| 输入 | SHA256 |
| --- | --- |
| ef-validate.py | `56bb8412a1ce98cefb0086a15f1389e99a2aaba22e02acc1df97e2219038fc10` |
| test_ef_contracts.py | `c3ae21705dda87ec0ae37adc04b00247cccd18e7edc32dd09e0c114b542d76be` |
| E-tools-v8/toolpack-manifest.json | `6a7eaa74e21bc338b04b0e6790b98e9cb569d2d82fa4a79a6207da8c0dbc7176` |
| E-tools-v8/source-manifest.json | `663f54d7bc3e9a3f5b77b5cbdba0222c9b1d2b8868946935e21fb0f81086fa97` |

八项toolpack live与24项snapshot逐项实读匹配。相对v7只上述两Python文件变化；E九源当前字节仍匹配原E-v5 aggregate `aab41c11…`/manifest `765f9384…`，policy、runner、common、W和编译startup `e2ab830b15ab0b3ca6a8a08927203cea0527d0a2198836debfb433550c7925e7`不变。

root可安排Low实际18合同、现有真实native收据的限定新校验，再按既有runner做新批准runtime；这些都应产生新验证artifact，不能重写旧失败 `ffe37…` 或将其改名为通过。shared roundtrip60独立通过保留原cohort；six54仍待新独立门，不从本次静态单位裁决直接接受。全部114个本次对象已有独立fresh absence事实，无需为本修复操作旧对象。其它E组/TLS/D/完整C仍按原依赖顺序另证。报告完成后停止写入。
