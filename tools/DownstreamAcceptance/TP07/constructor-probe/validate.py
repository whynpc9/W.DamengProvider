#!/usr/bin/env python3
"""Finite FF7 EXPANSION53 evidence checks; no SDK or database access."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import types

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
PREFIX = 'tools/DownstreamAcceptance/TP07/'
PACK_FILES = tuple(PREFIX + 'constructor-probe/' + name for name in
    ('validate.py', 'test_contracts.py', 'cases.json', 'TP07ConstructorProbe.csproj', 'Program.cs', 'Cases.cs')) + (
    PREFIX + 'runtime-templates/common/CandidateGuard.cs', PREFIX + 'runtime-templates/common/OwnedObjects.cs',
    PREFIX + 'runtime-templates/common/SafeEvidence.cs', PREFIX + 'runtime-templates/TP07LiteralProbe/LiteralOracle.cs',
    PREFIX + 'managed_identity.py', 'tools/TemporalBoundaryProbe/packed_oracle.py',
    PREFIX + 'runtime-validate.py', PREFIX + 'runtime-cases.json')
HASH = re.compile(r'[a-f0-9]{64}')
PROJECT = 'test/TP07ConstructorProbe/TP07ConstructorProbe.csproj'
LOCK = 'test/TP07ConstructorProbe/packages.lock.json'
PROVIDER_LOCK = 'src/W.EntityFrameworkCore.Dameng/packages.lock.json'
DLL = 'test/TP07ConstructorProbe/bin/Debug/net10.0/TP07LiteralProbe.dll'
OLD_FAILURE_SHA = 'ed5310c8feccfb0705b85a4bda12333a3ffd60a5ae9ea353820e37486757290d'
TASK = 'TP07-constructor-ff7-expansion'
MAX_TICKS = 3155378975999999999
ORDINARY = 639204129160000000
MICRO = ORDINARY + 1234560


class ContractError(ValueError): pass
def need(condition, code):
    if not condition: raise ContractError(code)
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def integer(value, code):
    need(type(value) is int, code + '_integer'); return value
def pairs(rows):
    result = {}
    for key, value in rows:
        need(key not in result, 'duplicate_json_key'); result[key] = value
    return result
def load(path):
    def invalid(_): raise ContractError('non_finite_json')
    return json.loads(Path(path).read_text(), object_pairs_hook=pairs, parse_constant=invalid)
def fields_from_ticks(local, offset):
    need(type(local) is int and 0 <= local <= MAX_TICKS, 'CLR_local_tick_domain')
    days, day_ticks = divmod(local, 864000000000)
    def before(year):
        y = year - 1; return 365 * y + y // 4 - y // 100 + y // 400
    low, high = 1, 9999
    while low < high:
        middle = (low + high + 1) // 2
        if before(middle) <= days: low = middle
        else: high = middle - 1
    year = low; days -= before(year)
    leap = year % 4 == 0 and (year % 100 != 0 or year % 400 == 0)
    months = [31, 29 if leap else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
    month = 1
    for count in months:
        if days < count: break
        month += 1; days -= count
    seconds, fraction = divmod(day_ticks, 10000000); hour, seconds = divmod(seconds, 3600); minute, second = divmod(seconds, 60)
    return dict(year=year, month=month, day=days + 1, hour=hour, minute=minute, second=second, nanoseconds=fraction * 100, offset_minutes=offset)


def expected_value(kind, local, offset=None):
    if local is None: return dict(kind=kind, is_null=True, local_ticks=None, offset_minutes=None, utc_ticks=None, fields=None)
    need(kind in ('dt', 'dto') and (offset is None if kind == 'dt' else type(offset) is int and -779 <= offset <= 840), 'fixed_kind_offset_domain')
    utc = local - offset * 600000000 if offset is not None else None
    need(utc is None or 0 <= utc <= MAX_TICKS, 'CLR_UTC_tick_domain')
    return dict(kind=kind, is_null=False, local_ticks=local, offset_minutes=offset, utc_ticks=utc, fields=fields_from_ticks(local, offset))


def registry():
    cases = []; domains = {}
    for kind in ('dt', 'dto'):
        values = [('tail' + str(n), MICRO + n, 330) for n in range(10)]
        values += [('min', 0, 0), ('min_tick', 1, 0), ('max', MAX_TICKS, 0), ('max_tick', MAX_TICKS - 1, 0), ('max_9ticks', MAX_TICKS - 9, 0),
            ('carry_last', ORDINARY + 9999999, 330), ('carry_next', ORDINARY + 10000000, 330), ('NULL', None, None)]
        if kind == 'dto': values += [('offset_0', MICRO + 7, 0), ('offset_m330', MICRO + 7, -330), ('offset_m779', MICRO + 7, -779), ('offset_p840', MICRO + 7, 840)]
        for index, (suffix, local, offset) in enumerate(values):
            row = dict(case_id='F7-V-' + kind.upper() + '-' + suffix, context='domain', kind=kind, suffix=suffix,
                table_key='F7-TABLE-' + kind.upper(), expected=expected_value(kind, local, offset if kind == 'dto' else None),
                typed_row_id=index * 2 + 1, expression_row_id=index * 2 + 2)
            cases.append(row); domains[(kind, suffix)] = row
    for kind in ('dt', 'dto'):
        for suffix, local in [('min_tick', 1), ('max', MAX_TICKS)]:
            cases.append(dict(case_id='F7-DEF-' + kind.upper() + '-' + suffix, context='default', kind=kind, suffix=suffix,
                table_key='F7-TABLE-DEF-' + kind.upper() + '-' + suffix, expected=expected_value(kind, local, 0 if kind == 'dto' else None), typed_row_id=1, expression_row_id=2))
    for kind in ('dt', 'dto'):
        for suffix, adjacent in [('tail7', 'tail6'), ('min_tick', 'min'), ('max', 'max_tick')]:
            target = domains[(kind, suffix)]
            cases.append(dict(case_id='F7-P-' + kind.upper() + '-' + suffix, context='predicate', kind=kind, suffix=suffix,
                table_key=target['table_key'], expected=target['expected'], adjacent=domains[(kind, adjacent)]['expected'],
                source_case_id=target['case_id'], target_row_id=target['expression_row_id']))
    for kind in ('dt', 'dto'):
        control = domains[(kind, 'tail7')]
        cases.append(dict(case_id='F7-U-' + kind.upper(), context='assignment', kind=kind, table_key=control['table_key'],
            expected=control['expected'], initial=domains[(kind, 'tail0')]['expected'], row_id=37 if kind == 'dt' else 45,
            control_case_id=control['case_id'], control_row_id=control['typed_row_id']))
    cases.append(dict(case_id='F7-A-DT-second1', context='dateadd', kind='dt', table_key='F7-TABLE-DT',
        initial=expected_value('dt', MICRO + 7), expected=expected_value('dt', MICRO + 7 + 10000000), row_id=38, expected_row_id=39))
    for ordinal, case in enumerate(cases, 1): case['ordinal'] = ordinal
    return cases


def policy():
    value = load(HERE / 'cases.json')
    need(value['schema_version'] == 2 and value['task'] == TASK and value['profile'] == 'shared' and value['case_count'] == 53
        and value['candidate_acceptance'] is False and value['full_EF_C_accepted'] is False and value['cases'] == registry(), 'independent_fixed_expansion53_registry')
    return value
def text_sha(value): return hashlib.sha256(value.encode('utf-8')).hexdigest()


def exact_oracle_mismatch(value, code):
    need(type(value) is dict and value == dict(type='TP07.Runtime.EvidenceException', code='OracleMismatch', number=None), code)


def pack_hashes(identity):
    need(sha(identity['manifest_path']) == identity['manifest_sha256'], 'source_pack_manifest_drift')
    actual = {name: sha(ROOT / name) for name in PACK_FILES}
    need(actual == identity['source_hashes'], 'actual_fourteen_file_pack_drift')
    return actual


def pack_entry(path, expected):
    need(type(expected) is str and HASH.fullmatch(expected), 'external_source_pack_SHA_required')
    path = Path(path).resolve(); need(sha(path) == expected, 'approved_source_pack_manifest_pin')
    need(path not in {ROOT / name for name in PACK_FILES}, 'external_manifest_outside_fourteen_files')
    manifest = load(path)
    need(set(manifest) == {'schema_version', 'task', 'source_hashes'} and manifest['schema_version'] == 1
        and manifest['task'] == TASK + ' source pack', 'finite_source_pack_schema')
    hashes = manifest['source_hashes']
    need(set(hashes) == set(PACK_FILES) and all(type(value) is str and HASH.fullmatch(value) for value in hashes.values()), 'finite_fourteen_file_pack')
    identity = dict(manifest_path=str(path), manifest_sha256=expected, source_hashes=hashes)
    before = pack_hashes(identity); p = policy()
    need(hashes[PREFIX + 'runtime-validate.py'] == p['frozen_value_validator_sha256']
        and hashes[PREFIX + 'runtime-cases.json'] == p['frozen_value_policy_sha256']
        and hashes['tools/TemporalBoundaryProbe/packed_oracle.py'] == p['independent_packed_oracle_sha256'], 'immutable_value_helpers')
    need(all(hashes[name] == digest for name, digest in p['immutable_source_hashes'].items()), 'immutable_common_oracle_identity_helper')
    return identity, before


def pack_binding(document, identity, phase):
    need(document['source_pack'] == identity, 'same_constructor_source_pack')
    need(document['source_pack_check'] == dict(phase=phase, before=identity['source_hashes'], after=identity['source_hashes']), 'complete_source_pack_phase_check')


def value_helpers():
    # Only pure value/profile/identity helpers are reused. The old six-file-pack
    # entry points and old L0 acceptance API are never called for this screen.
    path = ROOT / PREFIX / 'runtime-validate.py'; raw = path.read_bytes()
    need(hashlib.sha256(raw).hexdigest() == policy()['frozen_value_validator_sha256'], 'frozen_value_helper_bytes')
    module = types.ModuleType('constructor_frozen_value_helpers'); module.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module


def expression(expected):
    if expected['is_null']: return 'NULL'
    f = expected['fields']
    text = f"{f['year']:04d}-{f['month']:02d}-{f['day']:02d} {f['hour']:02d}:{f['minute']:02d}:{f['second']:02d}.{f['nanoseconds'] // 100:07d}"
    if expected['kind'] == 'dt': return "TO_TIMESTAMP('" + text + "','YYYY-MM-DD HH24:MI:SS.FF7')"
    offset = expected['offset_minutes']; sign = '+' if offset >= 0 else '-'; hours, minutes = divmod(abs(offset), 60)
    return "TO_TIMESTAMP_TZ('" + text + f" {sign}{hours:02d}:{minutes:02d}" + "','YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')"


def safe_value(expected):
    if expected['is_null']: return dict(type='System.DBNull', is_null=True)
    value = dict(type='System.DateTimeOffset' if expected['kind'] == 'dto' else 'System.DateTime', ticks=expected['local_ticks'])
    if expected['kind'] == 'dto': value.update(offset_minutes=expected['offset_minutes'], utc_ticks=expected['utc_ticks'])
    else: value['kind'] = 'Unspecified'
    return value


def observation(value, expected, attempt, helpers=None):
    v = helpers or value_helpers()
    if not expected['is_null']:
        raw = value['raw']; metadata = value['metadata']
        need(attempt['metadata'] == metadata and attempt['cell'] == dict(payload_length=raw['payload_length'], payload_sha256=raw['payload_sha256'],
            is_null=False, result_format='ordinary_non_BDTA'), 'fresh_raw_cell_descriptor_and_payload_binding')
        need(type(raw['hex']) is str and re.fullmatch(r'(?:[0-9A-F]{2}){9,11}', raw['hex']), 'bounded_expansion_raw_hex')
        decoded = v.packet_decoder()(bytes.fromhex(raw['hex']), integer(metadata['c_type'], 'c_type'), integer(metadata['scale'], 'scale'))
        local = v.ticks(decoded); offset = decoded['offset_minutes']
        observed = dict(kind=expected['kind'], bare=False, parameter_scale=7, expected_fields=decoded,
            expected_local_ticks=local, expected_offset_minutes=offset, expected_utc_ticks=local - offset * 600000000 if offset is not None else None)
        v.oracle(value, observed, 'shared')
        need(attempt['ff9'] == value['ff9'] and attempt['typed'] == value['typed'], 'fresh_FF9_and_typed_receipt_binding')
        exact = decoded == expected['fields'] and local == expected['local_ticks']
        return dict(outcome='exact' if exact else 'loss', observed_fields=decoded, observed_local_ticks=local,
            observed_offset_minutes=offset, requested_fields=expected['fields'])
    need(integer(value['rows'], 'NULL_rows') == 1 and value['is_null'] is True and value['raw'] is None
        and value['ff9'] is None and value['local_ticks'] is None and value['offset_minutes'] is None
        and value['typed'] == {'type': 'System.DBNull', 'is_null': True}, 'exact_NULL_oracle')
    v.profile(value['identity'], 'shared'); v.route(value['reader_route'], 'reader')
    m = value['metadata']; ctype, width = (27, 11) if expected['kind'] == 'dto' else (26, 9)
    need(attempt['metadata'] == m, 'fresh_NULL_metadata_binding')
    need((integer(m['c_type'], 'NULL_type'), integer(m['width'], 'NULL_width'), integer(m['scale'], 'NULL_scale')) == (ctype, width, 7)
        and m['mask'] == 0 and type(m['mask']) is int and m['is_bdta'] is False and m['metadata_role'] == 'result_column'
        and m['raw_source'] == 'ResultColumn' and m['raw_scale_status'] == 'captured_server_source'
        and tuple(integer(m[key], 'NULL_descriptor') for key in ('raw_c_type', 'raw_precision', 'raw_scale', 'raw_mask_before', 'raw_derived_mask', 'schema_scale'))
            == (ctype, width, 7, 0, 0, 7), 'real_NULL_ResultColumn_descriptor')
    cell = attempt['cell']
    need(cell == dict(payload_length=None, payload_sha256=None, is_null=True, result_format='ordinary_non_BDTA'), 'NULL_raw_cache_cell_is_empty')
    return dict(outcome='exact', observed_fields=None, observed_local_ticks=None, observed_offset_minutes=None, requested_fields=None)


def catalog(rows, entry, kind):
    need(type(rows) is list and len(rows) == 3 and [row['name'] for row in rows] == ['ID', 'V', entry['owner_marker']], 'exact_owned_catalog_ordinal')
    temporal = rows[1]
    expected = r'(?:TIMESTAMP|DATETIME)(?:\(7\))?' + (r' WITH TIME ZONE' if kind == 'dto' else '')
    need(re.fullmatch(expected, temporal['data_type'].upper()) and integer(temporal['scale'], 'catalog_scale') == 7
        and integer(temporal['data_length'], 'catalog_width') == (11 if kind == 'dto' else 9), 'target_seven_catalog')
    need(rows[0]['data_type'].upper() in ('INT', 'INTEGER') and rows[0]['nullable'] == 'N'
        and rows[2]['data_type'].upper() in ('VARCHAR', 'VARCHAR2') and rows[2]['data_length'] == 32, 'owned_catalog_structure')


def plans(case):
    context = case['context']; expected = case['expected']; initial = case.get('initial')
    if context in ('domain', 'default'):
        return [('typed_insert', 'insert', case['typed_row_id'], expected, expected),
            ('ff7_insert' if context == 'domain' else 'default_insert', 'insert', case['expression_row_id'], expected, expected)]
    if context == 'predicate':
        return [('predicate_positive', 'select', case['target_row_id'], expected, expected),
            ('predicate_negative', 'select', case['target_row_id'], case['adjacent'], expected)]
    if context == 'assignment':
        return [('typed_insert', 'insert', case['row_id'], initial, initial), ('assignment_update', 'update', case['row_id'], expected, expected)]
    return [('typed_insert', 'insert', case['row_id'], initial, initial), ('typed_expected', 'insert', case['expected_row_id'], expected, expected),
        ('dateadd_update', 'update', case['row_id'], initial, expected)]


def sql_for(role, row, operand, table):
    if role in ('typed_insert', 'typed_expected'): return f'INSERT INTO {table}(ID,V) VALUES({row},:v)', None
    if role == 'ff7_insert':
        exp = expression(operand); return f'INSERT INTO {table}(ID,V) VALUES({row},{exp})', exp
    if role == 'default_insert': return f'INSERT INTO {table}(ID) VALUES({row})', None
    if role.startswith('predicate_'): return f'SELECT COUNT(*) FROM {table} WHERE ID={row} AND V={expression(operand)}', None
    exp = 'DATEADD(second,1,V)' if role == 'dateadd_update' else expression(operand)
    return f'UPDATE {table} SET V={exp} WHERE ID={row}', exp


def operation(value, plan, case, entry, previous_rows, helpers, allow_dateadd_loss=False):
    role, action, row, operand, expected = plan; table = entry['table']; v = helpers
    need(value['operation_id'] == case['case_id'] + ':' + role and value['role'] == role
        and value['operation'] == action and value['table'] == table and integer(value['row_id'], 'operation_row') == row, 'finite_operation_identity')
    need(value['api'] == ('ExecuteScalarAsync' if action == 'select' else 'ExecuteNonQueryAsync'), 'actual_operation_API')
    need(value['input'] == safe_value(operand) and value['expected'] == safe_value(expected), 'independent_requested_operation_values')
    sql, exp = sql_for(role, row, operand, table)
    need(value['sql'] == sql and value['sql_sha256'] == text_sha(sql) and value['expression_sql'] == exp
        and value['expression_sha256'] == (None if exp is None else text_sha(exp))
        and value['predicate_expression_sql'] == (expression(operand) if action == 'select' else None), 'exact_FF7_SQL_provenance')
    v.route(value['command_route'], 'command'); v.route(value['connection_route'], 'connection'); v.profile(value['operation_identity'], 'shared')
    typed = role in ('typed_insert', 'typed_expected'); parameters = value['parameters']
    need(integer(value['parameter_count'], 'parameter_count') == len(parameters) == (1 if typed else 0), 'finite_literal_or_typed_parameter_count')
    if typed:
        parameter = parameters[0]; v.route(parameter['route'], 'parameter')
        need(parameter['db_type'] == ('DateTimeOffset' if case['kind'] == 'dto' else 'DateTime2')
            and integer(parameter['scale'], 'parameter_scale') == 7 and integer(parameter['precision'], 'parameter_precision') == 0
            and parameter['value'] == safe_value(operand), 'same_input_typed_seven_control')
    catalog(value['catalog_pre'], entry, case['kind']); catalog(value['catalog_post'], entry, case['kind'])
    need(value['catalog_pre'] == value['catalog_post'], 'catalog_unchanged')
    need(integer(value['row_total_pre'], 'row_total_pre') == previous_rows
        and integer(value['row_total_post'], 'row_total_post') == previous_rows + (1 if action == 'insert' else 0), 'per_operation_row_increment')
    if action == 'insert':
        absence = value['prewrite_absence']['oracle_read_attempts']
        need(len(absence) == 1 and absence[0]['table'] == table and absence[0]['column'] == 'V'
            and integer(absence[0]['row_id'], 'absent_row') == row and integer(absence[0]['rows'], 'absent_rows') == 0
            and absence[0]['completed'] is True, 'fresh_absent_before_insert')
        v.profile(absence[0]['identity'], 'shared')
    if action == 'update':
        need(value['before_expected'] == safe_value(case['initial']), 'fresh_UPDATE_prior_requested_value')
        prior = value['before_oracle_read_attempts']['oracle_read_attempts']
        need(len(prior) == 1 and prior[0]['table'] == table and prior[0]['column'] == 'V'
            and integer(prior[0]['row_id'], 'before_UPDATE_row_id') == row and integer(prior[0]['rows'], 'before_UPDATE_rows') == 1
            and prior[0]['completed'] is True, 'fresh_UPDATE_prior_original_V_read')
        v.profile(prior[0]['identity'], 'shared')
        need(observation(value['before_oracle'], case['initial'], prior[0], v)['outcome'] == 'exact', 'fresh_UPDATE_prior_exact_value')
    if action == 'select':
        need(value['affected_rows'] is None and integer(value['scalar_result'], 'predicate_scalar') == (1 if role == 'predicate_positive' else 0), 'executed_positive_and_negative_predicate_counts')
    else: need(integer(value['affected_rows'], 'affected_rows') == 1 and value['scalar_result'] is None, 'real_single_row_write')
    attempts = value['oracle_read_attempts']
    need(len(attempts) == 1 and attempts[0]['table'] == table and attempts[0]['column'] == 'V'
        and integer(attempts[0]['row_id'], 'fresh_row_id') == row and integer(attempts[0]['rows'], 'fresh_rows') == 1
        and attempts[0]['completed'] is True, 'completed_fresh_original_V_oracle')
    v.profile(attempts[0]['identity'], 'shared')
    result = observation(value['oracle'], expected, attempts[0], v)
    need(value['outcome'] == result['outcome'], 'independent_exact_loss_classification')
    if result['outcome'] == 'loss':
        need(allow_dateadd_loss and role == 'dateadd_update' and value['status'] == 'failed'
            and value['error_stage'] == 'value_mismatch', 'only_complete_last_DATEADD_loss_is_scoped')
        exact_oracle_mismatch(value['error'], 'actual_DATEADD_operation_OracleMismatch')
    else:
        need(value['status'] == 'completed' and 'error' not in value and 'error_stage' not in value, 'complete_exact_operation_without_error')
    need(result['outcome'] == 'exact' or allow_dateadd_loss and role == 'dateadd_update', 'all_literal_and_typed_gates_must_be_exact')
    return result, previous_rows + (1 if action == 'insert' else 0)


def table_definitions(cases):
    result = {'F7-TABLE-DT': ('dt', None), 'F7-TABLE-DTO': ('dto', None)}
    for case in cases:
        if case['context'] == 'default': result[case['table_key']] = (case['kind'], case['expected'])
    return result


def create_sql(entry, default):
    value_default = ' DEFAULT ' + expression(default) if default is not None else ''
    return 'CREATE TABLE ' + entry['table'] + '(ID INT NOT NULL,V ' + entry['literal_store_type'] + value_default + ',' + entry['owner_marker'] + " VARCHAR(32) DEFAULT '" + entry['nonce'] + "',CONSTRAINT " + entry['primary_key'] + ' PRIMARY KEY(ID))'


def default_catalog(value, case, table):
    query = "SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t AND COLUMN_NAME='V'"
    need(value['query'] == query and value['query_sha256'] == text_sha(query) and value['table'] == table and value['column'] == 'V'
        and integer(value['rows'], 'default_catalog_rows') == 1 and value['is_null'] is False
        and 1 <= integer(value['text_length'], 'bounded_default_text_length') <= 1024 and HASH.fullmatch(value['text_sha256']), 'precise_owned_default_catalog_source')
    exp = expression(case['expected'])
    need(value['expected_expression'] == exp and value['expected_expression_sha256'] == text_sha(exp)
        and value['interpretation'] == 'pending_hash_only_not_semantic_acceptance', 'default_catalog_hash_is_not_effect_proof')


def reference(case_id, table, row): return dict(case_id=case_id, table=table, row_id=row)


def validate_references(value, case, entries, controls):
    table = entries[case['table_key']]['table']; context = case['context']
    if context in ('domain', 'default'): ref = reference(case['case_id'], table, case['typed_row_id'])
    elif context == 'predicate':
        target = next(row for row in registry() if row['case_id'] == case['source_case_id'])
        ref = reference(target['case_id'], table, target['typed_row_id'])
    elif context == 'assignment': ref = reference(case['control_case_id'], table, case['control_row_id'])
    else: ref = reference(case['case_id'], table, case['expected_row_id'])
    need(value['control_reference'] == ref and (ref['case_id'], ref['row_id']) in controls
        and controls[(ref['case_id'], ref['row_id'])] == (table, case['expected']), 'same_table_prior_exact_typed_control')
    if context in ('assignment', 'dateadd'):
        source_case = 'F7-V-' + case['kind'].upper() + ('-tail0' if context == 'assignment' else '-tail7')
        source_row = 1 if context == 'assignment' else 15
        source_ref = reference(source_case, table, source_row)
        need(value['source_control_reference'] == source_ref and controls.get((source_case, source_row)) == (table, case['initial']), 'same_table_source_value_control')
        need(value['operations'][-1]['control_reference'] == ref, 'update_expected_control_binding')


def inherited_settings(value):
    need(value == dict(status='inherited_unknown', source_report_sha256=policy()['history']['main'], observed_unknown_number=-5504,
        query_executed=False, fallback_query_attempted=False, mode_inferred=False), 'prior_unknown_settings_no_new_query_or_mode_inference')


def report_contract(main, cleaned, ledger, startup, startup_sha, allow_dateadd_loss=False):
    v = value_helpers(); p = policy(); specs = registry()
    for report in (main, cleaned, ledger): v.no_credentials(report)
    for report in (main, cleaned):
        need(report['schema_version'] == 2 and report['producer_version'] == 2 and report['task'] == TASK and report['profile'] == 'shared'
            and report['candidate_acceptance'] is False and report['full_EF_C_accepted'] is False
            and report['literal_support_accepted'] is False and report['R04_accepted'] is False, 'isolated_v2_characterization_scope')
        need(report['design_sha256'] == p['design_sha256'] and report['scope'] == 'FF7_EXPANSION53_only'
            and report['startup_assembly_name'] == 'TP07LiteralProbe' and report['startup_name_compatibility_only'] is True, 'exact_expansion_design_and_startup_scope')
        need(report['execution_role'] == 'controlled_direct_SQL_not_EF_HasData_SaveChanges_ExecuteUpdate', 'direct_SQL_context_not_real_EF_claim')
        need(report['expected_only'] is False and report['database_accessed'] is True and report['observer_failed'] is False, 'real_observable_expansion_process')
        need(report['identity_manifest_sha256'] == v.policy()['candidate_identity_sha256']
            and report['capabilities_sha256'] == v.policy()['capabilities_sha256'], 'same_candidate_capabilities')
        v.runtime_identity(report['runtime_identity'], startup, startup_sha)
    need(cleaned['status'] == 'cleanup_complete' and integer(cleaned['process_exit_code'], 'cleanup_process_exit_code') == 0
        and not any(key in cleaned for key in ('failure', 'failed_case_id', 'failed_operation_id', 'cleanup_error'))
        and 'cleanup_error' not in main, 'independent_cleanup_complete_without_failure')
    v.profile(main['physical_profile'], 'shared'); v.route(main['factory_connection'], 'connection')
    need(main['provider']['assembly']['dll_sha256'] == startup['ef_provider']['dll_sha256']
        and main['provider']['assembly']['mvid'] == startup['ef_provider']['mvid'], 'actual_EF_provider_identity')
    inherited_settings(main['settings_observation'])
    need(ledger['schema_version'] == 1 and ledger['profile'] == 'shared' and ledger['package_sha256'] == p['W']['package_sha256']
        and ledger['identity_sha256'] == v.policy()['candidate_identity_sha256'], 'same_package_owned_ledger')
    definitions = table_definitions(specs); values = ledger['objects']
    need(len(values) == 6 and {entry['case_id'] for entry in values} == set(definitions)
        and len({entry['table'] for entry in values}) == 6, 'exact_six_owned_tables')
    entries = {entry['case_id']: entry for entry in values}
    for key, entry in entries.items():
        kind, default = definitions[key]; store = 'DATETIME(7) WITH TIME ZONE' if kind == 'dto' else 'TIMESTAMP(7)'
        nonce = entry['nonce']
        need(type(nonce) is str and re.fullmatch(r'[A-F0-9]{32}', nonce) and entry['table'] == 'TPL_' + nonce[:24]
            and entry['owner_marker'] == 'OWN_' + nonce[:24] and entry['primary_key'] == 'TP07PK_' + nonce[:24], 'fresh_nonce_owned_names')
        need(entry['shape'] == 'Literal' and entry['scale'] == 7 and entry['literal_store_type'] == store
            and entry['absent_before_create'] is True and entry['create_acknowledged'] is True and entry['creation_state'] == 'confirmed', 'confirmed_fixed_seven_owned_schema')
        create = entry['create_evidence']
        need(create['call_returned'] is True and create['api'] in ('ExecuteNonQuery', 'ExecuteNonQueryAsync')
            and create['sql_sha256'] == text_sha(create_sql(entry, default)), 'actual_create_lifecycle_and_DDL_provenance')
    cases = main['cases']
    need(len(cases) == integer(main['case_count'], 'case_count') == 53 and [case['case_id'] for case in cases]
        == main['required_case_ids'] == [spec['case_id'] for spec in specs], 'complete_ordered_expansion53')
    totals = {key: 0 for key in entries}; counts = dict(insert=0, update=0, predicate_select=0); controls = {}; results = []
    for value, case in zip(cases, specs):
        entry = entries[case['table_key']]
        need(value['ordinal'] == case['ordinal'] and type(value['ordinal']) is int and value['context'] == case['context']
            and value['kind'] == case['kind'] and value['table'] == entry['table'], 'ordered_logical_case_identity')
        plans_for_case = plans(case); operations = value['operations']
        need(len(operations) == len(plans_for_case) and [op['role'] for op in operations] == [plan[0] for plan in plans_for_case], 'typed_expression_and_context_operation_order')
        if case['context'] == 'default':
            create = value['create']; ddl = create_sql(entry, case['expected'])
            need(create['sql'] == ddl and create['sql_sha256'] == text_sha(ddl) and create['call_returned'] is True
                and create['api'] in ('ExecuteNonQuery', 'ExecuteNonQueryAsync') and -1 <= integer(create['affected_rows'], 'DDL_affected_rows') <= 1, 'real_selected_expression_DEFAULT_DDL')
            v.route(create['command_route'], 'command'); v.route(create['connection_route'], 'connection'); v.profile(create['identity'], 'shared')
            default_catalog(value['default_catalog_pre'], case, entry['table']); default_catalog(value['default_catalog_post'], case, entry['table'])
            need(value['default_catalog_pre'] == value['default_catalog_post'], 'owned_default_catalog_unchanged')
        last_result = None
        for index, (op, plan) in enumerate(zip(operations, plans_for_case)):
            allow_last = allow_dateadd_loss and case['context'] == 'dateadd' and plan[0] == 'dateadd_update'
            last_result, totals[case['table_key']] = operation(op, plan, case, entry, totals[case['table_key']], v, allow_last)
            if plan[0] in ('typed_insert', 'typed_expected'):
                need(last_result['outcome'] == 'exact', 'every_typed_control_exact')
                controls[(case['case_id'], plan[2])] = (entry['table'], plan[4])
            if index > 0 or case['context'] == 'predicate': validate_references(value, case, entries, controls)
            counts['predicate_select' if plan[1] == 'select' else plan[1]] += 1
        if case['context'] in ('assignment', 'dateadd'):
            before = operations[-1]['before_oracle']; after = operations[-1]['oracle']
            need(case['initial']['local_ticks'] != case['expected']['local_ticks'] and before['local_ticks'] == case['initial']['local_ticks']
                and (case['context'] == 'dateadd' and last_result['outcome'] == 'loss'
                    or before['local_ticks'] != after['local_ticks']), 'actual_changed_value_UPDATE')
        loss = last_result['outcome'] == 'loss'
        need(value['status'] == ('failed' if loss else 'completed') and value['outcome'] == ('loss' if loss else 'exact'), 'logical_case_completion')
        if loss:
            need(case['case_id'] == 'F7-A-DT-second1' and value['failure_stage'] == 'dateadd_value_mismatch', 'separate_R04_value_failure_stage')
            exact_oracle_mismatch(value['error'], 'actual_DATEADD_case_OracleMismatch')
        else: need('error' not in value and 'failure_stage' not in value, 'completed_case_without_failure')
        results.append(dict(case_id=case['case_id'], context=case['context'], outcome=last_result['outcome']))
    need(counts == main['operation_counts'] == dict(insert=92, update=3, predicate_select=12), 'executed_fixed_operation_counts')
    expected_totals = {key: 39 if key == 'F7-TABLE-DT' else 45 if key == 'F7-TABLE-DTO' else 2 for key in entries}
    need(totals == expected_totals, 'exact_final_six_table_rows')
    final = main['final_row_totals']
    need(len(final) == 6 and len({row['table'] for row in final}) == 6
        and {row['table']: integer(row['rows'], 'final_fresh_rows') for row in final} == {entries[key]['table']: rows for key, rows in totals.items()}, 'fresh_final_table_row_totals')
    for row in final: v.profile(row['identity'], 'shared')
    r04 = results[-1]['outcome'] == 'exact'
    need(main['literal_observation_set_complete'] is True, 'complete_prior_fifty_two_literal_observations')
    need(integer(main['process_exit_code'], 'main_process_exit_code') == (0 if r04 else 1)
        and main['status'] == ('expansion_complete' if r04 else 'execution_failed') and main['expansion_completed'] is r04
        and main['characterization_complete'] is r04 and integer(main['completed_case_count'], 'completed_case_count') == (53 if r04 else 52), 'honest_complete_or_narrow_R04_failure_counts')
    if r04:
        need(not any(key in main for key in ('failure', 'failed_case_id', 'failed_operation_id')), 'full_completion_has_no_failure_fields')
    else:
        need(main['failed_case_id'] == cases[-1]['case_id'] == 'F7-A-DT-second1'
            and main['failed_operation_id'] == cases[-1]['operations'][-1]['operation_id'] == 'F7-A-DT-second1:dateadd_update', 'exact_last_DATEADD_failure_IDs')
        exact_oracle_mismatch(main['failure'], 'actual_main_DATEADD_OracleMismatch')
    v.cleanup(main['finally_cleanup'], ledger, 'shared'); v.cleanup(cleaned['cleanup'], ledger, 'shared'); v.cleanup(cleaned['finally_cleanup'], ledger, 'shared')
    return dict(observations=results, literal_gates_complete=True, r04_gate_complete=r04, characterization_complete=r04,
        issues=[] if r04 else ['R04_DATEADD_value_mismatch'])

def artifact(reference):
    need(set(reference) == {'path', 'sha256'} and sha(reference['path']) == reference['sha256'], 'frozen_artifact_hash')
    return load(reference['path'])


def source_closure(replay, hashes):
    for name, digest in hashes.items():
        path = Path(name)
        need(not path.is_absolute() and '..' not in path.parts and '\\' not in name
            and not set(path.parts) & {'.local', 'secrets'}, 'relative_compile_source_path')
        need(sha(replay / path) == digest, 'actual_compile_source_lock_assets_drift')


def sdk_receipt(document, phase):
    commands = document['commands']
    need(document['SDK_version'] == '10.0.401' and len(commands) == (2 if phase == 'lock' else 3), 'actual_pinned_SDK_command_count')
    for row in commands:
        need(row['exit_code'] == 0 and row['timed_out'] is False and row['termination_code'] is None
            and sha(row['log_path']) == row['log_sha256'], 'successful_SDK_command_and_log')
        need(type(row['command']) is list and row['command'][0] == str(Path.home() / '.dotnet/dotnet'), 'fixed_dotnet_host')
    need(commands[0]['command'][1:] == ['--version'] and Path(commands[0]['log_path']).read_text().strip() == '10.0.401', 'actual_SDK_version_log')
    for row in commands[1:]: need(all(flag in row['command'] for flag in
        ('-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers')), 'single_node_disabled_build_servers')
    restore = commands[1]['command']
    need(restore[1:3] == ['restore', PROJECT] and '--disable-parallel' in restore
        and ('--force-evaluate' in restore and '--use-lock-file' in restore if phase == 'lock' else '--locked-mode' in restore), 'finite_constructor_restore_command')
    if phase == 'build': need(commands[2]['command'][1:3] == ['build', PROJECT] and '--no-restore' in commands[2]['command'], 'finite_constructor_build_command')


def validate_chain(startup, startup_sha, receipt, identity):
    p = policy(); v = value_helpers(); pack_binding(startup, identity, 'build'); pack_binding(receipt, identity, 'run')
    need(startup['task'] == TASK and startup['W'] == p['W'] and startup['lineage']['ef_commit'] == v.policy()['ef']['commit']
        and startup['lineage']['base_archive_sha256'] == v.policy()['ef']['archive_sha256'], 'same_W_and_EF_lineage')
    source_ref = dict(path=startup['source_manifest_path'], sha256=startup['source_manifest_sha256'])
    lock_ref = dict(path=startup['lock_manifest_path'], sha256=startup['lock_manifest_sha256'])
    source = artifact(source_ref); locked = artifact(lock_ref)
    pack_binding(source, identity, 'prepare'); pack_binding(locked, identity, 'lock')
    need(source['lineage'] == locked['lineage'] == startup['lineage'] and locked['source_manifest_sha256'] == source_ref['sha256'], 'three_phase_source_parent_chain')
    expected = dict(source['source_hashes']); need(LOCK not in expected and PROJECT in expected, 'new_constructor_project_source')
    expected[LOCK] = locked['lock_sha256']
    need(expected == locked['source_hashes'] == startup['source_hashes']
        and locked['provider_lock_sha256'] == expected[PROVIDER_LOCK], 'only_new_constructor_lock_delta')
    replay = Path(startup['replay_root']); source_closure(replay, expected)
    for collection in ('locks', 'assets'): source_closure(replay, startup[collection])
    need(startup['locks'] == {name: digest for name, digest in expected.items() if name.endswith('packages.lock.json')}, 'full_pinned_lock_collection')
    need(set(startup['assets']) == {'src/W.EntityFrameworkCore.Dameng/obj/project.assets.json', 'test/TP07ConstructorProbe/obj/project.assets.json'}, 'exact_two_constructor_assets')
    original = artifact(dict(path=startup['lineage_artifacts']['approved_ef_source_manifest']['path'], sha256=startup['lineage']['approved_ef_source_manifest_sha256']))['source_hashes']
    need(len(original) == 175 and len(source['source_hashes']) == 182 and len(expected) == 183
        and source['approved_ef_source_hashes'] == original and all(source['source_hashes'].get(name) == digest for name, digest in original.items()), 'approved_EF_source_preserved')
    additions = {name: digest for name, digest in source['source_hashes'].items() if name not in original}
    constructor_sources = { 'test/TP07ConstructorProbe/' + name: identity['source_hashes'][PREFIX + 'constructor-probe/' + name]
        for name in ('TP07ConstructorProbe.csproj', 'Program.cs', 'Cases.cs') }
    constructor_sources.update({'test/common/' + name: identity['source_hashes'][PREFIX + 'runtime-templates/common/' + name]
        for name in ('CandidateGuard.cs', 'OwnedObjects.cs', 'SafeEvidence.cs')})
    constructor_sources['test/TP07LiteralProbe/LiteralOracle.cs'] = identity['source_hashes'][PREFIX + 'runtime-templates/TP07LiteralProbe/LiteralOracle.cs']
    need(additions == constructor_sources, 'finite_constructor_compile_additions')
    need(source['source_pack_scope'] == startup['source_pack_scope'] == 'fourteen_source_helper_files_seven_compile_inputs'
        and source['compile_inputs'] == startup['compile_inputs'] == constructor_sources, 'explicit_seven_compile_input_scope')
    template = startup['lineage_artifacts']['runtime_template_source']
    need(startup['lineage']['runtime_template_source_sha256'] == identity['manifest_sha256']
        and template == dict(path=identity['manifest_path'], sha256=identity['manifest_sha256']), 'isolated_constructor_template_lineage')
    for row in startup['lineage_artifacts'].values(): need(sha(row['path']) == row['sha256'], 'original_lineage_artifact_drift')
    need(source['lineage_artifacts'] == startup['lineage_artifacts'], 'same_lineage_artifact_chain')
    for name, field in [('base_archive', 'base_archive_sha256'), ('bridge_patch', 'bridge_patch_sha256'),
            ('mapping_manifest', 'mapping_manifest_sha256'), ('mapping_patch', 'mapping_patch_sha256'),
            ('approved_ef_source_manifest', 'approved_ef_source_manifest_sha256'), ('runtime_template_source', 'runtime_template_source_sha256')]:
        need(startup['lineage_artifacts'][name]['sha256'] == startup['lineage'][field], 'bound_lineage_artifact_fields')
    provider = load(replay / PROVIDER_LOCK)['dependencies']['net10.0']; lock = load(replay / LOCK)['dependencies']['net10.0']
    originals = {name: row for name, row in provider.items() if name != 'W.DmProvider'}
    projects = [name for name in lock if name.casefold() == 'w.entityframeworkcore.dameng']
    need(len(originals) == 13 and len(projects) == 1 and len(lock) == len({name.casefold() for name in lock})
        and set(lock) == set(originals) | {'W.DmProvider', projects[0]}, 'approved_baseline_thirteen_dependency_graph')
    for name, old in originals.items():
        need(lock[name]['resolved'] == old['resolved'] and lock[name]['contentHash'] == old['contentHash']
            and lock[name].get('dependencies', {}) == old.get('dependencies', {}), 'unchanged_non_driver_dependency')
    need(lock['W.DmProvider']['type'] == 'Direct' and lock['W.DmProvider']['resolved'] == p['W']['version']
        and lock['W.DmProvider']['contentHash'] == provider['W.DmProvider']['contentHash']
        and lock[projects[0]]['type'] == 'Project'
        and lock[projects[0]]['dependencies'] == {'Microsoft.EntityFrameworkCore.Relational': '[10.0.12, 11.0.0)', 'W.DmProvider': '[' + p['W']['version'] + ', ' + p['W']['version'] + ']'}, 'exact_W_and_provider_project')
    need(len(locked['non_driver_records']) == 13 and {row['id'] for row in locked['non_driver_records']} == set(originals), 'thirteen_independent_package_records')
    for row in locked['non_driver_records']:
        need(row['version'] == originals[row['id']]['resolved'] and HASH.fullmatch(row['archive_sha256'])
            and HASH.fullmatch(row['fresh_metadata_sha256']), 'baseline_archive_and_metadata_proof')
    references = receipt['phase_receipts']
    parents = {name: references[name] for name in ('prepare', 'lock')}
    need(set(references) == {'prepare', 'lock', 'build'} and startup['phase_receipts'] == locked['phase_receipts'] == parents, 'three_external_phase_receipts')
    for phase in ('prepare', 'lock', 'build'):
        document = artifact(references[phase]); pack_binding(document, identity, phase)
        need(document['source_manifest_sha256'] == source_ref['sha256'] and document['database_accessed'] is False, 'external_phase_source_binding')
        if phase == 'prepare': need(document['SDK_executed'] is False and document['independent_replay_equal'] is True, 'source_only_prepare')
        else: sdk_receipt(document, phase)
        if phase == 'lock': need(document['lock_sha256'] == locked['lock_sha256'], 'actual_lock_receipt_binding')
        if phase == 'build': need(document['startup_manifest_sha256'] == startup_sha and document['lock_manifest_sha256'] == lock_ref['sha256']
            and document['phase_receipts'] == parents, 'actual_build_startup_binding')
    historical = receipt['historical_l0_failure']; need(historical['sha256'] == OLD_FAILURE_SHA and sha(historical['path']) == OLD_FAILURE_SHA, 'preserved_original_L0_failure')
    history_paths = validate_history(receipt['historical_screen25'])
    assemblies = startup['startup_assemblies']; need(len(assemblies) == 1 and assemblies[0]['name'] == 'TP07LiteralProbe', 'isolated_constructor_startup_identity')
    target = assemblies[0]; binary = replay / DLL
    binaries = {binary: target['dll_sha256'], binary.with_suffix('.deps.json'): target['deps_sha256'],
        binary.parent / 'W.DmProvider.dll': p['W']['dll_sha256'], binary.parent / 'W.EntityFrameworkCore.Dameng.dll': startup['ef_provider']['dll_sha256']}
    for path, expected_hash in binaries.items(): need(sha(path) == expected_hash, 'actual_constructor_binary_artifacts')
    deps = load(binary.with_suffix('.deps.json'))['libraries']
    need(deps['W.DmProvider/' + p['W']['version']]['type'] == 'package'
        and not any(name.casefold().startswith('dm.dmprovider/') for name in deps), 'no_official_driver_fallback')
    mandatory = [Path(identity['manifest_path']), Path(source_ref['path']), Path(lock_ref['path']), Path(historical['path'])]
    mandatory += history_paths
    mandatory += [ROOT / name for name in PACK_FILES] + [Path(row['path']) for row in references.values()]
    mandatory += [Path(row['path']) for row in startup['lineage_artifacts'].values()] + list(binaries)
    mandatory += [replay / name for name in set(expected) | set(startup['assets'])]
    need(all(receipt['input_hashes_before'].get(str(path)) == sha(path) for path in mandatory), 'complete_fourteen_pack_compile_and_artifact_inputs')
    need(v.policy()['candidate_identity_sha256'] in receipt['input_hashes_before'].values()
        and v.policy()['capabilities_sha256'] in receipt['input_hashes_before'].values(), 'actual_candidate_and_capability_input_hashes')
    need(receipt['input_hashes_before'] == receipt['input_hashes_after'], 'execution_input_hashes_before_after')
    for path, digest in receipt['input_hashes_after'].items(): need(sha(path) == digest, 'current_execution_input_drift')


def validate_history(references):
    p = policy(); need(set(references) == set(p['history']), 'complete_preserved_SCREEN25_history')
    paths = []
    documents = {}
    for name, expected in p['history'].items():
        ref = references[name]; need(ref['sha256'] == expected, 'pinned_SCREEN25_history_identity')
        documents[name] = artifact(ref); paths.append(Path(ref['path']))
    pack = documents['source_pack']
    need(pack['task'] == 'TP07-constructor-screen source pack' and set(pack['source_hashes']) == set(PACK_FILES), 'old_SCREEN25_pack_not_rewritten_as_v2')
    root = Path(references['source_pack']['path']).parent / 'source'
    for name, expected in pack['source_hashes'].items():
        path = root / name; need(sha(path) == expected, 'preserved_SCREEN25_source_snapshot'); paths.append(path)
    validation = documents['validation']; main = documents['main']; cleanup = documents['cleanup']
    need(validation['characterization_complete'] is True and validation['issues'] == []
        and validation['candidate_acceptance'] is False and validation['full_EF_C_accepted'] is False
        and len(validation['observations']) == 25
        and sum(row['outcome'] == 'exact' for row in validation['observations']) == 19
        and sum(row['outcome'] == 'loss' for row in validation['observations']) == 6, 'original_SCREEN25_characterization_preserved')
    need(main['task'] == 'TP07-constructor-screen' and main['process_exit_code'] == 0 and main['paired_controls_exact'] is True
        and cleanup['process_exit_code'] == 0 and cleanup['status'] == 'cleanup_complete', 'original_SCREEN25_process_proof')
    settings = main['settings_observation']
    need(settings['status'] == 'unknown' and settings['error']['number'] == -5504 and settings['fallback_query_attempted'] is False, 'historical_settings_unknown_not_mode_zero')
    return paths


def validate(args):
    identity, before = pack_entry(args.source_pack_manifest, args.source_pack_manifest_sha256)
    need(sha(args.startup_manifest) == args.startup_manifest_sha256, 'externally_approved_constructor_startup')
    startup = load(args.startup_manifest); receipt = load(args.execution_receipt)
    value_helpers().no_credentials(receipt)
    need(receipt['task'] == TASK and receipt['status'] in ('expansion_complete', 'r04_value_mismatch')
        and receipt['candidate_acceptance'] is False and receipt['full_EF_C_accepted'] is False, 'external_characterization_scope')
    need(receipt['startup_manifest_sha256'] == args.startup_manifest_sha256
        and receipt['input_hashes_before'].get(str(Path(args.startup_manifest).resolve())) == args.startup_manifest_sha256, 'executed_startup_manifest_binding')
    validate_chain(startup, args.startup_manifest_sha256, receipt, identity)
    narrow = receipt['status'] == 'r04_value_mismatch'
    for role, path in [('main', args.main), ('cleanup', args.cleanup)]:
        process = receipt[role]
        need(process['exit_code'] == (1 if role == 'main' and narrow else 0) and process['timed_out'] is False and process['termination_code'] is None
            and process['report']['path'] == str(Path(path).resolve()) and process['report']['sha256'] == sha(path), 'natural_independent_process_artifact')
    need(receipt['ledger']['path'] == str(Path(args.ledger).resolve()) and receipt['ledger']['sha256'] == sha(args.ledger), 'frozen_owned_ledger')
    main = load(args.main); cleanup = load(args.cleanup); ledger = load(args.ledger)
    need(main['finally_cleanup']['ledger_sha256'] == cleanup['cleanup']['ledger_sha256'] == cleanup['finally_cleanup']['ledger_sha256'] == sha(args.ledger), 'fresh_cleanup_ledger_binding')
    result = report_contract(main, cleanup, ledger, startup, args.startup_manifest_sha256, allow_dateadd_loss=narrow)
    need(narrow is (not result['r04_gate_complete']), 'external_receipt_matches_independent_R04_verdict')
    after = pack_hashes(identity); need(after == before, 'validator_exit_source_pack_drift')
    return dict(schema_version=2, task=TASK + ' independent validation', **result,
        candidate_acceptance=False, full_EF_C_accepted=False, literal_support_accepted=False, product_patch_authorized=False,
        source_pack=identity, source_pack_check=dict(phase='validate', before=before, after=after),
        source_pack_scope='fourteen_source_helper_files_seven_compile_inputs',
        startup_manifest_sha256=args.startup_manifest_sha256, execution_receipt_sha256=sha(args.execution_receipt))


def main():
    parser = argparse.ArgumentParser()
    for flag in ('main', 'cleanup', 'ledger', 'execution-receipt', 'startup-manifest', 'startup-manifest-sha256',
            'source-pack-manifest', 'source-pack-manifest-sha256', 'output'): parser.add_argument('--' + flag, required=True)
    args = parser.parse_args(); output = Path(args.output); need(not output.exists(), 'fresh_validation_output')
    try: result = validate(args); code = 0 if result['characterization_complete'] else 1
    except Exception as error:
        result = dict(schema_version=2, task=TASK + ' independent validation', issues=[str(error) if isinstance(error, ContractError) else type(error).__name__],
            literal_gates_complete=False, r04_gate_complete=False, characterization_complete=False, candidate_acceptance=False,
            full_EF_C_accepted=False, literal_support_accepted=False, product_patch_authorized=False); code = 1
    output.write_text(json.dumps(result, indent=2, sort_keys=True) + '\n'); return code


if __name__ == '__main__': raise SystemExit(main())
