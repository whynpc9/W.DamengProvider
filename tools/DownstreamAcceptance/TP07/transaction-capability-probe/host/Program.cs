using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TP07LiteralProbe;
namespace TP07.Runtime;
internal static class Program
{
    internal const long Ticks=639204129161234560;
    internal static readonly string[] Cases=["T1-shared-sync-pair","T1-shared-async-pair","T1-tls-sync-pair","T1-tls-async-pair"];
    static object Ref(string path)=>new{path=Path.GetFullPath(path),sha256=SafeEvidence.HashFile(path)};
    static void RequireKeys(JsonElement item,params string[] keys)=>SafeEvidence.Require(item.EnumerateObject().Select(x=>x.Name).Order().SequenceEqual(keys.Order()),EvidenceCode.SourceBinding);
    internal static async Task<int> Main(string[] args)
    {
        // No live profile, SQL, case-selection, or continuation argument exists.
        if(args.Length==3&&args[0]=="--offline"&&args[1]=="--output")return await OfflineContracts.Run(args[2]);
        try
        {
            SafeEvidence.Require(args.Length==4&&args[0]=="--ticket"&&args[2]=="--ticket-sha256",EvidenceCode.Usage);
            string ticketPath=Path.GetFullPath(args[1]);SafeEvidence.Require(SafeEvidence.HashFile(ticketPath)==args[3]&&Environment.GetEnvironmentVariable("TP07_T1_INTERNAL_TICKET_SHA256")==args[3],EvidenceCode.SourceBinding);
            var ticket=SafeEvidence.Load(ticketPath);RequireKeys(ticket,"schema_version","task","approval","cohort_id","ordinal","case_id","role","startup","predecessor_validation","output","ledger");
            SafeEvidence.Require(ticket.GetProperty("schema_version").GetInt32()==1&&ticket.GetProperty("task").GetString()=="TP07-T1-case-ticket",EvidenceCode.SourceBinding);
            int ordinal=ticket.GetProperty("ordinal").GetInt32();SafeEvidence.Require(ordinal>=0&&ordinal<4&&ticket.GetProperty("case_id").GetString()==Cases[ordinal],EvidenceCode.Usage);
            var approvalRef=ticket.GetProperty("approval");string approvalPath=approvalRef.GetProperty("path").GetString()!;
            SafeEvidence.Require(Path.IsPathFullyQualified(approvalPath)&&SafeEvidence.HashFile(approvalPath)==approvalRef.GetProperty("sha256").GetString(),EvidenceCode.SourceBinding);
            var approval=SafeEvidence.Load(approvalPath);SafeEvidence.Require(approval.GetProperty("task").GetString()=="TP07-T1-runtime-approval"&&approval.GetProperty("status").GetString()=="runtime_authorized"&&ticket.GetProperty("cohort_id").GetString()==SafeEvidence.HashFile(approvalPath)&&approval.GetProperty("ordered_cases").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(Cases),EvidenceCode.SourceBinding);
            string role=ticket.GetProperty("role").GetString()!;SafeEvidence.Require(role is "main" or "cleanup",EvidenceCode.Usage);
            string output=Path.GetFullPath(ticket.GetProperty("output").GetString()!),ledger=Path.GetFullPath(ticket.GetProperty("ledger").GetString()!);
            string caseRoot=Path.Combine(approval.GetProperty("output_root").GetString()!,new[]{"00-shared-sync","01-shared-async","02-tls-sync","03-tls-async"}[ordinal]);
            SafeEvidence.Require(output==Path.Combine(caseRoot,role)&&ledger==Path.Combine(caseRoot,"main","ownership.json")&&Directory.Exists(output)&&!File.Exists(Path.Combine(output,"report.json")),EvidenceCode.FreshOutput);
            SafeEvidence.Require(ticket.GetProperty("startup").GetProperty("path").GetString()==approval.GetProperty("startup").GetProperty("path").GetString()&&ticket.GetProperty("startup").GetProperty("sha256").GetString()==approval.GetProperty("startup").GetProperty("sha256").GetString(),EvidenceCode.SourceBinding);
            if(ordinal>0){var predecessor=ticket.GetProperty("predecessor_validation");string pp=predecessor.GetProperty("path").GetString()!;SafeEvidence.Require(SafeEvidence.HashFile(pp)==predecessor.GetProperty("sha256").GetString()&&SafeEvidence.Load(pp).GetProperty("status").GetString()=="passed",EvidenceCode.SourceBinding);}else SafeEvidence.Require(ticket.GetProperty("predecessor_validation").ValueKind==JsonValueKind.Null,EvidenceCode.SourceBinding);
            string profile=ordinal<2?"shared":"tls";bool async=(ordinal%2)==1;
            SafeEvidence.Require(Environment.GetEnvironmentVariable("TP07_PROFILE")==profile&&Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST")==ticket.GetProperty("startup").GetProperty("path").GetString()&&Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256")==ticket.GetProperty("startup").GetProperty("sha256").GetString(),EvidenceCode.SourceBinding);
            var guard=CandidateGuard.VerifyProcessEnvironment(Assembly.GetExecutingAssembly());
            return role=="cleanup"?Cleanup(guard,ticket,Ref(ticketPath),output,ledger):await MainCase(guard,ticket,Ref(ticketPath),output,ledger,async);
        }
        catch(Exception){return 2;} // Never print exception messages, stacks, or environment values.
    }
    static int Cleanup(CandidateGuard guard,JsonElement ticket,object ticketRef,string output,string ledgerPath)
    {
        var report=new Dictionary<string,object?>{["schema_version"]=1,["task"]="TP07-T1-cleanup",["approval"]=ticket.GetProperty("approval"),["ticket"]=ticketRef,["runtime_identity"]=guard.RuntimeIdentity,["ledger_before"]=Ref(ledgerPath),["objects"]=null,["cleanup"]=null,["finally_cleanup"]=null,["error"]=null,["status"]="started",["process_exit_code"]=1};
        OwnedObjects? owned=null;
        try{owned=OwnedObjects.LoadForCleanup(guard,ledgerPath);SafeEvidence.Require(owned.Entries.Count==1&&owned.Entries[0].CaseId==ticket.GetProperty("case_id").GetString(),EvidenceCode.LedgerIdentity);report["objects"]=owned.Entries;report["cleanup"]=owned.Cleanup();report["status"]="completed";report["process_exit_code"]=0;}
        catch(Exception e){report["error"]=SafeEvidence.Safe(e);report["status"]="failed";}
        finally{try{if(owned is not null)report["finally_cleanup"]=owned.Cleanup();}catch(Exception e){report["error"]=SafeEvidence.Safe(e);report["status"]="failed";report["process_exit_code"]=1;}SafeEvidence.Write(Path.Combine(output,"report.json"),report);}
        return (int)report["process_exit_code"]!;
    }
    static async Task<int> MainCase(CandidateGuard guard,JsonElement ticket,object ticketRef,string output,string ledgerPath,bool async)
    {
        int scale=guard.Profile=="shared"?7:6,quantum=scale==7?1:10;string id=ticket.GetProperty("case_id").GetString()!;
        var evidence=new TransactionEvidence();var steps=new List<Dictionary<string,object?>>();var transactions=new List<object>();var values=new Dictionary<string,object?>();
        var report=new Dictionary<string,object?>{["schema_version"]=1,["task"]="TP07-T1-case",["approval"]=ticket.GetProperty("approval"),["ticket"]=ticketRef,["cohort_id"]=ticket.GetProperty("cohort_id").GetString(),["case_id"]=id,["profile"]=guard.Profile,["api"]=async?"async":"sync",["definition"]=new{scale,ticks=Ticks,offset_minutes=330,quantum},["runtime_identity"]=guard.RuntimeIdentity,["services"]=null,["observer"]=null,["steps"]=steps,["transactions"]=transactions,["commands"]=evidence.CommandRows,["events"]=evidence.Events,["values"]=values,["ownership"]=null,["main_finally"]=null,["error"]=null,["status"]="started",["process_exit_code"]=1};
        string reportPath=Path.Combine(output,"report.json");OwnedObjects? owned=null;ProbeContext? context=null;IDbContextTransaction? transaction=null;DbConnection? txConnection=null;object? beforeDdlFactory=null;int? beforeDdlContext=null;long beforeDdlResolved=0;
        async Task<object?> Step(string name,string api,Func<Task<object?>> action)
        {
            evidence.Stage=name;var step=new Dictionary<string,object?>{["step_id"]=name,["ordinal"]=steps.Count,["started_sequence"]=evidence.Next(),["completed_sequence"]=null,["status"]="started",["api"]=api,["result"]=null,["error"]=null};steps.Add(step);SafeEvidence.Write(reportPath,report);
            try{var result=await action();step["result"]=result;step["status"]="completed";step["completed_sequence"]=evidence.Next();SafeEvidence.Write(reportPath,report);return result;}
            catch(Exception e){step["error"]=SafeEvidence.Safe(e);step["status"]="failed";step["completed_sequence"]=evidence.Next();throw;}
        }
        try
        {
            await Step("runtime_identity","synchronous_auxiliary",()=>{using var c=guard.OpenTestConnection();return Task.FromResult<object?>(guard.VerifyOpenedConnection(c));});
            owned=new OwnedObjects(guard,ledgerPath);var entry=owned.RegisterEfTable(id,EfObjectShape.Basic,scale);report["ownership"]=entry;
            context=new ProbeContext(entry,guard,evidence);evidence.Install(context);var initialFactory=context.GetService<IRelationalTransactionFactory>();guard.VerifyTransactionService(initialFactory,"factory");beforeDdlFactory=CandidateGuard.Identity(initialFactory);beforeDdlContext=evidence.ObjectId(context);beforeDdlResolved=evidence.Next();report["services"]=new{factory=beforeDdlFactory,before_ddl_factory=beforeDdlFactory,before_ddl_context_object_id=beforeDdlContext,before_ddl_resolved_sequence=beforeDdlResolved,di_origin="context_GetService_IRelationalTransactionFactory",auto_savepoints_enabled=context.Database.AutoSavepointsEnabled};
            await Step("create",async?"ExecuteSqlRawAsync":"ExecuteSqlRaw",async()=>{string sql=context.CreateSql();owned.BeginExternalCreate(entry);try{if(async)await context.Database.ExecuteSqlRawAsync(sql);else context.Database.ExecuteSqlRaw(sql);owned.RecordExternalCreateAcknowledged(entry,new(true,async?"ExecuteSqlRawAsync":"ExecuteSqlRaw",SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(sql))));owned.ConfirmExternalCreate(entry);}catch(Exception e){owned.FailExternalCreate(entry,e);throw;}return Schema(entry,guard,owned);});
            await Step("seed_SaveChanges",async?"SaveChangesAsync":"SaveChanges",async()=>{context.Rows.Add(Row(Ticks,"first"));int count=async?await context.SaveChangesAsync():context.SaveChanges();SafeEvidence.Require(count==1,EvidenceCode.MissingRow);return count;});
            await context.DisposeAsync();context=null;
            await Step("fresh_before",async?"SingleAsync_CountAsync":"Single_Count",async()=>{var result=await Fresh(entry,guard,evidence,owned,async,null,x=>values["before"]=x);values["before"]=result;return result;});
            context=new ProbeContext(entry,guard,evidence);evidence.Install(context);
            await Step("begin_transaction",async?"BeginTransactionAsync_ReadCommitted":"BeginTransaction_ReadCommitted",async()=>{transaction=async?await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted):context.Database.BeginTransaction(IsolationLevel.ReadCommitted);txConnection=context.Database.GetDbConnection();return new{transaction_id=transaction.TransactionId.ToString("D"),ef_object_id=evidence.ObjectId(transaction),underlying_object_id=evidence.ObjectId(transaction.GetDbTransaction())};});
            await Step("capabilities","actual_getters",()=>{IDbContextTransaction activeTransaction = transaction!;var factory=context.GetService<IRelationalTransactionFactory>();guard.VerifyTransactionService(factory,"factory");guard.VerifyTransactionService(activeTransaction,"adapter");var underlying=activeTransaction.GetDbTransaction();guard.VerifyObject(underlying,"transaction");guard.VerifyOpenedConnection(txConnection!,underlying);bool same=ReferenceEquals(activeTransaction,context.Database.CurrentTransaction),connected=ReferenceEquals(underlying.Connection,txConnection);bool auto=context.Database.AutoSavepointsEnabled;report["services"]=new{factory=CandidateGuard.Identity(factory),before_ddl_factory=beforeDdlFactory,before_ddl_context_object_id=beforeDdlContext,before_ddl_resolved_sequence=beforeDdlResolved,di_origin="context_GetService_IRelationalTransactionFactory",auto_savepoints_enabled=auto};transactions.Add(new{read_stage="after_begin",read_sequence=evidence.Next(),capture_state="observed",ef=CandidateGuard.Identity(activeTransaction),underlying=CandidateGuard.Identity(underlying),ef_object_id=evidence.ObjectId(activeTransaction),underlying_object_id=evidence.ObjectId(underlying),connection_object_id=evidence.ObjectId(txConnection),context_object_id=evidence.ObjectId(context),transaction_id=activeTransaction.TransactionId.ToString("D"),current_same_instance=same,underlying_connection_same=connected,ef_supports_savepoints=activeTransaction.SupportsSavepoints,underlying_supports_savepoints=underlying.SupportsSavepoints,isolation_level=underlying.IsolationLevel.ToString()});SafeEvidence.Require(same&&connected&&auto&&activeTransaction.SupportsSavepoints==(scale==7)&&underlying.SupportsSavepoints==(scale==7)&&underlying.IsolationLevel==IsolationLevel.ReadCommitted,EvidenceCode.TypeRoute);return Task.FromResult<object?>(true);});
            ProbeRow? tracked=null;
            await Step("tracking_query",async?"SingleAsync":"Single",async()=>{tracked=async?await context.Rows.SingleAsync(x=>x.Id==1):context.Rows.Single(x=>x.Id==1);steps[^1]["result"]=Value(tracked);Exact(tracked,Ticks,"first");return Value(tracked);});
            await Step("transaction_SaveChanges",async?"SaveChangesAsync":"SaveChanges",async()=>{IDbContextTransaction activeTransaction = transaction!;SafeEvidence.Require(context.Database.AutoSavepointsEnabled&&activeTransaction.SupportsSavepoints==(scale==7)&&activeTransaction.GetDbTransaction().SupportsSavepoints==(scale==7),EvidenceCode.TypeRoute);var underlying=activeTransaction.GetDbTransaction();transactions.Add(new{read_stage="before_write",read_sequence=evidence.Next(),capture_state="observed",ef=CandidateGuard.Identity(activeTransaction),underlying=CandidateGuard.Identity(underlying),ef_object_id=evidence.ObjectId(activeTransaction),underlying_object_id=evidence.ObjectId(underlying),connection_object_id=evidence.ObjectId(txConnection),context_object_id=evidence.ObjectId(context),transaction_id=activeTransaction.TransactionId.ToString("D"),current_same_instance=ReferenceEquals(activeTransaction,context.Database.CurrentTransaction),underlying_connection_same=ReferenceEquals(underlying.Connection,txConnection),ef_supports_savepoints=activeTransaction.SupportsSavepoints,underlying_supports_savepoints=underlying.SupportsSavepoints,isolation_level=underlying.IsolationLevel.ToString()});tracked!.D=new DateTime(Ticks+quantum,DateTimeKind.Unspecified);tracked.Z=new DateTimeOffset(tracked.D,TimeSpan.FromMinutes(330));tracked.N="rollback";int count=async?await context.SaveChangesAsync():context.SaveChanges();SafeEvidence.Require(count==1,EvidenceCode.MissingRow);return count;});
            await Step("query_inside_transaction",async?"AsNoTracking_SingleAsync":"AsNoTracking_Single",async()=>{var inside=async?await context.Rows.AsNoTracking().SingleAsync(x=>x.Id==1):context.Rows.AsNoTracking().Single(x=>x.Id==1);var result=Value(inside);values["inside"]=result;steps[^1]["result"]=result;Exact(inside,Ticks+quantum,"rollback");return result;});
            await Step("full_Rollback",async?"RollbackAsync":"Rollback",async()=>{if(async)await transaction!.RollbackAsync();else transaction!.Rollback();return new{returned=true,ef_object_id=evidence.ObjectId(transaction),transaction_id=transaction.TransactionId.ToString("D")};});
            await Step("dispose_transaction_context","DisposeAsync",async()=>{await transaction!.DisposeAsync();transaction=null;await context.DisposeAsync();context=null;return true;});
            await Step("fresh_after",async?"SingleAsync_CountAsync":"Single_Count",async()=>{var result=await Fresh(entry,guard,evidence,owned,async,txConnection,x=>values["after"]=x);values["after"]=result;return result;});
            evidence.Complete();report["status"]="completed";report["process_exit_code"]=0;
        }
        catch(Exception e){report["error"]=SafeEvidence.Safe(e);report["status"]="failed";}
        finally
        {
            try{if(transaction is not null)await transaction.DisposeAsync();}catch(Exception e){report["error"]=SafeEvidence.Safe(e);report["status"]="failed";report["process_exit_code"]=1;}
            try{if(context is not null)await context.DisposeAsync();}catch(Exception e){report["error"]=SafeEvidence.Safe(e);report["status"]="failed";report["process_exit_code"]=1;}
            try{if(owned is not null)report["main_finally"]=owned.Cleanup();}catch(Exception e){report["main_finally"]=new{error=SafeEvidence.Safe(e),cleanup_verified=false};report["status"]="failed";report["process_exit_code"]=1;}
            report["observer"]=evidence.Observation();SafeEvidence.Write(reportPath,report);
        }
        return (int)report["process_exit_code"]!;
    }
    static object Schema(OwnedObjects.Entry entry,CandidateGuard guard,OwnedObjects owned)
    {
        var columns=owned.Preflight(entry);using var c=guard.OpenTestConnection();
        string Read(string sql){using var command=CandidateGuard.Command(c,sql);var p=command.CreateParameter();p.ParameterName="t";p.DbType=DbType.String;p.Value=entry.Table;command.Parameters.Add(p);return Convert.ToString(command.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture)??"";}
        string primary=Read("SELECT CONSTRAINT_NAME FROM USER_CONSTRAINTS WHERE TABLE_NAME=:t AND CONSTRAINT_TYPE='P'");
        string comment=Read("SELECT COMMENTS FROM ALL_TAB_COMMENTS WHERE OWNER='WDM_PROVIDER_TEST' AND TABLE_NAME=:t AND TABLE_TYPE='TABLE'");
        SafeEvidence.Require(primary==entry.PrimaryKey&&comment=="TP07_"+entry.Nonce,EvidenceCode.SchemaMismatch);return new{columns,primary_key=primary,comment,table=entry.Table};
    }
    internal static ProbeRow Row(long ticks,string n)=>new(){Id=1,D=new DateTime(ticks,DateTimeKind.Unspecified),Z=new DateTimeOffset(new DateTime(ticks,DateTimeKind.Unspecified),TimeSpan.FromMinutes(330)),N=n};
    static object Value(ProbeRow row)=>new{id=row.Id,d=SafeEvidence.Value(row.D),z=SafeEvidence.Value(row.Z),n=row.N is "first" or "rollback"?row.N:"unexpected_value"};
    static void Exact(ProbeRow row,long ticks,string n)=>SafeEvidence.Require(row.Id==1&&row.D.Ticks==ticks&&row.D.Kind==DateTimeKind.Unspecified&&row.Z.Ticks==ticks&&row.Z.Offset==TimeSpan.FromMinutes(330)&&row.Z.UtcTicks==ticks-330L*600000000&&row.N==n,EvidenceCode.OracleMismatch);
    static async Task<object> Fresh(OwnedObjects.Entry entry,CandidateGuard guard,TransactionEvidence evidence,OwnedObjects owned,bool async,DbConnection? previous,Action<object>? partialSink=null)
    {
        await using var fresh=new ProbeContext(entry,guard,evidence);if(async)await fresh.Database.OpenConnectionAsync();else fresh.Database.OpenConnection();var c=fresh.Database.GetDbConnection();var identity=guard.VerifyOpenedConnection(c);
        SafeEvidence.Require(previous is null||!ReferenceEquals(c,previous),EvidenceCode.TypeRoute);
        var row=async?await fresh.Rows.AsNoTracking().SingleAsync(x=>x.Id==1):fresh.Rows.AsNoTracking().Single(x=>x.Id==1);int count=async?await fresh.Rows.CountAsync():fresh.Rows.Count();partialSink?.Invoke(new{row=Value(row),row_count=count,capture_state="partial"});Exact(row,Ticks,"first");SafeEvidence.Require(count==1,EvidenceCode.ExtraRow);
        var d=LiteralOracle.ReadFresh(guard,entry.Table,1,false,entry.Scale,"D");var z=LiteralOracle.ReadFresh(guard,entry.Table,1,true,entry.Scale,"Z");SafeEvidence.Require(LiteralOracle.Exact(d,row.D)&&LiteralOracle.Exact(z,row.Z),EvidenceCode.OracleMismatch);
        return new{row=Value(row),row_count=count,context_object_id=evidence.ObjectId(fresh),connection_object_id=evidence.ObjectId(c),different_connection=previous is null?(bool?)null:!ReferenceEquals(c,previous),identity,schema=Schema(entry,guard,owned),d_oracle=d,z_oracle=z,auxiliary_io="catalog_raw_oracle_cleanup_synchronous"};
    }
}
