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
    internal int? AffectedRows, ScalarValue;
    internal int[] ParameterTypes = [];
    internal Action<short, byte[]>? ObserveRequest;
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
        ObserveRequest?.Invoke(opcode, buffer.AsSpan(offset, count).ToArray());
        Assert.True(opcode switch
        {
            3 or 5 or 91 or 4 => true,
            6 or 13 => ParameterTypes.Length != 0,
            26 or 90 => ParameterTypes.Contains(12),
            44 => AffectedRows != null,
            _ => false
        }, $"Unexpected synthetic opcode {opcode}.");
        // Opcode 5 carries both prepare and execute. Its byte-21 flag
        // distinguishes their different reply header layouts.
        bool prepare = opcode == 5 && buffer[offset + 21] != 1;
        bool execute = opcode is 6 or 13 or 91 || opcode == 5 && !prepare;
        bool query = execute && AffectedRows == null;
        bool row = query && ScalarValue != null;
        byte[] uploadAck = opcode == 26
            ? Convert.FromHexString("020102030405060708090A0B0C0D0E0F1011121314") : [];
        int bodyLength = prepare ? 33 * ParameterTypes.Length : query ? 37 + (row ? 22 : 0) : uploadAck.Length;
        reply = new byte[64 + bodyLength];
        BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(4), opcode switch { 26 => 261, 44 => 0, _ => opcode });
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(6), bodyLength);
        if (opcode == 44) BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(10), 111); // no more results
        if (opcode == 3) BinaryPrimitives.WriteInt32LittleEndian(reply, 41);
        if (opcode == 26) uploadAck.CopyTo(reply, 64);
        if (prepare || execute) BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(20), execute && !query ? (short)158 : (short)160);
        if (prepare)
        {
            BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(22), (short)ParameterTypes.Length);
            for (int index = 0; index < ParameterTypes.Length; index++)
            {
                Span<byte> parameter = reply.AsSpan(64 + 33 * index, 33);
                BinaryPrimitives.WriteInt32LittleEndian(parameter, ParameterTypes[index]);
                BinaryPrimitives.WriteInt32LittleEndian(parameter[12..], 1);
                BinaryPrimitives.WriteInt16LittleEndian(parameter[24..], 1);
                parameter[32] = (byte)('p' + index);
            }
        }
        if (execute && !query) BinaryPrimitives.WriteInt64LittleEndian(reply.AsSpan(24), AffectedRows!.Value);
        if (query)
        {
            BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(22), 1);
            // Independent 32-byte plain column descriptor: INT32(7),
            // precision 4, nullable, no LOB flag, and five-byte ASCII name.
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(64), 7);
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(68), 4);
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(76), 1);
            BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(88), 5);
            "VALUE"u8.CopyTo(reply.AsSpan(96));
            if (row)
            {
                BinaryPrimitives.WriteInt64LittleEndian(reply.AsSpan(24), 1);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(35), 1);
                // 2-byte row prefix, 12-byte row id, 2-byte column ordinal and 2-byte value length.
                BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(117), 4);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(119), ScalarValue!.Value);
            }
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
