"""Synthetic expected-only contract mutations; never candidate/DB evidence."""
import copy
import hashlib
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest
import uuid
import zipfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("tp06_test_policy", HERE / "contracts.py")
C = importlib.util.module_from_spec(spec); spec.loader.exec_module(C)
spec = importlib.util.spec_from_file_location("tp06_test_validator", HERE / "validate.py")
V = importlib.util.module_from_spec(spec); spec.loader.exec_module(V)


def packet(fields, ctype, scale):
    """Handwritten test fixture packing only; production policy has no encoder."""
    if ctype == 21:
        sign = -1 if fields["negative"] else 1
        return struct.pack("<6i", *(fields[k] * sign for k in ("days", "hours", "minutes", "seconds", "microseconds")), scale)
    payload = b""
    if ctype != 15:
        word = fields["year"] | fields["month"] << 15 | fields["day"] << 19
        payload += word.to_bytes(3, "little")
    if ctype != 14:
        extended = ctype in (26, 27)
        fraction = fields["nanoseconds"] if extended else fields["nanoseconds"] // 1000
        word = fields["hour"] | fields["minute"] << 5 | fields["second"] << 11 | fraction << 17
        payload += word.to_bytes(6 if extended else 5, "little")
    if ctype in (23, 27):
        payload += fields["offset_minutes"].to_bytes(2, "little", signed=True)
    return payload


def text(fields, kind, packed=None):
    if kind == "interval":
        leading=2 if packed==1574 else 9
        return (f'INTERVAL {"-" if fields["negative"] else ""}\'{fields["days"]} '
                f'{fields["hours"]}:{fields["minutes"]}:{fields["seconds"]}.{fields["microseconds"]:06}\' DAY({leading}) TO SECOND(6)')
    part = "" if kind == "time" else f'{fields["year"]:04}-{fields["month"]:02}-{fields["day"]:02}'
    if kind == "date":
        return part
    if part:
        part += " "
    part += f'{fields["hour"]:02}:{fields["minute"]:02}:{fields["second"]:02}.{fields["nanoseconds"]:09}'
    if kind == "tz":
        offset = fields["offset_minutes"]
        part += f' {"-" if offset < 0 else "+"}{abs(offset)//60:02}:{abs(offset)%60:02}'
    return part


def interval_value_fixture(fields,packed):
    """Handwritten expected-only canonical fixture; not a driver formatter invocation."""
    width=2 if packed==1574 else 9
    day=str(fields["days"]).rjust(width,"0")
    sign="-" if fields["negative"] else ""
    hms=":".join(str(fields[k]).rjust(2,"0") for k in ("hours","minutes","seconds"))
    fraction=str(fields["microseconds"]).rjust(6,"0")
    return "INTERVAL '"+sign+day+" "+hms+"."+fraction+"' DAY("+str(width)+") TO SECOND(6)"


def error(type_name):
    return dict(type=type_name, number=None, failure_class="user_rejection", verified_server_response=False,
                failure_kind=None, phase=None, outcome=None, code=None)


def oracle(vector, ctype, scale, null, source="W", connection="fresh", row=1, async_api=False):
    fields = None if null else C.expected_fields(vector)
    value = dict(source=source, connection_id=connection, row_id=row, is_null=null, fields=fields,
                 server_text=None if null else text(fields, vector["kind"],scale if ctype==21 else None))
    if null:
        value["server_null_receipt"]=dict(actual_getter_invoked=True,api="IsDBNullAsync" if async_api else "IsDBNull",
                                           ordinal=1,is_null=True,source="server_projection")
    if source == "server":
        return value
    default = "System.DateTime" if vector["kind"] in ("date", "time", "ts") else "System.DateTimeOffset" if vector["kind"] == "tz" else "System.TimeSpan" if scale == 1574 else "W.Dm.DmIntervalDT"
    target = dict(ts="System.DateTime", tz="System.DateTimeOffset", date="System.DateOnly", time="System.TimeOnly", interval="System.TimeSpan")[vector["kind"]]
    api = "GetFieldValueAsync<T>" if async_api else "GetFieldValue<T>"
    raw = None
    cell = dict(observable=True, is_null=null, is_bdta=False, result_format="ordinary_non_BDTA", source="already_fetched_cell", payload_length=None, payload_sha256=None)
    if not null:
        data = packet(fields, ctype, scale)
        raw = dict(hex=data.hex(), c_type=ctype, scale=scale, width=C.WIDTHS[ctype], mask=0,
                   source="ordinary_result_cache_before_temporal_projection", capture_stage="before_any_temporal_getter", payload_sha256=hashlib.sha256(data).hexdigest())
        cell.update(payload_length=len(data), payload_sha256=raw["payload_sha256"])
    value.update(ticks=None if null else vector["ticks"], offset_minutes=vector["offset_minutes"],
                 kind="Unspecified" if vector["kind"] == "ts" else None, raw=raw, cell=cell,
                 field_type=default, schema_type=default, default_type="System.DBNull" if null else default,
                 provider_type="System.DBNull" if null else default, typed_type=target, typed_api=api,
                 schema_scale=6 if ctype == 21 else scale, get_string=None if null else interval_value_fixture(fields,scale) if vector["kind"] == "interval" else C.public_text(vector,scale),
                 typed_error=dict(error("W.Dm.DmException"), number=6081) if null else None,
                 typed_null_error="W.Dm.DmException" if null else None,
                 typed_null_receipt=dict(actual_getter_invoked=True, has_real_infra_inner=False, cache_is_null=True, getter_api=api) if null else None,
                 metadata=dict(available=True, c_type=ctype, semantic_scale=scale, width=C.WIDTHS[ctype], mask=0,
                     type_flag=0, raw_scale=scale, raw_scale_status="captured_server_source", raw_source="ResultColumn",
                     raw_c_type=ctype, raw_precision=C.WIDTHS[ctype], raw_mask_before=0, raw_derived_mask=0,
                     result_format="ordinary_packed", is_bdta=False, metadata_role="result_column"))
    value["default_ticks"] = None if null else vector["ticks"] + ((C.date(1900, 1, 1).toordinal()-1)*C.DAY if vector["kind"] == "time" else 0)
    return value


def frame(opcode, sql_code=0):
    return dict(stage="NATIVE_INTERVAL_REJECT_leading", request_opcode=opcode, response_opcode=opcode,
                sql_code=sql_code, body_length=16)


def prepared_receipts(actual, *, fixed_reject=False, sql_code=0):
    business=[] if fixed_reject else [frame(6,sql_code)]
    sends=[] if fixed_reject else [dict(stage="NATIVE_INTERVAL_REJECT_leading",request_opcode=6,
                                       session_id=1,lease_generation=1,execution_id=1,invocation_id=1)]
    actual.update(execution_frames=copy.deepcopy(business),business_frames=business,allocation_count=0,
        prepare_or_direct_count=0,reprepare_count=0,business_sent_frames=sends,
        business_response_source="physical_channel_receive_opcode",
        server_response=None if fixed_reject else C.response_receipt(business[0]))


def literal_receipts(actual, sql_code=0):
    business=frame(5,sql_code)
    actual.update(execution_frames=[frame(3),business],business_frames=[business],allocation_count=1,
        prepare_or_direct_count=1,business_response_count=1,business_response_source="physical_channel_receive_opcode",
        server_response=C.response_receipt(business))


def expected_physical(profile):
    # Expected-only input; no statement that this fixture was observed on a server.
    version,encoding,message={"shared":("8.1.5.60","utf-8",21),"tls":("8.1.4.6","gb18030",11)}[profile]
    return dict(server_version=version,encoding=encoding,negotiated_encoding=encoding,message_version=message,
        encoding_override_empty=True,result_format="ordinary_non_BDTA",timezone=480,
        server_user="WDM_PROVIDER_TEST",server_schema="WDM_PROVIDER_TEST",
        profile_origin="physical_get_conn_instance_conn_property",
        negotiated_encoding_origin="physical_conn_property_serverEncoding_field")


def expected_read_context(profile):
    return C.binding_read_context(profile,expected_physical(profile),C.BIND_SQL_SOURCE_SHA,C.RESULT_DESCRIPTOR_SHA,C.READ_ROOT_DECISION,
                                  C.TLS_READ6_SHA,C.TLS_READ6_ROOT_DECISION)


def send_frame(case_id,opcode=5):
    return dict(stage=case_id,request_opcode=opcode,session_id=1,lease_generation=1,execution_id=1,invocation_id=1)


def local_recovery_receipts(actual,case_id,state="Open"):
    old=actual["operation_connection_id"];fresh=actual.get("fresh_connection_id","fresh");retired=state in ("Broken","Closed")
    actual["target_receipt"]=dict(attempted=True,status="not_returned",api=actual["api"],connection_id=old,
        parameter_count=1,state_before="Open",state_after=state,execute_returned=False,send_observable=True,
        send_source="physical_channel_frame_sent",sent_frames=[],all_send_count=0,allocation_count=0,
        prepare_or_direct_count=0,business_send_count=0)
    probe=dict(step_id="old_connection_probe",status="completed",connection_id=old,api="ExecuteScalarAsync",
        state_before=state,state_after=state,execute_returned=not retired,send_source="physical_channel_frame_sent",
        sent_frames=[] if retired else [send_frame(case_id)],all_send_count=0 if retired else 1)
    if retired:probe["error"]=error("System.InvalidOperationException")
    else:probe["scalar"]=1
    steps=[probe]
    if retired:
        steps += [dict(step_id="old_command_dispose",status="completed",connection_id=old,api="DisposeAsync",state_before=state,state_after=state),
                  dict(step_id="old_connection_dispose",status="completed",connection_id=old,api="DisposeAsync",state_before=state,state_after="Closed")]
    steps += [dict(step_id="fresh_connection_open",status="completed",api="OpenAsync",connection_id=fresh,identity_user="WDM_PROVIDER_TEST",identity_schema="WDM_PROVIDER_TEST",state_after="Open"),
              dict(step_id="fresh_connection_probe",status="completed",api="ExecuteScalarAsync",connection_id=fresh,scalar=1),
              dict(step_id="fresh_row_verify",status="completed",api="ExecuteReaderAsync",connection_id=fresh),
              dict(step_id="fresh_connection_dispose",status="completed",api="DisposeAsync",connection_id=fresh)]
    recovery=dict(attempted=True,completed=True,scope="local_write_rejection",operation_connection_id=old,
        state_before=state,state_after="Closed" if retired else "Open",path="retired_connection" if retired else "same_open",
        steps=steps,last_attempted_step="fresh_connection_dispose",last_completed_step="fresh_connection_dispose",
        fresh_connection_id=fresh,fresh_scalar=1)
    if not retired:recovery.update(same_connection_id=old,scalar=1,api="ExecuteScalarAsync")
    actual.update(recovery=recovery,fresh_connection_id=fresh,fresh_rows=1,
                  fresh_column_metadata=copy.deepcopy(next(o for o in actual["observations"] if o["source"]=="W")["metadata"]))


