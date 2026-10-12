using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using W.Dm;

internal static partial class Program
{
    static JsonElement CaseManifest;
    static readonly Dictionary<string,int> TransactionRows=new(StringComparer.Ordinal);
    static IEnumerable<JsonElement> Specs=>CaseManifest.GetProperty("profiles").GetProperty(Profile).EnumerateArray();
    static object Vector(string name)
    {
        var v=CaseManifest.GetProperty("vectors").GetProperty(name); long ticks=v.GetProperty("ticks").GetInt64();
        return v.GetProperty("kind").GetString() switch { "date"=>DateOnly.FromDateTime(new DateTime(ticks)),"time"=>new TimeOnly(ticks),"interval"=>TimeSpan.FromTicks(ticks),"tz"=>new DateTimeOffset(new DateTime(ticks,DateTimeKind.Unspecified),TimeSpan.FromMinutes(v.GetProperty("offset_minutes").GetInt32())),_=>new DateTime(ticks,DateTimeKind.Unspecified) };
    }
    static string Column(JsonElement s)=>s.GetProperty("c_type").GetInt32() switch {14=>"A",15=>"T",21=>s.GetProperty("scale").GetInt32()==1574?"J":"I",23=>"Z6",27=>"Z7",26=>"D7",_=>"D6"};
    static DbType? TypeFor(string column)=>column switch {"A"=>DbType.Date,"T"=>DbType.Time,"I" or "J"=>null,_ when column.StartsWith('Z')=>DbType.DateTimeOffset,_=>DbType.DateTime2};
    static async Task Transactions()
    {
        foreach(var spec in Specs.Where(s=>s.GetProperty("case_id").GetString()!.StartsWith("TX_",StringComparison.Ordinal)))
        {
            string id=spec.GetProperty("case_id").GetString()!; string family=id[(id.LastIndexOf('_',id.LastIndexOf('_')-1)+1)..];
            bool insert=id.StartsWith("TX_DIRECT",StringComparison.Ordinal); int row=insert?NextId++:TransactionRows[family]; if(insert)TransactionRows[family]=row;
            var item=NewCase(id); await using var c=await OpenAsync();
            foreach(var s in spec.GetProperty("stages").EnumerateArray())await DmlStage(item,s,c,row,insert,!insert);
        }
        var reject=Specs.Single(s=>s.GetProperty("case_id").GetString()=="ASYNC_REJECT_THEN_QUERY");
        var rejection=NewCase("ASYNC_REJECT_THEN_QUERY");int rr=NextId++;var rc=await OpenAsync();Dictionary<string,object?>? previous=null;
        try
        {
            foreach(var spec in reject.GetProperty("stages").EnumerateArray())
            {
                string origin=previous is null?"initial_connection":"same_connection_from_previous_stage";string? previousId=previous?.GetValueOrDefault("operation_connection_id") as string;
                if(RequiresNewIndependentConnection(previous,rc.State,ConnectionId(rc)))
                {rc=await OpenAsync();ToolNeed(ConnectionId(rc)!=previousId,ToolAssertionCode.FreshIdReused);origin="new_connection_for_next_independent_stage_after_safe_abort";}
                previous=await DmlStage(rejection,spec,rc,rr,spec.GetProperty("stage_id").GetString()=="control",false,origin,previousId);
            }
        }
        finally{await rc.DisposeAsync();}
    }
    static async Task Native()
    {
        foreach(var spec in Specs.Where(s=>s.GetProperty("case_id").GetString()!.StartsWith("NATIVE_",StringComparison.Ordinal) && s.GetProperty("case_id").GetString()!="NATIVE_INTERVAL_REJECT_leading"))
        {
            var item=NewCase(spec.GetProperty("case_id").GetString()!); int row=NextId++; await using var c=await OpenAsync(); bool initial=true;
            foreach(var s in spec.GetProperty("stages").EnumerateArray()) { await DmlStage(item,s,c,row,initial,false); initial=false; }
        }
    }
    static async Task<Dictionary<string,object?>> DmlStage(Dictionary<string,object?> item,JsonElement s,DmConnection c,int row,bool insert,bool transactional,string origin="unchanged_case_connection",string? previousId=null)
    {
        string name=s.GetProperty("stage_id").GetString()!,api=s.GetProperty("api").GetString()!,column=Column(s);
        int prepare=s.GetProperty("prepare_calls").GetInt32();var stage=NewStage(item,name,api,prepare);stage["operation_connection_id"]=ConnectionId(c);stage["row_id"]=row;stage["connection_origin"]=origin;stage["previous_connection_id"]=previousId;
        string? expectedError=s.GetProperty("exception").GetString();bool nullValue=s.GetProperty("is_null").GetBoolean();
        bool scaleNull=name=="scale7_null"||(string)item["case_id"]! =="NATIVE_TIME_REJECT_SCALE7_NULL"&&name=="reject";
        object value=nullValue||scaleNull?DBNull.Value:Vector(s.GetProperty("input_vector").GetString()!);
        int scale=s.GetProperty("scale").GetInt32();if(scale>9)scale&=15;if(scaleNull)scale=7;
        await using var tx=transactional?await c.BeginTransactionAsync(Token):null;
        await using var command=Command(c,insert?$"INSERT INTO {Table}(ID,{column}) VALUES({row},:v)":$"UPDATE {Table} SET {column}=:v WHERE ID={row}");command.Transaction=tx;
        var p=Param(command,value,TypeFor(column),scale,column is "I" or "J"?DmDbType.IntervalDayToSecond:null);stage["parameter"]=Parameter(p);stage["affected_rows"]=null;
        bool returned=false;ErrorInfo? rejection=null;var calls=(List<string>)stage["actual_receipts"]!;if(transactional)calls.Add("BeginTransactionAsync");
        if(prepare==1){calls.Add("PrepareAsync");await command.PrepareAsync(Token);}
        int targetStart=BeginTarget(stage,c,command,api);
        try
        {
            calls.Add(api);int affected=api=="ExecuteNonQueryAsync"?await command.ExecuteNonQueryAsync(Token):command.ExecuteNonQuery();returned=true;stage["affected_rows"]=affected;
            Need(affected==1,"dml_affected_rows");Need(expectedError is null,"required_rejection_missing");
            if(transactional){string transaction=s.GetProperty("transaction").GetString()!;if(transaction=="CommitAsync")await tx!.CommitAsync(Token);else await tx!.RollbackAsync(Token);calls.Add(transaction);stage["transaction"]=transaction;stage["transaction_completed"]=true;}
        }
        catch(Exception e)
        {
            if(returned||expectedError is null)throw;CheckInfra(e);rejection=Safe(e);stage["status"]="rejected";stage["error"]=rejection;stage["affected_rows"]=0;
            Need(rejection.Type==expectedError&&rejection.FailureClass=="user_rejection","target_rejection_type");stage["diagnostic_business_send"]=rejection.Outcome;
        }
        finally{EndTarget(stage,c,targetStart,returned);}
        stage["execute_returned"]=returned;
        if(rejection is not null){await Recovery(stage,c,command,fresh=>FreshOnConnection(stage,column,row,fresh));await FreshOfficial(stage,column,row);}
        else await Fresh(stage,column,row);
        return stage;
    }
    static async Task Bindings()
    {
        foreach(var spec in Specs.Where(s=>s.GetProperty("case_id").GetString()!.StartsWith("BIND_",StringComparison.Ordinal)))
        {
            string id=spec.GetProperty("case_id").GetString()!; var item=NewCase(id); await using var originalConnection=await OpenAsync();
            DmConnection current=originalConnection; DmConnection? changed=null;
            bool tz=id.EndsWith("dto",StringComparison.Ordinal); string column=tz?"Z6":"D6";
            await using var cmd=Command(current,$"SELECT :v,TO_CHAR(:v,'{Format(column)}') FROM DUAL");
            // One named input repeated in projection resolves to one parameter snapshot.
            var first=spec.GetProperty("stages")[0]; var p=Param(cmd,Vector(first.GetProperty("input_vector").GetString()!),tz?DbType.DateTimeOffset:DbType.DateTime2,first.GetProperty("scale").GetInt32());
            DmParameter? original=null;
            if(id=="BIND_CLONE_RESET_REOPEN")
            {
                original=p; original.DmSqlType=DmDbType.DateTime; p=original.Clone(); cmd.Parameters.Clear(); cmd.Parameters.Add(p); item["original_parameter_id"]=ConnectionId(original);item["clone_parameter_id"]=ConnectionId(p);item["clone_source_before"]=Parameter(original); item["clone_before"]=Parameter(p);
            }
            try
            {
                foreach(var s in spec.GetProperty("stages").EnumerateArray())
                {
                    string name=s.GetProperty("stage_id").GetString()!;
                    if(name=="clone_after_original_mutation") { original!.Value=Vector("ts_tick"); original.Scale=3; item["clone_source_after"]=Parameter(original); item["clone_after"]=Parameter(p); item["clone_receipt"]=new Dictionary<string,object?> { ["clone_distinct_object"]=!ReferenceEquals(original,p),["original_value_mutated"]=!Equals(original.Value,p.Value),["original_scale_mutated"]=original.Scale!=p.Scale,["clone_value_unchanged"]=Equals(p.Value,Vector("ts_micro")),["clone_scale_unchanged"]=p.Scale==first.GetProperty("scale").GetInt32(),["reset_invoked"]=false }; }
                    if(name=="reset") { p.ResetDbType(); item["reset_db_type_invoked"]=true; p.Value=Vector("ts_second"); item["reset_parameter"]=Parameter(p); ((Dictionary<string,object?>)item["clone_receipt"]!)["reset_invoked"]=true; }
                    if(name=="reopen") { changed=await OpenAsync(); cmd.Connection=changed; current=changed; item["old_connection_id"]=ConnectionId(originalConnection); item["new_connection_id"]=ConnectionId(changed); item["new_physical_profile"]=ProfileInfo(changed); }
                    if(name=="tls_default_explicit_reject") { p=(DmParameter)cmd.CreateParameter();p.ParameterName="v";p.DbType=DbType.DateTime2;p.DmSqlType=DmDbType.DateTime;p.Value=Vector("ts_micro");cmd.Parameters.Clear();cmd.Parameters.Add(p); }
                    var setters=new List<string>();
                    if(name=="tls_default_explicit_reject")setters.AddRange(new[]{"CreateParameter","DbType","DmSqlType","Value","ReplaceParameter"});
                    else if(id.Contains("BIND_REUSE",StringComparison.Ordinal)) { if(name!="second") { p.Value=Vector(s.GetProperty("input_vector").GetString()!);setters.Add("Value"); } else setters.AddRange(new[]{"CreateParameter","Value","DbType","Scale"}); }
                    else if(id.Contains("AUTOREPREPARE",StringComparison.Ordinal)) { if(name!="control") { p.Value=Vector(s.GetProperty("input_vector").GetString()!);p.Scale=(byte)s.GetProperty("scale").GetInt32();setters.AddRange(new[]{"Value","Scale"});if(name=="scale3"){p.Precision=0;setters.Add("Precision");} }else setters.AddRange(new[]{"CreateParameter","Value","DbType","Scale"}); }
                    else if(name=="reopen") {p.Value=Vector("ts_micro");setters.AddRange(new[]{"OpenNewConnection","Command.Connection","Value"});}
                    else if(name=="reset")setters.AddRange(new[]{"ResetDbType","Value"});
                    else if(name=="clone_control")setters.AddRange(new[]{"CreateParameter","Value","DbType","Scale","DmSqlType","Clone","AttachClone"});
                    else if(name=="clone_after_original_mutation")setters.AddRange(new[]{"Original.Value","Original.Scale"});
                    // clone_after_original_mutation deliberately does not mutate clone before executing.
                    await BindingStage(item,s,current,cmd,p,column,setters);
                }
            }
            finally { if(changed is not null)await changed.DisposeAsync(); }
        }
    }
    static async Task BindingStage(Dictionary<string,object?> item,JsonElement s,DmConnection c,DbCommand cmd,DmParameter p,string column,List<string> setters)
    {
        string name=s.GetProperty("stage_id").GetString()!,api=s.GetProperty("api").GetString()!;int prepares=s.GetProperty("prepare_calls").GetInt32();var stage=NewStage(item,name,api,prepares);stage["operation_connection_id"]=ConnectionId(c);stage["parameter"]=Parameter(p);stage["parameter_actions"]=setters;stage["parameter_id"]=ConnectionId(p);stage["command_id"]=ConnectionId(cmd);stage["affected_rows"]=null;
        string? expected=s.GetProperty("exception").GetString();bool returned=false;ErrorInfo? rejection=null;var calls=(List<string>)stage["actual_receipts"]!;
        if(prepares==1){calls.Add("Prepare");cmd.Prepare();}
        int targetStart=BeginTarget(stage,c,cmd,api);
        try
        {
            calls.Add(api);using var r=api=="ExecuteReaderAsync"?await cmd.ExecuteReaderAsync(Token):cmd.ExecuteReader();returned=true;Need(expected is null,"required_binding_rejection_missing");Snapshot(stage,cmd,r);Need(r.Read(),"binding_row");
            ((List<object>)stage["observations"]!).AddRange(await ReadObservation(c,r,column,0,false));Need(!r.Read(),"binding_complete");stage["read_complete"]=true;
        }
        catch(Exception e)
        {
            if(returned||expected is null)throw;CheckInfra(e);rejection=Safe(e);stage["status"]="rejected";stage["error"]=rejection;Need(rejection.Type==expected&&rejection.FailureClass=="user_rejection","binding_rejection");
        }
        finally{EndTarget(stage,c,targetStart,returned);}
        if(rejection is not null)
        {
            await Recovery(stage,c,cmd,async fresh=>
            {
                await using var control=Command(fresh,$"SELECT :v,TO_CHAR(:v,'{Format(column)}') FROM DUAL");
                Param(control,Vector(s.GetProperty("vector").GetString()!),column.StartsWith('Z')?DbType.DateTimeOffset:DbType.DateTime2,6);
                await using var rr=await control.ExecuteReaderAsync(Token);bool first=await rr.ReadAsync(Token);long rows=first?1L:0L;stage["fresh_rows"]=rows;Need(first,"binding_recovery_row");((List<object>)stage["observations"]!).AddRange(await ReadObservation(fresh,rr,column,0,true));bool second=await rr.ReadAsync(Token);if(second)rows++;stage["fresh_rows"]=rows;Need(!second,"binding_recovery_complete");stage["fresh_connection_id"]=ConnectionId(fresh);stage["fresh_column_metadata"]=Columns(rr).FirstOrDefault();
            });
        }
    }
}
