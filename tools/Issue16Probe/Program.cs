using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using W.Dm;

// Real-server checks for GitHub issue #16. Output holds only booleans, numbers,
// enum names and generated object names; never the connection string.
const string TestUser = "WDM_PROVIDER_TEST";
string raw = Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("test connection missing");
var baseBuilder = new DmConnectionStringBuilder(raw) { ConnPooling = false, TransportSecurity = DmTransportSecurity.PlaintextAllowed };
string settings = baseBuilder.ConnectionString;
string serverHost = baseBuilder.Server;
int serverPort = baseBuilder.Port;
string nonce = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
var created = new List<string>();
var output = new Dictionary<string, object?> { ["nonce"] = nonce };
var failures = new List<string>();
string T(string tag) { var name = $"I16_{tag}_{nonce}"; created.Add(name); return name; }
void Check(string id, bool ok) { if (!ok) failures.Add(id); }

try
{
    using (var check = Open())
    {
        if (Scalar(check, "SELECT USER FROM DUAL") != TestUser) { Console.Error.WriteLine("wrong account"); return 2; }
        var caps = check.ServerCapabilities;
        output["identity"] = TestUser;
        output["server_version"] = check.ServerVersion;
        output["capabilities"] = new
        {
            caps.ServerVersion, caps.IsMeasuredProfile, caps.SupportsSavepoints, caps.SupportsReadUncommitted,
            caps.SupportsSerializable, caps.SupportsStatementErrorRecovery, caps.SupportsTableDdlInTransaction,
            caps.MaxTemporalWriteScale, caps.TemporalProfile
        };
        using var tx = check.BeginTransaction();
        Check("capabilities_match_gates", caps.SupportsSavepoints == ((DmTransaction)tx).SupportsSavepoints);
        tx.Rollback();
    }
    output["anonymous_blocks"] = await AnonymousBlocks();
    output["temporal_null"] = await TemporalNull();
    output["transport"] = await Transport();
    output["broken_message"] = BrokenMessage();
}
catch (Exception error)
{
    failures.Add("unexpected_" + error.GetType().Name);
    output["unexpected"] = DmDiagnostics.FormatException(error);
}
finally
{
    output["cleanup"] = Cleanup();
}
output["failures"] = failures;
output["accepted"] = failures.Count == 0;
Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
return failures.Count == 0 ? 0 : 1;

