"""Frozen TP-01 artifact and semantic contracts. Pure: no processes, DB or secrets.

This module is the single policy source shared by the executor and validator.
Handwritten offline test artifacts are separate from probe/run evidence.
"""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
from datetime import datetime, timedelta

SCHEMA_VERSION = 1
PROFILES = {"shared": "8.1.5.60", "tls": "8.1.4.6"}
LANES = ("core", "metadata", "extended")
DRIVER_LANES = ("O", "W", "server")
PHASES = ("baseline", "candidate")
MAX_TICKS = 3155378975999999999
TICKS_SECOND = 10_000_000
TICKS_MINUTE = 600_000_000
ORDINARY = 639204129160000000
O_PIN = dict(id="DM.DmProvider", version="8.3.1.47463", asset="lib/net9.0/DM.DmProvider.dll",
    package_sha256="62ec22acef319847ee59610359f02f1d3e08dc76af39b92ba50b23cc25e22e75",
    dll_sha256="8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b",
    mvid="d909b5af-965f-4cf9-a80a-a6181124e361")
W_PIN = dict(id="W.DmProvider", version="0.1.0-r3.20261007143838", asset="lib/net10.0/W.DmProvider.dll",
    package_sha256="6696a26d89e4caa1b9e7c40a73f024c4df9d8a695da1446bdbecc17757d3bcf9",
    dll_sha256="c70f7d4b7bd37187f55483d186d658206232a2e1f60a662530b3a0282e3231e5",
    mvid="69e5632e-75c4-4672-b924-57b8ffb58a52")
V23_SOURCE_HEAD = "2b39fec0fa1bc8974f8e491f9bdb5a6113d14631"
V23_SOURCE_SHA = "241e15b52b436a55fe3a299e777b281cee0d6b3c22782a0323de311e1d87d1d5"
DECLARATIONS = ("inferred", "inferred_dto", "DateTime", "DateTime2", "DateTimeOffset", "provider", "provider_dto")
SCALES = (None, 0, 3, 6, 7, 8, 9, 19, 23, 255)
FAILURE_CLASSES = ("user_rejection", "verified_server_response", "unverified_driver_error", "infra_failure", "clr_precondition")
CASE_STATUSES = ("written", "driver_write_rejected", "constructor_rejected_before_driver", "observed",
    "driver_parameter_rejected", "value_replacement_observed", "replacement_rejected", "rollback_verified",
    "server_columns_created", "server_column_created", "server_expression_evaluated", "closed", "failed",
    "seeded", "seed_rejected", "server_seed_created", "layout_unavailable")
HASH = re.compile(r"[a-f0-9]{64}")


class ContractError(ValueError):
    """Only a safe contract code, never producer text/SQL/connection data."""


def require(condition, code):
    if not condition:
        raise ContractError(code)


def true_int(value, code, minimum=None, maximum=None):
    require(type(value) is int, code + ":integer_required")
    require(minimum is None or value >= minimum, code + ":below_minimum")
    require(maximum is None or value <= maximum, code + ":above_maximum")
    return value


def boolean(value, code):
    require(type(value) is bool, code + ":boolean_required")
    return value


def string(value, code, nonempty=True):
    require(type(value) is str and (not nonempty or bool(value)), code + ":string_required")
    return value


def hash_value(value, code):
    require(type(value) is str and HASH.fullmatch(value) is not None, code + ":sha256_required")
    return value


def object_value(value, code):
    require(type(value) is dict, code + ":object_required")
    return value


def array(value, code, nonempty=False):
    require(type(value) is list and (not nonempty or bool(value)), code + ":array_required")
    return value


def member(value, key, code):
    object_value(value, code)
    require(key in value, code + ":missing_" + key)
    return value[key]


def enum(value, values, code):
    require(type(value) is str and value in values, code + ":unknown_enum")
    return value


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate_json_key")
        result[key] = value
    return result


def _constant(_):
    raise ContractError("non_finite_json_number")


def loads_strict(text):
    return json.loads(text, object_pairs_hook=_pairs, parse_constant=_constant)


def load_json(path):
    return loads_strict(Path(path).read_text())


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def relative_path(value, code):
    string(value, code)
    path = PurePosixPath(value)
    require(not path.is_absolute() and ".." not in path.parts and "\\" not in value and str(path) == value,
            code + ":relative_normalized_path_required")
    require(not any(part in (".local", "secrets") for part in path.parts), code + ":ignored_secret_path")
    return value


