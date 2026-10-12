# TP-01 v3 Astra 定点整合复审

日期：2026-10-08。结论：`changes_requested / offline_execution_not_run`。13项输入SHA逐项回读与root交接一致。v2的主要C#构造、schema、Reader所有权与清理自测问题已在源码层闭合；尚有两处TLS规则不一致，root已确认最小修复方向。修复并复审前不释放正式Low离线窗口，更不开放数据库。

本次未运行unittest、contracts CLI、dotnet或DB，没有读secrets或修改产品；仅写本报告。[v2复审](TP01-astra-rereview-v2.md)及其他冻结报告不修改。本报告的“静态闭合”不代表新增C#自测、Python合同或实际源包链已运行通过。

## 1. 待修 C01：baseline的TLS七位观察仍被套用candidate拒绝类型

位置：`eng/temporal-precision.validate.py:281–295`。`closed_advisory`分支在`if not candidate`之前无条件执行 `reject(..., System.NotSupportedException)`，故baseline也必须满足新candidate的七位拒绝契约。

这是可由现有源码确定的错误期待：旧W `DmTemporalCodec.NormalizeForWire` 对DTO先检查offset分钟范围，再检查fraction/wire能力；offset -840/-780先得到OverflowException，而不是NotSupported。Program在TLS closed7已正确改为不依赖D7/Z7的advisory请求，这些真实旧行为必须被记录为表征，不能让它们阻塞baseline或改驱动异常优先级来满足probe。

root裁决：**baseline closed7只表征真实结果，candidate closed7仍严格NotSupported。** 修复只移动/分离validator分支，不放松infra拒绝、输入expected检查和无DML事实。baseline advisory成功也只能记为旧W观察，不计七位开放；其没有DML，不能要求persisted_rows=1。candidate相同请求若成功、Overflow或任意DmException均不能通过NS门。

限定文件：validator及对应独立合同；必要README澄清。必需正反例：baseline TLS offset -840的真实Overflow分类通过characterization；candidate同型payload必须失败；candidate显式scale7即使值为整微秒也必须NS；baseline请求若实际返回，记录advisory零持久化且不产生capability acceptance。

## 2. 待修 C02：TLS fixed metadata列目标与validator不一致

位置：Program `ParameterCase:625–633`仍选D7/Z7，只在不存在时返回closed；validator `validate_binding:444–447`却因seven_write=false要求fixed column_scale=6。若TLS服务器实际存在D7/Z7，producer报告真实scale7会被当成错误。write capability不能反推出物理列精度。

root已确定最小规范：**fixed metadata轴在TLS明确选择必建D6/Z6，在shared选择D7/Z7；case记录实际target column及metadata。** validator根据这份冻结工具目标规则核对物理列，而非用cap强行解释D7为6。TLS若存在D7/Z7，可以另外表征，不作为该fixed轴的mandatory依赖。TLS7能力拒绝仍由独立advisory DateTime2/DTO+Scale7检查；fixed六位+用户scale7同时覆盖legacy wire不兼容，但两种拒绝原因不得混称。

限定文件：Program、validator、相关手工shape/contract tests、必要README。必需正反例：TLS fixed case精确记录D6/Z6及真实scale6；把目标名改D7却仍报6、把真实metadata换7或丢失实际target列字段都拒绝；shared fixed仍选D7/Z7并保七位主门；TLS七位DDL缺失不能令六位fixed用例直接closed/跳过。原来以“不存在D7”为由跳过mandatory fixed metadata轴的测试应改为真实六位轴，不保留宽松closed通路来伪装完成。

## 3. B01–B07静态处置

