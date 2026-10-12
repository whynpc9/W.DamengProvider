using System.Buffers.Binary;
using System.Collections;
using System.Data;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Types;
using Xunit;
using Fixture = W.DmProvider.TypeTests.TemporalNativeWriterAdapterTests.WriterFixture;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;
using LegacyStatement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.TypeTests;

public sealed class TemporalNativeScalarRouteTests
{
    public static IEnumerable<object[]> OutputCases()
    {
        foreach (int type in new[] { 14, 15, 16, 22, 23, 26, 27 })
        foreach (int message in new[] { 11, 21 })
        foreach (bool asyncRead in new[] { false, true })
            yield return [type, message, asyncRead];
    }

    [Theory]
    [MemberData(nameof(OutputCases))]
    public async Task OutputParametersDeclareTheirOwnPackedScalarFormat(int type, int message, bool asyncRead)
    {
        var fixture = new Fixture("8.1.5.60", message, "UTF-8");
        var (field, bytes, expected, dbType) = Sample(fixture, type);
        // BDTA result metadata must not turn these independent scalar values into BDTA rows.
        field.isBdta = true;
        var info = new DmInfo(fixture.Physical);
        info.SetParaNum(1); info.SetParamsInfo([field]); info.rsBdta = true;
        fixture.Statement.__t02_method_06000898(info);
        var parameter = new DmParameter { ParameterName = "value", Direction = ParameterDirection.Output, DmSqlType = dbType };
        var command = new DmCommand(); command.Parameters.Add(parameter);
        fixture.Statement.__t02_method_0600089A(command);
        byte[] payload = OutputPayload(bytes, message);
        var buffer = new LegacyBuffer(payload);

        object? result = Invoke(typeof(c), asyncRead ? "aAsync" : "a",
            asyncRead ? [buffer, fixture.Statement, CancellationToken.None] : [buffer, fixture.Statement]);
        if (asyncRead) await Assert.IsAssignableFrom<Task>(result);

        Assert.Equal(expected, parameter.Value);
        Assert.Equal(payload.Length, buffer.a());
    }

