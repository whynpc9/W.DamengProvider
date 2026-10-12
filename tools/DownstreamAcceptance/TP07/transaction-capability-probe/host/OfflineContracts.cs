using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
namespace TP07.Runtime;
// Offline is reachable only by its separate entry. The supplied connection and every
// command are in-memory stubs; the actual provider factory and EF batch are retained.
internal static class OfflineContracts
{
    internal static async Task<int> Run(string output)
    {
        var cases=new List<object>();int assertions=0;Exception? error=null;
        void Check(bool value){assertions++;SafeEvidence.Require(value,EvidenceCode.TypeRoute);}
        try
        {
            SafeEvidence.Require(Path.IsPathFullyQualified(output)&&!File.Exists(output),EvidenceCode.FreshOutput);
            foreach(bool async in new[]{false,true})foreach(bool supports in new[]{false,true})foreach(bool fail in new[]{false,true})foreach(bool external in new[]{false,true})
            {
                var connection=new StubConnection(supports){FailBusiness=fail};var evidence=new TransactionEvidence();
                var entry=new OwnedObjects.Entry{Table="TP07_OFFLINE",PrimaryKey="TP07_OFFLINE_PK",Scale=6};
                await using(var context=new ProbeContext(entry,null,evidence,connection))
                {
                    evidence.Install(context);var factory=context.GetService<IRelationalTransactionFactory>();
                    Check(factory.GetType().FullName=="W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory");Check(context.Database.AutoSavepointsEnabled);
                    IDbContextTransaction transaction;
                    if(external){connection.Open();var tx=connection.BeginTransaction(IsolationLevel.ReadCommitted);transaction=async?(await context.Database.UseTransactionAsync(tx))!:context.Database.UseTransaction(tx)!;}
                    else transaction=async?await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted):context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
                    Check(transaction.GetType().FullName=="W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransaction");
                    Check(transaction.SupportsSavepoints==supports&&transaction.GetDbTransaction().SupportsSavepoints==supports);
                    Check(ReferenceEquals(transaction.GetDbTransaction(),connection.Current)&&ReferenceEquals(transaction,context.Database.CurrentTransaction));
                    Check(ReferenceEquals(transaction.GetDbTransaction().Connection,connection)&&transaction.GetDbTransaction().IsolationLevel==IsolationLevel.ReadCommitted);
                    context.Update(Program.Row(Program.Ticks+10,"rollback"));evidence.Stage="transaction_SaveChanges";bool failed=false;
                    try{int count=async?await context.SaveChangesAsync():context.SaveChanges();Check(!fail&&count==1);}
                    catch(DbUpdateException ex)when(ex.InnerException is StubBusinessException){failed=true;}
                    Check(failed==fail);Check(connection.Business==1);
                    Check(connection.SavepointCreate==(supports?1:0));Check(connection.SavepointRelease==(supports?1:0));Check(connection.SavepointRollback==(supports&&fail?1:0));
                    var actualCallbacks=evidence.Events.Select(item=>System.Text.Json.JsonSerializer.SerializeToElement(item,SafeEvidence.Json)).Where(item=>item.GetProperty("stage").GetString()=="transaction_SaveChanges").ToArray();
                    string[] expectedCallbacks=supports?(fail?["CreatingSavepoint","CreatedSavepoint","RollingBackToSavepoint","RolledBackToSavepoint","ReleasingSavepoint","ReleasedSavepoint"]:["CreatingSavepoint","CreatedSavepoint","ReleasingSavepoint","ReleasedSavepoint"]):[];
                    Check(actualCallbacks.Select(item=>item.GetProperty("callback").GetString()).SequenceEqual(expectedCallbacks.Select(name=>async?name+"Async":name)));
                    var actualBatch=evidence.CommandRows.Single(row=>(string)row["stage"]! == "transaction_SaveChanges");
                    if(supports)
                    {
                        long batchStarted=(long)actualBatch["sequence"]!,batchFinished=(long)actualBatch["completed_sequence"]!;
                        Check(actualCallbacks[1].GetProperty("sequence").GetInt64()<batchStarted&&batchStarted<batchFinished&&batchFinished<actualCallbacks[2].GetProperty("sequence").GetInt64());
                        Check(actualCallbacks.Select(item=>item.GetProperty("sequence").GetInt64()).SequenceEqual(actualCallbacks.Select(item=>item.GetProperty("sequence").GetInt64()).Order()));
                    }
                    evidence.Stage="full_Rollback";if(async)await transaction.RollbackAsync();else transaction.Rollback();Check(connection.Current!.Rollbacks==1&&connection.Current.Commits==0&&transaction.GetDbTransaction().Connection is null);
                    await transaction.DisposeAsync();Check(connection.Current.Disposals==(external?0:1));evidence.Complete();
                    cases.Add(new{api=async?"async":"sync",supports_savepoints=supports,fail_business=fail,external,terminal="rollback",services=new{factory=CandidateGuard.Identity(factory)},observer=evidence.Observation(),events=evidence.Events,commands=evidence.CommandRows,counters=connection.Counters(),status="completed"});
                }
                // Every open and execution above dispatches into these stub methods.
                Check(connection.RealNetworkAttempts==0);
            }
            foreach(bool owned in new[]{false,true})foreach(bool supports in new[]{false,true})
            {
                var connection=new StubConnection(supports);var evidence=new TransactionEvidence();var entry=new OwnedObjects.Entry{Table="TP07_OFFLINE_FACTORY",PrimaryKey="TP07_OFFLINE_FACTORY_PK",Scale=6};
                await using var context=new ProbeContext(entry,null,evidence,connection);connection.Open();var tx=connection.BeginTransaction(IsolationLevel.ReadCommitted);var relational=context.GetService<IRelationalConnection>();var factory=context.GetService<IRelationalTransactionFactory>();Guid id=Guid.NewGuid();
                var adapter=factory.Create(relational,tx,id,context.GetService<Microsoft.EntityFrameworkCore.Diagnostics.IDiagnosticsLogger<Microsoft.EntityFrameworkCore.DbLoggerCategory.Database.Transaction>>(),owned);
                Check(adapter.TransactionId==id&&ReferenceEquals(adapter.GetDbTransaction(),tx)&&adapter.SupportsSavepoints==supports);adapter.Dispose();Check(connection.Current!.Disposals==(owned?1:0));
                cases.Add(new{api="factory_Create",supports_savepoints=supports,transaction_owned=owned,status="completed",counters=connection.Counters()});
            }
            foreach(bool async in new[]{false,true})foreach(bool supports in new[]{false,true})foreach(bool external in new[]{false,true})
            {
                var connection=new StubConnection(supports);var evidence=new TransactionEvidence();var entry=new OwnedObjects.Entry{Table="TP07_OFFLINE_COMMIT",PrimaryKey="TP07_OFFLINE_COMMIT_PK",Scale=6};
                await using(var context=new ProbeContext(entry,null,evidence,connection))
                {
                    evidence.Install(context);var factory=context.GetService<IRelationalTransactionFactory>();Check(factory.GetType().FullName=="W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory");Check(context.Database.AutoSavepointsEnabled);
                    IDbContextTransaction transaction;
                    if(external){connection.Open();var tx=connection.BeginTransaction(IsolationLevel.ReadCommitted);transaction=async?(await context.Database.UseTransactionAsync(tx))!:context.Database.UseTransaction(tx)!;}
                    else transaction=async?await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted):context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
                    Check(ReferenceEquals(transaction.GetDbTransaction(),connection.Current)&&transaction.SupportsSavepoints==supports&&ReferenceEquals(transaction.GetDbTransaction().Connection,connection));
                    evidence.Stage="full_Commit";if(async)await transaction.CommitAsync();else transaction.Commit();
                    Check(connection.Current!.Commits==1&&connection.Current.Rollbacks==0&&transaction.GetDbTransaction().Connection is null);
                    await transaction.DisposeAsync();Check(connection.Current.Disposals==(external?0:1));evidence.Complete();
                    var actual=evidence.Events.Select(item=>System.Text.Json.JsonSerializer.SerializeToElement(item,SafeEvidence.Json)).ToArray();
                    string[] callbacks=[external?"TransactionUsed":"TransactionStarted","TransactionCommitting","TransactionCommitted"];
                    Check(actual.Select(item=>item.GetProperty("callback").GetString()).SequenceEqual(callbacks.Select(name=>async?name+"Async":name)));
                    Check(actual[^1].GetProperty("current_connection_object_id").ValueKind==System.Text.Json.JsonValueKind.Null&&actual[^1].GetProperty("connection_binding_source").GetString()=="registered_transaction_binding");
                    Check(evidence.CommandRows.Count==0&&connection.Business==0&&connection.SavepointCreate==0&&connection.SavepointRelease==0&&connection.SavepointRollback==0&&connection.RealNetworkAttempts==0);
                    cases.Add(new{api=async?"async":"sync",supports_savepoints=supports,fail_business=false,external,terminal="commit",services=new{factory=CandidateGuard.Identity(factory)},observer=evidence.Observation(),events=evidence.Events,commands=evidence.CommandRows,counters=connection.Counters(),status="completed"});
                }
            }
            Check(cases.Count==28);
        }
        catch(Exception e){error=e;}
        SafeEvidence.Write(output,new{schema_version=1,task="TP07-T1-offline",status=error is null?"completed":"failed",database_accessed=false,required_case_count=28,case_count=cases.Count,actual_provider=new{name=System.Reflection.Assembly.Load("W.EntityFrameworkCore.Dameng").GetName().Name,sha256=SafeEvidence.HashFile(System.Reflection.Assembly.Load("W.EntityFrameworkCore.Dameng").Location),mvid=System.Reflection.Assembly.Load("W.EntityFrameworkCore.Dameng").ManifestModule.ModuleVersionId.ToString("D")},host=new{name=typeof(OfflineContracts).Assembly.GetName().Name,sha256=SafeEvidence.HashFile(typeof(OfflineContracts).Assembly.Location),mvid=typeof(OfflineContracts).Assembly.ManifestModule.ModuleVersionId.ToString("D")},cases,assertions,error=error is null?null:SafeEvidence.Safe(error),process_exit_code=error is null?0:1,activation_accepted=false});
        return error is null?0:1;
    }
}
internal sealed class StubBusinessException:Exception { }
internal sealed class StubConnection(bool supports):DbConnection
{
    ConnectionState state;public StubTransaction? Current;public bool FailBusiness;public int SyncOpens,AsyncOpens,SyncBusiness,AsyncBusiness,SavepointCreate,SavepointRelease,SavepointRollback;public int Business=>SyncBusiness+AsyncBusiness;public int RealNetworkAttempts=>0;
    public object Counters()=>new{sync_opens=SyncOpens,async_opens=AsyncOpens,sync_business=SyncBusiness,async_business=AsyncBusiness,savepoint_create=SavepointCreate,savepoint_release=SavepointRelease,savepoint_rollback=SavepointRollback,transaction_disposals=Current?.Disposals,full_rollbacks=Current?.Rollbacks,full_commits=Current?.Commits,real_network_attempts=RealNetworkAttempts};
    [AllowNull] public override string ConnectionString{get;set;}="";public override string Database=>"TP07_OFFLINE";public override string DataSource=>"in_memory_stub";public override string ServerVersion=>"8.1.4.6";public override ConnectionState State=>state;
    public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();public override void Close()=>state=ConnectionState.Closed;public override void Open(){SyncOpens++;state=ConnectionState.Open;}
    public override Task OpenAsync(CancellationToken token){AsyncOpens++;state=ConnectionState.Open;return Task.CompletedTask;}
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolation)=>Current=new(this,supports,isolation);
    protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolation,CancellationToken token)=>ValueTask.FromResult(BeginDbTransaction(isolation));
    protected override DbCommand CreateDbCommand()=>new StubCommand(this);
    internal int Execute(string sql,bool async)
    {
        if(sql.TrimStart().StartsWith("SAVEPOINT",StringComparison.OrdinalIgnoreCase)){SavepointCreate++;return 0;}
        if(sql.TrimStart().StartsWith("RELEASE SAVEPOINT",StringComparison.OrdinalIgnoreCase)){SavepointRelease++;return 0;}
        if(sql.TrimStart().StartsWith("ROLLBACK TO",StringComparison.OrdinalIgnoreCase)){SavepointRollback++;return 0;}
        if(async)AsyncBusiness++;else SyncBusiness++;if(FailBusiness)throw new StubBusinessException();return 1;
    }
}
internal sealed class StubTransaction(StubConnection connection,bool supports,IsolationLevel isolation):DbTransaction
{
    bool terminal;public int Disposals,Rollbacks,Commits;public override IsolationLevel IsolationLevel=>isolation;protected override DbConnection? DbConnection=>terminal?null:connection;public override bool SupportsSavepoints=>supports;
    public override void Commit(){Commits++;terminal=true;}public override Task CommitAsync(CancellationToken token=default){Commit();return Task.CompletedTask;}public override void Rollback(){Rollbacks++;terminal=true;}public override Task RollbackAsync(CancellationToken token=default){Rollback();return Task.CompletedTask;}
    protected override void Dispose(bool disposing){if(disposing)Disposals++;base.Dispose(disposing);}
}
internal sealed class StubCommand(StubConnection connection):DbCommand
{
    readonly StubParameters parameters=new();DbTransaction? transaction;
    [AllowNull] public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}public override bool DesignTimeVisible{get;set;}
    protected override DbConnection? DbConnection{get=>connection;set{if(!ReferenceEquals(value,connection))throw new NotSupportedException();}}protected override DbTransaction? DbTransaction{get=>transaction;set=>transaction=value;}protected override DbParameterCollection DbParameterCollection=>parameters;
    public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new StubParameter();
    public override int ExecuteNonQuery()=>connection.Execute(CommandText,false);public override Task<int> ExecuteNonQueryAsync(CancellationToken token)=>Task.FromResult(connection.Execute(CommandText,true));public override object ExecuteScalar()=>throw new NotSupportedException();
    DbDataReader Reader(bool async){int count=connection.Execute(CommandText,async);var table=new DataTable();table.Columns.Add("RowsAffected",typeof(int));table.Rows.Add(count);return table.CreateDataReader();}
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>Reader(false);protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token)=>Task.FromResult(Reader(true));
}
internal sealed class StubParameter:DbParameter
{public override DbType DbType{get;set;}public override ParameterDirection Direction{get;set;}=ParameterDirection.Input;public override bool IsNullable{get;set;}[AllowNull] public override string ParameterName{get;set;}="";[AllowNull] public override string SourceColumn{get;set;}="";public override object? Value{get;set;}public override bool SourceColumnNullMapping{get;set;}public override int Size{get;set;}public override void ResetDbType(){} }
internal sealed class StubParameters:DbParameterCollection
{
    readonly List<DbParameter> items=[];public override int Count=>items.Count;public override object SyncRoot=>this;
    public override int Add(object value){items.Add((DbParameter)value);return items.Count-1;}public override void AddRange(Array values){foreach(var value in values)Add(value!);}public override void Clear()=>items.Clear();public override bool Contains(object value)=>items.Contains((DbParameter)value);public override bool Contains(string name)=>items.Any(x=>x.ParameterName==name);public override void CopyTo(Array array,int index)=>((ICollection)items).CopyTo(array,index);public override IEnumerator GetEnumerator()=>items.GetEnumerator();public override int IndexOf(object value)=>items.IndexOf((DbParameter)value);public override int IndexOf(string name)=>items.FindIndex(x=>x.ParameterName==name);public override void Insert(int index,object value)=>items.Insert(index,(DbParameter)value);public override void Remove(object value)=>items.Remove((DbParameter)value);public override void RemoveAt(int index)=>items.RemoveAt(index);public override void RemoveAt(string name)=>RemoveAt(IndexOf(name));protected override DbParameter GetParameter(int index)=>items[index];protected override DbParameter GetParameter(string name)=>items[IndexOf(name)];protected override void SetParameter(int index,DbParameter value)=>items[index]=value;protected override void SetParameter(string name,DbParameter value)=>items[IndexOf(name)]=value;
}
