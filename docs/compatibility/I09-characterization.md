# 存储过程官方驱动对比与实库记录

本文分别记录官方驱动观察和 W 驱动断言，官方缺陷不会被当成本驱动的正确行为标准。当前使用方法见[存储过程说明](I09-stored-procedures.md)。

2026-10-15，服务器 `8.1.5.60`，官方驱动 `DM.DmProvider 8.3.1.47463`，SDK `10.0.401`。所有连接先校验 `USER` 和当前 Schema 为 `WDM_PROVIDER_TEST`，未使用管理账号。每轮使用随机完整对象名，创建前确认不存在，finally 只删除本轮记录的对象，再由新连接验证全部不存在。

## 执行入口

`tools/StoredProcedureProbe/Official.csproj` 是官方诊断，不把官方缺陷定义为 W 的合格行为；`Candidate.csproj` 引用当前 W 产品，执行显式行为断言，单独输出报告。当前工作树没有 test secret，命令用主 checkout 的现有 wrapper 读取唯一 TEST 变量；不复制 secret。

```sh
export DOTNET_CLI_HOME=/tmp/wdm-i09-dotnet
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build tools/StoredProcedureProbe/Official.csproj \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers \
  /p:RestoreSources=/tmp/wdm-consumers/.nuget/dm.dmprovider/8.3.1.47463 \
  /p:RestorePackagesPath=/tmp/wdm-consumers/.nuget
/home/air/Projects/W.DamengProvider/scripts/with-dameng-test.sh \
  dotnet tools/StoredProcedureProbe/bin/Debug/net10.0/StoredProcedureProbe.Official.dll \
  /tmp/wdm-i09-official.json

dotnet build tools/StoredProcedureProbe/Candidate.csproj \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers \
  /p:RestoreSources=/tmp/wdm-consumers/.nuget/dm.dmprovider/8.3.1.47463 \
  /p:RestorePackagesPath=/tmp/wdm-consumers/.nuget
/home/air/Projects/W.DamengProvider/scripts/with-dameng-test.sh \
  dotnet tools/StoredProcedureProbe/bin/Debug/net10.0/StoredProcedureProbe.Candidate.dll \
  /tmp/wdm-i09-candidate.json
```

离线 NuGet 路径是本机已有缓存，不是可移植前置条件；其他环境可正常恢复同版本包。Candidate 第二参数可按用例名子串过滤，例如 `cursors`。无连接时不应宣称集成成功。

## 官方公开 API 观察

| 行为 | 实测 |
| --- | --- |
| IN A=2、OUT B、INOUT C=5 | B=12，C=7；按名称把集合改成 C/B/A 仍正确 |
| 名称 | 大小写和 `:` 前缀均匹配；未知名称会危险地回退集合位置，W 必须拒绝 |
| 函数返回值 | 服务端返回槽固定第 0 位、名称为空；官方 ReturnValue 放集合首位得 304，中位却 -96、末位399，W 不继承此缺陷 |
| SQL 构造 | 过程 `name(?,?,?)`；函数 `? = name(?,?)`；无参纯 name 成功；Text `CALL "WDM_PROVIDER_TEST"."name"()` 成功；限定带引号名称成功 |
| NULL | OUT/INOUT 均返回 `DBNull.Value` |
| 普通多结果 | 顺序为 `[11,12]`、`[21]`；官方 `ExecuteReader` 返回时标量已可读，W 按 issue 契约延迟发布 |
| 多 RefCursor | 参数 Value 是官方内部 `DmParameterInternal`，父 Reader 的 NextResult 顺序展示两个游标 |
| 独立游标能力 | 反射用第二游标独立 statement 的缓存/描述构造 reader，与第一个交替读取各 20,000 行；计数和总和正确，包含跨 FETCH 读取 |
| 父 Reader/连接关闭 | 官方后续 Read 报 `6034`；事务 Commit 后已缓冲行仍可读，W 必须主动使旧游标失效 |
| 普通结果加游标 | CREATE 成功，但执行报服务器 `-7160`；不宣称该组合可用 |
| 服务端错误 | `RAISE_APPLICATION_ERROR(-20009,...)` 保留错误号，旧 OUT=-1 不变 |
| 显式事务中的过程 | 普通过程后 rollback 最终0行；内部 COMMIT 后再调用官方 tx.Rollback 仍返回成功，但新连接见1行；不能以 tx 对象可调用判断事务仍有效 |

