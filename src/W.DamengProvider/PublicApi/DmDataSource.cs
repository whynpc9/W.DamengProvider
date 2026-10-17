using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Pooling;

namespace W.Dm;

/// <summary>Immutable connection configuration and an independently owned capacity domain.
/// Returned physical sessions are always discarded; no verified session reset is currently available.</summary>
/// <remarks>.NET 6 has no <c>DbDataSource</c>; that target exposes the same typed members directly
/// and cannot be used through <c>DbDataSource</c> or <c>DbProviderFactory.CreateDataSource</c>.</remarks>
#if NET7_0_OR_GREATER
public sealed class DmDataSource : DbDataSource
#else
public sealed class DmDataSource : IDisposable, IAsyncDisposable
#endif
{
    private readonly DmConnectionSettings settings;
    private readonly object publicationGate = new();
    private int disposed;
    internal DmPoolOwner Owner { get; }
    internal DmPoolSnapshot Snapshot => Owner.Snapshot;

    public DmDataSource(string connectionString)
    {
        settings = DmConnectionSettings.Parse(connectionString);
        Owner = new DmPoolOwner(settings.MaxPoolSize, settings.MaxPoolWaiters);
    }

    public static DmDataSource Create(string connectionString) => new(connectionString);

#if NET7_0_OR_GREATER
    public override string ConnectionString => settings.ToConnectionString(includeSecrets: false);
    public new DmConnection CreateConnection() => (DmConnection)base.CreateConnection();
    public new DmConnection OpenConnection() => (DmConnection)base.OpenConnection();
    public new async ValueTask<DmConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        => (DmConnection)await base.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    protected override DbConnection CreateDbConnection() => CreateOwnedConnection();
    protected override DbCommand CreateDbCommand(string commandText = null) => CreateOwnedCommand(commandText);
    protected override DbBatch CreateDbBatch() => CreateOwnedBatch();
    protected override void Dispose(bool disposing)
    {
        Stop();
        base.Dispose(disposing);
    }
    protected override ValueTask DisposeAsyncCore()
    {
        Dispose(true);
        return default;
    }
#else
    public string ConnectionString => settings.ToConnectionString(includeSecrets: false);
    public DmConnection CreateConnection() => CreateOwnedConnection();

    // Same contract as DbDataSource.OpenConnection: a connection is only handed
    // out after Open succeeds; otherwise it is disposed before the error escapes.
    public DmConnection OpenConnection()
    {
        DmConnection connection = CreateOwnedConnection();
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async ValueTask<DmConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        DmConnection connection = CreateOwnedConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public DbCommand CreateCommand(string commandText = null) => CreateOwnedCommand(commandText);
#if !NETSTANDARD2_1
    public DbBatch CreateBatch() => CreateOwnedBatch();
#endif
    public void Dispose() => Stop();
    public ValueTask DisposeAsync()
    {
        Stop();
        return default;
    }
#endif

    private DmConnection CreateOwnedConnection()
    {
        ThrowIfDisposed();
        return new DmConnection(settings, this);
    }
    private DbCommand CreateOwnedCommand(string commandText)
    {
        ThrowIfDisposed();
        return new DmDataSourceCommand(this, commandText ?? string.Empty);
    }
#if !NETSTANDARD2_1
    private DbBatch CreateOwnedBatch()
    {
        ThrowIfDisposed();
        return new DmDataSourceBatch(this);
    }
#endif
    internal void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(DmDataSource));
    }
    internal void Publish(Action publish)
    {
        lock (publicationGate)
        {
            ThrowIfDisposed();
            publish();
        }
    }
    public void ClearPool()
    {
        ThrowIfDisposed();
        Owner.Clear();
    }
    private void Stop()
    {
        bool stop;
        lock (publicationGate) stop = Interlocked.Exchange(ref disposed, 1) == 0;
        if (stop) Owner.Stop(); // Waiter continuations/cancellation cleanup stay outside publicationGate.
    }
}
