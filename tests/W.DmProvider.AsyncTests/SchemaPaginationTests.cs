using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.AsyncTests;

public sealed class SchemaPaginationTests
{
    [Fact]
    public async Task SuccessfulDiscoveryAwaitsFetchAndCloseAndRetainsMetadataOwnership()
    {
        await using var database = new PaginationFixture();
        Task<DataTable> operation = database.Connection.GetSchemaAsync("Users");
        await database.Channel.FetchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(new short[] { 3, 5, 7 }, database.Channel.Opcodes);
        Assert.Equal(DmPhysicalSessionState.Busy, database.Session.State);
        Assert.Throws<InvalidOperationException>(() => database.Connection.GetSchema());

        database.Channel.FetchReply.TrySetResult();
        await database.Channel.CloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(operation.IsCompleted);
        Assert.Equal(DmPhysicalSessionState.Busy, database.Session.State);
        database.Channel.CloseReply.TrySetResult();

        DataTable result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Users", result.TableName);
        Assert.Equal(new[] { "FIRST_USER", "SECOND_USER" }, result.Rows.Cast<DataRow>().Select(row => (string)row["USERNAME"]));
        Assert.Equal(new short[] { 3, 5, 7, 4 }, database.Channel.Opcodes);
        Assert.All(database.Channel.Purposes, purpose => Assert.Equal(DmOperationPurpose.Metadata, purpose));
        Assert.Equal(0, database.Channel.SyncCalls);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        Assert.False(database.Channel.IsClosed);
        using var next = database.Session.BeginExecution(DmOperationPurpose.Metadata);
    }