def retained7_stage(report, kind, scale):
    # The actual fixture bytes/metadata/string change; parameter and EncodedScale are untouched.
    case_id="BIND_METADATA_AUTOREPREPARE_"+("dt" if kind=="ts" else "dto")
    actual=next(case for case in report["cases"] if case["case_id"]==case_id)
    name="scale3" if scale==3 else "scale6"
    target=next(s for s in actual["stages"] if s["stage_id"]==name)
    vector=C.vectors()[kind+("_scale3" if scale==3 else "_micro")]
    for index,old in enumerate(target["observations"]):
        target["observations"][index]=oracle(vector,26 if kind=="ts" else 27,7,False,source=old["source"],
            connection=old["connection_id"],row=old["row_id"])
    return target


def sample(profile="shared"):
    """Full synthetic shape. Explicit expected-only and database_accessed false."""
    policy = C.manifest(); cases = []
    for case_spec in policy["profiles"][profile]:
        cid = case_spec["case_id"]
        value = dict(case_id=cid, status="observed", stages=[])
        cases.append(value)
        if "reader_mode" in case_spec:
            mode = case_spec["reader_mode"]
            reader = dict(stage_id="fetch" if mode == "fetch" else "reader", api="ExecuteReaderAsync", prepare_calls=0,
                          status="observed", operation_connection_id="reader-op", observations=[])
            value["stages"].append(reader)
            sc = 7 if profile == "shared" else 6
            receipt = dict(disposed_async=True, scalar_after_dispose=1, same_connection_id="reader-op")
            reader["receipt"] = receipt
            if mode == "fetch":
                receipt.update(row_count=128, fetch_count=1, fetch_opcode=7, fetch_source="physical_channel_receive_opcode", buf_prefetch="default", is_bdta=False, fetch_boundaries=[64])
                vector = policy["vectors"]["ts_tick" if profile == "shared" else "ts_micro"]
                reader["observations"] = [oracle(vector, 26 if sc == 7 else 16, sc, False, connection="reader-op", row=n, async_api=True) for n in range(1, 129)]
            else:
                receipt.update(read_async_calls=8, next_result_async_calls=2, rowset_count=2, cached_repeat_count=2 if mode == "default" else 0,
                               getter_mismatch=dict(type="System.InvalidCastException", next_column_read=True, next_row_read=True))
                receipt.update(more_result_count=1,more_result_opcode=44)
                for result, kind in enumerate(("ts", "tz")):
                    for row in (9001, 9002, 9003):
                        v = policy["vectors"][kind + ("_tick" if row == 9002 and sc == 7 else "_micro")]
                        ct = (26 if kind == "ts" else 27) if sc == 7 else (16 if kind == "ts" else 23)
                        obs = oracle(v, ct, sc, row == 9003, connection="reader-op", row=row, async_api=True)
                        if (result,row)==(0,9001):obs.update(getter_mismatch_error="System.InvalidCastException",next_column_read=True)
                        if mode == "default" and row == 9001:
                            obs["cached_repeat"]=dict(outcome="returned",type="System.DateTime" if kind=="ts" else "System.DateTimeOffset",ticks=v["ticks"],offset_minutes=v["offset_minutes"])
                        if mode == "sequential":
                            calls=[1,2,3,4,6] if row == 9003 else [1,2,3,4,5,6]
                            if (result,row)==(0,9001):calls=[0]+calls
                            obs.update(sequential_aliases=dict(mismatch=0,is_dbnull=1,default=2,provider=3,typed=4,string=5,server=6),
                                       sequential_getter_ordinals=calls,raw_alias_ordinal=4)
                            if row==9003:obs["server_null_receipt"]["ordinal"]=6
                        obs["rowset_index"] = result; reader["observations"].append(obs)
            continue
        for s in case_spec["stages"]:
            requested = policy["vectors"][s["input_vector"]]
            null_request = s["is_null"] or s["stage_id"] == "scale7_null" or cid == "NATIVE_TIME_REJECT_SCALE7_NULL" and s["stage_id"] == "reject"
            p = dict(clr_type="System.DBNull" if null_request else dict(ts="System.DateTime",tz="System.DateTimeOffset",date="System.DateOnly",time="System.TimeOnly",interval="System.TimeSpan")[requested["kind"]],
                     value_ticks=None if null_request else requested["ticks"], value_is_dbnull=null_request,
                     declared_db_type="DateTime2", declared_dm_type="DateTime", scale=7 if null_request and s["exception"] else s["scale"] & 15,
                     precision=0, db_type_explicit=True, dm_type_explicit="CLONE" in cid, scale_explicit=True)
            if s["stage_id"] == "reset":
                p.update(db_type_explicit=False, dm_type_explicit=False)
            if s["stage_id"] == "tls_default_explicit_reject":
                p.update(scale=0,scale_explicit=False)
            if cid.startswith("NATIVE_DATE"):
                p["declared_db_type"]="Date"
            if cid.startswith("NATIVE_TIME"):
                p["declared_db_type"]="Time"
            if requested["kind"] == "interval":
                p.update(declared_dm_type="IntervalDayToSecond",dm_type_explicit=True)
            actual = dict(stage_id=s["stage_id"], api=s["api"], prepare_calls=s["prepare_calls"],
                operation_connection_id="new-op" if s["stage_id"] == "reopen" else "op", status="rejected" if s["exception"] else "observed",
                row_id=0 if s["binding"] else 1,
                error=error(s["exception"]) if s["exception"] else None, parameter=p,
                affected_rows=s["affected_rows"], actual_receipts=(["PrepareAsync" if s["api"].endswith("Async") else "Prepare"] if s["prepare_calls"] else [])+[s["api"]], observations=[])
            if s["transaction"]:
                actual["actual_receipts"] += ["BeginTransactionAsync", s["transaction"]]
            if s["exception"]:
                actual["recovery"] = dict(same_connection_id=actual["operation_connection_id"], scalar=1, api="ExecuteScalarAsync")
            if s["binding"]:
                actual.update(parameter_actions=C.parameter_actions(cid, s["stage_id"]), parameter_id="clone-id" if "CLONE" in cid else "parameter", command_id="command")
                actual.update(statement_owner="reader", statement_id=1, encoded_scale=s["scale"],
                    parameter_metadata=dict(available=True,c_type=s["c_type"],semantic_scale=s["scale"],width=C.WIDTHS[s["c_type"]],mask=0,
                        type_flag=2,raw_source="ParameterDescribe",raw_scale_status="captured_server_source",raw_mask_before=0,raw_derived_mask=0,raw_c_type=2,raw_precision=32767,raw_scale=0))
            if s["fresh"]:
                actual.update(fresh_connection_id="fresh", fresh_rows=1)
            if not s["exception"] or s["fresh"] or s["binding"]:
                v = policy["vectors"][s["vector"]]
                actual["observations"] = [oracle(v,s["c_type"],s["scale"],s["is_null"],source=src,connection="fresh" if s["fresh"] or s["exception"] else actual["operation_connection_id"],row=0 if s["binding"] else 1,async_api=s["api"].endswith("Async")) for src in ("W","server")]
            if s["exception"]:
                local_recovery_receipts(actual,cid)
            if cid == "NATIVE_INTERVAL_REJECT_leading":
                column=copy.deepcopy(actual["observations"][0]["metadata"])
                metadata=copy.deepcopy(column);metadata.update(type_flag=1,raw_source="ParameterDescribe",metadata_role="parameter_describe")
                actual.update(parameter_metadata=metadata,column_before=copy.deepcopy(column),column_after=copy.deepcopy(column),
                    binding=dict(observable=True,source="ParameterDescribe",reliable_fixed=True,subtype=6,leading_precision=2,fractional_scale=6,packed_scale=1574),
                    binding_capture_stage="after_target_failure" if s["stage_id"] == "reject" else "business_response_before_legacy_decode",binding_owner="command",binding_statement_id=1,
                    business_execute_count=0 if s["stage_id"] == "reject" else 1,
                    business_send_source="physical_channel_frame_sent",business_send_count=0 if s["stage_id"] == "reject" else 1,
                    business_response_count=0 if s["stage_id"] == "reject" else 1,
                    parameter_source_connection_id="op",parameter_source_statement_id=1,parameter_source_capture_stage="after_prepare_before_target_execute",
                    reprepare_count=0,current_no_reprepare=True,statement_ids_during_execution=[] if s["stage_id"] == "reject" else [1],
                    command_id="leading-command",parameter_id="leading-parameter",
                    parameter_actions=["Value"] if s["stage_id"] == "reject" else ["CreateParameter","Value","DmSqlType","Scale"])
                if s["stage_id"] == "reject":
                    actual.update(current_binding_observed=False,current_binding=None,binding=None,
                        constraint_evidence="pre_execute_parameter_describe_plus_control",binding_owner="unobservable",binding_statement_id=None)
                prepared_receipts(actual,fixed_reject=s["stage_id"]=="reject")
            value["stages"].append(actual)
        if cid == "NATIVE_INTERVAL_REJECT_leading":
            first,second=value["stages"]
            second["control_only_binding"]=dict(evidence_role="control_only",stage_id="control",command_id=first["command_id"],
                connection_id=first["operation_connection_id"],statement_id=first["binding_statement_id"],binding=copy.deepcopy(first["binding"]))
        if cid == "BIND_CLONE_RESET_REOPEN":
            first, second = value["stages"][:2]
            value.update(original_parameter_id="original-id",clone_parameter_id="clone-id",
                clone_receipt=dict(clone_distinct_object=True,original_value_mutated=True,original_scale_mutated=True,clone_value_unchanged=True,clone_scale_unchanged=True,reset_invoked=True),
                clone_source_before=copy.deepcopy(first["parameter"]), clone_before=copy.deepcopy(first["parameter"]),
                clone_source_after=dict(first["parameter"],value_ticks=policy["vectors"]["ts_tick"]["ticks"],scale=3), clone_after=copy.deepcopy(second["parameter"]))
    return dict(status="observed_complete", expected_only=True, database_accessed=False, cases=cases,W_profile=expected_physical(profile))


