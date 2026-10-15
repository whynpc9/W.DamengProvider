using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.AsyncTests;

public sealed class StoredProcedureResultCacheTests
{
    [Fact]
    public void NondefaultResultCacheOptionRemainsRejectedByPublicConfiguration()
    {
        var builder = new DmConnectionStringBuilder();
        Assert.False(builder.EnRsCache);
        Assert.Throws<NotSupportedException>(() => builder.EnRsCache = true);
        Assert.Throws<NotSupportedException>(() => new DmConnection("enRsCache=true"));
    }

    [Theory]
    [InlineData(CommandType.Text, false)]
    [InlineData(CommandType.StoredProcedure, false)]
    [InlineData(CommandType.StoredProcedure, true)]
    public async Task MatchingGlobalCacheEntryCannotBypassProcedureExecution(CommandType type, bool nullCache)
    {
        await using var database = new TransactionAsyncFixture();
        // Complete the fixture transaction through its normal verified ACK path;
        // public procedure execution intentionally rejects explicit transactions.
        database.Channel.ReleaseResponse.TrySetResult(true);
        await database.Transaction.CommitAsync();
        Assert.Null(database.Session.ActiveTransaction);
        EnableCache(database);
        using var channel = new RejectSendChannel();
        CancellationApiFixture.InstallChannel(database, channel);
        await using var command = new DmCommand(type == CommandType.Text ? "SELECT 1 FROM DUAL" : "P",
            database.Connection) { CommandType = type };
        Statement statement = CancellationApiFixture.InstallStatement(command, database);
        var info = statement.F();
        info.SetRetStmtType(160);
        info.SetHasResultSet(true);
        info.SetRowCount(1);
        var cached = new DmResultSetCache(statement, 0, 1)
        {
            datas = [[new byte[12]]], ids = [], tss = [], lastCheckDt = DateTime.Now
        };
        using var contract = DmCommandPlan.Capture(command.CommandText, type, 30, null!,
            database.Connection, command.Parameters);
        var key = Key(database, command, contract.WireSql);
        RsLRUCache previous = DmConnection.rsLRUCache;
        DmConnection.rsLRUCache = nullCache ? null! : new RsLRUCache(1024);
        try
        {
            if (!nullCache)
            {
                Assert.True(DmConnection.rsLRUCache.Add(key, cached));
                Assert.Same(cached, DmConnection.rsLRUCache.Find(key));
            }
            if (type == CommandType.StoredProcedure)
            {
                Assert.Throws<DmException>(() => command.ExecuteReader());
                Assert.Equal(1, channel.Sends);
            }
            else
            {
                using var reader = command.ExecuteReader();
                Assert.Equal(0, channel.Sends);
                Assert.True(reader.Read());
                Assert.Same(cached, Assert.IsType<DmDataReader>(reader).m_Statement.l());
                statement.o(); // Synthetic handle owns no server object.
            }
        }
        finally
        {
            statement.o();
            DmConnection.rsLRUCache = previous;
        }
    }

