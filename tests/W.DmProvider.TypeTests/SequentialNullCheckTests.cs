using System.Text;
using W.Dm;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class SequentialNullCheckTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RepeatedNullChecksLeaveScalarAndTextAvailable(bool text, bool isNull, bool asynchronous)
    {
        byte[]? bytes = isNull ? null : text ? Encoding.UTF8.GetBytes("A中🙂Z") : BitConverter.GetBytes(17);
        using var fixture = new ReaderReviewTests.ReaderFixture(text ? 2 : 7, bytes, sequential: true);
        var reader = fixture.Reader;
        Assert.Equal(isNull, await Probe(reader, 0, asynchronous));
        Assert.Equal(isNull, await Probe(reader, 0, !asynchronous));
        if (isNull)
            Assert.Same(DBNull.Value, asynchronous ? await reader.GetFieldValueAsync<object>(0) : reader.GetFieldValue<object>(0));
        else if (text)
            Assert.Equal("A中🙂Z", asynchronous ? await reader.GetFieldValueAsync<string>(0) : reader.GetFieldValue<string>(0));
        else
            Assert.Equal(17, asynchronous ? await reader.GetFieldValueAsync<int>(0) : reader.GetFieldValue<int>(0));
        Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(6097, Assert.Throws<DmException>(() => reader.GetValue(0)).Number);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbingLaterColumnStillForbidsEarlierColumn(bool asynchronous)
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(7, BitConverter.GetBytes(17), sequential: true);
        Assert.False(await Probe(fixture.Reader, 1, asynchronous));
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.IsDBNull(0)).Number);
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetInt32(0)).Number);
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
    }

    [Fact]
    public async Task CanceledAndInvalidNullChecksDoNotConsumeTheCurrentColumn()
    {
        using var fixture = new ReaderReviewTests.ReaderFixture(7, BitConverter.GetBytes(17), sequential: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Reader.IsDBNullAsync(0, cancellation.Token));
        Assert.Throws<DmException>(() => fixture.Reader.IsDBNull(-1));
        await Assert.ThrowsAsync<DmException>(() => fixture.Reader.IsDBNullAsync(2));
        Assert.False(fixture.Reader.IsDBNull(0));
        Assert.Equal(17, fixture.Reader.GetInt32(0));
    }

    private static Task<bool> Probe(DmDataReader reader, int ordinal, bool asynchronous) =>
        asynchronous ? reader.IsDBNullAsync(ordinal) : Task.FromResult(reader.IsDBNull(ordinal));
}
