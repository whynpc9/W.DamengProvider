using W.Dm;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class LegacyBlobStreamContractTests
{
    [Fact]
    public void PositionAndAllSeekOriginsUseZeroBasedOffsets()
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([10, 20, 30, 40], connection);
        using var stream = blob.GetStream();
        Assert.Equal(0, stream.Position);
        Assert.Equal(10, stream.ReadByte());
        stream.Position = 2;
        Assert.Equal(30, stream.ReadByte());
        Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
        Assert.Equal(10, stream.ReadByte());
        Assert.Equal(2, stream.Seek(1, SeekOrigin.Current));
        Assert.Equal(30, stream.ReadByte());
        Assert.Equal(3, stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(40, stream.ReadByte());
        Assert.Equal(4, stream.Seek(0, SeekOrigin.End));
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(6, stream.Seek(2, SeekOrigin.End));
        Assert.Equal(0, stream.Read(new byte[2], 0, 2));
        Assert.Equal(6, stream.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EofAndZeroCountReadsReturnTheStreamSentinels(bool empty)
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal(empty ? [] : [7, 8], connection);
        using var stream = blob.GetStream();
        byte[] destination = [99, 99, 99, 99];
        Assert.Equal(0, stream.Read(destination, destination.Length, 0));
        Assert.Equal(0, stream.Position);
        int expected = empty ? 0 : 2;
        Assert.Equal(expected, stream.Read(destination, 1, 3));
        Assert.Equal(empty ? new byte[] { 99, 99, 99, 99 } : [99, 7, 8, 99], destination);
        Assert.Equal(expected, stream.Position);
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(0, stream.Read(destination, 0, destination.Length));
        Assert.Equal(expected, stream.Position);
        stream.Position = long.MaxValue;
        Assert.Equal(-1, stream.ReadByte());
        Assert.Equal(long.MaxValue, stream.Position);
    }

    [Fact]
    public void InvalidPositionsAndOverflowDoNotMoveOrMutateTheStream()
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([1, 2, 3], connection);
        using var stream = blob.GetStream();
        stream.Position = 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
        Assert.Throws<IOException>(() => stream.Seek(-2, SeekOrigin.Current));
        Assert.Throws<IOException>(() => stream.Seek(long.MaxValue, SeekOrigin.End));
        Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)99));
        Assert.Equal(1, stream.Position);
        stream.Position = long.MaxValue;
        Assert.Throws<IOException>(() => stream.Seek(1, SeekOrigin.Current));
        Assert.Throws<IOException>(() => stream.Write([4], 0, 1));
        Assert.Equal(long.MaxValue, stream.Position);
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.GetBytes(0, 3));
    }

    [Fact]
    public void InvalidBuffersNeverConsumeOrOverwriteData()
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([1, 2, 3], connection);
        using var stream = blob.GetStream();
        stream.Position = 1;
        foreach (bool write in new[] { false, true })
        {
            Assert.Throws<ArgumentNullException>(() => Transfer(stream, write, null!, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Transfer(stream, write, [9], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Transfer(stream, write, [9], 0, -1));
            Assert.Throws<ArgumentException>(() => Transfer(stream, write, [9], 2, 0));
            Assert.Throws<ArgumentException>(() => Transfer(stream, write, [9], 1, int.MaxValue));
            Assert.Throws<ArgumentException>(() => Transfer(stream, write, [9], int.MaxValue, int.MaxValue));
            Assert.Equal(1, stream.Position);
        }
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.GetBytes(0, 3));
        Assert.Equal(2, stream.ReadByte());
    }

    [Fact]
    public async Task InvalidArgumentsAreRejectedBeforeRemoteLobIo()
    {
        await using var fixture = new OutputLobFixture();
        var blob = fixture.Blob(5);
        blob.m_length = -1;
        using var stream = blob.GetStream();
        Assert.Throws<ArgumentException>(() => stream.Read([0], 1, 1));
        Assert.Throws<ArgumentException>(() => stream.Write([0], 1, 1));
        Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.SetLength(-1));
        stream.Position = long.MaxValue;
        Assert.Throws<IOException>(() => stream.Write([0], 0, 1));
        Assert.Empty(fixture.Channel.Commands);
        Assert.Equal(long.MaxValue, stream.Position);
        Assert.Equal(-1, blob.m_length);
    }

    [Fact]
    public void OverwriteAppendAndTruncateChangeDataAndPosition()
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([1, 2, 3], connection);
        using var stream = blob.GetStream();
        stream.Position = 1;
        stream.Write([99, 8, 9, 99], 1, 2);
        Assert.Equal(3, stream.Position);
        Assert.Equal(new byte[] { 1, 8, 9 }, blob.GetBytes(0, 3));
        stream.Write([4, 5], 0, 2);
        Assert.Equal(5, stream.Length);
        Assert.Equal(5, stream.Position);
        stream.Flush();
        Assert.Equal(new byte[] { 1, 8, 9, 4, 5 }, blob.GetBytes(0, 5));
        stream.SetLength(2);
        Assert.Equal(2, stream.Length);
        Assert.Equal(2, stream.Position);
        Assert.Equal(new byte[] { 1, 8 }, blob.GetBytes(0, 2));
        stream.WriteByte(6);
        Assert.Equal(3, stream.Position);
        Assert.Equal(new byte[] { 1, 8, 6 }, blob.GetBytes(0, 3));
        stream.SetLength(0);
        Assert.Equal(0, stream.Position);
        Assert.Equal(0, stream.Length);
        Assert.Equal(-1, stream.ReadByte());
        stream.WriteByte(7);
        Assert.Equal(new byte[] { 7 }, blob.GetBytes(0, 1));
    }

    [Fact]
    public void UnsupportedExtensionsAndGapWritesLeaveDataUnchanged()
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([1, 2, 3], connection);
        using var stream = blob.GetStream();
        stream.Position = 1;
        Assert.Throws<NotSupportedException>(() => stream.SetLength(4));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(long.MaxValue));
        Assert.Equal(1, stream.Position);
        stream.Position = (long)int.MaxValue + 1;
        Assert.Throws<NotSupportedException>(() => stream.Write([9], 0, 1));
        Assert.Equal((long)int.MaxValue + 1, stream.Position);
        Assert.Equal(3, stream.Length);
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.GetBytes(0, 3));
        stream.Position = 3;
        stream.Write([9], 1, 0);
        Assert.Equal(3, stream.Length);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void ReadOnlyCapabilitiesRejectWritesAndStillAllowReadsAndFlush()
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([1, 2], connection);
        using var stream = blob.GetStream();
        Assert.True(stream.CanWrite);
        blob.updateable = false;
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Write([9], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Equal(0, stream.Position);
        stream.Flush();
        Assert.Equal(1, stream.ReadByte());
        Assert.Equal(new byte[] { 1, 2 }, blob.GetBytes(0, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeAndCloseAreIdempotentAndLeaveTheUnderlyingLobUsable(bool closeFirst)
    {
        using var connection = new DmConnection();
        var blob = DmBlob.newInstanceOfLocal([1, 2], connection);
        var stream = Assert.IsType<DmBLobStream>(blob.GetStream());
        Assert.False(stream.StreamCheck());
        if (closeFirst) stream.Close(); else stream.Dispose();
        stream.Close();
        stream.Dispose();
        Assert.True(stream.StreamCheck());
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => { _ = stream.Length; });
        Assert.Throws<ObjectDisposedException>(() => { _ = stream.Position; });
        Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => stream.Read([0], 0, 0));
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => stream.Write([0], 0, 0));
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => stream.SetLength(0));
        Assert.Throws<ObjectDisposedException>(() => stream.Flush());
        Assert.Equal(new byte[] { 1, 2 }, blob.GetBytes(0, 2));
        using var second = blob.GetStream();
        Assert.Equal(1, second.ReadByte());
    }

    [Fact]
    public void TimeoutsAreUnsupportedAndConstructorRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new DmBLobStream(null!));
        using var connection = new DmConnection();
        using var stream = DmBlob.newInstanceOfLocal([], connection).GetStream();
        Assert.False(stream.CanTimeout);
        Assert.Throws<InvalidOperationException>(() => { _ = stream.ReadTimeout; });
        Assert.Throws<InvalidOperationException>(() => stream.ReadTimeout = 100);
        Assert.Throws<InvalidOperationException>(() => { _ = stream.WriteTimeout; });
        Assert.Throws<InvalidOperationException>(() => stream.WriteTimeout = 100);
    }

    private static void Transfer(Stream stream, bool write, byte[] buffer, int offset, int count)
    {
        if (write) stream.Write(buffer, offset, count);
        else stream.Read(buffer, offset, count);
    }
}
