using System.Buffers.Binary;
using System.Data;
using System.IO;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Protocol;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Types;
using Xunit;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeFrozenCodecTests
{
    [Fact]
    public void NumericTextAndNullKeepTheCapturedProfileTypeAndHeaderAfterAllMutableInputsChange()
    {
        var fixture = Shared();
        var field = Fixed(fixture, 26, 7);
        var parameter = new DmParameter { DbType = DbType.DateTime2, Scale = 7 };
        var binding = Binding(fixture, parameter, field, 26);
        field.SetCType(16); field.SetScale(0); field.SetPrecision(8); field.SetTypeFlag(2); field.mask = 4;
        parameter.DbType = DbType.Date; parameter.Scale = 0; parameter.Precision = 9;
        fixture.Physical.ConnProperty.ServerVersion = "8.1.4.6";
        fixture.Physical.ConnProperty.msgVersion = 11;
        fixture.Physical.ConnProperty.ServerEncoding = "gb18030";
        fixture.Connection.ConnProperty.ServerVersion = "unknown";

        var expected = Convert.FromHexString("EA87BBEE81C8000000");
        foreach (object value in new object[] { new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1), "2026-07-23 14:15:16.0000001", DBNull.Value })
        {
            var output = Write(fixture, field, binding, value);
            Assert.Equal(26, output.GetSqlType()); Assert.Equal(9, output.GetPrec()); Assert.Equal(7, output.GetScale());
            Assert.Equal(value is DBNull, output.GetIsInDataNull());
            if (value is not DBNull) Assert.Equal(expected, output.GetInValue());
        }
        Assert.Equal(DmTemporalCapabilityState.VerifiedSeven, binding.Profile.State);
        Assert.Equal((byte)7, binding.Request.Scale); Assert.Equal(DbType.DateTime2, binding.Request.DeclaredDbType);
    }

    [Fact]
    public void AClosedFrozenProfileCannotBeReopenedByChangingTheConnectionForAnyRepresentation()
    {
        var fixture = Shared(); var field = Fixed(fixture, 26, 7);
        var binding = Binding(fixture, new DmParameter { DbType = DbType.DateTime2, Scale = 7 }, field, 26);
        var unknown = DmTemporalCapabilityProfile.Resolve(new("unknown", 0, null!, false));
        binding = binding with { Profile = unknown };
        foreach (object value in new object[] { new DateTime(2026, 7, 23).AddTicks(1), "2026-07-23 00:00:00.0000000", DBNull.Value })
        {
            var output = new DmParamValue();
            Assert.Throws<NotSupportedException>(() => Writer(fixture).SetResolvedTemporalObject(output, value, fixture.Connection, "", field, binding));
            Assert.False(output.GetInDataBound());
        }
    }

    [Fact]
    public void RequestedEncoderAndServerScalesRemainSeparateForNumericAndSemanticText()
    {
        var fixture = Shared(); var field = Fixed(fixture, 26, 7);
        var binding = Binding(fixture, new DmParameter { DbType = DbType.DateTime2, Scale = 3 }, field, 26);
        Assert.Equal(3, binding.EncodedScale); Assert.Equal(7, binding.WireScale);
        var expected = Convert.FromHexString("EA87BBEE8180A9A90E");
        foreach (object value in new object[] { new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1230000), "2026-07-23 14:15:16.123000000" })
        {
            var output = Write(fixture, field, binding, value);
            Assert.Equal(expected, output.GetInValue()); Assert.Equal(7, output.GetScale());
        }
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, "2026-07-23 14:15:16.123000001"));
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, new DateTime(2026, 7, 23).AddTicks(1234560)));

        field = Fixed(fixture, 26, 6);
        binding = Binding(fixture, new DmParameter { DbType = DbType.DateTime2, Scale = 7 }, field, 26);
        Assert.Equal(7, binding.EncodedScale); Assert.Equal(6, binding.WireScale);
        Assert.Equal(6, Write(fixture, field, binding, "2026-07-23 00:00:00.1234560").GetScale());
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, "2026-07-23 00:00:00.1234567"));
    }

    [Fact]
    public void OracleParsingUsesFrozenTypeAndTheSameProfileAndPrecisionGate()
    {
        var fixture = Shared(); var field = Fixed(fixture, 27, 7);
        var binding = Binding(fixture, new DmParameter { DbType = DbType.DateTimeOffset, Scale = 7 }, field, 27);
        fixture.Physical.ConnProperty.oracleTimestampTZFormat = "YYYY/MM/DD HH24:MI:SS.FF9 TZH:TZM";
        field.SetCType(14); field.SetScale(0); field.mask = 4;
        fixture.Physical.ConnProperty.ServerVersion = "unknown";
        var output = Write(fixture, field, binding, "2026/07/23 14:15:16.000000100 -00:30");
        Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000E2FF"), output.GetInValue());
        Assert.Equal(27, output.GetSqlType()); Assert.Equal(7, output.GetScale());
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, "2026/07/23 14:15:16.000000101 -00:30"));
    }

    [Fact]
    public void IntervalUsesTheFullFrozenPackedDeclarationAndIndependentUserQuantum()
    {
        var fixture = Shared(); int packed = (6 << 8) | (2 << 4) | 6;
        var field = Fixed(fixture, 21, packed);
        var binding = Binding(fixture, new DmParameter { DmSqlType = DmDbType.IntervalDayToSecond, Scale = 3 }, field, 21);
        field.SetScale(0); field.SetCType(20); field.SetPrecision(12); field.SetTypeFlag(2);
        foreach (object value in new object[] { TimeSpan.FromDays(12) + TimeSpan.FromTicks(1230000), "12 00:00:00.123000" })
        {
            var output = Write(fixture, field, binding, value); byte[] bytes = output.GetInValue();
            Assert.Equal(24, bytes.Length); Assert.Equal(12, BinaryPrimitives.ReadInt32LittleEndian(bytes));
            Assert.Equal(123000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16)));
            Assert.Equal(packed, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(20)));
            Assert.Equal(packed, output.GetScale()); Assert.Equal(21, output.GetSqlType());
        }
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, "12 00:00:00.123456"));
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, TimeSpan.FromTicks(1234560)));
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, TimeSpan.FromDays(100)));
        Assert.Equal(packed, Write(fixture, field, binding, DBNull.Value).GetScale());
    }

    [Fact]
    public void IntervalColumnQuantumAndSubtypeStillConstrainTheValueAndNullChecksCapability()
    {
        var fixture = Shared(); var field = Fixed(fixture, 21, (6 << 8) | (2 << 4) | 3);
        var binding = Binding(fixture, new DmParameter { DmSqlType = DmDbType.IntervalDayToSecond, Scale = 6 }, field, 21);
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, TimeSpan.FromTicks(1234560)));
        Assert.Throws<OverflowException>(() => Write(fixture, field, binding, "0 00:00:00.123456"));
        var output = Write(fixture, field, binding, TimeSpan.FromTicks(1230000));
        Assert.Equal(3, binding.EncodedScale); Assert.Equal(binding.WireScale, BinaryPrimitives.ReadInt32LittleEndian(output.GetInValue().AsSpan(20)));
        var unsupported = binding with { RequestedScale = 7 };
        Assert.Throws<NotSupportedException>(() => Write(fixture, field, unsupported, DBNull.Value));
        field = Fixed(fixture, 21, (9 << 8) | (2 << 4) | 6);
        binding = Binding(fixture, new DmParameter { DmSqlType = DmDbType.IntervalDayToSecond }, field, 21);
        Assert.Throws<NotSupportedException>(() => Write(fixture, field, binding, TimeSpan.Zero));
        Assert.True(Write(fixture, field, binding, DBNull.Value).GetIsInDataNull());
    }

    [Fact]
    public void ReadDescriptorRetainsTypeScaleMaskAndActualFormatAfterTheFieldChanges()
    {
        var fixture = Shared(); var field = Fixed(fixture, 26, 9);
        var descriptor = field.GetTemporalReadDescriptor(DmTemporalResultFormat.OrdinaryPacked);
        Assert.Equal(DmTemporalResultFormat.Unknown, descriptor.RawMetadata!.Value.ResultFormat);
        field.SetCType(16); field.SetScale(0); field.mask = 4; field.isBdta = true;
        var fields = DmDateTime.DecodeForReader(Convert.FromHexString("EA87BBEE8102000000"), descriptor);
        Assert.Equal(1, fields.NanosecondsWithinSecond); Assert.Equal(2026, fields.Year);
        Assert.Equal("2026-07-23 14:15:16.000000001", DmTemporalInteger.FormatExact(fields, descriptor.FractionalScale, DmTemporalTextKind.Timestamp));
        Assert.Throws<OverflowException>(() => DmTemporalInteger.ProjectDateTime(fields));
        Assert.Throws<NotSupportedException>(() => DmTemporalLegacyAdapter.ReadNetwork(new byte[12], descriptor));
        Assert.Throws<InvalidDataException>(() => DmTemporalLegacyAdapter.ReadNetwork(new byte[8], descriptor));
        Assert.Throws<NotSupportedException>(() => DmTemporalLegacyAdapter.ReadNetwork(new byte[9], descriptor with { ActualFormat = DmTemporalResultFormat.Unknown }));
        var observedRaw = descriptor with
        {
            RawMetadata = descriptor.RawMetadata.Value with { ResultFormat = DmTemporalResultFormat.OrdinaryPacked },
            ActualFormat = DmTemporalResultFormat.Unknown
        };
        Assert.Throws<NotSupportedException>(() => DmTemporalLegacyAdapter.ReadNetwork(new byte[9], observedRaw));
        Assert.Throws<NotSupportedException>(() => DmTemporalLegacyAdapter.ReadNetwork(new byte[9], descriptor with { Mask = 4 }));
    }

    [Fact]
    public void ExplicitBdtaFixtureReadsNanosecondsWithoutUsingRawFormatOrMutableField()
    {
        var fixture = Shared(); var field = Fixed(fixture, 26, 9);
        var descriptor = field.GetTemporalReadDescriptor(DmTemporalResultFormat.Bdta);
        byte[] bytes = new byte[13]; BinaryPrimitives.WriteInt16LittleEndian(bytes, 2026);
        bytes[2] = 7; bytes[3] = 23; bytes[4] = 14; bytes[5] = 15; bytes[6] = 16;
        bytes[7] = 1;
        var fields = DmTemporalLegacyAdapter.ReadNetwork(bytes, descriptor);
        Assert.Equal(1, fields.NanosecondsWithinSecond);
        Assert.Throws<OverflowException>(() => DmTemporalInteger.ProjectDateTime(fields));
        Assert.Throws<InvalidDataException>(() => DmTemporalLegacyAdapter.ReadNetwork(bytes, descriptor with { FractionalScale = 8 }));
        Assert.Throws<NotSupportedException>(() => DmTemporalLegacyAdapter.ReadNetwork(bytes, descriptor with { ActualFormat = DmTemporalResultFormat.Unknown }));
    }

    private static TemporalNativeWriterAdapterTests.WriterFixture Shared() => new("8.1.5.60", 21, "utf-8");
    private static DmSetValue Writer(TemporalNativeWriterAdapterTests.WriterFixture fixture) => new("utf-8", fixture.Statement);
    private static DmParameterInternal Fixed(TemporalNativeWriterAdapterTests.WriterFixture fixture, int type, int scale)
    {
        int precision = type == 21 ? 24 : DmTemporalWireCodec.GetLength(DmTemporalWireCodec.ForCType(type));
        byte[] frame = TemporalNativeBindingContractTests.BindingFixture.PrepareFrame([new(type, precision, scale, 1)]);
        using var stream = new MemoryStream(frame);
        var buffer = new LegacyBuffer();
        DmFrameReader.Read(stream, buffer, false, DmDeadline.Infinite);
        c.A(buffer, fixture.Statement);
        var field = Assert.Single(fixture.Statement.H().GetParamsInfo());
        Assert.Equal(new DmTemporalRawMetadata(DmTemporalMetadataSource.ParameterDescribe,
            type, precision, scale, 1, 0, 0, DmTemporalResultFormat.Unknown), field.TemporalServerMetadata);
        Assert.True(field.TemporalServerMetadata!.Value.HasServerSource);
        Assert.Equal(type, field.GetCType()); Assert.Equal(precision, field.GetPrecision()); Assert.Equal(scale, field.GetScale());
        return field;
    }
    private static DmTemporalParameterBinding Binding(TemporalNativeWriterAdapterTests.WriterFixture fixture, DmParameter request, DmParameterInternal field, int type) =>
        DmTemporalBindingResolver.Create(DmParameterMetadata.From(request), request.TypeSource, type, field, DmTemporalLegacyAdapter.Profile(fixture.Connection));
    private static DmParamValue Write(TemporalNativeWriterAdapterTests.WriterFixture fixture, DmParameterInternal field, DmTemporalParameterBinding binding, object value)
    {
        var output = new DmParamValue(); Writer(fixture).SetResolvedTemporalObject(output, value, fixture.Connection, "", field, binding); return output;
    }
}
