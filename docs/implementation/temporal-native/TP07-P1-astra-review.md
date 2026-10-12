# TP07-P1 消费桥与映射观察静态审阅

2026-10-09。结论：**bridge_static_accepted / Low_lock_build_observation_pending**。未发现阻塞P1窄窗口的静态问题。接受的是固定EF源码到唯一W候选的隔离消费桥和未修正mapping观察工具，不是时间mapping、EF测试全集、真实数据库或C门接受。

已读 [TP07设计](TP07-astra-design.md) 与 [TP06 root接受](TP06-acceptance.md)。本次只读源码/补丁/manifest及实际文件哈希，未执行prepare、SDK、restore/build/tests、observer或DB，未修改源。High的prepare/replay收据只作作者自检输入，不能替代Low独立重建。

## 1. 来源与精确改动范围

EF pin `5863d3755dbd2d333ac359bf1c0a789762adbaed`、tree `b8f99168225a3742675ae594e8735f481a591fd5`、archive SHA `668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8` 与既定设计一致。实读 `.local/temporal-native/TP07/v1/base-source` 的165文件和`bridge-work`的167文件，完整路径/hash字典均精确匹配bridge manifest；本地patch/manifest与工具目录冻结副本相同。

43个唯一allowlist路径恰好等于实际变化集合：38项namespace归属（包含retry三文件及CLI桥）、3项驱动依赖、2项新observer。prepare对每个已有文件核before SHA和anchor次数，未知action拒绝；新增文件来自冻结模板。重新计算的patch字节和完整manifest必须与冻结副本完全相同，范围外改动无法通过该比较。输出限定新TP07子目录，tar拒绝绝对路径、逃逸、链接及secrets路径；原dirty EF checkout只用git object/tree/archive，不执行checkout/build或读取其secrets。

五份原锁和global.json保持原字节；SDK pin为10.0.401/latestPatch，全部原锁内EF条目为10.0.12。DateTime mapping source、DTO/TimeOnly、migration generator未改；TimeSpan仅namespace切换，原facet/materializer/literal行为保留。原官方断言也保留，不能拿这些尚未适配的retry错误号/DTO GetValue断言宣称W通过。

## 2. 驱动桥、身份与安全边界

中央W版本用精确区间 `[0.1.0-temporal.20261008144915]`，provider和local-test项目转W PackageReference；observer通过ProjectReference消费隔离EF项目，但驱动自身仍是PackageReference，没有W产品源码引用。源码生产connection factory、scaffold和provider-specific mappings转W命名空间，未发现遗留`using Dm;`、`global::Dm`或官方PackageReference源码路由。

prepare先核nupkg SHA、nuspec ID/version、包内net10 DLL SHA与纯ECMA-335读取MVID；不加载执行驱动。candidate feed只复制这个包，restore配置清掉原sources并将精确W名称映射本地源，其它依赖走独立普通源。实际restore缓存/资产仍由Low核，不能仅靠source mapping字符串认定加载身份。

observer先核实际加载driver.Location的SHA/MVID；deps必须存在该精确W版本且type=package、EF/Relational10.0.12，并拒绝官方驱动library。随后明确要求生产EF创建的connection/command与mapping创建的parameter均为实际W类型，不以“W程序集被加载”替代真实服务接线。输出EF provider实际路径/hash与deps hash，Low还须将其与本次source/编译闭包绑定。

retry只接受W `IsTransient=true` 且非 `DmCommitOutcomeUnknownException`；builder两入口和strategy两构造均拒绝非空错误号override。未保留按错误号重放的OR支路。其运行合同须后续独立W counterpart验收，当前静态检查不冒称原官方retry测试通过。

## 3. 14项观察的真实性与限度

observer通过实际 `IRelationalTypeMappingSource`/模型property取得mapping，再由 `context.Database.GetDbConnection()` 的生产factory创建W command并调用 `mapping.CreateParameter`。7个property分别为默认DT、显式HasPrecision6 DT、显式TIMESTAMP6 DT、默认DTO、TimeOnly、TimeSpan、decimal控制，每项value/null共14次。输出是实际mapping/model facet、参数DbType/provider类型/Precision/Scale与内部显式标记；两个反射flag均与当前W的internal字段匹配。

