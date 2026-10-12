using System.Data.Common;
using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using W.Dm;

namespace TP07.Runtime;

internal sealed class CommandAudit(CandidateGuard guard) : DbCommandInterceptor, IDisposable
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly FieldInfo sendHook = typeof(W.Dm.DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!
        .GetField("AfterFrameSent", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("audit_send_hook_missing");
    private object? previousHook;
    private readonly FieldInfo frameHook = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")!
        .GetField("AfterFrame", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("audit_frame_hook_missing");
    private readonly FieldInfo decodeHook = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")!
        .GetField("AfterStatementDecode", BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("audit_decode_hook_missing");
    private object? previousFrameHook, previousDecodeHook;
    private sealed record Attempt(DbCommand Command, string Stage, int Id, Guid? EfCommandId)
    { internal int Events; internal Dictionary<string, object?>? LastFrame; }
    private Attempt? active;
    private int attemptOrdinal, eventOrdinal;
    private bool installed;
    internal string Stage { get; set; } = "startup";
    internal List<Dictionary<string, object?>> Commands { get; } = [];
    internal List<Dictionary<string, object?>> Sends { get; } = [];
    internal List<object> Connections { get; } = [];
    internal List<object> AsyncWriteControls { get; } = [];
    internal List<Dictionary<string, object?>> Responses { get; } = [];
    internal List<Dictionary<string, object?>> PrepareSources { get; } = [];
    internal bool ObserverFailed { get; private set; }
    internal void Install()
    {
        previousHook = sendHook.GetValue(null);
        previousFrameHook = frameHook.GetValue(null); previousDecodeHook = decodeHook.GetValue(null);
        RuntimeAssert.Require(previousHook == null && previousFrameHook == null && previousDecodeHook == null, "audit_requires_isolated_hook");
        MethodInfo method = GetType().GetMethod(nameof(Sent), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(sendHook.FieldType.GenericTypeArguments[0]);
        try
        {
            installed = true;
            sendHook.SetValue(null, Delegate.CreateDelegate(sendHook.FieldType, this, method));
            frameHook.SetValue(null, (Action<short, short, int, int>)Frame);
            decodeHook.SetValue(null, (Action<short, int, long, long, int, bool, bool>)Decoded);
        }
        catch { Dispose(); throw; }
    }
    private void Sent<T>(T identity, short opcode)
    {
        try
        {
            object? value = identity;
            long Number(string name) => value?.GetType().GetProperty(name, InstanceFlags)?.GetValue(value) is long number
                ? number : throw new InvalidOperationException("audit_send_identity_missing");
            Sends.Add(new() { ["stage"] = Stage, ["opcode"] = (int)opcode,
                ["session_id"] = Number("SessionId"), ["lease_generation"] = Number("LeaseGeneration"),
                ["execution_id"] = Number("ExecutionId"), ["invocation_id"] = Number("InvocationId") });
        }
        catch { ObserverFailed = true; }
    }
    internal int BusinessExecuteCount(string stage) => Sends.Count(item => (string)item["stage"]! == stage && (int)item["opcode"]! is 6 or 13);
    private static object? Property(object? source, string name) => source?.GetType().GetProperty(name, InstanceFlags)?.GetValue(source);
    private static object? Invoke(object source, string name) => source.GetType().GetMethod(name, InstanceFlags, Type.EmptyTypes)?.Invoke(source, null);
    private static long IdentityNumber(object identity, string name) => Property(identity, name) is long number && number > 0
        ? number : throw new RuntimeAssertionException("audit_operation_identity_invalid");
    private (object Invocation, object Session, object Identity, long[] Numbers) CurrentOwner(Attempt attempt)
    {
        Type invocationType = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Sessions.DmInvocation")!;
        object invocation = invocationType.GetProperty("Current", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)
            ?? throw new RuntimeAssertionException("audit_current_invocation_missing");
        object identity = Property(invocation, "Identity") ?? throw new RuntimeAssertionException("audit_identity_missing");
        object session = Property(Property(invocation, "Lease"), "Session") ?? throw new RuntimeAssertionException("audit_session_missing");
        long[] numbers = [IdentityNumber(identity, "SessionId"), IdentityNumber(identity, "LeaseGeneration"), IdentityNumber(identity, "ExecutionId"), IdentityNumber(identity, "InvocationId")];
        RuntimeAssert.Require(Property(invocation, "IsDisposed") is false && Property(invocation, "Completed") is false
            && ReferenceEquals(Property(attempt.Command.Connection, "Session"), session)
            && session.GetType().GetMethod("IsCurrentExecution", InstanceFlags, [identity.GetType()])?.Invoke(session, [identity]) is true,
            "audit_current_owner_mismatch");
        return (invocation, session, identity, numbers);
    }
    private void Frame(short request, short response, int code, int length)
    {
        if (active is not { } attempt || attempt.Command.Parameters.Count == 0) return;
        try
        {
            RuntimeAssert.Require(++attempt.Events <= 64, "audit_attempt_event_limit");
            var owner = CurrentOwner(attempt);
            var receipt = new Dictionary<string, object?> { ["stage"] = attempt.Stage, ["attempt_id"] = attempt.Id,
                ["sequence"] = ++eventOrdinal, ["request_opcode"] = (int)request, ["response_opcode"] = (int)response,
                ["body_length"] = length, ["sql_code"] = code, ["operation_identity"] = owner.Numbers };
            Responses.Add(receipt); attempt.LastFrame = receipt;
        }
        catch { ObserverFailed = true; }
    }
    private void Decoded(short request, int statementType, long rows, long affected, int columns, bool hasResult, bool terminal)
    {
        if (request != 5 || active is not { } attempt || attempt.Command.Parameters.Count == 0) return;
        try
        {
            RuntimeAssert.Require(++attempt.Events <= 64 && !ObserverFailed, "audit_decode_event_limit_or_observer_failed");
            var owner = CurrentOwner(attempt);
            RuntimeAssert.Require(attempt.LastFrame is { } frame && (int)frame["request_opcode"]! == 5 && (int)frame["sql_code"]! >= 0
                && ((long[])frame["operation_identity"]!).SequenceEqual(owner.Numbers), "audit_prepare_response_identity_mismatch");
            object statement = Field(attempt.Command, "m_Stmt") ?? throw new RuntimeAssertionException("audit_owned_statement_missing");
            object? instance = Invoke(statement, "G");
            object? serverStatementId = Invoke(statement, "g");
            RuntimeAssert.Require(ReferenceEquals(Invoke(statement, "f"), attempt.Command)
                && ReferenceEquals(Property(instance, "Session"), owner.Session)
                && ReferenceEquals(Property(instance, "Conn"), attempt.Command.Connection)
                && ReferenceEquals(Field(attempt.Command, "statementSession"), owner.Session)
                && ReferenceEquals(Field(attempt.Command, "statementConnection"), attempt.Command.Connection)
                && serverStatementId is int, "audit_statement_owner_mismatch");
            object? info = Invoke(statement, "H");
            Array parameters = info == null ? throw new RuntimeAssertionException("audit_current_info_missing")
                : Invoke(info, "GetParamsInfo") as Array ?? throw new RuntimeAssertionException("audit_parameters_missing");
            RuntimeAssert.Require(parameters.Length is > 0 and <= 32, "audit_parameter_count_scope");
            object plan = Field(attempt.Command, "activePlan") ?? throw new RuntimeAssertionException("audit_active_plan_missing");
            RuntimeAssert.Require(ReferenceEquals(Property(plan, "Connection"), attempt.Command.Connection), "audit_plan_connection_mismatch");
            object binding = Property(plan, "Binding") ?? throw new RuntimeAssertionException("audit_plan_binding_missing");
            MethodInfo resolve = binding.GetType().GetMethod("ResolveServerParameter", InstanceFlags, [typeof(int), typeof(string), typeof(int)])
                ?? throw new RuntimeAssertionException("audit_readonly_binding_selector_missing");
            var snapshots = new List<object>();
            for (int ordinal = 0; ordinal < parameters.Length; ordinal++)
            {
                object server = parameters.GetValue(ordinal)!;
                string serverName = Invoke(server, "GetName") as string ?? "";
                RuntimeAssert.Require(serverName.Length <= 128, "audit_server_name_limit");
                var resolved = (DbParameter)resolve.Invoke(binding, [ordinal, serverName, parameters.Length])!;
                var publicMatch = attempt.Command.Parameters.Cast<DbParameter>().Select((parameter, index) => (parameter, index))
                    .Single(pair => pair.parameter.ParameterName == resolved.ParameterName);
                RuntimeAssert.Require(publicMatch.parameter.DbType == resolved.DbType && publicMatch.parameter.Scale == resolved.Scale
                    && publicMatch.parameter.Precision == resolved.Precision && publicMatch.parameter.Direction == resolved.Direction
                    && JsonSerializer.Serialize(Value(publicMatch.parameter.Value)) == JsonSerializer.Serialize(Value(resolved.Value)), "audit_bound_public_parameter_mismatch");
                snapshots.Add(new { ordinal, server_parameter_name_sha256 = Hash(serverName), public_parameter_index = publicMatch.index,
                    public_parameter_name_sha256 = Hash(publicMatch.parameter.ParameterName), server_inout_type = Invoke(server, "GetInOutType"),
                    request = Parameter(publicMatch.parameter), raw = Raw(server) });
            }
            RuntimeAssert.Require(!PrepareSources.Any(source => (int)source["attempt_id"]! == attempt.Id), "audit_reprepare_not_in_finite_contract");
            PrepareSources.Add(new() { ["stage"] = attempt.Stage, ["attempt_id"] = attempt.Id, ["ef_command_id"] = attempt.EfCommandId,
                ["sequence"] = ++eventOrdinal, ["response_sequence"] = attempt.LastFrame!["sequence"], ["request_opcode"] = (int)request,
                ["capture_point"] = "after_prepare_decode_before_bind", ["operation_identity"] = owner.Numbers,
                ["server_statement_id"] = serverStatementId, ["statement_reference_token"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(statement),
                ["command_reference_token"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(attempt.Command),
                ["connection_reference_token"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(attempt.Command.Connection!),
                ["statement_type"] = statementType, ["row_count"] = rows, ["affected"] = affected, ["column_count"] = columns,
                ["has_result_set"] = hasResult, ["terminal"] = terminal, ["parameters"] = snapshots.ToArray() });
        }
        catch { ObserverFailed = true; }
    }
    internal void CloseStage(string stage)
    { if (active?.Stage == stage) { Commands.Add(new() { ["stage"] = stage, ["attempt_id"] = active.Id, ["status"] = "window_closed" }); active = null; } }
    internal object[] Sources(string stage)
    {
        RuntimeAssert.Require(!ObserverFailed, "audit_prepare_observer_failed");
        return PrepareSources.Where(source => (string)source["stage"]! == stage).Cast<object>().ToArray();
    }
    internal void BeginControlled(DbCommand command) => Before(command, "controlled_ADO_ExecuteNonQueryAsync", null);
    internal void CompleteControlled(DbCommand command, int rows) => After(command, "controlled_ADO_ExecuteNonQueryAsync_completed", rows: rows);
    internal void FailControlled(DbCommand command, Exception error) => Failed(command, error);
    internal object ConfirmAsyncWrite(string stage)
    {
        int frames = BusinessExecuteCount(stage);
        var source = PrepareSources.SingleOrDefault(source => (string)source["stage"]! == stage);
        RuntimeAssert.Require(installed && !ObserverFailed && frames > 0 && source != null, "async_write_positive_control_missing");
        long[] identity = (long[])source!["operation_identity"]!;
        RuntimeAssert.Require(Sends.Any(sent => (string)sent["stage"]! == stage && (int)sent["opcode"]! is 6 or 13
            && (long)sent["session_id"]! == identity[0] && (long)sent["lease_generation"]! == identity[1]
            && (long)sent["execution_id"]! == identity[2] && (long)sent["invocation_id"]! == identity[3]), "async_positive_control_Execute_identity_mismatch");
        var completed = Commands.SingleOrDefault(command => command.GetValueOrDefault("stage") as string == stage
            && command.GetValueOrDefault("status") as string == "completed" && command.GetValueOrDefault("attempt_id") is int id && id == (int)source["attempt_id"]!
            && command.GetValueOrDefault("api") as string is "ReaderExecutedAsync" or "NonQueryExecutedAsync");
        RuntimeAssert.Require(completed != null && JsonSerializer.SerializeToElement(completed["binding"], SafeEvidence.Json).EnumerateArray()
            .Any(binding => binding.GetProperty("binding_available").ValueKind == JsonValueKind.True), "async_positive_control_published_binding_missing");
        object receipt = new { stage, business_execute_frames = frames, source = "actual_successful_EF_SaveChangesAsync_AfterFrameSent",
            prepare_source = source, completed_command_attempt = completed,
            sent_identity = Sends.Where(item => (string)item["stage"]! == stage && (int)item["opcode"]! is 6 or 13).ToArray() };
        AsyncWriteControls.Add(receipt); return receipt;
    }
    internal object RefusalSendEvidence(string stage, bool async)
    {
        if (!async) return new { observation = "sync_send_unobserved", business_execute_frames = (int?)null, async_positive_controls = (object?)null };
        RuntimeAssert.Require(installed && !ObserverFailed && AsyncWriteControls.Count > 0, "async_refusal_positive_control_missing");
        RuntimeAssert.Require(BusinessExecuteCount(stage) == 0, "refusal_business_Execute_sent");
        return new { observation = "measured_async_Execute0", business_execute_frames = (int?)0,
            async_positive_controls = (object?)AsyncWriteControls.ToArray() };
    }
    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static object Value(object? value) => value switch
    {
        null or DBNull => new { is_null = true },
        DateTime date => new { clr_type = "DateTime", ticks = date.Ticks, kind = date.Kind.ToString() },
        DateTimeOffset date => new { clr_type = "DateTimeOffset", local_ticks = date.Ticks, offset_minutes = (int)date.Offset.TotalMinutes },
        DateOnly date => new { clr_type = "DateOnly", day_number = date.DayNumber },
        TimeOnly time => new { clr_type = "TimeOnly", ticks = time.Ticks },
        TimeSpan span => new { clr_type = "TimeSpan", ticks = span.Ticks },
        int number => new { clr_type = "Int32", value = number },
        string text when text is "seed" or "first" or "changed" or "stale" or "rollback" or "generated" => new { clr_type = "String", value = text },
        _ => new { clr_type = value.GetType().FullName, value_omitted = true }
    };
    private static object Parameter(DbParameter parameter) => new
    {
        db_type = parameter.DbType.ToString(), provider_type = ((DmParameter)parameter).DmSqlType.ToString(),
        precision = (int)parameter.Precision, scale = (int)parameter.Scale,
        explicit_precision = Field(parameter, "m_SetPrecFlag"), explicit_scale = Field(parameter, "m_SetScaleFlag"),
        direction = parameter.Direction.ToString(), value = Value(parameter.Value)
    };
    private static object Raw(object parameter)
    {
        object? raw = Property(parameter, "TemporalServerMetadata");
        return new { available = raw != null, source = Property(raw, "Source")?.ToString(),
            c_type = Property(raw, "CType"), precision = Property(raw, "Precision"), scale = Property(raw, "Scale"),
            type_flag = Property(raw, "TypeFlag"), mask = Property(raw, "MaskBeforeNormalization"),
            derived_mask = Property(raw, "DerivedMask"), result_format = Property(raw, "ResultFormat")?.ToString() };
    }
    private Dictionary<string, object?> Before(DbCommand command, string api, Guid? efCommandId)
    {
        RuntimeAssert.Require(active == null, "audit_overlapping_command_window");
        RuntimeAssert.Require(command.Connection != null, "audit_command_connection_missing");
        foreach (DbParameter parameter in command.Parameters)
            if (parameter.Value is DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan
                || parameter.DbType is DbType.Date or DbType.Time or DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset
                || parameter is DmParameter dm && dm.DmSqlType == DmDbType.IntervalDayToSecond)
                RuntimeAssert.Require(parameter.Precision == 0, "actual_EF_temporal_parameter_Precision_nonzero");
        var item = new Dictionary<string, object?> { ["stage"] = Stage, ["api"] = api, ["status"] = "attempted",
            ["attempt_id"] = ++attemptOrdinal, ["ef_command_id"] = efCommandId,
            ["sql_sha256"] = Hash(command.CommandText), ["sql_length"] = command.CommandText.Length,
            ["connection"] = guard.VerifyObject(command.Connection!, "connection"), ["command"] = guard.VerifyObject(command, "command"),
            ["parameters"] = command.Parameters.Cast<DbParameter>().Select(parameter => new
            {
                actual = guard.VerifyObject(parameter, "parameter"), db_type = parameter.DbType.ToString(),
                provider_type = ((DmParameter)parameter).DmSqlType.ToString(),
                precision = (int)parameter.Precision, scale = (int)parameter.Scale, direction = parameter.Direction.ToString(),
                explicit_precision = Field(parameter, "m_SetPrecFlag"), explicit_scale = Field(parameter, "m_SetScaleFlag"),
                value = Value(parameter.Value)
            }).ToArray() };
        Commands.Add(item); active = new(command, Stage, attemptOrdinal, efCommandId); return item;
    }
    private void After(DbCommand command, string api, DbDataReader? reader = null, int? rows = null, Guid? efCommandId = null)
    {
        RuntimeAssert.Require(active != null && ReferenceEquals(active.Command, command) && active.Stage == Stage
            && active.EfCommandId == efCommandId, "audit_completion_attempt_mismatch");
        var item = new Dictionary<string, object?> { ["stage"] = Stage, ["api"] = api, ["status"] = "completed",
            ["attempt_id"] = active!.Id,
            ["sql_sha256"] = Hash(command.CommandText), ["row_count"] = rows, ["binding"] = Describe(command, reader) };
        if (reader != null) item["reader"] = guard.VerifyObject(reader, "reader");
        Commands.Add(item); active = null;
    }
    internal static object[] Describe(DbCommand command, DbDataReader? reader = null)
    {
        object? statement = Field(reader ?? (object)command, reader == null ? "m_Stmt" : "m_Statement");
        object? info = statement?.GetType().GetMethod("H", InstanceFlags, Type.EmptyTypes)?.Invoke(statement, null);
        object? parameters = info?.GetType().GetMethod("GetParamsInfo", InstanceFlags, Type.EmptyTypes)?.Invoke(info, null);
        if (parameters is not Array array) return [];
        return array.Cast<object>().Select((parameter, ordinal) =>
        {
            object? Read(object source, string name) => source.GetType().GetProperty(name, InstanceFlags)?.GetValue(source);
            object? binding = Read(parameter, "TemporalBinding"), raw = Read(parameter, "TemporalServerMetadata");
            object? request = binding == null ? null : Read(binding, "Request");
            int? Number(object? source, string name) => source == null ? null : Read(source, name) is int number ? number : null;
            return (object)new
            {
                ordinal,
                raw_available = raw != null, raw_source = raw == null ? null : Read(raw, "Source")?.ToString(),
                raw_c_type = Number(raw, "CType"), raw_precision = Number(raw, "Precision"), raw_scale = Number(raw, "Scale"),
                raw_type_flag = Number(raw, "TypeFlag"), raw_mask = Number(raw, "MaskBeforeNormalization"),
                raw_derived_mask = Number(raw, "DerivedMask"), raw_result_format = raw == null ? null : Read(raw, "ResultFormat")?.ToString(),
                binding_available = binding != null, reliable_fixed = binding == null ? null : Read(binding, "ReliableFixed") as bool?,
                c_type = Number(binding, "CType"), encoded_scale = Number(binding, "EncodedScale"), wire_scale = Number(binding, "WireScale"),
                requested_scale = Number(binding, "RequestedScale"), column_scale = Number(binding, "ColumnScale"),
                request_explicit_scale = request == null ? null : Read(request, "ExplicitScale") as bool?,
                profile_state = binding == null ? null : Property(Read(binding, "Profile"), "State")?.ToString()
            };
        }).ToArray();
    }
    internal static object? Field(object value, string name)
    {
        for (Type? type = value.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(value);
        return null;
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Before(command, "ReaderExecuting", eventData.CommandId); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Before(command, "ReaderExecutingAsync", eventData.CommandId); return ValueTask.FromResult(result); }
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { After(command, "ReaderExecuted", result, efCommandId: eventData.CommandId); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    { After(command, "ReaderExecutedAsync", result, efCommandId: eventData.CommandId); return ValueTask.FromResult(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Before(command, "NonQueryExecuting", eventData.CommandId); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Before(command, "NonQueryExecutingAsync", eventData.CommandId); return ValueTask.FromResult(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { After(command, "NonQueryExecuted", rows: result, efCommandId: eventData.CommandId); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    { After(command, "NonQueryExecutedAsync", rows: result, efCommandId: eventData.CommandId); return ValueTask.FromResult(result); }
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        => Failed(command, eventData.Exception, eventData.CommandId);
    private void Failed(DbCommand command, Exception error, Guid? efCommandId = null)
    {
        RuntimeAssert.Require(active != null && ReferenceEquals(active.Command, command) && active.Stage == Stage
            && active.EfCommandId == efCommandId, "audit_failure_attempt_mismatch");
        Commands.Add(new() { ["stage"] = Stage, ["attempt_id"] = active!.Id, ["status"] = "failed", ["error"] = SafeEvidence.Safe(error),
            ["actual_exception_chain"] = RuntimeRefusal.Evidence(error), ["sql_sha256"] = Hash(command.CommandText),
            ["current_binding_observed"] = false, ["current_binding"] = null,
            ["parameter_describe_sources"] = PrepareSources.Where(source => (int)source["attempt_id"]! == active.Id).ToArray() });
        active = null;
    }
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { CommandFailed(command, eventData); return Task.CompletedTask; }
    public void Dispose()
    {
        active = null;
        if (installed)
        {
            try { sendHook.SetValue(null, previousHook); }
            finally { try { frameHook.SetValue(null, previousFrameHook); } finally { decodeHook.SetValue(null, previousDecodeHook); installed = false; } }
        }
    }
}

internal sealed class RuntimeConnectionAudit(CandidateGuard guard, CommandAudit audit) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => audit.Connections.Add(guard.VerifyOpenedConnection(connection));
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    { ConnectionOpened(connection, eventData); return Task.CompletedTask; }
}
