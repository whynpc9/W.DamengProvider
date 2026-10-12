"""Offline executor contracts. Tests mock every process; never starts dotnet or DB.

TP01_V23_SOURCE_MANIFEST is the independently frozen public archived manifest.
TP01_VERIFIED_FEED is needed only for the complete scheduler fixture. Neither is a secret.
"""
import copy
import hashlib
import importlib.util
import json
import os
import re
from pathlib import Path
import tempfile
import types
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("tp01_runner_contract_target", ROOT / "eng/temporal-precision.py")
runner = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(runner)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def synthetic_chain(phase="candidate"):
    source = {"schema_version": 1, "head": "a" * 40, "files": [
        {"path": "src/W.DmProvider/PublicApi/DmConnection.cs", "sha256": "1" * 64, "bytes": 10},
        {"path": "global.json", "sha256": "2" * 64, "bytes": 2}]}
    data = (json.dumps(source, indent=2) + "\n").encode()
    w = {"id": "W.DmProvider", "version": "0.1.0-tp.contract.20261008", "asset": "lib/net10.0/W.DmProvider.dll",
         "package_sha256": "3" * 64, "dll_sha256": "4" * 64, "mvid": "11111111-2222-3333-4444-555555555555"}
    identity = {"schema_version": 1, "phase": phase, "comparison_head": "b" * 40,
                "source_commit": source["head"], "O": copy.deepcopy(runner.O_PIN), "W": w,
                "producer": {"source_head": source["head"], "source_manifest_sha256": sha(data),
                             "source_archive_sha256": None, **{k: w[k] for k in ("version", "package_sha256", "dll_sha256", "mvid")}}}
    if phase == "baseline":
        identity["baseline_kind"] = "rebuilt_baseline"
    profile = {"server_version": "8.1.5.60", "root_approved": True, "evidence_id": "expected_only_not_run",
               "seven_write": True, "write_scales": [0, 1, 2, 3, 4, 5, 6, 7], "tltz_read": False, "tltz_write": False,
               "local_ticks_min": 0, "local_ticks_max": 3155378975999999999,
               "utc_ticks_min": 0, "utc_ticks_max": 3155378975999999999,
               "offset_min_minutes": -779, "offset_max_minutes": 840}
    cap = {"schema_version": 1, "source_commit": source["head"], "source_manifest_sha256": sha(data),
           "w_package_sha256": w["package_sha256"], "profiles": {"shared": profile}}
    return identity, source, sha(data), cap


def ownership(identity, identity_sha, state="confirmed", profile="shared"):
    nonce = "ABCDEF0123456789ABCDEF0123456789"
    if len(nonce) != 32 or re.fullmatch(r"[A-F0-9]{32}", nonce) is None:
        raise AssertionError("contract_fixture_nonce_must_be_32_hex")
    return {"schema_version": 1, "nonce": nonce, "table": "TPN_" + nonce[:24], "owner_marker": "OWN_" + nonce[:24],
            "profile": profile, "server_version": runner.contracts.PROFILES[profile],
            "owner_package_sha256": identity["W"]["package_sha256"], "identity_sha256": identity_sha,
            "absent_before_create": True, "identity_verified": True, "creation_state": state}


class InputContracts(unittest.TestCase):
    def test_candidate_and_rebuilt_positive(self):
        identity, source, source_sha, cap = synthetic_chain()
        runner.validate_inputs(identity, source, source_sha, None, "shared", cap)
        identity, source, source_sha, cap = synthetic_chain("baseline")
        runner.validate_inputs(identity, source, source_sha, None, "shared")

    def test_same_head_source_file_hash_swap_rejected(self):
        identity, source, source_sha, cap = synthetic_chain()
        altered = copy.deepcopy(source); altered["files"][0]["sha256"] = "9" * 64
        changed_sha = sha((json.dumps(altered, indent=2) + "\n").encode())
        with self.assertRaises(ValueError):
            runner.validate_inputs(identity, altered, changed_sha, None, "shared", cap)

    def test_nonempty_count_is_not_source_manifest(self):
        identity, source, source_sha, cap = synthetic_chain()
        del source["files"]; source["source_files"] = 1
        with self.assertRaises(ValueError):
            runner.validate_inputs(identity, source, source_sha, None, "shared", cap)

    def test_source_archive_swap_rejected(self):
        identity, source, source_sha, cap = synthetic_chain()
        identity["producer"]["source_archive_sha256"] = "5" * 64
        runner.validate_inputs(identity, source, source_sha, "5" * 64, "shared", cap)
        with self.assertRaises(ValueError):
            runner.validate_inputs(identity, source, source_sha, "6" * 64, "shared", cap)

    def test_producer_package_dll_and_capability_source_swap(self):
        for key in ("version", "package_sha256", "dll_sha256", "mvid"):
            identity, source, source_sha, cap = synthetic_chain()
            identity["producer"][key] = "another-producer"
            with self.subTest(key=key), self.assertRaises(ValueError):
                runner.validate_inputs(identity, source, source_sha, None, "shared", cap)
        identity, source, source_sha, cap = synthetic_chain()
        cap["source_manifest_sha256"] = "7" * 64
        with self.assertRaises(ValueError):
            runner.validate_inputs(identity, source, source_sha, None, "shared", cap)

    def test_duplicate_keys_bool_schema_and_profile_rejected(self):
        for text in ('{"phase":"baseline","phase":"candidate"}', '{"root_approved":false,"root_approved":true}', '{"expected":1,"expected":2}'):
            with self.assertRaises(ValueError):
                runner.contracts.loads_strict(text)
        for mutation in ("schema", "scale_bool", "duplicate_scale", "approval", "profile"):
            identity, source, source_sha, cap = synthetic_chain()
            profile = "shared"
            if mutation == "schema": identity["schema_version"] = True
            elif mutation == "scale_bool": cap["profiles"]["shared"]["write_scales"] = [True, 6, 7]
            elif mutation == "duplicate_scale": cap["profiles"]["shared"]["write_scales"] += [6]
            elif mutation == "approval": cap["profiles"]["shared"]["root_approved"] = 1
            else: profile = "unknown"
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                runner.validate_inputs(identity, source, source_sha, None, profile, cap)


