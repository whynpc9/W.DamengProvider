# TP07 E-v6九对象残留独立关闭

2026-10-10。结论：**exact_two_recovered / independent_fresh_nine_tables_and_PKs_absent / E_v6_failure_preserved / unchanged_E_v7_bounded_run_justified**。已独立回读实际恢复及另进程只读closure；接受本次原ledger九对象的残留关闭。E-v6 main/cleanup超时及validator失败不改变标签，本结论不接受E时间行为或说明变慢根因已知。

本轮仅读取收据、源码身份及文件字节，未启动SDK、测试、DB、探测或恢复，未修改源码、ledger或旧证据，仅写本文。

## 实际恢复与独立状态

恢复host自然exit0/host0、无timeout，耗时9.040688958019018秒，31个phase全部completed。操作连接和随后另建的新verification连接均实际核TEST user/current schema及原shared8.1.5.60/message21/utf-8/override-empty，typed PlaintextAllowed。

初始九名称batch只发现两表/两个精确PK。两表的全部四项fresh marker/Basic列/PK/PK-ID门均在第一个DROP之前完成；每个DROP紧前另有该表四项全部成功的复核，源码与实际phase顺序一致。新鲜列宽4/9/11/32、D/Z scale7、nonce marker及唯一ENABLED PK(ID,position1)均符合批准合同；没有rowcount或row0推断。

| 唯一实际DROP对象 | 固定SQL SHA256 | 实际结果 |
| --- | --- | --- |
| `TPL_431D133DF3E4440895268E1B` | `4b8813995229b0c379035200c0078f0bab9bcf02148a83149af8b8ebee73a220` | attempt一次、ACK=true、返回−1，0.3446069秒 |
| `TPL_63FCD302EB57437F9132DD37` | `1c86bb8c3657686d73575b4734f51ec8b97f53dfcdf569f41248313804ad4e01` | attempt一次、ACK=true、返回−1，0.35447秒 |

两项SQL hash已按受审固定字面量独立重算匹配。另七entry都是already-absent，drop_attempted=false/ACK=false、无DROP phase；无重试、CASCADE/PURGE或其他写。返回−1仅为实际返回值，完成证明来自后续absence。恢复host另一个新TEST连接的九表及九PK batch均为空，fresh_absence_observed=true；其independent_absence_closure=false原旗保持。

随后Low另起独立进程，在新`TP07-E-v6-owned-closure-v1-runtime`输出目录运行未改字节的已接受v2 inspector。实际自然exit0/host0、无timeout，耗时1.7420984590426087秒，7个phase全部completed。TEST身份及原物理profile再核成功；精确原九entry同序全部table_presence=0、primary_key_presence=0，两条batch为空，无DDL。该独立观察与恢复host结果共同满足关闭门；不改inspector自身的`inspection_complete_not_independent_closure`标记来伪造自认证。

## 身份与保全

恢复15项原引用与closure11项原引用的当前SHA逐项实读匹配；原九entry身份/名称/PK在恢复和closure之间同序一致，恢复control对象记录与实际recovery.json完全一致。两个执行收据指向各自实际DLL和独立新输出。

恢复host DLL `0c46c367534cd249ad968be1088f269726d741f7b98d55c52e65ef812d187b37`、deps `ad71bbeb41b0e349fb3c7cee79472f999bfe0fdf24d4589f1efccd756f7e320d`与实际文件字节匹配；记录MVID `f152b99e-3d0c-4b68-819c-59919985e558`。closure继续使用inspector DLL `e9c6668c23a5b59b1b284cd34ec8cdfc972b5c2206bc7cff12d569240a07be81`、deps `8756a287c2afb27b9fa647d9f36f8eefa46db9f3327a71214ee8d01097a2559a`，本轮也实读匹配；MVID仍`5b32413e-0374-4754-86af-4f4a604c1416`。W package/DLL/MVID均保持原批准身份。MVID来自Low与受审host收据，本轮未启动SDK提取。

恢复control记录664项preserved及source3未变；最终closure control记录670项历史/恢复/binary/source pins全部未变、window_released=true。这里分别引用Low独立结果，不冒称本reviewer重跑了664/670项全集检查。

以下路径相对 `.local/temporal-native/verification/`：

| 文件 | SHA256 |
| --- | --- |
| TP07-E-v6-owned-recovery-v1-runtime/recovery.json | `8997c4d9c4894c907ecfc5f017a5d4b6151faf5c4388eeac633a59e6205ab9f4` |
| TP07-E-v6-owned-recovery-v1-runtime-control/verification-summary.json | `82dd849b9e7563f10b6f8bef10c9fe707b2b2205c745bd557918fe550cb349ea` |
| TP07-E-v6-owned-closure-v1-runtime/inspection.json | `b701c58822268deec61b94f09c742716b993c0e0ed373cbdfda68e72f41809ca` |
| TP07-E-v6-owned-closure-v1-runtime-control/execution-receipt.json | `2f6f41d8feac43fe8dc368324bbde60db9021f4a24f3ca5decfe177085921425` |
| TP07-E-v6-owned-closure-v1-runtime-control/verification-summary.json | `53bd2c4d7c600d51985c59fe16e6d11dfa657f8f614b87c68f123f8f5b533773` |

原ledger`d37b870c…`、v6 runtime`1a0df6f5…`、run失败`2f185327…`及validator失败`321ddeed…`继续保持原字节；旧inspection七absent两present`d38c55df…`不回写为全absent。

## 当前延迟与下一次有界运行

相同v2 inspector的可比较阶段显著缩短：Open由4.4318649到0.5097405秒，身份由2.5998861到0.2777337秒，九表batch由2.1131359到0.3548224秒，PK batch由2.509644到0.3290657秒。恢复中的身份/目录/DDL也多为约0.24–0.35秒。这是同一固定输入/包/profile下新的实际延迟证据，**不证明先前网络、服务器或driver中哪一项导致等待，也不能保证接下来持续低延迟**。

在九对象独立关闭、工具/编译cohort与既有批准输入保持冻结后，建议root安排**一次新的、未改源码的E-v7完整cohort**：使用既定runner固定group/profile顺序与当前已批准E-tools-v9，输出全新目录，沿原main900秒/独立cleanup300秒/validator180秒界限和fail-stop规则。此建议已有当前具体延迟改善与残留清零证据支持，不是盲目提高deadline或续写旧v6收据；无需为未变C#重建或为本次现象先改产品。

运行前Low保存新before身份清单并独占窗口。若再次出现超时/失败，仍保留原失败、执行既定有界cleanup并按精确新ledger核残留；不自动重跑、不扩大权限、对象或模式。只有新cohort的每组main/cleanup/独立validator实际通过才逐项接受，不能用本次清理关闭补齐E-v6。D/完整C和TP08之后LocalClaude门继续按原依赖。
