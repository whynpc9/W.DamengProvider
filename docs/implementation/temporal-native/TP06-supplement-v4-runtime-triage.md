# Supplement v4 interval 零值失败：诊断性工具窄修

2026-10-09。结论：**read observation取证缺口已确认；具体运行失败原因尚未定位**。不能据此认定产品interval实现错误，也不能假定server CAST只差某种格式。root批准下一版只增加安全、分步、可保留的采集；原parser/validator语义与产品不变，下一次完整工具运行同时定位，不增加独立wide probe。

本次只读报告、源码与原冻结合同；没有build、tests、DB或工具/产品改动。v4写拒绝恢复已经有实际证据，不回滚该修复。

## 1. 已观察事实

- `TP06-supplement-v4-shared-candidate/supplemental.json` 实读SHA256：`4461d128776981f2f1944a0a4abcd2d2858444c368f71431f5568982e69d95a2`。
- `TP06-supplement-v4/independent-runtime-summary.json` 实读SHA256：`a9c5af5ae75bd7f6a855300a9e27230cb1ff3a85287a91f58f19eef47c1c430e`。
- `NATIVE_INTERVAL_zero.write` 的实际输入是TimeSpan、ticks0、显式IntervalDayToSecond/Scale6；同步ExecuteNonQuery返回1，目标公共State保持Open。同步send字段正确标unobservable，不是零发送证明。
- fresh连接已读到唯一行，并取得CType21、width24、packed scale1686（0x696）、mask0、ResultColumn/raw source及ordinary列metadata。`observations=[]`，没有实际cell长度/bytes/hash、typed ticks或任一文本通道结果。
- 顶层是System.InvalidOperationException/user_rejection，assertion_code=null。没有原message、调用子阶段；不能从此区分工具Need、reader生命周期或其它产品异常。
- 原ASYNC拒绝恢复已实际记录Open→Broken、发送2而业务发送0、旧连接InvalidOperation且全部发送0、Dispose及fresh控制完成。当前新的interval失败不否定该修复；leading、最终validator及TLS补充尚未到达。旧报告和main已接受证据保持原样。

## 2. 可定位范围与不能推断的内容

`Observations.cs:108` 在reader首次Read成功后保存fresh_column_metadata；`:109` 只有整个ReadObservation返回才AddRange到stage.observations。ReadObservation在`:67–98`把metadata、cell、raw、IsDBNull、schema、default/provider/typed getter、两种GetString及server fields都暂存在局部变量。因此任何中途异常都会丢掉此前成功的数据，现有空observations不能说明它在第一个getter前失败。

应区分下列可能点，当前不能挑选其中一个当根因：

| 位置/通道 | 当前可知 | 缺少的实际事实 |
| --- | --- | --- |
| RawPayload:47–51 | field侧CType21/width24/mask0/ordinary描述存在 | 实际cache位置、cache格式、cell是否NULL、实际payload长度/hash；metadata width24不证明实际长度24 |
| GetFieldType/provider field type/schema/GetValue/GetProviderSpecificValue | 当前源支持interval默认对象形态 | 该reader实际返回类型/异常及哪个步骤完成 |
| GetFieldValueAsync<TimeSpan> | 已批准整数转换合同存在 | 本次ticks0是否真的返回；局部typedError也尚未被发布 |
| value GetString(0)+SafeNativeText | 对合法DAY9/SECOND6零fields，GetDTString源码输出规范INTERVAL文字，属于现有regex允许形状 | 本次raw尚未证为零fields，且实际返回文字未采集，不能据源码expected声称已观察该文字 |
| server CAST GetString(1)+SafeNativeText | Projection确实为该owned列的CAST(VARCHAR(100)) | 实际server文字、空白/符号/精度形状和原guard是否拒绝 |
| server NULL比较/Fields/最终schema采集 | 均在同一ReadObservation路径 | 实际getter、null receipt和失败子步骤 |

`DmGetValue.GetINTERVALDT`对CType21进入byte构造，不走字符串literal解析器；`DmIntervalDT.GetDTString:299–328`在合法zero/DAY9/SECOND6状态下可静态推导规范格式。该事实只能缩小解释，不能排除实际payload/状态/其它guard，也不能猜测服务器CAST格式。尚无依据修改产品或扩展文本语法。

## 3. 诊断性v5最小范围

优先只改Observations.cs中的采集及同文件caller：ReadObservation新增**可选partial sink**；已有stage的FreshOnConnection和FreshOfficial传入该stage。当前失败必经FreshOnConnection，无须为此改Cases/G2 SQL来源pin、Readers、Sequential或case/vector/API集合。其它caller未传sink保持行为，不声称它们已经取得新增诊断。

