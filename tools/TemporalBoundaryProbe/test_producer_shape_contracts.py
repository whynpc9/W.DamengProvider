"""Portable serializer-consumer contracts; explicit --shape consumes real C# offline output.

Discovery has no environment/ignored dependency and never claims a real C# run.
Low must separately run the built C# offline entry, then this CLI with its shape file.
"""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
spec = importlib.util.spec_from_file_location("shape_validator", ROOT / "eng/temporal-precision.validate.py")
V = importlib.util.module_from_spec(spec)
spec.loader.exec_module(V)


def handwritten_shape():
    """Minimal independently handwritten values matching the agreed serializer topology."""
    def value(offset):
        return dict(outcome="returned",type="System.DateTime" if offset is None else "System.DateTimeOffset",
            ticks=639204129161234560,offset_minutes=offset,kind="Unspecified" if offset is None else None,text=None,error=None)
    def metadata(role):
        return dict(available=True,c_type=26,width=9,semantic_scale=7,mask=0,type_flag=role,
            raw_scale_status="captured_server_source",raw_source="ResultColumn" if role==0 else "ParameterDescribe",
            raw_c_type=2 if role==2 else 26,raw_precision=32767 if role==2 else 9,raw_scale=0 if role==2 else 7,
            raw_mask_before=0,raw_derived_mask=0,result_format="ordinary_packed",is_bdta=False)
    error=dict(type="System.NotSupportedException",number=None,failure_class="user_rejection",verified_server_response=False,
        failure_kind=None,phase=None,code=None,outcome=None)
    rejection=dict(phase="initial",status="driver_parameter_rejected",row_id=None,persisted_rows=None,persistence_source="not_executed",
        expected=value(None),requested_scale=6,requested_precision=7,execute_attempted=False,
        parameter_metadata=[],parameter_metadata_before_execute=[],oracles=[],error=error,failure_class="user_rejection",
        encoded_scale=None,encoded_scale_observable=False)
    before=dict(statement_owner="command",statement_id=501,parameter_metadata=[metadata(2)])
    live=dict(statement_owner="reader",statement_id=501,parameter_metadata=[metadata(2)],encoded_scale=7,encoded_scale_observable=True)
    closed=dict(statement_owner="unobservable",statement_id=None,parameter_metadata=[],encoded_scale=None,encoded_scale_observable=False)
    outcomes={
        "preexisting":("rejected",0),"known_create_failure":("rejected",0),"lost_ack_true_marker":("cleaned",1),
        "missing_marker":("rejected",0),"wrong_marker":("rejected",0),"same_prefix_wrong_full_nonce":("rejected",0),
        "bad_profile":("rejected",0),"bad_package":("rejected",0),"bad_identity":("rejected",0),"bad_nonce":("rejected",0),
        "duplicate_manifest":("rejected",0),"malformed_manifest":("rejected",0),"reserved_present":("rejected",0),
        "reserved_absent":("cleaned",0),"inflight_present":("cleaned",1),"confirmed_present":("cleaned",1),
        "wrong_db_identity":("rejected",0),"cleanup_failure":("rejected",1),"fresh_absence_failure":("rejected",1)}
    cleanup=[]
    for key,(outcome,drops) in outcomes.items():
        calls=["identity","exists","marker","drop"] if drops else ["identity"]
        if outcome=="cleaned" or key=="fresh_absence_failure":calls += ["fresh_identity","fresh_exists"]
        cleanup.append(dict(case_id=key,outcome=outcome,calls=calls,drop_count=drops))
    null_error=dict(type="W.Dm.DmException",number=6081,failure_class="infra_failure",verified_server_response=False,
        failure_kind="Transport",phase="Fetch",code="WDM_TRANSPORT",outcome="NotSent")
    null_receipt=dict(observation_class="known_typed_null_rejection",expected_dbnull=True,reader_lane="W",ordinal=0,getter_name="GetString",value_is_dbnull=True,
        server_format_is_dbnull=True,cache_observable=True,cached_cell=dict(is_null=True,payload_length=None,payload_sha256=None),actual_getter_invoked=True,
        provider_exception_type="W.Dm.DmException",number=6081,has_real_infra_inner=False,verified_server_response=False,cancel_source="None",transaction_outcome=None,
        info_present=True,server_error_number=None)
    null_negative=[dict(case_id=key,receipt_granted=False,getter_calls=1) for key in ("non_null_6081","expected_nonnull_6081","wrong_ordinal_6081","wrong_lane_6081","missing_cache_evidence","contradictory_cache","timeout_inner","socket_inner","cancel_inner","unknown_outcome","unknown_kind","verified_server_error","transaction_unknown","getvalue_6081")]
    fixed=dict(execution_api="ExecuteReader",snapshot_timing="live_reader_before_drain",statement_before_owner="command",statement_after_owner="reader",statement_id_before=701,statement_id_after=701,
        prepare_invoked=True,new_statement_observed=True,parameter_metadata=[metadata(1)],encoded_scale=7,encoded_scale_observable=True,
        drain_completed=True,records_affected=1,unexpected_rowset=False,after_close=copy.deepcopy(closed))
    import hashlib
    def raw_sample(key,literal,ctype,offset):
        m=metadata(0);m.update(c_type=ctype,width=9 if ctype==26 else 11,raw_c_type=ctype,raw_precision=9 if ctype==26 else 11,metadata_role="result_column")
        data=bytes.fromhex(literal)
        raw=dict(evidence_role="independent_cached_temporal_packed_input",source="ordinary_result_cache_before_temporal_projection",capture_stage="before_any_temporal_getter",read_only_synthetic=True,observable=True,
            c_type=ctype,semantic_scale=7,width=m["width"],mask=0,metadata_role="result_column",result_format="ordinary_non_BDTA",column_result_format="ordinary_packed",is_bdta=False,
            payload_length=len(data),payload_sha256=hashlib.sha256(data).hexdigest(),synthetic_temporal_hex=literal,layout_family="TS9" if ctype==26 else "TS_TZ11")
        return dict(case_id=key,metadata=m,raw_oracle=raw)
    raw_samples=[raw_sample(key,literal,ctype,offset) for key,literal,ctype,offset in (
        ("DT9_tick1","EA87BBEE81C8000000",26,None),("TZ11_offset0_tick1","EA87BBEE81C80000000000",27,0),
        ("TZ11_offset480_tick1","EA87BBEE81C8000000E001",27,480),("TZ11_offsetN300_tick1","EA87BBEE81C8000000D4FE",27,-300),("TZ11_offset330_tick1","EA87BBEE81C80000004A01",27,330))]
    for key in ("unknown12_closed","bdta_closed","type_length_mismatch_closed","mask4_closed"):
        raw_samples.append(dict(case_id=key,metadata=metadata(0),raw_oracle=dict(observable=False,synthetic_temporal_hex=None)))
    raw_before=copy.deepcopy(raw_samples[0]["raw_oracle"]);raw_after=copy.deepcopy(raw_before)
    raw_after.update(synthetic_temporal_hex="EA87BBEE81CA000000",payload_sha256=hashlib.sha256(bytes.fromhex("EA87BBEE81CA000000")).hexdigest())
    raw_mutation=dict(before=raw_before,after=raw_after)
    profile_capture = dict(logical_input=dict(server_version="9.9.9.9",encoding="gb18030",message_version=19,timezone=-300),logical_reads=0,
        captured_profile=dict(server_version="8.1.5.60",encoding="utf-8",negotiated_encoding="utf-8",message_version=21,timezone=480,
            result_format="ordinary_non_BDTA",server_user="WDM_PROVIDER_TEST",server_schema="WDM_PROVIDER_TEST",encoding_override_empty=True,
            profile_origin="physical_get_conn_instance_conn_property",negotiated_encoding_origin="physical_conn_property_serverEncoding_field"),
        rejections=[dict(case_id=key,capture_rejected=True) for key in ("missing_method","missing_instance","missing_property","nonempty_override",
            "encoding_conflict","encoding_unobservable","message_not_integer","override_unobservable","version_invalid","public_version_conflict")])
    return dict(schema_version=1,task="TP-01-offline-shape",synthetic=True,expected_only=True,database_accessed=False,
        physical_profile_capture=profile_capture,
        samples=[dict(case_id="sample_dt",observation=value(None)),dict(case_id="sample_dto",observation=value(330))],
        fixed_targets=[dict(profile=profile,tz=tz,target_column=column) for profile,tz,column in (("shared",False,"D7"),("shared",True,"Z7"),("tls",False,"D6"),("tls",True,"Z6"))],
        metadata=dict(result_column=metadata(0),fixed_parameter=metadata(1),advisory_parameter=metadata(2)),rejections=[rejection],
        statement_ownership=dict(before=before,after_live=live,after_close=closed),
        cleanup_cases=cleanup,
        null_observations=dict(known_w6081=dict(observation=dict(outcome="error",error=null_error,known_null_receipt=null_receipt),getter_calls=1),
            official_null=dict(observation=dict(outcome="returned-null",type=None,text=None),getter_calls=1),
            official_empty=dict(observation=dict(outcome="returned-string-empty",type="System.String",text=""),getter_calls=1),
            getvalue_dbnull=dict(observation=dict(outcome="null",type="System.DBNull",known_null_receipt=None),getter_calls=1),negative=null_negative),
        fixed_dml_observation=fixed,fixed_dml_after_close=copy.deepcopy(closed),raw_oracle_samples=raw_samples,raw_cache_mutation=raw_mutation)