def source_head(value):
    heads = [value[key] for key in ("head", "source_head", "source_commit") if key in value]
    require(bool(heads) and all(type(x) is str and re.fullmatch(r"[a-f0-9]{40}", x) for x in heads), "source_head")
    require(len(set(heads)) == 1, "source_head_conflict")
    return heads[0]


def validate_source_manifest(value):
    object_value(value, "product_source_manifest")
    true_int(member(value, "schema_version", "source"), "source.schema_version", 1, 1)
    source_head(value)
    files = array(member(value, "files", "source"), "source.files", nonempty=True)
    paths = set()
    for entry in files:
        path = relative_path(member(entry, "path", "source.file"), "source.file.path")
        require(path not in paths, "source.duplicate_path")
        paths.add(path)
        hash_value(member(entry, "sha256", "source.file"), "source.file.sha256")
        if "size" in entry:
            true_int(entry["size"], "source.file.size", 0)
        if "bytes" in entry:
            true_int(entry["bytes"], "source.file.bytes", 0)
        if "executable" in entry:
            boolean(entry["executable"], "source.file.executable")
    require(any(p.startswith("src/W.DmProvider/") for p in paths), "source.product_files_required")
    return value


def validate_identity(identity):
    object_value(identity, "identity")
    true_int(member(identity, "schema_version", "identity"), "identity.schema_version", 1, 1)
    phase = enum(member(identity, "phase", "identity"), PHASES, "identity.phase")
    require(member(identity, "O", "identity") == O_PIN, "official_pin_changed")
    w = object_value(member(identity, "W", "identity"), "identity.W")
    require(w.get("id") == "W.DmProvider" and w.get("asset") == "lib/net10.0/W.DmProvider.dll", "W_asset")
    require(type(w.get("version")) is str and re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]{0,100}", w["version"]), "W_version")
    hash_value(member(w, "package_sha256", "W"), "W.package")
    hash_value(member(w, "dll_sha256", "W"), "W.dll")
    require(type(w.get("mvid")) is str and re.fullmatch(r"[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}", w["mvid"]), "W_mvid")
    if phase == "baseline":
        kind = enum(member(identity, "baseline_kind", "identity"), ("archived_v23", "rebuilt_baseline"), "baseline_kind")
        require((w["version"] == W_PIN["version"]) == (kind == "archived_v23"), "baseline_version_kind")
        if kind == "archived_v23":
            require(w == W_PIN, "archived_v23_pin_changed")
    else:
        require(w["version"] != W_PIN["version"], "unique_candidate_version_required")
    return identity


def validate_producer(identity, source_sha, source_value, archive_sha=None):
    validate_identity(identity)
    validate_source_manifest(source_value)
    hash_value(source_sha, "actual_source_hash")
    producer = object_value(member(identity, "producer", "identity"), "producer")
    require(member(producer, "source_head", "producer") == source_head(source_value), "producer_source_head")
    require(member(producer, "source_manifest_sha256", "producer") == source_sha, "producer_source_manifest_hash")
    for key in ("version", "package_sha256", "dll_sha256", "mvid"):
        require(member(producer, key, "producer") == identity["W"][key], "producer_package_binding:" + key)
    archive = member(producer, "source_archive_sha256", "producer")
    if archive is not None:
        hash_value(archive, "producer_archive")
        require(archive_sha == archive, "producer_source_archive_hash")
    else:
        require(archive_sha is None, "producer_unexpected_archive")
    if identity.get("baseline_kind") == "archived_v23":
        require(source_sha == V23_SOURCE_SHA and producer["source_head"] == V23_SOURCE_HEAD, "v23_exact_producer")
    return producer


