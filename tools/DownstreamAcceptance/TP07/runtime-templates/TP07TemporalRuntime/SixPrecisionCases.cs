using System.Buffers.Binary;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace TP07.Runtime;

internal static class SixPrecisionCases
{
    internal static async Task Run(RuntimeHarness harness, RuntimeCase @case, Dictionary<string, object?> item)
    {
        ModelSpec spec = await harness.Create(@case, item);
        await using var context = harness.Context(spec);
        if (@case.Shape == "advisory7") { await Advisory(harness, @case, context, item); return; }
        await harness.Step(item, "open_EF_factory_connection", async () =>
        { if (@case.Async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection(); return true; });
        string property = @case.Kind switch { "dt" => "D", "dto" => "Z", "date" when @case.Shape == "DATE_nonmidnight" => "D", "date" => "A", "time" => "T", _ => "I" };
        var mapping = context.Mapping(property);
        item["actual_mapping"] = new { mapping = mapping.GetType().FullName, mapping.StoreType, model_precision = mapping.Precision, model_scale = mapping.Scale };
        if (@case.Kind is "dt" or "dto") RuntimeAssert.Require(mapping.Precision == (@case.Shape == "default7_fixed6" ? 7 : 6), "six_model_precision_changed");
        var row = new TemporalRow { Id = 1, N = "first" };
        if (@case.Kind is "dt" or "dto") RoundtripCases.Set(@case, row);
        else if (@case.Shape == "DATE_nonmidnight") row.D = @case.DateTimeValue;
        else if (@case.Kind == "date") row.A = @case.Ticks.HasValue ? DateOnly.FromDateTime(@case.DateTimeValue) : null;
        else if (@case.Kind == "time") row.T = @case.Ticks.HasValue ? new TimeOnly(@case.Ticks.Value) : null;
        else row.I = @case.Ticks.HasValue ? TimeSpan.FromTicks(@case.Ticks.Value) : null;
        context.Rows.Add(row);
        ModelSpec freshSpec = spec with { Shape = @case.Shape == "DATE_nonmidnight" ? "native" : spec.Shape };
        RuntimeHarness.OwnedState before = await harness.FreshState(freshSpec, @case.Async, item, "fresh_state_before_SaveChanges", 0);
        Type? refusalType = @case.Expected.StartsWith("Overflow", StringComparison.Ordinal) ? typeof(OverflowException)
            : @case.Expected.StartsWith("Argument", StringComparison.Ordinal) ? typeof(ArgumentException) : null;
        bool rejection = refusalType != null;
        bool actualDescribeBranch = @case.Shape == "default7_fixed6";
        Exception? refused = null;
        int? saved = null;
        try
        {
            saved = await harness.Step(item, "SaveChanges", () => RuntimeHarness.Save(context, @case.Async), result => new { affected_entities = result });
        }
        catch (Exception error) when (rejection || actualDescribeBranch) { refused = error; }
        if (actualDescribeBranch)
        {
            refusalType = FixedSixCommandExpectation(harness, @case, item);
            rejection = refusalType != null;
        }
        RuntimeAssert.Require(rejection ? refused != null && saved == null : refused == null && saved == 1, "six_expected_write_result");
        if (rejection)
        {
            RuntimeAssert.Require(refused != null, "six_missing_precision_refusal");
            item["actual_exception_chain"] = RuntimeRefusal.Evidence(refused!);
            RuntimeAssert.Require(RuntimeRefusal.Matches(refused!, refusalType!), "six_refusal_exception_contract_mismatch");
            item["old_connection_state_after_refusal"] = context.Database.GetDbConnection().State.ToString();
            item["expected_refusal"] = new { actual_chain = RuntimeRefusal.Evidence(refused!), expected_leaf_type = refusalType!.FullName,
                send_evidence = harness.Audit.RefusalSendEvidence(@case.CaseId + "/SaveChanges", @case.Async),
                old_context_reuse_not_required = true };
            RuntimeAssert.Require(!harness.Audit.Commands.Any(command => command.GetValueOrDefault("stage") as string == @case.CaseId + "/SaveChanges"
                && command.GetValueOrDefault("status") as string == "completed"), "six_refusal_command_completed");
            // An accepted local binding refusal can retire its old connection. Use a wholly fresh EF factory connection.
            RuntimeHarness.SameState(before, await harness.FreshState(freshSpec, @case.Async, item, "fresh_state_after_refusal", 0));
            return;
        }
        if (@case.Async) item["async_write_positive_control"] = harness.Audit.ConfirmAsyncWrite(@case.CaseId + "/SaveChanges");
        TemporalRow persisted = await harness.Fresh(spec, @case.Async, item, "fresh_AsNoTracking");
        if (@case.Kind is "dt" or "dto")
        {
            RuntimeHarness.Exact(@case, persisted); harness.Independent(spec, @case, item);
            if (@case.Shape == "default7_fixed6")
            {
                RequirePublishedSevenToFixedSix(harness, @case);
                item["fixed_precedence"] = "explicit_request7_shared_extended_fixed6_EncodedScale7_WireScale6";
            }
        }
        else
        {
            bool matches = @case.Kind switch
            {
                "date" => persisted.A?.DayNumber == (@case.Ticks.HasValue ? DateOnly.FromDateTime(@case.DateTimeValue).DayNumber : (int?)null),
                "time" => persisted.T?.Ticks == @case.Ticks,
                _ => persisted.I?.Ticks == @case.Ticks
            };
            RuntimeAssert.Require(matches, "EF_native_value_mismatch");
            item["independent_native"] = await harness.Step(item, "fresh_native_raw_oracle",
                () => Task.FromResult(NativeFresh(harness, spec.Table, @case, item))); harness.Write();
        }
    }
    private static JsonElement SameInvocationSource(RuntimeHarness harness, string stage, Dictionary<string, object?> item)
    {
        object[] sources = harness.Audit.Sources(stage);
        item["actual_parameter_describe"] = sources;
        item["parameter_describe_observed"] = sources.Length != 0;
        item["metadata_evidence_source"] = sources.Length == 1 ? "same_invocation_after_prepare_decode_before_bind" : "pending_missing_or_ambiguous_source";
        RuntimeAssert.Require(sources.Length == 1, "same_invocation_ParameterDescribe_pending_not_accepted");
        JsonElement source = JsonSerializer.SerializeToElement(sources[0], SafeEvidence.Json);
        RuntimeAssert.Require(source.GetProperty("capture_point").GetString() == "after_prepare_decode_before_bind"
            && source.GetProperty("request_opcode").GetInt32() == 5, "same_invocation_describe_capture_scope");
        return source;
    }
    private static Type? FixedSixCommandExpectation(RuntimeHarness harness, RuntimeCase @case, Dictionary<string, object?> item)
    {
        JsonElement source = SameInvocationSource(harness, @case.CaseId + "/SaveChanges", item);
        JsonElement[] allParameters = source.GetProperty("parameters").EnumerateArray().ToArray();
        RuntimeAssert.Require(allParameters.Length == 4 && allParameters.All(parameter => parameter.GetProperty("server_inout_type").GetInt32() == 0),
            "fixed6_command_parameter_scope_pending");
        foreach (JsonElement parameter in allParameters.Where(parameter =>
            parameter.GetProperty("request").GetProperty("db_type").GetString() is not ("DateTime2" or "DateTimeOffset")))
        {
            JsonElement request = parameter.GetProperty("request"), raw = parameter.GetProperty("raw"), value = request.GetProperty("value");
            string? type = request.GetProperty("db_type").GetString();
            RuntimeAssert.Require(raw.GetProperty("available").ValueKind == JsonValueKind.True && raw.GetProperty("source").GetString() == "ParameterDescribe"
                && raw.GetProperty("mask").GetInt32() == 0 && raw.GetProperty("derived_mask").GetInt32() == 0
                && raw.GetProperty("type_flag").GetInt32() == 1 && raw.GetProperty("result_format").GetString() == "Unknown"
                && request.GetProperty("direction").GetString() == "Input"
                && (type == "Int32" && raw.GetProperty("c_type").GetInt32() == 7 && value.GetProperty("value").GetInt32() == 1
                    || type == "AnsiString" && raw.GetProperty("c_type").GetInt32() == 2 && value.GetProperty("value").GetString() == "first"),
                "fixed6_non_temporal_input_pending_not_accepted");
        }
        JsonElement[] temporal = allParameters
            .Where(parameter => parameter.GetProperty("request").GetProperty("db_type").GetString() is "DateTime2" or "DateTimeOffset").ToArray();
        RuntimeAssert.Require(temporal.Length == 2 && temporal.Count(parameter => parameter.GetProperty("request").GetProperty("db_type").GetString() == "DateTime2") == 1,
            "fixed6_command_all_temporal_parameters_missing");
        foreach (JsonElement parameter in temporal)
        {
            JsonElement request = parameter.GetProperty("request"), raw = parameter.GetProperty("raw");
            bool dto = request.GetProperty("db_type").GetString() == "DateTimeOffset";
            RuntimeAssert.Require(request.GetProperty("precision").GetInt32() == 0 && request.GetProperty("scale").GetInt32() == 7
                && request.GetProperty("explicit_scale").ValueKind == JsonValueKind.True && request.GetProperty("direction").GetString() == "Input",
                "fixed6_actual_explicit7_request_missing");
            RequireRaw(raw, dto, 6, 1);
            JsonElement value = request.GetProperty("value");
            bool selected = dto == (@case.Kind == "dto");
            bool isNull = value.TryGetProperty("is_null", out JsonElement nullFlag) && nullFlag.GetBoolean();
            RuntimeAssert.Require(isNull == (!selected || !@case.Ticks.HasValue), "fixed6_typed_NULL_request_mismatch");
            if (!isNull)
            {
                RuntimeAssert.Require(value.GetProperty(dto ? "local_ticks" : "ticks").GetInt64() == @case.Ticks, "fixed6_actual_request_ticks_mismatch");
                if (dto) RuntimeAssert.Require(value.GetProperty("offset_minutes").GetInt32() == @case.OffsetMinutes, "fixed6_actual_request_offset_mismatch");
            }
        }
        // Validate every tuple first, then walk the actual server binding order. The other typed NULL can itself be the first refusal.
        Type? expected = null; int? refusingOrdinal = null;
        foreach (JsonElement parameter in temporal.OrderBy(parameter => parameter.GetProperty("ordinal").GetInt32()))
        {
            int ctype = parameter.GetProperty("raw").GetProperty("c_type").GetInt32();
            JsonElement request = parameter.GetProperty("request"), value = request.GetProperty("value");
            bool dto = request.GetProperty("db_type").GetString() == "DateTimeOffset";
            if (@case.Profile == "tls" || ctype is 16 or 23) expected = typeof(NotSupportedException);
            else if (!(value.TryGetProperty("is_null", out JsonElement flag) && flag.GetBoolean())
                && value.GetProperty(dto ? "local_ticks" : "ticks").GetInt64() % 10 != 0) expected = typeof(OverflowException);
            if (expected != null) { refusingOrdinal = parameter.GetProperty("ordinal").GetInt32(); break; }
        }
        item["whole_command_contract"] = new { source_branch = "actual_fixed6_all_temporal_server_binding_order",
            first_refusal_server_ordinal = refusingOrdinal, expected_leaf_type = expected?.FullName,
            success_requires_all_temporal_extended_shared = true };
        return expected;
    }
    private static void RequireRaw(JsonElement raw, bool dto, int scale, int flag)
    {
        RuntimeAssert.Require(raw.GetProperty("available").ValueKind == JsonValueKind.True && raw.GetProperty("source").GetString() == "ParameterDescribe"
            && raw.GetProperty("mask").GetInt32() == 0 && raw.GetProperty("derived_mask").GetInt32() == 0
            && raw.GetProperty("result_format").GetString() == "Unknown" && raw.GetProperty("type_flag").GetInt32() == flag
            && raw.GetProperty("scale").GetInt32() == scale, "ParameterDescribe_tuple_pending_not_accepted");
        int type = raw.GetProperty("c_type").GetInt32(), width = raw.GetProperty("precision").GetInt32();
        RuntimeAssert.Require(dto ? type == 23 && width == 10 || type == 27 && width == 11 : type == 16 && width == 8 || type == 26 && width == 9,
            "ParameterDescribe_family_width_pending_not_accepted");
    }
    private static void RequirePublishedSevenToFixedSix(RuntimeHarness harness, RuntimeCase @case)
    {
        string stage = @case.CaseId + "/SaveChanges";
        JsonElement source = JsonSerializer.SerializeToElement(harness.Audit.Sources(stage).Single(), SafeEvidence.Json);
        int attempt = source.GetProperty("attempt_id").GetInt32();
        JsonElement completed = JsonSerializer.SerializeToElement(harness.Audit.Commands.Single(command =>
            command.GetValueOrDefault("stage") as string == stage && command.GetValueOrDefault("status") as string == "completed"
            && command.GetValueOrDefault("attempt_id") is int id && id == attempt), SafeEvidence.Json);
        JsonElement[] bindings = completed.GetProperty("binding").EnumerateArray().ToArray();
        foreach (JsonElement parameter in source.GetProperty("parameters").EnumerateArray()
            .Where(parameter => parameter.GetProperty("request").GetProperty("db_type").GetString() is "DateTime2" or "DateTimeOffset"))
        {
            int ordinal = parameter.GetProperty("ordinal").GetInt32();
            JsonElement binding = bindings.Single(binding => binding.GetProperty("ordinal").GetInt32() == ordinal);
            JsonElement raw = parameter.GetProperty("raw");
            RuntimeAssert.Require(binding.GetProperty("binding_available").ValueKind == JsonValueKind.True && binding.GetProperty("reliable_fixed").ValueKind == JsonValueKind.True
                && binding.GetProperty("encoded_scale").GetInt32() == 7 && binding.GetProperty("wire_scale").GetInt32() == 6
                && binding.GetProperty("requested_scale").GetInt32() == 7 && binding.GetProperty("column_scale").GetInt32() == 6
                && binding.GetProperty("request_explicit_scale").ValueKind == JsonValueKind.True && binding.GetProperty("profile_state").GetString() == "VerifiedSeven"
                && binding.GetProperty("c_type").GetInt32() == raw.GetProperty("c_type").GetInt32()
                && binding.GetProperty("raw_source").GetString() == "ParameterDescribe" && binding.GetProperty("raw_c_type").GetInt32() == raw.GetProperty("c_type").GetInt32()
                && binding.GetProperty("raw_precision").GetInt32() == raw.GetProperty("precision").GetInt32()
                && binding.GetProperty("raw_scale").GetInt32() == 6 && binding.GetProperty("raw_type_flag").GetInt32() == 1
                && binding.GetProperty("raw_mask").GetInt32() == 0 && binding.GetProperty("raw_derived_mask").GetInt32() == 0
                && binding.GetProperty("raw_result_format").GetString() == "Unknown", "explicit7_fixed6_actual_published_binding_mismatch");
        }
    }
    private static int AdvisoryInt(JsonElement value)
    {
        int result = 0;
        RuntimeAssert.Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result), "TLS_advisory_integer_field");
        return result;
    }
    private static long AdvisoryLong(JsonElement value)
    {
        long result = 0;
        RuntimeAssert.Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result), "TLS_advisory_integer_field");
        return result;
    }
    private static bool AdvisorySameJson(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        if (left.ValueKind == JsonValueKind.Object)
            return left.EnumerateObject().Count() == right.EnumerateObject().Count()
                && left.EnumerateObject().All(p => right.TryGetProperty(p.Name, out var v) && AdvisorySameJson(p.Value, v));
        if (left.ValueKind == JsonValueKind.Array)
            return left.GetArrayLength() == right.GetArrayLength() && left.EnumerateArray().Zip(right.EnumerateArray()).All(p => AdvisorySameJson(p.First, p.Second));
        return left.GetRawText() == right.GetRawText();
    }
    private static string AdvisorySourceBranch(RuntimeHarness harness, RuntimeCase @case, DbCommand command, JsonElement source)
    {
        RuntimeAssert.Require(@case.Profile == "tls" && @case.Group == "six" && @case.Shape == "advisory7" && @case.Async
            && (@case.Kind is "dt" or "dto") && @case.CaseId == $"EF-R03-tls-{@case.Kind}-advisory7-capability-async"
            && @case.Scale == 7 && @case.Ticks == RuntimeCases.Ordinary + 1 && @case.OffsetMinutes == (@case.Kind == "dto" ? 330 : (int?)null), "TLS_exact_advisory_case_scope");
        string stage = @case.CaseId + "/advisory_ExecuteAsync", sqlHash = CommandAudit.Hash("SELECT :v FROM DUAL");
        RuntimeAssert.Require(command.CommandText == "SELECT :v FROM DUAL" && command.Parameters.Count == 1, "TLS_exact_controlled_SQL_scope");
        JsonElement[] commands = harness.Audit.Commands.Where(c => c.GetValueOrDefault("stage") as string == stage)
            .Select(c => JsonSerializer.SerializeToElement(c, SafeEvidence.Json)).ToArray();
        RuntimeAssert.Require(commands.Length == 2 && commands[0].GetProperty("status").GetString() == "attempted"
            && commands[1].GetProperty("status").GetString() == "failed", "TLS_one_controlled_attempt_and_failure");
        JsonElement start = commands[0], failed = commands[1];
        int attempt = AdvisoryInt(start.GetProperty("attempt_id"));
        RuntimeAssert.Require(attempt > 0 && AdvisoryInt(source.GetProperty("attempt_id")) == attempt && AdvisoryInt(failed.GetProperty("attempt_id")) == attempt
            && start.GetProperty("api").GetString() == "controlled_ADO_ExecuteNonQueryAsync" && start.GetProperty("ef_command_id").ValueKind == JsonValueKind.Null
            && source.GetProperty("ef_command_id").ValueKind == JsonValueKind.Null && source.GetProperty("stage").GetString() == stage
            && start.GetProperty("sql_sha256").GetString() == sqlHash && failed.GetProperty("sql_sha256").GetString() == sqlHash
            && AdvisoryInt(start.GetProperty("sql_length")) == 19, "TLS_same_controlled_command_source_and_SQL");
        RuntimeAssert.Require(!failed.TryGetProperty("binding", out _) && failed.GetProperty("current_binding_observed").ValueKind == JsonValueKind.False
            && failed.GetProperty("current_binding").ValueKind == JsonValueKind.Null && AdvisorySameJson(failed.GetProperty("parameter_describe_sources"),
                JsonSerializer.SerializeToElement(new[] { source }, SafeEvidence.Json)), "TLS_refusal_no_binding_same_source");
        RuntimeAssert.Require(AdvisoryInt(source.GetProperty("command_reference_token")) == System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(command)
            && AdvisoryInt(source.GetProperty("connection_reference_token")) == System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(command.Connection!)
            && AdvisoryInt(source.GetProperty("statement_reference_token")) != 0, "TLS_actual_source_reference_owner");
        AdvisoryInt(source.GetProperty("server_statement_id"));
        long[] identity = source.GetProperty("operation_identity").EnumerateArray().Select(AdvisoryLong).ToArray();
        RuntimeAssert.Require(identity.Length == 4 && identity.All(n => n > 0), "TLS_complete_source_operation_identity");
        int sequence = AdvisoryInt(source.GetProperty("sequence")), responseSequence = AdvisoryInt(source.GetProperty("response_sequence"));
        JsonElement[] responses = harness.Audit.Responses.Select(r => JsonSerializer.SerializeToElement(r, SafeEvidence.Json)).ToArray();
        var response = responses.Single(r => AdvisoryInt(r.GetProperty("sequence")) == responseSequence);
        RuntimeAssert.Require(responseSequence > 0 && responseSequence < sequence && response.GetProperty("stage").GetString() == stage
            && AdvisoryInt(response.GetProperty("attempt_id")) == attempt && AdvisoryInt(response.GetProperty("request_opcode")) == 5
            && AdvisoryInt(response.GetProperty("sql_code")) >= 0 && AdvisorySameJson(response.GetProperty("operation_identity"), source.GetProperty("operation_identity"))
            && responses.Where(r => AdvisoryInt(r.GetProperty("attempt_id")) == attempt && AdvisoryInt(r.GetProperty("sequence")) < sequence)
                .Max(r => AdvisoryInt(r.GetProperty("sequence"))) == responseSequence, "TLS_latest_successful_same_Prepare");
        RuntimeAssert.Require(source.GetProperty("capture_point").GetString() == "after_prepare_decode_before_bind" && AdvisoryInt(source.GetProperty("request_opcode")) == 5,
            "TLS_same_Prepare_capture_point");
        var parameters = source.GetProperty("parameters"); var publicParameters = start.GetProperty("parameters");
        RuntimeAssert.Require(parameters.GetArrayLength() == 1 && publicParameters.GetArrayLength() == 1, "TLS_single_whole_parameter_scope");
        JsonElement parameter = parameters[0], request = parameter.GetProperty("request"), actualPublic = publicParameters[0];
        RuntimeAssert.Require(AdvisoryInt(parameter.GetProperty("ordinal")) == 0 && AdvisoryInt(parameter.GetProperty("public_parameter_index")) == 0
            && AdvisoryInt(parameter.GetProperty("server_inout_type")) == 0 && parameter.GetProperty("public_parameter_name_sha256").GetString() == CommandAudit.Hash(command.Parameters[0].ParameterName)
            && parameter.GetProperty("public_parameter_name_sha256").GetString() == CommandAudit.Hash("v")
            && parameter.GetProperty("server_parameter_name_sha256").GetString() == CommandAudit.Hash("V"), "TLS_actual_ordinal_public_name_binding");
        RuntimeAssert.Require(request.EnumerateObject().Count() == 8 && actualPublic.EnumerateObject().Count() == 9
            && request.EnumerateObject().All(p => actualPublic.TryGetProperty(p.Name, out var v) && AdvisorySameJson(p.Value, v))
            && AdvisorySameJson(start.GetProperty("command"), JsonSerializer.SerializeToElement(harness.Guard.VerifyObject(command, "command"), SafeEvidence.Json))
            && AdvisorySameJson(start.GetProperty("connection"), JsonSerializer.SerializeToElement(harness.Guard.VerifyObject(command.Connection!, "connection"), SafeEvidence.Json))
            && AdvisorySameJson(actualPublic.GetProperty("actual"), JsonSerializer.SerializeToElement(harness.Guard.VerifyObject(command.Parameters[0], "parameter"), SafeEvidence.Json)), "TLS_actual_W_request_and_routes");
        bool dto = @case.Kind == "dto"; JsonElement value = request.GetProperty("value");
        RuntimeAssert.Require(request.GetProperty("db_type").GetString() == (dto ? "DateTimeOffset" : "DateTime2")
            && request.GetProperty("provider_type").GetString() == (dto ? "DateTimeOffset" : "DateTime") && AdvisoryInt(request.GetProperty("precision")) == 0
            && request.GetProperty("explicit_precision").ValueKind == JsonValueKind.False && AdvisoryInt(request.GetProperty("scale")) == 7
            && request.GetProperty("explicit_scale").ValueKind == JsonValueKind.True && request.GetProperty("direction").GetString() == "Input"
            && value.EnumerateObject().Count() == 3 && value.GetProperty("clr_type").GetString() == (dto ? "DateTimeOffset" : "DateTime")
            && AdvisoryLong(value.GetProperty(dto ? "local_ticks" : "ticks")) == @case.Ticks
            && (dto ? AdvisoryInt(value.GetProperty("offset_minutes")) == 330 : value.GetProperty("kind").GetString() == "Unspecified"), "TLS_exact_nonNULL_temporal_request7");
        JsonElement raw = parameter.GetProperty("raw");
        RuntimeAssert.Require(raw.EnumerateObject().Count() == 9 && raw.GetProperty("available").ValueKind == JsonValueKind.True
            && raw.GetProperty("source").GetString() == "ParameterDescribe" && raw.GetProperty("result_format").GetString() == "Unknown"
            && AdvisoryInt(raw.GetProperty("mask")) == 0 && AdvisoryInt(raw.GetProperty("derived_mask")) == 0, "TLS_advisory_exact_raw_source");
        int type = AdvisoryInt(raw.GetProperty("c_type")), width = AdvisoryInt(raw.GetProperty("precision")), scale = AdvisoryInt(raw.GetProperty("scale")), flag = AdvisoryInt(raw.GetProperty("type_flag"));
        if (type == 2 && width == 32767 && scale == 0 && flag == 2)
        {
            RuntimeAssert.Require(AdvisoryInt(response.GetProperty("response_opcode")) == 0, "TLS_generic_successful_Prepare_response0");
            return "exact_generic_ParameterDescribe_TLS_explicit7_refusal";
        }
        RequireRaw(raw, dto, 7, 1);
        RuntimeAssert.Require(type == (dto ? 27 : 26), "TLS_fixed7_extended_tuple_pending");
        return "actual_fixed7_extended_ParameterDescribe_TLS_verified6";
    }
    private static void AdvisoryControlPrefix(RuntimeHarness harness, RuntimeCase @case, JsonElement source)
    {
        // Program's frozen fixed15 helper puts both advisory cases in the TLS/six [45,56) process.
        var window = RuntimeCases.Required("tls", "six").Skip(45).Take(15).ToArray();
        int position = Array.FindIndex(window, c => c.CaseId == @case.CaseId);
        var earlier = window.Take(position).Select(c => c.CaseId + "/SaveChanges").ToHashSet(StringComparer.Ordinal);
        RuntimeAssert.Require(position > 0 && harness.Audit.AsyncWriteControls.Count > 0, "TLS_same_window_positive_control_missing");
        foreach (var actual in harness.Audit.AsyncWriteControls)
        {
            var control = JsonSerializer.SerializeToElement(actual, SafeEvidence.Json);
            string stage = control.GetProperty("stage").GetString()!;
            var prepared = JsonSerializer.SerializeToElement(harness.Audit.Sources(stage).Single(), SafeEvidence.Json);
            RuntimeAssert.Require(earlier.Contains(stage) && control.GetProperty("source").GetString() == "actual_successful_EF_SaveChangesAsync_AfterFrameSent"
                && AdvisoryInt(prepared.GetProperty("attempt_id")) < AdvisoryInt(source.GetProperty("attempt_id"))
                && AdvisoryInt(prepared.GetProperty("sequence")) < AdvisoryInt(source.GetProperty("sequence"))
                && AdvisorySameJson(control.GetProperty("prepare_source"), prepared), "TLS_prior_same_window_control_source");
            var complete = control.GetProperty("completed_command_attempt");
            RuntimeAssert.Require(complete.GetProperty("status").GetString() == "completed"
                && (complete.GetProperty("api").GetString() is "ReaderExecutedAsync" or "NonQueryExecutedAsync")
                && AdvisoryInt(complete.GetProperty("attempt_id")) == AdvisoryInt(prepared.GetProperty("attempt_id"))
                && harness.Audit.Commands.Any(c => AdvisorySameJson(JsonSerializer.SerializeToElement(c, SafeEvidence.Json), complete))
                && complete.GetProperty("binding").EnumerateArray().Any(b => b.GetProperty("binding_available").ValueKind == JsonValueKind.True), "TLS_actual_positive_completion_and_binding");
            var sent = harness.Audit.Sends.Where(s => s.GetValueOrDefault("stage") as string == stage && s.GetValueOrDefault("opcode") is int opcode && opcode is 6 or 13).ToArray();
            RuntimeAssert.Require(sent.Length > 0 && AdvisoryInt(control.GetProperty("business_execute_frames")) == sent.Length
                && AdvisorySameJson(control.GetProperty("sent_identity"), JsonSerializer.SerializeToElement(sent, SafeEvidence.Json))
                && sent.Any(s => new[] { "session_id", "lease_generation", "execution_id", "invocation_id" }.Select(k => (long)s[k]!)
                    .SequenceEqual(prepared.GetProperty("operation_identity").EnumerateArray().Select(AdvisoryLong))), "TLS_actual_prior_positive_sent_identity");
        }
    }
    private static async Task Advisory(RuntimeHarness harness, RuntimeCase @case, TemporalContext context, Dictionary<string, object?> item)
    {
        await harness.Step(item, "open_EF_factory_connection", async () => { await context.Database.OpenConnectionAsync(); return true; });
        DbConnection connection = context.Database.GetDbConnection(); harness.Guard.VerifyOpenedConnection(connection);
        await using DbCommand command = connection.CreateCommand(); harness.Guard.VerifyObject(command, "command");
        command.CommandText = "SELECT :v FROM DUAL";
        var parameter = context.Mapping(@case.Kind == "dt" ? "D" : "Z").CreateParameter(command, "v",
            @case.Kind == "dt" ? (object)@case.DateTimeValue : (object)@case.OffsetValue, nullable: true);
        command.Parameters.Add(parameter);
        RuntimeAssert.Require(parameter.Precision == 0 && parameter.Scale == 7 && CommandAudit.Field(parameter, "m_SetScaleFlag") is true,
            "TLS_EF_parameter_request7_missing");
        item["route"] = "EF_mapping_created_W_parameter_controlled_ADO_execute_not_SaveChanges";
        item["parameter"] = new { actual = harness.Guard.VerifyObject(parameter, "parameter"), precision = (int)parameter.Precision,
            scale = (int)parameter.Scale, explicit_scale = CommandAudit.Field(parameter, "m_SetScaleFlag"), db_type = parameter.DbType.ToString() };
        Exception? error = null;
        try
        {
            await harness.Step(item, "advisory_ExecuteAsync", async () =>
            {
                harness.Audit.BeginControlled(command);
                try { int rows = await command.ExecuteNonQueryAsync(); harness.Audit.CompleteControlled(command, rows); return rows; }
                catch (Exception refused) { harness.Audit.FailControlled(command, refused); throw; }
            });
        }
        catch (Exception refused) { error = refused; }
        if (error != null) item["actual_exception_chain"] = RuntimeRefusal.Evidence(error);
        JsonElement source = SameInvocationSource(harness, @case.CaseId + "/advisory_ExecuteAsync", item);
        item["source_branch"] = AdvisorySourceBranch(harness, @case, command, source);
        RuntimeAssert.Require(error != null, "TLS_seven_capability_refusal_missing");
        RuntimeAssert.Require(RuntimeRefusal.Matches(error!, typeof(NotSupportedException)), "TLS_seven_exception_contract_mismatch");
        var failed = harness.Audit.Commands.Single(c => c.GetValueOrDefault("stage") as string == @case.CaseId + "/advisory_ExecuteAsync"
            && c.GetValueOrDefault("status") as string == "failed");
        RuntimeAssert.Require(AdvisorySameJson(JsonSerializer.SerializeToElement(failed["actual_exception_chain"], SafeEvidence.Json),
            JsonSerializer.SerializeToElement(RuntimeRefusal.Evidence(error!), SafeEvidence.Json)), "TLS_refusal_actual_command_exception_join");
        AdvisoryControlPrefix(harness, @case, source);
        item["expected_refusal"] = new { actual_chain = RuntimeRefusal.Evidence(error!), expected_leaf_type = typeof(NotSupportedException).FullName,
            send_evidence = harness.Audit.RefusalSendEvidence(@case.CaseId + "/advisory_ExecuteAsync", true), current_binding = (object?)null,
            current_binding_observed = false };
        item["old_connection_state_after_refusal"] = connection.State.ToString();
        item["fresh_connection_health"] = await harness.Step(item, "fresh_after_TLS_refusal", () =>
        {
            using var fresh = harness.Guard.OpenTestConnection();
            using var control = CandidateGuard.Command(fresh, "SELECT 1 FROM DUAL");
            RuntimeAssert.Require(Convert.ToInt32(control.ExecuteScalar(), CultureInfo.InvariantCulture) == 1, "TLS_refusal_fresh_health_failed");
            return Task.FromResult(harness.Guard.VerifyOpenedConnection(fresh));
        });
    }
    private static object NativeFresh(RuntimeHarness harness, string table, RuntimeCase @case, Dictionary<string, object?> item)
    {
        CandidateGuard guard = harness.Guard;
        var attempt = new Dictionary<string, object?> { ["status"] = "attempted", ["source"] = "fresh_ordinary_native_raw_integer_oracle" };
        item["native_oracle_attempt"] = attempt; harness.Write();
        string column = @case.Kind switch { "date" => "A", "time" => "T", _ => "I" };
        using var connection = guard.OpenTestConnection(); using var command = connection.CreateCommand();
        guard.VerifyObject(command, "command");
        string textProjection = @case.Kind switch { "date" => $",TO_CHAR(\"{column}\",'YYYY-MM-DD')", "time" => $",TO_CHAR(\"{column}\",'HH24:MI:SS.FF9')", _ => $",CAST(\"{column}\" AS VARCHAR(100))" };
        command.CommandText = $"SELECT \"{column}\"{textProjection} FROM \"{table}\" WHERE \"ID\"=1";
        using var reader = command.ExecuteReader(); guard.VerifyObject(reader, "reader");
        RuntimeAssert.Require(reader.Read(), "native_oracle_row_missing");
        object? cache = CommandAudit.Field(reader, "m_RsCache");
        var rows = cache == null ? null : CommandAudit.Field(cache, "datas") as byte[][][];
        object? position = cache == null ? null : CommandAudit.Field(cache, "datasOffset");
        RuntimeAssert.Require(CommandAudit.Field(reader, "bdta") is false && cache != null && CommandAudit.Field(cache, "isRsBdta") is false
            && rows != null && position is int index && index >= 0 && index < rows.Length && rows[index].Length > 1, "native_oracle_raw_position");
        var columns = CommandAudit.Field(reader, "m_ColInfo") as Array;
        object? metadata = columns?.GetValue(0);
        int expectedType = @case.Kind == "date" ? 14 : @case.Kind == "time" ? 15 : 21;
        int expectedWireScale = @case.Kind == "date" ? 0 : @case.Kind == "time" ? 6 : (6 << 8 | 9 << 4 | 6);
        object? rawMetadata = metadata?.GetType().GetProperty("TemporalServerMetadata", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(metadata);
        RuntimeAssert.Require(metadata != null && CommandAudit.Field(metadata, "type") is int type && type == expectedType
            && CommandAudit.Field(metadata, "scale") is int declared && declared == expectedWireScale
            && CommandAudit.Field(metadata, "mask") is 0 && rawMetadata?.GetType().GetProperty("Source")?.GetValue(rawMetadata)?.ToString() == "ResultColumn",
            "native_oracle_result_metadata_scope");
        byte[]? raw = rows![(int)position!][1]; // Capture before any temporal getter.
        bool isNull = reader.IsDBNull(0); RuntimeAssert.Require(isNull == !@case.Ticks.HasValue && (raw == null) == isNull, "native_oracle_NULL_mismatch");
        long? ticks = null; int[]? intervalWords = null;
        if (raw != null)
        {
            RuntimeAssert.Require(raw.Length == (@case.Kind == "date" ? 3 : @case.Kind == "time" ? 5 : 24), "native_oracle_raw_width");
            attempt["raw_hex"] = Convert.ToHexString(raw); attempt["c_type"] = expectedType; attempt["wire_scale"] = expectedWireScale; attempt["raw_captured_before_temporal_getter"] = true; harness.Write();
            if (@case.Kind == "date")
            {
                int year = BinaryPrimitives.ReadUInt16LittleEndian(raw) & 0x7fff, month = ((raw[1] >> 7) | ((raw[2] & 7) << 1)), day = raw[2] >> 3;
                ticks = new DateTime(year, month, day).Ticks;
            }
            else if (@case.Kind == "time")
            {
                int hour = raw[0] & 31, minute = ((raw[0] >> 5) | ((raw[1] & 7) << 3)), second = (raw[1] >> 3) | ((raw[2] & 1) << 5);
                int micro = (raw[2] >> 1) | (raw[3] << 7) | (raw[4] << 15);
                RuntimeAssert.Require((raw[4] & 0xe0) == 0 && micro <= 999999 && hour < 24 && minute < 60 && second < 60, "native_TIME_field_range");
                ticks = ((long)hour * 3600 + minute * 60 + second) * TimeSpan.TicksPerSecond + micro * 10L;
            }
            else
            {
                int[] words = Enumerable.Range(0, 6).Select(index => BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(index * 4, 4))).ToArray();
                intervalWords = words;
                RuntimeAssert.Require(words[5] == (6 << 8 | 9 << 4 | 6), "native_interval_wire_declaration");
                RuntimeAssert.Require(words[0] is >= 0 and <= 999999999 && words[1] is >= 0 and <= 23 && words[2] is >= 0 and <= 59
                    && words[3] is >= 0 and <= 59 && words[4] is >= 0 and <= 999999, "native_interval_field_range");
                ticks = (((long)words[0] * 24 + words[1]) * 60 + words[2]) * TimeSpan.TicksPerMinute + words[3] * TimeSpan.TicksPerSecond + words[4] * 10L;
            }
            RuntimeAssert.Require(ticks == @case.Ticks, "native_raw_integer_ticks_mismatch");
        }
        RuntimeAssert.Require(reader.IsDBNull(1) == isNull, "native_server_text_NULL_mismatch");
        string? text = isNull ? null : reader.GetString(1);
        if (text != null)
        {
            if (@case.Kind == "interval")
            {
                // This projection is solely CAST of this owned synthetic interval row. Preserve a bounded
                // JSON-escaped receipt before grammar validation, including unfamiliar finite server text.
                attempt["server_text_character_length"] = text.Length; attempt["server_text_processing_limit"] = 120;
                attempt["server_text"] = text.Length <= 120 ? text : null;
                attempt["server_text_utf8_sha256"] = text.Length <= 120 ? CommandAudit.Hash(text) : null;
                harness.Write();
            }
            string pattern = @case.Kind == "date" ? @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}\z" : @case.Kind == "time" ? @"\A[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{9}\z"
                : @"\AINTERVAL '\+?(?<days>[0-9]{1,9}) (?<hours>[0-9]{1,2}):(?<minutes>[0-9]{1,2}):(?<seconds>[0-9]{1,2})\.(?<micro>[0-9]{6})' DAY\(9\) TO SECOND\(6\)\z";
            RuntimeAssert.Require(text.Length <= 120, "native_server_text_finite_format");
            Match match = Regex.Match(text, pattern, RegexOptions.CultureInvariant);
            RuntimeAssert.Require(match.Success, "native_server_text_finite_format");
            attempt["server_text"] = text; harness.Write();
            if (@case.Kind == "date") RuntimeAssert.Require(text == @case.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "native_DATE_server_text_mismatch");
            if (@case.Kind == "time")
            {
                TimeSpan time = TimeSpan.FromTicks(@case.Ticks!.Value);
                string expected = FormattableString.Invariant($"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.") + ((@case.Ticks!.Value % TimeSpan.TicksPerSecond) * 100).ToString("D9", CultureInfo.InvariantCulture);
                RuntimeAssert.Require(text == expected, "native_TIME_FF9_mismatch");
            }
            if (@case.Kind == "interval")
            {
                int[] actualFields = new[] { "days", "hours", "minutes", "seconds", "micro" }
                    .Select(name => int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture)).ToArray();
                RuntimeAssert.Require(actualFields[0] is >= 0 and <= 999999999 && actualFields[1] is >= 0 and <= 23
                    && actualFields[2] is >= 0 and <= 59 && actualFields[3] is >= 0 and <= 59 && actualFields[4] is >= 0 and <= 999999,
                    "native_interval_server_component_domain");
                RuntimeAssert.Require(intervalWords != null && actualFields.SequenceEqual(intervalWords.Take(5)), "native_interval_server_raw_components_mismatch");
                long serverTicks = checked((((long)actualFields[0] * 24 + actualFields[1]) * 60 + actualFields[2]) * TimeSpan.TicksPerMinute
                    + actualFields[3] * TimeSpan.TicksPerSecond + actualFields[4] * 10L);
                RuntimeAssert.Require(serverTicks == ticks && serverTicks == @case.Ticks, "native_interval_server_expected_ticks_mismatch");
            }
        }
        RuntimeAssert.Require(!reader.Read(), "native_oracle_multiple_rows");
        attempt["status"] = "completed"; harness.Write();
        return new { rows = 1, is_null = isNull, ticks, raw_hex = raw == null ? null : Convert.ToHexString(raw), capture = "fresh_ordinary_cache_before_temporal_getter" };
    }
}
