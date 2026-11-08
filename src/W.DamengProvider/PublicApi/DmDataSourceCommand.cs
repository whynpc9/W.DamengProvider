using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Sessions;

namespace W.Dm;

/// <summary>One shortcut command; user parameters survive its successive physical leases.</summary>
internal sealed class DmDataSourceCommand : DbCommand
{
    private readonly DmDataSource source;
    private readonly DmCommand command;
    private DmConnection activeConnection;
    private DmDataSourceReader activeReader;
    private DmDataSourceExecution activeExecution;
    internal static Action AfterOpen;
    internal static Action<DbDataReader> BeforeReaderPublished;
    internal static Action AfterCancelCaptured;
    private int active, disposed;
    private readonly object lifecycleGate = new();
    internal DmDataSourceCommand(DmDataSource source, string text, DmCommand command = null)
    { this.source = source; this.command = command ?? new DmCommand(); this.command.CommandText = text; }
    public override string CommandText { get => command.CommandText; set => command.CommandText = value; }
    public override int CommandTimeout { get => command.CommandTimeout; set => command.CommandTimeout = value; }
    public override CommandType CommandType { get => command.CommandType; set => command.CommandType = value; }
    public override UpdateRowSource UpdatedRowSource { get => command.UpdatedRowSource; set => command.UpdatedRowSource = value; }
    public override bool DesignTimeVisible { get => command.DesignTimeVisible; set => command.DesignTimeVisible = value; }
    protected override DbConnection DbConnection
    { get => null; set => throw new NotSupportedException("A data source command owns its connection."); }
    protected override DbTransaction DbTransaction
    { get => null; set => throw new NotSupportedException("Use a data source connection for transactions."); }
    protected override DbParameterCollection DbParameterCollection => command.Parameters;
    protected override DbParameter CreateDbParameter() => command.CreateParameter();
    public override void Cancel()
    {
        DmDataSourceExecution captured;
        lock (lifecycleGate) captured = activeExecution;
        Volatile.Read(ref AfterCancelCaptured)?.Invoke();
        captured?.RequestCancellation();
    }

    private DmDataSourceExecution Begin(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DmConnection connection = null;
        var execution = new DmDataSourceExecution(token);
        try
        {
            lock (lifecycleGate)
            {
                if (disposed != 0) throw new ObjectDisposedException(nameof(DmDataSourceCommand));
                if (active != 0) throw new InvalidOperationException("A data source command is already executing or owns a reader.");
                // Creation/assignment are local configuration only. Physical Open
                // and every Dispose/reader cleanup stay outside this gate.
                connection = source.CreateConnection();
                command.Connection = connection;
                execution.Connection = connection;
                activeExecution = execution;
                activeConnection = connection;
                active = 1;
                return execution;
            }
        }
        catch { execution.Dispose(); connection?.Dispose(); throw; }
    }

    private DbDataReader PublishReader(DbDataReader reader, DmDataSourceExecution execution)
    {
        Volatile.Read(ref BeforeReaderPublished)?.Invoke(reader);
        lock (lifecycleGate)
        {
            if (disposed != 0) throw new ObjectDisposedException(nameof(DmDataSourceCommand));
            return activeReader = new DmDataSourceReader(reader, () => End(execution), () => EndAsync(execution));
        }
    }

    private void End(DmDataSourceExecution execution)
    {
        try { execution.Connection.Dispose(); }
        finally { execution.Connection.RunAfterPhysicalClose(() => Release(execution)); }
    }
    private async ValueTask EndAsync(DmDataSourceExecution execution)
    {
        try { await execution.Connection.DisposeAsync().ConfigureAwait(false); }
        finally { execution.Connection.RunAfterPhysicalClose(() => Release(execution)); }
    }
    private void Release(DmDataSourceExecution execution)
    {
        bool skipSetter;
        lock (lifecycleGate)
        {
            if (!ReferenceEquals(activeExecution, execution)) return;
            skipSetter = disposed != 0;
        }
        try
        {
            command.ReleaseDataSourcePlan(execution);
            if (!skipSetter)
            {
                try { command.Connection = null; }
                catch (ObjectDisposedException error) when (error.ObjectName == nameof(DmCommand) && Volatile.Read(ref disposed) != 0)
                {
                    // Shortcut disposal retired the same underlying command after
                    // the gate check. It no longer requires connection rebinding.
                }
            }
        }
        finally
        {
            lock (lifecycleGate)
            {
                if (ReferenceEquals(activeExecution, execution))
                { activeExecution = null; activeConnection = null; activeReader = null; active = 0; }
            }
            execution.Dispose();
        }
    }

