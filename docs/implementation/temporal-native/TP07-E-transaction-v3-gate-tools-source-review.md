# TP07-E transaction v3 gate-tools source review

结论分开记录：A stored verifier `SOURCE_ACCEPTED_IN_SCOPE`；B historical consumer `SOURCE_REJECTED`，有一个确定阻塞 B01。两工具均未在本审阅中执行；本报告不声明 GeneratedCreate28 新gate已产生、历史12例已通过或当前E runtime接受。

## 实际冻结输入

- 总handoff `TP07-E-transaction-v3-gate-bindings-implementation-handoff/freeze-manifest.json`：`102d128f75db207a1d291b34c3592cc30e9da867153886f4f1d745d3f5c366a0`。
- B handoff `TP07-E-transaction-v3-historical-consumer-handoff/freeze-manifest.json`：`0633a657ceddaea4ce70a224c5811e95003e46952e548f8a82b56b05a6575024`。
- A `TP07-E-transaction-v3-generated-create-reverify-control/verify-stored-v4.py`：`deaa0eb739a5d88c0d6fb32b5e043fe265e521c480651b5e7d6f1fef00ddaeb5`。
- B `TP07-E-transaction-v3-historical-consumer-handoff/revalidate-history.py`：`c5ec9f7dd3f87384b1f770ea46a816885370bc3ad5e26ec2be4c60dd4338aaed`。

上述目录均位于 `.local/temporal-native/verification/`。本次独立核验两个freeze、所有handoff成员和源码副本SHA，B的15个OLD_REFS、8个CURRENT_REFS、23个WINDOW_FILES及旧artifact map全部1158个实际文件哈希均匹配。只使用文件读取、JSON、标准库哈希及AST解析，未导入/执行任何项目模块。

## A：五处常量换绑可接受

相对原 `verify-stored-v3.py`，独立AST比较确认恰5个Constant改变：新输出目录、新pack路径/SHA、新派生before-map绝对路径、新partition gate路径。归一化后完整AST相同，45个顶层statement、18个assert及所有判断body保持。

新before-map `d3c4d128f618b2e73390d66d985d22b3c230c05863ae351f619ae456d47d301a` 与母map `73254163e8b1928e70f6964d73d38ee67ad2c30d897e22a30dffa205ff200b47` 的103572个键完全相同，恰两项变化：当前ROOT下validator `c2cf988… → 5b534116…` 和test `b9801dee… → 41ca79e7…`。实际两源码匹配新值；其他103570项的记录值未改。High的逐项实际回读结论与派生proof已冻结，本审阅未冒称重复遍历这103572项。母map、原producer输出及历史保护语义未重写。

第11行的绝对路径实际存在；Path与绝对右操作数组合仍指向该派生文件，不会落回旧producer子目录。startup仍为 `1cadd834dacf071f35355188d549976f2b750af579f442f498630031310eb743`；producer、deep/cohort基线、metadata commands与实际服务/SQL比较保持。新partition gate `829e337534d038fcb1a1d9264667ab2889d0877dd90b78834459023a41f30f9e` 的process和report与旧gate完全相同，独立validation Ref为 `6fcbad6f281c2b35796ddb27e08ec121d623404bf2ef98f42b498d5d96feabcc`，identity绑定新pack `b48626947bb838f053755acd2a82f0262c8e903d65a50291cbec48751e0f41e5`，并未伪造新host事件。

A仍从实际cwd定位源码，为加载模块设置真实源码 `__file__`，经 `pack_entry`、实际二进制身份及 `offline_gates` 判断；后续Low须在固定ROOT按冻结脚本运行。当前输出目录仅有脚本，六个结果文件尚不存在。A源码接受不是其未来28例验证接受。

## B01：历史main命令期望遗漏八个argv项

位置：B `revalidate-history.py` 第120–125行。`expected`只构造到 `--output <main目录>`；第123行仅对cleanup追加参数，第125行随即要求 `host['command'] == expected`。

实际冻结 `main/E-host-receipt.json`（SHA `4fbd81bb147ec3d36fea2b22f8aec7b404304bc9ac273c2dd7ad01d80fb9821d`）的main命令还有以下八项：

```text
--group mutation --window-index 0
--run-id cc65a80b9e204293b87ad695c231dc45
--cohort-id 167591e064975bbae596e7aa9e82ce7180712d57b2e2cf1745e533760676e79f
```

因此，即使新context及此前全部门均成功，当前B也必然在 `exact_old_child_command` 拒绝，不能到达12例语义验证。这是静态比较实物argv所得，未执行消费者。

最小修复限第123–124行附近：为main分支追加上述原group/index及 `OLD_RUN_ID`、`OLD_COHORT_ID`；cleanup原参数保持。保留完整命令相等断言，不能截断实际命令、改旧receipt或省略身份比较。由High在新的冻结修复范围处理，当前冻结候选及本拒绝报告保持。

## B其余边界与后续

除B01外，本轮未发现其他必修阻塞。B严格核实际cwd/脚本路径、当前源码SHA及模块真实ROOT；调用原 `R.open_context`，不传关闭L0校验的参数。当前 `inputs` 默认实际调用L0 `validate_artifact`。旧1158制品、source24快照、732项原before/after、旧pack/approval/run/cohort、host/日志/12SQL及原失败validation均有有限检查；仅两已审当前源码通过旧snapshot保留历史SHA并校当前SHA，其余实际历史输入保持。

未来inputs schema恰3字段，approval由root在A实际成功后提供；没有预造SHA。当前context的产品/构建/L0 artifacts必须等于旧approval的对应Refs，仅允许新partition/generated gates。源码24与pack8实际结合，随后调用真实签名 `validate_report(report, cleaned, ledger, 'tls', 'mutation', startup, startup_sha, oracle)` 要求12，再调用 `generated_sql_contract`。未调用E `validate_window/run`，未改写report、旧cohort或ROOT，没有mock oracle。完整report函数继续核全部case、C#/provider/startup身份、协议、fresh读值和两次清理。

只有全部校验及前后pins通过才创建固定新目录，以独占创建方式写三个输出；当前目录尚不存在。成功报告明确 `runtime_accepted=false/current_cohort_accepted=false`；异常仅输出固定安全拒绝文字并exit1，不暴露原异常或凭据。Low将来的180秒/reap2外部进程控制不由此源码审阅执行或替代。

本轮只新增本报告，不修改A/B、tracked E8/source24、W/EF/C#、旧证据或状态。原39合同结果不因未变源码重复运行，本审阅也未运行测试。A后续执行与B修复后复审/执行须分别授权；历史语义验证不能替代全新同轮338例/25窗及原预算、完整清理和最终独立接受。报告冻结后停写。