class ProcessContracts(unittest.TestCase):
    def execute(self, primary=(0, False), cleanup=(0, False), state="confirmed", mutate=None, create=True, crash=False):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        directory = Path(temporary.name)
        identity, _, _, _ = synthetic_chain("baseline")
        identity_path = directory / "identities.json"; runner.write(identity_path, identity)
        owner, report = directory / "core.ownership.json", directory / "core.json"
        command = ["TEST-wrapper", "dotnet", "frozen.dll", "run", str(identity_path), "feed", "shared", "core", "original", str(owner), str(report), "manifest"]
        calls = []
        def fake_process(cmd, cwd, env, timeout):
            calls.append((cmd[3], timeout))
            if cmd[3] == "run":
                if create:
                    value = ownership(identity, runner.digest(identity_path), state)
                    if mutate: mutate(value)
                    runner.write(owner, value)
                runner.write(report, {"task": "expected_only_mock_process_not_run"})
                if crash: raise OSError("synthetic process adapter error")
                return primary
            return cleanup
        result, accepted = runner.execute_lane(command, directory, {}, 60, "core", owner, report, identity, fake_process)
        return result, accepted, calls, owner

    def test_success_requires_two_successful_processes(self):
        result, accepted, calls, _ = self.execute()
        self.assertTrue(accepted); self.assertEqual(calls, [("run", 60), ("cleanup", 90)])
        self.assertEqual(result["creation_state"], "confirmed")

    def test_primary_failure_is_preserved_when_cleanup_fails(self):
        result, accepted, calls, owner = self.execute(primary=(17, False), cleanup=(19, False))
        self.assertFalse(accepted); self.assertEqual(result["exit_code"], 17); self.assertEqual(result["cleanup_exit_code"], 19)
        self.assertTrue(owner.exists()); self.assertEqual(len(calls), 2)

    def test_deadline_before_create_has_no_cleanup_process(self):
        result, accepted, calls, _ = self.execute(primary=(124, True), create=False)
        self.assertFalse(accepted); self.assertEqual(calls, [("run", 60)])
        self.assertEqual(result["exit_code"], 124)

    def test_deadline_after_create_uses_exact_manifest(self):
        result, accepted, calls, owner = self.execute(primary=(124, True), state="create_inflight")
        self.assertFalse(accepted); self.assertEqual(calls[-1], ("cleanup", 90)); self.assertTrue(owner.exists())
        self.assertEqual(result["creation_state"], "create_inflight")

    def test_known_create_failure_preserved_for_no_drop_cleanup_verifier(self):
        result, accepted, calls, _ = self.execute(primary=(1, False), cleanup=(1, False), state="known_create_failure")
        self.assertFalse(accepted); self.assertEqual(result["creation_state"], "known_create_failure")
        self.assertEqual(result["exit_code"], 1)

    def test_cleanup_timeout_stops_successful_primary(self):
        result, accepted, calls, owner = self.execute(cleanup=(124, True))
        self.assertFalse(accepted); self.assertEqual(result["exit_code"], 0); self.assertTrue(result["cleanup_hard_deadline"])
        self.assertTrue(owner.exists()); self.assertEqual(len(calls), 2)

    def test_foreign_or_malformed_ownership_never_starts_cleanup(self):
        for key, replacement in (("nonce", "bad"), ("profile", "tls"), ("owner_marker", "FOREIGN"),
                                 ("owner_package_sha256", "0" * 64), ("identity_sha256", "0" * 64),
                                 ("schema_version", True), ("identity_verified", 1)):
            with self.subTest(key=key):
                result, accepted, calls, owner = self.execute(mutate=lambda value: value.update({key: replacement}))
                self.assertFalse(accepted); self.assertEqual(calls, [("run", 60)]); self.assertEqual(result["cleanup_exit_code"], 1)
                self.assertTrue(owner.exists())

    def test_bool_exit_zero_cannot_accept(self):
        result, accepted, _, _ = self.execute(primary=(False, False))
        self.assertFalse(accepted); self.assertEqual(result["exit_code"], 1)
        result, accepted, _, _ = self.execute(cleanup=(False, False))
        self.assertFalse(accepted); self.assertEqual(result["cleanup_exit_code"], 1)

    def test_unexpected_adapter_failure_still_attempts_cleanup(self):
        result, accepted, calls, _ = self.execute(crash=True)
        self.assertFalse(accepted); self.assertEqual(calls[-1][0], "cleanup"); self.assertEqual(result["primary_error_type"], "OSError")



