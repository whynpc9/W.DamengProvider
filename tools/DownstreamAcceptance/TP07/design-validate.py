#!/usr/bin/env python3
"""Finite D checkpoint/source/CLI/guard evidence validation; no SDK or DB calls."""
import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import sys
import types
import uuid
import xml.etree.ElementTree as ET
import zipfile

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PREFIX = 'tools/DownstreamAcceptance/TP07/'
NEW = tuple(PREFIX + name for name in ('design-run.py', 'design-validate.py', 'design-cases.json', 'test_design_contracts.py'))

TRANSACTION_PINS = {'transaction_product_manifest_sha256': '400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8', 'transaction_patch_sha256': 'd13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f', 'transaction_source_approval_sha256': '66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f'}
L0_SOURCE_SHA = '4f50b6188f13b7718f6dbabdc853ab63868c6ac5a36456cf690d012e91695faf'
L0_LOCK_SHA = '6982a1b12187c1f9ed18810222b5d8347528d4d758822b6697f8f4b5b074ecbb'
DESIGN_SHA = '261661b1d22789e778f42f50f2bd0e6fcc3d6f4b71732ef1166ee2489114caea'
PRODUCT_FIELDS = frozenset(('EF_relational_API', 'SDK_executed', 'W', 'added_files', 'aggregate_format', 'base_approved_source', 'base_lineage', 'base_lineage_artifacts', 'base_prepared_source', 'base_source_count', 'base_source_hashes', 'database_executed', 'full_unit_inventory', 'modified_files', 'namespace_boundary', 'old_L0_E_D_cohorts_reused_as_new_acceptance', 'old_parent_source_approval_reused_for_revision', 'patch_application_base', 'patch_paths', 'planned_full_unit_cases', 'planned_retry_unit_cases', 'planned_retry_unit_methods', 'planned_transaction_unit_cases', 'planned_unit_cases', 'planned_unit_methods', 'required_next_gates', 'required_source_approval_scope', 'retry_contract_inventory', 'revision', 'schema_version', 'source_aggregate_sha256', 'source_count', 'source_hashes', 'source_root', 'static_checks_only', 'status', 'task', 'tests_executed', 'transaction_capability_patch', 'transaction_probe_or_E_observation_implemented', 'unchanged_base_file_count', 'unit_delta_from_parent', 'unit_revision_parent'))
LINEAGE_ARTIFACTS = {'approved_ef_source_manifest': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/approved-EF-source-manifest.json', 'sha256': 'dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6'}, 'base_archive': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/base.tar', 'sha256': '668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8'}, 'bridge_manifest': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/bridge-manifest.json', 'sha256': '4750a330f2f1e63a5e40780acf19820b38b86db2c1bce08ff224600a18b8b8b4'}, 'bridge_patch': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/driver-bridge.patch', 'sha256': '08f56f8b84a4c5289ce2ff3bbefc9a000b0f66e75bc279eea45bd2ae90c870f5'}, 'mapping_manifest': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/mapping-manifest.json', 'sha256': '23f4738755ebbd3a076ca0cc66fb96abc006d2fce328d4b3c4a13c73cbb78a0c'}, 'mapping_patch': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/exports/temporal-mapping.patch', 'sha256': '4f0750d33cee5660237f098bfbd3b7867020ab32d022881258d60d7a87d2d1bc'}, 'runtime_template_source': {'suffix': '.local/temporal-native/verification/TP07-L0-tools-v3/source-template-manifest.json', 'sha256': '2a3affe25bc6ec3b0624e0a6d4a4ab3202827929a45a39f525d2f023fe5b29ba'}, 'transaction_patch': {'suffix': 'tools/DownstreamAcceptance/TP07/patches/transaction-capability.v3.patch', 'sha256': 'd13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f'}, 'transaction_product_manifest': {'suffix': 'tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json', 'sha256': '400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8'}, 'transaction_source_approval': {'suffix': '.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json', 'sha256': '66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f'}}
MODIFIED_PATHS = frozenset(('src/W.EntityFrameworkCore.Dameng/Extensions/DamengServiceCollectionExtensions.cs', 'test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs'))
ADDED_PATHS = frozenset(('src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransaction.cs', 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRelationalTransactionFactory.cs', 'test/W.EntityFrameworkCore.Dameng.Tests/DamengRelationalTransactionCapabilityTests.cs'))

HASH = re.compile(r'[a-f0-9]{64}')


class ContractError(ValueError): pass
def need(value, code):
    if not value: raise ContractError(code)
def integer(value, code):
    need(type(value) is int, code); return value
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def sqlsha(text): return hashlib.sha256(text.encode('utf-8')).hexdigest()
def pairs(rows):
    result = {}
    for key, value in rows:
        need(key not in result, 'duplicate_JSON_key'); result[key] = value
    return result
def load(path):
    def invalid(_): raise ContractError('nonfinite_JSON')
    return json.loads(Path(path).read_text(), object_pairs_hook=pairs, parse_constant=invalid)
def policy(): return load(HERE / 'design-cases.json')
def artifact(ref):
    need(set(ref) == {'path', 'sha256'} and type(ref['sha256']) is str and HASH.fullmatch(ref['sha256']), 'approved_artifact_reference')
    need(Path(ref['path']).is_absolute() and sha(ref['path']) == ref['sha256'], 'artifact_SHA_drift'); return load(ref['path'])
def file_artifact(ref):
    need(set(ref) == {'path', 'sha256'} and HASH.fullmatch(ref['sha256']) and Path(ref['path']).is_absolute()
        and sha(ref['path']) == ref['sha256'], 'file_artifact_drift'); return Path(ref['path']).resolve()
def inside(path, root):
    value, parent = Path(path).resolve(), Path(root).resolve()
    need(value != parent and value.is_relative_to(parent), 'bounded_descendant'); return value
def relative(name):
    value = Path(name)
    need(not value.is_absolute() and '\\' not in name and not set(value.parts) & {'..', '.', '.local', 'secrets'}, 'relative_source_path'); return value
def source_files(root, collection):
    need(type(collection) is dict and bool(collection), 'finite_source_collection')
    for name, digest in collection.items():
        value = inside(Path(root) / relative(name), root)
        need(HASH.fullmatch(digest) and sha(value) == digest, 'source_lock_asset_drift')


def pack(ref):
    value = artifact(ref); p = policy()
    need(set(value) == {'schema_version', 'task', 'source_hashes'} and value['schema_version'] == 1
        and value['task'] == 'TP07-D source pack', 'source_pack_schema')
    expected = set(NEW) | set(p['immutable_source_hashes'])
    need(len(expected) == p['pack_count'] == 25 and set(value['source_hashes']) == expected, 'finite_25_source_pack')
    actual = {name: sha(ROOT / relative(name)) for name in expected}
    need(actual == value['source_hashes'], 'executed_source_pack_drift')
    need(all(actual[name] == digest for name, digest in p['immutable_source_hashes'].items()), 'immutable_CSharp_and_helpers')
    return dict(manifest_path=ref['path'], manifest_sha256=ref['sha256'], source_hashes=actual)


def pure_helpers(identity):
    """Compile only approved function ASTs, never L0 top-level or acceptance APIs."""
    p = policy(); path = ROOT / PREFIX / 'runtime-validate.py'; raw = path.read_bytes()
    need(hashlib.sha256(raw).hexdigest() == identity['source_hashes'][PREFIX + 'runtime-validate.py'], 'pure_helper_source_pin')
    tree = ast.parse(raw, filename=str(path)); wanted = set(p['helper_functions'])
    nodes = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in wanted]
    need({node.name for node in nodes} == wanted and len(nodes) == len(wanted), 'finite_helper_AST_definitions')
    for node in nodes:
        need(not node.decorator_list and not any(isinstance(child, (ast.Import, ast.ImportFrom, ast.Global, ast.Nonlocal)) for child in ast.walk(node)), 'helper_AST_side_effect')
        for child in ast.walk(node):
            if isinstance(child, ast.Call) and isinstance(child.func, ast.Name):
                need(child.func.id not in {'exec', 'eval', 'compile', 'open', '__import__'}, 'helper_AST_execution_or_IO')
    decoder_path = ROOT / 'tools/TemporalBoundaryProbe/packed_oracle.py'
    decoder_bytes = decoder_path.read_bytes()
    need(hashlib.sha256(decoder_bytes).hexdigest() == identity['source_hashes']['tools/TemporalBoundaryProbe/packed_oracle.py'], 'decoder_source_pin')
    decoder = types.ModuleType('D_pinned_integer_decoder')
    exec(compile(decoder_bytes, str(decoder_path), 'exec'), decoder.__dict__)
    scope = dict(require=need, integer=integer, ContractError=ContractError, re=re, hashlib=hashlib,
        policy=lambda: load(ROOT / PREFIX / 'runtime-cases.json'), packet_decoder=lambda: decoder.decode_packet)
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(path), 'exec'), scope)
    return types.SimpleNamespace(**{name: scope[name] for name in wanted})


def managed_mvid(path, identity):
    source = ROOT / PREFIX / 'managed_identity.py'; raw = source.read_bytes()
    need(hashlib.sha256(raw).hexdigest() == identity['source_hashes'][PREFIX + 'managed_identity.py'], 'managed_identity_pin')
    module = types.ModuleType('D_pinned_managed_identity'); exec(compile(raw, str(source), 'exec'), module.__dict__)
    return module.mvid(Path(path).read_bytes())


