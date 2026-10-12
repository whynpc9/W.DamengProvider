#!/usr/bin/env python3
"""Strict TP01 artifacts: independent candidate contracts, baseline characterization only."""
import argparse
from decimal import Decimal
import importlib.util
from pathlib import Path
import re
import sys
import json

ADVISORY_SCOPE = "shared_advisory_result_tuple_v1"
ADVISORY_RULE_ID = "TP06-ADVISORY-RESULT-001"
PARAMETER_QUERY_PROGRAM_SHA256 = "e01dee6daae69272521df4b2bb2544af02818c438a473713fd7fbfae63ed0ff9"
ADVISORY_PHYSICAL = dict(server_version="8.1.5.60", message_version=21,
    negotiated_encoding="utf-8", encoding_override_empty=True)
ADVISORY_MISMATCHES = [{"write": [16, 6], "read": [26, 7]}, {"write": [23, 6], "read": [27, 7]}]
REVALIDATION_FILES = ("eng/temporal-precision.validate.py", "tools/TemporalBoundaryProbe/test_validator_contracts.py",
    "tools/TemporalBoundaryProbe/contracts.py", "tools/TemporalBoundaryProbe/packed_oracle.py",
    "tools/TemporalBoundaryProbe/Program.cs",
    "tools/TemporalBoundaryProbe/contract-fixtures/handwritten-artifact-spec.json",
    "tools/TemporalBoundaryProbe/contract-fixtures/packed_literal_cases.json")

# The frozen runner copies contracts.py into build; normal checkout uses tools/.
_contract_paths = [Path(__file__).resolve().parent.parent / "tools/TemporalBoundaryProbe/contracts.py",
                   Path(__file__).resolve().parent.parent / "build/contracts.py"]
_contract_path = next((p for p in _contract_paths if p.is_file()), None)
if _contract_path is None:
    raise RuntimeError("frozen_contract_module_missing")
_module_spec = importlib.util.spec_from_file_location("tp01_contracts", _contract_path)
C = importlib.util.module_from_spec(_module_spec)
_module_spec.loader.exec_module(C)

_packed_paths = [Path(__file__).resolve().parent.parent / "tools/TemporalBoundaryProbe/packed_oracle.py", Path(__file__).resolve().parent.parent / "build/packed_oracle.py"]
_packed_path = next((path for path in _packed_paths if path.is_file()), None)
if _packed_path is None: raise RuntimeError("independent_packed_oracle_missing")
_packed_spec = importlib.util.spec_from_file_location("independent_packed_oracle", _packed_path)
P = importlib.util.module_from_spec(_packed_spec)
_packed_spec.loader.exec_module(P)


def need(condition, code):
    C.require(condition, code)


def get(value, key, code):
    return C.member(value, key, code)


def exact_int(value, expected, code):
    C.true_int(value, code)
    need(value == expected, code + ":mismatch")


def yes(value, code):
    need(C.boolean(value, code) is True, code)


def zero(value, code):
    exact_int(value, 0, code)


def _types(value, path="artifact"):
    """Reject boolean pseudo-numbers even in observations later classed baseline."""
    numeric = {"schema_version", "process_exit_code", "exit_code", "cleanup_exit_code", "restore_exit_code", "build_exit_code",
        "completed_cases", "persisted_rows", "fresh_rows", "affected_rows", "row_id", "ticks", "local_ticks", "utc_ticks", "offset_minutes",
        "requested_scale", "requested_precision", "user_scale", "column_scale", "c_type", "width", "semantic_scale",
        "mask", "type_flag", "raw_scale", "raw_c_type", "raw_precision", "raw_mask_before", "raw_derived_mask",
        "payload_length", "encoded_scale", "statement_id_before", "statement_id_after", "statement_id", "nanoseconds", "year", "month", "day", "hour", "minute", "second", "number",
        "source_files", "timezone", "message_version", "logical_reads", "ordinal", "server_error_number", "records_affected", "getter_calls"}
    boolean = {"hard_deadline", "cleanup_hard_deadline", "root_approved", "seven_write", "tltz_read", "tltz_write",
        "server_identity_verified", "cleanup_verified", "absent_before_create", "identity_verified", "available",
        "server_exact", "server_components_exact", "server_ff7_exact", "subsequent_read_completion", "server_preserves_requested",
        "typed_null", "fixed_describe", "observable", "is_bdta", "package_reference_verified", "actual_asset_verified",
        "wire_metadata_stable", "capability_open", "verified_server_response", "execute_attempted", "prepared_requested", "expected_constructor_rejection", "encoded_scale_observable", "prepare_invoked", "new_statement_observed", "synthetic", "expected_only", "database_accessed", "expected_dbnull", "value_is_dbnull", "server_format_is_dbnull", "cache_observable", "actual_getter_invoked", "has_real_infra_inner", "info_present", "drain_completed", "unexpected_rowset", "is_null", "receipt_granted", "encoding_override_empty", "capture_rejected"}
    if type(value) is dict:
        for key, item in value.items():
            if key in numeric and item is not None:
                C.true_int(item, path + "." + key)
            if key in boolean and item is not None:
                C.boolean(item, path + "." + key)
            _types(item, path + "." + key)
    elif type(value) is list:
        for item in value:
            _types(item, path + "[]")


def failure(record, code, required_type=None, constructor=False):
    error = C.object_value(get(record, "error", code), code + ".error")
    kind = C.string(get(error, "type", code), code + ".error.type")
    failure_class = C.enum(get(record, "failure_class", code), C.FAILURE_CLASSES, code + ".failure_class")
    info_kind = get(error, "failure_kind", code)
    phase, outcome, safe_code = (get(error, key, code) for key in ("phase", "outcome", "code"))
    need(info_kind is None or info_kind in ("Unknown", "Timeout", "Canceled", "Transport", "Server", "OutcomeUnknown"), code + ":failure_kind_enum")
    need(phase is None or phase in ("Unknown", "Dns", "Connect", "Authenticate", "Prepare", "Send", "Receive", "Fetch", "Commit", "Rollback", "Cleanup", "PoolWait"), code + ":error_phase_enum")
    need(outcome is None or outcome in ("NotSent", "ServerReported", "Unknown"), code + ":error_outcome_enum")
    need(safe_code is None or (type(safe_code) is str and re.fullmatch(r"WDM_[A-Z0-9_]{1,60}", safe_code)), code + ":error_code_enum")
    need(info_kind not in ("Timeout", "Canceled", "Transport", "OutcomeUnknown") and phase not in ("Dns", "Connect", "Authenticate", "PoolWait"), code + ":structured_infra_failure")
    need(not safe_code or not any(token in safe_code for token in ("TIMEOUT", "CANCEL", "TRANSPORT", "SOCKET", "AUTH")), code + ":infra_code")
    need(failure_class == "verified_server_response" or info_kind != "Server", code + ":server_failure_class_conflict")
    need(failure_class != "infra_failure" and not any(token in kind for token in
        ("Timeout", "Socket", "Authentication", "OperationCanceled", "IOException", "Transport")), code + ":infrastructure_failure")
    if required_type:
        need(kind == required_type, code + ":wrong_exception")
        need(failure_class == ("clr_precondition" if constructor else "user_rejection"), code + ":wrong_failure_class")
    if failure_class == "verified_server_response":
        yes(get(error, "verified_server_response", code), code + ":server_response_unverified")
        C.true_int(get(error, "number", code), code + ":server_error_number")
    elif error.get("verified_server_response") is True:
        need(False, code + ":verified_response_class_conflict")
    return failure_class


def reject(record, error_type, code, status="driver_write_rejected", persisted=True):
    need(get(record, "status", code) == status, code + ":required_rejection")
    failure(record, code, error_type)
    if persisted:
        zero(get(record, "persisted_rows", code), code + ":rejection_persisted")


def observation(value, spec, code, null=False):
    C.object_value(value, code)
    if null:
        need(value.get("outcome") == "null" and value.get("type") == "System.DBNull", code + ":typed_NULL")
        return
    need(get(value, "outcome", code) == "returned", code + ":not_returned")
    need(get(value, "type", code) == spec["type"], code + ":CLR_type")
    exact_int(get(value, "ticks", code), spec["ticks"], code + ":ticks")
    if spec["offset_minutes"] is None:
        need(get(value, "offset_minutes", code) is None and get(value, "kind", code) == "Unspecified", code + ":Kind_or_offset")
    else:
        exact_int(get(value, "offset_minutes", code), spec["offset_minutes"], code + ":offset")
        need(get(value, "kind", code) is None, code + ":DTO_kind")


def requested(record, spec, code):
    value = C.object_value(get(record, "expected", code), code + ".expected")
    need(get(value, "type", code) == spec["type"] and get(value, "outcome", code) == "returned", code + ":request_type")
    exact_int(get(value, "ticks", code), spec["ticks"], code + ":independent_request_ticks")
    if spec["offset_minutes"] is None:
        need(get(value, "offset_minutes", code) is None and get(value, "kind", code) == spec["kind"], code + ":request_Kind")
    else:
        exact_int(get(value, "offset_minutes", code), spec["offset_minutes"], code + ":request_offset")


def metadata(entry, ctype, scale, code, candidate=False, fixed=None, tltz=False):
    C.object_value(entry, code)
    yes(get(entry, "available", code), code + ":metadata_unobservable")
    exact_int(get(entry, "c_type", code), ctype, code + ":CType")
    exact_int(get(entry, "semantic_scale", code), scale, code + ":column_scale")
    exact_int(get(entry, "width", code), {14: 3, 15: 5, 22: 7, 16: 8, 23: 10, 26: 9, 27: 11}[ctype], code + ":wire_width")
    exact_int(get(entry, "mask", code), 4 if tltz else 0, code + ":mask")
    C.true_int(get(entry, "type_flag", code), code + ":type_flag", 0, 2)
    exact_int(entry["type_flag"], 0 if fixed is None else 1 if fixed else 2, code + ":metadata_role_flag")
    if candidate:
        need(get(entry, "raw_scale_status", code) == "captured_server_source", code + ":raw_source_unobservable")
        need(get(entry, "raw_source", code) in (("result_column", "ResultColumn") if fixed is None else ("parameter_describe", "ParameterDescribe")), code + ":raw_source_role")
        C.true_int(get(entry, "raw_c_type", code), code + ":raw_CType", 0, 255)
        C.true_int(get(entry, "raw_precision", code), code + ":raw_width", 0 if fixed is False else 1)
        raw_scale = C.true_int(get(entry, "raw_scale", code), code + ":raw_scale", None if fixed is False else 0)
        C.true_int(get(entry, "raw_mask_before", code), code + ":raw_mask", 0)
        exact_int(get(entry, "raw_derived_mask", code), 4 if tltz else 0, code + ":derived_mask")
        if fixed is not False:
            exact_int(entry["raw_c_type"], ctype, code + ":raw_type_provenance")
            exact_int(entry["raw_precision"], entry["width"], code + ":raw_width_provenance")
            exact_int(raw_scale, scale | (0x1000 if tltz else 0), code + ":raw_scale_provenance")
        else:
            # A genuine generic describe (e.g. CType2/width32767/scale0) is not the selected wire layout.
            need(entry["raw_source"] in ("parameter_describe", "ParameterDescribe"), code + ":advisory_describe_source")
            if entry["raw_c_type"] in (14, 15, 16, 22, 23, 26, 27):
                need(raw_scale >= 0 and raw_scale & ~0xF == 0, code + ":decorated_temporal_advisory")
            need(entry["raw_mask_before"] == 0, code + ":decorated_advisory")


def metadata_list(record, key, ctypes, scales, code, candidate=False, fixed=None, tltz=False):
    values = C.array(get(record, key, code), code + "." + key)
    need(len(values) == len(ctypes), code + ":metadata_count")
    for index, (entry, ctype, scale) in enumerate(zip(values, ctypes, scales)):
        metadata(entry, ctype, scale, code + ":metadata:" + str(index), candidate, fixed, tltz)


def column_observation(case, ctypes, scales, code, candidate, tltz=False):
    need(get(case, "status", code) == "observed", code + ":column_not_observed")
    metadata_list(case, "metadata", ctypes, scales, code, candidate, tltz=tltz)
    schema = C.array(get(case, "schema_scales", code), code + ":schema")
    need(len(schema) == len(scales), code + ":schema_count")
    for actual, scale in zip(schema, scales):
        exact_int(actual, scale, code + ":schema_scale")


def cell(record, ctype, scale, code, required):
    item = C.object_value(get(record, "cell", code), code + ":cell")
    observable = C.boolean(get(item, "observable", code), code + ":cell.observable")
    if not observable:
        need(not required and item.get("source") == "unobservable", code + ":row_layout_unobservable")
        return False
    length = C.true_int(get(item, "payload_length", code), code + ":cell.length", 1, 64)
    C.hash_value(get(item, "payload_sha256", code), code + ":cell.hash")
    need(get(item, "result_format", code) in ("ordinary_non_BDTA", "ordinary"), code + ":result_format")
    need(C.boolean(get(item, "is_bdta", code), code + ":cell.bdta") is False, code + ":public_BDTA_not_open")
    need(get(item, "source", code) == "already_fetched_cell", code + ":cell_source")
    normal = {14: 3, 15: 5, 22: 7, 16: 8, 23: 10, 26: 9, 27: 11}[ctype]
    # Legacy 12-byte ordinary microsecond buffers are existing <=6 compatibility only.
    need(length == normal or (length == 12 and scale <= 6), code + ":unknown_row_layout")
    return True


