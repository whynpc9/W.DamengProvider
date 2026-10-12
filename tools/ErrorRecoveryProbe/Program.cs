using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
#if OFFICIAL
using Dm;
#else
using W.Dm;
using W.Dm.Config;
#endif

// Characterizes whether a statement-level server error leaves the physical session
// usable and synchronized: same server session, no partial statement effect, no
// open transaction or lock, and unchanged state of an enclosing local transaction.
// Output contains only numbers/booleans and generated object names.
const string TestUser = "WDM_PROVIDER_TEST";
string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("test connection missing");
#if OFFICIAL
string settings = raw;
const string implementation = "O";
#else
var builder = new DmConnectionStringBuilder(raw) { ConnPooling = false, TransportSecurity = DmTransportSecurity.PlaintextAllowed };
string settings = builder.ConnectionString;
const string implementation = "W";
#endif
string nonce = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
var created = new List<string>();
var output = new Dictionary<string, object?> { ["implementation"] = implementation, ["nonce"] = nonce };
var cases = new List<object>();

using (var check = Open())
{
    if (Scalar(check, "SELECT USER FROM DUAL") != TestUser) { Console.Error.WriteLine("wrong account"); return 2; }
    output["server_version"] = check.ServerVersion;
}
string? sidSql = null;
foreach (var candidate in new[] { "SELECT SESSID() FROM DUAL", "SELECT SYS_CONTEXT('USERENV','SID') FROM DUAL" })
{
    using var probe = Open();
    try { Scalar(probe, candidate); sidSql = candidate; break; } catch (DbException) { }
}
output["session_id_available"] = sidSql != null;

string T(string tag) { var name = $"ERP_{tag}_{nonce}"; created.Add(name); return name; }
string mark = T("MARK");
int markId = 0;