    [Theory]
    [InlineData("execute", CommandType.Text, false)]
    [InlineData("execute", CommandType.StoredProcedure, false)]
    [InlineData("execute_async", CommandType.Text, false)]
    [InlineData("execute_async", CommandType.StoredProcedure, false)]
    [InlineData("fetch", CommandType.Text, false)]
    [InlineData("fetch", CommandType.StoredProcedure, false)]
    [InlineData("execute", CommandType.StoredProcedure, true)]
    [InlineData("execute_async", CommandType.StoredProcedure, true)]
    [InlineData("fetch", CommandType.StoredProcedure, true)]
    public async Task CompleteRowsRemainLocalToProcedureAcrossResponsePaths(string route, CommandType type, bool nullCache)
    {
        await using var database = new TransactionAsyncFixture();
        EnableCache(database);
        await using var command = new DmCommand(type == CommandType.Text ? "SELECT 1 FROM DUAL" : "P",
            database.Connection, database.Transaction) { CommandType = type };
        Statement statement = CancellationApiFixture.InstallStatement(command, database);
        using var plan = DmCommandPlan.Capture(command.CommandText, type, 30, database.Transaction,
            database.Connection, command.Parameters);
        ReaderAsyncFixture.SetField(command, "activePlan", plan);
        statement.B(plan.WireSql);
        var key = Key(database, command, plan.WireSql);
        RsLRUCache previous = DmConnection.rsLRUCache;
        DmConnection.rsLRUCache = nullCache ? null! : new RsLRUCache(1024);
        try
        {
            using var lease = database.Session.BeginExecution(DmOperationPurpose.Reader);
            using var invocation = lease.BeginInvocation();
            b frame = CachedRowFrame();
            if (route == "execute") c.A(frame, statement, database.Connection.m_ConnInst.ConnProperty);
            else if (route == "execute_async") await c.AAsync(frame, statement, database.Connection.m_ConnInst.ConnProperty);
            else
            {
                // The shared fetch response path receives complete row and table-version
                // metadata. It must not populate the global cache for a procedure.
                MethodInfo decode = typeof(B).GetMethod("A", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [typeof(b), typeof(Statement), typeof(b), typeof(int), typeof(long), typeof(int)], null)!;
                Assert.NotNull(decode);
                decode.Invoke(database.Connection.m_ConnInst.GetCsi(), [new b(), statement, frame, 0, 1L, 0]);
            }
            invocation.Complete();
            Assert.NotNull(statement.l());
            Assert.Single(statement.l().datas);
            Assert.Equal(12, statement.l().datas[0][0].Length);
            Assert.Equal(1, statement.l().totalRowCount);
            if (!nullCache)
            {
                if (type == CommandType.Text) Assert.Same(statement.l(), DmConnection.rsLRUCache.Find(key));
                else Assert.Null(DmConnection.rsLRUCache.Find(key));
            }
        }
        finally
        {
            statement.o();
            ReaderAsyncFixture.SetField(command, "activePlan", null);
            DmConnection.rsLRUCache = previous;
        }
    }

    private static void EnableCache(TransactionAsyncFixture database)
    {
        // This legacy option is publicly rejected. Exercise its internal paths
        // without opening that option or confusing the logical and physical snapshots.
        foreach (var properties in new[] { database.Connection.ConnProperty, database.Connection.m_ConnInst.ConnProperty })
        {
            properties.property[DmConst.PROP_KEY_ENABLE_RS_CACHE] = true;
            properties.property[DmConst.PROP_KEY_RS_REFRESH_FREQ] = 0;
            properties.Guid = "synthetic-cache-db";
            properties.CurrentSchema = "S";
            properties.msgVersion = 21;
        }
        Assert.True(database.Connection.m_ConnInst.ConnProperty.EnRsCache);
    }

    private static RsKey Key(TransactionAsyncFixture database, DmCommand command, string sql) =>
        new(database.Connection.m_ConnInst.ConnProperty.Guid, database.Connection.m_ConnInst.ConnProperty.CurrentSchema,
            sql, command.Parameters.Count, command.Parameters);

    private static b CachedRowFrame()
    {
        // SELECT response: one row with zero user columns, a 12-byte row id,
        // followed by an empty table-version list. All cache preconditions hold.
        byte[] bytes = new byte[80];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 160);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(35), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(55), 78);
        var frame = new b(bytes);
        frame.f(64);
        return frame;
    }

    private sealed class RejectSendChannel : IDmByteChannel
    {
        internal int Sends { get; private set; }
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        { Sends++; throw new IOException("Expected procedure execution reached the transport."); }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => throw new InvalidOperationException();
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            ValueTask.FromException<int>(new InvalidOperationException("This test exercises synchronous cache lookup."));
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            ValueTask.FromException<int>(new InvalidOperationException());
        public void Dispose() => IsClosed = true;
    }
}
