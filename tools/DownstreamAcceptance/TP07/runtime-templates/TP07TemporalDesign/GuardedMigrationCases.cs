using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using TP07.Runtime;
using TP07LiteralProbe;
using W.Dm;

namespace TP07.TemporalDesign;

internal static class GuardedMigrationCases
{
    internal const string BaselineId="20261009000100_Baseline6",UpgradeId="20261009000200_Upgrade7";
    private static readonly HashSet<string> controls=[];
    private static readonly long Ordinary=new DateTime(2026,7,23,14,15,16,DateTimeKind.Unspecified).Ticks;
    internal static bool GuardControlsAccepted(string kind)=>controls.Contains(kind);
    internal static async Task Run()
    {
        await ScopeControls();
        if(Program.Current.Guard.Profile=="tls")
        {
            Program.Current.Report["R06_full_seven_profile_applicability"]=new{applicable=false,reason="typed_seven_profile_closed_no_literal_bypass"};
            Program.Current.Report["seven_closed_boundary_prerequisite"]=new{source="same_candidate_E_and_TP06",requires_independent_evidence_join=true,observed_by_this_D_group=false};
            foreach(string kind in new[]{"dt","dto"})await TlsSix(kind);
            return;
        }
        foreach(string kind in new[]{"dt","dto"})await Fixture(kind);
    }