class SemanticContracts(unittest.TestCase):
    def setUp(self):
        self.report = sample()
        # Establish positive baseline before every mutation, preventing false rejection tests.
        C.validate_report(self.report, "shared", expected_only=True)

    def case(self, name):
        return next(c for c in self.report["cases"] if c["case_id"] == name)

    def rejected(self):
        with self.assertRaises(C.ContractError):
            C.validate_report(self.report, "shared", expected_only=True)

    def test_expected_only_profiles_are_complete(self):
        for profile in ("shared", "tls"):
            self.assertEqual(C.validate_report(sample(profile),profile,expected_only=True),len(C.case_specs(profile)))

    def test_expected_only_cannot_be_real_candidate(self):
        with self.assertRaises(C.ContractError): C.validate_report(self.report,"shared")

    def test_missing_case(self):
        self.report["cases"].pop(); self.rejected()

    def test_duplicate_case(self):
        self.report["cases"][-1]=copy.deepcopy(self.report["cases"][0]);self.rejected()

    def test_relabel_case(self):
        self.report["cases"][0]["case_id"]="OTHER";self.rejected()

    def test_skip_case(self):
        self.report["cases"][0]["status"]="skipped";self.rejected()

    def test_unknown_exception(self):
        self.case("ASYNC_REJECT_THEN_QUERY")["stages"][1]["error"]["type"]="W.Dm.DmException";self.rejected()

    def test_transport_failure_not_unsupported(self):
        self.case("ASYNC_REJECT_THEN_QUERY")["stages"][2]["error"]["failure_kind"]="Transport";self.rejected()

    def test_fake_recovery(self):
        self.case("ASYNC_REJECT_THEN_QUERY")["stages"][1]["recovery"]["scalar"]=0;self.rejected()

    def test_recovery_new_connection(self):
        self.case("ASYNC_REJECT_THEN_QUERY")["stages"][1]["recovery"]["same_connection_id"]="replacement";self.rejected()

    def test_fake_commit(self):
        self.case("TX_PREPARED_ASYNC_UPDATE_COMMIT_shared7_ts")["stages"][0]["actual_receipts"].remove("CommitAsync");self.rejected()

    def test_rollback_only_count_not_value(self):
        obs=self.case("TX_ASYNC_UPDATE_ROLLBACK_shared7_ts")["stages"][0]["observations"][0];obs["ticks"]+=1;self.rejected()

    def test_false_direct_prepare(self):
        self.report["cases"][0]["stages"][0]["prepare_calls"]=1;self.rejected()

    def test_fake_fresh_oracle(self):
        self.report["cases"][0]["stages"][0]["fresh_connection_id"]="op";self.rejected()

    def test_no_server_oracle(self):
        self.report["cases"][0]["stages"][0]["observations"].pop();self.rejected()

    def test_wrong_fraction(self):
        self.report["cases"][0]["stages"][0]["observations"][0]["fields"]["nanoseconds"]+=100;self.rejected()

    def test_wrong_offset(self):
        self.case("TX_DIRECT_INSERT_shared7_tz")["stages"][0]["observations"][0]["offset_minutes"]=0;self.rejected()

    def test_wrong_server_text(self):
        self.case("NATIVE_DATE_ordinary")["stages"][0]["observations"][1]["server_text"]="2026-07-24";self.rejected()

    def test_wrong_raw_even_with_rehashed_bytes(self):
        o=self.case("NATIVE_TIME_ordinary")["stages"][0]["observations"][0];raw=o["raw"];data=bytearray.fromhex(raw["hex"]);data[0]^=1
        raw["hex"]=data.hex();raw["payload_sha256"]=hashlib.sha256(data).hexdigest();o["cell"]["payload_sha256"]=raw["payload_sha256"];self.rejected()

    def test_inconsistent_cached_payload(self):
        self.report["cases"][0]["stages"][0]["observations"][0]["cell"]["payload_sha256"]="0"*64;self.rejected()

    def test_unobservable_metadata(self):
        self.report["cases"][0]["stages"][0]["observations"][0]["metadata"]["available"]=False;self.rejected()

    def test_wrong_generic_mapping(self):
        self.case("NATIVE_DATE_ordinary")["stages"][0]["observations"][0]["typed_type"]="System.DateTime";self.rejected()

    def test_wrong_default_time_anchor(self):
        self.case("NATIVE_TIME_ordinary")["stages"][0]["observations"][0]["default_ticks"]-=C.DAY;self.rejected()

    def test_null_fake_getter(self):
        self.case("NATIVE_DATE_null")["stages"][0]["observations"][0]["typed_null_receipt"]["actual_getter_invoked"]=False;self.rejected()

    def test_null_wrong_error_number(self):
        self.case("NATIVE_TIME_null")["stages"][0]["observations"][0]["typed_error"]["number"]=6045;self.rejected()

    def test_null_real_infra(self):
        self.case("NATIVE_INTERVAL_null")["stages"][0]["observations"][0]["typed_null_receipt"]["has_real_infra_inner"]=True;self.rejected()

    def test_reuse_hidden_scale_setter(self):
        self.case("BIND_REUSE_NO_EXPLICIT_PREPARE_dt")["stages"][1]["parameter_actions"].append("Scale");self.rejected()

    def test_reuse_parameter_replaced(self):
        self.case("BIND_REUSE_NO_EXPLICIT_PREPARE_dto")["stages"][1]["parameter_id"]="replacement";self.rejected()

    def test_reprepare_manual_prepare(self):
        self.case("BIND_METADATA_AUTOREPREPARE_dt")["stages"][1]["actual_receipts"].append("Prepare");self.rejected()

    def test_reprepare_effective_type_stale(self):
        self.case("BIND_METADATA_AUTOREPREPARE_dto")["stages"][1]["parameter_metadata"]["c_type"]=27;self.rejected()

    def test_clone_repaired_before_execute(self):
        self.case("BIND_CLONE_RESET_REOPEN")["stages"][1]["parameter_actions"].append("Value");self.rejected()

    def test_clone_snapshot_changed(self):
        self.case("BIND_CLONE_RESET_REOPEN")["clone_after"]["value_ticks"]+=1;self.rejected()

    def test_clone_same_object(self):
        c=self.case("BIND_CLONE_RESET_REOPEN");c["original_parameter_id"]=c["clone_parameter_id"];self.rejected()

    def test_reset_retains_explicit_intent(self):
        self.case("BIND_CLONE_RESET_REOPEN")["stages"][2]["parameter"]["db_type_explicit"]=True;self.rejected()

    def test_reopen_same_physical(self):
        self.case("BIND_CLONE_RESET_REOPEN")["stages"][3]["operation_connection_id"]="op";self.rejected()

    def test_reader_missing_nextresult(self):
        self.case("READER_ORDINARY_default")["stages"][0]["receipt"]["next_result_async_calls"]=0;self.rejected()

    def test_reader_relabelled_rowset(self):
        self.case("READER_ORDINARY_sequential")["stages"][0]["observations"][3]["rowset_index"]=0;self.rejected()

    def test_sequential_getters_reuse_consumed_ordinal(self):
        self.case("READER_ORDINARY_sequential")["stages"][0]["observations"][0]["sequential_aliases"]["typed"]=1;self.rejected()

    def test_sequential_synthetic_getter_receipt(self):
        self.case("READER_ORDINARY_sequential")["stages"][0]["observations"][0]["sequential_getter_ordinals"]=[0,1,4,6];self.rejected()

    def test_fetch_rowcount_without_wire_receipt(self):
        self.case("READER_ORDINARY_FETCH")["stages"][0]["receipt"]["fetch_count"]=0;self.rejected()

    def test_next_result44_cannot_replace_fetch7(self):
        # Only opcode changes; 128 precise rows and the nonzero count remain valid.
        self.case("READER_ORDINARY_FETCH")["stages"][0]["receipt"]["fetch_opcode"]=44;self.rejected()

    def test_fetch_opcode_missing(self):
        self.case("READER_ORDINARY_FETCH")["stages"][0]["receipt"].pop("fetch_opcode");self.rejected()

    def test_temporal_NULL_server_projection_nonNULL(self):
        self.case("NATIVE_DATE_null")["stages"][0]["observations"][1]["server_null_receipt"]["is_null"]=False;self.rejected()

    def test_ordinary_server_NULL_getter_not_invoked(self):
        self.case("NATIVE_TIME_null")["stages"][0]["observations"][1]["server_null_receipt"]["actual_getter_invoked"]=False;self.rejected()

    def test_ordinary_server_NULL_receipt_missing(self):
        self.case("NATIVE_INTERVAL_null")["stages"][0]["observations"][1].pop("server_null_receipt");self.rejected()

    def test_server_NULL_uses_temporal_column_ordinal(self):
        self.case("NATIVE_DATE_null")["stages"][0]["observations"][1]["server_null_receipt"]["ordinal"]=0;self.rejected()

    def test_sequential_server_NULL_projection_nonNULL(self):
        self.case("READER_ORDINARY_sequential")["stages"][0]["observations"][2]["server_null_receipt"]["is_null"]=False;self.rejected()

    def test_extra_prepare5_cannot_hide_behind_same_statement(self):
        target=self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][0]
        target["execution_frames"].insert(0,frame(5));target["statement_ids_during_execution"].insert(0,1)
        target.update(prepare_or_direct_count=1,reprepare_count=1)
        self.rejected()

    def test_extra_prepare5_with_false_zero_summary(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][0]["execution_frames"].insert(0,frame(5));self.rejected()

    def test_allocation3_is_not_prepare_or_business_execute(self):
        target=self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][0]
        target["execution_frames"]=[frame(3)];target["allocation_count"]=1
        self.rejected()

    def test_business_response_summary_without_actual_frame(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][0]["business_frames"]=[];self.rejected()

    def test_business_send_summary_without_actual_send(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][0]["business_sent_frames"]=[];self.rejected()

    def test_fetch_duplicate_ordinal(self):
        self.case("READER_ORDINARY_FETCH")["stages"][0]["observations"][1]["row_id"]=1;self.rejected()

    def test_fetch_bdta_closed(self):
        self.case("READER_ORDINARY_FETCH")["stages"][0]["receipt"]["is_bdta"]=True;self.rejected()

    def test_bool_cannot_replace_integer(self):
        self.report["cases"][0]["stages"][0]["affected_rows"]=True;self.rejected()

    def test_leading_column_cannot_be_parameter_source(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]["parameter_metadata"]["raw_source"]="ResultColumn";self.rejected()

    def test_leading_fixed_must_not_send_business_execute(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]["business_execute_count"]=1;self.rejected()

    def test_leading_unknown_raw_scale_is_closed(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]["parameter_metadata"]["raw_scale"]=1590;self.rejected()

    def test_leading_advisory_cannot_claim_Overflow(self):
        target=self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]
        target["parameter_metadata"].update(type_flag=2,raw_scale=0,semantic_scale=0)
        target["binding"]=dict(observable=True,source="ParameterDescribe",reliable_fixed=False,subtype=6,leading_precision=9,fractional_scale=6,packed_scale=1686)
        target["business_execute_count"]=1;self.rejected()

    def test_leading_unapproved_negative_server_number_never_accepts(self):
        target=self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]
        target["parameter_metadata"].update(type_flag=2,raw_scale=0,semantic_scale=0)
        target["binding"]=dict(observable=True,source="ParameterDescribe",reliable_fixed=False,subtype=6,leading_precision=9,fractional_scale=6,packed_scale=1686)
        target.update(business_execute_count=1,status="server_leading_response_pending_review",
            binding_capture_stage="business_response_before_legacy_decode",
            binding_owner="command",binding_statement_id=1,current_binding_observed=True,current_binding=copy.deepcopy(target["binding"]),
            business_send_count=1,business_response_count=1,statement_ids_during_execution=[1],
            error=dict(type="W.Dm.DmException",number=-6118,failure_class="verified_server_response",verified_server_response=True,
                       failure_kind="Server",phase="Receive",outcome="ServerReported",code="WDM_SERVER"),
            server_response=dict(source="physical_channel_receive_opcode",sql_code=-6118,request_opcode=6,response_opcode=6,body_length=16))
        prepared_receipts(target,sql_code=-6118)
        target["recovery"].update(scope="verified_server_response",path="same_open",state_before="Open",state_after="Open")
        controls=[]
        for name,days,packed in (("99_day2",99,1574),("100_day9",100,1686),("100_day2",None,1574)):
            control=dict(control_id=name,api="ExecuteNonQuery",parameter_count=0,row_id=9101+len(controls),operation_connection_id="control-op",
                         fresh_connection_id="control-fresh",column_packed_scale=packed,status="rejected" if days is None else "observed",
                         fresh_rows=0 if days is None else 1,error=copy.deepcopy(target["error"]) if days is None else None,
                         server_response=copy.deepcopy(target["server_response"]) if days is None else None,affected_rows=0 if days is None else 1)
            if days is not None:
                vec=dict(kind="interval",ticks=days*C.DAY,offset_minutes=None)
                control["observations"]=[oracle(vec,21,packed,False,source=src,connection="control-fresh",row=control["row_id"]) for src in ("W","server")]
            literal_receipts(control,-6118 if days is None else 0)
            controls.append(control)
        target["server_controls"]=controls
        with self.assertRaisesRegex(C.ContractError,"^server_leading_response_pending_review$"):
            C.validate_report(self.report,"shared",expected_only=True)


    def test_leading_control_binding_cannot_be_current(self):
        target=self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]
        target["current_binding"]=copy.deepcopy(target["control_only_binding"]["binding"]);self.rejected()

    def test_leading_unrecorded_reprepare(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]["reprepare_count"]=1;self.rejected()

    def test_leading_source_wrong_physical_connection(self):
        self.case("NATIVE_INTERVAL_REJECT_leading")["stages"][1]["parameter_source_connection_id"]="other";self.rejected()