def validate_capabilities(value, identity, profile, _all_profiles=True):
    enum(profile, PROFILES, "profile")
    object_value(value, "capabilities")
    true_int(member(value, "schema_version", "capabilities"), "capabilities.schema_version", 1, 1)
    producer = member(identity, "producer", "identity")
    require(member(value, "source_manifest_sha256", "capabilities") == producer["source_manifest_sha256"], "capability_source_hash")
    require(member(value, "source_commit", "capabilities") == producer["source_head"], "capability_source_head")
    require(member(value, "w_package_sha256", "capabilities") == identity["W"]["package_sha256"], "capability_package")
    profiles = object_value(member(value, "profiles", "capabilities"), "capabilities.profiles")
    require(set(profiles) <= set(PROFILES) and profile in profiles, "capability_profile_enum")
    if _all_profiles:
        for other in profiles:
            if other != profile:
                validate_capabilities(value, identity, other, _all_profiles=False)
    selected = object_value(profiles[profile], "capability.profile")
    require(member(selected, "server_version", "capability") == PROFILES[profile], "capability_server_version")
    require(boolean(member(selected, "root_approved", "capability"), "root_approved") is True, "capability_root_approval")
    string(member(selected, "evidence_id", "capability"), "capability.evidence_id")
    seven = boolean(member(selected, "seven_write", "capability"), "seven_write")
    scales = array(member(selected, "write_scales", "capability"), "write_scales", nonempty=True)
    for scale in scales:
        true_int(scale, "write_scale", 0, 9)
    require(len(set(scales)) == len(scales) and 6 in scales, "capability_scale_set")
    require((7 in scales) == seven and (profile != "shared" or seven), "mandatory_seven_capability")
    # TLTZ remains a separate closed capability in this TP01 tool version.
    require(boolean(member(selected, "tltz_write", "capability"), "tltz_write") is False, "unapproved_tltz_write")
    require(boolean(member(selected, "tltz_read", "capability"), "tltz_read") is False, "unapproved_tltz_read")
    for key in ("local_ticks_min", "local_ticks_max", "utc_ticks_min", "utc_ticks_max"):
        true_int(member(selected, key, "capability"), key, 0, MAX_TICKS)
    require(selected["local_ticks_min"] <= selected["local_ticks_max"] and selected["utc_ticks_min"] <= selected["utc_ticks_max"], "capability_domain_order")
    for key in ("offset_min_minutes", "offset_max_minutes"):
        true_int(member(selected, key, "capability"), key, -840, 840)
    require(selected["offset_min_minutes"] <= selected["offset_max_minutes"], "capability_offset_order")
    return selected


def datetime_ticks(year, month, day, hour=0, minute=0, second=0):
    days = datetime(year, month, day).toordinal() - 1
    return ((days * 24 + hour) * 60 * 60 + minute * 60 + second) * TICKS_SECOND


# Numeric constants/case IDs are frozen independently of report.expected.
VECTORS = {
    "min": 0, "min_plus1": 1, "max": MAX_TICKS, "max_minus1": MAX_TICKS - 1,
    "max_minus9": MAX_TICKS - 9, "ordinary_tick1": ORDINARY + 1,
    "ordinary_micro": ORDINARY + 1_234_560,
    "century_leap": datetime_ticks(2000, 2, 29, 23, 59, 59) + 9_999_999,
    "century_nonleap": datetime_ticks(1900, 3, 1), "midnight": datetime_ticks(2026, 7, 24),
    **{"tail_" + str(i): ORDINARY + i for i in range(10)},
}
OFFSETS = (0, 480, -300, 330, -840, -780, -779, 840)


def value_spec(ticks, offset=None, kind="Unspecified"):
    return {"ticks": ticks, "offset_minutes": offset, "kind": None if offset is not None else kind,
            "type": "System.DateTimeOffset" if offset is not None else "System.DateTime"}


def core_specs():
    result = {}
    for name, ticks in VECTORS.items():
        for family, offset in (("dt", None), ("dto", 0)):
            for scale in (6, 7):
                result[f"{family}{scale}_{name}"] = {"value": value_spec(ticks, offset), "scale": scale}
    for kind in ("Unspecified", "Utc", "Local"):
        result["kind_" + kind] = {"value": value_spec(ORDINARY + 1, kind=kind), "scale": 7}
    for offset in OFFSETS:
        suffix = str(offset).replace("-", "N")
        result["offset_" + suffix] = {"value": value_spec(ORDINARY + 1, offset), "scale": 7}
        for name, ticks in (("min", 0), ("max", MAX_TICKS)):
            valid_local = 0 <= ticks - offset * TICKS_MINUTE <= MAX_TICKS
            local_id = ("local_" if valid_local else "constructor_") + name + "_" + suffix
            result[local_id] = {"constructor": not valid_local, "value": value_spec(ticks, offset), "scale": 7}
            local = ticks + offset * TICKS_MINUTE
            valid_utc = 0 <= local <= MAX_TICKS
            utc_id = ("utc_" if valid_utc else "utc_constructor_") + name + "_" + suffix
            result[utc_id] = {"constructor": not valid_utc, "value": value_spec(local, offset), "scale": 7}
    return result


