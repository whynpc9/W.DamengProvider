using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using W.Dm;

const string TestUser = "WDM_PROVIDER_TEST";
var result = new Dictionary<string, object?>();
var failures = new List<string>();
var firstChance = new System.Runtime.CompilerServices.ConditionalWeakTable<Exception, string>();
AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
{
    if (e.Exception is InvalidOperationException && !firstChance.TryGetValue(e.Exception, out _))
        firstChance.Add(e.Exception, e.Exception.StackTrace ?? "");
};
var created = new List<(string Kind, string Name)>();
string nonce = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
string table = "I10_Mix'%_" + nonce, counter = "I10_COUNT_" + nonce, function = "I10_FN_" + nonce;
string child = "I10_CHILD_" + nonce, view = "I10_VIEW_" + nonce;
string? raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING");
if (raw == null) { Console.WriteLine("integration_pending: no explicit TEST connection"); return 2; }
var builder = new DmConnectionStringBuilder(raw)
{
    ConnPooling = false, PreparePooling = false, StmtPooling = false,
    TransportSecurity = DmTransportSecurity.PlaintextAllowed, ConnectionTimeout = 10000
};
if (!builder.User.Equals(TestUser, StringComparison.OrdinalIgnoreCase))
{ Console.WriteLine("configured_test_identity_invalid"); return 2; }
builder.Schema = TestUser;
string settings = builder.ConnectionString;
string stage = "identity";
try
{
    using (var connection = Open())
        result["identity"] = new { user = TestUser, schema = TestUser, version = connection.ServerVersion };
    if (!args.Contains("--identity-only"))
    {
        stage = "objects";
        Create("TABLE", counter, $"CREATE TABLE {Q(counter)} (N INT NOT NULL)");
        Execute($"INSERT INTO {Q(counter)} VALUES (0)");
        Create("TABLE", table, $"CREATE TABLE {Q(table)} (\"Pk A\" INT NOT NULL, \"Pk B\" INT NOT NULL, \"Single Unique\" INT UNIQUE, \"Pair A\" INT, \"Pair B\" INT, \"Amount\" DECIMAL(18,4), \"When\" TIMESTAMP(6), \"Text\" CLOB, \"Bytes\" BLOB, PRIMARY KEY(\"Pk A\",\"Pk B\"), UNIQUE(\"Pair A\",\"Pair B\"))");
        Execute($"INSERT INTO {Q(table)} (\"Pk A\",\"Pk B\",\"Single Unique\",\"Pair A\",\"Pair B\",\"Amount\",\"When\") VALUES (1,2,3,4,5,12.3456,TIMESTAMP '2026-10-15 01:02:03.123456')");
        stage = "function_create";
        Create("FUNCTION", function, $"CREATE FUNCTION {Q(function)} (P INT) RETURN INT AS PRAGMA AUTONOMOUS_TRANSACTION; BEGIN UPDATE {Q(counter)} SET N=N+P; COMMIT; RETURN 1; END;");
        stage = "function_positive_control";
        using (var connection = Open()) Scalar(connection, $"SELECT {Q(function)}(1) FROM DUAL");
        string before = Count();
        Check("side_effect_positive_control", before == "1");
        stage = "prepare";
        var prepared = new List<object>();
        foreach (bool asynchronous in new[] { false, true })
        foreach (string shape in new[] { "ordinary", "parameter", "quoted_types", "side_effect" })
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = shape switch
            {
                "ordinary" => "SELECT 1 AS N FROM DUAL",
                "parameter" => $"SELECT * FROM {Q(table)} WHERE \"Pk A\"=:p",
                "quoted_types" => $"SELECT * FROM {Q(table)}",
                _ => $"SELECT {Q(function)}(1) AS N FROM DUAL"
            };
            if (shape == "parameter") command.Parameters.Add(new DmParameter("p", 1));
            using var trace = new PrepareTrace((DmCommand)command);
            if (asynchronous) await command.PrepareAsync(); else command.Prepare();
            object statement = Reflect.GetProperty(command, "Statement")!;
            object info = Reflect.Call(statement, "F")!;
            var columns = ((Array)Reflect.Call(info, "GetColumnsInfo")!).Cast<object>().Select(c =>
                new { name = Reflect.Call(c, "GetName"), schema = Reflect.Call(c, "GetSchema"), table = Reflect.Call(c, "GetTable"),
                    base_name = Reflect.Call(c, "GetBaseColumn"), type = Reflect.Field(c, "type"), precision = Reflect.Field(c, "prec"),
                    scale = Reflect.Field(c, "scale"), nullable = Reflect.Field(c, "nullable") }).ToArray();
            int parameterCount = (int)Reflect.Call(info, "GetParameterCount")!;
            trace.Dispose();
            string id = (asynchronous ? "async_" : "sync_") + shape;
            Check(id + "_columns", columns.Length == (shape is "ordinary" or "side_effect" ? 1 : 9));
            Check(id + "_parameters", parameterCount == (shape == "parameter" ? 1 : 0));
            Check(id + "_describe_only_wire", trace.Frames.Count == 1 && trace.Frames.All(f => f.Opcode == 5 && !f.Execute));
            Check(id + "_no_side_effect", Count() == before);
            prepared.Add(new { id, parameter_count = parameterCount, columns, frames = trace.Frames });
        }
        result["prepare"] = prepared;
        result["side_effect_counter"] = new { before, after = Count() };
        if (args.Contains("--full"))
        {
            stage = "full"; await Full();
            string finalCounter = Count();
            result["side_effect_counter_after_full"] = finalCounter;
            Check("full_final_side_effect_count", finalCounter == before);
        }
    }
}
catch (Exception error)
{
    failures.Add(stage);
    result["error"] = new { stage, type = error.GetType().Name, inner = error.InnerException?.GetType().Name,
        diagnostics = JsonDocument.Parse(DmDiagnostics.FormatException(error)).RootElement };
    if (stage == "identity") result["status"] = "integration_pending";
}
finally
{
    if (created.Count > 0)
    {
        var dropped = new List<string>();
        foreach (var item in created.AsEnumerable().Reverse())
            try { Execute($"DROP {item.Kind} {Q(item.Name)}"); dropped.Add(item.Name); }
            catch (Exception error) { failures.Add("cleanup_" + item.Name); result["cleanup_error"] = DmDiagnostics.FormatException(error); }
        try
        {
            using var check = Open();
            string remaining = Scalar(check, "SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME IN (" + string.Join(",", created.Select(x => L(x.Name))) + ")");
            Check("cleanup_remaining", remaining == "0");
            result["cleanup"] = new { created, dropped, remaining };
        }
        catch (Exception error) { failures.Add("cleanup_verify"); result["cleanup_verify_error"] = DmDiagnostics.FormatException(error); }
    }
}
result["failures"] = failures;
result["accepted"] = failures.Count == 0;
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
return failures.Count == 0 ? 0 : 1;

