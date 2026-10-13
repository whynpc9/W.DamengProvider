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

// Reviewed v11 safety subset adapted for exact candidate/profile pins, native table DDL,
// explicit fresh cleanup receipts and native scalar observations. Ownership policy is unchanged.
internal static partial class Program
{
    const string User = "WDM_PROVIDER_TEST";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    static readonly Dictionary<string, object?> Report = new();
    static readonly List<Dictionary<string, object?>> Cases = [];
    static JsonElement Identity;
    static JsonElement? Capabilities;
    static string Profile = "", Repo = "", Output = "", OwnershipPath = "", Table = "", Stage = "configuration", Raw = "", WSettings = "", OSettings = "";
    static bool Reserved;
    static Ownership? CurrentOwnership;
    static readonly ConditionalWeakTable<DbConnection, ConnectionProfile> VerifiedProfiles = new();
    sealed record Ownership(int SchemaVersion, string Nonce, string Table, string Profile, string ServerVersion, string OwnerPackageSha256, string IdentitySha256, bool AbsentBeforeCreate, bool IdentityVerified, string OwnerMarker, string CreationState);
    sealed record ErrorInfo(string? Type, int? Number, string FailureClass, bool VerifiedServerResponse, string? FailureKind, string? Phase, string? Code, string? Outcome,string? AssertionCode=null);
    sealed class ProbeInfrastructureException(Exception inner) : Exception("probe_infrastructure_failure", inner) { }
    sealed record Metadata(bool Available, int? CType, int? Width, int? SemanticScale, int? Mask, int? TypeFlag, int? RawScale, string RawScaleStatus, string? RawSource, int? RawCType, int? RawPrecision, int? RawMaskBefore, int? RawDerivedMask, string ResultFormat, bool? IsBdta, string MetadataRole);
    sealed record ValueObservation(string Outcome, string? Type, long? Ticks, int? OffsetMinutes, string? Kind, string? Text, ErrorInfo? Error);
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
            Need(info.ServerVersion == ExpectedVersion && info.MessageVersion == (Profile=="shared"?21:11) && info.Encoding == (Profile=="shared"?"utf-8":"gb18030"), "server_version_encoding_message");
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

