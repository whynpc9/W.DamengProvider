using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using W.Dm;
using W.Dm.Config;

internal static class Program
{
    private const string TestUser = "WDM_PROVIDER_TEST";
    private const string IdentitySql = "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL";
    private static readonly Dictionary<string, object?> Report = new()
    {
        ["issue"] = 6, ["implementation"] = "W-project-reference", ["session_reset_verified"] = false,
        ["reuse_policy"] = "discard", ["performance_improvement_claimed"] = false,
        ["workload"] = "open_identity_select_close", ["database_objects_created"] = 0,
        ["final_database_state"] = "unverified"
    };
    private static readonly List<object> Results = [];

    public static async Task<int> Main(string[] args)
    {
        int code = 1;
        Observation? observation = null;
        try
        {
            Require(args.Length is >= 1 and <= 4 && args[0] is "offline" or "shared" or "tls", "usage");
            string mode = args[0];
            int samples = Number(args, 1, 100, 2, 10000);
            int workers = Number(args, 2, 4, 2, 32);
            int warmup = Number(args, 3, 5, 1, 100);
            Require(samples >= workers, "samples_must_cover_workers");
            Report["mode"] = mode; Report["samples_per_scenario"] = samples;
            Report["parallel_workers"] = workers; Report["warmup_leases_per_scenario"] = warmup;
            Report["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            Report["loaded_dll"] = typeof(DmConnection).Assembly.Location;
            Report["loaded_mvid"] = typeof(DmConnection).Assembly.ManifestModule.ModuleVersionId;
            Report["results"] = Results;
            observation = new Observation();
            await OfflineAsync(observation);
            if (mode == "offline")
            {
                Report["status"] = "integration_pending";
                Report["baseline_measured"] = false;
                code = 0;
            }
            else
            {
                string settings = Configure(mode, workers);
                await ReadProfileAsync(settings);
                foreach (string owner in mode == "tls" ? new[] { "datasource" } : new[] { "datasource", "direct_pooling" })
                {
                    await ScenarioAsync(owner, settings, samples, 1, warmup, observation);
                    await ScenarioAsync(owner, settings, samples, workers, warmup, observation);
                }
                // An independent final lease verifies the server identity after all measured work.
                await using (var source = new DmDataSource(settings))
                await using (var connection = source.CreateConnection())
                {
                    await connection.OpenAsync();
                    await VerifyIdentityAsync(connection);
                    await connection.CloseAsync();
                }
                Report["final_server_identity"] = TestUser;
                Report["final_server_schema"] = TestUser;
                Report["final_database_state"] = "read_only_no_objects_created";
                Require(observation.Modes.All(m => m == (mode == "tls" ? 1 : 0)), "negotiated_transport_mismatch");
                Require(observation.Modes.Length == observation.Total.Connected, "transport_mode_observation_missing");
                Require(observation.Total.Created == observation.Total.Disposed, "physical_connections_not_drained");
                Require(mode != "tls" || observation.TlsUpgrades == observation.Total.Connected, "tls_not_verified_for_every_connection");
                Require(mode != "shared" || observation.TlsUpgrades == 0, "plaintext_mode_not_observed");
                Report["status"] = "baseline_measured"; Report["baseline_measured"] = true;
                code = 0;
            }
        }
        catch (Exception error)
        {
            code = error is ProbeFailure { Kind: "integration_pending" } ? 3 : 1;
            Report["status"] = code == 3 ? "integration_pending" : "failed";
            Report["baseline_measured"] = false; Report["error"] = SafeError(error);
        }
        finally
        {
            if (observation != null)
            {
                Report["total_physical_connections"] = observation.Total;
                Report["negotiated_encrypt_modes"] = observation.Modes.Distinct().Order().ToArray();
                Report["successful_tls_upgrades"] = observation.TlsUpgrades;
                observation.Dispose();
            }
        }
        Report["exit_code"] = code;
        Console.WriteLine(JsonSerializer.Serialize(Report, new JsonSerializerOptions { WriteIndented = true }));
        return code;
    }

    private static int Number(string[] args, int index, int fallback, int min, int max)
    {
        if (index >= args.Length) return fallback;
        Require(int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= min && value <= max, "argument_out_of_range");
        return value;
    }

    private static string Configure(string mode, int workers)
    {
        string? raw = Environment.GetEnvironmentVariable(mode == "tls"
            ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING");
        Require(!string.IsNullOrWhiteSpace(raw), "integration_pending");
        var builder = new DmConnectionStringBuilder(raw!);
        Require(string.Equals(builder.User, TestUser, StringComparison.OrdinalIgnoreCase), "test_identity_required_before_authentication");
        builder.Schema = TestUser; builder.Pooling = true;
        builder.MaxPoolSize = workers; builder.MaxPoolWaiters = workers;
        builder.PoolAcquireTimeout = TimeSpan.FromSeconds(15); builder.ConnectTimeout = TimeSpan.FromSeconds(15);
        builder.CommandTimeout = 15; builder.StmtPooling = false; builder.PreparePooling = false;
        builder.PersistSecurityInfo = false; builder.LogLevel = LogLevel.OFF;
        builder.TransportSecurity = mode == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        if (mode == "tls")
        {
            Require(builder.Server == "127.0.0.1" && builder.Port == 15236, "isolated_tls_target_required");
            string certs = Path.Combine(Directory.GetCurrentDirectory(), ".local", "t07", "certs", "client_ssl", TestUser);
            builder.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem");
            builder.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
            builder.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem");
            builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
            Report["tls_revocation_policy"] = "NoCheck_for_isolated_CA_without_CRL";
        }
        Report["transport_policy"] = builder.TransportSecurity.ToString();
        Report["max_pool_size"] = workers;
        Report["connect_command_acquire_timeout_seconds"] = 15;
        return builder.ConnectionString;
    }

    private static async Task OfflineAsync(Observation observation)
    {
        var builder = new DmConnectionStringBuilder
        {
            Server = "synthetic.invalid", User = "SYNTHETIC_USER", Password = "synthetic_secret",
            Pooling = true, TlsCaCertificatePath = Path.Combine(Path.GetTempPath(), "issue6-missing-ca.pem")
        };
        await using (var source = new DmDataSource(builder.ConnectionString))
        await using (var connection = source.CreateConnection())
            Require(connection.State == ConnectionState.Closed, "construction_must_remain_closed");
        await using (var direct = new DmConnection(builder.ConnectionString))
        {
            bool rejected = false;
            try { await direct.OpenAsync(); }
            catch (NotSupportedException) { rejected = true; }
            Require(rejected, "direct_explicit_certificate_pooling_must_reject");
        }
        Require(observation.Total == new Counters(0, 0, 0), "offline_must_not_create_sockets");
        Report["offline_contract"] = "closed_construction_and_direct_explicit_certificate_rejection";
    }

    private static async Task ReadProfileAsync(string settings)
    {
        await using var source = new DmDataSource(settings);
        await using var connection = source.CreateConnection();
        await connection.OpenAsync();
        await VerifyIdentityAsync(connection);
        Require(Regex.IsMatch(connection.ServerVersion, @"\A\d+(\.\d+){1,5}\z"), "numeric_server_version_required");
        Report["server_identity"] = TestUser; Report["server_schema"] = TestUser;
        Report["server_version"] = connection.ServerVersion;
        try
        {
            await using var command = new DmCommand("SELECT PARA_VALUE FROM V$DM_INI WHERE PARA_NAME='COMPATIBLE_MODE'", connection);
            object? value = await command.ExecuteScalarAsync();
            Require(value != null && value != DBNull.Value, "compatible_mode_missing");
            Report["compatible_mode"] = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (DmException error) when (error.FailureInfo is
            { ErrorKind: DmErrorKind.Server, OperationOutcome: DmOperationOutcome.ServerReported })
        {
            Report["compatible_mode"] = null;
            Report["compatible_mode_unavailable"] = SafeError(error);
            // This dedicated profile connection is disposed on method exit. No later
            // command uses a session that the provider may have marked non-reusable.
        }
    }

    private static async Task VerifyIdentityAsync(DmConnection connection)
    {
        await using var command = new DmCommand(IdentitySql, connection) { CommandTimeout = 15 };
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser,
            "server_identity_or_schema_mismatch");
    }

    private static async Task ScenarioAsync(string kind, string settings, int samples, int workers,
        int warmup, Observation observation)
    {
        DmDataSource? source = kind == "datasource" ? new DmDataSource(settings) : null;
        using var directKey = source == null ? new DmConnection(settings) : null;
        var owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var result = new Dictionary<string, object?> { ["owner"] = kind, ["workers"] = workers,
            ["samples"] = samples, ["status"] = "started" };
        Results.Add(result);
        Func<DmConnection> factory = source == null ? () => new DmConnection(settings) : source.CreateConnection;
        var start = observation.Total;
        int completed = 0;
        try
        {
            for (int i = 0; i < warmup; i++) await LeaseAsync(factory, owners);
            var before = observation.Total;
            result["warmup_physical_connections"] = before - start;
            double[] latencies = new double[samples];
            int next = -1;
            using var stop = new CancellationTokenSource();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task WorkerAsync()
            {
                await gate.Task;
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        int index = Interlocked.Increment(ref next);
                        if (index >= samples) break;
                        latencies[index] = await LeaseAsync(factory, owners, stop.Token);
                        Interlocked.Increment(ref completed);
                    }
                }
                catch { stop.Cancel(); throw; }
            }
            Task[] tasks = Enumerable.Range(0, workers).Select(_ => WorkerAsync()).ToArray();
            var elapsed = Stopwatch.StartNew();
            gate.SetResult();
            await Task.WhenAll(tasks);
            elapsed.Stop();
            Array.Sort(latencies);
            result["open_async_ms"] = new { p50 = Percentile(latencies, .50), p95 = Percentile(latencies, .95), p99 = Percentile(latencies, .99) };
            result["lease_elapsed_seconds"] = elapsed.Elapsed.TotalSeconds;
            result["leases_per_second"] = samples / elapsed.Elapsed.TotalSeconds;
            result["measured_physical_connections"] = observation.Total - before;
            result["status"] = "measured";
        }
        finally
        {
            result["completed_measurement_leases"] = Volatile.Read(ref completed);
            if (source != null) await source.DisposeAsync();
            else DmConnection.ClearPool(directKey!);
            result["final_physical_connections_including_warmup"] = observation.Total - start;
            result["final_pool_states"] = owners.Select(Snapshot).ToArray();
            result["final_drained"] = owners.All(o => Snapshot(o).Values.All(v => v == 0))
                && (observation.Total - start).Created == (observation.Total - start).Disposed;
        }
        Require((bool)result["final_drained"]!, "scenario_not_drained");
    }