    private void Open(DmDataSourceExecution execution, bool arrayBindingAllowed, CommandBehavior behavior = CommandBehavior.Default)
    {
        command.CaptureDataSourcePlan(execution, arrayBindingAllowed, behavior);
        execution.Open();
        Volatile.Read(ref AfterOpen)?.Invoke();
    }

    private async ValueTask OpenAsync(DmDataSourceExecution execution, bool arrayBindingAllowed, CancellationToken token,
        CommandBehavior behavior = CommandBehavior.Default)
    {
        command.CaptureDataSourcePlan(execution, arrayBindingAllowed, behavior);
        await execution.OpenAsync().ConfigureAwait(false);
        Volatile.Read(ref AfterOpen)?.Invoke();
        token.ThrowIfCancellationRequested();
    }

    public override int ExecuteNonQuery()
    {
        DmDataSourceExecution execution = Begin(CancellationToken.None);
        Exception primary = null;
        try
        {
            Open(execution, arrayBindingAllowed: true);
            return command.ExecuteNonQueryCore(command.TakeDataSourcePlan(execution));
        }
        catch (Exception error) { primary = error; throw; }
        finally { if (primary == null) End(execution); else try { End(execution); } catch { } }
    }
    public override object ExecuteScalar()
    {
        DmDataSourceExecution execution = Begin(CancellationToken.None);
        Exception primary = null;
        try
        {
            Open(execution, arrayBindingAllowed: false);
            return command.ExecuteScalarCore(command.TakeDataSourcePlan(execution));
        }
        catch (Exception error) { primary = error; throw; }
        finally { if (primary == null) End(execution); else try { End(execution); } catch { } }
    }
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        DmDataSourceExecution execution = Begin(cancellationToken);
        Exception primary = null;
        try
        {
            await OpenAsync(execution, arrayBindingAllowed: true, cancellationToken).ConfigureAwait(false);
            return (int)await command.ExecuteCompleteCoreAsync(false, command.TakeDataSourcePlan(execution),
                batchLease: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary == null) await EndAsync(execution).ConfigureAwait(false);
            else try { await EndAsync(execution).ConfigureAwait(false); } catch { }
        }
    }
    public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        DmDataSourceExecution execution = Begin(cancellationToken);
        Exception primary = null;
        try
        {
            await OpenAsync(execution, arrayBindingAllowed: false, cancellationToken).ConfigureAwait(false);
            return await command.ExecuteCompleteCoreAsync(true, command.TakeDataSourcePlan(execution),
                batchLease: null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary == null) await EndAsync(execution).ConfigureAwait(false);
            else try { await EndAsync(execution).ConfigureAwait(false); } catch { }
        }
    }
    public override void Prepare()
    {
        DmDataSourceExecution execution = Begin(CancellationToken.None);
        Exception primary = null;
        try
        {
            Open(execution, arrayBindingAllowed: true);
            command.PrepareCore(command.TakeDataSourcePlan(execution));
        }
        catch (Exception error) { primary = error; throw; }
        finally { if (primary == null) End(execution); else try { End(execution); } catch { } }
    }
    public override async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        DmDataSourceExecution execution = Begin(cancellationToken);
        Exception primary = null;
        try
        {
            await OpenAsync(execution, arrayBindingAllowed: true, cancellationToken).ConfigureAwait(false);
            await command.PrepareCoreAsync(command.TakeDataSourcePlan(execution), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary == null) await EndAsync(execution).ConfigureAwait(false);
            else try { await EndAsync(execution).ConfigureAwait(false); } catch { }
        }
    }
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        DmDataSourceExecution execution = Begin(CancellationToken.None);
        DbDataReader reader = null;
        try
        {
            Open(execution, arrayBindingAllowed: false, behavior);
            reader = command.ExecuteDbDataReaderCore(behavior, command.TakeDataSourcePlan(execution), batchLease: null);
            return PublishReader(reader, execution);
        }
        catch
        {
            try { reader?.Dispose(); } catch { }
            try { End(execution); } catch { }
            throw;
        }
    }
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        DmDataSourceExecution execution = Begin(cancellationToken);
        DbDataReader reader = null;
        try
        {
            await OpenAsync(execution, arrayBindingAllowed: false, cancellationToken, behavior).ConfigureAwait(false);
            reader = await command.ExecuteDbDataReaderCoreAsync(behavior, command.TakeDataSourcePlan(execution),
                batchLease: null, cancellationToken).ConfigureAwait(false);
            return PublishReader(reader, execution);
        }
        catch
        {
            try { if (reader != null) await reader.DisposeAsync().ConfigureAwait(false); } catch { }
            try { await EndAsync(execution).ConfigureAwait(false); } catch { }
            throw;
        }
    }
    private (DmDataSourceReader Reader, DmConnection Connection, bool Dispose) CaptureDispose()
    {
        lock (lifecycleGate)
        {
            if (disposed != 0) return (null, null, false);
            disposed = 1;
            return (activeReader, activeConnection, true);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var captured = CaptureDispose();
            if (captured.Dispose)
            {
                try { if (captured.Reader != null) captured.Reader.Dispose(); else captured.Connection?.Dispose(); }
                finally { command.Dispose(); }
            }
        }
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        var captured = CaptureDispose();
        if (!captured.Dispose) return;
        try
        {
            if (captured.Reader != null) await captured.Reader.DisposeAsync().ConfigureAwait(false);
            else if (captured.Connection != null) await captured.Connection.DisposeAsync().ConfigureAwait(false);
        }
        finally { await command.DisposeAsync().ConfigureAwait(false); }
        GC.SuppressFinalize(this);
    }
}