def transaction_contract(l0, old, product, approval, patch_text, workspace):
    """Semantic gate kept separate from byte pins for targeted mutation contracts."""
    need(set(product) == PRODUCT_FIELDS and product['schema_version'] == 1 and product['revision'] == 3, 'transaction_product_43_fields')
    need(set(approval) == {'schema_version', 'task', 'status', 'scope', 'manifest', 'exact_count', 'exact_paths'}
        and approval['schema_version'] == 1 and approval['task'] == 'TP07-T1-overlay-source-approval'
        and approval['status'] == 'source_approved', 'transaction_approval_7_fields')
    need(approval['scope'] == product['required_source_approval_scope'] == 'transaction_capability_and_w_retry_contract_tests_only', 'transaction_approval_scope')
    product_root = workspace / '.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source'
    need(product['source_root'] == str(product_root), 'transaction_product_root')
    need(approval['manifest'] == l0['lineage_artifacts']['transaction_product_manifest']
        and product['transaction_capability_patch'] == l0['lineage_artifacts']['transaction_patch']
        and product['base_approved_source'] == l0['lineage_artifacts']['approved_ef_source_manifest'], 'transaction_nested_ref_binding')
    need(set(old) == {'M1_execution_summary_sha256', 'execution_source_manifest_sha256', 'original_unit_lock_sha256', 'schema_version', 'source_hashes', 'task', 'unit_lock_sha256'}
        and old['schema_version'] == 1, 'transaction_original_approved_schema')
    base, current = old['source_hashes'], product['source_hashes']
    need(len(base) == 175 and product['base_source_count'] == 175 and product['base_source_hashes'] == base
        and l0['approved_ef_source_hashes'] == base, 'transaction_original_175_mapping')
    modified = {name for name in base if name in current and base[name] != current[name]}
    added = set(current) - set(base); unchanged = {name for name in base if current.get(name) == base[name]}
    need(set(base) <= set(current) and modified == MODIFIED_PATHS and added == ADDED_PATHS
        and len(unchanged) == product['unchanged_base_file_count'] == 173
        and set(product['modified_files']) == MODIFIED_PATHS and set(product['added_files']) == ADDED_PATHS,
        'transaction_exact_173_2_3_delta')
    historical_locks = {name: digest for name, digest in base.items() if name.endswith('/packages.lock.json')}
    need(len(historical_locks) == 6 and all(current.get(name) == digest for name, digest in historical_locks.items())
        and historical_locks['test/W.EntityFrameworkCore.Dameng.Tests/packages.lock.json'] == old['unit_lock_sha256'], 'transaction_six_historical_locks_and_unit_provenance')
    need(product['source_count'] == len(current) == 178 and l0['product_ef_source_hashes'] == current,
        'transaction_current_178_mapping')
    aggregate = hashlib.sha256(''.join(current[name] + '  ' + name + '\n' for name in sorted(current)).encode()).hexdigest()
    need(product['aggregate_format'] == 'sorted relative source paths: sha256 two spaces path newline'
        and product['source_aggregate_sha256'] == aggregate == '2fec75dab2b6905091923f83eb2cfd9325188eb8a2ba599c223438c48caab29c', 'transaction_product_aggregate')
    need(approval['exact_count'] == 178 and approval['exact_paths'] == sorted(current), 'transaction_approval_exact_paths')
    paths = re.findall(r'^diff --git a/(\S+) b/(\S+)$', patch_text, re.M)
    need(len(paths) == 5 and all(a == b for a, b in paths) and {a for a, b in paths} == MODIFIED_PATHS | ADDED_PATHS
        and product['patch_paths'] == sorted(MODIFIED_PATHS | ADDED_PATHS), 'transaction_signed_patch_paths')
    need(set(l0['lineage']) == {'ef_commit', 'base_archive_sha256', 'bridge_patch_sha256', 'mapping_manifest_sha256',
        'mapping_patch_sha256', 'approved_ef_source_manifest_sha256', 'runtime_template_source_sha256'} | set(TRANSACTION_PINS), 'transaction_ten_lineage_fields')
    need(all(l0['lineage'].get(key) == digest for key, digest in TRANSACTION_PINS.items()), 'transaction_lineage_pins')
    need(all(l0['lineage'][key] == digest for key, digest in product['base_lineage'].items()) and l0['W'] == product['W'], 'transaction_base_lineage_W')
    templates = l0['runtime_template_hashes']
    need(len(templates) == 7 and set(templates) == {'TP07LiteralProbe/LiteralCases.cs', 'TP07LiteralProbe/LiteralOracle.cs',
        'TP07LiteralProbe/Program.cs', 'TP07LiteralProbe/TP07LiteralProbe.csproj', 'common/CandidateGuard.cs', 'common/OwnedObjects.cs', 'common/SafeEvidence.cs'}, 'transaction_L0_seven_templates')
    need(l0['source_hashes'] == dict(current, **{'test/' + name: digest for name, digest in templates.items()})
        and len(l0['source_hashes']) == 185, 'transaction_exact_L0_185_mapping')
    return current


def nested_file_refs(value, workspace, protected):
    if type(value) is dict:
        if set(value) == {'path', 'sha256'}:
            path = Path(value['path']); need(path.is_absolute() and path.resolve().is_relative_to(workspace)
                and path.resolve() == path and re.fullmatch(r'[a-f0-9]{64}', value['sha256'])
                and sha(path) == value['sha256'], 'transaction_nested_artifact_pin')
            protected[str(path)] = value['sha256']
        else:
            for child in value.values(): nested_file_refs(child, workspace, protected)
    elif type(value) is list:
        for child in value: nested_file_refs(child, workspace, protected)


def transaction_origins(workspace, source_ref, lock_ref, l0root, pack_hashes):
    """Finite physical source authority; no module, SDK, runner or acceptance imports."""
    workspace = Path(workspace).resolve(); verification = workspace / '.local/temporal-native/verification'
    expected_source = verification / 'TP07-L0-transaction-v2-prepare/source-manifest.json'
    expected_lock = verification / 'TP07-L0-transaction-v2-lock/lock-manifest.json'
    need(source_ref == dict(path=str(expected_source), sha256=L0_SOURCE_SHA)
        and lock_ref == dict(path=str(expected_lock), sha256=L0_LOCK_SHA)
        and Path(l0root).resolve() == verification / 'TP07-L0-transaction-v2-prepare/replay', 'transaction_fixed_L0_refs_root')
    l0, locked = artifact(source_ref), artifact(lock_ref); protected = {source_ref['path']: source_ref['sha256'], lock_ref['path']: lock_ref['sha256']}
    need(set(l0) == {'W', 'approved_ef_source_hashes', 'lineage', 'lineage_artifacts', 'product_ef_source_hashes',
        'runtime_template_hashes', 'schema_version', 'source_hashes', 'task', 'toolpack', 'toolpack_check'}, 'transaction_L0_source_schema')
    expected_artifacts = {name: dict(path=str(workspace / row['suffix']), sha256=row['sha256']) for name, row in LINEAGE_ARTIFACTS.items()}
    need(l0['lineage_artifacts'] == expected_artifacts, 'transaction_ten_artifact_refs')
    nested_file_refs(expected_artifacts, workspace, protected)
    old = artifact(expected_artifacts['approved_ef_source_manifest']); product = artifact(expected_artifacts['transaction_product_manifest'])
    approval = artifact(expected_artifacts['transaction_source_approval'])
    nested_file_refs(product, workspace, protected)
    current = transaction_contract(l0, old, product, approval, Path(expected_artifacts['transaction_patch']['path']).read_text(), workspace)
    need(artifact(expected_artifacts['runtime_template_source'])['source_hashes'] == l0['runtime_template_hashes'], 'transaction_template_manifest_mapping')
    # Complete physical maps, including all six historical locks, remain pinned.
    for root, hashes in ((Path(product['source_root']), current), (Path(l0root), l0['source_hashes'])):
        actual = {p.relative_to(root).as_posix(): sha(p) for p in root.rglob('*') if p.is_file()}
        need(actual == hashes, 'transaction_physical_source_map')
        protected.update({str(root / name): digest for name, digest in hashes.items()})
    need(all(pack_hashes.get(PREFIX + 'runtime-templates/' + name) == digest for name, digest in l0['runtime_template_hashes'].items()), 'transaction_pack_same_L0_templates')
    host_lock = 'test/TP07LiteralProbe/packages.lock.json'
    expected = dict(l0['source_hashes'], **{host_lock: locked['lock_sha256']})
    need(locked['source_manifest_sha256'] == L0_SOURCE_SHA and locked['lineage'] == l0['lineage']
        and locked['source_hashes'] == expected and len(expected) == 186, 'transaction_exact_L0_lock_186')
    lockroot = expected_lock.parent / 'replay'
    need({p.relative_to(lockroot).as_posix(): sha(p) for p in lockroot.rglob('*') if p.is_file() and not set(p.relative_to(lockroot).parts) & {'bin', 'obj'}} == expected,
        'transaction_physical_L0_lock_map')
    protected.update({str(lockroot / name): digest for name, digest in expected.items()})
    return l0, locked, protected


