using System;
using System.Net;
using System.Net.Sockets;

namespace W.DmProvider.TestSupport;

/// <summary>A loopback listener that stops on Dispose; TcpListener is IDisposable only on .NET 8+.</summary>
internal sealed class LoopbackTcpListener : TcpListener, IDisposable
{
    internal LoopbackTcpListener() : base(IPAddress.Loopback, 0) { }

    public new void Dispose() => Stop();
}
