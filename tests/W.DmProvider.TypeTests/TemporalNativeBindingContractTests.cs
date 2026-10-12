using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.ExceptionServices;
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

public sealed class TemporalNativeBindingContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealDecoderKeepsGenericAdvisorySourceThroughRebindingAndExecutePacket(bool asyncRead)
    {
        var fixture = new BindingFixture();
        await fixture.Describe([new(2, 64, 0, 2)], asyncRead);
        var field = fixture.Fields[0];
        Assert.Equal(new DmTemporalRawMetadata(DmTemporalMetadataSource.ParameterDescribe,
            2, 64, 0, 2, 0, 0, DmTemporalResultFormat.Unknown), field.TemporalServerMetadata);
        DateTime value = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1);
        using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", value));
        var binding = Assert.Single(plan.TemporalBindings);
        Assert.NotNull(binding); Assert.Same(binding, field.TemporalBinding);
        Assert.False(binding.ReliableFixed); Assert.Null(binding.ColumnScale);
        Assert.Equal(26, binding.CType); Assert.Equal(7, binding.EncodedScale);
        Assert.Equal(2, binding.RawMetadata!.Value.CType); Assert.Equal(0, binding.RawMetadata.Value.Scale);
        byte[] packet = fixture.Packet();
        Assert.Equal(26, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(65)));
        Assert.Equal(9, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(69)));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(73)));
        Assert.Equal(9, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(77)));
        // Historical official golden; expected bytes are independent of the new encoder.
        Assert.Equal(Convert.FromHexString("EA87BBEE81C8000000"), packet[79..]);
    }

    [Theory]
    [InlineData(DbType.DateTime, 16, 6)]
    [InlineData(DbType.DateTime2, 26, 7)]
    [InlineData(DbType.DateTimeOffset, 23, 6)]
    public async Task ExplicitDefaultsDoNotFollowTheValueFraction(DbType dbType, int cType, int scale)
    {
        var fixture = new BindingFixture();
        await fixture.Describe([new(2, 32767, 0, 2)]);
        foreach (long fraction in new long[] { 0, 10, 1234560 })
        {
            object value;
            if (dbType == DbType.DateTimeOffset)
            {
                value = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(-30)).AddTicks(fraction);
                Assert.IsType<DateTimeOffset>(value);
            }
            else
            {
                value = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(fraction);
                Assert.IsType<DateTime>(value);
            }
            using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", value) { DbType = dbType });
            Assert.Equal(cType, plan.TemporalBindings[0].CType);
            Assert.Equal(scale, plan.TemporalBindings[0].EncodedScale);
        }
    }

    [Theory]
    [InlineData(DmDbType.DateTime, 16)] [InlineData(DmDbType.DateTimeOffset, 23)]
    public async Task ProviderOnlyDeclarationKeepsSixDigitDefault(DmDbType provider, int cType)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value) { DmSqlType = provider });
        Assert.Equal(cType, plan.TemporalBindings[0].CType); Assert.Equal(6, plan.TemporalBindings[0].EncodedScale);
        Assert.False(plan.TemporalBindings[0].Request.ExplicitDbType);
    }

    [Theory]
    [InlineData(0, "EA87BBEE81C80000000000")]
    [InlineData(480, "EA87BBEE81C8000000E001")]
    [InlineData(-300, "EA87BBEE81C8000000D4FE")]
    [InlineData(330, "EA87BBEE81C80000004A01")]
    public async Task InferredOffsetAndFixedSemanticTextMatchTheSameOfficialGolden(int minutes, string golden)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        DateTimeOffset value = new DateTimeOffset(2026, 7, 23, 14, 15, 16, TimeSpan.FromMinutes(minutes)).AddTicks(1);
        using (var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", value)))
        {
            Assert.Equal(27, plan.TemporalBindings[0].CType); Assert.Equal(7, plan.TemporalBindings[0].EncodedScale);
            Assert.Equal(Convert.FromHexString(golden), fixture.Packet()[79..]);
        }
        string sign = minutes < 0 ? "-" : "+";
        string offset = sign + (Math.Abs(minutes) / 60).ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
            + ":" + (Math.Abs(minutes) % 60).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        // Extended semantic text requires a real fixed target; advisory DTO input above
        // establishes the separate inferred CLR route without opening this text capability.
        await fixture.Describe([new(27, 11, 7, 1)]);
        using var text = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", "2026-07-23 14:15:16.0000001 " + offset)
            { DbType = DbType.DateTimeOffset, Scale = 7 });
        Assert.True(text.TemporalBindings[0].ReliableFixed);
        Assert.Equal(Convert.FromHexString(golden), fixture.Packet()[79..]);
    }

    [Fact]
    public async Task AdvisoryExtendedSemanticTextRejectsWithoutExecuteOrPublishedBinding()
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL",
            new DmParameter("p", "2026-07-23 14:15:16.0000001 +00:00")
                { DbType = DbType.DateTimeOffset, Scale = 7 }));
        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Null(fixture.Fields[0].TemporalBinding);
        Assert.Equal(2, fixture.Fields[0].TemporalServerMetadata!.Value.CType);
        Assert.Equal(2, fixture.Fields[0].TemporalServerMetadata.Value.TypeFlag);
    }

    [Fact]
    public async Task UntypedNullRequiresReliableFixedOrExplicitType()
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        Assert.Throws<InvalidOperationException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value)));
        await fixture.Describe([new(26, 9, 7, 1)]);
        using var fixedNull = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value));
        Assert.Equal(26, fixedNull.TemporalBindings[0].CType); Assert.True(fixture.Fields[0].GetIsInDataNull(0));
        byte[] packet = fixture.Packet(); Assert.Equal(0xFFFE, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(77)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BothExplicitDeclarationsKeepDateTime2IntentRegardlessOfSetterOrder(bool providerFirst)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        var parameter = new DmParameter("p", DBNull.Value);
        if (providerFirst) { parameter.DmSqlType = DmDbType.DateTime; parameter.DbType = DbType.DateTime2; }
        else { parameter.DbType = DbType.DateTime2; parameter.DmSqlType = DmDbType.DateTime; }
        using var plan = fixture.Bind("SELECT :p FROM DUAL", parameter.Clone());
        Assert.Equal(26, plan.TemporalBindings[0].CType); Assert.Equal(7, plan.TemporalBindings[0].EncodedScale);
        Assert.True(plan.ParameterMetadata[0].ExplicitDbType); Assert.True(plan.ParameterMetadata[0].ExplicitDmSqlType);
        Assert.Equal(DbType.DateTime2, plan.ParameterMetadata[0].DeclaredDbType);
        parameter.ResetDbType(); parameter.Value = new DateTime(2026, 7, 23);
        using var reset = fixture.Bind("SELECT :p FROM DUAL", parameter);
        Assert.False(reset.ParameterMetadata[0].ExplicitDbType); Assert.False(reset.ParameterMetadata[0].ExplicitDmSqlType);
        Assert.Equal(7, reset.TemporalBindings[0].EncodedScale);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public async Task AdvisoryExplicitScaleUsesAStableTypeAndPreservesExplicitZero(byte scale)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        using var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value)
            { DbType = DbType.DateTime2, Scale = scale, Precision = 0 });
        var binding = plan.TemporalBindings[0];
        Assert.Equal(scale == 7 ? 26 : 16, binding.CType); Assert.Equal((int)scale, binding.EncodedScale);
        Assert.Equal((int)scale, binding.RequestedScale); Assert.Equal(scale == 7 ? 9 : 8, binding.Precision);
        Assert.True(binding.Request.ExplicitPrecision); Assert.Equal((byte)0, binding.Request.Precision);
    }

    [Theory]
    [InlineData(19)] [InlineData(23)] [InlineData(255)]
    public void UserScaleRangeFailurePrecedesPrecisionAndNoBusinessExecuteIsSent(byte scale)
    {
        using var command = new DmCommand("SELECT :p FROM DUAL");
        command.Parameters.Add(new DmParameter("p", DBNull.Value) { DbType = DbType.DateTime2, Scale = scale, Precision = 7 });
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<ArgumentOutOfRangeException>(() => command.ExecuteNonQuery());
        Assert.Equal(sends, DmWireTestHooks.SendCount);
    }

    [Fact]
    public void TemporalNonzeroPrecisionRejectsAndDecimalPrecisionRemainsUnrestricted()
    {
        using var command = new DmCommand("SELECT :p FROM DUAL");
        command.Parameters.Add(new DmParameter("p", DBNull.Value) { DbType = DbType.DateTime, Precision = 7 });
        Assert.Throws<NotSupportedException>(() => command.ExecuteNonQuery());
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("p", 1m) { DbType = DbType.Decimal, Precision = 28, Scale = 19 });
        using var plan = DmCommandPlan.Capture("SELECT :p FROM DUAL", CommandType.Text, 30, null!, null!, parameters);
        Assert.Equal((byte)28, plan.ParameterMetadata[0].Precision); Assert.Equal((byte)19, plan.ParameterMetadata[0].Scale);
    }

    [Theory]
    [InlineData(8)] [InlineData(9)]
    public async Task TypedNullCannotBypassClosedHigherPrecision(byte scale)
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL",
            new DmParameter("p", DBNull.Value) { DbType = DbType.DateTime2, Scale = scale }));
        Assert.Null(fixture.Fields[0].TemporalBinding);
    }

    [Fact]
    public async Task IndependentFixedColumnAndRequestedScalesReachTheActualWriter()
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(26, 9, 6, 1)]);
        DateTime exact = new DateTime(2026, 7, 23, 14, 15, 16).AddTicks(1234560);
        using (var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", exact) { Scale = 7 }))
        {
            var binding = plan.TemporalBindings[0];
            Assert.Equal(6, binding.ColumnScale); Assert.Equal(7, binding.RequestedScale); Assert.Equal(7, binding.EncodedScale);
            Assert.Equal(6, fixture.Fields[0].GetScale());
            Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(fixture.Packet().AsSpan(73)));
        }
        Assert.Throws<OverflowException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", exact.AddTicks(1)) { Scale = 7 }));
        Assert.Null(fixture.Fields[0].TemporalBinding);
        await fixture.Describe([new(26, 9, 7, 1)]);
        using (var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", exact.AddTicks(-4560)) { Scale = 3 }))
            Assert.Equal(3, plan.TemporalBindings[0].EncodedScale);
        Assert.Throws<OverflowException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", exact) { Scale = 3 }));
        await fixture.Describe([new(16, 8, 6, 1)]);
        Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value) { DbType = DbType.DateTime2, Scale = 7 }));
    }

    [Fact]
    public async Task OccurrencesKeepDifferentFixedColumnsAndFailedSecondBindingPublishesNothing()
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(26, 9, 7, 1), new(26, 9, 6, 1)]);
        using (var plan = fixture.Bind("SELECT :p, :p FROM DUAL", new DmParameter("p", new DateTime(2026, 7, 23).AddTicks(10))))
        {
            Assert.Equal(7, plan.TemporalBindings[0].ColumnScale); Assert.Equal(6, plan.TemporalBindings[1].ColumnScale);
            Assert.NotSame(fixture.Fields[0].TemporalBinding, fixture.Fields[1].TemporalBinding);
        }
        Assert.Throws<OverflowException>(() => fixture.Bind("SELECT :p, :p FROM DUAL", new DmParameter("p", new DateTime(2026, 7, 23).AddTicks(1))));
        Assert.All(fixture.Fields, field => Assert.Null(field.TemporalBinding));
        await fixture.Describe([new(2, 64, 0, 2)]);
        using var reprepare = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DateTime.MinValue));
        Assert.Single(reprepare.TemporalBindings); Assert.False(reprepare.TemporalBindings[0].ReliableFixed);
    }

    [Fact]
    public async Task CurrentPhysicalProfileRebuildsAndUnknownCannotInheritSeven()
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(2, 64, 0, 2)]);
        using (var shared = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DateTime.MinValue)))
            Assert.Equal(26, shared.TemporalBindings[0].CType);
        fixture.Physical.ConnProperty.property[DmConst.PROP_KEY_ENCODING] = "utf-8";
        using var unknown = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DateTime.MinValue));
        Assert.Equal(16, unknown.TemporalBindings[0].CType); Assert.Equal(6, unknown.TemporalBindings[0].EncodedScale);
        Assert.False(unknown.TemporalBindings[0].Profile.Identity.EncodingOverrideEmpty);
        Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value) { DbType = DbType.DateTime2 }));
        await fixture.Describe([new(26, 9, 6, 1)]);
        using var narrow = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DateTime.MinValue));
        Assert.Equal(26, narrow.TemporalBindings[0].CType); Assert.True(narrow.TemporalBindings[0].ReliableFixed);
    }

    [Fact]
    public async Task IntervalPackedDeclarationIsNotAUserScaleMask()
    {
        var fixture = new BindingFixture(); await fixture.Describe([new(21, 24, 0x696, 1)]);
        TimeSpan exact = TimeSpan.FromHours(25).Add(TimeSpan.FromTicks(1230000));
        using (var plan = fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", exact) { Scale = 3 }))
        {
            var binding = plan.TemporalBindings[0];
            Assert.Equal(6, binding.ColumnScale); Assert.Equal(3, binding.RequestedScale); Assert.Equal(6, binding.EncodedScale);
            Assert.Equal(0x696, binding.WireScale); Assert.Equal(0x696, binding.EncodingPackedScale);
            Assert.Equal(9, binding.IntervalLeadingPrecision); Assert.Equal(6, binding.IntervalSubtype);
            byte[] packet = fixture.Packet(); Assert.Equal(0x696, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(73)));
            Assert.Equal(0x696, BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(99)));
        }
        Assert.Throws<OverflowException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", exact.Add(TimeSpan.FromTicks(10))) { Scale = 3 }));
        Assert.Throws<NotSupportedException>(() => fixture.Bind("SELECT :p FROM DUAL", new DmParameter("p", DBNull.Value) { DmSqlType = DmDbType.IntervalDayToSecond, Scale = 7 }));
    }

    internal readonly record struct DescribeField(int Type, int Precision, int Scale, byte Flag);
    internal sealed class BindingFixture
    {
        private readonly TemporalNativeWriterAdapterTests.WriterFixture codec = new("8.1.5.60", 21, "utf-8");
        internal DmConnInstance Physical => codec.Physical;
        internal DmParameterInternal[] Fields => codec.Statement.H().GetParamsInfo();
        internal DmColumn[] Columns => codec.Statement.H().GetColumnsInfo();
        private readonly DmCommand command;
        internal BindingFixture()
        {
            command = new DmCommand { Connection = codec.Connection };
            var session = new DmSession();
            Set(Physical, "<Session>k__BackingField", session);
            Set(codec.Connection, "session", session);
            codec.Statement.__t02_method_0600089A(command);
            Set(command, "m_Stmt", codec.Statement); Set(command, "statementSession", session);
            Set(command, "statementConnection", codec.Connection);
        }
        internal async Task Describe(DescribeField[] fields, bool asyncRead = false)
        {
            byte[] frame = PrepareFrame(fields);
            await DecodeFrame(frame, asyncRead);
        }
        internal async Task DescribeResults(DescribeField[] fields, bool asyncRead = false)
        {
            byte[] frame = PrepareFrame(fields);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(22), 0);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(24), checked((short)fields.Length));
            frame[19] = 0;
            for (int index = 0; index < 19; index++) frame[19] ^= frame[index];
            await DecodeFrame(frame, asyncRead);
        }
        private async Task DecodeFrame(byte[] frame, bool asyncRead)
        {
            using var stream = new MemoryStream(frame);
            var buffer = new LegacyBuffer();
            if (asyncRead) await DmFrameReader.ReadAsync(stream, buffer, false, DmDeadline.Infinite);
            else DmFrameReader.Read(stream, buffer, false, DmDeadline.Infinite);
            // Both production prepare routes invoke this same decoder after their respective I/O.
            c.A(buffer, codec.Statement);
        }
        internal DmCommandPlan Bind(string sql, params DmParameter[] parameters)
        {
            var collection = new DmParameterCollection(); foreach (var parameter in parameters) collection.Add(parameter);
            var plan = DmCommandPlan.Capture(sql, CommandType.Text, 30, null!, codec.Connection, collection);
            Set(command, "activePlan", plan);
            try
            {
                Invoke(() => typeof(DmCommand).GetMethod("BindParameters", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(command, [0, null, CommandBehavior.Default]));
                return plan;
            }
            catch { plan.Dispose(); throw; }
            finally { Set(command, "activePlan", null!); }
        }
        internal byte[] Packet()
        {
            var buffer = new LegacyBuffer(); C.A(buffer, codec.Statement, codec.Statement.H(), Physical.ConnProperty);
            return buffer.A()[..buffer.B()];
        }
        internal static byte[] PrepareFrame(DescribeField[] fields)
        {
            byte[] frame = new byte[64 + 32 * fields.Length];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), 5);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), frame.Length - 64);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(22), checked((ushort)fields.Length));
            for (int index = 0; index < fields.Length; index++)
            {
                Span<byte> record = frame.AsSpan(64 + 32 * index, 32); var field = fields[index];
                BinaryPrimitives.WriteInt32LittleEndian(record, field.Type);
                BinaryPrimitives.WriteInt32LittleEndian(record[4..], field.Precision);
                BinaryPrimitives.WriteInt32LittleEndian(record[8..], field.Scale);
                BinaryPrimitives.WriteInt32LittleEndian(record[12..], 1);
                BinaryPrimitives.WriteInt16LittleEndian(record[16..], field.Flag == 2 ? (short)8 : (short)0);
            }
            for (int index = 0; index < 19; index++) frame[19] ^= frame[index];
            return frame;
        }
        internal static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        internal static void Invoke(Action action)
        {
            try { action(); }
            catch (TargetInvocationException wrapper) when (wrapper.InnerException != null)
            { ExceptionDispatchInfo.Capture(wrapper.InnerException).Throw(); throw; }
        }
    }
}
