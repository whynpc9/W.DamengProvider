using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TP07.Runtime;
using TP07LiteralProbe;
using W.Dm;

namespace TP07.ConstructorScreen;

internal static class Program
{
    internal const string DesignSha="ae8d14bd4daceb77692ad5c1225e84cbd676079fa1659cbc76ef66cc51ce9748";
    internal const string ScreenSha="b9eb4e9a14a4ffbca86d590a80eab92cd962a4fe28f2023064c9ccda4abcfd21";
    internal const string DefaultQuery="SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t AND COLUMN_NAME='V'";
    private static readonly CancellationTokenSource Deadline=new(TimeSpan.FromMinutes(25));
    private static readonly Dictionary<string,long> totals=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,int> counts=new(){["insert"]=0,["update"]=0,["predicate_select"]=0};
    private static DbCommand? active;private static Observer? observer;
    private static Dictionary<string,object?> report=null!;private static string output="";
    private static void Save()=>SafeEvidence.Write(Path.Combine(output,"report.json"),report);
    private static string HashSql(string sql)=>SafeEvidence.Hash(Encoding.UTF8.GetBytes(sql));

    public static async Task<int> Main(string[] args)
    {
        var records=Cases.Expansion.Select(spec=>new Dictionary<string,object?>{["case_id"]=spec.Id,["ordinal"]=spec.Ordinal,
            ["group"]=spec.Group,["context"]=spec.Group,["kind"]=spec.Kind,["suffix"]=spec.Suffix,["gate"]=spec.Gate,
            ["status"]="not_run",["outcome"]="unobservable",["operations"]=new List<Dictionary<string,object?>>()}).ToList();
        report=new(){["schema_version"]=2,["producer_version"]=2,["task"]="TP07-constructor-ff7-expansion",["design_sha256"]=DesignSha,
            ["candidate_acceptance"]=false,["full_EF_C_accepted"]=false,["literal_support_accepted"]=false,["R04_accepted"]=false,
            ["expected_only"]=false,["database_accessed"]=false,["scope"]="FF7_EXPANSION53_only",["characterization_complete"]=false,["expansion_completed"]=false,
            ["literal_observation_set_complete"]=false,["case_count"]=53,["required_case_ids"]=Cases.Expansion.Select(spec=>spec.Id).ToArray(),["cases"]=records,
            ["operation_counts"]=counts,["startup_assembly_name"]=typeof(Program).Assembly.GetName().Name,["startup_name_compatibility_only"]=true,
            ["sql_route"]="controlled_direct_SQL_not_EF_literal_generator_or_SaveChanges",
            ["execution_role"]="controlled_direct_SQL_not_EF_HasData_SaveChanges_ExecuteUpdate",
            ["settings_observation"]=new{status="inherited_unknown",source_report_sha256=ScreenSha,observed_unknown_number=-5504,
                query_executed=false,fallback_query_attempted=false,mode_inferred=false}};
        bool acquired=false;OwnedObjects? owned=null;int exit=1;string ledger="";
        try
        {
            var options=Arguments(args);output=Path.GetFullPath(options["--output"]);
            SafeEvidence.Require(!Directory.Exists(output)&&!File.Exists(output),EvidenceCode.FreshOutput);Directory.CreateDirectory(output);acquired=true;
            Environment.SetEnvironmentVariable("TP07_STARTUP_MANIFEST",options["--build-manifest"]);
            Environment.SetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256",options["--build-manifest-sha256"]);
            SafeEvidence.Require(options["--profile"]=="shared",EvidenceCode.Usage);
            var guard=CandidateGuard.Load("shared",options["--candidate-manifest"],options.GetValueOrDefault("--capabilities"),options.GetValueOrDefault("--repo")??"/Users/wanghongyi/Projects/W.DmProvider");
            report["profile"]="shared";report["identity_manifest_sha256"]=SafeEvidence.HashFile(guard.IdentityManifestPath);
            report["capabilities_sha256"]=SafeEvidence.HashFile(guard.CapabilitiesPath);report["runtime_identity"]=guard.VerifyRuntime(typeof(Program).Assembly);
            report["startup_dll_sha256"]=SafeEvidence.HashFile(typeof(Program).Assembly.Location);
            report["build_manifest_path"]=Path.GetFullPath(options["--build-manifest"]);report["build_manifest_sha256"]=options["--build-manifest-sha256"];
            observer=new Observer();report["status"]="running";Save();
            if(options.ContainsKey("--cleanup"))
            {
                ledger=Path.GetFullPath(options["--ledger"]);SafeEvidence.Require(SafeEvidence.HashFile(ledger)==options["--ledger-sha256"],EvidenceCode.LedgerIdentity);
                owned=OwnedObjects.LoadForCleanup(guard,ledger);ValidateLedger(owned);report["database_accessed"]=true;
                report["cleanup"]=owned.Cleanup();report["cleanup_verified"]=true;report["status"]="cleanup_complete";exit=0;
            }
            else
            {
                ledger=Path.Combine(output,"ownership.json");owned=new OwnedObjects(guard,ledger);
                using(var context=new MappingContext(guard.BuildConnectionString()))
                {
                    var source=context.GetService<IRelationalTypeMappingSource>();report["provider"]=guard.VerifyProvider(source);
                    var connection=context.Database.GetDbConnection();report["factory_connection"]=guard.VerifyObject(connection,"connection");
                    await connection.OpenAsync(Deadline.Token);report["database_accessed"]=true;report["physical_profile"]=guard.VerifyOpenedConnection(connection);
                }
                var dt=owned.RegisterLiteral("F7-TABLE-DT","TIMESTAMP(7)");owned.Create(dt);totals.Add(dt.Table,0);
                var dto=owned.RegisterLiteral("F7-TABLE-DTO","DATETIME(7) WITH TIME ZONE");owned.Create(dto);totals.Add(dto.Table,0);
                report["main_tables"]=new{dt=dt.Table,dto=dto.Table};Save();
                foreach(var spec in Cases.Expansion)
                {
                    if(spec.Ordinal==53)
                    {
                        SafeEvidence.Require(records.Take(52).All(record=>Equals(record["status"],"completed")&&Equals(record["outcome"],"exact")),EvidenceCode.OracleMismatch);
                        report["literal_observation_set_complete"]=true;Save();
                    }
                    await RunCase(guard,owned,spec.Kind=="dto"?dto:dt,spec,records[spec.Ordinal-1]);
                }
                SafeEvidence.Require(owned.Entries.Count==6&&counts["insert"]==92&&counts["update"]==3&&counts["predicate_select"]==12&&!observer.Failed,EvidenceCode.SourceBinding);
                report["final_row_totals"]=FinalTotals(guard,owned);
                report["characterization_complete"]=true;report["expansion_completed"]=true;report["status"]="expansion_complete";exit=0;
            }
        }
        catch(Exception error){report["status"]="execution_failed";report["failure"]=SafeEvidence.Safe(error);}
        finally
        {
            if(owned is not null)
            {
                try{ValidateLedger(owned);report["finally_cleanup"]=owned.Cleanup();report["cleanup_verified"]=true;}
                catch(Exception error){report["cleanup_verified"]=false;report["cleanup_error"]=SafeEvidence.Safe(error);exit=1;}
            }
            if(observer is not null){report["protocol_frames"]=observer.Frames;report["sent_frames"]=observer.Sends;report["observer_failed"]=observer.Failed;if(observer.Failed)exit=1;}
            if(ledger.Length>0&&File.Exists(ledger)){report["ownership_ledger_path"]=ledger;report["ownership_ledger_sha256"]=SafeEvidence.HashFile(ledger);}
            report["completed_case_count"]=records.Count(record=>Equals(record["status"],"completed"));report["process_exit_code"]=exit;
            if(acquired)try{Save();}catch{exit=2;}
        }
        return exit;
    }

