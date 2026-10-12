# TP07 M01/M02：映射与读取表达式离线验收

2026-10-09，root 接受 mapping-v4 的限定离线范围。Sol High 实施、Astra High 设计与代码验收、Sol Low 独立重放和运行。**真实 EF shaper、SQL literal 持久化、scaffold、迁移以及 C 门仍待实库验收。**

映射补丁基于 P1 的固定 EF 源和同一个 W 候选包。175 项源码中，11 项为本次新增/修改；产品实现将时间 mapping 的小数秒声明转译为 W 参数 Scale，保留模型 facet、clone、NULL 和 decimal 行为。默认 DT/DTO 仍为七位，显式六位保持六位；裸声明无 facet 采用明确的六位兼容政策，服务器裸类型行为另由实库观察确认。DTO 读取选择实际 W typed getter；DATE、时间量化、历法转换区间及 TLTZ literal 关闭边界有专门检查。

SDK 10.0.401 的新测试、受影响回归和独立模型观察依次通过，所有进程退出 0：

| 门 | 实际结果 | 证据含义 |
| --- | --- | --- |
| 新 TP07 三个测试类 | 51/51，零跳过 | 实际 mapping/参数、严格 literal 字符串、编译 getter 表达式调用真实 W 缓存 reader；不等于真实数据库 shaper |
| 受影响原有映射/时间翻译回归 | 102/102，零跳过 | 同一已构建 unit DLL；其中两个身份检查与上组重叠 |
| 不同测试用例并集 | 151 | 不将重叠的两项重复相加为 153 |
| 真实 EF 模型参数观察 | 14/14 | 七个实际 IProperty 各 value/NULL，生产 EF connection factory→W command→mapping.CreateParameter |

观察结果：默认 DateTime/DateTimeOffset 为参数 Precision0、Scale7；显式六位 DateTime、TimeOnly、TimeSpan 为 Precision0、Scale6。上述时间 Precision 均保持未显式设置、Scale 显式设置；decimal 为 Precision12/Scale2、两者显式设置。模型和映射自身的 Precision 含义不变，interval 的 leading precision 没有误传为参数 Precision。

单元项目原有 49 个非驱动包的版本、依赖关系及原锁/新锁/SDK metadata contentHash 已独立核对。新 unit lock 仅将官方驱动切换为精确 W 的 CentralTransitive 依赖，并更新 provider 项目依赖。v3/v4 只修测试，逐项证明产品、依赖文件和锁字节不变后复用此图；没有重复 force-evaluate，也没有把历史恢复事件记作本轮新执行。

最终 mapping manifest SHA256 `bf8ded5959060658d66053aac99814388b730d142c28562d48746df652e73336`，完整 patch `0b972736620c58137a66acefe947d3fd8f4ddde524fae4cd3c3719741e29b021`，独立源码 snapshot manifest `d5fb6ee75e36df8afdd635be469b4c83eb1f2e0b2a36709590c5c0b4b781350f`。执行 summary `95aa7eb702ce1f0ad2a4ed1779cc4637a47c26b783ce615a39e358144b198550`，独立 readback `224bb25afa1195570eca8eb7cda54e123f7c028712af1f1ad66cfbfc59bdef72`，14 项观察报告 `4911343b6f0609ed9fb006fd9d11cb929c89c967658e153d3eda07a4b1c45ce5`。

unit 与 observer 在独立目录构建，EF provider DLL 身份分别记录并核对实际加载对象，不混称同一二进制。两者的 W DLL 均为 SHA256 `843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`，对应 TP06 接受的 `0.1.0-temporal.20261008144915` 包。全部原源码、依赖锁、历史失败及 W 产品输入前后无漂移。

历史记录保持原样：v1 静态审阅发现 before-first reader fixture 和 TLTZ literal 两项问题；协调器 schema 字段误读在 SDK 前停止；v2 build 因 xUnit2012 失败；v3 实际 51 项中 50 通过、1 项在无效模型测试 Dispose 阶段失败。v4 将该测试连接交给显式调用方持有，保留真正 Context.Model 拒绝断言和正常 Dispose，并验证 context 不代为释放、调用方释放一次。v3 的清理异常可能掩盖正文断言，只有 v4 的实际通过证明该完整负例成立；没有吞掉异常或改产品生命周期。

参见 [Astra 最终范围验收](TP07-M1-astra-acceptance.md)、[v3 失败归因](TP07-mapping-v3-triage.md) 和 [17 项原始证据 manifest](../evidence/TemporalNative/TP07-M1/manifest.json)。下一门为最终 overlay 上的 L0 实库 literal 验证；失败必须保留并停止依赖的昂贵实库组。
