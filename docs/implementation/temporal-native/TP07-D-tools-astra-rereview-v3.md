# TP07 D tools v3：cleanup负例fixture定点复核

2026-10-09。结论：**v2_fixture_missing_tool_root_closed / scoped_static_accepted_for_Low_offline / D_runtime_not_authorized**。仅复核本次fixture差异与冻结身份，未运行SDK、测试、runner、validator、cleanup或DB，未修改实现。

相对Low冻结v2，25项中仅 `test_design_contracts.py:223–237` 的 `test_cleanup_host_independent_of_broken_workflow_files` 改变：在规范化task root下创建空 `tool-root` 并写入manifest；其余模拟workflow包/配置继续缺失。这样 `cleanup_host` 仍只需要完好的批准host/home，主 `tool_bundle` 能通过tool_root目录检查，再到达预期的缺失 `local_tool_manifest` 文件。原异常范围未扩大；新增断言进一步要求实际异常为FileNotFoundError，且filename正是该缺失配置路径，不能用KeyError或别的提前失败冒充目标负例。

测试文件实算SHA256 `fccf94db7cf5be1722991ba6fb26557dabf3aa996545899c72da71ff3da9ff59`。其余24项逐项不变，包含runner、validator、policy、全部D/common/L0源与helper；44个计划合同方法没有新增/删除。未发现此次test-only差异新增阻塞。

Low `.local/temporal-native/verification/TP07-D-tools-v3/` 的source-pack manifest实算SHA256 `9c8d6022dea82d7f67e38fa2fb86afb61be77ef58773df4509f03b93dab82e8c`，25-source manifest为 `4b4599ad5194eab45443e604152f00b701f132ca56d8ed2f5ef3bba5dd171d28`；25项live/snapshot全部匹配。

v2实际43pass/1error及summary `00101d17a1bcf163cf269bade170f009d8c83d0cba4e86d72f47a0e0591ba460`保持历史失败；[v2静态报告](TP07-D-tools-astra-rereview-v2.md)仍为 `76f52e390631e30465a9be2305c9bc5341b0234b71e39672c16194f8e9a0f8f7`，没有回写其结果。当前接受只允许按root窗口交Low重跑全部44项离线合同，不是测试已通过。外部D构建、E前置门、D实库和完整EF-C仍未由本报告接受。
