using System.Data.Common;
using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TP07.Runtime;

namespace TP07.TemporalDesign;

internal sealed record DesignSpec(string Kind,string Table,string History,string PrimaryKey,string Index,
    string Check,int Scale,string DefaultValueSql)
{
    internal string StoreType(int scale)=>Kind=="dt"?$"TIMESTAMP({scale})":$"DATETIME({scale}) WITH TIME ZONE";
    internal string CacheIdentity=>$"{Kind}|{Table}|{History}|{PrimaryKey}|{Index}|{Check}|{Scale}|{DefaultValueSql}";
}

public sealed class DesignContext(DbContextOptions<DesignContext> options) : DbContext(options)
{
    internal DesignSpec Spec=>Program.Current.Spec??throw new InvalidOperationException("TP07_DESIGN_SPEC_MISSING");
    protected override void OnModelCreating(ModelBuilder builder)=>ConfigureModel(builder,Spec,Spec.Scale);
    internal static void ConfigureModel(ModelBuilder builder,DesignSpec spec,int scale)
    {
        builder.HasAnnotation("ProductVersion","10.0.12").HasAnnotation("Relational:MaxIdentifierLength",128);
        builder.UseDamengIdentityColumns();
        builder.HasDefaultSchema(CandidateGuard.User);
        if(spec.Kind=="dt")
        {
            var row=builder.Entity<GuardedDateTimeRow>();
            row.ToTable(spec.Table,CandidateGuard.User,table=>table.HasCheckConstraint(spec.Check,"\"ID\" > 0"));
            row.HasKey(e=>e.Id).HasName(spec.PrimaryKey);row.Property(e=>e.Id).HasColumnName("ID").HasColumnType("INT").ValueGeneratedNever();
            row.Property(e=>e.Value).HasColumnName("V").HasColumnType(spec.StoreType(scale)).HasPrecision(scale).HasDefaultValueSql(spec.DefaultValueSql);
            row.HasIndex(e=>e.Value).HasDatabaseName(spec.Index);
            return;
        }
        var dto=builder.Entity<GuardedDateTimeOffsetRow>();
        dto.ToTable(spec.Table,CandidateGuard.User,table=>table.HasCheckConstraint(spec.Check,"\"ID\" > 0"));
        dto.HasKey(e=>e.Id).HasName(spec.PrimaryKey);dto.Property(e=>e.Id).HasColumnName("ID").HasColumnType("INT").ValueGeneratedNever();
        dto.Property(e=>e.Value).HasColumnName("V").HasColumnType(spec.StoreType(scale)).HasPrecision(scale).HasDefaultValueSql(spec.DefaultValueSql);
        dto.HasIndex(e=>e.Value).HasDatabaseName(spec.Index);
    }
}
internal sealed class GuardedDateTimeRow { public int Id{get;set;} public DateTime? Value{get;set;} }
internal sealed class GuardedDateTimeOffsetRow { public int Id{get;set;} public DateTimeOffset? Value{get;set;} }
internal sealed class DesignModelCacheKey : IModelCacheKeyFactory
{
    public object Create(DbContext context,bool designTime)=>(context.GetType(),Program.Current.Spec?.CacheIdentity,designTime);
}

