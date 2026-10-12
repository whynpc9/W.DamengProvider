"""Offline handwritten full-artifact contracts; no dotnet, network, wrapper or DB.

Run only in the Low validation window:
  PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/TemporalBoundaryProbe -p test_validator_contracts.py -v
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
from unittest.mock import patch
import contextlib
import io
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
spec = importlib.util.spec_from_file_location("temporal_validator", ROOT / "eng/temporal-precision.validate.py")
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
DECLS = ("inferred", "inferred_dto", "DateTime", "DateTime2", "DateTimeOffset", "provider", "provider_dto")


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


def before_binding(after):
    # Independent server generic describe is preserved although selected wire has changed.
    before=copy.deepcopy(after)
    before.update(c_type=2,width=32767,semantic_scale=0)
    return before


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


def known_null_error(reader, getter):
    original=dict(type="W.Dm.DmException" if reader=="W" else "Dm.DmException",number=6081,verified_server_response=False,
        failure_class="infra_failure" if reader=="W" else "unverified_driver_error",
        failure_kind="Transport" if reader=="W" else None,phase="Fetch" if reader=="W" else None,
        code="WDM_TRANSPORT" if reader=="W" else None,outcome="NotSent" if reader=="W" else None)
    receipt=dict(observation_class="known_typed_null_rejection",expected_dbnull=True,reader_lane=reader,ordinal=0,getter_name=getter,
        value_is_dbnull=True,server_format_is_dbnull=True,cache_observable=True,
        cached_cell=dict(is_null=True,payload_length=None,payload_sha256=None),actual_getter_invoked=True,
        provider_exception_type=original["type"],number=6081,has_real_infra_inner=False,verified_server_response=False,
        cancel_source="None",transaction_outcome=None,info_present=reader=="W",server_error_number=None)
    return dict(outcome="error",error=original,known_null_receipt=receipt)


def oracles(value, scale, readers, ctype=None, null=False):
    result = []
    for reader in readers:
        c = ctype or ((23 if scale <= 6 else 27) if value["offset_minutes"] is not None else (16 if scale <= 6 else 26))
        observed = dict(reader_lane=reader, status="observed", metadata=[meta(c, scale)], schema_scales=[scale],
            cell=cell(c, scale), subsequent_read_completion=True)
        if not null and value["offset_minutes"] is not None:
            observed["raw_oracle"]=literal_raw_input(value,c,scale)
            observed["cell"]["payload_length"]=observed["raw_oracle"]["payload_length"]
            observed["cell"]["payload_sha256"]=observed["raw_oracle"]["payload_sha256"]
        if null:
            observed["cell"].update(is_null=True,payload_length=None,payload_sha256=None)
            observed.update(get_value=dict(outcome="null", type="System.DBNull"),
                null_context=dict(expected_dbnull=True,reader_lane=reader,ordinal=0,value_is_dbnull=True,server_format_is_dbnull=True),
                typed=known_null_error(reader,"GetDateTimeOffset" if value["offset_minutes"] is not None else "GetDateTime"),
                generic=known_null_error(reader,"GetFieldValue<DateTimeOffset>" if value["offset_minutes"] is not None else "GetFieldValue<DateTime>"),
                get_string=known_null_error(reader,"GetString"))
        else:
            ft = fields(value["ticks"])
            observed.update(server_ff9=text(value["ticks"], value["offset_minutes"]), server_ff7=text(value["ticks"], digits=7),
                server_exact=True, server_components_exact=True, server_ff7_exact=True, server_component_basis="local",
                server_components=[str(x) for x in ft[:5]] + [str(Decimal(ft[5]) + Decimal(ft[6]) / 1_000_000_000)],
                typed=clr(value["ticks"], value["offset_minutes"]), generic=clr(value["ticks"], value["offset_minutes"]),
                get_value=clr(value["ticks"], value["offset_minutes"]),
                get_string=dict(outcome="returned", type="System.String", text=text(value["ticks"], value["offset_minutes"], scale)))
        result.append(observed)
    return result


class HandmadeArtifact:
    """Independent synthetic report writer. Never calls validation policy helpers."""
    def __init__(self, directory, phase="candidate", profile="shared", scale8=True, legacy_column=True):
        require_handwritten_nonce(MANUAL["nonce"])
        self.directory, self.phase, self.profile = Path(directory), phase, profile
        self.seven = profile == "shared"
        self.scales = list(range(8 if self.seven else 7)) + ([8] if scale8 else [])
        self.legacy_column = legacy_column
        self.driver_lanes = ["O", "W"] if self.seven else ["W"]
        self.row = 10
        self.data = {}
        source = dict(schema_version=1, head=MANUAL["source_head"], files=[copy.deepcopy(MANUAL["source_file"])])
        self.put("frozen-input/product-source-manifest.json", source)
        w = dict(id="W.DmProvider", version=MANUAL["candidate_version" if phase == "candidate" else "baseline_version"],
            asset="lib/net10.0/W.DmProvider.dll", package_sha256=MANUAL["package_sha256"], dll_sha256=MANUAL["dll_sha256"], mvid=MANUAL["mvid"])
        identity = dict(schema_version=1, phase=phase, O=O, W=w, producer=dict(source_head=MANUAL["source_head"],
            source_manifest_sha256=self.hash("frozen-input/product-source-manifest.json"), source_archive_sha256=None,
            **{k: w[k] for k in ("version", "package_sha256", "dll_sha256", "mvid")}))
        if phase == "baseline": identity["baseline_kind"] = "rebuilt_baseline"
        self.put("frozen-input/identities.json", identity)
        self.identity = identity
        server_version = MANUAL[profile + "_server_version"]
        unsupported_ids = ["calendar_1582_10_" + str(d) for d in (4, 5, 10, 14, 15)] + ["bce", "year_zero"]
        unsupported_ids += [p + str(s) for p in ("time24_scale", "time_fraction_scale") for s in (6, 7, 8, 9)]
        selected = dict(server_version=server_version, root_approved=True, evidence_id="offline-contract-only-not-real-evidence",
            seven_write=self.seven, write_scales=self.scales, tltz_read=False, tltz_write=False,
            local_ticks_min=0, local_ticks_max=MAXIMUM, utc_ticks_min=0, utc_ticks_max=MAXIMUM,
            offset_min_minutes=-779, offset_max_minutes=840,
            verified_unsupported_error_numbers={key: [-6118] for key in unsupported_ids})
        if phase == "candidate":
            self.put("frozen-input/capabilities.json", dict(schema_version=1, source_commit=MANUAL["source_head"],
                source_manifest_sha256=identity["producer"]["source_manifest_sha256"], w_package_sha256=w["package_sha256"], profiles={profile: selected}))
        # These dummy assets deliberately cannot be mistaken for a build or actual driver package.
        self.put("build/obj/project.assets.json", {"offline_fixture": True})
        self.put("build/packages.lock.json", {"offline_fixture": True})
        binary = self.directory / "build/bin/Debug/net10.0/TemporalBoundaryProbe.dll"
        binary.parent.mkdir(parents=True, exist_ok=True); binary.write_bytes(b"OFFLINE-CONTRACT-NOT-A-DLL")
        policy = self.directory / "build/contracts.py"; policy.write_bytes((HERE / "contracts.py").read_bytes())
        (self.directory / "build/packed_oracle.py").write_bytes((HERE / "packed_oracle.py").read_bytes())
        validator = self.directory / "frozen-input/temporal-precision.validate.py"; validator.write_bytes((ROOT / "eng/temporal-precision.validate.py").read_bytes())
        frozen = dict(schema_version=1, phase=phase, identity_sha256=self.hash("frozen-input/identities.json"),
            product_source_manifest_sha256=self.hash("frozen-input/product-source-manifest.json"),
            capabilities_sha256=self.hash("frozen-input/capabilities.json") if phase == "candidate" else None,
            tools=[dict(path=path, sha256=self.hash(path)) for path in ("build/contracts.py", "build/packed_oracle.py", "frozen-input/temporal-precision.validate.py")], packages={"O": O["package_sha256"], "W": w["package_sha256"]})
        self.put("frozen-input/manifest.json", frozen)
        self.frozen = frozen
        self.runner = dict(schema_version=1, task="TP-01", phase=phase, profile=profile, status="execution_complete",
            identity_sha256=frozen["identity_sha256"], source_manifest_sha256=self.hash("frozen-input/manifest.json"),
            product_source_manifest_sha256=frozen["product_source_manifest_sha256"], capabilities_sha256=frozen["capabilities_sha256"],
            restore_exit_code=0, build_exit_code=0, assets_sha256=self.hash("build/obj/project.assets.json"),
            lock_sha256=self.hash("build/packages.lock.json"), tool_dll_sha256=self.hash("build/bin/Debug/net10.0/TemporalBoundaryProbe.dll"), runs=[])
        self.core(); self.metadata(); self.extended()
        self.flush()

    def hash(self, path): return sha((self.directory / path).read_bytes())
    def put(self, path, value):
        target = self.directory / path; target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(value, indent=2) + "\n"); self.data[path] = value

    def row_id(self): self.row += 1; return self.row

    def record(self, lane, driver, key, payload):
        payload.update(driver_lane=driver, case_id=key, category="offline_contract")
        self.data.setdefault(lane + ".cases", []).append(payload)
        return payload

    def write(self, lane, driver, key, value, scale, forced=None, ctype=None):
        payload = dict(expected=value, requested_scale=scale if driver=="W" else None,
            requested_precision=None if driver=="W" else scale, requested_db_type="DateTimeOffset" if value["offset_minutes"] is not None else "DateTime2",
            column_scale=scale, persisted_rows=1, row_id=self.row_id(), status="written")
        fail = forced
        if driver == "W":
            fail = fail or ("NotSupportedException" if scale not in self.scales else None)
            if fail is None and value["offset_minutes"] is not None and not -779 <= value["offset_minutes"] <= 840: fail = "OverflowException"
            if fail is None and value["ticks"] % 10 ** max(0, 7 - scale): fail = "OverflowException"
        if fail:
            payload.update(status="driver_write_rejected", persisted_rows=0, **error(fail))
            if self.profile=="tls" and scale==7 and lane=="core":
                payload.update(write_path="advisory_profile_request",column_scale=None,column_metadata=[],persistence_source="advisory_no_dml")
        else:
            payload["oracles"] = oracles(value, scale, self.driver_lanes, ctype)
        return self.record(lane, driver, key, payload)

    def core(self):
        ordinary = MANUAL["ordinary_ticks"]
        vectors = dict(min=0, min_plus1=1, max=MAXIMUM, max_minus1=MAXIMUM-1, max_minus9=MAXIMUM-9,
            ordinary_tick1=ordinary+1, ordinary_micro=ordinary+1234560,
            century_leap=calendar_ticks(2000,2,29,23,59,59)+9999999,
            century_nonleap=calendar_ticks(1900,3,1), midnight=calendar_ticks(2026,7,24))
        vectors.update({"tail_"+str(i): ordinary+i for i in range(10)})
        if not self.seven:
            for column in ("D7","Z7"):
                self.record("core","server","main_ddl_"+column,dict(status="failed",classification="server_response_observed",failure_class="verified_server_response",error=dict(type="W.Dm.DmException",number=-6118,verified_server_response=True,failure_kind="Server",phase="Receive",code=None,outcome="ServerReported")))
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

    def axes(self):
        # This independently handwritten enumeration does not call the production contract generator.
        axes={(s,None,False,False) for s in (None,0,3,6,7,8,9,19,23,255)}
        axes.update((None,p,False,False) for p in (0,7,9))
        axes.update((7,None,f,n) for f,n in ((False,True),(True,False),(True,True)))
        axes.update((None,p,True,True) for p in (0,7,9))
        axes.update((None,p,f,n) for p in (7,9) for f,n in ((True,False),(False,True)))
        return axes

    def binding(self, driver, declaration, scale, precision, fixed, null):
        key=f"bind_{declaration}_s{scale if scale is not None else 'unset'}_p{precision if precision is not None else 'unset'}_{'fixed' if fixed else 'advisory'}_{'null' if null else 'value'}"
        inferred=declaration.startswith("inferred"); tz=declaration in ("inferred_dto","DateTimeOffset","provider_dto")
        source="fixed_describe" if inferred and null and fixed else "untyped_null" if inferred and null else "inferred" if inferred else "provider" if declaration.startswith("provider") else "user_db_type"
        payload=dict(declaration=declaration,requested_scale=scale,requested_precision=precision,typed_null=null,
            fixed_describe=fixed,type_source=source,status="observed",persisted_rows=0, target_column=(("Z" if tz else "D")+("7" if self.profile=="shared" else "6")) if fixed else None)
        if fixed: payload["column_metadata"]=[meta((27 if tz else 26) if self.seven else (23 if tz else 16),7 if self.seven else 6)]
        chosen=(7 if self.seven else 6) if fixed else scale
        if not fixed and chosen is None:
            chosen=7 if self.seven and (inferred or declaration=="DateTime2") else 6
        requested=chosen if fixed and scale is None else scale if scale is not None else chosen
        failed=None
        if inferred and null and not fixed: failed="InvalidOperationException"
        elif scale is not None and scale>9: failed="ArgumentOutOfRangeException"
        elif precision not in (None,0): failed="NotSupportedException"
        elif not fixed and declaration=="DateTime2" and scale is None and not self.seven: failed="NotSupportedException"
        elif requested not in self.scales: failed="NotSupportedException"
        elif not null and 1234560 % 10**max(0,7-requested): failed="OverflowException"
        # Baseline can record a known legacy W rejection instead of pretending candidate semantics.
        if self.phase=="baseline" and driver=="W" and (inferred or declaration=="DateTime2") and scale in (None,7): failed="NotSupportedException"
        if driver=="O": failed=None
        if failed:
            payload.pop("persisted_rows",None)
            details=error(failed)
            phase=dict(phase="initial",status="driver_parameter_rejected",target_column=payload["target_column"],row_id=None,persisted_rows=None,
                execute_attempted=False,persistence_source="not_executed",parameter_metadata=[],parameter_metadata_before_execute=[],oracles=[],**copy.deepcopy(details))
            payload.update(status="driver_parameter_rejected",phase_results=[phase],**details)
        else:
            phases=[]
            for index,name in enumerate(("initial","replacement","reprepare")):
                actual_scale=6 if index==2 else requested
                ctype=((27 if tz else 26) if self.seven else (23 if tz else 16)) if fixed else (27 if tz else 26) if actual_scale>=7 else (23 if tz else 16)
                value=clr(MANUAL["ordinary_ticks"]+(1234560 if index==0 else 7654320),330 if tz else None)
                phase=dict(phase=name,status="observed",target_column=payload["target_column"],row_id=self.row_id(),persisted_rows=1 if fixed else 0,
                    encoded_scale=actual_scale,encoded_scale_observable=True,execute_attempted=True,
                    parameter_metadata=[meta(ctype,(7 if self.seven else 6) if fixed else actual_scale,fixed)],
                    parameter_metadata_before_execute=[meta(ctype,(7 if self.seven else 6),True) if fixed else before_binding(meta(ctype,actual_scale,False))],
                    statement_before_owner="command",statement_after_owner="reader",execution_api="ExecuteReader",snapshot_timing="live_reader_before_drain",drain_completed=True,records_affected=1 if fixed else -1,unexpected_rowset=False,statement_id_before=300+index,statement_id_after=300+index,prepare_invoked=True,new_statement_observed=True,recovery_source="none",
                    expected=dict(outcome="null",type="System.DBNull") if null else value,
                    oracles=oracles(value,(7 if self.seven else 6) if fixed else actual_scale,self.driver_lanes if fixed else ["W"],ctype,null))
                if not fixed and not null:
                    for oracle in phase["oracles"]:
                        oracle.update(server_component_basis="not_queried_advisory_server_text_only",server_components_exact=False,server_components=None)
                phases.append(phase)
            payload["phase_results"]=phases
        return self.record("metadata",driver,key,payload)

    def metadata(self):
        for driver in self.driver_lanes:
            for tz, key in ((False,"inferred_dt_tail_stability"),(True,"inferred_dto_tail_stability")):
                ctype=(27 if tz else 26) if self.seven else (23 if tz else 16);scale=7 if self.seven else 6
                result=dict(status="value_replacement_observed",first_metadata=[meta(ctype,scale,False)],second_metadata=[meta(ctype,scale,False)],
                    first_value=clr(MANUAL["ordinary_ticks"]+1234560,330 if tz else None),
                    second_value=clr(MANUAL["ordinary_ticks"]+1234567,330 if tz else None),wire_metadata_stable=True,
                    explicit_scale6_reprepare=[meta(23 if tz else 16,6,False)],scale6_execute_metadata=[meta(23 if tz else 16,6,False)],
                    scale6_value=clr(MANUAL["ordinary_ticks"]+7654320,330 if tz else None),subsequent_read_completion=True)
                phases=[]
                for index,name in enumerate(("initial","replacement","reprepare")):
                    after=meta(23 if tz else 16,6,False) if index==2 else meta(ctype,scale,False)
                    fraction=1234560 if index==0 else 1234567 if index==1 else 7654320
                    value=clr(MANUAL["ordinary_ticks"]+fraction,330 if tz else None)
                    phase=dict(phase=name,status="driver_parameter_rejected" if index==1 and not self.seven else "observed",
                        expected=value,parameter_metadata=[after],parameter_metadata_before_execute=[before_binding(after)],
                        execute_attempted=True,persistence_source="advisory_no_dml",persisted_rows=0,row_id=None,
                        statement_before_owner="command",statement_after_owner="reader",execution_api="ExecuteReader",snapshot_timing="live_reader_before_drain",statement_id_before=200+index,statement_id_after=200+index,
                        prepare_invoked=True,new_statement_observed=True,recovery_source="fresh_connection_after_rejection" if index==2 and not self.seven else "none",
                        encoded_scale=6 if index==2 else scale,encoded_scale_observable=True,
                        oracles=oracles(value,6 if index==2 else scale,["W"]))
                    for oracle in phase["oracles"]:oracle.update(server_component_basis="not_queried_advisory_server_text_only",server_components_exact=False,server_components=None)
                    if index==1 and not self.seven:
                        phase.update(oracles=[],**error("OverflowException"))
                    phases.append(phase)
                result["phase_results"]=phases
                if not self.seven:result["second_value"]=None
                if not self.seven: result.update(status="replacement_rejected",**error("OverflowException"))
                self.record("metadata",driver,key,result)
            for declaration in DECLS:
                for args in self.axes(): self.binding(driver,declaration,*args)
        for loss in (False,True):
            key=("dual_scale6_loss_" if loss else "dual_scale6_")+"bind_DateTime2_s7_punset_fixed_value"
            ctype=16 if self.legacy_column else 26
            forced="NotSupportedException" if ctype==16 else "OverflowException" if loss else None
            record=self.write("metadata","W",key,clr(MANUAL["ordinary_ticks"]+(1234567 if loss else 1234560)),7,forced,ctype)
            record["column_metadata"]=[meta(ctype,6)];record["column_scale"]=6;record["target_column"]="D6"
            if record["status"]=="written":record["oracles"]=oracles(record["expected"],6,self.driver_lanes,ctype)

    def extended(self):
        # Handwritten DATE/TIME/TIME TZ facts are independent of the validator's sample table.
        for key,sample in MANUAL["ordinary_layout_samples"].items():
            parts=sample["fields"];ctype=sample["ctype"];scale=sample["scale"];offset=sample["offset_minutes"]
            actual=dict(text=sample["server_text"],fields=dict(zip(("year","month","day","hour","minute","second","nanoseconds"),parts),offset_minutes=offset),semantic_scale=scale,observable=True)
            self.record("extended","server",key,dict(status="server_expression_evaluated",actual_server=actual,seed_writer="server_expression",independent_of_W_write=True,oracle_role="server_layout_seed_not_typed_write_oracle"))
            calendar=parts[:3] if ctype==14 else (1900,1,1)
            ticks=calendar_ticks(*calendar,*parts[3:6])+parts[6]//100
            invalid=dict(outcome="error",error=dict(type="System.InvalidCastException",number=None))
            shown=sample["server_text"] if ctype==14 else sample["server_text"].replace("123456000","123456")
            for driver in self.driver_lanes:
                self.record("extended",driver,key,dict(status="observed",actual_server=copy.deepcopy(actual),metadata=[meta(ctype,scale)],schema_scales=[scale],cell=cell(ctype,scale),
                    typed=clr(ticks),generic=copy.deepcopy(invalid) if ctype==22 else clr(ticks),get_value=clr(ticks,offset),
                    typed_offset=clr(ticks,offset) if ctype==22 else copy.deepcopy(invalid),generic_offset=clr(ticks,offset) if ctype==22 else copy.deepcopy(invalid),
                    get_string=dict(outcome="returned",type="System.String",text=shown),subsequent_read_completion=True))
        ordinary=MANUAL["ordinary_ticks"]
        for scale in (8,9):
            self.record("extended","server","ddl_scale"+str(scale),dict(status="server_columns_created"))
            for driver in self.driver_lanes:
                self.record("extended",driver,"columns_scale"+str(scale),dict(status="observed",metadata=[meta(26,scale),meta(27,scale)],schema_scales=[scale,scale]))
                for tz in (False,True):self.write("extended",driver,("write_tz_scale" if tz else "write_scale")+str(scale),clr(ordinary+1,330 if tz else None),scale)
            for ns in ((10,90,100,1) if scale==8 else (1,99,100)):
                actual_ns=0 if scale==8 and ns==1 else ns
                key=f"ns_scale{scale}_{ns}";actual_ticks=ordinary+actual_ns//100
                ff=text(ordinary)[:-9]+f"{actual_ns:09}"
                ft=dict(zip(("year","month","day","hour","minute","second","nanoseconds"),(*fields(ordinary)[:6],actual_ns)),offset_minutes=None)
                actual=dict(text=ff,fields=ft,semantic_scale=scale)
                self.record("extended","server",key,dict(status="server_expression_evaluated",actual_server=actual,server_text=ff,
                    requested_synthetic_text=text(ordinary)[:-9]+f"{ns:09}",server_preserves_requested=ns==actual_ns))
                for driver in self.driver_lanes:
                    value=dict(outcome="error",error=dict(type="System.OverflowException",number=None)) if actual_ns%100 else clr(actual_ticks)
                    self.record("extended",driver,key,dict(status="observed",actual_server=actual,metadata=[meta(26,scale)],schema_scales=[scale],cell=cell(26,scale),
                        get_value=value,typed_date=copy.deepcopy(value),generic_date=copy.deepcopy(value),
                        get_string=dict(outcome="returned",type="System.String",text=ff[:-9]+f"{actual_ns:09}"[:scale]),subsequent_read_completion=True))
        ids=["calendar_1582_10_"+str(d) for d in (4,5,10,14,15)]+["bce","year_zero"]
        ids += [p+str(s) for p in ("time24_scale","time_fraction_scale") for s in (6,7,8,9)]
        for key in ids:
            self.record("extended","server",key,dict(status="failed",classification="server_unsupported",failure_class="verified_server_response",
                error=dict(type="W.Dm.DmException",number=-6118,verified_server_response=True,failure_kind="Server",phase="Receive",code=None,outcome="ServerReported")))
        for scale in (6,7):
            self.record("extended","server","tltz_ddl_scale"+str(scale),dict(status="server_column_created"))
            for driver in self.driver_lanes:
                self.record("extended",driver,"tltz_metadata_scale"+str(scale),dict(status="observed",metadata=[meta(16 if scale==6 else 26,scale,tltz=True)],schema_scales=[scale]))
            values={"crossday":calendar_ticks(2026,7,23,23,59,59)+(9999999 if scale==7 else 9999990),"min":0,"max":MAXIMUM-(0 if scale==7 else 9)}
            for name,ticks in values.items():
                self.write("extended","W",f"tltz_{scale}_{name}",clr(ticks),scale,"NotSupportedException")
                self.record("extended","server",f"server_seed_TLTZ{scale}_{name}",dict(status="seeded",seed_writer="server_expression",independent_of_W_write=True,server_ff9=text(ticks)))
                value=dict(outcome="error",error=dict(type="System.NotSupportedException",number=None))
                self.record("extended","W",f"tltz_read_{scale}_{name}",dict(status="observed",typed=value,generic=copy.deepcopy(value),get_value=copy.deepcopy(value),get_string=copy.deepcopy(value),subsequent_read_completion=True))
        self.record("extended","W","tltz_cross_session_timezone",dict(status="closed",capability_open=False))

    def base_report(self,lane):
        return dict(schema_version=1,phase=self.phase,profile=self.profile,lane=lane,process_exit_code=0,
            identity_sha256=self.runner["identity_sha256"],source_manifest_sha256=self.runner["source_manifest_sha256"],
            product_source_manifest_sha256=self.runner["product_source_manifest_sha256"],
            server_identity_verified=True,cleanup_verified=True,final_database_state="owned_table_absent_fresh_connection",
            owned_table="TPN_"+MANUAL["nonce"][:24],W_profile=self.physical_profile(),O_profile=self.physical_profile(),
            identities={d:dict(self.identity[d],package_reference_verified=True,actual_asset_verified=True) for d in ("O","W")})

    def physical_profile(self):
        # Independent expected report fields; never call the production profile validator/capture helper.
        return dict(server_version=MANUAL[self.profile+"_server_version"],encoding="utf-8",negotiated_encoding="utf-8",message_version=21,timezone=480,
            result_format="ordinary_non_BDTA",server_user="WDM_PROVIDER_TEST",server_schema="WDM_PROVIDER_TEST",encoding_override_empty=True,
            profile_origin="physical_get_conn_instance_conn_property",negotiated_encoding_origin="physical_conn_property_serverEncoding_field")

    def flush(self):
        self.runner["runs"]=[]
        for lane in ("core","metadata","extended"):
            report=self.base_report(lane);report.update(status="characterization_complete",cases=self.data[lane+".cases"],completed_cases=len(self.data[lane+".cases"]))
            self.put(lane+".json",report);self.put(lane+".cleanup.json",self.base_report(lane))
            self.put(lane+".ownership.json",dict(schema_version=1,nonce=MANUAL["nonce"],table=report["owned_table"],profile=self.profile,server_version=MANUAL[self.profile+"_server_version"],
                owner_package_sha256=self.identity["W"]["package_sha256"],identity_sha256=self.runner["identity_sha256"],absent_before_create=True,identity_verified=True,
                creation_state="confirmed",owner_marker="OWN_"+MANUAL["nonce"][:24]))
            self.runner["runs"].append(dict(lane=lane,exit_code=0,hard_deadline=False,cleanup_exit_code=0,cleanup_hard_deadline=False,report_sha256=self.hash(lane+".json")))
        self.put("runner.json",self.runner)

    def refresh_transport_hashes(self):
        """Rebind nonauthoritative file/hash receipts, without changing producer approval."""
        self.frozen["identity_sha256"]=self.hash("frozen-input/identities.json")
        self.frozen["product_source_manifest_sha256"]=self.hash("frozen-input/product-source-manifest.json")
        if self.phase=="candidate":self.frozen["capabilities_sha256"]=self.hash("frozen-input/capabilities.json")
        self.put("frozen-input/manifest.json",self.frozen)
        self.runner.update(identity_sha256=self.frozen["identity_sha256"],
            product_source_manifest_sha256=self.frozen["product_source_manifest_sha256"],
            capabilities_sha256=self.frozen["capabilities_sha256"],source_manifest_sha256=self.hash("frozen-input/manifest.json"))
        for lane in ("core","metadata","extended"):
            for suffix in (".json",".cleanup.json"):
                record=self.data[lane+suffix]
                for key in ("identity_sha256","source_manifest_sha256","product_source_manifest_sha256"):
                    record[key]=self.runner[key]
                self.put(lane+suffix,record)
            owner=self.data[lane+".ownership.json"];owner["identity_sha256"]=self.runner["identity_sha256"]
            self.put(lane+".ownership.json",owner)
            next(r for r in self.runner["runs"] if r["lane"]==lane)["report_sha256"]=self.hash(lane+".json")
        self.put("runner.json",self.runner)

    def case(self,lane,key,driver="W"):
        return next(c for c in self.data[lane+".cases"] if c["case_id"]==key and c["driver_lane"]==driver)

    def rewrite_report(self,lane):
        self.data[lane+".json"]["cases"]=self.data[lane+".cases"]
        self.data[lane+".json"]["completed_cases"]=len(self.data[lane+".cases"])
        self.put(lane+".json",self.data[lane+".json"])
        next(r for r in self.runner["runs"] if r["lane"]==lane)["report_sha256"]=self.hash(lane+".json")
        self.put("runner.json",self.runner)


class ValidatorContracts(unittest.TestCase):
    def fresh(self, **kwargs):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        return HandmadeArtifact(temporary.name,**kwargs)

    def accepted(self,artifact):
        result=VALIDATOR.validate(artifact.directory,"candidate" if artifact.phase=="candidate" else "characterization")
        self.assertNotEqual("validation_failed",result["status"],result)
        self.assertEqual(artifact.phase=="candidate",result["candidate_acceptance"])
        self.assertFalse(result["full_temporal_acceptance"])

    def rejected(self,artifact,gate=None):
        result=VALIDATOR.validate(artifact.directory,gate or ("candidate" if artifact.phase=="candidate" else "characterization"))
        self.assertEqual("validation_failed",result["status"],result)
        self.assertFalse(result["candidate_acceptance"])

    def test_handwritten_shared_candidate_positive(self):self.accepted(self.fresh())
    def test_handwritten_tls_closed7_positive(self):self.accepted(self.fresh(profile="tls",scale8=False))
    def authorize_advisory_query(self, artifact):
        # File identity proves which immutable query produced the observation; expected
        # result metadata/value literals below are handwritten independently.
        program = (HERE / "Program.cs").read_bytes()
        self.assertEqual("e01dee6daae69272521df4b2bb2544af02818c438a473713fd7fbfae63ed0ff9", sha(program))
        (artifact.directory / "build/Program.cs").write_bytes(program)
        artifact.frozen["tools"].append(dict(path="build/Program.cs", sha256=sha(program)))
        artifact.refresh_transport_hashes()

    def extended_advisory_result(self, phase, tz=False):
        row = phase["oracles"][0]
        row["metadata"] = [meta(27 if tz else 26, 7)]
        row["schema_scales"] = [7]
        is_null = row["get_value"]["outcome"] == "null"
        row["cell"] = cell(27 if tz else 26, 7)
        row.pop("raw_oracle", None)  # advisory producer did not collect independent packed bytes
        if is_null:
            row["cell"].update(is_null=True, payload_length=None, payload_sha256=None)
        else:
            expected = phase["expected"]
            row["get_string"]["text"] = text(expected["ticks"], expected["offset_minutes"], 7)
        return row

    def test_v12_equal_legacy_and_precise_shared_mismatches_have_positive_controls(self):
        for declaration, tz in (("DateTime", False), ("DateTimeOffset", True)):
            for null in (False, True):
                a = self.fresh(); self.accepted(a)  # includes unchanged equal16/6 and23/6
                self.authorize_advisory_query(a)
                key = "bind_" + declaration + ("_s7_punset_advisory_null" if null else "_sunset_p0_advisory_value")
                case = a.case("metadata", key)
                for phase in case["phase_results"]:
                    self.extended_advisory_result(phase, tz)
                a.rewrite_report("metadata"); self.accepted(a)
        for tz, key in ((False, "inferred_dt_tail_stability"), (True, "inferred_dto_tail_stability")):
            a = self.fresh(); self.accepted(a); self.authorize_advisory_query(a)
            self.extended_advisory_result(a.case("metadata", key)["phase_results"][2], tz)
            a.rewrite_report("metadata"); self.accepted(a)

    def test_v12_result_scope_metadata_text_and_value_mutants_reject(self):
        mutations = {
            "drop_fraction_zero": lambda row: row["get_string"].update(text="2026-07-23 14:15:16.123456"),
            "extra_fraction_zero": lambda row: row["get_string"].update(text="2026-07-23 14:15:16.12345600"),
            "numeric_fraction": lambda row: row["get_string"].update(text="2026-07-23 14:15:16.1234561"),
            "typed_ticks": lambda row: row["typed"].update(ticks=MANUAL["ordinary_ticks"] + 1234561),
            "generic_ticks": lambda row: row["generic"].update(ticks=MANUAL["ordinary_ticks"] + 1234561),
            "getvalue_ticks": lambda row: row["get_value"].update(ticks=MANUAL["ordinary_ticks"] + 1234561),
            "ff9_ns": lambda row: row.update(server_ff9="2026-07-23 14:15:16.123456001"),
            "schema_conflict": lambda row: row.update(schema_scales=[6]),
            "raw_scale": lambda row: row["metadata"][0].update(raw_scale=6),
            "parameter_role": lambda row: row["metadata"][0].update(raw_source="ParameterDescribe"),
            "synthetic_role": lambda row: row["metadata"][0].update(raw_source="Synthetic"),
            "effective_role": lambda row: row["metadata"][0].update(metadata_role="parameter_describe"),
            "raw_type": lambda row: row["metadata"][0].update(raw_c_type=16),
            "raw_width": lambda row: row["metadata"][0].update(raw_precision=8),
            "flag": lambda row: row["metadata"][0].update(type_flag=1),
            "family": lambda row: row["metadata"][0].update(c_type=27),
            "TLTZ": lambda row: row["metadata"][0].update(mask=4, raw_derived_mask=4, raw_scale=0x1007),
            "raw_mask": lambda row: row["metadata"][0].update(raw_mask_before=1),
            "BDTA_metadata": lambda row: row["metadata"][0].update(is_bdta=True, result_format="bdta"),
            "BDTA_cell": lambda row: row["cell"].update(is_bdta=True, result_format="bdta"),
            "ordinary12": lambda row: row["cell"].update(payload_length=12),
            "unknown_layout": lambda row: row["cell"].update(payload_length=13),
            "missing_hash": lambda row: row["cell"].pop("payload_sha256"),
            "scale8": lambda row: row["metadata"][0].update(semantic_scale=8, raw_scale=8),
            "scale9": lambda row: row["metadata"][0].update(semantic_scale=9, raw_scale=9),
            "read_completion": lambda row: row.update(subsequent_read_completion=False),
        }
        for label, mutate in mutations.items():
            with self.subTest(label=label):
                a = self.fresh(); self.authorize_advisory_query(a)
                phase = a.case("metadata", "bind_DateTime_sunset_p0_advisory_value")["phase_results"][0]
                row = self.extended_advisory_result(phase); a.rewrite_report("metadata"); self.accepted(a)
                mutate(row); a.rewrite_report("metadata"); self.rejected(a)
        a = self.fresh(); self.authorize_advisory_query(a)
        phase = a.case("metadata", "bind_DateTimeOffset_sunset_p0_advisory_value")["phase_results"][0]
        row = self.extended_advisory_result(phase, True); a.rewrite_report("metadata"); self.accepted(a)
        row["get_string"]["text"] = "2026-07-23 14:15:16.1234560 +00:00"
        a.rewrite_report("metadata"); self.rejected(a)

    def test_v12_mismatch_needs_query_scope_and_does_not_change_write_or_fixed_core_contract(self):
        for mutation in ("no_Program", "wrong_Program", "physical_encoding", "physical_msg", "physical_version", "physical_override", "write_encoded", "write_metadata", "fixed", "core"):
            with self.subTest(mutation=mutation):
                a = self.fresh(); self.authorize_advisory_query(a)
                phase = a.case("metadata", "bind_DateTime_sunset_p0_advisory_value")["phase_results"][0]
                self.extended_advisory_result(phase); a.rewrite_report("metadata"); self.accepted(a)
                if mutation == "no_Program":
                    a.frozen["tools"] = [entry for entry in a.frozen["tools"] if entry["path"] != "build/Program.cs"]
                    a.refresh_transport_hashes()
                elif mutation == "wrong_Program":
                    (a.directory / "build/Program.cs").write_bytes(b"changed query")
                    next(entry for entry in a.frozen["tools"] if entry["path"] == "build/Program.cs")["sha256"] = a.hash("build/Program.cs")
                    a.refresh_transport_hashes()
                elif mutation.startswith("physical_"):
                    physical = a.data["metadata.json"]["W_profile"]
                    if mutation == "physical_encoding": physical.update(encoding="gb18030", negotiated_encoding="gb18030")
                    elif mutation == "physical_msg": physical["message_version"] = 11
                    elif mutation == "physical_version": physical["server_version"] = "8.1.5.61"
                    else: physical["encoding_override_empty"] = False
                    a.rewrite_report("metadata")
                elif mutation == "write_encoded": phase["encoded_scale"] = 7; a.rewrite_report("metadata")
                elif mutation == "write_metadata": phase["parameter_metadata"][0]["semantic_scale"] = 7; a.rewrite_report("metadata")
                elif mutation == "fixed":
                    a.case("metadata", "bind_DateTime_s7_punset_fixed_value")["phase_results"][0]["encoded_scale"] = 6
                    a.rewrite_report("metadata")
                else: a.case("core", "dt6_ordinary_micro")["oracles"][1]["metadata"][0]["raw_scale"] = 7; a.rewrite_report("core")
                self.rejected(a)
        a = self.fresh(profile="tls", scale8=False); self.accepted(a); self.authorize_advisory_query(a)
        self.extended_advisory_result(a.case("metadata", "bind_DateTime_sunset_p0_advisory_value")["phase_results"][0])
        a.rewrite_report("metadata"); self.rejected(a)

    def test_v12_NULL_result_descriptor_and_existing_receipts_both_remain_strict(self):
        for mutation in ("raw_role", "schema", "payload", "receipt", "context", "format"):
            with self.subTest(mutation=mutation):
                a = self.fresh(); self.authorize_advisory_query(a)
                phase = a.case("metadata", "bind_DateTime_s7_punset_advisory_null")["phase_results"][2]
                row = self.extended_advisory_result(phase); a.rewrite_report("metadata"); self.accepted(a)
                if mutation == "raw_role": row["metadata"][0]["raw_source"] = "ParameterDescribe"
                elif mutation == "schema": row["schema_scales"] = [6]
                elif mutation == "payload": row["cell"].update(payload_length=9, payload_sha256="e"*64)
                elif mutation == "receipt": row["get_string"].pop("known_null_receipt")
                elif mutation == "context": row["null_context"]["value_is_dbnull"] = False
                else: row["cell"].update(result_format="bdta", is_bdta=True)
                a.rewrite_report("metadata"); self.rejected(a)

    def lineage_fixture(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        root = Path(temporary.name); a = HandmadeArtifact(root / "producer")
        self.authorize_advisory_query(a)
        (a.directory / "frozen-input/product-source.tar").write_bytes(b"OFFLINE-CONTRACT-NOT-A-PRODUCT-ARCHIVE")
        a.identity["producer"]["source_archive_sha256"] = a.hash("frozen-input/product-source.tar")
        a.put("frozen-input/identities.json", a.identity)
        # Root-approved synthetic old policy is never executed. Default mode must reject its self hash.
        (a.directory / "frozen-input/temporal-precision.validate.py").write_bytes(b"OFFLINE-OLD-PRODUCER-POLICY")
        old_sha = a.hash("frozen-input/temporal-precision.validate.py")
        next(entry for entry in a.frozen["tools"] if entry["path"] == "frozen-input/temporal-precision.validate.py")["sha256"] = old_sha
        a.refresh_transport_hashes()
        for lane in ("core", "metadata", "extended"):
            a.put(lane + ".ownership.json.final.json", dict(a.data[lane + ".ownership.json"], synthetic=True))
        prior = root / "failure.json"
        prior.write_text(json.dumps(dict(status="validation_failed", gate="candidate", candidate_acceptance=False, full_temporal_acceptance=False)))
        summary = root / "runtime.json"; summary.write_text('{"synthetic":true,"database_accessed":false}')
        reviewed = root / "reviewed.diff"; reviewed.write_bytes(b"OFFLINE-HANDWRITTEN-ROOT-REVIEW-RECEIPT")
        rules = dict(schema_version=1, task="TP06", scope="shared_advisory_result_tuple_v1", rule_id="TP06-ADVISORY-RESULT-001",
            root_approved=True, root_acceptance_id="offline-contract-root-receipt-not-real-approval",
            physical=dict(server_version="8.1.5.60", message_version=21, negotiated_encoding="utf-8", encoding_override_empty=True),
            parameter_query=dict(program_path="build/Program.cs", program_sha256="e01dee6daae69272521df4b2bb2544af02818c438a473713fd7fbfae63ed0ff9", source_method="ParameterQuery(bool tz)"),
            allowed_mismatches=[dict(write=[16,6],read=[26,7]),dict(write=[23,6],read=[27,7])],
            producer_policy_sha256=old_sha, reviewed_patch=dict(path=str(reviewed),sha256=sha(reviewed.read_bytes())),
            reviewed_design_sha256="1e0926b03501ee8072cddeb7a6e348f01020b3d7539732328701c3702ec2151e")
        rules_path = root / "rules.json"; rules_path.write_text(json.dumps(rules))
        role_files = {"runner":"runner.json", "frozen_manifest":"frozen-input/manifest.json", "identities":"frozen-input/identities.json",
            "product_source_manifest":"frozen-input/product-source-manifest.json", "product_source_archive":"frozen-input/product-source.tar", "capabilities":"frozen-input/capabilities.json"}
        for lane in ("core", "metadata", "extended"):
            for suffix, role in ((".json","report"),(".cleanup.json","cleanup"),(".ownership.json","ownership"),(".ownership.json.final.json","ownership_final")):
                role_files[lane + "_" + role] = lane + suffix
        receipts = [dict(role=role,path="producer/"+path,sha256=a.hash(path)) for role,path in role_files.items()]
        receipts += [dict(role="failure",path="failure.json",sha256=sha(prior.read_bytes())),dict(role="runtime_summary",path="runtime.json",sha256=sha(summary.read_bytes()))]
        paths = ("eng/temporal-precision.validate.py","tools/TemporalBoundaryProbe/test_validator_contracts.py","tools/TemporalBoundaryProbe/contracts.py",
            "tools/TemporalBoundaryProbe/packed_oracle.py","tools/TemporalBoundaryProbe/Program.cs","tools/TemporalBoundaryProbe/contract-fixtures/handwritten-artifact-spec.json","tools/TemporalBoundaryProbe/contract-fixtures/packed_literal_cases.json")
        lineage = dict(schema_version=1,task="TP06",scope="shared_advisory_result_tuple_v1",producer_evidence_root=str(root),producer_directory="producer",
            producer_policy_sha256=old_sha,producer_evidence=receipts,verifier_root=str(ROOT),verifier_files=[dict(path=path,sha256=sha((ROOT/path).read_bytes())) for path in paths],
            rules_manifest=dict(path=str(rules_path),sha256=sha(rules_path.read_bytes())))
        manifest = root / "lineage.json"; manifest.write_text(json.dumps(lineage))
        return a, root, manifest, lineage, rules

    def revalidated(self, artifact, root, manifest):
        return VALIDATOR.validate(artifact.directory,"candidate",manifest,sha(manifest.read_bytes()),root/"fresh-output.json")

    def test_v12_explicit_lineage_preserves_old_inputs_and_default_selfhash(self):
        a, root, manifest, lineage, rules = self.lineage_fixture()
        before = {receipt["path"]:sha((root/receipt["path"]).read_bytes()) for receipt in lineage["producer_evidence"]}
        self.rejected(a)  # no silent fallback just because an older policy is present
        result = self.revalidated(a,root,manifest)
        self.assertEqual("candidate_profile_accepted",result["status"],result)
        self.assertTrue(result["original_failure_retained"]); self.assertFalse(result["database_accessed"])
        self.assertEqual(lineage["producer_policy_sha256"],result["producer_revision"]["policy_sha256"])
        self.assertEqual(sha((ROOT/"eng/temporal-precision.validate.py").read_bytes()),result["verifier_revision"]["files"]["eng/temporal-precision.validate.py"])
        self.assertEqual(before,{path:sha((root/path).read_bytes()) for path in before})
        output=root/"cli-output.json"
        args=["validator",str(a.directory),"--gate","candidate","--output",str(output),"--revalidation-manifest",str(manifest),"--revalidation-manifest-sha256",sha(manifest.read_bytes())]
        with patch.object(sys,"argv",args),contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0,VALIDATOR.main())
        receipt=json.loads(output.with_name(output.name+".lineage.json").read_text())
        self.assertEqual(sha(output.read_bytes()),receipt["output"]["sha256"])
        self.assertEqual(lineage["producer_evidence"],receipt["producer_inputs"])
        with patch.object(sys,"argv",args),contextlib.redirect_stdout(io.StringIO()):self.assertEqual(1,VALIDATOR.main())

    def test_v12_lineage_hash_role_dependency_scope_and_receipt_mutants_reject(self):
        for mutation in ("manifest_sha","old_failure","runner","report","cleanup","ownership_final","source_archive","duplicate_role","missing_lane","extra_role","escape",
                         "producer_tool","new_selfhash","test_hash","contracts","packed","rule_TLS","rule_cross_family","rule_scale8","rule_program","reviewed_patch"):
            with self.subTest(mutation=mutation):
                a,root,manifest,lineage,rules=self.lineage_fixture()
                self.assertEqual("candidate_profile_accepted",self.revalidated(a,root,manifest)["status"])
                supplied_sha=None
                if mutation=="manifest_sha":supplied_sha="0"*64
                elif mutation in ("old_failure","runner","report","cleanup","ownership_final","source_archive"):
                    role={"old_failure":"failure","report":"metadata_report","cleanup":"metadata_cleanup","ownership_final":"metadata_ownership_final","source_archive":"product_source_archive"}.get(mutation,mutation)
                    receipt=next(item for item in lineage["producer_evidence"] if item["role"]==role)
                    (root/receipt["path"]).write_bytes(b"mutated old input")
                elif mutation=="duplicate_role":lineage["producer_evidence"][-1]=copy.deepcopy(lineage["producer_evidence"][0])
                elif mutation=="missing_lane":lineage["producer_evidence"].pop()
                elif mutation=="extra_role":lineage["producer_evidence"].append(dict(role="extra",path="failure.json",sha256=sha((root/"failure.json").read_bytes())))
                elif mutation=="escape":lineage["producer_evidence"][0]["path"]="../runner.json"
                elif mutation=="producer_tool":(a.directory/"build/Program.cs").write_bytes(b"changed producer tool")
                elif mutation in ("new_selfhash","test_hash","contracts","packed"):
                    key={"new_selfhash":"eng/temporal-precision.validate.py","test_hash":"tools/TemporalBoundaryProbe/test_validator_contracts.py","contracts":"tools/TemporalBoundaryProbe/contracts.py","packed":"tools/TemporalBoundaryProbe/packed_oracle.py"}[mutation]
                    next(item for item in lineage["verifier_files"] if item["path"]==key)["sha256"]="0"*64
                elif mutation=="reviewed_patch":(root/"reviewed.diff").write_bytes(b"changed review patch")
                else:
                    if mutation=="rule_TLS":rules["physical"].update(server_version="8.1.4.6",message_version=11,negotiated_encoding="gb18030")
                    elif mutation=="rule_cross_family":rules["allowed_mismatches"][0]["read"]=[27,7]
                    elif mutation=="rule_scale8":rules["allowed_mismatches"][0]["read"]=[26,8]
                    else:rules["parameter_query"]["program_sha256"]="0"*64
                    (root/"rules.json").write_text(json.dumps(rules));lineage["rules_manifest"]["sha256"]=sha((root/"rules.json").read_bytes())
                manifest.write_text(json.dumps(lineage))
                result=VALIDATOR.validate(a.directory,"candidate",manifest,supplied_sha or sha(manifest.read_bytes()),root/"fresh.json")
                self.assertEqual("validation_failed",result["status"],result)
                self.assertFalse(result["candidate_acceptance"])

    def test_v12_CLI_requires_paired_options_and_fresh_external_output_and_sidecar(self):
        a,root,manifest,lineage,rules=self.lineage_fixture()
        for option,value in (("--revalidation-manifest",str(manifest)),("--revalidation-manifest-sha256",sha(manifest.read_bytes()))):
            args=["validator",str(a.directory),"--gate","candidate","--output",str(root/"missing-pair.json"),option,value]
            with patch.object(sys,"argv",args),contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as rejected:self.assertEqual(1,VALIDATOR.main())
                self.assertEqual(2,rejected.exception.code)
            self.assertFalse((root/"missing-pair.json").exists())
        for output in (a.directory/"new.json",root/"existing.json",root/"existing-sidecar.json"):
            if output.name=="existing.json":output.write_text("preserve")
            if output.name=="existing-sidecar.json":output.with_name(output.name+".lineage.json").write_text("preserve sidecar")
            args=["validator",str(a.directory),"--gate","candidate","--output",str(output),"--revalidation-manifest",str(manifest),"--revalidation-manifest-sha256",sha(manifest.read_bytes())]
            with patch.object(sys,"argv",args),contextlib.redirect_stdout(io.StringIO()):self.assertEqual(1,VALIDATOR.main())
            if output.name=="existing.json":self.assertEqual("preserve",output.read_text())
            else:self.assertFalse(output.exists())

    def test_v11_all_live_and_cleanup_profiles_require_physical_origin(self):
        changes=(lambda p:p.update(profile_origin="logical_conn_property"),lambda p:p.pop("profile_origin"),
            lambda p:p.update(negotiated_encoding_origin="raw_STARTUP"),lambda p:p.update(encoding_override_empty=False),
            lambda p:p.update(negotiated_encoding="gb18030"),lambda p:p.pop("negotiated_encoding"),
            lambda p:p.update(message_version=True),lambda p:p.update(timezone=None))
        for lane in ("core","metadata","extended"):
            for suffix,driver in ((".json","W"),(".cleanup.json","W"),(".json","O")):
                for change in changes:
                    with self.subTest(lane=lane,suffix=suffix,driver=driver):
                        a=self.fresh();self.accepted(a)
                        record=a.data[lane+suffix];change(record[driver+"_profile"])
                        a.put(lane+suffix,record)
                        if suffix==".json":a.rewrite_report(lane)
                        self.rejected(a)
    def test_handwritten_baseline_positive_not_candidate(self):
        artifact=self.fresh(phase="baseline");self.accepted(artifact);self.rejected(artifact,"candidate")
    def test_extended_scale6_dual_constraint_positive(self):self.accepted(self.fresh(legacy_column=False))

    def test_core_mutations_reject_at_top_level(self):
        mutations={
            "Kind failed":lambda c:c.update(status="failed",**error("InvalidOperationException","unverified_driver_error")),
            "wrongticks":lambda c:c["oracles"][1]["typed"].update(ticks=c["expected"]["ticks"]+1),
            "Kind Local":lambda c:c["oracles"][1]["generic"].update(kind="Local"),
            "server_exact false":lambda c:c["oracles"][1].update(server_exact=False),
            "wrong string":lambda c:c["oracles"][1]["get_string"].update(text="2026-07-23 14:15:16.0000002"),
            "read unfinished":lambda c:c["oracles"][1].update(subsequent_read_completion=False),
            "bool ticks":lambda c:c["oracles"][1]["typed"].update(ticks=True),
            "unobservable layout":lambda c:c["oracles"][1]["cell"].update(observable=False,source="unobservable"),
            "wrong raw provenance":lambda c:c["oracles"][1]["metadata"][0].update(raw_scale=6),
        }
        for name,mutation in mutations.items():
            with self.subTest(name=name):
                artifact=self.fresh();mutation(artifact.case("core","kind_Unspecified"));artifact.rewrite_report("core");self.rejected(artifact)

    def test_expected_and_actual_drift_together_rejects(self):
        a=self.fresh();c=a.case("core","dt7_max");c["expected"]["ticks"]-=1
        for oracle in c["oracles"]:
            for getter in ("typed","generic","get_value"):oracle[getter]["ticks"]-=1
        a.rewrite_report("core");self.rejected(a)

    def test_delete_kind_endpoint_and_fake_constructor_reject(self):
        for key in ("kind_Utc","local_min_0","utc_max_N300"):
            with self.subTest(key=key):
                a=self.fresh();a.data["core.cases"].remove(a.case("core",key));a.rewrite_report("core");self.rejected(a)
        a=self.fresh();c=a.case("core","local_min_0");c.update(status="constructor_rejected_before_driver",**error("ArgumentOutOfRangeException","clr_precondition"));a.rewrite_report("core");self.rejected(a)

    def test_wrong_offset_and_TLS_closed_success_reject(self):
        a=self.fresh();a.case("core","offset_330")["oracles"][1]["generic"]["offset_minutes"]=0;a.rewrite_report("core");self.rejected(a)
        a=self.fresh(profile="tls",scale8=False);c=a.case("core","dt7_min");c.update(status="written",persisted_rows=1,oracles=oracles(clr(0),7,["W"]));a.rewrite_report("core");self.rejected(a)

    def test_binding_mutations_reject(self):
        ids=("bind_DateTime_sunset_punset_advisory_value","bind_inferred_sunset_punset_advisory_value","bind_DateTime2_s7_punset_fixed_value")
        for key in ids:
            with self.subTest(key=key):
                a=self.fresh();a.case("metadata",key).update(status="driver_parameter_rejected",**error("NotSupportedException"));a.rewrite_report("metadata");self.rejected(a)
        for field,value in (("c_type",16),("semantic_scale",6),("width",7),("raw_scale",6)):
            with self.subTest(field=field):
                a=self.fresh();a.case("metadata","bind_inferred_sunset_punset_advisory_value")["phase_results"][0]["parameter_metadata"][0][field]=value;a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();c=a.case("metadata","inferred_dt_tail_stability");c["second_value"]["ticks"]+=10;a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();a.case("metadata","bind_DateTime_sunset_p0_advisory_value")["phase_results"][0]["parameter_metadata"][0]["width"]=0;a.rewrite_report("metadata");self.rejected(a)

    def test_missing_dual_reprepare_NULL_source_and_fixed_scale_reject(self):
        a=self.fresh();a.data["metadata.cases"].remove(a.case("metadata","dual_scale6_loss_bind_DateTime2_s7_punset_fixed_value"));a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();a.case("metadata","bind_DateTime2_s7_punset_fixed_value")["phase_results"].pop();a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();a.case("metadata","bind_inferred_s7_punset_advisory_null")["type_source"]="user_db_type";a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();a.case("metadata","bind_DateTime_s7_punset_fixed_value")["column_metadata"][0]["semantic_scale"]=6;a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh(profile="tls",scale8=False);c=a.case("metadata","bind_DateTime_s7_punset_advisory_value");c["status"]="observed";a.rewrite_report("metadata");self.rejected(a)

    def test_TLTZ_highscale_and_cast_mutants_reject(self):
        for key in ("tltz_read_6_min","tltz_7_max"):
            with self.subTest(key=key):
                a=self.fresh();a.data["extended.cases"].remove(a.case("extended",key));a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","tltz_read_6_min")["get_value"]=clr(0);a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","tltz_metadata_scale6")["metadata"][0]["mask"]=0;a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","ddl_scale8","server").update(status="failed",**error("NotSupportedException","unverified_driver_error"));a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","ns_scale8_1")["get_string"]["text"]="2026-07-23 14:15:16.000000010";a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","columns_scale8")["schema_scales"][0]=6;a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","ns_scale9_1")["get_value"]=clr(MANUAL["ordinary_ticks"]);a.rewrite_report("extended");self.rejected(a)

    def test_infrastructure_cleanup_profile_and_ambiguity_reject(self):
        for key in ("dt7_max","kind_Utc"):
            a=self.fresh();a.case("core",key).update(status="driver_write_rejected",persisted_rows=0,**error("TimeoutException","infra_failure"));a.rewrite_report("core");self.rejected(a)
        a=self.fresh();a.data["extended.json"]["W_profile"]["server_version"]="8.1.5.61";a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.data["core.json"]["W_profile"]["server_user"]="WRONG_TEST";a.rewrite_report("core");self.rejected(a)
        a=self.fresh();a.runner["runs"][0]["cleanup_exit_code"]=1;a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();c=a.data["core.cleanup.json"];c["cleanup_verified"]=False;a.put("core.cleanup.json",c);self.rejected(a)
        for key,value in (("profile","typo"),("restore_exit_code",False),("schema_version",True)):
            a=self.fresh();a.runner[key]=value;a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();a.data["core.json"]["lane"]="extended";a.rewrite_report("core");self.rejected(a)
        a=self.fresh();del a.case("core","dt7_max")["oracles"][1]["metadata"];a.rewrite_report("core");self.rejected(a)
        a=self.fresh();path=a.directory/"runner.json";path.write_text(path.read_text().replace('"profile": "shared"','"profile": "shared", "profile": "tls"'));self.rejected(a)

    def test_source_producer_and_capability_mutations_reject(self):
        a=self.fresh();source=a.data["frozen-input/product-source-manifest.json"];source["files"][0]["sha256"]="f"*64;a.put("frozen-input/product-source-manifest.json",source);a.refresh_transport_hashes();self.rejected(a)
        a=self.fresh();a.put("frozen-input/product-source-manifest.json",dict(schema_version=1,head=MANUAL["source_head"],source_files=1));a.refresh_transport_hashes();self.rejected(a)
        a=self.fresh();identity=a.data["frozen-input/identities.json"];identity["producer"]["dll_sha256"]="f"*64;a.put("frozen-input/identities.json",identity);a.refresh_transport_hashes();self.rejected(a)
        for field,value in (("source_manifest_sha256","f"*64),("seven_write",1),("write_scales",[True,6,7,8]),("write_scales",[0,1,2,3,4,5,6,7,7,8]),("tltz_read",True)):
            a=self.fresh();cap=a.data["frozen-input/capabilities.json"]
            if field=="source_manifest_sha256":cap[field]=value
            else:cap["profiles"]["shared"][field]=value
            a.put("frozen-input/capabilities.json",cap);a.refresh_transport_hashes();self.rejected(a)
        a=self.fresh();path=a.directory/"frozen-input/capabilities.json";path.write_text(path.read_text().replace('"root_approved": true','"root_approved": false, "root_approved": true'));self.rejected(a)
        a=self.fresh();identity=a.data["frozen-input/identities.json"];identity["producer"]["source_archive_sha256"]="f"*64;a.put("frozen-input/identities.json",identity);a.refresh_transport_hashes();self.rejected(a)

    def test_archived_pin_cannot_be_impersonated_by_mini_fixture(self):
        a=self.fresh(phase="baseline")
        identity=a.data["frozen-input/identities.json"]
        identity["baseline_kind"]="archived_v23"
        a.put("frozen-input/identities.json",identity);a.refresh_transport_hashes();self.rejected(a)

    def test_duplicate_nested_keys_counts_and_cleanup_booleans_reject(self):
        a=self.fresh();p=a.directory/"core.json"
        p.write_text(p.read_text().replace('"ticks": 639204129160000001','"ticks": 0, "ticks": 639204129160000001',1))
        a.runner["runs"][0]["report_sha256"]=a.hash("core.json");a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();a.data["core.json"]["completed_cases"]=True;a.rewrite_report("core")
        # rewrite_report normally derives count, so alter only this declaration after rehashing.
        r=a.data["core.json"];r["completed_cases"]=True;a.put("core.json",r)
        a.runner["runs"][0]["report_sha256"]=a.hash("core.json");a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();a.runner["runs"][0]["cleanup_exit_code"]=False;a.put("runner.json",a.runner);self.rejected(a)
        a=self.fresh();c=a.case("core","dt7_max");c.update(status="driver_write_rejected",persisted_rows=0,**error("OverflowException"))
        c["error"].update(failure_kind="Timeout",code="WDM_TIMEOUT",phase="Receive",outcome="Unknown")
        a.rewrite_report("core");self.rejected(a)

    def test_text_declared_zeros_and_encoded_scale_observability_reject(self):
        a=self.fresh();a.case("extended","ns_scale8_100")["get_string"]["text"]="2026-07-23 14:15:16.0000001";a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("metadata","bind_DateTime_sunset_punset_advisory_value")["phase_results"][0]["encoded_scale_observable"]=False;a.rewrite_report("metadata");self.rejected(a)

    def test_v8_TSTZ_raw_numeric_oracle_preserves_EXTRACT_characterization(self):
        a=self.fresh();case=a.case("core","offset_330","O")
        for row in case["oracles"]:
            row.update(server_components_exact=False,server_components=["2026","7","23","14","15","16.0000000"],server_component_basis="local")
        a.rewrite_report("core");self.accepted(a)
        self.assertFalse(case["oracles"][1]["server_components_exact"])
        a=self.fresh();case=a.case("core","offset_330","O")
        for row in case["oracles"]:
            row.update(server_components_exact=False,component_oracle_error=dict(type="W.Dm.DmException",number=-6112,failure_class="verified_server_response",verified_server_response=True,failure_kind="Server",phase="Receive",code=None,outcome="ServerReported"))
            row.pop("server_components",None);row.pop("server_component_basis",None)
        a.rewrite_report("core");self.accepted(a)
        a=self.fresh();a.case("core","dt7_ordinary_tick1","O")["oracles"][1]["server_components_exact"]=False;a.rewrite_report("core");self.rejected(a)

    def test_v8_actual_raw_context_mutants_reject_at_top_gate(self):
        mutants={
            "missing raw":lambda r:r.pop("raw_oracle"),
            "wrong raw hash":lambda r:r["raw_oracle"].update(payload_sha256="0"*64),
            "wrong cell hash":lambda r:r["cell"].update(payload_sha256="0"*64),
            "mixed metadata family":lambda r:r["metadata"][0].update(c_type=23),
            "TLTZ mask":lambda r:r["metadata"][0].update(mask=4),
            "wrong source":lambda r:r["raw_oracle"].update(source="server_format_string"),
            "wrong scale":lambda r:r["raw_oracle"].update(semantic_scale=6),
            "wrong length":lambda r:r["raw_oracle"].update(payload_length=10),
            "BDTA":lambda r:r["raw_oracle"].update(is_bdta=True,result_format="bdta"),
        }
        for name,mutate in mutants.items():
            with self.subTest(name=name):
                a=self.fresh();row=a.case("core","offset_330","O")["oracles"][1];mutate(row);a.rewrite_report("core");self.rejected(a)
        # Recomputed independent receipts must not let altered packet fields match the frozen input.
        for literal in ("EA87BBEE81900100004A01","EA87BBEE81C8000000D4FE","EA87B3EE81C80000004A01"):
            a=self.fresh();row=a.case("core","offset_330","O")["oracles"][1]
            row["raw_oracle"].update(synthetic_temporal_hex=literal,payload_sha256=sha(bytes.fromhex(literal)))
            row["cell"]["payload_sha256"]=row["raw_oracle"]["payload_sha256"]
            a.rewrite_report("core");self.rejected(a)

    def test_v10_official_confirmed_NULL_generic_InvalidCast_is_strict_observation(self):
        def local_cast():
            return dict(outcome="error",known_null_receipt=None,error=dict(type="System.InvalidCastException",failure_class="user_rejection",number=None,verified_server_response=False,failure_kind=None,phase=None,code=None,outcome=None))
        for declaration in ("DateTime","DateTimeOffset"):
            a=self.fresh(phase="baseline");self.accepted(a)
            row=a.case("metadata",f"bind_{declaration}_sunset_p0_fixed_null")["phase_results"][0]["oracles"][0]
            row["generic"]=local_cast();a.rewrite_report("metadata");self.accepted(a)
        for mutation in ("wrong_getter","W_lane","expected_nonNULL","wrong_context","actual_nonNULL","cache_unobserved","infra","Unknown","number","receipt","verified_server"):
            with self.subTest(mutation=mutation):
                a=self.fresh(phase="baseline");self.accepted(a)
                row=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"][0]
                value=local_cast();row["generic"]=value
                if mutation=="wrong_getter":row["typed"]=value;row["generic"]=known_null_error("O","GetFieldValue<DateTime>")
                elif mutation=="W_lane":row["reader_lane"]="W";row["null_context"]["reader_lane"]="W"
                elif mutation=="expected_nonNULL":row["null_context"]["expected_dbnull"]=False
                elif mutation=="wrong_context":row["null_context"]["ordinal"]=1
                elif mutation=="actual_nonNULL":row["null_context"]["value_is_dbnull"]=False
                elif mutation=="cache_unobserved":row["cell"]["observable"]=False
                elif mutation=="infra":value["error"].update(failure_class="infra_failure",failure_kind="Transport",phase="Fetch",code="WDM_TRANSPORT",outcome="NotSent")
                elif mutation=="Unknown":value["error"]["outcome"]="Unknown"
                elif mutation=="number":value["error"]["number"]=6081
                elif mutation=="receipt":value["known_null_receipt"]=known_null_error("O","GetFieldValue<DateTime>")["known_null_receipt"]
                else:value["error"]["verified_server_response"]=True
                a.rewrite_report("metadata");self.rejected(a)
        # The same error on a real non-NULL oracle stays rejected by the original precision gate.
        a=self.fresh();self.accepted(a);a.case("core","dt7_ordinary_tick1","O")["oracles"][0]["generic"]=local_cast()
        # O typed observation on ordinary input is characterization; W exact reader is the hard gate.
        a.case("core","dt7_ordinary_tick1","O")["oracles"][1]["generic"]=local_cast();a.rewrite_report("core");self.rejected(a)

    def test_v6_known_cached_NULL_preserves_original_Safe_and_is_local(self):
        a=self.fresh();case=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")
        row=case["phase_results"][0]["oracles"][1]
        self.assertEqual("Transport",row["get_string"]["error"]["failure_kind"])
        self.assertEqual("infra_failure",row["get_string"]["error"]["failure_class"])
        self.accepted(a)
        # The generic failure gate still rejects this exact fallback outside its NULL context.
        with self.assertRaises(VALIDATOR.C.ContractError):
            VALIDATOR.failure(dict(error=row["get_string"]["error"],failure_class="infra_failure"),"non_NULL")

    def test_v6_NULL_receipt_mutants_reject_at_top_validator(self):
        changes={
            "missing receipt":lambda v:v.pop("known_null_receipt"),
            "wrong6081":lambda v:v["error"].update(number=6082),
            "expected nonNULL":lambda v:v["known_null_receipt"].update(expected_dbnull=False),
            "value nonNULL":lambda v:v["known_null_receipt"].update(value_is_dbnull=False),
            "wrong ordinal":lambda v:v["known_null_receipt"].update(ordinal=1),
            "wrong reader":lambda v:v["known_null_receipt"].update(reader_lane="O"),
            "getter not called":lambda v:v["known_null_receipt"].update(actual_getter_invoked=False),
            "real socket":lambda v:v["known_null_receipt"].update(has_real_infra_inner=True),
            "actual socket type":lambda v:v["error"].update(type="System.Net.Sockets.SocketException"),
            "unknown outcome":lambda v:v["error"].update(outcome="Unknown"),
            "unknown kind":lambda v:v["error"].update(failure_kind="OutcomeUnknown"),
            "server error":lambda v:v["error"].update(verified_server_response=True),
            "terminal cancel":lambda v:v["known_null_receipt"].update(cancel_source="User"),
            "txUnknown":lambda v:v["known_null_receipt"].update(transaction_outcome="OutcomeUnknown"),
            "cache contradiction":lambda v:v["known_null_receipt"]["cached_cell"].update(is_null=False),
            "hide cache":lambda v:v["known_null_receipt"].update(cache_observable=False,cached_cell=None),
        }
        for name,change in changes.items():
            with self.subTest(name=name):
                a=self.fresh();row=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"][1]
                change(row["get_string"]);a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();row=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"][1]
        row["cell"].update(is_null=False,payload_length=9,payload_sha256="a"*64);a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();row=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"][1]
        row.pop("null_context");a.rewrite_report("metadata");self.rejected(a)

    def test_v6_official_NULL_forms_do_not_relax_W_candidate_contract(self):
        forms=(dict(outcome="returned-null",type=None,text=None),dict(outcome="returned-string-empty",type="System.String",text=""),clr(0))
        for value in forms:
            a=self.fresh(phase="baseline");rows=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"]
            rows[0]["get_string"]=copy.deepcopy(value);a.rewrite_report("metadata");self.accepted(a)
            a=self.fresh();rows=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"]
            rows[1]["get_string"]=copy.deepcopy(value);a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();row=a.case("metadata","bind_DateTime_sunset_p0_fixed_null")["phase_results"][0]["oracles"][1]
        row["get_value"]=known_null_error("W","GetValue");a.rewrite_report("metadata");self.rejected(a)

    def test_v6_fixed_Metadata_requires_live_reader_and_complete_drain(self):
        for field,value in (("statement_after_owner","command"),("execution_api","ExecuteNonQuery"),("snapshot_timing","after_dispose"),("drain_completed",False),("unexpected_rowset",True),("records_affected",0),("encoded_scale_observable",False)):
            with self.subTest(field=field):
                a=self.fresh()
                self.accepted(a)
                phase=a.case("metadata","bind_DateTime_s7_punset_fixed_value")["phase_results"][0]
                self.assertEqual("observed",phase["status"])
                phase[field]=value;a.rewrite_report("metadata");self.rejected(a)

    def test_C01_TLS_baseline_overflow_is_observation_candidate_requires_NS(self):
        for phase in ("baseline","candidate"):
            a=self.fresh(phase=phase,profile="tls",scale8=False)
            for key in ("offset_N840","offset_N780"):
                a.case("core",key).update(**error("OverflowException"))
            a.rewrite_report("core")
            if phase=="baseline":
                self.accepted(a)
                result=VALIDATOR.validate(a.directory,"characterization")
                self.assertFalse(result["candidate_acceptance"]);self.assertFalse(result["baseline_capability_acceptance"])
            else:self.rejected(a)
        a=self.fresh(phase="baseline",profile="tls",scale8=False)
        c=a.case("core","dt7_ordinary_micro");c.update(status="written",persisted_rows=0)
        c.pop("error",None);c.pop("failure_class",None)
        a.rewrite_report("core");self.accepted(a)
        a=self.fresh(profile="tls",scale8=False);c=a.case("core","dt7_ordinary_micro")
        c.update(status="written",persisted_rows=0);a.rewrite_report("core");self.rejected(a)
        a=self.fresh(phase="baseline",profile="tls",scale8=False)
        a.case("core","offset_N840").update(**error("TimeoutException","infra_failure"));a.rewrite_report("core");self.rejected(a)

    def test_C02_fixed_physical_targets_are_profile_frozen_not_cap_derived(self):
        for declaration,column in (("DateTime2","D6"),("DateTimeOffset","Z6")):
            key=f"bind_{declaration}_s7_punset_fixed_value"
            a=self.fresh(profile="tls",scale8=False)
            c=a.case("metadata",key);self.assertEqual(column,c["target_column"]);self.assertEqual(6,c["column_metadata"][0]["semantic_scale"]);self.accepted(a)
            for mutation in ("wrong_target","wrong_scale","missing_target","skip_fixed"):
                with self.subTest(declaration=declaration,mutation=mutation):
                    b=self.fresh(profile="tls",scale8=False);case=b.case("metadata",key)
                    if mutation=="wrong_target":case["target_column"]=column[0]+"7"
                    elif mutation=="wrong_scale":case["column_metadata"][0]["semantic_scale"]=7
                    elif mutation=="missing_target":case.pop("target_column")
                    else:case.update(status="closed",reason="column_unavailable_not_binding_acceptance",column_metadata=[])
                    b.rewrite_report("metadata");self.rejected(b)
        a=self.fresh();c=a.case("metadata","bind_DateTime2_s7_punset_fixed_value");self.assertEqual("D7",c["target_column"])
        c["target_column"]="D6";a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();c=a.case("metadata","bind_DateTime_sunset_punset_advisory_value");c["target_column"]="D7";a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh(phase="baseline",profile="tls",scale8=False);c=a.case("metadata","bind_DateTime2_s7_punset_fixed_value");c["target_column"]="D7";a.rewrite_report("metadata");self.rejected(a)

    def test_B02_actual_phase_rejection_without_top_persisted_rows_positive(self):
        for fixed in (False,True):
            a=self.fresh();key="bind_DateTime_sunset_p7_"+("fixed" if fixed else "advisory")+"_value"
            c=a.case("metadata",key);self.assertNotIn("persisted_rows",c)
            self.accepted(a)
            # A real executed fixed INSERT rejection must have a fresh row-count observation.
            if fixed:
                p=c["phase_results"][0];p.update(row_id=988,execute_attempted=True,persisted_rows=0,persistence_source="fresh_connection_row_count")
                a.rewrite_report("metadata");self.accepted(a)
                p.pop("persisted_rows");a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();c=a.case("metadata","bind_DateTime_sunset_p7_advisory_value")
        p=c["phase_results"][0];p.update(execute_attempted=True,persisted_rows=0,persistence_source="advisory_no_dml")
        a.rewrite_report("metadata");self.accepted(a)
        p["persistence_source"]="not_executed";a.rewrite_report("metadata");self.rejected(a)

    def test_B03_result_columns_are_not_fixed_parameters(self):
        a=self.fresh();column=a.case("metadata","bind_DateTime2_s7_punset_fixed_value")["column_metadata"][0]
        self.assertEqual(0,column["type_flag"]);self.assertEqual("result_column",column["raw_source"]);self.accepted(a)
        column.update(type_flag=1,raw_source="parameter_describe");a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh(phase="baseline");self.accepted(a)
        a.case("metadata","dual_scale6_bind_DateTime2_s7_punset_fixed_value")["column_metadata"][0]["type_flag"]=1
        a.rewrite_report("metadata");self.rejected(a)

    def test_B04_live_reader_owner_and_fresh_recovery_are_required(self):
        a=self.fresh();phase=a.case("metadata","bind_inferred_sunset_punset_advisory_value")["phase_results"][0]
        phase["statement_after_owner"]="command";a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();phase=a.case("metadata","bind_inferred_sunset_punset_advisory_value")["phase_results"][1]
        phase["prepare_invoked"]=False;a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh(profile="tls",scale8=False);self.accepted(a)
        phase=a.case("metadata","inferred_dt_tail_stability")["phase_results"][2]
        phase["recovery_source"]="none";a.rewrite_report("metadata");self.rejected(a)

    def test_B06_TLS_closed7_does_not_require_seven_columns(self):
        a=self.fresh(profile="tls",scale8=False)
        a.data["core.cases"]=[c for c in a.data["core.cases"] if c["case_id"] not in ("columns_6_7","column_D7","column_Z7")]
        a.rewrite_report("core");self.accepted(a)
        c=a.case("core","dt7_ordinary_micro");c.update(status="written",persisted_rows=0)
        a.rewrite_report("core");self.rejected(a)
        a=self.fresh(profile="tls",scale8=False);a.data["core.cases"].remove(a.case("core","dt6_max_minus9"));a.rewrite_report("core");self.rejected(a)
        a=self.fresh();a.data["core.cases"].remove(a.case("core","column_D7"));a.rewrite_report("core");self.rejected(a)

    def test_B06_TLTZ_extreme_seed_rejection_is_not_all_domain_support(self):
        a=self.fresh(phase="baseline");key="server_seed_TLTZ7_max"
        a.case("extended",key,"server").update(status="failed",classification="server_response_observed",failure_class="verified_server_response",error=dict(type="W.Dm.DmException",number=-6118,verified_server_response=True,failure_kind="Server",phase="Receive",code=None,outcome="ServerReported"))
        read=a.case("extended","tltz_read_7_max");read.update(status="closed",reason="independent_seed_unverified_read_gate_not_accepted")
        a.rewrite_report("extended");self.accepted(a)
        read["status"]="observed";a.rewrite_report("extended");self.rejected(a)

    def test_R07_required_seed_and_measured_layout_mutants_reject(self):
        for key in ("ordinary_date","ordinary_time6","ordinary_time_tz6"):
            with self.subTest(key=key):
                a=self.fresh();a.data["extended.cases"].remove(a.case("extended",key));a.rewrite_report("extended");self.rejected(a)
        mutations={
            "cell not observed":lambda c:c["cell"].update(observable=False,source="unobservable"),
            "cell missing length":lambda c:c["cell"].pop("payload_length"),
            "nominal CType wrong":lambda c:c["metadata"][0].update(c_type=16),
            "payload bool":lambda c:c["cell"].update(payload_length=True),
            "payload unknown layout":lambda c:c["cell"].update(payload_length=13),
            "payload no hash":lambda c:c["cell"].pop("payload_sha256"),
            "BDTA attempt":lambda c:c["cell"].update(is_bdta=True,result_format="bdta"),
            "column format unknown":lambda c:c["metadata"][0].update(result_format="unobservable"),
            "schema scale wrong":lambda c:c["schema_scales"].__setitem__(0,7),
            "completion missing":lambda c:c.update(subsequent_read_completion=False),
            "wrong anchor":lambda c:c["get_value"].update(ticks=0),
        }
        for name,mutate in mutations.items():
            with self.subTest(name=name):
                a=self.fresh();mutate(a.case("extended","ordinary_time6"));a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","ordinary_time_tz6")["typed_offset"]["offset_minutes"]=0;a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","ordinary_time_tz6")["generic"]=clr(calendar_ticks(1900,1,1,12,13,14)+1234560);a.rewrite_report("extended");self.rejected(a)
        a=self.fresh();a.case("extended","ordinary_date","server")["actual_server"]["fields"]["day"]=24;a.rewrite_report("extended");self.rejected(a)

    def test_R07_baseline_getter_failure_is_not_server_unsupported(self):
        a=self.fresh(phase="baseline");read=a.case("extended","ordinary_time_tz6")
        read["get_value"]=dict(outcome="error",error=dict(type="System.NotSupportedException",number=None))
        a.rewrite_report("extended");self.accepted(a)
        result=VALIDATOR.validate(a.directory,"characterization")
        self.assertEqual("sampled_ordinary_layout_verified",result["simple_layout_classifications"]["ordinary_time_tz6"])
        self.assertFalse(result["baseline_capability_acceptance"])
        self.assertFalse(result["wire_layout_verified"])
        a=self.fresh();seed=a.case("extended","ordinary_time_tz6","server")
        seed.update(status="failed",classification="server_unsupported",**error("NotSupportedException","unverified_driver_error"))
        a.rewrite_report("extended");self.rejected(a)

    def test_R07_approved_server_existence_rejection_is_classified_not_layout_verified(self):
        a=self.fresh();key="ordinary_time_tz6"
        cap=a.data["frozen-input/capabilities.json"];cap["profiles"]["shared"]["verified_unsupported_error_numbers"][key]=[-6118]
        a.put("frozen-input/capabilities.json",cap);a.refresh_transport_hashes()
        a.case("extended",key,"server").update(status="failed",classification="server_unsupported",failure_class="verified_server_response",
            error=dict(type="W.Dm.DmException",number=-6118,verified_server_response=True,failure_kind="Server",phase="Receive",code=None,outcome="ServerReported"))
        a.data["extended.cases"]=[c for c in a.data["extended.cases"] if c["case_id"]!=key or c["driver_lane"]=="server"]
        a.rewrite_report("extended");self.accepted(a)
        result=VALIDATOR.validate(a.directory,"candidate")
        self.assertEqual("server_rejection_verified",result["simple_layout_classifications"][key])
        self.assertFalse(result["wire_layout_verified"])

    def test_advisory_generic_raw_describe_and_effective_wire_positive(self):
        a=self.fresh();case=a.case("metadata","bind_inferred_sunset_punset_advisory_value")
        initial=case["phase_results"][0]
        self.assertEqual(2,initial["parameter_metadata"][0]["raw_c_type"])
        self.assertEqual(26,initial["parameter_metadata"][0]["c_type"])
        self.assertEqual(32767,initial["parameter_metadata"][0]["raw_precision"])
        self.assertEqual(9,initial["parameter_metadata"][0]["width"])
        self.accepted(a)

    def test_advisory_raw_snapshot_rewrite_and_source_mutants_reject(self):
        for name,modify in {
            "source swapped":lambda p:p["parameter_metadata"][0].update(raw_source="result_column"),
            "raw overwritten to effective":lambda p:p["parameter_metadata"][0].update(raw_c_type=26,raw_precision=9,raw_scale=7),
            "missing before":lambda p:p.pop("parameter_metadata_before_execute"),
            "false before captured":lambda p:p["parameter_metadata_before_execute"][0].update(raw_scale_status="unobservable"),
            "replacement original drift":lambda p:p["parameter_metadata"][0].update(raw_precision=42),
        }.items():
            with self.subTest(name=name):
                a=self.fresh();phase=a.case("metadata","bind_inferred_sunset_punset_advisory_value")["phase_results"][1]
                modify(phase);a.rewrite_report("metadata");self.rejected(a)
        a=self.fresh();phase=a.case("metadata","inferred_dt_tail_stability")["phase_results"][1]
        phase["parameter_metadata"][0]["raw_c_type"]=26;a.rewrite_report("metadata");self.rejected(a)

    def test_approved_domains_cannot_turn_mandatory_success_into_rejection(self):
        a=self.fresh();cap=a.data["frozen-input/capabilities.json"]
        cap["profiles"]["shared"]["local_ticks_max"]=MAXIMUM-10
        a.put("frozen-input/capabilities.json",cap);a.refresh_transport_hashes()
        for family in ("dt","dto"):
            for tail in ("max","max_minus1","max_minus9"):
                c=a.case("core",f"{family}7_{tail}");c.update(status="driver_write_rejected",persisted_rows=0,**error("OverflowException"))
        a.rewrite_report("core");self.rejected(a)
        a=self.fresh();cap=a.data["frozen-input/capabilities.json"]
        cap["profiles"]["shared"]["local_ticks_min"]=1
        a.put("frozen-input/capabilities.json",cap);a.refresh_transport_hashes()
        c=a.case("core","dt6_min");c.update(status="driver_write_rejected",persisted_rows=0,**error("OverflowException"))
        a.rewrite_report("core");self.rejected(a)

    def test_minimal_handwritten_managed_GUID_and_bounds_contract(self):
        # Hand-built PE/metadata bytes, not copied from or executed as an assembly.
        import uuid
        blob=bytearray(800)
        def put(at,value,size):blob[at:at+size]=value.to_bytes(size,"little")
        blob[:2]=b"MZ";put(0x3c,128,4);blob[128:132]=b"PE\0\0"
        put(134,1,2);put(148,224,2);put(152,0x10b,2)
        put(152+96+14*8,0x2000,4)
        put(376+8,512,4);put(376+12,0x2000,4);put(376+16,512,4);put(376+20,512,4)
        put(512+8,0x2040,4);blob[576:580]=b"BSJB";put(588,4,4);blob[592:596]=b"v4\0\0"
        put(598,2,2);put(600,64,4);put(604,64,4);blob[608:612]=b"#~\0\0"
        put(612,128,4);put(616,16,4);blob[620:628]=b"#GUID\0\0\0"
        put(640+8,1,8);put(640+24,1,4);put(640+28+4,1,2)
        blob[704:720]=uuid.UUID(MANUAL["mvid"]).bytes_le
        self.assertEqual(MANUAL["mvid"],VALIDATOR.C._managed_mvid(bytes(blob)))
        with self.assertRaises(VALIDATOR.C.ContractError):VALIDATOR.C._managed_mvid(bytes(blob[:600]))

    def test_baseline_errors_remain_observations_not_server_capability(self):
        a=self.fresh(phase="baseline");c=a.case("core","dt7_max");c.update(status="driver_write_rejected",persisted_rows=0,**error("InvalidOperationException","unverified_driver_error"));a.rewrite_report("core")
        self.accepted(a);result=VALIDATOR.validate(a.directory,"characterization");self.assertFalse(result["baseline_capability_acceptance"])


if __name__=="__main__":unittest.main()