    [Theory]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(22)]
    [InlineData(23)] [InlineData(26)] [InlineData(27)]
    public void ArrayAndRecordMembersUseTheirEmbeddedPackedScalarFormat(int type)
    {
        var fixture = new Fixture("8.1.5.60", 21, "UTF-8");
        var (field, bytes, expected, _) = Sample(fixture, type);
        field.isBdta = true;
        var queue = new Queue(); queue.Enqueue(fixture.Statement);
        Fixture.Set(fixture.Physical, "stmt_queue", queue);
        Fixture.Set(fixture.Physical, "m_Stmts", new List<LegacyStatement>());
        fixture.Physical.ConnProperty.property[DmConst.PROP_KEY_STMT_POOLING] = true;
        fixture.Physical.ConnProperty.property[DmConst.PROP_KEY_PSTMT_POOLING] = false;
        var member = new ComplexTypeDesc(fixture.Connection) { column = field };
        var arrayDesc = new ComplexTypeDesc(fixture.Connection) { m_arrObj = member };
        var recordDesc = new ComplexTypeDesc(fixture.Connection) { m_fieldsObj = [member], m_size = 1 };

        byte[] element = new byte[4 + bytes.Length]; element[1] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(element.AsSpan(2), checked((short)bytes.Length));
        bytes.CopyTo(element, 4);
        byte[] arrayPayload = new byte[8 + element.Length];
        BinaryPrimitives.WriteInt32LittleEndian(arrayPayload, arrayPayload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(arrayPayload.AsSpan(4), 1);
        element.CopyTo(arrayPayload, 8);
        byte[] recordPayload = new byte[4 + element.Length];
        BinaryPrimitives.WriteInt32LittleEndian(recordPayload, recordPayload.Length);
        element.CopyTo(recordPayload, 4);

        var array = ComplexTypeData.bytesToSArray(arrayPayload, null!, arrayDesc);
        var record = ComplexTypeData.bytesToRecord(recordPayload, null!, recordDesc);

        Assert.Equal(expected, Assert.Single(array.m_arrData).m_dumyData);
        Assert.Equal(bytes, Assert.Single(array.m_arrData).m_dataBuf);
        Assert.Equal(expected, Assert.Single(record.m_attribs).m_dumyData);
        Assert.Equal(bytes, Assert.Single(record.m_attribs).m_dataBuf);
        Assert.Single(queue.Cast<LegacyStatement>()); // No statement allocation or network close was needed.
    }

    [Fact]
    public void UnknownScalarFormatAndCompactBytesCannotBorrowPackedAcceptance()
    {
        var fixture = new Fixture("8.1.5.60", 21, "UTF-8");
        var (field, bytes, expected, _) = Sample(fixture, 26);
        field.isBdta = true;
        var implicitUnknown = new DmGetValue("UTF-8", fixture.Statement, false, [field]);
        var explicitUnknown = new DmGetValue("UTF-8", fixture.Statement, false, [field], DmTemporalResultFormat.Unknown);
        Assert.Throws<NotSupportedException>(() => implicitUnknown.GetObject(0, bytes, 26, 9, 7));
        Assert.Throws<NotSupportedException>(() => explicitUnknown.GetObject(0, bytes, 26, 9, 7));

        byte[] compact = new byte[13];
        BinaryPrimitives.WriteInt16LittleEndian(compact, 2026);
        compact[2] = 7; compact[3] = 23; compact[4] = 14; compact[5] = 15; compact[6] = 16;
        int nanoseconds = 123456700;
        compact[7] = (byte)nanoseconds; compact[8] = (byte)(nanoseconds >> 8);
        compact[9] = (byte)(nanoseconds >> 16); compact[12] = (byte)(nanoseconds >> 24);
        var packed = new DmGetValue("UTF-8", fixture.Statement, false, [field], DmTemporalResultFormat.OrdinaryPacked);
        var bdta = new DmGetValue("UTF-8", fixture.Statement, false, [field], DmTemporalResultFormat.Bdta);
        Assert.Equal(expected, packed.GetObject(0, bytes, 26, 9, 7));
        Assert.Throws<InvalidDataException>(() => packed.GetObject(0, compact, 26, 9, 7));
        Assert.Throws<NotSupportedException>(() => packed.GetObject(0, new byte[12], 26, 9, 7));
        Assert.Throws<InvalidDataException>(() => bdta.GetObject(0, bytes, 26, 9, 7));
        Assert.Equal(expected, bdta.GetObject(0, compact, 26, 9, 7));
    }

    private static (DmParameterInternal Field, byte[] Bytes, object Expected, DmDbType DbType) Sample(Fixture fixture, int type)
    {
        int scale = type == 14 ? 0 : type is 26 or 27 ? 7 : 6;
        int fraction = scale == 7 ? 123456700 : scale == 6 ? 123456000 : 0;
        bool time = type is 15 or 22, zone = type is 22 or 23 or 27;
        var field = fixture.Parameter(type, scale, fixedSource: true); field.name = "value";
        var fields = new DmTemporalFields(time ? 0 : 2026, time ? 0 : 7, time ? 0 : 23,
            type == 14 ? 0 : 14, type == 14 ? 0 : 15, type == 14 ? 0 : 16, fraction, zone ? 480 : null);
        var date = type == 14 ? new DateTime(2026, 7, 23) :
            new DateTime(time ? 1900 : 2026, time ? 1 : 7, time ? 1 : 23, 14, 15, 16).AddTicks(fraction / 100);
        object expected = date;
        if (zone) expected = new DateTimeOffset(date, TimeSpan.FromHours(8));
        DmDbType dbType = type switch
        {
            14 => DmDbType.Date, 15 => DmDbType.Time, 22 => DmDbType.TimeOffset,
            23 or 27 => DmDbType.DateTimeOffset, _ => DmDbType.DateTime
        };
        fixture.Physical.ConnProperty.property[DmConst.PROP_KEY_DBTIME_TO_TIMESPAN] = false;
        return (field, DmTemporalWireCodec.EncodeExact(fields, DmTemporalWireCodec.ForCType(type), scale), expected, dbType);
    }

    private static byte[] OutputPayload(byte[] bytes, int message)
    {
        int prefix = message >= 12 ? 4 : 0;
        byte[] payload = new byte[prefix + 2 + bytes.Length];
        if (prefix != 0) BinaryPrimitives.WriteInt32LittleEndian(payload, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(prefix), checked((ushort)bytes.Length));
        bytes.CopyTo(payload, prefix + 2);
        return payload;
    }

    private static object? Invoke(Type type, string name, object?[] arguments)
    {
        try
        {
            var method = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(candidate => candidate.Name == name && candidate.GetParameters().Length == arguments.Length);
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException wrapper) when (wrapper.InnerException != null)
        { ExceptionDispatchInfo.Capture(wrapper.InnerException).Throw(); throw; }
    }
}
