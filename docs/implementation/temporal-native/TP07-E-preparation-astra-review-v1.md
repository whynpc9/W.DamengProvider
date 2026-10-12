# TP07 E组合准备脚本静态审阅 v1

2026-10-09。结论：**scoped_static_accepted_for_Low_prepare_lock_build / actual_phase_results_pending / runtime_not_authorized**。未发现与本轮固定输入、当前E validator或phase收据不相容的确定阻塞。仅只读脚本/README/schema、固定artifact/helper及现有consumer；未执行待审脚本、加载其执行模块、运行SDK/合同测试/DB，未改产品、工具、旧收据或接受旗。

## 1. 输入与冻结身份

三个待审文件位于 `.local/temporal-native/verification/TP07-E-preparation-v1/`：

| 文件 | SHA256 |
| --- | --- |
| prepare-and-build.py | `5f03a47f64bb5a6b183dfad7fbc00b8a715c67db45f04ef59d952a1646fc498b` |
| README.md | `b4067158128d0e037f6184ce71d1feb84cb39a1221938fad5cb93e99b2253633` |
| schema-draft.json | `5f7488221a83f17e2b533edcba7992ff0798fec57a7e3e4a67b9a6851fd2614f` |

实读Low `TP07-E-preparation-v1-freeze/source-manifest.json` SHA `5994b5ca57889776458e433bc73a00f24112b450ae861c785367d166387af444`，三个original与snapshot均匹配。root输入 `.local/temporal-native/root-approvals/TP07-E-combined-v1-inputs/inputs.json` SHA `904e443ea0cf4e094966ad9ffde00011446aafed284217e553738525c4fe4b88`，字段形状与schema一致；六个reference的精确路径/完整hash、五个固定目录及实际dotnet host字节hash都已独立回读匹配。

L0 prepared root实际文件集合恰182且逐项等于固定source manifest，其中EF175+L0模板7；E-v3九模板实际字节匹配固定manifest。没有读取当前活动模板或任意EF checkout替换旧接受输入。mapping-v5 manifest `23f47387…`、approved EF source `dc2bfd15…`、L0模板来源 `2a3affe2…`保留，新增独立 `E_template_source`引用 `9eb28e39…`，不覆盖L0来源字段。

## 2. 三阶段与数量边界

| 阶段 | 代码实际行为 | 当前接受界线 |
| --- | --- | --- |
| prepare | 逐hash复制182个固定L0源，再按test路径复制E9，检查191实际文件；只写source/prepare收据 | 无SDK/restore/build/runtime；没有用文件计数替代字节比对 |
| lock | 新replay191；一次真实SDK查询，L0/E各一次force-evaluate/use-lock-file restore，共3个命令事件 | 原provider lock不变，只新增L0/E两个startup lock，锁后193；图不符立即保存失败停止 |
| build | 从外部reviewed lock-stage只复制193源到另一个新replay；SDK查询及两个host顺序各locked restore/build，共5个实际事件 | 不继承旧build成功，不复制bin/obj，源/锁前后必须一致；本轮未实际运行这些命令 |

193的source_hashes包括原EF source集合内已有锁；startup `locks`只列实际本次使用的provider/L0/E三个lock，`assets`恰对应三个project.assets.json。这一区分与现有E validator一致，不应把整个source中所有历史测试lock强塞进active locks。

lock阶段从固定provider lock得到13项non-driver，逐项核原49包native graph中的version、cache metadata contentHash、metadata字节hash和cache/feed nupkg SHA；本轮对实际13份对应archive/feed/metadata作只读核对，未见差异。W nupkg、native cache与实际DLL/MVID独立固定。每个startup实际解析图只允许这13包、精确W和唯一provider Project；依赖改变时保存实际graph并失败，不能强行改图为13。新L0 lock还必须与已接受L0-v3 lock相同，E lock由本次真实restore产生。

三个phase使用不存在且彼此/输入目录不重叠的新输出路径。外部输入引用、原source、template/helper、cache/feed、prepared/lock-stage源均加入前后hash；阶段错误写安全failure并保留新目录。`--source-reviewed`与build前的外部lock SHA/`--locks-reviewed`是显式窗口约束，不生成自动批准。

