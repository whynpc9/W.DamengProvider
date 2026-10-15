using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.CommandTests;

public sealed class StoredProcedureLifecycleTests
{
    [Fact]
    public void CommandCancellationImmediatelyRevokesPublishedCursorOutputs()
    {
        using var fixture = new ProcedureReaders();
        Assert.Same(fixture.Children[0], fixture.Parameters[1].Value);
        fixture.Plan.RequestCancellation();
        Assert.Equal(7, fixture.Parameters[0].Value);
        Assert.Equal("old cursor 1", fixture.Parameters[1].Value);
        Assert.Equal("old cursor 2", fixture.Parameters[2].Value);
        Assert.Throws<InvalidOperationException>(() => fixture.Plan.PublishOutputs());
    }

    [Theory]
    [InlineData("value")]
    [InlineData("decimal")]
    [InlineData("int32")]
    [InlineData("generic")]
    [InlineData("async")]
    public async Task GetterFailureRevokesCursorsAndCloseCannotPublishStagedScalars(string getter)
    {
        using var fixture = new ProcedureReaders();
        Assert.Same(fixture.Children[0], fixture.Parameters[1].Value);
        Assert.Equal(7, fixture.Parameters[0].Value);
        if (getter == "async")
            await Assert.ThrowsAsync<OverflowException>(() => fixture.Parent.GetFieldValueAsync<decimal>(0));
        else
            Assert.Throws<OverflowException>(() =>
            {
                _ = getter switch
                {
                    "value" => fixture.Parent.GetValue(0),
                    "decimal" => fixture.Parent.GetDecimal(0),
                    "int32" => fixture.Parent.GetInt32(0),
                    _ => (object)fixture.Parent.GetFieldValue<decimal>(0)
                };
            });
        Assert.Equal(7, fixture.Parameters[0].Value);
        Assert.Equal("old cursor 1", fixture.Parameters[1].Value);
        Assert.All(fixture.Children, child => Assert.True(child.IsClosed));
        fixture.InvalidateStatements(); // Model handles already released; Close must perform no I/O.
        long sends = DmWireTestHooks.SendCount;
        fixture.Parent.Close();
        Assert.True(fixture.Parent.IsClosed);
        Assert.Equal(7, fixture.Parameters[0].Value);
        Assert.Equal(sends, DmWireTestHooks.SendCount);
    }

    [Fact]
    public void ChildDecodeFailureInvalidatesSiblingAndRevokesAllCursorOutputs()
    {
        using var fixture = new ProcedureReaders();
        Assert.Throws<OverflowException>(() => fixture.Children[0].GetDecimal(0));
        Assert.All(fixture.Children, child => Assert.True(child.IsClosed));
        Assert.Equal("old cursor 1", fixture.Parameters[1].Value);
        Assert.Equal("old cursor 2", fixture.Parameters[2].Value);
        Assert.Throws<DmException>(() => fixture.Children[1].GetValue(0));
        Assert.Throws<InvalidOperationException>(() => fixture.Plan.PublishOutputs());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedEarlyCloseEndsParentAndChildrenAndNeverRetriesWire(bool asynchronous)
    {
        using var fixture = new ProcedureReaders();
        // A stale statement while ordinary results remain forces failure in the
        // result-finishing phase, before CloseOwned has set its closed flag.
        fixture.ParentStatement.o();
        long sends = DmWireTestHooks.SendCount;
        if (asynchronous)
        {
            var first = await Assert.ThrowsAsync<DmException>(() => fixture.Parent.CloseAsync());
            Assert.Same(first, await Assert.ThrowsAsync<DmException>(() => fixture.Parent.CloseAsync()));
        }
        else
        {
            Assert.Throws<DmException>(() => fixture.Parent.Close());
            fixture.Parent.Close();
        }
        Assert.True(fixture.Parent.IsClosed);
        Assert.All(fixture.Children, child => Assert.True(child.IsClosed));
        Assert.Null(fixture.Parent.m_Statement);
        Assert.All(fixture.Children, child => Assert.Null(child.m_Statement));
        Assert.Equal(7, fixture.Parameters[0].Value);
        Assert.Equal("old cursor 1", fixture.Parameters[1].Value);
        Assert.Equal(sends, DmWireTestHooks.SendCount);
        Assert.Throws<InvalidOperationException>(() => fixture.Children[0].GetValue(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulParentClosePublishesScalarAndPermanentlyClosesChildren(bool asynchronous)
    {
        using var fixture = new ProcedureReaders();
        fixture.MarkParentTerminal();
        fixture.InvalidateStatements(); // No fake server statement is sent a close.
        if (asynchronous) await fixture.Parent.CloseAsync(); else fixture.Parent.Close();
        Assert.True(fixture.Parent.IsClosed);
        Assert.Equal(99, fixture.Parameters[0].Value);
        Assert.All(fixture.Children, child => Assert.True(child.IsClosed));
        Assert.Throws<InvalidOperationException>(() => fixture.Children[0].Read());
        // Reusing the physical session with another execution cannot revive an old cursor.
        using var next = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        Assert.Throws<InvalidOperationException>(() => fixture.Children[0].GetValue(0));
    }

    [Theory]
    [InlineData((short)1, (byte)1)]
    [InlineData((short)2, (byte)2)]
    public void OutputCursorAcceptsCharacterizedDescribeModeAcrossPrepareCache(short mode, byte flag)
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("c", DmDbType.RefCursor, ParameterDirection.Output));
        using var plan = Capture(parameters);
        var descriptor = Describe("C", 120, mode, flag);
        plan.Binding.ValidateServerParameters([descriptor]);
        Assert.Same(plan.Parameters.do_GetParameter(0), plan.Binding.ResolveServerParameter(0, "C", 1));
    }

    [Fact]
    public void CursorDescribeExceptionDoesNotRelaxScalarDirectionOrPublicCursorDirection()
    {
        var scalar = new DmParameterCollection();
        scalar.Add(new DmParameter("p", DmDbType.Int32, ParameterDirection.Output));
        using var scalarPlan = Capture(scalar);
        Assert.Throws<InvalidOperationException>(() => scalarPlan.Binding.ValidateServerParameters([Describe("P", 7, 2, 2)]));
        var cursor = new DmParameterCollection();
        cursor.Add(new DmParameter("c", DmDbType.RefCursor, ParameterDirection.InputOutput));
        Assert.Throws<NotSupportedException>(() => Capture(cursor));
    }

    [Theory]
    [InlineData(28, 0)]
    [InlineData(12, 5)]
    public void UnsupportedOutputsRejectAtDescribeBeforeExecution(int type, int scale)
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter { ParameterName = "p", Direction = ParameterDirection.Output });
        using var plan = Capture(parameters);
        var descriptor = Describe("P", type, 1, 1);
        descriptor.SetScale(scale);
        Assert.Throws<NotSupportedException>(() => plan.Binding.ValidateServerParameters([descriptor]));
    }

