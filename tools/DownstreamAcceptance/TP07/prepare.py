#!/usr/bin/env python3
"""Exact EF object export and allowlisted W bridge. No dotnet, restore, secrets, or DB."""
import argparse
import difflib
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET
import zipfile
sys.dont_write_bytecode = True
from managed_identity import mvid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
BASE = ROOT / '.local/temporal-native/TP07'
PIN = '5863d3755dbd2d333ac359bf1c0a789762adbaed'
TREE = 'b8f99168225a3742675ae594e8735f481a591fd5'
ARCHIVE = '668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8'
CANDIDATE = {
    'id': 'W.DamengProvider', 'version': '0.1.0-temporal.20261008144915',
    'package_sha256': 'da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d',
    'dll_sha256': '843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980',
    'mvid': 'aa23e673-957c-4b1e-bf0f-291ff60077e6',
}


def sha(value):
    return hashlib.sha256(value if isinstance(value, bytes) else Path(value).read_bytes()).hexdigest()


def inventory(folder):
    return {str(path.relative_to(folder)): sha(path) for path in sorted(folder.rglob('*')) if path.is_file()}


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def extract(raw, folder):
    folder.mkdir(parents=True)
    with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
        for item in archive.getmembers():
            path = Path(item.name)
            if path.is_absolute() or '..' in path.parts or item.issym() or item.islnk() or '.local' in path.parts or path.name == '.local-test.secrets.json':
                raise ValueError('unsafe_archive_member')
            target = folder / path
            if item.isdir(): target.mkdir(parents=True, exist_ok=True)
            elif item.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.extractfile(item).read())
                target.chmod(item.mode)


def replace(text, old, new, expected, code):
    if text.count(old) != expected: raise ValueError('anchor_' + code)
    return text.replace(old, new)


def bridge(folder, allowlist):
    for entry in allowlist['entries']:
        relative = Path(entry['path'])
        if relative.is_absolute() or '..' in relative.parts:
            raise ValueError('unsafe_allowlist_path')
        path = folder / relative
        if entry['before_sha256'] is None:
            if path.exists(): raise ValueError('unexpected_added_path')
            name = path.name + '.template'
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((HERE / 'mapping-observation' / name).read_bytes())
            continue
        if sha(path) != entry['before_sha256']: raise ValueError('before_hash_mismatch')
        text = path.read_text()
        for action in entry['actions']:
            if action == 'driver_namespace':
                for old, count in entry['namespace_anchor_counts'].items():
                    text = replace(text, old, 'using W.Dm;' if old == 'using Dm;' else 'cref="W.Dm.', count, 'namespace')
            elif action == 'exact_candidate_central_dependency':
                text = replace(text, '<PackageVersion Include="DM.DmProvider" Version="[8.3.1.47463,8.4.0)" />',
                    '<PackageVersion Include="W.DamengProvider" Version="[' + CANDIDATE['version'] + ']" />', 1, 'central_driver')
                # Retain the pinned EF ranges verbatim. Low must prove lock 10.0.12 unchanged.
            elif action == 'driver_package_reference':
                text = replace(text, 'PackageReference Include="DM.DmProvider"', 'PackageReference Include="W.DamengProvider"', 1, 'driver_reference')
            elif action == 'safe_retry_detector':
                text = '''using W.Dm;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal static class DamengTransientExceptionDetector
{
    // Candidate safety metadata controls replay; an error number never proves it safe.
    public static bool ShouldRetryOn(Exception exception)
        => exception is DmException { IsTransient: true }
            && exception is not DmCommitOutcomeUnknownException;
}
'''
            elif action == 'reject_error_number_retry_overrides' and path.name == 'DamengRetryingExecutionStrategy.cs':
                text = replace(text, '=> _additionalErrorNumbers = errorNumbersToAdd?.ToFrozenSet();',
                    '=> _additionalErrorNumbers = ValidateAdditionalNumbers(errorNumbersToAdd);', 2, 'strategy_constructors')
                text = replace(text, '''        => (exception is DmException dmException
                && _additionalErrorNumbers?.Contains(dmException.Number) == true)
            || DamengTransientExceptionDetector.ShouldRetryOn(exception);''', '''        => DamengTransientExceptionDetector.ShouldRetryOn(exception);

    private static FrozenSet<int>? ValidateAdditionalNumbers(IEnumerable<int>? values)
    {
        var numbers = values?.ToFrozenSet();
        if (numbers is { Count: > 0 })
            throw new NotSupportedException("W does not support error-number retry overrides without a safe replay classifier.");
        return numbers;
    }''', 1, 'strategy_predicate')
                text = replace(text, 'Additional <see cref="DmException.Number" /> values to treat as transient.',
                    'Error-number overrides. Nonempty values are unsupported by the W bridge.', 3, 'strategy_docs')
            elif action == 'reject_error_number_retry_overrides' and path.name == 'DamengDbContextOptionsBuilder.cs':
                for old in ('var errorNumbers = errorNumbersToAdd.ToArray();', 'var errorNumbers = errorNumbersToAdd?.ToArray();'):
                    text = replace(text, old, old + '\n        RejectNonemptyOverrides(errorNumbers);', 1, 'builder_overrides')
                text = replace(text, 'Additional <see cref="W.Dm.DmException.Number" /> values to treat as transient.',
                    'Error-number overrides. Nonempty values are unsupported by the W bridge.', 2, 'builder_docs')
                if not text.endswith('}\n'): raise ValueError('builder_closing_anchor')
                text = text[:-2] + '''    private static void RejectNonemptyOverrides(int[]? numbers)
    {
        if (numbers is { Length: > 0 })
            throw new NotSupportedException("W does not support error-number retry overrides without a safe replay classifier.");
    }
}
'''
            elif action == 'cli_candidate_dependency_route':
                text = replace(text, 'DM.DmProvider', 'W.DamengProvider', 2, 'cli_package')
                text = replace(text, 'DM\\\\.DmProvider', 'W\\\\.DmProvider', 1, 'cli_version_regex')
            else:
                raise ValueError('unknown_bridge_action')
        path.write_text(text)


