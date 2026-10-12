using System.Collections;
using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.AsyncTests;

public sealed class TemporalNativeReaderAsyncContractTests
{
    [Theory]
    [InlineData(false, 26)] [InlineData(true, 26)] [InlineData(false, 27)] [InlineData(true, 27)]
    public async Task SubTickFailurePreservesForwardCachedReadAndCleanup(bool sequential, int type)
    {
        await using var fixture = new Fixture(type, sequential);
        if (type == 26) await Assert.ThrowsAsync<OverflowException>(() => fixture.Reader.GetFieldValueAsync<DateTime>(0));
        else await Assert.ThrowsAsync<OverflowException>(() => fixture.Reader.GetFieldValueAsync<DateTimeOffset>(0));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.Equal(9, (await fixture.Reader.GetSchemaTableAsync())!.Rows[0]["NumericScale"]);
        if (!sequential) Assert.Equal("2026-07-23 14:15:16.000000001" + (type == 27 ? " -05:00" : ""), fixture.Reader.GetString(0));
        Assert.Equal(42, await fixture.Reader.GetFieldValueAsync<int>(1));
        Assert.Equal("A中é尾", await fixture.Reader.GetFieldValueAsync<string>(2));
        if (sequential) Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetString(0)).Number);
        Assert.True(await fixture.Reader.ReadAsync());
        if (type == 26) Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1), await fixture.Reader.GetFieldValueAsync<DateTime>(0));
        else
        {
            var value = await fixture.Reader.GetFieldValueAsync<DateTimeOffset>(0);
            Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1).Ticks, value.Ticks);
            Assert.Equal(TimeSpan.FromHours(-5), value.Offset);
        }
        fixture.RetireStatement(); await fixture.Reader.DisposeAsync();
        Assert.True(fixture.Reader.IsClosed); Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PreCanceledGenericProjectionDoesNotConsumeOrdinalOrOverridePrecisionFailure(bool sequential)
    {
        await using var fixture = new Fixture(26, sequential);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Reader.GetFieldValueAsync<DateTime>(0, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal("2026-07-23 14:15:16.000000001", fixture.Reader.GetString(0));
        if (!sequential) await Assert.ThrowsAsync<OverflowException>(() => fixture.Reader.GetFieldValueAsync<DateTime>(0));
        Assert.Equal(42, await fixture.Reader.GetFieldValueAsync<int>(1));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
    }

    [Theory]
    [InlineData(16)] [InlineData(26)]
    public async Task ClosedLocalTimezoneNullHasOriginal6081AndDbNullPriority(int type)
    {
        await using var fixture = new Fixture(type, false, nullValue: true, localTimezone: true);
        Assert.True(fixture.Reader.IsDBNull(0));
        Assert.Same(DBNull.Value, await fixture.Reader.GetFieldValueAsync<object>(0));
        Assert.Same(DBNull.Value, await fixture.Reader.GetFieldValueAsync<DBNull>(0));
        Assert.Equal(6081, (await Assert.ThrowsAsync<DmException>(() => fixture.Reader.GetFieldValueAsync<DateTime>(0))).Number);
        Assert.Equal(6081, (await Assert.ThrowsAsync<DmException>(() => fixture.Reader.GetFieldValueAsync<string>(0))).Number);
        Assert.Equal(typeof(DateTime), (await fixture.Reader.GetSchemaTableAsync())!.Rows[0]["DataType"]);
        Assert.Equal(42, await fixture.Reader.GetFieldValueAsync<int>(1));
    }

    [Theory]
    [InlineData(16)] [InlineData(26)]
    public async Task ClosedLocalTimezoneGenericValueGatePrecedesCast(int type)
    {
        await using var fixture = new Fixture(type, false, localTimezone: true);
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Reader.GetFieldValueAsync<DateTimeOffset>(0));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Reader.GetFieldValueAsync<string>(0));
        Assert.Throws<InvalidCastException>(() => fixture.Reader.GetDateTimeOffset(0));
        Assert.Equal(42, await fixture.Reader.GetFieldValueAsync<int>(1));
    }

    // Complete cached rows and inline CLOBs isolate conversion behavior. R3 transport/cancel suites remain
    // responsible for actual paged replies, in-flight cancellation and physical close completion.
    private sealed class Fixture : IAsyncDisposable
    {
        internal DmSession Session { get; } = new();
        internal DmDataReader Reader { get; }
        private readonly Statement statement = Uninitialized<Statement>();
        internal Fixture(int type, bool sequential, bool nullValue = false, bool localTimezone = false)
        {
            var connection = new DmConnection(); var physical = Uninitialized<DmConnInstance>(); var property = new DmConnProperty();
            property.ServerEncoding = "UTF-8"; property.NewLobFlag = true;
            Set(physical, "m_ConnPro", property); physical.Conn = connection; connection.m_ConnInst = physical;
            Session.CompleteHandshakeForTests(); var lease = Session.BeginExecution(DmOperationPurpose.Reader);
            Set(physical, "<Session>k__BackingField", Session); Set(statement, "__t02_field_04000923", physical);
            var temporal = new DmColumn(physical); temporal.SetCType(type); temporal.SetScale((type == 16 ? 6 : 9) | (localTimezone ? 0x1000 : 0));
            var integer = new DmColumn(physical); integer.SetCType(7);
            var clob = new DmColumn(physical); clob.SetCType(19);
            DmColumn[] columns = [temporal, integer, clob]; var info = new DmInfo(); info.SetColumnsInfo(columns);
            var cache = Uninitialized<DmResultSetCache>(); cache.colNum = 3; cache.currentPos = cache.datasOffset = 0; cache.totalRowCount = 2;
            byte[]? first = nullValue ? null : Wire(type, type == 16 ? 1000 : 1);
            cache.datas = [[[], first!, BitConverter.GetBytes(42), InlineClob()], [[], Wire(type, type == 16 ? 1000 : 100), BitConverter.GetBytes(43), InlineClob()]];
            Reader = Uninitialized<DmDataReader>(); Reader.m_Statement = statement; Reader.m_RowCount = 2;
            Set(Reader, "executionLease", lease); Set(Reader, "ownsExecutionLease", true); Set(Reader, "asyncCloseGate", new object());
            Set(Reader, "m_Conn", physical); Set(Reader, "m_Clobs", new ArrayList { null, null, null }); Set(Reader, "m_DbInfo", info);
            Set(Reader, "m_ColInfo", columns); Set(Reader, "m_RsCache", cache);
            Set(Reader, "m_GetVal", new DmGetValue("UTF-8", statement, false, columns, DmTemporalResultFormat.OrdinaryPacked));
            Set(Reader, "is_SequentialAccess", sequential); Set(Reader, "m_Behavior", sequential ? CommandBehavior.SequentialAccess : CommandBehavior.Default);
            Set(Reader, "activeLobOrdinal", -1); Set(Reader, "sequentialLobUnitOrdinal", -1); Set(Reader, "m_SequentialSeq", -1);
        }
        private static byte[] Wire(int type, int ns) => DmTemporalWireCodec.EncodeExact(new(2026, 7, 23, 14, 15, 16, ns, type == 27 ? -300 : null),
            DmTemporalWireCodec.ForCType(type), type == 16 ? 6 : 9);
        private static byte[] InlineClob()
        {
            byte[] text = Encoding.UTF8.GetBytes("A中é尾"), locator = new byte[47 + text.Length]; locator[0] = AbstractLob.STORAGE_IN_ROW;
            BinaryPrimitives.WriteInt32LittleEndian(locator.AsSpan(9), text.Length); text.CopyTo(locator, 47); return locator;
        }
        internal void RetireStatement() => Set(statement, "__t02_field_04000930", true);
        public async ValueTask DisposeAsync() { RetireStatement(); await Reader.DisposeAsync(); }
        private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
}
