# TP07 D preparation：NuGet contentHash与sidecar分域复核

2026-10-09。结论：**DPREP-B02_confirmed / v2_preparation_execution_blocked / v3_narrow_repair_pending**。本轮只读33个现有包及代表性的旧原生恢复缓存，独立计算文件校验和；未运行SDK、restore、tests、准备脚本、模块导入、DB或网络，也未修改包、metadata、sidecar或旧报告。

此前静态审阅没有识别出这项既有相等断言错误。原 [Dprep-v2报告](TP07-D-preparation-astra-rereview-v2.md) SHA256仍为 `be887087b6de179532aa12efcd977a7e97515b3b8b2ea96c428f18fce0216038`，保留为B01 Hosting接口窄修的历史结论；本文件新增B02，不能继续据旧结论执行v2准备流程。

## 独立回读事实

root观察文件实际位置为 `.local/temporal-native/root-approvals/TP07-D-sidecar-triage/observations.json`，实算SHA256 `0cf076a60c10a0bc8a47e0e69f49999c6c1b5aa0aa89dd913e23c63d6efb2064`。独立从原Specification lock `685ecc8feabfd49aca9a1dd8cdb651564e3d30480d4e9b01c3cfae0ea77cc4b9`重建Design有向闭包，33个包ID与观察集合完全一致。

随后逐个读取 `/Users/wanghongyi/.nuget/packages/<lower-id>/<version>/` 的原nupkg、`.nupkg.metadata`和`.nupkg.sha512`，没有采信producer布尔作为结论：

| 实际比较 | 结果 |
| --- | ---: |
| metadata.contentHash == 原Spec lock.contentHash | 33/33 |
| sidecar文本 == Base64(SHA512(完整nupkg字节)) | 33/33 |
| sidecar文本 == 原Spec lock.contentHash | **0/33** |
| 原nupkg SHA256 == root观察中的archive_sha256 | 33/33 |

所以本次输入中，两类值分别成立但彼此不同。完整ZIP的SHA-512计算仅证明sidecar对应当前归档字节；它不是NuGet native contentHash的替代实现。本轮不推导或重写NuGet签名/内容hash算法，也不把上述现有缓存回读称作D新native restore结果。

## 代表性的旧原生恢复缓存

只读 `TP07-E-combined-v1-lock/` 的旧lock receipt SHA256为 `40d1fd47b3e5b6109ca5aae8f3d79cdcc14ed9d9dd241ad6190812006f7f862d`，lock manifest为 `bdf3abc69d91b26b9c6d1e927e6a58d0b0dc91db6ed4e3421c0e8afababa4c86`。其已有收据记录SDK10.0.401版本查询及L0/E两次native restore，三进程均exit0、无timeout、database_accessed=false；本轮没有重跑这些事件。

实际读取该轮task cache中的 `Microsoft.EntityFrameworkCore/10.0.12`，并与该轮真正生成的 `replay/test/TP07TemporalRuntime/packages.lock.json` 比较：metadata.contentHash与新锁相等，sidecar与完整ZIP SHA-512相等，sidecar与该native hash不等。对应文件身份：

| 文件 | SHA256 |
| --- | --- |
| nupkg | `7e9f98370ce04178d704d1bc6a3b86bda1c254230f1e487e4fa666f2cb8323f5` |
| fresh `.nupkg.metadata` | `2c55de6f9297d2a4e15dfdea7cc3e9d2cb67e4728fb95c6710ce0ccd38725a58` |
| fresh `.nupkg.sha512` 文件 | `a63b9664e3e2b86fed43c97f8290282792879c5fb607f8b423934fb4b9a0259f` |

此例说明差异并非仅由原global cache年代造成；现有真实原生恢复产物也有同样两类身份。它仍不能替代未来D的20新包及完整33包原生证明。

## B02触发点与最小修复合同

冻结v2 `prepare-d.py` SHA256 `d25f1e3173ce734a0bcbcd2c51ffbf1d86487f6f82bbcf0382fd75ac46d4638d` 的两处断言错误地把三者串成相等：

- `:218`，P0原输入验证：metadata.contentHash == sidecar == 原lock；当前33项均不满足，正常原输入会在SDK前停止。
- `:138`，`native_proof`：未来fresh metadata == fresh sidecar == 原lock；即使native restore得到正确结果，同样会误拒绝。

v3应只拆开这两种证明，不改包或预期值：

1. **NuGet native身份**：原metadata.contentHash与原lock核对；新restore之后，实际生成lock的resolved/dependencies/contentHash继续与原锁逐项核对，fresh NuGet metadata.contentHash与该native lock/原锁一致。值必须来自NuGet实际产物，不能用自行计算的ZIP hash填入metadata或lock。
2. **归档字节身份**：原/fresh sidecar分别与其对应完整nupkg字节的Base64 SHA-512核对；另保留原输入artifact SHA、archive SHA256、feed/cache/archive三方同字节、nuspec zip/落盘及版本身份检查。
3. **来源与执行**：维持新空task cache、local-only feeds、精确33+W图、20新包范围及实际SDK事件；不复制旧metadata作为fresh、不手写sidecar、不改全局缓存、不扩大网络或驱动范围。新收据清晰区分native contentHash和archive checksum，不再要求两域相等。

原v1/v2源码、Low冻结及报告保持不变。High完成新v3窄修停写、新freeze后再做源码复核；当前只确认B02及修复方向，不宣称v3已正确、D native proof已执行或D/完整EF-C已接受。
