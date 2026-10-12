using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using W.Dm;

internal static partial class Program
{
    static string Format(string column) => column switch { "A" => "YYYY-MM-DD", "T" => "HH24:MI:SS.FF9", _ when column.StartsWith('Z') => "YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM", _ => "YYYY-MM-DD HH24:MI:SS.FF9" };
    static string Projection(string column) => column is "I" or "J" ? $"{column},CAST({column} AS VARCHAR(100))" : $"{column},TO_CHAR({column},'{Format(column)}')";
    static string TargetType(string column) => column switch { "A" => "System.DateOnly", "T" => "System.TimeOnly", "I" or "J" => "System.TimeSpan", _ when column.StartsWith('Z') => "System.DateTimeOffset", _ => "System.DateTime" };
    static async Task<object?> Generic(DbDataReader r,int ordinal,string column,bool async)
    {
        return column switch {
            "A" => async ? await r.GetFieldValueAsync<DateOnly>(ordinal,Token) : r.GetFieldValue<DateOnly>(ordinal),
            "T" => async ? await r.GetFieldValueAsync<TimeOnly>(ordinal,Token) : r.GetFieldValue<TimeOnly>(ordinal),
            "I" or "J" => async ? await r.GetFieldValueAsync<TimeSpan>(ordinal,Token) : r.GetFieldValue<TimeSpan>(ordinal),
            _ when column.StartsWith('Z') => async ? await r.GetFieldValueAsync<DateTimeOffset>(ordinal,Token) : r.GetFieldValue<DateTimeOffset>(ordinal),
            _ => async ? await r.GetFieldValueAsync<DateTime>(ordinal,Token) : r.GetFieldValue<DateTime>(ordinal)
        };
    }
    static string SafeNativeText(string text,string column)
    {
        if(column is not ("I" or "J")) return SafeTemporalText(text);
        Need(text.Length<=120 && Regex.IsMatch(text,@"^(?:INTERVAL ')?[+-]?[0-9]{1,9} [0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?(?:' DAY\([1-9]\) TO SECOND\([0-6]\))?$",RegexOptions.CultureInvariant),"unsafe_interval_text"); return text;
    }
    static object? Fields(string text,bool interval,int packed=0)
    {
        if(interval)return ParseIntervalText(text,packed,true);
        var m=Regex.Match(text.Trim(),@"^(?:(\d{4})-(\d{2})-(\d{2})(?: )?)?(?:(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,9}))?)?(?: ([+-])(\d{2}):(\d{2}))?$",RegexOptions.CultureInvariant);
        if(!m.Success)return null;
        int N(int i)=>m.Groups[i].Success?int.Parse(m.Groups[i].Value,CultureInfo.InvariantCulture):0;
        return new { year=m.Groups[1].Success?(int?)N(1):null,month=m.Groups[2].Success?(int?)N(2):null,day=m.Groups[3].Success?(int?)N(3):null,hour=N(4),minute=N(5),second=N(6),nanoseconds=int.Parse(m.Groups[7].Value.PadRight(9,'0'),CultureInfo.InvariantCulture),offset_minutes=m.Groups[8].Success?(int?)((m.Groups[8].Value=="-"?-1:1)*(N(9)*60+N(10))):null };
    }
    static object? RawPayload(DbDataReader reader,int ordinal,Metadata metadata)
    {
        object? cache=ReadField(reader,"m_RsCache"); var rows=cache is null?null:ReadField(cache,"datas") as byte[][][];
        object? pos=cache is null?null:ReadField(cache,"datasOffset"); object? bdta=cache is null?null:ReadField(cache,"isRsBdta");
        ToolNeed(rows is not null && pos is int index && index>=0 && index<rows.Length && ordinal+1<rows[index].Length,ToolAssertionCode.ReadRawPositionInvalid);
        return GuardRawValue(bdta is false,metadata,rows![(int)pos!][ordinal+1]);
    }
    static object? GuardRawValue(bool ordinaryVerified,Metadata metadata,byte[]? bytes)
    {
        ToolNeed(ordinaryVerified && metadata is { Available:true,Mask:0,IsBdta:false,MetadataRole:"result_column",ResultFormat:"ordinary_packed" },ToolAssertionCode.ReadRawMetadataInvalid);
        if(bytes is null)return null;
        int width=metadata.CType switch {14=>3,15=>5,16=>8,23=>10,26=>9,27=>11,21=>24,_=>0};
        ToolNeed(width>0 && metadata.Width==width && bytes.Length==width,ToolAssertionCode.ReadRawWidthInvalid);
        return new { hex=Convert.ToHexString(bytes),c_type=metadata.CType,scale=metadata.SemanticScale,width=metadata.Width,mask=metadata.Mask,source="ordinary_result_cache_before_temporal_projection",capture_stage="before_any_temporal_getter",payload_sha256=Hash(bytes),is_bdta=false };
    }
    enum ReadStepId { Metadata,Cell,Raw,TemporalIsDBNull,FieldType,ProviderFieldType,Schema,GetterMismatch,GetValue,GetProviderSpecificValue,GenericGetter,OfficialTypedGetter,ValueStringGet,ValueTextGuard,ServerIsDBNull,ServerNullGuard,ServerStringGet,ServerTextGuard,ServerFields,SchemaScale,CachedRepeat,CompleteObservation }
    sealed class ReadTrace
    {
        internal Dictionary<string,object?> Attempt{get;} internal List<Dictionary<string,object?>> Steps{get;}=[];
        internal ReadTrace(Dictionary<string,object?>? sink,string source,string connection,int row,int ordinal,string column)
        {
            Attempt=new(){["case_id"]=Stage,["stage_id"]=sink?.GetValueOrDefault("stage_id"),["source"]=source,["connection_id"]=connection,["row_id"]=row,["ordinal"]=ordinal,["column"]=column,["completed"]=false,["steps"]=Steps};
            if(sink is not null){if(!sink.TryGetValue("read_attempts",out var existing))sink["read_attempts"]=existing=new List<object>();((List<object>)existing!).Add(Attempt);}
        }
        Dictionary<string,object?> Begin(ReadStepId id){var value=new Dictionary<string,object?>{["step_id"]=id.ToString(),["status"]="attempted"};Steps.Add(value);Attempt["last_attempted_step"]=id.ToString();return value;}
        internal T Step<T>(ReadStepId id,Func<T> action,Func<T,object?>? safe=null)
        {
            var entry=Begin(id);try{T result=action();entry["call_returned"]=true;if(safe is not null)entry["result"]=safe(result);entry["status"]="completed";Attempt["last_completed_step"]=id.ToString();return result;}catch(Exception e){entry["status"]="failed";if(!entry.ContainsKey("call_returned"))entry["call_returned"]=false;entry["error"]=Safe(e);throw;}
        }
        internal async Task<T> StepAsync<T>(ReadStepId id,Func<Task<T>> action,Func<T,object?>? safe=null)
        {
            var entry=Begin(id);try{T result=await action();entry["call_returned"]=true;if(safe is not null)entry["result"]=safe(result);entry["status"]="completed";Attempt["last_completed_step"]=id.ToString();return result;}catch(Exception e){entry["status"]="failed";if(!entry.ContainsKey("call_returned"))entry["call_returned"]=false;entry["error"]=Safe(e);throw;}
        }
        internal void Handled(ReadStepId id){Steps.Last(s=>s["step_id"] is string name&&name==id.ToString())["handled_error"]=true;}
        internal void Failed(Exception error){Attempt["failed_step"]=Attempt.GetValueOrDefault("last_attempted_step");Attempt["error"]=Safe(error);}
    }
    static Dictionary<string,object?> DiagnosticText(string? text)
    {
        var result=new Dictionary<string,object?>{["returned_null"]=text is null,["character_length"]=text?.Length,["processing_limit"]=120,["hash_computed"]=false,["utf8_sha256"]=null,["category_counts"]=null,["diagnostic_text"]=null};
        if(text is null){result["shape_status"]="returned_null";return result;}
        if(text.Length>120){result["shape_status"]="processing_limit_exceeded";return result;}
        var counts=new Dictionary<string,int>{{"digits",0},{"whitespace",0},{"signs",0},{"quotes",0},{"colons",0},{"dots",0},{"parentheses",0},{"slashes",0},{"letters",0},{"other",0}};
        foreach(char c in text){string key=c is >= '0' and <= '9'?"digits":c is ' ' or '\t' or '\r' or '\n'?"whitespace":c is '+' or '-'?"signs":c=='\''?"quotes":c==':'?"colons":c=='.'?"dots":c is '(' or ')'?"parentheses":c=='/'?"slashes":c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'?"letters":"other";counts[key]++;}
        result["category_counts"]=counts;result["hash_computed"]=true;result["utf8_sha256"]=Hash(System.Text.Encoding.UTF8.GetBytes(text));result["shape_status"]="bounded_shape";
        bool safe=Regex.IsMatch(text,@"^(?:[0-9+\- :./()'\t\r\n]|\b(?:INTERVAL|DAY|TO|SECOND)\b)*$",RegexOptions.CultureInvariant);
        if(safe)result["diagnostic_text"]=text;return result;
    }
    sealed record IntervalTextFields(int Days,int Hours,int Minutes,int Seconds,int Microseconds,bool Negative);
    static IntervalTextFields ParseIntervalText(string text,int packed,bool server)
    {
        int leading=packed==1574?2:packed==1686?9:0;Need(leading!=0,"interval_packed_declaration");
        var match=Regex.Match(text,@"\AINTERVAL (?<outer>-?)'(?<sign>[+-]?)(?<days>[0-9]{1,9}) (?<hours>[0-9]{1,2}):(?<minutes>[0-9]{1,2}):(?<seconds>[0-9]{1,2})\.(?<micro>[0-9]{6})' DAY\((?<leading>[29])\) TO SECOND\(6\)\z",RegexOptions.CultureInvariant);
        Need(match.Success,"interval_full_text");
        string D(string key)=>match.Groups[key].Value;int N(string key)=>int.Parse(D(key),CultureInfo.InvariantCulture);
        Need(D("leading")==leading.ToString(CultureInfo.InvariantCulture)&&D("days").Length<=leading,"interval_leading_match");
        Need(D("outer")==""||D("sign")=="","interval_single_sign");
        if(!server)Need(D("days").Length==leading&&D("hours").Length==2&&D("minutes").Length==2&&D("seconds").Length==2&&D("sign")!="+"&&D("outer")=="","interval_value_canonical_width");
        int days=N("days"),hours=N("hours"),minutes=N("minutes"),seconds=N("seconds"),micro=N("micro");int limit=leading==2?100:1000000000;
        Need(days<limit&&hours<=23&&minutes<=59&&seconds<=59&&micro<=999999,"interval_component_domain");bool negative=D("outer")=="-"||D("sign")=="-";
        Need(!negative||days!=0||hours!=0||minutes!=0||seconds!=0||micro!=0,"interval_negative_zero");
        var result=new IntervalTextFields(days,hours,minutes,seconds,micro,negative);
        if(!server)Need(text==CanonicalIntervalText(result,packed),"interval_value_canonical_exact");return result;
    }
    static string CanonicalIntervalText(IntervalTextFields fields,int packed)
    {
        int leading=packed==1574?2:packed==1686?9:0;Need(leading!=0,"interval_canonical_declaration");
        return "INTERVAL '"+(fields.Negative?"-":"")+fields.Days.ToString("D"+leading,CultureInfo.InvariantCulture)+" "+fields.Hours.ToString("D2",CultureInfo.InvariantCulture)+":"+fields.Minutes.ToString("D2",CultureInfo.InvariantCulture)+":"+fields.Seconds.ToString("D2",CultureInfo.InvariantCulture)+"."+fields.Microseconds.ToString("D6",CultureInfo.InvariantCulture)+"' DAY("+leading.ToString(CultureInfo.InvariantCulture)+") TO SECOND(6)";
    }
    static string GuardReadText(string text,string column,bool server,Metadata? metadata=null)
    {
        try
        {
            if(column is not ("I" or "J"))return SafeNativeText(text,column);
            Need(metadata is {Available:true,CType:21,Width:24,Mask:0,TypeFlag:0,RawCType:21,RawPrecision:24,RawMaskBefore:0,RawDerivedMask:0,RawSource:"ResultColumn",MetadataRole:"result_column",IsBdta:false},"interval_text_actual_metadata");
            int packed=metadata!.SemanticScale??-1;Need(metadata.RawScale==packed,"interval_text_raw_declaration");_=ParseIntervalText(text,packed,server);return text;
        }
        catch(InvalidOperationException){throw new ToolAssertionException(server?ToolAssertionCode.ReadServerTextGuardFailed:ToolAssertionCode.ReadValueTextGuardFailed);}
    }
    sealed record ServerNullReceipt(bool ActualGetterInvoked,string Api,int Ordinal,bool IsNull,string Source);
    sealed record ServerProjectionObservation(bool IsNull,string? Text,ServerNullReceipt Receipt);
    // Live and offline use this exact routing/check. The server projection is never inferred from the temporal cell.
    static async Task<ServerProjectionObservation> ObserveServerProjection(bool temporalNull,int ordinal,bool useAsync,
        Func<bool> isNull,Func<Task<bool>> isNullAsync,Func<string> text,ReadTrace? trace=null)
    {
        bool serverNull=trace is null?(useAsync?await isNullAsync():isNull()):useAsync?await trace.StepAsync(ReadStepId.ServerIsDBNull,isNullAsync,v=>v):trace.Step(ReadStepId.ServerIsDBNull,isNull,v=>v);
        var receipt=new ServerNullReceipt(true,useAsync?"IsDBNullAsync":"IsDBNull",ordinal,serverNull,"server_projection");
        if(trace is not null)trace.Attempt["server_null_receipt"]=receipt;
        if(trace is null)ToolNeed(serverNull==temporalNull,ToolAssertionCode.ReadServerNullConflict);else trace.Step(ReadStepId.ServerNullGuard,()=>{ToolNeed(serverNull==temporalNull,ToolAssertionCode.ReadServerNullConflict);return true;},v=>v);
        return new(serverNull,serverNull?null:trace is null?text():trace.Step(ReadStepId.ServerStringGet,text,v=>DiagnosticText(v)),receipt);
    }
    static async Task<List<object>> ReadObservation(DbConnection c,DbDataReader r,string column,int row,bool async,string source="W",bool cachedRepeat=false,int ordinal=0,bool mismatchControl=false,Dictionary<string,object?>? partialSink=null)
    {
        var trace=new ReadTrace(partialSink,source,ConnectionId(c),row,ordinal,column);
        try
        {
            var metadata=trace.Step(ReadStepId.Metadata,()=>Columns(r)[ordinal],v=>v);object cell=trace.Step(ReadStepId.Cell,()=>Cell(r,ordinal),v=>v);object? raw=trace.Step(ReadStepId.Raw,()=>RawPayload(r,ordinal,metadata),v=>v);
            bool nil=async?await trace.StepAsync(ReadStepId.TemporalIsDBNull,()=>r.IsDBNullAsync(ordinal,Token),v=>v):trace.Step(ReadStepId.TemporalIsDBNull,()=>r.IsDBNull(ordinal),v=>v);
            string fieldType=trace.Step(ReadStepId.FieldType,()=>r.GetFieldType(ordinal).FullName!,v=>v);string providerType=trace.Step(ReadStepId.ProviderFieldType,()=>r.GetProviderSpecificFieldType(ordinal).FullName!,v=>v);
            string? schemaType=trace.Step(ReadStepId.Schema,()=>{var schema=r.GetSchemaTable();return schema?.Rows[ordinal]["DataType"] is Type t?t.FullName:null;},v=>v);
            bool mismatchObserved=false;if(mismatchControl){try{_=await trace.StepAsync(ReadStepId.GetterMismatch,()=>r.GetFieldValueAsync<int>(ordinal,Token),v=>new{clr_type=typeof(int).FullName});}catch(InvalidCastException){mismatchObserved=true;trace.Handled(ReadStepId.GetterMismatch);}Need(mismatchObserved,"reader_mismatch_expected");}
            object? value=trace.Step(ReadStepId.GetValue,()=>r.GetValue(ordinal),v=>Observe(()=>v));object? provider=trace.Step(ReadStepId.GetProviderSpecificValue,()=>r.GetProviderSpecificValue(ordinal),v=>Observe(()=>v));
            object? typed=null;ErrorInfo? typedError=null;ErrorInfo? officialGenericError=null;bool nullHandled=false;
            try
            {
                if(source=="O")
                {
                    try{_=await trace.StepAsync(ReadStepId.GenericGetter,()=>Generic(r,ordinal,column,false),v=>Observe(()=>v));}catch(Exception e){CheckInfra(e);officialGenericError=Safe(e);trace.Handled(ReadStepId.GenericGetter);}
                    typed=trace.Step(ReadStepId.OfficialTypedGetter,()=>column.StartsWith('Z')?Invoke(r,"GetDateTimeOffset",ordinal):r.GetDateTime(ordinal),v=>Observe(()=>v));
                }
                else typed=await trace.StepAsync(ReadStepId.GenericGetter,()=>Generic(r,ordinal,column,async),v=>Observe(()=>v));
            }
            catch(Exception e)
            {
                bool RealInfra(Exception x)=>x is TimeoutException or OperationCanceledException or System.IO.IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException||x.InnerException is not null&&RealInfra(x.InnerException);
                bool knownNull=nil&&raw is null&&e is DmException dm&&dm.Number==6081&&!RealInfra(e);
                if(!knownNull)CheckInfra(e);else{nullHandled=true;trace.Handled(ReadStepId.GenericGetter);}typedError=Safe(e);trace.Attempt["typed_error"]=typedError;
            }
            string? getterText=null;
            if(!nil){string returned=trace.Step(ReadStepId.ValueStringGet,()=>r.GetString(ordinal),v=>DiagnosticText(v));getterText=trace.Step(ReadStepId.ValueTextGuard,()=>GuardReadText(returned,column,false,metadata),v=>new{accepted=true});}
            var projection=await ObserveServerProjection(nil,ordinal+1,async,()=>r.IsDBNull(ordinal+1),()=>r.IsDBNullAsync(ordinal+1,Token),()=>r.GetString(ordinal+1),trace);
            string? text=projection.Text;if(text is not null)text=trace.Step(ReadStepId.ServerTextGuard,()=>GuardReadText(projection.Text!,column,true,metadata),v=>new{accepted=true});
            object? fields=trace.Step(ReadStepId.ServerFields,()=>text is null?null:Fields(text,column is "I" or "J",metadata.SemanticScale??0),v=>new{observable=v is not null,fields=v});var actual=Observe(()=>typed);
            int? schemaScale=trace.Step(ReadStepId.SchemaScale,()=>SchemaScales(r)[ordinal],v=>v);
            var receipt=new Dictionary<string,object?>{["source"]=source,["connection_id"]=ConnectionId(c),["row_id"]=row,["is_null"]=nil,["fields"]=fields,["ticks"]=actual.Ticks,["offset_minutes"]=actual.OffsetMinutes,["kind"]=actual.Kind,["raw"]=raw,["metadata"]=metadata,["schema_scale"]=schemaScale,["cell"]=cell,["server_text"]=text,["server_null_receipt"]=projection.Receipt,["default_type"]=value?.GetType().FullName,["provider_type"]=provider?.GetType().FullName,["field_type"]=fieldType,["schema_type"]=schemaType,["typed_type"]=typed?.GetType().FullName??TargetType(column),["typed_api"]=source=="O"?(column.StartsWith('Z')?"GetDateTimeOffset":"GetDateTime"):async?"GetFieldValueAsync<T>":"GetFieldValue<T>",["typed_outcome"]=typedError is null?"returned":"error",["generic_error"]=officialGenericError,["typed_error"]=typedError,["typed_null_error"]=nil?typedError?.Type:null,["typed_null_receipt"]=nullHandled?new{actual_getter_invoked=true,has_real_infra_inner=false,cache_is_null=true,getter_api=async?"GetFieldValueAsync<T>":"GetFieldValue<T>"}:null,["get_string"]=getterText,["getter_mismatch_error"]=mismatchObserved?"System.InvalidCastException":null,["next_column_read"]=mismatchObserved&&text is not null,["default_ticks"]=Observe(()=>value).Ticks,["get_value"]=Observe(()=>value),["provider_specific"]=Observe(()=>provider)};
            if(cachedRepeat){var repeated=await trace.StepAsync(ReadStepId.CachedRepeat,()=>Generic(r,ordinal,column,async),v=>Observe(()=>v));receipt["cached_repeat"]=Observe(()=>repeated);}
            var server=new Dictionary<string,object?>{["source"]="server",["connection_id"]=ConnectionId(c),["row_id"]=row,["is_null"]=projection.IsNull,["fields"]=fields,["server_text"]=text,["server_null_receipt"]=projection.Receipt,["ticks"]=null,["raw"]=null};
            trace.Step(ReadStepId.CompleteObservation,()=>{trace.Attempt["completed"]=true;return true;},v=>v);return [receipt,server];
        }
        catch(Exception e){trace.Failed(e);throw;}
    }
    static async Task FreshOnConnection(Dictionary<string,object?> stage,string column,int row,DbConnection c,bool readAsync=true)
    {
        var observations=(List<object>)stage["observations"]!;stage["fresh_connection_id"]=ConnectionId(c);
        stage["fresh_rows"]=Convert.ToInt64(await ScalarAsync(c,$"SELECT COUNT(*) FROM {Table} WHERE ID={row}"),CultureInfo.InvariantCulture);
        if((long)stage["fresh_rows"]! == 1)
        {
            await using var cmd=Command(c,$"SELECT {Projection(column)} FROM {Table} WHERE ID={row}");
            await using var reader=await cmd.ExecuteReaderAsync(Token);Need(await reader.ReadAsync(Token),"fresh_row");stage["fresh_column_metadata"]=Columns(reader).FirstOrDefault();
            observations.AddRange(await ReadObservation(c,reader,column,row,readAsync,partialSink:stage));Need(!await reader.ReadAsync(Token),"fresh_unique_row");
        }
    }
    static async Task FreshOfficial(Dictionary<string,object?> stage,string column,int row)
    {
        if(Profile=="shared" && (long)stage["fresh_rows"]! == 1 && column is not ("I" or "J" or "A" or "T"))
        {
            using var c=Open("O");using var cmd=Command(c,$"SELECT {Projection(column)} FROM {Table} WHERE ID={row}");using var r=cmd.ExecuteReader();Need(r.Read(),"official_fresh_row");
            ((List<object>)stage["observations"]!).AddRange((await ReadObservation(c,r,column,row,false,"O",partialSink:stage)).OfType<Dictionary<string,object?>>().Where(o=>o["source"] is "O"));Need(!r.Read(),"official_unique_row");
        }
    }
    static async Task Fresh(Dictionary<string,object?> stage,string column,int row,bool readAsync=true)
    {
        await using(var c=await OpenAsync())await FreshOnConnection(stage,column,row,c,readAsync);
        await FreshOfficial(stage,column,row);
    }
    enum ToolAssertionCode { TargetBeforeNotOpen,TargetAfterInvalid,LocalTargetNotAsync,LocalTargetBusinessSent,RecoveryStateInvalid,OldProbeUnexpectedSuccess,OldProbeWrongError,OldProbeSent,OldProbeStateChanged,SameProbeNotOne,FreshIdentityInvalid,FreshIdReused,FreshProbeNotOne,DisposedStateInvalid,ReadRawPositionInvalid,ReadRawMetadataInvalid,ReadRawWidthInvalid,ReadValueTextGuardFailed,ReadServerTextGuardFailed,ReadServerNullConflict }
    sealed class ToolAssertionException(ToolAssertionCode code):InvalidOperationException("tool_assertion") { internal ToolAssertionCode Code{get;}=code; }
    static void ToolNeed(bool condition,ToolAssertionCode code){if(!condition)throw new ToolAssertionException(code);}
    static int BeginTarget(Dictionary<string,object?> stage,DbConnection c,DbCommand cmd,string api)
    {
        stage["target_receipt"]=new Dictionary<string,object?>{["attempted"]=true,["status"]="attempted",["api"]=api,["connection_id"]=ConnectionId(c),["parameter_count"]=cmd.Parameters.Count,["state_before"]=c.State.ToString()};
        return Observation!.SentFrames.Count;
    }
    static void EndTarget(Dictionary<string,object?> stage,DbConnection c,int start,bool returned)
    {
        var receipt=(Dictionary<string,object?>)stage["target_receipt"]!;var sent=Observation!.SentFrames.Skip(start).ToArray();
        receipt["send_observable"]=((string)receipt["api"]!).EndsWith("Async",StringComparison.Ordinal);receipt["state_after"]=c.State.ToString();receipt["status"]=returned?"completed":"not_returned";receipt["execute_returned"]=returned;receipt["send_source"]="physical_channel_frame_sent";receipt["sent_frames"]=sent;receipt["all_send_count"]=sent.Length;
        if(receipt["send_observable"] is false){receipt["send_source"]="unobservable_sync_path";receipt["all_send_count"]=null;receipt["sent_frames"]=null;receipt["allocation_count"]=null;receipt["prepare_or_direct_count"]=null;receipt["business_send_count"]=null;return;}
        receipt["allocation_count"]=sent.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.StatementAllocation);receipt["prepare_or_direct_count"]=sent.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.PrepareOrDirectSql);receipt["business_send_count"]=sent.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.PreparedExecute);
    }
    static bool RequiresNewIndependentConnection(Dictionary<string,object?>? previous,ConnectionState current,string id)
    {
        if(previous is null){ToolNeed(current==ConnectionState.Open,ToolAssertionCode.RecoveryStateInvalid);return false;}
        ToolNeed(Equals(previous["operation_connection_id"],id),ToolAssertionCode.FreshIdReused);
        if(previous.GetValueOrDefault("recovery") is Dictionary<string,object?> recovery&&recovery.GetValueOrDefault("path") is "retired_connection")
        {ToolNeed(current==ConnectionState.Closed,ToolAssertionCode.DisposedStateInvalid);return true;}
        ToolNeed(current==ConnectionState.Open,ToolAssertionCode.RecoveryStateInvalid);return false;
    }
    sealed record RecoveryIdentity(string ConnectionId,string User,string Schema,string State);
    interface IRecoveryAdapter
    {
        string OldId{get;} ConnectionState State{get;} int SentCount{get;} SentFrame[] SentSince(int index);
        Task<object?> ProbeOld();Task DisposeCommand();Task DisposeConnection();Task<RecoveryIdentity> OpenFresh();Task<object?> ProbeFresh();Task VerifyFresh();Task DisposeFresh();
    }
    static async Task Recover(Dictionary<string,object?> stage,IRecoveryAdapter adapter,bool local)
    {
        var steps=new List<Dictionary<string,object?>>();var recovery=new Dictionary<string,object?>{["attempted"]=true,["completed"]=false,["scope"]=local?"local_write_rejection":"verified_server_response",["operation_connection_id"]=adapter.OldId,["state_before"]=adapter.State.ToString(),["steps"]=steps};stage["recovery"]=recovery;
        async Task<T> Step<T>(string id,Func<Dictionary<string,object?>,Task<T>> action)
        {
            var entry=new Dictionary<string,object?>{["step_id"]=id,["status"]="attempted"};steps.Add(entry);recovery["last_attempted_step"]=id;
            try{var value=await action(entry);entry["status"]="completed";recovery["last_completed_step"]=id;return value;}
            catch(Exception e){entry["status"]="failed";entry["error"]=Safe(e);recovery["failed_step"]=id;throw;}
        }
        try
        {
        bool retired=adapter.State is ConnectionState.Broken or ConnectionState.Closed;
        if(local)
        {
            var target=(Dictionary<string,object?>)stage["target_receipt"]!;
            ToolNeed(target["state_before"] is "Open",ToolAssertionCode.TargetBeforeNotOpen);ToolNeed(((string)target["api"]!).EndsWith("Async",StringComparison.Ordinal),ToolAssertionCode.LocalTargetNotAsync);ToolNeed(target["business_send_count"] is 0,ToolAssertionCode.LocalTargetBusinessSent);
            ToolNeed(target["state_after"] is "Open" or "Broken" or "Closed",ToolAssertionCode.TargetAfterInvalid);ToolNeed(Equals(target["state_after"],adapter.State.ToString()),ToolAssertionCode.RecoveryStateInvalid);ToolNeed(target["execute_returned"] is false && target["parameter_count"] is 1,ToolAssertionCode.TargetAfterInvalid);
            ToolNeed(stage.GetValueOrDefault("error") is ErrorInfo {FailureClass:"user_rejection",Type:"System.OverflowException" or "System.NotSupportedException"},ToolAssertionCode.TargetAfterInvalid);
        }
        ToolNeed(adapter.State==ConnectionState.Open || local&&retired,ToolAssertionCode.RecoveryStateInvalid);recovery["path"]=retired?"retired_connection":"same_open";
        await Step("old_connection_probe",async entry=>
        {
            entry["connection_id"]=adapter.OldId;entry["api"]="ExecuteScalarAsync";entry["state_before"]=adapter.State.ToString();int start=adapter.SentCount;bool probeReturned=false;
            try
            {
                object? value=await adapter.ProbeOld();probeReturned=true;entry["execute_returned"]=true;entry["scalar"]=Convert.ToInt32(value,CultureInfo.InvariantCulture);
                ToolNeed(!retired,ToolAssertionCode.OldProbeUnexpectedSuccess);ToolNeed(entry["scalar"] is 1,ToolAssertionCode.SameProbeNotOne);ToolNeed(adapter.State==ConnectionState.Open,ToolAssertionCode.OldProbeStateChanged);
            }
            catch(Exception e)
            {
                if(probeReturned)throw;entry["error"]=Safe(e);entry["probe_error"]=Safe(e);entry["execute_returned"]=false;
                if(!retired)throw;ToolNeed(e.GetType()==typeof(InvalidOperationException)&&Safe(e).FailureClass=="user_rejection",ToolAssertionCode.OldProbeWrongError);ToolNeed(adapter.State is ConnectionState.Broken or ConnectionState.Closed,ToolAssertionCode.OldProbeStateChanged);
            }
            finally{entry["state_after"]=adapter.State.ToString();entry["send_source"]="physical_channel_frame_sent";entry["sent_frames"]=adapter.SentSince(start);entry["all_send_count"]=adapter.SentCount-start;}
            if(retired){ToolNeed(entry["all_send_count"] is 0,ToolAssertionCode.OldProbeSent);ToolNeed(Equals(entry["state_before"],entry["state_after"]),ToolAssertionCode.OldProbeStateChanged);}else{recovery["same_connection_id"]=adapter.OldId;recovery["scalar"]=1;recovery["api"]="ExecuteScalarAsync";}
            return true;
        });
        if(retired)
        {
            await Step("old_command_dispose",async entry=>{entry["connection_id"]=adapter.OldId;entry["api"]="DisposeAsync";entry["state_before"]=adapter.State.ToString();int start=adapter.SentCount;await adapter.DisposeCommand();entry["sent_frames"]=adapter.SentSince(start);entry["all_send_count"]=adapter.SentCount-start;entry["send_source"]="physical_channel_frame_sent";entry["state_after"]=adapter.State.ToString();return true;});
            await Step("old_connection_dispose",async entry=>{entry["connection_id"]=adapter.OldId;entry["api"]="DisposeAsync";entry["state_before"]=adapter.State.ToString();int start=adapter.SentCount;await adapter.DisposeConnection();entry["sent_frames"]=adapter.SentSince(start);entry["all_send_count"]=adapter.SentCount-start;entry["send_source"]="physical_channel_frame_sent";entry["state_after"]=adapter.State.ToString();ToolNeed(adapter.State==ConnectionState.Closed,ToolAssertionCode.DisposedStateInvalid);return true;});
        }
        var fresh=await Step("fresh_connection_open",async entry=>{entry["api"]="OpenAsync";var identity=await adapter.OpenFresh();entry["connection_id"]=identity.ConnectionId;entry["identity_user"]=identity.User;entry["identity_schema"]=identity.Schema;entry["state_after"]=identity.State;ToolNeed(identity.ConnectionId!=adapter.OldId,ToolAssertionCode.FreshIdReused);ToolNeed(identity.User==User&&identity.Schema==User&&identity.State=="Open",ToolAssertionCode.FreshIdentityInvalid);return identity;});recovery["fresh_connection_id"]=fresh.ConnectionId;
        try
        {
            await Step("fresh_connection_probe",async entry=>{entry["connection_id"]=fresh.ConnectionId;entry["api"]="ExecuteScalarAsync";entry["scalar"]=Convert.ToInt32(await adapter.ProbeFresh(),CultureInfo.InvariantCulture);ToolNeed(entry["scalar"] is 1,ToolAssertionCode.FreshProbeNotOne);recovery["fresh_scalar"]=1;return true;});
            await Step("fresh_row_verify",async entry=>{entry["connection_id"]=fresh.ConnectionId;entry["api"]="ExecuteReaderAsync";await adapter.VerifyFresh();return true;});
        }
        finally{await Step("fresh_connection_dispose",async entry=>{entry["connection_id"]=fresh.ConnectionId;entry["api"]="DisposeAsync";await adapter.DisposeFresh();return true;});}
        recovery["completed"]=true;
        }
        finally{recovery["state_after"]=adapter.State.ToString();}
    }
    sealed class LiveRecoveryAdapter(DmConnection old,DbCommand command,Func<DmConnection,Task> verify):IRecoveryAdapter
    {
        DmConnection? fresh;public string OldId=>ConnectionId(old);public ConnectionState State=>old.State;public int SentCount=>Observation!.SentFrames.Count;public SentFrame[] SentSince(int index)=>Observation!.SentFrames.Skip(index).ToArray();
        public Task<object?> ProbeOld()=>ScalarAsync(old,"SELECT 1 FROM DUAL");public Task DisposeCommand()=>command.DisposeAsync().AsTask();public Task DisposeConnection()=>old.DisposeAsync().AsTask();
        public async Task<RecoveryIdentity> OpenFresh(){fresh=await OpenAsync();var profile=ProfileInfo(fresh);return new(ConnectionId(fresh),profile.ServerUser,profile.ServerSchema,fresh.State.ToString());}
        public Task<object?> ProbeFresh()=>ScalarAsync(fresh!,"SELECT 1 FROM DUAL");public Task VerifyFresh()=>verify(fresh!);public Task DisposeFresh()=>fresh!.DisposeAsync().AsTask();
    }
    static async Task Recovery(Dictionary<string,object?> stage,DmConnection c,DbCommand cmd,Func<DmConnection,Task> verify,bool local=true)=>await Recover(stage,new LiveRecoveryAdapter(c,cmd,verify),local);
}
