# 公开流式 LOB 精确包探针

这是历史 T17 验收入口，检查指定包的公开 BLOB/CLOB 流式 API、大值边界、取消和清理。普通发布不需要运行；复现时必须使用原冻结包，旧 ACK/profile 工具保持只读。

项目只使用精确 PackageReference，核对包/version/DLL hash/MVID/deps。每轮 manifest 固定输入和预算，失败后不得提高预算或减小数据。

## 固定大输入

- BLOB：**67,109,888 字节（64 MiB + 1024）**，`byte_cycle_v1` seed 17/stride 131，unknown/nonseek/async-only generator。若产品访问 Length、Position、CanSeek、Seek 或同步 Read，输入会抛错。
- 独立参考值用一块 32 KiB 缓冲增量计算 SHA-256，不保存完整 BLOB。
- CLOB 和声明 NCLOB：各 **1,158,260 个 UTF-16 单元**，包含 emoji、BMP、NUL 和组合字符；unknown-length async-only TextReader 会产生短读并切开代理对。产品不得 Dispose 或 rewind。
- Command 总期限 300 秒、connect 15 秒、过程上限 1800 秒。

64 MiB 完整 API 拒绝和同字段公开流完整读取都必须通过，不能据此推断服务器最大 LOB。

## 公开 API 与读取要求

只调用公开 `GetStream` / `GetTextReader`，以及 `Value=Stream/Blob`、`TextReader/Clob`。

- 构造阶段不得有 GET_LOB_LEN/DATA。
- firstRead 记录实际 opcode 32 请求差额 1–2，累计 reply body 小于 1 MiB，分别最多返回 1031 bytes/17 chars；不能先扫完整值再返回小 buffer。
- 小 buffer 逐段独立检查内容、hash、UTF-16/rune，不物化大字符值。
- 64 MiB cap 必须精确匹配 `NotSupportedException` 的固定安全 limit 消息；JSON 不打印异常原文。
- `GetValue` 不得发 GETDATA，Reader 保持 Open；同 Reader 同字段随后 `GetStream` 能读正确前缀，再完成独立大值 hash。
- 覆盖 GetBytes/GetChars 向前读取、向后拒绝、null-buffer 长度扫描不消耗主流、合法 zeroLen 无 I/O、第二活动流拒绝。
- 下一行/结果、Reader 关闭和重开会话后，旧流必须在 I/O 前失效。
- 同步公开 GetValue/GetBytes/GetChars 单独统计网络，不冒充异步；其余实际异步入口 sync=0。

## 小输入与 ACK

Empty、exact-negotiated-chunk、final-short 的 BLOB 与 ASCII CLOB 共六个真实小输入，验证调用方短读不能当 EOF，并用 AfterChunk 和公开流内容检查 `[0]`、`[chunk,0]`、`[chunk,17]`。

chunk 取当前环境实际协商值与 32768 的较小值，不假设所有环境都是 32000。PUT_DATA2 ACK 只使用已观察的 261/status0/body21 数字 header，不读 token/body。

## 故障、观察与清理

真实故障包括：输入在已上传块后抛错、opcode 26 发送后取消、opcode 32 读取发送后取消。要求：

- 调用方流不被 Dispose。
- 保留原 token、Unknown、reusable=false（取消）、Broken/Closed。
- 不重放、不增加 socket。
- 新 TEST 连接确认本轮业务行 0。
- 精确 DROP 唯一 `T17S_<20hex>` 表，再由另一新连接确认不存在。

旧 40 KiB array/string 单列回归，不能替代大值。缺功能、错误或清理失败均非零，没有隐藏 skip。畸形响应、截断和 >1 GiB 逻辑输入由离线 LobTests 负责，不能用真实库结果替代。

只读观察包括 opcode 32/29/26、实际 reply body 峰值、input AfterChunk 数字和 sampled retained output buffer。它们不包含 CLR 对象或协议额外缓冲的全部占用，不是进程峰值、RSS 或长期压力测试。最终保持 all-discard/reuse=0，owner 六项计数归零。

仅使用 TEST/TLS wrapper，每个新连接验证 USER/Schema 和实际 mode。覆盖限定 TLS 8.1.4.6/GB18030/msg11 与 shared 8.1.5.60/UTF8/msg21；声明 NCLOB 沿该环境使用与 CLOB 相同编码，不声称 national charset 全覆盖。安全 JSON/checkpoint 不包含 SQL、值、locator、凭据或异常文本；超时后按本轮精确名称恢复，原失败不改写。
