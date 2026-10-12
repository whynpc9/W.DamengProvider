#!/usr/bin/env python3
"""Generate deterministic, expected-only native temporal logic vectors."""

from __future__ import annotations

import json
from datetime import date
from pathlib import Path


ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT / "expectations.json"

TICKS_PER_MILLISECOND = 10_000
TICKS_PER_SECOND = 10_000_000
TICKS_PER_MINUTE = 60 * TICKS_PER_SECOND
TICKS_PER_HOUR = 60 * TICKS_PER_MINUTE
TICKS_PER_DAY = 24 * TICKS_PER_HOUR
MAX_DATETIME_TICKS = 3_155_378_975_999_999_999
MIN_TIMESPAN_TICKS = -(1 << 63)
MAX_TIMESPAN_TICKS = (1 << 63) - 1
GENERATION_SEED = 20261008
MASK64 = (1 << 64) - 1


def datetime_ticks(
    year: int,
    month: int,
    day: int,
    hour: int = 0,
    minute: int = 0,
    second: int = 0,
    fractional_ticks: int = 0,
) -> int:
    day_number = date(year, month, day).toordinal() - 1
    return (
        day_number * TICKS_PER_DAY
        + hour * TICKS_PER_HOUR
        + minute * TICKS_PER_MINUTE
        + second * TICKS_PER_SECOND
        + fractional_ticks
    )


def datetime_fields(ticks: int) -> dict[str, object]:
    day_number, ticks_in_day = divmod(ticks, TICKS_PER_DAY)
    calendar_date = date.fromordinal(day_number + 1)
    seconds_in_day, fractional_ticks = divmod(ticks_in_day, TICKS_PER_SECOND)
    hour, seconds_in_hour = divmod(seconds_in_day, 3600)
    minute, second = divmod(seconds_in_hour, 60)
    return {
        "year": calendar_date.year,
        "month": calendar_date.month,
        "day": calendar_date.day,
        "hour": hour,
        "minute": minute,
        "second": second,
        "fractionalTicks": str(fractional_ticks),
        "nanosecondsWithinSecond": fractional_ticks * 100,
    }


def fields_from_ticks(ticks: int, offset_minutes: int | None = None) -> dict[str, object]:
    fields = datetime_fields(ticks)
    fields["offsetMinutes"] = offset_minutes
    return fields


def case(
    case_id: str,
    operation: str,
    input_unit: str,
    input_value: dict[str, object],
    expected: dict[str, object],
    basis: str,
) -> dict[str, object]:
    return {
        "id": case_id,
        "operation": operation,
        "input_unit": input_unit,
        "input": input_value,
        "expected": expected,
        "basis": basis,
        "expected_only": True,
        "status": "not_run",
        "server_evidence": False,
    }


def datetime_case(case_id: str, ticks: int, basis: str) -> dict[str, object]:
    return case(
        case_id,
        "project_datetime",
        "DateTime ticks: 100 ns since 0001-01-01T00:00:00",
        {"type": "DateTime", "ticks": str(ticks), "kind": "Unspecified"},
        {
            "ticks": str(ticks),
            "kind": "Unspecified",
            "fields": datetime_fields(ticks),
        },
        basis,
    )


def scale_case(case_id: str, ticks: int, scale: int, basis: str) -> dict[str, object]:
    fractional_ticks = ticks % TICKS_PER_SECOND
    quantum_ticks = 10 ** (7 - scale)
    input_value = {
        "type": "DateTime",
        "ticks": str(ticks),
        "kind": "Unspecified",
        "scale": scale,
    }
    if fractional_ticks % quantum_ticks == 0:
        expected = {
            "decision": "accept_exact",
            "ticks": str(ticks),
            "fractionalTicks": str(fractional_ticks),
            "quantumTicks": str(quantum_ticks),
        }
    else:
        expected = {
            "exception": "OverflowException",
            "reason": "fractional_ticks_not_divisible_by_scale_quantum",
            "fractionalTicks": str(fractional_ticks),
            "quantumTicks": str(quantum_ticks),
        }
    if scale == 6:
        expected["scale7_exact_control"] = {
            "decision": "accept_exact",
            "ticks": str(ticks),
            "fractionalTicks": str(fractional_ticks),
        }
    return case(
        case_id,
        "validate_fractional_ticks",
        "DateTime ticks and target decimal-second scale",
        input_value,
        expected,
        basis,
    )


