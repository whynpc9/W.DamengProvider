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
		get => timeout >= 0 ? timeout : connection?.ConnProperty.CommandTimeout ?? 30;
		set
		{
			using var mutation = gate.BeginMutation();
			if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
			timeout = value;
		}
	}

	public new DmConnection Connection
	{
		get => connection;
		set
		{
			using var mutation = gate.BeginMutation();
			connection = value;
		}
	}

	protected override DbConnection DbConnection
	{
		get => connection;
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

	public override int ExecuteNonQuery()
	{
		using DmBatchExecution execution = Begin(DmOperationPurpose.Query);
		return execution.RunNonQuery();
	}

	public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using DmBatchExecution execution = Begin(DmOperationPurpose.Query);
		return await execution.RunNonQueryAsync(cancellationToken).ConfigureAwait(false);
	}

	public override object ExecuteScalar()
	{
		using DbDataReader reader = ExecuteDbDataReader(CommandBehavior.Default);
		return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
	}

	public override async Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default)
	{
		DbDataReader reader = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
		await using (reader.ConfigureAwait(false))
		{
			return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.FieldCount > 0
				? await reader.GetFieldValueAsync<object>(0, cancellationToken).ConfigureAwait(false) : null;
		}
	}

	protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
	{
		CheckBehavior(behavior);
		DmBatchExecution execution = Begin(DmOperationPurpose.Reader);
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
		DmBatchExecution execution = Begin(DmOperationPurpose.Reader);
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
		using DmBatchExecution execution = Begin(purpose: null);
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

	// Freezes the batch: captures every step's plan (SQL, parameters, binding) and only
	// then takes the connection. Nothing reaches the server if any command is invalid.
	private DmBatchExecution Begin(DmOperationPurpose? purpose)
	{
		gate.EnterExecution(nameof(DmBatch));
		var execution = new DmBatchExecution(this);
		try
		{
			execution.Capture(commands.Snapshot(), connection, transaction);
			if (purpose is { } leasePurpose)
			{
				int seconds = Timeout;
				if (connection == null) throw new InvalidOperationException("Batch has no connection.");
				// Publish first: a Cancel racing with acquisition is applied to the new lease.
				Volatile.Write(ref activeExecution, execution);
				execution.Acquire(connection.BeginExecution(leasePurpose,
					DmDeadline.FromSeconds(seconds, connection.OperationClock), seconds));
			}
			return execution;
		}
		catch
		{
			execution.Dispose();
			throw;
		}
	}

	internal bool IsDisposed => gate.IsDisposed;

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
