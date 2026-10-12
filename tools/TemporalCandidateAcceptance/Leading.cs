using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using W.Dm;

internal static partial class Program
{
    static bool PendingLeading; static string PendingLeadingStatus="server_leading_response_pending_review";
    enum ProtocolFrameKind { Other,StatementAllocation,PrepareOrDirectSql,PreparedExecute }
    // Opcode5 is classified by the known call context, never as proof of prepared Execute.
    static ProtocolFrameKind ClassifyProtocolFrame(short opcode)=>opcode switch {3=>ProtocolFrameKind.StatementAllocation,5=>ProtocolFrameKind.PrepareOrDirectSql,6 or 13=>ProtocolFrameKind.PreparedExecute,_=>ProtocolFrameKind.Other};
    static bool CollectPreparedFrame(List<Frame> frames,Frame frame)
    {
        if(ClassifyProtocolFrame(frame.RequestOpcode)==ProtocolFrameKind.Other)return false;
        frames.Add(frame);return true;
    }
    static void CollectDirectFrame(List<Frame> frames,Frame frame)
    {
        if(ClassifyProtocolFrame(frame.RequestOpcode) is ProtocolFrameKind.StatementAllocation or ProtocolFrameKind.PrepareOrDirectSql)frames.Add(frame);
    }
    sealed record PreparedWindowObservation(int AllocationCount,int PrepareOrDirectCount,int ReprepareCount,bool CurrentNoReprepare,Frame[] BusinessFrames);
    static PreparedWindowObservation ObservePreparedWindow(IReadOnlyList<Frame> frames,IReadOnlyList<int?> statements,int? sourceStatement,bool observerFailed)
    {
        int allocations=frames.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.StatementAllocation);
        int prepare=frames.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.PrepareOrDirectSql);
        bool noReprepare=!observerFailed&&sourceStatement.HasValue&&allocations==0&&prepare==0&&statements.Count==frames.Count&&statements.All(id=>id==sourceStatement);
        return new(allocations,prepare,prepare,noReprepare,frames.Where(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.PreparedExecute).ToArray());
    }
    static Frame ObserveDirectResponse(IReadOnlyList<Frame> frames)
    {
        var direct=frames.Where(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.PrepareOrDirectSql).ToArray();
        Need(direct.Length==1,"literal_unique_direct_response_required");return direct[0];
    }
    static object WireResponse(Frame frame)=>new{source="physical_channel_receive_opcode",sql_code=frame.SqlCode,request_opcode=(int)frame.RequestOpcode,response_opcode=(int)frame.ResponseOpcode,body_length=frame.BodyLength};
    static object CurrentBinding(DbCommand command)
    {
        var selected=SelectOwnedStatement(command);var info=selected.Statement is null?null:Invoke(selected.Statement,"H");var parameters=info is null?null:Invoke(info,"GetParamsInfo");
        object? field=(parameters as Array)?.GetValue(0);object? binding=field?.GetType().GetProperty("TemporalBinding",Flags)?.GetValue(field);
        if(binding is null)return new Dictionary<string,object?>{["observable"]=false};
        object? P(string name)=>binding.GetType().GetProperty(name,Flags)?.GetValue(binding);
        object? raw=P("RawMetadata");string? source=raw?.GetType().GetProperty("Source",Flags)?.GetValue(raw)?.ToString();
        Need(source is "ParameterDescribe","binding_raw_source");
        return new Dictionary<string,object?>{["observable"]=true,["reliable_fixed"]=P("ReliableFixed"),["subtype"]=P("IntervalSubtype"),["leading_precision"]=P("IntervalLeadingPrecision"),["fractional_scale"]=P("EncodedScale"),["packed_scale"]=P("EncodingPackedScale"),["source"]=source};
    }
    static Metadata ColumnMetadata(string column)
    {
        using var c=Open("W");using var cmd=Command(c,$"SELECT {column} FROM {Table} WHERE 1=0");using var r=cmd.ExecuteReader();return Columns(r).Single();
    }
    static async Task Leading()
    {
        var item=NewCase("NATIVE_INTERVAL_REJECT_leading");int row=NextId++;await using var c=await OpenAsync();
        await using(var seed=Command(c,$"INSERT INTO {Table}(ID,J) VALUES({row},:v)"))
        {Param(seed,TimeSpan.FromDays(99),scale:6,provider:DmDbType.IntervalDayToSecond);Need(seed.ExecuteNonQuery()==1,"leading_own_seed");}
        await using var cmd=Command(c,$"UPDATE {Table} SET J=:v WHERE ID={row}");
        var p=Param(cmd,TimeSpan.FromDays(99),scale:6,provider:DmDbType.IntervalDayToSecond);
        var control=NewStage(item,"control","ExecuteNonQueryAsync",1);control["operation_connection_id"]=ConnectionId(c);control["command_id"]=ConnectionId(cmd);control["parameter_id"]=ConnectionId(p);control["row_id"]=row;control["parameter"]=Parameter(p);control["parameter_actions"]=new[]{"CreateParameter","Value","DmSqlType","Scale"};
        await LeadingExecute(control,c,cmd,expectReject:false);await Fresh(control,"J",row);
        p.Value=TimeSpan.FromDays(100);
        var reject=NewStage(item,"reject","ExecuteNonQueryAsync",1);reject["operation_connection_id"]=ConnectionId(c);reject["command_id"]=ConnectionId(cmd);reject["parameter_id"]=ConnectionId(p);reject["row_id"]=row;reject["parameter"]=Parameter(p);reject["parameter_actions"]=new[]{"Value"};
        await LeadingExecute(reject,c,cmd,expectReject:true);
        var raw=reject["parameter_metadata_before_execute"] as Metadata;
        bool fixedDay2=raw is {RawSource:"ParameterDescribe",TypeFlag:1,RawCType:21,RawScale:1574,RawMaskBefore:0,RawDerivedMask:0};
        bool placeholder=raw is {RawSource:"ParameterDescribe",TypeFlag:2,RawCType:21,RawScale:0,RawMaskBefore:0,RawDerivedMask:0};
        reject["source_branch"]=fixedDay2?"fixed_day2":placeholder?"advisory_placeholder":"unverified";
        bool local=fixedDay2&&reject["error"] is ErrorInfo {FailureClass:"user_rejection",Type:"System.OverflowException"};
        ToolNeed(local||reject["error"] is ErrorInfo {VerifiedServerResponse:true},ToolAssertionCode.TargetAfterInvalid);
        await Recovery(reject,c,cmd,fresh=>FreshOnConnection(reject,"J",row,fresh),local);
        if(fixedDay2)
        {
            reject["current_binding_observed"]=false;reject["current_binding"]=null;reject["constraint_evidence"]="pre_execute_parameter_describe_plus_control";
            reject["control_only_binding"]=new{evidence_role="control_only",stage_id="control",command_id=control["command_id"],connection_id=control["operation_connection_id"],statement_id=control["binding_statement_id"],binding=control["binding"]};
            reject["binding_owner"]="unobservable";reject["binding_statement_id"]=null;
            var actual=reject["binding"] as Dictionary<string,object?>;
            if(actual?.GetValueOrDefault("observable") is true) {item["status"]="unexpected_current_fixed_binding";PendingLeading=true;PendingLeadingStatus="leading_source_observation_pending_review";}
            else {reject["binding"]=null;reject["binding_observation_status"]="not_published_or_owner_released";}
        }
        if(placeholder)
        {
            var controls=await LeadingServerControls();item["server_controls"]=controls;reject["server_controls"]=controls;reject["status"]="server_leading_response_pending_review";item["status"]="server_leading_response_pending_review";PendingLeading=true;
        }
        else if(!fixedDay2 || reject["current_no_reprepare"] is not true)
        {item["status"]="leading_source_observation_pending_review";PendingLeading=true;PendingLeadingStatus="leading_source_observation_pending_review";}
    }
    static async Task LeadingExecute(Dictionary<string,object?> stage,DmConnection c,DbCommand cmd,bool expectReject)
    {
        stage["column_before"]=ColumnMetadata("J");var calls=(List<string>)stage["actual_receipts"]!;
        calls.Add("PrepareAsync");await cmd.PrepareAsync(Token);stage["parameter_metadata_before_execute"]=ParameterMetadata(cmd).FirstOrDefault();
        int? beforeStatement=SelectOwnedStatement(cmd).Id;stage["parameter_source_connection_id"]=ConnectionId(c);stage["parameter_source_statement_id"]=beforeStatement;stage["parameter_source_capture_stage"]="after_prepare_before_target_execute";
        var executionFrames=new List<Frame>();var executionStatements=new List<int?>();object? atResponse=null;Metadata? rawAtResponse=null;int? statement=null;
        Observation!.Observer=frame=>
        {
            if(!CollectPreparedFrame(executionFrames,frame))return;executionStatements.Add(SelectOwnedStatement(cmd).Id);if(ClassifyProtocolFrame(frame.RequestOpcode)!=ProtocolFrameKind.PreparedExecute)return;atResponse=CurrentBinding(cmd);rawAtResponse=ParameterMetadata(cmd).FirstOrDefault();statement=SelectOwnedStatement(cmd).Id;
        };
        int targetStart=BeginTarget(stage,c,cmd,"ExecuteNonQueryAsync");int sentStart=Observation.SentFrames.Count;bool returned=false;
        try
        {
            calls.Add("ExecuteNonQueryAsync");int affected=await cmd.ExecuteNonQueryAsync(Token);returned=true;stage["affected_rows"]=affected;Need(!expectReject&&affected==1,"leading_expected_boundary");
        }
        catch(Exception e)
        {
            if(returned||!expectReject)throw;CheckInfra(e);var error=Safe(e);
            Need(error.Type=="System.OverflowException" || error.VerifiedServerResponse,"leading_unverified_error");
            stage["error"]=error;stage["status"]="rejected";stage["affected_rows"]=0;
        }
        finally
        {
            Observation.Observer=null;EndTarget(stage,c,targetStart,returned);
            // Fixed pre-send failures may have no published current binding. Never substitute the 99-day binding.
            stage["binding"]=atResponse??CurrentBinding(cmd);stage["binding_capture_stage"]=atResponse is null?"after_target_failure":"business_response_before_legacy_decode";
            stage["binding_owner"]="command";stage["binding_statement_id"]=statement??SelectOwnedStatement(cmd).Id;
            stage["parameter_metadata"]=stage["parameter_metadata_before_execute"];stage["parameter_metadata_at_response"]=rawAtResponse;
            if(expectReject&&atResponse is not null){stage["current_binding_observed"]=true;stage["current_binding"]=atResponse;}
            var window=ObservePreparedWindow(executionFrames,executionStatements,beforeStatement,Observation.ObserverFailed);
            stage["allocation_count"]=window.AllocationCount;stage["prepare_or_direct_count"]=window.PrepareOrDirectCount;stage["reprepare_count"]=window.ReprepareCount;stage["current_no_reprepare"]=window.CurrentNoReprepare;stage["execution_statement_id"]=executionStatements.LastOrDefault();stage["post_failure_statement_id"]=SelectOwnedStatement(cmd).Id;stage["execution_frames"]=executionFrames;stage["statement_ids_during_execution"]=executionStatements;
            var sent=Observation.SentFrames.Skip(sentStart).Where(f=>f.RequestOpcode is 6 or 13).ToArray();Frame? response=window.BusinessFrames.LastOrDefault();stage["server_response"]=response is null?null:WireResponse(response);stage["business_execute_count"]=sent.Length;stage["business_send_count"]=sent.Length;stage["business_send_source"]="physical_channel_frame_sent";stage["business_sent_frames"]=sent;stage["business_response_count"]=window.BusinessFrames.Length;stage["business_response_source"]="physical_channel_receive_opcode";stage["business_frames"]=window.BusinessFrames;stage["column_after"]=ColumnMetadata("J");stage["execute_returned"]=returned;
        }
    }
    static async Task<List<object>> LeadingServerControls()
    {
        var controls=new List<object>();
        foreach(var entry in new[]{(Id:"99_day2",Days:99,Column:"J",Reject:false),(Id:"100_day9",Days:100,Column:"I",Reject:false),(Id:"100_day2",Days:100,Column:"J",Reject:true)})
        {
            int row=NextId++;await using var c=await OpenAsync();await using var cmd=Command(c,$"INSERT INTO {Table}(ID,{entry.Column}) VALUES({row},CAST('{entry.Days} 00:00:00.000000' AS INTERVAL DAY(9) TO SECOND(6)))");
            var stage=new Dictionary<string,object?>{["control_id"]=entry.Id,["api"]="ExecuteNonQuery",["parameter_count"]=cmd.Parameters.Count,["row_id"]=row,["operation_connection_id"]=ConnectionId(c),["column_packed_scale"]=entry.Column=="J"?1574:1686,["status"]="observed",["error"]=null,["observations"]=new List<object>()};
            var executionFrames=new List<Frame>();Observation!.Observer=frame=>CollectDirectFrame(executionFrames,frame);
            try
            {
                stage["affected_rows"]=cmd.ExecuteNonQuery();Need(!entry.Reject&&(int)stage["affected_rows"]! ==1,"leading_literal_boundary");
            }
            catch(Exception e)
            {
                if(!entry.Reject)throw;CheckInfra(e);var error=Safe(e);Need(error.VerifiedServerResponse,"literal_server_response_required");stage["affected_rows"]=0;stage["error"]=error;stage["status"]="rejected";
            }
            finally{Observation.Observer=null;}
            var response=ObserveDirectResponse(executionFrames);
            stage["execution_frames"]=executionFrames;stage["allocation_count"]=executionFrames.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.StatementAllocation);stage["prepare_or_direct_count"]=executionFrames.Count(f=>ClassifyProtocolFrame(f.RequestOpcode)==ProtocolFrameKind.PrepareOrDirectSql);
            stage["business_frames"]=new[]{response};stage["business_response_count"]=1;stage["server_response"]=WireResponse(response);
            await Fresh(stage,entry.Column,row);controls.Add(stage);
        }
        return controls;
    }
}
