using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;

namespace W.Dm.Internal.Execution;

/// <summary>
/// One run of a <see cref="DmBatch"/>: frozen step plans, the lease that owns the connection
/// for the whole run, and the position of the next step. Steps execute strictly in order.
/// </summary>
internal sealed class DmBatchExecution : IDisposable
{
    private readonly DmBatch owner;
    private readonly List<DmCommandPlanGate> enteredGates = new();
    private DmBatchCommand[] sources = Array.Empty<DmBatchCommand>();
    private DmCommand[] steps = Array.Empty<DmCommand>();
    private DmCommandPlan[] plans = Array.Empty<DmCommandPlan>();
    private DmExecutionLease lease;
    private int next;
    private int cancelRequested;
    private int disposed;

    internal DmBatchExecution(DmBatch owner) => this.owner = owner;

    internal DmConnection Connection { get; private set; }
    internal bool HasNext => next < steps.Length;
    /// <summary>False once the batch was canceled or its connection can no longer run a step.</summary>
    internal bool CanContinue => Volatile.Read(ref cancelRequested) == 0 && Connection?.State == ConnectionState.Open;

    internal void Capture(DmBatchCommand[] commands, DmConnection connection, DmTransaction transaction)
    {
        if (commands.Length == 0) throw new InvalidOperationException("Batch has no commands.");
        Connection = connection;
        sources = commands;
        var seen = new HashSet<DmCommandPlanGate>(ReferenceEqualityComparer.Instance);
        foreach (DmBatchCommand command in commands)
        {
            if (!seen.Add(command.Gate)) continue; // The same command may appear twice.
            command.Gate.EnterExecution(nameof(DmBatchCommand));
            enteredGates.Add(command.Gate);
        }
        foreach (DmBatchCommand command in commands) command.SetRecordsAffected(-1);
        steps = new DmCommand[commands.Length];
        plans = new DmCommandPlan[commands.Length];
        for (int index = 0; index < commands.Length; index++)
        {
            DmBatchCommand source = commands[index];
            steps[index] = DmCommand.ForBatchStep(source.CommandText, source.CommandType, source.Parameters, connection, transaction);
            plans[index] = steps[index].EnterBatchPlan();
            // Transaction-control text is rejected for every step before the first one runs.
            if (connection != null)
                DmTransaction.ValidateCommandSql(connection, transaction, plans[index].Sql, plans[index].Binding.MarkerCount != 0);
        }
    }

    internal void Acquire(DmExecutionLease acquired)
    {
        lease = acquired;
        if (Volatile.Read(ref cancelRequested) != 0) acquired.Cancel();
    }

    internal void Cancel()
    {
        Interlocked.Exchange(ref cancelRequested, 1);
        Volatile.Read(ref lease)?.Cancel();
    }

    internal int RunNonQuery()
    {
        var total = new AffectedTotal();
        while (HasNext)
        {
            int index = next++;
            try { total.Add(sources[index], steps[index].ExecuteBatchNonQuery(TakePlan(index), lease)); }
            catch (DmException error) { error.SetBatchCommand(sources[index]); throw; }
        }
        return total.Value;
    }

    internal async Task<int> RunNonQueryAsync(CancellationToken cancellationToken)
    {
        var total = new AffectedTotal();
        while (HasNext)
        {
            int index = next++;
            try
            {
                total.Add(sources[index], await steps[index].ExecuteBatchNonQueryAsync(TakePlan(index), lease,
                    cancellationToken).ConfigureAwait(false));
            }
            catch (DmException error) { error.SetBatchCommand(sources[index]); throw; }
        }
        return total.Value;
    }

    internal (DmDataReader Reader, DmBatchCommand Source) ExecuteNextReader(CommandBehavior behavior)
    {
        int index = next++;
        try { return (steps[index].ExecuteBatchReader(TakePlan(index), lease, behavior), sources[index]); }
        catch (DmException error) { error.SetBatchCommand(sources[index]); throw; }
    }

    internal async Task<(DmDataReader Reader, DmBatchCommand Source)> ExecuteNextReaderAsync(CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        int index = next++;
        try
        {
            return (await steps[index].ExecuteBatchReaderAsync(TakePlan(index), lease, behavior, cancellationToken)
                .ConfigureAwait(false), sources[index]);
        }
        catch (DmException error) { error.SetBatchCommand(sources[index]); throw; }
    }

    private DmCommandPlan TakePlan(int index)
    {
        if (lease == null) throw new InvalidOperationException("Batch execution has no connection lease.");
        // A Cancel that landed between steps (no invocation to terminate) marks the lease
        // again, so the next step's invocation reports cancellation instead of running.
        if (Volatile.Read(ref cancelRequested) != 0) lease.Cancel();
        DmCommandPlan plan = plans[index];
        plans[index] = null; // The step releases it from here on.
        return plan;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { lease?.Dispose(); }
        finally
        {
            // Unstarted plans and step commands are released only after the lease, so
            // no cleanup can compete with the batch for the connection.
            for (int index = 0; index < plans.Length; index++)
                if (plans[index] is { } plan) steps[index].ReleaseBatchPlan(plan);
            foreach (DmCommand step in steps) step?.Dispose();
            foreach (DmCommandPlanGate gate in enteredGates) gate.Exit();
            owner.Release(this);
        }
    }

    /// <summary>Sum of counts reported by completed steps; -1 when no step reported one.</summary>
    internal struct AffectedTotal
    {
        private long total;
        private bool known;

        internal void Add(DmBatchCommand source, int affected)
        {
            source.SetRecordsAffected(affected);
            if (affected < 0) return;
            total = checked(total + affected);
            known = true;
        }

        internal readonly int Value => !known ? -1 : total > int.MaxValue
            ? throw new OverflowException("Affected row count exceeds Int32.") : (int)total;
    }
}
