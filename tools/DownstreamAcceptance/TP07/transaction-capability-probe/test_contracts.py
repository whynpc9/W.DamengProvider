"""Handwritten synthetic contracts; no SDK, wrapper, secret, or database I/O.

These fixtures are not producer execution or live acceptance. No producer module,
provider codec, or fake ROOT is imported. Low runs this file after source review.
"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import shutil
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('tp07_t1_validator_contract', HERE / 'validate.py')
V = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(V)
H = 'a'*64
G = '12345678-1234-1234-1234-123456789abc'
SEED_G = '22345678-1234-1234-1234-123456789abc'
CG = '32345678-1234-1234-1234-123456789abc'
TICKS = 639204129161234560
IDS = ['T1-shared-sync-pair','T1-shared-async-pair','T1-tls-sync-pair','T1-tls-async-pair']
STEPS = ['runtime_identity','create','seed_SaveChanges','fresh_before','begin_transaction','capabilities','tracking_query','transaction_SaveChanges','query_inside_transaction','full_Rollback','dispose_transaction_context','fresh_after']


def reference(name='/synthetic/file.json'):
    return dict(path=name,sha256=H)


def identity(type_name,name,assembly):
    return dict(type=type_name,name=name,path=assembly['path'],sha256=assembly['sha256'],mvid=assembly['mvid'])


def physical(profile):
    version,message,encoding = ('8.1.5.60',21,'utf-8') if profile=='shared' else ('8.1.4.6',11,'gb18030')
    return dict(profile=profile,server_version=version,message_version=message,encoding=encoding,negotiated_encoding=encoding,negotiated_encoding_origin='physical_conn_property_serverEncoding_field',encoding_override_empty=True,profile_origin='physical_get_conn_instance_conn_property',server_user='WDM_PROVIDER_TEST',server_schema='WDM_PROVIDER_TEST',result_format='ordinary_non_BDTA',timezone=480)


def row(target=TICKS,n='first'):
    return dict(id=1,d=dict(type='System.DateTime',ticks=target,kind='Unspecified'),z=dict(type='System.DateTimeOffset',ticks=target,offset_minutes=330,utc_ticks=target-198000000000),n=n)


def handwritten_raw(scale,offset):
    # Fixed sample calendar from the reviewed contract, independently assembled
    # in the test fixture. The production validator has only a decoder.
    date = 2026 | (7 << 15) | (23 << 19)
    fraction = 123456000 if scale==7 else 123456
    time = 14 | (15 << 5) | (16 << 11) | (fraction << 17)
    payload = date.to_bytes(3,'little') + time.to_bytes(6 if scale==7 else 5,'little')
    if offset:
        payload += (330).to_bytes(2,'little',signed=True)
    return payload


def oracle(profile,scale,offset,w):
    payload=handwritten_raw(scale,offset)
    ctype=(27 if offset else 26) if scale==7 else (23 if offset else 16)
    fields=dict(year=2026,month=7,day=23,hour=14,minute=15,second=16,nanoseconds=123456000,offset_minutes=330 if offset else None)
    return dict(rows=1,is_null=False,local_ticks=TICKS,offset_minutes=330 if offset else None,raw=dict(source='ordinary_result_cache_before_temporal_projection',capture_stage='before_any_temporal_getter',c_type=ctype,width=len(payload),scale=scale,is_bdta=False,hex=payload.hex().upper(),payload_length=len(payload),payload_sha256=hashlib.sha256(payload).hexdigest(),decoded=fields),ff9='2026-07-23 14:15:16.123456000'+(' +05:30' if offset else ''),typed=row()['z' if offset else 'd'],metadata=dict(c_type=ctype,width=len(payload),scale=scale,mask=0,is_bdta=False,metadata_role='result_column',raw_source='ResultColumn',raw_c_type=ctype,raw_scale=scale,raw_precision=len(payload),raw_mask_before=0,raw_derived_mask=0,raw_scale_status='captured_server_source',schema_scale=scale),identity=physical(profile),reader_route=dict(role='reader',type='W.Dm.DmDataReader',dll_sha256=w['dll_sha256'],mvid=w['mvid']))


def fixture(ordinal):
    """Four complete handwritten reports, independent of producer output flags."""
    profile='shared' if ordinal<2 else 'tls'; async_api=ordinal%2==1; scale=7 if ordinal<2 else 6; quantum=1 if ordinal<2 else 10
    host=dict(type='host',name='TP07TransactionCapabilityProbe',path='/synthetic/TP07TransactionCapabilityProbe.dll',sha256=H,mvid=G,deps=reference('/synthetic/TP07TransactionCapabilityProbe.deps.json'))
    provider=dict(type='provider',name='W.EntityFrameworkCore.Dameng',path='/synthetic/W.EntityFrameworkCore.Dameng.dll',sha256=H,mvid=G)
    w=dict(version='0.1.0-temporal.20261008144915',package_sha256=H,dll_sha256=H,mvid=G,identity_sha256=H,source_manifest_sha256=H,archive_sha256=H,path='/synthetic/W.DmProvider.dll')
    startup=dict(W=w,host=host,ef_provider=provider,source=reference(),native=reference())
    runtime=dict(version=w['version'],package_sha256=H,dll_sha256=H,mvid=G,deps_sha256=H,runtime='10.0.1',ef_package_version='10.0.12',ef_provider=dict(dll_sha256=H,mvid=G,source='isolated_provider_ProjectReference_build_manifest_verified'),source_manifest_sha256=H,archive_sha256=H,archive_check='byte_hash_only_no_archive_introspection',build_manifest_sha256=H,source_manifest_sha256_binding=H,lock_manifest_sha256=H,startup_assembly='TP07TransactionCapabilityProbe',startup_sha256=H,startup_mvid=G,entry_host_type='TP07TransactionCapabilityProbe',entry_host_sha256=H)
    nonce=('%08X'%(ordinal+1))+'0'*24
    entry=dict(case_id=IDS[ordinal],nonce=nonce,table='TPL_'+nonce[:24],owner_marker='OWN_'+nonce[:24],primary_key='TP07PK_'+nonce[:24],index=None,check=None,index_acknowledged=False,check_acknowledged=False,shape='Basic',scale=scale,alter_before_scale=None,alter_target_scale=None,alter_before_catalog=None,alter_after_catalog=None,creation_state='confirmed',absent_before_create=True,create_acknowledged=True,comment_acknowledged=True,create_evidence=dict(call_returned=True,api='ExecuteSqlRawAsync' if async_api else 'ExecuteSqlRaw',sql_sha256=H),comment_evidence=dict(call_returned=True,api='ExecuteNonQuery',sql_sha256=H),create_failure=None,columns=[dict(name=n,kind=k,scale=s,leading=None) for n,k,s in [('ID','INT',None),('D','DT',scale),('Z','TZ',scale),('N','VARCHAR32',None)]],literal_store_type=None)
    ledger=dict(schema_version=1,profile=profile,package_sha256=H,identity_sha256=H,objects=[entry])
    schema=dict(table=entry['table'],primary_key=entry['primary_key'],comment='TP07_'+nonce,columns=[dict(name=n,data_type=t,precision=None,scale=s,nullable='N',data_length=l) for n,t,s,l in [('ID','INT',0,4),('D','TIMESTAMP',scale,9 if scale==7 else 8),('Z','DATETIME WITH TIME ZONE',scale,11 if scale==7 else 10),('N','VARCHAR',None,32)]])
    def fresh(after):
        return dict(row=row(),row_count=1,context_object_id=41 if after else 21,connection_object_id=42 if after else 22,different_connection=True if after else None,identity=physical(profile),schema=copy.deepcopy(schema),d_oracle=oracle(profile,scale,False,w),z_oracle=oracle(profile,scale,True,w),auxiliary_io='catalog_raw_oracle_cleanup_synchronous')
    values=dict(before=fresh(False),inside=row(TICKS+quantum,'rollback'),after=fresh(True))
    tx=dict(read_stage='after_begin',read_sequence=530,capture_state='observed',ef=identity('W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransaction','W.EntityFrameworkCore.Dameng',provider),underlying=identity('W.Dm.DmTransaction','W.DmProvider',dict(path=w['path'],sha256=H,mvid=G)),ef_object_id=29,underlying_object_id=30,connection_object_id=32,context_object_id=31,transaction_id=G,current_same_instance=True,underlying_connection_same=True,ef_supports_savepoints=ordinal<2,underlying_supports_savepoints=ordinal<2,isolation_level='ReadCommitted')
    tx2=copy.deepcopy(tx);tx2.update(read_stage='before_write',read_sequence=715)
    observer=dict(collector=identity('TP07.Runtime.TransactionEvidence','TP07TransactionCapabilityProbe',host),command_collector=identity('TP07.Runtime.CommandEvidence','TP07TransactionCapabilityProbe',host),collector_object_id=1,command_collector_object_id=2,constructed_sequence=1,installed_sequence=2,observation_started_sequence=3,observation_completed_sequence=1200,registration=dict(transaction_count=1,command_count=1,same_transaction_instance=True,same_command_instance=True,source='IDbContextOptions_CoreOptionsExtension_Interceptors',context_object_id=31,inspected_sequence=400),capture_state='observed')
    factory=identity('W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory','W.EntityFrameworkCore.Dameng',provider)
    services=dict(factory=factory,before_ddl_factory=copy.deepcopy(factory),before_ddl_context_object_id=11,before_ddl_resolved_sequence=95,di_origin='context_GetService_IRelationalTransactionFactory',auto_savepoints_enabled=True)
    steps=[]
    api_list=['synchronous_auxiliary','ExecuteSqlRawAsync' if async_api else 'ExecuteSqlRaw','SaveChangesAsync' if async_api else 'SaveChanges','SingleAsync_CountAsync' if async_api else 'Single_Count','BeginTransactionAsync_ReadCommitted' if async_api else 'BeginTransaction_ReadCommitted','actual_getters','SingleAsync' if async_api else 'Single','SaveChangesAsync' if async_api else 'SaveChanges','AsNoTracking_SingleAsync' if async_api else 'AsNoTracking_Single','RollbackAsync' if async_api else 'Rollback','DisposeAsync','SingleAsync_CountAsync' if async_api else 'Single_Count']
    results=[physical(profile),schema,1,values['before'],dict(transaction_id=G,ef_object_id=29,underlying_object_id=30),True,row(),1,values['inside'],dict(returned=True,ef_object_id=29,transaction_id=G),True,values['after']]
    for i,name in enumerate(STEPS):
        steps.append(dict(step_id=name,ordinal=i,started_sequence=i*100+10,completed_sequence=i*100+90,status='completed',api=api_list[i],result=copy.deepcopy(results[i]),error=None))
    commands=[]
    def cmd(stage,start,ctx,conn,txid,kind='reader',parameters=None,result=None):
        i=len(commands)+1
        commands.append(dict(sequence=start,completed_sequence=start+10,stage=stage,source='DbCommandInterceptor',command_id=('%08x'%i)+'-1234-1234-1234-123456789abc',connection_id=CG,context_object_id=ctx,command_object_id=100+i,transaction_object_id=txid,connection_object_id=conn,is_async=async_api,api=('NonQueryExecuting' if kind=='nonquery' else 'ReaderExecuting')+('Async' if async_api else ''),kind=kind,sql_sha256=H,parameters=parameters or [],completed=True,result=result,error=None,capture_state='observed'))
    cmd('create',130,11,12,None,'nonquery',result=0)
    cmd('seed_SaveChanges',240,11,12,13,parameters=[dict(type='W.Dm.DmParameter',db_type='DateTime2',value=row()['d']),dict(type='W.Dm.DmParameter',db_type='DateTimeOffset',value=row()['z'])])
    cmd('fresh_before',330,21,22,None);cmd('fresh_before',350,21,22,None)
    cmd('tracking_query',630,31,32,30)
    cmd('transaction_SaveChanges',740,31,32,30,parameters=[dict(type='W.Dm.DmParameter',db_type='DateTime2',value=row(TICKS+quantum)['d']),dict(type='W.Dm.DmParameter',db_type='DateTimeOffset',value=row(TICKS+quantum)['z']),dict(type='W.Dm.DmParameter',db_type='AnsiString',value=dict(type='System.String')),dict(type='W.Dm.DmParameter',db_type='Int32',value=dict(type='System.Int32'))])
    cmd('query_inside_transaction',830,31,32,30)
    cmd('fresh_after',1130,41,42,None);cmd('fresh_after',1150,41,42,None)
    events=[]
    def event(callback,seq,stage,seed=False):
        events.append(dict(sequence=seq,stage=stage,source='DbTransactionInterceptor',callback=callback+('Async' if async_api else ''),context_object_id=11 if seed else 31,transaction_object_id=13 if seed else 30,connection_object_id=12 if seed else 32,current_connection_object_id=None if callback in ('TransactionRolledBack','TransactionCommitted') else (12 if seed else 32),connection_binding_source='callback_connection_argument' if callback=='TransactionStarted' else ('registered_transaction_binding' if callback in ('TransactionRolledBack','TransactionCommitted') else 'current_transaction_connection'),binding_sequence=220 if seed else 430,transaction_id=SEED_G if seed else G,connection_id=CG,is_async=async_api,completed=callback not in ('CreatingSavepoint','ReleasingSavepoint','TransactionRollingBack','TransactionCommitting'),error=None,capture_state='observed'))
    event('TransactionStarted',220,'seed_SaveChanges',True);event('TransactionCommitting',260,'seed_SaveChanges',True);event('TransactionCommitted',270,'seed_SaveChanges',True)
    event('TransactionStarted',430,'begin_transaction')
    if ordinal<2:
        event('CreatingSavepoint',720,'transaction_SaveChanges');event('CreatedSavepoint',725,'transaction_SaveChanges');event('ReleasingSavepoint',760,'transaction_SaveChanges');event('ReleasedSavepoint',765,'transaction_SaveChanges')
    event('TransactionRollingBack',930,'full_Rollback');event('TransactionRolledBack',940,'full_Rollback')
    clean=dict(cleanup_verified=True,ledger_sha256=H,objects=[dict(table=entry['table'],nonce=nonce,dropped=True,fresh_absent=True,fresh_table_count=0,identity=physical(profile))])
    report=dict(schema_version=1,task='TP07-T1-case',approval=reference(),ticket=reference(),cohort_id=H,case_id=IDS[ordinal],profile=profile,api='async' if async_api else 'sync',definition=dict(scale=scale,ticks=TICKS,offset_minutes=330,quantum=quantum),runtime_identity=runtime,services=services,observer=observer,steps=steps,transactions=[tx,tx2],commands=commands,events=events,values=values,ownership=entry,main_finally=clean,error=None,status='completed',process_exit_code=0)
    cleanup=dict(schema_version=1,task='TP07-T1-cleanup',approval=reference(),ticket=reference(),runtime_identity=copy.deepcopy(runtime),ledger_before=reference(),objects=[copy.deepcopy(entry)],cleanup=copy.deepcopy(clean),finally_cleanup=copy.deepcopy(clean),error=None,status='completed',process_exit_code=0)
    return dict(report=report,cleanup=cleanup,ledger=ledger,startup=startup,startup_sha=H,ledger_sha=H,case_id=IDS[ordinal])


def change(value,path,new):
    obj=value
    for key in path[:-1]:
        obj=obj[key]
    obj[path[-1]]=new


class CompleteSyntheticContracts(unittest.TestCase):
    def test_four_complete_handwritten_positive_cases(self):
        for ordinal in range(4):
            with self.subTest(case=IDS[ordinal]):
                checks=V.case_contract(**fixture(ordinal))
                self.assertTrue(all(checks.values()))

    def test_every_finite_top_field_missing_or_extra_rejected(self):
        for name in ('report','cleanup'):
            good=fixture(0)
            for key in good[name]:
                with self.subTest(document=name,key=key):
                    bad=copy.deepcopy(good);del bad[name][key]
                    with self.assertRaises(V.ContractError):V.case_contract(**bad)
            bad=copy.deepcopy(good);bad[name]['accepted']=True
            with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_critical_actual_fields_each_mutated(self):
        mutations=[
          (['report','definition','quantum'],10),(['report','definition','ticks'],True),(['report','profile'],'tls'),(['report','api'],'async'),
          (['report','runtime_identity','startup_assembly'],'TP07EfRuntimeProbe'),(['report','runtime_identity','entry_host_type'],'TP07LiteralProbe'),(['report','runtime_identity','source_manifest_sha256_binding'],'b'*64),(['report','runtime_identity','lock_manifest_sha256'],'b'*64),(['report','runtime_identity','package_sha256'],'b'*64),(['report','runtime_identity','ef_provider','mvid'],SEED_G),
          (['report','services','factory','type'],'Microsoft.EntityFrameworkCore.Storage.RelationalTransactionFactory'),(['report','services','before_ddl_factory','sha256'],'b'*64),(['report','services','di_origin'],'fixture_ReplaceService'),(['report','services','auto_savepoints_enabled'],False),(['report','services','before_ddl_resolved_sequence'],200),
          (['report','transactions',0,'current_same_instance'],False),(['report','transactions',0,'underlying_connection_same'],False),(['report','transactions',0,'underlying_supports_savepoints'],False),(['report','transactions',1,'ef_supports_savepoints'],False),(['report','transactions',1,'underlying_object_id'],44),(['report','transactions',0,'isolation_level'],'Unspecified'),(['report','transactions',0,'underlying','type'],'System.Data.Common.DbTransaction'),(['report','transactions',1,'read_stage'],'after_begin'),(['report','transactions',1,'read_sequence'],700),
          (['report','observer','registration','transaction_count'],0),(['report','observer','registration','same_transaction_instance'],False),(['report','observer','registration','context_object_id'],11),(['report','observer','registration','source'],'AddInterceptors_called'),(['report','observer','capture_state'],'captured_unknown'),(['report','observer','observation_completed_sequence'],None),(['report','observer','collector','name'],'TP07EfRuntimeProbe'),
          (['report','commands',5,'transaction_object_id'],None),(['report','commands',5,'connection_object_id'],42),(['report','commands',5,'completed'],False),(['report','commands',5,'is_async'],True),(['report','commands',5,'parameters',0,'value','ticks'],TICKS+10),(['report','commands',5,'parameters',1,'value','offset_minutes'],0),(['report','commands',5,'parameters',1,'value','utc_ticks'],TICKS),(['report','commands',6,'kind'],'scalar'),
          (['report','events',3,'transaction_object_id'],13),(['report','events',3,'is_async'],True),(['report','events',4,'stage'],'seed_SaveChanges'),(['report','events',5,'completed'],False),(['report','events',-1,'callback'],'RolledBackToSavepoint'),(['report','events',-1,'error'],dict(type='System.NotSupportedException',code=None,number=None)),
          (['report','steps',9,'result','returned'],False),(['report','steps',9,'api'],'DisposeAsync'),(['report','steps',9,'status'],'failed'),(['report','steps',7,'result'],0),(['report','steps',8,'api'],'Local'),
          (['report','values','inside','d','ticks'],TICKS),(['report','values','inside','z','utc_ticks'],TICKS),(['report','values','inside','n'],'first'),(['report','values','after','row_count'],2),(['report','values','after','different_connection'],False),(['report','values','after','connection_object_id'],32),(['report','values','after','identity','server_user'],'SA'),(['report','values','after','schema','comment'],'TP07_FAKE'),(['report','values','after','schema','columns',1,'scale'],6),(['report','values','after','d_oracle','raw','hex'],'00'*9),(['report','values','after','z_oracle','ff9'],'2026-07-23 14:15:16.123456000 +00:00'),(['report','values','before','d_oracle','metadata','raw_source'],'model_metadata'),(['report','values','after','d_oracle','typed','kind'],'Utc'),
          (['ledger','objects',0,'owner_marker'],'OWN_FAKE'),(['ledger','objects',0,'create_acknowledged'],False),(['ledger','objects',0,'creation_state'],'reserved'),(['ledger','objects',0,'create_evidence','call_returned'],False),(['report','main_finally','objects',0,'fresh_absent'],False),(['cleanup','finally_cleanup','objects',0,'fresh_table_count'],1),(['cleanup','cleanup','objects',0,'table'],'TPL_OTHER'),(['cleanup','status'],'cleanup_pending'),(['report','process_exit_code'],1),
        ]
        for path,new in mutations:
            with self.subTest(path=path):
                bad=fixture(0);change(bad,path,new)
                with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_missing_steps_commands_callbacks_and_two_reads_rejected(self):
        for key in ('steps','commands','events','transactions'):
            for i in range(len(fixture(0)['report'][key])):
                # Seed callback omission is deliberately tested separately: it
                # cannot substitute for the explicit target callback chain.
                if key=='events' and i<3:continue
                with self.subTest(collection=key,index=i):
                    bad=fixture(0);bad['report'][key].pop(i)
                    with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_tls_zero_requires_real_begin_business_rollback_collector(self):
        for path,new in [(['observer','capture_state'],'captured_unknown'),(['observer','registration','transaction_count'],0),(['events'],[]),(['commands'],[]),(['transactions'],[])]:
            with self.subTest(path=path):
                bad=fixture(2);change(bad['report'],path,new)
                with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_tls_any_savepoint_or_commit_is_rejected(self):
        for callback in ('CreatingSavepoint','CreatedSavepoint','ReleasingSavepoint','ReleasedSavepoint','RollingBackToSavepoint','RolledBackToSavepoint','TransactionCommitting','TransactionCommitted'):
            bad=fixture(2);ev=copy.deepcopy(bad['report']['events'][3]);ev.update(sequence=730,stage='transaction_SaveChanges',callback=callback,completed=callback not in ('CreatingSavepoint','ReleasingSavepoint','RollingBackToSavepoint','TransactionCommitting'));bad['report']['events'].insert(4,ev)
            with self.subTest(callback=callback):
                with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_seed_callbacks_cannot_be_relabelled_as_explicit_evidence(self):
        bad=fixture(0)
        for ev in bad['report']['events']:
            if ev['stage']=='transaction_SaveChanges':ev.update(transaction_id=SEED_G,transaction_object_id=13)
        with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_bool_and_float_cannot_impersonate_every_integer(self):
        good=fixture(0)
        def integer_paths(obj,path=()):
            if type(obj) is dict:
                for key,val in obj.items():yield from integer_paths(val,path+(key,))
            elif type(obj) is list:
                for i,val in enumerate(obj):yield from integer_paths(val,path+(i,))
            elif type(obj) is int:
                yield path,obj
        for path,number in integer_paths(good):
            if path[0] not in ('report','cleanup','ledger'):continue
            for bad_number in (True,float(number)):
                with self.subTest(path=path,type=type(bad_number).__name__):
                    bad=copy.deepcopy(good);change(bad,path,bad_number)
                    with self.assertRaises(V.ContractError):V.case_contract(**bad)


class StrictJsonAndRefs(unittest.TestCase):
    def test_duplicate_nonfinite_float_and_credential_fields_rejected(self):
        for raw in ('{"x":1,"x":2}','{"x":{"a":1,"a":2}}','{"x":NaN}','{"x":Infinity}','{"x":1.0}','{"message":"unsafe"}','{"x":"Pwd=unsafe"}'):
            with self.subTest(raw=raw):
                with self.assertRaises(V.ContractError):V.loads(raw)

    def test_ref_exact_hash_missing_extra_relative_cross_root_symlink(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();p=root/'one.json';p.write_text('{}',encoding='utf-8');good=dict(path=str(p),sha256=V.sha(p))
            self.assertEqual(V.ref(good,root),p)
            for bad in [dict(path='relative',sha256=good['sha256']),dict(path=str(p),sha256='b'*64),dict(path=str(p)),dict(good,extra=True)]:
                with self.assertRaises(V.ContractError):V.ref(bad,root)
            child=root/'nested';child.mkdir()
            with self.assertRaises(V.ContractError):V.ref(good,child)
            link=root/'link.json';link.symlink_to(p)
            with self.assertRaises(V.ContractError):V.ref(dict(path=str(link),sha256=good['sha256']),root)

    def test_unknown_calendar_clock_offsets_or_ff9_shape_rejected(self):
        for text in ('2026-02-30 14:15:16.123456000','2026-07-23 25:15:16.123456000','2026-07-23 14:15:16.123456001','2026-07-23 14:15:16.123456000 trailing'):
            with self.assertRaises(V.ContractError):V.ff9(text,False)
        for text in ('2026-07-23 14:15:16.123456000 -00:00','2026-07-23 14:15:16.123456000 +15:00','2026-07-23 14:15:16.123456000 +05:60'):
            with self.assertRaises(V.ContractError):V.ff9(text,True)


class ProcessAndMatrixContracts(unittest.TestCase):
    def test_three_independent_180_second_natural_exit_contracts(self):
        for role in ('main','cleanup','validator'):
            argv=['/synthetic/'+role];cwd='/synthetic'
            good=dict(argv=argv,cwd=cwd,exit_code=0,elapsed_ms=180000,timed_out=False,termination=None,execution_deadline=180,termination_reap_budget=2,termination_reap_ms=0,signal_number=None)
            V.process_contract(good,argv,cwd)
            for key,value in [('exit_code',1),('exit_code',True),('elapsed_ms',180001),('elapsed_ms',-1),('elapsed_ms',1.0),('timed_out',True),('termination','deadline_kill'),('argv',['/synthetic/old-host']),('cwd','/synthetic/old-ROOT'),('execution_deadline',181),('termination_reap_budget',3),('termination_reap_ms',1),('termination_reap_ms',False),('signal_number',2)]:
                with self.subTest(role=role,key=key):
                    bad=copy.deepcopy(good);bad[key]=value
                    with self.assertRaises(V.ContractError):V.process_contract(bad,argv,cwd)
            for key in good:
                bad=copy.deepcopy(good);del bad[key]
                with self.assertRaises(V.ContractError):V.process_contract(bad,argv,cwd)

    def test_matrix_four_complete_review_only_and_failed_prefix_not_run(self):
        ar=reference('/synthetic/approval.json')
        receipts=[dict(case_id=c,cohort_id=H,status='passed') for c in IDS]
        validations=[dict(case_id=c,status='passed',activation_accepted=False) for c in IDS]
        matrix=dict(schema_version=1,task='TP07-T1-matrix',approval=ar,cohort_id=H,required_cases=list(IDS),attempted_cases=list(IDS),case_receipts=[reference('/synthetic/'+c) for c in IDS],case_validations=[reference('/synthetic/'+c+'-validation') for c in IDS],first_failure=None,input_drift=False,cleanup_pending=False,status='completed_pending_review',candidate_acceptance=False,full_EF_C_accepted=False)
        self.assertTrue(V.matrix_contract(matrix,receipts,validations,ar)['ordered_four_completed'])
        for failure_index in range(4):
            prefix=failure_index+1;failed=copy.deepcopy(matrix)
            failed.update(attempted_cases=IDS[:prefix],case_receipts=matrix['case_receipts'][:prefix],case_validations=matrix['case_validations'][:prefix],first_failure=IDS[failure_index],status='failed')
            rs=copy.deepcopy(receipts[:prefix]);vs=copy.deepcopy(validations[:prefix]);rs[-1]['status']='failed';vs[-1]['status']='rejected'
            result=V.matrix_contract(failed,rs,vs,ar)
            self.assertFalse(result['ordered_four_completed']);self.assertEqual(result['later_cases_not_run'],IDS[prefix:])

    def test_matrix_resume_filter_cross_slot_accepted_and_post_failure_rejected(self):
        ar=reference('/synthetic/approval.json');rs=[dict(case_id=c,cohort_id=H,status='passed') for c in IDS];vs=[dict(case_id=c,status='passed',activation_accepted=False) for c in IDS]
        good=dict(schema_version=1,task='TP07-T1-matrix',approval=ar,cohort_id=H,required_cases=list(IDS),attempted_cases=list(IDS),case_receipts=[reference('/synthetic/'+c) for c in IDS],case_validations=[reference('/synthetic/'+c+'-validation') for c in IDS],first_failure=None,input_drift=False,cleanup_pending=False,status='completed_pending_review',candidate_acceptance=False,full_EF_C_accepted=False)
        for path,new in [(['attempted_cases'],[IDS[1],IDS[0],IDS[2],IDS[3]]),(['required_cases'],IDS[:3]),(['candidate_acceptance'],True),(['full_EF_C_accepted'],True),(['cohort_id'],'b'*64),(['first_failure'],IDS[1]),(['input_drift'],True),(['cleanup_pending'],True),(['status'],'accepted')]:
            with self.subTest(path=path):
                bad=copy.deepcopy(good);change(bad,path,new)
                with self.assertRaises(V.ContractError):V.matrix_contract(bad,rs,vs,ar)
        wrong=copy.deepcopy(rs);wrong[1]['case_id']=IDS[0]
        with self.assertRaises(V.ContractError):V.matrix_contract(good,wrong,vs,ar)
        for i in range(3):
            failed_rs=copy.deepcopy(rs);failed_vs=copy.deepcopy(vs);failed_rs[i]['status']='failed';failed_vs[i]['status']='rejected'
            with self.assertRaises(V.ContractError):V.matrix_contract(good,failed_rs,failed_vs,ar)

    def test_validation_acceptance_and_positive_control_ref_tampering_rejected(self):
        ar=reference('/synthetic/approval.json');er=reference('/synthetic/execution-input.json')
        checks={k:True for k in V.CHECK_KEYS};controls=[reference('/synthetic/offline.json')]
        good=V.validation_document(ar,er,IDS[0],checks,controls)
        V.validation_contract(good,ar,er,IDS[0])
        for path,new in [(['activation_accepted'],True),(['captured_complete'],False),(['checks','formal_DI'],False),(['checks','shared_positive_controls'],False),(['case_receipt','path'],'/synthetic/final-receipt.json'),(['case_id'],IDS[2]),(['status'],'accepted')]:
            bad=copy.deepcopy(good);change(bad,path,new)
            with self.assertRaises(V.ContractError):V.validation_contract(bad,ar,er,IDS[0])
        # Positive controls are independently regenerated from the execution
        # graph; equality must include their exact offline/shared Ref closure.
        for bad_controls in ([],[reference('/synthetic/old-cohort.json')],[reference('/synthetic/offline.json'),reference('/synthetic/seed-evidence.json')]):
            bad=copy.deepcopy(good);bad['positive_control_refs']=bad_controls
            with self.assertRaises(V.ContractError):V.exact(bad,good,'recomputed_positive_controls')

    def test_closure_requires_root_exact_paths_and_finite_counts(self):
        product=['product/%03d.cs'%i for i in range(178)]
        host=sorted('test/TP07TransactionCapabilityProbe/'+p.removeprefix('host/') for p in V.TOOL_FILES if p.startswith('host/'))
        good=dict(product_paths=product,product_count=178,host_paths=host,source_paths=sorted(product+host),source_count=187,locked_paths=sorted(product+host+['test/TP07TransactionCapabilityProbe/packages.lock.json']),locked_count=188,feed='/synthetic/feed',feed_inventory={'one.nupkg':H},nuget_config=reference(),native_graphs={p:dict(lock_dependencies={},assets_frameworks={}) for p in V.PROJECTS})
        V.closure_contract(good)
        for path,new in [(['product_count'],175),(['product_count'],True),(['source_count'],999),(['locked_count'],187),(['product_paths'],product[:-1]),(['host_paths'],host+['test/old-host.cs']),(['source_paths'],good['source_paths'][:-1]),(['locked_paths'],good['source_paths']),(['product_paths',0],'../outside.cs')]:
            with self.subTest(path=path):
                bad=copy.deepcopy(good);change(bad,path,new)
                with self.assertRaises(V.ContractError):V.closure_contract(bad)

    def test_unknown_fields_in_every_actual_finite_subobject_rejected(self):
        paths=[['report','services'],['report','services','factory'],['report','observer'],['report','observer','registration'],['report','transactions',0],['report','steps',0],['report','commands',0],['report','events',3],['report','values'],['report','values','inside'],['report','values','after'],['report','values','after','identity'],['report','values','after','schema'],['report','values','after','d_oracle'],['report','values','after','d_oracle','raw'],['report','values','after','d_oracle','metadata'],['ledger'],['ledger','objects',0],['report','main_finally'],['cleanup','cleanup','objects',0]]
        for path in paths:
            with self.subTest(path=path):
                bad=fixture(0);obj=bad
                for key in path:obj=obj[key]
                obj['unexpected']=True
                with self.assertRaises(V.ContractError):V.case_contract(**bad)


def handwritten_probe_source(root):
    snapshot=root/'.local/temporal-native/verification/synthetic-probe-source/snapshot'
    snapshot.mkdir(parents=True)
    names=[name for name in V.TOOL_FILES if name.startswith('host/')]
    hashes={}
    for ordinal,name in enumerate(names):
        p=snapshot/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text('handwritten synthetic host source '+str(ordinal),encoding='utf-8');hashes[name]=V.sha(p)
    aggregate=''.join(hashes[name]+'  '+name+'\n' for name in sorted(hashes))
    doc=dict(schema_version=1,task='TP07-T1-probe-source-manifest',source_root=str(snapshot),source_hashes=hashes,source_count=9,source_aggregate_sha256=hashlib.sha256(aggregate.encode('utf-8')).hexdigest(),aggregate_format='sorted relative source paths: sha256 two spaces path newline')
    path=snapshot.parent/'manifest.json';path.write_text(json.dumps(doc),encoding='utf-8')
    pack=dict(schema_version=1,task='TP07-T1-toolpack',source_hashes={V.TOOL_PREFIX+name:digest for name,digest in hashes.items()})
    closure=dict(host_paths=sorted('test/TP07TransactionCapabilityProbe/'+name.removeprefix('host/') for name in names))
    return path,doc,pack,closure


class ProbeSourceManifestContracts(unittest.TestCase):
    def test_handwritten_nine_source_manifest_positive(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();path,doc,pack,closure=handwritten_probe_source(root)
            result=V.probe_source_contract(dict(path=str(path),sha256=V.sha(path)),root,pack,closure)
            self.assertEqual(result,doc)
            with self.assertRaises(V.ContractError):V.probe_source_contract(dict(path=str(path),sha256=V.sha(path)),root,pack,closure,'b'*64)

    def test_required_ref_has_no_legacy_inputs_compatibility(self):
        inputs={key:None for key in V.TOP['inputs'].split()};inputs.update(schema_version=1,task='TP07-T1-inputs')
        V.document(inputs,'inputs')
        del inputs['probe_source_manifest']
        with self.assertRaises(V.ContractError):V.document(inputs,'inputs')
        for bad in (None,{},dict(path='relative',sha256=H),dict(path='/absent',sha256=H)):
            with self.assertRaises(V.ContractError):V.ref(bad)

    def test_manifest_every_field_missing_extra_wrong_count_sha_aggregate_root(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();path,doc,pack,closure=handwritten_probe_source(root)
            variants=[]
            for key in doc:
                bad=copy.deepcopy(doc);del bad[key];variants.append(('missing_'+key,bad))
            variants.append(('extra',dict(doc,accepted=True)))
            for key,val in [('schema_version',True),('task','TP07LiteralProbe-source-manifest'),('source_count',8),('source_count',True),('source_aggregate_sha256','b'*64),('aggregate_format','arbitrary'),('source_root',str(root))]:
                bad=copy.deepcopy(doc);bad[key]=val;variants.append((key,bad))
            for name,bad in variants:
                path.write_text(json.dumps(bad),encoding='utf-8')
                with self.subTest(mutation=name):
                    with self.assertRaises(V.ContractError):V.probe_source_contract(dict(path=str(path),sha256=V.sha(path)),root,pack,closure)
            path.write_text(json.dumps(doc),encoding='utf-8')
            with self.assertRaises(V.ContractError):V.probe_source_contract(dict(path=str(path),sha256='b'*64),root,pack,closure)

    def test_every_source_drift_toolpack_mismatch_and_wrong_nine_paths(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();path,doc,pack,closure=handwritten_probe_source(root);reference=dict(path=str(path),sha256=V.sha(path))
            for name in doc['source_hashes']:
                with self.subTest(source=name):
                    source=Path(doc['source_root'])/name;original=source.read_bytes();source.write_bytes(original+b' drift')
                    with self.assertRaises(V.ContractError):V.probe_source_contract(reference,root,pack,closure)
                    source.write_bytes(original)
                    bad=copy.deepcopy(pack);bad['source_hashes'][V.TOOL_PREFIX+name]='b'*64
                    with self.assertRaises(V.ContractError):V.probe_source_contract(reference,root,bad,closure)
            bad=copy.deepcopy(closure);bad['host_paths'][0]='test/old-host/Program.cs'
            with self.assertRaises(V.ContractError):V.probe_source_contract(reference,root,pack,bad)
            for mutation in ('missing','extra','root_cross'):
                bad=copy.deepcopy(doc)
                if mutation=='missing':bad['source_hashes'].pop(next(iter(bad['source_hashes'])))
                elif mutation=='extra':bad['source_hashes']['host/FifthCase.cs']=H
                else:bad['source_hashes']['../outside.cs']=bad['source_hashes'].pop(next(iter(bad['source_hashes'])))
                path.write_text(json.dumps(bad),encoding='utf-8')
                with self.assertRaises(V.ContractError):V.probe_source_contract(dict(path=str(path),sha256=V.sha(path)),root,pack,closure)

    def test_snapshot_extra_file_symlink_and_cross_workspace_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();path,doc,pack,closure=handwritten_probe_source(root);reference=dict(path=str(path),sha256=V.sha(path));snapshot=Path(doc['source_root'])
            extra=snapshot/'host'/'extra.cs';extra.write_text('unapproved')
            with self.assertRaises(V.ContractError):V.probe_source_contract(reference,root,pack,closure)
            extra.unlink();name=next(iter(doc['source_hashes']));source=snapshot/name;original=source.read_bytes();source.unlink();source.symlink_to(path)
            with self.assertRaises(V.ContractError):V.probe_source_contract(reference,root,pack,closure)
            source.unlink();source.write_bytes(original)
            with self.assertRaises(V.ContractError):V.probe_source_contract(reference,root/'another-workspace',pack,closure)
            link=snapshot.parent/'linked-snapshot';link.symlink_to(snapshot,target_is_directory=True);bad=dict(doc,source_root=str(link));path.write_text(json.dumps(bad),encoding='utf-8')
            with self.assertRaises(V.ContractError):V.probe_source_contract(dict(path=str(path),sha256=V.sha(path)),root,pack,closure)


class ActualGraphAndOfflineBoundaryContracts(unittest.TestCase):
    def test_native_Core13_and_runtime_Core12_excludes_build_only_Analyzer(self):
        runtime_names=['Microsoft.EntityFrameworkCore','Microsoft.EntityFrameworkCore.Abstractions','Microsoft.EntityFrameworkCore.Relational','Microsoft.Extensions.Caching.Abstractions','Microsoft.Extensions.Caching.Memory','Microsoft.Extensions.Configuration.Abstractions','Microsoft.Extensions.DependencyInjection','Microsoft.Extensions.DependencyInjection.Abstractions','Microsoft.Extensions.Logging','Microsoft.Extensions.Logging.Abstractions','Microsoft.Extensions.Options','Microsoft.Extensions.Primitives','W.DmProvider']
        libraries={name+'/10.0.12':dict(type='package') for name in runtime_names}
        libraries['W.EntityFrameworkCore.Dameng/0.1.0']=dict(type='project')
        V.runtime_package_contract(libraries)
        self.assertEqual(set(V.CORE_PACKAGES),set(runtime_names)-{'W.DmProvider'}|{'Microsoft.EntityFrameworkCore.Analyzers'})
        for missing in runtime_names:
            bad=copy.deepcopy(libraries);del bad[missing+'/10.0.12']
            with self.assertRaises(V.ContractError):V.runtime_package_contract(bad)
        for extra in ('Microsoft.EntityFrameworkCore.Analyzers','System.Diagnostics.DiagnosticSource','Microsoft.EntityFrameworkCore.Design','DM.DmProvider'):
            bad=copy.deepcopy(libraries);bad[extra+'/10.0.12']=dict(type='package')
            with self.assertRaises(V.ContractError):V.runtime_package_contract(bad)

    def test_native_zip_and_NuGet_contentHash_domains_are_separate(self):
        zip_hash='A'*86+'==';content_hash='B'*86+'=='
        V.native_hash_domains_contract(zip_hash,zip_hash,content_hash,content_hash,content_hash)
        for args in [(zip_hash,content_hash,content_hash,content_hash,content_hash),(zip_hash,zip_hash,zip_hash,content_hash,content_hash),(zip_hash,zip_hash,content_hash,zip_hash,content_hash),(zip_hash,zip_hash,content_hash,content_hash,zip_hash)]:
            with self.assertRaises(V.ContractError):V.native_hash_domains_contract(*args)

    def test_offline_failed_batch_rollback_to_then_finally_release_both_APIs(self):
        for async_api in (False,True):
            suffix='Async' if async_api else ''
            for fail in (False,True):
                names=['CreatingSavepoint','CreatedSavepoint']+(['RollingBackToSavepoint','RolledBackToSavepoint'] if fail else [])+['ReleasingSavepoint','ReleasedSavepoint']
                seqs=[10,20]+([50,60] if fail else [])+[70,80]
                events=[dict(callback=name+suffix,sequence=seq) for name,seq in zip(names,seqs)]
                commands=[dict(stage='transaction_SaveChanges',sequence=30,completed_sequence=40)]
                counters=dict(savepoint_create=1,savepoint_release=1,savepoint_rollback=int(fail))
                V.offline_savepoint_contract(True,fail,counters,events,commands,async_api)
                missing=[e for e in events if not e['callback'].startswith(('ReleasingSavepoint','ReleasedSavepoint'))]
                with self.assertRaises(V.ContractError):V.offline_savepoint_contract(True,fail,counters,missing,commands,async_api)
                wrong=copy.deepcopy(counters);wrong['savepoint_release']=0
                with self.assertRaises(V.ContractError):V.offline_savepoint_contract(True,fail,wrong,events,commands,async_api)
                if fail:
                    wrong=copy.deepcopy(events);wrong[-2]['sequence']=45;wrong[-1]['sequence']=46
                    with self.assertRaises(V.ContractError):V.offline_savepoint_contract(True,fail,counters,wrong,commands,async_api)
                V.offline_savepoint_contract(False,fail,dict(savepoint_create=0,savepoint_release=0,savepoint_rollback=0),[],commands,async_api)

    def test_native_archive_real_nuspec_cache_feed_and_installed_bytes(self):
        import zipfile
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve();feed=root/'feed';feed.mkdir();cache=root/'cache';cache.mkdir()
            archive=cache/'sample.1.0.0.nupkg';nuspec=b'<package><metadata><id>Sample</id><version>1.0.0</version></metadata></package>'
            with zipfile.ZipFile(archive,'w') as package:package.writestr('Sample.nuspec',nuspec)
            (cache/'sample.nuspec').write_bytes(nuspec);(feed/archive.name).write_bytes(archive.read_bytes())
            closure=dict(feed=str(feed),feed_inventory={archive.name:V.sha(archive)})
            V.archive_origin_contract(archive,cache,closure,'Sample','1.0.0')
            for name,version in [('Other','1.0.0'),('Sample','2.0.0')]:
                with self.assertRaises(V.ContractError):V.archive_origin_contract(archive,cache,closure,name,version)
            (cache/'sample.nuspec').write_bytes(nuspec+b' drift')
            with self.assertRaises(V.ContractError):V.archive_origin_contract(archive,cache,closure,'Sample','1.0.0')
            (cache/'sample.nuspec').write_bytes(nuspec);(feed/archive.name).write_bytes(b'wrong-origin')
            with self.assertRaises(V.ContractError):V.archive_origin_contract(archive,cache,closure,'Sample','1.0.0')


class TerminalConnectionAndProcessContracts(unittest.TestCase):
    def test_four_live_contracts_observe_real_terminal_null_with_begin_binding(self):
        for ordinal in range(4):
            good=fixture(ordinal)
            terminal=good['report']['events'][-1]
            self.assertIsNone(terminal['current_connection_object_id'])
            self.assertEqual(terminal['binding_sequence'],430)
            V.case_contract(**good)

    def test_legitimate_observed_terminal_nonnull_uses_current_source(self):
        good=fixture(0);terminal=good['report']['events'][-1]
        terminal.update(current_connection_object_id=32,connection_binding_source='current_transaction_connection')
        V.case_contract(**good)

    def test_any_active_callback_premature_null_rejected(self):
        for ordinal in range(4):
            for i,event in enumerate(fixture(ordinal)['report']['events']):
                if event['callback'].removesuffix('Async') in ('TransactionCommitted','TransactionRolledBack'):continue
                bad=fixture(ordinal);bad['report']['events'][i]['current_connection_object_id']=None
                bad['report']['events'][i]['connection_binding_source']='registered_transaction_binding'
                with self.subTest(case=ordinal,callback=event['callback']):
                    with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_terminal_null_bad_object_GUID_context_connection_binding_or_phase(self):
        mutations=[('transaction_object_id',13),('transaction_id',SEED_G),('context_object_id',11),('connection_object_id',12),('connection_id',SEED_G),('binding_sequence',940),('binding_sequence',220),('binding_sequence',True),('connection_binding_source','current_transaction_connection'),('connection_binding_source','callback_connection_argument'),('capture_state','captured_unknown'),('completed',False),('error',dict(type='System.NotSupportedException',code=None,number=None)),('current_connection_object_id',42),('is_async',True)]
        for key,value in mutations:
            with self.subTest(field=key):
                bad=fixture(0);bad['report']['events'][-1][key]=value
                with self.assertRaises(V.ContractError):V.case_contract(**bad)
        bad=fixture(0);bad['report']['events'].pop(3)
        with self.assertRaises(V.ContractError):V.case_contract(**bad)

    def test_Started_or_Used_actual_null_terminal_bindings_both_APIs(self):
        for async_api in (False,True):
            suffix='Async' if async_api else ''
            for used in (False,True):
                for commit in (False,True):
                    original=fixture(1 if async_api else 0)['report']['events']
                    begin=copy.deepcopy(original[3]);pre=copy.deepcopy(original[-2]);post=copy.deepcopy(original[-1])
                    begin['callback']=('TransactionUsed' if used else 'TransactionStarted')+suffix
                    pre['callback']=('TransactionCommitting' if commit else 'TransactionRollingBack')+suffix
                    post['callback']=('TransactionCommitted' if commit else 'TransactionRolledBack')+suffix
                    events=[begin,pre,post]
                    V.events_binding_contract(events,allow_used=used)
                    if used:
                        with self.assertRaises(V.ContractError):V.events_binding_contract(events,allow_used=False)
                    for key in ('binding_sequence','connection_binding_source','current_connection_object_id'):
                        bad=copy.deepcopy(events);del bad[-1][key]
                        # The complete report contract rejects missing finite
                        # keys before applying association semantics.
                        live=fixture(0);del live['report']['events'][-1][key]
                        with self.assertRaises(V.ContractError):V.case_contract(**live)

    def test_every_termination_enum_and_reap_unknown_never_accepted(self):
        argv=['/synthetic/host'];cwd='/synthetic';natural=dict(argv=argv,cwd=cwd,exit_code=0,elapsed_ms=100,timed_out=False,termination=None,execution_deadline=180,termination_reap_budget=2,termination_reap_ms=0,signal_number=None)
        V.process_contract(natural,argv,cwd)
        for termination in ('deadline_kill','signal_kill','interrupt_kill','exception_kill','launch_failed'):
            for unknown in (False,True):
                bad=dict(natural,exit_code=None if unknown else -9,termination=termination+('_reap_unknown' if unknown else ''),timed_out=termination=='deadline_kill',termination_reap_ms=1,signal_number=2 if termination=='signal_kill' else None)
                with self.subTest(termination=bad['termination']):
                    with self.assertRaises(V.ContractError):V.process_contract(bad,argv,cwd)
        for field in ('execution_deadline','termination_reap_budget','termination_reap_ms','signal_number'):
            bad=copy.deepcopy(natural);del bad[field]
            with self.assertRaises(V.ContractError):V.process_contract(bad,argv,cwd)

    def test_eight_offline_commit_handwritten_null_positives_and_counter_drift(self):
        for async_api in (False,True):
            suffix='Async' if async_api else ''
            for supports in (False,True):
                for external in (False,True):
                    source=fixture(1 if async_api else 0)
                    events=[copy.deepcopy(source['report']['events'][3]),copy.deepcopy(source['report']['events'][-2]),copy.deepcopy(source['report']['events'][-1])]
                    events[0].update(callback=('TransactionUsed' if external else 'TransactionStarted')+suffix,stage='startup')
                    events[1].update(callback='TransactionCommitting'+suffix,stage='full_Commit')
                    events[2].update(callback='TransactionCommitted'+suffix,stage='full_Commit')
                    counters=dict(sync_opens=1,async_opens=0,sync_business=0,async_business=0,savepoint_create=0,savepoint_release=0,savepoint_rollback=0,transaction_disposals=0 if external else 1,full_rollbacks=0,full_commits=1,real_network_attempts=0)
                    row=dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=False,external=external,terminal='commit',services=dict(factory=source['report']['services']['factory']),observer=source['report']['observer'],events=events,commands=[],counters=counters,status='completed')
                    V.offline_commit_contract(row,source['startup'],async_api,supports,external)
                    for key in counters:
                        bad=copy.deepcopy(row);bad['counters'][key]=True
                        with self.assertRaises(V.ContractError):V.offline_commit_contract(bad,source['startup'],async_api,supports,external)
                    for key,value in [('full_commits',0),('full_rollbacks',1),('savepoint_create',1),('real_network_attempts',1)]:
                        bad=copy.deepcopy(row);bad['counters'][key]=value
                        with self.assertRaises(V.ContractError):V.offline_commit_contract(bad,source['startup'],async_api,supports,external)
                    bad=copy.deepcopy(row);bad['events'][-1]['current_connection_object_id']=32;bad['events'][-1]['connection_binding_source']='current_transaction_connection'
                    with self.assertRaises(V.ContractError):V.offline_commit_contract(bad,source['startup'],async_api,supports,external)


class FixedOfflineRegistryContracts(unittest.TestCase):
    def test_fixed28_observed_registry_counts_slots_values_and_no_missing_rows(self):
        rows=[]
        for async_api in (False,True):
            for supports in (False,True):
                for fail in (False,True):
                    for external in (False,True):rows.append(dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=fail,external=external,terminal='rollback'))
        for owned in (False,True):
            for supports in (False,True):rows.append(dict(api='factory_Create',supports_savepoints=supports,transaction_owned=owned))
        for async_api in (False,True):
            for supports in (False,True):
                for external in (False,True):rows.append(dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=False,external=external,terminal='commit'))
        good=dict(required_case_count=28,case_count=28,cases=rows)
        V.offline_registry_contract(good)
        for key,val in [('required_case_count',20),('case_count',20),('case_count',True),('required_case_count',28.0)]:
            bad=copy.deepcopy(good);bad[key]=val
            with self.assertRaises(V.ContractError):V.offline_registry_contract(bad)
        for ordinal in range(28):
            bad=copy.deepcopy(good);bad['cases'].pop(ordinal)
            with self.assertRaises(V.ContractError):V.offline_registry_contract(bad)
            for key in rows[ordinal]:
                bad=copy.deepcopy(good);del bad['cases'][ordinal][key]
                with self.assertRaises(V.ContractError):V.offline_registry_contract(bad)
        bad=copy.deepcopy(good);bad['cases'][0],bad['cases'][-1]=bad['cases'][-1],bad['cases'][0]
        with self.assertRaises(V.ContractError):V.offline_registry_contract(bad)
        bad=copy.deepcopy(good);bad['cases'][20]['terminal']='rollback'
        with self.assertRaises(V.ContractError):V.offline_registry_contract(bad)
        bad=copy.deepcopy(good);bad['cases'].append(rows[0]);bad['case_count']=29
        with self.assertRaises(V.ContractError):V.offline_registry_contract(bad)


# V2-R01 regression: the immutable producer policy is the actual fixture,
# never a handwritten subset of the policy being accepted.
V2_POLICY_SHA = '1a11cbba01a1b9626151fd99676aadc88f66a8151bfd04fd4dab3713f7d33b35'
V2_TOOLPACK_SHA = '2028d26c32ec8de6521aee7fa53ad31f67b3454a501290cdfc412290cb6992a2'
# Root-approved v4 nullable-only source delta. The other thirteen producer
# files retain the immutable v2 map above. This pin excludes both validator/test
# files, so changing this test cannot create a toolpack self-hash cycle.
V4_PRODUCER_OVERRIDES = {
    'host/OfflineContracts.cs': '359cb5df58362489dba0126d6f0908d7ed1da5ccd553ecb01419d019feeb7a1e',
    'host/TransactionEvidence.cs': 'becda80d3e091f18c7288e6277f328b9af49b0aa1f00341a6092fb57211cd82e',
}
V4_PRODUCER_AGGREGATE = 'b023a647a0f63533c463b79b2a52b1b04ad3bdb3391f6e0f7428a93832223b0d'

# V5 adds exactly the current prepare/README bytes to the historical v4 host
# overrides. These fifteen producer pins exclude validator and this test.
V5_PRODUCER_OVERRIDES = dict(V4_PRODUCER_OVERRIDES, **{
    'prepare.py': 'e131b64d83e1e0b78731ff83b5fdd022ae71645722e58473ae444be93570515c',
    'README.md': 'b2d9cb2c419d4fb412dab02667c48d7fa36d1b6ea96242c92a9ff85100f44adf',
})
V5_PRODUCER_AGGREGATE = '1ec53727763ee6b2f496356b3241372fbc3b40f001a4b72f3d9235103075162a'

# V6 preserves v4/v5 provenance and pins the four changed producer files.
V6_PRODUCER_OVERRIDES = dict(V5_PRODUCER_OVERRIDES, **{
    'prepare.py': 'dd81f63fdee26fa4e50d3515e906bb1b321c0d5cc73712c1c94a989ab908167c',
    'README.md': '74d6964e92a09cd3ab0ffbe013318840b089a79e62388629abe249029a3cb9d0',
    'host/ProbeContext.cs': '5eb5621b2ea6c1ba6d11266a8226d1060f111ec7adb6813afe61d22c61a4bdcc',
    'host/Program.cs': '0ab8ad1210423b3f5bd3db753fbfdea61d915caeb2bb5bf4758d92f21dd49303',
})
V6_PRODUCER_AGGREGATE = '134967c9191728e971fdee8453c9a5af8466a868dbcd6fb17033796f50d08f12'

# V7 retains V4/V5/V6 provenance; only Program and README have new producer bytes.
V7_PRODUCER_OVERRIDES = dict(V6_PRODUCER_OVERRIDES, **{
    'host/Program.cs': 'c132173ae0c8fd25c469311beb3f8441e6ad5ede978e6aadd70d376afd9b7b24',
    'README.md': '8ea71ee040659b2b0b6279d51633dc7be37de5311ea1bec88f680067b406e40f',
})
V7_PRODUCER_AGGREGATE = 'd4c64e9034c804b92a896ab0c352c0379979ee3f1674e8f7864755a1ba0b4c3d'

# V9 preserves V4–V7 provenance and pins explicit identity-command transaction binding.
V9_PRODUCER_OVERRIDES = dict(V7_PRODUCER_OVERRIDES, **{
    'host/ProbeIdentity.cs': '8e653b99e827535f7b643383c646d62d19dba6fbfbb1b6151dbbd698a82691ba',
    'host/Program.cs': '45d7db114b6adc095b033df3b701881f4e2d9c94644741d4d1c670c7c759fdb8',
})
V9_PRODUCER_AGGREGATE = 'ef2940d232e4f7b6eabd8aac6497f68bdf09ef2adbb956f1bb7264752d081c26'

POLICY_ADDITIONS = ('required_input_keys','probe_source_manifest','native_hash_domains','termination_reap_budget_seconds','process_receipt_keys','termination_reasons','termination_unknown_suffix','transaction_event_binding_fields','connection_binding_sources','binding_callbacks','offline_registry')


def read_only_frozen_v2_pack():
    # A test running from the workspace or its real Low snapshot can locate
    # the reviewed artifact through its real ancestors. No module path is
    # rewritten, and no old ROOT or old validator code is executed.
    for ancestor in HERE.parents:
        path=ancestor/'.local/temporal-native/verification/TP07-T1-probe-tools-v2/toolpack-manifest.json'
        if path.is_file():
            if V.sha(path)!=V2_TOOLPACK_SHA:
                raise AssertionError('immutable_v2_toolpack_fixture_drift')
            return V.load(path)
    raise AssertionError('reviewed_v2_toolpack_fixture_required')


def actual_current_policy():
    path=HERE/'probe-policy.json'
    if V.sha(path)!=V2_POLICY_SHA:
        raise AssertionError('immutable_current_producer_policy_drift')
    return V.load(path)


def closed_current_toolpack_fixture(root):
    """Copy actual 17 source bytes; other JSON is labeled synthetic input only.

    The copied v9 validator is imported from its real file path. Its actual
    HERE must equal the synthetic workspace's tool directory, exactly as in
    production. Each Ref points to real bytes with a recomputed SHA; SDK and
    provider placeholder files are test data and are never executed or offered
    as provider, offline, native, runtime, or activation evidence.
    """
    frozen=read_only_frozen_v2_pack();policy=actual_current_policy()
    producer_names=[name for name in V.TOOL_FILES if name not in ('validate.py','test_contracts.py')]
    expected_producer={name:V9_PRODUCER_OVERRIDES.get(name,frozen['source_hashes'][V.TOOL_PREFIX+name]) for name in producer_names}
    producer_aggregate=''.join(expected_producer[name]+'  '+name+'\n' for name in sorted(expected_producer))
    if len(expected_producer)!=15 or hashlib.sha256(producer_aggregate.encode()).hexdigest()!=V9_PRODUCER_AGGREGATE:
        raise AssertionError('v9_producer_pin_closure_drift')
    target=root/V.TOOL_PREFIX
    for name in V.TOOL_FILES:
        source=HERE/name
        if name in expected_producer:
            if V.sha(source)!=expected_producer[name]:
                raise AssertionError('actual_frozen_producer_source_drift')
        destination=target/name;destination.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source,destination)
    for destination,origin in V.HELPERS.items():
        original=root/origin['path'];original.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(target/destination,original)
        if V.sha(original)!=origin['sha256']:
            raise AssertionError('copied_reviewed_helper_origin_drift')
    artifact_root=root/'.local/temporal-native/verification/synthetic-policy-chain'
    artifact_root.mkdir(parents=True)
    def write_ref(name,value):
        path=artifact_root/name;path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(json.dumps(value,indent=2,sort_keys=True),encoding='utf-8')
        return dict(path=str(path),sha256=V.sha(path))
    pack=dict(schema_version=1,task='TP07-T1-toolpack',source_hashes={V.TOOL_PREFIX+name:V.sha(target/name) for name in V.TOOL_FILES})
    pack_ref=write_ref('toolpack.json',pack)
    host_root=artifact_root/'probe-source'
    host_hashes={}
    for name in policy['probe_source_manifest']['source_paths']:
        destination=host_root/name;destination.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(target/name,destination);host_hashes[name]=V.sha(destination)
    aggregate=''.join(host_hashes[name]+'  '+name+'\n' for name in sorted(host_hashes))
    host_doc=dict(schema_version=1,task='TP07-T1-probe-source-manifest',source_root=str(host_root),source_hashes=host_hashes,source_count=9,source_aggregate_sha256=hashlib.sha256(aggregate.encode()).hexdigest(),aggregate_format='sorted relative source paths: sha256 two spaces path newline')
    host_ref=write_ref('host-source.json',host_doc)
    # Source rows below are explicitly synthetic, although all files/Refs are
    # real. This tests only the inputs/toolpack contract, without implying any
    # accepted product source, executable version, restore, or live result.
    product_root=artifact_root/'synthetic-product';product_hashes={}
    for ordinal in range(178):
        name='synthetic-product/%03d.cs'%ordinal;path=product_root/name
        path.parent.mkdir(parents=True,exist_ok=True);path.write_text('synthetic input source only '+str(ordinal),encoding='utf-8')
        product_hashes[name]=V.sha(path)
    product_ref=write_ref('synthetic-product-manifest.json',dict(schema_version=1,task='synthetic_input_fixture_not_product_acceptance',source_hashes=product_hashes))
    root_approval_ref=write_ref('synthetic-source-approval.json',dict(schema_version=1,task='synthetic_input_fixture_not_root_approval'))
    sdk_path=artifact_root/'synthetic-sdk-identity';sdk_path.write_bytes(b'synthetic SDK identity bytes; never executable')
    python_path=artifact_root/'synthetic-python-identity';python_path.write_bytes(b'synthetic interpreter identity bytes; never executable')
    feed=artifact_root/'synthetic-feed';feed.mkdir();archive=feed/'synthetic.nupkg';archive.write_bytes(b'synthetic feed identity only; no restore')
    config_ref=write_ref('synthetic-config.json',dict(task='synthetic_input_config_not_restore_config'))
    product_paths=sorted(product_hashes)
    host_paths=sorted('test/TP07TransactionCapabilityProbe/'+name.removeprefix('host/') for name in host_hashes)
    closure=dict(product_paths=product_paths,product_count=178,host_paths=host_paths,source_paths=sorted(product_paths+host_paths),source_count=187,locked_paths=sorted(product_paths+host_paths+['test/TP07TransactionCapabilityProbe/packages.lock.json']),locked_count=188,feed=str(feed),feed_inventory={archive.name:V.sha(archive)},nuget_config=config_ref,native_graphs={name:dict(lock_dependencies={},assets_frameworks={}) for name in V.PROJECTS})
    inputs=dict(schema_version=1,task='TP07-T1-inputs',workspace_root=str(root),original_repo=str(root),sdk=dict(path=str(sdk_path),sha256=V.sha(sdk_path),version='10.0.401'),python=dict(path=str(python_path),sha256=V.sha(python_path),version='synthetic_not_executed'),W_identity=write_ref('synthetic-W-identity.json',dict(task='synthetic_input_not_W_identity_acceptance')),capabilities=write_ref('synthetic-capabilities.json',dict(task='synthetic_input_not_capability_acceptance')),ef_overlay=dict(source_root=str(product_root),manifest=product_ref,lineage=dict(probe_source_manifest_sha256=host_ref['sha256']),root_approval=root_approval_ref),toolpack=pack_ref,helper_origins=copy.deepcopy(V.HELPERS),expected_closure=closure,probe_source_manifest=host_ref)
    inputs_ref=write_ref('inputs.json',inputs)
    spec=importlib.util.spec_from_file_location('tp07_v9_real_closed_toolpack',target/'validate.py')
    validator=importlib.util.module_from_spec(spec);spec.loader.exec_module(validator)
    if validator.HERE!=target or Path(validator.__file__).resolve()!=target/'validate.py':
        raise AssertionError('actual_module_location_not_synthetic_workspace')
    return validator,inputs_ref,inputs,pack,pack_ref,policy


def wrong_policy_value(value):
    if type(value) is dict:
        bad=copy.deepcopy(value);key=next(iter(bad));bad[key]=wrong_policy_value(bad[key]);return bad
    if type(value) is list:
        bad=copy.deepcopy(value);bad[0]=wrong_policy_value(bad[0]);return bad
    if type(value) is bool:return not value
    if type(value) is int:return value+1
    return value+'_wrong'


class ActualFrozenPolicyAndToolpackContracts(unittest.TestCase):
    def test_actual_frozen_current_policy_complete_positive(self):
        policy=actual_current_policy()
        self.assertEqual(set(POLICY_ADDITIONS),{'required_input_keys','probe_source_manifest','native_hash_domains','termination_reap_budget_seconds','process_receipt_keys','termination_reasons','termination_unknown_suffix','transaction_event_binding_fields','connection_binding_sources','binding_callbacks','offline_registry'})
        self.assertEqual(V.policy_contract(policy),policy)

    def test_real_current17_toolpack_and_inputs_complete_positive_without_ROOT_rewrite(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve()
            validator,inputs_ref,inputs,pack,pack_ref,policy=closed_current_toolpack_fixture(root)
            self.assertEqual(validator.toolpack_contract(pack_ref,root),pack)
            self.assertEqual(validator.inputs_contract(inputs_ref),inputs)
            self.assertEqual(validator.load(root/V.TOOL_PREFIX/'probe-policy.json'),policy)
            self.assertEqual(len(pack['source_hashes']),17)
            self.assertEqual(set(inputs['probe_source_manifest']),{'path','sha256'})

    def test_each_added_field_missing_wrong_value_wrong_type_and_extra_rejected(self):
        good=actual_current_policy()
        for key in POLICY_ADDITIONS:
            variants=[]
            bad=copy.deepcopy(good);del bad[key];variants.append(('missing',bad))
            bad=copy.deepcopy(good);bad[key]=wrong_policy_value(bad[key]);variants.append(('wrong_value',bad))
            bad=copy.deepcopy(good);bad[key]=None;variants.append(('wrong_type',bad))
            bad=copy.deepcopy(good);bad[key+'_unexpected']=copy.deepcopy(bad[key]);variants.append(('extra_field',bad))
            for mutation,bad in variants:
                with self.subTest(field=key,mutation=mutation):
                    with self.assertRaises(V.ContractError):V.policy_contract(bad)

    def test_every_new_array_order_type_element_missing_or_extra_rejected(self):
        good=actual_current_policy()
        def arrays(value,path):
            if type(value) is dict:
                for key,child in value.items():yield from arrays(child,path+[key])
            elif type(value) is list:
                yield path,value
        for key in POLICY_ADDITIONS:
            for path,value in arrays(good[key],[key]):
                variants=[('reverse_order',list(reversed(value))),('tuple_type',tuple(value)),('missing_element',value[:-1]),('extra_element',value+[value[0]]),('wrong_element_type',[None]+value[1:])]
                for mutation,new in variants:
                    bad=copy.deepcopy(good);change(bad,path,new)
                    with self.subTest(path=path,mutation=mutation):
                        with self.assertRaises(V.ContractError):V.policy_contract(bad)

    def test_every_new_nested_object_key_and_integer_bool_type_is_closed(self):
        good=actual_current_policy()
        def objects(value,path):
            if type(value) is dict:
                yield path,value
                for key,child in value.items():yield from objects(child,path+[key])
        def scalars(value,path):
            if type(value) is dict:
                for key,child in value.items():yield from scalars(child,path+[key])
            elif type(value) in (int,bool):yield path,value
        for key in POLICY_ADDITIONS:
            for path,value in objects(good[key],[key]):
                bad=copy.deepcopy(good);change(bad,path,dict(value,unexpected=True))
                with self.subTest(path=path,mutation='nested_extra'):
                    with self.assertRaises(V.ContractError):V.policy_contract(bad)
                for missing in value:
                    bad=copy.deepcopy(good);new=copy.deepcopy(value);del new[missing];change(bad,path,new)
                    with self.subTest(path=path+[missing],mutation='nested_missing'):
                        with self.assertRaises(V.ContractError):V.policy_contract(bad)
            for path,value in scalars(good[key],[key]):
                for new in (True,float(value)) if type(value) is int else (0,1):
                    bad=copy.deepcopy(good);change(bad,path,new)
                    with self.subTest(path=path,mutation='integer_bool_type'):
                        with self.assertRaises(V.ContractError):V.policy_contract(bad)

    def test_current17_entry_drift_and_policy_drift_stop_entire_inputs_chain(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder).resolve()
            validator,inputs_ref,inputs,pack,pack_ref,policy=closed_current_toolpack_fixture(root)
            self.assertEqual(validator.inputs_contract(inputs_ref),inputs)
            for name in V.TOOL_FILES:
                path=root/V.TOOL_PREFIX/name;original=path.read_bytes();path.write_bytes(original+b'\nsource drift')
                with self.subTest(source=name):
                    with self.assertRaises(validator.ContractError):validator.inputs_contract(inputs_ref)
                path.write_bytes(original)
            # Re-pinning the JSON and toolpack after policy drift must still
            # fail the independent finite policy gate, including every new key.
            policy_path=root/V.TOOL_PREFIX/'probe-policy.json';pack_path=Path(pack_ref['path']);input_path=Path(inputs_ref['path'])
            original_policy=policy_path.read_bytes();original_pack=pack_path.read_bytes();original_inputs=input_path.read_bytes()
            for key in POLICY_ADDITIONS:
                bad=copy.deepcopy(policy);bad[key]=wrong_policy_value(bad[key]);policy_path.write_text(json.dumps(bad),encoding='utf-8')
                new_pack=copy.deepcopy(pack);new_pack['source_hashes'][V.TOOL_PREFIX+'probe-policy.json']=validator.sha(policy_path)
                pack_path.write_text(json.dumps(new_pack),encoding='utf-8')
                new_inputs=copy.deepcopy(inputs);new_inputs['toolpack']=dict(path=str(pack_path),sha256=validator.sha(pack_path))
                input_path.write_text(json.dumps(new_inputs),encoding='utf-8')
                with self.subTest(policy_field=key):
                    with self.assertRaises(validator.ContractError):validator.inputs_contract(dict(path=str(input_path),sha256=validator.sha(input_path)))
                policy_path.write_bytes(original_policy);pack_path.write_bytes(original_pack);input_path.write_bytes(original_inputs)


# The product fixture below is real, immutable provenance. Only in-memory
# copies are mutated; no reviewed file, ROOT, module identity, or SHA is forged.
PRODUCT_FIXTURE_SHA = '400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8'
SOURCE_APPROVAL_FIXTURE_SHA = '66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f'


def actual_product_v3_fixture():
    for root in HERE.parents:
        product=root/'tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json'
        approval=root/'.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json'
        if product.is_file() and approval.is_file():
            if V.sha(product)!=PRODUCT_FIXTURE_SHA or V.sha(approval)!=SOURCE_APPROVAL_FIXTURE_SHA:
                raise AssertionError('real_product_v3_or_new_source_approval_fixture_drift')
            manifest=V.read_ref(dict(path=str(product),sha256=PRODUCT_FIXTURE_SHA),root)
            source_approval=V.read_ref(dict(path=str(approval),sha256=SOURCE_APPROVAL_FIXTURE_SHA),root)
            paths=sorted(manifest['source_hashes'])
            hosts=sorted('test/TP07TransactionCapabilityProbe/'+name.removeprefix('host/') for name in V.TOOL_FILES if name.startswith('host/'))
            lineage=dict(manifest['base_lineage'],approved_ef_source_manifest_sha256=PRODUCT_FIXTURE_SHA,transaction_patch_sha256=manifest['transaction_capability_patch']['sha256'],parent_overlay_sha256=manifest['base_prepared_source']['sha256'])
            # This is only the source-boundary subset consumed by the pure
            # contracts. No fabricated executable/native/runtime fields exist.
            inputs=dict(workspace_root=str(root),ef_overlay=dict(source_root=manifest['source_root'],manifest=dict(path=str(product),sha256=PRODUCT_FIXTURE_SHA),root_approval=dict(path=str(approval),sha256=SOURCE_APPROVAL_FIXTURE_SHA),lineage=lineage),expected_closure=dict(product_paths=paths,product_count=178,host_paths=hosts,source_paths=sorted(paths+hosts),source_count=187,locked_paths=sorted(paths+hosts+['test/TP07TransactionCapabilityProbe/packages.lock.json']),locked_count=188))
            parent=manifest['unit_revision_parent']
            provenance={key:V.read_ref(parent[key],root) for key in ('product_manifest','sourcefreeze_manifest','source_approval')}
            rows=V.read_ref(manifest['full_unit_inventory']['baseline_rows'],root)
            return root,manifest,inputs,lineage,source_approval,provenance,rows
    raise AssertionError('actual_product_v3_and_new_source_approval_required')


def scalar_paths(value,path=()):
    if type(value) is dict:
        for key,child in value.items():yield from scalar_paths(child,path+(key,))
    elif type(value) is list:
        for index,child in enumerate(value):yield from scalar_paths(child,path+(index,))
    elif type(value) in (int,bool):yield path,value


class ActualProductV3SourceContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root,cls.manifest,cls.inputs,cls.lineage,cls.approval,cls.parents,cls.rows=actual_product_v3_fixture()

    def reject(self, function, *args):
        with self.assertRaises(V.ContractError) as caught:function(*args)
        # Semantic negatives operate after provenance reads. They must not
        # accidentally pass merely by rejecting a recomputed document SHA.
        self.assertNotIn('ref_sha_mismatch',str(caught.exception))

    def product_reject(self, bad, inputs=None, lineage=None):
        self.reject(V.product_manifest_contract,bad,self.inputs if inputs is None else inputs,self.lineage if lineage is None else lineage)

    def test_actual400123_complete43_and_new66c3_source_approval_positive(self):
        self.assertEqual(len(self.manifest),43)
        self.assertEqual(V.product_manifest_contract(self.manifest,self.inputs,self.lineage),self.manifest)
        self.assertEqual(V.product_source_approval_contract(self.approval,self.inputs,self.manifest),self.approval)
        self.assertEqual(len(self.rows),795)
        self.assertEqual(sum(row['outcome']=='Failed' for row in self.rows),12)
        self.assertFalse(self.manifest['tests_executed'])
        self.assertFalse(self.manifest['retry_contract_inventory']['synthetic_true_classification_is_transport_evidence'])

    def test_each_actual43_field_missing_and_extra_rejected_semantically(self):
        for key in self.manifest:
            with self.subTest(field=key):
                bad=copy.deepcopy(self.manifest);del bad[key];self.product_reject(bad)
                bad=copy.deepcopy(self.manifest);bad[key+'_extra']=copy.deepcopy(bad[key]);self.product_reject(bad)
        self.assertEqual(V.sha(self.inputs['ef_overlay']['manifest']['path']),PRODUCT_FIXTURE_SHA)

    def test_only_exact_v3_task_revision_and_primitive_types(self):
        for path,val in [(('task',),'TP07-T1 transaction-capability isolated EF overlay'),(('task',),self.parents['product_manifest']['task']),(('task',),self.manifest['task']+' future'),(('revision',),2),(('revision',),4)]:
            with self.subTest(path=path,value=val):
                bad=copy.deepcopy(self.manifest);change(bad,path,val);self.product_reject(bad)
        for path,val in scalar_paths(self.manifest):
            # Nested inventories have dedicated faster, no-I/O checks below.
            if path[0] in ('retry_contract_inventory','full_unit_inventory'):continue
            for wrong in (True,float(val)) if type(val) is int else (0,1):
                with self.subTest(path=path,value=wrong):
                    bad=copy.deepcopy(self.manifest);change(bad,path,wrong);self.product_reject(bad)
        self.product_reject(copy.deepcopy(self.parents['product_manifest']))

    def test_exact178_187_188_closures_reject_count_only_and_path_substitution(self):
        for count in (179,187,188,True,178.0):
            bad=copy.deepcopy(self.manifest);bad['source_count']=count;self.product_reject(bad)
        bad=copy.deepcopy(self.manifest);bad['source_hashes']['src/Extra.cs']=H;self.product_reject(bad)
        bad=copy.deepcopy(self.manifest);key=next(iter(bad['source_hashes']));bad['source_hashes']['src/Substitute.cs']=bad['source_hashes'].pop(key);self.product_reject(bad)
        for key in ('product_count','source_count','locked_count'):
            for wrong in (self.inputs['expected_closure'][key]+1,True,float(self.inputs['expected_closure'][key])):
                bad=copy.deepcopy(self.inputs);bad['expected_closure'][key]=wrong;self.product_reject(self.manifest,bad)
        for key in ('product_paths','host_paths','source_paths','locked_paths'):
            bad=copy.deepcopy(self.inputs);bad['expected_closure'][key][0]='src/Substitute.cs';self.product_reject(self.manifest,bad)
        bad=copy.deepcopy(self.manifest);bad['source_root']=str(self.root/'.local/temporal-native/verification/TP07-T1-product-v3-retry-sourcefreeze/ef-source');self.product_reject(bad)

    def test_actual_two_modified_five_patch_and_173_base_relationships(self):
        for path,val in [(('modified_files',),self.manifest['modified_files'][:1]),(('modified_files',),self.manifest['modified_files']+['src/Third.cs']),(('patch_paths',),self.manifest['patch_paths'][:-1]),(('patch_paths',),list(reversed(self.manifest['patch_paths']))),(('unchanged_base_file_count',),174)]:
            bad=copy.deepcopy(self.manifest);change(bad,path,val);self.product_reject(bad)
        bad=copy.deepcopy(self.manifest);name=next(iter(bad['base_source_hashes']));bad['base_source_hashes'][name]=H;self.product_reject(bad)
        bad=copy.deepcopy(self.manifest);bad['base_source_hashes'].pop(next(iter(bad['base_source_hashes'])));self.product_reject(bad)

    def test_all_ten_parent_refs_shape_pin_path_missing_and_symlink_rejected(self):
        parent=self.manifest['unit_revision_parent'];self.assertEqual(len(parent),10)
        for key,reference in parent.items():
            V.ref(reference,self.root)
            for mutation in ('sha','path','missing_field','extra_field'):
                bad=copy.deepcopy(self.manifest);changed=bad['unit_revision_parent'][key]
                if mutation=='sha':changed['sha256']='b'*64
                elif mutation=='path':changed['path']+='-missing'
                elif mutation=='missing_field':del changed['sha256']
                else:changed['extra']=True
                with self.subTest(ref=key,mutation=mutation):self.product_reject(bad)
            with tempfile.TemporaryDirectory() as folder:
                link=Path(folder).resolve()/'parent-link';link.symlink_to(reference['path'])
                with self.assertRaises(V.ContractError):V.ref(dict(path=str(link),sha256=reference['sha256']))
            with self.assertRaises(V.ContractError):V.ref(dict(path=reference['path']+'-missing',sha256=reference['sha256']),self.root)
        for key in parent:
            bad=copy.deepcopy(self.manifest);del bad['unit_revision_parent'][key];self.product_reject(bad)

    def test_parent178_freeze_approval_and_single177_source_delta(self):
        p=self.parents
        def reject(value=None,parent=None,frozen=None,approval=None):
            self.reject(V.product_parent_contract,self.manifest if value is None else value,p['product_manifest'] if parent is None else parent,p['sourcefreeze_manifest'] if frozen is None else frozen,p['source_approval'] if approval is None else approval)
        V.product_parent_contract(self.manifest,p['product_manifest'],p['sourcefreeze_manifest'],p['source_approval'])
        bad=copy.deepcopy(p['sourcefreeze_manifest']);bad['source_hashes'][next(iter(bad['source_hashes']))]=H;reject(frozen=bad)
        bad=copy.deepcopy(p['source_approval']);bad['manifest']=self.inputs['ef_overlay']['manifest'];reject(approval=bad)
        bad=copy.deepcopy(p['source_approval']);bad['exact_paths']=bad['exact_paths'][:-1];reject(approval=bad)
        bad=copy.deepcopy(p['product_manifest']);bad['source_hashes'][next(name for name in bad['source_hashes'] if name!=V.RETRY_PATH)]=H;reject(parent=bad)
        bad=copy.deepcopy(self.manifest);name=next(name for name in bad['source_hashes'] if name!=V.RETRY_PATH);bad['source_hashes'][name]=H;reject(value=bad)
        for key in ('parent_unit_sha256','new_unit_sha256','transaction_unit25_sha256'):
            bad=copy.deepcopy(self.manifest);bad['unit_delta_from_parent'][key]=H;reject(value=bad)
        for path,val in [(('unit_delta_from_parent','unchanged_source_count'),176),(('unit_delta_from_parent','changed_files'),[V.RETRY_PATH,'src/Second.cs'])]:
            bad=copy.deepcopy(self.manifest);change(bad,path,val);self.product_reject(bad)

    def test_new_source_scope_cannot_be_downgraded_even_when_both_agree(self):
        for scope in ('transaction_capability_overlay_only','transaction_capability_and_w_retry_contract_tests_only_or_overlay','other'):
            bad=copy.deepcopy(self.approval);bad['scope']=scope
            self.reject(V.product_source_approval_contract,bad,self.inputs,self.manifest)
            paired=copy.deepcopy(self.manifest);paired['required_source_approval_scope']=scope
            self.reject(V.product_source_approval_contract,bad,self.inputs,paired)
            self.product_reject(paired)
        for key in self.approval:
            bad=copy.deepcopy(self.approval);del bad[key];self.reject(V.product_source_approval_contract,bad,self.inputs,self.manifest)
        for path,val in [(('exact_count',),True),(('exact_count',),178.0),(('exact_paths',),self.approval['exact_paths'][:-1]),(('schema_version',),True),(('status',),'runtime_authorized'),(('manifest',),self.manifest['unit_revision_parent']['product_manifest'])]:
            bad=copy.deepcopy(self.approval);change(bad,path,val);self.reject(V.product_source_approval_contract,bad,self.inputs,self.manifest)
        bad=dict(self.approval,runtime_authorized=True);self.reject(V.product_source_approval_contract,bad,self.inputs,self.manifest)

    def test_three_lineage_roles_and_all_seven_ordered_gates(self):
        for key,val in [('approved_ef_source_manifest_sha256',self.manifest['base_approved_source']['sha256']),('approved_ef_source_manifest_sha256',self.manifest['unit_revision_parent']['product_manifest']['sha256']),('parent_overlay_sha256',self.manifest['unit_revision_parent']['product_manifest']['sha256']),('transaction_patch_sha256',self.manifest['unit_revision_parent']['transaction_capability_patch']['sha256'])]:
            bad=dict(self.lineage);bad[key]=val;self.product_reject(self.manifest,lineage=bad)
        for gates in (self.manifest['required_next_gates'][:5],self.manifest['required_next_gates'][:3],list(reversed(self.manifest['required_next_gates'])),['Astra source review','Low isolated restore/build and focused transaction DI/BatchExecutor units','Low full provider units and new overlay source/patch proof']):
            bad=copy.deepcopy(self.manifest);bad['required_next_gates']=gates;self.product_reject(bad)
        for reference in (self.manifest['base_approved_source'],self.manifest['unit_revision_parent']['product_manifest']):
            bad=copy.deepcopy(self.inputs);bad['ef_overlay']['manifest']=reference;self.product_reject(self.manifest,bad)

    def test_retry_inventory_shape_counts_and_every_wrong_primitive_type(self):
        good=self.manifest['retry_contract_inventory']
        for key in good:
            bad=copy.deepcopy(self.manifest);del bad['retry_contract_inventory'][key]
            self.reject(V.product_retry_inventory_contract,bad,self.rows)
        bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['passed_cases']=49;self.reject(V.product_retry_inventory_contract,bad,self.rows)
        for path,val in scalar_paths(good):
            for wrong in (True,float(val),val+1) if type(val) is int else (0,1,not val):
                bad=copy.deepcopy(self.manifest);change(bad,('retry_contract_inventory',)+path,wrong)
                with self.subTest(path=path,value=wrong):self.reject(V.product_retry_inventory_contract,bad,self.rows)
        for path,val in [(('planned_retry_unit_cases',),48),(('planned_retry_unit_methods',),20),(('planned_full_unit_cases',),795)]:
            bad=copy.deepcopy(self.manifest);change(bad,path,val);self.product_reject(bad)

    def test_exact21_methods_inline_data_order_parameter_types_and_names(self):
        good=self.manifest['retry_contract_inventory']['planned_methods']
        for index,method in enumerate(good):
            for key in method:
                bad=copy.deepcopy(self.manifest);del bad['retry_contract_inventory']['planned_methods'][index][key]
                self.reject(V.product_retry_inventory_contract,bad,self.rows)
            for key,val in [('attribute','Theory' if method['attribute']=='Fact' else 'Fact'),('method',method['method']+'Changed'),('parameters',tuple(method['parameters'])),('inline_data',tuple(method['inline_data'])),('planned_test_names',method['planned_test_names'][:-1])]:
                bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['planned_methods'][index][key]=val;self.reject(V.product_retry_inventory_contract,bad,self.rows)
            if len(method['inline_data'])>1:
                bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['planned_methods'][index]['inline_data']=list(reversed(method['inline_data']));self.reject(V.product_retry_inventory_contract,bad,self.rows)
        for methods in (good[:-1],good+[good[0]],list(reversed(good))):
            bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['planned_methods']=methods;self.reject(V.product_retry_inventory_contract,bad,self.rows)
        bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['planned_methods'][2]['planned_test_names'][1]=bad['retry_contract_inventory']['planned_methods'][2]['planned_test_names'][0];self.reject(V.product_retry_inventory_contract,bad,self.rows)

    def test_original20_original12_failed_mapping_and_new29_disjoint_sets(self):
        inv=self.manifest['retry_contract_inventory']
        for key in ('original20_to_new_mapping','original12_failed_to_new_mapping'):
            for mutation in ('omit','duplicate','reverse','outcome','target','source','extra_field'):
                bad=copy.deepcopy(self.manifest);array=bad['retry_contract_inventory'][key]
                if mutation=='omit':array.pop()
                elif mutation=='duplicate':array[1]=copy.deepcopy(array[0])
                elif mutation=='reverse':array.reverse()
                elif mutation=='outcome':array[0]['original_outcome']='Passed'
                elif mutation=='target':array[0]['new_planned_test_name']=array[1]['new_planned_test_name']
                elif mutation=='source':array[0]['original_test_name']+='Changed'
                else:array[0]['extra']=True
                with self.subTest(key=key,mutation=mutation):self.reject(V.product_retry_inventory_contract,bad,self.rows)
        for names in (inv['new29_planned_test_names'][:-1],inv['new29_planned_test_names'][:-1]+[inv['new29_planned_test_names'][0]],inv['new29_planned_test_names'][:-1]+[inv['original20_to_new_mapping'][0]['new_planned_test_name']]):
            bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['new29_planned_test_names']=names;self.reject(V.product_retry_inventory_contract,bad,self.rows)

    def test_baseline795_finite_rows_unique_names_ids_and_original_failures(self):
        V.product_baseline_rows_contract(self.rows,self.root)
        mutations=[(('outcome',),'Passed'),(('outcome',),'Skipped'),(('execution_id',),self.rows[1]['execution_id']),(('test_name',),self.rows[1]['test_name']),(('method','adapterTypeName'),'unknown')]
        failed=next(index for index,row in enumerate(self.rows) if row['outcome']=='Failed')
        for path,val in mutations:
            bad=copy.deepcopy(self.rows);change(bad[failed],path,val);self.reject(V.product_baseline_rows_contract,bad,self.root)
        for key in self.rows[0]:
            bad=copy.deepcopy(self.rows);del bad[0][key];self.reject(V.product_baseline_rows_contract,bad,self.root)
        bad=copy.deepcopy(self.rows);bad[0]['method']['extra']=True;self.reject(V.product_baseline_rows_contract,bad,self.root)
        for bad in (self.rows[:-1],self.rows+[self.rows[0]],tuple(self.rows)):
            self.reject(V.product_baseline_rows_contract,bad,self.root)

    def test_full824_complete_union_not_count_only_and_transaction25_retained(self):
        retries=V.product_retry_inventory_contract(self.manifest,self.rows)
        V.product_full_inventory_contract(self.manifest,self.rows,retries,self.root)
        good=self.manifest['full_unit_inventory']
        for key in good:
            bad=copy.deepcopy(self.manifest);del bad['full_unit_inventory'][key];self.reject(V.product_full_inventory_contract,bad,self.rows,retries,self.root)
        for path,val in scalar_paths(good):
            for wrong in (True,float(val),val+1) if type(val) is int else (0,1,not val):
                bad=copy.deepcopy(self.manifest);change(bad,('full_unit_inventory',)+path,wrong);self.reject(V.product_full_inventory_contract,bad,self.rows,retries,self.root)
        for replacement in ('W.EntityFrameworkCore.Dameng.Tests.Other.Substitute',good['planned_full_test_names'][1]):
            bad=copy.deepcopy(self.manifest);bad['full_unit_inventory']['planned_full_test_names'][0]=replacement;bad['full_unit_inventory']['planned_full_test_names'].sort();self.assertEqual(len(bad['full_unit_inventory']['planned_full_test_names']),824);self.reject(V.product_full_inventory_contract,bad,self.rows,retries,self.root)
        bad=copy.deepcopy(self.manifest);names=bad['full_unit_inventory']['planned_full_test_names'];index=next(i for i,name in enumerate(names) if '.DamengRelationalTransactionCapabilityTests.' in name);names[index]='W.EntityFrameworkCore.Dameng.Tests.Substitute.Transaction';names.sort();self.reject(V.product_full_inventory_contract,bad,self.rows,retries,self.root)
        changed=copy.deepcopy(self.rows);row=next(row for row in changed if row['method']['className']==V.TRANSACTION_CLASS);row['method']['className']='W.EntityFrameworkCore.Dameng.Tests.Substitute';self.reject(V.product_full_inventory_contract,self.manifest,changed,retries,self.root)
        for wrong in (good['planned_full_test_names'][:-1],list(reversed(good['planned_full_test_names'])),tuple(good['planned_full_test_names'])):
            bad=copy.deepcopy(self.manifest);bad['full_unit_inventory']['planned_full_test_names']=wrong;self.reject(V.product_full_inventory_contract,bad,self.rows,retries,self.root)

    def test_static_plans_never_sdk_trx_or_transport_acceptance(self):
        for key in ('SDK_executed','tests_executed','database_executed','old_parent_source_approval_reused_for_revision','old_L0_E_D_cohorts_reused_as_new_acceptance'):
            bad=copy.deepcopy(self.manifest);bad[key]=True;self.product_reject(bad)
        bad=copy.deepcopy(self.manifest);bad['retry_contract_inventory']['synthetic_true_classification_is_transport_evidence']=True;self.reject(V.product_retry_inventory_contract,bad,self.rows)
        bad=copy.deepcopy(self.manifest);bad['full_unit_inventory']['actual_total_executed_passed']=824
        retries=V.product_retry_inventory_contract(self.manifest,self.rows);self.reject(V.product_full_inventory_contract,bad,self.rows,retries,self.root)
        for key in ('retry_contract_inventory','full_unit_inventory'):
            bad=copy.deepcopy(self.manifest);bad[key]['inventory_kind']='actual SDK discovery and passed TRX';self.product_reject(bad)

# V6 raw fixtures use actual historical failed-build files, or an explicitly
# synthetic controlled directory. Neither fixture represents SDK/build success.
HISTORICAL_RAW_REPORT_SHA = 'bd802d330eaf136ea559a01010a12264357171df3b910294b273c8489166c81e'
HISTORICAL_SOURCE_SHA = 'c551e73319a1b2ac3e173f0bf771776b122fa86b461806f637f15043a3765834'


def historical_failed_build_raw_fixture():
    for root in HERE.parents:
        base=root/'.local/temporal-native/verification'
        report_path=base/'TP07-T1-probe-v5-build-control/raw-native-before-after.json'
        if not report_path.is_file():continue
        if V.sha(report_path)!=HISTORICAL_RAW_REPORT_SHA:raise AssertionError('historical_raw_report_drift')
        source_path=base/'TP07-T1-probe-v5-source/source.json'
        if V.sha(source_path)!=HISTORICAL_SOURCE_SHA:raise AssertionError('historical_source_fixture_drift')
        replay=Path(V.load(source_path)['replay_root']);report=V.load(report_path)
        before=base/'TP07-T1-probe-v5-native-control/native-original-raw-snapshot'
        after=base/'TP07-T1-probe-v5-build-control/postbuild-raw-snapshot'
        pairs=[];locks=[]
        for project in V.PROJECTS:
            paths=V.raw_project_paths(project)
            for kind in ('lock','assets','dgspec'):
                key=str(replay/paths[kind])
                old=dict(path=str(before/paths[kind]),sha256=report['before'][key])
                new=dict(path=str(after/paths[kind]),sha256=report['after'][key])
                V.ref(old,root);V.ref(new,root)
                if kind=='lock':locks.append((old,new))
                else:pairs.append((project,kind,old,new))
        return replay,pairs,locks
    raise AssertionError('immutable_historical_failed_build_raw_required')


def raw_mode_locations(value, kind):
    if kind=='assets':return [value['project']['restore']['restoreLockProperties']]
    return [project['restore']['restoreLockProperties'] for project in value['projects'].values()]


def controlled_raw_stage_fixture(root):
    """Boundary-only test data; empty SDK events cannot pass native_contract."""
    base=root/'.local/temporal-native/verification/synthetic-v6-raw-contract'
    replay=base/'source/replay';native_dir=base/'native';build_dir=base/'build'
    def write_ref(path,value):
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_text(json.dumps(value,sort_keys=True,indent=2),encoding='utf-8')
        return dict(path=str(path),sha256=V.sha(path))
    source_ref=write_ref(base/'source/boundary-fixture.json',dict(role='synthetic raw boundary only; no SDK acceptance'))
    input_ref=write_ref(base/'inputs.json',dict(role='synthetic boundary only'))
    graphs=[];locked=[]
    for index,project in enumerate(V.PROJECTS):
        paths=V.raw_project_paths(project)
        lock=write_ref(replay/paths['lock'],dict(version=1,synthetic_project=project))
        graph=dict(project=project,lock=lock,lock_dependencies={},frameworks={});row=dict(project=project)
        names=[str(replay/V.PROJECTS[0])]
        if index==1:names.append(str(replay/V.PROJECTS[1]))
        assets=dict(version=3,targets={'net10.0':{'Fixture/1':{'runtime':{'lib/net10.0/Fixture.dll':{}}}}},libraries={'Fixture/1':{'sha512':'fixture-content-hash','type':'package'}},project=dict(restore=dict(projectPath=str(replay/project),restoreLockProperties=dict(restorePackagesWithLockFile=True),originalTargetFrameworks=['net10.0']),frameworks={'net10.0':{'prune':{'Fixture':'[1, )'},'dependencies':{'Fixture':{'target':'Package','version':'[1, )'}}}}),header={'role':'synthetic fixture'})
        dgspec=dict(format=1,restore={str(replay/project):{}},projects={name:dict(restore=dict(projectPath=name,restoreLockProperties=dict(restorePackagesWithLockFile=True)),frameworks={'net10.0':{'prune':{'Fixture':'[1, )'}}}) for name in names})
        for kind,value in (('assets',assets),('dgspec',dgspec)):
            graph[kind]=write_ref(native_dir/'raw-native'/paths[kind],value)
            after=copy.deepcopy(value)
            for location in raw_mode_locations(after,kind):location['restoreLockedMode']=True
            row[kind]=write_ref(build_dir/'raw-locked'/paths[kind],after)
            write_ref(replay/paths[kind],after)
        graphs.append(graph);locked.append(row)
    native=dict(schema_version=1,task='TP07-T1-native',inputs=input_ref,source=source_ref,sdk_events=[],project_graphs=graphs,cache_root=str(base/'unused-cache'),archive_records=[],native_metadata_records=[],source_before={},source_after={},database_accessed=False)
    native_ref=write_ref(native_dir/'native.json',native)
    locks={g['project']:g['lock'] for g in graphs}
    startup=dict(source=source_ref,native=native_ref,replay_root=str(replay),locks={V.raw_project_paths(g['project'])['lock']:g['lock']['sha256'] for g in graphs},assets={V.raw_project_paths(g['project'])['assets']:g['assets']['sha256'] for g in locked},fixture_role='synthetic boundary only')
    startup_ref=write_ref(build_dir/'startup.json',startup)
    build=dict(schema_version=1,task='TP07-T1-build',inputs=input_ref,source=source_ref,native=native_ref,locked_restores=[],commands=[],startup=startup_ref,binaries={},source_before={},source_after={},lock_before=copy.deepcopy(locks),lock_after=copy.deepcopy(locks),offline_proofs=[],database_accessed=False,locked_project_graphs=locked)
    build_ref=write_ref(build_dir/'build.json',build)
    return dict(native=native,build=build,startup=startup,inputs=dict(workspace_root=str(root)),replay=replay,native_ref=native_ref,build_ref=build_ref),write_ref


class ImmutablePhaseRawContracts(unittest.TestCase):
    def reject_boundary(self,value):
        with self.assertRaises(V.ContractError):V.locked_project_graphs_contract(**value)

    def test_historical_failed_build_raw_full_relation_and_unchanged_locks(self):
        replay,pairs,locks=historical_failed_build_raw_fixture()
        self.assertEqual(len(pairs),4);self.assertEqual(len(locks),2)
        insertions=0
        for project,kind,old,new in pairs:
            before=V.read_ref(old);after=V.read_ref(new)
            V.locked_raw_delta_contract(before,after,project,kind,replay)
            insertions+=len(raw_mode_locations(before,kind))
        self.assertEqual(insertions,5)
        for old,new in locks:
            self.assertEqual(old['sha256'],new['sha256'])
            self.assertEqual(Path(old['path']).read_bytes(),Path(new['path']).read_bytes())

    def test_controlled_raw_stage_ref_live_positive_boundary_only(self):
        with tempfile.TemporaryDirectory() as directory:
            value,_=controlled_raw_stage_fixture(Path(directory).resolve())
            original,locked=V.locked_project_graphs_contract(**value)
            self.assertEqual(len(original),2);self.assertEqual(len(locked),2)
            self.assertEqual(value['build']['commands'],[])
            self.assertEqual(value['native']['sdk_events'],[])

    def test_finite_build_and_locked_graph_shapes_N01_to_N09(self):
        with tempfile.TemporaryDirectory() as directory:
            value,_=controlled_raw_stage_fixture(Path(directory).resolve())
            cases=[]
            bad=copy.deepcopy(value);del bad['build']['locked_project_graphs'];cases.append(('N01',bad))
            bad=copy.deepcopy(value);bad['build']['accepted']=True;cases.append(('N02',bad))
            for label,rows in [('N03',value['build']['locked_project_graphs'][:1]),('N04',value['build']['locked_project_graphs']+[value['build']['locked_project_graphs'][0]]),('N05',value['build']['locked_project_graphs'][::-1]),('N06',[value['build']['locked_project_graphs'][0]]*2),('N08',{})]:
                bad=copy.deepcopy(value);bad['build']['locked_project_graphs']=copy.deepcopy(rows);cases.append((label,bad))
            for key in ('assets','dgspec','project'):
                bad=copy.deepcopy(value);del bad['build']['locked_project_graphs'][0][key];cases.append(('N07-missing-'+key,bad))
            bad=copy.deepcopy(value);bad['build']['locked_project_graphs'][0]['lock']=reference();cases.append(('N07-extra',bad))
            for replacement in ([],1,None):
                bad=copy.deepcopy(value);bad['build']['locked_project_graphs'][0]['assets']=replacement;cases.append(('N08-ref',bad))
            bad=copy.deepcopy(value);bad['build']['locked_project_graphs'][0]['project']='other/project.csproj';cases.append(('N09-project',bad))
            for kind in ('assets','dgspec'):
                bad=copy.deepcopy(value);bad['build']['locked_project_graphs'][0][kind]=copy.deepcopy(value['build']['locked_project_graphs'][1][kind]);cases.append(('N09-cross-'+kind,bad))
            for label,bad in cases:
                with self.subTest(template=label):self.reject_boundary(bad)

    def test_stage_path_substitution_N10_to_N12(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory).resolve();value,write_ref=controlled_raw_stage_fixture(root)
            for index,project in enumerate(V.PROJECTS):
                for kind in ('assets','dgspec'):
                    for label,path in [('N10',value['replay']/V.raw_project_paths(project)[kind]),('N12-other-stage',root/'other-stage'/V.raw_project_paths(project)[kind]),('N12-escape',root/'outside.json')]:
                        bad=copy.deepcopy(value)
                        if label!='N10':write_ref(path,V.read_ref(value['native']['project_graphs'][index][kind]))
                        bad['native']['project_graphs'][index][kind]=dict(path=str(path),sha256=V.sha(path))
                        with self.subTest(template=label,project=project,kind=kind):self.reject_boundary(bad)
                    bad=copy.deepcopy(value);bad['build']['locked_project_graphs'][index][kind]=copy.deepcopy(value['native']['project_graphs'][index][kind])
                    with self.subTest(template='N11',project=project,kind=kind):self.reject_boundary(bad)
                    bad=copy.deepcopy(value);bad['build']['locked_project_graphs'][index][kind]['path']=str(root/'..'/Path(bad['build']['locked_project_graphs'][index][kind]['path']).name)
                    with self.subTest(template='N12-parent'):self.reject_boundary(bad)

    def test_snapshot_and_live_physical_integrity_N13_to_N16(self):
        for label in ('N13-snapshot','N13-live','N14','N15','N15-empty-directory','N16'):
            with tempfile.TemporaryDirectory() as directory:
                value,_=controlled_raw_stage_fixture(Path(directory).resolve())
                target=Path(value['build']['locked_project_graphs'][0]['assets']['path'])
                if label=='N13-live':target=value['replay']/V.raw_project_paths(V.PROJECTS[0])['assets']
                if label.startswith('N13'):
                    saved=target.with_name('symlink-target-copy');shutil.copyfile(target,saved);target.unlink();target.symlink_to(saved)
                elif label=='N14':target.unlink()
                elif label=='N15':target.with_name('extra.json').write_text('{}')
                elif label=='N15-empty-directory':target.with_name('extra-directory').mkdir()
                else:target.write_bytes(target.read_bytes()+b'\n')
                with self.subTest(template=label):self.reject_boundary(value)

    def test_real_live_lock_and_before_after_binding_N17_to_N19(self):
        for label in ('N17','N18','N19'):
            with tempfile.TemporaryDirectory() as directory:
                value,_=controlled_raw_stage_fixture(Path(directory).resolve());project=V.PROJECTS[0]
                original=copy.deepcopy(value['native']['project_graphs'])
                if label=='N17':
                    live=Path(value['native']['project_graphs'][0]['lock']['path']);live.write_bytes(live.read_bytes()+b'\n')
                elif label=='N18':value['build']['lock_before'][project]=copy.deepcopy(value['native']['project_graphs'][0]['assets'])
                else:value['build']['lock_after'][project]['sha256']='b'*64
                with self.subTest(template=label):self.reject_boundary(value)
                self.assertEqual(value['native']['project_graphs'],original)

    def test_only_live_assets_or_dgspec_drift_N20_N21(self):
        for kind,label in [('assets','N20'),('dgspec','N21')]:
            for index,project in enumerate(V.PROJECTS):
                with tempfile.TemporaryDirectory() as directory:
                    value,_=controlled_raw_stage_fixture(Path(directory).resolve())
                    snapshot=Path(value['build']['locked_project_graphs'][index][kind]['path']);saved=snapshot.read_bytes()
                    live=value['replay']/V.raw_project_paths(project)[kind];live.write_bytes(live.read_bytes()+b'\n')
                    with self.subTest(template=label,project=project):self.reject_boundary(value)
                    self.assertEqual(snapshot.read_bytes(),saved)

    def test_startup_requires_locked_hash_and_replay_relative_keys_N22_N23(self):
        for label in ('N22','N23-snapshot','N23-wrong-live'):
            with tempfile.TemporaryDirectory() as directory:
                value,write_ref=controlled_raw_stage_fixture(Path(directory).resolve())
                paths=V.raw_project_paths(V.PROJECTS[0]);assets=value['startup']['assets']
                if label=='N22':assets[paths['assets']]=value['native']['project_graphs'][0]['assets']['sha256']
                else:
                    hash_value=assets.pop(paths['assets']);key=value['build']['locked_project_graphs'][0]['assets']['path'] if label=='N23-snapshot' else 'wrong/obj/project.assets.json';assets[key]=hash_value
                value['build']['startup']=write_ref(Path(value['build']['startup']['path']),value['startup'])
                # Re-sign controlled startup only: failure must be its semantic mapping.
                with self.subTest(template=label):
                    with self.assertRaisesRegex(V.ContractError,'raw_startup_actual_locked_live_assets'):V.locked_project_graphs_contract(**value)

    def test_same_actual_refs_mode_semantics_N24_to_N28(self):
        replay,pairs,_=historical_failed_build_raw_fixture()
        for project,kind,old,new in pairs:
            native=V.read_ref(old);locked=V.read_ref(new)
            for position in range(len(raw_mode_locations(native,kind))):
                for label,replacement in [('N24',False),('N25',1),('N26','true'),('N27',None),('N28',True)]:
                    before=copy.deepcopy(native);after=copy.deepcopy(locked)
                    if label=='N28':raw_mode_locations(before,kind)[position]['restoreLockedMode']=True
                    elif label=='N27':del raw_mode_locations(after,kind)[position]['restoreLockedMode']
                    else:raw_mode_locations(after,kind)[position]['restoreLockedMode']=replacement
                    with self.subTest(template=label,project=project,kind=kind,position=position):
                        with self.assertRaisesRegex(V.ContractError,'raw_(native_no_locked_mode|locked_exact_true)'):V.locked_raw_delta_contract(before,after,project,kind,replay)
                    # Immutable Ref bytes are unchanged; no whole-file SHA oracle.
                    V.ref(old);V.ref(new)

    def test_same_actual_refs_all_other_json_fields_rejected_N29_to_N32(self):
        replay,pairs,_=historical_failed_build_raw_fixture()
        for project,kind,old,new in pairs:
            native=V.read_ref(old);locked=V.read_ref(new);cases=[]
            bad=copy.deepcopy(locked);bad['unapproved_header']='changed';cases.append(('N29-header',native,bad))
            if kind=='assets':
                for label,path,replacement in [('N29-restore',['project','restore','originalTargetFrameworks'],['other']),('N30-framework',['project','frameworks'],{}),('N31-targets',['targets'],{}),('N31-libraries',['libraries'],{})]:
                    bad=copy.deepcopy(locked);change(bad,path,replacement);cases.append((label,native,bad))
                bad=copy.deepcopy(locked);framework=next(iter(bad['project']['frameworks'].values()));framework['prune']={'extra.package':'[1, )'};cases.append(('N30-pruning',native,bad))
                bad=copy.deepcopy(locked);next(iter(bad['libraries'].values()))['sha512']='changed-content-hash';cases.append(('N31-contentHash',native,bad))
                bad=copy.deepcopy(locked);bad['project']['restore']['projectPath']=str(replay/'other.csproj');cases.append(('N32-assets-path',native,bad))
            else:
                for name in locked['projects']:
                    bad=copy.deepcopy(locked);bad['projects'][name]['restore']['projectPath']=str(replay/'other.csproj');cases.append(('N32-locked-path',native,bad))
                    before=copy.deepcopy(native);after=copy.deepcopy(locked);before['projects'][name]['restore']['projectPath']=str(replay/'other.csproj');after['projects'][name]['restore']['projectPath']=str(replay/'other.csproj');cases.append(('N32-both-path',before,after))
                    bad=copy.deepcopy(locked);del bad['projects'][name];cases.append(('N32-missing-project',native,bad))
                before=copy.deepcopy(native);after=copy.deepcopy(locked);name=next(iter(before['projects']));before['projects'][str(replay/'other.csproj')]=before['projects'].pop(name);after['projects'][str(replay/'other.csproj')]=after['projects'].pop(name);cases.append(('N32-both-project-set',before,after))
            for label,before,after in cases:
                with self.subTest(template=label,project=project,kind=kind):
                    with self.assertRaises(V.ContractError) as raised:V.locked_raw_delta_contract(before,after,project,kind,replay)
                    self.assertNotIn('ref_sha',str(raised.exception))
                V.ref(old);V.ref(new)

    def test_resigned_controlled_locked_snapshot_and_live_pruning_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            value,write_ref=controlled_raw_stage_fixture(Path(directory).resolve())
            row=value['build']['locked_project_graphs'][0];changed=V.read_ref(row['assets']);changed['project']['frameworks']['net10.0']['prune']['extra.package']='[1, )'
            row['assets']=write_ref(Path(row['assets']['path']),changed)
            write_ref(value['replay']/V.raw_project_paths(V.PROJECTS[0])['assets'],changed)
            value['startup']['assets'][V.raw_project_paths(V.PROJECTS[0])['assets']]=row['assets']['sha256']
            value['build']['startup']=write_ref(Path(value['build']['startup']['path']),value['startup'])
            value['build_ref']=write_ref(Path(value['build_ref']['path']),value['build'])
            with self.assertRaisesRegex(V.ContractError,'raw_full_json_only_fixed_true_insertions'):V.locked_project_graphs_contract(**value)

# V8 tests consume the immutable real v7 stage chain. This is historical
# artifact compatibility, never a new readiness or runtime approval.
ACTUAL_V7_NATIVE_FIXTURE_REFS = {
    'inputs': ('.local/temporal-native/root-approvals/TP07-T1-probe-v7-inputs/inputs.json', '1b881d7b10f270faca7bccd703969918799ff90d29de6eef8dda86eff20e8d41'),
    'source': ('.local/temporal-native/verification/TP07-T1-probe-v7-source/source.json', 'cb3012a2750e9e447a35e5e0eaa0de2d3eb9be655e844203c08e6d8d6f5043ab'),
    'native': ('.local/temporal-native/verification/TP07-T1-probe-v7-native/native.json', '7ffa132efe4cf60b22c1aa282ea52c3694245d3f2ca85285f8e1fab635f18076'),
    'build': ('.local/temporal-native/verification/TP07-T1-probe-v7-build/build.json', 'd190a74be2d6eca96659b7c474f442c1e3fd20583ee7884ff1c5f9b989cb2c52'),
    'startup': ('.local/temporal-native/verification/TP07-T1-probe-v7-build/startup.json', 'a7c2e705b871f4f64dfa04c18a70a4d2939a21197f3e72fd2709dba746f5c870'),
}


def actual_v7_complete_native_fixture():
    for root in HERE.parents:
        if not (root/ACTUAL_V7_NATIVE_FIXTURE_REFS['inputs'][0]).is_file():continue
        refs={name:dict(path=str(root/path),sha256=digest) for name,(path,digest) in ACTUAL_V7_NATIVE_FIXTURE_REFS.items()}
        documents={name:V.read_ref(value,root) for name,value in refs.items()}
        inputs=documents['inputs'];source=documents['source'];native=documents['native'];build=documents['build'];startup=documents['startup']
        if source['inputs']!=refs['inputs'] or native['inputs']!=refs['inputs'] or build['inputs']!=refs['inputs']:raise AssertionError('historical_v7_inputs_binding')
        if native['source']!=refs['source'] or build['source']!=refs['source'] or startup['source']!=refs['source']:raise AssertionError('historical_v7_source_binding')
        if build['native']!=refs['native'] or startup['native']!=refs['native'] or build['startup']!=refs['startup']:raise AssertionError('historical_v7_stage_binding')
        if inputs['workspace_root']!=str(root) or source['replay_root']!=startup['replay_root']:raise AssertionError('historical_v7_real_workspace_and_replay')
        for name,digest in source['source_hashes'].items():V.ref(dict(path=str(Path(source['replay_root'])/name),sha256=digest),source['replay_root'])
        return dict(native=native,build=build,startup=startup,inputs=inputs,replay=Path(source['replay_root']),native_ref=refs['native'],build_ref=refs['build'])
    raise AssertionError('immutable_actual_v7_complete_native_fixture_required')


def full_native_with_verified_loaded_lock_mutations(value, mutations, seen):
    """Verify each actual Ref first; mutate only loaded locks, never files.

    All other readers, process receipts, raw graphs, cache archives and actual
    binaries remain real. Physical-boundary tests do not use this adapter.
    """
    original_reader=V.read_ref
    lock_refs={g['lock']['path']:g['lock'] for g in value['native']['project_graphs']}
    if not set(mutations)<=set(lock_refs):raise AssertionError('only_actual_fixed_lock_memory_mutations')
    def verified_reader(reference,root=None):
        document=original_reader(reference,root)
        path=reference['path']
        if path in mutations:
            if reference!=lock_refs[path]:raise AssertionError('unchanged_actual_lock_ref_required')
            seen.append(path)
            return mutations[path](copy.deepcopy(document))
        return document
    with mock.patch.object(V,'read_ref',side_effect=verified_reader):
        return V.native_contract(**value)


class ActualCompleteNativeLockContracts(unittest.TestCase):
    def test_real_v7_full_native_contract_reaches_package_binary_and_runtime_gates(self):
        value=actual_v7_complete_native_fixture()
        self.assertEqual([V.read_ref(g['lock'])['version'] for g in value['native']['project_graphs']],[2,1])
        with mock.patch.object(V,'native_hash_domains_contract',wraps=V.native_hash_domains_contract) as domains, mock.patch.object(V,'archive_origin_contract',wraps=V.archive_origin_contract) as archives, mock.patch.object(V,'binary_contract',wraps=V.binary_contract) as binaries, mock.patch.object(V,'runtime_package_contract',wraps=V.runtime_package_contract) as runtime:
            self.assertIsNone(V.native_contract(**value))
            self.assertEqual(domains.call_count,28)
            self.assertEqual(archives.call_count,28)
            self.assertEqual(binaries.call_count,5)
            self.assertEqual(runtime.call_count,1)
        # No approval is constructed and the historical readiness is untouched.

    def test_full_native_same_refs_reject_swapped_versions_and_wrong_types(self):
        value=actual_v7_complete_native_fixture();paths=[g['lock']['path'] for g in value['native']['project_graphs']]
        cases=[]
        for index,expected in enumerate((2,1)):
            for replacement in (1 if index==0 else 2,0,3,-1,True,False,float(expected),str(expected),None):
                cases.append(('project-'+str(index)+'-'+repr(replacement),{paths[index]:replacement}))
        cases.extend([('both1',{paths[0]:1,paths[1]:1}),('both2',{paths[0]:2,paths[1]:2}),('swapped',{paths[0]:1,paths[1]:2})])
        for label,replacements in cases:
            seen=[]
            def version_mutation(document,replacement):
                document['version']=replacement;return document
            mutations={path:(lambda document,replacement=replacement:version_mutation(document,replacement)) for path,replacement in replacements.items()}
            with self.subTest(mutation=label):
                with self.assertRaisesRegex(V.ContractError,r'^native_lock_version(?:_type)?$'):
                    full_native_with_verified_loaded_lock_mutations(value,mutations,seen)
                self.assertTrue(seen)
                self.assertTrue(set(seen)<=set(replacements))
        self.assertEqual([V.read_ref(g['lock'])['version'] for g in value['native']['project_graphs']],[2,1])

    def test_full_native_same_refs_lock_shape_and_dependency_semantics(self):
        value=actual_v7_complete_native_fixture()
        for graph in value['native']['project_graphs']:
            path=graph['lock']['path'];original=V.read_ref(graph['lock']);cases=[]
            for key in ('version','dependencies'):
                changed=copy.deepcopy(original);del changed[key];cases.append(('missing-'+key,changed,r'^actual_lock_shape$'))
            changed=copy.deepcopy(original);changed['extra_header']=True;cases.append(('extra-header',changed,r'^actual_lock_shape$'))
            for key,replacement in [('resolved','0.0.0'),('contentHash','changed-content-hash')]:
                changed=copy.deepcopy(original);rows=next(iter(changed['dependencies'].values()));package=next(row for row in rows.values() if row['type']!='Project');package[key]=replacement;cases.append(('dependency-'+key,changed,r'^actual_lock_graph'))
            changed=copy.deepcopy(original);next(iter(changed['dependencies'].values())).pop('W.DmProvider');cases.append(('missing-W-dependency',changed,r'^actual_lock_graph'))
            changed=copy.deepcopy(original);next(iter(changed['dependencies'].values()))['Unapproved.Package']=dict(type='Transitive',resolved='1.0.0',contentHash='extra');cases.append(('extra-dependency',changed,r'^actual_lock_graph'))
            if graph['project']==V.PROJECTS[1]:
                changed=copy.deepcopy(original);row=next(row for row in next(iter(changed['dependencies'].values())).values() if row['type']=='Project');row['dependencies']={};cases.append(('provider-Project-dependencies',changed,r'^actual_lock_graph'))
            for label,changed,code in cases:
                seen=[]
                with self.subTest(project=graph['project'],mutation=label):
                    with self.assertRaisesRegex(V.ContractError,code):
                        full_native_with_verified_loaded_lock_mutations(value,{path:lambda document,changed=changed:copy.deepcopy(changed)},seen)
                    self.assertEqual(seen,[path])
                V.ref(graph['lock'],value['replay'])

    def test_full_native_fixed_project_identity_order_and_cross_lock_path(self):
        value=actual_v7_complete_native_fixture()
        for label in ('unknown','reversed','duplicate','cross-lock'):
            bad=copy.deepcopy(value)
            if label=='unknown':bad['native']['project_graphs'][0]['project']='src/Other/Other.csproj'
            elif label=='reversed':bad['native']['project_graphs'].reverse()
            elif label=='duplicate':bad['native']['project_graphs'][1]=copy.deepcopy(bad['native']['project_graphs'][0])
            else:bad['native']['project_graphs'][0]['lock']=copy.deepcopy(bad['native']['project_graphs'][1]['lock'])
            code=r'^raw_native_real_lock_path$' if label=='cross-lock' else r'^raw_snapshot_ordered_project$'
            with self.subTest(mutation=label):
                with self.assertRaisesRegex(V.ContractError,code):V.native_contract(**bad)

    def test_controlled_live_lock_hash_symlink_and_escape_use_physical_checks(self):
        for label in ('byte-drift','missing-file','symlink','escape'):
            with tempfile.TemporaryDirectory() as directory:
                root=Path(directory).resolve();value,_=controlled_raw_stage_fixture(root)
                graph=value['native']['project_graphs'][0];target=Path(graph['lock']['path'])
                if label=='byte-drift':
                    target.write_bytes(target.read_bytes()+b'\n');code=r'^ref_sha_mismatch$'
                elif label=='missing-file':target.unlink();code=r'^missing_file$'
                elif label=='symlink':
                    saved=root/'actual-lock-copy.json';shutil.copyfile(target,saved);target.unlink();target.symlink_to(saved);code=r'^symlink_path$'
                else:
                    outside=root/'outside-lock.json';shutil.copyfile(target,outside);graph['lock']=dict(path=str(outside),sha256=V.sha(outside));code=r'^raw_native_real_lock_path$'
                    with self.assertRaisesRegex(V.ContractError,r'^root_cross$'):V.ref(graph['lock'],value['replay'])
                # Only the bounded raw/live consumer is invoked here; no fake
                # SDK/binary positive is made from this synthetic directory.
                with self.subTest(mutation=label):
                    with self.assertRaisesRegex(V.ContractError,code):V.locked_project_graphs_contract(**value)


# Historical v9 main completed, but its original validator rejected CREATE -1.
# These immutable Ref bytes prove consumer compatibility, never runtime acceptance.
ACTUAL_V9_CASE_FIXTURE_REFS = {
    'report': ('.local/temporal-native/verification/TP07-T1-probe-v9-runtime/00-shared-sync/main/report.json', '20414e0d1176ceb73cce6305395cb012f507ac0947947c8de5a46c81e922589f'),
    'cleanup': ('.local/temporal-native/verification/TP07-T1-probe-v9-runtime/00-shared-sync/cleanup/report.json', 'd6ada506b54a8fa2423ffd7ea9161ea21ab51aaa1eecae90659bac506e4762fc'),
    'ledger': ('.local/temporal-native/verification/TP07-T1-probe-v9-runtime/00-shared-sync/main/ownership.json', '2c21e9950a56b94e2bd9e005f5fbad5f4ece9e65a25523b251bec414c2a3b069'),
    'startup': ('.local/temporal-native/verification/TP07-T1-probe-v9-build/startup.json', 'aca0a286a6aa0f1e9cfc34764a82d35ee6a80b11777a4bbb39dba43236b68322'),
}


def actual_v9_complete_case_fixture():
    """Read original physical Ref bytes before any loaded-document mutations."""
    for root in HERE.parents:
        if not (root/ACTUAL_V9_CASE_FIXTURE_REFS['report'][0]).is_file():continue
        refs={name:dict(path=str(root/path),sha256=digest) for name,(path,digest) in ACTUAL_V9_CASE_FIXTURE_REFS.items()}
        documents={name:V.read_ref(reference,root) for name,reference in refs.items()}
        return dict(documents,startup_sha=refs['startup']['sha256'],ledger_sha=refs['ledger']['sha256'],case_id='T1-shared-sync-pair')
    raise AssertionError('immutable_actual_v9_complete_case_fixture_required')


class ActualCompleteCreateResultContracts(unittest.TestCase):
    def test_real_v9_complete_case_create_minus_one_reaches_all_downstream_gates(self):
        value=actual_v9_complete_case_fixture()
        self.assertEqual([c['result'] for c in value['report']['commands'] if c['stage']=='create'],[-1])
        with mock.patch.object(V,'events_commands_contract',wraps=V.events_commands_contract) as commands, \
             mock.patch.object(V,'ledger_contract',wraps=V.ledger_contract) as ledger, \
             mock.patch.object(V,'catalog_contract',wraps=V.catalog_contract) as catalog, \
             mock.patch.object(V,'values_contract',wraps=V.values_contract) as values, \
             mock.patch.object(V,'cleanup_contract',wraps=V.cleanup_contract) as cleanup:
            result=V.case_contract(**value)
        self.assertTrue(result);self.assertTrue(all(flag is True for flag in result.values()))
        self.assertEqual(commands.call_count,1);self.assertEqual(ledger.call_count,1)
        self.assertEqual(catalog.call_count,3);self.assertEqual(values.call_count,1)
        self.assertEqual(cleanup.call_count,3)

    def test_real_v9_create_other_negatives_and_nonintegers_fail_exact_result_gates(self):
        value=actual_v9_complete_case_fixture()
        for result,code in [(-2,'command_affected_rows'),(-100,'command_affected_rows'),(True,'integer'),(False,'integer'),(-1.0,'non_integer_number'),('-1','integer'),(None,'integer')]:
            bad=copy.deepcopy(value);next(c for c in bad['report']['commands'] if c['stage']=='create')['result']=result
            with self.subTest(result=result,type=type(result).__name__):
                with self.assertRaises(V.ContractError) as error:V.case_contract(**bad)
                self.assertEqual(str(error.exception),code)

    def test_real_v9_every_noncreate_stage_still_rejects_nonquery_minus_one(self):
        value=actual_v9_complete_case_fixture()
        stages=sorted({c['stage'] for c in value['report']['commands'] if c['stage']!='create'})
        self.assertTrue(stages)
        for stage in stages:
            bad=copy.deepcopy(value);command=next(c for c in bad['report']['commands'] if c['stage']==stage)
            command.update(kind='nonquery',api='NonQueryExecuting',result=-1)
            with self.subTest(stage=stage):
                with self.assertRaises(V.ContractError) as error:V.case_contract(**bad)
                self.assertEqual(str(error.exception),'command_affected_rows')

    def test_real_v9_create_minus_one_retains_command_completion_error_API_and_kind_gates(self):
        value=actual_v9_complete_case_fixture()
        mutations=[({'stage':'unknown_stage'},'command_stage'),({'completed':False},'command_success'),({'error':'synthetic_safe_error'},'command_no_hidden_error_type'),({'api':'ReaderExecuting'},'command_API'),({'kind':'reader','api':'ReaderExecuting'},'command_result_safe_type')]
        for changes,code in mutations:
            bad=copy.deepcopy(value);next(c for c in bad['report']['commands'] if c['stage']=='create').update(changes)
            with self.subTest(changes=changes):
                with self.assertRaises(V.ContractError) as error:V.case_contract(**bad)
                self.assertEqual(str(error.exception),code)

    def test_real_v9_create_minus_one_retains_single_CREATE_and_ledger_SQL_binding(self):
        value=actual_v9_complete_case_fixture()
        for mutation,code in [('missing_create','actual_create_seed_command_create'),('command_SQL','CREATE_server_ACK_command_binding'),('ledger_SQL','CREATE_server_ACK_command_binding')]:
            bad=copy.deepcopy(value)
            if mutation=='missing_create':bad['report']['commands']=[c for c in bad['report']['commands'] if c['stage']!='create']
            elif mutation=='command_SQL':next(c for c in bad['report']['commands'] if c['stage']=='create')['sql_sha256']='0'*64
            else:bad['ledger']['objects'][0]['create_evidence']['sql_sha256']='0'*64
            with self.subTest(mutation=mutation):
                with self.assertRaises(V.ContractError) as error:V.case_contract(**bad)
                self.assertEqual(str(error.exception),code)


if __name__=='__main__':
    unittest.main()
