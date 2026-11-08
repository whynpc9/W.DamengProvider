using System.Data;
using W.Dm;
using W.Dm.Internal.Execution;
using Xunit;
using Xunit.Abstractions;

namespace W.DmProvider.CommandTests;

public sealed class ArrayShapePreflightTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("scalar", 0)]
    [InlineData("rank", 0)]
    [InlineData("length", 0)]
    [InlineData("scalar", 1)]
    [InlineData("rank", 1)]
    [InlineData("length", 1)]
    public void InvalidShapeDoesNotAllocatePerRow(string shape, int invalidIndex)
    {
        const int smallCount = 64;
        const int largeCount = 32768;
        using DmCommand small = CreateInvalidCommand(smallCount, shape, invalidIndex);
        using DmCommand large = CreateInvalidCommand(largeCount, shape, invalidIndex);
        Action captureSmall = () => { using var plan = Capture(small); };
        Action captureLarge = () => { using var plan = Capture(large); };
        string invalidName = invalidIndex == 0 ? "A" : "B";

        MeasureInvalidCapture(captureSmall, invalidName);
        MeasureInvalidCapture(captureLarge, invalidName);

        long smallBytes = MeasureInvalidCapture(captureSmall, invalidName);
        long largeBytes = MeasureInvalidCapture(captureLarge, invalidName);
        long growthLimit = largeCount * IntPtr.Size;
        output.WriteLine($"Count {smallCount}: {smallBytes} bytes. Count {largeCount}: {largeBytes} bytes.");
        Assert.True(largeBytes < smallBytes + growthLimit,
            $"Allocation grew by {largeBytes - smallBytes} bytes, expected less than {growthLimit} bytes.");
    }

    [Fact]
    public void LaterInvalidShapeIsReportedBeforeAnEarlierStreamIsCloned()
    {
        using var stream = new MemoryStream();
        using var command = new DmCommand("INSERT INTO T VALUES (?, ?)") { ArrayBindCount = 2 };
        command.Parameters.Add(new DmParameter("BLOB_VALUE", new object[] { stream, DBNull.Value }));
        var invalid = new DmParameter("INVALID", new[] { 1 });
        command.Parameters.Add(invalid);

        var error = Assert.Throws<InvalidOperationException>(() => Capture(command));
        Assert.Contains("'INVALID' requires a one-dimensional array", error.Message);

        invalid.Value = new[] { 1, 2 };
        Assert.Throws<NotSupportedException>(() => Capture(command));
    }

    [Fact]
    public void NonZeroLowerBoundRowsSnapshotArraySlotsAndByteContents()
    {
        byte[] first = [1, 2];
        byte[] second = [3, 4];
        Array values = Array.CreateInstance(typeof(byte[]), [2], [-2]);
        values.SetValue(first, -2);
        values.SetValue(second, -1);
        using var command = new DmCommand("INSERT INTO T VALUES (?)") { ArrayBindCount = 2 };
        command.Parameters.Add(new DmParameter("B", values));
        using var plan = Capture(command);

        values.SetValue(new byte[] { 5 }, -2);
        second[0] = 99;

        var row = plan.RowParameters(plan.Parameters.do_GetParameter(0));
        Assert.Equal(new byte[] { 1, 2 }, Assert.IsType<byte[]>(row(0).Value));
        Assert.Equal(new byte[] { 3, 4 }, Assert.IsType<byte[]>(row(1).Value));
    }

    private static DmCommand CreateInvalidCommand(int count, string shape, int invalidIndex)
    {
        object invalid = shape switch
        {
            "scalar" => 1,
            "rank" => new int[count, 1],
            "length" => new int[count - 1],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var command = new DmCommand("INSERT INTO T VALUES (?, ?)") { ArrayBindCount = count };
        var valid = new int[count];
        command.Parameters.Add(new DmParameter("A", invalidIndex == 0 ? invalid : valid));
        command.Parameters.Add(new DmParameter("B", invalidIndex == 1 ? invalid : valid));
        return command;
    }

    private static long MeasureInvalidCapture(Action capture, string invalidName)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Exception? exception = Record.Exception(capture);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var error = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains($"'{invalidName}' requires a one-dimensional array", error.Message);
        return bytes;
    }

    private static DmCommandPlan Capture(DmCommand command) => DmCommandPlan.Capture(
        command.CommandText, CommandType.Text, 30, null, null, command.Parameters, command.ArrayBindCount);
}
