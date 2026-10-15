using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;

namespace W.Dm.Internal.Transport;

/// <summary>
/// Remembers socket and stream failures raised by this driver's own transport I/O.
/// Caller-supplied input streams may also throw IOException, so the public boundary
/// wraps only exceptions observed here, never every IOException by type.
/// </summary>
internal static class DmTransportFailure
{
    private static readonly object Marker = new();
    private static readonly ConditionalWeakTable<Exception, object> observed = new();

    // Used as an exception filter: records the failure and never catches it, so the
    // original exception and stack continue unchanged to the existing cleanup paths.
    internal static bool Observe(Exception error)
    {
        if (error is SocketException or IOException) observed.AddOrUpdate(error, Marker);
        return false;
    }

    // The TLS upgrade reports every handshake failure as AuthenticationException;
    // only one caused by an observed socket/stream failure is a transport loss.
    internal static bool IsTransportFailure(Exception error) =>
        error is SocketException or IOException ? observed.TryGetValue(error, out _) :
        error is AuthenticationException { InnerException: SocketException or IOException } authentication &&
            observed.TryGetValue(authentication.InnerException, out _);

    internal static DmException Wrap(Exception error, DmFailureInfo info, string message = null)
    {
        message ??= info.Phase is DmFailurePhase.Dns or DmFailurePhase.Connect or DmFailurePhase.Authenticate
            ? "The network connection to the server failed while connecting."
            : info.OperationOutcome == DmOperationOutcome.NotSent
                ? "The network connection failed before the request was sent."
                : "The network connection failed after the request was sent; the operation outcome is unknown.";
        return new DmException(message, error, info);
    }
}
