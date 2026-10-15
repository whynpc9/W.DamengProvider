# Stored procedure probes

`Official.csproj` characterizes DM.DmProvider 8.3.1.47463; `Candidate.csproj` verifies the current W source with explicit assertions. They produce separate JSON reports. See [I09 characterization](../../docs/compatibility/I09-characterization.md) for commands, observed protocol details, retained failures, and coverage limits.

Both programs accept a report path as the first argument. Candidate accepts an optional case-name substring as the second argument (for example `cursors`). Official also supports the read-only `audit` command, which verifies the TEST identity and counts remaining `I09W_%` objects; it does not delete by prefix.

Run with `scripts/with-dameng-test.sh` and the repository's required .NET environment/build flags. No test connection is embedded in either project. W uses an explicit `PlaintextAllowed` typed builder for this authorized shared development instance. Temporal output tests use an official-driver Text typed insert to seed a dedicated table at exact seven-digit precision; the W output assertions require exact values and offsets.

Each run records the exact unique object names it owns, drops only those names, and checks absence using a fresh connection. Candidate preserves failed test and cleanup records. If W cleanup itself fails, the probe uses an independently verified official TEST connection to recover only those same exact objects, while retaining a failing exit status.

Post-send cancellation is established by a committed marker written inside the procedure and observed from a separate connection before cancellation. The later business write is inspected independently; the report does not infer rollback solely from the client exception.


After the latest metadata merge at `04ffa82`, the unchanged W probe again passed 59 cases on each actual runtime (8.0.32, 9.0.21, and 10.0.12), 177 in total, including synchronous and asynchronous calls, exact high-precision output, 100 KB LOB materialization, prepared-command reuse, output limits, independent cursor ownership, immediate cancellation revocation, batch/array preflight rejection with zero business writes, and independently observed database outcomes. Each report records the actual runtime, target framework, loaded assembly, and product module identity. The earlier 55-case run and the prior 177-case run at `7f390ed` remain recorded separately. Unsupported historical protocol frames and complex parameter types remain explicit rejections. See the characterization report for the tested assembly identity and retained failed attempts.
