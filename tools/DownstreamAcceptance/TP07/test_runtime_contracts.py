"""Handwritten synthetic contract inputs; never runtime/package/DB acceptance evidence."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
from types import SimpleNamespace
import unittest
from unittest import mock
import tempfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('tp07_runtime_validator', HERE / 'runtime-validate.py')
V = importlib.util.module_from_spec(spec)
spec.loader.exec_module(V)
FIXTURE_ROLE = 'handwritten_contract_fixture_not_runtime_or_DB_acceptance'


def physical(name='shared'):
    version, message, encoding = ('8.1.5.60', 21, 'utf-8') if name == 'shared' else ('8.1.4.6', 11, 'gb18030')
    return dict(profile=name, server_version=version, message_version=message, encoding=encoding,
        negotiated_encoding=encoding, encoding_override_empty=True, profile_origin='physical_get_conn_instance_conn_property',
        server_user='WDM_PROVIDER_TEST', server_schema='WDM_PROVIDER_TEST', result_format='ordinary_non_BDTA')


def route(role):
    names = {'reader': 'DmDataReader', 'connection': 'DmConnection', 'command': 'DmCommand', 'parameter': 'DmParameter'}
    return dict(role=role, type='W.Dm.' + names[role], dll_sha256=V.policy()['W']['dll_sha256'], mvid=V.policy()['W']['mvid'])


def handwritten_raw(case):
    """Test-only bit arithmetic, independent of producer/provider; not a production encoder."""
    f = case['expected_fields']; extended = case['parameter_scale'] == 7
    date = f['year'] | (f['month'] << 15) | (f['day'] << 19)
    fraction = f['nanoseconds'] if extended else f['nanoseconds'] // 1000
    time = f['hour'] | (f['minute'] << 5) | (f['second'] << 11) | (fraction << 17)
    raw = date.to_bytes(3, 'little') + time.to_bytes(6 if extended else 5, 'little')
    if case['kind'] == 'dto': raw += case['expected_offset_minutes'].to_bytes(2, 'little', signed=True)
    return raw


def oracle(case):
    raw = handwritten_raw(case)
    ctype = (27 if case['kind'] == 'dto' else 26) if case['parameter_scale'] == 7 else (23 if case['kind'] == 'dto' else 16)
    typed = dict(type='System.DateTimeOffset' if case['kind'] == 'dto' else 'System.DateTime', ticks=case['expected_local_ticks'])
    if case['kind'] == 'dto': typed.update(offset_minutes=330, utc_ticks=case['expected_utc_ticks'])
    else: typed['kind'] = 'Unspecified'
    return dict(rows=1, is_null=False, local_ticks=case['expected_local_ticks'], offset_minutes=case['expected_offset_minutes'],
        identity=physical(), reader_route=route('reader'), typed=typed,
        ff9='2026-07-23 14:15:16.' + str(case['expected_fields']['nanoseconds']).zfill(9) + (' +05:30' if case['kind'] == 'dto' else ''),
        metadata=dict(c_type=ctype, width=len(raw), scale=case['parameter_scale'], mask=0, is_bdta=False,
            metadata_role='result_column', schema_scale=case['parameter_scale'], raw_source='ResultColumn',
            raw_scale_status='captured_server_source', raw_c_type=ctype, raw_precision=len(raw),
            raw_scale=case['parameter_scale'], raw_mask_before=0, raw_derived_mask=0),
        raw=dict(source='ordinary_result_cache_before_temporal_projection', capture_stage='before_any_temporal_getter',
            c_type=ctype, width=len(raw), scale=case['parameter_scale'], is_bdta=False, hex=raw.hex().upper(),
            payload_length=len(raw), payload_sha256=hashlib.sha256(raw).hexdigest(), decoded=case['expected_fields']))


def frozen_toolpack(folder):
    """Copy only the finite source closure; tests never invoke SDK or wrappers."""
    root = Path(folder) / 'source'
    for name in V.TOOLPACK_FILES:
        path = root / name; path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(V.ROOT / name, path)
    hashes = {name: V.sha(root / name) for name in V.TOOLPACK_FILES}
    manifest = Path(folder) / 'toolpack-manifest.json'
    manifest.write_text(json.dumps(dict(schema_version=1, task='TP07-L0 toolpack', source_hashes=hashes), sort_keys=True))
    modules = []
    for name, filename in [('runner', 'runtime-run.py'), ('validator', 'runtime-validate.py')]:
        spec = importlib.util.spec_from_file_location('tp07_contract_' + name, root / 'tools/DownstreamAcceptance/TP07' / filename)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); modules.append(module)
    args = SimpleNamespace(toolpack_manifest=str(manifest), toolpack_manifest_sha256=V.sha(manifest))
    return root, manifest, args, modules[0], modules[1]


class ToolpackContracts(unittest.TestCase):
    def test_finite_pack_positive_and_every_source_drift_stops_all_operation_entries(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            identity, before = runner.toolpack_entry(args)
            self.assertEqual((identity, before), validator.toolpack_entry(manifest, args.toolpack_manifest_sha256))
            self.assertEqual(set(V.TOOLPACK_FILES), set(V.policy()['toolpack_contract']['source_files']))
            for name in V.TOOLPACK_FILES:
                with self.subTest(tool=name):
                    path = root / name; original = path.read_bytes(); path.write_bytes(original + b'\n# drift\n')
                    for operation in (runner.prepare, runner.offline, runner.run_runtime, runner.internal_host):
                        with mock.patch.object(runner, 'process') as process, mock.patch.object(runner.subprocess, 'call') as child:
                            with self.assertRaisesRegex(runner.Stop, 'executed_toolpack_drift'): operation(args)
                            process.assert_not_called(); child.assert_not_called()
                    with self.assertRaisesRegex(validator.ContractError, 'executed_toolpack_drift'):
                        validator.validate_artifact(Path(folder) / 'absent-run', '0' * 64, manifest, args.toolpack_manifest_sha256)
                    path.write_bytes(original)

    def test_omitted_extra_or_stale_manifest_cannot_bootstrap_itself(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            original = manifest.read_bytes()
            for mutation in ('omitted', 'extra', 'stale'):
                with self.subTest(mutation=mutation):
                    document = json.loads(original)
                    if mutation == 'omitted': document['source_hashes'].pop(V.TOOLPACK_FILES[-2])
                    elif mutation == 'extra': document['source_hashes']['tools/arbitrary-helper.py'] = '0' * 64
                    else: document['source_hashes'][V.TOOLPACK_FILES[0]] = '0' * 64
                    manifest.write_text(json.dumps(document))
                    # Re-pairing a new manifest with the old externally approved SHA fails.
                    with self.assertRaisesRegex(runner.Stop, 'approved_toolpack_manifest_pin'): runner.toolpack_entry(args)
                    with self.assertRaisesRegex(validator.ContractError, 'approved_toolpack_manifest_pin'):
                        validator.toolpack_entry(manifest, args.toolpack_manifest_sha256)
                    new_args = SimpleNamespace(toolpack_manifest=str(manifest), toolpack_manifest_sha256=V.sha(manifest))
                    with self.assertRaises(runner.Stop): runner.toolpack_entry(new_args)
                    with self.assertRaises(validator.ContractError): validator.toolpack_entry(manifest, new_args.toolpack_manifest_sha256)
            manifest.write_bytes(original)

    def test_cross_phase_pack_identity_and_incomplete_checks_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            identity, before = runner.toolpack_entry(args)
            for phase in ('prepare', 'lock', 'build', 'run', 'internal-host', 'validate'):
                good = dict(toolpack=identity, toolpack_check=runner.toolpack_exit(identity, before, phase))
                validator.toolpack_binding(good, identity, phase)
                for mutation in ('other_manifest', 'other_phase', 'missing_tool', 'after_drift'):
                    with self.subTest(phase=phase, mutation=mutation):
                        bad = copy.deepcopy(good)
                        if mutation == 'other_manifest': bad['toolpack']['manifest_path'] += '.other-approved-pack'
                        elif mutation == 'other_phase': bad['toolpack_check']['phase'] = 'stale-' + phase
                        elif mutation == 'missing_tool': bad['toolpack_check']['before'].pop(V.TOOLPACK_FILES[0])
                        else: bad['toolpack_check']['after'][V.TOOLPACK_FILES[0]] = '0' * 64
                        with self.assertRaises(runner.Stop): runner.toolpack_binding(bad, identity, phase)
                        with self.assertRaises(validator.ContractError): validator.toolpack_binding(bad, identity, phase)

    def test_phase_exit_and_after_run_validator_reject_all_tool_or_manifest_drift(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            identity, before = runner.toolpack_entry(args)
            for path in [root / name for name in V.TOOLPACK_FILES] + [manifest]:
                with self.subTest(path=str(path.relative_to(folder))):
                    original = path.read_bytes(); path.write_bytes(original + b'\n')
                    for phase in ('prepare', 'lock', 'build', 'run', 'internal-host'):
                        with self.assertRaises(runner.Stop): runner.toolpack_exit(identity, before, phase)
                    with self.assertRaises(validator.ContractError): validator.toolpack_exit(identity, before, 'validate')
                    with self.assertRaises(validator.ContractError):
                        validator.validate_artifact(Path(folder) / 'absent-run', '0' * 64, manifest, args.toolpack_manifest_sha256)
                    path.write_bytes(original)

    def test_phase_receipt_hash_source_and_toolpack_cannot_be_repaired_independently(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            identity, before = runner.toolpack_entry(args); source_sha = 'a' * 64
            path = Path(folder) / 'lock-receipt.json'
            good = dict(source_manifest_sha256=source_sha, toolpack=identity, toolpack_check=runner.toolpack_exit(identity, before, 'lock'))
            runner.write(path, good); reference = runner.artifact(path)
            validator.phase_receipt(reference, identity, 'lock', source_sha)
            for mutation in ('hash', 'source', 'phase', 'pack'):
                bad = copy.deepcopy(good)
                if mutation == 'source': bad['source_manifest_sha256'] = 'b' * 64
                elif mutation == 'phase': bad['toolpack_check']['phase'] = 'build'
                elif mutation == 'pack': bad['toolpack']['manifest_sha256'] = 'c' * 64
                else: bad['toolpack_check']['after'][V.TOOLPACK_FILES[-1]] = 'd' * 64
                runner.write(path, bad)
                ref = reference if mutation == 'hash' else runner.artifact(path)
                with self.assertRaises(runner.Stop): runner.phase_receipt(ref, identity, 'lock', source_sha)
                with self.assertRaises(validator.ContractError): validator.phase_receipt(ref, identity, 'lock', source_sha)

    def test_internal_dotnet_environment_is_task_local_and_contains_only_selected_TEST_variable(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            for name in ('shared', 'tls'):
                args.profile = name; args.candidate_manifest = '/fixture/candidate'; args.capabilities = '/fixture/caps'
                args.secret_repo = '/fixture/repo'; args.startup_manifest = '/fixture/startup'; args.startup_manifest_sha256 = 'a' * 64
                parent = dict(PATH='/bin', HOME=folder, DAMENG_TEST_CONNECTION_STRING='shared-fixture',
                    DAMENG_TLS_TEST_CONNECTION_STRING='tls-fixture', DAMENG_ADMIN_CONNECTION_STRING='must-not-inherit',
                    DOTNET_CLI_HOME='/wrong/home', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='0', DOTNET_CLI_TELEMETRY_OPTOUT='0')
                with mock.patch.dict(os.environ, parent, clear=True): env = runner.internal_environment(args, Path(folder) / name)
                self.assertEqual('tls-fixture' if name == 'tls' else 'shared-fixture', env['DAMENG_TEST_CONNECTION_STRING'])
                self.assertNotIn('DAMENG_TLS_TEST_CONNECTION_STRING', env); self.assertNotIn('DAMENG_ADMIN_CONNECTION_STRING', env)
                self.assertEqual('1', env['DOTNET_SKIP_FIRST_TIME_EXPERIENCE']); self.assertEqual('1', env['DOTNET_CLI_TELEMETRY_OPTOUT'])
                self.assertEqual(Path(folder) / name / 'cli-home', Path(env['DOTNET_CLI_HOME']))
                self.assertTrue(Path(env['DOTNET_CLI_HOME']).is_dir())

    def test_internal_host_supervises_success_failure_and_requires_final_source_check(self):
        with tempfile.TemporaryDirectory() as folder:
            root, manifest, args, runner, validator = frozen_toolpack(folder)
            identity, before = runner.toolpack_entry(args)
            startup = Path(folder) / 'startup.json'
            runner.write(startup, dict(toolpack=identity, toolpack_check=runner.toolpack_exit(identity, before, 'build')))
            args.startup_manifest = str(startup); args.startup_manifest_sha256 = runner.sha(startup)
            args.profile = 'shared'; args.cleanup = False; args.dll = '/fixture/host.dll'
            args.candidate_manifest = '/fixture/candidate'; args.capabilities = '/fixture/caps'; args.secret_repo = '/fixture/repo'
            for code in (0, 1):
                args.consumer_output = str(Path(folder) / ('child-' + str(code)))
                with mock.patch.dict(os.environ, {'DAMENG_TEST_CONNECTION_STRING': 'test-fixture'}, clear=True), mock.patch.object(runner.subprocess, 'call', return_value=code) as child:
                    self.assertEqual(code, runner.internal_host(args))
                self.assertEqual({'env'}, set(child.call_args.kwargs))  # No new session/process group.
                receipt = runner.read(Path(args.consumer_output) / 'toolpack-host-receipt.json')
                validator.toolpack_binding(receipt, identity, 'internal-host')
                self.assertEqual(code, receipt['child_exit_code'])
            args.consumer_output = str(Path(folder) / 'child-source-drift')
            def drift(*command, **kwargs):
                path = root / V.TOOLPACK_FILES[-2]; path.write_bytes(path.read_bytes() + b'\n# after-child drift\n')
                return 0
            with mock.patch.dict(os.environ, {'DAMENG_TEST_CONNECTION_STRING': 'test-fixture'}, clear=True), mock.patch.object(runner.subprocess, 'call', side_effect=drift):
                with self.assertRaisesRegex(runner.Stop, 'executed_toolpack_drift'): runner.internal_host(args)
            self.assertFalse((Path(args.consumer_output) / 'toolpack-host-receipt.json').exists())


class L0Contracts(unittest.TestCase):
    def setUp(self):
        self.policy = V.policy()
        self.case = next(row for row in self.policy['cases'] if row['case_id'] == 'LIT-DT7-tick1')
        self.value = oracle(self.case)

    def reject(self, operation):
        with self.assertRaises((V.ContractError, KeyError, ValueError)): operation()

    def test_manifest_case_scope_is_exact_and_independent(self):
        for name, count in [('shared', 12), ('tls', 2)]:
            rows = [row for row in self.policy['cases'] if name in row['profiles']]
            self.assertEqual(count, len(rows)); self.assertEqual(count, len({row['case_id'] for row in rows}))
        self.assertEqual(639204129160000001, self.case['expected_local_ticks'])
        self.assertEqual(100, self.case['expected_fields']['nanoseconds'])

    def test_handwritten_exact_tick_control_is_valid_only_as_a_contract_input(self):
        self.assertEqual('EA87BBEE81C8000000', self.value['raw']['hex'])
        self.assertEqual({'c_type': 26, 'width': 9, 'scale': 7}, V.oracle(self.value, self.case, 'shared'))
        self.assertIn('not_runtime', FIXTURE_ROLE)

    def test_producer_decoded_expected_fields_cannot_hide_raw_loss(self):
        damaged = copy.deepcopy(self.value)
        raw = bytearray.fromhex(damaged['raw']['hex']); raw[5] = 0; raw[6] = 0
        damaged['raw']['hex'] = raw.hex().upper(); damaged['raw']['payload_sha256'] = hashlib.sha256(raw).hexdigest()
        damaged['raw']['decoded'] = self.case['expected_fields']  # Producer still claims correct fields.
        self.reject(lambda: V.oracle(damaged, self.case, 'shared'))

    def test_typed_and_ff9_each_must_independently_match_requested_input(self):
        for field in ('typed', 'ff9', 'local_ticks'):
            damaged = copy.deepcopy(self.value)
            if field == 'typed': damaged[field]['ticks'] -= 1
            elif field == 'ff9': damaged[field] = damaged[field][:-3] + '000'
            else: damaged[field] -= 1
            self.reject(lambda: V.oracle(damaged, self.case, 'shared'))

    def test_hash_width_mask_and_unknown_layout_are_fail_closed(self):
        for mutation in ('hash', 'width', 'mask', 'type', 'bdta', 'scale'):
            damaged = copy.deepcopy(self.value)
            if mutation == 'hash': damaged['raw']['payload_sha256'] = '0' * 64
            elif mutation == 'width': damaged['metadata']['width'] = 8
            elif mutation == 'mask': damaged['metadata']['mask'] = 4
            elif mutation == 'type': damaged['metadata']['c_type'] = damaged['raw']['c_type'] = 99
            elif mutation == 'bdta': damaged['metadata']['is_bdta'] = True
            else: damaged['metadata']['scale'] = damaged['raw']['scale'] = 6
            self.reject(lambda: V.oracle(damaged, self.case, 'shared'))

    def test_offset_retains_local_ticks_not_only_utc_equivalence(self):
        case = next(row for row in self.policy['cases'] if row['case_id'] == 'LIT-DTO7-tick1')
        value = oracle(case); V.oracle(value, case, 'shared')
        value['typed']['offset_minutes'] = 0
        value['typed']['ticks'] = case['expected_utc_ticks']
        self.reject(lambda: V.oracle(value, case, 'shared'))

    def test_physical_identity_cannot_use_logical_default_or_wrong_TEST_schema(self):
        for key, invalid in [('server_schema', 'SYSDBA'), ('encoding', 'gb18030'), ('encoding_override_empty', False), ('profile_origin', 'logical')]:
            value = physical(); value[key] = invalid
            self.reject(lambda: V.profile(value, 'shared'))

    def test_cleanup_partial_duplicate_and_fresh_presence_each_fail(self):
        entry = {'table': 'TPL_' + 'A' * 24, 'nonce': 'A' * 32}
        ledger = {'objects': [entry]}
        good = dict(cleanup_verified=True, objects=[dict(**entry, fresh_absent=True, fresh_table_count=0, identity=physical())])
        V.cleanup(good, ledger, 'shared')
        for mutation in ('missing', 'duplicate', 'present', 'nonce'):
            bad = copy.deepcopy(good)
            if mutation == 'missing': bad['objects'] = []
            elif mutation == 'duplicate': bad['objects'] *= 2
            elif mutation == 'present': bad['objects'][0]['fresh_table_count'] = 1
            else: bad['objects'][0]['nonce'] = 'B' * 32
            self.reject(lambda: V.cleanup(bad, ledger, 'shared'))

    def test_case_precision_and_bare_rejection_do_not_share_supported_seven_claim(self):
        bare = next(row for row in self.policy['cases'] if row['case_id'] == 'LIT-BARE-timestamp-tick1')
        self.assertTrue(bare['expected_local_overflow']); self.assertEqual(6, bare['parameter_scale'])
        self.assertEqual('observed_characterization_unverified', bare['catalog_mode'])
        self.assertEqual('not_activated_pending_L0', self.policy['future_groups']['D'])

    def test_credentials_and_duplicate_json_are_rejected(self):
        self.reject(lambda: V.no_credentials({'message': 'PWD=DO_NOT_RECORD'}))
        self.reject(lambda: V._pairs([('cases', []), ('cases', [])]))

    def test_missing_duplicate_extra_and_partial_case_sets_fail(self):
        ids = [row['case_id'] for row in self.policy['cases'] if 'shared' in row['profiles']]
        good = dict(cases=[dict(case_id=name) for name in ids], required_case_ids=ids, case_count=12)
        V.required_cases(good, 'shared')
        for mutation in ('missing', 'duplicate', 'extra', 'partial_count'):
            bad = copy.deepcopy(good)
            if mutation == 'missing': bad['cases'].pop()
            elif mutation == 'duplicate': bad['cases'][-1] = bad['cases'][0]
            elif mutation == 'extra': bad['cases'][-1] = {'case_id': 'UNAPPROVED'}
            else: bad['case_count'] = 11
            self.reject(lambda: V.required_cases(bad, 'shared'))

    def test_source_and_lock_drift_paths_fail_without_any_runtime_execution(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'packages.lock.json'; path.write_bytes(b'fixed-lock')
            frozen = {'packages.lock.json': hashlib.sha256(path.read_bytes()).hexdigest()}
            V.source_closure(folder, frozen)
            path.write_bytes(b'drift')
            self.reject(lambda: V.source_closure(folder, frozen))
            self.reject(lambda: V.source_closure(folder, {'../escape': '0' * 64}))

    def test_actual_provider_and_startup_identity_each_fail_on_drift(self):
        w = self.policy['W']; startup_sha = 'a' * 64
        startup = dict(source_manifest_sha256='b' * 64, lock_manifest_sha256='c' * 64,
            ef_provider=dict(dll_sha256='d' * 64, mvid='EF_SYNTHETIC_MVID'),
            startup_assemblies=[dict(name='TP07LiteralProbe', dll_sha256='e' * 64, mvid='HOST_SYNTHETIC_MVID', deps_sha256='f' * 64)])
        value = dict(version=w['version'], package_sha256=w['package_sha256'], dll_sha256=w['dll_sha256'], mvid=w['mvid'],
            ef_package_version='10.0.12', startup_assembly='TP07LiteralProbe', startup_sha256='e' * 64,
            startup_mvid='HOST_SYNTHETIC_MVID', deps_sha256='f' * 64, build_manifest_sha256=startup_sha,
            source_manifest_sha256_binding='b' * 64, lock_manifest_sha256='c' * 64, ef_provider=startup['ef_provider'])
        V.runtime_identity(value, startup, startup_sha)
        for key in ('mvid', 'startup_sha256', 'build_manifest_sha256', 'lock_manifest_sha256', 'ef_provider'):
            bad = copy.deepcopy(value)
            bad[key] = {'dll_sha256': '0' * 64, 'mvid': 'OTHER'} if key == 'ef_provider' else 'DRIFT'
            self.reject(lambda: V.runtime_identity(bad, startup, startup_sha))

    def test_raw_descriptor_source_fields_and_schema_are_not_boolean_flags(self):
        for key, invalid in [('raw_source', 'Synthetic'), ('raw_c_type', 16), ('raw_precision', 8),
                ('raw_scale', 6), ('raw_mask_before', 8192), ('raw_derived_mask', 4), ('schema_scale', 6)]:
            bad = copy.deepcopy(self.value); bad['metadata'][key] = invalid
            self.reject(lambda: V.oracle(bad, self.case, 'shared'))

    def test_six_digit_literal_prefix_is_not_exact_literal_evidence(self):
        case = next(row for row in self.policy['cases'] if row['case_id'] == 'LIT-DT6-micro')
        literal = "TIMESTAMP '2026-07-23 14:15:16.123456'"
        table = 'TPL_' + 'A' * 24
        value = dict(case_id=case['case_id'], bare_characterization=False, status='observed_exact',
            expected=dict(type='System.DateTime', ticks=case['expected_local_ticks']), owned_table=table,
            mapping=dict(mapping_precision=6, store_type='TIMESTAMP(6)'), generated_literal_match=True,
            catalog=[dict(name='ID', data_type='INT'), dict(name='V', data_type='TIMESTAMP', scale=6), dict(name='OWN_' + 'A' * 24, data_type='VARCHAR')],
            catalog_contract='explicit_mapping_declaration_observed', literal_affected_rows=1,
            generated_literal=literal, generated_literal_sha256=hashlib.sha256(literal.encode()).hexdigest(),
            persisted_exact=True, ado_control_exact=True, literal_oracle=oracle(case), ado_control_oracle=oracle(case),
            literal_command=route('command'), ado_control_route=dict(command=route('command'), parameter=route('parameter'),
                scale=6, precision=0, evidence_role='ado_control_not_EF_SaveChanges'),
            steps=[dict(step_id='GenerateSqlLiteral', status='completed'), dict(step_id='PersistGeneratedLiteral', status='completed')],
            oracle_read_attempts=[dict(table=table, row_id=row, rows=1, completed=True) for row in (1, 2)])
        V.case(value, case, 'shared')
        for bad_literal in ("TIMESTAMP '2026-07-23 14:15:16.1234567'", "TIMESTAMP '2026-07-23 14:15:16.1234560'",
                "TIMESTAMP '2026-07-23 14:15:16.12345'", literal + ' ', literal.replace('2026', '2025')):
            bad = copy.deepcopy(value); bad['generated_literal'] = bad_literal
            bad['generated_literal_sha256'] = hashlib.sha256(bad_literal.encode()).hexdigest()
            self.reject(lambda: V.case(bad, case, 'shared'))


def ff7_case_value(case):
    fields = case['expected_fields']; clock = '2026-07-23 14:15:16.' + str(fields['nanoseconds']).zfill(9)[:7]
    literal = ("TO_TIMESTAMP_TZ('" + clock + " +05:30', 'YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')") if case['kind'] == 'dto' else ("TO_TIMESTAMP('" + clock + "', 'YYYY-MM-DD HH24:MI:SS.FF7')")
    table = 'TPL_' + 'A' * 24; expected = dict(type='System.DateTimeOffset' if case['kind'] == 'dto' else 'System.DateTime', ticks=case['expected_local_ticks'])
    if case['kind'] == 'dto': expected.update(offset_minutes=330, utc_ticks=case['expected_utc_ticks'])
    return dict(case_id=case['case_id'], bare_characterization=False, status='observed_exact', expected=expected, owned_table=table,
        mapping=dict(mapping_precision=7, store_type=case['store_type']), generated_literal_match=True,
        catalog=[dict(name='ID', data_type='INT'), dict(name='V', data_type=case['store_type'], scale=7), dict(name='OWN_' + 'A' * 24, data_type='VARCHAR')],
        catalog_contract='explicit_mapping_declaration_observed', literal_affected_rows=1, generated_literal=literal,
        generated_literal_sha256=hashlib.sha256(literal.encode()).hexdigest(), persisted_exact=True, ado_control_exact=True,
        literal_oracle=oracle(case), ado_control_oracle=oracle(case), literal_command=route('command'),
        ado_control_route=dict(command=route('command'), parameter=route('parameter'), scale=7, precision=0, evidence_role='ado_control_not_EF_SaveChanges'),
        steps=[dict(step_id='GenerateSqlLiteral', status='completed'), dict(step_id='PersistGeneratedLiteral', status='completed')],
        oracle_read_attempts=[dict(table=table, row_id=row, rows=1, completed=True) for row in (1, 2)])


class FF7AdapterContracts(unittest.TestCase):
    def test_four_declared_seven_cases_require_full_FF7_only(self):
        cases = [case for case in V.policy()['cases'] if case['parameter_scale'] == 7]
        self.assertEqual(4, len(cases))
        for case in cases:
            with self.subTest(case=case['case_id']):
                value = ff7_case_value(case); V.case(value, case, 'shared')
                literal = value['generated_literal']; clock = '2026-07-23 14:15:16.' + str(case['expected_fields']['nanoseconds']).zfill(9)[:7]
                old = "TIMESTAMP '" + clock + ('+05:30' if case['kind'] == 'dto' else '') + "'"
                bad_literals = [old, literal.replace('FF7', 'FF9'), literal.replace(clock, clock[:-1]), literal.replace(clock, clock + '0'),
                    literal.replace('2026-', '2025-'), literal.replace(', ', ','), literal.replace(', ', ',  '),
                    literal.replace('HH24', 'HH12'), literal + '; SELECT 1 FROM DUAL', literal + ' -- suffix',
                    'CAST(' + literal + ' AS TIMESTAMP(7))']
                if case['kind'] == 'dto': bad_literals += [literal.replace('+05:30', '+00:00'), literal.replace(' +05:30', '+05:30'),
                    literal.replace(' +05:30', '  +05:30'), literal.replace('TZH:TZM', 'TZR')]
                for changed in bad_literals:
                    bad = copy.deepcopy(value); bad['generated_literal'] = changed; bad['generated_literal_sha256'] = hashlib.sha256(changed.encode()).hexdigest()
                    with self.assertRaisesRegex(V.ContractError, 'independent_generated_literal'): V.case(bad, case, 'shared')

    def test_actual_CSharp_grammar_artifact_is_bound_and_cannot_be_synthetic_flags_only(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); binary = root / 'replay/test/TP07LiteralProbe/bin/Debug/net10.0/TP07LiteralProbe.dll'
            binary.parent.mkdir(parents=True); binary.write_bytes(b'handwritten-contract-DLL-bytes'); binary.with_suffix('.deps.json').write_bytes(b'handwritten-contract-deps')
            target = dict(name='TP07LiteralProbe', dll_sha256=V.sha(binary), mvid='HANDWRITTEN_CONTRACT_MVID', deps_sha256=V.sha(binary.with_suffix('.deps.json')))
            startup = dict(replay_root=str(root / 'replay'), startup_assemblies=[target])
            path = root / 'literal-grammar/report.json'; path.parent.mkdir(); log = root / 'grammar.log'; log.write_bytes(b'')
            report = dict(schema_version=1, task='TP07-L0-literal-grammar', status='grammar_selftest_complete', database_accessed=False, expected_only=False,
                candidate_acceptance=False, literal_runtime_accepted=False, process_exit_code=0, case_count=35, passed=35, failed=0, accepted_case_count=12, rejected_case_count=23,
                tool_identity=dict(target, runtime='10.0.8'), cases=[])
            for vector in V.policy()['literal_grammar_cases']:
                report['cases'].append(dict(vector, literal_sha256=hashlib.sha256(vector['literal'].encode()).hexdigest(), actual_accept=vector['expected_accept'], status='completed',
                    error=None if vector['expected_accept'] else dict(type='TP07.Runtime.EvidenceException', code='LiteralSafety', number=None)))
            path.write_text(json.dumps(report)); receipt = dict(report=dict(path=str(path), sha256=V.sha(path)), process=dict(exit_code=0, host_exit_code=0, timed_out=False,
                termination_code=None, log_path=str(log), log_sha256=V.sha(log), command=[str(Path.home() / '.dotnet/dotnet'), str(binary), '--selftest-literal-grammar', '--output', str(path.parent)]))
            self.assertEqual([path, log], V.literal_grammar_selftest(receipt, startup))
            runner_spec = importlib.util.spec_from_file_location('tp07_grammar_runner_contract', HERE / 'runtime-run.py')
            runner = importlib.util.module_from_spec(runner_spec); runner_spec.loader.exec_module(runner)
            self.assertEqual([path, log], runner.check_literal_grammar(receipt, startup))
            for mutation in ('missing', 'expected', 'actual', 'literal', 'error', 'DLL', 'report_scope', 'exit', 'timeout', 'argv', 'hash'):
                with self.subTest(mutation=mutation):
                    bad = copy.deepcopy(report); proof = copy.deepcopy(receipt)
                    if mutation == 'missing': bad['cases'].pop()
                    elif mutation == 'expected': bad['cases'][0]['expected_accept'] = False
                    elif mutation == 'actual': bad['cases'][-1]['actual_accept'] = True
                    elif mutation == 'literal': bad['cases'][0]['literal'] = bad['cases'][0]['literal'].replace('FF7', 'FF9'); bad['cases'][0]['literal_sha256'] = hashlib.sha256(bad['cases'][0]['literal'].encode()).hexdigest()
                    elif mutation == 'error': bad['cases'][-1]['error']['type'] = 'System.Exception'
                    elif mutation == 'DLL': bad['tool_identity']['dll_sha256'] = '0' * 64
                    elif mutation == 'report_scope': bad['database_accessed'] = True
                    elif mutation == 'exit': proof['process']['exit_code'] = 1
                    elif mutation == 'timeout': proof['process']['timed_out'] = True
                    elif mutation == 'argv': proof['process']['command'][2] = '--profile'
                    path.write_text(json.dumps(bad)); proof['report']['sha256'] = V.sha(path) if mutation != 'hash' else '0' * 64
                    with self.assertRaises((V.ContractError, KeyError)): V.literal_grammar_selftest(proof, startup)
                    with self.assertRaises((runner.Stop, KeyError)): runner.check_literal_grammar(proof, startup)


def actual_transaction_modules():
    """Original ROOT stays real; imports are performed only by Low's test run."""
    spec = importlib.util.spec_from_file_location('tp07_actual_transaction_runner', HERE / 'runtime-run.py')
    runner = importlib.util.module_from_spec(spec); spec.loader.exec_module(runner)
    if runner.ROOT != V.ROOT: raise AssertionError('same_actual_workspace_required')
    return [(runner,runner.Stop),(V,V.ContractError)]