class TLSRead6Contracts(unittest.TestCase):
    def setUp(self):
        self.report=sample("tls");C.validate_report(self.report,"tls",expected_only=True)

    def read6(self,kind):
        cid="BIND_METADATA_AUTOREPREPARE_"+("dt" if kind=="ts" else "dto")
        stage=next(c for c in self.report["cases"] if c["case_id"]==cid)["stages"][1]
        vector=C.vectors()[kind+"_scale3"]
        stage["observations"]=[oracle(vector,16 if kind=="ts" else 23,6,False,source=old["source"],
            connection=old["connection_id"],row=old["row_id"]) for old in stage["observations"]]
        C.validate_report(self.report,"tls",expected_only=True)
        return stage

    def test_exact_DT_DTO_read6_raw_and_text_controls(self):
        for kind,raw_hex,sha in (("ts","EA87BBEE81F0C003","a100bc48c780ecd4e635bec0534695b6217d8a75c24976e4773172c9c9880e0d"),
                                ("tz","EA87BBEE81F0C0034A01","020fa457b96633e76cfdbfcfa0d91457053e1d18feb686fbcd55e69a067dd0eb")):
            stage=self.read6(kind);obs=stage["observations"][0]
            self.assertEqual(obs["raw"]["hex"].upper(),raw_hex);self.assertEqual(obs["raw"]["payload_sha256"],sha)
            self.assertEqual(obs["ticks"],639204129161230000);self.assertEqual(obs["offset_minutes"],330 if kind=="tz" else None)
            self.assertEqual(obs["get_string"],"2026-07-23 14:15:16.123000"+(" +05:30" if kind=="tz" else ""))
            self.assertEqual(stage["parameter"]["scale"],3);self.assertEqual(stage["encoded_scale"],3)
        results=[];C.validate_report(self.report,"tls",expected_only=True,binding_read_results=results)
        rows=[r for r in results if r["rule_id"]=="g2_tls_autoreprepare_scale3_same_family_read6_v1"]
        self.assertEqual(len(rows),2);self.assertTrue(all(not r["observed"] for r in rows))
        self.assertTrue(all(r["write_tuple"]["scale"]==3 and r["read_tuple"]["scale"]==6 for r in rows))

    def test_read6_requires_exact_TLS_physical_SQL_and_approval(self):
        self.read6("ts")
        for key,value in (("cases_source_sha256","0"*64),("tls_design_sha256","0"*64),("tls_root_decision_id",None)):
            context=expected_read_context("tls");context[key]=value
            with self.subTest(key=key),self.assertRaises(C.ContractError):C.validate_report(self.report,"tls",expected_only=True,read_context=context)
        for key,value in (("message_version",21),("server_version","8.1.5.60"),("encoding","utf-8"),("encoding_override_empty",False)):
            report=copy.deepcopy(self.report);report["W_profile"][key]=value
            with self.subTest(key=key),self.assertRaises(C.ContractError):C.validate_report(report,"tls",expected_only=True)

    def test_read6_does_not_change_write3_encoded3_or_actions(self):
        self.read6("ts")
        for mutation in ("write","encoded","actions","quantum"):
            report=copy.deepcopy(self.report);stage=next(c for c in report["cases"] if c["case_id"]=="BIND_METADATA_AUTOREPREPARE_dt")["stages"][1]
            if mutation=="write":stage["parameter"]["scale"]=6
            elif mutation=="encoded":stage["encoded_scale"]=6
            elif mutation=="actions":stage["parameter_actions"].append("Prepare")
            else:stage["parameter"]["value_ticks"]+=10
            with self.subTest(mutation=mutation),self.assertRaises(C.ContractError):C.validate_report(report,"tls",expected_only=True)

    def test_read6_still_requires_independent_full_observation(self):
        self.read6("tz")
        for mutation in ("hash","schema","offset","ticks","GetString","FF9","connection","raw_role"):
            report=copy.deepcopy(self.report);stage=next(c for c in report["cases"] if c["case_id"]=="BIND_METADATA_AUTOREPREPARE_dto")["stages"][1];obs=stage["observations"][0]
            if mutation=="hash":obs["raw"]["payload_sha256"]="0"*64
            elif mutation=="schema":obs["schema_scale"]=3
            elif mutation=="offset":obs["offset_minutes"]=0
            elif mutation=="ticks":obs["ticks"]+=1
            elif mutation=="GetString":obs["get_string"]=obs["get_string"].replace(".123000 ",".12300 ")
            elif mutation=="FF9":stage["observations"][1]["server_text"]=stage["observations"][1]["server_text"].replace(".123000000",".123001000")
            elif mutation=="connection":stage["observations"][1]["connection_id"]="other-query"
            else:obs["metadata"]["raw_source"]="ParameterDescribe"
            with self.subTest(mutation=mutation),self.assertRaises(C.ContractError):C.validate_report(report,"tls",expected_only=True)

    def test_rule_not_other_case_stage_shared_or_cross_family(self):
        self.read6("ts");stage=next(c for c in self.report["cases"] if c["case_id"]=="BIND_METADATA_AUTOREPREPARE_dt")["stages"][1]
        expected=C.case_specs("tls")[next(i for i,c in enumerate(C.case_specs("tls")) if c["case_id"]=="BIND_METADATA_AUTOREPREPARE_dt")]["stages"][1]
        metadata=stage["observations"][0]["metadata"]
        for cid,name in (("BIND_REUSE_NO_EXPLICIT_PREPARE_dt","scale3"),("BIND_METADATA_AUTOREPREPARE_dt","other_stage")):
            altered=dict(expected,stage_id=name)
            with self.subTest(cid=cid,name=name),self.assertRaises(C.UnverifiedResultDescriptor):C.select_binding_read_contract("tls",altered,metadata,expected_read_context("tls"),cid)
        with self.assertRaises(C.UnverifiedResultDescriptor):C.select_binding_read_contract("shared",expected,metadata,expected_read_context("shared"),"BIND_METADATA_AUTOREPREPARE_dt")
        metadata=dict(metadata,c_type=23,width=10)
        with self.assertRaises(C.UnverifiedResultDescriptor):C.select_binding_read_contract("tls",expected,metadata,expected_read_context("tls"),"BIND_METADATA_AUTOREPREPARE_dt")

    def test_new_rule_does_not_open_TLS7(self):
        self.read6("ts");retained7_stage(self.report,"ts",3)
        with self.assertRaises(C.UnverifiedResultDescriptor):C.validate_report(self.report,"tls",expected_only=True)