    private static DmCommandPlan Capture(DmParameterCollection parameters) =>
        DmCommandPlan.Capture("worker", CommandType.StoredProcedure, 30, null, null, parameters);

    private static DmParameterInternal Describe(string name, int type, short mode, byte flag)
    {
        var descriptor = Uninitialized<DmParameterInternal>();
        descriptor.name = name;
        descriptor.SetCType(type);
        descriptor.SetInOutType(mode);
        descriptor.SetTypeFlag(flag);
        return descriptor;
    }

    // The same synthetic row/real lease technique used by ReaderReviewTests and
    // ConnectionDetachReviewTests. No network or new transport framework is needed.
    private sealed class ProcedureReaders : IDisposable
    {
        private readonly DmConnection connection = new();
        private readonly DmExecutionLease lease;
        private readonly DmConnInstance physical;
        private readonly List<Statement> statements = [];
        internal DmSession Session { get; } = new();
        internal DmParameterCollection Parameters { get; } = new();
        internal DmCommandPlan Plan { get; }
        internal DmDataReader Parent { get; }
        internal DmDataReader[] Children { get; }
        internal Statement ParentStatement => statements[0];

        internal ProcedureReaders()
        {
            Session.CompleteHandshakeForTests();
            lease = Session.BeginExecution(DmOperationPurpose.Reader);
            physical = Uninitialized<DmConnInstance>();
            connection.ConnProperty.ServerEncoding = "UTF-8";
            Set(physical, "m_ConnPro", connection.ConnProperty);
            Set(physical, "<Session>k__BackingField", Session);
            physical.Conn = connection;
            Parameters.Add(new DmParameter("scalar", DmDbType.Int32, ParameterDirection.Output) { Value = 7 });
            Parameters.Add(new DmParameter("c1", DmDbType.RefCursor, ParameterDirection.Output) { Value = "old cursor 1" });
            Parameters.Add(new DmParameter("c2", DmDbType.RefCursor, ParameterDirection.Output) { Value = "old cursor 2" });
            Plan = Capture(Parameters);
            Parent = CreateReader();
            Children = [CreateReader(), CreateReader()];
            Parent.AttachExecutionLease(lease);
            Parent.AttachCommandPlan(Plan, plan => plan.Dispose());
            foreach (var child in Children) child.AttachExecutionLease(lease, ownsLease: false);
            Parent.AttachProcedureExecution(Plan, Children);
            Plan.StageOutput(Plan.Parameters.do_GetParameter(0), 99);
            Plan.StageOutput(Plan.Parameters.do_GetParameter(1), Children[0]);
            Plan.StageOutput(Plan.Parameters.do_GetParameter(2), Children[1]);
            Plan.PublishCursorOutputs();
        }

        private DmDataReader CreateReader()
        {
            var statement = Uninitialized<Statement>();
            Set(statement, "__t02_field_04000923", physical);
            statements.Add(statement);
            var column = Uninitialized<DmColumn>();
            column.SetCType(9);
            column.SetPrecision(38);
            column.SetScale(0);
            var info = new DmInfo();
            info.SetColumnsInfo([column]);
            info.SetHasResultSet(true);
            info.SetRowCount(1);
            var cache = new DmResultSetCache(statement, 1, 1)
            {
                currentPos = 0,
                datasOffset = 0,
                datas = [[[], DmNumericCodec.EncodeDecimal(DmDecimal.Parse("12345678901234567890123456789012345678"))]]
            };
            var reader = new DmDataReader(cache, info, CommandBehavior.Default);
            Set(reader, "m_CurrentRow", 0L);
            return reader;
        }

        internal void MarkParentTerminal()
        {
            var cursor = (DmResultCursor)typeof(DmDataReader).GetField("resultCursor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Parent)!;
            var terminal = new DmInfo();
            terminal.MarkTerminal();
            cursor.Observe(terminal);
        }

        internal void InvalidateStatements()
        {
            foreach (var statement in statements) statement.o();
        }

        public void Dispose()
        {
            InvalidateStatements();
            Plan.FailOutputs();
            Plan.Dispose();
            lease.Dispose();
            connection.Dispose();
        }
    }

    private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, string name, object value) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
}
