# TP07 constructor FF7 EXPANSION53 静态审阅 v2

2026-10-09。结论：**changes_requested / one_scoped_acceptance_blocker / no_runtime_acceptance**。只读审阅冻结六文件及八个固定helper/必要接口，未运行SDK、构建、合同测试、producer、validator或DB，未改实现与旧证据。设计依据 `TP07-constructor-FF7-expansion-design.md` SHA256 `ae8d14bd4daceb77692ad5c1225e84cbd676079fa1659cbc76ef66cc51ce9748`。

## F7-V2-B01：DATEADD限定52项分支没有核真实异常

`validate.py:286–288`仅要求最后operation的outcome=loss、status=failed、error_stage=value_mismatch；`:408–422`只核case failure_stage、主status/exit/expansion_completed。**没有核对实际operation.error、case.error、main.failure，也没有核定main.failed_case_id/failed_operation_id**。

这不是只缺一个冗余字段。producer `Program.cs:251–258`先把完整观察到的不一致写成loss/value_mismatch，再抛OracleMismatch；这个try块内部的command/context使用using/await using，异常展开期间若Dispose抛出transport/cancel/其它异常，catch仍可能保留loss/value_mismatch标签，却在error写入真正的基础设施异常。保存报告或case收尾也可能留下与标签不同的实际失败。当前validator不读取这些真实error，可对这种结果返回 `literal_gates_complete=true`，违背root“任何infra不符合限定分支”的边界。

静态合同fixture也揭示该缺口：`test_contracts.py:make_last_loss`没有构造任何真实safe error收据，就被正例期待接受；其中infra负例只把outcome改为unobservable，没有测试**保留完整loss观察但替换实际exception**。本报告没有执行该测试，以上来自直接读代码路径。

### 必需窄修

只需validator及其合同fixture/负例的窄修；producer已保存必要真实error，不要求修改产品、common、SQL、case集合或正常执行路径。

1. 限定loss必须在operation.error、case.error、main.failure三个层次都是精确安全收据：`type=TP07.Runtime.EvidenceException`、`code=OracleMismatch`、`number=null`，不接纳任意同名标签或其它wrapper/error。它是 `SafeEvidence.Require(exact, EvidenceCode.OracleMismatch)`产生的工具值比较失败，不是服务器error。
2. 精确绑定 `failed_case_id=F7-A-DT-second1`、`failed_operation_id=F7-A-DT-second1:dateadd_update`，并保持该case/operation为最后项、完整raw/FF9/typed自洽loss、前52全部exact、两个本次typed输入/期望control、总操作数/目录/行数和独立cleanup门全部成立。
3. 拒绝main声称characterization_complete=true或completed_case_count不是52的窄失败结果；正常全通过分支相应核characterization_complete=true、completed_case_count=53且没有失败ID/异常字段。不能只在独立输出改旗而容忍主报告相互矛盾。
4. fixture补真正的OracleMismatch三层收据及精确last IDs/完成计数。负例在保留完整loss bytes/FF9/typed与正确阶段的前提下，分别删除/替换三个error、改code/number、放入transport/timeout/cancellation/Dispose类型、错失败ID、错主完成旗/计数；全部必须失败。原合法“52完整+最后DATEADD精度loss+cleanup完整”的限定结果应继续保留main exit1/status execution_failed，validator总体失败/exit1，不能升级full53或产品接受。

修复后新freeze/静态差异复核，再交Low。当前冻结v2不能直接进入认可该限定语义的离线/DB接受门。

## 其它限定逻辑复核结果

未发现第二项确定阻塞。以下只说明源码满足本轮设计，未声称运行通过：