def d_source_collection(hashes, l0, l0lock, pack_hashes, stage, generated=None):
    """Exact path increments; native lock hashes are supplied by the real lock stage."""
    cleanup = stage in ('cleanup', 'cleanup-native')
    expected = dict(l0['source_hashes'])
    if not cleanup:
        templates = { 'test/' + name.split('runtime-templates/', 1)[1]: digest for name, digest in pack_hashes.items()
            if name.startswith(PREFIX + 'runtime-templates/TP07TemporalDesign/')}
        need(len(templates) == 10, 'transaction_D_ten_templates'); expected.update(templates)
        config = (json.dumps({'version': 1, 'isRoot': True, 'tools': {'dotnet-ef': {'version': '10.0.12', 'commands': ['dotnet-ef'], 'rollForward': False}}}, indent=2, sort_keys=True) + '\n').encode()
        expected['.config/dotnet-tools.json'] = hashlib.sha256(config).hexdigest()
    if stage in ('native', 'cleanup-native', 'generated6', 'generated12'):
        expected['test/TP07LiteralProbe/packages.lock.json'] = l0lock['lock_sha256']
        if not cleanup:
            name = 'test/TP07TemporalDesign/packages.lock.json'
            need(name in hashes and re.fullmatch(r'[a-f0-9]{64}', hashes[name]), 'transaction_D_native_lock'); expected[name] = hashes[name]
    if stage in ('generated6', 'generated12'):
        count = 6 if stage == 'generated6' else 12
        need(type(generated) is dict and set(generated) == {'directory', 'project', 'source_hashes'}, 'transaction_generated_closure_schema')
        directory = generated['directory']; rows = generated['source_hashes']
        need(re.fullmatch(r'test/TP07Generated/[A-F0-9]{32}', directory) and generated['project'] == directory + '/TP07Generated.csproj'
            and len(rows) == count and all(name.startswith(directory + '/') and Path(name).suffix in ('.cs', '.csproj')
                and not set(Path(name).parts) & {'..', 'bin', 'obj'} for name in rows), 'transaction_exact_generated_paths')
        expected.update(rows); name = directory + '/packages.lock.json'
        need(name in hashes and re.fullmatch(r'[a-f0-9]{64}', hashes[name]), 'transaction_generated_native_lock'); expected[name] = hashes[name]
    counts = {'workflow': 196, 'cleanup': 185, 'native': 198, 'cleanup-native': 186, 'generated6': 205, 'generated12': 211}
    need(stage in counts and len(expected) == counts[stage] and hashes == expected, 'transaction_exact_D_' + stage + '_collection')
    return expected


def d_source_origins(source, identity, stage, generated=None):
    refs = source['origin_refs']
    need(set(refs) == {'source_pack', 'l0_source', 'l0_lock', 'packages', 'design', 'pruning_design'}, 'transaction_D_six_origin_refs')
    workspace = Path(refs['l0_source']['path']).parents[4]
    l0, locked, protected = transaction_origins(workspace, refs['l0_source'], refs['l0_lock'],
        workspace / '.local/temporal-native/verification/TP07-L0-transaction-v2-prepare/replay', identity['source_hashes'])
    need(refs['source_pack'] == dict(path=identity['manifest_path'], sha256=identity['manifest_sha256'])
        and refs['design'] == dict(path=str(workspace / 'docs/implementation/temporal-native/TP07-D-transaction-v1-preparation-design.md'), sha256=DESIGN_SHA), 'transaction_D_pack_design_refs')
    need(refs['packages'] == dict(path=str(workspace / '.local/temporal-native/verification/TP07-D-package-inputs-v1/dependency-inputs.json'), sha256='ea4aa63676e4722dbfe0ee059819e33df0278c3172b77aadef92b5355998ec14')
        and refs['pruning_design'] == dict(path=str(workspace / 'docs/implementation/temporal-native/TP07-D-native-graph-triage.md'), sha256='5ac2385f55a3afe2bbbf1f82f00f003c13bd576d03493c79f9f8becba032095b'), 'transaction_D_fixed_package_pruning_refs')
    nested_file_refs(refs, workspace, protected)
    need(source['lineage'] == l0['lineage'] and source['W'] == l0['W'], 'transaction_D_complete_lineage_W')
    d_source_collection(source['source_hashes'], l0, locked, identity['source_hashes'], stage, generated)
    return protected


def startup_origins(value, identity, documents, cleanup=False):
    stage = 'cleanup-native' if cleanup else 'native'
    generated = value.get('generated_closure')
    if generated is not None:
        need(not cleanup and len(generated['source_hashes']) in (6, 12), 'transaction_startup_generated_role')
        stage = 'generated6' if len(generated['source_hashes']) == 6 else 'generated12'
    protected = d_source_origins(documents['source_manifest'], identity, stage, generated)
    expected_locks = {'src/W.EntityFrameworkCore.Dameng/packages.lock.json', 'test/TP07LiteralProbe/packages.lock.json'}
    if not cleanup: expected_locks.add('test/TP07TemporalDesign/packages.lock.json')
    if generated is not None: expected_locks.add(generated['directory'] + '/packages.lock.json')
    need(value['locks'] == {name: value['source_hashes'][name] for name in expected_locks}, 'transaction_exact_active_locks')
    return protected


def lineage(value):
    p = policy(); actual = value['lineage']
    for key in ('ef_commit', 'base_archive_sha256', 'mapping_manifest_sha256', 'mapping_patch_sha256'):
        need(actual[key] == p[key], 'same_mapping_v5_' + key)
    need(all(actual.get(key) == digest for key, digest in TRANSACTION_PINS.items()), 'transaction_lineage_pins')
    need(value['W'] == p['W'], 'same_candidate_tuple')


def same_overlay(left,right):
    keys=('ef_commit','base_archive_sha256','bridge_patch_sha256','mapping_manifest_sha256','mapping_patch_sha256','approved_ef_source_manifest_sha256') + tuple(TRANSACTION_PINS)
    need(all(key in left and key in right and left[key]==right[key] for key in keys),'same_EF_mapping_v5_overlay_lineage')


def startup(ref, identity, cleanup=False):
    value = artifact(ref); need(value['schema_version'] == 1 and value['task'] == ('TP07-D-cleanup-startup' if cleanup else 'TP07-D-startup'), 'D_startup_schema')
    lineage(value); need(value['source_pack'] == identity, 'build_same_D_source_pack')
    root = Path(value['replay_root']).resolve()
    need(root.is_dir() and not any(part in root.parts for part in ('base-source', 'snapshots', 'source-freeze'))
        and 'mapping-v4/work' not in str(root) and 'mapping-v5/work' not in str(root), 'disposable_replay_only')
    for group in ('source_hashes', 'locks', 'assets'): source_files(root, value[group])
    documents = source_lock_documents(value,identity)
    startup_origins(value, identity, documents, cleanup)
    names = [row['name'] for row in value['startup_assemblies']]
    allowed=({'TP07LiteralProbe'},) if cleanup else ({'TP07TemporalDesign', 'TP07LiteralProbe'}, {'TP07TemporalDesign', 'TP07LiteralProbe', 'TP07GeneratedConsumer'})
    need(len(names) == len(set(names)) and set(names) in allowed, 'finite_D_cleanup_generated_hosts')
    for row in value['startup_assemblies']:
        binary = inside(root / relative(row['path']), root)
        need(sha(binary) == row['dll_sha256'] and sha(binary.with_suffix('.deps.json')) == row['deps_sha256']
            and managed_mvid(binary, identity) == row['mvid'], 'actual_startup_binary_identity')
        deps = load(binary.with_suffix('.deps.json'))['libraries']
        need(deps['W.DamengProvider/' + policy()['W']['version']]['type'] == 'package'
            and not any(name.lower().startswith('dm.dmprovider/') for name in deps), 'no_official_or_source_W_fallback')
        for name in ('Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational', 'Microsoft.EntityFrameworkCore.Abstractions'):
            need(deps[name + '/10.0.12']['type'] == 'package', 'exact_EF_package_version')
        need(sha(binary.parent / 'W.DamengProvider.dll') == policy()['W']['dll_sha256'], 'loaded_W_copy_pin')
        need(sha(binary.parent / 'W.EntityFrameworkCore.Dameng.dll') == value['ef_provider']['dll_sha256'], 'loaded_EF_copy_pin')
    return value


def source_lock_documents(value,identity):
    result={}
    for name in ('source_manifest','lock_manifest'):
        document=artifact(dict(path=value[name+'_path'],sha256=value[name+'_sha256']))
        need(document['schema_version']==1 and document['source_pack']==identity and document['replay_root']==value['replay_root'],'external_source_lock_binding')
        need(document['source_hashes']==value['source_hashes'] and document['lineage']==value['lineage'] and document['W']==value['W'],'exact_source_lock_startup_collections')
        if name=='lock_manifest':need(document['locks']==value['locks'],'exact_lock_manifest_startup_locks')
        result[name]=document
    return result


def build_receipt(ref, start_ref, identity):
    value = artifact(ref)
    need(value['schema_version'] == 1 and value['task'] == 'TP07-D-external-build' and value['source_pack'] == identity
        and value['startup_manifest'] == start_ref and value['SDK_version'] == '10.0.401'
        and value['database_accessed'] is False and value['locked_restore'] is True, 'external_Low_build_required')
    need(value['commands'] and all(row['exit_code'] == 0 and row['timed_out'] is False and row['termination_code'] is None for row in value['commands']), 'external_build_success')
    for row in value['commands']:
        file_artifact(dict(path=row['log_path'], sha256=row['log_sha256']))
    return value