class IntervalOuterMinusContracts(unittest.TestCase):
    def setUp(self):
        self.report=sample("shared");C.validate_report(self.report,"shared",expected_only=True)
        self.stage=next(c for c in self.report["cases"] if c["case_id"]=="NATIVE_INTERVAL_neg")["stages"][0]

    def test_frozen_negative_raw_typed_and_two_text_triangle(self):
        raw=bytes.fromhex("FFFFFFFFFFFFFFFF0000000000000000C01DFEFF96060000")
        value="INTERVAL '-000000001 01:00:00.123456' DAY(9) TO SECOND(6)"
        server="INTERVAL -'1 1:0:0.123456' DAY(9) TO SECOND(6)"
        self.assertEqual(hashlib.sha256(raw).hexdigest(),"1f4635fd456113d5f04b7d24fc3efd3b6dd3ae510b986160c61c95625b971540")
        self.assertEqual(hashlib.sha256(value.encode()).hexdigest(),"57fafeae47ce4d94248ff102710b9980973288058bc4cf17083a609d5572704e")
        self.assertEqual(hashlib.sha256(server.encode()).hexdigest(),"c35bb5bc06cc10a5245f3f051d3688a2177b06c54c48f6d5c98d21cc444fac36")
        fields,ticks=C.decode_interval(raw,1686);self.assertEqual(ticks,-900001234560)
        self.assertEqual(fields,C.expected_fields(C.vectors()["interval_neg"]))
        C.same(C.parse_interval_text(server,1686,"server"),fields,"observed_outer_minus")
        self.assertEqual(C.canonical_interval_text(C.vectors()["interval_neg"],1686),value)
        with self.assertRaises(C.ContractError):C.parse_interval_text(server,1686,"value")

    def test_inner_negative_and_synthetic_DAY2_outer_negative_retained(self):
        fields=C.expected_fields(C.vectors()["interval_neg"])
        for packed,leading in ((1574,2),(1686,9)):
            for literal in (f"INTERVAL '-1 1:0:0.123456' DAY({leading}) TO SECOND(6)",
                            f"INTERVAL -'1 1:0:0.123456' DAY({leading}) TO SECOND(6)"):
                C.same(C.parse_interval_text(literal,packed,"server"),fields,"expected_only_negative")

    def test_outer_inner_conflicts_and_unapproved_sign_positions(self):
        good="INTERVAL -'1 1:0:0.123456' DAY(9) TO SECOND(6)";C.parse_interval_text(good,1686,"server")
        bad=(good.replace("-'1","-'-1"),good.replace("-'1","-'+1"),good.replace("-'1","--'1"),
             good.replace("-'1","+'1"),good.replace("-'1","- '1"),"-"+good.replace(" -'"," '"),
             good.replace("1:0:0","-1:0:0"),good.replace("1:0:0","1:-0:0"),
             "INTERVAL -'0 0:0:0.000000' DAY(9) TO SECOND(6)")
        for literal in bad:
            with self.subTest(literal=literal),self.assertRaises(C.ContractError):C.parse_interval_text(literal,1686,"server")

    def test_outer_minus_cannot_be_W_value_even_with_full_padding(self):
        self.stage["observations"][0]["get_string"]="INTERVAL -'000000001 01:00:00.123456' DAY(9) TO SECOND(6)"
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"shared",expected_only=True)

    def test_text_sign_or_digit_mutation_cannot_override_raw_expected(self):
        good=self.stage["observations"][1]["server_text"]
        for changed in (good.replace(" -'"," '"),good.replace(".123456",".123455"),good.replace("'1 ","'2 ")):
            report=copy.deepcopy(self.report);stage=next(c for c in report["cases"] if c["case_id"]=="NATIVE_INTERVAL_neg")["stages"][0]
            stage["observations"][1]["server_text"]=changed
            with self.subTest(literal=changed),self.assertRaises(C.ContractError):C.validate_report(report,"shared",expected_only=True)

    def test_raw_sign_mutation_cannot_override_text_and_typed(self):
        obs=self.stage["observations"][0];raw=struct.pack("<6i",1,1,0,0,123456,1686)
        obs["raw"]["hex"]=raw.hex();obs["raw"]["payload_sha256"]=hashlib.sha256(raw).hexdigest()
        obs["cell"]["payload_sha256"]=obs["raw"]["payload_sha256"]
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"shared",expected_only=True)


class IntervalTextChannelContracts(unittest.TestCase):
    def setUp(self):
        self.report=sample("shared");C.validate_report(self.report,"shared",expected_only=True)

    def zero(self):
        return next(c for c in self.report["cases"] if c["case_id"]=="NATIVE_INTERVAL_zero")["stages"][0]

    def reject(self):
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"shared",expected_only=True)

    def test_frozen_zero_bytes_and_two_actual_channel_literals(self):
        raw=bytes.fromhex("000000000000000000000000000000000000000096060000")
        self.assertEqual(hashlib.sha256(raw).hexdigest(),"8eeec204e09cb4a7e35e0d81c4645bdd0962b2696f0ec7208f2a62c8aad7db96")
        value="INTERVAL '000000000 00:00:00.000000' DAY(9) TO SECOND(6)"
        server="INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)"
        self.assertEqual(hashlib.sha256(value.encode()).hexdigest(),"de042e037a898ddb3fd3fa1b29b5f45152ffd3609fe3dfba767578434ce57ca1")
        self.assertEqual(hashlib.sha256(server.encode()).hexdigest(),"49a6b8304f47f44ea1a1a7755100e85570bfe8c0ed1858b4b806f75de8e7e5b7")
        fields,ticks=C.decode_interval(raw,1686);self.assertEqual(ticks,0)
        C.same(C.parse_interval_text(value,1686,"value"),fields,"zero_value")
        C.same(C.parse_interval_text(server,1686,"server"),fields,"zero_server")
        self.assertEqual(C.canonical_interval_text(dict(kind="interval",ticks=0),1686),value)
        with self.assertRaises(C.ContractError):C.parse_interval_text(server,1686,"value")

    def test_synthetic_DAY2_DAY9_signed_fractional_controls(self):
        for packed in (1574,1686):
            for name in ("interval_zero","interval_pos","interval_neg"):
                with self.subTest(packed=packed,name=name):
                    spec=C.vectors()[name];fields=C.expected_fields(spec)
                    value=interval_value_fixture(fields,packed);server=text(fields,"interval",packed)
                    C.same(C.parse_interval_text(value,packed,"value"),fields,"value")
                    C.same(C.parse_interval_text(server,packed,"server"),fields,"server")
                    self.assertEqual(C.canonical_interval_text(spec,packed),value)

    def test_server_padded_and_mixed_HMS_width_controls(self):
        fields=C.expected_fields(C.vectors()["interval_pos"])
        for literal in ("INTERVAL '1 01:0:00.123456' DAY(9) TO SECOND(6)",
                        "INTERVAL '+000000001 1:00:0.123456' DAY(9) TO SECOND(6)"):
            C.same(C.parse_interval_text(literal,1686,"server"),fields,"server")

    def test_W_canonical_width_sign_fraction_negatives(self):
        baseline="INTERVAL '000000001 01:00:00.123456' DAY(9) TO SECOND(6)"
        C.parse_interval_text(baseline,1686,"value")
        bad=(baseline.replace("000000001","1"),baseline.replace("01:00:00","1:00:00"),
             baseline.replace("'000000001","'+000000001"),baseline.replace(".123456",".12345"),
             baseline.replace(".123456",".1234560"),"INTERVAL '-000000000 00:00:00.000000' DAY(9) TO SECOND(6)")
        for literal in bad:
            with self.subTest(literal=literal),self.assertRaises(C.ContractError):C.parse_interval_text(literal,1686,"value")

    def test_server_component_domain_width_fraction_negatives(self):
        baseline="INTERVAL '1 1:0:0.123456' DAY(2) TO SECOND(6)";C.parse_interval_text(baseline,1574,"server")
        bad=(baseline.replace("1 1:0:0","1 001:0:0"),baseline.replace("1 1:0:0","1 24:0:0"),
             baseline.replace("1:0:0","1:60:0"),baseline.replace("1:0:0","1:0:60"),
             baseline.replace("'1 ","'100 "),baseline.replace(".123456",".12345"),
             baseline.replace(".123456",".1234560"),baseline.replace(".123456",""),
             "INTERVAL '-0 0:0:0.000000' DAY(2) TO SECOND(6)")
        for literal in bad:
            with self.subTest(literal=literal),self.assertRaises(C.ContractError):C.parse_interval_text(literal,1574,"server")

    def test_full_wrapper_qualifier_subtype_ASCII_negatives(self):
        baseline="INTERVAL '0 0:0:0.000000' DAY(9) TO SECOND(6)";C.parse_interval_text(baseline,1686,"server")
        bad=(baseline.replace("DAY(9)","DAY(2)"),baseline.replace("SECOND(6)","SECOND(5)"),
             baseline.replace("DAY(9)","YEAR(9)"),baseline.replace("SECOND(6)","MONTH(6)"),
             baseline.removeprefix("INTERVAL '"),baseline.removesuffix(" DAY(9) TO SECOND(6)"),
             "0 0:0:0.000000", "-"+baseline,baseline.replace("'0 ","'--0 "),
             baseline.replace("'0 ","'０ ")," "+baseline,baseline+" ",baseline.lower(),baseline+"x")
        for literal in bad:
            with self.subTest(literal=literal),self.assertRaises(C.ContractError):C.parse_interval_text(literal,1686,"server")

    def test_actual_server_width_cannot_be_used_for_W_GetString(self):
        self.zero()["observations"][0]["get_string"]=self.zero()["observations"][1]["server_text"];self.reject()

    def test_actual_packed_qualifier_cannot_be_changed_by_server_text(self):
        obs=self.zero()["observations"][1];obs["server_text"]=obs["server_text"].replace("DAY(9)","DAY(2)");self.reject()

    def test_server_microsecond_loss_and_sign_changes_fail(self):
        for literal in ("INTERVAL '0 0:0:0.000001' DAY(9) TO SECOND(6)","INTERVAL '-0 0:0:0.000000' DAY(9) TO SECOND(6)"):
            report=copy.deepcopy(self.report);stage=next(c for c in report["cases"] if c["case_id"]=="NATIVE_INTERVAL_zero")["stages"][0]
            stage["observations"][1]["server_text"]=literal
            with self.subTest(literal=literal),self.assertRaises(C.ContractError):C.validate_report(report,"shared",expected_only=True)

    def test_nonzero_server_sign_must_match_expected_and_raw(self):
        stage=next(c for c in self.report["cases"] if c["case_id"]=="NATIVE_INTERVAL_pos")["stages"][0]
        stage["observations"][1]["server_text"]=stage["observations"][1]["server_text"].replace("'1 ","'-1 ");self.reject()

    def test_raw_payload_packed_schema_and_typed_crosschecks_remain(self):
        for mutation in ("packed","schema","typed"):
            report=copy.deepcopy(self.report);stage=next(c for c in report["cases"] if c["case_id"]=="NATIVE_INTERVAL_zero")["stages"][0];obs=stage["observations"][0]
            if mutation=="packed":
                raw=bytearray.fromhex(obs["raw"]["hex"]);raw[20:24]=struct.pack("<i",1574)
                obs["raw"]["hex"]=raw.hex();obs["raw"]["payload_sha256"]=hashlib.sha256(raw).hexdigest();obs["cell"]["payload_sha256"]=obs["raw"]["payload_sha256"]
            elif mutation=="schema":obs["schema_scale"]=5
            else:obs["ticks"]=10
            with self.subTest(mutation=mutation),self.assertRaises(C.ContractError):C.validate_report(report,"shared",expected_only=True)

    def test_shared_TLS_fixtures_do_not_claim_runtime_syntax_coverage(self):
        for profile in ("shared","tls"):
            report=sample(profile);results=[];C.validate_report(report,profile,expected_only=True,binding_read_results=results)
            self.assertTrue(report["expected_only"]);self.assertFalse(report["database_accessed"])
            self.assertTrue(all(not row["observed"] for row in results))


