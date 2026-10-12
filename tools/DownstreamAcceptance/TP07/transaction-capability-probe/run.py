#!/usr/bin/env python3
"""Fixed four-process cohort; internal tickets cannot select arbitrary SQL or cases."""
import argparse
import os
from pathlib import Path
import sys
import prepare

HERE=Path(__file__).resolve().parent
DIRECTORIES=('00-shared-sync','01-shared-async','02-tls-sync','03-tls-async')
APPROVAL_KEYS=('schema_version','task','status','inputs','source','native','build','startup','toolpack','offline','readiness','wrappers','ordered_cases','budgets','output_root','original_repo')

def approval(path, expected_sha):
    reference=dict(path=str(Path(path).absolute()),sha256=expected_sha); document=prepare.read(prepare.verify_ref(reference));prepare.exact(document,APPROVAL_KEYS,'approval_schema')
    prepare.need(document['schema_version']==1 and document['task']=='TP07-T1-runtime-approval' and document['status']=='runtime_authorized','root_runtime_approval')
    prepare.need(document['ordered_cases']==[x['case_id'] for x in prepare.policy()['cases']] and document['budgets']==dict(main=180,cleanup=180,validator=180),'finite_cases_budgets')
    inputs=prepare.read(prepare.verify_ref(document['inputs']));inputs=prepare.load_inputs(document['inputs']['path'],document['inputs']['sha256']);root=Path(inputs['workspace_root'])
    for name in ('source','native','build','startup','toolpack','offline','readiness'):prepare.verify_ref(document[name],root)
    prepare.need(document['inputs']==prepare.read(document['build']['path'])['inputs'] and document['toolpack']==inputs['toolpack'],'approval_inputs_toolpack')
    ready=prepare.read(document['readiness']['path']);prepare.need(ready['status']=='ready_pending_runtime' and all(ready[k]==document[k] for k in ('inputs','source','native','build','startup','toolpack','offline')),'approval_readiness_binding')
    prepare.exact(document['wrappers'],('shared','tls'),'wrapper_schema')
    for profile,name in [('shared','with-dameng-test.sh'),('tls','with-dameng-tls-test.sh')]:
        wrapper=prepare.verify_ref(document['wrappers'][profile]);prepare.need(wrapper==Path(document['original_repo']).resolve()/'scripts'/name,'fixed_original_TEST_wrapper')
    prepare.need(document['original_repo']==inputs['original_repo'] and Path(document['output_root']).is_absolute() and Path(document['output_root']).resolve().is_relative_to(root/'.local/temporal-native/verification'),'approved_output_root')
    return reference,document,inputs

def input_snapshot(document,inputs):
    result={}
    for key in ('inputs','source','native','build','startup','toolpack','offline','readiness'):
        path=prepare.verify_ref(document[key]);result[str(path)]=prepare.sha(path)
    startup=prepare.read(document['startup']['path']);replay=Path(startup['replay_root']);prepare.check_source(prepare.read(document['source']['path']))
    for collection in ('source_hashes','locks','assets'):
        for name, expected in startup[collection].items():
            path=prepare.no_symlink_ancestors(replay/name);prepare.need(path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(replay) and prepare.sha(path)==expected,'runtime_input_source_drift');result[str(path)]=expected
    for identity in [startup['host'],startup['ef_provider']]+startup['ef_dependencies']:
        path=prepare.verify_binary(identity);result[str(path)]=identity['sha256']
    deps=prepare.verify_ref(startup['host']['deps']);result[str(deps)]=startup['host']['deps']['sha256']
    w=prepare.no_symlink_ancestors(startup['W']['path']);prepare.need(w.is_file() and not w.is_symlink() and w.resolve().is_relative_to(replay) and prepare.sha(w)==startup['W']['dll_sha256'],'runtime_W_binary_drift');result[str(w)]=startup['W']['dll_sha256']
    build=prepare.read(document['build']['path'])
    for name,expected in build['binaries'].items():
        path=prepare.no_symlink_ancestors(replay/name);prepare.need(path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(replay) and prepare.sha(path)==expected,'runtime_all_binary_drift');result[str(path)]=expected
    for key in ('W_identity','capabilities','probe_source_manifest'):
        path=prepare.verify_ref(inputs[key]);result[str(path)]=inputs[key]['sha256']
    host_manifest=prepare.probe_source_manifest(inputs)
    for name,expected in host_manifest['source_hashes'].items():
        path=prepare.no_symlink_ancestors(Path(host_manifest['source_root'])/name);result[str(path)]=expected
    for executable in ('sdk','python'):
        item=inputs[executable];prepare.need(prepare.sha(item['path'])==item['sha256'],'runtime_executable_drift');result[item['path']]=item['sha256']
    for item in document['wrappers'].values():
        path=prepare.verify_ref(item);result[str(path)]=item['sha256']
    hashes=prepare.toolpack(document['toolpack'],inputs['workspace_root'])
    for name,value in hashes.items():result[str(Path(inputs['workspace_root'])/name)]=value
    return result

