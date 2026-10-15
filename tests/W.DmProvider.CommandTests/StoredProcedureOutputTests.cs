using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class StoredProcedureOutputTests
{
    [Fact]
    public void ObservedScalarFrameCopiesPayloadAndLeavesFollowingResultBytes()
    {
        byte[] packet = Convert.FromHexString("0100000004000C00000001000000040007000000AA55");
        var buffer = new b(packet);
        byte[][] outputs = DmCommand.ReadProcedureOutputPayloads(buffer, 21, 2, 2);
        Assert.Equal(new byte[] { 12, 0, 0, 0 }, outputs[0]);
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, outputs[1]);
        Assert.Equal(20, buffer.a());
        packet[6] = 99;
        Assert.Equal(12, outputs[0][0]);
        Assert.Equal(new byte[] { 0xAA, 0x55 }, buffer.F(2));
    }

    [Fact]
    public void NullMarkerAndEmptyScalarRemainDistinct()
    {
        var buffer = new b(Convert.FromHexString("01000000FEFF010000000000"));
        byte[][] values = DmCommand.ReadProcedureOutputPayloads(buffer, 21, 2, 2);
        Assert.Null(values[0]);
        Assert.Empty(values[1]);
    }

    [Theory]
    [InlineData("", 1, 2)]
    [InlineData("", -1, 0)]
    [InlineData("", 2, 1)]
    public void CountMismatchRejectsBeforeConsumingPayload(string hex, int count, int expected)
    {
        var buffer = new b(Convert.FromHexString(hex));
        Assert.Throws<InvalidDataException>(() => DmCommand.ReadProcedureOutputPayloads(buffer, 21, count, expected));
        Assert.Equal(0, buffer.a());
    }

    [Theory]
    [InlineData("00")]
    [InlineData("01000000")]
    [InlineData("010000000400010203")]
    [InlineData("0100000004000102030401000000FE")]
    public void TruncatedFrameNeverReturnsAPartialOutputArray(string hex)
    {
        int count = hex.Length > 24 ? 2 : 1;
        Assert.Throws<InvalidDataException>(() => DmCommand.ReadProcedureOutputPayloads(new b(Convert.FromHexString(hex)), 21, count, count));
    }

    [Theory]
    [InlineData("00000000")]
    [InlineData("02000000")]
    [InlineData("01000080")]
    [InlineData("01000000FDFF")]
    [InlineData("01000000FFFF")]
    public void UnknownArrayFlagsAndLengthMarkersAreRejected(string hex)
    {
        Assert.Throws<NotSupportedException>(() => DmCommand.ReadProcedureOutputPayloads(new b(Convert.FromHexString(hex)), 21, 1, 1));
    }

    [Fact]
    public void UnverifiedLegacyFrameIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => DmCommand.ReadProcedureOutputPayloads(new b(new byte[0]), 11, 1, 1));
    }

    [Fact]
    public void ExplicitUnsignedOutputKeepsItsClrTypeAndChecksOverflow()
    {
        var descriptor = Descriptor(7, 10, 0);
        var parameter = new DmParameter("p", DmDbType.UInt16, ParameterDirection.Output);
        DmGetValue getter = Getter(descriptor);
        object value = DmCommand.ConvertProcedureOutput(getter, parameter, descriptor, 0, BitConverter.GetBytes(65535));
        Assert.Equal((ushort)65535, Assert.IsType<ushort>(value));
        Assert.Throws<OverflowException>(() => DmCommand.ConvertProcedureOutput(getter, parameter, descriptor, 0, BitConverter.GetBytes(65536)));
        Assert.Throws<InvalidDataException>(() => DmCommand.ConvertProcedureOutput(getter, parameter, descriptor, 0, new byte[5]));
    }

    [Fact]
    public void ExactDecimalOutputRetainsLargePrecisionAndDecimalRejectsLoss()
    {
        var descriptor = Descriptor(9, 38, 9);
        DmDecimal exact = DmDecimal.Parse("12345678901234567890123456789.123456789");
        byte[] bytes = DmNumericCodec.EncodeDecimal(exact, 38, 9);
        var parameter = new DmParameter("p", DmDbType.XDEC, ParameterDirection.Output);
        Assert.Equal(exact, Assert.IsType<DmDecimal>(DmCommand.ConvertProcedureOutput(Getter(descriptor), parameter, descriptor, 0, bytes)));
        parameter.DmSqlType = DmDbType.Decimal;
        Assert.Throws<OverflowException>(() => DmCommand.ConvertProcedureOutput(Getter(descriptor), parameter, descriptor, 0, bytes));
    }

    [Theory]
    [InlineData(26, "EA077D4CC47999B70E")]
    [InlineData(27, "EA077D4CC47999B70E4A01")]
    public void ObservedPackedTimestampOutputsPreserveSevenDigitsAndOffset(int type, string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        var descriptor = Descriptor(type, bytes.Length, 9);
        var parameter = new DmParameter("p", type == 26 ? DmDbType.DateTime : DmDbType.DateTimeOffset, ParameterDirection.Output);
        object value = DmCommand.ConvertProcedureOutput(Getter(descriptor), parameter, descriptor, 0, bytes);
        if (type == 26) Assert.Equal(1234567, Assert.IsType<DateTime>(value).Ticks % TimeSpan.TicksPerSecond);
        else
        {
            DateTimeOffset timestamp = Assert.IsType<DateTimeOffset>(value);
            Assert.Equal(1234567, timestamp.Ticks % TimeSpan.TicksPerSecond);
            Assert.Equal(TimeSpan.FromMinutes(330), timestamp.Offset);
        }
        var layout = DmTemporalWireCodec.ForCType(type);
        DmTemporalFields fields = DmTemporalWireCodec.DecodeRaw(bytes, layout, 9);
        byte[] nonTickAligned = DmTemporalWireCodec.EncodeExact(fields with { NanosecondsWithinSecond = 123456789 }, layout, 9);
        Assert.Throws<OverflowException>(() => DmCommand.ConvertProcedureOutput(Getter(descriptor), parameter, descriptor, 0, nonTickAligned));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(22)]
    public void TimeOutputsKeepTheirDeclaredClrType(int type)
    {
        var fields = new DmTemporalFields(0, 0, 0, 12, 34, 56, 123456000, type == 22 ? 330 : null);
        byte[] bytes = DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireCodec.ForCType(type), 6);
        var descriptor = Descriptor(type, bytes.Length, 6);
        var parameter = new DmParameter("p", DmDbType.Time, ParameterDirection.Output);
        object value = DmCommand.ConvertProcedureOutput(Getter(descriptor), parameter, descriptor, 0, bytes);
        var expected = new TimeSpan(0, 12, 34, 56).Add(TimeSpan.FromTicks(1234560));
        if (type == 15) Assert.Equal(expected, Assert.IsType<DateTime>(value).TimeOfDay);
        else
        {
            var actual = Assert.IsType<DateTimeOffset>(value);
            Assert.Equal(expected, actual.TimeOfDay);
            Assert.Equal(TimeSpan.FromMinutes(330), actual.Offset);
        }
    }

    [Fact]
    public void FailedLaterConversionCannotPublishEarlierOutput()
    {
        var parameters = new DmParameterCollection();
        var first = new DmParameter("first", DmDbType.Int32, ParameterDirection.Output) { Value = 101 };
        var second = new DmParameter("second", DmDbType.Byte, ParameterDirection.Output) { Value = 102 };
        parameters.Add(first);
        parameters.Add(second);
        using DmCommandPlan plan = DmCommandPlan.Capture("P", CommandType.StoredProcedure, 30, null!, null!, parameters);
        var descriptor = Descriptor(7, 10, 0);
        var firstSnapshot = (DmParameter)plan.Parameters[0];
        var secondSnapshot = (DmParameter)plan.Parameters[1];
        plan.StageOutput(firstSnapshot, DmCommand.ConvertProcedureOutput(Getter(descriptor), firstSnapshot, descriptor, 0, BitConverter.GetBytes(12)));
        Assert.Throws<OverflowException>(() => DmCommand.ConvertProcedureOutput(Getter(descriptor), secondSnapshot, descriptor, 0, BitConverter.GetBytes(999)));
        plan.FailOutputs();
        Assert.Throws<InvalidOperationException>(plan.PublishOutputs);
        Assert.Equal(101, first.Value);
        Assert.Equal(102, second.Value);
    }

    [Fact]
    public void ExplicitSizeRejectsOversizeTextAndBytesWithoutTruncating()
    {
        var parameter = new DmParameter("p", DmDbType.VarChar, ParameterDirection.Output) { Size = 3 };
        Assert.Equal("abc", DmCommand.ValidateProcedureOutputValue(parameter, "abc"));
        Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue(parameter, "abcd"));
        byte[] bytes = [1, 2, 3];
        Assert.Same(bytes, DmCommand.ValidateProcedureOutputValue(parameter, bytes));
        Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue(parameter, new byte[4]));
        Assert.Same(DBNull.Value, DmCommand.ValidateProcedureOutputValue(parameter, DBNull.Value));
        parameter.Size = 0;
        Assert.Equal("abcd", DmCommand.ValidateProcedureOutputValue(parameter, "abcd"));
    }

    [Fact]
    public void DecimalDeclarationRequiresExactPrecisionAndScale()
    {
        var parameter = new DmParameter("p", DmDbType.Decimal, ParameterDirection.Output) { Precision = 5, Scale = 2 };
        Assert.Equal(123.45m, DmCommand.ValidateProcedureOutputValue(parameter, 123.45m));
        Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue(parameter, 1234.56m));
        Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue(parameter, 1.234m));
        DmDecimal rescaled = Assert.IsType<DmDecimal>(DmCommand.ValidateProcedureOutputValue(parameter, DmDecimal.Parse("1.2")));
        Assert.Equal(2, rescaled.Scale);
        Assert.Equal("1.20", rescaled.ToString());
        parameter.Scale = 6;
        Assert.Throws<ArgumentOutOfRangeException>(() => DmCommand.ValidateProcedureOutputValue(parameter, 1m));
    }

    [Theory]
    [InlineData(0, 10000000, true)]
    [InlineData(0, 1, false)]
    [InlineData(3, 1230000, true)]
    [InlineData(3, 1234000, false)]
    [InlineData(6, 1234560, true)]
    [InlineData(6, 1234567, false)]
    [InlineData(7, 1234567, true)]
    [InlineData(8, 1234567, true)]
    [InlineData(9, 1234567, true)]
    public void TemporalDeclarationRejectsFractionLossForEveryClrProjection(byte scale, long fraction, bool accepted)
    {
        var parameter = new DmParameter("p", DmDbType.DateTime, ParameterDirection.Output) { Scale = scale };
        DateTime dateTime = new DateTime(2026, 10, 15, 12, 0, 0).AddTicks(fraction);
        object[] values = [dateTime, new DateTimeOffset(dateTime, TimeSpan.FromMinutes(330)), TimeSpan.FromTicks(-fraction)];
        foreach (object value in values)
        {
            if (accepted) Assert.Same(value, DmCommand.ValidateProcedureOutputValue(parameter, value));
            else Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue(parameter, value));
        }
    }

    [Fact]
    public void FailedOutputDeclarationCannotPublishPreviouslyStagedValues()
    {
        var parameters = new DmParameterCollection();
        var first = new DmParameter("first", DmDbType.Int32, ParameterDirection.Output) { Value = 101 };
        var second = new DmParameter("second", DmDbType.VarChar, ParameterDirection.Output) { Value = "old", Size = 3 };
        parameters.Add(first);
        parameters.Add(second);
        using DmCommandPlan plan = DmCommandPlan.Capture("P", CommandType.StoredProcedure, 30, null!, null!, parameters);
        plan.StageOutput((DmParameter)plan.Parameters[0], 12);
        Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue((DmParameter)plan.Parameters[1], "long"));
        plan.FailOutputs();
        Assert.Throws<InvalidOperationException>(plan.PublishOutputs);
        Assert.Equal(101, first.Value);
        Assert.Equal("old", second.Value);
    }

    [Theory]
    [InlineData(0, 10000000, true)]
    [InlineData(0, 1234560, false)]
    [InlineData(3, 1230000, true)]
    [InlineData(3, 1234560, false)]
    [InlineData(6, 1234560, true)]
    [InlineData(3, -1230000, true)]
    [InlineData(3, -1234560, false)]
    public void IntervalOutputScaleUsesExactConversionWithoutChangingSubtypeOrLeadingPrecision(byte scale, long ticks, bool accepted)
    {
        const int described = (6 << 8) | (9 << 4) | 6;
        var value = new DmIntervalDT(TimeSpan.FromTicks(ticks), described);
        var parameter = new DmParameter("p", DmDbType.IntervalDayToSecond, ParameterDirection.Output) { Scale = scale };
        if (accepted)
        {
            var result = Assert.IsType<DmIntervalDT>(DmCommand.ValidateProcedureOutputValue(parameter, value));
            Assert.Equal((described & ~0xF) | scale, result.getScaleForSvr());
            Assert.Equal(TimeSpan.FromTicks(ticks), result.ToTimeSpanExact());
        }
        else Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue(parameter, value));
        Assert.Equal(described, value.getScaleForSvr());
        Assert.Equal(TimeSpan.FromTicks(ticks), value.ToTimeSpanExact());
    }

    [Fact]
    public void LossyIntervalOutputScalePreventsAllOutputPublication()
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("first", DmDbType.Int32, ParameterDirection.Output) { Value = 101 });
        parameters.Add(new DmParameter("interval", DmDbType.IntervalDayToSecond, ParameterDirection.Output) { Value = "old interval", Scale = 0 });
        using DmCommandPlan plan = DmCommandPlan.Capture("P", CommandType.StoredProcedure, 30, null!, null!, parameters);
        plan.StageOutput((DmParameter)plan.Parameters[0], 12);
        var value = new DmIntervalDT(TimeSpan.FromTicks(1234560), (6 << 8) | (9 << 4) | 6);
        Assert.Throws<OverflowException>(() => DmCommand.ValidateProcedureOutputValue((DmParameter)plan.Parameters[1], value));
        plan.FailOutputs();
        Assert.Throws<InvalidOperationException>(plan.PublishOutputs);
        Assert.Equal(101, parameters[0].Value);
        Assert.Equal("old interval", parameters[1].Value);
    }

    private static DmParameterInternal Descriptor(int type, int precision, int scale)
    {
        // Scalar conversion reads descriptor fields only; no connection or wire
        // parameter-value storage is involved in these parser/conversion tests.
        var result = (DmParameterInternal)RuntimeHelpers.GetUninitializedObject(typeof(DmParameterInternal));
        result.SetCType(type);
        result.SetPrecision(precision);
        result.SetScale(scale);
        return result;
    }

    private static DmGetValue Getter(DmParameterInternal descriptor) =>
        new("utf-8", null!, false, new DmField[] { descriptor }, DmTemporalResultFormat.OrdinaryPacked);
}