def patch_bytes(base, work):
    output = []
    for name in sorted(inventory(base).keys() | inventory(work).keys()):
        before, after = base / name, work / name
        old = before.read_bytes() if before.exists() else b''
        new = after.read_bytes() if after.exists() else b''
        if old == new: continue
        for line in difflib.unified_diff(old.decode().splitlines(True), new.decode().splitlines(True),
                fromfile='a/' + name if before.exists() else '/dev/null',
                tofile='b/' + name if after.exists() else '/dev/null'):
            output.append(line if line.endswith('\n') else line + '\n\\ No newline at end of file\n')
    return ''.join(output).encode()


def manifest(base, work, raw, patch, allowlist):
    before, after = inventory(base), inventory(work)
    changes = [{'path': name, 'operation': 'add' if name not in before else 'delete' if name not in after else 'change',
        'before_sha256': before.get(name), 'after_sha256': after.get(name)}
        for name in sorted(before.keys() | after.keys()) if before.get(name) != after.get(name)]
    if {row['path'] for row in changes} != {row['path'] for row in allowlist['entries']}:
        raise ValueError('changed_path_allowlist_mismatch')
    locks = {name: digest for name, digest in before.items() if name.endswith('packages.lock.json')}
    if any(after.get(name) != digest for name, digest in locks.items()): raise ValueError('original_lock_changed')
    if before['global.json'] != after['global.json']: raise ValueError('sdk_pin_changed')
    sdk = json.loads((base / 'global.json').read_text())['sdk']
    if sdk != {'version': '10.0.401', 'rollForward': 'latestPatch'}: raise ValueError('sdk_contract_mismatch')
    locked_ef = {}
    for name in locks:
        graph = json.loads((base / name).read_text())['dependencies']
        locked_ef[name] = {framework: {package: item['resolved'] for package, item in dependencies.items()
            if package.startswith('Microsoft.EntityFrameworkCore')} for framework, dependencies in graph.items()}
        if any(version != '10.0.12' for dependencies in locked_ef[name].values() for version in dependencies.values()):
            raise ValueError('original_ef_lock_version_mismatch')
    return {'schema': 1, 'task': 'TP07-P1', 'ef_commit': PIN, 'ef_tree': TREE, 'ef_archive_sha256': sha(raw),
        'candidate': CANDIDATE, 'bridge_patch_sha256': sha(patch), 'allowlist_sha256': sha(HERE / 'bridge-allowlist.json'),
        'changes': changes, 'base_source_hashes': before, 'bridge_source_hashes': after,
        'preserved_locks': locks, 'original_locked_ef_dependencies': locked_ef,
        'sdk_pin': '10.0.401', 'ef_locked_version': '10.0.12',
        'policy_inventory': {name: before[name] for name in before if name == 'AGENTS.md' or name.endswith('/SKILL.md')},
        'tool_hashes': {str(path.relative_to(HERE)): sha(path) for path in sorted(HERE.rglob('*')) if path.is_file()
            and path.name not in ('bridge-manifest.json', 'driver-bridge.patch') and '__pycache__' not in path.parts},
        'temporal_behavior_changed': False, 'bridge_runtime_accepted': False, 'integration_pending': True,
        'lock_preparation_pending': True, 'pending_contracts': [
            'Low creates candidate locks in a fresh rebuild copy and proves all original non-driver dependencies unchanged before locked restore',
            'Low compiles actual EF service baseline mapping observation and records unfixed Precision/Scale; no pass claim',
            'Official retry-number expectations retained as historical source; W counterpart and safe retry acceptance are future scope',
            'Functional/CLI DB and per-process candidate guard still require later explicit tools; no original admin/all launcher',
            'DT/DTO/TimeOnly/TimeSpan facets/materializer/literal behavior remains unfixed pending next owner'],
        'source_origin': 'read_only_git_object_archive_not_dirty_checkout'}


