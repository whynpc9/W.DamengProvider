# TP07 E-v7完整60对象独立关闭

2026-10-10。结论：**independent_fresh_full60_tables_and_PKs_absent / v7_owned_state_closed / bounded_window_implementation_gate_open**。两次独立进程均对原完整60项ledger取得自然完成的只读absence证据；present=0、unresolved=0。本次没有恢复DDL，也不推断先前main finally或独立cleanup究竟在哪一阶段删除了哪些表。

本reviewer只读两次收据、执行/control及源/包/binary引用并计算SHA；未运行SDK、测试、validator、DB或探测，未改代码、ledger或旧失败，仅新增本文。

## 两次实际只读观察

第一次host自然exit0/host0、无timeout，在180秒界限内耗时2.206297082826495秒；第二次使用同一未改字节inspector，在新的`TP07-E-v7-owned-closure-v1-runtime`目录另起进程，自然exit0/host0、无timeout，耗时2.0546761250589043秒。两次各7个phase全部completed，实际TEST user/current schema、shared8.1.5.60/message21/utf-8/encoding-override-empty及typed PlaintextAllowed均核实。

两次`presence_rows`和`exact_PK_rows`均为空；全部60个object分别为table_presence0、primary_key_presence0、table_and_PK_absent_observed。两份object的case/table/PK序列已逐项与原ledger `4ffde52b2f02a9607ffc338b1451250b42830bf730a966b150d77ca680e8debe`核对同序相等，覆盖完整60项，没有使用subset或synthetic ledger。查询只读、recovery_performed=false，没有进入present对象目录检查或任何写动作。

第二次Open0.5424687秒、身份0.2461367秒、两batch0.3273222/0.3278977秒，属于当次观察，不据此宣称环境时延永远稳定或E-v7超时根因已知。inspector自身`inspection_complete_not_independent_closure`原标签不改；本报告综合两个独立执行及最终control完成独立关闭。

## 来源、执行与保全

两次各11项input_artifacts的当前文件SHA均实读匹配；原main/group/run/validation/control路径及哈希仍指向失败E-v7，不引用E-v6。两次执行收据指向同一个已接受inspector DLL、各自不同的新输出目录，报告身份与实际文件字节对应。

实际DLL `44929c8cb999497804a239952d1f1630a6d8ed6a58a7cd43f7335e12adb9a203`及deps `21bfcf8a54f78015b8b7de0df48b939c0e029f584f5903006b36d846779c8aac`已实读匹配；记录MVID `1071c65d-911b-4eef-a4cf-f99c21f916fd`。两次加载W DLL字节均为`843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980`，包`da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`保持。MVID来自Low及受审host收据，本轮未启动SDK提取。

Program源仍为`20c83662633216557feb68ff28eaeb1df694845ec9d9d0f2365aa21864e1cb0f`。第一control记录1181项输入/source不变，最终control记录1187项输入/source3不变、independent_absence_closure=true、window_released=true；本报告引用Low结果，不冒称重复执行全集验算。

以下路径相对`.local/temporal-native/verification/`：

| 文件 | SHA256 |
| --- | --- |
| TP07-E-v7-owned-inspection-v1-runtime/inspection.json | `03d5193f3d4baa1dcbf3ebbb2b698ce0a2ed558e39057a61fadcebbaaa070f54` |
| TP07-E-v7-owned-inspection-v1-runtime-control/verification-summary.json | `f6a476c542286016bde38f1b7c3e4fd494c0a602bac9545991e471cd57da818a` |
| TP07-E-v7-owned-inspection-v1-runtime-control/execution-receipt.json | `399c1a3c984fd5d974dc1da282146462ad710b52922466fc9780fb9d58360be6` |
| TP07-E-v7-owned-closure-v1-runtime/inspection.json | `1e1f97a7a7ffc49ac9ba5d363df6a0f28b99f9623b3668efd02aae7dda52f848` |
| TP07-E-v7-owned-closure-v1-runtime-control/execution-receipt.json | `c3cb631eabd2135311162cd7d9aaaec053c8e278e0c5760ab0523b98dbdbecd0` |
| TP07-E-v7-owned-closure-v1-runtime-control/verification-summary.json | `9a35900571e2f8d8bdd16ebbe64d9d42a8c02d9f584c357193f15543fdebabd7` |

## 固定分窗实施门

设计`TP07-E-bounded-case-window-design.md`当前SHA仍为`0ed4960f21d459e01f1ef84f273cef320b1f0e3b24f04cf5e161f8adfda4fede`。其两个激活前置条件——原执行窗口释放、原完整60对象独立absence——现在均满足。root可以按已批准范围派发High实施固定15例/25窗、338case完整覆盖的五文件harness调整。

这只打开**实现与后续离线门**，不意味着新分窗源码已验收或DB已运行。High freeze后仍须源码复审，Low以新Program真实构建/cohort/toolpack执行合同、实际无DB partition和28-model GeneratedCreate门，再由root批准新runtime。case语义、同窗async控制、所有窗口cleanup/独立validator及原900/300/180界限继续严格保留；原L0/W/mapping/common不改。

E-v7的main900秒及cleanup300秒超时、缺cleanup report、validator失败永久保持；60个completed业务case与本次absence不能把旧cohort改成通过。没有额外DROP或恢复任务需要执行。D、完整C及TP08之后LocalClaude门继续按原依赖。
