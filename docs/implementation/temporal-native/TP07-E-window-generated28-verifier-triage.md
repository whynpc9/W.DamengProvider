# TP07 E窗口版GeneratedCreate28：独立读取器TypeError分诊

2026-10-10。结论：**stored_new_host_output_schema_unchanged / independent_N_reader_shape_correction_ready / deep_gate_pending**。观察到的新报告结构与已接受v4/v5相同；不需要修改Program、ef-validate、policy/toolpack或重新运行host。root可授权Low在新证据revision中修正其附加N facet读取逻辑并重验已保存的新host输出。原独立验证失败必须保留，不能把28个host成功直接改为deep gate通过。

本轮仅读取源码与JSON并比较保存文档字段；未执行SDK、自测、validator、DB或host，未编辑源码/原证据，仅新增本文。以下字段比较不等于执行完整独立验证器或生成批准gate。

## 实际文件与失败边界

新输出位于`.local/temporal-native/verification/TP07-E-window-v1-generated-create/`。shared/TLS各14项status=completed，host自然exit0/host0、无timeout/termination，耗时分别0.43422645796090364和0.3901207922026515秒。报告database_accessed=false、owned_objects_constructed=false、runtime_accepted=false；offline_ddl_accepted=true属于host结果。

| 保存artifact | SHA256 |
| --- | --- |
| shared/offline-generated-create.json | `a5a7d65ccf3dd1e8bbba9cefe8aaf2b850cce2afcf6335eb97ff8a1367882660` |
| tls/offline-generated-create.json | `81acbc1621e8bc7271dca3714402937139410029da0cc4a993070fd2c4e8f5bc` |
| commands.json | `d9bfd2318805c5842a6d279c3856245e11f0b4827749e17b4782b0982b8da1ed` |
| profiles.json | `63baa2b3ccc7157b3899ce6a2ef97078401e88a7f7f5439643e220fb7b141fe0` |
| failure-readback.json | `a217c72d5e81578f12c8b8f1b6f6009a16a1b74cf387156c79ec1389cd4d2540` |

failure-readback记录TypeError、deep_validation_completed=false、generated_gate_created=false、2778项prior inputs unchanged及window_released=true。它没有保存完整inline检查器源码/traceback；协调者提供的`string indices must be integers`及附加N处理位置与下述误读一致，但本报告不杜撰确切失败代码行。

## 真实结构：集合是list，选出的N是dict

新shared/TLS全部28项，与v4/v5对应case均具有同样结构：

| 字段 | 实际容器/元素 | N facet字段 |
| --- | --- | --- |
| runtime_model | list of dict | `name=N, column_type=VARCHAR(32), max_length=32, unicode=false` |
| database_model | list of dict | 同上，使用`unicode` |
| columns | list of dict | `name=N, column_type=VARCHAR(32), max_length=32, is_unicode=false` |

`native`及`DATE_nonmidnight`这两个shape的三个集合均无N；其余12个shape每个集合恰一个N。筛选出的**单个N元素是dict**。如果检查器已经通过next/索引选出该dict，却再把它作为list迭代，得到的是`name/column_type/...`字符串键；再用`row['name']`访问该字符串即可产生该TypeError。这是与事实一致的读取器错误归类，不能把整个runtime_model误称为dict，也不能要求host把单N字典改成列表。

源码`OfflineModel`本来就返回属性对象数组；migration `columns`也返回列对象数组。GeneratedCreate代码未改变该形状。冻结`ef-validate.py`仍为SHA`7f1a90051296c2c4d169cb53feac279a23b6ac7bc089707a12d01d650eea20c4`；其`generated28_semantics`直接比较完整projection，并未包含这种“重复迭代已选N”的代码。

本轮对新28项分别与v4/v5保存case做结构化文档比较：`case_id/shape/runtime_spec/database_spec/runtime_model/database_model/migration_operations/seed_operations/columns/generated_SQL`十个字段无差异。此比较确认未观察到相关schema或语义描述变化；不替代实际SQL文件字节、IO、compiled identity、进程与所有pin的完整独立门。

## 安全窄修与保存输出重验

附加N检查应先要求字段为list且元素为dict，再按`name == 'N'`得到matches；native/DATE_nonmidnight要求len0，其他shape要求len1。随后直接读取`n = matches[0]`的字段：`n['column_type'] == 'VARCHAR(32)'`、`type(n['max_length']) is int`且值32；模型对象`n['unicode'] is False`，migration列对象`n['is_unicode'] is False`。不能对n再次当list迭代；不能用`dict.values()`掩盖结构错误，不能同时支持任意list/dict形状的宽松fallback。缺字段、重复N、未知容器/元素、bool冒充整数、unicode true及VARCHAR2/128语义都继续拒绝。

修正仅位于Low本次独立reader的附加facet逻辑；冻结generated28_semantics及模型、默认值、seed、操作、SQL、IO/closed factory、actual新Program/provider身份门全部保留。不是skip失败检查、捕获TypeError后继续或只复制成功旗。

重验应在**新的证据revision目录**保存新的独立readback与gate，使用上述原report、commands、profiles及全部SQL的原绝对路径/真实hash，并再次核当前source/build/startup/toolpack和before/after保全。不要把host报告复制到新输出目录伪装新执行：gate会把report父目录与原执行`--output`联结，必须保持原命令事实；新gate可引用这些已保存、属于新编译cohort的原host路径。

完整重验须逐项完成28模型及v4深验/v5cohort provenance、模型/operation/default/seed全projection、真实SQL字节与SHA、zeroIO、自然进程退出、actual新host/provider/package/源码身份和新有限gate schema。通过后才创建新的generated_create_gate；保留failure-readback原SHA和失败状态，不回写旧readback、不重建或重跑host、不访问TEST/DB。若保存SQL或pin漂移或出现真正语义差异，停止回root，不能以本次“reader shape修正”预批准忽略它。

本轮没有生成successful readback或gate。当前仍为Generated28独立门pending，后续runtime/完整C及其他依赖不因此提前接受。
