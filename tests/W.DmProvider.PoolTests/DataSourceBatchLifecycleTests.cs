using System.Data;
using System.Data.Common;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
public sealed class DataSourceBatchLifecycleTests
{
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);
    private const string Query = "SELECT VALUE FROM SYNTHETIC_TABLE";

    public static IEnumerable<object[]> EntryPoints()
    {
        foreach (bool asynchronous in new[] { false, true })
        foreach (string method in new[] { "nonquery", "scalar", "reader" })
            yield return new object[] { asynchronous, method };
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task CancelInterruptsOpenPoolWait(bool asynchronous, string method)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var held = await source.OpenConnectionAsync();
        await using var batch = CreateBatch(source);
        Task call = Start(batch, asynchronous, method);
        try
        {
            await WaitFor(() => source.Snapshot.Waiting == 1);
            batch.Cancel();
            await AssertCanceled(call, DmCancelSource.Command, DmFailurePhase.PoolWait, DmOperationOutcome.NotSent);
            Assert.Equal(0, source.Snapshot.Waiting);
            Assert.Equal(1, source.Snapshot.Leased);
            Assert.Single(handshake.Channels);
            Assert.Equal(ConnectionState.Open, held.State);
            Assert.False(handshake.Channels[0].IsClosed);
        }
        finally
        {
            await held.CloseAsync();
            await Drain(call);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelDuringOpenHandshakeKeepsCapacityUntilPhysicalClose(bool asynchronous)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var batch = CreateBatch(source);
        await using var next = source.CreateConnection();
        var channel = new ShortcutWireChannel(asynchronous) { PausePhysicalClose = true };
        var entered = NewSignal();
        var releaseHandshake = NewSignal();
        int attempts = 0;
        DmPendingOpenTestHooks.Handshake = async (candidate, asyncOpen, token) =>
        {
            if (Interlocked.Increment(ref attempts) != 1) { handshake.Install(candidate); return; }
            Assert.Equal(asynchronous, asyncOpen);
            handshake.Install(candidate, channel);
            MarkHandshakeSent();
            entered.TrySetResult();
            if (asyncOpen) await releaseHandshake.Task.WaitAsync(token);
            else releaseHandshake.Task.Wait(token);
        };
        Task call = Start(batch, asynchronous, "nonquery");
        Task? cancel = null, waiting = null;
        try
        {
            await ObserveBarrier(entered.Task, call);
            cancel = Task.Run(batch.Cancel);
            await channel.CloseEntered.Task.WaitAsync(Watchdog);
            waiting = next.OpenAsync();
            await WaitFor(() => source.Snapshot.Waiting == 1);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.Equal(1, attempts);
            Assert.False(waiting.IsCompleted);
            Assert.False(channel.IsClosed);

            channel.ReleasePhysicalClose.TrySetResult();
            await cancel.WaitAsync(Watchdog);
            await AssertCanceled(call, DmCancelSource.Command, DmFailurePhase.Connect, DmOperationOutcome.Unknown);
            await waiting.WaitAsync(Watchdog);
            Assert.Equal(2, attempts);
            Assert.True(channel.IsClosed);
            Assert.Equal(1, channel.DisposeCalls);
            Assert.Empty(channel.Opcodes);
        }
        finally
        {
            channel.ReleasePhysicalClose.TrySetResult();
            releaseHandshake.TrySetResult();
            if (cancel != null) await Drain(cancel);
            await Drain(call);
            await next.CloseAsync();
            if (waiting != null) await Drain(waiting);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    public static IEnumerable<object[]> HandoffPoints()
    {
        foreach (object[] entry in EntryPoints())
        foreach (bool duringAcquire in new[] { false, true })
            yield return new[] { entry[0], entry[1], duringAcquire };
    }

    [Theory]
    [MemberData(nameof(HandoffPoints))]
    public async Task CancelAfterOpenBeforeLeaseHandoffSendsNoSql(bool asynchronous, string method, bool duringAcquire)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var batch = CreateBatch(source);
        var channel = new ShortcutWireChannel(asynchronous);
        Install(handshake, channel);
        if (duringAcquire)
            DmSessionTestHooks.AfterExecutionAcquired = _ => { if (handshake.Channels.Count != 0) batch.Cancel(); };
        else DmDataSourceBatch.AfterOpen = batch.Cancel;
        Task call = Start(batch, asynchronous, method);
        try
        {
            await AssertCompleted(call);
            var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
            Assert.Equal(DmCancelSource.Command, failure.FailureInfo.CancelSource);
            Assert.Equal(DmOperationOutcome.NotSent, failure.FailureInfo.OperationOutcome);
            Assert.Empty(channel.Opcodes);
            Assert.True(channel.IsClosed);
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            DmSessionTestHooks.AfterExecutionAcquired = null;
            DmDataSourceBatch.AfterOpen = null;
            await Drain(call);
        }
    }

    [Theory]
    [InlineData("pool", DmFailurePhase.PoolWait, DmOperationOutcome.NotSent)]
    [InlineData("handshake", DmFailurePhase.Connect, DmOperationOutcome.Unknown)]
    [InlineData("execute-user", DmFailurePhase.Receive, DmOperationOutcome.Unknown)]
    [InlineData("execute-command", DmFailurePhase.Receive, DmOperationOutcome.Unknown)]
    public async Task CancellationPreservesSourcePhaseAndCallerToken(string stage, DmFailurePhase phase, DmOperationOutcome outcome)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var batch = CreateBatch(source);
        using var caller = new CancellationTokenSource();
        bool executing = phase == DmFailurePhase.Receive;
        bool userCanceled = stage != "execute-command";
        var channel = new ShortcutWireChannel(true) { HoldQueryReply = executing };
        var entered = NewSignal();
        var releaseHandshake = NewSignal();
        await using var held = stage == "pool" ? await source.OpenConnectionAsync() : null;
        if (stage != "pool")
            DmPendingOpenTestHooks.Handshake = async (candidate, _, token) =>
            {
                handshake.Install(candidate, channel);
                if (stage != "handshake") return;
                MarkHandshakeSent();
                entered.TrySetResult();
                await releaseHandshake.Task.WaitAsync(token);
            };

        Task call = batch.ExecuteNonQueryAsync(caller.Token);
        try
        {
            if (stage == "pool") await WaitFor(() => source.Snapshot.Waiting == 1);
            else await ObserveBarrier(stage == "handshake" ? entered.Task : channel.ReceiveEntered.Task, call);
            if (userCanceled) caller.Cancel(); else batch.Cancel();
            var failure = await AssertCanceled(call, userCanceled ? DmCancelSource.User : DmCancelSource.Command, phase, outcome);
            if (userCanceled) Assert.Equal(caller.Token, failure.CancellationToken);
            if (executing) Assert.Equal(new short[] { 3, 5 }, channel.Opcodes);
            else Assert.Empty(channel.Opcodes);
            if (stage != "pool") Assert.True(channel.IsClosed);
        }
        finally
        {
            releaseHandshake.TrySetResult();
            channel.ReleaseReply.TrySetResult();
            if (held != null) await held.CloseAsync();
            await Drain(call);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DisposeInterruptsPendingOpen(bool disposeAsync, bool duringHandshake)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var batch = CreateBatch(source);
        var channel = new ShortcutWireChannel(true);
        var entered = NewSignal();
        var releaseHandshake = NewSignal();
        await using var held = duringHandshake ? null : await source.OpenConnectionAsync();
        if (duringHandshake)
            DmPendingOpenTestHooks.Handshake = async (candidate, _, token) =>
            {
                handshake.Install(candidate, channel);
                MarkHandshakeSent();
                entered.TrySetResult();
                await releaseHandshake.Task.WaitAsync(token);
            };
        Task call = batch.ExecuteNonQueryAsync();
        try
        {
            if (duringHandshake) await ObserveBarrier(entered.Task, call);
            else await WaitFor(() => source.Snapshot.Waiting == 1);
            if (disposeAsync) await batch.DisposeAsync(); else batch.Dispose();

            await AssertCompleted(call);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            Assert.Equal(0, source.Snapshot.Waiting);
            Assert.Single(handshake.Channels);
            Assert.Empty(channel.Opcodes);
            if (duringHandshake) Assert.True(channel.IsClosed);
            else Assert.Equal(ConnectionState.Open, held!.State);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => batch.ExecuteNonQueryAsync());
        }
        finally
        {
            releaseHandshake.TrySetResult();
            if (held != null) await held.CloseAsync();
            await Drain(call);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedCancelFromPriorRunCannotCancelReplacement(bool asynchronous)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var batch = CreateBatch(source);
        var first = new ShortcutWireChannel(asynchronous) { HoldQueryReply = true };
        var second = new ShortcutWireChannel(asynchronous) { HoldQueryReply = true };
        int attempts = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            handshake.Install(candidate, Interlocked.Increment(ref attempts) == 1 ? first : second);
            return ValueTask.CompletedTask;
        };
        Task call = Start(batch, asynchronous, "reader");
        var captured = NewSignal();
        var resume = NewSignal();
        Task? cancel = null, replacement = null;
        try
        {
            await ObserveBarrier(first.ReceiveEntered.Task, call);
            DmDataSourceBatch.AfterCancelCaptured = () =>
            {
                captured.TrySetResult();
                resume.Task.WaitAsync(Watchdog).GetAwaiter().GetResult();
            };
            cancel = Task.Run(batch.Cancel);
            await ObserveBarrier(captured.Task, cancel);
            first.ReleaseReply.TrySetResult();
            await call.WaitAsync(Watchdog);
            Assert.True(first.IsClosed);

            replacement = Start(batch, asynchronous, "reader");
            await ObserveBarrier(second.ReceiveEntered.Task, replacement);
            resume.TrySetResult();
            await cancel.WaitAsync(Watchdog);
            Assert.False(replacement.IsCompleted);
            Assert.False(second.IsClosed);
            second.ReleaseReply.TrySetResult();
            await replacement.WaitAsync(Watchdog);
            Assert.True(second.IsClosed);
            Assert.Equal(1, first.DisposeCalls);
            Assert.Equal(1, second.DisposeCalls);
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            DmDataSourceBatch.AfterCancelCaptured = null;
            resume.TrySetResult();
            first.ReleaseReply.TrySetResult();
            second.ReleaseReply.TrySetResult();
            if (cancel != null) await Drain(cancel);
            await Drain(call);
            if (replacement != null) await Drain(replacement);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeliveredReaderSurvivesBatchDisposeAndClosesOnlyOnce(bool asynchronous, bool disposeAsync)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var batch = CreateBatch(source);
        using var caller = new CancellationTokenSource();
        var channel = new ShortcutWireChannel(asynchronous) { PausePhysicalClose = true };
        Install(handshake, channel);
        await using var reader = asynchronous ? await batch.ExecuteReaderAsync(caller.Token) : batch.ExecuteReader();
        Task? close = null;
        try
        {
            caller.Cancel();
            if (disposeAsync) await batch.DisposeAsync(); else batch.Dispose();
            Assert.False(reader.IsClosed);
            Assert.Equal(1, reader.FieldCount);
            Assert.False(asynchronous ? await reader.ReadAsync() : reader.Read());
            Assert.Equal(1, source.Snapshot.Leased);
            Assert.False(channel.IsClosed);

            close = Task.Run(async () =>
            {
                if (asynchronous) await reader.DisposeAsync(); else reader.Dispose();
            });
            await ObserveBarrier(channel.CloseEntered.Task, close);
            await reader.DisposeAsync();
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            Assert.False(channel.IsClosed);
            channel.ReleasePhysicalClose.TrySetResult();
            await close.WaitAsync(Watchdog);
            Assert.Equal(1, channel.DisposeCalls);
            Assert.True(channel.IsClosed);
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            channel.ReleasePhysicalClose.TrySetResult();
            if (close != null) await Drain(close);
        }
    }

    [Fact]
    public async Task ReaderCloseKeepsExecutionOwnedUntilPhysicalReturn()
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var batch = CreateBatch(source);
        var first = new ShortcutWireChannel(true) { PausePhysicalClose = true };
        var second = new ShortcutWireChannel(true);
        int attempts = 0;
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            handshake.Install(candidate, Interlocked.Increment(ref attempts) == 1 ? first : second);
            return ValueTask.CompletedTask;
        };
        await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        Task close = Task.Run(async () => await reader.DisposeAsync());
        try
        {
            await ObserveBarrier(first.CloseEntered.Task, close);
            await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteNonQueryAsync().WaitAsync(Watchdog));
            Assert.Equal(1, attempts);
            Assert.Equal(0, source.Snapshot.Waiting);
            Assert.Equal(1, source.Snapshot.PhysicalCount);
            first.ReleasePhysicalClose.TrySetResult();
            await close.WaitAsync(Watchdog);
            await ReadAndCloseAsync(batch, default).WaitAsync(Watchdog);
            Assert.Equal(2, attempts);
            Assert.Equal(1, first.DisposeCalls);
            Assert.Equal(1, second.DisposeCalls);
            Assert.True(source.Snapshot.IsQuiescent);
        }
        finally
        {
            first.ReleasePhysicalClose.TrySetResult();
            await Drain(close);
        }
    }

    [Fact]
    public async Task PrepareAndPrepareAsyncAreLocalPreflight()
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var batch = CreateBatch(source);
        batch.Prepare();
        await batch.PrepareAsync();
        batch.BatchCommands[0].CommandType = CommandType.StoredProcedure;
        Assert.Throws<NotSupportedException>(batch.Prepare);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await batch.PrepareAsync());
        Assert.Empty(handshake.Channels);
        Assert.True(source.Snapshot.IsQuiescent);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void MarkHandshakeSent()
    {
        DmInvocation.Current.Phase = DmFailurePhase.Connect;
        DmInvocation.Current.SendAttempted = true;
    }

    private static void Install(SyntheticPoolHandshake handshake, ShortcutWireChannel channel) =>
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            handshake.Install(candidate, channel);
            return ValueTask.CompletedTask;
        };

    private static async Task ObserveBarrier(Task barrier, Task call)
    {
        Task winner = await Task.WhenAny(barrier, call).WaitAsync(Watchdog);
        if (winner == call) await call; // Surface fixture and protocol failures.
        Assert.True(barrier.IsCompleted);
    }

    private static DbBatch CreateBatch(DmDataSource source)
    {
        DbBatch batch = source.CreateBatch();
        DbBatchCommand command = batch.CreateBatchCommand();
        command.CommandText = Query;
        batch.BatchCommands.Add(command);
        return batch;
    }

    private static Task Start(DbBatch batch, bool asynchronous, string method, CancellationToken token = default)
    {
        if (asynchronous)
            return method switch
            {
                "nonquery" => batch.ExecuteNonQueryAsync(token),
                "scalar" => batch.ExecuteScalarAsync(token),
                "reader" => ReadAndCloseAsync(batch, token),
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            };
        return Task.Run(() =>
        {
            switch (method)
            {
                case "nonquery": batch.ExecuteNonQuery(); break;
                case "scalar": batch.ExecuteScalar(); break;
                case "reader": using (batch.ExecuteReader()) { } break;
                default: throw new ArgumentOutOfRangeException(nameof(method));
            }
        });
    }

    private static async Task ReadAndCloseAsync(DbBatch batch, CancellationToken token)
    { await using var reader = await batch.ExecuteReaderAsync(token); }

    private static Task WaitFor(Func<bool> condition) =>
        Task.Run(() => Assert.True(SpinWait.SpinUntil(condition, Watchdog)));

    private static async Task Drain(Task call)
    {
        await AssertCompleted(call);
        try { await call; } catch { } // The test asserts the original outcome separately.
    }

    private static async Task AssertCompleted(Task call) =>
        Assert.Same(call, await Task.WhenAny(call, Task.Delay(Watchdog)));

    private static async Task<DmOperationCanceledException> AssertCanceled(Task call, DmCancelSource source,
        DmFailurePhase phase, DmOperationOutcome outcome)
    {
        await AssertCompleted(call);
        var failure = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call);
        Assert.Equal(source, failure.FailureInfo.CancelSource);
        Assert.Equal(phase, failure.FailureInfo.Phase);
        Assert.Equal(outcome, failure.FailureInfo.OperationOutcome);
        return failure;
    }
}
