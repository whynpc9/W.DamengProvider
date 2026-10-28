using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Types;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.CommandTests;

public sealed class BatchProviderSpecificTests
{
    private static readonly DmDecimal First = DmDecimal.Parse("12345678901234567890123456789012345678");
    private static readonly DmDecimal Second = DmDecimal.Parse("87654321098765432109876543210987654321");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderSpecificGettersPreserveExactNumbersAndNullsAcrossResults(bool asynchronous)
    {
        using var fixture = new BatchRows();
        AssertValues(fixture.Reader, First);
        Assert.True(asynchronous ? await fixture.Reader.NextResultAsync() : fixture.Reader.NextResult());
        Assert.True(asynchronous ? await fixture.Reader.ReadAsync() : fixture.Reader.Read());
        AssertValues(fixture.Reader, Second);
        Assert.Equal(new short[] { 44 }, fixture.Channel.Requests);
    }

    [Fact]
    public void ProviderSpecificGettersRetainMissingResultAndClosedGuards()
    {
        using var batch = new DmBatch();
        using var reader = new DmBatchDataReader(new DmBatchExecution(batch), CommandBehavior.Default);
        AssertUnavailable(reader);
        reader.Close();
        AssertUnavailable(reader);
    }

    private static void AssertValues(DmBatchDataReader reader, DmDecimal expected)
    {
        Assert.Equal(typeof(decimal), reader.GetFieldType(0));
        Assert.Equal(typeof(DmDecimal), reader.GetProviderSpecificFieldType(0));
        Assert.Equal(typeof(DmDecimal), reader.GetProviderSpecificFieldType(1));
        Assert.Equal(expected, Assert.IsType<DmDecimal>(reader.GetProviderSpecificValue(0)));
        Assert.Same(DBNull.Value, reader.GetProviderSpecificValue(1));
        Assert.Equal(expected, reader.GetFieldValue<DmDecimal>(0));
        object sentinel = new();
        object[] values = [sentinel, sentinel, sentinel];
        Assert.Equal(2, reader.GetProviderSpecificValues(values));
        Assert.Equal(expected, Assert.IsType<DmDecimal>(values[0]));
        Assert.Same(DBNull.Value, values[1]);
        Assert.Same(sentinel, values[2]);
        object[] one = [sentinel];
        Assert.Equal(1, reader.GetProviderSpecificValues(one));
        Assert.Equal(expected, Assert.IsType<DmDecimal>(one[0]));
        Assert.Equal(0, reader.GetProviderSpecificValues([]));
        Assert.Throws<OverflowException>(() => reader.GetValue(0));
        Assert.Throws<OverflowException>(() => reader.GetDecimal(0));
    }

