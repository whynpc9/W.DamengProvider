# TP07 mapping-v1 静态审阅

2026-10-09。结论：**changes_requested / R01与R02阻塞**。root已确认两项须修复后重新冻结；Low当前不能将本候选交SDK运行。这里只读审阅patch、全部7产品/4测试文件、P1实际观察及W调用合同，未执行SDK、测试、DB或修改源码。

## R01：缓存fixture已在行上，首次Read直接EOF

`test/W.EntityFrameworkCore.Dameng.Tests/TP07WReaderFixture.cs:49–50` 设置 `currentPos=0L`、`datasOffset=0`、`totalRowCount=1L`，表示已经位于唯一一行。但所有新materializer测试先断言 `Reader.Read()` / `ReadAsync()` 为true。

实际W `DmResultSetCache.do_next()` 在 `currentPos == totalRowCount - 1` 时推进位置并返回false。因此当前正常/非整tick/NULL/Unknown/TLTZ用例都会在首次Read处失败，未触达编译getter合同；这不是W产品Reader缺陷，也不应删首次Read来迁就fixture。

High须把cache初始化为真实before-first `currentPos=-1L`、`datasOffset=-1`，同时显式设置reader `m_CurrentRow=-1L`等对应状态，保留首次Read与终止Read断言。本轮未运行测试；上述失败由现源码分支可确定。修复不得修改W产品或将fixture改成自制reader替代W。

## R02：TLTZ非NULL literal可绕过关闭边界

`DamengTypeMappingSource` 仍将 `TIMESTAMP WITH LOCAL TIME ZONE` 注册到新的DateTime mapping；其 `DamengDateTimeTypeMapping.GenerateNonNullSqlLiteral:45–49` 只区分DbType.Date并读取Precision，对TLTZ(6)/(7)直接生成普通TIMESTAMP literal。实际参数写入的fixed mask4会由W拒绝，但HasData/default/inline SQL没有这道参数绑定门。

root确认这是本轮阻塞，不能以原legacy inline行为为由默许。应在新DateTime mapping的非NULL literal格式化前明确拒绝LOCAL TIME ZONE；实际mapping service取得TLTZ6/7时保留metadata、`GenerateSqlLiteral(null)`仍为SQL NULL，非NULL须NotSupported。加入真实mapping service测试，不能只断言helper。

DTO常规registry没有TLTZ候选，但EF公开 `WithStoreTypeAndSize`/Clone可将DTO mapping复制成带LOCAL TIME ZONE的StoreType，而现DTO literal也未检查该语义。建议在同一受审helper/两条literal入口覆盖合法大小写/空白形式及clone，不依赖初次registry选择来避免旁路。ordinary timestamp与保原offset的TZ不受该关闭规则影响；仍保留原合法literal及NULL控制。

## 其余范围审阅

- P1实际summary SHA `9e2f7b85e8d2be350aefb7c621e7d7e75d23194e96726d141d9c640bf9d08e8d`实读匹配，四进程退出0、14事实已记录。原DT/DTO及显式六位参数Precision0且未设，而Scale0未设；本改动准确补Scale，不引入无依据的普通时间Precision清零。DATE显式Precision0/Scale0是root单独批准的合同；decimal原映射未改。
- 默认mapping Precision7→Scale7，显式0/3/6/7/8/9及clone保Precision，bare facet=null采用批准的兼容Scale6并保StoreType/null facet。8/9参数仅为声明/metadata，不宣称写通过；literal8/9明确关闭。实际server裸类型默认仍须后续验证。
- 新DateTime/DTO ConfigureParameter先量化核ticks，再调用base并写Scale；DATE拒非午夜，不取Date截断。NULL同样生成声明；DATE非零facet明确失败。TimeOnly最高6及NULL非法clone边界由helper限制；TimeSpan取消Clamp并独立检查leading1…9与fraction0…6，负值/大时长使用BigInteger绝对值检查天数，不将leading写入参数Precision。
- TimeSpan→TIME在公开及protected FindMapping前显式拒绝，nullable归一，精确TIME名称不误匹配TIMESTAMP；TIME注册仅TimeOnly，default TimeSpan/INTERVAL与store-only/scaffold路径保留。其它类型source、decimal、连接/命令生命周期未改。
- ordinary/TZ literal以整数quantum检查后输出Invariant fraction，七位极值与显式六位负例有独立期望。DateTime/DTO/DateOnly literal统一拒1582-10-05…14；DTO保原offset且限制[-779,840]。DateOnly新增类型仅literal guard与Clone，参数行为保持继承路径。服务器是否保七位literal仍是单独runtime门。
- DTO生产GetDataReaderMethod唯一选择实际W `GetDateTimeOffset(int)`，Customize为identity。测试从真实mapping取得MethodInfo、验证declaring/参数/返回类型并编译转换表达式；没有GetString/Parse生产旁路。保留的静态Parse仅为原直接helper单测兼容，root已批准，不能把其存在当严格新materializer已验证。
- 新fixture使用唯一包的真实W reader及合成raw来源、手写packet常量，不调用encoder/Open/Execute；session/lease与反射成员签名按当前W核对，public/nonpublic字段均显式检查，异常不被吞作成功。R01修复后仍须Low证明整个fixture运行、getter拒绝后精确文本/继续读取及Dispose；当前静态检查不能代替该门。
- M02编译mapping delegate只证明mapping方法选择与W缓存投影，不等价完整EF生成shaper已运行。真实EF shaper/SaveChanges/query、literal持久化、scaffold及guarded Down继续pending，未扩大TPLTZ或profile能力。

## 冻结身份与后续

本轮实际manifest位于 `tools/DownstreamAcceptance/TP07/patches/mapping-manifest.json`，SHA `6e834be9157c2baa8e36579410b58993bf590ab9c979c1a0f5df86c1e87322b6`；patch SHA `d98deffbc38ba39c97151baa3972e16298f281f9d312fba01c981f781d0ed243`。实读base168/work175完整路径/hash均匹配，确为11项（4change/7add），无delete；原锁、P1执行source及W产品未变。High replay仍只计作者自检。

R01 fixture SHA `74ec4ab4ff512c9d1482bbe59b54920c57cbb67d77a763039fcb80412b5c4556`；R02 DateTime mapping SHA `80cf26b03c62994de99eb76bcd2f3feed33c49919c763d997591406f80cdf64c`、DTO mapping SHA `88de7f5a2043d2ad265e8536c2de728f3dc5678295ce278e0bb2a989f45f83e5`。保留这些v1输入与本报告，root开新High窄窗修复，再提供新patch/manifest定点复核。

unit项目图锁仍未准备，后续由独立协调流程处理；本报告不要求为静态修复提前restore或改变旧锁。报告完成后停止写入。