- C#与独立Python registry均为DT18/DTO22个domain pair。tail0…9从`.1234560`逐tick递增；min/min+tick、max/max−tick/max−9ticks、最后fraction/下一整秒、NULL及DTO额外0/−330/−779/+840offset与设计一致。DTO极值offset0，普通tail/carry +330，不构造非法UTC极值；Python用整数日历重算，不依赖driver。
- 同kind40pair各先typed同值INSERT/fresh exact，再FF7/NULL INSERT/fresh exact。NULL独立核DBNull、rows1、NULL FF9/原始cell及真实ResultColumn type26/27、width9/11、scale7；不会调用只支持DateTime的Exact处理DBNull，也不把NULL变成min ticks。
- 四DEFAULT表使用既有RegisterLiteral→BeginExternalCreate→真实CREATE返回/ACK→ConfirmExternalCreate；V DEFAULT表达式之外的owner marker默认与PK(ID)未改。每表先显式V的typed同值row1，再真正省略V的INSERT(ID) row2。独立validator分别核DDL/hash、typed prior control、省略V SQL和fresh保存值。DATA_DEFAULT只保存有限hash并诚实标 `pending_hash_only_not_semantic_acceptance`，实际默认行为由省略V写入证明，没有为文本规范化改common。
- 六个predicate case在更新之前，各执行同值count1和相邻tick异值count0，SQL参数数0且SQL/hash精确。两assignment先typed seed `.1234560`，fresh before oracle后用FF7更新到`.1234567`，确实改值；source/target typed对照都关联同表已完成domain row。
- 最后DATEADD只在DT普通日`.1234567`上加1second，有独立typed source row38与typed expected row39，更新前fresh oracle检查原值。没有Max加法、DTO DATEADD或扩大translator。direct SQL与真实EF HasData/SaveChanges/ExecuteUpdate明确分开。
- 操作层面固定92 INSERT、3 UPDATE、12 predicate SELECT；每次先fresh absence或更新前fresh原值，再核实际API、command/parameter route、affected/scalar、raw/FF9/typed、目录及总行数增量。完整值集后两主表39/45行、四default表各2行，最终六表行数只是额外交叉检查。
- 新gate所有literal门都要求exact，任何早期loss/error/unobservable停止，未执行项保持not_run。仅最后DATEADD自洽loss有设计上的52子范围可能性；B01修复前其异常边界不足，不能启用接受。

## 冻结来源、历史与执行边界

schema/producer_version=2、task=`TP07-constructor-ff7-expansion`、scope=`FF7_EXPANSION53_only`。沿同W候选/EF mapping-v4，14文件source pack仍为6新+8immutable helper，compile additions恰7项、原EF175不变，prepare182/新增单lock后183。新startup仍在constructor项目独立路径编译，兼容AssemblyName不授予旧SCREEN25或L0 DLL复用。

新validator要求五项SCREEN25历史artifact固定hash，并读取旧source_pack旁的完整14文件快照逐字节核验，把这些路径纳入运行input before/after/current哈希；原L0 `ed5310c8…`也仍必需。继承settings为真实-5504 unknown，不重查、不fallback、不推断mode值。旧19exact/6loss、两表清理和旧接受旗不回写。

外部run收据只允许full expansion完成，或明确标记最后R04值loss的受限失败；前者主exit0，后者主exit1，独立cleanup均必须自然0，timeout/terminated不通过。validator对窄失败本身仍exit1、characterization_complete=false、r04_gate_complete=false，产品/full C/support旗均false。**B01必须补齐，才能确信该受限入口不接纳infra。**

六文件实读hash与root冻结一致：

| constructor-probe文件 | SHA256 |
| --- | --- |
| Cases.cs | `ccbd295156e715cfeb53062d5c3ed821324735400222d45e8055c46779f8e324` |
| Program.cs | `13882930304e5a3607bba100cdff53248ddc5a64b778267a0de3dc2c8fa97b26` |
| TP07ConstructorProbe.csproj | `f97350a514c3bd5654205336c04c19f8904e184011f63bdb46ac0c8f5fe6c7b8` |
| validate.py | `1f31c19e03568a9b9a7c1f61ead585b11131a19438f21e80d10342624af94823` |
| test_contracts.py | `ed97d5b8dcf6c222c8ab2a8ad80aad0d97c3409379aca3359f2d632bcab965bd` |
| cases.json | `410b81b85ae2d7c6c4fb9556503879d9732827da803ceaabac29edd1f3e165fd` |

12个合同方法已静态阅读，未执行、未计为12/12。修复仅解冻被指定的validator/test文件，新包快照及外部收据另起；其它输入继续冻结。Low后续先离线合同/精确源锁构建与身份，再由root单独开DB。52项literal专属门实际独立接受前不改产品；DATEADD独立R04结果不能误归constructor，也不能被修literal的后续工作冲销。
