using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using W.Dm;

// Real-server characterization for array binding (#7) and DbBatch (#8).
// Output holds only booleans, numbers, enum names, error numbers and generated
// object names; never the connection string or server message text.
const string TestUser = "WDM_PROVIDER_TEST";
string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("test connection missing");
string settings = new DmConnectionStringBuilder(raw) { ConnPooling = false, TransportSecurity = DmTransportSecurity.PlaintextAllowed }.ConnectionString;
string nonce = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
var created = new List<string>();
var output = new Dictionary<string, object?> { ["nonce"] = nonce };
var failures = new List<string>();
string[] only = args;
string T(string tag) { var name = $"BP_{tag}_{nonce}"; created.Add(name); return name; }
void Check(string id, bool ok) { if (!ok) failures.Add(id); }
bool Run(string section) => only.Length == 0 || only.Contains(section);

try
{
    using (var check = Open())
    {
        if (Scalar(check, "SELECT USER FROM DUAL") != TestUser) { Console.Error.WriteLine("wrong account"); return 2; }
        output["identity"] = TestUser;
        output["server_version"] = check.ServerVersion;
        output["server_loopback"] = System.Net.IPAddress.TryParse(new DmConnectionStringBuilder(raw).Server, out var ip) && System.Net.IPAddress.IsLoopback(ip);
    }
    foreach (bool asynchronous in new[] { false, true })
    {
        string mode = asynchronous ? "async" : "sync";
        if (Run("array")) output[mode + "_array_success"] = await ArraySuccess(asynchronous);
        if (Run("array")) output[mode + "_array_dml"] = await ArrayDml(asynchronous);
        if (Run("array")) output[mode + "_array_failure"] = await ArrayFailure(asynchronous);
        if (Run("array")) output[mode + "_array_rejections"] = await ArrayRejections(asynchronous);
        if (Run("array")) output[mode + "_array_timeout"] = await ArrayTimeout(asynchronous);
        if (Run("batch")) output[mode + "_batch_results"] = await BatchResults(asynchronous);
        if (Run("batch")) output[mode + "_batch_failure"] = await BatchFailure(asynchronous);
        if (Run("batch")) output[mode + "_batch_guards"] = await BatchGuards(asynchronous);
        if (Run("batch")) output[mode + "_batch_timeout"] = await BatchTimeout(asynchronous);
    }
    if (Run("perf")) output["array_performance"] = await ArrayPerformance();
    if (Run("batch")) output["batch_entry_points"] = await BatchEntryPoints();
}
catch (Exception error)
{
    failures.Add("unexpected_" + error.GetType().Name);
    output["unexpected"] = DmDiagnostics.FormatException(error);
    output["unexpected_stack"] = error.StackTrace;
    if (error is not DbException) output["unexpected_message"] = error.Message; // Driver-local text only.
}
finally
{
    output["cleanup"] = Cleanup();
}
output["failures"] = failures;
output["accepted"] = failures.Count == 0;
Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
return failures.Count == 0 ? 0 : 1;

async Task<object> ArraySuccess(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("OK" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, D DECIMAL(18,4), S VARCHAR(40), TS TIMESTAMP(6), B VARBINARY(16), N INT, C CLOB)");
    var ids = new[] { 1, 2, 3, 4, 5 };
    var decimals = new object[] { 1.5m, 1.25m, DBNull.Value, -12345.6789m, 0m };
    var strings = new object?[] { "a", null, "中文", "", new string('x', 40) };
    var stamps = new object[] { DBNull.Value, new DateTime(2026, 1, 2, 3, 4, 5).AddTicks(1234560), new DateTime(1999, 12, 31, 23, 59, 59), new DateTime(2026, 10, 15), DBNull.Value };
    var binaries = new object[] { new byte[] { 1, 2, 3 }, DBNull.Value, Array.Empty<byte>(), new byte[16], new byte[] { 255 } };
    var nulls = new int?[] { null, 7, null, null, -1 };
    var clobs = new object[] { "small", DBNull.Value, "lob-3", new string('c', 200), "" };
    long beforeSends = SendCount();
    int affected;
    using (var connection = Open())
    using (var command = (DmCommand)connection.CreateCommand())
    {
        command.CommandText = $"INSERT INTO \"{t}\" (ID, D, S, TS, B, N, C) VALUES (?, ?, ?, ?, ?, ?, ?)";
        command.ArrayBindCount = ids.Length;
        command.Parameters.Add(new DmParameter("ID", ids));
        command.Parameters.Add(new DmParameter("D", decimals));
        command.Parameters.Add(new DmParameter { ParameterName = "S", DbType = DbType.String, Value = strings });
        command.Parameters.Add(new DmParameter { ParameterName = "TS", DbType = DbType.DateTime2, Value = stamps });
        command.Parameters.Add(new DmParameter { ParameterName = "B", DbType = DbType.Binary, Value = binaries });
        command.Parameters.Add(new DmParameter("N", nulls));
        command.Parameters.Add(new DmParameter { ParameterName = "C", DmSqlType = DmDbType.Clob, Value = clobs });
        long start = SendCount();
        affected = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
        output[mode + "_success_sends"] = SendCount() - start;
    }
    var rows = new List<string>();
    using (var connection = Open())
    using (var command = (DmCommand)connection.CreateCommand())
    {
        command.CommandText = $"SELECT ID, D, S, TS, B, N, C FROM \"{t}\" ORDER BY ID";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var cells = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                cells.Add(reader.IsDBNull(i) ? "<null>" : reader.GetValue(i) switch
                {
                    byte[] bytes => Convert.ToHexString(bytes),
                    DateTime value => value.ToString("O", CultureInfo.InvariantCulture),
                    string text when text.Length > 12 => $"len{text.Length}:{text[0]}",
                    var value => Convert.ToString(value, CultureInfo.InvariantCulture)!
                });
            rows.Add(string.Join("|", cells));
        }
    }
    var expected = new[]
    {
        "1|1.5000|a|<null>|010203|<null>|small",
        "2|1.2500|<null>|2026-01-02T03:04:05.1234560|<null>|7|<null>",
        "3|<null>|中文|1999-12-31T23:59:59.0000000||<null>|lob-3",
        "4|-12345.6789||2026-10-15T00:00:00.0000000|00000000000000000000000000000000|<null>|len200:c",
        "5|0.0000|len40:x|<null>|FF|-1|"
    };
    bool exact = rows.SequenceEqual(expected);
    Check(mode + "_array_success", affected == 5 && exact);
    return new { affected, exact, rows = exact ? null : rows, sends = SendCount() - beforeSends };
}

