using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;

namespace W.Dm;

/// <summary>
/// Executes its commands in order on one connection. Dameng has no multi-statement request,
/// so each command is a separate server execution; the batch holds the connection for its
/// whole run, stops at the first failure and adds no implicit transaction.
/// </summary>
public sealed class DmBatch : DbBatch
{
	private readonly DmCommandPlanGate gate = new();
	private readonly DmBatchCommandCollection commands;
	private DmConnection connection;
	private DmTransaction transaction;
	private int timeout = -1;
	private DmBatchExecution activeExecution;
	private DmDataSourceExecution dataSourceExecution;

	public DmBatch()
	{
		commands = new DmBatchCommandCollection(gate);
	}

	public DmBatch(DmConnection connection, DmTransaction transaction = null) : this()
	{
		this.connection = connection;
		this.transaction = transaction;
	}

	public new DmBatchCommandCollection BatchCommands => commands;

	protected override DbBatchCommandCollection DbBatchCommands => commands;

	/// <summary>Seconds for the whole batch (each reader navigation receives a fresh budget); 0 waits indefinitely.</summary>
	public override int Timeout
	{
		get => GetTimeout(Connection);
		set
		{
			using var mutation = gate.BeginMutation();
			if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
			timeout = value;
		}
	}

	public new DmConnection Connection
	{
		get => Volatile.Read(ref dataSourceExecution)?.Connection ?? connection;
		set
		{
			using var mutation = gate.BeginMutation();
			connection = value;
		}
	}

	protected override DbConnection DbConnection
	{
		get => Connection;
		set => Connection = (DmConnection)value;
	}

	public new DmTransaction Transaction
	{
		get => transaction;
		set
		{
			using var mutation = gate.BeginMutation();
			transaction = value;
		}
	}

	protected override DbTransaction DbTransaction
	{
		get => transaction;
		set => Transaction = (DmTransaction)value;
	}

	public new DmBatchCommand CreateBatchCommand() => new();

	protected override DbBatchCommand CreateDbBatchCommand() => new DmBatchCommand();

	/// <summary>Stops the running batch like <see cref="DmCommand.Cancel"/>; the connection is not reusable afterwards.</summary>
	public override void Cancel() => Volatile.Read(ref activeExecution)?.Cancel();

	// The shortcut owns this binding from closed connection creation through physical close.
	internal void BindDataSourceExecution(DmDataSourceExecution execution) => Volatile.Write(ref dataSourceExecution, execution);
	internal void UnbindDataSourceExecution(DmDataSourceExecution execution) =>
		Interlocked.CompareExchange(ref dataSourceExecution, null, execution);

	public override int ExecuteNonQuery()
	{
		return ExecuteNonQueryCore(Begin(DmOperationPurpose.Query));
	}