def internal_host(a):
    ticket_ref=dict(path=str(Path(a.ticket).absolute()),sha256=a.ticket_sha256);ticket=prepare.read(prepare.verify_ref(ticket_ref));prepare.exact(ticket,('schema_version','task','approval','cohort_id','ordinal','case_id','role','startup','predecessor_validation','output','ledger'),'internal_ticket_shape')
    ar,document,inputs=approval(ticket['approval']['path'],ticket['approval']['sha256']);prepare.need(ticket['approval']==ar and ticket['cohort_id']==ar['sha256'],'internal_ticket_cohort')
    ordinal=ticket['ordinal'];prepare.need(type(ordinal)is int and ordinal in range(4) and ticket['case_id']==document['ordered_cases'][ordinal] and ticket['role'] in ('main','cleanup') and ticket['startup']==document['startup'],'internal_finite_case')
    case_root=Path(document['output_root'])/DIRECTORIES[ordinal];prepare.need(ticket['output']==str(case_root/ticket['role']) and ticket['ledger']==str(case_root/'main/ownership.json') and Path(ticket['output']).exists(),'internal_exact_output_ledger')
    expected_ticket=case_root/(ticket['role']+'-ticket.json');prepare.need(Path(a.ticket).resolve()==expected_ticket,'internal_ticket_path')
    if ordinal:
        pp=prepare.read(prepare.verify_ref(ticket['predecessor_validation']));prepare.need(pp['status']=='passed' and pp['case_id']==document['ordered_cases'][ordinal-1],'internal_predecessor')
    else:prepare.need(ticket['predecessor_validation'] is None,'internal_no_predecessor')
    credential_key='DAMENG_TEST_CONNECTION_STRING' if ordinal<2 else 'DAMENG_TLS_TEST_CONNECTION_STRING'
    prepare.need(credential_key in os.environ and bool(os.environ[credential_key]),'profile_TEST_wrapper_required');input_snapshot(document,inputs)
    startup=prepare.read(document['startup']['path']);environment=prepare.environment(Path(ticket['output']));environment['DAMENG_TEST_CONNECTION_STRING']=os.environ[credential_key]
    environment.update(TP07_CANDIDATE_MANIFEST=inputs['W_identity']['path'],TP07_CAPABILITIES=inputs['capabilities']['path'],TP07_ORIGINAL_REPO=document['original_repo'],TP07_PROFILE='shared' if ordinal<2 else 'tls',TP07_STARTUP_MANIFEST=document['startup']['path'],TP07_STARTUP_MANIFEST_SHA256=document['startup']['sha256'],TP07_T1_INTERNAL_TICKET_SHA256=a.ticket_sha256)
    # exec retains the wrapper process group. Outer deadline kills all descendants.
    os.execve(inputs['sdk']['path'],[inputs['sdk']['path'],startup['host']['path'],'--ticket',ticket_ref['path'],'--ticket-sha256',a.ticket_sha256],environment)

def ledger_trusted(path,case_id,profile):
    value=prepare.read(path);prepare.exact(value,('schema_version','profile','package_sha256','identity_sha256','objects'),'ledger_shape')
    prepare.need(value['schema_version']==1 and value['profile']==profile and len(value['objects'])==1,'exact_one_ledger')
    row=value['objects'][0];nonce=row['nonce'];prepare.need(row['case_id']==case_id and row['shape']=='Basic' and type(nonce)is str and __import__('re').fullmatch('[A-F0-9]{32}',nonce) and row['table']=='TPL_'+nonce[:24] and row['primary_key']=='TP07PK_'+nonce[:24],'exact_ledger_case_nonce')
    # The copied C# cleanup loader additionally enforces full finite structure and ACK state.
    return prepare.ref(path)

