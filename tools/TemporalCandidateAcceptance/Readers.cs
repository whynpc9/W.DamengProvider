using System.Data;
using System.Globalization;
using W.Dm;

internal static partial class Program
{
    static async Task Readers()
    {
        string d=Profile=="shared"?"D7":"D6",z=Profile=="shared"?"Z7":"Z6";int scale=Profile=="shared"?7:6;
        await using var c=await OpenAsync();
        for(int i=0;i<3;i++)
        {
            await using var seed=Command(c,$"INSERT INTO {Table}(ID,{d},{z}) VALUES({9001+i},:a,:b)");
            object dt=i==2?DBNull.Value:Vector(i==0?"ts_micro":Profile=="shared"?"ts_tick":"ts_micro");
            object dto=i==2?DBNull.Value:Vector(i==0?"tz_micro":Profile=="shared"?"tz_tick":"tz_micro");
            var a=Param(seed,dt,DbType.DateTime2,scale);a.ParameterName="a"; var b=Param(seed,dto,DbType.DateTimeOffset,scale);b.ParameterName="b";
            Need(await seed.ExecuteNonQueryAsync(Token)==1,"reader_seed");
        }
        foreach(string mode in new[]{"default","sequential"})
        {
            var item=NewCase("READER_ORDINARY_"+mode);var stage=NewStage(item,"reader","ExecuteReaderAsync");stage["operation_connection_id"]=ConnectionId(c);
            int reads=0,nexts=0,repeated=0;int moreBefore=Observation!.Count(44);bool mismatch=false;
            string pd=mode=="sequential"?SequentialProjection(d):Projection(d),pz=mode=="sequential"?SequentialProjection(z):Projection(z);
            var cmd=Command(c,$"SELECT {pd} FROM {Table} WHERE ID BETWEEN 9001 AND 9003 ORDER BY ID; SELECT {pz} FROM {Table} WHERE ID BETWEEN 9001 AND 9003 ORDER BY ID");
            var r=await cmd.ExecuteReaderAsync(mode=="sequential"?CommandBehavior.SequentialAccess:CommandBehavior.Default,Token);
            try
            {
                for(int result=0;result<2;result++)
                {
                    int count=0;
                    while(true)
                    {
                        reads++;bool hasRow=await r.ReadAsync(Token);if(!hasRow)break;int row=9001+count;Need(count<3,"reader_row_bound");
                        bool repeat=mode=="default"&&count==0;List<object> actual=mode=="sequential"?[await ReadSequential(c,r,result==0?d:z,row,result==0&&count==0)]:await ReadObservation(c,r,result==0?d:z,row,true,cachedRepeat:repeat,mismatchControl:result==0&&count==0);
                        foreach(var obs in actual.OfType<Dictionary<string,object?>>().Where(o=>o["source"] is "W")) { if(result==0&&count==0)mismatch=obs["getter_mismatch_error"] is "System.InvalidCastException" && obs["next_column_read"] is true; obs["rowset_index"]=result;((List<object>)stage["observations"]!).Add(obs); }
                        if(repeat)repeated++;count++;
                    }
                    Need(count==3,"reader_row_count");nexts++; bool more=await r.NextResultAsync(Token);Need(more==(result==0),"reader_next_result");
                }
            }
            finally { await r.DisposeAsync();await cmd.DisposeAsync(); }
            int scalar=Convert.ToInt32(await ScalarAsync(c,"SELECT 1 FROM DUAL"),CultureInfo.InvariantCulture);
            stage["receipt"]=new {read_async_calls=reads,next_result_async_calls=nexts,rowset_count=2,disposed_async=true,scalar_after_dispose=scalar,same_connection_id=ConnectionId(c),getter_mismatch=new {type=mismatch?"System.InvalidCastException":null,next_column_read=mismatch,next_row_read=mismatch},cached_repeat_count=repeated,more_result_count=Observation.Count(44)-moreBefore,more_result_opcode=44};
        }
        var fetch=NewCase("READER_ORDINARY_FETCH");var fs=NewStage(fetch,"fetch","ExecuteReaderAsync");fs["operation_connection_id"]=ConnectionId(c);
        await using(var seed=Command(c,$"INSERT INTO {Table}(ID,{d},PAD) SELECT LEVEL,:v,RPAD('x',2048,'x') FROM DUAL CONNECT BY LEVEL<=128"))
        {Param(seed,Vector(Profile=="shared"?"ts_tick":"ts_micro"),DbType.DateTime2,scale);Need(await seed.ExecuteNonQueryAsync(Token)==128,"fetch_seed_bound");}
        int total=0,frameBefore=Observation.Count(7);var boundaries=new List<int>();
        var fc=Command(c,$"SELECT {Projection(d)},ID,PAD FROM {Table} WHERE ID BETWEEN 1 AND 128 ORDER BY ID");var fr=await fc.ExecuteReaderAsync(Token);
        int lastCount=Observation.Count(7);frameBefore=lastCount;
        try
        {
            while(true)
            {
                bool has=await fr.ReadAsync(Token);int now=Observation.Count(7);if(now>lastCount){boundaries.Add(total);lastCount=now;}if(!has)break;
                Need(total<128,"fetch_row_bound");int id=await fr.GetFieldValueAsync<int>(2,Token);Need(id==total+1,"fetch_id_order");
                // Default reader supports cached arbitrary ordinal getters; fetch pad is ordinary VARCHAR.
                Need((await fr.GetFieldValueAsync<string>(3,Token)).Length==2048,"fetch_pad_length");
                var actual=await ReadObservation(c,fr,d,id,true);foreach(var obs in actual.OfType<Dictionary<string,object?>>().Where(o=>o["source"] is "W"))((List<object>)fs["observations"]!).Add(obs);total++;
            }
        }
        finally {await fr.DisposeAsync();await fc.DisposeAsync();}
        Need(total==128&&Observation.Count(7)>frameBefore&&boundaries.Count>0,"actual_fetch_not_observed");
        fs["fetch_boundaries"]=boundaries;fs["receipt"]=new {same_connection_id=ConnectionId(c),fetch_boundaries=boundaries,row_count=total,fetch_count=Observation.Count(7)-frameBefore,fetch_opcode=7,fetch_source="physical_channel_receive_opcode",buf_prefetch="default",is_bdta=false,disposed_async=true,scalar_after_dispose=Convert.ToInt32(await ScalarAsync(c,"SELECT 1 FROM DUAL"),CultureInfo.InvariantCulture)};
    }
}