class ProducerShapeContracts(unittest.TestCase):
    def test_v11_physical_capture_rejects_logical_fallback_and_override(self):
        mutations=(lambda c:c["captured_profile"].update(profile_origin="logical_conn_property"),
            lambda c:c["captured_profile"].pop("profile_origin"),
            lambda c:c["captured_profile"].update(negotiated_encoding_origin="raw_STARTUP"),
            lambda c:c["captured_profile"].update(encoding="gb18030",negotiated_encoding="gb18030"),
            lambda c:c["captured_profile"].update(encoding_override_empty=False),
            lambda c:c["captured_profile"].update(negotiated_encoding="gb18030"),
            lambda c:c["captured_profile"].update(message_version=True),
            lambda c:c["captured_profile"].update(timezone=-300),
            lambda c:c.update(logical_reads=1),lambda c:c["rejections"].pop(),
            lambda c:c["rejections"][0].update(capture_rejected=False))
        for mutation in mutations:
            shape=handwritten_shape()
            self.assertEqual("producer_shape_contract_verified",V.validate_producer_shape(shape)["status"])
            mutation(shape["physical_profile_capture"])
            self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
        shape=handwritten_shape();shape.pop("physical_profile_capture")
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
    def test_handwritten_serializer_shape_positive_is_not_database_acceptance(self):
        result=V.validate_producer_shape(handwritten_shape())
        self.assertEqual("producer_shape_contract_verified",result["status"],result)
        self.assertFalse(result["candidate_acceptance"])

    def test_v8_actual_raw_cache_shape_and_mutation_contracts(self):
        for mutate in (
            lambda s:s["raw_oracle_samples"][0]["raw_oracle"].update(source="formatter_reencoded"),
            lambda s:s["raw_oracle_samples"][1]["metadata"].update(mask=4),
            lambda s:s["raw_oracle_samples"][0]["raw_oracle"].update(payload_sha256="0"*64),
            lambda s:s["raw_cache_mutation"]["after"].update(synthetic_temporal_hex="EA87BBEE81C8000000"),
            lambda s:s["raw_oracle_samples"].pop(),
        ):
            shape=handwritten_shape();mutate(shape)
            self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])

    def test_v6_actual_NULL_getter_delegate_and_fixed_DML_shape_are_required(self):
        mutations=(
            lambda s:s["null_observations"]["known_w6081"].update(getter_calls=0),
            lambda s:s["null_observations"]["known_w6081"]["observation"].pop("known_null_receipt"),
            lambda s:s["null_observations"]["known_w6081"]["observation"]["error"].update(outcome="Unknown"),
            lambda s:s["null_observations"]["negative"][0].update(receipt_granted=True),
            lambda s:s["null_observations"]["negative"].pop(),
            lambda s:s["fixed_dml_observation"].update(statement_after_owner="command"),
            lambda s:s["fixed_dml_observation"].update(drain_completed=False),
            lambda s:s["fixed_dml_observation"].update(encoded_scale_observable=False),
        )
        for mutate in mutations:
            shape=handwritten_shape();mutate(shape)
            self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])

    def test_actual_fixed_target_helper_is_checked_against_independent_profile_table(self):
        shape=handwritten_shape();shape["fixed_targets"][2]["target_column"]="D7"
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
        shape=handwritten_shape();shape["fixed_targets"].pop()
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
        shape=handwritten_shape();shape["fixed_targets"][0]["tz"]=0
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])

    def test_actual_helper_boxing_and_kind_mutants_reject(self):
        for change in (dict(type="System.DateTimeOffset",offset_minutes=480,kind=None),dict(ticks=639204129161234561),dict(kind="Local")):
            shape=handwritten_shape();shape["samples"][0]["observation"].update(change)
            self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])

    def test_actual_result_flag_and_early_phase_mutants_reject(self):
        for key,value in (("type_flag",1),("raw_source","ParameterDescribe")):
            shape=handwritten_shape();shape["metadata"]["result_column"][key]=value
            self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
        shape=handwritten_shape();shape["rejections"][0].pop("execute_attempted")
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
        shape=handwritten_shape();shape["rejections"][0]["persistence_source"]="fresh_connection_row_count"
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])

    def test_live_reader_and_closed_stale_source_mutants_reject(self):
        for part,mutation in (("after_live",dict(statement_owner="command")),("after_live",dict(statement_id=502)),("after_close",dict(statement_owner="reader",statement_id=501))):
            shape=handwritten_shape();shape["statement_ownership"][part].update(mutation)
            self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])

    def test_synthetic_shape_cannot_claim_real_profile(self):
        shape=handwritten_shape();shape["database_accessed"]=True
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])
        shape=handwritten_shape();shape["expected_only"]=False
        self.assertEqual("producer_shape_contract_failed",V.validate_producer_shape(shape)["status"])


def consume(shape_path, output):
    if output.exists(): raise ValueError("shape_output_must_be_new")
    try:
        result=V.validate_producer_shape(V.C.load_json(shape_path))
    except Exception as error:
        result=dict(status="producer_shape_contract_failed",candidate_acceptance=False,issues=["shape_load_error:"+type(error).__name__])
    result["actual_csharp_shape_sha256"]=V.C.digest(shape_path)
    result["input_role"]="actual_CSharp_offline_serializer_output_expected_only"
    output.write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(dict(status=result["status"])))
    return 0 if result["status"]=="producer_shape_contract_verified" else 1


if __name__=="__main__":
    if "--shape" in sys.argv:
        import argparse
        parser=argparse.ArgumentParser(description=__doc__)
        parser.add_argument("--shape",type=Path,required=True)
        parser.add_argument("--output",type=Path,required=True)
        args=parser.parse_args();raise SystemExit(consume(args.shape,args.output))
    unittest.main()