def run(a):
    ar,document,inputs=approval(a.approval,a.approval_sha256);prepare.need(str(Path(a.output).absolute())==document['output_root'],'approved_fresh_runtime_root');output=prepare.fresh(a.output,inputs['workspace_root'])
    snapshot=input_snapshot(document,inputs);receipts=[];validations=[];attempted=[];first_failure=None;drift=False;pending=False;predecessor=None
    matrix=dict(schema_version=1,task='TP07-T1-matrix',approval=ar,cohort_id=ar['sha256'],required_cases=document['ordered_cases'],attempted_cases=attempted,case_receipts=receipts,case_validations=validations,first_failure=None,input_drift=False,cleanup_pending=False,status='started',candidate_acceptance=False,full_EF_C_accepted=False)
    prepare.write(output/'matrix.json',matrix)
    for case in prepare.policy()['cases']:
        ordinal=case['ordinal'];case_root=output/DIRECTORIES[ordinal];case_root.mkdir();main=case_root/'main';main.mkdir();cleanup=case_root/'cleanup';cleanup.mkdir();attempted.append(case['case_id']);ledger_path=main/'ownership.json'
        def ticket(role):
            value=dict(schema_version=1,task='TP07-T1-case-ticket',approval=ar,cohort_id=ar['sha256'],ordinal=ordinal,case_id=case['case_id'],role=role,startup=document['startup'],predecessor_validation=predecessor,output=str(case_root/role),ledger=str(ledger_path));path=case_root/(role+'-ticket.json');prepare.write(path,value);return prepare.ref(path)
        before=input_snapshot(document,inputs);main_ticket=ticket('main')
        def host_process(tr):
            argv=[document['wrappers'][case['profile']]['path'],inputs['python']['path'],str(HERE/'run.py'),'_host','--ticket',tr['path'],'--ticket-sha256',tr['sha256']]
            return prepare.process(argv,inputs['workspace_root'],prepare.environment(case_root),180)
        main_process=host_process(main_ticket);prepare.write(main/'process.json',main_process);main_report=prepare.ref(main/'report.json') if (main/'report.json').exists() else None
        ledger=None;cleanup_slot=None
        if ledger_path.exists():
            try:
                ledger=ledger_trusted(ledger_path,case['case_id'],case['profile']);cleanup_ticket=ticket('cleanup');cleanup_process=host_process(cleanup_ticket);prepare.write(cleanup/'process.json',cleanup_process);cleanup_report=prepare.ref(cleanup/'report.json') if (cleanup/'report.json').exists() else None
                cleanup_slot=dict(process=cleanup_process,report=cleanup_report,ticket=cleanup_ticket)
                if not prepare.process_succeeded(cleanup_process) or cleanup_report is None:pending=True
            except Exception:pending=True
        else:
            # Absence of the ledger is not proof of absent DB objects after a partial main.
            pending=True
        try:after=input_snapshot(document,inputs);drift=drift or before!=after or after!=snapshot
        except Exception:after={};drift=True
        execution=dict(schema_version=1,task='TP07-T1-case-execution',approval=ar,cohort_id=ar['sha256'],case_id=case['case_id'],ticket=main_ticket,inputs_before=before,main=dict(process=main_process,report=main_report,ticket=main_ticket),ledger=ledger,independent_cleanup=cleanup_slot,validator=None,inputs_after=after,status='execution_completed_pending_validation')
        execution_path=case_root/'execution-input.json';prepare.write(execution_path,execution);execution_ref=prepare.ref(execution_path)
        validation_path=case_root/'validation.json';argv=[inputs['python']['path'],str(HERE/'validate.py'),'case','--approval',ar['path'],'--approval-sha256',ar['sha256'],'--case-receipt',str(execution_path),'--output',str(validation_path)]
        validation_process=prepare.process(argv,inputs['workspace_root'],prepare.environment(case_root),180);prepare.write(case_root/'validator-process.json',validation_process);validation_ref=prepare.ref(validation_path) if validation_path.exists() else None
        passed=False
        if validation_ref is not None:
            try:v=prepare.read(validation_path);passed=v['status']=='passed' and v['case_id']==case['case_id'] and v['activation_accepted'] is False
            except Exception:passed=False
        passed=passed and prepare.process_succeeded(main_process) and prepare.process_succeeded(validation_process) and cleanup_slot is not None and prepare.process_succeeded(cleanup_slot['process']) and not pending and not drift
        final=dict(execution);final['validator']=dict(execution_input=execution_ref,process=validation_process,report=validation_ref);final['status']='passed' if passed else 'failed';final_path=case_root/'case-receipt.json';prepare.write(final_path,final);receipts.append(prepare.ref(final_path));validations.append(validation_ref)
        if not passed:first_failure=case['case_id'];break
        predecessor=validation_ref
    matrix.update(first_failure=first_failure,input_drift=drift,cleanup_pending=pending,status='completed_pending_review' if first_failure is None and len(attempted)==4 else 'failed');prepare.write(output/'matrix.json',matrix)
    return 0 if matrix['status']=='completed_pending_review' else 1

def main():
    parser=argparse.ArgumentParser();sub=parser.add_subparsers(dest='mode',required=True)
    public=sub.add_parser('run');public.add_argument('--approval',required=True);public.add_argument('--approval-sha256',required=True);public.add_argument('--output',required=True)
    internal=sub.add_parser('_host');internal.add_argument('--ticket',required=True);internal.add_argument('--ticket-sha256',required=True)
    a=parser.parse_args()
    try:return run(a) if a.mode=='run' else internal_host(a)
    except Exception as e:
        print(__import__('json').dumps(dict(status='failed',error_type=type(e).__name__,code=e.args[0] if isinstance(e,prepare.Stop) else 'external_failure')));return 1
if __name__=='__main__':raise SystemExit(main())
