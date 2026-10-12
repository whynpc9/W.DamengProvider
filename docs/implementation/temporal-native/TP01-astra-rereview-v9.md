# TP-01 v8 整合审阅 / v9 定点复审

结论：**v9 静态接受，可交 Low 重新执行全部离线门。** 本次没有执行 unittest、工具验证 API、dotnet 或 DB；不代表运行通过、shared/TLS 接受或 TP-01 关闭。仅写本文，未改产品、工具或旧报告。

## 冻结输入与修订历史

v8 的 17 项输入以 `.local/temporal-native/verification/TP01-v8/manifest.json` 为逐文件基准，其 `all17_aggregate_sha256` 为 `ba3f51bbc4ba158dc0ed73e1e739a7c033152cb51291b34728945a57db57ed27`。独立读取当前 17 文件并计算 SHA 后，确认 v9 只有以下两项变化，另外 15 项与 v8 完全相同：

| 文件 | v9 SHA256 |
| --- | --- |
| `tools/TemporalBoundaryProbe/packed_oracle.py` | `bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9` |
| `tools/TemporalBoundaryProbe/test_packed_oracle_contracts.py` | `b623848f9c4d783991f5b02786e7372cd2ddb232e9f98463c72ea957c18ac722` |

关键未变输入：Program `ff126d021ef6edae694ef8f29b60782800aa442cd4219eb5d09d8924475ce260`；OfflineContracts `5f394887b1bae041e65aad9cacfd6760bef29216c104378681558bbd600ca00b`；validator `22e96a302e42ec6549e9fd590a90efc555f7a4392dd48c28a5d466959c33f3fd`；literal fixture `df97ea26457079c0b5a3465d9191078cb3e4ae4e5e49ba7a4be48be31f8ed148`。

v8 唯一静态缺口：独立 `decode_observation` 原来只要求 raw.width 等于 metadata.width。两者同改错误值时，O 路径缺少 W 后续 metadata_list 的布局宽度核验。真实 producer 已有 width 等于已知布局长度的门，正常采样不会产生此值，但独立接受边界仍应自守该不变量。root 授权 High 仅修上述两文件；v8 未运行，快照保留。

v9 新增一条 `integer(metadata.width)==LAYOUTS[c_type][0]`，在已知 CType 检查之后执行，拒绝 bool 和不匹配宽度。新增负例使用合法 TZ11 原 bytes/hash/cell length，同时把 raw/meta width 改为 10，要求 PacketError。负例 setup 在目标 catch 之外；原正例与其他拒绝测试未删改。该缺口关闭，没有剩余静态阻塞。

## v8 整合范围的审阅结论

### 真实采样与来源

`Program.cs:405–445` 的 ReadOracle 在 Read 成功后、任何 temporal getter 之前，调用同一 `RawTemporalOracle`；现有 Columns/SchemaScales/Cell 只先取元数据及缓存观察。FF9/FF7、GetValue、typed/generic/GetString 和随后 Read 的原流程保留。原 EXTRACT 查询、decimal 转换、local/UTC 比较及错误记录未改；独立 CAST 诊断文件仍为已冻结 `4eaf7731c869747ab0edafdd70152729188e605a10b54707f328a45e7d9d2393`。

`OfflineContracts.cs:116–147` 直接读取 `m_RsCache.datas[datasOffset][ordinal+1]`，从该 cell 取得长度、SHA 与 hex，不调用 temporal getter/encoder/TP-02，也不从格式文本重建。实际普通缓存无 temporal normalization 的来源证明沿用 `TP01-components-diagnostic-v2-triage.md §2`：W FillRows→msg.F/G→字节复制，GetBytes 直接返回；W cache 文件与实际 v23 source manifest 的 SHA/长度一致，O 固定资产有独立普通复制链。该证明只覆盖绑定资产的 ordinary 缓存。

导出门共同要求 actual metadata Available、result_column、普通格式、Mask=0、非 BDTA、实际 CType/width 与已知布局长度匹配、实际 cell 长度相同。未知 12-byte、BDTA、TLTZ/非零 mask、不匹配布局无 hex，不猜测类型。采样函数内 hash 与 hex 连续取自同一 cell，无中间 getter；保存后的字符串不受后续缓存 mutation 影响。没有修改 Reader 生命周期、连接池、诊断策略或 ownership/cleanup。