def binding_specs():
    result = {}
    for declaration in DECLARATIONS:
        axes = {(scale, None, False, False) for scale in SCALES}
        axes |= {(None, p, False, False) for p in (0, 7, 9)}
        axes |= {(7, None, fixed, null) for fixed in (False, True) for null in (False, True) if fixed or null}
        axes |= {(None, p, True, True) for p in (0, 7, 9)}
        # Nonzero precision must be exercised in fixed non-NULL and advisory NULL too.
        axes |= {(None, p, fixed, null) for p in (7, 9) for fixed, null in ((True, False), (False, True))}
        for scale, precision, fixed, null in axes:
            name = f"bind_{declaration}_s{scale if scale is not None else 'unset'}_p{precision if precision is not None else 'unset'}_{'fixed' if fixed else 'advisory'}_{'null' if null else 'value'}"
            result[name] = dict(declaration=declaration, scale=scale, precision=precision, fixed=fixed, null=null)
    return result


def write_scale_supported(scale, capability):
    # Verified legacy6 includes exact lower-scale quantization. 8/9 are separate approvals.
    return type(scale) is int and ((0 <= scale <= 6 and 6 in capability["write_scales"]) or scale in capability["write_scales"])


def binding_expectation(spec, capability, column=None):
    """Frozen v2 stable selection and exact rejection, without reading report.expected."""
    declaration, scale, precision, fixed, null = (spec[x] for x in ("declaration", "scale", "precision", "fixed", "null"))
    inferred = declaration.startswith("inferred")
    tz = declaration in ("inferred_dto", "DateTimeOffset", "provider_dto")
    if inferred and null and not fixed:
        return {"error": "System.InvalidOperationException", "type_source": "untyped_null"}
    source = "fixed_describe" if inferred and null else "inferred" if inferred else "provider" if declaration.startswith("provider") else "user_db_type"
    if scale is not None and scale > 9:
        return {"error": "System.ArgumentOutOfRangeException", "type_source": source}
    if precision not in (None, 0):
        return {"error": "System.NotSupportedException", "type_source": source}
    legacy, extended = (23, 27) if tz else (16, 26)
    if fixed:
        require(column is not None, "fixed_column_missing")
        ctype, column_scale = column["c_type"], column["semantic_scale"]
        chosen = column_scale if scale is None else scale
        if chosen > (6 if ctype in (16, 23) else 9) or not write_scale_supported(chosen, capability):
            return {"error": "System.NotSupportedException", "type_source": source}
    else:
        chosen = scale
        if chosen is None:
            chosen = 7 if (inferred or declaration == "DateTime2") and capability["seven_write"] else 6
            if declaration == "DateTime2" and not capability["seven_write"]:
                return {"error": "System.NotSupportedException", "type_source": source}
        if not write_scale_supported(chosen, capability):
            return {"error": "System.NotSupportedException", "type_source": source}
        ctype, column_scale = (legacy if chosen <= 6 else extended), None
    quantum = 10 ** max(0, 7 - chosen)
    if not null and (1_234_560 % quantum or (column_scale is not None and 1_234_560 % 10 ** max(0, 7 - column_scale))):
        return {"error": "System.OverflowException", "type_source": source}
    return dict(c_type=ctype, scale=chosen, width={16: 8, 23: 10, 26: 9, 27: 11}[ctype], column_scale=column_scale, type_source=source)


def ticks_fields(ticks):
    true_int(ticks, "ticks", 0, MAX_TICKS)
    seconds, fraction = divmod(ticks, TICKS_SECOND)
    days, seconds = divmod(seconds, 86400)
    date = datetime(1, 1, 1) + timedelta(days=days)
    hour, seconds = divmod(seconds, 3600)
    minute, second = divmod(seconds, 60)
    return (date.year, date.month, date.day, hour, minute, second, fraction * 100)


