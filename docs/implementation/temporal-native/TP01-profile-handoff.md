# TP-01 → TP-03 profile 交接草稿

状态：**design_prepared / profile_evidence_pending / TP03_not_activated**。日期：2026-10-08。

本文只准备决策结构和接线接口。root 报告 v9 offline 66/17/51 已通过，shared 新三 lane 正在运行，TLS 尚待开窗；这些状态不构成 profile 接受。最终只用完成后的 v9 shared/TLS 实际报告、validator 结果、源/包身份和清理证据收口。旧 v7 及 CAST 诊断失败不回写。本次仅只读源码和已冻结 v7 安全报告，未运行测试、构建或 DB。

## 1. 已由 root 裁决的边界

| 决策 | 产品行为 | 证据含义 |
| --- | --- | --- |
| 未知/null serverVersion | 保留既有普通 legacy≤6 兼容路径；默认选择稳定16/6或23/6。七位、8/9、TLTZ需求仍 NotSupported；非整微秒 Overflow | 命名 `CompatibilityLegacyUnverified`，绝不标为新已验 profile |
| 未知版本的既有 fixed extended 六位 | 可靠服务器 fixed 来源已给26/27、列scale≤6且用户未请求7时，保留原六位写路径；不开放 extended 默认。advisory/synthetic flag不能冒充 fixed | 这是已有固定六位兼容，非扩展能力证明；用户显式7即使fixed6仍 NotSupported |
| typed NULL | 先验证目标能力、声明类型及Precision/Scale；没有值不豁免闭门 | 仅跳过值域/fraction检查，不跳过元数据合同 |
| 1582-10-05…14参数写入 | 所有含日期参数的 DATE/TS/TZ CLR和semantic text路径保守 NotSupported，理由为 `unverified_calendar_transform_region` | SQL CAST样本折15，typed精确保真未建立；不是“typed服务器拒绝已证” |
| 纯helper与读取 | pure整数核心、无连接公开helper仍可表示Gregorian日期；原SQL不重写，合法raw Reader不因上述writer区间被阻断 | 禁止把连接profile限制写入TP-02核心或当成数据库不存在该日期 |

产品按 exact serverVersion、ordinary格式和 temporal family 分项；证据另绑定实际 msgVersion/encoding 的已测scope，不用 `shared`/`tls` 环境名作产品键，不用版本大小比较继承能力。建连不新增SQL探测，不改Builder、TLS策略或session设置。

## 2. ledger 的最小结构

每一项独立保存下列信息，禁止单个 `supported=true` 同时代表读、写、默认选择和实测范围：

```text
profile identity: exact serverVersion, observed msgVersion, encoding,
                  resultFormat, temporalFamily, CType/layout/scale
evidence: tool source manifest SHA, product source manifest SHA,
          O/W package + loaded DLL/MVID identity,
          report path/SHA, lane + case_id + writer/reader,
          final validator path/SHA, cleanup/fresh-absence path/SHA
observation: operation (typed_parameter_write / SQL_CAST /
             column_DDL / raw_read / metadata / constructor),
             actual outcome/error, actual fields/format/layout,
             requested value, exactness, supported projection
decision: read-layout rule, write scales, default-seven,
          local/UTC/offset constraints, calendar exclusions,
          open/closed/compatibility-unverified, reason, root approval
```

`O write failed`、`W old guard rejected`、`verified server response`、`CAST transformed source`分别记录。只有相应实际操作可证明该操作；DDL成功不证明参数写入无损，CAST改值不证明typed参数改值，pure codec成功不证明服务器支持。来源字段保留实际raw/effective区别，原CType2 advisory不反造为26的服务器来源。

最终表须同时保存服务器事实与产品决定：例如服务器有8/9列/O可写，与本轮W8/9写关闭可以同时成立；已存在明确布局的8/9合法raw读也不因writer关闭而全局版本拒绝。

## 3. 当前仅作草稿的能力行

下表的 v7 内容是历史真实观察，**不是 v9 接受行**。最终每一格必须指向新证据或明确保留closed/compatibility状态。

