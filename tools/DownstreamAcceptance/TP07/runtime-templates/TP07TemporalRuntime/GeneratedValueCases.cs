using Microsoft.EntityFrameworkCore;

namespace TP07.Runtime;

internal static class GeneratedValueCases
{
    internal static async Task Run(RuntimeHarness harness, RuntimeCase @case, Dictionary<string, object?> item)
    {
        string? defaultD = null, defaultZ = null;
        if (@case.Shape == "sql_default")
        {
            // Generate both defaults from actual model-selected mappings, not a hand-written equivalent literal.
            var bootstrap = new ModelSpec("TP07_LITERAL_MODEL", "roundtrip", @case.Profile, "pair", @case.Scale, "TP07_LITERAL_MODEL_PK");
            await using var model = harness.Context(bootstrap);
            defaultD = model.Mapping("D").GenerateSqlLiteral(@case.DateTimeValue);
            defaultZ = model.Mapping("Z").GenerateSqlLiteral(@case.OffsetValue);
            item["default_literal_mapping"] = new { d = model.Mapping("D").GetType().FullName, z = model.Mapping("Z").GetType().FullName };
        }
        else if (@case.Shape == "NULL_default") { defaultD = defaultZ = "NULL"; }
        else if (@case.Shape == "clock_default") { defaultD = defaultZ = "CURRENT_TIMESTAMP"; }
        ModelSpec spec = await harness.Create(@case, item, defaultD, defaultZ);
        await using var context = harness.Context(spec);
        TemporalRow? generated = null;
        if (@case.Shape is "constant_default" or "sql_default" or "NULL_default" or "clock_default")
        {
            generated = new TemporalRow { Id = 1, N = "generated" };
            context.Rows.Add(generated);
            RuntimeAssert.Require(await harness.Step(item, "SaveChanges_generated_on_add", () => RuntimeHarness.Save(context, @case.Async), count => new { affected_entities = count }) == 1, "generated_save_count");
            item["generated_current_values"] = RuntimeHarness.Row(generated);
            item["generated_original_values"] = new
            {
                d = CommandAudit.Value(context.Entry(generated).Property(row => row.D).OriginalValue),
                z = CommandAudit.Value(context.Entry(generated).Property(row => row.Z).OriginalValue)
            };
        }
        if (@case.Shape == "inline")
        {
            DateTime dt = @case.DateTimeValue; DateTimeOffset dto = @case.OffsetValue;
            var query = context.Rows.AsNoTracking().Where(row => row.D == EF.Constant(dt) && row.Z == EF.Constant(dto));
            item["inline_query_SQL_sha256"] = CommandAudit.Hash(query.ToQueryString());
            TemporalRow inline = await harness.Step(item, "actual_inline_EF_query", () => @case.Async ? query.SingleAsync() : Task.FromResult(query.Single()), RuntimeHarness.Row);
            Pair(@case, inline, @case.Ticks, @case.Ticks);
            var executions = harness.Audit.Commands.Where(command => command.GetValueOrDefault("stage") as string == @case.CaseId + "/actual_inline_EF_query"
                && command.ContainsKey("parameters")).ToArray();
            RuntimeAssert.Require(executions.Length > 0 && executions.All(command => ((Array)command["parameters"]!).Length == 0), "inline_query_parameterized_instead_of_literal");
        }
        long? expectedD = @case.Ticks, expectedZ = @case.Ticks;
        if (@case.Shape == "update_expression")
        {
            // DATEADD(second,1,D) is produced by the pinned DateTime method translator. DTO stays unchanged.
            int rows = await harness.Step(item, "actual_EF_server_update_expression", () => @case.Async
                ? context.Rows.Where(row => row.Id == 1).ExecuteUpdateAsync(setters => setters.SetProperty<DateTime?>(row => row.D, row => row.D!.Value.AddSeconds(1)))
                : Task.FromResult(context.Rows.Where(row => row.Id == 1).ExecuteUpdate(setters => setters.SetProperty<DateTime?>(row => row.D, row => row.D!.Value.AddSeconds(1)))),
                count => new { affected_rows = count });
            RuntimeAssert.Require(rows == 1, "generated_update_expression_row_count"); expectedD = @case.Ticks!.Value + TimeSpan.TicksPerSecond;
        }
        TemporalRow fresh = await harness.Fresh(spec, @case.Async, item, "fresh_generated_materialization");
        if (@case.Shape == "clock_default")
        {
            RuntimeAssert.Require(fresh.D.HasValue && fresh.Z.HasValue, "clock_default_missing");
            DateTime clockD = fresh.D!.Value; DateTimeOffset clockZ = fresh.Z!.Value;
            RuntimeAssert.Require(generated!.D?.Ticks == clockD.Ticks && generated.Z is DateTimeOffset generatedZ
                && generatedZ.Ticks == clockZ.Ticks && generatedZ.Offset == clockZ.Offset, "clock_generated_readback_mismatch");
            expectedD = clockD.Ticks; expectedZ = clockZ.Ticks;
            item["clock_actual"] = new { d_ticks = expectedD, z_local_ticks = expectedZ, offset_minutes = (int)clockZ.Offset.TotalMinutes,
                d_ticks_within_second = expectedD % TimeSpan.TicksPerSecond, z_ticks_within_second = expectedZ % TimeSpan.TicksPerSecond,
                configured_column_scale = @case.Scale, no_seventh_digit_claim = true };
        }
        else Pair(@case, fresh, expectedD, expectedZ);
        if (generated != null && @case.Shape != "clock_default")
        {
            Pair(@case, generated, expectedD, expectedZ);
            RuntimeAssert.Require(context.Entry(generated).Property(row => row.D).OriginalValue?.Ticks == expectedD
                && context.Entry(generated).Property(row => row.Z).OriginalValue?.Ticks == expectedZ, "generated_original_values_mismatch");
        }
        harness.Independent(spec, @case with { Kind = "dt", Ticks = expectedD }, item);
        harness.Independent(spec, @case with { Kind = "dto", Ticks = expectedZ, OffsetMinutes = fresh.Z.HasValue ? (int)fresh.Z.Value.Offset.TotalMinutes : @case.OffsetMinutes }, item);
        await using var rowCount = harness.Context(spec);
        RuntimeAssert.Require(await harness.Step(item, "fresh_generated_row_count", () => RuntimeHarness.Count(rowCount, @case.Async)) == 1, "generated_persisted_count");
    }
    private static void Pair(RuntimeCase @case, TemporalRow row, long? dtTicks, long? dtoTicks)
    {
        RuntimeAssert.Require(row.D?.Ticks == dtTicks, "generated_DT_ticks_mismatch");
        RuntimeAssert.Require(row.Z?.Ticks == dtoTicks, "generated_DTO_localticks_mismatch");
        RuntimeAssert.Require(!row.Z.HasValue || (int)row.Z.Value.Offset.TotalMinutes == @case.OffsetMinutes, "generated_DTO_offset_mismatch");
    }
}