    private static void AssertUnavailable(DmBatchDataReader reader)
    {
        Assert.Throws<InvalidOperationException>(() => reader.GetProviderSpecificFieldType(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetProviderSpecificValue(0));
        Assert.Throws<InvalidOperationException>(() => reader.GetProviderSpecificValues(new object[1]));
    }

    // A cached first row and a synthetic MORE_RESULTS response exercise the actual
    // reader, numeric codec, result transition and batch wrapper without a database.
    private sealed class BatchRows : IDisposable
    {
        private readonly DmConnection connection = new(new DmConnectionStringBuilder
        {
            Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only"
        }.ConnectionString);
        private readonly DmBatch batch = new();
        private readonly DmExecutionLease lease;
        private readonly Statement statement;
        private readonly DmDataReader inner;
        internal DmBatchDataReader Reader { get; }
        internal RowChannel Channel { get; } = new();

        internal BatchRows()
        {
            var session = new DmSession();
            Set(connection, "session", session);
            session.BeginConnecting();
            DmConnInstance physical;
            using (var handshake = session.BeginExecution(DmOperationPurpose.Handshake))
            using (var invocation = handshake.BeginInvocation())
            {
                physical = new DmConnInstance(connection);
                connection.m_ConnInst = physical;
                physical.ConnProperty.ServerEncoding = "UTF-8";
                physical.ConnProperty.msgVersion = 21;
                var protocol = physical.GetCsi();
                var wire = protocol.A();
                ((DmTransport)Get(wire, "transport")).Dispose();
                Set(wire, "transport", new DmTransport(Channel));
                Set(wire, "__t02_field_04000AAD", false);
                Set(protocol, "__t02_field_04000ABD", false);
                session.CompleteHandshake();
            }
            connection.do_State = ConnectionState.Open;
            lease = session.BeginExecution(DmOperationPurpose.Reader);
            statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
            Set(statement, "__t02_field_04000923", physical);
            Set(statement, "__t02_field_04000924", physical.GetCsi());
            Set(statement, "__t02_field_04000933", new DmCommand("synthetic", connection));
            Set(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
            Set(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
            var info = new DmInfo(physical);
            info.SetColumnsInfo([new DmColumn(physical) { type = 9, prec = 38, name = "N" },
                new DmColumn(physical) { type = 9, prec = 38, name = "NULL_N" }]);
            info.SetHasResultSet(true);
            info.SetRowCount(1);
            info.SetRetStmtType(160);
            Set(statement, "__t02_field_04000927", info);
            var cache = new DmResultSetCache(statement, 2, 1)
            {
                datas = [[[], DmNumericCodec.EncodeDecimal(First), null!]], datasStartPos = 0
            };
            Set(statement, "__t02_field_04000928", cache);
            inner = new DmDataReader(cache, info, CommandBehavior.Default);
            inner.AttachExecutionLease(lease, ownsLease: false);
            Reader = new DmBatchDataReader(new DmBatchExecution(batch), CommandBehavior.Default);
            Set(Reader, "current", inner);
            Set(Reader, "currentSource", new DmBatchCommand("synthetic"));
            Assert.True(Reader.Read());
        }

        public void Dispose()
        {
            // Synthetic statement handles are released locally, as in the other
            // cached-reader fixtures; disposal cannot add protocol requests.
            var terminal = new DmInfo();
            terminal.MarkTerminal();
            ((DmResultCursor)Get(inner, "resultCursor")).Observe(terminal);
            statement.o();
            Reader.Close();
            lease.Dispose();
            connection.Dispose();
            batch.Dispose();
        }
    }

    private sealed class RowChannel : IDmByteChannel
    {
        private byte[] reply = [];
        private int position;
        internal List<short> Requests { get; } = [];
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeout)
        {
            short opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Assert.Equal((short)44, opcode);
            Requests.Add(opcode);
            reply = Rowset(Second);
            position = 0;
            return count;
        }
        public int Receive(byte[] buffer, int offset, int count, int timeout)
        {
            int read = Math.Min(count, reply.Length - position);
            reply.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
            position += read;
            return read;
        }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Send(buffer, offset, count, 0)); }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Receive(buffer, offset, count, 0)); }
        public void Dispose() => IsClosed = true;

        private static byte[] Rowset(DmDecimal value)
        {
            byte[] encoded = DmNumericCodec.EncodeDecimal(value);
            // Two 32-byte metadata descriptors, row prefix/rowid/ordinals,
            // the native decimal and a NULL length marker.
            byte[] body = new byte[64 + 2 + 12 + 4 + 2 + encoded.Length + 2];
            foreach (int start in new[] { 0, 32 })
            {
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(start), 9);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(start + 4), 38);
                BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(start + 12), 1);
            }
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(82), checked((ushort)encoded.Length));
            encoded.CopyTo(body, 84);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(84 + encoded.Length), 65534);
            byte[] frame = new byte[64 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), 2);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), 1);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(35), 1);
            body.CopyTo(frame, 64);
            for (int index = 0; index < 19; index++) frame[19] ^= frame[index];
            return frame;
        }
    }

    private static object Get(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
}
