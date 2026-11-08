using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using W.Dm;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class ClobWriteBoundaryTests
{
    private const int DefaultChunkSize = 32000;
    private const short SetLobData = 30;
    private const int ClobType = 19;

    [Fact]
    public async Task DefaultUtf8ChunkSizeSendsTheFinalThreeByteCharacter()
    {
        string input = new('界', 21333);
        await VerifyWrite(input, input, "UTF-8", DefaultChunkSize, [31998, 31998, 3]);
    }

    [Theory]
    [InlineData(0, new[] { 0 })]
    [InlineData(7, new[] { 7 })]
    [InlineData(DefaultChunkSize, new[] { DefaultChunkSize, 0 })]
    [InlineData(2 * DefaultChunkSize, new[] { DefaultChunkSize, DefaultChunkSize, 0 })]
    public async Task PreservesEmptyAndAsciiTerminatorFrames(int length, int[] frameLengths)
    {
        string input = new('a', length);
        await VerifyWrite(input, input, "UTF-8", DefaultChunkSize, frameLengths);
    }

    [Theory]
    [InlineData("UTF-8", "界🙂界🙂", 6, new[] { 3, 4, 3, 4 })]
    [InlineData("GB18030", "中𠀀中𠀀", 5, new[] { 2, 4, 2, 4 })]
    public async Task SliceOverloadWritesOnlyTheRequestedTextAcrossRepeatedBackoffs(
        string encoding, string slice, int chunkSize, int[] frameLengths)
    {
        await VerifyWrite("prefix" + slice + "suffix", slice, encoding, chunkSize, frameLengths, offset: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveAckStopsWithoutSendingTheRemainingText(int stopAck)
    {
        await using var fixture = CreateFixture("UTF-8", 6);
        var reader = fixture.Reader(ClobType);
        var clob = reader.GetClob(0);
        var capture = CaptureRequests(fixture);
        AddWriteReplies(fixture, [9, stopAck, 5]);

        Assert.Equal(9, clob.SetString(2, "界🙂界🙂"));

        Assert.Equal(new[] { SetLobData, SetLobData }, fixture.Channel.Commands);
        var frames = capture.Requests.Select(DecodeFrame).ToArray();
        Assert.Equal(new byte[] { 1, 0 }, frames.Select(frame => frame.Flags));
        Assert.Equal(new long[] { 2, 11 }, frames.Select(frame => frame.Position));
        Assert.Equal(Encoding.UTF8.GetBytes("界🙂"), frames.SelectMany(frame => frame.Payload).ToArray());
    }

    [Fact]
    public async Task ReadOnlyClobRejectsBothOverloadsBeforeSending()
    {
        await using var fixture = CreateFixture("UTF-8", DefaultChunkSize);
        var reader = fixture.Reader(ClobType);
        var clob = reader.GetClob(0);
        clob.updateable = false;

        Assert.Throws<DmException>(() => clob.SetString(0, "界"));
        Assert.Throws<DmException>(() => clob.SetString(0, "a界", 1, 1));
        Assert.Empty(fixture.Channel.Commands);
    }

    [Fact]
    public async Task RejectsAChunkTooSmallForTheFirstCharacterBeforeSending()
    {
        await using var fixture = CreateFixture("UTF-8", 2);
        var reader = fixture.Reader(ClobType);
        var clob = reader.GetClob(0);

        var error = Assert.Throws<Exception>(() => clob.SetString(0, "界"));
        Assert.Equal("invalid Object Blob Data", error.Message);
        Assert.Empty(fixture.Channel.Commands);
    }

    private static async Task VerifyWrite(
        string input, string expectedText, string encodingName, int chunkSize, int[] frameLengths, int? offset = null)
    {
        await using var fixture = CreateFixture(encodingName, chunkSize);
        var reader = fixture.Reader(ClobType);
        var clob = reader.GetClob(0);
        var capture = CaptureRequests(fixture);
        var encoding = Encoding.GetEncoding(encodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        byte[] expectedBytes = encoding.GetBytes(expectedText);
        byte[] originalBytes = encoding.GetBytes(input);
        // Synthetic server units deliberately differ from byte counts and CLR string lengths.
        int[] acknowledgements = frameLengths.Select((length, index) => length == 0 ? 0 : 17 + index).ToArray();
        AddWriteReplies(fixture, acknowledgements);

        const long start = 3;
        int result = offset.HasValue
            ? clob.SetString(start, input, offset.Value, expectedText.Length)
            : clob.SetString(start, input);

        var frames = capture.Requests.Select(DecodeFrame).ToArray();
        Assert.Equal(expectedBytes.Length, frames.Sum(frame => frame.Payload.Length));
        Assert.Equal(expectedBytes, frames.SelectMany(frame => frame.Payload).ToArray());
        Assert.Equal(frameLengths, frames.Select(frame => frame.Payload.Length));
        Assert.Equal(frameLengths.Length, fixture.Channel.Commands.Count);
        Assert.All(fixture.Channel.Commands, command => Assert.Equal(SetLobData, command));
        Assert.Equal(expectedText, string.Concat(frames.Select(frame => encoding.GetString(frame.Payload))));
        Assert.Equal(originalBytes, encoding.GetBytes(input));

        long position = start;
        for (int index = 0; index < frames.Length; index++)
        {
            byte flags = (byte)((index == 0 ? 1 : 0) | (index == frames.Length - 1 ? 2 : 0));
            Assert.Equal(flags, frames[index].Flags);
            Assert.Equal(position, frames[index].Position);
            position += acknowledgements[index];
        }
        Assert.Equal(acknowledgements.Sum(), result);
    }

    private static OutputLobFixture CreateFixture(string encoding, int chunkSize)
    {
        var fixture = new OutputLobFixture(newLobProfile: true);
        fixture.Instance.ConnProperty.ServerEncoding = encoding;
        fixture.Instance.ConnProperty.property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG] = chunkSize;
        return fixture;
    }

    private static void AddWriteReplies(OutputLobFixture fixture, int[] acknowledgements)
    {
        foreach (int units in acknowledgements)
        {
            // Remaining bytes encode a zeroed remote locator (group is 0, not -1).
            byte[] body = new byte[30];
            BinaryPrimitives.WriteInt32LittleEndian(body, units);
            fixture.Channel.AddRawReply(SetLobData, body);
        }
        fixture.ReleaseAll();
    }

    private static (byte Flags, long Position, byte[] Payload) DecodeFrame(byte[] frame)
    {
        // These offsets apply to this fixture's long LOB with a 12-byte row identifier.
        const int payloadOffset = 126;
        int length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(122));
        Assert.Equal(payloadOffset + length, frame.Length);
        return (frame[65], BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(114)), frame[payloadOffset..]);
    }

    private static RecordingChannel CaptureRequests(OutputLobFixture fixture)
    {
        var legacyTransport = fixture.Instance.GetCsi().A();
        var transport = (DmTransport)legacyTransport.GetType().GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(legacyTransport)!;
        var capture = new RecordingChannel(fixture.Channel);
        OutputLobFixture.SetField(transport, "channel", capture);
        return capture;
    }

    private sealed class RecordingChannel(IDmByteChannel inner) : IDmByteChannel
    {
        internal List<byte[]> Requests { get; } = [];

        public bool IsClosed => inner.IsClosed;

        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            Requests.Add(buffer.AsSpan(offset, count).ToArray());
            return inner.Send(buffer, offset, count, timeoutMilliseconds);
        }

        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Requests.Add(buffer.AsSpan(offset, count).ToArray());
            return inner.SendAsync(buffer, offset, count, cancellationToken);
        }

        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) =>
            inner.Receive(buffer, offset, count, timeoutMilliseconds);

        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReceiveAsync(buffer, offset, count, cancellationToken);

        public void Dispose() => inner.Dispose();
    }
}