async Task<object> AnonymousBlocks()
{
    string t = T("BLK");
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, NAME VARCHAR(20))");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (1,'a')");
    var result = new Dictionary<string, object?>();
    foreach (bool asynchronous in new[] { false, true })
    {
        string mode = asynchronous ? "async" : "sync";
        using (var connection = Open())
        {
            string before = ScalarFresh($"SELECT NAME FROM \"{t}\" WHERE ID=1")!;
            using var tx = (DmTransaction)connection.BeginTransaction();
            int affected = await Run(connection, tx, $"BEGIN UPDATE \"{t}\" SET NAME='x' WHERE ID=1; END;", asynchronous);
            string inside = Scalar(connection, $"SELECT NAME FROM \"{t}\" WHERE ID=1", tx)!;
            tx.Rollback();
            string fresh = ScalarFresh($"SELECT NAME FROM \"{t}\" WHERE ID=1")!;
            result[mode + "_rollback"] = new { affected, before, inside, fresh, outcome = tx.Outcome.ToString() };
            Check(mode + "_block_rollback", inside == "x" && fresh == before && tx.Outcome == DmTransactionOutcome.RolledBack);
        }
        using (var connection = Open())
        {
            using var tx = (DmTransaction)connection.BeginTransaction();
            await Run(connection, tx, $"BEGIN UPDATE \"{t}\" SET NAME='{mode}' WHERE ID=1; INSERT INTO \"{t}\" VALUES (10{(asynchronous ? 1 : 0)},'n'); END;", asynchronous);
            tx.Commit();
            string fresh = ScalarFresh($"SELECT NAME FROM \"{t}\" WHERE ID=1")!;
            string inserted = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID=10{(asynchronous ? 1 : 0)}")!;
            result[mode + "_commit"] = new { fresh, inserted, outcome = tx.Outcome.ToString() };
            Check(mode + "_block_commit", fresh == mode && inserted == "1" && tx.Outcome == DmTransactionOutcome.Committed);
        }
        using (var connection = Open())
        {
            using var tx = (DmTransaction)connection.BeginTransaction();
            await Run(connection, tx, $"UPDATE \"{t}\" SET NAME='before' WHERE ID=1", asynchronous);
            int? number = null; string? kind = null; bool? reusable = null;
            try { await Run(connection, tx, $"BEGIN INSERT INTO \"{t}\" VALUES (2,'b'); INSERT INTO \"{t}\" VALUES (1,'dup'); END;", asynchronous); }
            catch (DmException error) { number = error.Number; kind = error.ErrorKind.ToString(); reusable = error.FailureInfo?.ConnectionReusable; }
            string state = connection.State.ToString();
            string? partialInside = null, priorInside = null;
            if (connection.State == ConnectionState.Open)
            {
                partialInside = Scalar(connection, $"SELECT COUNT(*) FROM \"{t}\" WHERE ID=2", tx);
                priorInside = Scalar(connection, $"SELECT NAME FROM \"{t}\" WHERE ID=1", tx);
                tx.Rollback();
            }
            string freshPartial = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID=2")!;
            string freshName = ScalarFresh($"SELECT NAME FROM \"{t}\" WHERE ID=1")!;
            result[mode + "_failing_block"] = new { number, kind, reusable, state, partialInside, priorInside, freshPartial, freshName, outcome = tx.Outcome.ToString() };
            Check(mode + "_failing_block", number == -6602 && kind == "Server" && freshPartial == "0" && freshName == mode &&
                (state != "Open" || (partialInside == "0" && priorInside == "before")));
        }
        using (var connection = Open())
        {
            using var tx = (DmTransaction)connection.BeginTransaction();
            bool rejected = false;
            foreach (string sql in new[]
            {
                $"BEGIN UPDATE \"{t}\" SET NAME='c' WHERE ID=1; COMMIT; END;",
                $"BEGIN EXECUTE IMMEDIATE 'UPDATE \"{t}\" SET NAME=''d'''; END;",
                $"DECLARE V INT; BEGIN UPDATE \"{t}\" SET NAME='e' WHERE ID=1; END;",
                $"BEGIN UPDATE \"{t}\" SET NAME='f' WHERE ID=1; EXCEPTION WHEN OTHERS THEN NULL; END;",
                $"BEGIN NULL; \"{t}_PROC\"(); END;"
            })
            {
                try { await Run(connection, tx, sql, asynchronous); rejected = false; break; }
                catch (NotSupportedException) { rejected = true; continue; }
            }
            bool active = tx.Outcome == DmTransactionOutcome.Active && connection.State == ConnectionState.Open;
            tx.Rollback();
            string fresh = ScalarFresh($"SELECT NAME FROM \"{t}\" WHERE ID=1")!;
            result[mode + "_control_blocks"] = new { rejected, active, fresh };
            Check(mode + "_control_blocks_rejected", rejected && active && fresh == mode);
        }
    }
    return result;
}

async Task<object> TemporalNull()
{
    string t = T("TS9");
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, TS TIMESTAMP(9), TS8 TIMESTAMP(8), TZ TIMESTAMP(9) WITH TIME ZONE, T6 TIMESTAMP(6))");
    var result = new Dictionary<string, object?>();
    int id = 0;
    foreach (bool prepared in new[] { false, true })
    foreach (bool asynchronous in new[] { false, true })
    {
        string mode = (prepared ? "prepared_" : "text_") + (asynchronous ? "async" : "sync");
        id++;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO \"{t}\" (ID, TS, TS8, TZ, T6) VALUES (:id, :ts, :ts8, :tz, :t6)";
        command.Parameters.Add(new DmParameter("id", id));
        command.Parameters.Add(new DmParameter("ts", DBNull.Value) { DbType = DbType.DateTime2, Scale = 9 });
        command.Parameters.Add(new DmParameter("ts8", DBNull.Value) { DbType = DbType.DateTime2, Scale = 8 });
        command.Parameters.Add(new DmParameter("tz", DBNull.Value) { DbType = DbType.DateTimeOffset, Scale = 9 });
        command.Parameters.Add(new DmParameter("t6", new DateTime(2026, 10, 14, 1, 2, 3).AddTicks(123450)) { DbType = DbType.DateTime2, Scale = 6 });
        if (prepared) command.Prepare();
        int inserted = asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
        // EF-style UPDATE: same facets, NULL value.
        using var update = connection.CreateCommand();
        update.CommandText = $"UPDATE \"{t}\" SET TS = :ts WHERE ID = :id";
        update.Parameters.Add(new DmParameter("ts", DBNull.Value) { DbType = DbType.DateTime2, Scale = 9 });
        update.Parameters.Add(new DmParameter("id", id));
        if (prepared) update.Prepare();
        int updated = asynchronous ? await update.ExecuteNonQueryAsync() : update.ExecuteNonQuery();
        string nulls = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID={id} AND TS IS NULL AND TS8 IS NULL AND TZ IS NULL AND T6 IS NOT NULL")!;
        string t6 = ScalarFresh($"SELECT TO_CHAR(T6, 'YYYY-MM-DD HH24:MI:SS.FF6') FROM \"{t}\" WHERE ID={id}")!;
        // A non-NULL value at Scale 9 is still rejected before any send.
        using var nonNull = connection.CreateCommand();
        nonNull.CommandText = $"UPDATE \"{t}\" SET TS = :ts WHERE ID = :id";
        nonNull.Parameters.Add(new DmParameter("ts", new DateTime(2026, 10, 14)) { DbType = DbType.DateTime2, Scale = 9 });
        nonNull.Parameters.Add(new DmParameter("id", id));
        bool nonNullRejected = false;
        try { if (asynchronous) await nonNull.ExecuteNonQueryAsync(); else nonNull.ExecuteNonQuery(); }
        catch (NotSupportedException) { nonNullRejected = true; }
        // Recorded only: this rejection happens after the server describe, on the unchanged non-NULL path.
        string stateAfterRejection = connection.State.ToString();
        string unchanged = ScalarFresh($"SELECT COUNT(*) FROM \"{t}\" WHERE ID={id} AND TS IS NULL")!;
        result[mode] = new { inserted, updated, nulls, t6, nonNullRejected, stateAfterRejection, unchanged };
        Check("temporal_null_" + mode, inserted == 1 && updated == 1 && nulls == "1" && t6 == "2026-10-14 01:02:03.012345" &&
            nonNullRejected && unchanged == "1");
    }
    return result;
}