    private static async Task Fixture(string kind)
    {
        var state=Program.Current;var owned=state.Owned!;
        var table=owned.RegisterDesignTable("R06-UP-"+kind,kind=="dt"?DesignObjectShape.GuardedDt:DesignObjectShape.GuardedDto,6);
        var history=owned.RegisterDesignTable("R06-HISTORY-"+kind,DesignObjectShape.History);
        string defaultSql=GenerateDefault(kind);
        state.Spec=new(kind,table.Table,history.Table,table.PrimaryKey,table.Index!,table.Check!,7,defaultSql);
        using(var context=Program.Context())VerifyMigrationIds(context);
        await CreateHistory(history);
        using(var context=Program.Context())
        {
            owned.BeginExternalCreate(table);
            try{await context.GetService<IMigrator>().MigrateAsync(BaselineId,Program.Token);}
            catch(Exception error){owned.FailExternalCreate(table,error);throw;}
            owned.ConfirmExternalCreate(table);
        }
        await Insert(table.Table,1,kind,Ordinary+1_234_560,6,kind=="dto"?330:0);
        await Insert(table.Table,2,kind,null,6,0);
        using(var c=state.Guard.OpenTestConnection())await Program.Execute(c,$"INSERT INTO {Program.Quote(table.Table)}(\"ID\") VALUES(3)");
        var before6=await Snapshot(table,history);
        await Program.Case("R06-UP-"+kind,async record=>
        {
            owned.BeginExternalAlter(table,7);
            using var context=Program.Context();await context.GetService<IMigrator>().MigrateAsync(UpgradeId,Program.Token);
            owned.ConfirmExternalAlter(table,7);
            var after7=await Snapshot(table,history);
            Preserved(before6,after7,allowScale:true,allowHistory:true);
            Program.Require(after7.ColumnScale==7&&after7.HistoryIds.SequenceEqual(new[]{BaselineId,UpgradeId}),DesignCode.UnexpectedHistory);
            record["before"]=before6;record["after"]=after7;record["ddl_rollback_claim"]=false;
        });
        var expression=owned.RegisterDesignTable("DOWN-G00-expression-"+kind,kind=="dt"?DesignObjectShape.GuardedDt:DesignObjectShape.GuardedDto,7);
        await CreateControlTable(expression,kind,defaultSql);
        await ExpressionControls(expression,kind);
        await ExtremeControls(expression,kind);
        await GuardControls(expression,kind);
        controls.Add(kind);

        await Insert(table.Table,900,kind,Ordinary+1_234_567,7,kind=="dto"?330:0);
        var refusalBefore=await Snapshot(table,history);
        await Program.Case("DOWN-G03-refusal-"+kind,async record=>
        {
            await Preflight(table,history,refusalBefore);
            using var context=Program.Context();var commands=VerifiedDownCommands(context,table);
            record["down_command_order"]=commands.Select((c,i)=>new{ordinal=i,sha256=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(c.CommandText)),c.TransactionSuppressed}).ToArray();
            int commandStart=state.Audit.Commands.Count,frameStart=Program.ProtocolFrames.Count;
            Exception? rejection=null;
            try{await context.GetService<IMigrator>().MigrateAsync(BaselineId,Program.Token);}catch(Exception error){rejection=error;}
            Program.Require(rejection!=null,DesignCode.GuardCode);
            record["guard_error"]=VerifiedGuardError(rejection!,frameStart);record["migration_api"]="IMigrator.MigrateAsync";
            var attempts=state.Audit.Commands.Skip(commandStart).Where(c=>Equals(c.GetValueOrDefault("status"),"attempted")).ToArray();
            RequireFailureStopped(attempts);
            var after=await Snapshot(table,history);Preserved(refusalBefore,after,allowScale:false,allowHistory:false);
            Program.Require(after.ColumnScale==7&&after.Rows.Single(r=>r.Id==900).Ticks==Ordinary+1_234_567,DesignCode.ProtectedStateChanged);
            record["before"]=refusalBefore;record["after"]=after;record["subsequent_alter_history_mutations"]=0;
        });
        await Program.Case("DOWN-G04-success-"+kind,async record=>
        {
            using(var c=state.Guard.OpenTestConnection())
                Program.Require(await Program.Execute(c,$"DELETE FROM {Program.Quote(table.Table)} WHERE \"ID\"=900")==1,DesignCode.UnexpectedWrite);
            var remaining=await Snapshot(table,history);Program.Require(remaining.Rows.All(r=>r.IsNull||r.Ticks!.Value%10==0),DesignCode.GuardControl);
            await Preflight(table,history,remaining);
            using var context=Program.Context();var commands=VerifiedDownCommands(context,table);
            record["down_command_order"]=commands.Select((c,i)=>new{ordinal=i,sha256=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(c.CommandText)),c.TransactionSuppressed}).ToArray();
            owned.BeginExternalAlter(table,6);
            int start=state.Audit.Commands.Count;
            await context.GetService<IMigrator>().MigrateAsync(BaselineId,Program.Token);owned.ConfirmExternalAlter(table,6);
            var attempts=state.Audit.Commands.Skip(start).Where(c=>Equals(c.GetValueOrDefault("status"),"attempted")).ToArray();
            int guard=Array.FindIndex(attempts,c=>Equals(c.GetValueOrDefault("kind"),"guard"));int alter=Array.FindIndex(attempts,c=>Equals(c.GetValueOrDefault("kind"),"alter"));
            Program.Require(guard>=0&&alter>guard&&!attempts.Take(guard).Any(Mutation),DesignCode.GuardOrder);
            var after=await Snapshot(table,history);Preserved(remaining,after,allowScale:true,allowHistory:true);
            Program.Require(after.ColumnScale==6&&after.HistoryIds.SequenceEqual(new[]{BaselineId}),DesignCode.UnexpectedHistory);
            record["explicit_deleted_owned_row"]=900;record["before"]=remaining;record["after"]=after;record["ddl_rollback_claim"]=false;
        });
    }

    private static async Task TlsSix(string kind)
    {
        await Program.Case("R06-TLS6-"+kind,async record=>
        {
            var state=Program.Current;var owned=state.Owned!;
            var table=owned.RegisterDesignTable("R06-TLS6-"+kind,kind=="dt"?DesignObjectShape.GuardedDt:DesignObjectShape.GuardedDto,6);
            var history=owned.RegisterDesignTable("R06-TLS6-HISTORY-"+kind,DesignObjectShape.History);
            state.Spec=new(kind,table.Table,history.Table,table.PrimaryKey,table.Index!,table.Check!,7,GenerateDefault(kind));
            using(var context=Program.Context())VerifyMigrationIds(context);
            await CreateHistory(history);owned.BeginExternalCreate(table);
            try{using var context=Program.Context();await context.GetService<IMigrator>().MigrateAsync(BaselineId,Program.Token);}
            catch(Exception error){owned.FailExternalCreate(table,error);throw;}
            owned.ConfirmExternalCreate(table);
            await Insert(table.Table,1,kind,Ordinary+1_234_560,6,kind=="dto"?330:0);await Insert(table.Table,2,kind,null,6,0);
            using(var c=state.Guard.OpenTestConnection())Program.Require(await Program.Execute(c,$"INSERT INTO {Program.Quote(table.Table)}(\"ID\") VALUES(3)")==1,DesignCode.UnexpectedWrite);
            var before=await Snapshot(table,history);var fresh=await Snapshot(table,history);Preserved(before,fresh,false,false);
            Program.Require(fresh.ColumnScale==6&&fresh.HistoryIds.SequenceEqual([BaselineId])&&fresh.Rows.Length==3
                &&fresh.Rows.Single(r=>r.Id==2).IsNull
                &&fresh.Rows.Where(r=>r.Id is 1 or 3).All(r=>r.Ticks==Ordinary+1_234_560&&r.Offset==(kind=="dto"?(int?)330:null)),DesignCode.ProtectedStateChanged);
            record["before"]=before;record["fresh"]=fresh;record["migration_api"]="IMigrator.MigrateAsync_Baseline6_only";
            record["seven_write_attempted"]=false;record["Up_Down_seven_attempted"]=false;
        });
    }

    internal static string GenerateDefault(string kind)
    {
        // The same whole-micro SQL is carried unchanged by both AlterColumn directions.
        string store=kind=="dt"?"TIMESTAMP(6)":"DATETIME(6) WITH TIME ZONE";
        var guard=Program.Current.Guard;
        using var context=new DefaultMappingContext(guard);
        var mapping=context.GetService<IRelationalTypeMappingSource>().FindMapping(store)??throw new DesignFailure(DesignCode.SchemaFacet);
        object value=kind=="dt"?(object)new DateTime(Ordinary+1_234_560,DateTimeKind.Unspecified):new DateTimeOffset(new DateTime(Ordinary+1_234_560,DateTimeKind.Unspecified),TimeSpan.FromMinutes(330));
        return mapping.GenerateSqlLiteral(value);
    }
    private static void VerifyMigrationIds(DesignContext context)
    {
        var assembly=context.GetService<IMigrationsAssembly>();
        Program.Require(assembly.FindMigrationId(BaselineId)==BaselineId&&assembly.FindMigrationId("Baseline6")==BaselineId
            &&assembly.FindMigrationId(UpgradeId)==UpgradeId&&assembly.FindMigrationId("Upgrade7")==UpgradeId,DesignCode.UnexpectedHistory);
        Program.Current.Report["migration_id_resolution"]=new{baseline=BaselineId,upgrade=UpgradeId,source="actual_IMigrationsAssembly_FindMigrationId_full_ID_and_name"};
    }
    private sealed class DefaultMappingContext(CandidateGuard guard):DbContext
    {protected override void OnConfiguring(DbContextOptionsBuilder builder)=>builder.UseDameng(guard.BuildConnectionString()).EnableSensitiveDataLogging(false).EnableDetailedErrors(false);}
    private static async Task CreateHistory(OwnedObjects.Entry entry)
    {
        Program.Current.Owned!.BeginExternalCreate(entry);using var c=Program.Current.Guard.OpenTestConnection();
        try{await Program.Execute(c,$"CREATE TABLE {Program.Quote(entry.Table)}(\"MigrationId\" VARCHAR(150) NOT NULL,\"ProductVersion\" VARCHAR(32) NOT NULL,CONSTRAINT {Program.Quote(entry.PrimaryKey)} PRIMARY KEY(\"MigrationId\"))");}
        catch(Exception error){Program.Current.Owned!.FailExternalCreate(entry,error);throw;}
        Program.Current.Owned!.ConfirmExternalCreate(entry);
    }
    private static async Task CreateControlTable(OwnedObjects.Entry entry,string kind,string defaultSql)
    {
        Program.Current.Owned!.BeginExternalCreate(entry);using var c=Program.Current.Guard.OpenTestConnection();string store=kind=="dt"?"TIMESTAMP(7)":"DATETIME(7) WITH TIME ZONE";
        try
        {
            await Program.Execute(c,$"CREATE TABLE {Program.Quote(entry.Table)}(\"ID\" INT NOT NULL,\"V\" {store} DEFAULT {defaultSql},CONSTRAINT {Program.Quote(entry.PrimaryKey)} PRIMARY KEY(\"ID\"),CONSTRAINT {Program.Quote(entry.Check!)} CHECK(\"ID\">0))");
            await Program.Execute(c,$"CREATE INDEX {Program.Quote(entry.Index!)} ON {Program.Quote(entry.Table)}(\"V\")");
        }catch(Exception error){Program.Current.Owned!.FailExternalCreate(entry,error);throw;}
        Program.Current.Owned!.ConfirmExternalCreate(entry);
    }
    private static async Task Insert(string table,int row,string kind,long? ticks,int scale,int offset)
    {
        using var c=Program.Current.Guard.OpenTestConnection();await using var command=CandidateGuard.Command(c,$"INSERT INTO {Program.Quote(table)}(\"ID\",\"V\") VALUES({row},:v)");
        var p=command.CreateParameter();p.ParameterName="v";p.DbType=kind=="dt"?DbType.DateTime2:DbType.DateTimeOffset;p.Precision=0;p.Scale=(byte)scale;
        p.Value=ticks.HasValue?(kind=="dt"?(object)new DateTime(ticks.Value,DateTimeKind.Unspecified):new DateTimeOffset(new DateTime(ticks.Value,DateTimeKind.Unspecified),TimeSpan.FromMinutes(offset))):DBNull.Value;command.Parameters.Add(p);
        Program.Current.Guard.VerifyObject(p,"parameter");Program.Current.Audit.StartRaw(command,"ExecuteNonQueryAsync");
        try
        {
            int affected=await command.ExecuteNonQueryAsync(Program.Token);
            Program.Current.Audit.EndRaw(command,"ExecuteNonQueryAsync");Program.Require(affected==1,DesignCode.UnexpectedWrite);
        }
        catch(Exception error){Program.Current.Audit.FailRaw(command,error);throw;}
        finally{Program.ActiveCommand=null;}
    }
    private static IReadOnlyList<MigrationCommand> VerifiedDownCommands(DesignContext context,OwnedObjects.Entry table)
    {
        var spec=Program.Current.Spec!;var assembly=context.GetService<IMigrationsAssembly>();
        var migration=assembly.CreateMigration(assembly.Migrations[UpgradeId],context.Database.ProviderName!);
        var operations=migration.DownOperations;
        Program.Require(operations.Count==2&&operations[0] is SqlOperation{SuppressTransaction:true}
            &&operations[1] is AlterColumnOperation alter&&alter.Table==table.Table&&alter.Schema==CandidateGuard.User
            &&alter.Name=="V"&&alter.ColumnType==spec.StoreType(6)&&alter.OldColumn.ColumnType==spec.StoreType(7)
            &&alter.IsNullable&&alter.OldColumn.IsNullable&&alter.DefaultValueSql==spec.DefaultValueSql
            &&alter.OldColumn.DefaultValueSql==spec.DefaultValueSql,DesignCode.GuardOrder);
        var commands=context.GetService<IMigrationsSqlGenerator>().Generate(operations,context.GetService<IDesignTimeModel>().Model);
        MigrationGuardSql.VerifyCommands(commands,table.Table,Program.Current.Tables);return commands;
    }
    private static async Task ExpressionControls(OwnedObjects.Entry entry,string kind)
    {
        await Program.Case("DOWN-G00-expression-"+kind,async record=>
        {
            var vectors=Enumerable.Range(0,10).Select(t=>Ordinary+1_234_560+t).Concat([Ordinary,Ordinary+1]).ToArray();var observed=new List<object>();
            for(int i=0;i<vectors.Length;i++)
            {
                await Insert(entry.Table,100+i,kind,vectors[i],7,kind=="dto"?330:0);
                var oracle=LiteralOracle.ReadFresh(Program.Current.Guard,entry.Table,100+i,kind=="dto",7);
                object value=kind=="dt"?(object)new DateTime(vectors[i],DateTimeKind.Unspecified):new DateTimeOffset(new DateTime(vectors[i],DateTimeKind.Unspecified),TimeSpan.FromMinutes(330));
                Program.Require(LiteralOracle.Exact(oracle,value),DesignCode.GuardExpression);
                using var c=Program.Current.Guard.OpenTestConnection();await using var command=CandidateGuard.Command(c,$"SELECT TO_CHAR(\"V\",'FF9'),TO_NUMBER(TO_CHAR(\"V\",'FF9')),MOD(TO_NUMBER(TO_CHAR(\"V\",'FF9')),1000) FROM {Program.Quote(entry.Table)} WHERE \"ID\"={100+i}");
                await using var reader=await command.ExecuteReaderAsync(Program.Token);Program.Require(await reader.ReadAsync(Program.Token),DesignCode.GuardExpression);
                string text=reader.GetString(0);Program.Require(text.Length==9&&text.All(ch=>ch is >= '0' and <= '9'),DesignCode.GuardExpression);
                int n=(int)(vectors[i]%TimeSpan.TicksPerSecond)*100;long parsed=long.Parse(text,CultureInfo.InvariantCulture);decimal number=reader.GetDecimal(1),mod=reader.GetDecimal(2);
                Program.Require(parsed==n&&number==n&&mod==n%1000&&!await reader.ReadAsync(Program.Token),DesignCode.GuardExpression);
                observed.Add(new{row_id=100+i,ticks=vectors[i],ff9=text,integer=number,mod,oracle});
            }
            record["observations"]=observed;
        });
    }
    private static async Task ExtremeControls(OwnedObjects.Entry entry,string kind)
    {
        await Program.Case("DOWN-G01-extremes-"+kind,async record=>
        {
            long[] ticks=[DateTime.MinValue.Ticks,DateTime.MinValue.Ticks+1,DateTime.MaxValue.Ticks,DateTime.MaxValue.Ticks-1,DateTime.MaxValue.Ticks-9];var observations=new List<object>();
            for(int i=0;i<ticks.Length;i++)
            {
                using(var clean=Program.Current.Guard.OpenTestConnection())await Program.Execute(clean,$"DELETE FROM {Program.Quote(entry.Table)}");
                await Insert(entry.Table,200+i,kind,ticks[i],7,0);var oracle=LiteralOracle.ReadFresh(Program.Current.Guard,entry.Table,200+i,kind=="dto",7);
                object expected=kind=="dt"?(object)new DateTime(ticks[i],DateTimeKind.Unspecified):new DateTimeOffset(new DateTime(ticks[i],DateTimeKind.Unspecified),TimeSpan.Zero);
                Program.Require(LiteralOracle.Exact(oracle,expected),DesignCode.GuardExpression);
                using var c=Program.Current.Guard.OpenTestConnection();string ff=Convert.ToString(await Program.Scalar(c,$"SELECT TO_CHAR(\"V\",'FF9') FROM {Program.Quote(entry.Table)} WHERE \"ID\"={200+i}"),CultureInfo.InvariantCulture)!;
                Program.Require(ff.Length==9&&ff.All(ch=>ch is >= '0' and <= '9')&&long.Parse(ff,CultureInfo.InvariantCulture)==ticks[i]%TimeSpan.TicksPerSecond*100,DesignCode.GuardExpression);
                bool allowed=ticks[i]%10==0;int first=Program.ProtocolFrames.Count;Exception? rejected=null;
                try{await Program.Execute(c,MigrationGuardSql.ForOwnedTable(entry.Table,Program.Current.Tables));}catch(Exception error){rejected=error;}
                if(allowed)Program.Require(rejected==null,DesignCode.GuardExpression);else{Program.Require(rejected!=null,DesignCode.GuardCode);VerifiedGuardError(rejected!,first);}
                observations.Add(new{row_id=200+i,ticks=ticks[i],ff9=ff,downgrade_allowed=allowed,actual_guard_rejected=rejected!=null,oracle});
            }
            record["observations"]=observations;
        });
    }
    private static async Task GuardControls(OwnedObjects.Entry entry,string kind)
    {
        await Program.Case("DOWN-G02-control-"+kind,async record=>
        {
            using var c=Program.Current.Guard.OpenTestConnection();string guard=MigrationGuardSql.ForOwnedTable(entry.Table,Program.Current.Tables);
            var controls=new List<Dictionary<string,object?>>();record["controls"]=controls;
            async Task Observe(string name,int[] rows,bool reject)
            {
                var control=new Dictionary<string,object?>{["control_id"]=name,["table"]=entry.Table,["row_ids"]=rows,
                    ["before"]=ControlSnapshot(entry,kind,rows)};controls.Add(control);
                int commands=Program.Current.Audit.Commands.Count,frames=Program.ProtocolFrames.Count;Exception? error=null;int? affected=null;
                try{affected=await Program.Execute(c,guard);}catch(Exception failure){error=failure;}
                control["guard_call"]=new{api="ExecuteNonQueryAsync",sql_sha256=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(guard)),
                    command_start=commands,command_end=Program.Current.Audit.Commands.Count,frame_start=frames,frame_end=Program.ProtocolFrames.Count,
                    affected_rows=affected,outcome=error is null?"returned":"rejected",error=error is null?null:Program.Error(error)};
                if(reject){Program.Require(error is not null,DesignCode.GuardControl);control["verified_error"]=VerifiedGuardError(error!,frames);record["verified_error"]=control["verified_error"];}
                else if(error is not null)throw error;
                control["after"]=ControlSnapshot(entry,kind,rows);
            }
            await Program.Execute(c,$"DELETE FROM {Program.Quote(entry.Table)}");await Observe("empty",[],false);
            await Insert(entry.Table,1,kind,null,7,0);await Observe("NULL",[1],false);
            await Insert(entry.Table,2,kind,Ordinary+1_234_560,7,kind=="dto"?330:0);await Observe("whole_micro",[1,2],false);
            await Insert(entry.Table,3,kind,Ordinary+1_234_567,7,kind=="dto"?330:0);
            await Observe("nonmicro",[1,2,3],true);
            record["whole_micro_NULL_empty_passed"]=true;record["literal_seed_used"]=false;
        });
    }
    private static object ControlSnapshot(OwnedObjects.Entry entry,string kind,int[] rows)
    {
        using var connection=Program.Current.Guard.OpenTestConnection();long count=Convert.ToInt64(CandidateGuard.Scalar(connection,$"SELECT COUNT(*) FROM {Program.Quote(entry.Table)}"),CultureInfo.InvariantCulture);
        var oracles=rows.Select(id=>CaptureRow(entry.Table,id,kind=="dto",7)).ToArray();
        return new{row_count=count,identity=Program.Current.Guard.VerifyOpenedConnection(connection),row_oracles=oracles};
    }
    private static object VerifiedGuardError(Exception error,int firstFrame)
    {
        while(error is System.Reflection.TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
        int? number=error.GetType().GetProperty("Number")?.GetValue(error) as int?;
        bool verified=error.GetType().GetProperty("HasVerifiedServerResponse",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)?.GetValue(error) is true;
        var frames=Program.ProtocolFrames.Skip(firstFrame).Where(f=>f.SqlCode==MigrationGuardSql.NonMicroError).ToArray();
        Program.Require(error is DmException&&number==MigrationGuardSql.NonMicroError&&verified&&frames.Length==1&&!Program.ObserverFailed
            &&frames[0].CommandId.HasValue&&frames[0].RequestOpcode is 5 or 6 or 13&&frames[0].ResponseOpcode==frames[0].RequestOpcode,DesignCode.GuardCode);
        return new{type=error.GetType().FullName,number,verified_server_response=verified,response=frames[0],source="physical_channel_receive_opcode",stage=Program.Current.Audit.Stage};
    }
    private static bool Mutation(Dictionary<string,object?> command)=>command.GetValueOrDefault("kind") is "alter" or "history_mutation" or "ddl";
    private static void RequireFailureStopped(Dictionary<string,object?>[] attempts)
    {
        int guard=Array.FindIndex(attempts,c=>Equals(c.GetValueOrDefault("kind"),"guard"));
        Program.Require(guard>=0&&attempts.Count(c=>Equals(c.GetValueOrDefault("kind"),"guard"))==1
            &&!attempts.Take(guard).Any(Mutation)&&!attempts.Skip(guard+1).Any(Mutation),DesignCode.GuardOrder);
    }
    private static void RequirePreflight(bool ownerVerified,bool historyVerified,int scale,string[] historyIds)
    {
        Program.Require(ownerVerified&&historyVerified&&scale==7&&historyIds.SequenceEqual([BaselineId,UpgradeId]),DesignCode.GuardNotAccepted);
    }
    private static async Task Preflight(OwnedObjects.Entry table,OwnedObjects.Entry history,ProtectedState actual)
    {
        Program.Current.Owned!.Preflight(table);Program.Current.Owned!.Preflight(history);
        using var c=Program.Current.Guard.OpenTestConnection();
        async Task<bool> Marker(OwnedObjects.Entry entry)
        {
            await using var cmd=CandidateGuard.Command(c,"SELECT COMMENTS FROM ALL_TAB_COMMENTS WHERE OWNER=:s AND TABLE_NAME=:t AND TABLE_TYPE='TABLE'");
            foreach(var pair in new[]{("s",CandidateGuard.User),("t",entry.Table)}){var p=cmd.CreateParameter();p.ParameterName=pair.Item1;p.DbType=DbType.String;p.Value=pair.Item2;cmd.Parameters.Add(p);}
            await using var reader=await cmd.ExecuteReaderAsync(Program.Token);
            return await reader.ReadAsync(Program.Token)&&!reader.IsDBNull(0)&&reader.GetString(0)=="TP07_"+entry.Nonce&&!await reader.ReadAsync(Program.Token);
        }
        RequirePreflight(table.CreationState=="confirmed"&&table.Scale==7&&await Marker(table),history.CreationState=="confirmed"&&await Marker(history),actual.ColumnScale,actual.HistoryIds);
        Program.Require(actual.HistoryRows.All(row=>row.ProductVersion=="10.0.12"),DesignCode.UnexpectedHistory);
    }
    private sealed record SavedRow(int Id,bool IsNull,long? Ticks,int? Offset);
    private sealed record RowOracle(int Id,string Table,string Column,TemporalOracleReceipt Oracle,Dictionary<string,object?> ReadEvidence);
    private static RowOracle CaptureRow(string table,int id,bool isOffset,int scale)
    {
        var evidence=new Dictionary<string,object?>();var receipt=LiteralOracle.ReadFresh(Program.Current.Guard,table,id,isOffset,scale,partialSink:evidence);
        return new(id,table,"V",receipt,evidence);
    }
    private sealed record HistoryRow(string MigrationId,string ProductVersion);
    private sealed record ProtectedState(int ColumnScale,string? Default,string Columns,string ColumnsExceptScale,string Indexes,string Constraints,
        string HistoryColumns,string HistoryConstraints,HistoryRow[] HistoryRows,SavedRow[] Rows,RowOracle[] RowOracles)
    { public string[] HistoryIds=>HistoryRows.Select(row=>row.MigrationId).ToArray(); }
    private static async Task<ProtectedState> Snapshot(OwnedObjects.Entry entry,OwnedObjects.Entry history)
    {
        using var c=Program.Current.Guard.OpenTestConnection();var columns=await CatalogRows(c,"SELECT COLUMN_NAME,DATA_TYPE,DATA_PRECISION,DATA_SCALE,DATA_DEFAULT,NULLABLE FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t ORDER BY COLUMN_ID",entry.Table);
        var indexes=await CatalogRows(c,"SELECT I.INDEX_NAME,I.UNIQUENESS,C.COLUMN_NAME,C.COLUMN_POSITION FROM USER_INDEXES I JOIN USER_IND_COLUMNS C ON I.INDEX_NAME=C.INDEX_NAME WHERE I.TABLE_NAME=:t ORDER BY I.INDEX_NAME,C.COLUMN_POSITION",entry.Table);
        var constraints=await CatalogRows(c,"SELECT CONSTRAINT_NAME,CONSTRAINT_TYPE,STATUS,VALIDATED,SEARCH_CONDITION FROM USER_CONSTRAINTS WHERE TABLE_NAME=:t ORDER BY CONSTRAINT_NAME",entry.Table);
        var v=columns.Single(row=>(string?)row[0]=="V");
        // R06 keeps its CHECK. A scaffold factory rejects CHECK by contract, so this lane reads
        // only the exact owned table's catalog and cross-checks the scale against fresh raw rows.
        Program.Require(columns.Count==2&&columns.All(row=>row[0] is "ID" or "V")
            &&Program.Current.Spec!.Table==entry.Table&&Program.Current.Spec.History==history.Table,DesignCode.SchemaFacet);
        string actualType=Convert.ToString(v[1],CultureInfo.InvariantCulture)?.ToUpperInvariant()??"";
        bool isOffset=entry.Shape=="GuardedDto";
        Program.Require(entry.Shape is "GuardedDt" or "GuardedDto"
            &&(isOffset?actualType is "DATETIME WITH TIME ZONE" or "TIMESTAMP WITH TIME ZONE":actualType is "DATETIME" or "TIMESTAMP")
            &&v[3] is not null&&(string?)v[5]=="Y",DesignCode.SchemaFacet);
        decimal rawScale=Convert.ToDecimal(v[3],CultureInfo.InvariantCulture);
        Program.Require(rawScale is 6m or 7m,DesignCode.SchemaFacet);int actualScale=(int)rawScale;
        Program.Require(actualScale==entry.Scale&&Program.Current.Spec!.Kind==(isOffset?"dto":"dt"),DesignCode.SchemaFacet);
        Program.Require(constraints.Any(row=>(string?)row[0]==entry.Check&&(string?)row[1]=="C")
            &&constraints.Any(row=>(string?)row[0]==entry.PrimaryKey&&(string?)row[1]=="P")
            &&indexes.Any(row=>(string?)row[0]==entry.Index&&(string?)row[2]=="V"),DesignCode.ProtectedStateChanged);
        var rowIds=new List<int>();await using(var cmd=CandidateGuard.Command(c,$"SELECT \"ID\" FROM {Program.Quote(entry.Table)} ORDER BY \"ID\""))await using(var reader=await cmd.ExecuteReaderAsync(Program.Token))while(await reader.ReadAsync(Program.Token))rowIds.Add(reader.GetInt32(0));
        var oracles=rowIds.Select(id=>CaptureRow(entry.Table,id,isOffset,actualScale)).ToArray();
        var rows=oracles.Select(row=>{var receipt=row.Oracle;
            Program.Require(receipt.Rows==1&&JsonSerializer.SerializeToElement(receipt.Metadata,SafeEvidence.Json).GetProperty("scale").GetInt32()==actualScale,DesignCode.SchemaFacet);
            return new SavedRow(row.Id,receipt.IsNull,receipt.LocalTicks,receipt.OffsetMinutes);}).ToArray();
        var historyRows=new List<HistoryRow>();await using(var cmd=CandidateGuard.Command(c,$"SELECT \"MigrationId\",\"ProductVersion\" FROM {Program.Quote(history.Table)} ORDER BY \"MigrationId\""))await using(var reader=await cmd.ExecuteReaderAsync(Program.Token))while(await reader.ReadAsync(Program.Token))
        {Program.Require(historyRows.Count<3,DesignCode.UnexpectedHistory);historyRows.Add(new(reader.GetString(0),reader.GetString(1)));}
        Program.Require(historyRows.All(row=>row.ProductVersion=="10.0.12"),DesignCode.UnexpectedHistory);
        var historyColumns=await CatalogRows(c,"SELECT COLUMN_NAME,DATA_TYPE,DATA_PRECISION,DATA_SCALE,DATA_DEFAULT,NULLABLE FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t ORDER BY COLUMN_ID",history.Table);
        var historyConstraints=await CatalogRows(c,"SELECT CONSTRAINT_NAME,CONSTRAINT_TYPE,STATUS,VALIDATED,SEARCH_CONDITION FROM USER_CONSTRAINTS WHERE TABLE_NAME=:t ORDER BY CONSTRAINT_NAME",history.Table);
        var normalized=columns.Select(row=>{var copy=(object?[])row.Clone();if((string?)copy[0]=="V")copy[3]="checked_fractional_scale";return copy;}).ToArray();
        return new(actualScale,v[4] as string,JsonSerializer.Serialize(columns,SafeEvidence.Json),JsonSerializer.Serialize(normalized,SafeEvidence.Json),
            JsonSerializer.Serialize(indexes,SafeEvidence.Json),JsonSerializer.Serialize(constraints,SafeEvidence.Json),JsonSerializer.Serialize(historyColumns,SafeEvidence.Json),
            JsonSerializer.Serialize(historyConstraints,SafeEvidence.Json),historyRows.ToArray(),rows,oracles);
    }
    private static async Task<List<object?[]>> CatalogRows(DbConnection c,string sql,string table)
    {
        await using var cmd=CandidateGuard.Command(c,sql);var p=cmd.CreateParameter();p.ParameterName="t";p.DbType=DbType.String;p.Value=table;cmd.Parameters.Add(p);
        await using var reader=await cmd.ExecuteReaderAsync(Program.Token);var rows=new List<object?[]>();while(await reader.ReadAsync(Program.Token))
        {
            Program.Require(rows.Count<32,DesignCode.SchemaFacet);var row=new object?[reader.FieldCount];for(int i=0;i<row.Length;i++)row[i]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(row);
        }
        return rows;
    }
    private static void Preserved(ProtectedState before,ProtectedState after,bool allowScale,bool allowHistory)
    {
        Program.Require(before.Default==after.Default&&before.Indexes==after.Indexes&&before.Constraints==after.Constraints
            &&before.ColumnsExceptScale==after.ColumnsExceptScale&&before.HistoryColumns==after.HistoryColumns
            &&before.HistoryConstraints==after.HistoryConstraints&&before.Rows.SequenceEqual(after.Rows),DesignCode.ProtectedStateChanged);
        if(!allowScale)Program.Require(before.ColumnScale==after.ColumnScale&&before.Columns==after.Columns,DesignCode.ProtectedStateChanged);
        if(!allowHistory)Program.Require(before.HistoryRows.SequenceEqual(after.HistoryRows),DesignCode.ProtectedStateChanged);
        else Program.Require(before.HistoryRows.Where(row=>after.HistoryIds.Contains(row.MigrationId,StringComparer.Ordinal))
            .All(row=>after.HistoryRows.Contains(row)),DesignCode.ProtectedStateChanged);
    }
    private static Task ScopeControls()
    {
        return Program.Case("DOWN-G05-scope",record=>
        {
            void Reject(Action call){bool failed=false;try{call();}catch(InvalidOperationException){failed=true;}Program.Require(failed,DesignCode.OwnedSqlScope);}
            Reject(()=>MigrationGuardSql.ForOwnedTable("BUSINESS_TABLE",new HashSet<string>()));
            Reject(()=>MigrationGuardSql.ForOwnedTable("TPL_"+new string('A',24),new HashSet<string>()));
            Program.Require(!GuardControlsAccepted("dt")&&!GuardControlsAccepted("dto"),DesignCode.GuardNotAccepted);
            string table="TPL_"+new string('A',24);var allowed=new HashSet<string>{table};string guard=MigrationGuardSql.ForOwnedTable(table,allowed);
            var prior=Program.Current.Spec;
            try
            {
                Program.Current.Spec=new("dt",table,"TPL_"+new string('B',24),"TP07PK_"+new string('A',24),"TP07IX_"+new string('A',24),"TP07CK_"+new string('A',24),7,"TIMESTAMP '2026-07-23 14:15:16.123456'");
                using var context=Program.Context();var generator=context.GetService<IMigrationsSqlGenerator>();
                VerifyMigrationIds(context);
                var operations=new MigrationOperation[]{new SqlOperation{Sql=guard,SuppressTransaction=true},new AlterColumnOperation{Name="V",Table=table,Schema=CandidateGuard.User,ClrType=typeof(DateTime),ColumnType="TIMESTAMP(6)",IsNullable=true,
                    OldColumn=new AddColumnOperation{Name="V",Table=table,Schema=CandidateGuard.User,ClrType=typeof(DateTime),ColumnType="TIMESTAMP(7)",IsNullable=true}}};
                var commands=generator.Generate(operations,context.GetService<IDesignTimeModel>().Model);MigrationGuardSql.VerifyCommands(commands,table,allowed);
                Reject(()=>MigrationGuardSql.VerifyCommands(commands.Skip(1).ToArray(),table,allowed));
                Reject(()=>MigrationGuardSql.VerifyCommands(commands.Reverse().ToArray(),table,allowed));
                var wrong=generator.Generate([new SqlOperation{Sql=guard.Replace("\"WDM_PROVIDER_TEST\".","\"OTHER_SCHEMA\".",StringComparison.Ordinal),SuppressTransaction=true},operations[1]],context.GetService<IDesignTimeModel>().Model);
                Reject(()=>MigrationGuardSql.VerifyCommands(wrong,table,allowed));
                Reject(()=>RequirePreflight(false,true,7,[BaselineId,UpgradeId]));
                Reject(()=>RequirePreflight(true,false,7,[BaselineId,UpgradeId]));
                Reject(()=>RequireFailureStopped([new(){["kind"]="guard"},new(){["kind"]="alter"}]));
                var migration=context.GetService<IMigrationsAssembly>().CreateMigration(context.GetService<IMigrationsAssembly>().Migrations[UpgradeId],context.Database.ProviderName!);
                Reject(()=>{_ =migration.DownOperations;});
                record["generator_source"]="actual_IMigrationsSqlGenerator";record["generated_command_hashes"]=commands.Select(command=>SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(command.CommandText))).ToArray();
            }
            finally{Program.Current.Spec=prior;}
            record["expected_only"]=true;record["database_accessed"]=false;record["negative_controls"]=new[]{"foreign_object","missing_allowlist","wrong_schema","missing_guard","alter_before_guard","owner_preflight_missing","history_preflight_missing","catch_continue_disallowed","unverified_Down_closed"};return Task.CompletedTask;
        });
    }
}
