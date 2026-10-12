# TP07-P1 NuGet hash 语义窄裁决

2026-10-09。结论：**coordinator_hash_semantics_defect / v2_continuation_design_accepted**。v1停止正确，不能继续使用错误比较；目前差异属于archive hash与NuGet contentHash的语义不同，不是已经证实的包损坏。tracked bridge、W产品和五份原锁无须修改。仅由High在新ignored协调脚本中修正规则，Low独立执行后续门；本报告没有执行SDK/restore/build/observer/DB。

## 1. 失败位置与实读事实

`.local/temporal-native/verification/TP07-P1-v1/prepare-independent-validation.py:26` 将 `Base64(SHA512(整个nupkg))` 直接与原lock `contentHash` 比较；这是错误的等价关系。第30行成功文案也把尚未由SDK重新建立的contentHash宣称已核，v2应明确改为provisional/pending_sdk。

原 `validation/verification-summary.json` SHA为 `a04252015d2f012733f0bd2fc791b7df8ee39b89be81a3e72cd34d5412fc8258`；独立patch check/apply均0且167文件/43变更匹配，之后停在首个baseline包预检。SDK、restore、build、observer均未执行，旧失败/窗口释放记录保持。

本次只读重算本机 `Microsoft.EntityFrameworkCore.Relational 10.0.12`：nupkg SHA256为 `993a89827edc7a0d36d3861e7e965af4f345717f02154841fc3f34ee087c5792`；whole-archive SHA512以 `2CIHh1ki…` 开头且与旁边`.nupkg.sha512`完全一致；`.nupkg.metadata.contentHash`为 `OiHmr8Xz…`，与固定base lock完全一致；archive含`.signature.p7s`。没有修改、解包重写或重新下载该包。这些事实不单独替代SDK的包消费/完整性门。

NuGet官方说明区分旧`.nupkg.sha512`的整包hash与`.nupkg.metadata`的contentHash，签名变化可能使前者不同而后者保持。当前公开 `PackageArchiveReader` 实现也分别提供 `GetContentHash`（signed专用路径、unsigned回退）与 `GetArchiveHashAsync`（对archive stream计算）。不能自行“删签名文件后重新zip”来模拟NuGet算法。[NuGet metadata设计](https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File)、[PackageArchiveReader源码](https://source.dot.net/NuGet.Packaging/PackageArchiveReader.cs.html)。

## 2. v2最小可审门

1. 新独立输出目录及新协调脚本SHA，绑定旧v1 summary/hash、原source/bridge/patch/SDK pin和同一W候选；不续写旧validation目录，不改旧lock来迎合archive hash。原独立patch成功证据可引用其精确hash，v2执行副本仍需证明对应167文件闭包。
2. 对每个被消费的原非驱动包，冻结明确ID/version和实际nupkg SHA256。整包SHA512只与同一archive的旧`.nupkg.sha512`核；旧`.nupkg.metadata.contentHash`只与base lock作**provisional**核对。缺包、sidecar不一致或未知内容先停止交root，不能下载升级、换包或将旧metadata视为最终通过。
3. localfeed只含这批已pin实际nupkg和唯一W候选；source mapping限定它们。创建空的任务级`NUGET_PACKAGES`与独立HTTP cache/CLI home，不复制旧解包目录或`.nupkg.metadata`来制造命中；实际restore从已冻结archive重新消费。保持SDK有效默认签名验证设置，不设置关闭开关、不改包签名，不把“没有错误日志”额外宣称为独立签名认证报告。
4. 在fresh build副本仅对observer+provider图准备新锁。SDK成功后，逐原非驱动包要求：**SDK新生成metadata.contentHash == 新锁contentHash == 固定base lock.contentHash**；实际新cache nupkg SHA256仍与localfeed归档pin一致。所有原非驱动ID/resolved/contentHash/依赖项保持，EF/Relational仍10.0.12。W包另外按既定package/DLL/MVID身份门，不与原官方驱动contentHash混比。
5. 上述事实完整通过后才冻结新执行锁，执行locked restore、build及14项observer，继续原P1字段/actual W routing/deps门。锁准备成功不等于locked restore或编译成功，更不等于时间mapping接受。新metadata或lock不一致即保留失败停止，不由Low改实现或放宽比较。

报告分列 `archive_sha256`、`archive_sha512`、`old_content_hash_provisional`、`sdk_generated_content_hash`、`new_lock_content_hash` 与各阶段状态，避免再用一个“hash_matched”掩盖比较对象。准备阶段只能标`pending_sdk_content_hash_verification`；完成SDK三方比对后才记录原非驱动包内容合同保持。无需新增自制NuGet hash/签名工具。

修复仅限ignored协调层，不扩大官方测试、CLI或DB范围；默认时间mapping仍未修正，observer仍必须输出 `temporal_mapping_accepted=false`。旧v1失败永久保留，root审新协调代码后由Low新窗口执行。
