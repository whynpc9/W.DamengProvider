# TP-01 v4 Astra 定点复审

日期：2026-10-08。结论：`static_accepted_for_offline_validation / runtime_pending / database_not_authorized`。C01、C02均已在源码层关闭；六项差异与root交接SHA一致，其余七项输入与v3只读快照逐字节SHA一致。可以交Sol Low完成全部离线阶段；DB仍须root在离线结果通过后另行批准。

本次仅核对 [v3报告](TP01-astra-rereview-v3.md) 的C01/C02及影响域，保留v3已闭合项，不重开未变化范围。未运行unittest、contracts CLI、dotnet或数据库，没有产品/旧报告修改。本报告完成后停止写入。

## C01：baseline观察与candidate七位拒绝分离

`eng/temporal-precision.validate.py:287–301`先核closed advisory的零持久化与advisory_no_dml，再按candidate分支要求NotSupported；baseline只接受真实written/driver_write_rejected分类，拒绝类仍经过failure检查排除infra。baseline SELECT返回不要求持久化1行，也不赋予任何写能力接受。

新增 `test_C01_TLS_baseline_overflow_is_observation_candidate_requires_NS`覆盖baseline -840/-780 offset的Overflow观察、candidate同型拒绝、baseline SELECT返回、candidate闭门成功拒绝以及timeout/infra拒绝。独立expected及请求检查仍在分支之前，不能靠更改请求值绕过。

因此旧W真实拒绝顺序可被表征，candidate显式Scale7（包括整微秒值）仍必须NotSupported。shared7及六位整微秒主门没有被此差异修改。

## C02：fixed metadata使用明确物理列目标

`Program.FixedTargetColumn:470–475`是live与offline共用的明确选择函数：shared→D7/Z7，TLS→D6/Z6。ParameterCase、恢复后的fixed phase和early-rejection phase均记录实际target_column；advisory记录null。原“TLS缺D7就closed跳过fixed轴”的分支已删除。dual-scale仍明确指向D6。

validator `validate_binding:434–455`从profile对应的冻结工具目标核对顶层及每phase目标名，再核实际ResultColumn的CType族/scale；不再由seven_write反推任意物理列。结果列flag0与参数fixed1/advisory2区分保持；没有要求产品伪造metadata。

新增合同覆盖：TLS D6/Z6正例、错误D7目标/错误scale/缺目标/closed跳过拒绝、shared错误D6拒绝、advisory伪称列目标拒绝及baseline TLS目标一致性。OfflineContracts新增四个profile/type组合，实际调用同一FixedTargetColumn并经实际C# serializer输出；Python shape消费者独立检查四项完整、目标准确及tz真bool，不以手工shape代替实际C#运行。

## 未变化的关键边界

- candidate closed7严格NotSupported、shared七位Min/Max及六位整微秒成功要求保持；cap缩域不能使主门拒绝变通过。
- 原v3活Reader取得statement/metadata/EncodedScale、失败后fresh连接恢复、immutable raw/effective区分保持。
- C# offline在Configure/Open/DAMENG读取前分派；live/fake共用创建/marker/cleanup判定；19个owner分支和其余自测不被替换为Python模拟。
- runner、contracts与原source/package身份链七项输入未变；strict JSON、真实payload长度、TLTZ值门/metadata分离及8/9精确文本规则继续沿用v3静态结论。
- 未发现由这六项差异新增的明显C#编译/API或producer/validator字段不一致。实际构建和合同执行结果仍待Low。

## Low离线开窗建议

允许root冻结v4后按顺序执行：portable Python全部合同→显式local-required→真实source/package CLI→runner `--offline-only`的restore/build及真实C# selftest→Python消费该次实际C# shape。使用仓库10.0.203与规定CLI_HOME/禁首次体验/禁遥测/单节点等设置。首失败保留自然退出码、输入和日志并释放窗口；不修改旧失败或本次冻结输入。

待执行命令形式沿用v3报告，关键最后两步是：

```sh
python3 eng/temporal-precision.py --feed '<verified-feed>' \
  --identities tools/TemporalBoundaryProbe/identities.json --source-manifest '<241e15-manifest>' \
  --repo /Users/wanghongyi/Projects/W.DmProvider --profile shared --output '<new-v4-offline-dir>' --offline-only
PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalBoundaryProbe/test_producer_shape_contracts.py \
  --shape '<new-v4-offline-dir>/offline-producer-shape.json' --output '<new-v4-shape-validation.json>'
```

路径由Low替换为已冻结真实输入与全新输出；这不是执行证据。只读归档链、C#假DB分支、实际serializer shape各自分开报告，不把离线成功称为实际DB、TLS、wire或candidate profile接受。全部离线通过也不自动授权进入DB。

## 六项复审差异SHA256

| 文件 | SHA256 |
| --- | --- |
| Program.cs | `6f5c117c957e15449866c9cc1419e73a13de6321a6f96010e08da154df901839` |
| OfflineContracts.cs | `4a5ffde6003b26054faa993292ffb7308de909863278f86ca2013657eb9704a5` |
| README.md | `5fc5dabb01cb4667f4c0f4f94df3701d2e20ff9d9b9149fd79921c95ab919e81` |
| validator | `06e8de2d403a9e06478c2a5a67a935a12f4ddd7080dab419b0cdf0d67f9c33ef` |
| test_validator | `e3b844a5a35fcf886dfb08727da554dca27f6da4f2d2845c56be530cc1ad576a` |
| test_producer_shape_contracts | `0298b0a04896a1cc7ea431218f73cff35121d435ba8488a267eabc7285918962` |

其余七项为runner、test_runner、csproj、global、identities、contracts、handwritten fixture，已与v3快照独立比对一致。原v1–v3审阅、输入与失败历史保持只读。
