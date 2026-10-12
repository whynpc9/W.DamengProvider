# TP01 physical v11 能力与证据修正交接

2026-10-08。状态：**physical_baseline_evidence_reviewed / corrected_profile_handoff_accepted**。root 已接受限定设计；本次独立逐 case 复核支持按下述两个精确物理 scope 更新 compiled profile。**这不是 TP03 新 candidate、TP04/05、同包 A/B/C 或发布验收通过。**

本文件补充并修正 `TP01-profile-handoff.final.md` 的身份来源，不覆盖原文、v10 报告或失败证据。旧工具将 logical ConnProperty 的 encoding/msgVersion 与 physical public ServerVersion 混合记录；实际产品 adapter 原本正确读取 physical。v11 重新使用原 v23 包跑完整基线，解决该来源缺陷。

审阅仅读取冻结 JSON、源码和制品哈希，独立以整数按位解码保存 raw 并比对字段；未运行 dotnet、测试、validator API、数据库或修改产品。

## 1. 修正后的精确 scope

| 能力状态 | ServerVersion | MessageVersion | physical effective encoding | 必要前提 |
| --- | --- | --- | --- | --- |
| VerifiedSeven | 8.1.5.60 | 21 | utf-8 | encoding override 为空；ordinary；类型及 write scale 门继续独立检查 |
| VerifiedSix | 8.1.4.6 | 11 | gb18030 | 同上；七位继续关闭 |
| CompatibilityLegacyUnverified | 其余身份 | 不作版本/消息继承 | 不作编码别名扩张 | 保留既有 legacy≤6、可靠 fixed26/27 六位窄兼容 |

两环境 observed timezone=480、用户及 Schema=WDM_PROVIDER_TEST。shared O/W 相同物理身份；TLS 仅 W，O 仍为 `closed: no_verified_strict_official_tls_configuration`。shared 为显式 PlaintextAllowed；TLS 为既定本机 CA 的 RequireTls/explicit NoCheck，并非关闭证书验证。传输名称不作时间能力键。

v11 每条实际 profile 均满足：

- `profile_origin=physical_get_conn_instance_conn_property`。
- `encoding` 为 physical ServerEncoding getter 的实际有效编码。
- `negotiated_encoding` 来自 physical 私有 serverEncoding 字段，`negotiated_encoding_origin=physical_conn_property_serverEncoding_field`，不宣称采集了原始 STARTUP bytes。
- `encoding_override_empty=true`，有效与协商编码大小写归一后相同。

原 shared `(8.1.5.60,21,gb18030)` 与原 TLS `(8.1.4.6,21,gb18030)` 标签没有这次物理来源证据，不再保留为 verified；它们按 unknown 兼容规则处理。非空 override 即使与协商编码同名，也不继承这次仅 empty=true 的前提。

## 2. 逐 case 能力核对

shared 三 lane 为260/334/106，共700。W core typed writes：**七位63次 written**（CType26=23、CType27=40），**六位12次 written**（16/23 各6）。每次 affected_rows=1、fresh persisted_rows=1；每次均有 O 与 W oracle。

对这75次成功写入的 **150条 O/W packed raw** 独立按字段位置解码，核对 payload SHA/实际长度、年月日/时分秒/ns/offset 与请求 ticks 及 actual_server.fields 全部一致。独立生成的 FF9（TZ含offset）与 FF7（本工具模板明确不含offset）都精确匹配。W GetValue、typed、generic 的 ticks/original offset 及后续 Read 完成均匹配。没有以新 encoder 生成 expected，也未把 O typed 行为当唯一真值。

| 分组 | 可定位 case | 结果 |
| --- | --- | --- |
| TS/TZ 七位 | dt7/dto7 min、min_plus1、max、max_minus1、max_minus9、ordinary_tick1、ordinary_micro、tail0…9、leap/nonleap/midnight；另 offset 和 Kind 组 | 26/7/9-byte、27/7/11-byte 保持100ns；保本地字段与原offset；W普通 DateTime Kind 为既有 Unspecified 合同 |
| TS/TZ 六位 | `dt6_` 与 `dto6_` 的 min、max_minus9、ordinary_micro、century_nonleap、midnight、tail_0 | 共12次，16/6/8-byte、23/6/10-byte 精确 |
| 非整微秒六位请求 | 原28个请求 | Overflow，persisted_rows=0，不降精度写入 |
| 保守 offset 外请求 | offset_N840、local_min_N840、utc_max_N840、offset_N780、local_min_N780、utc_max_N780 | 原6次 Overflow，persisted_rows=0；不据旧 guard 推断服务器绝对能力 |

TLS 三 lane 为131/168/75，共374。W六位成功仍为相同 dt6/dto6 六组，共12次；独立核对 **12条 W packed raw** 的 SHA/长度/字段、请求 ticks/offset、FF9/FF7、GetValue/typed/generic及后续Read全部精确，affected/persisted均1。六位其余28个非整微秒请求为 Overflow且 rows0。

TLS 七位有 **63次 NotSupportedException**，均为 `advisory_profile_request`，persisted_rows=0；另上述6个负offset请求先被旧 guard Overflow，rows0。不得写成69次全NS，也不得以旧 W 的拒绝宣称服务器不存在七位。新 candidate 仍须按目标能力优先的严格合同验证。