async Task Full()
{
    Create("TABLE", child, $"CREATE TABLE {Q(child)} (A INT, B INT, FOREIGN KEY(A,B) REFERENCES {Q(table)}(\"Pk A\",\"Pk B\"))");
    Create("VIEW", view, $"CREATE VIEW {Q(view)} AS SELECT * FROM {Q(table)}");
    var collections = new List<object>();
    result["collections"] = collections;
    using (var connection = Open())
    {
        DataTable names = connection.GetSchema();
        Check("sixteen_collections", names.Rows.Count == 16);
        foreach (DataRow row in names.Rows)
        {
            string name = (string)row["CollectionName"];
            if (name == "ReservedWords")
            {
                foreach (bool asynchronous in new[] { false, true })
                {
                    using var permissionConnection = Open();
                    try
                    {
                        var words = asynchronous ? await permissionConnection.GetSchemaAsync(name) : permissionConnection.GetSchema(name);
                        Check("reserved_words_nonempty", words.Rows.Count > 0);
                        collections.Add(new { name, asynchronous, rows = words.Rows.Count });
                    }
                    catch (DmException error) when (error.Number == -5504)
                    {
                        Check("permission_state", permissionConnection.State == ConnectionState.Broken && error.FailureInfo?.ConnectionReusable == false);
                        collections.Add(new { name, asynchronous, capability = "current_TEST_permission_denied", error_number = error.Number,
                            state = permissionConnection.State.ToString(), diagnostics = DmDiagnostics.FormatException(error) });
                    }
                }
                continue;
            }
            string[] restrictions = name switch
            {
                "Tables" or "Columns" or "Indexes" or "PrimaryKeys" or "IndexColumns" => new[] { TestUser, table },
                "ForeignKeys" => new[] { TestUser, child },
                "Procedures" or "Parameters" => new[] { TestUser, function },
                "Users" or "Schemas" or "Databases" => new[] { TestUser },
                _ => Array.Empty<string>()
            };
            try
            {
                using var catalogConnection = Open();
                DataTable sync = catalogConnection.GetSchema(name, restrictions);
                DataTable asyncTable = await catalogConnection.GetSchemaAsync(name.ToLowerInvariant(), restrictions);
                bool same = Canonical(sync) == Canonical(asyncTable);
                Check("collection_parity_" + name, same);
                Check("collection_nonempty_" + name, sync.Rows.Count > 0);
                collections.Add(new { name, rows = sync.Rows.Count, same, data = name is "Columns" or "PrimaryKeys" or "ForeignKeys" or "Parameters" ? SchemaRows(sync) : null });
                if (name == "Columns")
                {
                    Check("columns_count", sync.Rows.Count == 9);
                    DataRow amount = sync.Rows.Cast<DataRow>().Single(r => (string)r["COLUMN_NAME"] == "Amount");
                    Check("decimal_catalog", Convert.ToInt32(amount["NUMERIC_PRECISION"]) == 18 && Convert.ToInt32(amount["COLUMN_SCALE"]) == 4);
                    DataRow temporal = sync.Rows.Cast<DataRow>().Single(r => (string)r["COLUMN_NAME"] == "When");
                    Check("temporal_catalog", Convert.ToInt32(temporal["DATETIME_PRECISION"]) == 6);
                }
                if (name == "PrimaryKeys")
                    Check("pk_order", sync.Rows.Count == 2 && (string)sync.Rows[0]["COLUMN_NAME"] == "Pk A" &&
                        (string)sync.Rows[1]["COLUMN_NAME"] == "Pk B" && Convert.ToInt32(sync.Rows[1]["KEY_SEQUENCE"]) == 2);
                if (name == "ForeignKeys")
                    Check("fk_direction_order", sync.Rows.Count == 2 && (string)sync.Rows[0]["TABLE_NAME"] == child &&
                        (string)sync.Rows[0]["REFERENCED_TABLE_NAME"] == table && (string)sync.Rows[1]["REFERENCED_COLUMN_NAME"] == "Pk B");
            }
            catch (Exception error)
            {
                failures.Add("collection_" + name);
                collections.Add(new { name, type = error.GetType().Name, location = error.TargetSite?.DeclaringType?.FullName + "." + error.TargetSite?.Name, stack = firstChance.TryGetValue(error, out var originalStack) ? originalStack : error.StackTrace, diagnostics = DmDiagnostics.FormatException(error) });
            }
        }
        foreach (string objectName in new[] { table, view })
        {
            var a = connection.GetSchema("Tables", new[] { TestUser, objectName });
            var b = await connection.GetSchemaAsync("Tables", new[] { TestUser, objectName });
            Check("quoted_view_tables_" + objectName, a.Rows.Count == 1 && Canonical(a) == Canonical(b));
        }
        Check("exact_restriction_not_pattern", (await connection.GetSchemaAsync("Tables", new[] { TestUser, "I10_%" })).Rows.Count == 0);
        Check("empty_restriction", (await connection.GetSchemaAsync("Tables", new[] { TestUser, "" })).Rows.Count == 0);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await connection.GetSchemaAsync("Columns", new[] { TestUser, table }, canceled.Token); failures.Add("precancel_not_canceled"); }
        catch (OperationCanceledException) { }
        Check("precancel_reusable", Scalar(connection, "SELECT 1 FROM DUAL") == "1");
    }
    result["collections"] = collections;
    var readers = new List<object>();
    result["readers"] = readers;
    foreach (bool asynchronous in new[] { false, true })
    foreach (CommandBehavior behavior in new[] { CommandBehavior.Default, CommandBehavior.SchemaOnly, CommandBehavior.KeyInfo, CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo })
    {
        string id = (asynchronous ? "async_" : "sync_") + behavior.ToString();
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {Q(table)} WHERE \"Pk A\"=:p";
            command.Parameters.Add(new DmParameter("p", 1));
            await using DbDataReader reader = asynchronous ? await command.ExecuteReaderAsync(behavior) : command.ExecuteReader(behavior);
            DataTable schema = (asynchronous ? await reader.GetSchemaTableAsync() : reader.GetSchemaTable())!;
            Check(id + "_columns", schema.Rows.Count == 9);
            DataRow amount = schema.Rows[5], temporal = schema.Rows[6];
            Check(id + "_decimal", (int)amount["NumericPrecision"] == 18 && (int)amount["NumericScale"] == 4);
            Check(id + "_time", (int)temporal["NumericScale"] == 6);
            Check(id + "_lob", (bool)schema.Rows[7]["IsLong"] && (bool)schema.Rows[8]["IsLong"]);
            Check(id + "_clr_types", reader.GetFieldType(5) == typeof(decimal) && reader.GetFieldType(6) == typeof(DateTime) && reader.GetFieldType(7) == typeof(string) && reader.GetFieldType(8) == typeof(byte[]));
            Check(id + "_source", (string)schema.Rows[0]["BaseTableName"] == table && (string)schema.Rows[0]["BaseColumnName"] == "Pk A");
            if (behavior.HasFlag(CommandBehavior.KeyInfo))
            {
                Check(id + "_pk", Equals(schema.Rows[0]["IsKey"], true) && Equals(schema.Rows[1]["IsKey"], true));
                Check(id + "_composite_not_single_unique", Equals(schema.Rows[0]["IsUnique"], false) && Equals(schema.Rows[3]["IsUnique"], false) && Equals(schema.Rows[4]["IsUnique"], false));
                Check(id + "_nullable_unique", Equals(schema.Rows[2]["IsUnique"], true) && Equals(schema.Rows[2]["AllowDBNull"], true));
            }
            long beforeRead = SendCount();
            bool read = asynchronous ? await reader.ReadAsync() : reader.Read();
            Check(id + "_read", read != behavior.HasFlag(CommandBehavior.SchemaOnly));
            if (read) Check(id + "_actual_values", reader.GetDecimal(5) == 12.3456m && reader.GetDateTime(6) == new DateTime(2026, 10, 15, 1, 2, 3).AddTicks(1234560));
            if (behavior.HasFlag(CommandBehavior.SchemaOnly))
            {
                Check(id + "_read_repeat", !(asynchronous ? await reader.ReadAsync() : reader.Read()));
                Check(id + "_next_result", !(asynchronous ? await reader.NextResultAsync() : reader.NextResult()));
                Check(id + "_no_fetch_wire", SendCount() == beforeRead);
            }
            readers.Add(new { id, read, schema = SchemaRows(schema) });
            if (asynchronous) await reader.CloseAsync(); else reader.Close();
            Check(id + "_reuse", Scalar(connection, "SELECT 1 FROM DUAL") == "1");
        }
        catch (Exception error) { failures.Add(id); readers.Add(new { id, type = error.GetType().Name, location = error.TargetSite?.DeclaringType?.FullName + "." + error.TargetSite?.Name, stack = firstChance.TryGetValue(error, out var originalStack) ? originalStack : error.StackTrace, diagnostics = DmDiagnostics.FormatException(error) }); }
    }
    foreach (bool asynchronous in new[] { false, true })
    foreach (string shape in new[] { "side_effect", "alias", "expression", "join", "missing" })
    {
        string id = (asynchronous ? "async_" : "sync_") + shape;
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = shape switch
            {
                "side_effect" => $"SELECT {Q(function)}(1) FROM DUAL",
                "alias" => $"SELECT \"Pk A\" AS \"Alias\" FROM {Q(table)}",
                "expression" => $"SELECT \"Pk A\"+1 AS \"Expression\" FROM {Q(table)}",
                "join" => $"SELECT A.\"Pk A\" FROM {Q(table)} A JOIN {Q(table)} B ON A.\"Pk A\"=B.\"Pk A\"",
                _ => $"SELECT * FROM {Q("I10_MISSING_" + nonce)}"
            };
            string before = Count();
            try
            {
                await using DbDataReader reader = asynchronous ? await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo) : command.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo);
                DataTable schema = reader.GetSchemaTable()!;
                Check(id + "_empty", !(asynchronous ? await reader.ReadAsync() : reader.Read()));
                if (shape == "alias") Check(id + "_alias", Equals(schema.Rows[0]["IsAliased"], true));
                if (shape == "expression") Check(id + "_expression", !Equals(schema.Rows[0]["IsExpression"], false) && schema.Rows[0]["IsKey"] == DBNull.Value);
                if (shape == "join") Check(id + "_key_unknown", schema.Rows[0]["IsKey"] == DBNull.Value);
                if (shape == "missing") failures.Add(id + "_not_rejected");
                readers.Add(new { id, schema = SchemaRows(schema) });
                if (asynchronous) await reader.CloseAsync(); else reader.Close();
            }
            catch (DmException e) when (shape == "missing")
            {
                Check(id + "_server_error", e.Number == -2106);
                readers.Add(new { id, expected_error = e.Number, state = connection.State.ToString() });
            }
            Check(id + "_no_side_effect", Count() == before);
            if (connection.State == ConnectionState.Open) Check(id + "_reuse", Scalar(connection, "SELECT 1 FROM DUAL") == "1");
        }
        catch (Exception error) { failures.Add(id); readers.Add(new { id, type = error.GetType().Name, location = error.TargetSite?.DeclaringType?.FullName + "." + error.TargetSite?.Name, stack = firstChance.TryGetValue(error, out var originalStack) ? originalStack : error.StackTrace, diagnostics = DmDiagnostics.FormatException(error) }); }
    }
    result["readers"] = readers;
}

