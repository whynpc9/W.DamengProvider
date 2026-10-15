using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using Xunit;
using Statement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "Pooling")]
public sealed class ServerErrorClassificationTests
{
    [Theory]
    [InlineData(false, "8.1.4.6", -6602, false)]
    [InlineData(true, "8.1.4.6", -6602, false)]
    [InlineData(false, "8.1.4.6", -6602, true)]
    [InlineData(true, "8.1.4.6", -6602, true)]
    [InlineData(false, "8.1.4.6", -2106, true)]
    [InlineData(true, "8.1.4.6", -2106, true)]
    // Unmeasured numbers and the edges outside the RAISE_APPLICATION_ERROR range.
    [InlineData(false, "8.1.5.60", -6001, true)]
    [InlineData(true, "8.1.5.60", -6001, false)]
    [InlineData(false, "8.1.5.60", -19999, false)]
    [InlineData(true, "8.1.5.60", -30001, true)]
    [InlineData(false, "8.1.5.60", -1, false)]
    // -3510 is also EC_BAK_INFO_WRITE, not only an out-of-range RAISE error.
    [InlineData(false, "8.1.5.60", -3510, false)]
    [InlineData(true, "8.1.5.60", -3510, false)]
    [InlineData(false, "8.1.5.60", -3510, true)]
    [InlineData(true, "8.1.5.60", -3510, true)]
    public async Task CompleteStatementErrorHasServerReceiptButGenericSessionIsDiscarded(bool asynchronous, string version, int number, bool transaction)
    {
        using var fixture = new ErrorFixture(version, transaction);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        invocation.Phase = DmFailurePhase.Receive;
        var wire = fixture.Session.BeginWireExchange();
        var response = Response(number, Body("SYNTH", "TABLE", "COL", "diagnostic"));
        var error = await CaptureAsync(asynchronous, response, fixture);
        wire.Dispose();
        Assert.Same(error, invocation.TranslateFailure(error));
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.True(invocation.Completed);
        Assert.True(invocation.ShouldAbortAfterFailure(error));
        Assert.Equal(number, error.Number);
        Assert.Equal(number, error.FailureInfo.ServerErrorNumber);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmFailurePhase.Receive, error.FailureInfo.Phase);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
        if (transaction) Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
        Assert.Equal(0, response.a(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingExactTransactionWhitelistKeepsSessionAndReceipt(bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-2106, Body("", "", "", "duplicate")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.False(invocation.ShouldAbortAfterFailure(error));
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.False(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Theory]
    [InlineData(false, -6602, true)]
    [InlineData(true, -6602, false)]
    [InlineData(false, -6607, false)]
    [InlineData(true, -6609, true)]
    [InlineData(false, -2106, false)]
    [InlineData(true, -564, false)]
    [InlineData(false, -2713, false)]
    [InlineData(true, -20000, false)]
    [InlineData(false, -30000, true)]
    [InlineData(true, -6628, false)]
    [InlineData(false, -5622, false)]
    [InlineData(true, -7069, true)]
    [InlineData(false, -6804, false)]
    [InlineData(true, -6804, false)]
    [InlineData(false, -6804, true)]
    [InlineData(true, -6804, true)]
    public async Task MeasuredStatementScopedErrorKeepsSessionInAutocommitAndTransaction(bool asynchronous, int number, bool transaction)
    {
        using var fixture = new ErrorFixture("8.1.5.60", transaction);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var response = Response(number, Body("", "", "", "statement"));
        var error = await CaptureAsync(asynchronous, response, fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.False(invocation.ShouldAbortAfterFailure(error));
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(number, error.FailureInfo.ServerErrorNumber);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        if (transaction) Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        else Assert.Null(error.FailureInfo.TransactionOutcome);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.False(fixture.Handshake.Channels.Single().IsClosed);
        Assert.Equal(0, response.a(false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualCommitWithoutLocalTransactionCannotEnterPreservationWhitelist(bool asynchronous)
    {
        // Autocommit off with no DmTransaction means the server may hold an implicit
        // transaction this client does not track; keep the conservative discard.
        using var fixture = new ErrorFixture("8.1.5.60", false);
        fixture.Instance.ConnProperty.AutoCommit = false;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-6602, Body("", "", "", "duplicate")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightTransactionStateCannotEnterAutocommitPreservation(bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.5.60", false);
        fixture.Session.SetTransactionState(DmLocalTransactionState.OutcomeUnknown);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-6602, Body("", "", "", "duplicate")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedTransactionOwnerCannotEnterPreservationWhitelist(bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        fixture.Instance.Transaction = null;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-2106, Body("", "", "", "duplicate")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
    }

    [Theory]
    [InlineData(false, -2007)]
    [InlineData(true, -2007)]
    [InlineData(false, -2106)]
    [InlineData(true, -2106)]
    [InlineData(false, -4015)]
    [InlineData(true, -4015)]
    public void MeasuredPrepareErrorKeepsOwnedSessionAndTransaction(bool transaction, int number)
    {
        using var fixture = new ErrorFixture("8.1.5.60", transaction);
        var activeTransaction = fixture.Session.ActiveTransaction;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        invocation.Phase = DmFailurePhase.Receive;
        var wire = fixture.Session.BeginWireExchange();
        var response = Response(number, Body("", "", "", "prepare"));
        var error = Assert.Throws<DmException>(() => c.A(response, fixture.Statement));
        wire.Dispose();
        Assert.Same(error, invocation.TranslateFailure(error));
        Assert.True(error.HasVerifiedServerResponse);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.True(invocation.Completed);
        Assert.False(invocation.ShouldAbortAfterFailure(error));
        Assert.Equal(number, error.Number);
        Assert.Equal(number, error.FailureInfo.ServerErrorNumber);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmFailurePhase.Receive, error.FailureInfo.Phase);
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Busy, fixture.Session.State);
        Assert.False(fixture.Handshake.Channels.Single().IsClosed);
        Assert.Same(activeTransaction, fixture.Session.ActiveTransaction);
        if (transaction) Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        else Assert.Null(error.FailureInfo.TransactionOutcome);
        Assert.Equal(0, response.a(false));
    }

    [Theory]
    [InlineData("8.1.4.6", -2007, false)]
    [InlineData("8.1.4.6", -2106, true)]
    [InlineData("8.1.4.6", -4015, false)]
    [InlineData("8.1.5.60.1", -4015, true)]
    [InlineData("8.1.5.60", -6804, false)]
    [InlineData("8.1.5.60", -6804, true)]
    [InlineData("8.1.5.60", -6602, false)]
    [InlineData("8.1.5.60", -20000, true)]
    [InlineData("8.1.5.60", -6001, false)]
    [InlineData("8.1.5.60", -1, true)]
    public void UnmeasuredPrepareProfileOrNumberKeepsReceiptButBreaksSession(string version, int number, bool transaction)
    {
        using var fixture = new ErrorFixture(version, transaction);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = Assert.Throws<DmException>(() => c.A(Response(number, Body("", "", "", "prepare")), fixture.Statement));
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.True(invocation.Completed);
        Assert.True(invocation.ShouldAbortAfterFailure(error));
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
        if (transaction) Assert.Equal(DmTransactionOutcome.OutcomeUnknown, error.FailureInfo.TransactionOutcome);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SessionAcceptanceRechecksPhaseSpecificErrorNumber(bool prepare, bool executeOnlyNumber)
    {
        using var fixture = new ErrorFixture("8.1.5.60", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        int number = executeOnlyNumber ? -6602 : -4015;
        var error = new DmException(c.ReadCompleteErrorBody(Response(number, new byte[16]), "UTF-8"));
        fixture.Session.AcceptServerError(invocation, wire, error, preserveSession: true, prepare: prepare);
        wire.Dispose();
        invocation.TranslateFailure(error);
        bool expected = prepare != executeOnlyNumber;
        Assert.True(error.HasVerifiedServerResponse);
        Assert.Equal(expected, error.CanPreserveSessionAfterServerError);
        Assert.Equal(expected, error.FailureInfo.ConnectionReusable);
        Assert.Equal(expected ? DmPhysicalSessionState.Busy : DmPhysicalSessionState.Broken, fixture.Session.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidFunctionDuringExecuteDoesNotUsePrepareWhitelist(bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.5.60", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = await CaptureAsync(asynchronous, Response(-4015, Body("", "", "", "invalid function")), fixture);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Theory]
    [InlineData("manual_commit")]
    [InlineData("transaction_in_flight")]
    [InlineData("changed_transaction_owner")]
    [InlineData("inactive_transaction")]
    [InlineData("non_query_operation")]
    public void PrepareRecoveryRequiresExistingTransactionAndOperationOwnership(string rejection)
    {
        bool transaction = rejection is "changed_transaction_owner" or "inactive_transaction";
        using var fixture = new ErrorFixture("8.1.5.60", transaction);
        if (rejection == "manual_commit") fixture.Instance.ConnProperty.AutoCommit = false;
        if (rejection == "transaction_in_flight") fixture.Session.SetTransactionState(DmLocalTransactionState.OutcomeUnknown);
        if (rejection == "changed_transaction_owner") fixture.Instance.Transaction = null;
        if (rejection == "inactive_transaction") fixture.Session.ActiveTransaction!.SetOutcomeFromSession(DmTransactionOutcome.OutcomeUnknown);
        using var lease = fixture.Session.BeginExecution(rejection == "non_query_operation" ? DmOperationPurpose.Metadata : DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = Assert.Throws<DmException>(() => c.A(Response(-4015, new byte[16]), fixture.Statement));
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.True(error.HasVerifiedServerResponse);
        Assert.False(error.CanPreserveSessionAfterServerError);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Theory]
    [InlineData("negative_length")]
    [InlineData("truncated_string")]
    [InlineData("overlong_string")]
    [InlineData("missing_fourth_length")]
    [InlineData("trailing")]
    [InlineData("bad_boundary")]
    [InlineData("invalid_utf8")]
    public void MalformedPrepareDiagnosticCannotBecomeVerifiedReceipt(string kind)
    {
        using var fixture = new ErrorFixture("8.1.5.60", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        byte[] body = kind switch
        {
            "negative_length" => BitConverter.GetBytes(-1),
            "truncated_string" => [6, 0, 0, 0, 65],
            "overlong_string" => BitConverter.GetBytes(int.MaxValue),
            "missing_fourth_length" => new byte[12],
            "trailing" => new byte[17],
            "invalid_utf8" => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0xc3],
            _ => new byte[16]
        };
        var response = Response(-4015, body);
        if (kind == "bad_boundary") response.G(63);
        Exception error = Record.Exception(() => c.A(response, fixture.Statement))!;
        if (kind == "invalid_utf8") Assert.IsType<DecoderFallbackException>(error);
        else Assert.IsType<InvalidDataException>(error);
        wire.Dispose();
        Assert.False(invocation.ServerErrorAccepted);
        Assert.False(invocation.Completed);
        Assert.Equal(DmErrorKind.Transport, invocation.CreateFailureInfo(error).ErrorKind);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Theory]
    [InlineData(false, "negative_length")]
    [InlineData(true, "negative_length")]
    [InlineData(false, "truncated_string")]
    [InlineData(true, "truncated_string")]
    [InlineData(false, "overlong_string")]
    [InlineData(true, "overlong_string")]
    [InlineData(false, "missing_fourth_length")]
    [InlineData(true, "missing_fourth_length")]
    [InlineData(false, "trailing")]
    [InlineData(true, "trailing")]
    [InlineData(false, "bad_boundary")]
    [InlineData(true, "bad_boundary")]
    public async Task MalformedBodyCannotBecomeServerReported(bool asynchronous, string kind)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        byte[] body = kind switch
        {
            "negative_length" => BitConverter.GetBytes(-1),
            "truncated_string" => [6, 0, 0, 0, 65],
            "overlong_string" => BitConverter.GetBytes(int.MaxValue),
            "missing_fourth_length" => new byte[12],
            "trailing" => new byte[17],
            _ => new byte[16]
        };
        var response = Response(-6602, body);
        if (kind == "bad_boundary") response.G(63);
        Exception error;
        if (asynchronous) error = await Assert.ThrowsAsync<InvalidDataException>(() => c.AAsync(response, fixture.Statement, fixture.Instance.ConnProperty));
        else error = Assert.Throws<InvalidDataException>(() => c.A(response, fixture.Statement, fixture.Instance.ConnProperty));
        wire.Dispose();
        Assert.Equal(DmErrorKind.Transport, invocation.CreateFailureInfo(error).ErrorKind);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.False(invocation.ServerErrorAccepted);
        Assert.False(invocation.Completed);
    }

    [Theory]
    [InlineData("UTF-8", false)]
    [InlineData("UTF-8", true)]
    [InlineData("GB18030", false)]
    [InlineData("GB18030", true)]
    public async Task InvalidDeclaredCharsetTailCannotBecomeVerifiedReceipt(string charset, bool asynchronous)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        fixture.Instance.ConnProperty.ServerEncoding = charset;
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        byte[] invalid = charset == "UTF-8" ? [0xc3] : [0x81, 0x30, 0x81];
        byte[] body = new byte[16 + invalid.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), invalid.Length);
        invalid.CopyTo(body, 16);
        var response = Response(-6602, body);
        Exception failure = asynchronous ? await Assert.ThrowsAsync<DecoderFallbackException>(() =>
            c.AAsync(response, fixture.Statement, fixture.Instance.ConnProperty)) :
            Assert.Throws<DecoderFallbackException>(() => c.A(response, fixture.Statement, fixture.Instance.ConnProperty));
        wire.Dispose();
        Assert.False(invocation.ServerErrorAccepted);
        Assert.Equal(DmErrorKind.Transport, invocation.CreateFailureInfo(failure).ErrorKind);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("command", false)]
    [InlineData("deadline", false)]
    [InlineData("idle", false)]
    [InlineData("user", true)]
    [InlineData("command", true)]
    [InlineData("deadline", true)]
    [InlineData("idle", true)]
    public void ReceiptAndCancellationHaveOneWinner(string cause, bool receiptFirst)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using var invocation = lease.BeginInvocation(caller.Token);
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        void Cancel()
        {
            if (cause == "user") caller.Cancel();
            else if (cause == "command") lease.Cancel();
            else fixture.Session.TerminateInvocation(invocation, cause == "idle" ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline);
        }
        if (receiptFirst)
        {
            fixture.Session.AcceptServerError(invocation, wire, error, false);
            Cancel();
            Assert.Equal(DmCancelSource.None, invocation.TerminalCause);
            Assert.True(error.HasVerifiedServerResponse);
            wire.Dispose();
            Assert.Same(error, invocation.TranslateFailure(error));
            Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
            Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        }
        else
        {
            Cancel();
            Exception failure = Record.Exception(() => fixture.Session.AcceptServerError(invocation, wire, error, false))!;
            Assert.NotNull(failure);
            Assert.False(error.HasVerifiedServerResponse);
            Assert.False(invocation.Completed);
            wire.Dispose();
            failure = invocation.TranslateFailure(error);
            if (cause is "user" or "command")
            {
                var canceled = Assert.IsType<DmOperationCanceledException>(failure);
                Assert.Equal(cause == "user" ? caller.Token : lease.CommandCancellationToken, canceled.CancellationToken);
                Assert.Equal(cause == "user" ? DmCancelSource.User : DmCancelSource.Command, canceled.FailureInfo.CancelSource);
                Assert.Equal(DmOperationOutcome.Unknown, canceled.FailureInfo.OperationOutcome);
            }
            else
            {
                var timedOut = Assert.IsType<DmTimeoutException>(failure);
                Assert.Equal(cause == "idle" ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline, timedOut.FailureInfo.CancelSource);
                Assert.Equal(DmOperationOutcome.Unknown, timedOut.FailureInfo.OperationOutcome);
            }
        }
    }

    [Fact]
    public void ActualDeadlineBudgetExpiredBeforeReceiptCannotBePromotedEvenIfTimerHasNotFired()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        var clock = new SilentDeadlineClock();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query, DmDeadline.Start(TimeSpan.FromSeconds(1), clock));
        using var invocation = lease.BeginInvocation();
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var failure = Assert.Throws<DmTimeoutException>(() => fixture.Session.AcceptServerError(invocation, wire, error, false));
        Assert.Equal(DmCancelSource.TotalDeadline, failure.FailureInfo.CancelSource);
        Assert.False(error.HasVerifiedServerResponse);
        wire.Dispose();
    }

    [Theory]
    [InlineData("user")]
    [InlineData("command")]
    [InlineData("deadline")]
    [InlineData("idle")]
    public void PreservedReceiptWinnerCannotBeChangedByLateReaderCancellation(string cause)
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using var invocation = lease.BeginInvocation(caller.Token);
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-2106, new byte[16]), "UTF-8"));
        fixture.Session.AcceptServerError(invocation, wire, error, true);
        if (cause == "user") caller.Cancel();
        else if (cause == "command") lease.Cancel();
        else fixture.Session.TerminateInvocation(invocation, cause == "idle" ? DmCancelSource.IdleTimeout : DmCancelSource.TotalDeadline);
        wire.Dispose();
        invocation.TranslateFailure(error);
        Assert.Equal(DmCancelSource.None, invocation.TerminalCause);
        Assert.True(error.CanPreserveSessionAfterServerError);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.True(error.FailureInfo.ConnectionReusable);
        Assert.Equal(DmTransactionOutcome.Active, error.FailureInfo.TransactionOutcome);
        Assert.False(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Fact]
    public void SuccessfulCompletedReaderCallStillAllowsCommandCancellationOfItsIdleCursor()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Reader);
        using var invocation = lease.BeginInvocation();
        invocation.SendAttempted = lease.SendAttempted = true;
        invocation.Complete();
        Assert.False(invocation.ServerErrorAccepted);
        lease.Cancel();
        Assert.Equal(DmCancelSource.Command, lease.TerminalCause);
        Assert.Equal(DmPhysicalSessionState.Broken, fixture.Session.State);
        Assert.True(fixture.Handshake.Channels.Single().IsClosed);
    }

    [Fact]
    public async Task DiagnosticTraceCallbackCanCancelAfterBodyValidationBeforeAcceptance()
    {
        using var fixture = new ErrorFixture("8.1.5.60", true);
        using var caller = new CancellationTokenSource();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation(caller.Token);
        invocation.SendAttempted = lease.SendAttempted = true;
        var wire = fixture.Session.BeginWireExchange();
        bool traced = false;
        DmTransactionProtocolTrace.AfterDiagnosticBody = (_, _, _, _, _, _, remaining, complete) =>
        {
            traced = true;
            Assert.True(complete);
            Assert.Equal(0, remaining);
            caller.Cancel();
        };
        try
        {
            var error = await Assert.ThrowsAsync<DmOperationCanceledException>(() => c.AAsync(
                Response(-2106, Body("", "", "", "duplicate")), fixture.Statement, fixture.Instance.ConnProperty));
            Assert.True(traced);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Equal(DmErrorKind.Canceled, error.FailureInfo.ErrorKind);
            Assert.False(invocation.ServerErrorAccepted);
        }
        finally { DmTransactionProtocolTrace.AfterDiagnosticBody = null; wire.Dispose(); }
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    public void StaleIdentityCannotAcceptReceiptOrModifyAnotherActiveInvocation(string wrong)
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var original = lease.BeginInvocation();
        var wire = fixture.Session.BeginWireExchange();
        var identity = wrong == "session" ? original.Identity with { SessionId = original.Identity.SessionId + 1 } :
            original.Identity with { LeaseGeneration = original.Identity.LeaseGeneration + 1 };
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        using (var stale = new DmInvocation(fixture.Session, lease, identity, DmDeadline.Infinite))
        {
            SetField(fixture.Session, "activeInvocation", stale);
            try { Assert.Throws<InvalidOperationException>(() => fixture.Session.AcceptServerError(stale, wire, error, false)); }
            finally { SetField(fixture.Session, "activeInvocation", original); }
        }
        Assert.False(error.HasVerifiedServerResponse);
        Assert.False(original.Completed);
        Assert.Equal(DmCancelSource.None, original.TerminalCause);
        wire.Complete(); wire.Dispose();
    }

    [Fact]
    public void WrongOwnerCannotAcceptReceiptForAnotherSession()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        var other = new DmSession();
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var wire = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        Assert.Throws<InvalidOperationException>(() => other.AcceptServerError(invocation, wire, error, false));
        Assert.False(error.HasVerifiedServerResponse);
        Assert.Equal(DmCancelSource.None, invocation.TerminalCause);
        wire.Complete(); wire.Dispose();
    }

    [Fact]
    public void DisposedWrongWireCannotMarkReceiptOrCompleteCurrentWire()
    {
        using var fixture = new ErrorFixture("8.1.4.6", false);
        using var lease = fixture.Session.BeginExecution(DmOperationPurpose.Query);
        using var invocation = lease.BeginInvocation();
        var old = fixture.Session.BeginWireExchange();
        old.Complete(); old.Dispose();
        var current = fixture.Session.BeginWireExchange();
        var error = new DmException(c.ReadCompleteErrorBody(Response(-6602, new byte[16]), "UTF-8"));
        Assert.Throws<InvalidOperationException>(() => fixture.Session.AcceptServerError(invocation, old, error, false));
        Assert.False(error.HasVerifiedServerResponse);
        Assert.False(invocation.Completed);
        Assert.Same(current, DmWireExchange.Current);
        current.Complete(); current.Dispose();
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    public async Task PublicPreparePreservesServerMetadataAndReportsConnectionLostWhenCleanupFails(int entry, bool failClose)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new StatementErrorChannel(-2007, failCloseSend: failClose);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, channel);
            candidate.m_ConnInst.ConnProperty.ServerVersion = "8.1.5.60";
            candidate.m_ConnInst.ConnProperty.ServerEncoding = "UTF-8";
            return ValueTask.CompletedTask;
        };
        using var connection = source.OpenConnection();
        using var command = new DmCommand("SELECT SYNTHETIC_INVALID FROM DUAL", connection);
        DmException error = entry == 2 ? await Assert.ThrowsAsync<DmException>(() => command.PrepareAsync()) :
            Assert.Throws<DmException>(() => { if (entry == 0) command.Prepare(); else command.PrepareInternal(false); });
        Assert.NotNull(error.FailureInfo);
        Assert.Equal(-2007, error.Number);
        Assert.Equal(-2007, error.FailureInfo.ServerErrorNumber);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.Equal(DmFailurePhase.Receive, error.FailureInfo.Phase);
        Assert.Equal(!failClose, error.FailureInfo.ConnectionReusable);
        Assert.Equal(failClose, channel.IsClosed);
        Assert.Equal(!failClose ? ConnectionState.Open : entry == 2 ? ConnectionState.Broken : ConnectionState.Closed, connection.State);
        Assert.Equal(new short[] { 3, 5, 4 }, channel.Opcodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualDataSourceShortcutKeepsServerFailureAndClosesTransportBeforeReturningPermit(bool asynchronous)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new StatementErrorChannel(-6602);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, channel);
            candidate.m_ConnInst.ConnProperty.ServerVersion = "8.1.4.6";
            candidate.m_ConnInst.ConnProperty.ServerEncoding = "UTF-8";
            return ValueTask.CompletedTask;
        };
        using var command = source.CreateCommand("INSERT INTO SYNTHETIC VALUES (1)");
        DmException error = asynchronous ? await Assert.ThrowsAsync<DmException>(() => command.ExecuteNonQueryAsync()) :
            Assert.Throws<DmException>(() => command.ExecuteNonQuery());
        Assert.True(channel.IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.Equal(DmErrorKind.Server, error.FailureInfo.ErrorKind);
        Assert.Equal(DmOperationOutcome.ServerReported, error.FailureInfo.OperationOutcome);
        Assert.Equal(-6602, error.FailureInfo.ServerErrorNumber);
        Assert.False(error.FailureInfo.ConnectionReusable);
        Assert.Equal(asynchronous ? 0 : 2, channel.SyncSends);
        Assert.Equal(asynchronous ? 2 : 0, channel.AsyncSends);
        Assert.Equal(new short[] { 3, asynchronous ? (short)5 : (short)91 }, channel.Opcodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptimizedSynchronousExecutionSendFailurePropagatesWithoutReplayAndReturnsCapacityAfterAbort(bool internalNonReader)
    {
        using var hook = new SyntheticPoolHandshake();
        using var source = new DmDataSource(PoolApiSettings.Text);
        var channel = new StatementErrorChannel(-6602, failExecutionSend: true);
        DmPendingOpenTestHooks.Handshake = (candidate, _, _) =>
        {
            hook.Install(candidate, channel);
            candidate.m_ConnInst.ConnProperty.ServerVersion = "8.1.4.6";
            candidate.m_ConnInst.ConnProperty.ServerEncoding = "UTF-8";
            return ValueTask.CompletedTask;
        };
        IOException error;
        if (internalNonReader)
        {
            using var connection = source.OpenConnection();
            using var command = new DmCommand("INSERT INTO SYNTHETIC VALUES (1)", connection);
            using var lease = connection.BeginExecution(DmOperationPurpose.Query);
            error = Assert.Throws<IOException>(() => command.ExecuteInternalNonQuery(lease));
            Assert.True(channel.IsClosed);
            connection.Close();
        }
        else
        {
            using var command = source.CreateCommand("INSERT INTO SYNTHETIC VALUES (1)");
            // The public boundary wraps the transport failure and keeps it as InnerException.
            var wrapped = Assert.Throws<DmException>(() => command.ExecuteNonQuery());
            error = Assert.IsType<IOException>(wrapped.InnerException);
            Assert.Equal(DmErrorKind.Transport, wrapped.ErrorKind);
            Assert.Equal("WDM_TRANSPORT", wrapped.DriverErrorCode);
            Assert.Equal(DmOperationOutcome.Unknown, wrapped.FailureInfo.OperationOutcome);
            Assert.False(wrapped.FailureInfo.ConnectionReusable);
            Assert.Equal(0, wrapped.Number);
        }
        Assert.Same(channel.SendFailure, error);
        Assert.Equal(new short[] { 3, 91 }, channel.Opcodes);
        Assert.Equal(2, channel.SyncSends);
        Assert.Equal(0, channel.AsyncSends);
        Assert.True(channel.IsClosed);
        Assert.Equal(0, source.Snapshot.PhysicalCount);
        Assert.NotNull(channel.ExecutionInvocation);
        var info = channel.ExecutionInvocation!.CreateFailureInfo(error);
        Assert.Equal(DmErrorKind.Transport, info.ErrorKind);
        Assert.Equal(DmOperationOutcome.Unknown, info.OperationOutcome);
        Assert.False(info.ConnectionReusable);
        Assert.Null(info.ServerErrorNumber);
    }

    private static async Task<DmException> CaptureAsync(bool asynchronous, b response, ErrorFixture fixture) =>
        asynchronous ? await Assert.ThrowsAsync<DmException>(() => c.AAsync(response, fixture.Statement, fixture.Instance.ConnProperty)) :
            Assert.Throws<DmException>(() => c.A(response, fixture.Statement, fixture.Instance.ConnProperty));
    private static byte[] Body(params string[] values)
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, Encoding.UTF8, true))
            foreach (string value in values) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        return body.ToArray();
    }
    private static b Response(int number, byte[] body)
    { var response = new b(Frame(5, number, body)); response.G(64); return response; }
    private static byte[] Frame(short opcode, int number, byte[] body)
    {
        byte[] frame = new byte[64 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 1);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(6), body.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(10), number);
        body.CopyTo(frame, 64);
        for (int i = 0; i < 19; i++) frame[19] ^= frame[i];
        return frame;
    }
    private static void SetField(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);

    private sealed class ErrorFixture : IDisposable
    {
        internal readonly SyntheticPoolHandshake Handshake = new();
        internal readonly DmDataSource Source = new(PoolApiSettings.Text);
        internal readonly DmConnection Connection;
        internal readonly DmCommand Command;
        internal readonly Statement Statement;
        internal DmSession Session => Connection.Session ?? captured;
        private readonly DmSession captured;
        internal DmConnInstance Instance => Connection.m_ConnInst ?? instance;
        private readonly DmConnInstance instance;
        internal ErrorFixture(string version, bool transaction)
        {
            Connection = Source.OpenConnection();
            captured = Connection.Session;
            instance = Connection.m_ConnInst;
            instance.ConnProperty.ServerVersion = version;
            instance.ConnProperty.ServerEncoding = "UTF-8";
            if (transaction)
            {
                Session.SetTransactionState(DmLocalTransactionState.Starting);
                using var lease = Session.BeginExecution(DmOperationPurpose.TransactionControl);
                using var invocation = lease.BeginInvocation();
                var tx = new DmTransaction(instance);
                instance.Transaction = tx;
                Session.ActivateTransaction(tx, invocation.Identity);
            }
            Command = new DmCommand("synthetic", Connection);
            Statement = (Statement)RuntimeHelpers.GetUninitializedObject(typeof(Statement));
            SetField(Statement, "__t02_field_04000923", instance);
            SetField(Statement, "__t02_field_04000924", instance.GetCsi());
            SetField(Statement, "__t02_field_04000933", Command);
            SetField(Statement, "__t02_field_04000925", new b());
            SetField(Statement, "__t02_field_04000926", new b());
            SetField(Statement, "__t02_field_04000927", new DmInfo(instance));
        }
        public void Dispose()
        { Command.Dispose(); Connection.Dispose(); Source.Dispose(); Handshake.Dispose(); }
    }

    private sealed class StatementErrorChannel(int number, bool failExecutionSend = false, bool failCloseSend = false) : IDmByteChannel
    {
        private byte[] reply = [];
        private int position;
        internal int SyncSends, AsyncSends;
        internal readonly List<short> Opcodes = [];
        internal readonly IOException SendFailure = new("Synthetic execution send failed.");
        internal DmInvocation? ExecutionInvocation;
        public bool IsClosed { get; private set; }
        private int SendCore(byte[] buffer, int offset, int count)
        {
            short opcode = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset + 4));
            Opcodes.Add(opcode);
            if (opcode != 3)
            {
                ExecutionInvocation = DmInvocation.Current;
                if (failExecutionSend || opcode == 4 && failCloseSend) throw SendFailure;
            }
            reply = Frame(opcode, opcode is 3 or 4 ? 0 : number, opcode is 3 or 4 ? [] : new byte[16]);
            position = 0;
            return count;
        }
        private int ReadCore(byte[] buffer, int offset, int count)
        {
            int read = Math.Min(count, reply.Length - position);
            reply.AsSpan(position, read).CopyTo(buffer.AsSpan(offset, read));
            position += read;
            return read;
        }
        public int Send(byte[] buffer, int offset, int count, int timeout) { SyncSends++; return SendCore(buffer, offset, count); }
        public int Receive(byte[] buffer, int offset, int count, int timeout) => ReadCore(buffer, offset, count);
        public ValueTask<int> SendAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); AsyncSends++; return ValueTask.FromResult(SendCore(buffer, offset, count)); }
        public ValueTask<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(ReadCore(buffer, offset, count)); }
        public void Dispose() => IsClosed = true;
    }

    private sealed class SilentDeadlineClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        internal void Advance(TimeSpan amount) => timestamp += amount.Ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new SilentTimer();
        private sealed class SilentTimer : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
