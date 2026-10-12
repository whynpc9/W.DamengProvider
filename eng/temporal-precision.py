#!/usr/bin/env python3
"""Low-only executor. No credentials are read, copied or printed by this runner."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import zipfile
import xml.etree.ElementTree as ET
import importlib.util

_CONTRACT_PATH = Path(__file__).resolve().parent.parent / "tools" / "TemporalBoundaryProbe" / "contracts.py"
_spec = importlib.util.spec_from_file_location("temporal_boundary_contracts", _CONTRACT_PATH)
contracts = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(contracts)

O_PIN = contracts.O_PIN
W_PIN = contracts.W_PIN

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def write(path, value):
    temporary = Path(str(path) + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n")
    temporary.replace(path)

def require(condition, code):
    if not condition:
        raise ValueError(code)

def bounded(command, cwd, env, seconds):
    # Never relay potentially credential-bearing native exceptions or driver stdout.
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

def validate_inputs(identity, source_value, source_sha, archive_sha, profile, capabilities=None):
    contracts.enum(profile, contracts.PROFILES, "profile")
    contracts.validate_producer(identity, source_sha, source_value, archive_sha)
    require(type(identity.get("comparison_head")) is str and re.fullmatch(r"[a-f0-9]{40}", identity["comparison_head"]), "comparison_head")
    if identity["phase"] == "candidate":
        require(capabilities is not None, "candidate_requires_root_approved_capabilities")
    if capabilities is not None:
        contracts.validate_capabilities(capabilities, identity, profile)
    return identity

def validate_ownership(value, identity, profile, identity_sha):
    contracts.object_value(value, "ownership")
    contracts.true_int(value.get("schema_version"), "ownership.schema", 1, 1)
    nonce = value.get("nonce")
    require(type(nonce) is str and re.fullmatch(r"[A-F0-9]{32}", nonce), "ownership_nonce")
    require(value.get("table") == "TPN_" + nonce[:24] and value.get("owner_marker") == "OWN_" + nonce[:24], "ownership_names")
    require(value.get("profile") == profile and value.get("server_version") == contracts.PROFILES[profile], "ownership_profile")
    require(value.get("owner_package_sha256") == identity["W"]["package_sha256"] and value.get("identity_sha256") == identity_sha, "ownership_identity")
    require(value.get("absent_before_create") is True and value.get("identity_verified") is True, "ownership_preconditions")
    contracts.enum(value.get("creation_state"), ("reserved", "create_inflight", "confirmed", "known_create_failure"), "creation_state")
    return value

def execute_lane(command, cwd, env, seconds, lane, ownership, report, identity, executor=None):
    """Process adapter is injectable for offline contracts; cleanup cannot overwrite primary failure."""
    executor = bounded if executor is None else executor
    started = time.monotonic()
    entry = dict(lane=lane, exit_code=1, hard_deadline=False, cleanup_exit_code=None, cleanup_hard_deadline=False,
                 safe_command=["TEST_wrapper", "dotnet", "frozen_tool", "run", "frozen_identities", "frozen_feed", command[6], lane,
                               "original_repo", "exact_ownership", "safe_report", "frozen_manifest"])
    try:
        code, deadline = executor(command, cwd, env, seconds)
        contracts.true_int(code, "process_exit_code")
        contracts.boolean(deadline, "process_hard_deadline")
        entry.update(exit_code=code, hard_deadline=deadline)
    except Exception as error:
        entry["primary_error_type"] = type(error).__name__
    finally:
        entry["elapsed_seconds"] = round(time.monotonic() - started, 3)
        if ownership.exists():
            entry["ownership_manifest_present"] = True
            entry["ownership_manifest_sha256"] = digest(ownership)
            entry["recovery_manifest"] = ownership.name
            try:
                owned = validate_ownership(contracts.load_json(ownership), identity, command[6], digest(command[4]))
                entry["creation_state"] = owned["creation_state"]
                # Even a known failure invokes the C# cleanup verifier, which refuses any DROP of a present foreign table.
                cleanup = command.copy(); cleanup[3] = "cleanup"; cleanup[10] = str(report.with_name(lane + ".cleanup.json"))
                code, deadline = executor(cleanup, cwd, env, 90)
                contracts.true_int(code, "cleanup_exit_code"); contracts.boolean(deadline, "cleanup_hard_deadline")
                entry.update(cleanup_exit_code=code, cleanup_hard_deadline=deadline)
            except Exception as error:
                entry["cleanup_error_type"] = type(error).__name__
                entry["cleanup_exit_code"] = 1
        else:
            entry["ownership_manifest_present"] = False
    if report.exists():
        entry["report_sha256"] = digest(report)
    successful = entry["exit_code"] == 0 and entry["hard_deadline"] is False and entry["cleanup_exit_code"] == 0 and entry["cleanup_hard_deadline"] is False
    return entry, successful


def run(a):
    contracts.true_int(a.deadline, "deadline", 60, 1800)
    lanes = a.lanes.split(",")
    require(bool(lanes) and len(set(lanes)) == len(lanes), "unique_lanes")
    for lane in lanes: contracts.enum(lane, contracts.LANES, "lane")
    output = a.output.resolve()
    require(not output.exists(), "new_output_directory_required")
    output.mkdir(parents=True, mode=0o700)
    identity = contracts.load_json(a.identities)
    source_value = contracts.load_json(a.source_manifest)
    archive_sha = digest(a.source_archive) if a.source_archive is not None else None
    validate_inputs(identity, source_value, digest(a.source_manifest), archive_sha, a.profile,
                    contracts.load_json(a.capabilities) if a.capabilities is not None else None)
    freeze = output / "frozen-input"
    freeze.mkdir()
    source = Path(__file__).resolve().parent.parent / "tools" / "TemporalBoundaryProbe"
    tool_paths = sorted(p for p in source.rglob("*") if p.is_file() and p.suffix in (".cs", ".csproj", ".json", ".md", ".py")
                        and not any(part in ("bin", "obj", "__pycache__") for part in p.relative_to(source).parts))
    copied = []
    build = output / "build"
    build.mkdir()
    for p in tool_paths:
        # Do not import arbitrary pre-existing lock files into a different package version.
        if p.name.endswith("packages.lock.json"):
            continue
        target = build / p.relative_to(source); target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(p, target)
        copied.append(dict(path="build/" + str(p.relative_to(source)), sha256=digest(target)))
    for p in sorted(Path(__file__).parent.glob("temporal-precision.*")):
        shutil.copyfile(p, freeze / p.name)
        copied.append(dict(path="frozen-input/" + p.name, sha256=digest(freeze / p.name)))
    shutil.copyfile(a.identities, freeze / "identities.json")
    shutil.copyfile(a.source_manifest, freeze / "product-source-manifest.json")
    capability_hash = None
    if a.capabilities is not None:
        shutil.copyfile(a.capabilities, freeze / "capabilities.json")
        capability_hash = digest(freeze / "capabilities.json")
    if a.source_archive is not None:
        shutil.copyfile(a.source_archive, freeze / "product-source.tar")
    feed = freeze / "feed"
    feed.mkdir()
    for lane in ("O", "W"):
        expected = identity[lane]
        require(re.fullmatch(r"[a-f0-9]{64}", expected["package_sha256"]) and re.fullmatch(r"[a-f0-9]{64}", expected["dll_sha256"]), "identity_hash")
        names = [p for p in a.feed.glob("*.nupkg") if p.name.lower() == f'{expected["id"]}.{expected["version"]}.nupkg'.lower()]
        require(len(names) == 1, "exact_package_file_required")
        require(digest(names[0]) == expected["package_sha256"], "package_hash")
        with zipfile.ZipFile(names[0]) as archive:
            require(hashlib.sha256(archive.read(expected["asset"])).hexdigest() == expected["dll_sha256"], "package_asset_hash")
            nuspecs = [n for n in archive.namelist() if n.endswith(".nuspec")]
            require(len(nuspecs) == 1, "unique_package_metadata")
            xml = ET.fromstring(archive.read(nuspecs[0]))
            metadata = next(n for n in xml if n.tag.split("}")[-1] == "metadata")
            actual = {n.tag.split("}")[-1]: n.text for n in metadata}
            require(type(actual.get("id")) is str and actual["id"].lower() == expected["id"].lower() and actual.get("version") == expected["version"], "actual_package_id_version")
        shutil.copyfile(names[0], feed / f'{expected["id"]}.{expected["version"]}.nupkg'.lower())
    frozen = dict(schema_version=1, source_commit=identity["producer"]["source_head"], comparison_head=identity["comparison_head"], phase=identity["phase"],
                  source_archive_sha256=archive_sha, producer=identity["producer"],
                  product_source_manifest_sha256=digest(freeze / "product-source-manifest.json"),
                  identity_sha256=digest(freeze / "identities.json"), capabilities_sha256=capability_hash, tools=copied,
                  packages={lane: identity[lane]["package_sha256"] for lane in ("O", "W")})
    write(freeze / "manifest.json", frozen)
    env = os.environ.copy()
    # Build outside TEST wrapper; remove any inherited TEST/admin/development connection environment.
    for key in list(env):
        if key.startswith("DAMENG_"):
            del env[key]
    env.update(DOTNET_CLI_HOME=str(output / "cli-home"), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    version = identity["W"]["version"]
    project = build / "TemporalBoundaryProbe.csproj"
    result = dict(schema_version=1, task="TP-01", phase=identity["phase"], profile=a.profile,
                  source_manifest_sha256=digest(freeze / "manifest.json"),
                  product_source_manifest_sha256=frozen["product_source_manifest_sha256"],
                  identity_sha256=frozen["identity_sha256"], capabilities_sha256=capability_hash, runs=[], status="not_run")
    write(output / "runner.json", result)
    restore = ["dotnet", "restore", str(project), "--source", str(feed),
               f"/p:WTemporalVersion={version}", "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false", "--disable-build-servers"]
    code, stopped = bounded(restore, build, env, 180)
    contracts.true_int(code, "restore_exit_code"); contracts.boolean(stopped, "restore_deadline")
    result["restore_exit_code"] = code
    if code:
        result["status"] = "restore_failed"; write(output / "runner.json", result); return 1
    code, stopped = bounded(["dotnet", "build", str(project), "--no-restore", f"/p:WTemporalVersion={version}",
                             "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false", "--disable-build-servers"], build, env, 180)
    contracts.true_int(code, "build_exit_code"); contracts.boolean(stopped, "build_deadline")
    result["build_exit_code"] = code
    if code:
        result["status"] = "build_failed"; write(output / "runner.json", result); return 1
    offline_report, offline_shape = output / "offline-contracts.json", output / "offline-producer-shape.json"
    executable = build / "bin" / "Debug" / "net10.0" / "TemporalBoundaryProbe.dll"
    offline_code, offline_deadline = bounded(["dotnet", str(executable), "offline", str(offline_report), str(offline_shape)], build, env, 90)
    contracts.true_int(offline_code, "offline_exit_code"); contracts.boolean(offline_deadline, "offline_deadline")
    result["offline_exit_code"] = offline_code; result["offline_deadline"] = offline_deadline
    if offline_code or offline_deadline:
        result["status"] = "offline_contracts_failed"; write(output / "runner.json", result); return 1
    offline = contracts.load_json(offline_report)
    require(offline.get("status") == "offline_contracts_passed" and offline.get("expected_only") is True and offline.get("database_accessed") is False, "actual_CSharp_offline_contracts_required")
    result["offline_report_sha256"] = digest(offline_report); result["offline_shape_sha256"] = digest(offline_shape)
    # Verify NuGet selected precisely the pinned runtime assets, not a project or file reference.
    assets = contracts.load_json(build / "obj" / "project.assets.json")
    for lane in ("O", "W"):
        e = identity[lane]; key = e["id"] + "/" + e["version"]
        require(assets["libraries"][key]["type"] == "package", "package_reference_required")
        require(any(e["asset"] in t[key].get("runtime", {}) for t in assets["targets"].values() if key in t), "selected_asset")
    result["assets_sha256"] = digest(build / "obj" / "project.assets.json")
    result["lock_sha256"] = digest(build / "packages.lock.json")
    dll = build / "bin" / "Debug" / "net10.0" / "TemporalBoundaryProbe.dll"
    result["tool_dll_sha256"] = digest(dll)
    if getattr(a, "offline_only", False):
        result["status"] = "offline_complete_no_database"; write(output / "runner.json", result); return 0
    wrapper = a.repo.resolve() / "scripts" / ("with-dameng-tls-test.sh" if a.profile == "tls" else "with-dameng-test.sh")
    require(wrapper.is_file(), "original_TEST_wrapper_missing")
    result["wrapper_sha256"] = digest(wrapper)
    for lane in a.lanes.split(","):
        contracts.enum(lane, contracts.LANES, "lane")
        ownership, report = output / (lane + ".ownership.json"), output / (lane + ".json")
        command = [str(wrapper), "dotnet", str(dll), "run", str(freeze / "identities.json"), str(feed), a.profile,
                   lane, str(a.repo.resolve()), str(ownership), str(report), str(freeze / "manifest.json")]
        entry, successful = execute_lane(command, build, env, a.deadline, lane, ownership, report, identity)
        result["runs"].append(entry); write(output / "runner.json", result)
        if not successful:
            result["status"] = "execution_failed"; write(output / "runner.json", result); return 1
    result["status"] = "execution_complete"
    write(output / "runner.json", result)
    return 0

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--feed", type=Path, required=True)
    p.add_argument("--source-manifest", type=Path, required=True)
    p.add_argument("--source-archive", type=Path, help="required exactly when producer records a source_archive_sha256")
    p.add_argument("--identities", type=Path, required=True)
    p.add_argument("--capabilities", type=Path, help="required for candidate: final root-approved profile capabilities, pinned to this W package")
    p.add_argument("--repo", type=Path, required=True, help="original repository providing TEST wrappers and TLS certificates")
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--profile", choices=("shared", "tls"), required=True)
    p.add_argument("--lanes", default="core,metadata,extended")
    p.add_argument("--deadline", type=int, default=900)
    p.add_argument("--offline-only", action="store_true", help="restore/build/C# selftests only; no TEST wrapper or DB")
    a = p.parse_args()
    try:
        require(60 <= a.deadline <= 1800, "bounded_deadline")
        return run(a)
    except Exception as error:
        # Exception text may contain local or network data; export only Python class.
        print(json.dumps(dict(status="runner_failed", error_type=type(error).__name__)))
        return 1

if __name__ == "__main__":
    sys.exit(main())
