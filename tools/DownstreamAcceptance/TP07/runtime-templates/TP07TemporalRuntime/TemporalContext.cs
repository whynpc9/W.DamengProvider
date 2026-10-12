using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace TP07.Runtime;

internal sealed class TemporalContext(ModelSpec spec, CandidateGuard guard, CommandAudit audit, OfflineIoState? offline = null) : DbContext
{
    internal ModelSpec Spec { get; } = spec;
    internal DbSet<TemporalRow> Rows => Set<TemporalRow>();
    internal static ModelSpec DatabaseSpec(ModelSpec runtime) => runtime with
    { Shape = runtime.Shape switch { "default7_fixed6" => "facet6", "DATE_nonmidnight" => "native", _ => runtime.Shape } };

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Offline selection short-circuits before the live environment/TEST/TLS builder.
        optionsBuilder.UseDameng(offline == null ? guard.BuildConnectionString() : OfflineIoState.SyntheticConnectionString())
            .ReplaceService<IModelCacheKeyFactory, TemporalModelCacheKeyFactory>()
            .EnableSensitiveDataLogging(false).EnableDetailedErrors(false);
        if (offline == null) optionsBuilder.AddInterceptors(audit, new RuntimeConnectionAudit(guard, audit));
        else optionsBuilder.AddInterceptors(new OfflineConnectionBlocker(offline), new OfflineCommandBlocker(offline));
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var row = builder.Entity<TemporalRow>();
        row.ToTable(Spec.Table);
        row.HasKey(item => item.Id).HasName(Spec.PrimaryKey);
        row.Property(item => item.Id).HasColumnName("ID").ValueGeneratedNever();
        row.Property(item => item.N).HasColumnName("N").HasColumnType("VARCHAR(32)").HasMaxLength(32).IsUnicode(false);
        if (Spec.Shape == "native")
        {
            row.Ignore(item => item.N);
            row.Ignore(item => item.D); row.Ignore(item => item.Z);
            row.Property(item => item.A).HasColumnName("A").HasColumnType("DATE");
            row.Property(item => item.T).HasColumnName("T").HasColumnType("TIME(6)").HasPrecision(6);
            row.Property(item => item.I).HasColumnName("I").HasColumnType("INTERVAL DAY(9) TO SECOND(6)").HasPrecision(9, 6);
            return;
        }
        row.Ignore(item => item.A); row.Ignore(item => item.T); row.Ignore(item => item.I);
        var dt = row.Property(item => item.D).HasColumnName("D");
        var dto = row.Property(item => item.Z).HasColumnName("Z");
        if (Spec.Shape == "DATE_nonmidnight")
        {
            row.Ignore(item => item.N); row.Ignore(item => item.Z); dt.HasColumnName("A").HasColumnType("DATE"); return;
        }
        if (Spec.Shape == "store6")
        {
            dt.HasColumnType("TIMESTAMP(6)"); dto.HasColumnType("DATETIME(6) WITH TIME ZONE");
        }
        else if (Spec.Shape is not ("default7_fixed6" or "advisory7"))
        {
            dt.HasPrecision(Spec.Scale); dto.HasPrecision(Spec.Scale);
        }
        if (Spec.Shape == "concurrency")
        {
            if (Spec.Kind == "dt") dt.IsConcurrencyToken(); else dto.IsConcurrencyToken();
        }
        if (Spec.Shape == "constant_default")
        {
            RuntimeAssert.Require(Spec.SeedTicks.HasValue && Spec.SeedOffset.HasValue, "model_default_value_missing");
            dt.HasDefaultValue(new DateTime(Spec.SeedTicks!.Value, DateTimeKind.Unspecified)).ValueGeneratedOnAdd();
            dto.HasDefaultValue(new DateTimeOffset(new DateTime(Spec.SeedTicks!.Value, DateTimeKind.Unspecified), TimeSpan.FromMinutes(Spec.SeedOffset!.Value))).ValueGeneratedOnAdd();
        }
        if (Spec.Shape is "sql_default" or "NULL_default" or "clock_default")
        {
            RuntimeAssert.Require(Spec.DefaultD != null && Spec.DefaultZ != null, "model_default_SQL_missing");
            dt.HasDefaultValueSql(Spec.DefaultD!).ValueGeneratedOnAdd(); dto.HasDefaultValueSql(Spec.DefaultZ!).ValueGeneratedOnAdd();
        }
        if (Spec.Shape is "HasData" or "inline" or "update_expression")
        {
            RuntimeAssert.Require(Spec.SeedTicks.HasValue && Spec.SeedOffset.HasValue, "model_seed_value_missing");
            row.HasData(new TemporalRow
            {
                Id = 1, D = new DateTime(Spec.SeedTicks!.Value, DateTimeKind.Unspecified),
                Z = new DateTimeOffset(new DateTime(Spec.SeedTicks!.Value, DateTimeKind.Unspecified), TimeSpan.FromMinutes(Spec.SeedOffset!.Value)), N = "seed"
            });
        }
    }

    internal RelationalTypeMapping Mapping(string property) =>
        this.GetService<IRelationalTypeMappingSource>().FindMapping(Model.FindEntityType(typeof(TemporalRow))!.FindProperty(property)!)
        ?? throw new InvalidOperationException("runtime_mapping_missing");

    internal IReadOnlyList<MigrationCommand> GeneratedCreate(out IReadOnlyList<MigrationOperation> operations)
    {
        IModel model = this.GetService<IDesignTimeModel>().Model;
        operations = this.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        foreach (MigrationOperation operation in operations)
        {
            bool owned = operation switch
            {
                CreateTableOperation table => table.Name == Spec.Table && table.Schema == null
                    && table.ForeignKeys.Count == 0 && table.CheckConstraints.Count == 0 && table.PrimaryKey?.Name == Spec.PrimaryKey,
                InsertDataOperation data => data.Table == Spec.Table && data.Schema == null,
                _ => false
            };
            RuntimeAssert.Require(owned, "generated_operation_outside_owned_table");
        }
        RuntimeAssert.Require(operations.OfType<CreateTableOperation>().Count() == 1, "generated_create_table_count");
        return this.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
    }
}

