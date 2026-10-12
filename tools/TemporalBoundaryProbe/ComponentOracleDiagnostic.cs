using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;

// Explicit read-only characterization. No ownership, DDL, DML or session mutation.
internal static partial class Program
{
    static void RunComponentDiagnostic()
    {
        Report["task"] = "TP-01-components-diagnostic";
        Report["evidence_role"] = "CAST_conversion_characterization_read_only";
        Report["read_only"] = true; Report["no_ownership_objects"] = true;
        Report["ddl_dml_session_statements"] = 0;
        var vectors = new List<(string Id, DateTimeOffset Value)>();
        foreach (int offset in new[] { 0, 480, -300, 330 })
        foreach (long fraction in new[] { 1L, 1234567L, 1234560L })
            vectors.Add(($"ordinary_o{offset.ToString(CultureInfo.InvariantCulture).Replace('-', 'N')}_f{fraction}", new DateTimeOffset(Ordinary.AddTicks(fraction), TimeSpan.FromMinutes(offset))));
        vectors.Add(("max", new DateTimeOffset(DateTime.MaxValue, TimeSpan.Zero)));
        vectors.Add(("max_minus1", new DateTimeOffset(DateTime.MaxValue.AddTicks(-1), TimeSpan.Zero)));
        vectors.Add(("max_minus9", new DateTimeOffset(DateTime.MaxValue.AddTicks(-9), TimeSpan.Zero)));
        vectors.Add(("leap_last_tick", new DateTimeOffset(new DateTime(2000, 2, 29, 23, 59, 59).AddTicks(9999999), TimeSpan.Zero)));
        foreach (string lane in new[] { "O", "W" })
        foreach (var vector in vectors)
        {
            // Every query obtains its own verified TEST connection and repeats the public source contract.
            var source = DiagnosticTemporal(lane, vector.Id, vector.Value, false);
            var cast = DiagnosticTemporal(lane, vector.Id, vector.Value, true);
            var originalComponents = DiagnosticNumeric(lane, vector.Id, vector.Value, false);
            var castComponents = DiagnosticNumeric(lane, vector.Id, vector.Value, true);
            bool sourceComplete = source.GetValueOrDefault("status") is "observed" && source.GetValueOrDefault("subsequent_read_completion") is true;
            bool castComplete = cast.GetValueOrDefault("status") is "observed" && cast.GetValueOrDefault("subsequent_read_completion") is true;
            bool q2Complete = castComponents.GetValueOrDefault("status") is "observed" && castComponents.GetValueOrDefault("subsequent_read_completion") is true;
            bool sourceExact = sourceComplete && source.GetValueOrDefault("source_exact") is true;
            bool offsetExact = sourceComplete && source.GetValueOrDefault("source_offset_exact") is true;
            bool castLocal = castComplete && cast.GetValueOrDefault("cast_local_exact") is true;
            bool castFraction = castComplete && cast.GetValueOrDefault("cast_fraction_exact") is true;
            bool q2Exact = q2Complete && castComponents.GetValueOrDefault("numeric_exact") is true;
            Add(lane, vector.Id + "_summary", "component_diagnostic_summary", new() {
                ["status"] = "observed", ["expected"] = Observe(() => vector.Value),
                ["source_exact"] = sourceExact, ["source_offset_exact"] = offsetExact,
                ["source_status"] = source.GetValueOrDefault("status") is "failed" ? "source_rejected" : !sourceComplete ? "source_incomplete" : sourceExact && offsetExact ? "source_exact" : "source_not_exact",
                ["cast_validation_status"] = cast.GetValueOrDefault("status") is "failed" ? "cast_rejected" : !castComplete ? "cast_incomplete" : castLocal && castFraction ? "cast_same_local_verified" : "cast_not_exact",
                ["cast_local_exact"] = castLocal, ["cast_fraction_exact"] = castFraction,
                ["basis"] = sourceExact && offsetExact && castLocal && castFraction ? "same_local_fields_verified" : "unverified",
                ["Q1_numeric_exact"] = originalComponents.GetValueOrDefault("numeric_exact") is true,
                ["Q2_numeric_exact"] = q2Exact,
                ["Q2_oracle_classification"] = !sourceExact || !offsetExact || !castLocal || !castFraction ? "source_unverified_not_projection_acceptance"
                    : q2Exact ? "projection_exact" : "projection_failed_or_not_exact",
                ["Q2_oracle_usable"] = sourceExact && offsetExact && castLocal && castFraction && q2Exact });
        }
        Report["status"] = "diagnostic_complete";
    }
    static string DiagnosticSource(string lane) => lane == "O"
        ? "(SELECT CAST(:v AS DATETIME(7) WITH TIME ZONE) AS Z FROM DUAL)"
        : "(SELECT CAST(CAST(:txt AS VARCHAR(80)) AS DATETIME(7) WITH TIME ZONE) AS Z FROM DUAL)";
    static void ConfigureDiagnosticParameter(DbCommand command, string lane, DateTimeOffset value)
    {
        var p = command.CreateParameter();
        if (lane == "O") { p.ParameterName = "v"; p.DbType = DbType.DateTimeOffset; p.Precision = 7; p.Value = value; }
        else { p.ParameterName = "txt"; p.DbType = DbType.String; p.Size = 80; p.Value = value.ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture); }
        command.Parameters.Add(p);
    }
    static Dictionary<string, object?> DiagnosticTemporal(string lane, string id, DateTimeOffset expected, bool cast)
    {
        string query = cast ? "Q0b" : "Q0a"; string expression = cast ? "CAST(Z AS TIMESTAMP(9))" : "Z";
        string format = cast ? "YYYY-MM-DD HH24:MI:SS.FF9" : "YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM";
        var result = new Dictionary<string, object?> { ["query"] = query, ["expected"] = Observe(() => expected),
            ["source_kind"] = lane == "O" ? "official_DTO_parameter_Precision7" : "VARCHAR80_to_TZ7_CAST_not_typed_write" };
        Stage = lane + ":" + id + "_" + query;
        try
        {
            using var c = Open(lane); using var cmd = Command(c, $"SELECT {expression},TO_CHAR({expression},'{format}') FROM {DiagnosticSource(lane)}");
            ConfigureDiagnosticParameter(cmd, lane, expected); cmd.Prepare();
            result["parameter_metadata_before_execute"] = ParameterMetadata(cmd);
            using var r = cmd.ExecuteReader(); Need(r.Read(), "diagnostic_source_row");
            result["parameter_metadata"] = ParameterMetadata(cmd, r); result["metadata"] = Columns(r).Take(1).ToArray();
            result["schema_scales"] = SchemaScales(r).Take(1).ToArray(); result["cell"] = Cell(r, 0);
            string actual = SafeTemporalText(r.GetString(1)); var fields = ParseFields(actual);
            result["server_text"] = actual; result["actual_server"] = ActualServer(actual, Columns(r).FirstOrDefault()?.SemanticScale);
            result["get_value"] = Observe(() => r.GetValue(0)); result["get_string"] = Observe(() => r.GetString(0));
            result["typed"] = cast ? Observe(() => r.GetDateTime(0)) : Observe(() => Invoke(r, "GetDateTimeOffset", 0));
            var expectedFields = ParseFields(ExpectedText(cast ? (object)expected.DateTime : expected, 9));
            result[cast ? "cast_local_exact" : "source_exact"] = fields is not null && fields == expectedFields;
            result[cast ? "cast_fraction_exact" : "source_offset_exact"] = fields is not null && (cast ? fields.Nanoseconds == expectedFields?.Nanoseconds : fields.OffsetMinutes == (int)expected.Offset.TotalMinutes);
            result["subsequent_read_completion"] = !r.Read(); result["status"] = "observed";
        }
        catch (Exception e) { CheckInfra(e); result["status"] = "failed"; result["error"] = Safe(e); result["failure_class"] = Safe(e).FailureClass; }
        Add(lane, id + "_" + query, "component_diagnostic_temporal", result); return result;
    }
    static Dictionary<string, object?> DiagnosticNumeric(string lane, string id, DateTimeOffset expected, bool cast)
    {
        string query = cast ? "Q2" : "Q1", expression = cast ? "CAST(Z AS TIMESTAMP(9))" : "Z";
        var result = new Dictionary<string, object?> { ["query"] = query, ["expected"] = Observe(() => expected) };
        Stage = lane + ":" + id + "_" + query;
        try
        {
            string[] parts = ["YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND"];
            string projections = string.Join(',', parts.Select(part => $"EXTRACT({part} FROM {expression})"));
            using var c = Open(lane); using var cmd = Command(c, $"SELECT {projections} FROM {DiagnosticSource(lane)}");
            ConfigureDiagnosticParameter(cmd, lane, expected); cmd.Prepare();
            result["parameter_metadata_before_execute"] = ParameterMetadata(cmd);
            using var r = cmd.ExecuteReader(); Need(r.Read(), "diagnostic_component_row");
            result["parameter_metadata"] = ParameterMetadata(cmd, r);
            var metadata = Columns(r); var schema = r.GetSchemaTable(); var observations = new List<object>(); var actual = new decimal?[6]; var typedActual = new decimal?[6];
            for (int i = 0; i < parts.Length; i++)
            {
                var value = DiagnosticDecimal(() => r.GetValue(i)); var getter = DiagnosticDecimal(() => r.GetDecimal(i));
                actual[i] = value.DecimalValue; typedActual[i] = getter.DecimalValue;
                observations.Add(new { component = parts[i], get_value = value, get_decimal = getter,
                    metadata = i < metadata.Length ? metadata[i] : null,
                    numeric_precision = schema is not null && i < schema.Rows.Count && schema.Rows[i]["NumericPrecision"] is not DBNull ? (int?)Convert.ToInt32(schema.Rows[i]["NumericPrecision"], CultureInfo.InvariantCulture) : null,
                    numeric_scale = schema is not null && i < schema.Rows.Count && schema.Rows[i]["NumericScale"] is not DBNull ? (int?)Convert.ToInt32(schema.Rows[i]["NumericScale"], CultureInfo.InvariantCulture) : null,
                    cell = DiagnosticNumericCell(r, i, i < metadata.Length ? metadata[i].CType : null) });
            }
            DateTime local = expected.DateTime;
            decimal second = local.Second + (local.Ticks % TimeSpan.TicksPerSecond) / (decimal)TimeSpan.TicksPerSecond;
            decimal[] wanted = [local.Year, local.Month, local.Day, local.Hour, local.Minute, second];
            bool valueExact = actual.Select((value, i) => value.HasValue && value.Value == wanted[i]).All(v => v);
            bool decimalExact = typedActual.Select((value, i) => value.HasValue && value.Value == wanted[i]).All(v => v);
            result["components"] = observations; result["get_value_exact"] = valueExact; result["get_decimal_exact"] = decimalExact; result["numeric_exact"] = valueExact && decimalExact;
            result["subsequent_read_completion"] = !r.Read(); result["status"] = "observed";
        }
        catch (Exception e) { CheckInfra(e); result["status"] = "failed"; result["error"] = Safe(e); result["failure_class"] = Safe(e).FailureClass; }
        Add(lane, id + "_" + query, "component_diagnostic_numeric", result); return result;
    }
    sealed record NumericObservation(string Outcome, string? ClrType, decimal? DecimalValue, ErrorInfo? Error);
    static NumericObservation DiagnosticDecimal(Func<object?> getter)
    {
        try
        {
            object? value = getter();
            return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                ? new("returned", value.GetType().FullName, Convert.ToDecimal(value, CultureInfo.InvariantCulture), null)
                : new("unsupported_type", value?.GetType().FullName, null, null);
        }
        catch (Exception e) { CheckInfra(e); return new("error", null, null, Safe(e)); }
    }
    static object DiagnosticNumericCell(DbDataReader reader, int ordinal, int? ctype)
    {
        object observation = Cell(reader, ordinal);
        string? hex = null;
        if (ctype is 5 or 6 or 7 or 8 or 9 or 10 or 11 or 24)
        {
            try
            {
                object cache = ReadField(reader, "m_RsCache")!;
                var rows = ReadField(cache, "datas") as byte[][][]; int offset = (int)ReadField(cache, "datasOffset")!;
                byte[]? bytes = rows![offset][ordinal + 1]; if (bytes is { Length: <= 64 }) hex = Convert.ToHexString(bytes);
            }
            catch { }
        }
        return new { observation, synthetic_numeric_hex = hex };
    }
}