class ReadDiagnosticIsolationContracts(unittest.TestCase):
    def setUp(self):
        self.report=sample("shared")
        C.validate_report(self.report,"shared",expected_only=True)
        self.stage=next(c for c in self.report["cases"] if c["case_id"]=="NATIVE_INTERVAL_zero")["stages"][0]
        # Extra diagnostic structure is explicitly synthetic and not an independent observation.
        self.stage["read_attempts"]=[dict(expected_only=True,completed=False,failed_step="value_text_guard",
            case_id="NATIVE_INTERVAL_zero",stage_id="write",source="W",connection_id="fresh",row_id=1,ordinal=0,
            metadata=copy.deepcopy(self.stage["observations"][0]["metadata"]),
            partial_fields=copy.deepcopy(self.stage["observations"][0]["fields"]),partial_ticks=0,
            partial_raw=copy.deepcopy(self.stage["observations"][0]["raw"]))]

    def reject(self):
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"shared",expected_only=True)

    def test_partial_attempt_cannot_replace_missing_full_observations(self):
        self.stage["observations"]=[];self.reject()

    def test_completed_diagnostic_attempt_is_not_full_observation(self):
        self.stage["read_attempts"][0]["completed"]=True;self.stage["observations"]=[];self.reject()

    def test_partial_metadata_cannot_bypass_bad_actual_result_source(self):
        self.stage["observations"][0]["metadata"]["raw_source"]="ParameterDescribe";self.reject()

    def test_partial_fields_cannot_bypass_wrong_actual_typed_ticks(self):
        self.stage["observations"][0]["ticks"]=10;self.reject()

    def test_partial_raw_cannot_replace_actual_packet_hash(self):
        self.stage["observations"][0]["raw"]["payload_sha256"]="0"*64;self.reject()

    def test_diagnostic_text_shape_cannot_bypass_original_text_parser(self):
        self.stage["read_attempts"][0]["text_shape"]={"safe":True,"length":9}
        self.stage["observations"][1]["server_text"]="unverified";self.reject()

    def test_failed_read_process_cannot_claim_complete_profile(self):
        self.report["status"]="execution_failed";self.reject()

    def test_diagnostic_connection_identity_cannot_override_actual_connection(self):
        self.stage["observations"][0]["connection_id"]="unrelated";self.reject()


class LocalWriteRecoveryContracts(unittest.TestCase):
    def setUp(self):
        self.report=sample("shared")
        C.validate_report(self.report,"shared",expected_only=True)

    def target(self):
        return next(c for c in self.report["cases"] if c["case_id"]=="NATIVE_TIME_REJECT_LAST_TICK")["stages"][1]

    def retired(self,state="Broken"):
        target=self.target();local_recovery_receipts(target,"NATIVE_TIME_REJECT_LAST_TICK",state)
        C.validate_report(self.report,"shared",expected_only=True)
        return target

    def reject(self):
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"shared",expected_only=True)

    def test_Open_original_query_and_fresh_state_control(self):
        self.assertEqual(self.target()["recovery"]["path"],"same_open")
        C.validate_report(self.report,"shared",expected_only=True)

    def test_Broken_and_Closed_original_rejection_fresh_controls(self):
        for state in ("Broken","Closed"):
            with self.subTest(state=state):self.retired(state)

    def test_missing_target_state(self):
        self.target()["target_receipt"].pop("state_after");self.reject()

    def test_wrong_target_before_state(self):
        self.target()["target_receipt"]["state_before"]="Closed";self.reject()

    def test_unknown_target_after_state(self):
        self.target()["target_receipt"]["state_after"]="Connecting";self.reject()

    def test_missing_actual_send_observation(self):
        self.target()["target_receipt"]["send_observable"]=False;self.reject()

    def test_sync_callback_zero_is_not_async_business_zero(self):
        self.target()["target_receipt"]["api"]="ExecuteNonQuery";self.reject()

    def test_target_business6_or13_send_is_never_local_exemption(self):
        for opcode in (6,13):
            report=copy.deepcopy(self.report);target=next(c for c in report["cases"] if c["case_id"]=="NATIVE_TIME_REJECT_LAST_TICK")["stages"][1]
            target["target_receipt"].update(sent_frames=[send_frame("NATIVE_TIME_REJECT_LAST_TICK",opcode)],all_send_count=1,business_send_count=1)
            with self.subTest(opcode=opcode),self.assertRaises(C.ContractError):C.validate_report(report,"shared",expected_only=True)

    def test_prior_prepare_send_is_allowed_and_not_claimed_whole_NotSent(self):
        target=self.target();target["target_receipt"].update(sent_frames=[send_frame("NATIVE_TIME_REJECT_LAST_TICK",5)],all_send_count=1,prepare_or_direct_count=1)
        target["diagnostic_business_send"]=None;C.validate_report(self.report,"shared",expected_only=True)

    def test_missing_FailureInfo_cannot_be_fabricated_NotSent(self):
        self.target()["diagnostic_business_send"]="NotSent";self.reject()

    def test_retired_old_probe_any_send_fails(self):
        target=self.retired();probe=target["recovery"]["steps"][0]
        probe.update(sent_frames=[send_frame("NATIVE_TIME_REJECT_LAST_TICK",5)],all_send_count=1);self.reject()

    def test_retired_old_probe_wrong_exception_fails(self):
        self.retired()["recovery"]["steps"][0]["error"]["type"]="System.NotSupportedException";self.reject()

    def test_retired_old_probe_wrong_state_fails(self):
        self.retired()["recovery"]["steps"][0]["state_after"]="Open";self.reject()

    def test_Open_failure_cannot_switch_to_retired_exemption(self):
        self.target()["recovery"]["path"]="retired_connection";self.reject()

    def test_fresh_root_identity_and_scalar_are_real(self):
        target=self.retired();fresh=next(s for s in target["recovery"]["steps"] if s["step_id"]=="fresh_connection_open")
        fresh["identity_user"]="SYSDBA";self.reject()

    def test_fresh_probe_wrong_scalar_fails(self):
        target=self.retired();next(s for s in target["recovery"]["steps"] if s["step_id"]=="fresh_connection_probe")["scalar"]=2;self.reject()

    def test_fresh_reusing_retired_ID_fails(self):
        target=self.retired();old=target["operation_connection_id"]
        target["fresh_connection_id"]=old;target["recovery"]["fresh_connection_id"]=old
        for step in target["recovery"]["steps"]:
            if step["step_id"].startswith("fresh_"):step["connection_id"]=old
        for obs in target["observations"]:obs["connection_id"]=old
        self.reject()

    def test_fresh_old_value_changed_fails(self):
        self.retired()["observations"][0]["ticks"]+=10;self.reject()

    def test_fresh_old_row_count_changed_fails(self):
        self.retired()["fresh_rows"]=2;self.reject()

    def test_fresh_schema_changed_fails(self):
        self.retired()["fresh_column_metadata"]["semantic_scale"]=7;self.reject()

    def test_hidden_old_connection_reopen_step_fails(self):
        target=self.retired();target["recovery"]["steps"].insert(3,dict(step_id="old_connection_reopen",status="completed",api="OpenAsync",connection_id=target["operation_connection_id"]))
        self.reject()

    def test_old_connection_revived_after_fresh_fails(self):
        self.retired()["recovery"]["state_after"]="Open";self.reject()

    def test_missing_dispose_or_incomplete_substep_fails(self):
        target=self.retired();target["recovery"]["steps"][1]["status"]="attempted";self.reject()

    def test_failed_middle_substep_cannot_claim_completed(self):
        self.retired()["recovery"]["failed_step"]="fresh_row_verify";self.reject()

    def test_retired_next_ASYNC_independent_stage_has_explicit_new_ID(self):
        stages=next(c for c in self.report["cases"] if c["case_id"]=="ASYNC_REJECT_THEN_QUERY")["stages"]
        local_recovery_receipts(stages[1],"ASYNC_REJECT_THEN_QUERY","Broken")
        stages[2].update(operation_connection_id="next-independent",connection_origin="new_connection_for_next_independent_stage_after_safe_abort",previous_connection_id=stages[1]["operation_connection_id"])
        local_recovery_receipts(stages[2],"ASYNC_REJECT_THEN_QUERY")
        C.validate_report(self.report,"shared",expected_only=True)
        stages[2]["previous_connection_id"]="unrelated";self.reject()

    def test_retired_next_ASYNC_cannot_reuse_old_ID(self):
        stages=next(c for c in self.report["cases"] if c["case_id"]=="ASYNC_REJECT_THEN_QUERY")["stages"]
        local_recovery_receipts(stages[1],"ASYNC_REJECT_THEN_QUERY","Broken");self.reject()

    def test_Open_next_ASYNC_cannot_replace_connection(self):
        stages=next(c for c in self.report["cases"] if c["case_id"]=="ASYNC_REJECT_THEN_QUERY")["stages"]
        stages[2].update(operation_connection_id="replacement",connection_origin="new_connection_for_next_independent_stage_after_safe_abort",previous_connection_id=stages[1]["operation_connection_id"])
        local_recovery_receipts(stages[2],"ASYNC_REJECT_THEN_QUERY");self.reject()

    def test_reader_recovery_cannot_use_local_write_fresh_exemption(self):
        reader=next(c for c in self.report["cases"] if c["case_id"]=="READER_ORDINARY_default")["stages"][0]
        reader["recovery"]=copy.deepcopy(self.retired()["recovery"])
        reader["receipt"]["scalar_after_dispose"]=0;self.reject()


