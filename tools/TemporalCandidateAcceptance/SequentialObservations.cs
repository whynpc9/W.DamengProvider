using System.Data.Common;
using W.Dm;

internal static partial class Program
{
    static string SequentialProjection(string column)=>$"{column},{column},{column},{column},{column},{column},TO_CHAR({column},'{Format(column)}')";
    static async Task<Dictionary<string,object?>> ReadSequential(DbConnection c,DbDataReader r,string column,int row,bool mismatch)
    {
        // Capture every scalar alias before the first getter. Sequential scalar ordinals are consumed once.
        var columns=Columns(r);var packets=Enumerable.Range(0,6).Select(i=>RawPayload(r,i,columns[i])).ToArray();
        object cell=Cell(r,4);bool nil=packets[4] is null;var ordinals=new List<int>();ErrorInfo? typedError=null;bool nullHandled=false;
        if(mismatch)
        {
            bool refused=false;ordinals.Add(0);try{_=await r.GetFieldValueAsync<int>(0,Token);}catch(InvalidCastException){refused=true;}
            Need(refused,"sequential_mismatch_type");
        }
        ordinals.Add(1);bool actualNull=await r.IsDBNullAsync(1,Token);Need(actualNull==nil,"sequential_alias_null");
        ordinals.Add(2);object? value=r.GetValue(2);
        ordinals.Add(3);object? provider=r.GetProviderSpecificValue(3);
        object? typed=null;ordinals.Add(4);
        try{typed=await Generic(r,4,column,true);}
        catch(Exception e)
        {
            bool RealInfra(Exception x)=>x is TimeoutException or OperationCanceledException or System.IO.IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException||x.InnerException is not null&&RealInfra(x.InnerException);
            nullHandled=nil&&packets[4] is null&&e is DmException dm&&dm.Number==6081&&!RealInfra(e);if(!nullHandled)CheckInfra(e);typedError=Safe(e);
        }
        string? getterText=null;if(!nil){ordinals.Add(5);getterText=SafeTemporalText(r.GetString(5));}
        ordinals.Add(6);ServerProjectionObservation? projection=nil?await ObserveServerProjection(true,6,true,()=>r.IsDBNull(6),()=>r.IsDBNullAsync(6,Token),()=>SafeTemporalText(r.GetString(6))):null;
        string? server=nil?projection!.Text:SafeTemporalText(r.GetString(6));
        var actual=Observe(()=>typed);var schema=r.GetSchemaTable();
        return new Dictionary<string,object?> {
            ["source"]="W",["connection_id"]=ConnectionId(c),["row_id"]=row,["is_null"]=nil,["fields"]=server is null?null:Fields(server,false),["ticks"]=actual.Ticks,["offset_minutes"]=actual.OffsetMinutes,["kind"]=actual.Kind,["raw"]=packets[4],["metadata"]=columns[4],["cell"]=cell,["schema_scale"]=SchemaScales(r)[4],["server_text"]=server,["server_null_receipt"]=projection?.Receipt,
            ["default_type"]=value?.GetType().FullName,["provider_type"]=provider?.GetType().FullName,["field_type"]=r.GetFieldType(4).FullName,["schema_type"]=schema?.Rows[4]["DataType"] is Type t?t.FullName:null,["typed_type"]=typed?.GetType().FullName??TargetType(column),["typed_api"]="GetFieldValueAsync<T>",["typed_error"]=typedError,["typed_null_error"]=nil?typedError?.Type:null,["typed_null_receipt"]=nullHandled?new{actual_getter_invoked=true,has_real_infra_inner=false,cache_is_null=true,getter_api="GetFieldValueAsync<T>"}:null,["default_ticks"]=Observe(()=>value).Ticks,["get_string"]=getterText,["get_value"]=Observe(()=>value),["provider_specific"]=Observe(()=>provider),
            ["sequential_aliases"]=new{mismatch=0,is_dbnull=1,@default=2,provider=3,typed=4,@string=5,server=6},["sequential_getter_ordinals"]=ordinals,["raw_alias_ordinal"]=4,["alias_raw_packets"]=packets,["getter_mismatch_error"]=mismatch?"System.InvalidCastException":null,["next_column_read"]=mismatch
        };
    }
}
