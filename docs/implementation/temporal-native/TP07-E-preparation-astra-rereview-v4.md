# TP07 E preparation-v4：E-tools-v10引用窄修验收

2026-10-10。结论：**reference_only_scoped_static_accepted / actual_new_prepare_build_offline_gates_pending**。未发现阻塞项。仅只读三源及实际E9 manifest/snapshot，未执行准备脚本、freeze/source副本、复制、SDK、测试或DB；仅新增本报告。

| preparation-v4文件 | SHA256 |
| --- | --- |
| prepare-and-build.py | `2c3cc3e379338bbdca9f6f1e07d4ff722e421f34ec687237494507c8d52cc07a` |
| README.md | `781647a40afbac4b68d3d49b98b7bbb159a15c45fb3c19a777bc0e7dbea474ff` |
| schema-draft.json | `f64d40380980d05895ad18c9ace65fc7a86ddcb3f069dc5c26c8ceb8b6111d75` |

原v3脚本实读SHA仍为已接受的`616494d94c1ca0b58cb9e87a10e0eb9adbc6da1cd3dd05ad657c421a99cdf0ee`。v4逐行仅三处改变：首行doc标签、PIN.e_source切到`TP07-E-tools-v10/E-source-manifest.json`/SHA`5621d0d8ee8477023197f37eae5ca4c48376d9d56cfdcf6390717e3b09d76b8f`、FOLDERS.e_templates_root切到v10 snapshot的runtime-templates目录；其余字节完全相同。

因此原182+9=191准备源、193锁后源、native13 graph证明、三个active locks/assets、锁阶段三条与build阶段五条命令，以及L0/mapping/W/cache/feed/SDK/helper所有pin、环境、身份、输出隔离、失败和外部批准守卫均未改。README/schema只更新来源、标签、新鲜combined-v4示例及26合同/实际partition/28模型后续门说明，不自动执行这些门，不产生runtime批准。schema-draft仍不是批准文件。

实际新E9 manifest具有脚本所读count/files形状，count9、九个唯一`TP07TemporalRuntime/`相对路径；map与已接受window policy的E_template_hashes精确相同。九个snapshot文件逐项实读hash匹配；按manifest声明的排序`sha256 + 两空格 + path + 换行`格式重算aggregate为`3e54a9a2b3fca3208af37d00824c750051db4379af6df8e8424f99564108b494`，与声明及policy一致。Program是已接受新分窗源码，其余八项保持原pin；没有拿当前工作模板或不同目录补缺。

按**原目录**`.local/temporal-native/verification/TP07-E-preparation-v4/prepare-and-build.py`的绝对路径计算，`parents[4]`实际等于当前workspace ROOT。freeze/source副本多一层，不能作为执行入口；Low应在原v4路径调用，使用已批准输入和全新、互不重叠的阶段输出，不能执行副本或放宽ROOT守卫。

本次接受来源引用调整，不把已报告的26离线合同通过扩展为新build成功。后续由Low实际prepare/lock/build，独立核新source/lock/startup/DLL/MVID/deps，再运行真实compiled partition及完整shared/TLS28-model GeneratedCreate；root最后绑定两新gate及原L0 accepted proof创建新runtime批准。原v3、历史cohort失败及owned关闭证据保持不变。报告完成后停写。