    private static void ValidateLedger(OwnedObjects owned)
    {
        SafeEvidence.Require(owned.Entries.Count<=6&&owned.Entries.Select(entry=>entry.CaseId).Distinct(StringComparer.Ordinal).Count()==owned.Entries.Count,EvidenceCode.LedgerIdentity);
        foreach(var entry in owned.Entries)
        {
            bool dto=entry.CaseId is "F7-TABLE-DTO" or "F7-TABLE-DEF-DTO-min_tick" or "F7-TABLE-DEF-DTO-max";
            bool dt=entry.CaseId is "F7-TABLE-DT" or "F7-TABLE-DEF-DT-min_tick" or "F7-TABLE-DEF-DT-max";
            SafeEvidence.Require((dt||dto)&&entry.Shape=="Literal"&&entry.Scale==7
                &&entry.LiteralStoreType==(dto?"DATETIME(7) WITH TIME ZONE":"TIMESTAMP(7)"),EvidenceCode.LedgerIdentity);
        }
    }
    private static object Control(string caseId,string table,int row)=>new{case_id=caseId,table,row_id=row};
    private static async Task RunCase(CandidateGuard guard,OwnedObjects owned,OwnedObjects.Entry main,ExpansionCase spec,Dictionary<string,object?> record)
    {
        record["status"]="attempted";record["input"]=SafeEvidence.Value(spec.Input.Value);record["table"]=main.Table;Save();
        try
        {
            if(spec.Group=="domain")
            {
                record["control_reference"]=Control(spec.Id,main.Table,spec.TypedRow);
                await Insert(guard,owned,main,record,"typed_insert",spec.TypedRow,spec.Input,true);
                await Insert(guard,owned,main,record,"ff7_insert",spec.Row,spec.Input,false);
            }
            else if(spec.Group=="default")
            {
                var entry=owned.RegisterLiteral("F7-TABLE-DEF-"+spec.Kind.ToUpperInvariant()+"-"+spec.Suffix,spec.Input.StoreType);
                record["table"]=entry.Table;record["control_reference"]=Control(spec.Id,entry.Table,1);
                await CreateDefault(guard,owned,entry,spec,record);totals.Add(entry.Table,0);
                record["default_catalog_pre"]=await DefaultCatalog(guard,entry,spec.Input);
                await Insert(guard,owned,entry,record,"typed_insert",1,spec.Input,true);
                await Operation(guard,owned,entry,record,"default_insert","insert",2,spec.Input,spec.Input,
                    $"INSERT INTO {entry.Table}(ID) VALUES(2)",null,false);
                record["default_catalog_post"]=await DefaultCatalog(guard,entry,spec.Input);
                SafeEvidence.Require(JsonSerializer.Serialize(record["default_catalog_pre"],SafeEvidence.Json)==JsonSerializer.Serialize(record["default_catalog_post"],SafeEvidence.Json),EvidenceCode.SchemaMismatch);
            }
            else if(spec.Group=="predicate")
            {
                var domain=Cases.Domain(spec.Kind,spec.Suffix);record["control_reference"]=Control(domain.Id,main.Table,domain.TypedRow);
                foreach(bool positive in new[]{true,false})
                {
                    var operand=positive?spec.Input:spec.Adjacent!;string expression=Cases.Expression(operand);
                    await Operation(guard,owned,main,record,positive?"predicate_positive":"predicate_negative","select",spec.Row,operand,spec.Input,
                        $"SELECT COUNT(*) FROM {main.Table} WHERE ID={spec.Row} AND V={expression}",null,false,expectedScalar:positive?1:0,predicateExpression:expression);
                }
            }
            else if(spec.Group=="assignment")
            {
                var target=Cases.Domain(spec.Kind,"tail7");var source=Cases.Domain(spec.Kind,"tail0");
                record["control_reference"]=Control(target.Id,main.Table,target.TypedRow);record["source_control_reference"]=Control(source.Id,main.Table,source.TypedRow);
                var seed=Cases.Input(spec.Kind,Cases.Micro);await Insert(guard,owned,main,record,"typed_insert",spec.Row,seed,true);
                string expression=Cases.Expression(spec.Input);
                await Operation(guard,owned,main,record,"assignment_update","update",spec.Row,spec.Input,spec.Input,
                    $"UPDATE {main.Table} SET V={expression} WHERE ID={spec.Row}",expression,false,beforeExpected:seed);
            }
            else if(spec.Group=="dateadd")
            {
                var expected=Cases.Input("dt",spec.Input.Ticks!.Value+TimeSpan.TicksPerSecond);
                record["control_reference"]=Control(spec.Id,main.Table,39);record["source_control_reference"]=Control(Cases.Domain("dt","tail7").Id,main.Table,15);
                await Insert(guard,owned,main,record,"typed_insert",38,spec.Input,true);
                await Insert(guard,owned,main,record,"typed_expected",39,expected,true);
                await Operation(guard,owned,main,record,"dateadd_update","update",38,spec.Input,expected,
                    $"UPDATE {main.Table} SET V=DATEADD(second,1,V) WHERE ID=38","DATEADD(second,1,V)",false,beforeExpected:spec.Input);
            }
            else throw new EvidenceException(EvidenceCode.Usage);
            record["status"]="completed";record["outcome"]="exact";Save();
        }
        catch(Exception error)
        {
            var operations=(List<Dictionary<string,object?>>)record["operations"]!;var last=operations.LastOrDefault();
            record["status"]="failed";record["outcome"]=last?.GetValueOrDefault("outcome")??"unobservable";
            record["failure_stage"]=spec.Group=="dateadd"&&Equals(last?.GetValueOrDefault("role"),"dateadd_update")&&Equals(last?.GetValueOrDefault("outcome"),"loss")?"dateadd_value_mismatch":last?.GetValueOrDefault("error_stage")??"case_setup";
            record["error"]=SafeEvidence.Safe(error);report["failed_case_id"]=spec.Id;
            if(spec.Ordinal==53&&Equals(record["failure_stage"],"dateadd_value_mismatch")&&counts["insert"]==92&&counts["update"]==3&&counts["predicate_select"]==12)
                report["final_row_totals"]=FinalTotals(guard,owned);
            Save();throw;
        }
    }
    private static Task Insert(CandidateGuard guard,OwnedObjects owned,OwnedObjects.Entry entry,Dictionary<string,object?> record,string role,int row,ValueInput input,bool typed)
    {
        string? expression=typed?null:Cases.Expression(input);
        return Operation(guard,owned,entry,record,role,"insert",row,input,input,$"INSERT INTO {entry.Table}(ID,V) VALUES({row},{(typed?":v":expression)})",expression,typed);
    }
    private static async Task Operation(CandidateGuard guard,OwnedObjects owned,OwnedObjects.Entry entry,Dictionary<string,object?> parent,
        string role,string operation,int row,ValueInput input,ValueInput expected,string sql,string? expression,bool typed,
        long? expectedScalar=null,ValueInput? beforeExpected=null,string? predicateExpression=null)
    {
        string operationId=(string)parent["case_id"]!+":"+role;observer!.Stage=operationId;
        var record=new Dictionary<string,object?>{["operation_id"]=operationId,["role"]=role,["operation"]=operation,
            ["api"]=operation=="select"?"ExecuteScalarAsync":"ExecuteNonQueryAsync",["table"]=entry.Table,["row_id"]=row,
            ["input"]=SafeEvidence.Value(input.Value),["expected"]=SafeEvidence.Value(expected.Value),["expression_sql"]=expression,
            ["expression_sha256"]=expression is null?null:HashSql(expression),["predicate_expression_sql"]=predicateExpression,
            ["sql"]=sql,["sql_sha256"]=HashSql(sql),["parameter_count"]=typed?1:0,["affected_rows"]=null,["scalar_result"]=null,
            ["status"]="attempted",["outcome"]="unobservable",["control_reference"]=parent.GetValueOrDefault("control_reference")};
        ((List<Dictionary<string,object?>>)parent["operations"]!).Add(record);Save();
        string stage="preflight";
        try
        {
            record["catalog_pre"]=owned.Preflight(entry);record["row_total_pre"]=Total(guard,entry.Table);
            SafeEvidence.Require((long)record["row_total_pre"]! == totals[entry.Table],EvidenceCode.ExtraRow);
            if(operation=="insert")
            {
                var sink=new Dictionary<string,object?>();var absent=LiteralOracle.ReadFresh(guard,entry.Table,row,input.IsOffset,7,partialSink:sink);
                record["prewrite_absence"]=sink;SafeEvidence.Require(absent.Rows==0,EvidenceCode.ExtraRow);
            }
            if(beforeExpected is not null)
            {
                var sink=new Dictionary<string,object?>();var before=LiteralOracle.ReadFresh(guard,entry.Table,row,input.IsOffset,7,partialSink:sink);
                record["before_oracle"]=before;record["before_oracle_read_attempts"]=sink;record["before_expected"]=SafeEvidence.Value(beforeExpected.Value);
                SafeEvidence.Require(Exact(before,beforeExpected,sink),EvidenceCode.OracleMismatch);
                SafeEvidence.Require(beforeExpected.Ticks!=expected.Ticks, EvidenceCode.Usage);
            }
            using var context=new MappingContext(guard.BuildConnectionString());guard.VerifyProvider(context.GetService<IRelationalTypeMappingSource>());
            var connection=context.Database.GetDbConnection();record["connection_route"]=guard.VerifyObject(connection,"connection");await connection.OpenAsync(Deadline.Token);
            record["operation_identity"]=guard.VerifyOpenedConnection(connection);
            await using var command=CandidateGuard.Command(connection,sql);record["command_route"]=guard.VerifyObject(command,"command");record["command_id"]=Id(command);
            var parameters=new List<object>();record["parameters"]=parameters;
            if(typed)
            {
                var p=new DmParameter{ParameterName="v",DbType=input.IsOffset?DbType.DateTimeOffset:DbType.DateTime2,Precision=0,Scale=7,Value=input.Value};
                command.Parameters.Add(p);parameters.Add(new{route=guard.VerifyObject(p,"parameter"),db_type=p.DbType.ToString(),precision=(int)p.Precision,scale=(int)p.Scale,value=SafeEvidence.Value(p.Value)});
            }
            SafeEvidence.Require(command.Parameters.Count==(typed?1:0),EvidenceCode.TypeRoute);
            stage="execute";counts[operation=="select"?"predicate_select":operation]++;Save();active=command;
            try
            {
                if(operation=="select")
                {
                    object? scalar=await command.ExecuteScalarAsync(Deadline.Token);
                    SafeEvidence.Require(scalar is byte or short or int or long or decimal,EvidenceCode.RawScope);
                    decimal number=Convert.ToDecimal(scalar,CultureInfo.InvariantCulture);SafeEvidence.Require(number is 0m or 1m,EvidenceCode.RawScope);
                    record["scalar_result"]=(long)number;
                }
                else record["affected_rows"]=await command.ExecuteNonQueryAsync(Deadline.Token);
            }
            finally{active=null;}
            if(operation!="select")SafeEvidence.Require(record["affected_rows"] is int affected&&affected==1,EvidenceCode.MissingRow);
            stage="fresh_read";var actual=LiteralOracle.ReadFresh(guard,entry.Table,row,expected.IsOffset,7,partialSink:record);record["oracle"]=actual;record["fresh_rows"]=actual.Rows;
            bool exact=Exact(actual,expected,record);record["observed_value_exact"]=exact;
            record["catalog_post"]=owned.Preflight(entry);record["row_total_post"]=Total(guard,entry.Table);
            SafeEvidence.Require(JsonSerializer.Serialize(record["catalog_pre"],SafeEvidence.Json)==JsonSerializer.Serialize(record["catalog_post"],SafeEvidence.Json),EvidenceCode.SchemaMismatch);
            long after=totals[entry.Table]+(operation=="insert"?1:0);
            SafeEvidence.Require((long)record["row_total_post"]! == after,EvidenceCode.ExtraRow);totals[entry.Table]=after;
            SafeEvidence.Require(!observer.Failed,EvidenceCode.SourceBinding);
            if(operation=="select")
            {record["expected_scalar"]=expectedScalar;record["scalar_exact"]=record["scalar_result"] is long scalar&&scalar==expectedScalar;exact=exact&&record["scalar_exact"] is true;}
            record["outcome"]=exact?"exact":"loss";stage="value_mismatch";
            SafeEvidence.Require(exact,EvidenceCode.OracleMismatch);record["status"]="completed";Save();
        }
        catch(Exception error)
        {
            record["status"]="failed";record["error_stage"]=stage;record["error"]=SafeEvidence.Safe(error);
            if(error.GetType()==typeof(DmException)&&Verified(error)&&((DmException)error).FailureInfo is {ErrorKind:DmErrorKind.Server,OperationOutcome:DmOperationOutcome.ServerReported})record["outcome"]="server_rejected";
            report["failed_operation_id"]=operationId;Save();throw;
        }
    }
    private static bool Exact(TemporalOracleReceipt receipt,ValueInput expected,Dictionary<string,object?> sink)
    {
        SafeEvidence.Require(receipt.Rows==1&&receipt.Metadata is not null,EvidenceCode.MissingRow);
        var metadata=JsonSerializer.SerializeToElement(receipt.Metadata,SafeEvidence.Json);
        SafeEvidence.Require(metadata.GetProperty("c_type").GetInt32()==(expected.IsOffset?27:26)&&metadata.GetProperty("width").GetInt32()==(expected.IsOffset?11:9)
            &&metadata.GetProperty("scale").GetInt32()==7&&metadata.GetProperty("mask").GetInt32()==0&&!metadata.GetProperty("is_bdta").GetBoolean()
            &&metadata.GetProperty("raw_source").GetString()=="ResultColumn"&&metadata.GetProperty("metadata_role").GetString()=="result_column"
            &&metadata.GetProperty("raw_scale_status").GetString()=="captured_server_source"
            &&metadata.GetProperty("raw_c_type").GetInt32()==(expected.IsOffset?27:26)&&metadata.GetProperty("raw_precision").GetInt32()==(expected.IsOffset?11:9)
            &&metadata.GetProperty("raw_scale").GetInt32()==7&&metadata.GetProperty("raw_mask_before").GetInt32()==0
            &&metadata.GetProperty("raw_derived_mask").GetInt32()==0&&metadata.GetProperty("schema_scale").GetInt32()==7,EvidenceCode.RawMetadata);
        if(expected.Ticks.HasValue)return !receipt.IsNull&&LiteralOracle.Exact(receipt,expected.Value);
        SafeEvidence.Require(receipt.IsNull&&receipt.LocalTicks is null&&receipt.OffsetMinutes is null&&receipt.Raw is null&&receipt.FF9 is null,EvidenceCode.OracleMismatch);
        var typed=JsonSerializer.SerializeToElement(receipt.Typed,SafeEvidence.Json);
        SafeEvidence.Require(typed.GetProperty("type").GetString()=="System.DBNull"&&typed.GetProperty("is_null").GetBoolean(),EvidenceCode.OracleMismatch);
        var attempt=JsonSerializer.SerializeToElement(sink["oracle_read_attempts"],SafeEvidence.Json).EnumerateArray().Single();var cell=attempt.GetProperty("cell");
        SafeEvidence.Require(attempt.GetProperty("completed").GetBoolean()&&cell.GetProperty("is_null").GetBoolean()
            &&cell.GetProperty("payload_length").ValueKind==JsonValueKind.Null&&cell.GetProperty("payload_sha256").ValueKind==JsonValueKind.Null,EvidenceCode.RawMetadata);return true;
    }
    private static async Task CreateDefault(CandidateGuard guard,OwnedObjects owned,OwnedObjects.Entry entry,ExpansionCase spec,Dictionary<string,object?> record)
    {
        string expression=Cases.Expression(spec.Input);
        string sql=$"CREATE TABLE {entry.Table}(ID INT NOT NULL,V {spec.Input.StoreType} DEFAULT {expression},{entry.OwnerMarker} VARCHAR(32) DEFAULT '{entry.Nonce}',CONSTRAINT {entry.PrimaryKey} PRIMARY KEY(ID))";
        owned.BeginExternalCreate(entry);using var connection=guard.OpenTestConnection();await using var command=CandidateGuard.Command(connection,sql);
        var created=new Dictionary<string,object?>{["sql"]=sql,["sql_sha256"]=HashSql(sql),["api"]="ExecuteNonQueryAsync",["call_returned"]=false,
            ["command_route"]=guard.VerifyObject(command,"command"),["connection_route"]=guard.VerifyObject(connection,"connection"),["identity"]=guard.VerifyOpenedConnection(connection)};
        record["create"]=created;record["default_expression_sql"]=expression;Save();active=command;observer!.Stage=spec.Id+":create";
        try
        {
            created["affected_rows"]=await command.ExecuteNonQueryAsync(Deadline.Token);created["call_returned"]=true;
            owned.RecordExternalCreateAcknowledged(entry,new(true,"ExecuteNonQueryAsync",HashSql(sql)));
        }
        catch(Exception error){created["error"]=SafeEvidence.Safe(error);owned.FailExternalCreate(entry,error);throw;}
        finally{active=null;}
        owned.ConfirmExternalCreate(entry);record["created_catalog"]=owned.Preflight(entry);Save();
    }
    private static async Task<object> DefaultCatalog(CandidateGuard guard,OwnedObjects.Entry entry,ValueInput expected)
    {
        using var connection=guard.OpenTestConnection();await using var command=CandidateGuard.Command(connection,DefaultQuery);
        var p=command.CreateParameter();p.ParameterName="t";p.DbType=DbType.String;p.Value=entry.Table;command.Parameters.Add(p);
        await using var reader=await command.ExecuteReaderAsync(Deadline.Token);guard.VerifyObject(reader,"reader");
        SafeEvidence.Require(await reader.ReadAsync(Deadline.Token)&&!reader.IsDBNull(0),EvidenceCode.SchemaMismatch);
        string text=reader.GetString(0);SafeEvidence.Require(text.Length is >0 and <=1024&&!await reader.ReadAsync(Deadline.Token),EvidenceCode.RawScope);
        string expression=Cases.Expression(expected);
        return new{query=DefaultQuery,query_sha256=HashSql(DefaultQuery),table=entry.Table,column="V",rows=1,is_null=false,
            text_length=text.Length,text_sha256=HashSql(text),expected_expression=expression,expected_expression_sha256=HashSql(expression),
            interpretation="pending_hash_only_not_semantic_acceptance"};
    }
    private static long Total(CandidateGuard guard,string table)
    {SafeEvidence.Identifier(table);using var connection=guard.OpenTestConnection();return Convert.ToInt64(CandidateGuard.Scalar(connection,$"SELECT COUNT(*) FROM {table}"),CultureInfo.InvariantCulture);}
    private static object FinalTotals(CandidateGuard guard,OwnedObjects owned)
    {
        return owned.Entries.Select(entry=>
        {
            using var connection=guard.OpenTestConnection();long actual=Convert.ToInt64(CandidateGuard.Scalar(connection,$"SELECT COUNT(*) FROM {entry.Table}"),CultureInfo.InvariantCulture);
            long expected=entry.CaseId=="F7-TABLE-DT"?39:entry.CaseId=="F7-TABLE-DTO"?45:2;
            SafeEvidence.Require(actual==expected&&totals[entry.Table]==expected,EvidenceCode.ExtraRow);
            return new{table=entry.Table,rows=actual,identity=guard.VerifyOpenedConnection(connection)};
        }).ToArray();
    }
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static bool Verified(Exception error)=>error.GetType().GetProperty("HasVerifiedServerResponse",Flags)?.GetValue(error) is true;
    private sealed class ObjectId(int value){internal int Value{get;}=value;}
    private static readonly ConditionalWeakTable<object,ObjectId> objectIds=new();private static int nextId;
    private static int Id(object value)=>objectIds.GetValue(value,_=>new(Interlocked.Increment(ref nextId))).Value;
    private static int? Statement(DbCommand command)
    {for(Type? type=command.GetType();type is not null;type=type.BaseType)if(type.GetField("m_Stmt",Flags|BindingFlags.DeclaredOnly)?.GetValue(command) is{} value)return Id(value);return null;}
    private sealed record OperationId(long SessionId,long LeaseGeneration,long ExecutionId,long InvocationId);
    private static OperationId Identity(object value)
    {
        long Read(string name){SafeEvidence.Require(value.GetType().GetProperty(name,Flags)?.GetValue(value) is long n&&n>0,EvidenceCode.SourceBinding);return(long)value.GetType().GetProperty(name,Flags)!.GetValue(value)!;}
        return new(Read("SessionId"),Read("LeaseGeneration"),Read("ExecutionId"),Read("InvocationId"));
    }
    private sealed record Frame(int Sequence,string Stage,int RequestOpcode,int ResponseOpcode,int SqlCode,int BodyLength,int CommandId,int? StatementId);
    private sealed record Send(int Sequence,string Stage,int Opcode,int CommandId,OperationId Identity);
    private sealed class Observer
    {
        internal string Stage{get;set;}="startup";internal List<Frame> Frames{get;}=[];internal List<Send> Sends{get;}=[];internal bool Failed{get;private set;}
        internal Observer()
        {
            var assembly=typeof(DmConnection).Assembly;var receive=assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")!.GetField("AfterFrame",BindingFlags.Static|BindingFlags.NonPublic)!;
            SafeEvidence.Require(receive.GetValue(null) is null,EvidenceCode.SourceBinding);
            receive.SetValue(null,(Action<short,short,int,int>)((request,response,code,length)=>
            {if(active is null)return;try{SafeEvidence.Require(Frames.Count<20_000,EvidenceCode.SourceBinding);Frames.Add(new(Frames.Count,Stage,request,response,code,length,Id(active),Statement(active)));}catch{Failed=true;}}));
            var send=assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!.GetField("AfterFrameSent",BindingFlags.Static|BindingFlags.NonPublic)!;
            SafeEvidence.Require(send.GetValue(null) is null,EvidenceCode.SourceBinding);
            MethodInfo handler=GetType().GetMethod(nameof(Sent),Flags)!.MakeGenericMethod(send.FieldType.GenericTypeArguments[0]);
            send.SetValue(null,Delegate.CreateDelegate(send.FieldType,this,handler));
        }
        private void Sent<T>(T identity,short opcode)
        {if(active is null)return;try{SafeEvidence.Require(Sends.Count<20_000&&identity is not null,EvidenceCode.SourceBinding);Sends.Add(new(Sends.Count,Stage,opcode,Id(active),Program.Identity(identity!)));}catch{Failed=true;}}
    }
    private static Dictionary<string,string> Arguments(string[] args)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<args.Length;i++)
        {
            string key=args[i];if(key=="--cleanup"){SafeEvidence.Require(result.TryAdd(key,"true"),EvidenceCode.Usage);continue;}
            SafeEvidence.Require(key is "--profile" or "--candidate-manifest" or "--output" or "--capabilities" or "--repo" or "--ledger" or "--ledger-sha256" or "--build-manifest" or "--build-manifest-sha256",EvidenceCode.Usage);
            SafeEvidence.Require(i+1<args.Length&&result.TryAdd(key,args[++i]),EvidenceCode.Usage);
        }
        SafeEvidence.Require(new[]{"--profile","--candidate-manifest","--output","--build-manifest","--build-manifest-sha256"}.All(result.ContainsKey)
            &&(result.ContainsKey("--cleanup")?(result.ContainsKey("--ledger")&&result.ContainsKey("--ledger-sha256")):(!result.ContainsKey("--ledger")&&!result.ContainsKey("--ledger-sha256"))),EvidenceCode.Usage);return result;
    }
}
