# TP07 E tools v2：两项接口修复复核

2026-10-09。结论：**ETOOLS-B01_closed / ETOOLS-B02_closed / scoped_static_accepted_for_Low_offline / E_runtime_pending**。两项v1阻塞已在源码层面关闭，未发现本次窄修新增的确定接口或逻辑阻塞。可由Low按root排定窗口执行离线合同与后续外部准备；本轮未运行SDK、测试、validator、runner或DB，未修改实现。

## 冻结与差异

Low `.local/temporal-native/verification/TP07-E-tools-v2/` 的 toolpack manifest SHA256 为 `ff858c16fabef52ac56f9053f1a15eacf62c94861272fcffc7921d6cdadcb958`，24项 source manifest 为 `0ae1d06c20834eeb9997235c54eb547537de00a1c65f5b2a0ae389b218ebf272`。24个live与snapshot文件均实算匹配。

相对 [v1审阅](TP07-E-tools-astra-review-v1.md)，仅以下两项改变，其余22项字节保持；8工具/24source集合、E九源、L0七模板、338 case、runner及外部approval schema均未改变：

| 文件 | v2 SHA256 |
| --- | --- |
| `ef-validate.py` | `c842fa9a080567bed10112101d4d4d494f6f84a3d11e8f58dd68520efe00d21d` |
| `test_ef_contracts.py` | `39efb9deafd123e7a9570c6cf3bd51cd23cd6894181bea60d393b1c1ab7d08ca` |

旧v1 manifest仍为 `710d8906f47070c5adc7315333c5c9d4c4b0ba70d0c39766e109a4c648f1d114`，旧24项snapshot均匹配，历史问题与源未回写。15个计划合同方法是静态计数，不是运行通过数。

## ETOOLS-B01 已关闭

`ef-validate.py:127–166` 提取 `project_dependencies`，由原 `inputs` 三assets循环直接调用。provider分支精确接受冻结的无内联版本PackageReference，核项目与根 `Directory.Packages.props` 的source hash、中央管理启用、无更近props、单一无条件精确W `[version]`；E/L0两项目继续限定原内联精确版本。实际assets中的W包、EF10.0.12、无官方驱动与无W ProjectReference检查保留，没有用宽范围版本或另一份provider源码规避原问题。

新增合同的 `PROVIDER_XML`、`CENTRAL_XML` 通过AST只读取常量后与真实mapping-v5源逐字节比较，均一致，SHA分别为 `163c17cc3b8c6df3ae9b6914d079678676d9676075e910465e0810742aa7b745` 和 `b7b340c8c8b90816f5cdb612acb0a26ea00856b344b081764a51b2e290d25846`。两个模板正例直接读取冻结模板，三者都调用生产 `inputs` 使用的同一依赖函数。

负例覆盖中央版本缺失/错误/宽范围/重复、管理关闭、条件声明、props丢失或hash漂移、VersionOverride、W ProjectReference、官方包/错误候选，以及两模板错误内联版本。这里没有执行这些断言；它们已经补齐v1只检查假设形状的缺口，待Low实际运行。

## ETOOLS-B02 已关闭

`steps:290–316` 只对经过主case校验的 roundtrip/reuse DT/DTO，允许该列恰好三次 `fresh_independent_D/Z`；普通业务步骤仍唯一，不接受另一列或任意重复。位置约束精确为初始fresh后/改值前、changed query后/改NULL前、NULL fresh后且为最后一步。原required顺序、completed/failed、call-returned规则继续生效。

`independent:557–575` 进一步核递增observation ordinal、读取step数、attempt数、同列、completed、step.result与observation逐项相等，再按独立期望逐次核原值→changed→NULL的raw/FF9/typed以及本次owned table/row/column和metadata/identity。没有把重复步骤折叠为最后一次结果，也没有跳过fresh读取；返回业务步骤映射时只移除已单独严格验证的重复读取项。

与冻结C#接口吻合：`RuntimeHarness.Independent` 的step名可重复，observation键递增，`CompleteStep`携带相同结果；roundtrip/reuse恰好三次，普通DT/DTO一次、generated pair按D再Z两次，后两类也满足新绑定规则。

新增合同枚举全部8个reuse case（shared/TLS × DT/DTO × sync/async），同时调用真实steps与independent函数。负例包括少/多读取、changed/NULL交换、复用旧oracle、错step/observation列、跨case表、未完成step/attempt、读写时序颠倒及业务重复。原两步骤普通重复负例和其余13个合同方法保留，未放宽旧负例。

## 接受边界

本次接受限定为两项修复及原v1审查范围的源码闭环。完整外部编译闭包仍为175 EF源 + L0七模板 + E九模板=191，再加入两新项目锁成为193；当前24项工具snapshot不等于该闭包已经准备或构建。Low须先执行15项离线合同，再按root窗口进行独立外部准备/实际构建及运行。

[新L0-v3接受](TP07-L0-v3-acceptance.md) 实读SHA256 `3d4ca42e7f5a28b7a50c233667114d6487d386c729a4ec0d1ef0de0942938533`，其shared12/TLS2前置门已由另一轮独立接受；本次不重跑或扩展该结论，原L0失败保留。E runner仍须消费该已批准新mapping/L0 lineage，自己的参数来源、全部338 case、真实EF链和独立cleanup尚待运行。

TLS两项受控参数case的未知advisory或缺ParameterDescribe仍pending/failure；本修复未新增可接受tuple、删case或将controlled ADO改称SaveChanges。E实际运行、D和完整EF-C仍未接受。