    [Fact]
    public async Task CancellationDuringSecondPageDiscardsSessionWithoutSynchronousCleanup()
    {
        await using var database = new PaginationFixture();
        using var cancellation = new CancellationTokenSource();
        Task<DataTable> operation = database.Connection.GetSchemaAsync("Users", cancellation.Token);
        await database.Channel.FetchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        database.Channel.FetchReply.TrySetResult();
        var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, database.Session.State);
        Assert.True(database.Channel.IsClosed);
        Assert.Equal(new short[] { 3, 5, 7 }, database.Channel.Opcodes);
        Assert.Equal(0, database.Channel.SyncCalls);
    }

    [Fact]
    public async Task OldPagedDiscoveryCannotCloseAReopenedConnectionsNewSession()
    {
        await using var database = new PaginationFixture();
        DmSession oldSession = database.Session;
        PaginationChannel oldChannel = database.Channel;
        Task<DataTable> oldOperation = database.Connection.GetSchemaAsync("Users");
        await oldChannel.FetchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await database.Connection.CloseAsync();
        Assert.Equal(ConnectionState.Closed, database.Connection.State);
        Assert.True(oldChannel.IsClosed);
        Assert.False(oldOperation.IsCompleted);

        // Install the same ready physical session boundary used by the other
        // protocol fixtures, after public CloseAsync has detached the old one.
        // The old receive remains pending until the replacement is in use.
        database.AttachReadySession();
        Assert.NotSame(oldSession, database.Session);
        Assert.NotNull(await database.Connection.GetSchemaAsync());
        oldChannel.FetchReply.TrySetResult();
        var error = await Record.ExceptionAsync(() => oldOperation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.NotNull(error);
        Assert.IsNotType<TimeoutException>(error);
        Assert.Equal(ConnectionState.Open, database.Connection.State);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
        Assert.False(database.Channel.IsClosed);

        database.Channel.FetchReply.TrySetResult();
        database.Channel.CloseReply.TrySetResult();
        DataTable result = await database.Connection.GetSchemaAsync("Users").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new short[] { 3, 5, 7, 4 }, database.Channel.Opcodes);
        Assert.Equal(0, oldChannel.SyncCalls + database.Channel.SyncCalls);
        Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
    }

    private sealed class PaginationFixture : IAsyncDisposable
    {
        internal DmConnection Connection { get; }
        internal DmSession Session { get; private set; } = null!;
        internal PaginationChannel Channel { get; private set; } = null!;
        private readonly List<PaginationChannel> channels = [];

        internal PaginationFixture()
        {
            Connection = new DmConnection(new DmConnectionStringBuilder
            {
                Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only",
                CommandTimeout = 5, CleanupTimeout = TimeSpan.FromSeconds(5)
            }.ConnectionString);
            AttachReadySession();
        }

        internal void AttachReadySession()
        {
            Channel = new PaginationChannel(); channels.Add(Channel);
            Session = new DmSession(broken =>
            {
                if (ReferenceEquals(Connection.Session, broken)) Connection.do_State = ConnectionState.Broken;
            });
            ReaderAsyncFixture.SetField(Connection, "session", Session);
            Session.BeginConnecting();
            using (var lease = Session.BeginExecution(DmOperationPurpose.Handshake))
            using (var invocation = lease.BeginInvocation())
            {
                var instance = new DmConnInstance(Connection);
                Connection.m_ConnInst = instance;
                instance.ConnProperty.ServerVersion = "8.1.5.60";
                instance.ConnProperty.msgVersion = 9;
                var protocol = instance.GetCsi();
                var wrapper = protocol.A();
                var field = wrapper.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
                ((DmTransport)field.GetValue(wrapper)!).Dispose();
                field.SetValue(wrapper, new DmTransport(Channel));
                ReaderAsyncFixture.SetField(wrapper, "__t02_field_04000AAD", false);
                ReaderAsyncFixture.SetField(protocol, "__t02_field_04000ABD", false);
                Session.CompleteHandshake();
            }
            Connection.do_State = ConnectionState.Open;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var channel in channels)
            { channel.FetchReply.TrySetResult(); channel.CloseReply.TrySetResult(); }
            await Connection.DisposeAsync();
        }
    }

    private sealed class PaginationChannel : IDmByteChannel
    {
        private readonly MemoryStream request = new();
        private byte[]? response;
        private int position;
        private short opcode;
        internal readonly List<short> Opcodes = [];
        internal readonly List<DmOperationPurpose> Purposes = [];
        internal TaskCompletionSource FetchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FetchReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CloseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CloseReply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SyncCalls;
        public bool IsClosed { get; private set; }

        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        { SyncCalls++; throw new InvalidOperationException("Schema pagination attempted synchronous send."); }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        { SyncCalls++; throw new InvalidOperationException("Schema pagination attempted synchronous receive."); }

        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (IsClosed) throw new ObjectDisposedException(nameof(PaginationChannel));
            request.Write(buffer, offset, count);
            return ValueTask.FromResult(count);
        }

        public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            if (response == null)
            {
                byte[] packet = request.ToArray(); request.SetLength(0);
                opcode = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4));
                Opcodes.Add(opcode);
                Purposes.Add(DmInvocation.Current.Lease.Purpose);
                position = 0;
                switch (opcode)
                {
                    case 3:
                        response = Frame(3, []);
                        BinaryPrimitives.WriteInt32LittleEndian(response, 41);
                        Checksum(response);
                        break;
                    case 5:
                        Assert.Equal(1, packet[21]); // Actual catalog SELECT, not prepare.
                        response = InitialRows();
                        break;
                    case 7:
                        response = FetchRows();
                        FetchEntered.TrySetResult();
                        // Delay unwind explicitly to exercise old cleanup after a
                        // replacement session is ready; no sleeps or timeout races.
                        await FetchReply.Task.ConfigureAwait(false);
                        break;
                    case 4:
                        response = Frame(4, []);
                        CloseEntered.TrySetResult();
                        await CloseReply.Task.ConfigureAwait(false);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected schema opcode {opcode}.");
                }
            }
            token.ThrowIfCancellationRequested();
            if (IsClosed) throw new ObjectDisposedException(nameof(PaginationChannel));
            int read = Math.Min(count, response.Length - position);
            response.AsSpan(position, read).CopyTo(buffer.AsSpan(offset)); position += read;
            if (position == response.Length) response = null;
            return read;
        }

        public void Dispose() => IsClosed = true;

        private static byte[] InitialRows()
        {
            byte[] name = Encoding.ASCII.GetBytes("USERNAME");
            byte[] row = Row("FIRST_USER");
            byte[] body = new byte[32 + name.Length + row.Length];
            BinaryPrimitives.WriteInt32LittleEndian(body, 2); // VARCHAR
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 128);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 1);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(24), (short)name.Length);
            name.CopyTo(body, 32); row.CopyTo(body, 32 + name.Length);
            byte[] frame = Frame(5, body);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), 1);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), long.MaxValue);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(35), 1);
            Checksum(frame); return frame;
        }

        private static byte[] FetchRows()
        {
            byte[] frame = Frame(7, Row("SECOND_USER"));
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20), 2);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28), 1);
            Checksum(frame); return frame;
        }

        private static byte[] Row(string value)
        {
            byte[] data = Encoding.ASCII.GetBytes(value);
            byte[] row = new byte[18 + data.Length]; // prefix, 12-byte row ID, ordinal, length, value
            BinaryPrimitives.WriteInt16LittleEndian(row.AsSpan(16), (short)data.Length);
            data.CopyTo(row, 18); return row;
        }

        private static byte[] Frame(short opcode, byte[] body)
        {
            byte[] frame = new byte[64 + body.Length];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            body.CopyTo(frame, 64); Checksum(frame); return frame;
        }

        private static void Checksum(byte[] frame)
        { frame[19] = 0; for (int index = 0; index < 19; index++) frame[19] ^= frame[index]; }
    }
}