def text_precision(text, scale, code):
    # Declared precision controls textual zeros, not just numeric equality.
    match = re.search(r"\d{1,2}:\d{1,2}:\d{1,2}(?:\.(\d+))?", text)
    need(match is not None and len(match.group(1) or "") == scale, code + ":declared_text_precision")


def exact_text(text, spec, code):
    fields, offset = C.parse_temporal_text(text)
    need(fields == C.ticks_fields(spec["ticks"]) and offset == spec["offset_minutes"], code + ":independent_server_text")


def typed_null_receipt(value, reader, getter_name, code):
    """Only the proven cached-NULL getter context; generic failure() remains unchanged."""
    need(getter_name in ("GetString", "GetDateTime", "GetDateTimeOffset", "GetFieldValue<DateTime>", "GetFieldValue<DateTimeOffset>"), code + ":only_typed_NULL_getters")
    need(get(value, "outcome", code) == "error", code + ":real_NULL_getter_error_required")
    receipt = C.object_value(get(value, "known_null_receipt", code), code + ":NULL_receipt")
    need(get(receipt, "observation_class", code) == "known_typed_null_rejection", code + ":NULL_class")
    for key in ("expected_dbnull", "value_is_dbnull", "server_format_is_dbnull", "actual_getter_invoked"):
        yes(get(receipt, key, code), code + ":" + key)
    need(get(receipt, "reader_lane", code) == reader and get(receipt, "getter_name", code) == getter_name, code + ":actual_reader_getter")
    exact_int(get(receipt, "ordinal", code), 0, code + ":NULL_ordinal")
    need(C.boolean(get(receipt, "has_real_infra_inner", code), code + ":real_infra_inner") is False, code + ":real_infra_forbidden")
    need(C.boolean(get(receipt, "verified_server_response", code), code + ":receipt_server_response") is False, code + ":server_response_forbidden")
    cancel = get(receipt, "cancel_source", code)
    need(cancel in (None, "None"), code + ":terminal_cancel_forbidden")
    transaction = get(receipt, "transaction_outcome", code)
    need(transaction in (None, "Active", "Committed", "RolledBack", "CompletedExternally"), code + ":unknown_transaction_forbidden")
    error = C.object_value(get(value, "error", code), code + ":original_error")
    provider_type = "W.Dm.DmException" if reader == "W" else "Dm.DmException"
    need(get(error, "type", code) == get(receipt, "provider_exception_type", code) == provider_type, code + ":loaded_provider_exception")
    exact_int(get(error, "number", code), 6081, code + ":original_NULL_number")
    exact_int(get(receipt, "number", code), 6081, code + ":receipt_NULL_number")
    need(get(error, "verified_server_response", code) is False and get(receipt, "server_error_number", code) is None, code + ":verified_server_error_forbidden")
    cached = C.boolean(get(receipt, "cache_observable", code), code + ":cache_observable")
    cell = get(receipt, "cached_cell", code)
    if cached:
        yes(get(cell, "is_null", code), code + ":cached_NULL")
        need(get(cell, "payload_length", code) is None and get(cell, "payload_sha256", code) is None, code + ":cache_NULL_payload_conflict")
    else:
        need(cell is None, code + ":unobservable_cache_claim")
    info = C.boolean(get(receipt, "info_present", code), code + ":info_present")
    fields = tuple(get(error, key, code) for key in ("failure_kind", "phase", "code", "outcome"))
    if info:
        need(reader == "W" and fields == ("Transport", "Fetch", "WDM_TRANSPORT", "NotSent") and cancel == "None", code + ":only_known_local_fallback_allowed")
    else:
        need(fields == (None, None, None, None), code + ":traditional_NULL_must_not_hide_failure_info")


def official_null_value(value, reader, getter_name, code):
    if value.get("outcome") == "error":
        if getter_name == "GetValue":
            need(value.get("known_null_receipt") is None, code + ":GetValue_has_no_typed_NULL_exception")
            failure({"error": value.get("error"), "failure_class": value.get("error", {}).get("failure_class")}, code)
        else:
            typed_null_receipt(value, reader, getter_name, code)
        return
    outcome = C.enum(get(value, "outcome", code), ("null", "returned-null", "returned-string-empty", "returned"), code + ":official_NULL_observation")
    if outcome == "null":
        need(value.get("type") == "System.DBNull", code + ":official_DBNull")
    elif outcome == "returned-null":
        need(value.get("type") is None and value.get("text") is None, code + ":official_null_value")
    elif outcome == "returned-string-empty":
        need(value.get("type") == "System.String" and value.get("text") == "", code + ":official_empty_value")
    else:
        need(value.get("type") in ("System.DateTime", "System.DateTimeOffset"), code + ":official_default_type")
        C.true_int(get(value, "ticks", code), code + ":official_default_ticks", 0, C.MAX_TICKS)


def null_oracle(observed, reader, spec, code):
    context = C.object_value(get(observed, "null_context", code), code + ":NULL_context")
    need(context.get("reader_lane") == reader, code + ":NULL_context_lane")
    exact_int(get(context, "ordinal", code), 0, code + ":context_ordinal")
    for key in ("expected_dbnull", "value_is_dbnull", "server_format_is_dbnull"):
        yes(get(context, key, code), code + ":context_" + key)
    if reader == "W":
        observation(get(observed, "get_value", code), spec, code + ":GetValue_DBNull", True)
        need(observed["get_value"].get("known_null_receipt") is None, code + ":GetValue_cannot_use_NULL_rejection_exception")
    else:
        official_null_value(get(observed, "get_value", code), reader, "GetValue", code + ":official_GetValue")
    observed_cell = C.object_value(get(observed, "cell", code), code + ":actual_NULL_cell")
    cache_visible = C.boolean(get(observed_cell, "observable", code), code + ":actual_cache_observable")
    if cache_visible:
        yes(get(observed_cell, "is_null", code), code + ":actual_cached_NULL")
        need(get(observed_cell, "payload_length", code) is None and get(observed_cell, "payload_sha256", code) is None, code + ":actual_NULL_payload")
    getter_names = {
        "typed": "GetDateTimeOffset" if spec["offset_minutes"] is not None else "GetDateTime",
        "generic": "GetFieldValue<DateTimeOffset>" if spec["offset_minutes"] is not None else "GetFieldValue<DateTime>",
        "get_string": "GetString",
    }
    for getter, actual_name in getter_names.items():
        value = C.object_value(get(observed, getter, code), code + ":" + getter)
        if reader == "W":
            typed_null_receipt(value, reader, actual_name, code + ":" + getter)
            if cache_visible:
                yes(get(value["known_null_receipt"], "cache_observable", code), code + ":receipt_cannot_hide_visible_cache")
        elif getter == "generic" and value.get("outcome") == "error" and value.get("error", {}).get("type") == "System.InvalidCastException":
            # Observed O generic NULL casting, only within the fully checked NULL/cache context above.
            need(reader == "O" and cache_visible, code + ":official_generic_NULL_cache_required")
            need(value.get("known_null_receipt") is None, code + ":InvalidCast_is_not_6081_receipt")
            original = C.object_value(get(value, "error", code), code + ":official_generic_InvalidCast")
            need(get(original, "number", code) is None and get(original, "verified_server_response", code) is False, code + ":local_generic_InvalidCast_only")
            need(all(get(original, field, code) is None for field in ("failure_kind", "phase", "code", "outcome")), code + ":generic_InvalidCast_has_no_failure_info")
            failure({"error": original, "failure_class": get(original, "failure_class", code)}, code + ":official_generic_NULL", "System.InvalidCastException")
        else:
            official_null_value(value, reader, actual_name, code + ":" + getter)
    yes(get(observed, "subsequent_read_completion", code), code + ":NULL_read_completion")


def exact_raw_components(observed, spec, code):
    metadata_entry = _single_metadata(observed, "metadata", code)
    decoded = P.decode_observation(get(observed, "raw_oracle", code), metadata_entry, get(observed, "cell", code))
    expected = dict(zip(("year", "month", "day", "hour", "minute", "second", "nanoseconds"), C.ticks_fields(spec["ticks"])), offset_minutes=spec["offset_minutes"])
    need(decoded == expected, code + ":independent_packed_expected_fields")
    fields9, offset9 = C.parse_temporal_text(get(observed, "server_ff9", code))
    need(tuple(decoded[key] for key in ("year", "month", "day", "hour", "minute", "second", "nanoseconds")) == fields9 and decoded["offset_minutes"] == offset9, code + ":raw_FF9_conflict")
    fields7, offset7 = C.parse_temporal_text(get(observed, "server_ff7", code))
    need(fields7 == fields9 and offset7 is None, code + ":raw_FF7_conflict")


def exact_oracles(record, spec, code, readers, scale, candidate, ctype=None, null=False, components_required=True):
    observations = C.array(get(record, "oracles", code), code + ":oracles", nonempty=True)
    indexed = {}
    for observed in observations:
        reader = C.enum(get(observed, "reader_lane", code), ("O", "W"), code + ":reader_lane")
        need(reader not in indexed, code + ":duplicate_oracle")
        indexed[reader] = observed
    need(set(indexed) == set(readers), code + ":independent_reader_set")
    for reader, observed in indexed.items():
        prefix = code + ":oracle:" + reader
        need(get(observed, "status", prefix) == "observed", prefix + ":failed")
        if null:
            null_oracle(observed, reader, spec, prefix)
            continue
        need(not any(isinstance(observed.get(key), dict) and observed[key].get("known_null_receipt") is not None for key in ("typed", "generic", "get_value", "get_string")), prefix + ":NULL_receipt_on_nonNULL_value")
        exact_text(get(observed, "server_ff9", prefix), spec, prefix + ":FF9")
        text_precision(observed["server_ff9"], 9, prefix + ":FF9")
        # FF7 intentionally has no TZ suffix, so compare only local fields.
        fields7, offset7 = C.parse_temporal_text(get(observed, "server_ff7", prefix))
        need(fields7 == C.ticks_fields(spec["ticks"]) and offset7 is None, prefix + ":FF7")
        text_precision(observed["server_ff7"], 7, prefix + ":FF7")
        for key in ("server_exact", "server_ff7_exact"):
            yes(get(observed, key, prefix), prefix + ":" + key)
        if components_required and spec["offset_minutes"] is not None:
            # EXTRACT on TSTZ is retained verbatim as characterization, not an exact fractional oracle.
            C.boolean(get(observed, "server_components_exact", prefix), prefix + ":EXTRACT_observation_flag")
            if "component_oracle_error" in observed:
                error = observed["component_oracle_error"]
                failure({"error": error, "failure_class": error.get("failure_class")}, prefix + ":EXTRACT_error_classification")
            else:
                C.array(get(observed, "server_components", prefix), prefix + ":EXTRACT_observed_components")
                C.string(get(observed, "server_component_basis", prefix), prefix + ":EXTRACT_basis")
            exact_raw_components(observed, spec, prefix)
        elif components_required:
            yes(get(observed, "server_components_exact", prefix), prefix + ":server_components_exact")
            components = C.array(get(observed, "server_components", prefix), prefix + ":components")
            need(len(components) == 6 and all(type(x) in (int, float, str) for x in components), prefix + ":component_types")
            basis = C.enum(get(observed, "server_component_basis", prefix), ("local", "utc", "local_and_utc_equal"), prefix + ":component_basis")
            component_ticks = spec["ticks"]
            need(basis != "local_and_utc_equal" or spec["offset_minutes"] in (None, 0), prefix + ":false_equal_component_basis")
            if basis == "utc":
                need(spec["offset_minutes"] is not None, prefix + ":UTC_basis_without_offset")
                component_ticks -= spec["offset_minutes"] * C.TICKS_MINUTE
            fields = C.ticks_fields(component_ticks)
            expected_components = [Decimal(x) for x in fields[:5]] + [Decimal(fields[5]) + Decimal(fields[6]) / 1_000_000_000]
            need([Decimal(str(x)) for x in components] == expected_components, prefix + ":independent_components")
        else:
            need(get(observed, "server_component_basis", prefix) == "not_queried_advisory_server_text_only", prefix + ":advisory_oracle_role")
            need(get(observed, "server_components_exact", prefix) is False, prefix + ":advisory_did_not_query_components")
        yes(get(observed, "subsequent_read_completion", prefix), prefix + ":read_completion")
        if reader == "W":
            for getter in ("typed", "generic", "get_value"):
                observation(get(observed, getter, prefix), spec, prefix + ":" + getter)
            text = C.object_value(get(observed, "get_string", prefix), prefix + ":GetString")
            need(text.get("outcome") == "returned" and text.get("type") == "System.String", prefix + ":string_return")
            exact_text(get(text, "text", prefix), spec, prefix + ":GetString")
            text_precision(text["text"], scale, prefix + ":GetString")
            expected_type = ctype or ((23 if scale <= 6 else 27) if spec["offset_minutes"] is not None else (16 if scale <= 6 else 26))
            metadata_list(observed, "metadata", [expected_type], [scale], prefix, candidate)
            schema = C.array(get(observed, "schema_scales", prefix), prefix + ":schema")
            need(len(schema) == 1, prefix + ":schema_count")
            exact_int(schema[0], scale, prefix + ":schema_scale")
            cell(observed, expected_type, scale, prefix, candidate)


