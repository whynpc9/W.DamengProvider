#!/usr/bin/env python3
"""Finite L0 source replay / reviewed lock build / TEST-wrapper runtime coordination."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import types
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
TOOLPACK_FILES = (
    'tools/DownstreamAcceptance/TP07/runtime-run.py',
    'tools/DownstreamAcceptance/TP07/runtime-validate.py',
    'tools/DownstreamAcceptance/TP07/runtime-cases.json',
    'tools/DownstreamAcceptance/TP07/test_runtime_contracts.py',
    'tools/DownstreamAcceptance/TP07/managed_identity.py',
    'tools/TemporalBoundaryProbe/packed_oracle.py')
POLICY = json.loads((HERE / 'runtime-cases.json').read_text())
W = POLICY['W']
L0_FILES = ['common/CandidateGuard.cs', 'common/SafeEvidence.cs', 'common/OwnedObjects.cs',
    'TP07LiteralProbe/TP07LiteralProbe.csproj', 'TP07LiteralProbe/Program.cs',
    'TP07LiteralProbe/LiteralCases.cs', 'TP07LiteralProbe/LiteralOracle.cs']
PROVIDER_LOCK = 'src/W.EntityFrameworkCore.Dameng/packages.lock.json'
L0_PROJECT = 'test/TP07LiteralProbe/TP07LiteralProbe.csproj'
L0_LOCK = 'test/TP07LiteralProbe/packages.lock.json'
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']


class Stop(ValueError): pass
def need(value, code):
    if not value: raise Stop(code)
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def read(path):
    def pairs(rows):
        result = {}
        for key, value in rows:
            need(key not in result, 'duplicate_json_key')
            result[key] = value
        return result
    def invalid(_): raise Stop('non_finite_json')
    return json.loads(Path(path).read_text(), object_pairs_hook=pairs, parse_constant=invalid)
def write(path, value): Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')
def inv(folder): return {str(p.relative_to(folder)): sha(p) for p in sorted(Path(folder).rglob('*')) if p.is_file()}


def toolpack_hashes(identity):
    need(sha(identity['manifest_path']) == identity['manifest_sha256'], 'toolpack_manifest_drift')
    actual = {name: sha(ROOT / name) for name in TOOLPACK_FILES}
    need(actual == identity['source_hashes'], 'executed_toolpack_drift')
    return actual


def toolpack_entry(a):
    # The caller supplies the independently approved manifest SHA, outside the
    # six-file pack. No tool includes its own hash or imports caller-selected code.
    need(a.toolpack_manifest and re.fullmatch(r'[a-f0-9]{64}', a.toolpack_manifest_sha256 or ''), 'approved_toolpack_required')
    path = Path(a.toolpack_manifest).resolve()
    need(sha(path) == a.toolpack_manifest_sha256, 'approved_toolpack_manifest_pin')
    document = read(path)
    need(set(document) == {'schema_version', 'task', 'source_hashes'} and document['schema_version'] == 1
        and document['task'] == 'TP07-L0 toolpack', 'finite_toolpack_manifest_schema')
    hashes = document['source_hashes']
    need(set(hashes) == set(TOOLPACK_FILES) and all(type(value) is str and re.fullmatch(r'[a-f0-9]{64}', value) for value in hashes.values()), 'finite_toolpack_closure')
    identity = dict(manifest_path=str(path), manifest_sha256=a.toolpack_manifest_sha256, source_hashes=hashes)
    before = toolpack_hashes(identity)
    need(hashes['tools/TemporalBoundaryProbe/packed_oracle.py'] == POLICY['independent_packed_oracle_sha256'], 'independent_decoder_policy_pin')
    return identity, before


def toolpack_exit(identity, before, phase):
    after = toolpack_hashes(identity)
    need(before == after, 'phase_toolpack_drift')
    return dict(phase=phase, before=before, after=after)


def toolpack_binding(document, identity, phase):
    need(document['toolpack'] == identity, 'same_toolpack_required')
    check = document['toolpack_check']
    need(check == dict(phase=phase, before=identity['source_hashes'], after=identity['source_hashes']), 'complete_phase_toolpack_check')


def artifact(path): return dict(path=str(Path(path).resolve()), sha256=sha(path))


def phase_receipt(reference, identity, phase, source_sha):
    need(sha(reference['path']) == reference['sha256'], 'phase_receipt_artifact_drift')
    document = read(reference['path'])
    toolpack_binding(document, identity, phase)
    need(document['source_manifest_sha256'] == source_sha, 'phase_receipt_source_binding')
    return document


def check_literal_grammar(receipt, startup):
    row = receipt['process']; reference = receipt['report']; path = Path(reference['path'])
    need(type(row['exit_code']) is int and type(row['host_exit_code']) is int and row['exit_code'] == row['host_exit_code'] == 0
        and row['timed_out'] is False and row['termination_code'] is None and sha(row['log_path']) == row['log_sha256'], 'actual_CSharp_grammar_process')
    need(sha(path) == reference['sha256'], 'frozen_CSharp_grammar_report')
    dll = Path(startup['replay_root']) / 'test/TP07LiteralProbe/bin/Debug/net10.0/TP07LiteralProbe.dll'
    need(row['command'] == [str(Path.home() / '.dotnet/dotnet'), str(dll), '--selftest-literal-grammar', '--output', str(path.parent)], 'actual_CSharp_grammar_command')
    report = read(path)
    need(report['schema_version'] == 1 and report['task'] == 'TP07-L0-literal-grammar' and report['status'] == 'grammar_selftest_complete'
        and report['database_accessed'] is False and report['expected_only'] is False and report['candidate_acceptance'] is False
        and report['literal_runtime_accepted'] is False and 'failure' not in report
        and type(report['process_exit_code']) is int and report['process_exit_code'] == 0, 'actual_grammar_selftest_scope')
    expected = POLICY['literal_grammar_cases']; cases = report['cases']
    need(report['case_count'] == report['passed'] == len(cases) == len(expected) == 35 and report['failed'] == 0
        and all(type(report[key]) is int for key in ('case_count', 'passed', 'failed'))
        and type(report['accepted_case_count']) is int and type(report['rejected_case_count']) is int
        and report['accepted_case_count'] == 12 and report['rejected_case_count'] == 23
        and [value['case_id'] for value in cases] == [value['case_id'] for value in expected], 'complete_CSharp_grammar_cases')
    for value, vector in zip(cases, expected):
        need(value['literal'] == vector['literal'] and value['literal_sha256'] == hashlib.sha256(vector['literal'].encode()).hexdigest()
            and value['expected_accept'] is vector['expected_accept'] and value['actual_accept'] is vector['expected_accept']
            and value['status'] == 'completed', 'independent_CSharp_grammar_vector')
        error = None if vector['expected_accept'] else dict(type='TP07.Runtime.EvidenceException', code='LiteralSafety', number=None)
        need(value['error'] == error, 'actual_CSharp_grammar_error')
    targets = [value for value in startup['startup_assemblies'] if value['name'] == 'TP07LiteralProbe']
    need(len(targets) == 1, 'grammar_startup_selection')
    target = targets[0]; actual = report['tool_identity']
    need(all(actual[key] == target[key] for key in ('name', 'dll_sha256', 'mvid', 'deps_sha256'))
        and re.fullmatch(r'10\.[0-9]+\.[0-9]+(?:\+[^\s]+)?', actual['runtime']), 'actual_CSharp_grammar_assembly_identity')
    need(sha(dll) == actual['dll_sha256'] and sha(dll.with_suffix('.deps.json')) == actual['deps_sha256'], 'current_CSharp_grammar_binary_drift')
    return [path, Path(row['log_path'])]


def fresh(path, root):
    path = Path(path).resolve()
    need(path.is_relative_to(Path(root).resolve() / '.local/temporal-native/verification') and not path.exists(), 'fresh_runtime_output')
    path.mkdir(parents=True)
    return path


def public_env(stage):
    value = {key: os.environ[key] for key in ('PATH', 'HOME', 'TMPDIR', 'LANG', 'LC_ALL', 'DOTNET_ROOT') if key in os.environ}
    value.update(DOTNET_CLI_HOME=str(stage / 'cli-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
        NUGET_PACKAGES=str(stage / 'nuget-packages'), NUGET_HTTP_CACHE_PATH=str(stage / 'http-cache'), PYTHONDONTWRITEBYTECODE='1')
    return value


def process(command, cwd, log, environment, timeout):
    start = datetime.datetime.now(datetime.timezone.utc)
    timed_out = False
    with Path(log).open('w') as stream:
        child = subprocess.Popen(command, cwd=cwd, env=environment, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try: child.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            import signal
            os.killpg(child.pid, signal.SIGKILL)
            child.wait()
    return dict(command=command, cwd=str(cwd), exit_code=124 if timed_out else child.returncode,
        host_exit_code=child.returncode, timed_out=timed_out, termination_code='deadline_kill' if timed_out else None,
        actual_elapsed_seconds=(datetime.datetime.now(datetime.timezone.utc) - start).total_seconds(), log_sha256=sha(log))


def check_source(folder, expected): need(inv(folder) == expected, 'exact_source_closure')



# One root-approved transaction product revision; no caller-selected code.
TX_REFS = {'transaction_patch': ('tools/DownstreamAcceptance/TP07/patches/transaction-capability.v3.patch',
                       'd13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f'),
 'transaction_product_manifest': ('tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json',
                                  '400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8'),
 'transaction_source_approval': ('.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json',
                                 '66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f')}
TX_PRODUCT_KEYS = ('EF_relational_API', 'SDK_executed', 'W', 'added_files', 'aggregate_format', 'base_approved_source', 'base_lineage', 'base_lineage_artifacts', 'base_prepared_source', 'base_source_count', 'base_source_hashes', 'database_executed', 'full_unit_inventory', 'modified_files', 'namespace_boundary', 'old_L0_E_D_cohorts_reused_as_new_acceptance', 'old_parent_source_approval_reused_for_revision', 'patch_application_base', 'patch_paths', 'planned_full_unit_cases', 'planned_retry_unit_cases', 'planned_retry_unit_methods', 'planned_transaction_unit_cases', 'planned_unit_cases', 'planned_unit_methods', 'required_next_gates', 'required_source_approval_scope', 'retry_contract_inventory', 'revision', 'schema_version', 'source_aggregate_sha256', 'source_count', 'source_hashes', 'source_root', 'static_checks_only', 'status', 'task', 'tests_executed', 'transaction_capability_patch', 'transaction_probe_or_E_observation_implemented', 'unchanged_base_file_count', 'unit_delta_from_parent', 'unit_revision_parent')
TX_PRODUCT_FIXED = {'SDK_executed': False,
 'aggregate_format': 'sorted relative source paths: sha256 two spaces path newline',
 'base_source_count': 175,
 'database_executed': False,
 'old_L0_E_D_cohorts_reused_as_new_acceptance': False,
 'old_parent_source_approval_reused_for_revision': False,
 'patch_application_base': 'exact approved175 EF sources, already including the original bridge and temporal mapping',
 'planned_full_unit_cases': 824,
 'planned_retry_unit_cases': 49,
 'planned_retry_unit_methods': 21,
 'planned_transaction_unit_cases': 25,
 'planned_unit_cases': 25,
 'planned_unit_methods': 4,
 'required_source_approval_scope': 'transaction_capability_and_w_retry_contract_tests_only',
 'revision': 3,
 'schema_version': 1,
 'source_aggregate_sha256': '2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c',
 'source_count': 178,
 'static_checks_only': True,
 'status': 'source_frozen_verification_pending',
 'task': 'TP07-T1 transaction-capability and W retry-contract test isolated EF overlay v3',
 'tests_executed': False,
 'transaction_probe_or_E_observation_implemented': False,
 'unchanged_base_file_count': 173}
TX_NESTED_REFS = [('.local/temporal-native/root-approvals/TP07-T1-product-source-v2/source-approval.json',
  'd6cff0a1a92a1b8341b12c6dfaa352028039d8908fffd1b1387254561c3f0486'),
 ('.local/temporal-native/verification/TP07-E-combined-v5-prepare/source-manifest.json',
  '9d1f8cb6ec81364bcc3335fdb4c12fef39f44bee577dd36f0cdaffb405bcae7a'),
 ('.local/temporal-native/verification/TP07-L0-tools-v3/approved-EF-source-manifest.json',
  'dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6'),
 ('.local/temporal-native/verification/TP07-L0-tools-v3/exports/base.tar',
  '668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8'),
 ('.local/temporal-native/verification/TP07-L0-tools-v3/exports/bridge-manifest.json',
  '4750a330f2f1e63a5e40780acf19820b38b86db2c1bce08ff224600a18b8b8b4'),
 ('.local/temporal-native/verification/TP07-L0-tools-v3/exports/driver-bridge.patch',
  '08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5'),
 ('.local/temporal-native/verification/TP07-L0-tools-v3/exports/mapping-manifest.json',
  '23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c'),
 ('.local/temporal-native/verification/TP07-L0-tools-v3/exports/temporal-mapping.patch',
  '4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc'),
 ('.local/temporal-native/verification/TP07-T1-product-v2-unitfix-sourcefreeze/source-manifest.json',
  'a842630f569b6dfbeebaa464133ab4bc17cf42f2166691be337b7ac9483f1236'),
 ('.local/temporal-native/verification/TP07-T1-product-v3-full-control/actual-full-rows.json',
  '83a36972c1d668de0d471579c1f1d0f65599fec481c7ba682fa980ed5574fd5b'),
 ('.local/temporal-native/verification/TP07-T1-product-v3-full-control/independent-readback.json',
  'f4fa0be7bd222b7061a91912fc3c36877606d7818a56ef90636f7c53b6bbbc5a'),
 ('.local/temporal-native/verification/TP07-T1-product-v3-full-control/safe-failed-reasons.json',
  'cac4a3fb4b962f14d3723af9e28c9765c6894d27269d2aba9348e054c4dc22ed'),
 ('.local/temporal-native/verification/TP07-T1-product-v3-full/manifest.json',
  '61234feda70debd44fa17f290f376d0895abf94d18114649c5eded4f77daebd4'),
 ('.local/temporal-native/verification/TP07-T1-product-v3-full/results/full.trx',
  'f470a92ddb772627a311bf6bd9e2966826c4f960520c63f9c3aa5e375b009271'),
 ('docs/implementation/temporal-native/TP07-T1-product-full-failure-triage-v1.md',
  'a97e8c4b48b0cf5003a24611fcb031505be4f88dfdcb3c3bd8f4547b8f063953'),
 ('docs/implementation/temporal-native/TP07-T1-product-source-review-v2.md',
  'eb47339baa20c4bbedf2be7ef644b3b0c2a343a0f00d191745cc24e2e53ebbf5'),
 ('tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v2.json',
  'fcb1d60f97d7ab77f9e4ed86526e5cb90ff369c1257df438a34d72029ee9d432'),
 ('tools/DownstreamAcceptance/TP07/patches/transaction-capability.v2.patch',
  '5df9c73a4879fbf7f2cb79da48379a92856e093fecea7df3c31956080b1f3566'),
 ('tools/DownstreamAcceptance/TP07/patches/transaction-capability.v3.patch',
  'd13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f')]
TX_MODIFIED = ('src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs', 'test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs')
TX_ADDED = ('src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransaction.cs', 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransactionFactory.cs', 'test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs')
TX_BASE_LINEAGE = {'approved_ef_source_manifest_sha256': 'dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6',
 'base_archive_sha256': '668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8',
 'bridge_patch_sha256': '08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5',
 'ef_commit': '5863d3755dbd2d333ac359bf1c0a789762adbaed',
 'mapping_manifest_sha256': '23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c',
 'mapping_patch_sha256': '4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc'}
TX_SOURCE_ROOT = '.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source'
TX_BASE_ROOT = '.local/temporal-native/verification/TP07-L0-tools-v3/approved-EF-source'
TX_TEMPLATE_REF = ('.local/temporal-native/verification/TP07-L0-tools-v3/source-template-manifest.json', '2a3affe25bc6ec3b0624e0a6d4a4ab3202827929a45a39f525d2f023fe5b29ba')
TX_HOST_LOCK = 'test/TP07LiteralProbe/packages.lock.json'
TX_UNIT_LOCK = 'test/W.EntityFrameworkCore.Dameng.Tests/packages.lock.json'
TX_GENERATED = ('src/W.EntityFrameworkCore.Dameng/bin/', 'src/W.EntityFrameworkCore.Dameng/obj/', 'test/TP07LiteralProbe/bin/', 'test/TP07LiteralProbe/obj/')

def origin_exact(actual, expected, code):
    need(type(actual) is type(expected), code + '_type')
    if isinstance(expected, dict):
        need(set(actual) == set(expected), code + '_keys')
        for key in expected: origin_exact(actual[key], expected[key], code)
    elif isinstance(expected, (list, tuple)):
        need(len(actual) == len(expected), code + '_length')
        for left, right in zip(actual, expected): origin_exact(left, right, code)
    else:
        need(actual == expected, code)


def origin_relative(name):
    need(type(name) is str and name and '\\' not in name and '\x00' not in name
        and not Path(name).is_absolute() and all(part not in ('', '.', '..') for part in name.split('/')), 'origin_relative_path')
    return name


def origin_map(value):
    need(type(value) is dict, 'origin_source_map')
    for name, digest in value.items():
        origin_relative(name)
        need(type(digest) is str and re.fullmatch(r'[a-f0-9]{64}', digest), 'origin_source_hash')
    return value


def origin_path(path, root, file=True):
    need(type(path) is str and Path(path).is_absolute() and '\x00' not in path, 'origin_absolute_path')
    target = Path(path); root = Path(root)
    need('..' not in target.parts and str(target) == str(target.resolve())
        and target.is_relative_to(root) and not any(part.is_symlink() for part in (target, *target.parents)), 'origin_path_boundary')
    need(target.is_file() if file else target.is_dir(), 'origin_missing_path')
    return target


def origin_refs(value):
    result = []
    if type(value) is dict:
        if 'path' in value or 'sha256' in value:
            need(set(value) == {'path', 'sha256'} and type(value['path']) is str
                and type(value['sha256']) is str and re.fullmatch(r'[a-f0-9]{64}', value['sha256']), 'origin_nested_ref_shape')
            result.append(value)
        else:
            for child in value.values(): result.extend(origin_refs(child))
    elif type(value) is list:
        for child in value: result.extend(origin_refs(child))
    return result


def transaction_product_semantics(product, approval, base, prepared_base, patch_text, root):
    """Pure loaded-document contract; physical Ref checks precede this call."""
    need(type(product) is dict and set(product) == set(TX_PRODUCT_KEYS), 'transaction_product_fields')
    for key, expected in TX_PRODUCT_FIXED.items(): origin_exact(product[key], expected, 'transaction_' + key)
    origin_exact(product['W'], W, 'transaction_W')
    origin_exact(product['source_root'], str(Path(root) / TX_SOURCE_ROOT), 'transaction_source_root')
    expected_refs = sorted((str(Path(root) / name), digest) for name, digest in TX_NESTED_REFS)
    actual_refs = sorted((row['path'], row['sha256']) for row in origin_refs(product))
    origin_exact(actual_refs, expected_refs, 'transaction_nested_refs')
    need(type(base) is dict and set(base) == {'M1_execution_summary_sha256','execution_source_manifest_sha256',
        'original_unit_lock_sha256','schema_version','source_hashes','task','unit_lock_sha256'}, 'transaction_base_fields')
    origin_exact(base['schema_version'], 1, 'transaction_base_schema')
    old = origin_map(base['source_hashes']); current = origin_map(product['source_hashes'])
    need(len(old) == 175, 'transaction_base175')
    origin_exact(product['base_source_hashes'], old, 'transaction_base_map')
    origin_exact(prepared_base['approved_ef_source_hashes'], old, 'transaction_prepared_base_map')
    origin_exact(base['unit_lock_sha256'], old[TX_UNIT_LOCK], 'transaction_old_unit_lock')
    origin_exact(product['base_lineage'], TX_BASE_LINEAGE, 'transaction_base_lineage')
    for key, value in TX_BASE_LINEAGE.items(): origin_exact(prepared_base['lineage'][key], value, 'transaction_prepared_base_lineage')
    added = sorted(set(current) - set(old)); removed = sorted(set(old) - set(current))
    modified = sorted(name for name in old if name in current and old[name] != current[name])
    need(not removed and len(current) == 178, 'transaction_current178')
    origin_exact(added, list(TX_ADDED), 'transaction_added_files')
    origin_exact(modified, list(TX_MODIFIED), 'transaction_modified_files')
    origin_exact(product['added_files'], added, 'transaction_declared_added')
    origin_exact(product['modified_files'], modified, 'transaction_declared_modified')
    need(sum(old[name] == current[name] for name in old) == 173, 'transaction_unchanged173')
    patch_paths = sorted([line[6:] for line in patch_text.splitlines() if line.startswith('+++ b/')])
    origin_exact(patch_paths, sorted(list(TX_ADDED) + list(TX_MODIFIED)), 'transaction_signed_patch_paths')
    origin_exact(product['patch_paths'], patch_paths, 'transaction_declared_patch_paths')
    aggregate = ''.join(current[name] + '  ' + name + '\n' for name in sorted(current))
    origin_exact(hashlib.sha256(aggregate.encode()).hexdigest(), TX_PRODUCT_FIXED['source_aggregate_sha256'], 'transaction_aggregate')
    expected_approval = dict(schema_version=1, task='TP07-T1-overlay-source-approval', status='source_approved',
        scope=TX_PRODUCT_FIXED['required_source_approval_scope'], exact_count=178, exact_paths=sorted(current),
        manifest=dict(path=str(Path(root) / TX_REFS['transaction_product_manifest'][0]), sha256=TX_REFS['transaction_product_manifest'][1]))
    origin_exact(approval, expected_approval, 'transaction_source_approval')
    return current


def origin_inventory(folder, generated=False):
    folder = Path(folder)
    origin_path(str(folder), ROOT, file=False)
    result = {}
    for path in sorted(folder.rglob('*')):
        need(not path.is_symlink(), 'origin_source_symlink')
        if not path.is_file(): continue
        name = str(path.relative_to(folder))
        if generated and any(name.startswith(prefix) for prefix in TX_GENERATED): continue
        origin_relative(name); result[name] = sha(path)
    return result


def transaction_product_inputs(references=None):
    root = ROOT
    expected = {key: dict(path=str(root / path), sha256=digest) for key, (path, digest) in TX_REFS.items()}
    if references is not None: origin_exact(references, expected, 'transaction_input_refs')
    paths = []
    def checked(row):
        path = origin_path(row['path'], root)
        origin_exact(sha(path), row['sha256'], 'transaction_ref_hash'); paths.append(path)
        return path
    product = read(checked(expected['transaction_product_manifest']))
    approval = read(checked(expected['transaction_source_approval']))
    patch_text = checked(expected['transaction_patch']).read_text()
    # Fixed nested references are hashed as bytes, including TRX/archive/patch.
    origin_exact(sorted((x['path'],x['sha256']) for x in origin_refs(product)),
        sorted((str(root / path),digest) for path,digest in TX_NESTED_REFS), 'transaction_nested_refs')
    for row in origin_refs(product): checked(row)
    base = read(checked(product['base_approved_source']))
    prepared_base = read(checked(product['base_prepared_source']))
    current = transaction_product_semantics(product, approval, base, prepared_base, patch_text, root)
    old_root = origin_path(str(root / TX_BASE_ROOT), root, file=False)
    current_root = origin_path(product['source_root'], root, file=False)
    origin_exact(origin_inventory(old_root), base['source_hashes'], 'transaction_physical175')
    origin_exact(origin_inventory(current_root), current, 'transaction_physical178')
    paths += [old_root / name for name in base['source_hashes']] + [current_root / name for name in current]
    template_ref = dict(path=str(root / TX_TEMPLATE_REF[0]),sha256=TX_TEMPLATE_REF[1])
    templates = read(checked(template_ref))
    origin_exact(templates['source_hashes'], POLICY['literal_template_source_hashes'], 'transaction_frozen_templates')
    for name, digest in templates['source_hashes'].items():
        path = origin_path(str(HERE / 'runtime-templates' / name), root)
        origin_exact(sha(path), digest, 'transaction_template_bytes'); paths.append(path)
    # Preserve historical unit-lock provenance against the signed mapping layer.
    mapping = read(product['base_lineage_artifacts']['mapping_manifest']['path'])
    origin_exact(base['original_unit_lock_sha256'], mapping['after_source_hashes'][TX_UNIT_LOCK], 'transaction_original_unit_lock')
    return dict(product=product,approval=approval,base=base,prepared_base=prepared_base,patch_text=patch_text,
        references=expected,protected_paths=sorted(set(paths)),template_ref=template_ref)


def transaction_source_semantics(source, value, lock=None, startup=None):
    product = value['product']; base = value['base']; root = ROOT
    need(type(source) is dict and set(source) == {'schema_version','task','source_hashes','toolpack','toolpack_check','approved_ef_source_hashes','product_ef_source_hashes','runtime_template_hashes','lineage','W','lineage_artifacts'}, 'transaction_prepared_fields')
    origin_exact(source['schema_version'], 1, 'transaction_prepared_schema')
    origin_exact(source['task'], 'TP07-L0 prepared source', 'transaction_prepared_task')
    origin_exact(source['W'], W, 'transaction_prepared_W')
    origin_exact(source['approved_ef_source_hashes'], base['source_hashes'], 'transaction_prepared175')
    origin_exact(source['product_ef_source_hashes'], product['source_hashes'], 'transaction_prepared178')
    templates = POLICY['literal_template_source_hashes']
    origin_exact(source['runtime_template_hashes'], templates, 'transaction_prepared_templates')
    expected = dict(product['source_hashes'])
    for name, digest in templates.items():
        target = 'test/' + origin_relative(name)
        need(target not in expected, 'transaction_template_overlap'); expected[target] = digest
    need(len(expected) == 185 and TX_HOST_LOCK not in expected, 'transaction_prepared185')
    origin_exact(source['source_hashes'], expected, 'transaction_prepared_closure')
    lineage = dict(TX_BASE_LINEAGE, runtime_template_source_sha256=value['template_ref']['sha256'],
        transaction_product_manifest_sha256=TX_REFS['transaction_product_manifest'][1],
        transaction_patch_sha256=TX_REFS['transaction_patch'][1], transaction_source_approval_sha256=TX_REFS['transaction_source_approval'][1])
    origin_exact(source['lineage'], lineage, 'transaction_lineage10')
    artifacts = dict(product['base_lineage_artifacts'], approved_ef_source_manifest=product['base_approved_source'],
        runtime_template_source=value['template_ref'], **value['references'])
    origin_exact(source['lineage_artifacts'], artifacts, 'transaction_artifacts10')
    if lock is not None:
        need(type(lock) is dict and {'lineage','lock_sha256','source_hashes','provider_lock_sha256'} <= set(lock), 'transaction_lock_fields')
        origin_exact(lock['lineage'], lineage, 'transaction_lock_lineage')
        need(type(lock['lock_sha256']) is str and re.fullmatch(r'[a-f0-9]{64}', lock['lock_sha256']), 'transaction_host_lock_hash')
        expected[TX_HOST_LOCK] = lock['lock_sha256']
        origin_exact(lock['source_hashes'], expected, 'transaction_locked186')
        origin_exact(lock['provider_lock_sha256'], product['source_hashes']['src/W.EntityFrameworkCore.Dameng/packages.lock.json'], 'transaction_provider_lock')
    if startup is not None:
        need(type(startup) is dict and {'lineage','lineage_artifacts','source_hashes'} <= set(startup), 'transaction_startup_fields')
        need(lock is not None, 'transaction_startup_requires_lock')
        origin_exact(startup['lineage'], lineage, 'transaction_startup_lineage')
        origin_exact(startup['lineage_artifacts'], artifacts, 'transaction_startup_artifacts')
        origin_exact(startup['source_hashes'], expected, 'transaction_startup186')
    return expected


def transaction_source_contract(source, lock=None, startup=None, replay=None, generated=False):
    value = transaction_product_inputs()
    expected = transaction_source_semantics(source, value, lock, startup)
    if replay is not None: origin_exact(origin_inventory(replay,generated), expected, 'transaction_replay_projection')
    return value


def transaction_origin_hashes(value):
    return {str(path): sha(path) for path in value['protected_paths']}


def transaction_origin_protection(before, after, value):
    expected = transaction_origin_hashes(value)
    need(type(before) is dict and type(after) is dict and all(before.get(path) == digest and after.get(path) == digest for path,digest in expected.items()), 'transaction_origin_protection')
    return expected


def transaction_prepare_commands(receipt, source, value, replay):
    patches = [source['lineage_artifacts'][name]['path'] for name in ('bridge_patch','mapping_patch','transaction_patch')]
    rows = receipt['overlay_commands']; need(type(rows) is list and len(rows) == 6, 'transaction_six_git_commands')
    for ordinal, (patch, check) in enumerate((patch,check) for patch in patches for check in (True,False)):
        row = rows[ordinal]
        origin_exact(row['command'], ['git','apply'] + (['--check'] if check else []) + [patch], 'transaction_git_command')
        origin_exact(row['cwd'], str(replay), 'transaction_git_cwd')
        origin_exact(row['exit_code'], 0, 'transaction_git_exit')
        origin_exact(row['host_exit_code'], 0, 'transaction_git_host_exit')
        origin_exact(row['timed_out'], False, 'transaction_git_timeout')
        origin_exact(row['termination_code'], None, 'transaction_git_termination')
        log = Path(replay).parent / (('bridge','mapping','transaction')[ordinal//2] + ('-check' if check else '-apply') + '.log')
        origin_path(str(log), ROOT); origin_exact(sha(log), row['log_sha256'], 'transaction_git_log_hash')
    return True


def prepare(a):
    toolpack, before = toolpack_entry(a)
    root = Path(a.workspace_root).resolve()
    need(a.source_approved, 'mapping_unit_source_acceptance_required')
    origin_exact(root, ROOT, 'transaction_workspace')
    refs = {key:dict(path=getattr(a,path,None),sha256=getattr(a,digest,None)) for key,path,digest in [('transaction_product_manifest','product_manifest','product_manifest_sha256'),('transaction_patch','transaction_patch','transaction_patch_sha256'),('transaction_source_approval','product_source_approval','product_source_approval_sha256')]}
    origin_value = transaction_product_inputs(refs); origin_before = transaction_origin_hashes(origin_value)
    origin_exact(a.approved_source, str(ROOT / TX_BASE_ROOT), 'transaction_old_source_argument')
    origin_exact(a.approved_source_manifest, origin_value['product']['base_approved_source']['path'], 'transaction_old_manifest_argument')
    origin_exact(a.approved_source_manifest_sha256, origin_value['product']['base_approved_source']['sha256'], 'transaction_old_manifest_SHA')
    for name in ('base_archive','bridge_manifest','bridge_patch','mapping_manifest','mapping_patch'):
        origin_exact(getattr(a,name), origin_value['product']['base_lineage_artifacts'][name]['path'], 'transaction_old_overlay_argument')
    origin_exact(a.template_manifest, origin_value['template_ref']['path'], 'transaction_template_manifest_argument')
    origin_exact(a.template_manifest_sha256, origin_value['template_ref']['sha256'], 'transaction_template_manifest_SHA')
    origin_exact(a.templates, str(HERE / 'runtime-templates'), 'transaction_template_root_argument')
    source = Path(a.approved_source).resolve(); manifest = read(a.approved_source_manifest)
    need(sha(a.approved_source_manifest) == a.approved_source_manifest_sha256, 'approved_source_manifest_pin')
    approved = manifest['source_hashes']; check_source(source, approved)
    bridge = read(a.bridge_manifest); mapping = read(a.mapping_manifest)
    need(sha(a.bridge_manifest) == a.bridge_manifest_sha256 and sha(a.mapping_manifest) == a.mapping_manifest_sha256, 'overlay_manifest_pins')
    need(sha(a.base_archive) == POLICY['ef']['archive_sha256'], 'base_archive_pin')
    need(sha(a.bridge_patch) == bridge['bridge_patch_sha256'] and sha(a.mapping_patch) == mapping['patch_sha256'], 'patch_pins')
    need(mapping['base_commit'] == POLICY['ef']['commit'], 'EF_commit_pin')
    output = fresh(a.output, root)
    replay = output / 'replay'; replay.mkdir()
    git_environment = public_env(output).copy()
    git_environment['GIT_CEILING_DIRECTORIES'] = str(replay.parent)
    commands = []
    with tarfile.open(a.base_archive) as archive:
        for member in archive.getmembers():
            relative = Path(member.name)
            need(not relative.is_absolute() and '..' not in relative.parts and not member.issym() and not member.islnk(), 'unsafe_archive_member')
            if member.isdir(): (replay / relative).mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                target = replay / relative; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(archive.extractfile(member).read()); target.chmod(member.mode)
    check_source(replay, bridge['base_source_hashes'])
    for label, patch, expected in [('bridge', a.bridge_patch, bridge['bridge_source_hashes']), ('mapping', a.mapping_patch, mapping['after_source_hashes'])]:
        if label == 'mapping':
            for name, digest in mapping['base_source_hashes'].items():
                path = replay / name
                if not path.exists() or sha(path) != digest:
                    need(name in (PROVIDER_LOCK, 'test/TP07MappingObservation/packages.lock.json') and sha(source / name) == digest, 'P1_lock_overlay_only')
                    path.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source / name, path)
            check_source(replay, mapping['base_source_hashes'])
        for check in (True, False):
            command = ['git', 'apply'] + (['--check'] if check else []) + [str(Path(patch).resolve())]
            row = process(command, replay, output / (label + ('-check' if check else '-apply') + '.log'), git_environment, 30)
            commands.append(row); need(row['exit_code'] == 0 and not row['timed_out'], 'exact_overlay_replay')
        check_source(replay, expected)
    unit_lock = 'test/W.EntityFrameworkCore.Dameng.Tests/packages.lock.json'
    for name, digest in approved.items():
        if sha(replay / name) != digest:
            need(name == unit_lock, 'approved_unit_lock_overlay_only'); shutil.copyfile(source / name, replay / name)
    check_source(replay, approved)
    for check in (True,False):
        command = ['git','apply'] + (['--check'] if check else []) + [a.transaction_patch]
        row = process(command,replay,output / ('transaction-check.log' if check else 'transaction-apply.log'),git_environment,30)
        commands.append(row); need(type(row['exit_code']) is int and row['exit_code']==0 and row['timed_out'] is False and row['termination_code'] is None, 'exact_transaction_replay')
    product = origin_value['product']; check_source(replay,product['source_hashes'])
    transaction_product_inputs(refs)
    templates = Path(a.templates).resolve()
    template_hashes = {name: sha(templates / name) for name in L0_FILES}
    need(template_hashes == POLICY['literal_template_source_hashes'], 'exact_frozen_first7_L0_source')
    need(sha(a.template_manifest) == a.template_manifest_sha256 and read(a.template_manifest)['source_hashes'] == template_hashes, 'reviewed_L0_template_closure')
    for name in L0_FILES:
        target = replay / 'test' / name
        need(not target.exists(), 'new_L0_template_only'); target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(templates / name, target)
    source_hashes = inv(replay)
    lineage = dict(ef_commit=POLICY['ef']['commit'], base_archive_sha256=POLICY['ef']['archive_sha256'],
        bridge_patch_sha256=bridge['bridge_patch_sha256'], mapping_manifest_sha256=a.mapping_manifest_sha256,
        mapping_patch_sha256=mapping['patch_sha256'], approved_ef_source_manifest_sha256=a.approved_source_manifest_sha256,
        runtime_template_source_sha256=a.template_manifest_sha256,
        transaction_product_manifest_sha256=a.product_manifest_sha256, transaction_patch_sha256=a.transaction_patch_sha256,
        transaction_source_approval_sha256=a.product_source_approval_sha256)
    toolcheck = toolpack_exit(toolpack, before, 'prepare')
    prepared_document = dict(schema_version=1, task='TP07-L0 prepared source', source_hashes=source_hashes,
        toolpack=toolpack, toolpack_check=toolcheck,
        approved_ef_source_hashes=approved, product_ef_source_hashes=product['source_hashes'], runtime_template_hashes=template_hashes, lineage=lineage, W=W,
        lineage_artifacts={name: {'path': str(Path(path).resolve()), 'sha256': sha(path)} for name, path in {
            'base_archive': a.base_archive, 'bridge_manifest': a.bridge_manifest, 'bridge_patch': a.bridge_patch,
            'mapping_manifest': a.mapping_manifest, 'mapping_patch': a.mapping_patch,
            'approved_ef_source_manifest': a.approved_source_manifest, 'runtime_template_source': a.template_manifest,
            'transaction_product_manifest': a.product_manifest, 'transaction_patch': a.transaction_patch,
            'transaction_source_approval': a.product_source_approval}.items()})
    current_origin = transaction_source_contract(prepared_document,replay=replay)
    transaction_origin_protection(origin_before,transaction_origin_hashes(current_origin),current_origin)
    transaction_prepare_commands(dict(overlay_commands=commands),prepared_document,current_origin,replay)
    write(output / 'source-manifest.json',prepared_document)
    write(output / 'prepare-receipt.json', dict(schema_version=1, source_manifest_sha256=sha(output / 'source-manifest.json'),
        toolpack=toolpack, toolpack_check=toolpack_exit(toolpack, before, 'prepare'),
        overlay_commands=commands, independent_replay_equal=True, SDK_executed=False, database_accessed=False))


def offline(a):
    toolpack, before = toolpack_entry(a)
    need(a.source_approved, 'offline_source_approval_required')
    prepared = Path(a.prepared).resolve(); manifest = read(prepared / 'source-manifest.json')
    need(sha(prepared / 'source-manifest.json') == a.prepared_manifest_sha256, 'prepared_manifest_pin')
    toolpack_binding(manifest, toolpack, 'prepare')
    origin_value = transaction_source_contract(manifest,replay=prepared / 'replay')
    origin_before = transaction_origin_hashes(origin_value)
    receipts = {'prepare': artifact(prepared / 'prepare-receipt.json')}
    prepare_receipt = phase_receipt(receipts['prepare'], toolpack, 'prepare', a.prepared_manifest_sha256)
    transaction_prepare_commands(prepare_receipt,manifest,origin_value,prepared / 'replay')
    lock_manifest = None
    if a.phase == 'build':
        need(a.locks_reviewed and sha(a.lock_manifest) == a.lock_manifest_sha256, 'reviewed_L0_locks_required')
        lock_manifest = read(a.lock_manifest)
        toolpack_binding(lock_manifest, toolpack, 'lock')
        need(lock_manifest['source_manifest_sha256'] == a.prepared_manifest_sha256
            and lock_manifest['lineage'] == manifest['lineage'], 'lock_source_lineage')
        need(lock_manifest['phase_receipts']['prepare'] == receipts['prepare'], 'lock_prepare_receipt_binding')
        receipts['lock'] = lock_manifest['phase_receipts']['lock']
        phase_receipt(receipts['lock'], toolpack, 'lock', a.prepared_manifest_sha256)
        transaction_source_semantics(manifest,origin_value,lock_manifest)
    check_source(prepared / 'replay', manifest['source_hashes'])
    output = fresh(a.output, a.workspace_root)
    work = output / 'replay'; shutil.copytree(prepared / 'replay', work)
    for name in ('cli-home', 'http-cache'): (output / name).mkdir()
    cache = output / 'nuget-packages'; shutil.copytree(a.cache_source, cache)
    feed = Path(a.feed_source).resolve()
    need(sha(feed / 'candidate-feed' / ('W.DmProvider.' + W['version'] + '.nupkg')) == W['package_sha256'], 'offline_exact_candidate_feed')
    config = ET.Element('configuration'); sources = ET.SubElement(config, 'packageSources'); ET.SubElement(sources, 'clear')
    for name in ('candidate', 'baseline'): ET.SubElement(sources, 'add', key=name, value=str(feed / (name + '-feed')))
    mappings = ET.SubElement(config, 'packageSourceMapping'); ET.SubElement(ET.SubElement(mappings, 'packageSource', key='candidate'), 'package', pattern='W.DmProvider')
    baseline = ET.SubElement(mappings, 'packageSource', key='baseline')
    old_provider = read(work / PROVIDER_LOCK)['dependencies']['net10.0']
    need(a.cache_proof is not None and sha(a.cache_proof) == a.cache_proof_sha256, 'verified_cache_proof_required')
    proof = read(a.cache_proof)
    proof_records = proof['non_driver_records']
    for name, old in old_provider.items():
        if name == 'W.DmProvider': continue
        rows = [row for row in proof_records if row['id'] == name]
        need(len(rows) == 1, 'cache_package_proof')
        pin = rows[0]; folder = cache / name.lower() / old['resolved']
        need(pin['version'] == old['resolved'] and sha(folder / (name.lower() + '.' + old['resolved'] + '.nupkg')) == pin['archive_sha256'], 'cache_archive_pin')
        need(read(folder / '.nupkg.metadata')['contentHash'] == old['contentHash'], 'verified_cache_contentHash')
    for name in old_provider:
        if name != 'W.DmProvider': ET.SubElement(baseline, 'package', pattern=name)
    config_path = output / 'restore.config'; ET.ElementTree(config).write(config_path, encoding='utf-8', xml_declaration=True)
    if a.phase == 'build':
        lock_source = Path(a.lock_source).resolve()
        origin_path(str(lock_source / L0_LOCK),ROOT)
        origin_exact(sha(lock_source / L0_LOCK),lock_manifest['lock_sha256'],'transaction_reviewed_host_lock')
        shutil.copyfile(lock_source / L0_LOCK,work / L0_LOCK)
    env = public_env(output); host = str(Path.home() / '.dotnet/dotnet'); rows = []
    def source_gate(startup=None):
        projection_lock = lock_manifest
        if a.phase=='lock' and (work / L0_LOCK).exists():
            projection = dict(manifest['source_hashes']); projection[L0_LOCK]=sha(work / L0_LOCK)
            projection_lock = dict(lineage=manifest['lineage'],source_hashes=projection,lock_sha256=projection[L0_LOCK],provider_lock_sha256=manifest['source_hashes'][PROVIDER_LOCK])
        current = transaction_source_contract(manifest,projection_lock,startup,work,generated=True)
        transaction_origin_protection(origin_before,transaction_origin_hashes(current),current)
        return current
    def run(lane, command):
        source_gate(); toolpack_hashes(toolpack)
        row = process(command, work, output / (lane + '.log'), env, 300); rows.append(row)
        source_gate()
        write(output / 'offline-receipt.json', dict(schema_version=1, phase=a.phase, source_manifest_sha256=a.prepared_manifest_sha256,
            toolpack=toolpack, toolpack_check=toolpack_exit(toolpack, before, a.phase),
            commands=rows, database_accessed=False, accepted=False))
        need(row['exit_code'] == 0 and not row['timed_out'], lane)
        return row
    run('sdk', [host, '--version']); need((output / 'sdk.log').read_text().strip() == '10.0.401', 'pinned_sdk')
    if a.phase == 'lock':
        run('prepare-L0-lock', [host, 'restore', L0_PROJECT, '--force-evaluate', '--use-lock-file', '--configfile', str(config_path), '--disable-parallel'] + FLAGS)
        need(sha(work / PROVIDER_LOCK) == manifest['source_hashes'][PROVIDER_LOCK], 'accepted_provider_lock_unchanged')
        lock = read(work / L0_LOCK)['dependencies']['net10.0']
        originals = {name: value for name, value in old_provider.items() if name != 'W.DmProvider'}
        project_ids = [name for name in lock if name.casefold() == 'w.entityframeworkcore.dameng']
        need(len(project_ids) == 1 and lock[project_ids[0]]['type'] == 'Project', 'unique_provider_project')
        need(set(lock) == set(originals) | {'W.DmProvider', project_ids[0]}, 'L0_exact_minimal_graph')
        need(len({name.casefold() for name in lock}) == len(lock), 'duplicate_case_alias')
        verified = []
        for name, old in originals.items():
            folder = cache / name.lower() / old['resolved']
            need(lock[name]['resolved'] == old['resolved'] and lock[name].get('dependencies', {}) == old.get('dependencies', {}), 'non_driver_version_dependencies')
            need(lock[name]['contentHash'] == old['contentHash'] == read(folder / '.nupkg.metadata')['contentHash'], 'native_content_hash_three_way')
            pin = next(row for row in proof_records if row['id'] == name)
            archive = folder / (name.lower() + '.' + old['resolved'] + '.nupkg')
            need(sha(archive) == pin['archive_sha256'] and sha(feed / 'baseline-feed' / archive.name) == pin['archive_sha256'], 'L0_archive_feed_cache_pin')
            verified.append(dict(id=name, version=old['resolved'], archive_sha256=pin['archive_sha256'], fresh_metadata_sha256=sha(folder / '.nupkg.metadata')))
        need(lock['W.DmProvider']['resolved'] == W['version'] and lock['W.DmProvider']['type'] == 'Direct', 'L0_W_version')
        wfolder = cache / 'w.dmprovider' / W['version']
        need(sha(wfolder / ('w.dmprovider.' + W['version'] + '.nupkg')) == W['package_sha256']
            and lock['W.DmProvider']['contentHash'] == old_provider['W.DmProvider']['contentHash'] == read(wfolder / '.nupkg.metadata')['contentHash'], 'L0_W_package_native_hash')
        need(lock[project_ids[0]]['dependencies'] == {'Microsoft.EntityFrameworkCore.Relational': '[10.0.12, 11.0.0)', 'W.DmProvider': '[' + W['version'] + ', ' + W['version'] + ']'}, 'L0_provider_project_dependencies')
        expected = dict(manifest['source_hashes']); expected[L0_LOCK] = sha(work / L0_LOCK)
        for name, digest in expected.items(): need(sha(work / name) == digest, 'lock_source_only_delta')
        toolcheck = toolpack_exit(toolpack, before, 'lock')
        receipts['lock'] = artifact(output / 'offline-receipt.json')
        source_gate()
        write(output / 'lock-manifest.json', dict(schema_version=1, lineage=manifest['lineage'], source_hashes=expected,
            toolpack=toolpack, toolpack_check=toolcheck, phase_receipts=receipts,
            source_manifest_sha256=a.prepared_manifest_sha256, lock_sha256=expected[L0_LOCK], provider_lock_sha256=expected[PROVIDER_LOCK],
            SDK_events_origin=str(output / 'offline-receipt.json'), non_driver_records=verified, reviewed=False))
        return
    check_source(work, lock_manifest['source_hashes'])
    run('locked-restore', [host, 'restore', L0_PROJECT, '--locked-mode', '--configfile', str(config_path), '--disable-parallel'] + FLAGS)
    run('build', [host, 'build', L0_PROJECT, '--no-restore'] + FLAGS)
    for name, digest in lock_manifest['source_hashes'].items(): need(sha(work / name) == digest, 'build_source_lock_drift')
    toolpack_hashes(toolpack)
    # Fixed helper path, hashed as part of this externally approved pack.
    helper_path = HERE / 'managed_identity.py'; helper_bytes = helper_path.read_bytes()
    need(hashlib.sha256(helper_bytes).hexdigest() == toolpack['source_hashes']['tools/DownstreamAcceptance/TP07/managed_identity.py'], 'executed_identity_helper_bytes')
    helper = types.ModuleType('tp07_managed_identity')
    exec(compile(helper_bytes, str(helper_path), 'exec'), helper.__dict__)
    mvid = helper.mvid
    dll = work / 'test/TP07LiteralProbe/bin/Debug/net10.0/TP07LiteralProbe.dll'
    provider = dll.parent / 'W.EntityFrameworkCore.Dameng.dll'
    need(sha(dll.parent / 'W.DmProvider.dll') == W['dll_sha256'], 'build_loaded_candidate_bytes')
    startup = dict(schema_version=1, task='TP07-L0', replay_root=str(work), lineage=manifest['lineage'], W=W,
        toolpack=toolpack, toolpack_check=toolpack_exit(toolpack, before, 'build'), phase_receipts=receipts,
        source_manifest_sha256=a.prepared_manifest_sha256, lock_manifest_sha256=a.lock_manifest_sha256,
        source_manifest_path=str(prepared / 'source-manifest.json'), lock_manifest_path=str(Path(a.lock_manifest).resolve()),
        lineage_artifacts=manifest['lineage_artifacts'],
        source_hashes=lock_manifest['source_hashes'], locks={name: digest for name, digest in lock_manifest['source_hashes'].items() if name.endswith('packages.lock.json')},
        assets={str(path.relative_to(work)): sha(path) for path in sorted(work.rglob('project.assets.json'))},
        ef_provider=dict(name='W.EntityFrameworkCore.Dameng', dll_sha256=sha(provider), mvid=mvid(provider.read_bytes())),
        startup_assemblies=[dict(name='TP07LiteralProbe', dll_sha256=sha(dll), mvid=mvid(dll.read_bytes()), deps_sha256=sha(dll.with_suffix('.deps.json')))])
    need(set(startup['assets']) == {'src/W.EntityFrameworkCore.Dameng/obj/project.assets.json', 'test/TP07LiteralProbe/obj/project.assets.json'}, 'L0_two_project_assets')
    source_gate(startup)
    write(output / 'startup-manifest.json', startup)
    grammar_dir = output / 'literal-grammar'
    grammar_process = run('literal-grammar-selftest', [host, str(dll), '--selftest-literal-grammar', '--output', str(grammar_dir)])
    grammar_process['log_path'] = str(output / 'literal-grammar-selftest.log')
    grammar = dict(process=grammar_process, report=artifact(grammar_dir / 'report.json'))
    check_literal_grammar(grammar, startup)
    source_gate(startup)
    write(output / 'build-receipt.json', dict(schema_version=1, startup_manifest_sha256=sha(output / 'startup-manifest.json'),
        literal_grammar_selftest=grammar,
        toolpack=toolpack, toolpack_check=toolpack_exit(toolpack, before, 'build'), phase_receipts=receipts,
        source_manifest_sha256=a.prepared_manifest_sha256, lock_manifest_sha256=a.lock_manifest_sha256, commands=rows,
        runtime_not_authorized=True, database_accessed=False))


def internal_host(a):
    toolpack, before = toolpack_entry(a)
    need(sha(a.startup_manifest) == a.startup_manifest_sha256, 'internal_startup_manifest_pin')
    toolpack_binding(read(a.startup_manifest), toolpack, 'build')
    output = Path(a.consumer_output).resolve()
    need(not output.exists(), 'fresh_internal_consumer_output')
    env = internal_environment(a, output.parent)
    command = [str(Path.home() / '.dotnet/dotnet'), a.dll, '--profile', a.profile, '--candidate-manifest', a.candidate_manifest,
        '--capabilities', a.capabilities, '--repo', a.secret_repo, '--build-manifest', a.startup_manifest,
        '--build-manifest-sha256', a.startup_manifest_sha256, '--output', a.consumer_output]
    if a.cleanup: command += ['--cleanup', '--ledger', a.ledger]
    toolpack_hashes(toolpack)
    # Retain the outer process group: the runner's deadline kills host and child.
    exit_code = subprocess.call(command, env=env)
    check = toolpack_exit(toolpack, before, 'internal-host')
    need(sha(a.startup_manifest) == a.startup_manifest_sha256, 'internal_startup_after_drift')
    output.mkdir(parents=True, exist_ok=True)
    write(output / 'toolpack-host-receipt.json', dict(schema_version=1, toolpack=toolpack, toolpack_check=check,
        profile=a.profile, cleanup=a.cleanup, startup_manifest_sha256=a.startup_manifest_sha256, child_exit_code=exit_code))
    return exit_code


def internal_environment(a, stage):
    env = public_env(stage)
    Path(env['DOTNET_CLI_HOME']).mkdir(parents=True, exist_ok=True)
    key = 'DAMENG_TLS_TEST_CONNECTION_STRING' if a.profile == 'tls' else 'DAMENG_TEST_CONNECTION_STRING'
    need(bool(os.environ.get(key)), 'TEST_wrapper_variable_required')
    env['DAMENG_TEST_CONNECTION_STRING'] = os.environ[key]  # In-process only; never serialized or argv.
    env.update(TP07_CANDIDATE_MANIFEST=a.candidate_manifest, TP07_CAPABILITIES=a.capabilities,
        TP07_PROFILE=a.profile, TP07_ORIGINAL_REPO=a.secret_repo,
        TP07_STARTUP_MANIFEST=a.startup_manifest, TP07_STARTUP_MANIFEST_SHA256=a.startup_manifest_sha256)
    return env


def run_runtime(a):
    toolpack, tool_before = toolpack_entry(a)
    need(a.runtime_approved and sha(a.startup_manifest) == a.startup_manifest_sha256, 'reviewed_startup_manifest_required')
    need(sha(a.candidate_manifest) == POLICY['candidate_identity_sha256'] and sha(a.capabilities) == POLICY['capabilities_sha256'], 'same_candidate_caps')
    startup = read(a.startup_manifest); replay = Path(startup['replay_root']).resolve()
    toolpack_binding(startup, toolpack, 'build')
    source_path = origin_path(startup['source_manifest_path'],ROOT); lock_path = origin_path(startup['lock_manifest_path'],ROOT)
    origin_exact(sha(source_path),startup['source_manifest_sha256'],'transaction_runtime_source_pin')
    origin_exact(sha(lock_path),startup['lock_manifest_sha256'],'transaction_runtime_lock_pin')
    source_manifest = read(source_path); lock_manifest = read(lock_path)
    origin_value = transaction_source_contract(source_manifest,lock_manifest,startup,replay,generated=True)
    phase_receipts = dict(startup['phase_receipts'])
    phase_receipts['build'] = artifact(Path(a.startup_manifest).resolve().parent / 'build-receipt.json')
    for phase in ('prepare', 'lock', 'build'):
        phase_document = phase_receipt(phase_receipts[phase], toolpack, phase, startup['source_manifest_sha256'])
        if phase == 'prepare': transaction_prepare_commands(phase_document,source_manifest,origin_value,source_path.parent / 'replay')
        if phase == 'build':
            need(phase_document['startup_manifest_sha256'] == a.startup_manifest_sha256
                and phase_document['lock_manifest_sha256'] == startup['lock_manifest_sha256'], 'runtime_build_receipt_binding')
            grammar_paths = check_literal_grammar(phase_document['literal_grammar_selftest'], startup)
    for key in ('source_hashes', 'locks', 'assets'):
        for name, digest in startup[key].items(): need(sha(replay / name) == digest, 'startup_input_drift')
    output = fresh(a.output, a.workspace_root)
    paths = [Path(a.startup_manifest), Path(a.candidate_manifest), Path(a.capabilities), Path(startup['source_manifest_path']), Path(startup['lock_manifest_path'])]
    paths += [Path(row['path']) for row in startup['lineage_artifacts'].values()]
    paths += origin_value['protected_paths']
    paths += [Path(toolpack['manifest_path'])] + [ROOT / name for name in TOOLPACK_FILES]
    paths += [Path(row['path']) for row in phase_receipts.values()]
    paths += grammar_paths
    paths += [replay / name for name in set(startup['source_hashes']) | set(startup['locks']) | set(startup['assets'])]
    binary_dir = replay / 'test/TP07LiteralProbe/bin/Debug/net10.0'
    paths += [binary_dir / name for name in ('TP07LiteralProbe.dll', 'TP07LiteralProbe.deps.json', 'W.EntityFrameworkCore.Dameng.dll', 'W.DmProvider.dll')]
    before = {str(path): sha(path) for path in paths}
    receipt = dict(schema_version=1, task='TP07-L0 runtime', status='running', required_profiles=['shared', 'tls'], profiles={},
        toolpack=toolpack, phase_receipts=phase_receipts,
        startup_manifest_path=str(Path(a.startup_manifest).resolve()), startup_manifest_sha256=a.startup_manifest_sha256,
        input_hashes_before=before, literal_runtime_accepted=False, full_EF_C_accepted=False)
    def save(): write(output / 'run-receipt.json', receipt)
    save()
    for profile in ('shared', 'tls'):
        stage = output / profile; stage.mkdir()
        wrapper = Path(a.secret_repo).resolve() / 'scripts' / ('with-dameng-tls-test.sh' if profile == 'tls' else 'with-dameng-test.sh')
        expected_wrapper = {'shared': 'b2af867921b567045fdeeccb3da490662661976b182e5884d029a738d41e00d8', 'tls': '872d722808a7f53415505e2536438d1b644793822b2abeac3cb5b662ac94d706'}[profile]
        need(sha(wrapper) == expected_wrapper, 'original_TEST_wrapper_pin')
        common = [str(wrapper), sys.executable, str(Path(__file__).resolve()), 'internal-host', '--profile', profile,
            '--dll', str(replay / 'test/TP07LiteralProbe/bin/Debug/net10.0/TP07LiteralProbe.dll'),
            '--candidate-manifest', str(Path(a.candidate_manifest).resolve()), '--capabilities', str(Path(a.capabilities).resolve()),
            '--secret-repo', str(Path(a.secret_repo).resolve()), '--startup-manifest', str(Path(a.startup_manifest).resolve()),
            '--startup-manifest-sha256', a.startup_manifest_sha256,
            '--toolpack-manifest', toolpack['manifest_path'], '--toolpack-manifest-sha256', toolpack['manifest_sha256']]
        current = transaction_source_contract(source_manifest,lock_manifest,startup,replay,generated=True)
        transaction_origin_protection(before,{str(path):sha(path) for path in paths},current)
        toolpack_hashes(toolpack)
        main = process(common + ['--consumer-output', str(stage / 'main')], replay, stage / 'main.log', public_env(output), 300)
        row = receipt['profiles'][profile] = dict(main=main, cleanup=None)
        report_path = stage / 'main/report.json'; ledger_path = stage / 'main/ownership.json'
        if report_path.exists(): main.update(report_path=str(report_path.relative_to(output)), report_sha256=sha(report_path))
        if ledger_path.exists():
            current = transaction_source_contract(source_manifest,lock_manifest,startup,replay,generated=True)
            transaction_origin_protection(before,{str(path):sha(path) for path in paths},current)
            toolpack_hashes(toolpack)
            cleanup = process(common + ['--consumer-output', str(stage / 'cleanup'), '--cleanup', '--ledger', str(ledger_path)], replay,
                stage / 'cleanup.log', public_env(output), 180)
            row['cleanup'] = cleanup; cleanup_report = stage / 'cleanup/report.json'
            if cleanup_report.exists(): cleanup.update(report_path=str(cleanup_report.relative_to(output)), report_sha256=sha(cleanup_report))
            row.update(ledger_path=str(ledger_path.relative_to(output)), ledger_sha256=sha(ledger_path))
        for role in ('main', 'cleanup'):
            host_receipt = stage / role / 'toolpack-host-receipt.json'
            if row[role] is not None and host_receipt.exists():
                row[role].update(toolpack_host_receipt_path=str(host_receipt.relative_to(output)), toolpack_host_receipt_sha256=sha(host_receipt))
        receipt['toolpack_check'] = toolpack_exit(toolpack, tool_before, 'run')
        current = transaction_source_contract(source_manifest,lock_manifest,startup,replay,generated=True)
        receipt['input_hashes_after'] = {str(path): sha(path) for path in paths}
        transaction_origin_protection(before,receipt['input_hashes_after'],current); save()
        good = main['exit_code'] == 0 and not main['timed_out'] and row['cleanup'] is not None and row['cleanup']['exit_code'] == 0 and not row['cleanup']['timed_out']
        if not good:
            receipt['status'] = 'profile_process_or_cleanup_failed'; save()
        need(good, 'profile_process_or_cleanup_failed')
    receipt.update(status='l0_profiles_complete', input_hashes_after={str(path): sha(path) for path in paths},
        toolpack_check=toolpack_exit(toolpack, tool_before, 'run')); save()


def main():
    p = argparse.ArgumentParser()
    p.add_argument('phase', choices=('prepare', 'lock', 'build', 'run', 'internal-host'))
    p.add_argument('--toolpack-manifest', required=True)
    p.add_argument('--toolpack-manifest-sha256', required=True)
    for key in ('workspace-root', 'output', 'approved-source', 'approved-source-manifest', 'approved-source-manifest-sha256',
            'base-archive', 'bridge-manifest', 'bridge-manifest-sha256', 'bridge-patch', 'mapping-manifest', 'mapping-manifest-sha256',
            'mapping-patch', 'templates', 'template-manifest', 'template-manifest-sha256', 'prepared', 'prepared-manifest-sha256',
            'cache-source', 'cache-proof', 'cache-proof-sha256', 'feed-source', 'lock-manifest', 'lock-manifest-sha256', 'lock-source', 'startup-manifest',
            'product-manifest', 'product-manifest-sha256', 'transaction-patch', 'transaction-patch-sha256',
            'product-source-approval', 'product-source-approval-sha256',
            'startup-manifest-sha256', 'candidate-manifest', 'capabilities', 'secret-repo', 'profile', 'dll', 'consumer-output', 'ledger'):
        p.add_argument('--' + key)
    for key in ('source-approved', 'locks-reviewed', 'runtime-approved', 'cleanup'): p.add_argument('--' + key, action='store_true')
    a = p.parse_args()
    try:
        if a.phase == 'prepare': prepare(a)
        elif a.phase in ('lock', 'build'): offline(a)
        elif a.phase == 'run': run_runtime(a)
        else: return internal_host(a)
        return 0
    except Exception as error:
        print('TP07_L0_stopped_' + (str(error) if isinstance(error, Stop) else type(error).__name__))
        return 1


if __name__ == '__main__': raise SystemExit(main())
