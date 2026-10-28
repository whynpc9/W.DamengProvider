# 独立合成 LOB 测试数据

本目录包含小型固定文本、与协议无关的逻辑输入调度、一个大值描述和合成行外偏移样本。所有用例为 `not_run`；没有服务器抓包，也不会生成大 payload 文件。

## 生成方式

```sh
python3 tests/fixtures/r3-lob/generate_vectors.py
```

生成器只用 Python 标准库，写入 `vectors.json` 和 `schedules.json`。元数据记录 Python 实现、运行时和 codec 名称；GB18030 结果只属于该参考运行时，不识别服务器映射版本。

固定字符串为 `A\u0000雪🚀e\u0301Z`。JSON 保存两种参考编码的字节 hex、UTF-16 序列、rune 数、码点、字节数和 SHA-256，并列出每个字节及 UTF-16 切分位置，标记落入字符或代理对内部的切分。组合符保持原样，不做规范化；严格往返和非法样本拒绝由生成器先行验证。

孤立代理项使用 UTF-16 标签，避免 JSON 出现非法代理文本。非法 GB18030 尾部只是该参考 codec 输出的截断前缀，不概括所有实现。

## 调度与大值模型

`schedules.json` 覆盖空输入、整块、末尾短块、未知长度不可 seek、调用方持有、源读取错误、取消，以及下一行/结果、Reader 关闭和会话代际变化后的失效。事件名只是测试调度标签，不是达梦消息或抓包。

`vectors.json` 描述 1 GiB + 1 字节 BLOB，使用 `byte_cycle_v1`、seed 17、stride 131。消费者可按 16 KiB 块直接增量计算 SHA-256，不预分配或写盘；fake recorder 不保留 payload，只保存聚合计数和增量 hash。该限制属于测试宿主，不是产品内存测量。

小型行外样本为每行使用独立 Unicode 值和唯一 marker，范围预期使用 UTF-16 偏移。`out_row_profile_large` 是独立大控制值：保存完整确定性内容，UTF-8 超过 128 KiB 但远低于 64 MiB 物化上限，包含分布式 marker 及 UTF-16/scalar 偏移，混合 ASCII、BMP、emoji、NUL 和未规范化组合序列。这些只用于发现偏移错误，不证明服务器行外存储行为。

测试应按[数据源与 LOB](../../../docs/compatibility/R3.md)消费这些输入，并将结果标为 synthetic/offline 或改进证据，不得标为 OfficialCapture、实库结果或 NCLOB/服务器字符集证据。文件不含认证字节、CRC、服务器转储、locator 或协议包。
