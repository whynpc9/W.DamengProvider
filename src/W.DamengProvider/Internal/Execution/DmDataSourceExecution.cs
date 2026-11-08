using System;
using System.Threading;
using System.Threading.Tasks;

namespace W.Dm.Internal.Execution;

/// <summary>Cancellation belongs to one shortcut call, including its open and reader lifetime.</summary>
internal sealed class DmDataSourceExecution : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource openCancellation = new();
    private readonly CancellationToken callerToken;
    private readonly CancellationToken openToken;
    private CancellationTokenRegistration callerRegistration;
    private DmCommandPlan plan;
#if !NETSTANDARD2_1
    private DmBatchExecution batch;
#endif
    private bool commandCanceled, retired;
    private int cancellationCalls;
    private int openCancelSource;
    internal DmConnection Connection { get; set; }

    internal DmDataSourceExecution(CancellationToken callerToken)
    {
        this.callerToken = callerToken;
        openToken = openCancellation.Token;
        callerRegistration = callerToken.Register(() => RequestCancellation(user: true));
    }

    internal void RequestCancellation() => RequestCancellation(user: false);

    private void RequestCancellation(bool user)
    {
        DmCommandPlan captured;
#if !NETSTANDARD2_1
        DmBatchExecution capturedBatch;
#endif
        lock (gate)
        {
            if (retired) return;
            if (!user) commandCanceled = true;
            Interlocked.CompareExchange(ref openCancelSource, (int)(user ? DmCancelSource.User : DmCancelSource.Command), 0);
            captured = user ? null : plan;
#if !NETSTANDARD2_1
            capturedBatch = user ? null : batch;
#endif
            cancellationCalls++;
        }
        try
        {
            // CTS callbacks and a plan's transport abort may block. Neither runs
            // under a lifecycle/state lock, nor can either target a later call.
            try { openCancellation.Cancel(); }
            finally
            {
                captured?.RequestCancellation();
#if !NETSTANDARD2_1
                capturedBatch?.Cancel();
#endif
            }
        }
        finally
        {
            bool dispose;
            lock (gate) dispose = --cancellationCalls == 0 && retired;
            if (dispose) openCancellation.Dispose();
        }
    }

    internal void BindPlan(DmCommandPlan captured)
    {
        bool cancel;
        lock (gate)
        {
            if (retired) return;
            plan = captured;
            cancel = commandCanceled;
        }
        if (cancel) captured.RequestCancellation();
    }

#if !NETSTANDARD2_1
    // Batch execution and reader calls already receive their own caller tokens.
    internal void StopObservingCallerForOpen() => callerRegistration.Dispose();

    internal void BindBatch(DmBatchExecution captured)
    {
        bool cancel;
        lock (gate)
        {
            if (retired) return;
            batch = captured;
            cancel = commandCanceled;
        }
        if (cancel) captured.Cancel();
    }
#endif

    internal void Open()
    {
        try { Connection.OpenForDataSource(openToken); }
        catch (Exception error) { throw TranslateOpenFailure(error); }
    }

    internal async Task OpenAsync()
    {
        try { await Connection.OpenAsync(openToken).ConfigureAwait(false); }
        catch (Exception error) { throw TranslateOpenFailure(error); }
    }

    private Exception TranslateOpenFailure(Exception error)
    {
        DmCancelSource source = (DmCancelSource)Volatile.Read(ref openCancelSource);
        if (source == DmCancelSource.None) return error;
        CancellationToken token = source == DmCancelSource.User ? callerToken : openToken;
        if (error is DmOperationCanceledException canceled && canceled.CancellationToken == openToken &&
            canceled.FailureInfo.CancelSource == DmCancelSource.User)
        {
            DmFailureInfo old = canceled.FailureInfo;
            var info = new DmFailureInfo(old.ErrorKind, old.Phase, old.ErrorCode, old.OperationOutcome,
                old.TransactionOutcome, old.ConnectionReusable, source, old.ServerErrorNumber);
            return new DmOperationCanceledException(info, token, canceled);
        }
        // Cancellation before InstallPending has no physical operation or phase.
        if (error is OperationCanceledException early && error is not DmOperationCanceledException &&
            early.CancellationToken == openToken)
            return new DmOperationCanceledException(new DmFailureInfo(DmErrorKind.Canceled, DmFailurePhase.PoolWait,
                "WDM_POOL_WAIT_CANCELED", DmOperationOutcome.NotSent, null, false, source), token, early);
        return error;
    }

    public void Dispose()
    {
        bool dispose;
        lock (gate)
        {
            if (retired) return;
            retired = true;
            plan = null;
#if !NETSTANDARD2_1
            batch = null;
#endif
            dispose = cancellationCalls == 0;
        }
        // Unregister waits for an already-running caller callback outside gate.
        callerRegistration.Dispose();
        if (dispose) openCancellation.Dispose();
    }
}
