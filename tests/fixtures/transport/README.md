# 传输与帧边界测试数据

`scenarios.json` 提供 S04 NET-01 至 NET-04 的确定性合成输入。所有用例都标记为 `execution_status: not_run`；`expected` 是期望行为，不是已观察结果。

## 字节字段依据

- [`T02_0200008D.cs`](../../../src/W.DamengProvider/Internal/Legacy/Dm/T02_0200008D.cs#L142) 定义 64 字节头：命令偏移 4、正文长度偏移 6、校验字节偏移 19，heartbeat 命令为 269。
- 同文件 203–208 行写入命令和正文长度，正文长度为缓冲区长度减 64；`T02_02000092.cs` 361–370 行按 little-endian 写整数。
- 298–335 行仅在协商 `crcBody` 且命令不是 200 时使用正文 CRC32；STARTUP 200 使用头部 XOR。338–367 行为校验实现，385–622 行为 CRC32 表。
- Heartbeat 使用短合成正文和 `crc_body_negotiated: false`，明确覆盖 XOR 路径。

未建模的头字节填零，只为让解析输入可复现。这些不是合法应用消息或服务器抓包，也不推断未知字段含义或服务器协议上限。

## 覆盖范围

- NET-01：逐字节头读取、分块正文、短发送、零进展发送和合并帧。
- NET-02：头/正文 EOF、负长度、总长度溢出、64 MiB 产品帧上限、长 decimal 运算溢出、正文 CRC32 和 STARTUP XOR 不匹配。
- NET-03：heartbeat 头和完整正文消费，原命令期限不变。
- NET-04：DNS、TCP、TLS、LOGIN 共用一份连接期限。

64 MiB 是产品的总帧保护值，不是服务器最大值。超限输入只含小头部，不实际分配大正文。decimal 使用字符串，避免 JSON 数值舍入。

CRC32 示例由上述源码算法计算：64 字节头（命令 6、正文长度 8）加正文 `A1B2C3D4`，trailer 按 little-endian 写入；错误向量翻转其中一位。它只用于确定性解析测试，不证明对端会接受该消息。TLS 当前规则见[安全说明](../../../docs/compatibility/T07-security.md)。

准备 fixture 不会打开 socket 或连接数据库。
