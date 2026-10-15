using System.Data;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class StoredProcedureBindingTests
{
    [Theory]
    [InlineData("worker", "CALL \"WORKER\"()")]
    [InlineData(" app . worker ", "CALL \"APP\".\"WORKER\"()")]
    [InlineData("\"MiX\".\"a\"\"b\"", "CALL \"MiX\".\"a\"\"b\"()")]
    public void IdentifierParserQuotesOnlyIdentifier(string text, string expected)
    {
        using var plan = Capture(new DmParameterCollection(), text);
        Assert.Equal(expected, plan.WireSql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a()")]
    [InlineData("a; DELETE FROM b")]
    [InlineData("a -- comment")]
    [InlineData("a/*comment*/")]
    [InlineData("a.b.c")]
    [InlineData("a b")]
    [InlineData("\"unterminated")]
    [InlineData("\"\"")]
    public void AdditionalSqlAndAmbiguousIdentifiersReject(string text) =>
        Assert.Throws<ArgumentException>(() => Capture(new DmParameterCollection(), text));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FunctionReturnAlwaysOccupiesServerSlotZero(int returnPosition)
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("a", 2));
        parameters.Add(new DmParameter("b", 3));
        parameters.Insert(returnPosition, new DmParameter("result", DmDbType.Int32, ParameterDirection.ReturnValue));
        using var plan = Capture(parameters);
        Assert.Equal("? = \"WORKER\"(?,?)", plan.WireSql);
        var described = new[] { Describe("", 1), Describe("A", 0), Describe("B", 0) };
        plan.Binding.ValidateServerParameters(described);
        Assert.Equal("result", plan.Binding.ResolveServerParameter(0, "", 3).ParameterName);
        Assert.Equal(2, plan.Binding.ResolveServerParameter(1, "A", 3).Value);
        Assert.Equal(3, plan.Binding.ResolveServerParameter(2, "B", 3).Value);
    }

    [Fact]
    public void NamedServerMappingUsesAllNamesWithCollectionPrefixAndCaseRules()
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("@c", DmDbType.Int32, ParameterDirection.InputOutput) { Value = 4 });
        parameters.Add(new DmParameter(":b", DmDbType.Int32, ParameterDirection.Output));
        parameters.Add(new DmParameter("a", 2));
        using var plan = Capture(parameters);
        plan.Binding.ValidateServerParameters([Describe("A", 0), Describe("B", 1), Describe("C", 2)]);
        Assert.Equal("a", plan.Binding.ResolveServerParameter(0, "A", 3).ParameterName);
        Assert.Equal(":b", plan.Binding.ResolveServerParameter(1, "B", 3).ParameterName);
        Assert.Equal("@c", plan.Binding.ResolveServerParameter(2, "C", 3).ParameterName);
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ResolveServerParameter(2, "OTHER", 3));
    }

    [Fact]
    public void InvalidDescriptionsCannotLeavePartialOrStaleMapping()
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("a", 2));
        parameters.Add(new DmParameter("b", DmDbType.Int32, ParameterDirection.Output));
        using var plan = Capture(parameters);
        plan.Binding.ValidateServerParameters([Describe("A", 0), Describe("B", 1)]);
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0), Describe("missing", 1)]));
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ResolveServerParameter(0, "A", 2));
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0), Describe("", 1)]));
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0), Describe(":a", 1)]));
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0), Describe("B", 0)]));
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0), Describe("B", 3)]));
        Assert.Throws<InvalidOperationException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0)]));
        var complex = Describe("B", 1);
        complex.SetCType(117);
        Assert.Throws<NotSupportedException>(() => plan.Binding.ValidateServerParameters([Describe("A", 0), complex]));
    }

    [Fact]
    public void EntirelyUnnamedDescriptionUsesPosition()
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("first", 2));
        parameters.Add(new DmParameter("second", 3));
        using var plan = Capture(parameters);
        plan.Binding.ValidateServerParameters([Describe("", 0), Describe("", 0)]);
        Assert.Equal(2, plan.Binding.ResolveServerParameter(0, "", 2).Value);
        Assert.Equal(3, plan.Binding.ResolveServerParameter(1, "", 2).Value);
    }

    [Fact]
    public void MultipleReturnParametersReject()
    {
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("r1", DmDbType.Int32, ParameterDirection.ReturnValue));
        parameters.Add(new DmParameter("r2", DmDbType.Int32, ParameterDirection.ReturnValue));
        Assert.Throws<InvalidOperationException>(() => Capture(parameters));
    }

    [Fact]
    public void OutputSnapshotIgnoresOldReaderAndClrInference()
    {
        using var oldReader = new DataTable().CreateDataReader();
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("reader", oldReader) { Direction = ParameterDirection.Output });
        parameters.Add(new DmParameter("number", 17) { Direction = ParameterDirection.ReturnValue });
        using var plan = Capture(parameters);
        Assert.Same(DBNull.Value, plan.Parameters.do_GetParameter(0).Value);
        Assert.Same(DBNull.Value, plan.Parameters.do_GetParameter(1).Value);
        Assert.Equal(DmParameterTypeSource.Unresolved, plan.Parameters.do_GetParameter(0).TypeSource);
        Assert.Equal(DmParameterTypeSource.Unresolved, plan.Parameters.do_GetParameter(1).TypeSource);
        Assert.Same(oldReader, parameters[0].Value);
    }

    [Fact]
    public void InputOutputCopiesMutableInput()
    {
        byte[] input = [1, 2];
        var parameters = new DmParameterCollection();
        parameters.Add(new DmParameter("p", input) { Direction = ParameterDirection.InputOutput });
        using var plan = Capture(parameters);
        input[0] = 9;
        Assert.Equal(new byte[] { 1, 2 }, plan.Parameters.do_GetParameter(0).Value);
    }

    [Fact]
    public void OutputsPublishThroughGateOnlyAfterAllHaveArrived()
    {
        var gate = new DmCommandPlanGate();
        var parameters = Outputs();
        parameters.AttachPlanGate(gate);
        using var plan = gate.Enter(() => Capture(parameters));
        plan.StageOutput(plan.Parameters.do_GetParameter(0), 21);
        Assert.Equal(1, parameters[0].Value);
        Assert.Throws<InvalidOperationException>(() => plan.PublishOutputs());
        Assert.Throws<InvalidOperationException>(() => parameters[0].Value = 100);
        plan.StageOutput(plan.Parameters.do_GetParameter(1), 22);
        plan.PublishOutputs();
        Assert.Equal(21, parameters[0].Value);
        Assert.Equal(22, parameters[1].Value);
        Assert.True(gate.IsExecuting);
        plan.FailOutputs();
        Assert.Equal(21, parameters[0].Value);
    }

    [Fact]
    public void FailureAndDuplicateOutputsNeverPublishPartialValues()
    {
        var parameters = Outputs();
        using var plan = Capture(parameters);
        plan.StageOutput(plan.Parameters.do_GetParameter(0), 21);
        Assert.Throws<InvalidOperationException>(() => plan.StageOutput(plan.Parameters.do_GetParameter(0), 22));
        Assert.Throws<InvalidOperationException>(() => plan.StageOutput(parameters.do_GetParameter(1), 22));
        plan.FailOutputs();
        Assert.Throws<InvalidOperationException>(() => plan.PublishOutputs());
        Assert.Throws<InvalidOperationException>(() => plan.StageOutput(plan.Parameters.do_GetParameter(1), 22));
        Assert.Equal(1, parameters[0].Value);
        Assert.Equal(2, parameters[1].Value);
    }

    [Fact]
    public void PublicationUsesCapturedIdentityEvenIfDetachedCollectionChanges()
    {
        var parameters = Outputs();
        var original = parameters[0];
        using var plan = Capture(parameters);
        parameters[0] = new DmParameter("a", 99);
        plan.StageOutput(plan.Parameters.do_GetParameter(0), 21);
        plan.StageOutput(plan.Parameters.do_GetParameter(1), 22);
        plan.PublishOutputs();
        Assert.Equal(21, original.Value);
        Assert.Equal(99, parameters[0].Value);
    }

    [Fact]
    public void CursorPublicationCanBeRevokedWithoutPublishingScalars()
    {
        using var reader = new DataTable().CreateDataReader();
        var parameters = Outputs();
        parameters.do_GetParameter(0).DmSqlType = DmDbType.RefCursor;
        using var plan = Capture(parameters);
        Assert.True(plan.HasOutputParameters);
        Assert.True(plan.HasCursorParameters);
        plan.StageOutput(plan.Parameters.do_GetParameter(0), reader);
        plan.StageOutput(plan.Parameters.do_GetParameter(1), 22);
        plan.PublishCursorOutputs();
        Assert.Same(reader, parameters[0].Value);
        Assert.Equal(2, parameters[1].Value);
        plan.FailOutputs();
        Assert.Equal(1, parameters[0].Value);
        Assert.Equal(2, parameters[1].Value);
        Assert.False(reader.IsClosed); // Reader/session owner performs physical cleanup.
    }

    private static DmParameterCollection Outputs()
    {
        var result = new DmParameterCollection();
        result.Add(new DmParameter("a", DmDbType.Int32, ParameterDirection.Output) { Value = 1 });
        result.Add(new DmParameter("b", DmDbType.Int32, ParameterDirection.InputOutput) { Value = 2 });
        return result;
    }

    private static DmCommandPlan Capture(DmParameterCollection parameters, string name = "worker") =>
        DmCommandPlan.Capture(name, CommandType.StoredProcedure, 30, null, null, parameters);

    private static DmParameterInternal Describe(string name, short direction)
    {
        var result = (DmParameterInternal)RuntimeHelpers.GetUninitializedObject(typeof(DmParameterInternal));
        result.name = name;
        result.SetCType(7);
        result.SetInOutType(direction);
        result.SetTypeFlag(1);
        return result;
    }
}