def raw_temporal_fields(
    year: int = 2000,
    month: int = 1,
    day: int = 1,
    hour: int = 0,
    minute: int = 0,
    second: int = 0,
    nanoseconds: int = 0,
    offset_minutes: int | None = None,
) -> dict[str, object]:
    return {
        "year": year,
        "month": month,
        "day": day,
        "hour": hour,
        "minute": minute,
        "second": second,
        "nanosecondsWithinSecond": nanoseconds,
        "offsetMinutes": offset_minutes,
    }


def make_cases() -> list[dict[str, object]]:
    cases: list[dict[str, object]] = []

    # Exact CLR calendar endpoints and their adjacent tick values.
    extrema = [
        ("datetime_min", 0),
        ("datetime_min_plus_1_tick", 1),
        ("datetime_max", MAX_DATETIME_TICKS),
        ("datetime_max_minus_1_tick", MAX_DATETIME_TICKS - 1),
        ("datetime_max_minus_9_ticks", MAX_DATETIME_TICKS - 9),
    ]
    for case_id, ticks in extrema:
        cases.append(
            datetime_case(
                case_id,
                ticks,
                "DateTime's defined CLR tick range; calendar fields are integer division by ticks per day/second.",
            )
        )

    # Gregorian day boundaries: 2000 is a leap century; 1900 and 2100 are not.
    transitions = [
        ("leap_year_feb_28_to_feb_29_2000", 2000, 2, 28),
        ("leap_year_feb_29_to_mar_01_2000", 2000, 2, 29),
        ("nonleap_century_feb_28_to_mar_01_1900", 1900, 2, 28),
        ("nonleap_century_feb_28_to_mar_01_2100", 2100, 2, 28),
    ]
    for case_id, year, month, day in transitions:
        final_tick = datetime_ticks(year, month, day, 23, 59, 59, TICKS_PER_SECOND - 1)
        cases.append(
            datetime_case(
                case_id,
                final_tick + 1,
                "Proleptic Gregorian calendar arithmetic; one tick after the final tick of the source day.",
            )
        )

    # Fixed-seed tick values exercise the full CLR range without storing JSON numbers.
    state = GENERATION_SEED
    for index in range(4):
        state = (state * 6_364_136_223_846_793_005 + 1) & MASK64
        ticks = state % (MAX_DATETIME_TICKS + 1)
        cases.append(
            datetime_case(
                f"datetime_seed_{GENERATION_SEED}_{index:02d}",
                ticks,
                "Generated by the fixture's documented 64-bit LCG and fixed seed; CLR tick identity only.",
            )
        )

    # Each final decimal tick digit is paired with both legacy microsecond and 7-digit checks.
    tail_base = datetime_ticks(2024, 2, 29, 12, 34, 56)
    for digit in range(10):
        ticks = tail_base + digit
        cases.append(
            scale_case(
                f"tick_tail_{digit}_scale6",
                ticks,
                6,
                "Scale 6 requires fractional ticks divisible by 10; no rounding or carry is permitted.",
            )
        )

    # Scale 0 and 3 quantum boundaries, computed as integer tick remainders.
    scale_boundaries = [
        ("scale0_whole_second", tail_base, 0),
        ("scale0_one_tick", tail_base + 1, 0),
        ("scale3_exact_one_millisecond", tail_base + TICKS_PER_MILLISECOND, 3),
        ("scale3_one_tick_below_quantum", tail_base + TICKS_PER_MILLISECOND - 1, 3),
    ]
    for case_id, ticks, scale in scale_boundaries:
        cases.append(
            scale_case(
                case_id,
                ticks,
                scale,
                f"Scale {scale} quantum is 10^(7-scale) CLR ticks.",
            )
        )

    # Raw logical timestamp fields: these are not encoded wire byte strings.
    nanosecond_vectors = [
        ("scale8_ns10", 8, 10),
        ("scale8_ns90", 8, 90),
        ("scale8_ns100", 8, 100),
        ("scale8_ns1_invalid", 8, 1),
        ("scale9_ns1", 9, 1),
        ("scale9_ns99", 9, 99),
        ("scale9_ns100", 9, 100),
    ]
    for case_id, scale, nanoseconds in nanosecond_vectors:
        raw_fields = raw_temporal_fields(nanoseconds=nanoseconds)
        input_value = {
            "layout_candidate": "TS9",
            "fields": raw_fields,
            "scale": scale,
            "server_source": False,
            "wire_bytes_included": False,
        }
        if nanoseconds % (10 ** (9 - scale)) != 0:
            expected = {
                "format_rejection": "declared_scale_violation",
                "exception_type": "not_fixed_by_plan",
            }
            operation = "format"
            basis = "Nanoseconds are checked directly against the declared scale before CLR projection or formatting; exact exception type is not fixed by the plan."
        else:
            fraction_9_digits = f"{nanoseconds:09d}"
            expected = {
                "format": fraction_9_digits[:scale],
                "format_scope": "fractional_digits_only",
            }
            if nanoseconds % 100:
                expected["clr_exception"] = "OverflowException"
            else:
                expected["ticks"] = str(nanoseconds // 100)
            operation = "format"
            basis = "Integer nanosecond formatting preserves the declared digits; CLR projection is exact only when nanoseconds are divisible by 100."
        cases.append(
            case(
                case_id,
                operation,
                "Synthetic DmTemporalFields nanosecondsWithinSecond; logical fields only, not server bytes",
                input_value,
                expected,
                basis,
            )
        )

    # User Scale range is intentionally separated from type/layout/profile capability.
    scale_inputs: list[tuple[str, dict[str, object], dict[str, object]]] = [
        (
            "user_scale_unset",
            {"parameterType": "DateTime", "scaleSet": False},
            {"decision": "no_explicit_scale_request"},
        )
    ]
    for scale in (0, 3, 6, 7, 8, 9):
        scale_inputs.append(
            (
                f"user_scale_{scale}",
                {"parameterType": "DateTime", "scaleSet": True, "scale": scale},
                {
                    "decision": "within_user_scale_range",
                    "profile_or_layout_capability": "not_asserted_by_this_vector",
                },
            )
        )
    for scale in (19, 23, 255):
        scale_inputs.append(
            (
                f"user_scale_{scale}_out_of_range",
                {"parameterType": "DateTime", "scaleSet": True, "scale": scale},
                {"exception": "ArgumentOutOfRangeException"},
            )
        )
    for case_id, input_value, expected in scale_inputs:
        cases.append(
            case(
                case_id,
                "validate_user_scale",
                "DmParameter.Scale decimal fractional-second scale; set/unset state is explicit",
                input_value,
                expected,
                "For a resolved time type, explicit Scale must be in 0..9; the range check does not grant wire/profile support.",
            )
        )

    precision_inputs = [
        ("time_precision_unset", {"parameterType": "DateTime", "precisionSet": False}, {"decision": "no_precision_limit_requested"}),
        ("time_precision_explicit_zero", {"parameterType": "DateTime", "precisionSet": True, "precision": 0}, {"decision": "no_precision_limit_requested"}),
        ("time_precision_7_rejected", {"parameterType": "DateTime", "precisionSet": True, "precision": 7}, {"exception": "NotSupportedException"}),
        ("time_precision_9_rejected", {"parameterType": "DateTime", "precisionSet": True, "precision": 9}, {"exception": "NotSupportedException"}),
    ]
    for case_id, input_value, expected in precision_inputs:
        cases.append(
            case(
                case_id,
                "validate_time_precision",
                "DmParameter.Precision, distinct from DmParameter.Scale and wire width",
                input_value,
                expected,
                "Settled temporal parameter contract: unset/0 imposes no Precision limit; nonzero Precision is unsupported.",
            )
        )

    # Safe DateTimeOffset local date lets each CLR-valid offset be constructed independently.
    dto_local_ticks = datetime_ticks(2000, 1, 2, 12, 34, 56, 1_234_567)
    for offset in (0, 480, -300, 330, -840, -780, -779, 840):
        utc_ticks = dto_local_ticks - offset * TICKS_PER_MINUTE
        within_current_guard = -779 <= offset <= 840
        cases.append(
            case(
                f"datetimeoffset_valid_clr_offset_{offset:+d}",
                "construct_datetime_offset_and_check_current_guard",
                "DateTimeOffset local ticks (100 ns) plus integer offset minutes",
                {"localTicks": str(dto_local_ticks), "offsetMinutes": offset},
                {
                    "constructible_by_clr": True,
                    "localTicks": str(dto_local_ticks),
                    "offsetMinutes": offset,
                    "utcTicks": str(utc_ticks),
                    "currentWWriterGuard": {
                        "rangeInclusiveMinutes": [-779, 840],
                        "decision": "within_guard" if within_current_guard else "reject",
                        "exception": None if within_current_guard else "OverflowException",
                    },
                    "server_acceptance": "not_asserted",
                },
                "DateTimeOffset keeps local ticks and integer offset; UTC ticks equal local ticks minus offset minutes. Current W guard is a source fact, not a claim about all server profiles.",
            )
        )

    # CLR constructor negatives are distinct from raw-field projection negatives.
    for case_id, local_ticks, offset, reason in [
        ("datetimeoffset_ctor_offset_plus_14h01", dto_local_ticks, 841, "offset exceeds +14:00"),
        ("datetimeoffset_ctor_offset_minus_14h01", dto_local_ticks, -841, "offset is below -14:00"),
        ("datetimeoffset_ctor_utc_before_min", 0, 1, "local minus offset is below DateTime.MinValue"),
        ("datetimeoffset_ctor_utc_after_max", MAX_DATETIME_TICKS, -1, "local minus offset is above DateTime.MaxValue"),
    ]:
        cases.append(
            case(
                case_id,
                "construct_datetime_offset",
                "DateTimeOffset constructor local ticks (100 ns) and integer offset minutes",
                {"localTicks": str(local_ticks), "offsetMinutes": offset},
                {"exception": "ArgumentOutOfRangeException", "reason": reason},
                "CLR constructor precondition only; no provider invocation or server write is represented.",
            )
        )

    # Logical raw fields can be retained even when the corresponding CLR projection cannot exist.
    raw_offset_cases = [
        ("raw_wire_local_min_utc_underflow", 0, 1),
        ("raw_wire_local_max_utc_overflow", MAX_DATETIME_TICKS, -1),
    ]
    for case_id, local_ticks, offset in raw_offset_cases:
        fields = fields_from_ticks(local_ticks, offset)
        cases.append(
            case(
                case_id,
                "project_raw_fields_to_datetime_offset",
                "Synthetic timezone fields; local DateTime fields plus offset minutes, with no DateTimeOffset construction",
                {
                    "layout_candidate": "TS_TZ11",
                    "fields": fields,
                    "server_source": False,
                    "wire_bytes_included": False,
                },
                {
                    "raw_fields_retained": True,
                    "clr_exception": "OverflowException",
                    "reason": "localTicks minus offsetMinutes times ticksPerMinute is outside DateTime range",
                },
                "TP-00 requires a wide/checked local-to-UTC range check when projecting raw timezone fields to DateTimeOffset.",
            )
        )

    # TimeOnly's final representable tick and raw hour=24 behavior.
    timeonly_max_ticks = TICKS_PER_DAY - 1
    cases.append(
        case(
            "timeonly_last_tick",
            "project_timeonly",
            "TimeOnly ticks since 00:00:00, 100 ns units",
            {"type": "TimeOnly", "ticks": str(timeonly_max_ticks)},
            {
                "ticks": str(timeonly_max_ticks),
                "fields": {
                    "hour": 23,
                    "minute": 59,
                    "second": 59,
                    "fractionalTicks": "9999999",
                    "nanosecondsWithinSecond": 999_999_900,
                },
            },
            "TimeOnly's last CLR value is one 100 ns tick before 24:00:00.",
        )
    )
    cases.append(
        case(
            "time_raw_hour24_projection",
            "project_timeonly",
            "Synthetic TIME fields; logical fields only, not server bytes",
            {
                "layout_candidate": "TIME5",
                "fields": raw_temporal_fields(hour=24),
                "server_source": False,
                "wire_bytes_included": False,
            },
            {"clr_exception": "OverflowException", "forbidden_normalization": "modulo_or_next_day_carry"},
            "Plan and TP-00 require hour=24 CLR projection to fail without modulo reduction or day carry.",
        )
    )

    # TimeSpan remains a signed 100 ns quantity; no Math.Abs(long.MinValue) is needed.
    interval_values = [
        ("timespan_positive", 1_234_567_890_123),
        ("timespan_negative", -1_234_567_890_123),
        ("timespan_zero", 0),
        ("timespan_long_min", MIN_TIMESPAN_TICKS),
        ("timespan_long_max", MAX_TIMESPAN_TICKS),
    ]
    for case_id, ticks in interval_values:
        cases.append(
            case(
                case_id,
                "project_timespan",
                "TimeSpan ticks: signed 100 ns units",
                {"type": "TimeSpan", "ticks": str(ticks), "scale": 7},
                {"ticks": str(ticks), "signPreserved": True},
                "TimeSpan's CLR tick domain is signed Int64; conversion expectations use integer identity, including long.MinValue.",
            )
        )
    cases.append(
        case(
            "timespan_negative_one_tick_scale6_rejected",
            "validate_interval_fraction",
            "TimeSpan ticks and decimal-second scale",
            {"type": "TimeSpan", "ticks": "-1", "scale": 6},
            {"exception": "OverflowException", "reason": "fractional_ticks_not_divisible_by_10"},
            "The interval path retains strict microsecond precision; negative remainder is checked without Math.Abs.",
        )
    )

    twelve_days_ticks = 12 * TICKS_PER_DAY
    for case_id, leading_precision, expected in [
        ("interval_leading_precision_2_accepts_12_days", 2, {"decision": "accept_exact", "ticks": str(twelve_days_ticks)}),
        ("interval_leading_precision_1_rejects_12_days", 1, {"exception": "OverflowException", "reason": "day_component_exceeds_leading_precision"}),
    ]:
        cases.append(
            case(
                case_id,
                "validate_interval_leading_precision",
                "Signed INTERVAL DAY TO SECOND represented by TimeSpan ticks",
                {"ticks": str(twelve_days_ticks), "leadingPrecision": leading_precision, "scale": 7},
                expected,
                "The leading precision counts decimal digits in the day component; months are not represented as fixed ticks.",
            )
        )

    # Public 12-byte helpers use microseconds; internal temporal fields preserve 7-digit ticks.
    cases.extend(
        [
            case(
                "legacy12_microseconds_to_internal_ticks",
                "decode_legacy12",
                "Public legacy 12-byte logical buffer fraction in microseconds",
                {
                    "legacy12Fields": {
                        "year": 2000,
                        "month": 1,
                        "day": 1,
                        "hour": 0,
                        "minute": 0,
                        "second": 0,
                        "microsecondsWithinSecond": 123456,
                        "offsetMinutes": 0,
                    },
                    "raw_byte_array_included": False,
                },
                {"fractionalTicks": "1234560", "nanosecondsWithinSecond": 123456000},
                "Root-approved D02: every public legacy 12-byte buffer fraction is microseconds; conversion to 100 ns ticks multiplies by 10.",
            ),
            case(
                "internal_ticks_to_legacy12_microseconds",
                "encode_legacy12",
                "CLR DateTime fractional ticks in 100 ns units",
                {"fractionalTicks": "1234560"},
                {"microsecondsWithinSecond": 123456},
                "Exact whole-microsecond conversion divides 100 ns ticks by 10.",
            ),
            case(
                "internal_seventh_digit_rejected_by_legacy12",
                "encode_legacy12",
                "CLR DateTime fractional ticks in 100 ns units",
                {"fractionalTicks": "1234567"},
                {"exception": "OverflowException", "reason": "not_an_integral_microsecond"},
                "D02 forbids rounding or unit guessing in the public legacy 12-byte path.",
            ),
            case(
                "raw_nanoseconds_preserved_as_seven_digit_ticks",
                "project_raw_fields_to_internal_ticks",
                "Synthetic extended timestamp fields; nanosecondsWithinSecond before any DateTime construction",
                {
                    "layout_candidate": "TS9",
                    "fields": raw_temporal_fields(nanoseconds=123456700),
                    "server_source": False,
                    "wire_bytes_included": False,
                },
                {"fractionalTicks": "1234567", "nanosecondsWithinSecond": 123456700},
                "Raw nanoseconds divide by 100 exactly to seven fractional CLR ticks; the vector never passes through DateTime first.",
            ),
            case(
                "raw_nanoseconds_not_integral_microsecond_rejected_by_legacy12",
                "decode_raw_fields_to_legacy12",
                "Synthetic extended timestamp fields; nanosecondsWithinSecond before any DateTime construction",
                {
                    "layout_candidate": "TS9",
                    "fields": raw_temporal_fields(nanoseconds=123456700),
                    "server_source": False,
                    "wire_bytes_included": False,
                },
                {"exception": "OverflowException", "reason": "nanoseconds_not_divisible_by_1000"},
                "D02's public legacy 12-byte projection requires integral microseconds; internal ns/ticks remain separate and exact.",
            ),
        ]
    )

    ids = [item["id"] for item in cases]
    if len(ids) != len(set(ids)):
        raise ValueError("case IDs must be unique")
    return cases


def main() -> None:
    document = {
        "fixture": "native temporal precision expected-only logic vectors",
        "schema_version": 1,
        "expected_only": True,
        "execution_status": "not_run",
        "server_evidence": False,
        "generation": {
            "seed": GENERATION_SEED,
            "algorithm": "64-bit LCG: state=(state*6364136223846793005+1) mod 2^64; ticks=state mod (DateTime.MaxValue.Ticks+1)",
            "randomized_case_count": 4,
        },
        "source_basis": [
            "docs/implementation/temporal-dotnet-precision-plan.v2.md sections 4 and 7",
            "docs/implementation/temporal-native/TP00-astra-design.md sections 2 and 3",
        ],
        "cases": make_cases(),
    }
    OUTPUT.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
