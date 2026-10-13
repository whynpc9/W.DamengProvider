using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using W.Dm;
using W.Dm.Config;

// A single-package, read-only observation of the TP03-v5 physical profile.
internal static class Program
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string User = "WDM_PROVIDER_TEST";
    const string PackageId = "W.DamengProvider", Version = "0.1.0-tp03.20261008053249";
    const string Asset = "lib/net10.0/W.DamengProvider.dll";
    const string PackageSha = "502f16248df703dc01060e50e3791fb28cda6f66935ad42e944045bb16b3070a";
    const string DllSha = "dcc88da8e7a9ccb2fb926682d2d7aefb991acba2cbfbbb02866a0d8f2a388e33";
    const string Mvid = "2fa28391-1964-489b-ae51-de9759aa6662";
    const string SourceHead = "fe89bd44abe99c1608499f71f4634a13e732c826";
    const string SourceSha = "04eec93c259fa24a1afe06bdfc5350b80b086fcc9557bac4e2bfd8490051af6c";
    const string ArchiveSha = "c722428a7b6abd1c126aa25c5d86f32703c861ffcb0161f241aa42e77fa7995a";
    static readonly Dictionary<string, object?> Report = new()
    {
        ["schema_version"] = 1, ["scope"] = "TP03_physical_profile_diagnostic",
        ["diagnostic_only"] = true, ["profile_acceptance"] = false,
        ["candidate_acceptance"] = false, ["tp03_wiring_accepted"] = false,
        ["full_temporal_acceptance"] = false, ["server_identity_verified"] = false,
        ["database_objects_created"] = false, ["connection_disposed"] = false
    };

    sealed class DiagnosticFailure(string code) : Exception
    {
        internal string Code { get; } = code;
    }
    static void Need(bool condition, string code)
    {
        if (!condition) throw new DiagnosticFailure(code);
    }
    static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    static JsonElement Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Unique(document.RootElement);
        return document.RootElement.Clone();
    }
    static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject()) { Need(names.Add(field.Name), "duplicate_json_key"); Unique(field.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    static void Match(JsonElement value, string name, string expected) => Need(value.GetProperty(name).GetString() == expected, "identity_binding");

    public static int Main(string[] args)
    {
        // identities, source manifest, source archive, feed, new output JSON.
        if (args.Length != 5) { Console.WriteLine("{\"status\":\"diagnostic_usage_failed\"}"); return 2; }
        FileStream output;
        try { output = new FileStream(args[4], FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        catch { Console.WriteLine("{\"status\":\"diagnostic_output_create_failed\"}"); return 2; }
        using (output)
        {
            int exit;
            try
            {
                CheckArtifacts(args);
                var builder = new DmConnectionStringBuilder(Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING") ?? "");
                Need(builder.User.Equals(User, StringComparison.OrdinalIgnoreCase), "test_identity_before_auth");
                builder.TransportSecurity = DmTransportSecurity.PlaintextAllowed;
                builder.Schema = User; builder.PersistSecurityInfo = false;
                builder.ConnPooling = false; builder.StmtPooling = false; builder.PreparePooling = false;
                builder.LogLevel = LogLevel.OFF; builder.ConnectTimeout = TimeSpan.FromSeconds(15); builder.CommandTimeout = 12;
                Report["transport"] = "PlaintextAllowed_explicit";
                Report["pooling"] = false;
                var connection = new DmConnection(builder.ConnectionString);
                try
                {
                    connection.Open();
                    // These are the only two SQL statements issued by this program.
                    Need(ReadIdentity(connection, "SELECT USER FROM DUAL") == User, "actual_test_user");
                    Need(ReadIdentity(connection, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL") == User, "actual_test_schema");
                    Report["server_identity_verified"] = true;
                    Report["server_user"] = User; Report["server_schema"] = User;
                    Report["public_server_version"] = SafeVersion(connection.ServerVersion);
                    var logical = Property(connection, "ConnProperty");
                    var getInstance = connection.GetType().GetMethod("GetConnInstance", Flags, null, Type.EmptyTypes, null)
                        ?? throw new DiagnosticFailure("physical_connection_method_missing");
                    var physical = Property(getInstance.Invoke(connection, null) ?? throw new DiagnosticFailure("physical_connection_missing"), "ConnProperty");
                    Report["logical"] = ObserveProperty(logical);
                    Report["physical"] = ObserveProperty(physical);
                    ObserveAdapter(connection);
                }
                finally { connection.Dispose(); Report["connection_disposed"] = true; }
                Report["status"] = "diagnostic_complete";
                exit = 0;
            }
            catch (Exception error)
            {
                Report["status"] = "diagnostic_failed";
                Report["failure"] = SafeError(error);
                exit = 1;
            }
            try { JsonSerializer.Serialize(output, Report, new JsonSerializerOptions { WriteIndented = true }); output.Flush(); }
            catch { Console.WriteLine("{\"status\":\"diagnostic_report_write_failed\"}"); return 2; }
            return exit;
        }
    }

    static void CheckArtifacts(string[] args)
    {
        var identity = Load(args[0]);
        Need(identity.GetProperty("schema_version").GetInt32() == 1, "identity_schema");
        Match(identity, "phase", "candidate"); Match(identity, "comparison_head", SourceHead);
        var w = identity.GetProperty("W");
        foreach (var pair in new[] { ("id", PackageId), ("version", Version), ("asset", Asset), ("package_sha256", PackageSha), ("dll_sha256", DllSha), ("mvid", Mvid) }) Match(w, pair.Item1, pair.Item2);
        var producer = identity.GetProperty("producer");
        foreach (var pair in new[] { ("source_head", SourceHead), ("source_manifest_sha256", SourceSha), ("source_archive_sha256", ArchiveSha),
            ("version", Version), ("package_sha256", PackageSha), ("dll_sha256", DllSha), ("mvid", Mvid) }) Match(producer, pair.Item1, pair.Item2);
        Need(Hash(File.ReadAllBytes(args[1])) == SourceSha && Hash(File.ReadAllBytes(args[2])) == ArchiveSha, "actual_source_bytes");
        var source = Load(args[1]);
        Need(source.GetProperty("schema_version").GetInt32() == 1, "source_schema"); Match(source, "head", SourceHead);
        var candidates = Directory.EnumerateFiles(args[3], "*.nupkg").Where(p => Path.GetFileName(p).Equals(PackageId + "." + Version + ".nupkg", StringComparison.OrdinalIgnoreCase)).ToArray();
        Need(candidates.Length == 1, "exact_package_file");
        var bytes = File.ReadAllBytes(candidates[0]); Need(Hash(bytes) == PackageSha, "actual_package_sha");
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            var assets = archive.Entries.Where(e => e.FullName == Asset).ToArray(); Need(assets.Length == 1, "exact_package_asset");
            using var content = new MemoryStream(); using (var entry = assets[0].Open()) entry.CopyTo(content);
            var dll = content.ToArray(); Need(Hash(dll) == DllSha, "package_dll_sha");
            using var pe = new PEReader(new MemoryStream(dll)); var metadata = pe.GetMetadataReader();
            Need(metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D") == Mvid, "package_dll_mvid");
            var specs = archive.Entries.Where(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal) && !e.FullName.Contains('/')).ToArray();
            Need(specs.Length == 1, "exact_nuspec"); using var xmlInput = specs[0].Open();
            var xml = XDocument.Load(xmlInput); var packageMetadata = xml.Root?.Elements().Single(e => e.Name.LocalName == "metadata") ?? throw new DiagnosticFailure("package_metadata_missing");
            Need(packageMetadata.Elements().Single(e => e.Name.LocalName == "id").Value == PackageId
                && packageMetadata.Elements().Single(e => e.Name.LocalName == "version").Value == Version, "package_id_version");
        }
        var assembly = typeof(DmConnection).Assembly;
        Need(Hash(File.ReadAllBytes(assembly.Location)) == DllSha && assembly.ManifestModule.ModuleVersionId.ToString("D") == Mvid, "actual_loaded_dll_identity");
        var depsPath = Path.ChangeExtension(typeof(Program).Assembly.Location, ".deps.json"); var deps = Load(depsPath);
        string key = PackageId + "/" + Version;
        Match(deps.GetProperty("libraries").GetProperty(key), "type", "package");
        Need(deps.GetProperty("targets").EnumerateObject().Any(t => t.Value.TryGetProperty(key, out var library)
            && library.TryGetProperty("runtime", out var runtime) && runtime.TryGetProperty(Asset, out _)), "actual_package_reference_asset");
        Report["source_identity"] = new { source_head = SourceHead, source_manifest_sha256 = SourceSha, source_archive_sha256 = ArchiveSha, identities_sha256 = Hash(File.ReadAllBytes(args[0])) };
        Report["package_identity"] = new { id = PackageId, version = Version, asset = Asset, package_sha256 = PackageSha, dll_sha256 = DllSha, mvid = Mvid, package_reference_verified = true, actual_asset_verified = true, deps_sha256 = Hash(File.ReadAllBytes(depsPath)) };
    }

    static string? ReadIdentity(DmConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.CommandTimeout = 12;
        return command.ExecuteScalar() is string value ? value.Trim() : null;
    }
    static object Property(object owner, string name) => owner.GetType().GetProperty(name, Flags)?.GetValue(owner)
        ?? throw new DiagnosticFailure("required_property_missing");
    static object? Field(object owner, string name) => (owner.GetType().GetField(name, Flags)
        ?? throw new DiagnosticFailure("required_field_missing")).GetValue(owner);
    static string? SafeVersion(object? value)
    {
        if (value is null) return null;
        Need(value is string, "unexpected_version_type"); var text = (string)value;
        Need(text.Length == 0 || Regex.IsMatch(text, "^[0-9]{1,4}(\\.[0-9]{1,5}){1,4}$", RegexOptions.CultureInvariant), "unexpected_version_value");
        return text;
    }
    static string? SafeEncoding(object? value)
    {
        if (value is null) return null;
        Need(value is string, "unexpected_encoding_type"); var text = (string)value;
        Need(text is "gb18030" or "GB18030" or "utf-8" or "UTF-8" or "big5" or "BIG5" or "default", "unexpected_encoding_value");
        return text;
    }
    static int SafeNumber(object? value, int min, int max)
    {
        Need(value is int or short, "unexpected_numeric_type"); int number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        Need(number >= min && number <= max, "unexpected_numeric_value"); return number;
    }
    static object ObserveProperty(object property)
    {
        var values = Field(property, "property") as IDictionary ?? throw new DiagnosticFailure("encoding_override_dictionary_missing");
        Need(values.Contains("encoding") && values["encoding"] is string, "encoding_override_missing");
        bool empty = ((string)values["encoding"]!).Length == 0;
        // No dictionary enumeration or arbitrary value formatting.
        var version = property.GetType().GetProperty("ServerVersion", Flags) ?? throw new DiagnosticFailure("server_version_property_missing");
        return new { server_version = SafeVersion(version.GetValue(property)), message_version = SafeNumber(Field(property, "msgVersion"), 0, 65535),
            effective_server_encoding = SafeEncoding(Property(property, "ServerEncoding")), negotiated_server_encoding = SafeEncoding(Field(property, "serverEncoding")),
            encoding_override_empty = empty, timezone = SafeNumber(Property(property, "TimeZone"), -1440, 1440) };
    }
    static void ObserveAdapter(DmConnection connection)
    {
        var assembly = typeof(DmConnection).Assembly;
        var adapter = assembly.GetType("W.Dm.Internal.Types.DmTemporalLegacyAdapter", true)!;
        var method = adapter.GetMethod("Profile", Flags, null, new[] { typeof(DmConnection) }, null) ?? throw new DiagnosticFailure("adapter_profile_method_missing");
        var profile = method.Invoke(null, new object[] { connection }) ?? throw new DiagnosticFailure("adapter_profile_missing");
        var identity = Property(profile, "Identity");
        string state = EnumName(Property(profile, "State"), "CompatibilityLegacyUnverified", "VerifiedSix", "VerifiedSeven");
        var evidence = Property(profile, "EvidenceId") as string;
        Need(evidence is "TP01-v10-shared-57b662eed842779a" or "TP01-v10-tls-d64fe3717e812c8d" or "CompatibilityLegacyUnverified", "unexpected_evidence_id");
        Report["adapter_profile"] = new { identity = new { server_version = SafeVersion(Property(identity, "ServerVersion")),
            message_version = SafeNumber(Property(identity, "MessageVersion"), 0, 65535), server_encoding = SafeEncoding(Property(identity, "ServerEncoding")) }, state, evidence_id = evidence };
        var targetType = assembly.GetType("W.Dm.Internal.Types.DmTemporalWriteTarget", true)!;
        var layoutType = assembly.GetType("W.Dm.Internal.Types.DmTemporalWireLayout", true)!;
        var formatType = assembly.GetType("W.Dm.Internal.Types.DmTemporalResultFormat", true)!;
        var constructor = targetType.GetConstructor(Flags, null, new[] { typeof(int), layoutType, formatType, typeof(int), typeof(int), typeof(int?), typeof(bool), typeof(int) }, null)
            ?? throw new DiagnosticFailure("write_target_constructor_missing");
        object target = constructor.Invoke(new object?[] { 26, Enum.Parse(layoutType, "Timestamp9"), Enum.Parse(formatType, "OrdinaryPacked"), 7, 7, (int?)7, true, 0 });
        var validate = profile.GetType().GetMethod("ValidateWriteTarget", Flags, null, new[] { targetType }, null) ?? throw new DiagnosticFailure("write_target_method_missing");
        try { validate.Invoke(profile, new[] { target }); Report["synthetic_target_result"] = new { result = "allowed", synthetic = true, database_write_attempted = false }; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            Report["synthetic_target_result"] = new { result = "rejected", synthetic = true, database_write_attempted = false, error = SafeError(error.InnerException) };
        }
    }
    static string EnumName(object value, params string[] allowed)
    {
        var name = Enum.GetName(value.GetType(), value); Need(name is not null && allowed.Contains(name), "unexpected_profile_state"); return name!;
    }
    static string SafeName(string? name) => name is not null && Regex.IsMatch(name, "^[A-Za-z0-9_.+`<>]{1,180}$", RegexOptions.CultureInvariant) ? name : "unexpected_identifier";
    static object SafeError(Exception error)
    {
        while (error is TargetInvocationException { InnerException: not null } invocation) error = invocation.InnerException;
        return new { exception_type = SafeName(error.GetType().FullName), code = error is DiagnosticFailure failure ? failure.Code : "unexpected_exception",
            stack = new StackTrace(error, false).GetFrames()?.Take(12).Select(frame => new { declaring_class = SafeName(frame.GetMethod()?.DeclaringType?.FullName), method = SafeName(frame.GetMethod()?.Name) }).ToArray() };
    }
}