普通结果与游标混用的服务器限制亦见[达梦 DMSQL SQL 语句，第 5.4 节](https://eco.dameng.com/document/dm/zh-cn/pm/dm8_sql-sql-statement)。公开参数语法参考[官方 .NET 指南](https://eco.dameng.com/document/dm/zh-cn/pm/net-rogramming-guide.html)。这里以本机行为判定支持范围，不把参考文档代替运行证据。

## 描述与输出协议

以下只读取身份核验后本次命令的 statement 元数据/执行 buffer；没有采集 STARTUP/LOGIN、认证报文或连接串。

- 协商 `msgVersion=21`；执行请求 opcode=13，响应 opcode=187。
- 普通 IN/OUT/INOUT 的 `GetParameterMode=1/2/3`，`GetInOutType=0/1/2`。
- TypeFlag 观察到 1 和 2，与参数方向不是同一个枚举；不可强制所有输出为1。
- INT 描述 CType=7/Precision=4/Scale=0；decimal=9/28/9；TIMESTAMP=26/9/7或9；TZ=27/11/7或9；BLOB=12/2147483647/0；CLOB=19/2147483647/0；RefCursor=120/32768/0。
- 游标第一次 Prepare 可呈 OUT/inout=1/typeflag=1；同一过程执行后，再 Prepare（官方驱动同样如此）呈 INOUT/inout=2/typeflag=2。仅此已实测游标模式应兼容；不能放宽普通标量方向冲突。
- RefCursor 在 Prepare 后尚未拥有子 statement，绑定执行时为每个参数创建独立 statement；`GetInValue(0)` 为4字节 little endian stmt ID，例如 `01000000` 与 `stmt.g()=1` 一致。线上游标绑定方向为2，携带该输入句柄。
- 每个标量输出槽为 int32 元素数量（本轮均1）+ uint16 长度 + payload。两个 INT 输出12/7的完整 body 为 `0100000004000C00000001000000040007000000`。
- 两个 NULL 输出 body 为 `01000000FEFF01000000FEFF`。
- 两个游标加 INT=99 的 body 为 `01000000FEFF01000000FEFF01000000040063000000`。游标的 `FEFF` 不表示普通 NULL，而由预绑定 statement 获取数据。
- 尚未真实覆盖元素数量不为1、其他长度标记、未知复杂类型；这些不能从遗留分支推断支持。

## 精度和 LOB

DECIMAL(28,9) 输出精确为 `1234567890123456789.123456789`；BLOB 输出 `byte[]{00,11,FE,FF}`；CLOB 输出 Unicode string `I09 中文 CLOB`。

时间需区分三条路径：

1. 直接把七位字符串 CAST 后赋给过程 OUT，本机服务端结果只有 `.123456000`。
2. 官方过程 typed IN 即使 descriptor 是26/27、scale7或9，实际请求把 IN 改为16/23、scale6；`.1234567` 在服务端 TO_CHAR 中变为 `.123457000`。这是官方过程绑定降级，不能作为 W 七位精度的允许误差。
3. 官方 **Text typed INSERT** 向本轮 TIMESTAMP(9)/TZ(9) 表写入 `.1234567`，过程 SELECT INTO 输出，服务端 TO_CHAR 为 `.123456700`，官方 CLR 也精确 `.1234567`，偏移 `+05:30` 保留。W 候选验收沿用这个独立官方写入 oracle。

第3条的输出 packed bytes 是 `EA077D4CC47999B70E`（9字节）与 `EA077D4CC47999B70E4A01`（11字节）；均为 CType26/27、scale9。非整100ns的真正九位值及超过64MiB的实际LOB上限拒绝尚未实库覆盖；大于 CLR decimal 的数值、100KB LOB 的后续 W 验证见下文。

## W 候选与保留的失败

首轮 `/tmp/wdm-i09-candidate-r1.json` 退出1：26例通过、7例失败；9个精确自有对象均由新连接确认删除。四方向/重用、任意返回槽位置、无参、NULL、精确 decimal/时间/LOB、普通多结果与提前关闭的延迟标量发布、服务器错误、严格名称、显式事务拒绝通过。预取消、发送后取消、超时均没有发布输出，新连接观察业务行数均0。

`/tmp/wdm-i09-candidate-cursors-r2.json`、`...-r3.json` 用安全 stack/脱敏消息定位到后续游标 ExecuteReader 报 `Procedure parameter direction conflicts with server metadata.`；第一个同步游标完整读取成功，后续均失败。官方重复 Prepare 的游标 inout=2 为该问题提供了对照，不能把失败从记录中移除。

官方与 W 的报告、结论分开；下文记录后续修复和最终候选验证。

后续保留记录：

- 修正仅 RefCursor 已注册句柄的 INOUT 描述兼容后，`candidate-cursors-r4` 的同步/异步8例均通过，exit0，精确清理成功。
- `candidate-r4` 的33例全部通过，但最终清理核验连接出现 SocketException（当轮未保留 message，无法据此归因）；删除循环已完成。另用官方新连接只读查询当前 Schema 的 `I09W_%` 对象数为0，未按前缀执行删除。这轮仍保留 exit1。
- `candidate-r5` 扩展39例，37例通过，两个 interval 组合输出报 `Interval metadata contains invalid declaration fields.`；精确11对象清理成功。官方同声明输出成功，DT 描述 scale1574，YM scale326，此差异须处理。
- R5 新增 Prepared/PrepareAsync 后改用相同参数描述但不同过程，结果正确。新取消测试先由过程插入专用 marker 并 COMMIT，再继续未提交业务写入及等待；独立连接观察 marker=1 后才执行 token cancel / 同步 Cancel，避免把固定计时器误称发送后取消。两者最终 marker=1、业务0；预取消 marker=0、业务0。同步/异步timeout也观察 marker=1、业务0；同步timeout实际返回普通 DmException 包裹 SocketException(TimedOut)，异步返回 DmTimeoutException。已收紧后续断言，二者不能被笼统“发生异常”替代。


## 合并主线前 W 集成结果

`/tmp/wdm-i09-candidate-r7.json`：**55/55 通过，退出码0**。本轮使用上述 Candidate 命令，构建日志 `/tmp/wdm-i09-candidate-build-r7.log`；加载程序集 `tools/StoredProcedureProbe/bin/Debug/net10.0/W.DamengProvider.dll` 的 MVID 为 `82905a60-8469-4bc5-b51a-342a021d0675`。这是本轮实际受测程序集身份，不是后续运行的固定门槛。

本轮对象精确名称由报告的 `reserved_name`/`owned` 记录：前缀 `I09W_03DFA874EC734720` 下的 DATA 表、P/P2/V/Z/R/C/T/TL/E/W 过程以及 F 函数，共12个。全部逐名删除，新连接逐名查询确认不存在；没有按前缀删除。

| 已验证行为 | 最终结果 |
| --- | --- |
| 同步/异步 IN/OUT/INOUT、命名重排/前缀/大小写、同参数重复执行 | 精确预期值 |
| 函数 ReturnValue 在集合第0/1/2位 | 均304 |
| Prepare/PrepareAsync 后改 CommandText，参数描述相同的另一个过程 | 使用新过程，B22/C11 |
| 无参、NULL、普通 ExecuteScalar、普通多结果 | 返回值和顺序正确；Reader存续期间保留旧标量，耗尽或Close后发布 |
| 普通多结果与提前Close的业务写入 | 新连接各见精确本次ID的1行 |
| DECIMAL(28,9)、TIMESTAMP(9)/TZ(9)中的七位精度 | 精确，不采用官方过程输入降级误差 |
| DATE/TIME、INTERVAL DT/YM、string/VARBINARY、DECIMAL(38,5) | 正确CLR类型和值；38位值使用公开 `W.Dm.DmDecimal` |
| BLOB/CLOB | 小值及100,000字节二进制、100,000个中文字符均完整物化，逐内容比对 |
| Size/Precision/Scale、interval缩Scale、decimal溢出 | 明确失败，全部旧输出保持，无部分发布 |
| 两个输出参数 DbDataReader | 各1,000行交错读取、独立位置和精确总和 |
| 父Reader提前关闭、连接关闭/重开 | 旧游标拒绝继续读；连接中断不发布标量 |
| Cancel 后游标撤销 | 无需后续Read，两个参数立即恢复旧值；旧reader拒绝继续读 |
| 服务器错误 -20009 | 保留错误码和旧输出，并独立记录业务最终状态 |
| 显式事务与存储过程组合 | 发送执行前明确拒绝 |
| 预取消 | marker0/业务0，无输出发布 |
| 同步Cancel、异步token cancel | 先独立观察已提交marker1才取消；最终业务0，均取消异常 |
| 同步/异步CommandTimeout | 已提交marker1/业务0，均 `DmTimeoutException`，无输出发布 |

R6 的 interval 归一修复后，又发现普通 TIME 被条件表达式隐式转换成 DateTimeOffset；该轮53例中51通过，2个精确类型断言失败，保留 `/tmp/wdm-i09-candidate-r6.json`。修复后先单独运行 `additional_types`，实际普通 TIME 为 `System.DateTime 1900-01-01T12:34:56.1234560`，再执行上述55例全矩阵。未放宽类型或精度断言。R4 的清理核验 SocketException 未留足消息，无法事后断言其确切原因；R5/R6/R7 均由 W 新连接完成了精确对象最终核验，历史失败不改写为成功。

本文件负责官方表征与 W 实库矩阵。独立离线回归由另一验证者记录。尚未实库证明的范围包括非100ns九位时间值、超过64MiB实际LOB拒绝、未声明复杂类型和旧协议版本；不得因当前矩阵通过而宣称这些能力。


## 合并主线后的三目标验收

合并 commit `7f390ed`（集成主线 DbBatch/数组绑定与 .NET 8/9/10 目标）后，使用同一份 Candidate 源码分别构建并运行三个真实 runtime。探针新增 `runtime` 和 `target_framework` 元数据；产品在测试期间无修改。每目标 **59/59 通过，退出码0**，共177例，保留上述合并前 R7 记录。

| 目标 | 实际 Environment.Version | 产品程序集 MVID | JSON 报告 |
| --- | --- | --- | --- |
| net8.0 | 8.0.32 | `bdf03642-707e-40cf-8383-df371c43cfb4` | `/tmp/wdm-i09-candidate-merged-net8.json` |
| net9.0 | 9.0.21 | `1f8a3056-4b9f-4052-9782-fb933bc9e10f` | `/tmp/wdm-i09-candidate-merged-net9.json` |
| net10.0 | 10.0.12 | `6dee0b20-0518-485a-974e-6a3816598d3a` | `/tmp/wdm-i09-candidate-merged-net10.json` |

在原55例之外，每目标新增以下同步及异步验收：

- DbBatch 第一条为 INSERT、第二条为 StoredProcedure；整批抛 `NotSupportedException`，独立新连接确认第一条 INSERT 的精确业务ID行数为0。
- 存储过程 `ArrayBindCount=2`；抛 `NotSupportedException`，OUT 保留旧值，独立新连接确认两个业务ID均为0。

三轮继续覆盖全部原有精确值、输出发布、游标生命周期、取消和超时断言。预取消最终 marker0/业务0；真实观察 marker 后的同步/异步取消以及同步/异步timeout最终 marker1/业务0。每轮连接身份为 `WDM_PROVIDER_TEST`、服务器8.1.5.60；对象精确清单保存在各报告。三个本轮前缀分别为 `I09W_6A7DB8C1B3BF499E`、`I09W_15F133305E59456C`、`I09W_DBEEAB9024544B88`，每轮12对象逐名删除并由 W 新连接逐名核验不存在，无官方恢复清理、无前缀删除。

执行环境与命令（在仓库根目录依次以 `i09_tfm=net8.0`、`net9.0`、`net10.0` 执行；下述变量只表示工具路径和目标，不含连接信息）：

```sh
export DOTNET_ROOT=/home/air/.t3/worktrees/W.DamengProvider/t3-5b93f0cb/.local/issue12/dotnet
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_CLI_HOME="$PWD/.local/issue9/validation/dotnet-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
i09_tfm=net8.0
"$DOTNET_ROOT/dotnet" restore tools/StoredProcedureProbe/Candidate.csproj \
  -p:TargetFramework="$i09_tfm" -p:TargetFrameworks="$i09_tfm" \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers -v:q
"$DOTNET_ROOT/dotnet" build tools/StoredProcedureProbe/Candidate.csproj --no-restore \
  -p:TargetFramework="$i09_tfm" \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers -v:q
/home/air/Projects/W.DamengProvider/scripts/with-dameng-test.sh \
  "$DOTNET_ROOT/dotnet" "tools/StoredProcedureProbe/bin/Debug/$i09_tfm/StoredProcedureProbe.Candidate.dll" \
  "/tmp/wdm-i09-candidate-merged-${i09_tfm%.0}.json"
```

构建/还原日志使用 `/tmp/wdm-i09-candidate-merged-net{8,9,10}-{build,restore}.log`。三个构建均0错误，20条既有探针 nullable 警告。首次直接 `build -p:TargetFramework=net8.0` 因已有 assets 只有 net10 目标报 NETSDK1005（未执行数据库操作）；随后显式 restore 同时指定 TargetFramework 与 TargetFrameworks，三目标均成功。没有清理全局 NuGet 缓存。三目标验证没有扩大前述未实库覆盖范围。


## 元数据主线合并后的最终复跑

主线 PR22 元数据能力合入后，受测 commit 为 `04ffa82`（合并 `origin/main` 的 `1dec19e`）。未修改探针或放宽任何断言，按前节相同环境与 restore/build/run 命令串行复跑59例×3个目标；仅报告和日志名替换为 `candidate-metadata-merged-net{8,9,10}`。三个 restore/build/run 均退出0，构建均0错误、20条既有探针 nullable 警告；产品与测试源码在运行期间没有修改。

| 目标 | 实际 runtime | 结果 | 产品 MVID | 独立报告 |
| --- | --- | --- | --- | --- |
| net8.0 | 8.0.32 | 59/59，exit0 | `67a77912-6668-44e6-9716-0c918f2357be` | `/tmp/wdm-i09-candidate-metadata-merged-net8.json` |
| net9.0 | 9.0.21 | 59/59，exit0 | `99e134bd-be17-4f6a-abce-64b71c36add2` | `/tmp/wdm-i09-candidate-metadata-merged-net9.json` |
| net10.0 | 10.0.12 | 59/59，exit0 | `280ac022-ca5d-4f77-8c44-12cf950cd8f5` | `/tmp/wdm-i09-candidate-metadata-merged-net10.json` |

最终177例再次覆盖全部既有过程、函数、输出、精度、LOB、游标、取消/超时以及 batch/array 预检断言。新增元数据行为 `SchemaOnly`/`KeyInfo` 与 StoredProcedure 的组合由另一个验证者的6个离线拒绝用例覆盖，不计入本次59例实库数。

三轮身份均为 `WDM_PROVIDER_TEST`，服务器8.1.5.60；新连接核查业务最终状态与前轮一致。每轮12个对象的完整精确清单记录在独立报告中，对应前缀分别为 `I09W_9C42A44EE6904096`、`I09W_0B34679F3C6D43B4`、`I09W_1C837B68B018403B`。全部逐名删除并由 W 新连接逐名确认不存在，无官方恢复清理。前一合并的177例及更早失败报告均保留；本次没有新增失败，也没有扩大已声明支持或实库覆盖范围。
