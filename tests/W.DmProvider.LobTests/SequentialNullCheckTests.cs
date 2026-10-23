using System.Data;
using System.Text;
using W.Dm;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class SequentialNullCheckTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NullCheckLeavesWholeLobAvailableToTypedGetter(bool text, bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.NewLobFlag = true;
        byte[] payload = text ? Encoding.UTF8.GetBytes("A🙂中Z") : [1, 2, 3, 4];
        var reader = fixture.Reader(text ? 19 : 12, OutputLobFixture.Locator(payload.Length, payload), CommandBehavior.SequentialAccess);
        Assert.False(await Probe(reader, 0, asynchronous));
        if (text)
            Assert.Equal("A🙂中Z", asynchronous ? await reader.GetFieldValueAsync<string>(0) : reader.GetFieldValue<string>(0));
        else
            Assert.Equal(payload, asynchronous ? await reader.GetFieldValueAsync<byte[]>(0) : reader.GetFieldValue<byte[]>(0));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NullCheckBeforeAndDuringLobReadPreservesTheCursor(bool text, bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.NewLobFlag = true;
        byte[] payload = text ? Encoding.UTF8.GetBytes("A🙂中Z") : [1, 2, 3, 4];
        var reader = fixture.Reader(text ? 19 : 12, OutputLobFixture.Locator(payload.Length, payload), CommandBehavior.SequentialAccess);
        Assert.False(await Probe(reader, 0, asynchronous));
        Assert.False(await Probe(reader, 0, !asynchronous));
        if (text)
        {
            using var stream = reader.GetTextReader(0);
            Assert.Equal('A', stream.Read());
            Assert.False(await Probe(reader, 0, asynchronous));
            Assert.False(await Probe(reader, 0, !asynchronous));
            Assert.Equal("🙂中Z", await stream.ReadToEndAsync());
        }
        else
        {
            using var stream = reader.GetStream(0);
            Assert.Equal(1, stream.ReadByte());
            Assert.False(await Probe(reader, 0, asynchronous));
            Assert.False(await Probe(reader, 0, !asynchronous));
            using var remaining = new MemoryStream();
            await stream.CopyToAsync(remaining);
            Assert.Equal(new byte[] { 2, 3, 4 }, remaining.ToArray());
        }
        Assert.Empty(fixture.Channel.Commands);
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(12, true)]
    [InlineData(19, false)]
    [InlineData(19, true)]
    public async Task NullLobRemainsAvailableAfterRepeatedChecks(int type, bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        var reader = OutputSequentialRows.Create(fixture, type, null!, 7, BitConverter.GetBytes(17));
        Assert.True(await Probe(reader, 0, asynchronous));
        Assert.True(await Probe(reader, 0, !asynchronous));
        Assert.Same(DBNull.Value, asynchronous ? await reader.GetFieldValueAsync<object>(0) : reader.GetFieldValue<object>(0));
        Assert.Equal(17, reader.GetInt32(1));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterNullCheckInvalidatesEarlierLobAndPreventsBackread(bool asynchronous)
    {
        await using var fixture = new OutputLobFixture();
        fixture.Instance.ConnProperty.NewLobFlag = true;
        var reader = OutputSequentialRows.Create(fixture, 12, OutputLobFixture.Locator(3, [1, 2, 3]), 7, BitConverter.GetBytes(17));
        using var stream = reader.GetStream(0);
        Assert.Equal(1, stream.ReadByte());
        Assert.False(await Probe(reader, 1, asynchronous));
        Assert.ThrowsAny<InvalidOperationException>(() => stream.ReadByte());
        Assert.Equal(6097, Assert.Throws<DmException>(() => reader.IsDBNull(0)).Number);
        Assert.Throws<InvalidOperationException>(() => reader.GetStream(0));
        Assert.Equal(17, reader.GetInt32(1));
        Assert.Empty(fixture.Channel.Commands);
    }

    private static Task<bool> Probe(DmDataReader reader, int ordinal, bool asynchronous) =>
        asynchronous ? reader.IsDBNullAsync(ordinal) : Task.FromResult(reader.IsDBNull(ordinal));
}