class LocalRequiredContracts(unittest.TestCase):
    """Only selected by --local-required; absence is a failure, never a skip."""
    def test_real_archived_producer_positive(self):
        path = os.environ.get("TP01_V23_SOURCE_MANIFEST")
        self.assertTrue(path, "Low must supply the frozen public v23 manifest; no skip or invented source body")
        source = runner.contracts.load_json(path)
        identity = runner.contracts.load_json(ROOT / "tools/TemporalBoundaryProbe/identities.json")
        self.assertEqual(runner.digest(path), runner.contracts.V23_SOURCE_SHA)
        runner.validate_inputs(identity, source, runner.digest(path), None, "shared")
        # Same head/package plus a changed source byte identity is insufficient.
        with self.assertRaises(ValueError):
            runner.validate_inputs(identity, source, "f" * 64, None, "shared")

    def test_complete_scheduler_does_not_start_next_lane_after_cleanup_failure(self):
        source_path, feed = os.environ.get("TP01_V23_SOURCE_MANIFEST"), os.environ.get("TP01_VERIFIED_FEED")
        self.assertTrue(source_path and feed, "Low must supply public frozen producer and verified package feed; no skip")
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary); repo = directory / "original"; (repo / "scripts").mkdir(parents=True)
            (repo / "scripts/with-dameng-test.sh").write_text("# mock wrapper, never executed\n")
            a = types.SimpleNamespace(output=directory / "new-run", identities=ROOT / "tools/TemporalBoundaryProbe/identities.json",
                source_manifest=Path(source_path), source_archive=None, feed=Path(feed), capabilities=None, repo=repo,
                profile="shared", lanes="core,metadata,extended", deadline=60)
            db_modes = []
            def fake_process(cmd, cwd, env, timeout):
                if cmd[0] == "dotnet":
                    if len(cmd) > 2 and cmd[2] == "offline":
                        runner.write(cmd[3], {"status": "offline_contracts_passed", "expected_only": True, "database_accessed": False})
                        runner.write(cmd[4], {"task": "expected_only_mock_offline_not_actual_CSharp"})
                    if cmd[1] == "build":
                        build = Path(cwd); (build / "obj").mkdir(); dll = build / "bin/Debug/net10.0/TemporalBoundaryProbe.dll"; dll.parent.mkdir(parents=True); dll.write_bytes(b"mock tool not compiled")
                        identity = runner.contracts.load_json(a.identities)
                        libraries = {identity[l]["id"] + "/" + identity[l]["version"]: {"type": "package"} for l in ("O", "W")}
                        target = {identity[l]["id"] + "/" + identity[l]["version"]: {"runtime": {identity[l]["asset"]: {}}} for l in ("O", "W")}
                        runner.write(build / "obj/project.assets.json", {"libraries": libraries, "targets": {"net10.0": target}})
                        runner.write(build / "packages.lock.json", {"mock": "expected_only_not_run"})
                    return 0, False
                db_modes.append((cmd[3], cmd[7]))
                if cmd[3] == "run":
                    identity = runner.contracts.load_json(cmd[4]); runner.write(cmd[9], ownership(identity, runner.digest(cmd[4])))
                    runner.write(cmd[10], {"mock": "expected_only_not_run"}); return 0, False
                return 124, True
            with patch.object(runner, "bounded", side_effect=fake_process):
                self.assertEqual(runner.run(a), 1)
            self.assertEqual(db_modes, [("run", "core"), ("cleanup", "core")])
            report = runner.contracts.load_json(a.output / "runner.json")
            self.assertEqual(report["status"], "execution_failed"); self.assertEqual(len(report["runs"]), 1)


_LOCAL_REQUIRED = False

def load_tests(loader, _tests, _pattern):
    suite = unittest.TestSuite()
    for cls in (InputContracts, ProcessContracts):
        suite.addTests(loader.loadTestsFromTestCase(cls))
    if _LOCAL_REQUIRED:
        suite.addTests(loader.loadTestsFromTestCase(LocalRequiredContracts))
    return suite


if __name__ == "__main__":
    if "--local-required" in sys.argv:
        _LOCAL_REQUIRED = True; sys.argv.remove("--local-required")
    unittest.main()
