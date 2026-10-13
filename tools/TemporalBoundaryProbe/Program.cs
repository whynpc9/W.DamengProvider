using System.Data;
using System.Data.Common;
using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Config;

// Only synthetic temporal fields and allowlisted diagnostics reach the report.
// Never serialize a driver object, exception message, connection string or SQL.
internal static partial class Program
{
    const string User = "WDM_PROVIDER_TEST";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly Dictionary<string, object?> Report = new();
    static readonly List<Dictionary<string, object?>> Cases = [];
    static JsonElement Identity;
    static string Profile = "", Repo = "", Output = "", OwnershipPath = "", Table = "", Stage = "configuration", Raw = "", WSettings = "", OSettings = "";
    static bool Reserved;
    static int NextId = 1;
    static readonly HashSet<string> OwnedColumns = new(StringComparer.Ordinal) { "D6", "Z6" };
    static bool SevenClosedTls => Profile == "tls" && (Capabilities is null || !Capabilities.Value.GetProperty("profiles").GetProperty("tls").GetProperty("seven_write").GetBoolean());
    static readonly DateTime Ordinary = new(2026, 7, 23, 14, 15, 16, DateTimeKind.Unspecified);
    sealed record Ownership(int SchemaVersion, string Nonce, string Table, string Profile, string ServerVersion,
        string OwnerPackageSha256, string IdentitySha256, bool AbsentBeforeCreate, bool IdentityVerified, string OwnerMarker, string CreationState);
    sealed record ErrorInfo(string? Type, int? Number, string FailureClass, bool VerifiedServerResponse,
        string? FailureKind, string? Phase, string? Code, string? Outcome);
    sealed class ProbeInfrastructureException(Exception inner) : Exception("probe_infrastructure_failure", inner) { }
    static Ownership? CurrentOwnership;
    static JsonElement? Capabilities;
    static readonly ConditionalWeakTable<DbConnection, ConnectionProfile> VerifiedProfiles = new();
    sealed record Metadata(bool Available, int? CType, int? Width, int? SemanticScale, int? Mask, int? TypeFlag,
        int? RawScale, string RawScaleStatus, string? RawSource, int? RawCType, int? RawPrecision,
        int? RawMaskBefore, int? RawDerivedMask, string ResultFormat, bool? IsBdta, string MetadataRole);
    sealed record ValueObservation(string Outcome, string? Type, long? Ticks, int? OffsetMinutes, string? Kind, string? Text, ErrorInfo? Error, object? KnownNullReceipt = null, object? NullContext = null);

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "offline") return RunOfflineContracts(args);
        // args: run|cleanup identities packages profile core|metadata|extended repo ownership report
        int exit = 1;
        try
        {
            Need(args.Length == 9 && args[0] is "run" or "cleanup" or "diagnose-components", "usage");
            Identity = LoadStrict(args[1]);
            Profile = args[3]; Repo = args[5]; OwnershipPath = args[6]; Output = args[7];
            // Ninth argument pins the frozen source/tool manifest supplied by runner.
            Need(Profile is "shared" or "tls" && args[4] is "core" or "metadata" or "extended", "configuration");
            Report["schema_version"] = 1; Report["task"] = "TP-01"; Report["runtime"] = Environment.Version.ToString(); Report["mode"] = args[0]; Report["phase"] = Identity.GetProperty("phase").GetString();
            Report["profile"] = Profile; Report["lane"] = args[4]; Report["cases"] = Cases;
            Report["identity_sha256"] = Hash(File.ReadAllBytes(args[1]));
            Report["source_manifest_sha256"] = Hash(File.ReadAllBytes(args[8]));
            Report["product_source_manifest_sha256"] = Identity.GetProperty("producer").GetProperty("source_manifest_sha256").GetString();
            Report["producer_source_head"] = Identity.GetProperty("producer").GetProperty("source_head").GetString();
            Report["comparison_head"] = Identity.GetProperty("comparison_head").GetString();
            Report["identities"] = CheckIdentities(args[2]);
            CheckSourceBinding(args[8]);
            Stage = "configuration"; Configure();
            Stage = "profile_preflight";
            using (var w = Open("W")) { Report["W_profile"] = ProfileInfo(w); }
            if (Profile == "shared" && (args[0] is "run" or "diagnose-components")) using (var o = Open("O")) { Report["O_profile"] = ProfileInfo(o); }
            else Report["O_profile"] = new { status = Profile == "tls" ? "closed" : "not_used", reason = Profile == "tls" ? "no_verified_strict_official_tls_configuration" : "cleanup_requires_verified_W_only" };
            Report["server_identity_verified"] = true;
            if (args[0] == "diagnose-components")
            {
                Need(Profile == "shared", "component_diagnostic_shared_only");
                RunComponentDiagnostic(); exit = 0;
            }
            else if (args[0] == "cleanup")
            {
                LoadOwnership(); Cleanup(); exit = Report.GetValueOrDefault("cleanup_verified") is true ? 0 : 1;
            }
            else
            {
                Reserve();
                try
                {
                    if (args[4] == "core") Core();
                    else if (args[4] == "metadata") Parameters();
                    else Extended();
                    Report["status"] = "characterization_complete"; exit = 0;
                }
                finally { Cleanup(); }
                if (Report.GetValueOrDefault("cleanup_verified") is not true) exit = 1;
            }
        }
        catch (Exception e)
        {
            Report["status"] = Raw.Length == 0 ? "integration_pending_or_configuration_failed" : "execution_failed";
            Report["failure_stage"] = Stage; Report["failure"] = Safe(e);
            if (Reserved && Report.GetValueOrDefault("cleanup_verified") is not true) Cleanup();
        }
        Report["process_exit_code"] = exit;
        try { Write(Output, Report); }
        catch { Console.WriteLine("{\"status\":\"safe_report_write_failed\"}"); return 2; }
        return exit;
    }

    static object CheckIdentities(string packages)
    {
        var result = new Dictionary<string, object>();
        foreach (string lane in new[] { "O", "W" })
        {
            var expected = Identity.GetProperty(lane);
            string id = expected.GetProperty("id").GetString()!, version = expected.GetProperty("version").GetString()!, asset = expected.GetProperty("asset").GetString()!;
            string package = Directory.EnumerateFiles(packages, "*.nupkg").Single(p => Path.GetFileName(p).Equals(id + "." + version + ".nupkg", StringComparison.OrdinalIgnoreCase));
            var assembly = lane == "W" ? typeof(DmConnection).Assembly : typeof(global::Dm.DmConnection).Assembly;
            string dllHash = Hash(File.ReadAllBytes(assembly.Location)), packageHash = Hash(File.ReadAllBytes(package));
            using var zip = ZipFile.OpenRead(package);
            using (var nuspec = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open())
            {
                var xml = XDocument.Load(nuspec); var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
                Need(metadata.Elements().Single(e => e.Name.LocalName == "id").Value.Equals(id, StringComparison.OrdinalIgnoreCase)
                    && metadata.Elements().Single(e => e.Name.LocalName == "version").Value == version, "actual_package_id_version");
            }
            using var source = (zip.GetEntry(asset) ?? throw new InvalidOperationException()).Open();
            using var content = new MemoryStream(); source.CopyTo(content);
            Need(packageHash == expected.GetProperty("package_sha256").GetString()
                && dllHash == expected.GetProperty("dll_sha256").GetString() && Hash(content.ToArray()) == dllHash
                && assembly.ManifestModule.ModuleVersionId.ToString("D") == expected.GetProperty("mvid").GetString(), "package_identity");
            var deps = LoadStrict(Path.ChangeExtension(typeof(Program).Assembly.Location, ".deps.json"));
            Need(deps.GetProperty("libraries").GetProperty(id + "/" + version).GetProperty("type").GetString() == "package", "package_reference");
            bool selected = deps.GetProperty("targets").EnumerateObject().Any(t => t.Value.TryGetProperty(id + "/" + version, out var lib)
                && lib.TryGetProperty("runtime", out var runtime) && runtime.TryGetProperty(asset, out _));
            Need(selected, "actual_asset_selection");
            result[lane] = new { id, version, asset, package_sha256 = packageHash, dll_sha256 = dllHash,
                mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"), package_reference_verified = true, actual_asset_verified = true };
        }
        return result;
    }

    static void CheckSourceBinding(string frozenPath)
    {
        var frozen = LoadStrict(frozenPath); var producer = Identity.GetProperty("producer");
        Need(Identity.GetProperty("schema_version").GetInt32() == 1 && frozen.GetProperty("schema_version").GetInt32() == 1, "source_schema");
        Need(Identity.GetProperty("phase").GetString() is "baseline" or "candidate", "source_phase");
        Need(frozen.GetProperty("phase").GetString() == Identity.GetProperty("phase").GetString(), "source_phase_binding");
        string productPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(frozenPath))!, "product-source-manifest.json");
        string sha = Hash(File.ReadAllBytes(productPath)); var product = LoadStrict(productPath);
        Need(sha == producer.GetProperty("source_manifest_sha256").GetString() && sha == frozen.GetProperty("product_source_manifest_sha256").GetString(), "product_source_bytes_binding");
        string? head = product.TryGetProperty("head", out var h) ? h.GetString() : product.TryGetProperty("source_head", out h) ? h.GetString() : product.GetProperty("source_commit").GetString();
        Need(head == producer.GetProperty("source_head").GetString() && head == frozen.GetProperty("source_commit").GetString(), "producer_head_binding");
        Need(frozen.GetProperty("identity_sha256").GetString() == (string)Report["identity_sha256"]!, "frozen_identity_bytes");
        var archive = producer.GetProperty("source_archive_sha256");
        Need(archive.ValueKind == frozen.GetProperty("source_archive_sha256").ValueKind, "source_archive_binding");
        if (archive.ValueKind != JsonValueKind.Null)
        {
            Need(archive.GetString() == frozen.GetProperty("source_archive_sha256").GetString()
                && archive.GetString() == Hash(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(frozenPath))!, "product-source.tar"))), "source_archive_bytes");
        }
        Need(product.GetProperty("schema_version").GetInt32() == 1 && product.GetProperty("files").ValueKind == JsonValueKind.Array && product.GetProperty("files").GetArrayLength() > 0, "producer_source_files");
        var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in product.GetProperty("files").EnumerateArray())
        {
            string? path = file.GetProperty("path").GetString(), fileHash = file.GetProperty("sha256").GetString();
            Need(path is not null && !Path.IsPathRooted(path) && !path.Split('/').Any(p => p is ".." or ".local" or "secrets")
                && !path.Contains('\\') && sourcePaths.Add(path) && fileHash is not null && Regex.IsMatch(fileHash, "^[a-f0-9]{64}$"), "producer_file_entry");
            foreach (string size in new[] { "size", "bytes" }) if (file.TryGetProperty(size, out var n)) Need(n.GetInt64() >= 0, "producer_file_size");
        }
        Need(sourcePaths.Any(p => p.StartsWith("src/W.DamengProvider/", StringComparison.Ordinal)), "producer_product_source_files");
        foreach (string key in new[] { "version", "package_sha256", "dll_sha256", "mvid" }) Need(producer.GetProperty(key).GetString() == Identity.GetProperty("W").GetProperty(key).GetString(), "producer_package_binding");
        if (Identity.GetProperty("phase").GetString() == "candidate")
        {
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(frozenPath))!, "capabilities.json");
            Need(Hash(File.ReadAllBytes(path)) == frozen.GetProperty("capabilities_sha256").GetString(), "capability_bytes_binding");
            var cap = LoadStrict(path); var selected = cap.GetProperty("profiles").GetProperty(Profile);
            Need(cap.GetProperty("schema_version").GetInt32() == 1 && cap.GetProperty("source_manifest_sha256").GetString() == sha
                && cap.GetProperty("source_commit").GetString() == head && cap.GetProperty("w_package_sha256").GetString() == producer.GetProperty("package_sha256").GetString()
                && selected.GetProperty("server_version").GetString() == ExpectedVersion && selected.GetProperty("root_approved").GetBoolean(), "capability_producer_binding");
            Need(!selected.GetProperty("tltz_read").GetBoolean() && !selected.GetProperty("tltz_write").GetBoolean(), "tltz_independent_closed_gate");
            bool seven = selected.GetProperty("seven_write").GetBoolean(); var scales = new HashSet<int>();
            foreach (var scale in selected.GetProperty("write_scales").EnumerateArray()) { int n = scale.GetInt32(); Need(n is >= 0 and <= 9 && scales.Add(n), "capability_scales"); }
            Need(scales.Contains(6) && scales.Contains(7) == seven && (Profile != "shared" || seven), "capability_seven");
            foreach (string key in new[] { "local_ticks_min", "local_ticks_max", "utc_ticks_min", "utc_ticks_max" })
            { long ticks = selected.GetProperty(key).GetInt64(); Need(ticks >= 0 && ticks <= DateTime.MaxValue.Ticks, "capability_tick_domain"); }
            foreach (string key in new[] { "offset_min_minutes", "offset_max_minutes" })
            { int minutes = selected.GetProperty(key).GetInt32(); Need(minutes is >= -840 and <= 840, "capability_offset_domain"); }
            if (selected.TryGetProperty("verified_unsupported_error_numbers", out var numbers)) foreach (var item in numbers.EnumerateObject())
            { var seen = new HashSet<int>(); foreach (var n in item.Value.EnumerateArray()) Need(seen.Add(n.GetInt32()), "capability_error_numbers"); }
            Need(selected.GetProperty("local_ticks_min").GetInt64() <= selected.GetProperty("local_ticks_max").GetInt64()
                && selected.GetProperty("utc_ticks_min").GetInt64() <= selected.GetProperty("utc_ticks_max").GetInt64()
                && selected.GetProperty("offset_min_minutes").GetInt32() <= selected.GetProperty("offset_max_minutes").GetInt32(), "capability_domain_order");
            Capabilities = cap;
        }
    }

    static void Configure()
    {
        Raw = Environment.GetEnvironmentVariable(Profile == "tls" ? "DAMENG_TLS_TEST_CONNECTION_STRING" : "DAMENG_TEST_CONNECTION_STRING") ?? "";
        Need(!string.IsNullOrWhiteSpace(Raw), "integration_pending");
        var b = new DmConnectionStringBuilder(Raw);
        Need(b.User.Equals(User, StringComparison.OrdinalIgnoreCase), "test_user_before_auth");
        b.TransportSecurity = Profile == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed;
        b.Schema = User; b.PersistSecurityInfo = false; b.ConnPooling = false; b.StmtPooling = false; b.PreparePooling = false;
        b.LogLevel = LogLevel.OFF; b.ConnectTimeout = TimeSpan.FromSeconds(15); b.CommandTimeout = 12;
        if (Profile == "tls")
        {
            Need(b.Server == "127.0.0.1" && b.Port == 15236, "isolated_tls_target");
            string certs = Path.Combine(Repo, ".local/t07/certs/client_ssl", User);
            b.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem"); b.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
            b.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem"); b.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
        }
        WSettings = b.ConnectionString;
        if (Profile == "shared")
        {
            var ob = new global::Dm.DmConnectionStringBuilder(Raw) { Schema = User,
                LogLevel = global::Dm.Config.LogLevel.OFF, ConnPooling = false, StmtPooling = false,
                PreparePooling = false, ConnectionTimeout = 15000, CommandTimeout = 12, SocketTimeout = 15000 };
            Need(ob.User.Equals(User, StringComparison.OrdinalIgnoreCase), "official_test_user_before_auth");
            OSettings = ob.ConnectionString;
        }
        Report["transport"] = Profile == "tls" ? "RequireTls_local_CA_explicit_NoCheck" : "PlaintextAllowed_explicit";
        Report["nondefault_timezone_bdta_interval_time_modes"] = "not_enabled";
    }

    static DbConnection Open(string lane)
    {
        Need(lane == "W" || Profile == "shared", "official_tls_closed");
        DbConnection c = lane == "W" ? new DmConnection(WSettings) : new global::Dm.DmConnection(OSettings);
        try
        {
            c.Open();
            string user = Convert.ToString(Scalar(c, "SELECT USER FROM DUAL"), CultureInfo.InvariantCulture)?.Trim() ?? "";
            string schema = Convert.ToString(Scalar(c, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"), CultureInfo.InvariantCulture)?.Trim() ?? "";
            Need(user == User, "test_user"); Need(schema == User, "test_schema");
            var info = ProfileInfo(c, user, schema);
            Need(info.ServerVersion == ExpectedVersion, "server_version");
            return c;
        }
        catch (Exception e) { c.Dispose(); throw new ProbeInfrastructureException(e); }
    }
    static string ExpectedVersion => Profile == "tls" ? "8.1.4.6" : "8.1.5.60";
    sealed record ConnectionProfile(string ServerVersion, string Encoding, int Timezone, int MessageVersion, string ResultFormat, string ServerUser, string ServerSchema,
        string NegotiatedEncoding, string NegotiatedEncodingOrigin, bool EncodingOverrideEmpty, string ProfileOrigin);
    static ConnectionProfile ProfileInfo(DbConnection c, string? verifiedUser = null, string? verifiedSchema = null)
    {
        if (VerifiedProfiles.TryGetValue(c, out var existing)) return existing;
        string user = verifiedUser ?? Convert.ToString(Scalar(c, "SELECT USER FROM DUAL"), CultureInfo.InvariantCulture)?.Trim() ?? "";
        string schema = verifiedSchema ?? Convert.ToString(Scalar(c, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"), CultureInfo.InvariantCulture)?.Trim() ?? "";
        var profile = CapturePhysicalProfile(c, user, schema);
        VerifiedProfiles.Add(c, profile); return profile;
    }

    // Live O/W and offline fake connections use this exact capture path. Logical ConnProperty is never read.
    static ConnectionProfile CapturePhysicalProfile(object connection, string user, string schema)
    {
        Need(user == User && schema == User, "actual_profile_identity");
        var getInstance = connection.GetType().GetMethod("GetConnInstance", Flags, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException("physical_profile_method_missing");
        object instance = getInstance.Invoke(connection, null) ?? throw new InvalidOperationException("physical_profile_instance_missing");
        object props = ProfileProperty(instance, "ConnProperty");
        string version = ProfileVersion(ProfileProperty(props, "ServerVersion"));
        Need(ProfileVersion(ProfileProperty(connection, "ServerVersion")) == version, "physical_public_version_conflict");
        int message = ProfileNumber(ProfileField(props, "msgVersion"), 0, 65535);
        int timezone = ProfileNumber(ProfileProperty(props, "TimeZone"), -1440, 1440);
        var options = ProfileField(props, "property") as IDictionary ?? throw new InvalidOperationException("physical_encoding_override_unobservable");
        Need(options.Contains("encoding") && options["encoding"] is string, "physical_encoding_override_unobservable");
        bool overrideEmpty = ((string)options["encoding"]!).Length == 0;
        Need(overrideEmpty, "physical_encoding_override_closed");
        string effective = ProfileEncoding(ProfileProperty(props, "ServerEncoding"));
        string negotiated = ProfileEncoding(ProfileField(props, "serverEncoding"));
        Need(effective == negotiated, "physical_encoding_conflict");
        return new(version, effective, timezone, message, "ordinary_non_BDTA", user, schema, negotiated,
            "physical_conn_property_serverEncoding_field", overrideEmpty, "physical_get_conn_instance_conn_property");
    }
    static object ProfileProperty(object owner, string name) => owner.GetType().GetProperty(name, Flags)?.GetValue(owner)
        ?? throw new InvalidOperationException("physical_profile_property_missing");
    static object ProfileField(object owner, string name) => owner.GetType().GetField(name, Flags)?.GetValue(owner)
        ?? throw new InvalidOperationException("physical_profile_field_missing");
    static string ProfileVersion(object value)
    {
        Need(value is string version && Regex.IsMatch(version, "\\A[0-9]{1,4}(\\.[0-9]{1,5}){1,4}\\z", RegexOptions.CultureInvariant), "physical_profile_version_invalid");
        return (string)value;
    }
    static string ProfileEncoding(object value)
    {
        Need(value is string, "physical_profile_encoding_invalid");
        string encoding = ((string)value).ToLowerInvariant();
        Need(encoding is "utf-8" or "gb18030" or "big5", "physical_profile_encoding_invalid");
        return encoding;
    }
    static int ProfileNumber(object value, int minimum, int maximum)
    {
        Need(value is int or short, "physical_profile_numeric_invalid");
        int number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        Need(number >= minimum && number <= maximum, "physical_profile_numeric_invalid"); return number;
    }

    static void Reserve()
    {
        Stage = "reserve_ownership";
        string nonce = Guid.NewGuid().ToString("N").ToUpperInvariant(); Table = "TPN_" + nonce[..24];
        var initial = new Ownership(1, nonce, Table, Profile, ExpectedVersion,
            Identity.GetProperty("W").GetProperty("package_sha256").GetString()!, (string)Report["identity_sha256"]!, true, true,
            "OWN_" + nonce[..24], "reserved");
        CreateWithLifecycle(initial, new LiveOwnershipDatabase(), ownership => {
            CurrentOwnership = ownership;
            if (!Reserved)
            {
                using var f = new FileStream(OwnershipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(f, ownership, Json); Reserved = true;
            }
            else Write(OwnershipPath, ownership);
            Report["owned_table"] = Table; Report["creation_state"] = ownership.CreationState;
        });
        Report["ddl_implicit_commit"] = "assumed_no_ddl_rollback_claim";
        foreach (var column in new[] { ("D7", "TIMESTAMP(7)"), ("Z7", "DATETIME(7) WITH TIME ZONE") })
            Attempt("server", "main_ddl_" + column.Item1, "server_ddl", () => {
                using var c = Open("W"); Exec(c, $"ALTER TABLE {Table} ADD {column.Item1} {column.Item2}"); OwnedColumns.Add(column.Item1);
                return new() { ["status"] = "server_column_created", ["column_scale"] = 7 };
            });
    }
    static void LoadOwnership()
    {
        var o = ParseOwnershipJson(File.ReadAllText(OwnershipPath));
        ValidateOwnership(o, Profile, ExpectedVersion, Identity.GetProperty("W").GetProperty("package_sha256").GetString()!, (string)Report["identity_sha256"]!);
        CurrentOwnership = o; Table = o.Table; Reserved = true; Report["owned_table"] = Table; Report["creation_state"] = o.CreationState;
    }
    static void Cleanup()
    {
        if (!Reserved) return;
        string previousStage = Stage; Stage = "cleanup";
        try
        {
            LoadOwnership();
            CleanupWithOwnership(CurrentOwnership!, new LiveOwnershipDatabase(), Profile, ExpectedVersion,
                Identity.GetProperty("W").GetProperty("package_sha256").GetString()!, (string)Report["identity_sha256"]!);
            Report["cleanup_verified"] = true; Report["final_database_state"] = "owned_table_absent_fresh_connection";
            Write(OwnershipPath + ".final.json", new { table = Table, profile = Profile, identity_verified = true, absent_verified = true });
        }
        catch (Exception e) { Report["cleanup_verified"] = false; Report["cleanup_failure"] = Safe(e); Report["final_database_state"] = "unverified_exact_ownership_manifest_retained"; }
        finally { Stage = previousStage; }
    }

    static void Core()
    {
        ColumnMetadata();
        var dates = new (string Id, DateTime Value)[] {
            ("min", DateTime.MinValue), ("min_plus1", DateTime.MinValue.AddTicks(1)),
            ("max", DateTime.MaxValue), ("max_minus1", DateTime.MaxValue.AddTicks(-1)), ("max_minus9", DateTime.MaxValue.AddTicks(-9)),
            ("ordinary_tick1", Ordinary.AddTicks(1)), ("ordinary_micro", Ordinary.AddTicks(1234560)),
            ("century_leap", new DateTime(2000,2,29,23,59,59).AddTicks(9999999)),
            ("century_nonleap", new DateTime(1900,3,1)), ("midnight", new DateTime(2026,7,24)) };
        dates = dates.Concat(Enumerable.Range(0, 10).Select(tick => ("tail_" + tick, Ordinary.AddTicks(tick)))).ToArray();
        foreach (string lane in DriverLanes)
        {
            foreach (var d in dates)
            {
                WriteCase(lane, "dt7_" + d.Id, "D7", DbType.DateTime2, d.Value, 7, true, false);
                WriteCase(lane, "dt6_" + d.Id, "D6", DbType.DateTime2, d.Value, 6, true, false);
                WriteCase(lane, "dto7_" + d.Id, "Z7", DbType.DateTimeOffset, new DateTimeOffset(d.Value, TimeSpan.Zero), 7, true, false);
                WriteCase(lane, "dto6_" + d.Id, "Z6", DbType.DateTimeOffset, new DateTimeOffset(d.Value, TimeSpan.Zero), 6, true, false);
            }
            foreach (var kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Utc, DateTimeKind.Local })
                WriteCase(lane, "kind_" + kind, "D7", DbType.DateTime2, DateTime.SpecifyKind(Ordinary.AddTicks(1), kind), 7, false, false);
            foreach (int minutes in new[] { 0, 480, -300, 330, -840, -780, -779, 840 })
            {
                string suffix = minutes.ToString(CultureInfo.InvariantCulture).Replace('-', 'N');
                WriteCase(lane, "offset_" + suffix, "Z7", DbType.DateTimeOffset, new DateTimeOffset(Ordinary.AddTicks(1), TimeSpan.FromMinutes(minutes)), 7, true, false);
                // Constructible local and UTC boundary vectors; invalid CLR constructors are recorded separately.
                foreach (var endpoint in new[] { ("min", DateTime.MinValue), ("max", DateTime.MaxValue) })
                {
                    DateTimeOffset value;
                    try { value = new DateTimeOffset(endpoint.Item2, TimeSpan.FromMinutes(minutes)); }
                    catch (ArgumentException e) { Add(lane, "constructor_" + endpoint.Item1 + "_" + suffix, "clr_precondition", new() { ["error"] = Safe(e) with { FailureClass = "clr_precondition" }, ["failure_class"] = "clr_precondition", ["status"] = "constructor_rejected_before_driver", ["requested_fields"] = new { local_ticks = endpoint.Item2.Ticks, utc_ticks = endpoint.Item2.Ticks - minutes * TimeSpan.TicksPerMinute, offset_minutes = minutes }, ["expected_constructor_rejection"] = true, ["execute_attempted"] = false }); value = default; }
                    if (!Cases.Any(c => (string)c["driver_lane"]! == lane && (string)c["case_id"]! == "constructor_" + endpoint.Item1 + "_" + suffix)) WriteCase(lane, "local_" + endpoint.Item1 + "_" + suffix, "Z7", DbType.DateTimeOffset, value, 7, true, false);
                    DateTimeOffset utcEndpoint = new DateTimeOffset(endpoint.Item2, TimeSpan.Zero);
                    try { value = utcEndpoint.ToOffset(TimeSpan.FromMinutes(minutes)); }
                    catch (ArgumentException e) { Add(lane, "utc_constructor_" + endpoint.Item1 + "_" + suffix, "clr_precondition", new() { ["error"] = Safe(e) with { FailureClass = "clr_precondition" }, ["failure_class"] = "clr_precondition", ["status"] = "constructor_rejected_before_driver", ["requested_fields"] = new { local_ticks = endpoint.Item2.Ticks + minutes * TimeSpan.TicksPerMinute, utc_ticks = endpoint.Item2.Ticks, offset_minutes = minutes }, ["expected_constructor_rejection"] = true, ["execute_attempted"] = false }); continue; }
                    WriteCase(lane, "utc_" + endpoint.Item1 + "_" + suffix, "Z7", DbType.DateTimeOffset, value, 7, true, false);
                }
            }
        }
        foreach (string lane in DriverLanes) Rollback(lane);
    }
    static IEnumerable<string> DriverLanes => Profile == "shared" ? new[] { "O", "W" } : new[] { "W" };

    static void ColumnMetadata()
    {
        foreach (string lane in DriverLanes)
        foreach (string column in new[] { "D6", "Z6", "D7", "Z7" }) Attempt(lane, "column_" + column, "column_metadata", () => {
            using var c = Open(lane); using var cmd = Command(c, $"SELECT {column} FROM {Table} WHERE 1=0"); using var r = cmd.ExecuteReader();
            return new() { ["metadata"] = Columns(r), ["status"] = "observed", ["schema_scales"] = SchemaScales(r) };
        });
        foreach (string lane in DriverLanes) Attempt(lane, "columns_6_7", "column_metadata", () => {
            using var c = Open(lane); using var cmd = Command(c, $"SELECT D6,Z6,D7,Z7 FROM {Table} WHERE 1=0"); using var r = cmd.ExecuteReader();
            return new() { ["metadata"] = Columns(r), ["status"] = "observed", ["schema_scales"] = SchemaScales(r) };
        });
    }

    static void WriteCase(string lane, string id, string column, DbType dbType, object value, int scale, bool prepare, bool explicitPrecision)
    {
        Attempt(lane, id, "typed_write", () => {
            if (lane == "W" && scale == 7 && SevenClosedTls && (column is "D7" or "Z7"))
            {
                using var c = Open("W"); bool tz = value is DateTimeOffset; using var cmd = Command(c, ParameterQuery(tz));
                var p = cmd.CreateParameter(); p.ParameterName = "v"; p.DbType = dbType; p.Scale = 7; cmd.Parameters.Add(p);
                var phase = ExecuteParameterPhase(lane, cmd, p, null, column, value, "initial", 7, null, true);
                var closed = new Dictionary<string, object?> { ["requested_db_type"] = dbType.ToString(), ["requested_scale"] = 7,
                    ["requested_precision"] = null, ["column_scale"] = null, ["target_column"] = null, ["column_metadata"] = Array.Empty<Metadata>(), ["write_path"] = "advisory_profile_request",
                    ["statement_shape"] = "advisory_select", ["prepared_requested"] = true, ["expected"] = Observe(() => value), ["row_id"] = null,
                    ["persisted_rows"] = 0L, ["persistence_source"] = "advisory_no_dml", ["phase_results"] = new[] { phase }, ["oracles"] = phase["oracles"],
                    ["execute_attempted"] = phase["execute_attempted"], ["status"] = phase["status"] is "observed" ? "written" : "driver_write_rejected" };
                if (phase.TryGetValue("error", out var error)) { closed["error"] = error; closed["failure_class"] = phase["failure_class"]; }
                return closed;
            }
            var result = new Dictionary<string, object?> { ["requested_db_type"] = dbType.ToString(), ["requested_scale"] = lane == "O" ? null : scale,
                ["requested_precision"] = lane == "O" ? scale : explicitPrecision ? scale : null,
                ["write_path"] = "fixed_column", ["target_column"] = column, ["column_scale"] = column[^1] - '0', ["column_metadata"] = ColumnDescriptor(lane, column), ["prepared_requested"] = prepare, ["expected"] = Observe(() => value) };
            int row = NextId++; result["row_id"] = row;
            using (var c = Open(lane))
            using (var cmd = Command(c, $"INSERT INTO {Table}(ID,{column}) VALUES({row},:v)"))
            {
                string phase = "parameter_configuration";
                try
                {
                    var p = cmd.CreateParameter(); p.ParameterName = "v"; p.DbType = dbType; p.Value = value;
                    if (lane == "O") p.Precision = checked((byte)scale);
                    else if (explicitPrecision) p.Precision = checked((byte)scale);
                    else p.Scale = checked((byte)scale);
                    cmd.Parameters.Add(p);
                    phase = "prepare"; if (prepare) cmd.Prepare(); result["parameter_metadata"] = ParameterMetadata(cmd);
                    result["execute_attempted"] = true; phase = "execute";
                    result["affected_rows"] = cmd.ExecuteNonQuery();
                    result["parameter_metadata_after_execute"] = ParameterMetadata(cmd); result["status"] = "written";
                }
                catch (Exception e) { CheckInfra(e); result["failure_class"] = Safe(e).FailureClass; result["error"] = Safe(e); result["failure_stage"] = phase; result["status"] = "driver_write_rejected"; }
            }
            // Fresh independent connection: failed sends must not secretly persist a row.
            using (var check = Open("W")) result["persisted_rows"] = Convert.ToInt64(Scalar(check, $"SELECT COUNT(*) FROM {Table} WHERE ID={row}"), CultureInfo.InvariantCulture);
            result["persistence_source"] = "fresh_connection_row_count";
            if (Convert.ToInt64(result["persisted_rows"], CultureInfo.InvariantCulture) == 1)
                result["oracles"] = DriverLanes.Select(reader => ReadOracle(reader, column, row, value)).ToArray();
            return result;
        });
    }

    static object ReadOracle(string lane, string column, int row, object expected)
    {
        var result = new Dictionary<string, object?> { ["reader_lane"] = lane, ["writer_value"] = Observe(() => expected) };
        try
        {
            using var c = Open(lane);
            string fmt = column.StartsWith('Z') ? "YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM" : "YYYY-MM-DD HH24:MI:SS.FF9";
            using var cmd = Command(c, $"SELECT {column},TO_CHAR({column},'{fmt}'),TO_CHAR({column},'YYYY-MM-DD HH24:MI:SS.FF7') FROM {Table} WHERE ID={row}");
            using var r = cmd.ExecuteReader(); Need(r.Read(), "oracle_row_missing");
            result["metadata"] = Columns(r).Take(1).ToArray(); result["schema_scales"] = SchemaScales(r).Take(1).ToArray(); result["cell"] = Cell(r, 0);
            // Capture the direct ordinary result cache before any temporal getter; Python owns independent decoding.
            result["raw_oracle"] = RawTemporalOracle(r, 0, Columns(r).FirstOrDefault());
            // Server strings are synthetic date values only; all strings must match a strict date allowlist.
            string ff9 = SafeTemporalText(r.GetString(1)), ff7 = SafeTemporalText(r.GetString(2));
            result["server_ff9"] = ff9; result["server_ff7"] = ff7;
            result["actual_server"] = ActualServer(ff9, Columns(r).FirstOrDefault()?.SemanticScale);
            DateTime expectedDate = expected is DateTimeOffset edto ? edto.DateTime : (DateTime)expected;
            result["server_ff7_exact"] = ff7.Trim() == ExpectedText(expectedDate, 7);
            result["server_exact"] = ff9.Trim() == ExpectedText(expected, 9);
            result["get_value"] = Observe(() => r.GetValue(0)); var observedString = Observe(() => r.GetString(0)); result["get_string"] = observedString;
            result["get_string_exact"] = observedString.Outcome == "returned" && observedString.Text is not null && ParseFields(ff9) is not null && ParseFields(observedString.Text) == ParseFields(ff9);
            result["typed"] = expected is DateTimeOffset ? Observe(() => Invoke(r, "GetDateTimeOffset", 0)) : Observe(() => r.GetDateTime(0));
            result["generic"] = expected is DateTimeOffset ? Observe(() => r.GetFieldValue<DateTimeOffset>(0)) : Observe(() => r.GetFieldValue<DateTime>(0));
            result["subsequent_read_completion"] = !r.Read(); result["status"] = "observed";
            // Components use a separate query/connection: an unsupported EXTRACT must not suppress typed reads.
            try
            {
                using var componentConnection = Open(lane);
                using var componentCommand = Command(componentConnection, $"SELECT EXTRACT(YEAR FROM {column}),EXTRACT(MONTH FROM {column}),EXTRACT(DAY FROM {column}),EXTRACT(HOUR FROM {column}),EXTRACT(MINUTE FROM {column}),EXTRACT(SECOND FROM {column}) FROM {Table} WHERE ID={row}");
                using var componentReader = componentCommand.ExecuteReader(); Need(componentReader.Read(), "component_row");
                decimal[] components = Enumerable.Range(0, 6).Select(i => Convert.ToDecimal(componentReader.GetValue(i), CultureInfo.InvariantCulture)).ToArray();
                static decimal[] Fields(DateTime dt) => [dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second + (dt.Ticks % TimeSpan.TicksPerSecond) / (decimal)TimeSpan.TicksPerSecond];
                bool localExact = components.SequenceEqual(Fields(expectedDate));
                bool utcExact = expected is DateTimeOffset dto && components.SequenceEqual(Fields(dto.UtcDateTime));
                result["server_components"] = components; result["server_components_match_local"] = localExact;
                result["server_components_match_utc"] = utcExact;
                result["server_component_basis"] = localExact && utcExact ? "local_and_utc_equal" : localExact ? "local" : utcExact ? "utc" : "neither";
                result["server_components_exact"] = localExact || utcExact;
            }
            catch (Exception e) { CheckInfra(e); result["server_components_exact"] = false; result["component_oracle_error"] = Safe(e); }
        }
        catch (Exception e) { CheckInfra(e); result["status"] = "oracle_failed"; result["failure_class"] = Safe(e).FailureClass; result["error"] = Safe(e); }
        return result;
    }

    static void Parameters()
    {
        foreach (string lane in DriverLanes)
        {
            var actualSix = ColumnDescriptor(lane, "D6");
            Add(lane, "extended_column_scale6_layout", "layout_classification", new() { ["status"] = actualSix.FirstOrDefault()?.CType == 26 ? "observed" : "layout_unavailable",
                ["metadata"] = actualSix, ["requested_c_type"] = 26, ["requested_column_scale"] = 6, ["reason"] = "actual_TIMESTAMPS6_layout_recorded_no_synthetic_upgrade" });
        }
        foreach (string lane in DriverLanes)
        foreach (bool tz in new[] { false, true }) TailStability(lane, tz);
        foreach (string lane in DriverLanes)
        foreach (string declaration in new[] { "inferred", "inferred_dto", "DateTime", "DateTime2", "DateTimeOffset", "provider", "provider_dto" })
        {
            foreach (int? scale in new int?[] { null, 0, 3, 6, 7, 8, 9, 19, 23, 255 }) ParameterCase(lane, declaration, scale, null, false, false);
            foreach (int? precision in new int?[] { 0, 7, 9 }) ParameterCase(lane, declaration, null, precision, false, false);
            foreach (bool fixedDescribe in new[] { false, true })
            foreach (bool typedNull in new[] { false, true })
                if (fixedDescribe || typedNull) ParameterCase(lane, declaration, 7, null, fixedDescribe, typedNull);
            foreach (int precision in new[] { 0, 7, 9 }) ParameterCase(lane, declaration, null, precision, true, true);
            foreach (int precision in new[] { 7, 9 })
            { ParameterCase(lane, declaration, null, precision, true, false); ParameterCase(lane, declaration, null, precision, false, true); }
        }
    }
    static object TemporalSample(bool tz, long fraction)
    {
        if (tz) return new DateTimeOffset(Ordinary.AddTicks(fraction), TimeSpan.FromMinutes(330));
        return Ordinary.AddTicks(fraction);
    }
    static string FixedTargetColumn(string profile, bool tz)
    {
        if (profile == "shared") return tz ? "Z7" : "D7";
        if (profile == "tls") return tz ? "Z6" : "D6";
        throw new InvalidOperationException("fixed_target_profile_invalid");
    }
    static string ParameterQuery(bool tz)
    {
        string fmt = tz ? "YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM" : "YYYY-MM-DD HH24:MI:SS.FF9";
        return $"SELECT V,TO_CHAR(V,'{fmt}'),TO_CHAR(V,'YYYY-MM-DD HH24:MI:SS.FF7') FROM (SELECT :v AS V FROM DUAL)";
    }
    static Metadata[] ColumnDescriptor(string lane, string column)
    {
        using var c = Open(lane); using var cmd = Command(c, $"SELECT {column} FROM {Table} WHERE 1=0"); using var r = cmd.ExecuteReader(); return Columns(r);
    }
    static Dictionary<string, object?> EncodingObservation(object cmd, object? reader = null)
    {
        try
        {
            var selection = SelectOwnedStatement(cmd, reader);
            var info = selection.Statement is null ? null : Invoke(selection.Statement, "H"); var values = info is null ? null : Invoke(info, "GetParamsInfo");
            object? field = (values as Array)?.GetValue(0);
            object? binding = field?.GetType().GetProperty("TemporalBinding", Flags)?.GetValue(field);
            object? scale = binding?.GetType().GetProperty("EncodedScale", Flags)?.GetValue(binding)
                ?? binding?.GetType().GetProperty("EncodingScale", Flags)?.GetValue(binding);
            return new() { ["encoded_scale"] = scale is int n ? n : null, ["encoded_scale_observable"] = scale is int,
                ["encoding_source"] = scale is int ? "immutable_temporal_binding" : "unobservable_not_inferred_from_request_or_column" };
        }
        catch { return new() { ["encoded_scale"] = null, ["encoded_scale_observable"] = false, ["encoding_source"] = "unobservable" }; }
    }
    static Dictionary<string, object?> ExecuteParameterPhase(string lane, DbCommand cmd, DbParameter p, DbParameter? rowParameter,
        string column, object expected, string phase, int? requestedScale, int? requestedPrecision, bool prepare, int? previousStatementId = null, string recovery = "none")
    {
        int? row = rowParameter is null ? null : NextId++;
        var result = NewPhaseRecord(phase, row, expected, requestedScale, requestedPrecision);
        result["target_column"] = rowParameter is null ? null : column;
        result["recovery_source"] = recovery; string failureStage = "parameter_configuration";
        try
        {
            p.Value = expected; if (rowParameter is not null) rowParameter.Value = row!;
            // Reader.Close owns/disposes its statement under R3; establish a real fresh prepare observation each phase.
            failureStage = "prepare"; result["prepare_invoked"] = true; cmd.Prepare();
            var before = SelectOwnedStatement(cmd); result["statement_before_owner"] = before.Owner; result["statement_id_before"] = before.Id;
            result["new_statement_observed"] = before.Id.HasValue && before.Id != previousStatementId;
            result["parameter_metadata_before_execute"] = ParameterMetadata(cmd).Take(1).ToArray();
            result["full_metadata_before_execute"] = ParameterMetadata(cmd);
            result["execute_attempted"] = true; failureStage = "execute";
            result["execution_api"] = "ExecuteReader";
            using var r = cmd.ExecuteReader();
            CaptureLiveExecutionSnapshot(result, cmd, r);
            if (rowParameter is not null)
            {
                var drain = DrainDmlResults(r.Read, r.NextResult, () => r.RecordsAffected);
                foreach (var item in drain) result[item.Key] = item.Value;
                if (drain["unexpected_rowset"] is true) throw new ProbeInfrastructureException(new InvalidOperationException("metadata_dml_unexpected_rowset"));
                result["affected_rows"] = drain["records_affected"]; result["status"] = "observed"; result["write_status"] = "written";
            }
            else
            {
                Need(r.Read(), "advisory_row");
                result["oracles"] = new[] { CaptureAdvisoryOracle(lane, r, expected, column.StartsWith('Z')) }; result["status"] = "observed";
            }
        }
        catch (Exception e)
        {
            CheckInfra(e); result["status"] = "driver_parameter_rejected"; result["error"] = Safe(e); result["failure_class"] = Safe(e).FailureClass; result["failure_stage"] = failureStage;
            var after = SelectOwnedStatement(cmd); result["statement_after_owner"] = after.Owner; result["statement_id_after"] = after.Id;
            result["parameter_metadata"] = ParameterMetadata(cmd).Take(1).ToArray();
            foreach (var item in EncodingObservation(cmd)) result[item.Key] = item.Value;
        }
        if (row.HasValue)
        {
            using var fresh = Open("W"); result["persisted_rows"] = Convert.ToInt64(Scalar(fresh, $"SELECT COUNT(*) FROM {Table} WHERE ID={row.Value}"), CultureInfo.InvariantCulture);
            result["persistence_source"] = "fresh_connection_row_count";
            if ((long)result["persisted_rows"]! == 1)
                result["oracles"] = DriverLanes.Select(reader => expected is DBNull ? ReadNullOracle(reader, column, row.Value) : ReadOracle(reader, column, row.Value, expected)).ToArray();
        }
        else if (result["execute_attempted"] is true)
        {
            result["persisted_rows"] = 0L; result["persistence_source"] = "advisory_no_dml";
        }
        return result;
    }
    static int? LastStatementId(Dictionary<string, object?> phase) => phase.GetValueOrDefault("statement_id_after") as int? ?? phase.GetValueOrDefault("statement_id_before") as int?;
    static Dictionary<string, object?> FreshScale6Recovery(string lane, string declaration, bool tz, bool fixedDescribe, bool typedNull, string column, int? previousStatement)
    {
        if (fixedDescribe) column = FixedTargetColumn(Profile, tz);
        using var c = Open(lane); using var cmd = Command(c, fixedDescribe ? $"INSERT INTO {Table}({column},ID) VALUES(:v,:rid)" : ParameterQuery(tz));
        var p = cmd.CreateParameter(); p.ParameterName = "v";
        if (declaration is "DateTime" or "DateTime2" or "DateTimeOffset") p.DbType = Enum.Parse<DbType>(declaration);
        else if (declaration is "provider" or "provider_dto") { var prop = p.GetType().GetProperty("DmSqlType")!; prop.SetValue(p, Enum.Parse(prop.PropertyType, tz ? "DateTimeOffset" : "DateTime")); }
        p.Scale = 6; p.Precision = 0; cmd.Parameters.Add(p); DbParameter? rid = null;
        if (fixedDescribe) { rid = cmd.CreateParameter(); rid.ParameterName = "rid"; rid.DbType = DbType.Int32; cmd.Parameters.Add(rid); }
        return ExecuteParameterPhase(lane, cmd, p, rid, column, typedNull ? DBNull.Value : TemporalSample(tz, 7654320), "reprepare", 6, 0, true, previousStatement, "fresh_connection_after_rejection");
    }
    static object CaptureAdvisoryOracle(string lane, DbDataReader r, object expected, bool tz)
    {
        bool serverNull = r.IsDBNull(1);
        string? ff9 = serverNull ? null : SafeTemporalText(r.GetString(1)), ff7 = serverNull ? null : SafeTemporalText(r.GetString(2));
        bool valueNull = r.IsDBNull(0); bool formatsNull = serverNull && r.IsDBNull(2); object cell = Cell(r, 0);
        var context = new NullObservationContext(expected is DBNull, lane, 0, valueNull, formatsNull, cell);
        ValueObservation Getter(string name, Func<object?> get) => expected is DBNull ? ObserveConfirmedNull(context, name, get) : Observe(get);
        var str = Getter("GetString", () => r.GetString(0)); var result = new Dictionary<string, object?> {
            ["reader_lane"] = lane, ["status"] = "observed", ["metadata"] = Columns(r).Take(1).ToArray(), ["schema_scales"] = SchemaScales(r).Take(1).ToArray(),
            ["cell"] = cell, ["server_null"] = serverNull, ["null_context"] = expected is DBNull ? context.PublicEvidence() : null,
            ["server_ff9"] = ff9, ["server_ff7"] = ff7,
            ["actual_server"] = ff9 is null ? null : ActualServer(ff9, Columns(r).FirstOrDefault()?.SemanticScale),
            ["server_exact"] = expected is DBNull ? formatsNull && valueNull : ff9 == ExpectedText(expected, 9),
            ["server_ff7_exact"] = expected is DBNull ? formatsNull && valueNull : ff7 == ExpectedText(expected is DateTimeOffset dto ? dto.DateTime : expected, 7),
            ["get_value"] = Getter("GetValue", () => r.GetValue(0)), ["get_string"] = str,
            ["get_string_exact"] = expected is DBNull ? valueNull && formatsNull : str.Text is not null && ParseFields(ff9!) is not null && ParseFields(str.Text) == ParseFields(ff9!),
            ["typed"] = tz ? Getter("GetDateTimeOffset", () => Invoke(r, "GetDateTimeOffset", 0)) : Getter("GetDateTime", () => r.GetDateTime(0)),
            ["generic"] = tz ? Getter("GetFieldValue<DateTimeOffset>", () => r.GetFieldValue<DateTimeOffset>(0)) : Getter("GetFieldValue<DateTime>", () => r.GetFieldValue<DateTime>(0)),
            ["server_component_basis"] = "not_queried_advisory_server_text_only", ["server_components_exact"] = false,
            ["subsequent_read_completion"] = !r.Read()
        };
        return result;
    }
    static object ReadNullOracle(string lane, string column, int row)
    {
        using var c = Open(lane); string fmt = column.StartsWith('Z') ? "YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM" : "YYYY-MM-DD HH24:MI:SS.FF9";
        using var cmd = Command(c, $"SELECT {column},TO_CHAR({column},'{fmt}'),TO_CHAR({column},'YYYY-MM-DD HH24:MI:SS.FF7') FROM {Table} WHERE ID={row}");
        using var r = cmd.ExecuteReader(); Need(r.Read(), "null_oracle_row"); return CaptureAdvisoryOracle(lane, r, DBNull.Value, column.StartsWith('Z'));
    }
    static void TailStability(string lane, bool tz)
    {
        Attempt(lane, tz ? "inferred_dto_tail_stability" : "inferred_dt_tail_stability", "prepare_value_stability", () => {
            using var c = Open(lane); using var cmd = Command(c, ParameterQuery(tz));
            var p = cmd.CreateParameter(); p.ParameterName = "v"; cmd.Parameters.Add(p);
            var phases = new List<Dictionary<string, object?>>();
            phases.Add(ExecuteParameterPhase(lane, cmd, p, null, tz ? "Z7" : "D7", TemporalSample(tz, 1234560), "initial", null, null, true));
            if (phases[0]["status"] is "observed")
            {
                phases.Add(ExecuteParameterPhase(lane, cmd, p, null, tz ? "Z7" : "D7", TemporalSample(tz, 1234567), "replacement", null, null, false, LastStatementId(phases[0])));
                if (RequiresFreshRecovery(phases[1])) phases.Add(FreshScale6Recovery(lane, tz ? "inferred_dto" : "inferred", tz, false, false, tz ? "Z7" : "D7", LastStatementId(phases[1])));
                else
                {
                    p.Scale = 6; p.Precision = 0;
                    phases.Add(ExecuteParameterPhase(lane, cmd, p, null, tz ? "Z7" : "D7", TemporalSample(tz, 7654320), "reprepare", 6, 0, true, LastStatementId(phases[1])));
                }
            }
            var result = new Dictionary<string, object?> { ["status"] = phases.Count > 1 && phases[1]["status"] is "observed" ? "value_replacement_observed" : "replacement_rejected", ["phase_results"] = phases, ["type_source"] = "inferred" };
            object? Getter(int index) => phases[index]["oracles"] is object[] { Length: > 0 } oracles && oracles[0] is Dictionary<string, object?> row ? row.GetValueOrDefault("get_value") : null;
            result["first_metadata"] = phases[0]["parameter_metadata"]; result["first_value"] = Getter(0);
            if (phases.Count > 1)
            {
                result["second_metadata"] = phases[1]["parameter_metadata"]; result["second_value"] = Getter(1);
                var first = phases[0]["parameter_metadata"] as Metadata[]; var second = phases[1]["parameter_metadata"] as Metadata[];
                result["wire_metadata_stable"] = first is { Length: 1 } && second is { Length: 1 } && first[0].CType == second[0].CType && first[0].Width == second[0].Width && first[0].SemanticScale == second[0].SemanticScale;
                if (phases[1].TryGetValue("error", out var replacementError)) { result["error"] = replacementError; result["failure_class"] = phases[1]["failure_class"]; }
            }
            else if (phases[0].TryGetValue("error", out var initialError)) { result["error"] = initialError; result["failure_class"] = phases[0]["failure_class"]; }
            if (phases.Count > 2)
            {
                result["explicit_scale6_reprepare"] = phases[2]["parameter_metadata"]; result["scale6_execute_metadata"] = phases[2]["parameter_metadata"];
                result["scale6_value"] = Getter(2);
                result["subsequent_read_completion"] = phases[2]["oracles"] is object[] { Length: > 0 } oracles && oracles[0] is Dictionary<string, object?> row && row.GetValueOrDefault("subsequent_read_completion") is true;
            }
            return result;
        });
    }
    static void ParameterCase(string lane, string declaration, int? scale, int? precision, bool fixedDescribe, bool typedNull)
    {
        string id = $"bind_{declaration}_s{scale?.ToString() ?? "unset"}_p{precision?.ToString() ?? "unset"}_{(fixedDescribe ? "fixed" : "advisory")}_{(typedNull ? "null" : "value")}";
        Attempt(lane, id, "parameter_metadata", () => {
            bool tz = declaration is "DateTimeOffset" or "inferred_dto" or "provider_dto";
            object value = typedNull ? DBNull.Value : TemporalSample(tz, 1234560);
            string column = fixedDescribe ? FixedTargetColumn(Profile, tz) : tz ? "Z7" : "D7";
            string source = declaration.StartsWith("inferred", StringComparison.Ordinal) ? typedNull ? fixedDescribe ? "fixed_describe" : "untyped_null" : "inferred" : declaration.StartsWith("provider", StringComparison.Ordinal) ? "provider" : "user_db_type";
            var result = new Dictionary<string, object?> { ["declaration"] = declaration, ["requested_scale"] = scale,
                ["requested_precision"] = precision, ["typed_null"] = typedNull, ["fixed_describe"] = fixedDescribe, ["type_source"] = source,
                ["target_column"] = fixedDescribe ? column : null,
                ["column_metadata"] = fixedDescribe ? ColumnDescriptor(lane, column) : Array.Empty<Metadata>() };
            using var c = Open(lane); using var cmd = Command(c, fixedDescribe ? $"INSERT INTO {Table}({column},ID) VALUES(:v,:rid)" : ParameterQuery(tz));
            var phases = new List<Dictionary<string, object?>>(); result["phase_results"] = phases;
            try
            {
                var p = cmd.CreateParameter(); p.ParameterName = "v";
                if (declaration is "DateTime" or "DateTime2" or "DateTimeOffset") p.DbType = Enum.Parse<DbType>(declaration);
                else if (declaration is "provider" or "provider_dto") { var prop = p.GetType().GetProperty("DmSqlType")!; prop.SetValue(p, Enum.Parse(prop.PropertyType, tz ? "DateTimeOffset" : "DateTime")); }
                if (scale.HasValue) p.Scale = checked((byte)scale.Value); if (precision.HasValue) p.Precision = checked((byte)precision.Value);
                cmd.Parameters.Add(p); DbParameter? rid = null;
                if (fixedDescribe) { rid = cmd.CreateParameter(); rid.ParameterName = "rid"; rid.DbType = DbType.Int32; cmd.Parameters.Add(rid); }
                phases.Add(ExecuteParameterPhase(lane, cmd, p, rid, column, value, "initial", scale, precision, true));
                if (phases[0]["status"] is "written" or "observed")
                {
                    object replacement = typedNull ? DBNull.Value : TemporalSample(tz, 7654320);
                    phases.Add(ExecuteParameterPhase(lane, cmd, p, rid, column, replacement, "replacement", scale, precision, false, LastStatementId(phases[0])));
                    if (RequiresFreshRecovery(phases[1])) phases.Add(FreshScale6Recovery(lane, declaration, tz, fixedDescribe, typedNull, column, LastStatementId(phases[1])));
                    else
                    {
                        p.Scale = 6; p.Precision = 0;
                        phases.Add(ExecuteParameterPhase(lane, cmd, p, rid, column, replacement, "reprepare", 6, 0, true, LastStatementId(phases[1])));
                    }
                }
                result["status"] = phases[0]["status"] is "written" or "observed" ? "observed" : "driver_parameter_rejected";
                if (phases[0].TryGetValue("error", out var error)) { result["error"] = error; result["failure_class"] = phases[0]["failure_class"]; }
            }
            catch (Exception e)
            {
                CheckInfra(e); result["status"] = "driver_parameter_rejected"; result["error"] = Safe(e); result["failure_class"] = Safe(e).FailureClass;
                if (phases.Count == 0)
                {
                    var failed = NewPhaseRecord("initial", null, value, scale, precision); failed["target_column"] = fixedDescribe ? column : null;
                    failed["status"] = "driver_parameter_rejected"; failed["error"] = Safe(e); failed["failure_class"] = Safe(e).FailureClass; failed["failure_stage"] = "parameter_configuration";
                    phases.Add(failed);
                }
            }
            return result;
        });
        if (fixedDescribe && !typedNull && declaration == "DateTime2" && scale == 7 && precision is null)
        {
            WriteCase(lane, "dual_scale6_" + id, "D6", DbType.DateTime2, Ordinary.AddTicks(1234560), 7, true, false);
            WriteCase(lane, "dual_scale6_loss_" + id, "D6", DbType.DateTime2, Ordinary.AddTicks(1234567), 7, true, false);
        }
    }

    static void Extended()
    {
        SimpleLayouts();
        foreach (int scale in new[] { 8, 9 })
        {
            bool available = false;
            Attempt("server", "ddl_scale" + scale, "server_ddl", () => {
                using var c = Open("W"); Exec(c, $"ALTER TABLE {Table} ADD D{scale} TIMESTAMP({scale})");
                Exec(c, $"ALTER TABLE {Table} ADD Z{scale} DATETIME({scale}) WITH TIME ZONE"); available = true;
                return new() { ["status"] = "server_columns_created" };
            });
            if (!available) { Add("server", "scale" + scale + "_dependent", "classification", new() { ["status"] = "closed", ["reason"] = "exact_ddl_failed_not_driver_capability_inference" }); continue; }
            foreach (string lane in DriverLanes) Attempt(lane, "columns_scale" + scale, "column_metadata", () => {
                using var c = Open(lane); using var cmd = Command(c, $"SELECT D{scale},Z{scale} FROM {Table} WHERE 1=0"); using var r = cmd.ExecuteReader();
                return new() { ["status"] = "observed", ["metadata"] = Columns(r), ["schema_scales"] = SchemaScales(r) };
            });
            foreach (string lane in DriverLanes)
            {
                // Write configured higher scale, separately from server CAST existence/read probes.
                WriteCase(lane, "write_scale" + scale, "D" + scale, DbType.DateTime2, Ordinary.AddTicks(1), scale, true, false);
                WriteCase(lane, "write_tz_scale" + scale, "Z" + scale, DbType.DateTimeOffset, new DateTimeOffset(Ordinary.AddTicks(1), TimeSpan.FromMinutes(330)), scale, true, false);
            }
            foreach (int ns in scale == 8 ? new[] { 10, 90, 100, 1 } : new[] { 1, 99, 100 })
                ServerExpression($"ns_scale{scale}_{ns}", $"CAST('2026-07-23 14:15:16.{ns:D9}' AS TIMESTAMP({scale}))", $"2026-07-23 14:15:16.{ns:D9}", "higher_scale_cast_characterization");
        }
        foreach (int day in new[] { 4, 5, 10, 14, 15 })
            ServerExpression("calendar_1582_10_" + day, $"CAST('1582-10-{day:D2} 12:00:00.0000001' AS TIMESTAMP(7))", $"1582-10-{day:D2} 12:00:00.000000100", "calendar_cast_characterization");
        ServerExpression("year_zero", "CAST('0000-01-01 00:00:00' AS TIMESTAMP(7))", null, "clr_outside_calendar_characterization");
        ServerExpression("bce", "CAST('-0001-01-01 00:00:00' AS TIMESTAMP(7))", null, "bce_cast_characterization");
        foreach (int scale in new[] { 6, 7, 8, 9 })
        {
            ServerExpression("time24_scale" + scale, $"CAST('24:00:00' AS TIME({scale}))", null, "time24_existence_characterization", true);
            ServerExpression("time_fraction_scale" + scale, $"CAST('12:13:14.123456789' AS TIME({scale}))", null, "time_gt6_existence_characterization", true);
        }
        foreach (int scale in new[] { 6, 7 })
        {
            string col = "L" + scale; bool exists = false;
            Attempt("server", "tltz_ddl_scale" + scale, "tltz_server_ddl", () => {
                using var c = Open("W"); Exec(c, $"ALTER TABLE {Table} ADD {col} TIMESTAMP({scale}) WITH LOCAL TIME ZONE"); exists = true;
                return new() { ["status"] = "server_column_created" };
            });
            if (!exists) { Add("server", "tltz_scale" + scale + "_closed", "classification", new() { ["status"] = "closed", ["reason"] = "exact_tltz_ddl_failed" }); continue; }
            foreach (string lane in DriverLanes) Attempt(lane, "tltz_metadata_scale" + scale, "tltz_metadata", () => {
                using var c = Open(lane); using var cmd = Command(c, $"SELECT {col} FROM {Table} WHERE 1=0"); using var r = cmd.ExecuteReader();
                return new() { ["status"] = "observed", ["metadata"] = Columns(r), ["schema_scales"] = SchemaScales(r) };
            });
            foreach (var v in new[] { ("crossday", new DateTime(2026,7,23,23,59,59).AddTicks(scale == 7 ? 9999999 : 9999990)),
                ("min", DateTime.MinValue), ("max", DateTime.MaxValue.AddTicks(scale == 7 ? 0 : -9)) })
            {
                foreach (string lane in DriverLanes) WriteCase(lane, "tltz_" + scale + "_" + v.Item1, col, DbType.DateTime2, v.Item2, scale, true, false);
                TltzSeedRead(scale, col, v.Item1, v.Item2);
            }
        }
        Add("W", "tltz_cross_session_timezone", "classification", new() { ["status"] = "closed", ["reason"] = "builder_forbids_nondefault_timezone_no_verified_alternate_session_path", ["capability_open"] = false });
    }

    static void SimpleLayouts()
    {
        foreach (var item in new[] {
            (Id: "ordinary_date", Expression: "CAST('2026-07-23' AS DATE)", Format: "YYYY-MM-DD"),
            (Id: "ordinary_time6", Expression: "CAST('12:13:14.123456' AS TIME(6))", Format: "HH24:MI:SS.FF9"),
            (Id: "ordinary_time_tz6", Expression: "CAST('12:13:14.123456 +05:30' AS TIME(6) WITH TIME ZONE)", Format: "HH24:MI:SS.FF9 TZH:TZM") })
        {
            string? text = null; bool evaluated = false; int? scale = null;
            Attempt("server", item.Id, "ordinary_layout_server_seed", () => {
                using var c = Open("W"); text = SafeTemporalText(Convert.ToString(Scalar(c, $"SELECT TO_CHAR({item.Expression},'{item.Format}') FROM DUAL"), CultureInfo.InvariantCulture)!);
                using var cmd = Command(c, $"SELECT {item.Expression} FROM DUAL WHERE 1=0"); using var r = cmd.ExecuteReader(); scale = Columns(r).FirstOrDefault()?.SemanticScale;
                evaluated = true; return new() { ["status"] = "server_expression_evaluated", ["actual_server"] = ActualServer(text, scale),
                    ["seed_writer"] = "server_expression", ["independent_of_W_write"] = true, ["oracle_role"] = "server_layout_seed_not_typed_write_oracle" };
            });
            if (!evaluated) continue;
            foreach (string lane in DriverLanes) Attempt(lane, item.Id, "ordinary_layout_driver_read", () => {
                using var c = Open(lane); using var cmd = Command(c, $"SELECT {item.Expression} FROM DUAL"); using var r = cmd.ExecuteReader(); Need(r.Read(), "layout_seed_row");
                return new() { ["status"] = "observed", ["actual_server"] = ActualServer(text!, scale), ["metadata"] = Columns(r), ["schema_scales"] = SchemaScales(r),
                    ["cell"] = Cell(r, 0), ["get_value"] = Observe(() => r.GetValue(0)), ["get_string"] = Observe(() => r.GetString(0)),
                    ["typed"] = Observe(() => r.GetDateTime(0)), ["generic"] = Observe(() => r.GetFieldValue<DateTime>(0)),
                    ["typed_offset"] = Observe(() => Invoke(r, "GetDateTimeOffset", 0)), ["generic_offset"] = Observe(() => r.GetFieldValue<DateTimeOffset>(0)),
                    ["subsequent_read_completion"] = !r.Read() };
            });
        }
    }

    static void TltzSeedRead(int scale, string column, string name, DateTime value)
    {
        int row = NextId++; bool seeded = false; string? server = null;
        Attempt("server", $"server_seed_TLTZ{scale}_{name}", "tltz_independent_server_seed", () => {
            using var c = Open("W"); string text = value.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            Exec(c, $"INSERT INTO {Table}(ID,{column}) VALUES({row},CAST('{text}' AS TIMESTAMP({scale}) WITH LOCAL TIME ZONE))");
            using var fresh = Open("W"); server = SafeTemporalText(Convert.ToString(Scalar(fresh, $"SELECT TO_CHAR({column},'YYYY-MM-DD HH24:MI:SS.FF9') FROM {Table} WHERE ID={row}"), CultureInfo.InvariantCulture)!);
            seeded = true; return new() { ["status"] = "server_seed_created", ["row_id"] = row,
                ["actual_server"] = ActualServer(server, null), ["server_ff9"] = server, ["seed_role"] = "independent_server_CAST_not_typed_write_oracle", ["seed_writer"] = "server_expression", ["independent_of_W_write"] = true };
        });
        if (!seeded)
        {
            Add("W", $"tltz_read_{scale}_{name}", "tltz_independent_read", new() { ["status"] = "closed", ["reason"] = "independent_seed_unverified_read_gate_not_accepted", ["row_id"] = row });
            return;
        }
        Attempt("W", $"tltz_read_{scale}_{name}", "tltz_independent_read", () => {
            using var c = Open("W"); using var cmd = Command(c, $"SELECT {column} FROM {Table} WHERE ID={row}"); using var r = cmd.ExecuteReader(); Need(r.Read(), "tltz_seed_missing");
            return new() { ["status"] = "observed", ["row_id"] = row, ["server_ff9"] = server,
                ["actual_server"] = ActualServer(server!, Columns(r).FirstOrDefault()?.SemanticScale), ["metadata"] = Columns(r), ["schema_scales"] = SchemaScales(r),
                ["cell"] = Cell(r, 0), ["typed"] = Observe(() => r.GetDateTime(0)), ["generic"] = Observe(() => r.GetFieldValue<DateTime>(0)),
                ["get_value"] = Observe(() => r.GetValue(0)), ["get_string"] = Observe(() => r.GetString(0)), ["subsequent_read_completion"] = !r.Read() };
        });
    }

    static void ServerExpression(string id, string expression, string? expectedText, string category, bool time = false)
    {
        // Scalar server formatting runs separately from client temporal decode; a decoder failure never means server unsupported.
        string? server = null; bool evaluated = false;
        Attempt("server", id, category, () => {
            using var c = Open("W"); string fmt = time ? "HH24:MI:SS.FF9" : "YYYY-MM-DD HH24:MI:SS.FF9";
            server = SafeTemporalText(Convert.ToString(Scalar(c, $"SELECT TO_CHAR({expression},'{fmt}') FROM DUAL"), CultureInfo.InvariantCulture)!);
            using var metadataCommand = Command(c, $"SELECT {expression} FROM DUAL WHERE 1=0"); using var metadataReader = metadataCommand.ExecuteReader();
            int? observedScale = Columns(metadataReader).FirstOrDefault()?.SemanticScale; evaluated = true;
            return new() { ["status"] = "server_expression_evaluated", ["server_text"] = server, ["requested_synthetic_text"] = expectedText,
                ["server_preserves_requested"] = expectedText is null ? null : server.Trim() == expectedText,
                ["actual_server"] = ActualServer(server, observedScale),
                ["oracle_role"] = "CAST_conversion_only_not_typed_write_oracle" };
        });
        if (!evaluated) return;
        foreach (string lane in DriverLanes) Attempt(lane, id, "server_expression_driver_read", () => {
            using var c = Open(lane); using var cmd = Command(c, $"SELECT {expression} FROM DUAL"); using var r = cmd.ExecuteReader(); Need(r.Read(), "expression_row");
            var text = Observe(() => r.GetString(0));
            var typed = Observe(() => r.GetDateTime(0)); var generic = Observe(() => r.GetFieldValue<DateTime>(0));
            return new() { ["status"] = "observed", ["server_text"] = server, ["actual_server"] = ActualServer(server!, Columns(r).FirstOrDefault()?.SemanticScale),
                ["metadata"] = Columns(r), ["schema_scales"] = SchemaScales(r), ["cell"] = Cell(r, 0),
                ["get_value"] = Observe(() => r.GetValue(0)), ["get_string"] = text,
                ["get_string_exact"] = text.Text is not null && ParseFields(server!) is not null && ParseFields(text.Text) == ParseFields(server!),
                ["typed"] = typed, ["generic"] = generic, ["typed_date"] = typed, ["generic_date"] = generic,
                ["subsequent_read_completion"] = !r.Read() };
        });
    }

    static void Rollback(string lane)
    {
        Attempt(lane, "typed_dml_rollback", "transaction", () => {
            int id = NextId++; using (var c = Open(lane)) using (var tx = c.BeginTransaction())
            using (var cmd = Command(c, $"INSERT INTO {Table}(ID,D6) VALUES({id},:v)"))
            {
                cmd.Transaction = tx; var p = cmd.CreateParameter(); p.ParameterName = "v"; p.DbType = DbType.DateTime; p.Value = Ordinary; cmd.Parameters.Add(p);
                cmd.ExecuteNonQuery(); tx.Rollback();
            }
            using var fresh = Open("W"); long count = Convert.ToInt64(Scalar(fresh, $"SELECT COUNT(*) FROM {Table} WHERE ID={id}"), CultureInfo.InvariantCulture);
            return new() { ["status"] = count == 0 ? "rollback_verified" : "rollback_failed", ["fresh_rows"] = count };
        });
    }

    static void Attempt(string lane, string id, string category, Func<Dictionary<string, object?>> work)
    {
        Stage = lane + ":" + id;
        try { Add(lane, id, category, work()); }
        catch (Exception e) { CheckInfra(e); Add(lane, id, category, new() { ["status"] = "failed", ["error"] = Safe(e), ["failure_class"] = Safe(e).FailureClass }); }
    }
    static void Add(string lane, string id, string category, Dictionary<string, object?> result)
    {
        Need(!Cases.Any(c => (string)c["case_id"]! == id && (string)c["driver_lane"]! == lane), "duplicate_case");
        result["case_id"] = id; result["driver_lane"] = lane; result["category"] = category;
        if (lane == "server" && result.GetValueOrDefault("error") is ErrorInfo { VerifiedServerResponse: true, Number: { } number }
            && Capabilities is { } cap && cap.GetProperty("profiles").GetProperty(Profile).TryGetProperty("verified_unsupported_error_numbers", out var numbers)
            && numbers.TryGetProperty(id, out var approved) && approved.EnumerateArray().Any(n => n.GetInt32() == number)) result["classification"] = "server_unsupported";
        Cases.Add(result);
        Report["completed_cases"] = Cases.Count; Report["last_case"] = lane + ":" + id; Write(Output, Report);
    }

    static Metadata[] ParameterMetadata(object command, object? reader = null)
    {
        try
        {
            var selection = SelectOwnedStatement(command, reader);
            var info = selection.Statement is null ? null : Invoke(selection.Statement, "H"); var values = info is null ? null : Invoke(info, "GetParamsInfo");
            return values is Array a ? a.Cast<object>().Select(f => Capture(f, "parameter_describe")).ToArray() : [];
        }
        catch { return []; }
    }
    static Metadata[] Columns(DbDataReader r) => r.GetType().GetField("m_ColInfo", Flags)?.GetValue(r) is Array a ? a.Cast<object>().Select(f => Capture(f, "result_column")).ToArray() : [];
    static int?[] SchemaScales(DbDataReader r)
    {
        try { var schema = r.GetSchemaTable(); return schema is null ? [] : schema.Rows.Cast<DataRow>().Select(row => row["NumericScale"] is DBNull ? (int?)null : Convert.ToInt32(row["NumericScale"], CultureInfo.InvariantCulture)).ToArray(); }
        catch { return []; }
    }
    static Metadata Capture(object field, string role)
    {
        try
        {
            int? Call(string method) { var v = field.GetType().GetMethod(method, Flags, null, Type.EmptyTypes, null)?.Invoke(field, null); return v is null ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            object? raw = field.GetType().GetProperty("TemporalServerMetadata", Flags)?.GetValue(field);
            bool hasRaw = raw?.GetType().GetProperty("HasServerSource", Flags)?.GetValue(raw) is true;
            int? RawInt(string name) => hasRaw && raw!.GetType().GetProperty(name, Flags)?.GetValue(raw) is int n ? n : null;
            int? rawScale = RawInt("Scale");
            bool? bdta = field.GetType().GetField("isBdta", Flags)?.GetValue(field) as bool?;
            string? rawSource = hasRaw ? raw!.GetType().GetProperty("Source", Flags)?.GetValue(raw)?.ToString() : null;
            if (rawSource is not ("ParameterDescribe" or "ResultColumn")) rawSource = null;
            object? mask = field.GetType().GetField("mask", Flags)?.GetValue(field);
            return new(Call("GetCType").HasValue && Call("GetScale").HasValue, Call("GetCType"), Call("GetPrecision"), Call("GetScale"), mask is null ? null : Convert.ToInt32(mask, CultureInfo.InvariantCulture),
                Call("GetTypeFlag"), rawScale, rawScale.HasValue ? "captured_server_source" : "unobservable_after_legacy_normalization_not_inferred",
                rawSource, RawInt("CType"), RawInt("Precision"), RawInt("MaskBeforeNormalization"), RawInt("DerivedMask"),
                bdta == false ? "ordinary_packed" : bdta == true ? "bdta" : "unobservable", bdta, role);
        }
        catch { return new(false, null, null, null, null, null, null, "unobservable", null, null, null, null, null, "unobservable", null, role); }
    }
    static ValueObservation Observe(Func<object?> getter)
    {
        try
        {
            object? value = getter();
            return value switch {
                DateTime dt => new("returned", "System.DateTime", dt.Ticks, null, dt.Kind.ToString(), null, null),
                DateTimeOffset dto => new("returned", "System.DateTimeOffset", dto.Ticks, checked((int)dto.Offset.TotalMinutes), null, null, null),
                string s => new("returned", "System.String", null, null, null, SafeTemporalText(s), null),
                DBNull => new("null", "System.DBNull", null, null, null, null, null),
                _ => new("returned", value?.GetType().FullName, null, null, null, null, null)
            };
        }
        catch (Exception e) { CheckInfra(e); return new("error", null, null, null, null, null, Safe(e)); }
    }
    static string ExpectedText(object value, int digits)
    {
        var dt = value is DateTimeOffset dto ? dto.DateTime : (DateTime)value;
        long ns = dt.Ticks % TimeSpan.TicksPerSecond * 100;
        string text = dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "." + ns.ToString("D9", CultureInfo.InvariantCulture)[..digits];
        if (value is DateTimeOffset offset) text += " " + offset.ToString("zzz", CultureInfo.InvariantCulture);
        return text;
    }
    static string SafeTemporalText(string text)
    {
        Need(text.Length <= 80 && Regex.IsMatch(text, @"^[-+0-9 :./TtZzBbCcEeAaDdMmPp]+$"), "unsafe_temporal_text");
        return text;
    }
    static ErrorInfo Safe(Exception e)
    {
        bool forcedInfra = e is ProbeInfrastructureException;
        while ((e is TargetInvocationException || e is ProbeInfrastructureException) && e.InnerException is not null) e = e.InnerException;
        int? number = null;
        try { if (e.GetType().GetProperty("Number", Flags)?.GetValue(e) is int n) number = n; } catch { }
        object? info = e.GetType().GetProperty("FailureInfo", Flags)?.GetValue(e);
        string? EnumValue(string property, string[] allowed)
        {
            string? value = info?.GetType().GetProperty(property, Flags)?.GetValue(info)?.ToString();
            return value is not null && allowed.Contains(value) ? value : null;
        }
        string? kind = EnumValue("ErrorKind", ["Unknown", "Timeout", "Canceled", "Transport", "Server", "OutcomeUnknown"]);
        string? phase = EnumValue("Phase", ["Unknown", "Dns", "Connect", "Authenticate", "Prepare", "Send", "Receive", "Fetch", "Commit", "Rollback", "Cleanup", "PoolWait"]);
        string? outcome = EnumValue("OperationOutcome", ["NotSent", "ServerReported", "Unknown"]);
        string? code = info?.GetType().GetProperty("ErrorCode", Flags)?.GetValue(info) as string;
        if (code is not null && !Regex.IsMatch(code, "^WDM_[A-Z0-9_]{1,60}$")) code = null;
        bool verified = e.GetType().GetProperty("HasVerifiedServerResponse", Flags)?.GetValue(e) is true
            || kind == "Server" && outcome == "ServerReported";
        bool InnerInfra(Exception current) => current is TimeoutException or OperationCanceledException or IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException
            || current.InnerException is not null && InnerInfra(current.InnerException);
        bool knownNetworkNumber = false;
        if (number.HasValue && !verified)
        {
            string? prefix = e.GetType().Namespace;
            var definition = prefix is null ? null : e.GetType().Assembly.GetType(prefix + ".DmErrorDefinition");
            knownNetworkNumber = new[] { "ECNET_COMMUNITION_ERROR", "ECNET_NO_SOCKET_DATA", "ECNET_CONNECTION_CLOSED", "ECNET_CONNCTION_NOT_OPENED", "ECNET_COMMAND_TIME_OUT", "ECNET_CONNECTION_SWITCHED", "ECNET_CONNECTION_SWITCH_FAILED", "ECNET_CONNPOOL_TIMEOUT" }
                .Any(name => definition?.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int n && n == number.Value);
        }
        bool infra = forcedInfra || knownNetworkNumber || InnerInfra(e) || kind is "Timeout" or "Canceled" or "Transport" or "OutcomeUnknown"
            || e is TimeoutException or OperationCanceledException or IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException;
        string classification = infra ? "infra_failure" : verified ? "verified_server_response"
            : e is ArgumentException or OverflowException or NotSupportedException or InvalidCastException or InvalidOperationException ? "user_rejection" : "unverified_driver_error";
        if (verified && number.HasValue && Capabilities is { } cap && Stage.StartsWith("server:", StringComparison.Ordinal)
            && cap.GetProperty("profiles").GetProperty(Profile).TryGetProperty("verified_unsupported_error_numbers", out var numbers)
            && numbers.TryGetProperty(Stage[7..], out var approved) && approved.ValueKind == JsonValueKind.Array
            && approved.EnumerateArray().Any(n => n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out int x) && x == number.Value)) classification = "verified_server_response";
        return new(e.GetType().FullName, number, classification, verified, kind, phase, code, outcome);
    }
    static void CheckInfra(Exception e) { if (Safe(e).FailureClass == "infra_failure") throw new ProbeInfrastructureException(e); }
    static JsonElement LoadStrict(string path)
    {
        return ParseStrict(File.ReadAllText(path));
    }
    static JsonElement ParseStrict(string text)
    {
        using var document = JsonDocument.Parse(text);
        static void Check(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject()) { Need(names.Add(property.Name), "duplicate_json_key"); Check(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Check(item);
        }
        Check(document.RootElement); return document.RootElement.Clone();
    }
    sealed record RawFields(int? Year, int? Month, int? Day, int Hour, int Minute, int Second, int Nanoseconds, int? OffsetMinutes);
    static RawFields? ParseFields(string text)
    {
        var match = Regex.Match(text, @"^\s*(?:(-?\d{1,4})[-/](\d{1,2})[-/](\d{1,2})[ T])?(\d{1,2}):(\d{1,2}):(\d{1,2})(?:\.(\d{1,9}))?(?:\s+([+-])(\d{1,2}):(\d{2}))?\s*$");
        if (!match.Success)
        {
            var date = Regex.Match(text, @"^\s*(-?\d{1,6})[-/](\d{1,2})[-/](\d{1,2})\s*$");
            return date.Success ? new(int.Parse(date.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(date.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(date.Groups[3].Value, CultureInfo.InvariantCulture), 0, 0, 0, 0, null) : null;
        }
        int Number(int index) => int.Parse(match.Groups[index].Value, CultureInfo.InvariantCulture);
        int? offset = match.Groups[8].Success ? (match.Groups[8].Value == "+" ? 1 : -1) * (Number(9) * 60 + Number(10)) : null;
        return new(match.Groups[1].Success ? Number(1) : null, match.Groups[2].Success ? Number(2) : null,
            match.Groups[3].Success ? Number(3) : null, Number(4), Number(5), Number(6),
            int.Parse(match.Groups[7].Value.PadRight(9, '0'), CultureInfo.InvariantCulture), offset);
    }
    static object ActualServer(string text, int? scale) => new { text, fields = ParseFields(text), semantic_scale = scale, observable = ParseFields(text) is not null };
    static object Cell(DbDataReader r, int ordinal)
    {
        try
        {
            object cache = r.GetType().GetField("m_RsCache", Flags)!.GetValue(r)!;
            var datas = cache.GetType().GetField("datas", Flags)!.GetValue(cache) as byte[][][];
            int offset = (int)cache.GetType().GetField("datasOffset", Flags)!.GetValue(cache)!;
            bool? bdta = cache.GetType().GetField("isRsBdta", Flags)?.GetValue(cache) as bool?;
            Need(datas is not null && offset >= 0 && offset < datas.Length && ordinal + 1 < datas[offset].Length, "cached_cell_unobservable");
            byte[]? bytes = datas![offset][ordinal + 1];
            return new { observable = true, payload_length = bytes?.Length, payload_sha256 = bytes is null ? null : Hash(bytes),
                is_null = bytes is null, is_bdta = bdta, result_format = bdta == false ? "ordinary_non_BDTA" : bdta == true ? "bdta" : "unobservable", source = "already_fetched_cell" };
        }
        catch { return new { observable = false, source = "unobservable", reason = "no_wire_layout_verification" }; }
    }
    static object? Invoke(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Flags, null, args.Select(a => a.GetType()).ToArray(), null)?.Invoke(obj, args);
    static DbCommand Command(DbConnection c, string sql) { var cmd = c.CreateCommand(); cmd.CommandTimeout = 12; cmd.CommandText = sql; return cmd; }
    static object? Scalar(DbConnection c, string sql) { using var cmd = Command(c, sql); return cmd.ExecuteScalar(); }
    static void Exec(DbConnection c, string sql) { using var cmd = Command(c, sql); cmd.ExecuteNonQuery(); }
    static bool Exists(DbConnection c, string table)
    {
        using var cmd = Command(c, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:n");
        var p = cmd.CreateParameter(); p.ParameterName = "n"; p.DbType = DbType.String; p.Value = table; cmd.Parameters.Add(p);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }
    static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    static void Need(bool condition, string code) { if (!condition) throw new InvalidOperationException(code); }
    static void Write(string path, object value)
    {
        Need(!string.IsNullOrWhiteSpace(path), "report_path");
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, true);
    }
}
