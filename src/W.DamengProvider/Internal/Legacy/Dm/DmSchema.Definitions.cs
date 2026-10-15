using System;
using System.Data;
using System.Text;

namespace W.Dm;

internal sealed partial class DmSchema
{
    internal sealed class Definition
    {
        internal readonly string Name;
        internal readonly int IdentifierParts;
        internal readonly string[] RestrictionNames;
        internal readonly string[] RestrictionExpressions;
        internal readonly (string Name, Type Type)[] Columns;
        internal readonly string Sql;
        internal readonly string OrderBy;

        internal Definition(string name, int identifierParts, string[] restrictionNames = null,
            string[] restrictionExpressions = null, (string, Type)[] columns = null,
            string sql = null, string orderBy = null)
        {
            Name = name;
            IdentifierParts = identifierParts;
            RestrictionNames = restrictionNames ?? Array.Empty<string>();
            RestrictionExpressions = restrictionExpressions ?? Array.Empty<string>();
            Columns = columns ?? Array.Empty<(string, Type)>();
            Sql = sql;
            OrderBy = orderBy;
        }

        internal DataTable CreateTable()
        {
            var table = new DataTable(Name == "Databases" ? "Schemas" : Name);
            foreach (var column in Columns) table.Columns.Add(column.Name, column.Type);
            return table;
        }
    }

    internal sealed class Request
    {
        internal readonly Definition Definition;
        internal readonly string[] Restrictions;
        internal readonly string CommandText;

        internal Request(Definition definition, string[] restrictions)
        {
            Definition = definition;
            Restrictions = restrictions;
            if (definition.Sql == null) return;
            var sql = new StringBuilder(definition.Sql);
            for (int i = 0; i < restrictions.Length; i++)
                if (restrictions[i] != null)
                    sql.Append(" AND ").Append(definition.RestrictionExpressions[i]).Append(" = :r").Append(i);
            if (definition.OrderBy != null) sql.Append(" ORDER BY ").Append(definition.OrderBy);
            CommandText = sql.ToString();
        }
    }