internal sealed class DesignAudit(CandidateGuard guard) : DbCommandInterceptor
{
    private sealed class Identity(int id){internal int Id{get;}=id;}
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object,Identity> identities=new();
    private static int nextIdentity;
    internal static int Id(object value)=>identities.GetValue(value,_=>new(Interlocked.Increment(ref nextIdentity))).Id;
    internal static int? StatementId(DbCommand command)
    {
        for(Type? type=command.GetType();type!=null;type=type.BaseType)
            if(type.GetField("m_Stmt",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)?.GetValue(command) is{} statement)return Id(statement);
        return null;
    }
    internal List<Dictionary<string,object?>> Commands {get;}=[];
    internal string Stage {get;set;}="startup";
    internal void StartRaw(DbCommand command,string api)=>Before(command,api);
    internal void EndRaw(DbCommand command,string api)=>Complete(command,api);
    internal void FailRaw(DbCommand command,Exception error)
    {Commands.Add(new(){["sequence"]=Commands.Count,["stage"]=Stage,["status"]="failed",["command_id"]=Id(command),["statement_id"]=StatementId(command),["kind"]=Kind(command.CommandText),["error"]=SafeEvidence.Safe(error)});Program.ActiveCommand=null;}
    private string Kind(string sql)
    {
        string text=sql.TrimStart();var spec=Program.Current.Spec;
        if(text.StartsWith("DECLARE",StringComparison.OrdinalIgnoreCase)&&text.Contains("TP07_DOWN_NONMICRO",StringComparison.Ordinal))return "guard";
        if(System.Text.RegularExpressions.Regex.IsMatch(text,@"\bALTER\s+TABLE\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return "alter";
        if(spec!=null&&text.Contains(spec.History,StringComparison.Ordinal)&&!text.StartsWith("SELECT",StringComparison.OrdinalIgnoreCase))return "history_mutation";
        if(text.StartsWith("CREATE",StringComparison.OrdinalIgnoreCase)||text.StartsWith("DROP",StringComparison.OrdinalIgnoreCase)||text.StartsWith("COMMENT",StringComparison.OrdinalIgnoreCase))return "ddl";
        return text.StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)?"read":"other";
    }
    private void Before(DbCommand command,string api)
    {
        Program.Require(command.Connection!=null,DesignCode.MissingConnection);
        Program.ActiveCommand=command;
        foreach(DbParameter parameter in command.Parameters)
            if(parameter.Value is DateTime or DateTimeOffset||parameter.DbType is DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset)
                Program.Require(parameter.Precision==0,DesignCode.SchemaFacet);
        Commands.Add(new(){["sequence"]=Commands.Count,["stage"]=Stage,["status"]="attempted",["api"]=api,["kind"]=Kind(command.CommandText),
            ["sql_sha256"]=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(command.CommandText)),["sql_length"]=command.CommandText.Length,
            ["connection"]=guard.VerifyObject(command.Connection!,"connection"),["connection_id"]=Id(command.Connection!),["command"]=guard.VerifyObject(command,"command"),["command_id"]=Id(command),["statement_id"]=StatementId(command),
            ["parameters"]=command.Parameters.Cast<DbParameter>().Select(p=>new{actual=guard.VerifyObject(p,"parameter"),db_type=p.DbType.ToString(),provider_type=((W.Dm.DmParameter)p).DmSqlType.ToString(),precision=(int)p.Precision,scale=(int)p.Scale,value=SafeEvidence.Value(p.Value)}).ToArray()});
    }
    private void Complete(DbCommand command,string api,DbDataReader? reader=null)
    {
        if(api is "ExecuteNonQuery" or "ExecuteNonQueryAsync")RecordCreateCompletion(command,api);
        Commands.Add(new(){["sequence"]=Commands.Count,["stage"]=Stage,["status"]="completed",["api"]=api,["kind"]=Kind(command.CommandText),
            ["sql_sha256"]=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(command.CommandText)),["command_id"]=Id(command),["statement_id"]=StatementId(command),["reader"]=reader==null?null:guard.VerifyObject(reader,"reader"),["bindings"]=Bindings(command,reader)});
        if(ReferenceEquals(Program.ActiveCommand,command))Program.ActiveCommand=null;
    }
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    internal static void RecordCreateCompletion(DbCommand command,string api)
    {
        if(Program.Current?.Owned is not{} owned)return;
        string text=command.CommandText.TrimStart();string prefix="(?:\"WDM_PROVIDER_TEST\"\\.)?";
        foreach(var entry in owned.Entries)
        {
            if(entry.CreationState=="create_inflight"&&System.Text.RegularExpressions.Regex.IsMatch(text,"\\ACREATE\\s+TABLE\\s+"+prefix+"\""+entry.Table+"\"(?=\\s|\\()",System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                owned.RecordExternalCreateAcknowledged(entry,new(true,api,SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(command.CommandText))));
                if(entry.Check is not null&&text.Contains('\"'+entry.Check+'\"',StringComparison.Ordinal))owned.RecordExternalChildAcknowledged(entry,entry.Check);
            }
            if(entry.CreateAcknowledged&&entry.Index is not null&&System.Text.RegularExpressions.Regex.IsMatch(text,"\\ACREATE\\s+INDEX\\s+"+prefix+"\""+entry.Index+"\"(?=\\s)",System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                owned.RecordExternalChildAcknowledged(entry,entry.Index);
        }
    }
    internal static object[] Bindings(DbCommand command,DbDataReader? reader=null)
    {
        object? Field(object value,string name){for(Type? type=value.GetType();type!=null;type=type.BaseType)if(type.GetField(name,Flags|BindingFlags.DeclaredOnly) is{} field)return field.GetValue(value);return null;}
        object? statement=Field(reader??(object)command,reader==null?"m_Stmt":"m_Statement");
        object? info=statement?.GetType().GetMethod("H",Flags,Type.EmptyTypes)?.Invoke(statement,null);
        object? parameters=info?.GetType().GetMethod("GetParamsInfo",Flags,Type.EmptyTypes)?.Invoke(info,null);
        if(parameters is not Array items)return[];
        return items.Cast<object>().Select(p=>
        {
            object? Read(object? value,string name)=>value?.GetType().GetProperty(name,Flags)?.GetValue(value);
            object? raw=Read(p,"TemporalServerMetadata"),binding=Read(p,"TemporalBinding");
            return(object)new{raw_source=Read(raw,"Source")?.ToString(),raw_c_type=Read(raw,"CType"),raw_scale=Read(raw,"Scale"),raw_type_flag=Read(raw,"TypeFlag"),raw_mask=Read(raw,"MaskBeforeNormalization"),
                reliable_fixed=Read(binding,"ReliableFixed"),c_type=Read(binding,"CType"),encoded_scale=Read(binding,"EncodedScale"),wire_scale=Read(binding,"WireScale")};
        }).ToArray();
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> result){Before(c,"ExecuteReader");return result;}
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> result,CancellationToken token=default){Before(c,"ExecuteReaderAsync");return ValueTask.FromResult(result);}
    public override DbDataReader ReaderExecuted(DbCommand c,CommandExecutedEventData e,DbDataReader reader){Complete(c,"ExecuteReader",reader);return reader;}
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c,CommandExecutedEventData e,DbDataReader reader,CancellationToken token=default){Complete(c,"ExecuteReaderAsync",reader);return ValueTask.FromResult(reader);}
    public override InterceptionResult<int> NonQueryExecuting(DbCommand c,CommandEventData e,InterceptionResult<int> result){Before(c,"ExecuteNonQuery");return result;}
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<int> result,CancellationToken token=default){Before(c,"ExecuteNonQueryAsync");return ValueTask.FromResult(result);}
    public override int NonQueryExecuted(DbCommand c,CommandExecutedEventData e,int result){Complete(c,"ExecuteNonQuery");return result;}
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c,CommandExecutedEventData e,int result,CancellationToken token=default){Complete(c,"ExecuteNonQueryAsync");return ValueTask.FromResult(result);}
    public override void CommandFailed(DbCommand command,CommandErrorEventData data)
    {Commands.Add(new(){["sequence"]=Commands.Count,["stage"]=Stage,["status"]="failed",["kind"]=Kind(command.CommandText),["command_id"]=Id(command),["statement_id"]=StatementId(command),["error"]=SafeEvidence.Safe(data.Exception)});if(ReferenceEquals(Program.ActiveCommand,command))Program.ActiveCommand=null;}
    public override Task CommandFailedAsync(DbCommand command,CommandErrorEventData data,CancellationToken token=default){CommandFailed(command,data);return Task.CompletedTask;}
}

internal sealed class DesignConnectionAudit(CandidateGuard guard) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection,ConnectionEventData data,InterceptionResult result)
    {Program.Require(!Program.Current.DesignTimeOnly,DesignCode.UnexpectedWrite);return result;}
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,ConnectionEventData data,InterceptionResult result,CancellationToken token=default)
    {Program.Require(!Program.Current.DesignTimeOnly,DesignCode.UnexpectedWrite);return ValueTask.FromResult(result);}
    public override void ConnectionOpened(DbConnection connection,ConnectionEndEventData data)=>guard.VerifyOpenedConnection(connection);
    public override Task ConnectionOpenedAsync(DbConnection connection,ConnectionEndEventData data,CancellationToken token=default){guard.VerifyOpenedConnection(connection);return Task.CompletedTask;}
}