async Task<object> ArrayDml(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("DML" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, S VARCHAR(20))");
    ExecFresh($"INSERT INTO \"{t}\" SELECT LEVEL, 'v' || LEVEL FROM DUAL CONNECT BY LEVEL <= 6");
    using var connection = Open();
    int updated, deleted, merged, updateMulti;
    using (var command = (DmCommand)connection.CreateCommand())
    {
        // Rows 1 and 3 match, 99 matches nothing.
        command.CommandText = $"UPDATE \"{t}\" SET S = ? WHERE ID = ?";
        command.ArrayBindCount = 3;
        command.Parameters.Add(new DmParameter("S", new[] { "u1", "u99", "u3" }));
        command.Parameters.Add(new DmParameter("ID", new[] { 1, 99, 3 }));
        updated = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
    }
    using (var command = (DmCommand)connection.CreateCommand())
    {
        // One row deletes two rows; total is the server's sum.
        command.CommandText = $"DELETE FROM \"{t}\" WHERE ID BETWEEN ? AND ?";
        command.ArrayBindCount = 2;
        command.Parameters.Add(new DmParameter("LO", new[] { 5, 100 }));
        command.Parameters.Add(new DmParameter("HI", new[] { 6, 200 }));
        deleted = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
    }
    using (var command = (DmCommand)connection.CreateCommand())
    {
        command.CommandText = $"MERGE INTO \"{t}\" T USING (SELECT ? AS ID, ? AS S FROM DUAL) X ON (T.ID = X.ID) " +
            "WHEN MATCHED THEN UPDATE SET T.S = X.S WHEN NOT MATCHED THEN INSERT (ID, S) VALUES (X.ID, X.S)";
        command.ArrayBindCount = 2;
        command.Parameters.Add(new DmParameter("ID", new[] { 2, 50 }));
        command.Parameters.Add(new DmParameter("S", new[] { "m2", "m50" }));
        merged = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
    }
    using (var command = (DmCommand)connection.CreateCommand())
    {
        command.CommandText = $"UPDATE \"{t}\" SET S = S || ? WHERE ID <= ?";
        command.ArrayBindCount = 2;
        command.Parameters.Add(new DmParameter("S", new[] { "x", "y" }));
        command.Parameters.Add(new DmParameter("ID", new[] { 2, 3 }));
        updateMulti = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
    }
    int scalarMerge;
    using (var command = (DmCommand)connection.CreateCommand())
    {
        command.CommandText = $"MERGE INTO \"{t}\" T USING (SELECT ? AS ID FROM DUAL) X ON (T.ID = X.ID) " +
            "WHEN MATCHED THEN UPDATE SET T.S = 'sm' WHEN NOT MATCHED THEN INSERT (ID, S) VALUES (X.ID, 'si')";
        command.Parameters.Add(new DmParameter("ID", 60));
        scalarMerge = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
    }
    string state = string.Join(",", RowsFresh($"SELECT ID || '=' || S FROM \"{t}\" ORDER BY ID"));
    const string expectedState = "1=u1xy,2=m2xy,3=u3y,4=v4,50=m50,60=si";
    Check(mode + "_array_dml", updated == 2 && deleted == 2 && merged == 2 && updateMulti == 5 && scalarMerge == 1 && state == expectedState);
    return new { updated, deleted, merged, updateMulti, scalarMerge, state };
}

async Task<object> ArrayFailure(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("ERR" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, S VARCHAR(5) NOT NULL)");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (3, 'pre')");
    var result = new Dictionary<string, object?>();
    // Autocommit: row index 2 (ID 3) violates the primary key.
    using (var connection = Open())
    {
        var outcome = await Failing(connection, null, $"INSERT INTO \"{t}\" VALUES (?, ?)", new[] { 1, 2, 3, 4 }, new[] { "a", "b", "c", "d" }, asynchronous);
        string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" ORDER BY ID"));
        string after = connection.State == ConnectionState.Open ? Scalar(connection, "SELECT 1 FROM DUAL")! : "-";
        result["autocommit_pk"] = new { outcome, fresh, state = connection.State.ToString(), after };
        Check(mode + "_autocommit_pk_failure", outcome.Number == -6602 && connection.State == ConnectionState.Open && after == "1");
        output[mode + "_autocommit_pk_fresh"] = fresh;
    }
    ExecFresh($"DELETE FROM \"{t}\" WHERE ID <> 3");
    // Value too long at the last row, inside an explicit transaction with earlier work.
    using (var connection = Open())
    {
        using var tx = (DmTransaction)connection.BeginTransaction();
        Exec(connection, $"INSERT INTO \"{t}\" VALUES (10, 'tx')", tx);
        var outcome = await Failing(connection, tx, $"INSERT INTO \"{t}\" VALUES (?, ?)", new[] { 11, 12, 13 }, new[] { "a", "b", "toolong" }, asynchronous);
        string inside = connection.State == ConnectionState.Open ? string.Join(",", Rows(connection, $"SELECT ID FROM \"{t}\" ORDER BY ID", tx)) : "-";
        string txState = tx.Outcome.ToString();
        if (connection.State == ConnectionState.Open) tx.Commit();
        string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" ORDER BY ID"));
        result["transaction_value"] = new { outcome, inside, txState, committed = tx.Outcome.ToString(), fresh, state = connection.State.ToString() };
        output[mode + "_transaction_failure_fresh"] = fresh;
        Check(mode + "_transaction_failure", outcome.Number != null && connection.State == ConnectionState.Open);
    }
    ExecFresh($"DELETE FROM \"{t}\" WHERE ID <> 3");
    // Same failure, transaction rolled back afterwards.
    using (var connection = Open())
    {
        using var tx = (DmTransaction)connection.BeginTransaction();
        var outcome = await Failing(connection, tx, $"INSERT INTO \"{t}\" VALUES (?, ?)", new[] { 20, 3, 21 }, new[] { "a", "b", "c" }, asynchronous);
        if (connection.State == ConnectionState.Open) tx.Rollback();
        string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" ORDER BY ID"));
        result["transaction_rollback"] = new { outcome, rolledBack = tx.Outcome.ToString(), fresh };
        Check(mode + "_transaction_rollback", fresh == "3" && tx.Outcome == DmTransactionOutcome.RolledBack);
    }
    return result;
}

