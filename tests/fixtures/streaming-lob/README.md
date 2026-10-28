# 流式 LOB 测试场景

`scenarios.json` 分为合成 `cases` 和 `real_database_probes`。合成用例为 `not_run`，实库过程为 `planned_not_run`。文件只包含生成参数、长度、哈希和逻辑 API 操作，不包含二进制载荷、达梦报文、LOB locator 或服务器响应。

## 确定性数据

`byte_cycle_v1` 定义为 `byte[i] = (seed + stride*i) mod 256`，seed 17、stride 131。预期 SHA-256 按顺序覆盖恰好 `length_bytes`，使用有限块增量计算。边界哈希覆盖合成 4096 字节块及其二倍边界的前后位置。1 GiB+ 用例不保存字节数组，由测试增量计算摘要。

CLOB 向量分别重复 `A雪🚀é`（UTF-8）和 `达梦数据库`（GB18030）。UTF-16 单元数与编码字节数分开记录；块大小是测试参数，不是协议限制。切分故意跨多字节字符和 emoji 代理对，最终 decoder flush 必须拒绝不完整尾部。

## 覆盖范围

- LOB-01/02：惰性打开流、分块哈希、不可 seek 的未知长度输入、增量 TextReader 编码，以及与 LOB 总大小无关的内存边界。
- LOB-03/06：UTF-8/GB18030 多字节边界、代理对、非法尾部、GetBytes 字节偏移、GetChars UTF-16 偏移、长度查询、缓冲区边界、零长度/短读、EOF 和意外截断。
- LOB-04/05：单活动字段流、两个 LOB 列、NextResult/Reader 关闭/事务结束/会话代际变化后的失效、只进 seek 拒绝和流中取消不重放。
- LOB-07/08：完整物化限制与流式读取分开；`GetString` 仍会物化，EF 字符串路径不能证明流式内存边界。

实库大对象测试前必须只读检查现有环境的安全容量，使用 TEST 下唯一对象并只清理该对象，不变更表空间。容量不足时保持未运行并报告 `integration_pending`，不能假定有数百 MiB 可用。

EF 40 KiB 兼容路径与大流式对象测试分开。准备 fixture 未构建、连接数据库或生成二进制文件。