def cleanup_host(ref):
    value=artifact(ref)
    need(value['schema_version']==1 and value['task']=='TP07-D-cli-tool','approved_cleanup_host_manifest')
    need(inside(value['cli_home'],value['task_root']).is_dir(),'prepared_cleanup_task_home')
    return value,file_artifact(value['dotnet_host'])


def tool_bundle(ref, root):
    value = artifact(ref); p = policy()
    need(value['schema_version'] == 1 and value['task'] == 'TP07-D-cli-tool' and value['replay_root'] == str(root), 'frozen_D_tool_manifest')
    task = Path(value['task_root']).resolve()
    for name in ('cli_home', 'tool_root'): need(inside(value[name], task).is_dir(), 'externally_prepared_tool_home')
    host = file_artifact(value['dotnet_host']); manifest_path = file_artifact(value['local_tool_manifest'])
    need(manifest_path == root / '.config/dotnet-tools.json', 'same_local_tool_manifest')
    tool = load(manifest_path)['tools']['dotnet-ef']
    need(tool['version'] == '10.0.12' and tool['commands'] == ['dotnet-ef'] and tool['rollForward'] is False, 'exact_local_tool_declaration')
    package = file_artifact(value['package']); inside(package, task)
    need(value['package']['sha256'] == p['tool_package_sha256'] and value['tool_files'] == p['tool_files'], 'reviewed_tool_package_and_files')
    with zipfile.ZipFile(package) as zipped:
        meta = ET.fromstring(zipped.read('dotnet-ef.nuspec'))
        fields = {node.tag.split('}')[-1]: node.text for node in meta.iter()}
        need(fields['id'] == 'dotnet-ef' and fields['version'] == '10.0.12', 'tool_nuspec_identity')
        for name, digest in p['tool_files'].items():
            need(sha(Path(value['tool_root']) / name) == digest and hashlib.sha256(zipped.read('tools/net8.0/any/' + name)).hexdigest() == digest, 'tool_entry_bytes')
    command = ET.parse(Path(value['tool_root']) / 'DotnetToolSettings.xml').getroot().find('Commands/Command')
    need(command.attrib == {'Name': 'dotnet-ef', 'EntryPoint': 'dotnet-ef.dll', 'Runner': 'dotnet'}, 'explicit_dotnet_exec_entrypoint')
    for name, digest in value.get('preparation_cache_files', {}).items():
        inside(name, value['cli_home']); need(sha(name) == digest, 'preparation_cache_artifact_only')
    return value, host


def dependencies(ref, start, profile):
    gate = artifact(ref); lineage(gate)
    need(gate['schema_version'] == 1 and gate['task'] == 'TP07-D-model-gates' and gate['status'] == 'accepted_in_scope'
        and gate['profile'] == profile, 'explicit_same_v5_model_activation')
    same_overlay(gate['lineage'],start['lineage'])
    need(set(gate['accepted_gate_refs']) == {'L0', 'E'}, 'E_L0_prerequisites')
    for name, reference in gate['accepted_gate_refs'].items():
        accepted = artifact(reference)
        need(accepted['status'] == 'accepted_in_scope' and accepted['issues'] == [] and accepted['W'] == policy()['W']
            and accepted['profile'] == profile, 'accepted_' + name + '_same_candidate')
        same_overlay(accepted['lineage'],start['lineage'])
        for evidence in accepted['evidence_refs']: artifact(evidence)
    if profile == 'tls':
        closed = gate['seven_closed_refs']; need(set(closed) == {'E', 'TP06'}, 'TLS_closed7_explicit_join')
        for name, reference in closed.items():
            proof = artifact(reference)
            need(proof['W'] == policy()['W'] and proof['profile'] == 'tls' and proof['seven_write'] is False
                and proof['status'] == 'accepted_in_scope' and proof['issues'] == [] and proof['evidence_refs'], 'same_candidate_closed7_' + name)
            for evidence in proof['evidence_refs']: artifact(evidence)
    return gate


def plan(ref):
    value = artifact(ref); required = {'schema_version', 'task', 'stage', 'profile', 'source_pack', 'startup_manifest', 'external_build_receipt',
        'cli_tool_manifest', 'model_gate_manifest', 'helper_ast_proof', 'candidate_manifest', 'capabilities', 'original_repo', 'wrapper', 'python', 'parent_checkpoint',
        'cleanup_startup_manifest','cleanup_build_receipt'}
    need(set(value) == required and value['schema_version'] == 1 and value['task'] == 'TP07-D-execution-plan', 'finite_execution_plan')
    need(value['stage'] in ('scaffold', 'scaffold-consume', 'scaffold-finalize', 'guarded', 'cleanup') and value['profile'] in ('shared', 'tls'), 'finite_stage_profile')
    identity = pack(value['source_pack']); cleanup_only=value['stage']=='cleanup'
    start = startup(value['cleanup_startup_manifest'] if cleanup_only else value['startup_manifest'], identity,cleanup_only)
    proof=artifact(value['helper_ast_proof'])
    need(value['helper_ast_proof']['sha256']=='88424da24c4d68d4561e73afeaa2961f87b5cf8b7173788d4e519a1553b1b7a2'
        and proof['pure16_AST_equal'] is True and proof['pure16_AST_sha256']=='8e254c98c4edbd0006081e642d3dd13ec12de56f2e8d979e0be5fef85b4f05d7'
        and proof['transitive_top_level_function_closure_equal'] is True, 'approved_current_L0_pure_AST_proof')
    if not cleanup_only:build_receipt(value['external_build_receipt'], value['startup_manifest'], identity)
    base=startup(value['cleanup_startup_manifest'],identity,True);build_receipt(value['cleanup_build_receipt'],value['cleanup_startup_manifest'],identity)
    if not cleanup_only:
        same_overlay(base['lineage'],start['lineage'])
        need(base['replay_root']!=start['replay_root']
        and not Path(base['replay_root']).resolve().is_relative_to(Path(start['replay_root']).resolve()), 'separate_protected_cleanup_host_closure')
    tool_root=Path(artifact(value['cli_tool_manifest'])['replay_root'])
    tool, host = cleanup_host(value['cli_tool_manifest']) if cleanup_only else tool_bundle(value['cli_tool_manifest'], tool_root)
    if not cleanup_only:
        need(tool_root==Path(start['replay_root']),'same_workflow_tool_replay');dependencies(value['model_gate_manifest'], start, value['profile'])
    file_artifact(value['candidate_manifest']); file_artifact(value['capabilities']); python = file_artifact(value['python'])
    need(value['candidate_manifest']['sha256'] == policy()['candidate_identity_sha256'] and value['capabilities']['sha256'] == policy()['capabilities_sha256'], 'candidate_capability_pins')
    wrapper = file_artifact(value['wrapper']); need(wrapper == Path(value['original_repo']).resolve() / 'scripts' / ('with-dameng-tls-test.sh' if value['profile'] == 'tls' else 'with-dameng-test.sh')
        and value['wrapper']['sha256'] == policy()['wrappers'][value['profile']], 'original_TEST_only_wrapper')
    if value['stage'] in ('scaffold-consume', 'scaffold-finalize', 'cleanup'): need(value['parent_checkpoint'] is not None, 'exact_parent_checkpoint_required')
    else: need(value['parent_checkpoint'] is None, 'fresh_stage_no_parent')
    return value, identity, start, tool, host, python


def runtime(value, start, start_sha, name):
    for key in ('version', 'package_sha256', 'dll_sha256', 'mvid'): need(value[key] == policy()['W'][key], 'runtime_W_identity')
    target = next(row for row in start['startup_assemblies'] if row['name'] == name)
    need(value['startup_assembly'] == name and value['startup_sha256'] == target['dll_sha256'] and value['startup_mvid'] == target['mvid']
        and value['deps_sha256'] == target['deps_sha256'] and value['build_manifest_sha256'] == start_sha
        and value['source_manifest_sha256_binding'] == start['source_manifest_sha256'] and value['lock_manifest_sha256'] == start['lock_manifest_sha256'], 'actual_runtime_source_binary_binding')
    need(value['ef_provider']['dll_sha256'] == start['ef_provider']['dll_sha256'] and value['ef_provider']['mvid'] == start['ef_provider']['mvid'], 'runtime_EF_identity')


def generated_hashes(root, folder):
    return {str(path.relative_to(root)): sha(path) for path in sorted(folder.rglob('*')) if path.is_file()
        and path.suffix in ('.cs', '.csproj') and not set(path.relative_to(folder).parts) & {'bin', 'obj'}}