def advisory_result_oracles(record, spec, code, write_type, write_scale, context, null=False):
    """Write selection has already passed; never infer it from the SELECT result."""
    observations = C.array(get(record, "oracles", code), code + ":advisory_results", nonempty=True)
    need(len(observations) == 1 and observations[0].get("reader_lane") == "W", code + ":advisory_W_result")
    observed = observations[0]
    entry = _single_metadata(observed, "metadata", code)
    read_type = C.true_int(get(entry, "c_type", code), code + ":read_CType")
    read_scale = C.true_int(get(entry, "semantic_scale", code), code + ":read_scale", 0, 9)
    need(read_type in ((16, 26) if spec["offset_minutes"] is None else (23, 27)), code + ":result_temporal_family")
    if (read_type, read_scale) != (write_type, write_scale):
        need({"write": [write_type, write_scale], "read": [read_type, read_scale]} in ADVISORY_MISMATCHES,
            code + ":unapproved_write_read_tuple")
        need(type(context) is dict and context.get("profile") == "shared"
            and context.get("program_sha256") == PARAMETER_QUERY_PROGRAM_SHA256, code + ":ParameterQuery_scope")
        physical = C.object_value(context.get("physical"), code + ":physical_scope")
        need(all(type(physical.get(key)) is type(value) and physical.get(key) == value
                 for key, value in ADVISORY_PHYSICAL.items()), code + ":shared_physical_scope")
        need(physical.get("encoding") == "utf-8" and physical.get("profile_origin") == "physical_get_conn_instance_conn_property"
            and physical.get("negotiated_encoding_origin") == "physical_conn_property_serverEncoding_field",
            code + ":physical_source_scope")
    metadata(entry, read_type, read_scale, code + ":advisory_result", True)
    need(entry.get("metadata_role") == "result_column" and entry.get("result_format") == "ordinary_packed"
        and entry.get("is_bdta") is False and entry.get("raw_mask_before") == 0, code + ":result_role_format")
    schema = C.array(get(observed, "schema_scales", code), code + ":result_schema")
    need(len(schema) == 1, code + ":result_schema_count")
    exact_int(schema[0], read_scale, code + ":result_schema_scale")
    observed_cell = C.object_value(get(observed, "cell", code), code + ":advisory_cell")
    need(observed_cell.get("is_bdta") is False
        and observed_cell.get("result_format") in ("ordinary_non_BDTA", "ordinary"), code + ":advisory_cell_format")
    if not null:
        cell(observed, read_type, read_scale, code + ":advisory_cell", True)
    elif observed_cell.get("observable") is True:
        need(observed_cell.get("source") == "already_fetched_cell", code + ":advisory_NULL_cell_source")
    # NULL also reaches the result metadata/schema gate above, before the original
    # DBNull/6081/cache receipt gate. No payload or text is invented for NULL.
    exact_oracles(record, spec, code, ("W",), read_scale, True, read_type, null, components_required=False)


def write_outcome(case, spec, capability, code, readers, candidate, forced_error=None, ctype=None, read_scale=None):
    status = get(case, "status", code)
    if spec.get("constructor"):
        need(status == "constructor_rejected_before_driver", code + ":constructor_classification")
        failure(case, code, "System.ArgumentOutOfRangeException", constructor=True)
        need(case.get("execute_attempted") is False, code + ":constructor_entered_driver")
        fields = get(case, "requested_fields", code)
        local, offset = spec["value"]["ticks"], spec["value"]["offset_minutes"]
        exact_int(get(fields, "local_ticks", code), local, code + ":constructor_local")
        exact_int(get(fields, "utc_ticks", code), local - offset * C.TICKS_MINUTE, code + ":constructor_UTC")
        exact_int(get(fields, "offset_minutes", code), offset, code + ":constructor_offset")
        yes(get(case, "expected_constructor_rejection", code), code + ":constructor_rejection_flag")
        return
    requested(case, spec["value"], code)
    driver = C.enum(get(case, "driver_lane", code), ("O", "W"), code + ":writer_lane")
    tz = spec["value"]["offset_minutes"] is not None
    need(get(case, "requested_db_type", code) == ("DateTimeOffset" if tz else "DateTime2"), code + ":requested_DbType")
    if driver == "W":
        exact_int(get(case, "requested_scale", code), spec["scale"], code + ":requested_Scale")
        need(get(case, "requested_precision", code) is None, code + ":unexpected_requested_Precision")
    else:
        need(get(case, "requested_scale", code) is None, code + ":official_requested_Scale")
        exact_int(get(case, "requested_precision", code), spec["scale"], code + ":official_requested_Precision")
    closed_advisory = spec["scale"] == 7 and not capability["seven_write"] and driver == "W" and case.get("write_path") == "advisory_profile_request"
    if closed_advisory:
        need(get(case, "column_scale", code) is None and get(case, "column_metadata", code) == [], code + ":closed_request_must_not_depend_on_column")
    else:
        exact_int(get(case, "column_scale", code), spec["scale"] if read_scale is None else read_scale, code + ":column_scale")
    if closed_advisory:
        zero(get(case, "persisted_rows", code), code + ":closed_advisory_no_dml")
        need(get(case, "persistence_source", code) == "advisory_no_dml", code + ":closed_advisory_persistence_fact")
        if candidate:
            reject(case, "System.NotSupportedException", code, persisted=False)
        else:
            need(status in ("written", "driver_write_rejected"), code + ":unclassified_closed_baseline")
            if status == "driver_write_rejected":
                failure(case, code)
            # A historical SELECT return is an observation, never a seven-write capability acceptance.
        return
    if not candidate:
        need(status in ("written", "driver_write_rejected"), code + ":unclassified_baseline")
        if status == "driver_write_rejected":
            failure(case, code)
            zero(get(case, "persisted_rows", code), code + ":baseline_rejection_persisted")
        else:
            exact_int(get(case, "persisted_rows", code), 1, code + ":baseline_written_count")
        return
    value, scale = spec["value"], spec["scale"]
    error = forced_error
    if error is None and not C.write_scale_supported(scale, capability):
        error = "System.NotSupportedException"
    if error is None:
        ticks, offset = value["ticks"], value["offset_minutes"]
        utc = ticks - (offset or 0) * C.TICKS_MINUTE
        if not capability["local_ticks_min"] <= ticks <= capability["local_ticks_max"] or not capability["utc_ticks_min"] <= utc <= capability["utc_ticks_max"] or (offset is not None and not capability["offset_min_minutes"] <= offset <= capability["offset_max_minutes"]):
            error = "System.OverflowException"
        elif ticks % (10 ** max(0, 7 - scale)):
            error = "System.OverflowException"
    if error:
        reject(case, error, code)
    else:
        need(status == "written", code + ":required_exact_success")
        exact_int(get(case, "persisted_rows", code), 1, code + ":persisted_row")
        exact_oracles(case, value, code, readers, scale if read_scale is None else read_scale, candidate, ctype)


def case_index(report, lane):
    cases = C.array(get(report, "cases", lane), lane + ":cases", nonempty=True)
    exact_int(get(report, "completed_cases", lane), len(cases), lane + ":completed_cases")
    result = {}
    for case in cases:
        driver = C.enum(get(case, "driver_lane", lane), C.DRIVER_LANES, lane + ":driver_lane")
        key = (driver, C.string(get(case, "case_id", lane), lane + ":case_id"))
        need(key not in result, lane + ":duplicate_case")
        C.enum(get(case, "status", lane), C.CASE_STATUSES, lane + ":case_status")
        if case.get("status") in ("failed", "driver_write_rejected", "driver_parameter_rejected", "replacement_rejected", "seed_rejected"):
            failure(case, lane + ":" + key[1])
        result[key] = case
    return result


def at(index, driver, case_id, lane):
    key = (driver, case_id)
    need(key in index, lane + ":missing_case:" + driver + ":" + case_id)
    return index[key]


def validate_core(index, profile, capability, candidate):
    readers = ("O", "W") if profile == "shared" else ("W",)
    specs = C.core_specs()
    for driver in readers:
        seven_required = profile == "shared" or capability["seven_write"]
        ctypes = [16, 23, 26, 27]
        if seven_required:
            columns = at(index, driver, "columns_6_7", "core")
            column_observation(columns, ctypes, [6, 6, 7, 7], driver + ":columns", candidate and driver == "W")
        else:
            for column in ("D7", "Z7"):
                ddl = at(index, "server", "main_ddl_" + column, "core")
                server_case(ddl, "TLS_seven_column_existence:" + column, capability, False)
        for column, ctype, scale in zip(("D6", "Z6", "D7", "Z7") if seven_required else ("D6", "Z6"), ctypes, (6, 6, 7, 7)):

            column_observation(at(index, driver, "column_" + column, "core"), [ctype], [scale], driver + ":column:" + column, candidate and driver == "W")
        for case_id, spec in specs.items():
            case = at(index, driver, case_id, "core")
            if not seven_required and spec["scale"] == 7 and not spec.get("constructor"):
                need(case.get("write_path") == "advisory_profile_request", driver + ":closed7_requires_independent_advisory_request")
            write_outcome(case, spec, capability, driver + ":" + case_id, readers, candidate and driver == "W")
            if candidate and driver == "W":
                mandatory_six = re.fullmatch(r"(?:dt|dto)6_.+", case_id) and spec["value"]["ticks"] % 10 == 0
                mandatory_main_seven = profile == "shared" and (re.fullmatch(r"(?:dt|dto)7_.+", case_id) or case_id.startswith("kind_") or case_id in ("offset_0", "offset_480", "offset_N300", "offset_330"))
                if mandatory_six or mandatory_main_seven:
                    need(case.get("status") == "written", "mandatory_exact_success_not_cap_domain_rejection:" + case_id)
            # A valid constructor may not be replaced with an unrelated constructor rejection.
            if "constructor" not in case_id:
                opposing = case_id.replace("local_", "constructor_").replace("utc_", "utc_constructor_")
                if opposing != case_id:
                    need((driver, opposing) not in index, driver + ":spurious_constructor_rejection")
        rollback = at(index, driver, "typed_dml_rollback", "core")
        need(get(rollback, "status", "rollback") == "rollback_verified", driver + ":rollback")
        zero(get(rollback, "fresh_rows", "rollback"), driver + ":rollback_rows")
    if profile == "shared":
        for case_id in ("dt7_ordinary_tick1", "dt7_ordinary_micro", "offset_0", "offset_480", "offset_N300", "offset_330"):
            record = at(index, "O", case_id, "core")
            need(record.get("status") == "written", "O_typed_seed_required:" + case_id)
            # O behavior at extremes remains characterization. Required ordinary O seed is a real W-read oracle.
            exact_oracles(record, specs[case_id]["value"], "O_to_W:" + case_id, readers, 7, candidate)


def _single_metadata(record, key, code):
    entries = C.array(get(record, key, code), code + ":" + key)
    need(len(entries) == 1, code + ":single_metadata")
    return entries[0]


RAW_SNAPSHOT_FIELDS = ("raw_source", "raw_c_type", "raw_precision", "raw_scale", "raw_mask_before", "raw_derived_mask", "type_flag")


def advisory_snapshot(record, after, code):
    """Compare independent pre-Execute capture to retained raw metadata, never to selected CType."""
    before = _single_metadata(record, "parameter_metadata_before_execute", code)
    need(get(before, "raw_scale_status", code) == "captured_server_source", code + ":before_raw_unobservable")
    need(get(before, "raw_source", code) in ("parameter_describe", "ParameterDescribe"), code + ":before_parameter_source")
    C.true_int(get(before, "raw_c_type", code), code + ":before_raw_type", 0, 255)
    C.true_int(get(before, "raw_precision", code), code + ":before_raw_precision", 0)
    C.true_int(get(before, "raw_scale", code), code + ":before_raw_scale")
    C.true_int(get(before, "raw_mask_before", code), code + ":before_raw_mask", 0)
    C.true_int(get(before, "raw_derived_mask", code), code + ":before_derived_mask", 0)
    exact_int(get(before, "type_flag", code), 2, code + ":before_type_flag")
    raw = tuple(get(before, key, code) for key in RAW_SNAPSHOT_FIELDS)
    need(raw == tuple(get(after, key, code) for key in RAW_SNAPSHOT_FIELDS), code + ":raw_describe_rewritten")
    return raw


def binding_rejection(case, expected_error, code, fixed):
    reject(case, expected_error, code, "driver_parameter_rejected", persisted=False)
    phases = C.array(get(case, "phase_results", code), code + ":rejection_phases")
    need(len(phases) == 1 and phases[0].get("phase") == "initial", code + ":initial_rejection_required")
    phase = phases[0]
    reject(phase, expected_error, code + ":initial", "driver_parameter_rejected", persisted=False)
    need(phase["error"] == case["error"] and phase["failure_class"] == case["failure_class"], code + ":rejection_summary_mismatch")
    attempted = C.boolean(get(phase, "execute_attempted", code), code + ":execute_attempted")
    row = get(phase, "row_id", code)
    if row is not None:
        C.true_int(row, code + ":row_id", 1)
    if fixed and row is not None:
        zero(get(phase, "persisted_rows", code), code + ":fresh_rejection_rows")
        need(get(phase, "persistence_source", code) == "fresh_connection_row_count", code + ":unverified_fixed_rejection")
    else:
        need(not attempted or not fixed, code + ":fixed_execute_missing_row")
        need(get(phase, "persistence_source", code) == ("advisory_no_dml" if attempted else "not_executed"), code + ":rejection_execution_fact")
        if phase.get("persisted_rows") is not None:
            zero(phase["persisted_rows"], code + ":rejection_persistence")


