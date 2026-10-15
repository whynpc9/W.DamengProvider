#if CANDIDATE
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using W.Dm;

internal static class Candidate
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private static readonly string Prefix = "I09W_" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    private static readonly List<(string Kind, string Name)> Owned = [];
    private static readonly List<object> Results = [];
    private static string Raw = "";
    private static int Failures;
    private static string? Filter;
    private static string? ReportPath;
    private static string Name(string suffix) => Prefix + "_" + suffix;

    public static async Task<int> Main(string[] args)
    {
        bool clean = false;
        Filter = args.Length > 1 ? args[1] : null;
        ReportPath = args.Length > 0 ? args[0] : null;
        try
        {
            Raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("integration_pending");
            using (var c = Open())
            {
                Results.Add(new { lane = "W", user = TestUser, server = c.ServerVersion, assembly = typeof(DmConnection).Assembly.FullName, module=typeof(DmConnection).Assembly.ManifestModule.ModuleVersionId, loaded_assembly=typeof(DmConnection).Assembly.Location });
                Create(c, "TABLE", "DATA", "(ID INT PRIMARY KEY, T TIMESTAMP(9), Z DATETIME(9) WITH TIME ZONE, B BLOB, C CLOB)");
                Create(c, "PROCEDURE", "P", "(A IN INT, B OUT INT, C IN OUT INT) AS BEGIN B := A+10; C := C+A; END;");
                Create(c, "PROCEDURE", "P2", "(A IN INT, B OUT INT, C IN OUT INT) AS BEGIN B := A+20; C := C+A*2; END;");
                Create(c,"PROCEDURE","V","(D OUT DATE,T OUT TIME(6),I OUT INTERVAL DAY(2) TO SECOND(6),Y OUT INTERVAL YEAR(4) TO MONTH,S OUT VARCHAR(32),B OUT VARBINARY(4),X OUT DECIMAL(38,5)) AS BEGIN D:=DATE '2026-10-15';T:=TIME '12:34:56.123456';I:=INTERVAL '2 03:04:05.123456' DAY(2) TO SECOND(6);Y:=INTERVAL '3-4' YEAR(4) TO MONTH;S:='I09 字符串';B:=HEXTORAW('0011FEFF');X:=123456789012345678901234567890123.12345;END;");
                Create(c, "FUNCTION", "F", "(A IN INT, B IN INT) RETURN INT AS BEGIN RETURN A*100+B; END;");
                Create(c, "PROCEDURE", "Z", "AS BEGIN NULL; END;");
                Create(c, "PROCEDURE", "R", $"(K IN INT,B OUT INT) AS BEGIN INSERT INTO \"{Name("DATA")}\"(ID) VALUES(K); B:=77; SELECT 11 AS N FROM DUAL UNION ALL SELECT 12 FROM DUAL; SELECT 21 AS N FROM DUAL; END;");
                Create(c, "PROCEDURE", "C", "(A OUT SYS_REFCURSOR, B OUT SYS_REFCURSOR, N OUT INT) AS BEGIN N:=99; OPEN A FOR SELECT LEVEL AS N FROM DUAL CONNECT BY LEVEL<=1000; OPEN B FOR SELECT LEVEL+100000 AS N FROM DUAL CONNECT BY LEVEL<=1000; END;");
                Create(c, "PROCEDURE", "T", $"(D OUT DECIMAL(28,9), T OUT TIMESTAMP(9), Z OUT DATETIME(9) WITH TIME ZONE, B OUT BLOB, C OUT CLOB) AS BEGIN D:=1234567890123456789.123456789; SELECT T,Z INTO T,Z FROM \"{Name("DATA")}\" WHERE ID=1; B:=HEXTORAW('0011FEFF'); C:='I09 中文 CLOB'; END;");
                Create(c,"PROCEDURE","TL",$"(B OUT BLOB,C OUT CLOB) AS BEGIN SELECT B,C INTO B,C FROM \"{Name("DATA")}\" WHERE ID=1; END;");
                Create(c, "PROCEDURE", "E", $"(K IN INT,B OUT INT) AS BEGIN INSERT INTO \"{Name("DATA")}\"(ID) VALUES(K); B:=88; RAISE_APPLICATION_ERROR(-20009,'I09 controlled error'); END;");
                Create(c, "PROCEDURE", "W", $"(K IN INT, O OUT INT) AS BEGIN INSERT INTO \"{Name("DATA")}\" (ID) VALUES(K+1000); COMMIT; INSERT INTO \"{Name("DATA")}\" (ID) VALUES(K); DBMS_LOCK.SLEEP(3); O:=55; END;");
            }
            SeedTemporalWithOfficial();
            foreach (bool async in new[] { false, true })
            {
                string lane = async ? "async" : "sync";
                await Case(lane+"_prepared_reuse_and_sql_change",async()=>{using var c=Open();using var cmd=Proc(c,"P");cmd.Parameters.Add(P("A",DmDbType.Int32,ParameterDirection.Input,2));cmd.Parameters.Add(P("B",DmDbType.Int32,ParameterDirection.Output,-1));cmd.Parameters.Add(P("C",DmDbType.Int32,ParameterDirection.InputOutput,5));if(async)await cmd.PrepareAsync();else cmd.Prepare();if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();Require((int)cmd.Parameters[1].Value! ==12 && (int)cmd.Parameters[2].Value! ==7,"prepared_initial");cmd.CommandText=$"\"{TestUser}\".\"{Name("P2")}\"";if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();Require((int)cmd.Parameters[1].Value! ==22 && (int)cmd.Parameters[2].Value! ==11,"prepared_sql_change");return Values(cmd);});
                await Case(lane+"_additional_types",async()=>{using var c=Open();using var cmd=Proc(c,"V");foreach(var x in new[]{("D",DmDbType.Date),("T",DmDbType.Time),("I",DmDbType.IntervalDayToSecond),("Y",DmDbType.IntervalYearToMonth),("S",DmDbType.VarChar),("B",DmDbType.VarBinary),("X",DmDbType.XDEC)})cmd.Parameters.Add(P(x.Item1,x.Item2,ParameterDirection.Output,DBNull.Value));if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();Results.Add(new{observed_case=lane+"_additional_types",parameters=Values(cmd)});Require((DateTime)cmd.Parameters[0].Value! ==new DateTime(2026,10,15),"date_value");Require(cmd.Parameters[1].Value is DateTime t && t.TimeOfDay==new TimeSpan(0,12,34,56).Add(TimeSpan.FromTicks(1234560)),"time_value");Require(cmd.Parameters[2].Value is DmIntervalDT dt && dt.getDay()==2 && dt.getHour()==3 && dt.getMinute()==4 && dt.getSecond()==5 && dt.getMsec()==123456,"interval_dt_value");Require(cmd.Parameters[3].Value is DmIntervalYM ym && ym.getYear()==3 && ym.getMonth()==4,"interval_ym_value");Require((string)cmd.Parameters[4].Value! =="I09 字符串","string_value");Require(cmd.Parameters[5].Value is byte[] b && b.SequenceEqual(new byte[]{0,17,254,255}),"binary_value");Require(cmd.Parameters[6].Value is W.Dm.DmDecimal xdec && xdec.ToString()=="123456789012345678901234567890123.12345","xdec_value");return Values(cmd);});
                await Case(lane+"_decimal_overflow_no_publish",async()=>{using var c=Open();using var cmd=Proc(c,"V");foreach(var x in new[]{("D",DmDbType.Date),("T",DmDbType.Time),("I",DmDbType.IntervalDayToSecond),("Y",DmDbType.IntervalYearToMonth),("S",DmDbType.VarChar),("B",DmDbType.VarBinary),("X",DmDbType.Decimal)})cmd.Parameters.Add(P(x.Item1,x.Item2,ParameterDirection.Output,DBNull.Value));Exception? error=null;try{if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();}catch(Exception e){error=e;}Require(error is OverflowException,"large_decimal_not_rejected");Require(cmd.Parameters.Cast<DmParameter>().All(p=>p.Value==DBNull.Value),"large_decimal_partial_publish");return Error(error!);});
                await Case(lane+"_interval_scale_rejects_loss",async()=>{using var c=Open();using var cmd=Proc(c,"V");foreach(var x in new[]{("D",DmDbType.Date),("T",DmDbType.Time),("I",DmDbType.IntervalDayToSecond),("Y",DmDbType.IntervalYearToMonth),("S",DmDbType.VarChar),("B",DmDbType.VarBinary),("X",DmDbType.XDEC)})cmd.Parameters.Add(P(x.Item1,x.Item2,ParameterDirection.Output,DBNull.Value));cmd.Parameters[2].Scale=0;Exception? error=null;try{if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();}catch(Exception e){error=e;}Require(error is OverflowException,"interval_scale_not_rejected");Require(cmd.Parameters.Cast<DmParameter>().All(p=>p.Value==DBNull.Value),"interval_scale_partial_publish");return Error(error!);});
                await Case(lane + "_directions_reuse", async () =>
                {
                    using var c = Open(); using var cmd = Proc(c, "P");
                    cmd.Parameters.Add(P("c", DmDbType.Int32, ParameterDirection.InputOutput, 5));
                    cmd.Parameters.Add(P(":B", DmDbType.Int32, ParameterDirection.Output, -1));
                    cmd.Parameters.Add(P("A", DmDbType.Int32, ParameterDirection.Input, 2));
                    for (int i = 0; i < 2; i++)
                    {
                        if (async) await cmd.ExecuteNonQueryAsync(); else cmd.ExecuteNonQuery();
                        Require((int)cmd.Parameters[1].Value == 12 && (int)cmd.Parameters[0].Value == 7 + i * 2, "direction_or_reuse_value");
                    }
                    return Values(cmd);
                });
                foreach (int position in new[] { 0, 1, 2 }) await Case(lane + "_return_" + position, async () =>
                {
                    using var c = Open(); using var cmd = Proc(c, "F");
                    var args = new List<DmParameter> { P("A", DmDbType.Int32, ParameterDirection.Input, 3), P("B", DmDbType.Int32, ParameterDirection.Input, 4) };
                    var ret = P("RETURN_VALUE", DmDbType.Int32, ParameterDirection.ReturnValue, -1); args.Insert(position, ret);
                    foreach (var p in args) cmd.Parameters.Add(p);
                    if (async) await cmd.ExecuteNonQueryAsync(); else cmd.ExecuteNonQuery();
                    Require((int)ret.Value == 304, "return_position_value"); return Values(cmd);
                });
                await Case(lane + "_no_parameters", async () => { using var c = Open(); using var cmd = Proc(c, "Z"); if (async) await cmd.ExecuteNonQueryAsync(); else cmd.ExecuteNonQuery(); return true; });
                await Case(lane + "_null", async () =>
                {
                    using var c = Open(); using var cmd = Proc(c, "P");
                    cmd.Parameters.Add(P("A", DmDbType.Int32, ParameterDirection.Input, DBNull.Value)); cmd.Parameters.Add(P("B", DmDbType.Int32, ParameterDirection.Output, -1)); cmd.Parameters.Add(P("C", DmDbType.Int32, ParameterDirection.InputOutput, DBNull.Value));
                    if (async) await cmd.ExecuteNonQueryAsync(); else cmd.ExecuteNonQuery();
                    Require(cmd.Parameters[1].Value == DBNull.Value && cmd.Parameters[2].Value == DBNull.Value, "null_outputs"); return Values(cmd);
                });
                foreach(string limit in new[]{"size","precision","scale"})await Case(lane+"_output_"+limit+"_rejects_loss",async()=>{using var c=Open();using var cmd=Proc(c,"T");foreach(var x in new[]{("D",DmDbType.Decimal),("T",DmDbType.DateTime),("Z",DmDbType.DateTimeOffset),("B",DmDbType.Blob),("C",DmDbType.Clob)})cmd.Parameters.Add(P(x.Item1,x.Item2,ParameterDirection.Output,DBNull.Value));if(limit=="size")cmd.Parameters[4].Size=2;if(limit=="precision")cmd.Parameters[0].Precision=10;if(limit=="scale")cmd.Parameters[1].Scale=6;Exception? error=null;try{if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();}catch(Exception e){error=e;}Require(error is OverflowException,"output_constraint_not_rejected");Require(cmd.Parameters.Cast<DmParameter>().All(p=>p.Value==DBNull.Value),"output_constraint_partial_publish");return Error(error!);});
                await Case(lane+"_large_lob_outputs",async()=>{using var c=Open();using var cmd=Proc(c,"TL");var b=P("B",DmDbType.Blob,ParameterDirection.Output,DBNull.Value);var text=P("C",DmDbType.Clob,ParameterDirection.Output,DBNull.Value);cmd.Parameters.Add(b);cmd.Parameters.Add(text);if(async)await cmd.ExecuteNonQueryAsync();else cmd.ExecuteNonQuery();Require(b.Value is byte[] actual && actual.SequenceEqual(LargeBinary),"large_blob_content");Require(text.Value is string value && value==LargeText,"large_clob_content");return new{blob_length=((byte[])b.Value).Length,clob_length=((string)text.Value).Length};});
                await Case(lane + "_types", async () =>
                {
                    using var c = Open(); using var cmd = Proc(c, "T");
                    foreach (var x in new[] { ("D", DmDbType.Decimal), ("T", DmDbType.DateTime), ("Z", DmDbType.DateTimeOffset), ("B", DmDbType.Blob), ("C", DmDbType.Clob) }) cmd.Parameters.Add(P(x.Item1, x.Item2, ParameterDirection.Output, DBNull.Value));
                    if (async) await cmd.ExecuteNonQueryAsync(); else cmd.ExecuteNonQuery();
                    Require((decimal)cmd.Parameters[0].Value == 1234567890123456789.123456789m, "decimal_exact");
                    Require((DateTime)cmd.Parameters[1].Value == Temporal && ((DateTime)cmd.Parameters[1].Value).Kind==DateTimeKind.Unspecified, "temporal_exact");
                    Require((DateTimeOffset)cmd.Parameters[2].Value == new DateTimeOffset(Temporal, TimeSpan.FromMinutes(330)) && ((DateTimeOffset)cmd.Parameters[2].Value).Offset == TimeSpan.FromMinutes(330), "timezone_exact");
                    Require(cmd.Parameters[3].Value is byte[] b && b.SequenceEqual(new byte[] { 0, 17, 254, 255 }), "blob_exact");
                    Require((string)cmd.Parameters[4].Value == "I09 中文 CLOB", "clob_exact"); return Values(cmd);
                });
                await Case(lane+"_execute_scalar_outputs",async()=>{using var c=Open();using var cmd=Proc(c,"R");int id=async?601:600;cmd.Parameters.Add(P("K",DmDbType.Int32,ParameterDirection.Input,id));var p=P("B",DmDbType.Int32,ParameterDirection.Output,-1);cmd.Parameters.Add(p);object? value=async?await cmd.ExecuteScalarAsync():cmd.ExecuteScalar();Require(Convert.ToInt32(value)==11 && (int)p.Value==77,"scalar_result_or_output");using var fresh=Open();Require(Convert.ToInt32(Scalar(fresh,$"SELECT COUNT(*) FROM \"{Name("DATA")}\" WHERE ID={id}"))==1,"scalar_business_result");return new{scalar=value,output=p.Value,final_rows=1};});
                foreach (bool early in new[] { false, true }) await Case(lane + "_ordinary_early_" + early, async () =>
                {
                    using var c = Open(); using var cmd = Proc(c, "R"); var output = P("B", DmDbType.Int32, ParameterDirection.Output, -1); cmd.Parameters.Add(output); int id=(async?300:200)+(early?1:0);cmd.Parameters.Add(P("K",DmDbType.Int32,ParameterDirection.Input,id));
                    using var r = async ? await cmd.ExecuteReaderAsync() : cmd.ExecuteReader();
                    Require((int)output.Value == -1, "scalar_published_before_reader_terminal");
                    var rows = new List<int>();
                    if (!early) do { while (async ? await r.ReadAsync() : r.Read()) rows.Add(r.GetInt32(0)); } while (async ? await r.NextResultAsync() : r.NextResult());
                    if (async) await r.CloseAsync(); else r.Close();
                    Require((int)output.Value == 77, "scalar_missing_after_close"); if (!early) Require(rows.SequenceEqual(new[] { 11, 12, 21 }), "ordinary_results_order");
                    using var fresh=Open();int committed=Convert.ToInt32(Scalar(fresh,$"SELECT COUNT(*) FROM \"{Name("DATA")}\" WHERE ID={id}"));Require(committed==1,"reader_close_business_result");return new { rows, output = output.Value,final_rows=committed };
                });
                foreach (string terminal in new[] { "complete", "parent", "connection", "reopen", "cancel" }) await Case(lane + "_cursors_" + terminal, async () =>
                {
                    using var c = Open(); using var cmd = CursorCommand(c); using var parent = async ? await cmd.ExecuteReaderAsync() : cmd.ExecuteReader();
                    Require(cmd.Parameters[0].Value is DbDataReader && cmd.Parameters[1].Value is DbDataReader, "cursor_reader_public_shape");
                    using var a = (DbDataReader)cmd.Parameters[0].Value; using var b = (DbDataReader)cmd.Parameters[1].Value;
                    Require((int)cmd.Parameters[2].Value == -1, "cursor_scalar_published_early");
                    if (terminal == "complete")
                    {
                        long sa = 0, sb = 0; int count = 0;
                        while (async ? await a.ReadAsync() : a.Read()) { Require(async ? await b.ReadAsync() : b.Read(), "cursor_b_short"); sa += a.GetInt32(0); sb += b.GetInt32(0); count++; }
                        Require(count == 1000 && sa == 500500 && sb == 100500500, "independent_cursor_values"); Require(!(async ? await b.ReadAsync() : b.Read()), "cursor_b_extra");
                    }
                    if(terminal=="cancel"){cmd.Cancel();Require(cmd.Parameters[0].Value==DBNull.Value && cmd.Parameters[1].Value==DBNull.Value && (int)cmd.Parameters[2].Value! == -1,"cancel_did_not_immediately_revoke_outputs");}
                    else if (terminal is "connection" or "reopen") { c.Close(); if (terminal == "reopen") { c.Open(); Require(Convert.ToString(Scalar(c,"SELECT USER FROM DUAL"))?.Trim()==TestUser,"reopened_identity"); } }
                    else if (async) await parent.CloseAsync(); else parent.Close();
                    Require(await RejectRead(a, async), "old_cursor_survived_terminal"); Require(await RejectRead(b, async), "old_cursor_b_survived_terminal");
                    if (terminal is "complete" or "parent") Require((int)cmd.Parameters[2].Value == 99, "scalar_missing_after_cursor_close");
                    return new { terminal, output = cmd.Parameters[2].Value };
                });
                await Case(lane + "_server_error", async () =>
                {
                    using var c = Open(); using var cmd = Proc(c, "E"); var p = P("B", DmDbType.Int32, ParameterDirection.Output, -1); cmd.Parameters.Add(p);int id=async?501:401;cmd.Parameters.Add(P("K",DmDbType.Int32,ParameterDirection.Input,id));
                    Exception? failure = null; try { if (async) await cmd.ExecuteNonQueryAsync(); else cmd.ExecuteNonQuery(); } catch (Exception e) { failure = e; }
                    Require(failure is DmException dm && dm.Number == -20009, "server_error_code"); Require((int)p.Value == -1, "partial_output_after_error");using var fresh=Open();int rows=Convert.ToInt32(Scalar(fresh,$"SELECT COUNT(*) FROM \"{Name("DATA")}\" WHERE ID={id}"));return new{failure=Error(failure!),final_rows=rows};
                });
            }
            await Case("strict_names", async () =>
            {
                using var c = Open(); using var cmd = Proc(c, "P"); cmd.Parameters.Add(P("UNKNOWN", DmDbType.Int32, ParameterDirection.Input, 2)); cmd.Parameters.Add(P("B", DmDbType.Int32, ParameterDirection.Output, -1)); cmd.Parameters.Add(P("C", DmDbType.Int32, ParameterDirection.InputOutput, 5));
                Exception? failure = null; try { await cmd.ExecuteNonQueryAsync(); } catch (Exception e) { failure = e; } Require(failure != null, "unknown_name_accepted"); return Error(failure!);
            });
            await Case("explicit_transaction_rejected", async () =>
            {
                using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = Proc(c, "Z"); cmd.Transaction = tx;
                Exception? failure = null; try { await cmd.ExecuteNonQueryAsync(); } catch (Exception e) { failure = e; } Require(failure is NotSupportedException, "explicit_transaction_not_rejected"); tx.Rollback(); return Error(failure!);
            });
            foreach (string mode in new[] { "pre_cancel", "post_send_cancel", "timeout", "sync_cancel", "sync_timeout" }) await Case(mode, async () =>
            {
                using var c = Open(); using var cmd = Proc(c, "W"); int id = mode switch { "pre_cancel"=>101,"post_send_cancel"=>102,"timeout"=>103,"sync_cancel"=>104,_=>105 };
                cmd.Parameters.Add(P("K", DmDbType.Int32, ParameterDirection.Input, id)); var output = P("O", DmDbType.Int32, ParameterDirection.Output, -1); cmd.Parameters.Add(output);
                using var cancel = new CancellationTokenSource(); if (mode == "pre_cancel") cancel.Cancel(); if (mode.Contains("timeout")) cmd.CommandTimeout = 1;
                Exception? failure = null; bool sendObserved=false;
                try {
                    Task<int> execution=mode.StartsWith("sync")?Task.Run(()=>cmd.ExecuteNonQuery()):cmd.ExecuteNonQueryAsync(cancel.Token);
                    if(mode is "post_send_cancel" or "sync_cancel") { sendObserved=await WaitForMarker(id+1000); Require(sendObserved,"server_execution_marker_missing"); if(mode=="sync_cancel")cmd.Cancel();else cancel.Cancel(); }
                    await execution;
                } catch (Exception e) { failure=e; }
                Require(failure != null, "deadline_or_cancel_did_not_fail"); Require((int)output.Value == -1, "partial_output_after_cancel_or_timeout");
                bool correctException=mode.Contains("timeout") ? failure is DmTimeoutException : failure is OperationCanceledException;
                using var fresh = Open(); int rows = Convert.ToInt32(Scalar(fresh, $"SELECT COUNT(*) FROM \"{Name("DATA")}\" WHERE ID={id}"));int markers=Convert.ToInt32(Scalar(fresh,$"SELECT COUNT(*) FROM \"{Name("DATA")}\" WHERE ID={id+1000}"));
                if (mode == "pre_cancel") Require(rows == 0 && markers==0, "pre_cancel_executed"); else Require(markers==1,"server_execution_not_observed");
                Results.Add(new{observed_case=mode,failure=Error(failure!),final_rows=rows,committed_execution_marker=markers,send_observed_before_cancel=sendObserved}); Require(correctException,"timeout_or_cancel_exception_type"); return new { final_rows = rows, committed_execution_marker=markers,send_observed_before_cancel=sendObserved };
            });
        }
        catch (Exception e) { Failures++; Results.Add(new { fatal = Error(e) }); }
        finally
        {
            try
            {
                using (var c = Open()) foreach (var item in Owned.AsEnumerable().Reverse()) if (Exists(c, item.Name)) Exec(c, $"DROP {item.Kind} \"{item.Name}\"");
                using var fresh = Open(); clean = Owned.All(x => !Exists(fresh, x.Name));
                Results.Add(new { cleanup_verified = clean, owned = Owned.Select(x => new { x.Kind, x.Name }).ToArray() });
            }
            catch (Exception e)
            {
                Failures++; Results.Add(new { cleanup_failure = Error(e) }); SaveProgress();
                try
                {
                    using(var official=OpenOfficial())foreach(var item in Owned.AsEnumerable().Reverse())if(OfficialCount(official,item.Name)!=0){using var drop=official.CreateCommand();drop.CommandText=$"DROP {item.Kind} \"{item.Name}\"";drop.ExecuteNonQuery();}
                    using var independent=OpenOfficial();clean=Owned.All(item=>OfficialCount(independent,item.Name)==0);
                    Results.Add(new { official_recovery_cleanup_verified=clean });
                }
                catch(Exception recovery){Results.Add(new{recovery_cleanup_failure=Error(recovery)});}
            }
        }
        Results.Add(new { failures = Failures, status = Failures == 0 && clean ? "passed" : "failed" });
        string json = JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true });
        if (args.Length > 0) File.WriteAllText(args[0], json + "\n"); else Console.WriteLine(json);
        return Failures == 0 && clean ? 0 : 1;
    }

    private static void SaveProgress(){if(ReportPath!=null)File.WriteAllText(ReportPath,JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true})+"\n");}
    private static async Task<bool> WaitForMarker(int id){var deadline=DateTime.UtcNow.AddSeconds(2);while(DateTime.UtcNow<deadline){using(var c=Open()){if(Convert.ToInt32(Scalar(c,$"SELECT COUNT(*) FROM \"{Name("DATA")}\" WHERE ID={id}"))==1)return true;}await Task.Delay(25);}return false;}
    private static byte[] LargeBinary=>Enumerable.Range(0,100000).Select(i=>(byte)(i%251)).ToArray();
    private static string LargeText=>new string('中',100000);
    private static DateTime Temporal => new DateTime(2026, 10, 15, 12, 34, 56).AddTicks(1234567);
    private static Dm.DmConnection OpenOfficial(){var c=new Dm.DmConnection(Raw);try{c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT USER FROM DUAL";Require(Convert.ToString(cmd.ExecuteScalar())?.Trim()==TestUser,"official_identity");cmd.CommandText="SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL";Require(Convert.ToString(cmd.ExecuteScalar())?.Trim()==TestUser,"official_schema");return c;}catch{c.Dispose();throw;}}
    private static int OfficialCount(Dm.DmConnection c,string name){using var cmd=c.CreateCommand();cmd.CommandText=$"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME='{name}'";return Convert.ToInt32(cmd.ExecuteScalar());}
    private static void SeedTemporalWithOfficial()
    {
        using var c = OpenOfficial();
        using var cmd = c.CreateCommand(); cmd.CommandText = $"INSERT INTO \"{Name("DATA")}\" VALUES(1,:T,:Z,:B,:C)";
        var t = cmd.CreateParameter(); t.ParameterName = "T"; t.DbType = DbType.DateTime2; t.Precision = 9; t.Value = Temporal; cmd.Parameters.Add(t);
        var z = cmd.CreateParameter(); z.ParameterName = "Z"; z.DbType = DbType.DateTimeOffset; z.Precision = 9; z.Value = new DateTimeOffset(Temporal, TimeSpan.FromMinutes(330)); cmd.Parameters.Add(z);
        cmd.Parameters.Add(new Dm.DmParameter("B",Dm.DmDbType.Blob){Value=LargeBinary});cmd.Parameters.Add(new Dm.DmParameter("C",Dm.DmDbType.Clob){Value=LargeText});
        Require(cmd.ExecuteNonQuery() == 1, "official_seed_insert");
        Results.Add(new { official_oracle_seed = "Text typed timestamp(9)/timezone(9), exact seven digits", temporal = Temporal.ToString("O") });
    }
    private static async Task Case(string name, Func<Task<object>> action) { if (Filter != null && !name.Contains(Filter, StringComparison.Ordinal)) return; try { Results.Add(new { name, passed = true, result = await action() }); } catch (Exception e) { Failures++; Results.Add(new { name, passed = false, error = Error(e) }); } }
    private static void Require(bool condition, string message) { if (!condition) throw new ProbeFailure(message); }
    private sealed class ProbeFailure(string message) : Exception(message);
    private static object Error(Exception e) => new { kind = e.GetType().FullName, code = e is DmException dm ? dm.Number : (int?)null, assertion = e is ProbeFailure ? e.Message : null, message = Redact(e.Message), inner = e.InnerException == null ? null : Error(e.InnerException), socket_code=e is System.Net.Sockets.SocketException socket ? socket.SocketErrorCode.ToString() : null, stack = e.StackTrace };
    private static string Redact(string text) { if (Raw.Length > 0) text = text.Replace(Raw, "[connection]"); var settings = new DbConnectionStringBuilder { ConnectionString = Raw }; foreach (string key in settings.Keys) if (key.Replace(" ", "").ToLowerInvariant() is "password" or "pwd" or "userid" or "uid") { string value = Convert.ToString(settings[key]) ?? ""; if (value.Length > 0) text = text.Replace(value, "[credential]", StringComparison.OrdinalIgnoreCase); } return text; }
    private static async Task<bool> RejectRead(DbDataReader r, bool async) { try { if (async) await r.ReadAsync(); else r.Read(); return false; } catch (InvalidOperationException) { return true; } catch (DmException) { return true; } catch(OperationCanceledException){return true;} }
    private static DmConnection Open()
    {
        var builder = new DmConnectionStringBuilder(Raw) { TransportSecurity = DmTransportSecurity.PlaintextAllowed, PersistSecurityInfo = false, Schema = TestUser };
        var c = new DmConnection(builder.ConnectionString);
        try { c.Open(); Require(Convert.ToString(Scalar(c, "SELECT USER FROM DUAL"))?.Trim() == TestUser, "test_identity"); Require(Convert.ToString(Scalar(c, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"))?.Trim() == TestUser, "test_schema"); return c; } catch { c.Dispose(); throw; }
    }
    private static void Create(DmConnection c, string kind, string suffix, string body) { string name = Name(suffix); Require(!Exists(c, name), "name_collision"); Owned.Add((kind, name)); Results.Add(new { reserved_kind=kind,reserved_name=name }); SaveProgress(); Exec(c, $"CREATE {kind} \"{name}\" {body}"); }
    private static bool Exists(DmConnection c, string name) => Convert.ToInt32(Scalar(c, $"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME='{name}'")) != 0;
    private static object? Scalar(DmConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 15; return cmd.ExecuteScalar(); }
    private static void Exec(DmConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 15; cmd.ExecuteNonQuery(); }
    private static DmCommand Proc(DmConnection c, string suffix) => new() { Connection = c, CommandText = $"\"{TestUser}\".\"{Name(suffix)}\"", CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
    private static DmParameter P(string name, DmDbType type, ParameterDirection direction, object value) => new(name, type) { Direction = direction, Value = value };
    private static DmCommand CursorCommand(DmConnection c) { var cmd = Proc(c, "C"); cmd.Parameters.Add(P("A", DmDbType.RefCursor, ParameterDirection.Output, DBNull.Value)); cmd.Parameters.Add(P("B", DmDbType.RefCursor, ParameterDirection.Output, DBNull.Value)); cmd.Parameters.Add(P("N", DmDbType.Int32, ParameterDirection.Output, -1)); return cmd; }
    private static object Values(DmCommand cmd) => cmd.Parameters.Cast<DmParameter>().Select(p => new { name = p.ParameterName, type = p.Value?.GetType().FullName, value = p.Value switch { DBNull=>"DBNULL", DateTime d=>d.ToString("O"),DateTimeOffset d=>d.ToString("O"),byte[] b=>Convert.ToHexString(b),_=>Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) } }).ToArray();
}
#endif
