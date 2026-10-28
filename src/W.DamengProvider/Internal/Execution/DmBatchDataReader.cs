using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace W.Dm.Internal.Execution;

/// <summary>
/// Presents each batch step's rowsets in command order. A step executes only when the
/// previous step's results are exhausted; steps without a rowset contribute their counts.
/// Closing the reader still executes the remaining steps unless an earlier step failed.
/// </summary>
internal sealed class DmBatchDataReader : DbDataReader
{
    private readonly DmBatchExecution execution;
    private readonly CommandBehavior behavior;
    private DmBatchExecution.AffectedTotal affected;
    private DmDataReader current;
    private DmBatchCommand currentSource;
    private bool closed;
    private bool faulted;

    internal DmBatchDataReader(DmBatchExecution execution, CommandBehavior behavior)
    {
        this.execution = execution;
        this.behavior = behavior;
    }

    private CommandBehavior StepBehavior => behavior & CommandBehavior.SequentialAccess;

    private DmDataReader Current => !closed && current != null ? current
        : throw new InvalidOperationException(closed ? "Batch reader is closed." : "Batch reader has no current result set.");

    internal void Start() => Guard(() => AdvanceToRowset());

    internal Task StartAsync(CancellationToken cancellationToken) => GuardAsync(() => AdvanceToRowsetAsync(cancellationToken));

    /// <summary>Releases the batch after a failed start without executing later steps.</summary>
    internal void Abandon()
    {
        faulted = true;
        try { Close(); } catch { /* Preserve the start failure. */ }
    }

    private bool AdvanceToRowset()
    {
        while (execution.HasNext)
        {
            var (reader, source) = execution.ExecuteNextReader(StepBehavior);
            current = reader; currentSource = source;
            if (reader.FieldCount > 0) return true;
            current = null;
            CompleteStep(reader, source);
        }
        return false;
    }

    private async Task<bool> AdvanceToRowsetAsync(CancellationToken cancellationToken)
    {
        while (execution.HasNext)
        {
            var (reader, source) = await execution.ExecuteNextReaderAsync(StepBehavior, cancellationToken).ConfigureAwait(false);
            current = reader; currentSource = source;
            if (reader.FieldCount > 0) return true;
            current = null;
            await CompleteStepAsync(reader, source).ConfigureAwait(false);
        }
        return false;
    }

    private void CompleteStep(DmDataReader reader, DmBatchCommand source)
    {
        int count = reader.RecordsAffected;
        try { reader.Close(); }
        catch (DmException error) { error.SetBatchCommand(source); throw; }
        affected.Add(source, count);
    }

    private async Task CompleteStepAsync(DmDataReader reader, DmBatchCommand source)
    {
        int count = reader.RecordsAffected;
        try { await reader.CloseAsync().ConfigureAwait(false); }
        catch (DmException error) { error.SetBatchCommand(source); throw; }
        affected.Add(source, count);
    }

    private T Guard<T>(Func<T> action)
    {
        try { return action(); }
        catch (DmException error) { faulted = true; if (currentSource != null) error.SetBatchCommand(currentSource); throw; }
        catch { faulted = true; throw; }
    }

    private async Task<T> GuardAsync<T>(Func<Task<T>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (DmException error) { faulted = true; if (currentSource != null) error.SetBatchCommand(currentSource); throw; }
        catch { faulted = true; throw; }
    }

    public override bool Read()
    {
        ThrowIfUnusable();
        return current != null && Guard(() => current.Read());
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnusable();
        return current != null ? GuardAsync(() => current.ReadAsync(cancellationToken)) : Task.FromResult(false);
    }

    // After any failure the remaining steps never run (stop at the first failure).
    private void ThrowIfUnusable()
    {
        if (closed) throw new InvalidOperationException("Batch reader is closed.");
        if (faulted) throw new InvalidOperationException("Batch reader stopped after an earlier failure; close it.");
    }

    public override bool NextResult()
    {
        ThrowIfUnusable();
        if (current == null) return false;
        return Guard(() =>
        {
            if (current.NextResult()) return true;
            DmDataReader finished = current;
            current = null;
            CompleteStep(finished, currentSource);
            return AdvanceToRowset();
        });
    }

