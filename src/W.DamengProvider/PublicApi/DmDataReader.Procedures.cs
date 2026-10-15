using System;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Sessions;

namespace W.Dm;

public partial class DmDataReader
{
    private DmCommandPlan procedurePlan;
    private DmDataReader procedureParent;
    private DmDataReader[] procedureChildren = Array.Empty<DmDataReader>();
    private bool procedureFailed;

    private bool ProcedureParentInvalid => procedureParent != null &&
        (procedureParent.do_IsClosed || procedureParent.procedureFailed);

    internal void AttachProcedureExecution(DmCommandPlan plan, DmDataReader[] children)
    {
        procedurePlan = plan ?? throw new ArgumentNullException(nameof(plan));
        procedureChildren = children;
        foreach (var child in children) child.procedureParent = this;
    }

    private void MarkProcedureFailure()
    {
        procedureFailed = true;
        procedurePlan?.FailOutputs();
        procedureParent?.MarkProcedureFailure();
    }

    private void AbandonProcedureReaderState()
    {
        if (procedurePlan == null && procedureParent == null) return;
        m_IsClosed = true;
        foreach (var child in procedureChildren) child.AbandonProcedureReaderState();
        try { InvalidateLobFlows(); } catch { /* Preserve the original close failure. */ }
        m_Statement?.o();
        m_Statement = null;
        m_DbInfo = null;
        m_ColInfo = null;
        m_RsCache = null;
        m_CurrentRow = -1;
    }

    private void CheckProcedureCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) return;
        MarkProcedureFailure();
        token.ThrowIfCancellationRequested();
    }

    internal void PublishProcedureOutputsIfTerminal()
    {
        // Cursor outputs remain alive until the parent is closed. A parent with no
        // ordinary rowsets is not proof that its child cursors have completed.
        if (procedurePlan != null && !procedureFailed && procedureChildren.Length == 0 &&
            resultCursor.IsTerminal && ownsExecutionLease)
            procedurePlan.PublishOutputs();
    }

    private void PublishProcedureOutputsAfterClose()
    {
        if (procedurePlan != null && !procedureFailed && resultCursor.IsTerminal)
            procedurePlan.PublishOutputs();
    }

    private void FinishProcedureResultsOwned()
    {
        if (procedurePlan == null || procedureFailed || m_IsClosed) return;
        var lease = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
        if (lease == null || !ReferenceEquals(DmInvocation.Current?.Lease, lease) ||
            !lease.Session.IsCurrent(lease.Identity.SessionId, lease.Identity.LeaseGeneration))
        {
            MarkProcedureFailure();
            return;
        }
        // initialSeek ignores SingleResult, which limits presentation, not cleanup.
        while (!resultCursor.IsTerminal) AdvanceToReadableResultOwned(initialSeek: true);
    }

    private async Task FinishProcedureResultsOwnedAsync()
    {
        if (procedurePlan == null || procedureFailed || m_IsClosed) return;
        var lease = Volatile.Read(ref executionLease) ?? DmInvocation.Current?.Lease;
        if (lease == null || !ReferenceEquals(DmInvocation.Current?.Lease, lease) ||
            !lease.Session.IsCurrent(lease.Identity.SessionId, lease.Identity.LeaseGeneration))
        {
            MarkProcedureFailure();
            return;
        }
        while (!resultCursor.IsTerminal)
            await AdvanceToReadableResultOwnedAsync(true, CancellationToken.None).ConfigureAwait(false);
    }

    private void CloseProcedureChildrenOwned()
    {
        foreach (var child in procedureChildren)
        {
            try { child.do_Close(); }
            catch { MarkProcedureFailure(); throw; }
        }
    }

    private async Task CloseProcedureChildrenOwnedAsync()
    {
        foreach (var child in procedureChildren)
        {
            try { await child.CloseAsync().ConfigureAwait(false); }
            catch { MarkProcedureFailure(); throw; }
        }
    }
}