## 3. helper、环境和同provider构建

实际冻结process helper为L0-v3 `runtime-run.py` SHA `d2c34db7184c8450daae7ce3c81c0b1a3d5e8df642cd42f2989ddb28f5a802ee`；managed identity helper为 `ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac`。脚本从受pin字节加载其现有 `process(command,cwd,log,environment,timeout)`、`public_env(stage)`和 `mvid(bytes)`接口，参数及返回字段与实际源码一致，没有调用helper旧runner接受入口。

public_env只保留公开环境白名单，设置本stage可写DOTNET_CLI_HOME、跳过首启/telemetry、独立NuGet packages/http cache；不会继承 `DAMENG_TEST_CONNECTION_STRING`、TP07运行配置、任意MSBuild覆盖值或 `TP07ProviderProject`。每条restore/build含 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers`，restore另有disable-parallel；版本由绝对、字节固定的host实际查询并要求10.0.401。无secret、TEST wrapper、startup执行或DB入口。

L0 csproj保留原 `TP07ProviderProject`默认声明，E是直接ProjectReference；命令与环境均不传覆盖。两个host在同replay、同provider项目中顺序构建；实际assets的resolved ProjectReference必须精确指向这个provider root。两个输出目录的W hash/MVID必须相同于固定候选，EF provider hash/MVID彼此相同且hash与同根provider输出一致。不假定同源provider必须与旧L0 DLL字节相同。

每个host完整输出目录进入binary_hashes，startup保存两个实际assembly DLL/MVID/deps身份；三assets/三locks和全部源码再次hash。未声明compile文件、official driver fallback、EF版本不同、两个provider输出不同或任一source drift均停止，不能由后一个成功命令掩盖。

## 4. 当前consumer/schema兼容性

只读核对当前 `ef-validate.py` SHA `8ac6a2a49e39d987b6deb27dd39b78cdb22c800035a221497cb24c3eaa156592` 的 `inputs/project_dependencies/successful_process`：

- startup schema1、source/lock的path/hash、共同lineage、source_hashes/locks/assets/binary_hashes、两host与同provider身份均有对应输出。
- build schema1/task=`TP07-E build`/`sdk_version`及source/lock/startup SHA与193 source before/after字段吻合。process补充deadline_seconds/log_path/environment，保留真实argv/cwd/exit/host exit/timeout/termination/elapsed/log hash，满足consumer字段要求。
- 当前consumer有限识别L0的原property-default XML形状和E的直接reference形状，并再核实际assets解析目标。无需修改L0 project或加TP07ProviderProject覆盖来迎合consumer。
- schema-draft的191/193、3 locks/3 assets、lock3/build5事件和两startup清单与实现一致。schema是说明，实际输出仍须Low运行后按hash审阅，不能把草案当已发生收据。

prepare/lock/build不会生成 `TP07-E approved execution`。后续root须单独绑定新source/locks/build/startup、candidate/capabilities、E toolpack及已接受L0 run/startup/toolpack/validation；E runner将再次验证L0 lineage和值门。当前脚本的批准不能代替这份runtime批准，也不自动安排E实库。

## 5. Low调用位置与后续门

执行时使用README列出的**原批准路径** `TP07-E-preparation-v1/prepare-and-build.py`，先核其SHA。脚本 `__file__.parents[4]`将该路径绑定当前workspace；Low快照额外的 `TP07-E-preparation-v1-freeze/source/`多一层，直接执行那份copy会被ROOT检查拒绝。快照是不可变审阅证据，不是这里的执行入口；无需为此修改脚本或放宽root校验。

root可据本限定静态接受安排Low prepare/lock；实际锁必须回读并以完整外部hash接受后才进入build。三个阶段实际失败一律保存并停止，不能以本报告提前宣称191/193已产出、13包图通过或双host构建成功。E工具离线门、组合build实证、runtime批准及后续实库仍各自独立。本文不接受E/D运行、最终C或发布。报告完成后停止写入。
