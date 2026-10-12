"""Handwritten/mutated offline contracts. These fixtures never establish live E acceptance.

Low runs this file after the external source freeze. No SDK, wrapper, database or
product codec is invoked; packet construction below is test-only bit arithmetic.
"""
import copy
import datetime
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import tempfile
import types
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent


def module(name):
    path = HERE / name; result = types.ModuleType(name.replace('-', '_'))
    result.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), 'exec'), result.__dict__)
    return result


V = module('ef-validate.py')
R = module('ef-run.py')
L0 = module('runtime-validate.py')
FIXTURE_ROLE = 'handwritten_contract_fixture_not_runtime_or_DB_acceptance'

# Exact accepted mapping-v5 XML bytes, rather than a self-invented inline provider.
# Origin: TP07-M1-v5-graph-reuse/execution-source, read during the B01 static repair.
PROVIDER_XML = '''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>W.EntityFrameworkCore.Dameng</AssemblyName>
    <RootNamespace>W.EntityFrameworkCore.Dameng</RootNamespace>
    <VersionPrefix>0.3.0</VersionPrefix>
    <PackageId>W.EntityFrameworkCore.Dameng</PackageId>
    <Title>Unofficial Dameng provider for Entity Framework Core 10</Title>
    <Description>An independently maintained Entity Framework Core 10 provider for Dameng Database.</Description>
    <PackageTags>entity-framework-core;efcore;dameng;dm8;database-provider</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);EF1001</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="W.DmProvider" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
  </ItemGroup>

  <ItemGroup>
    <None Include="../../README.md" Pack="true" PackagePath="/" />
    <None Include="../../THIRD-PARTY-NOTICES.md" Pack="true" PackagePath="/" />
  </ItemGroup>
</Project>
'''
CENTRAL_XML = '''<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="W.DmProvider" Version="[0.1.0-temporal.20261008144915]" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="[10.0.12,11.0.0)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational.Specification.Tests" Version="[10.0.12,11.0.0)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="[10.0.12,11.0.0)" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>
</Project>
'''


def route(role):
    names = {'reader': 'DmDataReader', 'connection': 'DmConnection', 'command': 'DmCommand', 'parameter': 'DmParameter'}
    return dict(role=role, type='W.Dm.' + names[role], dll_sha256=V.policy()['W']['dll_sha256'], mvid=V.policy()['W']['mvid'])


def physical(name='shared'):
    ver, message, encoding = ('8.1.5.60', 21, 'utf-8') if name == 'shared' else ('8.1.4.6', 11, 'gb18030')
    return dict(profile=name, server_version=ver, message_version=message, encoding=encoding, negotiated_encoding=encoding,
        encoding_override_empty=True, profile_origin='physical_get_conn_instance_conn_property',
        server_user='WDM_PROVIDER_TEST', server_schema='WDM_PROVIDER_TEST', result_format='ordinary_non_BDTA')


def chain(leaf): return [dict(depth=0, type=leaf, safe=dict(type=leaf, code=None, number=None))]


def parameter(kind, ticks, scale=7):
    value = {'is_null': True} if ticks is None else ({'clr_type': 'DateTime', 'ticks': ticks, 'kind': 'Unspecified'} if kind == 'dt'
        else {'clr_type': 'DateTimeOffset', 'local_ticks': ticks, 'offset_minutes': 330})
    return dict(db_type='DateTime2' if kind == 'dt' else 'DateTimeOffset', provider_type='DateTime',
        precision=0, scale=scale, explicit_precision=False, explicit_scale=True, direction='Input', value=value)


def raw(kind, scale, extended=True):
    ctype = (26 if kind == 'dt' else 27) if extended else (16 if kind == 'dt' else 23)
    return dict(available=True, source='ParameterDescribe', c_type=ctype, precision={16: 8, 23: 10, 26: 9, 27: 11}[ctype],
        scale=scale, type_flag=1, mask=0, derived_mask=0, result_format='Unknown')


def metadata_report(scale=6, ticks=639204993161234560, profile='shared', order=('dt', 'dto'), extended=True):
    stage = 'fixture/SaveChanges'; requests = [parameter(kind, ticks if kind == 'dt' else None) for kind in order]
    requests += [dict(db_type='Int32', provider_type='Int', precision=0, scale=0, explicit_precision=False,
        explicit_scale=False, direction='Input', value={'clr_type': 'Int32', 'value': 1}),
        dict(db_type='AnsiString', provider_type='VarChar', precision=0, scale=0, explicit_precision=False,
        explicit_scale=False, direction='Input', value={'clr_type': 'String', 'value': 'first'})]
    efid = '12345678-1234-1234-1234-123456789abc'; identity = [1, 2, 3, 4]
    start = dict(stage=stage, status='attempted', api='ReaderExecutingAsync', ef_command_id=efid, attempt_id=1,
        sql_sha256='a'*64, command=route('command'), connection=route('connection'), parameters=[dict(p, actual=route('parameter')) for p in requests])
    params = []
    for i, request in enumerate(requests):
        typ = request['db_type']; rr = raw('dt' if typ == 'DateTime2' else 'dto', scale, extended)
        if typ not in ('DateTime2', 'DateTimeOffset'): rr.update(c_type=7 if typ == 'Int32' else 2, precision=4, scale=0)
        params.append(dict(ordinal=i, server_parameter_name_sha256='a'*64, public_parameter_name_sha256='b'*64,
            public_parameter_index=i, server_inout_type=0, request=request, raw=rr))
    src = dict(stage=stage, attempt_id=1, ef_command_id=efid, sequence=2, response_sequence=1, request_opcode=5,
        capture_point='after_prepare_decode_before_bind', operation_identity=identity, server_statement_id=100,
        statement_reference_token=12, command_reference_token=13, connection_reference_token=14, parameters=params)
    response = dict(stage=stage, attempt_id=1, sequence=1, request_opcode=5, response_opcode=200,
        sql_code=0, body_length=32, operation_identity=identity)
    return dict(commands=[start], parameter_describe_sources=[src], protocol_responses=[response], _oracle=L0,
        sent_frames=[], async_write_positive_controls=[])


def binding_report():
    report = metadata_report(); src = report['parameter_describe_sources'][0]
    bindings = []
    for p in src['parameters'][:2]:
        bindings.append(dict(ordinal=p['ordinal'], binding_available=True, raw_available=True, reliable_fixed=True, c_type=p['raw']['c_type'],
            encoded_scale=7, wire_scale=6, requested_scale=7, column_scale=6, request_explicit_scale=True,
            profile_state='VerifiedSeven', **{'raw_' + k: v for k, v in p['raw'].items() if k != 'available'}))
    report['commands'].append(dict(stage=src['stage'], attempt_id=1, status='completed', api='ReaderExecutedAsync',
        sql_sha256='a'*64, binding=bindings, reader=route('reader')))
    return report


