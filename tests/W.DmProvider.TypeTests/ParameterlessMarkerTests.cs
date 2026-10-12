using System;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class ParameterlessMarkerTests
{
    [Theory]
    [InlineData("CREATE TRIGGER \"T\" BEFORE UPDATE ON \"X\" FOR EACH ROW BEGIN :NEW.\"VERSION\" := :OLD.\"VERSION\" + 1; END;")]
    [InlineData("SELECT :X FROM DUAL")]
    [InlineData("SELECT ? FROM DUAL")]
    [InlineData("SELECT :A, ? FROM DUAL")]
    public void CommandWithoutParametersIsNotBoundAgainstMarkerText(string sql)
    {
        using var command = new DmCommand();
        var binding = DmParameterBinding.Create(sql, command.Parameters);
        Assert.Equal(0, binding.MarkerCount);
        // A server that unexpectedly describes parameters still fails closed.
        Assert.Throws<InvalidOperationException>(() => binding.ValidateServerCount(1));
        binding.ValidateServerCount(0);
    }

    [Fact]
    public void ParametersStillRequireExactMarkerMatches()
    {
        using var command = new DmCommand();
        command.Parameters.Add(new DmParameter { ParameterName = "value", Value = 1 });
        Assert.Throws<InvalidOperationException>(() =>
            DmParameterBinding.Create("BEGIN :NEW.V := :value; END;", command.Parameters));
        Assert.Throws<InvalidOperationException>(() =>
            DmParameterBinding.Create("SELECT 1 FROM DUAL", command.Parameters));
        Assert.Throws<NotSupportedException>(() =>
            DmParameterBinding.Create("SELECT :value, ? FROM DUAL", command.Parameters));
        Assert.Equal(1, DmParameterBinding.Create("SELECT :value FROM DUAL", command.Parameters).MarkerCount);
    }

    [Fact]
    public void ParameterlessCommandStillRejectsUnterminatedLexemes()
    {
        using var command = new DmCommand();
        Assert.Throws<System.IO.InvalidDataException>(() =>
            DmParameterBinding.Create("SELECT 'unterminated", command.Parameters));
    }
}
