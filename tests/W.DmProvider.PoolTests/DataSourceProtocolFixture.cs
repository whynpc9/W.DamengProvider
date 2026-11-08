using System.Buffers.Binary;
using W.Dm.Internal.Transport;
using Xunit;

namespace W.DmProvider.PoolTests;

// Fixed allocation, one-column SELECT, and close replies traverse the real
// statement/protocol paths. I/O and physical-close barriers are independently released.
internal sealed class ShortcutWireChannel(bool asynchronous) : IDmByteChannel
{
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private byte[] reply = [];
    private int position;
    private short opcode;
    internal readonly List<short> Opcodes = [];
    internal readonly TaskCompletionSource ReceiveEntered = NewSignal();
    internal readonly TaskCompletionSource ReleaseReply = NewSignal();
    internal readonly TaskCompletionSource CloseEntered = NewSignal();
    internal readonly TaskCompletionSource PhysicalClosed = NewSignal();
    internal readonly TaskCompletionSource ReleasePhysicalClose = NewSignal();
    internal bool HoldQueryReply, PausePhysicalClose;
    internal int SyncCalls, AsyncCalls, DisposeCalls;
    internal Action? BeforeDispose;
    public bool IsClosed { get; private set; }
    public int Send(byte[] buffer, int offset, int count, int timeout)
    { SyncCalls++; Assert.False(asynchronous); return SendCore(buffer, offset, count); }
    public int Receive(byte[] buffer, int offset, int count, int timeout)
    {
        SyncCalls++; Assert.False(asynchronous);
        if (HoldQueryReply && (opcode is 5 or 91))
        { ReceiveEntered.TrySetResult(); Assert.True(ReleaseReply.Task.Wait(TimeSpan.FromSeconds(15))); }
        return ReceiveCore(buffer, offset, count);
    }
    public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
    { AsyncCalls++; Assert.True(asynchronous); token.ThrowIfCancellationRequested(); return ValueTask.FromResult(SendCore(buffer, offset, count)); }
    public async ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        AsyncCalls++; Assert.True(asynchronous); token.ThrowIfCancellationRequested();
        if (HoldQueryReply && (opcode is 5 or 91))
        { ReceiveEntered.TrySetResult(); await ReleaseReply.Task.WaitAsync(token); }
        return ReceiveCore(buffer, offset, count);
    }
    private int SendCore(byte[] buffer, int offset, int count)
    {
        opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
        Opcodes.Add(opcode);
        Assert.Contains(opcode, new short[] { 3, 5, 91, 4 });
        // Opcode 5 carries both prepare and execute. Its byte-21 flag
        // distinguishes their different reply header layouts.
        bool query = opcode == 91 || opcode == 5 && buffer[offset + 21] == 1;
        reply = new byte[query ? 64 + 32 + 5 : 64];
        BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(6), reply.Length - 64);
        if (opcode == 3) BinaryPrimitives.WriteInt32LittleEndian(reply, 41);
        if (opcode is 5 or 91) BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(20), 160);
        if (query)
        {
            BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(22), 1);
            // Independent 32-byte plain column descriptor: INT32(7),
            // precision 4, nullable, no LOB flag, and five-byte ASCII name.
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(64), 7);
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(68), 4);
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(76), 1);
            BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(88), 5);
            new byte[] { 86, 65, 76, 85, 69 }.CopyTo(reply, 96);
        }
        for (int index = 0; index < 19; index++) reply[19] ^= reply[index];
        position = 0; return count;
    }
    private int ReceiveCore(byte[] buffer, int offset, int count)
    {
        if (IsClosed) return 0;
        int read = Math.Min(count, reply.Length - position);
        reply.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
        position += read; return read;
    }
    public void Dispose()
    {
        Interlocked.Increment(ref DisposeCalls);
        BeforeDispose?.Invoke();
        CloseEntered.TrySetResult();
        if (PausePhysicalClose) Assert.True(ReleasePhysicalClose.Task.Wait(TimeSpan.FromSeconds(15)));
        IsClosed = true;
        ReleaseReply.TrySetResult();
        PhysicalClosed.TrySetResult();
    }
    internal void AssertIoPath()
    {
        Assert.True(asynchronous ? AsyncCalls > 0 : SyncCalls > 0);
        Assert.Equal(0, asynchronous ? SyncCalls : AsyncCalls);
    }
}
