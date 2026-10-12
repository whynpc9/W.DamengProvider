"""Independent ordinary packed temporal byte oracle; integers only, no provider/core imports.

Layouts are the separately audited wire positions. This module has no encoder.
12-byte compatibility buffers, BDTA, decorated/TLTZ metadata and unknown layouts stay closed.
"""
import hashlib
import re

LAYOUTS = {14: (3, "DATE3"), 15: (5, "TIME5"), 22: (7, "TIME_TZ7"),
           16: (8, "TS8"), 23: (10, "TS_TZ10"), 26: (9, "TS9"), 27: (11, "TS_TZ11")}


class PacketError(ValueError):
    pass


def require(condition, code):
    if not condition: raise PacketError(code)


def integer(value, code):
    require(type(value) is int, code + ":integer_required")
    return value


def calendar_valid(year, month, day):
    require(1 <= year <= 9999 and 1 <= month <= 12, "ordinary_calendar_domain")
    leap = year % 4 == 0 and (year % 100 != 0 or year % 400 == 0)
    days = (31, 29 if leap else 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31)
    require(1 <= day <= days[month - 1], "ordinary_calendar_day")


def decode_packet(payload, c_type, scale):
    require(type(payload) is bytes, "payload_bytes_required")
    integer(c_type, "CType"); integer(scale, "scale")
    require(c_type in LAYOUTS and 0 <= scale <= 9, "ordinary_layout_or_scale")
    require(len(payload) == LAYOUTS[c_type][0], "ordinary_exact_length")
    has_date = c_type in (14, 16, 23, 26, 27)
    has_time = c_type != 14
    has_offset = c_type in (22, 23, 27)
    year = month = day = None
    hour = minute = second = nanoseconds = 0
    offset = None
    if has_date:
        date_word = int.from_bytes(payload[:3], "little")
        year = date_word & 0x7fff
        if year & 0x4000: year -= 0x8000
        month, day = (date_word >> 15) & 15, (date_word >> 19) & 31
        calendar_valid(year, month, day)
    if has_time:
        start = 3 if has_date else 0
        extended = c_type in (26, 27)
        time_bytes = 6 if extended else 5
        time_word = int.from_bytes(payload[start:start + time_bytes], "little")
        fraction_width = 30 if extended else 20
        require(time_word >> (17 + fraction_width) == 0, "ordinary_reserved_bits")
        hour, minute, second = time_word & 31, (time_word >> 5) & 63, (time_word >> 11) & 63
        fraction = (time_word >> 17) & ((1 << fraction_width) - 1)
        require(0 <= hour <= 23 and 0 <= minute <= 59 and 0 <= second <= 59, "ordinary_clock_domain")
        require(fraction <= (999_999_999 if extended else 999_999), "ordinary_fraction_domain")
        nanoseconds = fraction if extended else fraction * 1000
        require(nanoseconds % (10 ** (9 - scale)) == 0, "ordinary_declared_scale")
        if not extended: require(scale <= 6, "legacy_scale_closed")
    else:
        require(scale == 0, "DATE_scale_zero")
    if has_offset:
        offset = int.from_bytes(payload[-2:], "little", signed=True)
        require(-840 <= offset <= 840, "ordinary_offset_domain")
    return dict(year=year, month=month, day=day, hour=hour, minute=minute, second=second,
                nanoseconds=nanoseconds, offset_minutes=offset)


def decode_observation(raw, metadata, cell):
    require(type(raw) is dict and type(metadata) is dict and type(cell) is dict, "raw_context_objects")
    require(raw.get("observable") is True and raw.get("read_only_synthetic") is True, "raw_cache_observation_required")
    require(raw.get("evidence_role") == "independent_cached_temporal_packed_input" and raw.get("source") == "ordinary_result_cache_before_temporal_projection" and raw.get("capture_stage") == "before_any_temporal_getter", "raw_cache_source")
    c_type = integer(metadata.get("c_type"), "metadata.CType")
    scale = integer(metadata.get("semantic_scale"), "metadata.scale")
    require(c_type in LAYOUTS and metadata.get("mask") == 0 and type(metadata.get("mask")) is int, "decorated_or_unknown_metadata_closed")
    require(integer(metadata.get("width"), "metadata.width") == LAYOUTS[c_type][0], "ordinary_metadata_width")
    require(raw.get("c_type") == c_type and type(raw.get("c_type")) is int and raw.get("semantic_scale") == scale and type(raw.get("semantic_scale")) is int, "raw_metadata_conflict")
    require(raw.get("mask") == metadata.get("mask") and type(raw.get("mask")) is int and raw.get("width") == metadata.get("width") and type(raw.get("width")) is int, "raw_width_mask_conflict")
    require(raw.get("metadata_role") == metadata.get("metadata_role") == "result_column", "raw_result_role_required")
    require(metadata.get("is_bdta") is False and raw.get("column_result_format") == metadata.get("result_format") == "ordinary_packed", "column_format_closed")
    require(raw.get("is_bdta") is False and cell.get("is_bdta") is False and raw.get("result_format") == cell.get("result_format") == "ordinary_non_BDTA", "row_format_closed")
    require(cell.get("observable") is True and cell.get("source") == "already_fetched_cell", "cell_source_required")
    require(raw.get("layout_family") == LAYOUTS[c_type][1], "raw_layout_family_conflict")
    text = raw.get("synthetic_temporal_hex")
    require(type(text) is str and re.fullmatch(r"(?:[0-9A-Fa-f]{2})+", text) is not None, "raw_hex_required")
    payload = bytes.fromhex(text)
    length = integer(raw.get("payload_length"), "raw.length")
    require(length == len(payload) == LAYOUTS[c_type][0] and cell.get("payload_length") == length and type(cell.get("payload_length")) is int, "raw_actual_length_conflict")
    digest = hashlib.sha256(payload).hexdigest()
    require(raw.get("payload_sha256") == cell.get("payload_sha256") == digest, "raw_actual_hash_conflict")
    return decode_packet(payload, c_type, scale)