保留最终`observations`的现有完整合同，另在stage建立 `read_attempts`。**在第一个可能失败动作前**将attempt对象加入stage，后续就地追加安全记录；成功也不把diagnostic本身当独立值oracle，失败attempt不可伪成完整observation。记录固定case/stage归属、source W/O、已存在的connection ID、row ID、ordinal和通道；不要存driver/reader对象。

推荐固定步骤序列（每步先attempted，调用后completed或failed）：metadata、cell、raw、temporal IsDBNull、field/provider field type、schema、GetValue、GetProviderSpecificValue、generic getter、value GetString获取、value文本guard、server IsDBNull、server NULL一致性guard、server GetString获取、server文本guard、server fields解析、完整observation返回。具体ID用工具自己的固定枚举/常量；不得从异常message推断步骤。

每个成功步立即保存其**已经实际取得**的有限结果：

- metadata只保原有数值/布尔/来源枚举；cell先存可观察性、真实长度/hash/NULL/格式，在raw width等断言之前留存。
- raw仍只在本任务受控时间列、合法来源/格式、合法长度且不超过24 bytes时输出原有hex；位置/格式/宽度不合法时最多保cell长度/hash及固定code，不把任意cell bytes或完整报文写出。不能因诊断绕开RawPayload原guard。
- getter只保实际CLR类型、现有允许的ticks/offset/Kind/NULL及Safe error；不序列化DmIntervalDT等driver对象，不额外调用其ToString/隐藏转换充当真实getter结果。generic已捕获的6081/null等保持原处理顺序并即时记录，不因诊断扩大吞异常范围。
- value GetString与server CAST GetString分开记录“调用是否返回”和“原文本guard是否通过”，先留安全形状再跑原guard。server-null helper须先记录真实投影IsDBNull结果，再做原一致性检查，不能复制temporal NULL。
- Fields返回null也如实记录解析不可观察，不发明分量。最终严格validator仍决定该值合同，不以diagnostic shape替代fields/packet/ticks。

## 4. 两个文本通道的安全记录

未知文字不是错误message，但仍只允许受控的合成时间内容进入报告。诊断至少记录固定通道/value或server、字符长度、有限字符类别计数/形状及有界hash；区分空白/符号/引号/冒号/小数点/括号，避免下一次仍无法区分返回失败与格式guard失败。

若保留diagnostic安全文字，必须同时满足：本任务自有时间列/投影上下文、严格长度上限，以及仅数字、受限时间分隔符和完整固定INTERVAL/DAY/TO/SECOND关键字等受限词法白名单。此白名单只决定能否安全输出，**不决定格式可被接受**。不符合者不输出原文，仅长度/hash/类别；超出有界处理上限则明确hash未计算，不输出任意前缀/后缀或未知字符代码点。类别形状不得原样回显未知单词。

可沿现有120字符上限或更小诊断上限实现。不得自动Trim、补零、改符号、改大小写后送入原parser；不把“只差空格”之类推测登记为批准格式。原SafeNativeText和独立validator的语义规则完全不变。若新完整运行仍在同一guard失败，这正是保留的真实失败，只是此次应有充分证据供root下一步定点裁决。

## 5. 安全断言与失败留痕

将本路径工具自己的raw位置/metadata/width、value文本、server文本、server-null一致性等guard连接到固定ToolAssertionCode成员，使同一个InvalidOperation能有明确工具code。仅工具自己构造的白名单枚举可写assertion_code；外部异常继续仅Safe(Type/Number/既有FailureInfo)，禁止复制任意Exception.Message、完整stack、SQL、连接串、环境或认证字节。

partial sink不能吞掉失败：记录failed_step/Safe error后保留原异常退出；完成步骤和数据不回滚，attempt.completed保持false。原finally和独立cleanup仍必须执行。不要把预初始化的write成功状态或已经持久化的row当作读验收成功。

## 6. 冻结与下一门

最小合同测试调用实际新增采集/步骤helper，验证：前序数据在raw-width guard失败后保留；generic getter成功但value guard失败可定位；value成功而server guard失败可定位；getter自身异常与guard异常分开；NULL投影调用仍真实；未知/超长/含非白名单字符文本只输出有界安全形状；固定code不会复制外部message；partial failure不能产生完整observation或candidate接受。旧140项validator与36项C#合同不删减、不改预期；若新增实际C#检查，相应runner/validator只同步精确ID列表，不放宽数据门。

High仅在root授权文件范围做诊断性修复，形成新source/tool freeze，再Astra静态复核、Low离线及新的完整运行。若测试/validator须识别read_attempts，只可要求其关联与未完成状态，不能因诊断存在绕过原断言。无需新server SQL、宽探针或额外独立DB诊断窗口。

当前根因仍unresolved；产品、parser、case/vector、v4恢复、同包身份、leading未批准号pending、ownership/cleanup及所有旧证据保持。新报告出现足够事实前，不批准任何文本兼容例外或产品修复。TP06仍待完整Supplement完成。