def transaction_source_boundary_fixture(module, value):
    """Pure source metadata fixture, without executable/SDK/runtime claims."""
    p = value['product']; templates = (module.POLICY if hasattr(module,'POLICY') else module.policy())['literal_template_source_hashes']
    lineage = dict(p['base_lineage'],runtime_template_source_sha256=value['template_ref']['sha256'],
        transaction_product_manifest_sha256=value['references']['transaction_product_manifest']['sha256'],
        transaction_patch_sha256=value['references']['transaction_patch']['sha256'],
        transaction_source_approval_sha256=value['references']['transaction_source_approval']['sha256'])
    artifacts = dict(p['base_lineage_artifacts'],approved_ef_source_manifest=p['base_approved_source'],
        runtime_template_source=value['template_ref'],**value['references'])
    hashes = dict(p['source_hashes']); hashes.update({'test/'+name:digest for name,digest in templates.items()})
    source = dict(schema_version=1,task='TP07-L0 prepared source',source_hashes=hashes,toolpack={},toolpack_check={},
        approved_ef_source_hashes=value['base']['source_hashes'],product_ef_source_hashes=p['source_hashes'],
        runtime_template_hashes=templates,lineage=lineage,W=p['W'],lineage_artifacts=artifacts)
    # Only source/lock relationships are exercised. This hash is explicitly
    # synthetic and is never presented as a restored lock or startup artifact.
    lock = dict(lineage=copy.deepcopy(lineage),source_hashes=dict(hashes,**{module.TX_HOST_LOCK:'a'*64}),
        lock_sha256='a'*64,provider_lock_sha256=p['source_hashes']['src/W.EntityFrameworkCore.Dameng/packages.lock.json'])
    startup = dict(lineage=copy.deepcopy(lineage),lineage_artifacts=copy.deepcopy(artifacts),source_hashes=copy.deepcopy(lock['source_hashes']))
    return source,lock,startup