class BindingReadContracts(unittest.TestCase):
    def setUp(self):
        self.report=sample("shared")
        C.validate_report(self.report,"shared",expected_only=True)

    def retained(self,kind="ts",scale=3):
        actual=retained7_stage(self.report,kind,scale)
        C.validate_report(self.report,"shared",expected_only=True)
        return actual

    def reject(self,context=None):
        with self.assertRaises(C.ContractError):
            C.validate_report(self.report,"shared",expected_only=True,read_context=context)

    def test_equal_legacy3_legacy6_and_extended7_controls(self):
        tuples=set()
        for profile in ("shared","tls"):
            results=[];C.validate_report(sample(profile),profile,expected_only=True,binding_read_results=results)
            for row in results:
                self.assertEqual(row["write_tuple"],row["read_tuple"])
                self.assertEqual(row["rule_id"],"g2_write_read_equal_v1")
                self.assertFalse(row["observed"]);self.assertEqual(row["observation_status"],"expected_only")
                tuples.add((row["write_tuple"]["c_type"],row["write_tuple"]["scale"]))
        self.assertTrue({(16,3),(16,6),(23,3),(23,6),(26,7),(27,7)}<=tuples)

    def test_shared_scale3_scale6_same_family_retained7_controls(self):
        for kind in ("ts","tz"):
            for scale in (3,6):
                with self.subTest(kind=kind,scale=scale):
                    report=sample("shared");stage=retained7_stage(report,kind,scale);results=[]
                    C.validate_report(report,"shared",expected_only=True,binding_read_results=results)
                    row=next(r for r in results if r["case_id"]=="BIND_METADATA_AUTOREPREPARE_"+("dt" if kind=="ts" else "dto") and r["stage_id"]==stage["stage_id"])
                    self.assertEqual(row["write_tuple"]["scale"],scale)
                    self.assertEqual(stage["encoded_scale"],scale)
                    self.assertEqual(row["read_tuple"],dict(c_type=26 if kind=="ts" else 27,scale=7,width=9 if kind=="ts" else 11))
                    self.assertEqual(row["rule_id"],"g2_shared_same_family_retained7_v1")
                    self.assertFalse(row["observed"])
                    self.assertEqual(row["policy_status"],"design_allowed")
                    self.assertEqual(row["runtime_status"],"runtime_pending")
                    self.assertEqual(row["unobserved_alternatives"][0]["runtime_status"],"runtime_pending")

    def test_equal_control_does_not_claim_retained7_observed(self):
        results=[];C.validate_report(self.report,"shared",expected_only=True,binding_read_results=results)
        row=next(r for r in results if r["case_id"]=="BIND_METADATA_AUTOREPREPARE_dt" and r["stage_id"]=="scale3")
        self.assertEqual(row["unobserved_alternatives"],[dict(read_tuple=dict(c_type=26,scale=7,width=9),
            rule_id="g2_shared_same_family_retained7_v1",policy_status="design_allowed",runtime_status="runtime_pending")])

    def test_retained7_cannot_change_parameter_scale3(self):
        self.retained()["parameter"]["scale"]=7;self.reject()

    def test_retained7_cannot_change_encoded_scale3(self):
        self.retained()["encoded_scale"]=7;self.reject()

    def test_retained7_cannot_change_parameter_metadata(self):
        self.retained()["parameter_metadata"]["semantic_scale"]=7;self.reject()

    def test_retained7_requires_root_decision_even_with_boolean_flag(self):
        self.retained();context=expected_read_context("shared");context.update(root_decision_id=None,root_approved=True);self.reject(context)

    def test_retained7_requires_design_pin(self):
        self.retained();context=expected_read_context("shared");context["design_sha256"]="0"*64;self.reject(context)

    def test_retained7_requires_exact_SQL_source(self):
        self.retained();context=expected_read_context("shared");context["cases_source_sha256"]="0"*64;self.reject(context)

    def test_retained7_wrong_physical_contexts(self):
        self.retained()
        for key,value in (("server_version","8.1.4.6"),("message_version",11),("encoding","gb18030"),
                          ("encoding_override_empty",False),("result_format","bdta")):
            changed=copy.deepcopy(self.report);changed["W_profile"][key]=value
            with self.subTest(key=key),self.assertRaises(C.ContractError):C.validate_report(changed,"shared",expected_only=True)

    def test_retained7_wrong_context_profile(self):
        self.retained();context=expected_read_context("shared");context["profile"]="tls";self.reject(context)

    def test_unknown_physical_profile_closed(self):
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"unknown",expected_only=True)

    def test_retained7_raw_role_cannot_be_parameter_describe(self):
        self.retained()["observations"][0]["metadata"]["raw_source"]="ParameterDescribe";self.reject()

    def test_retained7_raw_effective_metadata_must_match(self):
        self.retained()["observations"][0]["metadata"]["raw_c_type"]=16;self.reject()

    def test_retained7_schema_must_be_result_scale(self):
        self.retained()["observations"][0]["schema_scale"]=3;self.reject()

    def test_retained7_packet_hash_must_match(self):
        self.retained()["observations"][0]["raw"]["payload_sha256"]="0"*64;self.reject()

    def test_retained7_packet_length_must_match(self):
        self.retained()["observations"][0]["cell"]["payload_length"]=8;self.reject()

    def test_retained7_raw_integer_fields_must_match(self):
        obs=self.retained()["observations"][0];data=bytearray.fromhex(obs["raw"]["hex"]);data[3]^=1
        obs["raw"]["hex"]=data.hex();obs["raw"]["payload_sha256"]=hashlib.sha256(data).hexdigest()
        obs["cell"]["payload_sha256"]=obs["raw"]["payload_sha256"];self.reject()

    def test_retained7_typed_ticks_must_match(self):
        self.retained()["observations"][0]["ticks"]+=1;self.reject()

    def test_retained7_typed_offset_must_match(self):
        self.retained("tz")["observations"][0]["offset_minutes"]=0;self.reject()

    def test_retained7_raw_offset_must_match(self):
        obs=self.retained("tz")["observations"][0];data=bytearray.fromhex(obs["raw"]["hex"]);data[-2:]=(60).to_bytes(2,"little",signed=True)
        obs["raw"]["hex"]=data.hex();obs["raw"]["payload_sha256"]=hashlib.sha256(data).hexdigest()
        obs["cell"]["payload_sha256"]=obs["raw"]["payload_sha256"];self.reject()

    def test_retained7_independent_server_text_must_match(self):
        obs=self.retained()["observations"][1];obs["server_text"]=obs["server_text"].replace(".123000000",".123456000");self.reject()

    def test_retained7_GetString_missing_trailing_zero(self):
        obs=self.retained()["observations"][0];obs["get_string"]=obs["get_string"][:-1];self.reject()

    def test_retained7_GetString_extra_trailing_zero(self):
        self.retained()["observations"][0]["get_string"]+="0";self.reject()

    def test_result_scale8_scale9_are_unverified(self):
        self.retained()
        for scale in (8,9):
            report=copy.deepcopy(self.report)
            stage=next(c for c in report["cases"] if c["case_id"]=="BIND_METADATA_AUTOREPREPARE_dt")["stages"][1]
            stage["observations"][0]["metadata"]["semantic_scale"]=scale
            with self.subTest(scale=scale),self.assertRaises(C.UnverifiedResultDescriptor):C.validate_report(report,"shared",expected_only=True)

    def test_cross_family_result_is_unverified(self):
        stage=self.retained();old=stage["observations"][0]
        stage["observations"][0]=oracle(C.vectors()["tz_scale3"],27,7,False,connection=old["connection_id"],row=old["row_id"])
        with self.assertRaises(C.UnverifiedResultDescriptor):C.validate_report(self.report,"shared",expected_only=True)

    def test_TLS_retained7_is_unverified(self):
        report=sample("tls");C.validate_report(report,"tls",expected_only=True);retained7_stage(report,"ts",6)
        with self.assertRaises(C.UnverifiedResultDescriptor):C.validate_report(report,"tls",expected_only=True)

    def test_retained7_BDTA_and_TLTZ_closed(self):
        self.retained()
        for key,value in (("is_bdta",True),("mask",4)):
            report=copy.deepcopy(self.report)
            target=next(c for c in report["cases"] if c["case_id"]=="BIND_METADATA_AUTOREPREPARE_dt")["stages"][1]
            target["observations"][0]["metadata"][key]=value
            with self.subTest(key=key),self.assertRaises(C.ContractError):C.validate_report(report,"shared",expected_only=True)

    def test_retained7_cannot_mix_another_connection_server_projection(self):
        self.retained()["observations"][1]["connection_id"]="other-query";self.reject()

    def test_fixed_DML_cannot_use_G2_retained7_rule(self):
        actual=next(c for c in self.report["cases"] if c["case_id"]=="TX_DIRECT_INSERT_legacy6_ts")["stages"][0]
        old=actual["observations"][0];actual["observations"][0]=oracle(C.vectors()["ts_micro"],26,7,False,
            connection=old["connection_id"],row=old["row_id"])
        self.reject()

    def test_rejection_recovery_cannot_use_G2_retained7_rule(self):
        report=sample("tls");C.validate_report(report,"tls",expected_only=True)
        actual=next(c for c in report["cases"] if c["case_id"]=="BIND_REUSE_NO_EXPLICIT_PREPARE_dt")["stages"][2]
        actual["observations"]=[oracle(C.vectors()["ts_micro"],26,7,False,source=source,connection="recovery",row=0) for source in ("W","server")]
        with self.assertRaises(C.ContractError):C.validate_report(report,"tls",expected_only=True)

    def test_read_result_output_waits_for_all_lifecycle_checks(self):
        self.retained();results=[]
        clone=next(c for c in self.report["cases"] if c["case_id"]=="BIND_CLONE_RESET_REOPEN")
        clone["clone_after"]["scale"]=3
        with self.assertRaises(C.ContractError):C.validate_report(self.report,"shared",expected_only=True,binding_read_results=results)
        self.assertEqual(results,[])


