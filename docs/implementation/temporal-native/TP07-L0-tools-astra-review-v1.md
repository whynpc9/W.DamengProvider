# TP07 L0 Python 工具组合静态审阅 v1

2026-10-09。结论：**changes_requested，存在一项工具执行身份闭环缺口；尚不接受完整组合工具进入 DB 验收。** 值判定与既有七项 C# 模板的组合合同未发现其他确定阻塞。本轮仅阅读源码、既有证据和计算文件哈希；未运行 SDK、合同测试、工具 prepare/restore/build/run、数据库或修改实现。仅新增本报告。

## 冻结身份

以下四项 SHA256 与 root 派发值及本次工作区实读一致：

| 文件，位于 `tools/DownstreamAcceptance/TP07/` | SHA256 |
| --- | --- |
| `runtime-run.py` | `813b2f9831da6ea0c16244783b786e2e078e50f7c34e90ba681f395f6c237479` |
| `runtime-validate.py` | `f175e7c69b1cb6ad32576f98c99fe85b6144d6d0b3b9ccd8d0b07eb9e89609e3` |
| `runtime-cases.json` | `60d32cc12106b7fb11cb128cb8f335275cf822cb87dac370bce178a3a6a4eeca` |
| `test_runtime_contracts.py` | `f36eb358849303801fd76afde7b775def9c25a2bb6ce61990dcce43c82a04b0a` |