def continuation(parent, old, new, root, after_generation=None):
    need(parent['status'] in policy()['pending_statuses'].values() and parent['exit_code'] == 0 and parent['cleanup_verified'] is False, 'pending_parent_only')
    generation = parent['generation']; folder = inside(root / relative(generation['directory']), root)
    need(re.fullmatch(r'test/TP07Generated/[A-F0-9]{32}', generation['directory']) and generation['project'] == generation['directory'] + '/TP07Generated.csproj', 'owned_generated_subtree')
    need(old['replay_root'] == new['replay_root'] == generation['replay_root'] == str(root)
        and new['parent_build_manifest_sha256'] == generation['parent_build_manifest_sha256'], 'unchanged_replay_and_parent_build')
    closure = {key: generation[key] for key in ('directory', 'project', 'source_hashes')}
    actual=generated_hashes(root,folder)
    need(new['generated_closure'] == closure and all(actual.get(name)==digest for name,digest in closure['source_hashes'].items()), 'exact_approved_generated_closure')
    need(actual==(after_generation['source_hashes'] if after_generation is not None else closure['source_hashes']), 'bounded_current_generated_sources')
    need(len(closure['source_hashes']) == (6 if parent['status'] == 'generated_pending_external_build' else 12), 'finite_generated_file_count')
    mutable = {'source_hashes', 'locks', 'assets', 'startup_assemblies', 'source_manifest_path', 'source_manifest_sha256', 'lock_manifest_path', 'lock_manifest_sha256', 'parent_build_manifest_sha256', 'parent_report_sha256', 'generated_closure'}
    need(all(new.get(key) == value for key, value in old.items() if key not in mutable), 'retained_original_manifest_keys_pins')
    for group in ('source_hashes', 'locks', 'assets'):
        need(all(new[group].get(name) == digest for name, digest in old[group].items()), 'retained_original_' + group)
        for name in set(new[group]) - set(old[group]):
            relative(name); need(name.startswith(generation['directory'] + '/'), 'no_broader_generated_addition')
    need(all(new['source_hashes'].get(name) == digest for name, digest in closure['source_hashes'].items()), 'generated_source_approved_before_exec')
    for row in old['startup_assemblies']:
        if row['name'] != 'TP07GeneratedConsumer': need(row in new['startup_assemblies'], 'retained_original_host_identity')
    for group, suffix in (('locks', '/packages.lock.json'), ('assets', '/obj/project.assets.json')):
        need(generation['directory'] + suffix in new[group], 'generated_external_locked_build_assets')
    source_lock_documents(old,old['source_pack']);source_lock_documents(new,old['source_pack'])


def ledger(value):
    need(value['schema_version'] == 1 and value['profile'] in ('shared', 'tls') and value['package_sha256'] == policy()['W']['package_sha256']
        and value['identity_sha256'] == policy()['candidate_identity_sha256'], 'owned_ledger_candidate')
    objects = value['objects']; need(len(objects) <= 6 and len({row['table'] for row in objects}) == len(objects), 'finite_owned_ledger')
    for row in objects:
        nonce = row['nonce']; need(re.fullmatch(r'[A-F0-9]{32}', nonce) and row['table'] == 'TPL_' + nonce[:24]
            and row['owner_marker'] == 'OWN_' + nonce[:24] and row['primary_key'] == 'TP07PK_' + nonce[:24] and row['absent_before_create'] is True, 'exact_owned_nonce_names')
        need(row['shape'] in ('ScaffoldMetadata', 'ScaffoldSix', 'GuardedDt', 'GuardedDto', 'History') and row['creation_state'] in
            ('reserved', 'create_inflight', 'create_acknowledged', 'confirmed', 'alter_inflight', 'known_create_failure'), 'D_only_shape_state')
        if row['creation_state'] == 'confirmed': need(row['create_acknowledged'] is True and row['comment_acknowledged'] is True, 'confirmed_ACK_and_marker')
        if row['shape'] in ('GuardedDt', 'GuardedDto'):
            need(row['index'] == 'TP07IX_' + nonce[:24] and row['check'] == 'TP07CK_' + nonce[:24], 'owned_index_check')
    return objects


def cleanup_proof(value, owned, helpers, profile):
    helpers.cleanup(value,{'objects':owned},profile)
    for result in value['objects']:
        entry=next(row for row in owned if row['table']==result['table'])
        if result['dropped'] is True:
            need(entry['create_acknowledged'] is True or entry['comment_acknowledged'] is True, 'unknown_CREATE_without_ACK_marker_not_drop_accepted')


def expect_oracle(value, ticks, kind, scale, helpers, name):
    if ticks is None:
        need(value['rows'] == 1 and value['is_null'] is True and all(value[key] is None for key in ('local_ticks', 'offset_minutes', 'raw', 'ff9'))
            and value['typed'] == {'type': 'System.DBNull', 'is_null': True}, 'real_NULL_receipt')
        helpers.profile(value['identity'], name); helpers.route(value['reader_route'], 'reader')
        m = value['metadata']; ctype = (27 if kind == 'dto' else 26) if scale == 7 else (23 if kind == 'dto' else 16)
        need((m['c_type'], m['width'], m['scale'], m['mask'], m['raw_source']) == (ctype, {16:8,23:10,26:9,27:11}[ctype], scale, 0, 'ResultColumn'), 'NULL_descriptor_scale_type')
        return
    local, offset = ticks if isinstance(ticks, tuple) else (ticks, 330 if kind == 'dto' else None)
    fields = helpers.ff9(value['ff9'], kind == 'dto')
    need(helpers.ticks(fields) == local and fields['offset_minutes'] == offset, 'requested_integer_tick_offset')
    spec = dict(kind=kind, bare=False, parameter_scale=scale, expected_fields=fields, expected_local_ticks=local,
        expected_offset_minutes=offset, expected_utc_ticks=local - offset * 600000000 if offset is not None else None)
    helpers.oracle(value, spec, name)


def row_oracles(state, expected, kind, scale, helpers, name, table):
    rows = state['rows']; receipts = state['row_oracles']
    need([row['id'] for row in rows] == sorted(expected) and [row['id'] for row in receipts] == sorted(expected), 'complete_Dv3_row_oracle_collection')
    for scalar, receipt in zip(rows, receipts):
        value = expected[scalar['id']]; tick, offset = value if isinstance(value, tuple) else (value, 330 if kind == 'dto' and value is not None else None)
        need((scalar['is_null'], scalar['ticks'], scalar['offset']) == (tick is None, tick, offset), 'protected_scalar_rows')
        owned_read_evidence(receipt,table,scalar['id'],helpers,name)
        expect_oracle(receipt['oracle'], value, kind, scale, helpers, name)


def owned_read_evidence(receipt,table,row_id,helpers,name):
    need(receipt['id']==row_id and receipt['table']==table and receipt['column']=='V','fresh_owned_original_column')
    reads=receipt['read_evidence']['oracle_read_attempts']
    need(len(reads)==1 and reads[0]['completed'] is True and reads[0]['table']==table and reads[0]['row_id']==row_id
        and reads[0]['column']=='V' and reads[0]['rows']==1,'actual_owned_raw_read_evidence')
    helpers.profile(reads[0]['identity'],name)


def protected(state, entry, history, expected, kind, scale, ids, helpers, name):
    need(state['column_scale'] == scale and state['history_ids'] == ids
        and state['history_rows'] == [{'migration_id': item, 'product_version': '10.0.12'} for item in ids], 'precision_and_exact_history')
    columns = json.loads(state['columns']); normalized = json.loads(state['columns_except_scale'])
    need(len(columns) == 2 and [row[0] for row in columns] == ['ID', 'V'] and columns[1][3] == scale
        and columns[1][5] == 'Y' and normalized[1][3] == 'checked_fractional_scale', 'exact_table_catalog')
    default = "TIMESTAMP '2026-07-23 14:15:16.123456" + ('+05:30' if kind == 'dto' else '') + "'"
    need(state['default'] == columns[1][4] and type(state['default']) is str and default in state['default'], 'preserved_generated_six_default')
    indexes, constraints = json.loads(state['indexes']), json.loads(state['constraints'])
    need(any(row[0] == entry['index'] and row[2] == 'V' for row in indexes)
        and any(row[0] == entry['primary_key'] and row[1] == 'P' for row in constraints)
        and any(row[0] == entry['check'] and row[1] == 'C' and row[4] for row in constraints), 'own_index_primary_CHECK_preserved')
    hcolumns, hconstraints = json.loads(state['history_columns']), json.loads(state['history_constraints'])
    need([row[0] for row in hcolumns] == ['MigrationId', 'ProductVersion'] and any(row[0] == history['primary_key'] and row[1] == 'P' for row in hconstraints), 'own_history_structure')
    row_oracles(state, expected, kind, scale, helpers, name, entry['table'])


def preserved(before, after, scale_change=False, history_change=False):
    for key in ('default', 'columns_except_scale', 'indexes', 'constraints', 'history_columns', 'history_constraints', 'rows'):
        need(before[key] == after[key], 'protected_' + key + '_changed')
    if not scale_change: need(before['column_scale'] == after['column_scale'] and before['columns'] == after['columns'], 'refused_schema_unchanged')
    if not history_change: need(before['history_rows'] == after['history_rows'], 'refused_history_unchanged')


