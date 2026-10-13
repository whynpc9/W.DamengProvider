#!/usr/bin/env python3
"""Low-only bounded same-package TP06 executor. Child output never relayed."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("tp06_policy", HERE / "contracts.py")
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
base, BASE_PATH = policy.reviewed_module("tp06_reviewed_v11", "v11-contracts.py", policy.BASE_CONTRACT_SHA)
_, PACKED_PATH = policy.reviewed_module("tp06_reviewed_packed", "v11-packed-oracle.py", policy.PACKED_SHA)
require = policy.require
digest = policy.digest


def write(path, value):
    temporary = Path(str(path) + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)


def bounded(command, cwd, env, seconds):
    process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL, start_new_session=True)
    try:
        return process.wait(timeout=seconds), False
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGTERM)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5)
        return 124, True


OFFLINE_CHECKS = (
    "owner_valid", "owner_bad_nonce", "marker_exact", "create_collision", "create_states",
    "cleanup_exact", "cleanup_foreign", "cleanup_known_failure", "cleanup_fresh_failure",
    "duplicate_json", "physical_missing_closed", "safe_exception", "server_null_sync_getter",
    "server_null_async_getter", "server_null_conflict_refused", "server_nonnull_projection_getters",
    "frame_classification", "prepared_collection_exact", "prepared_execute_control",
    "prepared_extra5_refused", "prepared_allocation3_refused", "direct5_error_response",
    "direct_allocation_response_refused", "direct_ambiguous_response_refused",
    "recovery_open_control", "recovery_broken_control", "recovery_closed_control",
    "recovery_unapproved_state_rejected", "recovery_old_send_rejected", "recovery_wrong_probe_error_rejected",
    "recovery_fresh_id_reuse_rejected", "recovery_fresh_scalar_rejected", "recovery_midstep_retained",
    "recovery_server_cannot_retire", "recovery_target_business_sent_rejected", "recovery_independent_stage_origin",
    "read_partial_raw_guard_retained", "read_partial_value_guard_retained", "read_partial_server_guard_retained",
    "read_partial_getter_error_retained", "read_text_bounded_unknown", "read_text_overlong",
    "read_server_null_attempt_retained", "read_partial_not_complete",
    "interval_text_observed_channels_control", "interval_text_declared_positive_controls",
    "interval_text_value_width_refused", "interval_text_value_sign_refused", "interval_text_fraction_refused",
    "interval_text_server_domain_refused", "interval_text_wrapper_qualifier_refused", "interval_text_numeric_mutation_detected",
    "interval_outer_minus_observed_control", "interval_outer_minus_positive_controls",
    "interval_outer_minus_conflict_refused", "interval_outer_minus_numeric_mutation_detected",
)


def validate_offline_report(checks):
    require(checks.get("status")=="offline_contracts_passed" and checks.get("expected_only") is True
            and checks.get("database_accessed") is False and type(checks.get("check_count")) is int
            and checks["check_count"]==len(OFFLINE_CHECKS) and checks.get("checks")==list(OFFLINE_CHECKS),
            "actual_offline_policy_gate")
    return checks


def package_filename(entry):
    return f'{entry["id"]}.{entry["version"]}.nupkg'.lower()


def sdk_version(cwd, env):
    process=subprocess.Popen(["dotnet","--version"],cwd=cwd,env=env,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,start_new_session=True)
    try:
        output,_=process.communicate(timeout=30)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid,signal.SIGKILL);process.wait(timeout=5)
        require(False,"SDK_probe_deadline")
    require(process.returncode==0 and output.decode("ascii",errors="replace").strip()=="10.0.401","exact_SDK_required")
    return "10.0.401"


def validate_inputs(a):
    identity, source, caps = [policy.load_json(p) for p in (a.identities, a.source_manifest, a.capabilities)]
    require(digest(a.identities)==policy.IDENTITIES_SHA and digest(a.capabilities)==policy.CAPS_SHA,"exact_identity_capability_bytes")
    require(identity.get("W") == policy.W_PIN, "exact_candidate_identity")
    require(digest(a.source_manifest) == policy.SOURCE_SHA and digest(a.source_archive) == policy.ARCHIVE_SHA, "exact_candidate_source_archive")
    base.validate_producer(identity, digest(a.source_manifest), source, digest(a.source_archive))
    base.validate_capabilities(caps, identity, a.profile)
    require(policy.load_json(HERE / "case-manifest.json") == policy.manifest(), "case_manifest_policy")
    for lane in ("O", "W"):
        e = identity[lane]
        paths = [p for p in a.feed.glob("*.nupkg") if p.name.lower() == f'{e["id"]}.{e["version"]}.nupkg'.lower()]
        require(len(paths) == 1, "exact_package_file")
        base.validate_package_identity(paths[0], e)
    return identity, source, caps


def validate_ownership(value, identity, profile, identity_sha):
    require(type(value) is dict and type(value.get("schema_version")) is int and value["schema_version"] == 1, "ownership_schema")
    nonce = value.get("nonce")
    require(type(nonce) is str and re.fullmatch(r"[A-F0-9]{32}", nonce), "ownership_nonce")
    require(value.get("table") == "TPN_" + nonce[:24] and value.get("owner_marker") == "OWN_" + nonce[:24], "ownership_names")
    require(value.get("profile") == profile and value.get("server_version") == base.PROFILES[profile], "ownership_profile")
    require(value.get("owner_package_sha256") == identity["W"]["package_sha256"] and value.get("identity_sha256") == identity_sha, "ownership_identity")
    require(value.get("absent_before_create") is True and value.get("identity_verified") is True, "ownership_preconditions")
    require(value.get("creation_state") in ("reserved", "create_inflight", "confirmed", "known_create_failure"), "ownership_state")
    return value


def execute(command, cwd, env, seconds, ownership, report, identity, executor=bounded):
    """Separate cleanup always runs after primary; injected adapters are pure tests only."""
    entry = dict(lane="supplemental", exit_code=1, hard_deadline=False, cleanup_exit_code=None, cleanup_hard_deadline=False,
                 safe_command=["TEST_wrapper", "dotnet", "frozen_tool", "run", "frozen_inputs", "profile", "supplemental"])
    started = time.monotonic()
    try:
        code, deadline = executor(command, cwd, env, seconds)
        require(type(code) is int and type(deadline) is bool, "primary_process_receipt")
        entry.update(exit_code=code, hard_deadline=deadline)
    except Exception as error:
        entry["primary_error_type"] = type(error).__name__
    finally:
        entry["elapsed_seconds"] = round(time.monotonic() - started, 3)
        if ownership.is_file():
            try:
                validate_ownership(policy.load_json(ownership), identity, command[6], digest(command[4]))
                cleanup = command.copy(); cleanup[3] = "cleanup"; cleanup[10] = str(report.with_name("supplemental.cleanup.json"))
                code, deadline = executor(cleanup, cwd, env, 90)
                require(type(code) is int and type(deadline) is bool, "cleanup_process_receipt")
                entry.update(cleanup_exit_code=code, cleanup_hard_deadline=deadline)
            except Exception as error:
                entry["cleanup_error_type"] = type(error).__name__; entry["cleanup_exit_code"] = 1
            entry["ownership_sha256"] = digest(ownership)
            entry["recovery_manifest"] = ownership.name
        else:
            entry["ownership_manifest_present"] = False
    for key, path in (("report_sha256", report), ("cleanup_sha256", report.with_name("supplemental.cleanup.json"))):
        if path.is_file(): entry[key] = digest(path)
    successful = entry["exit_code"] == 0 and entry["hard_deadline"] is False and entry["cleanup_exit_code"] == 0 and entry["cleanup_hard_deadline"] is False
    return entry, successful


def run(a):
    require(type(a.deadline) is int and 60 <= a.deadline <= 1800, "bounded_deadline")
    identity, _, _ = validate_inputs(a)
    output = a.output.resolve(); require(not output.exists(), "fresh_output_required")
    output.mkdir(parents=True, mode=0o700); build=output / "build"; freeze=output / "frozen-input"
    build.mkdir(); freeze.mkdir(); copied=[]
    def copy(source, target):
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
        copied.append(dict(path=str(target.relative_to(output)), sha256=digest(target)))
    for source in sorted(HERE.iterdir()):
        if source.is_file() and source.suffix in (".cs", ".csproj", ".json", ".md", ".py") and not source.name.endswith("packages.lock.json"):
            copy(source, build / source.name)
    copy(BASE_PATH, build / "v11-contracts.py"); copy(PACKED_PATH, build / "v11-packed-oracle.py")
    for name in ("contracts.py", "validate.py", "case-manifest.json", "run.py"):
        copy(HERE / name, freeze / name)
    for source, name in ((a.identities,"identities.json"),(a.source_manifest,"product-source-manifest.json"),(a.source_archive,"product-source.tar"),(a.capabilities,"capabilities.json")):
        shutil.copyfile(source,freeze / name)
    feed=freeze / "feed";feed.mkdir()
    for lane in ("O","W"):
        e=identity[lane];source=next(p for p in a.feed.glob("*.nupkg") if p.name.lower()==f'{e["id"]}.{e["version"]}.nupkg'.lower())
        shutil.copyfile(source,feed / package_filename(e))
    manifest=dict(schema_version=1,task="TP-06-supplement",phase="candidate",source_commit=identity["producer"]["source_head"],comparison_head=identity["comparison_head"],producer=identity["producer"],product_source_manifest_sha256=digest(freeze / "product-source-manifest.json"),source_archive_sha256=digest(freeze / "product-source.tar"),identity_sha256=digest(freeze / "identities.json"),capabilities_sha256=digest(freeze / "capabilities.json"),case_manifest_sha256=digest(freeze / "case-manifest.json"),tools=copied,packages={lane:identity[lane]["package_sha256"] for lane in ("O","W")})
    write(freeze / "manifest.json",manifest)
    env={k:v for k,v in os.environ.items() if not k.startswith("DAMENG_")}
    env.update(DOTNET_CLI_HOME=str(output / "cli-home"),DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",DOTNET_CLI_TELEMETRY_OPTOUT="1",PYTHONDONTWRITEBYTECODE="1")
    result=dict(schema_version=1,task="TP-06-supplement",phase="candidate",profile=a.profile,source_manifest_sha256=digest(freeze / "manifest.json"),product_source_manifest_sha256=manifest["product_source_manifest_sha256"],identity_sha256=manifest["identity_sha256"],capabilities_sha256=manifest["capabilities_sha256"],case_manifest_sha256=manifest["case_manifest_sha256"],runs=[],status="not_run")
    write(output / "runner.json",result)
    result["sdk_version"]=sdk_version(build,env);write(output / "runner.json",result)
    project=build / "TemporalCandidateAcceptance.csproj";flags=["-m:1","/nodeReuse:false","/p:UseSharedCompilation=false","--disable-build-servers"]
    for stage,command in (("restore",["dotnet","restore",str(project),"--source",str(feed),*flags]),("build",["dotnet","build",str(project),"--no-restore",*flags])):
        code,deadline=bounded(command,build,env,180);result[stage+"_exit_code"]=code;result[stage+"_hard_deadline"]=deadline
        if code or deadline:result["status"]=stage+"_failed";write(output / "runner.json",result);return 1
    assets=policy.load_json(build / "obj/project.assets.json")
    for lane in ("O","W"):
        e=identity[lane];key=e["id"]+"/"+e["version"]
        require(assets["libraries"][key]["type"]=="package" and any(e["asset"] in t.get(key,{}).get("runtime",{}) for t in assets["targets"].values()),"actual_asset_selection")
    dll=build / "bin/Debug/net10.0/TemporalCandidateAcceptance.dll";deps=dll.with_suffix(".deps.json")
    result.update(assets_sha256=digest(build / "obj/project.assets.json"),lock_sha256=digest(build / "packages.lock.json"),tool_dll_sha256=digest(dll),deps_sha256=digest(deps),tool_mvid=base._managed_mvid(dll.read_bytes()))
    offline=output / "offline-contracts.json"
    arguments=[str(freeze / "identities.json"),str(feed),a.profile,"supplemental",str(a.repo.resolve()),str(output / "unused-offline-ownership.json"),str(offline),str(freeze / "manifest.json")]
    code,deadline=bounded(["dotnet",str(dll),"offline",*arguments],build,env,90);result.update(offline_exit_code=code,offline_hard_deadline=deadline)
    if code or deadline:result["status"]="offline_contracts_failed";write(output / "runner.json",result);return 1
    checks=policy.load_json(offline)
    validate_offline_report(checks)
    result["offline_report_sha256"]=digest(offline)
    if a.offline_only:result["status"]="offline_complete_no_database";write(output / "runner.json",result);return 0
    wrapper=a.repo.resolve() / "scripts" / ("with-dameng-tls-test.sh" if a.profile=="tls" else "with-dameng-test.sh")
    require(wrapper.is_file(),"original_TEST_wrapper");result["wrapper_sha256"]=digest(wrapper)
    ownership=output / "supplemental.ownership.json";report=output / "supplemental.json"
    command=[str(wrapper),"dotnet",str(dll),"run",str(freeze / "identities.json"),str(feed),a.profile,"supplemental",str(a.repo.resolve()),str(ownership),str(report),str(freeze / "manifest.json")]
    entry,success=execute(command,build,env,a.deadline,ownership,report,identity);result["runs"].append(entry)
    result["status"]="execution_complete" if success else "execution_failed";write(output / "runner.json",result)
    return 0 if success else 1


def main():
    p=argparse.ArgumentParser(description=__doc__)
    for name in ("feed","source-manifest","source-archive","identities","capabilities","repo","output"):p.add_argument("--"+name,type=Path,required=True)
    p.add_argument("--profile",choices=("shared","tls"),required=True);p.add_argument("--deadline",type=int,default=1200);p.add_argument("--offline-only",action="store_true")
    try:return run(p.parse_args())
    except Exception as error:
        print(json.dumps(dict(status="runner_failed",error_type=type(error).__name__)));return 1

if __name__=="__main__":sys.exit(main())
