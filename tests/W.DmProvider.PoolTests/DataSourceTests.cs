using System.Data;
using System.Data.Common;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class DataSourceTests
{
    [Fact]
    public void SafeDisplayNeverSuppliesCredentialsAndDataSourcesHaveIndependentOwners()
    {
        using var first = new DmDataSource(PoolApiSettings.Text);
        using var second = new DmDataSource(PoolApiSettings.Text);
        Assert.NotSame(first.Owner, second.Owner);
        Assert.DoesNotContain("CANARY_POOL", first.ConnectionString);
        using var connection = first.CreateConnection();
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal("CANARY_POOL", connection.Settings.Password);
        Assert.DoesNotContain("CANARY_POOL", connection.ConnectionString);
        Assert.Throws<InvalidOperationException>(() => connection.ConnectionString = PoolApiSettings.Text.Replace("CANARY_POOL", "DIFFERENT"));
        Assert.Throws<InvalidOperationException>(() => connection.Schema = "OTHER");
        using var clone = connection.Clone();
        Assert.Same(first.Owner, clone.PoolOwner);
        using var factory = DmClientFactory.Instance.CreateDataSource(PoolApiSettings.Text);
        Assert.IsType<DmDataSource>(factory);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DataSourceDisposeStopsNewAdmissionWhileExistingLeaseCanFinish(bool pooling)
    {
        using var hook = new SyntheticPoolHandshake();
        var source = new DmDataSource(PoolApiSettings.Text.Replace("Pooling=true", "Pooling=" + pooling));
        using var first = await source.OpenConnectionAsync();
        using var closed = source.CreateConnection();
        await source.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => source.CreateConnection());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => closed.OpenAsync());
        Assert.Equal(ConnectionState.Open, first.State);
        Assert.False(hook.Channels[0].IsClosed);
        await first.CloseAsync();
        Assert.True(hook.Channels[0].IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedDataSourceOpenDisposesUndeliveredConnectionAndReturnsCapacity(bool asynchronous)
    {
        // .NET 6 has no DbDataSource; its typed OpenConnection must keep the base class cleanup contract.
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var failure = new InvalidOperationException("synthetic handshake failure");
        DmConnection? undelivered = null;
        DmPendingOpenTestHooks.AfterInstalled = connection => undelivered = connection;
        DmPendingOpenTestHooks.Handshake = (_, _, _) => throw failure;
        Exception error = asynchronous
            ? await Assert.ThrowsAnyAsync<Exception>(async () => await source.OpenConnectionAsync())
            : Assert.ThrowsAny<Exception>(() => source.OpenConnection());
        Assert.Same(failure, error);
        // The connection the caller never received was disposed, not merely closed.
        Assert.NotNull(undelivered);
        Assert.Throws<ObjectDisposedException>(() => undelivered!.Open());
        DmPendingOpenTestHooks.AfterInstalled = null;
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        // MaxPoolSize=1: the next open succeeds only if the failed attempt released its permit.
        DmPendingOpenTestHooks.Handshake = (connection, _, _) => { hook.Install(connection); return ValueTask.CompletedTask; };
        using var next = asynchronous ? await source.OpenConnectionAsync() : source.OpenConnection();
        Assert.Equal(ConnectionState.Open, next.State);
        Assert.Equal(1, source.Snapshot.Leased);
    }

    [Fact]
    public async Task DisposeRejectsCreatingConnectionBeforePublishAndAbortsCandidate()
    {
        using var hook = new SyntheticPoolHandshake();
        var source = new DmDataSource(PoolApiSettings.Text);
        using var connection = source.CreateConnection();
        DmPendingOpenTestHooks.BeforePublish = _ => source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.OpenAsync());
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.True(hook.Channels.Single().IsClosed);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public async Task DisposeCancelsWaiterButDoesNotAbortPublishedLease()
    {
        using var hook = new SyntheticPoolHandshake();
        var source = new DmDataSource(PoolApiSettings.Text);
        using var first = await source.OpenConnectionAsync();
        using var second = source.CreateConnection();
        Task pending = second.OpenAsync();
        Assert.Equal(1, source.Snapshot.Waiting);
        source.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Equal(ConnectionState.Open, first.State);
        Assert.Equal(1, source.Snapshot.Leased);
        await first.CloseAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ShortcutReaderOwnsConnectionUntilDisposeThenAllowsRepeatWithSameParameters()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var first = new ShortcutWireChannel(true) { ParameterTypes = [7], ScalarValue = 8 };
        var second = new ShortcutWireChannel(true) { ParameterTypes = [7], ScalarValue = 9 };
        byte[]? firstExecute = null, secondExecute = null;
        first.ObserveRequest = (opcode, frame) => { if (opcode is 6 or 13) firstExecute = frame; };
        second.ObserveRequest = (opcode, frame) => { if (opcode is 6 or 13) secondExecute = frame; };
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, hook.Channels.Count == 0 ? first : second);
            return ValueTask.CompletedTask;
        };
        await using var command = source.CreateCommand("SELECT :p FROM DUAL");
        var parameter = new DmParameter("p", DmDbType.Int32) { Value = 8 };
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.Equal(1, source.Snapshot.Leased);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(8, await reader.GetFieldValueAsync<int>(0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteScalarAsync());
        await reader.DisposeAsync();
        await reader.DisposeAsync();
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Same(parameter, command.Parameters[0]);
        parameter.Value = 9;
        Assert.Equal(9, await command.ExecuteScalarAsync());
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        first.AssertIoPath();
        second.AssertIoPath();
        Assert.NotNull(firstExecute);
        Assert.NotNull(secondExecute);
        Assert.Equal(8, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(firstExecute.AsSpan(^4)));
        Assert.Equal(9, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(secondExecute.AsSpan(^4)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShortcutNonReaderPrepareAndErrorsReleaseEveryLease(bool asynchronous)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, new ShortcutWireChannel(asynchronous) { AffectedRows = 1, ParameterTypes = [7] });
            return ValueTask.CompletedTask;
        };
        await using var command = source.CreateCommand("INSERT INTO SYNTHETIC_TABLE VALUES (:p)");
        command.Parameters.Add(new DmParameter("p", DmDbType.Int32) { Value = 10 });
        if (asynchronous)
        {
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            await command.PrepareAsync();
        }
        else { Assert.Equal(1, command.ExecuteNonQuery()); command.Prepare(); }
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        DmDataSourceCommand.AfterOpen = () => throw new InvalidOperationException("synthetic execution failure");
        try
        {
            if (asynchronous) await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteScalarAsync());
            else Assert.Throws<InvalidOperationException>(() => command.ExecuteScalar());
        }
        finally { DmDataSourceCommand.AfterOpen = null; }
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Equal(3, hook.Channels.Count);
        foreach (ShortcutWireChannel channel in hook.Channels.Take(2)) channel.AssertIoPath();
        Assert.Empty(((ShortcutWireChannel)hook.Channels[2]).Opcodes);
        Assert.All(hook.Channels, channel => Assert.True(channel.IsClosed));
    }

    [Fact]
    public async Task ShortcutCleanupCallbackFailureCannotReplaceTypedPrimaryFailure()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var primary = new DmTimeoutException(new DmFailureInfo(DmErrorKind.Timeout, DmFailurePhase.Receive,
            "WDM_TIMEOUT", DmOperationOutcome.Unknown, null, false, DmCancelSource.TotalDeadline));
        await using var command = source.CreateCommand("synthetic");
        DmPendingOpenTestHooks.AfterCapacityAcquired = connection => connection.StateChange += (_, change) =>
        { if (change.CurrentState == ConnectionState.Closed) throw new InvalidOperationException("synthetic cleanup failure"); };
        DmDataSourceCommand.AfterOpen = () => throw primary;
        try
        {
            var error = await Assert.ThrowsAsync<DmTimeoutException>(() => command.ExecuteNonQueryAsync());
            Assert.Same(primary, error);
            Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
        }
        finally { DmDataSourceCommand.AfterOpen = null; }
    }

    [Fact]
    public async Task ShortcutDisposeBeforeAuthenticationPreventsNewHandshakeAndReturnsPermit()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        DmPendingOpenTestHooks.AfterCapacityAcquired = _ => command.Dispose();
        await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteScalarAsync());
        Assert.Empty(hook.Channels);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ShortcutDisposeBeforeReaderPublicationClosesUnpublishedReaderAndConnection()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new ShortcutWireChannel(true);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { hook.Install(candidate, channel); return ValueTask.CompletedTask; };
        using var command = source.CreateCommand("SELECT VALUE FROM SYNTHETIC_TABLE");
        DbDataReader? unpublished = null;
        DmDataSourceCommand.BeforeReaderPublished = reader =>
        {
            unpublished = reader;
            command.Dispose();
        };
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => command.ExecuteReaderAsync());
            Assert.NotNull(unpublished);
            Assert.True(unpublished.IsClosed);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
        }
        finally { DmDataSourceCommand.BeforeReaderPublished = null; }
    }

    [Fact]
    public async Task ShortcutPreCanceledInvocationDoesNotAcquireConnectionOrMutateParameters()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var command = source.CreateCommand("synthetic");
        var parameter = new DmParameter("p", DmDbType.Int32) { Value = 42 };
        command.Parameters.Add(parameter);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteNonQueryAsync(caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Same(parameter, command.Parameters[0]);
        Assert.Empty(hook.Channels);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
    }

    [Fact]
    public async Task ActualShortcutPrepareUsesAsyncOnlyChannelAndCanceledFailureReturnsPermit()
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        using var command = source.CreateCommand("SELECT 1 FROM DUAL");
        using var caller = new CancellationTokenSource();
        var channel = new AsyncOnlyShortcutChannel();
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) => { hook.Install(candidate, channel); return ValueTask.CompletedTask; };
        DmTransportTestHooks.BeforeNetworkIo = (asynchronous, _) => Assert.True(asynchronous);
        try
        {
            Task call = command.PrepareAsync(caller.Token);
            await channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel();
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
            Assert.Equal(0, channel.SyncCalls);
            Assert.True(channel.AsyncSends > 0);
            Assert.True(channel.IsClosed);
            Assert.Equal(0, source.Snapshot.PhysicalCount);
        }
        finally { DmTransportTestHooks.BeforeNetworkIo = null; }
    }

    private sealed class AsyncOnlyShortcutChannel : IDmByteChannel
    {
        internal readonly TaskCompletionSource ReceiveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource aborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SyncCalls, AsyncSends;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync send forbidden."); }
        public int Receive(byte[] buffer, int offset, int count, int timeout) { SyncCalls++; throw new InvalidOperationException("Sync receive forbidden."); }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); AsyncSends++; return ValueTask.FromResult(count); }
        public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { ReceiveEntered.TrySetResult(); await aborted.Task.WaitAsync(token); return 0; }
        public void Dispose() { IsClosed = true; aborted.TrySetResult(); }
    }

}