    interface IOwnershipDatabase
    {
        void VerifyIdentity();
        bool Exists(Ownership ownership);
        void Create(Ownership ownership);
        string? ReadMarkerDefault(Ownership ownership);
        void Drop(Ownership ownership);
        bool FreshAbsent(Ownership ownership);
    }
    sealed class LiveOwnershipDatabase : IOwnershipDatabase
    {
        internal bool Present; internal int Drops; internal long FreshCount=-1; internal string? Marker,FreshId,Connection,ActualUser,ActualSchema,FreshUser,FreshSchema;
        internal object Receipt(Ownership o) => new { identity_user=ActualUser,identity_schema=ActualSchema,cleanup_connection_id=Connection,marker_observed=Marker,table_observed=o.Table,exists_before_drop=Present,drops=Drops,fresh_connection_id=FreshId,fresh_identity_user=FreshUser,fresh_identity_schema=FreshSchema,fresh_table_count=FreshCount,guard_verified=true };
        public void VerifyIdentity() { using var c = Open("W"); }
        public bool Exists(Ownership ownership) { using var c = Open("W"); var info=ProfileInfo(c); ActualUser=info.ServerUser;ActualSchema=info.ServerSchema;Connection=ConnectionId(c);Present=Program.Exists(c, ownership.Table); return Present; }
        public void Create(Ownership o)
        {
            using var c = Open("W");
            Exec(c, $"CREATE TABLE {o.Table}(ID INT PRIMARY KEY,D6 TIMESTAMP(6),Z6 DATETIME(6) WITH TIME ZONE,A DATE,T TIME(6),I INTERVAL DAY(9) TO SECOND(6),J INTERVAL DAY(2) TO SECOND(6),PAD VARCHAR(2048),{o.OwnerMarker} VARCHAR(32) DEFAULT '{o.Nonce}')");
        }
        public string? ReadMarkerDefault(Ownership o)
        {
            using var c = Open("W"); using var cmd = Command(c, "SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t AND COLUMN_NAME=:m");
            foreach (var item in new[] { ("t", o.Table), ("m", o.OwnerMarker) })
            { var p = cmd.CreateParameter(); p.ParameterName = item.Item1; p.DbType = System.Data.DbType.String; p.Value = item.Item2; cmd.Parameters.Add(p); }
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0)) return null;
            string value = reader.GetString(0); bool multiple=reader.Read(); if(!multiple&&MarkerMatches(o,value))Marker=o.OwnerMarker; return multiple ? null : value;
        }
        public void Drop(Ownership ownership) { using var c = Open("W"); Exec(c, $"DROP TABLE {ownership.Table}");Connection=ConnectionId(c);Drops++; }
        public bool FreshAbsent(Ownership ownership) { using var c = Open("W"); FreshId=ConnectionId(c);var info=ProfileInfo(c);FreshUser=info.ServerUser;FreshSchema=info.ServerSchema;FreshCount=TableCount(c,ownership.Table); return FreshCount==0; }
    }
    static Ownership ParseOwnershipJson(string text) => JsonSerializer.Deserialize<Ownership>(ParseStrict(text).GetRawText(), Json) ?? throw new InvalidOperationException("ownership_missing");
    static void ValidateOwnership(Ownership o, string profile, string version, string packageSha, string identitySha)
    {
        Need(o.SchemaVersion == 1 && System.Text.RegularExpressions.Regex.IsMatch(o.Nonce, "^[A-F0-9]{32}$")
            && o.Table == "TPN_" + o.Nonce[..24] && o.OwnerMarker == "OWN_" + o.Nonce[..24]
            && o.CreationState is "reserved" or "create_inflight" or "confirmed" or "known_create_failure"
            && o.Profile == profile && o.ServerVersion == version && o.AbsentBeforeCreate && o.IdentityVerified
            && o.OwnerPackageSha256 == packageSha && o.IdentitySha256 == identitySha, "ownership_identity");
    }
    static void CreateWithLifecycle(Ownership initial, IOwnershipDatabase database, Action<Ownership> persist)
    {
        database.VerifyIdentity(); Need(!database.Exists(initial), "nonce_collision");
        persist(initial with { CreationState = "reserved" }); persist(initial with { CreationState = "create_inflight" });
        try { database.Create(initial); persist(initial with { CreationState = "confirmed" }); }
        catch (Exception e) { if (Safe(e).VerifiedServerResponse) persist(initial with { CreationState = "known_create_failure" }); throw; }
    }
    static bool MarkerMatches(Ownership ownership, string? rawDefault)
    {
        if (rawDefault is null || rawDefault.Length > 100) return false;
        string value = rawDefault.Trim();
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')') value = value[1..^1].Trim();
        return value == "'" + ownership.Nonce + "'";
    }
    static void CleanupWithOwnership(Ownership ownership, IOwnershipDatabase database, string profile, string version, string packageSha, string identitySha)
    {
        ValidateOwnership(ownership, profile, version, packageSha, identitySha);
        database.VerifyIdentity();
        if (database.Exists(ownership))
        {
            Need(ownership.CreationState != "known_create_failure", "known_create_failure_never_drop");
            Need(ownership.CreationState is "confirmed" or "create_inflight", "unconfirmed_creation_never_drop");
            Need(MarkerMatches(ownership, database.ReadMarkerDefault(ownership)), "ownership_marker_unverified_never_drop");
            database.Drop(ownership);
        }
        Need(database.FreshAbsent(ownership), "fresh_owned_absence_unverified");
    }

    sealed record StatementSelection(object? Statement, string Owner, int? Id);
    sealed class StatementIdentity(int value) { internal int Value { get; } = value; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, StatementIdentity> StatementIds = new();
    static int NextStatementId;
    static object? ReadField(object value, string name)
    {
        for (Type? type = value.GetType(); type is not null; type = type.BaseType)
        { var field = type.GetField(name, Flags | BindingFlags.DeclaredOnly); if (field is not null) return field.GetValue(value); }
        return null;
    }
    static StatementSelection SelectOwnedStatement(object command, object? reader = null)
    {
        object? statement; string owner;
        if (reader is not null)
        {
            bool closed = reader is DbDataReader dbReader ? dbReader.IsClosed : reader.GetType().GetProperty("IsClosed", Flags)?.GetValue(reader) is true;
            if (closed) return new(null, "unobservable", null);
            statement = ReadField(reader, "m_Statement"); owner = "reader";
        }
        else { statement = ReadField(command, "m_Stmt"); owner = "command"; }
        return statement is null ? new(null, "unobservable", null)
            : new(statement, owner, StatementIds.GetValue(statement, _ => new(System.Threading.Interlocked.Increment(ref NextStatementId))).Value);
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
            var database=new LiveOwnershipDatabase();
            CleanupWithOwnership(CurrentOwnership!, database, Profile, ExpectedVersion,
                Identity.GetProperty("W").GetProperty("package_sha256").GetString()!, (string)Report["identity_sha256"]!);
            Report["cleanup_receipt"]=database.Receipt(CurrentOwnership!);
            Report["cleanup_verified"] = true; Report["final_database_state"] = "owned_table_absent_fresh_connection";
            Write(OwnershipPath + ".final.json", new { table = Table, profile = Profile, identity_verified = true, absent_verified = true });
        }
        catch (Exception e) { Report["cleanup_verified"] = false; Report["cleanup_failure"] = Safe(e); Report["final_database_state"] = "unverified_exact_ownership_manifest_retained"; }
        finally { Stage = previousStage; }
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
                DateOnly date => new("returned", "System.DateOnly", date.ToDateTime(TimeOnly.MinValue).Ticks, null, null, null, null),
                TimeOnly time => new("returned", "System.TimeOnly", time.Ticks, null, null, null, null),
                TimeSpan span => new("returned", "System.TimeSpan", span.Ticks, null, null, null, null),
                DateTime dt => new("returned", "System.DateTime", dt.Ticks, null, dt.Kind.ToString(), null, null),
                DateTimeOffset dto => new("returned", "System.DateTimeOffset", dto.Ticks, checked((int)dto.Offset.TotalMinutes), null, null, null),
                string s => new("returned", "System.String", null, null, null, SafeTemporalText(s), null),
                DBNull => new("null", "System.DBNull", null, null, null, null, null),
                _ => new("returned", value?.GetType().FullName, null, null, null, null, null)
            };
        }
        catch (Exception e) { CheckInfra(e); return new("error", null, null, null, null, null, Safe(e)); }
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
        return new(e.GetType().FullName, number, classification, verified, kind, phase, code, outcome,e is ToolAssertionException assertion&&Enum.IsDefined(assertion.Code)?assertion.Code.ToString():null);
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
    static bool Exists(DbConnection c,string table)=>TableCount(c,table)!=0;
    static long TableCount(DbConnection c, string table)
    {
        using var cmd = Command(c, "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:n");
        var p = cmd.CreateParameter(); p.ParameterName = "n"; p.DbType = DbType.String; p.Value = table; cmd.Parameters.Add(p);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
    static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    static void Need(bool condition, string code) { if (!condition) throw new InvalidOperationException(code); }
    static void Write(string path, object value)
    {
        Need(!string.IsNullOrWhiteSpace(path), "report_path");
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, true);
    }
}