async Task<object> Transport()
{
    var result = new Dictionary<string, object?>();
    // Refused TCP connect: a loopback port with no listener.
    int closedPort;
    using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); closedPort = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
    foreach (bool asynchronous in new[] { false, true })
    {
        var refused = new DmConnectionStringBuilder(settings) { Server = "127.0.0.1", Port = closedPort };
        Exception? error = null;
        try
        {
            using var connection = new DmConnection(refused.ConnectionString);
            if (asynchronous) await connection.OpenAsync(); else connection.Open();
        }
        catch (Exception e) { error = e; }
        result[(asynchronous ? "async" : "sync") + "_connect_refused"] = Describe(error);
        Check((asynchronous ? "async" : "sync") + "_connect_refused", error is DmException { ErrorKind: DmErrorKind.Transport } d &&
            d.FailureInfo!.OperationOutcome == DmOperationOutcome.NotSent && d.FailureInfo.Phase == DmFailurePhase.Connect &&
            !d.FailureInfo.ConnectionReusable && d.InnerException is SocketException);
    }
    string t = T("NET");
    ExecFresh($"CREATE TABLE \"{t}\" (ID INT PRIMARY KEY, NAME VARCHAR(20))");
    ExecFresh($"INSERT INTO \"{t}\" VALUES (1,'a')");
    foreach (bool asynchronous in new[] { false, true })
    foreach (string shape in new[] { "nonquery", "reader", "scalar", "transaction" })
    {
        string id = (asynchronous ? "async_" : "sync_") + shape;
        await using var proxy = new ResettingProxy(serverHost, serverPort);
        var viaProxy = new DmConnectionStringBuilder(settings) { Server = "127.0.0.1", Port = proxy.Port };
        using var connection = new DmConnection(viaProxy.ConnectionString);
        connection.Open();
        DmTransaction? tx = shape == "transaction" ? (DmTransaction)connection.BeginTransaction() : null;
        if (tx != null) Exec(connection, $"UPDATE \"{t}\" SET NAME='tx' WHERE ID=1", tx);
        proxy.ResetAll();
        Exception? error = null;
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = shape is "reader" or "scalar" ? $"SELECT NAME FROM \"{t}\"" : $"UPDATE \"{t}\" SET NAME='lost' WHERE ID=1";
            if (shape == "scalar")
            {
                if (asynchronous) await command.ExecuteScalarAsync(); else command.ExecuteScalar();
            }
            else if (shape == "reader")
            {
                using var reader = asynchronous ? await command.ExecuteReaderAsync() : command.ExecuteReader();
                while (asynchronous ? await reader.ReadAsync() : reader.Read()) { }
            }
            else if (asynchronous) await command.ExecuteNonQueryAsync();
            else command.ExecuteNonQuery();
        }
        catch (Exception e) { error = e; }
        string state = connection.State.ToString();
        string? next = null;
        try { Scalar(connection, "SELECT 1 FROM DUAL"); } catch (InvalidOperationException e) { next = e.Message; }
        string fresh = ScalarFresh($"SELECT NAME FROM \"{t}\" WHERE ID=1")!;
        result[id] = new { error = Describe(error), state, next_message_mentions_broken = next?.Contains("broken", StringComparison.Ordinal), fresh,
            tx_outcome = tx?.Outcome.ToString() };
        var info = (error as DmException)?.FailureInfo;
        Check(id, error is DmException { ErrorKind: DmErrorKind.Transport } && info!.ErrorCode == "WDM_TRANSPORT" &&
            info.OperationOutcome is DmOperationOutcome.Unknown or DmOperationOutcome.NotSent && !info.ConnectionReusable &&
            error.InnerException is SocketException or IOException && state == "Broken" && next?.Contains("broken", StringComparison.Ordinal) == true &&
            fresh == "a" && (tx == null || info.TransactionOutcome == DmTransactionOutcome.OutcomeUnknown) &&
            DmDiagnostics.FormatException(error).Contains("\"WDM_TRANSPORT\"", StringComparison.Ordinal));
        tx?.Dispose();
    }
    return result;
}

