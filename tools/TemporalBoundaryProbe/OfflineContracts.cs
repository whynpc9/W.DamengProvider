using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

// Offline and live use the same owner policy, marker parser, statement selector and phase serializer.
internal static partial class Program
{
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
        public void VerifyIdentity() { using var c = Open("W"); }
        public bool Exists(Ownership ownership) { using var c = Open("W"); return Program.Exists(c, ownership.Table); }
        public void Create(Ownership o)
        {
            using var c = Open("W");
            Exec(c, $"CREATE TABLE {o.Table}(ID INT PRIMARY KEY,D6 TIMESTAMP(6),Z6 DATETIME(6) WITH TIME ZONE,{o.OwnerMarker} VARCHAR(32) DEFAULT '{o.Nonce}')");
        }
        public string? ReadMarkerDefault(Ownership o)
        {
            using var c = Open("W"); using var cmd = Command(c, "SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t AND COLUMN_NAME=:m");
            foreach (var item in new[] { ("t", o.Table), ("m", o.OwnerMarker) })
            { var p = cmd.CreateParameter(); p.ParameterName = item.Item1; p.DbType = System.Data.DbType.String; p.Value = item.Item2; cmd.Parameters.Add(p); }
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0)) return null;
            string value = reader.GetString(0); return reader.Read() ? null : value;
        }
        public void Drop(Ownership ownership) { using var c = Open("W"); Exec(c, $"DROP TABLE {ownership.Table}"); }
        public bool FreshAbsent(Ownership ownership) { using var c = Open("W"); return !Program.Exists(c, ownership.Table); }
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
    static Dictionary<string, object?> NewPhaseRecord(string phase, int? row, object expected, int? scale, int? precision)
        => new() { ["phase"] = phase, ["row_id"] = row, ["expected"] = Observe(() => expected), ["requested_scale"] = scale,
            ["requested_precision"] = precision, ["target_column"] = null, ["persisted_rows"] = null, ["persistence_source"] = "not_executed", ["execute_attempted"] = false,
            ["parameter_metadata"] = Array.Empty<Metadata>(), ["parameter_metadata_before_execute"] = Array.Empty<Metadata>(), ["oracles"] = Array.Empty<object>(),
            ["statement_before_owner"] = "unobservable", ["statement_after_owner"] = "unobservable", ["statement_id_before"] = null, ["statement_id_after"] = null,
            ["prepare_invoked"] = false, ["new_statement_observed"] = false, ["recovery_source"] = "none",
            ["encoded_scale"] = null, ["encoded_scale_observable"] = false, ["encoding_source"] = "unobservable" };
    static bool RequiresFreshRecovery(Dictionary<string, object?> phase) => phase.GetValueOrDefault("status") is "driver_parameter_rejected";
    static Dictionary<string, object?> StatementObservation(object command, object? reader = null)
    {
        var selected = SelectOwnedStatement(command, reader);
        var result = new Dictionary<string, object?> { ["statement_owner"] = selected.Owner, ["statement_id"] = selected.Id,
            ["parameter_metadata"] = ParameterMetadata(command, reader) };
        foreach (var item in EncodingObservation(command, reader)) result[item.Key] = item.Value;
        return result;
    }

    static Dictionary<string, object?> RawTemporalOracle(object reader, int ordinal, Metadata? metadata)
    {
        var result = new Dictionary<string, object?> { ["evidence_role"] = "independent_cached_temporal_packed_input", ["source"] = "ordinary_result_cache_before_temporal_projection",
            ["capture_stage"] = "before_any_temporal_getter", ["read_only_synthetic"] = true, ["observable"] = false,
            ["c_type"] = metadata?.CType, ["semantic_scale"] = metadata?.SemanticScale, ["width"] = metadata?.Width, ["mask"] = metadata?.Mask,
            ["metadata_role"] = metadata?.MetadataRole, ["column_result_format"] = metadata?.ResultFormat,
            ["result_format"] = "unobservable", ["is_bdta"] = null, ["payload_length"] = null, ["payload_sha256"] = null,
            ["synthetic_temporal_hex"] = null, ["layout_family"] = null, ["reason"] = "cache_unobservable" };
        try
        {
            object cache = ReadField(reader, "m_RsCache") ?? throw new InvalidOperationException("cache_missing");
            var rows = ReadField(cache, "datas") as byte[][][]; int offset = (int)ReadField(cache, "datasOffset")!;
            bool bdta = (bool)ReadField(cache, "isRsBdta")!; result["is_bdta"] = bdta; result["result_format"] = bdta ? "bdta" : "ordinary_non_BDTA";
            Need(rows is not null && offset >= 0 && offset < rows.Length && ordinal >= 0 && ordinal + 1 < rows[offset].Length, "raw_cache_position");
            byte[]? bytes = rows![offset][ordinal + 1]; result["payload_length"] = bytes?.Length; result["payload_sha256"] = bytes is null ? null : Hash(bytes);
            string? layout = metadata?.CType switch { 14 => "DATE3", 15 => "TIME5", 22 => "TIME_TZ7", 16 => "TS8", 23 => "TS_TZ10", 26 => "TS9", 27 => "TS_TZ11", _ => null };
            int length = metadata?.CType switch { 14 => 3, 15 => 5, 22 => 7, 16 => 8, 23 => 10, 26 => 9, 27 => 11, _ => 0 };
            if (bdta || metadata?.IsBdta is not false) { result["reason"] = "bdta_or_column_format_unverified"; return result; }
            if (metadata is not { Available: true, Mask: 0, MetadataRole: "result_column", ResultFormat: "ordinary_packed" }) { result["reason"] = "metadata_mask_or_source_unverified"; return result; }
            if (metadata.SemanticScale is not (>= 0 and <= 9) || layout is null || metadata.Width != length || bytes is null || bytes.Length != length)
            { result["reason"] = "unverified_type_scale_width_or_length_no_layout_guess"; return result; }
            result["observable"] = true; result["synthetic_temporal_hex"] = Convert.ToHexString(bytes); result["layout_family"] = layout; result["reason"] = null;
            return result;
        }
        catch { return result; }
    }
    sealed class FakeTemporalCache(byte[] bytes, bool bdta = false)
    {
        public byte[][][] datas = [[[], bytes]];
        public int datasOffset = 0;
        public bool isRsBdta = bdta;
    }
    sealed class FakeTemporalCacheReader(FakeTemporalCache cache) { public FakeTemporalCache m_RsCache = cache; }
    sealed class FakeTemporalColumn(int ctype, int width, int scale, bool bdta = false, int maskValue = 0)
    {
        public int mask = maskValue;
        public bool isBdta = bdta;
        public int GetCType() => ctype;
        public int GetPrecision() => width;
        public int GetScale() => scale;
        public int GetTypeFlag() => 0;
    }

    static void CaptureLiveExecutionSnapshot(Dictionary<string, object?> result, object command, object reader)
    {
        var after = SelectOwnedStatement(command, reader);
        result["snapshot_timing"] = "live_reader_before_drain";
        result["statement_after_owner"] = after.Owner; result["statement_id_after"] = after.Id;
        result["parameter_metadata"] = ParameterMetadata(command, reader).Take(1).ToArray(); result["full_metadata"] = ParameterMetadata(command, reader);
        foreach (var item in EncodingObservation(command, reader)) result[item.Key] = item.Value;
    }
    static Dictionary<string, object?> DrainDmlResults(Func<bool> read, Func<bool> nextResult, Func<int> recordsAffected)
    {
        bool unexpected = false; int rows = 0, results = 0;
        do { while (read()) { unexpected = true; if (++rows > 1024) throw new ProbeInfrastructureException(new InvalidOperationException("metadata_dml_drain_bound")); }
            if (++results > 64) throw new ProbeInfrastructureException(new InvalidOperationException("metadata_dml_result_bound")); }
        while (nextResult());
        return new() { ["drain_completed"] = true, ["records_affected"] = recordsAffected(), ["unexpected_rowset"] = unexpected };
    }
    sealed record NullObservationContext(bool ExpectedDbnull, string ReaderLane, int Ordinal, bool ValueIsDbnull, bool ServerFormatIsDbnull, object Cell)
    {
        internal object PublicEvidence() => new { expected_dbnull = ExpectedDbnull, reader_lane = ReaderLane, ordinal = Ordinal,
            value_is_dbnull = ValueIsDbnull, server_format_is_dbnull = ServerFormatIsDbnull };
        internal bool Valid()
        {
            if (!ExpectedDbnull || ReaderLane is not ("W" or "O") || Ordinal != 0 || !ValueIsDbnull || !ServerFormatIsDbnull) return false;
            var cell = JsonSerializer.SerializeToElement(Cell, Json);
            return cell.TryGetProperty("observable", out var observable) && (observable.ValueKind == JsonValueKind.False
                || observable.ValueKind == JsonValueKind.True && cell.GetProperty("is_null").ValueKind == JsonValueKind.True
                && cell.GetProperty("payload_length").ValueKind == JsonValueKind.Null && cell.GetProperty("payload_sha256").ValueKind == JsonValueKind.Null);
        }
    }
    static bool HasRealNullInfra(Exception error)
    {
        return error is ProbeInfrastructureException or global::W.Dm.DmTimeoutException or TimeoutException or OperationCanceledException or IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException
            || error.InnerException is not null && HasRealNullInfra(error.InnerException);
    }
    static ValueObservation ObserveConfirmedNull(NullObservationContext context, string getterName, Func<object?> getter)
    {
        try
        {
            object? value = getter(); // Always invoke: NULL context alone never produces a claimed getter failure.
            if (context.Valid() && value is null) return new("returned-null", null, null, null, null, null, null, null, context.PublicEvidence());
            if (context.Valid() && value is string { Length: 0 }) return new("returned-string-empty", "System.String", null, null, null, "", null, null, context.PublicEvidence());
            var observation = Observe(() => value);
            return observation with { NullContext = context.PublicEvidence() };
        }
        catch (Exception exception)
        {
            Exception error = exception;
            while (error is TargetInvocationException && error.InnerException is not null) error = error.InnerException;
            var safe = Safe(exception);
            Type? provider = context.ReaderLane == "W" ? typeof(global::W.Dm.DmException) : context.ReaderLane == "O" ? typeof(global::Dm.DmException) : null;
            bool preciseGetter = getterName is "GetString" or "GetDateTime" or "GetDateTimeOffset" or "GetFieldValue<DateTime>" or "GetFieldValue<DateTimeOffset>";
            object? info = error.GetType().GetProperty("FailureInfo", Flags)?.GetValue(error);
            string? Meta(string property) => info?.GetType().GetProperty(property, Flags)?.GetValue(info)?.ToString();
            string? cancel = Meta("CancelSource"), transaction = Meta("TransactionOutcome");
            object? serverNumber = info?.GetType().GetProperty("ServerErrorNumber", Flags)?.GetValue(info);
            bool stableTransaction = transaction is null or "Active" or "Committed" or "RolledBack" or "CompletedExternally";
            bool permittedInfo = info is null || safe.FailureKind == "Transport" && safe.Phase == "Fetch" && safe.Code == "WDM_TRANSPORT"
                && safe.Outcome == "NotSent" && cancel == "None" && stableTransaction && serverNumber is null;
            if (context.Valid() && preciseGetter && provider is not null && provider.IsInstanceOfType(error) && safe.Number == 6081
                && !HasRealNullInfra(exception) && !safe.VerifiedServerResponse && permittedInfo)
            {
                var cached = JsonSerializer.SerializeToElement(context.Cell, Json); bool visible = cached.GetProperty("observable").GetBoolean();
                var receipt = new { observation_class = "known_typed_null_rejection", expected_dbnull = true, reader_lane = context.ReaderLane,
                    ordinal = context.Ordinal, getter_name = getterName, value_is_dbnull = true, server_format_is_dbnull = true,
                    cache_observable = visible, cached_cell = visible ? new { is_null = true, payload_length = (int?)null, payload_sha256 = (string?)null } : null,
                    actual_getter_invoked = true, provider_exception_type = error.GetType().FullName, number = safe.Number,
                    has_real_infra_inner = false, verified_server_response = safe.VerifiedServerResponse, info_present = info is not null,
                    cancel_source = cancel, transaction_outcome = transaction, server_error_number = serverNumber };
                return new("error", null, null, null, null, null, safe, receipt, context.PublicEvidence());
            }
            CheckInfra(exception);
            return new("error", null, null, null, null, null, safe, null, context.PublicEvidence());
        }
    }

    sealed class FakeOwnershipDatabase : IOwnershipDatabase
    {
        internal List<string> Calls { get; } = [];
        internal bool IdentityOk = true, Present, ThrowDrop, FreshFailure, VerifiedCreateFailure, LostCreateAck;
        internal string? Marker;
        internal int Drops, Creates;
        public void VerifyIdentity() { Calls.Add("identity"); Need(IdentityOk, "fake_identity_invalid"); }
        public bool Exists(Ownership _) { Calls.Add("exists"); return Present; }
        public void Create(Ownership o)
        {
            Calls.Add("create"); Creates++;
            if (VerifiedCreateFailure) throw new FakeVerifiedServerException();
            Present = true; Marker = "'" + o.Nonce + "'";
            if (LostCreateAck) throw new IOException("synthetic_ack_lost");
        }
        public string? ReadMarkerDefault(Ownership _) { Calls.Add("marker"); return Marker; }
        public void Drop(Ownership _) { Calls.Add("drop"); Drops++; if (ThrowDrop) throw new IOException("synthetic_cleanup_failure"); Present = false; }
        public bool FreshAbsent(Ownership _) { Calls.Add("fresh_identity"); Need(IdentityOk, "fake_fresh_identity_invalid"); Calls.Add("fresh_exists"); return !Present && !FreshFailure; }
    }
    sealed class FakeVerifiedServerException : Exception { public bool HasVerifiedServerResponse => true; public int Number => -999; }
    sealed class FakeProfileProperties(string version, string effective, string negotiated, int message = 21, short timezone = 480, string encodingOverride = "")
    {
        public string ServerVersion => version;
        public string ServerEncoding => effective;
        public short TimeZone => timezone;
        public object msgVersion = message;
        public object serverEncoding = negotiated;
        public Dictionary<string, object> property = new() { ["encoding"] = encodingOverride };
    }
    sealed class FakeProfileInstance(object props) { public object ConnProperty => props; }
    sealed class FakeProfileConnection(object logical, object? physical)
    {
        internal int LogicalReads;
        public object ConnProperty { get { LogicalReads++; return logical; } }
        public string ServerVersion => "8.1.5.60";
        internal object? GetConnInstance() => physical;
    }
    sealed class FakeProfileMissingMethod(object logical)
    {
        public object ConnProperty => logical;
        public string ServerVersion => "8.1.5.60";
    }
    sealed class FakeBinding { public int EncodedScale => 7; }
    sealed class FakeRawMetadata(string source, int ctype, int precision, int scale)
    {
        public bool HasServerSource => true;
        public string Source => source;
        public int CType => ctype;
        public int Precision => precision;
        public int Scale => scale;
        public int MaskBeforeNormalization => 0;
        public int DerivedMask => 0;
    }
    sealed class FakeField(int flag, string source, int rawType = 26, int rawWidth = 9, int rawScale = 7)
    {
        public int mask = 0;
        public bool isBdta = false;
        public FakeRawMetadata TemporalServerMetadata => new(source, rawType, rawWidth, rawScale);
        public FakeBinding TemporalBinding => new();
        public int GetCType() => 26;
        public int GetPrecision() => 9;
        public int GetScale() => 7;
        public int GetTypeFlag() => flag;
    }
    sealed class FakeInfo(bool fixedDescribe = false) { public object[] GetParamsInfo() => fixedDescribe ? [new FakeField(1, "ParameterDescribe")] : [new FakeField(2, "ParameterDescribe", 2, 32767, 0)]; }
    sealed class FakeStatement(bool fixedDescribe = false) { public FakeInfo H() => new(fixedDescribe); }
    sealed class FakeCommand { public object? m_Stmt; }
    sealed class FakeReader { public object? m_Statement; public bool IsClosed { get; set; } }

    static Exception SyntheticNullException(string kind = "Transport", string outcome = "NotSent", Exception? inner = null, bool verified = false, string? transaction = null)
    {
        var provider = typeof(global::W.Dm.DmException); var assembly = provider.Assembly;
        var errorType = assembly.GetType("W.Dm.DmError")!;
        object error = Activator.CreateInstance(errorType, Flags, null, [6081, "synthetic_null"], CultureInfo.InvariantCulture)!;
        var exception = (Exception)Activator.CreateInstance(provider, Flags, null, [error], CultureInfo.InvariantCulture)!;
        var infoType = assembly.GetType("W.Dm.DmFailureInfo")!;
        object EnumValue(string name, string value) => Enum.Parse(assembly.GetType("W.Dm." + name)!, value);
        object? tx = transaction is null ? null : EnumValue("DmTransactionOutcome", transaction);
        object info = Activator.CreateInstance(infoType, Flags, null, [EnumValue("DmErrorKind", kind), EnumValue("DmFailurePhase", "Fetch"), "WDM_TRANSPORT",
            EnumValue("DmOperationOutcome", outcome), tx, false, EnumValue("DmCancelSource", "None"), null], CultureInfo.InvariantCulture)!;
        provider.GetMethod("SetFailureInfo", Flags)!.Invoke(exception, [info]);
        if (verified) provider.GetProperty("HasVerifiedServerResponse", Flags)!.SetValue(exception, true);
        if (inner is not null) typeof(Exception).GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(exception, inner);
        return exception;
    }

    static int RunOfflineContracts(string[] args)
    {
        var checks = new List<object>(); var cleanupShapes = new List<object>(); bool passed = true;
        void Test(string id, Action action)
        {
            try { action(); checks.Add(new { case_id = id, passed = true }); }
            catch (Exception e) { passed = false; checks.Add(new { case_id = id, passed = false, error_type = e.GetType().FullName }); }
        }
        static bool Rejected(Action action) { try { action(); return false; } catch { return true; } }
        const string nonce = "ABCDEF0123456789ABCDEF0123456789";
        Need(nonce.Length == 32 && System.Text.RegularExpressions.Regex.IsMatch(nonce, "^[A-F0-9]{32}$"), "offline_fixture_nonce_must_be_32_hex");
        var original = new Ownership(1, nonce, "TPN_" + nonce[..24], "shared", "8.1.5.60", new string('A', 64), new string('B', 64), true, true, "OWN_" + nonce[..24], "confirmed");
        void CleanupCase(string id, Ownership owner, FakeOwnershipDatabase fake, bool mustReject, int drops, Func<Ownership>? parse = null)
        {
            Test(id, () => {
                bool rejected = Rejected(() => CleanupWithOwnership(parse is null ? owner : parse(), fake, "shared", "8.1.5.60", new string('A', 64), new string('B', 64)));
                Need(rejected == mustReject && fake.Drops == drops, "offline_cleanup_branch");
                cleanupShapes.Add(new { case_id = id, outcome = rejected ? "rejected" : "cleaned", calls = fake.Calls.ToArray(), drop_count = fake.Drops });
            });
        }
        try
        {
            Need(args.Length == 3 && !File.Exists(args[1]) && !File.Exists(args[2]), "offline_new_outputs_required");
            Test("sample_exact_CLR_types", () => {
                object plain = TemporalSample(false, 1234560), offset = TemporalSample(true, 1234560);
                Need(plain.GetType() == typeof(DateTime) && plain is DateTime { Kind: DateTimeKind.Unspecified } dt && dt.Ticks == 639204129161234560L, "offline_DateTime_sample");
                Need(offset.GetType() == typeof(DateTimeOffset) && offset is DateTimeOffset dto && dto.Ticks == 639204129161234560L && dto.Offset == TimeSpan.FromMinutes(330), "offline_DTO_sample");
            });
            Test("fixed_target_four_profile_type_combinations", () => {
                Need(FixedTargetColumn("shared", false) == "D7" && FixedTargetColumn("shared", true) == "Z7"
                    && FixedTargetColumn("tls", false) == "D6" && FixedTargetColumn("tls", true) == "Z6", "offline_fixed_target_rule");
            });
            var fixedTargets = new[] {
                new { profile = "shared", tz = false, target_column = FixedTargetColumn("shared", false) },
                new { profile = "shared", tz = true, target_column = FixedTargetColumn("shared", true) },
                new { profile = "tls", tz = false, target_column = FixedTargetColumn("tls", false) },
                new { profile = "tls", tz = true, target_column = FixedTargetColumn("tls", true) } };
            var logicalProfile = new FakeProfileProperties("9.9.9.9", "gb18030", "gb18030", 19, -300);
            var physicalProfile = new FakeProfileProperties("8.1.5.60", "UTF-8", "utf-8");
            var divergentConnection = new FakeProfileConnection(logicalProfile, new FakeProfileInstance(physicalProfile));
            var capturedProfile = CapturePhysicalProfile(divergentConnection, User, User);
            Test("physical_profile_divergent_logical_ignored", () => Need(capturedProfile.ServerVersion == "8.1.5.60"
                && capturedProfile.Encoding == "utf-8" && capturedProfile.NegotiatedEncoding == "utf-8" && capturedProfile.MessageVersion == 21
                && capturedProfile.Timezone == 480 && capturedProfile.EncodingOverrideEmpty && divergentConnection.LogicalReads == 0
                && capturedProfile.ProfileOrigin == "physical_get_conn_instance_conn_property"
                && capturedProfile.NegotiatedEncodingOrigin == "physical_conn_property_serverEncoding_field", "offline_physical_profile_capture"));
            var profileRejections = new List<object>();
            void ProfileReject(string id, object connection)
            {
                bool rejected = Rejected(() => CapturePhysicalProfile(connection, User, User));
                Test("physical_profile_" + id, () => Need(rejected, "offline_physical_profile_must_reject"));
                profileRejections.Add(new { case_id = id, capture_rejected = rejected });
            }
            ProfileReject("missing_method", new FakeProfileMissingMethod(logicalProfile));
            ProfileReject("missing_instance", new FakeProfileConnection(logicalProfile, null));
            ProfileReject("missing_property", new FakeProfileConnection(logicalProfile, new object()));
            ProfileReject("nonempty_override", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(new FakeProfileProperties("8.1.5.60", "utf-8", "utf-8", encodingOverride: "utf-8"))));
            ProfileReject("encoding_conflict", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(new FakeProfileProperties("8.1.5.60", "gb18030", "utf-8"))));
            ProfileReject("encoding_unobservable", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(new FakeProfileProperties("8.1.5.60", "unobservable", "utf-8"))));
            var badMessage = new FakeProfileProperties("8.1.5.60", "utf-8", "utf-8") { msgVersion = true };
            ProfileReject("message_not_integer", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(badMessage)));
            var missingOverride = new FakeProfileProperties("8.1.5.60", "utf-8", "utf-8"); missingOverride.property.Remove("encoding");
            ProfileReject("override_unobservable", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(missingOverride)));
            ProfileReject("version_invalid", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(new FakeProfileProperties("not_a_version", "utf-8", "utf-8"))));
            ProfileReject("public_version_conflict", new FakeProfileConnection(logicalProfile, new FakeProfileInstance(new FakeProfileProperties("8.1.4.6", "utf-8", "utf-8"))));
            var profileCapture = new { logical_input = new { server_version = "9.9.9.9", encoding = "gb18030", message_version = 19, timezone = -300 },
                captured_profile = capturedProfile, logical_reads = divergentConnection.LogicalReads, rejections = profileRejections };
            Test("preexisting", () => {
                var fake = new FakeOwnershipDatabase { Present = true }; int records = 0;
                Need(Rejected(() => CreateWithLifecycle(original with { CreationState = "reserved" }, fake, _ => records++)) && records == 0 && fake.Creates == 0 && fake.Drops == 0, "offline_preexisting_no_create_drop");
                cleanupShapes.Add(new { case_id = "preexisting", outcome = "rejected", calls = fake.Calls.ToArray(), drop_count = fake.Drops });
            });
            Test("known_create_failure", () => {
                var fake = new FakeOwnershipDatabase { VerifiedCreateFailure = true }; Ownership state = original;
                Need(Rejected(() => CreateWithLifecycle(original with { CreationState = "reserved" }, fake, o => state = o)) && state.CreationState == "known_create_failure", "offline_known_creation_state");
                fake.Present = true; fake.Marker = "'" + nonce + "'";
                Need(Rejected(() => CleanupWithOwnership(state, fake, "shared", "8.1.5.60", new string('A', 64), new string('B', 64))) && fake.Drops == 0 && !fake.Calls.Contains("marker"), "offline_known_failure_never_drop");
                cleanupShapes.Add(new { case_id = "known_create_failure", outcome = "rejected", calls = fake.Calls.ToArray(), drop_count = fake.Drops });
            });
            Test("lost_ack_true_marker", () => {
                var fake = new FakeOwnershipDatabase { LostCreateAck = true }; Ownership state = original;
                Need(Rejected(() => CreateWithLifecycle(original with { CreationState = "reserved" }, fake, o => state = o)) && state.CreationState == "create_inflight", "offline_lost_ack_state");
                CleanupWithOwnership(state, fake, "shared", "8.1.5.60", new string('A', 64), new string('B', 64));
                Need(fake.Drops == 1 && !fake.Present, "offline_lost_ack_exact_recovery");
                cleanupShapes.Add(new { case_id = "lost_ack_true_marker", outcome = "cleaned", calls = fake.Calls.ToArray(), drop_count = fake.Drops });
            });
            CleanupCase("missing_marker", original, new() { Present = true }, true, 0);
            CleanupCase("wrong_marker", original, new() { Present = true, Marker = "'WRONG'" }, true, 0);
            CleanupCase("same_prefix_wrong_full_nonce", original, new() { Present = true, Marker = "'" + nonce[..24] + "00000000'" }, true, 0);
            CleanupCase("bad_profile", original with { Profile = "tls" }, new() { Present = true }, true, 0);
            CleanupCase("bad_package", original with { OwnerPackageSha256 = new string('C', 64) }, new() { Present = true }, true, 0);
            CleanupCase("bad_identity", original with { IdentitySha256 = new string('C', 64) }, new() { Present = true }, true, 0);
            CleanupCase("bad_nonce", original with { Nonce = "bad" }, new() { Present = true }, true, 0);
            CleanupCase("duplicate_manifest", original, new() { Present = true }, true, 0, () => ParseOwnershipJson("{\"schema_version\":1,\"schema_version\":1}"));
            CleanupCase("malformed_manifest", original, new() { Present = true }, true, 0, () => ParseOwnershipJson("{broken"));
            CleanupCase("reserved_present", original with { CreationState = "reserved" }, new() { Present = true, Marker = "'" + nonce + "'" }, true, 0);
            CleanupCase("reserved_absent", original with { CreationState = "reserved" }, new(), false, 0);
            CleanupCase("inflight_present", original with { CreationState = "create_inflight" }, new() { Present = true, Marker = " ( '" + nonce + "' ) " }, false, 1);
            CleanupCase("confirmed_present", original, new() { Present = true, Marker = "'" + nonce + "'" }, false, 1);
            CleanupCase("wrong_db_identity", original, new() { IdentityOk = false, Present = true, Marker = "'" + nonce + "'" }, true, 0);
            CleanupCase("cleanup_failure", original, new() { Present = true, Marker = "'" + nonce + "'", ThrowDrop = true }, true, 1);
            CleanupCase("fresh_absence_failure", original, new() { Present = true, Marker = "'" + nonce + "'", FreshFailure = true }, true, 1);
            var statement = new FakeStatement(); var command = new FakeCommand { m_Stmt = statement }; var reader = new FakeReader();
            object before = StatementObservation(command);
            command.m_Stmt = null; reader.m_Statement = statement;
            object after = StatementObservation(command, reader);
            reader.IsClosed = true; object closed = StatementObservation(command, reader);
            Test("reader_owned_statement_actual_selection", () => {
                var first = SelectOwnedStatement(new FakeCommand { m_Stmt = statement }); reader.IsClosed = false;
                var live = SelectOwnedStatement(command, reader); reader.IsClosed = true; var stale = SelectOwnedStatement(command, reader);
                Need(first.Owner == "command" && live.Owner == "reader" && first.Id == live.Id && stale.Statement is null && stale.Id is null, "offline_reader_statement_ownership");
                Need(ParameterMetadata(command, reader).Length == 0 && EncodingObservation(command, reader)["encoded_scale_observable"] is false, "offline_no_stale_snapshot");
            });
            Test("rejection_selects_fresh_recovery", () => {
                var failure = NewPhaseRecord("replacement", null, TemporalSample(false, 1234567), null, null); failure["status"] = "driver_parameter_rejected";
                var success = NewPhaseRecord("replacement", null, TemporalSample(false, 1234560), null, null); success["status"] = "observed";
                Need(RequiresFreshRecovery(failure) && !RequiresFreshRecovery(success), "offline_recovery_policy");
            });
            var emptyCell = new { observable = true, is_null = true, payload_length = (int?)null, payload_sha256 = (string?)null };
            var nullContext = new NullObservationContext(true, "W", 0, true, true, emptyCell);
            int knownCalls = 0; var known = ObserveConfirmedNull(nullContext, "GetString", () => { knownCalls++; throw SyntheticNullException(); });
            Test("known_null_actual_getter", () => { Need(knownCalls == 1 && known.KnownNullReceipt is not null && known.Error is { Number: 6081, FailureClass: "infra_failure", FailureKind: "Transport", Phase: "Fetch", Code: "WDM_TRANSPORT", Outcome: "NotSent" }, "offline_known_null_receipt_preserves_original"); });
            int nullCalls = 0, emptyCalls = 0;
            var officialContext = nullContext with { ReaderLane = "O" };
            var officialNull = ObserveConfirmedNull(officialContext, "GetString", () => { nullCalls++; return null; });
            var officialEmpty = ObserveConfirmedNull(officialContext, "GetString", () => { emptyCalls++; return ""; });
            Test("official_null_empty_actual_return", () => Need(nullCalls == 1 && emptyCalls == 1 && officialNull.Outcome == "returned-null" && officialEmpty.Outcome == "returned-string-empty" && officialEmpty.Text == "" && officialNull.Error is null && officialEmpty.Error is null, "offline_official_null_return"));
            var nullNegatives = new List<object>();
            void NullNegative(string id, NullObservationContext context, Exception error, string getter = "GetString")
            {
                int calls = 0; bool receipt = false;
                try { receipt = ObserveConfirmedNull(context, getter, () => { calls++; throw error; }).KnownNullReceipt is not null; } catch { }
                Test(id, () => Need(calls == 1 && !receipt, "offline_null_exception_must_not_be_exempt"));
                nullNegatives.Add(new { case_id = id, receipt_granted = receipt, getter_calls = calls });
            }
            NullNegative("non_null_6081", nullContext with { ValueIsDbnull = false }, SyntheticNullException());
            NullNegative("expected_nonnull_6081", nullContext with { ExpectedDbnull = false }, SyntheticNullException());
            NullNegative("wrong_ordinal_6081", nullContext with { Ordinal = 1 }, SyntheticNullException());
            NullNegative("wrong_lane_6081", nullContext with { ReaderLane = "O" }, SyntheticNullException());
            NullNegative("missing_cache_evidence", nullContext with { Cell = new { absent = true } }, SyntheticNullException());
            NullNegative("contradictory_cache", nullContext with { Cell = new { observable = true, is_null = false, payload_length = 9, payload_sha256 = new string('A', 64) } }, SyntheticNullException());
            NullNegative("timeout_inner", nullContext, SyntheticNullException(inner: new TimeoutException()));
            NullNegative("socket_inner", nullContext, SyntheticNullException(inner: new System.Net.Sockets.SocketException()));
            NullNegative("cancel_inner", nullContext, SyntheticNullException(inner: new OperationCanceledException()));
            NullNegative("unknown_outcome", nullContext, SyntheticNullException(outcome: "Unknown"));
            NullNegative("unknown_kind", nullContext, SyntheticNullException(kind: "Unknown"));
            NullNegative("verified_server_error", nullContext, SyntheticNullException(verified: true));
            NullNegative("transaction_unknown", nullContext, SyntheticNullException(transaction: "OutcomeUnknown"));
            NullNegative("getvalue_6081", nullContext, SyntheticNullException(), "GetValue");
            int dbnullCalls = 0; var dbnull = ObserveConfirmedNull(nullContext, "GetValue", () => { dbnullCalls++; return DBNull.Value; });
            Test("actual_getvalue_dbnull", () => Need(dbnullCalls == 1 && dbnull.Outcome == "null" && dbnull.Type == "System.DBNull" && dbnull.KnownNullReceipt is null, "offline_dbnull_value_contract"));
            var fixedStatement = new FakeStatement(true); var fixedCommand = new FakeCommand { m_Stmt = fixedStatement }; var fixedReader = new FakeReader { m_Statement = fixedStatement };
            var fixedDml = NewPhaseRecord("initial", 1, TemporalSample(false, 1234560), 7, null); var fixedBefore = SelectOwnedStatement(fixedCommand);
            fixedDml["execution_api"] = "ExecuteReader"; fixedDml["statement_before_owner"] = fixedBefore.Owner; fixedDml["statement_id_before"] = fixedBefore.Id;
            fixedCommand.m_Stmt = null; CaptureLiveExecutionSnapshot(fixedDml, fixedCommand, fixedReader);
            int dmlReads = 0, dmlNexts = 0;
            foreach (var item in DrainDmlResults(() => { dmlReads++; return false; }, () => { dmlNexts++; return false; }, () => 1)) fixedDml[item.Key] = item.Value;
            fixedReader.IsClosed = true; var fixedClosed = StatementObservation(fixedCommand, fixedReader);
            Test("fixed_dml_live_reader_snapshot_and_drain", () => Need(fixedDml["statement_after_owner"] is "reader" && (int?)fixedDml["statement_id_after"] == fixedBefore.Id && fixedDml["encoded_scale_observable"] is true
                && fixedDml["drain_completed"] is true && fixedDml["records_affected"] is 1 && fixedDml["unexpected_rowset"] is false && dmlReads == 1 && dmlNexts == 1 && SelectOwnedStatement(fixedCommand, fixedReader).Statement is null, "offline_fixed_reader_observation"));
            var rawSamples = new List<object>();
            void RawSample(string id, string hex, int ctype, int width, bool bdta = false, int mask = 0)
            {
                var input = Convert.FromHexString(hex); var cache = new FakeTemporalCache(input, bdta); var reader = new FakeTemporalCacheReader(cache);
                var metadata = Capture(new FakeTemporalColumn(ctype, width, 7, bdta, mask), "result_column");
                var raw = RawTemporalOracle(reader, 0, metadata); rawSamples.Add(new { case_id = id, metadata, raw_oracle = raw });
                Test(id, () => { bool allowed = !bdta && mask == 0 && (ctype == 26 && input.Length == 9 || ctype == 27 && input.Length == 11);
                    Need((raw["observable"] is true) == allowed && (allowed ? raw["synthetic_temporal_hex"] is string value && value == hex && raw["payload_sha256"] is string hash && hash == Hash(input) : raw["synthetic_temporal_hex"] is null), "offline_raw_actual_cache"); });
            }
            RawSample("DT9_tick1", "EA87BBEE81C8000000", 26, 9);
            RawSample("TZ11_offset0_tick1", "EA87BBEE81C80000000000", 27, 11);
            RawSample("TZ11_offset480_tick1", "EA87BBEE81C8000000E001", 27, 11);
            RawSample("TZ11_offsetN300_tick1", "EA87BBEE81C8000000D4FE", 27, 11);
            RawSample("TZ11_offset330_tick1", "EA87BBEE81C80000004A01", 27, 11);
            RawSample("unknown12_closed", "EA87BBEE81C8000000000000", 26, 9);
            RawSample("bdta_closed", "EA87BBEE81C8000000", 26, 9, true);
            RawSample("type_length_mismatch_closed", "EA87BBEE81C8000000", 27, 11);
            RawSample("mask4_closed", "EA87BBEE81C8000000", 26, 9, false, 4);
            var mutationBytes = Convert.FromHexString("EA87BBEE81C8000000"); var mutationReader = new FakeTemporalCacheReader(new FakeTemporalCache(mutationBytes));
            var mutationMetadata = Capture(new FakeTemporalColumn(26, 9, 7), "result_column");
            var rawBefore = RawTemporalOracle(mutationReader, 0, mutationMetadata); mutationBytes[5] ^= 2; var rawAfter = RawTemporalOracle(mutationReader, 0, mutationMetadata);
            Test("raw_cache_mutation_is_observed", () => Need(rawBefore["payload_sha256"] is string first && rawAfter["payload_sha256"] is string second && first != second
                && rawBefore["synthetic_temporal_hex"] is string beforeHex && beforeHex == "EA87BBEE81C8000000" && rawAfter["synthetic_temporal_hex"] is string afterHex && afterHex == "EA87BBEE81CA000000", "offline_raw_cache_not_expected_reconstruction"));
            var rejection = NewPhaseRecord("initial", null, TemporalSample(false, 1234560), 6, 7);
            rejection["status"] = "driver_parameter_rejected"; rejection["error"] = Safe(new NotSupportedException()); rejection["failure_class"] = "user_rejection"; rejection["failure_stage"] = "parameter_configuration";
            var shape = new { schema_version = 1, task = "TP-01-offline-shape", synthetic = true, expected_only = true, database_accessed = false,
                physical_profile_capture = profileCapture,
                samples = new[] { new { case_id = "sample_dt", observation = Observe(() => TemporalSample(false, 1234560)) }, new { case_id = "sample_dto", observation = Observe(() => TemporalSample(true, 1234560)) } },
                metadata = new { result_column = Capture(new FakeField(0, "ResultColumn"), "result_column"), fixed_parameter = Capture(new FakeField(1, "ParameterDescribe"), "parameter_describe"),
                    advisory_parameter = Capture(new FakeField(2, "ParameterDescribe", 2, 32767, 0), "parameter_describe") },
                raw_oracle_samples = rawSamples, raw_cache_mutation = new { before = rawBefore, after = rawAfter },
                null_observations = new { known_w6081 = new { observation = known, getter_calls = knownCalls }, official_null = new { observation = officialNull, getter_calls = nullCalls }, official_empty = new { observation = officialEmpty, getter_calls = emptyCalls },
                    getvalue_dbnull = new { observation = dbnull, getter_calls = dbnullCalls }, negative = nullNegatives }, fixed_dml_observation = fixedDml, fixed_dml_after_close = fixedClosed,
                fixed_targets = fixedTargets, rejections = new[] { rejection }, statement_ownership = new { before, after_live = after, after_close = closed }, cleanup_cases = cleanupShapes };
            Write(args[2], shape);
            Write(args[1], new { schema_version = 1, task = "TP-01-offline-contracts", expected_only = true, database_accessed = false,
                status = passed ? "offline_contracts_passed" : "offline_contracts_failed", cases = checks, shape_sha256 = Hash(File.ReadAllBytes(args[2])) });
            return passed ? 0 : 1;
        }
        catch (Exception e)
        {
            if (args.Length > 1 && !File.Exists(args[1])) Write(args[1], new { schema_version = 1, task = "TP-01-offline-contracts", expected_only = true, database_accessed = false,
                status = "offline_contracts_failed", error_type = e.GetType().FullName });
            return 1;
        }
    }
}
