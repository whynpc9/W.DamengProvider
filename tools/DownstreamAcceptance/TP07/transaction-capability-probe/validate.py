#!/usr/bin/env python3
"""Independent file-only T1 contract. Synthetic contracts are not live acceptance.

No producer success flag is an oracle. Integer calendar/FF9 closure is adapted
from runtime-validate.py (d9ea39850a3d394f86f2fef54fe8330d732ea3a46d6823da3da74e84b9188d55).
The packed decoder is a byte-pinned, ROOT-independent audited copy. This module
never starts a process, accesses secrets, invokes SDKs, or opens a database.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import types

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
HASH = re.compile(r'[a-f0-9]{64}')
MVID = re.compile(r'[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}')
PACKED_SHA = 'bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9'
BASE_TICKS = 639204129161234560
OFFSET = 330
HOST = 'TP07TransactionCapabilityProbe'
CASE_IDS = ('T1-shared-sync-pair', 'T1-shared-async-pair', 'T1-tls-sync-pair', 'T1-tls-async-pair')
STEPS = ('runtime_identity', 'create', 'seed_SaveChanges', 'fresh_before', 'begin_transaction', 'capabilities', 'tracking_query', 'transaction_SaveChanges', 'query_inside_transaction', 'full_Rollback', 'dispose_transaction_context', 'fresh_after')
TOP = {
 'inputs': 'workspace_root original_repo sdk python W_identity capabilities ef_overlay probe_source_manifest toolpack helper_origins expected_closure',
 'source': 'inputs toolpack replay_root lineage W source_hashes exact_paths exact_count role',
 'native': 'inputs source sdk_events project_graphs cache_root archive_records native_metadata_records source_before source_after database_accessed',
 'build': 'inputs source native locked_restores commands startup binaries source_before source_after lock_before lock_after offline_proofs database_accessed locked_project_graphs',
 'startup': 'source native replay_root lineage W locks assets host ef_provider ef_dependencies source_hashes',
 'runtime-approval': 'status inputs source native build startup toolpack offline readiness wrappers ordered_cases budgets output_root original_repo',
 'case-ticket': 'approval cohort_id ordinal case_id role startup predecessor_validation output ledger',
 'case': 'approval ticket cohort_id case_id profile api definition runtime_identity services observer steps transactions commands events values ownership main_finally error status process_exit_code',
 'cleanup': 'approval ticket runtime_identity ledger_before objects cleanup finally_cleanup error status process_exit_code',
 'case-execution': 'approval cohort_id case_id ticket inputs_before main ledger independent_cleanup validator inputs_after status',
 'validation': 'approval case_receipt case_id status issues missing captured_complete checks positive_control_refs activation_accepted',
 'matrix': 'approval cohort_id required_cases attempted_cases case_receipts case_validations first_failure input_drift cleanup_pending status candidate_acceptance full_EF_C_accepted',
}


class ContractError(ValueError):
    """Only stable safe issue codes leave the validator."""


def require(value, code):
    if not value:
        raise ContractError(code)


def integer(value, code='integer'):
    require(type(value) is int, code)
    return value


def shape(value, keys, code):
    expected = set(keys.split()) if type(keys) is str else set(keys)
    require(type(value) is dict and set(value) == expected, code + '_shape')
    return value


def sequence(value, code):
    require(type(value) is list, code + '_array')
    return value


def exact(actual, expected, code):
    # Python equality alone accepts True == 1 and 1.0 == 1 recursively.
    require(type(actual) is type(expected), code + '_type')
    if type(expected) is dict:
        shape(actual, expected, code)
        for key in expected:
            exact(actual[key], expected[key], code + '_' + key)
    elif type(expected) is list:
        require(len(actual) == len(expected), code + '_length')
        for i, value in enumerate(expected):
            exact(actual[i], value, code + '_' + str(i))
    else:
        require(actual == expected, code)


def safe_tree(value):
    if type(value) is dict:
        for key, child in value.items():
            require(type(key) is str and not re.search(r'password|connection_string|secret|auth_packet|exception_message|stack_trace|^message$', key, re.I), 'unsafe_field')
            safe_tree(child)
    elif type(value) is list:
        for child in value:
            safe_tree(child)
    elif type(value) is str:
        require(not re.search(r'(?i)(?:password|pwd|user\s+id)\s*=', value), 'unsafe_value')
    else:
        require(value is None or type(value) in (bool, int), 'non_integer_number')


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate_json_key')
        result[key] = value
    return result


def loads(raw):
    def invalid(_):
        raise ContractError('non_integer_number')
    try:
        value = json.loads(raw, object_pairs_hook=_pairs, parse_float=invalid, parse_constant=invalid)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ContractError('invalid_json') from error
    safe_tree(value)
    return value


def load(path):
    return loads(Path(path).read_text(encoding='utf-8'))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def document(value, task):
    shape(value, 'schema_version task ' + TOP[task], task)
    exact(value['schema_version'], 1, 'schema_version')
    exact(value['task'], 'TP07-T1-' + task, 'document_task')
    safe_tree(value)
    return value


def absolute_path(path, root=None, exists=True):
    require(type(path) is str and Path(path).is_absolute() and '\x00' not in path, 'absolute_path')
    p = Path(path)
    require('..' not in p.parts, 'parent_path')
    # Deny symlinks in every segment, including symlinks still within root.
    for segment in (p, *p.parents):
        require(not segment.is_symlink(), 'symlink_path')
    require(str(p) == str(p.resolve()), 'canonical_path')
    if root is not None:
        r = absolute_path(str(root), exists=False)
        require(p == r or r in p.parents, 'root_cross')
    require(not exists or p.is_file(), 'missing_file')
    return p


def ref(value, root=None):
    shape(value, 'path sha256', 'ref')
    require(type(value['sha256']) is str and HASH.fullmatch(value['sha256']), 'ref_sha_shape')
    path = absolute_path(value['path'], root)
    require(sha(path) == value['sha256'], 'ref_sha_mismatch')
    return path


def read_ref(value, root=None):
    return load(ref(value, root))


def definition(ordinal):
    integer(ordinal, 'ordinal_integer')
    require(0 <= ordinal < 4, 'ordinal_domain')
    return dict(ordinal=ordinal, case_id=CASE_IDS[ordinal], profile='shared' if ordinal < 2 else 'tls', api='sync' if ordinal % 2 == 0 else 'async', scale=7 if ordinal < 2 else 6, ticks=BASE_TICKS, offset_minutes=OFFSET, quantum=1 if ordinal < 2 else 10, supports_savepoints=ordinal < 2)


def ticks(fields):
    shape(fields, 'year month day hour minute second nanoseconds offset_minutes', 'fields')
    year, month, day, hour, minute, second, ns = (integer(fields[key], 'fields_integer') for key in ('year', 'month', 'day', 'hour', 'minute', 'second', 'nanoseconds'))
    require(1 <= year <= 9999 and 1 <= month <= 12, 'calendar_domain')
    leap = year % 4 == 0 and (year % 100 != 0 or year % 400 == 0)
    months = (31, 29 if leap else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31)
    require(1 <= day <= months[month - 1], 'calendar_day')
    require(0 <= hour < 24 and 0 <= minute < 60 and 0 <= second < 60 and 0 <= ns < 1_000_000_000 and ns % 100 == 0, 'clock_domain')
    if fields['offset_minutes'] is not None:
        require(-840 <= integer(fields['offset_minutes']) <= 840, 'offset_domain')
    y = year - 1
    days = 365*y + y//4 - y//100 + y//400 + sum(months[:month-1]) + day-1
    return days*864_000_000_000 + (hour*3600+minute*60+second)*10_000_000 + ns//100


def ff9(text, offset):
    expression = r'([0-9]{4})-([0-9]{2})-([0-9]{2}) ([0-9]{2}):([0-9]{2}):([0-9]{2})\.([0-9]{9})'
    if offset:
        expression += r' ([+-])([0-9]{2}):([0-9]{2})'
    require(type(text) is str, 'ff9_text')
    match = re.fullmatch(expression, text, flags=re.ASCII)
    require(match is not None, 'ff9_shape')
    fields = dict(zip(('year', 'month', 'day', 'hour', 'minute', 'second', 'nanoseconds'), map(int, match.groups()[:7])))
    zone = None
    if offset:
        sign, hours, minutes = match.groups()[7:]
        require(int(minutes) < 60, 'ff9_offset_minutes')
        zone = (int(hours)*60 + int(minutes)) * (-1 if sign == '-' else 1)
        require(not (sign == '-' and zone == 0), 'ff9_negative_zero')
    fields['offset_minutes'] = zone
    ticks(fields)
    return fields


def packet_decoder():
    path = HERE / 'packed_oracle.py'
    raw = path.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == PACKED_SHA, 'packed_oracle_pin')
    module = types.ModuleType('tp07_t1_independent_decoder')
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module.decode_packet


def typed(value, target, offset):
    if offset:
        exact(value, dict(type='System.DateTimeOffset', ticks=target, offset_minutes=OFFSET, utc_ticks=target-OFFSET*600_000_000), 'typed_DTO')
    else:
        exact(value, dict(type='System.DateTime', ticks=target, kind='Unspecified'), 'typed_DT')


def physical(value, profile):
    # The inherited guard adds transport evidence; its finite serializer is
    # checked at the runtime-identity layer as well as every fresh oracle.
    required = {'profile', 'server_version', 'message_version', 'encoding', 'negotiated_encoding', 'encoding_override_empty', 'profile_origin', 'server_user', 'server_schema', 'result_format'}
    shape(value, required | {'negotiated_encoding_origin', 'timezone'}, 'physical')
    exact(value['negotiated_encoding_origin'], 'physical_conn_property_serverEncoding_field', 'physical_encoding_source')
    integer(value['timezone'], 'physical_timezone')
    expected = ('8.1.5.60', 21, 'utf-8') if profile == 'shared' else ('8.1.4.6', 11, 'gb18030')
    exact([value[k] for k in ('server_version', 'message_version', 'encoding')], list(expected), 'physical_profile')
    exact(value['profile'], profile, 'physical_name')
    exact(value['negotiated_encoding'], expected[2], 'physical_negotiated_encoding')
    exact(value['encoding_override_empty'], True, 'physical_override')
    exact(value['profile_origin'], 'physical_get_conn_instance_conn_property', 'physical_source')
    exact(value['server_user'], 'WDM_PROVIDER_TEST', 'physical_user')
    exact(value['server_schema'], 'WDM_PROVIDER_TEST', 'physical_schema')
    exact(value['result_format'], 'ordinary_non_BDTA', 'physical_format')


def oracle(value, spec, offset, w):
    shape(value, 'rows is_null local_ticks offset_minutes raw ff9 typed metadata identity reader_route', 'oracle')
    exact(value['rows'], 1, 'oracle_rows'); exact(value['is_null'], False, 'oracle_null')
    physical(value['identity'], spec['profile'])
    route = value['reader_route']
    shape(route, 'role type dll_sha256 mvid', 'reader_route')
    exact(route, dict(role='reader', type='W.Dm.DmDataReader', dll_sha256=w['dll_sha256'], mvid=w['mvid']), 'reader_route_identity')
    raw, metadata = value['raw'], value['metadata']
    shape(raw, 'source capture_stage c_type width scale is_bdta hex payload_length payload_sha256 decoded', 'raw')
    shape(metadata, 'c_type width scale mask is_bdta metadata_role raw_source raw_c_type raw_scale raw_precision raw_mask_before raw_derived_mask raw_scale_status schema_scale', 'metadata')
    ctype = (27 if offset else 26) if spec['scale'] == 7 else (23 if offset else 16)
    width = {16:8,23:10,26:9,27:11}[ctype]
    for key, target in dict(c_type=ctype,width=width,scale=spec['scale'],mask=0,is_bdta=False,metadata_role='result_column',raw_source='ResultColumn',raw_c_type=ctype,raw_scale=spec['scale'],raw_precision=width,raw_mask_before=0,raw_derived_mask=0,raw_scale_status='captured_server_source',schema_scale=spec['scale']).items():
        exact(metadata[key], target, 'metadata_' + key)
    for key, target in dict(source='ordinary_result_cache_before_temporal_projection',capture_stage='before_any_temporal_getter',c_type=ctype,width=width,scale=spec['scale'],is_bdta=False,payload_length=width).items():
        exact(raw[key], target, 'raw_' + key)
    require(type(raw['hex']) is str and re.fullmatch(r'(?:[0-9A-F]{2}){'+str(width)+'}', raw['hex']), 'raw_hex')
    payload = bytes.fromhex(raw['hex'])
    exact(raw['payload_sha256'], hashlib.sha256(payload).hexdigest(), 'raw_hash')
    try:
        decoded = packet_decoder()(payload, ctype, spec['scale'])
    except ValueError as error:
        raise ContractError('independent_raw_decode') from error
    exact(raw['decoded'], decoded, 'reported_decoded')
    exact(ff9(value['ff9'], offset), decoded, 'independent_ff9')
    exact(ticks(decoded), BASE_TICKS, 'independent_ticks')
    exact(decoded['offset_minutes'], OFFSET if offset else None, 'independent_offset')
    exact(value['local_ticks'], BASE_TICKS, 'oracle_local_ticks')
    exact(value['offset_minutes'], OFFSET if offset else None, 'oracle_offset')
    typed(value['typed'], BASE_TICKS, offset)


def ledger_contract(value, spec, w):
    shape(value, 'schema_version profile package_sha256 identity_sha256 objects', 'ledger')
    exact(value['schema_version'], 1, 'ledger_version'); exact(value['profile'], spec['profile'], 'ledger_profile')
    exact(value['package_sha256'], w['package_sha256'], 'ledger_package'); exact(value['identity_sha256'], w['identity_sha256'], 'ledger_identity')
    require(type(value['objects']) is list and len(value['objects']) == 1, 'ledger_one_object')
    entry = value['objects'][0]
    shape(entry, 'case_id nonce table owner_marker primary_key index check index_acknowledged check_acknowledged shape scale alter_before_scale alter_target_scale alter_before_catalog alter_after_catalog creation_state absent_before_create create_acknowledged comment_acknowledged create_evidence comment_evidence create_failure columns literal_store_type', 'ledger_entry')
    exact(entry['case_id'], spec['case_id'], 'ledger_case')
    require(type(entry['nonce']) is str and re.fullmatch(r'[A-F0-9]{32}', entry['nonce']), 'ledger_nonce')
    suffix = entry['nonce'][:24]
    for key, target in dict(table='TPL_'+suffix, owner_marker='OWN_'+suffix, primary_key='TP07PK_'+suffix, shape='Basic', scale=spec['scale'], creation_state='confirmed', absent_before_create=True, create_acknowledged=True, comment_acknowledged=True,index_acknowledged=False,check_acknowledged=False).items():
        exact(entry[key], target, 'ledger_' + key)
    for key in ('index','check','alter_before_scale','alter_target_scale','alter_before_catalog','alter_after_catalog','create_failure','literal_store_type'):
        exact(entry[key], None, 'ledger_' + key)
    columns = [dict(name=n,kind=k,scale=s,leading=None) for n,k,s in [('ID','INT',None),('D','DT',spec['scale']),('Z','TZ',spec['scale']),('N','VARCHAR32',None)]]
    exact(entry['columns'], columns, 'ledger_columns')
    for key in ('create_evidence','comment_evidence'):
        row = shape(entry[key], 'call_returned api sql_sha256', 'ledger_ack')
        exact(row['call_returned'], True, 'ledger_ack_returned')
        require(row['api'] in ('ExecuteNonQuery','ExecuteNonQueryAsync','ExecuteSqlRaw','ExecuteSqlRawAsync'), 'ledger_ack_api')
        require(type(row['sql_sha256']) is str and HASH.fullmatch(row['sql_sha256']), 'ledger_ack_hash')
    return entry


def cleanup_contract(value, entry, ledger_sha, spec):
    shape(value, 'cleanup_verified objects ledger_sha256', 'cleanup')
    exact(value['cleanup_verified'], True, 'cleanup_verified')
    exact(value['ledger_sha256'], ledger_sha, 'cleanup_ledger_pin')
    require(type(value['objects']) is list and len(value['objects']) == 1, 'cleanup_exact_ledger')
    row = shape(value['objects'][0], 'table nonce dropped fresh_absent fresh_table_count identity', 'cleanup_object')
    exact(row['table'], entry['table'], 'cleanup_table'); exact(row['nonce'], entry['nonce'], 'cleanup_nonce')
    require(type(row['dropped']) is bool, 'cleanup_dropped_bool')
    exact(row['fresh_absent'], True, 'cleanup_fresh_absent'); exact(row['fresh_table_count'], 0, 'cleanup_fresh_count')
    physical(row['identity'], spec['profile'])


def identity(value, expected, type_name, name):
    shape(value, 'type name path sha256 mvid', 'identity')
    require(type(value['mvid']) is str and MVID.fullmatch(value['mvid']), 'identity_mvid')
    exact(value['type'], type_name, 'identity_type'); exact(value['name'], name, 'identity_name')
    for key in ('path', 'sha256', 'mvid'):
        exact(value[key], expected[key], 'identity_' + key)


def runtime_identity(value, startup, startup_sha):
    shape(value, 'version package_sha256 dll_sha256 mvid deps_sha256 runtime ef_package_version ef_provider source_manifest_sha256 archive_sha256 archive_check build_manifest_sha256 source_manifest_sha256_binding lock_manifest_sha256 startup_assembly startup_sha256 startup_mvid entry_host_type entry_host_sha256', 'runtime_identity')
    w = startup['W']
    for key in ('version','package_sha256','dll_sha256','mvid','source_manifest_sha256','archive_sha256'):
        exact(value[key], w[key], 'runtime_W_' + key)
    exact(value['ef_package_version'], '10.0.12', 'runtime_EF_version')
    require(type(value['runtime']) is str and re.fullmatch(r'10\.[0-9]+\.[0-9]+', value['runtime']), 'runtime_version')
    for key, target in dict(archive_check='byte_hash_only_no_archive_introspection',build_manifest_sha256=startup_sha,source_manifest_sha256_binding=startup['source']['sha256'],lock_manifest_sha256=startup['native']['sha256'],startup_assembly=HOST,startup_sha256=startup['host']['sha256'],startup_mvid=startup['host']['mvid'],deps_sha256=startup['host']['deps']['sha256'],entry_host_type=HOST,entry_host_sha256=startup['host']['sha256']).items():
        exact(value[key], target, 'runtime_' + key)
    exact(value['ef_provider'], dict(dll_sha256=startup['ef_provider']['sha256'],mvid=startup['ef_provider']['mvid'],source='isolated_provider_ProjectReference_build_manifest_verified'), 'runtime_provider')


def steps_contract(rows, spec):
    sequence(rows, 'steps'); require(len(rows) == len(STEPS), 'steps_complete')
    index = {}
    prior = 0
    for ordinal, (row, step_id) in enumerate(zip(rows, STEPS)):
        shape(row, 'step_id ordinal started_sequence completed_sequence status api result error', 'step')
        exact(row['step_id'], step_id, 'step_id'); exact(row['ordinal'], ordinal, 'step_ordinal')
        exact(row['status'], 'completed', 'step_completed'); exact(row['error'], None, 'step_error')
        start, end = integer(row['started_sequence']), integer(row['completed_sequence'])
        require(prior < start < end, 'step_order'); prior = end
        require(type(row['api']) is str and row['api'] != '', 'step_api')
        index[step_id] = row
    return index


def inside_step(sequence_id, step):
    require(integer(step['started_sequence']) < integer(sequence_id) < integer(step['completed_sequence']), 'stage_sequence_binding')


def observer_contract(value, startup):
    shape(value, 'collector command_collector collector_object_id command_collector_object_id constructed_sequence installed_sequence observation_started_sequence observation_completed_sequence registration capture_state', 'observer')
    identity(value['collector'], startup['host'], 'TP07.Runtime.TransactionEvidence', HOST)
    identity(value['command_collector'], startup['host'], 'TP07.Runtime.CommandEvidence', HOST)
    ids = [integer(value[k]) for k in ('collector_object_id','command_collector_object_id')]
    require(all(i > 0 for i in ids) and len(set(ids)) == 2, 'observer_object_ids')
    ordered = [integer(value[k], 'observer_sequence_integer') for k in ('constructed_sequence','installed_sequence','observation_started_sequence','observation_completed_sequence')]
    require(0 < ordered[0] < ordered[1] < ordered[2] < ordered[3], 'observer_interval')
    exact(value['capture_state'], 'observed', 'observer_known')
    registration = shape(value['registration'], 'transaction_count command_count same_transaction_instance same_command_instance source context_object_id inspected_sequence', 'observer_registration')
    for key, target in dict(transaction_count=1,command_count=1,same_transaction_instance=True,same_command_instance=True,source='IDbContextOptions_CoreOptionsExtension_Interceptors').items():
        exact(registration[key], target, 'observer_actual_registration_' + key)
    require(integer(registration['context_object_id']) > 0 and ordered[2] < integer(registration['inspected_sequence']) < ordered[3], 'observer_registration_readback')
    return ordered[2], ordered[3]


def transactions_contract(rows, spec, startup):
    require(type(rows) is list and len(rows) == 2, 'capability_two_actual_reads')
    previous = None
    for ordinal, row in enumerate(rows):
        shape(row, 'read_stage read_sequence capture_state ef underlying ef_object_id underlying_object_id connection_object_id context_object_id transaction_id current_same_instance underlying_connection_same ef_supports_savepoints underlying_supports_savepoints isolation_level', 'transaction')
        exact(row['capture_state'], 'observed', 'transaction_known')
        identity(row['ef'], startup['ef_provider'], 'W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransaction', 'W.EntityFrameworkCore.Dameng')
        w = startup['W']
        identity(row['underlying'], dict(path=w['path'],sha256=w['dll_sha256'],mvid=w['mvid']), 'W.Dm.DmTransaction', 'W.DamengProvider')
        ids = [integer(row[k]) for k in ('ef_object_id','underlying_object_id','connection_object_id','context_object_id')]
        require(all(i > 0 for i in ids) and len(set(ids)) == 4, 'transaction_object_ids')
        require(type(row['transaction_id']) is str and MVID.fullmatch(row['transaction_id']), 'transaction_id')
        for key in ('current_same_instance','underlying_connection_same'):
            exact(row[key], True, 'same_transaction_' + key)
        for key in ('ef_supports_savepoints','underlying_supports_savepoints'):
            exact(row[key], spec['supports_savepoints'], 'capability_' + key)
        exact(row['isolation_level'], 'ReadCommitted', 'transaction_isolation')
        exact(row['read_stage'], ('after_begin','before_write')[ordinal], 'capability_read_stage')
        integer(row['read_sequence'], 'capability_read_sequence')
        if previous is not None:
            require(previous['read_sequence'] < row['read_sequence'], 'capability_read_order')
            exact({k:v for k,v in row.items() if k not in ('read_stage','read_sequence')}, {k:v for k,v in previous.items() if k not in ('read_stage','read_sequence')}, 'capability_reads_same_objects')
        previous = row
    return rows[0]


CALLBACKS = ('TransactionStarted','TransactionUsed','CreatingSavepoint','CreatedSavepoint','ReleasingSavepoint','ReleasedSavepoint','RollingBackToSavepoint','RolledBackToSavepoint','TransactionRollingBack','TransactionRolledBack','TransactionCommitting','TransactionCommitted','TransactionFailed')
PRE_CALLBACKS = ('CreatingSavepoint','ReleasingSavepoint','RollingBackToSavepoint','TransactionRollingBack','TransactionCommitting')


def events_binding_contract(events,allow_used=False):
    bindings={}
    for row in events:
        callback=row['callback'].removesuffix('Async')
        current=row['current_connection_object_id']
        require(current is None or integer(current)>0,'actual_current_connection_id')
        require(integer(row['connection_object_id'])>0,'associated_connection_id')
        integer(row['binding_sequence'],'actual_binding_sequence')
        for field in ('transaction_id','connection_id'):
            require(type(row[field]) is str and MVID.fullmatch(row[field]),'binding_actual_GUID')
        for field in ('transaction_object_id','context_object_id'):
            require(integer(row[field])>0,'binding_actual_object')
        require(type(row['is_async']) is bool,'binding_actual_API')
        identity_tuple=[row['transaction_id'],row['context_object_id'],row['connection_object_id'],row['connection_id'],row['is_async']]
        if callback in ('TransactionStarted','TransactionUsed'):
            require(callback!='TransactionUsed' or allow_used,'no_external_UseTransaction_live')
            require(row['transaction_object_id'] not in bindings,'single_observed_transaction_binding')
            exact(row['binding_sequence'],row['sequence'],'callback_binding_real_sequence')
            exact(row['connection_binding_source'],'callback_connection_argument','begin_binding_actual_argument')
            exact(current,row['connection_object_id'],'begin_actual_current_connection')
            require(row['capture_state']=='observed' and row['completed'] is True and row['error'] is None,'observed_successful_binding')
            bindings[row['transaction_object_id']]=(identity_tuple,row['sequence'])
        else:
            require(row['transaction_object_id'] in bindings,'observed_prior_transaction_binding_required')
            actual,sequence_id=bindings[row['transaction_object_id']]
            exact(identity_tuple,actual,'same_actual_transaction_binding')
            exact(row['binding_sequence'],sequence_id,'same_actual_begin_binding_sequence')
            require(sequence_id<row['sequence'],'binding_precedes_callback')
            if current is None:
                require(callback in ('TransactionRolledBack','TransactionCommitted') and row['completed'] is True and row['error'] is None and row['capture_state']=='observed','null_connection_only_successful_terminal')
                exact(row['connection_binding_source'],'registered_transaction_binding','terminal_actual_null_bound_source')
            else:
                exact(current,row['connection_object_id'],'actual_current_same_connection')
                exact(row['connection_binding_source'],'current_transaction_connection','active_actual_connection_source')
    return bindings


def events_commands_contract(events, commands, observer, tx, steps, spec):
    begin, end = observer_contract_interval(observer)
    require(type(events) is list and type(commands) is list and 0 < len(events)+len(commands) <= 256, 'event_bound')
    all_sequences = []
    for row in events:
        shape(row, 'sequence stage source callback context_object_id transaction_object_id connection_object_id current_connection_object_id connection_binding_source binding_sequence transaction_id connection_id is_async completed error capture_state', 'event')
        require(row['stage'] in STEPS, 'event_stage')
        require(row['callback'] in CALLBACKS or row['callback'].endswith('Async') and row['callback'][:-5] in CALLBACKS, 'event_callback')
        suffix = row['callback'].endswith('Async'); callback = row['callback'][:-5] if suffix else row['callback']
        exact(row['is_async'], suffix, 'event_actual_API')
        exact(row['source'], 'DbTransactionInterceptor', 'event_source')
        exact(row['capture_state'], 'observed', 'event_known'); exact(row['error'], None, 'event_no_hidden_error')
        require(callback != 'TransactionFailed', 'transaction_failure_not_success')
        exact(row['completed'], callback not in PRE_CALLBACKS, 'event_completion_kind')
        require(begin < integer(row['sequence']) < end, 'event_observed_interval')
        inside_step(row['sequence'], steps[row['stage']])
        for key in ('context_object_id','transaction_object_id','connection_object_id'):
            require(integer(row[key]) > 0, 'event_object_id')
        for key in ('transaction_id','connection_id'):
            require(type(row[key]) is str and MVID.fullmatch(row[key]), 'event_guid')
        all_sequences.append(row['sequence'])
    require([r['sequence'] for r in events] == sorted(r['sequence'] for r in events), 'event_sequence_order')
    events_binding_contract(events)
    command_ids = set()
    for row in commands:
        shape(row, 'sequence completed_sequence stage source command_id connection_id context_object_id command_object_id transaction_object_id connection_object_id is_async api kind sql_sha256 parameters completed result error capture_state', 'command')
        require(row['stage'] in STEPS, 'command_stage')
        exact(row['source'], 'DbCommandInterceptor', 'command_source'); exact(row['capture_state'], 'observed', 'command_known')
        exact(row['completed'], True, 'command_success'); exact(row['error'], None, 'command_no_hidden_error')
        require(begin < integer(row['sequence']) < integer(row['completed_sequence']) < end, 'command_interval')
        inside_step(row['sequence'], steps[row['stage']]); inside_step(row['completed_sequence'], steps[row['stage']])
        require(type(row['command_id']) is str and MVID.fullmatch(row['command_id']) and row['command_id'] not in command_ids, 'command_unique_id'); command_ids.add(row['command_id'])
        require(type(row['connection_id']) is str and MVID.fullmatch(row['connection_id']), 'command_connection_id')
        for key in ('context_object_id','command_object_id','connection_object_id'):
            require(integer(row[key]) > 0, 'command_object_id')
        require(row['transaction_object_id'] is None or integer(row['transaction_object_id']) > 0, 'command_transaction_id')
        require(type(row['sql_sha256']) is str and HASH.fullmatch(row['sql_sha256']), 'command_SQL_hash')
        require(row['kind'] in ('reader','nonquery','scalar'), 'command_kind')
        api = {'reader':'ReaderExecuting','nonquery':'NonQueryExecuting','scalar':'ScalarExecuting'}[row['kind']]
        exact(row['api'], api+('Async' if row['is_async'] is True else ''), 'command_API')
        require(type(row['is_async']) is bool, 'command_async_bool')
        if row['kind'] == 'nonquery':
            require(integer(row['result']) >= 0 or (row['result'] == -1 and row['stage'] == 'create'), 'command_affected_rows')
        else:
            exact(row['result'], None, 'command_result_safe')
        sequence(row['parameters'], 'parameters')
        for param in row['parameters']:
            shape(param, 'type db_type value', 'parameter')
            exact(param['type'], 'W.Dm.DmParameter', 'parameter_route')
            require(param['db_type'] in ('DateTime2','DateTimeOffset','Int32','AnsiString','String'), 'parameter_db_type')
            v = param['value']
            require(type(v) is dict and 'type' in v, 'parameter_value')
            if v['type'] == 'System.DateTime':
                shape(v, 'type ticks kind', 'parameter_DT'); require(type(v['ticks']) is int and v['kind'] == 'Unspecified', 'parameter_DT_shape')
            elif v['type'] == 'System.DateTimeOffset':
                shape(v, 'type ticks offset_minutes utc_ticks', 'parameter_DTO')
                require(all(type(v[k]) is int for k in ('ticks','offset_minutes','utc_ticks')), 'parameter_DTO_integer')
            else:
                require(v['type'] in ('System.Int32','System.String'), 'parameter_finite_type'); shape(v, 'type', 'parameter_safe_value')
        all_sequences += [row['sequence'],row['completed_sequence']]
    require(len(all_sequences) == len(set(all_sequences)), 'observation_unique_sequence')
    require([r['sequence'] for r in commands] == sorted(r['sequence'] for r in commands), 'command_sequence_order')
    target = [row for row in events if row['transaction_id'] == tx['transaction_id']]
    require(target, 'explicit_transaction_callbacks')
    connection_ids = set()
    for row in target:
        exact(row['context_object_id'], tx['context_object_id'], 'event_same_context')
        exact(row['transaction_object_id'], tx['underlying_object_id'], 'event_same_underlying_transaction')
        exact(row['connection_object_id'], tx['connection_object_id'], 'event_same_connection')
        exact(row['is_async'], spec['api'] == 'async', 'event_case_API')
        connection_ids.add(row['connection_id'])
    require(len(connection_ids) == 1, 'event_same_connection_guid')
    explicit_ids = (tx['context_object_id'],tx['underlying_object_id'],tx['connection_object_id'])
    for row in events:
        # An explicit object must not be hidden under another GUID/stage.
        if row['transaction_object_id'] == tx['underlying_object_id']:
            exact(row['transaction_id'], tx['transaction_id'], 'event_no_seed_relabelling')
    business = {}
    for stage in ('tracking_query','transaction_SaveChanges','query_inside_transaction'):
        rows = [row for row in commands if row['stage'] == stage]
        require(len(rows) == 1, 'single_actual_command_' + stage)
        row = rows[0]; business[stage] = row
        exact([row[k] for k in ('context_object_id','transaction_object_id','connection_object_id')], list(explicit_ids), 'command_same_transaction')
        exact(row['connection_id'], next(iter(connection_ids)), 'command_connection_guid')
        exact(row['is_async'], spec['api'] == 'async', 'command_case_API')
        if stage != 'transaction_SaveChanges':
            exact(row['kind'], 'reader', 'actual_database_query')
    params = business['transaction_SaveChanges']['parameters']
    for offset in (False,True):
        selected = [p for p in params if p['value']['type'] == ('System.DateTimeOffset' if offset else 'System.DateTime')]
        require(len(selected) == 1, 'pair_write_parameters')
        exact(selected[0]['db_type'], 'DateTimeOffset' if offset else 'DateTime2', 'write_parameter_db_type')
        typed(selected[0]['value'], BASE_TICKS+spec['quantum'], offset)
    def select(callback, stage):
        suffix = 'Async' if spec['api'] == 'async' else ''
        found = [r for r in target if r['callback'] == callback+suffix]
        require(len(found) == 1 and found[0]['stage'] == stage, 'explicit_callback_' + callback)
        return found[0]['sequence']
    started = select('TransactionStarted','begin_transaction')
    rolling = select('TransactionRollingBack','full_Rollback')
    rolled = select('TransactionRolledBack','full_Rollback')
    require(tx['read_sequence'] < steps['tracking_query']['started_sequence'], 'after_begin_capability_before_tracking')
    require(integer(tx['read_sequence']) < business['transaction_SaveChanges']['sequence'], 'capability_before_business')
    require(started < business['tracking_query']['sequence'] < business['transaction_SaveChanges']['sequence'] < business['query_inside_transaction']['sequence'] < rolling < rolled, 'transaction_business_rollback_order')
    allowed = {'TransactionStarted','TransactionRollingBack','TransactionRolledBack'}
    if spec['supports_savepoints']:
        creating = select('CreatingSavepoint','transaction_SaveChanges'); created = select('CreatedSavepoint','transaction_SaveChanges')
        releasing = select('ReleasingSavepoint','transaction_SaveChanges'); released = select('ReleasedSavepoint','transaction_SaveChanges')
        require(creating < created < business['transaction_SaveChanges']['sequence'] < business['transaction_SaveChanges']['completed_sequence'] < releasing < released, 'automatic_savepoint_business_order')
        allowed.update(('CreatingSavepoint','CreatedSavepoint','ReleasingSavepoint','ReleasedSavepoint'))
    require(len(target) == len(allowed) and {r['callback'].removesuffix('Async') for r in target} == allowed, 'exact_explicit_callbacks')
    return True


def observer_contract_interval(observer):
    return observer['observation_started_sequence'], observer['observation_completed_sequence']


def row_value(value, target, n):
    shape(value, 'id d z n', 'row')
    exact(value['id'], 1, 'row_ID'); exact(value['n'], n, 'row_N')
    typed(value['d'], target, False); typed(value['z'], target, True)


def catalog_contract(value, spec, entry):
    shape(value, 'columns primary_key comment table', 'catalog')
    exact(value['primary_key'], entry['primary_key'], 'catalog_PK')
    exact(value['comment'], 'TP07_'+entry['nonce'], 'catalog_owned_comment')
    exact(value['table'], entry['table'], 'catalog_table')
    columns = value['columns']
    require(type(columns) is list and len(columns) == 4 and [c.get('name') for c in columns] == ['ID','D','Z','N'], 'catalog_exact_columns')
    for row in columns:
        shape(row, 'name data_type precision scale nullable data_length', 'catalog_column')
        exact(row['nullable'], 'N', 'catalog_non_null')
        require(type(row['data_type']) is str, 'catalog_type')
        for key in ('precision','scale','data_length'):
            require(row[key] is None or type(row[key]) is int, 'catalog_integer')
        typ = row['data_type'].upper()
        if row['name'] == 'ID':
            require(typ in ('INT','INTEGER'), 'catalog_ID_type')
        elif row['name'] == 'D':
            require(typ in ('TIMESTAMP','DATETIME'), 'catalog_D_type')
            exact(row['scale'], spec['scale'], 'catalog_D_scale')
            exact(row['data_length'], 9 if spec['scale'] == 7 else 8, 'catalog_D_width')
        elif row['name'] == 'Z':
            require(typ in ('TIMESTAMP WITH TIME ZONE','DATETIME WITH TIME ZONE'), 'catalog_Z_type')
            exact(row['scale'], spec['scale'], 'catalog_Z_scale')
            exact(row['data_length'], 11 if spec['scale'] == 7 else 10, 'catalog_Z_width')
        else:
            require(typ in ('VARCHAR','VARCHAR2','VARCHAR(32)'), 'catalog_N_type')
            exact(row['data_length'], 32, 'catalog_N_width')


def values_contract(value, spec, entry, startup, tx, commands):
    shape(value, 'before inside after', 'values')
    row_value(value['inside'], BASE_TICKS+spec['quantum'], 'rollback')
    ids = []
    for when in ('before','after'):
        fresh = shape(value[when], 'row row_count context_object_id connection_object_id different_connection identity schema d_oracle z_oracle auxiliary_io', 'fresh')
        row_value(fresh['row'], BASE_TICKS, 'first'); exact(fresh['row_count'], 1, 'fresh_total_rows')
        context, connection = integer(fresh['context_object_id']), integer(fresh['connection_object_id'])
        require(context > 0 and connection > 0 and context != connection and context != tx['context_object_id'] and connection != tx['connection_object_id'], 'fresh_new_context_connection')
        exact(fresh['different_connection'], True if when == 'after' else None, 'fresh_different_actual_connection')
        physical(fresh['identity'], spec['profile']); catalog_contract(fresh['schema'], spec, entry)
        oracle(fresh['d_oracle'], spec, False, startup['W']); oracle(fresh['z_oracle'], spec, True, startup['W'])
        exact(fresh['auxiliary_io'], 'catalog_raw_oracle_cleanup_synchronous', 'fresh_auxiliary_API')
        actual_commands = [c for c in commands if c['stage'] == 'fresh_'+when]
        require(len(actual_commands) == 2, 'fresh_database_query_and_total_count')
        for c in actual_commands:
            exact(c['context_object_id'], context, 'fresh_query_context'); exact(c['connection_object_id'], connection, 'fresh_query_connection')
            exact(c['transaction_object_id'], None, 'fresh_no_old_transaction')
            exact(c['is_async'], spec['api'] == 'async', 'fresh_query_API')
            exact(c['kind'], 'reader', 'fresh_query_reader')
        ids += [context,connection]
    require(len(set(ids)) == 4, 'fresh_before_after_distinct')
    exact(value['before']['schema'], value['after']['schema'], 'catalog_unchanged')


def case_contract(report, cleanup, ledger, startup, startup_sha, ledger_sha, case_id):
    require(case_id in CASE_IDS, 'fixed_case_id')
    spec = definition(CASE_IDS.index(case_id))
    document(report, 'case'); document(cleanup, 'cleanup')
    exact(report['case_id'], case_id, 'report_case')
    exact(report['profile'], spec['profile'], 'report_profile'); exact(report['api'], spec['api'], 'report_API')
    exact(report['definition'], {k:spec[k] for k in ('scale','ticks','offset_minutes','quantum')}, 'report_definition')
    exact(report['status'], 'completed', 'main_completed'); exact(report['process_exit_code'], 0, 'main_exit')
    exact(report['error'], None, 'main_no_error')
    runtime_identity(report['runtime_identity'], startup, startup_sha)
    shape(report['services'], 'factory before_ddl_factory before_ddl_context_object_id before_ddl_resolved_sequence di_origin auto_savepoints_enabled', 'services')
    identity(report['services']['before_ddl_factory'], startup['ef_provider'], 'W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory', 'W.EntityFrameworkCore.Dameng')
    require(integer(report['services']['before_ddl_context_object_id']) > 0, 'before_DDL_context')
    identity(report['services']['factory'], startup['ef_provider'], 'W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory', 'W.EntityFrameworkCore.Dameng')
    exact(report['services']['di_origin'], 'context_GetService_IRelationalTransactionFactory', 'factory_formal_DI')
    exact(report['services']['auto_savepoints_enabled'], True, 'AutoSavepointsEnabled')
    observer_contract(report['observer'], startup)
    steps = steps_contract(report['steps'], spec)
    physical(steps['runtime_identity']['result'], spec['profile'])
    require(steps['runtime_identity']['completed_sequence'] < integer(report['services']['before_ddl_resolved_sequence']) < steps['create']['started_sequence'], 'formal_DI_before_DDL')
    tx = transactions_contract(report['transactions'], spec, startup)
    inside_step(report['transactions'][0]['read_sequence'], steps['capabilities'])
    inside_step(report['transactions'][1]['read_sequence'], steps['transaction_SaveChanges'])
    exact(report['observer']['registration']['context_object_id'], tx['context_object_id'], 'observer_explicit_context_registration')
    require(steps['fresh_before']['completed_sequence'] < report['observer']['registration']['inspected_sequence'] < steps['begin_transaction']['started_sequence'], 'observer_explicit_registration_before_begin')
    begin = steps['begin_transaction']['result']
    exact(begin, dict(transaction_id=tx['transaction_id'],ef_object_id=tx['ef_object_id'],underlying_object_id=tx['underlying_object_id']), 'begin_actual_return')
    exact(steps['capabilities']['result'], True, 'capabilities_step_completed')
    exact(steps['full_Rollback']['result'], dict(returned=True,ef_object_id=tx['ef_object_id'],transaction_id=tx['transaction_id']), 'full_Rollback_actual_return')
    exact(steps['dispose_transaction_context']['result'], True, 'explicit_disposal_completed')
    for step in ('seed_SaveChanges','transaction_SaveChanges'):
        exact(steps[step]['result'], 1, 'SaveChanges_count')
    expected_apis = {'runtime_identity':'synchronous_auxiliary','create':'ExecuteSqlRaw','seed_SaveChanges':'SaveChanges','fresh_before':'Single_Count','begin_transaction':'BeginTransaction_ReadCommitted','capabilities':'actual_getters','tracking_query':'Single','transaction_SaveChanges':'SaveChanges','query_inside_transaction':'AsNoTracking_Single','full_Rollback':'Rollback','dispose_transaction_context':'DisposeAsync','fresh_after':'Single_Count'}
    if spec['api'] == 'async':
        expected_apis.update(create='ExecuteSqlRawAsync',seed_SaveChanges='SaveChangesAsync',fresh_before='SingleAsync_CountAsync',begin_transaction='BeginTransactionAsync_ReadCommitted',tracking_query='SingleAsync',transaction_SaveChanges='SaveChangesAsync',query_inside_transaction='AsNoTracking_SingleAsync',full_Rollback='RollbackAsync',fresh_after='SingleAsync_CountAsync')
    for name, api in expected_apis.items():
        exact(steps[name]['api'], api, 'step_API_' + name)
    events_commands_contract(report['events'], report['commands'], report['observer'], tx, steps, spec)
    entry = ledger_contract(ledger, spec, startup['W'])
    for stage in ('create','seed_SaveChanges'):
        actual = [c for c in report['commands'] if c['stage'] == stage]
        require(len(actual) == 1, 'actual_create_seed_command_' + stage)
        exact(actual[0]['context_object_id'], report['services']['before_ddl_context_object_id'], 'pre_DDL_formal_context')
        exact(actual[0]['is_async'], spec['api'] == 'async', 'create_seed_actual_API')
        if stage == 'create':
            exact(actual[0]['sql_sha256'], entry['create_evidence']['sql_sha256'], 'CREATE_server_ACK_command_binding')
            exact(actual[0]['kind'], 'nonquery', 'CREATE_actual_command')
        else:
            for offset in (False,True):
                params = [p for p in actual[0]['parameters'] if p['value']['type'] == ('System.DateTimeOffset' if offset else 'System.DateTime')]
                require(len(params) == 1, 'seed_pair_actual_parameters')
                typed(params[0]['value'], BASE_TICKS, offset)
    exact(report['ownership'], entry, 'report_exact_owned_entry')
    catalog_contract(steps['create']['result'], spec, entry)
    values_contract(report['values'], spec, entry, startup, tx, report['commands'])
    exact(steps['fresh_before']['result'], report['values']['before'], 'before_actual_step_result')
    exact(steps['fresh_after']['result'], report['values']['after'], 'after_actual_step_result')
    exact(steps['query_inside_transaction']['result'], report['values']['inside'], 'inside_actual_step_result')
    row_value(steps['tracking_query']['result'], BASE_TICKS, 'first')
    cleanup_contract(report['main_finally'], entry, ledger_sha, spec)
    exact(cleanup['status'], 'completed', 'independent_cleanup_completed'); exact(cleanup['process_exit_code'], 0, 'independent_cleanup_exit')
    exact(cleanup['error'], None, 'independent_cleanup_no_error')
    runtime_identity(cleanup['runtime_identity'], startup, startup_sha)
    exact(cleanup['objects'], ledger['objects'], 'independent_exact_ledger_objects')
    cleanup_contract(cleanup['cleanup'], entry, ledger_sha, spec)
    cleanup_contract(cleanup['finally_cleanup'], entry, ledger_sha, spec)
    return {'fixed_integer_pair':True,'runtime_source_binary':True,'formal_DI':True,'observer_installed':True,'same_transaction_callbacks':True,'shared_savepoints_or_TLS_zero':True,'explicit_full_rollback':True,'fresh_original_raw_typed_FF9':True,'exact_ledger_cleanup':True}


TOOL_PREFIX = 'tools/DownstreamAcceptance/TP07/transaction-capability-probe/'
TOOL_FILES = ('README.md','probe-policy.json','prepare.py','run.py','validate.py','test_contracts.py','managed_identity.py','packed_oracle.py','host/TP07TransactionCapabilityProbe.csproj','host/Program.cs','host/ProbeContext.cs','host/TransactionEvidence.cs','host/ProbeIdentity.cs','host/OfflineContracts.cs','host/common/OwnedObjects.cs','host/common/SafeEvidence.cs','host/common/LiteralOracle.cs')
HELPERS = {
 'host/common/OwnedObjects.cs':dict(path='tools/DownstreamAcceptance/TP07/runtime-templates/common/OwnedObjects.cs',sha256='80c1d2aa0b6aae04a89d168625f37ca013eb7d7e19fe0482049312db175e5946'),
 'host/common/SafeEvidence.cs':dict(path='tools/DownstreamAcceptance/TP07/runtime-templates/common/SafeEvidence.cs',sha256='44c5e9b5643a3d33ba348ed8acc51780ccad2f94a5b43cc819f5897c9f9c11ef'),
 'host/common/LiteralOracle.cs':dict(path='tools/DownstreamAcceptance/TP07/runtime-templates/TP07LiteralProbe/LiteralOracle.cs',sha256='cb1696d1b305471e12c1a953630ee66454c4ab92d505d650e291e7170c44f492'),
 'managed_identity.py':dict(path='tools/DownstreamAcceptance/TP07/managed_identity.py',sha256='ca7bb8eba198cf431d2fcfe5cbd4843360ddc80ad71937b1070099e6edd888ac'),
 'packed_oracle.py':dict(path='tools/TemporalBoundaryProbe/packed_oracle.py',sha256=PACKED_SHA),
}
PROJECTS = ('src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj','test/TP07TransactionCapabilityProbe/TP07TransactionCapabilityProbe.csproj')
CORE_PACKAGES = ('Microsoft.EntityFrameworkCore','Microsoft.EntityFrameworkCore.Abstractions','Microsoft.EntityFrameworkCore.Analyzers','Microsoft.EntityFrameworkCore.Relational','Microsoft.Extensions.Caching.Abstractions','Microsoft.Extensions.Caching.Memory','Microsoft.Extensions.Configuration.Abstractions','Microsoft.Extensions.DependencyInjection','Microsoft.Extensions.DependencyInjection.Abstractions','Microsoft.Extensions.Logging','Microsoft.Extensions.Logging.Abstractions','Microsoft.Extensions.Options','Microsoft.Extensions.Primitives')


def relative_path(name, generated=False):
    require(type(name) is str and name and not Path(name).is_absolute() and '\\' not in name and all(x and x not in ('.','..','.local','secrets') for x in name.split('/')), 'relative_path')
    require(generated or all(x not in ('bin','obj') for x in name.split('/')), 'source_generated_path')
    return name


def hash_map(value, root, expected_paths=None, generated=False):
    require(type(value) is dict and value, 'hash_map')
    if expected_paths is not None:
        exact(sorted(value), sorted(expected_paths), 'exact_paths_hash_map')
    for name, digest in value.items():
        relative_path(name, generated)
        ref(dict(path=str(Path(root)/name),sha256=digest), root)
    return value


def closure_contract(value):
    shape(value, 'product_paths product_count host_paths source_paths source_count locked_paths locked_count feed feed_inventory nuget_config native_graphs', 'approved_closure')
    for key,count in (('product',178),('source',187),('locked',188)):
        exact(value[key+'_count'], count, 'finite_'+key+'_count')
        paths = value[key+'_paths']
        require(type(paths) is list and len(paths) == count and paths == sorted(paths) and len(set(paths)) == count, 'approved_'+key+'_paths')
        for path in paths:
            relative_path(path)
    expected_host = sorted('test/TP07TransactionCapabilityProbe/'+p.removeprefix('host/') for p in TOOL_FILES if p.startswith('host/'))
    exact(value['host_paths'], expected_host, 'finite_nine_host_paths')
    exact(value['source_paths'], sorted(value['product_paths']+expected_host), 'finite_source_paths')
    exact(value['locked_paths'], sorted(value['source_paths']+['test/TP07TransactionCapabilityProbe/packages.lock.json']), 'finite_locked_paths')
    require(type(value['native_graphs']) is dict and set(value['native_graphs']) == set(PROJECTS), 'finite_project_graphs')
    for graph in value['native_graphs'].values():
        shape(graph, 'lock_dependencies assets_frameworks', 'approved_native_graph')
    return value


def process_contract(value, argv, cwd, budget=180):
    shape(value, 'argv cwd exit_code elapsed_ms timed_out termination execution_deadline termination_reap_budget termination_reap_ms signal_number', 'process')
    exact(value['execution_deadline'], 180, 'process_actual_execution_deadline')
    exact(value['termination_reap_budget'], 2, 'process_actual_reap_budget')
    exact(value['termination_reap_ms'], 0, 'process_natural_no_reap')
    exact(value['signal_number'], None, 'process_no_signal')
    exact(value['argv'], argv, 'process_argv'); exact(value['cwd'], str(cwd), 'process_cwd')
    exact(value['exit_code'], 0, 'process_natural_exit')
    require(0 <= integer(value['elapsed_ms'], 'process_elapsed_integer') <= budget*1000, 'process_budget')
    exact(value['timed_out'], False, 'process_no_timeout'); exact(value['termination'], None, 'process_no_termination')


def managed_reader():
    path = HERE/'managed_identity.py';raw=path.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == HELPERS['managed_identity.py']['sha256'], 'managed_reader_pin')
    module=types.ModuleType('tp07_t1_managed_identity');exec(compile(raw,str(path),'exec'),module.__dict__)
    return module.mvid


def binary_contract(value, root, name, deps=False):
    shape(value, 'name path sha256 mvid'+(' deps' if deps else ''), 'binary')
    exact(value['name'], name, 'binary_name')
    path=ref(dict(path=value['path'],sha256=value['sha256']),root)
    try:
        actual=managed_reader()(path.read_bytes())
    except (ValueError,IndexError,KeyError) as error:
        raise ContractError('managed_binary_identity') from error
    exact(value['mvid'], actual, 'binary_MVID')
    if deps:
        ref(value['deps'],root)
        exact(value['deps']['path'],str(path.with_suffix('.deps.json')), 'host_deps_path')
    return path


def policy_contract(policy):
    """Pure independent finite policy contract; no producer or file imports."""
    shape(policy, 'schema_version task host_name sdk_version ef_version product_count_pending expected_product_count expected_replay_count expected_locked_count source_files helper_origins budgets max_events cases step_ids auxiliary_io core_packages required_input_keys probe_source_manifest native_hash_domains termination_reap_budget_seconds process_receipt_keys termination_reasons termination_unknown_suffix transaction_event_binding_fields connection_binding_sources binding_callbacks offline_registry','policy')
    for key,target in dict(schema_version=1,task='TP07-T1-policy',host_name=HOST,sdk_version='10.0.401',ef_version='10.0.12',expected_product_count=178,expected_replay_count=187,expected_locked_count=188,source_files=list(TOOL_FILES),helper_origins=HELPERS,budgets=dict(main=180,cleanup=180,validator=180),max_events=256,cases=[definition(i) for i in range(4)],step_ids=list(STEPS),auxiliary_io='catalog_raw_oracle_cleanup_synchronous',core_packages=list(CORE_PACKAGES)).items():
        exact(policy[key],target,'independent_policy_'+key)
    require(type(policy['product_count_pending']) is bool, 'policy_pending_bool')
    additions = dict(
        required_input_keys=['schema_version','task','workspace_root','original_repo','sdk','python','W_identity','capabilities','ef_overlay','toolpack','helper_origins','expected_closure','probe_source_manifest'],
        probe_source_manifest=dict(
            task='TP07-T1-probe-source-manifest',
            required_keys=['schema_version','task','source_root','source_hashes','source_count','source_aggregate_sha256','aggregate_format'],
            source_count=9,
            source_paths=['host/OfflineContracts.cs','host/ProbeContext.cs','host/ProbeIdentity.cs','host/Program.cs','host/TP07TransactionCapabilityProbe.csproj','host/TransactionEvidence.cs','host/common/LiteralOracle.cs','host/common/OwnedObjects.cs','host/common/SafeEvidence.cs'],
            aggregate_format='sorted relative source paths: sha256 two spaces path newline',
            source_root_scope='workspace_verification_snapshot'),
        native_hash_domains=dict(archive_sidecar='base64_SHA512_of_actual_full_ZIP_bytes',nuget_content_hash='actual_native_metadata_and_native_lock_equal_root_approved_origin_lock_record'),
        termination_reap_budget_seconds=2,
        process_receipt_keys=['argv','cwd','exit_code','elapsed_ms','timed_out','termination','execution_deadline','termination_reap_budget','termination_reap_ms','signal_number'],
        termination_reasons=['deadline_kill','signal_kill','interrupt_kill','exception_kill','launch_failed'],
        termination_unknown_suffix='_reap_unknown',
        transaction_event_binding_fields=['current_connection_object_id','connection_binding_source','binding_sequence'],
        connection_binding_sources=['callback_connection_argument','current_transaction_connection','registered_transaction_binding'],
        binding_callbacks=['TransactionStarted','TransactionStartedAsync','TransactionUsed','TransactionUsedAsync'],
        offline_registry=dict(rollback_cases=16,factory_cases=4,commit_cases=8,ordered_blocks=['rollback','factory_Create','commit'],case_count=28,terminals=['rollback','commit'],counters_include=['full_rollbacks','full_commits'],live_commit_allowed=False))
    for key,target in additions.items():
        exact(policy[key],target,'independent_policy_'+key)
    return policy


def toolpack_contract(reference, root):
    pack=read_ref(reference,root)
    shape(pack,'schema_version task source_hashes','toolpack')
    exact(pack['schema_version'],1,'toolpack_version');exact(pack['task'],'TP07-T1-toolpack','toolpack_task')
    hash_map(pack['source_hashes'],root,[TOOL_PREFIX+p for p in TOOL_FILES])
    require(HERE == Path(root)/TOOL_PREFIX.rstrip('/'), 'actual_new_toolpack_location')
    policy=load(Path(root)/TOOL_PREFIX/'probe-policy.json')
    policy_contract(policy)
    for dst, origin in HELPERS.items():
        exact(sha(Path(root)/origin['path']), origin['sha256'], 'helper_original_pin')
        exact(sha(Path(root)/TOOL_PREFIX/dst), origin['sha256'], 'helper_copy_pin')
    return pack


def probe_source_contract(reference,root,toolpack,closure,lineage_sha=None):
    if lineage_sha is not None:
        exact(reference['sha256'],lineage_sha,'probe_source_manifest_lineage')
    value=read_ref(reference,root)
    shape(value,'schema_version task source_root source_hashes source_count source_aggregate_sha256 aggregate_format','probe_source_manifest')
    exact(value['schema_version'],1,'probe_source_version');exact(value['task'],'TP07-T1-probe-source-manifest','probe_source_task')
    exact(value['source_count'],9,'probe_source_count')
    exact(value['aggregate_format'],'sorted relative source paths: sha256 two spaces path newline','probe_source_aggregate_format')
    snapshot=absolute_path(value['source_root'],root,False)
    require(Path(root)/'.local/temporal-native/verification' in snapshot.parents,'probe_source_controlled_snapshot')
    names=[name for name in TOOL_FILES if name.startswith('host/')]
    hash_map(value['source_hashes'],snapshot,names)
    actual=[]
    for path in snapshot.rglob('*'):
        require(not path.is_symlink(),'probe_source_symlink')
        if path.is_file():actual.append(str(path.relative_to(snapshot)))
    exact(sorted(actual),sorted(names),'probe_source_exact_nine_files')
    for name,digest in value['source_hashes'].items():
        exact(toolpack['source_hashes'].get(TOOL_PREFIX+name),digest,'probe_source_toolpack_binding')
    exact(sorted('test/TP07TransactionCapabilityProbe/'+name.removeprefix('host/') for name in value['source_hashes']),closure['host_paths'],'probe_source_expected_paths')
    aggregate=''.join(value['source_hashes'][name]+'  '+name+'\n' for name in sorted(value['source_hashes']))
    exact(value['source_aggregate_sha256'],hashlib.sha256(aggregate.encode('utf-8')).hexdigest(),'probe_source_aggregate')
    return value


def inputs_contract(reference):
    inputs=document(read_ref(reference),'inputs')
    root=absolute_path(inputs['workspace_root'],exists=False)
    absolute_path(inputs['original_repo'],exists=False)
    closure_contract(inputs['expected_closure'])
    toolpack_contract(inputs['toolpack'],root)
    exact(inputs['helper_origins'], HELPERS, 'inputs_helpers')
    probe_source_contract(inputs['probe_source_manifest'],root,read_ref(inputs['toolpack'],root),inputs['expected_closure'],inputs['ef_overlay']['lineage']['probe_source_manifest_sha256'])
    for field in ('sdk','python'):
        shape(inputs[field],'path sha256 version','executable')
        ref({k:inputs[field][k] for k in ('path','sha256')})
        require(type(inputs[field]['version']) is str, 'executable_version')
    exact(inputs['sdk']['version'],'10.0.401','SDK_version')
    ref(inputs['W_identity']);ref(inputs['capabilities'])
    overlay=shape(inputs['ef_overlay'],'source_root manifest lineage root_approval','overlay')
    absolute_path(overlay['source_root'],root,False);ref(overlay['manifest'],root);ref(overlay['root_approval'],root)
    return inputs


def approval_contract(reference):
    approval=document(read_ref(reference),'runtime-approval')
    exact(approval['status'],'runtime_authorized','root_runtime_authorization')
    exact(approval['ordered_cases'],list(CASE_IDS),'fixed_ordered_cases')
    exact(approval['budgets'],dict(main=180,cleanup=180,validator=180),'independent_budgets')
    inputs=inputs_contract(approval['inputs']);root=Path(inputs['workspace_root'])
    exact(approval['original_repo'],inputs['original_repo'],'original_repo_scope')
    output=absolute_path(approval['output_root'],root,False)
    require(Path(root/'.local/temporal-native/verification') in output.parents,'runtime_output_boundary')
    exact(approval['toolpack'],inputs['toolpack'],'approval_toolpack')
    shape(approval['wrappers'],'shared tls','wrappers')
    for profile,wrapper in approval['wrappers'].items():
        ref(wrapper)
        exact(wrapper['path'],str(Path(inputs['original_repo'])/'scripts'/('with-dameng-test.sh' if profile=='shared' else 'with-dameng-tls-test.sh')),'fixed_test_wrapper')
    source=document(read_ref(approval['source'],root),'source')
    native=document(read_ref(approval['native'],root),'native')
    build=document(read_ref(approval['build'],root),'build')
    startup=document(read_ref(approval['startup'],root),'startup')
    for doc in (source,native,build):
        exact(doc['inputs'],approval['inputs'],'same_inputs')
    exact(source['toolpack'],inputs['toolpack'],'source_toolpack')
    exact(source['W'],read_ref(inputs['W_identity'])['W'],'source_original_W')
    closure=inputs['expected_closure'];overlay=inputs['ef_overlay'];replay=absolute_path(source['replay_root'],root,False)
    exact(source['exact_paths'],closure['source_paths'],'approved_exact_source_paths');exact(source['exact_count'],closure['source_count'],'approved_exact_source_count')
    hash_map(source['source_hashes'],replay,closure['source_paths'])
    actual_source=[]
    for path in replay.rglob('*'):
        require(not path.is_symlink(),'replay_symlink_entry')
        relative=path.relative_to(replay)
        if path.is_file() and not any(part in ('bin','obj') for part in relative.parts):actual_source.append(str(relative))
    exact(sorted(actual_source),closure['locked_paths'],'no_unlisted_replay_sources')
    exact(source['role'],'producer_source_pending_independent_acceptance','source_role')
    host_paths=closure['host_paths']
    for path in host_paths:
        name=path.removeprefix('test/TP07TransactionCapabilityProbe/')
        exact(source['source_hashes'][path],sha(HERE/'host'/name),'replayed_exact_new_host_source')
    for doc in (native,build,startup):
        exact(doc['source'],approval['source'],'same_source')
    exact(native['source_before'],source['source_hashes'],'native_before');exact(native['source_after'],source['source_hashes'],'native_after')
    exact(build['source_before'],source['source_hashes'],'build_before');exact(build['source_after'],source['source_hashes'],'build_after')
    exact(build['native'],approval['native'],'build_native');exact(build['startup'],approval['startup'],'build_startup')
    exact(startup['native'],approval['native'],'startup_native');exact(startup['replay_root'],str(replay),'startup_replay')
    exact(startup['source_hashes'],source['source_hashes'],'startup_source')
    for doc in (source,startup):
        exact(doc['lineage'],overlay['lineage'],'new_overlay_lineage')
    lineage=shape(startup['lineage'],'ef_commit base_archive_sha256 bridge_patch_sha256 mapping_manifest_sha256 mapping_patch_sha256 approved_ef_source_manifest_sha256 transaction_patch_sha256 parent_overlay_sha256 probe_source_manifest_sha256 toolpack_manifest_sha256','lineage')
    exact(lineage['ef_commit'],'5863d3755dbd2d333ac359bf1c0a789762adbaed','EF_commit')
    exact(lineage['base_archive_sha256'],'668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8','EF_base_archive')
    require(all(type(v) is str and HASH.fullmatch(v) for k,v in lineage.items() if k!='ef_commit'),'lineage_hashes')
    exact(lineage['approved_ef_source_manifest_sha256'],overlay['manifest']['sha256'],'new_manifest_lineage')
    exact(lineage['toolpack_manifest_sha256'],inputs['toolpack']['sha256'],'toolpack_lineage')
    exact(lineage['probe_source_manifest_sha256'],inputs['probe_source_manifest']['sha256'],'probe_source_manifest_lineage')
    probe_source=probe_source_contract(inputs['probe_source_manifest'],root,read_ref(inputs['toolpack'],root),closure,lineage['probe_source_manifest_sha256'])
    for name,digest in probe_source['source_hashes'].items():
        replay_name='test/TP07TransactionCapabilityProbe/'+name.removeprefix('host/')
        exact(source['source_hashes'][replay_name],digest,'replayed_frozen_probe_source')
    # The independently delivered product manifest and future root approval
    # have finite contracts; old175/count>= cannot satisfy either gate.
    manifest=read_ref(overlay['manifest'],root)
    product_manifest_contract(manifest,inputs,startup['lineage'])
    hash_map(manifest['source_hashes'],overlay['source_root'],closure['product_paths'])
    exact({p:source['source_hashes'][p] for p in closure['product_paths']},manifest['source_hashes'],'replayed_approved_product')
    source_approval=read_ref(overlay['root_approval'],root)
    product_source_approval_contract(source_approval,inputs,manifest)
    exact(overlay['root_approval'],dict(path=str(root/SOURCE_APPROVAL_V3_PATH),sha256=SOURCE_APPROVAL_V3_SHA),'source_approval_fixed_v3_ref')
    native_contract(native,build,startup,inputs,replay,approval['native'],approval['build'])
    offline_contract(approval,inputs,build,startup,root)
    return approval,inputs,startup


def native_hash_domains_contract(full_zip_sha512,sidecar,metadata_content_hash,sdk_lock_content_hash,origin_content_hash):
    for value in (full_zip_sha512,sidecar,metadata_content_hash,sdk_lock_content_hash,origin_content_hash):
        require(type(value) is str and re.fullmatch(r'[A-Za-z0-9+/]{86}==',value),'native_SHA512_base64_shape')
    exact(sidecar,full_zip_sha512,'fullZIP_native_sidecar_hash')
    exact(metadata_content_hash,sdk_lock_content_hash,'native_metadata_actual_SDK_contentHash')
    exact(sdk_lock_content_hash,origin_content_hash,'native_SDK_root_origin_contentHash')


def runtime_package_contract(libraries):
    require(type(libraries) is dict,'runtime_libraries')
    packages={name.split('/')[0] for name,row in libraries.items() if row.get('type')=='package'}
    expected=(set(CORE_PACKAGES)-{'Microsoft.EntityFrameworkCore.Analyzers'})|{'W.DamengProvider'}
    require(packages==expected,'runtime_exact_Core12_W_no_build_only_Analyzer')


def offline_savepoint_contract(supports,fail,counters,events,commands,async_api):
    for key,target in dict(savepoint_create=int(supports),savepoint_release=int(supports),savepoint_rollback=int(supports and fail)).items():
        exact(counters[key],target,'offline_true_finally_'+key)
    suffix='Async' if async_api else ''
    expected={'CreatingSavepoint':int(supports),'CreatedSavepoint':int(supports),'ReleasingSavepoint':int(supports),'ReleasedSavepoint':int(supports),'RollingBackToSavepoint':int(supports and fail),'RolledBackToSavepoint':int(supports and fail)}
    positions={}
    for callback,count in expected.items():
        rows=[e for e in events if e['callback']==callback+suffix]
        require(len(rows)==count,'offline_actual_savepoint_'+callback)
        if count:positions[callback]=integer(rows[0]['sequence'])
    if supports:
        business=[c for c in commands if c['stage']=='transaction_SaveChanges']
        require(business,'offline_savepoint_business_command')
        first=min(integer(c['sequence']) for c in business);last=max(integer(c['completed_sequence']) for c in business)
        require(positions['CreatingSavepoint']<positions['CreatedSavepoint']<first<=last<positions['ReleasingSavepoint']<positions['ReleasedSavepoint'],'offline_savepoint_batch_finally_release_order')
        if fail:
            require(last<positions['RollingBackToSavepoint']<positions['RolledBackToSavepoint']<positions['ReleasingSavepoint'],'offline_failure_rollback_to_before_finally_release')


def archive_origin_contract(archive,directory,closure,name,version):
    import zipfile
    import xml.etree.ElementTree as ET
    feed=absolute_path(closure['feed'],exists=False)
    candidates=[entry for entry in closure['feed_inventory'] if Path(entry).name.casefold()==archive.name.casefold()]
    require(len(candidates)==1,'exact_feed_archive_origin')
    entry=candidates[0];relative_path(entry)
    digest=closure['feed_inventory'][entry]
    ref(dict(path=str(feed/entry),sha256=digest),feed)
    exact(sha(archive),digest,'native_cache_feed_ZIP_SHA256')
    try:
        with zipfile.ZipFile(archive) as package:
            names=[p for p in package.namelist() if p.lower().endswith('.nuspec')]
            require(len(names)==1,'single_ZIP_nuspec')
            raw=package.read(names[0]);root=ET.fromstring(raw)
    except (zipfile.BadZipFile,ET.ParseError) as error:
        raise ContractError('native_archive_nuspec') from error
    local=lambda tag:tag.rsplit('}',1)[-1]
    metadata=[node for node in root if local(node.tag)=='metadata']
    require(len(metadata)==1,'ZIP_nuspec_metadata')
    selected=[node for node in metadata[0] if local(node.tag) in ('id','version')]
    require(len(selected)==2,'ZIP_nuspec_id_version_unique')
    exact({local(node.tag):node.text for node in selected},dict(id=name,version=version),'ZIP_actual_id_version')
    installed=absolute_path(str(directory/(name.lower()+'.nuspec')),directory)
    require(installed.read_bytes()==raw,'installed_nuspec_archive_bytes')


def raw_project_paths(project):
    require(type(project) is str and project in PROJECTS,'raw_exact_project')
    base=Path(project).parent
    return dict(lock=str(base/'packages.lock.json'),assets=str(base/'obj/project.assets.json'),dgspec=str(base/'obj'/(Path(project).name+'.nuget.dgspec.json')))


def raw_snapshot_contract(stage_ref, graphs, replay, workspace, phase):
    require(phase in ('native','locked'),'raw_snapshot_phase')
    stage=ref(stage_ref,workspace)
    exact(stage.name,'native.json' if phase=='native' else 'build.json','raw_snapshot_stage_name')
    root=absolute_path(str(stage.parent/('raw-'+phase)),workspace,False)
    require(root.is_dir(),'raw_snapshot_root_directory')
    paths=sorted(raw_project_paths(project)[kind] for project in PROJECTS for kind in ('assets','dgspec'))
    directories={str(parent) for relative in paths for parent in Path(relative).parents if str(parent)!='.'}
    actual=[];actual_directories=[]
    for entry in root.rglob('*'):
        require(not entry.is_symlink(),'raw_snapshot_entry_symlink')
        if entry.is_file():actual.append(str(entry.relative_to(root)))
        elif entry.is_dir():actual_directories.append(str(entry.relative_to(root)))
        else:require(False,'raw_snapshot_special_entry')
    exact(sorted(actual),paths,'raw_snapshot_four_exact_files')
    exact(sorted(actual_directories),sorted(directories),'raw_snapshot_exact_parent_directories')
    sequence(graphs,'raw_snapshot_graphs');exact(len(graphs),2,'raw_snapshot_graph_count')
    for index,graph in enumerate(graphs):
        shape(graph,'project lock assets dgspec lock_dependencies frameworks' if phase=='native' else 'project assets dgspec','raw_snapshot_graph')
        exact(graph['project'],PROJECTS[index],'raw_snapshot_ordered_project')
        names=raw_project_paths(graph['project'])
        for kind in ('assets','dgspec'):
            shape(graph[kind],'path sha256','raw_snapshot_ref')
            exact(graph[kind]['path'],str(root/names[kind]),'raw_snapshot_fixed_stage_path')
            ref(graph[kind],root)
        if phase=='native':
            shape(graph['lock'],'path sha256','raw_native_lock_ref')
            exact(graph['lock']['path'],str(Path(replay)/names['lock']),'raw_native_real_lock_path')
            ref(graph['lock'],replay)
    return graphs


def locked_raw_delta_contract(native_raw, locked_raw, project, kind, replay):
    import copy
    raw_project_paths(project)
    require(kind in ('assets','dgspec'),'raw_kind')
    require(type(native_raw) is dict and type(locked_raw) is dict,'raw_root_object')
    expected=copy.deepcopy(native_raw)
    if kind=='assets':
        project_value=native_raw.get('project')
        require(type(project_value) is dict and type(project_value.get('restore')) is dict,'raw_assets_project_restore')
        exact(project_value['restore'].get('projectPath'),str(Path(replay)/project),'raw_assets_replay_project')
        paths=[['project','restore','restoreLockProperties']]
    else:
        projects=native_raw.get('projects')
        require(type(projects) is dict,'raw_dgspec_project_object')
        names=[str(Path(replay)/PROJECTS[0])]
        if project==PROJECTS[1]:names.append(str(Path(replay)/PROJECTS[1]))
        exact(sorted(projects),sorted(names),'raw_dgspec_fixed_actual_projects')
        paths=[]
        for name in names:
            require(type(projects[name]) is dict and type(projects[name].get('restore')) is dict,'raw_dgspec_project_restore')
            exact(projects[name]['restore'].get('projectPath'),name,'raw_dgspec_replay_project')
            paths.append(['projects',name,'restore','restoreLockProperties'])
    for path in paths:
        old=native_raw;actual=locked_raw;target=expected
        for key in path:
            require(type(old) is dict and key in old,'raw_native_mode_parent')
            require(type(actual) is dict and key in actual,'raw_locked_mode_parent')
            old=old[key];actual=actual[key];target=target[key]
        require(type(old) is dict and 'restoreLockedMode' not in old,'raw_native_no_locked_mode')
        require(type(actual) is dict and type(actual.get('restoreLockedMode')) is bool and actual['restoreLockedMode'],'raw_locked_exact_true')
        target['restoreLockedMode']=True
    exact(locked_raw,expected,'raw_full_json_only_fixed_true_insertions')


def locked_project_graphs_contract(native, build, startup, inputs, replay, native_ref, build_ref):
    # Stage roots come only from already verified native/build JSON Refs.
    # This helper checks file boundaries and JSON semantics, not SDK success.
    document(build,'build');document(native,'native')
    exact(native['source'],build['source'],'raw_native_build_same_source');exact(native['inputs'],build['inputs'],'raw_native_build_same_inputs')
    exact(native['database_accessed'],False,'raw_native_no_database');exact(build['database_accessed'],False,'raw_build_no_database')
    root=inputs['workspace_root']
    native_path=ref(native_ref,root);build_path=ref(build_ref,root)
    exact(native_path.name,'native.json','raw_native_stage_filename');exact(build_path.name,'build.json','raw_build_stage_filename')
    exact(build['native'],native_ref,'raw_build_native_stage_binding')
    exact(startup['native'],native_ref,'raw_startup_native_stage_binding');exact(startup['source'],build['source'],'raw_same_source_binding')
    exact(startup['replay_root'],str(replay),'raw_actual_replay_binding')
    exact(build['startup']['path'],str(build_path.parent/'startup.json'),'raw_startup_build_stage_path')
    exact(read_ref(build['startup'],root),startup,'raw_startup_actual_document')
    original=raw_snapshot_contract(native_ref,native['project_graphs'],replay,root,'native')
    locked=raw_snapshot_contract(build_ref,build['locked_project_graphs'],replay,root,'locked')
    locks={};live_assets={}
    for index,graph in enumerate(original):
        names=raw_project_paths(graph['project']);locked_graph=locked[index]
        ref(graph['lock'],replay);locks[graph['project']]=graph['lock']
        for kind in ('assets','dgspec'):
            live=dict(path=str(Path(replay)/names[kind]),sha256=locked_graph[kind]['sha256'])
            ref(live,replay)
            before=read_ref(graph[kind],root);after=read_ref(locked_graph[kind],root)
            locked_raw_delta_contract(before,after,graph['project'],kind,replay)
        live_assets[names['assets']]=locked_graph['assets']['sha256']
    exact(build['lock_before'],locks,'raw_build_before_actual_native_locks')
    exact(build['lock_after'],locks,'raw_build_after_actual_native_locks')
    exact(startup['locks'],{raw_project_paths(project)['lock']:value['sha256'] for project,value in locks.items()},'raw_startup_actual_live_locks')
    exact(startup['assets'],live_assets,'raw_startup_actual_locked_live_assets')
    return original,locked


def native_contract(native,build,startup,inputs,replay,native_ref,build_ref):
    import base64
    closure=inputs['expected_closure']
    locked_project_graphs_contract(native,build,startup,inputs,replay,native_ref,build_ref)
    exact(native['database_accessed'],False,'native_no_DB');exact(build['database_accessed'],False,'build_no_DB')
    graphs=native['project_graphs'];require(type(graphs) is list and len(graphs)==2,'two_new_native_graphs')
    exact([g['project'] for g in graphs],list(PROJECTS),'native_fixed_projects')
    config=closure['nuget_config'];ref(config,inputs['workspace_root'])
    require(len(native['sdk_events'])==len(build['locked_restores'])==len(build['commands'])==2,'two_actual_restore_builds')
    locks={};assets={};expected_archive_pairs=[]
    for i,g in enumerate(graphs):
        shape(g,'project lock assets dgspec lock_dependencies frameworks','native_graph')
        expected=closure['native_graphs'][g['project']]
        exact(g['lock_dependencies'],expected['lock_dependencies'],'approved_lock_dependencies');exact(g['frameworks'],expected['assets_frameworks'],'approved_frameworks_pruning')
        directory=Path(g['project']).parent
        ref(g['lock'],replay);exact(g['lock']['path'],str(replay/directory/'packages.lock.json'),'native_output_lock_path')
        locks[g['project']]=g['lock'];assets[raw_project_paths(g['project'])['assets']]=build['locked_project_graphs'][i]['assets']['sha256']
        lock=read_ref(g['lock'],replay);shape(lock,'version dependencies','actual_lock')
        exact(lock['version'],{PROJECTS[0]:2,PROJECTS[1]:1}[g['project']],'native_lock_version')
        flattened={name:row for group in lock['dependencies'].values() for name,row in group.items()}
        exact(flattened,g['lock_dependencies'],'actual_lock_graph')
        for name,row in flattened.items():
            require(row.get('type') in ('Direct','Transitive','Project'),'native_dependency_type')
            if row['type']=='Project':
                shape(row,'type dependencies','project_lock_dependency')
            else:
                keys='type resolved contentHash dependencies'+(' requested' if row['type']=='Direct' else '')
                # Native NuGet omits an empty dependencies object.
                if 'dependencies' not in row:keys=keys.replace(' dependencies','')
                shape(row,keys,'package_lock_dependency')
                require(type(row['resolved']) is str and type(row['contentHash']) is str,'package_version_contentHash')
                expected_archive_pairs.append((name,row['resolved'],row['contentHash'],g['project']))
        assets_doc=read_ref(g['assets'],inputs['workspace_root'])
        exact(assets_doc['project']['frameworks'],g['frameworks'],'actual_native_assets_framework')
        packages={name.split('/')[0] for name,row in assets_doc['libraries'].items() if row['type']=='package'}
        require(packages==set(CORE_PACKAGES)|{'W.DamengProvider'},'native_exact_Core13_W')
        require(not any('DM.DmProvider/' in n or 'Design/' in n or 'AspNet' in n for n in assets_doc['libraries']),'no_official_Design_AspNet')
        dgspec=read_ref(g['dgspec'],inputs['workspace_root'])
        expected_projects={str(replay/PROJECTS[0])}|({str(replay/PROJECTS[1])} if i==1 else set())
        require(set(dgspec['projects'])==expected_projects,'actual_dgspec_projects')
        common=['--disable-build-servers','-m:1','/nodeReuse:false','/p:UseSharedCompilation=false']
        process_contract(native['sdk_events'][i],[inputs['sdk']['path'],'restore',str(replay/g['project']),'--configfile',config['path'],'--force-evaluate']+common,replay)
        process_contract(build['locked_restores'][i],[inputs['sdk']['path'],'restore',str(replay/g['project']),'--locked-mode','--configfile',config['path']]+common,replay)
        process_contract(build['commands'][i],[inputs['sdk']['path'],'build',str(replay/g['project']),'--no-restore']+common,replay)
    exact(build['lock_before'],locks,'build_locks_before');exact(build['lock_after'],locks,'build_locks_after')
    exact(startup['locks'],{str(Path(v['path']).relative_to(replay)):v['sha256'] for v in locks.values()},'startup_exact_locks')
    exact(startup['assets'],assets,'startup_exact_assets')
    archives=native['archive_records'];metadata=native['native_metadata_records']
    require(type(archives) is list and type(metadata) is list and len(archives)==len(metadata)==len(expected_archive_pairs),'native_cache_complete')
    cache=absolute_path(native['cache_root'],inputs['workspace_root'],False)
    for a,m,(name,version,content_hash,project) in zip(archives,metadata,expected_archive_pairs):
        shape(a,'id version archive sidecar sha512','archive');shape(m,'id version metadata content_hash','native_metadata')
        exact([a['id'],a['version'],m['id'],m['version']],[name,version,name,version],'archive_package_identity')
        archive=ref(a['archive'],cache);sidecar=ref(a['sidecar'],cache);meta_path=ref(m['metadata'],cache)
        directory=cache/name.lower()/version
        exact(str(archive),str(directory/(name.lower()+'.'+version+'.nupkg')),'native_archive_path')
        exact(str(sidecar),str(archive)+'.sha512','native_sidecar_path');exact(str(meta_path),str(directory/'.nupkg.metadata'),'native_metadata_path')
        calculated=base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode('ascii')
        exact(a['sha512'],calculated,'whole_archive_SHA512');exact(sidecar.read_text().strip(),calculated,'native_sidecar_content')
        native_meta=load(meta_path);shape(native_meta,'version contentHash source','actual_native_metadata')
        exact(native_meta['version'],2,'native_metadata_version')
        exact(native_meta['contentHash'],content_hash,'actual_native_contentHash');exact(content_hash,inputs['expected_closure']['native_graphs'][project]['lock_dependencies'][name]['contentHash'],'root_origin_contentHash')
        exact(m['content_hash'],content_hash,'receipt_contentHash')
        native_hash_domains_contract(calculated,sidecar.read_text().strip(),native_meta['contentHash'],content_hash,inputs['expected_closure']['native_graphs'][project]['lock_dependencies'][name]['contentHash'])
        archive_origin_contract(archive,directory,inputs['expected_closure'],name,version)
    binary_contract(startup['host'],replay,HOST,True);binary_contract(startup['ef_provider'],replay,'W.EntityFrameworkCore.Dameng')
    deps=startup['ef_dependencies'];require(type(deps) is list and len(deps)==3,'three_actual_EF_dependencies')
    exact([d['name'] for d in deps],['Microsoft.EntityFrameworkCore','Microsoft.EntityFrameworkCore.Relational','Microsoft.EntityFrameworkCore.Abstractions'],'fixed_EF_dependencies')
    for d in deps:binary_contract(d,replay,d['name'])
    shape(startup['W'],'version package_sha256 dll_sha256 mvid identity_sha256 source_manifest_sha256 archive_sha256 path','startup_W')
    w_identity=read_ref(inputs['W_identity'])
    w=startup['W'];
    for key in ('version','package_sha256','dll_sha256','mvid'):exact(w[key],w_identity['W'][key],'startup_original_W_'+key)
    exact(w['identity_sha256'],inputs['W_identity']['sha256'],'W_identity_pin')
    exact(w['source_manifest_sha256'],w_identity['producer']['source_manifest_sha256'],'W_source_pin');exact(w['archive_sha256'],w_identity['producer']['source_archive_sha256'],'W_archive_pin')
    w_path=ref(dict(path=w['path'],sha256=w['dll_sha256']),replay)
    exact(managed_reader()(w_path.read_bytes()),w['mvid'],'actual_W_MVID')
    hash_map(build['binaries'],replay,generated=True)
    actual_binaries=sorted(str(p.relative_to(replay)) for p in replay.rglob('*') if p.is_file() and 'bin' in p.relative_to(replay).parts and p.suffix in ('.dll','.json','.pdb'))
    exact(sorted(build['binaries']),actual_binaries,'exact_all_built_binary_outputs')
    required_binary={str(Path(x['path']).relative_to(replay)):x['sha256'] for x in [startup['host'],startup['ef_provider']]+deps}
    required_binary[str(w_path.relative_to(replay))]=w['dll_sha256']
    required_binary[str(Path(startup['host']['deps']['path']).relative_to(replay))]=startup['host']['deps']['sha256']
    require(all(build['binaries'].get(k)==v for k,v in required_binary.items()),'build_all_loaded_binaries')
    deps_doc=read_ref(startup['host']['deps'],replay)
    require(deps_doc['libraries'].get('W.DamengProvider/'+w['version'],{}).get('type')=='package','host_W_PackageReference')
    require(deps_doc['libraries'].get('W.EntityFrameworkCore.Dameng/0.1.0',{}).get('type')=='project' or any(k.startswith('W.EntityFrameworkCore.Dameng/') and v.get('type')=='project' for k,v in deps_doc['libraries'].items()),'host_provider_ProjectReference')
    runtime_package_contract(deps_doc['libraries'])
    for name in ('Microsoft.EntityFrameworkCore','Microsoft.EntityFrameworkCore.Relational','Microsoft.EntityFrameworkCore.Abstractions'):
        require(deps_doc['libraries'].get(name+'/10.0.12',{}).get('type')=='package','host_EF_version')
    exact(build['offline_proofs'],[],'build_offline_separate_receipt')


def offline_registry_contract(observed):
    exact(observed['required_case_count'],28,'offline_required_count')
    exact(observed['case_count'],28,'offline_actual_count')
    cases=sequence(observed['cases'],'offline_registry')
    require(len(cases)==28,'offline_fixed_twenty_eight_cases')
    signatures=[]
    for async_api in (False,True):
        for supports in (False,True):
            for fail in (False,True):
                for external in (False,True):
                    signatures.append(dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=fail,external=external,terminal='rollback'))
    for owned in (False,True):
        for supports in (False,True):
            signatures.append(dict(api='factory_Create',supports_savepoints=supports,transaction_owned=owned))
    for async_api in (False,True):
        for supports in (False,True):
            for external in (False,True):
                signatures.append(dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=False,external=external,terminal='commit'))
    for ordinal,(row,expected) in enumerate(zip(cases,signatures)):
        require(type(row) is dict,'offline_actual_registry_row')
        for key,target in expected.items():
            require(key in row,'offline_registry_missing_'+key)
            exact(row[key],target,'offline_registry_slot_'+str(ordinal)+'_'+key)
    return signatures


def offline_commit_contract(row,startup,async_api,supports,external):
    shape(row,'api supports_savepoints fail_business external terminal services observer events commands counters status','offline_commit_case')
    for key,target in dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=False,external=external,terminal='commit',status='completed').items():exact(row[key],target,'offline_commit_registry_'+key)
    shape(row['services'],'factory','offline_commit_services')
    identity(row['services']['factory'],startup['ef_provider'],'W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory','W.EntityFrameworkCore.Dameng')
    observer_contract(row['observer'],startup)
    counters=shape(row['counters'],'sync_opens async_opens sync_business async_business savepoint_create savepoint_release savepoint_rollback transaction_disposals full_rollbacks full_commits real_network_attempts','offline_commit_counters')
    for value in counters.values():integer(value,'offline_commit_actual_counter')
    for key,target in dict(sync_business=0,async_business=0,savepoint_create=0,savepoint_release=0,savepoint_rollback=0,transaction_disposals=0 if external else 1,full_rollbacks=0,full_commits=1,real_network_attempts=0).items():exact(counters[key],target,'offline_commit_counter_'+key)
    require(counters['sync_opens']+counters['async_opens']>=1,'offline_commit_actual_stub_open')
    exact(row['commands'],[],'offline_commit_pure_terminal_no_business')
    events=sequence(row['events'],'offline_commit_events')
    suffix='Async' if async_api else ''
    exact([e['callback'] for e in events],[('TransactionUsed' if external else 'TransactionStarted')+suffix,'TransactionCommitting'+suffix,'TransactionCommitted'+suffix],'offline_commit_exact_three_callbacks')
    for e in events:
        shape(e,'sequence stage source callback context_object_id transaction_object_id connection_object_id current_connection_object_id connection_binding_source binding_sequence transaction_id connection_id is_async completed error capture_state','offline_commit_event')
        exact(e['source'],'DbTransactionInterceptor','offline_commit_source');exact(e['capture_state'],'observed','offline_commit_known');exact(e['error'],None,'offline_commit_no_error')
        exact(e['is_async'],async_api,'offline_commit_API');exact(e['completed'],not e['callback'].startswith('TransactionCommitting'),'offline_commit_callback_phase')
        require(row['observer']['observation_started_sequence']<integer(e['sequence'])<row['observer']['observation_completed_sequence'],'offline_commit_observed_interval')
        exact(e['context_object_id'],row['observer']['registration']['context_object_id'],'offline_commit_same_context')
        exact(e['stage'],'startup' if e['callback'].removesuffix('Async') in ('TransactionStarted','TransactionUsed') else 'full_Commit','offline_commit_stage')
    require([e['sequence'] for e in events]==sorted(e['sequence'] for e in events),'offline_commit_event_order')
    events_binding_contract(events,allow_used=external)
    exact(events[-1]['current_connection_object_id'],None,'offline_actual_commit_terminal_null')


def offline_contract(approval,inputs,build,startup,root):
    offline=read_ref(approval['offline'],root)
    shape(offline,'schema_version task inputs build startup host_process host_report python_process toolpack database_accessed status','offline_execution')
    for key,target in dict(schema_version=1,task='TP07-T1-offline-execution',inputs=approval['inputs'],build=approval['build'],startup=approval['startup'],toolpack=approval['toolpack'],database_accessed=False,status='completed_pending_independent_review').items():exact(offline[key],target,'offline_'+key)
    host_report_path=ref(offline['host_report'],root)
    process_contract(offline['host_process'],[inputs['sdk']['path'],startup['host']['path'],'--offline','--output',str(host_report_path)],startup['replay_root'])
    process_contract(offline['python_process'],[inputs['python']['path'],str(HERE/'test_contracts.py')],inputs['workspace_root'])
    observed=load(host_report_path)
    shape(observed,'schema_version task status database_accessed actual_provider host cases required_case_count case_count assertions error process_exit_code activation_accepted','offline_host')
    for key,target in dict(schema_version=1,task='TP07-T1-offline',status='completed',database_accessed=False,error=None,process_exit_code=0,activation_accepted=False).items():exact(observed[key],target,'offline_host_'+key)
    for key,expected in (('host',startup['host']),('actual_provider',startup['ef_provider'])):
        exact(observed[key],{k:expected[k] for k in ('name','sha256','mvid')},'offline_same_'+key)
    require(integer(observed['assertions'])>=1,'actual_offline_assertions')
    offline_registry_contract(observed)
    ordinal=0
    for async_api in (False,True):
        for supports in (False,True):
            for fail in (False,True):
                for external in (False,True):
                    row=observed['cases'][ordinal];ordinal+=1
                    shape(row,'api supports_savepoints fail_business external terminal services observer events commands counters status','offline_case')
                    for key,target in dict(api='async' if async_api else 'sync',supports_savepoints=supports,fail_business=fail,external=external,terminal='rollback',status='completed').items():exact(row[key],target,'offline_registry_'+key)
                    shape(row['services'],'factory','offline_services')
                    identity(row['services']['factory'],startup['ef_provider'],'W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory','W.EntityFrameworkCore.Dameng')
                    observer_contract(row['observer'],startup)
                    counters=shape(row['counters'],'sync_opens async_opens sync_business async_business savepoint_create savepoint_release savepoint_rollback transaction_disposals full_rollbacks full_commits real_network_attempts','offline_counters')
                    for val in counters.values():integer(val,'offline_actual_counter')
                    expected=dict(sync_business=0 if async_api else 1,async_business=1 if async_api else 0,savepoint_create=1 if supports else 0,savepoint_release=1 if supports else 0,savepoint_rollback=1 if supports and fail else 0,transaction_disposals=0 if external else 1,full_rollbacks=1,full_commits=0,real_network_attempts=0)
                    for key,target in expected.items():exact(counters[key],target,'offline_counter_'+key)
                    require(counters['sync_opens']+counters['async_opens']>=1,'offline_stub_open_observed')
                    events=sequence(row['events'],'offline_events');commands=sequence(row['commands'],'offline_commands')
                    require(0 < len(events)+len(commands)<=256,'offline_observation_bound')
                    suffix='Async' if async_api else ''
                    callbacks=[e['callback'] for e in events]
                    offline_savepoint_contract(supports,fail,counters,events,commands,async_api)
                    for callback,count in (('CreatingSavepoint',int(supports)),('CreatedSavepoint',int(supports)),('ReleasingSavepoint',int(supports)),('ReleasedSavepoint',int(supports)),('RollingBackToSavepoint',int(supports and fail)),('RolledBackToSavepoint',int(supports and fail)),('TransactionRollingBack',1),('TransactionRolledBack',1)):
                        require(callbacks.count(callback+suffix)==count,'offline_real_callback_'+callback)
                    require(not any(c.removesuffix('Async') in ('TransactionCommitting','TransactionCommitted','TransactionFailed') for c in callbacks),'offline_no_fake_commit')
                    for e in events:
                        shape(e,'sequence stage source callback context_object_id transaction_object_id connection_object_id current_connection_object_id connection_binding_source binding_sequence transaction_id connection_id is_async completed error capture_state','offline_event')
                        exact(e['capture_state'],'observed','offline_event_known');exact(e['error'],None,'offline_transaction_error')
                        exact(e['source'],'DbTransactionInterceptor','offline_event_source')
                        require(e['stage'] in ('startup','transaction_SaveChanges','full_Rollback'),'offline_event_finite_stage')
                        exact(e['is_async'],async_api,'offline_event_API')
                        require(e['callback'] in [c+suffix for c in CALLBACKS], 'offline_callback_finite')
                        exact(e['completed'],e['callback'].removesuffix('Async') not in PRE_CALLBACKS,'offline_callback_phase')
                        require(row['observer']['observation_started_sequence'] < integer(e['sequence']) < row['observer']['observation_completed_sequence'], 'offline_event_interval')
                        for field in ('context_object_id','transaction_object_id','connection_object_id'):require(integer(e[field])>0,'offline_event_object')
                    for c in commands:
                        shape(c,'sequence completed_sequence stage source command_id connection_id context_object_id command_object_id transaction_object_id connection_object_id is_async api kind sql_sha256 parameters completed result error capture_state','offline_command')
                        exact(c['source'],'DbCommandInterceptor','offline_command_source');exact(c['capture_state'],'observed','offline_command_known');exact(c['is_async'],async_api,'offline_command_API')
                        exact(c['stage'],'transaction_SaveChanges','offline_real_batch_command_stage')
                        require(row['observer']['observation_started_sequence'] < integer(c['sequence']) < integer(c['completed_sequence']) < row['observer']['observation_completed_sequence'],'offline_command_interval')
                        for field in ('context_object_id','command_object_id','transaction_object_id','connection_object_id'):require(integer(c[field])>0,'offline_command_object')
                        require(c['kind'] in ('reader','nonquery') and c['api'] == ('ReaderExecuting' if c['kind']=='reader' else 'NonQueryExecuting')+suffix,'offline_business_API')
                        for p in c['parameters']:
                            shape(p,'type db_type value','offline_parameter');exact(p['type'],'TP07.Runtime.StubParameter','offline_stub_parameter')
                    business=[c for c in commands if c['stage']=='transaction_SaveChanges']
                    require(len(business)>=1,'offline_real_business')
                    error_commands=[c for c in business if c['error'] is not None]
                    require(len(error_commands)==int(fail),'offline_original_failure_visible')
                    if fail:
                        exact(error_commands[0]['error'],dict(type='TP07.Runtime.StubBusinessException',code=None,number=None),'offline_failure_type')
                    else:
                        require(all(c['completed'] is True for c in business),'offline_business_complete')
                    events_binding_contract(events,allow_used=external)
                    terminal=[e for e in events if e['callback']=='TransactionRolledBack'+suffix]
                    require(len(terminal)==1,'offline_actual_terminal_callback')
                    exact(terminal[0]['current_connection_object_id'],None,'offline_actual_rollback_terminal_null')
                    callback_tx={e['transaction_object_id'] for e in events}
                    require(len(callback_tx)==1 and all(c['transaction_object_id'] in callback_tx for c in business),'offline_same_transaction')
    for owned in (False,True):
        for supports in (False,True):
            row=observed['cases'][ordinal];ordinal+=1
            shape(row,'api supports_savepoints transaction_owned status counters','offline_factory_case')
            for key,target in dict(api='factory_Create',supports_savepoints=supports,transaction_owned=owned,status='completed').items():exact(row[key],target,'offline_factory_registry')
            counters=shape(row['counters'],'sync_opens async_opens sync_business async_business savepoint_create savepoint_release savepoint_rollback transaction_disposals full_rollbacks full_commits real_network_attempts','offline_factory_counters')
            for v in counters.values():integer(v,'offline_factory_counter')
            for key,target in dict(sync_opens=1,async_opens=0,sync_business=0,async_business=0,savepoint_create=0,savepoint_release=0,savepoint_rollback=0,transaction_disposals=int(owned),full_rollbacks=0,full_commits=0,real_network_attempts=0).items():exact(counters[key],target,'factory_actual_lifecycle_'+key)
    for async_api in (False,True):
        for supports in (False,True):
            for external in (False,True):
                row=observed['cases'][ordinal];ordinal+=1
                offline_commit_contract(row,startup,async_api,supports,external)
    require(ordinal==28,'exact_offline_registry_finished')
    ready=read_ref(approval['readiness'],root)
    shape(ready,'schema_version task inputs source native build startup toolpack offline ordered_cases budgets status database_accessed activation_accepted','readiness')
    for key,target in dict(schema_version=1,task='TP07-T1-readiness',inputs=approval['inputs'],source=approval['source'],native=approval['native'],build=approval['build'],startup=approval['startup'],toolpack=approval['toolpack'],offline=approval['offline'],ordered_cases=list(CASE_IDS),budgets=dict(main=180,cleanup=180,validator=180),status='ready_pending_runtime',database_accessed=False,activation_accepted=False).items():exact(ready[key],target,'readiness_'+key)


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
       'id': 'W.DamengProvider',
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

def product_fixed(root):
    def resolve(value):
        if type(value) is dict:
            return {key: (str(Path(root)/child) if key in ('path','source_root') else resolve(child)) for key,child in value.items()}
        if type(value) is list: return [resolve(child) for child in value]
        return value
    return resolve(PRODUCT_V3_FIXED)


def product_map(value, count, code):
    require(type(value) is dict and len(value)==count, code)
    for name,digest in value.items():
        relative_path(name)
        require(type(digest) is str and HASH.fullmatch(digest), code+'_digest')
    return value


def product_parent_contract(value, parent, frozen, approval):
    shape(parent, ('EF_relational_API', 'SDK_executed', 'W', 'added_files', 'aggregate_format', 'base_approved_source', 'base_lineage', 'base_lineage_artifacts', 'base_prepared_source', 'base_source_count', 'base_source_hashes', 'database_executed', 'modified_files', 'namespace_boundary', 'old_L0_E_D_cohorts_reused_as_new_acceptance', 'old_parent_source_approval_reused_for_revision', 'patch_application_base', 'patch_paths', 'planned_unit_cases', 'planned_unit_methods', 'required_next_gates', 'revision', 'schema_version', 'source_aggregate_sha256', 'source_count', 'source_hashes', 'source_root', 'static_checks_only', 'status', 'task', 'tests_executed', 'transaction_capability_patch', 'transaction_probe_or_E_observation_implemented', 'unchanged_base_file_count', 'unit_delta_from_parent', 'unit_revision_parent'), 'parent_manifest36')
    shape(frozen, ('DB_executed','SDK_executed','patch_sha256','product_manifest_ref','schema_version','source178_delta_from_parent','source_aggregate_sha256','source_count','source_hashes','task','tests_executed'), 'parent_freeze_shape')
    exact(parent['source_count'], 178, 'parent_source_count')
    exact(parent['base_source_hashes'], value['base_source_hashes'], 'parent_original_base175')
    exact(frozen['schema_version'], 1, 'parent_freeze_schema')
    exact(frozen['DB_executed'], False, 'parent_freeze_not_executed')
    exact(frozen['SDK_executed'], False, 'parent_freeze_not_executed')
    exact(frozen['tests_executed'], False, 'parent_freeze_not_executed')
    exact(parent['revision'],2,'parent_revision')
    exact(parent['task'], 'TP07-T1 transaction-capability isolated EF overlay v2', 'parent_task')
    old=product_map(parent['source_hashes'],178,'parent_sources');new=product_map(value['source_hashes'],178,'new_sources')
    exact(frozen['product_manifest_ref'],value['unit_revision_parent']['product_manifest'],'parent_frozen_product')
    exact(frozen['source_count'],178,'parent_frozen_count');exact(frozen['source_hashes'],old,'parent_frozen_sources')
    exact(frozen['source_aggregate_sha256'],parent['source_aggregate_sha256'],'parent_frozen_aggregate')
    exact(parent['transaction_capability_patch'],value['unit_revision_parent']['transaction_capability_patch'],'parent_patch_identity')
    exact(frozen['patch_sha256'],parent['transaction_capability_patch']['sha256'],'parent_frozen_patch')
    exact(approval,dict(schema_version=1,task='TP07-T1-overlay-source-approval',status='source_approved',manifest=value['unit_revision_parent']['product_manifest'],exact_paths=sorted(old),exact_count=178,scope='transaction_capability_overlay_only'),'parent_approved_sources')
    exact(sorted(old),sorted(new),'parent_path_set')
    differences=[name for name in sorted(old) if old[name]!=new[name]]
    exact(differences,[RETRY_PATH],'parent_exact_single_difference')
    exact(len(old)-len(differences),177,'parent_retained177')
    delta=value['unit_delta_from_parent']
    exact(delta['parent_unit_sha256'],old[RETRY_PATH],'parent_unit_digest');exact(delta['new_unit_sha256'],new[RETRY_PATH],'new_unit_digest')
    exact(delta['transaction_unit25_sha256'],old[TRANSACTION_PATH],'transaction_unit_digest');exact(new[TRANSACTION_PATH],old[TRANSACTION_PATH],'transaction25_unchanged')


def product_baseline_rows_contract(rows, root):
    sequence(rows,'baseline_rows');exact(len(rows),795,'baseline_rows795')
    seen_names=set();seen_ids=set();outcomes=[]
    codebase=str(Path(root)/'.local/temporal-native/verification/TP07-T1-product-v3-build/work/test/W.EntityFrameworkCore.Dameng.Tests/bin/Debug/net10.0/W.EntityFrameworkCore.Dameng.Tests.dll')
    for row in rows:
        shape(row,'execution_id method outcome test_name','baseline_row')
        shape(row['method'],'adapterTypeName className codeBase name','baseline_method')
        require(all(type(item) is str and item for item in row['method'].values()),'baseline_method_text')
        exact(row['method']['adapterTypeName'],'executor://xunit/VsTestRunner3/netcore/','baseline_adapter')
        exact(row['method']['codeBase'],codebase,'baseline_codebase')
        name=row['test_name'];execution=row['execution_id']
        require(type(name) is str and name and name not in seen_names,'baseline_name_unique')
        require(type(execution) is str and re.fullmatch(r'[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}',execution) and execution not in seen_ids,'baseline_execution_unique')
        require(type(row['outcome']) is str and row['outcome'] in ('Passed','Failed'),'baseline_outcome')
        require((name == row['method']['className']+'.'+row['method']['name'] or name.startswith(row['method']['className']+'.'+row['method']['name']+'(')),'baseline_method_name')
        seen_names.add(name);seen_ids.add(execution);outcomes.append(row['outcome'])
    exact(outcomes.count('Passed'),783,'baseline_passed783');exact(outcomes.count('Failed'),12,'baseline_failed12')
    return rows


def product_retry_inventory_contract(value, rows):
    inv=shape(value['retry_contract_inventory'],set(RETRY_V3_FIXED)|{'planned_methods','new29_planned_test_names','original20_to_new_mapping','original12_failed_to_new_mapping'},'retry_inventory')
    for key,target in RETRY_V3_FIXED.items():exact(inv[key],target,'retry_fixed_'+key)
    methods=sequence(inv['planned_methods'],'retry_methods');exact(len(methods),21,'retry_methods21')
    all_names=[]
    for index,method in enumerate(methods):
        shape(method,'attribute inline_data method parameters planned_count planned_test_names','retry_method')
        declaration=RETRY_V3_METHODS[index]
        for key in declaration:exact(method[key],declaration[key],'retry_declaration_'+key)
        stem=RETRY_CLASS+'.'+declaration['method']
        if declaration['attribute']=='Fact': names=[stem]
        else:
            names=[]
            for data in declaration['inline_data']:
                arguments=[parameter+': '+str(data[i]) for i,parameter in enumerate(declaration['parameters'])]
                names.append(stem+'('+', '.join(arguments)+')')
        exact(method['planned_count'],len(names),'retry_method_case_count');exact(method['planned_test_names'],names,'retry_method_names')
        all_names+=names
    exact(len(all_names),49,'retry_names49');exact(len(set(all_names)),49,'retry_names49_unique')
    original=[row for row in rows if row['method']['className']==RETRY_CLASS]
    exact(len(original),20,'original_retry20');exact(sum(row['outcome']=='Failed' for row in original),12,'original_retry_failed12')
    pairs=[]
    special={RETRY_CLASS+'.StrategyRetriesAnAdditionalErrorNumber':RETRY_CLASS+'.StrategyRejectsAdditionalErrorNumberBeforeExecutingDelegate',RETRY_CLASS+'.EnableRetryOnFailureAppliesCustomSettingsAndCopiesErrorNumbers':RETRY_CLASS+'.EnableRetryOnFailureAppliesCustomSettingsAndCopiesEmptyErrorNumbers'}
    for row in original:
        name=row['test_name']
        mapped=special.get(name,name.replace('.StrategyRecognizesOnlyExplicitDefaultTransientDamengCodes(','.StrategyDoesNotRetryUnclassifiedWServerNumbers('))
        pairs.append(dict(original_test_name=name,original_outcome=row['outcome'],new_planned_test_name=mapped))
    exact(inv['original20_to_new_mapping'],pairs,'original20_mapping')
    exact(inv['original12_failed_to_new_mapping'],[row for row in pairs if row['original_outcome']=='Failed'],'original12_failed_mapping')
    targets={row['new_planned_test_name'] for row in pairs}
    require(len(targets)==20 and targets<=set(all_names),'original20_distinct_targets')
    added=sorted(set(all_names)-targets);exact(len(added),29,'added_retry29')
    exact(inv['new29_planned_test_names'],added,'new29_exact_disjoint_names')
    return all_names


def product_full_inventory_contract(value, rows, retry_names, root):
    full=shape(value['full_unit_inventory'],set(FULL_V3_FIXED)|{'planned_full_test_names'},'full_inventory')
    for key,target in FULL_V3_FIXED.items():
        if key=='baseline_rows':target=dict(path=str(Path(root)/target['path']),sha256=target['sha256'])
        exact(full[key],target,'full_fixed_'+key)
    retained=[row['test_name'] for row in rows if row['method']['className']!=RETRY_CLASS]
    exact(len(retained),775,'full_retained775')
    tx=[row['test_name'] for row in rows if row['method']['className']==TRANSACTION_CLASS]
    require(len(tx)==25 and set(tx)<=set(retained),'full_transaction25_preserved')
    names=sorted(retained+retry_names)
    require(len(names)==len(set(names))==824,'full824_unique_union')
    exact(full['planned_full_test_names'],names,'full_exact_complete824_case_set')


def product_manifest_contract(value,inputs,lineage):
    root=inputs['workspace_root'];fixed=product_fixed(root)
    shape(value,set(fixed)|{'source_hashes','base_source_hashes','retry_contract_inventory','full_unit_inventory'},'product43')
    for key,target in fixed.items():exact(value[key],target,'product_fixed_'+key)
    exact(inputs['ef_overlay']['manifest'],dict(path=str(Path(root)/PRODUCT_V3_PATH),sha256=PRODUCT_V3_SHA),'current_v3_manifest_ref')
    exact(inputs['ef_overlay']['source_root'],fixed['source_root'],'actual_v3_overlay_root')
    references=[value[key] for key in ('base_prepared_source','base_approved_source','transaction_capability_patch')]
    references+=list(value['base_lineage_artifacts'].values())+list(value['unit_revision_parent'].values())
    for reference in references:ref(reference,root)
    base=product_map(value['base_source_hashes'],175,'base175');current=product_map(value['source_hashes'],178,'current178')
    exact(base,read_ref(value['base_approved_source'],root)['source_hashes'],'base_approved175_map')
    exact(base,read_ref(value['base_prepared_source'],root)['approved_ef_source_hashes'],'base_prepared175_map')
    require(set(base)<=set(current),'base_paths_preserved')
    exact(sorted(set(current)-set(base)),sorted(value['added_files']),'actual_added3')
    changed=sorted(name for name in base if base[name]!=current[name]);exact(changed,value['modified_files'],'actual_modified2')
    exact(len(base)-len(changed),173,'base_retained173')
    closure=inputs['expected_closure'];exact(closure['product_paths'],sorted(current),'approved178_paths');exact(closure['product_count'],178,'approved178_count')
    hosts=sorted('test/TP07TransactionCapabilityProbe/'+name.removeprefix('host/') for name in TOOL_FILES if name.startswith('host/'))
    exact(closure['host_paths'],hosts,'host9_paths')
    exact(closure['source_paths'],sorted(list(current)+hosts),'source187_paths');exact(closure['source_count'],187,'source187_count')
    exact(closure['locked_paths'],sorted(list(current)+hosts+['test/TP07TransactionCapabilityProbe/packages.lock.json']),'locked188_paths');exact(closure['locked_count'],188,'locked188_count')
    source_root=absolute_path(value['source_root'],root,False);require(source_root.is_dir(),'actual_product_source_root')
    entries=[]
    for path in source_root.rglob('*'):
        require(not path.is_symlink(),'product_source_symlink')
        if path.is_file():entries.append(str(path.relative_to(source_root)))
    exact(sorted(entries),sorted(current),'product_complete_directory_enumeration')
    hash_map(current,source_root,sorted(current))
    aggregate=''.join(current[name]+'  '+name+'\n' for name in sorted(current))
    exact(value['source_aggregate_sha256'],hashlib.sha256(aggregate.encode()).hexdigest(),'product178_aggregate')
    for key,target in value['base_lineage'].items():
        if key!='approved_ef_source_manifest_sha256':exact(lineage[key],target,'base_lineage_'+key)
    exact(lineage['approved_ef_source_manifest_sha256'],PRODUCT_V3_SHA,'tool_lineage_current_v3')
    exact(lineage['transaction_patch_sha256'],value['transaction_capability_patch']['sha256'],'tool_lineage_v3_patch')
    exact(lineage['parent_overlay_sha256'],value['base_prepared_source']['sha256'],'tool_lineage_base_prepared175')
    parents=value['unit_revision_parent']
    product_parent_contract(value,read_ref(parents['product_manifest'],root),read_ref(parents['sourcefreeze_manifest'],root),read_ref(parents['source_approval'],root))
    baseline=value['full_unit_inventory'].get('baseline_rows');target=FULL_V3_FIXED['baseline_rows']
    exact(baseline,dict(path=str(Path(root)/target['path']),sha256=target['sha256']),'baseline_fixed_identity')
    rows=product_baseline_rows_contract(read_ref(baseline,root),root)
    names=product_retry_inventory_contract(value,rows)
    product_full_inventory_contract(value,rows,names,root)
    return value


def product_source_approval_contract(approval, inputs, manifest):
    exact(manifest['required_source_approval_scope'],SOURCE_SCOPE_V3,'source_manifest_scope_v3')
    paths=sorted(manifest['source_hashes'])
    exact(inputs['expected_closure']['product_paths'],paths,'source_approval_product_paths')
    exact(approval,dict(schema_version=1,task='TP07-T1-overlay-source-approval',status='source_approved',manifest=inputs['ef_overlay']['manifest'],exact_paths=paths,exact_count=178,scope=SOURCE_SCOPE_V3),'source_approval_v3')
    return approval


def snapshot_contract(approval,inputs,startup):
    result={}
    for key in ('inputs','source','native','build','startup','toolpack','offline','readiness'):
        path=ref(approval[key]);result[str(path)]=approval[key]['sha256']
    replay=Path(startup['replay_root'])
    for group in ('source_hashes','locks','assets'):
        for name,digest in startup[group].items():
            ref(dict(path=str(replay/name),sha256=digest),replay);result[str(replay/name)]=digest
    build=read_ref(approval['build'])
    for name,digest in build['binaries'].items():
        ref(dict(path=str(replay/name),sha256=digest),replay);result[str(replay/name)]=digest
    for value in [startup['host'],startup['ef_provider']]+startup['ef_dependencies']:
        ref(dict(path=value['path'],sha256=value['sha256']),replay);result[value['path']]=value['sha256']
    result[startup['W']['path']]=startup['W']['dll_sha256']
    result[startup['host']['deps']['path']]=startup['host']['deps']['sha256']
    for key in ('W_identity','capabilities','probe_source_manifest'):
        path=ref(inputs[key]);result[str(path)]=inputs[key]['sha256']
    probe=read_ref(inputs['probe_source_manifest'],inputs['workspace_root'])
    for name,digest in probe['source_hashes'].items():
        path=ref(dict(path=str(Path(probe['source_root'])/name),sha256=digest),inputs['workspace_root']);result[str(path)]=digest
    for key in ('sdk','python'):
        value=inputs[key];ref({k:value[k] for k in ('path','sha256')});result[value['path']]=value['sha256']
    for value in approval['wrappers'].values():
        ref(value);result[value['path']]=value['sha256']
    pack=toolpack_contract(approval['toolpack'],inputs['workspace_root'])
    for name,digest in pack['source_hashes'].items():result[str(Path(inputs['workspace_root'])/name)]=digest
    return result


CASE_DIRECTORIES=('00-shared-sync','01-shared-async','02-tls-sync','03-tls-async')
CHECK_KEYS=('fixed_integer_pair','runtime_source_binary','formal_DI','observer_installed','same_transaction_callbacks','shared_savepoints_or_TLS_zero','explicit_full_rollback','fresh_original_raw_typed_FF9','exact_ledger_cleanup','processes_natural_exit','immutable_input_snapshot','shared_positive_controls')


def validation_document(approval_ref,execution_ref,case_id,checks,controls):
    return dict(schema_version=1,task='TP07-T1-validation',approval=approval_ref,case_receipt=execution_ref,case_id=case_id,status='passed',issues=[],missing=[],captured_complete=True,checks=checks,positive_control_refs=controls,activation_accepted=False)


def validation_contract(value,approval_ref,execution_ref,case_id):
    document(value,'validation')
    exact(value['approval'],approval_ref,'validation_approval');exact(value['case_receipt'],execution_ref,'validation_immutable_input')
    exact(value['case_id'],case_id,'validation_case');exact(value['status'],'passed','validation_status')
    exact(value['issues'],[],'validation_issues');exact(value['missing'],[],'validation_missing')
    exact(value['captured_complete'],True,'validation_complete_derived')
    exact(value['activation_accepted'],False,'no_producer_activation_acceptance')
    exact(value['checks'],{k:True for k in CHECK_KEYS},'validation_all_independent_checks')


def ticket_contract(reference,approval_ref,approval,startup,case_id,role,predecessor=None):
    ordinal=CASE_IDS.index(case_id);case_root=Path(approval['output_root'])/CASE_DIRECTORIES[ordinal]
    exact(reference['path'],str(case_root/(role+'-ticket.json')),'ticket_exact_path')
    ticket=document(read_ref(reference,approval['output_root']),'case-ticket')
    exact(ticket,dict(schema_version=1,task='TP07-T1-case-ticket',approval=approval_ref,cohort_id=approval_ref['sha256'],ordinal=ordinal,case_id=case_id,role=role,startup=approval['startup'],predecessor_validation=predecessor,output=str(case_root/role),ledger=str(case_root/'main/ownership.json')),'immutable_ticket')
    return ticket


def host_process_contract(slot,approval,inputs,case_id,role):
    shape(slot,'process report ticket','host_slot')
    spec=definition(CASE_IDS.index(case_id));case_root=Path(approval['output_root'])/CASE_DIRECTORIES[spec['ordinal']]
    exact(slot['report']['path'],str(case_root/role/'report.json'),'host_exact_report_path')
    ref(slot['report'],case_root)
    argv=[approval['wrappers'][spec['profile']]['path'],inputs['python']['path'],str(HERE/'run.py'),'_host','--ticket',slot['ticket']['path'],'--ticket-sha256',slot['ticket']['sha256']]
    process_contract(slot['process'],argv,inputs['workspace_root'])


def finalized_control_contract(validation_ref,validation,approval_ref,approval,inputs,case_id):
    ordinal=CASE_IDS.index(case_id);case_root=Path(approval['output_root'])/CASE_DIRECTORIES[ordinal]
    final_path=case_root/'case-receipt.json'
    require(final_path.is_file(),'positive_control_final_receipt_required')
    final=document(load(absolute_path(str(final_path),approval['output_root'])),'case-execution')
    exact(final['case_id'],case_id,'positive_control_final_case');exact(final['cohort_id'],approval_ref['sha256'],'positive_control_final_cohort')
    exact(final['status'],'passed','positive_control_final_natural_acceptance')
    slot=shape(final['validator'],'execution_input process report','positive_control_validator_slot')
    exact(slot['report'],validation_ref,'positive_control_final_validation_ref')
    exact(slot['execution_input'],validation['case_receipt'],'positive_control_final_execution_ref')
    execution=document(read_ref(slot['execution_input'],case_root),'case-execution')
    exact({k:v for k,v in final.items() if k not in ('validator','status')},{k:v for k,v in execution.items() if k not in ('validator','status')},'positive_control_final_immutable_input')
    argv=[inputs['python']['path'],str(HERE/'validate.py'),'case','--approval',approval_ref['path'],'--approval-sha256',approval_ref['sha256'],'--case-receipt',slot['execution_input']['path'],'--output',validation_ref['path']]
    process_contract(slot['process'],argv,inputs['workspace_root'])


def execution_contract(execution_ref,approval_ref,approval,inputs,startup,cache=None):
    cache={} if cache is None else cache
    key=(execution_ref['path'],execution_ref['sha256'])
    if key in cache:return cache[key]
    receipt=document(read_ref(execution_ref,approval['output_root']),'case-execution')
    case_id=receipt['case_id'];require(case_id in CASE_IDS,'execution_fixed_case');ordinal=CASE_IDS.index(case_id)
    case_root=Path(approval['output_root'])/CASE_DIRECTORIES[ordinal]
    exact(execution_ref['path'],str(case_root/'execution-input.json'),'immutable_execution_input_path')
    exact(receipt['approval'],approval_ref,'execution_approval');exact(receipt['cohort_id'],approval_ref['sha256'],'execution_cohort')
    exact(receipt['validator'],None,'immutable_execution_not_final_receipt')
    exact(receipt['status'],'execution_completed_pending_validation','execution_initial_status')
    snapshot=snapshot_contract(approval,inputs,startup)
    exact(receipt['inputs_before'],snapshot,'exact_inputs_before');exact(receipt['inputs_after'],snapshot,'exact_inputs_after')
    predecessor=None
    if ordinal:
        path=Path(approval['output_root'])/CASE_DIRECTORIES[ordinal-1]/'validation.json'
        require(path.is_file(),'predecessor_required')
        predecessor=dict(path=str(path),sha256=sha(path))
        previous=read_ref(predecessor,approval['output_root']);document(previous,'validation')
        previous_checks,previous_controls=execution_contract(previous['case_receipt'],approval_ref,approval,inputs,startup,cache)
        validation_contract(previous,approval_ref,previous['case_receipt'],CASE_IDS[ordinal-1])
        exact(previous,validation_document(approval_ref,previous['case_receipt'],CASE_IDS[ordinal-1],previous_checks,previous_controls),'predecessor_independent_recomputation')
        finalized_control_contract(predecessor,previous,approval_ref,approval,inputs,CASE_IDS[ordinal-1])
    ticket_contract(receipt['ticket'],approval_ref,approval,startup,case_id,'main',predecessor)
    exact(receipt['main']['ticket'],receipt['ticket'],'main_ticket_binding')
    host_process_contract(receipt['main'],approval,inputs,case_id,'main')
    host_process_contract(receipt['independent_cleanup'],approval,inputs,case_id,'cleanup')
    ticket_contract(receipt['independent_cleanup']['ticket'],approval_ref,approval,startup,case_id,'cleanup',predecessor)
    exact(receipt['ledger']['path'],str(case_root/'main/ownership.json'),'exact_ledger_path')
    ledger=read_ref(receipt['ledger'],case_root)
    report=read_ref(receipt['main']['report'],case_root);cleanup=read_ref(receipt['independent_cleanup']['report'],case_root)
    for observed,slot in ((report,receipt['main']),(cleanup,receipt['independent_cleanup'])):
        exact(observed['approval'],approval_ref,'report_approval_binding');exact(observed['ticket'],slot['ticket'],'report_ticket_binding')
    exact(report['cohort_id'],approval_ref['sha256'],'report_cohort_binding')
    exact(cleanup['ledger_before'],receipt['ledger'],'cleanup_frozen_exact_ledger')
    checks=case_contract(report,cleanup,ledger,startup,approval['startup']['sha256'],receipt['ledger']['sha256'],case_id)
    controls=[approval['offline']]
    if ordinal>=2:
        for control_ordinal in (0,1):
            control_path=Path(approval['output_root'])/CASE_DIRECTORIES[control_ordinal]/'validation.json'
            control_ref=dict(path=str(control_path),sha256=sha(control_path));control=read_ref(control_ref,approval['output_root'])
            control_checks,control_controls=execution_contract(control['case_receipt'],approval_ref,approval,inputs,startup,cache)
            validation_contract(control,approval_ref,control['case_receipt'],CASE_IDS[control_ordinal])
            exact(control,validation_document(approval_ref,control['case_receipt'],CASE_IDS[control_ordinal],control_checks,control_controls),'TLS_same_cohort_shared_positive_control')
            finalized_control_contract(control_ref,control,approval_ref,approval,inputs,CASE_IDS[control_ordinal])
            controls.append(control_ref)
    checks.update(processes_natural_exit=True,immutable_input_snapshot=True,shared_positive_controls=True)
    cache[key]=(checks,controls)
    return checks,controls


def matrix_contract(value,receipts,validations,approval_ref):
    document(value,'matrix')
    exact(value['approval'],approval_ref,'matrix_approval');exact(value['cohort_id'],approval_ref['sha256'],'matrix_cohort')
    exact(value['required_cases'],list(CASE_IDS),'matrix_fixed_four_cases')
    exact(value['candidate_acceptance'],False,'matrix_no_candidate_acceptance');exact(value['full_EF_C_accepted'],False,'matrix_no_E338_acceptance')
    n=len(value['attempted_cases']);require(1<=n<=4,'matrix_attempted_bound')
    exact(value['attempted_cases'],list(CASE_IDS[:n]),'matrix_fixed_prefix_no_resume')
    require(type(receipts) is list and type(validations) is list and len(receipts)==len(validations)==len(value['case_receipts'])==len(value['case_validations'])==n,'matrix_receipt_coverage')
    failure=None
    for i,(receipt,validation) in enumerate(zip(receipts,validations)):
        exact(receipt['case_id'],CASE_IDS[i],'matrix_receipt_slot');exact(receipt['cohort_id'],approval_ref['sha256'],'matrix_receipt_cohort')
        if validation is None or validation['status']!='passed' or receipt['status']!='passed':
            require(failure is None and i==n-1,'matrix_fail_stop_no_later_execution');failure=CASE_IDS[i]
        else:
            exact(validation['case_id'],CASE_IDS[i],'matrix_validation_slot')
            exact(validation['activation_accepted'],False,'matrix_no_case_acceptance')
    exact(value['first_failure'],failure,'matrix_first_failure')
    require(type(value['input_drift']) is bool and type(value['cleanup_pending']) is bool,'matrix_pending_bool')
    if failure is None:
        require(n==4,'matrix_all_four_required');exact(value['input_drift'],False,'matrix_no_drift');exact(value['cleanup_pending'],False,'matrix_no_cleanup_pending')
        exact(value['status'],'completed_pending_review','matrix_review_only')
    else:
        exact(value['status'],'failed','matrix_failed_preserved')
    return {'ordered_four_completed':failure is None,'later_cases_not_run':list(CASE_IDS[n:]),'candidate_acceptance':False,'full_EF_C_accepted':False}


def validate_matrix(reference,approval_ref,approval,inputs,startup):
    value=read_ref(reference,approval['output_root']);document(value,'matrix')
    exact(reference['path'],str(Path(approval['output_root'])/'matrix.json'),'matrix_path')
    receipts=[];validations=[];cache={};nonces=[];tables=[]
    for i,receipt_ref in enumerate(value['case_receipts']):
        require(i<4,'matrix_count')
        case_root=Path(approval['output_root'])/CASE_DIRECTORIES[i]
        exact(receipt_ref['path'],str(case_root/'case-receipt.json'),'matrix_final_receipt_path')
        final=document(read_ref(receipt_ref,case_root),'case-execution')
        receipts.append(final)
        shape(final['validator'],'execution_input process report','final_validator_slot')
        execution_ref=final['validator']['execution_input'];execution=document(read_ref(execution_ref,case_root),'case-execution')
        exact({k:v for k,v in final.items() if k not in ('validator','status')},{k:v for k,v in execution.items() if k not in ('validator','status')},'final_immutable_input_binding')
        validation_ref=final['validator']['report'];exact(value['case_validations'][i],validation_ref,'matrix_validator_ref')
        validation=document(read_ref(validation_ref,case_root),'validation');validations.append(validation)
        argv=[inputs['python']['path'],str(HERE/'validate.py'),'case','--approval',approval_ref['path'],'--approval-sha256',approval_ref['sha256'],'--case-receipt',execution_ref['path'],'--output',validation_ref['path']]
        process_contract(final['validator']['process'],argv,inputs['workspace_root'])
        checks,controls=execution_contract(execution_ref,approval_ref,approval,inputs,startup,cache)
        exact(validation,validation_document(approval_ref,execution_ref,CASE_IDS[i],checks,controls),'matrix_independent_validation_recomputation')
        entry=read_ref(execution['ledger'],case_root)['objects'][0];nonces.append(entry['nonce']);tables.append(entry['table'])
    result=matrix_contract(value,receipts,validations,approval_ref)
    for suffix in CASE_DIRECTORIES[len(receipts):]:require(not (Path(approval['output_root'])/suffix).exists(),'failed_suffix_must_not_run')
    require(len(nonces)==len(set(nonces)) and len(tables)==len(set(tables)),'matrix_unique_owned_objects')
    require(result['ordered_four_completed'],'matrix_activation_pending_failed')
    return result


def write_new(path,value,root):
    output=absolute_path(str(path),root,False)
    require(not output.exists(),'fresh_validator_output')
    require(output.parent.is_dir(),'validator_output_parent')
    with output.open('x',encoding='utf-8') as stream:
        json.dump(value,stream,indent=2,sort_keys=True,allow_nan=False);stream.write('\n')


def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='mode',required=True)
    for mode,input_flag in (('case','case-receipt'),('matrix','matrix')):
        p=sub.add_parser(mode);p.add_argument('--approval',required=True);p.add_argument('--approval-sha256',required=True);p.add_argument('--'+input_flag,required=True);p.add_argument('--output',required=True)
    args=parser.parse_args();approval_ref=dict(path=args.approval,sha256=args.approval_sha256)
    execution_ref=None;case_id=None;approval=None
    try:
        approval,inputs,startup=approval_contract(approval_ref)
        artifact_path=absolute_path(getattr(args,'case_receipt' if args.mode=='case' else 'matrix'),approval['output_root'])
        execution_ref=dict(path=str(artifact_path),sha256=sha(artifact_path))
        if args.mode=='case':
            receipt=load(artifact_path);case_id=receipt.get('case_id')
            require(case_id in CASE_IDS,'fixed_case_id')
            exact(args.output,str(artifact_path.parent/'validation.json'),'case_validation_path')
            checks,controls=execution_contract(execution_ref,approval_ref,approval,inputs,startup)
            value=validation_document(approval_ref,execution_ref,case_id,checks,controls)
        else:
            result=validate_matrix(execution_ref,approval_ref,approval,inputs,startup)
            value=dict(schema_version=1,task='TP07-T1-matrix-validation',approval=approval_ref,matrix=execution_ref,status='passed_pending_root_review',checks=result,activation_accepted=False)
        write_new(args.output,value,approval['output_root']);return 0
    except Exception as error:
        code=error.args[0] if isinstance(error,ContractError) and error.args and type(error.args[0]) is str else 'external_file_contract_failure'
        if args.mode=='case' and approval is not None and execution_ref is not None and case_id in CASE_IDS:
            value=dict(schema_version=1,task='TP07-T1-validation',approval=approval_ref,case_receipt=execution_ref,case_id=case_id,status='rejected',issues=[code],missing=[],captured_complete=False,checks={},positive_control_refs=[],activation_accepted=False)
            try:write_new(args.output,value,approval['output_root'])
            except Exception:pass
        print(json.dumps(dict(status='rejected',code=code)));return 1


if __name__=='__main__':
    raise SystemExit(main())
