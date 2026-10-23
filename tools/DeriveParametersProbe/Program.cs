using System.Data;
using System.Diagnostics;
using System.Text.Json;
using W.Dm;

internal static class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private static readonly string Prefix = "DP_" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    private static readonly List<(string Kind, string Name)> Owned = [];
    private static readonly List<string> Attempted = [];
    private static readonly List<object> Results = [];
    private static readonly HashSet<DmConnection> VerifiedConnections = [];
    private static string Raw = "";
    private static string Phase = "setup";
    private static int Failures;
    private static string Name(string suffix) => Prefix + "_" + suffix;
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private static int Main(string[] args)
    {
        bool skipPackage = args.Contains("--standalone-only") || args.Contains("--skip-package");
        Raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING") ?? "";
        if (Raw.Length == 0)
        {
            Console.WriteLine("integration_pending");
            return 2;
        }
        try
        {
            using (var connection = Open())
            {
                Results.Add(new { runtime = Environment.Version.ToString(), assembly = typeof(DmConnection).Assembly.Location,
                    identity = TestUser, server = connection.ServerVersion });
                if (args.Contains("--catalog-shape"))
                {
                    CatalogShape(connection);
                    return 0;
                }
                Create(connection, "PROCEDURE", Name("P"),
                    "(A IN INT, B OUT INT, C IN OUT INT) AS BEGIN B := A + 10; C := C + A; END;");
                Create(connection, "FUNCTION", Name("F"),
                    "(A IN INT, B IN INT) RETURN INT AS BEGIN RETURN A * 100 + B; END;");
                Create(connection, "PROCEDURE", Name("Z"), "AS BEGIN NULL; END;");
                Create(connection, "PROCEDURE", Name("Mi.x'\"Case"),
                    "(A IN INT, B OUT INT) AS BEGIN B := A + 1; END;");
                foreach (var item in Owned) CatalogRows(connection, item.Name, false);
            }

            Case("procedure", connection => CheckProcedure(connection, Quote(Name("P"))));
            Case("schema_qualified", connection => CheckProcedure(connection, Quote(TestUser) + "." + Quote(Name("P"))));
            Case("function_return", connection =>
            {
                using var command = Derive(connection, Quote(Name("F")));
                Require(command.Parameters.Count == 3, "function_count");
                Require(command.Parameters[0].Direction == ParameterDirection.ReturnValue, "function_return_direction");
                Require(((DmParameter)command.Parameters[0]).DmSqlType == DmDbType.Int32, "function_return_type");
                CheckNames(command, 1, "A", "B");
                command.Parameters[1].Value = 3; command.Parameters[2].Value = 4;
                ExecuteProcedure(command);
                Require(Convert.ToInt32(command.Parameters[0].Value) == 304, "function_return_value");
            });
            Case("zero_parameters", connection =>
            {
                using var command = Derive(connection, Quote(Name("Z")));
                Require(command.Parameters.Count == 0, "zero_parameter_count");
                ExecuteProcedure(command);
            });
            Case("quoted_punctuation", connection =>
            {
                using var command = Derive(connection, Quote(Name("Mi.x'\"Case")));
                CheckNames(command, 0, "A", "B");
                Require(command.Parameters.Count == 2, "quoted_parameter_count");
                command.Parameters[0].Value = 6;
                ExecuteProcedure(command);
                Require(Convert.ToInt32(command.Parameters[1].Value) == 7, "quoted_parameter_value");
            });
            Case("missing_preserves_original", connection => RejectDiscovery(connection, Quote(Name("MISSING")), typeof(InvalidOperationException)));
            Case("invalid_preserves_original", connection => RejectDiscovery(connection, "p'; SELECT 1 FROM DUAL --", typeof(ArgumentException)));
            Case("three_parts_rejected", connection => RejectDiscovery(connection, "a.b.c", typeof(ArgumentException)));
            Case("transaction_binding", connection =>
            {
                using var transaction = (DmTransaction)connection.BeginTransaction();
                using var command = new DmCommand(Quote(Name("P")), connection, transaction)
                { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
                Phase = "derive";
                DmCommandBuilder.DeriveParameters(command);
                Phase = "validate_metadata";
                Require(command.Parameters.Count == 3, "transaction_parameter_count");
                using var check = new DmCommand("SELECT 1 FROM DUAL", connection, transaction);
                Require(Convert.ToInt32(check.ExecuteScalar()) == 1, "transaction_connection_reuse");
                transaction.Rollback();
            });

            // The shared TEST account has no CREATE PACKAGE privilege. Explicitly
            // skip that lane there; the default remains available to authorized environments.
            string packageNotRunReason = skipPackage ? "missing_create_package_privilege" : "package_setup_failed";
            if (skipPackage) Results.Add(new { name = "create_package", status = "not_run",
                integration_status = "integration_pending", reason = packageNotRunReason });
            bool packageReady = !skipPackage && Case("create_package", connection =>
            {
                Create(connection, "PACKAGE", Name("PKG"), "AS " +
                    "PROCEDURE P(A IN INT, B OUT INT); PROCEDURE Z; " +
                    "FUNCTION F(A IN INT) RETURN INT; FUNCTION F0 RETURN INT; " +
                    "PROCEDURE O(A IN INT); PROCEDURE O(A IN VARCHAR); END;");
                CatalogRows(connection, Name("PKG"), true);
                Phase = "package_body";
                ExecuteOwnedDdl(connection, "CREATE PACKAGE BODY " + Quote(Name("PKG")) + " AS " +
                    "PROCEDURE P(A IN INT, B OUT INT) AS BEGIN B := A + 1; END; " +
                    "PROCEDURE Z AS BEGIN NULL; END; " +
                    "FUNCTION F(A IN INT) RETURN INT AS BEGIN RETURN A + 1; END; " +
                    "FUNCTION F0 RETURN INT AS BEGIN RETURN 17; END; " +
                    "PROCEDURE O(A IN INT) AS BEGIN NULL; END; " +
                    "PROCEDURE O(A IN VARCHAR) AS BEGIN NULL; END; END;");
                CatalogRows(connection, Name("PKG"), true);
            });
            PackageCase("package_parameters", packageReady, packageNotRunReason, connection =>
            {
                using var command = Derive(connection, PackageMember("P"));
                Require(command.Parameters.Count == 2, "package_parameter_count");
                CheckNames(command, 0, "A", "B");
                Require(command.Parameters[0].Direction == ParameterDirection.Input &&
                    command.Parameters[1].Direction == ParameterDirection.Output, "package_directions");
            });
            PackageCase("package_zero", packageReady, packageNotRunReason, connection =>
            {
                using var command = Derive(connection, PackageMember("Z"));
                Require(command.Parameters.Count == 0, "package_zero_count");
            });
            PackageCase("package_function", packageReady, packageNotRunReason, connection => CheckPackageFunction(connection, "F", 2));
            PackageCase("package_zero_function", packageReady, packageNotRunReason, connection => CheckPackageFunction(connection, "F0", 1));
            PackageCase("package_overload_rejected", packageReady, packageNotRunReason, connection => RejectDiscovery(connection, PackageMember("O"), typeof(InvalidOperationException)));
            PackageCase("package_missing_rejected", packageReady, packageNotRunReason, connection => RejectDiscovery(connection, PackageMember("MISSING"), typeof(InvalidOperationException)));
        }
        catch (Exception error) { Failures++; Results.Add(new { fatal = Error(error) }); }
        finally
        {
            Cleanup();
            Console.WriteLine(JsonSerializer.Serialize(new { failures = Failures, package_integration_pending = skipPackage,
                results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Failures == 0 ? 0 : 1;
    }

    private static string PackageMember(string member) => Quote(Name("PKG")) + "." + Quote(member);
    private static void CheckPackageFunction(DmConnection connection, string member, int count)
    {
        using var command = Derive(connection, PackageMember(member));
        Require(command.Parameters.Count == count, "package_function_count");
        DmParameter[] returns = command.Parameters.Cast<DmParameter>()
            .Where(parameter => parameter.Direction == ParameterDirection.ReturnValue).ToArray();
        Require(returns.Length == 1, "package_function_return");
        Require(returns[0].DmSqlType == DmDbType.Int32, "package_function_type");
        DmParameter[] arguments = command.Parameters.Cast<DmParameter>()
            .Where(parameter => parameter.Direction != ParameterDirection.ReturnValue).ToArray();
        Require(arguments.Length == count - 1, "package_function_argument_count");
        if (count == 2)
            Require(arguments[0].ParameterName == "A" && arguments[0].Direction == ParameterDirection.Input,
                "package_function_argument");
    }

    private static void CheckProcedure(DmConnection connection, string name)
    {
        using var command = Derive(connection, name);
        Require(command.Parameters.Count == 3, "procedure_count");
        CheckNames(command, 0, "A", "B", "C");
        Require(command.Parameters.Cast<DmParameter>().Select(p => p.Direction).SequenceEqual(new[]
            { ParameterDirection.Input, ParameterDirection.Output, ParameterDirection.InputOutput }), "procedure_directions");
        Require(command.Parameters.Cast<DmParameter>().All(p => p.DmSqlType == DmDbType.Int32), "procedure_types");
        command.Parameters[0].Value = 2; command.Parameters[2].Value = 5;
        ExecuteProcedure(command);
        Require(Convert.ToInt32(command.Parameters[1].Value) == 12 && Convert.ToInt32(command.Parameters[2].Value) == 7,
            "procedure_values");
    }

    private static void CheckNames(DmCommand command, int offset, params string[] names) =>
        Require(names.Select((name, index) => command.Parameters[index + offset].ParameterName == name).All(x => x), "parameter_names");

    private static DmCommand Derive(DmConnection connection, string name)
    {
        var command = new DmCommand(name, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
        try
        {
            var collection = command.Parameters;
            var original = new DmParameter("original", 17); collection.Add(original);
            Phase = "derive";
            DmCommandBuilder.DeriveParameters(command);
            Phase = "validate_metadata";
            Require(ReferenceEquals(collection, command.Parameters), "collection_identity");
            using var ownershipCheck = new DmCommand();
            ownershipCheck.Parameters.Add(original);
            return command;
        }
        catch { command.Dispose(); throw; }
    }

    private static void RejectDiscovery(DmConnection connection, string name, Type expected)
    {
        using var command = new DmCommand(name, connection) { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
        var original = new DmParameter("original", 17); command.Parameters.Add(original);
        Exception? error = null;
        Phase = "derive_expected_rejection";
        try { DmCommandBuilder.DeriveParameters(command); } catch (Exception caught) { error = caught; }
        Require(error != null && expected.IsInstanceOfType(error), "expected_discovery_rejection");
        Require(command.Parameters.Count == 1 && ReferenceEquals(command.Parameters[0], original) &&
            Convert.ToInt32(original.Value) == 17, "failed_discovery_changed_parameters");
        Results.Add(new { expected_rejection = Error(error!) });
    }

    private static bool Case(string name, Action<DmConnection> test)
    {
        DmConnection? connection = null;
        bool passed = false, reusable = false;
        try { Phase = "case_start"; connection = Open(); test(connection); passed = true; }
        catch (Exception error) { Failures++; Results.Add(new { name, error = Error(error) }); }
        finally
        {
            if (connection != null)
            {
                try { Phase = "same_connection_select"; Require(Convert.ToInt32(Scalar(connection, "SELECT 1 FROM DUAL")) == 1, "same_connection_select"); reusable = true; }
                catch (Exception error) { Failures++; Results.Add(new { name, reuse_error = Error(error) }); }
                connection.Dispose();
            }
            Results.Add(new { name, passed, same_connection_select = reusable });
        }
        return passed;
    }

    private static void PackageCase(string name, bool ready, string notRunReason, Action<DmConnection> test)
    {
        if (ready) Case(name, test);
        else Results.Add(new { name, status = "not_run", integration_status = "integration_pending", reason = notRunReason });
    }

    private static DmConnection Open()
    {
        string previousPhase = Phase;
        Phase = "connection_open";
        var builder = new DmConnectionStringBuilder(Raw) { TransportSecurity = DmTransportSecurity.PlaintextAllowed,
            PersistSecurityInfo = false, ConnPooling = false, PreparePooling = false, StmtPooling = false,
            ConnectionTimeout = 10000, Schema = TestUser };
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "configured_test_identity");
        var connection = new DmConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            Phase = "connection_identity";
            Require(Convert.ToString(Scalar(connection, "SELECT USER FROM DUAL"))?.Trim() == TestUser, "server_test_identity");
            Require(Convert.ToString(Scalar(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"))?.Trim() == TestUser, "server_test_schema");
            VerifiedConnections.Add(connection);
            Phase = previousPhase;
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static void Create(DmConnection connection, string kind, string name, string body)
    {
        Require(!Exists(connection, name), "unique_object_name");
        Attempted.Add(name);
        Phase = kind == "PACKAGE" ? "package_spec" : "create_" + kind.ToLowerInvariant();
        ExecuteOwnedDdl(connection, "CREATE " + kind + " " + Quote(name) + " " + body);
        Owned.Add((kind, name));
    }

    private static bool Exists(DmConnection connection, string name)
    {
        using var command = new DmCommand("SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME=:name", connection);
        command.Parameters.Add(new DmParameter("name", DmDbType.VarChar) { Value = name });
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    private static void Cleanup()
    {
        foreach (var item in Owned.AsEnumerable().Reverse())
        {
            try { using var connection = Open(); Execute(connection, "DROP " + item.Kind + " " + Quote(item.Name)); }
            catch (Exception error) { Failures++; Results.Add(new { cleanup_object = item.Name, error = Error(error) }); }
        }
        if (Attempted.Count == 0) return;
        try
        {
            using var fresh = Open();
            var remaining = Attempted.Where(name => Exists(fresh, name)).ToArray();
            Require(remaining.Length == 0, "cleanup_absence");
            Results.Add(new { cleanup_verified = true, exact_objects = Attempted.ToArray() });
        }
        catch (Exception error) { Failures++; Results.Add(new { cleanup_verified = false, error = Error(error) }); }
    }

    private static void CatalogShape(DmConnection connection)
    {
        foreach (string table in new[] { "SYS.SYSPKGPROCINFOS", "SYS.SYSPKGPROCPARAMS", "ALL_ARGUMENTS" })
        {
            using var command = new DmCommand("SELECT * FROM " + table + " WHERE 1=0", connection);
            using var reader = command.ExecuteReader();
            Results.Add(new { catalog = table, columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new { name = reader.GetName(i), type = reader.GetDataTypeName(i) }).ToArray() });
        }
    }

    private static void CatalogRows(DmConnection connection, string name, bool package)
    {
        Require(Owned.Any(item => item.Name == name), "catalog_object_ownership");
        Phase = "catalog_rows";
        string restriction = package ? "PACKAGE_NAME=:name" : "OBJECT_NAME=:name AND PACKAGE_NAME IS NULL";
        using var command = new DmCommand("SELECT OBJECT_NAME, PACKAGE_NAME, ARGUMENT_NAME, POSITION, DATA_TYPE, IN_OUT, SEQUENCE, SUBPROGRAM_ID " +
            "FROM ALL_ARGUMENTS WHERE OWNER=:owner AND " + restriction + " ORDER BY OBJECT_NAME, SUBPROGRAM_ID, SEQUENCE", connection);
        command.Parameters.Add(new DmParameter("owner", DmDbType.VarChar) { Value = TestUser });
        command.Parameters.Add(new DmParameter("name", DmDbType.VarChar) { Value = name });
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        while (reader.Read()) rows.Add(new
        {
            object_name = reader.GetValue(0), package_name = reader.IsDBNull(1) ? null : reader.GetValue(1),
            argument_name = reader.IsDBNull(2) ? null : reader.GetValue(2), position = reader.GetValue(3),
            data_type = reader.GetValue(4), direction = reader.GetValue(5), sequence = reader.GetValue(6), subprogram = reader.GetValue(7)
        });
        Results.Add(new { catalog_object = name, catalog = "ALL_ARGUMENTS", rows });
    }

    private static void ExecuteOwnedDdl(DmConnection connection, string sql)
    {
        Require(VerifiedConnections.Contains(connection), "ddl_verified_connection");
        try { Execute(connection, sql); }
        catch (DmException error) when (error.ErrorKind == DmErrorKind.Server)
        {
            // Only our fixed DDL on a verified TEST connection reaches this path.
            // Login/connect failures never expose their messages.
            string message = error.Message.Replace(Raw, "[connection]", StringComparison.OrdinalIgnoreCase);
            var settings = new DmConnectionStringBuilder(Raw);
            foreach (string value in new[] { settings.Password, settings.User, settings.Server })
                if (!string.IsNullOrEmpty(value)) message = message.Replace(value, "[redacted]", StringComparison.OrdinalIgnoreCase);
            message = new string(message.Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t').ToArray());
            Results.Add(new { phase = Phase, owned_ddl_server_message = message, error = Error(error) });
            throw;
        }
    }

    private static void ExecuteProcedure(DmCommand command)
    {
        Phase = "execute";
        command.ExecuteNonQuery();
        Phase = "validate_values";
    }

    private static object? Scalar(DmConnection connection, string sql)
    { using var command = new DmCommand(sql, connection) { CommandTimeout = 15 }; return command.ExecuteScalar(); }
    private static void Execute(DmConnection connection, string sql)
    { using var command = new DmCommand(sql, connection) { CommandTimeout = 15 }; command.ExecuteNonQuery(); }
    private static object Error(Exception error) => new { type = error.GetType().Name, phase = Phase,
        assertion = error is ProbeFailure ? error.Message : null, diagnostics = JsonDocument.Parse(DmDiagnostics.FormatException(error)).RootElement,
        stack = new StackTrace(error, true).GetFrames()?.Select(frame => new
        { method = frame.GetMethod()?.DeclaringType?.FullName + "." + frame.GetMethod()?.Name, file = frame.GetFileName(), line = frame.GetFileLineNumber() }).ToArray() };
    private static void Require(bool condition, string message) { if (!condition) throw new ProbeFailure(message); }
    private sealed class ProbeFailure(string message) : Exception(message);
}
