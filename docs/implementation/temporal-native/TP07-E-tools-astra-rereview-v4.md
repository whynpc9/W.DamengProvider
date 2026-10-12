# TP07 E tools v4：单行fixture路径修复复核

2026-10-09。结论：**ETOOLS-V3-B01_closed / scoped_static_accepted_for_Low_offline / E_runtime_pending**。本轮只核单行修复与冻结身份，未运行SDK、测试、validator、runner或DB，未修改实现。

相对Low冻结v3，24项输入只有 `test_ef_contracts.py:224` 一行改变：`replay=Path(folder)` → `replay=Path(folder).resolve()`。该fixture随后的项目路径、assets引用key/projectPath及预期都由同一个规范化根生成，与生产 `project_dependencies` 的路径合同一致，消除macOS `/var` / `/private/var`别名不一致。没有放宽validator或删除正反例。

`test_ef_contracts.py`实算SHA256为 `c439d7d8c73bca5ccee9c7b00341f52b0357bbe31e9110e58a907d6ef1d5adc2`；其余23项逐项不变，包含validator `8ac6a2a49e39d987b6deb27dd39b78cdb22c800035a221497cb24c3eaa156592`、三项目精确XML/实际恢复图规则、B02八个reuse协议、全部旧负例及15个计划合同方法。

Low `.local/temporal-native/verification/TP07-E-tools-v4/` 的toolpack manifest实算SHA256 `1f47a2a2d5eeadc1091df1b526868e7e6b8b46a6abd5d1e581db213986521268`，24-source manifest为 `0f0e8871673b658f274e520678a0db9480e83f58be7be8a93fae7e786e672e4e`；24项live/snapshot全部匹配。[v3报告](TP07-E-tools-astra-rereview-v3.md)仍为 `a6f8366a11895d65c89b0dcd73373d8cb96b5132e696179e2923b0ad14bfca12`，v2实际失败及旧源/报告保持原样。

未发现此单点差异新增阻塞，继承v3对三项目依赖接口及v2 B02的限定复核，可交Low执行全部15项离线合同。该结论不是测试通过，也不接受尚待外部准备的193源/锁构建闭包或338项E实库；后续SDK/DB仍由root安排。D与完整EF-C保持pending。
