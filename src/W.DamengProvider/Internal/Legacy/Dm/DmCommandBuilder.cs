using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using W.Dm.filter;

namespace W.Dm;

public sealed class DmCommandBuilder : DbCommandBuilder, IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator = 0L;

	private static string m_QuotePrefix = "\"";

	private static string m_QuoteSuffix = "\"";

	public long ID
	{
		get
		{
			if (id < 0)
			{
				id = Interlocked.Increment(ref idGenerator);
			}
			return id;
		}
	}

	public BaseFilter filterHead
	{
		get => null;
		set
		{
			if (value != null)
				throw new NotSupportedException("Legacy filter injection is unsupported.");
		}
	}

	public LogInfo LogInfo { get; set; }

	public RWInfo RWInfo { get; set; }

	public RecoverInfo RecoverInfo { get; set; }

	internal string do_QuotePrefix
	{
		get
		{
			return m_QuotePrefix;
		}
		set
		{
			_ = m_QuotePrefix != value;
		}
	}

	internal string do_QuoteSuffix
	{
		get
		{
			return m_QuoteSuffix;
		}
		set
		{
			_ = m_QuoteSuffix != value;
		}
	}

	internal string do_CatalogSeparator
	{
		get
		{
			return base.CatalogSeparator;
		}
		set
		{
			base.CatalogSeparator = value;
		}
	}

	internal CatalogLocation do_CatalogLocation
	{
		get
		{
			return base.CatalogLocation;
		}
		set
		{
			base.CatalogLocation = value;
		}
	}

	internal ConflictOption do_ConflictOption
	{
		get
		{
			return base.ConflictOption;
		}
		set
		{
			base.ConflictOption = value;
		}
	}

	internal string do_SchemaSeparator
	{
		get
		{
			return base.SchemaSeparator;
		}
		set
		{
			base.SchemaSeparator = value;
		}
	}

	public override string QuotePrefix
	{
		get
		{
			if (filterHead == null)
			{
				return do_QuotePrefix;
			}
			return filterHead.getQuotePrefix(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_QuotePrefix = value;
			}
			else
			{
				filterHead.setQuotePrefix(this, value);
			}
		}
	}

	public override string QuoteSuffix
	{
		get
		{
			if (filterHead == null)
			{
				return do_QuoteSuffix;
			}
			return filterHead.getQuoteSuffix(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_QuoteSuffix = value;
			}
			else
			{
				filterHead.setQuoteSuffix(this, value);
			}
		}
	}

	public override string CatalogSeparator
	{
		get
		{
			if (filterHead == null)
			{
				return do_CatalogSeparator;
			}
			return filterHead.getCatalogSeparator(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_CatalogSeparator = value;
			}
			else
			{
				filterHead.setCatalogSeparator(this, value);
			}
		}
	}

	public override CatalogLocation CatalogLocation
	{
		get
		{
			if (filterHead == null)
			{
				return do_CatalogLocation;
			}
			return filterHead.getCatalogLocation(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_CatalogLocation = value;
			}
			else
			{
				filterHead.setCatalogLocation(this, value);
			}
		}
	}

	public override ConflictOption ConflictOption
	{
		get
		{
			if (filterHead == null)
			{
				return do_ConflictOption;
			}
			return filterHead.getConflictOption(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_ConflictOption = value;
			}
			else
			{
				filterHead.setConflictOption(this, value);
			}
		}
	}

	public override string SchemaSeparator
	{
		get
		{
			if (filterHead == null)
			{
				return do_SchemaSeparator;
			}
			return filterHead.getSchemaSeparator(this);
		}
		set
		{
			if (filterHead == null)
			{
				do_SchemaSeparator = value;
			}
			else
			{
				filterHead.setSchemaSeparator(this, value);
			}
		}
	}

	public DmCommandBuilder()
	{
		base.ConflictOption = ConflictOption.CompareRowVersion;
	}

	public DmCommandBuilder(DmDataAdapter adapter)
		: this()
	{
		base.ConflictOption = ConflictOption.CompareRowVersion;
		base.DataAdapter = adapter;
	}

	internal string do_QuoteIdentifier(string org_str)
	{
		string text = org_str.Replace("\"", "\"\"").Insert(0, "\"");
		return text.Insert(text.Length, "\"");
	}

	internal void do_RefreshSchema()
	{
		base.RefreshSchema();
	}

	internal string do_UnquoteIdentifier(string org_str)
	{
		string text = null;
		text = org_str.Replace("\"\"", "\"");
		int num = text.IndexOf("\"");
		text = text.Substring(num + 1, text.Length - 1);
		return text[..text.LastIndexOf("\"")];
	}

	internal void do_ApplyParameterInfo(DmParameter parameter, DataRow datarow, StatementType statementType, bool whereClause)
	{
		DmDbType sqlType = DmSqlType.CTypeToDmDbType((int)datarow[SchemaTableColumn.ProviderType]);
		parameter.do_DbType = DmSqlType.DmSqlTypeToDbType(sqlType);
	}

	internal string do_GetParameterName(string parameterName)
	{
		return ":" + parameterName;
	}

	internal string do_GetParameterName(int parameterOrdinal)
	{
		return ":p" + parameterOrdinal.ToString(DmConst.invariantCulture);
	}

	internal string do_GetParameterPlaceholder(int parameterOrdinal)
	{
		return ":p" + parameterOrdinal.ToString(DmConst.invariantCulture);
	}

	internal DataTable do_GetSchemaTable(DmCommand sourceCommand)
	{
		return base.GetSchemaTable(sourceCommand);
	}

	internal DmCommand do_InitializeCommand(DmCommand command)
	{
		return (DmCommand)base.InitializeCommand(command);
	}

	internal void do_SetRowUpdatingHandler(DmDataAdapter adapter)
	{
		if (adapter == base.DataAdapter)
		{
			adapter.RowUpdating -= RowUpdatingHandler;
		}
		else
		{
			adapter.RowUpdating += RowUpdatingHandler;
		}
	}

	public override string QuoteIdentifier(string org_str)
	{
		if (filterHead == null)
		{
			return do_QuoteIdentifier(org_str);
		}
		return filterHead.QuoteIdentifier(this, org_str);
	}

	public override void RefreshSchema()
	{
		if (filterHead == null)
		{
			do_RefreshSchema();
		}
		else
		{
			filterHead.RefreshSchema(this);
		}
	}

	public override string UnquoteIdentifier(string org_str)
	{
		if (filterHead == null)
		{
			return do_UnquoteIdentifier(org_str);
		}
		return filterHead.UnquoteIdentifier(this, org_str);
	}

	protected override void ApplyParameterInfo(DbParameter parameter, DataRow datarow, StatementType statementType, bool whereClause)
	{
		if (filterHead == null)
		{
			do_ApplyParameterInfo((DmParameter)parameter, datarow, statementType, whereClause);
		}
		else
		{
			filterHead.ApplyParameterInfo(this, (DmParameter)parameter, datarow, statementType, whereClause);
		}
	}

	protected override string GetParameterName(string parameterName)
	{
		if (filterHead == null)
		{
			return do_GetParameterName(parameterName);
		}
		return filterHead.GetParameterName(this, parameterName);
	}

	protected override string GetParameterName(int parameterOrdinal)
	{
		if (filterHead == null)
		{
			return do_GetParameterName(parameterOrdinal);
		}
		return filterHead.GetParameterName(this, parameterOrdinal);
	}

	protected override string GetParameterPlaceholder(int parameterOrdinal)
	{
		if (filterHead == null)
		{
			return do_GetParameterPlaceholder(parameterOrdinal);
		}
		return filterHead.GetParameterPlaceholder(this, parameterOrdinal);
	}

	protected override DataTable GetSchemaTable(DbCommand sourceCommand)
	{
		if (filterHead == null)
		{
			return do_GetSchemaTable((DmCommand)sourceCommand);
		}
		return filterHead.GetSchemaTable(this, (DmCommand)sourceCommand);
	}

	protected override DbCommand InitializeCommand(DbCommand command)
	{
		if (filterHead == null)
		{
			return do_InitializeCommand((DmCommand)command);
		}
		return filterHead.InitializeCommand(this, (DmCommand)command);
	}

	protected override void SetRowUpdatingHandler(DbDataAdapter adapter)
	{
		if (filterHead == null)
		{
			do_SetRowUpdatingHandler((DmDataAdapter)adapter);
		}
		else
		{
			filterHead.SetRowUpdatingHandler(this, (DmDataAdapter)adapter);
		}
	}

	public new DbCommand GetUpdateCommand(bool useColumnsForParameterNames)
	{
		return base.GetUpdateCommand(false);
	}

	protected override void Dispose(bool disposing)
	{
		base.Dispose(disposing);
	}

	public static void DeriveParameters(DmCommand command) =>
		W.Dm.Internal.Execution.DmDerivedParameters.Derive(command);

	private void RowUpdatingHandler(object sender, DmSqlRowUpdatingEventArgs e)
	{
		RowUpdatingHandler(e);
	}

}
