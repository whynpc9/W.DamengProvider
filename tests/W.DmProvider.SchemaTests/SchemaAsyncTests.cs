using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.SchemaTests;

public sealed class SchemaAsyncTests
{
    [Theory]
    [InlineData("MetaDataCollections")]
    [InlineData("Restrictions")]
    [InlineData("DataTypes")]
    [InlineData("DataSourceInformation")]
    public async Task MemoryCollectionsMatchSynchronouslyAndAsynchronouslyWithoutSending(string name)
    {
        await using var database = new SchemaFixture();
        DataTable sync = database.Connection.GetSchema(name);
        Task<DataTable> operation = database.Connection.GetSchemaAsync(name);
        Assert.True(operation.IsCompletedSuccessfully);
        DataTable async = await operation;
        Assert.Equal(sync.TableName, async.TableName);
        Assert.Equal(sync.Columns.Cast<DataColumn>().Select(c => (c.ColumnName, c.DataType)),
            async.Columns.Cast<DataColumn>().Select(c => (c.ColumnName, c.DataType)));
        Assert.Equal(sync.Rows.Count, async.Rows.Count);
        for (int i = 0; i < sync.Rows.Count; i++) Assert.Equal(sync.Rows[i].ItemArray, async.Rows[i].ItemArray);
        Assert.Equal(0, database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EveryOverloadPreCancellationAcquiresNoLeaseAndSendsNothing(int overload)
    {
        await using var database = new SchemaFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<DataTable> operation = overload switch
        {
            0 => database.Connection.GetSchemaAsync(cancellation.Token),
            1 => database.Connection.GetSchemaAsync("Tables", cancellation.Token),
            _ => database.Connection.GetSchemaAsync("Tables", new[] { "test" }, cancellation.Token)
        };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
    }

    [Fact]
    public async Task CatalogOperationOwnsMetadataLeaseAndWaitsForAsyncTransport()
    {
        await using var database = new SchemaFixture();
        Task<DataTable> operation = database.Connection.GetSchemaAsync("Tables");
        await database.Channel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(DmPhysicalSessionState.Busy, database.Session.State);
        Assert.Throws<InvalidOperationException>(() => database.Connection.GetSchema());
        database.Channel.Reply.TrySetResult(false);
        var error = await Assert.ThrowsAsync<DmException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DmErrorKind.Transport, error.ErrorKind);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(1, database.Channel.Sends);
    }

    [Fact]
    public async Task InFlightCancellationBreaksUnknownProtocolStateWithoutSyncCleanup()
    {
        await using var database = new SchemaFixture();
        using var cancellation = new CancellationTokenSource();
        Task<DataTable> operation = database.Connection.GetSchemaAsync("Tables", cancellation.Token);
        await database.Channel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(1, database.Channel.Sends);
    }

    [Fact]
    public async Task CloseDetachesPendingDiscoveryWithoutSyncNetworkCleanup()
    {
        await using var database = new SchemaFixture();
        Task<DataTable> operation = database.Connection.GetSchemaAsync("Tables");
        await database.Channel.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await database.Connection.CloseAsync();
        var error = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(error);
        Assert.IsNotType<TimeoutException>(error);
        Assert.Equal(ConnectionState.Closed, database.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Closed, database.Session.State);
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(1, database.Channel.Sends);
    }
}

// Bind the production connection/protocol to a transport which rejects all sync I/O.
internal sealed class SchemaFixture : IAsyncDisposable
{
    internal DmConnection Connection { get; }
    internal DmSession Session { get; }
    internal SchemaBarrierChannel Channel { get; } = new();

    internal SchemaFixture()
    {
        Connection = new DmConnection(new DmConnectionStringBuilder
        {
            Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only",
            CommandTimeout = 5, ConnectTimeout = TimeSpan.FromSeconds(5), CleanupTimeout = TimeSpan.FromSeconds(5)
        }.ConnectionString);
        Session = new DmSession(broken =>
        {
            if (ReferenceEquals(Connection.Session, broken)) Connection.do_State = ConnectionState.Broken;
        });
        SetField(Connection, "session", Session);
        Session.BeginConnecting();
        using (var lease = Session.BeginExecution(DmOperationPurpose.Handshake))
        using (var invocation = lease.BeginInvocation())
        {
            var instance = new DmConnInstance(Connection);
            Connection.m_ConnInst = instance;
            instance.ConnProperty.ServerVersion = "8.1.5.60";
            var protocol = instance.GetCsi();
            var transport = protocol.A();
            ((DmTransport)GetField(transport, "transport")!).Dispose();
            SetField(transport, "transport", new DmTransport(Channel));
            SetField(transport, "__t02_field_04000AAD", false);
            SetField(protocol, "__t02_field_04000ABD", false);
            Session.CompleteHandshake();
        }
        Connection.do_State = ConnectionState.Open;
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
    private static object? GetField(object owner, string name) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner);
    private static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}

internal sealed class SchemaBarrierChannel : IDmByteChannel
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Sends { get; private set; }
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Schema async path performed synchronous send.");
    public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        => throw new InvalidOperationException("Schema async path performed synchronous receive.");
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sends++;
        return ValueTask.FromResult(count);
    }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        bool reply = await Reply.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!reply) return 0;
        Array.Clear(buffer, offset, count);
        return count;
    }
    public void Dispose()
    {
        IsClosed = true;
        Reply.TrySetResult(false);
    }
}
