# TP07 D 外部准备 v2：Hosting身份接口窄修复核

2026-10-09。结论：**DPREP-B01_closed / scoped_static_accepted_for_Low_external_preparation / D_runtime_not_authorized**。本轮仅静态读源、AST、JSON及已安装文件字节；未导入/执行脚本、SDK、CLI、tests、DB、cleanup或网络操作，未修改实现或E对象状态。可由root按独占窗口安排Low的有限外部准备，不等于准备流程已执行通过。

## 差异范围与实际文件来源

相对v1，只增加workflow预期Hosting身份、before/after文件pin、外部build/CLI证明及schema/README说明。其余P0/N0/B0/BC/G6/G12阶段逻辑、包图、SDK命令、路径/锁/源集合及运行权限边界不变。

`prepare-d.py.shared_framework`从批准dotnet host规范化安装根的固定 `shared/Microsoft.AspNetCore.App/10.0.12/` 读取文件，不扫描候选版本或fallback。只读实文件核对结果：

| 文件 | 实算SHA256 |
| --- | --- |
| `/Users/wanghongyi/.dotnet/shared/Microsoft.AspNetCore.App/10.0.12/Microsoft.Extensions.Hosting.dll` | `f266f73b2c15ab8a94d8e995c760b4f995ae2bbde5913cbce0de11501c6d807c` |
| 同目录 `Microsoft.AspNetCore.App.deps.json` | `016b52cb9f578fb396a4b495d7c7d2f98389825bb1e2c2df78a614c476e1a92f` |

deps实读runtimeTarget为 `.NETCoreApp,Version=v10.0/osx-arm64`，唯一runtime package为 `Microsoft.AspNetCore.App.Runtime.osx-arm64/10.0.12`，Hosting assemblyVersion/fileVersion分别为 `10.0.0.0` / `10.0.1226.42308`，均与代码/schema一致。这是已安装文件的外部expected身份，尚非D或CLI加载证据。

## DPREP-B01闭环判定

- D0 workflow startup现在写入稳定的 `shared_framework.hosting_dll_sha256`，以及path、版本来源ref和声明版本；与冻结consumer `process_identity`读取字段一致。原consumer对C#实际loaded Hosting hash的比较没有修改或绕过。
- workflow source/lock/build阶段将DLL和deps纳入protected before/after；外部build receipt、CLI proof、preparation receipt记录同一身份及两个文件的前后hash。CLI只读版本事件后再次检查身份一致，没有把安装证明称作加载成功。
- G6/G12先核实际文件与parent startup、parent build receipt的整项identity相同，再把两个文件加入protected；child通过 `dict(old)`保留原shared_framework，不把它加入mutable集合。构建后仍检查原pin并记录新phase证明。
- BC的 `expected_framework=None`，不调用shared_framework读取；cleanup startup不含该字段，统一receipt仅记录null与空pin集合。此次改动没有给protected cleanup引入Hosting/CLI runtime依赖。

本次相关差异未发现新的确定接口或执行阻塞。文件来源与hash校验失败仍停止，不改consumer、不换框架版本、不改runtimeconfig凑通过。

## 停写源码身份

| v2文件 | 实算SHA256 |
| --- | --- |
| `prepare-d.py` | `d25f1e3173ce734a0bcbcd2c51ffbf1d86487f6f82bbcf0382fd75ac46d4638d` |
| `build-generated.py` | `fbd86dac081c6c12c799514c2c0a4c3aa6a521573c3e75dae11a52536fc35fef` |
| `README.md` | `8c640b05db1e40acfb5a33f2c730a1e87eca2ef82ec86ea6fc9c4ff0b2d6be06` |
| `schema-draft.json` | `c2fba9f03d465e86015de28b677c681ccfc762ba3c4957049b96d83d4edd2d0b` |

旧v1四个原文件及snapshot均保持，旧freeze manifest仍为 `ba8330b00e1233c76a3cb8e7fa8b63d736c58864527c2853af4ebe87885649f3`。[v1报告](TP07-D-preparation-astra-review-v1.md)保留历史B01结论，不回写为通过。

Low `.local/temporal-native/verification/TP07-D-preparation-v2-freeze/source-manifest.json` 实算SHA256 `cfe34c102b48ac1a9138800a8464e2b2edf249bdcff50e00a876b5f71ecbc078`；上述四项live与snapshot全部逐项匹配。Low亦只读复核安装文件来源，没有把它记成loaded观察。

真实SDK阶段、33包native证明、D/cleanup实际构建、CLI身份与G6/G12仍各待原门；即使外部准备通过，E模型前置门和D DB窗口仍由root另行判断，完整EF-C继续pending。
