"""TP03-only expected artifact contracts; Low runs these after source freeze.
No processes, dotnet, connections or real DB evidence are created here.
"""
import copy
from datetime import datetime, timedelta
from decimal import Decimal
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
spec = importlib.util.spec_from_file_location("tp03_wiring_validator", HERE / "validate.py")
VALIDATOR = importlib.util.module_from_spec(spec)
spec.loader.exec_module(VALIDATOR)

def require_handwritten_nonce(nonce):
    """Independent fixture precondition; do not invoke or relax the production validator."""
    assert type(nonce) is str, "handwritten nonce must be a string"
    assert len(nonce) == 32, "handwritten nonce must contain exactly 32 characters"
    assert all(char in "0123456789ABCDEF" for char in nonce), "handwritten nonce must be uppercase hexadecimal"
    return nonce


MANUAL = json.loads((HERE / "contract-fixtures/handwritten-artifact-spec.json").read_text())
PACKET_LITERALS = json.loads((HERE / "contract-fixtures/packed_literal_cases.json").read_text())
require_handwritten_nonce(MANUAL["nonce"])
O = dict(id="DM.DmProvider", version="8.3.1.47463", asset="lib/net9.0/DM.DmProvider.dll",
    package_sha256="62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75",
    dll_sha256="8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b",
    mvid="d909b5af-965f-4cf9-a80a-a6181124e361")
SECOND, MINUTE, MAXIMUM = 10_000_000, 600_000_000, 3155378975999999999


def sha(data):
    return hashlib.sha256(data).hexdigest()


def calendar_ticks(y, m, d, h=0, minute=0, second=0):
    return ((datetime(y, m, d).toordinal() - 1) * 86400 + h * 3600 + minute * 60 + second) * SECOND


def clr(ticks, offset=None, kind="Unspecified"):
    return dict(outcome="returned", type="System.DateTimeOffset" if offset is not None else "System.DateTime",
                ticks=ticks, offset_minutes=offset, kind=None if offset is not None else kind, text=None, error=None)


def fields(ticks):
    days, within = divmod(ticks, 86400 * SECOND)
    date = datetime(1, 1, 1) + timedelta(days=days)
    hour, within = divmod(within, 3600 * SECOND)
    minute, within = divmod(within, 60 * SECOND)
    second, frac = divmod(within, SECOND)
    return (date.year, date.month, date.day, hour, minute, second, frac * 100)


def text(ticks, offset=None, digits=9):
    y, m, d, h, mi, s, ns = fields(ticks)
    result = f"{y:04}-{m:02}-{d:02} {h:02}:{mi:02}:{s:02}." + f"{ns:09}"[:digits]
    if offset is not None:
        result += f" {'-' if offset < 0 else '+'}{abs(offset)//60:02}:{abs(offset)%60:02}"
    return result


def error(name, category="user_rejection"):
    return dict(error=dict(type="System." + name, number=None, verified_server_response=False, failure_kind=None, phase=None, code=None, outcome=None), failure_class=category)


def meta(ctype, scale, fixed=None, tltz=False):
    return dict(available=True, c_type=ctype, semantic_scale=scale, width=MANUAL["metadata_widths"][str(ctype)],
        mask=4 if tltz else 0, type_flag=1 if fixed else 2 if fixed is False else 0,
        raw_scale_status="captured_server_source", raw_source="parameter_describe" if fixed is not None else "result_column",
        raw_c_type=2 if fixed is False else ctype, raw_precision=32767 if fixed is False else MANUAL["metadata_widths"][str(ctype)], raw_scale=0 if fixed is False else scale | (4096 if tltz else 0),
        raw_mask_before=0, raw_derived_mask=4 if tltz else 0, result_format="ordinary_packed", is_bdta=False, metadata_role="parameter_describe" if fixed is not None else "result_column")