def guard_sql(table):
    need(re.fullmatch(r'TPL_[A-F0-9]{24}', table), 'guard_exact_owned_identifier')
    return '''DECLARE
    v_user VARCHAR(128);
    v_schema VARCHAR(128);
    v_bad BIGINT;
BEGIN
    SELECT USER, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())
      INTO v_user, v_schema FROM DUAL;
    IF v_user IS NULL OR v_schema IS NULL
       OR v_user <> 'WDM_PROVIDER_TEST' OR v_schema <> 'WDM_PROVIDER_TEST' THEN
        RAISE_APPLICATION_ERROR(-20070, 'TP07_DOWN_SCOPE');
    END IF;
    SELECT COUNT(*) INTO v_bad
      FROM "WDM_PROVIDER_TEST"."''' + table + '''"
     WHERE "V" IS NOT NULL
       AND (TO_CHAR("V", 'FF9') IS NULL
            OR LENGTH(TO_CHAR("V", 'FF9')) <> 9
            OR MOD(TO_NUMBER(TO_CHAR("V", 'FF9')), 1000) <> 0);
    IF v_bad > 0 THEN
        RAISE_APPLICATION_ERROR(-20071, 'TP07_DOWN_NONMICRO');
    END IF;
END;'''


def emitted_guard_sql(table):
    # Fixed EF10.0.12 SqlOperation.AppendLine on macOS; raw G01/G02 never add LF.
    return guard_sql(table)+'\n'


def guard_attempts(report,stage,table,migrated=False):
    need(all(row['sequence']==index and type(row['sequence']) is int for index,row in enumerate(report['commands'])), 'actual_command_sequence')
    need(all(row['sequence']==index and type(row['sequence']) is int for index,row in enumerate(report['protocol_frames'])), 'actual_frame_sequence')
    result=[row for row in report['commands'] if row['stage']==stage and row['status']=='attempted' and row['kind']=='guard']
    expected=sqlsha(emitted_guard_sql(table) if migrated else guard_sql(table))
    for row in result:
        need(row['api']=='ExecuteNonQueryAsync' and row['sql_sha256']==expected and integer(row['command_id'],'guard_command_id')>0, 'actual_exact_guard_API_bytes')
    need(len({row['command_id'] for row in result})==len(result),'unique_actual_guard_calls');return result


def guard_outcome(report,stage,attempt,rejected,command_window=None,frame_window=None):
    commands=report['commands'];frames=report['protocol_frames']
    if command_window is not None:
        begin,end=command_window;need(0<=begin<end<=len(commands),'guard_command_window');commands=commands[begin:end]
        need(attempt in commands,'guard_attempt_in_call_window')
    if frame_window is not None:
        begin,end=frame_window;need(0<=begin<end<=len(frames),'guard_frame_window');frames=frames[begin:end]
    matching=[row for row in commands if row['stage']==stage and row['command_id']==attempt['command_id'] and row['kind']=='guard']
    need([row['status'] for row in matching]==['attempted','failed' if rejected else 'completed'], 'actual_guard_attempt_terminal_pair')
    terminal=matching[-1];need(terminal['sequence']>attempt['sequence'],'guard_terminal_after_attempt')
    received=[row for row in frames if row['stage']==stage and row['command_id']==attempt['command_id'] and row['request_opcode'] in (5,6,13)]
    need(received and all(row['request_opcode']==row['response_opcode'] for row in received),'actual_guard_frame_command_binding')
    errors=[row for row in received if row['sql_code']!=0]
    if rejected:
        need(terminal['error']=={'type':'W.Dm.DmException','code':None,'number':-20071} and len(errors)==1
            and errors[0]['sql_code']==-20071 and integer(errors[0]['statement_id'],'guard_statement_id')>0,'actual_failed_guard_error_response')
        return errors[0]
    need(terminal['api']=='ExecuteNonQueryAsync' and terminal['sql_sha256']==attempt['sql_sha256'] and not errors,'actual_guard_completed_without_error')
    return None


def guard_error(error, report, stage, attempt=None, frame_window=None):
    response = error['response']; need(error['type'] == 'W.Dm.DmException' and error['number'] == policy()['guard_error']
        and error['verified_server_response'] is True and error['source'] == 'physical_channel_receive_opcode' and error['stage'] == stage, 'exact_verified_guard_error')
    need(response in report['protocol_frames'] and response['stage'] == stage and response['sql_code'] == -20071
        and response['request_opcode'] == response['response_opcode'] and response['request_opcode'] in (5,6,13)
        and integer(response['command_id'], 'actual_guard_command') > 0, 'real_guard_response_binding')
    need(attempt is not None and response['command_id']==attempt['command_id'],'same_guard_error_command')
    if frame_window is not None:
        begin,end=frame_window;need(response in report['protocol_frames'][begin:end],'verified_error_in_guard_call_frame_window')


def guard_order(report, stage, table, refused):
    commands = [row for row in report['commands'] if row['stage'] == stage and row['status'] == 'attempted']
    guards = [index for index, row in enumerate(commands) if row['kind'] == 'guard']
    audited=guard_attempts(report,stage,table,True)
    need(len(guards)==len(audited)==1 and commands[guards[0]]==audited[0], 'actual_exact_emitted_guard_first')
    destructive = {'alter', 'history_mutation', 'ddl'}
    need(not any(row['kind'] in destructive for row in commands[:guards[0]]), 'no_pre_guard_mutation')
    if refused: need(not any(row['kind'] in destructive for row in commands[guards[0]+1:]), 'no_post_error_ALTER_history_mutation')
    else:
        alters=[index for index,row in enumerate(commands) if row['kind']=='alter'];history=[index for index,row in enumerate(commands) if row['kind']=='history_mutation']
        need(len(alters)==len(history)==1 and guards[0]<alters[0]<history[0] and not any(row['kind']=='ddl' for row in commands),'Down_guard_ALTER_history_strict_order')
        for index in (alters[0],history[0]):
            row=commands[index];terminal=[item for item in report['commands'] if item['stage']==stage and item['command_id']==row['command_id'] and item['kind']==row['kind'] and item['status']!='attempted']
            need(row['api']=='ExecuteNonQueryAsync' and len(terminal)==1 and terminal[0]['status']=='completed' and terminal[0]['sequence']>row['sequence']
                and terminal[0]['api']=='ExecuteNonQueryAsync' and terminal[0]['sql_sha256']==row['sql_sha256'],'actual_ALTER_history_completion')
    guard_outcome(report,stage,audited[0],refused)
    return commands[guards[0]]


def parent_SHA_binding(parent_ref, report, start):
    need(report['parent_report_sha256']==parent_ref['report']['sha256'] and start['parent_report_sha256']==parent_ref['report']['sha256'], 'exact_parent_report_SHA')


def extreme_guard_calls(report,stage,table,values):
    calls=guard_attempts(report,stage,table);need(len(calls)==len(values)==5,'five_actual_extreme_guard_calls')
    for call,ticks in zip(calls,values):guard_outcome(report,stage,call,ticks%10!=0)
    return calls