七个模板逐文件仍匹配 [C# 静态验收](TP07-L0-astra-review-v1.md) 中的七项 SHA；其已接受 aggregate 为 `e83129a1572991a81cb11a3d6a6e7d6f34453ab2e5805b53978de91d0ec69fe1`，Low 原 snapshot manifest 为 `66f915aa1a9bd5cb11eb191f5a7f0fd613f9983f639b77650b9dbd30b3794439`。

本轮实际工具依赖另包括 `managed_identity.py`，实读 SHA `ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac`；以及 `tools/TemporalBoundaryProbe/packed_oracle.py`，实读 SHA `bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9`。两者仅依赖 Python 标准库。

## R01：工具清单已记录但未约束实际后续执行，且漏掉 MVID 解码器

**验收阻塞，范围为工具冻结身份，非新增安全或产品能力要求。** `runtime-run.py:123–129` 在准备清单内记录四工具哈希；但 `offline()` 只核准备清单和 EF replay 源，随后 `runtime-run.py:208` 从当前 `HERE` 导入 `managed_identity.py`，未校其哈希，也没有将其列入准备清单。`run_runtime()` 的前后输入集合（249–253）不含四工具及这两个 Python 依赖。`runtime-validate.py:314–354` 核对源、锁、lineage 和运行输入，却从不消费 `source_manifest['tools']`。

因此，prepare 后发生工具文件漂移，prepared/startup manifest SHA 仍可保持不变，后续阶段仍能用另一份 runner、case policy 或 validator 执行；MVID reader 甚至没有初始记录。已生成的 `input_hashes_before == input_hashes_after` 不能证明实际工具与审核冻结版本一致。`packet_decoder()` 对 `packed_oracle.py` 的既有 policy pin 是正确局部防线，应保留，但它不能补齐四工具和 MVID reader 的缺口。

最小修复：建立由 root 接受 SHA 的完整 toolpack 清单，包含本四项、上述两依赖，并在 prepare、lock/build、run/internal-host 与独立 validate 的适当入口核实际执行目录；将工具闭环身份带入准备/启动/运行证据并在最终验证时复核。也可由另一个**受审且强制执行**的 Low 入口完成同等阶段前后检查，并从冻结副本运行；单纯复制依赖或事后列出 snapshot 哈希不足以关闭本项。无需改 C#、W 包、mapping、数据库范围或做通用工具框架。

关闭证据应至少证明：固定 toolpack 正控可读取；任一工具、case manifest、MVID reader 或 packed oracle 漂移会在相应 SDK/DB 操作前停止；结束后漂移不能得到成功验收。修复后重新冻结工具并由 Low 运行适用离线合同，保留本 v1 审阅。

## 已检查且无新增确定阻塞的组合路径

- **源与包链。** `prepare` 对固定 archive 做完整文件集检查，分别 check/apply bridge 与 mapping，限制 P1 两锁注入和最终 unit lock 差异，再与获批 EF 源全量相等；七模板逐文件匹配 policy，后加入独立 replay。批准的具体 mapping-v4、bridge 和执行源 manifest SHA 由 root/Low 的实际命令输入承担，不能把自行提供 SHA 的同名文件称为 root 批准。W 仍为 `0.1.0-temporal.20261008144915`，没有新包或 W ProjectReference。
- **新 L0 锁与原依赖。** lock 阶段只容许 provider 原非驱动集合、精确 W 和一个 provider Project；核原版本、依赖关系及 old/new/SDK metadata 的原生 contentHash，单独核 nupkg archive SHA，不再误把签名包整体 hash 当 NuGet contentHash。provider 原锁须字节不变；build 阶段另要受审 lock SHA、locked restore 和 no-restore build。实际新锁的 13 个非驱动包清单、case-insensitive 唯一 Project ID、source/lock 不漂移与 SDK 10.0.401 仍需 Low 的实际输出。
- **DB 前实际身份。** build manifest 固定 startup DLL/MVID/deps、EF provider DLL/MVID、两份 assets 及源码/锁；C# 强制读取相同 manifest SHA，核实际 loaded W 和 EF provider，deps 拒官方驱动。此前七模板的入口强制绑定修复继续有效；本轮没有把这些静态控制称为已构建通过。
- **连接与清理。** runner 只调用固定 SHA 的 TEST wrapper，internal-host 将选定 TEST 变量留在进程环境，不放 argv；C# 显式 shared PlaintextAllowed 或本机 TLS RequireTls/CA/client/NoCheck，开连接后核 TEST user/schema 和物理 profile。主进程非零或 timeout 后，只要已生成 ledger，runner 仍执行独立 cleanup；C# 本身 finally cleanup，独立 cleanup 也再次 finally 核 absence。缺 ledger、cleanup 未运行或失败均不能成为成功。现有 nonce、结构、marker 与未知 CREATE 保护沿用七模板接受范围。
- **精确 literal 与独立值。** validator 使用固定完整 `TIMESTAMP '…'` 字符串及 SHA，比对 6/7 位和原 `+05:30`，没有依赖 producer 的 Contains 作为最终 oracle；六位 prefix 冒充七位已由合成负例覆盖。真实 raw 来源、ResultColumn 原始及有效 type/width/scale/mask、schema scale、bounded hex 与 payload SHA、独立 packed decode、完整 FF9、typed local ticks/original offset/UTC 逐层比较。producer 自报 decoded/expected 不充当独立期望。
- **有限 case 与失败。** shared 12、TLS 2 的集合/唯一性/完整状态和每 case ownership 收据受核；bare micro 仅 characterization，bare tick 只接受本地 OverflowException、无返回 literal、0 业务写入、fresh rows0 且无持久化/ADO oracle。主/独立 cleanup 自然退出、报告及 ledger 哈希、fresh absence 和运行前后源哈希都必需；缺字段、partial、非零或 timeout 不能通过。不会把本 L0 升格为全部 EF C。

## 执行前输入说明

`runtime-run.py:114` 的 template manifest API 要求 `source_hashes` 字典。原 `66f915…` snapshot manifest 实际为 `files[]` 格式，不可直接作为这个参数。root 已明确 Low 会在新证据目录生成适配清单；runner 未硬编码原路径，因此本项**不是代码必现阻塞**。新清单须保留原 snapshot manifest/aggregate 来源，提取恰好七项相对路径，独立核字节并由 root 接受其 SHA 后传入。原清单和历史证据不得重写。

修复 R01 并冻结完整工具包后，可进行 Low 独立合同、source-only prepare、新 L0 lock 审阅和 locked build。只有相应证据获接受后才开放同包 L0 的 shared/TLS 窗口；任何真实默认七位 literal 丢位仍是硬失败，保留原失败和清理证据，停止依赖的昂贵组。本文不宣称 SDK/tests/DB 通过，不改变 TP06 A/B、M01/M02 离线已接受范围或 C 门未完成状态。