def cell(ctype, scale):
    return dict(observable=True, payload_length={14: 3, 15: 5, 22: 7, 16: 8, 23: 10, 26: 9, 27: 11}[ctype],
        payload_sha256="eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
        result_format="ordinary_non_BDTA", is_bdta=False, source="already_fetched_cell")


def literal_raw_input(value, ctype, scale):
    literal=PACKET_LITERALS["packets"][f'{ctype}:{value["ticks"]}']
    if ctype in (23,27):literal += PACKET_LITERALS["offset_suffix_literals"][str(value["offset_minutes"])]
    data=bytes.fromhex(literal);digest=sha(data)
    return dict(evidence_role="independent_cached_temporal_packed_input",source="ordinary_result_cache_before_temporal_projection",capture_stage="before_any_temporal_getter",read_only_synthetic=True,
        observable=True,c_type=ctype,semantic_scale=scale,width=MANUAL["metadata_widths"][str(ctype)],mask=0,metadata_role="result_column",result_format="ordinary_non_BDTA",column_result_format="ordinary_packed",is_bdta=False,
        payload_length=len(data),payload_sha256=digest,synthetic_temporal_hex=literal,layout_family="TS_TZ10" if ctype==23 else "TS_TZ11" if ctype==27 else "TS8" if ctype==16 else "TS9")


def oracles(value, scale, readers):
    result = []
    for reader in readers:
        c = (23 if scale == 6 else 27) if value["offset_minutes"] is not None else (16 if scale == 6 else 26)
        raw = literal_raw_input(value, c, scale)
        observed = dict(reader_lane=reader, status="observed", metadata=[meta(c, scale)], schema_scales=[scale],
            cell=cell(c, scale), subsequent_read_completion=True, raw_oracle=raw)
        observed["cell"].update(payload_length=raw["payload_length"], payload_sha256=raw["payload_sha256"])
        ft = fields(value["ticks"])
        observed.update(server_ff9=text(value["ticks"], value["offset_minutes"]), server_ff7=text(value["ticks"], digits=7),
            server_exact=True, server_components_exact=True, server_ff7_exact=True, server_component_basis="local",
            server_components=[str(x) for x in ft[:5]] + [str(Decimal(ft[5]) + Decimal(ft[6]) / 1_000_000_000)],
            typed=clr(value["ticks"], value["offset_minutes"]), generic=clr(value["ticks"], value["offset_minutes"]),
            get_value=clr(value["ticks"], value["offset_minutes"]),
            get_string=dict(outcome="returned", type="System.String", text=text(value["ticks"], value["offset_minutes"], scale)))
        result.append(observed)
    return result


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



