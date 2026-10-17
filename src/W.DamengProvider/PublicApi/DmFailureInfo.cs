using System;
using System.Threading;

namespace W.Dm;

/// <summary>Stable, credential-free information about an operation failure.</summary>
public sealed class DmFailureInfo
{
    public DmErrorKind ErrorKind { get; }
    public DmFailurePhase Phase { get; }
    public string ErrorCode { get; }
    public DmOperationOutcome OperationOutcome { get; }
    public DmTransactionOutcome? TransactionOutcome { get; }
    public bool ConnectionReusable { get; }
    public DmCancelSource CancelSource { get; }
    public int? ServerErrorNumber { get; }
    internal DmFailureInfo WithCommitUnknown() => new(DmErrorKind.OutcomeUnknown, DmFailurePhase.Commit,
        "WDM_COMMIT_UNKNOWN", DmOperationOutcome.Unknown, DmTransactionOutcome.OutcomeUnknown, false, CancelSource, ServerErrorNumber);
    // A lost session cannot confirm an in-flight local transaction's final state.
    internal DmFailureInfo WithConnectionLost() => new(ErrorKind, Phase, ErrorCode, OperationOutcome,
        TransactionOutcome is DmTransactionOutcome.Active or DmTransactionOutcome.Committing or
            DmTransactionOutcome.RollingBack ||
            (TransactionOutcome == DmTransactionOutcome.Starting && OperationOutcome != DmOperationOutcome.NotSent)
            ? DmTransactionOutcome.OutcomeUnknown : TransactionOutcome,
        false, CancelSource, ServerErrorNumber);

    internal DmFailureInfo(DmErrorKind errorKind, DmFailurePhase phase, string errorCode,
        DmOperationOutcome outcome, DmTransactionOutcome? transactionOutcome, bool reusable,
        DmCancelSource cancelSource = DmCancelSource.None, int? serverErrorNumber = null)
    {
        ErrorKind = errorKind; Phase = phase; ErrorCode = errorCode; OperationOutcome = outcome;
        TransactionOutcome = transactionOutcome; ConnectionReusable = reusable;
        CancelSource = cancelSource; ServerErrorNumber = serverErrorNumber;
    }
}

public enum DmErrorKind { Unknown, Timeout, Canceled, Transport, Server, OutcomeUnknown }
public enum DmFailurePhase { Unknown, Dns, Connect, Authenticate, Prepare, Send, Receive, Fetch, Commit, Rollback, Cleanup, PoolWait }
public enum DmOperationOutcome { NotSent, ServerReported, Unknown }
/// <summary>What ended an operation early.</summary>
public enum DmCancelSource
{
    /// <summary>The operation was not canceled and no client deadline expired.</summary>
    None,
    /// <summary>The CancellationToken passed to the asynchronous call (including OpenAsync) was canceled.</summary>
    User,
    /// <summary><see cref="DmCommand.Cancel"/> (or the data source command's Cancel) was called while the command ran.</summary>
    Command,
    /// <summary>
    /// The operation's absolute deadline expired: CommandTimeout for commands, readers and transaction
    /// control, Connection Timeout for Open, or Cleanup Timeout for cleanup. Reported as a timeout.
    /// </summary>
    TotalDeadline,
    /// <summary>Reserved for a per-read idle deadline; this release does not report it.</summary>
    IdleTimeout
}

/// <summary>A client deadline expired. Number remains zero because no server error is invented.</summary>
public sealed class DmTimeoutException : DmException
{
    internal DmTimeoutException(DmFailureInfo info, Exception inner = null)
        : base("The operation deadline expired.", inner) { SetFailureInfo(info); }
#if NETSTANDARD2_1
    public bool IsTransient => false;
#else
    public override bool IsTransient => false;
#endif
}

/// <summary>A canceled operation with its original token and stable failure metadata.</summary>
public sealed class DmOperationCanceledException : OperationCanceledException
{
    private DmFailureInfo failureInfo;
    public DmFailureInfo FailureInfo => Volatile.Read(ref failureInfo);
    internal void SetFailureInfo(DmFailureInfo info) => Volatile.Write(ref failureInfo, info);
    internal DmOperationCanceledException(DmFailureInfo info, CancellationToken token, Exception inner = null)
        : base("The operation was canceled.", inner, token) { failureInfo = info; }
}