def validate_binding(case, spec, capability, code, readers, candidate, profile, advisory_context=None):
    expected_source = "fixed_describe" if spec["declaration"].startswith("inferred") and spec["null"] and spec["fixed"] else "untyped_null" if spec["declaration"].startswith("inferred") and spec["null"] else "inferred" if spec["declaration"].startswith("inferred") else "provider" if spec["declaration"].startswith("provider") else "user_db_type"
    for key, value in (("declaration", spec["declaration"]), ("requested_scale", spec["scale"]), ("requested_precision", spec["precision"]), ("typed_null", spec["null"]), ("fixed_describe", spec["fixed"]), ("type_source", expected_source)):
        need(get(case, key, code) == value and type(case[key]) is type(value), code + ":request_axis:" + key)
    tz = spec["declaration"] in ("inferred_dto", "DateTimeOffset", "provider_dto")
    frozen_target = (("Z" if tz else "D") + ("7" if profile == "shared" else "6")) if spec["fixed"] else None
    need(get(case, "target_column", code) == frozen_target, code + ":frozen_physical_target")
    need(case.get("status") != "closed", code + ":mandatory_fixed_or_advisory_axis_cannot_skip")
    for phase in C.array(get(case, "phase_results", code), code + ":actual_target_phases", nonempty=True):
        need(get(phase, "target_column", code) == frozen_target, code + ":actual_phase_physical_target")
    column = None
    if spec["fixed"]:
        column = _single_metadata(case, "column_metadata", code)
        column_type = C.true_int(get(column, "c_type", code), code + ":fixed_column_type")
        column_scale = C.true_int(get(column, "semantic_scale", code), code + ":fixed_column_scale", 0, 9)
        need(column_type in ((23, 27) if tz else (16, 26)), code + ":fixed_family")
        need(column_scale == (7 if profile == "shared" else 6), code + ":physical_target_scale")
        metadata(column, column_type, column_scale, code + ":fixed_column", candidate)
    if not candidate:
        need(case.get("status") in ("observed", "driver_parameter_rejected"), code + ":baseline_binding_classification")
        if case["status"] == "driver_parameter_rejected": failure(case, code)
        elif spec["null"]:
            null_spec = C.value_spec(C.ORDINARY, 330 if tz else None)
            for phase in C.array(get(case, "phase_results", code), code + ":baseline_NULL_phases", nonempty=True):
                observation(get(phase, "expected", code), null_spec, code + ":expected_DBNull", True)
                for observed in C.array(get(phase, "oracles", code), code + ":baseline_NULL_oracles", nonempty=True):
                    reader = C.enum(get(observed, "reader_lane", code), ("O", "W"), code + ":NULL_reader")
                    null_oracle(observed, reader, null_spec, code + ":baseline_NULL:" + reader)
        return
    expected = C.binding_expectation(spec, capability, column)
    if "error" in expected:
        binding_rejection(case, expected["error"], code, spec["fixed"])
        return
    need(case.get("status") == "observed", code + ":valid_binding_must_succeed")
    phases = C.array(get(case, "phase_results", code), code + ":phase_results")
    need([p.get("phase") for p in phases] == ["initial", "replacement", "reprepare"], code + ":executed_binding_phases")
    row_ids = []
    retained_raw = None
    previous_after = None
    for index, phase in enumerate(phases):
        prefix = code + ":" + phase["phase"]
        need(get(phase, "status", prefix) == "observed", prefix + ":failed")
        need(get(phase, "target_column", prefix) == frozen_target, prefix + ":actual_phase_target")
        previous_after = statement_observation(phase, spec["fixed"], prefix, previous_after)
        need(get(phase, "recovery_source", prefix) == "none", prefix + ":unexpected_recovery")
        fraction = 1_234_560 if index == 0 else 7_654_320
        tz = spec["declaration"] in ("inferred_dto", "DateTimeOffset", "provider_dto")
        value = C.value_spec(C.ORDINARY + fraction, 330 if tz else None)
        effective = expected.copy()
        if index == 2:
            updated = dict(spec, scale=6, precision=0)
            effective = C.binding_expectation(updated, capability, column)
            need("error" not in effective, prefix + ":unexpected_reprepare_contract")
        if not spec["null"]:
            requested(phase, value, prefix)
        else:
            observation(get(phase, "expected", prefix), value, prefix + ":expected_NULL", True)
        yes(get(phase, "encoded_scale_observable", prefix), prefix + ":encoded_scale_unobservable")
        exact_int(get(phase, "encoded_scale", prefix), effective["scale"], prefix + ":encoded_scale")
        metadata_list(phase, "parameter_metadata", [effective["c_type"]], [column["semantic_scale"] if spec["fixed"] else effective["scale"]], prefix, True, spec["fixed"])
        yes(get(phase, "execute_attempted", prefix), prefix + ":not_executed")
        if not spec["fixed"]:
            raw = advisory_snapshot(phase, _single_metadata(phase, "parameter_metadata", prefix), prefix)
            if index == 0:
                retained_raw = raw

        if spec["fixed"]:
            exact_int(get(phase, "persisted_rows", prefix), 1, prefix + ":fresh_persisted")
            row_ids.append(C.true_int(get(phase, "row_id", prefix), prefix + ":row_id", 1))
            exact_oracles(phase, value, prefix, readers, column["semantic_scale"], True, expected["c_type"], spec["null"])
        else:
            zero(get(phase, "persisted_rows", prefix), prefix + ":advisory_persistence")
            advisory_result_oracles(phase, value, prefix, effective["c_type"], effective["scale"], advisory_context, spec["null"])
    need(not row_ids or len(set(row_ids)) == len(row_ids), code + ":fixed_reexecute_reused_PK")


def statement_observation(phase, fixed, code, previous_after=None):
    yes(get(phase, "prepare_invoked", code), code + ":real_prepare_required")
    need(get(phase, "statement_before_owner", code) == "command", code + ":before_owner")
    before = C.true_int(get(phase, "statement_id_before", code), code + ":before_statement")
    after = C.true_int(get(phase, "statement_id_after", code), code + ":after_statement")
    need(before != 0 and after == before, code + ":statement_transfer_identity")
    need(get(phase, "statement_after_owner", code) == "reader", code + ":live_after_owner")
    need(get(phase, "execution_api", code) == "ExecuteReader", code + ":public_execution_api")
    need(get(phase, "snapshot_timing", code) == "live_reader_before_drain", code + ":live_snapshot_timing")
    if fixed:
        yes(get(phase, "drain_completed", code), code + ":DML_drain_completed")
        need(C.boolean(get(phase, "unexpected_rowset", code), code + ":unexpected_rowset") is False, code + ":unexpected_DML_rowset")
        exact_int(get(phase, "records_affected", code), 1, code + ":DML_affected_rows")
    observed_new = C.boolean(get(phase, "new_statement_observed", code), code + ":new_statement_observed")
    if previous_after is not None:
        need(observed_new == (before != previous_after), code + ":statement_change_fact")
    return after


def validate_stability(case, tz, capability, code, candidate, advisory_context=None):
    if not candidate:
        need(case.get("status") in ("value_replacement_observed", "replacement_rejected"), code + ":baseline_stability")
        if case["status"] == "replacement_rejected": failure(case, code)
        return
    phases = C.array(get(case, "phase_results", code), code + ":stability_phases")
    need([p.get("phase") for p in phases] == ["initial", "replacement", "reprepare"], code + ":stability_phases_required")
    first_type = (27 if tz else 26) if capability["seven_write"] else (23 if tz else 16)
    first_scale = 7 if capability["seven_write"] else 6
    previous_after = None
    for index, phase in enumerate(phases):
        prefix = code + ":" + phase["phase"]
        value = C.value_spec(C.ORDINARY + (1_234_560 if index == 0 else 1_234_567 if index == 1 else 7_654_320), 330 if tz else None)
        requested(phase, value, prefix)
        if index == 1 and not capability["seven_write"]:
            reject(phase, "System.OverflowException", prefix, "driver_parameter_rejected", persisted=False)
            need(get(phase, "persistence_source", prefix) in ("advisory_no_dml", "not_executed"), prefix + ":failed_SELECT_fact")
            if phase.get("persisted_rows") is not None: zero(phase["persisted_rows"], prefix + ":failed_rows")
            previous_after = None
            continue
        need(phase.get("status") == "observed", prefix + ":success_required")
        recovery = get(phase, "recovery_source", prefix)
        need(recovery == ("fresh_connection_after_rejection" if index == 2 and not capability["seven_write"] else "none"), prefix + ":recovery_source")
        previous_after = statement_observation(phase, False, prefix, previous_after)
        ctype, scale = ((23 if tz else 16), 6) if index == 2 else (first_type, first_scale)
        metadata_list(phase, "parameter_metadata", [ctype], [scale], prefix, True, False)
        advisory_snapshot(phase, _single_metadata(phase, "parameter_metadata", prefix), prefix)
        yes(get(phase, "encoded_scale_observable", prefix), prefix + ":encoded_scale_observable")
        exact_int(get(phase, "encoded_scale", prefix), scale, prefix + ":encoded_scale")
        zero(get(phase, "persisted_rows", prefix), prefix + ":SELECT_rows")
        advisory_result_oracles(phase, value, prefix, ctype, scale, advisory_context)
    summary_pairs = (("first_metadata", 0, "parameter_metadata"), ("second_metadata", 1, "parameter_metadata"), ("explicit_scale6_reprepare", 2, "parameter_metadata"), ("scale6_execute_metadata", 2, "parameter_metadata"))
    for name, index, key in summary_pairs:
        need(get(case, name, code) == get(phases[index], key, code), code + ":summary_not_live_capture:" + name)
    for name, index in (("first_value", 0), ("second_value", 1), ("scale6_value", 2)):
        rows = phases[index].get("oracles", [])
        actual = rows[0].get("get_value") if rows else None
        need(get(case, name, code) == actual, code + ":summary_value_mismatch:" + name)
    need(case.get("status") == ("value_replacement_observed" if capability["seven_write"] else "replacement_rejected"), code + ":stability_summary")
    if not capability["seven_write"]: failure(case, code, "System.OverflowException")


def validate_metadata(index, profile, capability, candidate, advisory_context=None):
    readers = ("O", "W") if profile == "shared" else ("W",)
    for driver in readers:
        for tz, case_id in ((False, "inferred_dt_tail_stability"), (True, "inferred_dto_tail_stability")):
            validate_stability(at(index, driver, case_id, "metadata"), tz, capability, driver + ":" + case_id, candidate and driver == "W", advisory_context)
        for case_id, spec in C.binding_specs().items():
            validate_binding(at(index, driver, case_id, "metadata"), spec, capability, driver + ":" + case_id, readers, candidate and driver == "W", profile, advisory_context)
    for loss in (False, True):
        base = "bind_DateTime2_s7_punset_fixed_value"
        case_id = ("dual_scale6_loss_" if loss else "dual_scale6_") + base
        record = at(index, "W", case_id, "metadata")
        need(get(record, "target_column", case_id) == "D6", case_id + ":dual_physical_target")
        column = _single_metadata(record, "column_metadata", case_id)
        ctype = C.true_int(get(column, "c_type", case_id), case_id + ":column_CType")
        need(ctype in (16, 26), case_id + ":column_family")
        metadata(column, ctype, 6, case_id + ":column", candidate)
        error = "System.NotSupportedException" if ctype == 16 else "System.OverflowException" if loss else None
        write_outcome(record, {"value": C.value_spec(C.ORDINARY + (1_234_567 if loss else 1_234_560)), "scale": 7}, capability, case_id, readers, candidate, error, ctype, read_scale=6)


def server_case(case, code, capability, candidate, approved=False):
    status = get(case, "status", code)
    if status in ("server_columns_created", "server_column_created", "server_expression_evaluated", "seeded", "server_seed_created"):
        return True
    need(not approved, code + ":approved_capability_DDL_or_expression_failed")
    need(status in ("failed", "seed_rejected"), code + ":server_classification")
    kind = failure(case, code)
    if candidate:
        need(kind == "verified_server_response", code + ":server_failure_not_verified")
        allowed = capability.get("verified_unsupported_error_numbers", {}).get(case["case_id"], [])
        need(type(allowed) is list and all(type(x) is int for x in allowed) and case["error"]["number"] in allowed,
             code + ":server_unsupported_not_approved")
        need(case.get("classification") == "server_unsupported", code + ":server_failure_not_unsupported")
    return False


