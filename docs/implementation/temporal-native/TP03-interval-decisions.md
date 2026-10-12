# TP-03 interval 四项限定设计复核

结论：root已裁决的四项与TP03-astra-design §6/G4及TP00的113签名清单相容，**无设计阻塞**。本次只读基线入口，未验收正在编写的candidate，未运行tests/build/DB；不改产品或已冻结设计。

## 1. 五分量构造

基线 `DmIntervalDT.cs:58–67` 采用默认leading2、sec6，最后type=6，却遗漏scaleForSvr初始化；毫秒乘1000也未checked。修正为DAY(2) TO SECOND(6)，packed值 `(6<<8)+(2<<4)+6=1574`，fraction仍微秒，millseconds仍毫秒输入，保留原public签名和参数拼写。

使用宽整数/checked完成符号、范围与精度检查后建立一致状态。days=100超两位即Overflow，不自动扩大leading；毫秒缩成微秒不容unchecked wrap。此为无损与元数据一致性修正，不新增秒内纳秒能力。

## 2. public折叠mutator与encode分离

基线 `ToHours:1159` 仅hours+=days*24、days=0；ToMinutes仅折hours到minutes；ToSeconds仅折minutes到seconds。保留每次只折这一层的public字段修改，不级联、不改变private subtype/leading/sec/scaleForSvr，也不暗中“升级”成H/M/S类型。

先宽整数计算并检查目标int，再一次提交相关字段。失败保持原字段；例如days=1的DHMS对象ToHours可得到hours=24而仍为DHMS，后续严格消费可以因不符合原subtype拒绝，不能为了让后续成功偷偷换type。

`encode(scale)`/convertTo独立捕获当前public字段，用宽整数按目标subtype重组和精确缩窄，不依赖上述中间态或连续调用mutator。目标缺失分量必须全零、fraction满足目标quantum，不能原地进位/舍入。成功后的type/scale/fields须一致；失败不部分改对象或输出部分编码。该要求与原G4一致。

## 3. byte constructor、reset与Reader交接

公开签名仍为 `DmIntervalDT(byte[] interval,int leadScale=2,int secScale=6)`。可选参数在运行时无法区别“省略”与“明确2/6”，所以二者一律是明确构造约束：先校验wire subtype/fields，再以所传leading/sec建立一致packed scale并作无损约束检查，不维持“字段2/6但getScaleForSvr仍旧wire值”的矛盾状态。合法值可精确收窄；超过传入leading或fraction精度则拒绝。记录为公开行为修正：默认byte构造不再默许超DAY(2)等leading的大值。

`reset(byte[])`没有额外精度参数，采用实际wire packed声明；先验证完整候选状态再整体替换。切换subtype的成功reset不能残留旧对象已不使用的days/hours等字段，失败则保留旧对象。不要缓存旧成功值忽略public mutation。

public Reader `DmGetValue.GetINTERVALDT:648–661` 已从actual scale拆lead/sec并显式传byte构造；保持这一事实，不为方便改回默认。`GetIntervalDtByMode:680–687` 的TimeSpan/provider对象选择不变。

**TP-05交接**：`Internal/Legacy/Dm.util/DB2N.cs:73`（转字符串）与`:689`（转对象）仍调用默认 `new DmIntervalDT(bytes)`。对应内部/受守卫路径也不能让真实大leading wire被默认2位无意收窄；TP-05应使用真实wire packed精度或等价窄内部解码入口，不能猜precision、取消检查或打开复杂类型/IntervalMode守卫。这两点不归当前interval owner修改。

## 4. string两种构造的声明语义

基线默认string构造`:118`先链显式DHMS/2/6，之后再补解析声明；严格化后不能照此顺序，否则完整合法HOUR/MINUTE/SECOND等literal会先被错误拒绝。

- 默认 `DmIntervalDT(string)`：完整SQL INTERVAL literal按其有效subtype及显式precision解释；声明省略部分采用既定默认。bare文本维持DAY TO SECOND、leading2/sec6。
- 显式 `(str,type,lead,sec)`：完整literal里真正声明的字段必须与参数一致；未声明的precision从参数取，不把省略等同显式默认。bare文本直接按参数解释。
- literal mismatch不能通过静默覆盖参数“修好”；真正超leading或scale不可表示也不舍入。YEAR/MONTH subtype不因公用常量存在而并入DT宽整数路径。

最小验证点由原High编码、Low运行：component1574/days100/checked毫秒；三个一层折叠及溢出原子性；byte约束缩窄与reset大leading保留/失败不变/换subtype清残留；默认完整 `INTERVAL '123' HOUR(3)` 与bare2/6；显式声明匹配、冲突及省略precision；Reader实际leading大于2的传递与DB2N后续内部fixture。

TP00清单中的22个DmIntervalDT公开构造/方法签名均保留，整体113公开helper清单不变。这里只补已批准的行为细节，不引入新框架。已向root、tp_core、tp_inventory同步；本文冻结后停止写入。