    internal static Request CreateRequest(string collectionName, string[] restrictionValues)
    {
        string name = string.IsNullOrEmpty(collectionName) ? "MetaDataCollections" : collectionName;
        foreach (Definition definition in Definitions)
        {
            if (!string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (restrictionValues != null && restrictionValues.Length > definition.RestrictionNames.Length)
                throw new ArgumentException("Too many restrictions for the metadata collection.", nameof(restrictionValues));
            // Values are exact identifiers/values, never SQL fragments or LIKE patterns.
            // Null means unrestricted; an empty string is bound unchanged. Preserve quotes and case.
            return new Request(definition, restrictionValues == null ? Array.Empty<string>() : (string[])restrictionValues.Clone());
        }
        throw new ArgumentException("Unknown metadata collection.", nameof(collectionName));
    }

    private static readonly Type Text = typeof(string);
    private static readonly Type Number = typeof(int);

    internal static readonly Definition[] Definitions =
    {
        new("MetaDataCollections", 0),
        new("DataSourceInformation", 0),
        new("DataTypes", 0),
        new("Restrictions", 0),
        new("ReservedWords", 0, columns: new[] { ("ReservedWord", Text) },
            sql: "SELECT KEYWORD FROM V$RESERVED_WORDS WHERE 1=1", orderBy: "KEYWORD"),
        new("Tables", 2, new[] { "Schema", "Table", "TableType" },
            new[] { "OWNER", "OBJECT_NAME", "OBJECT_TYPE" },
            new[] { ("TABLE_CATALOG", Text), ("TABLE_SCHEMA", Text), ("TABLE_NAME", Text),
                ("TABLE_TYPE", Text), ("FILLFACTOR", Number), ("SPACE_LIMIT", Number), ("ROW_COUNT", typeof(ulong)) },
            "SELECT CAST(NULL AS VARCHAR(128)), OWNER, OBJECT_NAME, CASE OBJECT_TYPE WHEN 'TABLE' THEN 'UTAB' ELSE OBJECT_TYPE END, " +
            "CAST(NULL AS INT), CAST(NULL AS INT), CAST(NULL AS BIGINT) FROM ALL_OBJECTS WHERE OBJECT_TYPE IN ('TABLE','VIEW')",
            "OWNER, OBJECT_NAME"),
        new("Columns", 3, new[] { "Schema", "Table", "Column" },
            new[] { "OWNER", "TABLE_NAME", "COLUMN_NAME" },
            new[] { ("TABLE_SCHEMA", Text), ("TABLE_NAME", Text), ("COLUMN_NAME", Text), ("COLUMN_ID", Number),
                ("COLUMN_TYPE", Text), ("COLUMN_SIZE", Number), ("COLUMN_SCALE", Number), ("NULLABLE", Text),
                ("DEFAULT_VALUE", Text), ("NUMERIC_PRECISION", Number), ("DATETIME_PRECISION", Number), ("CHARACTER_OCTET_LENGTH", Number) },
            "SELECT OWNER, TABLE_NAME, COLUMN_NAME, COLUMN_ID, DATA_TYPE, " +
            "CASE WHEN DATA_TYPE IN ('CHAR','VARCHAR','VARCHAR2','NCHAR','NVARCHAR','NVARCHAR2') THEN CHAR_LENGTH " +
            "WHEN DATA_TYPE IN ('DECIMAL','DEC','NUMERIC','NUMBER') THEN DATA_PRECISION ELSE NULL END, " +
            "DATA_SCALE, NULLABLE, DATA_DEFAULT, CASE WHEN DATA_TYPE IN ('DECIMAL','DEC','NUMERIC','NUMBER','REAL','DOUBLE','DOUBLE PRECISION','FLOAT','INTEGER','INT','BIGINT','SMALLINT','TINYINT') THEN DATA_PRECISION ELSE NULL END, " +
            "CASE WHEN DATA_TYPE LIKE 'TIME%' OR DATA_TYPE LIKE 'DATETIME%' THEN DATA_SCALE ELSE NULL END, " +
            "CASE WHEN DATA_TYPE IN ('CHAR','VARCHAR','VARCHAR2','NCHAR','NVARCHAR','NVARCHAR2') THEN DATA_LENGTH ELSE NULL END " +
            "FROM ALL_TAB_COLUMNS WHERE 1=1", "OWNER, TABLE_NAME, COLUMN_ID"),
        new("Users", 1, new[] { "Name" }, new[] { "USERNAME" }, new[] { ("USERNAME", Text) },
            "SELECT USERNAME FROM ALL_USERS WHERE 1=1", "USERNAME"),
        new("Schemas", 1, new[] { "Name" }, new[] { "NAME" }, new[] { ("NAME", Text) },
            "SELECT NAME FROM SYS.SYSOBJECTS WHERE TYPE$='SCH'", "NAME"),
        // Historical alias: these are schemas, not physical databases.
        new("Databases", 1, new[] { "Name" }, new[] { "NAME" }, new[] { ("NAME", Text) },
            "SELECT NAME FROM SYS.SYSOBJECTS WHERE TYPE$='SCH'", "NAME"),
        new("Procedures", 2, new[] { "Schema", "Procedure", "ProcedureType" },
            new[] { "OWNER", "OBJECT_NAME", "OBJECT_TYPE" },
            new[] { ("PROCEDURE_SCHEMA", Text), ("PROCEDURE_NAME", Text), ("PROCEDURE_TYPE", Text) },
            "SELECT OWNER, OBJECT_NAME, OBJECT_TYPE FROM ALL_OBJECTS WHERE OBJECT_TYPE IN ('PROCEDURE','FUNCTION')", "OWNER, OBJECT_NAME"),
        new("Parameters", 3, new[] { "Schema", "Procedure", "Parameter" },
            new[] { "OWNER", "OBJECT_NAME", "ARGUMENT_NAME" },
            new[] { ("PROCEDURE_SCHEMA", Text), ("PROCEDURE_NAME", Text), ("PARAMETER_NAME", Text),
                ("PARAMETER_ID", Number), ("PARAMETER_TYPE", Text), ("PARAMETER_SIZE", Number),
                ("PARAMETER_SCALE", Number), ("DEFAULT_VALUE", Text), ("NULLABLE", Text), ("PARAMETER_INOUT", typeof(short)) },
            "SELECT OWNER, OBJECT_NAME, ARGUMENT_NAME, POSITION, DATA_TYPE, DATA_LENGTH, DATA_SCALE, " +
            "DEFAULT_VALUE, CAST(NULL AS VARCHAR(1)), CASE IN_OUT WHEN 'IN' THEN 0 WHEN 'OUT' THEN 1 WHEN 'IN/OUT' THEN 2 ELSE NULL END " +
            "FROM ALL_ARGUMENTS WHERE PACKAGE_NAME IS NULL", "OWNER, OBJECT_NAME, SEQUENCE"),
        new("Indexes", 3, new[] { "Schema", "Table", "Name" },
            new[] { "TABLE_OWNER", "TABLE_NAME", "INDEX_NAME" },
            new[] { ("INDEX_SCHEMA", Text), ("INDEX_NAME", Text), ("TABLE_NAME", Text), ("UNIQUE", Text),
                ("INDEX_TYPE", Text), ("TABLE_SCHEMA", Text) },
            "SELECT OWNER, INDEX_NAME, TABLE_NAME, CASE UNIQUENESS WHEN 'UNIQUE' THEN 'Y' WHEN 'NONUNIQUE' THEN 'N' ELSE NULL END, " +
            "INDEX_TYPE, TABLE_OWNER FROM ALL_INDEXES WHERE 1=1", "TABLE_OWNER, TABLE_NAME, INDEX_NAME"),
        new("PrimaryKeys", 2, new[] { "Schema", "Table" }, new[] { "C.OWNER", "C.TABLE_NAME" },
            new[] { ("TABLE_SCHEMA", Text), ("TABLE_NAME", Text), ("COLUMN_NAME", Text), ("KEY_SEQUENCE", Number), ("PK_NAME", Text) },
            "SELECT C.OWNER, C.TABLE_NAME, CC.COLUMN_NAME, CC.POSITION, C.CONSTRAINT_NAME FROM ALL_CONSTRAINTS C " +
            "JOIN ALL_CONS_COLUMNS CC ON CC.OWNER=C.OWNER AND CC.CONSTRAINT_NAME=C.CONSTRAINT_NAME AND CC.TABLE_NAME=C.TABLE_NAME " +
            "WHERE C.CONSTRAINT_TYPE='P'", "C.OWNER, C.TABLE_NAME, C.CONSTRAINT_NAME, CC.POSITION"),
        new("IndexColumns", 3, new[] { "Schema", "Table", "Index" },
            new[] { "TABLE_OWNER", "TABLE_NAME", "INDEX_NAME" },
            new[] { ("COLUMN_NAME", Text), ("SORT_ORDER", Text), ("TABLE_SCHEMA", Text), ("TABLE_NAME", Text),
                ("INDEX_SCHEMA", Text), ("INDEX_NAME", Text), ("ORDINAL_POSITION", Number) },
            "SELECT COLUMN_NAME, DESCEND, TABLE_OWNER, TABLE_NAME, INDEX_OWNER, INDEX_NAME, COLUMN_POSITION " +
            "FROM ALL_IND_COLUMNS WHERE 1=1", "TABLE_OWNER, TABLE_NAME, INDEX_NAME, COLUMN_POSITION"),
        // Restrictions address the referencing (child) table and its constraint.
        new("ForeignKeys", 3, new[] { "Schema", "Table", "Name" },
            new[] { "C.OWNER", "C.TABLE_NAME", "C.CONSTRAINT_NAME" },
            new[] { ("TABLE_SCHEMA", Text), ("TABLE_NAME", Text), ("COLUMN_NAME", Text),
                ("FOREIGN_KEY_CONSTRAINT_NAME", Text), ("KEY_SEQUENCE", Number), ("REFERENCED_TABLE_SCHEMA", Text),
                ("REFERENCED_TABLE_NAME", Text), ("REFERENCED_COLUMN_NAME", Text), ("DELETE_RULE", Text) },
            "SELECT C.OWNER, C.TABLE_NAME, CC.COLUMN_NAME, C.CONSTRAINT_NAME, CC.POSITION, " +
            "RC.OWNER, RC.TABLE_NAME, RCC.COLUMN_NAME, C.DELETE_RULE FROM ALL_CONSTRAINTS C " +
            "JOIN ALL_CONS_COLUMNS CC ON CC.OWNER=C.OWNER AND CC.CONSTRAINT_NAME=C.CONSTRAINT_NAME AND CC.TABLE_NAME=C.TABLE_NAME " +
            "LEFT JOIN ALL_CONSTRAINTS RC ON RC.OWNER=C.R_OWNER AND RC.CONSTRAINT_NAME=C.R_CONSTRAINT_NAME " +
            "LEFT JOIN ALL_CONS_COLUMNS RCC ON RCC.OWNER=RC.OWNER AND RCC.CONSTRAINT_NAME=RC.CONSTRAINT_NAME " +
            "AND RCC.TABLE_NAME=RC.TABLE_NAME AND RCC.POSITION=CC.POSITION WHERE C.CONSTRAINT_TYPE='R'",
            "C.OWNER, C.TABLE_NAME, C.CONSTRAINT_NAME, CC.POSITION")
    };
}
