using System.Buffers.Binary;
using System.Reflection;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.LobTests;

[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLob")]
public sealed class TruncateWidthTests
{
    public static IEnumerable<object[]> SupportedLengths()
    {
        foreach (bool clob in new[] { false, true })
        foreach (bool longLob in new[] { false, true })
        {
            foreach (long length in new[] { (long)int.MaxValue - 1, int.MaxValue, (long)int.MaxValue + 1, (long)uint.MaxValue - 1, uint.MaxValue })
                yield return [clob, longLob, length];
            if (longLob)
                foreach (long length in new[] { (long)uint.MaxValue + 1, (long)uint.MaxValue + 2, long.MaxValue })
                    yield return [clob, longLob, length];
        }
    }

    [Theory]
    [MemberData(nameof(SupportedLengths))]
    public async Task TruncatePreservesNegotiatedLengthInTheActualRequest(bool clob, bool longLob, long length)
    {
        await using var fixture = new OutputLobFixture(newLobProfile: true);
        fixture.Instance.ConnProperty.LongLobFlag = longLob;
        AbstractLob lob = CreateLob(fixture, clob);
        lob.m_length = lob.bytesLength = long.MaxValue; // Virtual remote length; no large allocation.
        var capture = CaptureRequests(fixture);
        AddTruncateReply(fixture, longLob, length, 77);
        fixture.ReleaseAll();

        Truncate(lob, length);

        AssertRequestLength(Assert.Single(capture.Requests), longLob, length);
        Assert.Equal(new short[] { 31 }, fixture.Channel.Commands);
        Assert.Equal(length, lob.m_length);
        Assert.Equal(length, lob.bytesLength);
        Assert.Equal(77, lob.id);
        Assert.Equal(lob.fileId, lob.curFileId);
        Assert.Equal(lob.pageNo, lob.curPageNo);
        Assert.Equal(0, lob.curOffset);
        Assert.Equal(0, lob.totalOffset);
    }

    [Theory]
    [InlineData(false, 4294967296L)]
    [InlineData(true, 4294967296L)]
    [InlineData(false, long.MaxValue)]
    [InlineData(true, long.MaxValue)]
    public async Task LegacyOverflowRejectsBeforeSendAndAllowsSubsequentTruncate(bool clob, long length)
    {
        await using var fixture = new OutputLobFixture(newLobProfile: true);
        fixture.Instance.ConnProperty.LongLobFlag = false;
        AbstractLob lob = CreateLob(fixture, clob);
        var before = Snapshot(lob);
        var capture = CaptureRequests(fixture);

        Assert.Throws<OverflowException>(() => Truncate(lob, length));

        Assert.Empty(capture.Requests);
        Assert.Empty(fixture.Channel.Commands);
        Assert.Equal(before, Snapshot(lob));
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        AddTruncateReply(fixture, false, 3, 77);
        fixture.ReleaseAll();
        Truncate(lob, 3);
        AssertRequestLength(Assert.Single(capture.Requests), false, 3);
        Assert.Equal(3, lob.m_length);
        Assert.Equal(77, lob.id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NegativeAndReadOnlyRequestsPreserveStateWithoutSending(bool clob, bool longLob)
    {
        await using var fixture = new OutputLobFixture(newLobProfile: true);
        fixture.Instance.ConnProperty.LongLobFlag = longLob;
        AbstractLob lob = CreateLob(fixture, clob);
        var before = Snapshot(lob);
        Assert.Throws<DmException>(() => Truncate(lob, -1));
        lob.updateable = false;
        Assert.Throws<DmException>(() => Truncate(lob, 3));
        Assert.Empty(fixture.Channel.Commands);
        Assert.Equal(before, Snapshot(lob));
        lob.updateable = true;
        AddTruncateReply(fixture, longLob, 3, 77);
        fixture.ReleaseAll();
        Truncate(lob, 3);
        Assert.Equal(new short[] { 31 }, fixture.Channel.Commands);
        Assert.Equal(3, lob.m_length);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IncompleteAcknowledgementDoesNotPublishPartialLocatorState(bool clob, bool longLob)
    {
        await using var fixture = new OutputLobFixture(newLobProfile: true);
        fixture.Instance.ConnProperty.LongLobFlag = longLob;
        AbstractLob lob = CreateLob(fixture, clob);
        var before = Snapshot(lob);
        byte[] body = new byte[longLob ? 8 : 4];
        if (longLob) BinaryPrimitives.WriteInt64LittleEndian(body, 3);
        else BinaryPrimitives.WriteUInt32LittleEndian(body, 3);
        fixture.Channel.AddRawReply(31, body);
        fixture.ReleaseAll();

        Assert.Throws<InvalidDataException>(() => Truncate(lob, 3));

        Assert.Equal(new short[] { 31 }, fixture.Channel.Commands);
        Assert.Equal(before, Snapshot(lob));
    }

    private static AbstractLob CreateLob(OutputLobFixture fixture, bool clob) => clob ? fixture.Clob(5) : fixture.Blob(5);
    private static void Truncate(AbstractLob lob, long length)
    {
        if (lob is DmBlob blob) blob.Truncate(length);
        else ((DmClob)lob).Truncate(length);
    }
    private static (long, long, long, int, int, int, long) Snapshot(AbstractLob lob) =>
        (lob.m_length, lob.bytesLength, lob.id, lob.curFileId, lob.curPageNo, lob.curOffset, lob.totalOffset);

    private static void AddTruncateReply(OutputLobFixture fixture, bool longLob, long length, long id)
    {
        int lengthBytes = longLob ? 8 : 4;
        byte[] body = new byte[lengthBytes + 8];
        if (longLob) BinaryPrimitives.WriteInt64LittleEndian(body, length);
        else BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)length));
        BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(lengthBytes), id);
        fixture.Channel.AddRawReply(31, body);
    }

    private static void AssertRequestLength(byte[] request, bool longLob, long length)
    {
        Assert.Equal(31, BinaryPrimitives.ReadInt16LittleEndian(request.AsSpan(4)));
        // 64-byte frame header, then flag/id/group/file/page/table/column and 12-byte row id.
        const int lengthOffset = 64 + 1 + 8 + 2 + 2 + 4 + 4 + 2 + 12;
        Assert.Equal(lengthOffset + (longLob ? 8 : 12), request.Length);
        if (longLob) Assert.Equal(length, BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(lengthOffset)));
        else
        {
            Assert.Equal(length, (long)BinaryPrimitives.ReadUInt32LittleEndian(request.AsSpan(lengthOffset)));
            Assert.All(request[(lengthOffset + 4)..], value => Assert.Equal(255, value));
        }
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
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds) => inner.Receive(buffer, offset, count, timeoutMilliseconds);
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReceiveAsync(buffer, offset, count, cancellationToken);
        public void Dispose() => inner.Dispose();
    }
}
