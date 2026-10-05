# SPDX-License-Identifier: MIT
"""Font-program checks copied from the verified Fullbleed 2.5.7 release checker.

https://github.com/fullbleed-engine/fullbleed-official/blob/v2.5.7/tools/smoke_font_subsets.py
Development-only; this module is not part of the NuGet package.
"""
from io import BytesIO
import struct
from fontTools.ttLib import TTFont


def fonts_in(resources_dict, seen=None):
    seen = set() if seen is None else seen
    resources_dict = resources_dict.get_object()
    fonts = resources_dict.get('/Font', {})
    fonts = fonts.get_object() if hasattr(fonts, 'get_object') else fonts
    for ref in fonts.values():
        font = ref.get_object()
        identity = getattr(ref, 'idnum', id(font))
        if identity in seen:
            continue
        seen.add(identity)
        for descendant in font.get('/DescendantFonts', [font]):
            descendant = descendant.get_object()
            descriptor = descendant.get('/FontDescriptor')
            if descriptor and '/FontFile2' in descriptor.get_object():
                yield font, descendant, descriptor.get_object()['/FontFile2'].get_object()
    xobjects = resources_dict.get('/XObject', {})
    xobjects = xobjects.get_object() if hasattr(xobjects, 'get_object') else xobjects
    for ref in xobjects.values():
        obj = ref.get_object()
        if obj.get('/Subtype') == '/Form' and '/Resources' in obj:
            yield from fonts_in(obj['/Resources'], seen)


def used_glyphs(font, descendant, original):
    used = {0}
    if '/W' in descendant:
        widths = descendant['/W']
        index = 0
        while index < len(widths):
            first = int(widths[index])
            value = widths[index + 1]
            if isinstance(value, list):
                used.update(range(first, first + len(value)))
                index += 2
            else:
                used.update(range(first, int(value) + 1))
                index += 3
    else:
        assert font['/Subtype'] == '/TrueType' and font['/Encoding'] == '/WinAnsiEncoding'
        cmap = original.getBestCmap()
        for code in range(256):
            try:
                character = bytes([code]).decode('cp1252')
            except UnicodeDecodeError:
                continue
            if ord(character) in cmap:
                used.add(original.getGlyphID(cmap[ord(character)]))
    order = original.getGlyphOrder()
    pending = list(used)
    while pending:
        gid = pending.pop()
        glyph = original['glyf'][order[gid]]
        if glyph.isComposite():
            for component in glyph.components:
                child = original.getGlyphID(component.glyphName)
                if child not in used:
                    used.add(child)
                    pending.append(child)
    return used


def check_font(font, descendant, stream, source, legacy):
    data = stream.get_data()
    original = TTFont(source)
    embedded = TTFont(BytesIO(data), checkChecksums=2)
    assert embedded['maxp'].numGlyphs == original['maxp'].numGlyphs
    assert embedded.reader['name'] == original.reader['name'], 'Font notices/names changed'
    padded = data + b'\0' * ((-len(data)) % 4)
    assert sum(struct.unpack(f'>{len(padded) // 4}I', padded)) & 0xffffffff == 0xb1b0afba
    keep = used_glyphs(font, descendant, original)
    before_order, after_order = original.getGlyphOrder(), embedded.getGlyphOrder()
    before_glyf, after_glyf = original.reader['glyf'], embedded.reader['glyf']
    before_loca, after_loca = original['loca'].locations, embedded['loca'].locations
    for gid in keep:
        before = before_glyf[before_loca[gid]:before_loca[gid + 1]]
        after = after_glyf[after_loca[gid]:after_loca[gid + 1]]
        assert before.rstrip(b'\0') == after.rstrip(b'\0'), f'Outline/instructions changed: {gid}'
        assert original['hmtx'][before_order[gid]] == embedded['hmtx'][after_order[gid]], f'Metrics changed: {gid}'
    for old_table, new_table in zip(original['cmap'].tables, embedded['cmap'].tables, strict=True):
        assert (old_table.platformID, old_table.platEncID, old_table.format) == (
            new_table.platformID, new_table.platEncID, new_table.format)
        if not hasattr(old_table, 'cmap'):
            continue
        for point, name in old_table.cmap.items():
            gid = original.getGlyphID(name)
            if gid in keep:
                assert point in new_table.cmap, f'Character mapping lost: {point}'
                assert embedded.getGlyphID(new_table.cmap[point]) == gid
    if not legacy:
        assert embedded['post'].formatType == 3 and len(embedded.reader['post']) == 32, 'Embedded post metadata was not compacted'
        fields = ['advanceWidthMax', 'minLeftSideBearing', 'minRightSideBearing', 'xMaxExtent']
        original_header = tuple(getattr(embedded['hhea'], name) for name in fields)
        embedded['hhea'].recalc(embedded)
        assert original_header == tuple(getattr(embedded['hhea'], name) for name in fields)
        long_count = embedded['hhea'].numberOfHMetrics
        shared = any(gid >= long_count for gid in keep)
        for gid in range(embedded['maxp'].numGlyphs):
            if gid in keep:
                continue
            advance, bearing = embedded['hmtx'][after_order[gid]]
            assert bearing == 0
            if gid < long_count - 1 or (gid == long_count - 1 and not shared):
                assert advance == 0
    result = {'original_bytes': source.stat().st_size, 'program_bytes': len(data),
              'encoded_bytes': len(stream._data), 'retained_glyphs_and_components': len(keep),
              'tables': {tag: embedded.reader.tables[tag].length for tag in embedded.reader.keys()},
              'original_glyph_ids_outlines_metrics_and_mappings_preserved': True,
              'font_name_and_notice_table_preserved': True}
    original.close()
    embedded.close()
    return result
