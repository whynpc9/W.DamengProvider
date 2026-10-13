"""TP06 supplement policy. No provider code, formatter, encoder or DB access.

Case expectations are handwritten integers below, independently reconstructed here.
The serialized manifest is an interface, and must equal this policy exactly.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import struct
from datetime import date

DESIGN_SHA = "c4e1f7cdde9fbb79654a53d6f60189eaf84b667235b91a90ece45e92abe78862"
ADDENDUM_SHA = "4b378e8882814e8f4630a76c8fde3c32615021bbcd4c4b721f5f8c8c35856a8d"
OBSERVABILITY_SHA = "f24005af7472f5e1eab348999605345f015eefba58def18452237bf14619bca7"
RESULT_DESCRIPTOR_SHA = "a926f5632dbc6f6b7902b42857be5c9452676d0e5c024999d62909d99e720d5e"
WRITE_RECOVERY_SHA = "9a9a25433b0631ac7a825df9358d81185da9336b2c57057484665f1f670ce980"
READ_DIAGNOSTIC_SHA = "eeb8165bb719a4a757aa3e7d62cf11f3dc006b33537e9ca93dc4c58d3f721bc6"
INTERVAL_TEXT_SHA = "282c9a4323f9f7fc3d13645a0df0b3723efcfd65a7c1fd80dc9d4f8851e40ef2"
INTERVAL_OUTER_MINUS_SHA = "563f52b307cd4692332568174b71e402514b6f3aba7469081f7dfe3c596c1163"
TLS_READ6_SHA = "259b358661f63b7e897cd5f5891c68081f81e4aab9694e0444d0ca5c420ffae9"
TLS_READ6_ROOT_DECISION = "root-20261009-supplement-tls-g2-read6-v1"
READ_ROOT_DECISION = "root-20261009-supplement-g2-read-contract-v1"
BIND_SQL_SOURCE_SHA = "0360d2c3887daf50b6cc08437701ce3fe30aa605c65cde88ae4576497c0fa507"
BASE_CONTRACT_SHA = "408f12017593a05f24e1f642baecc5d3613fe78303ce520cf4485cf70e89d016"
PACKED_SHA = "bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9"
W_PIN = dict(id="W.DamengProvider", version="0.1.0-temporal.20261008144915", asset="lib/net10.0/W.DamengProvider.dll",
    package_sha256="da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d",
    dll_sha256="843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980", mvid="aa23e673-957c-4b1e-bf0f-291ff60077e6")
SOURCE_SHA = "8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f"
ARCHIVE_SHA = "836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441"
IDENTITIES_SHA = "e14eb1baca9a3c07b04f988dd3bcdb652048fd1a61211ad8c2cb117a9e0955a3"
CAPS_SHA = "f10acfd86b5e7158eac0a0aa8cfb972418881837b4bfe63205245d82a08f54fa"
SOURCE_HEAD = "fe89bd44abe99c1608499f71f4634a13e732c826"
OFFLINE_CHECKS = ["owner_valid", "owner_bad_nonce", "marker_exact", "create_collision", "create_states",
    "cleanup_exact", "cleanup_foreign", "cleanup_known_failure", "cleanup_fresh_failure", "duplicate_json",
    "physical_missing_closed", "safe_exception", "server_null_sync_getter", "server_null_async_getter",
    "server_null_conflict_refused", "server_nonnull_projection_getters", "frame_classification",
    "prepared_collection_exact", "prepared_execute_control", "prepared_extra5_refused", "prepared_allocation3_refused",
    "direct5_error_response", "direct_allocation_response_refused", "direct_ambiguous_response_refused",
    "recovery_open_control", "recovery_broken_control", "recovery_closed_control", "recovery_unapproved_state_rejected",
    "recovery_old_send_rejected", "recovery_wrong_probe_error_rejected", "recovery_fresh_id_reuse_rejected",
    "recovery_fresh_scalar_rejected", "recovery_midstep_retained", "recovery_server_cannot_retire",
    "recovery_target_business_sent_rejected", "recovery_independent_stage_origin",
    "read_partial_raw_guard_retained", "read_partial_value_guard_retained", "read_partial_server_guard_retained",
    "read_partial_getter_error_retained", "read_text_bounded_unknown", "read_text_overlong",
    "read_server_null_attempt_retained", "read_partial_not_complete",
    "interval_text_observed_channels_control", "interval_text_declared_positive_controls",
    "interval_text_value_width_refused", "interval_text_value_sign_refused", "interval_text_fraction_refused",
    "interval_text_server_domain_refused", "interval_text_wrapper_qualifier_refused", "interval_text_numeric_mutation_detected",
    "interval_outer_minus_observed_control", "interval_outer_minus_positive_controls",
    "interval_outer_minus_conflict_refused", "interval_outer_minus_numeric_mutation_detected"]
WIDTHS = {14: 3, 15: 5, 16: 8, 23: 10, 26: 9, 27: 11, 21: 24}
SECOND = 10_000_000
DAY = SECOND * 86400
ORDINARY = (date(2026, 7, 23).toordinal() - 1) * DAY + (14 * 3600 + 15 * 60 + 16) * SECOND
TIME_ORDINARY = (14 * 3600 + 15 * 60 + 16) * SECOND + 1_234_560
_REVIEWED = {}


class ContractError(ValueError):
    """Only safe fixed contract codes belong in this exception."""


class UnverifiedResultDescriptor(ContractError):
    def __init__(self, case_id, stage_id, write_tuple, read_tuple):
        super().__init__("unverified_result_descriptor")
        self.detail = dict(case_id=case_id, stage_id=stage_id, write_tuple=write_tuple, read_tuple=read_tuple,
                           observation_status="unverified", candidate_acceptance=False)


def require(condition, code):
    if not condition:
        raise ContractError(code)


def integer(value, code, minimum=None):
    require(type(value) is int and (minimum is None or value >= minimum), code + ":integer")
    return value


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate_json_key")
        result[key] = value
    return result


def loads_strict(text):
    def invalid(_):
        raise ContractError("non_finite_number")
    return json.loads(text, object_pairs_hook=_pairs, parse_constant=invalid)


def load_json(path):
    return loads_strict(Path(path).read_text())


def validate_candidate_pins(identity, identity_sha, source_sha, archive_sha, caps_sha):
    same(identity.get("W"), W_PIN, "same_TP06_candidate")
    for value, expected, name in ((identity_sha, IDENTITIES_SHA, "identities"), (source_sha, SOURCE_SHA, "source"),
                                (archive_sha, ARCHIVE_SHA, "archive"), (caps_sha, CAPS_SHA, "root_approved_caps")):
        require(value == expected, "same_TP06_pin:" + name)
    producer = identity.get("producer")
    require(type(producer) is dict and producer.get("source_head") == SOURCE_HEAD
            and producer.get("source_manifest_sha256") == SOURCE_SHA
            and producer.get("source_archive_sha256") == ARCHIVE_SHA, "same_TP06_producer")


def reviewed_module(name, filename, expected_hash=None):
    here = Path(__file__).resolve().parent
    original = "contracts.py" if filename == "v11-contracts.py" else "packed_oracle.py"
    choices = [here / filename, here.parent / "TemporalBoundaryProbe" / original,
               here.parent / "build" / filename]
    path = next((p for p in choices if p.is_file()), None)
    require(path is not None, "reviewed_module_missing")
    if expected_hash:
        require(digest(path) == expected_hash, "reviewed_module_changed")
    cache_key = (name, str(path.resolve()), digest(path))
    if cache_key in _REVIEWED:
        return _REVIEWED[cache_key], path
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    _REVIEWED[cache_key] = module
    return module, path


def vectors():
    return {
        "ts_second": dict(kind="ts", ticks=ORDINARY, offset_minutes=None),
        "ts_micro": dict(kind="ts", ticks=ORDINARY + 1_234_560, offset_minutes=None),
        "ts_tick": dict(kind="ts", ticks=ORDINARY + 1_234_567, offset_minutes=None),
        "ts_scale3": dict(kind="ts", ticks=ORDINARY + 1_230_000, offset_minutes=None),
        "tz_second": dict(kind="tz", ticks=ORDINARY, offset_minutes=330),
        "tz_micro": dict(kind="tz", ticks=ORDINARY + 1_234_560, offset_minutes=330),
        "tz_tick": dict(kind="tz", ticks=ORDINARY + 1_234_567, offset_minutes=330),
        "tz_scale3": dict(kind="tz", ticks=ORDINARY + 1_230_000, offset_minutes=330),
        "date_min": dict(kind="date", ticks=0, offset_minutes=None),
        "date_max": dict(kind="date", ticks=(date(9999, 12, 31).toordinal() - 1) * DAY, offset_minutes=None),
        "date_ordinary": dict(kind="date", ticks=(date(2026, 7, 23).toordinal() - 1) * DAY, offset_minutes=None),
        "time_zero": dict(kind="time", ticks=0, offset_minutes=None),
        "time_last_micro": dict(kind="time", ticks=DAY - 10, offset_minutes=None),
        "time_last_tick": dict(kind="time", ticks=DAY - 1, offset_minutes=None),
        "time_ordinary": dict(kind="time", ticks=TIME_ORDINARY, offset_minutes=None),
        "interval_zero": dict(kind="interval", ticks=0, offset_minutes=None),
        "interval_pos": dict(kind="interval", ticks=25 * 3600 * SECOND + 1_234_560, offset_minutes=None),
        "interval_neg": dict(kind="interval", ticks=-(25 * 3600 * SECOND + 1_234_560), offset_minutes=None),
        "interval_min_micro": dict(kind="interval", ticks=-(2 ** 63) + 8, offset_minutes=None),
        "interval_max_micro": dict(kind="interval", ticks=2 ** 63 - 8, offset_minutes=None),
        "interval_min": dict(kind="interval", ticks=-(2 ** 63), offset_minutes=None),
        "interval_max": dict(kind="interval", ticks=2 ** 63 - 1, offset_minutes=None),
        "interval_tick": dict(kind="interval", ticks=1, offset_minutes=None),
        "interval_99days": dict(kind="interval", ticks=99 * DAY, offset_minutes=None),
        "interval_100days": dict(kind="interval", ticks=100 * DAY, offset_minutes=None),
    }


def stage(name, api, vector, scale, c_type, *, prepare=0, exception=None, affected=None,
          input_vector=None, transaction=None, binding=False, fresh=True, null=False):
    return dict(stage_id=name, api=api, vector=vector, scale=scale, c_type=c_type,
        prepare_calls=prepare, exception=exception, affected_rows=affected,
        input_vector=input_vector or vector, transaction=transaction, binding=binding,
        fresh=fresh, is_null=null)


def case_specs(profile):
    require(profile in ("shared", "tls"), "profile")
    cases = []
    def add(name, stages, **extra):
        cases.append(dict(case_id=name, stages=stages, **extra))
    for family in ("legacy6", "shared7") if profile == "shared" else ("legacy6",):
        scale = 7 if family == "shared7" else 6
        for kind in ("ts", "tz"):
            ctype = (26 if kind == "ts" else 27) if scale == 7 else (16 if kind == "ts" else 23)
            vector = kind + ("_tick" if scale == 7 else "_micro")
            suffix = family + "_" + kind
            add("TX_DIRECT_INSERT_" + suffix, [stage("insert", "ExecuteNonQuery", vector, scale, ctype, affected=1)])
            add("TX_PREPARED_ASYNC_UPDATE_COMMIT_" + suffix,
                [stage("commit", "ExecuteNonQueryAsync", vector + "_commit", scale, ctype, prepare=1,
                       affected=1, input_vector=vector + "_commit", transaction="CommitAsync")])
            add("TX_ASYNC_UPDATE_ROLLBACK_" + suffix,
                [stage("rollback", "ExecuteNonQueryAsync", vector + "_commit", scale, ctype,
                       affected=1, input_vector=vector + "_rollback", transaction="RollbackAsync")])
    add("ASYNC_REJECT_THEN_QUERY", [
        stage("control", "ExecuteNonQueryAsync", "ts_micro", 6, 16, affected=1),
        stage("fraction", "ExecuteNonQueryAsync", "ts_micro", 6, 16, input_vector="ts_tick", exception="System.OverflowException", affected=0),
        stage("scale7_null", "ExecuteNonQueryAsync", "ts_micro", 6, 16, exception="System.NotSupportedException", affected=0)])
    for kind in ("ts", "tz"):
        ct = (26 if kind == "ts" else 27) if profile == "shared" else (16 if kind == "ts" else 23)
        sc = 7 if profile == "shared" else 6
        add("BIND_REUSE_NO_EXPLICIT_PREPARE_" + ("dt" if kind == "ts" else "dto"), [
            stage("second", "ExecuteReader", kind + "_second", sc, ct, prepare=1, binding=True, fresh=False),
            stage("micro", "ExecuteReader", kind + "_micro", sc, ct, binding=True, fresh=False),
            stage("tick", "ExecuteReader" if profile == "shared" else "ExecuteReaderAsync", kind + ("_tick" if profile == "shared" else "_micro"), sc, ct,
                  input_vector=kind + "_tick", exception=None if profile == "shared" else "System.OverflowException", binding=True, fresh=False)])
        add("BIND_METADATA_AUTOREPREPARE_" + ("dt" if kind == "ts" else "dto"), [
            stage("control", "ExecuteReader", kind + "_micro", sc, ct, prepare=1, binding=True, fresh=False),
            stage("scale3", "ExecuteReader", kind + "_scale3", 3, 16 if kind == "ts" else 23, binding=True, fresh=False),
            stage("scale6", "ExecuteReader", kind + "_micro", 6, 16 if kind == "ts" else 23, binding=True, fresh=False)])
    add("BIND_CLONE_RESET_REOPEN", [
        stage("clone_control", "ExecuteReader", "ts_micro", 7 if profile == "shared" else 6, 26 if profile == "shared" else 16, binding=True, fresh=False),
        stage("clone_after_original_mutation", "ExecuteReader", "ts_micro", 7 if profile == "shared" else 6, 26 if profile == "shared" else 16, binding=True, fresh=False),
        stage("reset", "ExecuteReader", "ts_second", 7 if profile == "shared" else 6, 26 if profile == "shared" else 16, binding=True, fresh=False),
        stage("reopen", "ExecuteReader", "ts_micro", 7 if profile == "shared" else 6, 26 if profile == "shared" else 16, binding=True, fresh=False),
        *([stage("tls_default_explicit_reject", "ExecuteReaderAsync", "ts_micro", 6, 16, exception="System.NotSupportedException", binding=True, fresh=False)] if profile == "tls" else [])])
    for kind, names, ctype, sc in (("date", ("min", "max", "ordinary", "null"), 14, 0),
                                 ("time", ("zero", "last_micro", "ordinary", "null"), 15, 6),
                                 ("interval", ("zero", "pos", "neg", "min_micro", "max_micro", "null"), 21, 1686)):
        for name in names:
            vector = kind + "_" + ("ordinary" if kind != "interval" else "zero") if name == "null" else kind + "_" + name
            add("NATIVE_" + kind.upper() + "_" + name, [stage("write", "ExecuteNonQueryAsync" if name in ("ordinary", "pos") else "ExecuteNonQuery", vector, sc, ctype, affected=1, null=name == "null")])
    add("NATIVE_DATE_REJECT_TIME", [stage("control", "ExecuteNonQuery", "date_ordinary", 0, 14, affected=1),
        stage("reject", "ExecuteNonQueryAsync", "date_ordinary", 0, 14, input_vector="ts_micro", exception="System.OverflowException", affected=0)])
    for name, vector, exception in (("LAST_TICK", "time_last_tick", "System.OverflowException"), ("SCALE7_NULL", "time_ordinary", "System.NotSupportedException")):
        add("NATIVE_TIME_REJECT_" + name, [stage("control", "ExecuteNonQuery", "time_ordinary", 6, 15, affected=1),
            stage("reject", "ExecuteNonQueryAsync", "time_ordinary", 6, 15, input_vector=vector, exception=exception, affected=0)])
    for name in ("min", "max", "tick"):
        add("NATIVE_INTERVAL_REJECT_" + name, [stage("control", "ExecuteNonQuery", "interval_zero", 1686, 21, affected=1),
            stage("reject", "ExecuteNonQueryAsync", "interval_zero", 1686, 21, input_vector="interval_" + name, exception="System.OverflowException", affected=0)])
    add("NATIVE_INTERVAL_REJECT_leading", [stage("control", "ExecuteNonQueryAsync", "interval_99days", 1574, 21, affected=1, prepare=1),
        stage("reject", "ExecuteNonQueryAsync", "interval_99days", 1574, 21, input_vector="interval_100days", exception="System.OverflowException", affected=0, prepare=1)])
    for mode in ("default", "sequential"):
        add("READER_ORDINARY_" + mode, [], reader_mode=mode)
    add("READER_ORDINARY_FETCH", [], reader_mode="fetch")
    return cases


def manifest():
    vals = vectors()
    for kind in ("ts", "tz"):
        for unit in ("micro", "tick"):
            for suffix, extra in (("commit", 1_000_000), ("rollback", 2_000_000)):
                vals[f"{kind}_{unit}_{suffix}"] = dict(vals[f"{kind}_{unit}"], ticks=vals[f"{kind}_{unit}"]["ticks"] + extra)
    return dict(schema_version=1, task="TP-06-supplement", design_sha256=DESIGN_SHA,
                coverage_addendum_sha256=ADDENDUM_SHA, observability_addendum_sha256=OBSERVABILITY_SHA, vectors=vals,
                result_descriptor_addendum_sha256=RESULT_DESCRIPTOR_SHA, root_decision_id=TLS_READ6_ROOT_DECISION,
                shared_g2_read_root_decision_id=READ_ROOT_DECISION, tls_g2_read6_design_sha256=TLS_READ6_SHA,
                tls_g2_read6_rule_id="g2_tls_autoreprepare_scale3_same_family_read6_v1",
                local_write_recovery_addendum_sha256=WRITE_RECOVERY_SHA,
                read_observation_diagnostic_design_sha256=READ_DIAGNOSTIC_SHA,
                interval_text_channel_design_sha256=INTERVAL_TEXT_SHA,
                interval_server_outer_minus_design_sha256=INTERVAL_OUTER_MINUS_SHA,
                binding_sql_source_sha256=BIND_SQL_SOURCE_SHA,
                profiles={p: case_specs(p) for p in ("shared", "tls")})


def expected_fields(spec):
    ticks = spec["ticks"]
    if spec["kind"] == "interval":
        magnitude = abs(ticks)
        days, rem = divmod(magnitude, DAY)
        hours, rem = divmod(rem, 3600 * SECOND)
        minutes, rem = divmod(rem, 60 * SECOND)
        seconds, rem = divmod(rem, SECOND)
        return dict(days=days, hours=hours, minutes=minutes, seconds=seconds, microseconds=rem // 10, negative=ticks < 0)
    if spec["kind"] == "time":
        days, rem = None, ticks
        year = month = day = None
    else:
        days, rem = divmod(ticks, DAY)
        dt = date.fromordinal(days + 1)
        year, month, day = dt.year, dt.month, dt.day
    hour, rem = divmod(rem, 3600 * SECOND)
    minute, rem = divmod(rem, 60 * SECOND)
    second, rem = divmod(rem, SECOND)
    return dict(year=year, month=month, day=day, hour=hour, minute=minute, second=second,
                nanoseconds=rem * 100, offset_minutes=spec["offset_minutes"])


def decode_interval(payload, scale):
    require(type(payload) is bytes and len(payload) == 24, "interval_exact_24_bytes")
    values = struct.unpack("<6i", payload)
    require(values[-1] == scale and scale in (1686, 1574), "interval_header_scale")
    parts = values[:5]
    nonzero = [x for x in parts if x]
    require(not nonzero or all((x < 0) == (nonzero[0] < 0) for x in nonzero), "interval_mixed_sign")
    days, hours, minutes, seconds, micros = map(abs, parts)
    leading = (scale >> 4) & 15
    require(scale >> 8 == 6 and scale & 15 == 6 and days < 10 ** leading,
            "interval_subtype_leading_fraction")
    require(hours < 24 and minutes < 60 and seconds < 60 and micros < 1_000_000, "interval_component_domain")
    ticks = days * DAY + hours * 3600 * SECOND + minutes * 60 * SECOND + seconds * SECOND + micros * 10
    if nonzero and nonzero[0] < 0:
        ticks = -ticks
    require(-(2 ** 63) <= ticks < 2 ** 63, "interval_CLR_domain")
    return expected_fields(dict(kind="interval", ticks=ticks)), ticks


def same(actual, expected, code):
    """Recursive exact comparison also rejects bool-as-int and absent fields."""
    require(type(actual) is type(expected), code + ":type")
    if type(expected) is dict:
        require(set(actual) == set(expected), code + ":keys")
        for key in expected:
            same(actual[key], expected[key], code + "." + key)
    elif type(expected) is list:
        require(len(actual) == len(expected), code + ":length")
        for a, b in zip(actual, expected):
            same(a, b, code)
    else:
        require(actual == expected, code + ":value")


def interval_leading(packed):
    require(type(packed) is int and packed in (1574,1686),"interval_text_exact_declared_packed")
    return (packed>>4)&15


def parse_interval_text(text, packed, channel):
    leading=interval_leading(packed)
    require(channel in ("value","server"),"interval_text_channel")
    require(type(text) is str,"interval_text_required")
    match=re.fullmatch(r"INTERVAL (-?)'([+-]?)([0-9]{1,9}) ([0-9]{1,2}):([0-9]{1,2}):([0-9]{1,2})\.([0-9]{6})' DAY\(([0-9])\) TO SECOND\(([0-9])\)",text)
    require(match is not None,"interval_full_wrapper_ASCII_syntax")
    outer,sign,day,hour,minute,second,micro,decl_leading,decl_fraction=match.groups()
    require(not outer or not sign,"interval_server_inner_outer_sign_conflict")
    require(int(decl_leading)==leading and int(decl_fraction)==6,"interval_text_qualifier_matches_packed")
    require(1<=len(day)<=leading,"interval_server_day_width")
    if channel=="value":
        require(not outer and len(day)==leading and len(hour)==len(minute)==len(second)==2 and sign!="+","interval_value_canonical_width_sign")
    fields=dict(days=int(day),hours=int(hour),minutes=int(minute),seconds=int(second),microseconds=int(micro),negative=bool(outer) or sign=="-")
    require(fields["days"]<10**leading and fields["hours"]<=23 and fields["minutes"]<=59
            and fields["seconds"]<=59 and fields["microseconds"]<=999999,"interval_text_integer_domain")
    require(not fields["negative"] or any(fields[k] for k in ("days","hours","minutes","seconds","microseconds")),"interval_text_negative_zero")
    return fields


def canonical_interval_text(spec, packed):
    leading=interval_leading(packed)
    require(spec["kind"]=="interval" and spec["ticks"]%10==0,"interval_canonical_exact_microseconds")
    fields=expected_fields(spec)
    require(fields["days"]<10**leading,"interval_canonical_leading_domain")
    return (f'INTERVAL \'{"-" if fields["negative"] else ""}{fields["days"]:0{leading}d} '
            f'{fields["hours"]:02}:{fields["minutes"]:02}:{fields["seconds"]:02}.{fields["microseconds"]:06}\' '
            f'DAY({leading}) TO SECOND(6)')


def parse_server_text(text, kind, packed=None):
    require(type(text) is str, "server_text_required")
    if kind == "interval":
        return parse_interval_text(text,packed,"server")
    datepart = r"([0-9]{4})-([0-9]{2})-([0-9]{2})"
    timepart = r"([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]{1,9}))?"
    if kind == "date":
        match = re.fullmatch(datepart, text)
        require(match is not None, "server_DATE_canonical_text")
        year, month, day = map(int, match.groups())
        hour = minute = second = ns = 0
        offset = None
    else:
        pattern = timepart if kind == "time" else datepart + " " + timepart
        if kind == "tz":
            pattern += r" ([+-])([0-9]{2}):([0-9]{2})"
        match = re.fullmatch(pattern, text)
        require(match is not None, "server_temporal_canonical_text")
        groups = list(match.groups())
        year = month = day = None
        if kind != "time":
            year, month, day = map(int, groups[:3]); groups = groups[3:]
        hour, minute, second = map(int, groups[:3])
        ns = int((groups[3] or "").ljust(9, "0"))
        offset = None
        if kind == "tz":
            offset = (int(groups[5]) * 60 + int(groups[6])) * (-1 if groups[4] == "-" else 1)
    return dict(year=year, month=month, day=day, hour=hour, minute=minute, second=second,
                nanoseconds=ns, offset_minutes=offset)


def public_text(spec, scale):
    fields = expected_fields(spec)
    if spec["kind"] == "interval":
        return canonical_interval_text(spec,scale)
    text = "" if spec["kind"] == "time" else f'{fields["year"]:04}-{fields["month"]:02}-{fields["day"]:02}'
    if spec["kind"] == "date":
        return text
    if text:
        text += " "
    text += f'{fields["hour"]:02}:{fields["minute"]:02}:{fields["second"]:02}'
    if scale:
        text += "." + f'{fields["nanoseconds"]:09}'[:scale]
    if spec["kind"] == "tz":
        offset = spec["offset_minutes"]
        text += f' {"-" if offset < 0 else "+"}{abs(offset)//60:02}:{abs(offset)%60:02}'
    return text


def server_null_projection(observed, null, ordinal, async_required):
    receipt = observed.get("server_null_receipt")
    if not null and receipt is None:
        return  # Sequential non-NULL consumes its server scalar via GetString once.
    require(type(receipt) is dict, "server_NULL_actual_projection_receipt")
    apis = ("IsDBNullAsync",) if async_required else ("IsDBNull", "IsDBNullAsync")
    require(receipt.get("api") in apis, "server_NULL_projection_API")
    same(receipt, dict(actual_getter_invoked=True, api=receipt["api"], ordinal=ordinal,
                       is_null=null, source="server_projection"), "server_NULL_independent_projection")


def observation(observed, spec, ctype, scale, null, *, source, fresh_id=None, async_required=False, server_ordinal=1):
    require(type(observed) is dict and observed.get("source") == source, "observation_source")
    connection_id = observed.get("connection_id")
    require(type(connection_id) is str and re.fullmatch(r"[A-Za-z0-9_-]{1,80}", connection_id), "observation_connection_id")
    if fresh_id is not None and source != "O":
        require(connection_id == fresh_id, "observation_fresh_connection")
    same(observed.get("is_null"), null, "observation_NULL")
    server_null_projection(observed, null, server_ordinal, async_required and source != "O")
    wanted = None if null else expected_fields(spec)
    same(observed.get("fields"), wanted, "observation_fields")
    if source == "server":
        if null:
            require(observed.get("server_text") is None, "server_NULL_text")
        else:
            same(parse_server_text(observed.get("server_text"), spec["kind"],scale if ctype==21 else None), wanted, "independent_server_text")
        return
    # Official is an additional independent oracle on shared, never a W substitute.
    if source == "O":
        if not null:
            same(observed.get("ticks"), spec["ticks"], "official_ticks")
            same(observed.get("offset_minutes"), spec["offset_minutes"], "official_typed_offset")
            require(observed.get("typed_api") == ("GetDateTimeOffset" if spec["kind"] == "tz" else "GetDateTime")
                    and observed.get("typed_outcome") == "returned", "official_typed_API")
            require(observed.get("typed_type") == ("System.DateTimeOffset" if spec["kind"] == "tz" else "System.DateTime")
                    and observed.get("typed_error") is None and observed.get("default_type") == "System.DateTime", "official_actual_getter_mapping")
            if spec["kind"] == "tz":
                error = observed.get("generic_error")
                require(type(error) is dict and error.get("type") == "System.InvalidCastException", "official_generic_characterization")
        return
    typed_type = {"ts": "System.DateTime", "tz": "System.DateTimeOffset", "date": "System.DateOnly",
                  "time": "System.TimeOnly", "interval": "System.TimeSpan"}[spec["kind"]]
    default_type = "System.DateTime" if spec["kind"] in ("date", "time", "ts") else "System.DateTimeOffset" if spec["kind"] == "tz" else "System.TimeSpan" if scale == 1574 else "W.Dm.DmIntervalDT"
    for key in ("field_type", "schema_type"):
        require(observed.get(key) == default_type, "actual_public_mapping:" + key)
    require(observed.get("default_type") == ("System.DBNull" if null else default_type), "actual_default_mapping")
    require(observed.get("provider_type") == ("System.DBNull" if null else default_type), "actual_provider_mapping")
    require(observed.get("typed_type") == typed_type, "actual_generic_mapping")
    same(observed.get("schema_scale"), 6 if ctype == 21 else scale, "actual_schema_scale")
    metadata = observed.get("metadata")
    require(type(metadata) is dict, "actual_result_metadata")
    for key, wanted_metadata in dict(available=True, c_type=ctype, semantic_scale=scale, width=WIDTHS[ctype],
        mask=0, type_flag=0, raw_scale=scale, raw_scale_status="captured_server_source",
        raw_source="ResultColumn", raw_c_type=ctype, raw_precision=WIDTHS[ctype], raw_mask_before=0,
        raw_derived_mask=0, result_format="ordinary_packed", is_bdta=False, metadata_role="result_column").items():
        same(metadata.get(key), wanted_metadata, "actual_result_metadata:" + key)
    cell = observed.get("cell")
    require(type(cell) is dict, "actual_cell_required")
    for key, wanted_cell in dict(observable=True, is_null=null, is_bdta=False,
        result_format="ordinary_non_BDTA", source="already_fetched_cell").items():
        same(cell.get(key), wanted_cell, "actual_cell:" + key)
    apis = ("GetFieldValueAsync<T>",) if async_required else ("GetFieldValue<T>", "GetFieldValueAsync<T>")
    require(observed.get("typed_api") in apis, "actual_generic_API")
    if null:
        require(observed.get("typed_null_error") == "W.Dm.DmException", "typed_NULL_invocation_required")
        error = observed.get("typed_error")
        require(type(error) is dict and error.get("type") == "W.Dm.DmException", "typed_NULL_exact_driver_error")
        same(error.get("number"), 6081, "typed_NULL_exact_number")
        receipt = observed.get("typed_null_receipt")
        same(receipt, dict(actual_getter_invoked=True, has_real_infra_inner=False,
                           cache_is_null=True, getter_api=observed["typed_api"]), "typed_NULL_scoped_receipt")
        require(error.get("failure_kind") in (None, "Unknown", "Transport")
                and error.get("phase") in (None, "Unknown", "Fetch") and error.get("outcome") in (None, "NotSent")
                and error.get("verified_server_response") is False, "typed_NULL_context_closed")
        require(observed.get("ticks") is None and observed.get("raw") is None, "NULL_payload_closed")
        require(cell.get("payload_length") is None and cell.get("payload_sha256") is None, "NULL_cell_payload_closed")
        return
    require(observed.get("typed_error") is None, "typed_success_error_closed")
    same(observed.get("ticks"), spec["ticks"], "actual_generic_ticks")
    same(observed.get("offset_minutes"), spec["offset_minutes"], "actual_offset")
    if spec["kind"] == "ts":
        require(observed.get("kind") == "Unspecified", "actual_DateTime_kind")
    raw = observed.get("raw")
    require(type(raw) is dict, "raw_required")
    for key, expected in dict(c_type=ctype, scale=scale, width=WIDTHS[ctype], mask=0,
        source="ordinary_result_cache_before_temporal_projection", capture_stage="before_any_temporal_getter").items():
        same(raw.get(key), expected, "raw:" + key)
    text = raw.get("hex")
    require(type(text) is str and re.fullmatch(r"(?:[A-Fa-f0-9]{2})+", text), "raw_hex")
    payload = bytes.fromhex(text)
    require(len(payload) == WIDTHS[ctype] and raw.get("payload_sha256") == hashlib.sha256(payload).hexdigest(), "raw_actual_bytes_hash")
    same(cell.get("payload_length"), len(payload), "actual_cached_payload_length")
    same(cell.get("payload_sha256"), raw["payload_sha256"], "actual_cached_payload_hash")
    if ctype == 21:
        fields, ticks = decode_interval(payload, scale)
        same(ticks, spec["ticks"], "independent_interval_ticks")
    else:
        decoder, _ = reviewed_module("tp06_independent_packed", "v11-packed-oracle.py", PACKED_SHA)
        try:
            fields = decoder.decode_packet(payload, ctype, scale)
        except decoder.PacketError:
            raise ContractError("independent_packed_decode_rejected") from None
    same(fields, wanted, "independent_packed_fields")
    if ctype==21:
        same(parse_interval_text(observed.get("get_string"),scale,"value"),wanted,"actual_GetString_interval_fields")
    else:
        same(parse_server_text(observed.get("get_string"),spec["kind"]),wanted,"actual_GetString_fields")
    same(observed.get("get_string"),public_text(spec,scale),"actual_GetString_precision")
    if spec["kind"] in ("date", "time", "ts", "tz"):
        default_ticks = spec["ticks"]
        if spec["kind"] == "time":
            default_ticks += (date(1900, 1, 1).toordinal() - 1) * DAY
        same(observed.get("default_ticks"), default_ticks, "legacy_default_ticks")


def binding_metadata(value, ctype, scale):
    require(type(value) is dict and value.get("available") is True, "binding_unobservable")
    for key, expected in dict(c_type=ctype, semantic_scale=scale, width=WIDTHS[ctype], mask=0, type_flag=2,
        raw_source="ParameterDescribe", raw_scale_status="captured_server_source", raw_mask_before=0, raw_derived_mask=0).items():
        same(value.get(key), expected, "binding:" + key)
    for key in ("raw_c_type", "raw_precision", "raw_scale"):
        integer(value.get(key), "binding:" + key, 0)
    require(value["raw_c_type"] in (2, 14, 15, 16, 23, 26, 27), "binding_raw_CType")
    require(value["raw_c_type"] == 2 or value["raw_scale"] <= 9, "binding_raw_scale_closed")


def parameter(actual, stage_spec, value, case_id):
    require(type(actual) is dict, "parameter_required")
    clr = {"ts": "System.DateTime", "tz": "System.DateTimeOffset", "date": "System.DateOnly", "time": "System.TimeOnly", "interval": "System.TimeSpan"}[value["kind"]]
    requested_null = stage_spec["is_null"] or stage_spec["stage_id"] == "scale7_null" or case_id == "NATIVE_TIME_REJECT_SCALE7_NULL" and stage_spec["stage_id"] == "reject"
    require(actual.get("clr_type") == ("System.DBNull" if requested_null else clr), "parameter_CLR")
    same(actual.get("value_is_dbnull"), requested_null, "parameter_NULL")
    same(actual.get("value_ticks"), None if requested_null else value["ticks"], "parameter_input_ticks")
    for key in ("db_type_explicit", "dm_type_explicit", "scale_explicit"):
        require(type(actual.get(key)) is bool, "parameter_declaration_flags")
    integer(actual.get("scale"), "parameter_scale", 0)
    integer(actual.get("precision"), "parameter_precision", 0)
    if "AUTOREPREPARE" in case_id and stage_spec["stage_id"] in ("scale3", "scale6"):
        same(actual.get("scale"), stage_spec["scale"], "automatic_scale_change")
        same(actual.get("precision"), 0, "automatic_precision_zero")
        require(actual["scale_explicit"] is True, "automatic_scale_explicit")
    if stage_spec["binding"]:
        desired_scale = 0 if stage_spec["stage_id"] == "tls_default_explicit_reject" else stage_spec["scale"]
        same(actual.get("scale"), desired_scale, "binding_actual_scale")
    if requested_null:
        require(actual["db_type_explicit"] or actual["dm_type_explicit"], "NULL_typed_declaration")
    if stage_spec["stage_id"] == "scale7_null" or case_id == "NATIVE_TIME_REJECT_SCALE7_NULL" and stage_spec["stage_id"] == "reject":
        same(actual.get("scale"), 7, "rejection_requested_scale7")
        require(actual["scale_explicit"] is True, "rejection_explicit_scale7")
    if stage_spec["stage_id"] == "tls_default_explicit_reject":
        require(actual.get("declared_db_type") == "DateTime2" and actual["db_type_explicit"]
                and actual["scale_explicit"] is False, "tls_explicit_default_rejection_contract")
    if case_id.startswith("NATIVE_"):
        if value["kind"] == "interval":
            require(actual.get("declared_dm_type") == "IntervalDayToSecond" and actual["dm_type_explicit"], "native_interval_declaration")
        elif case_id.startswith("NATIVE_DATE"):
            require(actual.get("declared_db_type") == "Date" and actual["db_type_explicit"], "native_DATE_declaration")
        elif case_id.startswith("NATIVE_TIME"):
            require(actual.get("declared_db_type") == "Time" and actual["db_type_explicit"], "native_TIME_declaration")
    if "CLONE" in case_id:
        phase = stage_spec["stage_id"]
        if phase.startswith("clone"):
            require(actual.get("declared_db_type") == "DateTime2" and actual.get("declared_dm_type") == "DateTime"
                    and actual["db_type_explicit"] and actual["dm_type_explicit"], "clone_dual_explicit_intent")
        if phase == "reset":
            require(actual["db_type_explicit"] is False and actual["dm_type_explicit"] is False, "reset_explicit_intent_removed")


def parameter_actions(case_id, stage_id):
    if "REUSE_NO_EXPLICIT" in case_id:
        return ["CreateParameter", "Value", "DbType", "Scale"] if stage_id == "second" else ["Value"]
    if "AUTOREPREPARE" in case_id:
        return {"control": ["CreateParameter", "Value", "DbType", "Scale"],
                "scale3": ["Value", "Scale", "Precision"], "scale6": ["Value", "Scale"]}[stage_id]
    return {"clone_control": ["CreateParameter", "Value", "DbType", "Scale", "DmSqlType", "Clone", "AttachClone"],
        "clone_after_original_mutation": ["Original.Value", "Original.Scale"], "reset": ["ResetDbType", "Value"],
        "reopen": ["OpenNewConnection", "Command.Connection", "Value"],
        "tls_default_explicit_reject": ["CreateParameter", "DbType", "DmSqlType", "Value", "ReplaceParameter"]}[stage_id]


def protocol_frames(value, allowed, code):
    require(type(value) is list, code + ":frames_required")
    for frame in value:
        require(type(frame) is dict and set(frame) == {"stage", "request_opcode", "response_opcode", "sql_code", "body_length"}, code + ":frame_shape")
        require(frame["stage"] == "NATIVE_INTERVAL_REJECT_leading", code + ":case_association")
        require(integer(frame["request_opcode"], code + ":request_opcode", 0) in allowed, code + ":request_scope")
        integer(frame["response_opcode"], code + ":response_opcode", 0)
        integer(frame["sql_code"], code + ":sql_code")
        integer(frame["body_length"], code + ":body_length", 0)
    return value


def response_receipt(frame):
    return dict(source="physical_channel_receive_opcode", **{key: frame[key] for key in
        ("request_opcode", "response_opcode", "sql_code", "body_length")})


def prepared_protocol(actual, expected_business):
    frames = protocol_frames(actual.get("execution_frames"), (3, 5, 6, 13), "prepared_target")
    allocations = sum(frame["request_opcode"] == 3 for frame in frames)
    prepares = sum(frame["request_opcode"] == 5 for frame in frames)
    business = [frame for frame in frames if frame["request_opcode"] in (6, 13)]
    same(actual.get("allocation_count"), allocations, "leading_actual_allocation_count")
    same(actual.get("prepare_or_direct_count"), prepares, "leading_actual_opcode5_count")
    same(actual.get("reprepare_count"), prepares, "leading_reprepare_from_opcode5")
    require(allocations == 0 and prepares == 0, "leading_pre_execute_source_invalidated")
    same(actual.get("business_frames"), business, "leading_actual_business_response_frames")
    same(actual.get("business_response_count"), len(business), "leading_actual_business_response_summary")
    require(actual.get("business_response_source") == "physical_channel_receive_opcode", "leading_response_origin")
    same(len(business), expected_business, "leading_expected_business_responses")
    same(actual.get("server_response"), response_receipt(business[-1]) if business else None, "leading_target_response_association")
    sends = actual.get("business_sent_frames")
    require(type(sends) is list and len(sends) == expected_business, "leading_actual_sent_frames")
    for sent in sends:
        require(type(sent) is dict and set(sent) == {"stage", "request_opcode", "session_id", "lease_generation", "execution_id", "invocation_id"}, "leading_actual_send_frame_shape")
        require(sent["stage"] == "NATIVE_INTERVAL_REJECT_leading", "leading_send_case_association")
        require(integer(sent["request_opcode"], "leading_sent_request", 0) in (6, 13), "leading_prepared_business_send")
        for key in ("session_id", "lease_generation", "execution_id", "invocation_id"):
            integer(sent[key], "leading_sent_identity:" + key, 0)
    same([sent["request_opcode"] for sent in sends], [frame["request_opcode"] for frame in business], "leading_send_response_operation_match")
    same(actual.get("business_execute_count"), len(sends), "leading_business_execute_send_summary")
    same(actual.get("business_send_count"), len(sends), "leading_business_send_summary")
    return frames


def literal_protocol(actual):
    frames = protocol_frames(actual.get("execution_frames"), (3, 5), "literal_control")
    allocations = sum(frame["request_opcode"] == 3 for frame in frames)
    direct = [frame for frame in frames if frame["request_opcode"] == 5]
    same(actual.get("allocation_count"), allocations, "literal_allocation_summary")
    same(actual.get("prepare_or_direct_count"), len(direct), "literal_direct_summary")
    require(len(direct) == 1, "literal_actual_direct_opcode5_required")
    same(actual.get("business_frames"), direct, "literal_business_frames")
    same(actual.get("business_response_count"), 1, "literal_response_summary")
    # Source is checked in the exact wire receipt below, rather than a duplicate summary label.
    same(actual.get("server_response"), response_receipt(direct[0]), "literal_direct_response_association")
    return direct[0]


def sent_window(value, case_id, code):
    require(type(value) is list, code + ":actual_frames_required")
    for frame in value:
        require(type(frame) is dict and set(frame)=={"stage","request_opcode","session_id","lease_generation","execution_id","invocation_id"}, code + ":frame_shape")
        same(frame["stage"],case_id,code + ":case_identity")
        require(integer(frame["request_opcode"],code + ":opcode",0) in (3,4,5,6,13),code + ":write_scope_opcode")
        for key in ("session_id","lease_generation","execution_id","invocation_id"):
            integer(frame[key],code + ":" + key,0)
    return value


def local_write_recovery(actual, expected, case_id):
    target=actual.get("target_receipt");require(type(target) is dict,"local_target_receipt_required")
    op=actual["operation_connection_id"]
    for key,value in dict(attempted=True,status="not_returned",api=expected["api"],connection_id=op,
        parameter_count=1,state_before="Open",execute_returned=False,send_observable=True,send_source="physical_channel_frame_sent").items():
        same(target.get(key),value,"local_target:"+key)
    require(expected["api"] in ("ExecuteNonQueryAsync","ExecuteReaderAsync"),"local_target_real_async_API")
    after=target.get("state_after");require(after in ("Open","Broken","Closed"),"local_target_public_state")
    frames=sent_window(target.get("sent_frames"),case_id,"local_target_send")
    same(target.get("all_send_count"),len(frames),"local_target_all_send_summary")
    same(target.get("allocation_count"),sum(f["request_opcode"]==3 for f in frames),"local_target_allocation_summary")
    same(target.get("prepare_or_direct_count"),sum(f["request_opcode"]==5 for f in frames),"local_target_prepare_summary")
    business=sum(f["request_opcode"] in (6,13) for f in frames)
    same(target.get("business_send_count"),business,"local_target_business_summary")
    require(business==0,"local_target_business_was_sent")
    if "diagnostic_business_send" in actual:
        same(actual["diagnostic_business_send"],actual["error"].get("outcome"),"local_failure_outcome_preserved")
    recovery=actual.get("recovery");require(type(recovery) is dict,"local_recovery_attempt_required")
    require(recovery.get("failed_step") is None,"local_recovery_no_failed_substep")
    retired=after in ("Broken","Closed")
    for key,value in dict(attempted=True,completed=True,scope="local_write_rejection",operation_connection_id=op,
        state_before=after,path="retired_connection" if retired else "same_open",
        state_after="Closed" if retired else "Open",last_attempted_step="fresh_connection_dispose",
        last_completed_step="fresh_connection_dispose").items():
        same(recovery.get(key),value,"local_recovery:"+key)
    ids=["old_connection_probe"]+(["old_command_dispose","old_connection_dispose"] if retired else [])+[
        "fresh_connection_open","fresh_connection_probe","fresh_row_verify","fresh_connection_dispose"]
    steps=recovery.get("steps")
    require(type(steps) is list and len(steps)==len(ids),"local_recovery_steps_complete")
    for entry,name in zip(steps,ids):
        require(type(entry) is dict,"local_recovery_step_object")
        same(entry.get("step_id"),name,"local_recovery_step_order")
        same(entry.get("status"),"completed","local_recovery_step_completion")
    index={step["step_id"]:step for step in steps}
    probe=index["old_connection_probe"]
    for key,value in dict(connection_id=op,api="ExecuteScalarAsync",state_before=after,state_after=after,
        send_source="physical_channel_frame_sent",execute_returned=not retired).items():
        same(probe.get(key),value,"local_old_probe:"+key)
    old_frames=sent_window(probe.get("sent_frames"),case_id,"local_old_probe_send")
    same(probe.get("all_send_count"),len(old_frames),"local_old_probe_all_send_summary")
    if retired:
        require(not old_frames,"retired_old_probe_sent")
        require(probe.get("scalar") is None,"retired_old_probe_returned_value")
        error=probe.get("error")
        require(type(error) is dict and error.get("type")=="System.InvalidOperationException"
            and error.get("failure_class")=="user_rejection" and error.get("number") is None
            and error.get("verified_server_response") is False and error.get("failure_kind") in (None,"Unknown")
            and error.get("phase") in (None,"Unknown") and error.get("outcome") in (None,"NotSent")
            and error.get("code") is None,"retired_old_probe_exact_rejection")
        require(recovery.get("same_connection_id") is None and recovery.get("scalar") is None,"retired_not_same_connection_success")
        command=index["old_command_dispose"];connection=index["old_connection_dispose"]
        for entry in (command,connection):
            same(entry.get("connection_id"),op,"retired_dispose_original_ID")
            same(entry.get("api"),"DisposeAsync","retired_dispose_API")
        same(command.get("state_before"),after,"retired_command_dispose_state")
        require(command.get("state_after") in (after,"Closed"),"retired_command_no_reopen")
        same(connection.get("state_before"),command["state_after"],"retired_dispose_state_association")
        same(connection.get("state_after"),"Closed","retired_connection_closed")
    else:
        same(probe.get("scalar"),1,"open_original_probe_scalar")
        require(probe.get("error") is None,"open_original_probe_success")
        same(recovery.get("same_connection_id"),op,"open_original_ID")
        same(recovery.get("scalar"),1,"open_original_scalar")
        same(recovery.get("api"),"ExecuteScalarAsync","open_original_API")
    fresh=index["fresh_connection_open"];fresh_id=fresh.get("connection_id")
    require(type(fresh_id) is str and fresh_id and fresh_id!=op,"local_recovery_new_ID")
    for key,value in dict(api="OpenAsync",identity_user="WDM_PROVIDER_TEST",identity_schema="WDM_PROVIDER_TEST",state_after="Open").items():
        same(fresh.get(key),value,"local_fresh_identity:"+key)
    same(recovery.get("fresh_connection_id"),fresh_id,"local_recovery_fresh_ID")
    same(actual.get("fresh_connection_id"),fresh_id,"local_fresh_oracle_ID")
    same(actual.get("fresh_rows"),1,"local_fresh_exact_rows")
    same(recovery.get("fresh_scalar"),1,"local_fresh_query_scalar")
    for name,api in (("fresh_connection_probe","ExecuteScalarAsync"),("fresh_row_verify","ExecuteReaderAsync"),("fresh_connection_dispose","DisposeAsync")):
        same(index[name].get("connection_id"),fresh_id,"local_fresh_step_ID")
        same(index[name].get("api"),api,"local_fresh_step_API")
        require(index[name].get("error") is None,"local_fresh_step_success")
    same(index["fresh_connection_probe"].get("scalar"),1,"local_fresh_probe_actual_scalar")
    return fresh_id


def leading_source(actual, *, negative=True):
    """Only the two approved live sources; no assumed server number allowlist."""
    metadata = actual.get("parameter_metadata")
    binding = actual.get("binding")
    require(type(metadata) is dict, "leading_source_unobservable")
    for key, value in dict(available=True, c_type=21, width=24, mask=0, raw_source="ParameterDescribe",
        raw_scale_status="captured_server_source", raw_c_type=21, raw_precision=24,
        raw_mask_before=0, raw_derived_mask=0, metadata_role="parameter_describe").items():
        same(metadata.get(key), value, "leading_actual_source:" + key)
    fixed = metadata.get("type_flag") == 1 and metadata.get("raw_scale") == 1574
    advisory = metadata.get("type_flag") == 2 and metadata.get("raw_scale") == 0
    require(type(metadata.get("type_flag")) is int and type(metadata.get("raw_scale")) is int
            and (fixed or advisory), "leading_unapproved_describe")
    packed = 1574 if fixed else 1686
    same(metadata.get("semantic_scale"), 1574 if fixed else 0, "leading_pre_execute_raw_scale")
    require(actual.get("parameter_source_connection_id") == actual.get("operation_connection_id"), "leading_source_physical_identity")
    source_statement = integer(actual.get("parameter_source_statement_id"), "leading_source_statement", 0)
    require(actual.get("parameter_source_capture_stage") == "after_prepare_before_target_execute", "leading_pre_execute_source_stage")
    same(actual.get("reprepare_count"), 0, "leading_no_unrecorded_reprepare")
    same(actual.get("current_no_reprepare"), True, "leading_no_reprepare_receipt")
    during = actual.get("statement_ids_during_execution")
    require(type(during) is list and all(type(s) is int and s == source_statement for s in during), "leading_current_statement_association")
    if fixed and negative:
        same(actual.get("current_binding_observed"), False, "leading_failed_binding_unpublished")
        require(actual.get("current_binding") is None and binding is None, "leading_control_not_current_binding")
        require(actual.get("constraint_evidence") == "pre_execute_parameter_describe_plus_control", "leading_fixed_constraint_evidence")
        require(actual.get("binding_owner") == "unobservable" and actual.get("binding_statement_id") is None, "leading_released_owner_not_reused")
    else:
        same(binding, dict(observable=True, source="ParameterDescribe", reliable_fixed=fixed,
            subtype=6, leading_precision=2 if fixed else 9, fractional_scale=6, packed_scale=packed), "leading_immutable_binding")
        require(actual.get("binding_owner") == "command", "leading_binding_legal_owner")
        same(actual.get("binding_statement_id"), source_statement, "leading_response_statement_association")
        if negative:
            same(actual.get("current_binding_observed"), True, "leading_advisory_published_binding")
            same(actual.get("current_binding"), binding, "leading_advisory_current_binding")
    require(actual.get("binding_capture_stage") == ("after_target_failure" if fixed and negative
                else "business_response_before_legacy_decode"), "leading_binding_capture_stage")
    same(actual.get("business_execute_count"), 0 if fixed and negative else 1, "leading_actual_business_execute")
    require(actual.get("business_send_source") == "physical_channel_frame_sent", "leading_actual_send_source")
    same(actual.get("business_send_count"), 0 if fixed and negative else 1, "leading_actual_send_count")
    same(actual.get("business_response_count"), 0 if fixed and negative else 1, "leading_actual_response_count")
    frames = prepared_protocol(actual, 0 if fixed and negative else 1)
    require(len(during) == len(frames), "leading_every_target_frame_statement_associated")
    require(len(during) == actual["business_response_count"], "leading_response_statement_receipts")
    for key in ("column_before", "column_after"):
        column = actual.get(key)
        require(type(column) is dict, "leading_result_column_required")
        for column_key, value in dict(available=True,c_type=21,semantic_scale=1574,width=24,mask=0,type_flag=0,
            raw_c_type=21,raw_precision=24,raw_scale=1574,raw_source="ResultColumn",raw_scale_status="captured_server_source",
            raw_mask_before=0,raw_derived_mask=0,metadata_role="result_column").items():
            same(column.get(column_key), value, "leading_COLUMN_source:" + column_key)
    return "fixed" if fixed else "advisory"


def verified_leading_server_error(error, response, request_opcodes=(6, 13)):
    require(type(error) is dict and error.get("type") == "W.Dm.DmException"
            and error.get("failure_class") == "verified_server_response"
            and error.get("verified_server_response") is True and error.get("outcome") == "ServerReported"
            and error.get("failure_kind") == "Server" and error.get("phase") in ("Send", "Receive"), "leading_verified_server_response")
    number = integer(error.get("number"), "leading_actual_server_number")
    require(type(response) is dict and response.get("source") == "physical_channel_receive_opcode", "leading_actual_wire_response")
    same(response.get("sql_code"), number, "leading_wire_error_number")
    require(integer(response.get("request_opcode"), "leading_wire_request", 0) in request_opcodes, "leading_wire_operation")
    integer(response.get("response_opcode"), "leading_wire_response_opcode", 0)
    integer(response.get("body_length"), "leading_wire_body", 1)
    return number


def leading_controls(actual):
    controls = actual.get("server_controls")
    require(type(controls) is list and len(controls) == 3, "leading_independent_server_controls")
    expected = (("99_day2", 99, 1574), ("100_day9", 100, 1686), ("100_day2", None, 1574))
    ids = set()
    for control, (name, days, packed) in zip(controls, expected):
        require(type(control) is dict and control.get("control_id") == name
            and control.get("api") == "ExecuteNonQuery" and control.get("parameter_count") == 0, "leading_server_only_control")
        row_id = integer(control.get("row_id"), "leading_control_row")
        require(row_id not in ids, "leading_control_unique_row")
        ids.add(row_id)
        op, fresh = control.get("operation_connection_id"), control.get("fresh_connection_id")
        require(type(op) is str and op and type(fresh) is str and fresh and fresh != op, "leading_control_fresh_connection")
        same(control.get("column_packed_scale"), packed, "leading_control_column")
        direct = literal_protocol(control)
        if days is None:
            require(control.get("status") == "rejected", "leading_server_DAY2_negative_control")
            verified_leading_server_error(control.get("error"), control.get("server_response"), (5,))
            same(control.get("fresh_rows"), 0, "leading_server_negative_no_row")
        else:
            same(direct["sql_code"], 0, "literal_positive_server_success")
            require(control.get("status") == "observed" and control.get("error") is None, "leading_server_positive_control")
            same(control.get("affected_rows"), 1, "leading_server_positive_affected")
            same(control.get("fresh_rows"), 1, "leading_server_positive_fresh")
            observations = control.get("observations")
            require(type(observations) is list and len(observations) == 2
                    and {o.get("source") for o in observations} == {"W", "server"}, "leading_control_independent_oracles")
            vector = dict(kind="interval", ticks=days*DAY, offset_minutes=None)
            for obs in observations:
                same(obs.get("row_id"), row_id, "leading_control_exact_row")
                observation(obs, vector, 21, packed, False, source=obs["source"], fresh_id=fresh)


def binding_read_context(profile, physical, cases_source_sha, design_sha, root_decision, tls_design_sha=None, tls_root_decision=None):
    require(profile in ("shared", "tls"), "binding_read_profile")
    require(cases_source_sha == BIND_SQL_SOURCE_SHA, "binding_read_SQL_source_changed")
    require(design_sha == RESULT_DESCRIPTOR_SHA and root_decision == READ_ROOT_DECISION,
            "binding_read_root_decision_missing")
    if tls_design_sha is not None or tls_root_decision is not None:
        require(tls_design_sha==TLS_READ6_SHA and tls_root_decision==TLS_READ6_ROOT_DECISION,"TLS_read6_root_decision_missing")
    require(type(physical) is dict, "binding_read_physical_required")
    version, encoding, message = {"shared": ("8.1.5.60", "utf-8", 21), "tls": ("8.1.4.6", "gb18030", 11)}[profile]
    for key, expected in dict(server_version=version,encoding=encoding,negotiated_encoding=encoding,
        message_version=message,encoding_override_empty=True,result_format="ordinary_non_BDTA",
        server_user="WDM_PROVIDER_TEST",server_schema="WDM_PROVIDER_TEST",
        profile_origin="physical_get_conn_instance_conn_property",
        negotiated_encoding_origin="physical_conn_property_serverEncoding_field").items():
        same(physical.get(key), expected, "binding_read_physical:" + key)
    require(-1440 <= integer(physical.get("timezone"), "binding_read_timezone") <= 1440, "binding_read_timezone_domain")
    return dict(profile=profile, physical=dict(physical), cases_source_sha256=cases_source_sha,
                design_sha256=design_sha, root_decision_id=root_decision,
                tls_design_sha256=tls_design_sha,tls_root_decision_id=tls_root_decision)


def read_tuple(ctype, scale, width):
    return dict(c_type=ctype, scale=scale, width=width)


def select_binding_read_contract(profile, expected, metadata, context, case_id):
    """Finite approved alternatives, selected from this reader; write expectations stay frozen."""
    require(expected["binding"] is True and expected["exception"] is None, "binding_read_success_only")
    require(type(context) is dict and context.get("profile") == profile, "binding_read_context_profile")
    binding_read_context(profile, context.get("physical"), context.get("cases_source_sha256"),
                         context.get("design_sha256"), context.get("root_decision_id"),
                         context.get("tls_design_sha256"),context.get("tls_root_decision_id"))
    write = read_tuple(expected["c_type"], expected["scale"], WIDTHS[expected["c_type"]])
    require((write["c_type"], write["scale"]) in ((16,3),(16,6),(23,3),(23,6),(26,7),(27,7)), "binding_read_write_scope")
    alternatives = [dict(read_tuple=write, rule_id="g2_write_read_equal_v1", policy_status="design_allowed")]
    if profile == "shared" and expected["c_type"] in (16,23) and expected["scale"] in (3,6):
        extended = 26 if expected["c_type"] == 16 else 27
        alternatives.append(dict(read_tuple=read_tuple(extended,7,WIDTHS[extended]),
            rule_id="g2_shared_same_family_retained7_v1", policy_status="design_allowed"))
    tls_cases={"BIND_METADATA_AUTOREPREPARE_dt":16,"BIND_METADATA_AUTOREPREPARE_dto":23}
    if (profile=="tls" and context.get("tls_design_sha256")==TLS_READ6_SHA
        and context.get("tls_root_decision_id")==TLS_READ6_ROOT_DECISION and case_id in tls_cases
        and expected["stage_id"]=="scale3" and expected["scale"]==3 and expected["c_type"]==tls_cases[case_id]):
        alternatives.append(dict(read_tuple=read_tuple(expected["c_type"],6,WIDTHS[expected["c_type"]]),
            rule_id="g2_tls_autoreprepare_scale3_same_family_read6_v1",policy_status="design_allowed"))
    require(type(metadata) is dict, "binding_read_result_metadata")
    for key, wanted in dict(available=True,type_flag=0,raw_source="ResultColumn",metadata_role="result_column",
        raw_scale_status="captured_server_source",mask=0,raw_mask_before=0,raw_derived_mask=0,
        result_format="ordinary_packed",is_bdta=False).items():
        same(metadata.get(key), wanted, "binding_read_result_provenance:" + key)
    actual = read_tuple(integer(metadata.get("c_type"), "binding_read_CType", 0),
        integer(metadata.get("semantic_scale"), "binding_read_scale", 0), integer(metadata.get("width"), "binding_read_width", 0))
    selected = next((rule for rule in alternatives if rule["read_tuple"] == actual), None)
    if selected is None:
        raise UnverifiedResultDescriptor(case_id, expected["stage_id"], write, actual)
    return dict(write_tuple=write, read_tuple=actual, rule_id=selected["rule_id"], policy_status="design_allowed",
                unobserved_alternatives=[dict(rule,runtime_status="runtime_pending") for rule in alternatives if rule is not selected])


def validate_stage(actual, expected, case_id, profile, *, read_context=None):
    require(type(actual) is dict and actual.get("stage_id") == expected["stage_id"], "stage_identity")
    same(actual.get("api"), expected["api"], "stage_API")
    same(actual.get("prepare_calls"), expected["prepare_calls"], "stage_prepare_calls")
    op_id = actual.get("operation_connection_id")
    require(type(op_id) is str and bool(op_id), "operation_connection_id")
    vals = manifest()["vectors"]
    spec, requested = vals[expected["vector"]], vals[expected["input_vector"]]
    parameter(actual.get("parameter"), expected, requested, case_id)
    if expected["binding"]:
        same(actual.get("parameter_actions"), parameter_actions(case_id, expected["stage_id"]), "binding_actual_setter_actions")
        require(type(actual.get("parameter_id")) is str and actual["parameter_id"]
                and type(actual.get("command_id")) is str and actual["command_id"], "binding_object_identity")
    if expected["affected_rows"] is not None:
        same(actual.get("affected_rows"), expected["affected_rows"], "affected_rows")
    calls = actual.get("actual_receipts")
    require(type(calls) is list and all(type(c) is str for c in calls), "actual_API_receipts")
    require(expected["api"] in calls, "actual_execute_receipt")
    require(sum(c in ("Prepare", "PrepareAsync") for c in calls) == expected["prepare_calls"], "actual_prepare_receipt")
    if expected["prepare_calls"]:
        require(calls.count("PrepareAsync" if expected["api"].endswith("Async") else "Prepare") == 1, "actual_prepare_API")
    if expected["transaction"]:
        require(calls.count("BeginTransaction") + calls.count("BeginTransactionAsync") == 1
            and calls.count(expected["transaction"]) == 1, "actual_transaction_receipt")
        require(("RollbackAsync" not in calls) if expected["transaction"] == "CommitAsync" else ("CommitAsync" not in calls), "transaction_conflict")
    leading_branch = None
    if case_id == "NATIVE_INTERVAL_REJECT_leading":
        same(actual.get("parameter_actions"), ["Value"] if expected["stage_id"] == "reject"
             else ["CreateParameter", "Value", "DmSqlType", "Scale"], "leading_same_declaration_only_value_change")
        integer(actual["parameter"].get("precision"), "leading_parameter_precision")
        same(actual["parameter"].get("precision"), 0, "leading_no_precision_override")
        same(actual["parameter"].get("scale"), 6, "leading_parameter_fraction6")
        branch = leading_source(actual, negative=expected["stage_id"] == "reject")
        if expected["stage_id"] == "reject":
            leading_branch = branch
    if leading_branch == "advisory":
        require(actual.get("status") == "server_leading_response_pending_review", "leading_unapproved_response_pending")
        verified_leading_server_error(actual.get("error"), actual.get("server_response"))
        leading_controls(actual)
        recovery = actual.get("recovery")
        require(type(recovery) is dict and recovery.get("same_connection_id") == op_id, "leading_same_connection_recovery")
        same(recovery.get("scalar"), 1, "leading_recovery_QUERY")
        require(recovery.get("scope")=="verified_server_response" and recovery.get("path")=="same_open"
                and recovery.get("state_before")==recovery.get("state_after")=="Open","verified_server_no_write_retirement_exemption")
    elif expected["exception"]:
        require(actual.get("status") == "rejected", "required_rejection")
        error = actual.get("error")
        require(type(error) is dict and error.get("type") == expected["exception"] and error.get("failure_class") == "user_rejection", "exact_user_rejection")
        require(error.get("number") is None, "user_rejection_not_server_number")
        require(error.get("failure_kind") in (None, "Unknown") and error.get("phase") in (None, "Prepare", "Unknown")
                and error.get("outcome") in (None, "NotSent") and error.get("verified_server_response") in (None, False), "reject_infra_closed")
        require(error.get("code") is None or error["code"] == "WDM_TEMPORAL_REJECT", "reject_code_closed")
        local_write_recovery(actual,expected,case_id)
    else:
        require(actual.get("status") == "observed" and actual.get("error") is None, "required_success")
    if expected["binding"] and not expected["exception"]:
        binding_metadata(actual.get("parameter_metadata"), expected["c_type"], expected["scale"])
        same(actual.get("encoded_scale"), expected["scale"], "actual_encoded_scale")
        require(actual.get("statement_owner") == "reader", "binding_legal_owner")
        integer(actual.get("statement_id"), "binding_statement_id", 0)
    observations = actual.get("observations")
    require(type(observations) is list, "observations_required")
    sources = [item.get("source") for item in observations if type(item) is dict]
    mandatory = {"W", "server"} if not expected["exception"] or expected["fresh"] or expected["binding"] else set()
    require(len(sources) == len(observations) == len(set(sources)) and mandatory <= set(sources)
            and set(sources) <= ({"W", "server", "O"} if profile == "shared" else {"W", "server"}), "oracle_lanes")
    fresh_id = None
    if expected["fresh"]:
        fresh_id = actual.get("fresh_connection_id")
        require(type(fresh_id) is str and fresh_id and fresh_id != op_id, "fresh_independent_connection")
        same(actual.get("fresh_rows"), 1, "fresh_exact_row_count")
    elif expected["exception"] and leading_branch != "advisory":
        fresh_id=actual["recovery"]["fresh_connection_id"]
    selected_read = None
    if expected["binding"] and not expected["exception"]:
        w_observation = next(item for item in observations if item["source"] == "W")
        selected_read = select_binding_read_contract(profile, expected, w_observation.get("metadata"), read_context, case_id)
    if expected["exception"] and leading_branch != "advisory":
        w_observation=next(item for item in observations if item["source"]=="W")
        same(actual.get("fresh_column_metadata"),w_observation.get("metadata"),"local_fresh_schema_actual_result_source")
    for observed in observations:
        same(observed.get("row_id"), 0 if expected["binding"] else integer(actual.get("row_id"), "operation_row_id"), "operation_oracle_same_row")
        if observed["source"] == "O":
            require(observed.get("connection_id") != op_id, "official_independent_connection")
        if selected_read is not None and observed["source"] in ("W", "server"):
            same(observed.get("connection_id"), op_id, "binding_read_same_actual_connection")
        descriptor = selected_read["read_tuple"] if selected_read else read_tuple(expected["c_type"], expected["scale"], WIDTHS[expected["c_type"]])
        observation(observed, spec, descriptor["c_type"], descriptor["scale"], expected["is_null"],
                    source=observed["source"], fresh_id=fresh_id,
                    async_required=expected["api"].endswith("Async"))
    if leading_branch == "advisory":
        # No root/Astra-approved error number exists in this frozen policy. Never accept by sign/type.
        raise ContractError("server_leading_response_pending_review")
    return selected_read


def validate_reader(actual, profile, mode):
    stages = actual.get("stages")
    require(type(stages) is list and len(stages) == 1, "reader_stage_required")
    value = stages[0]
    require(value.get("stage_id") == ("fetch" if mode == "fetch" else "reader")
            and value.get("api") == "ExecuteReaderAsync" and value.get("status") == "observed", "reader_actual_API")
    same(value.get("prepare_calls"), 0, "reader_direct")
    receipt = value.get("receipt")
    require(type(receipt) is dict, "reader_receipt")
    require(receipt.get("disposed_async") is True, "reader_dispose_async")
    same(receipt.get("scalar_after_dispose"), 1, "reader_after_dispose_query")
    require(receipt.get("same_connection_id") == value.get("operation_connection_id")
            and type(value.get("operation_connection_id")) is str and value["operation_connection_id"], "reader_same_connection")
    observations = value.get("observations")
    require(type(observations) is list, "reader_observations")
    scale = 7 if profile == "shared" else 6
    vals = vectors()
    if mode == "fetch":
        same(receipt.get("row_count"), 128, "fetch_row_count")
        require(integer(receipt.get("fetch_count"), "fetch_count", 1) <= 128, "fetch_bounded")
        require(type(receipt.get("fetch_opcode")) is int and receipt["fetch_opcode"] == 7
                and receipt.get("fetch_source") == "physical_channel_receive_opcode", "real_fetch_receipt")
        require(receipt.get("buf_prefetch") == "default" and receipt.get("is_bdta") is False, "ordinary_fetch_scope")
        boundaries = receipt.get("fetch_boundaries")
        require(type(boundaries) is list and len(boundaries) == receipt["fetch_count"]
                and all(type(n) is int and 1 <= n < 128 for n in boundaries)
                and boundaries == sorted(set(boundaries)), "fetch_boundaries")
        require(len(observations) == 128, "fetch_all_rows_required")
        seen = set()
        for obs in observations:
            ordinal = integer(obs.get("row_id"), "fetch_row_id", 1)
            require(ordinal <= 128 and ordinal not in seen, "fetch_duplicate_row")
            seen.add(ordinal)
            observation(obs, vals["ts_tick" if scale == 7 else "ts_micro"], 26 if scale == 7 else 16,
                        scale, False, source="W", async_required=True)
        return
    same(receipt.get("read_async_calls"), 8, "reader_actual_async_reads")
    same(receipt.get("next_result_async_calls"), 2, "reader_actual_nextresult")
    same(receipt.get("rowset_count"), 2, "reader_two_real_rowsets")
    require(integer(receipt.get("more_result_count"), "actual_nextresult_wire_count", 1) <= 4, "actual_nextresult_wire_bounded")
    same(receipt.get("more_result_opcode"), 44, "actual_nextresult_wire_opcode")
    same(receipt.get("cached_repeat_count"), 2 if mode == "default" else 0, "reader_cached_getters")
    same(receipt.get("getter_mismatch"), dict(type="System.InvalidCastException", next_column_read=True, next_row_read=True), "reader_mismatch_recovery")
    require(len(observations) == 6, "reader_all_rows_required")
    seen = set()
    for obs in observations:
        key = (integer(obs.get("rowset_index"), "reader_rowset", 0), integer(obs.get("row_id"), "reader_row_id"))
        require(key[0] in (0, 1) and key[1] in (9001, 9002, 9003) and key not in seen, "reader_duplicate_or_relabelled")
        seen.add(key)
        kind = "ts" if key[0] == 0 else "tz"
        vector = kind + ("_tick" if key[1] == 9002 and profile == "shared" else "_micro")
        ct = (26 if kind == "ts" else 27) if scale == 7 else (16 if kind == "ts" else 23)
        observation(obs, vals[vector], ct, scale, key[1] == 9003, source="W", async_required=True,
                    server_ordinal=6 if mode == "sequential" else 1)
        if key == (0, 9001):
            require(obs.get("getter_mismatch_error") == "System.InvalidCastException"
                    and obs.get("next_column_read") is True, "reader_actual_getter_mismatch_receipt")
        if mode == "default" and key[1] == 9001:
            repeated = obs.get("cached_repeat")
            require(type(repeated) is dict and repeated.get("outcome") == "returned", "reader_actual_cached_repeat")
            require(repeated.get("type") == ("System.DateTime" if kind == "ts" else "System.DateTimeOffset"), "reader_cached_generic_type")
            same(repeated.get("ticks"), vals[vector]["ticks"], "reader_cached_generic_ticks")
            same(repeated.get("offset_minutes"), vals[vector]["offset_minutes"], "reader_cached_generic_offset")
        if mode == "sequential":
            aliases = dict(mismatch=0, is_dbnull=1, default=2, provider=3, typed=4, string=5, server=6)
            same(obs.get("sequential_aliases"), aliases, "sequential_distinct_getter_projections")
            calls = [1, 2, 3, 4, 6] if key[1] == 9003 else [1, 2, 3, 4, 5, 6]
            if key == (0, 9001):
                calls = [0] + calls
            same(obs.get("sequential_getter_ordinals"), calls, "sequential_actual_getter_ordinals")
            same(obs.get("raw_alias_ordinal"), 4, "sequential_raw_alias")


def validate_report(report, profile, *, expected_only=False, read_context=None, binding_read_results=None):
    require(type(report) is dict and report.get("status") == "observed_complete", "report_not_complete")
    same(report.get("expected_only"), expected_only, "report_expected_only")
    same(report.get("database_accessed"), not expected_only, "report_database_access")
    if read_context is None:
        require(expected_only, "binding_read_verified_artifact_context_required")
        # Synthetic physical facts remain explicitly expected-only; actual runs supply frozen-source context.
        read_context = binding_read_context(profile, report.get("W_profile"), BIND_SQL_SOURCE_SHA,
                                            RESULT_DESCRIPTOR_SHA, READ_ROOT_DECISION,TLS_READ6_SHA,TLS_READ6_ROOT_DECISION)
    require(type(read_context) is dict, "binding_read_context_required")
    same(report.get("W_profile"), read_context.get("physical"), "binding_read_report_physical_association")
    require(binding_read_results is None or type(binding_read_results) is list, "binding_read_results_sink")
    verified_binding_results = []
    cases = report.get("cases")
    wanted = case_specs(profile)
    require(type(cases) is list and len(cases) == len(wanted), "required_case_count")
    index = {}
    for case in cases:
        require(type(case) is dict and type(case.get("case_id")) is str and case["case_id"] not in index, "duplicate_case")
        require(case.get("status") == "observed", "case_skip_or_failure_closed")
        index[case["case_id"]] = case
    require(set(index) == {c["case_id"] for c in wanted}, "required_case_set")
    for contract in wanted:
        actual = index[contract["case_id"]]
        if "reader_mode" in contract:
            validate_reader(actual, profile, contract["reader_mode"])
            continue
        stages = actual.get("stages")
        require(type(stages) is list and len(stages) == len(contract["stages"]), "required_stage_count")
        for value, expectation in zip(stages, contract["stages"]):
            selected = validate_stage(value, expectation, contract["case_id"], profile, read_context=read_context)
            if selected is not None and binding_read_results is not None:
                verified_binding_results.append(dict(selected, case_id=contract["case_id"],stage_id=expectation["stage_id"],
                    observed=not expected_only, observation_status="expected_only" if expected_only else "observed",
                    runtime_status="runtime_pending" if expected_only else "observed",
                    evidence_role="synthetic_expected_only" if expected_only else "actual_candidate_run"))
        if contract["case_id"]=="ASYNC_REJECT_THEN_QUERY":
            control,fraction,next_stage=stages
            same(fraction["operation_connection_id"],control["operation_connection_id"],"async_first_reject_original_connection")
            same(control.get("row_id"),fraction.get("row_id"),"async_reject_original_row")
            same(next_stage.get("row_id"),fraction.get("row_id"),"async_next_independent_original_row")
            if fraction["target_receipt"]["state_after"] in ("Broken","Closed"):
                require(next_stage["operation_connection_id"]!=fraction["operation_connection_id"],"async_retired_next_stage_new_ID")
                same(next_stage.get("connection_origin"),"new_connection_for_next_independent_stage_after_safe_abort","async_explicit_retired_successor")
                same(next_stage.get("previous_connection_id"),fraction["operation_connection_id"],"async_retired_predecessor_identity")
            else:
                same(next_stage["operation_connection_id"],fraction["operation_connection_id"],"async_open_connection_not_replaced")
                require(next_stage.get("connection_origin") not in ("new_connection_for_next_independent_stage_after_safe_abort","reopened_original_connection"),"async_open_no_retirement_claim")
        if contract["case_id"].startswith("BIND_"):
            require(len({s["command_id"] for s in stages}) == 1, "binding_same_command_object")
            require(len({s["parameter_id"] for s in stages if s["stage_id"] != "tls_default_explicit_reject"}) == 1,
                    "binding_same_parameter_object")
            if contract["case_id"] != "BIND_CLONE_RESET_REOPEN":
                require(len({s["operation_connection_id"] for s in stages}) == 1, "binding_same_logical_connection")
        if contract["case_id"] == "NATIVE_INTERVAL_REJECT_leading":
            control, target = stages
            for key in ("command_id", "parameter_id", "operation_connection_id", "row_id"):
                require(key in control and key in target, "leading_operation_identity_missing")
                same(control[key], target[key], "leading_control_target_same_operation:" + key)
            for key in ("command_id", "parameter_id", "operation_connection_id"):
                require(type(control[key]) is str and control[key], "leading_actual_operation_identity")
            if target["parameter_metadata"]["type_flag"] == 1:
                receipt = target.get("control_only_binding")
                same(receipt, dict(evidence_role="control_only", stage_id="control", command_id=control["command_id"],
                    connection_id=control["operation_connection_id"], statement_id=control["binding_statement_id"],
                    binding=control["binding"]), "leading_control_only_binding_attribution")
                require(control["binding"]["reliable_fixed"] is True and control["binding"]["packed_scale"] == 1574,
                    "leading_fixed_success_control_required")
        if contract["case_id"] == "BIND_CLONE_RESET_REOPEN":
            receipt = actual.get("clone_receipt")
            same(receipt, dict(clone_distinct_object=True, original_value_mutated=True,
                              original_scale_mutated=True, clone_value_unchanged=True,
                              clone_scale_unchanged=True, reset_invoked=True), "clone_actual_receipt")
            ids = [value["operation_connection_id"] for value in stages]
            require(ids[0] == ids[1] == ids[2] and ids[3] != ids[0], "reopen_new_physical_connection")
            original_id, clone_id = actual.get("original_parameter_id"), actual.get("clone_parameter_id")
            require(type(original_id) is str and original_id and type(clone_id) is str and clone_id
                and original_id != clone_id and stages[0]["parameter_id"] == clone_id, "clone_distinct_actual_object")
            before, after = actual.get("clone_source_before"), actual.get("clone_source_after")
            require(type(before) is dict and type(after) is dict, "clone_source_snapshots")
            same(before.get("value_ticks"), vectors()["ts_micro"]["ticks"], "clone_original_initial_ticks")
            same(after.get("value_ticks"), vectors()["ts_tick"]["ticks"], "clone_original_mutated_ticks")
            same(before.get("scale"), 7 if profile == "shared" else 6, "clone_original_initial_scale")
            same(after.get("scale"), 3, "clone_original_mutated_scale")
            same(actual.get("clone_before"), stages[0]["parameter"], "clone_initial_snapshot")
            same(actual.get("clone_after"), stages[1]["parameter"], "clone_isolation_snapshot")
            same(actual["clone_before"], actual["clone_after"], "clone_actual_value_and_declaration_unchanged")
    transaction_rows = {}
    for case in cases:
        if case["case_id"].startswith("TX_"):
            suffix = "_".join(case["case_id"].split("_")[-2:])
            row = integer(case["stages"][0].get("row_id"), "transaction_row_id")
            if suffix in transaction_rows:
                same(row, transaction_rows[suffix], "transaction_original_row_identity")
            else:
                transaction_rows[suffix] = row
    if binding_read_results is not None:
        binding_read_results.extend(verified_binding_results)
    return len(cases)
