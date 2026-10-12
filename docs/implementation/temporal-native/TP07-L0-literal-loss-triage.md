# TP07 L0 literal 丢第七位：事实、候选与有限探测

2026-10-09。状态：**L0_hard_failure_preserved / diagnosis_design_only / no_expression_accepted**。本次只读原始报告、冻结源码及达梦官方文档；未运行SDK、测试或DB，未读secret、修改参数/实例、产品、mapping或旧证据。下面是独立characterization的提案，不是执行许可。

## 已确认事实与尚未确认的原因

实读 `.local/temporal-native/verification/TP07-L0-v2-runtime-control/verification-summary.json`，SHA256为 `44472875a8dc7666899d961b6f9456dc67711a1b8a4f51627ae1a7ca7e77dd0d`；主报告 `TP07-L0-v2-runtime/shared/main/report.json` SHA256为 `ed5310c8feccfb0705b85a4bda12333a3ffd60a5ae9ea353820e37486757290d`。

- 首case `LIT-DT7-tick1`实际mapping为 `DamengDateTimeTypeMapping`、StoreType `TIMESTAMP(7)`、mapping Precision7。实际生成 `TIMESTAMP '2026-07-23 14:15:16.0000001'`，字符合同正确；INSERT affected1。
- **目标目录已是七位**：报告catalog中V的DATA_TYPE=TIMESTAMP、DATA_SCALE=7、DATA_LENGTH=9；DATA_PRECISION=null不是scale未设。原报告 `catalog_contract=unverified` 保持，本文不回写状态旗。
- **没有读侧CAST**：冻结 `TP07-L0-tools-v2/source/.../TP07LiteralProbe/LiteralOracle.cs:30`实际SQL为 `SELECT V,TO_CHAR(V,'YYYY-MM-DD HH24:MI:SS.FF9') FROM <owned> WHERE ID=1`。第一列原始ordinary cell在任何temporal getter之前捕获。其ResultColumn CType26/width9/scale7/masks0与目录一致。
- 原始字节 `EA87BBEE8100000000`的fraction为0，FF9为`.000000000`，typed ticks为 `639204129160000000`；请求为 `639204129160000001`。故不是单纯显示格式少一位，也不能解释成列本来为6。当前证明的是该literal写入—原列取回链路没有保留第七位，尚不凭此定位内部parser/转换/赋值哪一层。
- physical TEST身份为8.1.5.60 / message21 / utf-8；W DLL `843710be…`、MVID `aa23e673-957c-4b1e-bf0f-291ff60077e6`与已接受TP06同包。主进程exit1；主finally与独立cleanup均证明owned对象fresh absent，独立cleanup exit0。只观察1/12 shared case，bare/TLS/E/D未执行；原full validator也未执行。

当前L0在literal mismatch处停止，**没有在这次同表/同read SQL中执行paired typed7 control**。旧TP06 typed7成功不能替代这项定位控制。mapping-v4源码 `DamengTemporalParameterFacets.TimestampLiteral`保七位字符后返回TIMESTAMP引导literal，没有构造显式精度SQL表达式；DT/DTO的GenerateNonNullSqlLiteral都调用它。修复若有必要应定位该窄层，但现在不能先猜表达式再改产品。

## 官方依据及其适用边界