def guarded(report, owned, helpers, name):
    cases = {row['case_id']: row for row in report['cases']}; p = policy()
    scope = cases['DOWN-G05-scope']; need(scope['expected_only'] is True and scope['database_accessed'] is False and scope['negative_controls'] == p['scope_controls'], 'actual_scope_negative_controls')
    for kind in ('dt', 'dto'):
        entry = next(row for row in owned if row['case_id'] == ('R06-UP-' if name == 'shared' else 'R06-TLS6-') + kind)
        history = next(row for row in owned if row['case_id'] == ('R06-HISTORY-' if name == 'shared' else 'R06-TLS6-HISTORY-') + kind)
        expected = {1:p['micro_ticks'],2:None,3:p['micro_ticks']}
        if name == 'tls':
            case = cases['R06-TLS6-' + kind]
            for field in ('before', 'fresh'): protected(case[field], entry, history, expected, kind, 6, [p['baseline_id']], helpers, name)
            preserved(case['before'], case['fresh']); need(case['seven_write_attempted'] is False and case['Up_Down_seven_attempted'] is False, 'TLS_six_only')
            continue
        up = cases['R06-UP-' + kind]
        protected(up['before'], entry, history, expected, kind, 6, [p['baseline_id']], helpers, name)
        protected(up['after'], entry, history, expected, kind, 7, [p['baseline_id'],p['upgrade_id']], helpers, name); preserved(up['before'], up['after'], True, True)
        expression = cases['DOWN-G00-expression-' + kind]['observations']; values = [p['micro_ticks']+n for n in range(10)] + [p['ordinary_ticks'],p['ordinary_ticks']+1]
        need(len(expression) == 12, 'complete_guard_tail_vectors')
        for index, (row, ticks) in enumerate(zip(expression, values)):
            need(row['row_id'] == 100+index and row['ticks'] == ticks and re.fullmatch(r'[0-9]{9}', row['ff9'])
                and int(row['ff9']) == row['integer'] == ticks % 10000000 * 100 and row['mod'] == ticks % 10 * 100, 'independent_FF9_integer_MOD')
            expect_oracle(row['oracle'], ticks, kind, 7, helpers, name)
        extremes = cases['DOWN-G01-extremes-' + kind]['observations']; values = [0,1,p['maximum_ticks'],p['maximum_ticks']-1,p['maximum_ticks']-9]
        need(len(extremes) == 5, 'complete_guard_extremes')
        control_table=next(row['table'] for row in owned if row['case_id']=='DOWN-G00-expression-'+kind)
        extreme_calls=extreme_guard_calls(report,'DOWN-G01-extremes-'+kind,control_table,values)
        for index,(row,ticks) in enumerate(zip(extremes,values)):
            need(row['row_id'] == 200+index and row['ticks'] == ticks and row['downgrade_allowed'] is (ticks % 10 == 0)
                and row['actual_guard_rejected'] is (ticks % 10 != 0) and int(row['ff9']) == ticks % 10000000 * 100, 'guard_extreme_math')
            expect_oracle(row['oracle'], (ticks,0) if kind=='dto' else ticks, kind,7,helpers,name)
        controls = cases['DOWN-G02-control-' + kind]['controls']; need([row['control_id'] for row in controls] == ['empty','NULL','whole_micro','nonmicro'], 'Dv3_actual_control_receipts')
        table = next(row['table'] for row in owned if row['case_id'] == 'DOWN-G00-expression-' + kind)
        need(len(guard_attempts(report,'DOWN-G02-control-'+kind,table))==4,'four_actual_G02_guard_calls')
        previous_commands=previous_frames=0
        for index,control in enumerate(controls):
            expected_rows = {} if index==0 else {1:None} if index==1 else {1:None,2:p['micro_ticks']} if index==2 else {1:None,2:p['micro_ticks'],3:p['tail7_ticks']}
            need(control['table'] == table and control['row_ids'] == list(expected_rows), 'control_owned_rows')
            for field in ('before','after'):
                state = control[field]; helpers.profile(state['identity'],name); need(state['row_count'] == len(expected_rows), 'actual_empty_or_control_count')
                receipts = state['row_oracles']; need([row['id'] for row in receipts] == list(expected_rows), 'control_raw_receipts')
                for row in receipts:
                    owned_read_evidence(row,table,row['id'],helpers,name);expect_oracle(row['oracle'],expected_rows[row['id']],kind,7,helpers,name)
            call = control['guard_call']; need(call['sql_sha256'] == sqlsha(guard_sql(table)) and call['outcome'] == ('rejected' if index==3 else 'returned'), 'actual_control_guard_call')
            stage='DOWN-G02-control-'+kind;attempts=guard_attempts(report,stage,table)
            window=(call['command_start'],call['command_end']);frames=(call['frame_start'],call['frame_end'])
            need(all(type(value) is int for value in window+frames) and window[0]>=previous_commands and frames[0]>=previous_frames,'ordered_disjoint_control_call_windows')
            previous_commands,previous_frames=window[1],frames[1]
            attempts=[row for row in attempts if window[0]<=row['sequence']<window[1]]
            need(len(attempts)==1 and call['api']=='ExecuteNonQueryAsync','single_actual_control_guard_API')
            received=guard_outcome(report,stage,attempts[0],index==3,window,frames)
            if index==3:
                guard_error(control['verified_error'],report,stage,attempts[0],frames);need(control['verified_error']['response']==received and call['affected_rows'] is None
                    and call['error']=={'type':'W.Dm.DmException','code':None,'number':-20071},'same_control_failed_guard_outcome')
            else:need(type(call['affected_rows']) is int and call['error'] is None,'returned_guard_actual_API_outcome')
        refused = cases['DOWN-G03-refusal-' + kind]; extra = dict(expected); extra[900] = p['tail7_ticks']
        for field in ('before','after'): protected(refused[field],entry,history,extra,kind,7,[p['baseline_id'],p['upgrade_id']],helpers,name)
        preserved(refused['before'],refused['after']);attempt=guard_order(report,'DOWN-G03-refusal-'+kind,entry['table'],True)
        guard_error(refused['guard_error'],report,'DOWN-G03-refusal-'+kind,attempt)
        need(refused['down_command_order'][0]=={'ordinal':0,'sha256':sqlsha(emitted_guard_sql(entry['table'])),'transaction_suppressed':True}
            and len(refused['down_command_order'])==2,'actual_generated_refusal_guard_command_order')
        success = cases['DOWN-G04-success-' + kind]
        protected(success['before'],entry,history,expected,kind,7,[p['baseline_id'],p['upgrade_id']],helpers,name)
        protected(success['after'],entry,history,expected,kind,6,[p['baseline_id']],helpers,name); preserved(success['before'],success['after'],True,True)
        attempt=guard_order(report,'DOWN-G04-success-'+kind,entry['table'],False)
        need(success['down_command_order'][0]=={'ordinal':0,'sha256':sqlsha(emitted_guard_sql(entry['table'])),'transaction_suppressed':True}
            and len(success['down_command_order'])==2,'actual_generated_success_guard_command_order')
        alter=next(row for row in report['commands'] if row['stage']=='DOWN-G04-success-'+kind and row['kind']=='alter' and row['status']=='attempted')
        need(success['down_command_order'][1]['ordinal']==1 and success['down_command_order'][1]['sha256']==alter['sql_sha256'],'generated_ALTER_actual_execution_binding')
        need(success['explicit_deleted_owned_row']==900 and success['ddl_rollback_claim'] is False, 'explicit_owned_delete_no_atomic_claim')


def metadata_stores():
    result={'ID':'INT','I9':'INTERVAL DAY(9) TO SECOND(6)','I2':'INTERVAL DAY(2) TO SECOND(6)','L':'TIMESTAMP(6) WITH LOCAL TIME ZONE'}
    for scale in (0,6,7,8,9): result.update({f'D{scale}':f'TIMESTAMP({scale})',f'Z{scale}':f'DATETIME({scale}) WITH TIME ZONE'})
    return result
def facets(rows, stores):
    need(len(rows)==len(stores) and {row['name'].upper():row['store_type'] for row in rows}==stores, 'model_snapshot_precision_preservation')
def process_identity(ref, start, start_sha, tool, catalog=False):
    value=artifact(ref); need(value['task']=='TP07-D-process' and value['final'] is True and value['observer_failed'] is False, 'actual_final_CLI_process_receipt')
    runtime(value['startup_identity'],start,start_sha,'TP07TemporalDesign')
    need(value['framework_assembly_sha256']==start['shared_framework']['hosting_dll_sha256'], 'actual_shared_framework_identity')
    actual=value['prepared_cli_tool_identity']; need(actual['manifest_sha256']==tool['manifest_sha256'] and actual['route']=='exact_dotnet_exec_no_tool_resolver'
        and actual['version']=='10.0.12' and actual['tool_files']==policy()['tool_files'] and actual['resolver_cache_used'] is False, 'actual_CLI_tool_identity')
    if catalog: need(any(row['request_opcode'] in (5,6,13) for row in value['protocol_frames']), 'actual_catalog_wire_activity')
    return value


def cli_children(report,start,start_sha,tool):
    expected={'scaffold':2,'scaffold-consume':2,'scaffold-finalize':3,'guarded':0}
    need(len(report.get('child_invocations',[]))==expected[report['group']],'complete_fixed_CLI_process_count')
    for row in report.get('child_invocations',[]):
        need(row['status']=='completed' and row['exit_code']==0 and row['executable']==tool['dotnet_host']['path'], 'CLI_natural_exit_same_host')
        argv=row['argv'];need(argv[0]=='exec','explicit_CLI_exec')
        if '--snapshot-proof' in argv:
            generated=next(item for item in start['startup_assemblies'] if item['name']=='TP07GeneratedConsumer')
            need(argv[1]==str(Path(start['replay_root'])/generated['path']),'approved_generated_exec_only')
        else:need(argv[1]==str(Path(tool['tool_root'])/'dotnet-ef.dll') and '--no-build' in argv,'exact_tool_exec_no_hidden_build')
        if 'scaffold' in row['declared_argv']:
            need('Name=ConnectionStrings:TP07' in row['argv'] and '--no-onconfiguring' in row['argv'] and row['argv'].count('--table')==1, 'named_connection_owned_table_only')
        child=process_identity(dict(path=row['process_receipt'],sha256=row['process_receipt_sha256']),start,start_sha,tool,'scaffold' in row['declared_argv'])
        if report['group']!='scaffold':
            observed=[item for item in child['verified_startup_assemblies'] if item['startup_assembly']=='TP07GeneratedConsumer']
            need(observed,'actual_generated_host_initializer');runtime(observed[-1],start,start_sha,'TP07GeneratedConsumer')


