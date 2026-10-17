using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Types;
using W.Dm.Internal.Diagnostics;
using W.Dm.Internal.Execution;

internal static class Program
{
    private const string Settings = "server=synthetic.invalid;user=PROBE;password=synthetic-only;schema=PROBE;Pooling=true;MaxPoolSize=1;MaxPoolWaiters=4;transport_security=PlaintextAllowed";
    private static int passed;
    private static int assertions;
    private static async Task<int> Main()
    {
        Console.WriteLine("runtime=" + RuntimeInformation.FrameworkDescription + " arch=" + RuntimeInformation.ProcessArchitecture);
        Console.WriteLine("provider=" + typeof(DmConnection).Assembly.Location);
        try
        {
            Check(Environment.Version.Major == 3 && Environment.Version.Minor == 1, "requires actual .NET Core 3.1 runtime");
            Check(typeof(DmConnection).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>().FrameworkName == ".NETStandard,Version=v2.1", "netstandard2.1 provider asset");
            await DataSource(); Pass("typed datasource lifecycle and failed opens");
            ArrayPlan(); Pass("array plan snapshots, NULL typing and invalid length rejection");
            Temporal(); Pass("DateTime/TimeSpan parameter semantics and timestamp wire roundtrip");
            await PendingResolver(); Pass("pending resolver cancellation and deadline");
            await Loopback(false); await Loopback(true); Pass("sync/async loopback bytes, deadlines, cancellation and close");
            await Tls(); Pass("legacy TLS rejection and default RequireTls");
            Check(typeof(DmConnection).Assembly.GetType("W.Dm.DmBatch") == null, "batch class absent");
            Check(typeof(DmDataSource).GetMethod("CreateBatch") == null, "datasource batch API absent");
            Check(typeof(DmConnection).GetMethod("GetSchemaAsync", new[] { typeof(string), typeof(string[]), typeof(CancellationToken) }) != null, "typed async schema API retained");
            Check(typeof(DmTransaction).GetMethod("SaveAsync", new[] { typeof(string), typeof(CancellationToken) }) != null, "typed async savepoint API retained");
            Check(typeof(DmTransaction).GetProperty("SupportsSavepoints") != null, "savepoint capability retained");
            Pass("batch API absent, typed schema/savepoint members retained");
            Check(await DmDiagnosticsCore.FlushAsync(TimeSpan.FromSeconds(2)), "diagnostics queue flush");
            Check(DmDiagnosticsCore.Snapshot.InitializationState == 1, "diagnostics dependencies initialized");
            Console.WriteLine("PASS groups=" + passed + " assertions=" + assertions); return 0;
        }
        catch (Exception error)
        {
            // All inputs are synthetic. Never load real connection secrets in this executable.
            Console.Error.WriteLine("FAIL " + error); return 1;
        }
    }
    private static void Pass(string message) { passed++; Console.WriteLine("PASS " + message); }
    private static void Check(bool value, string message) { Interlocked.Increment(ref assertions); if (!value) throw new Exception(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T error) when (!(error is ProbeWatchdogException)) { Interlocked.Increment(ref assertions); return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await Bounded(action()); } catch (T error) when (!(error is ProbeWatchdogException)) { Interlocked.Increment(ref assertions); return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class ProbeWatchdogException : Exception
    {
        internal ProbeWatchdogException() : base("Probe watchdog expired") { }
    }
    private static async Task Bounded(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(5000)) != task) throw new ProbeWatchdogException();
        await task;
    }
    private static async Task DataSource()
    {
        using var hook = new Handshake();
        foreach (bool asynchronous in new[] { false, true })
        {
            using var source = new DmDataSource(Settings);
            DmConnection undelivered = null;
            var expected = new InvalidOperationException("synthetic handshake failure");
            DmPendingOpenTestHooks.AfterInstalled = connection => undelivered = connection;
            DmPendingOpenTestHooks.Handshake = (_, _, _) => throw expected;
            Exception failure = asynchronous
                ? await ThrowsAsync<InvalidOperationException>(async () => { await source.OpenConnectionAsync(); })
                : Throws<InvalidOperationException>(() => source.OpenConnection());
            Check(ReferenceEquals(expected, failure), "original failure preserved");
            Check(undelivered != null, "failed open installed candidate");
            Throws<ObjectDisposedException>(() => undelivered.Open());
            Check(source.Snapshot.PhysicalCount == 0, "failed open returns capacity");
            DmPendingOpenTestHooks.AfterInstalled = null;
            DmPendingOpenTestHooks.Handshake = (candidate, _, _) => { hook.Install(candidate); return default; };
            using var open = asynchronous ? await source.OpenConnectionAsync() : source.OpenConnection();
            Check(open.State == ConnectionState.Open && source.Snapshot.Leased == 1, "next lease admitted");
            using var closed = source.CreateConnection();
            await source.DisposeAsync();
            Throws<ObjectDisposedException>(() => source.CreateConnection());
            await ThrowsAsync<ObjectDisposedException>(() => closed.OpenAsync());
            Check(open.State == ConnectionState.Open, "published lease survives datasource disposal");
            if (asynchronous) await open.CloseAsync(); else open.Close();
            Check(source.Snapshot.PhysicalCount == 0 && hook.Channels.Last().IsClosed, "lease transport released");
        }
    }
    // Core of ArrayBindingAndBatchTests.ArrayPlanSnapshotsRowsAndSharesInferredTypeWithNullRows.
    private static void ArrayPlan()
    {
        int[] ids = { 1, 2, 3 };
        byte[][] blobs = { new byte[] { 1 }, new byte[] { 2, 2 }, new byte[] { 3, 3, 3 } };
        object[] names = { DBNull.Value, "b", null };
        using var command = new DmCommand("INSERT INTO T VALUES (?, ?, ?)");
        command.Parameters.Add(new DmParameter("ID", ids));
        command.Parameters.Add(new DmParameter("B", blobs));
        command.Parameters.Add(new DmParameter("N", names));
        using var plan = DmCommandPlan.Capture(command.CommandText, CommandType.Text, 30, null, null, command.Parameters, 3);
        ids[0] = 99; blobs[1][0] = 99; names[1] = "changed";
        Check(plan.ArrayBindCount == 3, "array row count captured");
        DmParameter id = plan.Parameters.do_GetParameter(0), blob = plan.Parameters.do_GetParameter(1), name = plan.Parameters.do_GetParameter(2);
        Check((int)plan.RowParameters(id)(0).Value == 1, "integer array snapshotted");
        Check(((byte[])plan.RowParameters(blob)(1).Value).SequenceEqual(new byte[] { 2, 2 }), "nested blob copied");
        Check((string)plan.RowParameters(name)(1).Value == "b", "object array snapshotted");
        for (int row = 0; row < 3; row++) Check(plan.RowParameters(name)(row).DbType == DbType.String, "NULL shares inferred string type");
        Check(plan.RowParameters(name)(0).Value == DBNull.Value && plan.RowParameters(name)(2).Value == DBNull.Value, "both NULL forms normalized");
        using var invalid = new DmCommand("INSERT INTO T VALUES (?)");
        invalid.Parameters.Add(new DmParameter("P", new[] { 1, 2 }));
        Throws<InvalidOperationException>(() => DmCommandPlan.Capture(invalid.CommandText, CommandType.Text, 30, null, null, invalid.Parameters, 3));
    }
    private static void Temporal()
    {
        foreach (DateTime date in new[] { DateTime.MinValue, new DateTime(2000, 2, 29), new DateTime(9999, 12, 31) })
        {
            int dayNumber = DmDateTime.DmtimeCalcNDaysFromZero(date.Year, date.Month, date.Day);
            int year = 0, month = 0, day = 0;
            DmDateTime.DmTimeGetDateByNdaysFromZero(dayNumber, ref year, ref month, ref day);
            Check(new DateTime(year, month, day) == date, "day-number roundtrip boundary");
            DateTime projected = DmTemporalInteger.ProjectDate(new DmTemporalFields(year, month, day, 12, 34, 56, 0));
            Check(projected == date && projected.Kind == DateTimeKind.Unspecified, "date projection ignores clock");
        }
        Throws<OverflowException>(() => DmDateTime.DmtimeCalcNDaysFromZero(1900, 2, 29));
        DateTime stamp = new DateTime(2026, 7, 23, 14, 15, 16, DateTimeKind.Unspecified).AddTicks(1);
        TimeSpan span = new TimeSpan(0, 12, 34, 56).Add(TimeSpan.FromTicks(1234560));
        var dateParameter = new DmParameter("p", stamp);
        var timeParameter = new DmParameter("p", span);
        Check((DateTime)dateParameter.Value == stamp && dateParameter.DbType == DbType.DateTime, "DateTime inference preserves value");
        Check((TimeSpan)DmTemporalCodec.NormalizeForWire(span, 21, 6) == span, "TimeSpan interval wire normalization");
        Check((TimeSpan)timeParameter.Value == span && timeParameter.DbType == DbType.Object && timeParameter.DmSqlType == DmDbType.IntervalDayToSecond, "TimeSpan inference preserves value");
        object normalized = DmTemporalCodec.NormalizeForWire(stamp, 26, 7, "8.1.5.60");
        byte[] wire = null;
        DmDateTime.DmdtEncodeFast2(ref wire, (DateTime)normalized);
        Check(wire.SequenceEqual(new byte[] { 0xEA, 0x87, 0xBB, 0xEE, 0x81, 0xC8, 0, 0, 0 }), "timestamp golden bytes");
        var decoded = DmTemporalInteger.ProjectDateTime(DmTemporalWireCodec.DecodeRaw(wire, DmTemporalWireLayout.Timestamp9, 7));
        Check(decoded.Ticks == stamp.Ticks && decoded.Kind == DateTimeKind.Unspecified, "timestamp ticks/kind roundtrip");
    }
    private static async Task Loopback(bool asynchronous)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using var transport = new DmTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            Task<Socket> accepting = listener.AcceptSocketAsync();
            if (asynchronous) await Bounded(transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(2))));
            else transport.Open(DmDeadline.Start(TimeSpan.FromSeconds(2)));
            await Bounded(accepting); using Socket peer = accepting.Result;
            peer.ReceiveTimeout = peer.SendTimeout = 2000;
            byte[] bytes = { 2, 3, 5, 7 };
            if (asynchronous) await transport.SendAllAsync(bytes, 0, bytes.Length, DmDeadline.Start(TimeSpan.FromSeconds(2)), 0);
            else transport.SendAll(bytes, 0, bytes.Length, DmDeadline.Start(TimeSpan.FromSeconds(2)), 0);
            byte[] received = new byte[4]; int offset = 0;
            while (offset < received.Length) { int n = peer.Receive(received, offset, received.Length - offset, SocketFlags.None); Check(n > 0, "peer EOF"); offset += n; }
            Check(bytes.SequenceEqual(received), "outbound bytes");
            foreach (byte value in bytes) peer.Send(new[] { value });
            Array.Clear(received, 0, received.Length);
            if (asynchronous) await transport.ReadExactlyAsync(received, 0, 4, DmDeadline.Start(TimeSpan.FromSeconds(2)), 0);
            else transport.ReadExactly(received, 0, 4, DmDeadline.Start(TimeSpan.FromSeconds(2)), 0);
            Check(bytes.SequenceEqual(received), "inbound bytes");
            if (asynchronous)
            {
                using var cancellation = new CancellationTokenSource();
                Task read = transport.ReadSomeAsync(received, 0, 1, DmDeadline.Infinite, 0, cancellation.Token).AsTask();
                Check(!read.IsCompleted, "read remains pending"); cancellation.Cancel();
                var error = await ThrowsAsync<OperationCanceledException>(() => read);
                Check(error.CancellationToken == cancellation.Token, "caller cancellation token preserved");
            }
            else Throws<TimeoutException>(() => transport.ReadSome(received, 0, 1, DmDeadline.Start(TimeSpan.FromMilliseconds(100)), 0));
            transport.Close(); transport.Close(); Check(transport.IsClosed, "idempotent close");
        }
        finally { listener.Stop(); }
        if (asynchronous) await AsyncDeadline();
        // Fresh socket: cancellation is allowed to retire the previous socket on old runtimes.
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using var transport = new DmTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            Task<Socket> accepting = listener.AcceptSocketAsync(); await transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(2)));
            await Bounded(accepting); using Socket peer = accepting.Result;
            Task read = asynchronous
                ? transport.ReadSomeAsync(new byte[1], 0, 1, DmDeadline.Infinite, 0).AsTask()
                : Task.Run(() => transport.ReadSome(new byte[1], 0, 1, DmDeadline.Infinite, 0));
            await Task.Delay(50); Check(!read.IsCompleted, "close test read pending"); transport.Close();
            await ThrowsAsync<Exception>(() => read); Check(transport.IsClosed, "close unblocks pending read");
        }
        finally { listener.Stop(); }
    }
    private static async Task PendingResolver()
    {
        var resolver = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var transport = new DmTransport("pending.invalid", 1, (_, _) => resolver.Task))
        using (var cancellation = new CancellationTokenSource())
        {
            DmTransportTestHooks.Reset();
            Task opening = transport.OpenAsync(DmDeadline.Infinite, cancellation.Token);
            Check(!opening.IsCompleted, "DNS remains pending"); cancellation.Cancel();
            var error = await ThrowsAsync<OperationCanceledException>(() => opening);
            Check(error.CancellationToken == cancellation.Token, "pending DNS preserves caller token");
            Check(DmTransportTestHooks.AttemptedTcpConnections == 0 && transport.IsClosed, "pending DNS cancellation closes without TCP");
            resolver.SetException(new IOException("synthetic late DNS failure"));
            Check(resolver.Task.Exception.InnerException is IOException, "late DNS failure observed");
        }
        resolver = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var transport = new DmTransport("pending.invalid", 1, (_, _) => resolver.Task))
        {
            DmTransportTestHooks.Reset();
            await ThrowsAsync<TimeoutException>(() => transport.OpenAsync(DmDeadline.Start(TimeSpan.FromMilliseconds(100))));
            Check(DmTransportTestHooks.AttemptedTcpConnections == 0 && transport.IsClosed, "pending DNS deadline closes without TCP");
            resolver.SetException(new IOException("synthetic late DNS deadline failure"));
            Check(resolver.Task.Exception.InnerException is IOException, "late deadline DNS failure observed");
        }
    }
    private static async Task AsyncDeadline()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using var transport = new DmTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            Task<Socket> accepting = listener.AcceptSocketAsync();
            await transport.OpenAsync(DmDeadline.Start(TimeSpan.FromSeconds(2)));
            await Bounded(accepting); using Socket peer = accepting.Result;
            await ThrowsAsync<TimeoutException>(() => transport.ReadSomeAsync(new byte[1], 0, 1,
                DmDeadline.Start(TimeSpan.FromMilliseconds(100)), 0).AsTask());
            transport.Close(); Check(transport.IsClosed, "deadline socket released");
        }
        finally { listener.Stop(); }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        DmTransportTestHooks.Reset();
        using var canceled = new DmTransport("127.0.0.1", 1);
        await ThrowsAsync<OperationCanceledException>(() => canceled.OpenAsync(DmDeadline.Infinite, cancellation.Token));
        Check(DmTransportTestHooks.AttemptedTcpConnections == 0, "precanceled connect never attempts TCP");
        canceled.Close(); Check(canceled.IsClosed, "precanceled transport closes without resources");
    }
    private static async Task Tls()
    {
        var builder = new DmConnectionStringBuilder { Server = "127.0.0.1", Port = 1 };
        Check(builder.TransportSecurity == DmTransportSecurity.RequireTls, "secure policy is default");
        foreach (bool asynchronous in new[] { false, true })
        foreach (int mode in new[] { 0, 1, 4 })
        foreach (bool plaintext in new[] { false, true })
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int login = 0;
            Task server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync();
                using NetworkStream stream = peer.GetStream();
                byte[] header = new byte[64]; await ReadFrame(stream, header);
                Check(BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(4)) == 200, "STARTUP first");
                byte[] version = Encoding.ASCII.GetBytes("8.1.5.60");
                byte[] reply = new byte[84 + version.Length];
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(0), 41);
                BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(4), 200);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(6), 20 + version.Length);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(20), mode);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(28), 1);
                BinaryPrimitives.WriteInt16LittleEndian(reply.AsSpan(48), 8);
                BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(80), version.Length);
                version.CopyTo(reply, 84);
                for (int i = 0; i < 19; i++) reply[19] ^= reply[i];
                await stream.WriteAsync(reply, 0, reply.Length);
                int first = await stream.ReadAsync(header, 0, 1);
                if (mode == 0 && plaintext)
                {
                    Check(first == 1, "explicit plaintext reaches LOGIN");
                    await ReadExactly(stream, header, 1, 63);
                    Check(BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(4)) == 1, "expected LOGIN opcode");
                    login++;
                }
                else Check(first == 0, "TLS refusal ends with EOF: no LOGIN or TLS hello");
            });
            try
            {
                string text = "server=127.0.0.1;port=" + ((IPEndPoint)listener.LocalEndpoint).Port + ";user=PROBE;password=synthetic-only;Pooling=false";
                if (plaintext) text += ";transport_security=PlaintextAllowed";
                using var connection = new DmConnection(text);
                Exception error = asynchronous
                    ? await ThrowsAsync<Exception>(() => connection.OpenAsync())
                    : Throws<Exception>(() => connection.Open());
                if (mode != 0) Check(error is PlatformNotSupportedException, "TLS-required STARTUP explicitly unsupported");
                else if (!plaintext) Check(error is NotSupportedException, "mode0 default RequireTls refuses plaintext");
                Check(connection.State == ConnectionState.Closed, "failed handshake closes connection");
                await Bounded(server);
                Check(login == (mode == 0 && plaintext ? 1 : 0), "LOGIN only under explicit plaintext mode0");
            }
            finally { listener.Stop(); }
        }
        Check(typeof(DmCommand).GetField("ArrayBindCount") != null, "array binding API retained");
    }
    private static async Task ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int n = await stream.ReadAsync(buffer, offset, count);
            if (n == 0) throw new EndOfStreamException();
            offset += n; count -= n;
        }
    }
    private static async Task ReadFrame(Stream stream, byte[] header)
    {
        await ReadExactly(stream, header, 0, header.Length);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(6));
        Check(length >= 0 && length <= 1048576, "synthetic frame body bounded");
        byte[] discard = new byte[Math.Min(length, 4096)];
        while (length > 0) { int n = Math.Min(length, discard.Length); await ReadExactly(stream, discard, 0, n); Array.Clear(discard, 0, n); length -= n; }
    }
    // Adapted from ConnectionPoolTests.SyntheticPoolHandshake; no protocol I/O is accepted.
    private sealed class Handshake : IDisposable
    {
        internal readonly List<Channel> Channels = new List<Channel>();
        internal void Install(DmConnection candidate)
        {
            var instance = new DmConnInstance(candidate); candidate.m_ConnInst = instance;
            instance.ConnProperty.ServerVersion = "8.1.5.60";
            var protocol = instance.GetCsi(); var wire = protocol.A();
            ((DmTransport)Field(wire, "transport").GetValue(wire)).Dispose();
            var channel = new Channel(); Channels.Add(channel);
            Field(wire, "transport").SetValue(wire, new DmTransport(channel));
            Field(wire, "__t02_field_04000AAD").SetValue(wire, false);
            Field(protocol, "__t02_field_04000ABD").SetValue(protocol, false);
            candidate.Session.BeginAuthenticating(); candidate.do_State = ConnectionState.Open;
        }
        private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        public void Dispose()
        {
            DmPendingOpenTestHooks.Handshake = null; DmPendingOpenTestHooks.AfterInstalled = null;
        }
    }
    private sealed class Channel : IDmByteChannel
    {
        public bool IsClosed { get; private set; }
        public int Send(byte[] b, int o, int c, int t) => throw new Exception("unexpected handshake I/O");
        public int Receive(byte[] b, int o, int c, int t) => throw new Exception("unexpected handshake I/O");
        public ValueTask<int> SendAsync(byte[] b, int o, int c, CancellationToken t) => throw new Exception("unexpected handshake I/O");
        public ValueTask<int> ReceiveAsync(byte[] b, int o, int c, CancellationToken t) => throw new Exception("unexpected handshake I/O");
        public void Dispose() => IsClosed = true;
    }
}