class IdentityAndCleanupContracts(unittest.TestCase):
    def test_literal_direct5_error_has_independent_allocation_frame(self):
        control=dict(expected_only=True,database_accessed=False)
        literal_receipts(control,-6118)
        actual=C.literal_protocol(control)
        self.assertEqual(actual["request_opcode"],5)
        self.assertEqual(control["allocation_count"],1)
        failure=dict(type="W.Dm.DmException",number=-6118,failure_class="verified_server_response",
            verified_server_response=True,outcome="ServerReported",failure_kind="Server",phase="Receive")
        C.verified_leading_server_error(failure,control["server_response"],(5,))
        control["server_response"]=C.response_receipt(frame(3,-6118))
        with self.assertRaises(C.ContractError):C.literal_protocol(control)

    def test_literal_allocation3_only_cannot_supply_error_response(self):
        control=dict(expected_only=True,database_accessed=False)
        literal_receipts(control,-6118);C.literal_protocol(control)
        control.update(execution_frames=[frame(3,-6118)],business_frames=[],prepare_or_direct_count=0,
                       business_response_count=0,server_response=C.response_receipt(frame(3,-6118)))
        with self.assertRaises(C.ContractError):C.literal_protocol(control)

    def test_literal_error_response_must_match_actual_direct5(self):
        control=dict(expected_only=True,database_accessed=False)
        literal_receipts(control,-6118);C.literal_protocol(control)
        control["server_response"]["sql_code"]=-6019
        with self.assertRaises(C.ContractError):C.literal_protocol(control)

    def test_literal_multiple_direct5_responses_are_ambiguous(self):
        control=dict(expected_only=True,database_accessed=False)
        literal_receipts(control,-6118);C.literal_protocol(control)
        control["execution_frames"].append(frame(5,-6118));control["business_frames"].append(frame(5,-6118))
        control.update(prepare_or_direct_count=2,business_response_count=2)
        with self.assertRaises(C.ContractError):C.literal_protocol(control)

    def test_prepared_error_cannot_be_allocation3_response(self):
        failure=dict(type="W.Dm.DmException",number=-6118,failure_class="verified_server_response",
            verified_server_response=True,outcome="ServerReported",failure_kind="Server",phase="Receive")
        response=C.response_receipt(frame(6,-6118));C.verified_leading_server_error(failure,response)
        response["request_opcode"]=3
        with self.assertRaises(C.ContractError):C.verified_leading_server_error(failure,response)

    def test_offline_helper_contracts_cannot_be_omitted_by_same_count(self):
        C.same(list(C.OFFLINE_CHECKS),C.OFFLINE_CHECKS,"offline")
        altered=list(C.OFFLINE_CHECKS);altered[altered.index("server_null_async_getter")]="owner_valid"
        with self.assertRaises(C.ContractError):C.same(altered,C.OFFLINE_CHECKS,"offline")

    def test_same_source_archive_package_capability_pins(self):
        identity=dict(W=copy.deepcopy(C.W_PIN),producer=dict(source_head=C.SOURCE_HEAD,
            source_manifest_sha256=C.SOURCE_SHA,source_archive_sha256=C.ARCHIVE_SHA))
        args=[identity,C.IDENTITIES_SHA,C.SOURCE_SHA,C.ARCHIVE_SHA,C.CAPS_SHA]
        C.validate_candidate_pins(*args)
        for offset in range(1,5):
            changed=copy.deepcopy(args);changed[offset]="0"*64
            with self.subTest(offset=offset),self.assertRaises(C.ContractError):C.validate_candidate_pins(*changed)
        for key,value in (("version","different"),("asset","lib/net9.0/W.DmProvider.dll"),("package_sha256","0"*64),("dll_sha256","0"*64),("mvid","00000000-0000-0000-0000-000000000000")):
            changed=copy.deepcopy(args);changed[0]["W"][key]=value
            with self.subTest(key=key),self.assertRaises(C.ContractError):C.validate_candidate_pins(*changed)
        changed=copy.deepcopy(args);changed[0]["producer"]["source_head"]="0"*40
        with self.assertRaises(C.ContractError):C.validate_candidate_pins(*changed)

    def test_real_pure_package_hash_DLL_and_MVID_mutations(self):
        # A tiny handwritten PE is an offline expected-only fixture, never the candidate DLL.
        blob=bytearray(768)
        def put(offset,value,length):blob[offset:offset+length]=value.to_bytes(length,"little")
        blob[:2]=b"MZ";put(0x3c,64,4);blob[64:68]=b"PE\0\0";put(70,1,2);put(84,224,2)
        put(88,0x10b,2);put(88+96+14*8,0x2000,4)
        section=312;put(section+8,512,4);put(section+12,0x2000,4);put(section+16,512,4);put(section+20,256,4)
        put(256+8,0x2100,4);md=512;blob[md:md+4]=b"BSJB";put(md+12,4,4);blob[md+16:md+20]=b"v1\0\0"
        put(md+22,2,2);put(md+24,128,4);put(md+28,40,4);blob[md+32:md+36]=b"#~\0\0"
        put(md+36,192,4);put(md+40,16,4);blob[md+44:md+52]=b"#GUID\0\0\0"
        tables=640;put(tables+8,1,8);put(tables+24,1,4);put(tables+32,1,2)
        mvid="00112233-4455-6677-8899-aabbccddeeff";blob[704:720]=uuid.UUID(mvid).bytes_le
        base,_=C.reviewed_module("tp06_test_base_package","v11-contracts.py",C.BASE_CONTRACT_SHA)
        self.assertEqual(base._managed_mvid(bytes(blob)),mvid)
        with tempfile.TemporaryDirectory() as temporary:
            package=Path(temporary)/"fixture.nupkg";asset="lib/net10.0/Fixture.dll"
            with zipfile.ZipFile(package,"w") as archive:
                archive.writestr(asset,blob)
                archive.writestr("Fixture.nuspec","<package><metadata><id>Fixture</id><version>1.0.0</version></metadata></package>")
            pin=dict(id="Fixture",version="1.0.0",asset=asset,package_sha256=C.digest(package),dll_sha256=hashlib.sha256(blob).hexdigest(),mvid=mvid)
            base.validate_package_identity(package,pin)
            for key,value in (("package_sha256","0"*64),("dll_sha256","0"*64),("mvid","00000000-0000-0000-0000-000000000000"),("asset","lib/other/Fixture.dll")):
                changed=dict(pin);changed[key]=value
                with self.subTest(key=key),self.assertRaises(base.ContractError):base.validate_package_identity(package,changed)
            package.write_bytes(package.read_bytes()+b"mutated")
            with self.assertRaises(base.ContractError):base.validate_package_identity(package,pin)

    def test_manifest_is_fixed(self):
        C.same(C.load_json(HERE/"case-manifest.json"),C.manifest(),"fixed")

    def test_manifest_vector_drift(self):
        data=C.manifest();data["vectors"]["ts_tick"]["ticks"]+=1
        with self.assertRaises(C.ContractError):C.same(data,C.manifest(),"fixed")

    def test_duplicate_json_and_nonfinite(self):
        for text_value in ('{"x":1,"x":2}','{"x":NaN}','{"x":Infinity}'):
            with self.subTest(text_value=text_value),self.assertRaises(C.ContractError):C.loads_strict(text_value)

    def test_physical_profiles_and_drift(self):
        for profile,version,encoding,message in (("shared","8.1.5.60","utf-8",21),("tls","8.1.4.6","gb18030",11)):
            value=dict(server_version=version,encoding=encoding,negotiated_encoding=encoding,message_version=message,timezone=480,
                encoding_override_empty=True,result_format="ordinary_non_BDTA",server_user="WDM_PROVIDER_TEST",server_schema="WDM_PROVIDER_TEST",
                profile_origin="physical_get_conn_instance_conn_property",negotiated_encoding_origin="physical_conn_property_serverEncoding_field")
            V.physical_profile(value,profile)
            for key,replacement in (("encoding","big5"),("message_version",0),("server_schema","SYSDBA"),("encoding_override_empty",False)):
                changed=dict(value);changed[key]=replacement
                with self.subTest(profile=profile,key=key),self.assertRaises(V.C.ContractError):V.physical_profile(changed,profile)

    def test_package_asset_and_project_drift(self):
        identity=dict(W=C.W_PIN,O=dict(id="DM.DmProvider",version="8.3.1.47463",asset="lib/net9.0/DM.DmProvider.dll"))
        libraries={v["id"]+"/"+v["version"]:dict(type="package") for v in identity.values()}
        target={v["id"]+"/"+v["version"]:dict(runtime={v["asset"]:{}}) for v in identity.values()}
        value=dict(libraries=libraries,targets={"net10.0":target});V.package_assets(value,identity,"test")
        for mutation in ("project","version","asset"):
            changed=copy.deepcopy(value);key=C.W_PIN["id"]+"/"+C.W_PIN["version"]
            if mutation=="project":changed["libraries"][key]["type"]="project"
            if mutation=="version":changed["libraries"]["W.DmProvider/other"]={"type":"package"}
            if mutation=="asset":changed["targets"]["net10.0"][key]["runtime"]={"lib/other/W.DmProvider.dll":{}}
            with self.subTest(mutation=mutation),self.assertRaises(V.C.ContractError):V.package_assets(changed,identity,"test")

    def test_cleanup_receipt_mutations(self):
        owner=dict(table="TPN_"+"A"*24,owner_marker="OWN_"+"A"*24)
        receipt=dict(identity_user="WDM_PROVIDER_TEST",identity_schema="WDM_PROVIDER_TEST",fresh_identity_user="WDM_PROVIDER_TEST",
            fresh_identity_schema="WDM_PROVIDER_TEST",table_observed=owner["table"],guard_verified=True,exists_before_drop=True,drops=1,
            marker_observed=owner["owner_marker"],fresh_table_count=0,fresh_connection_id="fresh-cleanup")
        value=dict(cleanup_verified=True,server_identity_verified=True,final_database_state="owned_table_absent_fresh_connection",
                   cleanup_connection_id="drop-connection",cleanup_receipt=receipt)
        receipt["cleanup_connection_id"]="drop-connection"
        V.cleanup_evidence(value,owner,main=True)
        for key,replacement in (("marker_observed","OTHER"),("fresh_table_count",1),("fresh_connection_id","drop-connection"),("guard_verified",False),("drops",0),("identity_user","SYSDBA")):
            changed=copy.deepcopy(value);changed["cleanup_receipt"][key]=replacement
            with self.subTest(key=key),self.assertRaises(V.C.ContractError):V.cleanup_evidence(changed,owner,main=True)
        changed=dict(value);changed.pop("cleanup_receipt")
        with self.assertRaises(V.C.ContractError):V.cleanup_evidence(changed,owner,main=True)

    def test_interval_header_and_mixed_sign(self):
        golden=bytes.fromhex("FFFFFFFFFEFFFFFFFDFFFFFFFCFFFFFFAE55F7FF96060000")
        fields,ticks=C.decode_interval(golden,1686)
        self.assertEqual(ticks,-(C.DAY+2*3600*C.SECOND+3*60*C.SECOND+4*C.SECOND+5_678_900))
        self.assertTrue(fields["negative"])
        for wrong in (struct.pack("<6i",-1,2,-3,-4,-567890,1686),golden[:20]+struct.pack("<i",1574),golden[:-1]):
            with self.subTest(raw=wrong.hex()),self.assertRaises(C.ContractError):C.decode_interval(wrong,1686)

    def test_native_numeric_boundaries_are_independent(self):
        self.assertEqual(C.vectors()["interval_min_micro"]["ticks"],-9_223_372_036_854_775_800)
        self.assertEqual(C.vectors()["interval_max_micro"]["ticks"],9_223_372_036_854_775_800)
        self.assertEqual(C.vectors()["time_last_micro"]["ticks"],863_999_999_990)

    def test_incomplete_offline_artifacts_never_accept(self):
        with tempfile.TemporaryDirectory() as directory:
            result=V.validate(directory)
            self.assertFalse(result["candidate_acceptance"])
            self.assertFalse(result["full_temporal_acceptance"])


if __name__ == "__main__":
    unittest.main()