def actual_fields(record, code):
    actual = C.object_value(get(record, "actual_server", code), code + ":actual_server")
    fields = C.object_value(get(actual, "fields", code), code + ":fields")
    keys = ("year", "month", "day", "hour", "minute", "second", "nanoseconds")
    parts = tuple(C.true_int(get(fields, key, code), code + ":" + key) for key in keys)
    need(0 <= parts[6] < 1_000_000_000, code + ":ns_range")
    scale = C.true_int(get(actual, "semantic_scale", code), code + ":scale", 0, 9)
    need(parts[6] % 10 ** (9 - scale) == 0, code + ":invalid_declared_fraction")
    parsed, offset = C.parse_temporal_text(get(actual, "text", code))
    need(parsed == parts and offset == get(fields, "offset_minutes", code), code + ":actual_server_text_fields_conflict")
    return actual, parts, offset


def expression_read(case, server, scale, code, candidate):
    need(get(case, "status", code) == "observed", code + ":read_not_observed")
    actual, parts, offset = actual_fields(server, code)
    need(get(case, "actual_server", code) == actual, code + ":actual_server_not_independent")
    # The actual server result, even when CAST changed the request, owns the read contract.
    ctype = _single_metadata(case, "metadata", code).get("c_type")
    need(ctype in (15, 16, 26), code + ":expression_CType")
    column_observation(case, [ctype], [scale], code, candidate)
    cell(case, ctype, scale, code, candidate)
    text = C.object_value(get(case, "get_string", code), code + ":GetString")
    need(text.get("outcome") == "returned" and text.get("type") == "System.String", code + ":GetString_failed")
    parsed, actual_offset = C.parse_temporal_text(get(text, "text", code))
    need(parsed == parts and actual_offset == offset, code + ":exact_actual_GetString")
    text_precision(text["text"], scale, code + ":GetString")
    yes(get(case, "subsequent_read_completion", code), code + ":read_completion")
    if ctype == 15:
        clr_error = parts[3] >= 24 or parts[6] % 100 != 0
        ticks = C.datetime_ticks(1900, 1, 1, *parts[3:6]) + parts[6] // 100 if not clr_error else None
    else:
        clr_error = not 1 <= parts[0] <= 9999 or parts[6] % 100 != 0
        ticks = C.datetime_ticks(*parts[:6]) + parts[6] // 100 if not clr_error else None
    for name in ("get_value", "typed_date", "generic_date"):
        value = get(case, name, code)
        if clr_error:
            need(value.get("outcome") == "error" and value.get("error", {}).get("type") == "System.OverflowException", code + ":CLR_must_reject:" + name)
        else:
            observation(value, C.value_spec(ticks), code + ":CLR:" + name)


def validate_simple_layouts(index, readers, capability, candidate):
    samples = {
        "ordinary_date": (14, 0, (2026, 7, 23, 0, 0, 0, 0), None),
        "ordinary_time6": (15, 6, (None, None, None, 12, 13, 14, 123456000), None),
        "ordinary_time_tz6": (22, 6, (None, None, None, 12, 13, 14, 123456000), 330),
    }
    classifications = {}
    for case_id, (ctype, scale, fields, offset) in samples.items():
        server = at(index, "server", case_id, "extended")
        if not server_case(server, case_id, capability, candidate):
            classifications[case_id] = "server_rejection_verified" if server.get("failure_class") == "verified_server_response" else "server_behavior_unverified"
            continue
        actual = C.object_value(get(server, "actual_server", case_id), case_id + ":actual_server")
        yes(get(actual, "observable", case_id), case_id + ":actual_fields_unobservable")
        raw_fields = C.object_value(get(actual, "fields", case_id), case_id + ":actual_fields")
        observed = tuple(get(raw_fields, key, case_id) for key in ("year", "month", "day", "hour", "minute", "second", "nanoseconds"))
        need(observed == fields and all(type(a) is type(b) for a, b in zip(observed, fields)), case_id + ":independent_seed_fields")
        need(get(raw_fields, "offset_minutes", case_id) == offset and type(raw_fields["offset_minutes"]) is type(offset), case_id + ":seed_offset")
        exact_int(get(actual, "semantic_scale", case_id), scale, case_id + ":seed_scale")
        parsed, parsed_offset = C.parse_temporal_fields(get(actual, "text", case_id))
        need(parsed == fields and parsed_offset == offset, case_id + ":seed_text_fields")
        need(get(server, "seed_writer", case_id) == "server_expression", case_id + ":independent_seed_writer")
        yes(get(server, "independent_of_W_write", case_id), case_id + ":seed_independence")
        for driver in readers:
            code = driver + ":" + case_id
            record = at(index, driver, case_id, "extended")
            need(get(record, "actual_server", code) == actual, code + ":server_observation_mismatch")
            column_observation(record, [ctype], [scale], code, candidate and driver == "W")
            entry = _single_metadata(record, "metadata", code)
            need(get(entry, "result_format", code) == "ordinary_packed", code + ":column_result_format")
            need(C.boolean(get(entry, "is_bdta", code), code + ":column_bdta") is False, code + ":BDTA_closed")
            # Recorded payload size/hash from already-fetched cache is required, not inferred from CType.
            cell(record, ctype, scale, code, required=True)
            yes(get(record, "subsequent_read_completion", code), code + ":read_completion")
            if candidate and driver == "W":
                calendar = fields[:3] if ctype == 14 else (1900, 1, 1)
                ticks = C.datetime_ticks(*calendar, *fields[3:6]) + fields[6] // 100
                projection = C.value_spec(ticks, offset if ctype == 22 else None)
                observation(get(record, "get_value", code), projection, code + ":GetValue")
                observation(get(record, "typed", code), C.value_spec(ticks), code + ":GetDateTime")
                if ctype == 22:
                    need(record.get("generic", {}).get("outcome") == "error" and record["generic"].get("error", {}).get("type") == "System.InvalidCastException", code + ":generic_DateTime_must_reject")
                    for getter in ("typed_offset", "generic_offset"):
                        observation(get(record, getter, code), projection, code + ":" + getter)
                else:
                    observation(get(record, "generic", code), projection, code + ":generic_DateTime")
                    for getter in ("typed_offset", "generic_offset"):
                        value = get(record, getter, code)
                        need(value.get("outcome") == "error" and value.get("error", {}).get("type") == "System.InvalidCastException", code + ":offset_getter_must_reject")
                text = C.object_value(get(record, "get_string", code), code + ":GetString")
                need(text.get("outcome") == "returned" and text.get("type") == "System.String", code + ":GetString")
                parsed, parsed_offset = C.parse_temporal_fields(get(text, "text", code))
                need(parsed == fields and parsed_offset == offset, code + ":exact_seed_GetString")
                if ctype != 14:
                    text_precision(text["text"], scale, code + ":GetString")
        classifications[case_id] = "sampled_ordinary_layout_verified"
    return classifications


def validate_extended(index, profile, capability, candidate):
    readers = ("O", "W") if profile == "shared" else ("W",)
    for scale in (8, 9):
        ddl = at(index, "server", "ddl_scale" + str(scale), "extended")
        available = server_case(ddl, "scale" + str(scale), capability, candidate, scale in capability["write_scales"] if candidate else False)
        if not available:
            continue
        for driver in readers:
            column_observation(at(index, driver, "columns_scale" + str(scale), "extended"), [26, 27], [scale, scale], driver + ":higher_columns", candidate and driver == "W")
            for tz in (False, True):
                case_id = ("write_tz_scale" if tz else "write_scale") + str(scale)
                write_outcome(at(index, driver, case_id, "extended"), {"value": C.value_spec(C.ORDINARY + 1, 330 if tz else None), "scale": scale}, capability, driver + ":" + case_id, readers, candidate and driver == "W")
        for ns in ((10, 90, 100, 1) if scale == 8 else (1, 99, 100)):
            case_id = f"ns_scale{scale}_{ns}"
            server = at(index, "server", case_id, "extended")
            if server_case(server, case_id, capability, candidate):
                actual, _, _ = actual_fields(server, case_id)
                exact_int(actual["semantic_scale"], scale, case_id + ":actual_scale")
                for driver in readers:
                    read = at(index, driver, case_id, "extended")
                    if candidate and driver == "W": expression_read(read, server, scale, driver + ":" + case_id, True)
                    else: need(read.get("status") == "observed", driver + ":baseline_expression_read")
    # Extension existence evidence is separate from approved write/read support.
    ids = ["calendar_1582_10_" + str(day) for day in (4, 5, 10, 14, 15)] + ["bce", "year_zero"]
    ids += [prefix + str(scale) for prefix in ("time24_scale", "time_fraction_scale") for scale in (6, 7, 8, 9)]
    for case_id in ids:
        server = at(index, "server", case_id, "extended")
        server_case(server, case_id, capability, candidate)
    for scale in (6, 7):
        successful_seeds = 0
        ddl = at(index, "server", "tltz_ddl_scale" + str(scale), "extended")
        if not server_case(ddl, "tltz_DDL" + str(scale), capability, candidate):
            continue
        for driver in readers:
            case = at(index, driver, "tltz_metadata_scale" + str(scale), "extended")
            entry = _single_metadata(case, "metadata", "TLTZ")
            ctype = C.true_int(entry.get("c_type"), "TLTZ.CType")
            need(ctype in (16, 26), "TLTZ.ctype_family")
            if candidate and driver == "W": column_observation(case, [ctype], [scale], "TLTZ.column", True, tltz=True)
            # TLTZ mask semantics differs from an ordinary TIMESTAMP column.
            metadata(entry, ctype, scale, "TLTZ.metadata", candidate and driver == "W", tltz=True)
        values = {"crossday": C.datetime_ticks(2026, 7, 23, 23, 59, 59) + (9_999_999 if scale == 7 else 9_999_990),
                  "min": 0, "max": C.MAX_TICKS - (0 if scale == 7 else 9)}
        for name, ticks in values.items():
            written = at(index, "W", f"tltz_{scale}_{name}", "extended")
            write_outcome(written, {"value": C.value_spec(ticks), "scale": scale}, capability, "TLTZ.write", readers, candidate,
                          "System.NotSupportedException" if candidate else None)
            seed = at(index, "server", f"server_seed_TLTZ{scale}_{name}", "extended")
            if not server_case(seed, "TLTZ.independent_seed", capability, candidate):
                read_closed = index.get(("W", f"tltz_read_{scale}_{name}"))
                need(read_closed is None or (read_closed.get("status") == "closed" and read_closed.get("reason") == "independent_seed_unverified_read_gate_not_accepted"), "TLTZ.failed_seed_cannot_claim_read")
                continue
            successful_seeds += 1
            need(seed.get("seed_writer") in ("O", "server_expression") and seed.get("independent_of_W_write") is True, "TLTZ.seed_not_independent")
            read = at(index, "W", f"tltz_read_{scale}_{name}", "extended")
            need(read.get("status") == "observed", "TLTZ.read_missing")
            yes(get(read, "subsequent_read_completion", "TLTZ"), "TLTZ.read_completion")
            if candidate:
                for getter in ("typed", "generic", "get_value", "get_string"):
                    value = get(read, getter, "TLTZ")
                    need(value.get("outcome") == "error" and value.get("error", {}).get("type") == "System.NotSupportedException", "TLTZ.closed_read:" + getter)
        need(not candidate or successful_seeds > 0, "TLTZ.closed_read_requires_successful_independent_seed")
    cross = at(index, "W", "tltz_cross_session_timezone", "extended")
    need(cross.get("status") == "closed" and C.boolean(get(cross, "capability_open", "TLTZ"), "TLTZ.capability_open") is False, "TLTZ.cross_session_closed")


def physical_profile(value, expected_version, code):
    """v11 evidence origin only; no runtime capability or encoding-scope expansion."""
    C.object_value(value, code)
    keys = {"server_version", "encoding", "timezone", "message_version", "result_format", "server_user", "server_schema",
            "negotiated_encoding", "negotiated_encoding_origin", "encoding_override_empty", "profile_origin"}
    need(set(value) == keys, code + ":physical_profile_fields")
    version = C.string(get(value, "server_version", code), code + ":version")
    need(re.fullmatch(r"[0-9]{1,4}(?:\.[0-9]{1,5}){1,4}", version) is not None and version == expected_version, code + ":server_version")
    C.true_int(get(value, "message_version", code), code + ":message_version", 0, 65535)
    C.true_int(get(value, "timezone", code), code + ":timezone", -1440, 1440)
    effective = C.enum(get(value, "encoding", code), ("utf-8", "gb18030", "big5"), code + ":encoding")
    negotiated = C.enum(get(value, "negotiated_encoding", code), ("utf-8", "gb18030", "big5"), code + ":negotiated_encoding")
    yes(get(value, "encoding_override_empty", code), code + ":encoding_override_closed")
    need(effective == negotiated, code + ":physical_encoding_conflict")
    need(get(value, "profile_origin", code) == "physical_get_conn_instance_conn_property", code + ":physical_origin_required")
    need(get(value, "negotiated_encoding_origin", code) == "physical_conn_property_serverEncoding_field", code + ":negotiated_origin_required")
    need(get(value, "result_format", code) == "ordinary_non_BDTA", code + ":result_format")
    need(get(value, "server_user", code) == get(value, "server_schema", code) == "WDM_PROVIDER_TEST", code + ":actual_TEST_identity")


