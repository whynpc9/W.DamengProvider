"""Independent hand-calculated literal goldens and byte mutation contracts; no encoder."""
import copy
import hashlib
import importlib.util
from pathlib import Path
import unittest

HERE=Path(__file__).resolve().parent
s=importlib.util.spec_from_file_location('packed_oracle',HERE/'packed_oracle.py')
P=importlib.util.module_from_spec(s);s.loader.exec_module(P)


def context(literal,ctype=27,scale=7):
    data=bytes.fromhex(literal);digest=hashlib.sha256(data).hexdigest()
    meta=dict(c_type=ctype,semantic_scale=scale,width={14:3,15:5,22:7,16:8,23:10,26:9,27:11}[ctype],mask=0,metadata_role='result_column',is_bdta=False,result_format='ordinary_packed')
    cell=dict(observable=True,source='already_fetched_cell',payload_length=len(data),payload_sha256=digest,is_bdta=False,result_format='ordinary_non_BDTA')
    raw=dict(evidence_role='independent_cached_temporal_packed_input',source='ordinary_result_cache_before_temporal_projection',capture_stage='before_any_temporal_getter',read_only_synthetic=True,observable=True,
        c_type=ctype,semantic_scale=scale,width=meta['width'],mask=0,metadata_role='result_column',is_bdta=False,result_format='ordinary_non_BDTA',column_result_format='ordinary_packed',payload_length=len(data),payload_sha256=digest,
        synthetic_temporal_hex=literal,layout_family=P.LAYOUTS[ctype][1])
    return raw,meta,cell


class PackedOracleContracts(unittest.TestCase):
    def test_official_literal_one_tick_and_offsets(self):
        # T12 O net9 seed packets, preserved literals; do not generate from expected fields.
        literals=[('EA87BBEE81C8000000',26,None),('EA87BBEE81C80000000000',27,0),('EA87BBEE81C8000000E001',27,480),('EA87BBEE81C8000000D4FE',27,-300),('EA87BBEE81C80000004A01',27,330)]
        for literal,ctype,offset in literals:
            decoded=P.decode_observation(*context(literal,ctype))
            self.assertEqual(dict(year=2026,month=7,day=23,hour=14,minute=15,second=16,nanoseconds=100,offset_minutes=offset),decoded)

    def test_hand_calendar_clock_micro_and_max_literal(self):
        vectors=[('EA87BB',14,0,dict(year=2026,month=7,day=23,hour=0,minute=0,second=0,nanoseconds=0,offset_minutes=None)),
            ('0000000000',15,6,dict(year=None,month=None,day=None,hour=0,minute=0,second=0,nanoseconds=0,offset_minutes=None)),
            ('EA87BBEE8180C403',16,6,dict(year=2026,month=7,day=23,hour=14,minute=15,second=16,nanoseconds=123456000,offset_minutes=None)),
            ('0F27FE77DF399335770000',27,7,dict(year=9999,month=12,day=31,hour=23,minute=59,second=59,nanoseconds=999999900,offset_minutes=0))]
        for literal,ctype,scale,expected in vectors:self.assertEqual(expected,P.decode_observation(*context(literal,ctype,scale)))

    def test_declared_eight_digit_raw_fraction_does_not_require_CLR_tick(self):
        decoded=P.decode_observation(*context("EA87BBEE8114000000",26,8))
        self.assertEqual(10,decoded["nanoseconds"])
        self.assertNotEqual(0,decoded["nanoseconds"]%100)
        with self.assertRaises(P.PacketError):P.decode_observation(*context("EA87BBEE8102000000",26,8))

    def test_malformed_reserved_length_scale_hash_metadata_reject(self):
        raw,meta,cell=context('EA87BBEE81C80000004A01')
        changes=[lambda r,m,c:r.update(payload_sha256='0'*64),lambda r,m,c:r.update(payload_length=10),
            lambda r,m,c:r.update(synthetic_temporal_hex='EA87BBEE81C80000804A01'),lambda r,m,c:r.update(semantic_scale=6),
            lambda r,m,c:m.update(c_type=23),lambda r,m,c:m.update(mask=4),lambda r,m,c:r.update(source='formatter_generated'),
            lambda r,m,c:c.update(payload_sha256='0'*64),lambda r,m,c:r.update(capture_stage='after_getter'),lambda r,m,c:r.update(metadata_role='parameter_describe'),lambda r,m,c:r.update(observable=False)]
        for mutate in changes:
            r,m,c=copy.deepcopy((raw,meta,cell));mutate(r,m,c)
            with self.assertRaises(P.PacketError):P.decode_observation(r,m,c)
        # Valid recomputed SHA cannot hide reserved bits, bad day/time or declared scale corruption.
        for literal,ctype,scale in [('EA87BBEE81C80000804A01',27,7),('EA87BBEE81C80000004A01',27,6),('EA8700EE81C80000004A01',27,7),('EA87BBFF81C80000004A01',27,7),('EA87BBEE8180C4E3',16,6),('EA87BBEE81C80000004A0100',27,7)]:
            with self.assertRaises(P.PacketError):P.decode_observation(*context(literal,ctype,scale))

    def test_raw_and_metadata_width_drift_together_is_rejected(self):
        raw,meta,cell=context("EA87BBEE81C80000004A01")
        # Hash/hex/cell length remain a valid TS_TZ11 packet; agreement alone is insufficient.
        raw["width"]=10;meta["width"]=10
        with self.assertRaises(P.PacketError):P.decode_observation(raw,meta,cell)

    def test_tick_offset_and_last_day_mutations_are_not_same_expected(self):
        expected=P.decode_packet(bytes.fromhex('0F27FE77DF399335770000'),27,7)
        for literal in ('0F27FE77DF719235770000','0F27F677DF399335770000','0F27FE77DF3993357730FF'):
            decoded=P.decode_observation(*context(literal))
            self.assertNotEqual(expected,decoded)
        original=P.decode_packet(bytes.fromhex('EA87BBEE81C80000004A01'),27,7)
        changed=P.decode_packet(bytes.fromhex('EA87BBEE81CA0000004A01'),27,9)
        self.assertEqual(101,changed['nanoseconds']);self.assertNotEqual(original,changed)


if __name__=='__main__':unittest.main()
