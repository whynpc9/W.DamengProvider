using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;
using W.Dm.Config;

// Copied to an independent temporary project with a sole PackageReference.
internal static class Program
{
    private const string PackageId = "W.DamengProvider";
    private const string TestUser = "WDM_PROVIDER_TEST";
    private static string Stage = "arguments";
    private static readonly Dictionary<string, object?> Report = new();

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Need(args.Length is 2 or 3 && args[0] is "offline" or "plaintext" or "tls", "usage");
            using var manifest = JsonDocument.Parse(File.ReadAllText(args[1]));
            Stage = "package_identity";
            var assembly = typeof(DmConnection).Assembly;
            Need(assembly.GetName().Name == PackageId, "assembly_name_mismatch");
            Need(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ==
                 manifest.RootElement.GetProperty("tag_version").GetString(), "assembly_version_mismatch");
            Need(Path.GetFullPath(assembly.Location) == Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PackageId + ".dll")), "assembly_location_mismatch");
            Need(assembly.GetReferencedAssemblies().All(a => !string.Equals(a.Name, "DM.DmProvider", StringComparison.OrdinalIgnoreCase)), "official_driver_reference");
            foreach (var asset in manifest.RootElement.GetProperty("assets").EnumerateObject())
            {
                string relative = asset.Name["lib/net10.0/".Length..];
                Need(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, relative)))) ==
                     asset.Value.GetString(), "loaded_package_asset_mismatch");
            }
            using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NuGetSmoke.deps.json")));
            Need(deps.RootElement.GetProperty("libraries").GetProperty(PackageId + "/" + manifest.RootElement.GetProperty("version").GetString())
                 .GetProperty("type").GetString() == "package", "package_reference_required");
            Report["assembly"] = assembly.GetName().Name;
            Report["package_version"] = manifest.RootElement.GetProperty("version").GetString();
            Report["package_sha256"] = manifest.RootElement.GetProperty("package_sha256").GetString();
            Report["loaded_dll_sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)));
            Stage = "factory_and_resources";
            DbProviderFactories.RegisterFactory(PackageId, DmClientFactory.Instance);
            Need(ReferenceEquals(DbProviderFactories.GetFactory(PackageId), DmClientFactory.Instance), "factory_registration_failed");
            using var connection = DmClientFactory.Instance.CreateConnection();
            using var command = DmClientFactory.Instance.CreateCommand();
            Need(connection is DmConnection && command is DmCommand &&
                 DmClientFactory.Instance.CreateParameter() is DmParameter &&
                 DmClientFactory.Instance.CreateConnectionStringBuilder() is DmConnectionStringBuilder, "factory_type_mismatch");
            var manager = new ResourceManager("Dm.DmErrorDefinition", assembly);
            foreach (string culture in new[] { "", "en", "zh-CN", "zh-HK", "zh-TW" })
            {
                Need(!string.IsNullOrWhiteSpace(manager.GetString("message.error", CultureInfo.GetCultureInfo(culture))), "localized_resource_missing");
                if (culture.Length > 0)
                {
                    var satellite = assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo(culture));
                    Need(satellite.GetName().Name == PackageId + ".resources", "satellite_identity_mismatch");
                    using var resource = satellite.GetManifestResourceStream("Dm.DmErrorDefinition." + culture + ".resources");
                    Need(resource is not null, "satellite_resource_missing");
                }
            }
            if (args[0] != "offline")
            {
                Stage = "database_configuration";
                string variable = args[0] == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING";
                string raw = Environment.GetEnvironmentVariable(variable) ?? throw new InvalidOperationException("integration_pending");
                var builder = new DmConnectionStringBuilder(raw);
                Need(builder.User.Equals(TestUser, StringComparison.OrdinalIgnoreCase), "configured_TEST_identity_required");
                builder.Schema = TestUser;
                builder.Pooling = false; builder.StmtPooling = false; builder.PreparePooling = false;
                builder.PersistSecurityInfo = false; builder.LogLevel = LogLevel.OFF;
                builder.ConnectTimeout = TimeSpan.FromSeconds(20); builder.CommandTimeout = 30;
                builder.TransportSecurity = args[0] == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
                if (args[0] == "tls")
                {
                    Need(args.Length == 3 && builder.Server == "127.0.0.1" && builder.Port == 15236, "isolated_TLS_configuration_required");
                    string certs = Path.Combine(Path.GetFullPath(args[2]), ".local/t07/certs/client_ssl", TestUser);
                    builder.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem");
                    builder.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
                    builder.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem");
                    // The local test CA has no CRL; product default remains Online.
                    builder.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
                }
                await DatabaseAsync(builder.ConnectionString);
                Report["transport"] = args[0] == "tls" ? "RequireTls" : "PlaintextAllowed";
                Report["integration"] = "real_TEST_verified";
            }
            else Report["integration"] = "integration_pending";
            Report["status"] = "smoke_verified";
            Console.WriteLine(JsonSerializer.Serialize(Report));
            return 0;
        }
        catch (Exception error)
        {
            Report["status"] = "rejected"; Report["stage"] = Stage;
            Report["safe_error"] = DmDiagnostics.FormatException(error);
            Console.Error.WriteLine(JsonSerializer.Serialize(Report));
            return 1;
        }
    }

    private static async Task<DmConnection> OpenAsync(string settings)
    {
        var connection = new DmConnection(settings);
        try
        {
            await connection.OpenAsync();
            using var identity = connection.CreateCommand();
            identity.CommandText = "SELECT USER,SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL";
            using var reader = await identity.ExecuteReaderAsync();
            Need(await reader.ReadAsync() && reader.GetString(0) == TestUser && reader.GetString(1) == TestUser, "server_TEST_identity_required");
            Report["server_account"] = TestUser; Report["server_schema"] = TestUser;
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<int> ExecuteAsync(DmConnection connection, string sql, DmTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql; command.Transaction = transaction;
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(DmConnection connection, string sql, string? table = null)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql;
        if (table is not null) command.Parameters.Add(new DmParameter("name", table));
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task DatabaseAsync(string settings)
    {
        string table = "NUGET_" + Guid.NewGuid().ToString("N")[..24].ToUpperInvariant();
        bool created = false;
        Report["owned_table"] = table;
        try
        {
            Stage = "database_create";
            await using var connection = await OpenAsync(settings);
            Need(await CountAsync(connection, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:name", table) == 0, "table_name_already_exists");
            await ExecuteAsync(connection, $"CREATE TABLE {table}(ID INT PRIMARY KEY,V VARCHAR(400),D TIMESTAMP(6),B BLOB,C CLOB)");
            created = true;
            Stage = "parameter_transaction_write";
            byte[] bytes = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
            string text = "NuGet 参数化中文 🐾 " + new string('X', 2048);
            var timestamp = new DateTime(2026, 10, 13, 12, 34, 56, DateTimeKind.Unspecified).AddTicks(1234560);
            await using (var transaction = (DmTransaction)await connection.BeginTransactionAsync())
            {
                using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {table}(ID,V,D,B,C) VALUES(:id,:v,:d,:b,:c)";
                insert.Parameters.Add(new DmParameter("id", 1));
                insert.Parameters.Add(new DmParameter("v", "package-中文"));
                insert.Parameters.Add(new DmParameter("d", timestamp) { DbType = DbType.DateTime, Scale = 6 });
                insert.Parameters.Add(new DmParameter("b", DmDbType.Blob) { Value = bytes });
                insert.Parameters.Add(new DmParameter("c", DmDbType.Clob) { Value = text });
                Need(await insert.ExecuteNonQueryAsync() == 1, "insert_row_count_mismatch");
                await transaction.CommitAsync();
            }
            Stage = "fresh_parameter_reader";
            await using (var fresh = await OpenAsync(settings))
            {
                using var read = fresh.CreateCommand(); read.CommandText = $"SELECT ID,V,D,B,C FROM {table} WHERE ID=:id";
                read.Parameters.Add(new DmParameter("id", 1));
                using var reader = await read.ExecuteReaderAsync();
                Need(await reader.ReadAsync() && reader.GetInt32(0) == 1 && reader.GetString(1) == "package-中文" &&
                     reader.GetDateTime(2) == timestamp && ((byte[])reader.GetValue(3)).AsSpan().SequenceEqual(bytes) &&
                     reader.GetString(4) == text && !await reader.ReadAsync(), "parameter_reader_roundtrip_mismatch");
            }
            Stage = "transaction_rollback";
            await using (var transaction = (DmTransaction)await connection.BeginTransactionAsync())
            {
                Need(await ExecuteAsync(connection, $"UPDATE {table} SET V='rollback' WHERE ID=1", transaction) == 1, "update_row_count_mismatch");
                await transaction.RollbackAsync();
            }
            await using (var fresh = await OpenAsync(settings))
                Need(await CountAsync(fresh, $"SELECT COUNT(*) FROM {table} WHERE ID=1 AND V='package-中文'") == 1, "rollback_not_persisted");
            Report["parameter_reader_transaction_temporal_blob_clob"] = true;
        }
        finally
        {
            if (created)
            {
                // Ownership comes from this exact successful CREATE and GUID, not a prefix scan.
                string originalStage = Stage;
                Stage = "exact_owned_object_cleanup";
                await using (var cleanup = await OpenAsync(settings)) await ExecuteAsync(cleanup, $"DROP TABLE {table}");
                await using (var verify = await OpenAsync(settings))
                    Need(await CountAsync(verify, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:name", table) == 0, "owned_table_remains");
                Report["final_database_state"] = "owned_table_absent";
                Stage = originalStage;
            }
        }
    }

    private static void Need(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
