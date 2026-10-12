"""Handwritten EXPANSION53 contracts; never runtime or database acceptance."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('ff7_expansion_validator', HERE / 'validate.py')
V = importlib.util.module_from_spec(spec); spec.loader.exec_module(V)
FIXTURE_ROLE = 'handwritten_contract_inputs_not_runtime_or_database_acceptance'


def physical():
    return dict(profile='shared', server_version='8.1.5.60', message_version=21, encoding='utf-8', negotiated_encoding='utf-8',
        encoding_override_empty=True, profile_origin='physical_get_conn_instance_conn_property', server_user='WDM_PROVIDER_TEST',
        server_schema='WDM_PROVIDER_TEST', result_format='ordinary_non_BDTA')


def route(role):
    names = dict(reader='DmDataReader', command='DmCommand', parameter='DmParameter', connection='DmConnection')
    w = V.policy()['W']; return dict(role=role, type='W.Dm.' + names[role], dll_sha256=w['dll_sha256'], mvid=w['mvid'])


def handwritten_oracle(expected):
    dto = expected['kind'] == 'dto'; ctype, width = (27, 11) if dto else (26, 9)
    metadata = dict(c_type=ctype, width=width, scale=7, mask=0, is_bdta=False, metadata_role='result_column', schema_scale=7,
        raw_source='ResultColumn', raw_scale_status='captured_server_source', raw_c_type=ctype, raw_precision=width,
        raw_scale=7, raw_mask_before=0, raw_derived_mask=0)
    if expected['is_null']:
        value = dict(rows=1, is_null=True, local_ticks=None, offset_minutes=None, raw=None, ff9=None,
            typed={'type': 'System.DBNull', 'is_null': True}, metadata=metadata, identity=physical(), reader_route=route('reader'))
        cell = dict(payload_length=None, payload_sha256=None, is_null=True, result_format='ordinary_non_BDTA')
        return value, cell
    f = expected['fields']
    date = f['year'] | (f['month'] << 15) | (f['day'] << 19)
    clock = f['hour'] | (f['minute'] << 5) | (f['second'] << 11) | (f['nanoseconds'] << 17)
    raw = date.to_bytes(3, 'little') + clock.to_bytes(6, 'little')
    if dto: raw += f['offset_minutes'].to_bytes(2, 'little', signed=True)
    text = f"{f['year']:04d}-{f['month']:02d}-{f['day']:02d} {f['hour']:02d}:{f['minute']:02d}:{f['second']:02d}.{f['nanoseconds']:09d}"
    if dto:
        sign = '+' if f['offset_minutes'] >= 0 else '-'; hours, minutes = divmod(abs(f['offset_minutes']), 60)
        text += f' {sign}{hours:02d}:{minutes:02d}'
    cell = dict(payload_length=len(raw), payload_sha256=hashlib.sha256(raw).hexdigest(), is_null=False, result_format='ordinary_non_BDTA')
    value = dict(rows=1, is_null=False, local_ticks=expected['local_ticks'], offset_minutes=expected['offset_minutes'],
        identity=physical(), reader_route=route('reader'), typed=V.safe_value(expected), ff9=text, metadata=metadata,
        raw=dict(source='ordinary_result_cache_before_temporal_projection', capture_stage='before_any_temporal_getter', is_bdta=False,
            c_type=ctype, width=width, scale=7, hex=raw.hex().upper(), payload_length=len(raw), payload_sha256=hashlib.sha256(raw).hexdigest()))
    return value, cell


def catalog(entry, kind):
    return [dict(name='ID', data_type='INT', nullable='N', scale=0, data_length=4),
        dict(name='V', data_type='DATETIME WITH TIME ZONE' if kind == 'dto' else 'TIMESTAMP', scale=7, data_length=11 if kind == 'dto' else 9, nullable='Y'),
        dict(name=entry['owner_marker'], data_type='VARCHAR', data_length=32, nullable='Y', scale=None)]


def operation(case, plan, entry, before):
    role, action, row, operand, expected = plan; sql, exp = V.sql_for(role, row, operand, entry['table'])
    oracle, cell = handwritten_oracle(expected)
    attempt = dict(table=entry['table'], row_id=row, column='V', rows=1, completed=True, identity=physical(), metadata=oracle['metadata'], cell=cell)
    if not expected['is_null']: attempt.update(ff9=oracle['ff9'], typed=oracle['typed'])
    typed = role in ('typed_insert', 'typed_expected')
    parameters = [dict(route=route('parameter'), db_type='DateTimeOffset' if case['kind'] == 'dto' else 'DateTime2',
        scale=7, precision=0, value=V.safe_value(operand))] if typed else []
    value = dict(operation_id=case['case_id'] + ':' + role, role=role, operation=action, api='ExecuteScalarAsync' if action == 'select' else 'ExecuteNonQueryAsync',
        table=entry['table'], row_id=row, input=V.safe_value(operand), expected=V.safe_value(expected), expression_sql=exp,
        expression_sha256=None if exp is None else V.text_sha(exp), predicate_expression_sql=V.expression(operand) if action == 'select' else None, sql=sql, sql_sha256=V.text_sha(sql),
        parameter_count=len(parameters), parameters=parameters, command_route=route('command'), connection_route=route('connection'), operation_identity=physical(),
        catalog_pre=catalog(entry, case['kind']), catalog_post=catalog(entry, case['kind']), row_total_pre=before, row_total_post=before + (1 if action == 'insert' else 0),
        affected_rows=None if action == 'select' else 1, scalar_result=1 if role == 'predicate_positive' else 0 if role == 'predicate_negative' else None,
        status='completed', outcome='exact', oracle=oracle, oracle_read_attempts=[attempt])
    if action == 'insert': value['prewrite_absence'] = dict(oracle_read_attempts=[dict(table=entry['table'], row_id=row, column='V', rows=0, completed=True, identity=physical())])
    if action == 'update':
        before_oracle, before_cell = handwritten_oracle(case['initial'])
        before_attempt = dict(table=entry['table'], row_id=row, column='V', rows=1, completed=True, identity=physical(), metadata=before_oracle['metadata'],
            cell=before_cell, ff9=before_oracle['ff9'], typed=before_oracle['typed'])
        value.update(before_expected=V.safe_value(case['initial']), before_oracle=before_oracle, before_oracle_read_attempts=dict(oracle_read_attempts=[before_attempt]))
    return value


def reports():
    p = V.policy(); cases = p['cases']; old = V.value_helpers().policy(); w = p['W']; startup_sha = 'a' * 64
    startup = dict(source_manifest_sha256='b' * 64, lock_manifest_sha256='c' * 64, ef_provider=dict(dll_sha256='d' * 64, mvid='fixture-EF'),
        startup_assemblies=[dict(name='TP07LiteralProbe', dll_sha256='e' * 64, mvid='fixture-startup', deps_sha256='f' * 64)])
    runtime = dict(version=w['version'], package_sha256=w['package_sha256'], dll_sha256=w['dll_sha256'], mvid=w['mvid'], ef_package_version='10.0.12',
        startup_assembly='TP07LiteralProbe', startup_sha256='e' * 64, startup_mvid='fixture-startup', deps_sha256='f' * 64, build_manifest_sha256=startup_sha,
        source_manifest_sha256_binding='b' * 64, lock_manifest_sha256='c' * 64, ef_provider=startup['ef_provider'])
    entries = {}
    for index, (key, (kind, default)) in enumerate(V.table_definitions(cases).items()):
        nonce = 'ABCDEF'[index] * 32
        entry = dict(case_id=key, nonce=nonce, table='TPL_' + nonce[:24], owner_marker='OWN_' + nonce[:24], primary_key='TP07PK_' + nonce[:24],
            shape='Literal', scale=7, literal_store_type='DATETIME(7) WITH TIME ZONE' if kind == 'dto' else 'TIMESTAMP(7)',
            absent_before_create=True, create_acknowledged=True, creation_state='confirmed')
        entry['create_evidence'] = dict(call_returned=True, api='ExecuteNonQueryAsync' if default is not None else 'ExecuteNonQuery', sql_sha256=V.text_sha(V.create_sql(entry, default)))
        entries[key] = entry
    ledger = dict(schema_version=1, profile='shared', package_sha256=w['package_sha256'], identity_sha256=old['candidate_identity_sha256'], objects=list(entries.values()))
    fresh_cleanup = dict(cleanup_verified=True, objects=[dict(table=e['table'], nonce=e['nonce'], fresh_absent=True, fresh_table_count=0, identity=physical()) for e in entries.values()])
    common = dict(schema_version=2, producer_version=2, task=V.TASK, profile='shared', candidate_acceptance=False, full_EF_C_accepted=False,
        literal_support_accepted=False, R04_accepted=False,
        design_sha256=p['design_sha256'], scope='FF7_EXPANSION53_only', execution_role='controlled_direct_SQL_not_EF_HasData_SaveChanges_ExecuteUpdate',
        startup_assembly_name='TP07LiteralProbe', startup_name_compatibility_only=True, expected_only=False, database_accessed=True,
        observer_failed=False, process_exit_code=0, identity_manifest_sha256=old['candidate_identity_sha256'], capabilities_sha256=old['capabilities_sha256'], runtime_identity=runtime)
    main = dict(common, status='expansion_complete', expansion_completed=True, characterization_complete=True, completed_case_count=53,
        physical_profile=physical(), factory_connection=route('connection'), provider=dict(assembly=startup['ef_provider']),
        literal_observation_set_complete=True,
        settings_observation=dict(status='inherited_unknown', source_report_sha256=p['history']['main'], observed_unknown_number=-5504, query_executed=False, fallback_query_attempted=False, mode_inferred=False),
        required_case_ids=[case['case_id'] for case in cases], case_count=53, cases=[], operation_counts=dict(insert=92, update=3, predicate_select=12), finally_cleanup=fresh_cleanup)
    totals = {key: 0 for key in entries}
    for case in cases:
        entry = entries[case['table_key']]; value = dict(case_id=case['case_id'], ordinal=case['ordinal'], context=case['context'], kind=case['kind'], table=entry['table'], operations=[], status='completed', outcome='exact')
        for plan in V.plans(case):
            op = operation(case, plan, entry, totals[case['table_key']]); totals[case['table_key']] = op['row_total_post']; value['operations'].append(op)
        context = case['context']
        if context in ('domain', 'default'): ref = V.reference(case['case_id'], entry['table'], case['typed_row_id'])
        elif context == 'predicate':
            domain = next(c for c in cases if c['case_id'] == case['source_case_id']); ref = V.reference(domain['case_id'], entry['table'], domain['typed_row_id'])
        elif context == 'assignment': ref = V.reference(case['control_case_id'], entry['table'], case['control_row_id'])
        else: ref = V.reference(case['case_id'], entry['table'], case['expected_row_id'])
        value['control_reference'] = ref
        if context in ('assignment', 'dateadd'):
            value['source_control_reference'] = V.reference('F7-V-' + case['kind'].upper() + ('-tail0' if context == 'assignment' else '-tail7'), entry['table'], 1 if context == 'assignment' else 15)
            value['operations'][-1]['control_reference'] = ref
        if context == 'default':
            sql = V.create_sql(entry, case['expected']); value['create'] = dict(sql=sql, sql_sha256=V.text_sha(sql), api='ExecuteNonQueryAsync', call_returned=True, affected_rows=0,
                command_route=route('command'), connection_route=route('connection'), identity=physical())
            query = "SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t AND COLUMN_NAME='V'"; exp = V.expression(case['expected'])
            default_catalog = dict(query=query, query_sha256=V.text_sha(query), table=entry['table'], column='V', rows=1, is_null=False, text_length=len(exp), text_sha256=V.text_sha(exp),
                expected_expression=exp, expected_expression_sha256=V.text_sha(exp), interpretation='pending_hash_only_not_semantic_acceptance')
            value.update(default_catalog_pre=default_catalog, default_catalog_post=copy.deepcopy(default_catalog))
        main['cases'].append(value)
    main['final_row_totals'] = [dict(table=entries[key]['table'], rows=count, identity=physical()) for key, count in totals.items()]
    cleanup = dict(common, status='cleanup_complete', cleanup=copy.deepcopy(fresh_cleanup), finally_cleanup=copy.deepcopy(fresh_cleanup))
    return main, cleanup, ledger, startup, startup_sha


def make_last_loss(main):
    case = V.policy()['cases'][-1]; op = main['cases'][-1]['operations'][-1]
    expected = copy.deepcopy(case['expected']); expected['local_ticks'] -= 7; expected['fields']['nanoseconds'] -= 700
    oracle, cell = handwritten_oracle(expected); op['oracle'] = oracle
    attempt = op['oracle_read_attempts'][0]; attempt.update(metadata=oracle['metadata'], cell=cell, ff9=oracle['ff9'], typed=oracle['typed'])
    mismatch = dict(type='TP07.Runtime.EvidenceException', code='OracleMismatch', number=None)
    op.update(status='failed', outcome='loss', error_stage='value_mismatch', error=copy.deepcopy(mismatch))
    main['cases'][-1].update(status='failed', outcome='loss', failure_stage='dateadd_value_mismatch', error=copy.deepcopy(mismatch))
    main.update(status='execution_failed', process_exit_code=1, expansion_completed=False, characterization_complete=False,
        completed_case_count=52, failed_case_id='F7-A-DT-second1', failed_operation_id='F7-A-DT-second1:dateadd_update', failure=copy.deepcopy(mismatch))


class ExpansionContracts(unittest.TestCase):
    def reject(self, operation):
        with self.assertRaises((ValueError, KeyError, TypeError, OSError)): operation()

    def check(self, main, cleanup, ledger, startup, startup_sha, narrow=False):
        return V.report_contract(main, cleanup, ledger, startup, startup_sha, allow_dateadd_loss=narrow)

    def test_positive53_has_exact92_3_12_counts_and_never_claims_real_EF(self):
        data = reports(); result = self.check(*data)
        self.assertTrue(result['characterization_complete']); self.assertTrue(result['literal_gates_complete']); self.assertTrue(result['r04_gate_complete'])
        self.assertEqual(53, len(result['observations'])); self.assertIn('not_runtime', FIXTURE_ROLE)
        self.assertEqual([39, 45, 2, 2, 2, 2], [row['rows'] for row in data[0]['final_row_totals']])

    def test_independent_integer_boundaries_offsets_NULL_and_exact_FF7_shape(self):
        cases = {case['case_id']: case for case in V.policy()['cases']}
        minimum = cases['F7-V-DT-min_tick']['expected']; maximum = cases['F7-V-DTO-max']['expected']
        self.assertEqual(1, minimum['local_ticks']); self.assertEqual(100, minimum['fields']['nanoseconds'])
        self.assertEqual("TO_TIMESTAMP('0001-01-01 00:00:00.0000001','YYYY-MM-DD HH24:MI:SS.FF7')", V.expression(minimum))
        self.assertEqual("TO_TIMESTAMP_TZ('9999-12-31 23:59:59.9999999 +00:00','YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')", V.expression(maximum))
        self.assertEqual('NULL', V.expression(cases['F7-V-DTO-NULL']['expected']))
        for name, zone in [('offset_m779', '-12:59'), ('offset_p840', '+14:00'), ('offset_m330', '-05:30')]:
            expected = cases['F7-V-DTO-' + name]['expected']; self.assertIn(zone, V.expression(expected)); self.assertEqual(expected['local_ticks'] - expected['offset_minutes'] * 600000000, expected['utc_ticks'])
        self.reject(lambda: V.expected_value('dto', 0, 330)); self.reject(lambda: V.expected_value('dt', V.MAX_TICKS + 1))

    def test_coverage_order_missing_typed_pair_and_cross_table_control_each_fail(self):
        for mutation in ('missing', 'duplicate', 'pair_missing', 'pair_order', 'cross_table', 'old_report'):
            main, cleanup, ledger, startup, startup_sha = reports()
            if mutation == 'missing': main['cases'].pop()
            elif mutation == 'duplicate': main['cases'][-1] = main['cases'][0]
            elif mutation == 'pair_missing': main['cases'][0]['operations'].pop(0)
            elif mutation == 'pair_order': main['cases'][0]['operations'].reverse()
            elif mutation == 'cross_table': main['cases'][0]['control_reference']['table'] = ledger['objects'][1]['table']
            else: main['task'] = 'TP07-constructor-screen'; main['schema_version'] = 1
            self.reject(lambda: self.check(main, cleanup, ledger, startup, startup_sha))

    def test_boundary_tick_loss_raw_FF9_typed_disagreement_and_UTC_equivalence_fail(self):
        for mutation in ('boundary_tick', 'ff9', 'typed', 'offset', 'raw_hash', 'metadata', 'cell_hash'):
            main, cleanup, ledger, startup, startup_sha = reports(); op = main['cases'][30]['operations'][1]
            if mutation == 'boundary_tick':
                expected = copy.deepcopy(V.policy()['cases'][30]['expected']); expected['local_ticks'] -= 1; expected['fields']['nanoseconds'] -= 100
                oracle, cell = handwritten_oracle(expected); op['oracle'] = oracle; op['oracle_read_attempts'][0].update(metadata=oracle['metadata'], cell=cell, ff9=oracle['ff9'], typed=oracle['typed'])
            elif mutation == 'ff9': op['oracle']['ff9'] = op['oracle']['ff9'].replace('9999999', '9999998')
            elif mutation == 'typed': op['oracle']['typed']['ticks'] -= 1
            elif mutation == 'offset': op['oracle']['typed']['offset_minutes'] = 330; op['oracle']['typed']['ticks'] += 198000000000
            elif mutation == 'raw_hash': op['oracle']['raw']['payload_sha256'] = '0' * 64
            elif mutation == 'metadata': op['oracle']['metadata']['raw_scale'] = 6
            else: op['oracle_read_attempts'][0]['cell']['payload_sha256'] = '0' * 64
            self.reject(lambda: self.check(main, cleanup, ledger, startup, startup_sha))

    def test_NULL_cannot_be_zero_ticks_nonempty_cell_or_non_DBNull(self):
        for mutation in ('ticks', 'cell', 'type', 'descriptor', 'constructor'):
            data = reports(); op = data[0]['cases'][17]['operations'][1]
            if mutation == 'ticks': op['oracle']['local_ticks'] = 0
            elif mutation == 'cell': op['oracle_read_attempts'][0]['cell']['payload_length'] = 9
            elif mutation == 'type': op['oracle']['typed'] = {'type': 'System.DateTime', 'ticks': 0}
            elif mutation == 'descriptor': op['oracle']['metadata']['c_type'] = 16
            else: op['sql'] = op['sql'].replace('NULL', "TO_TIMESTAMP('0001-01-01','YYYY-MM-DD')")
            self.reject(lambda: self.check(*data))

    def test_DEFAULT_requires_prior_same_table_typed_control_omitted_V_and_unchanged_catalog(self):
        for mutation in ('explicit_V', 'DDL', 'missing_control', 'catalog_source', 'catalog_drift'):
            data = reports(); case = data[0]['cases'][40]
            if mutation == 'explicit_V': case['operations'][1]['sql'] = 'INSERT INTO ' + case['table'] + '(ID,V) VALUES(2,NULL)'
            elif mutation == 'DDL': case['create']['sql'] = case['create']['sql'].replace('FF7', 'FF9')
            elif mutation == 'missing_control': case['control_reference']['row_id'] = 3
            elif mutation == 'catalog_source': case['default_catalog_pre']['column'] = 'OTHER'
            else: case['default_catalog_post']['text_sha256'] = '0' * 64
            self.reject(lambda: self.check(*data))

    def test_predicate_must_execute_both_one_and_zero_before_actual_updates(self):
        for mutation in ('missing_negative', 'false_scalar', 'parameters', 'wrong_adjacent', 'order'):
            data = reports(); case = data[0]['cases'][44]
            if mutation == 'missing_negative': case['operations'].pop()
            elif mutation == 'false_scalar': case['operations'][1]['scalar_result'] = 1
            elif mutation == 'parameters': case['operations'][1]['parameter_count'] = 1
            elif mutation == 'wrong_adjacent': case['operations'][1]['sql'] = case['operations'][0]['sql']
            else: data[0]['cases'][44], data[0]['cases'][50] = data[0]['cases'][50], data[0]['cases'][44]
            self.reject(lambda: self.check(*data))

    def test_assignment_must_change_the_value_and_retain_source_and_target_controls(self):
        for mutation in ('unchanged', 'wrong_target_ref', 'wrong_source_ref', 'wrong_expression'):
            data = reports(); case = data[0]['cases'][50]
            if mutation == 'unchanged':
                case['operations'][1]['oracle'] = copy.deepcopy(case['operations'][0]['oracle']); case['operations'][1]['oracle_read_attempts'] = copy.deepcopy(case['operations'][0]['oracle_read_attempts'])
            elif mutation == 'wrong_target_ref': case['operations'][1]['control_reference']['row_id'] = 1
            elif mutation == 'wrong_source_ref': case['source_control_reference']['row_id'] = 15
            else: case['operations'][1]['sql'] = case['operations'][1]['sql'].replace('FF7', 'FF9')
            self.reject(lambda: self.check(*data))

    def test_only_last_fully_observed_DATEADD_loss_has_separate_failed_R04_verdict(self):
        data = reports(); make_last_loss(data[0]); result = self.check(*data, narrow=True)
        self.assertTrue(result['literal_gates_complete']); self.assertFalse(result['r04_gate_complete']); self.assertFalse(result['characterization_complete'])
        self.assertEqual(['R04_DATEADD_value_mismatch'], result['issues']); self.reject(lambda: self.check(*data))
        for mutation in ('earlier_loss', 'missing_expected_control', 'infra', 'wrong_stage', 'false_success', 'missing_cleanup'):
            bad = reports(); make_last_loss(bad[0]); case = bad[0]['cases'][-1]
            if mutation == 'earlier_loss': bad[0]['cases'][0]['operations'][1]['outcome'] = 'loss'
            elif mutation == 'missing_expected_control': case['operations'].pop(1)
            elif mutation == 'infra': case['operations'][-1]['outcome'] = 'unobservable'
            elif mutation == 'wrong_stage': case['failure_stage'] = 'transport_failure'
            elif mutation == 'false_success': bad[0]['process_exit_code'] = 0
            else: bad[1]['cleanup']['objects'].pop()
            self.reject(lambda: self.check(*bad, narrow=True))

    def test_preserved_loss_requires_exact_actual_OracleMismatch_at_all_three_error_levels(self):
        invalid_types = ('W.Dm.DmException', 'W.Dm.DmTimeoutException', 'W.Dm.DmOperationCanceledException',
            'System.OperationCanceledException', 'System.ObjectDisposedException', 'System.Reflection.TargetInvocationException')
        for level in ('operation', 'case', 'main'):
            for mutation in ('missing', 'null', 'code', 'number', 'wrapper') + invalid_types:
                with self.subTest(level=level, mutation=mutation):
                    data = reports(); make_last_loss(data[0]); main = data[0]
                    holder = main['cases'][-1]['operations'][-1] if level == 'operation' else main['cases'][-1] if level == 'case' else main
                    key = 'failure' if level == 'main' else 'error'
                    if mutation == 'missing': del holder[key]
                    elif mutation == 'null': holder[key] = None
                    elif mutation == 'code': holder[key]['code'] = 'CleanupFailed'
                    elif mutation == 'number': holder[key]['number'] = 10082
                    elif mutation == 'wrapper': holder[key]['inner'] = copy.deepcopy(holder[key])
                    else: holder[key]['type'] = mutation
                    # Actual value loss and stage labels deliberately remain valid.
                    self.assertEqual('loss', main['cases'][-1]['operations'][-1]['outcome'])
                    self.assertEqual('value_mismatch', main['cases'][-1]['operations'][-1]['error_stage'])
                    self.reject(lambda: self.check(*data, narrow=True))

    def test_last_failure_IDs_honest_52_counts_and_full_53_completion_are_required(self):
        for key, invalid in [('failed_case_id', 'F7-U-DT'), ('failed_operation_id', 'F7-A-DT-second1:typed_expected'),
                ('completed_case_count', 53), ('completed_case_count', 51), ('completed_case_count', True),
                ('characterization_complete', True), ('expansion_completed', True), ('process_exit_code', 0), ('process_exit_code', True)]:
            with self.subTest(key=key, invalid=invalid):
                data = reports(); make_last_loss(data[0]); data[0][key] = invalid
                self.reject(lambda: self.check(*data, narrow=True))
        for key in ('failed_case_id', 'failed_operation_id', 'completed_case_count', 'characterization_complete'):
            data = reports(); make_last_loss(data[0]); del data[0][key]
            self.reject(lambda: self.check(*data, narrow=True))
        for mutation in ('case_count', 'completion_flag', 'main_error', 'failure_ID', 'operation_error', 'case_error', 'cleanup_error'):
            data = reports()
            if mutation == 'case_count': data[0]['completed_case_count'] = 52
            elif mutation == 'completion_flag': data[0]['characterization_complete'] = False
            elif mutation == 'main_error': data[0]['failure'] = dict(type='TP07.Runtime.EvidenceException', code='OracleMismatch', number=None)
            elif mutation == 'failure_ID': data[0]['failed_operation_id'] = 'F7-A-DT-second1:dateadd_update'
            elif mutation == 'operation_error': data[0]['cases'][-1]['operations'][-1]['error'] = None
            elif mutation == 'case_error': data[0]['cases'][-1]['error'] = None
            else: data[0]['cleanup_error'] = None
            self.reject(lambda: self.check(*data))
        data = reports(); make_last_loss(data[0]); data[0]['cleanup_error'] = dict(type='W.Dm.DmTimeoutException', code=None, number=0)
        self.reject(lambda: self.check(*data, narrow=True))

    def test_cleanup_six_tables_source_DLL_identity_counts_and_unknown_settings_remain_required(self):
        for mutation in ('cleanup_missing', 'fresh_present', 'source_identity', 'old_DLL', 'counts', 'settings', 'fake_HasData'):
            data = reports()
            if mutation == 'cleanup_missing': data[1]['cleanup']['objects'].pop()
            elif mutation == 'fresh_present': data[0]['finally_cleanup']['objects'][0]['fresh_table_count'] = 1
            elif mutation == 'source_identity': data[0]['runtime_identity']['source_manifest_sha256_binding'] = '0' * 64
            elif mutation == 'old_DLL': data[0]['runtime_identity']['startup_sha256'] = '0' * 64
            elif mutation == 'counts': data[0]['operation_counts']['insert'] = 91
            elif mutation == 'settings': data[0]['settings_observation']['mode_inferred'] = True
            else: data[0]['execution_role'] = 'EF_HasData'
            self.reject(lambda: self.check(*data))

    def test_source_pack14_and_phase_checks_reject_every_source_or_helper_drift(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'source'
            for name in V.PACK_FILES:
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(V.ROOT / name, path)
            manifest = Path(folder) / 'pack.json'; hashes = {name: V.sha(root / name) for name in V.PACK_FILES}
            manifest.write_text(json.dumps(dict(schema_version=1, task=V.TASK + ' source pack', source_hashes=hashes)))
            spec = importlib.util.spec_from_file_location('ff7_frozen_fixture', root / 'tools/DownstreamAcceptance/TP07/constructor-probe/validate.py')
            module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); identity, before = module.pack_entry(manifest, V.sha(manifest))
            for name in V.PACK_FILES:
                path = root / name; original = path.read_bytes(); path.write_bytes(original + b'\n')
                self.reject(lambda: module.pack_hashes(identity)); path.write_bytes(original)
            good = dict(source_pack=identity, source_pack_check=dict(phase='build', before=before, after=before))
            module.pack_binding(good, identity, 'build'); bad = copy.deepcopy(good); bad['source_pack_check']['phase'] = 'lock'
            self.reject(lambda: module.pack_binding(bad, identity, 'build'))
            old = json.loads(manifest.read_text()); old['task'] = 'TP07-constructor-screen source pack'; manifest.write_text(json.dumps(old))
            self.reject(lambda: module.pack_entry(manifest, V.sha(manifest)))

    def test_SDK_proof_and_complete_pinned_prior_SCREEN25_history_are_required(self):
        self.reject(lambda: V.validate_history({}))
        refs = {name: dict(path='/absent-fixture', sha256='0' * 64) for name in V.policy()['history']}
        self.reject(lambda: V.validate_history(refs))
        with tempfile.TemporaryDirectory() as folder:
            host = str(Path.home() / '.dotnet/dotnet'); flags = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']
            commands = [[host, '--version'], [host, 'restore', V.PROJECT, '--locked-mode', '--disable-parallel'] + flags, [host, 'build', V.PROJECT, '--no-restore'] + flags]
            rows = []
            for index, command in enumerate(commands):
                log = Path(folder) / (str(index) + '.log'); log.write_text('10.0.401\n' if index == 0 else 'fixture command output\n')
                rows.append(dict(command=command, exit_code=0, timed_out=False, termination_code=None, log_path=str(log), log_sha256=V.sha(log)))
            good = dict(SDK_version='10.0.401', commands=rows); V.sdk_receipt(good, 'build')
            for mutation in ('version', 'flags', 'locked', 'exit', 'log'):
                bad = copy.deepcopy(good)
                if mutation == 'version': bad['SDK_version'] = '10.0.400'
                elif mutation == 'flags': bad['commands'][1]['command'].remove('/nodeReuse:false')
                elif mutation == 'locked': bad['commands'][1]['command'].remove('--locked-mode')
                elif mutation == 'exit': bad['commands'][2]['exit_code'] = 1
                else: bad['commands'][2]['log_sha256'] = '0' * 64
                self.reject(lambda: V.sdk_receipt(bad, 'build'))


if __name__ == '__main__': unittest.main()