/// <summary>Returns the shortcut command's connection even if reader cleanup fails.</summary>
internal sealed class DmDataSourceReader : DbDataReader
{
    private readonly DbDataReader reader;
    private readonly Action closeConnection;
    private readonly Func<ValueTask> closeConnectionAsync;
    private int closed;
    internal DmDataSourceReader(DbDataReader reader, Action closeConnection, Func<ValueTask> closeConnectionAsync)
    { this.reader = reader; this.closeConnection = closeConnection; this.closeConnectionAsync = closeConnectionAsync; }
    public override int Depth => reader.Depth;
    public override int FieldCount => reader.FieldCount;
    public override int VisibleFieldCount => reader.VisibleFieldCount;
    public override bool HasRows => reader.HasRows;
    public override bool IsClosed => Volatile.Read(ref closed) != 0 || reader.IsClosed;
    public override int RecordsAffected => reader.RecordsAffected;
    public override object this[int ordinal] => reader[ordinal];
    public override object this[string name] => reader[name];
    public override bool Read() => reader.Read();
    public override bool NextResult() => reader.NextResult();
    public override Task<bool> ReadAsync(CancellationToken cancellationToken) => reader.ReadAsync(cancellationToken);
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => reader.NextResultAsync(cancellationToken);
    public override bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => reader.GetByte(ordinal);
    public override long GetBytes(int ordinal, long offset, byte[] buffer, int bufferOffset, int length) => reader.GetBytes(ordinal, offset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => reader.GetChar(ordinal);
    public override long GetChars(int ordinal, long offset, char[] buffer, int bufferOffset, int length) => reader.GetChars(ordinal, offset, buffer, bufferOffset, length);
    public override DateTime GetDateTime(int ordinal) => reader.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => reader.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => reader.GetDouble(ordinal);
    public override float GetFloat(int ordinal) => reader.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => reader.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => reader.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => reader.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => reader.GetInt64(ordinal);
    public override string GetString(int ordinal) => reader.GetString(ordinal);
    public override string GetName(int ordinal) => reader.GetName(ordinal);
    public override int GetOrdinal(string name) => reader.GetOrdinal(name);
    public override string GetDataTypeName(int ordinal) => reader.GetDataTypeName(ordinal);
    public override Type GetFieldType(int ordinal) => reader.GetFieldType(ordinal);
    public override object GetValue(int ordinal) => reader.GetValue(ordinal);
    public override int GetValues(object[] values) => reader.GetValues(values);
    public override bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => reader.IsDBNullAsync(ordinal, cancellationToken);
    public override T GetFieldValue<T>(int ordinal) => reader.GetFieldValue<T>(ordinal);
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
    public override Stream GetStream(int ordinal) => reader.GetStream(ordinal);
    public override TextReader GetTextReader(int ordinal) => reader.GetTextReader(ordinal);
    public override Type GetProviderSpecificFieldType(int ordinal) => reader.GetProviderSpecificFieldType(ordinal);
    public override object GetProviderSpecificValue(int ordinal) => reader.GetProviderSpecificValue(ordinal);
    public override int GetProviderSpecificValues(object[] values) => reader.GetProviderSpecificValues(values);
    public override DataTable GetSchemaTable() => reader.GetSchemaTable();
#if NETSTANDARD2_1
    public Task<DataTable> GetSchemaTableAsync(CancellationToken cancellationToken = default) => ((DmDataReader)reader).GetSchemaTableAsync(cancellationToken);
#else
    public override Task<DataTable> GetSchemaTableAsync(CancellationToken cancellationToken = default) => reader.GetSchemaTableAsync(cancellationToken);
#endif
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    public override void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        Exception primary = null;
        try { reader.Close(); }
        catch (Exception error) { primary = error; throw; }
        finally { if (primary == null) closeConnection(); else try { closeConnection(); } catch { } }
    }
    public override async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        Exception primary = null;
        try { await reader.CloseAsync().ConfigureAwait(false); }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary == null) await closeConnectionAsync().ConfigureAwait(false);
            else try { await closeConnectionAsync().ConfigureAwait(false); } catch { }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}

