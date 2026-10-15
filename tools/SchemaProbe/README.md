# Issue #10 real-server probe

The probe uses only `DAMENG_TEST_CONNECTION_STRING`, verifies both the authenticated
user and current schema are `WDM_PROVIDER_TEST`, and explicitly selects
`PlaintextAllowed` through the typed W builder. Run through the existing TEST
wrapper; do not copy secrets into the tool or load administrator credentials.

```sh
export DOTNET_CLI_HOME=/tmp/wdm-schema-dotnet
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
mkdir -p "$DOTNET_CLI_HOME"
dotnet build tools/SchemaProbe/SchemaProbe.csproj --disable-build-servers -m:1 /nodeReuse:false /p:UseSharedCompilation=false
scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll --identity-only
scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll
scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll --full
```

The default mode characterizes public synchronous/asynchronous `Prepare` for
ordinary, parameterized, quoted-identifier/type, and side-effecting-function
queries. A positive control first executes the function once. Subsequent describe
operations must preserve the counter. Internal reflection observes only the sent
request opcode and execute flag, and extracts column descriptors; it never emits
SQL, raw frames, or credentials.

`--full` additionally checks all advertised metadata collections, restrictions,
composite primary/foreign-key order, view discovery, decimal/time/LOB metadata,
and synchronous/asynchronous reader behavior for `SchemaOnly` and `KeyInfo`.
Schema-only reads and next-result checks must send no fetch messages. Alias,
expression, join, missing-object, cancellation, and session-reuse cases are
included. Catalog failures are reported rather than accepted as empty collections.
On the current TEST instance, `ReservedWords` requires access to
`V$RESERVED_WORDS` that TEST does not have. The full probe separately checks that
synchronous and asynchronous calls return server error `-5504`, mark the
connection broken and non-reusable, and records this capability limitation.
The other 15 collections must return nonempty results with matching schemas and
values. No additional grants are used.

In a worktree without local secrets, invoke the existing checkout's TEST wrapper
while keeping the current working directory at the worktree root; for this
environment the command is:

```sh
/home/air/Projects/W.DamengProvider/scripts/with-dameng-test.sh dotnet tools/SchemaProbe/bin/net10.0/SchemaProbe.dll --full
```

Each run creates uniquely named objects and records successful creations for
reverse-order, exact-name cleanup in `finally`. The JSON result includes the
authenticated identity, assertions, safe error diagnostics, and a final catalog
count for those exact objects. Save command exit codes alongside output. A missing
connection or failed identity stage is `integration_pending`; it is not a passing
real-server test. This tool tests the W driver only.
