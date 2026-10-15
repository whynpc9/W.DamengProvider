using System.Buffers.Binary;
using System.Data;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class SchemaOnlyReaderTests
{
    [Theory]
    [InlineData(CommandBehavior.SchemaOnly, false)]
    [InlineData(CommandBehavior.SchemaOnly, true)]
    [InlineData(CommandBehavior.KeyInfo, false)]
    [InlineData(CommandBehavior.KeyInfo, true)]
    [InlineData(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, false)]
    [InlineData(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo, true)]
    public async Task ProcedureMetadataModesRejectBeforeAnyProtocolBytes(CommandBehavior behavior, bool async)
    {
        await using var database = new TransactionAsyncFixture();
        await using var command = new DmCommand("P", database.Connection, database.Transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        var output = new DmParameter("B", DmDbType.Int32)
        {
            Direction = ParameterDirection.Output,
            Value = 17
        };
        command.Parameters.Add(output);
        if (async)
            await Assert.ThrowsAsync<NotSupportedException>(() => command.ExecuteReaderAsync(behavior));
        else
            Assert.Throws<NotSupportedException>(() => command.ExecuteReader(behavior));
        Assert.Equal(0, database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        Assert.Equal(17, output.Value);
        command.CommandText = "OTHER_P";
        output.Value = 23;
        using var next = database.Session.BeginExecution(DmOperationPurpose.Metadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescribeUsesPrepareOnlyAndAllNavigationIsLocal(bool async)
    {
        await using var database = new TransactionAsyncFixture();
        using var channel = new DescribeChannel(asyncOnly: async);
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("/* SELECT ; ignored */ SELECT 17 AS VALUE FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        await using var reader = async
            ? await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo)
            : command.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo);
        Assert.Equal(1, reader.FieldCount);
        Assert.Equal("VALUE", reader.GetName(0));
        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(-1, reader.RecordsAffected);
        var schema = reader.GetSchemaTable()!;
        Assert.Equal(DBNull.Value, schema.Rows[0]["IsKey"]);
        Assert.Equal(DBNull.Value, schema.Rows[0]["IsExpression"]);
        Assert.Equal(DBNull.Value, schema.Rows[0]["IsReadOnly"]);
        Assert.Equal(DBNull.Value, schema.Rows[0]["IsAliased"]);
        Assert.False(reader.HasRows);
        Assert.False(reader.Read());
        Assert.False(await reader.ReadAsync());
        Assert.False(reader.NextResult());
        Assert.False(await reader.NextResultAsync());
        Assert.Single(channel.Requests);
        Assert.Equal((5, 0), channel.Requests[0]);
        Assert.Throws<InvalidOperationException>(() => database.Connection.GetSchema());
        Assert.Throws<InvalidOperationException>(() => command.CommandText = "SELECT 2 FROM DUAL");
        if (async) await reader.CloseAsync(); else reader.Close();
        Assert.True(reader.IsClosed);
        Assert.Equal(2, channel.Requests.Count);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        command.CommandText = "SELECT 2 FROM DUAL";
        using var next = database.Session.BeginExecution(DmOperationPurpose.Metadata);
    }

    [Theory]
    [InlineData("SELECT 1 FROM DUAL; DELETE FROM T")]
    [InlineData("WITH X AS (SELECT 1 FROM DUAL) SELECT * FROM X")]
    [InlineData("BEGIN DELETE FROM T; END;")]
    [InlineData("DELETE FROM T")]
    public async Task UnsafeDescriptionShapesRejectBeforeAnyProtocolBytes(string sql)
    {
        await using var database = new TransactionAsyncFixture();
        await using var command = new DmCommand(sql, database.Connection, database.Transaction);
        await Assert.ThrowsAsync<NotSupportedException>(() => command.ExecuteReaderAsync(CommandBehavior.SchemaOnly));
        Assert.Throws<NotSupportedException>(() => command.ExecuteReader(CommandBehavior.KeyInfo));
        Assert.Equal(0, database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
    }

    [Fact]
    public async Task PreCanceledDescriptionDoesNotAllocateStatement()
    {
        await using var database = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, canceled.Token));
        Assert.Equal(0, database.Channel.Sends);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
    }

    [Fact]
    public async Task CancellationWhileDescribingBreaksOnlyItsSessionAndReleasesPlan()
    {
        await using var database = new TransactionAsyncFixture();
        await using var command = new DmCommand("SELECT 1 FROM DUAL", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        using var cancel = new CancellationTokenSource();
        Task call = command.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancel.Token);
        await database.Channel.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(1, database.Channel.Sends);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DescriptionErrorsPreserveOriginalServerNumberAndReflectCleanupOutcome(bool async, bool failClose)
    {
        await using var database = new TransactionAsyncFixture();
        using var channel = new DescribeChannel(asyncOnly: async, failPrepare: true, failClose: failClose);
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT * FROM MISSING", database.Connection, database.Transaction);
        CancellationApiFixture.InstallStatement(command, database);
        var error = async
            ? await Assert.ThrowsAsync<DmException>(() => command.ExecuteReaderAsync(CommandBehavior.SchemaOnly))
            : Assert.Throws<DmException>(() => command.ExecuteReader(CommandBehavior.SchemaOnly));
        Assert.Equal(-2106, error.Number);
        Assert.Equal(!failClose, error.FailureInfo.ConnectionReusable);
        if (failClose)
        {
            Assert.True(database.Session.State is DmPhysicalSessionState.Broken or DmPhysicalSessionState.Closed);
            Assert.True(channel.IsClosed);
        }
        else Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        Assert.Equal(2, channel.Requests.Count);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchedDescribeParameterCountNeverExecutesOrReturnsAReader(bool async)
    {
        await using var database = new TransactionAsyncFixture();
        using var channel = new DescribeChannel(asyncOnly: async);
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand("SELECT :p AS VALUE FROM DUAL", database.Connection, database.Transaction);
        command.Parameters.Add(new DmParameter("p", 17));
        CancellationApiFixture.InstallStatement(command, database);
        // The synthetic server returns zero parameter descriptors despite one bound marker.
        if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteReaderAsync(CommandBehavior.SchemaOnly));
        else Assert.Throws<InvalidOperationException>(() => command.ExecuteReader(CommandBehavior.SchemaOnly));
        Assert.Equal((5, 0), Assert.Single(channel.Requests));
        Assert.True(channel.IsClosed);
        command.CommandText = "SELECT 2 FROM DUAL";
    }

    private sealed class DescribeChannel(bool asyncOnly, bool failPrepare = false, bool failClose = false) : IDmByteChannel
    {
        private readonly MemoryStream request = new();
        private byte[]? response;
        private int position;
        public bool IsClosed { get; private set; }
        internal List<(int Opcode, int Execute)> Requests { get; } = [];
        public int Send(byte[] bytes, int offset, int count, int timeout)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous send forbidden.");
            return SendCore(bytes, offset, count);
        }
        private int SendCore(byte[] bytes, int offset, int count)
        {
            request.Write(bytes, offset, count);
            return count;
        }
        public int Receive(byte[] bytes, int offset, int count, int timeout)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous receive forbidden.");
            return ReceiveCore(bytes, offset, count);
        }
        private int ReceiveCore(byte[] bytes, int offset, int count)
        {
            if (response == null)
            {
                byte[] packet = request.ToArray();
                int opcode = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4));
                Requests.Add((opcode, packet[21]));
                if (opcode == 4 && failClose) throw new IOException("Synthetic describe cleanup failure.");
                response = opcode == 5 ? failPrepare ? ErrorFrame() : DescribeFrame() : new byte[64];
                position = 0;
                request.SetLength(0);
            }
            int read = Math.Min(count, response.Length - position);
            response.AsSpan(position, read).CopyTo(bytes.AsSpan(offset));
            position += read;
            if (position == response.Length) response = null;
            return read;
        }
        public ValueTask<int> SendAsync(byte[] bytes, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(bytes, offset, count)); }
        public ValueTask<int> ReceiveAsync(byte[] bytes, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReceiveCore(bytes, offset, count)); }
        public void Dispose() { IsClosed = true; request.Dispose(); }
        private static byte[] ErrorFrame()
        {
            byte[] frame = new byte[80];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), 5);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), 16);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), -2106);
            for (int i = 0; i < 19; i++) frame[19] ^= frame[i];
            return frame;
        }
        private static byte[] DescribeFrame()
        {
            byte[] name = Encoding.ASCII.GetBytes("VALUE");
            byte[] frame = new byte[96 + name.Length];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), 5);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), frame.Length - 64);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(24), 1);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(64), 7);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(68), 4);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(88), (short)name.Length);
            name.CopyTo(frame, 96);
            for (int i = 0; i < 19; i++) frame[19] ^= frame[i];
            return frame;
        }
    }
}
