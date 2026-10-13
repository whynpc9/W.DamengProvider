#!/usr/bin/env python3
"""Independent finite E contracts. Run/build preparation belongs to Low, D remains pending."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
import types
import xml.etree.ElementTree as ET

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PACK_FILES = tuple('tools/DownstreamAcceptance/TP07/' + name for name in
    ('ef-run.py', 'ef-validate.py', 'ef-cases.json', 'test_ef_contracts.py',
     'runtime-validate.py', 'runtime-cases.json', 'managed_identity.py')) + ('tools/TemporalBoundaryProbe/packed_oracle.py',)
L0_PACK_FILES = tuple('tools/DownstreamAcceptance/TP07/' + name for name in
    ('runtime-run.py', 'runtime-validate.py', 'runtime-cases.json', 'test_runtime_contracts.py',
     'managed_identity.py')) + ('tools/TemporalBoundaryProbe/packed_oracle.py',)
HASH = re.compile(r'[a-f0-9]{64}')
GROUPS = ('roundtrip', 'six', 'mutation', 'generated')
PROFILES = ('shared', 'tls')
FLAGS = ('-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers')
WINDOW_SIZE = 15
RUN_ID = re.compile(r'[a-f0-9]{32}')
E_TRANSACTION_REFS = {
    'source': ('.local/temporal-native/verification/TP07-L0-transaction-v2-prepare/source-manifest.json', '4f50b6188f13b7718f6dbabdc853ab63868c6ac5a36456cf690d012e91695faf'),
    'locks': ('.local/temporal-native/verification/TP07-L0-transaction-v2-lock/lock-manifest.json', '6982a1b12187c1f9ed18810222b5d8347528d4d758822b6697f8f4b5b074ecbb'),
    'startup': ('.local/temporal-native/verification/TP07-L0-transaction-v2-build/startup-manifest.json', 'a1cc53af8ef27db498777cda211975e941270ec903fb830d23387c780cd237a5'),
    'toolpack': ('.local/temporal-native/verification/TP07-L0-transaction-tools-v2/toolpack-manifest.json', 'd4220ee78441af4d79b5ced3d4d10fe2cd3e1b6b391efa0dd337a9cc3e092170'),
    'run': ('.local/temporal-native/verification/TP07-L0-transaction-v2-runtime/run-receipt.json', '2c76e364215ca32067286f8c75759279fe56905569d859a49ef013b3ae6e6da9'),
    'validation': ('.local/temporal-native/verification/TP07-L0-transaction-v2-runtime/independent-validation.json', '266a3054f496cb23d6f1d738e26d90e6b2f6e83fdde3c60328568328d63dfa1b'),
    'product': ('tools/DownstreamAcceptance/TP07/patches/transaction-capability-manifest.v3.json', '400123398ba61ecc05d2ae84fd8c262fa4eb4f7f5d2e034416c7d7eac4fdb3f8'),
    'approval': ('.local/temporal-native/root-approvals/TP07-T1-product-source-v3/source-approval.json', '66c30dc03b860f3847f9e39e2ea1ef01a084f8ceb3bfb18e687bb22bd5b88d6f'),
    'patch': ('tools/DownstreamAcceptance/TP07/patches/transaction-capability.v3.patch', 'd13f018ebd905fcf602447c78804f00c751f58450c95f3bf61bb5fcd548c284f'),
}


def e_transaction_reference(name):
    path, digest = E_TRANSACTION_REFS[name]
    return dict(path=str(ROOT / path), sha256=digest)


def e_transaction_inputs():
    """Independently protect the fixed parent175, product178 and all nested19 refs."""
    pins = {}
    def checked(row):
        path = Path(row['path'])
        need(path.is_relative_to(ROOT) and path.resolve() == path
             and not any(p.is_symlink() for p in (path, *path.parents)), 'E_transaction_ref_root')
        check_reference(row); pins[str(path)] = row['sha256']
        return path
    docs = {}
    for name in E_TRANSACTION_REFS:
        path = checked(e_transaction_reference(name))
        if name != 'patch': docs[name] = load(path)
    product = docs['product']; current = product['source_hashes']
    def refs(value):
        if type(value) is dict:
            if 'path' in value or 'sha256' in value:
                need(set(value) == {'path', 'sha256'}, 'E_transaction_nested_ref_shape')
                return [value]
            return [row for child in value.values() for row in refs(child)]
        if type(value) is list: return [row for child in value for row in refs(child)]
        return []
    nested = refs(product); need(len(nested) == 19, 'E_transaction_nested19')
    for row in nested: checked(row)
    base = load(checked(product['base_approved_source']))
    need(product['base_approved_source']['sha256'] == 'dc2bfd159c6e8b1bd2e61e998b9c4ee5a47d65d444c18602ef35ca5870b495b6'
         and product['base_source_hashes'] == base['source_hashes'] and len(base['source_hashes']) == 175,
         'E_transaction_parent175')
    need(len(current) == 178 and product['source_root'] == str(ROOT / '.local/temporal-native/verification/TP07-T1-transaction-adapter-v3/ef-source'),
         'E_transaction_canonical178')
    expected_approval = dict(schema_version=1, task='TP07-T1-overlay-source-approval', status='source_approved',
        scope='transaction_capability_and_w_retry_contract_tests_only', exact_count=178, exact_paths=sorted(current),
        manifest=e_transaction_reference('product'))
    need(docs['approval'] == expected_approval, 'E_transaction_strict_source_scope')
    for root, hashes in ((ROOT / '.local/temporal-native/verification/TP07-L0-tools-v3/approved-EF-source', base['source_hashes']),
                         (Path(product['source_root']), current)):
        actual = {}
        for path in sorted(root.rglob('*')):
            need(not path.is_symlink(), 'E_transaction_source_symlink')
            if path.is_file(): actual[path.relative_to(root).as_posix()] = sha(path); pins[str(path)] = sha(path)
        need(actual == hashes, 'E_transaction_physical_source_closure')
    docs['base'] = base; docs['pins'] = pins
    return docs


def e_transaction_semantics(source, locks, startup, value, e_hashes):
    """Exact NEW E maps and eleven lineage refs, independent of producer counts."""
    l0 = value['source']; product = value['product']; base = value['base']
    need(set(source) == {'schema_version', 'task', 'source_hashes', 'lineage', 'W', 'lineage_artifacts',
        'approved_ef_source_hashes', 'product_ef_source_hashes', 'runtime_template_hashes', 'E_template_hashes', 'input_reference'}
        and source['schema_version'] == 1 and source['task'] == 'TP07-E prepared source' and source['W'] == product['W'],
        'E_transaction_source_schema')
    need(source['approved_ef_source_hashes'] == l0['approved_ef_source_hashes'] == base['source_hashes'], 'E_transaction_approved175_map')
    need(source['product_ef_source_hashes'] == l0['product_ef_source_hashes'] == product['source_hashes'], 'E_transaction_product178_map')
    need(source['runtime_template_hashes'] == l0['runtime_template_hashes'] and len(l0['runtime_template_hashes']) == 7,
         'E_transaction_templates7')
    expected = dict(product['source_hashes'])
    for name, digest in l0['runtime_template_hashes'].items():
        path = 'test/' + name; need(path not in expected, 'E_transaction_L0_overlap'); expected[path] = digest
    need(expected == l0['source_hashes'] and len(expected) == 185, 'E_transaction_L0_exact185')
    need(len(e_hashes) == 9 and source['E_template_hashes'] == e_hashes, 'E_transaction_E9_map')
    for name, digest in e_hashes.items():
        path = 'test/' + name; need(path not in expected, 'E_transaction_E_overlap'); expected[path] = digest
    need(source['source_hashes'] == expected and len(expected) == 194, 'E_transaction_exact194')
    transaction_refs = {'transaction_product_manifest': e_transaction_reference('product'),
        'transaction_patch': e_transaction_reference('patch'), 'transaction_source_approval': e_transaction_reference('approval')}
    lineage = dict(product['base_lineage'], runtime_template_source_sha256=l0['lineage']['runtime_template_source_sha256'],
        **{name + '_sha256': row['sha256'] for name, row in transaction_refs.items()})
    artifacts = dict(product['base_lineage_artifacts'], approved_ef_source_manifest=product['base_approved_source'],
        runtime_template_source=l0['lineage_artifacts']['runtime_template_source'], **transaction_refs)
    need(l0['lineage'] == lineage and l0['lineage_artifacts'] == artifacts and len(lineage) == len(artifacts) == 10,
         'E_transaction_L0_lineage10')
    e_ref = source['lineage_artifacts']['E_template_source']
    need(e_ref == dict(path=str(ROOT / '.local/temporal-native/verification/TP07-E-transaction-v1-implementation-handoff/E-source-manifest.json'),
        sha256='deb47b37037cb588785e87f7bff83984fe80facf29cb08ca9abf62e46c9b0a9a'), 'E_transaction_E_ref')
    e_document = reference(e_ref)
    need(e_ref['sha256'] == 'deb47b37037cb588785e87f7bff83984fe80facf29cb08ca9abf62e46c9b0a9a'
         and e_document['count'] == 9 and {r['path']: r['sha256'] for r in e_document['files']} == e_hashes,
         'E_transaction_E9_reference')
    lineage['E_template_source_sha256'] = e_ref['sha256']; artifacts['E_template_source'] = e_ref
    need(source['lineage'] == locks['lineage'] == startup['lineage'] == lineage
         and source['lineage_artifacts'] == startup['lineage_artifacts'] == artifacts and len(lineage) == len(artifacts) == 11,
         'E_transaction_lineage11')
    active = {'src/W.EntityFrameworkCore.Dameng/packages.lock.json',
        'test/TP07LiteralProbe/packages.lock.json', 'test/TP07TemporalRuntime/packages.lock.json'}
    need(set(locks['locks']) == set(startup['locks']) == active, 'E_transaction_active_locks3')
    need(locks['locks']['src/W.EntityFrameworkCore.Dameng/packages.lock.json'] == expected['src/W.EntityFrameworkCore.Dameng/packages.lock.json']
         == '77d4c083db2e456a179a270f315e16378c0a3456e87c52a56b1231ad823299a4'
         and locks['locks']['test/TP07LiteralProbe/packages.lock.json'] == value['locks']['lock_sha256']
         == '896258992bb91e4289953fef191287f8e6fe496706d6a3df070a85eb5dc5916e', 'E_transaction_frozen_native_locks')
    expected.update(locks['locks'])
    need(locks['source_hashes'] == startup['source_hashes'] == expected and len(expected) == 196
         and startup['locks'] == locks['locks'], 'E_transaction_exact196')
    return expected


def e_transaction_projection(replay, expected):
    """Exclude only six generated directory prefixes; reject every traversed link."""
    replay = Path(replay)
    need(not replay.is_symlink(), 'E_transaction_projection_symlink')
    generated = tuple(project + '/' + output + '/' for project in (
        'src/W.EntityFrameworkCore.Dameng', 'test/TP07LiteralProbe', 'test/TP07TemporalRuntime')
        for output in ('bin', 'obj'))
    projected = {}
    for path in sorted(replay.rglob('*')):
        need(not path.is_symlink(), 'E_transaction_projection_symlink')
        if not path.is_file(): continue
        name = path.relative_to(replay).as_posix()
        if any(name.startswith(prefix) for prefix in generated): continue
        projected[name] = sha(path)
    need(projected == expected, 'E_transaction_actual196_projection')
    return projected


class ContractError(ValueError): pass


def need(value, code):
    if not value: raise ContractError(code)


def integer(value, code):
    need(type(value) is int, code + '_integer')
    return value


def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load(path):
    def pairs(rows):
        result = {}
        for key, value in rows:
            need(key not in result, 'duplicate_json_key')
            result[key] = value
        return result
    def invalid(_): raise ContractError('non_finite_json')
    return json.loads(Path(path).read_text(), object_pairs_hook=pairs, parse_constant=invalid)


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def policy(): return load(HERE / 'ef-cases.json')


def check_reference(row):
    need(set(row) == {'path', 'sha256'} and HASH.fullmatch(row['sha256'])
         and Path(row['path']).is_absolute(), 'artifact_reference_shape')
    need(sha(row['path']) == row['sha256'], 'artifact_reference_drift')
    return row['path']


def reference(row): return load(check_reference(row))


def pack_entry(path, digest):
    need(type(digest) is str and HASH.fullmatch(digest), 'external_approved_pack_required')
    doc = reference(dict(path=str(Path(path).resolve()), sha256=digest))
    need(set(doc) == {'schema_version', 'task', 'source_hashes'} and doc['schema_version'] == 1
         and doc['task'] == 'TP07-E toolpack' and set(doc['source_hashes']) == set(PACK_FILES), 'finite_E_pack')
    identity = dict(manifest_path=str(Path(path).resolve()), manifest_sha256=digest, source_hashes=doc['source_hashes'])
    pack_check(identity)
    need(doc['source_hashes']['tools/TemporalBoundaryProbe/packed_oracle.py'] == policy()['independent_packed_oracle_sha256'], 'accepted_packet_oracle_pin')
    return identity


def pack_check(identity):
    need(sha(identity['manifest_path']) == identity['manifest_sha256'], 'pack_manifest_drift')
    current = {name: sha(ROOT / name) for name in PACK_FILES}
    need(current == identity['source_hashes'], 'executed_pack_drift')
    return current


def helper(identity, name):
    pack_check(identity)
    path = HERE / name
    raw = path.read_bytes()
    need(hashlib.sha256(raw).hexdigest() == identity['source_hashes'][str(path.relative_to(ROOT))], 'helper_executed_bytes')
    module = types.ModuleType('tp07_E_' + name.replace('.', '_').replace('-', '_'))
    module.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module


def original_l0_validator(pack_reference, run_reference, e_pack):
    """Load the approved L0 source at its original recorded root, never the E tree."""
    pack_check(e_pack)
    document = reference(pack_reference)
    need(set(document) == {'schema_version', 'task', 'source_hashes'} and document['schema_version'] == 1
         and document['task'] == 'TP07-L0 toolpack' and set(document['source_hashes']) == set(L0_PACK_FILES)
         and all(type(value) is str and HASH.fullmatch(value) for value in document['source_hashes'].values()),
         'external_finite_L0_toolpack')
    hashes = document['source_hashes']
    for name in set(PACK_FILES) & set(L0_PACK_FILES):
        need(e_pack['source_hashes'][name] == hashes[name], 'E_L0_shared_source_hash_agreement')
    manifest = Path(pack_reference['path']).resolve()
    new_transaction = pack_reference == e_transaction_reference('toolpack') or run_reference == e_transaction_reference('run')
    if new_transaction:
        need(pack_reference == e_transaction_reference('toolpack') and run_reference == e_transaction_reference('run'),
             'finite_NEW_L0_pack_run_identity')
        source_root = ROOT
    else: source_root = (manifest.parent / 'source').resolve()
    identity = dict(manifest_path=str(manifest), manifest_sha256=pack_reference['sha256'], source_hashes=hashes)
    run = reference(run_reference)
    need(run['toolpack'] == identity, 'original_L0_run_toolpack_binding')
    if new_transaction:
        need(run['startup_manifest_path'] == e_transaction_reference('startup')['path']
             and run['startup_manifest_sha256'] == e_transaction_reference('startup')['sha256'], 'finite_NEW_L0_startup_identity')
    need(run['input_hashes_before'] == run['input_hashes_after'], 'original_L0_run_input_drift')
    checked = {}; pins = {str(manifest): pack_reference['sha256']}
    for name in L0_PACK_FILES:
        expected_path = source_root / name
        suffix = Path(name).parts
        for phase in ('input_hashes_before', 'input_hashes_after'):
            matches = [path for path in run[phase] if Path(path).parts[-len(suffix):] == suffix]
            need(len(matches) == 1, 'unambiguous_original_L0_source_path')
            need(matches[0] == str(expected_path) and run[phase][matches[0]] == hashes[name], 'original_L0_source_path_hash_binding')
        need(expected_path.is_file() and expected_path.resolve() == expected_path, 'original_L0_source_file_required')
        raw = expected_path.read_bytes()
        need(hashlib.sha256(raw).hexdigest() == hashes[name], 'original_L0_source_bytes_drift')
        checked[name] = raw; pins[str(expected_path)] = hashes[name]
    path = source_root / 'tools/DownstreamAcceptance/TP07/runtime-validate.py'
    module = types.ModuleType('tp07_E_original_L0_validator')
    module.__file__ = str(path)
    exec(compile(checked['tools/DownstreamAcceptance/TP07/runtime-validate.py'], str(path), 'exec'), module.__dict__)
    need(module.ROOT == source_root and module.HERE == path.parent, 'natural_original_L0_module_root')
    # Keep all six bytes and their external manifest under E's subsequent pre/post checks.
    unchanged(pins)
    return module, pins


def successful_process(row):
    need(row['exit_code'] == 0 and row['host_exit_code'] == 0 and row['timed_out'] is False
         and row['termination_code'] is None and 0 <= row['actual_elapsed_seconds'] <= row['deadline_seconds'], 'process_success_no_timeout')


def bounded_process(row, deadline):
    need(integer(row['deadline_seconds'], 'fixed_process_deadline') == deadline, 'fixed_window_deadline')
    successful_process(row)


def source_files(replay, hashes):
    need(type(hashes) is dict and hashes, 'source_collection_required')
    for name, digest in hashes.items():
        rel = Path(name)
        need(not rel.is_absolute() and not set(rel.parts) & {'..', '.local', 'secrets'} and '\\' not in name
             and HASH.fullmatch(digest), 'finite_relative_source')
        need(sha(replay / rel) == digest, 'source_lock_asset_drift')


def actual_identity(value, startup, startup_sha, assembly):
    p = policy()
    for key, expected in p['W'].items(): need(value[key] == expected, 'actual_W_' + key)
    need(value['ef_package_version'] == p['ef_version'] and value['build_manifest_sha256'] == startup_sha,
         'actual_EF_startup_binding')
    need(value['source_manifest_sha256_binding'] == startup['source_manifest_sha256']
         and value['lock_manifest_sha256'] == startup['lock_manifest_sha256'], 'actual_source_lock_binding')
    rows = [row for row in startup['startup_assemblies'] if row['name'] == assembly]
    need(len(rows) == 1, 'unique_actual_startup')
    expected = rows[0]
    need(value['startup_assembly'] == assembly and value['startup_sha256'] == expected['dll_sha256']
         and value['startup_mvid'] == expected['mvid'] and value['deps_sha256'] == expected['deps_sha256'], 'actual_startup_identity')
    need(value['ef_provider']['dll_sha256'] == startup['ef_provider']['dll_sha256']
         and value['ef_provider']['mvid'] == startup['ef_provider']['mvid'], 'actual_provider_identity')


def project_dependencies(replay, asset_name, source_hashes):
    """Recognize the three frozen XML shapes and their actual resolved references."""
    p = policy(); replay = Path(replay).resolve()
    assets = load(replay / asset_name); libs = assets['libraries']
    need(libs['W.DamengProvider/' + p['W']['version']]['type'] == 'package'
         and not any(x.lower().startswith('dm.dmprovider/') for x in libs), 'assets_exact_W_no_official')
    for package in ('Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational', 'Microsoft.EntityFrameworkCore.Abstractions'):
        need(libs[package + '/' + p['ef_version']]['type'] == 'package', 'EF_assets_10_0_12')
    project_name = asset_name.replace('/obj/project.assets.json', '/' + asset_name.split('/')[-3] + '.csproj')
    project = replay / project_name
    need(project_name in source_hashes and sha(project) == source_hashes[project_name], 'dependency_project_source_pin')
    tree = ET.parse(project)
    provider_name = 'src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj'
    literal_name = 'test/TP07LiteralProbe/TP07LiteralProbe.csproj'
    runtime_name = 'test/TP07TemporalRuntime/TP07TemporalRuntime.csproj'
    default_reference = '../../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj'
    need(project_name in (provider_name, literal_name, runtime_name), 'finite_dependency_project')
    references = list(tree.iter('ProjectReference'))
    defaults = list(tree.iter('TP07ProviderProject'))
    if project_name == provider_name:
        need(not references and not defaults, 'provider_has_no_project_reference_or_host_override')
    elif project_name == literal_name:
        # A single known default is a source declaration, not general MSBuild expansion.
        need(len(defaults) == 1 and defaults[0].attrib == {'Condition': "'$(TP07ProviderProject)' == ''"}
             and (defaults[0].text or '').strip() == default_reference and not list(defaults[0]), 'exact_L0_provider_property_default')
        need(len(references) == 1 and references[0].attrib == {'Include': '$(TP07ProviderProject)'}
             and not list(references[0]), 'exact_L0_property_project_reference')
    else:
        need(not defaults and len(references) == 1 and references[0].attrib == {'Include': default_reference}
             and not list(references[0]), 'exact_E_literal_project_reference')
    frameworks = assets['project']['restore']['frameworks']
    need(set(frameworks) == {'net10.0'}, 'finite_resolved_project_framework')
    actual_references = frameworks['net10.0']['projectReferences']
    target = str(replay / provider_name)
    expected_references = {} if project_name == provider_name else {target: {'projectPath': target}}
    need(actual_references == expected_references, 'actual_same_replay_provider_reference_no_override')
    packages = [node for node in tree.iter('PackageReference') if node.attrib.get('Include', '').casefold() == 'w.dmprovider']
    need(len(packages) == 1, 'unique_W_PackageReference')
    w = packages[0]
    if project_name == provider_name:
        # There is no MSBuild evaluation/fallback here: accept only this approved central shape.
        need(w.attrib == {'Include': 'W.DamengProvider'} and not list(w), 'frozen_provider_central_reference')
        need(not list(tree.iter('ManagePackageVersionsCentrally')), 'no_provider_central_management_override')
        central = replay / 'Directory.Packages.props'
        need('Directory.Packages.props' in source_hashes and central.is_file()
             and sha(central) == source_hashes['Directory.Packages.props'], 'central_W_props_source_pin')
        parent = project.parent
        while parent != replay:
            need(not (parent / 'Directory.Packages.props').exists(), 'no_nearer_central_W_props')
            parent = parent.parent
        props = ET.parse(central)
        need(all('Condition' not in node.attrib for node in props.iter()), 'unconditional_central_W_props')
        enabled = list(props.iter('ManagePackageVersionsCentrally'))
        need(len(enabled) == 1 and (enabled[0].text or '').strip() == 'true' and not enabled[0].attrib,
             'central_W_management_enabled')
        versions = [node for node in props.iter('PackageVersion') if node.attrib.get('Include', '').casefold() == 'w.dmprovider']
        need(len(versions) == 1 and versions[0].attrib == {'Include': 'W.DamengProvider', 'Version': '[' + p['W']['version'] + ']'}
             and not list(versions[0]), 'central_exact_W_version')
    else:
        version = '[' + p['W']['version'] + ']' if project_name == literal_name else p['W']['version']
        need(w.attrib == {'Include': 'W.DamengProvider', 'Version': version} and not list(w), 'exact_host_W_PackageReference')


def inputs(path, digest, pack, check_l0=True):
    """Exact external Low preparation contract; no latest discovery or self approval."""
    approval = reference(dict(path=str(Path(path).resolve()), sha256=digest))
    need(set(approval) == {'schema_version', 'task', 'artifacts', 'dotnet', 'mapping_manifest_sha256', 'original_repo'}
         and approval['schema_version'] == 1 and approval['task'] == 'TP07-E approved execution', 'external_preparation_schema')
    p = policy()
    need(approval['original_repo'] == p['original_repo'] and HASH.fullmatch(approval['mapping_manifest_sha256']), 'original_profile_source')
    need(set(approval['artifacts']) == {'source', 'locks', 'build', 'startup', 'candidate', 'capabilities',
                                      'l0_run', 'l0_startup', 'l0_toolpack', 'l0_validation',
                                      'partition_gate', 'generated_create_gate'}, 'finite_preparation_artifacts')
    docs = {name: reference(row) for name, row in approval['artifacts'].items()}
    refs = approval['artifacts']; startup = docs['startup']; source = docs['source']; locks = docs['locks']; build = docs['build']
    need(refs['candidate']['sha256'] == p['candidate_sha256'] and refs['capabilities']['sha256'] == p['capabilities_sha256'], 'candidate_capabilities_pin')
    host = approval['dotnet']
    need(set(host) == {'path', 'sha256', 'sdk_version'} and host['sdk_version'] == p['sdk_version']
         and Path(host['path']).is_absolute() and sha(host['path']) == host['sha256'], 'pinned_dotnet_host')
    need(startup['schema_version'] == 1 and startup['source_manifest_sha256'] == refs['source']['sha256']
         and startup['lock_manifest_sha256'] == refs['locks']['sha256'], 'startup_external_source_lock_binding')
    need(startup['source_manifest_path'] == refs['source']['path'] and startup['lock_manifest_path'] == refs['locks']['path'], 'startup_paths_binding')
    lineage = startup['lineage']
    need(lineage['ef_commit'] == p['ef_commit'] and lineage['base_archive_sha256'] == p['ef_archive_sha256']
         and lineage['mapping_manifest_sha256'] == approval['mapping_manifest_sha256']
         and source['lineage'] == locks['lineage'] == lineage, 'approved_mapping_source_lineage')
    need(startup['source_hashes'] == locks['source_hashes'], 'startup_locked_source_set')
    for name, value in source['source_hashes'].items():
        need(startup['source_hashes'].get(name) == value or name.endswith('packages.lock.json'), 'prepared_source_only_lock_delta')
    need(set(startup['source_hashes']) - set(source['source_hashes']) <= set(startup['locks']), 'no_extra_build_source')
    replay = Path(startup['replay_root']).resolve()
    for key in ('source_hashes', 'locks', 'assets', 'binary_hashes'): source_files(replay, startup[key])
    for name, value in p['E_template_hashes'].items(): need(startup['source_hashes'].get('test/' + name) == value, 'frozen_E_nine_source')
    for name in ('CandidateGuard.cs', 'SafeEvidence.cs', 'OwnedObjects.cs'):
        need('test/common/' + name in startup['source_hashes'], 'common_compile_source_closure')
    need('test/TP07LiteralProbe/LiteralOracle.cs' in startup['source_hashes'], 'actual_oracle_compile_source')
    for name in ('Program.cs', 'LiteralCases.cs', 'TP07LiteralProbe.csproj'):
        need('test/TP07LiteralProbe/' + name in startup['source_hashes'], 'same_build_cleanup_host_compile_source')
    compile_files = {str(f.relative_to(replay)) for f in replay.rglob('*') if f.is_file()
                     and not set(f.relative_to(replay).parts) & {'bin', 'obj', '.git'}
                     and f.suffix in ('.cs', '.csproj', '.props', '.targets')}
    need(compile_files <= set(startup['source_hashes']), 'all_actual_compile_inputs_packed')
    expected_assets = {'src/W.EntityFrameworkCore.Dameng/obj/project.assets.json',
        'test/TP07TemporalRuntime/obj/project.assets.json', 'test/TP07LiteralProbe/obj/project.assets.json'}
    need(set(startup['assets']) == expected_assets, 'finite_three_project_assets')
    need(set(startup['locks']) == {name.replace('/obj/project.assets.json', '/packages.lock.json') for name in expected_assets}, 'finite_three_locks')
    for name in expected_assets:
        project_dependencies(replay, name, startup['source_hashes'])
    need({row['name'] for row in startup['startup_assemblies']} == {'TP07TemporalRuntime', 'TP07LiteralProbe'}
         and len(startup['startup_assemblies']) == 2, 'finite_startup_assemblies')
    managed = helper(pack, 'managed_identity.py')
    actual_binaries = {}
    for row in startup['startup_assemblies']:
        binary = replay / ('test/' + row['name'] + '/bin/Debug/net10.0')
        dll = binary / (row['name'] + '.dll')
        need(sha(dll) == row['dll_sha256'] and managed.mvid(dll.read_bytes()) == row['mvid']
             and sha(dll.with_suffix('.deps.json')) == row['deps_sha256'], 'startup_managed_byte_identity')
        need(sha(binary / 'W.DamengProvider.dll') == p['W']['dll_sha256']
             and managed.mvid((binary / 'W.DamengProvider.dll').read_bytes()) == p['W']['mvid'], 'actual_W_binary')
        need(sha(binary / 'W.EntityFrameworkCore.Dameng.dll') == startup['ef_provider']['dll_sha256']
             and managed.mvid((binary / 'W.EntityFrameworkCore.Dameng.dll').read_bytes()) == startup['ef_provider']['mvid'], 'actual_EF_binary')
        actual = {str(f.relative_to(replay)): sha(f) for f in binary.rglob('*') if f.is_file()}
        need(actual and all(startup['binary_hashes'].get(name) == value for name, value in actual.items()), 'complete_startup_binary_closure')
        actual_binaries.update(actual)
    need(actual_binaries == startup['binary_hashes'], 'exact_two_startup_output_directories')
    need(build['schema_version'] == 1 and build['task'] == 'TP07-E build' and build['sdk_version'] == p['sdk_version']
         and build['source_manifest_sha256'] == refs['source']['sha256']
         and build['lock_manifest_sha256'] == refs['locks']['sha256']
         and build['startup_manifest_sha256'] == refs['startup']['sha256'], 'external_build_receipt_binding')
    need(build['source_hashes_before'] == build['source_hashes_after'] == startup['source_hashes'], 'build_source_before_after')
    need(build['commands'], 'real_build_commands_required')
    for command in build['commands']: successful_process(command)
    cmds = [row['command'] for row in build['commands']]
    need(any(cmd == [host['path'], '--version'] for cmd in cmds), 'actual_pinned_SDK_command')
    for project in ('test/TP07TemporalRuntime/TP07TemporalRuntime.csproj', 'test/TP07LiteralProbe/TP07LiteralProbe.csproj'):
        for phase, flag in (('restore', '--locked-mode'), ('build', '--no-restore')):
            need(any(cmd[:3] == [host['path'], phase, project] and flag in cmd and set(FLAGS) <= set(cmd) for cmd in cmds), 'locked_single_node_' + phase)
    new_transaction = refs['l0_toolpack'] == e_transaction_reference('toolpack') or refs['l0_run'] == e_transaction_reference('run')
    transaction_pins = {}
    if new_transaction:
        for name in ('run', 'startup', 'toolpack', 'validation'):
            need(refs['l0_' + name] == e_transaction_reference(name), 'E_transaction_execution_refs')
        value = e_transaction_inputs()
        need(docs['l0_startup'] == value['startup'] and docs['l0_validation'] == value['validation'], 'E_transaction_L0_documents')
        e_transaction_semantics(source, locks, startup, value, p['E_template_hashes'])
        e_transaction_projection(replay, startup['source_hashes'])
        transaction_pins = value['pins']
    oracle, original_l0_pins = original_l0_validator(refs['l0_toolpack'], refs['l0_run'], pack)
    l0 = docs['l0_startup']
    need(all(l0['lineage'][key] == lineage[key] for key in ('ef_commit', 'base_archive_sha256', 'bridge_patch_sha256',
         'mapping_manifest_sha256', 'mapping_patch_sha256', 'approved_ef_source_manifest_sha256')), 'L0_same_new_mapping_source')
    for name, digest0 in l0['source_hashes'].items():
        if name.startswith('src/') or name.startswith('test/common/') or name.startswith('test/TP07LiteralProbe/') and not name.endswith('packages.lock.json'):
            need(startup['source_hashes'].get(name) == digest0, 'L0_same_actual_provider_source')
    need(docs['l0_validation']['l0_value_contract_valid'] is True
         and docs['l0_validation']['run_receipt_sha256'] == refs['l0_run']['sha256']
         and docs['l0_validation']['startup_manifest_sha256'] == refs['l0_startup']['sha256'], 'L0_saved_validation_binding')
    if check_l0:
        live = oracle.validate_artifact(Path(refs['l0_run']['path']).parent, refs['l0_startup']['sha256'],
             refs['l0_toolpack']['path'], refs['l0_toolpack']['sha256'])
        need(live['l0_value_contract_valid'] is True and live == docs['l0_validation'], 'independent_new_L0_gate')
    gate_pins = offline_gates(docs, refs, startup, pack, host)
    pins = {str(Path(path).resolve()): sha(path) for path in
            [path, pack['manifest_path'], host['path']] + [row['path'] for row in refs.values()] + [str(ROOT / name) for name in PACK_FILES]}
    for key in ('source_hashes', 'locks', 'assets', 'binary_hashes'):
        pins.update({str(replay / name): digest0 for name, digest0 in startup[key].items()})
    pins.update(original_l0_pins)
    pins.update(transaction_pins)
    pins.update(gate_pins)
    for profile in PROFILES:
        wrapper = Path(p['original_repo']) / 'scripts' / ('with-dameng-tls-test.sh' if profile == 'tls' else 'with-dameng-test.sh')
        need(sha(wrapper) == p['wrapper_hashes'][profile], 'original_wrapper_pin')
        pins[str(wrapper)] = sha(wrapper)
    for row in startup['lineage_artifacts'].values():
        check_reference(row); pins[row['path']] = row['sha256']
    return dict(approval=approval, documents=docs, pack=pack, pins=pins, oracle=oracle,
                execution_reference=dict(path=str(Path(path).resolve()), sha256=digest))


def unchanged(pins):
    now = {name: sha(name) for name in pins}
    need(now == pins, 'execution_input_before_after_drift')
    return now


def specs(profile, group):
    rows = [row for row in policy()['cases'] if row['profile'] == profile and row['group'] == group]
    need(len(rows) == policy()['required_counts'][profile][group] and len({row['case_id'] for row in rows}) == len(rows), 'independent_finite_registry')
    return rows


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def window_specs(profile, group, index):
    rows = specs(profile, group)
    need(type(index) is int and 0 <= index < (len(rows) + WINDOW_SIZE - 1) // WINDOW_SIZE, 'finite_window_index')
    return rows[index * WINDOW_SIZE:(index + 1) * WINDOW_SIZE]


def window_descriptor(profile, group, index):
    rows = window_specs(profile, group, index)
    return dict(profile=profile, group=group, index=index, start=index * WINDOW_SIZE, count=len(rows),
                full_group_count=len(specs(profile, group)), case_ids=[s['case_id'] for s in rows])


def windows():
    return [window_descriptor(p, g, i) for p in PROFILES for g in GROUPS
            for i in range((len(specs(p, g)) + WINDOW_SIZE - 1) // WINDOW_SIZE)]


def window_contract():
    expected = dict(version=1, size=15, total_windows=25, total_cases=338, windows=windows())
    need(digest(policy()['window_contract']) == digest(expected), 'fixed_window_policy_contract')
    need(len(expected['windows']) == 25 and sum(w['count'] for w in expected['windows']) == 338, 'finite_window_registry_coverage')
    return expected


def control_prefix(rows):
    positive = False
    for s in rows:
        if s['group'] == 'six' and s['async_api'] and s['contract'] != 'exact':
            need(positive, 'same_window_async_control_prefix_missing')
        if (s['group'] == 'six' and s['async_api'] and s['contract'] == 'exact' and s['ticks'] is not None
                and s['shape'] in ('facet6', 'store6', 'native')): positive = True


def runtime_definition(s):
    """Independent mapping of the unchanged Models.cs routes and expected semantics."""
    group, shape, api = s['group'], s['shape'], s['async_api']
    expected = 'exact'
    if group == 'roundtrip':
        route = 'same_context_SaveChanges_value_then_NULL_parameterized_query' if shape == 'reuse' else 'actual_SaveChanges_fresh_AsNoTracking'
        if shape == 'reuse': expected = 'exact_NULL'
    elif group == 'mutation':
        route = 'actual_EF_tracked_update_transaction'
        if shape == 'concurrency': expected = 'DbUpdateConcurrencyException_then_fresh_unchanged'
    elif group == 'generated':
        route = 'actual_generated_migration_SQL_EF_readback'
        if shape == 'clock_default': expected = 'reported_actual_clock_precision'
        elif s['case_id'] != 'EF-R04-' + s['profile'] + '-' + shape + ('-async' if api else '-sync'): expected = 'exact_boundary'
    elif shape == 'advisory7':
        route = 'EF_mapping_created_parameter_controlled_ADO_execute_not_SaveChanges'
        expected = 'NotSupported_with_observed_advisory_or_fixed7_metadata_verified_async_Execute0_positive_control'
    else:
        route = 'actual_SaveChanges_existing_fixed6' if shape in ('facet6', 'store6', 'default7_fixed6') else 'actual_SaveChanges_fresh_native_query'
        if shape == 'default7_fixed6': expected = 'actual_describe_explicit7_branch_fresh_state'
        elif s['contract'] != 'exact':
            expected = ('Overflow' if s['contract'] == 'System.OverflowException' else 'Argument') + '_fresh_rows0_' + (
                'verified_async_Execute0_positive_control' if api else 'sync_send_unobserved')
    return {**{k: s[k] for k in ('case_id', 'group', 'profile', 'shape', 'kind', 'ticks', 'offset_minutes', 'scale')},
            'async': api, 'route': route, 'expected': expected}


def preparation_identity(startup, startup_sha, pack):
    return dict(startup_manifest_sha256=startup_sha, source_manifest_sha256=startup['source_manifest_sha256'],
                lock_manifest_sha256=startup['lock_manifest_sha256'], toolpack_manifest_sha256=pack['manifest_sha256'],
                policy_sha256=sha(HERE / 'ef-cases.json'), window_contract_sha256=digest(window_contract()))


def cohort_id(context):
    return digest(dict(preparation=preparation_identity(context['documents']['startup'], context['approval']['artifacts']['startup']['sha256'], context['pack']),
                       approved_execution=context['execution_reference']))


def validate_partition_report(report, startup, startup_sha, pack):
    need(report['schema_version'] == 1 and report['task'] == 'TP07-E offline partition'
         and report['database_accessed'] is False and report['candidate_guard_constructed'] is False
         and report['owned_objects_constructed'] is False and report['runtime_context_constructed'] is False
         and report['process_exit_code'] == 0 and integer(report['window_size'], 'compiled_window_size') == WINDOW_SIZE,
         'actual_compiled_offline_partition')
    all_specs = [s for p in PROFILES for g in GROUPS for s in specs(p, g)]
    need(digest(report['cases']) == digest([runtime_definition(s) for s in all_specs]), 'compiled_registry_definition_order')
    need(digest(report['windows']) == digest(window_contract()['windows']), 'compiled_fixed_window_coverage')
    for w in report['windows']: control_prefix(window_specs(w['profile'], w['group'], w['index']))
    refusals = report['argument_refusals']
    need([r['test_id'] for r in refusals] == ['missing_index', 'negative_index', 'out_of_range', 'non_integer',
         'arbitrary_size', 'arbitrary_range', 'arbitrary_ids', 'generated_mix', 'partition_mix']
         and all(r['status'] == 'refused' and type(r['assertion']) is str and r['assertion'] for r in refusals), 'compiled_illegal_argument_refusals')
    row = next(r for r in startup['startup_assemblies'] if r['name'] == 'TP07TemporalRuntime')
    need(report['host_identity'] == {k: row[k] for k in ('dll_sha256', 'mvid', 'deps_sha256')}, 'compiled_partition_actual_host')
    for name, value in policy()['E_template_hashes'].items():
        need(startup['source_hashes']['test/' + name] == value, 'partition_new_E_source')
    return dict(schema_version=1, task='TP07-E independent compiled partition validation',
                partition_contract_valid=True, database_accessed=False, cases_validated=338, windows_validated=25,
                identity=preparation_identity(startup, startup_sha, pack), host_identity=report['host_identity'])


def offline_gates(docs, refs, startup, pack, host):
    """Gate schemas: partition {schema_version,task,identity,process,report,validation};
    generated {schema_version,task,identity,profiles,commands,validation}, profiles are
    exactly [{profile,process,report}] in shared/tls order. Every document is a ref.
    Generated validation retains the existing actual_offline_GeneratedCreate28 readback.
    """
    identity = preparation_identity(startup, refs['startup']['sha256'], pack)
    pins = {}
    def read(row):
        check_reference(row); pins[row['path']] = row['sha256']; return reference(row)
    for name in ('partition_gate', 'generated_create_gate'):
        need(docs[name]['schema_version'] == 1 and docs[name]['identity'] == identity, 'same_new_compiled_offline_gate')
    gate = docs['partition_gate']
    need(set(gate) == {'schema_version', 'task', 'identity', 'process', 'report', 'validation'} and gate['task'] == 'TP07-E compiled partition gate', 'finite_partition_gate_schema')
    report = read(gate['report']); bounded_process(gate['process'], 180)
    dll = str(Path(startup['replay_root']) / 'test/TP07TemporalRuntime/bin/Debug/net10.0/TP07TemporalRuntime.dll')
    need(gate['process']['command'] == [host['path'], dll, '--offline-partition', 'true', '--output', str(Path(gate['report']['path']).parent)]
         and gate['process']['cwd'] == startup['replay_root'], 'actual_compiled_partition_command')
    expected = validate_partition_report(report, startup, refs['startup']['sha256'], pack)
    expected['partition_report_sha256'] = gate['report']['sha256']
    need(read(gate['validation']) == expected, 'independent_compiled_partition_result')
    gate = docs['generated_create_gate']
    need(set(gate) == {'schema_version', 'task', 'identity', 'profiles', 'commands', 'validation'}
         and gate['task'] == 'TP07-E generated-create28 gate' and [r['profile'] for r in gate['profiles']] == list(PROFILES), 'finite_generated28_gate_schema')
    commands = read(gate['commands']); result = read(gate['validation'])
    shapes = ['roundtrip', 'facet6', 'store6', 'default7_fixed6', 'concurrency', 'native', 'DATE_nonmidnight',
              'constant_default', 'HasData', 'sql_default', 'NULL_default', 'inline', 'update_expression', 'clock_default']
    expected_ids = ['OFF-DDL-' + p + '-' + s for p in PROFILES for s in shapes]
    need(result['accepted_deep_baseline']['sha256'] == 'c6f269c52b17f67ac8ed22054a4823cd44a1f547bb2ac8ff8cc5da522a2e5276'
         and result['accepted_cohort_baseline']['sha256'] == 'ec3f90e7ce1817876e089a101e080c17a49dc0b8afb3b80b9ad248f999e82682', 'accepted_generated28_provenance')
    baseline = read(result['accepted_deep_baseline']); read(result['accepted_cohort_baseline'])
    need(set(result['baseline_reports']) == set(PROFILES) and result['semantic_comparison'] == dict(
        method='exact_accepted_model_operation_seed_default_and_SQL_bytes', case_ids=expected_ids,
        model_operation_seed_default_drift=[], SQL_byte_drift=[]), 'actual_new_deep_baseline_comparison')
    need(result['status'] == 'actual_offline_GeneratedCreate28_independently_verified' and result['total_cases'] == 28
         and result['DB_executed'] is False and result['TEST_wrapper_executed'] is False and result['runtime_accepted'] is False
         and result['startup_manifest_sha256'] == refs['startup']['sha256'] and result['commands_sha256'] == gate['commands']['sha256']
         and result['old_input_drift'] == result['W_product_drift'] == [], 'new_actual_deep_generated28_readback')
    runtime = next(r for r in startup['startup_assemblies'] if r['name'] == 'TP07TemporalRuntime')
    need(result['actual_E_DLL'] == runtime and result['actual_EF_provider'] == startup['ef_provider'], 'generated28_same_compiled_cohort')
    need([r['case_id'] for r in result['case_checks']] == expected_ids
         and all(all(r[k] is True for k in ('N_exact_VARCHAR32_or_absent', 'actual_SQL_checked', 'io_zero', 'models_defaults_seeds_checked'))
                 for r in result['case_checks']), 'all_28_deep_SQL_model_checks')
    need(len(commands) == 2, 'two_actual_generated28_commands')
    for row, original in zip(gate['profiles'], commands):
        need(set(row) == {'profile', 'process', 'report'}, 'finite_generated_profile_gate')
        p = row['profile']; report = read(row['report']); bounded_process(row['process'], 180)
        old_ref = result['baseline_reports'][p]; old = read(old_ref)
        need(old_ref['sha256'] == baseline['profiles'][p]['report_sha256'], 'actual_accepted_deep_profile_report')
        expected_command = [host['path'], dll, '--offline-generated-create', 'true', '--profile', p,
            '--candidate-manifest', refs['candidate']['path'], '--capabilities', refs['capabilities']['path'], '--output', str(Path(row['report']['path']).parent)]
        need(row['process']['command'] == original['argv'] == expected_command and original['exit_code'] == 0
             and original['timed_out'] is False and original['termination_code'] is None, 'actual_new_generated28_command')
        need(report['task'] == 'TP07-E offline GeneratedCreate' and report['profile'] == p and report['offline_ddl_accepted'] is True
             and report['database_accessed'] is False and report['owned_objects_constructed'] is False and report['process_exit_code'] == 0
             and report['required_model_count'] == 14 and report['required_shapes'] == shapes
             and [r['case_id'] for r in report['cases']] == ['OFF-DDL-' + p + '-' + s for s in shapes]
             and result['profiles'][p]['case_count'] == 14 and result['profiles'][p]['report_sha256'] == row['report']['sha256'], 'actual_14_models_each_profile')
        actual_identity(report['candidate_identity'], startup, refs['startup']['sha256'], 'TP07TemporalRuntime')
        need([c['case_id'] for c in old['cases']] == [c['case_id'] for c in report['cases']], 'accepted_28_case_order')
        for case, prior in zip(report['cases'], old['cases']):
            need(case['status'] == 'completed', 'all_actual_generated_models_complete')
            generated28_semantics(case, prior)
            need(case['actual_generator']['type'] == prior['actual_generator']['type']
                 and case['actual_generator']['assembly']['dll_sha256'] == startup['ef_provider']['dll_sha256']
                 and case['actual_generator']['assembly']['mvid'] == startup['ef_provider']['mvid'], 'generated28_actual_new_provider')
            for sql, old_sql in zip(case['generated_SQL'], prior['generated_SQL']):
                path = Path(row['report']['path']).parent / sql['path']
                need(path.resolve() == path and path.parent == Path(row['report']['path']).parent and sha(path) == sql['sha256'], 'new_generated28_SQL_bytes')
                pins[str(path)] = sql['sha256']
                old_path = Path(old_ref['path']).parent / old_sql['path']
                need(old_path.resolve() == old_path and old_path.parent == Path(old_ref['path']).parent and sha(old_path) == old_sql['sha256']
                     and path.read_bytes() == old_path.read_bytes(), 'all_actual_generated28_SQL_bytes_equal_accepted_deep_gate')
                pins[str(old_path)] = old_sql['sha256']
        generated28_io(report['io'])
    return pins


def generated28_io(io):
    need(all(integer(io[k], 'actual_offline_' + k) == 0 for k in ('sync_open_attempts', 'async_open_attempts', 'sync_command_attempts', 'async_command_attempts'))
         and io['all_factory_connections_closed'] is True and integer(io['factory_connection_count'], 'factory_count') == len(io['factory_connections'])
         and io['factory_connection_count'] > 0, 'actual_generated28_zero_IO_closed_factory')
    for connection in io['factory_connections']:
        need(connection['state'] == 'Closed' and connection['actual']['type'] == 'W.Dm.DmConnection'
             and connection['actual']['dll_sha256'] == policy()['W']['dll_sha256']
             and connection['actual']['mvid'] == policy()['W']['mvid'], 'actual_generated28_factory_W_identity')


def generated28_semantics(case, prior):
    keys = ('case_id', 'shape', 'runtime_spec', 'database_spec', 'runtime_model', 'database_model',
            'migration_operations', 'seed_operations', 'columns', 'generated_SQL')
    need(digest({k: case[k] for k in keys}) == digest({k: prior[k] for k in keys}), 'all_new_models_operations_seeds_defaults_equal_accepted_deep_gate')
    generated28_io(case['io'])


def required_cases(report, profile, group):
    w = report['window']; need(digest(w) == digest(window_descriptor(profile, group, w['index'])), 'independent_report_window_descriptor')
    expected = window_specs(profile, group, w['index']); ids = [s['case_id'] for s in expected]
    need([i['case_id'] for i in report['cases']] == ids and len(set(ids)) == len(ids)
         and [i['case_id'] for i in report['required_cases']] == ids
         and integer(report['required_case_count'], 'required_case_count') == len(ids)
         and integer(report['full_group_case_count'], 'full_group_count') == len(specs(profile, group)), 'missing_duplicate_reordered_E_cases')
    need(digest(report['required_cases']) == digest([runtime_definition(s) for s in expected]), 'compiled_window_required_case_definitions')
    control_prefix(expected)
    return expected


def steps(item, required, failed=()):
    values = item['steps']
    names = [row['step_id'] for row in values]
    definition = item.get('definition', {})
    reuse = definition.get('shape') == 'reuse' and definition.get('group') == 'roundtrip' and definition.get('kind') in ('dt', 'dto')
    repeated = 'fresh_independent_' + ('Z' if definition.get('kind') == 'dto' else 'D')
    unique_names = [name for name in names if not reuse or name != repeated]
    need(len(unique_names) == len(set(unique_names)), 'duplicate_operation_step')
    if reuse:
        independent_positions = [i for i, name in enumerate(names) if name == repeated]
        need(len(independent_positions) == 3 and all(name == repeated for name in names if name.startswith('fresh_independent_')),
             'reuse_exact_three_same_column_reads')
        boundaries = ('fresh_AsNoTracking', 'SaveChanges_value_only', 'actual_parameterized_query_changed_value',
                      'SaveChanges_NULL_only', 'fresh_AsNoTracking_NULL')
        need(all(name in names for name in boundaries), 'reuse_read_write_boundaries_missing')
        b = [names.index(name) for name in boundaries]
        need(b[0] < independent_positions[0] < b[1] < b[2] < independent_positions[1] < b[3] < b[4]
             < independent_positions[2] == len(names) - 1, 'reuse_initial_changed_NULL_read_order')
    need(all(name in names for name in required), 'actual_EF_operation_step_missing')
    position = [names.index(name) for name in required]
    need(position == sorted(position), 'operation_step_order')
    need({row['step_id'] for row in values if row['status'] == 'failed'} == set(failed), 'only_exact_expected_failed_steps')
    for row in values:
        need(row['status'] in ('completed', 'failed'), 'partial_operation_step')
        if row['status'] == 'completed' and not row['step_id'].startswith('fresh_independent_'):
            need(row.get('call_returned') is True, 'operation_call_returned')
    return {row['step_id']: row for row in values if not reuse or row['step_id'] != repeated}


def value(actual, kind, ticks, offset=None):
    if ticks is None:
        need(actual == {'is_null': True}, 'actual_typed_NULL')
        return
    expected = {'dt': 'DateTime', 'dto': 'DateTimeOffset', 'date': 'DateOnly', 'time': 'TimeOnly', 'interval': 'TimeSpan'}[kind]
    need(actual['clr_type'] == expected, 'actual_value_CLR_type')
    if kind == 'date': need(integer(actual['day_number'], 'native_day') == ticks // 864000000000, 'native_date_input')
    else: need(integer(actual['local_ticks' if kind == 'dto' else 'ticks'], 'actual_ticks') == ticks, 'actual_value_ticks')
    if kind == 'dt': need(actual['kind'] == 'Unspecified', 'DateTime_unspecified')
    if kind == 'dto': need(integer(actual['offset_minutes'], 'actual_offset') == offset, 'actual_original_offset')


def row(actual, spec, ticks, n=None, pair=False, zticks=None, offset=None):
    need(integer(actual['id'], 'entity_id') == 1, 'entity_key')
    if pair:
        value(actual['d'], 'dt', ticks)
        value(actual['z'], 'dto', zticks, spec['offset_minutes'] if offset is None else offset)
    else:
        key = {'dt': 'd', 'dto': 'z', 'date': 'a', 'time': 't', 'interval': 'i'}[spec['kind']]
        value(actual[key], spec['kind'], ticks, spec['offset_minutes'])
    if n is not None: need(actual['n'] == {'clr_type': 'String', 'value': n}, 'fresh_entity_N')


def exact_chain(chain, leaf):
    need(type(chain) is list and 1 <= len(chain) <= 8, 'bounded_actual_exception_chain')
    need([integer(x['depth'], 'chain_depth') for x in chain] == list(range(len(chain))), 'actual_chain_depth_order')
    need(chain[-1]['type'] == leaf and all(x['type'] == 'Microsoft.EntityFrameworkCore.DbUpdateException' for x in chain[:-1]), 'exact_exception_type_no_infrastructure')
    for x in chain: need(x['safe']['type'] == x['type'], 'exception_safe_type_binding')


def command_window(report, spec, step, success=True, reader=False, parameters=None):
    stage = spec['case_id'] + '/' + step
    rows = [x for x in report['commands'] if x['stage'] == stage]
    attempted = [x for x in rows if x['status'] == 'attempted']
    need(attempted, 'genuine_EF_command_attempt')
    allowed = ('ReaderExecutingAsync', 'NonQueryExecutingAsync') if spec['async_api'] else ('ReaderExecuting', 'NonQueryExecuting')
    for start in attempted:
        need(start['api'] in allowed and type(start['ef_command_id']) is str and re.fullmatch(r'[a-f0-9-]{36}', start['ef_command_id']), 'genuine_EF_interceptor_route')
        need(integer(start['attempt_id'], 'command_attempt') > 0 and HASH.fullmatch(start['sql_sha256']), 'bounded_command_identity')
        for role in ('command', 'connection'): report['_oracle'].route(start[role], role)
        for param in start['parameters']: report['_oracle'].route(param['actual'], 'parameter')
        ends = [x for x in rows if x['attempt_id'] == start['attempt_id'] and x['status'] in ('completed', 'failed')]
        need(len(ends) == 1 and ends[0]['status'] == ('completed' if success else 'failed')
             and ends[0]['sql_sha256'] == start['sql_sha256'], 'same_actual_command_completion')
        end = ends[0]
        if reader: report['_oracle'].route(end['reader'], 'reader')
        if not success:
            need(end.get('current_binding_observed') is False and end.get('current_binding') is None and 'binding' not in end, 'refusal_binding_never_published')
        if parameters is not None: need(len(start['parameters']) == parameters, 'actual_parameter_count')
    return attempted


def parameter_values(report, spec, step, expected, success=True):
    starts = command_window(report, spec, step, success)
    actual = [p for start in starts for p in start['parameters'] if p['db_type'] in ('DateTime2', 'DateTimeOffset')]
    need(actual, 'actual_EF_temporal_parameter_missing')
    for kind, ticks in expected:
        selected = [p for p in actual if p['db_type'] == ('DateTime2' if kind == 'dt' else 'DateTimeOffset')
                    and p['value'].get('local_ticks' if kind == 'dto' else 'ticks') == ticks] if ticks is not None else [p for p in actual if p['db_type'] == ('DateTime2' if kind == 'dt' else 'DateTimeOffset') and p['value'].get('is_null') is True]
        need(selected, 'actual_current_original_parameter_missing')
        for param in selected:
            need(param['precision'] == 0 and type(param['precision']) is int and param['scale'] == spec['scale']
                 and param['explicit_scale'] is True and param['direction'] == 'Input', 'actual_temporal_facets')
            value(param['value'], kind, ticks, spec['offset_minutes'])
    if success and 'SaveChanges' in step:
        s = source(report, spec['case_id'] + '/' + step)
        for p in s['parameters']:
            if p['request']['db_type'] in ('DateTime2', 'DateTimeOffset'):
                raw_tuple(p['raw'], 'dt' if p['request']['db_type'] == 'DateTime2' else 'dto', spec['scale'])
        published(report, s, spec['scale'], spec['scale'], 'VerifiedSeven' if spec['profile'] == 'shared' else 'VerifiedSix')


def concurrency_parameters(report, item, spec):
    """Infer the controlled EF original role jointly; SQL hashes do not prove a WHERE clause."""
    need(spec['profile'] == 'shared' and spec['group'] == 'mutation' and spec['shape'] == 'concurrency'
         and spec['scale'] == 7 and spec['kind'] in ('dt', 'dto') and type(spec['ticks']) is int
         and spec['offset_minutes'] == (330 if spec['kind'] == 'dto' else None), 'measured_shared_concurrency_scope')
    need(item['case_id'] == spec['case_id'] and item['status'] == 'completed', 'controlled_concurrency_case_identity')
    for key in ('case_id', 'group', 'profile', 'shape', 'kind', 'scale', 'ticks', 'offset_minutes'):
        need(item['definition'][key] == spec[key], 'controlled_concurrency_definition')
    need(item['definition']['async'] is spec['async_api'], 'controlled_concurrency_API')
    value(item['original_temporal_concurrency_token'], spec['kind'], spec['ticks'], spec['offset_minutes'])
    st = steps(item, ['seed_SaveChanges', 'fresh_state_before_mutation', 'tracking_query_current',
        'tracking_query_stale', 'winner_SaveChanges', 'fresh_state_after_winner_before_stale',
        'stale_SaveChanges', 'fresh_after_stale_failure', 'fresh_state_after_stale_failure'], ['stale_SaveChanges'])
    row(st['tracking_query_current']['result'], spec, spec['ticks'], 'first')
    row(st['tracking_query_stale']['result'], spec, spec['ticks'], 'first')
    exact_chain(st['stale_SaveChanges']['actual_exception_chain'], 'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException')
    need(item['expected_concurrency_exception']['actual_chain'] == st['stale_SaveChanges']['actual_exception_chain']
         and item['expected_concurrency_exception']['expected_leaf_type'] == 'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException',
         'joint_concurrency_actual_exception')
    row(st['fresh_after_stale_failure']['result'], spec, spec['ticks'] + 1, 'changed')
    owned_spec = dict(spec, owned_primary_key=item['owned_primary_key'])
    before = catalog_state(st['fresh_state_before_mutation'], owned_spec, 1)
    winner = catalog_state(st['fresh_state_after_winner_before_stale'], owned_spec, 1)
    same_state(before, winner)
    same_state(winner, catalog_state(st['fresh_state_after_stale_failure'], owned_spec, 1))
    # SaveChanges==1 is asserted by the frozen C# caller; the step records return, not a count.
    independent(item, spec, [(spec['kind'], spec['ticks'] + 1, spec['offset_minutes'])], report['_oracle'])
    locations = {'current': (0, 1), 'n': (1, 3), 'id': (2, 0), 'original': (3, 2)} if spec['kind'] == 'dt' else {
        'n': (0, 1), 'current': (1, 2), 'id': (2, 0), 'original': (3, 3)}
    db_type = 'DateTime2' if spec['kind'] == 'dt' else 'DateTimeOffset'
    provider_type = 'DateTime' if spec['kind'] == 'dt' else 'DateTimeOffset'
    ctype, width = (26, 9) if spec['kind'] == 'dt' else (27, 11)
    stages = []
    for step, delta, n_value in (('winner_SaveChanges', 1, 'changed'), ('stale_SaveChanges', 2, 'stale')):
        starts = command_window(report, spec, step, reader=True, parameters=4)
        need(len(starts) == 1, 'one_concurrency_command_attempt')
        start = starts[0]; stage = spec['case_id'] + '/' + step; s = source(report, stage)
        ends = [x for x in report['commands'] if x['stage'] == stage and x['status'] == 'completed']
        need(len(ends) == 1 and start['api'] == ('ReaderExecutingAsync' if spec['async_api'] else 'ReaderExecuting')
             and ends[0]['api'] == ('ReaderExecutedAsync' if spec['async_api'] else 'ReaderExecuted')
             and len([x for x in report['commands'] if x['stage'] == stage]) == 2, 'actual_concurrency_EF_reader_route')
        end = ends[0]
        need(0 < integer(start['sql_length'], 'concurrency_sql_length') <= 16000, 'bounded_concurrency_SQL_receipt')
        parameters = s['parameters']
        need(len(parameters) == 4, 'whole_concurrency_four_parameters')
        temporal = [p for p in parameters if p['request']['db_type'] == db_type]
        need(len(temporal) == 2, 'two_concurrency_temporal_values')
        tick_key = 'ticks' if spec['kind'] == 'dt' else 'local_ticks'
        # Roles are determined by the controlled model/token and distinct values before looking at flags.
        roles = {}
        for role, ticks0 in (('current', spec['ticks'] + delta), ('original', spec['ticks'])):
            candidates = [p for p in temporal if p['request']['value'].get(tick_key) == ticks0]
            need(len(candidates) == 1, 'unique_current_original_concurrency_values')
            roles[role] = candidates[0]
        for role, kind0 in (('n', 'AnsiString'), ('id', 'Int32')):
            candidates = [p for p in parameters if p['request']['db_type'] == kind0]
            need(len(candidates) == 1, 'complete_concurrency_N_ID_roles'); roles[role] = candidates[0]
        need(len({p['ordinal'] for p in roles.values()}) == 4, 'distinct_concurrency_parameter_roles')
        bindings = end['binding']
        need([integer(b['ordinal'], 'concurrency_binding_ordinal') for b in bindings] == list(range(4)), 'whole_concurrency_published_binding')
        for role, p in roles.items():
            need((p['ordinal'], p['public_parameter_index']) == locations[role], 'measured_concurrency_role_mapping')
            request = p['request']; raw = p['raw']; b = bindings[p['ordinal']]
            for key in ('c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask'):
                integer(raw[key], 'concurrency_raw_' + key)
            need(request['direction'] == 'Input' and request['precision'] == 0 and request['explicit_precision'] is False,
                 'concurrency_input_request')
            if role in ('current', 'original'):
                need(request['provider_type'] == provider_type and request['scale'] == 7 and request['explicit_scale'] is True,
                     'concurrency_exact_temporal_request')
                ticks0 = spec['ticks'] + delta if role == 'current' else spec['ticks']
                value(request['value'], spec['kind'], ticks0, spec['offset_minutes'])
                expected_value = dict(clr_type='DateTime', ticks=ticks0, kind='Unspecified') if spec['kind'] == 'dt' else dict(
                    clr_type='DateTimeOffset', local_ticks=ticks0, offset_minutes=spec['offset_minutes'])
                need(request['value'] == expected_value, 'exact_nonNULL_concurrency_temporal_value')
                if role == 'current' or raw['type_flag'] == 1: raw_tuple(raw, spec['kind'], 7)
                else: need(raw['type_flag'] == 2, 'original_nonfixed_flag2_only')
                expected_raw = dict(available=True, source='ParameterDescribe', c_type=ctype, precision=width, scale=7,
                    type_flag=1 if role == 'current' else raw['type_flag'], mask=0, derived_mask=0, result_format='Unknown')
                fixed = expected_raw['type_flag'] == 1
                need(b['binding_available'] is True and b['reliable_fixed'] is fixed
                     and b['column_scale'] == (7 if fixed else None) and integer(b['c_type'], 'concurrency_binding_type') == ctype
                     and all(integer(b[key], 'concurrency_' + key) == 7 for key in ('encoded_scale', 'wire_scale', 'requested_scale'))
                     and b['request_explicit_scale'] is True and b['profile_state'] == 'VerifiedSeven', 'exact_concurrency_temporal_publication')
                if fixed: integer(b['column_scale'], 'concurrency_fixed_column_scale')
            else:
                expected_raw = dict(available=True, source='ParameterDescribe', c_type=2 if role == 'n' else 7,
                    precision=32 if role == 'n' else 4, scale=0, type_flag=1 if role == 'n' else 2,
                    mask=0, derived_mask=0, result_format='Unknown')
                expected_value = {'clr_type': 'String', 'value': n_value} if role == 'n' else {'clr_type': 'Int32', 'value': 1}
                need(request['provider_type'] == ('VarChar' if role == 'n' else 'Int32') and request['scale'] == 0
                     and request['explicit_scale'] is False and request['value'] == expected_value, 'concurrency_N_ID_actual_values')
                if role == 'id': integer(request['value']['value'], 'concurrency_ID')
                need(b['binding_available'] is False and all(b[key] is None for key in ('reliable_fixed', 'c_type', 'encoded_scale',
                     'wire_scale', 'requested_scale', 'column_scale', 'request_explicit_scale', 'profile_state')), 'N_ID_not_temporal_binding')
            need(raw['available'] is True and raw == expected_raw and b['raw_available'] is True, 'exact_controlled_concurrency_raw_tuple')
            for key in ('source', 'c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask', 'result_format'):
                if key in ('c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask'):
                    integer(b['raw_' + key], 'concurrency_published_raw_' + key)
                need(b['raw_' + key] == raw[key], 'concurrency_prepare_published_raw_retention')
        for name in ('public_parameter_name_sha256', 'server_parameter_name_sha256'):
            need(len({p[name] for p in roles.values()}) == 4, 'unique_concurrency_parameter_names')
        if spec['async_api']:
            sent = [x for x in report['sent_frames'] if x['stage'] == stage and x['opcode'] in (6, 13)]
            need(sent and all([integer(x[key], 'concurrency_send_identity') for key in
                 ('session_id', 'lease_generation', 'execution_id', 'invocation_id')] == s['operation_identity'] for x in sent),
                 'concurrency_async_actual_Execute_identity')
        stages.append((start, s, roles))
    first, second = stages
    need(first[0]['sql_sha256'] == second[0]['sql_sha256'] and first[0]['sql_length'] == second[0]['sql_length'], 'same_shape_concurrency_SQL_join')
    need(first[0]['attempt_id'] != second[0]['attempt_id'] and first[0]['ef_command_id'] != second[0]['ef_command_id']
         and first[1]['operation_identity'] != second[1]['operation_identity'], 'distinct_winner_stale_invocations')
    for role in locations:
        for key in ('ordinal', 'public_parameter_index', 'public_parameter_name_sha256', 'server_parameter_name_sha256'):
            need(first[2][role][key] == second[2][role][key], 'stable_joint_concurrency_role_name_mapping')


def tls_concurrency_parameters(report, item, spec):
    """Jointly validate the four measured TLS6 roles; no SQL hash implies a WHERE predicate."""
    need(spec['profile'] == 'tls' and spec['group'] == 'mutation' and spec['shape'] == 'concurrency'
         and integer(spec['scale'], 'tls_concurrency_scale') == 6 and spec['kind'] in ('dt', 'dto') and type(spec['ticks']) is int
         and spec['offset_minutes'] == (330 if spec['kind'] == 'dto' else None), 'measured_TLS6_concurrency_scope')
    need(type(spec['async_api']) is bool and spec['case_id'] == 'EF-R02-tls-' + spec['kind'] + '-concurrency-'
         + ('async' if spec['async_api'] else 'sync') and spec['ticks'] == 639204129161234560, 'finite_TLS6_concurrency_identity')
    if spec['kind'] == 'dto': integer(spec['offset_minutes'], 'tls_concurrency_offset')
    need(item['case_id'] == spec['case_id'] and item['status'] == 'completed', 'controlled_concurrency_case_identity')
    for key in ('case_id', 'group', 'profile', 'shape', 'kind', 'scale', 'ticks', 'offset_minutes'):
        need(item['definition'][key] == spec[key], 'controlled_concurrency_definition')
    integer(item['definition']['scale'], 'tls_definition_scale'); integer(item['definition']['ticks'], 'tls_definition_ticks')
    if spec['kind'] == 'dto': integer(item['definition']['offset_minutes'], 'tls_definition_offset')
    need(item['definition']['async'] is spec['async_api'], 'controlled_concurrency_API')
    value(item['original_temporal_concurrency_token'], spec['kind'], spec['ticks'], spec['offset_minutes'])
    st = steps(item, ['seed_SaveChanges', 'fresh_state_before_mutation', 'tracking_query_current',
        'tracking_query_stale', 'winner_SaveChanges', 'fresh_state_after_winner_before_stale',
        'stale_SaveChanges', 'fresh_after_stale_failure', 'fresh_state_after_stale_failure'], ['stale_SaveChanges'])
    row(st['tracking_query_current']['result'], spec, spec['ticks'], 'first')
    row(st['tracking_query_stale']['result'], spec, spec['ticks'], 'first')
    exact_chain(st['stale_SaveChanges']['actual_exception_chain'], 'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException')
    need(item['expected_concurrency_exception']['actual_chain'] == st['stale_SaveChanges']['actual_exception_chain']
         and item['expected_concurrency_exception']['expected_leaf_type'] == 'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException',
         'joint_concurrency_actual_exception')
    row(st['fresh_after_stale_failure']['result'], spec, spec['ticks'] + 10, 'changed')
    owned_spec = dict(spec, owned_primary_key=item['owned_primary_key'])
    before = catalog_state(st['fresh_state_before_mutation'], owned_spec, 1)
    winner = catalog_state(st['fresh_state_after_winner_before_stale'], owned_spec, 1)
    same_state(before, winner)
    same_state(winner, catalog_state(st['fresh_state_after_stale_failure'], owned_spec, 1))
    # SaveChanges==1 is asserted by the frozen C# caller; the step records return, not a count.
    independent(item, spec, [(spec['kind'], spec['ticks'] + 10, spec['offset_minutes'])], report['_oracle'])
    locations = {'current': (0, 1), 'n': (1, 3), 'id': (2, 0), 'original': (3, 2)} if spec['kind'] == 'dt' else {
        'n': (0, 1), 'current': (1, 2), 'id': (2, 0), 'original': (3, 3)}
    db_type = 'DateTime2' if spec['kind'] == 'dt' else 'DateTimeOffset'
    provider_type = 'DateTime' if spec['kind'] == 'dt' else 'DateTimeOffset'
    ctype, width = (16, 8) if spec['kind'] == 'dt' else (23, 10)
    stages = []
    for step, delta, n_value in (('winner_SaveChanges', 10, 'changed'), ('stale_SaveChanges', 20, 'stale')):
        starts = command_window(report, spec, step, reader=True, parameters=4)
        need(len(starts) == 1, 'one_concurrency_command_attempt')
        start = starts[0]; stage = spec['case_id'] + '/' + step; s = source(report, stage)
        response = next(x for x in report['protocol_responses'] if x['sequence'] == s['response_sequence'])
        need(all(type(x) is int for x in response['operation_identity'])
             and integer(response['attempt_id'], 'tls_response_attempt') == start['attempt_id']
             and integer(response['sequence'], 'tls_response_sequence') == s['response_sequence'], 'strict_TLS6_response_identity')
        need(integer(response['response_opcode'], 'tls_prepare_response_opcode') == 0
             and integer(response['sql_code'], 'tls_prepare_sql_code') == 0, 'exact_TLS6_Prepare_success')
        ends = [x for x in report['commands'] if x['stage'] == stage and x['status'] == 'completed']
        need(len(ends) == 1 and start['api'] == ('ReaderExecutingAsync' if spec['async_api'] else 'ReaderExecuting')
             and ends[0]['api'] == ('ReaderExecutedAsync' if spec['async_api'] else 'ReaderExecuted')
             and len([x for x in report['commands'] if x['stage'] == stage]) == 2, 'actual_concurrency_EF_reader_route')
        end = ends[0]
        need(0 < integer(start['sql_length'], 'concurrency_sql_length') <= 16000, 'bounded_concurrency_SQL_receipt')
        parameters = s['parameters']
        need(len(parameters) == 4, 'whole_concurrency_four_parameters')
        temporal = [p for p in parameters if p['request']['db_type'] == db_type]
        need(len(temporal) == 2, 'two_concurrency_temporal_values')
        tick_key = 'ticks' if spec['kind'] == 'dt' else 'local_ticks'
        # Roles are determined by the controlled model/token and distinct values before looking at flags.
        roles = {}
        for role, ticks0 in (('current', spec['ticks'] + delta), ('original', spec['ticks'])):
            candidates = [p for p in temporal if p['request']['value'].get(tick_key) == ticks0]
            need(len(candidates) == 1, 'unique_current_original_concurrency_values')
            roles[role] = candidates[0]
        for role, kind0 in (('n', 'AnsiString'), ('id', 'Int32')):
            candidates = [p for p in parameters if p['request']['db_type'] == kind0]
            need(len(candidates) == 1, 'complete_concurrency_N_ID_roles'); roles[role] = candidates[0]
        need(len({p['ordinal'] for p in roles.values()}) == 4, 'distinct_concurrency_parameter_roles')
        bindings = end['binding']
        need([integer(b['ordinal'], 'concurrency_binding_ordinal') for b in bindings] == list(range(4)), 'whole_concurrency_published_binding')
        for role, p in roles.items():
            need((p['ordinal'], p['public_parameter_index']) == locations[role], 'measured_concurrency_role_mapping')
            request = p['request']; raw = p['raw']; b = bindings[p['ordinal']]
            need(integer(p['server_inout_type'], 'tls_server_inout') == 0, 'tls_input_server_parameter')
            for key in ('c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask'):
                integer(raw[key], 'concurrency_raw_' + key)
            need(request['direction'] == 'Input' and request['precision'] == 0 and request['explicit_precision'] is False,
                 'concurrency_input_request')
            if role in ('current', 'original'):
                need(request['provider_type'] == provider_type and request['scale'] == 6 and request['explicit_scale'] is True,
                     'concurrency_exact_temporal_request')
                ticks0 = spec['ticks'] + delta if role == 'current' else spec['ticks']
                value(request['value'], spec['kind'], ticks0, spec['offset_minutes'])
                expected_value = dict(clr_type='DateTime', ticks=ticks0, kind='Unspecified') if spec['kind'] == 'dt' else dict(
                    clr_type='DateTimeOffset', local_ticks=ticks0, offset_minutes=spec['offset_minutes'])
                need(request['value'] == expected_value, 'exact_nonNULL_concurrency_temporal_value')
                need(raw['type_flag'] == (1 if role == 'current' else 2), 'TLS6_current_fixed_original_nonfixed')
                expected_raw = dict(available=True, source='ParameterDescribe', c_type=ctype, precision=width, scale=6,
                    type_flag=1 if role == 'current' else 2, mask=0, derived_mask=0, result_format='Unknown')
                fixed = expected_raw['type_flag'] == 1
                need(b['binding_available'] is True and b['reliable_fixed'] is fixed
                     and b['column_scale'] == (6 if fixed else None) and integer(b['c_type'], 'concurrency_binding_type') == ctype
                     and all(integer(b[key], 'concurrency_' + key) == 6 for key in ('encoded_scale', 'wire_scale', 'requested_scale'))
                     and b['request_explicit_scale'] is True and b['profile_state'] == 'VerifiedSix', 'exact_concurrency_temporal_publication')
                if fixed: need(integer(b['column_scale'], 'concurrency_fixed_column_scale') == 6, 'tls_current_column6')
            else:
                expected_raw = dict(available=True, source='ParameterDescribe', c_type=2 if role == 'n' else 7,
                    precision=32 if role == 'n' else 4, scale=0, type_flag=1 if role == 'n' else 2,
                    mask=0, derived_mask=0, result_format='Unknown')
                expected_value = {'clr_type': 'String', 'value': n_value} if role == 'n' else {'clr_type': 'Int32', 'value': 1}
                need(request['provider_type'] == ('VarChar' if role == 'n' else 'Int32') and request['scale'] == 0
                     and request['explicit_scale'] is False and request['value'] == expected_value, 'concurrency_N_ID_actual_values')
                if role == 'id': integer(request['value']['value'], 'concurrency_ID')
                need(b['binding_available'] is False and all(b[key] is None for key in ('reliable_fixed', 'c_type', 'encoded_scale',
                     'wire_scale', 'requested_scale', 'column_scale', 'request_explicit_scale', 'profile_state')), 'N_ID_not_temporal_binding')
            need(raw['available'] is True and raw == expected_raw and b['raw_available'] is True, 'exact_controlled_concurrency_raw_tuple')
            for key in ('source', 'c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask', 'result_format'):
                if key in ('c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask'):
                    integer(b['raw_' + key], 'concurrency_published_raw_' + key)
                need(b['raw_' + key] == raw[key], 'concurrency_prepare_published_raw_retention')
        for name in ('public_parameter_name_sha256', 'server_parameter_name_sha256'):
            need(len({p[name] for p in roles.values()}) == 4, 'unique_concurrency_parameter_names')
        if spec['async_api']:
            sent = [x for x in report['sent_frames'] if x['stage'] == stage and x['opcode'] in (6, 13)]
            need(sent and all([integer(x[key], 'concurrency_send_identity') for key in
                 ('session_id', 'lease_generation', 'execution_id', 'invocation_id')] == s['operation_identity'] for x in sent),
                 'concurrency_async_actual_Execute_identity')
        stages.append((start, s, roles))
    first, second = stages
    need(first[0]['sql_sha256'] == second[0]['sql_sha256'] and first[0]['sql_length'] == second[0]['sql_length'], 'same_shape_concurrency_SQL_join')
    need(first[0]['attempt_id'] != second[0]['attempt_id'] and first[0]['ef_command_id'] != second[0]['ef_command_id']
         and first[1]['operation_identity'] != second[1]['operation_identity'], 'distinct_winner_stale_invocations')
    for role in locations:
        for key in ('ordinal', 'public_parameter_index', 'public_parameter_name_sha256', 'server_parameter_name_sha256'):
            need(first[2][role][key] == second[2][role][key], 'stable_joint_concurrency_role_name_mapping')


def source(report, stage):
    found = [x for x in report['parameter_describe_sources'] if x['stage'] == stage]
    need(len(found) == 1, 'same_invocation_source_pending')
    s = found[0]
    need(s['capture_point'] == 'after_prepare_decode_before_bind' and s['request_opcode'] == 5, 'legal_prepare_capture_point')
    identity = s['operation_identity']
    need(type(identity) is list and len(identity) == 4 and all(integer(x, 'operation_identity') > 0 for x in identity), 'complete_operation_identity')
    for name in ('statement_reference_token', 'command_reference_token', 'connection_reference_token'):
        need(integer(s[name], name) != 0, 'actual_reference_identity')
    need(type(s['server_statement_id']) is int, 'server_statement_identity')
    starts = [x for x in report['commands'] if x['stage'] == stage and x['attempt_id'] == s['attempt_id'] and x['status'] == 'attempted']
    need(len(starts) == 1 and starts[0]['ef_command_id'] == s['ef_command_id'], 'prepare_command_EF_identity')
    start = starts[0]
    receipts = [x for x in report['protocol_responses'] if x['sequence'] == s['response_sequence']]
    need(len(receipts) == 1, 'unique_prepare_response')
    response = receipts[0]
    need(response['stage'] == stage and response['attempt_id'] == s['attempt_id'] and response['request_opcode'] == 5
         and integer(response['sql_code'], 'prepare_code') >= 0 and response['operation_identity'] == identity
         and response['sequence'] < s['sequence'], 'prepare_response_same_invocation')
    previous = [x for x in report['protocol_responses'] if x['attempt_id'] == s['attempt_id'] and x['sequence'] < s['sequence']]
    need(previous and max(x['sequence'] for x in previous) == response['sequence'], 'latest_prepare_response')
    params = s['parameters']
    need(0 < len(params) <= 32 and [integer(x['ordinal'], 'server_ordinal') for x in params] == list(range(len(params))), 'actual_server_binding_order')
    indexes = [integer(x['public_parameter_index'], 'public_parameter_index') for x in params]
    need(len(set(indexes)) == len(indexes) == len(start['parameters']) and set(indexes) == set(range(len(indexes))), 'whole_actual_public_binding')
    for p in params:
        original = start['parameters'][p['public_parameter_index']]
        integer(p['request']['precision'], 'request_precision'); integer(p['request']['scale'], 'request_scale')
        need(p['request'] == {key: val for key, val in original.items() if key != 'actual'}, 'source_actual_public_request_match')
        need(p['server_inout_type'] == 0 and HASH.fullmatch(p['server_parameter_name_sha256'])
             and HASH.fullmatch(p['public_parameter_name_sha256']), 'actual_parameter_name_inout')
    return s


def advisory_source_branch(report, item, spec):
    """Only the two controlled TLS requests; generic raw is a negative descriptor, never temporal scale7."""
    need(spec['case_id'] == 'EF-R03-tls-' + spec['kind'] + '-advisory7-capability-async'
         and spec['profile'] == 'tls' and spec['group'] == 'six' and spec['shape'] == 'advisory7' and spec['async_api'] is True
         and spec['kind'] in ('dt', 'dto') and integer(spec['scale'], 'advisory_scale') == 7
         and integer(spec['ticks'], 'advisory_ticks') == 639204129160000001
         and spec['offset_minutes'] == (330 if spec['kind'] == 'dto' else None), 'exact_TLS_advisory_scope')
    if spec['kind'] == 'dto': integer(spec['offset_minutes'], 'advisory_offset')
    need(digest(report['window']) == digest(window_descriptor('tls', 'six', 3)), 'same_finite_advisory_window')
    need(item['route'] == 'EF_mapping_created_W_parameter_controlled_ADO_execute_not_SaveChanges', 'honest_controlled_ADO_route')
    stage = spec['case_id'] + '/advisory_ExecuteAsync'
    rows = [c for c in report['commands'] if c['stage'] == stage]
    need(len(rows) == 2 and [c['status'] for c in rows] == ['attempted', 'failed'], 'one_controlled_attempt_and_failed_completion')
    start, end = rows
    need(start['api'] == 'controlled_ADO_ExecuteNonQueryAsync' and start['ef_command_id'] is None
         and integer(start['attempt_id'], 'advisory_attempt') > 0 and integer(end['attempt_id'], 'advisory_failed_attempt') == start['attempt_id']
         and integer(start['sql_length'], 'advisory_SQL_length') == 19
         and start['sql_sha256'] == end['sql_sha256'] == hashlib.sha256(b'SELECT :v FROM DUAL').hexdigest(), 'exact_controlled_SQL_attempt_join')
    need('binding' not in start and 'binding' not in end and end['current_binding_observed'] is False and end['current_binding'] is None, 'advisory_no_binding_publication')
    candidates = [s for s in report['parameter_describe_sources'] if s['stage'] == stage]
    need(len(candidates) == 1, 'single_advisory_prepare_source')
    for key in ('sequence', 'response_sequence', 'attempt_id', 'request_opcode'): integer(candidates[0][key], 'advisory_source_' + key)
    for r in report['protocol_responses']:
        if r['attempt_id'] == start['attempt_id']:
            for key in ('sequence', 'attempt_id', 'request_opcode', 'sql_code'): integer(r[key], 'advisory_response_' + key)
    s = source(report, stage)
    need(s['ef_command_id'] is None and s['attempt_id'] == start['attempt_id']
         and len(s['parameters']) == len(start['parameters']) == 1 and s['sequence'] > s['response_sequence'] > 0
         and digest(end['parameter_describe_sources']) == digest([s]) and digest(item['actual_parameter_describe']) == digest([s])
         and item['parameter_describe_observed'] is True and item['metadata_evidence_source'] == 'same_invocation_after_prepare_decode_before_bind', 'advisory_whole_same_source_join')
    p = s['parameters'][0]; request = p['request']; raw = p['raw']
    need(integer(p['ordinal'], 'advisory_ordinal') == integer(p['public_parameter_index'], 'advisory_public_index') == integer(p['server_inout_type'], 'advisory_inout') == 0
         and p['public_parameter_name_sha256'] == hashlib.sha256(b'v').hexdigest()
         and p['server_parameter_name_sha256'] == hashlib.sha256(b'V').hexdigest(), 'actual_advisory_public_name_mapping')
    for role in ('command', 'connection'): report['_oracle'].route(start[role], role)
    report['_oracle'].route(start['parameters'][0]['actual'], 'parameter'); report['_oracle'].route(item['parameter']['actual'], 'parameter')
    need(digest(item['parameter']['actual']) == digest(start['parameters'][0]['actual'])
         and integer(item['parameter']['precision'], 'public_parameter_precision') == 0 and integer(item['parameter']['scale'], 'public_parameter_scale') == 7
         and item['parameter']['explicit_scale'] is True and item['parameter']['db_type'] == request['db_type'], 'actual_mapping_parameter_receipt')
    value_ = dict(clr_type='DateTime', ticks=spec['ticks'], kind='Unspecified') if spec['kind'] == 'dt' else dict(clr_type='DateTimeOffset', local_ticks=spec['ticks'], offset_minutes=330)
    expected_request = dict(db_type='DateTime2' if spec['kind'] == 'dt' else 'DateTimeOffset', provider_type='DateTime' if spec['kind'] == 'dt' else 'DateTimeOffset',
        precision=0, scale=7, explicit_precision=False, explicit_scale=True, direction='Input', value=value_)
    need(digest(request) == digest(expected_request) and digest({k: v for k, v in start['parameters'][0].items() if k != 'actual'}) == digest(expected_request), 'exact_nonNULL_advisory_request')
    for key in ('c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask'): integer(raw[key], 'advisory_raw_' + key)
    generic = dict(available=True, source='ParameterDescribe', c_type=2, precision=32767, scale=0, type_flag=2, mask=0, derived_mask=0, result_format='Unknown')
    fixed = dict(generic, c_type=26 if spec['kind'] == 'dt' else 27, precision=9 if spec['kind'] == 'dt' else 11, scale=7, type_flag=1)
    if digest(raw) == digest(generic):
        response = next(r for r in report['protocol_responses'] if r['sequence'] == s['response_sequence'])
        need(integer(response['response_opcode'], 'generic_Prepare_response_opcode') == 0, 'TLS_generic_successful_Prepare_response0')
        branch = 'exact_generic_ParameterDescribe_TLS_explicit7_refusal'
    else:
        raw_tuple(raw, spec['kind'], 7)
        need(digest(raw) == digest(fixed), 'TLS_exact_fixed7_extended_only_unknown_pending')
        branch = 'actual_fixed7_extended_ParameterDescribe_TLS_verified6'
    need(item['source_branch'] == branch, 'independently_derived_advisory_branch')
    return s, end


def validate_advisory(report, item, spec):
    need(item['case_id'] == spec['case_id'] and item['status'] == 'completed'
         and digest(item['definition']) == digest(runtime_definition(spec)), 'complete_exact_advisory_case_definition')
    st = steps(item, ['open_EF_factory_connection', 'advisory_ExecuteAsync', 'fresh_after_TLS_refusal'], ['advisory_ExecuteAsync'])
    s, end = advisory_source_branch(report, item, spec)
    chain = st['advisory_ExecuteAsync']['actual_exception_chain']; exact_chain(chain, 'System.NotSupportedException')
    need(all(digest(chain) == digest(other) for other in (end['actual_exception_chain'], item['actual_exception_chain'], item['expected_refusal']['actual_chain']))
         and item['expected_refusal']['expected_leaf_type'] == 'System.NotSupportedException'
         and item['expected_refusal']['current_binding'] is None and item['expected_refusal']['current_binding_observed'] is False, 'advisory_actual_refusal_not_fake_binding')
    send_refusal(report, spec, item, s['stage'])
    ids = report['window']['case_ids']; position = ids.index(spec['case_id']); earlier = {c + '/SaveChanges' for c in ids[:position]}
    for control in item['expected_refusal']['send_evidence']['async_positive_controls']:
        prior = source(report, control['stage'])
        need(control['stage'] in earlier and integer(prior['attempt_id'], 'prior_control_attempt') < s['attempt_id']
             and integer(prior['sequence'], 'prior_control_sequence') < s['sequence'], 'actual_prior_same_window_control')
    for sent in report['sent_frames']:
        if sent['stage'] == s['stage']:
            need([integer(sent[k], 'advisory_sent_identity') for k in ('session_id', 'lease_generation', 'execution_id', 'invocation_id')] == s['operation_identity'], 'advisory_sent_same_operation')
    report['_oracle'].profile(item['fresh_connection_health'], 'tls')


def raw_tuple(raw, kind, scale):
    need(raw['available'] is True and raw['source'] == 'ParameterDescribe' and raw['result_format'] == 'Unknown'
         and (raw['type_flag'], raw['mask'], raw['derived_mask'], raw['scale']) == (1, 0, 0, scale), 'approved_fixed_tuple_pending')
    for key in ('type_flag', 'mask', 'derived_mask', 'scale', 'c_type', 'precision'): integer(raw[key], 'raw_' + key)
    allowed = {(16, 8), (26, 9)} if kind == 'dt' else {(23, 10), (27, 11)}
    need((raw['c_type'], raw['precision']) in allowed, 'approved_family_width_pending')


def fixed_expectation(report, spec):
    s = source(report, spec['case_id'] + '/SaveChanges')
    need(len(s['parameters']) == 4, 'fixed6_whole_four_parameters')
    types_ = [p['request']['db_type'] for p in s['parameters']]
    need(sorted(types_) == sorted(('DateTime2', 'DateTimeOffset', 'Int32', 'AnsiString')), 'whole_command_type_scope')
    temporal = []
    for p in s['parameters']:
        request = p['request']; kind = 'dt' if request['db_type'] == 'DateTime2' else 'dto'
        if request['db_type'] not in ('DateTime2', 'DateTimeOffset'):
            raw = p['raw']
            need(raw['available'] is True and raw['source'] == 'ParameterDescribe' and raw['type_flag'] == 1
                 and raw['mask'] == raw['derived_mask'] == 0 and raw['result_format'] == 'Unknown', 'non_temporal_fixed_source')
            need((request['db_type'], raw['c_type'], request['value']['value']) in (('Int32', 7, 1), ('AnsiString', 2, 'first')), 'fixed_non_temporal_value')
            continue
        raw_tuple(p['raw'], kind, 6)
        need(request['precision'] == 0 and request['scale'] == 7 and request['explicit_scale'] is True
             and request['direction'] == 'Input', 'actual_explicit7_request')
        value(request['value'], kind, spec['ticks'] if kind == spec['kind'] else None, spec['offset_minutes'])
        temporal.append(p)
    # All tuples are checked before deriving the first refusal, including the other typed NULL.
    for p in temporal:
        if spec['profile'] == 'tls' or p['raw']['c_type'] in (16, 23): return 'System.NotSupportedException', s
        v = p['request']['value']
        if not v.get('is_null', False) and v.get('local_ticks', v.get('ticks')) % 10: return 'System.OverflowException', s
    return None, s


def published(report, s, encoded, wire, state):
    ends = [x for x in report['commands'] if x['stage'] == s['stage'] and x['attempt_id'] == s['attempt_id'] and x['status'] == 'completed']
    need(len(ends) == 1, 'actual_published_binding_command')
    bindings = ends[0]['binding']
    need(len({b['ordinal'] for b in bindings}) == len(bindings), 'unique_actual_binding_ordinal')
    for p in s['parameters']:
        if p['request']['db_type'] not in ('DateTime2', 'DateTimeOffset'): continue
        selected = [x for x in bindings if x['ordinal'] == p['ordinal']]
        need(len(selected) == 1, 'actual_binding_all_temporal')
        b = selected[0]; raw = p['raw']
        need(b['binding_available'] is True and b['raw_available'] is True and b['reliable_fixed'] is True
             and (b['encoded_scale'], b['wire_scale'], b['requested_scale'], b['column_scale']) == (encoded, wire, encoded, wire)
             and b['request_explicit_scale'] is True and b['profile_state'] == state and b['c_type'] == raw['c_type'], 'actual_encoded_wire_profile_binding')
        for key in ('source', 'c_type', 'precision', 'scale', 'type_flag', 'mask', 'derived_mask', 'result_format'):
            need(b['raw_' + key] == raw[key], 'published_before_after_raw_identity')


def send_refusal(report, spec, item, stage):
    evidence = item['expected_refusal']['send_evidence']
    sends = [x for x in report['sent_frames'] if x['stage'] == stage and x['opcode'] in (6, 13)]
    if not spec['async_api']:
        need(evidence == {'observation': 'sync_send_unobserved', 'business_execute_frames': None, 'async_positive_controls': None}, 'honest_sync_unobserved')
        return
    need(evidence['observation'] == 'measured_async_Execute0' and evidence['business_execute_frames'] == 0
         and type(evidence['business_execute_frames']) is int and not sends, 'actual_async_zero_execute')
    controls = evidence['async_positive_controls']
    need(controls and all(control in report['async_write_positive_controls'] for control in controls), 'same_run_positive_control')
    for control in controls:
        need(control['source'] == 'actual_successful_EF_SaveChangesAsync_AfterFrameSent', 'genuine_async_positive_control')
        s = source(report, control['stage'])
        need(control['prepare_source'] == s, 'positive_control_same_source')
        actual = [x for x in report['sent_frames'] if x['stage'] == control['stage'] and x['opcode'] in (6, 13)]
        need(actual and control['business_execute_frames'] == len(actual) and control['sent_identity'] == actual, 'observed_positive_frames')
        need(any([x[k] for k in ('session_id', 'lease_generation', 'execution_id', 'invocation_id')] == s['operation_identity'] for x in actual), 'positive_control_matching_invocation')
        completed = control['completed_command_attempt']
        need(completed in report['commands'] and completed['status'] == 'completed' and completed['attempt_id'] == s['attempt_id']
             and completed['api'] in ('ReaderExecutedAsync', 'NonQueryExecutedAsync')
             and any(b['binding_available'] is True for b in completed['binding']), 'positive_EF_actual_binding')


def catalog_state(step, spec, expected_rows):
    state = step['result']; catalog = state['catalog']
    need(integer(state['rows'], 'fresh_total_rows') == expected_rows and HASH.fullmatch(state['catalog_sha256']), 'fresh_state_rows_catalog')
    need(catalog['primary_key_contract'] == 'exactly_one_owned_primary_key' and catalog['verification_source'] == 'OwnedObjects.Preflight', 'fresh_owned_catalog_source')
    need(catalog['primary_key_name_checked_against_catalog'] == spec['owned_primary_key'], 'fresh_catalog_exact_owned_PK')
    columns = catalog['columns']; native = spec['shape'] in ('native', 'DATE_nonmidnight')
    expected = {'ID', 'A', 'T', 'I'} if native else {'ID', 'N', 'D', 'Z'}
    need(len(columns) == len(expected) and {c['name'] for c in columns} == expected, 'fresh_exact_owned_columns')
    need(hashlib.sha256(json.dumps(catalog, indent=2, ensure_ascii=False).encode()).hexdigest() == state['catalog_sha256'], 'actual_catalog_semantics_hash')
    for c in columns:
        if c['name'] in ('D', 'Z'): need(c['scale'] == spec['scale'], 'fresh_six_model_not_upgraded')
        data_type = c['data_type'].upper()
        if c['name'] == 'ID': need(data_type in ('INT', 'INTEGER') and c['nullable'] == 'N', 'fresh_owned_ID_schema')
        elif c['name'] == 'D': need(data_type in ('TIMESTAMP', 'DATETIME'), 'fresh_owned_DT_schema')
        elif c['name'] == 'Z': need('TIME ZONE' in data_type and 'LOCAL' not in data_type, 'fresh_owned_DTO_schema')
        elif c['name'] == 'N': need(data_type.startswith('VARCHAR') and c['data_length'] == 32, 'fresh_owned_string_schema')
        elif c['name'] == 'A': need(data_type == 'DATE' and c['scale'] == 0, 'fresh_native_DATE_schema')
        elif c['name'] == 'T': need(data_type == 'TIME' and c['scale'] == 6, 'fresh_native_TIME_schema')
        elif c['name'] == 'I': need(data_type.startswith('INTERVAL DAY') and c['scale'] == 6 and c['precision'] == 9, 'fresh_native_interval_schema')
    return state


def same_state(before, after):
    need(before['rows'] == after['rows'] and before['catalog_sha256'] == after['catalog_sha256']
         and before['catalog'] == after['catalog'], 'fresh_row_schema_unchanged')


def oracle(value_, spec, ticks, offset, helper_):
    helper_.profile(value_['identity'], spec['profile']); helper_.route(value_['reader_route'], 'reader')
    need(integer(value_['rows'], 'raw_rows') == 1 and value_['is_null'] is (ticks is None), 'fresh_raw_rows_NULL')
    if ticks is None:
        need(value_['raw'] is None and value_['ff9'] is None and value_['local_ticks'] is None and value_['offset_minutes'] is None
             and value_['typed'] == {'type': 'System.DBNull', 'is_null': True}, 'fresh_NULL_raw_and_getter')
        return
    raw = value_['raw']; metadata = value_['metadata']
    need(raw['source'] == 'ordinary_result_cache_before_temporal_projection' and raw['capture_stage'] == 'before_any_temporal_getter'
         and raw['is_bdta'] is False and metadata['metadata_role'] == 'result_column' and metadata['raw_source'] == 'ResultColumn'
         and metadata['raw_scale_status'] == 'captured_server_source' and metadata['mask'] == metadata['raw_mask_before'] == metadata['raw_derived_mask'] == 0,
         'genuine_fresh_raw_capture_metadata')
    ctype, width, scale = (integer(metadata[k], 'oracle_' + k) for k in ('c_type', 'width', 'scale'))
    need((ctype, width) in ({(23, 10), (27, 11)} if spec['kind'] == 'dto' else {(16, 8), (26, 9)})
         and scale == spec['scale'] and metadata['schema_scale'] == scale and metadata['is_bdta'] is False, 'fresh_kind_width_scale')
    need((metadata['raw_c_type'], metadata['raw_precision'], metadata['raw_scale']) == (ctype, width, scale)
         and (raw['c_type'], raw['width'], raw['scale']) == (ctype, width, scale), 'raw_effective_tuple')
    need(type(raw['hex']) is str and re.fullmatch(r'(?:[0-9A-F]{2}){' + str(width) + '}', raw['hex']), 'finite_raw_packet')
    payload = bytes.fromhex(raw['hex'])
    need(raw['payload_length'] == width and raw['payload_sha256'] == hashlib.sha256(payload).hexdigest(), 'raw_packet_hash_width')
    decoded = helper_.packet_decoder()(payload, ctype, scale)
    fields = helper_.ff9(value_['ff9'], spec['kind'] == 'dto')
    need(dict(decoded) == fields and helper_.ticks(fields) == ticks and value_['local_ticks'] == ticks
         and fields['offset_minutes'] == offset == value_['offset_minutes'], 'independent_packet_FF9_input_ticks_offset')
    typed = value_['typed']
    need(typed['type'] == ('System.DateTimeOffset' if spec['kind'] == 'dto' else 'System.DateTime') and typed['ticks'] == ticks, 'fresh_typed_ticks')
    if spec['kind'] == 'dto': need(typed['offset_minutes'] == offset and typed['utc_ticks'] == ticks - offset * 600000000, 'fresh_typed_offset_utc')
    else: need(typed['kind'] == 'Unspecified', 'fresh_typed_unspecified')


def independent(item, spec, expectations, helper_):
    recorded = [(re.fullmatch(r'independent_([DZ])_([0-9]+)', k), v) for k, v in item.items()
                if re.fullmatch(r'independent_[DZ]_[0-9]+', k)]
    observations = [v for _, v in recorded]
    need(len(observations) == len(expectations), 'fresh_independent_oracle_count')
    ordinals = [int(match.group(2)) for match, _ in recorded]
    need(all(a < b for a, b in zip(ordinals, ordinals[1:])), 'fresh_observation_increasing_ordinal')
    reads = [step for step in item['steps'] if step['step_id'].startswith('fresh_independent_')]
    need(len(reads) == len(expectations), 'fresh_completed_independent_step_count')
    attempts = item['oracle_read_attempts']
    need(len(attempts) == len(expectations), 'fresh_oracle_attempt_count')
    for (match, observed), step, attempt, (kind, ticks, offset) in zip(recorded, reads, attempts, expectations):
        column = 'Z' if kind == 'dto' else 'D'
        need(match.group(1) == column and step['step_id'] == 'fresh_independent_' + column
             and step['status'] == 'completed' and step['result'] == observed, 'fresh_step_observation_exact_binding')
        need(attempt['completed'] is True and attempt['table'] == item['owned_table'] and attempt['row_id'] == 1
             and attempt['column'] == column and attempt['rows'] == 1, 'actual_fresh_oracle_owned_call')
        oracle(observed, dict(spec, kind=kind), ticks, offset, helper_)
        need(attempt['identity'] == observed['identity'] and attempt['metadata'] == observed['metadata'], 'oracle_attempt_result_binding')


def native_oracle(item, spec):
    result = item['independent_native']; attempt = item['native_oracle_attempt']; t = spec['ticks']; kind = spec['kind']
    need(result['rows'] == 1 and result['is_null'] is (t is None) and result['ticks'] == t
         and result['capture'] == 'fresh_ordinary_cache_before_temporal_getter'
         and attempt['status'] == 'completed' and attempt['source'] == 'fresh_ordinary_native_raw_integer_oracle', 'native_fresh_receipt')
    if t is None: need(result['raw_hex'] is None, 'native_NULL_packet'); return
    text = result['raw_hex']; width = {'date': 3, 'time': 5, 'interval': 24}[kind]
    need(type(text) is str and re.fullmatch(r'(?:[0-9A-F]{2}){' + str(width) + '}', text), 'native_finite_packet')
    raw = bytes.fromhex(text)
    need(attempt['raw_hex'] == text and attempt['raw_captured_before_temporal_getter'] is True
         and attempt['c_type'] == {'date': 14, 'time': 15, 'interval': 21}[kind]
         and attempt['wire_scale'] == {'date': 0, 'time': 6, 'interval': 1686}[kind], 'native_actual_raw_layout')
    if kind == 'date':
        year = int.from_bytes(raw[:2], 'little') & 32767; month = (raw[1] >> 7) | ((raw[2] & 7) << 1); day = raw[2] >> 3
        import datetime
        actual = (datetime.date(year, month, day).toordinal() - 1) * 864000000000
        need(attempt['server_text'] == f'{year:04}-{month:02}-{day:02}', 'native_DATE_server_text')
    elif kind == 'time':
        h = raw[0] & 31; m = (raw[0] >> 5) | ((raw[1] & 7) << 3); s = (raw[1] >> 3) | ((raw[2] & 1) << 5)
        micro = (raw[2] >> 1) | (raw[3] << 7) | (raw[4] << 15)
        need(h < 24 and m < 60 and s < 60 and micro <= 999999 and raw[4] & 224 == 0, 'native_TIME_domains')
        actual = (h * 3600 + m * 60 + s) * 10000000 + micro * 10
        need(attempt['server_text'] == f'{h:02}:{m:02}:{s:02}.{micro * 1000:09}', 'native_TIME_FF9')
    else:
        d, h, m, s, micro, declaration = struct.unpack('<6i', raw)
        need(0 <= d <= 999999999 and 0 <= h < 24 and 0 <= m < 60 and 0 <= s < 60 and 0 <= micro <= 999999
             and declaration == 1686, 'native_interval_domains')
        actual = ((d * 24 + h) * 3600 + m * 60 + s) * 10000000 + micro * 10
        server_text = attempt['server_text']
        need(type(server_text) is str and len(server_text) <= 120
             and integer(attempt['server_text_character_length'], 'interval_text_length') == len(server_text)
             and integer(attempt['server_text_processing_limit'], 'interval_text_limit') == 120
             and attempt['server_text_utf8_sha256'] == hashlib.sha256(server_text.encode('utf-8')).hexdigest(), 'bounded_actual_interval_text_receipt')
        # Positive DAY(9)/SECOND(6) server CAST grammar from accepted TP06 ParseIntervalText(server=true).
        # Unlike the canonical getter, actual server HMS may each contain one or two digits.
        match = re.fullmatch(r"INTERVAL '\+?([0-9]{1,9}) ([0-9]{1,2}):([0-9]{1,2}):([0-9]{1,2})\.([0-9]{6})' DAY\(9\) TO SECOND\(6\)", server_text, flags=re.ASCII)
        need(match is not None, 'native_interval_full_server_grammar')
        fields = tuple(map(int, match.groups()))
        days, hours, minutes, seconds, micros = fields
        need(0 <= days <= 999999999 and 0 <= hours <= 23 and 0 <= minutes <= 59 and 0 <= seconds <= 59
             and 0 <= micros <= 999999, 'native_interval_server_component_domain')
        need(fields == (d, h, m, s, micro), 'native_interval_server_raw_integer_components')
        text_ticks = ((days * 24 + hours) * 3600 + minutes * 60 + seconds) * 10000000 + micros * 10
        need(text_ticks == actual == t, 'native_interval_server_expected_ticks')
    need(actual == t, 'independent_native_raw_ticks')


def validate_case(report, item, spec):
    helper_ = report['_oracle']; t = spec['ticks']; off = spec['offset_minutes']; q = 1 if spec['profile'] == 'shared' else 10
    need(item['case_id'] == spec['case_id'] and item['status'] == 'completed', 'case_completed_exact_identity')
    definition = item['definition']
    for key in ('case_id', 'group', 'profile', 'shape', 'kind', 'ticks', 'offset_minutes', 'scale'):
        need(definition[key] == spec[key], 'recorded_registry_' + key)
    need(definition['async'] is spec['async_api'], 'recorded_registry_API')
    spec = dict(spec, owned_primary_key=item['owned_primary_key'])
    shape = spec['shape']; group = spec['group']; case_id = spec['case_id']
    expectations = []
    if group == 'roundtrip':
        required = ['SaveChanges_insert', 'fresh_AsNoTracking']
        if shape == 'reuse': required += ['SaveChanges_value_only', 'actual_parameterized_query_changed_value', 'SaveChanges_NULL_only', 'fresh_AsNoTracking_NULL']
        st = steps(item, required)
        need(st['SaveChanges_insert']['result']['affected_entities'] == 1, 'actual_EF_insert_count')
        row(st['fresh_AsNoTracking']['result'], spec, t, 'first')
        parameter_values(report, spec, 'SaveChanges_insert', [(spec['kind'], t)])
        command_window(report, spec, 'fresh_AsNoTracking', reader=True)
        expectations.append((spec['kind'], t, off))
        if shape == 'reuse':
            for step in ('SaveChanges_value_only', 'SaveChanges_NULL_only'): need(st[step]['result']['affected_entities'] == 1, 'actual_reuse_update_count')
            parameter_values(report, spec, 'SaveChanges_value_only', [(spec['kind'], t + q)])
            parameter_values(report, spec, 'actual_parameterized_query_changed_value', [(spec['kind'], t + q)])
            parameter_values(report, spec, 'SaveChanges_NULL_only', [(spec['kind'], None)])
            row(st['actual_parameterized_query_changed_value']['result'], spec, t + q)
            row(st['fresh_AsNoTracking_NULL']['result'], spec, None)
            command_window(report, spec, 'fresh_AsNoTracking_NULL', reader=True)
            expectations += [(spec['kind'], t + q, off), (spec['kind'], None, off)]
    elif group == 'mutation':
        required = ['seed_SaveChanges', 'fresh_state_before_mutation', 'tracking_query_current']
        failure = []
        if shape == 'concurrency':
            required += ['tracking_query_stale', 'winner_SaveChanges', 'fresh_state_after_winner_before_stale', 'stale_SaveChanges', 'fresh_after_stale_failure', 'fresh_state_after_stale_failure']
            failure = ['stale_SaveChanges']
        elif shape == 'rollback': required += ['transaction_SaveChanges', 'query_inside_transaction', 'Rollback', 'fresh_after_rollback', 'fresh_state_after_rollback']
        else: required += ['tracked_SaveChanges_update', 'fresh_after_tracked_update', 'fresh_state_after_tracked_update']
        st = steps(item, required, failure)
        parameter_values(report, spec, 'seed_SaveChanges', [(spec['kind'], t)])
        row(st['tracking_query_current']['result'], spec, t, 'first')
        before = catalog_state(st['fresh_state_before_mutation'], spec, 1)
        if shape == 'concurrency':
            row(st['tracking_query_stale']['result'], spec, t, 'first')
            value(item['original_temporal_concurrency_token'], spec['kind'], t, off)
            if spec['profile'] == 'shared' and spec['scale'] == 7:
                concurrency_parameters(report, item, spec)
            elif spec['profile'] == 'tls' and spec['scale'] == 6:
                tls_concurrency_parameters(report, item, spec)
            else:
                parameter_values(report, spec, 'winner_SaveChanges', [(spec['kind'], t), (spec['kind'], t + q)])
                # The SQL callback completes with no matching row; EF then raises the concurrency exception.
                parameter_values(report, spec, 'stale_SaveChanges', [(spec['kind'], t), (spec['kind'], t + 2*q)])
            exact_chain(st['stale_SaveChanges']['actual_exception_chain'], 'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException')
            need(item['expected_concurrency_exception']['actual_chain'] == st['stale_SaveChanges']['actual_exception_chain'], 'concurrency_actual_chain_binding')
            row(st['fresh_after_stale_failure']['result'], spec, t + q, 'changed')
            winner = catalog_state(st['fresh_state_after_winner_before_stale'], spec, 1)
            same_state(before, winner); same_state(winner, catalog_state(st['fresh_state_after_stale_failure'], spec, 1))
            expectations = [(spec['kind'], t + q, off)]
        elif shape == 'rollback':
            parameter_values(report, spec, 'transaction_SaveChanges', [(spec['kind'], t + q)])
            row(st['query_inside_transaction']['result'], spec, t + q, 'rollback')
            row(st['fresh_after_rollback']['result'], spec, t, 'first')
            same_state(before, catalog_state(st['fresh_state_after_rollback'], spec, 1))
            inside_stages = {case_id + '/' + name for name in ('transaction_SaveChanges', 'query_inside_transaction', 'Rollback')}
            need(all(not c['stage'].startswith(case_id + '/migration_command_') for c in report['commands'] if c['stage'] in inside_stages), 'transaction_no_DDL')
            expectations = [(spec['kind'], t, off)]
        else:
            parameter_values(report, spec, 'tracked_SaveChanges_update', [(spec['kind'], t + q)])
            row(st['fresh_after_tracked_update']['result'], spec, t + q, 'changed')
            same_state(before, catalog_state(st['fresh_state_after_tracked_update'], spec, 1))
            expectations = [(spec['kind'], t + q, off)]
    elif group == 'six':
        if shape == 'advisory7':
            validate_advisory(dict(report, _oracle=helper_), item, spec)
            return
        leaf = spec['contract'] if spec['contract'].startswith('System.') else None
        s = None
        if shape == 'default7_fixed6': leaf, s = fixed_expectation(report, spec)
        required = ['open_EF_factory_connection', 'fresh_state_before_SaveChanges', 'SaveChanges']
        if leaf: required += ['fresh_state_after_refusal']
        else: required += ['fresh_AsNoTracking']
        if spec['kind'] in ('date', 'time', 'interval') and not leaf: required += ['fresh_native_raw_oracle']
        st = steps(item, required, ['SaveChanges'] if leaf else [])
        mapping = item['actual_mapping']
        if spec['kind'] in ('dt', 'dto'): need(mapping['model_precision'] == (7 if shape == 'default7_fixed6' else 6), 'actual_six_model_mapping_precision')
        before = catalog_state(st['fresh_state_before_SaveChanges'], spec, 0)
        if leaf:
            exact_chain(st['SaveChanges']['actual_exception_chain'], leaf)
            need(item['actual_exception_chain'] == item['expected_refusal']['actual_chain'] == st['SaveChanges']['actual_exception_chain'], 'real_refusal_chain_binding')
            need(not any(c['stage'] == case_id + '/SaveChanges' and c['status'] == 'completed' for c in report['commands']), 'refusal_no_completed_command')
            for c in report['commands']:
                if c['stage'] == case_id + '/SaveChanges' and c['status'] == 'failed':
                    need(c['current_binding_observed'] is False and c['current_binding'] is None and 'binding' not in c, 'failed_binding_not_fabricated')
            send_refusal(report, spec, item, case_id + '/SaveChanges')
            same_state(before, catalog_state(st['fresh_state_after_refusal'], spec, 0))
            need(not any(re.fullmatch(r'independent_[DZ]_[0-9]+', k) for k in item), 'refusal_no_persisted_success_oracle')
            return
        need(st['SaveChanges']['result']['affected_entities'] == 1, 'six_actual_save_count')
        row(st['fresh_AsNoTracking']['result'], spec, t)
        command_window(report, spec, 'SaveChanges')
        if spec['kind'] in ('dt', 'dto'):
            if s is not None: published(report, s, 7, 6, 'VerifiedSeven')
            else: parameter_values(report, spec, 'SaveChanges', [(spec['kind'], t)])
            expectations = [(spec['kind'], t, off)]
        else: native_oracle(item, spec); return
    else:
        required = []
        defaults = shape in ('constant_default', 'sql_default', 'NULL_default', 'clock_default')
        if defaults: required.append('SaveChanges_generated_on_add')
        if shape == 'inline': required.append('actual_inline_EF_query')
        if shape == 'update_expression': required.append('actual_EF_server_update_expression')
        required += ['fresh_generated_materialization', 'fresh_generated_row_count']
        st = steps(item, required)
        # Frozen E source asserts the Count return; the unchanged source and completed real EF count route bind it.
        command_window(report, spec, 'fresh_generated_row_count', reader=True)
        command_window(report, spec, 'fresh_generated_materialization', reader=True)
        dticks = t + 10000000 if shape == 'update_expression' else t; zticks = t
        offset = off
        if shape == 'clock_default':
            actual = item['clock_actual']; dticks = integer(actual['d_ticks'], 'clock_DT'); zticks = integer(actual['z_local_ticks'], 'clock_DTO'); offset = integer(actual['offset_minutes'], 'clock_offset')
            need(0 <= dticks <= 3155378975999999999 and 0 <= zticks <= 3155378975999999999
                 and -779 <= offset <= 840 and actual['configured_column_scale'] == spec['scale']
                 and actual['d_ticks_within_second'] == dticks % 10000000 and actual['z_ticks_within_second'] == zticks % 10000000
                 and actual['no_seventh_digit_claim'] is True, 'honest_clock_precision')
        row(st['fresh_generated_materialization']['result'], spec, dticks, pair=True, zticks=zticks, offset=offset)
        if defaults:
            need(st['SaveChanges_generated_on_add']['result']['affected_entities'] == 1, 'generated_actual_SaveChanges')
            starts = command_window(report, spec, 'SaveChanges_generated_on_add', reader=True)
            need(all(p['db_type'] not in ('DateTime2', 'DateTimeOffset') for c in starts for p in c['parameters']), 'actual_generated_readback_not_parameter_insert')
            row(item['generated_current_values'], spec, dticks, pair=True, zticks=zticks, offset=offset)
            value(item['generated_original_values']['d'], 'dt', dticks)
            value(item['generated_original_values']['z'], 'dto', zticks, offset)
        if shape == 'inline':
            command_window(report, spec, 'actual_inline_EF_query', reader=True, parameters=0)
            row(st['actual_inline_EF_query']['result'], spec, t, pair=True, zticks=t)
        if shape == 'update_expression':
            need(st['actual_EF_server_update_expression']['result']['affected_rows'] == 1, 'actual_EF_update_expression_count')
            command_window(report, spec, 'actual_EF_server_update_expression')
        expectations = [('dt', dticks, None), ('dto', zticks, offset)]
    if expectations: independent(item, spec, expectations, helper_)


def ledger_contract(ledger, items, profile):
    p = policy()
    need(ledger['schema_version'] == 1 and ledger['profile'] == profile
         and ledger['package_sha256'] == p['W']['package_sha256'] and ledger['identity_sha256'] == p['candidate_sha256'], 'exact_owned_ledger_identity')
    entries = ledger['objects']
    need([e['case_id'] for e in entries] == [i['case_id'] for i in items], 'whole_owned_ledger_case_order')
    need(len({e['table'] for e in entries}) == len(entries) == len({e['nonce'] for e in entries}), 'unique_owned_objects')
    for entry, item in zip(entries, items):
        nonce = entry['nonce']
        need(re.fullmatch(r'[A-F0-9]{32}', nonce) and entry['table'] == 'TPL_' + nonce[:24]
             and entry['owner_marker'] == 'OWN_' + nonce[:24] and entry['primary_key'] == 'TP07PK_' + nonce[:24], 'full_nonce_owned_object')
        need(item['owned_table'] == entry['table'] and item['owned_primary_key'] == entry['primary_key']
             and entry['absent_before_create'] is True and entry['index'] is None and entry['check'] is None, 'exact_owned_scope')
        advisory = item['definition']['shape'] == 'advisory7'
        need(entry['creation_state'] == ('reserved' if advisory else 'confirmed'), 'owned_creation_state')
        if not advisory:
            need(entry['create_acknowledged'] is True and entry['comment_acknowledged'] is True
                 and entry['create_evidence']['call_returned'] is True
                 and entry['create_evidence']['api'] in ('ExecuteSqlRaw', 'ExecuteSqlRawAsync')
                 and item['actual_CREATE_completion'] == entry['create_evidence'], 'real_owned_CREATE_ack')


def validate_report(report, cleaned, ledger, profile, group, startup, startup_sha, helper_):
    helper_.no_credentials(report); helper_.no_credentials(cleaned); helper_.no_credentials(ledger)
    need(report['schema_version'] == 2 and report['task'] == 'TP07-E' and report['profile'] == profile
         and report['group'] == group and report['candidate_acceptance'] is False
         and report['execution_completed'] is True and report['cleanup_verified'] is True
         and report['bootstrap_model_built'] is True and report['protocol_observer_failed'] is False, 'complete_E_report')
    need(report['scaffold6_followup'] == policy()['scaffold_followup'], 'actual_scaffold_remains_D_pending')
    actual_identity(report['candidate_identity'], startup, startup_sha, 'TP07TemporalRuntime')
    provider = report['actual_EF_mapping_source']
    need(provider['type'] == 'W.EntityFrameworkCore.Dameng.Storage.Internal.DamengTypeMappingSource'
         and provider['assembly']['dll_sha256'] == startup['ef_provider']['dll_sha256']
         and provider['assembly']['mvid'] == startup['ef_provider']['mvid'], 'actual_EF_mapping_service_identity')
    expected = required_cases(report, profile, group)
    need(report['ef_opened_connections'], 'actual_EF_connections')
    for physical in report['ef_opened_connections']: helper_.profile(physical, profile)
    sequences = [x['sequence'] for x in report['parameter_describe_sources'] + report['protocol_responses']]
    need(len(sequences) == len(set(sequences)), 'unique_protocol_event_sequence')
    attempted = [c for c in report['commands'] if c['status'] == 'attempted']
    need(len({c['attempt_id'] for c in attempted}) == len(attempted), 'unique_command_attempts')
    allowed_stages = {i['case_id'] + '/' + step['step_id'] for i in report['cases'] for step in i['steps']}
    need(all(c['stage'] in allowed_stages for c in report['commands']), 'no_borrowed_case_command')
    for c in report['commands']:
        if c['status'] == 'failed':
            need(c['current_binding_observed'] is False and c['current_binding'] is None and 'binding' not in c, 'no_failed_command_fabricated_binding')
    ledger_contract(ledger, report['cases'], profile)
    report = dict(report, _oracle=helper_)
    for item, spec in zip(report['cases'], expected): validate_case(report, item, spec)
    helper_.cleanup(report['cleanup'], ledger, profile)
    need(cleaned['task'] == 'TP07-L0' and cleaned['profile'] == profile and cleaned['status'] == 'cleanup_complete'
         and cleaned['process_exit_code'] == 0, 'independent_exact_ledger_cleanup_process')
    actual_identity(cleaned['runtime_identity'], startup, startup_sha, 'TP07LiteralProbe')
    helper_.cleanup(cleaned['cleanup'], ledger, profile); helper_.cleanup(cleaned['finally_cleanup'], ledger, profile)
    return len(expected)


def canonical(path, expected):
    path, expected = Path(path), Path(expected)
    need(path.is_absolute() and path == expected and path.resolve() == expected, 'canonical_new_run_artifact_path')
    return path


def window_metadata(context, run_id, descriptor):
    need(type(run_id) is str and RUN_ID.fullmatch(run_id), 'finite_new_run_identity')
    return dict(schema_version=2, run_id=run_id, cohort_id=cohort_id(context), window=descriptor,
                policy_sha256=sha(HERE / 'ef-cases.json'), window_contract_sha256=digest(window_contract()),
                approved_execution=context['execution_reference'], toolpack=context['pack'])


def run_context(folder, context, run_id=None):
    folder = Path(folder).absolute(); canonical(folder, folder)
    receipt = load(folder / 'run-receipt.json')
    need(integer(receipt['schema_version'], 'window_run_schema') == 2 and receipt['task'] == 'TP07-E window run'
         and receipt['status'] in ('running', 'window_failed_stop', 'E_windows_complete')
         and type(receipt['run_id']) is str and RUN_ID.fullmatch(receipt['run_id'])
         and (run_id is None or receipt['run_id'] == run_id) and receipt['cohort_id'] == cohort_id(context)
         and receipt['toolpack'] == context['pack'] and receipt['approved_execution'] == context['execution_reference']
         and receipt['policy_sha256'] == sha(HERE / 'ef-cases.json')
         and receipt['window_contract_sha256'] == digest(window_contract())
         and receipt['input_hashes_before'] == receipt['input_hashes_after'] == context['pins'], 'new_window_run_source_identity')
    flattened = [w['window'] for g in receipt['groups'] for w in g['windows']]
    need(digest(flattened) == digest(windows()[:len(flattened)]) and len(flattened) <= 25, 'new_run_started_window_prefix')
    return receipt


def common_command(context):
    return ['--approved-execution', context['execution_reference']['path'], '--approved-execution-sha256', context['execution_reference']['sha256'],
            '--toolpack-manifest', context['pack']['manifest_path'], '--toolpack-manifest-sha256', context['pack']['manifest_sha256']]


def window_validator_command(folder, descriptor, context, run_id):
    return [sys.executable, str(HERE / 'ef-validate.py')] + common_command(context) + ['--run', str(folder), '--profile', descriptor['profile'],
        '--group', descriptor['group'], '--window-index', str(descriptor['index']), '--run-id', run_id, '--output', str(Path(folder) / 'validation.json')]


def window_host_command(folder, descriptor, context, run_id, cleanup=False):
    p, g = descriptor['profile'], descriptor['group']; approval = context['approval']
    wrapper = Path(approval['original_repo']) / 'scripts' / ('with-dameng-tls-test.sh' if p == 'tls' else 'with-dameng-test.sh')
    command = [str(wrapper), sys.executable, str(HERE / 'ef-run.py'), 'internal-host'] + common_command(context) + [
        '--profile', p, '--group', g, '--window-index', str(descriptor['index']), '--run-id', run_id,
        '--output', str(Path(folder) / ('cleanup' if cleanup else 'main'))]
    if cleanup: command += ['--cleanup', '--ledger', str(Path(folder) / 'main/ownership.json')]
    return command


def generated_sql_contract(folder, report, profile, group, context):
    # Generated SQL artifacts are exact outputs of the frozen genuine EF migrations path.
    files = list((folder / 'main').glob('generated-*.sql'))
    for file in files: canonical(file, folder / 'main' / file.name)
    sql_artifacts = {sha(f): f.read_text() for f in files}
    audited = dict(report, _oracle=context['oracle'])
    independent_specs = {s['case_id']: s for s in specs(profile, group)}
    for item in report['cases']:
        if item['definition']['shape'] == 'advisory7': continue
        operations = item['migration_operations']
        need(operations.count('CreateTableOperation') == 1 and set(operations) <= {'CreateTableOperation', 'InsertDataOperation'}, 'actual_finite_EF_migration_operations')
        for index, generated in enumerate(item['generated_SQL']):
            need(generated['sha256'] in sql_artifacts, 'generated_SQL_frozen_artifact_missing')
            step_name = 'migration_command_' + str(index)
            actual_steps = [step for step in item['steps'] if step['step_id'] == step_name]
            need(len(actual_steps) == 1 and actual_steps[0]['status'] == 'completed'
                 and actual_steps[0]['call_returned'] is True, 'actual_generated_migration_call')
            starts = command_window(audited, independent_specs[item['case_id']], step_name)
            need(all(start['sql_sha256'] == generated['sha256'] and start['api'] in ('NonQueryExecuting', 'NonQueryExecutingAsync') for start in starts), 'actual_generated_SQL_command_identity')
        create = item['actual_CREATE_completion']
        need(create['sql_sha256'] in sql_artifacts and re.match(r'\s*CREATE TABLE\b', sql_artifacts[create['sql_sha256']], re.I), 'actual_generated_CREATE_text')
        if item['definition']['shape'] in ('HasData', 'inline', 'update_expression'):
            need('InsertDataOperation' in operations and any(re.search(r'\bINSERT\s+INTO\b', sql_artifacts[g['sha256']], re.I) for g in item['generated_SQL']), 'actual_HasData_literal_seed')


def validate_window(folder, profile, group, index, context, run_id):
    folder = Path(folder).absolute(); root = folder.parents[2]
    descriptor = window_descriptor(profile, group, index)
    canonical(folder, root / profile / group / ('window-' + str(index).zfill(3)))
    run = run_context(root, context, run_id)
    need(descriptor in [w['window'] for g in run['groups'] for w in g['windows']], 'window_was_started_in_new_run')
    receipt = load(folder / 'window-receipt.json'); expected_meta = window_metadata(context, run_id, descriptor)
    need(digest({k: receipt[k] for k in expected_meta}) == digest(expected_meta) and receipt['task'] == 'TP07-E window run', 'same_new_window_receipt')
    need(receipt['input_hashes_before'] == receipt['input_hashes_after'] == context['pins'], 'window_before_after_exact_input_set')
    unchanged(context['pins']); pack_check(context['pack'])
    for role, deadline in (('main', 900), ('cleanup', 300)):
        row = receipt[role]; bounded_process(row, deadline)
        need(row['command'] == window_host_command(folder, descriptor, context, run_id, role == 'cleanup')
             and row['cwd'] == context['documents']['startup']['replay_root'], 'exact_window_parent_command')
        canonical(row['log_path'], folder / (role + '.log'))
    expected_paths = dict(report=folder / 'main/runtime.json', ledger=folder / 'main/ownership.json', cleanup=folder / 'cleanup/report.json',
                          main_host=folder / 'main/E-host-receipt.json', cleanup_host=folder / 'cleanup/E-host-receipt.json')
    need(set(receipt['artifacts']) == set(expected_paths), 'whole_window_artifact_set')
    for key, path in expected_paths.items(): canonical(receipt['artifacts'][key]['path'], path)
    report = reference(receipt['artifacts']['report']); cleaned = reference(receipt['artifacts']['cleanup']); ledger = reference(receipt['artifacts']['ledger'])
    need(report['schema_version'] == 2 and report['task'] == 'TP07-E' and report['run_id'] == run_id
         and report['cohort_id'] == expected_meta['cohort_id'] and digest(report['window']) == digest(descriptor), 'actual_compiled_window_identity')
    need(receipt['artifacts']['ledger']['sha256'] == report['cleanup']['ledger_sha256'] == cleaned['cleanup']['ledger_sha256'], 'exact_window_cleanup_ledger_hash')
    for role in ('main', 'cleanup'):
        host = reference(receipt['artifacts'][role + '_host'])
        need(digest({k: host[k] for k in expected_meta}) == digest(expected_meta) and host['task'] == 'TP07-E internal window host'
             and host['cleanup'] is (role == 'cleanup') and host['input_hashes_before'] == host['input_hashes_after'] == context['pins']
             and host['child_exit_code'] == 0, 'same_window_host_execution_inputs')
        approval = context['approval']; refs = approval['artifacts']; startup = context['documents']['startup']
        name = 'TP07LiteralProbe' if role == 'cleanup' else 'TP07TemporalRuntime'
        command = [approval['dotnet']['path'], str(Path(startup['replay_root']) / ('test/' + name + '/bin/Debug/net10.0/' + name + '.dll')),
            '--profile', profile, '--candidate-manifest', refs['candidate']['path'], '--capabilities', refs['capabilities']['path'], '--output', str(folder / role)]
        if role == 'main': command += ['--group', group, '--window-index', str(index), '--run-id', run_id, '--cohort-id', expected_meta['cohort_id']]
        else: command += ['--repo', approval['original_repo'], '--build-manifest', refs['startup']['path'], '--build-manifest-sha256', refs['startup']['sha256'],
                          '--cleanup', '--ledger', str(folder / 'main/ownership.json')]
        need(host['command'] == command, 'exact_executed_window_startup_command')
    startup = context['documents']['startup']; startup_sha = context['approval']['artifacts']['startup']['sha256']
    count = validate_report(report, cleaned, ledger, profile, group, startup, startup_sha, context['oracle'])
    generated_sql_contract(folder, report, profile, group, context)
    return dict(**expected_meta, task='TP07-E independent window validation', cases_validated=count,
                validated_case_ids=[i['case_id'] for i in report['cases']], e_window_contract_valid=True,
                full_EF_C_accepted=False, D_runtime_support='pending', window_receipt_sha256=sha(folder / 'window-receipt.json'),
                owned_objects=[{k: e[k] for k in ('nonce', 'table', 'primary_key')} for e in ledger['objects']])


def validate_group(folder, profile, group, context, run_id):
    folder = Path(folder).absolute(); canonical(folder, folder.parents[1] / profile / group)
    receipt = load(folder / 'group-receipt.json')
    need(receipt['schema_version'] == 2 and receipt['task'] == 'TP07-E window group' and receipt['run_id'] == run_id
         and receipt['profile'] == profile and receipt['group'] == group and receipt['cohort_id'] == cohort_id(context)
         and receipt['toolpack'] == context['pack'] and receipt['approved_execution'] == context['execution_reference']
         and receipt['policy_sha256'] == sha(HERE / 'ef-cases.json') and receipt['window_contract_sha256'] == digest(window_contract())
         and receipt['input_hashes_before'] == receipt['input_hashes_after'] == context['pins'], 'complete_group_window_cohort')
    expected = [w for w in windows() if w['profile'] == profile and w['group'] == group]
    need([r['window'] for r in receipt['windows']] == expected, 'disjoint_exhaustive_group_window_order')
    results = []
    for row in receipt['windows']:
        w = row['window']; stage = folder / ('window-' + str(w['index']).zfill(3))
        canonical(row['receipt']['path'], stage / 'window-receipt.json'); check_reference(row['receipt'])
        canonical(row['validation']['path'], stage / 'validation.json')
        bounded_process(row['validator'], 180)
        need(row['validator']['command'] == window_validator_command(stage, w, context, run_id)
             and row['validator']['cwd'] == context['documents']['startup']['replay_root'], 'exact_window_validator_command')
        canonical(row['validator']['log_path'], stage / 'validate.log')
        result = validate_window(stage, profile, group, w['index'], context, run_id)
        need(digest(reference(row['validation'])) == digest(result), 'saved_independent_window_binding')
        results.append(result)
    need([c for result in results for c in result['validated_case_ids']] == [s['case_id'] for s in specs(profile, group)], 'full_group_original_registry_coverage')
    return results


def coverage(results, context, run_id):
    expected = windows()
    need(len(results) == 25 and digest([r['window'] for r in results]) == digest(expected), 'exact_25_new_window_coverage')
    for result, w in zip(results, expected):
        meta = window_metadata(context, run_id, w)
        need(digest({k: result[k] for k in meta}) == digest(meta)
             and result['e_window_contract_valid'] is True and integer(result['cases_validated'], 'verified_window_cases') == w['count']
             and result['validated_case_ids'] == w['case_ids'], 'verified_same_cohort_window_coverage')
    ids = [c for r in results for c in r['validated_case_ids']]
    need(ids == [s['case_id'] for p in PROFILES for g in GROUPS for s in specs(p, g)] and len(set(ids)) == len(ids) == 338, 'disjoint_exhaustive_original338')
    owned = [o for r in results for o in r['owned_objects']]
    need(len(owned) == 338 and all(len({o[k] for o in owned}) == 338 for k in ('nonce', 'table', 'primary_key')), 'all_new_run_owned_nonces_unique')
    return len(ids)


def validate_run(folder, context):
    folder = Path(folder).absolute(); receipt = run_context(folder, context)
    need(receipt['status'] == 'E_windows_complete', 'complete_new_window_run')
    bounded_process(receipt['sdk'], 30)
    need((folder / 'sdk.log').read_text().strip() == policy()['sdk_version'], 'actual_pinned_SDK')
    need([(r['profile'], r['group']) for r in receipt['groups']] == [(p, g) for p in PROFILES for g in GROUPS], 'finite_eight_group_order')
    results = []
    for row in receipt['groups']:
        stage = folder / row['profile'] / row['group']
        canonical(row['receipt']['path'], stage / 'group-receipt.json'); check_reference(row['receipt'])
        group = load(stage / 'group-receipt.json')
        need(group['windows'] == row['windows'], 'new_run_group_window_index_binding')
        results += validate_group(stage, row['profile'], row['group'], context, receipt['run_id'])
    count = coverage(results, context, receipt['run_id']); unchanged(context['pins'])
    return dict(schema_version=2, task='TP07-E independent window-run validation', issues=[], e_value_contract_valid=True,
                windows_validated=25, cases_validated=count, full_EF_C_accepted=False, D_runtime_support='pending',
                run_id=receipt['run_id'], cohort_id=receipt['cohort_id'], run_receipt_sha256=sha(folder / 'run-receipt.json'), toolpack=context['pack'])

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('approved-execution', 'approved-execution-sha256', 'toolpack-manifest', 'toolpack-manifest-sha256', 'run', 'output',
                 'profile', 'group', 'run-id', 'startup', 'startup-sha256', 'partition-report'):
        parser.add_argument('--' + name, required=name in ('toolpack-manifest', 'toolpack-manifest-sha256', 'output'))
    parser.add_argument('--window-index', type=int)
    args = parser.parse_args(); output = Path(args.output); need(not output.exists(), 'fresh_E_validation_output')
    try:
        pack = pack_entry(args.toolpack_manifest, args.toolpack_manifest_sha256)
        if args.partition_report:
            need(args.startup and args.startup_sha256 and not any((args.approved_execution, args.approved_execution_sha256, args.run,
                 args.profile, args.group, args.run_id, args.window_index is not None)), 'partition_only_preapproval_arguments')
            startup = reference(dict(path=str(Path(args.startup).resolve()), sha256=args.startup_sha256))
            for key in ('source_hashes', 'locks', 'assets', 'binary_hashes'): source_files(Path(startup['replay_root']), startup[key])
            managed = helper(pack, 'managed_identity.py')
            for row in startup['startup_assemblies']:
                dll = Path(startup['replay_root']) / ('test/' + row['name'] + '/bin/Debug/net10.0/' + row['name'] + '.dll')
                need(sha(dll) == row['dll_sha256'] and managed.mvid(dll.read_bytes()) == row['mvid'] and sha(dll.with_suffix('.deps.json')) == row['deps_sha256'], 'partition_actual_startup_bytes')
            result = validate_partition_report(load(args.partition_report), startup, args.startup_sha256, pack)
            result['partition_report_sha256'] = sha(args.partition_report); pack_check(pack)
        else:
            need(args.approved_execution and args.approved_execution_sha256 and args.run and not args.startup and not args.startup_sha256, 'runtime_external_approval_required')
            context = inputs(args.approved_execution, args.approved_execution_sha256, pack)
            if args.profile or args.group or args.window_index is not None or args.run_id:
                need(args.profile in PROFILES and args.group in GROUPS and args.window_index is not None and args.run_id, 'finite_window_validation_arguments')
                result = validate_window(args.run, args.profile, args.group, args.window_index, context, args.run_id)
            else: result = validate_run(args.run, context)
            unchanged(context['pins']); pack_check(pack)
        code = 0
    except Exception as error:
        result = dict(schema_version=2, task='TP07-E independent validation', e_value_contract_valid=False,
                      e_window_contract_valid=False, partition_contract_valid=False,
                      issues=[str(error) if isinstance(error, ContractError) else type(error).__name__], full_EF_C_accepted=False, D_runtime_support='pending'); code = 1
    write(output, result); return code


if __name__ == '__main__': raise SystemExit(main())
