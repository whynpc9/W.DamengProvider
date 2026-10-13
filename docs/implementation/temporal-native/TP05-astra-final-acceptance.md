# TP05 离线最终接受

2026-10-08。结论：**TP05 offline acceptance recommended / no remaining offline blocker**。root 可关闭 TP05 离线任务；TP04 独立 reviewer 接受后，可激活 TP06 的新候选同包实库验收。本报告只读核对冻结制品、TRX、覆盖映射与哈希，未重跑构建/测试、validator 或 DB，未产包。

## 证据闭环

统一 v7 的九门退出码均0，实读九份 TRX 的 SHA256 与 Counters 均匹配 Low summary，各门 total=executed=passed，无失败/跳过/中止：

| 离线门 | 通过数 |
| --- | ---: |
| TP04 binding/metadata/frozen codec | 57 |
| TP05 Reader contract/routes | 368 |
| TP05 Reader async | 10 |
| 相关 temporal targets | 703 |
| 完整 TypeTests | 798 |
| 完整 CommandTests | 16 |
| 完整 AsyncTests | 134 |
| R3 reader/diagnostics | 83 |
| 完整 TransportTests | 115 |

目标门与完整套件重叠，不把这些计数相加为独立测试总数。388 项 source manifest 在 workspace 与冻结 source 两处均无漂移，10 项汇总程序集 SHA 均匹配。v2–v6 历史失败 summary 实读哈希均匹配 v7 manifest 的保留 pin；旧失败未被本轮成功覆盖。

TP05 的最终测试源码 SHA 为 `7eb0d1a528c700fdf9e7a4b2e6fd3d06c7fb43571741da64010b5477e9483e29`。继 v1/v2 静态报告之后，DmField 两处构造、public bdta fixture 赋值、DateTime/DTO expected 条件装箱均已经 root 定点修复、逐次回退修改后核对哈希及本轮独立运行验证。此前静态审阅未发现的 fixture 缺陷由历史失败记录保留；未通过改产品或削弱 actual 类型/值/offset 断言处理。

99 格独立 execution mapping 与原 policy 的 case ID 集合完全一致且唯一；逐项定位 TRX 中正常/NULL 结果及已记录 async 对应项，均 Passed，normal testId 也一致。mapping 的 TRX、matrix、policy 和 source manifest pin 匹配。原 coverage 仍保留历史 not_run；本次成功记录在新 execution mapping，不回写旧 policy。手工 ResultColumn raw fixture 仅证明 Reader 输入/snapshot/Unknown-format 行为，不升级为真实 decoder provenance 或 DB 证据；实际 decoder 来源由 TP04 完整帧测试承担。

| 证据（相对 `.local/temporal-native/verification/TP04-TP05-v7/`） | SHA256 |
| --- | --- |
| `validation-summary.json` | `268c9d65c0ad0e07b700becedb37b9ab0d2d209a5dfab5a21d77ef02c42fbfe7` |
| `source-manifest.json` | `a5bef77bb9af40c78bf9feaff1e490c717049d4084677a1646fbf03c4227b5a2` |
| `TP05-reader-execution-mapping.json` | `fe0d7d96d1ac1704f61406991695a7694cb4a82790d383ca5da5cc55765ff7a0` |

## TP06 边界

本轮 `offline_only=true`、`database_executed=false`、`package_created=false`，summary 的 root acceptance 旗及 `full_temporal_acceptance=false` 保持原值，不修改 Low 历史输入。真实 scale8/9 非整 tick 的精确文本/CLR拒绝仍仅有内部 fixture 证据；BDTA、非默认 TIME/interval 配置、ApplyResult 缓存切换也不成为公开服务器能力证明。TLTZ 正向继续关闭。

TP06 须使用完整冻结新候选的唯一包，验证 shared/TLS 的实际物理 profile、binding/metadata/Reader/schema 与独立 raw/oracle/cleanup，不能拼接 TP03 旧包或只靠本轮离线通过关闭实库门。TLS 全候选、服务端8/9样本可观察性及最终同包范围按原计划判定；未观测能力不得改写为接受。PR/review/merge 与发布状态不由本报告推出。

完成后停止写入。