1. 官方配置文档将 `IFUN_DATETIME_MODE`列为静态参数：0是相关时间系统函数/转换的六位模式，超过六位的字符串输入可能截断，显式更宽声明可补零；1为九位模式。**这与现象吻合，只是候选解释；当前实例实际值未观察。** 不允许据此修改参数或重启。物理version/message/encoding profile没有包含该值，typed能力不能推出literal能力。[DM物理存储结构，IFUN_DATETIME_MODE](https://eco.dameng.com/document/dm/zh-cn/pm/physical-storage.html)
2. 官方函数手册列有 `TO_TIMESTAMP(char,fmt)`、`TO_TIMESTAMP_TZ(char,fmt)`、`FF1…9`和 `FROM_TZ`；FF行为依兼容模式，部分模式即使写FF7–9仍最终保六位。该手册的datepart表把 `SQL_TSI_FRAC_SECOND`定义为**毫秒**，`MICROSECOND/US`为微秒，未列100ns单位。因此格式指定位数不是无损保证，整数FRAC_SECOND不能当tick加法。[DM函数，日期时间函数及格式](https://eco.dameng.com/document/dm/zh-cn/pm/function.html)
3. 官方目录说明 `V$DM_INI`含PARA_VALUE/SESS_VALUE/FILE_VALUE，`V$PARAMETER`区分当前会话/系统/文件值。只读查询应保留这些角色，不能把文件待生效值当实际运行值。TEST是否可读本轮未知；权限不足记录未知，不用SA/提权。[系统管理员手册附录2，数据库配置参数](https://eco.dameng.com/document/dm/zh-cn/pm/dm8-admin-manual-appendix2.html)
4. 有限官方检索未找到 `TIMESTAMP(7) 'text'`这个**精度放在literal引导符**的语法依据；类型声明支持(7)不等于该literal语法成立。未找到达梦 `DATETIME2FROMPARTS`支持依据；Microsoft官方将它记为SQL Server函数，不能移植为达梦事实。[Microsoft DATETIME2FROMPARTS适用范围](https://learn.microsoft.com/en-us/sql/t-sql/functions/datetime2fromparts-transact-sql)

在线手册不是8.1.5.60服务器二进制的固定语法/行为证书。以下候选只据已记录语法提出，全部实际结果仍未验证；本文不接受任一表达式跨版本/兼容模式通用。

## 候选矩阵：先小门，再扩展已存活候选

下表`S7`是**普通字符串** `2026-07-23 14:15:16.0000001`，`S9`为同值`.000000100`，`Z7/Z9`分别追加空格及明确`+05:30`；不可先把S7变成已经丢位的TIMESTAMP literal再传TO_TIMESTAMP。ID、字符串和值均固定合成数据。

| ID / 优先级 | 候选表达式（待测） | 用途与限制 |
| --- | --- | --- |
| C0 / 首先 | 同包W typed DateTime2、Scale7、Precision0参数插入V7；DTO用DateTimeOffset、Scale7 | 必须与literal相同owned表结构、相同原列read SQL。control失败立即停，不能继续称SQL表达式故障 |
| B0 / 原失败对照 | mapping-v4真实生成的 `TIMESTAMP 'S7'` | 单独row重现并保存，保持原L0失败；DTO原literal另取实际mapping结果，不预判其损失 |
| D1 / 第一批 | `TO_TIMESTAMP('S7','YYYY-MM-DD HH24:MI:SS.FF7')` | 明确格式从原始字符串解析；返回类型/precision及持久化精度都要观察 |
| D2 / 第一批 | `TO_TIMESTAMP('S9','YYYY-MM-DD HH24:MI:SS.FF9')` | 同一100ns值的九位文本对照，不能把FF9当额外写能力 |
| Z1 / DT候选后 | `TO_TIMESTAMP_TZ('Z7','YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')` | 独立DTO候选，必须保本地ticks与原offset，不能仅比较UTC |
| Z2 / DT候选后 | `TO_TIMESTAMP_TZ('Z9','YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM')` | 同上；DT成功不授予DTO成功 |
| D3 / 有限后备对照 | `CAST('S7' AS TIMESTAMP(7))` | CAST通用语法有官方依据，目标声明7已有本地catalog依据；字符串转换是否仍丢位待测。不是 `CAST(TIMESTAMP 'S7' AS …)`，后者可能仅扩宽已丢值 |
| Z3 / 仅DT构造已精确时 | `FROM_TZ(<已证DT表达式>,'+05:30')` | 单独检查offset附加和fraction是否保留；不得凭FROM_TZ名称假定不舍入 |
| X1 / 不进入首批 | `TIMESTAMP(7) 'S7'` | 未取得达梦语法依据，暂不生成或执行 |
| X2 / 不进入首批 | `DATETIME2FROMPARTS(2026,7,23,14,15,16,1,7)` | 没有达梦支持依据，不作默认“兼容SQL Server”猜测 |
| X3 / 排除tick构造 | `TIMESTAMPADD(SQL_TSI_FRAC_SECOND,1,<base>)` | 文档单位为毫秒，不是1tick；至多作单位对照，不作为修复候选 |

不添加fractional-day浮点加法、DOUBLE epoch、先舍入后补末位、十进制转浮点隐藏运算、修改session NLS/IFUN/compatible mode、UDF或内部原始字节构造等绕行。格式缺省和裸日期字符串转换也不作为候选。

## 单独characterization最小实施范围

建议另建有限producer/runner与固定case清单，或新独立诊断入口；**不改原L0入口、期待值、accepted旗或已冻结mapping-v4**。High只实现经root批准的工具文件，Low新freeze/审核后独占运行；旧L0原报告/source/hash保持只读。全程 `candidate_acceptance=false/full_EF_C_accepted=false`，完成characterization不等于literal可接受。

1. 新empty output、固定同W包/EF source/实际DLL/MVID，shared TEST wrapper和physical身份先验。只创建两个新nonce表：一张V TIMESTAMP(7)，一张V DATETIME(7) WITH TIME ZONE，各沿L0的ID/owner marker/PK结构并保存fresh catalog。新表不是旧已清理对象的复活。
2. **第一批写入先做C0**：DT/DTO分别tick1与整micro；先证明fresh原列raw、FF9、typed全部一致，再让对应类型进入B0及候选。每candidate使用独立ID，提交完成后用fresh连接读同一列，表结构保持相同；不以SELECT表达式显示通过替代持久化。
3. 在TEST连接作一次窄只读环境观察，建议SQL如下。仅返回三个白名单参数的数值/枚举；读取失败保存安全type/number并标unknown，不申请授权、不查全参数表、不读取配置文件。IFUN只能作相关证据，不以mode值代替结果门。

```sql
SELECT PARA_NAME, PARA_VALUE, SESS_VALUE, FILE_VALUE, PARA_TYPE
FROM V$DM_INI
WHERE PARA_NAME IN ('IFUN_DATETIME_MODE','COMPATIBLE_MODE','ORA_DATE_FMT')
ORDER BY PARA_NAME;
```

若此view当前TEST不可读，仅在root预先冻结了支持的只读替代query时读取 `V$PARAMETER`对应白名单行；否则保持unknown。禁止任何SP_SET、ALTER SESSION/SYSTEM、SA/admin/bootstrap或grant。

4. D1/D2优先只用tick1、`.1234567`和整micro`.1234560`筛选；对应DTO候选先用相同三个fraction及+05:30。每个case明确结果为exact/loss/server_rejected/unobservable，不把任意异常算成功；仅有安全且可关联的语法/服务器拒绝允许记录并继续其它有限候选，timeout/transport/身份/owner/oracle基础设施失败立即停并清理。继续探索的规则属于新诊断入口，绝不回写原L0“首次失败停止”政策。
5. 第一批无候选精确时停止扩展，报告“尚无可接受表达式”；不穷举拼写。只有候选在两种普通非micro和micro控制都精确时，才扩展：ordinary尾数0…9（100ns步长）、min/min+1tick、max/max−1tick/max−9ticks，另加普通秒`.9999999`与下一秒整值以抓carry。DTO极值offset0；普通日offset0、+05:30、−05:30，再用已接受范围边缘−12:59/+14:00（构造合法UTC域）；NULL控制独立，不以NULL通过证明fraction能力。
6. 每次fresh read第一列必须直接V，无CAST/函数包装，捕获raw来源/format/type/width/scale/masks/cell长度/hash并先于typed getter；第二列TO_CHAR FF9（DTO加TZH:TZM）。独立整数decoder核ns与offset，ns必须整除100并等于请求，typed ticks和DTO本地ticks/offset须同样精确；任何三方不一致都拒绝。保留表达式SQL/hash、受影响行、row total、目录前后快照。预先保存的typed7配对control必须来自同一新run/表/读法，不借TP06历史报告。
7. 最终仅按ledger清理本次对象，main与独立fresh absence都留证。所有成功表达式的结论绑定实际physical/profile、已观察配置（或明确unknown）、SQL形状和输入域。没有精确control、未经批准的服务器错误或清理不明均不能进入修复讨论。

这里先证明**表达式直接INSERT**。即使成功，后续产品patch前仍需针对选定表达式做小型上下文门：列DEFAULT的DDL接受与fresh保存、INSERT seed、inline predicate、UPDATE表达式；DDL默认表达式可能有额外限制，不能从SELECT/INSERT成功直接推出。随后才由root开mapping overlay窄修和新的M1/L0受影响门；全E/D运行不用于寻找最基础literal候选。

## 无无损表达式时的明确失败策略

TP07主设计第3节已允许literal/default/seed/inline入口明确拒绝，而非静默丢位。若上述有限候选没有独立证明无损，建议新mapping修复仅令受影响的非整micro DT/DTO literal生成在返回SQL之前抛精确NotSupportedException，保留typed parameter的显式Scale7路径与模型Precision7；不要把模型默降到6。六位值和NULL能否继续沿原路径需自身控制，不能从本次未到达case中虚构成功。

拒绝必须覆盖实际调用GenerateSqlLiteral的DefaultValue、HasData、inline等路径；显式用户DefaultValueSql是独立raw SQL入口，不会自动通过mapping校验，必须继续标未验证/不接受，不能宣称已被全局拦截。迁移在生成阶段拒绝时核未执行DDL/业务写、fresh状态不变。能力说明明确“typed7与SQL literal7分开”；不建议通过把DDL常量变参数绕过default/seed语义。

采取该策略也需要root批准窄产品窗口、High实施、独立Low复验；它只能关闭静默损失缺陷并形成限定拒绝合同，**不能把原L0成功门、完整R04或full C改称完成**。旧失败一直保留。若未来另有实例配置支持literal7，应独立特征化，不因本文使用当前版本号推断所有同版本实例行为。
