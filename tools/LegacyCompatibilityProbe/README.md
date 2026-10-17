# .NET Core 3.1 compatibility probe

This console probe consumes the provider's `netstandard2.1` asset and requires an
actual .NET Core 3.1 runtime. It uses synthetic handshake hooks and loopback TCP;
it never loads secrets or connects to a database. Exit 0 means every assertion
passed; a watchdog bounds asynchronous operations. Run one instance per process.

Build using a modern SDK (C# 12), with the repository build environment and
single-node flags. The default project reference pins `netstandard2.1` explicitly:

```sh
export DOTNET_CLI_HOME="$PWD/.local/legacy-probe-cli"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build tools/LegacyCompatibilityProbe/LegacyCompatibilityProbe.csproj -c Release -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

To test a locally packed NuGet instead, copy this project's `.csproj` and
`Program.cs` to an empty ignored task directory, then restore/build that project
with `/p:UsePackage=true /p:PackageVersion=0.2.1-core31.1` and sources pointing to
the candidate package directory and nuget.org. This avoids retaining project
reference assets; the package mode has no product project reference. Inspect
`obj/project.assets.json` to confirm both compile/runtime use
`lib/netstandard2.1/W.DamengProvider.dll`.
The runtime assertion checks the provider's TargetFrameworkAttribute, so a newer
asset cannot silently satisfy this test.

Run the build output with `dotnet W.DmProvider.LegacyCompatibilityProbe.dll` inside
a .NET Core 3.1 Linux container for each required architecture, with output and
its dependencies mounted read-only. Do not enable runtime roll-forward. Report
runtime/architecture output separately for x64 and arm64. A modern SDK compilation
alone does not prove old-runtime behavior.

Coverage: typed datasource failure cleanup and lifecycle; actual three-row array
plan snapshots, inferred NULL types and invalid length rejection; DateTime/TimeSpan
inference, timestamp golden bytes, day-number/calendar boundaries; pending DNS
cancellation/deadline; sync/async loopback transport bytes, caller cancellation,
deadlines and close; 12 sync/async STARTUP mode/policy combinations (mode 0 only
reaches LOGIN with explicit PlaintextAllowed, modes 1/4 fail explicitly with EOF
and no LOGIN/TLS hello); diagnostics initialization; deliberate lack of batch API.
Schema/savepoint member presence is checked, but their protocol behavior is not
exercised. The synthetic server deliberately closes after observing LOGIN; this
does not establish successful database authentication. This is a bounded offline
compatibility probe, not database acceptance.

Successful current runs report seven groups and 167 assertions. Package build
warnings about upstream libraries not officially supporting netcoreapp3.1 remain
visible; runtime success does not change those upstream support statements.
