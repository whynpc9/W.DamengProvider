using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class DeriveParametersTests
{
    [Theory]
    [InlineData("")]
    [InlineData("p()")]
    [InlineData("p; SELECT 1 FROM DUAL")]
    [InlineData("p -- suffix")]
    [InlineData("p/*comment*/")]
    [InlineData("a.b.c")]
    [InlineData("\"unterminated")]
    [InlineData("\"\"")]
    public void InvalidNamesRejectBeforeConnectionOrWireAndPreserveParameters(string name)
    {
        using var source = Source(name);
        DmParameter original = source.Parameters.do_GetParameter(0);
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<ArgumentException>(() => DmCommandBuilder.DeriveParameters(source));
        Assert.Same(original, source.Parameters[0]);
        Assert.Same(source.Parameters, original.parameterCollection);
        Assert.Equal(sends, DmWireTestHooks.SendCount);
    }

    [Fact]
    public void NullWrongTypeDisposedAndExecutingRejectBeforeIo()
    {
        long sends = DmWireTestHooks.SendCount;
        Assert.Throws<ArgumentNullException>(() => DmCommandBuilder.DeriveParameters(null!));
        using var text = new DmCommand("SELECT 1 FROM DUAL");
        Assert.Throws<InvalidOperationException>(() => DmCommandBuilder.DeriveParameters(text));
        var disposed = Source("p"); disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => DmCommandBuilder.DeriveParameters(disposed));
        using var source = Source("p");
        using (var plan = source.Parameters.PlanGate.Enter(() => DmCommandPlan.Capture(
            source.CommandText, source.CommandType, 30, null, null, source.Parameters)))
            Assert.Throws<InvalidOperationException>(() => DmCommandBuilder.DeriveParameters(source));
        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Equal("original", source.Parameters[0].ParameterName);
    }

    [Fact]
    public void AnotherThreadCannotDeriveOrExecuteWhileDiscoveryOwnsMutation()
    {
        using var source = Source("p");
        using (source.Parameters.PlanGate.BeginMutation())
        {
            // Do not await while holding the thread-affine mutation scope.
            Exception? failure = Task.Run(() => Record.Exception(() => DmCommandBuilder.DeriveParameters(source)))
                .GetAwaiter().GetResult();
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Throws<InvalidOperationException>(() => source.ExecuteNonQuery());
        }
        source.CommandText = "after";
    }

    [Theory]
    [InlineData(" worker ", "WORKER", null)]
    [InlineData(" app . worker ", "APP", "WORKER")]
    [InlineData("\"MiX\".\"a.'b\"\"c\"", "MiX", "a.'b\"c")]
    [InlineData("\"a.b\"", "a.b", null)]
    public void SharedParserReturnsDecodedPartsWithoutChangingExecutionGrammar(string name, string first, string? second)
    {
        Assert.Equal(second == null ? new[] { first } : new[] { first, second }, DmProcedureCall.ParseIdentifier(name));
        using var parameters = new DmCommand();
        string expected = "CALL " + Quote(first) + (second == null ? "" : "." + Quote(second)) + "()";
        Assert.Equal(expected, DmProcedureCall.Create(name, parameters.Parameters));
    }

    [Fact]
    public void ProjectionSortsCatalogSequenceAndTreatsPositionZeroAsReturnEvenWhenNamed()
    {
        var result = DmDerivedParameters.Project(new[]
        {
            new DmDerivedParameterRow("B", 2, "INT", "IN/OUT", 3),
            new DmDerivedParameterRow("V_RET", 0, "INT", "OUT", 1),
            new DmDerivedParameterRow("A", 1, "VARCHAR", "IN", 2)
        }, true);
        Assert.Equal(new[] { "V_RET", "A", "B" }, result.Select(p => p.ParameterName));
        Assert.Equal(new[] { ParameterDirection.ReturnValue, ParameterDirection.Input, ParameterDirection.InputOutput },
            result.Select(p => p.Direction));
        Assert.Equal(new[] { DmDbType.Int32, DmDbType.VarChar, DmDbType.Int32 }, result.Select(p => p.DmSqlType));
        Assert.All(result, parameter => Assert.Null(parameter.parameterCollection));
    }

    [Theory]
    [InlineData("IN", ParameterDirection.Input)]
    [InlineData("OUT", ParameterDirection.Output)]
    [InlineData("IN/OUT", ParameterDirection.InputOutput)]
    public void ProcedurePositionZeroIsAnArgumentWhoseDirectionComesFromCatalog(string direction, ParameterDirection expected)
    {
        var result = DmDerivedParameters.Project(new[]
        {
            new DmDerivedParameterRow("A", 0, "INT", direction, 1),
            new DmDerivedParameterRow("B", 1, "INT", "OUT", 2)
        }, false);
        Assert.Equal(expected, result[0].Direction);
        Assert.Equal(ParameterDirection.Output, result[1].Direction);
        Assert.DoesNotContain(result, parameter => parameter.Direction == ParameterDirection.ReturnValue);
    }

    [Fact]
    public void ZeroArgumentsAndCursorAndTimeZoneTypesHaveExplicitMeaning()
    {
        Assert.Empty(DmDerivedParameters.Project(Array.Empty<DmDerivedParameterRow>(), false));
        Assert.Throws<InvalidOperationException>(() => DmDerivedParameters.Project(Array.Empty<DmDerivedParameterRow>(), true));
        var result = DmDerivedParameters.Project(new[]
        {
            new DmDerivedParameterRow("C", 0, "SYS_REFCURSOR", "OUT", 1),
            new DmDerivedParameterRow("T", 1, "TIME WITH TIME ZONE", "IN", 2),
            new DmDerivedParameterRow("D", 2, "TIMESTAMP WITH TIME ZONE", "IN", 3)
        }, false);
        Assert.Equal(new[] { DmDbType.Cursor, DmDbType.TimeOffset, DmDbType.DateTimeOffset }, result.Select(p => p.DmSqlType));
    }

    [Theory]
    [InlineData("duplicate-name")]
    [InlineData("duplicate-sequence")]
    [InlineData("position-gap")]
    [InlineData("missing-name")]
    [InlineData("direction")]
    [InlineData("unknown-type")]
    public void InvalidCatalogCannotPartiallyReplaceOriginalParameters(string fault)
    {
        using var source = Source("p");
        DmParameter original = source.Parameters.do_GetParameter(0);
        var rows = new[]
        {
            new DmDerivedParameterRow("A", 0, "INT", "IN", 1),
            new DmDerivedParameterRow("B", 1, "INT", "OUT", 2)
        };
        rows[1] = fault switch
        {
            "duplicate-name" => rows[1] with { Name = ":a" },
            "duplicate-sequence" => rows[1] with { Sequence = 1 },
            "position-gap" => rows[1] with { Position = 3 },
            "missing-name" => rows[1] with { Name = "" },
            "direction" => rows[1] with { Direction = "unknown" },
            _ => rows[1] with { TypeName = "UNRECOGNIZED_CATALOG_TYPE" }
        };
        Assert.NotNull(Record.Exception(() => source.Parameters.ReplaceDerivedParameters(DmDerivedParameters.Project(rows, false))));
        Assert.Single(source.Parameters.Cast<DmParameter>());
        Assert.Same(original, source.Parameters[0]);
        Assert.Equal(17, original.Value);
        Assert.Same(source.Parameters, original.parameterCollection);
    }

    [Fact]
    public void PackageArgumentsUseParameterIdOrderingAndIoFlagWithoutAssumingAStartingId()
    {
        var result = DmDerivedParameters.ProjectPackage(new[]
        {
            new DmDerivedPackageParameterRow("C", 91, "INT", "IO"),
            new DmDerivedPackageParameterRow("A", 14, "INT", "I"),
            new DmDerivedPackageParameterRow("B", 37, "VARCHAR", "O")
        });
        Assert.Equal(new[] { "A", "B", "C" }, result.Select(parameter => parameter.ParameterName));
        Assert.Equal(new[] { ParameterDirection.Input, ParameterDirection.Output, ParameterDirection.InputOutput },
            result.Select(parameter => parameter.Direction));
        Assert.Equal(new[] { DmDbType.Int32, DmDbType.VarChar, DmDbType.Int32 }, result.Select(parameter => parameter.DmSqlType));
    }

    [Fact]
    public void PackageZeroArgumentProcedureAndFunctionsRetainExplicitReturnMetadata()
    {
        Assert.Empty(DmDerivedParameters.ProjectPackage(Array.Empty<DmDerivedPackageParameterRow>()));
        var function = DmDerivedParameters.ProjectPackage(new[]
        {
            new DmDerivedPackageParameterRow("A", 8, "INT", "I"),
            new DmDerivedPackageParameterRow("V_RET", 25, "INT", "R")
        });
        Assert.Equal(ParameterDirection.Input, function[0].Direction);
        Assert.Equal(ParameterDirection.ReturnValue, function[1].Direction);
        var zeroArgumentFunction = DmDerivedParameters.ProjectPackage(new[]
        {
            new DmDerivedPackageParameterRow(null!, 30, "INT", "R")
        });
        Assert.Equal("RETURN_VALUE", Assert.Single(zeroArgumentFunction).ParameterName);
        Assert.Equal(ParameterDirection.ReturnValue, zeroArgumentFunction[0].Direction);
        var procedure = DmDerivedParameters.ProjectPackage(new[] { new DmDerivedPackageParameterRow("A", 0, "INT", "O") });
        Assert.Equal(ParameterDirection.Output, Assert.Single(procedure).Direction);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-name")]
    [InlineData("multiple-returns")]
    [InlineData("unknown-direction")]
    [InlineData("unknown-type")]
    public void InvalidPackageMetadataPreservesOriginalParameters(string fault)
    {
        using var source = Source("PKG.P");
        DmParameter original = source.Parameters.do_GetParameter(0);
        var rows = new[]
        {
            new DmDerivedPackageParameterRow("A", 10, "INT", "R"),
            new DmDerivedPackageParameterRow("B", 42, "INT", "I")
        };
        rows[1] = fault switch
        {
            "duplicate-id" => rows[1] with { ParameterId = 10 },
            "duplicate-name" => rows[1] with { Name = ":a" },
            "multiple-returns" => rows[1] with { Direction = "R" },
            "unknown-direction" => rows[1] with { Direction = "unknown" },
            _ => rows[1] with { TypeName = "UNRECOGNIZED_CATALOG_TYPE" }
        };
        Assert.NotNull(Record.Exception(() => source.Parameters.ReplaceDerivedParameters(DmDerivedParameters.ProjectPackage(rows))));
        Assert.Single(source.Parameters.Cast<DmParameter>());
        Assert.Same(original, source.Parameters[0]);
        Assert.Equal(17, original.Value);
        Assert.Same(source.Parameters, original.parameterCollection);
    }

    [Fact]
    public void PackageMemberLookupDistinguishesMissingAndUniqueFromOverloaded()
    {
        Assert.Null(DmDerivedParameters.UniqueMemberId(Array.Empty<object[]>()));
        Assert.Equal(81L, DmDerivedParameters.UniqueMemberId(new[] { new object[] { 81 } }));
        Assert.Throws<InvalidOperationException>(() => DmDerivedParameters.UniqueMemberId(new[]
        { new object[] { 81 }, new object[] { 96 } }));
    }

    [Fact]
    public void PackageCatalogQueryBindsBothIdsAndInheritsSourceSettings()
    {
        using var connection = new DmConnection();
        using var source = Source("\"pkg.'name\".\"member\"");
        source.Connection = connection;
        source.CommandTimeout = 19;
        using var query = DmDerivedParameters.CreatePackageArgumentsQuery(source, 13579, 24680);
        Assert.Same(connection, query.Connection);
        Assert.Equal(19, query.CommandTimeout);
        Assert.Equal(CommandType.Text, query.CommandType);
        Assert.Contains("PKGID=:package_id AND MTDID=:member_id ORDER BY PARAMID", query.CommandText);
        Assert.DoesNotContain("13579", query.CommandText);
        Assert.DoesNotContain("24680", query.CommandText);
        Assert.DoesNotContain("pkg.'name", query.CommandText);
        Assert.Equal(2, query.Parameters.Count);
        Assert.Equal(13579L, query.Parameters["package_id"].Value);
        Assert.Equal(24680L, query.Parameters["member_id"].Value);
        Assert.All(query.Parameters.Cast<DmParameter>(), parameter => Assert.Equal(DbType.Int64, parameter.DbType));
        using var plan = DmCommandPlan.Capture(query.CommandText, query.CommandType, query.CommandTimeout,
            null, connection, query.Parameters);
        Assert.Equal(query.CommandText, plan.WireSql);
        Assert.Equal(2, plan.Binding.MarkerCount);
        DmParameter package = plan.Binding.ResolveServerParameter(0, "", 2);
        DmParameter member = plan.Binding.ResolveServerParameter(1, "", 2);
        Assert.Equal("package_id", package.ParameterName);
        Assert.Equal("member_id", member.ParameterName);
        Assert.Equal(DbType.Int64, package.DbType);
        Assert.Equal(DbType.Int64, member.DbType);
        Assert.Equal(13579L, Assert.IsType<long>(package.Value));
        Assert.Equal(24680L, Assert.IsType<long>(member.Value));
    }

    [Fact]
    public void AtomicReplacementRetainsCollectionIdentityAndReleasesOldOwnership()
    {
        using var source = Source("p");
        DmParameterCollection collection = source.Parameters;
        DmParameter original = collection.do_GetParameter(0);
        var replacement = new[] { new DmParameter("new", DmDbType.Int32, ParameterDirection.Output) };
        collection.ReplaceDerivedParameters(replacement);
        Assert.Same(collection, source.Parameters);
        Assert.Same(replacement[0], collection["new"]);
        Assert.Same(collection, replacement[0].parameterCollection);
        Assert.Null(original.parameterCollection);
        using var other = new DmCommand();
        other.Parameters.Add(original);
        Assert.Throws<InvalidOperationException>(() => collection.ReplaceDerivedParameters(new[] { original }));
        Assert.Same(replacement[0], collection[0]);
        Assert.Same(other.Parameters, original.parameterCollection);
    }

    [Fact]
    public void DuplicateReplacementAndDisposedOwnerPreserveExistingSet()
    {
        using var source = Source("p");
        DmParameter original = source.Parameters.do_GetParameter(0);
        var first = new DmParameter("same", 1);
        var second = new DmParameter(":SAME", 2);
        Assert.Throws<InvalidOperationException>(() => source.Parameters.ReplaceDerivedParameters(new[] { first, second }));
        Assert.Same(original, source.Parameters[0]);
        Assert.Null(first.parameterCollection); Assert.Null(second.parameterCollection);
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Parameters.ReplaceDerivedParameters(new[] { first }));
        Assert.Same(original, source.Parameters[0]);
    }

    [Fact]
    public void PublicDiscoveryClosesSchemaReaderBeforeLookupAndUnwindsFailure()
    {
        using var database = new SchemaFixture();
        using var source = Source("\"A.'B\"\"C\"");
        source.Connection = database.Connection;
        source.CommandTimeout = 7;
        DmParameter original = source.Parameters.do_GetParameter(0);
        int captured = 0;
        DmCommand.AfterPlanCaptured = () =>
        {
            if (++captured != 2) return;
            Assert.Equal(1, database.Channel.ClosedStatements);
            Assert.Equal(DmPhysicalSessionState.Busy, database.Session.State);
            throw new CatalogCheckpoint();
        };
        try
        {
            Assert.Throws<CatalogCheckpoint>(() => DmCommandBuilder.DeriveParameters(source));
            Assert.Same(original, source.Parameters[0]);
            Assert.Same(source.Parameters, original.parameterCollection);
            Assert.Equal(new short[] { 3, 5, 4 }, database.Channel.Opcodes);
            Assert.Equal(DmPhysicalSessionState.Ready, database.Session.State);
            using var next = database.Session.BeginExecution(DmOperationPurpose.Query);
        }
        finally { DmCommand.AfterPlanCaptured = null; }
    }

    private sealed class CatalogCheckpoint : Exception { }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static DmCommand Source(string name)
    {
        var source = new DmCommand(name) { CommandType = CommandType.StoredProcedure };
        source.Parameters.Add(new DmParameter("original", 17));
        return source;
    }

    // Uses the same ready-session and opcode 5 result framing as SchemaPaginationTests.
    // Only the schema query is synthesized; the real catalog matrix lives in DeriveParametersProbe.
    private sealed class SchemaFixture : IDisposable
    {
        internal DmConnection Connection { get; } = new(new DmConnectionStringBuilder
        { Server = "127.0.0.1", User = "synthetic_test", Password = "synthetic_only" }.ConnectionString);
        internal DmSession Session { get; } = new();
        internal SchemaChannel Channel { get; } = new();
        internal SchemaFixture()
        {
            Set(Connection, "session", Session);
            Session.BeginConnecting();
            using (var lease = Session.BeginExecution(DmOperationPurpose.Handshake))
            using (var invocation = lease.BeginInvocation())
            {
                var instance = new DmConnInstance(Connection);
                Connection.m_ConnInst = instance;
                instance.ConnProperty.ServerVersion = "8.1.5.60";
                instance.ConnProperty.msgVersion = 9;
                var protocol = instance.GetCsi();
                var wrapper = protocol.A();
                var field = wrapper.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!;
                ((DmTransport)field.GetValue(wrapper)!).Dispose();
                field.SetValue(wrapper, new DmTransport(Channel));
                Set(wrapper, "__t02_field_04000AAD", false);
                Set(protocol, "__t02_field_04000ABD", false);
                Session.CompleteHandshake();
            }
            Connection.do_State = ConnectionState.Open;
        }
        public void Dispose() => Connection.Dispose();
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
    }

    private sealed class SchemaChannel : IDmByteChannel
    {
        private readonly MemoryStream request = new();
        private byte[]? response;
        private int position;
        internal List<short> Opcodes { get; } = new();
        internal int ClosedStatements;
        public bool IsClosed { get; private set; }
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        { request.Write(buffer, offset, count); return count; }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            if (response == null)
            {
                byte[] packet = request.ToArray(); request.SetLength(0);
                short opcode = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4));
                Opcodes.Add(opcode); position = 0;
                switch (opcode)
                {
                    case 3: response = Frame(3, Array.Empty<byte>()); BinaryPrimitives.WriteInt32LittleEndian(response, 41); break;
                    case 5: response = SchemaRow(); break;
                    case 4: ClosedStatements++; response = Frame(4, Array.Empty<byte>()); break;
                    default: throw new InvalidOperationException("Unexpected synthetic catalog opcode.");
                }
                Checksum(response);
            }
            int read = Math.Min(count, response.Length - position);
            response.AsSpan(position, read).CopyTo(buffer.AsSpan(offset)); position += read;
            if (position == response.Length) response = null;
            return read;
        }
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            throw new InvalidOperationException("Synchronous discovery used asynchronous transport.");
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            throw new InvalidOperationException("Synchronous discovery used asynchronous transport.");
        public void Dispose() => IsClosed = true;
        private static byte[] SchemaRow()
        {
            byte[] name = Encoding.ASCII.GetBytes("SCHEMA_NAME"), value = Encoding.ASCII.GetBytes("CURRENT_SCHEMA");
            byte[] row = new byte[18 + value.Length];
            BinaryPrimitives.WriteInt16LittleEndian(row.AsSpan(16), (short)value.Length); value.CopyTo(row, 18);
            byte[] body = new byte[32 + name.Length + row.Length];
            BinaryPrimitives.WriteInt32LittleEndian(body, 2);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 128);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 1);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(24), (short)name.Length);
            name.CopyTo(body, 32); row.CopyTo(body, 32 + name.Length);
            byte[] frame = Frame(5, body);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(20), 160);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(22), 1);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24), 1);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(35), 1);
            return frame;
        }
        private static byte[] Frame(short opcode, byte[] body)
        {
            byte[] frame = new byte[64 + body.Length];
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
            body.CopyTo(frame, 64); return frame;
        }
        private static void Checksum(byte[] frame)
        { frame[19] = 0; for (int index = 0; index < 19; index++) frame[19] ^= frame[index]; }
    }
}
