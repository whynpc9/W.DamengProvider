# TP07 D 外部准备 v3：B02 sidecar窄修复核

2026-10-09。结论：**DPREP-B02_closed / scoped_static_accepted_for_Low_external_preparation / D_runtime_not_authorized**。本轮仅静态读源、AST、JSON与哈希；未导入/执行准备脚本、SDK、CLI、tests、DB、cleanup或网络操作，未修改实现。可由root按独占窗口安排Low有限外部准备，不等于新的native证明或构建已经通过。

## 窄修判定

对照v2完整diff，唯一代码逻辑变化为两处原/fresh sidecar校验分域、向native_proof传入实际D lock，以及新证明字段。`build-generated.py`逐字节不变；B01 Hosting身份、cleanup独立性、source/lock/startup集合及G6/G12字段保留逻辑未变。

- P0原输入先核冻结artifact SHA，metadata.contentHash只与原Spec lock比较；sidecar另与其对应完整nupkg字节的Base64 SHA-512比较。没有再要求sidecar等于native contentHash。
- N0两个真实restore及原graph检查之后，读取 `root / locks((L0,D))[2]`；该索引准确指向 `test/TP07TemporalDesign/packages.lock.json`。33项fresh metadata.contentHash须同时等于原Spec和该次实际生成D lock的contentHash，不用原锁值冒充新锁。
- fresh sidecar独立核完整归档SHA-512，并要求原sidecar文件SHA仍匹配冻结input、fresh/source SHA及字节相同。原/archive→feed→fresh cache SHA256、zip/cached nuspec、包ID/version、resolved/dependencies、33+W图和新空cache门全部保留。
- native-proof行分别记录contentHash、actual_lock_contentHash和full_archive_sha512_base64，带sidecar语义、原引用及fresh文件SHA。没有计算或重写NuGet native contentHash，没有更改metadata/sidecar内容或全局缓存；原有已证明cache的阶段复制方式未扩展。

未发现此窄修新增的确定调用/字段接口阻塞。generated阶段只转引native-proof，不解析其新增row字段，因此不需要改动该脚本。新增Base64计算只证明archive checksum；不能被解释成NuGet签名内容hash算法或已完成新的原生恢复。

## 停写输入

| v3文件 | 实算SHA256 |
| --- | --- |
| `prepare-d.py` | `167ba871760ba53f9e1a629a316ad87b325a1fd9e9e671590ddeda9034199e63` |
| `build-generated.py` | `fbd86dac081c6c12c799514c2c0a4c3aa6a521573c3e75dae11a52536fc35fef` |
| `README.md` | `953392796912854aa2abcc3669005790b90890b50000c5e6fa237d5ea3c35abd` |
| `schema-draft.json` | `a7c8166507b891e9daebd85bc962e725f1acdecd28cb756c1bc2400bd2b6b90b` |

旧v2原文件与snapshot均实算不变，Low manifest仍为 `cfe34c102b48ac1a9138800a8464e2b2edf249bdcff50e00a876b5f71ecbc078`；[B01窄修报告](TP07-D-preparation-astra-rereview-v2.md)仍为 `be887087b6de179532aa12efcd977a7e97515b3b8b2ea96c428f18fce0216038`。[B02 triage](TP07-D-preparation-sidecar-triage.md)仍为 `fa7d9303c402ff437b219c5c179004e617030f41eea5b54d02e803c95e2200fa`，不回写为通过。

Low `.local/temporal-native/verification/TP07-D-preparation-v3-freeze/source-manifest.json` 实算SHA256 `f58bbbdf3794114a6a32dc65ed5428e3520d924c18d257141242745a1883d3c6`；上述四项live/snapshot逐项全部匹配。冻结为source-only，未执行脚本或SDK。

README/schema明确root观察 `0cf076a6…` 是只读33包回读，native_restore_proof=false；不拿它替代未来N0。真实空cache restore、actual lock/metadata、20新包及完整33包证明、构建/CLI事件仍未执行接受。D DB前置模型门和完整EF-C继续pending。