def parse_temporal_text(text):
    string(text, "temporal_text")
    m = re.fullmatch(r"\s*(\d{1,4})[-/](\d{1,2})[-/](\d{1,2})[ T](\d{1,2}):(\d{1,2}):(\d{1,2})(?:\.(\d{1,9}))?(?:\s+([+-])(\d{1,2}):(\d{2}))?\s*", text)
    require(m is not None, "temporal_text_format")
    fields = tuple(int(x) for x in m.groups()[:6]) + (int((m.group(7) or "").ljust(9, "0")),)
    offset = None if m.group(8) is None else (1 if m.group(8) == "+" else -1) * (int(m.group(9)) * 60 + int(m.group(10)))
    return fields, offset


def parse_temporal_fields(text):
    """Raw fields for independent DATE/TIME seeds; TIME has no implicit date here."""
    string(text, "raw_temporal_text")
    match = re.fullmatch(r"\s*(?:(-?\d{1,6})[-/](\d{1,2})[-/](\d{1,2})[ T])?(\d{1,2}):(\d{1,2}):(\d{1,2})(?:\.(\d{1,9}))?(?:\s+([+-])(\d{1,2}):(\d{2}))?\s*", text)
    if match is not None:
        calendar = tuple(int(match.group(i)) for i in (1, 2, 3)) if match.group(1) is not None else (None, None, None)
        clock = tuple(int(match.group(i)) for i in (4, 5, 6)) + (int((match.group(7) or "").ljust(9, "0")),)
        offset = None if match.group(8) is None else (1 if match.group(8) == "+" else -1) * (int(match.group(9)) * 60 + int(match.group(10)))
        return calendar + clock, offset
    match = re.fullmatch(r"\s*(-?\d{1,6})[-/](\d{1,2})[-/](\d{1,2})\s*", text)
    require(match is not None, "raw_temporal_text_format")
    return tuple(int(match.group(i)) for i in (1, 2, 3)) + (0, 0, 0, 0), None


def _managed_mvid(data):
    """Read the Module.Mvid GUID from a managed PE, without loading executable code."""
    import struct
    import uuid

    def take(offset, size):
        require(type(offset) is int and offset >= 0 and offset + size <= len(data), "managed_PE_bounds")
        return data[offset:offset + size]

    def number(offset, size):
        return int.from_bytes(take(offset, size), "little")

    require(take(0, 2) == b"MZ", "managed_PE_DOS")
    pe = number(0x3C, 4)
    require(take(pe, 4) == b"PE\0\0", "managed_PE_signature")
    section_count, optional_size = number(pe + 6, 2), number(pe + 20, 2)
    optional = pe + 24
    magic = number(optional, 2)
    require(magic in (0x10B, 0x20B), "managed_PE_optional_header")
    directory_offset = 96 if magic == 0x10B else 112
    require(optional_size >= directory_offset + 15 * 8, "managed_PE_CLR_directory")
    sections = optional + optional_size

    def rva(value):
        for index in range(section_count):
            section = sections + index * 40
            virtual_size, virtual_base = number(section + 8, 4), number(section + 12, 4)
            raw_size, raw_base = number(section + 16, 4), number(section + 20, 4)
            if virtual_base <= value < virtual_base + max(virtual_size, raw_size):
                delta = value - virtual_base
                require(delta < raw_size, "managed_PE_virtual_only_RVA")
                return raw_base + delta
        raise ContractError("managed_PE_RVA_unmapped")

    cli = rva(number(optional + directory_offset + 14 * 8, 4))
    metadata = rva(number(cli + 8, 4))
    require(take(metadata, 4) == b"BSJB", "managed_metadata_signature")
    version_length = number(metadata + 12, 4)
    cursor = metadata + 16 + ((version_length + 3) & ~3)
    stream_count = number(cursor + 2, 2)
    cursor += 4
    streams = {}
    for _ in range(stream_count):
        offset, size = number(cursor, 4), number(cursor + 4, 4)
        name_start = cursor + 8
        name_end = name_start
        while take(name_end, 1) != b"\0":
            require(name_end - name_start < 32, "managed_stream_name")
            name_end += 1
        name = take(name_start, name_end - name_start).decode("ascii")
        require(name not in streams, "managed_duplicate_stream")
        take(metadata + offset, size)
        streams[name] = (metadata + offset, size)
        cursor = name_start + ((name_end - name_start + 1 + 3) & ~3)
    require("#GUID" in streams and ("#~" in streams or "#-" in streams), "managed_metadata_streams")
    tables, tables_size = streams.get("#~", streams.get("#-"))
    heaps = number(tables + 6, 1)
    valid = number(tables + 8, 8)
    require(valid & 1, "managed_Module_table_missing")
    cursor = tables + 24
    module_rows = None
    for table in range(64):
        if valid & (1 << table):
            rows = number(cursor, 4)
            if table == 0: module_rows = rows
            cursor += 4
    require(module_rows == 1, "managed_Module_row_count")
    string_size, guid_size = (4 if heaps & 1 else 2), (4 if heaps & 2 else 2)
    require(cursor + 2 + string_size + 3 * guid_size <= tables + tables_size, "managed_Module_bounds")
    mvid_index = number(cursor + 2 + string_size, guid_size)
    guid_base, guid_bytes = streams["#GUID"]
    require(mvid_index >= 1 and mvid_index * 16 <= guid_bytes, "managed_Mvid_index")
    return str(uuid.UUID(bytes_le=take(guid_base + (mvid_index - 1) * 16, 16)))