def validate_producer_shape(shape):
    """Consumes actual C# offline serialization; never promotes synthetic data to a DB gate."""
    try:
        _types(shape)
        exact_int(get(shape, "schema_version", "shape"), 1, "shape.schema")
        need(get(shape, "task", "shape") == "TP-01-offline-shape", "shape.task")
        yes(get(shape, "synthetic", "shape"), "shape.synthetic")
        yes(get(shape, "expected_only", "shape"), "shape.expected_only")
        need(C.boolean(get(shape, "database_accessed", "shape"), "shape.database_accessed") is False, "shape.DB_forbidden")
        capture = C.object_value(get(shape, "physical_profile_capture", "shape"), "shape.profile_capture")
        logical = C.object_value(get(capture, "logical_input", "shape"), "shape.logical_input")
        need(logical == dict(server_version="9.9.9.9", encoding="gb18030", message_version=19, timezone=-300), "shape.divergent_logical_input")
        captured = get(capture, "captured_profile", "shape")
        physical_profile(captured, "8.1.5.60", "shape.physical_profile")
        need(captured["encoding"] == captured["negotiated_encoding"] == "utf-8", "shape.physical_encoding_selection")
        exact_int(captured["message_version"], 21, "shape.physical_message_selection")
        exact_int(captured["timezone"], 480, "shape.physical_timezone_selection")
        zero(get(capture, "logical_reads", "shape"), "shape.logical_must_not_be_read")
        profile_rejections = C.array(get(capture, "rejections", "shape"), "shape.profile_rejections", nonempty=True)
        required_rejections = {"missing_method", "missing_instance", "missing_property", "nonempty_override", "encoding_conflict", "encoding_unobservable",
                               "message_not_integer", "override_unobservable", "version_invalid", "public_version_conflict"}
        need(len(profile_rejections) == len(required_rejections) and {entry.get("case_id") for entry in profile_rejections} == required_rejections, "shape.profile_required_rejections")
        for entry in profile_rejections: yes(get(entry, "capture_rejected", "shape"), "shape.profile_fail_closed")
        samples = C.array(get(shape, "samples", "shape"), "shape.samples")
        need(len(samples) == 2 and {sample.get("case_id") for sample in samples} == {"sample_dt", "sample_dto"}, "shape.samples_required")
        for sample in samples:
            spec = C.value_spec(639204129161234560, 330 if sample["case_id"] == "sample_dto" else None)
            requested({"expected": get(sample, "observation", "shape")}, spec, "shape:" + sample["case_id"])
        targets = C.array(get(shape, "fixed_targets", "shape"), "shape.fixed_targets")
        expected_targets = {("shared", False): "D7", ("shared", True): "Z7", ("tls", False): "D6", ("tls", True): "Z6"}
        seen_targets = set()
        for entry in targets:
            profile = C.enum(get(entry, "profile", "shape"), C.PROFILES, "shape.fixed_target_profile")
            tz = C.boolean(get(entry, "tz", "shape"), "shape.fixed_target_tz")
            pair = (profile, tz)
            need(pair not in seen_targets and get(entry, "target_column", "shape") == expected_targets[pair], "shape.independent_fixed_target")
            seen_targets.add(pair)
        need(seen_targets == set(expected_targets), "shape.required_fixed_targets")
        roles = C.object_value(get(shape, "metadata", "shape"), "shape.metadata")
        for key, fixed in (("result_column", None), ("fixed_parameter", True), ("advisory_parameter", False)):
            metadata(get(roles, key, "shape"), 26, 7, "shape:" + key, True, fixed)
        rejects = C.array(get(shape, "rejections", "shape"), "shape.rejections", nonempty=True)
        for index, phase in enumerate(rejects):
            need(phase.get("phase") == "initial", "shape.initial_rejection")
            # Use the real top-level rejection consumer: no invented persisted_rows aggregate.
            case = dict(status="driver_parameter_rejected", error=phase.get("error"), failure_class=phase.get("failure_class"), phase_results=[phase])
            binding_rejection(case, "System.NotSupportedException", "shape:reject:" + str(index), False)
            need(phase.get("persisted_rows") is None and phase.get("execute_attempted") is False, "shape.early_rejection_fact")
            exact_int(get(phase, "requested_scale", "shape"), 6, "shape.requested_scale")
            exact_int(get(phase, "requested_precision", "shape"), 7, "shape.requested_precision")
        owners = C.object_value(get(shape, "statement_ownership", "shape"), "shape.owners")
        before, live, closed = (get(owners, key, "shape") for key in ("before", "after_live", "after_close"))
        need(before.get("statement_owner") == "command" and live.get("statement_owner") == "reader", "shape.reader_ownership_transfer")
        bid = C.true_int(get(before, "statement_id", "shape"), "shape.before_id")
        need(bid != 0 and get(live, "statement_id", "shape") == bid, "shape.same_transferred_statement")
        need(closed.get("statement_owner") == "unobservable" and closed.get("statement_id") is None and closed.get("parameter_metadata") == [], "shape.no_stale_reader_statement")
        need(live.get("parameter_metadata") != [] and live.get("encoded_scale_observable") is True, "shape.live_reader_observation_required")
        exact_int(get(live, "encoded_scale", "shape"), 7, "shape.live_encoded_scale")
        raw_samples = C.array(get(shape, "raw_oracle_samples", "shape"), "shape.raw_oracle_samples")
        golden_samples = {
            "DT9_tick1": ("EA87BBEE81C8000000", None), "TZ11_offset0_tick1": ("EA87BBEE81C80000000000", 0),
            "TZ11_offset480_tick1": ("EA87BBEE81C8000000E001", 480), "TZ11_offsetN300_tick1": ("EA87BBEE81C8000000D4FE", -300),
            "TZ11_offset330_tick1": ("EA87BBEE81C80000004A01", 330),
        }
        closed_samples = {"unknown12_closed", "bdta_closed", "type_length_mismatch_closed", "mask4_closed"}
        need(len(raw_samples) == 9 and {sample.get("case_id") for sample in raw_samples} == set(golden_samples) | closed_samples, "shape.raw_samples_required")
        for sample in raw_samples:
            case_id = sample["case_id"]
            raw = get(sample, "raw_oracle", "shape")
            if case_id in closed_samples:
                need(raw.get("observable") is False and raw.get("synthetic_temporal_hex") is None, "shape.unknown_raw_closed")
                continue
            literal, offset = golden_samples[case_id]
            need(raw.get("synthetic_temporal_hex") == literal, "shape.raw_literal_not_independent_golden")
            cell = {"observable": True, "source": "already_fetched_cell", "payload_length": raw.get("payload_length"), "payload_sha256": raw.get("payload_sha256"), "result_format": raw.get("result_format"), "is_bdta": raw.get("is_bdta")}
            decoded = P.decode_observation(raw, get(sample, "metadata", "shape"), cell)
            need(decoded == dict(year=2026,month=7,day=23,hour=14,minute=15,second=16,nanoseconds=100,offset_minutes=offset), "shape.independent_raw_fields")
        mutation = C.object_value(get(shape, "raw_cache_mutation", "shape"), "shape.raw_cache_mutation")
        before, after = get(mutation, "before", "shape"), get(mutation, "after", "shape")
        need(before.get("synthetic_temporal_hex") == "EA87BBEE81C8000000" and after.get("synthetic_temporal_hex") == "EA87BBEE81CA000000", "shape.actual_cache_mutation_bytes")
        need(before.get("payload_sha256") != after.get("payload_sha256"), "shape.actual_cache_mutation_hash")
        for raw in (before,after):
            need(raw.get("payload_sha256") == __import__("hashlib").sha256(bytes.fromhex(raw["synthetic_temporal_hex"])).hexdigest(), "shape.mutation_actual_hash")
        # Mutation is 101ns at declared scale7: it must fail independently, not be coerced to 100ns.
        dt_meta = next(sample["metadata"] for sample in raw_samples if sample["case_id"] == "DT9_tick1")
        after_cell = dict(observable=True,source="already_fetched_cell",payload_length=after.get("payload_length"),payload_sha256=after.get("payload_sha256"),result_format=after.get("result_format"),is_bdta=after.get("is_bdta"))
        rejected_after = False
        try: P.decode_observation(after,dt_meta,after_cell)
        except P.PacketError: rejected_after = True
        need(rejected_after, "shape.declared_scale_mutation_must_reject")
        nulls = C.object_value(get(shape, "null_observations", "shape"), "shape.NULL_observations")
        known = C.object_value(get(nulls, "known_w6081", "shape"), "shape.known_NULL")
        exact_int(get(known, "getter_calls", "shape"), 1, "shape.actual_NULL_getter_invoked")
        typed_null_receipt(get(known, "observation", "shape"), "W", "GetString", "shape.known_NULL")
        # FailureInfo must remain the measured Transport fallback, not normalized to pass classification.
        original = known["observation"]["error"]
        need(original.get("failure_class") == "infra_failure", "shape.preserve_original_safe_classification")
        for key, outcome in (("official_null", "returned-null"), ("official_empty", "returned-string-empty")):
            item = get(nulls, key, "shape")
            exact_int(get(item, "getter_calls", "shape"), 1, "shape.actual_official_getter_called")
            need(item.get("observation", {}).get("outcome") == outcome, "shape.official_real_NULL_return")
            official_null_value(item["observation"], "O", "GetString", "shape:" + key)
        dbnull = get(nulls, "getvalue_dbnull", "shape")
        exact_int(get(dbnull, "getter_calls", "shape"), 1, "shape.actual_GetValue_DBNull_called")
        observation(get(dbnull, "observation", "shape"), C.value_spec(0), "shape.actual_GetValue_DBNull", True)
        need(dbnull["observation"].get("known_null_receipt") is None, "shape.GetValue_not_typed_receipt")
        negatives = C.array(get(nulls, "negative", "shape"), "shape.NULL_negative")
        expected_negative = {"non_null_6081", "expected_nonnull_6081", "wrong_ordinal_6081", "wrong_lane_6081", "missing_cache_evidence", "contradictory_cache", "timeout_inner", "socket_inner", "cancel_inner", "unknown_outcome", "unknown_kind", "verified_server_error", "transaction_unknown", "getvalue_6081"}
        need(len(negatives) == len(expected_negative) and {item.get("case_id") for item in negatives} == expected_negative, "shape.all_NULL_negative_branches")
        for item in negatives:
            exact_int(get(item, "getter_calls", "shape"), 1, "shape.negative_real_getter_called")
            need(C.boolean(get(item, "receipt_granted", "shape"), "shape.NULL_negative_receipt") is False, "shape.forbidden_NULL_exception")
        fixed = C.object_value(get(shape, "fixed_dml_observation", "shape"), "shape.fixed_DML")
        need(fixed.get("execution_api") == "ExecuteReader" and fixed.get("snapshot_timing") == "live_reader_before_drain", "shape.public_DML_live_snapshot")
        need(fixed.get("statement_before_owner") == "command" and fixed.get("statement_after_owner") == "reader", "shape.fixed_owner_transfer")
        before_id = C.true_int(get(fixed, "statement_id_before", "shape"), "shape.DML_before_id")
        need(before_id != 0 and fixed.get("statement_id_after") == before_id, "shape.fixed_statement_identity")
        yes(get(fixed, "encoded_scale_observable", "shape"), "shape.fixed_real_encoded_scale")
        exact_int(get(fixed, "encoded_scale", "shape"), 7, "shape.fixed_encode_scale")
        metadata_list(fixed, "parameter_metadata", [26], [7], "shape.fixed_metadata", True, True)
        yes(get(fixed, "drain_completed", "shape"), "shape.fixed_drain")
        need(C.boolean(get(fixed, "unexpected_rowset", "shape"), "shape.unexpected_DML_rowset") is False, "shape.no_unexpected_rows")
        exact_int(get(fixed, "records_affected", "shape"), 1, "shape.fixed_records_affected")
        closed_fixed = get(shape, "fixed_dml_after_close", "shape")
        need(closed_fixed.get("statement_owner") == "unobservable" and closed_fixed.get("statement_id") is None and closed_fixed.get("parameter_metadata") == [], "shape.fixed_no_stale_after_close")
        cleanup = C.array(get(shape, "cleanup_cases", "shape"), "shape.cleanup", nonempty=True)
        keys = [entry.get("case_id") for entry in cleanup]
        cleanup_expected = {
            "preexisting": ("rejected", 0), "known_create_failure": ("rejected", 0), "lost_ack_true_marker": ("cleaned", 1),
            "missing_marker": ("rejected", 0), "wrong_marker": ("rejected", 0), "same_prefix_wrong_full_nonce": ("rejected", 0),
            "bad_profile": ("rejected", 0), "bad_package": ("rejected", 0), "bad_identity": ("rejected", 0), "bad_nonce": ("rejected", 0),
            "duplicate_manifest": ("rejected", 0), "malformed_manifest": ("rejected", 0), "reserved_present": ("rejected", 0),
            "reserved_absent": ("cleaned", 0), "inflight_present": ("cleaned", 1), "confirmed_present": ("cleaned", 1),
            "wrong_db_identity": ("rejected", 0), "cleanup_failure": ("rejected", 1), "fresh_absence_failure": ("rejected", 1),
        }
        need(len(keys) == len(set(keys)) and set(keys) == set(cleanup_expected), "shape.required_cleanup_cases")
        for entry in cleanup:
            C.string(get(entry, "case_id", "shape"), "shape.cleanup_case_id")
            C.enum(get(entry, "outcome", "shape"), ("cleaned", "rejected"), "shape.cleanup_outcome")
            calls = C.array(get(entry, "calls", "shape"), "shape.cleanup_calls")
            need(all(call in ("create", "identity", "exists", "marker", "drop", "fresh_identity", "fresh_exists") for call in calls), "shape.cleanup_call_enum")
            exact_int(get(entry, "drop_count", "shape"), calls.count("drop"), "shape.drop_count")
            need((entry["outcome"], entry["drop_count"]) == cleanup_expected[entry["case_id"]], "shape.independent_cleanup_outcome")
            need(entry["outcome"] != "rejected" or entry["drop_count"] == 0 or "marker" in calls, "shape.unverified_drop")
            if "drop" in calls:
                need(calls.index("identity") < calls.index("marker") < calls.index("drop"), "shape.drop_identity_marker_order")
                if entry["outcome"] == "cleaned":
                    need("fresh_identity" in calls and "fresh_exists" in calls, "shape.clean_requires_fresh_verification")
        return dict(schema_version=1, status="producer_shape_contract_verified", synthetic=True, expected_only=True,
                    candidate_acceptance=False, full_temporal_acceptance=False, database_accessed=False, issues=[])
    except C.ContractError as error:
        return dict(status="producer_shape_contract_failed", candidate_acceptance=False, issues=[str(error)])
    except Exception as error:
        return dict(status="producer_shape_contract_failed", candidate_acceptance=False, issues=["shape_error:" + type(error).__name__])