| v2问题 | v3核对结果 |
| --- | --- |
| B01条件表达式类型提升 | `TemporalSample:465–469`改为显式if，各return保持DateTime或DTO；OfflineContracts直接调用同一helper核GetType/ticks/Kind/+330，关闭隐式转换路径 |
| B02拒绝记录层级 | 共用NewPhaseRecord建立真实phase层；binding_rejection检查initial phase的实际execute_attempted/row/persistence_source，未伪造顶层persisted_rows；早期未执行与fresh行数区分 |
| B03ResultColumn与ParameterDescribe | Capture分别带role；metadata对结果列flag0、fixed参数1、advisory参数2区分，raw source按角色检查；dual-scale不再把ColumnDescriptor强标fixed1 |
| B04Reader owner/恢复 | SelectOwnedStatement在活Reader优先读其m_Statement，关闭后不回退到stale对象；每phase显式Prepare取得before snapshot，在Reader取值/关闭前取得after/EncodedScale；预期拒绝后的scale6用FreshScale6Recovery另开连接/命令，并记录recovery_source |
| B05实际C#清理分支 | live与fake通过同一CreateWithLifecycle/ValidateOwnership/MarkerMatches/CleanupWithOwnership；19个所有权分支+样本类型/Reader选择/恢复策略在真正C# offline入口执行，不再是Python复制策略 |
| B06关闭能力与列存在性 | Program已将TLS closed7主请求改为advisory，不依赖7列；TLTZ seed已允许单独失败分类且candidate至少一个成功种子证实读侧NS；剩余C01/C02必须修 |
| B07真实producer形状 | offline用实际Observe/Capture/NewPhaseRecord/StatementObservation/JsonSerializer生成synthetic shape，Python consumer独立比对expected；不会赋予candidate/profile接受。尚未实际执行，必须由Low build后验 |

R3 statement ID来自本进程ConditionalWeakTable按实际object reference编号，仅用于同次工具进程的owner转移/新对象观察，不冒称服务器handle或session ID。validator检查before command→after reader的同对象编号；fresh恢复可产生新编号。没有给驱动m_Stmt回填、没有改Reader Close/Pool/Discard策略。

## 4. C#离线门的安全与复用核对

`Program.Main:45`在Identity读取、CheckIdentities、CheckSourceBinding、Configure/Open之前分派`offline`。offline只用固定合成数据、fake对象和两个输出路径；没有调用LiveOwnershipDatabase，也不读取DAMENG环境、TEST wrapper、证书或secret。runner build环境继续移除DAMENG变量，`--offline-only`在访问wrapper/执行DB lane之前返回。

实际共用关系已逐项确认：

- Reserve调用CreateWithLifecycle；它先VerifyIdentity/Exists，预存对象不落ownership也不CREATE。reserved→inflight先持久化，再CREATE，verified server失败记录known_create_failure；ACK丢失保inflight。
- live Cleanup和offline CleanupCase均调用CleanupWithOwnership。后者先校验manifest身份，再VerifyIdentity；present对象必须confirmed/inflight、完整nonce DEFAULT匹配才DROP，known failure/reserved不DROP；之后FreshAbsent。
- MarkerMatches保留完整128-bit nonce，不只核24位name前缀；missing/wrong/same-prefix-other-suffix均有fake执行负例。
- C# offline覆盖known-create-failure、错误DB身份、坏/重复key manifest、cleanup抛错及fresh absence失败；Python shape还独立检查drop_count和identity→marker→drop顺序。Pythonrunner调度mock仍仅作为另一层测试，不冒称C#或DB执行。
- Reader观察和phase初始记录由live和offline共用；shape中的结果列flag0/参数flag1或2及early rejection来自同一serializer，而expected来自独立Python文本规范。

未发现新的明显C#签名可访问性、partial类、接口实现或调用参数编译问题；真实编译结果仍必须由Low给出。live DATA_DEFAULT在实际数据库上的具体返回格式、反射可观察性与严格TLS登录不是offline fake能证明的，仍待后续root独立批准DB阶段。

## 5. 原R01–R08保留结论

精确241e15 source manifest→原v23包/DLL/MVID绑定与comparison_head分离保留；strict duplicate-key/true-int检查保留。candidate核心按独立case表核Min/Max/末尾ticks/Kind/offset，强制shared主七位与六位整微秒成功，cap缩域不能把主门成功替换成通过的拒绝。

