#!/usr/bin/env python3
"""Independent bounded L0 evidence contract; no SDK, DB, producer encoder, or CLR parsing."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import types

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
POLICY_PATH = HERE / 'runtime-cases.json'
HASH = re.compile(r'[a-f0-9]{64}')
TOOLPACK_FILES = (
    'tools/DownstreamAcceptance/TP07/runtime-run.py',
    'tools/DownstreamAcceptance/TP07/runtime-validate.py',
    'tools/DownstreamAcceptance/TP07/runtime-cases.json',
    'tools/DownstreamAcceptance/TP07/test_runtime_contracts.py',
    'tools/DownstreamAcceptance/TP07/managed_identity.py',
    'tools/TemporalBoundaryProbe/packed_oracle.py')


class ContractError(ValueError): pass


def require(value, code):
    if not value: raise ContractError(code)


def integer(value, code):
    require(type(value) is int, code + '_integer')
    return value


def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate_json_key')
        result[key] = value
    return result


def load(path):
    def invalid(_): raise ContractError('non_finite_json')
    return json.loads(Path(path).read_text(), object_pairs_hook=_pairs, parse_constant=invalid)


def policy(): return load(POLICY_PATH)


def toolpack_hashes(identity):
    require(sha(identity['manifest_path']) == identity['manifest_sha256'], 'toolpack_manifest_drift')
    actual = {name: sha(ROOT / name) for name in TOOLPACK_FILES}
    require(actual == identity['source_hashes'], 'executed_toolpack_drift')
    return actual


def toolpack_entry(manifest_path, expected_sha):
    require(type(expected_sha) is str and HASH.fullmatch(expected_sha), 'approved_toolpack_required')
    path = Path(manifest_path).resolve()
    require(sha(path) == expected_sha, 'approved_toolpack_manifest_pin')
    document = load(path)
    require(set(document) == {'schema_version', 'task', 'source_hashes'} and document['schema_version'] == 1
        and document['task'] == 'TP07-L0 toolpack', 'finite_toolpack_manifest_schema')
    hashes = document['source_hashes']
    require(set(hashes) == set(TOOLPACK_FILES) and all(type(value) is str and HASH.fullmatch(value) for value in hashes.values()), 'finite_toolpack_closure')
    identity = dict(manifest_path=str(path), manifest_sha256=expected_sha, source_hashes=hashes)
    before = toolpack_hashes(identity)
    require(hashes['tools/TemporalBoundaryProbe/packed_oracle.py'] == policy()['independent_packed_oracle_sha256'], 'independent_decoder_policy_pin')
    return identity, before


def toolpack_exit(identity, before, phase):
    after = toolpack_hashes(identity)
    require(before == after, 'phase_toolpack_drift')
    return dict(phase=phase, before=before, after=after)


def toolpack_binding(document, identity, phase):
    require(document['toolpack'] == identity, 'same_toolpack_required')
    require(document['toolpack_check'] == dict(phase=phase, before=identity['source_hashes'], after=identity['source_hashes']), 'complete_phase_toolpack_check')


def phase_receipt(reference, identity, phase, source_sha):
    require(set(reference) == {'path', 'sha256'} and sha(reference['path']) == reference['sha256'], 'phase_receipt_artifact_drift')
    document = load(reference['path'])
    toolpack_binding(document, identity, phase)
    require(document['source_manifest_sha256'] == source_sha, 'phase_receipt_source_binding')
    return document


def packet_decoder():
    path = ROOT / 'tools/TemporalBoundaryProbe/packed_oracle.py'
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == policy()['independent_packed_oracle_sha256'], 'independent_oracle_source_drift')
    # Execute the pinned source bytes, never a filesystem bytecode cache.
    module = types.ModuleType('tp07_independent_packed_oracle')
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module.decode_packet


def no_credentials(value):
    if isinstance(value, dict):
        for key, child in value.items():
            require(not re.search(r'password|connection_string|secret|auth_packet|exception_message', key, re.I), 'credential_field')
            no_credentials(child)
    elif isinstance(value, list):
        for child in value: no_credentials(child)
    elif isinstance(value, str):
        require(not re.search(r'(?i)(?:password|pwd|user\s+id)\s*=', value), 'credential_value')


def ticks(fields):
    year, month, day = (integer(fields[key], 'fields_' + key) for key in ('year', 'month', 'day'))
    require(1 <= year <= 9999 and 1 <= month <= 12, 'calendar_domain')
    leap = year % 4 == 0 and (year % 100 != 0 or year % 400 == 0)
    months = [31, 29 if leap else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    require(1 <= day <= months[month - 1], 'calendar_day')
    y = year - 1
    days = 365 * y + y // 4 - y // 100 + y // 400 + sum(months[:month - 1]) + day - 1
    hour, minute, second, ns = (integer(fields[key], 'fields_' + key) for key in ('hour', 'minute', 'second', 'nanoseconds'))
    require(0 <= hour < 24 and 0 <= minute < 60 and 0 <= second < 60 and 0 <= ns < 1_000_000_000 and ns % 100 == 0, 'clock_tick_domain')
    return days * 864_000_000_000 + (hour * 3600 + minute * 60 + second) * 10_000_000 + ns // 100


def ff9(text, offset):
    expression = r'([0-9]{4})-([0-9]{2})-([0-9]{2}) ([0-9]{2}):([0-9]{2}):([0-9]{2})\.([0-9]{9})'
    if offset: expression += r' ([+-])([0-9]{2}):([0-9]{2})'
    require(type(text) is str, 'ff9_text')
    match = re.fullmatch(expression, text, flags=re.ASCII)
    require(match is not None, 'strict_ff9_shape')
    fields = dict(zip(('year', 'month', 'day', 'hour', 'minute', 'second', 'nanoseconds'), map(int, match.groups()[:7])))
    zone = None
    if offset:
        sign, hours, minutes = match.groups()[7:]
        require(int(minutes) < 60, 'ff9_offset_minutes')
        zone = (int(hours) * 60 + int(minutes)) * (-1 if sign == '-' else 1)
        require(-779 <= zone <= 840 and not (sign == '-' and zone == 0), 'ff9_offset_domain')
    fields['offset_minutes'] = zone
    ticks(fields)
    return fields


def profile(value, name):
    expected = {'shared': ('8.1.5.60', 21, 'utf-8'), 'tls': ('8.1.4.6', 11, 'gb18030')}[name]
    require(value['profile'] == name and (value['server_version'], integer(value['message_version'], 'message'), value['encoding']) == expected, 'physical_profile')
    require(value['negotiated_encoding'] == expected[2] and value['encoding_override_empty'] is True, 'physical_encoding_scope')
    require(value['profile_origin'] == 'physical_get_conn_instance_conn_property', 'physical_origin')
    require(value['server_user'] == value['server_schema'] == 'WDM_PROVIDER_TEST', 'test_identity')
    require(value['result_format'] == 'ordinary_non_BDTA', 'physical_result_format')


def route(value, role):
    w = policy()['W']
    types = {'connection': 'DmConnection', 'command': 'DmCommand', 'parameter': 'DmParameter', 'reader': 'DmDataReader'}
    require(value['role'] == role and value['type'] == 'W.Dm.' + types[role], 'actual_' + role + '_route')
    require(value['dll_sha256'] == w['dll_sha256'] and value['mvid'] == w['mvid'], 'route_candidate_identity')


def runtime_identity(value, startup, expected_startup_sha):
    w = policy()['W']
    for key in ('version', 'package_sha256', 'dll_sha256', 'mvid'):
        require(value[key] == w[key], 'runtime_W_' + key)
    require(value['ef_package_version'] == '10.0.12', 'runtime_ef_version')
    selected = [row for row in startup['startup_assemblies'] if row['name'] == 'TP07LiteralProbe']
    require(len(selected) == 1, 'startup_selection')
    target = selected[0]
    require(value['startup_assembly'] == target['name'] and value['startup_sha256'] == target['dll_sha256']
        and value['startup_mvid'] == target['mvid'] and value['deps_sha256'] == target['deps_sha256'], 'startup_actual_identity')
    require(value['build_manifest_sha256'] == expected_startup_sha, 'startup_manifest_runtime_binding')
    require(value['source_manifest_sha256_binding'] == startup['source_manifest_sha256']
        and value['lock_manifest_sha256'] == startup['lock_manifest_sha256'], 'runtime_source_lock_binding')
    require(value['ef_provider']['dll_sha256'] == startup['ef_provider']['dll_sha256']
        and value['ef_provider']['mvid'] == startup['ef_provider']['mvid'], 'actual_loaded_EF_identity')


def oracle(value, spec, name):
    require(value['rows'] == 1 and type(value['rows']) is int and value['is_null'] is False, 'fresh_exact_row')
    profile(value['identity'], name)
    route(value['reader_route'], 'reader')
    raw, metadata = value['raw'], value['metadata']
    require(raw['source'] == 'ordinary_result_cache_before_temporal_projection'
        and raw['capture_stage'] == 'before_any_temporal_getter' and raw['is_bdta'] is False, 'raw_before_projection')
    kind_types = (23, 27) if spec['kind'] == 'dto' else (16, 26)
    ctype, width, scale = (integer(metadata[key], 'metadata_' + key) for key in ('c_type', 'width', 'scale'))
    require(ctype in kind_types and metadata['mask'] == 0 and type(metadata['mask']) is int
        and metadata['is_bdta'] is False and metadata['metadata_role'] == 'result_column', 'metadata_scope')
    require((raw['c_type'], raw['width'], raw['scale']) == (ctype, width, scale), 'raw_metadata_tuple')
    require(metadata['schema_scale'] == scale and type(metadata['schema_scale']) is int, 'schema_scale')
    require(metadata['raw_source'] == 'ResultColumn' and metadata['raw_scale_status'] == 'captured_server_source', 'actual_server_raw_descriptor_source')
    require(tuple(integer(metadata[key], key) for key in ('raw_c_type', 'raw_precision', 'raw_scale', 'raw_mask_before', 'raw_derived_mask'))
        == (ctype, width, scale, 0, 0), 'actual_raw_effective_descriptor_tuple')
    if not spec['bare']:
        expected_type = (27 if spec['kind'] == 'dto' else 26) if spec['parameter_scale'] == 7 else (23 if spec['kind'] == 'dto' else 16)
        require((ctype, width, scale) == (expected_type, {16: 8, 23: 10, 26: 9, 27: 11}[expected_type], spec['parameter_scale']), 'fixed_result_tuple')
    text = raw['hex']
    require(type(text) is str and re.fullmatch(r'(?:[0-9A-F]{2}){8,11}', text, flags=re.ASCII), 'bounded_raw_hex')
    payload = bytes.fromhex(text)
    require(raw['payload_length'] == len(payload) == width and type(raw['payload_length']) is int, 'raw_payload_length')
    require(raw['payload_sha256'] == hashlib.sha256(payload).hexdigest(), 'raw_payload_hash')
    try:
        decoded = packet_decoder()(payload, ctype, scale)
    except ValueError as error:
        raise ContractError('independent_raw_decode') from error
    normalized = dict(decoded)
    # The accepted TP06 decoder names the unit nanoseconds; no producer decoded fields are used as the oracle.
    require(normalized == spec['expected_fields'], 'independent_raw_requested_input')
    require(ff9(value['ff9'], spec['kind'] == 'dto') == spec['expected_fields'], 'server_ff9_requested_input')
    require(ticks(normalized) == spec['expected_local_ticks'] == integer(value['local_ticks'], 'fresh_local_ticks'), 'local_tick_input')
    require(value['offset_minutes'] == spec['expected_offset_minutes'], 'original_local_offset')
    typed = value['typed']
    require(typed['type'] == ('System.DateTimeOffset' if spec['kind'] == 'dto' else 'System.DateTime'), 'typed_kind')
    require(integer(typed['ticks'], 'typed_ticks') == spec['expected_local_ticks'], 'typed_requested_ticks')
    if spec['kind'] == 'dto':
        require(typed['offset_minutes'] == spec['expected_offset_minutes'] and typed['utc_ticks'] == spec['expected_utc_ticks'], 'typed_local_offset_utc')
    else: require(typed['kind'] == 'Unspecified', 'ordinary_DateTime_kind')
    return {'c_type': ctype, 'width': width, 'scale': scale}


def expected_literal(spec):
    fields = spec['expected_fields']
    fraction = str(fields['nanoseconds']).zfill(9)[:spec['parameter_scale']]
    clock = f"{fields['year']:04d}-{fields['month']:02d}-{fields['day']:02d} {fields['hour']:02d}:{fields['minute']:02d}:{fields['second']:02d}." + fraction
    if spec['parameter_scale'] == 7:
        if spec['kind'] == 'dt': return "TO_TIMESTAMP('" + clock + "', 'YYYY-MM-DD HH24:MI:SS.FF7')"
        offset = spec['expected_offset_minutes']; sign = '+' if offset >= 0 else '-'; hours, minutes = divmod(abs(offset), 60)
        return "TO_TIMESTAMP_TZ('" + clock + f" {sign}{hours:02d}:{minutes:02d}" + "', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')"
    zone = '+05:30' if spec['kind'] == 'dto' else ''
    return "TIMESTAMP '" + clock + zone + "'"


def case(value, spec, name):
    require(value['case_id'] == spec['case_id'] and value['bare_characterization'] is spec['bare'], 'case_id_policy')
    mapping = value['mapping']
    require(mapping['mapping_precision'] == spec['expected_field_precision'], 'mapping_precision_policy')
    require(mapping['store_type'].upper() == spec['store_type'], 'mapping_store_policy')
    require(value['generated_literal_match'] is True, 'producer_literal_gate')
    expected = value['expected']
    require(expected['type'] == ('System.DateTimeOffset' if spec['kind'] == 'dto' else 'System.DateTime')
        and integer(expected['ticks'], 'producer_requested_ticks') == spec['expected_local_ticks'], 'recorded_requested_input')
    if spec['kind'] == 'dto':
        require(expected['offset_minutes'] == spec['expected_offset_minutes'] and expected['utc_ticks'] == spec['expected_utc_ticks'], 'recorded_requested_offset')
    catalog = value['catalog']
    require(type(catalog) is list and len(catalog) == 3 and len({row['name'] for row in catalog}) == 3, 'catalog_exact_owned_columns')
    column = [row for row in catalog if row['name'] == 'V']
    require(len(column) == 1, 'catalog_value_column')
    data_type = column[0]['data_type'].upper()
    pattern = r'(?:TIMESTAMP|DATETIME)(?:\([0-9]\))?' + (r' WITH TIME ZONE' if spec['kind'] == 'dto' else '')
    require(re.fullmatch(pattern, data_type) is not None, 'catalog_temporal_kind')
    if not spec['bare']:
        require(column[0]['scale'] == spec['parameter_scale'] and type(column[0]['scale']) is int, 'catalog_fixed_scale')
        require(value['catalog_contract'] == 'explicit_mapping_declaration_observed', 'explicit_catalog_contract')
    elif not spec['expected_local_overflow']:
        require(value['catalog_contract'] == 'observed_unverified_bare_declaration', 'bare_catalog_not_supported_claim')
    if spec['expected_local_overflow']:
        require(value['status'] == 'expected_local_rejection' and value['literal_error']['type'] == 'System.OverflowException', 'bare_tick_local_overflow')
        require(value['generated_literal'] is None and value['literal_business_writes'] == 0 and type(value['literal_business_writes']) is int
            and value['fresh_rows'] == 0 and type(value['fresh_rows']) is int and value['persisted_exact'] is None, 'bare_tick_zero_write_rows')
        require(value['persistence_status'] == 'not_applicable_expected_local_literal_rejection', 'bare_tick_not_applicable')
        require('literal_oracle' not in value and 'ado_control_oracle' not in value, 'bare_tick_no_unsupported_execution')
        attempts = value['oracle_read_attempts']
        require(len(attempts) == 1 and attempts[0]['row_id'] == 1 and attempts[0]['rows'] == 0
            and attempts[0]['completed'] is True and attempts[0]['table'] == value['owned_table'], 'bare_tick_fresh_zero_receipt')
        profile(attempts[0]['identity'], name)
        steps = value['steps']
        require(len(steps) == 1 and steps[0]['step_id'] == 'GenerateSqlLiteral' and steps[0]['status'] == 'completed'
            and steps[0]['result']['literal_returned'] is False and steps[0]['result']['expected_local_overflow'] is True, 'bare_tick_generation_receipt')
        return None
    require(value['status'] == 'observed_exact' and value['literal_affected_rows'] == 1 and type(value['literal_affected_rows']) is int, 'literal_persistence')
    literal = expected_literal(spec)
    require(value['generated_literal'] == literal and value['generated_literal_sha256'] == hashlib.sha256(literal.encode()).hexdigest(), 'independent_generated_literal')
    require(value['persisted_exact'] is True and value['ado_control_exact'] is True, 'producer_exact_flags')
    literal_tuple = oracle(value['literal_oracle'], spec, name)
    oracle(value['ado_control_oracle'], spec, name)
    route(value['literal_command'], 'command')
    control = value['ado_control_route']
    route(control['command'], 'command'); route(control['parameter'], 'parameter')
    require(control['scale'] == spec['parameter_scale'] and control['precision'] == 0
        and control['evidence_role'] == 'ado_control_not_EF_SaveChanges', 'ado_control_separation')
    steps = value['steps']
    require([row['step_id'] for row in steps] == ['GenerateSqlLiteral', 'PersistGeneratedLiteral']
        and all(row['status'] == 'completed' for row in steps), 'case_step_completion')
    attempts = value['oracle_read_attempts']
    require(len(attempts) == 2 and [row['row_id'] for row in attempts] == [1, 2]
        and all(row['completed'] is True and row['rows'] == 1 and row['table'] == value['owned_table'] for row in attempts), 'fresh_oracle_attempt_completion')
    return literal_tuple


def cleanup(value, ledger, name):
    require(value['cleanup_verified'] is True, 'cleanup_verified')
    entries = ledger['objects']
    results = value['objects']
    require(len(results) == len(entries) and len({row['table'] for row in results}) == len(results), 'cleanup_completeness')
    require({row['table'] for row in results} == {row['table'] for row in entries}, 'cleanup_owned_allowlist')
    for row in results:
        entry = next(item for item in entries if item['table'] == row['table'])
        require(row['nonce'] == entry['nonce'] and row['fresh_absent'] is True and row['fresh_table_count'] == 0
            and type(row['fresh_table_count']) is int, 'fresh_owned_absence')
        profile(row['identity'], name)


def required_cases(report, name):
    p = policy()
    specs = [row for row in p['cases'] if name in row['profiles']]
    required = {row['case_id'] for row in specs}
    values = report['cases']
    require(len(values) == len({row['case_id'] for row in values}) == p['required_case_count'][name], 'case_count_unique_complete')
    require({row['case_id'] for row in values} == set(report['required_case_ids']) == required
        and len(report['required_case_ids']) == len(required), 'required_case_set')
    require(report['case_count'] == len(required), 'completed_case_count')
    return specs, required, values


def source_closure(replay, collection):
    for name, expected in collection.items():
        relative = Path(name)
        require(not relative.is_absolute() and '..' not in relative.parts and not any(x in ('secrets', '.local') for x in relative.parts), 'startup_relative_paths')
        require(sha(Path(replay) / relative) == expected, 'startup_source_lock_assets_drift')


def literal_grammar_selftest(receipt, startup):
    process = receipt['process']; reference = receipt['report']
    require(integer(process['exit_code'], 'grammar_exit') == integer(process['host_exit_code'], 'grammar_host_exit') == 0 and process['timed_out'] is False
        and process['termination_code'] is None and sha(process['log_path']) == process['log_sha256'], 'actual_CSharp_grammar_process')
    path = Path(reference['path'])
    require(sha(path) == reference['sha256'], 'frozen_CSharp_grammar_report')
    dll = Path(startup['replay_root']) / 'test/TP07LiteralProbe/bin/Debug/net10.0/TP07LiteralProbe.dll'
    require(process['command'] == [str(Path.home() / '.dotnet/dotnet'), str(dll), '--selftest-literal-grammar', '--output', str(path.parent)], 'actual_CSharp_grammar_command')
    report = load(path); no_credentials(report)
    require(report['schema_version'] == 1 and report['task'] == 'TP07-L0-literal-grammar' and report['status'] == 'grammar_selftest_complete'
        and report['database_accessed'] is False and report['expected_only'] is False and report['candidate_acceptance'] is False
        and report['literal_runtime_accepted'] is False and 'failure' not in report
        and integer(report['process_exit_code'], 'grammar_report_exit') == 0, 'actual_grammar_selftest_scope')
    expected = policy()['literal_grammar_cases']; cases = report['cases']
    require(integer(report['case_count'], 'grammar_case_count') == integer(report['passed'], 'grammar_passed') == len(cases) == len(expected) == 35
        and integer(report['failed'], 'grammar_failed') == 0
        and integer(report['accepted_case_count'], 'grammar_accept_count') == 12 and integer(report['rejected_case_count'], 'grammar_reject_count') == 23
        and [row['case_id'] for row in cases] == [row['case_id'] for row in expected], 'complete_CSharp_grammar_cases')
    for case, vector in zip(cases, expected):
        require(case['literal'] == vector['literal'] and case['literal_sha256'] == hashlib.sha256(vector['literal'].encode()).hexdigest()
            and case['expected_accept'] is vector['expected_accept'] and case['actual_accept'] is vector['expected_accept']
            and case['status'] == 'completed', 'independent_CSharp_grammar_vector')
        error = None if vector['expected_accept'] else dict(type='TP07.Runtime.EvidenceException', code='LiteralSafety', number=None)
        require(case['error'] == error, 'actual_CSharp_grammar_error')
    targets = [row for row in startup['startup_assemblies'] if row['name'] == 'TP07LiteralProbe']
    require(len(targets) == 1, 'grammar_startup_selection')
    target = targets[0]; actual = report['tool_identity']
    require(all(actual[key] == target[key] for key in ('name', 'dll_sha256', 'mvid', 'deps_sha256'))
        and re.fullmatch(r'10\.[0-9]+\.[0-9]+(?:\+[^\s]+)?', actual['runtime']), 'actual_CSharp_grammar_assembly_identity')
    require(sha(dll) == actual['dll_sha256'] and sha(dll.with_suffix('.deps.json')) == actual['deps_sha256'], 'current_CSharp_grammar_binary_drift')
    return [path, Path(process['log_path'])]


def validate_profile(report, independent_cleanup, ledger, name, startup, startup_sha):
    no_credentials(report); no_credentials(independent_cleanup); no_credentials(ledger)
    p = policy()
    require(report['schema_version'] == 1 and report['task'] == 'TP07-L0' and report['profile'] == name, 'report_scope')
    require(report['expected_only'] is False and report['database_accessed'] is True and report['process_exit_code'] == 0, 'real_complete_process')
    require(report['status'] == 'literal_cases_complete' and report['literal_runtime_accepted'] is True, 'producer_complete_not_partial')
    require(report['identity_manifest_sha256'] == p['candidate_identity_sha256'] and report['capabilities_sha256'] == p['capabilities_sha256'], 'candidate_manifest_pins')
    runtime_identity(report['runtime_identity'], startup, startup_sha)
    require(report['provider']['assembly']['dll_sha256'] == startup['ef_provider']['dll_sha256'] and report['provider']['assembly']['mvid'] == startup['ef_provider']['mvid'], 'mapping_source_actual_EF')
    route(report['factory_connection'], 'connection'); profile(report['physical_profile'], name)
    specs, required, values = required_cases(report, name)
    require(ledger['schema_version'] == 1 and ledger['profile'] == name
        and ledger['package_sha256'] == p['W']['package_sha256'] and ledger['identity_sha256'] == p['candidate_identity_sha256'], 'ledger_identity')
    entries = ledger['objects']
    require(len(entries) == len(required) and {row['case_id'] for row in entries} == required
        and len({row['table'] for row in entries}) == len(entries), 'ledger_per_case')
    observations = []
    for spec in specs:
        value = next(row for row in values if row['case_id'] == spec['case_id'])
        entry = next(row for row in entries if row['case_id'] == spec['case_id'])
        nonce = entry['nonce']
        require(re.fullmatch(r'[A-F0-9]{32}', nonce) is not None and entry['table'] == 'TPL_' + nonce[:24]
            and entry['owner_marker'] == 'OWN_' + nonce[:24] and entry['primary_key'] == 'TP07PK_' + nonce[:24], 'owned_nonce_names')
        require(value['owned_table'] == entry['table'] and entry['absent_before_create'] is True
            and entry['create_acknowledged'] is True and entry['creation_state'] == 'confirmed', 'owned_create_receipts')
        require(entry['shape'] == 'Literal' and entry['literal_store_type'] == spec['store_type']
            and entry['scale'] == spec['parameter_scale'], 'owned_literal_shape')
        ddl = "CREATE TABLE " + entry['table'] + "(ID INT NOT NULL,V " + spec['store_type'] + "," + entry['owner_marker'] + " VARCHAR(32) DEFAULT '" + nonce + "',CONSTRAINT " + entry['primary_key'] + " PRIMARY KEY(ID))"
        create = entry['create_evidence']
        require(create['call_returned'] is True and create['api'] == 'ExecuteNonQuery'
            and create['sql_sha256'] == hashlib.sha256(ddl.encode()).hexdigest(), 'actual_create_returned_SQL_hash')
        require({row['name'] for row in value['catalog']} == {'ID', 'V', entry['owner_marker']}, 'exact_catalog_owned_names')
        tuple_value = case(value, spec, name)
        if spec['bare'] and tuple_value is not None:
            observations.append(dict(case_id=spec['case_id'], actual_result_tuple=tuple_value, actual_catalog=value['catalog'], status='observed_pending_root_review_never_alias_seven_support'))
    cleanup(report['finally_cleanup'], ledger, name)
    require(independent_cleanup['status'] == 'cleanup_complete' and independent_cleanup['process_exit_code'] == 0, 'independent_cleanup_process')
    require(independent_cleanup['task'] == 'TP07-L0' and independent_cleanup['profile'] == name
        and independent_cleanup['identity_manifest_sha256'] == p['candidate_identity_sha256']
        and independent_cleanup['capabilities_sha256'] == p['capabilities_sha256'], 'independent_cleanup_source_identity')
    runtime_identity(independent_cleanup['runtime_identity'], startup, startup_sha)
    cleanup(independent_cleanup['cleanup'], ledger, name)
    cleanup(independent_cleanup['finally_cleanup'], ledger, name)
    return observations



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
    require(type(actual) is type(expected), code + '_type')
    if isinstance(expected, dict):
        require(set(actual) == set(expected), code + '_keys')
        for key in expected: origin_exact(actual[key], expected[key], code)
    elif isinstance(expected, (list, tuple)):
        require(len(actual) == len(expected), code + '_length')
        for left, right in zip(actual, expected): origin_exact(left, right, code)
    else:
        require(actual == expected, code)


def origin_relative(name):
    require(type(name) is str and name and '\\' not in name and '\x00' not in name
        and not Path(name).is_absolute() and all(part not in ('', '.', '..') for part in name.split('/')), 'origin_relative_path')
    return name


def origin_map(value):
    require(type(value) is dict, 'origin_source_map')
    for name, digest in value.items():
        origin_relative(name)
        require(type(digest) is str and re.fullmatch(r'[a-f0-9]{64}', digest), 'origin_source_hash')
    return value


def origin_path(path, root, file=True):
    require(type(path) is str and Path(path).is_absolute() and '\x00' not in path, 'origin_absolute_path')
    target = Path(path); root = Path(root)
    require('..' not in target.parts and str(target) == str(target.resolve())
        and target.is_relative_to(root) and not any(part.is_symlink() for part in (target, *target.parents)), 'origin_path_boundary')
    require(target.is_file() if file else target.is_dir(), 'origin_missing_path')
    return target


def origin_refs(value):
    result = []
    if type(value) is dict:
        if 'path' in value or 'sha256' in value:
            require(set(value) == {'path', 'sha256'} and type(value['path']) is str
                and type(value['sha256']) is str and re.fullmatch(r'[a-f0-9]{64}', value['sha256']), 'origin_nested_ref_shape')
            result.append(value)
        else:
            for child in value.values(): result.extend(origin_refs(child))
    elif type(value) is list:
        for child in value: result.extend(origin_refs(child))
    return result


def transaction_product_semantics(product, approval, base, prepared_base, patch_text, root):
    """Pure loaded-document contract; physical Ref checks precede this call."""
    require(type(product) is dict and set(product) == set(TX_PRODUCT_KEYS), 'transaction_product_fields')
    for key, expected in TX_PRODUCT_FIXED.items(): origin_exact(product[key], expected, 'transaction_' + key)
    origin_exact(product['W'], policy()['W'], 'transaction_W')
    origin_exact(product['source_root'], str(Path(root) / TX_SOURCE_ROOT), 'transaction_source_root')
    expected_refs = sorted((str(Path(root) / name), digest) for name, digest in TX_NESTED_REFS)
    actual_refs = sorted((row['path'], row['sha256']) for row in origin_refs(product))
    origin_exact(actual_refs, expected_refs, 'transaction_nested_refs')
    require(type(base) is dict and set(base) == {'M1_execution_summary_sha256','execution_source_manifest_sha256',
        'original_unit_lock_sha256','schema_version','source_hashes','task','unit_lock_sha256'}, 'transaction_base_fields')
    origin_exact(base['schema_version'], 1, 'transaction_base_schema')
    old = origin_map(base['source_hashes']); current = origin_map(product['source_hashes'])
    require(len(old) == 175, 'transaction_base175')
    origin_exact(product['base_source_hashes'], old, 'transaction_base_map')
    origin_exact(prepared_base['approved_ef_source_hashes'], old, 'transaction_prepared_base_map')
    origin_exact(base['unit_lock_sha256'], old[TX_UNIT_LOCK], 'transaction_old_unit_lock')
    origin_exact(product['base_lineage'], TX_BASE_LINEAGE, 'transaction_base_lineage')
    for key, value in TX_BASE_LINEAGE.items(): origin_exact(prepared_base['lineage'][key], value, 'transaction_prepared_base_lineage')
    added = sorted(set(current) - set(old)); removed = sorted(set(old) - set(current))
    modified = sorted(name for name in old if name in current and old[name] != current[name])
    require(not removed and len(current) == 178, 'transaction_current178')
    origin_exact(added, list(TX_ADDED), 'transaction_added_files')
    origin_exact(modified, list(TX_MODIFIED), 'transaction_modified_files')
    origin_exact(product['added_files'], added, 'transaction_declared_added')
    origin_exact(product['modified_files'], modified, 'transaction_declared_modified')
    require(sum(old[name] == current[name] for name in old) == 173, 'transaction_unchanged173')
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
        require(not path.is_symlink(), 'origin_source_symlink')
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
    product = load(checked(expected['transaction_product_manifest']))
    approval = load(checked(expected['transaction_source_approval']))
    patch_text = checked(expected['transaction_patch']).read_text()
    # Fixed nested references are hashed as bytes, including TRX/archive/patch.
    origin_exact(sorted((x['path'],x['sha256']) for x in origin_refs(product)),
        sorted((str(root / path),digest) for path,digest in TX_NESTED_REFS), 'transaction_nested_refs')
    for row in origin_refs(product): checked(row)
    base = load(checked(product['base_approved_source']))
    prepared_base = load(checked(product['base_prepared_source']))
    current = transaction_product_semantics(product, approval, base, prepared_base, patch_text, root)
    old_root = origin_path(str(root / TX_BASE_ROOT), root, file=False)
    current_root = origin_path(product['source_root'], root, file=False)
    origin_exact(origin_inventory(old_root), base['source_hashes'], 'transaction_physical175')
    origin_exact(origin_inventory(current_root), current, 'transaction_physical178')
    paths += [old_root / name for name in base['source_hashes']] + [current_root / name for name in current]
    template_ref = dict(path=str(root / TX_TEMPLATE_REF[0]),sha256=TX_TEMPLATE_REF[1])
    templates = load(checked(template_ref))
    origin_exact(templates['source_hashes'], policy()['literal_template_source_hashes'], 'transaction_frozen_templates')
    for name, digest in templates['source_hashes'].items():
        path = origin_path(str(HERE / 'runtime-templates' / name), root)
        origin_exact(sha(path), digest, 'transaction_template_bytes'); paths.append(path)
    # Preserve historical unit-lock provenance against the signed mapping layer.
    mapping = load(product['base_lineage_artifacts']['mapping_manifest']['path'])
    origin_exact(base['original_unit_lock_sha256'], mapping['after_source_hashes'][TX_UNIT_LOCK], 'transaction_original_unit_lock')
    return dict(product=product,approval=approval,base=base,prepared_base=prepared_base,patch_text=patch_text,
        references=expected,protected_paths=sorted(set(paths)),template_ref=template_ref)


def transaction_source_semantics(source, value, lock=None, startup=None):
    product = value['product']; base = value['base']; root = ROOT
    require(type(source) is dict and set(source) == {'schema_version','task','source_hashes','toolpack','toolpack_check','approved_ef_source_hashes','product_ef_source_hashes','runtime_template_hashes','lineage','W','lineage_artifacts'}, 'transaction_prepared_fields')
    origin_exact(source['schema_version'], 1, 'transaction_prepared_schema')
    origin_exact(source['task'], 'TP07-L0 prepared source', 'transaction_prepared_task')
    origin_exact(source['W'], policy()['W'], 'transaction_prepared_W')
    origin_exact(source['approved_ef_source_hashes'], base['source_hashes'], 'transaction_prepared175')
    origin_exact(source['product_ef_source_hashes'], product['source_hashes'], 'transaction_prepared178')
    templates = policy()['literal_template_source_hashes']
    origin_exact(source['runtime_template_hashes'], templates, 'transaction_prepared_templates')
    expected = dict(product['source_hashes'])
    for name, digest in templates.items():
        target = 'test/' + origin_relative(name)
        require(target not in expected, 'transaction_template_overlap'); expected[target] = digest
    require(len(expected) == 185 and TX_HOST_LOCK not in expected, 'transaction_prepared185')
    origin_exact(source['source_hashes'], expected, 'transaction_prepared_closure')
    lineage = dict(TX_BASE_LINEAGE, runtime_template_source_sha256=value['template_ref']['sha256'],
        transaction_product_manifest_sha256=TX_REFS['transaction_product_manifest'][1],
        transaction_patch_sha256=TX_REFS['transaction_patch'][1], transaction_source_approval_sha256=TX_REFS['transaction_source_approval'][1])
    origin_exact(source['lineage'], lineage, 'transaction_lineage10')
    artifacts = dict(product['base_lineage_artifacts'], approved_ef_source_manifest=product['base_approved_source'],
        runtime_template_source=value['template_ref'], **value['references'])
    origin_exact(source['lineage_artifacts'], artifacts, 'transaction_artifacts10')
    if lock is not None:
        require(type(lock) is dict and {'lineage','lock_sha256','source_hashes','provider_lock_sha256'} <= set(lock), 'transaction_lock_fields')
        origin_exact(lock['lineage'], lineage, 'transaction_lock_lineage')
        require(type(lock['lock_sha256']) is str and re.fullmatch(r'[a-f0-9]{64}', lock['lock_sha256']), 'transaction_host_lock_hash')
        expected[TX_HOST_LOCK] = lock['lock_sha256']
        origin_exact(lock['source_hashes'], expected, 'transaction_locked186')
        origin_exact(lock['provider_lock_sha256'], product['source_hashes']['src/W.EntityFrameworkCore.Dameng/packages.lock.json'], 'transaction_provider_lock')
    if startup is not None:
        require(type(startup) is dict and {'lineage','lineage_artifacts','source_hashes'} <= set(startup), 'transaction_startup_fields')
        require(lock is not None, 'transaction_startup_requires_lock')
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
    require(type(before) is dict and type(after) is dict and all(before.get(path) == digest and after.get(path) == digest for path,digest in expected.items()), 'transaction_origin_protection')
    return expected


def transaction_prepare_commands(receipt, source, value, replay):
    patches = [source['lineage_artifacts'][name]['path'] for name in ('bridge_patch','mapping_patch','transaction_patch')]
    rows = receipt['overlay_commands']; require(type(rows) is list and len(rows) == 6, 'transaction_six_git_commands')
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


def validate_artifact(folder, expected_startup_sha, toolpack_manifest, expected_toolpack_sha):
    toolpack, tool_before = toolpack_entry(toolpack_manifest, expected_toolpack_sha)
    folder = Path(folder).resolve()
    receipt = load(folder / 'run-receipt.json')
    startup_path = Path(receipt['startup_manifest_path'])
    require(sha(startup_path) == expected_startup_sha == receipt['startup_manifest_sha256'], 'trusted_startup_manifest_SHA')
    startup = load(startup_path)
    toolpack_binding(startup, toolpack, 'build')
    toolpack_binding(receipt, toolpack, 'run')
    require(startup['schema_version'] == 1 and startup['lineage']['ef_commit'] == policy()['ef']['commit']
        and startup['lineage']['base_archive_sha256'] == policy()['ef']['archive_sha256'], 'startup_source_lineage')
    require(startup['W'] == policy()['W'], 'startup_candidate_tuple')
    source_path = Path(startup['source_manifest_path']); lock_path = Path(startup['lock_manifest_path'])
    require(sha(source_path) == startup['source_manifest_sha256'] and sha(lock_path) == startup['lock_manifest_sha256'], 'startup_source_lock_manifest_files')
    source_manifest = load(source_path); lock_manifest = load(lock_path)
    toolpack_binding(source_manifest, toolpack, 'prepare')
    toolpack_binding(lock_manifest, toolpack, 'lock')
    origin_value = transaction_source_contract(source_manifest,lock_manifest,startup,Path(startup['replay_root']),generated=True)
    references = receipt['phase_receipts']
    require(set(references) == {'prepare', 'lock', 'build'} and startup['phase_receipts'] == lock_manifest['phase_receipts']
        == {name: references[name] for name in ('prepare', 'lock')}, 'complete_phase_receipt_chain')
    phases = {name: phase_receipt(references[name], toolpack, name, startup['source_manifest_sha256']) for name in ('prepare', 'lock', 'build')}
    require(phases['prepare']['independent_replay_equal'] is True and phases['prepare']['SDK_executed'] is False
        and phases['prepare']['database_accessed'] is False, 'prepare_receipt_scope')
    require(phases['lock']['phase'] == 'lock' and phases['lock']['database_accessed'] is False, 'lock_receipt_scope')
    require(phases['build']['startup_manifest_sha256'] == expected_startup_sha
        and phases['build']['lock_manifest_sha256'] == startup['lock_manifest_sha256']
        and phases['build']['phase_receipts'] == startup['phase_receipts']
        and phases['build']['database_accessed'] is False, 'build_receipt_manifest_binding')
    transaction_prepare_commands(phases['prepare'],source_manifest,origin_value,source_path.parent / 'replay')
    grammar_paths = literal_grammar_selftest(phases['build']['literal_grammar_selftest'], startup)
    for phase in ('lock', 'build'):
        require(phases[phase]['commands'] and all(row['exit_code'] == 0 and row['timed_out'] is False
            and row['termination_code'] is None for row in phases[phase]['commands']), 'successful_offline_phase_commands')
    require(source_manifest['lineage'] == startup['lineage'] == lock_manifest['lineage'], 'startup_three_stage_lineage')
    require(lock_manifest['source_manifest_sha256'] == startup['source_manifest_sha256']
        and lock_manifest['source_hashes'] == startup['source_hashes'], 'startup_locked_source_closure')
    expected_sources = dict(source_manifest['source_hashes'])
    require('test/TP07LiteralProbe/packages.lock.json' not in expected_sources, 'prepared_without_new_L0_lock')
    expected_sources['test/TP07LiteralProbe/packages.lock.json'] = lock_manifest['lock_sha256']
    require(expected_sources == lock_manifest['source_hashes']
        and lock_manifest['provider_lock_sha256'] == source_manifest['source_hashes']['src/W.EntityFrameworkCore.Dameng/packages.lock.json'], 'prepare_to_lock_only_delta')
    require(source_manifest['lineage_artifacts'] == startup['lineage_artifacts'], 'prepared_startup_artifact_chain')
    for name, row in startup['lineage_artifacts'].items():
        require(sha(row['path']) == row['sha256'], 'approved_lineage_artifact_drift')
    require(startup['lineage_artifacts']['mapping_manifest']['sha256'] == startup['lineage']['mapping_manifest_sha256']
        and startup['lineage_artifacts']['mapping_patch']['sha256'] == startup['lineage']['mapping_patch_sha256']
        and startup['lineage_artifacts']['bridge_patch']['sha256'] == startup['lineage']['bridge_patch_sha256']
        and startup['lineage_artifacts']['runtime_template_source']['sha256'] == startup['lineage']['runtime_template_source_sha256'], 'lineage_fields_bound')
    replay = Path(startup['replay_root'])
    for collection in ('source_hashes', 'locks', 'assets'):
        source_closure(replay, startup[collection])
    binary_dir = replay / 'test/TP07LiteralProbe/bin/Debug/net10.0'
    selected = [row for row in startup['startup_assemblies'] if row['name'] == 'TP07LiteralProbe']
    require(len(selected) == 1, 'startup_selection')
    target = selected[0]
    binaries = {binary_dir / 'TP07LiteralProbe.dll': target['dll_sha256'],
        binary_dir / 'TP07LiteralProbe.deps.json': target['deps_sha256'],
        binary_dir / 'W.EntityFrameworkCore.Dameng.dll': startup['ef_provider']['dll_sha256'],
        binary_dir / 'W.DamengProvider.dll': policy()['W']['dll_sha256']}
    for path, expected in binaries.items(): require(sha(path) == expected, 'executed_binary_artifact_drift')
    mandatory = [startup_path, source_path, lock_path, Path(toolpack['manifest_path'])]
    mandatory += grammar_paths + origin_value['protected_paths']
    mandatory += [ROOT / name for name in TOOLPACK_FILES]
    mandatory += [Path(row['path']) for row in references.values()] + [Path(row['path']) for row in startup['lineage_artifacts'].values()]
    mandatory += [replay / name for group in ('source_hashes', 'locks', 'assets') for name in startup[group]] + list(binaries)
    mandatory_hashes = {str(path): sha(path) for path in mandatory}
    require(all(receipt['input_hashes_before'].get(path) == expected for path, expected in mandatory_hashes.items()), 'complete_execution_input_hashes')
    require(policy()['candidate_identity_sha256'] in receipt['input_hashes_before'].values()
        and policy()['capabilities_sha256'] in receipt['input_hashes_before'].values(), 'execution_candidate_capabilities_inputs')
    require(receipt['status'] == 'l0_profiles_complete' and receipt['required_profiles'] == ['shared', 'tls'], 'all_required_profiles')
    observations = []
    for name in ('shared', 'tls'):
        row = receipt['profiles'][name]
        for role in ('main', 'cleanup'):
            process = row[role]
            require(process['exit_code'] == 0 and process['timed_out'] is False and process['termination_code'] is None, 'natural_process_completion')
            require(sha(folder / process['report_path']) == process['report_sha256'], 'report_frozen_hash')
            host_path = folder / process['toolpack_host_receipt_path']
            require(sha(host_path) == process['toolpack_host_receipt_sha256'], 'internal_host_receipt_frozen_hash')
            host = load(host_path); toolpack_binding(host, toolpack, 'internal-host')
            require(host['profile'] == name and host['cleanup'] is (role == 'cleanup')
                and host['startup_manifest_sha256'] == expected_startup_sha and host['child_exit_code'] == 0, 'internal_host_phase_binding')
        ledger_path = folder / row['ledger_path']
        require(sha(ledger_path) == row['ledger_sha256'], 'ledger_frozen_hash')
        report = load(folder / row['main']['report_path'])
        cleaned = load(folder / row['cleanup']['report_path'])
        ledger = load(ledger_path)
        require(report['finally_cleanup']['ledger_sha256'] == cleaned['cleanup']['ledger_sha256'] == sha(ledger_path), 'cleanup_ledger_hash_binding')
        observations.extend(validate_profile(report, cleaned, ledger, name, startup, expected_startup_sha))
    require(receipt['input_hashes_before'] == receipt['input_hashes_after'], 'execution_input_drift')
    for path, expected in receipt['input_hashes_after'].items(): require(sha(path) == expected, 'execution_input_current_drift')
    current = transaction_source_contract(source_manifest,lock_manifest,startup,replay,generated=True)
    transaction_origin_protection(receipt['input_hashes_before'],receipt['input_hashes_after'],current)
    return dict(schema_version=1, task='TP07-L0 independent validation', issues=[], l0_value_contract_valid=True,
        bare_characterization=observations, bare_alias_seven_support=False, full_EF_C_accepted=False,
        startup_manifest_sha256=expected_startup_sha, run_receipt_sha256=sha(folder / 'run-receipt.json'),
        toolpack=toolpack, toolpack_check=toolpack_exit(toolpack, tool_before, 'validate'))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--run', required=True); parser.add_argument('--startup-manifest-sha256', required=True); parser.add_argument('--output', required=True)
    parser.add_argument('--toolpack-manifest', required=True); parser.add_argument('--toolpack-manifest-sha256', required=True)
    args = parser.parse_args()
    output = Path(args.output)
    require(not output.exists(), 'fresh_validation_output')
    try:
        result = validate_artifact(args.run, args.startup_manifest_sha256, args.toolpack_manifest, args.toolpack_manifest_sha256)
        code = 0
    except Exception as error:
        result = dict(schema_version=1, task='TP07-L0 independent validation', issues=[str(error) if isinstance(error, ContractError) else type(error).__name__],
            l0_value_contract_valid=False, full_EF_C_accepted=False)
        code = 1
    output.write_text(json.dumps(result, indent=2, sort_keys=True) + '\n')
    return code


if __name__ == '__main__': raise SystemExit(main())