async Task<FailureOutcome> Failing(DmConnection connection, DbTransaction? tx, string sql, int[] ids, string[] values, bool asynchronous)
{
    using var command = (DmCommand)connection.CreateCommand();
    command.Transaction = (DmTransaction?)tx;
    command.CommandText = sql;
    command.ArrayBindCount = ids.Length;
    command.Parameters.Add(new DmParameter("ID", ids));
    command.Parameters.Add(new DmParameter("S", values));
    try
    {
        int affected = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
        return new FailureOutcome(null, null, null, affected, null);
    }
    catch (DmException error)
    {
        return new FailureOutcome(error.Number, error.ErrorKind.ToString(), error.FailureInfo?.ConnectionReusable, null,
            error.Message.Contains("row", StringComparison.OrdinalIgnoreCase) || error.Message.Contains('行'));
    }
}

async Task<object> ArrayRejections(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("REJ" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, D DECIMAL(10,2), S VARCHAR(10), C CLOB, B BLOB)");
    var result = new Dictionary<string, object?>();
    using var connection = Open();
    long sendsBefore = SendCount();
    async Task<string> Reject(string id, string sql, Action<DmCommand> setup, Func<DmCommand, Task>? action = null)
    {
        using var command = (DmCommand)connection.CreateCommand();
        command.CommandText = sql;
        setup(command);
        long start = SendCount();
        string kind;
        try
        {
            if (action != null) await action(command);
            else if (asynchronous) await command.ExecuteNonQueryAsync(); else command.ExecuteNonQuery();
            kind = "accepted";
        }
        catch (Exception error) { kind = error.GetType().Name + (error is DmException dm ? ":" + dm.Number : ""); }
        result[id] = new { kind, sends = SendCount() - start };
        return kind;
    }
    string insert = $"INSERT INTO \"{t}\" (ID, S) VALUES (?, ?)";
    Check(mode + "_reject_length", (await Reject("length_mismatch", insert, c =>
    { c.ArrayBindCount = 3; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("S", new[] { "a", "b", "c" })); })) == "InvalidOperationException");
    Check(mode + "_reject_scalar_value", (await Reject("scalar_value", insert, c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", 1)); c.Parameters.Add(new DmParameter("S", new[] { "a", "b" })); })) == "InvalidOperationException");
    Check(mode + "_reject_negative", (await Reject("negative_count", insert, c =>
    { c.ArrayBindCount = -1; c.Parameters.Add(new DmParameter("ID", 1)); c.Parameters.Add(new DmParameter("S", "a")); })) == "InvalidOperationException");
    Check(mode + "_reject_no_parameters", (await Reject("no_parameters", $"INSERT INTO \"{t}\" (ID) VALUES (1)", c => c.ArrayBindCount = 2)) == "InvalidOperationException");
    Check(mode + "_reject_mixed_clr", (await Reject("mixed_clr_types", insert, c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new object[] { 1, "2" })); c.Parameters.Add(new DmParameter("S", new[] { "a", "b" })); })) == "InvalidOperationException");
    Check(mode + "_reject_all_null_untyped", (await Reject("all_null_untyped", $"INSERT INTO \"{t}\" (ID, S) VALUES (?, ? || '')", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("S", new object[] { DBNull.Value, DBNull.Value })); })) != "accepted");
    Check(mode + "_reject_select", (await Reject("select_statement", "SELECT ? FROM DUAL WHERE 1 = ?", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("A", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("B", new[] { 1, 1 })); })) == "NotSupportedException");
    Check(mode + "_reject_block", (await Reject("anonymous_block", $"BEGIN INSERT INTO \"{t}\" (ID, S) VALUES (?, ?); END;", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("S", new[] { "a", "b" })); })) == "NotSupportedException");
    Check(mode + "_reject_reader", (await Reject("reader", insert, c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("S", new[] { "a", "b" })); },
        async c => { if (asynchronous) await (await c.ExecuteReaderAsync()).DisposeAsync(); else c.ExecuteReader().Dispose(); })) == "NotSupportedException");
    Check(mode + "_reject_scalar", (await Reject("scalar", insert, c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("S", new[] { "a", "b" })); },
        async c => { if (asynchronous) await c.ExecuteScalarAsync(); else c.ExecuteScalar(); })) == "NotSupportedException");
    Check(mode + "_reject_large_clob", (await Reject("out_of_row_clob", $"INSERT INTO \"{t}\" (ID, C) VALUES (?, ?)", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter { ParameterName = "C", DmSqlType = DmDbType.Clob, Value = new[] { "a", new string('z', 200_000) } }); })) == "NotSupportedException");
    Check(mode + "_reject_large_blob", (await Reject("out_of_row_blob", $"INSERT INTO \"{t}\" (ID, B) VALUES (?, ?)", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter { ParameterName = "B", DmSqlType = DmDbType.Blob, Value = new[] { new byte[1], new byte[200_000] } }); })) == "NotSupportedException");
    Check(mode + "_reject_stream", (await Reject("stream_element", $"INSERT INTO \"{t}\" (ID, B) VALUES (?, ?)", c =>
    { c.ArrayBindCount = 1; c.Parameters.Add(new DmParameter("ID", new[] { 1 })); c.Parameters.Add(new DmParameter { ParameterName = "B", DmSqlType = DmDbType.Blob, Value = new object[] { new MemoryStream(new byte[4]) } }); })) == "NotSupportedException");
    // An advisory decimal descriptor cannot carry two scales.
    result["advisory_decimal_scales"] = await Reject("advisory_decimal_scales", $"INSERT INTO \"{t}\" (ID, D) VALUES (?, ? + 0)", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 1, 2 })); c.Parameters.Add(new DmParameter("D", new[] { 1.5m, 1.25m })); });
    // Explicit precision/scale fixes the descriptor for every row.
    string explicitScale = await Reject("advisory_decimal_explicit", $"INSERT INTO \"{t}\" (ID, D) VALUES (?, ? + 0)", c =>
    { c.ArrayBindCount = 2; c.Parameters.Add(new DmParameter("ID", new[] { 101, 102 })); c.Parameters.Add(new DmParameter { ParameterName = "D", DbType = DbType.Decimal, Precision = 10, Scale = 2, Value = new[] { 1.5m, 1.25m } }); });
    string explicitRows = string.Join(",", Rows(connection, $"SELECT ID || '=' || D FROM \"{t}\" WHERE ID > 100 ORDER BY ID"));
    result["advisory_decimal_explicit_rows"] = explicitRows;
    Check(mode + "_advisory_explicit_scale", explicitScale == "accepted" && explicitRows == "101=1.50,102=1.25");
    string state = connection.State.ToString();
    string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" WHERE ID < 100 ORDER BY ID"));
    result["state_after"] = state;
    result["fresh_rows"] = fresh;
    Check(mode + "_rejections_leave_no_rows", fresh == "" && state == "Open");
    return result;
}

