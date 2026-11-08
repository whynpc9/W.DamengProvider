using System.Data;
using System.Data.Common;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
public sealed class DataSourceInputSnapshotTests
{
    private const string Sql = "SELECT VALUE FROM SYNTHETIC_TABLE";
    private const string ParameterizedSql = "SELECT :p FROM SYNTHETIC_TABLE";
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(5);

    public static IEnumerable<object[]> WaitingEntryPoints()
    {
        foreach (bool asynchronous in new[] { false, true })
        {
            foreach (string method in new[] { "nonquery", "scalar", "reader", "prepare" })
                yield return new object[] { asynchronous, method, false };
            foreach (string method in new[] { "nonquery", "scalar", "reader" })
                yield return new object[] { asynchronous, method, true };
        }
    }

    [Theory]
    [MemberData(nameof(WaitingEntryPoints))]
    public async Task WaitingMutationIsRejected(bool asynchronous, string method, bool batchCall)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var held = await source.OpenConnectionAsync();
        var channel = new ShortcutWireChannel(asynchronous);
        Install(handshake, channel);
        await using var command = source.CreateCommand(ParameterizedSql);
        await using var batch = source.CreateBatch();
        var step = new DmBatchCommand(ParameterizedSql);
        batch.BatchCommands.Add(step);
        DbParameterCollection parameters = batchCall ? step.Parameters : command.Parameters;
        var parameter = new DmParameter("p", DmDbType.Int32) { Value = 3 };
        parameters.Add(parameter);
        Task call = batchCall ? Start(batch, asynchronous, method) : Start(command, asynchronous, method);
        try
        {
            await WaitForPool(source, call);
            Assert.Empty(channel.Opcodes);
            if (batchCall)
            {
                Assert.Throws<InvalidOperationException>(() => batch.Timeout = 7);
                Assert.Throws<InvalidOperationException>(() => step.CommandText = "changed");
                Assert.Throws<InvalidOperationException>(() => step.CommandType = CommandType.StoredProcedure);
                Assert.Throws<InvalidOperationException>(() => batch.BatchCommands.Add(new DmBatchCommand(Sql)));
                Assert.Throws<InvalidOperationException>(() => batch.BatchCommands.Remove(step));
                Assert.Throws<InvalidOperationException>(() => batch.BatchCommands.Clear());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => command.CommandText = "changed");
                Assert.Throws<InvalidOperationException>(() => command.CommandType = CommandType.StoredProcedure);
                Assert.Throws<InvalidOperationException>(() => command.CommandTimeout = 7);
            }
            Assert.Throws<InvalidOperationException>(() => parameter.Value = 4);
            Assert.Throws<InvalidOperationException>(() => parameter.ParameterName = "changed");
            Assert.Throws<InvalidOperationException>(() => parameter.DbType = DbType.String);
            Assert.Throws<InvalidOperationException>(() => parameter.Direction = ParameterDirection.Output);
            Assert.Throws<InvalidOperationException>(() => parameters.Add(new DmParameter()));
            Assert.Throws<InvalidOperationException>(() => parameters.Remove(parameter));
            Assert.Throws<InvalidOperationException>(() => parameters.Clear());
            // Cancel while parked to verify that the frozen inputs are released without SQL.
            if (batchCall) batch.Cancel(); else command.Cancel();
            await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Watchdog));
            Assert.Empty(channel.Opcodes);
            parameter.Value = 4;
        }
        finally
        {
            if (batchCall) batch.Cancel(); else command.Cancel();
            await held.CloseAsync();
            await Drain(call);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BufferSnapshotAndExecutionBudgetSurvivePoolWait(bool asynchronous, bool batchCall)
    {
        const string insert = "INSERT INTO SYNTHETIC_TABLE VALUES (:p, :q)";
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var held = await source.OpenConnectionAsync();
        var clock = new SnapshotClock();
        DmPendingOpenTestHooks.AfterInstalled = candidate => candidate.OperationClock = clock;
        var channel = new ShortcutWireChannel(asynchronous) { AffectedRows = 1, ParameterTypes = [18, 2] };
        var executions = new List<byte[]>();
        channel.ObserveRequest = (opcode, frame) =>
        {
            if (opcode is not (6 or 13)) return;
            Assert.Equal(3, DmInvocation.Current!.Lease.TimeoutSeconds);
            Assert.Equal(TimeSpan.FromSeconds(3), DmInvocation.Current.Lease.Deadline.RemainingTime);
            executions.Add(frame);
        };
        Install(handshake, channel);
        await using var command = source.CreateCommand(insert);
        command.CommandTimeout = 3;
        await using var batch = source.CreateBatch();
        batch.Timeout = 3;
        var step = new DmBatchCommand(insert);
        batch.BatchCommands.Add(step);
        batch.BatchCommands.Add(step); // One source gate, two separate frozen plans.
        DbParameterCollection parameters = batchCall ? step.Parameters : command.Parameters;
        byte[] bytes = [0xD1, 0x1E, 0xA7, 0x65];
        char[] chars = "Original text".ToCharArray();
        parameters.Add(new DmParameter("p", DmDbType.VarBinary) { Value = bytes });
        parameters.Add(new DmParameter("q", DmDbType.VarChar) { Value = chars });
        Task<int> call = batchCall
            ? asynchronous ? batch.ExecuteNonQueryAsync() : Task.Run(batch.ExecuteNonQuery)
            : asynchronous ? command.ExecuteNonQueryAsync() : Task.Run(command.ExecuteNonQuery);
        try
        {
            await WaitForPool(source, call);
            Assert.Empty(channel.Opcodes);
            Array.Fill(bytes, (byte)0xE4);
            Array.Fill(chars, 'X');
            clock.Advance(TimeSpan.FromSeconds(4));
            await held.CloseAsync();
            int expectedSteps = batchCall ? 2 : 1;
            Assert.Equal(expectedSteps, await call.WaitAsync(Watchdog));
            Assert.Equal(expectedSteps, executions.Count);
            foreach (byte[] frame in executions)
            {
                byte[] expected = [4, 0, 0xD1, 0x1E, 0xA7, 0x65, 13, 0, .. Encoding.ASCII.GetBytes("Original text")];
                Assert.Equal(expected, frame[^expected.Length..]);
            }
            Assert.Same(bytes, parameters[0].Value);
            Assert.Same(chars, parameters[1].Value);
            if (batchCall) Assert.Equal(1, step.RecordsAffected);
            channel.AssertIoPath();
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
    public async Task ReaderLifetimeKeepsInputsFrozenThroughClose(bool batchCall)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new ShortcutWireChannel(true) { ParameterTypes = [7], ScalarValue = 3 };
        Install(handshake, channel);
        await using var command = source.CreateCommand(ParameterizedSql);
        await using var batch = source.CreateBatch();
        var step = new DmBatchCommand(ParameterizedSql);
        batch.BatchCommands.Add(step);
        DbParameterCollection parameters = batchCall ? step.Parameters : command.Parameters;
        var parameter = new DmParameter("p", 3);
        parameters.Add(parameter);
        await using DbDataReader reader = batchCall ? await batch.ExecuteReaderAsync() : await command.ExecuteReaderAsync();
        Assert.Equal(1, source.Snapshot.Leased);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3, reader.GetInt32(0));
        Assert.Throws<InvalidOperationException>(() => parameter.Value = 4);
        Assert.Throws<InvalidOperationException>(() => parameters.Clear());
        if (batchCall) Assert.Throws<InvalidOperationException>(() => batch.BatchCommands.Clear());
        await reader.CloseAsync();
        await reader.CloseAsync();
        parameter.Value = 4;
        parameters.Clear();
        if (batchCall) batch.BatchCommands.Clear();
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReleaseAfterFailureAllowsMutationAndReuse(bool batchCall, bool openFailure)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        await using var command = source.CreateCommand(ParameterizedSql);
        await using var batch = source.CreateBatch();
        var step = new DmBatchCommand(ParameterizedSql);
        batch.BatchCommands.Add(step);
        DbParameterCollection parameters = batchCall ? step.Parameters : command.Parameters;
        var parameter = new DmParameter("p", 3);
        parameters.Add(parameter);
        var failure = new InvalidOperationException("Synthetic open failure.");
        int opens = 0;
        var invalidStep = new DmBatchCommand();
        if (!openFailure)
        {
            if (batchCall) batch.BatchCommands.Add(invalidStep);
            else command.CommandText = invalidStep.CommandText;
        }
        DmPendingOpenTestHooks.Handshake = (_, _, _) => { opens++; throw failure; };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            batchCall ? batch.ExecuteNonQueryAsync() : command.ExecuteNonQueryAsync());
        Assert.Equal(openFailure ? 1 : 0, opens);
        if (openFailure) Assert.Same(failure, error);
        Assert.True(source.Snapshot.IsQuiescent);
        parameter.Value = 9;
        if (batchCall)
        {
            step.CommandText = ParameterizedSql;
            invalidStep.CommandText = Sql;
            batch.BatchCommands.Remove(invalidStep);
        }
        else command.CommandText = ParameterizedSql;
        Install(handshake, new ShortcutWireChannel(true) { ParameterTypes = [7], AffectedRows = 1 });
        Assert.Equal(1, await (batchCall ? batch.ExecuteNonQueryAsync() : command.ExecuteNonQueryAsync()));
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StreamingInputUsesNegotiatedProfileWithoutReadingOrOwningCallerStream(bool batchCall, bool modern)
    {
        const string insert = "INSERT INTO SYNTHETIC_TABLE VALUES (:p)";
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        await using var held = await source.OpenConnectionAsync();
        var channel = new ShortcutWireChannel(true) { ParameterTypes = [12], AffectedRows = 1 };
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            handshake.Install(candidate, channel);
            candidate.GetConnInstance().ConnProperty.msgVersion = (short)(modern ? 21 : 9);
            return ValueTask.CompletedTask;
        };
        await using var command = source.CreateCommand(insert);
        await using var batch = source.CreateBatch();
        var step = new DmBatchCommand(insert);
        batch.BatchCommands.Add(step);
        using var input = new TrackingInput([1, 2, 3, 4]);
        DbParameterCollection parameters = batchCall ? step.Parameters : command.Parameters;
        var parameter = new DmParameter("p", DmDbType.Blob) { Value = input };
        parameters.Add(parameter);
        Task<int> call = batchCall ? batch.ExecuteNonQueryAsync() : command.ExecuteNonQueryAsync();
        try
        {
            await WaitForPool(source, call);
            Assert.Equal(0, input.Reads);
            Assert.False(input.WasDisposed);
            await held.CloseAsync();
            if (modern)
            {
                Assert.Equal(1, await call.WaitAsync(Watchdog));
                Assert.Contains((short)26, channel.Opcodes);
                Assert.True(input.Reads > 0);
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => call.WaitAsync(Watchdog));
                Assert.Empty(channel.Opcodes);
                Assert.Equal(0, input.Reads);
            }
            Assert.False(input.WasDisposed);
            Assert.Same(input, parameter.Value);
            parameter.Value = new byte[] { 1 };
        }
        finally
        {
            await held.CloseAsync();
            await Drain(call);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    [Fact]
    public async Task BatchPreservesPriorCountsDuringAndAfterCanceledPoolWait()
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text + ";conn_pool_timeout=0");
        Install(handshake, new ShortcutWireChannel(true) { AffectedRows = 2 });
        await using var batch = source.CreateBatch();
        var step = new DmBatchCommand("UPDATE SYNTHETIC_TABLE SET VALUE = 1");
        batch.BatchCommands.Add(step);
        Assert.Equal(2, await batch.ExecuteNonQueryAsync());
        Assert.Equal(2, step.RecordsAffected);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate); return ValueTask.CompletedTask; };
        await using var held = await source.OpenConnectionAsync();
        Task call = batch.ExecuteNonQueryAsync();
        try
        {
            await WaitForPool(source, call);
            Assert.Equal(2, step.RecordsAffected);
            batch.Cancel();
            await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(Watchdog));
            Assert.Equal(2, step.RecordsAffected);
        }
        finally
        {
            batch.Cancel();
            await held.CloseAsync();
            await Drain(call);
        }
        Assert.True(source.Snapshot.IsQuiescent);
    }

    private sealed class SnapshotClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
    }

    private sealed class TrackingInput(byte[] data) : MemoryStream(data)
    {
        internal int Reads;
        internal bool WasDisposed;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++;
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, data.Length - (int)Position);
            data.AsSpan((int)Position, count).CopyTo(buffer.Span);
            Position += count;
            return ValueTask.FromResult(count);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Sync stream read.");

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static void Install(SyntheticPoolHandshake handshake, ShortcutWireChannel channel) =>
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        { handshake.Install(candidate, channel); return ValueTask.CompletedTask; };

    private static Task Start(DbCommand command, bool asynchronous, string method) => asynchronous ? method switch
    {
        "nonquery" => command.ExecuteNonQueryAsync(),
        "scalar" => command.ExecuteScalarAsync(),
        "prepare" => command.PrepareAsync(),
        _ => CloseReaderAsync(command.ExecuteReaderAsync())
    } : Task.Run(() =>
    {
        switch (method)
        {
            case "nonquery": command.ExecuteNonQuery(); break;
            case "scalar": command.ExecuteScalar(); break;
            case "prepare": command.Prepare(); break;
            default: using (command.ExecuteReader()) { } break;
        }
    });

    private static Task Start(DbBatch batch, bool asynchronous, string method) => asynchronous ? method switch
    {
        "nonquery" => batch.ExecuteNonQueryAsync(),
        "scalar" => batch.ExecuteScalarAsync(),
        _ => CloseReaderAsync(batch.ExecuteReaderAsync())
    } : Task.Run(() =>
    {
        switch (method)
        {
            case "nonquery": batch.ExecuteNonQuery(); break;
            case "scalar": batch.ExecuteScalar(); break;
            default: using (batch.ExecuteReader()) { } break;
        }
    });

    private static async Task CloseReaderAsync(Task<DbDataReader> reader)
    {
        await using var owned = await reader;
    }

    private static async Task WaitForPool(DmDataSource source, Task call)
    {
        using var timeout = new CancellationTokenSource(Watchdog);
        while (source.Snapshot.Waiting != 1)
        {
            if (call.IsCompleted) await call;
            await Task.Delay(1, timeout.Token);
        }
    }

    private static async Task Drain(Task call)
    {
        try { await call.WaitAsync(Watchdog); }
        catch when (call.IsCompleted) { }
    }
}
