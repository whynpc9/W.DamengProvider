# TP03 writer / reader wiring acceptance

This separate policy consumes a new **candidate / shared / core-only** run from the unchanged TP01 runner. Its scope is `TP03_writer_reader_wiring`. It verifies the existing fixed-column TS/TZ six- and seven-digit writer paths and the public W typed, generic, GetValue and GetString reader continuity. TLS is rejected by this policy.

It does not close TP04 binding, TP05 getter coverage or full temporal acceptance. `candidate_acceptance`, `full_temporal_acceptance`, `tp04_binding_acceptance` and `tp05_complete` are always false. DATE/TIME/interval, malformed inputs, async routes and lifecycle regressions remain separate TP03 offline gates. A passing core run must not substitute for them.

## Production inputs

After independent offline gates and root's source/package freeze, Low uses the existing runner with a unique package, its exact producer source manifest/archive, identities and root-approved capability manifest:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 eng/temporal-precision.py \
  --feed <frozen-feed> --source-manifest <product-source-manifest.json> \
  --source-archive <product-source.tar> --identities <identities.json> \
  --capabilities <capabilities.json> --repo <original-worktree> \
  --profile shared --lanes core --output <new-run-directory>

PYTHONDONTWRITEBYTECODE=1 python3 tools/TemporalWiringAcceptance/validate.py \
  <new-run-directory> --output <new-tp03-validation.json>
```

O remains `DM.DmProvider 8.3.1.47463`; W must be the actual new candidate package. The runner supplies the TEST wrapper, single-node build flags and isolated CLI home. Do not run a second build/DB window concurrently. O/W must verify `WDM_PROVIDER_TEST` identities, unique exact ownership and final fresh-connection absence. Do not modify old runner manifests or old evidence to introduce this policy.

The validator pins the three TP01 Python modules before importing them. The validator pin matches the current repository helper (`96faba6fe24e488921eeb702bc2c69cccea74316681d34596f6752dab1b3eb14`); the contracts pin reflects the package/source rename to `W.DamengProvider` (`408f12017593a05f24e1f642baecc5d3613fe78303ce520cf4485cf70e89d016`). The packed-oracle pin remains unchanged. These pins apply to new runs; historical frozen inputs and acceptance records are not rewritten or reaccepted. It checks the run's complete tool hashes, producer/archive identity, actual nupkg and runtime DLL SHA/MVID, NuGet assets/lock/deps, C# offline report and v11 serializer shape, one core run's process/ownership receipts and cleanup. TP03 requires an existing source archive and non-null SHA256 matching producer, frozen manifest and actual bytes. The policy's self hash and dependency hashes are recorded separately in its result.

The supported shared profile is exactly `8.1.5.60 / msg21 / utf-8 / ordinary_non_BDTA`, scales 0 through 7, complete CLR local and UTC tick domains, and offsets -779 through +840. W run, W cleanup and O run profiles must all pass the unchanged v11 `physical_profile` gate, then match this exact shared message/encoding scope. `encoding` is the physical effective ServerEncoding; `negotiated_encoding` is the physical private serverEncoding field. Both must be normalized `utf-8`, `encoding_override_empty` must be true, `profile_origin` must be `physical_get_conn_instance_conn_property`, and `negotiated_encoding_origin` must be `physical_conn_property_serverEncoding_field`. The internal field is not claimed to be raw STARTUP bytes. Missing physical provenance, logical fallback, nonempty override, encoding conflict, the old GB scope or a different message version fails even after artifact hashes are rebound.

The old logical GB label was corrected by [TP01 profile origin correction](../../docs/implementation/temporal-native/TP01-profile-origin-correction.md). The approved source and exact scopes are recorded in the new [physical v11 handoff](../../docs/implementation/temporal-native/TP01-profile-handoff.physical-v11.md); the old final handoff and all old snapshots remain historical. The v11 TLS baseline has its separate physical scope and grants no TLS wiring acceptance here. Capability/source/package approval must agree; a smaller advertised domain cannot convert a mandatory success into a permitted refusal.

All independently enumerated W core specs are required: exact success, exact Overflow rejection for nonmicrosecond six-digit values or out-of-profile offsets, and CLR constructor preconditions. Both rollback checks require fresh rows zero. O historical outcomes retain their original characterization, while the six established O-to-W seven-digit seeds must be exact successes. Each successful required O/W oracle needs the expected result-column CType, scale, width, mask, type flag and single schema scale, ordinary packed bytes of the exact layout length, matching metadata/hash and independent expected fields, FF9/FF7 and read completion. Ordinary timestamp EXTRACT remains required. This policy omits only TP04's future captured raw-server metadata provenance requirement; it does not relax byte/metadata consistency.

## Pure contracts

Low runs after source freeze:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s tools/TemporalWiringAcceptance -p test_contracts.py -v
```

The fixtures independently enumerate core inputs and consume saved integer packed literals. Their v11 C# shape and physical UTF-8 profile are handwritten expected data. Dummy archive/package/DLL bytes are explicitly marked synthetic. Every negative test first verifies a normal synthetic control, then changes one constraint; ordinary transport hashes are rebound where needed to reach the intended semantic check. The updated physical-source tests cover W run, W cleanup and O run plus the v11 capture shape. They do not expand the existing core-only writer/reader or TLS scope.

The internal `_package_inspector` seam covers synthetic package/loaded-DLL inspection only. Injection is detected from the Python argument, independently of artifact fields; every injected result has `synthetic_expected_only=true` and `tp03_wiring_accepted=false`. It never produces the production `tp03_wiring_accepted` status. CLI has no seam option, and its default path rejects dummy packages. Passing these contracts proves expected policy control flow only; Low must separately pass the default entry against the real candidate package and real shared TEST run.

The CLI refuses to overwrite an existing output, returns nonzero on any rejected or incomplete run, and emits safe issue codes rather than raw package, database or exception details. No connection strings, credentials or raw authentication belong in input/output artifacts.