async Task<object> ArrayTimeout(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("TMO" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, S VARCHAR(10))");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (1, 'a'), (2, 'b'), (3, 'c')");
    var result = new Dictionary<string, object?>();
    foreach (string trigger in new[] { "timeout", "cancel" })
    {
        using var blocker = Open();
        using var hold = blocker.BeginTransaction();
        Exec(blocker, $"UPDATE \"{t}\" SET S = 'held' WHERE ID = 3", hold);
        using var connection = Open();
        using var command = (DmCommand)connection.CreateCommand();
        command.CommandText = $"UPDATE \"{t}\" SET S = ? WHERE ID = ?";
        command.ArrayBindCount = 3;
        command.Parameters.Add(new DmParameter("S", new[] { trigger[..1] + "1", trigger[..1] + "2", trigger[..1] + "3" }));
        command.Parameters.Add(new DmParameter("ID", new[] { 1, 2, 3 }));
        if (trigger == "timeout") command.CommandTimeout = 2;
        using var cts = new CancellationTokenSource();
        Timer? timer = null;
        if (trigger == "cancel")
            timer = asynchronous ? null : new Timer(_ => command.Cancel(), null, 1500, Timeout.Infinite);
        if (trigger == "cancel" && asynchronous) cts.CancelAfter(1500);
        var watch = Stopwatch.StartNew();
        string kind = "completed";
        string? outcome = null;
        try { if (asynchronous) await command.ExecuteNonQueryAsync(cts.Token); else command.ExecuteNonQuery(); }
        catch (Exception error)
        {
            kind = error.GetType().Name;
            outcome = (error as DmException)?.FailureInfo?.OperationOutcome.ToString()
                ?? (error as DmOperationCanceledException)?.FailureInfo?.OperationOutcome.ToString();
        }
        watch.Stop();
        timer?.Dispose();
        string state = connection.State.ToString();
        string whileHeld = string.Join(",", RowsFresh($"SELECT ID || '=' || S FROM \"{t}\" ORDER BY ID"));
        hold.Rollback();
        // The abandoned server statement may still acquire the lock; wait for it to settle.
        string fresh = whileHeld;
        for (int i = 0; i < 20; i++)
        {
            Thread.Sleep(250);
            fresh = string.Join(",", RowsFresh($"SELECT ID || '=' || S FROM \"{t}\" ORDER BY ID"));
        }
        string p = trigger[..1];
        bool atomic = fresh == "1=a,2=b,3=c" || fresh == $"1={p}1,2={p}2,3={p}3";
        result[trigger] = new { kind, outcome, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), state, whileHeld, fresh, atomic };
        Check(mode + "_array_" + trigger, kind != "completed" && outcome == "Unknown" && state == "Broken" &&
            whileHeld == "1=a,2=b,3=c" && atomic && watch.Elapsed < TimeSpan.FromSeconds(30));
        ExecFresh($"UPDATE \"{t}\" SET S = CASE ID WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END");
    }
    return result;
}

async Task<object> ArrayPerformance()
{
    var result = new Dictionary<string, object?>();
    foreach (int count in new[] { 1000, 5000 })
    {
        string arrayTable = T("PA" + count), rowTable = T("PR" + count);
        ExecFresh($"CREATE TABLE \"{arrayTable}\" (ID INT PRIMARY KEY, D DECIMAL(18,4), S VARCHAR(40), TS TIMESTAMP(6))");
        ExecFresh($"CREATE TABLE \"{rowTable}\" (ID INT PRIMARY KEY, D DECIMAL(18,4), S VARCHAR(40), TS TIMESTAMP(6))");
        var ids = Enumerable.Range(1, count).ToArray();
        var ds = ids.Select(i => i / 7m).Select(d => Math.Round(d, 4)).ToArray();
        var ss = ids.Select(i => "row-" + i).ToArray();
        var ts = ids.Select(i => new DateTime(2026, 1, 1).AddSeconds(i)).ToArray();
        using var connection = Open();
        // Array path, one statement.
        long sends = SendCount();
        long bytes = SentBytes();
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        int affected;
        using (var command = (DmCommand)connection.CreateCommand())
        {
            command.CommandText = $"INSERT INTO \"{arrayTable}\" VALUES (?, ?, ?, ?)";
            command.ArrayBindCount = count;
            command.Parameters.Add(new DmParameter("ID", ids));
            command.Parameters.Add(new DmParameter { ParameterName = "D", DbType = DbType.Decimal, Precision = 18, Scale = 4, Value = ds });
            command.Parameters.Add(new DmParameter("S", ss));
            command.Parameters.Add(new DmParameter { ParameterName = "TS", DbType = DbType.DateTime2, Value = ts });
            affected = command.ExecuteNonQuery();
        }
        watch.Stop();
        var array = new { affected, ms = watch.ElapsedMilliseconds, sends = SendCount() - sends, sent_kb = (SentBytes() - bytes) / 1024, alloc_kb = (GC.GetAllocatedBytesForCurrentThread() - alloc) / 1024 };
        // Per-row path, one prepared command reused. A transport drop on the shared remote
        // instance (also seen on the unmodified baseline) restarts this baseline once on its own table.
        object? interrupted = null;
        object rows = null!;
        int perRow = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var rowConnection = Open();
            if (attempt > 0) Exec(rowConnection, $"DELETE FROM \"{rowTable}\"");
            sends = SendCount(); bytes = SentBytes(); alloc = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            perRow = 0;
            int failedAt = -1;
            using (var command = (DmCommand)rowConnection.CreateCommand())
            {
                command.CommandText = $"INSERT INTO \"{rowTable}\" VALUES (?, ?, ?, ?)";
                var pId = new DmParameter("ID", (object)0); var pD = new DmParameter { ParameterName = "D", DbType = DbType.Decimal, Precision = 18, Scale = 4, Value = 0m };
                var pS = new DmParameter("S", ""); var pT = new DmParameter { ParameterName = "TS", DbType = DbType.DateTime2, Value = DateTime.MinValue };
                command.Parameters.Add(pId); command.Parameters.Add(pD); command.Parameters.Add(pS); command.Parameters.Add(pT);
                for (int i = 0; i < count; i++)
                {
                    pId.Value = ids[i]; pD.Value = ds[i]; pS.Value = ss[i]; pT.Value = ts[i];
                    try { perRow += command.ExecuteNonQuery(); }
                    catch (DmException error) when (error.ErrorKind == DmErrorKind.Transport)
                    {
                        failedAt = i;
                        interrupted = new { attempt, iteration = i, chain = Chain(error) };
                        break;
                    }
                }
            }
            watch.Stop();
            rows = new { affected = perRow, ms = watch.ElapsedMilliseconds, sends = SendCount() - sends, sent_kb = (SentBytes() - bytes) / 1024, alloc_kb = (GC.GetAllocatedBytesForCurrentThread() - alloc) / 1024 };
            if (failedAt < 0) break;
        }
        string diff = Scalar(connection, $"SELECT COUNT(*) FROM (SELECT * FROM \"{arrayTable}\" MINUS SELECT * FROM \"{rowTable}\")")!;
        string arrayCount = Scalar(connection, $"SELECT COUNT(*) FROM \"{arrayTable}\"")!;
        result[count.ToString(CultureInfo.InvariantCulture)] = new { array, per_row = rows, per_row_interrupted = interrupted, identical_rows = diff == "0" && arrayCount == count.ToString(CultureInfo.InvariantCulture) };
        Check("perf_" + count, affected == count && perRow == count && diff == "0" && arrayCount == count.ToString(CultureInfo.InvariantCulture));
    }
    return result;
}

