#!/usr/bin/env python3
"""Run only: Low supplies approved exact E/L0 source, locks, builds and startup identities.

External approval schema is enforced by ef-validate.inputs. No prepare/restore/build,
retry, arbitrary group selection, SQL, admin, fallback or global cleanup is provided.
Every fixed window runs independent exact-ledger cleanup even after failure, then validation.
"""
import argparse
import datetime
import os
from pathlib import Path
import signal
import subprocess
import sys
import types
import uuid

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent


def validator():
    path = HERE / 'ef-validate.py'
    module = types.ModuleType('tp07_E_runner_contract')
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


def environment(stage):
    # No inherited credential/configuration variables enter a subprocess before its TEST wrapper.
    env = {k: os.environ[k] for k in ('PATH', 'HOME', 'TMPDIR', 'LANG', 'LC_ALL') if k in os.environ}
    env.update(DOTNET_CLI_HOME=str(stage / 'cli-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
        DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
        NUGET_PACKAGES=str(stage / 'nuget-packages'), NUGET_HTTP_CACHE_PATH=str(stage / 'http-cache'),
        PYTHONDONTWRITEBYTECODE='1')
    for name in ('cli-home', 'nuget-packages', 'http-cache'): (stage / name).mkdir(parents=True, exist_ok=True)
    return env


def process(command, cwd, log, env, deadline):
    start = datetime.datetime.now(datetime.timezone.utc); timed_out = False
    with Path(log).open('x') as stream:
        try: child = subprocess.Popen(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        except OSError as error:
            # Still return a failed main receipt so the enclosing finally performs independent cleanup and validation.
            return dict(command=command, cwd=str(cwd), log_path=str(Path(log).resolve()), deadline_seconds=deadline,
                exit_code=126, host_exit_code=126, timed_out=False, termination_code='process_start_' + type(error).__name__,
                actual_elapsed_seconds=(datetime.datetime.now(datetime.timezone.utc) - start).total_seconds())
        try: child.wait(timeout=deadline)
        except subprocess.TimeoutExpired:
            timed_out = True; os.killpg(child.pid, signal.SIGKILL); child.wait()
    return dict(command=command, cwd=str(cwd), log_path=str(Path(log).resolve()), deadline_seconds=deadline,
        exit_code=124 if timed_out else child.returncode, host_exit_code=child.returncode,
        timed_out=timed_out, termination_code='deadline_kill' if timed_out else None,
        actual_elapsed_seconds=(datetime.datetime.now(datetime.timezone.utc) - start).total_seconds())


def artifact(v, path): return dict(path=str(Path(path).resolve()), sha256=v.sha(path))


def open_context(v, args):
    pack = v.pack_entry(args.toolpack_manifest, args.toolpack_manifest_sha256)
    return v.inputs(args.approved_execution, args.approved_execution_sha256, pack)


def common_arguments(args):
    return ['--approved-execution', str(Path(args.approved_execution).resolve()),
        '--approved-execution-sha256', args.approved_execution_sha256,
        '--toolpack-manifest', str(Path(args.toolpack_manifest).resolve()),
        '--toolpack-manifest-sha256', args.toolpack_manifest_sha256]


def internal_host(v, args):
    context = open_context(v, args); approval = context['approval']; startup = context['documents']['startup']
    output = Path(args.output).absolute(); v.need(not output.exists(), 'fresh_internal_E_output')
    stage = output.parent; root = stage.parents[2]
    descriptor = v.window_descriptor(args.profile, args.group, args.window_index)
    v.canonical(stage, root / args.profile / args.group / ('window-' + str(args.window_index).zfill(3)))
    v.canonical(output, stage / ('cleanup' if args.cleanup else 'main'))
    run_receipt = v.run_context(root, context, args.run_id)
    started = [r['window'] for g in run_receipt['groups'] for r in g['windows']]
    v.need(started and started[-1] == descriptor and run_receipt['status'] == 'running', 'only_current_started_window_host')
    metadata = v.window_metadata(context, args.run_id, descriptor)
    env = environment(stage)
    key = 'DAMENG_TLS_TEST_CONNECTION_STRING' if args.profile == 'tls' else 'DAMENG_TEST_CONNECTION_STRING'
    v.need(bool(os.environ.get(key)), 'original_TEST_wrapper_variable_required')
    env['DAMENG_TEST_CONNECTION_STRING'] = os.environ[key]  # Memory only; never serialized, logged or argv.
    refs = approval['artifacts']; replay = Path(startup['replay_root'])
    env.update(TP07_CANDIDATE_MANIFEST=refs['candidate']['path'], TP07_CAPABILITIES=refs['capabilities']['path'],
        TP07_PROFILE=args.profile, TP07_ORIGINAL_REPO=approval['original_repo'],
        TP07_STARTUP_MANIFEST=refs['startup']['path'], TP07_STARTUP_MANIFEST_SHA256=refs['startup']['sha256'])
    name = 'TP07LiteralProbe' if args.cleanup else 'TP07TemporalRuntime'
    command = [approval['dotnet']['path'], str(replay / ('test/' + name + '/bin/Debug/net10.0/' + name + '.dll')),
        '--profile', args.profile, '--candidate-manifest', refs['candidate']['path'],
        '--capabilities', refs['capabilities']['path'], '--output', str(output)]
    if args.cleanup:
        v.need(args.ledger and Path(args.ledger).resolve() == stage / 'main/ownership.json', 'exact_window_ledger_only')
        command += ['--repo', approval['original_repo'], '--build-manifest', refs['startup']['path'],
            '--build-manifest-sha256', refs['startup']['sha256'], '--cleanup', '--ledger', str(Path(args.ledger).resolve())]
    else: command += ['--group', args.group, '--window-index', str(args.window_index), '--run-id', args.run_id, '--cohort-id', metadata['cohort_id']]
    v.unchanged(context['pins']); v.pack_check(context['pack'])
    # Child shares outer deadline process group; stdout is the runner-owned bounded-stage log.
    code = subprocess.call(command, cwd=replay, env=env)
    after = v.unchanged(context['pins']); v.pack_check(context['pack'])
    output.mkdir(parents=True, exist_ok=True)
    v.write(output / 'E-host-receipt.json', dict(**metadata, task='TP07-E internal window host',
        cleanup=args.cleanup, child_exit_code=code, command=command, input_hashes_before=context['pins'], input_hashes_after=after))
    return code


def run(v, args):
    context = open_context(v, args); approval = context['approval']; startup = context['documents']['startup']
    output = Path(args.output).absolute(); v.canonical(output, output)
    v.need(output.is_relative_to(v.ROOT / '.local/temporal-native/verification') and not output.exists(), 'new_stage_specific_E_output')
    output.mkdir(parents=True); run_id = uuid.uuid4().hex
    receipt = dict(schema_version=2, task='TP07-E window run', status='running', run_id=run_id, cohort_id=v.cohort_id(context),
        policy_sha256=v.sha(v.HERE / 'ef-cases.json'), window_contract_sha256=v.digest(v.window_contract()), toolpack=context['pack'],
        approved_execution=artifact(v, args.approved_execution), input_hashes_before=context['pins'], input_hashes_after=context['pins'],
        groups=[], e_runtime_accepted=False, full_EF_C_accepted=False, D_runtime_support='pending')
    def save(): v.write(output / 'run-receipt.json', receipt)
    save(); replay = Path(startup['replay_root'])
    receipt['sdk'] = process([approval['dotnet']['path'], '--version'], replay, output / 'sdk.log', environment(output), 30)
    save(); v.bounded_process(receipt['sdk'], 30)
    v.need((output / 'sdk.log').read_text().strip() == v.policy()['sdk_version'], 'actual_pinned_SDK')
    for profile in v.PROFILES:
        for group in v.GROUPS:
            group_folder = output / profile / group; group_folder.mkdir(parents=True)
            group_row = dict(profile=profile, group=group, windows=[]); receipt['groups'].append(group_row); save()
            for descriptor in [w for w in v.windows() if w['profile'] == profile and w['group'] == group]:
                stage = group_folder / ('window-' + str(descriptor['index']).zfill(3)); stage.mkdir()
                metadata = v.window_metadata(context, run_id, descriptor)
                row = dict(window=descriptor, receipt=None, validation=None, validator=None)
                group_row['windows'].append(row); save()
                main = cleanup = None; artifacts = {}
                try:
                    v.unchanged(context['pins']); v.pack_check(context['pack'])
                    main = process(v.window_host_command(stage, descriptor, context, run_id), replay, stage / 'main.log', environment(stage), 900)
                finally:
                    # Preserve independent exact-ledger cleanup even for a partial or failed main. Never create a substitute ledger.
                    ledger = stage / 'main/ownership.json'
                    if ledger.exists():
                        v.unchanged(context['pins']); v.pack_check(context['pack'])
                        cleanup = process(v.window_host_command(stage, descriptor, context, run_id, True), replay,
                            stage / 'cleanup.log', environment(stage), 300)
                    for key, path in {'report': stage / 'main/runtime.json', 'ledger': ledger, 'cleanup': stage / 'cleanup/report.json',
                        'main_host': stage / 'main/E-host-receipt.json', 'cleanup_host': stage / 'cleanup/E-host-receipt.json'}.items():
                        if path.exists(): artifacts[key] = artifact(v, path)
                    receipt['input_hashes_after'] = v.unchanged(context['pins'])
                    document = dict(**metadata, task='TP07-E window run', main=main, cleanup=cleanup, artifacts=artifacts,
                        input_hashes_before=context['pins'], input_hashes_after=receipt['input_hashes_after'])
                    v.write(stage / 'window-receipt.json', document); row['receipt'] = artifact(v, stage / 'window-receipt.json'); save()
                validation = stage / 'validation.json'
                row['validator'] = process(v.window_validator_command(stage, descriptor, context, run_id), replay,
                    stage / 'validate.log', environment(stage), 180)
                if validation.exists(): row['validation'] = artifact(v, validation)
                save()
                try:
                    v.bounded_process(main, 900); v.need(cleanup is not None, 'independent_window_cleanup_required')
                    v.bounded_process(cleanup, 300); v.bounded_process(row['validator'], 180)
                    v.need(v.load(validation)['e_window_contract_valid'] is True, 'independent_window_failed')
                except Exception:
                    receipt['status'] = 'window_failed_stop'; save(); raise
            group_document = dict(schema_version=2, task='TP07-E window group', profile=profile, group=group,
                run_id=run_id, cohort_id=receipt['cohort_id'], policy_sha256=receipt['policy_sha256'],
                window_contract_sha256=receipt['window_contract_sha256'], approved_execution=context['execution_reference'],
                toolpack=context['pack'], windows=group_row['windows'], input_hashes_before=context['pins'], input_hashes_after=v.unchanged(context['pins']))
            v.write(group_folder / 'group-receipt.json', group_document)
            group_row['receipt'] = artifact(v, group_folder / 'group-receipt.json'); save()
    receipt.update(status='E_windows_complete', input_hashes_after=v.unchanged(context['pins']))
    save()
    # Complete run receipt is frozen before the final independent 25-window reread.
    final = output / 'validation.json'
    final_process = process([sys.executable, str(HERE / 'ef-validate.py')] + common_arguments(args) +
        ['--run', str(output), '--output', str(final)], replay, output / 'validate.log', environment(output), 180)
    v.write(output / 'final-validation-process.json', final_process); v.bounded_process(final_process, 180)
    v.need(v.load(final)['e_value_contract_valid'] is True, 'independent_final_E_window_contract')
    return 0

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=('run', 'internal-host'))
    for name in ('approved-execution', 'approved-execution-sha256', 'toolpack-manifest', 'toolpack-manifest-sha256', 'output'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--profile', choices=('shared', 'tls')); parser.add_argument('--group', choices=('roundtrip', 'mutation', 'six', 'generated'))
    parser.add_argument('--cleanup', action='store_true'); parser.add_argument('--ledger')
    parser.add_argument('--window-index', type=int); parser.add_argument('--run-id')
    args = parser.parse_args(); v = validator()
    try:
        if args.phase == 'internal-host':
            v.need(args.profile and args.group and args.window_index is not None and args.run_id, 'internal_finite_window_required')
            return internal_host(v, args)
        v.need(not args.profile and not args.group and not args.cleanup and not args.ledger and args.window_index is None and not args.run_id, 'finite_new_complete_run_only')
        return run(v, args)
    except Exception as error:
        print('TP07_E_stopped_' + (str(error) if isinstance(error, v.ContractError) else type(error).__name__))
        return 1


if __name__ == '__main__': raise SystemExit(main())
