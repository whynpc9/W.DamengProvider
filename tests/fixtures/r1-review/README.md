# R1 审查逻辑回归数据

`cases.json` 包含四个 R1 审查缺陷的小型合成输入和期望客户端行为。所有用例为 `not_run`，不是测试结果；准备过程未执行产品、构建、数据库或网络操作。

## 覆盖行为

- SQL 块注释在第一个 `*/` 结束，嵌套样式的 opener 只是注释内容。包含可见 `; COMMIT` 的回滚逃逸必须拒绝；引号标识符保持引用，首个注释关闭后的标记可见，未结束注释拒绝。
- `GetInt32` 遇到 SQL NULL 拒绝，整数零仍返回零。
- CLOB `GetChars` 的空缓冲区长度查询返回完整 CLR UTF-16 长度；部分和零长度读取使用 UTF-16 偏移并保留分解组合序列。非 NULL 空文本与 SQL NULL 分开。
- `DbCommand.Connection = null` 在 Prepare 后清除连接关联，后续执行不能使用旧连接的语句。

## CLOB 数据与哈希

BMP 文本为 `A中e` + U+0301 COMBINING ACUTE ACCENT + `文`，共 5 个 UTF-16 单元，保持分解形式。

UTF-8 哈希是解码文本重新以 UTF-8 编码后的 SHA-256；GB18030 哈希使用 Python 标准库 `str.encode("gb18030")`。两者都无 BOM、替换或规范化，只描述生成的字符数据，不包含协议 payload。

rocket 向量仅用于离线 UTF-16 切片，不声称 GB18030 可编码该字符。空文本用例保留服务器表达式未确认状态，不能从 fixture 推断服务器存的是空串还是 SQL NULL。