DmBatchCommand Bc(string sql, params (string Name, object Value)[] parameters)
{
    var command = new DmBatchCommand(sql);
    foreach (var (name, value) in parameters) command.Parameters.Add(new DmParameter { ParameterName = name, Value = value });
    return command;
}

async Task<List<string>> ReadAll(DbDataReader reader, bool asynchronous)
{
    var sets = new List<string>();
    do
    {
        var rows = new List<string>();
        while (asynchronous ? await reader.ReadAsync() : reader.Read())
        {
            var cells = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++) cells.Add(Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)!);
            rows.Add(string.Join("|", cells));
        }
        sets.Add(string.Join(";", rows));
    } while (asynchronous ? await reader.NextResultAsync() : reader.NextResult());
    return sets;
}

async Task<object> BatchResults(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("BR" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, S VARCHAR(20))");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (1, 'a'), (2, 'b'), (3, 'c')");
    var result = new Dictionary<string, object?>();
    using (var connection = Open())
    {
        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (?, ?)", ("ID", 10), ("S", "x")));
        batch.BatchCommands.Add(Bc($"UPDATE \"{t}\" SET S = S || ? WHERE ID < ?", ("S", "!"), ("ID", 3)));
        batch.BatchCommands.Add(Bc($"SELECT ID, S FROM \"{t}\" WHERE ID <= ? ORDER BY ID", ("ID", 2)));
        batch.BatchCommands.Add(Bc($"DELETE FROM \"{t}\" WHERE ID = ?", ("ID", 3)));
        batch.BatchCommands.Add(Bc($"SELECT COUNT(*) FROM \"{t}\""));
        batch.BatchCommands.Add(Bc($"SELECT S FROM \"{t}\" WHERE ID = -1"));
        long start = SendCount();
        List<string> sets;
        int total;
        await using (DbDataReader reader = asynchronous ? await batch.ExecuteReaderAsync() : batch.ExecuteReader())
        {
            sets = await ReadAll(reader, asynchronous);
            total = reader.RecordsAffected;
        }
        var counts = batch.BatchCommands.Select(c => c.RecordsAffected).ToArray();
        string fresh = string.Join(",", RowsFresh($"SELECT ID || '=' || S FROM \"{t}\" ORDER BY ID"));
        result["reader"] = new { sets, total, counts, fresh, sends = SendCount() - start };
        Check(mode + "_batch_reader", string.Join("/", sets) == "1|a!;2|b!/3/" && total == 4 &&
            string.Join(",", counts) == "1,2,-1,1,-1,-1" && fresh == "1=a!,2=b!,10=x");
        // ExecuteNonQuery over the same batch reruns it with fresh counts.
        batch.BatchCommands.RemoveAt(5); batch.BatchCommands.RemoveAt(4); batch.BatchCommands.RemoveAt(2);
        batch.BatchCommands[0].Parameters["ID"].Value = 11;
        start = SendCount();
        int affected = asynchronous ? await batch.ExecuteNonQueryAsync() : batch.ExecuteNonQuery();
        long batchSends = SendCount() - start;
        counts = batch.BatchCommands.Select(c => c.RecordsAffected).ToArray();
        result["non_query"] = new { affected, counts, sends = batchSends };
        Check(mode + "_batch_non_query", affected == 3 && string.Join(",", counts) == "1,2,0");
        // The same three commands one by one, for a round-trip comparison.
        start = SendCount();
        foreach (var (sql, values) in new[] {
            ($"INSERT INTO \"{t}\" VALUES (?, ?)", new object[] { 12, "y" }),
            ($"UPDATE \"{t}\" SET S = S || ? WHERE ID < ?", new object[] { "!", 3 }),
            ($"DELETE FROM \"{t}\" WHERE ID = ?", new object[] { 3 }) })
        {
            using var command = (DmCommand)connection.CreateCommand();
            command.CommandText = sql;
            for (int i = 0; i < values.Length; i++) command.Parameters.Add(new DmParameter { ParameterName = "P" + i, Value = values[i] });
            if (asynchronous) await command.ExecuteNonQueryAsync(); else command.ExecuteNonQuery();
        }
        result["individual_sends"] = SendCount() - start;
        object? scalar;
        using (var scalarBatch = connection.CreateBatch())
        {
            scalarBatch.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (20, 's')"));
            scalarBatch.BatchCommands.Add(Bc($"SELECT S FROM \"{t}\" WHERE ID = 20"));
            scalarBatch.BatchCommands.Add(Bc($"UPDATE \"{t}\" SET S = 'after' WHERE ID = 20"));
            scalar = asynchronous ? await scalarBatch.ExecuteScalarAsync() : scalarBatch.ExecuteScalar();
            result["scalar"] = new { value = scalar, counts = scalarBatch.BatchCommands.Select(c => c.RecordsAffected).ToArray() };
        }
        string drained = ScalarFresh($"SELECT S FROM \"{t}\" WHERE ID = 20")!;
        result["scalar_drained"] = drained;
        Check(mode + "_batch_scalar", (string?)scalar == "s" && drained == "after");
        // Closing a reader early still executes the remaining commands.
        using (var early = connection.CreateBatch())
        {
            early.BatchCommands.Add(Bc($"SELECT ID FROM \"{t}\" ORDER BY ID"));
            early.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (30, 'late')"));
            using (var reader = early.ExecuteReader()) reader.Read();
            result["early_close"] = new { counts = early.BatchCommands.Select(c => c.RecordsAffected).ToArray(), inserted = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID = 30") };
            Check(mode + "_batch_early_close", early.BatchCommands[1].RecordsAffected == 1 && ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID = 30") == "1");
        }
        // Repeated batches on one connection keep the session healthy.
        int repeats = 0;
        for (int i = 0; i < 50; i++)
        {
            using var again = connection.CreateBatch();
            again.BatchCommands.Add(Bc($"SELECT COUNT(*) FROM \"{t}\""));
            again.BatchCommands.Add(Bc($"UPDATE \"{t}\" SET S = S WHERE ID = 30"));
            using var reader = asynchronous ? await again.ExecuteReaderAsync() : again.ExecuteReader();
            if (reader.Read()) repeats++;
        }
        result["repeats"] = repeats;
        Check(mode + "_batch_repeats", repeats == 50 && Scalar(connection, "SELECT 1 FROM DUAL") == "1");
    }
    return result;
}

async Task<object> BatchFailure(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("BF" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, S VARCHAR(20))");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (5, 'pre')");
    var result = new Dictionary<string, object?>();
    async Task<object> Run(DmConnection connection, DmTransaction? tx, bool reader)
    {
        using var batch = connection.CreateBatch();
        batch.Transaction = tx;
        batch.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (1, 'one')"));
        batch.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (5, 'dup')"));
        batch.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (3, 'three')"));
        if (reader) batch.BatchCommands.Insert(0, Bc($"SELECT ID FROM \"{t}\""));
        int? number = null; int failedIndex = -2; bool? reusable = null;
        try
        {
            if (reader)
            {
                using var r = asynchronous ? await batch.ExecuteReaderAsync() : batch.ExecuteReader();
                while (r.Read()) { }
                if (asynchronous) await r.NextResultAsync(); else r.NextResult();
            }
            else if (asynchronous) await batch.ExecuteNonQueryAsync(); else batch.ExecuteNonQuery();
        }
        catch (DmException error)
        {
            number = error.Number;
            reusable = error.FailureInfo?.ConnectionReusable;
            failedIndex = error.BatchCommand is DmBatchCommand failed ? batch.BatchCommands.IndexOf(failed) : -1;
            if (reusable != true) output[$"{mode}_batch_failure_unreusable_{(reader ? "reader" : "nonquery")}"] = DmDiagnostics.FormatException(error);
        }
        return new { number, failedIndex, reusable, counts = batch.BatchCommands.Select(c => c.RecordsAffected).ToArray(), state = connection.State.ToString() };
    }
    using (var connection = Open())
    {
        var outcome = await Run(connection, null, reader: false);
        string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" ORDER BY ID"));
        result["autocommit"] = new { outcome, fresh };
        Check(mode + "_batch_autocommit_stop", fresh == "1,5" && Json(outcome).Contains("\"failedIndex\":1") &&
            Json(outcome).Contains("\"counts\":[1,-1,-1]") && connection.State == ConnectionState.Open && Scalar(connection, "SELECT 1 FROM DUAL") == "1");
    }
    ExecFresh($"DELETE FROM \"{t}\" WHERE ID <> 5");
    using (var connection = Open())
    {
        using var tx = (DmTransaction)connection.BeginTransaction();
        var outcome = await Run(connection, tx, reader: false);
        string inside = string.Join(",", Rows(connection, $"SELECT ID FROM \"{t}\" ORDER BY ID", tx));
        string txState = tx.Outcome.ToString();
        tx.Rollback();
        string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" ORDER BY ID"));
        result["transaction"] = new { outcome, inside, txState, fresh };
        Check(mode + "_batch_transaction_stop", inside == "1,5" && txState == "Active" && fresh == "5" && Json(outcome).Contains("\"failedIndex\":1"));
    }
    using (var connection = Open())
    {
        var outcome = await Run(connection, null, reader: true);
        string fresh = string.Join(",", RowsFresh($"SELECT ID FROM \"{t}\" ORDER BY ID"));
        result["reader"] = new { outcome, fresh };
        Check(mode + "_batch_reader_stop", fresh == "1,5" && Json(outcome).Contains("\"failedIndex\":2") &&
            Json(outcome).Contains("\"counts\":[-1,1,-1,-1]") && connection.State == ConnectionState.Open);
    }
    ExecFresh($"DELETE FROM \"{t}\" WHERE ID <> 5");
    return result;
}

