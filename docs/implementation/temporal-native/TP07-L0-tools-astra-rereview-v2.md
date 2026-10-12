# TP07 L0 Python 工具组合静态复核 v2

2026-10-09。结论：**v1 R01 在源码层关闭；六文件工具包与既有七模板组合静态接受，可进入 Low 独立离线合同和 source-only prepare。** 未发现本次窄修复的新增确定阻塞。新 toolpack manifest 的实际 SHA、离线测试、L0 新锁、locked restore/build 和 DB 结果仍由各后续门单独接受。本轮未运行 SDK、测试、prepare 或 DB，未修改实现；只新增本报告。v1 报告保留。

## 冻结范围

实读四工具 SHA256 与 root 派发 v2 完全一致：

| 文件，位于 `tools/DownstreamAcceptance/TP07/` | SHA256 |
| --- | --- |
| `runtime-run.py` | `6c23c42a378699c322ed7acfd6078301192b9c35490ff85e3b7606144033f649` |
| `runtime-validate.py` | `4fe33ecc43cb56ee53e610d180dbf476fc057d48ebcfa1cb3493d90b8745d16b` |
| `runtime-cases.json` | `68d24c496e73bb4c04bde45c869b9405760b347cdd5767863eb14a45d9003335` |
| `test_runtime_contracts.py` | `775fdb255c38ff05abc884feaef4e55070886d9a7ae9f67709b9b04a5d672e59` |

两实际 Python 依赖保持 `managed_identity.py = ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac`、`tools/TemporalBoundaryProbe/packed_oracle.py = bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9`。七 C# / project 模板接受范围继续沿 [七模板审阅](TP07-L0-astra-review-v1.md) 的 aggregate `e83129a1572991a81cb11a3d6a6e7d6f34453ab2e5805b53978de91d0ec69fe1`；本次修复不改模板、mapping-v4、W 包或任何 temporal 值政策。

## R01 关闭理由

- **有限外部信任根。** runner 与 validator 都强制要求 `--toolpack-manifest` 和独立批准的 `--toolpack-manifest-sha256`。manifest 顶层仅允许 `schema_version`、`task`、`source_hashes`，task 固定 `TP07-L0 toolpack`，hash 字典恰好六个固定仓库相对路径；不接受漏项、额外 helper 或任意导入路径。两端都拒重复 JSON key/nonfinite，核 manifest 字节 SHA 和实际执行 `ROOT` 下六文件 SHA。实际 Low 清单必须采用这三个顶层键；其他 provenance 应放另一个 evidence 文件，不能塞入该严格 API。
- **全过程绑定。** `prepare` 保存本次 pack 身份及前后检查；lock/build 在 SDK 前核相同身份、前阶段收据、source manifest 和 lineage；每次 SDK 子调用前后再核。lock manifest 指向 prepare/lock 收据，startup/build receipt 延续相同链。run 在 DB 前核 build/source/lock 链，将六工具、toolpack manifest、阶段收据一并加入运行前后输入。validator 独立重核实际工具、所有阶段身份/哈希、prepare→lock 唯一新增锁差异、实际二进制和必需运行输入集合，缺失或不一致不产生有效验收。
- **实际子进程有结束收据。** `internal-host` 从原 exec 替换为受监督的 `subprocess.call`，启动前核 pack/startup，子进程结束后再核 pack/startup，并写 profile、main/cleanup、child exit code 与 toolpack 前后检查。外层记录收据 SHA，validator 强制四个 main/cleanup host 收据匹配且自然成功；child 失败、timeout、缺收据或结束后漂移不会通过。
- **解码器字节就是执行字节。** MVID helper 从固定位置读取 bytes、对照完整 toolpack 后 compile/exec；packed oracle 从固定位置读取 bytes、对照原 policy pin 后 compile/exec。两者均不从可能存在的未追踪 `.pyc` 执行，保留了原独立 oracle hash。无需修改已审 helper 本体。

这满足 v1 要求的实际工具冻结身份闭环；批准者仍须在各阶段传入同一个受审外部 manifest SHA，不能把任意自行生成的 pack SHA 等同 root 批准。

## 其他窄修复及不回归检查

`internal_environment()` 现在由 `public_env(stage)` 构建 dotnet 环境，实际创建任务目录的 `DOTNET_CLI_HOME`，保留 `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`；仅将当前 shared/TLS TEST 变量映射为子进程的 TEST 连接变量，不继承无关管理员变量。原 restore/build 的单节点与禁 build server 参数未放宽。

内部 host 的 `subprocess.call` 不设新 session/process group，因此 dotnet 保留外层 wrapper/internal-host 的进程组；外层 timeout 的 killpg 仍能覆盖实际 dotnet 子进程。主业务非零和 timeout 在工具未漂移的冻结窗口中仍走已有独立 cleanup 路径；没有用新 host 成功收据取代 C# finally 和 fresh absence 验证。

对照 Low v1 snapshot 的源码 diff：validator 的 literal/descriptor/raw/FF9/typed、fixed case、bare rejection 和 owned cleanup 函数未改；policy 仅新增 toolpack 合同；原 `L0Contracts` **15 个**方法正文未改，新增 `ToolpackContracts` **7 个**方法。新增源码涵盖六文件漂移、缺项/多项/旧 SHA、跨阶段身份/检查缺失、阶段退出漂移、收据重配、dotnet 环境及 host 成败/结束漂移。这里的 22 是静态方法数，尚非已执行/通过数。

template manifest 的 API 仍是 `source_hashes`。已查看 Low v1 派生 `source-template-manifest.json`，其恰有原七项及 prior `66f915…`、aggregate `e83129…` 来源；当前 runner 按已固定 policy 再逐项核模板。v2 应在新证据目录使用经独立核实的派生清单和获批 SHA；保留原 snapshot 和 v1 证据，不能直接把只有 `files[]` 的历史清单传给此接口。

下一步仅按既定门顺序推进：Low 固定完整六文件包及实际命令，运行 22 项适用离线合同和独立源重放；新 L0 锁审阅接受后再 locked restore/build。实际 startup/EF/W 身份和阶段收据获接受后，root 才开放原定 shared12/TLS2 L0 窗口。任何真实默认七位 literal 丢位仍硬失败并停止依赖组；本报告不宣称 L0 runtime、完整 EF C 或发布接受。
