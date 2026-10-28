# SPDX-License-Identifier: MIT
"""Independent test-only inspection helpers from the core 2.5.40 decoration probe."""
# Source: https://github.com/fullbleed-engine/fullbleed-official/blob/v2.5.40/tools/smoke_text_decoration_spaces.py
# Source SHA-256: bf51bf16ac91370ed69795ee4ed1efff54705ad8d1de16a9820f5314731f524e
from hashlib import sha256
import pypdfium2 as pdfium

def groups(values):
    result = []
    for value in sorted(set(values)):
        if not result or value > result[-1][-1] + 1:
            result.append([value])
        else:
            result[-1].append(value)
    return result

def check_image(image, case):
    rgb = image.convert('RGB')
    red = {}
    data = rgb.tobytes()
    stride = rgb.width * 3
    for y in range(rgb.height):
        row = data[y * stride:(y + 1) * stride]
        xs = [offset // 3 for offset in range(0, stride, 3)
              if row[offset] > 140 and row[offset + 1] < 80 and row[offset + 2] < 80]
        if xs:
            red[y] = xs
    bands = groups(red)
    assert len(bands) == case['bands'], (case['name'], 'decoration bands', len(bands), case['bands'])
    bounds = []
    for band in bands:
        intervals = groups(x for y in band for x in red[y])
        assert len(intervals) == case['intervals'], (case['name'], 'disconnected intervals', [(v[0], v[-1]) for v in intervals])
        bounds.append([intervals[0][0], band[0], intervals[-1][-1], band[-1]])
        if case['name'] == 'decoration-shadow':
            # CSS px -> pixels at 144 dpi. The shadow must bridge the same space.
            y = (band[0] + band[-1]) // 2 + 12
            for x in range(intervals[0][0] + 1, intervals[-1][-1]):
                r, g, b = rgb.getpixel((x, y))
                assert b > 140 and r < 80 and g < 80, ('shadow gap', x, y, (r, g, b))
    return {'bounds': bounds, 'pixels_sha256': sha256(rgb.tobytes()).hexdigest()}

def glyph_geometry(data):
    result = []
    with pdfium.PdfDocument(data) as document:
        for page in document:
            text = page.get_textpage()
            result.append([(text.get_text_range(i, 1), tuple(round(v, 4) for v in text.get_charbox(i)))
                           for i in range(text.count_chars())])
            text.close()
            page.close()
    return result
