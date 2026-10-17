using System;
using System.Data.Common;

namespace W.Dm;

public class DmClientFactory : DbProviderFactory, IServiceProvider
{
	public static readonly DmClientFactory Instance = new DmClientFactory();

	public override bool CanCreateDataSourceEnumerator => false;

#if !NETSTANDARD2_1
	public override bool CanCreateBatch => true;

	public override DbBatch CreateBatch() => new DmBatch();

	public override DbBatchCommand CreateBatchCommand() => new DmBatchCommand();

#endif
	private DmClientFactory()
	{
	}

	public override DbCommand CreateCommand()
	{
		return new DmCommand();
	}

	public override DbCommandBuilder CreateCommandBuilder()
	{
		return new DmCommandBuilder();
	}

	public override DbConnection CreateConnection()
	{
		return new DmConnection();
	}

#if NET7_0_OR_GREATER
	public override DbDataSource CreateDataSource(string connectionString) => new DmDataSource(connectionString);
#else
	// .NET 6 has no DbProviderFactory.CreateDataSource; only typed callers can reach this.
	public DmDataSource CreateDataSource(string connectionString) => new DmDataSource(connectionString);
#endif

	public override DbConnectionStringBuilder CreateConnectionStringBuilder()
	{
		return new DmConnectionStringBuilder();
	}

	public override DbDataAdapter CreateDataAdapter()
	{
		return new DmDataAdapter();
	}

	public override DbParameter CreateParameter()
	{
		return new DmParameter();
	}

	object IServiceProvider.GetService(Type serviceType)
	{
		return null;
	}
}