class HandmadeArtifact:
    """Synthetic independent enumeration, with explicit dummy package/DLL bytes.

    Expected outcomes come from this fixture, never from core_specs or validators.
    Copied raw literals were frozen independently of provider/core encoders.
    """
    def __init__(self, directory):
        self.directory = Path(directory)
        self.data = {}
        self.row = 10
        self.seven = True
        self.profile = "shared"
        self.driver_lanes = ["O", "W"]
        source = dict(schema_version=1, head=MANUAL["source_head"], files=[copy.deepcopy(MANUAL["source_file"])])
        self.put("frozen-input/product-source-manifest.json", source)
        archive = self.directory / "frozen-input/product-source.tar"
        archive.write_bytes(b"SYNTHETIC-SOURCE-ARCHIVE-NOT-REBUILD-EVIDENCE")
        w = dict(id="W.DmProvider", version=MANUAL["candidate_version"], asset="lib/net10.0/W.DmProvider.dll",
                 package_sha256=MANUAL["package_sha256"], dll_sha256=MANUAL["dll_sha256"], mvid=MANUAL["mvid"])
        self.identity = dict(schema_version=1, phase="candidate", comparison_head="e" * 40, O=copy.deepcopy(O), W=w,
            producer=dict(source_head=MANUAL["source_head"], source_manifest_sha256=self.hash("frozen-input/product-source-manifest.json"),
                source_archive_sha256=sha(archive.read_bytes()), **{k:w[k] for k in ("version", "package_sha256", "dll_sha256", "mvid")}))
        self.put("frozen-input/identities.json", self.identity)
        cap = dict(server_version="8.1.5.60", root_approved=True, evidence_id="SYNTHETIC-EXPECTED-ONLY",
            seven_write=True, write_scales=list(range(8)), tltz_read=False, tltz_write=False,
            local_ticks_min=0, local_ticks_max=MAXIMUM, utc_ticks_min=0, utc_ticks_max=MAXIMUM,
            offset_min_minutes=-779, offset_max_minutes=840)
        self.put("frozen-input/capabilities.json", dict(schema_version=1, source_commit=MANUAL["source_head"],
            source_manifest_sha256=self.identity["producer"]["source_manifest_sha256"], w_package_sha256=w["package_sha256"], profiles=dict(shared=cap)))
        libs = {e["id"] + "/" + e["version"]:dict(type="package") for e in (O,w)}
        runtime = {e["id"] + "/" + e["version"]:dict(runtime={e["asset"]:{}}) for e in (O,w)}
        for path in ("build/obj/project.assets.json", "build/bin/Debug/net10.0/TemporalBoundaryProbe.deps.json"):
            self.put(path, dict(libraries=copy.deepcopy(libs), targets={"net10.0":copy.deepcopy(runtime)}))
        self.put("build/packages.lock.json", dict(dependencies={"net10.0":{e["id"]:dict(resolved=e["version"]) for e in (O,w)}}))
        self.binary("build/bin/Debug/net10.0/TemporalBoundaryProbe.dll", b"SYNTHETIC-TOOL-NOT-A-DLL")
        for e in (O,w):
            self.binary("frozen-input/feed/" + (e["id"] + "." + e["version"] + ".nupkg").lower(), b"SYNTHETIC-PACKAGE-NOT-REAL")
            self.binary("build/bin/Debug/net10.0/" + Path(e["asset"]).name, b"SYNTHETIC-LOADED-DLL-NOT-REAL")
        for target, origin in (("build/contracts.py", "tools/TemporalBoundaryProbe/contracts.py"),
                               ("build/packed_oracle.py", "tools/TemporalBoundaryProbe/packed_oracle.py"),
                               ("frozen-input/temporal-precision.validate.py", "eng/temporal-precision.validate.py")):
            self.binary(target, (ROOT / origin).read_bytes())
        self.frozen = dict(schema_version=1, phase="candidate", source_commit=MANUAL["source_head"],
            comparison_head=self.identity["comparison_head"], producer=copy.deepcopy(self.identity["producer"]),
            source_archive_sha256=self.identity["producer"]["source_archive_sha256"],
            identity_sha256=self.hash("frozen-input/identities.json"), product_source_manifest_sha256=self.hash("frozen-input/product-source-manifest.json"),
            capabilities_sha256=self.hash("frozen-input/capabilities.json"),
            tools=[dict(path=p,sha256=self.hash(p)) for p in VALIDATOR.PINS], packages={d:self.identity[d]["package_sha256"] for d in ("O","W")})
        self.put("frozen-input/manifest.json", self.frozen)
        self.put("offline-producer-shape.json", handwritten_shape())
        self.put("offline-contracts.json", dict(status="offline_contracts_passed", expected_only=True, database_accessed=False,
            shape_sha256=self.hash("offline-producer-shape.json"), cases=[dict(case_id="synthetic_contract_shape",passed=True)]))
        self.runner = dict(schema_version=1, phase="candidate", profile="shared", status="execution_complete", runs=[],
            restore_exit_code=0, build_exit_code=0, offline_exit_code=0, offline_deadline=False,
            offline_report_sha256=self.hash("offline-contracts.json"), offline_shape_sha256=self.hash("offline-producer-shape.json"),
            identity_sha256=self.frozen["identity_sha256"], product_source_manifest_sha256=self.frozen["product_source_manifest_sha256"],
            capabilities_sha256=self.frozen["capabilities_sha256"], source_manifest_sha256=self.hash("frozen-input/manifest.json"),
            assets_sha256=self.hash("build/obj/project.assets.json"), lock_sha256=self.hash("build/packages.lock.json"),
            tool_dll_sha256=self.hash("build/bin/Debug/net10.0/TemporalBoundaryProbe.dll"))
        self.core()
        self.put("core.json", dict(self.base_report(),mode="run",status="characterization_complete",cases=self.data["core.cases"],completed_cases=len(self.data["core.cases"])))
        self.put("core.cleanup.json", dict(self.base_report(),mode="cleanup"))
        self.put("core.ownership.json", dict(schema_version=1,nonce=MANUAL["nonce"],table="TPN_"+MANUAL["nonce"][:24],owner_marker="OWN_"+MANUAL["nonce"][:24],
            profile="shared",server_version="8.1.5.60",owner_package_sha256=w["package_sha256"],identity_sha256=self.runner["identity_sha256"],
            identity_verified=True,absent_before_create=True,creation_state="confirmed"))
        self.runner["runs"] = [dict(lane="core",exit_code=0,hard_deadline=False,cleanup_exit_code=0,cleanup_hard_deadline=False,
            ownership_manifest_present=True,recovery_manifest="core.ownership.json",creation_state="confirmed",
            ownership_manifest_sha256=self.hash("core.ownership.json"),report_sha256=self.hash("core.json"))]
        self.put("runner.json",self.runner)

    def hash(self,path): return sha((self.directory / path).read_bytes())
    def binary(self,path,data):
        target=self.directory / path;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
    def put(self,path,value):
        self.binary(path,(json.dumps(value,indent=2)+"\n").encode());self.data[path]=value
    def row_id(self): self.row += 1;return self.row
    def record(self,lane,driver,key,payload):
        payload.update(driver_lane=driver,case_id=key,category="synthetic_expected_only")
        self.data.setdefault(lane+".cases",[]).append(payload)
        return payload
    def write(self,lane,driver,key,value,scale):
        ctype=(23 if scale==6 else 27) if value["offset_minutes"] is not None else (16 if scale==6 else 26)
        payload=dict(expected=value,requested_scale=scale if driver=="W" else None,requested_precision=None if driver=="W" else scale,
            requested_db_type="DateTimeOffset" if value["offset_minutes"] is not None else "DateTime2",
            write_path="fixed_column",target_column=("Z" if value["offset_minutes"] is not None else "D")+str(scale),
            column_scale=scale,column_metadata=[meta(ctype,scale)],persisted_rows=1,persistence_source="fresh_connection_row_count",
            affected_rows=1,execute_attempted=True,row_id=self.row_id(),status="written")
        fail=driver=="W" and ((value["offset_minutes"] is not None and not -779<=value["offset_minutes"]<=840) or value["ticks"] % 10**(7-scale)!=0)
        if fail:payload.update(status="driver_write_rejected",persisted_rows=0,**error("OverflowException"))
        else:payload["oracles"]=oracles(value,scale,["O","W"])
        return self.record(lane,driver,key,payload)
    def core(self):
        ordinary = MANUAL["ordinary_ticks"]
        vectors = dict(min=0, min_plus1=1, max=MAXIMUM, max_minus1=MAXIMUM-1, max_minus9=MAXIMUM-9,
            ordinary_tick1=ordinary+1, ordinary_micro=ordinary+1234560,
            century_leap=calendar_ticks(2000,2,29,23,59,59)+9999999,
            century_nonleap=calendar_ticks(1900,3,1), midnight=calendar_ticks(2026,7,24))
        vectors.update({"tail_"+str(i): ordinary+i for i in range(10)})
        for driver in self.driver_lanes:
            columns = [meta(c,s) for c,s in zip((16,23,26,27),(6,6,7,7))]
            self.record("core", driver, "columns_6_7", dict(status="observed", metadata=columns, schema_scales=[6,6,7,7]))
            for col, metadata, scale in zip(("D6","Z6","D7","Z7"), columns, (6,6,7,7)):
                self.record("core",driver,"column_"+col,dict(status="observed",metadata=[metadata],schema_scales=[scale]))
            for key,ticks in vectors.items():
                for family,offset in (("dt",None),("dto",0)):
                    for scale in (6,7): self.write("core",driver,f"{family}{scale}_{key}",clr(ticks,offset),scale)
            for kind in ("Unspecified","Utc","Local"):
                self.write("core",driver,"kind_"+kind,clr(ordinary+1,kind=kind),7)
            for offset in (0,480,-300,330,-840,-780,-779,840):
                suffix=str(offset).replace("-","N")
                self.write("core",driver,"offset_"+suffix,clr(ordinary+1,offset),7)
                for endpoint,ticks in (("min",0),("max",MAXIMUM)):
                    for basis,local in (("local",ticks),("utc",ticks+offset*MINUTE)):
                        utc=local-offset*MINUTE
                        valid=0<=local<=MAXIMUM and 0<=utc<=MAXIMUM
                        key=(basis+"_" if valid else "constructor_" if basis=="local" else "utc_constructor_")+endpoint+"_"+suffix
                        if valid: self.write("core",driver,key,clr(local,offset),7)
                        else:
                            self.record("core",driver,key,dict(status="constructor_rejected_before_driver",execute_attempted=False,
                                expected_constructor_rejection=True,requested_fields=dict(local_ticks=local,utc_ticks=utc,offset_minutes=offset),
                                **error("ArgumentOutOfRangeException","clr_precondition")))
            self.record("core",driver,"typed_dml_rollback",dict(status="rollback_verified",fresh_rows=0))


    def base_report(self):
        profile=copy.deepcopy(MANUAL["shared_physical_profile"])
        return dict(schema_version=1,phase="candidate",profile="shared",lane="core",process_exit_code=0,
            producer_source_head=self.identity["producer"]["source_head"],comparison_head=self.identity["comparison_head"],
            **{k:self.runner[k] for k in ("identity_sha256","source_manifest_sha256","product_source_manifest_sha256")},
            server_identity_verified=True,cleanup_verified=True,final_database_state="owned_table_absent_fresh_connection",owned_table="TPN_"+MANUAL["nonce"][:24],
            W_profile=copy.deepcopy(profile),O_profile=copy.deepcopy(profile),
            identities={d:dict(self.identity[d],package_reference_verified=True,actual_asset_verified=True) for d in ("O","W")})
    def case(self,key,driver="W"):
        return next(c for c in self.data["core.cases"] if c["case_id"]==key and c["driver_lane"]==driver)
    def flush(self):
        self.data["core.json"]["completed_cases"]=len(self.data["core.json"]["cases"])
        for name,value in list(self.data.items()):
            if name.endswith(".json"):self.put(name,value)
        self.data["offline-contracts.json"]["shape_sha256"] = self.hash("offline-producer-shape.json")
        self.put("offline-contracts.json", self.data["offline-contracts.json"])
        self.runner.update(assets_sha256=self.hash("build/obj/project.assets.json"), lock_sha256=self.hash("build/packages.lock.json"),
            offline_shape_sha256=self.hash("offline-producer-shape.json"), offline_report_sha256=self.hash("offline-contracts.json"))
        self.runner["runs"][0].update(report_sha256=self.hash("core.json"),ownership_manifest_sha256=self.hash("core.ownership.json"))
        self.put("runner.json",self.runner)
    def rebind(self):
        for name in ("frozen-input/identities.json", "frozen-input/product-source-manifest.json", "frozen-input/capabilities.json"):
            self.put(name, self.data[name])
        self.frozen.update(identity_sha256=self.hash("frozen-input/identities.json"),product_source_manifest_sha256=self.hash("frozen-input/product-source-manifest.json"),capabilities_sha256=self.hash("frozen-input/capabilities.json"))
        self.put("frozen-input/manifest.json",self.frozen)
        self.runner.update(**{k:self.frozen[k] for k in ("identity_sha256","product_source_manifest_sha256","capabilities_sha256")},source_manifest_sha256=self.hash("frozen-input/manifest.json"))
        for name in ("core.json","core.cleanup.json"):
            self.data[name].update(**{k:self.runner[k] for k in ("identity_sha256","source_manifest_sha256","product_source_manifest_sha256")})
        self.data["core.ownership.json"]["identity_sha256"]=self.runner["identity_sha256"]
        self.flush()