def _contained_file(root, relative, code):
    relative = C.relative_path(relative, code)
    path = (root / relative).resolve()
    need(path.is_relative_to(root) and path.is_file(), code + ":missing_or_escaped_file")
    return path


def _external_receipt(receipt, code):
    need(type(receipt) is dict and set(receipt) == {"path", "sha256"}, code + ":receipt_fields")
    path = Path(C.string(receipt["path"], code + ":path"))
    need(path.is_absolute() and path.is_file(), code + ":absolute_file_required")
    path = path.resolve()
    expected = C.hash_value(receipt["sha256"], code + ":sha256")
    need(C.digest(path) == expected, code + ":hash")
    return path


def _fresh_output(directory, output):
    need(output is not None, "revalidation_output_required")
    output = Path(output).resolve()
    sidecar = output.with_name(output.name + ".lineage.json")
    need(not output.is_relative_to(directory), "revalidation_output_inside_producer")
    need(not output.exists() and not sidecar.exists(), "revalidation_existing_output_or_sidecar")
    return output


def revalidation_lineage(directory, gate, manifest_path, manifest_sha256, output):
    """Only an independently supplied root-frozen manifest enables verifier overlay."""
    need(gate == "candidate", "revalidation_candidate_only")
    directory = Path(directory).resolve()
    _fresh_output(directory, output)
    manifest_path = Path(manifest_path).resolve()
    expected_manifest = C.hash_value(manifest_sha256, "revalidation_manifest_sha256")
    need(C.digest(manifest_path) == expected_manifest, "revalidation_manifest_hash")
    lineage = C.load_json(manifest_path); _types(lineage)
    need(set(lineage) == {"schema_version", "task", "scope", "producer_evidence_root", "producer_directory",
        "producer_policy_sha256", "producer_evidence", "verifier_root", "verifier_files", "rules_manifest"}, "revalidation_manifest_fields")
    exact_int(lineage["schema_version"], 1, "revalidation_schema")
    need(lineage["task"] == "TP06" and lineage["scope"] == ADVISORY_SCOPE, "revalidation_scope")
    root = Path(C.string(lineage["producer_evidence_root"], "producer_evidence_root"))
    need(root.is_absolute() and root.is_dir(), "producer_evidence_root_absolute")
    root = root.resolve()
    relative_directory = C.relative_path(lineage["producer_directory"], "producer_directory")
    need((root / relative_directory).resolve() == directory and directory.is_relative_to(root), "producer_directory_binding")
    records = C.array(lineage["producer_evidence"], "producer_evidence", nonempty=True)
    roles = {"runner": "runner.json", "frozen_manifest": "frozen-input/manifest.json",
        "identities": "frozen-input/identities.json", "product_source_manifest": "frozen-input/product-source-manifest.json",
        "product_source_archive": "frozen-input/product-source.tar", "capabilities": "frozen-input/capabilities.json"}
    for lane in C.LANES:
        for suffix, name in ((".json", "report"), (".cleanup.json", "cleanup"), (".ownership.json", "ownership"),
                             (".ownership.json.final.json", "ownership_final")):
            roles[lane + "_" + name] = lane + suffix
    need(len(records) == 20 and {item.get("role") for item in records} == set(roles) | {"failure", "runtime_summary"},
        "producer_evidence_exact_roles")
    paths = set(); indexed = {}
    for item in records:
        need(type(item) is dict and set(item) == {"role", "path", "sha256"}, "producer_evidence_fields")
        path = _contained_file(root, item["path"], "producer_evidence_path")
        need(path not in paths, "producer_evidence_duplicate_path"); paths.add(path)
        digest = C.hash_value(item["sha256"], "producer_evidence_sha256")
        need(C.digest(path) == digest, "producer_evidence_hash:" + item["role"])
        if item["role"] in roles:
            need(path == (directory / roles[item["role"]]).resolve(), "producer_evidence_role_path:" + item["role"])
        else:
            need(not path.is_relative_to(directory), "producer_prior_result_must_be_external")
        indexed[item["role"]] = path
    producer_runner = C.load_json(indexed["runner"])
    need(producer_runner.get("profile") == "shared" and producer_runner.get("phase") == "candidate",
        "revalidation_shared_candidate_only")
    physical = get(C.load_json(indexed["metadata_report"]), "W_profile", "revalidation_metadata")
    physical_profile(physical, "8.1.5.60", "revalidation_physical")
    need(all(type(physical.get(key)) is type(value) and physical.get(key) == value
             for key, value in ADVISORY_PHYSICAL.items()), "revalidation_exact_physical_scope")
    prior = C.load_json(indexed["failure"])
    need(prior.get("status") == "validation_failed" and prior.get("candidate_acceptance") is False
        and prior.get("full_temporal_acceptance") is False and prior.get("gate") == "candidate", "producer_original_failure")
    C.object_value(C.load_json(indexed["runtime_summary"]), "producer_runtime_summary")
    rules_path = _external_receipt(lineage["rules_manifest"], "rules_manifest")
    rules = C.load_json(rules_path); _types(rules)
    need(set(rules) == {"schema_version", "task", "scope", "rule_id", "root_approved", "root_acceptance_id",
        "physical", "parameter_query", "allowed_mismatches", "producer_policy_sha256", "reviewed_patch", "reviewed_design_sha256"},
        "rules_manifest_fields")
    exact_int(rules["schema_version"], 1, "rules_schema")
    need(rules["task"] == "TP06" and rules["scope"] == ADVISORY_SCOPE and rules["rule_id"] == ADVISORY_RULE_ID, "rules_scope")
    yes(rules["root_approved"], "rules_root_approval")
    C.string(rules["root_acceptance_id"], "rules_root_acceptance_id")
    need(rules["physical"] == ADVISORY_PHYSICAL and rules["allowed_mismatches"] == ADVISORY_MISMATCHES, "rules_cannot_expand_scope")
    for pair in rules["allowed_mismatches"]:
        for values in pair.values():
            need(type(values) is list and all(type(value) is int for value in values), "rules_tuple_integer_types")
    need(rules["reviewed_design_sha256"] == "1e0926b03501ee8072cddeb7a6e348f01020b3d7539732328701c3702ec2151e", "rules_reviewed_design")
    _external_receipt(rules["reviewed_patch"], "rules_reviewed_patch")
    query = rules["parameter_query"]
    need(query == {"program_path": "build/Program.cs", "program_sha256": PARAMETER_QUERY_PROGRAM_SHA256,
        "source_method": "ParameterQuery(bool tz)"}, "rules_ParameterQuery_binding")
    need(C.digest(_contained_file(directory, "build/Program.cs", "producer_Program")) == PARAMETER_QUERY_PROGRAM_SHA256, "producer_ParameterQuery_hash")
    old_policy = C.digest(directory / "frozen-input/temporal-precision.validate.py")
    need(C.hash_value(lineage["producer_policy_sha256"], "producer_policy_sha256") == old_policy
        == rules["producer_policy_sha256"], "producer_policy_binding")
    verifier_root = Path(C.string(lineage["verifier_root"], "verifier_root"))
    need(verifier_root.is_absolute() and verifier_root.is_dir(), "verifier_root_absolute")
    verifier_root = verifier_root.resolve()
    files = C.array(lineage["verifier_files"], "verifier_files", nonempty=True)
    need(len(files) == len(REVALIDATION_FILES) and {item.get("path") for item in files} == set(REVALIDATION_FILES), "verifier_exact_files")
    verified = {}
    for item in files:
        need(type(item) is dict and set(item) == {"path", "sha256"}, "verifier_file_fields")
        path = _contained_file(verifier_root, item["path"], "verifier_file_path")
        digest = C.hash_value(item["sha256"], "verifier_file_sha256")
        need(C.digest(path) == digest, "verifier_file_hash:" + item["path"])
        verified[item["path"]] = digest
    need((verifier_root / "eng/temporal-precision.validate.py").resolve() == Path(__file__).resolve(), "verifier_self_path")
    need((verifier_root / "tools/TemporalBoundaryProbe/contracts.py").resolve() == _contract_path.resolve()
        and (verifier_root / "tools/TemporalBoundaryProbe/packed_oracle.py").resolve() == _packed_path.resolve(), "verifier_dependency_paths")
    # This overlay only replaces the verifier. Both imported policy dependencies remain
    # exactly the versions frozen by the original producer, and every old tool is checked below.
    need(verified["tools/TemporalBoundaryProbe/contracts.py"] == C.digest(directory / "build/contracts.py")
        and verified["tools/TemporalBoundaryProbe/packed_oracle.py"] == C.digest(directory / "build/packed_oracle.py"), "verifier_dependencies_changed")
    need(verified["tools/TemporalBoundaryProbe/Program.cs"] == PARAMETER_QUERY_PROGRAM_SHA256, "verifier_Program_changed")
    frozen = C.load_json(indexed["frozen_manifest"])
    tool_paths = {entry.get("path") for entry in C.array(frozen.get("tools"), "producer_tools")}
    need("build/Program.cs" in tool_paths, "producer_Program_manifest_required")
    identity = C.load_json(indexed["identities"])
    return dict(manifest_path=str(manifest_path), manifest_sha256=expected_manifest,
        rules_manifest=dict(path=str(rules_path), sha256=lineage["rules_manifest"]["sha256"]),
        producer_revision=dict(policy_sha256=old_policy, source_head=identity["producer"]["source_head"],
            product_version=identity["W"]["version"], source_manifest_sha256=C.digest(indexed["product_source_manifest"])),
        verifier_revision=dict(rule_id=ADVISORY_RULE_ID, root_acceptance_id=rules["root_acceptance_id"], files=verified),
        producer_inputs=records, producer_evidence_root=str(root), producer_directory=str(directory), verifier_root=str(verifier_root),
        reviewed_patch=rules["reviewed_patch"])


def _check_lineage_inputs(lineage):
    root = Path(lineage["producer_evidence_root"])
    for receipt in lineage["producer_inputs"]:
        need(C.digest(_contained_file(root, receipt["path"], "producer_recheck_path")) == receipt["sha256"], "producer_changed_during_revalidation")
    need(C.digest(lineage["manifest_path"]) == lineage["manifest_sha256"], "lineage_changed_during_revalidation")
    need(C.digest(lineage["rules_manifest"]["path"]) == lineage["rules_manifest"]["sha256"], "rules_changed_during_revalidation")
    _external_receipt(lineage["reviewed_patch"], "reviewed_patch_recheck")
    for relative, digest in lineage["verifier_revision"]["files"].items():
        need(C.digest(_contained_file(Path(lineage["verifier_root"]), relative, "verifier_recheck_path")) == digest,
            "verifier_changed_during_revalidation")
    producer = Path(lineage["producer_directory"])
    frozen = C.load_json(producer / "frozen-input/manifest.json")
    for tool in frozen["tools"]:
        need(C.digest(_contained_file(producer, tool["path"], "producer_tool_recheck_path")) == tool["sha256"],
            "producer_tool_changed_during_revalidation")
    runner = C.load_json(producer / "runner.json")
    for key, relative in (("assets_sha256", "build/obj/project.assets.json"), ("lock_sha256", "build/packages.lock.json"),
                          ("tool_dll_sha256", "build/bin/Debug/net10.0/TemporalBoundaryProbe.dll")):
        need(C.digest(_contained_file(producer, relative, "producer_build_recheck_path")) == runner[key],
            "producer_build_changed_during_revalidation")