### 独立数值精度门

`packed_oracle.py` 只依赖标准库 hashlib/re，采用整体 little-endian 位置整数，未引入 provider/TP-02 调用或 encoder。固定 3/5/7/8/10/9/11-byte 布局分别还原日历、时分秒、legacy 微秒×1000 / extended ns 及 signed offset。字段范围、reserved bits、声明 scale 的整数 quantum、Gregorian 1…9999 域和 offset 范围均显式检查；没有先除以100截尾或浮点 tolerance。负年、hour24 等本轮未证域继续拒绝，不以该模块开放能力。

`decode_observation` 交叉检查真实 raw/metadata/cell 的 CType、scale、width、mask、来源、角色、format/BDTA、长度及重算 SHA；v9 补齐独立宽度门。`temporal-precision.validate.py:295–368` 再把独立 decoded 全字段逐项等于冻结 case 规范 ticks/offset，并同时等于 FF9 与 FF7；W 原 typed/generic/GetValue/GetString、声明格式精度、metadata/schema/cell 门继续执行。

替换严格限于 `components_required` 且有 offset 的 TSTZ：原 EXTRACT bool/components/basis/error 保留为表征，错误仍走原分类，基础设施失败仍拒绝。普通 TS 的 server_components_exact 和独立 numeric 比较保持；advisory 的既有独立角色保持。没有把旧 O 实际丢 tick、六位舍入、FF9 差异或 CLR 返回差异改为精确。shared 七位、六位强制范围、TLS 七位关闭、TLTZ 关闭及候选拒绝类型不变。

### 合同测试的静态可执行性

实际 C# serializer 的 raw shape 调用同一个采样函数：五个官方 one-tick 字节 literal 覆盖 TS9 与 TZ11 的 0/+480/-300/+330；四个闭门样本覆盖未知12/BDTA/type-length/mask4。缓存 byte5 的 C8→CA mutation 由真实 fake cache 修改产生，100ns→101ns；Python 明确要求后者在声明 scale7 下拒绝，不以舍入后的100ns通过。

Python 模块正例另有手算 DATE、TIME、legacy123456μs（`EE8180C403`）及最大日期 literal；末日/offset/tick/保留位/长度/hash/scale/mask/source 等负例独立检查。新 JSON 是固定 literal 输入表，TZ 尾部从固定 signed offset literal 拼接，无运行时 provider/新 encoder 生成 expected。legacy 对非整微秒请求的量化 literal 明确仅用于旧 O 表征，不能证明新精确合同。

顶层 validator 合同有成功 shared/TLS/baseline 基准，并验证 TSTZ EXTRACT=false 或真实 server error 只有在 raw/FF9/FF7/规范仍全准时可接受；普通 TS false 继续拒绝。缺 raw、元数据混族、mask、hash、scale、length、BDTA 以及修改 ns/offset/date 后重算 hash 的 mutants 仍拒绝。shape 的 mutation 负例只捕 PacketError，其他异常不冒充预期拒绝。此前 NULL、fixed observer、source/package、cleanup 和 closed profile 已闭合范围未重开。

## Low 下一窗口

使用新的 v9 冻结目录，先重新核验 17 项 hash，再顺序执行既定全部离线门：portable `test_*_contracts.py`（含新增 packed 测试）、`test_runner_contracts.py --local-required`、真实归档 source/package 的 contracts CLI、私有 restore/build、实际 C# offline selftest，以及 Python 对该次真实 serializer shape 的消费。runner 的 `--offline-only` 停在 DB 前；使用既定 SDK、可写 DOTNET_CLI_HOME 和单节点/禁共享编译参数。

不能沿用 v7 的通过计数，也不能用 v8 未运行快照作为 v9 执行证据。任一步失败即保存新失败并释放窗口，由原 High 修复后重新冻结。全部离线通过后由 root 单独开启 shared/TLS 新运行；不复跑已失真的 CAST 诊断，不向旧 v7 报告补造 raw hex。旧 v7 shared 验证失败、diagnostic-v2 结果和原清理证据全部保留。

本文完成后冻结并停止写入。
