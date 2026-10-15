# TP03-v5 physical profile diagnostic

This one-off diagnostic consumes only `W.DamengProvider 0.1.0-tp03.20261008053249` through an exact `PackageReference` on .NET 10. It compares the logical connection property with the physical instance property that `DmTemporalLegacyAdapter.Profile(connection)` actually reads. It creates no database objects and makes no business writes, session-setting calls or temporal SQL probes.

The only two SQL statements are `SELECT USER FROM DUAL` and `SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL`, immediately after opening one shared TEST connection. Both must equal `WDM_PROVIDER_TEST` before profile observations. Configuration follows the existing shared TP01 probe: typed W builder, explicit `PlaintextAllowed`, TEST schema, logging off, pooling off, bounded connect/command timeout, and no encoding override added by this tool. The original TEST wrapper supplies the sole connection variable; no secrets are copied into the diagnostic folder or build snapshot.

The executable takes five positional inputs: identities JSON, product-source manifest, product-source archive, existing feed directory, and a **new** output JSON. It hard-pins the current TP03-v5 source head, source manifest/archive SHA, package ID/version/asset/SHA, packaged and loaded DLL SHA/MVID. It also requires its own `.deps.json` to identify that exact runtime asset as a NuGet package. Project references and loose DLL substitutions are not accepted. verifier's existing frozen contracts preflight remains mandatory and is run separately before build/connection.

Reports contain only source/package receipts, TEST identity receipts, safe public version, and logical/physical `ServerVersion`, `msgVersion`, effective `ServerEncoding`, private negotiated `serverEncoding`, timezone and a boolean indicating whether the single encoding override is empty. The dictionary is never enumerated. Version/encoding values are validated before output; unexpected values produce fixed safe failure codes. Adapter reflection reports only its `Identity`, `State` and `EvidenceId`. A single clearly synthetic `(26, Timestamp9, OrdinaryPacked, 7, 7, 7, true, 0)` target is passed to that actual profile's `ValidateWriteTarget`; its allowed/rejected result does not establish server support.

Exception output contains only safe exception type, fixed error code, and declaring-class/method-name stack frames. It excludes Message, ToString, arguments, filenames and line numbers. The output uses `CreateNew`, and the one connection is disposed in `finally`. All profile/candidate/temporal acceptance flags remain false, including when the diagnostic exits zero.

## Independent verifier window

implementer did not run builds, tests or DB. After independent reviewer/root review and hash freeze, verifier creates a new ignored directory, preserves the source/input manifest and builds a private copy of these four files. Use the original TP03-v5 package directory and frozen contracts module; do not rebuild or relabel that provider.

The following is an execution outline from the original worktree. Substitute a new, never-used run suffix. Do not run beside another build/DB window.

```sh
DIAG_REPO="$PWD"
DIAG_PACKAGE="$DIAG_REPO/.local/temporal-native/verification/TP03-v5-package"
DIAG_RUN="$DIAG_REPO/.local/temporal-native/verification/TP03-profile-diagnostic-<new-suffix>"
mkdir -m 700 "$DIAG_RUN"
mkdir -m 700 "$DIAG_RUN/build" "$DIAG_RUN/cli-home"
cp tools/TemporalProfileDiagnostic/Program.cs tools/TemporalProfileDiagnostic/TemporalProfileDiagnostic.csproj \
   tools/TemporalProfileDiagnostic/global.json tools/TemporalProfileDiagnostic/README.md "$DIAG_RUN/build/"

# This is the same unchanged TP01 contracts CLI; verify its frozen hash first.
PYTHONDONTWRITEBYTECODE=1 python3 "$DIAG_PACKAGE/frozen-tool-working/tools/TemporalBoundaryProbe/contracts.py" \
  --identities "$DIAG_PACKAGE/identities.json" \
  --source-manifest "$DIAG_PACKAGE/product-source-manifest.json" \
  --source-archive "$DIAG_PACKAGE/product-source.tar" \
  --package "$DIAG_PACKAGE/feed/W.DamengProvider.0.1.0-tp03.20261008053249.nupkg" \
  --output "$DIAG_RUN/source-package-chain.json"
```

The frozen contracts SHA256 must equal `f8ad9abddd86e415374b9594954f2264fc5d701e3f603c7a96ed77e14dbca6cb`. Resolve its actual frozen path from the v5 tool manifest if the path above differs; do not substitute an edited policy. Verify the feed filename's actual case and supply that exact file. Stop if preflight is nonzero.

For restore/build, run from `"$DIAG_RUN/build"` so its pinned `global.json` applies, with every inherited `DAMENG_*` variable removed. Set:

```sh
export DOTNET_CLI_HOME="$DIAG_RUN/cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$DIAG_RUN/build"
/usr/local/share/dotnet/dotnet restore TemporalProfileDiagnostic.csproj \
  --source "$DIAG_PACKAGE/feed" --disable-parallel \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
/usr/local/share/dotnet/dotnet build TemporalProfileDiagnostic.csproj -c Release --no-restore \
  -m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers
```

Capture command exit codes and safe build logs. Do not continue after a failure. Record the actual SDK, lock/assets/deps/tool DLL hashes and four frozen source hashes before opening TEST. No unittest/test command is required for this one-off read-only tool.

Run exactly once through the original shared TEST wrapper. Discard both runtime stdout and stderr, because only the safe JSON is evidence:

```sh
"$DIAG_REPO/scripts/with-dameng-test.sh" /usr/local/share/dotnet/dotnet \
  "$DIAG_RUN/build/bin/Release/net10.0/TemporalProfileDiagnostic.dll" \
  "$DIAG_PACKAGE/identities.json" "$DIAG_PACKAGE/product-source-manifest.json" \
  "$DIAG_PACKAGE/product-source.tar" "$DIAG_PACKAGE/feed" "$DIAG_RUN/diagnostic.json" \
  >/dev/null 2>/dev/null
```

Record its exit code and safe report SHA. A completed report needs TEST identity verification and `connection_disposed=true`. Interpret logical/physical/profile disagreement from the returned fields; do not infer an encoding profile expansion or edit the old TP01 labels. No ownership/cleanup gate exists because no database objects are created. Keep the natural failure report and run directory immutable if any gate fails.