#if !NETSTANDARD2_1
/// <summary>A data source batch; each execution opens a pooled connection and returns it when the run or its reader ends.</summary>
internal sealed class DmDataSourceBatch : DbBatch
{
    private readonly DmDataSource source;
    private readonly DmBatch batch = new();
    private readonly object lifecycleGate = new();
    private DmDataSourceExecution activeExecution;
    private bool disposed;
    private bool readerDelivered;

    internal static Action AfterOpen;
    internal static Action AfterCancelCaptured;

    internal DmDataSourceBatch(DmDataSource source) => this.source = source;
    protected override DbBatchCommandCollection DbBatchCommands => batch.BatchCommands;
    public override int Timeout { get => batch.Timeout; set => batch.Timeout = value; }
    protected override DbConnection DbConnection
    { get => null; set => throw new NotSupportedException("A data source batch owns its connection."); }
    protected override DbTransaction DbTransaction
    { get => null; set => throw new NotSupportedException("Use a data source connection for transactions."); }
    protected override DbBatchCommand CreateDbBatchCommand() => new DmBatchCommand();

    public override void Cancel()
    {
        DmDataSourceExecution captured;
        lock (lifecycleGate) captured = activeExecution;
        Volatile.Read(ref AfterCancelCaptured)?.Invoke();
        captured?.RequestCancellation();
    }

    public override void Prepare() => batch.Prepare();
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => batch.PrepareAsync(cancellationToken);

