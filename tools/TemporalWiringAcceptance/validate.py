#!/usr/bin/env python3
"""TP03 shared writer/reader wiring only. Reviewed TP01 v11 policy is reused unchanged."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

SCOPE = "TP03_writer_reader_wiring"
PINS = {
    "frozen-input/temporal-precision.validate.py": "7b4f23ee1281bfe4e8bc1a5004f39bcab8b5dce27072df1ce4e00d79ef23e72f",
    "build/contracts.py": "f8ad9abddd86e415374b9594954f2264fc5d701e3f603c7a96ed77e14dbca6cb",
    "build/packed_oracle.py": "bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9",
}


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def frozen_policy(directory):
    directory = Path(directory).resolve()
    # Validate executable Python bytes before import; do not let the old loader choose a shadow path.
    for name, expected in PINS.items():
        if digest(directory / name) != expected:
            raise ValueError("reviewed_policy_hash_mismatch")
    if (directory / "tools/TemporalBoundaryProbe/contracts.py").exists() or (directory / "tools/TemporalBoundaryProbe/packed_oracle.py").exists():
        raise ValueError("shadow_policy_forbidden")
    spec = importlib.util.spec_from_file_location("tp03_frozen_tp01_policy", directory / "frozen-input/temporal-precision.validate.py")
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    if module._contract_path.resolve() != directory / "build/contracts.py" or module._packed_path.resolve() != directory / "build/packed_oracle.py":
        raise ValueError("frozen_dependency_path_mismatch")
    return module


def strict_core(V, index, capability):
    """Every W input comes from the reviewed independent spec; no baseline-outcome shortcut."""
    C = V.C
    for driver in ("O", "W"):
        V.column_observation(V.at(index, driver, "columns_6_7", "core"), [16, 23, 26, 27], [6, 6, 7, 7], driver + ":columns", False)
        for column, ctype, scale in (("D6", 16, 6), ("Z6", 23, 6), ("D7", 26, 7), ("Z7", 27, 7)):
            V.column_observation(V.at(index, driver, "column_" + column, "core"), [ctype], [scale], driver + ":" + column, False)
    specs = C.core_specs()
    for case_id, spec in specs.items():
        case = V.at(index, "W", case_id, "core"); code = "W:" + case_id
        if spec.get("constructor"):
            V.write_outcome(case, spec, capability, code, ("O", "W"), True)
            continue
        value, scale = spec["value"], spec["scale"]
        V.requested(case, value, code)
        V.need(case.get("requested_db_type") == ("DateTimeOffset" if value["offset_minutes"] is not None else "DateTime2"), code + ":DbType")
        V.exact_int(case.get("requested_scale"), scale, code + ":Scale")
        V.need(case.get("requested_precision") is None and case.get("write_path") == "fixed_column", code + ":request_source")
        V.exact_int(case.get("column_scale"), scale, code + ":column_scale")
        V.need(case.get("target_column") == ("Z" if value["offset_minutes"] is not None else "D") + str(scale), code + ":physical_target")
        ctype = (23 if scale == 6 else 27) if value["offset_minutes"] is not None else (16 if scale == 6 else 26)
        V.metadata_list(case, "column_metadata", [ctype], [scale], code + ":fixed_column", False)
        V.need(case.get("persistence_source") == "fresh_connection_row_count", code + ":fresh_persistence_source")
        # Complete CLR domains are fixed. Out-of-profile offsets and six-digit non-microseconds refuse exactly.
        ticks, offset = value["ticks"], value["offset_minutes"]
        error = None
        if offset is not None and not -779 <= offset <= 840:
            error = "System.OverflowException"
        elif not 0 <= ticks - (offset or 0) * C.TICKS_MINUTE <= C.MAX_TICKS:
            error = "System.OverflowException"
        elif ticks % (10 ** max(0, 7 - scale)):
            error = "System.OverflowException"
        if error:
            V.reject(case, error, code)
        else:
            V.need(case.get("status") == "written", code + ":required_exact_success")
            V.yes(case.get("execute_attempted"), code + ":actual_execution")
            V.exact_int(case.get("affected_rows"), 1, code + ":affected_rows")
            V.exact_int(case.get("persisted_rows"), 1, code + ":fresh_row")
            V.exact_oracles(case, value, code, ("O", "W"), scale, False)
            exact_actual_raw(V, case, value, scale, code)
        opposing = case_id.replace("local_", "constructor_").replace("utc_", "utc_constructor_")
        if opposing != case_id:
            V.need(("W", opposing) not in index, code + ":spurious_constructor_rejection")
    # Historical O failures remain characterized, and six required ordinary seeds still prove O->W.
    for case_id, spec in specs.items():
        V.write_outcome(V.at(index, "O", case_id, "core"), spec, capability, "O:" + case_id, ("O", "W"), False)
    for case_id in ("dt7_ordinary_tick1", "dt7_ordinary_micro", "offset_0", "offset_480", "offset_N300", "offset_330"):
        case = V.at(index, "O", case_id, "core"); spec = specs[case_id]["value"]
        V.need(case.get("status") == "written", "mandatory_O_seed:" + case_id)
        V.exact_int(case.get("affected_rows"), 1, "mandatory_O_seed:affected:" + case_id)
        V.need(case.get("persistence_source") == "fresh_connection_row_count", "mandatory_O_seed:fresh:" + case_id)
        V.exact_oracles(case, spec, "O_to_W:" + case_id, ("O", "W"), 7, False)
        exact_actual_raw(V, case, spec, 7, "O_to_W:" + case_id)
    for driver in ("O", "W"):
        rollback = V.at(index, driver, "typed_dml_rollback", "core")
        V.need(rollback.get("status") == "rollback_verified", driver + ":rollback")
        V.zero(rollback.get("fresh_rows"), driver + ":rollback_fresh_rows")


def exact_actual_raw(V, case, value, scale, code):
    ctype = (23 if scale == 6 else 27) if value["offset_minutes"] is not None else (16 if scale == 6 else 26)
    length = {16: 8, 23: 10, 26: 9, 27: 11}[ctype]
    for observed in case["oracles"]:
        prefix = code + ":" + observed["reader_lane"]
        V.metadata_list(observed, "metadata", [ctype], [scale], prefix, candidate=False, fixed=None)
        schema = V.C.array(observed.get("schema_scales"), prefix + ":schema")
        V.need(len(schema) == 1, prefix + ":schema_count")
        V.exact_int(schema[0], scale, prefix + ":schema_scale")
        V.cell(observed, ctype, scale, prefix, True)
        V.exact_int(observed["cell"]["payload_length"], length, prefix + ":normal_payload_only")
        V.exact_raw_components(observed, value, prefix)  # Includes actual metadata/hash/bytes, expected and FF9/FF7.


def _validate(directory, package_inspector=None):
    directory = Path(directory).resolve(); V = frozen_policy(directory); C = V.C
    synthetic = package_inspector is not None
    read = lambda name: C.load_json(directory / name)
    runner, frozen, identity, source = (read(name) for name in ("runner.json", "frozen-input/manifest.json", "frozen-input/identities.json", "frozen-input/product-source-manifest.json"))
    for record in (runner, frozen, identity, source): V._types(record)
    C.validate_identity(identity)
    V.need(re.fullmatch(r"[a-f0-9]{40}", C.string(identity.get("comparison_head"), "comparison_head")) is not None, "comparison_head_required")
    V.need(runner.get("profile") == "shared" and runner.get("phase") == identity.get("phase") == frozen.get("phase") == "candidate", "shared_unique_candidate_required")
    V.exact_int(runner.get("schema_version"), 1, "runner.schema"); V.exact_int(frozen.get("schema_version"), 1, "frozen.schema")
    V.need(runner.get("status") == "execution_complete", "runner_not_complete")
    hashes = {"identity_sha256": digest(directory / "frozen-input/identities.json"), "source_manifest_sha256": digest(directory / "frozen-input/manifest.json"), "product_source_manifest_sha256": digest(directory / "frozen-input/product-source-manifest.json")}
    for key, value in hashes.items(): V.need(runner.get(key) == value, "runner_hash:" + key)
    for key in ("identity_sha256", "product_source_manifest_sha256"): V.need(frozen.get(key) == hashes[key], "freeze_hash:" + key)
    archive = directory / "frozen-input/product-source.tar"
    V.need(archive.is_file(), "candidate_source_archive_required")
    producer = C.object_value(identity.get("producer"), "producer")
    archive_sha = C.hash_value(producer.get("source_archive_sha256"), "producer_archive_required")
    V.need(C.hash_value(frozen.get("source_archive_sha256"), "freeze_archive_required") == archive_sha == digest(archive), "candidate_source_archive_binding")
    C.validate_producer(identity, hashes["product_source_manifest_sha256"], source, archive_sha)
    V.need(frozen.get("source_commit") == identity["producer"]["source_head"] and frozen.get("producer") == identity["producer"], "freeze_producer_binding")
    V.need(frozen.get("source_archive_sha256") == identity["producer"]["source_archive_sha256"], "freeze_archive_binding")
    V.need(frozen.get("comparison_head") == identity.get("comparison_head"), "freeze_comparison_binding")
    tools = C.array(frozen.get("tools"), "tools", nonempty=True)
    paths = [entry.get("path") for entry in tools]
    V.need(len(paths) == len(set(paths)) and set(PINS) <= set(paths), "tool_policy_bundle")
    for entry in tools:
        path = C.relative_path(entry.get("path"), "tool.path")
        V.need(digest(directory / path) == C.hash_value(entry.get("sha256"), "tool.sha"), "tool_input_changed")
    for key, name in (("assets_sha256", "build/obj/project.assets.json"), ("lock_sha256", "build/packages.lock.json"), ("tool_dll_sha256", "build/bin/Debug/net10.0/TemporalBoundaryProbe.dll")):
        V.need(runner.get(key) == digest(directory / name), "build_identity:" + key)
    for key in ("restore_exit_code", "build_exit_code", "offline_exit_code"): V.zero(runner.get(key), key)
    V.need(C.boolean(runner.get("offline_deadline"), "offline.deadline") is False, "offline_deadline")
    offline, shape = read("offline-contracts.json"), read("offline-producer-shape.json")
    for item in (offline, shape): V._types(item)
    V.need(offline.get("status") == "offline_contracts_passed" and offline.get("expected_only") is True and offline.get("database_accessed") is False, "actual_offline_required")
    V.need(runner.get("offline_report_sha256") == digest(directory / "offline-contracts.json") and runner.get("offline_shape_sha256") == offline.get("shape_sha256") == digest(directory / "offline-producer-shape.json"), "offline_hash_chain")
    checks = C.array(offline.get("cases"), "offline.cases", nonempty=True)
    V.need(all(C.boolean(item.get("passed"), "offline.passed") is True for item in checks), "offline_case_failed")
    V.need(V.validate_producer_shape(shape).get("status") == "producer_shape_contract_verified", "actual_CSharp_shape_required")
    cap_doc = read("frozen-input/capabilities.json"); V._types(cap_doc); cap = C.validate_capabilities(cap_doc, identity, "shared")
    V.need(runner.get("capabilities_sha256") == frozen.get("capabilities_sha256") == digest(directory / "frozen-input/capabilities.json"), "capability_hash")
    V.need(cap["seven_write"] is True and cap["write_scales"] == list(range(8)), "fixed_shared_write_scales")
    for key, value in (("local_ticks_min", 0), ("utc_ticks_min", 0), ("local_ticks_max", C.MAX_TICKS), ("utc_ticks_max", C.MAX_TICKS), ("offset_min_minutes", -779), ("offset_max_minutes", 840)):
        V.exact_int(cap.get(key), value, "fixed_profile_domain:" + key)
    assets, lock, deps = read("build/obj/project.assets.json"), read("build/packages.lock.json"), read("build/bin/Debug/net10.0/TemporalBoundaryProbe.deps.json")
    for item in (assets, lock, deps): V._types(item)
    package_receipts = {}
    for driver in ("O", "W"):
        e = identity[driver]; key = e["id"] + "/" + e["version"]
        V.need(frozen.get("packages", {}).get(driver) == e["package_sha256"], "frozen_package")
        package = directory / "frozen-input/feed" / (e["id"] + "." + e["version"] + ".nupkg").lower()
        dll = directory / "build/bin/Debug/net10.0" / Path(e["asset"]).name
        if synthetic:
            package_receipts[driver] = package_inspector(package, e, dll)
        else:
            package_receipts[driver] = C.validate_package_identity(package, e)
            V.need(digest(dll) == e["dll_sha256"], "actual_loaded_DLL_file")
            V.need(C._managed_mvid(dll.read_bytes()) == e["mvid"], "actual_loaded_DLL_MVID")
        V.need(package_receipts[driver] == {k:e[k] for k in ("package_sha256", "dll_sha256", "mvid")}, "package_receipt")
        V.need(assets["libraries"][key]["type"] == deps["libraries"][key]["type"] == "package", "actual_PackageReference")
        for document in (assets, deps):
            V.need(any(key in target and e["asset"] in target[key].get("runtime", {}) for target in document["targets"].values()), "actual_selected_asset")
        references = [target[e["id"]] for target in lock.get("dependencies", {}).values() if e["id"] in target]
        V.need(bool(references) and all(record.get("resolved") == e["version"] for record in references), "package_lock_version")
    runs = C.array(runner.get("runs"), "runs")
    V.need(len(runs) == 1 and runs[0].get("lane") == "core", "exact_core_only_run")
    run = runs[0]; V._types(run)
    V.yes(run.get("ownership_manifest_present"), "actual_ownership_receipt")
    V.need(run.get("recovery_manifest") == "core.ownership.json" and run.get("creation_state") == "confirmed", "exact_recovery_receipt")
    for key in ("exit_code", "cleanup_exit_code"): V.zero(run.get(key), key)
    for key in ("hard_deadline", "cleanup_hard_deadline"): V.need(C.boolean(run.get(key), key) is False, key)
    report, cleanup, ownership = (read("core" + suffix) for suffix in (".json", ".cleanup.json", ".ownership.json"))
    for item in (report, cleanup, ownership): V._types(item)
    V.exact_int(ownership.get("schema_version"), 1, "ownership.schema")
    V.need(run.get("report_sha256") == digest(directory / "core.json") and run.get("ownership_manifest_sha256") == digest(directory / "core.ownership.json"), "actual_run_receipts")
    for item in (report, cleanup):
        V.exact_int(item.get("schema_version"), 1, "report.schema")
        V.need(item.get("phase") == "candidate" and item.get("profile") == "shared" and item.get("lane") == "core", "report_scope")
        V.need(item.get("mode") == ("run" if item is report else "cleanup"), "actual_process_mode")
        V.need(item.get("producer_source_head") == identity["producer"]["source_head"] and item.get("comparison_head") == identity.get("comparison_head"), "actual_source_head_receipt")
        V.zero(item.get("process_exit_code"), "report.exit")
        for key in ("server_identity_verified", "cleanup_verified"): V.yes(item.get(key), key)
        V.need(item.get("final_database_state") == "owned_table_absent_fresh_connection", "fresh_absence")
        for key, value in hashes.items(): V.need(item.get(key) == value, "report_identity_chain:" + key)
        profile = item.get("W_profile", {})
        V.physical_profile(profile, "8.1.5.60", "actual_W_physical_profile")
        V.need(profile["encoding"] == profile["negotiated_encoding"] == "utf-8", "fixed_actual_shared_encoding")
        V.exact_int(profile.get("message_version"), 21, "message_version")
        for driver in ("O", "W"):
            actual = item.get("identities", {}).get(driver, {})
            V.need(all(actual.get(key) == identity[driver][key] for key in ("id", "version", "asset", "package_sha256", "dll_sha256", "mvid")), "actual_loaded_receipt")
            V.yes(actual.get("package_reference_verified"), "PackageReference_receipt"); V.yes(actual.get("actual_asset_verified"), "asset_receipt")
    op = report.get("O_profile", {})
    V.physical_profile(op, "8.1.5.60", "actual_O_physical_profile")
    V.need(op["encoding"] == op["negotiated_encoding"] == "utf-8", "fixed_actual_O_shared_encoding")
    V.exact_int(op["message_version"], 21, "official_message_version")
    nonce = C.string(ownership.get("nonce"), "nonce")
    V.need(re.fullmatch(r"[A-F0-9]{32}", nonce) is not None, "nonce")
    V.need(ownership.get("table") == report.get("owned_table") == cleanup.get("owned_table") == "TPN_" + nonce[:24] and ownership.get("owner_marker") == "OWN_" + nonce[:24], "exact_owned_object")
    for key in ("absent_before_create", "identity_verified"): V.yes(ownership.get(key), key)
    V.need(ownership.get("creation_state") == "confirmed" and ownership.get("profile") == "shared" and ownership.get("server_version") == "8.1.5.60" and ownership.get("owner_package_sha256") == identity["W"]["package_sha256"] and ownership.get("identity_sha256") == hashes["identity_sha256"], "ownership_origin")
    strict_core(V, V.case_index(report, "core"), cap)
    return dict(schema_version=1, scope=SCOPE, status="synthetic_wiring_policy_verified" if synthetic else "tp03_wiring_accepted", synthetic_expected_only=synthetic,
        tp03_wiring_accepted=not synthetic, candidate_acceptance=False, full_temporal_acceptance=False, tp04_binding_acceptance=False, tp05_complete=False,
        policy_sha256=digest(Path(__file__)), dependency_sha256=PINS, runner_sha256=digest(directory / "runner.json"), issues=[])


def validate(directory, *, _package_inspector=None):
    """Test seam is not reachable from CLI; it never produces a production accepted result."""
    try: return _validate(directory, _package_inspector)
    except Exception as error:
        code = str(error) if type(error).__name__ in ("ContractError", "PacketError") else type(error).__name__
        return dict(schema_version=1, scope=SCOPE, status="validation_failed", tp03_wiring_accepted=False, candidate_acceptance=False,
                    synthetic_expected_only=_package_inspector is not None, full_temporal_acceptance=False, tp04_binding_acceptance=False, tp05_complete=False, issues=[code])


def main():
    p = argparse.ArgumentParser(description=__doc__); p.add_argument("directory", type=Path); p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    if a.output.exists(): return 1
    result = validate(a.directory); a.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({"status":result["status"], "scope":SCOPE}))
    return 0 if result["tp03_wiring_accepted"] else 1


if __name__ == "__main__": sys.exit(main())