| 项 | v7 已见事实 / 既有源码事实 | 拟交产品决定；最终证据 |
| --- | --- | --- |
| 普通 DATE14 / TIME15 / TIME TZ22 | ordinary cell分别3/5/7字节；TIME仅六位布局 | 继续明确布局适配，DATE scale0、TIME/TZ0…6；v9实际metadata/length/hash待填 |
| legacy TS16 / TZ23 | ordinary分别8/10字节，fraction微秒 | 六位精确写及明确布局读；v9 shared/TLS mandatory six证据待填 |
| extended TS26 / TZ27 | ordinary分别9/11字节，fraction纳秒；shared七位主门 | 8.1.5.60在v9收口后才登记默认七位/显式七位；8.1.4.6七位保持closed，六位另验 |
| TS/TZ scale8/9 | shared列可建立；O typed写成功、旧W明确NS。CAST fraction可能转换 | 本轮write8/9仍closed；已明确布局/metadata合法读按整数ns精确文本、CLR非整tick拒绝；不从CAST成功扩写能力 |
| TLTZ6/7 | 列可建立，旧O/W路径存在；跨session一致性未证明，原getter解释也不一致 | read/write closed；非NULL适用值getter NotSupported；NULL和metadata/schema不进非NULL值门，不把不适用DTO getter改为支持 |
| TIME>6 | TIME(7/8/9)表达式真实-6121 | 本轮TIME/TIME TZ新精度不开放；不把表达式错误泛化为所有未来服务器事实 |
| TIME hour24 | TIME(6) CAST真实-6118；更高scale先失败-6121 | 写24小时closed；pure raw/format可表示既定fixture，CLR投影Overflow，不取模/移到次日 |
| 1582区间 | 实际只采4/5/10/14/15；5/10/14被SQL CAST折15。所有这些含1tick请求的CAST还丢了tick | root保守关闭整个5…14参数写区间，不能声称逐日typed写已测。4/15不因这些CAST结果新增writer拒绝 |
| year0 | SQL CAST真实-6118 | 写域不开放；独立记录服务器expression拒绝与pure calendar合法性，不与BCE混同 |
| BCE | 负年CAST表达式有返回，格式化文本为0001；缺era符号的显示不能证明原BCE语义被正确保留 | BCE typed无法由CLR输入；semantic writer不开放。已知raw字段/精确文本与CLR Overflow分层，未知格式不猜 |
| offset/local/UTC | 现W guard为[-779,840]分钟；CLR构造合法性与服务器域分开；v7多offset及极值已有观察 | 本轮不据负向O/旧W失败扩张范围。待v9逐格回填；维持local/UTC均合法，已知服务端域用精确预检，未知服务器拒绝保留DmException |
| BDTA /非默认时区/interval mode/TIME输出模式 | Builder当前有守卫 | 不开放；BDTA及第二TIME模式仅内部fixture，不能算实库profile支持 |

历史依据：v7 shared `verification-summary.json` SHA `121929ab8e05c60c275fec0161ad9749506cddb3ca0279da43c7e0dc9497cf2d`，700条观察后最终validator失败；`extended.json`中calendar/TIME/BCE条目保持原样。v9最终输出位置/hash全部 **pending**，不得把此历史SHA填入新的通过证据栏。

## 4. 产品最小API与文件界面

可在TP-03激活时增一个内部 `Internal/Types/DmTemporalCapabilityProfile.cs`，由codec owner独占；须root确认激活范围。既有 `DmTemporalCodec.cs` 作为策略入口，已批准 `DmTemporalLegacyAdapter.cs` 只承担纯表示适配。TP-02五个accepted core文件不变；不新建连接探测器/配置开关/运行时JSON加载器。

建议的内部API形状（名称可按现风格调整，语义不可合并）：

```csharp
// 不持有连接/可变DmField；从本次已打开物理连接的协商属性捕获。
DmTemporalProfileIdentity CaptureIdentity(string serverVersion,
    int messageVersion, string serverEncoding);
DmTemporalCapabilityProfile Resolve(DmTemporalProfileIdentity identity);

// 目标类型/format及用户请求先行检查，NULL也调用。
void ValidateWriteTarget(DmTemporalCapabilityProfile profile,
    DmTemporalWriteTarget target);
// 非NULL CLR或semantic text转成统一ns Fields后调用。
void ValidateWriteValue(DmTemporalCapabilityProfile profile,
    DmTemporalWriteTarget target, DmTemporalFields fields);
```

`WriteTarget` 至少保存 actual CType、明确layout/format、zone kind、columnScale、encodedScale、可空requestedScale及其是否显式、fixed是否可靠；不能只传一个effectiveScale而遗失“fixed6+requested7”。其raw provenance来自TP-04不可变快照；TP-03先定义消费接口和能从当前可靠metadata取得的窄适配，不修改绑定层来伪造来源。`Profile`内部包含状态（已批准/兼容未验证）、按family的write/default-seven策略及范围限制；只接受compiled immutable行和内部evidence ID。