    private static async Task<double> LeaseAsync(Func<DmConnection> factory, HashSet<object> owners,
        CancellationToken cancellationToken = default)
    {
        await using var connection = factory();
        var started = Stopwatch.GetTimestamp();
        await connection.OpenAsync(cancellationToken);
        double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        object owner = typeof(DmConnection).GetProperty("PoolOwner", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(connection) ?? throw new ProbeFailure("pool_owner_missing");
        lock (owners) owners.Add(owner);
        await VerifyIdentityAsync(connection);
        await connection.CloseAsync();
        return milliseconds;
    }

    private static Dictionary<string, long> Snapshot(object owner)
    {
        object snapshot = owner.GetType().GetProperty("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        return new[] { "Creating", "Leased", "Closing", "Waiting", "Idle", "Resetting" }.ToDictionary(n => n,
            n => Convert.ToInt64(snapshot.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(snapshot), CultureInfo.InvariantCulture));
    }
    private static double Percentile(double[] values, double percentile) => values[(int)Math.Ceiling(values.Length * percentile) - 1];
    private static object SafeError(Exception error) => new
    {
        type = error.GetType().FullName, classification = (error as ProbeFailure)?.Kind,
        phase = (error as DmException)?.FailureInfo?.Phase.ToString(),
        kind = (error as DmException)?.FailureInfo?.ErrorKind.ToString(),
        outcome = (error as DmException)?.FailureInfo?.OperationOutcome.ToString(),
        server_number = (error as DmException)?.FailureInfo?.ServerErrorNumber,
        driver_error_code = (error as DmException)?.DriverErrorCode,
        exception_chain = SafeExceptionChain(error)
    };
    private static object[] SafeExceptionChain(Exception error)
    {
        var chain = new List<object>();
        for (Exception? current = error; current != null && chain.Count < 5; current = current.InnerException)
            chain.Add(new
            {
                type = current.GetType().FullName,
                socket_error = (current as System.Net.Sockets.SocketException)?.SocketErrorCode.ToString(),
                native_error_code = (current as System.Net.Sockets.SocketException)?.NativeErrorCode,
                io_hresult = current is IOException ? (int?)current.HResult : null
            });
        return chain.ToArray();
    }
    private static void Require(bool condition, string kind) { if (!condition) throw new ProbeFailure(kind); }
    private sealed class ProbeFailure(string kind) : Exception { internal string Kind { get; } = kind; }
    private sealed record Counters(long Created, long Disposed, long Connected)
    {
        public static Counters operator -(Counters a, Counters b) => new(a.Created - b.Created, a.Disposed - b.Disposed, a.Connected - b.Connected);
    }
    private sealed class Observation : IDisposable
    {
        private readonly Type transport = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")!;
        private readonly FieldInfo modeField = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!
            .GetField("AfterStartupNegotiatedEncryptMode", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly List<int> modes = [];
        private readonly Counters initial;
        private readonly long initialTls;
        internal Observation()
        {
            initial = Read(); initialTls = Counter("SuccessfulTlsUpgrades");
            Require(modeField.GetValue(null) == null, "isolated_observation_required");
            modeField.SetValue(null, (Action<int>)(mode => { lock (modes) modes.Add(mode); }));
        }
        private long Counter(string name) => (long)(transport.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new ProbeFailure("transport_counter_missing"));
        private Counters Read() => new(Counter("CreatedTcpSockets"), Counter("DisposedTcpSockets"), Counter("SuccessfulTcpConnections"));
        internal Counters Total => Read() - initial;
        internal long TlsUpgrades => Counter("SuccessfulTlsUpgrades") - initialTls;
        internal int[] Modes { get { lock (modes) return modes.ToArray(); } }
        public void Dispose() => modeField.SetValue(null, null);
    }
}