    private DmDataSourceExecution Begin(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var execution = new DmDataSourceExecution(token);
        try
        {
            lock (lifecycleGate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(DmDataSourceBatch));
                if (activeExecution != null)
                    throw new InvalidOperationException("A data source batch is already executing or owns a reader.");
                execution.Connection = source.CreateConnection();
                batch.BindDataSourceExecution(execution);
                activeExecution = execution;
                return execution;
            }
        }
        catch { execution.Dispose(); execution.Connection?.Dispose(); throw; }
    }

    private DbDataReader PublishReader(DbDataReader reader, DmDataSourceExecution execution)
    {
        lock (lifecycleGate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(DmDataSourceBatch));
            var wrapper = new DmDataSourceReader(reader, () => End(execution), () => EndAsync(execution));
            readerDelivered = true;
            return wrapper;
        }
    }

    private void End(DmDataSourceExecution execution)
    {
        try { execution.Connection.Dispose(); }
        finally { execution.Connection.RunAfterPhysicalClose(() => Release(execution)); }
    }

    private async ValueTask EndAsync(DmDataSourceExecution execution)
    {
        try { await execution.Connection.DisposeAsync().ConfigureAwait(false); }
        finally { execution.Connection.RunAfterPhysicalClose(() => Release(execution)); }
    }

    private void Release(DmDataSourceExecution execution)
    {
        try { execution.TakePendingBatch()?.Dispose(); }
        finally
        {
            lock (lifecycleGate)
            {
                if (ReferenceEquals(activeExecution, execution))
                {
                    batch.UnbindDataSourceExecution(execution);
                    readerDelivered = false;
                    activeExecution = null;
                }
            }
            execution.Dispose();
        }
    }

    public override int ExecuteNonQuery() => Execute(DmOperationPurpose.Query, DmBatch.ExecuteNonQueryCore);

    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(DmOperationPurpose.Query, DmBatch.ExecuteNonQueryCoreAsync, cancellationToken);

    public override object ExecuteScalar() => Execute(DmOperationPurpose.Reader,
        execution => DmBatch.ReadScalar(DmBatch.ExecuteReaderCore(execution, CommandBehavior.Default)));

    public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(DmOperationPurpose.Reader, async (execution, token) =>
        {
            DbDataReader reader = await DmBatch.ExecuteReaderCoreAsync(execution, CommandBehavior.Default, token).ConfigureAwait(false);
            return await DmBatch.ReadScalarAsync(reader, token).ConfigureAwait(false);
        }, cancellationToken);

    private T Execute<T>(DmOperationPurpose purpose, Func<DmBatchExecution, T> run)
    {
        DmDataSourceExecution execution = Begin(CancellationToken.None);
        Exception primary = null;
        try
        {
            batch.CaptureDataSourceBatch(execution, CommandBehavior.Default);
            execution.Open();
            execution.StopObservingCallerForOpen();
            Volatile.Read(ref AfterOpen)?.Invoke();
            return run(batch.TakeDataSourceBatch(execution, purpose));
        }
        catch (Exception error) { primary = error; throw; }
        finally { if (primary == null) End(execution); else try { End(execution); } catch { } }
    }

    private async Task<T> ExecuteAsync<T>(DmOperationPurpose purpose, Func<DmBatchExecution, CancellationToken, Task<T>> run, CancellationToken token)
    {
        DmDataSourceExecution execution = Begin(token);
        Exception primary = null;
        try
        {
            batch.CaptureDataSourceBatch(execution, CommandBehavior.Default);
            await execution.OpenAsync().ConfigureAwait(false);
            execution.StopObservingCallerForOpen();
            Volatile.Read(ref AfterOpen)?.Invoke();
            token.ThrowIfCancellationRequested();
            return await run(batch.TakeDataSourceBatch(execution, purpose), token).ConfigureAwait(false);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            if (primary == null) await EndAsync(execution).ConfigureAwait(false);
            else try { await EndAsync(execution).ConfigureAwait(false); } catch { }
        }
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        DmDataSourceExecution execution = Begin(CancellationToken.None);
        DbDataReader reader = null;
        try
        {
            batch.CaptureDataSourceBatch(execution, behavior);
            execution.Open();
            execution.StopObservingCallerForOpen();
            Volatile.Read(ref AfterOpen)?.Invoke();
            reader = DmBatch.ExecuteReaderCore(batch.TakeDataSourceBatch(execution, DmOperationPurpose.Reader), behavior);
            return PublishReader(reader, execution);
        }
        catch
        {
            // Close normally drains later steps. Cancel before cleaning up an undelivered reader.
            if (reader != null) try { execution.RequestCancellation(); } catch { }
            try { reader?.Dispose(); } catch { }
            try { End(execution); } catch { }
            throw;
        }
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        DmDataSourceExecution execution = Begin(cancellationToken);
        DbDataReader reader = null;
        try
        {
            batch.CaptureDataSourceBatch(execution, behavior);
            await execution.OpenAsync().ConfigureAwait(false);
            execution.StopObservingCallerForOpen();
            Volatile.Read(ref AfterOpen)?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            reader = await DmBatch.ExecuteReaderCoreAsync(batch.TakeDataSourceBatch(execution, DmOperationPurpose.Reader),
                behavior, cancellationToken).ConfigureAwait(false);
            return PublishReader(reader, execution);
        }
        catch
        {
            if (reader != null) try { execution.RequestCancellation(); } catch { }
            try { if (reader != null) await reader.DisposeAsync().ConfigureAwait(false); } catch { }
            try { await EndAsync(execution).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    public override void Dispose()
    {
        DmDataSourceExecution pending;
        lock (lifecycleGate)
        {
            if (disposed) return;
            disposed = true;
            // A published reader owns cleanup, even after its batch is disposed.
            pending = readerDelivered ? null : activeExecution;
        }
        try { batch.Dispose(); }
        finally { pending?.RequestCancellation(); }
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

#endif
