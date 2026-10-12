using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
namespace TP07.Runtime;
internal sealed class TransactionEvidence:DbTransactionInterceptor
{
    readonly Dictionary<object,int> objects=new(ReferenceEqualityComparer.Instance);
    sealed record BeginBinding(DbConnection Connection,Guid TransactionId,Guid ConnectionId,DbContext? Context,bool IsAsync,long Sequence);
    readonly Dictionary<DbTransaction,BeginBinding> bindings=new(ReferenceEqualityComparer.Instance);
    internal readonly List<object> Events=[];
    internal readonly List<Dictionary<string,object?>> CommandRows=[];
    internal readonly CommandEvidence Commands;
    internal string Stage="startup"; internal long Sequence;internal bool Unknown;
    readonly long constructed;long installed,started;long? completed;
    object? registration;
    internal TransactionEvidence(){Commands=new(this);constructed=Next();ObjectId(this);ObjectId(Commands);}
    internal long Next()=>++Sequence;
    internal int? ObjectId(object? value){if(value is null)return null;if(!objects.TryGetValue(value,out int id)){id=objects.Count+1;objects.Add(value,id);}return id;}
    internal void Install(DbContext context)
    {
        var registrations=context.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Single().Interceptors?.ToArray()??[];
        int a=registrations.Count(x=>ReferenceEquals(x,this)),b=registrations.Count(x=>ReferenceEquals(x,Commands));
        if(installed==0)installed=Next();SafeEvidence.Require(a==1&&b==1,EvidenceCode.SourceBinding);if(started==0)started=Next();
        registration=new{transaction_count=a,command_count=b,same_transaction_instance=a==1,same_command_instance=b==1,source="IDbContextOptions_CoreOptionsExtension_Interceptors",context_object_id=ObjectId(context),inspected_sequence=Next()};
    }
    internal void Complete(){completed=Next();}
    internal object Observation()=>new{collector=CandidateGuard.Identity(this),command_collector=CandidateGuard.Identity(Commands),collector_object_id=ObjectId(this),command_collector_object_id=ObjectId(Commands),constructed_sequence=constructed,installed_sequence=installed,observation_started_sequence=started,observation_completed_sequence=completed,registration,capture_state=Unknown?"captured_unknown":"observed"};
    void Record(string callback,DbTransaction tx,TransactionEventData data,bool done,Exception? error=null,DbConnection? callbackConnection=null)
    {
        if(Events.Count+CommandRows.Count>=256){Unknown=true;return;}
        long sequence=Next();DbConnection? current=tx.Connection,associated=null;long? bindingSequence=null;string? bindingSource=null;
        bool known=data.Context is not null;bool isBinding=callback is nameof(TransactionStarted) or nameof(TransactionStartedAsync) or nameof(TransactionUsed) or nameof(TransactionUsedAsync);
        bool isTerminal=callback is nameof(TransactionRolledBack) or nameof(TransactionRolledBackAsync) or nameof(TransactionCommitted) or nameof(TransactionCommittedAsync);
        if(isBinding)
        {
            associated=callbackConnection;bindingSequence=sequence;bindingSource="callback_connection_argument";
            known=known&&callbackConnection is not null&&ReferenceEquals(current,callbackConnection)&&!bindings.ContainsKey(tx);
            if(known)bindings.Add(tx,new(callbackConnection!,data.TransactionId,data.ConnectionId,data.Context,data.IsAsync,sequence));
        }
        else if(bindings.TryGetValue(tx,out var binding))
        {
            associated=binding.Connection;bindingSequence=binding.Sequence;
            known=known&&data.TransactionId==binding.TransactionId&&data.ConnectionId==binding.ConnectionId&&ReferenceEquals(data.Context,binding.Context)&&data.IsAsync==binding.IsAsync&&binding.Sequence<sequence;
            if(current is null){bindingSource="registered_transaction_binding";known=known&&isTerminal&&done&&error is null;}
            else{bindingSource="current_transaction_connection";known=known&&ReferenceEquals(current,binding.Connection);}
        }
        else known=false;
        if(error is not null||callback is nameof(TransactionFailed) or nameof(TransactionFailedAsync))known=false;
        if(!known)Unknown=true;
        Events.Add(new{sequence,stage=Stage,source="DbTransactionInterceptor",callback,context_object_id=ObjectId(data.Context),transaction_object_id=ObjectId(tx),connection_object_id=ObjectId(associated),current_connection_object_id=ObjectId(current),connection_binding_source=bindingSource,binding_sequence=bindingSequence,transaction_id=data.TransactionId.ToString("D"),connection_id=data.ConnectionId.ToString("D"),is_async=data.IsAsync,completed=done,error=error is null?null:SafeEvidence.Safe(error),capture_state=known?"observed":"captured_unknown"});
    }
    public override DbTransaction TransactionStarted(DbConnection connection,TransactionEndEventData data,DbTransaction result){Record(nameof(TransactionStarted),result,data,true,callbackConnection:connection);return result;}
    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,TransactionEndEventData data,DbTransaction result,CancellationToken token=default){Record(nameof(TransactionStartedAsync),result,data,true,callbackConnection:connection);return ValueTask.FromResult(result);}
    public override DbTransaction TransactionUsed(DbConnection connection,TransactionEventData data,DbTransaction result){Record(nameof(TransactionUsed),result,data,true,callbackConnection:connection);return result;}
    public override ValueTask<DbTransaction> TransactionUsedAsync(DbConnection connection,TransactionEventData data,DbTransaction result,CancellationToken token=default){Record(nameof(TransactionUsedAsync),result,data,true,callbackConnection:connection);return ValueTask.FromResult(result);}
    public override InterceptionResult CreatingSavepoint(DbTransaction tx,TransactionEventData data,InterceptionResult result){Record(nameof(CreatingSavepoint),tx,data,false);return result;}
    public override ValueTask<InterceptionResult> CreatingSavepointAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken token=default){Record(nameof(CreatingSavepointAsync),tx,data,false);return ValueTask.FromResult(result);}
    public override void CreatedSavepoint(DbTransaction tx,TransactionEventData data){Record(nameof(CreatedSavepoint),tx,data,true);}
    public override Task CreatedSavepointAsync(DbTransaction tx,TransactionEventData data,CancellationToken token=default){Record(nameof(CreatedSavepointAsync),tx,data,true);return Task.CompletedTask;}
    public override InterceptionResult ReleasingSavepoint(DbTransaction tx,TransactionEventData data,InterceptionResult result){Record(nameof(ReleasingSavepoint),tx,data,false);return result;}
    public override ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken token=default){Record(nameof(ReleasingSavepointAsync),tx,data,false);return ValueTask.FromResult(result);}
    public override void ReleasedSavepoint(DbTransaction tx,TransactionEventData data){Record(nameof(ReleasedSavepoint),tx,data,true);}
    public override Task ReleasedSavepointAsync(DbTransaction tx,TransactionEventData data,CancellationToken token=default){Record(nameof(ReleasedSavepointAsync),tx,data,true);return Task.CompletedTask;}
    public override InterceptionResult RollingBackToSavepoint(DbTransaction tx,TransactionEventData data,InterceptionResult result){Record(nameof(RollingBackToSavepoint),tx,data,false);return result;}
    public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken token=default){Record(nameof(RollingBackToSavepointAsync),tx,data,false);return ValueTask.FromResult(result);}
    public override void RolledBackToSavepoint(DbTransaction tx,TransactionEventData data){Record(nameof(RolledBackToSavepoint),tx,data,true);}
    public override Task RolledBackToSavepointAsync(DbTransaction tx,TransactionEventData data,CancellationToken token=default){Record(nameof(RolledBackToSavepointAsync),tx,data,true);return Task.CompletedTask;}
    public override InterceptionResult TransactionRollingBack(DbTransaction tx,TransactionEventData data,InterceptionResult result){Record(nameof(TransactionRollingBack),tx,data,false);return result;}
    public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken token=default){Record(nameof(TransactionRollingBackAsync),tx,data,false);return ValueTask.FromResult(result);}
    public override void TransactionRolledBack(DbTransaction tx,TransactionEndEventData data){Record(nameof(TransactionRolledBack),tx,data,true);}
    public override Task TransactionRolledBackAsync(DbTransaction tx,TransactionEndEventData data,CancellationToken token=default){Record(nameof(TransactionRolledBackAsync),tx,data,true);return Task.CompletedTask;}
    public override InterceptionResult TransactionCommitting(DbTransaction tx,TransactionEventData data,InterceptionResult result){Record(nameof(TransactionCommitting),tx,data,false);return result;}
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx,TransactionEventData data,InterceptionResult result,CancellationToken token=default){Record(nameof(TransactionCommittingAsync),tx,data,false);return ValueTask.FromResult(result);}
    public override void TransactionCommitted(DbTransaction tx,TransactionEndEventData data){Record(nameof(TransactionCommitted),tx,data,true);}
    public override Task TransactionCommittedAsync(DbTransaction tx,TransactionEndEventData data,CancellationToken token=default){Record(nameof(TransactionCommittedAsync),tx,data,true);return Task.CompletedTask;}
    public override void TransactionFailed(DbTransaction tx,TransactionErrorEventData data){Record(nameof(TransactionFailed),tx,data,false,data.Exception);}
    public override Task TransactionFailedAsync(DbTransaction tx,TransactionErrorEventData data,CancellationToken token=default){Record(nameof(TransactionFailedAsync),tx,data,false,data.Exception);return Task.CompletedTask;}
}
internal sealed class CommandEvidence(TransactionEvidence owner):DbCommandInterceptor
{
    readonly Dictionary<Guid,Dictionary<string,object?>> rows=[];
    void Begin(DbCommand command,CommandEventData data,string api,string kind)
    {
        if(owner.Events.Count+owner.CommandRows.Count>=256){owner.Unknown=true;return;}
        var row=new Dictionary<string,object?>{["sequence"]=owner.Next(),["completed_sequence"]=null,["stage"]=owner.Stage,["source"]="DbCommandInterceptor",["command_id"]=data.CommandId.ToString("D"),["connection_id"]=data.ConnectionId.ToString("D"),["context_object_id"]=owner.ObjectId(data.Context),["command_object_id"]=owner.ObjectId(command),["transaction_object_id"]=owner.ObjectId(command.Transaction),["connection_object_id"]=owner.ObjectId(command.Connection),["is_async"]=data.IsAsync,["api"]=api,["kind"]=kind,["sql_sha256"]=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(command.CommandText)),["parameters"]=command.Parameters.Cast<DbParameter>().Select(p=>new{type=p.GetType().FullName,db_type=p.DbType.ToString(),value=SafeEvidence.Value(p.Value)}).ToArray(),["completed"]=false,["result"]=null,["error"]=null,["capture_state"]="observed"};
        if(!rows.TryAdd(data.CommandId,row)){owner.Unknown=true;return;}owner.CommandRows.Add(row);
    }
    void End(CommandEndEventData data,object? result,Exception? error=null){if(!rows.TryGetValue(data.CommandId,out var row)){owner.Unknown=true;return;}row["completed_sequence"]=owner.Next();row["completed"]=error is null;row["result"]=result;row["error"]=error is null?null:SafeEvidence.Safe(error);}
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command,CommandEventData data,InterceptionResult<int> result){Begin(command,data,"NonQueryExecuting","nonquery");return result;}
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken token=default){Begin(command,data,"NonQueryExecutingAsync","nonquery");return ValueTask.FromResult(result);}
    public override int NonQueryExecuted(DbCommand command,CommandExecutedEventData data,int result){End(data,result);return result;}
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,CommandExecutedEventData data,int result,CancellationToken token=default){End(data,result);return ValueTask.FromResult(result);}
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result){Begin(command,data,"ReaderExecuting","reader");return result;}
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken token=default){Begin(command,data,"ReaderExecutingAsync","reader");return ValueTask.FromResult(result);}
    public override DbDataReader ReaderExecuted(DbCommand command,CommandExecutedEventData data,DbDataReader result){End(data,null);return result;}
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken token=default){End(data,null);return ValueTask.FromResult(result);}
    public override InterceptionResult<object> ScalarExecuting(DbCommand command,CommandEventData data,InterceptionResult<object> result){Begin(command,data,"ScalarExecuting","scalar");return result;}
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<object> result,CancellationToken token=default){Begin(command,data,"ScalarExecutingAsync","scalar");return ValueTask.FromResult(result);}
    public override object? ScalarExecuted(DbCommand command,CommandExecutedEventData data,object? result){End(data,null);return result;}
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command,CommandExecutedEventData data,object? result,CancellationToken token=default){End(data,null);return ValueTask.FromResult<object?>(result);}
    public override void CommandFailed(DbCommand command,CommandErrorEventData data){End(data,null,data.Exception);}
    public override Task CommandFailedAsync(DbCommand command,CommandErrorEventData data,CancellationToken token=default){End(data,null,data.Exception);return Task.CompletedTask;}
}