def packet_oracle(spec):
    """Handwritten test-only ordinary layout, never imported by production tools."""
    ticks = spec['ticks']; dto = spec['kind'] == 'dto'; off = spec['offset_minutes'] if dto else None
    days, within = divmod(ticks, 864000000000); date = datetime.date.min + datetime.timedelta(days=days)
    seconds, frac = divmod(within, 10000000); hour, seconds = divmod(seconds, 3600); minute, second = divmod(seconds, 60)
    ns = frac * 100; extended = spec['scale'] == 7
    dw = date.year | date.month << 15 | date.day << 19
    tw = hour | minute << 5 | second << 11 | (ns if extended else ns // 1000) << 17
    payload = dw.to_bytes(3, 'little') + tw.to_bytes(6 if extended else 5, 'little')
    if dto: payload += off.to_bytes(2, 'little', signed=True)
    ctype = (27 if dto else 26) if extended else (23 if dto else 16)
    typed = dict(type='System.DateTimeOffset' if dto else 'System.DateTime', ticks=ticks)
    if dto: typed.update(offset_minutes=off, utc_ticks=ticks - off * 600000000)
    else: typed['kind'] = 'Unspecified'
    return dict(rows=1, is_null=False, local_ticks=ticks, offset_minutes=off, typed=typed, identity=physical(spec['profile']),
        reader_route=route('reader'), ff9=f'{date.year:04}-{date.month:02}-{date.day:02} {hour:02}:{minute:02}:{second:02}.{ns:09}' + (' +05:30' if dto else ''),
        metadata=dict(c_type=ctype, width=len(payload), scale=spec['scale'], mask=0, is_bdta=False, metadata_role='result_column',
            schema_scale=spec['scale'], raw_source='ResultColumn', raw_scale_status='captured_server_source', raw_c_type=ctype,
            raw_precision=len(payload), raw_scale=spec['scale'], raw_mask_before=0, raw_derived_mask=0),
        raw=dict(source='ordinary_result_cache_before_temporal_projection', capture_stage='before_any_temporal_getter',
            is_bdta=False, c_type=ctype, width=len(payload), scale=spec['scale'], hex=payload.hex().upper(),
            payload_length=len(payload), payload_sha256=hashlib.sha256(payload).hexdigest(), decoded={'producer': 'ignored'}))


def reuse_item(spec):
    """The actual frozen RoundtripCases/RuntimeHarness repeated-step protocol."""
    q = 1 if spec['profile'] == 'shared' else 10
    expected = [(spec['kind'], spec['ticks'], spec['offset_minutes']),
        (spec['kind'], spec['ticks'] + q, spec['offset_minutes']), (spec['kind'], None, spec['offset_minutes'])]
    column = 'Z' if spec['kind'] == 'dto' else 'D'
    item = dict(case_id=spec['case_id'], definition=dict(spec), owned_table='TPL_123456789ABCDEF012345678', steps=[], oracle_read_attempts=[])
    initial = packet_oracle(spec)
    changed = packet_oracle(dict(spec, ticks=spec['ticks'] + q))
    null = dict(rows=1, is_null=True, local_ticks=None, offset_minutes=None, raw=None, ff9=None,
        typed={'type': 'System.DBNull', 'is_null': True}, metadata=copy.deepcopy(initial['metadata']),
        identity=physical(spec['profile']), reader_route=route('reader'))
    def business(name): item['steps'].append(dict(step_id=name,status='completed',call_returned=True))
    def independent(index, observation):
        item['steps'].append(dict(step_id='fresh_independent_' + column,status='completed',result=observation))
        item['independent_' + column + '_' + str(index)] = observation
        item['oracle_read_attempts'].append(dict(table=item['owned_table'],row_id=1,column=column,rows=1,
            completed=True,identity=observation['identity'],metadata=observation['metadata']))
    business('migration_command_0'); business('SaveChanges_insert'); business('fresh_AsNoTracking'); independent(1,initial)
    business('SaveChanges_value_only'); business('actual_parameterized_query_changed_value'); independent(2,changed)
    business('SaveChanges_NULL_only'); business('fresh_AsNoTracking_NULL'); independent(3,null)
    return item, expected


def interval_item(fields, text):
    """Handwritten positive raw packet and bounded projection receipt; no driver encoder."""
    d,h,m,s,micro=fields
    ticks=((d*24+h)*3600+m*60+s)*10000000+micro*10
    raw=struct.pack('<6i',d,h,m,s,micro,1686).hex().upper()
    item=dict(independent_native=dict(rows=1,is_null=False,ticks=ticks,raw_hex=raw,capture='fresh_ordinary_cache_before_temporal_getter'),
        native_oracle_attempt=dict(status='completed',source='fresh_ordinary_native_raw_integer_oracle',raw_hex=raw,
            raw_captured_before_temporal_getter=True,c_type=21,wire_scale=1686))
    interval_text_receipt(item,text)
    return item,dict(kind='interval',ticks=ticks)


def interval_text_receipt(item,text):
    attempt=item['native_oracle_attempt']
    attempt.update(server_text=text,server_text_character_length=len(text),server_text_processing_limit=120,
        server_text_utf8_sha256=hashlib.sha256(text.encode('utf-8')).hexdigest())


def concurrency_fixture(kind,async_api):
    """Complete handwritten case with the measured four-role/public-index shape; no SQL reconstruction."""
    spec=next(copy.deepcopy(s) for s in V.specs('shared','mutation') if s['shape']=='concurrency' and s['kind']==kind and s['async_api'] is async_api)
    report=dict(commands=[],parameter_describe_sources=[],protocol_responses=[],sent_frames=[],_oracle=L0)
    t=spec['ticks']; off=spec['offset_minutes']
    def temporal(kind0,ticks):
        p=parameter(kind0,ticks,7); p['provider_type']='DateTime' if kind0=='dt' else 'DateTimeOffset'
        return p
    def non_temporal(role,label='first'):
        return dict(db_type='Int32' if role=='id' else 'AnsiString',provider_type='Int32' if role=='id' else 'VarChar',
            precision=0,scale=0,explicit_precision=False,explicit_scale=False,direction='Input',
            value={'clr_type':'Int32','value':1} if role=='id' else {'clr_type':'String','value':label})
    def append_stage(step,public,server_order,raws):
        attempt=len(report['parameter_describe_sources'])+1; stage=spec['case_id']+'/'+step
        identity=[10+attempt,1,4,9]; efid=f'{attempt:08x}-1234-1234-1234-123456789abc'
        params=[]; bindings=[]
        for ordinal,(role,index) in enumerate(server_order):
            rr=raws[role]
            params.append(dict(ordinal=ordinal,public_parameter_index=index,server_inout_type=0,request=public[index],raw=rr,
                server_parameter_name_sha256=hashlib.sha256(('server-'+role).encode()).hexdigest(),
                public_parameter_name_sha256=hashlib.sha256(('public-'+role).encode()).hexdigest()))
            time=public[index]['db_type'] in ('DateTime2','DateTimeOffset'); fixed=rr['type_flag']==1
            bindings.append(dict(ordinal=ordinal,raw_available=True,**{'raw_'+key:val for key,val in rr.items() if key!='available'},
                binding_available=time,reliable_fixed=fixed if time else None,c_type=rr['c_type'] if time else None,
                encoded_scale=7 if time else None,wire_scale=7 if time else None,requested_scale=7 if time else None,
                column_scale=7 if time and fixed else None,request_explicit_scale=True if time else None,profile_state='VerifiedSeven' if time else None))
        start=dict(stage=stage,status='attempted',api='ReaderExecutingAsync' if async_api else 'ReaderExecuting',attempt_id=attempt,
            ef_command_id=efid,sql_sha256='a'*64,sql_length=135,command=route('command'),connection=route('connection'),
            parameters=[dict(p,actual=route('parameter')) for p in public])
        report['commands'] += [start,dict(stage=stage,status='completed',api='ReaderExecutedAsync' if async_api else 'ReaderExecuted',
            attempt_id=attempt,sql_sha256='a'*64,row_count=None,binding=bindings,reader=route('reader'))]
        report['protocol_responses'].append(dict(stage=stage,attempt_id=attempt,sequence=attempt*2-1,request_opcode=5,response_opcode=200,
            sql_code=0,body_length=32,operation_identity=identity))
        report['parameter_describe_sources'].append(dict(stage=stage,attempt_id=attempt,ef_command_id=efid,sequence=attempt*2,
            response_sequence=attempt*2-1,request_opcode=5,capture_point='after_prepare_decode_before_bind',operation_identity=identity,
            server_statement_id=3,statement_reference_token=attempt+100,command_reference_token=attempt+200,
            connection_reference_token=attempt+300,parameters=params))
        if async_api: report['sent_frames'].append(dict(stage=stage,opcode=13,session_id=identity[0],lease_generation=1,execution_id=4,invocation_id=9))
    nraw=dict(available=True,source='ParameterDescribe',c_type=2,precision=32,scale=0,type_flag=1,mask=0,derived_mask=0,result_format='Unknown')
    idraw=dict(nraw,c_type=7,precision=4,type_flag=2)
    public=[non_temporal('id'),temporal('dt',t if kind=='dt' else None),temporal('dto',t if kind=='dto' else None),non_temporal('n')]
    append_stage('seed_SaveChanges',public,[('id',0),('d',1),('z',2),('n',3)],
        dict(id=copy.deepcopy(idraw),d=raw('dt',7),z=raw('dto',7),n=copy.deepcopy(nraw)))
    for step,delta,label in (('winner_SaveChanges',1,'changed'),('stale_SaveChanges',2,'stale')):
        public=([non_temporal('id'),temporal(kind,t+delta),temporal(kind,t),non_temporal('n',label)] if kind=='dt' else
            [non_temporal('id'),non_temporal('n',label),temporal(kind,t+delta),temporal(kind,t)])
        order=[('current',1),('n',3),('id',0),('original',2)] if kind=='dt' else [('n',1),('current',2),('id',0),('original',3)]
        append_stage(step,public,order,dict(current=raw(kind,7),original=dict(raw(kind,7),type_flag=2),id=copy.deepcopy(idraw),n=copy.deepcopy(nraw)))
    def entity(ticks,label):
        result=dict(id=1,d={'is_null':True},z={'is_null':True},n={'clr_type':'String','value':label})
        result['d' if kind=='dt' else 'z']=temporal(kind,ticks)['value']; return result
    pk='TP07PK_HANDWRITTEN'; table='TPL_HANDWRITTEN'
    catalog=dict(columns=[dict(name='ID',data_type='INT',precision=None,scale=0,nullable='N',data_length=4),
        dict(name='D',data_type='TIMESTAMP',precision=None,scale=7,nullable='Y',data_length=9),
        dict(name='Z',data_type='DATETIME WITH TIME ZONE',precision=None,scale=7,nullable='Y',data_length=11),
        dict(name='N',data_type='VARCHAR',precision=None,scale=0,nullable='Y',data_length=32)],
        primary_key_name_checked_against_catalog=pk,primary_key_contract='exactly_one_owned_primary_key',verification_source='OwnedObjects.Preflight')
    state=dict(rows=1,catalog=catalog,catalog_sha256=hashlib.sha256(json.dumps(catalog,indent=2,ensure_ascii=False).encode()).hexdigest())
    def step(name,result=None):
        x=dict(step_id=name,status='completed',call_returned=True)
        if result is not None: x['result']=copy.deepcopy(result)
        return x
    actual_chain=chain('Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException')
    observed=packet_oracle(dict(spec,ticks=t+1))
    item=dict(case_id=spec['case_id'],status='completed',definition={key:val for key,val in spec.items() if key not in ('async_api','contract')},
        owned_table=table,owned_primary_key=pk,original_temporal_concurrency_token=temporal(kind,t)['value'],
        expected_concurrency_exception=dict(actual_chain=actual_chain,expected_leaf_type='Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException'),
        steps=[step('seed_SaveChanges'),step('fresh_state_before_mutation',state),step('tracking_query_current',entity(t,'first')),
            step('tracking_query_stale',entity(t,'first')),step('winner_SaveChanges'),step('fresh_state_after_winner_before_stale',state),
            dict(step_id='stale_SaveChanges',status='failed',actual_exception_chain=actual_chain),
            step('fresh_after_stale_failure',entity(t+1,'changed')),
            dict(step_id='fresh_independent_'+('Z' if kind=='dto' else 'D'),status='completed',result=observed),
            step('fresh_state_after_stale_failure',state)],
        oracle_read_attempts=[dict(completed=True,table=table,row_id=1,column='Z' if kind=='dto' else 'D',rows=1,
            identity=observed['identity'],metadata=observed['metadata'])])
    item['definition']['async']=async_api
    item['independent_'+('Z' if kind=='dto' else 'D')+'_1']=observed
    return dict(report=report,item=item,spec=spec)


def tls_concurrency_fixture(kind,async_api):
    """Independent synthetic TLS6 case shaped from frozen e01a13ac observations; never live acceptance."""
    spec=next(copy.deepcopy(s) for s in V.specs('tls','mutation') if s['shape']=='concurrency' and s['kind']==kind and s['async_api'] is async_api)
    report=dict(commands=[],parameter_describe_sources=[],protocol_responses=[],sent_frames=[],_oracle=L0)
    t=spec['ticks']; off=spec['offset_minutes']
    def temporal(kind0,ticks):
        p=parameter(kind0,ticks,6); p['provider_type']='DateTime' if kind0=='dt' else 'DateTimeOffset'
        return p
    def non_temporal(role,label='first'):
        return dict(db_type='Int32' if role=='id' else 'AnsiString',provider_type='Int32' if role=='id' else 'VarChar',
            precision=0,scale=0,explicit_precision=False,explicit_scale=False,direction='Input',
            value={'clr_type':'Int32','value':1} if role=='id' else {'clr_type':'String','value':label})
    def append_stage(step,public,server_order,raws):
        attempt=len(report['parameter_describe_sources'])+1; stage=spec['case_id']+'/'+step
        identity=[10+attempt,1,4,9]; efid=f'{attempt:08x}-1234-1234-1234-123456789abc'
        params=[]; bindings=[]
        for ordinal,(role,index) in enumerate(server_order):
            rr=raws[role]
            params.append(dict(ordinal=ordinal,public_parameter_index=index,server_inout_type=0,request=public[index],raw=rr,
                server_parameter_name_sha256=hashlib.sha256(('server-'+role).encode()).hexdigest(),
                public_parameter_name_sha256=hashlib.sha256(('public-'+role).encode()).hexdigest()))
            time=public[index]['db_type'] in ('DateTime2','DateTimeOffset'); fixed=rr['type_flag']==1
            bindings.append(dict(ordinal=ordinal,raw_available=True,**{'raw_'+key:val for key,val in rr.items() if key!='available'},
                binding_available=time,reliable_fixed=fixed if time else None,c_type=rr['c_type'] if time else None,
                encoded_scale=6 if time else None,wire_scale=6 if time else None,requested_scale=6 if time else None,
                column_scale=6 if time and fixed else None,request_explicit_scale=True if time else None,profile_state='VerifiedSix' if time else None))
        start=dict(stage=stage,status='attempted',api='ReaderExecutingAsync' if async_api else 'ReaderExecuting',attempt_id=attempt,
            ef_command_id=efid,sql_sha256='a'*64,sql_length=135,command=route('command'),connection=route('connection'),
            parameters=[dict(p,actual=route('parameter')) for p in public])
        report['commands'] += [start,dict(stage=stage,status='completed',api='ReaderExecutedAsync' if async_api else 'ReaderExecuted',
            attempt_id=attempt,sql_sha256='a'*64,row_count=None,binding=bindings,reader=route('reader'))]
        report['protocol_responses'].append(dict(stage=stage,attempt_id=attempt,sequence=attempt*2-1,request_opcode=5,response_opcode=0,
            sql_code=0,body_length=32,operation_identity=identity))
        report['parameter_describe_sources'].append(dict(stage=stage,attempt_id=attempt,ef_command_id=efid,sequence=attempt*2,
            response_sequence=attempt*2-1,request_opcode=5,capture_point='after_prepare_decode_before_bind',operation_identity=identity,
            server_statement_id=3,statement_reference_token=attempt+100,command_reference_token=attempt+200,
            connection_reference_token=attempt+300,parameters=params))
        if async_api: report['sent_frames'].append(dict(stage=stage,opcode=13,session_id=identity[0],lease_generation=1,execution_id=4,invocation_id=9))
    nraw=dict(available=True,source='ParameterDescribe',c_type=2,precision=32,scale=0,type_flag=1,mask=0,derived_mask=0,result_format='Unknown')
    idraw=dict(nraw,c_type=7,precision=4,type_flag=2)
    public=[non_temporal('id'),temporal('dt',t if kind=='dt' else None),temporal('dto',t if kind=='dto' else None),non_temporal('n')]
    append_stage('seed_SaveChanges',public,[('id',0),('d',1),('z',2),('n',3)],
        dict(id=copy.deepcopy(idraw),d=raw('dt',6,extended=False),z=raw('dto',6,extended=False),n=copy.deepcopy(nraw)))
    for step,delta,label in (('winner_SaveChanges',10,'changed'),('stale_SaveChanges',20,'stale')):
        public=([non_temporal('id'),temporal(kind,t+delta),temporal(kind,t),non_temporal('n',label)] if kind=='dt' else
            [non_temporal('id'),non_temporal('n',label),temporal(kind,t+delta),temporal(kind,t)])
        order=[('current',1),('n',3),('id',0),('original',2)] if kind=='dt' else [('n',1),('current',2),('id',0),('original',3)]
        append_stage(step,public,order,dict(current=raw(kind,6,extended=False),original=dict(raw(kind,6,extended=False),type_flag=2),id=copy.deepcopy(idraw),n=copy.deepcopy(nraw)))
    def entity(ticks,label):
        result=dict(id=1,d={'is_null':True},z={'is_null':True},n={'clr_type':'String','value':label})
        result['d' if kind=='dt' else 'z']=temporal(kind,ticks)['value']; return result
    pk='TP07PK_TLS_SYNTHETIC'; table='TPL_TLS_SYNTHETIC'
    catalog=dict(columns=[dict(name='ID',data_type='INT',precision=None,scale=0,nullable='N',data_length=4),
        dict(name='D',data_type='TIMESTAMP',precision=None,scale=6,nullable='Y',data_length=8),
        dict(name='Z',data_type='DATETIME WITH TIME ZONE',precision=None,scale=6,nullable='Y',data_length=10),
        dict(name='N',data_type='VARCHAR',precision=None,scale=0,nullable='Y',data_length=32)],
        primary_key_name_checked_against_catalog=pk,primary_key_contract='exactly_one_owned_primary_key',verification_source='OwnedObjects.Preflight')
    state=dict(rows=1,catalog=catalog,catalog_sha256=hashlib.sha256(json.dumps(catalog,indent=2,ensure_ascii=False).encode()).hexdigest())
    def step(name,result=None):
        x=dict(step_id=name,status='completed',call_returned=True)
        if result is not None: x['result']=copy.deepcopy(result)
        return x
    actual_chain=chain('Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException')
    observed=packet_oracle(dict(spec,ticks=t+10))
    item=dict(case_id=spec['case_id'],status='completed',definition={key:val for key,val in spec.items() if key not in ('async_api','contract')},
        owned_table=table,owned_primary_key=pk,original_temporal_concurrency_token=temporal(kind,t)['value'],
        expected_concurrency_exception=dict(actual_chain=actual_chain,expected_leaf_type='Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException'),
        steps=[step('seed_SaveChanges'),step('fresh_state_before_mutation',state),step('tracking_query_current',entity(t,'first')),
            step('tracking_query_stale',entity(t,'first')),step('winner_SaveChanges'),step('fresh_state_after_winner_before_stale',state),
            dict(step_id='stale_SaveChanges',status='failed',actual_exception_chain=actual_chain),
            step('fresh_after_stale_failure',entity(t+10,'changed')),
            dict(step_id='fresh_independent_'+('Z' if kind=='dto' else 'D'),status='completed',result=observed),
            step('fresh_state_after_stale_failure',state)],
        oracle_read_attempts=[dict(completed=True,table=table,row_id=1,column='Z' if kind=='dto' else 'D',rows=1,
            identity=observed['identity'],metadata=observed['metadata'])])
    item['definition']['async']=async_api
    item['independent_'+('Z' if kind=='dto' else 'D')+'_1']=observed
    return dict(report=report,item=item,spec=spec)


def window_context_fixture():
    """Handwritten source-join identities; never an execution/build approval."""
    startup=dict(replay_root='/handwritten/replay',source_manifest_sha256='1'*64,lock_manifest_sha256='2'*64,
        source_hashes={'test/'+k:v for k,v in V.policy()['E_template_hashes'].items()},
        startup_assemblies=[dict(name='TP07TemporalRuntime',dll_sha256='3'*64,mvid='12345678-1234-1234-1234-123456789abc',deps_sha256='4'*64)])
    return dict(documents={'startup':startup},approval=dict(original_repo=V.policy()['original_repo'],dotnet={'path':'/handwritten/dotnet'},
        artifacts=dict(startup={'path':'/handwritten/startup.json','sha256':'5'*64},candidate={'path':'/handwritten/candidate.json'},capabilities={'path':'/handwritten/capabilities.json'})),
        execution_reference=dict(path='/handwritten/approval.json',sha256='6'*64),
        pack=dict(manifest_path='/handwritten/toolpack.json',manifest_sha256='7'*64,source_hashes={}),
        pins={'/handwritten/source':'8'*64},oracle=L0)


def partition_fixture(context):
    startup=context['documents']['startup']; host=startup['startup_assemblies'][0]
    return dict(schema_version=1,task='TP07-E offline partition',database_accessed=False,candidate_guard_constructed=False,
        owned_objects_constructed=False,runtime_context_constructed=False,process_exit_code=0,window_size=15,
        cases=[V.runtime_definition(s) for p in V.PROFILES for g in V.GROUPS for s in V.specs(p,g)],windows=V.windows(),
        host_identity={k:host[k] for k in ('dll_sha256','mvid','deps_sha256')},argument_refusals=[dict(test_id=k,status='refused',assertion='handwritten_expected_guard')
        for k in ('missing_index','negative_index','out_of_range','non_integer','arbitrary_size','arbitrary_range','arbitrary_ids','generated_mix','partition_mix')])


def coverage_fixture(context,run_id):
    result=[]; ordinal=0
    for w in V.windows():
        owned=[]
        for _ in w['case_ids']:
            ordinal+=1;nonce=f'{ordinal:08X}'+'A'*24
            owned.append(dict(nonce=nonce,table='TPL_'+nonce[:24],primary_key='TP07PK_'+nonce[:24]))
        result.append(dict(**V.window_metadata(context,run_id,w),e_window_contract_valid=True,cases_validated=w['count'],
            validated_case_ids=copy.deepcopy(w['case_ids']),owned_objects=owned))
    return result


def window_join_fixture(root,context,run_id):
    """Real file/hash/path joins, with business/raw validation stubbed only by the caller test."""
    w=V.window_descriptor('shared','roundtrip',0);stage=root/'shared/roundtrip/window-000';stage.mkdir(parents=True)
    for role in ('main','cleanup'): (stage/role).mkdir()
    meta=V.window_metadata(context,run_id,w)
    V.write(root/'run-receipt.json',dict(schema_version=2,task='TP07-E window run',status='running',run_id=run_id,cohort_id=meta['cohort_id'],
        toolpack=context['pack'],approved_execution=context['execution_reference'],policy_sha256=meta['policy_sha256'],window_contract_sha256=meta['window_contract_sha256'],
        input_hashes_before=context['pins'],input_hashes_after=context['pins'],groups=[dict(profile='shared',group='roundtrip',windows=[dict(window=w)])]))
    def ref(path):return dict(path=str(path),sha256=V.sha(path))
    V.write(stage/'main/ownership.json',dict(objects=coverage_fixture(context,run_id)[0]['owned_objects']))
    ledger_sha=V.sha(stage/'main/ownership.json')
    V.write(stage/'main/runtime.json',dict(schema_version=2,task='TP07-E',run_id=run_id,cohort_id=meta['cohort_id'],window=w,
        cleanup={'ledger_sha256':ledger_sha},cases=[dict(case_id=k) for k in w['case_ids']]))
    V.write(stage/'cleanup/report.json',dict(cleanup={'ledger_sha256':ledger_sha}))
    artifacts=dict(report=ref(stage/'main/runtime.json'),ledger=ref(stage/'main/ownership.json'),cleanup=ref(stage/'cleanup/report.json'))
    for role in ('main','cleanup'):
        name='TP07TemporalRuntime' if role=='main' else 'TP07LiteralProbe';approval=context['approval'];refs=approval['artifacts']
        command=[approval['dotnet']['path'],str(Path(context['documents']['startup']['replay_root'])/('test/'+name+'/bin/Debug/net10.0/'+name+'.dll')),
            '--profile','shared','--candidate-manifest',refs['candidate']['path'],'--capabilities',refs['capabilities']['path'],'--output',str(stage/role)]
        if role=='main':command+=['--group','roundtrip','--window-index','0','--run-id',run_id,'--cohort-id',meta['cohort_id']]
        else:command+=['--repo',approval['original_repo'],'--build-manifest',refs['startup']['path'],'--build-manifest-sha256',refs['startup']['sha256'],'--cleanup','--ledger',str(stage/'main/ownership.json')]
        path=stage/role/'E-host-receipt.json';V.write(path,dict(**meta,task='TP07-E internal window host',cleanup=role=='cleanup',child_exit_code=0,command=command,
            input_hashes_before=context['pins'],input_hashes_after=context['pins']));artifacts[role+'_host']=ref(path)
    def process(role,deadline):return dict(command=V.window_host_command(stage,w,context,run_id,role=='cleanup'),cwd=context['documents']['startup']['replay_root'],
        log_path=str(stage/(role+'.log')),deadline_seconds=deadline,exit_code=0,host_exit_code=0,timed_out=False,termination_code=None,actual_elapsed_seconds=1)
    V.write(stage/'window-receipt.json',dict(**meta,task='TP07-E window run',input_hashes_before=context['pins'],input_hashes_after=context['pins'],
        main=process('main',900),cleanup=process('cleanup',300),artifacts=artifacts))
    return stage


def advisory_fixture(kind,fixed=False):
    """Complete synthetic controlled refusal plus an earlier same-window native EF send control; no live evidence."""
    spec=next(copy.deepcopy(s) for s in V.specs('tls','six') if s['shape']=='advisory7' and s['kind']==kind)
    w=V.window_descriptor('tls','six',3);positive_stage=w['case_ids'][0]+'/SaveChanges';stage=spec['case_id']+'/advisory_ExecuteAsync'
    report=dict(window=w,commands=[],parameter_describe_sources=[],protocol_responses=[],sent_frames=[],async_write_positive_controls=[],_oracle=L0)
    def prepare(stage,attempt,request,raw_,controlled):
        operation=[attempt,1,6,13];before=dict(stage=stage,status='attempted',attempt_id=attempt,
            api='controlled_ADO_ExecuteNonQueryAsync' if controlled else 'ReaderExecutingAsync',ef_command_id=None if controlled else '12345678-1234-1234-1234-123456789abc',
            sql_sha256=hashlib.sha256(b'SELECT :v FROM DUAL').hexdigest() if controlled else 'a'*64,sql_length=19 if controlled else 100,
            command=route('command'),connection=route('connection'),parameters=[dict(request,actual=route('parameter'))])
        p=dict(ordinal=0,public_parameter_index=0,server_inout_type=0,public_parameter_name_sha256=hashlib.sha256(b'v').hexdigest(),
            server_parameter_name_sha256=hashlib.sha256(b'V').hexdigest(),request=request,raw=raw_)
        source_=dict(stage=stage,attempt_id=attempt,ef_command_id=before['ef_command_id'],sequence=attempt*2,response_sequence=attempt*2-1,
            request_opcode=5,capture_point='after_prepare_decode_before_bind',operation_identity=operation,server_statement_id=5,
            statement_reference_token=11,command_reference_token=12,connection_reference_token=13,parameters=[p])
        report['commands'].append(before);report['parameter_describe_sources'].append(source_)
        report['protocol_responses'].append(dict(stage=stage,attempt_id=attempt,sequence=attempt*2-1,request_opcode=5,response_opcode=0,sql_code=0,body_length=32,operation_identity=operation))
        return before,source_,operation
    positive_request=dict(db_type='Date',provider_type='Date',precision=0,scale=0,explicit_precision=False,explicit_scale=True,direction='Input',
        value={'clr_type':'DateOnly','day_number':739819})
    positive_raw=dict(available=True,source='ParameterDescribe',c_type=14,precision=3,scale=0,type_flag=1,mask=0,derived_mask=0,result_format='Unknown')
    positive,positive_source,identity=prepare(positive_stage,1,positive_request,positive_raw,False)
    binding=dict(ordinal=0,binding_available=True,raw_available=True,reliable_fixed=True,c_type=14,requested_scale=0,encoded_scale=0,wire_scale=0,column_scale=0,
        request_explicit_scale=True,profile_state='VerifiedSix',**{'raw_'+k:v for k,v in positive_raw.items() if k!='available'})
    positive_end=dict(stage=positive_stage,status='completed',attempt_id=1,api='ReaderExecutedAsync',sql_sha256=positive['sql_sha256'],binding=[binding],reader=route('reader'))
    report['commands'].append(positive_end)
    frame=dict(stage=positive_stage,opcode=13,session_id=identity[0],lease_generation=1,execution_id=6,invocation_id=13);report['sent_frames'].append(frame)
    control=dict(stage=positive_stage,source='actual_successful_EF_SaveChangesAsync_AfterFrameSent',business_execute_frames=1,prepare_source=positive_source,
        completed_command_attempt=positive_end,sent_identity=[frame]);report['async_write_positive_controls'].append(control)
    request=parameter(kind,spec['ticks']);request['provider_type']='DateTime' if kind=='dt' else 'DateTimeOffset'
    rr=raw(kind,7) if fixed else dict(available=True,source='ParameterDescribe',c_type=2,precision=32767,scale=0,type_flag=2,mask=0,derived_mask=0,result_format='Unknown')
    before,source_,identity=prepare(stage,2,request,rr,True);actual_chain=chain('System.NotSupportedException')
    failed=dict(stage=stage,status='failed',attempt_id=2,sql_sha256=before['sql_sha256'],actual_exception_chain=actual_chain,
        current_binding_observed=False,current_binding=None,parameter_describe_sources=[source_]);report['commands'].append(failed)
    for opcode in (3,5):report['sent_frames'].append(dict(stage=stage,opcode=opcode,session_id=identity[0],lease_generation=1,execution_id=6,invocation_id=13))
    item=dict(case_id=spec['case_id'],status='completed',definition=V.runtime_definition(spec),owned_primary_key='TP07PK_SYNTHETIC_ADVISORY',
        route='EF_mapping_created_W_parameter_controlled_ADO_execute_not_SaveChanges',parameter=dict(actual=route('parameter'),precision=0,scale=7,explicit_scale=True,db_type=request['db_type']),
        actual_parameter_describe=[source_],parameter_describe_observed=True,metadata_evidence_source='same_invocation_after_prepare_decode_before_bind',
        source_branch='actual_fixed7_extended_ParameterDescribe_TLS_verified6' if fixed else 'exact_generic_ParameterDescribe_TLS_explicit7_refusal',
        actual_exception_chain=actual_chain,expected_refusal=dict(actual_chain=actual_chain,expected_leaf_type='System.NotSupportedException',current_binding_observed=False,current_binding=None,
            send_evidence=dict(observation='measured_async_Execute0',business_execute_frames=0,async_positive_controls=[control])),fresh_connection_health=physical('tls'),
        steps=[dict(step_id='open_EF_factory_connection',status='completed',call_returned=True),
            dict(step_id='advisory_ExecuteAsync',status='failed',actual_exception_chain=actual_chain),
            dict(step_id='fresh_after_TLS_refusal',status='completed',call_returned=True)])
    return dict(report=report,item=item,spec=spec)


class FiniteContracts(unittest.TestCase):
    def rejects(self, call, original, mutations):
        call(original)
        for label, mutation in mutations:
            with self.subTest(mutation=label):
                changed = copy.deepcopy(original, {id(L0): L0}); mutation(changed)
                with self.assertRaises((V.ContractError, L0.ContractError, ValueError, KeyError)): call(changed)

    def test_manifest_finite_order_counts_and_independent_inputs(self):
        p = V.policy()
        self.assertEqual(338, len(p['cases']))
        self.assertEqual([180, 158], [sum(p['required_counts'][name].values()) for name in V.PROFILES])
        self.assertEqual(338, len({row['case_id'] for row in p['cases']}))
        for profile in V.PROFILES:
            for group in V.GROUPS:
                self.assertEqual(p['required_counts'][profile][group], len(V.specs(profile, group)))
        for mutation in ('missing', 'duplicate', 'wrongprofile'):
            changed = copy.deepcopy(p)
            if mutation == 'missing': changed['cases'].pop(0)
            if mutation == 'duplicate': changed['cases'].append(changed['cases'][0])
            if mutation == 'wrongprofile': changed['cases'][0]['profile'] = 'tls'
            with mock.patch.object(V, 'policy', return_value=changed), self.assertRaises(V.ContractError): V.specs('shared', 'roundtrip')

    def test_fixed25_windows_partition_and_same_process_control_prefix(self):
        contract=V.window_contract();self.assertEqual(25,len(contract['windows']));self.assertEqual(338,sum(w['count'] for w in contract['windows']))
        self.assertEqual([13,12],[sum(w['profile']==p for w in contract['windows']) for p in V.PROFILES])
        for w in contract['windows']:V.control_prefix(V.window_specs(w['profile'],w['group'],w['index']))
        for index in (-1,4,0.0,True,'0'):
            with self.subTest(index=index),self.assertRaises(V.ContractError):V.window_specs('shared','roundtrip',index)
        for change in (lambda p:p['window_contract'].update(size=16),lambda p:p['window_contract']['windows'].pop(),
                lambda p:p['window_contract']['windows'].reverse()):
            p=copy.deepcopy(V.policy());change(p)
            with mock.patch.object(V,'policy',return_value=p),self.assertRaises(V.ContractError):V.window_contract()
        for p in V.PROFILES:
            for index in (1,2,3):
                rows=copy.deepcopy(V.window_specs(p,'six',index));V.control_prefix(rows)
                rows=[s for s in rows if not(s['async_api'] and s['contract']=='exact' and s['ticks'] is not None and s['shape'] in ('facet6','store6','native'))]
                with self.subTest(profile=p,index=index),self.assertRaisesRegex(V.ContractError,'same_window_async_control_prefix_missing'):V.control_prefix(rows)

    def test_compiled_partition_registry_source_host_and_window_mutations(self):
        context=window_context_fixture();report=partition_fixture(context)
        call=lambda x:V.validate_partition_report(x,context['documents']['startup'],'5'*64,context['pack'])
        self.rejects(call,report,[('missing_case',lambda x:x['cases'].pop()),('wrong_ticks',lambda x:x['cases'][0].update(ticks=1)),
            ('wrong_route',lambda x:x['cases'][0].update(route='controlled_ADO')),('wrong_API_type',lambda x:x['cases'][0].update(**{'async':0})),
            ('missing_window',lambda x:x['windows'].pop()),('duplicate_window',lambda x:x['windows'].append(x['windows'][0])),
            ('reordered_windows',lambda x:x['windows'].reverse()),('cross_slice',lambda x:x['windows'][1].update(start=0)),
            ('size16',lambda x:x.update(window_size=16)),('host_old',lambda x:x['host_identity'].update(dll_sha256='0'*64)),
            ('missing_argument_refusal',lambda x:x['argument_refusals'].pop()),('claimed_DB',lambda x:x.update(database_accessed=True))])
        bad=copy.deepcopy(context,{id(L0):L0});bad['documents']['startup']['source_hashes']['test/TP07TemporalRuntime/Program.cs']='0'*64
        with self.assertRaisesRegex(V.ContractError,'partition_new_E_source'):V.validate_partition_report(report,bad['documents']['startup'],'5'*64,bad['pack'])

    def test_new_run_disjoint_exhaustive_coverage_and_cohort_mutations(self):
        context=window_context_fixture();run_id='a'*32;rows=coverage_fixture(context,run_id)
        self.assertEqual(338,V.coverage(rows,context,run_id))
        self.rejects(lambda x:V.coverage(x,context,run_id),rows,[('missing',lambda x:x.pop()),('duplicate',lambda x:x.append(x[0])),
            ('extra',lambda x:x.append(dict(x[0],window=dict(x[0]['window'],index=99)))),('reordered',lambda x:x.reverse()),
            ('mixed_run',lambda x:x[1].update(run_id='b'*32)),('mixed_cohort',lambda x:x[1].update(cohort_id='b'*64)),
            ('mixed_pack',lambda x:x[1]['toolpack'].update(manifest_sha256='b'*64)),('mixed_policy',lambda x:x[1].update(policy_sha256='b'*64)),
            ('wrong_registry_ID',lambda x:x[1]['validated_case_ids'].__setitem__(0,x[0]['validated_case_ids'][0])),
            ('producer_count',lambda x:x[1].update(cases_validated=14)),('duplicate_nonce',lambda x:x[1]['owned_objects'][0].update(nonce=x[0]['owned_objects'][0]['nonce'])),
            ('missing_owned',lambda x:x[1]['owned_objects'].pop())])

    def test_window_receipt_canonical_host_source_ledger_and_deadline_joins(self):
        context=window_context_fixture();run_id='a'*32
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp).resolve();stage=window_join_fixture(root,context,run_id)
            def call():return V.validate_window(stage,'shared','roundtrip',0,context,run_id)
            # Only the old complete business/raw oracle and SQL body checks are stubbed here;
            # the actual file hashes, canonical paths, source/run/window and executed command joins run.
            with mock.patch.object(V,'unchanged',side_effect=lambda p:p),mock.patch.object(V,'pack_check'),\
                    mock.patch.object(V,'validate_report',return_value=15),mock.patch.object(V,'generated_sql_contract'):
                self.assertTrue(call()['e_window_contract_valid'])
                receipt_path=stage/'window-receipt.json';original=V.load(receipt_path)
                mutations=[('old_schema',lambda x:x.update(schema_version=1)),('wrong_run',lambda x:x.update(run_id='b'*32)),
                    ('wrong_source',lambda x:x['input_hashes_after'].update(extra='b'*64)),('wrong_window',lambda x:x['window'].update(index=1)),
                    ('deadline900_expanded',lambda x:x['main'].update(deadline_seconds=901)),('deadline300_expanded',lambda x:x['cleanup'].update(deadline_seconds=301)),
                    ('failed_cleanup',lambda x:x['cleanup'].update(exit_code=1)),('wrong_main_command',lambda x:x['main']['command'].append('--resume')),
                    ('other_window_ledger',lambda x:x['artifacts']['ledger'].update(path=str(stage.parent/'window-001/main/ownership.json'))),
                    ('other_run_report',lambda x:x['artifacts']['report'].update(path=str(root.parent/'other/main/runtime.json')))]
                for name,change in mutations:
                    with self.subTest(mutation=name):
                        altered=copy.deepcopy(original);change(altered);V.write(receipt_path,altered)
                        with self.assertRaises((V.ContractError,KeyError)):call()
                V.write(receipt_path,original)
                host_path=stage/'main/E-host-receipt.json';host=V.load(host_path);host['command'][host['command'].index('--window-index')+1]='1';V.write(host_path,host)
                modified=copy.deepcopy(original);modified['artifacts']['main_host']['sha256']=V.sha(host_path);V.write(receipt_path,modified)
                with self.assertRaisesRegex(V.ContractError,'exact_executed_window_startup_command'):call()

    def test_generated28_semantic_baseline_defaults_seeds_SQL_and_IO(self):
        # Handwritten shape of the deep readback fields. Actual Low gates require pinned v4 evidence.
        prior=dict(case_id='OFF-DDL-shared-HasData',shape='HasData',
            runtime_spec=dict(table='TP07_OFF_SHARED_09',shape='HasData',default_d=None,default_z=None,seed_ticks=639204129161234567),
            database_spec=dict(table='TP07_OFF_SHARED_09',shape='HasData',default_d=None,default_z=None,seed_ticks=639204129161234567),
            runtime_model=[dict(name='Id'),dict(name='D',precision=7),dict(name='N',unicode=False,max_length=32),dict(name='Z',precision=7)],
            database_model=[dict(name='Id'),dict(name='D',precision=7),dict(name='N',unicode=False,max_length=32),dict(name='Z',precision=7)],
            columns=[dict(name='ID'),dict(name='D',precision=7),dict(name='N',is_unicode=False,max_length=32),dict(name='Z',precision=7)],
            migration_operations=['CreateTableOperation','InsertDataOperation'],seed_operations=[dict(table='TP07_OFF_SHARED_09',columns=['ID','D','Z','N'],values=[1,639204129161234567,639204129161234567,'seed'])],
            generated_SQL=[dict(path='model-09-00.sql',sha256='a'*64,transaction_suppressed=True),dict(path='model-09-01.sql',sha256='b'*64,transaction_suppressed=True)],
            io=dict(sync_open_attempts=0,async_open_attempts=0,sync_command_attempts=0,async_command_attempts=0,factory_connection_count=1,
                all_factory_connections_closed=True,factory_connections=[dict(state='Closed',actual=route('connection'))]))
        self.rejects(lambda x:V.generated28_semantics(x,prior),prior,[
            ('database_precision',lambda x:x['columns'][1].update(precision=6)),('runtime_model_unicode',lambda x:x['runtime_model'][2].update(unicode=True)),
            ('seed_ticks',lambda x:x['seed_operations'].clear()),('missing_operation',lambda x:x['migration_operations'].pop()),
            ('default_sql',lambda x:x['database_spec'].update(default_d='NULL')),('raw_SQL_hash',lambda x:x['generated_SQL'][0].update(sha256='0'*64)),
            ('IO_open',lambda x:x['io'].update(sync_open_attempts=1)),('IO_factory_open',lambda x:x['io']['factory_connections'][0].update(state='Open'))])

    def test_TLS_advisory_generic_and_original_fixed_complete_DT_DTO(self):
        for kind in ('dt','dto'):
            for fixed in (False,True):
                with self.subTest(kind=kind,fixed=fixed):
                    f=advisory_fixture(kind,fixed)
                    V.validate_case(f['report'],f['item'],f['spec'])
                    if not fixed:
                        with self.assertRaises(V.ContractError):V.raw_tuple(f['report']['parameter_describe_sources'][1]['parameters'][0]['raw'],kind,7)

    def test_TLS_advisory_raw_request_source_command_refusal_control_health_mutations(self):
        for kind in ('dt','dto'):
            f=advisory_fixture(kind)
            def call(x):V.validate_case(x['report'],x['item'],x['spec'])
            def src(x):return x['report']['parameter_describe_sources'][1]
            def p(x):return src(x)['parameters'][0]
            def start(x):return x['report']['commands'][2]
            def end(x):return x['report']['commands'][3]
            def request(x,**changes):p(x)['request'].update(changes);start(x)['parameters'][0].update(changes)
            def old_partial(x):
                x['item']['steps'].pop();x['item'].pop('expected_refusal');x['item'].pop('fresh_connection_health');x['item'].pop('source_branch')
            def newer_response(x):
                src(x)['sequence']=5;x['report']['protocol_responses'].append(dict(x['report']['protocol_responses'][1],sequence=4))
            mutations=[]
            for key,value_ in [('c_type',26),('precision',32766),('scale',7),('type_flag',1),('mask',1),('derived_mask',1),('result_format','BDTA'),('source','ResultColumn'),('available',False),('available',1),('type_flag',2.0),('scale',False)]:
                mutations.append(('raw_'+key+'_'+str(value_),lambda x,k=key,v=value_:p(x)['raw'].update({k:v})))
            mutations += [('missing_raw_field',lambda x:p(x)['raw'].pop('precision')),
                ('producer_branch',lambda x:x['item'].update(source_branch='actual_fixed7_extended_ParameterDescribe_TLS_verified6')),
                ('SQL_hash',lambda x:start(x).update(sql_sha256='b'*64)),('SQL_length',lambda x:start(x).update(sql_length=18)),
                ('extra_parameter',lambda x:src(x)['parameters'].append(copy.deepcopy(p(x)))),('ordinal',lambda x:p(x).update(ordinal=1)),
                ('public_index',lambda x:p(x).update(public_parameter_index=1)),('inout',lambda x:p(x).update(server_inout_type=1)),
                ('public_name',lambda x:p(x).update(public_parameter_name_sha256='b'*64)),('server_name',lambda x:p(x).update(server_parameter_name_sha256='b'*64)),
                ('NULL',lambda x:request(x,value={'is_null':True})),('wrong_ticks',lambda x:request(x,value=dict(p(x)['request']['value'],**{'local_ticks' if kind=='dto' else 'ticks':x['spec']['ticks']+1}))),
                ('wrong_type',lambda x:request(x,db_type='String')),('wrong_provider',lambda x:request(x,provider_type='VarChar')),
                ('precision',lambda x:request(x,precision=1)),('precision_bool',lambda x:request(x,precision=False)),('scale_float',lambda x:request(x,scale=7.0)),
                ('explicit_precision',lambda x:request(x,explicit_precision=True)),('explicit_scale',lambda x:request(x,explicit_scale=False)),('direction',lambda x:request(x,direction='Output')),
                ('public_request_join',lambda x:start(x)['parameters'][0].update(scale=6)),('bad_W_parameter',lambda x:start(x)['parameters'][0]['actual'].update(type='DM.DmParameter')),
                ('EF_ID',lambda x:start(x).update(ef_command_id='12345678-1234-1234-1234-123456789abc')),
                ('source_attempt',lambda x:src(x).update(attempt_id=1)),('source_capture',lambda x:src(x).update(capture_point='after_bind')),
                ('source_operation',lambda x:src(x).update(operation_identity=[2,1,6,14])),('latest_response',newer_response),
                ('negative_Prepare',lambda x:x['report']['protocol_responses'][1].update(sql_code=-1)),('wrong_Prepare_opcode',lambda x:x['report']['protocol_responses'][1].update(response_opcode=200)),
                ('bool_Prepare_opcode',lambda x:x['report']['protocol_responses'][1].update(response_opcode=False)),('response_operation',lambda x:x['report']['protocol_responses'][1].update(operation_identity=[2,1,6,14])),
                ('failed_source_copy',lambda x:end(x).update(parameter_describe_sources=[])),('failed_attempt',lambda x:end(x).update(attempt_id=1)),
                ('wrong_exception',lambda x:end(x).update(actual_exception_chain=chain('System.ArgumentException'))),('successful_completion',lambda x:end(x).update(status='completed')),
                ('binding_fabricated',lambda x:end(x).update(binding=[])),('observed_binding',lambda x:end(x).update(current_binding_observed=True)),
                ('Execute1',lambda x:x['report']['sent_frames'].append(dict(x['report']['sent_frames'][1],opcode=13))),('wrong_sent_operation',lambda x:x['report']['sent_frames'][1].update(invocation_id=14)),
                ('no_control',lambda x:x['report']['async_write_positive_controls'].clear()),('control_missing_actual_send',lambda x:x['report']['sent_frames'].pop(0)),
                ('control_other_window',lambda x:x['report']['window'].update(index=2)),('control_wrong_operation',lambda x:x['report']['sent_frames'][0].update(invocation_id=99)),
                ('control_future_attempt',lambda x:x['report']['parameter_describe_sources'][0].update(attempt_id=3)),
                ('health_missing',lambda x:x['item'].pop('fresh_connection_health')),('health_wrong_TEST',lambda x:x['item']['fresh_connection_health'].update(server_user='OTHER')),
                ('health_wrong_profile',lambda x:x['item']['fresh_connection_health'].update(profile='shared')),('health_partial',lambda x:x['item']['steps'][-1].update(status='attempted')),
                ('old_partial_whole_case_still_fails',old_partial)]
            if kind=='dto':mutations.append(('DTO_offset',lambda x:request(x,value=dict(p(x)['request']['value'],offset_minutes=0))))
            else:mutations.append(('DT_kind',lambda x:request(x,value=dict(p(x)['request']['value'],kind='Utc'))))
            self.rejects(call,f,mutations)
            for changes in (dict(profile='shared'),dict(async_api=False),dict(shape='facet6'),dict(group='roundtrip'),dict(case_id='arbitrary-case')):
                bad=copy.deepcopy(f,{id(L0):L0});bad['spec'].update(changes)
                with self.subTest(kind=kind,wrong_lane=changes),self.assertRaises(V.ContractError):V.advisory_source_branch(bad['report'],bad['item'],bad['spec'])

    def test_operation_steps_missing_duplicate_reordered_fake_failed(self):
        item = {'steps': [dict(step_id=x, status='completed', call_returned=True) for x in ('SaveChanges', 'fresh')]}
        self.rejects(lambda x: V.steps(x, ['SaveChanges', 'fresh']), item, [
            ('missing', lambda x: x['steps'].pop()), ('duplicate', lambda x: x['steps'].append(x['steps'][0])),
            ('reordered', lambda x: x['steps'].reverse()), ('partial', lambda x: x['steps'][0].update(status='attempted')),
            ('fakefailed', lambda x: x['steps'][0].update(status='failed')), ('nocall', lambda x: x['steps'][0].update(call_returned=False))])

    def test_provider_central_and_inline_templates_same_actual_input_path(self):
        self.assertEqual('163c17cc3b8c6df3ae9b6914d079678676d9676075e910465e0810742aa7b745', hashlib.sha256(PROVIDER_XML.encode()).hexdigest())
        self.assertEqual('b7b340c8c8b90816f5cdb612acb0a26ea00856b344b081764a51b2e290d25846', hashlib.sha256(CENTRAL_XML.encode()).hexdigest())
        with tempfile.TemporaryDirectory() as folder:
            replay=Path(folder).resolve()
            provider_name='src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj'
            provider=replay/provider_name; provider.parent.mkdir(parents=True); provider.write_text(PROVIDER_XML)
            central=replay/'Directory.Packages.props'; central.write_text(CENTRAL_XML)
            names=[provider_name]+['test/'+name+'/'+name+'.csproj' for name in ('TP07TemporalRuntime','TP07LiteralProbe')]
            for name in names[1:]:
                target=replay/name; target.parent.mkdir(parents=True)
                target.write_bytes((HERE/'runtime-templates'/Path(name).relative_to('test')).read_bytes())
            self.assertEqual('f0bdfdd8698fdcf17677c878a7d33947c0dfa0c976fde4d01425e51746489f68',V.sha(replay/names[2]))
            libs={'W.DmProvider/'+V.policy()['W']['version']:{'type':'package'}}
            libs.update({package+'/10.0.12':{'type':'package'} for package in ('Microsoft.EntityFrameworkCore','Microsoft.EntityFrameworkCore.Relational','Microsoft.EntityFrameworkCore.Abstractions')})
            assets=[]; asset_documents=[]
            provider_target=str(replay/provider_name)
            for index,name in enumerate(names):
                asset=str(Path(name).parent/'obj/project.assets.json'); target=replay/asset; target.parent.mkdir()
                refs={} if index==0 else {provider_target:{'projectPath':provider_target}}
                document={'libraries':libs,'project':{'restore':{'frameworks':{'net10.0':{'projectReferences':refs}}}}}
                target.write_text(json.dumps(document)); assets.append(asset); asset_documents.append(document)
            def hashes(): return {name:V.sha(replay/name) for name in names+['Directory.Packages.props'] if (replay/name).exists()}
            for asset in assets: V.project_dependencies(replay,asset,hashes())
            negative_props=[
                ('missing_version',CENTRAL_XML.replace('    <PackageVersion Include="W.DmProvider" Version="[0.1.0-temporal.20261008144915]" />\n','')),
                ('wrong_version',CENTRAL_XML.replace('[0.1.0-temporal.20261008144915]','[0.1.0]')),
                ('wide_range',CENTRAL_XML.replace('[0.1.0-temporal.20261008144915]','[0.1.0-temporal.20261008144915,1.0.0)')),
                ('management_disabled',CENTRAL_XML.replace('>true<','>false<')),
                ('conditional_parent',CENTRAL_XML.replace('<ItemGroup>','<ItemGroup Condition="false">')),
                ('duplicate_version',CENTRAL_XML.replace('</ItemGroup>','<PackageVersion Include="W.DmProvider" Version="[0.1.0-temporal.20261008144915]" /></ItemGroup>'))]
            for label,text in negative_props:
                with self.subTest(mutation=label):
                    central.write_text(text)
                    with self.assertRaises((V.ContractError,KeyError)): V.project_dependencies(replay,assets[0],hashes())
            central.write_text(CENTRAL_XML)
            central.unlink()
            with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[0],hashes())
            central.write_text(CENTRAL_XML)
            pinned=hashes(); central.write_text(CENTRAL_XML+'\n')
            with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[0],pinned)
            central.write_text(CENTRAL_XML)
            for label,text in [('W_ProjectReference',PROVIDER_XML.replace('</Project>','<ItemGroup><ProjectReference Include="../../W.DmProvider/W.DmProvider.csproj" /></ItemGroup></Project>')),
                    ('VersionOverride',PROVIDER_XML.replace('Include="W.DmProvider" />','Include="W.DmProvider" VersionOverride="0.1.0" />'))]:
                with self.subTest(mutation=label):
                    provider.write_text(text)
                    with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[0],hashes())
            provider.write_text(PROVIDER_XML)
            for label,badlibs in [('official',dict(libs,**{'DM.DmProvider/8.0':{'type':'package'}})),
                    ('wrong_candidate',{('W.DmProvider/0.1.0' if key.startswith('W.DmProvider/') else key):val for key,val in libs.items()})]:
                with self.subTest(mutation=label):
                    document=copy.deepcopy(asset_documents[0]); document['libraries']=badlibs
                    (replay/assets[0]).write_text(json.dumps(document))
                    with self.assertRaises((V.ContractError,KeyError)): V.project_dependencies(replay,assets[0],hashes())
            for index in (1,2):
                target=replay/names[index]
                expected='[0.1.0-temporal.20261008144915]' if index==2 else '0.1.0-temporal.20261008144915'
                target.write_text(target.read_text().replace('Version="'+expected+'"','Version="0.1.0"'))
                with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[index],hashes())
                target.write_bytes((HERE/'runtime-templates'/Path(names[index]).relative_to('test')).read_bytes())
            literal=replay/names[2]; original_literal=literal.read_text()
            condition="Condition=\"'$(TP07ProviderProject)' == ''\""
            default_line='    <TP07ProviderProject '+condition+'>../../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj</TP07ProviderProject>\n'
            reference_line='<ProjectReference Include="$(TP07ProviderProject)" />'
            mutations=[
                ('missing_property',original_literal.replace(default_line,'')),
                ('changed_default',original_literal.replace('>../../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj<','>../../src/Other/Other.csproj<')),
                ('changed_condition',original_literal.replace(condition,'Condition="true"')),
                ('duplicate_property',original_literal.replace(default_line,default_line+default_line)),
                ('unknown_property',original_literal.replace(reference_line,'<ProjectReference Include="$(OtherProviderProject)" />')),
                ('literal_override',original_literal.replace(reference_line,'<ProjectReference Include="../../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj" />')),
                ('projectref_override',original_literal.replace(reference_line,'<ProjectReference Include="$(TP07ProviderProject)" AdditionalProperties="TP07ProviderProject=Other" />')),
                ('missing_reference',original_literal.replace(reference_line,'')),
                ('duplicate_reference',original_literal.replace(reference_line,reference_line+reference_line)),
                ('version_range',original_literal.replace('Version="[0.1.0-temporal.20261008144915]"','Version="[0.1.0-temporal.20261008144915,1.0.0)"')),
                ('version_override',original_literal.replace('Version="[0.1.0-temporal.20261008144915]"','Version="[0.1.0-temporal.20261008144915]" VersionOverride="0.1.0"'))]
            for label,text in mutations:
                with self.subTest(mutation=label):
                    literal.write_text(text)
                    with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[2],hashes())
            literal.write_text(original_literal)
            runtime=replay/names[1]; original_runtime=runtime.read_text()
            for label,text in [('E_property_reference',original_runtime.replace('Include="../../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj"','Include="$(TP07ProviderProject)"')),
                    ('E_bracket_version',original_runtime.replace('Version="0.1.0-temporal.20261008144915"','Version="[0.1.0-temporal.20261008144915]"')),
                    ('E_version_range',original_runtime.replace('Version="0.1.0-temporal.20261008144915"','Version="[0.1.0-temporal.20261008144915,1.0.0)"'))]:
                with self.subTest(mutation=label):
                    runtime.write_text(text)
                    with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[1],hashes())
            runtime.write_text(original_runtime)
            literal.write_text(original_literal.replace('Version="[0.1.0-temporal.20261008144915]"','Version="0.1.0-temporal.20261008144915"'))
            with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[2],hashes())
            literal.write_text(original_literal)
            pinned=hashes(); literal.write_text(original_literal+'\n')
            with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[2],pinned)
            literal.write_text(original_literal)
            other_target=str(replay/'src/OtherProvider/OtherProvider.csproj')
            for index in (0,1,2):
                for label,refs in [('redirected',{other_target:{'projectPath':other_target}}),
                        ('wrong_projectPath',{provider_target:{'projectPath':other_target}}),
                        ('extra_reference',{provider_target:{'projectPath':provider_target},other_target:{'projectPath':other_target}})]:
                    with self.subTest(project=names[index],mutation=label):
                        document=copy.deepcopy(asset_documents[index]); document['project']['restore']['frameworks']['net10.0']['projectReferences']=refs
                        (replay/assets[index]).write_text(json.dumps(document))
                        with self.assertRaises(V.ContractError): V.project_dependencies(replay,assets[index],hashes())
                (replay/assets[index]).write_text(json.dumps(asset_documents[index]))
            for asset in assets: V.project_dependencies(replay,asset,hashes())

    def test_reuse_all_eight_actual_three_read_protocols_and_mutations(self):
        required=['SaveChanges_insert','fresh_AsNoTracking','SaveChanges_value_only',
            'actual_parameterized_query_changed_value','SaveChanges_NULL_only','fresh_AsNoTracking_NULL']
        reuse_specs=[s for profile in V.PROFILES for s in V.specs(profile,'roundtrip') if s['shape']=='reuse']
        self.assertEqual(8,len(reuse_specs))
        for spec in reuse_specs:
            with self.subTest(case=spec['case_id']):
                item,expected=reuse_item(spec)
                def call(candidate):
                    V.steps(candidate,required)
                    V.independent(candidate,spec,expected,L0)
                call(item)
                column='Z' if spec['kind']=='dto' else 'D'
                def swap_reads(x): x['steps'][6]['result'],x['steps'][9]['result']=x['steps'][9]['result'],x['steps'][6]['result']
                def old_oracle(x):
                    x['independent_'+column+'_2']=copy.deepcopy(x['independent_'+column+'_1'])
                    x['steps'][6]['result']=x['independent_'+column+'_2']
                    x['oracle_read_attempts'][1]=copy.deepcopy(x['oracle_read_attempts'][0])
                def wrong_observation_column(x): x['independent_'+('D' if column=='Z' else 'Z')+'_2']=x.pop('independent_'+column+'_2')
                self.rejects(call,item,[
                    ('missing_read',lambda x:x['steps'].pop(6)),
                    ('extra_read',lambda x:x['steps'].append(copy.deepcopy(x['steps'][9]))),
                    ('changed_NULL_swapped',swap_reads),('repeated_old_oracle',old_oracle),
                    ('wrong_step_column',lambda x:x['steps'][6].update(step_id='fresh_independent_'+('D' if column=='Z' else 'Z'))),
                    ('wrong_observation_column',wrong_observation_column),
                    ('crosscase_attempt',lambda x:x['oracle_read_attempts'][1].update(table='TPL_OTHER_CASE')),
                    ('unfinished_read',lambda x:x['steps'][6].update(status='attempted')),
                    ('unfinished_attempt',lambda x:x['oracle_read_attempts'][1].update(completed=False)),
                    ('read_before_changed_query',lambda x:x['steps'].insert(5,x['steps'].pop(6))),
                    ('read_before_initial_fresh',lambda x:x['steps'].insert(2,x['steps'].pop(3))),
                    ('read_before_NULL_fresh',lambda x:x['steps'].insert(8,x['steps'].pop(9))),
                    ('ordinary_business_duplicate',lambda x:x['steps'].append(copy.deepcopy(x['steps'][1])))])

    def test_report_cases_missing_duplicate_reordered_wrongprofile_and_counters(self):
        selected=V.window_specs('shared','roundtrip',0)
        cases=[dict(case_id=s['case_id']) for s in selected]
        report=dict(window=V.window_descriptor('shared','roundtrip',0),full_group_case_count=60,
                    cases=cases,required_cases=[V.runtime_definition(s) for s in selected],required_case_count=15)
        self.rejects(lambda x:V.required_cases(x,'shared','roundtrip'),report,[
            ('missing',lambda x:x['cases'].pop()),('duplicate',lambda x:x['cases'].append(x['cases'][0])),
            ('reordered',lambda x:x['cases'].reverse()),('wrongprofile',lambda x:x['cases'][0].update(case_id=x['cases'][0]['case_id'].replace('shared','tls'))),
            ('wrongcounter',lambda x:x.update(required_case_count=14)),('fake_required',lambda x:x['required_cases'].pop()),
            ('wrongstart',lambda x:x['window'].update(start=1)),('wrong_full_group',lambda x:x.update(full_group_case_count=15))])

    def test_same_invocation_source_response_owner_order_actual_binding(self):
        report = metadata_report()
        call = lambda x: V.source(x, 'fixture/SaveChanges')
        self.rejects(call, report, [
            ('crossinvocation', lambda x: x['protocol_responses'][0].update(operation_identity=[1,2,3,5])),
            ('crossattempt', lambda x: x['protocol_responses'][0].update(attempt_id=9)),
            ('crosscommand', lambda x: x['commands'][0].update(ef_command_id='other')),
            ('crossstage', lambda x: x['parameter_describe_sources'][0].update(stage='other/SaveChanges')),
            ('missingreceipt', lambda x: x['protocol_responses'].clear()),
            ('fakeowner', lambda x: x['parameter_describe_sources'][0].update(statement_reference_token=0)),
            ('reorderedordinal', lambda x: x['parameter_describe_sources'][0]['parameters'].reverse()),
            ('wrongpublicbinding', lambda x: x['parameter_describe_sources'][0]['parameters'][0].update(public_parameter_index=1)),
            ('changedrequest', lambda x: x['parameter_describe_sources'][0]['parameters'][0]['request'].update(scale=6)),
            ('missingparameter', lambda x: x['parameter_describe_sources'][0]['parameters'].pop())])

    def test_explicit7_fixed6_whole_command_first_refusal_typed_NULL(self):
        spec = dict(case_id='fixture', profile='shared', kind='dt', ticks=639204993161234560, offset_minutes=None)
        self.assertIsNone(V.fixed_expectation(metadata_report(), spec)[0])
        self.assertEqual('System.NotSupportedException', V.fixed_expectation(metadata_report(extended=False), spec)[0])
        self.assertEqual('System.NotSupportedException', V.fixed_expectation(metadata_report(), dict(spec, profile='tls'))[0])
        submicro = dict(spec, ticks=spec['ticks'] + 1)
        self.assertEqual('System.OverflowException', V.fixed_expectation(metadata_report(ticks=submicro['ticks']), submicro)[0])
        report = metadata_report(ticks=submicro['ticks'], order=('dto', 'dt'))
        report['parameter_describe_sources'][0]['parameters'][0]['raw'] = raw('dto', 6, False)
        self.assertEqual('System.NotSupportedException', V.fixed_expectation(report, submicro)[0])
        self.rejects(lambda x: V.fixed_expectation(x, spec), metadata_report(), [
            ('ResultColumn', lambda x: x['parameter_describe_sources'][0]['parameters'][0]['raw'].update(source='ResultColumn')),
            ('advisory_unknown', lambda x: x['parameter_describe_sources'][0]['parameters'][0]['raw'].update(type_flag=0)),
            ('width', lambda x: x['parameter_describe_sources'][0]['parameters'][0]['raw'].update(precision=8)),
            ('mask', lambda x: x['parameter_describe_sources'][0]['parameters'][0]['raw'].update(derived_mask=1)),
            ('fakeNULL', lambda x: x['parameter_describe_sources'][0]['parameters'][1]['request'].update(value={'clr_type':'DateTimeOffset','local_ticks':spec['ticks'],'offset_minutes':330}))])

    def test_published_binding_never_inferred_or_scaled_back(self):
        report = binding_report()
        self.rejects(lambda x: V.published(x, x['parameter_describe_sources'][0], 7, 6, 'VerifiedSeven'), report, [
            ('unpublished', lambda x: x['commands'][1]['binding'][0].update(binding_available=False)),
            ('encoded6', lambda x: x['commands'][1]['binding'][0].update(encoded_scale=6)),
            ('wire7', lambda x: x['commands'][1]['binding'][0].update(wire_scale=7)),
            ('rawchanged', lambda x: x['commands'][1]['binding'][0].update(raw_source='ResultColumn')),
            ('missingNULLbinding', lambda x: x['commands'][1]['binding'].pop()),
            ('wrongprofile', lambda x: x['commands'][1]['binding'][0].update(profile_state='VerifiedSix'))])

    def test_exact_exception_chains_exclude_infrastructure_wrong_native_leaf(self):
        for leaf in ('System.ArgumentException', 'System.OverflowException', 'System.NotSupportedException', 'Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException'):
            V.exact_chain(chain(leaf), leaf)
        good = chain('System.OverflowException')
        self.rejects(lambda x: V.exact_chain(x, 'System.OverflowException'), good, [
            ('timeout', lambda x: x[0].update(type='System.TimeoutException')),
            ('wrongnative', lambda x: x[0].update(type='System.ArgumentException')),
            ('cancelwrapper', lambda x: x.insert(0, dict(depth=0,type='System.OperationCanceledException',safe={'type':'System.OperationCanceledException'}))),
            ('fakechain', lambda x: x[0]['safe'].update(type='System.InvalidOperationException'))])

    def test_async_refusal_requires_actual_matching_positive_control_and_zero_send(self):
        report = binding_report(); src = report['parameter_describe_sources'][0]
        frame = dict(stage=src['stage'], opcode=6, session_id=1, lease_generation=2, execution_id=3, invocation_id=4)
        report['sent_frames'] = [frame]
        control = dict(stage=src['stage'], source='actual_successful_EF_SaveChangesAsync_AfterFrameSent', business_execute_frames=1,
            prepare_source=src, completed_command_attempt=report['commands'][1], sent_identity=[frame])
        report['async_write_positive_controls'] = [control]
        item = dict(expected_refusal=dict(send_evidence=dict(observation='measured_async_Execute0', business_execute_frames=0, async_positive_controls=[control])))
        combined = dict(report=report, item=item)
        def call(x): V.send_refusal(x['report'], {'async_api': True}, x['item'], 'negative/SaveChanges')
        self.rejects(call, combined, [
            ('nocontrol', lambda x: x['report']['async_write_positive_controls'].clear()),
            ('dead_hook', lambda x: x['report']['sent_frames'].clear()),
            ('crosspositive', lambda x: x['report']['sent_frames'][0].update(invocation_id=9)),
            ('negative_execute', lambda x: x['report']['sent_frames'].append(dict(frame, stage='negative/SaveChanges'))),
            ('fakecounter', lambda x: x['item']['expected_refusal']['send_evidence'].update(business_execute_frames=1))])
        sync = {'expected_refusal': {'send_evidence': {'observation':'sync_send_unobserved','business_execute_frames':None,'async_positive_controls':None}}}
        V.send_refusal(report, {'async_api': False}, sync, 'negative')
        sync['expected_refusal']['send_evidence']['business_execute_frames'] = 0
        with self.assertRaises(V.ContractError): V.send_refusal(report, {'async_api': False}, sync, 'negative')

    def test_packet_FF9_getter_input_offset_and_producer_decoded_fields(self):
        spec = next(s for s in V.specs('shared', 'roundtrip') if s['kind'] == 'dto' and s['offset_minutes'] == 330)
        observation = packet_oracle(spec)
        call = lambda x: V.oracle(x, spec, spec['ticks'], 330, L0)
        self.rejects(call, observation, [
            ('wrongoffset', lambda x: x.update(offset_minutes=0)),
            ('wrongticks', lambda x: x.update(local_ticks=spec['ticks']+1)),
            ('aftergetter', lambda x: x['raw'].update(capture_stage='after_getter')),
            ('wrongpacket', lambda x: x['raw'].update(hex='00'*11)),
            ('wrongff9', lambda x: x.update(ff9=x['ff9'].replace('+05:30', '+00:00'))),
            ('wrongprofile', lambda x: x['identity'].update(profile='tls')),
            ('wrongpackage', lambda x: x['reader_route'].update(dll_sha256='0'*64)),
            ('literal_as_parameter', lambda x: x['raw'].update(source='literal_match_only'))])
        observation['raw']['decoded'] = {'fabricated': 'ignored entirely'}
        call(observation)

    def test_fresh_state_concurrency_rollback_schema_and_cleanup_mutations(self):
        catalog = dict(columns=[dict(name='ID',data_type='INT',nullable='N',scale=None),
            dict(name='N',data_type='VARCHAR',data_length=32,scale=None),dict(name='D',data_type='TIMESTAMP',scale=6),
            dict(name='Z',data_type='DATETIME WITH TIME ZONE',scale=6)], primary_key_contract='exactly_one_owned_primary_key',
            primary_key_name_checked_against_catalog='TP07PK_FIXTURE', verification_source='OwnedObjects.Preflight')
        state = dict(rows=1,catalog=catalog,catalog_sha256=hashlib.sha256(json.dumps(catalog,indent=2,ensure_ascii=False).encode()).hexdigest())
        spec = dict(shape='concurrency',scale=6,owned_primary_key='TP07PK_FIXTURE')
        V.catalog_state({'result':state},spec,1); V.same_state(state, copy.deepcopy(state))
        for mutation in ('rows','schema','hash'):
            changed=copy.deepcopy(state)
            if mutation=='rows': changed['rows']=0
            elif mutation=='schema': changed['catalog']['columns'][2]['scale']=7
            else: changed['catalog_sha256']='0'*64
            with self.assertRaises(V.ContractError): V.same_state(state,changed)
        specrow = dict(kind='dt',offset_minutes=None)
        actual = dict(id=1,d={'clr_type':'DateTime','ticks':10,'kind':'Unspecified'},n={'clr_type':'String','value':'changed'})
        V.row(actual,specrow,10,'changed')
        with self.assertRaises(V.ContractError): V.row(actual,specrow,11,'changed')
        with self.assertRaises(V.ContractError): V.row(actual,specrow,10,'first')
        ledger={'objects':[dict(table='TPL_FIXTURE',nonce='ABC')]}
        clean=dict(cleanup_verified=True,objects=[dict(table='TPL_FIXTURE',nonce='ABC',fresh_absent=True,fresh_table_count=0,identity=physical())])
        self.rejects(lambda x:L0.cleanup(x,ledger,'shared'),clean,[('missing',lambda x:x['objects'].clear()),
            ('foreign',lambda x:x['objects'][0].update(table='TPL_OTHER')),('present',lambda x:x['objects'][0].update(fresh_table_count=1))])

    def test_positive_interval_real_server_widths_and_strict_integer_receipts(self):
        # The first two strings are retained TP06 server CAST formats, not the unknown failed E projection.
        vectors=[((0,0,0,0,0),"INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)"),
            ((1,1,0,0,123456),"INTERVAL '1 1:0:0.123456' DAY(9) TO SECOND(6)"),
            ((2,3,4,5,123456),"INTERVAL '2 3:4:5.123456' DAY(9) TO SECOND(6)"),
            ((2,3,4,5,123456),"INTERVAL '+2 03:4:05.123456' DAY(9) TO SECOND(6)"),
            ((2,3,4,5,123456),"INTERVAL '000000002 03:04:05.123456' DAY(9) TO SECOND(6)")]
        for fields,text in vectors:
            with self.subTest(positive=text):
                item,spec=interval_item(fields,text); V.native_oracle(item,spec)
        item,spec=interval_item((2,3,4,5,123456),vectors[2][1])
        bad_texts=[
            '2 3:4:5.123456',"INTERVAL '2 3:4:5.123456'", "'2 3:4:5.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '2 3:4:5.123456' DAY(2) TO SECOND(6)","INTERVAL '2 3:4:5.123456' DAY(9) TO SECOND(7)",
            "INTERVAL '2 3:4:5.12345' DAY(9) TO SECOND(6)","INTERVAL '2 3:4:5.1234567' DAY(9) TO SECOND(6)",
            "INTERVAL '2 3:4:5' DAY(9) TO SECOND(6)","INTERVAL '2 24:4:5.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '2 3:60:5.123456' DAY(9) TO SECOND(6)","INTERVAL '2 3:4:60.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '2 003:4:5.123456' DAY(9) TO SECOND(6)","INTERVAL '0000000002 3:4:5.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '-2 3:4:5.123456' DAY(9) TO SECOND(6)","INTERVAL -'2 3:4:5.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '++2 3:4:5.123456' DAY(9) TO SECOND(6)","INTERVAL '٢ 3:4:5.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '3 3:4:5.123456' DAY(9) TO SECOND(6)","INTERVAL '2 4:4:5.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '2 3:5:5.123456' DAY(9) TO SECOND(6)","INTERVAL '2 3:4:6.123456' DAY(9) TO SECOND(6)",
            "INTERVAL '2 3:4:5.123457' DAY(9) TO SECOND(6)",vectors[2][1]+'\n','X'*121]
        for text in bad_texts:
            with self.subTest(malformed_or_mismatch=text):
                changed=copy.deepcopy(item); interval_text_receipt(changed,text)
                with self.assertRaises(V.ContractError): V.native_oracle(changed,spec)
        self.rejects(lambda x:V.native_oracle(x,spec),item,[
            ('receipt_hash',lambda x:x['native_oracle_attempt'].update(server_text_utf8_sha256='0'*64)),
            ('receipt_length',lambda x:x['native_oracle_attempt'].update(server_text_character_length=1)),
            ('receipt_limit',lambda x:x['native_oracle_attempt'].update(server_text_processing_limit=121)),
            ('expected_ticks',lambda x:x['independent_native'].update(ticks=spec['ticks']+1)),
            ('raw_component',lambda x:x['independent_native'].update(raw_hex=struct.pack('<6i',2,3,4,6,123456,1686).hex().upper()))])

    def test_native_TIME_actual_microseconds_to_FF9_and_mutations(self):
        # Exact retained E raw/text/tick tuple; this tests Python's independent unit conversion only.
        spec=dict(kind='time',ticks=513161234560)
        item=dict(independent_native=dict(rows=1,is_null=False,ticks=513161234560,raw_hex='EE8180C403',
                capture='fresh_ordinary_cache_before_temporal_getter'),
            native_oracle_attempt=dict(status='completed',source='fresh_ordinary_native_raw_integer_oracle',
                raw_hex='EE8180C403',raw_captured_before_temporal_getter=True,c_type=15,wire_scale=6,
                server_text='14:15:16.123456000'))
        call=lambda x:V.native_oracle(x,spec)
        self.rejects(call,item,[
            ('old_10x_smaller_unit',lambda x:x['native_oracle_attempt'].update(server_text='14:15:16.012345600')),
            ('10x_larger_unit',lambda x:x['native_oracle_attempt'].update(server_text='14:15:16.1234560000')),
            ('fraction6',lambda x:x['native_oracle_attempt'].update(server_text='14:15:16.123456')),
            ('fraction8',lambda x:x['native_oracle_attempt'].update(server_text='14:15:16.12345600')),
            ('fraction_wrong_value',lambda x:x['native_oracle_attempt'].update(server_text='14:15:16.123457000')),
            ('text_wrong_hour',lambda x:x['native_oracle_attempt'].update(server_text='15:15:16.123456000')),
            ('text_unpadded_hour',lambda x:x['native_oracle_attempt'].update(server_text='014:15:16.123456000')),
            ('text_suffix',lambda x:x['native_oracle_attempt'].update(server_text='14:15:16.123456000\n')),
            ('text_wrapper',lambda x:x['native_oracle_attempt'].update(server_text="TIME '14:15:16.123456000'")),
            ('typed_tick_mismatch',lambda x:x['independent_native'].update(ticks=513161234570))])
        for label,payload in [('hour24','F88180C403'),('minute60','8E8780C403'),('second60','EEE181C403'),
                ('micro1000000','EE8180841E'),('reserved_fraction_bits','EE8180C4E3')]:
            with self.subTest(raw_range=label):
                changed=copy.deepcopy(item)
                changed['independent_native']['raw_hex']=payload; changed['native_oracle_attempt']['raw_hex']=payload
                with self.assertRaisesRegex(V.ContractError,'native_TIME_domains'): call(changed)
        changed=copy.deepcopy(item)
        changed['independent_native']['raw_hex']='EE8182C403'; changed['native_oracle_attempt']['raw_hex']='EE8182C403'
        changed['native_oracle_attempt']['server_text']='14:15:16.123457000'
        with self.assertRaisesRegex(V.ContractError,'independent_native_raw_ticks'): call(changed)

    def test_TLS6_concurrency_independent_DT_DTO_sync_async_positive(self):
        for kind in ('dt','dto'):
            for async_api in (False,True):
                with self.subTest(kind=kind,async_api=async_api):
                    fixture=tls_concurrency_fixture(kind,async_api)
                    V.tls_concurrency_parameters(fixture['report'],fixture['item'],fixture['spec'])
                    V.validate_case(fixture['report'],fixture['item'],fixture['spec'])
                    # These remain outside the original generic fixed-only contracts.
                    source_=fixture['report']['parameter_describe_sources'][1]
                    with self.assertRaises(V.ContractError): V.raw_tuple(source_['parameters'][3]['raw'],kind,6)
                    with self.assertRaises(V.ContractError): V.published(fixture['report'],source_,6,6,'VerifiedSix')

    def test_TLS6_concurrency_joint_source_binding_protocol_and_outcome_negatives(self):
        for kind in ('dt','dto'):
            fixture=tls_concurrency_fixture(kind,True)
            current=0 if kind=='dt' else 1; n=1 if kind=='dt' else 0
            tick_key='ticks' if kind=='dt' else 'local_ticks'; field='d' if kind=='dt' else 'z'
            def source_(x,index=1): return x['report']['parameter_describe_sources'][index]
            def start(x,index=1): return next(c for c in x['report']['commands'] if c['status']=='attempted' and c['attempt_id']==index+1)
            def end(x,index=1): return next(c for c in x['report']['commands'] if c['status']=='completed' and c['attempt_id']==index+1)
            def step(x,name): return next(s for s in x['item']['steps'] if s['step_id']==name)
            def request(x,ordinal,**changes):
                p=source_(x)['parameters'][ordinal]; p['request'].update(changes)
                start(x)['parameters'][p['public_parameter_index']].update(changes)
            def original(x): return copy.deepcopy(source_(x)['parameters'][3]['request']['value'])
            def raw_(x,ordinal=3,**changes):
                source_(x)['parameters'][ordinal]['raw'].update(changes)
                end(x)['binding'][ordinal].update(**{'raw_'+k:v for k,v in changes.items()})
            def role_swap(x):
                value_=copy.deepcopy(source_(x)['parameters'][current]['request']['value'])
                request(x,current,value=original(x)); request(x,3,value=value_)
            def fake_fixed(x):
                raw_(x,type_flag=1); end(x)['binding'][3].update(reliable_fixed=True,column_scale=6)
            def TLS_tick1(x):
                value_=copy.deepcopy(source_(x)['parameters'][current]['request']['value'])
                value_[tick_key]=x['spec']['ticks']+1; request(x,current,value=value_)
            def oracle_(x): return x['item']['independent_'+('Z' if kind=='dto' else 'D')+'_1']
            self.rejects(lambda x:V.validate_case(x['report'],x['item'],x['spec']),fixture,[
                ('role_swap',role_swap),('current_flag2',lambda x:raw_(x,current,type_flag=2)),
                ('original_fake_fixed',fake_fixed),('generic_ctype2_32767',lambda x:raw_(x,c_type=2,precision=32767)),
                ('wrong_family',lambda x:raw_(x,c_type=26 if kind=='dt' else 27,precision=9 if kind=='dt' else 11)),
                ('wrong_width',lambda x:raw_(x,precision=9 if kind=='dt' else 11)),
                ('wrong_scale',lambda x:raw_(x,scale=7)),('raw_mask',lambda x:raw_(x,mask=1)),
                ('derived_mask',lambda x:raw_(x,derived_mask=1)),('bool_raw_flag',lambda x:raw_(x,type_flag=True)),
                ('float_raw_scale',lambda x:raw_(x,scale=6.0)),('wrong_source',lambda x:raw_(x,source='ResultColumn')),
                ('wrong_format',lambda x:raw_(x,result_format='BDTA')),
                ('original_column6',lambda x:end(x)['binding'][3].update(column_scale=6)),
                ('current_column7',lambda x:end(x)['binding'][current].update(column_scale=7)),
                ('bool_current_column',lambda x:end(x)['binding'][current].update(column_scale=True)),
                ('original_reliable',lambda x:end(x)['binding'][3].update(reliable_fixed=True)),
                ('profile_Seven',lambda x:end(x)['binding'][3].update(profile_state='VerifiedSeven')),
                ('encoded_scale',lambda x:end(x)['binding'][3].update(encoded_scale=7)),
                ('wire_scale',lambda x:end(x)['binding'][3].update(wire_scale=7)),
                ('requested_scale',lambda x:end(x)['binding'][3].update(requested_scale=7)),
                ('raw_retention',lambda x:end(x)['binding'][3].update(raw_type_flag=1)),
                ('binding_missing',lambda x:end(x)['binding'].pop(3)),
                ('binding_duplicate',lambda x:end(x)['binding'][3].update(ordinal=2)),
                ('missing_parameter',lambda x:source_(x)['parameters'].pop(3)),
                ('duplicate_parameter',lambda x:source_(x)['parameters'].append(copy.deepcopy(source_(x)['parameters'][3]))),
                ('request_precision_bool',lambda x:request(x,3,precision=False)),
                ('request_scale7',lambda x:request(x,3,scale=7)),
                ('request_implicit_scale',lambda x:request(x,3,explicit_scale=False)),
                ('request_explicit_precision',lambda x:request(x,3,explicit_precision=True)),
                ('request_output',lambda x:request(x,3,direction='Output')),
                ('request_provider',lambda x:request(x,3,provider_type='Int32')),
                ('original_NULL',lambda x:request(x,3,value={'is_null':True})),
                ('original_ticks',lambda x:request(x,3,value=dict(original(x),**{tick_key:x['spec']['ticks']+10}))),
                ('TLS_tick_plus1',TLS_tick1),
                ('N_wrong',lambda x:request(x,n,value={'clr_type':'String','value':'stale'})),
                ('ID_wrong',lambda x:request(x,2,value={'clr_type':'Int32','value':2})),
                ('public_index_swap',lambda x:source_(x)['parameters'][3].update(public_parameter_index=1)),
                ('name_duplicate',lambda x:source_(x)['parameters'][3].update(server_parameter_name_sha256=source_(x)['parameters'][current]['server_parameter_name_sha256'])),
                ('response_operation_graft',lambda x:x['report']['protocol_responses'][1].update(operation_identity=[999,1,4,9])),
                ('Prepare_response_opcode',lambda x:x['report']['protocol_responses'][1].update(response_opcode=200)),
                ('Prepare_sql_code',lambda x:x['report']['protocol_responses'][1].update(sql_code=1)),
                ('async_frame_operation_graft',lambda x:x['report']['sent_frames'][1].update(invocation_id=10)),
                ('async_frame_missing',lambda x:x['report']['sent_frames'].pop(1)),
                ('SQL_shape_changed',lambda x:start(x,2).update(sql_sha256='b'*64)),
                ('EF_command_join',lambda x:source_(x).update(ef_command_id='0'*36)),
                ('same_winner_stale_attempt',lambda x:start(x,2).update(attempt_id=start(x)['attempt_id'])),
                ('stale_exception',lambda x:step(x,'stale_SaveChanges').update(actual_exception_chain=chain('System.OverflowException'))),
                ('expected_leaf',lambda x:x['item']['expected_concurrency_exception'].update(expected_leaf_type='System.Exception')),
                ('fresh_value',lambda x:step(x,'fresh_after_stale_failure')['result'][field].update(**{tick_key:x['spec']['ticks']+20})),
                ('fresh_N',lambda x:step(x,'fresh_after_stale_failure')['result']['n'].update(value='stale')),
                ('fresh_rows',lambda x:step(x,'fresh_state_after_stale_failure')['result'].update(rows=0)),
                ('fresh_schema',lambda x:step(x,'fresh_state_after_stale_failure')['result']['catalog']['columns'][1].update(scale=7)),
                ('oracle_missing',lambda x:x['item']['oracle_read_attempts'].clear()),
                ('oracle_typed',lambda x:oracle_(x)['typed'].update(ticks=x['spec']['ticks']+20)),
                ('oracle_FF9',lambda x:oracle_(x).update(ff9='2000-01-01 00:00:00.000000000')),
                ('oracle_raw',lambda x:oracle_(x)['raw'].update(payload_sha256='0'*64))])
            if kind=='dto':
                self.rejects(lambda x:V.tls_concurrency_parameters(x['report'],x['item'],x['spec']),fixture,[
                    ('DTO_offset',lambda x:request(x,3,value=dict(original(x),offset_minutes=0)))])
            else:
                self.rejects(lambda x:V.tls_concurrency_parameters(x['report'],x['item'],x['spec']),fixture,[
                    ('DT_kind',lambda x:request(x,3,value=dict(original(x),kind='Utc')))])

    def test_TLS6_concurrency_scope_seed_and_owned_cleanup_remain_strict(self):
        fixture=tls_concurrency_fixture('dt',False)
        for changes in (dict(profile='shared'),dict(scale=7),dict(shape='update'),dict(group='six'),
                dict(kind='date'),dict(async_api=1),dict(ticks=639204129161234561)):
            with self.subTest(scope=changes):
                bad=copy.deepcopy(fixture,{id(L0):L0}); bad['spec'].update(changes)
                with self.assertRaises(V.ContractError): V.tls_concurrency_parameters(bad['report'],bad['item'],bad['spec'])
        self.rejects(lambda x:V.validate_case(x['report'],x['item'],x['spec']),fixture,[
            ('seed_flag2',lambda x:x['report']['parameter_describe_sources'][0]['parameters'][1]['raw'].update(type_flag=2)),
            ('winner_not_returned',lambda x:next(s for s in x['item']['steps'] if s['step_id']=='winner_SaveChanges').update(call_returned=False))])
        ledger={'objects':[dict(table='TPL_TLS_SYNTHETIC',nonce='ABC')]}
        clean=dict(cleanup_verified=True,objects=[dict(table='TPL_TLS_SYNTHETIC',nonce='ABC',fresh_absent=True,
            fresh_table_count=0,identity=physical('tls'))])
        L0.cleanup(clean,ledger,'tls')
        self.rejects(lambda x:L0.cleanup(x,ledger,'tls'),clean,[
            ('missing_cleanup',lambda x:x['objects'].clear()),('foreign_cleanup',lambda x:x['objects'][0].update(table='TPL_OTHER')),
            ('owned_not_absent',lambda x:x['objects'][0].update(fresh_absent=False)),
            ('owned_count_not_zero',lambda x:x['objects'][0].update(fresh_table_count=1)),
            ('cleanup_not_verified',lambda x:x.update(cleanup_verified=False)),
            ('cleanup_wrong_profile',lambda x:x['objects'][0]['identity'].update(profile='shared'))])

    def test_shared_concurrency_complete_DT_DTO_sync_async_and_fixed_original(self):
        for kind in ('dt','dto'):
            for async_api in (False,True):
                with self.subTest(kind=kind,async_api=async_api):
                    fixture=concurrency_fixture(kind,async_api)
                    V.concurrency_parameters(fixture['report'],fixture['item'],fixture['spec'])
                    V.validate_case(fixture['report'],fixture['item'],fixture['spec'])
                    # Preserve the independently valid fixed1 original-value publication.
                    fixed=copy.deepcopy(fixture,{id(L0):L0})
                    for source_ in fixed['report']['parameter_describe_sources'][1:]:
                        source_['parameters'][3]['raw']['type_flag']=1
                    for end in fixed['report']['commands']:
                        if end['status']=='completed' and end['stage'].endswith(('winner_SaveChanges','stale_SaveChanges')):
                            end['binding'][3].update(raw_type_flag=1,reliable_fixed=True,column_scale=7)
                    V.concurrency_parameters(fixed['report'],fixed['item'],fixed['spec'])
                    V.validate_case(fixed['report'],fixed['item'],fixed['spec'])

    def test_shared_concurrency_roles_sources_publications_and_outcome_mutations(self):
        fixture=concurrency_fixture('dto',True)
        def call(x): V.validate_case(x['report'],x['item'],x['spec'])
        def source_(x,stage_index=1): return x['report']['parameter_describe_sources'][stage_index]
        def before(x,stage_index=1):
            return next(c for c in x['report']['commands'] if c['status']=='attempted' and c['attempt_id']==stage_index+1)
        def end(x,stage_index=1):
            return next(c for c in x['report']['commands'] if c['status']=='completed' and c['attempt_id']==stage_index+1)
        def change_request(x,ordinal,**changes):
            p=source_(x)['parameters'][ordinal]; p['request'].update(changes)
            before(x)['parameters'][p['public_parameter_index']].update(changes)
        def change_role_value(x,ordinal,value_): change_request(x,ordinal,value=value_)
        def original_value(x): return copy.deepcopy(source_(x)['parameters'][3]['request']['value'])
        def step(x,name): return next(s for s in x['item']['steps'] if s['step_id']==name)
        def sql_join(x):
            before(x,2)['sql_sha256']='b'*64; end(x,2)['sql_sha256']='b'*64
        def names_join(x):
            source_(x,2)['parameters'][3]['public_parameter_name_sha256']='b'*64
        def newer_response_before_capture(x):
            source_(x)['sequence']+=1
            x['report']['protocol_responses'].append(dict(x['report']['protocol_responses'][1],sequence=4))
        def duplicate_role_names(x):
            for stage_index in (1,2):
                for key in ('server_parameter_name_sha256','public_parameter_name_sha256'):
                    source_(x,stage_index)['parameters'][3][key]=source_(x,stage_index)['parameters'][1][key]
        self.rejects(call,fixture,[
            ('current_flag2',lambda x:source_(x)['parameters'][1]['raw'].update(type_flag=2)),
            ('original_as_current',lambda x:change_role_value(x,3,copy.deepcopy(source_(x)['parameters'][1]['request']['value']))),
            ('current_as_original',lambda x:change_role_value(x,1,original_value(x))),
            ('original_NULL',lambda x:change_role_value(x,3,{'is_null':True})),
            ('original_NULL_with_ticks',lambda x:change_role_value(x,3,dict(original_value(x),is_null=True))),
            ('original_wrong_ticks',lambda x:change_role_value(x,3,dict(original_value(x),local_ticks=x['spec']['ticks']-1))),
            ('original_wrong_offset',lambda x:change_role_value(x,3,dict(original_value(x),offset_minutes=0))),
            ('extra_time_parameter',lambda x:source_(x)['parameters'].append(copy.deepcopy(source_(x)['parameters'][3]))),
            ('missing_ID',lambda x:source_(x)['parameters'].pop(2)),
            ('wrong_N',lambda x:change_role_value(x,0,{'clr_type':'String','value':'first'})),
            ('wrong_ID',lambda x:change_role_value(x,2,{'clr_type':'Int32','value':2})),
            ('wrong_public_index',lambda x:source_(x)['parameters'][3].update(public_parameter_index=2)),
            ('wrong_server_ordinal',lambda x:source_(x)['parameters'][3].update(ordinal=2)),
            ('name_hash_join',names_join),('SQL_hash_join',sql_join),
            ('duplicate_role_names',duplicate_role_names),
            ('SQL_length_join',lambda x:before(x,2).update(sql_length=136)),
            ('original_claim_fixed',lambda x:end(x)['binding'][3].update(reliable_fixed=True)),
            ('original_claim_column7',lambda x:end(x)['binding'][3].update(column_scale=7)),
            ('encoded_scale',lambda x:end(x)['binding'][3].update(encoded_scale=6)),
            ('wire_scale',lambda x:end(x)['binding'][3].update(wire_scale=6)),
            ('requested_scale',lambda x:end(x)['binding'][3].update(requested_scale=6)),
            ('binding_profile',lambda x:end(x)['binding'][3].update(profile_state='VerifiedSix')),
            ('raw_retention',lambda x:end(x)['binding'][3].update(raw_type_flag=1)),
            ('published_fractionalflag',lambda x:end(x)['binding'][3].update(raw_type_flag=2.0)),
            ('published_boolmask',lambda x:end(x)['binding'][3].update(raw_mask=False)),
            ('duplicate_binding_ordinal',lambda x:end(x)['binding'][3].update(ordinal=1)),
            ('missing_binding',lambda x:end(x)['binding'].pop(3)),
            ('missing_binding_available',lambda x:end(x)['binding'][3].update(binding_available=False)),
            ('missing_raw_available',lambda x:end(x)['binding'][3].update(raw_available=False)),
            ('ID_fake_temporal',lambda x:end(x)['binding'][2].update(binding_available=True)),
            ('raw_unknownflag',lambda x:source_(x)['parameters'][3]['raw'].update(type_flag=3)),
            ('raw_legacy',lambda x:source_(x)['parameters'][3]['raw'].update(c_type=23,precision=10)),
            ('raw_generic',lambda x:source_(x)['parameters'][3]['raw'].update(c_type=2)),
            ('raw_width',lambda x:source_(x)['parameters'][3]['raw'].update(precision=10)),
            ('raw_scale',lambda x:source_(x)['parameters'][3]['raw'].update(scale=6)),
            ('raw_mask',lambda x:source_(x)['parameters'][3]['raw'].update(derived_mask=1)),
            ('raw_numeric_available',lambda x:source_(x)['parameters'][3]['raw'].update(available=1)),
            ('raw_role',lambda x:source_(x)['parameters'][3]['raw'].update(source='ResultColumn')),
            ('raw_format',lambda x:source_(x)['parameters'][3]['raw'].update(result_format='BDTA')),
            ('raw_fractionalflag',lambda x:source_(x)['parameters'][3]['raw'].update(type_flag=2.0)),
            ('request_wrong_provider',lambda x:change_request(x,3,provider_type='DateTime')),
            ('request_wrong_precision',lambda x:change_request(x,3,precision=1)),
            ('request_scale_not_explicit',lambda x:change_request(x,3,explicit_scale=False)),
            ('request_output_direction',lambda x:change_request(x,3,direction='Output')),
            ('crosscase_source',lambda x:source_(x).update(stage='othercase/winner_SaveChanges')),
            ('crossoperation_response',lambda x:x['report']['protocol_responses'][1].update(operation_identity=[999,1,4,9])),
            ('latest_response_sequence',newer_response_before_capture),
            ('EF_CommandId',lambda x:source_(x).update(ef_command_id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')),
            ('borrowed_completion',lambda x:end(x,2).update(attempt_id=end(x)['attempt_id'])),
            ('extra_failed_completion',lambda x:x['report']['commands'].append(dict(end(x),status='failed'))),
            ('no_completion_successflag',lambda x:x['report']['commands'].remove(end(x,2))),
            ('controlled_ADO',lambda x:before(x).update(api='controlled_ADO_ExecuteNonQueryAsync')),
            ('async_no_Execute',lambda x:x['report']['sent_frames'].pop(1)),
            ('async_other_invocation',lambda x:x['report']['sent_frames'][1].update(invocation_id=10)),
            ('stale_wrong_exception',lambda x:step(x,'stale_SaveChanges').update(actual_exception_chain=chain('System.OverflowException'))),
            ('winner_call_not_returned',lambda x:step(x,'winner_SaveChanges').update(call_returned=False)),
            ('tracked_wrong_original',lambda x:step(x,'tracking_query_stale')['result']['z'].update(local_ticks=x['spec']['ticks']+1)),
            ('original_token_wrong',lambda x:x['item']['original_temporal_concurrency_token'].update(local_ticks=x['spec']['ticks']+1)),
            ('fresh_wrong_winner',lambda x:step(x,'fresh_after_stale_failure')['result']['z'].update(local_ticks=x['spec']['ticks']+2)),
            ('fresh_wrong_N',lambda x:step(x,'fresh_after_stale_failure')['result']['n'].update(value='stale')),
            ('fresh_total_rows',lambda x:step(x,'fresh_state_after_stale_failure')['result'].update(rows=0)),
            ('fresh_catalog_change',lambda x:step(x,'fresh_state_after_stale_failure')['result']['catalog']['columns'][1].update(scale=6))])
        # The helper itself requires the joint business outcome, not just the selected raw parameter.
        self.rejects(lambda x:V.concurrency_parameters(x['report'],x['item'],x['spec']),fixture,[
            ('helper_wrong_fresh_winner',lambda x:step(x,'fresh_after_stale_failure')['result']['z'].update(local_ticks=x['spec']['ticks']+2)),
            ('helper_wrong_stale_exception',lambda x:step(x,'stale_SaveChanges').update(actual_exception_chain=chain('System.OverflowException'))),
            ('helper_missing_fresh_oracle',lambda x:x['item']['oracle_read_attempts'].clear())])
        for changes in (dict(profile='tls'),dict(scale=6),dict(shape='update'),dict(shape='rollback'),
                dict(group='roundtrip',shape='roundtrip'),dict(group='roundtrip',shape='reuse'),dict(group='six',shape='facet6')):
            with self.subTest(out_of_scope=changes):
                bad=copy.deepcopy(fixture,{id(L0):L0}); bad['spec'].update(changes)
                with self.assertRaisesRegex(V.ContractError,'measured_shared_concurrency_scope'):
                    V.concurrency_parameters(bad['report'],bad['item'],bad['spec'])
        for source_ in fixture['report']['parameter_describe_sources'][1:]:
            with self.assertRaises(V.ContractError): V.raw_tuple(source_['parameters'][3]['raw'],'dto',7)
            with self.assertRaises(V.ContractError): V.published(fixture['report'],source_,7,7,'VerifiedSeven')

    def test_shared_DT_concurrency_value_facets_and_seed_remain_strict(self):
        fixture=concurrency_fixture('dt',False)
        def source_(x,index=1): return x['report']['parameter_describe_sources'][index]
        def request(x,ordinal,**changes):
            p=source_(x)['parameters'][ordinal]; p['request'].update(changes)
            start=next(c for c in x['report']['commands'] if c['status']=='attempted' and c['attempt_id']==2)
            start['parameters'][p['public_parameter_index']].update(changes)
        def original(x): return copy.deepcopy(source_(x)['parameters'][3]['request']['value'])
        def seed_flag2(x): source_(x,0)['parameters'][1]['raw']['type_flag']=2
        self.rejects(lambda x:V.validate_case(x['report'],x['item'],x['spec']),fixture,[
            ('DT_kind',lambda x:request(x,3,value=dict(original(x),kind='Utc'))),
            ('DT_wrong_ticks',lambda x:request(x,3,value=dict(original(x),ticks=x['spec']['ticks']+1))),
            ('DT_NULL_with_ticks',lambda x:request(x,3,value=dict(original(x),is_null=True))),
            ('DT_provider',lambda x:request(x,3,provider_type='DateTimeOffset')),
            ('DT_precision_bool',lambda x:request(x,3,precision=False)),
            ('DT_current_flag2',lambda x:source_(x)['parameters'][0]['raw'].update(type_flag=2)),
            ('seed_flag2_still_strict',seed_flag2)])

    def test_genuine_EF_route_rejects_rawSQL_or_literal_parameter_substitution(self):
        report=metadata_report()
        report['commands'].append(dict(stage='fixture/SaveChanges',attempt_id=1,status='completed',sql_sha256='a'*64,reader=route('reader')))
        spec=dict(case_id='fixture',async_api=True)
        self.rejects(lambda x:V.command_window(x,spec,'SaveChanges',reader=True),report,[
            ('rawADO',lambda x:x['commands'][0].update(api='ExecuteNonQueryAsync')),
            ('noEFID',lambda x:x['commands'][0].update(ef_command_id=None)),
            ('wrongreader',lambda x:x['commands'][1]['reader'].update(type='DM.DmDataReader'))])
        with self.assertRaises(V.ContractError): V.command_window(report,spec,'SaveChanges',parameters=0)

    def test_process_timeout_cannot_be_accepted_and_runner_private_environment(self):
        with tempfile.TemporaryDirectory() as folder:
            env=R.environment(Path(folder))
            self.assertEqual('1',env['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'])
            self.assertEqual('1',env['DOTNET_CLI_TELEMETRY_OPTOUT'])
            self.assertNotIn('DAMENG_TEST_CONNECTION_STRING',env)
            child=mock.Mock(); child.pid=987; child.returncode=-9
            child.wait.side_effect=[subprocess.TimeoutExpired(['fixture'],1),None]
            with mock.patch.object(R.subprocess,'Popen',return_value=child), mock.patch.object(R.os,'killpg') as kill:
                receipt=R.process(['fixture'],folder,Path(folder)/'deadline.log',env,1)
            kill.assert_called_once_with(987,R.signal.SIGKILL)
            self.assertEqual(124,receipt['exit_code'])
            self.assertTrue(receipt['timed_out'])
            with self.assertRaises(V.ContractError): V.successful_process(receipt)

    def test_exact_external_pack_omissions_source_drift_and_approval_SHA(self):
        hashes={name:V.sha(V.ROOT/name) for name in V.PACK_FILES}
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'approved-pack.json'
            document=dict(schema_version=1,task='TP07-E toolpack',source_hashes=hashes)
            path.write_text(json.dumps(document)); digest=V.sha(path)
            V.pack_entry(path,digest)
            for mutation in ('omitted','extra','source','task'):
                changed=copy.deepcopy(document)
                if mutation=='omitted': changed['source_hashes'].pop(V.PACK_FILES[-1])
                elif mutation=='extra': changed['source_hashes']['tools/extra.py']='0'*64
                elif mutation=='source': changed['source_hashes'][V.PACK_FILES[0]]='0'*64
                else: changed['task']='TP07-L0 toolpack'
                path.write_text(json.dumps(changed))
                with self.assertRaises(V.ContractError): V.pack_entry(path,digest)
                with self.assertRaises(V.ContractError): V.pack_entry(path,V.sha(path))

    def test_original_L0_six_file_source_root_and_before_exec_mutations(self):
        # Self-contained recorded-root topology matches the actual L0 six-file contract.
        # Four shared sources are the frozen E bytes. The two L0-only sentinels are
        # never executed; Low's separate actual inputs preflight revalidates f103.
        shared=set(V.PACK_FILES)&set(V.L0_PACK_FILES)
        original_bytes={name:(V.ROOT/name).read_bytes() for name in shared}
        sentinels={
            'tools/DownstreamAcceptance/TP07/runtime-run.py':b'# inert handwritten L0-only runtime-run contract fixture; never executed\n',
            'tools/DownstreamAcceptance/TP07/test_runtime_contracts.py':b'# inert handwritten L0-only test_runtime_contracts contract fixture; never executed\n'}
        self.assertEqual({
            'tools/DownstreamAcceptance/TP07/runtime-run.py':'21a568d0272d4f4eae2b40a1f83c0eeecd51ac36a6befb62ef512771e8ff4651',
            'tools/DownstreamAcceptance/TP07/test_runtime_contracts.py':'f960f1aab439c5d2fe9f15c6a6b582c95f8a139271b31f49f26e45485060430a'},
            {name:hashlib.sha256(raw).hexdigest() for name,raw in sentinels.items()})
        original_bytes.update(sentinels)
        original_document=dict(schema_version=1,task='TP07-L0 toolpack',
            source_hashes={name:hashlib.sha256(original_bytes[name]).hexdigest() for name in V.L0_PACK_FILES})
        with tempfile.TemporaryDirectory() as folder:
            base=Path(folder).resolve(); slot=base/'TP07-L0-tools-v3'; root=slot/'source'; root.mkdir(parents=True)
            for name,raw in original_bytes.items():
                target=root/name; target.parent.mkdir(parents=True,exist_ok=True); target.write_bytes(raw)
            pack_path=slot/'toolpack-manifest.json'; V.write(pack_path,original_document)
            pack_ref=dict(path=str(pack_path),sha256=V.sha(pack_path))
            identity=dict(manifest_path=str(pack_path),manifest_sha256=pack_ref['sha256'],source_hashes=original_document['source_hashes'])
            pins={str(root/name):digest for name,digest in original_document['source_hashes'].items()}
            run_document=dict(toolpack=identity,input_hashes_before=pins,input_hashes_after=copy.deepcopy(pins))
            run_path=base/'original-run-receipt.json'; V.write(run_path,run_document)
            def run_ref(): return dict(path=str(run_path),sha256=V.sha(run_path))
            e_path=base/'E-toolpack-manifest.json'
            V.write(e_path,dict(schema_version=1,task='TP07-E toolpack',source_hashes={name:V.sha(V.ROOT/name) for name in V.PACK_FILES}))
            e_pack=V.pack_entry(e_path,V.sha(e_path))
            loaded,loaded_pins=V.original_l0_validator(pack_ref,run_ref(),e_pack)
            self.assertEqual(root,loaded.ROOT)
            self.assertEqual(str(root/'tools/DownstreamAcceptance/TP07/runtime-validate.py'),loaded.__file__)
            self.assertEqual(pins,{name:digest for name,digest in loaded_pins.items() if name!=str(pack_path)})
            self.assertEqual(identity,loaded.toolpack_entry(pack_path,pack_ref['sha256'])[0])
            self.assertEqual(set(pins),{str(loaded.ROOT/name) for name in loaded.TOOLPACK_FILES})
            def rejected_before_exec(reference=pack_ref):
                with mock.patch.object(V,'compile',create=True) as compile_source:
                    with self.assertRaises(V.ContractError): V.original_l0_validator(reference,run_ref(),e_pack)
                    compile_source.assert_not_called()
            for name in V.L0_PACK_FILES:
                with self.subTest(mutation='any_six_bytes_drift',file=name):
                    target=root/name; target.write_bytes(original_bytes[name]+b'\n# drift\n')
                    rejected_before_exec(); target.write_bytes(original_bytes[name])
            for name in ('tools/DownstreamAcceptance/TP07/runtime-run.py','tools/DownstreamAcceptance/TP07/test_runtime_contracts.py'):
                with self.subTest(mutation='missing_L0_only_file',file=name):
                    target=root/name; target.unlink(); rejected_before_exec(); target.write_bytes(original_bytes[name])
            for location in ('E-snapshot/source','original-repository'):
                with self.subTest(mutation='wrong_recorded_root',location=location):
                    wrong_root=base/location
                    for name,raw in original_bytes.items():
                        target=wrong_root/name; target.parent.mkdir(parents=True,exist_ok=True); target.write_bytes(raw)
                    changed=copy.deepcopy(run_document)
                    wrong_pins={str(wrong_root/name):digest for name,digest in original_document['source_hashes'].items()}
                    changed.update(input_hashes_before=wrong_pins,input_hashes_after=copy.deepcopy(wrong_pins))
                    V.write(run_path,changed); rejected_before_exec()
            changed=copy.deepcopy(run_document)
            name=V.L0_PACK_FILES[0]; duplicate=str(base/'duplicate-root'/name)
            for phase in ('input_hashes_before','input_hashes_after'): changed[phase][duplicate]=original_document['source_hashes'][name]
            V.write(run_path,changed); rejected_before_exec()
            changed=copy.deepcopy(run_document); changed['input_hashes_after'][str(root/name)]='0'*64
            V.write(run_path,changed); rejected_before_exec()
            V.write(run_path,run_document)
            for mutation in ('omitted','extra','wrong_task'):
                with self.subTest(mutation=mutation):
                    changed=copy.deepcopy(original_document)
                    if mutation=='omitted': changed['source_hashes'].pop(V.L0_PACK_FILES[0])
                    elif mutation=='extra': changed['source_hashes']['tools/arbitrary.py']='0'*64
                    else: changed['task']='TP07-E toolpack'
                    V.write(pack_path,changed)
                    rejected_before_exec()
                    rejected_before_exec(dict(path=str(pack_path),sha256=V.sha(pack_path)))
            V.write(pack_path,original_document)
            shared='tools/DownstreamAcceptance/TP07/runtime-validate.py'
            changed=copy.deepcopy(original_document); raw=original_bytes[shared]+b'\n# separately repinned drift\n'
            (root/shared).write_bytes(raw); changed['source_hashes'][shared]=hashlib.sha256(raw).hexdigest(); V.write(pack_path,changed)
            # Even externally repinning the L0 manifest does not permit E/L0 shared-source disagreement.
            rejected_before_exec(dict(path=str(pack_path),sha256=V.sha(pack_path)))
            (root/shared).write_bytes(original_bytes[shared]); V.write(pack_path,original_document)
            V.original_l0_validator(pack_ref,run_ref(),e_pack)


class NewTransactionSourceContracts(unittest.TestCase):
    """New source fixtures are planned maps, never SDK locks or E acceptance."""

    def e_pack_fixture(self, folder):
        path = Path(folder) / 'actual-current-E-eight.json'
        V.write(path, dict(schema_version=1, task='TP07-E toolpack',
            source_hashes={name: V.sha(V.ROOT / name) for name in V.PACK_FILES}))
        return V.pack_entry(path, V.sha(path))

    def test_actual_NEW_L0_loader_complete_saved_validation(self):
        with tempfile.TemporaryDirectory() as folder:
            pack = self.e_pack_fixture(folder)
            oracle, pins = V.original_l0_validator(V.e_transaction_reference('toolpack'), V.e_transaction_reference('run'), pack)
            self.assertEqual(V.ROOT, oracle.ROOT)
            self.assertEqual(str(V.ROOT / 'tools/DownstreamAcceptance/TP07/runtime-validate.py'), oracle.__file__)
            self.assertEqual(set(V.L0_PACK_FILES), {str(Path(p).relative_to(V.ROOT)) for p in pins if p.endswith('.py') or p.endswith('runtime-cases.json')})
            actual = oracle.validate_artifact(Path(V.e_transaction_reference('run')['path']).parent,
                V.e_transaction_reference('startup')['sha256'], V.e_transaction_reference('toolpack')['path'],
                V.e_transaction_reference('toolpack')['sha256'])
            self.assertEqual(V.reference(V.e_transaction_reference('validation')), actual)
            self.assertTrue(actual['l0_value_contract_valid'])

    def test_NEW_L0_recorded_path_and_before_after_semantics_before_compile(self):
        run_ref = V.e_transaction_reference('run'); pack_ref = V.e_transaction_reference('toolpack')
        original = V.reference(run_ref); actual_reference = V.reference
        name = V.L0_PACK_FILES[0]; path = str(V.ROOT / name)
        mutations = []
        changed = copy.deepcopy(original)
        for phase in ('input_hashes_before', 'input_hashes_after'):
            changed[phase][str(V.ROOT / 'duplicate-source' / name)] = changed[phase][path]
        mutations.append(('unambiguous_original_L0_source_path', changed))
        changed = copy.deepcopy(original); changed['input_hashes_after'][path] = '0' * 64
        mutations.append(('original_L0_run_input_drift', changed))
        for names in ((name,), V.L0_PACK_FILES):
            changed = copy.deepcopy(original)
            for phase in ('input_hashes_before', 'input_hashes_after'):
                for item in names:
                    changed[phase][str(V.ROOT / 'wrong-root' / item)] = changed[phase].pop(str(V.ROOT / item))
            mutations.append(('original_L0_source_path_hash_binding', changed))
        changed = copy.deepcopy(original); changed['startup_manifest_sha256'] = '0' * 64
        mutations.append(('finite_NEW_L0_startup_identity', changed))
        changed = copy.deepcopy(original); changed['toolpack']['manifest_sha256'] = '0' * 64
        mutations.append(('original_L0_run_toolpack_binding', changed))
        with tempfile.TemporaryDirectory() as folder:
            pack = self.e_pack_fixture(folder)
            for code, changed in mutations:
                with self.subTest(code=code), mock.patch.object(V, 'reference', side_effect=lambda row: changed if row == run_ref else actual_reference(row)), \
                        mock.patch.object(V, 'compile', create=True) as compiler:
                    with self.assertRaisesRegex(V.ContractError, '^' + code + '$'):
                        V.original_l0_validator(pack_ref, run_ref, pack)
                    compiler.assert_not_called()

    def test_NEW_L0_all_six_physical_bytes_and_missing_files_before_compile(self):
        actual_read = Path.read_bytes; actual_file = Path.is_file
        with tempfile.TemporaryDirectory() as folder:
            pack = self.e_pack_fixture(folder)
            for name in V.L0_PACK_FILES:
                target = V.ROOT / name
                with self.subTest(name=name, mutation='bytes'), mock.patch.object(Path, 'read_bytes',
                        lambda path: actual_read(path) + b'\n# controlled read-only drift\n' if path == target else actual_read(path)), \
                        mock.patch.object(V, 'compile', create=True) as compiler:
                    # Shared sources stop at E's byte guard; L0-only sources reach their own byte guard.
                    code = 'executed_pack_drift' if name in V.PACK_FILES else 'original_L0_source_bytes_drift'
                    with self.assertRaisesRegex(V.ContractError, '^' + code + '$'):
                        V.original_l0_validator(V.e_transaction_reference('toolpack'), V.e_transaction_reference('run'), pack)
                    compiler.assert_not_called()
                with self.subTest(name=name, mutation='missing'), mock.patch.object(Path, 'is_file',
                        lambda path: False if path == target else actual_file(path)), mock.patch.object(V, 'compile', create=True) as compiler:
                    with self.assertRaisesRegex(V.ContractError, '^original_L0_source_file_required$'):
                        V.original_l0_validator(V.e_transaction_reference('toolpack'), V.e_transaction_reference('run'), pack)
                    compiler.assert_not_called()

    def test_NEW_L0_pack_run_and_shared_identity_before_compile(self):
        pack_ref = V.e_transaction_reference('toolpack'); run_ref = V.e_transaction_reference('run')
        original = V.reference(pack_ref); actual_reference = V.reference
        with tempfile.TemporaryDirectory() as folder:
            pack = self.e_pack_fixture(folder)
            for code, changed in (('external_finite_L0_toolpack', dict(original, task='wrong task')),
                                 ('E_L0_shared_source_hash_agreement', dict(original, source_hashes=dict(original['source_hashes'],
                                    **{'tools/DownstreamAcceptance/TP07/runtime-validate.py': '0' * 64})))):
                with self.subTest(code=code), mock.patch.object(V, 'reference', side_effect=lambda row: changed if row == pack_ref else actual_reference(row)), \
                        mock.patch.object(V, 'compile', create=True) as compiler:
                    with self.assertRaisesRegex(V.ContractError, '^' + code + '$'):
                        V.original_l0_validator(pack_ref, run_ref, pack)
                    compiler.assert_not_called()
            for p, r in ((dict(pack_ref, sha256='0' * 64), run_ref), (pack_ref, dict(run_ref, sha256='0' * 64))):
                with self.subTest(pack=p, run=r), mock.patch.object(V, 'reference', side_effect=lambda row: original if row == p else actual_reference(row)), \
                        mock.patch.object(V, 'compile', create=True) as compiler:
                    with self.assertRaisesRegex(V.ContractError, '^finite_NEW_L0_pack_run_identity$'):
                        V.original_l0_validator(p, r, pack)
                    compiler.assert_not_called()

    def source_fixture(self):
        value = V.e_transaction_inputs(); l0 = value['source']; e = V.policy()['E_template_hashes']
        source = dict(schema_version=1, task='TP07-E prepared source', W=value['product']['W'],
            input_reference=dict(path=str(V.ROOT / 'planned-source-input-fixture.json'), sha256='0' * 64),
            source_hashes=dict(l0['source_hashes'], **{'test/' + n: h for n, h in e.items()}),
            approved_ef_source_hashes=copy.deepcopy(l0['approved_ef_source_hashes']), product_ef_source_hashes=copy.deepcopy(l0['product_ef_source_hashes']),
            runtime_template_hashes=copy.deepcopy(l0['runtime_template_hashes']), E_template_hashes=e,
            lineage=dict(l0['lineage'], E_template_source_sha256='deb47b37037cb588785e87f7bff83984fe80facf29cb08ca9abf62e46c9b0a9a'),
            lineage_artifacts=dict(l0['lineage_artifacts'], E_template_source=dict(path=str(V.ROOT / '.local/temporal-native/verification/TP07-E-transaction-v1-implementation-handoff/E-source-manifest.json'),
                sha256='deb47b37037cb588785e87f7bff83984fe80facf29cb08ca9abf62e46c9b0a9a')))
        active = {'src/W.EntityFrameworkCore.Dameng/packages.lock.json': l0['source_hashes']['src/W.EntityFrameworkCore.Dameng/packages.lock.json'],
            'test/TP07LiteralProbe/packages.lock.json': value['locks']['lock_sha256'],
            'test/TP07TemporalRuntime/packages.lock.json': hashlib.sha256(b'handwritten E lock map fixture, not SDK evidence').hexdigest()}
        locks = dict(lineage=source['lineage'], locks=active, source_hashes=dict(source['source_hashes'], **active))
        startup = dict(locks, lineage_artifacts=source['lineage_artifacts'])
        return source, locks, startup, value, e

    def test_actual_parent_product_refs_and_planned194_196_map_contract(self):
        source, locks, startup, value, e = self.source_fixture()
        result = V.e_transaction_semantics(source, locks, startup, value, e)
        self.assertEqual(194, len(source['source_hashes'])); self.assertEqual(196, len(result))
        self.assertEqual(175, len(source['approved_ef_source_hashes'])); self.assertEqual(178, len(source['product_ef_source_hashes']))
        self.assertEqual(11, len(source['lineage'])); self.assertEqual(11, len(source['lineage_artifacts']))
        self.assertTrue(all(V.sha(path) == digest for path, digest in value['pins'].items()))

    def test_NEW_E_exact_source_maps_and_transaction_lineage_mutations(self):
        original = self.source_fixture()
        mutations = []
        for name in ('transaction_product_manifest', 'transaction_patch', 'transaction_source_approval'):
            for action in ('omitted', 'replaced'):
                changed = copy.deepcopy(original)
                for document in changed[:3]:
                    if action == 'omitted': document['lineage'].pop(name + '_sha256', None)
                    else: document['lineage'][name + '_sha256'] = '0' * 64
                mutations.append(('E_transaction_lineage11', changed))
        changed = copy.deepcopy(original); changed[0]['product_ef_source_hashes'] = changed[0]['approved_ef_source_hashes']
        mutations.append(('E_transaction_product178_map', changed))
        changed = copy.deepcopy(original); name = next(n for n in changed[0]['source_hashes'] if not n.endswith('packages.lock.json'))
        changed[0]['source_hashes']['same-count-wrong-path.cs'] = changed[0]['source_hashes'].pop(name)
        mutations.append(('E_transaction_exact194', changed))
        changed = copy.deepcopy(original); changed[0]['source_hashes'][name] = '0' * 64
        mutations.append(('E_transaction_exact194', changed))
        changed = copy.deepcopy(original); changed[1]['source_hashes'][name] = '0' * 64
        mutations.append(('E_transaction_exact196', changed))
        changed = copy.deepcopy(original); changed[0]['lineage_artifacts'].pop('transaction_source_approval')
        mutations.append(('E_transaction_lineage11', changed))
        for code, changed in mutations:
            with self.subTest(code=code), mock.patch.object(V, 'compile', create=True) as compiler:
                with self.assertRaisesRegex(V.ContractError, '^' + code + '$'): V.e_transaction_semantics(*changed)
                compiler.assert_not_called()


class NewTransactionProjectionContracts(unittest.TestCase):
    def projection_fixture(self, root):
        # Actual frozen source194 and two real baseline lock byte copies. This
        # temporary topology is a consumer fixture, never new SDK lock evidence.
        l0 = V.reference(V.e_transaction_reference('source'))
        verify = V.ROOT / '.local/temporal-native/verification'
        origins = {name: verify / 'TP07-L0-transaction-v2-prepare/replay' / name for name in l0['source_hashes']}
        e = V.policy()['E_template_hashes']
        origins.update({'test/' + name: verify / 'TP07-E-transaction-v1-implementation-handoff/source/tools/DownstreamAcceptance/TP07/runtime-templates' / name for name in e})
        source194 = dict(l0['source_hashes'], **{'test/' + name: digest for name, digest in e.items()})
        self.assertEqual(194, len(source194))
        origins['test/TP07LiteralProbe/packages.lock.json'] = verify / 'TP07-L0-transaction-v2-lock/replay/test/TP07LiteralProbe/packages.lock.json'
        origins['test/TP07TemporalRuntime/packages.lock.json'] = verify / 'TP07-E-combined-v5-lock/replay/test/TP07TemporalRuntime/packages.lock.json'
        expected = {}
        for name, origin in origins.items():
            raw = origin.read_bytes(); digest = hashlib.sha256(raw).hexdigest()
            if name in source194: self.assertEqual(source194[name], digest)
            target = root / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(raw)
            expected[name] = digest
        self.assertEqual(196, len(expected))
        return expected

    def test_NEW_E_exact_three_project_generated_projection_and_extras(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); expected = self.projection_fixture(root)
            for project in ('src/W.EntityFrameworkCore.Dameng', 'test/TP07LiteralProbe', 'test/TP07TemporalRuntime'):
                for output in ('bin', 'obj'):
                    path = root / project / output / 'nested/generated-fixture.dat'
                    path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b'generated fixture, not SDK evidence')
            self.assertEqual(expected, V.e_transaction_projection(root, expected))
            for name in ('foo/bin/extra.cs', 'test/unapproved/obj/extra.props', 'ordinary-extra.txt',
                         'src/W.EntityFrameworkCore.Dameng/bin-near/extra.cs',
                         'test/TP07LiteralProbeSuffix/obj/extra.props', 'test/TP07TemporalRuntime/obj-near/extra.cs'):
                with self.subTest(name=name):
                    extra = root / name; extra.parent.mkdir(parents=True, exist_ok=True); extra.write_bytes(b'extra source')
                    with self.assertRaisesRegex(V.ContractError, '^E_transaction_actual196_projection$'):
                        V.e_transaction_projection(root, expected)
                    extra.unlink()
                    self.assertEqual(expected, V.e_transaction_projection(root, expected))

    def test_NEW_E_projection_rejects_file_and_directory_links_before_exclusion(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'replay'; root.mkdir(); expected = self.projection_fixture(root)
            target_file = root / 'README.md'; target_directory = root / 'src/W.EntityFrameworkCore.Dameng'
            for name, target in (('ordinary-link.cs', target_file),
                                 ('test/TP07TemporalRuntime/bin/file-link.cs', target_file),
                                 ('ordinary-linked-directory', target_directory),
                                 ('test/TP07LiteralProbe/obj/linked-directory', target_directory),
                                 ('src/W.EntityFrameworkCore.Dameng/bin', target_directory)):
                with self.subTest(name=name):
                    link = root / name; link.parent.mkdir(parents=True, exist_ok=True)
                    link.symlink_to(target, target_is_directory=target.is_dir())
                    with self.assertRaisesRegex(V.ContractError, '^E_transaction_projection_symlink$'):
                        V.e_transaction_projection(root, expected)
                    link.unlink()
                    self.assertEqual(expected, V.e_transaction_projection(root, expected))


if __name__ == '__main__': unittest.main()
