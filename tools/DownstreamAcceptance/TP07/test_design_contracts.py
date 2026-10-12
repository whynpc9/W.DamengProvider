"""Offline D evidence mutations; synthetic records are never runtime acceptance."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent
V=types.ModuleType('D_contract_validator');V.__file__=str(HERE/'design-validate.py')
exec(compile((HERE/'design-validate.py').read_bytes(),V.__file__,'exec'),V.__dict__)
R=types.ModuleType('D_contract_runner');R.__file__=str(HERE/'design-run.py')
exec(compile((HERE/'design-run.py').read_bytes(),R.__file__,'exec'),R.__dict__)


class DMutations(unittest.TestCase):
    def fixture(self, root):
        root=root.resolve()
        folder=root/'test/TP07Generated'/('A'*32);folder.mkdir(parents=True)
        for index in range(5):(folder/('Source'+str(index)+'.cs')).write_text('approved '+str(index))
        (folder/'TP07Generated.csproj').write_text('<Project/>')
        hashes=V.generated_hashes(root,folder);directory=str(folder.relative_to(root))
        generation=dict(directory=directory,project=directory+'/TP07Generated.csproj',source_hashes=hashes,replay_root=str(root),parent_build_manifest_sha256='a'*64)
        parent=dict(status='generated_pending_external_build',exit_code=0,cleanup_verified=False,generation=generation)
        old=dict(replay_root=str(root),lineage={'pin':'v5'},W={'pin':'W'},source_pack={'pin':'D25'},source_hashes={'base.cs':'b'*64},locks={'base.lock':'c'*64},assets={'base.assets':'d'*64},startup_assemblies=[{'name':'TP07TemporalDesign','dll_sha256':'e'*64}])
        new=copy.deepcopy(old);new.update(parent_build_manifest_sha256='a'*64,generated_closure={key:generation[key] for key in ('directory','project','source_hashes')})
        new['source_hashes'].update(hashes);new['locks'][directory+'/packages.lock.json']='1'*64;new['assets'][directory+'/obj/project.assets.json']='2'*64
        self.manifest_refs(root,old,'old');self.manifest_refs(root,new,'new')
        return parent,old,new,folder
    def manifest_refs(self,root,start,prefix):
        for name in ('source_manifest','lock_manifest'):
            document={key:copy.deepcopy(start[key]) for key in ('replay_root','source_pack','source_hashes','lineage','W')}
            document['schema_version']=1
            if name=='lock_manifest':document['locks']=copy.deepcopy(start['locks'])
            path=root/(prefix+'-'+name+'.json');path.write_text(json.dumps(document))
            start[name+'_path']=str(path);start[name+'_sha256']=V.sha(path)
    def test_checkpoint_exact_positive(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);V.continuation(parent,old,new,root)
    def test_generated_source_splice(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,folder=self.fixture(root);(folder/'Source0.cs').write_text('different')
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_parent_build_SHA_splice(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);new['parent_build_manifest_sha256']='0'*64
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_parent_report_SHA_splice(self):
        with self.assertRaises(V.ContractError):V.parent_SHA_binding({'report':{'sha256':'a'*64}}, {'parent_report_sha256':'b'*64},{'parent_report_sha256':'a'*64})
    def test_replay_root_rebase(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);new['replay_root']=str(root/'other')
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_unapproved_generated_source(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,folder=self.fixture(root);(folder/'Injected.cs').write_text('extra')
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_original_manifest_pin_mutation(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);new['lineage']['pin']='different'
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_generated_path_escape(self):
        with self.assertRaises(V.ContractError):V.relative('../outside/Program.cs')
    def test_child_new_immutable_manifest_paths_positive(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root)
            self.assertNotEqual(old['source_manifest_path'],new['source_manifest_path']);self.assertNotEqual(old['lock_manifest_path'],new['lock_manifest_path'])
            before={name:V.sha(old[name+'_path']) for name in ('source_manifest','lock_manifest')};V.continuation(parent,old,new,root)
            self.assertEqual(before,{name:V.sha(old[name+'_path']) for name in before})
    def test_child_manifest_new_path_wrong_SHA(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);new['source_manifest_sha256']=old['source_manifest_sha256']
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_child_manifest_foreign_root_or_pack(self):
        for field in ('replay_root','source_pack'):
            with tempfile.TemporaryDirectory() as temp:
                root=Path(temp).resolve();parent,old,new,_=self.fixture(root);path=Path(new['source_manifest_path']);document=V.load(path)
                document[field]=str(root/'foreign') if field=='replay_root' else {'pin':'foreign25'};path.write_text(json.dumps(document));new['source_manifest_sha256']=V.sha(path)
                with self.subTest(field=field),self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_child_manifest_collection_mismatch(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);path=Path(new['lock_manifest_path']);document=V.load(path)
            document['source_hashes']['base.cs']='f'*64;path.write_text(json.dumps(document));new['lock_manifest_sha256']=V.sha(path)
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def test_old_parent_manifest_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();parent,old,new,_=self.fixture(root);Path(old['source_manifest_path']).write_text('{}')
            with self.assertRaises(V.ContractError):V.continuation(parent,old,new,root)
    def commands(self, kinds):
        table='TPL_'+'A'*24;stage='DOWN-G03-refusal-dt'
        return table,stage,{'commands':[dict(sequence=index,command_id=index+1,api='ExecuteNonQueryAsync',stage=stage,status='attempted',kind=kind,
            sql_sha256=V.sqlsha(V.emitted_guard_sql(table)) if kind=='guard' else '1'*64) for index,kind in enumerate(kinds)],'protocol_frames':[]}
    def test_guard_after_DDL(self):
        table,stage,report=self.commands(['ddl','guard'])
        with self.assertRaises(V.ContractError):V.guard_order(report,stage,table,True)
    def test_guard_catch_continue_history_mutation(self):
        table,stage,report=self.commands(['guard','history_mutation'])
        with self.assertRaises(V.ContractError):V.guard_order(report,stage,table,True)
    def test_guard_error_wrong_number(self):
        error=dict(type='W.Dm.DmException',number=-20070,verified_server_response=True,source='physical_channel_receive_opcode',stage='case',response={})
        with self.assertRaises(V.ContractError):V.guard_error(error,{'protocol_frames':[]},'case')
    def state(self):
        return dict(default='D',columns_except_scale='C',indexes='I',constraints='K',history_columns='HC',history_constraints='HK',rows=[{'id':1,'ticks':7}],
            column_scale=7,columns='CC',history_rows=[{'migration_id':'Up7'}])
    def test_missing_before_after_history(self):
        before=self.state();after=copy.deepcopy(before);del after['history_rows']
        with self.assertRaises(KeyError):V.preserved(before,after)
    def test_refusal_schema_corruption(self):
        before=self.state();after=copy.deepcopy(before);after['column_scale']=6
        with self.assertRaises(V.ContractError):V.preserved(before,after)
    def test_refusal_value_corruption(self):
        before=self.state();after=copy.deepcopy(before);after['rows'][0]['ticks']=0
        with self.assertRaises(V.ContractError):V.preserved(before,after)
    def test_default_CHECK_index_corruption(self):
        for field in ('default','constraints','indexes'):
            before=self.state();after=copy.deepcopy(before);after[field]='changed'
            with self.subTest(field=field),self.assertRaises(V.ContractError):V.preserved(before,after)
    def test_missing_CLI_identity(self):
        value=dict(task='TP07-D-process',final=True,observer_failed=False,startup_identity={},framework_assembly_sha256='a'*64)
        with patch.object(V,'artifact',return_value=value),patch.object(V,'runtime',return_value=None):
            with self.assertRaises(KeyError):V.process_identity({}, {'shared_framework':{'hosting_dll_sha256':'a'*64}},'b'*64,{},False)
    def helper(self):
        return V.pure_helpers({'source_hashes':V.policy()['immutable_source_hashes']})
    def test_cleanup_missing_owned_table(self):
        with self.assertRaises(V.ContractError):V.cleanup_proof({'cleanup_verified':True,'objects':[]},[{'table':'T','nonce':'N'}],self.helper(),'shared')
    def test_unknown_create_no_ACK_marker(self):
        class Helpers:
            @staticmethod
            def cleanup(*_):pass
        with self.assertRaises(V.ContractError):V.cleanup_proof({'objects':[{'table':'T','dropped':True}]},
            [{'table':'T','create_acknowledged':False,'comment_acknowledged':False}],Helpers(),'shared')
    def test_missing_Dv3_raw_receipts(self):
        with self.assertRaises(KeyError):V.row_oracles({'rows':[]},{},'dt',7,None,'shared','T')
    def guard_fixture(self,migrated=True,rejected=False):
        table='TPL_'+'A'*24;stage='DOWN-G03-refusal-dt' if migrated else 'DOWN-G02-control-dt'
        digest=V.sqlsha(V.emitted_guard_sql(table) if migrated else V.guard_sql(table))
        attempt=dict(sequence=0,stage=stage,status='attempted',kind='guard',api='ExecuteNonQueryAsync',command_id=10,statement_id=None,sql_sha256=digest)
        terminal=dict(sequence=1,stage=stage,status='failed' if rejected else 'completed',kind='guard',command_id=10,statement_id=100)
        if rejected:terminal['error']=dict(type='W.Dm.DmException',code=None,number=-20071)
        else:terminal.update(api='ExecuteNonQueryAsync',sql_sha256=digest)
        frame=dict(sequence=0,stage=stage,request_opcode=13,response_opcode=13,sql_code=-20071 if rejected else 0,body_length=0,command_id=10,statement_id=100)
        report=dict(commands=[attempt,terminal],protocol_frames=[frame]);return table,stage,report,attempt
    def test_raw_control_exact_bytes_positive(self):
        table,stage,report,attempt=self.guard_fixture(False)
        self.assertEqual(V.guard_attempts(report,stage,table),[attempt]);V.guard_outcome(report,stage,attempt,False,(0,2),(0,1))
    def test_EF_guard_exact_terminal_LF_positive(self):
        table,stage,report,attempt=self.guard_fixture(True,True)
        self.assertEqual(V.guard_order(report,stage,table,True),attempt)
    def test_EF_guard_missing_terminal_LF(self):
        table,stage,report,attempt=self.guard_fixture(True,True);attempt['sql_sha256']=V.sqlsha(V.guard_sql(table))
        with self.assertRaises(V.ContractError):V.guard_order(report,stage,table,True)
    def test_raw_guard_extra_LF(self):
        table,stage,report,attempt=self.guard_fixture(False);attempt['sql_sha256']=V.sqlsha(V.emitted_guard_sql(table))
        with self.assertRaises(V.ContractError):V.guard_attempts(report,stage,table)
    def test_guard_content_or_extra_SQL(self):
        for sql in (V.guard_sql('TPL_'+'A'*24).replace('1000','100'),V.guard_sql('TPL_'+'A'*24)+'\nDELETE FROM X;'):
            table,stage,report,attempt=self.guard_fixture(True,True);attempt['sql_sha256']=V.sqlsha(sql)
            with self.subTest(sql=sql),self.assertRaises(V.ContractError):V.guard_order(report,stage,table,True)
    def test_missing_guard(self):
        table,stage,report,_=self.guard_fixture(True,True);report['commands']=[]
        with self.assertRaises(V.ContractError):V.guard_order(report,stage,table,True)
    def test_missing_raw_guard_completion(self):
        _,stage,report,attempt=self.guard_fixture(False);report['commands'].pop()
        with self.assertRaises(V.ContractError):V.guard_outcome(report,stage,attempt,False,(0,1),(0,1))
    def test_wrong_guard_completion_API(self):
        _,stage,report,attempt=self.guard_fixture(False);report['commands'][1]['api']='ExecuteReaderAsync'
        with self.assertRaises(V.ContractError):V.guard_outcome(report,stage,attempt,False)
    def test_guard_borrowed_response_command(self):
        _,stage,report,attempt=self.guard_fixture(False,True);report['protocol_frames'][0]['command_id']=11
        with self.assertRaises(V.ContractError):V.guard_outcome(report,stage,attempt,True,(0,2),(0,1))
    def test_guard_response_outside_call_window(self):
        _,stage,report,attempt=self.guard_fixture(False,True)
        report['protocol_frames'].insert(0,dict(report['protocol_frames'][0],sequence=0,command_id=99));report['protocol_frames'][1]['sequence']=1
        with self.assertRaises(V.ContractError):V.guard_outcome(report,stage,attempt,True,(0,2),(0,1))
    def test_G03_error_other_guard_command(self):
        _,stage,report,attempt=self.guard_fixture(True,True)
        borrowed=dict(report['protocol_frames'][0],sequence=1,command_id=99);report['protocol_frames'].append(borrowed)
        error=dict(type='W.Dm.DmException',number=-20071,verified_server_response=True,source='physical_channel_receive_opcode',stage=stage,response=borrowed)
        with self.assertRaises(V.ContractError):V.guard_error(error,report,stage,attempt)
    def success_migration(self,history_first=False):
        table,stage,report,attempt=self.guard_fixture(True)
        kinds=['history_mutation','alter'] if history_first else ['alter','history_mutation']
        for kind in kinds:
            command_id=20+len(report['commands']);sequence=len(report['commands'])
            report['commands'].append(dict(sequence=sequence,stage=stage,status='attempted',kind=kind,api='ExecuteNonQueryAsync',command_id=command_id,sql_sha256='a'*64))
            report['commands'].append(dict(sequence=sequence+1,stage=stage,status='completed',kind=kind,api='ExecuteNonQueryAsync',command_id=command_id,sql_sha256='a'*64))
        return table,stage,report,attempt
    def test_guard_ALTER_history_positive(self):
        table,stage,report,attempt=self.success_migration();self.assertEqual(V.guard_order(report,stage,table,False),attempt)
    def test_history_before_ALTER(self):
        table,stage,report,_=self.success_migration(True)
        with self.assertRaises(V.ContractError):V.guard_order(report,stage,table,False)
    def test_G01_missing_real_guard_call(self):
        table,stage,report,attempt=self.guard_fixture(False);report['commands'][0]['kind']='read'
        with self.assertRaises(V.ContractError):V.extreme_guard_calls(report,stage,table,[0,1,99,98,90])
    def test_G01_five_real_outcomes_positive(self):
        table='TPL_'+'A'*24;stage='DOWN-G01-extremes-dt';values=[0,1,99,98,90];report={'commands':[],'protocol_frames':[]}
        for index,ticks in enumerate(values):
            _,_,part,_=self.guard_fixture(False,ticks%10!=0)
            for row in part['commands']:
                row.update(stage=stage,sequence=len(report['commands']),command_id=10+index);report['commands'].append(row)
            for row in part['protocol_frames']:
                row.update(stage=stage,sequence=len(report['protocol_frames']),command_id=10+index);report['protocol_frames'].append(row)
        V.extreme_guard_calls(report,stage,table,values)
        report['commands'][3]['error']['number']=-20070
        with self.assertRaises(V.ContractError):V.extreme_guard_calls(report,stage,table,values)
    def test_G02_cross_owned_oracle(self):
        class Helpers:
            @staticmethod
            def profile(*_):pass
        receipt=dict(id=1,table='OTHER',column='V',read_evidence={'oracle_read_attempts':[dict(completed=True,table='T',row_id=1,column='V',rows=1,identity={})]})
        with self.assertRaises(V.ContractError):V.owned_read_evidence(receipt,'T',1,Helpers(),'shared')
    def test_G02_wrong_read_row_id(self):
        class Helpers:
            @staticmethod
            def profile(*_):pass
        receipt=dict(id=1,table='T',column='V',read_evidence={'oracle_read_attempts':[dict(completed=True,table='T',row_id=2,column='V',rows=1,identity={})]})
        with self.assertRaises(V.ContractError):V.owned_read_evidence(receipt,'T',1,Helpers(),'shared')
    def test_cleanup_host_independent_of_broken_workflow_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();home=root/'cli-home';home.mkdir();host=root/'dotnet';host.write_bytes(b'approved host')
            tool_root=root/'tool-root';tool_root.mkdir()
            manifest=dict(schema_version=1,task='TP07-D-cli-tool',task_root=str(root),cli_home=str(home),tool_root=str(tool_root),dotnet_host=dict(path=str(host),sha256=V.sha(host)),
                replay_root=str(root/'missing-workflow'),local_tool_manifest={'path':str(root/'missing.json'),'sha256':'0'*64},package={'path':str(root/'missing.nupkg'),'sha256':'1'*64},tool_files={})
            path=root/'tool.json';path.write_text(json.dumps(manifest));reference=dict(path=str(path),sha256=V.sha(path))
            _,actual=V.cleanup_host(reference);self.assertEqual(actual,host)
            with self.assertRaises((OSError,V.ContractError)) as rejected:V.tool_bundle(reference,Path(manifest['replay_root']))
            self.assertIsInstance(rejected.exception,FileNotFoundError)
            self.assertEqual(Path(rejected.exception.filename),Path(manifest['local_tool_manifest']['path']))
    def test_cleanup_host_invalid_own_host_stays_pending(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();home=root/'cli-home';home.mkdir();host=root/'dotnet';host.write_bytes(b'changed')
            path=root/'tool.json';path.write_text(json.dumps(dict(schema_version=1,task='TP07-D-cli-tool',task_root=str(root),cli_home=str(home),dotnet_host={'path':str(host),'sha256':'0'*64})))
            with self.assertRaises(V.ContractError):V.cleanup_host(dict(path=str(path),sha256=V.sha(path)))
    def test_bad_workflow_gate_still_attempts_protected_cleanup_preserves_failure(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();ledger=root/'active-ledger.json';ledger.write_text('{"objects":[]}')
            config=dict(parent_checkpoint={'report':{'path':'parent','sha256':'a'*64}},source_pack={},cleanup_startup_manifest={},profile='shared')
            def documents(reference):return {'ownership_ledger_active':str(ledger)} if reference.get('path')=='parent' else config
            attempted=[]
            def cleanup(plan_ref,configuration,start,output,role,active_ledger):
                attempted.append((role,Path(active_ledger)));Path(output).mkdir(parents=True)
                (Path(output)/'report.json').write_text(json.dumps({'status':'cleanup_complete','cleanup':{},'finally_cleanup':{}}))
                return {'exit_code':0,'timed_out':False,'termination_code':None},{}
            helpers=types.SimpleNamespace()
            with (patch.object(R.V,'artifact',side_effect=documents),patch.object(R.V,'plan',side_effect=R.V.ContractError('broken_workflow_CLI')),
                 patch.object(R.V,'pack',return_value={'source_hashes':{}}),patch.object(R.V,'startup',return_value={'replay_root':str(root/'protected')}),
                 patch.object(R.V,'pure_helpers',return_value=helpers),patch.object(R.V,'cleanup_proof',return_value=None),patch.object(R,'wrapped',side_effect=cleanup)):
                code=R.stage(types.SimpleNamespace(plan=str(root/'plan.json'),plan_sha256='a'*64,output=str(root/'output')))
            receipt=json.loads((root/'output/execution-receipt.json').read_text())
            self.assertEqual(attempted,[('cleanup',ledger)]);self.assertEqual(code,1);self.assertEqual(receipt['status'],'terminal_failed')
            self.assertEqual(receipt['independent_cleanup']['process']['exit_code'],0)



class TransactionSourceContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workspace = next(root for root in HERE.parents if (root / '.local/temporal-native/verification/TP07-L0-transaction-v2-prepare/source-manifest.json').is_file())
        cls.verification = cls.workspace / '.local/temporal-native/verification'
        cls.l0 = V.load(cls.verification / 'TP07-L0-transaction-v2-prepare/source-manifest.json')
        cls.l0lock = V.load(cls.verification / 'TP07-L0-transaction-v2-lock/lock-manifest.json')
        refs = cls.l0['lineage_artifacts']
        cls.old = V.artifact(refs['approved_ef_source_manifest']); cls.product = V.artifact(refs['transaction_product_manifest'])
        cls.approval = V.artifact(refs['transaction_source_approval'])
        cls.patch_text = V.file_artifact(refs['transaction_patch']).read_text()
        cls.pack_path = cls.verification / 'TP07-D-transaction-tools-v1/source-pack-manifest.json'
        cls.pack = V.artifact(dict(path=str(cls.pack_path), sha256=V.sha(cls.pack_path)))
        cls.identity = dict(manifest_path=str(cls.pack_path), manifest_sha256=V.sha(cls.pack_path), source_hashes=cls.pack['source_hashes'])

    def semantic(self, l0=None, old=None, product=None, approval=None, patch_text=None):
        return V.transaction_contract(self.l0 if l0 is None else l0, self.old if old is None else old,
            self.product if product is None else product, self.approval if approval is None else approval,
            self.patch_text if patch_text is None else patch_text, self.workspace)

    def collection(self, stage, generated=None):
        hashes = dict(self.l0['source_hashes'])
        if stage not in ('cleanup', 'cleanup-native'):
            hashes.update({'test/' + name.split('runtime-templates/', 1)[1]: digest for name, digest in self.pack['source_hashes'].items()
                if name.startswith(V.PREFIX + 'runtime-templates/TP07TemporalDesign/')})
            config = (json.dumps({'version': 1, 'isRoot': True, 'tools': {'dotnet-ef': {'version': '10.0.12', 'commands': ['dotnet-ef'], 'rollForward': False}}}, indent=2, sort_keys=True) + '\n').encode()
            hashes['.config/dotnet-tools.json'] = hashlib.sha256(config).hexdigest()
        if stage not in ('workflow', 'cleanup'):
            hashes['test/TP07LiteralProbe/packages.lock.json'] = self.l0lock['lock_sha256']
            if stage != 'cleanup-native': hashes['test/TP07TemporalDesign/packages.lock.json'] = 'a' * 64
        if generated is not None:
            hashes.update(generated['source_hashes']); hashes[generated['directory'] + '/packages.lock.json'] = 'b' * 64
        return hashes

    def generated(self, count):
        directory = 'test/TP07Generated/' + 'A' * 32
        rows = {directory + '/Source' + str(index) + '.cs': 'c' * 64 for index in range(count - 1)}
        rows[directory + '/TP07Generated.csproj'] = 'd' * 64
        return dict(directory=directory, project=directory + '/TP07Generated.csproj', source_hashes=rows)

    def source(self, stage, generated=None):
        references = V.load(self.workspace / '.local/temporal-native/root-approvals/TP07-D-preparation-v6-inputs/inputs.json')['references']
        references.update(source_pack=dict(path=str(self.pack_path), sha256=V.sha(self.pack_path)),
            l0_source=dict(path=str(self.verification / 'TP07-L0-transaction-v2-prepare/source-manifest.json'), sha256=V.L0_SOURCE_SHA),
            l0_lock=dict(path=str(self.verification / 'TP07-L0-transaction-v2-lock/lock-manifest.json'), sha256=V.L0_LOCK_SHA),
            design=dict(path=str(self.workspace / 'docs/implementation/temporal-native/TP07-D-transaction-v1-preparation-design.md'), sha256=V.DESIGN_SHA))
        return dict(schema_version=1, source_pack=self.identity, replay_root=str(self.verification / 'contract-only-replay'),
            source_hashes=self.collection(stage, generated), lineage=copy.deepcopy(self.l0['lineage']), W=self.l0['W'],
            origin_refs={key:references[key] for key in ('source_pack', 'l0_source', 'l0_lock', 'packages', 'design', 'pruning_design')})

    def test_actual_175_178_185_origins(self):
        self.assertEqual(self.product['source_hashes'], self.semantic())
        source = self.source('workflow'); refs = source['origin_refs']
        l0, locked, protected = V.transaction_origins(self.workspace, refs['l0_source'], refs['l0_lock'],
            self.verification / 'TP07-L0-transaction-v2-prepare/replay', self.pack['source_hashes'])
        self.assertEqual(185, len(l0['source_hashes'])); self.assertEqual(186, len(locked['source_hashes']))
        self.assertIn(refs['l0_source']['path'], protected)

    def test_transaction_three_lineage_wrong_or_missing(self):
        for key in V.TRANSACTION_PINS:
            for missing in (False, True):
                l0 = copy.deepcopy(self.l0)
                if missing: del l0['lineage'][key]
                else: l0['lineage'][key] = '0' * 64
                code = 'transaction_ten_lineage_fields' if missing else 'transaction_lineage_pins'
                with self.subTest(key=key, missing=missing), self.assertRaisesRegex(V.ContractError, '^' + code + '$'): self.semantic(l0=l0)

    def test_approval_scope_root_and_manifest_binding(self):
        approval = copy.deepcopy(self.approval); approval['scope'] = 'anything_accepted'
        with self.assertRaisesRegex(V.ContractError, '^transaction_approval_scope$'): self.semantic(approval=approval)
        product = copy.deepcopy(self.product); product['source_root'] += '-other'
        with self.assertRaisesRegex(V.ContractError, '^transaction_product_root$'): self.semantic(product=product)
        approval = copy.deepcopy(self.approval); approval['manifest']['path'] += '-other'
        with self.assertRaisesRegex(V.ContractError, '^transaction_nested_ref_binding$'): self.semantic(approval=approval)

    def test_full_product_and_approval_schema(self):
        product = copy.deepcopy(self.product); product['extra'] = True
        with self.assertRaisesRegex(V.ContractError, '^transaction_product_43_fields$'): self.semantic(product=product)
        approval = copy.deepcopy(self.approval); del approval['exact_paths']
        with self.assertRaisesRegex(V.ContractError, '^transaction_approval_7_fields$'): self.semantic(approval=approval)

    def test_manifest_175_178_and_signed_patch_mismatch(self):
        l0 = copy.deepcopy(self.l0); l0['approved_ef_source_hashes'] = dict(self.product['source_hashes'])
        with self.assertRaisesRegex(V.ContractError, '^transaction_original_175_mapping$'): self.semantic(l0=l0)
        l0 = copy.deepcopy(self.l0); l0['product_ef_source_hashes'] = dict(self.old['source_hashes'])
        with self.assertRaisesRegex(V.ContractError, '^transaction_current_178_mapping$'): self.semantic(l0=l0)
        text = self.patch_text.replace(next(iter(V.MODIFIED_PATHS)), 'unapproved/Other.cs')
        with self.assertRaisesRegex(V.ContractError, '^transaction_signed_patch_paths$'): self.semantic(patch_text=text)

    def test_same_count_product_replacement_and_extra_missing(self):
        for change in ('replacement', 'extra', 'missing'):
            product = copy.deepcopy(self.product); name = next(iter(V.ADDED_PATHS))
            if change != 'extra': del product['source_hashes'][name]
            if change != 'missing': product['source_hashes']['unapproved/Other.cs'] = '0' * 64
            with self.subTest(change=change), self.assertRaisesRegex(V.ContractError, '^transaction_exact_173_2_3_delta$'): self.semantic(product=product)

    def test_old_182_template_and_185_same_count_replacement(self):
        for change in ('old182', 'replacement'):
            l0 = copy.deepcopy(self.l0)
            if change == 'old182': l0['source_hashes'] = dict(self.old['source_hashes'], **{'test/' + key:value for key,value in self.l0['runtime_template_hashes'].items()})
            else:
                name = next(iter(V.ADDED_PATHS)); l0['source_hashes']['unapproved/Other.cs'] = l0['source_hashes'].pop(name)
            with self.subTest(change=change), self.assertRaisesRegex(V.ContractError, '^transaction_exact_L0_185_mapping$'): self.semantic(l0=l0)

    def test_all_exact_D_stage_collections(self):
        for stage, count in (('workflow',196),('cleanup',185),('native',198),('cleanup-native',186),('generated6',205),('generated12',211)):
            generated = self.generated(6 if stage == 'generated6' else 12) if stage.startswith('generated') else None
            hashes = self.collection(stage, generated)
            with self.subTest(stage=stage): self.assertEqual(count, len(V.d_source_collection(hashes,self.l0,self.l0lock,self.pack['source_hashes'],stage,generated)))

    def test_stage_same_count_replacement_extra_missing_or_lock(self):
        for stage in ('workflow','cleanup','native','cleanup-native','generated6','generated12'):
            generated = self.generated(6 if stage == 'generated6' else 12) if stage.startswith('generated') else None
            for change in ('replacement','extra','missing','hash'):
                hashes = self.collection(stage,generated); name = next(iter(self.l0['product_ef_source_hashes']))
                if change == 'replacement': hashes['unapproved/Other.cs'] = hashes.pop(name)
                elif change == 'extra': hashes['unapproved/Other.cs'] = '0' * 64
                elif change == 'missing': del hashes[name]
                else: hashes[name] = '0' * 64
                with self.subTest(stage=stage,change=change), self.assertRaisesRegex(V.ContractError, '^transaction_exact_D_' + stage + '_collection$'):
                    V.d_source_collection(hashes,self.l0,self.l0lock,self.pack['source_hashes'],stage,generated)
        hashes = self.collection('native'); hashes['test/TP07LiteralProbe/packages.lock.json'] = '0' * 64
        with self.assertRaisesRegex(V.ContractError, '^transaction_exact_D_native_collection$'): V.d_source_collection(hashes,self.l0,self.l0lock,self.pack['source_hashes'],'native')

    def test_generated_same_count_wrong_directory(self):
        generated = self.generated(6); hashes = self.collection('generated6',generated)
        generated['source_hashes'] = {name.replace('/' + 'A' * 32 + '/', '/' + 'B' * 32 + '/'):digest for name,digest in generated['source_hashes'].items()}
        with self.assertRaisesRegex(V.ContractError, '^transaction_exact_generated_paths$'):
            V.d_source_collection(hashes,self.l0,self.l0lock,self.pack['source_hashes'],'generated6',generated)

    def test_startup_origins_workflow_cleanup_G6_G12(self):
        for stage in ('native','cleanup-native','generated6','generated12'):
            generated = self.generated(6 if stage == 'generated6' else 12) if stage.startswith('generated') else None
            source = self.source(stage,generated); value = dict(source)
            value['locks'] = {name:digest for name,digest in source['source_hashes'].items() if name.endswith('/packages.lock.json') and (name.startswith('src/W.EntityFrameworkCore.Dameng/') or name.startswith('test/TP07LiteralProbe/') or name.startswith('test/TP07TemporalDesign/') or name.startswith('test/TP07Generated/'))}
            if generated is not None: value['generated_closure'] = generated
            with self.subTest(stage=stage): V.startup_origins(value,self.identity,dict(source_manifest=source,lock_manifest={}),stage=='cleanup-native')
            source['source_hashes']['unapproved/Other.cs'] = source['source_hashes'].pop(next(iter(V.ADDED_PATHS)))
            with self.subTest(stage=stage,negative=True), self.assertRaisesRegex(V.ContractError, '^transaction_exact_D_' + stage + '_collection$'):
                V.startup_origins(value,self.identity,dict(source_manifest=source,lock_manifest={}),stage=='cleanup-native')

    def test_actual_startup_wires_source_gate_before_binary_identity(self):
        value = self.source('native'); value['task'] = 'TP07-D-startup'; value['locks'] = {}; value['assets'] = {}
        value['source_manifest_path'] = '/not-read'; value['lock_manifest_path'] = '/not-read'; value['startup_assemblies'] = []
        with patch.object(V,'artifact',return_value=value), patch.object(V,'source_files'), patch.object(V,'source_lock_documents',return_value={}), \
             patch.object(V,'startup_origins',side_effect=V.ContractError('source_gate_sentinel')) as origin, patch.object(V,'managed_mvid',side_effect=AssertionError('binary_reached')):
            # A disposable temporary root satisfies the existing independent root gate.
            with tempfile.TemporaryDirectory() as temp:
                value['replay_root'] = temp
                with self.assertRaisesRegex(V.ContractError,'^source_gate_sentinel$'): V.startup({},self.identity)
        origin.assert_called_once()

    def test_overlay_nine_fields_and_runtime_template_independence(self):
        left = self.l0['lineage']; right = copy.deepcopy(left); right['runtime_template_source_sha256'] = 'f' * 64
        V.same_overlay(left,right)
        keys = ('ef_commit','base_archive_sha256','bridge_patch_sha256','mapping_manifest_sha256','mapping_patch_sha256','approved_ef_source_manifest_sha256') + tuple(V.TRANSACTION_PINS)
        for key in keys:
            for missing in (False,True):
                bad = copy.deepcopy(right)
                if missing: del bad[key]
                else: bad[key] = '0' * 64
                with self.subTest(key=key,missing=missing), self.assertRaisesRegex(V.ContractError,'^same_EF_mapping_v5_overlay_lineage$'): V.same_overlay(left,bad)

    def test_model_dependency_adapters_compare_all_nine_fields(self):
        gate = dict(schema_version=1, task='TP07-D-model-gates',status='accepted_in_scope',profile='shared',W=self.l0['W'],lineage=copy.deepcopy(self.l0['lineage']),accepted_gate_refs={'L0':'L0','E':'E'})
        accepted = {name:dict(status='accepted_in_scope',issues=[],W=self.l0['W'],profile='shared',lineage=copy.deepcopy(self.l0['lineage']),evidence_refs=['evidence']) for name in ('L0','E')}
        accepted['E']['lineage']['runtime_template_source_sha256'] = 'f' * 64
        def reader(ref): return gate if ref == 'gate' else {} if ref == 'evidence' else accepted[ref]
        with patch.object(V,'artifact',side_effect=reader): V.dependencies('gate',dict(lineage=self.l0['lineage']),'shared')
        keys = ('ef_commit','base_archive_sha256','bridge_patch_sha256','mapping_manifest_sha256','mapping_patch_sha256','approved_ef_source_manifest_sha256') + tuple(V.TRANSACTION_PINS)
        for name in ('L0','E'):
            for key in keys:
                original = accepted[name]['lineage'][key]; accepted[name]['lineage'][key] = '0' * 64
                with self.subTest(adapter=name,key=key), patch.object(V,'artifact',side_effect=reader), self.assertRaisesRegex(V.ContractError,'^same_EF_mapping_v5_overlay_lineage$'):
                    V.dependencies('gate',dict(lineage=self.l0['lineage']),'shared')
                accepted[name]['lineage'][key] = original


if __name__=='__main__':unittest.main()