// These guards are used only by the explicit offline entry. No deliberate open is attempted.
internal sealed class OfflineIoState
{
    internal int SyncOpenAttempts, AsyncOpenAttempts, SyncCommandAttempts, AsyncCommandAttempts;
    private readonly List<(DbConnection Connection, object Route)> connections = [];
    internal static string SyntheticConnectionString() => new W.Dm.DmConnectionStringBuilder
    {
        Server = "127.0.0.1", Port = 1, User = "TP07_OFFLINE_SYNTHETIC", Password = "TP07_OFFLINE_SYNTHETIC",
        Schema = "TP07_OFFLINE_SYNTHETIC", PersistSecurityInfo = false, ConnPooling = false,
        StmtPooling = false, PreparePooling = false, TransportSecurity = W.Dm.DmTransportSecurity.RequireTls
    }.ConnectionString;
    internal void Observe(DbConnection connection, object route)
    {
        RuntimeAssert.Require(connection.State == ConnectionState.Closed, "offline_factory_connection_not_closed");
        connections.Add((connection, route));
    }
    internal object Evidence(bool requireZero = true)
    {
        bool closed = connections.Count > 0 && connections.All(item => item.Connection.State == ConnectionState.Closed);
        if (requireZero)
        {
            RuntimeAssert.Require(SyncOpenAttempts == 0 && AsyncOpenAttempts == 0 && SyncCommandAttempts == 0 && AsyncCommandAttempts == 0,
                "offline_database_attempt_detected");
            RuntimeAssert.Require(closed, "offline_factory_connections_not_closed");
        }
        return new { sync_open_attempts = SyncOpenAttempts, async_open_attempts = AsyncOpenAttempts,
            sync_command_attempts = SyncCommandAttempts, async_command_attempts = AsyncCommandAttempts,
            factory_connection_count = connections.Count, all_factory_connections_closed = closed,
            factory_connections = connections.Select(item => new { actual = item.Route, state = item.Connection.State.ToString() }).ToArray() };
    }
}

internal sealed class OfflineConnectionBlocker(OfflineIoState state) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result)
    { state.SyncOpenAttempts++; throw new RuntimeAssertionException("offline_sync_open_forbidden"); }
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data, InterceptionResult result, CancellationToken token = default)
    { state.AsyncOpenAttempts++; throw new RuntimeAssertionException("offline_async_open_forbidden"); }
}

internal sealed class OfflineCommandBlocker(OfflineIoState state) : DbCommandInterceptor
{
    private void Fail(bool async)
    { if (async) state.AsyncCommandAttempts++; else state.SyncCommandAttempts++; throw new RuntimeAssertionException("offline_command_execution_forbidden"); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
    { Fail(false); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
    { Fail(true); return ValueTask.FromResult(result); }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
    { Fail(false); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
    { Fail(true); return ValueTask.FromResult(result); }
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
    { Fail(false); return result; }
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default)
    { Fail(true); return ValueTask.FromResult(result); }
}

internal sealed class TemporalModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is TemporalContext temporal
        ? (context.GetType(), temporal.Spec.CacheIdentity, designTime)
        : (context.GetType(), designTime);
}
