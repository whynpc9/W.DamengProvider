# TP-01 基线与能力交接验收

日期：2026-10-08。root 结论：**baseline_characterization_accepted / handoff_ready**，关闭 TP-01。TP-02 的整数核心离线验收已关闭，TP-03 的依赖现已满足。

本次接受的是现有 O/W 基线表征、探针及限定能力交接，不是新驱动候选、EF 或发布验收。所有原始报告中的 candidate_acceptance、full_temporal_acceptance、baseline_capability_acceptance 总旗保持 false；不回写这些旗标。

## 输入与独立分工

- Sol High：tp_probe 实施采集端，tp_inventory 实施独立 Python 校验端；所有工具已停写。
- Sol Low：tp_verify 独占离线、构建和真实数据库验证窗口；当前全部窗口释放，没有待恢复对象。
- Astra High：设计、全部关键静态审阅和最终逐案例证据审阅；root 独立回读最终 validator、运行结果及能力裁决。
- v10 的 17 输入 aggregate SHA256：276a08a3ae5645802fb93948c9d4972e7ac9e492930b1096b5441024f767d56e。
- v10 源快照 manifest SHA256：48038301a7b902b43f80688ce4a8a6f149f06322d7c84e819336296efbb010ed。
- [v10 静态验收](TP01-astra-rereview-v10.md) SHA256：7b42746e60865bd1ad2dedcc3e9eebf45c8af35e40aab6018b019e39b627569c。
- [最终 profile 交接](TP01-profile-handoff.final.md) SHA256：0a0f2c1a125c9acff57fecc99f7b41a963e95d07b5e65565f8f2232e529de759。

实际 W 基线包为归档 v23 的 0.1.0-r3.20261007143838，producer source head 为 2b39fec0fa1bc8974f8e491f9bdb5a6113d14631。fe89bd44 是本次实施比较基线，不冒充归档包的生产来源。O/W 包、实际加载 DLL/MVID、源清单和工具输入均在运行摘要中精确绑定。

## 实际验证

| 范围 | 结果 | 接受含义 |
| --- | --- | --- |
| v10 受影响离线 | portable 67/67、local-required 17/17、真实源包链和实际 shape 消费通过，无 skip | producer C# 未变，离线明确复用 v9 的实际 shape；不称为新 C# 执行 |
| v10 shared 完整运行 | 新构建与实际 C# 自测、core 260、metadata 334、extended 106；runner/各 lane/独立 cleanup/validator 均 0 | 700 个基线观察，最终 issues 为空，三个对象均由新 TEST 连接确认不存在 |
| v10 TLS 完整运行 | 新构建与实际 C# 自测、core 131、metadata 168、extended 75；所有进程与 validator 均 0 | 374 个基线观察；RequireTls、既有本机 CA 显式 NoCheck，未改变产品默认；O TLS 保持关闭，三个对象新连接确认不存在 |
| 完整性 | 17 输入前后、旧源快照、1546 项历史证据及旧失败记录无漂移 | 新报告独立保存；旧失败未改成通过 |

新运行均加载唯一 TEST 身份及同名 Schema。没有使用 SA/admin/bootstrap，没有跨 Schema 操作、证书/模式或实例设置变更。

## 精确证据与批准范围

shared 的实际 profile 为 8.1.5.60 / message 21 / gb18030 / ordinary；W core 的 63 次七位写入和 12 次六位写入，经 O/W 双路、独立原始字段、服务器文本及 getter 核对精确。root 批准该范围作为后续普通 TS/TZ 七位写及稳定默认选择的服务器能力前提；新的绑定和产品行为仍须 TP-03～06 验收。

TLS 的实际 profile 为 8.1.4.6 / message 21 / gb18030 / ordinary；12 次六位写入精确。七位 closed 请求保留实际 63 次 NotSupported 和 6 次既有 offset Overflow，不能将它们改写成全部 NotSupported，也不能推断服务器不存在七位类型。产品在此范围保持七位关闭。

TSTZ 的直接 EXTRACT 存在小数秒量化/秒末错误，旧 CAST 诊断的源值也发生改变。因此仅 TSTZ 精确分量门改用独立 Python 位置整数解码真实 cached packed bytes，并继续与 FF9/FF7、规范请求、O/W getter 严格交叉。普通 TS 的 EXTRACT 精确门保留。原 EXTRACT 值、错误和失败轮次不改；详细理由见 [oracle 方法限定修订](TP01-components-diagnostic-v2-triage.md)。

官方泛型 NULL 的 231 个观察为安全 InvalidCastException，作为 O 的已确认 NULL 表征独立接受；W 仍要求原有 DBNull/6081 契约。见 [NULL 表征处置](TP01-shared-v9-triage.md)。

8/9 位写、TLTZ 和新增 TIME 精度仍关闭。非整 tick 的 8/9 服务器样本及 fixed extended/scale6 实库组合本轮未取得；其合法 raw/绑定合同后续仅按明确内部 fixture 验证，不冒称本库实测。未知版本保留限定既有六位兼容；1582-10-05～14 参数写入按保守未验证转换区间拒绝。具体语义和范围以最终 profile 交接为准。

## 可审阅材料与下一步

公开证据清单（历史记录已归档）保存每个源报告和公开制品 SHA256。六份完整场景 JSON 使用确定性 gzip 保存，解压字节与原报告相同；summary、validator、runner、布局事实及工具源清单直接保存为 JSON。

旧 v4/v6 离线失败、v5/v7/v9 实库失败和只读 CAST 诊断保留在各自原目录，相关 triage 与审阅报告保留；本接受不追溯改写它们。

下一步只激活 TP-03：Sol High 将已接受整数核心接入公开 helper、严格 writer 与同一候选内的最小 typed reader 路由；Astra 审阅后由 Low 独立验证。TP-04/05、同包 ADO、EF、PR 和 merge 仍未完成。