object SchemaRows(DataTable table) => table.Rows.Cast<DataRow>().Select(r => table.Columns.Cast<DataColumn>()
    .ToDictionary(c => c.ColumnName, c => r[c] == DBNull.Value ? null : r[c] is Type t ? (object)t.FullName! : r[c])).ToArray();
long SendCount() => (long)typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!
    .GetProperty("SendCount", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
string Canonical(DataTable table)
{
    string columns = string.Join("|", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName + ":" + c.DataType.FullName));
    return columns + "\n" + string.Join("\n", table.Rows.Cast<DataRow>().Select(r => JsonSerializer.Serialize(r.ItemArray.Select(v => v == DBNull.Value ? null : v is Type t ? (object)t.FullName! : v))).OrderBy(x => x, StringComparer.Ordinal));
}

void Check(string id, bool accepted) { if (!accepted) failures.Add(id); }
string Q(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
string L(string value) => "'" + value.Replace("'", "''") + "'";
void Create(string kind, string name, string sql) { Execute(sql); created.Add((kind, name)); }
void Execute(string sql) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
string Count() { using var c = Open(); return Scalar(c, $"SELECT N FROM {Q(counter)}"); }
string Scalar(DbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)!; }
DmConnection Open()
{
    var connection = new DmConnection(settings);
    try
    {
        connection.Open();
        if (Scalar(connection, "SELECT USER FROM DUAL") != TestUser || Scalar(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL") != TestUser)
            throw new InvalidOperationException("server_identity_or_schema_mismatch");
        return connection;
    }
    catch { connection.Dispose(); throw; }
}

static class Reflect
{
    internal const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static object? GetProperty(object value, string name) => value.GetType().GetProperty(name, Flags)!.GetValue(value);
    internal static object? Call(object value, string name) => value.GetType().GetMethod(name, Flags, Type.EmptyTypes)!.Invoke(value, null);
    internal static object? Field(object value, string name)
    {
        for (Type? type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Flags | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(value);
        throw new MissingFieldException(name);
    }
}

// Observe only the already-encoded request opcode and execute bit. No SQL,
// connection string, authentication payload or frame bytes leave this callback.
sealed class PrepareTrace : IDisposable
{
    private readonly DmCommand command;
    private readonly FieldInfo hook;
    internal readonly List<Frame> Frames = new();
    internal sealed record Frame(short Opcode, bool Execute);
    internal PrepareTrace(DmCommand command)
    {
        this.command = command;
        var hooks = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!;
        hook = hooks.GetField("AfterSendBeforeReceive", BindingFlags.Static | BindingFlags.NonPublic)!;
        var parameters = hook.FieldType.GetMethod("Invoke")!.GetParameters().Select(x => Expression.Parameter(x.ParameterType)).ToArray();
        var body = Expression.Call(Expression.Constant(this), typeof(PrepareTrace).GetMethod(nameof(Observe), BindingFlags.Instance | BindingFlags.NonPublic)!);
        hook.SetValue(null, Expression.Lambda(hook.FieldType, body, parameters).Compile());
    }
    private void Observe()
    {
        if (Reflect.GetProperty(command, "Statement") is not { } statement) return;
        var buffer = Reflect.Field(statement, "__t02_field_04000925")!;
        byte[] bytes = (byte[])Reflect.Call(buffer, "A")!;
        short opcode = (short)(bytes[4] | bytes[5] << 8);
        if (opcode == 5) Frames.Add(new Frame(opcode, bytes[21] != 0));
    }
    public void Dispose() => hook.SetValue(null, null);
}
