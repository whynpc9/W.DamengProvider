# TP07 guarded Down：达梦候选 SQL 与执行门

2026-10-09。**SQL 方言与 EF 装配路径可进入实现；当前候选语句尚未实库验证，Down 执行接受仍 pending。** 本次只读固定源码、既有证据与达梦官方文档；未执行 SQL/SDK/tests，未改产品或冻结 runtime 设计。范围仅本轮 owned 的单个 DT 或 DTO 列、专用示例 migration，不给通用 generator 加降精度禁令。

## 1. 已核依据

固定 EF `5863d3755dbd2d333ac359bf1c0a789762adbaed` 中，下列路径相对 `.local/temporal-native/TP07/v1/base-source/`：

| 依据 | 可证明 / 不可替代的门 |
| --- | --- |
| `src/W.EntityFrameworkCore.Dameng/Migrations/Internal/DamengMigrationsSqlGenerator.cs:38–45` | provider 已用独立 `BEGIN / IF / RAISE_APPLICATION_ERROR / END` 前置 guard，`EndCommand(suppressTransaction:true)`；不是借 Oracle 语法猜测 |
| 同文件 `:61–77`、`:214–221` | standalone anonymous block 保留为一条 command；不把它包装成 `EXECUTE IMMEDIATE`；SqlOperation拒绝DIsql `/` 分隔符 |
| `test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationsFunctionalTests.cs:118` 的 `CommentPrefixedInlineBlocksExecuteAsGeneratedCommands` | pin 中有 BEGIN/DECLARE 独立 SqlOperation 的实际功能测试代码，包括 `SuppressTransaction=true`；本次未重跑，不能将其当本候选执行结果 |
| `test/W.EntityFrameworkCore.Dameng.Tests/DamengMigrationsSqlGeneratorTests.cs:137` 及 `DamengReverseEngineeringFunctionalTests.cs:1273–1277` | 前者断言 guard在DDL之前；后者有强制 guard失败的实库测试代码。新工具不复制其任意异常 Message 输出 |
| [达梦 DMSQL 异常处理 §6.4/6.6](https://eco.dameng.com/document/dm/zh-cn/pm/dm8_sql-exception-handling) | `RAISE_APPLICATION_ERROR(error_code, message)`，码域 −30000…−20000；无handler则外传、当前块后续语句不执行 |
| [达梦函数手册：MOD、TO_NUMBER、TO_CHAR](https://eco.dameng.com/document/dm/zh-cn/pm/function) | MOD取余；TO_NUMBER把数字字符串转DECIMAL；时间TO_CHAR接受FF1…9。显式FF9避免依赖默认fraction格式。文档不是两个固定服务器的结果保证 |

TP06 已接受持久值的完整 `TO_CHAR(V,'YYYY-MM-DD HH24:MI:SS.FF9')`，TZ另加 `TZH:TZM`，并与raw整数/typed值比较；见 [TP06最终接受](TP06-astra-final-acceptance.md) 与 `tools/TemporalCandidateAcceptance/Observations.cs:9–10`。本次新用的**单独FF9及其TO_NUMBER/MOD组合**必须另做下述表达式控制，不能借完整FF9接受自动宣称组合已过。

## 2. 精确候选 SQL

模板中的 `__OWNED_TABLE__` 只能由冻结allowlist中的本次大写nonce标识符替换，`V` 是该fixture唯一待降精度列；schema固定 `WDM_PROVIDER_TEST`，不能接受用户任意SQL、schema或column。DT和DTO分别用独立fixture运行同一模板。code −20070为scope拒绝，−20071为精度/格式拒绝；消息仅工具自有常量。

```sql
DECLARE
    v_user VARCHAR(128);
    v_schema VARCHAR(128);
    v_bad BIGINT;
BEGIN
    SELECT USER, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())
      INTO v_user, v_schema
      FROM DUAL;
    IF v_user IS NULL OR v_schema IS NULL
       OR v_user <> 'WDM_PROVIDER_TEST' OR v_schema <> 'WDM_PROVIDER_TEST' THEN
        RAISE_APPLICATION_ERROR(-20070, 'TP07_DOWN_SCOPE');
    END IF;

    SELECT COUNT(*) INTO v_bad
      FROM "WDM_PROVIDER_TEST"."__OWNED_TABLE__"
     WHERE "V" IS NOT NULL
       AND (TO_CHAR("V", 'FF9') IS NULL
            OR LENGTH(TO_CHAR("V", 'FF9')) <> 9
            OR MOD(TO_NUMBER(TO_CHAR("V", 'FF9')), 1000) <> 0);
    IF v_bad > 0 THEN
        RAISE_APPLICATION_ERROR(-20071, 'TP07_DOWN_NONMICRO');
    END IF;
END;
```

不附 `/`，不含 `COMMIT`、DDL、DML、动态SQL、异常handler或`WHEN OTHERS`。缺表/缺列、格式转换、权限等错误均原样中止，不能包成“已正确拒绝非微秒”。上面长度/null防护不能代替表达式门的严格ASCII九位验证；若数值转换自己抛错，candidate失败，不算 −20071 正例。

数学依据：FF9给秒内纳秒整数 `n∈[0,999999999]`，六位可表示当且仅当 `n mod 1000=0`。七位列的正常tick尾数形成`…000/100/…/900`，故非零第七位必拒绝。无timestamp→timestamp6 CAST、ROUND、TRUNC、浮点或UTC转换；DTO以本地fraction检查，offset由前后独立oracle保持。NULL不含待截断fraction，可通过；空表可通过，但先证明表/列存在且当前为目标type7，不能把空表当任意对象许可。

调用前必须从实际TEST连接核精确profile、catalog列type/scale7、owned marker、表/列/约束/index/default、专用history及已应用Up记录；catalog TZ编码与wire scale分别解释，不能盲写 `DATA_SCALE=7` 冒充TZ校验。使用现有catalog来源规则与raw证明。所有匹配失败在migration前结束。SQL内仍自检user/schema，并显式限定TEST；对象丢失会编译/执行失败。无并发DDL/写入的独占窗口是先决条件。

## 3. EF migration 的实际命令顺序

guarded sample `Down` 第一个 operation 为 `migrationBuilder.Sql(guardSql, suppressTransaction: true)`，然后才是 `AlterColumn` 7→6；类型/default/nullability等其余facet完整保留。若未来同一个migration降两个列，必须在第一块中先检查两列再允许任何ALTER，不能“guard DT→ALTER DT→guard DTO”。当前最小实现每fixture一个待降列。

使用实际 `IMigrationsSqlGenerator` 获取命令列表，冻结并断言 command[0]是上述完整块、TransactionSuppressed=true、只一个block；后续command才有ALTER，history删除最后。通过真实 EF migrator/executor顺序执行；不把整段 `DECLARE…END; ALTER…` 拼成一个 DbCommand，不按分号切块，不手写循环catch后继续，不启用失败自动重试。非idempotent真实迁移是主门；生成script可留证，但脚本文本不替代实际执行。

“第一条”是 **Down migration command列表第一条**，不是整个进程第一条SQL：EF会有history读取/ensure及既有DBMS_LOCK获取/释放。运行前已存在owned history及Up记录，必须审并观察这些前导不产生新的schema/history mutation；如仍实际建/改history，门失败回报，不能把它排除在保护范围之外。失败后无ALTER、无history删/写，fresh比较所有受保护对象。guard发出并返回真实server错误是预期行为，不能要求此门“业务发送0”；要求的是**后续破坏性命令未执行**，用实际command/发送顺序与fresh结果共同证明。

本guard是一条数据库调用内完成检查与抛错；它与后续ALTER不构成在线原子事务。达梦DDL隐式提交，不能依赖rollback恢复或称DBMS_LOCK迁移锁自动阻止所有业务writer。开始前确保写入方静止、没有待提交业务事务；全程保持本轮独占窗口。

## 4. 最小控制与验收顺序

| ID | 输入/调用 | 必要结论 |
| --- | --- | --- |
| `DOWN-G00-expression-{dt,dto}` | 用已接受typed写入在7列保存固定普通日的fraction `.1234560`…`.1234569`、`.0000000`/`.0000001`；读 V raw+完整FF9，再读 `TO_CHAR(V,'FF9')`、TO_NUMBER结果与MOD结果 | strict九位ASCII；整数等于raw纳秒；tail0余0，tail1…9余100…900。DTO用合法非零offset并核原offset；无literal种子替代typed输入 |
| `DOWN-G01-extremes-{dt,dto}` | 已接受域内 min、min+1tick、max、max−1tick、max−9ticks；DTO极值offset0，普通向量另cover330 | min与max−9ticks可降；min+tick/max/max−tick拒绝。所有期望由固定integer ticks算出，不从producer `expected`取值 |
| `DOWN-G02-control` | whole-micro、NULL、空表分别执行独立guard；再加非micro行执行同块 | 前三无业务错误，后一必须实际verifiedserver −20071；不能接受任意 DmException/SQL语法错或仅Exception.Message匹配 |
| `DOWN-G03-refusal` | 已Up7并保存nonmicro，调用真正 guarded Down | 精确 −20071，随后ALTER/history mutation0；fresh仍scale7，原行/rowcount、defaults/index/constraints/history相同；不会为了继续测试自动删除失败行 |
| `DOWN-G04-success` | 由明确下一控制stage只删除本次nonmicro行，fresh证明全部可降，再调用同Down | guard先成功、ALTER后成功、history正常更新；fresh为6且全部剩余数据/约束/default/index保持。不得重写全部值使其可降 |
| `DOWN-G05-scope` | 离线生成器/runner负例：非allowlist对象、错误schema、缺少guard、倒序ALTER、缺少history/owner preflight、catch继续 | 均fail closed；不为测试scope错误连接其他账号或执行跨Schema SQL |

真实错误收据至少包含实际statement/command ID、server response来源/错误码、异常类型、执行阶段；未知message不输出。若W错误码投影不同，不预先放宽 −20071；保留安全response证据交Astra核实。fixture最后仅清理本次owned对象，fresh absence与原失败同时保留。

shared七位profile承担完整G00–G04。TLS尚无七位typed写能力，不能靠literal/改cap造出该门：只验证适用六位控制与原七位关闭边界；完整七位Down向量标 profile外，不能称TLS R06七位安全已证。若需要TLS固定7路径，须先有独立批准的真实能力/输入来源，不由本设计新增。

## 5. 待运行证明与交付界线

仍待 Low 在同TP06 W包、最终EF overlay上确认：匿名块真实W传输与fixed错误码、FF9-only/整数组合精确性、真正migrator遇guard错误立即停后续命令、history前导无变更、7→6成功控制与失败fresh状态。上述任一失败先保存该轮证据并停止Down，不“修一下SQL继续跑”。只在guard控制完成后激活真正ALTER测试。

实现仅进入已划定 D owner 的 `MigrationGuardSql.cs`、专用migration/控制与独立validator；不改通用provider generator、W产品、旧runtime设计或历史证据。这是可实施候选与验收设计，未宣称数据库已执行通过。

### E → D 的 scaffold6 交接

E owner 的 R03 先承担实际显式6源码模型；它不能被命名为“已验证scaffold6”。R05由D owner闭合真实 `catalog → CLI生成scaffold6源码 → compile → 该生成模型的 SaveChanges/query → fresh oracle`。不仅检查生成文本，还须执行精确微秒/NULL往返，并观察实际mapping/parameter与最终schema仍6；不能手写一个scaffold-like模型、补 `.HasPrecision(6)` 修复生成结果或改默认7后冒充原生成模型接受。生成模型的snapshot/新migration无自动升7与实际往返均须有证据，E门通过不能替代这一D门。