GetString继续核独立实际值和声明scale位数；8/9读以actual server值为准，不因CAST改变请求而跳过。TLTZ仅适用值getter关闭、metadata/schema可观察，candidate仍需独立种子；不适用DTO getter不被改成NS。Cell继续从已经fetch的真实cache读取payload长度/hash/isRsBdta，不按nominal CType制造数值。raw advisory CType2与effective26/27允许不同，原snapshot保持角色来源。

这轮没有重新发现需要扩大到产品/Builder/协议I/O的修复。两项剩余TLS修正属于现有工具目标，不新增服务器能力或验收范围。

## 6. 修后Low仅离线阶段顺序

当前结论仍为changes_requested。C01/C02修后High停写、Astra/root定点复审新hash，再允许Low依次执行：

1. 全部portable Python合同，含producer-shape手工正反例。
2. 显式`--local-required`真实归档来源/调度门；没有所需文件即失败，不skip。
3. contracts CLI核真实241e15 source/package/DLL/MVID（只读，不执行managed代码）。
4. runner `--offline-only`：新目录中restore/build，并自动执行真正C# offline合同；不访问TEST wrapper。
5. Python shape CLI消费该次**实际C#**输出，不能用手工shape代替。

可执行模板（Low用已冻结真实路径替换占位）：

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/TemporalBoundaryProbe -p 'test_*_contracts.py'
TP01_V23_SOURCE_MANIFEST='<241e15-manifest>' TP01_VERIFIED_FEED='<verified-feed>' \
  PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/test_runner_contracts.py --local-required
PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/contracts.py \
  --identities tools/TemporalBoundaryProbe/identities.json --source-manifest '<241e15-manifest>' \
  --package '<原v23-nupkg>' --output '<新目录>/source-package.json'
python3 eng/temporal-precision.py --feed '<verified-feed>' \
  --identities tools/TemporalBoundaryProbe/identities.json --source-manifest '<241e15-manifest>' \
  --repo /Users/wanghongyi/Projects/W.DmProvider --profile shared --output '<new-offline-dir>' --offline-only
PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/test_producer_shape_contracts.py \
  --shape '<new-offline-dir>/offline-producer-shape.json' --output '<新目录>/actual-shape-validation.json'
```

Low需确保PATH中的dotnet解析到10.0.203 host，runner已设置任务CLI_HOME及禁首次体验/遥测，build单节点/noReuse/noSharedCompilation/disable-build-servers。记录自然退出码、C#每case结果、shape hash及全部输入首尾hash。无DB授权；所有离线门通过后仍由root另行决定baseline shared/TLS实际窗口。

## 7. 复审输入指纹

| 文件 | SHA256 |
| --- | --- |
| Program.cs | `ceefbcd76441a347d9d0171116bb2aed3bad38b5a9e1953cdeaccd0662411ba8` |
| OfflineContracts.cs | `e6644aa98809461cc19c79561c746cad5866180fcf03daa70e748f8cf62ea758` |
| runner | `85e1601ebe57902a396c7ea15124a78c23d19ab2cec0ac84d4ddde1281de4de4` |
| README | `31b873134217a3a1350fb3ded0645ddc72c2590182d335a2ca48876e141c648e` |
| test_runner | `17ad5f8d53938de29e0d3e3813d7c772cf9975ac7af247b84a4b222daaea9967` |
| validator | `7a5bec6d4ac160fbc7c499c3f22700709b8ed5bb63a2c3834fd524bdbbc87d88` |
| test_validator | `c500c9e1787bb373098d620a3d1cb6e499ffe3e863acda82bfd8e46accf32fab` |
| test_producer_shape_contracts | `8c364eb4b33fc1f16ec9bd6bc2834c65b5f9fefe441795d271c4125729c69909` |

csproj/global/identities/contracts/handwritten fixture五项与v2交接SHA一致，已实际回读核对；producer8 aggregate由root给定为 `ec08fca8370ca931f97942b324f8a7f1b3cb74cb873ae5015f3ea8be8d27b036`，本轮独立核对逐文件SHA，不冒称重算未给算法的aggregate。

本报告完成后停止写入。既有R01–R08/B01–B07记录不回写；C01/C02修复后只需定点复审这些差异，不重开无关设计。
