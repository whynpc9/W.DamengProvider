namespace TP07.Runtime;

internal sealed class RuntimeAssertionException(string id) : InvalidOperationException("TP07_runtime_assertion_failed")
{ internal string AssertionId { get; } = id; }

internal static class RuntimeAssert
{
    internal static void Require(bool value, string fixedAssertionId)
    { if (!value) throw new RuntimeAssertionException(fixedAssertionId); }
}

internal static class RuntimeRefusal
{
    private static IReadOnlyList<Exception> BoundedChain(Exception error)
    {
        var chain = new List<Exception>();
        for (Exception? current = error; current != null; current = current.InnerException)
        {
            RuntimeAssert.Require(chain.Count < 8 && !chain.Any(previous => ReferenceEquals(previous, current)), "exception_chain_unbounded");
            chain.Add(current);
        }
        return chain;
    }
    internal static object Evidence(Exception error) => BoundedChain(error).Select((actual, depth) => new
    { depth, type = actual.GetType().FullName, safe = SafeEvidence.Safe(actual) }).ToArray();
    internal static bool Matches(Exception error, Type expectedLeaf)
    {
        var chain = BoundedChain(error);
        return chain[^1].GetType() == expectedLeaf
            && chain.Take(chain.Count - 1).All(wrapper => wrapper.GetType() == typeof(Microsoft.EntityFrameworkCore.DbUpdateException));
    }
}

internal sealed class TemporalRow
{
    public int Id { get; set; }
    public DateTime? D { get; set; }
    public DateTimeOffset? Z { get; set; }
    public string? N { get; set; }
    public DateOnly? A { get; set; }
    public TimeOnly? T { get; set; }
    public TimeSpan? I { get; set; }
}

