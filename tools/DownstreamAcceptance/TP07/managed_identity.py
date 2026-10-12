"""Pure ECMA-335 identity reader; copied from accepted T19/common.py without loading code."""
import struct
import uuid

def mvid(raw):
    """Read ECMA-335 Module.Mvid from an ordinary managed PE, without loading executable code."""
    def u16(offset): return struct.unpack_from('<H', raw, offset)[0]
    def u32(offset): return struct.unpack_from('<I', raw, offset)[0]
    def u64(offset): return struct.unpack_from('<Q', raw, offset)[0]
    pe = u32(0x3c)
    if raw[pe:pe + 4] != b'PE\0\0': raise ValueError('invalid_managed_pe')
    sections = u16(pe + 6); optional_size = u16(pe + 20); optional = pe + 24
    magic = u16(optional)
    directories = optional + (96 if magic == 0x10b else 112 if magic == 0x20b else 0)
    if directories == optional: raise ValueError('invalid_pe_optional_header')
    section_offset = optional + optional_size
    def file_offset(rva):
        for index in range(sections):
            offset = section_offset + index * 40
            address, virtual, size, pointer = u32(offset + 12), u32(offset + 8), u32(offset + 16), u32(offset + 20)
            if address <= rva < address + max(virtual, size):
                result = pointer + rva - address
                if result >= len(raw): raise ValueError('pe_rva_outside_file')
                return result
        raise ValueError('pe_rva_not_mapped')
    cli = file_offset(u32(directories + 14 * 8)); metadata = file_offset(u32(cli + 8))
    if raw[metadata:metadata + 4] != b'BSJB': raise ValueError('invalid_metadata_root')
    cursor = (metadata + 16 + u32(metadata + 12) + 3) & ~3
    streams = u16(cursor + 2); cursor += 4; heaps = {}
    for _ in range(streams):
        offset, length = u32(cursor), u32(cursor + 4); name_start = cursor + 8
        end = raw.index(0, name_start); name = raw[name_start:end].decode('ascii')
        heaps[name] = (metadata + offset, length); cursor = (end + 1 + 3) & ~3
    tables, _ = heaps.get('#~', heaps.get('#-', (0, 0))); guid_start, guid_size = heaps['#GUID']
    if not tables: raise ValueError('metadata_tables_missing')
    heap_flags = raw[tables + 6]; valid = u64(tables + 8); rows = tables + 24; module_rows = None
    for index in range(64):
        if valid & (1 << index):
            if index == 0: module_rows = u32(rows)
            rows += 4
    if module_rows != 1: raise ValueError('metadata_module_row_invalid')
    string_size = 4 if heap_flags & 1 else 2; guid_size_index = 4 if heap_flags & 2 else 2
    field = rows + 2 + string_size; ordinal = u32(field) if guid_size_index == 4 else u16(field)
    if ordinal < 1 or ordinal * 16 > guid_size: raise ValueError('metadata_mvid_index_invalid')
    return str(uuid.UUID(bytes_le=raw[guid_start + (ordinal - 1) * 16:guid_start + ordinal * 16]))

