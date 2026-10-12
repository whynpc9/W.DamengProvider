# Native temporal expected-only vectors

`expectations.json` contains deterministic logical inputs and expected results for TP-02's pure integer and CLR temporal rules. Every case has a unique ID, an input unit, `expected_only: true`, `status: "not_run"`, and `server_evidence: false`. These vectors have not been run against the driver or a server.

The contract basis is `docs/implementation/temporal-dotnet-precision-plan.v2.md` sections 4 and 7 and `docs/implementation/temporal-native/TP00-astra-design.md` sections 2 and 3. The file has no credentials, database output, or fabricated server bytes. `layout_candidate` labels the logical layout a case targets; it does not claim that the listed fields came from a server or form an encoded packet. The 8/9 digit vectors pass raw integer nanoseconds directly and never construct a `DateTime` first.

## Coverage

- CLR `DateTime` endpoints and adjacent ticks, every final tick digit 0 through 9, and Gregorian day transitions across leap-year and century boundaries.
- Fixed-seed tick samples spanning the CLR range. The generator uses seed `20261008` with the documented 64-bit LCG recurrence in the JSON metadata; it does not depend on Python's randomized hash or a runtime PRNG.
- Scale 0, 3, 6, and 7 integer tick quanta; user `Scale` unset/0/3/6/7/8/9/19/23/255; and `Precision` unset/0/7/9. In-range user scale cases assert only the numeric range decision. They do not imply type, wire-layout, or server-profile support for 7/8/9.
- Raw logical nanoseconds at scale 8 (10, 90, 100, and invalid 1) and scale 9 (1, 99, 100). `expected.format` contains only the declared fractional digits because the plan fixes digit preservation but does not define a new full timestamp separator pattern. CLR projection is expected to overflow when nanoseconds are not divisible by 100. The exact exception class for the scale-8 invalid raw value is not set by the plan and is deliberately left unresolved.
- `DateTimeOffset` CLR-constructible offsets 0, +480, -300, +330, -840, -780, -779, and +840 minutes, plus CLR constructor failures and raw local/UTC range failures. The current W writer guard `[-779, 840]` is a source-level product fact; it is not represented as a server-wide offset limit or acceptance result.
- `TimeOnly`'s final tick and logical TIME hour 24 projection; signed `TimeSpan` values including zero and both `Int64` endpoints; and interval leading precision.
- The root-approved public legacy 12-byte microsecond unit paired with internal 100 ns ticks and raw nanoseconds. Public-buffer fields are logical values only: the fixture does not invent a 12-byte hex golden. Non-integral microseconds are rejected by that public projection while internal seven-digit ticks remain exact.

All fields named `ticks`, `fractionalTicks`, `localTicks`, or `utcTicks` are decimal strings, never JSON numbers. Nanoseconds, scale, offsets, and calendar components use JSON integers with their units named explicitly.

## Regeneration

From the repository root:

```sh
python3 tests/fixtures/temporal-native/generate_expectations.py
```

The generator uses only the Python standard library and writes `expectations.json`. It performs no network, database, .NET, or credential access. Historical official wire goldens remain in their original tests and are not regenerated or relabeled here.