internal sealed record ModelSpec(string Table, string Shape, string Profile, string Kind,
    int Scale, string PrimaryKey, string? DefaultD = null, string? DefaultZ = null, long? SeedTicks = null, int? SeedOffset = null)
{
    internal string CacheIdentity => string.Join("|", Table, Shape, Profile, Kind, Scale.ToString(System.Globalization.CultureInfo.InvariantCulture), PrimaryKey,
        DefaultD ?? "", DefaultZ ?? "", SeedTicks?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        SeedOffset?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
}

// Finite source-defined input registry. The independent validator reconstructs these expectations;
// producer reports alone do not establish an oracle. D must later run actual CLI-generated scaffold6 source.
internal sealed record RuntimeCase(string CaseId, string Group, string Profile, bool Async,
    string Shape, string Kind, long? Ticks, int? OffsetMinutes, int Scale, string Route, string Expected)
{
    internal DateTime DateTimeValue => new(Ticks ?? throw new InvalidOperationException("case_ticks_missing"), DateTimeKind.Unspecified);
    internal DateTimeOffset OffsetValue => new(DateTimeValue, TimeSpan.FromMinutes(OffsetMinutes ?? 0));
}

internal static class RuntimeCases
{
    internal const string ScaffoldFollowup = "pending_D_actual_catalog_CLI_generated_model_SaveChanges_fresh_read_no_auto_upgrade";
    internal static readonly long Ordinary = new DateTime(2026, 7, 23, 14, 15, 16, DateTimeKind.Unspecified).Ticks;
    internal static IReadOnlyList<RuntimeCase> Required(string profile, string group)
    {
        RuntimeAssert.Require(profile is "shared" or "tls", "runtime_profile_invalid");
        RuntimeAssert.Require(group is "roundtrip" or "mutation" or "six" or "generated", "runtime_group_invalid");
        var result = new List<RuntimeCase>();
        int scale = profile == "shared" ? 7 : 6;
        long quantum = profile == "shared" ? 1 : 10;
        foreach (bool async in new[] { false, true })
        {
            string api = async ? "async" : "sync";
            if (group == "roundtrip")
            {
                var vectors = profile == "shared"
                    ? new List<(string, long)> { ("min", 0), ("min_plus_tick", 1), ("max", DateTime.MaxValue.Ticks), ("max_minus_tick", DateTime.MaxValue.Ticks - 1), ("max_minus_9ticks", DateTime.MaxValue.Ticks - 9) }
                    : new List<(string, long)> { ("min_micro", 0), ("min_plus_micro", 10), ("max_whole_micro", DateTime.MaxValue.Ticks - 9) };
                for (int tail = 1; tail <= 9; tail++) vectors.Add((profile == "shared" ? "tail_tick_" + tail : "tail_micro_" + tail, Ordinary + tail * quantum));
                foreach (string kind in new[] { "dt", "dto" })
                {
                    foreach ((string name, long ticks) in vectors)
                        result.Add(new($"EF-R01-{profile}-{kind}-{name}-{api}", group, profile, async, "roundtrip", kind, ticks,
                            kind == "dto" ? ticks >= Ordinary && ticks < Ordinary + TimeSpan.TicksPerSecond ? 330 : 0 : null,
                            scale, "actual_SaveChanges_fresh_AsNoTracking", "exact"));
                    result.Add(new($"EF-R01-{profile}-{kind}-value_null_reuse-{api}", group, profile, async, "reuse", kind, Ordinary + quantum,
                        kind == "dto" ? 330 : null, scale, "same_context_SaveChanges_value_then_NULL_parameterized_query", "exact_NULL"));
                }
            }
            if (group == "mutation")
                foreach (string kind in new[] { "dt", "dto" })
                    foreach (string shape in new[] { "update", "concurrency", "rollback" })
                        result.Add(new($"EF-R02-{profile}-{kind}-{shape}-{api}", group, profile, async, shape, kind, Ordinary + 1234560,
                            kind == "dto" ? 330 : null, scale, "actual_EF_tracked_update_transaction", shape == "concurrency" ? "DbUpdateConcurrencyException_then_fresh_unchanged" : "exact"));
            if (group == "six")
            {
                foreach (string shape in new[] { "facet6", "store6", "default7_fixed6" })
                    foreach (string kind in new[] { "dt", "dto" })
                        foreach (string value in new[] { "micro", "NULL", "submicro_reject" })
                            result.Add(new($"EF-R03-{profile}-{shape}-{kind}-{value}-{api}", group, profile, async, shape, kind,
                                value == "NULL" ? null : Ordinary + (value == "micro" ? 1234560 : 1234561), kind == "dto" ? 330 : null,
                                6, "actual_SaveChanges_existing_fixed6", shape == "default7_fixed6" ? "actual_describe_explicit7_branch_fresh_state"
                                    : value == "submicro_reject" ? "Overflow_fresh_rows0_" + (async ? "verified_async_Execute0_positive_control" : "sync_send_unobserved") : "exact"));
                foreach (string kind in new[] { "date", "time", "interval" })
                    foreach (string value in new[] { "value", "NULL", "reject" })
                    {
                        long ticks = kind switch
                        {
                            "date" => new DateTime(2026, 7, 23).Ticks,
                            "time" => new TimeSpan(0, 14, 15, 16).Ticks + 1234560,
                            _ => new TimeSpan(2, 3, 4, 5).Ticks + 1234560
                        };
                        if (value == "reject") ticks += 1;
                        result.Add(new($"EF-R03-{profile}-native-{kind}-{value}-{api}", group, profile, async,
                            value == "reject" && kind == "date" ? "DATE_nonmidnight" : "native", kind,
                            value == "NULL" ? null : ticks, null, 6, "actual_SaveChanges_fresh_native_query",
                            value == "reject" ? (kind == "date" ? "Overflow" : "Argument") + "_fresh_rows0_"
                                + (async ? "verified_async_Execute0_positive_control" : "sync_send_unobserved") : "exact"));
                    }
            }
            if (group == "generated")
            {
                foreach (string shape in new[] { "constant_default", "HasData", "sql_default", "NULL_default", "inline", "update_expression", "clock_default" })
                    result.Add(new($"EF-R04-{profile}-{shape}-{api}", group, profile, async, shape, "pair",
                        shape is "NULL_default" or "clock_default" ? null : Ordinary + (profile == "shared" ? 1234567 : 1234560), shape == "clock_default" ? null : 330,
                        scale, "actual_generated_migration_SQL_EF_readback", shape == "clock_default" ? "reported_actual_clock_precision" : "exact"));
                // L0's ordinary literals do not establish persistence at the calendar/tick boundaries.
                var boundaries = profile == "shared"
                    ? new[] { ("min", 0L), ("min_plus_tick", 1L), ("max", DateTime.MaxValue.Ticks), ("max_minus_tick", DateTime.MaxValue.Ticks - 1), ("max_minus_9ticks", DateTime.MaxValue.Ticks - 9) }
                    : new[] { ("min_micro", 0L), ("min_plus_micro", 10L), ("max_whole_micro", DateTime.MaxValue.Ticks - 9) };
                foreach (string shape in new[] { "constant_default", "HasData", "sql_default", "inline" })
                    foreach ((string vector, long ticks) in boundaries)
                        result.Add(new($"EF-R04-{profile}-{shape}-{vector}-{api}", group, profile, async, shape, "pair", ticks, 0,
                            scale, "actual_generated_migration_SQL_EF_readback", "exact_boundary"));
            }
        }
        if (group == "six" && profile == "tls")
            foreach (string kind in new[] { "dt", "dto" })
                result.Add(new($"EF-R03-tls-{kind}-advisory7-capability-async", group, profile, true, "advisory7", kind,
                    Ordinary + 1, kind == "dto" ? 330 : null, 7, "EF_mapping_created_parameter_controlled_ADO_execute_not_SaveChanges",
                    "NotSupported_with_observed_advisory_or_fixed7_metadata_verified_async_Execute0_positive_control"));
        RuntimeAssert.Require(result.Select(item => item.CaseId).Distinct(StringComparer.Ordinal).Count() == result.Count, "runtime_duplicate_case");
        return result;
    }
}