没有Open、Execute、DB测试入口或秘密环境读取。连接串是固定合成值，且不写入输出；正常输出不含参数Value或异常文本。prepare本身只输出安全失败码/异常类型，不输出秘密。observer没有吞异常伪造14项结果；若进程失败，Low保留失败，不能补造缺失项。

这些value是低风险零fraction控制，目的是看未修正EF配置是否写非零Precision/缺失Scale，不验证实际写入精度。NULL是传给CreateParameter的明确输入分支，当前输出不单独证明server NULL行为。固定 `temporal_mapping_accepted=false`、`unfixed_mapping_observation`正确；即使14项全收集且退出0，也只完成P1观察，不能视为mapping已兼容。

## 4. Low 后续精确窗口

1. root给新目录和冻结工具/manifest/patch/包tuple。Low只在该目录从base archive重建，并从另一干净base副本**独立apply冻结patch**，比较167个完整文件/hash；不能仅引用High自己执行prepare的`replay_source_equal=true`。原base/bridge-work及dirty checkout保持只读。
2. P1只消费 `test/TP07MappingObservation/TP07MappingObservation.csproj` 与其EF provider项目图。为该图建立任务级NuGet缓存、feed/config和锁副本；显式force-evaluate准备替换provider的原官方锁并生成observer新锁。记录original/new锁及diff，逐包核所有原非驱动resolved版本/contentHash不变，特别是EF10.0.12；新增项只能是已解释的W/observer图依赖。出现非驱动升级或未知依赖先停止交root，不自动接受新版本。
3. 完成锁审核后冻结新的**执行source+锁闭包**，再对同图执行locked restore；构建使用其SDK实际版本、可写DOTNET_CLI_HOME、首启/遥测变量和单节点禁共享编译/build servers。保存命令/退出码、实际SDK、assets/deps/locks以及编译DLL身份。其它未消费的四份原项目锁仍是历史输入，不宣称其candidate restore/build已验；P2扩大图时另准备对应锁。
4. 运行observer的实际已构建DLL，禁止隐式再build/restore。核14个唯一case ID，恰为7×value/null，无缺失/重复；每条都是实际W参数，decimal控制仍保持自身Precision/Scale语义。将真实观察的时间Precision/Scale/显式位原样记录，包括不符合后续W时间合同的值；不要为了进程通过或结果好看清零/改写。
5. 绑定当前bridge manifest/patch、执行source/新锁、W包/DLL/MVID、实际EF provider DLL及observer DLL/deps、退出码和JSON SHA。前后复核冻结输入不变。缺SDK/restore/编译/观察失败均保留，不由Low改source自修；窗口释放给High定点修正。

本阶段不跑原all/admin launcher、EF功能/CLI/实库，不加载TEST secrets；不发数据库命令、不创建对象。P2时间mapping修复、strict DTO materializer、literal/default持久化、scaffold/guarded Down及完整候选C门仍按设计独立进行。

## 5. 冻结身份

| 文件 | SHA256 |
| --- | --- |
| `tools/DownstreamAcceptance/TP07/prepare.py` | `8c5edec0014974f90dfe804deee56aff245adc68ec862e67982571314c0dd4e9` |
| `bridge-allowlist.json` | `247654ad865a6955652daf3c1037a978d6ca80cdfd9a3210d83b19a6fd47a487` |
| `bridge-manifest.json` | `4750a330f2f1e63a5e40780acf19820b38b86db2c1bce08ff224600a18b8b8b4` |
| `patches/driver-bridge.patch` | `08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5` |
| `mapping-observation/Program.cs.template` | `b58af43f9c7be7e94a5cb743ca6362e177cf2930121de0f4e7cb87b6ddefe85b` |

表中简写均相对同TP07工具目录；manifest的其它tool hashes实读也一致。唯一W候选身份已由TP06接受记录固定，P1不重打包。报告完成后停止写入。
