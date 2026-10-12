using System.Collections;
using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using W.Dm.util;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeReaderContractTests
{
    public static readonly string[] Columns = ["DATE14", "TIME15", "TIME_TZ22", "TS16", "TS26", "TS_TZ23", "TS_TZ27", "TLTZ16", "TLTZ26"];
    public static readonly string[] Getters = ["GetDateTime", "GetDateTimeOffset", "GetValue", "GetProviderSpecificValue",
        "GetFieldValue<DateTime>", "GetFieldValue<DateTimeOffset>", "GetFieldValue<DateOnly>", "GetFieldValue<TimeOnly>",
        "GetFieldValue<TimeSpan>", "GetString", "GetFieldValue<string>"];
    public static IEnumerable<object[]> Cells => Getters.SelectMany(getter => Columns.Select(column => new object[] { getter, column }));
    public static IEnumerable<object[]> GenericCells => Cells.Where(cell => ((string)cell[0]).StartsWith("GetFieldValue<", StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryGetterColumnContract(string getter, string column)
    {
        using var fixture = ReaderFixture.ForColumn(column);
        AssertContract(getter, column, () => Invoke(fixture.Reader, getter));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    [Theory]
    [MemberData(nameof(GenericCells))]
    public async Task GenericAsyncUsesTheSameContract(string getter, string column)
    {
        using var fixture = ReaderFixture.ForColumn(column);
        Exception? error = null;
        object? value = null;
        try { value = await InvokeAsync(fixture.Reader, getter, CancellationToken.None); }
        catch (Exception caught) { error = caught; }
        AssertContract(getter, column, () => error == null ? value : throw error);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void NullPrecedesTypeAndCapabilityChecks(string getter, string column)
    {
        using var fixture = ReaderFixture.ForColumn(column, nullValue: true);
        Assert.True(fixture.Reader.IsDBNull(0));
        if (getter is "GetValue" or "GetProviderSpecificValue") Assert.Same(DBNull.Value, Invoke(fixture.Reader, getter));
        else Assert.Equal(6081, Assert.Throws<DmException>(() => Invoke(fixture.Reader, getter)).Number);
        Assert.Same(DBNull.Value, fixture.Reader.GetFieldValue<object>(0));
        Assert.Same(DBNull.Value, fixture.Reader.GetFieldValue<DBNull>(0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    [Theory]
    [MemberData(nameof(GenericCells))]
    public async Task AsyncNullPrecedesTypeAndCapabilityChecks(string getter, string column)
    {
        using var fixture = ReaderFixture.ForColumn(column, nullValue: true);
        Assert.Equal(6081, (await Assert.ThrowsAsync<DmException>(() => InvokeAsync(fixture.Reader, getter, CancellationToken.None))).Number);
        Assert.Same(DBNull.Value, await fixture.Reader.GetFieldValueAsync<object>(0));
        Assert.Same(DBNull.Value, await fixture.Reader.GetFieldValueAsync<DBNull>(0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    public static IEnumerable<object[]> SubTicks =>
        from type in new[] { 26, 27 }
        from pair in new[] { (8, 10, "00000001"), (8, 90, "00000009"), (9, 1, "000000001"), (9, 99, "000000099") }
        select new object[] { type, pair.Item1, pair.Item2, pair.Item3 };

    [Theory]
    [MemberData(nameof(SubTicks))]
    public async Task ExactTextSurvivesEveryClrProjectionFailure(int type, int scale, int ns, string fraction)
    {
        using var fixture = new ReaderFixture(type, scale, Wire(type, ns, type == 27 ? -300 : null));
        string expected = "2026-07-23 14:15:16." + fraction + (type == 27 ? " -05:00" : "");
        foreach (string getter in Getters.Where(name => name != "GetString" && (type == 27 || name != "GetDateTimeOffset")))
        {
            Assert.Throws<OverflowException>(() => Invoke(fixture.Reader, getter));
            Assert.Equal(expected, fixture.Reader.GetString(0));
        }
        if (type == 26) Assert.Throws<InvalidCastException>(() => fixture.Reader.GetDateTimeOffset(0));
        foreach (string getter in Getters.Where(name => name.StartsWith("GetFieldValue<", StringComparison.Ordinal)))
            await Assert.ThrowsAsync<OverflowException>(() => InvokeAsync(fixture.Reader, getter, CancellationToken.None));
        Assert.Equal(expected, fixture.Reader.GetString(0));
        Assert.Equal(scale, fixture.Reader.GetSchemaTable()!.Rows[0]["NumericScale"]);
        Assert.Throws<InvalidCastException>(() => fixture.Reader.GetChars(0, 0, null!, 0, 0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
    }

    [Theory]
    [InlineData(8, 0, "00000000", 0)]
    [InlineData(8, 100, "00000010", 1)]
    [InlineData(9, 0, "000000000", 0)]
    [InlineData(9, 100, "000000100", 1)]
    public void HighScaleZeroAndExactTickKeepDeclaredZeros(int scale, int ns, string fraction, int ticks)
    {
        using var fixture = new ReaderFixture(26, scale, Wire(26, ns));
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(ticks), fixture.Reader.GetDateTime(0));
        Assert.Equal("2026-07-23 14:15:16." + fraction, fixture.Reader.GetString(0));
    }

    [Fact]
    public void InvalidDeclaredFractionFailsTextAndNumericBeforeProjection()
    {
        using var fixture = new ReaderFixture(26, 8, Wire(26, 1)); // Encoded as scale9, inconsistent with the column's scale8.
        Assert.Throws<System.IO.InvalidDataException>(() => fixture.Reader.GetString(0));
        Assert.Throws<System.IO.InvalidDataException>(() => fixture.Reader.GetDateTime(0));
    }

    [Theory]
    [InlineData(-1, "-0001-07-23 14:15:16.000000100")]
    [InlineData(-4712, "-4712-07-23 14:15:16.000000100")]
    public void ValidWireCalendarOutsideClrStillHasExactText(int year, string expected)
    {
        using var fixture = new ReaderFixture(26, 9, DmTemporalWireCodec.EncodeExact(new(year, 7, 23, 14, 15, 16, 100), DmTemporalWireLayout.Timestamp9, 9));
        Assert.Throws<OverflowException>(() => fixture.Reader.GetValue(0));
        Assert.Equal(expected, fixture.Reader.GetString(0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InternalTimeModeKeepsObjectAndExactCastContracts(bool spanMode)
    {
        foreach (string getter in Getters)
        {
            using var fixture = ReaderFixture.ForColumn("TIME15", spanMode: spanMode);
            if (getter == "GetFieldValue<DateTime>" && spanMode || getter == "GetFieldValue<TimeSpan>" && !spanMode)
                Assert.Throws<InvalidCastException>(() => Invoke(fixture.Reader, getter));
            else if (getter is "GetValue" or "GetProviderSpecificValue" or "GetFieldValue<DateTime>" or "GetFieldValue<TimeSpan>")
                Assert.Equal(spanMode ? (object)ClockSpan : Clock, Invoke(fixture.Reader, getter));
            else AssertContract(getter, "TIME15", () => Invoke(fixture.Reader, getter));
        }
    }

    [Theory]
    [InlineData(16, 6)] [InlineData(26, 8)] [InlineData(26, 9)]
    public void ClosedLocalTimezoneStillExposesSchemaRawMetadataAndNull(int type, int scale)
    {
        using var fixture = new ReaderFixture(type, scale | 0x1000, Wire(type, 0));
        var raw = new DmTemporalRawMetadata(DmTemporalMetadataSource.ResultColumn, type, 37, scale | 0x1000, 0, 0, 4, DmTemporalResultFormat.Unknown);
        fixture.Columns[0].CaptureTemporalServerMetadata(raw);
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetString(0));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetValue(0));
        Assert.Throws<InvalidCastException>(() => fixture.Reader.GetDateTimeOffset(0));
        Assert.Equal(typeof(DateTime), fixture.Reader.GetFieldType(0));
        Assert.Equal(typeof(DateTime), fixture.Reader.GetProviderSpecificFieldType(0));
        Assert.Equal("SERVER_TEMPORAL_ALIAS", fixture.Reader.GetDataTypeName(0));
        DataRow row = fixture.Reader.GetSchemaTable()!.Rows[0];
        Assert.Equal(scale, row["NumericScale"]); Assert.Equal(9, row["NumericPrecision"]); Assert.Equal(9, row["ColumnSize"]);
        Assert.Equal(typeof(DateTime), row["DataType"]);
        var snapshot = fixture.Columns[0].GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked);
        Assert.Equal(raw, snapshot.RawMetadata); Assert.Equal(DmTemporalResultFormat.Unknown, snapshot.RawMetadata!.Value.ResultFormat);
        Assert.Equal(DmTemporalResultFormat.OrdinaryPacked, snapshot.ActualFormat); Assert.Equal(4, snapshot.Mask);
        fixture.Columns[0].SetScale(0); fixture.Columns[0].mask = 0;
        Assert.Equal(scale, snapshot.FractionalScale); Assert.Equal(scale | 0x1000, snapshot.WireScale);
        using var nullFixture = ReaderFixture.ForColumn(type == 16 ? "TLTZ16" : "TLTZ26", nullValue: true);
        Assert.Same(DBNull.Value, nullFixture.Reader.GetValue(0));
        Assert.Equal(6081, Assert.Throws<DmException>(() => nullFixture.Reader.GetString(0)).Number);
    }

    [Theory]
    [InlineData(26, 8)] [InlineData(27, 9)]
    public void HighPrecisionSchemaKeepsClrTypeAndActualServerName(int type, int scale)
    {
        using var fixture = new ReaderFixture(type, scale, Wire(type, 0, type == 27 ? -300 : null));
        Type expected = type == 26 ? typeof(DateTime) : typeof(DateTimeOffset);
        Assert.Equal(expected, fixture.Reader.GetFieldType(0));
        Assert.Equal(expected, fixture.Reader.GetProviderSpecificFieldType(0));
        Assert.Equal("SERVER_TEMPORAL_ALIAS", fixture.Reader.GetDataTypeName(0));
        var row = fixture.Reader.GetSchemaTable()!.Rows[0];
        Assert.Equal(expected, row["DataType"]); Assert.Equal(scale, row["NumericScale"]);
    }

    [Theory]
    [InlineData(1574, true)] [InlineData(1590, false)]
    public void IntervalSchemaMatchesFieldTypeAndPublicObject(int packedScale, bool span)
    {
        using var fixture = new ReaderFixture(21, packedScale, IntervalWire(1, packedScale));
        Type expected = span ? typeof(TimeSpan) : typeof(DmIntervalDT);
        Assert.Equal(expected, fixture.Reader.GetFieldType(0));
        Assert.Equal(expected, fixture.Reader.GetSchemaTable()!.Rows[0]["DataType"]);
        Assert.Equal(expected, fixture.Reader.GetValue(0).GetType());
        Assert.Equal(6, fixture.Reader.GetSchemaTable()!.Rows[0]["NumericScale"]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InternalIntervalModesKeepYearMonthZeroScaleAndDaySecondObjectMapping(bool modeEnabled)
    {
        using var ym = new ReaderFixture(20, (2 << 8) | (3 << 4), new byte[12]);
        ym.Physical.ConnProperty.property[DmConst.PROP_KEY_INTERVAL_MODE] = modeEnabled ? W.Dm.Config.IntervalMode.ALL : W.Dm.Config.IntervalMode.OFF;
        Assert.Equal(0, ym.Reader.GetSchemaTable()!.Rows[0]["NumericScale"]);
        Assert.Equal(ym.Reader.GetFieldType(0), ym.Reader.GetSchemaTable()!.Rows[0]["DataType"]);
        using var dt = new ReaderFixture(21, 1590, IntervalWire(1, 1590));
        dt.Physical.ConnProperty.property[DmConst.PROP_KEY_INTERVAL_MODE] = modeEnabled ? W.Dm.Config.IntervalMode.ALL : W.Dm.Config.IntervalMode.OFF;
        Type expected = modeEnabled ? typeof(TimeSpan) : typeof(DmIntervalDT);
        Assert.Equal(expected, dt.Reader.GetValue(0).GetType());
        Assert.Equal(expected, dt.Reader.GetFieldType(0)); Assert.Equal(expected, dt.Reader.GetSchemaTable()!.Rows[0]["DataType"]);
    }

    [Fact]
    public void EveryResultCapturesCurrentFormatAndMetadataWithoutReusingOldSnapshot()
    {
        using var fixture = new ReaderFixture(26, 9, Wire(26, 1));
        var old = fixture.Columns[0].GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked);
        Assert.Throws<OverflowException>(() => fixture.Reader.GetValue(0));
        fixture.ApplyResult(27, 8, CompactWire(10, -300), DmTemporalResultFormat.Bdta);
        Assert.Equal("2026-07-23 14:15:16.00000001 -05:00", fixture.Reader.GetString(0));
        Assert.Equal(8, fixture.Reader.GetSchemaTable()!.Rows[0]["NumericScale"]);
        Assert.Equal(typeof(DateTimeOffset), fixture.Reader.GetFieldType(0));
        Assert.Equal(9, old.FractionalScale); Assert.Equal(26, old.CType);
        fixture.ApplyResult(26, 7, Wire(26, 100), DmTemporalResultFormat.OrdinaryPacked);
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1), fixture.Reader.GetDateTime(0));
        Assert.Equal("2026-07-23 14:15:16.0000001", fixture.Reader.GetString(0));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task FailurePreservesForwardRowsLobCleanupAndCancellationPriority(bool sequential, bool async)
    {
        using var fixture = new ReaderFixture(27, 9, Wire(27, 1, -300), sequential: sequential, next: Wire(27, 100, -300));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Reader.GetFieldValueAsync<DateTimeOffset>(0, cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
        if (async) await Assert.ThrowsAsync<OverflowException>(() => fixture.Reader.GetFieldValueAsync<DateTimeOffset>(0));
        else Assert.Throws<OverflowException>(() => fixture.Reader.GetDateTimeOffset(0));
        Assert.Equal(typeof(DateTimeOffset), fixture.Reader.GetSchemaTable()!.Rows[0]["DataType"]);
        if (!sequential) Assert.Equal("2026-07-23 14:15:16.000000001 -05:00", fixture.Reader.GetString(0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
        Assert.Equal("A中é尾", new string(ReadClob(fixture.Reader)));
        if (sequential) Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetString(0)).Number);
        if (async) Assert.True(await fixture.Reader.ReadAsync()); else Assert.True(fixture.Reader.Read());
        var expected = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromHours(-5)).AddTicks(1);
        Assert.Equal(expected, fixture.Reader.GetDateTimeOffset(0));
        Assert.Equal(43, fixture.Reader.GetInt32(1));
        fixture.RetireStatement();
        if (async) await fixture.Reader.DisposeAsync(); else fixture.Reader.Dispose();
        Assert.True(fixture.Reader.IsClosed); Assert.Equal(DmPhysicalSessionState.Ready, fixture.Session.State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedProjectionStillHonorsCloseConnectionCleanup(bool async)
    {
        using var fixture = new ReaderFixture(26, 9, Wire(26, 1), closeConnection: true);
        Assert.Throws<OverflowException>(() => fixture.Reader.GetValue(0));
        Assert.Equal("A中é尾", new string(ReadClob(fixture.Reader)));
        fixture.RetireStatement();
        if (async) await fixture.Reader.CloseAsync(); else fixture.Reader.Close();
        Assert.True(fixture.Reader.IsClosed); Assert.Equal(DmPhysicalSessionState.Closed, fixture.Session.State);
        Assert.Equal(ConnectionState.Closed, fixture.Physical.Conn.State);
    }

    [Theory]
    [InlineData("en-US")] [InlineData("fr-FR")] [InlineData("zh-CN")]
    public void FractionFormattingUsesDeclaredPrecisionAndInvariantDigits(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("", DateUtil.formatMilliSecond(0, 0));
            Assert.Equal("1", DateUtil.formatMilliSecond(100000000, 1));
            Assert.Equal("00000001", DateUtil.formatMilliSecond(10, 8));
            Assert.Equal("000000001", DateUtil.formatMilliSecond(1, 9));
            Assert.Equal("100000000", DateUtil.formatMilliSecond(100000000, 9));
            using var fixture = new ReaderFixture(27, 9, Wire(27, 1, -300));
            Assert.Equal("2026-07-23 14:15:16.000000001 -05:00", fixture.Reader.GetString(0));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(-1, 9)] [InlineData(1000000000, 9)] [InlineData(1, 8)] [InlineData(1, 0)] [InlineData(0, -1)] [InlineData(0, 10)]
    public void FractionFormatterRejectsInexactOrInvalidDeclaration(int ns, int scale) =>
        Assert.Throws<OverflowException>(() => DateUtil.formatMilliSecond(ns, scale));

    [Fact]
    public void ExplicitDisplayTemplateKeepsItsOwnProjectionAndSeparators()
    {
        using var fixture = new ReaderFixture(27, 9, Wire(27, 123456789, -300));
        fixture.Physical.ConnProperty.oracleTimestampTZFormat = "YYYY/MM/DD HH24:MI:SS.FF3 TZH:TZM";
        Assert.Equal("2026/07/23 14:15:16.123 -05:00", fixture.Reader.GetString(0));
        fixture.Physical.ConnProperty.oracleTimestampTZFormat = "";
        Assert.Equal("2026-07-23 14:15:16.123456789 -05:00", fixture.Reader.GetString(0));
    }

    [Fact]
    public void UnknownActualFormatCannotBorrowRawOrFieldOrdinaryLabels()
    {
        using var fixture = new ReaderFixture(26, 7, Wire(26, 100), format: DmTemporalResultFormat.Unknown);
        fixture.Columns[0].CaptureTemporalServerMetadata(new(DmTemporalMetadataSource.ResultColumn, 26, 9, 7, 0, 0, 0, DmTemporalResultFormat.OrdinaryPacked));
        Assert.False(fixture.Columns[0].isBdta);
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetDateTime(0));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetString(0));
        Assert.Equal(7, fixture.Reader.GetSchemaTable()!.Rows[0]["NumericScale"]);
    }

    [Fact]
    public void OrdinaryTwelveBytesCannotBorrowLegacyMicrosecondsOrCompactNanoseconds()
    {
        using var fixture = new ReaderFixture(26, 9, CompactWire(1, 0)[..12]);
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetString(0));
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetDateTime(0));
        Assert.Equal(42, fixture.Reader.GetInt32(1));
    }

    [Fact]
    public void OracleDateMaskKeepsExistingDefaultDisplayProjection()
    {
        using var fixture = new ReaderFixture(26, 7 | 0x2000, Wire(26, 100));
        Assert.Equal("2026-07-23", fixture.Reader.GetString(0));
        Assert.Equal(7, fixture.Reader.GetSchemaTable()!.Rows[0]["NumericScale"]);
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1), fixture.Reader.GetDateTime(0));
    }

    [Theory]
    [InlineData(3, 0)] [InlineData(9, 6)]
    public void Db2NIntervalUsesActualWireLeadingAndFractionDeclaration(int leading, int fraction)
    {
        int packed = (6 << 8) | (leading << 4) | fraction;
        byte[] bytes = IntervalWire(123, packed);
        var field = new DmField("UTF-8"); field.SetCType(21); field.SetScale(1574); // Not the wire declaration.
        var value = Assert.IsType<DmIntervalDT>(DB2N.toObject(bytes, field, null!, null!));
        Assert.Equal(packed, value.getScaleForSvr()); Assert.Equal(123, value.days);
        Assert.Equal(value.GetDTString(), DB2N.toString(bytes, field, null!));
        Assert.Throws<OverflowException>(() => new DmIntervalDT(bytes));
    }

    [Fact]
    public void Db2NKeepsUnknownRefusalExactTemporalProjectionAndTimezoneObjectShape()
    {
        var field = new DmField("UTF-8"); field.SetCType(26); field.SetScale(7);
        byte[] bytes = Wire(26, 100);
        Assert.Throws<NotSupportedException>(() => DB2N.toObject(bytes, field, null!, null!));
        Assert.Throws<NotSupportedException>(() => DB2N.toString(bytes, field, null!));
        Assert.Throws<NotSupportedException>(() => DB2N.toDate(bytes, field, null!));
        Assert.Throws<NotSupportedException>(() => DB2N.toTime(bytes, field, null!));
        var format = DmTemporalResultFormat.OrdinaryPacked;
        Assert.Equal(new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1), DB2N.toObject(bytes, field, null!, null!, format));
        Assert.Equal(new DateTime(2026, 7, 23), DB2N.toDate(bytes, field, null!, format));
        Assert.Equal(new DateTime(1900, 1, 1, 14, 15, 16).AddTicks(1), DB2N.toTime(bytes, field, null!, format));
        field.SetCType(27); field.SetScale(9); bytes = Wire(27, 1, -300);
        var provider = Assert.IsType<DmDateTime>(DB2N.toObject(bytes, field, null!, null!, format));
        Assert.Equal(27, provider.dtype); Assert.Equal(9, provider.scale); Assert.Equal(1, provider.dt[6]); Assert.Equal(-300, provider.dt[7]);
        Assert.Equal("2026-07-23 14:15:16.000000001 -05:00", DB2N.toString(bytes, field, null!, format));
        Assert.Throws<OverflowException>(() => DB2N.toTime(bytes, field, null!, format));
        field.SetCType(7); field.SetScale(0);
        Assert.Equal(42, DB2N.toObject(BitConverter.GetBytes(42), field, null!, null!));
    }

    private static readonly DateTime Stamp = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1234560);
    private static readonly DateTime Clock = new DateTime(1900, 1, 1, 14, 15, 16).AddTicks(1234560);
    private static readonly TimeSpan ClockSpan = new TimeSpan(0, 14, 15, 16).Add(TimeSpan.FromTicks(1234560));
    private static void AssertContract(string getter, string column, Func<object?> action)
    {
        if (column.StartsWith("TLTZ", StringComparison.Ordinal))
        {
            if (getter == "GetDateTimeOffset") Assert.Throws<InvalidCastException>(action);
            else Assert.Throws<NotSupportedException>(action);
            return;
        }
        bool time = column is "TIME15" or "TIME_TZ22", tz = column.Contains("TZ", StringComparison.Ordinal);
        DateTime local = column == "DATE14" ? new(2026, 7, 23) : time ? Clock : column is "TS26" or "TS_TZ27" ? new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1) : Stamp;
        object? expected = getter switch
        {
            "GetDateTime" => local,
            "GetDateTimeOffset" when tz => new DateTimeOffset(local, TimeSpan.FromHours(-5)),
            "GetValue" or "GetProviderSpecificValue" => tz ? (object)new DateTimeOffset(local, TimeSpan.FromHours(-5)) : (object)local,
            "GetFieldValue<DateTime>" when !tz => local,
            "GetFieldValue<DateTimeOffset>" when tz => new DateTimeOffset(local, TimeSpan.FromHours(-5)),
            "GetFieldValue<DateOnly>" when column == "DATE14" => new DateOnly(2026, 7, 23),
            "GetFieldValue<TimeOnly>" when column == "TIME15" => new TimeOnly(14, 15, 16).Add(TimeSpan.FromTicks(1234560)),
            "GetString" => column switch
            {
                "DATE14" => "2026-07-23", "TIME15" => "14:15:16.123456", "TIME_TZ22" => "14:15:16.123456 -05:00",
                "TS16" => "2026-07-23 14:15:16.123456", "TS26" => "2026-07-23 14:15:16.0000001",
                "TS_TZ23" => "2026-07-23 14:15:16.123456 -05:00", "TS_TZ27" => "2026-07-23 14:15:16.0000001 -05:00", _ => throw new ArgumentException(column)
            },
            _ => null
        };
        if (expected == null) { Assert.Throws<InvalidCastException>(action); return; }
        if (getter is "GetValue" or "GetProviderSpecificValue")
            Assert.Equal(tz ? typeof(DateTimeOffset) : typeof(DateTime), expected.GetType());
        object? actual = action(); Assert.Equal(expected.GetType(), actual!.GetType()); Assert.Equal(expected, actual);
        if (actual is DateTime date) Assert.Equal(DateTimeKind.Unspecified, date.Kind);
        if (actual is DateTimeOffset offset) { Assert.Equal(local.Ticks, offset.Ticks); Assert.Equal(TimeSpan.FromHours(-5), offset.Offset); }
    }

    internal static object? Invoke(DmDataReader reader, string getter) => getter switch
    {
        "GetDateTime" => reader.GetDateTime(0), "GetDateTimeOffset" => reader.GetDateTimeOffset(0),
        "GetValue" => reader.GetValue(0), "GetProviderSpecificValue" => reader.GetProviderSpecificValue(0), "GetString" => reader.GetString(0),
        "GetFieldValue<DateTime>" => reader.GetFieldValue<DateTime>(0), "GetFieldValue<DateTimeOffset>" => reader.GetFieldValue<DateTimeOffset>(0),
        "GetFieldValue<DateOnly>" => reader.GetFieldValue<DateOnly>(0), "GetFieldValue<TimeOnly>" => reader.GetFieldValue<TimeOnly>(0),
        "GetFieldValue<TimeSpan>" => reader.GetFieldValue<TimeSpan>(0), "GetFieldValue<string>" => reader.GetFieldValue<string>(0),
        _ => throw new ArgumentException(getter)
    };
    private static async Task<object?> InvokeAsync(DmDataReader reader, string getter, CancellationToken token) => getter switch
    {
        "GetFieldValue<DateTime>" => await reader.GetFieldValueAsync<DateTime>(0, token),
        "GetFieldValue<DateTimeOffset>" => await reader.GetFieldValueAsync<DateTimeOffset>(0, token),
        "GetFieldValue<DateOnly>" => await reader.GetFieldValueAsync<DateOnly>(0, token),
        "GetFieldValue<TimeOnly>" => await reader.GetFieldValueAsync<TimeOnly>(0, token),
        "GetFieldValue<TimeSpan>" => await reader.GetFieldValueAsync<TimeSpan>(0, token),
        "GetFieldValue<string>" => await reader.GetFieldValueAsync<string>(0, token),
        _ => throw new ArgumentException(getter)
    };
    private static char[] ReadClob(DmDataReader reader) { var chars = new char[4]; Assert.Equal(4, reader.GetChars(2, 0, chars, 0, 4)); return chars; }
    internal static byte[] Wire(int type, int ns, int? offset = null) => DmTemporalWireCodec.EncodeExact(
        type is 15 or 22 ? new(0, 0, 0, 14, 15, 16, ns, offset) : type == 14 ? new(2026, 7, 23, 0, 0, 0, 0) : new(2026, 7, 23, 14, 15, 16, ns, offset),
        DmTemporalWireCodec.ForCType(type), type == 14 ? 0 : type is 26 or 27 ? 9 : 6);
    private static byte[] IntervalWire(int days, int scale)
    {
        byte[] wire = new byte[24]; BinaryPrimitives.WriteInt32LittleEndian(wire, days); BinaryPrimitives.WriteInt32LittleEndian(wire.AsSpan(20), scale); return wire;
    }
    private static byte[] CompactWire(int ns, short offset)
    {
        byte[] wire = new byte[13]; BinaryPrimitives.WriteInt16LittleEndian(wire, 2026);
        wire[2] = 7; wire[3] = 23; wire[4] = 14; wire[5] = 15; wire[6] = 16;
        wire[7] = (byte)ns; wire[8] = (byte)(ns >> 8); wire[9] = (byte)(ns >> 16); wire[12] = (byte)(ns >> 24);
        BinaryPrimitives.WriteInt16LittleEndian(wire.AsSpan(10), offset); return wire;
    }

    internal sealed class ReaderFixture : IDisposable
    {
        internal DmSession Session { get; } = new();
        internal DmDataReader Reader { get; }
        internal DmConnInstance Physical => writer.Physical;
        internal DmColumn[] Columns { get; private set; }
        private readonly TemporalNativeWriterAdapterTests.WriterFixture writer = new("8.1.5.60", 21, "UTF-8");
        internal static ReaderFixture ForColumn(string column, bool nullValue = false, bool spanMode = false)
        {
            int type = column switch { "DATE14" => 14, "TIME15" => 15, "TIME_TZ22" => 22, "TS16" or "TLTZ16" => 16, "TS26" or "TLTZ26" => 26, "TS_TZ23" => 23, "TS_TZ27" => 27, _ => throw new ArgumentException(column) };
            int scale = type == 14 ? 0 : type is 26 or 27 ? 7 : 6;
            byte[]? bytes = nullValue ? null : Wire(type, type == 14 ? 0 : type is 26 or 27 ? 100 : 123456000, type is 22 or 23 or 27 ? -300 : null);
            var fixture = new ReaderFixture(type, scale | (column.StartsWith("TLTZ", StringComparison.Ordinal) ? 0x1000 : 0), bytes);
            fixture.Physical.ConnProperty.property[DmConst.PROP_KEY_DBTIME_TO_TIMESPAN] = spanMode;
            return fixture;
        }
        internal ReaderFixture(int type, int scale, byte[]? bytes, bool sequential = false, byte[]? next = null, DmTemporalResultFormat format = DmTemporalResultFormat.OrdinaryPacked, bool closeConnection = false)
        {
            Physical.ConnProperty.NewLobFlag = true; Session.CompleteHandshakeForTests();
            var lease = Session.BeginExecution(DmOperationPurpose.Reader);
            Set(Physical, "<Session>k__BackingField", Session);
            if (closeConnection)
            {
                Set(writer.Connection, "session", Session); Set(writer.Connection, "connectionState", ConnectionState.Open);
            }
            Columns = CreateColumns(type, scale);
            var info = new DmInfo(); info.SetColumnsInfo(Columns); info.rsBdta = format == DmTemporalResultFormat.Bdta;
            var cache = Cache(bytes, next);
            Reader = TemporalNativeWriterAdapterTests.WriterFixture.Uninitialized<DmDataReader>();
            Reader.m_Statement = writer.Statement; Reader.m_RowCount = cache.totalRowCount;
            Set(Reader, "executionLease", lease); Set(Reader, "ownsExecutionLease", true); Set(Reader, "asyncCloseGate", new object()); Set(Reader, "m_Conn", Physical);
            Set(Reader, "m_Clobs", new ArrayList { null, null, null }); Set(Reader, "m_DbInfo", info); Set(Reader, "m_ColInfo", Columns); Set(Reader, "m_RsCache", cache);
            Reader.bdta = info.rsBdta; Set(Reader, "m_GetVal", new DmGetValue("UTF-8", writer.Statement, false, Columns, format));
            Set(Reader, "is_SequentialAccess", sequential);
            Set(Reader, "m_Behavior", (sequential ? CommandBehavior.SequentialAccess : CommandBehavior.Default) | (closeConnection ? CommandBehavior.CloseConnection : CommandBehavior.Default));
            Set(Reader, "activeLobOrdinal", -1); Set(Reader, "sequentialLobUnitOrdinal", -1); Set(Reader, "m_SequentialSeq", -1);
        }
        private DmColumn[] CreateColumns(int type, int scale)
        {
            var temporal = new DmColumn(Physical); temporal.SetCType(type); temporal.SetScale(scale); temporal.SetPrecision(type == 21 ? 24 : type == 14 ? 3 : 9);
            temporal.SetSize(type == 21 ? 24 : type == 14 ? 3 : 9); temporal.name = "temporal"; temporal.typeName = "SERVER_TEMPORAL_ALIAS";
            var integer = new DmColumn(Physical); integer.SetCType(7); integer.name = "integer";
            var clob = new DmColumn(Physical); clob.SetCType(19); clob.name = "lob";
            return [temporal, integer, clob];
        }
        private static DmResultSetCache Cache(byte[]? bytes, byte[]? next)
        {
            var cache = TemporalNativeWriterAdapterTests.WriterFixture.Uninitialized<DmResultSetCache>();
            cache.colNum = 3; cache.currentPos = cache.datasOffset = 0; cache.totalRowCount = next == null ? 1 : 2;
            byte[] encoded = Encoding.UTF8.GetBytes("A中é尾"), locator = new byte[47 + encoded.Length]; locator[0] = AbstractLob.STORAGE_IN_ROW;
            BinaryPrimitives.WriteInt32LittleEndian(locator.AsSpan(9), encoded.Length); encoded.CopyTo(locator, 47);
            cache.datas = next == null ? [[[], bytes!, BitConverter.GetBytes(42), locator]] : [[[], bytes!, BitConverter.GetBytes(42), locator], [[], next, BitConverter.GetBytes(43), locator]];
            return cache;
        }
        internal void ApplyResult(int type, int scale, byte[] bytes, DmTemporalResultFormat format)
        {
            Columns = CreateColumns(type, scale);
            var info = new DmInfo(); info.SetColumnsInfo(Columns); info.SetHasResultSet(true); info.SetRowCount(1); info.rsBdta = format == DmTemporalResultFormat.Bdta;
            Set(writer.Statement, "__t02_field_04000928", Cache(bytes, null));
            // Exercise the real per-result publication method without inventing transport/protocol evidence.
            typeof(DmDataReader).GetMethod("ApplyResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Reader, [info, writer.Statement]);
        }
        internal void RetireStatement() => Set(writer.Statement, "__t02_field_04000930", true);
        internal static void Set(object target, string field, object value) => TemporalNativeWriterAdapterTests.WriterFixture.Set(target, field, value);
        public void Dispose() { RetireStatement(); Reader.Dispose(); }
    }
}
