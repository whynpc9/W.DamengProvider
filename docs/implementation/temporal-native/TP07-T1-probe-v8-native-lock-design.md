# TP07 T1 probe v8 原生锁版本有限设计

2026-10-11，Astra High。结论：完整静态对照仅确认一处错误门——`native_contract` 对两项目统一要求 lock version 2，而冻结原生输出分别为 provider 2、host 1。可按本文限改 `validate.py`、`test_contracts.py` 两文件；未发现必须同时修订的第二处实际结构矛盾。本文是设计，不是新源码、测试或 readiness 接受，不授权 SDK/DB/runtime。

本轮完整读取 native 合同及相关 Ref/raw/process/package/binary/runtime helpers、实际 v7 source/native/build/startup/offline/readiness 和锁/缓存/包/原始 JSON/运行时依赖。仅标准库静态 JSON/hash/文本及 PE 元数据读取，未导入项目模块、调用实际 validator、执行 tests/SDK/DB。唯一新增文件为本文，历史证据与实现保持冻结。

## 实际依据

下列路径均在 `.local/temporal-native/verification/` 下；锁位于 `TP07-T1-probe-v7-source/replay/` 的对应项目目录。项目身份必须使用表中完整相对路径，不能以顺序以外的猜测、文件内容或任意版本集合决定预期版本。

| 项目 | 原生 header | 实际 packages.lock.json SHA256 |
| --- | --- | --- |
| `src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj` | exact int `2` | `77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4` |
| `test/TP07TransactionCapabilityProbe/TP07TransactionCapabilityProbe.csproj` | exact int `1` | `69c5a714efcac8afe2f046aa65e82e334a05074c63a0bcd4ad45c64147e40dde` |

| 制品 | SHA256 |
| --- | --- |
| `TP07-T1-probe-v7-source/source.json` | `cb3012a2750e9e447a35e5e0eaa0de2d3eb9be655e844203c08e6d8d6f5043ab` |
| `TP07-T1-probe-v7-native/native.json` | `7ffa132efe4cf60b22c1aa282ea52c3694245d3f2ca85285f8e1fab635f18076` |
| `TP07-T1-probe-v7-build/build.json` | `d190a74be2d6eca96659b7c474f442c1e3fd20583ee7884ff1c5f9b989cb2c52` |
| `TP07-T1-probe-v7-build/startup.json` | `a7c2e705b871f4f64dfa04c18a70a4d2939a21197f3e72fd2709dba746f5c870` |
| `TP07-T1-probe-v7-offline/offline.json` | `ce43452a602526039e3343d354af87cf4189ebba06525793c3d3cc2263bb1fa5` |
| `TP07-T1-probe-v7-readiness/readiness.json` | `45d6192367a088e134d8ef7baa6aec582c5bf0712f7bedb1fc075d1c191dd13c` |
| `TP07-T1-probe-v7-readiness-control/independent-failed-readback.json` | `f0baed9410e60a994d0272ad26ddd69a98cb13156647416cc06221b498206082` |

实际失败为 `native_lock_version`。原 compiled28/337 assertions、Python 两自然退出零及使用真实 readiness Ref 的完整 offline 合同已通过；readiness 本身未独立接受，未产生 runtime 授权。18480 保护对象未变是该失败窗口证据，不能转换为新合同通过。

provider 使用根中央包管理配置及 transitive pinning；host 显式关闭中央包管理、直接引用 EF 10.0.12 并 ProjectReference provider。配置差异与原生锁版本差异相容，但“NuGet 因此生成 2/1”仅属推断，本轮没有核验 NuGet 内部版本选择算法。有限合同直接以冻结真实 source/lock 证据立据。

## 全链静态对照结果

source187 的实际文件及 lineage/inputs 绑定相符。两项目顺序、完整 graph shape、锁根 `{version, dependencies}`、`net10.0` 与展开依赖一致；provider 14 行、host 15 行的 Direct/Transitive/Project 字段合法。新旧 raw 合同相容：native 四 immutable、locked 四 immutable、实际四 live 与两 live locks、完整 JSON 仅五处 `restoreLockedMode=true` 新增、真实 project 路径、pruning/frameworks、startup 相对路径及 lock-before/after 全部对应。

