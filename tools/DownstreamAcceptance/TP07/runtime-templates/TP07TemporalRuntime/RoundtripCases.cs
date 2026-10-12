using Microsoft.EntityFrameworkCore;

namespace TP07.Runtime;

internal static class RoundtripCases
{
    internal static async Task Run(RuntimeHarness harness, RuntimeCase @case, Dictionary<string, object?> item)
    {
        ModelSpec spec = await harness.Create(@case, item);
        await using var context = harness.Context(spec);
        var entity = new TemporalRow { Id = 1, N = "first" };
        Set(@case, entity);
        context.Rows.Add(entity);
        int inserted = await harness.Step(item, "SaveChanges_insert", () => RuntimeHarness.Save(context, @case.Async), rows => new { affected_entities = rows });
        RuntimeAssert.Require(inserted == 1, "roundtrip_insert_count");
        TemporalRow persisted = await harness.Fresh(spec, @case.Async, item, "fresh_AsNoTracking");
        RuntimeHarness.Exact(@case, persisted); harness.Independent(spec, @case, item);
        if (@case.Shape != "reuse") return;
        long changedTicks = @case.Ticks!.Value + (@case.Profile == "shared" ? 1 : 10);
        RuntimeCase changed = @case with { Ticks = changedTicks };
        Set(changed, entity);
        int updated = await harness.Step(item, "SaveChanges_value_only", () => RuntimeHarness.Save(context, @case.Async), rows => new { affected_entities = rows });
        RuntimeAssert.Require(updated == 1, "roundtrip_value_only_count");
        DateTime dt = changed.DateTimeValue; DateTimeOffset dto = changed.OffsetValue;
        IQueryable<TemporalRow> query = changed.Kind == "dt"
            ? context.Rows.AsNoTracking().Where(row => row.D == dt)
            : context.Rows.AsNoTracking().Where(row => row.Z == dto);
        TemporalRow selected = await harness.Step(item, "actual_parameterized_query_changed_value",
            () => @case.Async ? query.SingleAsync() : Task.FromResult(query.Single()), RuntimeHarness.Row);
        RuntimeHarness.Exact(changed, selected); harness.Independent(spec, changed, item);
        RuntimeCase nullCase = @case with { Ticks = null };
        Set(nullCase, entity);
        int cleared = await harness.Step(item, "SaveChanges_NULL_only", () => RuntimeHarness.Save(context, @case.Async), rows => new { affected_entities = rows });
        RuntimeAssert.Require(cleared == 1, "roundtrip_NULL_update_count");
        TemporalRow nullRow = await harness.Fresh(spec, @case.Async, item, "fresh_AsNoTracking_NULL");
        RuntimeHarness.Exact(nullCase, nullRow); harness.Independent(spec, nullCase, item);
    }
    internal static void Set(RuntimeCase @case, TemporalRow entity)
    {
        if (@case.Kind == "dt") entity.D = @case.Ticks.HasValue ? @case.DateTimeValue : null;
        else entity.Z = @case.Ticks.HasValue ? @case.OffsetValue : null;
    }
}