async Task<object> BatchGuards(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("BG" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)");
    var result = new Dictionary<string, object?>();
    using var connection = Open();
    async Task<string> Attempt(Func<Task> action)
    {
        try { await action(); return "accepted"; }
        catch (Exception error) { return error.GetType().Name; }
    }
    async Task<(string Kind, long Sends)> Validate(params DmBatchCommand[] commands)
    {
        using var batch = connection.CreateBatch();
        foreach (var command in commands) batch.BatchCommands.Add(command);
        long start = SendCount();
        string kind = await Attempt(async () => { if (asynchronous) await batch.ExecuteNonQueryAsync(); else batch.ExecuteNonQuery(); });
        return (kind, SendCount() - start);
    }
    var empty = await Validate();
    var procedure = await Validate(Bc($"INSERT INTO \"{t}\" VALUES (1)"), new DmBatchCommand("P") { CommandType = CommandType.StoredProcedure });
    var markers = await Validate(Bc($"INSERT INTO \"{t}\" VALUES (2)"), Bc($"INSERT INTO \"{t}\" VALUES (? + ?)", ("A", 1)));
    var output = await Validate(Bc($"INSERT INTO \"{t}\" VALUES (3)"), new Func<DmBatchCommand>(() =>
    {
        var command = Bc($"INSERT INTO \"{t}\" VALUES (?)", ("ID", 4));
        command.Parameters[0].Direction = ParameterDirection.Output;
        return command;
    })());
    string control;
    using (var tx = (DmTransaction)connection.BeginTransaction())
    {
        using var batch = connection.CreateBatch();
        batch.Transaction = tx;
        batch.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (5)"));
        batch.BatchCommands.Add(Bc("COMMIT"));
        control = await Attempt(async () => { if (asynchronous) await batch.ExecuteNonQueryAsync(); else batch.ExecuteNonQuery(); });
        tx.Rollback();
    }
    string fresh = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\"")!;
    result["prevalidation"] = new { empty = empty.ToString(), procedure = procedure.ToString(), markers = markers.ToString(), output = output.ToString(), control, fresh };
    Check(mode + "_batch_prevalidation", empty == ("InvalidOperationException", 0) && procedure == ("NotSupportedException", 0) &&
        markers == ("InvalidOperationException", 0) && output == ("NotSupportedException", 0) && control == "NotSupportedException" && fresh == "0");
    // Mutation and connection ownership while a batch reader is open.
    using (var batch = connection.CreateBatch())
    {
        var first = Bc("SELECT 1 FROM DUAL");
        var second = Bc($"INSERT INTO \"{t}\" VALUES (?)", ("ID", 6));
        batch.BatchCommands.Add(first);
        batch.BatchCommands.Add(second);
        var guards = new Dictionary<string, string>();
        using (var reader = asynchronous ? await batch.ExecuteReaderAsync() : batch.ExecuteReader())
        {
            guards["timeout"] = await Attempt(() => { batch.Timeout = 5; return Task.CompletedTask; });
            guards["add"] = await Attempt(() => { batch.BatchCommands.Add(Bc("SELECT 2 FROM DUAL")); return Task.CompletedTask; });
            guards["text"] = await Attempt(() => { second.CommandText = "SELECT 3 FROM DUAL"; return Task.CompletedTask; });
            guards["parameter"] = await Attempt(() => { second.Parameters[0].Value = 7; return Task.CompletedTask; });
            guards["rerun"] = await Attempt(() => { batch.ExecuteNonQuery(); return Task.CompletedTask; });
            guards["other_command"] = await Attempt(() => { Scalar(connection, "SELECT 1 FROM DUAL"); return Task.CompletedTask; });
            reader.Read();
        }
        guards["after_close_text"] = await Attempt(() => { second.CommandText = $"INSERT INTO \"{t}\" VALUES (?)"; return Task.CompletedTask; });
        guards["after_close_connection"] = Scalar(connection, "SELECT 1 FROM DUAL")!;
        string inserted = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID = 6")!;
        result["guards"] = guards;
        result["guards_inserted"] = inserted;
        // Cancel while the reader is open: closing stops, later steps never run.
        using (var cancelConnection = Open())
        using (var canceled = cancelConnection.CreateBatch())
        {
            canceled.BatchCommands.Add(Bc("SELECT 1 FROM DUAL"));
            canceled.BatchCommands.Add(Bc($"INSERT INTO \"{t}\" VALUES (8)"));
            var reader = asynchronous ? await canceled.ExecuteReaderAsync() : canceled.ExecuteReader();
            reader.Read();
            canceled.Cancel();
            guards["cancel_then_close"] = await Attempt(async () => { if (asynchronous) await reader.DisposeAsync(); else reader.Dispose(); });
            guards["cancel_state"] = cancelConnection.State.ToString();
            guards["cancel_counts"] = string.Join(",", canceled.BatchCommands.Select(c => c.RecordsAffected));
        }
        guards["cancel_inserted"] = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID = 8")!;
        Check(mode + "_batch_cancel_close", guards["cancel_then_close"] == "accepted" && guards["cancel_state"] == "Broken" &&
            guards["cancel_counts"] == "-1,-1" && guards["cancel_inserted"] == "0");
        Check(mode + "_batch_guards", guards["timeout"] == "InvalidOperationException" && guards["add"] == "InvalidOperationException" &&
            guards["text"] == "InvalidOperationException" && guards["parameter"] == "InvalidOperationException" &&
            guards["rerun"] == "InvalidOperationException" && guards["other_command"] == "InvalidOperationException" &&
            guards["after_close_text"] == "accepted" && guards["after_close_connection"] == "1" && inserted == "1");
    }
    return result;
}