六条实际 SDK receipt 与 argv/cwd/十字段过程合同对应，均自然零退出且满足 180 秒边界，无超时/信号/reap；本轮未重新执行。28 组 archive/metadata 完整且有序，实际 ZIP/sidecar SHA512、cache/feed 来源、nuspec ID/version、解包字节以及 metadata/lock/root-origin contentHash 各自一致。`.nupkg.metadata.version` 全部为严格整数 2，其独立合同绝不随锁版本修订而放宽。

26 个 binary 的完整枚举与 hash 相符，独立从六个 DLL 的 PE 元数据静态读取 Module.Mvid 与声明相符；host deps 的 Core12+W runtime 集合、EF 10.0.12 和 provider 0.3.0 ProjectReference 均受现合同接受，Analyzer 为 build-only。Core13+W 的原生包集合、不含 official/Design/AspNet、空 build.offline_proofs 也相符。未发现其他必然失败差异；这些静态结果不能代替 Low 执行完整新 `native_contract`。

## 两文件实现边界与验收测试

`validate.py` 只把 `native_lock_version` 的固定期望改为按上述两个完整 PROJECTS 身份选择的有限常量，继续使用严格类型相等；固定项目集合及顺序先行验证。不得使用通用 `{1,2}`、从 actual lock 反推期望、bool/int 宽松比较或从未批准输入增加项目。原 lock exactshape、真实 replay 路径/Ref/hash、依赖图与全部后续校验原样保留；无需抽取或改写 native 的其他算法。metadata version 2 保持原代码。

`test_contracts.py` 保留原 66 方法 AST，新增有限合同测试；由 High 冻结时列出实际方法名、集合及数量，不预填通过数。测试须覆盖：

1. 直接以本文冻结真实 v7 inputs/source/native/build/startup 及原 Ref 调用完整新 `native_contract` 的正例，使全部后续 package/raw/process/binary/runtime 分支实际执行；不伪造 ROOT、SDK receipt 或 runtime approval，不导入 producer 当 oracle。该正例只是旧真实制品的合同兼容性证明，不提升旧 readiness 状态。
2. 在同一完整调用边界，测试专用 `read_ref` 包装先调用原函数核验原 Ref/实际字节，再仅改已加载的锁对象。分别拒绝 provider 1、host 2、两者互换，以及非许可整数、bool、float、string 等错误类型；明确断言版本错误来自 `native_lock_version`，不能靠整文件 SHA 提前拒绝代替语义覆盖。
3. 同样的真实 Ref 内存变异覆盖锁顶层缺字段/额外字段、依赖值变化，分别到达原 shape/graph 合同；项目或跨项目 lock path 替换应到达固定项目/实际路径或锁绑定门。独立的实际文件/Ref hash、symlink/escape 负例保留真实物理边界，可复用既有受控 fixture，只改临时副本，不修改原历史文件。不可把语义 Ref 包装用于这些物理检查而跳过实际核验。

README、prepare、run、policy、host9 及其余 15 个 tracked 文件不变。producer15 排除 validate/test，继续为 `d4c64e9034c804b92a896ab0c352c0379979ee3f1674e8f7864755a1ba0b4c3d`；无需新增 producer override 或版本文案。原 V4/V5/V6/V7 fixture pins 保持，避免自指。host9 aggregate 继续为 `0f99c744ac71e3c099a219399068a53e66117a5998812339b986c4551d434d40`。新 actual17/toolpack 必须重算；旧 actual17 `611588a4e41cb14a0b936f43fe51b9e1779a4206e14994c04dcb8c579ed498b7` 仅为历史。

## 后续门与停止条件

root 读回本文后另行授权 High 两文件实施；冻结及 Astra/root 源码接受后，Low 才可在新静态 pack/inputs 绑定下执行完整原 66 加实际新增方法。随后新 source187 → native → locked build → compiled28/offline → 独立完整 native/offline/readiness 依次重建与核验 lineage，失败即停。必须用真实 readiness Ref；旧 v7 full offline 与 28/337 通过不替代新链。若实现或验证发现第二处真实合同差异，先交 root 定范围，不自行扩大修复。

产品178、W包、产品824、TLS/证书/保存点、process/DEVNULL/180+2、十字段 SDK receipt、CLI13、snapshot 覆盖范围及 runtime4 均保持；无需重跑产品824。runtime4 仍待单独批准。本文完成后停写，所有旧源码快照、失败及成功证据原样保存。