def synthetic_inspector(package,identity,dll):
    if package.read_bytes()!=b"SYNTHETIC-PACKAGE-NOT-REAL" or dll.read_bytes()!=b"SYNTHETIC-LOADED-DLL-NOT-REAL":
        raise ValueError("synthetic_fixture_marker")
    return {k:identity[k] for k in ("package_sha256","dll_sha256","mvid")}


class WiringContracts(unittest.TestCase):
    def fresh(self):
        directory=tempfile.TemporaryDirectory(prefix="tp03-contract-");self.addCleanup(directory.cleanup)
        return HandmadeArtifact(directory.name)
    def accepted(self,a):
        result=VALIDATOR.validate(a.directory,_package_inspector=synthetic_inspector)
        self.assertEqual("synthetic_wiring_policy_verified",result["status"],result)
        self.assertTrue(result["synthetic_expected_only"])
        for flag in ("tp03_wiring_accepted","candidate_acceptance","full_temporal_acceptance","tp04_binding_acceptance","tp05_complete"):
            self.assertFalse(result[flag],result)
    def rejected(self,a):
        result=VALIDATOR.validate(a.directory,_package_inspector=synthetic_inspector)
        self.assertEqual("validation_failed",result["status"],result)
        self.assertFalse(result["tp03_wiring_accepted"])
    def mutate(self,mutation,rebind=False):
        a=self.fresh();self.accepted(a)  # A negative result cannot hide a broken normal fixture.
        mutation(a)
        if rebind:a.rebind()
        else:a.flush()
        self.rejected(a)
    def test_normal_expected_only_and_production_rejects_dummy_package(self):
        a=self.fresh();self.accepted(a)
        result=VALIDATOR.validate(a.directory)
        self.assertEqual("validation_failed",result["status"],result)
        self.assertFalse(result["tp03_wiring_accepted"])
    def test_missing_required_cases_and_duplicate(self):
        for key,driver in (("dt7_max","W"),("dto7_max_minus1","W"),("column_D7","W"),("offset_480","O"),("typed_dml_rollback","W")):
            with self.subTest(key=key):self.mutate(lambda a:a.data["core.cases"].remove(a.case(key,driver)))
        self.mutate(lambda a:a.data["core.cases"].append(copy.deepcopy(a.case("dt7_min"))))
    def test_tp04_raw_server_provenance_is_the_only_omitted_metadata_gate(self):
        a=self.fresh();self.accepted(a)
        provenance=("raw_scale_status","raw_source","raw_c_type","raw_precision","raw_scale","raw_mask_before","raw_derived_mask")
        for case in a.data["core.cases"]:
            entries=list(case.get("metadata",[]))+list(case.get("column_metadata",[]))
            for observed in case.get("oracles",[]):entries.extend(observed.get("metadata",[]))
            for entry in entries:
                for field in provenance:entry.pop(field,None)
        a.flush();self.accepted(a)
        a.case("dt7_ordinary_tick1")["oracles"][1].pop("raw_oracle")
        a.flush();self.rejected(a)
    def test_mandatory_success_never_becomes_baseline_rejection(self):
        for key in ("dt7_max","dto7_max_minus1","dto7_max_minus9","dt7_ordinary_tick1"):
            self.mutate(lambda a:a.case(key).update(status="driver_write_rejected",persisted_rows=0,**error("NotSupportedException")))
        for name in ("NotSupportedException","InvalidOperationException","TimeoutException"):
            self.mutate(lambda a:a.case("dt6_ordinary_tick1").update(**error(name)))
        self.mutate(lambda a:a.case("offset_N840").update(**error("NotSupportedException")))
    def test_domains_and_package_source_approval_cannot_be_rebound(self):
        for key,value in (("local_ticks_min",1),("local_ticks_max",MAXIMUM-1),("utc_ticks_min",1),("utc_ticks_max",MAXIMUM-1),("offset_min_minutes",-778),("offset_max_minutes",839),("write_scales",[6,7]),("write_scales",list(range(9))),("seven_write",False),("root_approved",False)):
            with self.subTest(key=key):self.mutate(lambda a:a.data["frozen-input/capabilities.json"]["profiles"]["shared"].update({key:value}),True)
        self.mutate(lambda a:a.identity["W"].update(package_sha256="f"*64),True)
        self.mutate(lambda a:a.identity["producer"].update(source_head="f"*40),True)
        self.mutate(lambda a:a.data["frozen-input/capabilities.json"].update(source_manifest_sha256="f"*64),True)
        self.mutate(lambda a:a.frozen.update(source_archive_sha256=None),True)
        self.mutate(lambda a:a.binary("frozen-input/product-source.tar",b"mutated-source"))
    def test_raw_and_public_oracle_negative_matrix(self):
        changes=(lambda r:r.pop("raw_oracle"),lambda r:r["raw_oracle"].update(payload_sha256="0"*64),
            lambda r:r["raw_oracle"].update(synthetic_temporal_hex="EA87BBEE81CA000000"),
            lambda r:r["raw_oracle"].update(source="formatter_reencoded"),lambda r:r["metadata"][0].update(mask=4),
            lambda r:r["cell"].update(payload_length=12),lambda r:r["cell"].update(observable=False),
            lambda r:r["cell"].update(is_bdta=True),lambda r:r["typed"].update(ticks=1),
            lambda r:r["generic"].update(ticks=1),lambda r:r["get_value"].update(ticks=1),
            lambda r:r["get_string"].update(text="2026-07-23 14:15:16.0000000"),
            lambda r:r.update(server_ff9="2026-07-23 14:15:16.000000200"),
            lambda r:r.update(server_components_exact=False),lambda r:r.update(subsequent_read_completion=False))
        for change in changes:
            with self.subTest(change=change):self.mutate(lambda a:change(a.case("dt7_ordinary_tick1")["oracles"][1]))
        self.mutate(lambda a:a.case("offset_480")["oracles"][1]["get_value"].update(offset_minutes=0))
        self.mutate(lambda a:a.case("dt7_ordinary_tick1","O")["oracles"][0]["cell"].update(payload_length=12))
    def test_O_target_metadata_and_schema_are_independent_of_raw_self_report(self):
        def changed_scale(a):
            row=a.case("dt7_ordinary_tick1")["oracles"][0]
            row["metadata"][0]["semantic_scale"]=8
            row["raw_oracle"]["semantic_scale"]=8
        self.mutate(changed_scale)
        for field,value in (("type_flag",1),("c_type",27),("width",11)):
            with self.subTest(field=field):
                self.mutate(lambda a:a.case("dt7_ordinary_tick1")["oracles"][0]["metadata"][0].update({field:value}))
        self.mutate(lambda a:a.case("dt7_ordinary_tick1")["oracles"][0].update(schema_scales=[8]))
        self.mutate(lambda a:a.case("offset_480","O")["oracles"][0].update(schema_scales=[]))
    def test_candidate_archive_cannot_be_absent_null_or_mismatched(self):
        self.mutate(lambda a:(a.directory / "frozen-input/product-source.tar").unlink(),True)
        def null_hashes(a):
            a.identity["producer"]["source_archive_sha256"]=None
            a.frozen.update(source_archive_sha256=None,producer=copy.deepcopy(a.identity["producer"]))
        self.mutate(null_hashes,True)
        def null_archive(a):
            (a.directory / "frozen-input/product-source.tar").unlink()
            null_hashes(a)
        self.mutate(null_archive,True)
        def mismatched_archive(a):
            a.identity["producer"]["source_archive_sha256"]="f"*64
            a.frozen.update(source_archive_sha256="f"*64,producer=copy.deepcopy(a.identity["producer"]))
        self.mutate(mismatched_archive,True)
    def test_receipts_cleanup_profile_and_package_assets(self):
        for change in (lambda a:a.runner["runs"].append(copy.deepcopy(a.runner["runs"][0])),
            lambda a:a.runner["runs"][0].update(cleanup_exit_code=1),lambda a:a.runner["runs"][0].update(hard_deadline=True),
            lambda a:a.runner["runs"][0].update(ownership_manifest_present=False),
            lambda a:a.data["core.cleanup.json"].update(cleanup_verified=False),
            lambda a:a.data["core.cleanup.json"].update(final_database_state="unverified"),
            lambda a:a.data["core.ownership.json"].update(nonce="A"*32),
            lambda a:a.data["core.ownership.json"].update(owner_marker="foreign"),
            lambda a:a.data["core.json"]["identities"]["W"].update(actual_asset_verified=False),
            lambda a:a.data["core.json"]["W_profile"].update(message_version=20),
            lambda a:a.data["core.json"]["W_profile"].update(encoding="gb18030"),
            lambda a:a.data["core.json"]["W_profile"].update(server_user="OTHER"),
            lambda a:a.case("typed_dml_rollback").update(fresh_rows=1),
            lambda a:a.case("dt7_min").update(affected_rows=0),lambda a:a.case("dt7_min").update(persistence_source="assumed")):
            self.mutate(change)
        self.mutate(lambda a:a.data["build/obj/project.assets.json"]["libraries"][O["id"]+"/"+O["version"]].update(type="project"))
        self.mutate(lambda a:a.data["offline-producer-shape.json"]["samples"].pop())
        self.mutate(lambda a:a.runner.update(offline_exit_code=1))
    def test_v4_shared_physical_scope_rejects_logical_GB_and_rebound_drift(self):
        changes=(lambda p:p.update(profile_origin="logical_conn_property"),lambda p:p.pop("profile_origin"),
            lambda p:p.update(negotiated_encoding_origin="raw_STARTUP"),lambda p:p.pop("negotiated_encoding_origin"),
            lambda p:p.update(encoding="gb18030"),lambda p:p.update(negotiated_encoding="gb18030"),
            lambda p:p.update(encoding_override_empty=False),lambda p:p.pop("encoding_override_empty"),
            lambda p:p.update(encoding="gb18030",negotiated_encoding="gb18030",message_version=21),
            lambda p:p.update(message_version=11))
        for name,driver in (("core.json","W"),("core.cleanup.json","W"),("core.json","O")):
            for change in changes:
                with self.subTest(name=name,driver=driver):
                    self.mutate(lambda a:change(a.data[name][driver+"_profile"]))
        self.mutate(lambda a:a.data["offline-producer-shape.json"]["physical_profile_capture"]["captured_profile"].update(profile_origin="logical_conn_property"))
        self.mutate(lambda a:a.data["offline-producer-shape.json"]["physical_profile_capture"].update(logical_reads=1))
    def test_policy_and_hash_receipt_mutations(self):
        a=self.fresh();self.accepted(a)
        a.binary("build/contracts.py",b"raise RuntimeError('not to be executed')")
        self.rejected(a)
        a=self.fresh();self.accepted(a)
        a.runner["runs"][0]["report_sha256"]="0"*64;a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();self.accepted(a)
        a.runner["runs"][0]["ownership_manifest_sha256"]="0"*64;a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();self.accepted(a)
        a.runner.update(profile="tls");a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();self.accepted(a)
        a.binary("tools/TemporalBoundaryProbe/contracts.py",b"raise RuntimeError('shadow')");self.rejected(a)


if __name__=="__main__":unittest.main()
