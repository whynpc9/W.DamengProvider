"""Expected-only scheduler/ownership mutants. Never executes SDK, wrappers or DB."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("tp06_runner_under_test",HERE / "run.py")
runner=importlib.util.module_from_spec(spec);spec.loader.exec_module(runner)

class RunnerContracts(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name)
        self.identity=dict(W=copy.deepcopy(runner.policy.W_PIN))
        self.identity_path=self.root / "identities.json";self.identity_path.write_text(json.dumps(self.identity))
        self.owner=self.root / "supplemental.ownership.json";self.report=self.root / "supplemental.json"
        self.value=dict(schema_version=1,nonce="A"*32,table="TPN_"+"A"*24,owner_marker="OWN_"+"A"*24,profile="shared",server_version="8.1.5.60",owner_package_sha256=self.identity["W"]["package_sha256"],identity_sha256=runner.digest(self.identity_path),absent_before_create=True,identity_verified=True,creation_state="confirmed")
        self.command=["TEST_wrapper","dotnet","frozen_tool","run",str(self.identity_path),"feed","shared","supplemental","repo",str(self.owner),str(self.report),"manifest"]
    def persist(self,value=None):self.owner.write_text(json.dumps(value or self.value))
    def execute(self,outcomes):
        calls=[]
        def adapter(command,cwd,env,seconds):
            calls.append((command[3],seconds));result=outcomes[len(calls)-1]
            if isinstance(result,Exception):raise result
            return result
        entry,success=runner.execute(self.command,self.root,{},60,self.owner,self.report,self.identity,adapter)
        return entry,success,calls
    def test_actual_offline_check_set_control(self):
        value=dict(status="offline_contracts_passed",expected_only=True,database_accessed=False,
                   check_count=56,checks=list(runner.OFFLINE_CHECKS))
        runner.validate_offline_report(value)
    def test_actual_offline_check_set_mutants(self):
        control=dict(status="offline_contracts_passed",expected_only=True,database_accessed=False,
                     check_count=56,checks=list(runner.OFFLINE_CHECKS))
        mutants=[]
        missing=copy.deepcopy(control);missing["checks"].pop();mutants.append(missing)
        duplicate=copy.deepcopy(control);duplicate["checks"][-1]=duplicate["checks"][0];mutants.append(duplicate)
        wrong=copy.deepcopy(control);wrong["checks"][-1]="synthetic_count_only";mutants.append(wrong)
        count=copy.deepcopy(control);count["check_count"]=55;mutants.append(count)
        skipped=copy.deepcopy(control);skipped["status"]="offline_skipped";mutants.append(skipped)
        database=copy.deepcopy(control);database["database_accessed"]=True;mutants.append(database)
        for index,value in enumerate(mutants):
            with self.subTest(index=index):
                with self.assertRaises(ValueError):runner.validate_offline_report(value)
    def test_feed_filename_canonical_from_uppercase_id(self):
        self.assertEqual(runner.package_filename(runner.policy.W_PIN),"w.damengprovider.0.1.0-temporal.20261008144915.nupkg")
        self.assertEqual(runner.package_filename(dict(id="DM.DmProvider",version="8.3.1.47463")),"dm.dmprovider.8.3.1.47463.nupkg")
    def test_exact_owner(self):
        runner.validate_ownership(self.value,self.identity,"shared",runner.digest(self.identity_path))
    def test_owner_mutants(self):
        changes={"schema_version":True,"nonce":"A"*34,"table":"TPN_FOREIGN","owner_marker":"OWN_FOREIGN","profile":"tls","server_version":"9.0","owner_package_sha256":"0"*64,"identity_sha256":"0"*64,"absent_before_create":False,"identity_verified":False,"creation_state":"assumed_created"}
        for key,value in changes.items():
            with self.subTest(key=key):
                mutant=copy.deepcopy(self.value);mutant[key]=value
                with self.assertRaises(ValueError):runner.validate_ownership(mutant,self.identity,"shared",runner.digest(self.identity_path))
    def test_primary_failure_preserved_after_cleanup(self):
        self.persist();entry,ok,calls=self.execute([(1,False),(0,False)])
        self.assertFalse(ok);self.assertEqual(entry["exit_code"],1);self.assertEqual(entry["cleanup_exit_code"],0);self.assertEqual(calls,[("run",60),("cleanup",90)])
    def test_hard_deadline_still_cleanup(self):
        self.persist();entry,ok,calls=self.execute([(124,True),(0,False)])
        self.assertFalse(ok);self.assertTrue(entry["hard_deadline"]);self.assertEqual(len(calls),2)
    def test_cleanup_failure_blocks_success(self):
        self.persist();entry,ok,calls=self.execute([(0,False),(1,False)])
        self.assertFalse(ok);self.assertEqual(entry["exit_code"],0);self.assertEqual(entry["cleanup_exit_code"],1)
    def test_cleanup_deadline_blocks_success(self):
        self.persist();entry,ok,_=self.execute([(0,False),(124,True)])
        self.assertFalse(ok);self.assertTrue(entry["cleanup_hard_deadline"])
    def test_primary_exception_still_cleanup(self):
        self.persist();entry,ok,calls=self.execute([RuntimeError("must_not_serialize_message"),(0,False)])
        self.assertFalse(ok);self.assertEqual(entry["primary_error_type"],"RuntimeError");self.assertNotIn("must_not_serialize_message",json.dumps(entry));self.assertEqual(len(calls),2)
    def test_bad_owner_never_dispatches_cleanup(self):
        value=copy.deepcopy(self.value);value["table"]="FOREIGN";self.persist(value)
        entry,ok,calls=self.execute([(0,False)])
        self.assertFalse(ok);self.assertEqual(len(calls),1);self.assertEqual(entry["cleanup_exit_code"],1)
    def test_known_create_failure_dispatches_verifier(self):
        value=copy.deepcopy(self.value);value["creation_state"]="known_create_failure";self.persist(value)
        _,ok,calls=self.execute([(1,False),(0,False)])
        self.assertFalse(ok);self.assertEqual(calls[-1],("cleanup",90))
    def test_missing_owner_does_not_establish_final_state(self):
        _,ok,calls=self.execute([(0,False)])
        self.assertFalse(ok);self.assertEqual(calls,[("run",60)])
    def test_bool_exit_not_an_integer_receipt(self):
        self.persist();entry,ok,calls=self.execute([(False,False),(0,False)])
        self.assertFalse(ok);self.assertEqual(entry["exit_code"],1);self.assertEqual(len(calls),2)
    def test_duplicate_owner_json_refused(self):
        self.owner.write_text('{"schema_version":1,"schema_version":1}')
        _,ok,calls=self.execute([(0,False)])
        self.assertFalse(ok);self.assertEqual(len(calls),1)

if __name__=="__main__":unittest.main()