`CaptureIdentity`取 `conn.GetConnInstance().ConnProperty.ServerVersion/msgVersion/ServerEncoding`；当前 `DmSetValue.cs:1647–1648` 已从同一实例取版本。不要依赖可拦截的public ServerVersion滤镜，不按端点/账号/connection string建缓存。encoding可用限定别名规范化作scope比较，但不改变连接编码；新七位能力匹配必须落在最终批准的msgVersion/encoding范围。scope不匹配只得到兼容态，不静默扩大已验范围。

`Resolve`是无I/O查表。physical session重建、连接切换或metadata重建时重捕身份；不可持有上一连接的profile。TP-04绑定冻结保存本次profile identity和resolved target；不要增加独立全局可变缓存。

## 5. 写门顺序与兼容细节

1. 明确来源和语义：普通、TZ、TLTZ不能互换；actualfixed约束优先。requestedPrecision/Scale的用户有效性和typed NULL检查最终由TP-04冻结，公开helper prec不是Parameter.Precision。
2. 验证 `WriteTarget` 的family、layout、请求精度及profile策略。TLS/unknown显式7，即使值为整秒或fixed6也NS；8/9和TLTZ同理。shared已批准profile的fixed26/27 scale6+requested7可以保留该target，但值须同时满足两个quantum。legacy16/23请求7始终NS，不就地升级fixedwire。
3. 验证源Fields：Gregorian合法性、offset、local/UTC、已批准的保守历法区间；日历transform区间NS、不可表示精度/确定范围Overflow。未知服务器约束不新映射为Overflow，保留实际DmException。
4. `DmTemporalInteger.ValidateWireFraction(ns, requestedScale/columnScale/encodedScale)`按适用的每个独立约束检查；legacy必须ns%1000==0。普通CLR ticks先精确×100变ns，semantic text直接整数ns，无字符串数值中转。
5. 调明确layout的pure Encode写DmParamValue。helper无连接直接编码能力与连接写入授权分开；成功Decode/Format不调用或授予writer能力。

未知版本默认保持legacy，但可靠fixed已有26/27、实际scale≤6且用户没有七位请求时保持现有窄路径。此项不是新增推断默认；缺少可靠fixed不得靠值的小数尾数、advisory typeFlag2或合成field选择extended。显式DateTime2无fixed且Scale未设仍表示原生七位需求，unknown/TLS保持NS；其显式Scale≤6按v2既定legacy精确路径。

`SetString→valueOf→encode`必须与CLR路径共用上述target/value门，防止文本绕开8/9/TLTZ/历法限制。原SQL literal/CAST不经过参数时间encode的语义不改写。没有可信时间describe的普通String仍是String，不仅因内容像日期就附加时间profile。

读取由layout+actualmetadata校验驱动，既有明确普通布局不加全局版本白名单；TLTZ是单独关闭项。`DecodeForReader`和LegacyAdapter向TP-05传有/无offset、ns和明确layout，不能从legacy12 padding或名义CType猜实际12-byte格式。TP-03切换公开legacy微秒契约时七位Reader必须同候选连续可用。

## 6. 最终关闭与TP-03派发清单

TP-01关闭前，root/Low必须把下列pending项变为带新SHA的事实：v9 shared/TLS各自工具输入、runtime source/package/DLL/MVID；actual serverVersion/msgVersion/encoding；core/metadata/extended报告及计数；最终characterization validator；每lane清理及fresh absence；各layout实际CType/width/length/format；offset/local/UTC边界分类。baseline characterization接受不能冒充新candidate或TP-06产品通过。

然后root明确批准compiled初始行：8.1.5.60普通TS/TZ七位及默认策略、8.1.4.6严格六位和七位closed；逐行实测scope不扩大。未知兼容规则沿§1；8/9、TLTZ、TIME>6和BCE保持本轮关闭，不为追求新支持补无限探测。若新结果与本文历史观察冲突，以新事实定点修订后另冻结，不悄悄更新原报告。

派发TP-03时附本表最终版本、TP03-astra-design各组、helper契约及新增profile/LegacyAdapter范围；API预留给TP-04，DmGetValue只作TP-03最小typed路由，停写验收后才交TP-05。TP-06再执行候选能力及日期NS/精度/NULL/恢复测试；日期NS测试只证明产品保守策略，不称服务器不存在该日期。

本文作为 **pending草稿冻结**，不激活TP-03，不把未完成v9 shared/TLS先记接受。完成后停止写入，后续证据收口由root另行授权。