def _validate(directory, gate, lineage=None):
    need(gate in ("characterization", "candidate"), "gate_enum")
    directory = Path(directory)
    read = lambda path: C.load_json(directory / path)
    runner, frozen, identity, source = (read(p) for p in ("runner.json", "frozen-input/manifest.json", "frozen-input/identities.json", "frozen-input/product-source-manifest.json"))
    for item in (runner, frozen, identity, source): _types(item)
    C.validate_identity(identity)
    profile = C.enum(get(runner, "profile", "runner"), C.PROFILES, "runner.profile")
    true_phase = C.enum(get(runner, "phase", "runner"), C.PHASES, "runner.phase")
    need(true_phase == identity["phase"] == get(frozen, "phase", "frozen"), "phase_mismatch")
    need(identity["phase"] == ("candidate" if gate == "candidate" else "baseline"), "baseline_cannot_accept_candidate")
    exact_int(get(runner, "schema_version", "runner"), 1, "runner.schema")
    exact_int(get(frozen, "schema_version", "frozen"), 1, "frozen.schema")
    need(get(runner, "status", "runner") == "execution_complete", "runner_not_complete")
    hashes = {"identity_sha256": C.digest(directory / "frozen-input/identities.json"),
              "source_manifest_sha256": C.digest(directory / "frozen-input/manifest.json"),
              "product_source_manifest_sha256": C.digest(directory / "frozen-input/product-source-manifest.json")}
    for key, actual in hashes.items(): need(get(runner, key, "runner") == actual, "runner_hash:" + key)
    need(get(frozen, "identity_sha256", "frozen") == hashes["identity_sha256"], "frozen_identity_hash")
    need(get(frozen, "product_source_manifest_sha256", "frozen") == hashes["product_source_manifest_sha256"], "frozen_product_hash")
    archive = directory / "frozen-input/product-source.tar"
    C.validate_producer(identity, hashes["product_source_manifest_sha256"], source, C.digest(archive) if archive.is_file() else None)
    tools = C.array(get(frozen, "tools", "frozen"), "frozen.tools", nonempty=True)
    tool_paths = [file.get("path") for file in tools]
    need(len(set(tool_paths)) == len(tool_paths), "frozen_duplicate_tool")
    need("build/contracts.py" in tool_paths and "build/packed_oracle.py" in tool_paths and "frozen-input/temporal-precision.validate.py" in tool_paths, "frozen_validator_policy_required")
    need(C.digest(directory / "build/contracts.py") == C.digest(_contract_path), "frozen_contract_policy_differs")
    need(C.digest(directory / "build/packed_oracle.py") == C.digest(_packed_path), "frozen_packed_oracle_policy_differs")
    need(C.digest(directory / "frozen-input/temporal-precision.validate.py") ==
        (lineage["producer_revision"]["policy_sha256"] if lineage is not None else C.digest(Path(__file__))), "frozen_validator_policy_differs")
    for file in tools:
        path = C.relative_path(get(file, "path", "frozen.tool"), "frozen.tool.path")
        actual_path = _contained_file(directory.resolve(), path, "frozen.tool.path")
        need(C.digest(actual_path) == C.hash_value(get(file, "sha256", "frozen.tool"), "tool.hash"), "frozen_tool_changed")
    for driver in ("O", "W"):
        need(get(frozen, "packages", "frozen")[driver] == identity[driver]["package_sha256"], "frozen_package_binding")
    zero(get(runner, "restore_exit_code", "runner"), "restore_failed")
    zero(get(runner, "build_exit_code", "runner"), "build_failed")
    for key, path in (("assets_sha256", "build/obj/project.assets.json"), ("lock_sha256", "build/packages.lock.json"),
                      ("tool_dll_sha256", "build/bin/Debug/net10.0/TemporalBoundaryProbe.dll")):
        need(get(runner, key, "runner") == C.digest(directory / path), "build_identity:" + key)
        if path.endswith(".json"):
            _types(read(path), path)
    capability = dict(seven_write=profile == "shared", write_scales=list(range(8 if profile == "shared" else 7)),
        local_ticks_min=0, local_ticks_max=C.MAX_TICKS, utc_ticks_min=0, utc_ticks_max=C.MAX_TICKS,
        offset_min_minutes=-779, offset_max_minutes=840)
    if gate == "candidate":
        cap = read("frozen-input/capabilities.json"); _types(cap)
        capability = C.validate_capabilities(cap, identity, profile)
        cap_sha = C.digest(directory / "frozen-input/capabilities.json")
        need(get(runner, "capabilities_sha256", "runner") == get(frozen, "capabilities_sha256", "frozen") == cap_sha, "capabilities_hash")
    all_cases = {}
    advisory_context = None
    runs = C.array(get(runner, "runs", "runner"), "runner.runs")
    need(len(runs) == 3 and {r.get("lane") for r in runs} == set(C.LANES), "all_lanes_required")
    for run in runs:
        lane = C.enum(get(run, "lane", "run"), C.LANES, "run.lane")
        zero(get(run, "exit_code", lane), lane + ":execution_failed")
        need(C.boolean(get(run, "hard_deadline", lane), lane + ":hard_deadline") is False, lane + ":deadline")
        zero(get(run, "cleanup_exit_code", lane), lane + ":cleanup_failed")
        need(C.boolean(get(run, "cleanup_hard_deadline", lane), lane + ":cleanup_deadline") is False, lane + ":cleanup_deadline")
        report, cleanup, ownership = (read(lane + suffix) for suffix in (".json", ".cleanup.json", ".ownership.json"))
        for item in (report, cleanup, ownership): _types(item)
        need(C.digest(directory / (lane + ".json")) == get(run, "report_sha256", lane), lane + ":report_hash")
        for item, prefix in ((report, lane), (cleanup, lane + ":cleanup")):
            exact_int(get(item, "schema_version", prefix), 1, prefix + ":schema")
            need(get(item, "phase", prefix) == true_phase and get(item, "profile", prefix) == profile and get(item, "lane", prefix) == lane, prefix + ":report_enums")
            zero(get(item, "process_exit_code", prefix), prefix + ":exit")
            yes(get(item, "server_identity_verified", prefix), prefix + ":TEST_identity")
            yes(get(item, "cleanup_verified", prefix), prefix + ":cleanup")
            need(get(item, "final_database_state", prefix) == "owned_table_absent_fresh_connection", prefix + ":fresh_absence")
            for key in ("identity_sha256", "source_manifest_sha256"):
                need(get(item, key, prefix) == hashes[key], prefix + ":identity_chain")
            need(get(item, "product_source_manifest_sha256", prefix) == hashes["product_source_manifest_sha256"], prefix + ":producer_source_identity")
            physical_profile(get(item, "W_profile", prefix), C.PROFILES[profile], prefix + ":W_profile")
            for driver in ("O", "W"):
                observed = get(item, "identities", prefix)[driver]
                need(all(get(observed, key, prefix) == identity[driver][key] for key in ("id", "version", "asset", "package_sha256", "dll_sha256", "mvid")), prefix + ":loaded_identity")
                yes(get(observed, "package_reference_verified", prefix), prefix + ":package_reference")
                yes(get(observed, "actual_asset_verified", prefix), prefix + ":actual_asset")
        if profile == "shared":
            physical_profile(get(report, "O_profile", lane), C.PROFILES[profile], lane + ":O_profile")
        nonce = C.string(get(ownership, "nonce", lane), lane + ":nonce")
        need(re.fullmatch(r"[A-F0-9]{32}", nonce) is not None, lane + ":nonce")
        table = "TPN_" + nonce[:24]
        need(get(ownership, "table", lane) == get(report, "owned_table", lane) == get(cleanup, "owned_table", lane) == table, lane + ":owned_table")
        yes(get(ownership, "absent_before_create", lane), lane + ":creation_precondition")
        yes(get(ownership, "identity_verified", lane), lane + ":ownership_identity")
        need(get(ownership, "profile", lane) == profile and get(ownership, "server_version", lane) == C.PROFILES[profile], lane + ":ownership_profile")
        need(get(ownership, "owner_package_sha256", lane) == identity["W"]["package_sha256"] and get(ownership, "identity_sha256", lane) == hashes["identity_sha256"], lane + ":ownership_package")
        need(get(ownership, "creation_state", lane) == "confirmed", lane + ":creation_not_confirmed")
        need(get(ownership, "owner_marker", lane) == "OWN_" + nonce[:24], lane + ":owner_marker")
        all_cases[lane] = case_index(report, lane)
        if lane == "metadata":
            program = directory / "build/Program.cs"
            advisory_context = dict(profile=profile, physical=get(report, "W_profile", lane),
                program_sha256=C.digest(program) if program.is_file() and "build/Program.cs" in tool_paths else None)
    candidate = gate == "candidate"
    validate_core(all_cases["core"], profile, capability, candidate)
    validate_metadata(all_cases["metadata"], profile, capability, candidate, advisory_context)
    validate_extended(all_cases["extended"], profile, capability, candidate)
    simple_layouts = validate_simple_layouts(all_cases["extended"], ("O", "W") if profile == "shared" else ("W",), capability, candidate)
    return dict(schema_version=1, task="TP-01", gate=gate, profile=profile,
        status="candidate_profile_accepted" if candidate else "baseline_characterization_accepted",
        candidate_acceptance=candidate, full_temporal_acceptance=False,
        baseline_capability_acceptance=False, baseline_unverified_observations=not candidate,
        wire_layout_verified=candidate and all(value == "sampled_ordinary_layout_verified" for value in simple_layouts.values()), simple_layout_classifications=simple_layouts, issues=[], case_counts={k: len(v) for k, v in all_cases.items()},
        runner_sha256=C.digest(directory / "runner.json"))


def validate(directory, gate, revalidation_manifest=None, revalidation_manifest_sha256=None, output=None):
    lineage = None
    try:
        need((revalidation_manifest is None) == (revalidation_manifest_sha256 is None), "revalidation_arguments_must_be_paired")
        if revalidation_manifest is not None:
            lineage = revalidation_lineage(directory, gate, revalidation_manifest, revalidation_manifest_sha256, output)
        result = _validate(Path(directory).resolve(), gate, lineage)
    except C.ContractError as error:
        result = dict(schema_version=1, task="TP-01", gate=gate, status="validation_failed",
                    candidate_acceptance=False, full_temporal_acceptance=False, issues=[str(error)])
    except Exception as error:
        # Never copy native/producer exception text into artifacts.
        result = dict(schema_version=1, task="TP-01", gate=gate, status="validation_failed",
                    candidate_acceptance=False, full_temporal_acceptance=False, issues=["artifact_error:" + type(error).__name__])
    if lineage is not None:
        try:
            _check_lineage_inputs(lineage)
        except Exception as error:
            result.update(status="validation_failed", candidate_acceptance=False,
                issues=result["issues"] + [str(error) if isinstance(error, C.ContractError) else "lineage_recheck_error:" + type(error).__name__])
        result.update(task="TP06", revalidation=True, producer_revision=lineage["producer_revision"],
            verifier_revision=lineage["verifier_revision"], revalidation_manifest_sha256=lineage["manifest_sha256"],
            rules_manifest=lineage["rules_manifest"], producer_inputs=lineage["producer_inputs"],
            original_failure_retained=True, original_failure_sha256=next(item["sha256"] for item in lineage["producer_inputs"] if item["role"] == "failure"),
            database_accessed=False, tls_candidate_acceptance=False)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--gate", choices=("characterization", "candidate"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--revalidation-manifest", type=Path)
    parser.add_argument("--revalidation-manifest-sha256")
    args = parser.parse_args()
    if (args.revalidation_manifest is None) != (args.revalidation_manifest_sha256 is None):
        parser.error("--revalidation-manifest and --revalidation-manifest-sha256 must be supplied together")
    if args.output.exists():
        print('{"status":"validator_refuses_existing_output"}')
        return 1
    if args.revalidation_manifest is not None:
        try:
            _fresh_output(args.directory.resolve(), args.output)
        except C.ContractError as error:
            print(json.dumps(dict(status="validation_failed", issues=[str(error)])))
            return 1
    result = validate(args.directory.resolve(), args.gate, args.revalidation_manifest,
        args.revalidation_manifest_sha256, args.output)
    # Exclusive creation protects both the legacy entry and the revalidation entry from overwrite.
    with args.output.open("x") as file:
        file.write(json.dumps(result, indent=2) + "\n")
    output_sha256 = C.digest(args.output)
    if args.revalidation_manifest is not None:
        receipt = dict(schema_version=1, task="TP06", output=dict(path=str(args.output.resolve()), sha256=output_sha256),
            revalidation_manifest=dict(path=str(args.revalidation_manifest.resolve()),
                sha256=C.digest(args.revalidation_manifest)), root_frozen_manifest_sha256=args.revalidation_manifest_sha256,
            producer_revision=result.get("producer_revision"), verifier_revision=result.get("verifier_revision"),
            rules_manifest=result.get("rules_manifest"),
            original_failure_sha256=result.get("original_failure_sha256"), producer_inputs=result.get("producer_inputs"),
            status=result["status"], candidate_acceptance=result["candidate_acceptance"], full_temporal_acceptance=False)
        with args.output.with_name(args.output.name + ".lineage.json").open("x") as file:
            file.write(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(dict(status=result["status"], issue_count=len(result["issues"]), output_sha256=output_sha256)))
    return 0 if result["status"] in ("baseline_characterization_accepted", "candidate_profile_accepted") else 1


if __name__ == "__main__":
    sys.exit(main())
