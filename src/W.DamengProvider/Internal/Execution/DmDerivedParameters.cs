using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;

namespace W.Dm.Internal.Execution;

internal readonly record struct DmDerivedParameterRow(string Name, int Position, string TypeName,
    string Direction, int Sequence);
internal readonly record struct DmDerivedPackageParameterRow(string Name, int ParameterId, string TypeName,
    string Direction);

/// <summary>Catalog discovery under one owned session, followed by atomic parameter publication.</summary>
internal static class DmDerivedParameters
{
    private const string CurrentSchemaSql = "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL";
    private const string StandaloneSql = "SELECT OBJECT_TYPE FROM ALL_OBJECTS " +
        "WHERE OWNER=:owner AND OBJECT_NAME=:name AND OBJECT_TYPE IN ('PROCEDURE','FUNCTION')";
    private const string PackageSql = "SELECT ID FROM SYS.SYSOBJECTS WHERE SCHID=CURRENT_SCHID() " +
        "AND TYPE$='SCHOBJ' AND SUBTYPE$='PKG' AND NAME=:name";
    private const string MemberSql = "SELECT DISTINCT MTDID FROM SYS.SYSPKGPROCINFOS " +
        "WHERE PKGID=:package_id AND NAME=:name";
    private const string ArgumentsSql = "SELECT ARGUMENT_NAME, POSITION, DATA_TYPE, IN_OUT, SEQUENCE " +
        "FROM ALL_ARGUMENTS WHERE OWNER=:owner AND OBJECT_NAME=:name ";
    private const string PackageArgumentsSql = "SELECT IOFLAG, TYPE$, NAME, PARAMID FROM SYS.SYSPKGPROCPARAMS " +
        "WHERE PKGID=:package_id AND MTDID=:member_id ORDER BY PARAMID";

    internal static void Derive(DmCommand source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        DmParameterCollection parameters = source.Parameters;
        using var mutation = parameters.PlanGate.BeginMutation();
        if (source.CommandType != CommandType.StoredProcedure)
            throw new InvalidOperationException("Parameter discovery requires CommandType.StoredProcedure.");
        string[] parts = DmProcedureCall.ParseIdentifier(source.CommandText);
        DmConnection connection = source.do_DbConnection;
        if (connection == null) throw new InvalidOperationException("Parameter discovery requires an open connection.");
        DmTransaction.ValidateCommandBinding(connection, source.do_DbTransaction);
        using var lease = connection.BeginExecution(DmOperationPurpose.Metadata,
            DmDeadline.FromSeconds(source.CommandTimeout, connection.OperationClock), source.CommandTimeout);
        DmTransaction.ValidateCommandBinding(connection, source.do_DbTransaction);

        List<object[]> schemaRows = Read(source, lease, CurrentSchemaSql);
        if (schemaRows.Count != 1 || schemaRows[0][0] is not string currentSchema || currentSchema.Length == 0)
            throw InvalidMetadata();
        string owner = parts.Length == 1 ? currentSchema : parts[0];
        string name = parts[parts.Length - 1];
        List<object[]> standalone = Read(source, lease, StandaloneSql, Text("owner", owner), Text("name", name));
        if (standalone.Count > 1) throw AmbiguousName();

        long packageId = 0;
        long? memberId = null;
        if (parts.Length == 2)
        {
            List<object[]> packages = Read(source, lease, PackageSql, Text("name", parts[0]));
            if (packages.Count > 1) throw AmbiguousName();
            if (packages.Count == 1)
            {
                packageId = Convert.ToInt64(packages[0][0], CultureInfo.InvariantCulture);
                List<object[]> members = Read(source, lease, MemberSql,
                    Number("package_id", packageId),
                    Text("name", name));
                memberId = UniqueMemberId(members);
            }
        }
        if (standalone.Count == 1 && memberId.HasValue) throw AmbiguousName();
        if (standalone.Count == 0 && !memberId.HasValue)
            throw new InvalidOperationException("The procedure or function is not visible in the selected schema.");

        List<DmParameter> replacement;
        if (memberId.HasValue)
        {
            using var query = CreatePackageArgumentsQuery(source, packageId, memberId.Value);
            List<object[]> argumentRows = Read(query, lease);
            var rows = new List<DmDerivedPackageParameterRow>(argumentRows.Count);
            foreach (object[] row in argumentRows)
                rows.Add(new DmDerivedPackageParameterRow(row[2] == DBNull.Value ? null : (string)row[2],
                    Convert.ToInt32(row[3], CultureInfo.InvariantCulture), (string)row[1], (string)row[0]));
            replacement = ProjectPackage(rows);
        }
        else
        {
            bool function = standalone[0][0] switch
            {
                "FUNCTION" => true,
                "PROCEDURE" => false,
                _ => throw InvalidMetadata()
            };
            List<object[]> argumentRows = Read(source, lease, ArgumentsSql + "AND PACKAGE_NAME IS NULL ORDER BY SEQUENCE",
                Text("owner", owner), Text("name", name));
            var rows = new List<DmDerivedParameterRow>(argumentRows.Count);
            foreach (object[] row in argumentRows)
                rows.Add(new DmDerivedParameterRow(row[0] == DBNull.Value ? null : (string)row[0],
                    Convert.ToInt32(row[1], CultureInfo.InvariantCulture), (string)row[2], (string)row[3],
                    Convert.ToInt32(row[4], CultureInfo.InvariantCulture)));
            replacement = Project(rows, function);
        }

        // Every catalog reader has closed before conversion/publication. Conversion,
        // invalid metadata, duplicate names and disposal all leave the old set intact.
        parameters.ReplaceDerivedParameters(replacement);
    }

