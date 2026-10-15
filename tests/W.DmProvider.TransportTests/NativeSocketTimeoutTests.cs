using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using LegacyTransport = W.Dm.Internal.Legacy.A.D;

namespace W.DmProvider.TransportTests;

public sealed class NativeSocketTimeoutTests
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
    public void NativeTimeoutUsesSelectedBudgetAndPreservesCause(bool receive, bool tlsWrapper, bool idle)
    {
        var socketError = new SocketException((int)SocketError.TimedOut);
        Exception cause = tlsWrapper ? new IOException("TLS read failed.", socketError) : socketError;
        using var fixture = new NativeFailureFixture(cause, receive);
        fixture.Session.SetTransactionState(DmLocalTransactionState.Active);
        var clock = new CancellationClock();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query,
            DmDeadline.FromMilliseconds(idle ? 100 : 40, clock));
        using var invocation = lease.BeginInvocation();
        using var wire = fixture.Session.BeginWireExchange();
        if (receive) fixture.Transport.SendAll([1], 0, 1, invocation.Deadline, 0);
        var error = Assert.Throws<DmTimeoutException>(() => Execute(fixture.Transport, receive,
            invocation.Deadline, idle ? 40 : 100));
        Assert.Equal(40, fixture.Channel.LastTimeout);
        Assert.Equal(SocketError.TimedOut, socketError.SocketErrorCode);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(idle ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline, error.FailureInfo!.CancelSource);
        Assert.Equal(DmErrorKind.Timeout, error.FailureInfo.ErrorKind);
        Assert.Equal(receive ? DmFailurePhase.Receive : DmFailurePhase.Send, error.FailureInfo.Phase);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
        Assert.Same(error, invocation.TranslateFailure(error));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierUserCancellationWinsOverNativeTimeout(bool receive)
    {
        var cause = new SocketException((int)SocketError.TimedOut);
        using var fixture = new NativeFailureFixture(cause, receive);
        using var cancellation = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query,
            DmDeadline.FromMilliseconds(100, new CancellationClock()));
        using var invocation = lease.BeginInvocation(cancellation.Token);
        using var wire = fixture.Session.BeginWireExchange();
        if (receive) fixture.Transport.SendAll([1], 0, 1, invocation.Deadline, 0);
        fixture.Channel.BeforeFailure = cancellation.Cancel;
        var error = Assert.Throws<DmOperationCanceledException>(() => Execute(fixture.Transport, receive, invocation.Deadline, 40));
        Assert.Equal(DmCancelSource.User, error.FailureInfo.CancelSource);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Same(cause, error.InnerException);
        Assert.Equal(DmOperationOutcome.Unknown, error.FailureInfo.OperationOutcome);
        Assert.Equal(1, fixture.Channel.DisposeCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OtherSocketFailuresRemainTransportErrors(bool receive, bool tlsWrapper)
    {
        var socketError = new SocketException((int)SocketError.ConnectionReset);
        Exception cause = tlsWrapper ? new IOException("TLS read failed.", socketError) : socketError;
        using var fixture = new NativeFailureFixture(cause, receive);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query,
            DmDeadline.FromMilliseconds(100, new CancellationClock()));
        using var invocation = lease.BeginInvocation();
        using var wire = fixture.Session.BeginWireExchange();
        if (receive) fixture.Transport.SendAll([1], 0, 1, invocation.Deadline, 0);
        var raw = Record.Exception(() => Execute(fixture.Transport, receive, invocation.Deadline, 40));
        Assert.Same(cause, raw);
        var translated = Assert.IsType<DmException>(invocation.TranslateFailure(raw));
        Assert.Equal(DmErrorKind.Transport, translated.FailureInfo!.ErrorKind);
        Assert.Equal(DmCancelSource.None, translated.FailureInfo.CancelSource);
        Assert.Same(cause, translated.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SocketTimeoutWithoutConfiguredBudgetRemainsTransportError(bool receive)
    {
        var cause = new SocketException((int)SocketError.TimedOut);
        using var fixture = new NativeFailureFixture(cause, receive);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        using var wire = fixture.Session.BeginWireExchange();
        if (receive) fixture.Transport.SendAll([1], 0, 1, invocation.Deadline, 0);
        var raw = Assert.Throws<SocketException>(() => Execute(fixture.Transport, receive, DmDeadline.Infinite, 0));
        Assert.Same(cause, raw);
        Assert.Equal(0, fixture.Channel.LastTimeout);
        var translated = Assert.IsType<DmException>(invocation.TranslateFailure(raw));
        Assert.Equal(DmErrorKind.Transport, translated.FailureInfo!.ErrorKind);
        Assert.Equal(DmCancelSource.None, translated.FailureInfo.CancelSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteIdleBudgetWithoutInvocationStillRetainsNativeCause(bool receive)
    {
        var cause = new SocketException((int)SocketError.TimedOut);
        using var channel = new NativeFailureChannel(cause, receive);
        using var transport = new DmTransport(channel);
        var error = Assert.Throws<TimeoutException>(() => Execute(transport, receive, DmDeadline.Infinite, 40));
        Assert.Same(cause, error.InnerException);
        Assert.Equal(40, channel.LastTimeout);
    }

    private static void Execute(DmTransport transport, bool receive, DmDeadline deadline, int idleMilliseconds)
    {
        if (receive) transport.ReadSome(new byte[1], 0, 1, deadline, idleMilliseconds);
        else transport.SendAll([1], 0, 1, deadline, idleMilliseconds);
    }

    // Same registered-transport fixture as CancellationFixture, with deterministic
    // synchronous native failures instead of the existing async barrier channel.
    private sealed class NativeFailureFixture : IDisposable
    {
        internal DmSession Session { get; } = new();
        internal NativeFailureChannel Channel { get; }
        internal DmTransport Transport { get; }
        internal NativeFailureFixture(Exception error, bool receive)
        {
            Channel = new NativeFailureChannel(error, receive);
            Transport = new DmTransport(Channel);
            var adapter = (LegacyTransport)RuntimeHelpers.GetUninitializedObject(typeof(LegacyTransport));
            typeof(LegacyTransport).GetField("transport", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(adapter, Transport);
            Session.RegisterPendingTransport(adapter);
            Session.CompleteHandshakeForTests();
        }
        public void Dispose() => Transport.Dispose();
    }

    private sealed class NativeFailureChannel(Exception error, bool receiveFailure) : IDmByteChannel
    {
        internal int LastTimeout;
        internal int DisposeCalls;
        internal Action? BeforeFailure;
        public bool IsClosed => DisposeCalls != 0;
        public int Send(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            if (!receiveFailure) Fail(timeoutMilliseconds);
            return count;
        }
        public int Receive(byte[] buffer, int offset, int count, int timeoutMilliseconds)
        {
            Fail(timeoutMilliseconds);
            return 0;
        }
        private void Fail(int timeoutMilliseconds)
        {
            LastTimeout = timeoutMilliseconds;
            BeforeFailure?.Invoke();
            throw error;
        }
        public void Dispose() => DisposeCalls++;
    }
}
