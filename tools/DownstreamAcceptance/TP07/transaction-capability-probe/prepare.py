#!/usr/bin/env python3
"""T1 finite file-only preparation and fixed SDK stages. Import performs no I/O."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile
import managed_identity

HERE = Path(__file__).resolve().parent
HOST = 'test/TP07TransactionCapabilityProbe'
PROVIDER = 'src/W.EntityFrameworkCore.Dameng'
class Stop(ValueError): pass
class ProcessCancelled(BaseException):
    def __init__(self,number):self.signal_number=number

def need(value, code):
    if not value: raise Stop(code)
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def pairs(items):
    result = {}
    for key, value in items:
        need(key not in result, 'duplicate_key'); result[key] = value
    return result
def read(path):
    def invalid(_): raise Stop('non_finite_json')
    def floating(_): raise Stop('floating_json_number')
    return json.loads(Path(path).read_text(encoding='utf-8'), object_pairs_hook=pairs, parse_constant=invalid, parse_float=floating)
def write(path, document):
    path = Path(path); temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(document, indent=2, sort_keys=True, allow_nan=False) + '\n', encoding='utf-8'); temporary.replace(path)
def exact(value, keys, code): need(type(value) is dict and set(value) == set(keys), code)
def ref(path):
    path = Path(path).resolve(); return dict(path=str(path), sha256=sha(path))
def no_symlink_ancestors(path):
    path=Path(path).absolute()
    need(not path.is_symlink() and all(not parent.is_symlink() for parent in path.parents), 'symlink_ancestor')
    return path
def verify_ref(value, root=None):
    exact(value, ('path','sha256'), 'finite_ref'); path = no_symlink_ancestors(value['path'])
    need(path.is_absolute() and path.exists() and not path.is_symlink() and re.fullmatch('[a-f0-9]{64}', value['sha256']) is not None, 'ref_shape')
    if root is not None: need(path.resolve().is_relative_to(Path(root).resolve()), 'ref_root_escape')
    need(sha(path) == value['sha256'], 'ref_hash'); return path.resolve()
def safe_relative(name):
    need(type(name) is str and name and not Path(name).is_absolute() and '\\' not in name and all(x not in ('.','..','.local','secrets','bin','obj') for x in name.split('/')), 'source_path')
    return name
def inventory(root, paths):
    result = {}
    for name in paths:
        safe_relative(name); path = no_symlink_ancestors(Path(root) / name)
        need(path.is_file() and not path.is_symlink() and path.resolve().is_relative_to(Path(root).resolve()), 'source_escape'); result[name] = sha(path)
    return result
def policy(): return read(HERE / 'probe-policy.json')
def toolpack(reference, workspace):
    document = read(verify_ref(reference, workspace)); exact(document, ('schema_version','task','source_hashes'), 'toolpack_schema')
    need(type(document['schema_version']) is int and document['schema_version'] == 1 and document['task'] == 'TP07-T1-toolpack', 'toolpack_task')
    required = ['tools/DownstreamAcceptance/TP07/transaction-capability-probe/' + x for x in policy()['source_files']]
    need(set(document['source_hashes']) == set(required) and len(required) == 17, 'toolpack_exact_17')
    need(inventory(workspace, required) == document['source_hashes'], 'toolpack_drift'); return document['source_hashes']
def load_inputs(path, expected_sha):
    document = read(verify_ref(dict(path=str(Path(path).absolute()),sha256=expected_sha)))
    exact(document, ('schema_version','task','workspace_root','original_repo','sdk','python','W_identity','capabilities','ef_overlay','toolpack','helper_origins','expected_closure','probe_source_manifest'), 'inputs_schema')
    need(type(document['schema_version']) is int and document['schema_version'] == 1 and document['task'] == 'TP07-T1-inputs', 'inputs_task')
    root = Path(document['workspace_root']); need(root.is_absolute() and root.resolve() == HERE.parents[3], 'explicit_workspace_root')
    pack_hashes=toolpack(document['toolpack'], root)
    probe_source_manifest(document,pack_hashes)
    need(set(document['helper_origins']) == set(policy()['helper_origins']), 'finite_helper_origins')
    for dst, origin in policy()['helper_origins'].items():
        need(document['helper_origins'].get(dst) == origin and sha(HERE / dst) == origin['sha256'] and sha(root / origin['path']) == origin['sha256'], 'helper_byte_provenance')
    for field, version in (('sdk','10.0.401'), ('python',None)):
        exact(document[field], ('path','sha256','version'), 'executable_identity'); need(Path(document[field]['path']).is_absolute() and sha(document[field]['path']) == document[field]['sha256'], 'executable_pin')
        if version: need(document[field]['version'] == version, 'sdk_pin')
    verify_ref(document['W_identity']); verify_ref(document['capabilities'])
    return document


def probe_source_manifest(inputs, pack_hashes=None):
    root=no_symlink_ancestors(inputs['workspace_root'])
    reference=inputs['probe_source_manifest']
    exact(reference, ('path','sha256'), 'probe_source_ref_schema')
    need(type(reference['path']) is str and Path(reference['path']).is_absolute(), 'absolute_probe_source_ref')
    document=read(verify_ref(reference,root))
    exact(document, ('schema_version','task','source_root','source_hashes','source_count','source_aggregate_sha256','aggregate_format'), 'probe_source_manifest_schema')
    need(type(document['schema_version']) is int and document['schema_version']==1 and document['task']=='TP07-T1-probe-source-manifest', 'probe_source_manifest_task')
    need(type(document['source_count']) is int and document['source_count']==9, 'probe_source_exact9_count')
    need(type(document['source_root']) is str and Path(document['source_root']).is_absolute(), 'absolute_probe_source_snapshot')
    snapshot=no_symlink_ancestors(document['source_root'])
    need(snapshot.is_dir() and snapshot.resolve().is_relative_to(root/'.local/temporal-native/verification') and snapshot.resolve()!=HERE, 'controlled_probe_source_snapshot')
    required=sorted(name for name in policy()['source_files'] if name.startswith('host/'))
    hashes=document['source_hashes']
    need(type(hashes) is dict and sorted(hashes)==required and len(hashes)==9 and all(type(value) is str and re.fullmatch('[a-f0-9]{64}',value) for value in hashes.values()), 'probe_source_exact9_paths_hashes')
    actual_host_paths={str(path.relative_to(snapshot)) for path in (snapshot/'host').rglob('*') if path.is_file() or path.is_symlink()}
    need(actual_host_paths==set(required) and inventory(snapshot,required)==hashes, 'probe_source_snapshot_exact9')
    aggregate=''.join(hashes[name]+'  '+name+'\n' for name in required).encode('utf-8')
    need(document['aggregate_format']=='sorted relative source paths: sha256 two spaces path newline' and type(document['source_aggregate_sha256']) is str and document['source_aggregate_sha256']==hashlib.sha256(aggregate).hexdigest(), 'probe_source_aggregate')
    if pack_hashes is None:pack_hashes=toolpack(inputs['toolpack'],root)
    prefix='tools/DownstreamAcceptance/TP07/transaction-capability-probe/'
    need(hashes=={name:pack_hashes[prefix+name] for name in required}, 'probe_source_same_toolpack9')
    expected_host=sorted(HOST+'/'+name.removeprefix('host/') for name in required)
    need(inputs['expected_closure']['host_paths']==expected_host, 'probe_source_same_expected_paths')
    need(inputs['ef_overlay']['lineage']['probe_source_manifest_sha256']==reference['sha256'], 'probe_source_lineage_ref')
    return document

def fresh(path, workspace):
    path = no_symlink_ancestors(path); parent = Path(workspace) / '.local/temporal-native/verification'
    need(path.resolve().is_relative_to(parent.resolve()) and not path.exists(), 'fresh_allowed_output'); path.mkdir(parents=True); return path

def environment(stage):
    result = {key:os.environ[key] for key in ('PATH','HOME','TMPDIR','LANG','LC_ALL','DOTNET_ROOT') if key in os.environ}
    result.update(DOTNET_CLI_HOME=str(stage/'cli-home'),DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',NUGET_PACKAGES=str(stage/'nuget-packages'),NUGET_HTTP_CACHE_PATH=str(stage/'http-cache'),PYTHONDONTWRITEBYTECODE='1')
    return result

def process(argv, cwd, env, timeout):
    start=time.monotonic_ns();deadline=start+timeout*1000000000
    child=None;timeout_hit=False;termination=None;signal_number=None;reap_ms=0
    original_handlers={};launching=False;finalizing=False;pending_signal=None
    def cancelled(number,_frame):
        nonlocal pending_signal
        if launching or finalizing:
            if pending_signal is None:pending_signal=number
            return
        raise ProcessCancelled(number)
    try:
        for number in (signal.SIGINT,signal.SIGTERM):
            original_handlers[number]=signal.getsignal(number);signal.signal(number,cancelled)
        # Deferring a signal during Popen ensures a launched child is assigned
        # before cancellation enters finally; no child receives a new signal mask.
        launching=True
        child=subprocess.Popen(argv,cwd=cwd,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,start_new_session=True)
        launching=False
        if pending_signal is not None:raise ProcessCancelled(pending_signal)
        remaining=(deadline-time.monotonic_ns())/1000000000
        if remaining<=0:raise subprocess.TimeoutExpired(argv,timeout)
        child.wait(timeout=remaining)
    except subprocess.TimeoutExpired:
        timeout_hit=True;termination='deadline_kill'
    except ProcessCancelled as error:
        signal_number=error.signal_number;termination='signal_kill' if child is not None else 'launch_failed'
    except KeyboardInterrupt:
        signal_number=int(signal.SIGINT);termination='interrupt_kill' if child is not None else 'launch_failed'
    except BaseException:
        termination='exception_kill' if child is not None else 'launch_failed'
    finally:
        finalizing=True;launching=False
        try:
            if child is not None and (termination is not None or child.poll() is None):
                if termination is None:termination='exception_kill'
                reap_started=time.monotonic_ns();reap_deadline=reap_started+2000000000
                try:
                    try:os.killpg(child.pid,signal.SIGKILL)
                    except ProcessLookupError:pass
                    remaining=max(0,(reap_deadline-time.monotonic_ns())/1000000000)
                    try:child.wait(timeout=remaining)
                    except BaseException:pass
                except BaseException:pass
                finally:reap_ms=(time.monotonic_ns()-reap_started)//1000000
                if child.poll() is None:termination+='_reap_unknown'
            if signal_number is None and pending_signal is not None:signal_number=pending_signal
        finally:
            for number,previous in original_handlers.items():
                try:signal.signal(number,previous)
                except BaseException:pass
    exit_code=child.poll() if child is not None else None
    return dict(argv=argv,cwd=str(cwd),exit_code=exit_code,elapsed_ms=(time.monotonic_ns()-start)//1000000,timed_out=timeout_hit,termination=termination,execution_deadline=timeout,termination_reap_budget=2,termination_reap_ms=reap_ms,signal_number=signal_number)

def process_succeeded(value):
    return value['exit_code']==0 and value['timed_out'] is False and value['termination'] is None and value['signal_number'] is None and value['termination_reap_ms']==0

def phase_ref(a, key, task, inputs):
    reference = dict(path=str(Path(getattr(a,key)).absolute()),sha256=getattr(a,key+'_sha256'))
    document = read(verify_ref(reference, inputs['workspace_root']))
    need(document['schema_version']==1 and document['task']==task and document['inputs']==ref(a.inputs), 'phase_binding');return reference,document

# V5 accepts this one frozen product revision. Paths are resolved against the
# explicit workspace; these constants are not runtime or SDK evidence.
PRODUCT_V3_SHA = '400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8'
PRODUCT_V3_PATH = 'tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json'
SOURCE_APPROVAL_V3_PATH = '.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json'
SOURCE_APPROVAL_V3_SHA = '66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f'
SOURCE_SCOPE_V3 = 'transaction_capability_and_w_retry_contract_tests_only'
RETRY_PATH = 'test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs'
TRANSACTION_PATH = 'test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs'
RETRY_CLASS = 'W.EntityFrameworkCore.Dameng.Tests.DamengExecutionStrategyTests'
TRANSACTION_CLASS = 'W.EntityFrameworkCore.Dameng.Tests.DamengRelationalTransactionCapabilityTests'
PRODUCT_V3_FIXED = {'EF_relational_API': {'dll_sha256': 'a1e4f36208650b15b75a4b01a5821d61d5be417ac2e86ec3c65e2560a87e0d7d',
                       'inspection': 'actual package XML and ECMA-335 MethodDef flags, without loading '
                                     'assemblies',
                       'version': '10.0.12'},
 'SDK_executed': False,
 'W': {'dll_sha256': '843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980',
       'id': 'W.DmProvider',
       'mvid': 'aa23e673-957c-4b1e-bf0f-291ff60077e6',
       'package_sha256': 'da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d',
       'version': '0.1.0-temporal.20261008144915'},
 'added_files': ['src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransaction.cs',
                 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransactionFactory.cs',
                 'test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs'],
 'aggregate_format': 'sorted relative source paths: sha256 two spaces path newline',
 'base_approved_source': {'path': '.local/temporal-native/verification/TP07-L0-tools-v3/approved-EF-source-manifest.json',
                          'sha256': 'dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6'},
 'base_lineage': {'approved_ef_source_manifest_sha256': 'dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6',
                  'base_archive_sha256': '668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8',
                  'bridge_patch_sha256': '08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5',
                  'ef_commit': '5863d3755dbd2d333ac359bf1c0a789762adbaed',
                  'mapping_manifest_sha256': '23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c',
                  'mapping_patch_sha256': '4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc'},
 'base_lineage_artifacts': {'base_archive': {'path': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/base.tar',
                                             'sha256': '668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8'},
                            'bridge_manifest': {'path': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/bridge-manifest.json',
                                                'sha256': '4750a330f2f1e63a5e40780acf19820b38b86db2c1bce08ff224600a18b8b8b4'},
                            'bridge_patch': {'path': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/driver-bridge.patch',
                                             'sha256': '08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5'},
                            'mapping_manifest': {'path': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/mapping-manifest.json',
                                                 'sha256': '23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c'},
                            'mapping_patch': {'path': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/temporal-mapping.patch',
                                              'sha256': '4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc'}},
 'base_prepared_source': {'path': '.local/temporal-native/verification/TP07-E-combined-v5-prepare/source-manifest.json',
                          'sha256': '9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a'},
 'base_source_count': 175,
 'database_executed': False,
 'modified_files': ['src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs',
                    'test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs'],
 'namespace_boundary': {'W_product_or_package_modified': False,
                        'added_product': 'W.EntityFrameworkCore.Dameng.Storage.Internal',
                        'added_unit_tests': 'W.EntityFrameworkCore.Dameng.Tests',
                        'official_Dameng_provider_copied_or_modified': False,
                        'registration_only': 'Microsoft.Extensions.DependencyInjection'},
 'old_L0_E_D_cohorts_reused_as_new_acceptance': False,
 'old_parent_source_approval_reused_for_revision': False,
 'patch_application_base': 'exact approved175 EF sources, already including the original bridge and temporal '
                           'mapping',
 'patch_paths': ['src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs',
                 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransaction.cs',
                 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransactionFactory.cs',
                 'test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs',
                 'test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs'],
 'planned_full_unit_cases': 824,
 'planned_retry_unit_cases': 49,
 'planned_retry_unit_methods': 21,
 'planned_transaction_unit_cases': 25,
 'planned_unit_cases': 25,
 'planned_unit_methods': 4,
 'required_next_gates': ['Astra v3 complete178/test-only contract and full five-path patch source review',
                         'Low new immutable178 snapshot and independent full patch reconstruction from '
                         'approved175',
                         'root new source-only seven-field approval bound to v3 manifest and '
                         'transaction_capability_and_w_retry_contract_tests_only scope',
                         'new reviewed preparation revision with fixed v3 references and the single exact new '
                         'source-scope guard',
                         'Low new source/native/locked build and source/binary/raw4/lock identity verification',
                         'Low actual retry49 TRX, original transaction25 TRX, and same-build unfiltered '
                         'full824 TRX with complete declared case-set readback',
                         'new probe tool/product manifest compatibility revision and new '
                         'source/native/build/compiled28/readiness before any separately approved four-case '
                         'runtime'],
 'required_source_approval_scope': 'transaction_capability_and_w_retry_contract_tests_only',
 'revision': 3,
 'schema_version': 1,
 'source_aggregate_sha256': '2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c',
 'source_count': 178,
 'source_root': '.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source',
 'static_checks_only': True,
 'status': 'source_frozen_verification_pending',
 'task': 'TP07-T1 transaction-capability and W retry-contract test isolated EF overlay v3',
 'tests_executed': False,
 'transaction_capability_patch': {'path': 'tools/DownstreamAcceptance/TP07/patches/transaction-capability.v3.patch',
                                  'sha256': 'd13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f'},
 'transaction_probe_or_E_observation_implemented': False,
 'unchanged_base_file_count': 173,
 'unit_delta_from_parent': {'W_package_changed': False,
                            'changed_files': ['test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs'],
                            'global_analyzer_or_build_rules_changed': False,
                            'new_unit_sha256': 'cb7b6114490fc1fd34fc3c839e2fdc24d1eaeffdf8ed8b5ac6f81215e9fbfe2f',
                            'parent_unit_sha256': '82caac3eca1dde1f3823ae3560d4f0ebb0c9388f18dc2380ad75cfdd9cf23285',
                            'private_failure_classification_injected': False,
                            'production_retry_logic_changed': False,
                            'transaction_unit25_sha256': 'f4f689c8c2285ed4665880d65f0ccbd22424862dc1ac9eef1fc0cc69b44dad1d',
                            'unchanged_source_count': 177},
 'unit_revision_parent': {'full_failed_TRX': {'path': '.local/temporal-native/verification/TP07-T1-product-v3-full/results/full.trx',
                                              'sha256': 'f470a92ddb772627a311bf6bd9e2966826c4f960520c63f9c3aa5e375b009271'},
                          'full_failed_manifest': {'path': '.local/temporal-native/verification/TP07-T1-product-v3-full/manifest.json',
                                                   'sha256': '61234feda70debd44fa17f290f376d0895abf94d18114649c5eded4f77daebd4'},
                          'full_failure_triage': {'path': 'docs/implementation/temporal-native/TP07-T1-product-full-failure-triage-v1.md',
                                                  'sha256': 'a97e8c4b48b0cf5003a24611fcb031505be4f88dfdcb3c3bd8f4547b8f063953'},
                          'full_independent_readback': {'path': '.local/temporal-native/verification/TP07-T1-product-v3-full-control/independent-readback.json',
                                                        'sha256': 'f4fa0be7bd222b7061a91912fc3c36877606d7818a56ef90636f7c53b6bbbc5a'},
                          'product_manifest': {'path': 'tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v2.json',
                                               'sha256': 'fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432'},
                          'safe_failed_reasons': {'path': '.local/temporal-native/verification/TP07-T1-product-v3-full-control/safe-failed-reasons.json',
                                                  'sha256': 'cac4a3fb4b962f14d3723af9e28c9765c6894d27269d2aba9348e054c4dc22ed'},
                          'source_approval': {'path': '.local/temporal-native/root-approvals/TP07-T1-product-source-v2/source-approval.json',
                                              'sha256': 'd6cff0a1a92a1b8341b12c6dfaa352028039d8908fffd1b1387254561c3f0486'},
                          'source_review': {'path': 'docs/implementation/temporal-native/TP07-T1-product-source-review-v2.md',
                                            'sha256': 'eb47339baa20c4bbedf2be7ef644b3b0c2a343a0f00d191745cc24e2e53ebbf5'},
                          'sourcefreeze_manifest': {'path': '.local/temporal-native/verification/TP07-T1-product-v2-unitfix-sourcefreeze/source-manifest.json',
                                                    'sha256': 'a842630f569b6dfbeebaa464133ab4bc17cf42f2166691be337b7ac9483f1236'},
                          'transaction_capability_patch': {'path': 'tools/DownstreamAcceptance/TP07/patches/transaction-capability.v2.patch',
                                                           'sha256': '5df9c73a4879fbf7f2cb79da48379a92856e093fecea7df3c31956080b1f3566'}}}

RETRY_V3_METHODS = [{'method': 'EnableRetryOnFailureUsesDefaultDamengStrategy',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'EnableRetryOnFailureAppliesCustomSettingsAndCopiesEmptyErrorNumbers',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'StrategyDoesNotRetryUnclassifiedWServerNumbers',
  'attribute': 'Theory',
  'parameters': ['errorNumber'],
  'inline_data': [[-3003], [-3404], [-6003], [-6004], [-6010], [6001], [6027], [6060], [6089], [6123]]},
 {'method': 'StrategyDoesNotRetryNonTransientOrAmbiguousDamengCodes',
  'attribute': 'Theory',
  'parameters': ['errorNumber'],
  'inline_data': [[-2007], [-1040], [-1210], [-3002], [-6011]]},
 {'method': 'StrategyDoesNotRetryNonDamengTimeoutException',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'StrategyRejectsAdditionalErrorNumberBeforeExecutingDelegate',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'CollectionOverloadRejectsNull', 'attribute': 'Fact', 'parameters': [], 'inline_data': []},
 {'method': 'EnableRetryOnFailureMaxRetryCountPreservesDefaultDelay',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'EnableRetryOnFailureAcceptsNullAdditionalErrorNumbers',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'CollectionOverloadCopiesEmptyCollectionBeforeCallerMutation',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'BuilderCollectionOverloadRejectsNonemptyOverrides',
  'attribute': 'Theory',
  'parameters': ['errorNumber'],
  'inline_data': [[-12345], [-54321], [-77777]]},
 {'method': 'BuilderEnumerableOverloadRejectsNonemptyOverrides',
  'attribute': 'Theory',
  'parameters': ['errorNumber'],
  'inline_data': [[-12345], [-54321], [-77777]]},
 {'method': 'ContextConstructorRejectsNonemptyOverrides',
  'attribute': 'Theory',
  'parameters': ['errorNumber'],
  'inline_data': [[-12345], [-54321]]},
 {'method': 'DependenciesConstructorsRejectNonemptyOverrides',
  'attribute': 'Theory',
  'parameters': ['customSettings', 'errorNumber'],
  'inline_data': [[False, -12345],
                  [False, -54321],
                  [False, -77777],
                  [True, -12345],
                  [True, -54321],
                  [True, -77777]]},
 {'method': 'ContextConstructorAcceptsNullOrCopiesEmptyOverrides',
  'attribute': 'Theory',
  'parameters': ['empty'],
  'inline_data': [[False], [True]]},
 {'method': 'DependenciesConstructorsAcceptNullOrCopyEmptyOverrides',
  'attribute': 'Theory',
  'parameters': ['customSettings', 'empty'],
  'inline_data': [[False, False], [False, True], [True, False], [True, True]]},
 {'method': 'ContextAndDependenciesConstructorsPreserveDefaultAndCustomRetryCounts',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'SyntheticPublicWClassificationControlsActualRetryAttempts',
  'attribute': 'Theory',
  'parameters': ['transient'],
  'inline_data': [[False], [True]]},
 {'method': 'NonWDbExceptionCannotEnableRetriesEvenWhenTransient',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'ActualSealedCommitUnknownExceptionIsPropagatedWithoutRetry',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []},
 {'method': 'ActualSealedWTimeoutExceptionIsPropagatedWithoutRetry',
  'attribute': 'Fact',
  'parameters': [],
  'inline_data': []}]

RETRY_V3_FIXED = {'inventory_kind': 'static source declaration and InlineData inventory; not SDK discovery or execution',
 'original_class_case_count': 20,
 'planned_case_count': 49,
 'reflection_fixture_kind': 'exact actual nonpublic constructor signatures only; no private flags or fallback',
 'synthetic_true_classification_is_transport_evidence': False}

FULL_V3_FIXED = {'added_planned_case_count': 29,
 'baseline_count': 795,
 'baseline_rows': {'path': '.local/temporal-native/verification/TP07-T1-product-v3-full-control/actual-full-rows.json',
                   'sha256': '83a36972c1d668de0d471579c1f1d0f65599fec481c7ba682fa980ed5574fd5b'},
 'inventory_kind': 'actual failed baseline TRX row set plus declared one-to-one test adaptations and static '
                   'new cases; pending independent SDK discovery/TRX',
 'non_retry_baseline_count': 775,
 'original_retry_class_count': 20,
 'planned_full_case_count': 824,
 'requires_actual_total_executed_passed': 824,
 'requires_complete_exact_case_set': True,
 'retained_baseline_case_count': 795,
 'skip_or_abort_allowed': False,
 'transaction25_unchanged': True}

def product_exact(actual, expected, code):
    need(type(actual) is type(expected), code + '_type')
    if type(expected) is dict:
        exact(actual, expected, code)
        for key in expected: product_exact(actual[key], expected[key], code + '_' + key)
    elif type(expected) is list:
        need(len(actual) == len(expected), code + '_length')
        for actual_item, expected_item in zip(actual, expected): product_exact(actual_item, expected_item, code)
    else: need(actual == expected, code)


def product_fixed(root):
    # Resolve only path-valued fields; other literal strings remain unchanged.
    import copy
    fixed = copy.deepcopy(PRODUCT_V3_FIXED)
    fixed['source_root'] = str(Path(root) / fixed['source_root'])
    for key in ('base_prepared_source','base_approved_source','transaction_capability_patch'):
        fixed[key]['path'] = str(Path(root) / fixed[key]['path'])
    for key in ('base_lineage_artifacts','unit_revision_parent'):
        for reference in fixed[key].values(): reference['path'] = str(Path(root) / reference['path'])
    return fixed


def product_map(value, count, code):
    need(type(value) is dict and len(value) == count, code)
    for name, digest in value.items():
        safe_relative(name)
        need(all(name.split('/')), code + '_canonical_path')
        need(type(digest) is str and re.fullmatch('[a-f0-9]{64}', digest), code + '_hash')
    return value


def product_parent_contract(value, parent, frozen, approval):
    # Bounded historical readback: no historical tool is imported or executed.
    exact(parent, ('EF_relational_API', 'SDK_executed', 'W', 'added_files', 'aggregate_format', 'base_approved_source', 'base_lineage', 'base_lineage_artifacts', 'base_prepared_source', 'base_source_count', 'base_source_hashes', 'database_executed', 'modified_files', 'namespace_boundary', 'old_L0_E_D_cohorts_reused_as_new_acceptance', 'old_parent_source_approval_reused_for_revision', 'patch_application_base', 'patch_paths', 'planned_unit_cases', 'planned_unit_methods', 'required_next_gates', 'revision', 'schema_version', 'source_aggregate_sha256', 'source_count', 'source_hashes', 'source_root', 'static_checks_only', 'status', 'task', 'tests_executed', 'transaction_capability_patch', 'transaction_probe_or_E_observation_implemented', 'unchanged_base_file_count', 'unit_delta_from_parent', 'unit_revision_parent'), 'parent_manifest36')
    exact(frozen, ('DB_executed','SDK_executed','patch_sha256','product_manifest_ref','schema_version','source178_delta_from_parent','source_aggregate_sha256','source_count','source_hashes','task','tests_executed'), 'parent_freeze_shape')
    product_exact(parent['source_count'], 178, 'parent_source_count')
    product_exact(parent['base_source_hashes'], value['base_source_hashes'], 'parent_original_base175')
    product_exact(frozen['schema_version'], 1, 'parent_freeze_schema')
    product_exact(frozen['DB_executed'], False, 'parent_freeze_not_executed')
    product_exact(frozen['SDK_executed'], False, 'parent_freeze_not_executed')
    product_exact(frozen['tests_executed'], False, 'parent_freeze_not_executed')
    product_exact(parent['revision'], 2, 'parent_revision')
    product_exact(parent['task'], 'TP07-T1 transaction-capability isolated EF overlay v2', 'parent_task')
    old = product_map(parent['source_hashes'], 178, 'parent_map178')
    new = product_map(value['source_hashes'], 178, 'current_map178')
    product_exact(frozen['source_count'], 178, 'parent_freeze_count')
    product_exact(frozen['source_hashes'], old, 'parent_freeze_map')
    product_exact(frozen['source_aggregate_sha256'], parent['source_aggregate_sha256'], 'parent_freeze_aggregate')
    product_exact(frozen['product_manifest_ref'], value['unit_revision_parent']['product_manifest'], 'parent_freeze_manifest')
    product_exact(parent['transaction_capability_patch'], value['unit_revision_parent']['transaction_capability_patch'], 'parent_patch')
    product_exact(frozen['patch_sha256'], parent['transaction_capability_patch']['sha256'], 'parent_freeze_patch')
    product_exact(approval, dict(schema_version=1, task='TP07-T1-overlay-source-approval', status='source_approved', manifest=value['unit_revision_parent']['product_manifest'], exact_paths=sorted(old), exact_count=178, scope='transaction_capability_overlay_only'), 'parent_source_approval')
    product_exact(sorted(old), sorted(new), 'parent_same_paths')
    changed = sorted(name for name in old if old[name] != new[name])
    product_exact(changed, [RETRY_PATH], 'parent_single_retry_delta')
    need(sum(old[name] == new[name] for name in old) == 177, 'parent_177_unchanged')
    delta = value['unit_delta_from_parent']
    product_exact(old[RETRY_PATH], delta['parent_unit_sha256'], 'parent_retry_hash')
    product_exact(new[RETRY_PATH], delta['new_unit_sha256'], 'current_retry_hash')
    product_exact(old[TRANSACTION_PATH], delta['transaction_unit25_sha256'], 'parent_transaction_hash')
    product_exact(new[TRANSACTION_PATH], old[TRANSACTION_PATH], 'transaction25_source_unchanged')


def product_baseline_rows_contract(rows, root):
    need(type(rows) is list and len(rows) == 795, 'baseline_exact795_rows')
    names = set(); executions = set()
    for row in rows:
        exact(row, ('execution_id','method','outcome','test_name'), 'baseline_row')
        exact(row['method'], ('adapterTypeName','className','codeBase','name'), 'baseline_method')
        need(all(type(x) is str and x for x in row['method'].values()), 'baseline_method_strings')
        product_exact(row['method']['adapterTypeName'], 'executor://xunit/VsTestRunner3/netcore/', 'baseline_adapter')
        product_exact(row['method']['codeBase'], str(Path(root)/'.local/temporal-native/verification/TP07-T1-product-v3-build/work/test/W.EntityFrameworkCore.Dameng.Tests/bin/Debug/net10.0/W.EntityFrameworkCore.Dameng.Tests.dll'), 'baseline_codebase')
        name = row['test_name']; eid = row['execution_id']
        need(type(name) is str and name and name not in names, 'baseline_unique_name')
        need(type(eid) is str and re.fullmatch('[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}', eid) and eid not in executions, 'baseline_unique_execution')
        need(type(row['outcome']) is str and row['outcome'] in ('Passed','Failed'), 'baseline_outcome')
        need((name == row['method']['className']+'.'+row['method']['name'] or name.startswith(row['method']['className']+'.'+row['method']['name']+'(')), 'baseline_name_method')
        names.add(name); executions.add(eid)
    need(sum(row['outcome'] == 'Failed' for row in rows) == 12 and sum(row['outcome'] == 'Passed' for row in rows) == 783, 'baseline_historical_failure_counts')
    return rows


def product_retry_inventory_contract(value, rows):
    inv = value['retry_contract_inventory']
    exact(inv, tuple(RETRY_V3_FIXED) + ('planned_methods','new29_planned_test_names','original20_to_new_mapping','original12_failed_to_new_mapping'), 'retry_inventory')
    for key, expected in RETRY_V3_FIXED.items(): product_exact(inv[key], expected, 'retry_' + key)
    need(type(inv['planned_methods']) is list and len(inv['planned_methods']) == 21, 'retry_exact21_methods')
    expanded = []
    for actual, declaration in zip(inv['planned_methods'], RETRY_V3_METHODS):
        exact(actual, ('method','attribute','parameters','inline_data','planned_count','planned_test_names'), 'retry_method')
        for key, expected in declaration.items(): product_exact(actual[key], expected, 'retry_declaration_' + key)
        prefix = RETRY_CLASS + '.' + declaration['method']
        names = [prefix] if declaration['attribute'] == 'Fact' else [prefix + '(' + ', '.join(param + ': ' + str(item) for param,item in zip(declaration['parameters'], data)) + ')' for data in declaration['inline_data']]
        product_exact(actual['planned_count'], len(names), 'retry_method_count')
        product_exact(actual['planned_test_names'], names, 'retry_exact_rendered_names')
        expanded.extend(names)
    need(len(expanded) == len(set(expanded)) == 49, 'retry_unique49')
    original = [row for row in rows if row['method']['className'] == RETRY_CLASS]
    need(len(original) == 20 and sum(row['outcome']=='Failed' for row in original)==12, 'retry_historical20_failed12')
    mapping = []
    for row in original:
        old = row['test_name']; new = old
        new = new.replace('.StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(', '.StrategyDoesNotRetryUnclassifiedWServerNumbers(')
        if old == RETRY_CLASS+'.StrategyRetriesAnAdditionalErrorNumber': new=RETRY_CLASS+'.StrategyRejectsAdditionalErrorNumberBeforeExecutingDelegate'
        if old == RETRY_CLASS+'.EnableRetryOnFailureAppliesCustomSettingsAndCopiesErrorNumbers': new=RETRY_CLASS+'.EnableRetryOnFailureAppliesCustomSettingsAndCopiesEmptyErrorNumbers'
        mapping.append(dict(original_test_name=old, original_outcome=row['outcome'], new_planned_test_name=new))
    product_exact(inv['original20_to_new_mapping'], mapping, 'retry_one_to_one_original20')
    product_exact(inv['original12_failed_to_new_mapping'], [row for row in mapping if row['original_outcome']=='Failed'], 'retry_preserve_original12_failures')
    targets = [row['new_planned_test_name'] for row in mapping]
    need(len(set(targets))==20 and set(targets)<=set(expanded), 'retry_unique20_targets')
    new_names=sorted(set(expanded)-set(targets))
    need(len(new_names)==29 and not set(new_names)&set(targets), 'retry_new29_disjoint')
    product_exact(inv['new29_planned_test_names'], new_names, 'retry_exact_new29')
    return expanded


def product_full_inventory_contract(value, rows, retry_names, root):
    inv=value['full_unit_inventory']
    exact(inv, tuple(FULL_V3_FIXED)+('planned_full_test_names',), 'full_inventory')
    for key, expected in FULL_V3_FIXED.items():
        if key=='baseline_rows': expected=dict(path=str(Path(root)/expected['path']), sha256=expected['sha256'])
        product_exact(inv[key], expected, 'full_' + key)
    names=[row['test_name'] for row in rows if row['method']['className']!=RETRY_CLASS]
    need(len(names)==775, 'full_nonretry775')
    transaction=[row['test_name'] for row in rows if row['method']['className']==TRANSACTION_CLASS]
    need(len(transaction)==25 and set(transaction)<=set(names), 'full_transaction25_retained')
    expected=sorted(names+retry_names)
    need(len(expected)==len(set(expected))==824, 'full_unique824')
    product_exact(inv['planned_full_test_names'], expected, 'full_complete_case_set')


def product_manifest_contract(value, inputs, lineage):
    root=inputs['workspace_root']; fixed=product_fixed(root)
    exact(value, tuple(fixed)+('source_hashes','base_source_hashes','retry_contract_inventory','full_unit_inventory'), 'product_manifest43')
    for key, expected in fixed.items(): product_exact(value[key], expected, 'product_' + key)
    product_exact(inputs['ef_overlay']['manifest'], dict(path=str(Path(root)/PRODUCT_V3_PATH),sha256=PRODUCT_V3_SHA), 'only_current_product_v3_ref')
    product_exact(inputs['ef_overlay']['source_root'], value['source_root'], 'actual_v3_source_root')
    for key in ('base_prepared_source','base_approved_source','transaction_capability_patch'):
        verify_ref(value[key], root)
    for references in (value['base_lineage_artifacts'],value['unit_revision_parent']):
        for reference in references.values(): verify_ref(reference, root)
    base=product_map(value['base_source_hashes'],175,'base_map175'); current=product_map(value['source_hashes'],178,'product_map178')
    product_exact(base, read(verify_ref(value['base_approved_source'],root))['source_hashes'], 'actual_approved_base175')
    product_exact(base, read(verify_ref(value['base_prepared_source'],root))['approved_ef_source_hashes'], 'actual_prepared_base175')
    need(set(base)<=set(current), 'base_no_deleted_paths')
    product_exact(sorted(set(current)-set(base)), sorted(value['added_files']), 'actual_three_added')
    product_exact(sorted(name for name in base if base[name]!=current[name]), value['modified_files'], 'actual_two_modified')
    need(sum(base[name]==current[name] for name in base)==173, 'actual_base173_unchanged')
    product_exact(sorted(current), inputs['expected_closure']['product_paths'], 'actual_approved_product_paths')
    product_exact(inputs['expected_closure']['product_count'],178,'closure_product178')
    hosts=inputs['expected_closure']['host_paths']
    expected_hosts=sorted(HOST+'/'+name.removeprefix('host/') for name in policy()['source_files'] if name.startswith('host/'))
    product_exact(hosts,expected_hosts,'closure_host9')
    product_exact(inputs['expected_closure']['source_paths'], sorted(list(current)+hosts), 'closure_source187_paths')
    product_exact(inputs['expected_closure']['source_count'],187,'closure_source187_count')
    product_exact(inputs['expected_closure']['locked_paths'], sorted(list(current)+hosts+[HOST+'/packages.lock.json']), 'closure_locked188_paths')
    product_exact(inputs['expected_closure']['locked_count'],188,'closure_locked188_count')
    source_root=no_symlink_ancestors(value['source_root'])
    need(source_root.is_dir(), 'product_actual_source_directory')
    actual=[]
    for path in source_root.rglob('*'):
        need(not path.is_symlink(), 'product_source_symlink')
        if path.is_file(): actual.append(str(path.relative_to(source_root)))
    product_exact(sorted(actual), sorted(current), 'product_source_exact_enumeration')
    product_exact(inventory(source_root, sorted(current)), current, 'product_source_real_bytes')
    aggregate=''.join(current[name]+'  '+name+'\n' for name in sorted(current))
    product_exact(value['source_aggregate_sha256'], hashlib.sha256(aggregate.encode()).hexdigest(), 'product_aggregate')
    for key, expected in value['base_lineage'].items():
        if key!='approved_ef_source_manifest_sha256': product_exact(lineage[key],expected,'base_lineage_' + key)
    product_exact(lineage['approved_ef_source_manifest_sha256'],PRODUCT_V3_SHA,'lineage_current_v3')
    product_exact(lineage['transaction_patch_sha256'],value['transaction_capability_patch']['sha256'],'lineage_patch_v3')
    product_exact(lineage['parent_overlay_sha256'],value['base_prepared_source']['sha256'],'lineage_base175_parent')
    parents=value['unit_revision_parent']
    product_parent_contract(value, read(verify_ref(parents['product_manifest'],root)), read(verify_ref(parents['sourcefreeze_manifest'],root)), read(verify_ref(parents['source_approval'],root)))
    baseline=value['full_unit_inventory'].get('baseline_rows')
    expected=FULL_V3_FIXED['baseline_rows']
    product_exact(baseline,dict(path=str(Path(root)/expected['path']),sha256=expected['sha256']),'baseline_fixed_ref')
    rows=product_baseline_rows_contract(read(verify_ref(baseline,root)),root)
    retry_names=product_retry_inventory_contract(value,rows)
    product_full_inventory_contract(value,rows,retry_names,root)
    return value


def product_source_approval_contract(approval, inputs, manifest):
    paths=sorted(manifest['source_hashes'])
    product_exact(approval,dict(schema_version=1,task='TP07-T1-overlay-source-approval',status='source_approved',manifest=inputs['ef_overlay']['manifest'],exact_paths=paths,exact_count=178,scope=SOURCE_SCOPE_V3),'source_approval_v3')
    product_exact(manifest['required_source_approval_scope'], SOURCE_SCOPE_V3, 'manifest_exact_source_scope_v3')
    product_exact(inputs['expected_closure']['product_paths'],paths,'source_approval_closure_paths')
    return approval


def source(a, inputs, output):
    host_manifest=probe_source_manifest(inputs)
    overlay=inputs['ef_overlay'];exact(overlay, ('source_root','manifest','lineage','root_approval'), 'overlay_schema')
    manifest=read(verify_ref(overlay['manifest'],inputs['workspace_root']));approval=read(verify_ref(overlay['root_approval'],inputs['workspace_root']))
    product_manifest_contract(manifest,inputs,overlay['lineage'])
    product_source_approval_contract(approval,inputs,manifest)
    product_exact(overlay['root_approval'],dict(path=str(Path(inputs['workspace_root'])/SOURCE_APPROVAL_V3_PATH),sha256=SOURCE_APPROVAL_V3_SHA),'source_approval_fixed_v3_ref')
    paths=sorted(manifest['source_hashes']);closure=inputs['expected_closure']
    exact(closure, ('product_paths','product_count','host_paths','source_paths','source_count','locked_paths','locked_count','feed','feed_inventory','nuget_config','native_graphs'), 'closure_schema')
    expected_host=[HOST+'/'+x.removeprefix('host/') for x in policy()['source_files'] if x.startswith('host/')]
    need(type(closure['product_count']) is int and closure['product_count']==178 and paths==closure['product_paths'] and len(paths)==178, 'actual_new_product_exact178')
    need(closure['host_paths']==sorted(expected_host) and closure['source_paths']==sorted(paths+expected_host) and closure['source_count']==187, 'actual_exact187_closure')
    need(closure['locked_paths']==sorted(paths+expected_host+[HOST+'/packages.lock.json']) and closure['locked_count']==188, 'locked_exact188_closure')
    need(inventory(overlay['source_root'],paths)==manifest['source_hashes'], 'approved_product_source_drift')
    replay=output/'replay';replay.mkdir()
    for name in paths:
        target=replay/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(Path(overlay['source_root'])/name,target)
    for name in expected_host:
        target=replay/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(Path(host_manifest['source_root'])/'host'/name.removeprefix(HOST+'/'),target)
    hashes=inventory(replay,closure['source_paths'])
    need({name:hashes[HOST+'/'+name.removeprefix('host/')] for name in host_manifest['source_hashes']}==host_manifest['source_hashes'], 'copied_probe_source_exact9')
    probe_source_manifest(inputs)
    write(output/'source.json',dict(schema_version=1,task='TP07-T1-source',inputs=ref(a.inputs),toolpack=inputs['toolpack'],replay_root=str(replay),lineage=overlay['lineage'],W=read(verify_ref(inputs['W_identity']))['W'],source_hashes=hashes,exact_paths=sorted(hashes),exact_count=len(hashes),role='producer_source_pending_independent_acceptance'))

def check_source(source):
    root=Path(source['replay_root']);actual={str(p.relative_to(root)) for p in root.rglob('*') if p.is_file() and not any(part in ('bin','obj') for part in p.relative_to(root).parts) and str(p.relative_to(root))!=HOST+'/packages.lock.json'}
    need(actual==set(source['exact_paths']), 'unlisted_replay_file')
    need(inventory(source['replay_root'],source['exact_paths'])==source['source_hashes'], 'replay_source_drift')

RAW_PROJECTS = (PROVIDER+'/W.EntityFrameworkCore.Dameng.csproj', HOST+'/TP07TransactionCapabilityProbe.csproj')


def raw_project_paths(project):
    need(type(project) is str and project in RAW_PROJECTS, 'raw_fixed_project')
    directory=Path(project).parent
    return dict(lock=str(directory/'packages.lock.json'),assets=str(directory/'obj/project.assets.json'),dgspec=str(directory/'obj'/(Path(project).name+'.nuget.dgspec.json')))


def raw_snapshot_files(root):
    root=no_symlink_ancestors(root);need(root.is_dir(),'raw_snapshot_directory')
    expected=sorted(raw_project_paths(project)[key] for project in RAW_PROJECTS for key in ('assets','dgspec'))
    directories={str(parent) for relative in expected for parent in Path(relative).parents if str(parent)!='.'}
    actual=[];actual_directories=[]
    for path in root.rglob('*'):
        need(not path.is_symlink(),'raw_snapshot_symlink')
        if path.is_file():actual.append(str(path.relative_to(root)))
        elif path.is_dir():actual_directories.append(str(path.relative_to(root)))
        else:need(False,'raw_snapshot_special_entry')
    product_exact(sorted(actual),expected,'raw_snapshot_exact_four')
    product_exact(sorted(actual_directories),sorted(directories),'raw_snapshot_exact_parent_directories')


def snapshot_raw_graphs(graphs, replay, output, phase):
    # Called only after the corresponding successful SDK operations. Copy
    # original bytes, then re-read both sides; never reserialize SDK output.
    import copy
    need(phase in ('native','locked'),'raw_snapshot_phase')
    need(type(graphs) is list and len(graphs)==2,'raw_snapshot_two_graphs')
    product_exact([graph['project'] for graph in graphs],list(RAW_PROJECTS),'raw_snapshot_projects')
    root=no_symlink_ancestors(Path(output)/('raw-'+phase));need(not root.exists(),'fresh_raw_snapshot');root.mkdir()
    result=copy.deepcopy(graphs)
    for graph in result:
        exact(graph,('project','lock','assets','dgspec','lock_dependencies','frameworks') if phase=='native' else ('project','assets','dgspec'),'raw_snapshot_graph')
        for key in ('assets','dgspec'):
            relative=raw_project_paths(graph['project'])[key];source=no_symlink_ancestors(Path(replay)/relative)
            product_exact(graph[key]['path'],str(source),'raw_original_live_path');verify_ref(graph[key],replay)
            target=root/relative;target.parent.mkdir(parents=True,exist_ok=True)
            before=sha(source);shutil.copyfile(source,target)
            need(sha(source)==before==sha(target)==graph[key]['sha256'],'raw_copy_original_bytes')
            graph[key]=ref(target)
    raw_snapshot_files(root)
    return result


def raw_snapshot_contract(stage_ref, graphs, replay, workspace, phase):
    need(phase in ('native','locked'),'raw_contract_phase')
    stage=verify_ref(stage_ref,workspace);expected_name='native.json' if phase=='native' else 'build.json'
    product_exact(stage_ref['path'],str(stage),'raw_stage_canonical_ref');need(stage.name==expected_name,'raw_stage_filename')
    root=stage.parent/('raw-'+phase);raw_snapshot_files(root)
    need(type(graphs) is list and len(graphs)==2,'raw_contract_two_graphs')
    product_exact([graph.get('project') if type(graph) is dict else None for graph in graphs],list(RAW_PROJECTS),'raw_contract_ordered_projects')
    for graph in graphs:
        exact(graph,('project','lock','assets','dgspec','lock_dependencies','frameworks') if phase=='native' else ('project','assets','dgspec'),'raw_contract_graph_shape')
        for key in ('assets','dgspec'):
            relative=raw_project_paths(graph['project'])[key]
            exact(graph[key],('path','sha256'),'raw_contract_ref_shape')
            product_exact(graph[key]['path'],str(root/relative),'raw_contract_stage_path');verify_ref(graph[key],root)
        if phase=='native':
            exact(graph['lock'],('path','sha256'),'raw_contract_lock_ref_shape')
            product_exact(graph['lock']['path'],str(Path(replay)/raw_project_paths(graph['project'])['lock']),'native_live_lock_path');verify_ref(graph['lock'],replay)
    return graphs


def locked_raw_delta_contract(native_raw, locked_raw, project, kind, replay):
    # This is a full JSON comparison with five finite insertions across four
    # documents. Other fields, including headers/pruning, remain untouched.
    import copy
    raw_project_paths(project);need(kind in ('assets','dgspec'),'raw_json_kind')
    need(type(native_raw) is dict and type(locked_raw) is dict,'raw_json_object')
    if kind=='assets':
        paths=[('project','restore','restoreLockProperties')]
        need(type(native_raw.get('project')) is dict and type(native_raw['project'].get('restore')) is dict,'raw_assets_restore')
        product_exact(native_raw['project']['restore'].get('projectPath'),str(Path(replay)/project),'raw_assets_actual_project_path')
    else:
        projects=[str(Path(replay)/name) for name in RAW_PROJECTS[:1 if project==RAW_PROJECTS[0] else 2]]
        need(type(native_raw.get('projects')) is dict,'raw_dgspec_projects')
        product_exact(sorted(native_raw['projects']),sorted(projects),'raw_dgspec_actual_project_set')
        paths=[('projects',name,'restore','restoreLockProperties') for name in projects]
        for name in projects:
            document=native_raw['projects'][name]
            need(type(document) is dict and type(document.get('restore')) is dict,'raw_dgspec_restore')
            product_exact(document['restore'].get('projectPath'),name,'raw_dgspec_actual_project_path')
    expected=copy.deepcopy(native_raw)
    for path in paths:
        old=native_raw;new=locked_raw;target=expected
        for key in path:
            need(type(old) is dict and key in old and type(new) is dict and key in new,'raw_locked_mode_container')
            old=old[key];new=new[key];target=target[key]
        need(type(old) is dict and type(new) is dict and 'restoreLockedMode' not in old,'raw_native_mode_absent')
        need(type(new.get('restoreLockedMode')) is bool and new['restoreLockedMode'] is True,'raw_locked_mode_true_bool')
        target['restoreLockedMode']=True
    product_exact(locked_raw,expected,'raw_complete_only_finite_locked_mode_delta')


def live_lock_contract(graphs, replay):
    need(type(graphs) is list and len(graphs)==2,'live_two_locks')
    product_exact([graph.get('project') if type(graph) is dict else None for graph in graphs],list(RAW_PROJECTS),'live_lock_projects')
    result={}
    for graph in graphs:
        path=no_symlink_ancestors(Path(replay)/raw_project_paths(graph['project'])['lock'])
        product_exact(graph['lock']['path'],str(path),'live_lock_fixed_path')
        verify_ref(graph['lock'],replay)
        result[graph['project']]=ref(path)
    return result


def stage_raw_contract(native_ref, native_doc, build_ref, build_doc, startup, replay, workspace):
    exact(native_doc,('schema_version','task','inputs','source','sdk_events','project_graphs','cache_root','archive_records','native_metadata_records','source_before','source_after','database_accessed'),'raw_native_finite_shape')
    exact(build_doc,('schema_version','task','inputs','source','native','locked_restores','commands','startup','binaries','source_before','source_after','lock_before','lock_after','offline_proofs','database_accessed','locked_project_graphs'),'raw_build_finite_shape')
    product_exact(native_doc['schema_version'],1,'raw_native_schema');product_exact(native_doc['task'],'TP07-T1-native','raw_native_task')
    product_exact(native_doc['source'],build_doc['source'],'raw_native_build_same_source');product_exact(native_doc['inputs'],build_doc['inputs'],'raw_native_build_same_inputs')
    product_exact(native_doc['database_accessed'],False,'raw_native_no_database');product_exact(build_doc['database_accessed'],False,'raw_build_no_database')
    product_exact(build_doc['schema_version'],1,'raw_build_schema');product_exact(build_doc['task'],'TP07-T1-build','raw_build_task')
    product_exact(build_doc['native'],native_ref,'raw_build_native_ref')
    product_exact(startup['native'],native_ref,'raw_startup_native_ref');product_exact(startup['source'],build_doc['source'],'raw_same_source')
    product_exact(startup['replay_root'],str(replay),'raw_startup_replay')
    build_path=verify_ref(build_ref,workspace)
    product_exact(build_doc['startup']['path'],str(build_path.parent/'startup.json'),'raw_startup_same_build_stage')
    product_exact(read(verify_ref(build_doc['startup'],workspace)),startup,'raw_actual_startup_bytes')
    native=raw_snapshot_contract(native_ref,native_doc['project_graphs'],replay,workspace,'native')
    locked=raw_snapshot_contract(build_ref,build_doc['locked_project_graphs'],replay,workspace,'locked')
    locks=live_lock_contract(native,replay)
    product_exact(build_doc['lock_before'],locks,'raw_true_live_lock_before');product_exact(build_doc['lock_after'],locks,'raw_true_live_lock_after')
    assets={}
    for old,new in zip(native,locked):
        for kind in ('assets','dgspec'):
            relative=raw_project_paths(old['project'])[kind]
            verify_ref(dict(path=str(Path(replay)/relative),sha256=new[kind]['sha256']),replay)
            locked_raw_delta_contract(read(verify_ref(old[kind])),read(verify_ref(new[kind])),old['project'],kind,replay)
        assets[raw_project_paths(new['project'])['assets']]=new['assets']['sha256']
    product_exact(startup['locks'],{raw_project_paths(project)['lock']:value['sha256'] for project,value in locks.items()},'raw_startup_live_locks')
    product_exact(startup['assets'],assets,'raw_startup_locked_live_assets')


def native(a, inputs, output):
    sr,source=phase_ref(a,'source','TP07-T1-source',inputs);check_source(source);replay=Path(source['replay_root']);closure=inputs['expected_closure']
    feed=Path(closure['feed']);need(feed.is_absolute() and feed.is_dir(), 'local_feed');need(inventory(feed,sorted(closure['feed_inventory']))==closure['feed_inventory'], 'exact_feed_archives')
    config=verify_ref(closure['nuget_config'],inputs['workspace_root']);tree=ET.parse(config).getroot();sources=tree.find('packageSources')
    need(sources is not None and len(list(sources))==2 and sources[0].tag=='clear' and sources[1].tag=='add' and Path(sources[1].get('value','')).resolve()==feed.resolve(), 'single_local_native_feed')
    env=environment(output);need(not Path(env['NUGET_PACKAGES']).exists(), 'fresh_native_cache')
    projects=[PROVIDER+'/W.EntityFrameworkCore.Dameng.csproj',HOST+'/TP07TransactionCapabilityProbe.csproj'];events=[];graphs=[]
    before=dict(source['source_hashes'])
    for project in projects:
        event=process([inputs['sdk']['path'],'restore',str(replay/project),'--configfile',str(config),'--force-evaluate','--disable-build-servers','-m:1','/nodeReuse:false','/p:UseSharedCompilation=false'],replay,env,180);events.append(event);write(output/('native-process-'+str(len(events)-1)+'.json'),event);need(process_succeeded(event), 'native_restore_failed')
    # Read both final native graphs only after both restores have succeeded.
    for project in projects:
        directory=replay/Path(project).parent;lock=read(directory/'packages.lock.json');assets=read(directory/'obj/project.assets.json');dgspec=read(directory/'obj'/ (Path(project).name+'.nuget.dgspec.json'))
        expected=closure['native_graphs'][project]
        actual={name:{k:v for k,v in row.items()} for target in lock['dependencies'].values() for name,row in target.items()}
        need(actual==expected['lock_dependencies'], 'exact_native_lock_graph')
        packages={name.split('/')[0] for name,row in assets['libraries'].items() if row['type']=='package'}
        need(packages==set(policy()['core_packages']+['W.DmProvider']), 'exact_Core13_W_packages')
        need(set(dgspec['projects'])=={str(replay/PROVIDER/'W.EntityFrameworkCore.Dameng.csproj')}|({str(replay/HOST/'TP07TransactionCapabilityProbe.csproj')} if project.startswith(HOST) else set()), 'native_project_graph_paths')
        need(assets['project']['frameworks']==expected['assets_frameworks'], 'exact_framework_pruning_references')
        graphs.append(dict(project=project,lock=ref(directory/'packages.lock.json'),assets=ref(directory/'obj/project.assets.json'),dgspec=ref(directory/'obj'/(Path(project).name+'.nuget.dgspec.json')),lock_dependencies=actual,frameworks=assets['project']['frameworks']))
    archives=[];metadata=[];cache=Path(env['NUGET_PACKAGES'])
    for graph in graphs:
        for name,row in graph['lock_dependencies'].items():
            if row['type']=='Project':continue
            package=cache/name.lower()/row['resolved'];nupkg=package/(name.lower()+'.'+row['resolved']+'.nupkg');sidecar=Path(str(nupkg)+'.sha512');meta=package/'.nupkg.metadata'
            content=base64.b64encode(hashlib.sha512(nupkg.read_bytes()).digest()).decode();native_meta=read(meta)
            need(sidecar.read_text().strip()==content, 'archive_fullZIP_sidecar_sha512')
            origin=closure['native_graphs'][graph['project']]['lock_dependencies'][name]
            need(native_meta['contentHash']==row['contentHash'] and row['contentHash']==origin['contentHash'] and row['resolved']==origin['resolved'], 'native_metadata_lock_origin_contentHash')
            feed_names=[entry for entry in closure['feed_inventory'] if Path(entry).name.casefold()==nupkg.name.casefold()]
            need(len(feed_names)==1 and sha(nupkg)==closure['feed_inventory'][feed_names[0]] and sha(feed/feed_names[0])==sha(nupkg), 'actual_cache_feed_archive_sha256')
            with zipfile.ZipFile(nupkg) as archive:
                nuspec_names=[entry for entry in archive.namelist() if entry.lower().endswith('.nuspec')]
                need(len(nuspec_names)==1, 'archive_single_nuspec')
                nuspec_bytes=archive.read(nuspec_names[0]);nuspec=ET.fromstring(nuspec_bytes)
            local=lambda tag:tag.rsplit('}',1)[-1]
            metadata_nodes=[node for node in nuspec if local(node.tag)=='metadata']
            need(len(metadata_nodes)==1, 'nuspec_metadata')
            fields={local(node.tag):node.text for node in metadata_nodes[0] if local(node.tag) in ('id','version')}
            need(fields==dict(id=name,version=row['resolved']), 'archive_nuspec_id_version')
            installed_nuspec=package/(name.lower()+'.nuspec')
            need(installed_nuspec.is_file() and installed_nuspec.read_bytes()==nuspec_bytes, 'native_installed_nuspec_bytes')
            archives.append(dict(id=name,version=row['resolved'],archive=ref(nupkg),sidecar=ref(sidecar),sha512=content));metadata.append(dict(id=name,version=row['resolved'],metadata=ref(meta),content_hash=native_meta['contentHash']))
    graphs=snapshot_raw_graphs(graphs,replay,output,'native')
    check_source(source)
    write(output/'native.json',dict(schema_version=1,task='TP07-T1-native',inputs=ref(a.inputs),source=sr,sdk_events=events,project_graphs=graphs,cache_root=str(cache),archive_records=archives,native_metadata_records=metadata,source_before=before,source_after=inventory(replay,source['exact_paths']),database_accessed=False))

def binary(path, name, deps=False):
    result=dict(name=name,path=str(Path(path).resolve()),sha256=sha(path),mvid=managed_identity.mvid(Path(path).read_bytes()))
    if deps:result['deps']=ref(Path(path).with_suffix('.deps.json'))
    return result

def build(a, inputs, output):
    sr,source=phase_ref(a,'source','TP07-T1-source',inputs);nr,native_doc=phase_ref(a,'native','TP07-T1-native',inputs);need(native_doc['source']==sr and native_doc['database_accessed'] is False,'native_same_source');check_source(source)
    replay=Path(source['replay_root']);env=environment(output);env['NUGET_PACKAGES']=native_doc['cache_root'];config=verify_ref(inputs['expected_closure']['nuget_config']);restores=[];commands=[]
    raw_snapshot_contract(nr,native_doc['project_graphs'],replay,inputs['workspace_root'],'native')
    lock_before=live_lock_contract(native_doc['project_graphs'],replay)
    for project in [PROVIDER+'/W.EntityFrameworkCore.Dameng.csproj',HOST+'/TP07TransactionCapabilityProbe.csproj']:
        restore=process([inputs['sdk']['path'],'restore',str(replay/project),'--locked-mode','--configfile',str(config),'--disable-build-servers','-m:1','/nodeReuse:false','/p:UseSharedCompilation=false'],replay,env,180);restores.append(restore);write(output/('locked-restore-process-'+str(len(restores)-1)+'.json'),restore);need(process_succeeded(restore),'locked_restore_failed')
        command=process([inputs['sdk']['path'],'build',str(replay/project),'--no-restore','--disable-build-servers','-m:1','/nodeReuse:false','/p:UseSharedCompilation=false'],replay,env,180);commands.append(command);write(output/('build-process-'+str(len(commands)-1)+'.json'),command);need(process_succeeded(command),'fixed_build_failed')
    check_source(source);lock_after=live_lock_contract(native_doc['project_graphs'],replay);product_exact(lock_before,lock_after,'build_lock_drift')
    locked_live=[dict(project=project,assets=ref(replay/raw_project_paths(project)['assets']),dgspec=ref(replay/raw_project_paths(project)['dgspec'])) for project in RAW_PROJECTS]
    locked_graphs=snapshot_raw_graphs(locked_live,replay,output,'locked')
    for original,locked in zip(native_doc['project_graphs'],locked_graphs):
        for kind in ('assets','dgspec'):
            verify_ref(dict(path=str(replay/raw_project_paths(original['project'])[kind]),sha256=locked[kind]['sha256']),replay)
            locked_raw_delta_contract(read(verify_ref(original[kind])),read(verify_ref(locked[kind])),original['project'],kind,replay)
    out=replay/HOST/'bin/Debug/net10.0';host=binary(out/'TP07TransactionCapabilityProbe.dll','TP07TransactionCapabilityProbe',True);provider=binary(out/'W.EntityFrameworkCore.Dameng.dll','W.EntityFrameworkCore.Dameng');deps=[binary(out/(n+'.dll'),n) for n in ['Microsoft.EntityFrameworkCore','Microsoft.EntityFrameworkCore.Relational','Microsoft.EntityFrameworkCore.Abstractions']]
    w=read(verify_ref(inputs['W_identity']));identity_path=verify_ref(inputs['W_identity']);W=dict(version=w['W']['version'],package_sha256=w['W']['package_sha256'],dll_sha256=w['W']['dll_sha256'],mvid=w['W']['mvid'],identity_sha256=inputs['W_identity']['sha256'],source_manifest_sha256=w['producer']['source_manifest_sha256'],archive_sha256=w['producer']['source_archive_sha256'],path=str(out/'W.DmProvider.dll'))
    need(sha(W['path'])==W['dll_sha256'] and managed_identity.mvid(Path(W['path']).read_bytes())==W['mvid'],'original_W_binary_identity')
    startup=dict(schema_version=1,task='TP07-T1-startup',source=sr,native=nr,replay_root=str(replay),lineage=source['lineage'],W=W,locks={str(verify_ref(g['lock']).relative_to(replay)):g['lock']['sha256'] for g in native_doc['project_graphs']},assets={raw_project_paths(g['project'])['assets']:g['assets']['sha256'] for g in locked_graphs},host=host,ef_provider=provider,ef_dependencies=deps,source_hashes=source['source_hashes']);write(output/'startup.json',startup)
    binaries={str(p.relative_to(replay)):sha(p) for p in sorted(replay.rglob('*')) if p.is_file() and 'bin' in p.relative_to(replay).parts and p.suffix in ('.dll','.json','.pdb')}
    write(output/'build.json',dict(schema_version=1,task='TP07-T1-build',inputs=ref(a.inputs),source=sr,native=nr,locked_restores=restores,commands=commands,startup=ref(output/'startup.json'),binaries=binaries,source_before=source['source_hashes'],source_after=inventory(replay,source['exact_paths']),lock_before=lock_before,lock_after=lock_after,offline_proofs=[],database_accessed=False,locked_project_graphs=locked_graphs))

def offline(a, inputs, output):
    br,build_doc=phase_ref(a,'build','TP07-T1-build',inputs);startup=read(verify_ref(build_doc['startup']));env=environment(output);dll=verify_binary(startup['host']);event=process([inputs['sdk']['path'],str(dll),'--offline','--output',str(output/'host-offline.json')],Path(startup['replay_root']),env,180)
    write(output/'offline-host-process.json',event);need(process_succeeded(event),'offline_host_failed');host=read(output/'host-offline.json');need(host['status']=='completed' and host['database_accessed'] is False and host['activation_accepted'] is False,'offline_observation')
    python_event=process([inputs['python']['path'],str(HERE/'test_contracts.py')],inputs['workspace_root'],env,180);write(output/'offline-python-process.json',python_event);need(process_succeeded(python_event),'offline_python_failed')
    write(output/'offline.json',dict(schema_version=1,task='TP07-T1-offline-execution',inputs=ref(a.inputs),build=br,startup=build_doc['startup'],host_process=event,host_report=ref(output/'host-offline.json'),python_process=python_event,toolpack=inputs['toolpack'],database_accessed=False,status='completed_pending_independent_review'))

def verify_binary(identity):
    path=no_symlink_ancestors(identity['path']);need(path.is_absolute() and sha(path)==identity['sha256'] and managed_identity.mvid(path.read_bytes())==identity['mvid'],'binary_identity');return path

def readiness(a, inputs, output):
    br,b=phase_ref(a,'build','TP07-T1-build',inputs);orr,o=phase_ref(a,'offline','TP07-T1-offline-execution',inputs);need(o['build']==br and o['startup']==b['startup'] and b['database_accessed'] is False and o['database_accessed'] is False,'readiness_same_lineage')
    startup=read(verify_ref(b['startup']));source=read(verify_ref(b['source']));native_doc=read(verify_ref(b['native']));check_source(source)
    need(startup['source']==b['source'] and startup['native']==b['native'] and startup['host']['name']=='TP07TransactionCapabilityProbe','new_startup_identity')
    for identity in [startup['host'],startup['ef_provider']]+startup['ef_dependencies']:verify_binary(identity)
    stage_raw_contract(b['native'],native_doc,br,b,startup,Path(source['replay_root']),inputs['workspace_root'])
    need(all(sha(Path(startup['replay_root'])/name)==value for name,value in b['binaries'].items()),'build_output_drift')
    toolpack(inputs['toolpack'],inputs['workspace_root']);verify_ref(o['host_report']);need(process_succeeded(o['host_process']) and process_succeeded(o['python_process']),'offline_processes')
    write(output/'readiness.json',dict(schema_version=1,task='TP07-T1-readiness',inputs=ref(a.inputs),source=b['source'],native=b['native'],build=br,startup=b['startup'],toolpack=inputs['toolpack'],offline=orr,ordered_cases=[x['case_id'] for x in policy()['cases']],budgets=policy()['budgets'],status='ready_pending_runtime',database_accessed=False,activation_accepted=False))

def main():
    parser=argparse.ArgumentParser();sub=parser.add_subparsers(dest='stage',required=True)
    for name in ('source','native','build','offline','readiness'):
        p=sub.add_parser(name);p.add_argument('--inputs',required=True);p.add_argument('--inputs-sha256',required=True);p.add_argument('--output',required=True)
        for phase in {'source':[],'native':['source'],'build':['source','native'],'offline':['build'],'readiness':['build','offline']}[name]:p.add_argument('--'+phase,required=True);p.add_argument('--'+phase+'-sha256',required=True)
    a=parser.parse_args()
    try:
        inputs=load_inputs(a.inputs,a.inputs_sha256);output=fresh(a.output,inputs['workspace_root']);globals()[a.stage](a,inputs,output);toolpack(inputs['toolpack'],inputs['workspace_root']);return 0
    except Exception as e:
        # Safe error code only; never stringify an external exception.
        print(json.dumps({'status':'failed','error_type':type(e).__name__,'code':e.args[0] if isinstance(e,Stop) else 'external_failure'}));return 1
if __name__=='__main__':raise SystemExit(main())