	public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await ExecuteNonQueryCoreAsync(Begin(DmOperationPurpose.Query), cancellationToken).ConfigureAwait(false);
	}

	internal static int ExecuteNonQueryCore(DmBatchExecution execution)
	{
		using (execution) return execution.RunNonQuery();
	}

	internal static async Task<int> ExecuteNonQueryCoreAsync(DmBatchExecution execution, CancellationToken cancellationToken)
	{
		using (execution) return await execution.RunNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	public override object ExecuteScalar() => ReadScalar(ExecuteDbDataReader(CommandBehavior.Default));

	internal static object ReadScalar(DbDataReader reader)
	{
		using (reader) return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
	}

	public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default)
	{
		DbDataReader reader = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
		return await ReadScalarAsync(reader, cancellationToken).ConfigureAwait(false);
	}

	internal static async Task<object> ReadScalarAsync(DbDataReader reader, CancellationToken cancellationToken)
	{
		await using (reader.ConfigureAwait(false))
		{
			return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.FieldCount > 0
				? await reader.GetFieldValueAsync<object>(0, cancellationToken).ConfigureAwait(false) : null;
		}
	}

	protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
	{
		CheckBehavior(behavior);
		return ExecuteReaderCore(Begin(DmOperationPurpose.Reader), behavior);
	}

	internal static DbDataReader ExecuteReaderCore(DmBatchExecution execution, CommandBehavior behavior)
	{
		var reader = new DmBatchDataReader(execution, behavior);
		try
		{
			reader.Start();
			return reader;
		}
		catch
		{
			reader.Abandon();
			throw;
		}
	}

	protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		CheckBehavior(behavior);
		return await ExecuteReaderCoreAsync(Begin(DmOperationPurpose.Reader), behavior, cancellationToken).ConfigureAwait(false);
	}

	internal static async Task<DbDataReader> ExecuteReaderCoreAsync(DmBatchExecution execution, CommandBehavior behavior,
		CancellationToken cancellationToken)
	{
		var reader = new DmBatchDataReader(execution, behavior);
		try
		{
			await reader.StartAsync(cancellationToken).ConfigureAwait(false);
			return reader;
		}
		catch
		{
			reader.Abandon();
			throw;
		}
	}

	/// <summary>Validates every command locally. Statements are prepared when the batch executes.</summary>
	public override void Prepare()
	{
		using DmBatchExecution execution = Capture(forExecution: false);
	}

	public override Task PrepareAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Prepare();
		return Task.CompletedTask;
	}

	public override void Dispose()
	{
		gate.MarkDisposed();
		base.Dispose();
	}

	public override ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	private static void CheckBehavior(CommandBehavior behavior)
	{
		const CommandBehavior supported = CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection;
		if ((behavior & ~supported) != 0)
			throw new NotSupportedException("A batch reader supports only SequentialAccess and CloseConnection.");
	}

	private DmBatchExecution Begin(DmOperationPurpose purpose)
	{
		DmBatchExecution execution = Capture(forExecution: true);
		try { execution.Acquire(purpose); return execution; }
		catch { execution.Dispose(); throw; }
	}

	internal void CaptureDataSourceBatch(DmDataSourceExecution shortcut, CommandBehavior behavior)
	{
		CheckBehavior(behavior);
		DmBatchExecution execution = Capture(forExecution: true, shortcut);
		try { shortcut.BindBatch(execution); }
		catch { execution.Dispose(); throw; }
	}

	internal DmBatchExecution TakeDataSourceBatch(DmDataSourceExecution shortcut, DmOperationPurpose purpose)
	{
		if (gate.IsDisposed) throw new ObjectDisposedException(nameof(DmBatch));
		DmBatchExecution execution = shortcut.TakePendingBatch() ??
			throw new InvalidOperationException("The shortcut has no pending batch.");
		try
		{
			execution.ValidateConnectedPlans();
			execution.Acquire(purpose);
			return execution;
		}
		catch
		{
			execution.Dispose();
			throw;
		}
	}

	// Freeze every step before taking a lease. Shortcuts capture before opening
	// and validate the negotiated profile once the connection is open.
	private DmBatchExecution Capture(bool forExecution, DmDataSourceExecution shortcut = null)
	{
		gate.EnterExecution(nameof(DmBatch));
		var execution = new DmBatchExecution(this);
		try
		{
			DmConnection capturedConnection = shortcut?.Connection ?? connection;
			execution.Capture(commands.Snapshot(), capturedConnection, transaction, GetTimeout(capturedConnection),
				deferConnectedValidation: shortcut != null);
			// Publish first: a Cancel racing with acquisition is applied to the new lease.
			if (forExecution) Volatile.Write(ref activeExecution, execution);
			return execution;
		}
		catch
		{
			execution.Dispose();
			throw;
		}
	}

	private int GetTimeout(DmConnection connection) => timeout >= 0 ? timeout : connection?.ConnProperty.CommandTimeout ?? 30;

	internal void Release(DmBatchExecution execution)
	{
		Interlocked.CompareExchange(ref activeExecution, null, execution);
		gate.Exit();
	}
}

public sealed class DmBatchCommand : DbBatchCommand
{
	private readonly DmCommandPlanGate gate = new();
	private readonly DmParameterCollection parameters = new();
	private string commandText = string.Empty;
	private CommandType commandType = CommandType.Text;
	private int recordsAffected = -1;

