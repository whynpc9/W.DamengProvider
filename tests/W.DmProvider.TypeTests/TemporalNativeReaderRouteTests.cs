using System.Collections;
using System.Buffers.Binary;
using System.Data;
using System.IO;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeReaderRouteTests
{
    private static readonly DateTime Timestamp = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1);

    [Fact]
    public async Task PublicTimestampGettersKeepTheOfficialSeventhDigitAfterLegacyProjectionCloses()
    {
        byte[] wire = Convert.FromHexString("EA87BBEE81C8000000");
        Assert.Throws<OverflowException>(() => DmDateTime.DmTimeFromRec4(wire, 26));
        using var fixture = new ReaderFixture(26, 7, wire);
        Assert.Equal(Timestamp, fixture.Reader.GetDateTime(0));
        Assert.Equal(Timestamp, fixture.Reader.GetValue(0));
        Assert.Equal(Timestamp, fixture.Reader.GetProviderSpecificValue(0));
        Assert.Equal(Timestamp, fixture.Reader.GetFieldValue<DateTime>(0));
        Assert.Equal(Timestamp, await fixture.Reader.GetFieldValueAsync<DateTime>(0, CancellationToken.None));
        Assert.Equal("2026-07-23 14:15:16.0000001", fixture.Reader.GetString(0));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
    }

    [Theory]
    [InlineData(0, "EA87BBEE81C80000000000")]
    [InlineData(480, "EA87BBEE81C8000000E001")]
    [InlineData(-300, "EA87BBEE81C8000000D4FE")]
    [InlineData(330, "EA87BBEE81C80000004A01")]
    public async Task PublicOffsetGettersKeepSeventhDigitAndOriginalOffset(int minutes, string golden)
    {
        byte[] wire = Convert.FromHexString(golden);
        Assert.Throws<OverflowException>(() => DmDateTime.DmTimeFromRec4(wire, 27));
        using var fixture = new ReaderFixture(27, 7, wire);
        DateTimeOffset expected = new(Timestamp, TimeSpan.FromMinutes(minutes));
        Assert.Equal(expected, fixture.Reader.GetDateTimeOffset(0));
        Assert.Equal(expected, fixture.Reader.GetValue(0));
        Assert.Equal(expected, fixture.Reader.GetProviderSpecificValue(0));
        Assert.Equal(expected, fixture.Reader.GetFieldValue<DateTimeOffset>(0));
        Assert.Equal(expected, await fixture.Reader.GetFieldValueAsync<DateTimeOffset>(0, CancellationToken.None));
        Assert.Equal(Timestamp, fixture.Reader.GetDateTime(0));
        Assert.Throws<InvalidCastException>(() => fixture.Reader.GetFieldValue<DateTime>(0));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task LegalSubTickFailurePreservesOtherColumnsLobAndNextRow(bool sequential, bool async)
    {
        byte[] first = DmTemporalWireCodec.EncodeExact(new(2026, 7, 23, 14, 15, 16, 1), DmTemporalWireLayout.Timestamp9, 9);
        byte[] next = DmTemporalWireCodec.EncodeExact(new(2026, 7, 24, 14, 15, 16, 100), DmTemporalWireLayout.Timestamp9, 9);
        using var fixture = new ReaderFixture(26, 9, first, next, sequential);
        if (async)
            await Assert.ThrowsAsync<OverflowException>(() => fixture.Reader.GetFieldValueAsync<DateTime>(0, CancellationToken.None));
        else Assert.Throws<OverflowException>(() => fixture.Reader.GetDateTime(0));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        if (!sequential) Assert.Equal("2026-07-23 14:15:16.000000001", fixture.Reader.GetString(0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
        var chars = new char[4];
        Assert.Equal(4, fixture.Reader.GetChars(2, 0, chars, 0, chars.Length));
        Assert.Equal("A中é尾", new string(chars));
        if (sequential) Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetString(0)).Number);
        if (async) Assert.True(await fixture.Reader.ReadAsync(CancellationToken.None));
        else Assert.True(fixture.Reader.Read());
        Assert.Equal(new DateTime(2026, 7, 24, 14, 15, 16).AddTicks(1), fixture.Reader.GetDateTime(0));
        Assert.Equal(43, fixture.Reader.GetInt32(1));
        fixture.RetireSyntheticStatement();
        if (async) await fixture.Reader.CloseAsync(); else fixture.Reader.Close();
        Assert.True(fixture.Reader.IsClosed);
        Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Fact]
    public void TemporalNullKeepsDbNullAndTyped6081Contracts()
    {
        using var fixture = new ReaderFixture(26, 7, null);
        Assert.True(fixture.Reader.IsDBNull(0));
        Assert.Same(DBNull.Value, fixture.Reader.GetValue(0));
        Assert.Same(DBNull.Value, fixture.Reader.GetProviderSpecificValue(0));
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetDateTime(0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<DateTime>(0)).Number);
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    [Fact]
    public async Task PreCancelledCacheReadDoesNotConsumeTheNextRow()
    {
        byte[] wire = Convert.FromHexString("EA87BBEE81C8000000");
        using var fixture = new ReaderFixture(26, 7, wire, wire);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Reader.ReadAsync(cancellation.Token));
        Assert.Equal(Timestamp, fixture.Reader.GetDateTime(0));
        Assert.True(await fixture.Reader.ReadAsync(CancellationToken.None));
        Assert.Equal(Timestamp, fixture.Reader.GetDateTime(0));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
    }

    [Fact]
    public void TimeModesAndExplicitTimezoneProjectionKeepExistingAnchors()
    {
        using var plain = new ReaderFixture(15, 6, Convert.FromHexString("EE8180C403"));
        Assert.Equal(new DateTime(1900, 1, 1, 14, 15, 16).AddTicks(1234560), plain.Reader.GetDateTime(0));
        Assert.Equal(new TimeOnly(14, 15, 16).Add(TimeSpan.FromTicks(1234560)), plain.Reader.GetFieldValue<TimeOnly>(0));
        plain.Physical.ConnProperty.property[DmConst.PROP_KEY_DBTIME_TO_TIMESPAN] = true; // Internal fixture only; Builder still rejects this setting.
        Assert.Equal(new TimeSpan(0, 14, 15, 16).Add(TimeSpan.FromTicks(1234560)), plain.Reader.GetValue(0));
        using var zone = new ReaderFixture(22, 6, Convert.FromHexString("EE8180C403D4FE"));
        Assert.Equal(new DateTimeOffset(1900, 1, 1, 14, 15, 16, TimeSpan.FromMinutes(-300)).AddTicks(1234560), zone.Reader.GetDateTimeOffset(0));
    }

    [Fact]
    public void ReaderLayoutAndMetadataAreCheckedBeforeClrProjection()
    {
        using var badLength = new ReaderFixture(26, 7, new byte[8]);
        Assert.Throws<InvalidDataException>(() => badLength.Reader.GetDateTime(0));
        byte[] badFields = Convert.FromHexString("EA87BBEE81C8000000"); badFields[3] = 0xf9;
        using var badClock = new ReaderFixture(26, 7, badFields);
        Assert.Throws<InvalidDataException>(() => badClock.Reader.GetDateTime(0));
        using var local = new ReaderFixture(26, 7, Convert.FromHexString("EA87BBEE81C8000000"));
        local.Columns[0].mask = 4;
        Assert.Throws<NotSupportedException>(() => local.Reader.GetDateTime(0));
        Assert.Throws<NotSupportedException>(() => local.Reader.GetString(0));
        Assert.Equal(7, local.Columns[0].GetScale()); Assert.Equal(4, local.Columns[0].mask);
    }

    // Cached rows and complete inline CLOB locators test public conversion/recovery without a socket.
    // Server cancellation, transport framing and multi-page lease evidence remain the existing R3 suites' responsibility.
    private sealed class ReaderFixture : IDisposable
    {
        internal DmSession Session { get; } = new();
        internal DmDataReader Reader { get; }
        internal DmConnInstance Physical { get; }
        internal DmColumn[] Columns { get; }
        internal ReaderFixture(int type, int scale, byte[]? value, byte[]? next = null, bool sequential = false)
        {
            var writer = new TemporalNativeWriterAdapterTests.WriterFixture("8.1.5.60", 21, "UTF-8");
            Physical = writer.Physical;
            Physical.ConnProperty.NewLobFlag = true;
            Session.CompleteHandshakeForTests();
            var lease = Session.BeginExecution(DmOperationPurpose.Reader);
            Set(Physical, "<Session>k__BackingField", Session);
            var temporal = new DmColumn(Physical); temporal.SetCType(type); temporal.SetScale(scale);
            temporal.SetPrecision(DmTemporalWireCodec.GetLength(DmTemporalWireCodec.ForCType(type)));
            var integer = new DmColumn(Physical); integer.SetCType(7);
            var clob = new DmColumn(Physical); clob.SetCType(19);
            Columns = [temporal, integer, clob];
            var info = new DmInfo(); info.SetColumnsInfo(Columns);
            var cache = TemporalNativeWriterAdapterTests.WriterFixture.Uninitialized<DmResultSetCache>();
            cache.colNum = 3; cache.currentPos = cache.datasOffset = 0;
            cache.totalRowCount = next == null ? 1 : 2;
            byte[] locator = InlineClob("A中é尾");
            cache.datas = next == null ? [[[], value!, BitConverter.GetBytes(42), locator]]
                : [[[], value!, BitConverter.GetBytes(42), locator], [[], next, BitConverter.GetBytes(43), locator]];
            Reader = TemporalNativeWriterAdapterTests.WriterFixture.Uninitialized<DmDataReader>();
            Set(Reader, "procedureChildren", Array.Empty<DmDataReader>());
            Reader.m_Statement = writer.Statement; Reader.m_RowCount = cache.totalRowCount;
            Set(Reader, "executionLease", lease); Set(Reader, "ownsExecutionLease", true);
            Set(Reader, "asyncCloseGate", new object()); Set(Reader, "m_Conn", Physical);
            Set(Reader, "m_Clobs", new ArrayList { null, null, null });
            Set(Reader, "m_DbInfo", info); Set(Reader, "m_ColInfo", Columns); Set(Reader, "m_RsCache", cache);
            Set(Reader, "m_GetVal", new DmGetValue("UTF-8", writer.Statement, false, Columns, DmTemporalResultFormat.OrdinaryPacked));
            Set(Reader, "is_SequentialAccess", sequential);
            Set(Reader, "m_Behavior", sequential ? CommandBehavior.SequentialAccess : CommandBehavior.Default);
            Set(Reader, "activeLobOrdinal", -1); Set(Reader, "sequentialLobUnitOrdinal", -1); Set(Reader, "m_SequentialSeq", -1);
        }
        private static byte[] InlineClob(string text)
        {
            byte[] encoded = Encoding.UTF8.GetBytes(text); var bytes = new byte[47 + encoded.Length];
            bytes[0] = AbstractLob.STORAGE_IN_ROW;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9), encoded.Length); encoded.CopyTo(bytes, 47);
            return bytes;
        }
        private static void Set(object target, string name, object value) => TemporalNativeWriterAdapterTests.WriterFixture.Set(target, name, value);
        internal void RetireSyntheticStatement()
        {
            // There is no server statement in this fixture; the normal cleanup path observes it already retired.
            if (Reader.m_Statement != null) Set(Reader.m_Statement, "__t02_field_04000930", true);
        }
        public void Dispose() { RetireSyntheticStatement(); Reader.Dispose(); }
    }
}