def call_product_semantics(module, value):
    return module.transaction_product_semantics(value['product'],value['approval'],value['base'],value['prepared_base'],value['patch_text'],module.ROOT)


class TransactionSourceOriginContracts(unittest.TestCase):
    def test_actual175_product178_refs_and_both_independent_pure_contracts_positive(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs()
            self.assertEqual(len(value['base']['source_hashes']),175)
            self.assertEqual(len(call_product_semantics(module,value)),178)
            source,lock,startup=transaction_source_boundary_fixture(module,value)
            self.assertEqual(len(module.transaction_source_semantics(source,value)),185)
            self.assertEqual(len(module.transaction_source_semantics(source,value,lock,startup)),186)
            self.assertEqual(set(value['references']),{'transaction_product_manifest','transaction_patch','transaction_source_approval'})

    def test_actual_refs_reject_43_and7_field_mutations_scopes_and_boolean_counts(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();call_product_semantics(module,value)
            for key in value['product']:
                bad=copy.deepcopy(value);del bad['product'][key]
                with self.subTest(consumer=module.__name__,missing=key):
                    with self.assertRaisesRegex(error,'^transaction_product_fields$'):call_product_semantics(module,bad)
            for document in ('product','approval'):
                bad=copy.deepcopy(value);bad[document]['unapproved_extra']=True
                with self.assertRaisesRegex(error,'^transaction_product_fields$' if document=='product' else '^transaction_source_approval_keys$'):call_product_semantics(module,bad)
            for key in value['approval']:
                bad=copy.deepcopy(value);del bad['approval'][key]
                with self.assertRaisesRegex(error,'^transaction_source_approval_keys$'):call_product_semantics(module,bad)
            for key in ('source_count','base_source_count','unchanged_base_file_count','revision','schema_version','planned_full_unit_cases'):
                bad=copy.deepcopy(value);bad['product'][key]=True
                with self.assertRaisesRegex(error,'^transaction_'+key+'_type$'):call_product_semantics(module,bad)
            bad=copy.deepcopy(value);bad['approval']=json.loads(Path(value['product']['unit_revision_parent']['source_approval']['path']).read_text())
            with self.assertRaisesRegex(error,'^transaction_source_approval$'):call_product_semantics(module,bad)
            for changes,code in [({'scope':'transaction_capability_only'},'transaction_source_approval'),({'exact_count':True},'transaction_source_approval_type'),({'manifest':value['product']['unit_revision_parent']['product_manifest']},'transaction_source_approval')]:
                bad=copy.deepcopy(value);bad['approval'].update(changes)
                with self.assertRaisesRegex(error,'^'+code+'$'):call_product_semantics(module,bad)

    def test_actual175_and178_maps_reject_missing_patch_files_extra_changes_and_old_manifest(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();call_product_semantics(module,value)
            mutations=[]
            for name in module.TX_MODIFIED:
                mutations.append((name,'omitted_modify'))
            for name in module.TX_ADDED:
                mutations.append((name,'omitted_add'))
            unchanged=next(n for n,d in value['base']['source_hashes'].items() if value['product']['source_hashes'][n]==d)
            mutations += [(unchanged,'changed_unchanged'),('extra.cs','extra'),(unchanged,'base_map'),('', 'old175_is178')]
            for name,kind in mutations:
                bad=copy.deepcopy(value)
                if kind=='omitted_modify':bad['product']['source_hashes'][name]=bad['base']['source_hashes'][name];code='transaction_modified_files_length'
                elif kind=='omitted_add':del bad['product']['source_hashes'][name];code='transaction_current178'
                elif kind=='changed_unchanged':bad['product']['source_hashes'][name]='0'*64;code='transaction_modified_files_length'
                elif kind=='extra':bad['product']['source_hashes'][name]='0'*64;code='transaction_current178'
                elif kind=='base_map':bad['product']['base_source_hashes'][name]='0'*64;code='transaction_base_map'
                else:bad['base']['source_hashes']=copy.deepcopy(bad['product']['source_hashes']);code='transaction_base175'
                with self.subTest(consumer=module.__name__,mutation=kind,path=name):
                    with self.assertRaisesRegex(error,'^'+code+'$'):call_product_semantics(module,bad)
            bad=copy.deepcopy(value);bad['product']=json.loads(Path(value['product']['unit_revision_parent']['product_manifest']['path']).read_text())
            with self.assertRaisesRegex(error,'^transaction_product_fields$'):call_product_semantics(module,bad)

    def test_actual_refs_patch_sets_aggregate_and_fixed_paths_are_not_self_declared(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();call_product_semantics(module,value)
            for label,code in [('patch_order','transaction_declared_patch_paths'),('added_modified_swap','transaction_declared_added_length'),('aggregate','transaction_source_aggregate_sha256'),('modified_bytes','transaction_aggregate'),('nested_ref','transaction_nested_refs')]:
                bad=copy.deepcopy(value)
                if label=='patch_order':bad['product']['patch_paths'].reverse()
                elif label=='added_modified_swap':bad['product']['added_files'],bad['product']['modified_files']=bad['product']['modified_files'],bad['product']['added_files']
                elif label=='aggregate':bad['product']['source_aggregate_sha256']='0'*64
                elif label=='modified_bytes':bad['product']['source_hashes'][module.TX_MODIFIED[0]]='0'*64
                else:bad['product']['base_approved_source']['sha256']='0'*64
                with self.subTest(consumer=module.__name__,mutation=label):
                    with self.assertRaisesRegex(error,'^'+code+'$'):call_product_semantics(module,bad)
            for key in value['references']:
                for field in ('path','sha256'):
                    bad=copy.deepcopy(value['references']);bad[key][field]=str(module.ROOT/'tools/DownstreamAcceptance/TP07/runtime-run.py') if field=='path' else '0'*64
                    with self.assertRaisesRegex(error,'^transaction_input_refs$'):module.transaction_product_inputs(bad)

    def test_prepared185_requires_product178_original175_and_exact_seven_templates(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();source,lock,startup=transaction_source_boundary_fixture(module,value)
            module.transaction_source_semantics(source,value)
            for label,code in [('missing_product','transaction_prepared_closure_keys'),('extra_source','transaction_prepared_closure_keys'),('old175_as_product','transaction_prepared178_keys'),('template','transaction_prepared_templates'),('old175_replaced','transaction_prepared175_keys')]:
                bad=copy.deepcopy(source)
                if label=='missing_product':del bad['source_hashes'][module.TX_ADDED[0]]
                elif label=='extra_source':bad['source_hashes']['extra.props']='0'*64
                elif label=='old175_as_product':bad['product_ef_source_hashes']=bad['approved_ef_source_hashes']
                elif label=='template':bad['runtime_template_hashes']=dict(bad['runtime_template_hashes']);bad['runtime_template_hashes'][next(iter(bad['runtime_template_hashes']))]='0'*64
                else:bad['approved_ef_source_hashes']=bad['product_ef_source_hashes']
                with self.assertRaisesRegex(error,'^'+code+'$'):module.transaction_source_semantics(bad,value)
            overlap=copy.deepcopy(value);bad=copy.deepcopy(source);target='test/'+next(iter(source['runtime_template_hashes']))
            overlap['product']['source_hashes'][target]='0'*64;bad['product_ef_source_hashes']=overlap['product']['source_hashes']
            with self.assertRaisesRegex(error,'^transaction_template_overlap$'):module.transaction_source_semantics(bad,overlap)

    def test_locked186_and_startup_reject_old_refs_extra_locks_and_three_SHA_crosswires(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();source,lock,startup=transaction_source_boundary_fixture(module,value)
            module.transaction_source_semantics(source,value,lock,startup)
            for label,code in [('extra_lock','transaction_locked186_keys'),('changed_old','transaction_locked186'),('old_startup','transaction_startup_artifacts_keys')]:
                badlock=copy.deepcopy(lock);badstartup=copy.deepcopy(startup)
                if label=='extra_lock':badlock['source_hashes']['test/Other/packages.lock.json']='0'*64
                elif label=='changed_old':badlock['source_hashes'][module.TX_UNIT_LOCK]='0'*64
                else:del badstartup['lineage_artifacts']['transaction_product_manifest']
                with self.assertRaisesRegex(error,'^'+code+'$'):module.transaction_source_semantics(source,value,badlock,badstartup)
            for key in ('transaction_product_manifest_sha256','transaction_patch_sha256','transaction_source_approval_sha256'):
                for stage,code in [('source','transaction_lineage10'),('lock','transaction_lock_lineage'),('startup','transaction_startup_lineage')]:
                    a,b,c=copy.deepcopy((source,lock,startup));{'source':a,'lock':b,'startup':c}[stage]['lineage'][key]='0'*64
                    with self.subTest(consumer=module.__name__,stage=stage,field=key):
                        with self.assertRaisesRegex(error,'^'+code+'$'):module.transaction_source_semantics(a,value,b,c)

    def test_physical_source_hash_symlink_escape_and_only_two_project_output_projection(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs()
            with tempfile.TemporaryDirectory(dir=module.ROOT/'.local/temporal-native/verification') as folder:
                root=Path(folder);name=module.TX_ADDED[0];actual=Path(value['product']['source_root'])/name
                target=root/name;target.parent.mkdir(parents=True);shutil.copyfile(actual,target)
                expected={name:value['product']['source_hashes'][name]}
                module.origin_exact(module.origin_inventory(root),expected,'test_physical_source')
                target.write_bytes(target.read_bytes()+b'\n')
                with self.assertRaisesRegex(error,'^test_physical_source$'):module.origin_exact(module.origin_inventory(root),expected,'test_physical_source')
                target.unlink();target.symlink_to(actual)
                with self.assertRaisesRegex(error,'^origin_source_symlink$'):module.origin_inventory(root)
                with self.assertRaisesRegex(error,'^origin_path_boundary$'):module.origin_path(str(target),module.ROOT)
                target.unlink();shutil.copyfile(actual,target)
                with self.assertRaisesRegex(error,'^origin_path_boundary$'):module.origin_path(str(root/'..'/root.name/name),module.ROOT)
                for prefix in module.TX_GENERATED:
                    generated=root/prefix/'generated.bin';generated.parent.mkdir(parents=True,exist_ok=True);generated.write_bytes(b'only permitted project output')
                module.origin_exact(module.origin_inventory(root,generated=True),expected,'test_output_projection')
                for unexpected in ('src/Other/obj/extra.props','src/W.EntityFrameworkCore.Dameng/extra.targets','extra.cs'):
                    path=root/unexpected;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'extra source')
                    with self.assertRaisesRegex(error,'^test_output_projection_keys$'):module.origin_exact(module.origin_inventory(root,generated=True),expected,'test_output_projection')
                    path.unlink()

    def test_all_nested_origin_refs_and_original175_current178_enter_before_after_protection(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();hashes=module.transaction_origin_hashes(value)
            module.transaction_origin_protection(hashes,hashes,value)
            for ref in value['references'].values():self.assertIn(ref['path'],hashes)
            for root,names in [(module.ROOT/module.TX_BASE_ROOT,value['base']['source_hashes']),(Path(value['product']['source_root']),value['product']['source_hashes'])]:
                self.assertTrue(all(str(root/name) in hashes for name in names))
            for key in value['references']:
                path=value['references'][key]['path']
                for when in ('before','after'):
                    before=copy.deepcopy(hashes);after=copy.deepcopy(hashes);{'before':before,'after':after}[when].pop(path)
                    with self.assertRaisesRegex(error,'^transaction_origin_protection$'):module.transaction_origin_protection(before,after,value)

    def test_prepare_git_receipt_requires_six_ordered_natural_processes_and_real_log_hashes(self):
        for module,error in actual_transaction_modules():
            value=module.transaction_product_inputs();source,_,_=transaction_source_boundary_fixture(module,value)
            path=module.ROOT/'.local/temporal-native/verification/TP07-L0-v3-python-prepare/prepared/prepare-receipt.json'
            self.assertEqual(module.sha(path),'10c2ac8cc0ee586d993932668fab3b20ef0ee8ab066fabbba1910fee71b00ba3')
            old=json.loads(path.read_text());replay=Path(old['overlay_commands'][0]['cwd'])
            with self.assertRaisesRegex(error,'^transaction_six_git_commands$'):module.transaction_prepare_commands(old,source,value,replay)
            # Failure-only rows: no synthetic successful transaction replay.
            for changes,code in [({'exit_code':True},'transaction_git_exit_type'),({'host_exit_code':1},'transaction_git_host_exit'),({'timed_out':True},'transaction_git_timeout'),({'termination_code':'deadline_kill'},'transaction_git_termination_type'),({'log_sha256':'0'*64},'transaction_git_log_hash'),({'command':['git','apply','unapproved.patch']},'transaction_git_command_length')]:
                bad=dict(overlay_commands=copy.deepcopy(old['overlay_commands']+[old['overlay_commands'][0],old['overlay_commands'][1]]));bad['overlay_commands'][0].update(changes)
                with self.assertRaisesRegex(error,'^'+code+'$'):module.transaction_prepare_commands(bad,source,value,replay)

    def test_prepare_lock_build_run_and_independent_consumer_refuse_before_any_process(self):
        runner,error=actual_transaction_modules()[0];value=runner.transaction_product_inputs();source,lock,startup=transaction_source_boundary_fixture(runner,value)
        with tempfile.TemporaryDirectory(dir=runner.ROOT/'.local/temporal-native/verification') as folder:
            root=Path(folder);manifest=root/'toolpack.json';hashes={n:runner.sha(runner.ROOT/n) for n in runner.TOOLPACK_FILES}
            manifest.write_text(json.dumps(dict(schema_version=1,task='TP07-L0 toolpack',source_hashes=hashes)))
            args=SimpleNamespace(toolpack_manifest=str(manifest),toolpack_manifest_sha256=runner.sha(manifest))
            identity,before=runner.toolpack_entry(args)
            def bind(document,phase):
                document['toolpack']=identity;document['toolpack_check']=dict(phase=phase,before=before,after=before)
            bind(source,'prepare');bind(lock,'lock');bind(startup,'build')
            del source['source_hashes'][runner.TX_ADDED[0]]
            source_path=root/'source-manifest.json';source_path.write_text(json.dumps(source))
            lock_path=root/'lock.json';lock_path.write_text(json.dumps(lock))
            startup.update(schema_version=1,W=value['product']['W'],replay_root=str(root/'replay'),source_manifest_path=str(source_path),source_manifest_sha256=runner.sha(source_path),lock_manifest_path=str(lock_path),lock_manifest_sha256=runner.sha(lock_path))
            startup_path=root/'startup.json';startup_path.write_text(json.dumps(startup))
            candidate=runner.ROOT/'.local/temporal-native/verification/TP06-v1-package/identities.json';caps=runner.ROOT/'.local/temporal-native/verification/TP06-v1-package/capabilities.root-approved.json'
            with mock.patch.object(runner,'process',side_effect=AssertionError('process must never start')) as child:
                bad=SimpleNamespace(**vars(args),workspace_root=str(runner.ROOT),source_approved=True,product_manifest=str(candidate),product_manifest_sha256=runner.sha(candidate),transaction_patch=value['references']['transaction_patch']['path'],transaction_patch_sha256=value['references']['transaction_patch']['sha256'],product_source_approval=value['references']['transaction_source_approval']['path'],product_source_approval_sha256=value['references']['transaction_source_approval']['sha256'])
                with self.assertRaisesRegex(error,'^transaction_input_refs$'):runner.prepare(bad)
                for phase in ('lock','build'):
                    bad=SimpleNamespace(**vars(args),source_approved=True,phase=phase,prepared=str(root),prepared_manifest_sha256=runner.sha(source_path))
                    with self.assertRaisesRegex(error,'^transaction_prepared_closure_keys$'):runner.offline(bad)
                bad=SimpleNamespace(**vars(args),runtime_approved=True,startup_manifest=str(startup_path),startup_manifest_sha256=runner.sha(startup_path),candidate_manifest=str(candidate),capabilities=str(caps))
                with self.assertRaisesRegex(error,'^transaction_prepared_closure_keys$'):runner.run_runtime(bad)
                child.assert_not_called()
            receipt=dict(startup_manifest_path=str(startup_path),startup_manifest_sha256=runner.sha(startup_path));bind(receipt,'run')
            (root/'run-receipt.json').write_text(json.dumps(receipt))
            with self.assertRaisesRegex(V.ContractError,'^transaction_prepared_closure_keys$'):V.validate_artifact(root,runner.sha(startup_path),manifest,args.toolpack_manifest_sha256)


    def test_prepare_all_six_git_calls_use_copied_exact_ceiling_and_refuse_without_execution(self):
        import ast
        runner,error=actual_transaction_modules()[0];value=runner.transaction_product_inputs()
        tree=ast.parse((HERE/'runtime-run.py').read_text())
        prepare=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='prepare')
        calls=[n for n in ast.walk(prepare) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name) and n.func.id=='process']
        self.assertEqual(len(calls),2)
        for call in calls:
            self.assertIsInstance(call.args[3],ast.Name)
            self.assertEqual(call.args[3].id,'git_environment')
            self.assertEqual(ast.literal_eval(call.args[4]),30)
        loops=[n for n in ast.walk(prepare) if isinstance(n,ast.For) and isinstance(n.target,ast.Name) and n.target.id=='check']
        self.assertEqual(len(loops),2)
        for loop in loops:self.assertEqual(ast.literal_eval(loop.iter),(True,False))
        overlays=next(n for n in prepare.body if isinstance(n,ast.For) and isinstance(n.target,ast.Tuple)
            and [v.id for v in n.target.elts]==['label','patch','expected'])
        self.assertEqual([ast.literal_eval(row.elts[0]) for row in overlays.iter.elts],['bridge','mapping'])
        self.assertEqual(len(overlays.iter.elts)*2+2,6)
        # The real prepare reaches its first Git call, then refuses to execute.
        # Structural coverage above includes both loops; no successful Git result
        # is mocked and this test cannot establish a completed six-call replay.
        with tempfile.TemporaryDirectory(dir=runner.ROOT/'.local/temporal-native/verification') as folder:
            root=Path(folder);output=root/'prepare';replay=output/'replay';manifest=root/'toolpack.json'
            hashes={n:runner.sha(runner.ROOT/n) for n in runner.TOOLPACK_FILES}
            manifest.write_text(json.dumps(dict(schema_version=1,task='TP07-L0 toolpack',source_hashes=hashes)))
            artifacts=value['product']['base_lineage_artifacts']
            args=SimpleNamespace(toolpack_manifest=str(manifest),toolpack_manifest_sha256=runner.sha(manifest),
                workspace_root=str(runner.ROOT),output=str(output),source_approved=True,
                approved_source=str(runner.ROOT/runner.TX_BASE_ROOT),
                approved_source_manifest=value['product']['base_approved_source']['path'],
                approved_source_manifest_sha256=value['product']['base_approved_source']['sha256'],
                templates=str(HERE/'runtime-templates'),template_manifest=value['template_ref']['path'],
                template_manifest_sha256=value['template_ref']['sha256'],
                product_manifest=value['references']['transaction_product_manifest']['path'],
                product_manifest_sha256=value['references']['transaction_product_manifest']['sha256'],
                transaction_patch=value['references']['transaction_patch']['path'],
                transaction_patch_sha256=value['references']['transaction_patch']['sha256'],
                product_source_approval=value['references']['transaction_source_approval']['path'],
                product_source_approval_sha256=value['references']['transaction_source_approval']['sha256'])
            for name in ('base_archive','bridge_manifest','bridge_patch','mapping_manifest','mapping_patch'):
                setattr(args,name,artifacts[name]['path'])
            for name in ('bridge_manifest','mapping_manifest'):setattr(args,name+'_sha256',artifacts[name]['sha256'])
            with mock.patch.dict(os.environ,{'GIT_DIR':'unapproved-parent-repository','GIT_WORK_TREE':'unapproved-parent-worktree',
                    'GIT_CEILING_DIRECTORIES':'unapproved-ceiling'},clear=False):
                public=runner.public_env(output);original=copy.deepcopy(public)
                self.assertFalse(any(key.startswith('GIT_') for key in public))
                def refuse(command,cwd,log,environment,timeout):
                    self.assertEqual(command,['git','apply','--check',artifacts['bridge_patch']['path']])
                    self.assertEqual(cwd,replay);self.assertEqual(log,output/'bridge-check.log');self.assertEqual(timeout,30)
                    self.assertIsNot(environment,public)
                    self.assertEqual(environment,dict(original,GIT_CEILING_DIRECTORIES=str(replay.parent)))
                    self.assertEqual(public,original)
                    raise AssertionError('git_replay_refusal_before_child')
                with mock.patch.object(runner,'public_env',return_value=public),mock.patch.object(runner,'process',side_effect=refuse) as child:
                    with self.assertRaisesRegex(AssertionError,'^git_replay_refusal_before_child$'):runner.prepare(args)
                    self.assertEqual(child.call_count,1)
                self.assertEqual(public,original)
                self.assertEqual(runner.public_env(output),original)
            self.assertFalse((replay/'.git').exists())
            self.assertFalse((output/'source-manifest.json').exists())
            self.assertFalse((output/'prepare-receipt.json').exists())


if __name__ == '__main__': unittest.main()
