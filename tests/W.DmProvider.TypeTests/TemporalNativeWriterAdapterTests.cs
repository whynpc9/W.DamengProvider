using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;
using LegacyStatement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeWriterAdapterTests
{
    [Theory]
    [InlineData("8.1.5.60", 21, "utf-8", true, 2)]
    [InlineData("8.1.5.60", 21, "UTF-8", true, 2)]
    [InlineData("8.1.5.60", 21, "GB18030", false, 0)]
    [InlineData("8.1.5.60", 20, "utf-8", false, 0)]
    [InlineData("8.1.5.60", 22, "utf-8", false, 0)]
    [InlineData("8.1.5.59", 21, "utf-8", false, 0)]
    [InlineData("8.1.5.61", 21, "utf-8", false, 0)]
    [InlineData("8.1.5.60 ", 21, "utf-8", false, 0)]
    [InlineData("8.1.4.6", 11, "gb18030", false, 1)]
    [InlineData("8.1.4.6", 21, "gb18030", false, 0)]
    [InlineData("8.1.4.6", 11, "utf-8", false, 0)]
    [InlineData(null, 0, null, false, 0)]
    public void ProductionWriterUsesFullNegotiatedScope(string? version, int message, string? encoding, bool seven, int state)
    {
        var fixture = new WriterFixture(version, message, encoding);
        var profile = DmTemporalLegacyAdapter.Profile(fixture.Connection);
        Assert.Equal((DmTemporalCapabilityState)state, profile.State);
        Assert.True(profile.Identity.EncodingOverrideEmpty);
        if (state == 2) Assert.Equal("TP01-v11-shared-physical", profile.EvidenceId);
        if (state == 1) Assert.Equal("TP01-v11-tls-physical", profile.EvidenceId);
        var fields = fixture.Parameter(26, 7, fixedSource: true);
        DateTime value = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1);
        if (seven)
        {
            byte[] actual = fixture.Write(value, fields, 7);
            Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000"), actual);
            Assert.Equal(actual, fixture.Write("2026-07-23 14:15:16.0000001", fields, 7));
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => fixture.Write(value, fields, 7));
            Assert.Throws<NotSupportedException>(() => fixture.Write("2026-07-23 14:15:16.0000000", fields, 7));
            Assert.Throws<NotSupportedException>(() => fixture.Write(DBNull.Value, fields, 7));
        }
    }

    [Theory]
    [InlineData(true, "utf-8")] [InlineData(true, "UTF-8")] [InlineData(true, "gb18030")]
    [InlineData(true, " ")] [InlineData(false, null)] [InlineData(true, null)]
    [InlineData(true, 123)] [InlineData(true, true)]
    public void EncodingOverrideMustBePresentStringAndEmptyBeforeAnyVerifiedCapability(bool present, object? value)
    {
        foreach (var fixture in new[] { new WriterFixture("8.1.5.60", 21, "utf-8"), new WriterFixture("8.1.4.6", 11, "gb18030") })
        {
            var seven = fixture.Parameter(26, 7, fixedSource: true);
            var legacy = fixture.Parameter(16, 6, fixedSource: true);
            var fixedSix = fixture.Parameter(26, 6, fixedSource: true);
            if (present) fixture.Physical.ConnProperty.property[DmConst.PROP_KEY_ENCODING] = value!;
            else fixture.Physical.ConnProperty.property.Remove(DmConst.PROP_KEY_ENCODING);
            var profile = DmTemporalLegacyAdapter.Profile(fixture.Connection);
            Assert.Equal(DmTemporalCapabilityState.CompatibilityLegacyUnverified, profile.State);
            Assert.False(profile.Identity.EncodingOverrideEmpty); Assert.Null(profile.Identity.ServerEncoding);
            Assert.Throws<NotSupportedException>(() => fixture.Write(new DateTime(2026, 7, 23).AddTicks(1), seven, 7));
            Assert.Throws<NotSupportedException>(() => fixture.Write("2026-07-23 00:00:00.0000000", seven, 7));
            Assert.Throws<NotSupportedException>(() => fixture.Write(DBNull.Value, seven, 7));
            DateTime micro = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1234560);
            Assert.Equal(Convert.FromHexString("EA87BBEE8180C403"), fixture.Write(micro, legacy));
            Assert.Equal(Convert.FromHexString("EA87BBEE8180C403"), fixture.Write("2026-07-23 14:15:16.123456", legacy));
            Assert.Equal(123456000, DmTemporalWireCodec.DecodeRaw(fixture.Write(micro, fixedSix), DmTemporalWireLayout.Timestamp9, 6).NanosecondsWithinSecond);
            Assert.Equal(123456000, DmTemporalWireCodec.DecodeRaw(fixture.Write("2026-07-23 14:15:16.123456", fixedSix), DmTemporalWireLayout.Timestamp9, 6).NanosecondsWithinSecond);
        }
    }

    [Fact]
    public void MandatoryIdentityFactAndCurrentPhysicalSessionCannotInheritLogicalDefaults()
    {
        Assert.Equal(DmTemporalCapabilityState.CompatibilityLegacyUnverified, DmTemporalCapabilityProfile.Resolve(
            DmTemporalCapabilityProfile.CaptureIdentity("8.1.5.60", 21, "utf-8", false)).State);
        Assert.Equal(DmTemporalCapabilityState.CompatibilityLegacyUnverified, DmTemporalCapabilityProfile.Resolve(
            DmTemporalCapabilityProfile.CaptureIdentity("8.1.4.6", 11, "gb18030", false)).State);
        var shared = new WriterFixture("8.1.5.60", 21, "utf-8");
        Assert.NotSame(shared.Connection.ConnProperty, shared.Physical.ConnProperty);
        shared.Connection.ConnProperty.ServerVersion = "logical-only";
        shared.Connection.ConnProperty.msgVersion = 99;
        shared.Connection.ConnProperty.property[DmConst.PROP_KEY_ENCODING] = "gb18030";
        Assert.Equal(DmTemporalCapabilityState.VerifiedSeven, DmTemporalLegacyAdapter.Profile(shared.Connection).State);
        var unverified = new WriterFixture("unknown", 21, "utf-8");
        shared.Connection.ConnProperty.ServerVersion = "8.1.5.60";
        shared.Connection.ConnProperty.msgVersion = 21;
        shared.Connection.ConnProperty.ServerEncoding = "utf-8";
        shared.Connection.ConnProperty.property[DmConst.PROP_KEY_ENCODING] = "";
        shared.Connection.m_ConnInst = unverified.Physical;
        Assert.Equal(DmTemporalCapabilityState.CompatibilityLegacyUnverified, DmTemporalLegacyAdapter.Profile(shared.Connection).State);
        shared.Connection.m_ConnInst = shared.Physical;
        Assert.Equal(DmTemporalCapabilityState.VerifiedSeven, DmTemporalLegacyAdapter.Profile(shared.Connection).State);
    }

    [Fact]
    public void FixedTimeDescribeNeverTreatsTimeSpanDurationAsTimeOfDay()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        foreach (int type in new[] { 15, 22 })
        foreach (int scale in new[] { 0, 6 })
        foreach (TimeSpan span in new[] { TimeSpan.Zero, TimeSpan.FromHours(25), TimeSpan.FromHours(-1), TimeSpan.FromTicks(10) })
        {
            var field = fixture.Parameter(type, scale, fixedSource: true);
            var parameter = new DmParameter { Value = span };
            var resolved = parameter.ResolveType(field);
            Assert.Equal(DmParameterTypeSource.ServerDescribe, resolved.Source);
            Assert.Equal(DmDbType.Time, resolved.Type);
            // This is the public parameter's actual pre-writer normalization; TimeSpan remains a duration.
            object normalized = DmSysTypeConvertion.TypeConvertion(parameter);
            Assert.Equal(span, Assert.IsType<TimeSpan>(normalized));
            typeof(DmCommand).GetMethod("SelectBoundCType", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [field, resolved.Type, resolved.Source]);
            var output = new DmParamValue();
            Assert.Throws<NotSupportedException>(() => fixture.WriteInto(output, normalized, field));
            Assert.False(output.GetInDataBound()); Assert.Null(output.m_InValue);
            Assert.Equal(type, field.GetCType()); Assert.Equal((byte)1, field.GetTypeFlag());
        }

        Assert.Equal(Convert.FromHexString("EE8180C403"), fixture.Write(
            new TimeOnly(14, 15, 16).Add(TimeSpan.FromTicks(1234560)), fixture.Parameter(15, 6, fixedSource: true)));
        var interval = fixture.Parameter(21, 1574, fixedSource: true);
        TimeSpan expected = TimeSpan.FromHours(25).Add(TimeSpan.FromTicks(1234560));
        foreach (TimeSpan span in new[] { expected, -expected })
            Assert.Equal(span, new DmIntervalDT(fixture.Write(span, interval)).ToTimeSpanExact());
    }

    [Fact]
    public void NativeMaximumVectorsStayExactOrFailBeforeLegacyEncoding()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var extended = fixture.Parameter(26, 7, fixedSource: true);
        var legacy = fixture.Parameter(16, 6, fixedSource: true);
        foreach (int delta in new[] { 0, -1, -9 })
        {
            DateTime value = DateTime.MaxValue.AddTicks(delta);
            byte[] wire = fixture.Write(value, extended, 7);
            Assert.Equal(value.Ticks, DmTemporalInteger.ProjectDateTime(DmTemporalWireCodec.DecodeRaw(wire, DmTemporalWireLayout.Timestamp9, 7)).Ticks);
            if (delta == -9)
            {
                byte[] six = fixture.Write(value, legacy, 6);
                Assert.Equal(value.Ticks, new DmDateTime(DmDateTime.DmDtDecodeFast(six), 8).GetTimestamp().Ticks);
            }
            else Assert.Throws<OverflowException>(() => fixture.Write(value, legacy, 6));
        }
        foreach (int scale in new[] { 19, 23, 255 })
            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Write(DBNull.Value, extended, scale));
    }

    [Fact]
    public void UnknownProfileKeepsLegacyAndReliableFixedExtendedSixOnly()
    {
        var fixture = new WriterFixture(null, 21, "gb18030");
        DateTime exact = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1234560);
        var legacy = fixture.Parameter(16, 6, fixedSource: true);
        Assert.Equal(Convert.FromHexString("EA87BBEE8180C403"), fixture.Write(exact, legacy));
        Assert.Equal(Convert.FromHexString("EA87BBEE8180C403"), fixture.Write("2026-07-23 14:15:16.123456", legacy));
        Assert.Throws<OverflowException>(() => fixture.Write(exact.AddTicks(1), legacy));
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16.1234567", legacy));
        var fixedExtended = fixture.Parameter(26, 6, fixedSource: true);
        byte[] expected = DmTemporalWireCodec.EncodeExact(new(2026, 7, 23, 14, 15, 16, 123456000), DmTemporalWireLayout.Timestamp9, 6);
        Assert.Equal(expected, fixture.Write(exact, fixedExtended));
        Assert.Equal(expected, fixture.Write("2026-07-23 14:15:16.123456", fixedExtended));
        Assert.Throws<NotSupportedException>(() => fixture.Write(exact, fixedExtended, 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write(exact, fixture.Parameter(26, 6, fixedSource: false), 6));
    }

    [Fact]
    public void FixedColumnAndExplicitRequestRemainIndependentConstraints()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var column = fixture.Parameter(26, 6, fixedSource: true);
        DateTime micro = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(10);
        byte[] actual = fixture.Write(micro, column, 7);
        Assert.Equal(6, column.GetScale());
        Assert.Equal(1000, DmTemporalWireCodec.DecodeRaw(actual, DmTemporalWireLayout.Timestamp9, 6).NanosecondsWithinSecond);
        Assert.Equal(actual, fixture.Write("2026-07-23 14:15:16.0000010", column, 7));
        Assert.Throws<OverflowException>(() => fixture.Write(micro.AddTicks(1), column, 7));
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16.0000011", column, 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write(micro, fixture.Parameter(16, 6, fixedSource: true), 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write(DBNull.Value, fixture.Parameter(16, 6, fixedSource: true), 7));
        var advisory = fixture.Parameter(26, 9, fixedSource: false);
        Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000"), fixture.Write(micro.AddTicks(-9), advisory, 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write("2026-07-23 14:15:16.0000001", advisory, 7));
    }

    [Theory]
    [InlineData(8)] [InlineData(9)]
    public void WholeSecondsAndNullCannotBypassClosedWritePrecision(int scale)
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var field = fixture.Parameter(26, scale, fixedSource: true);
        Assert.Throws<NotSupportedException>(() => fixture.Write(new DateTime(2026, 7, 23), field, scale));
        Assert.Throws<NotSupportedException>(() => fixture.Write("2026-07-23 00:00:00.000000000", field, scale));
        Assert.Throws<NotSupportedException>(() => fixture.Write(DBNull.Value, field, scale));
    }

    [Fact]
    public void LocalTimezoneAndCalendarExclusionsApplyToClrAndSemanticText()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var local = fixture.Parameter(26, 7, fixedSource: true); local.mask = 4;
        Assert.Throws<NotSupportedException>(() => fixture.Write(DateTime.MinValue, local, 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write("2026-07-23 00:00:00", local, 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write(DBNull.Value, local, 7));
        var date = fixture.Parameter(14, 0, fixedSource: true);
        var timestamp = fixture.Parameter(26, 7, fixedSource: true);
        for (int day = 5; day <= 14; day++)
        {
            DateTime value = new(1582, 10, day);
            Assert.Throws<NotSupportedException>(() => fixture.Write(value, date));
            Assert.Throws<NotSupportedException>(() => fixture.Write(value, timestamp, 7));
            string text = "1582-10-" + day.ToString("D2") + " 00:00:00";
            Assert.Throws<NotSupportedException>(() => fixture.Write(text, timestamp, 7));
            // Pure helpers and raw reads retain Gregorian representation, independent of this writer policy.
            byte[] helper = DmDateTime.DateEncodeFast(new DmDateTime(value, 3, 0, 0, 0).GetByteArrayValue());
            Assert.Equal(day, new DmDateTime(helper, 3).GetDate().Day);
        }
        Assert.Equal(4, new DmDateTime(fixture.Write(new DateTime(1582, 10, 4), date), 3).GetDate().Day);
        Assert.Equal(15, new DmDateTime(fixture.Write(new DateTime(1582, 10, 15), date), 3).GetDate().Day);
        Assert.Throws<NotSupportedException>(() => fixture.Write("-4712-01-01 00:00:00", timestamp, 7));
        Assert.Throws<NotSupportedException>(() => fixture.Write("24:00:00", fixture.Parameter(15, 6, fixedSource: true), 6));
    }

    [Fact]
    public void TextWritersValidateShapeAndTemplatesWithoutLosingCalendarOrFraction()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var time = fixture.Parameter(15, 6, fixedSource: true);
        Assert.Equal(Convert.FromHexString("EE8180C403"), fixture.Write("14:15:16.123456", time));
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16.123456", time));
        Assert.Throws<InvalidCastException>(() => fixture.Write("14:15:16.123456 +00:00", time));
        var date = fixture.Parameter(14, 0, fixedSource: true);
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16", date));
        fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF7";
        var stamp = fixture.Parameter(26, 7, fixedSource: true);
        Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000"), fixture.Write("2026-07-23 14:15:16.0000001", stamp, 7));
        Assert.Throws<FormatException>(() => fixture.Write("2026-07-23 14:15", stamp, 7));
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16.0000001", fixture.Parameter(26, 6, fixedSource: true), 7));
        fixture.Physical.ConnProperty.oracleTimestampTZFormat = "YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM";
        byte[] offsetWire = fixture.Write("2026-07-23 14:15:16.0000001 -00:30", fixture.Parameter(27, 7, fixedSource: true), 7);
        Assert.Equal(-30, DmTemporalWireCodec.DecodeRaw(offsetWire, DmTemporalWireLayout.TimestampTz11, 7).OffsetMinutes);
    }

    [Fact]
    public void FractionLexerWidthsReachTheStrictWriterWithoutOpeningHigherWriteScales()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var stamp = fixture.Parameter(26, 7, fixedSource: true);
        string[] fractions = ["1", "12", "123", "1234", "12345", "123456", "0000001", "00000010", "000000100"];
        int[] expectedNs = [100000000, 120000000, 123000000, 123400000, 123450000, 123456000, 100, 100, 100];
        for (int width = 1; width <= 9; width++)
        {
            // Template width describes text, while the fixed target and requested write scale stay seven.
            fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF" + width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            byte[] wire = fixture.Write("2026-07-23 14:15:16." + fractions[width - 1], stamp, 7);
            Assert.Equal(expectedNs[width - 1], DmTemporalWireCodec.DecodeRaw(wire, DmTemporalWireLayout.Timestamp9, 7).NanosecondsWithinSecond);
        }
        fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF";
        Assert.Equal(123456000, DmTemporalWireCodec.DecodeRaw(fixture.Write("2026-07-23 14:15:16.123456", stamp, 7), DmTemporalWireLayout.Timestamp9, 7).NanosecondsWithinSecond);
        fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF7";
        Assert.Equal(10000, DmTemporalWireCodec.DecodeRaw(fixture.Write("2026-07-23 14:15:16.00001", stamp, 7), DmTemporalWireLayout.Timestamp9, 7).NanosecondsWithinSecond);
        fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF8";
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16.00000001", stamp, 7));
        fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF9";
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16.000000001", stamp, 7));
        foreach (string token in new[] { "FF0", "FF10", "FF７" })
        {
            fixture.Physical.ConnProperty.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS." + token;
            var output = new DmParamValue();
            Assert.Equal(6015, Assert.Throws<DmException>(() => fixture.WriteInto(output, "2026-07-23 14:15:16.1", stamp, 7)).Number);
            Assert.False(output.GetInDataBound()); Assert.Null(output.m_InValue);
        }
    }

    [Fact]
    public void StrictTemplatesRejectParsedSourceComponentsBeforeTargetNormalization()
    {
        var fixture = new WriterFixture("8.1.5.60", 21, "utf-8");
        var property = fixture.Physical.ConnProperty;
        var date = fixture.Parameter(14, 0, fixedSource: true);
        var time = fixture.Parameter(15, 6, fixedSource: true);
        var timeZone = fixture.Parameter(22, 6, fixedSource: true);
        var stamp = fixture.Parameter(26, 7, fixedSource: true);
        var stampZone = fixture.Parameter(27, 7, fixedSource: true);

        property.oracleDateFormat = "YYYY-MM-DD";
        Assert.Equal(Convert.FromHexString("EA87BB"), fixture.Write("2026-07-23", date));
        property.oracleDateFormat = "YYYY-MM-DD HH24:MI:SS";
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16", date));
        property.oracleDateFormat = "YYYY-MM-DD TZH:TZM";
        Assert.Throws<InvalidCastException>(() => fixture.Write("2026-07-23 +00:00", date));

        property.oracleTimeFormat = "HH24:MI:SS.FF6";
        Assert.Equal(Convert.FromHexString("EE8180C403"), fixture.Write("14:15:16.123456", time));
        property.oracleTimeFormat = "YYYY-MM-DD HH24:MI:SS";
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16", time));
        property.oracleTimeTZFormat = "YYYY-MM-DD HH24:MI:SS TZH:TZM";
        Assert.Throws<OverflowException>(() => fixture.Write("2026-07-23 14:15:16 -00:30", timeZone));
        property.oracleTimeFormat = "HH24:MI:SS TZH:TZM";
        Assert.Throws<InvalidCastException>(() => fixture.Write("14:15:16 +00:00", time));
        property.oracleTimeTZFormat = "HH24:MI:SS.FF6 TZH:TZM";
        var zoneFields = DmTemporalWireCodec.DecodeRaw(fixture.Write("14:15:16.123456 -00:30", timeZone), DmTemporalWireLayout.TimeTz7, 6);
        Assert.Equal(-30, zoneFields.OffsetMinutes); Assert.Equal(123456000, zoneFields.NanosecondsWithinSecond);

        property.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM";
        foreach (string offset in new[] { "+05:30", "+00:00", "-00:30" })
            Assert.Throws<InvalidCastException>(() => fixture.Write("2026-07-23 14:15:16.0000001 " + offset, stamp, 7));
        property.oracleTimestampFormat = "YYYY-MM-DD HH24:MI:SS.FF7";
        Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000"), fixture.Write("2026-07-23 14:15:16.0000001", stamp, 7));
        property.oracleTimestampTZFormat = "YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM";
        var fields = DmTemporalWireCodec.DecodeRaw(fixture.Write("2026-07-23 14:15:16.0000001 -00:30", stampZone, 7), DmTemporalWireLayout.TimestampTz11, 7);
        Assert.Equal(-30, fields.OffsetMinutes); Assert.Equal(100, fields.NanosecondsWithinSecond);
    }

    [Fact]
    public void OffsetRangeAndUtcOverflowAreCheckedAfterTheTargetGate()
    {
        var shared = new WriterFixture("8.1.5.60", 21, "utf-8");
        var tz = shared.Parameter(27, 7, fixedSource: true);
        foreach (int offset in new[] { -779, 0, 480, 840 })
        {
            DateTimeOffset value = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(offset)).AddTicks(1);
            byte[] wire = shared.Write(value, tz, 7);
            Assert.Equal(value, DmTemporalInteger.ProjectDateTimeOffset(DmTemporalWireCodec.DecodeRaw(wire, DmTemporalWireLayout.TimestampTz11, 7)));
        }
        Assert.Throws<OverflowException>(() => shared.Write("2026-07-23 14:15:16.0000001 -13:00", tz, 7));
        Assert.Throws<OverflowException>(() => shared.Write("0001-01-01 00:00:00 +00:01", tz, 7));
        var tls = new WriterFixture("8.1.4.6", 11, "gb18030");
        Assert.Throws<NotSupportedException>(() => tls.Write("0001-01-01 00:00:00 -13:00", tls.Parameter(27, 7, fixedSource: true), 7));
    }

    internal sealed class WriterFixture
    {
        internal DmConnection Connection { get; }
        internal DmConnInstance Physical { get; }
        internal LegacyStatement Statement { get; }
        private readonly DmSetValue writer;
        internal WriterFixture(string? version, int message, string? encoding)
        {
            Connection = new DmConnection();
            Physical = Uninitialized<DmConnInstance>();
            // Negotiation belongs to the physical object; logical defaults remain a separate source.
            var property = new DmConnProperty();
            property.ServerVersion = version!; property.msgVersion = message;
            if (encoding != null) property.ServerEncoding = encoding;
            property.oracleDateFormat = property.oracleTimeFormat = property.oracleTimeTZFormat =
                property.oracleTimestampFormat = property.oracleTimestampTZFormat = "";
            Set(Physical, "m_ConnPro", property);
            Physical.Conn = Connection; Connection.m_ConnInst = Physical;
            Statement = Uninitialized<LegacyStatement>();
            Set(Statement, "__t02_field_04000923", Physical);
            writer = new DmSetValue(property.ServerEncoding, Statement);
        }
        internal DmParameterInternal Parameter(int type, int scale, bool fixedSource)
        {
            var field = new DmParameterInternal(Physical);
            field.SetCType(type); field.SetScale(scale); field.SetTypeFlag(fixedSource ? (byte)1 : (byte)2);
            field.SetPrecision(type == 21 ? 24 : DmTemporalWireCodec.GetLength(DmTemporalWireCodec.ForCType(type)));
            return field;
        }
        internal byte[] Write(object value, DmParameterInternal field, int? requestedScale = null)
        {
            var output = new DmParamValue();
            WriteInto(output, value, field, requestedScale);
            return output.GetInValue();
        }
        internal void WriteInto(DmParamValue output, object value, DmParameterInternal field, int? requestedScale = null) =>
            writer.SetResolvedObject(output, value, Connection, null!, field.GetCType(), field, scaleOverride: requestedScale);
        internal static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        internal static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