def verify_candidate(path):
    raw = path.read_bytes()
    if sha(raw) != CANDIDATE['package_sha256']: raise ValueError('candidate_package_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        spec = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith('.nuspec'))))
        fields = {node.tag.split('}')[-1]: node.text for node in spec.iter() if node.tag.split('}')[-1] in ('id', 'version')}
        dll = archive.read('lib/net10.0/W.DamengProvider.dll')
        if fields != {key: CANDIDATE[key] for key in ('id', 'version')} or sha(dll) != CANDIDATE['dll_sha256'] or mvid(dll) != CANDIDATE['mvid']:
            raise ValueError('candidate_dll_identity_mismatch')
    return raw


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--ef-repo', default='/Users/wanghongyi/Projects/dameng-entityframework-core')
    parser.add_argument('--candidate', default=str(ROOT / '.local/temporal-native/verification/TP06-v1-package/feed' / ('W.DamengProvider.' + CANDIDATE['version'] + '.nupkg')))
    args = parser.parse_args()
    output = Path(args.output).resolve()
    if output == BASE.resolve() or not output.is_relative_to(BASE.resolve()) or output.exists(): raise ValueError('fresh_tp07_output_required')
    package = verify_candidate(Path(args.candidate).resolve())
    tree = subprocess.check_output(['git', '-C', args.ef_repo, 'rev-parse', PIN + '^{tree}'], text=True).strip()
    raw = subprocess.check_output(['git', '-C', args.ef_repo, 'archive', '--format=tar', PIN])
    if tree != TREE or sha(raw) != ARCHIVE: raise ValueError('ef_object_identity_mismatch')
    allowlist = json.loads((HERE / 'bridge-allowlist.json').read_text())
    output.mkdir(parents=True)
    (output / 'base.tar').write_bytes(raw)
    base, work = output / 'base-source', output / 'bridge-work'
    extract(raw, base); extract(raw, work); bridge(work, allowlist)
    patch = patch_bytes(base, work)
    expected_patch = HERE / 'patches/driver-bridge.patch'
    if not expected_patch.exists() or patch != expected_patch.read_bytes(): raise ValueError('frozen_patch_replay_mismatch')
    expected = json.loads((HERE / 'bridge-manifest.json').read_text())
    result = manifest(base, work, raw, patch, allowlist)
    if result != expected: raise ValueError('frozen_manifest_replay_mismatch')
    (output / 'driver-bridge.patch').write_bytes(patch)
    write_json(output / 'bridge-manifest.json', result)
    feed = output / 'candidate-feed'; feed.mkdir()
    (feed / ('W.DamengProvider.' + CANDIDATE['version'] + '.nupkg')).write_bytes(package)
    config = ET.Element('configuration'); sources = ET.SubElement(config, 'packageSources'); ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='candidate', value=str(feed))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(config, 'packageSourceMapping')
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='candidate'), 'package', pattern='W.DamengProvider')
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget.org'), 'package', pattern='*')
    ET.ElementTree(config).write(output / 'restore.config', encoding='utf-8', xml_declaration=True)
    write_json(output / 'prepare-receipt.json', {'schema': 1, 'task': 'TP07-P1', 'replay_source_equal': True,
        'manifest_sha256': sha(HERE / 'bridge-manifest.json'), 'patch_sha256': sha(patch), 'candidate': CANDIDATE,
        'restore_config_sha256': sha(output / 'restore.config'), 'build_or_restore_run': False, 'integration_pending': True})
    print('tp07_bridge_replay_ready ' + str(output))


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print('tp07_prepare_failed ' + (str(error) if isinstance(error, ValueError) else type(error).__name__))
        raise SystemExit(1)
