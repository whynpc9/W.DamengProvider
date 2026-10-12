#!/usr/bin/env python3
"""Run only externally approved D builds; preserve pending checkpoints; independent cleanup."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
import types

sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent
module=types.ModuleType('D_finite_validator');module.__file__=str(HERE/'design-validate.py')
exec(compile((HERE/'design-validate.py').read_bytes(),module.__file__,'exec'),module.__dict__)
V=module


def write(path,value):
    path=Path(path);temporary=path.with_suffix(path.suffix+'.tmp');temporary.write_text(json.dumps(value,indent=2,sort_keys=True)+'\n');os.replace(temporary,path)
def ref(path):return dict(path=str(Path(path).resolve()),sha256=V.sha(path))
def fresh(path):
    path=Path(path).resolve();V.need(not path.exists(),'fresh_output');path.mkdir(parents=True);return path
def public_environment(plan,tool,host):
    env={key:os.environ[key] for key in ('HOME','TMPDIR','LANG','LC_ALL') if key in os.environ}
    env['PATH']=str(host.parent)+os.pathsep+os.defpath
    resolved=shutil.which('dotnet',path=env['PATH']);V.need(resolved is not None and Path(resolved).resolve()==host and V.sha(resolved)==plan['dotnet_host_sha256'],'same_approved_dotnet_for_official_descendants')
    env.update(DOTNET_CLI_HOME=tool['cli_home'],DOTNET_ROOT=str(host.parent),DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',
        TP07_CANDIDATE_MANIFEST=plan['candidate_manifest']['path'],TP07_CAPABILITIES=plan['capabilities']['path'],TP07_PROFILE=plan['profile'],
        TP07_ORIGINAL_REPO=plan['original_repo'],TP07_STARTUP_MANIFEST=plan['startup_manifest']['path'],TP07_STARTUP_MANIFEST_SHA256=plan['startup_manifest']['sha256'],
        TP07_CLI_TOOL_MANIFEST=plan['cli_tool_manifest']['path'],TP07_CLI_TOOL_MANIFEST_SHA256=plan['cli_tool_manifest']['sha256'])
    return env,dict(resolved=str(host),sha256=V.sha(host),path=env['PATH'])


def run_process(command,cwd,env,timeout=1620):
    begin=time.monotonic();child=None;timed=False;termination=None;code=None
    try:
        child=subprocess.Popen(command,cwd=cwd,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
        try:code=child.wait(timeout=timeout)
        except subprocess.TimeoutExpired:timed=True;termination='deadline_kill';os.killpg(child.pid,signal.SIGKILL);code=child.wait()
        except BaseException:
            termination='interrupted';os.killpg(child.pid,signal.SIGKILL);child.wait();raise
    finally:
        if child is not None and child.poll() is None:os.killpg(child.pid,signal.SIGKILL);child.wait()
    return dict(command=command,cwd=str(cwd),exit_code=code,timed_out=timed,termination_code=termination,elapsed_seconds=time.monotonic()-begin,
        stdout_discarded=True,stderr_discarded=True)


def host_command(plan,start,consumer,role,ledger=None):
    target=next(row for row in start['startup_assemblies'] if row['name']==('TP07LiteralProbe' if role=='cleanup' else 'TP07TemporalDesign'))
    dll=str(Path(start['replay_root'])/target['path']);host=plan['dotnet_host'];args=[host,'exec',dll]
    if role=='cleanup':
        return args+['--profile',plan['profile'],'--candidate-manifest',plan['candidate_manifest']['path'],'--capabilities',plan['capabilities']['path'],
            '--repo',plan['original_repo'],'--build-manifest',plan['startup_manifest']['path'],'--build-manifest-sha256',plan['startup_manifest']['sha256'],
            '--output',str(consumer),'--cleanup','--ledger',str(ledger)]
    args+=['--profile',plan['profile'],'--group',plan['stage'],'--output',str(consumer)]
    if plan['stage'].startswith('scaffold'):args+=['--replay-root',start['replay_root']]
    if plan['stage']=='scaffold':args+=['--project',str(Path(start['replay_root'])/'test/TP07TemporalDesign/TP07TemporalDesign.csproj')]
    if plan['stage'] in ('scaffold-consume','scaffold-finalize'):
        parent=plan['parent_checkpoint']['report'];args+=['--generation-report',parent['path'],'--generation-report-sha256',parent['sha256']]
    return args


def internal_host(args):
    reference=dict(path=str(Path(args.plan).resolve()),sha256=args.plan_sha256)
    original=V.artifact(reference);identity=V.pack(original['source_pack'])
    plan=dict(original)
    if args.role=='cleanup':plan['startup_manifest']=original['cleanup_startup_manifest'];plan['external_build_receipt']=original['cleanup_build_receipt']
    start=V.startup(plan['startup_manifest'],identity,args.role=='cleanup');V.build_receipt(plan['external_build_receipt'],plan['startup_manifest'],identity)
    tool_root=Path(V.artifact(plan['cli_tool_manifest'])['replay_root'])
    tool,host=V.cleanup_host(plan['cli_tool_manifest']) if args.role=='cleanup' else V.tool_bundle(plan['cli_tool_manifest'],tool_root)
    plan['dotnet_host']=str(host);plan['dotnet_host_sha256']=V.sha(host)
    env,pathproof=public_environment(plan,tool,host)
    credential_key='DAMENG_TLS_TEST_CONNECTION_STRING' if plan['profile']=='tls' else 'DAMENG_TEST_CONNECTION_STRING'
    V.need(bool(os.environ.get(credential_key)),'original_TEST_wrapper_variable_only')
    env['DAMENG_TEST_CONNECTION_STRING']=os.environ[credential_key]  # never argv, logs, files or serialized
    consumer=Path(args.consumer_output).resolve();V.need(not consumer.exists(),'fresh_internal_consumer')
    if args.role=='main':env.update(TP07_PROCESS_RECEIPT=str(consumer.parent/'process-receipt.json'),TP07_CLI_TOOL_REQUIRED='1')
    command=host_command(plan,start,consumer,args.role,args.ledger)
    # Remain in the outer wrapper process group; parent deadline owns all descendants.
    child=subprocess.Popen(command,cwd=start['replay_root'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    code=child.wait();write(consumer.parent/('host-'+args.role+'.json'),dict(schema_version=1,task='TP07-D-internal-host',plan=reference,
        source_pack=identity,PATH_host=pathproof,command=command,child_exit_code=code,stdout_discarded=True,stderr_discarded=True))
    V.pack(original['source_pack']);return code


def wrapped(plan_ref,configuration,start,output,role,ledger=None):
    command=[configuration['wrapper']['path'],configuration['python']['path'],str(Path(__file__).resolve()),'internal-host',
        '--plan',plan_ref['path'],'--plan-sha256',plan_ref['sha256'],'--role',role,'--consumer-output',str(output)]
    if ledger is not None:command+=['--ledger',str(ledger)]
    tool_root=Path(V.artifact(configuration['cli_tool_manifest'])['replay_root'])
    tool,host=V.cleanup_host(configuration['cli_tool_manifest']) if role=='cleanup' else V.tool_bundle(configuration['cli_tool_manifest'],tool_root)
    setup=dict(configuration,dotnet_host_sha256=V.sha(host))
    env,pathproof=public_environment(setup,tool,host)
    return run_process(command,start['replay_root'],env),pathproof


def checkpoint_inputs(configuration,start):
    if configuration['parent_checkpoint'] is None:return None
    parent_ref=configuration['parent_checkpoint'];parent=V.artifact(parent_ref['report']);receipt=V.artifact(parent_ref['execution_receipt'])
    V.need(receipt['report']==parent_ref['report'] and receipt['status']=='pending_external_build','exact_pending_checkpoint_parent')
    ledger=Path(parent['ownership_ledger_active']).resolve()
    V.need(V.sha(ledger)==parent['ownership_ledger_sha256'] and receipt['ledger']['path']==str(ledger),'active_exact_parent_ledger')
    if configuration['stage']!='cleanup':
        old=V.artifact(dict(path=parent['build_manifest_path'],sha256=parent['build_manifest_sha256']))
        V.need(start['parent_report_sha256']==parent_ref['report']['sha256'],'external_child_parent_SHA_approval')
        V.continuation(parent,old,start,Path(start['replay_root']))
    return ledger


def freeze_generated(main,output):
    generation=main['generation'];root=Path(generation['replay_root']);folder=V.inside(root/V.relative(generation['directory']),root)
    V.need(V.generated_hashes(root,folder)==generation['source_hashes'],'generated_source_freeze_before_external_build')
    snapshot=output/'generated-source-snapshot';snapshot.mkdir()
    for name,digest in generation['source_hashes'].items():
        target=snapshot/V.relative(name);target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(root/name,target);V.need(V.sha(target)==digest,'generated_snapshot_copy')
    document=output/'generated-source-snapshot.json';write(document,dict(schema_version=1,task='TP07-D-generated-checkpoint',
        original_replay_root=str(root),snapshot_root=str(snapshot),source_hashes=generation['source_hashes']))
    return ref(document)


def stage(args):
    plan_ref=dict(path=str(Path(args.plan).resolve()),sha256=args.plan_sha256);configuration=V.artifact(plan_ref)
    output=fresh(args.output);execution=dict(schema_version=1,task='TP07-D-execution',plan=plan_ref,candidate_acceptance=False,
        full_temporal_acceptance=False,status='running',independent_cleanup=None)
    ledger=None;identity=None;start=None;process=None;pending=False;exit_code=1
    try:
        configuration,identity,start,tool,host,python=V.plan(plan_ref);execution['source_pack']=identity;execution['source_hashes_before']=identity['source_hashes']
        execution['cli_tool_manifest']=configuration['cli_tool_manifest'];ledger=checkpoint_inputs(configuration,start)
        if configuration['stage']=='cleanup':V.need(ledger is not None,'cleanup_exact_active_ledger')
        consumer=output/'main';process,pathproof=wrapped(plan_ref,configuration,start,consumer,'cleanup' if configuration['stage']=='cleanup' else 'main',ledger)
        execution['process']=process;execution['PATH_host']=pathproof
        if configuration['stage']=='cleanup':
            main=V.load(consumer/'report.json');execution['report']=ref(consumer/'report.json')
        else:
            V.need((consumer/'design-report.json').exists(),'actual_D_report_required');main=V.load(consumer/'design-report.json');execution['report']=ref(consumer/'design-report.json')
            execution['process_receipt']=ref(output/'process-receipt.json')
        if ledger is None:ledger=Path(main.get('ownership_ledger_active',consumer/'ownership.json')).resolve()
        V.need(ledger.exists(),'actual_owned_ledger_required');execution['ledger']=ref(ledger);owned=V.load(ledger);V.ledger(owned)
        V.need(process['exit_code']==0 and not process['timed_out'] and process['termination_code'] is None,'D_stage_natural_success')
        if configuration['stage'] in V.policy()['pending_statuses']:
            V.report_contract(main,owned['objects'],configuration['stage'],configuration['profile'],start,configuration['startup_manifest']['sha256'],
                dict(tool,manifest_sha256=configuration['cli_tool_manifest']['sha256']),V.pure_helpers(identity))
            execution['generated_snapshot']=freeze_generated(main,output)
            execution['ledger_snapshot']=ref(consumer/'ownership-snapshot.json');execution['status']='pending_external_build';pending=True
        else:execution['status']='terminal_complete'
        exit_code=0
    except BaseException as error:
        execution['status']='terminal_failed';execution['error']={'type':type(error).__name__,'code':str(error) if isinstance(error,V.ContractError) else None}
        if ledger is None and configuration.get('parent_checkpoint'):
            try:ledger=Path(V.artifact(configuration['parent_checkpoint']['report'])['ownership_ledger_active']).resolve()
            except (V.ContractError,KeyError,OSError):pass
        if ledger is None and (output/'main/ownership.json').exists():ledger=output/'main/ownership.json'
    finally:
        if identity is not None:
            try:execution['source_hashes_after']=V.pack(configuration['source_pack'])['source_hashes']
            except (V.ContractError,OSError,KeyError) as error:
                execution['source_hashes_after']={};execution['source_drift']=type(error).__name__;execution['status']='terminal_failed';pending=False;exit_code=1
        if not pending and ledger is not None and ledger.exists():
            try:
                if start is None:
                    identity=V.pack(configuration['source_pack']);start=V.startup(configuration['cleanup_startup_manifest'],identity,True)
                cleanup_process,_=wrapped(plan_ref,configuration,start,output/'cleanup','cleanup',ledger)
                cleanup_report=output/'cleanup/report.json';execution['independent_cleanup']=dict(process=cleanup_process,report=ref(cleanup_report))
                cleaned=V.load(cleanup_report);helpers=V.pure_helpers(identity);owned=V.load(ledger)
                V.need(cleanup_process['exit_code']==0 and cleaned['status']=='cleanup_complete','independent_cleanup_failed')
                V.cleanup_proof(cleaned['cleanup'],owned['objects'],helpers,configuration['profile']);V.cleanup_proof(cleaned['finally_cleanup'],owned['objects'],helpers,configuration['profile'])
                execution['ledger']=ref(ledger)
            except BaseException as error:
                execution['cleanup_error']={'type':type(error).__name__,'code':str(error) if isinstance(error,V.ContractError) else None};execution['cleanup_pending']=True;exit_code=1
        execution['exit_code']=exit_code;write(output/'execution-receipt.json',execution)
    return exit_code


def main():
    def interrupted(_signum,_frame):raise KeyboardInterrupt()
    signal.signal(signal.SIGTERM,interrupted)
    parser=argparse.ArgumentParser();parser.add_argument('phase',choices=('stage','internal-host'));parser.add_argument('--plan',required=True);parser.add_argument('--plan-sha256',required=True)
    parser.add_argument('--output');parser.add_argument('--consumer-output');parser.add_argument('--role',choices=('main','cleanup'));parser.add_argument('--ledger')
    args=parser.parse_args()
    if args.phase=='internal-host':V.need(args.role is not None and args.consumer_output is not None,'finite_internal_host_arguments');return internal_host(args)
    V.need(args.output is not None and args.role is None and args.consumer_output is None and args.ledger is None,'finite_stage_arguments');return stage(args)
if __name__=='__main__':raise SystemExit(main())
