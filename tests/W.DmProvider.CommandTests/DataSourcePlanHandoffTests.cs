using System.Data;
using W.Dm;
using W.Dm.Internal.Execution;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class DataSourcePlanHandoffTests
{
    [Fact]
    public void HandoffConsumesTheSamePlanOnceAndPublishesToOriginalOutputs()
    {
        using var connection = new DmConnection();
        using var command = new DmCommand("worker", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = 11
        };
        var inputOutput = new DmParameter("p", DmDbType.Int32, ParameterDirection.InputOutput) { Value = 1 };
        var output = new DmParameter("q", DmDbType.Int32, ParameterDirection.Output) { Value = 2 };
        command.Parameters.Add(inputOutput);
        command.Parameters.Add(output);
        using var owner = new DmDataSourceExecution(default) { Connection = connection };
        using var unrelated = new DmDataSourceExecution(default);

        command.CaptureDataSourcePlan(owner, arrayBindingAllowed: false, CommandBehavior.Default);
        var captured = command.ActivePlan;
        Assert.Same(connection, captured.Connection);
        Assert.Equal(11, captured.TimeoutSeconds);
        Assert.Equal("\"WORKER\"(?,?)", captured.WireSql);
        Assert.Equal(1, captured.Parameters[0].Value);
        Assert.Throws<InvalidOperationException>(() => command.TakeDataSourcePlan(unrelated));

        var handedOff = command.TakeDataSourcePlan(owner);
        try
        {
            Assert.Same(captured, handedOff);
            Assert.Throws<InvalidOperationException>(() => command.TakeDataSourcePlan(owner));
            handedOff.StageOutput(handedOff.Parameters.do_GetParameter(0), 10);
            Assert.Throws<InvalidOperationException>(handedOff.PublishOutputs);
            Assert.Equal(1, inputOutput.Value);
            Assert.Equal(2, output.Value);
            handedOff.StageOutput(handedOff.Parameters.do_GetParameter(1), 20);
            handedOff.PublishOutputs();
            Assert.Same(inputOutput, command.Parameters[0]);
            Assert.Same(output, command.Parameters[1]);
            Assert.Equal(10, inputOutput.Value);
            Assert.Equal(20, output.Value);
        }
        finally { command.ReleaseBatchPlan(handedOff); }
    }

    [Fact]
    public void LocalCaptureDoesNotRequireNegotiationButHandoffRejectsMissingProfile()
    {
        using var connection = new DmConnection();
        using var command = new DmCommand("INSERT INTO SYNTHETIC_TABLE VALUES (:p)", connection);
        using var stream = new MemoryStream([1, 2, 3]);
        var parameter = new DmParameter("p", DmDbType.Blob) { Value = stream };
        command.Parameters.Add(parameter);
        using var owner = new DmDataSourceExecution(default) { Connection = connection };
        command.CaptureDataSourcePlan(owner, arrayBindingAllowed: true, CommandBehavior.Default);
        Assert.Equal(0, stream.Position);
        Assert.Throws<InvalidOperationException>(() => parameter.Value = new byte[] { 7 });
        Assert.Throws<NotSupportedException>(() => command.TakeDataSourcePlan(owner));
        Assert.Null(command.ActivePlan);
        Assert.Equal(0, stream.Position);
        Assert.True(stream.CanRead);
        Assert.Same(stream, parameter.Value);
        parameter.Value = new byte[] { 7 };
        Assert.Throws<InvalidOperationException>(() => command.TakeDataSourcePlan(owner));
    }
}
