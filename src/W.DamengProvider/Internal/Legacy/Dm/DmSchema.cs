using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;

namespace W.Dm;

// One description drives discovery, restriction validation, SQL and both execution paths.
internal sealed partial class DmSchema
{
    private readonly DmConnection m_Conn;
    private readonly DmExecutionLease lease;

    internal DmSchema(DmConnection connection, DmExecutionLease lease)
    {
        m_Conn = connection ?? throw new ArgumentNullException(nameof(connection));
        this.lease = lease ?? throw new ArgumentNullException(nameof(lease));
    }

    internal DataTable GetSchema(Request request)
    {
        DataTable memory = GetMemorySchema(request.Definition.Name);
        if (memory != null) return memory;
        DataTable table = request.Definition.CreateTable();
        using DmCommand command = CreateCommand(request);
        using DmDataReader reader = command.ExecuteInternalReader(lease, CommandBehavior.Default);
        while (reader.do_Read())
        {
            object[] values = new object[table.Columns.Count];
            for (int i = 0; i < values.Length; i++) values[i] = reader.GetValue(i);
            AddRow(table, values);
        }
        return table;
    }

    internal async Task<DataTable> GetSchemaAsync(Request request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DataTable memory = GetMemorySchema(request.Definition.Name);
        if (memory != null) return memory;
        DataTable table = request.Definition.CreateTable();
        await using DmCommand command = CreateCommand(request);
        await using DmDataReader reader = await command.ExecuteInternalReaderAsync(
            lease, CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        while (await reader.ReadInternalAsync(cancellationToken).ConfigureAwait(false))
        {
            object[] values = new object[table.Columns.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = await reader.GetFieldValueAsync<object>(i, cancellationToken).ConfigureAwait(false);
            AddRow(table, values);
        }
        return table;
    }

    private DmCommand CreateCommand(Request request)
    {
        var command = m_Conn.CreateCommand(request.CommandText);
        for (int i = 0; i < request.Restrictions.Length; i++)
        {
            string value = request.Restrictions[i];
            if (value == null) continue;
            // UTAB is the historical provider spelling; catalog uses TABLE.
            if (request.Definition.Name == "Tables" && i == 2 && value == "UTAB") value = "TABLE";
            command.Parameters.Add(new DmParameter { ParameterName = "r" + i, DbType = DbType.String, Value = value });
        }
        return command;
    }

    internal static void AddRow(DataTable table, object[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            object value = values[i];
            values[i] = value == null || value == DBNull.Value ? DBNull.Value :
                table.Columns[i].DataType.IsInstanceOfType(value) ? value :
                Convert.ChangeType(value, table.Columns[i].DataType, CultureInfo.InvariantCulture);
        }
        table.Rows.Add(values);
    }

    private DataTable GetMemorySchema(string name) => name switch
    {
        "MetaDataCollections" => GetCollections(),
        "Restrictions" => GetRestrictions(),
        "DataSourceInformation" => GetDataSourceInformation(),
        "DataTypes" => GetDataTypes(),
        _ => null
    };

    internal static DataTable GetCollections()
    {
        var table = new DataTable("MetaDataCollections");
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("NumberOfRestrictions", typeof(int));
        table.Columns.Add("NumberOfIdentifierParts", typeof(int));
        foreach (Definition definition in Definitions)
            table.Rows.Add(definition.Name, definition.RestrictionNames.Length, definition.IdentifierParts);
        return table;
    }

    internal static DataTable GetRestrictions()
    {
        var table = new DataTable("Restrictions");
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("RestrictionName", typeof(string));
        table.Columns.Add("RestrictionDefault", typeof(string));
        table.Columns.Add("RestrictionNumber", typeof(int));
        foreach (Definition definition in Definitions)
            for (int i = 0; i < definition.RestrictionNames.Length; i++)
                table.Rows.Add(definition.Name, definition.RestrictionNames[i], DBNull.Value, i + 1);
        return table;
    }

	private DataTable GetDataSourceInformation()
	{
		DataTable obj = new DataTable("DataSourceInformation")
		{
			Columns = 
			{
				{
					"CompositeIdentifierSeparatorPattern",
					typeof(string)
				},
				{
					"DataSourceProductName",
					typeof(string)
				},
				{
					"DataSourceProductVersion",
					typeof(string)
				},
				{
					"DataSourceProductVersionNormalized",
					typeof(string)
				},
				{
					"GroupByBehavior",
					typeof(GroupByBehavior)
				},
				{
					"IdentifierPattern",
					typeof(string)
				},
				{
					"IdentifierCase",
					typeof(IdentifierCase)
				},
				{
					"OrderByColumnsInSelect",
					typeof(bool)
				},
				{
					"ParameterMarkerFormat",
					typeof(string)
				},
				{
					"ParameterMarkerPattern",
					typeof(string)
				},
				{
					"ParameterNameMaxLength",
					typeof(int)
				},
				{
					"ParameterNamePattern",
					typeof(string)
				},
				{
					"QuotedIdentifierPattern",
					typeof(string)
				},
				{
					"QuotedIdentifierCase",
					typeof(IdentifierCase)
				},
				{
					"StatementSeparatorPattern",
					typeof(string)
				},
				{
					"StringLiteralPattern",
					typeof(string)
				},
				{
					"SupportedJoinOperators",
					typeof(SupportedJoinOperators)
				}
			}
		};
		DataRow dataRow = obj.NewRow();
		dataRow["CompositeIdentifierSeparatorPattern"] = "\\.";
		dataRow["DataSourceProductName"] = "DM";
		dataRow["DataSourceProductVersion"] = m_Conn.ServerVersion;
		dataRow["DataSourceProductVersionNormalized"] = DBNull.Value;
		dataRow["GroupByBehavior"] = GroupByBehavior.Unrelated;
		dataRow["IdentifierPattern"] = "(^\\`\\p{Lo}\\p{Lu}\\p{Ll}_@#][\\p{Lo}\\p{Lu}\\p{Ll}\\p{Nd}@$#_]*$)|(^\\`[^\\`\\0]|\\`\\`+\\`$)|(^\\\" + [^\\\"\\0]|\\\"\\\"+\\\"$)";
		if (m_Conn.GetConnInstance().ConnProperty.CaseSensitive)
		{
			dataRow["IdentifierCase"] = IdentifierCase.Sensitive;
		}
		else
		{
			dataRow["IdentifierCase"] = IdentifierCase.Insensitive;
		}
		dataRow["OrderByColumnsInSelect"] = false;
		dataRow["ParameterMarkerFormat"] = "{0}";
		dataRow["ParameterMarkerPattern"] = "(@[A-Za-z0-9_$#]*)";
		dataRow["ParameterNameMaxLength"] = 128;
		dataRow["ParameterNamePattern"] = "^[\\p{Lo}\\p{Lu}\\p{Ll}\\p{Lm}_@#][\\p{Lo}\\p{Lu}\\p{Ll}\\p{Lm}\\p{Nd}\\uff3f_@#\\$]*(?=\\s+|$)";
		dataRow["QuotedIdentifierPattern"] = "(([^\\`]|\\`\\`)*)";
		dataRow["QuotedIdentifierCase"] = IdentifierCase.Sensitive;
		dataRow["StatementSeparatorPattern"] = ";";
		dataRow["StringLiteralPattern"] = "'(([^']|'')*)'";
		dataRow["SupportedJoinOperators"] = 15;
		obj.Rows.Add(dataRow);
		return obj;
	}

	private static void DataTypesGenCols(DataTable dt)
	{
		dt.Columns.Add(new DataColumn("TypeName", typeof(string)));
		dt.Columns.Add(new DataColumn("ProviderDbType", typeof(int)));
		dt.Columns.Add(new DataColumn("ColumnSize", typeof(long)));
		dt.Columns.Add(new DataColumn("CreateFormat", typeof(string)));
		dt.Columns.Add(new DataColumn("CreateParameters", typeof(string)));
		dt.Columns.Add(new DataColumn("DataType", typeof(string)));
		dt.Columns.Add(new DataColumn("IsAutoincrementable", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsBestMatch", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsCaseSensitive", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsFixedLength", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsFixedPrecisionScale", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsLong", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsNullable", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsSearchable", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsSearchableWithLike", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsUnsigned", typeof(bool)));
		dt.Columns.Add(new DataColumn("MaximumScale", typeof(short)));
		dt.Columns.Add(new DataColumn("MinimumScale", typeof(short)));
		dt.Columns.Add(new DataColumn("IsConcurrencyType", typeof(bool)));
		dt.Columns.Add(new DataColumn("IsLiteralsSupported", typeof(bool)));
		dt.Columns.Add(new DataColumn("LiteralPrefix", typeof(string)));
		dt.Columns.Add(new DataColumn("LiteralSuffix", typeof(string)));
		dt.Columns.Add(new DataColumn("NativeDataType", typeof(string)));
	}

	private static void DataTypesAddRow(DataTable dt, string TypeName, int ProviderDbType, long ColumnSize, string CreateFormat, string CreateParameters, string DataType, bool IsAutoincrementable, bool IsBestMatch, bool IsCaseSensitive, bool IsFixedLength, bool IsFixedPrecisionScale, bool IsLong, bool IsNullable, bool IsSearchable, bool IsSearchableWithLike, object IsUnsigned, object MaximumScale, object MinimumScale, bool IsConcurrencyType, object IsLiteralsSupported, string LiteralPrefix, string LiteralSuffix, string NativeDataType)
	{
		DataRow dataRow = dt.NewRow();
		dataRow["TypeName"] = TypeName;
		dataRow["ProviderDbType"] = ProviderDbType;
		dataRow["ColumnSize"] = ColumnSize;
		dataRow["CreateFormat"] = CreateFormat;
		dataRow["CreateParameters"] = CreateParameters;
		dataRow["DataType"] = DataType;
		dataRow["IsAutoincrementable"] = IsAutoincrementable;
		dataRow["IsBestMatch"] = IsBestMatch;
		dataRow["IsCaseSensitive"] = IsCaseSensitive;
		dataRow["IsFixedLength"] = IsFixedLength;
		dataRow["IsFixedPrecisionScale"] = IsFixedPrecisionScale;
		dataRow["IsLong"] = IsLong;
		dataRow["IsNullable"] = IsNullable;
		dataRow["IsSearchable"] = IsSearchable;
		dataRow["IsSearchableWithLike"] = IsSearchableWithLike;
		dataRow["IsUnsigned"] = IsUnsigned;
		dataRow["MaximumScale"] = MaximumScale;
		dataRow["MinimumScale"] = MinimumScale;
		dataRow["IsConcurrencyType"] = IsConcurrencyType;
		dataRow["IsLiteralsSupported"] = IsLiteralsSupported;
		dataRow["LiteralPrefix"] = LiteralPrefix;
		dataRow["LiteralSuffix"] = LiteralSuffix;
		dataRow["NativeDataType"] = NativeDataType;
		dt.Rows.Add(dataRow);
	}

	private DataTable GetDataTypes()
	{
		DataTable dataTable = new DataTable("DataTypes");
		DataTypesGenCols(dataTable);
		DataTypesAddRow(dataTable, "smallint", 12, 5L, "smallint", null, "System.Int16", IsAutoincrementable: true, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: true, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "smallint");
		DataTypesAddRow(dataTable, "int", 13, 10L, "int", null, "System.Int32", IsAutoincrementable: true, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: true, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "int");
		DataTypesAddRow(dataTable, "real", 11, 7L, "real", null, "System.Single", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "real");
		DataTypesAddRow(dataTable, "double", 10, 53L, "double", null, "System.Double", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "double");
		DataTypesAddRow(dataTable, "bit", 2, 1L, "bit", null, "System.Boolean", IsAutoincrementable: false, IsBestMatch: false, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "bit");
		DataTypesAddRow(dataTable, "tinyint", 17, 3L, "tinyint", null, "System.SByte", IsAutoincrementable: true, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: true, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "tinyint");
		DataTypesAddRow(dataTable, "bigint", 14, 19L, "bigint", null, "System.Int64", IsAutoincrementable: true, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: true, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, null, null, "bigint");
		DataTypesAddRow(dataTable, "varbinary", 23, 8000L, "varbinary({0})", "max length", "System.Byte[]", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: false, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, "0x", null, "varbinary");
		DataTypesAddRow(dataTable, "binary", 1, 8000L, "binary({0})", "length", "System.Byte[]", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, "0x", null, "binary");
		DataTypesAddRow(dataTable, "image", 0, 2147483647L, "image", null, "System.Byte[]", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: false, IsFixedPrecisionScale: false, IsLong: true, IsNullable: true, IsSearchable: false, IsSearchableWithLike: false, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, "0x", null, "image");
		DataTypesAddRow(dataTable, "char", 4, 8000L, "char", "length", "System.String", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: true, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, "'", "'", "char");
		DataTypesAddRow(dataTable, "varchar", 24, 8000L, "varchar({0})", "max length", "System.String", IsAutoincrementable: false, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: false, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: true, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, "'", "'", "varchar");
		DataTypesAddRow(dataTable, "text", 5, 2147483647L, "text", null, "System.String", IsAutoincrementable: false, IsBestMatch: false, IsCaseSensitive: false, IsFixedLength: false, IsFixedPrecisionScale: false, IsLong: true, IsNullable: true, IsSearchable: false, IsSearchableWithLike: false, DBNull.Value, DBNull.Value, DBNull.Value, IsConcurrencyType: false, DBNull.Value, "'", "'", "text");
		DataTypesAddRow(dataTable, "decimal", 8, 38L, "decimal({0}, {1})", "precision,scale", "System.Decimal", IsAutoincrementable: true, IsBestMatch: true, IsCaseSensitive: false, IsFixedLength: true, IsFixedPrecisionScale: false, IsLong: false, IsNullable: true, IsSearchable: true, IsSearchableWithLike: false, false, 38, 0, IsConcurrencyType: false, DBNull.Value, "'", "'", "decimal");
		// Type-level fractional precision is separate from a column's DATA_SCALE and
		// the negotiated temporal write capability profile. Never expose packed wire scale.
		AddTemporalType(dataTable, "date", DmDbType.Date, typeof(DateTime), 10, 0);
		AddTemporalType(dataTable, "time", DmDbType.Time,
			DmSqlType.CTypeToSystemType(DmSqlType.TIME, 0, m_Conn.GetConnInstance().ConnProperty), 15, 6);
		AddTemporalType(dataTable, "datetime", DmDbType.DateTime, typeof(DateTime), 29, 9);
		AddTemporalType(dataTable, "timestamp", DmDbType.DateTime, typeof(DateTime), 29, 9);
		AddTemporalType(dataTable, "time with time zone", DmDbType.TimeOffset, typeof(DateTimeOffset), 21, 6);
		AddTemporalType(dataTable, "timestamp with time zone", DmDbType.DateTimeOffset, typeof(DateTimeOffset), 35, 9);
		DataRow blob = dataTable.NewRow();
		blob.ItemArray = (object[])dataTable.Select("TypeName='image'")[0].ItemArray.Clone();
		blob["TypeName"] = blob["CreateFormat"] = blob["NativeDataType"] = "blob";
		dataTable.Rows.Add(blob);
		DataRow clob = dataTable.NewRow();
		clob.ItemArray = (object[])dataTable.Select("TypeName='text'")[0].ItemArray.Clone();
		clob["TypeName"] = clob["CreateFormat"] = clob["NativeDataType"] = "clob";
		clob["IsBestMatch"] = true;
		dataTable.Rows.Add(clob);
		return dataTable;
	}

    private static void AddTemporalType(DataTable table, string name, DmDbType providerType, Type clrType,
        long displaySize, short maximumScale)
    {
        string format = name == "date" ? name : name.Contains(" with ")
            ? name.Replace(" with ", "({0}) with ") : name + "({0})";
        DataTypesAddRow(table, name, (int)providerType, displaySize, format,
            maximumScale == 0 ? null : "scale", clrType.FullName, false, name != "timestamp", false,
            true, false, false, true, true, false, DBNull.Value, maximumScale, (short)0,
            false, true, "'", "'", name);
    }

}
