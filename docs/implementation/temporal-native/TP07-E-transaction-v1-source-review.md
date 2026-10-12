# NEW E transaction v1 源码审阅

2026-10-11。结论：**source_rejected：B01 完整源码投影边界不成立**。必须先修复下述有限问题并重新冻结，不能据本轮开启实际合同、SDK 或 DB 验收。本文只读五文件完整差异、实际 refs/JSON 与标准库 hash/AST，未 import 项目、执行测试/validator/SDK/Git/DB；唯一写入为本文。

## 当前冻结身份

| 输入 | 实际 SHA-256 |
| --- | --- |
| 已接受设计 | `12585dadb4e18a2ead94c138555419f0e6ec3b220b0728666a162e75d5298353` |
| handoff `freeze-manifest.json` | `ec7d3d2665d65f5d1affd477a8175070f8363d2410811e82c608de69360fc79f` |
| 完整五文件 `implementation.patch` | `aa8b4fc50af1c6ae402115bab915c91793f2ff0057cabfa1e21d04ed7a1680ce` |
| 新 `prepare-and-build.py` | `7f2323750e0025fe3be2e4faec373df02be786611627a000d79b1d3f96646c66` |
| 新 preparation README | `f22a72824f0c2f70cbc74d4b42e93274f0249f4b2b5eab6a6937c1a6cee18ffb` |
| 新 preparation schema draft | `9cdea3999aea07a327dc638e7afa56dd0895c1dbf218cc77281a1600c4082a98` |
| `ef-validate.py` | `a2ccfce756c0b305ca3e6e657dfe6d4d7cc5383ea5a60c6ba1ffdcad75eb2fc6` |
| `test_ef_contracts.py` | `e14aeae3c8eee209ecfe74f885d53a540eb0d3e74f150764313f0084c61d55a1` |
| candidate E source24 / E8 pack | `bfe37c1298af7687b219669b1a6b32d50725847a4e5e5def661e78f6031fa314` / `7e8cb9854f588251910da58d6a17082d8036d4db6170d36ee07200e3ce15b852` |

handoff 位于 `.local/temporal-native/verification/TP07-E-transaction-v1-implementation-handoff`；新准备三文件位于同级 `TP07-E-transaction-v1-preparation`。五项实际源码哈希、36项 handoff assets、E24 live/snapshot、E8实际成员与映射均重新核对一致。旧28方法AST全部相同；当前新增6、合计34仅为 planned，尚未执行。

## B01：任意路径段排除隐藏额外源码

`ef-validate.py:441–443` 的新 `inputs` 分支使用：

```python
not set(f.relative_to(replay).parts) & {'bin', 'obj'}
```

这会排除 **任意位置** 的这两个目录名。例如，在完全合法的196源集合之外加入 `foo/bin/extra.cs` 或 `test/unapproved/obj/extra.props`，新 projected map仍等于原196 map；前面的旧 `compile_files` 同样排除这些路径，不会补上本次完整物理闭包要求。此判断从源码可直接确定，本次没有创建文件或执行 validator 来构造通过结果。问题不是计数文字，而是会让额外文件绕过新设计要求的完整194/196来源验收。

最小修复：把实际投影抽为有限可测 helper或同等窄实现，只允许以下三个**固定项目目录**各自的 `bin/`、`obj/` 六个生成前缀不计入 source map：

- `src/W.EntityFrameworkCore.Dameng/`
- `test/TP07LiteralProbe/`
- `test/TP07TemporalRuntime/`

使用带路径段边界的相对前缀，其他文件全部计入；遍历时在生成目录排除之前拒绝 symlink，避免以目录/文件链接隐藏额外来源。最终完整 map 必须逐路径/hash 等于已验证的196 map。保留旧分支、原运行/值合同与三项目既有assets/binary核验，不改预算、产品或模板。

新增有意义的受控临时目录合同：精确源集合加六合法生成前缀为正例；`foo/bin/...`、非三项目 `obj/...`、普通额外文件、近似但非完整前缀、符号链接为负例，并验证实际投影/源码闭包错误。不得用 whole-manifest SHA 早拒代替此边界，不生成伪SDK/runtime证据。既有28及本轮6方法保持；新增方法数由 High实际声明，不预填通过数。

## 其余完成审阅的边界

未发现第二个必然的源码/断言错误，以下静态结论不抵消B01，也不表示实际34通过：

- latest v5→新prep保持五顶层/六PIN/五FOLDERS；全部6 PIN及9个transaction refs实际哈希匹配，原入口 `parents[4]` 为真实workspace。base175/dc2b、canonical178/4001、source-only66scope/d13f、product19 nested refs、L010 artifacts与E1均显式保护。source194由product178+L07+E9不交叠组成，locked196只加两hostlock；prepared source保留175与178两个不同map。
- native consumer读取de4真实完整字段集合及6982/receipt绑定，核14包/15图、13 non-W原依赖边、W及Project依赖、两个实际锁、旧origin/cache/feed/contentHash身份。没有补造旧readback字段。新 literal lock固定896258、provider lock77d4；old/new phase fresh输出与输入保护、3/5命令、三assets/双host/同provider输出保持。新增SDK version30秒是已接受设计的有限预算，restore/build仍300秒；其余原prep函数体逐字相同。
- loader只有精确d422+2c76新分支使用真实workspace；startup a1cc绑定、六suffix唯一、before/after及真实字节、四shared项一致均在compile前检查。旧snapshot分支保留；新模块从真实原路径编译，自然ROOT/HERE，不重写ROOT。原完整L0 validator会实际重新消费保存run并比较266a结果，没有mock成功。
- 新6方法逐项静态审阅：真实NEW L0完整正例；recorded path/before-after/startup/toolpack语义拒绝；六物理文件读取/缺失拒绝；pack/run/shared身份拒绝；明示为planned的194/196 map正例；transaction lineage/同计数路径与非lock hash变异。语义变异先读取原实际Ref，再只在被测读取边界替换内存对象，compile断言用于拒绝路径；真实正例仍调用原函数。各预期码与当前检查顺序静态相符。
- 独立AST确认原validator只变 `original_l0_validator`/`inputs`，新增三个来源helper，其他原函数体保持；E24成员不扩，21项保持、两tracked改动及此前已接受的runtime-validator f172重绑。E8/独立L0six分别完整绑定，L0-only runner/tests没有被冒称属于E24；E9、v11 policy218138、ef-run、原值oracles与338/25不变。

High保全报告 `e5353be49e1b51d30c78612a9d91f4fe50c19ddef14ff3e072b091b3b12a7e38` 声明56616历史项无变化；本审阅核对其报告及引用身份，不声称自行重新计算全部56616文件。旧 E288/window22、T1/L0失败和本次拒绝均须保留。修复交由root在静态窗口释放后限定授权High，新freeze/source复审后才由Low进入既有合同及后续准备链；不增加无关门。本文完成后停写。