def report_contract(report, owned, stage, profile, start, start_sha, tool, helpers):
    helpers.no_credentials(report); need(report['task']=='TP07-D' and report['schema_version']==1 and report['profile']==profile
        and report['group']==stage and report['registry']==policy()['registry'] and report['candidate_acceptance'] is False
        and report['full_temporal_acceptance'] is False and report['observer_failed'] is False and report['exit_code']==0, 'D_report_scope_and_exit')
    runtime(report['identity'],start,start_sha,'TP07TemporalDesign')
    key='guarded-'+profile if stage=='guarded' else stage; expected=policy()['stages'][key]
    need([row['case_id'] for row in report['cases']]==expected and all(row['status']=='completed' for row in report['cases']), 'complete_ordered_fixed_D_cases')
    cli_children(report,start,start_sha,tool)
    cases={row['case_id']:row for row in report['cases']}
    if stage in policy()['pending_statuses']:
        need(report['status']==policy()['pending_statuses'][stage] and report['cleanup_verified'] is False, 'pending_checkpoint_not_accepted')
    else:
        need(report['status']=='execution_complete' and report['cleanup_verified'] is True, 'terminal_main_cleanup')
        cleanup_proof(report['cleanup'],owned,helpers,profile)
    if stage=='scaffold':
        factory=cases['R05-CATALOG']['factory_route']; helpers.route(factory['connection'],'connection')
        need(factory['factory_type']=='W.EntityFrameworkCore.Dameng.Scaffolding.Internal.DamengDatabaseModelFactory'
            and factory['activity']=='actual_IDatabaseModelFactory_Create_DbConnection', 'public_DI_actual_factory')
        model=cases['R05-CATALOG']['database_model']; need(len(model)==2, 'two_actual_catalog_tables')
        for row in model:
            entry=next(item for item in owned if item['table']==row['name']); stores=metadata_stores() if entry['shape']=='ScaffoldMetadata' else {'ID':'INT','D':'TIMESTAMP(6)','Z':'DATETIME(6) WITH TIME ZONE'}
            need(row['schema']=='WDM_PROVIDER_TEST' and {col['name']:col['store_type'] for col in row['columns']}==stores, 'catalog_to_DatabaseModel_exact_facets')
    if stage=='scaffold-consume':
        compiled=cases['R05-COMPILE-GENERATED']; facets(compiled['metadata_model_facets'],metadata_stores()); need(compiled['metadata_context_SaveChanges_invoked'] is False,'metadata_only_model')
        micro=cases['R05-SCAFFOLD6-MICRO']; null=cases['R05-SCAFFOLD6-NULL']
        for kind in ('dt','dto'):
            expect_oracle(micro['oracle_'+kind],policy()['micro_ticks'],kind,6,helpers,profile); expect_oracle(null['oracle_'+kind],None,kind,6,helpers,profile)
        need(micro['query_result']['dt']['ticks']==policy()['micro_ticks'] and micro['query_result']['dto']['ticks']==policy()['micro_ticks']
            and micro['query_result']['dto']['offset_minutes']==330, 'actual_generated_six_query')
    if stage=='scaffold-finalize':
        case=cases['R05-SNAPSHOT-MIGRATION']; proof=case['compiled_snapshot_proof']
        need(proof['source']=='actual_compiled_CLI_snapshots_and_migrations' and proof['snapshot_six'] is True and proof['migration_six'] is True
            and case['not_applied'] is True and case['metadata_rows']==0 and case['metadata_context_SaveChanges_invoked'] is False,'actual_compiled_metadata_snapshot_no_write')
        for name,stores in (('six',{'ID':'INT','D':'TIMESTAMP(6)','Z':'DATETIME(6) WITH TIME ZONE'}),('metadata',metadata_stores())):
            facets(proof[name]['snapshot_facets'],stores); need({col['name']:col['column_type'] for col in proof[name]['actual_migration_columns']}==stores,'actual_migration_column_facets')
        generated=next(item for item in start['startup_assemblies'] if item['name']=='TP07GeneratedConsumer')
        folder=Path(start['replay_root'])/generated['path'];folder=folder.parents[3]
        for filename,key,stores in (('migration.sql','sql_sha256',{'D':'TIMESTAMP(6)','Z':'DATETIME(6) WITH TIME ZONE'}),('metadata-migration.sql','metadata_sql_sha256',metadata_stores())):
            path=folder/filename;need(sha(path)==case[key],'actual_CLI_SQL_artifact')
            sql=path.read_text()
            for column,store in stores.items():need(re.search('"'+re.escape(column)+'"\\s+'+re.escape(store).replace(r'\ ',r'\s+'),sql) is not None,'actual_SQL_column_precision')
    if stage=='guarded': guarded(report,owned,helpers,profile)


def validate_receipt(ref, plan_ref, terminal=False):
    execution=artifact(ref); configuration,identity,start,bundle,host,python=plan(plan_ref); helpers=pure_helpers(identity)
    need(execution['task']=='TP07-D-execution' and execution['plan']==plan_ref and execution['source_pack']==identity
        and execution['source_hashes_before']==execution['source_hashes_after']==identity['source_hashes'], 'execution_same_immutable_pack')
    helpers.no_credentials(execution)
    need(execution['PATH_host']['resolved']==str(host) and execution['PATH_host']['sha256']==sha(host)
        and execution['cli_tool_manifest']==configuration['cli_tool_manifest'], 'official_tool_descendants_same_PATH_host')
    stage=configuration['stage']; process=execution['process']
    need(process['exit_code']==0 and process['timed_out'] is False and process['termination_code'] is None
        and process['stdout_discarded'] is True and process['stderr_discarded'] is True, 'natural_secret_safe_main_process')
    owned_document=artifact(execution['ledger']); owned=ledger(owned_document)
    need(owned_document['profile']==configuration['profile'], 'ledger_exact_profile')
    main=artifact(execution['report'])
    if stage=='cleanup':
        runtime(main['runtime_identity'],start,configuration['cleanup_startup_manifest']['sha256'],'TP07LiteralProbe')
        need(main['status']=='cleanup_complete' and main['process_exit_code']==0, 'explicit_abandonment_cleanup')
        cleanup_proof(main['cleanup'],owned,helpers,configuration['profile']);cleanup_proof(main['finally_cleanup'],owned,helpers,configuration['profile'])
        return {'status':'cleanup_verified','cases_validated':0,'runtime_accepted':False}
    reported_tool=dict(bundle,manifest_sha256=configuration['cli_tool_manifest']['sha256'])
    report_contract(main,owned,stage,configuration['profile'],start,configuration['startup_manifest']['sha256'],reported_tool,helpers)
    main_process=artifact(execution['process_receipt'])
    need(main_process['final'] is True and main_process['observer_failed'] is False,'final_main_module_identity')
    runtime(main_process['startup_identity'],start,configuration['startup_manifest']['sha256'],'TP07TemporalDesign')
    parent_ref=configuration['parent_checkpoint']
    if parent_ref is not None:
        need(set(parent_ref)=={'report','execution_receipt'},'finite_parent_checkpoint')
        parent=artifact(parent_ref['report']); previous=artifact(parent_ref['execution_receipt'])
        need(previous['report']==parent_ref['report'] and previous['source_pack']==identity
            and previous['status']=='pending_external_build','same_immutable_parent_checkpoint')
        old=artifact(parent['build_manifest_path'] and dict(path=parent['build_manifest_path'],sha256=parent['build_manifest_sha256']))
        parent_SHA_binding(parent_ref,main,start)
        continuation(parent,old,start,Path(start['replay_root']),main.get('generation') if stage=='scaffold-consume' else None)
    if stage in policy()['pending_statuses']:
        generation=main['generation']; folder=inside(Path(start['replay_root'])/relative(generation['directory']),start['replay_root'])
        need(generated_hashes(Path(start['replay_root']),folder)==generation['source_hashes'], 'source_generated_then_frozen')
        copied=artifact(execution['generated_snapshot'])
        need(copied['original_replay_root']==start['replay_root'] and copied['source_hashes']==generation['source_hashes'], 'immutable_generated_checkpoint_snapshot')
        source_files(copied['snapshot_root'],copied['source_hashes'])
        snapshot=artifact(execution['ledger_snapshot']);need(snapshot==owned_document and execution['ledger_snapshot']['sha256']==main['ownership_snapshot_sha256'],'immutable_owned_checkpoint_snapshot')
        need(execution['status']=='pending_external_build' and execution['independent_cleanup'] is None,'pending_live_ledger_not_dropped_or_accepted')
        return {'status':'pending_external_build','cases_validated':len(main['cases']),'runtime_accepted':False}
    cleanup=execution['independent_cleanup'];need(cleanup is not None,'terminal_independent_cleanup_required')
    cleaned=artifact(cleanup['report']);cp=cleanup['process'];need(cp['exit_code']==0 and cp['timed_out'] is False and cp['termination_code'] is None,'independent_cleanup_process')
    need(cleaned['task']=='TP07-L0' and cleaned['profile']==configuration['profile'] and cleaned['status']=='cleanup_complete'
        and cleaned['process_exit_code']==0,'actual_frozen_L0_cleanup_entrypoint')
    cleanup_start=startup(configuration['cleanup_startup_manifest'],identity,True)
    runtime(cleaned['runtime_identity'],cleanup_start,configuration['cleanup_startup_manifest']['sha256'],'TP07LiteralProbe')
    cleanup_proof(cleaned['cleanup'],owned,helpers,configuration['profile']);cleanup_proof(cleaned['finally_cleanup'],owned,helpers,configuration['profile'])
    return {'status':'stage_validated_in_scope','cases_validated':len(main['cases']),'runtime_accepted':False}


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--execution-receipt',required=True); parser.add_argument('--execution-receipt-sha256',required=True)
    parser.add_argument('--plan',required=True); parser.add_argument('--plan-sha256',required=True); parser.add_argument('--output',required=True)
    args=parser.parse_args(); result={'schema_version':1,'task':'TP07-D-validation','candidate_acceptance':False,'full_temporal_acceptance':False,'accepted':False}
    try:
        summary=validate_receipt(dict(path=str(Path(args.execution_receipt).resolve()),sha256=args.execution_receipt_sha256),dict(path=str(Path(args.plan).resolve()),sha256=args.plan_sha256))
        result.update(summary);result['issues']=[];code=0
    except (ContractError,KeyError,ValueError,TypeError,OSError) as error:
        result['status']='rejected';result['issues']=[str(error) if isinstance(error,ContractError) else type(error).__name__];code=1
    path=Path(args.output);need(not path.exists(),'fresh_validation_output');path.write_text(json.dumps(result,indent=2)+'\n');return code
if __name__=='__main__': raise SystemExit(main())
