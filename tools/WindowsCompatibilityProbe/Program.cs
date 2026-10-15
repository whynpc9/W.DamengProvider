using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using W.Dm;

// Read-only profile and final-state observation. Never accepts administrator credentials.
var report = new Dictionary<string, object?> { ["runtime"] = RuntimeInformation.FrameworkDescription,
    ["rid"] = RuntimeInformation.RuntimeIdentifier, ["os"] = RuntimeInformation.OSDescription };
int exit = 1;
try
{
    string user = Environment.GetEnvironmentVariable("DAMENG_TEST_EXPECTED_USER") ?? "WDM_PROVIDER_TEST";
    Require(user == "WDM_PROVIDER_TEST" || Regex.IsMatch(user, @"\AWDM_PROVIDER_TEST_I11_[A-F0-9]{8}\z"));
    var builder = new DmConnectionStringBuilder(Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException("integration_pending"));
    Require(builder.User == user);
    builder.Schema = user; builder.ConnPooling = false; builder.ConnectTimeout = TimeSpan.FromSeconds(20);
    builder.CommandTimeout = 20; builder.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
    await using (var connection = new DmConnection(builder.ConnectionString))
    {
        await connection.OpenAsync();
        Require(await Scalar(connection, "SELECT USER FROM DUAL") == user);
        Require(await Scalar(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL") == user);
        report["user"] = user; report["schema"] = user; report["server_version"] = connection.ServerVersion;
        report["capabilities"] = connection.ServerCapabilities;
        object instance = Field(connection, "m_ConnInst");
        object properties = Property(instance, "ConnProperty");
        report["server_encoding"] = Property(properties, "ServerEncoding");
        report["message_version"] = Field(properties, "msgVersion");
        report["explicit_transport"] = "PlaintextAllowed";
        report["remaining_tables"] = await Scalar(connection, "SELECT COUNT(*) FROM USER_TABLES");
        report["remaining_objects"] = await Scalar(connection, "SELECT COUNT(*) FROM USER_OBJECTS");
        var assembly = typeof(DmConnection).Assembly;
        report["assembly_version"] = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }
    // A fresh connection for each optional query avoids carrying a failed metadata query forward.
    var metadata = new Dictionary<string, object?>();
    foreach (var (name, sql) in new[] { ("full_version", "SELECT BANNER FROM V$VERSION"),
        ("unicode_flag", "SELECT SF_GET_UNICODE_FLAG() FROM DUAL") })
    {
        try
        {
            await using var connection = new DmConnection(builder.ConnectionString); await connection.OpenAsync();
            Require(await Scalar(connection, "SELECT USER FROM DUAL") == user);
            Require(await Scalar(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL") == user);
            await using var command = new DmCommand(sql, connection) { CommandTimeout = 20 };
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
            while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
            metadata[name] = rows;
        }
        catch (Exception error) { metadata[name] = new { unavailable = true, failure = JsonDocument.Parse(DmDiagnostics.FormatException(error)).RootElement }; }
    }
    report["metadata"] = metadata;
    builder.TransportSecurity = DmTransportSecurity.RequireTls;
    bool rejected = false;
    try { await using var strict = new DmConnection(builder.ConnectionString); await strict.OpenAsync(); }
    catch (Exception error)
    {
        report["require_tls_failure"] = JsonDocument.Parse(DmDiagnostics.FormatException(error)).RootElement;
        rejected = error is NotSupportedException && error.Message.StartsWith("The server did not negotiate TLS transport.", StringComparison.Ordinal);
    }
    report["require_tls_rejected"] = rejected;
    Require(rejected); report["accepted"] = true; exit = 0;
}
catch (Exception error) { report["accepted"] = false; report["failure"] = JsonDocument.Parse(DmDiagnostics.FormatException(error)).RootElement; }
report["exit_code"] = exit;
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length == 1) await File.WriteAllTextAsync(args[0], json);
Console.WriteLine(json); return exit;

static void Require(bool ok) { if (!ok) throw new InvalidOperationException("profile_contract_failed"); }
static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
static object Property(object value, string name) => value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(value)!;
static async Task<string> Scalar(DmConnection connection, string sql)
{ await using var command = new DmCommand(sql, connection) { CommandTimeout = 20 }; return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!; }
