using Microsoft.EntityFrameworkCore;

namespace TP07.Runtime;

internal static class MutationCases
{
    internal static async Task Run(RuntimeHarness harness, RuntimeCase @case, Dictionary<string, object?> item)
    {
        ModelSpec spec = await harness.Create(@case, item);
        await using (var seed = harness.Context(spec))
        {
            var initial = new TemporalRow { Id = 1, N = "first" }; RoundtripCases.Set(@case, initial); seed.Rows.Add(initial);
            RuntimeAssert.Require(await harness.Step(item, "seed_SaveChanges", () => RuntimeHarness.Save(seed, @case.Async)) == 1, "mutation_seed_count");
        }
        RuntimeHarness.OwnedState before = await harness.FreshState(spec, @case.Async, item, "fresh_state_before_mutation", 1);
        long changedTicks = @case.Ticks!.Value + (@case.Profile == "shared" ? 1 : 10);
        var changed = @case with { Ticks = changedTicks };
        await using var currentContext = harness.Context(spec);
        TemporalRow current = await harness.Step(item, "tracking_query_current", () => RuntimeHarness.Single(currentContext, @case.Async, true), RuntimeHarness.Row);
        RuntimeHarness.Exact(@case, current);
        if (@case.Shape == "concurrency")
        {
            await using var staleContext = harness.Context(spec);
            TemporalRow stale = await harness.Step(item, "tracking_query_stale", () => RuntimeHarness.Single(staleContext, @case.Async, true), RuntimeHarness.Row);
            RuntimeHarness.Exact(@case, stale);
            RoundtripCases.Set(changed, current); current.N = "changed";
            RuntimeAssert.Require(await harness.Step(item, "winner_SaveChanges", () => RuntimeHarness.Save(currentContext, @case.Async)) == 1, "concurrency_winner_count");
            RuntimeHarness.OwnedState winner = await harness.FreshState(spec, @case.Async, item, "fresh_state_after_winner_before_stale", 1);
            RuntimeHarness.SameState(before, winner);
            stale.N = "stale"; RoundtripCases.Set(@case with { Ticks = changedTicks + (@case.Profile == "shared" ? 1 : 10) }, stale);
            string token = @case.Kind == "dt" ? nameof(TemporalRow.D) : nameof(TemporalRow.Z);
            object? original = staleContext.Entry(stale).Property(token).OriginalValue;
            item["original_temporal_concurrency_token"] = CommandAudit.Value(original);
            RuntimeAssert.Require(original is DateTime date && @case.Kind == "dt" && date.Ticks == @case.Ticks
                || original is DateTimeOffset offset && @case.Kind == "dto" && offset.Ticks == @case.Ticks && (int)offset.Offset.TotalMinutes == @case.OffsetMinutes,
                "concurrency_original_temporal_value_mismatch");
            Exception? failure = null;
            try { await harness.Step(item, "stale_SaveChanges", () => RuntimeHarness.Save(staleContext, @case.Async)); }
            catch (Exception error) { failure = error; }
            RuntimeAssert.Require(failure != null, "stale_temporal_update_did_not_throw_concurrency");
            item["expected_concurrency_exception"] = new { actual_chain = RuntimeRefusal.Evidence(failure!), expected_leaf_type = typeof(DbUpdateConcurrencyException).FullName };
            RuntimeAssert.Require(RuntimeRefusal.Matches(failure!, typeof(DbUpdateConcurrencyException)), "stale_concurrency_exception_contract_mismatch");
            TemporalRow fresh = await harness.Fresh(spec, @case.Async, item, "fresh_after_stale_failure");
            RuntimeHarness.Exact(changed, fresh); RuntimeAssert.Require(fresh.N == "changed", "stale_update_changed_row"); harness.Independent(spec, changed, item);
            RuntimeHarness.SameState(winner, await harness.FreshState(spec, @case.Async, item, "fresh_state_after_stale_failure", 1));
            return;
        }
        if (@case.Shape == "rollback")
        {
            await using var transaction = @case.Async
                ? await currentContext.Database.BeginTransactionAsync()
                : currentContext.Database.BeginTransaction();
            RoundtripCases.Set(changed, current); current.N = "rollback";
            RuntimeAssert.Require(await harness.Step(item, "transaction_SaveChanges", () => RuntimeHarness.Save(currentContext, @case.Async)) == 1, "rollback_update_count");
            var inside = await harness.Step(item, "query_inside_transaction", () => RuntimeHarness.Single(currentContext, @case.Async), RuntimeHarness.Row);
            RuntimeHarness.Exact(changed, inside);
            await harness.Step(item, "Rollback", async () => { if (@case.Async) await transaction.RollbackAsync(); else transaction.Rollback(); return true; });
            TemporalRow original = await harness.Fresh(spec, @case.Async, item, "fresh_after_rollback");
            RuntimeHarness.Exact(@case, original); RuntimeAssert.Require(original.N == "first", "rollback_original_row_mismatch"); harness.Independent(spec, @case, item);
            RuntimeHarness.SameState(before, await harness.FreshState(spec, @case.Async, item, "fresh_state_after_rollback", 1));
            return;
        }
        RoundtripCases.Set(changed, current); current.N = "changed";
        RuntimeAssert.Require(await harness.Step(item, "tracked_SaveChanges_update", () => RuntimeHarness.Save(currentContext, @case.Async)) == 1, "tracked_update_count");
        var updated = await harness.Fresh(spec, @case.Async, item, "fresh_after_tracked_update"); RuntimeHarness.Exact(changed, updated); harness.Independent(spec, changed, item);
        RuntimeHarness.SameState(before, await harness.FreshState(spec, @case.Async, item, "fresh_state_after_tracked_update", 1));
    }
}