    public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnusable();
        if (current == null) return Task.FromResult(false);
        return GuardAsync(async () =>
        {
            if (await current.NextResultAsync(cancellationToken).ConfigureAwait(false)) return true;
            DmDataReader finished = current;
            current = null;
            await CompleteStepAsync(finished, currentSource).ConfigureAwait(false);
            return await AdvanceToRowsetAsync(cancellationToken).ConfigureAwait(false);
        });
    }

    public override void Close()
    {
        if (closed) return;
        closed = true;
        Exception failure = null;
        try
        {
            if (current != null)
            {
                DmDataReader finished = current;
                current = null;
                CompleteStep(finished, currentSource);
            }
            while (!faulted && execution.CanContinue && execution.HasNext)
            {
                var (reader, source) = execution.ExecuteNextReader(StepBehavior);
                CompleteStep(reader, source);
            }
        }
        catch (Exception error) { failure = error; }
        finally { Finish(); }
        if (failure != null) ExceptionDispatchInfo.Throw(failure);
    }

    public override async Task CloseAsync()
    {
        if (closed) return;
        closed = true;
        Exception failure = null;
        try
        {
            if (current != null)
            {
                DmDataReader finished = current;
                current = null;
                await CompleteStepAsync(finished, currentSource).ConfigureAwait(false);
            }
            while (!faulted && execution.CanContinue && execution.HasNext)
            {
                var (reader, source) = await execution.ExecuteNextReaderAsync(StepBehavior, CancellationToken.None).ConfigureAwait(false);
                await CompleteStepAsync(reader, source).ConfigureAwait(false);
            }
        }
        catch (Exception error) { failure = error; }
        finally { Finish(); }
        if (failure != null) ExceptionDispatchInfo.Throw(failure);
    }

    private void Finish()
    {
        if (current != null)
        {
            try { current.Close(); } catch { /* The batch failure is reported. */ }
            current = null;
        }
        DmConnection connection = execution.Connection;
        execution.Dispose();
        if ((behavior & CommandBehavior.CloseConnection) != 0) connection?.Close();
    }

    public override ValueTask DisposeAsync() => new(CloseAsync());

    public override int RecordsAffected => affected.Value;
    public override int Depth => 0;
    public override bool IsClosed => closed;
    public override int FieldCount => closed ? throw new InvalidOperationException("Batch reader is closed.") : current?.FieldCount ?? 0;
    public override int VisibleFieldCount => FieldCount;
    public override bool HasRows => !closed && current != null && current.HasRows;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool GetBoolean(int ordinal) => Current.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => Current.GetByte(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) =>
        Current.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => Current.GetChar(ordinal);
    public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) =>
        Current.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
    public override string GetDataTypeName(int ordinal) => Current.GetDataTypeName(ordinal);
    public override DateTime GetDateTime(int ordinal) => Current.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => Current.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => Current.GetDouble(ordinal);
    public override Type GetFieldType(int ordinal) => Current.GetFieldType(ordinal);
    public override Type GetProviderSpecificFieldType(int ordinal) => Current.GetProviderSpecificFieldType(ordinal);
    public override object GetProviderSpecificValue(int ordinal) => Current.GetProviderSpecificValue(ordinal);
    public override int GetProviderSpecificValues(object[] values) => Current.GetProviderSpecificValues(values);
    public override float GetFloat(int ordinal) => Current.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => Current.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => Current.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => Current.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => Current.GetInt64(ordinal);
    public override string GetName(int ordinal) => Current.GetName(ordinal);
    public override int GetOrdinal(string name) => Current.GetOrdinal(name);
    public override string GetString(int ordinal) => Current.GetString(ordinal);
    public override object GetValue(int ordinal) => Current.GetValue(ordinal);
    public override int GetValues(object[] values) => Current.GetValues(values);
    public override bool IsDBNull(int ordinal) => Current.IsDBNull(ordinal);
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
        Current.IsDBNullAsync(ordinal, cancellationToken);
    public override T GetFieldValue<T>(int ordinal) => Current.GetFieldValue<T>(ordinal);
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
        Current.GetFieldValueAsync<T>(ordinal, cancellationToken);
    public override Stream GetStream(int ordinal) => Current.GetStream(ordinal);
    public override TextReader GetTextReader(int ordinal) => Current.GetTextReader(ordinal);
    public override DataTable GetSchemaTable() => Current.GetSchemaTable();
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, (behavior & CommandBehavior.CloseConnection) != 0);
}