async Task<object> BatchTimeout(bool asynchronous)
{
    string mode = asynchronous ? "async" : "sync";
    string t = T("BT" + (asynchronous ? "A" : "S"));
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, S VARCHAR(10))");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (1, 'a'), (2, 'b'), (3, 'c')");
    var result = new Dictionary<string, object?>();
    foreach (string trigger in new[] { "timeout", "cancel" })
    {
        using var blocker = Open();
        using var hold = blocker.BeginTransaction();
        Exec(blocker, $"UPDATE \"{t}\" SET S = 'held' WHERE ID = 3", hold);
        using var connection = Open();
        using var batch = connection.CreateBatch();
        batch.BatchCommands.Add(Bc($"UPDATE \"{t}\" SET S = ? WHERE ID = 1", ("S", trigger[..1] + "1")));
        batch.BatchCommands.Add(Bc($"UPDATE \"{t}\" SET S = ? WHERE ID = 3", ("S", trigger[..1] + "3")));
        batch.BatchCommands.Add(Bc($"UPDATE \"{t}\" SET S = ? WHERE ID = 2", ("S", trigger[..1] + "2")));
        batch.Timeout = trigger == "timeout" ? 2 : 0;
        using var cts = new CancellationTokenSource();
        Timer? timer = trigger == "cancel" && !asynchronous ? new Timer(_ => batch.Cancel(), null, 1500, Timeout.Infinite) : null;
        if (trigger == "cancel" && asynchronous) cts.CancelAfter(1500);
        var watch = Stopwatch.StartNew();
        string kind = "completed"; string? outcome = null; int failedIndex = -2;
        try { if (asynchronous) await batch.ExecuteNonQueryAsync(cts.Token); else batch.ExecuteNonQuery(); }
        catch (Exception error)
        {
            kind = error.GetType().Name;
            outcome = (error as DmException)?.FailureInfo?.OperationOutcome.ToString() ?? (error as DmOperationCanceledException)?.FailureInfo?.OperationOutcome.ToString();
            failedIndex = (error as DbException)?.BatchCommand is DmBatchCommand failed ? batch.BatchCommands.IndexOf(failed) : -1;
        }
        watch.Stop();
        timer?.Dispose();
        string state = connection.State.ToString();
        var counts = batch.BatchCommands.Select(c => c.RecordsAffected).ToArray();
        string whileHeld = string.Join(",", RowsFresh($"SELECT ID || '=' || S FROM \"{t}\" ORDER BY ID"));
        hold.Rollback();
        Thread.Sleep(3000);
        string fresh = string.Join(",", RowsFresh($"SELECT ID || '=' || S FROM \"{t}\" ORDER BY ID"));
        string p = trigger[..1];
        result[trigger] = new { kind, outcome, failedIndex, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), state, counts, whileHeld, fresh };
        // Step 0 committed; step 1 is unconfirmed (atomic); step 2 never ran.
        Check(mode + "_batch_" + trigger, kind != "completed" && outcome == "Unknown" && state == "Broken" &&
            string.Join(",", counts) == "1,-1,-1" && whileHeld == $"1={p}1,2=b,3=c" &&
            (fresh == $"1={p}1,2=b,3=c" || fresh == $"1={p}1,2=b,3={p}3") && watch.Elapsed < TimeSpan.FromSeconds(30) &&
            (asynchronous && trigger == "cancel" ? failedIndex == -1 : failedIndex is 1 or -1));
        ExecFresh($"UPDATE \"{t}\" SET S = CASE ID WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'c' END");
    }
    return result;
}