	public DmBatchCommand()
	{
		parameters.AttachPlanGate(gate);
	}

	public DmBatchCommand(string commandText) : this()
	{
		this.commandText = commandText ?? string.Empty;
	}

	public override string CommandText
	{
		get => commandText;
		set
		{
			using var mutation = gate.BeginMutation();
			commandText = value ?? string.Empty;
		}
	}

	/// <summary>Only <see cref="CommandType.Text"/> executes; other values are rejected before the batch runs.</summary>
	public override CommandType CommandType
	{
		get => commandType;
		set
		{
			using var mutation = gate.BeginMutation();
			if (value is not (CommandType.Text or CommandType.StoredProcedure or CommandType.TableDirect))
				throw new ArgumentOutOfRangeException(nameof(value));
			commandType = value;
		}
	}

	/// <summary>Rows changed by this command in the last execution; -1 for queries, unknown counts or commands that did not complete.</summary>
	public override int RecordsAffected => Volatile.Read(ref recordsAffected);

	public new DmParameterCollection Parameters => parameters;

	protected override DbParameterCollection DbParameterCollection => parameters;

#if NET8_0_OR_GREATER
	public override bool CanCreateParameter => true;

	public override DbParameter CreateParameter() => new DmParameter();
#else
	// DbBatchCommand gained these virtual members in .NET 8.
	public bool CanCreateParameter => true;

	public DbParameter CreateParameter() => new DmParameter();
#endif

	internal void SetRecordsAffected(int value) => Volatile.Write(ref recordsAffected, value);

	internal DmCommandPlanGate Gate => gate;
}

public sealed class DmBatchCommandCollection : DbBatchCommandCollection
{
	private readonly List<DmBatchCommand> items = new();
	private readonly DmCommandPlanGate gate;

	internal DmBatchCommandCollection(DmCommandPlanGate gate) => this.gate = gate;

	public override int Count => items.Count;

	public override bool IsReadOnly => false;

	public new DmBatchCommand this[int index]
	{
		get => items[index];
		set => SetBatchCommand(index, value);
	}

	public override void Add(DbBatchCommand item)
	{
		using var mutation = gate.BeginMutation();
		items.Add(Convert(item));
	}

	public override void Clear()
	{
		using var mutation = gate.BeginMutation();
		items.Clear();
	}

	public override bool Contains(DbBatchCommand item) => item is DmBatchCommand command && items.Contains(command);

	public override void CopyTo(DbBatchCommand[] array, int arrayIndex)
	{
		ArgumentNullException.ThrowIfNull(array);
		for (int index = 0; index < items.Count; index++) array[arrayIndex + index] = items[index];
	}

	public override IEnumerator<DbBatchCommand> GetEnumerator() => items.GetEnumerator();

	public override int IndexOf(DbBatchCommand item) => item is DmBatchCommand command ? items.IndexOf(command) : -1;

	public override void Insert(int index, DbBatchCommand item)
	{
		using var mutation = gate.BeginMutation();
		items.Insert(index, Convert(item));
	}

	public override bool Remove(DbBatchCommand item)
	{
		using var mutation = gate.BeginMutation();
		return item is DmBatchCommand command && items.Remove(command);
	}

	public override void RemoveAt(int index)
	{
		using var mutation = gate.BeginMutation();
		items.RemoveAt(index);
	}

	protected override DbBatchCommand GetBatchCommand(int index) => items[index];

	protected override void SetBatchCommand(int index, DbBatchCommand batchCommand)
	{
		using var mutation = gate.BeginMutation();
		items[index] = Convert(batchCommand);
	}

	internal DmBatchCommand[] Snapshot() => items.ToArray();

	private static DmBatchCommand Convert(DbBatchCommand item) => item switch
	{
		null => throw new ArgumentNullException(nameof(item)),
		DmBatchCommand command => command,
		_ => throw new ArgumentException("Only DmBatchCommand instances can be added to a DmBatch.", nameof(item))
	};
}