    internal static List<DmParameter> Project(IEnumerable<DmDerivedParameterRow> descriptions, bool function)
    {
        if (descriptions == null) throw new ArgumentNullException(nameof(descriptions));
        var rows = new List<DmDerivedParameterRow>(descriptions);
        rows.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        var result = new List<DmParameter>(rows.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (function && rows.Count == 0) throw InvalidMetadata();
        int nextPosition = 0;
        int lastSequence = -1;
        foreach (DmDerivedParameterRow row in rows)
        {
            if (row.Sequence < 0 || row.Sequence <= lastSequence || row.Position != nextPosition++)
                throw InvalidMetadata();
            lastSequence = row.Sequence;
            string direction = row.Direction?.Trim().ToUpperInvariant();
            ParameterDirection mode = direction switch
            {
                "IN" => ParameterDirection.Input,
                "OUT" => ParameterDirection.Output,
                "IN/OUT" => ParameterDirection.InputOutput,
                _ => throw InvalidMetadata()
            };
            // Both procedures and functions start at position zero on DM. Only a
            // FUNCTION object's first row is its return, even when named V_RET.
            if (function && row.Position == 0)
            {
                if (mode != ParameterDirection.Output) throw InvalidMetadata();
                mode = ParameterDirection.ReturnValue;
            }
            result.Add(Parameter(row.Name, row.TypeName, mode, names));
        }
        return result;
    }

    internal static List<DmParameter> ProjectPackage(IEnumerable<DmDerivedPackageParameterRow> descriptions)
    {
        if (descriptions == null) throw new ArgumentNullException(nameof(descriptions));
        var rows = new List<DmDerivedPackageParameterRow>(descriptions);
        rows.Sort((left, right) => left.ParameterId.CompareTo(right.ParameterId));
        var result = new List<DmParameter>(rows.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int? previousId = null;
        bool hasReturn = false;
        foreach (DmDerivedPackageParameterRow row in rows)
        {
            if (previousId == row.ParameterId) throw InvalidMetadata();
            previousId = row.ParameterId;
            ParameterDirection mode = row.Direction?.Trim().ToUpperInvariant() switch
            {
                "I" => ParameterDirection.Input,
                "O" => ParameterDirection.Output,
                "IO" => ParameterDirection.InputOutput,
                "R" => ParameterDirection.ReturnValue,
                _ => throw InvalidMetadata()
            };
            if (mode == ParameterDirection.ReturnValue)
            {
                if (hasReturn) throw InvalidMetadata();
                hasReturn = true;
            }
            result.Add(Parameter(row.Name, row.TypeName, mode, names));
        }
        return result;
    }

    internal static long? UniqueMemberId(IReadOnlyList<object[]> members)
    {
        if (members.Count > 1) throw AmbiguousName();
        return members.Count == 0 ? (long?)null : Convert.ToInt64(members[0][0], CultureInfo.InvariantCulture);
    }

    private static DmParameter Parameter(string name, string catalogType, ParameterDirection mode, HashSet<string> names)
    {
        if (mode == ParameterDirection.ReturnValue && string.IsNullOrEmpty(name)) name = "RETURN_VALUE";
        if (string.IsNullOrEmpty(name) || name is "?" or ":?" or "@?") throw InvalidMetadata();
        string normalized = name[0] is ':' or '@' ? name.Substring(1) : name;
        if (normalized.Length == 0 || !names.Add(normalized)) throw InvalidMetadata();
        if (string.IsNullOrWhiteSpace(catalogType)) throw InvalidMetadata();
        string typeName = catalogType.Trim().ToUpperInvariant();
        int type = typeName == "SYS_REFCURSOR" ? 120 : DmSqlType.TypeNameToCType(typeName);
        DmDbType providerType = type switch
        {
            22 => DmDbType.TimeOffset,
            23 => DmDbType.DateTimeOffset,
            _ => DmSqlType.CTypeToDmDbType(type)
        };
        return new DmParameter(name, providerType, mode);
    }

    private static DmParameter Text(string name, string value) =>
        new() { ParameterName = name, DbType = DbType.String, Value = value };
    private static DmParameter Number(string name, long value) =>
        new() { ParameterName = name, DbType = DbType.Int64, Value = value };

    internal static DmCommand CreatePackageArgumentsQuery(DmCommand source, long packageId, long memberId) =>
        CreateQuery(source, PackageArgumentsSql, Number("package_id", packageId), Number("member_id", memberId));

    private static List<object[]> Read(DmCommand source, DmExecutionLease lease, string sql,
        params DmParameter[] values)
    {
        using var query = CreateQuery(source, sql, values);
        return Read(query, lease);
    }

    private static DmCommand CreateQuery(DmCommand source, string sql, params DmParameter[] values)
    {
        var query = new DmCommand(sql, source.do_DbConnection, source.do_DbTransaction)
        { CommandTimeout = source.CommandTimeout };
        foreach (DmParameter value in values) query.Parameters.Add(value);
        return query;
    }

    private static List<object[]> Read(DmCommand query, DmExecutionLease lease)
    {
        using var reader = query.ExecuteInternalReader(lease, CommandBehavior.Default);
        var rows = new List<object[]>();
        while (reader.do_Read())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }
        return rows;
    }

    private static InvalidOperationException AmbiguousName() => new(
        "The procedure name is ambiguous or overloaded; parameter discovery cannot select one signature.");
    private static InvalidOperationException InvalidMetadata() => new(
        "The procedure parameter catalog is incomplete or ambiguous.");
}