def validate_package_identity(package_path, identity):
    """Pure read-only package->asset SHA/MVID check; does not execute or extract DLLs."""
    import zipfile
    import xml.etree.ElementTree as ET
    expected = object_value(identity, "package_identity")
    require(digest(package_path) == hash_value(member(expected, "package_sha256", "package"), "package.sha"), "actual_package_hash")
    with zipfile.ZipFile(package_path) as package:
        names = package.namelist()
        asset = member(expected, "asset", "package")
        require(names.count(asset) == 1, "exact_package_asset_required")
        data = package.read(asset)
        require(hashlib.sha256(data).hexdigest() == expected["dll_sha256"], "actual_package_DLL_hash")
        mvid = _managed_mvid(data)
        require(mvid == expected["mvid"], "actual_package_MVID")
        nuspecs = [name for name in names if name.endswith(".nuspec") and "/" not in name]
        require(len(nuspecs) == 1, "single_package_nuspec")
        xml = ET.fromstring(package.read(nuspecs[0]))
        def field(name):
            found = [node.text for node in xml.iter() if node.tag.rsplit("}", 1)[-1] == name]
            require(len(found) == 1, "nuspec_field:" + name)
            return found[0]
        require(field("id") == expected["id"] and field("version") == expected["version"], "actual_package_id_version")
    return dict(package_sha256=expected["package_sha256"], dll_sha256=expected["dll_sha256"], mvid=mvid)


def verify_source_package(identities_path, source_manifest_path, package_path, source_archive_path=None):
    """Low's mandatory real archived/rebuilt/candidate producer preflight, no DB/build."""
    identity, source = load_json(identities_path), load_json(source_manifest_path)
    source_sha = digest(source_manifest_path)
    producer = validate_producer(identity, source_sha, source,
        digest(source_archive_path) if source_archive_path is not None else None)
    package = validate_package_identity(package_path, identity["W"])
    return dict(schema_version=1, task="TP-01", status="source_package_chain_verified",
        phase=identity["phase"], baseline_kind=identity.get("baseline_kind"), source_head=producer["source_head"],
        source_manifest_sha256=source_sha, source_archive_sha256=producer["source_archive_sha256"],
        package_identity=package, candidate_acceptance=False, full_temporal_acceptance=False,
        operation="read_only_no_managed_code_execution")


def _main():
    import argparse
    import sys
    parser = argparse.ArgumentParser(description="Read-only real producer/source/package preflight; not a DB gate")
    parser.add_argument("--identities", required=True, type=Path)
    parser.add_argument("--source-manifest", required=True, type=Path)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--source-archive", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    require(not args.output.exists(), "preflight_requires_new_output")
    try:
        result = verify_source_package(args.identities, args.source_manifest, args.package, args.source_archive)
        code = 0
    except ContractError as error:
        result = dict(schema_version=1, status="source_package_chain_failed", issues=[str(error)], candidate_acceptance=False)
        code = 1
    except Exception as error:
        result = dict(schema_version=1, status="source_package_chain_failed", issues=["artifact_error:" + type(error).__name__], candidate_acceptance=False)
        code = 1
    args.output.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(dict(status=result["status"])))
    return code


if __name__ == "__main__":
    raise SystemExit(_main())
