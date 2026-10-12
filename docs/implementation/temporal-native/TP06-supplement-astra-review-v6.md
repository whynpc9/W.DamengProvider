# Supplement v6 interval 双通道静态验收

2026-10-09。结论：**static_accepted_for_independent_verification**。实现符合root批准的 [v5运行triage/v6设计](TP06-supplement-v5-runtime-triage.md)，当前无确认的静态阻塞。可交Low完整离线验证，再按root安排的新完整运行取证；此次没有执行SDK、Python测试、consumer或DB，没有修改产品/工具。

## 冻结身份

Low快照 `.local/temporal-native/verification/TP06-supplement-v6/source-manifest.json` 实读SHA256为 `86c09b66e7a528b9d49976b2e09279b35507ad3e49e636005d3ab64af3f82636`。17份工具SHA均实读匹配最终交接；root/owner提供aggregate `635f35e1dcb160c6efa70a80ff14c1df15de6ac869929c7a5076970a77fc5f10`，两个v11依赖不变。

关键文件：Observations.cs `ee3a13c2c7d01f32faa5e28653474eb7f9ad2c5e8ea070986aeb08e68467d319`；contracts.py `9d0ee3405e28d0cb81adb77f63176bb0380f71bf50271fc8de79071004b9e0f5`；test_validator_contracts.py `da0b9612bb953e5362ff282c7426bcace0742bd4db62112bcfa97f06814df8fc`；case-manifest `da2e1542f485042f68fe739474fe0367284de9fa109da7582fb87c36116dc37d`。validate.py、Cases.cs 0360…、Safety/Leading/Readers/Sequential/Program及产品包不变。manifest只新增设计SHA `282c9a4323f9f7fc3d13645a0df0b3723efcfd65a7c1fd80dc9d4f8851e40ef2`，SQL/API/case/vector集合未改。

## 真实描述与两个文本通道

producer的value及server guard都接收同一reader实际metadata；interval入口要求ResultColumn、CType21、width24、mask0、raw/effective声明相符，packed只认1574/1686。不会从server文字或列名猜leading，也不改metadata以适配文本。此前raw位置/ordinary格式/精确长度门仍先执行。

C# ParseIntervalText以ASCII数字和`\A…\z`完整匹配，强制完整INTERVAL引号及DAY(L) TO SECOND(6)。server允许day1…L及H/M/S各1或2位；value要求day恰L位、H/M/S各2位且无正号。两者均小数点后恰6位，整数检查days<10^L、hour≤23、minute/second≤59、micro≤999999；负零拒绝，限定词严格等于实际packed。没有Trim、剥离任意suffix、补小数、舍入或大小写/任意空白宽免。

value还由纯整数构造CanonicalIntervalText精确比较格式；该producer检查不代替数值oracle。独立Python从固定输入ticks和packed构造自己的canonical expected，再与W get_string逐字比较；server调用独立server parser，不能借该宽度规则放松W值文本。原始bytes独立整数解码、actual fields、typed ticks/offset、schema/声明及server数值交叉检查继续存在。

Fields的interval支路改为消费严格完整解析，不再先Trim/去suffix后忽略限定词；非interval解析分支逐行保持。原宽松SafeNativeText interval分支在当前真实调用链不再被使用，GuardReadText仅在非I/J路径调用它；interval均进入上述明确channel和metadata门。未改变产品public interval parser或formatter。

## 正反控制与诊断保持

真实zero raw及两条value/server literal连同SHA已写为独立固定控制：server的45字符unpadded形式只可走server通道，放到W value通道明确拒绝。DAY2/9、正/负/零、非零微秒和混合1/2位server显示的其它控制明确属于synthetic expected-only，不冒称本次实库已观察。

新增反例覆盖value少补零/多正号/负零、三位HMS、hour24及minute/second60、超leading天数、缺少/多余fraction、错误限定词/其它subtype、bare/残缺wrapper、双号/引号外号、Unicode数字、前后空白及尾随内容；另外修改文本数字/符号/微秒、raw packed/hash、schema或typed ticks仍必须失败。旧synthetic bare get_string改为独立完整规范fixture，是纠正工具expected形状，不是删除产品兼容承诺或扩大能力。

v5分步read_attempts、有限安全text shape/hash、固定own guard code与异常传播保留。能够安全记录某条文字不代表语法通过；诊断不能取代完整observations。旧value/server partial-guard控制已补传合法metadata，仍在目标文本guard处失败，不会误以缺metadata满足原反例。

## 原门与验证交接

R1–R4、v4公共State/真实异步发送0/退休连接与fresh原值恢复、G2有限read tuple、同包source/archive/DLL/MVID、TEST身份、ownership/独立cleanup及leading未批准号pending均未变化。没有新server SQL、额外case或业务重放。

旧148项Python方法保留，新增12组至160；实际C# helper检查保旧44加8至52，runner与独立validator精确ID列表一致。控制调用实际ParseIntervalText/GuardReadText，独立Python另用手写fixture与整数oracle；没有调用产品formatter/encoder生成expected。本review未运行这些检查，也未发现确认的静态接口/序列化错配；编译与运行门仍由Low负责。

Low须先完成新冻结的完整离线门，再由root安排shared/TLS完整Supplement。已观察的shared DAY9 zero显示仅为本次修复依据；其它输入或TLS出现集合外文字仍fail-closed并保留诊断，不能自动继承格式许可或改caps。旧v3/v4/v5失败、主矩阵接受及所有历史evidence不回写；完整Supplement通过前TP06仍未关闭。
