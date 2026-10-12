using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TP07LiteralProbe;

namespace TP07.Runtime;

internal static class Program
{
    private const int WindowSize = 15;
    private static readonly string[] WindowProfiles = ["shared", "tls"], WindowGroups = ["roundtrip", "six", "mutation", "generated"];
    private sealed record CaseWindow(string Profile, string Group, int Index, int Start, int Count, int FullGroupCount, string[] CaseIds);
    private static (CaseWindow Window, IReadOnlyList<RuntimeCase> Cases) SelectWindow(string profile, string group, int index)
    {
        var all = RuntimeCases.Required(profile, group);
        RuntimeAssert.Require(index >= 0 && index < (all.Count + WindowSize - 1) / WindowSize, "window_index_outside_registry");
        int start = WindowSize * index;
        var selected = all.Skip(start).Take(WindowSize).ToArray();
        bool earlierPositive = false;
        foreach (var item in selected)
        {
            if (item.Group == "six" && item.Async && item.Expected != "exact")
                RuntimeAssert.Require(earlierPositive, "same_window_async_control_prefix_missing");
            if (item.Group == "six" && item.Async && item.Expected == "exact" && item.Ticks.HasValue
                && item.Shape is "facet6" or "store6" or "native") earlierPositive = true;
        }
        return (new(profile, group, index, start, selected.Length, all.Count, selected.Select(c => c.CaseId).ToArray()), selected);
    }
    private static string ArgumentMode(Dictionary<string, string> options)
    {
        if (options.ContainsKey("--offline-partition"))
        {
            RuntimeAssert.Require(options.Count == 2 && options["--offline-partition"] == "true" && options.ContainsKey("--output"), "offline_partition_exact_arguments");
            return "partition";
        }
        if (options.ContainsKey("--offline-generated-create"))
        {
            RuntimeAssert.Require(options["--offline-generated-create"] == "true"
                && options.Keys.All(k => k is "--offline-generated-create" or "--profile" or "--output" or "--candidate-manifest" or "--capabilities"), "offline_generated_no_window_arguments");
            return "generated";
        }
        foreach (string name in new[] { "--profile", "--group", "--output", "--candidate-manifest", "--window-index", "--run-id", "--cohort-id" })
            RuntimeAssert.Require(options.ContainsKey(name), "runtime_window_argument_missing");
        RuntimeAssert.Require(Regex.IsMatch(options["--run-id"], "\\A[a-f0-9]{32}\\z", RegexOptions.CultureInvariant)
            && Regex.IsMatch(options["--cohort-id"], "\\A[a-f0-9]{64}\\z", RegexOptions.CultureInvariant), "runtime_run_cohort_identity");
        RuntimeAssert.Require(Regex.IsMatch(options["--window-index"], "\\A(?:0|[1-9][0-9]{0,2})\\z", RegexOptions.CultureInvariant), "runtime_window_index_text");
        SelectWindow(options["--profile"], options["--group"], int.Parse(options["--window-index"], CultureInfo.InvariantCulture));
        return "runtime";
    }
    internal static async Task<int> Main(string[] args)
    {
        Dictionary<string, string> options = Parse(args);
        string mode = ArgumentMode(options);
        if (mode == "partition") return OfflinePartition(options);
        if (mode == "generated") return OfflineGeneratedCreate(options);
        string profile = Required(options, "--profile"), group = Required(options, "--group"), output = Path.GetFullPath(Required(options, "--output"));
        var selection = SelectWindow(profile, group, int.Parse(options["--window-index"], CultureInfo.InvariantCulture));
        RuntimeAssert.Require(!Directory.Exists(output) && !File.Exists(output), "runtime_output_not_fresh");
        Directory.CreateDirectory(output);
        string ownershipMarker = Path.Combine(output, ".runtime-owner");
        using (var acquisition = new FileStream(ownershipMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] marker = System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")); acquisition.Write(marker);
        }
        RuntimeAssert.Require(Directory.EnumerateFileSystemEntries(output).All(path => path == ownershipMarker), "runtime_output_acquisition_race");
        var report = new Dictionary<string, object?> { ["schema_version"] = 2, ["task"] = "TP07-E", ["profile"] = profile, ["group"] = group,
            ["run_id"] = options["--run-id"], ["cohort_id"] = options["--cohort-id"], ["window"] = selection.Window,
            ["full_group_case_count"] = selection.Window.FullGroupCount,
            ["runtime_accepted"] = false, ["candidate_acceptance"] = false, ["cases"] = new List<Dictionary<string, object?>>(),
            ["scaffold6_followup"] = RuntimeCases.ScaffoldFollowup };
        RuntimeHarness? harness = null;
        try
        {
            var guard = CandidateGuard.Load(profile, Required(options, "--candidate-manifest"), options.GetValueOrDefault("--capabilities"));
            report["candidate_identity"] = guard.VerifyRuntime();
            report["ef_identity"] = new { assembly = typeof(DbContext).Assembly.FullName, location = typeof(DbContext).Assembly.Location,
                relational_assembly = typeof(IMigrationsSqlGenerator).Assembly.FullName,
                provider_assembly = typeof(W.EntityFrameworkCore.Dameng.Infrastructure.Internal.DamengOptionsExtension).Assembly.FullName };
            var cases = selection.Cases;
            report["required_cases"] = cases; report["required_case_count"] = cases.Count;
            harness = new(guard, new OwnedObjects(guard, Path.Combine(output, "ownership.json")), output, report);
            // Construct the actual relational factory and mapping service before the first identity/ownership DB call.
            await using (var bootstrap = harness.Context(new("TP07_E_BOOTSTRAP", "facet6", profile, "pair", 6, "TP07_E_BOOTSTRAP_PK")))
                report["bootstrap_model_built"] = bootstrap.Model.FindEntityType(typeof(TemporalRow)) != null;
            harness.Audit.Install();
            foreach (RuntimeCase @case in cases)
            {
                var item = harness.Begin(@case);
                try
                {
                    switch (group)
                    {
                        case "roundtrip": await RoundtripCases.Run(harness, @case, item); break;
                        case "mutation": await MutationCases.Run(harness, @case, item); break;
                        case "six": await SixPrecisionCases.Run(harness, @case, item); break;
                        case "generated": await GeneratedValueCases.Run(harness, @case, item); break;
                    }
                    RuntimeAssert.Require(!harness.Audit.ObserverFailed, "runtime_protocol_observer_failed");
                    item["status"] = "completed"; harness.Write();
                }
                catch (Exception error) { item["status"] = "failed"; item["error"] = SafeEvidence.Safe(error); if (error is RuntimeAssertionException assertion) item["failed_assertion"] = assertion.AssertionId; harness.Write(); throw; }
            }
            report["execution_completed"] = true;
        }
        catch (Exception error) { report["error"] = SafeEvidence.Safe(error); if (error is RuntimeAssertionException assertion) report["failed_assertion"] = assertion.AssertionId; report["execution_completed"] = false; }
        finally
        {
            if (harness != null)
            {
                try { harness.Audit.Stage = "cleanup"; report["cleanup"] = harness.Owned.Cleanup(); report["cleanup_verified"] = true; }
                catch (Exception error) { report["cleanup_verified"] = false; report["cleanup_error"] = SafeEvidence.Safe(error); }
                finally { harness.Audit.Dispose(); harness.Write(); }
            }
            report["runtime_accepted"] = report.GetValueOrDefault("execution_completed") is true && report.GetValueOrDefault("cleanup_verified") is true;
            SafeEvidence.Write(Path.Combine(output, "runtime.json"), report);
        }
        return report["runtime_accepted"] is true ? 0 : 1;
    }
    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        RuntimeAssert.Require(args.Length % 2 == 0, "runtime_arguments_invalid");
        for (int index = 0; index < args.Length; index += 2)
        {
            RuntimeAssert.Require(args[index] is "--profile" or "--group" or "--output" or "--candidate-manifest" or "--capabilities" or "--offline-generated-create"
                or "--offline-partition" or "--window-index" or "--run-id" or "--cohort-id", "runtime_argument_unknown");
            RuntimeAssert.Require(result.TryAdd(args[index], args[index + 1]), "runtime_argument_duplicate");
        }
        return result;
    }
    private static string Required(Dictionary<string, string> options, string name) => options.TryGetValue(name, out string? value)
        ? value : throw new InvalidOperationException("runtime_required_argument_missing");

    private static int OfflinePartition(Dictionary<string, string> options)
    {
        string output = Path.GetFullPath(Required(options, "--output"));
        RuntimeAssert.Require(!Directory.Exists(output) && !File.Exists(output), "partition_output_not_fresh");
        Directory.CreateDirectory(output);
        using (var marker = new FileStream(Path.Combine(output, ".partition-owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            marker.Write(System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
        var cases = new List<RuntimeCase>(); var windows = new List<CaseWindow>();
        foreach (string profile in WindowProfiles) foreach (string group in WindowGroups)
        {
            var all = RuntimeCases.Required(profile, group); cases.AddRange(all);
            for (int index = 0; index < (all.Count + WindowSize - 1) / WindowSize; index++) windows.Add(SelectWindow(profile, group, index).Window);
        }
        RuntimeAssert.Require(cases.Count == 338 && windows.Count == 25 && cases.Select(c => c.CaseId).Distinct().Count() == 338, "compiled_partition_complete_registry");
        string[] baseArgs = ["--profile", "shared", "--group", "roundtrip", "--output", "unused", "--candidate-manifest", "unused",
            "--window-index", "0", "--run-id", new string('a', 32), "--cohort-id", new string('b', 64)];
        var refusals = new List<object>();
        foreach (string test in new[] { "missing_index", "negative_index", "out_of_range", "non_integer", "arbitrary_size", "arbitrary_range", "arbitrary_ids", "generated_mix", "partition_mix" })
        {
            var invalid = baseArgs.ToList();
            if (test == "missing_index") invalid.RemoveRange(8, 2);
            else if (test is "negative_index" or "out_of_range" or "non_integer") invalid[9] = test == "negative_index" ? "-1" : test == "out_of_range" ? "99" : "0.5";
            else { invalid.Add(test switch { "arbitrary_size" => "--window-size", "arbitrary_range" => "--start", "arbitrary_ids" => "--case-ids",
                "generated_mix" => "--offline-generated-create", _ => "--offline-partition" }); invalid.Add("true"); }
            string? assertion = null;
            try { ArgumentMode(Parse(invalid.ToArray())); }
            catch (RuntimeAssertionException error) { assertion = error.AssertionId; }
            RuntimeAssert.Require(assertion != null, "partition_illegal_argument_not_refused");
            refusals.Add(new { test_id = test, status = "refused", assertion });
        }
        var assembly = typeof(Program).Assembly;
        SafeEvidence.Write(Path.Combine(output, "partition.json"), new { schema_version = 1, task = "TP07-E offline partition", database_accessed = false,
            candidate_guard_constructed = false, owned_objects_constructed = false, runtime_context_constructed = false,
            window_size = WindowSize, cases, windows, argument_refusals = refusals, process_exit_code = 0,
            host_identity = new { dll_sha256 = SafeEvidence.HashFile(assembly.Location), mvid = assembly.ManifestModule.ModuleVersionId.ToString("D"),
                deps_sha256 = SafeEvidence.HashFile(Path.ChangeExtension(assembly.Location, ".deps.json")) } });
        return 0;
    }

    private static int OfflineGeneratedCreate(Dictionary<string, string> options)
    {
        RuntimeAssert.Require(options["--offline-generated-create"] == "true" && !options.ContainsKey("--group"), "offline_true_flag_and_no_group_required");
        string profile = Required(options, "--profile"), output = Path.GetFullPath(Required(options, "--output"));
        RuntimeAssert.Require(profile is "shared" or "tls", "offline_profile_invalid");
        RuntimeAssert.Require(!Directory.Exists(output) && !File.Exists(output), "offline_output_not_fresh");
        Directory.CreateDirectory(output);
        using (var acquisition = new FileStream(Path.Combine(output, ".offline-ddl-owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { byte[] marker = System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")); acquisition.Write(marker); }
        var io = new OfflineIoState();
        var report = new Dictionary<string, object?> { ["schema_version"] = 1, ["task"] = "TP07-E offline GeneratedCreate",
            ["profile"] = profile, ["database_accessed"] = false, ["owned_objects_constructed"] = false,
            ["offline_ddl_accepted"] = false, ["runtime_accepted"] = false, ["candidate_acceptance"] = false,
            ["full_EF_C_accepted"] = false,
            ["live_DATA_LENGTH_32_verified"] = false, ["cases"] = new List<Dictionary<string, object?>>() };
        string[] shapes = ["roundtrip", "facet6", "store6", "default7_fixed6", "concurrency", "native", "DATE_nonmidnight",
            "constant_default", "HasData", "sql_default", "NULL_default", "inline", "update_expression", "clock_default"];
        report["required_shapes"] = shapes; report["required_model_count"] = shapes.Length;
        report["required_case_ids"] = shapes.Select(shape => "OFF-DDL-" + profile + "-" + shape).ToArray();
        try
        {
            var guard = CandidateGuard.Load(profile, Required(options, "--candidate-manifest"), options.GetValueOrDefault("--capabilities"));
            report["candidate_identity"] = guard.VerifyRuntime();
            var audit = new CommandAudit(guard);
            int index = 0;
            foreach (string shape in shapes)
            {
                int scale = shape is "facet6" or "store6" or "default7_fixed6" or "native" or "DATE_nonmidnight" ? 6 : profile == "shared" ? 7 : 6;
                long ticks = RuntimeCases.Ordinary + (scale == 7 ? 1234567 : 1234560);
                string table = "TP07_OFF_" + profile.ToUpperInvariant() + "_" + (++index).ToString("D2", CultureInfo.InvariantCulture);
                string? defaultD = null, defaultZ = null;
                if (shape == "sql_default")
                {
                    using var literalContext = OfflineContext(new(table + "_LIT", "roundtrip", profile, "pair", scale, table + "_LIT_PK"), guard, audit, io);
                    defaultD = literalContext.Mapping("D").GenerateSqlLiteral(new DateTime(ticks, DateTimeKind.Unspecified));
                    defaultZ = literalContext.Mapping("Z").GenerateSqlLiteral(new DateTimeOffset(new DateTime(ticks, DateTimeKind.Unspecified), TimeSpan.FromMinutes(330)));
                }
                else if (shape == "NULL_default") defaultD = defaultZ = "NULL";
                else if (shape == "clock_default") defaultD = defaultZ = "CURRENT_TIMESTAMP";
                var runtime = new ModelSpec(table, shape, profile, "dt", scale, table + "_PK", defaultD, defaultZ, ticks, 330);
                ModelSpec database = TemporalContext.DatabaseSpec(runtime);
                var item = new Dictionary<string, object?> { ["case_id"] = "OFF-DDL-" + profile + "-" + shape, ["shape"] = shape, ["status"] = "attempted",
                    ["runtime_spec"] = runtime, ["database_spec"] = database };
                ((List<Dictionary<string, object?>>)report["cases"]!).Add(item);
                SafeEvidence.Write(Path.Combine(output, "offline-generated-create.json"), report);
                using (var runtimeContext = OfflineContext(runtime, guard, audit, io))
                    item["runtime_model"] = OfflineModel(runtimeContext, runtime);
                using var context = OfflineContext(database, guard, audit, io);
                item["database_model"] = OfflineModel(context, database);
                var commands = context.GeneratedCreate(out var operations);
                RequireOfflineOperations(database, operations);
                string sql = string.Join("\n", commands.Select(command => command.CommandText));
                RequireOfflineSql(context, database, sql);
                RuntimeAssert.Require(commands.Count is > 0 and <= 4 && commands.All(command => command.CommandText.Length <= 16000), "offline_SQL_bound");
                item["migration_operations"] = operations.Select(operation => operation.GetType().Name).ToArray();
                item["seed_operations"] = operations.OfType<InsertDataOperation>().Select(insert => new
                { insert.Table, insert.Schema, insert.Columns, values = Enumerable.Range(0, insert.Columns.Length)
                    .Select(ordinal => CommandAudit.Value(insert.Values[0, ordinal])).ToArray() }).ToArray();
                item["columns"] = operations.OfType<CreateTableOperation>().Single().Columns.Select(column => new
                { column.Name, column.ColumnType, column.Precision, column.Scale, column.MaxLength, column.IsUnicode, column.IsNullable,
                    default_value = column.DefaultValue is null ? null : CommandAudit.Value(column.DefaultValue), column.DefaultValueSql }).ToArray();
                item["actual_generator"] = guard.VerifyProvider(context.GetService<IMigrationsSqlGenerator>());
                var differ = context.GetService<IMigrationsModelDiffer>().GetType();
                item["actual_model_differ"] = new { type = differ.FullName, assembly = differ.Assembly.FullName,
                    dll_sha256 = SafeEvidence.HashFile(differ.Assembly.Location), mvid = differ.Assembly.ManifestModule.ModuleVersionId.ToString("D") };
                item["generated_SQL"] = commands.Select((command, ordinal) =>
                {
                    string name = "model-" + index.ToString("D2", CultureInfo.InvariantCulture) + "-" + ordinal.ToString("D2", CultureInfo.InvariantCulture) + ".sql";
                    File.WriteAllText(Path.Combine(output, name), command.CommandText);
                    return new { path = name, sha256 = CommandAudit.Hash(command.CommandText), command.TransactionSuppressed };
                }).ToArray();
                item["io"] = io.Evidence(); item["status"] = "completed";
            }
            RuntimeAssert.Require(audit.Commands.Count == 0 && audit.Sends.Count == 0, "offline_audit_command_or_send");
            report["io"] = io.Evidence(); report["offline_ddl_accepted"] = true;
        }
        catch (Exception error)
        {
            report["error"] = SafeEvidence.Safe(error);
            if (error is RuntimeAssertionException assertion) report["failed_assertion"] = assertion.AssertionId;
            report["io"] = io.Evidence(false);
        }
        report["process_exit_code"] = report["offline_ddl_accepted"] is true ? 0 : 1;
        SafeEvidence.Write(Path.Combine(output, "offline-generated-create.json"), report);
        return report["offline_ddl_accepted"] is true ? 0 : 1;
    }

    private static TemporalContext OfflineContext(ModelSpec spec, CandidateGuard guard, CommandAudit audit, OfflineIoState io)
    {
        var context = new TemporalContext(spec, guard, audit, io);
        try
        {
            DbConnection connection = context.Database.GetDbConnection();
            io.Observe(connection, guard.VerifyObject(connection, "connection"));
            return context;
        }
        catch { context.Dispose(); throw; }
    }

    private static object OfflineModel(TemporalContext context, ModelSpec spec)
    {
        var entity = context.Model.FindEntityType(typeof(TemporalRow))!;
        var n = entity.FindProperty(nameof(TemporalRow.N));
        bool native = spec.Shape is "native" or "DATE_nonmidnight";
        RuntimeAssert.Require(native ? n == null : n != null && n.GetColumnType() == "VARCHAR(32)" && n.GetMaxLength() == 32 && n.IsUnicode() is false,
            "offline_N_model_contract");
        RuntimeAssert.Require(entity.FindProperty(nameof(TemporalRow.Id))!.ValueGenerated == ValueGenerated.Never, "offline_ID_generated_changed");
        if (!native)
        {
            int precision = spec.Shape == "default7_fixed6" ? 7 : spec.Scale;
            RuntimeAssert.Require(context.Mapping("D").Precision == precision && context.Mapping("Z").Precision == precision, "offline_temporal_model_precision");
            RuntimeAssert.Require(entity.FindProperty(nameof(TemporalRow.D))!.IsConcurrencyToken == (spec.Shape == "concurrency")
                && !entity.FindProperty(nameof(TemporalRow.Z))!.IsConcurrencyToken, "offline_concurrency_model_changed");
            ValueGenerated expectedGenerated = spec.Shape is "constant_default" or "sql_default" or "NULL_default" or "clock_default" ? ValueGenerated.OnAdd : ValueGenerated.Never;
            RuntimeAssert.Require(entity.FindProperty(nameof(TemporalRow.D))!.ValueGenerated == expectedGenerated
                && entity.FindProperty(nameof(TemporalRow.Z))!.ValueGenerated == expectedGenerated, "offline_generated_model_changed");
        }
        return entity.GetProperties().Select(property => new { property.Name, column = property.GetColumnName(),
            column_type = property.GetColumnType(), precision = property.GetPrecision(), scale = property.GetScale(),
            max_length = property.GetMaxLength(), unicode = property.IsUnicode(), property.ValueGenerated, property.IsConcurrencyToken }).ToArray();
    }

    private static void RequireOfflineOperations(ModelSpec spec, IReadOnlyList<MigrationOperation> operations)
    {
        CreateTableOperation table = operations.OfType<CreateTableOperation>().Single();
        bool native = spec.Shape == "native", seeded = spec.Shape is "HasData" or "inline" or "update_expression";
        string[] expected = native ? ["ID", "A", "T", "I"] : ["ID", "D", "Z", "N"];
        RuntimeAssert.Require(table.Name == spec.Table && table.Schema == null && table.Columns.Count == expected.Length
            && table.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expected)
            && table.PrimaryKey is { } key && key.Name == spec.PrimaryKey && key.Columns.SequenceEqual(["ID"])
            && table.ForeignKeys.Count == 0 && table.CheckConstraints.Count == 0, "offline_CREATE_table_PK_columns");
        ColumnOperation Column(string name) => table.Columns.Single(column => column.Name == name);
        RuntimeAssert.Require(Column("ID").ClrType == typeof(int) && !Column("ID").IsNullable, "offline_ID_column_contract");
        if (native)
        {
            RuntimeAssert.Require(Column("A").ColumnType == "DATE" && Column("T").ColumnType == "TIME(6)"
                && Column("I").ColumnType == "INTERVAL DAY(9) TO SECOND(6)", "offline_native_column_types");
        }
        else
        {
            RuntimeAssert.Require(Column("N").ColumnType == "VARCHAR(32)" && Column("N").MaxLength == 32 && Column("N").IsUnicode is false
                && Column("N").IsNullable && Column("N").DefaultValue == null && Column("N").DefaultValueSql == null, "offline_N_operation_contract");
            RuntimeAssert.Require(Column("D").ColumnType == $"TIMESTAMP({spec.Scale})" && Column("Z").ColumnType == $"DATETIME({spec.Scale}) WITH TIME ZONE"
                && Column("D").IsNullable && Column("Z").IsNullable, "offline_DT_DTO_operation_types");
            foreach (string name in new[] { "D", "Z" })
            {
                ColumnOperation column = Column(name); bool dto = name == "Z";
                object? expectedValue = spec.Shape == "constant_default" ? dto
                    ? (object)new DateTimeOffset(new DateTime(spec.SeedTicks!.Value, DateTimeKind.Unspecified), TimeSpan.FromMinutes(spec.SeedOffset!.Value))
                    : new DateTime(spec.SeedTicks!.Value, DateTimeKind.Unspecified) : null;
                RuntimeAssert.Require(OfflineSameValue(column.DefaultValue, expectedValue) && column.DefaultValueSql == (dto ? spec.DefaultZ : spec.DefaultD), "offline_default_operation_semantics");
            }
        }
        var inserts = operations.OfType<InsertDataOperation>().ToArray();
        RuntimeAssert.Require(operations.Count == (seeded ? 2 : 1) && inserts.Length == (seeded ? 1 : 0), "offline_seed_operation_count");
        if (seeded)
        {
            var insert = inserts.Single();
            RuntimeAssert.Require(insert.Table == spec.Table && insert.Schema == null && insert.Columns.Length == 4
                && insert.Columns.ToHashSet(StringComparer.Ordinal).SetEquals(["ID", "D", "Z", "N"])
                && insert.Values.GetLength(0) == 1 && insert.Values.GetLength(1) == 4, "offline_actual_seed_shape");
            foreach (string name in insert.Columns)
            {
                object expectedValue = name switch { "ID" => 1, "N" => "seed", "D" => new DateTime(spec.SeedTicks!.Value, DateTimeKind.Unspecified),
                    _ => new DateTimeOffset(new DateTime(spec.SeedTicks!.Value, DateTimeKind.Unspecified), TimeSpan.FromMinutes(spec.SeedOffset!.Value)) };
                RuntimeAssert.Require(OfflineSameValue(insert.Values[0, Array.IndexOf(insert.Columns, name)], expectedValue), "offline_actual_seed_value");
            }
        }
    }

    private static bool OfflineSameValue(object? actual, object? expected) => expected switch
    {
        DateTime date => actual is DateTime observed && observed.Ticks == date.Ticks && observed.Kind == date.Kind,
        DateTimeOffset offset => actual is DateTimeOffset observed && observed.Ticks == offset.Ticks && observed.Offset == offset.Offset,
        _ => Equals(actual, expected)
    };

    private static void RequireOfflineSql(TemporalContext context, ModelSpec spec, string sql)
    {
        bool HasColumn(string name, string type) => Regex.IsMatch(sql, @"(?m)^[ \t]*""" + name + @""" " + type + @"(?=\s|,)", RegexOptions.CultureInvariant);
        RuntimeAssert.Require(Regex.IsMatch(sql, @"\ACREATE TABLE """ + spec.Table + @""" \(", RegexOptions.CultureInvariant)
            && HasColumn("ID", "INT NOT NULL") && sql.Contains("CONSTRAINT \"" + spec.PrimaryKey + "\" PRIMARY KEY (\"ID\")", StringComparison.Ordinal), "offline_actual_CREATE_SQL_identity");
        if (spec.Shape == "native")
            RuntimeAssert.Require(!sql.Contains("\"N\"", StringComparison.Ordinal) && HasColumn("A", "DATE") && HasColumn("T", @"TIME\(6\)")
                && HasColumn("I", @"INTERVAL DAY\(9\) TO SECOND\(6\)"), "offline_actual_native_SQL");
        else
        {
            RuntimeAssert.Require(Regex.Matches(sql, @"(?m)^[ \t]*""N"" VARCHAR\(32\) NULL,[ \t]*\r?$", RegexOptions.CultureInvariant).Count == 1
                && HasColumn("D", @"TIMESTAMP\(" + spec.Scale + @"\)") && HasColumn("Z", @"DATETIME\(" + spec.Scale + @"\) WITH TIME ZONE"), "offline_actual_N_DT_DTO_SQL");
            foreach (string name in new[] { "D", "Z" })
            {
                string? expectedDefault = name == "D" ? spec.DefaultD : spec.DefaultZ;
                if (spec.Shape == "constant_default") expectedDefault = context.Mapping(name).GenerateSqlLiteral(name == "D"
                    ? (object)new DateTime(spec.SeedTicks!.Value, DateTimeKind.Unspecified)
                    : new DateTimeOffset(new DateTime(spec.SeedTicks!.Value, DateTimeKind.Unspecified), TimeSpan.FromMinutes(spec.SeedOffset!.Value)));
                var line = Regex.Matches(sql, @"(?m)^[ \t]*""" + name + @""" [^\r\n]*", RegexOptions.CultureInvariant);
                RuntimeAssert.Require(line.Count == 1 && (expectedDefault == null
                    ? !Regex.IsMatch(line[0].Value, @"\bDEFAULT\b", RegexOptions.CultureInvariant)
                    : Regex.IsMatch(line[0].Value, @"\bDEFAULT\b", RegexOptions.CultureInvariant) && line[0].Value.Contains(expectedDefault, StringComparison.Ordinal)),
                    "offline_actual_default_SQL");
            }
        }
        bool seeded = spec.Shape is "HasData" or "inline" or "update_expression";
        RuntimeAssert.Require(Regex.Matches(sql, @"(?m)^INSERT INTO """ + spec.Table + @"""(?=\s)", RegexOptions.CultureInvariant).Count == (seeded ? 1 : 0),
            "offline_actual_seed_INSERT_SQL");
    }
}

internal sealed class RuntimeHarness(CandidateGuard guard, OwnedObjects owned, string output, Dictionary<string, object?> report)
{
    internal CandidateGuard Guard { get; } = guard;
    internal OwnedObjects Owned { get; } = owned;
    internal CommandAudit Audit { get; } = new(guard);
    private int artifactOrdinal;
    internal void Write()
    {
        report["commands"] = Audit.Commands; report["sent_frames"] = Audit.Sends; report["ef_opened_connections"] = Audit.Connections;
        report["async_write_positive_controls"] = Audit.AsyncWriteControls;
        report["protocol_responses"] = Audit.Responses; report["parameter_describe_sources"] = Audit.PrepareSources;
        report["protocol_observer_failed"] = Audit.ObserverFailed;
        SafeEvidence.Write(Path.Combine(output, "runtime.json"), report);
    }
    internal Dictionary<string, object?> Begin(RuntimeCase @case)
    {
        var item = new Dictionary<string, object?> { ["case_id"] = @case.CaseId, ["status"] = "attempted", ["definition"] = @case,
            ["steps"] = new List<Dictionary<string, object?>>() };
        ((List<Dictionary<string, object?>>)report["cases"]!).Add(item); Write(); return item;
    }
    internal async Task<T> Step<T>(Dictionary<string, object?> item, string id, Func<Task<T>> call, Func<T, object?>? receipt = null)
    {
        Audit.Stage = (string)item["case_id"]! + "/" + id;
        var step = new Dictionary<string, object?> { ["step_id"] = id, ["status"] = "attempted" };
        ((List<Dictionary<string, object?>>)item["steps"]!).Add(step); Write();
        try { T result = await call(); step["status"] = "completed"; step["call_returned"] = true; if (receipt != null) step["result"] = receipt(result); Write(); return result; }
        catch (Exception error) { step["status"] = "failed"; step["error"] = SafeEvidence.Safe(error); step["actual_exception_chain"] = RuntimeRefusal.Evidence(error); if (error is RuntimeAssertionException assertion) step["failed_assertion"] = assertion.AssertionId; Write(); throw; }
        finally { Audit.CloseStage((string)item["case_id"]! + "/" + id); }
    }
    internal TemporalContext Context(ModelSpec spec)
    {
        var context = new TemporalContext(spec, Guard, Audit);
        Guard.VerifyObject(context.Database.GetDbConnection(), "connection");
        report["actual_EF_mapping_source"] = Guard.VerifyProvider(context.GetService<IRelationalTypeMappingSource>());
        return context;
    }
    internal async Task<ModelSpec> Create(RuntimeCase @case, Dictionary<string, object?> item, string? defaultD = null, string? defaultZ = null)
    {
        EfObjectShape ownedShape = @case.Shape is "native" or "DATE_nonmidnight" ? EfObjectShape.Native : @case.Group == "generated" ? EfObjectShape.Generated : EfObjectShape.Basic;
        var entry = Owned.RegisterEfTable(@case.CaseId, ownedShape, @case.Scale);
        var runtime = new ModelSpec(entry.Table, @case.Shape, @case.Profile, @case.Kind, @case.Scale, entry.PrimaryKey,
            defaultD, defaultZ, @case.Ticks, @case.OffsetMinutes);
        item["owned_table"] = entry.Table; item["owned_primary_key"] = entry.PrimaryKey;
        if (@case.Shape == "advisory7") return runtime; // No table creation is needed for the controlled parameter lane.
        ModelSpec database = TemporalContext.DatabaseSpec(runtime);
        await using var context = Context(database);
        var commands = context.GeneratedCreate(out IReadOnlyList<MigrationOperation> operations);
        item["migration_operations"] = operations.Select(operation => operation.GetType().Name).ToArray();
        var allowed = new HashSet<string>(StringComparer.Ordinal) { entry.Table, entry.PrimaryKey, "ID", "N", "D", "Z", "A", "T", "I" };
        foreach (MigrationCommand command in commands)
        {
            RuntimeAssert.Require(command.CommandText.Length <= 16000, "generated_SQL_too_large");
            foreach (Match match in Regex.Matches(command.CommandText, "\"([^\"]+)\"", RegexOptions.CultureInvariant))
                RuntimeAssert.Require(allowed.Contains(match.Groups[1].Value), "generated_SQL_identifier_outside_owned_allowlist");
            RuntimeAssert.Require(!Regex.IsMatch(command.CommandText, @"\b(?:DROP|ALTER|GRANT|TRIGGER|SEQUENCE|TABLESPACE|DATABASE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "generated_SQL_operation_outside_create_scope");
            string path = Path.Combine(output, "generated-" + (++artifactOrdinal).ToString("D4", CultureInfo.InvariantCulture) + ".sql");
            File.WriteAllText(path, command.CommandText);
        }
        item["generated_SQL"] = commands.Select(command => new { sha256 = CommandAudit.Hash(command.CommandText), transaction_suppressed = command.TransactionSuppressed }).ToArray();
        Owned.BeginExternalCreate(entry);
        bool createConfirmed = false;
        for (int index = 0; index < commands.Count; index++)
        {
            MigrationCommand command = commands[index];
            bool tableCreate = command.CommandText.TrimStart().StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase);
            await Step(item, "migration_command_" + index, async () =>
            {
                int rows;
                try { rows = await ExecuteSql(context, command.CommandText, @case.Async); }
                catch (Exception error) { if (tableCreate) Owned.FailExternalCreate(entry, error); throw; }
                if (tableCreate)
                {
                    RuntimeAssert.Require(!createConfirmed, "generated_duplicate_table_CREATE");
                    var completion = new OwnedObjects.CreateCompletion(true, @case.Async ? "ExecuteSqlRawAsync" : "ExecuteSqlRaw", CommandAudit.Hash(command.CommandText));
                    Owned.RecordExternalCreateAcknowledged(entry, completion); item["actual_CREATE_completion"] = completion;
                }
                return rows;
            }, rows => new { affected_rows = rows });
            if (tableCreate) { Owned.ConfirmExternalCreate(entry); createConfirmed = true; }
        }
        RuntimeAssert.Require(createConfirmed, "generated_table_CREATE_ACK_missing");
        return runtime;
    }
    internal static Task<int> Save(TemporalContext context, bool async) => async ? context.SaveChangesAsync() : Task.FromResult(context.SaveChanges());
    internal static Task<int> ExecuteSql(TemporalContext context, string sql, bool async) => async
        ? context.Database.ExecuteSqlRawAsync(sql) : Task.FromResult(context.Database.ExecuteSqlRaw(sql));
    internal static Task<TemporalRow> Single(TemporalContext context, bool async, bool tracking = false) => async
        ? (tracking ? context.Rows : context.Rows.AsNoTracking()).SingleAsync(item => item.Id == 1)
        : Task.FromResult((tracking ? context.Rows : context.Rows.AsNoTracking()).Single(item => item.Id == 1));
    internal static Task<int> Count(TemporalContext context, bool async) => async ? context.Rows.AsNoTracking().CountAsync() : Task.FromResult(context.Rows.AsNoTracking().Count());
    internal async Task<TemporalRow> Fresh(ModelSpec spec, bool async, Dictionary<string, object?> item, string step)
    {
        await using var context = Context(spec);
        return await Step(item, step, () => Single(context, async), row => Row(row));
    }
    internal sealed record OwnedState(int Rows, string CatalogSha256, object Catalog);
    internal async Task<OwnedState> FreshState(ModelSpec spec, bool async, Dictionary<string, object?> item, string step, int expectedRows)
    {
        return await Step(item, step, async () =>
        {
            await using var context = Context(spec);
            int rows = await Count(context, async);
            RuntimeAssert.Require(rows == expectedRows, "fresh_owned_total_row_count_mismatch");
            var entry = Owned.Entries.Single(entry => entry.Table == spec.Table);
            // Preflight reads the current owned columns and verifies marker and exactly one owned PK on a fresh TEST connection.
            object columns = Owned.Preflight(entry);
            object catalog = new { columns, primary_key_name_checked_against_catalog = entry.PrimaryKey,
                primary_key_contract = "exactly_one_owned_primary_key", verification_source = "OwnedObjects.Preflight" };
            string hash = CommandAudit.Hash(JsonSerializer.Serialize(catalog, SafeEvidence.Json));
            return new OwnedState(rows, hash, catalog);
        }, state => state);
    }
    internal static void SameState(OwnedState before, OwnedState after)
    {
        RuntimeAssert.Require(before.Rows == after.Rows, "owned_total_row_count_changed");
        RuntimeAssert.Require(before.CatalogSha256 == after.CatalogSha256, "owned_catalog_semantics_changed");
    }
    internal static object Row(TemporalRow row) => new { id = row.Id, d = CommandAudit.Value(row.D), z = CommandAudit.Value(row.Z),
        a = CommandAudit.Value(row.A), t = CommandAudit.Value(row.T), i = CommandAudit.Value(row.I), n = CommandAudit.Value(row.N) };
    internal static void Exact(RuntimeCase @case, TemporalRow row, long? ticks = null)
    {
        long? expected = ticks ?? @case.Ticks;
        if (@case.Kind == "dt") { RuntimeAssert.Require(row.D?.Ticks == expected, "EF_DT_ticks_mismatch"); if (row.D.HasValue) RuntimeAssert.Require(row.D.Value.Kind == DateTimeKind.Unspecified, "EF_DT_kind_mismatch"); }
        else if (@case.Kind == "dto") { RuntimeAssert.Require(row.Z?.Ticks == expected, "EF_DTO_localticks_mismatch"); RuntimeAssert.Require(!row.Z.HasValue || (int)row.Z.Value.Offset.TotalMinutes == @case.OffsetMinutes, "EF_DTO_offset_mismatch"); }
    }
    internal void Independent(ModelSpec spec, RuntimeCase @case, Dictionary<string, object?> item, long? ticks = null)
    {
        string column = @case.Kind == "dto" ? "Z" : "D";
        Audit.Stage = @case.CaseId + "/fresh_independent_" + column;
        var attempt = SafeEvidence.StartStep(item, "fresh_independent_" + column);
        Write();
        try
        {
            object observation = LiteralOracle.ReadFresh(Guard, spec.Table, 1, @case.Kind == "dto", @case.Scale, column, item);
            item["independent_" + column + "_" + artifactOrdinal++] = observation;
            JsonElement value = JsonSerializer.SerializeToElement(observation, SafeEvidence.Json);
            RequireOracle(value, ticks ?? @case.Ticks, @case.Kind == "dto" ? @case.OffsetMinutes : null);
            SafeEvidence.CompleteStep(attempt, observation); Write();
        }
        catch (Exception error) { SafeEvidence.FailStep(attempt, error); Write(); throw; }
    }
    internal static void RequireOracle(JsonElement observation, long? ticks, int? offset)
    {
        RuntimeAssert.Require(observation.GetProperty("rows").GetInt32() == 1, "oracle_persisted_row_count");
        RuntimeAssert.Require(observation.GetProperty("is_null").GetBoolean() == !ticks.HasValue, "oracle_NULL_mismatch");
        if (!ticks.HasValue) return;
        RuntimeAssert.Require(observation.GetProperty("local_ticks").GetInt64() == ticks.Value, "oracle_ticks_mismatch");
        if (offset.HasValue) RuntimeAssert.Require(observation.GetProperty("offset_minutes").GetInt32() == offset.Value, "oracle_offset_mismatch");
    }
}
