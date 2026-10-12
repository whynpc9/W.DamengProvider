#!/usr/bin/env python3
"""Fail-closed TP06 supplemental artifact validator. Read-only; no managed code/DB."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import sys

_policy_path = Path(__file__).resolve().with_name("contracts.py")
_spec = importlib.util.spec_from_file_location("tp06_policy", _policy_path)
C = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(C)


def physical_profile(value, profile):
    expected = dict(shared=("8.1.5.60", "utf-8", 21), tls=("8.1.4.6", "gb18030", 11))[profile]
    C.require(type(value) is dict, "physical_profile_required")
    for key, wanted in dict(server_version=expected[0], encoding=expected[1], negotiated_encoding=expected[1],
        message_version=expected[2], encoding_override_empty=True, result_format="ordinary_non_BDTA",
        server_user="WDM_PROVIDER_TEST", server_schema="WDM_PROVIDER_TEST",
        profile_origin="physical_get_conn_instance_conn_property",
        negotiated_encoding_origin="physical_conn_property_serverEncoding_field").items():
        C.same(value.get(key), wanted, "physical:" + key)
    C.integer(value.get("timezone"), "physical_timezone")
    C.require(-1440 <= value["timezone"] <= 1440, "physical_timezone_domain")


def cleanup_evidence(value, ownership, *, main):
    C.require(value.get("cleanup_verified") is True and value.get("server_identity_verified") is True
        and value.get("final_database_state") == "owned_table_absent_fresh_connection", "cleanup_final_state")
    receipt = value.get("cleanup_receipt")
    C.require(type(receipt) is dict, "cleanup_real_receipt_required")
    for key in ("identity_user", "identity_schema", "fresh_identity_user", "fresh_identity_schema"):
        C.require(receipt.get(key) == "WDM_PROVIDER_TEST", "cleanup_TEST_identity")
    C.same(receipt.get("table_observed"), ownership["table"], "cleanup_owned_table")
    C.require(receipt.get("guard_verified") is True, "cleanup_guard")
    exists = receipt.get("exists_before_drop")
    C.require(type(exists) is bool, "cleanup_exists_receipt")
    C.same(receipt.get("drops"), 1 if exists else 0, "cleanup_exact_drop_count")
    C.same(receipt.get("marker_observed"), ownership["owner_marker"] if exists else None, "cleanup_exact_marker")
    if main:
        C.require(exists, "main_cleanup_own_created_table")
    C.same(receipt.get("fresh_table_count"), 0, "cleanup_fresh_absence_count")
    fresh = receipt.get("fresh_connection_id")
    current = receipt.get("cleanup_connection_id")
    C.require(type(fresh) is str and fresh and fresh != current, "cleanup_fresh_connection")
    C.require(type(current) is str and current, "cleanup_connection_observed")


def package_assets(value, identity, code):
    C.require(type(value) is dict and type(value.get("libraries")) is dict and type(value.get("targets")) is dict, code + ":shape")
    for lane in ("W", "O"):
        expected = identity[lane]
        key = expected["id"] + "/" + expected["version"]
        libraries = value["libraries"]
        C.require(key in libraries and libraries[key].get("type") == "package", code + ":exact_package")
        C.require(not any(name.split("/")[0] == expected["id"] and name != key for name in libraries), code + ":version_drift")
        hits = [target[key] for target in value["targets"].values() if type(target) is dict and key in target]
        C.require(hits and all(type(hit.get("runtime")) is dict and expected["asset"] in hit["runtime"] for hit in hits), code + ":runtime_asset")
        C.require(all(hit.get("type", "package") == "package" for hit in hits), code + ":project_reference")


def _validate(directory):
    root = Path(directory).resolve()
    read = lambda name: C.load_json(root / name)
    runner, frozen, identity = (read(p) for p in ("runner.json", "frozen-input/manifest.json", "frozen-input/identities.json"))
    C.require(runner.get("status") == "execution_complete", "execution_not_complete")
    profile = runner.get("profile")
    C.require(profile in ("shared", "tls"), "profile")
    for value in (runner, frozen, identity):
        C.same(value.get("schema_version"), 1, "artifact_schema")
        C.require(value.get("phase") == "candidate", "candidate_only")
    base, base_path = C.reviewed_module("tp06_v11_identity", "v11-contracts.py", C.BASE_CONTRACT_SHA)
    _, packed_path = C.reviewed_module("tp06_v11_packed", "v11-packed-oracle.py", C.PACKED_SHA)
    base.validate_identity(identity)
    C.same(identity["W"], C.W_PIN, "same_TP06_candidate")
    C.require(identity["producer"]["source_manifest_sha256"] == C.SOURCE_SHA
              and identity["producer"]["source_archive_sha256"] == C.ARCHIVE_SHA, "same_TP06_product_source")
    paths = {"identity_sha256": "frozen-input/identities.json",
             "source_manifest_sha256": "frozen-input/manifest.json",
             "product_source_manifest_sha256": "frozen-input/product-source-manifest.json",
             "capabilities_sha256": "frozen-input/capabilities.json",
             "case_manifest_sha256": "frozen-input/case-manifest.json"}
    hashes = {key: C.digest(root / path) for key, path in paths.items()}
    C.validate_candidate_pins(identity, hashes["identity_sha256"], hashes["product_source_manifest_sha256"],
                              C.digest(root / "frozen-input/product-source.tar"), hashes["capabilities_sha256"])
    for key, sha in hashes.items():
        C.require(runner.get(key) == sha, "runner_input_hash:" + key)
        if key != "source_manifest_sha256":
            C.require(frozen.get(key) == sha, "frozen_input_hash:" + key)
    C.require(hashes["product_source_manifest_sha256"] == C.SOURCE_SHA
              and C.digest(root / "frozen-input/product-source.tar") == C.ARCHIVE_SHA, "product_source_archive_pin")
    base.validate_producer(identity, C.SOURCE_SHA, read("frozen-input/product-source-manifest.json"), C.ARCHIVE_SHA)
    caps = read("frozen-input/capabilities.json")
    cap = base.validate_capabilities(caps, identity, profile)
    C.same(cap["seven_write"], profile == "shared", "physical_seven_capability")
    C.require(set(cap["write_scales"]) == set(range(8 if profile == "shared" else 7)), "physical_write_scales")
    C.same(read("frozen-input/case-manifest.json"), C.manifest(), "fixed_case_manifest")
    tools = frozen.get("tools")
    C.require(type(tools) is list and tools, "frozen_tools_required")
    index = {}
    for entry in tools:
        path = base.relative_path(entry.get("path"), "frozen_tool_path")
        C.require(path not in index, "duplicate_frozen_tool")
        actual = (root / path).resolve()
        C.require(actual.is_relative_to(root), "frozen_tool_outside_root")
        C.require(C.digest(actual) == entry.get("sha256"), "frozen_tool_hash")
        index[path] = entry["sha256"]
    pinned = {"frozen-input/contracts.py": C.digest(_policy_path), "frozen-input/validate.py": C.digest(__file__),
        "frozen-input/case-manifest.json": hashes["case_manifest_sha256"],
        "build/Cases.cs": C.BIND_SQL_SOURCE_SHA,
        "build/v11-contracts.py": C.BASE_CONTRACT_SHA, "build/v11-packed-oracle.py": C.PACKED_SHA}
    for path, sha in pinned.items():
        C.require(index.get(path) == sha, "frozen_reviewed_policy:" + path)
    C.require(C.digest(base_path) == C.BASE_CONTRACT_SHA and C.digest(packed_path) == C.PACKED_SHA, "executing_reviewed_dependency")
    for lane in ("O", "W"):
        pin = identity[lane]
        C.require(frozen.get("packages", {}).get(lane) == pin["package_sha256"], "frozen_package_binding")
        package = root / "frozen-input/feed" / f'{pin["id"]}.{pin["version"]}.nupkg'.lower()
        base.validate_package_identity(package, pin)
        dll = root / "build/bin/Debug/net10.0" / Path(pin["asset"]).name
        C.require(C.digest(dll) == pin["dll_sha256"] and base._managed_mvid(dll.read_bytes()) == pin["mvid"], "runtime_DLL_identity")
    for key in ("restore_exit_code", "build_exit_code"):
        C.same(runner.get(key), 0, key)
    for key in ("restore_hard_deadline", "build_hard_deadline", "offline_hard_deadline"):
        C.same(runner.get(key), False, key)
    C.same(runner.get("offline_exit_code"), 0, "offline_process")
    offline = read("offline-contracts.json")
    C.require(runner.get("offline_report_sha256") == C.digest(root / "offline-contracts.json"), "offline_report_hash")
    C.require(offline.get("status") == "offline_contracts_passed" and offline.get("expected_only") is True
        and offline.get("database_accessed") is False, "actual_CSharp_offline_contract_gate")
    C.same(offline.get("check_count"), len(C.OFFLINE_CHECKS), "actual_CSharp_offline_contract_count")
    C.same(offline.get("checks"), C.OFFLINE_CHECKS, "actual_CSharp_helper_contracts")
    for key, path in {"assets_sha256": "build/obj/project.assets.json", "lock_sha256": "build/packages.lock.json",
        "tool_dll_sha256": "build/bin/Debug/net10.0/TemporalCandidateAcceptance.dll",
        "deps_sha256": "build/bin/Debug/net10.0/TemporalCandidateAcceptance.deps.json"}.items():
        C.require(runner.get(key) == C.digest(root / path), "build_artifact_hash:" + key)
    package_assets(read("build/obj/project.assets.json"), identity, "NuGet_assets")
    package_assets(read("build/bin/Debug/net10.0/TemporalCandidateAcceptance.deps.json"), identity, "runtime_deps")
    tool_mvid = base._managed_mvid((root / "build/bin/Debug/net10.0/TemporalCandidateAcceptance.dll").read_bytes())
    C.require(runner.get("tool_mvid") == tool_mvid, "actual_probe_MVID")
    lock = read("build/packages.lock.json")
    dependencies = lock.get("dependencies")
    C.require(type(dependencies) is dict and dependencies, "package_lock_dependencies")
    for framework in dependencies.values():
        for lane in ("O", "W"):
            pin = identity[lane]
            match = [item for name, item in framework.items() if name.lower() == pin["id"].lower()]
            C.require(len(match) == 1 and match[0].get("resolved") == pin["version"]
                and match[0].get("type") == "Direct", "package_lock_exact_direct")
    runs = runner.get("runs")
    C.require(type(runs) is list and len(runs) == 1 and runs[0].get("lane") == "supplemental", "supplemental_run_required")
    run = runs[0]
    for key in ("exit_code", "cleanup_exit_code"):
        C.same(run.get(key), 0, "run:" + key)
    for key in ("hard_deadline", "cleanup_hard_deadline"):
        C.same(run.get(key), False, "run:" + key)
    report, cleanup, ownership = (read("supplemental" + suffix) for suffix in (".json", ".cleanup.json", ".ownership.json"))
    for key, suffix in (("report_sha256", ".json"), ("cleanup_sha256", ".cleanup.json"), ("ownership_sha256", ".ownership.json")):
        C.require(run.get(key) == C.digest(root / ("supplemental" + suffix)), "run_artifact_hash:" + key)
    nonce = ownership.get("nonce")
    C.require(type(nonce) is str and re.fullmatch(r"[A-F0-9]{32}", nonce), "ownership_nonce")
    table = "TPN_" + nonce[:24]
    for key, wanted in dict(table=table, owner_marker="OWN_" + nonce[:24], absent_before_create=True,
        identity_verified=True, profile=profile, server_version=base.PROFILES[profile], creation_state="confirmed",
        owner_package_sha256=identity["W"]["package_sha256"], identity_sha256=hashes["identity_sha256"]).items():
        C.same(ownership.get(key), wanted, "ownership:" + key)
    for value in (report, cleanup):
        C.same(value.get("schema_version"), 1, "report_schema")
        C.same(value.get("process_exit_code"), 0, "report_exit")
        C.require(value.get("profile") == profile and value.get("phase") == "candidate"
            and value.get("lane") == "supplemental" and value.get("owned_table") == table, "report_context")
        C.require(value.get("producer_source_head") == C.SOURCE_HEAD
            and value.get("comparison_head") == identity.get("comparison_head"), "actual_producer_comparison_head")
        C.same(value.get("expected_only"), False, "synthetic_candidate_closed")
        C.same(value.get("database_accessed"), True, "real_DB_required")
        for key, sha in hashes.items():
            C.require(value.get(key) == sha, "report_input_hash:" + key)
        physical_profile(value.get("W_profile"), profile)
        if profile == "shared" and value is report:
            physical_profile(value.get("O_profile"), profile)
        elif profile == "shared":
            C.same(value.get("O_profile"), dict(status="not_used", reason="cleanup_requires_verified_W_only"), "official_cleanup_not_used")
        else:
            C.require(type(value.get("O_profile")) is dict and value["O_profile"].get("status") == "closed", "official_TLS_closed")
        C.require(value.get("mode") == ("run" if value is report else "cleanup"), "separate_cleanup_mode")
        for lane in ("W", "O"):
            observed = value.get("identities", {}).get(lane, {})
            C.require(all(observed.get(key) == val for key, val in identity[lane].items()), "loaded_package_identity")
            C.require(observed.get("package_reference_verified") is True and observed.get("actual_asset_verified") is True, "loaded_package_reference")
        C.require(value.get("tool_dll_sha256") == runner["tool_dll_sha256"]
                and value.get("deps_sha256") == runner["deps_sha256"], "actual_loaded_probe_deps")
        C.require(value.get("tool_mvid") == tool_mvid, "actual_loaded_probe_MVID")
        C.require(type(value.get("runtime")) is str and re.fullmatch(r"10\.[0-9]+\.[0-9]+", value["runtime"]), "actual_net10_runtime")
    cleanup_evidence(report, ownership, main=True)
    cleanup_evidence(cleanup, ownership, main=False)
    C.require(report["cleanup_receipt"]["fresh_connection_id"] != cleanup["cleanup_receipt"]["fresh_connection_id"], "independent_cleanup_fresh_connection")
    read_policy = read("frozen-input/case-manifest.json")
    read_context = C.binding_read_context(profile, report["W_profile"], C.digest(root / "build/Cases.cs"),
        read_policy.get("result_descriptor_addendum_sha256"), read_policy.get("shared_g2_read_root_decision_id"),
        read_policy.get("tls_g2_read6_design_sha256"),read_policy.get("root_decision_id"))
    binding_results = []
    count = C.validate_report(report, profile, read_context=read_context, binding_read_results=binding_results)
    return dict(schema_version=1, task="TP-06-supplement", profile=profile, status="supplemental_profile_accepted",
        candidate_acceptance=True, full_temporal_acceptance=False, case_count=count, issues=[],
        binding_result_observations=binding_results, runner_sha256=C.digest(root / "runner.json"))


def validate(directory):
    try:
        return _validate(directory)
    except Exception as error:
        safe = str(error) if isinstance(error, C.ContractError) else "artifact_error:" + type(error).__name__
        if isinstance(error, C.UnverifiedResultDescriptor):
            return dict(schema_version=1,task="TP-06-supplement",status="unverified_result_descriptor",
                candidate_acceptance=False,full_temporal_acceptance=False,issues=[safe],result_descriptor=error.detail)
        return dict(schema_version=1, task="TP-06-supplement", status="validation_failed", candidate_acceptance=False,
                    full_temporal_acceptance=False, issues=[safe])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = validate(args.directory)
    rendered = json.dumps(result, indent=2) + "\n"
    if args.output:
        args.output.write_text(rendered)
    else:
        sys.stdout.write(rendered)
    return 0 if result["candidate_acceptance"] else 1


if __name__ == "__main__":
    sys.exit(main())