对两环境各三个 lane 的 report/独立 cleanup 共12个制品，实读哈希与安全 summary 完全一致；process/cleanup退出均0，TEST身份和 physical profile 正确，最终均 `owned_table_absent_fresh_connection`。两个 runner 与 characterization validator 哈希也核实；validator 均 issues=[]。

## 3. 证据身份

安全汇总：`.local/temporal-native/verification/TP01-v11/physical-baseline-verification-summary.json`，SHA256 `f83cc93bf6c6465915d14c03b8d42417ea906b6eb27fbdb4cfca1606cf11aab1`。

| 制品 | shared SHA256 | TLS SHA256 |
| --- | --- | --- |
| core.json | 2d31eb4726e3d03f52c7cc008fa4a66fc7450c662c8d984e2588e6b5cd06dbbf | 0346f61b448bbe6ce867ef7cb9f8ab18561ca300181c6947b3260897dc306ed8 |
| metadata.json | b352745d9ef75e541b279427337332216b572d993d5338d10d57c8a24aa95ab3 | 83f69d9422b256d28f82085f5a874bf0816680875d582d6bbd4a3b76f5c69cfa |
| extended.json | 375ae551c020f9688c0054dd988892eba162613ab42b8f03641531f70ff599d6 | 9b6327cf417e97927b0b22ce4623689d3e157d91424e074e444617306f52b08a |
| characterization-validation.json | 6ade2eeccd18d867554c8c0771971ebea993fcb33b268bc771cf3460909461c3 | 06e51fe08e71c9b66e07d1f7294f0945d846d8133950a98902297bd9db6c89db |
| runner.json | 9cd8076eb77bf05e464d2218550aec14390e57db42f6e7629eacdec5de4e3fe0 | ac6cce321109ed7e25c0d79d70439dbf67673c330fb3e0c997511854d353c934 |

目录分别为 `.local/temporal-native/verification/TP01-v11-shared-baseline` 和 `TP01-v11-tls-baseline`。各 cleanup 哈希与逐 lane fresh absence 收据保存在上述安全 summary，已实读核对。

两环境同一工具 source manifest：`6c1a3a1dc3ed75374e02b7047da1682b657ba577bf660f9bc08e89272bc5a4bb`。实际仍为归档 W `0.1.0-r3.20261007143838`：package SHA `6696a26d89e4caa1b9e7c40a73f024c4df9d8a695da1446bdbecc17757d3bcf9`，DLL SHA `c70f7d4b7bd37187f55483d186d658206232a2e1f60a662530b3a0282e3231e5`，MVID `69e5632e-75c4-4672-b924-57b8ffb58a52`。各 report/cleanup 的 actual_asset/package_reference receipt 均匹配该身份。

产包 source manifest `241e15b52b436a55fe3a299e777b281cee0d6b3c22782a0323de311e1d87d1d5`，producer HEAD `2b39fec0fa1bc8974f8e491f9bdb5a6113d14631`；comparison HEAD `fe89bd44abe99c1608499f71f4634a13e732c826` 不是产包 source。identity JSON SHA `9f18cdfee3b8bb4fba7f08477514ecc68ba47b205c0d9b1c3d8ba5ae5248063d`。

shared O 为原 `DM.DmProvider 8.3.1.47463` net9：package SHA `62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75`，DLL SHA `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b`，MVID `d909b5af-965f-4cf9-a80a-a6181124e361`。TLS 仅验证其资产身份，不存在 O TLS 实库接受。

## 4. 产品与工具交接边界

可以按 root 已接受的最小设计实施：内部 immutable profile identity 增加必填 EncodingOverrideEmpty，无默认true；adapter 从当前 physical 字典定点读取，缺失/坏类型/非空均 unknown，并避免坏键触发 getter 索引异常。只登记§1两个 exact tuple，EvidenceId引用本次v11证据。纯 helpers、不涉及连接的表示能力、ValidateTarget/Value严格规则和 unknown 既有六位兼容不变。

wiring 更新仅重pin v11依赖、强校验 physical schema/shared exact utf-8 identity；严格写读结果、独立raw oracle、NULL、helper及恢复门不降低。新 v6 产品/工具冻结后重新独立验证和同包生产路径取证。

原 handoff 的范围限制继续适用：8/9新写、TLTZ值读写、TIME>6仍关闭；offset保守域[-779,840]；1582-10-05…14含日期参数仍NS。fixed extended六位和8/9非整tick服务器样本的未观测边界不因来源修正变为实测接受，pure/internal fixture不等同实库支持。不得把修正编码或message版本当作扩大其他能力的理由。

两个 characterization validator 的 candidate/full/baseline-capability/wire-layout总旗仍false，保持原报告原值；本文仅接受具体逐操作能力前提与修正后物理身份。TP03-v5失败 summary SHA `fb6bcd6aa5f5254528a3e1681c8d6b69d886bc162013aa5c9f0522ec462c9461` 继续保留，不能由本基线报告冲销或改成新 candidate 成功。