object BrokenMessage()
{
    using var connection = Open();
    int? number = null;
    try { Scalar(connection, "SELECT SLEEP(1) FROM DUAL"); } catch (DmException e) { number = e.Number; }
    string state = connection.State.ToString();
    string? next = null;
    if (connection.State == ConnectionState.Broken)
        try { Scalar(connection, "SELECT 1 FROM DUAL"); } catch (InvalidOperationException e) { next = e.Message; }
    Check("broken_message", number != null && (state == "Open" || next?.Contains("earlier operation failed", StringComparison.Ordinal) == true));
    return new { number, state, next_message_mentions_earlier_failure = next?.Contains("earlier operation failed", StringComparison.Ordinal) };
}

object Describe(Exception? error) => error == null ? new { type = "none" } : new
{
    type = error.GetType().Name,
    inner = error.InnerException?.GetType().Name,
    diagnostics = JsonDocument.Parse(DmDiagnostics.FormatException(error)).RootElement
};

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
    string remaining = Scalar(verify, $"SELECT COUNT(*) FROM USER_OBJECTS WHERE OBJECT_NAME LIKE 'I16\\_%\\_{nonce}' ESCAPE '\\'")!;
    if (cleanupFailures.Count != 0 || remaining != "0") failures.Add("cleanup");
    return new { dropped, failures = cleanupFailures, fresh_remaining = remaining };
}

DmConnection Open() { var connection = new DmConnection(settings); connection.Open(); return connection; }
void ExecFresh(string sql) { using var connection = Open(); Exec(connection, sql); }
string? ScalarFresh(string sql) { using var connection = Open(); return Scalar(connection, sql); }
void Exec(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.ExecuteNonQuery();
}
async Task<int> Run(DbConnection connection, DbTransaction? transaction, string sql, bool asynchronous)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    return asynchronous ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery();
}
string? Scalar(DbConnection connection, string sql, DbTransaction? transaction = null)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}

// Loopback TCP relay to the configured server. ResetAll aborts every relayed
// connection with RST (linger 0), as an intermediate network device would.
sealed class ResettingProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly List<(Socket Client, Socket Server)> pairs = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task acceptLoop;
    private readonly string host;
    private readonly int port;
    internal int Port { get; }

    internal ResettingProxy(string host, int port)
    {
        this.host = host; this.port = port;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptLoop = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener.AcceptSocketAsync(stop.Token); } catch { return; }
            var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await server.ConnectAsync(host, port);
            lock (pairs) pairs.Add((client, server));
            _ = Pump(client, server);
            _ = Pump(server, client);
        }
    }

    private static async Task Pump(Socket from, Socket to)
    {
        var buffer = new byte[16384];
        try
        {
            while (true)
            {
                int read = await from.ReceiveAsync(buffer, SocketFlags.None);
                if (read <= 0) break;
                await to.SendAsync(buffer.AsMemory(0, read), SocketFlags.None);
            }
        }
        catch { }
    }

    internal void ResetAll()
    {
        lock (pairs)
            foreach (var (client, server) in pairs)
            {
                foreach (var socket in new[] { client, server })
                {
                    try { socket.LingerState = new LingerOption(true, 0); socket.Close(); } catch { }
                }
            }
        Thread.Sleep(200);
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        ResetAll();
        try { await acceptLoop; } catch { }
    }
}
