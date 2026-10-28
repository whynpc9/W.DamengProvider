# Reader 类型与范围读取独立测试数据

本目录包含 ROWID 渲染和文本 `GetChars` 范围的独立输入及字面预期。所有向量均为合成 `not_run`，不是服务器抓包或真实用户数据。JSON 枚举名只是测试标签，数值 `ctype` 只是分派输入，不代表服务器枚举名。

## 来源与生成

`generate_vectors.py` 只用 Python 标准库，重新实现相关 `DmRowId` 位移和字段打包，并对照固定 ROWID 文本；不会加载或调用产品。

源码依据：

- `DmRowId.valueOf(byte[])` 接受 8/12 字节形式。8 字节经 `DmConvertion.EightByteToLong` 转为 decimal `Int64`；12 字节使用本地 18 字符 base64-like 字母表和位移。所选 12 字节值前六字节为零，后六字节故意包含非法 UTF-8，只用于验证本地转换形状，不证明服务器 ROWID 范围。
- `DmGetValue.GetString` 按 `ServerEncoding` 解码 CType 0、1、2、54；CType 28 渲染 `DmRowId`。`DmDataReader.do_GetChars` 使用 UTF-16 单元偏移。
- `DmDataReader.do_GetBytes` 拒绝 CType 0、1、2；`getbytes_rejection_controls` 以 `InvalidCastException` 防止新增文本 GetChars 时意外扩大二进制支持。

当前行为见[类型说明](../../../docs/compatibility/T09-types.md)和[数据源与 LOB](../../../docs/compatibility/R3.md)。

## 固定值

混合文本为 `A雪🚀e\u0301\u0000Z`：7 个 Unicode scalar、8 个 UTF-16 单元（`0041 96EA D83D DE80 0065 0301 0000 005A`）。

- UTF-8：`41E99BAAF09F9A8065CC81005A`，13 字节。
- Python GB18030：`41D1A995308B34658130BC37005A`，14 字节。

JSON 对 CType 0 和 54 重复这些向量，包含完整 UTF-16 数组、长度查询、落入 emoji 代理对内部的偏移和末尾短范围。生成器使用严格 codec 校验字节；结果只属于该 Python 参考运行时，不识别服务器映射或 NCLOB 行为。

| 形状 | 原始字节 | 预期文本 | UTF-16 单元 |
| --- | --- | --- | ---: |
| 8 字节旧形式 | `FFC080F59F988001` | `108254203842969855` | 18 |
| 12 字节当前形式 | `000000000000FFC080F59F98` | `AAAAAAAAAA/8CA9Z+Y` | 18 |

两个 ROWID 原始值都不能按严格 UTF-8 解码；测试只使用渲染字符串和 UTF-16 范围，不通过 ServerEncoding 解码 ROWID。

## 重新生成

```sh
python3 tests/fixtures/r3-reader-types/generate_vectors.py
```

只写 `vectors.json`，不创建数据库数据、凭据、大型制品或网络流量。