async Task<object> BatchEntryPoints()
{
    string t = T("BE");
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)");
    var factory = DmClientFactory.Instance;
    DbBatch fromFactory = factory.CreateBatch();
    DbBatchCommand factoryCommand = factory.CreateBatchCommand();
    var parameter = factoryCommand.CreateParameter();
    bool types = factory.CanCreateBatch && fromFactory is DmBatch && factoryCommand is DmBatchCommand && parameter is DmParameter && factoryCommand.CanCreateParameter;
    // Provider-agnostic use through base types only.
    using (DbConnection connection = factory.CreateConnection()!)
    {
        connection.ConnectionString = settings;
        await connection.OpenAsync();
        types &= connection.CanCreateBatch;
        using DbBatch batch = connection.CreateBatch();
        DbBatchCommand insert = batch.CreateBatchCommand();
        insert.CommandText = $"INSERT INTO \"{t}\" VALUES (?)";
        DbParameter id = insert.CreateParameter();
        id.ParameterName = "ID"; id.Value = 1;
        insert.Parameters.Add(id);
        batch.BatchCommands.Add(insert);
        DbBatchCommand select = batch.CreateBatchCommand();
        select.CommandText = $"SELECT COUNT(*) FROM \"{t}\"";
        batch.BatchCommands.Add(select);
        object? count = await batch.ExecuteScalarAsync();
        types &= Convert.ToInt32(count) == 1 && insert.RecordsAffected == 1;
    }
    // DbDataSource batches use the base wrapper over a DmConnection.
    string viaDataSource;
    await using (var dataSource = DmDataSource.Create(settings))
    {
        using DbBatch batch = dataSource.CreateBatch();
        var insert = batch.CreateBatchCommand(); insert.CommandText = $"INSERT INTO \"{t}\" VALUES (2)";
        batch.BatchCommands.Add(insert);
        var select = batch.CreateBatchCommand(); select.CommandText = $"SELECT COUNT(*) FROM \"{t}\"";
        batch.BatchCommands.Add(select);
        viaDataSource = Convert.ToString(await batch.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
    }
    // Disposing a data source batch before its reader still returns the connection cleanly.
    var disposeOrder = new List<object>();
    string pooledSettings = new DmConnectionStringBuilder(settings)
        { ConnPooling = true, MaxPoolSize = 1, ConnPoolTimeout = 2000 }.ConnectionString;
    await using (var dataSource = DmDataSource.Create(pooledSettings))
    {
        foreach (bool asynchronous in new[] { false, true })
        foreach (bool disposeBatchAsync in new[] { false, true })
        {
            using DbBatch batch = dataSource.CreateBatch();
            var select = batch.CreateBatchCommand(); select.CommandText = "SELECT 1 FROM DUAL";
            batch.BatchCommands.Add(select);
            using DbDataReader reader = asynchronous ? await batch.ExecuteReaderAsync() : batch.ExecuteReader();
            bool valueRead = (asynchronous ? await reader.ReadAsync() : reader.Read()) && reader.GetInt32(0) == 1;
            if (disposeBatchAsync) await batch.DisposeAsync(); else batch.Dispose();
            string status = "ok";
            try
            {
                if (asynchronous) await reader.DisposeAsync(); else reader.Dispose();
                if (asynchronous) await reader.DisposeAsync(); else reader.Dispose();
            }
            catch (Exception error) { status = error.GetType().Name; }
            // A leaked reader lease exhausts this one-slot pool and fails the next checkout.
            using var next = dataSource.CreateCommand("SELECT 1 FROM DUAL");
            bool reopened = Convert.ToInt32(asynchronous ? await next.ExecuteScalarAsync() : next.ExecuteScalar()) == 1;
            types &= valueRead && status == "ok" && reader.IsClosed && reopened;
            disposeOrder.Add(new { asynchronous, disposeBatchAsync, valueRead, status, reopened });
        }
    }
    Check("batch_entry_points", types && viaDataSource == "2");
    return new { types, viaDataSource, disposeOrder };
}

static string Json(object value) => JsonSerializer.Serialize(value);

static string Chain(Exception? error)
{
    var parts = new List<string>();
    for (; error != null; error = error.InnerException)
        parts.Add(error.GetType().Name + (error is System.Net.Sockets.SocketException socket ? ":" + socket.SocketErrorCode : "")
            + (error is IOException or EndOfStreamException or System.Net.Sockets.SocketException or TimeoutException ? "(" + error.Message + ")" : ""));
    return string.Join(" <- ", parts);
}

object Cleanup()
{
    var dropped = new List<string>();
    var cleanupFailures = new List<object>();
    using (var connection = Open())
    {
        foreach (string name in created)
        {
            if (Scalar(connection, $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME='{name}'") == "0") continue;
            try { Exec(connection, $"DROP TABLE \"{name}\""); dropped.Add(name); }
            catch (DbException e) { cleanupFailures.Add(new { name, number = (e as DmException)?.Number }); }
        }
    }
    using var verify = Open();
    string remaining = Scalar(verify, $"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME LIKE 'BP\\_%\\_{nonce}' ESCAPE '\\'")!;
    if (cleanupFailures.Count != 0 || remaining != "0") failures.Add("cleanup");
    return new { dropped = dropped.Count, failures = cleanupFailures, fresh_remaining = remaining };
}

long SendCount() => ReadWire("SendCount");
long SentBytes() => ReadWire("SentBytes");
static long ReadWire(string name) => (long)(typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")?
    .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? throw new InvalidOperationException("wire_counter_missing"));
DmConnection Open() { var connection = new DmConnection(settings); connection.Open(); return connection; }
void ExecFresh(string sql) { using var connection = Open(); Exec(connection, sql); }
string? ScalarFresh(string sql) { using var connection = Open(); return Scalar(connection, sql); }
void Exec(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = (DmCommand)connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.ExecuteNonQuery();
}
string? Scalar(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = (DmCommand)connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}
List<string> RowsFresh(string sql) { using var connection = Open(); return Rows(connection, sql); }
List<string> Rows(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = (DmCommand)connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    using var reader = command.ExecuteReader();
    var rows = new List<string>();
    while (reader.Read()) rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
    return rows;
}

record FailureOutcome(int? Number, string? Kind, bool? Reusable, int? Affected, bool? MessageMentionsRow);