var definitions = new List<Case>
{
    new("duplicate_pk", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, NAME VARCHAR(20))", $"INSERT INTO \"{t}\" VALUES (17,'a')" },
        t => $"INSERT INTO \"{t}\" VALUES (17,'dup')", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("not_null", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, NAME VARCHAR(20))", $"INSERT INTO \"{t}\" VALUES (17,'a')" },
        t => $"INSERT INTO \"{t}\" (NAME) VALUES ('missing')", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("fk_child_insert", t => new[] { $"CREATE TABLE \"{t}P\" (ID INT PRIMARY KEY)", $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, PID INT, FOREIGN KEY(PID) REFERENCES \"{t}P\"(ID))", $"INSERT INTO \"{t}P\" VALUES (1)", $"INSERT INTO \"{t}\" VALUES (1,1)" },
        t => $"INSERT INTO \"{t}\" VALUES (2,999)", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true, Extra: "P"),
    new("fk_parent_delete", t => new[] { $"CREATE TABLE \"{t}P\" (ID INT PRIMARY KEY)", $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, PID INT, FOREIGN KEY(PID) REFERENCES \"{t}P\"(ID))", $"INSERT INTO \"{t}P\" VALUES (1)", $"INSERT INTO \"{t}P\" VALUES (2)", $"INSERT INTO \"{t}\" VALUES (1,2)" },
        t => $"DELETE FROM \"{t}P\"", t => $"SELECT COUNT(*) FROM \"{t}P\"", 2, Dml: true, Extra: "P"),
    new("value_too_long", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, N VARCHAR(9 BYTE))", $"INSERT INTO \"{t}\" VALUES (1,'a')" },
        t => $"INSERT INTO \"{t}\" VALUES (2,'中文文中文文中文文')", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("multirow_partial", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)", $"INSERT INTO \"{t}\" VALUES (500)" },
        t => $"INSERT INTO \"{t}\" SELECT LEVEL FROM DUAL CONNECT BY LEVEL <= 1000", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("diskspace_quota", t => new[] { $"CREATE TABLE \"{t}\" (ID INT, V VARCHAR(4000)) DISKSPACE LIMIT 4 STORAGE(CLUSTERBTR)", $"INSERT INTO \"{t}\" VALUES (0,'initial')" },
        t => $"INSERT INTO \"{t}\" SELECT LEVEL, RPAD('x',4000,'x') FROM DUAL CONNECT BY LEVEL <= 2000", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("missing_table_select", t => new[] { $"CREATE TABLE \"{t}\" (ID INT)" },
        t => $"SELECT * FROM \"{t}_MISSING\"", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: true),
    new("raise_application_error", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-20001, 'probe guard'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("block_dml_then_raise", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => $"BEGIN INSERT INTO \"{t}\" VALUES (1); RAISE_APPLICATION_ERROR(-20002, 'probe guard'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("ddl_identity_rejected", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => $"CREATE TABLE \"{t}_SID\" (ID SMALLINT IDENTITY(3,2) PRIMARY KEY)", t => $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME='{t}_SID'", 0, Dml: true, Extra: "_SID"),
    new("ddl_duplicate_name", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => $"CREATE TABLE \"{t}\" (ID INT)", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: true),
    new("missing_sequence_default", t => new[] { $"CREATE SEQUENCE \"{t}Q\" START WITH 41", $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, N BIGINT DEFAULT \"{t}Q\".NEXTVAL)", $"INSERT INTO \"{t}\"(ID) VALUES (1)", $"DROP SEQUENCE \"{t}Q\"" },
        t => $"INSERT INTO \"{t}\"(ID) VALUES (2)", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("privilege_denied", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => $"CREATE SCHEMA \"{t}_S\"", t => $"SELECT COUNT(*) FROM SYS.SYSOBJECTS WHERE NAME='{t}_S' AND TYPE$='SCH'", 0, Dml: false),
    new("trigger_raise", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)", $"INSERT INTO \"{t}\" VALUES (1)",
            $"CREATE TRIGGER \"{t}_TR\" BEFORE INSERT ON \"{t}\" FOR EACH ROW BEGIN IF :NEW.ID > 100 THEN RAISE_APPLICATION_ERROR(-20003, 'probe trigger'); END IF; END;" },
        t => $"INSERT INTO \"{t}\" SELECT LEVEL + 95 FROM DUAL CONNECT BY LEVEL <= 10", t => $"SELECT COUNT(*) FROM \"{t}\"", 1, Dml: true),
    new("raise_20000", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-20000, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("raise_20071", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-20071, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("raise_20999", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-20999, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("raise_29999", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-29999, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("raise_30000", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-30000, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("raise_19999", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-19999, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("raise_30001", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => "BEGIN RAISE_APPLICATION_ERROR(-30001, 'probe range'); END;", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: false),
    new("fk_parent_update", t => new[] { $"CREATE TABLE \"{t}P\" (ID INT PRIMARY KEY)", $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, PID INT DEFAULT 1, FOREIGN KEY(PID) REFERENCES \"{t}P\"(ID) ON UPDATE NO ACTION)", $"INSERT INTO \"{t}P\" VALUES (1)", $"INSERT INTO \"{t}P\" VALUES (2)", $"INSERT INTO \"{t}\" VALUES (1,2)" },
        t => $"UPDATE \"{t}P\" SET ID=3 WHERE ID=2", t => $"SELECT COUNT(*) FROM \"{t}P\" WHERE ID=2", 1, Dml: true, Extra: "P"),
    new("mview_privilege_denied", t => new[] { $"CREATE TABLE \"{t}\" (ID INT) STORAGE(CLUSTERBTR)", $"CREATE TABLE \"{t}B\" (ID INT) STORAGE(CLUSTERBTR)" },
        t => $"CREATE MATERIALIZED VIEW \"{t}V\" FOR \"{t}B\" ON PREBUILT TABLE REFRESH COMPLETE ON DEMAND AS SELECT ID FROM \"{t}\"",
        t => $"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME='{t}V'", 0, Dml: false, Extra: "B"),
    new("sequence_exhausted", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)", $"CREATE SEQUENCE \"{t}Q\" START WITH 3 INCREMENT BY 1 MINVALUE 1 MAXVALUE 3 NOCYCLE NOCACHE NOORDER", $"SELECT \"{t}Q\".NEXTVAL FROM DUAL" },
        t => $"INSERT INTO \"{t}\" SELECT \"{t}Q\".NEXTVAL FROM DUAL", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: true),
    new("syntax_error", t => new[] { $"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY)" },
        t => $"INSRT INTO \"{t}\" VALUES (1)", t => $"SELECT COUNT(*) FROM \"{t}\"", 0, Dml: true),
};

bool markersOnly = args.Length == 1 && args[0] == "markers";
int exit = 0;
try
{
    if (markersOnly)
    {
        string trig = T("MKR");
        var markers = new Dictionary<string, object?>();
        output["markers"] = markers;
        ExecFresh($"CREATE TABLE \"{trig}\" (ID INT PRIMARY KEY, VERSION INT)");
        foreach (var (name, sql) in new[] {
            ("trigger_new_old_no_parameters", $"CREATE TRIGGER \"{trig}_TR\" BEFORE UPDATE ON \"{trig}\" FOR EACH ROW BEGIN :NEW.VERSION := :OLD.VERSION + 1; END;"),
            ("select_named_marker_no_parameters", "SELECT :X FROM DUAL"),
            ("select_positional_marker_no_parameters", "SELECT ? FROM DUAL") })
        {
            using var connection = Open();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
                markers[name] = new { threw = false };
            }
            catch (Exception e) { markers[name] = new { threw = true, type = e.GetType().Name, number = (e as DmException)?.Number }; }
        }
        using (var connection = Open())
        {
            Exec(connection, $"INSERT INTO \"{trig}\" VALUES (1, 1)");
            Exec(connection, $"UPDATE \"{trig}\" SET ID = 1");
            markers["trigger_effect_version"] = Scalar(connection, $"SELECT VERSION FROM \"{trig}\"");
        }
    }
    else
    {
        ExecFresh($"CREATE TABLE \"{mark}\" (ID INT PRIMARY KEY, NOTE VARCHAR(40))");
        foreach (var definition in definitions)
        foreach (bool asynchronous in new[] { false, true })
        foreach (bool inTransaction in definition.Dml ? new[] { false, true } : new[] { false })
            cases.Add(await RunCase(definition, asynchronous, inTransaction));
    }
}
catch (Exception error)
{
    output["failed"] = error.GetType().Name;
    exit = 1;
}
finally
{
    if (!markersOnly) output["cases"] = cases;
    try { output["cleanup"] = Cleanup(); }
    catch (Exception error) { output["cleanup"] = new { failed = error.GetType().Name }; exit = 1; }
    Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
}
return exit;

async Task<object> RunCase(Case definition, bool asynchronous, bool inTransaction)
{
    string tag = "C" + definitions.IndexOf(definition).ToString("D2", CultureInfo.InvariantCulture);
    string table = T(tag + (asynchronous ? "A" : "S") + (inTransaction ? "T" : "C"));
    if (definition.Extra != null) created.Add(table + definition.Extra);
    var result = new Dictionary<string, object?> { ["case"] = definition.Name, ["async"] = asynchronous, ["transaction"] = inTransaction };
    try { foreach (var sql in definition.Setup(table)) ExecFresh(sql); }
    catch (Exception e) { result["setup_failed"] = e.GetType().Name; return result; }
    using var connection = Open();
    string? sidBefore = sidSql == null ? null : Scalar(connection, sidSql);
    DbTransaction? transaction = null;
    int before = 0, after = 0;
    if (inTransaction)
    {
        transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        before = ++markId;
        Exec(connection, $"INSERT INTO \"{mark}\" VALUES ({before}, 'before')", transaction);
    }
    Exception? error = null;
    try
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = definition.Failing(table);
        if (asynchronous) await command.ExecuteNonQueryAsync(); else command.ExecuteNonQuery();
    }
    catch (Exception e) { error = e; }
    result["threw"] = error != null;
    result["exception_type"] = error?.GetType().Name;
#if OFFICIAL
    result["number"] = (error as DmException)?.Number;
#else
    result["number"] = (error as DmException)?.Number;
    result["reusable_reported"] = (error as DmException)?.FailureInfo?.ConnectionReusable;
    result["error_kind"] = (error as DmException)?.FailureInfo?.ErrorKind.ToString();
    result["transaction_outcome"] = (error as DmException)?.FailureInfo?.TransactionOutcome?.ToString();
#endif
    result["state_after"] = connection.State.ToString();
    try
    {
        string? sidAfter = sidSql == null ? "1" : Scalar(connection, sidSql, transaction);
        result["same_connection_usable"] = true;
        result["same_server_session"] = sidSql == null ? null : sidBefore == sidAfter;
        if (inTransaction)
        {
            using (var fresh = Open())
                result["tx_prior_work_still_uncommitted"] = Scalar(fresh, $"SELECT COUNT(*) FROM \"{mark}\" WHERE ID={before}") == "0";
            after = ++markId;
            Exec(connection, $"INSERT INTO \"{mark}\" VALUES ({after}, 'after')", transaction);
            transaction!.Commit();
            using var fresh2 = Open();
            result["tx_commit_kept_before_and_after"] =
                Scalar(fresh2, $"SELECT COUNT(*) FROM \"{mark}\" WHERE ID IN ({before},{after})") == "2";
        }
        else
        {
            after = ++markId;
            Exec(connection, $"INSERT INTO \"{mark}\" VALUES ({after}, 'autocommit')");
            using var fresh = Open();
            result["autocommit_after_error_visible"] = Scalar(fresh, $"SELECT COUNT(*) FROM \"{mark}\" WHERE ID={after}") == "1";
        }
    }
    catch (Exception e) { result["same_connection_usable"] = false; result["reuse_exception"] = e.GetType().Name; }
    using (var fresh = Open())
    {
        result["failed_statement_no_effect"] = Scalar(fresh, definition.Effect(table)) == definition.Expected.ToString(CultureInfo.InvariantCulture);
        result["no_lock_left"] = TryExclusiveLock(fresh, definition.LockTarget(table));
    }
    return result;
}

bool TryExclusiveLock(DbConnection connection, string table)
{
    try
    {
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        Exec(connection, $"LOCK TABLE \"{table}\" IN EXCLUSIVE MODE NOWAIT", transaction);
        transaction.Rollback();
        return true;
    }
    catch (DbException) { return false; }
}

object Cleanup()
{
    var dropped = new List<string>();
    var failures = new List<object>();
    foreach (var name in Enumerable.Reverse(created).Distinct().ToList())
    {
        using var connection = Open();
        foreach (var (kind, drop) in new[] { ("TABLE", " CASCADE"), ("SEQUENCE", "") })
        {
            string target = kind == "SEQUENCE" ? name + "Q" : name;
            if (Scalar(connection, $"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME='{target}' AND OBJECT_TYPE='{kind}'") == "0") continue;
            try { Exec(connection, $"DROP {kind} \"{target}\"{drop}"); dropped.Add(target); }
            catch (DbException e) { failures.Add(new { name = target, number = (e as DmException)?.Number }); }
        }
    }
    using var verify = Open();
    var remaining = Scalar(verify, $"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME LIKE 'ERP\\_%\\_{nonce}%' ESCAPE '\\'");
    return new { dropped = dropped.Count, failures, fresh_remaining = remaining };
}

DbConnection Open() { var connection = new DmConnection(settings); connection.Open(); return connection; }
void ExecFresh(string sql) { using var connection = Open(); Exec(connection, sql); }
void Exec(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.ExecuteNonQuery();
}
string? Scalar(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}

sealed record Case(string Name, Func<string, string[]> Setup, Func<string, string> Failing, Func<string, string> Effect,
    int Expected, bool Dml, string? Extra = null)
{
    internal string LockTarget(string table) => Extra == "P" ? table + "P" : table;
}
