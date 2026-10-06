#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Independently verify mixed inline wrapping and intrinsic widths from a NuGet consumer."""
import argparse
from collections import Counter
from hashlib import sha256
from importlib.metadata import version
import json
from pathlib import Path
import re

from pypdf import PdfReader
import pypdfium2 as pdfium
from PIL import Image, ImageChops

TAIL = ('to change the paper size, margins, heading scale, table colors, or code treatment. '
        'Keep local image references next to the document.')
EXPECTED = 'Edit print.css ' + TAIL + ' After the paragraph.'
NAMES = ['plain', 'unstyled-span', 'code', 'color', 'background', 'padding', 'different-font',
         'decorated', 'long-inline', 'collapsed-spaces', 'helvetica', 'times']
MODES = ['pdf', 'fixed', 'reflow', 'compact']
MATRIX = {f'{mode}-{name}': (mode, 'wrapping', EXPECTED) for mode in MODES for name in NAMES}
for display in ['flex', 'inline-block']:
    expected = 'NORTHSTAR STUDIO / INVOICE NS-1042 01 / 01' if display == 'flex' else 'TOTAL OPERATING ALLOCATION USD END'
    MATRIX.update({f'sizing-{display}-{spacing}': ('pdf', 'sizing', expected) for spacing in ['0', '0.45', '1.5']})


def digest(data):
    return sha256(data).hexdigest()


def inspect_page(page):
    textpage = page.get_textpage()
    try:
        text = ''.join(textpage.get_text_range(i, 1) for i in range(textpage.count_chars()))
        words = []
        for match in re.finditer(r'\S+', text):
            boxes = [textpage.get_charbox(i) for i in range(match.start(), match.end())]
            words.append({'text': match.group(), 'box': [min(b[0] for b in boxes), min(b[1] for b in boxes),
                          max(b[2] for b in boxes), max(b[3] for b in boxes)]})
        return ' '.join(text.split()), words
    finally:
        textpage.close()


def visual_order(words):
    rows = []
    for word in sorted(words, key=lambda w: -(w['box'][1] + w['box'][3]) / 2):
        center = (word['box'][1] + word['box'][3]) / 2
        if not rows or abs(rows[-1][0] - center) >= 5:
            rows.append((center, []))
        rows[-1][1].append(word)
    return [word for _, row in rows for word in sorted(row, key=lambda w: w['box'][0])]


def verify(directory):
    root = Path(directory).resolve()
    inputs = json.loads((root / 'renders.json').read_text(encoding='utf-8'))
    assert inputs['schema'] == 'fullbleed.dotnet.inline_layout.v1' and inputs['ok']
    names = [item['name'] for item in inputs['cases']]
    assert len(names) == len(set(names)) == len(MATRIX) and set(names) == set(MATRIX), 'Missing, unexpected or duplicate inline fixtures'
    assert len(inputs['fonts']) == 2
    for entry in inputs['fonts']:
        font = (root / entry['path']).resolve()
        assert font.is_relative_to(root) and digest(font.read_bytes()) == entry['sha256']
    report = {'ok': False, 'packageVersion': inputs['packageVersion'], 'cases': [],
              'readers': {name: version(name) for name in ['pypdf', 'pypdfium2', 'pillow']},
              'scope': '48 ordinary/fixed/reflow inline wrapping fixtures and six intrinsic-width cases. '
                       'Unembedded Helvetica/Times preview fidelity is checked independently with PDFium; '
                       'native previews use host font fallbacks and can differ or omit glyphs. '
                       'No general layout or conformance claim.'}
    try:
        for item in inputs['cases']:
            mode, kind, expected = MATRIX[item['name']]
            assert (item['mode'], item['kind'], item['expectedText']) == (mode, kind, expected)
            assert item['repeatPdfBytesIdentical']
            folder = root / item['name']
            result = {'name': item['name'], 'mode': mode, 'kind': kind, 'ok': False}
            report['cases'].append(result)
            try:
                for filename, key in [('input.html', 'htmlSha256'), ('style.css', 'cssSha256'), ('document.pdf', 'pdfSha256')]:
                    assert digest((folder / filename).read_bytes()) == item[key], (item['name'], key)
                    result[key] = item[key]
                assert len(item['previewsSha256']) == len(item['previewFiles']) == 1
                preview = (folder / item['previewFiles'][0]).resolve()
                assert preview.is_relative_to(folder.resolve())
                assert digest(preview.read_bytes()) == item['previewsSha256'][0]
                result['previewSha256'] = item['previewsSha256'][0]
                with Image.open(preview) as source:
                    image = source.convert('RGB')
                    result['nativeInkBounds'] = ImageChops.difference(image, Image.new('RGB', image.size, 'white')).getbbox()
                builtin = item['name'].endswith(('-helvetica', '-times'))
                if not builtin:
                    assert result['nativeInkBounds'], 'Embedded-font preview is blank'
                data = (folder / 'document.pdf').read_bytes()
                reader = PdfReader(folder / 'document.pdf', strict=True)
                assert len(reader.pages) == 1
                pdf_fonts = [ref.get_object() for ref in reader.pages[0]['/Resources']['/Font'].values()]
                assert pdf_fonts, 'Expected PDF font resources'
                if builtin:
                    expected_face = '/Helvetica' if item['name'].endswith('-helvetica') else '/Times-Roman'
                    assert pdf_fonts and all(font['/BaseFont'] == expected_face and '/FontDescriptor' not in font for font in pdf_fonts), 'Expected only the explicit unembedded standard font'
                    result['previewFontScope'] = 'unembedded-base14'
                else:
                    for font in pdf_fonts:
                        for descendant in font.get('/DescendantFonts', [font]):
                            descriptor = descendant.get_object().get('/FontDescriptor')
                            assert descriptor and '/FontFile2' in descriptor.get_object(), 'Portable native-preview fixture must embed its font'
                    result['previewFontScope'] = 'embedded'
                result['pypdfText'] = ' '.join(reader.pages[0].extract_text().split())
                if item['name'].endswith(('-different-font', '-decorated')):
                    result['fonts'] = [str(ref.get_object().get('/BaseFont', '')) for ref in reader.pages[0]['/Resources']['/Font'].values()]
                    assert any('NotoSans-Regular' in face for face in result['fonts']), 'Second font was not selected'
                with pdfium.PdfDocument(data) as document:
                    page = document[0]
                    try:
                        result['pdfiumText'], words = inspect_page(page)
                        bitmap = page.render(scale=1.5)
                        image = bitmap.to_pil()
                        result['pdfiumPixelsSha256'] = digest(image.tobytes())
                        image.save(folder / 'pdfium-1.png')
                        image.close()
                        bitmap.close()
                    finally:
                        page.close()
                result['words'] = words
                if kind == 'sizing':
                    for key in ['pypdfText', 'pdfiumText']:
                        assert ''.join(result[key].split()) == ''.join(expected.split()), f'{key} content differs'
                    assert ''.join(word['text'] for word in words) == ''.join(expected.split())
                    centers = [(word['box'][1] + word['box'][3]) / 2 for word in words]
                    assert max(centers) - min(centers) < 2, 'A max-content label wrapped despite available room'
                    assert all(19 <= w['box'][0] < w['box'][2] <= 401 for w in words), 'Text leaves the content box'
                    assert all(a['box'][2] <= b['box'][0] + .1 for a, b in zip(words, words[1:])), 'Adjacent words overlap'
                else:
                    if mode == 'fixed':
                        # Paint-only replacements follow the static base in the PDF
                        # stream; check their content once and their physical order.
                        for key in ['pypdfText', 'pdfiumText']:
                            assert Counter(result[key].split()) == Counter(expected.split()), f'{key} content differs'
                        words = visual_order(words)
                        result['visualWords'] = words
                    else:
                        assert result['pypdfText'] == expected, 'pypdf content or order differs'
                        assert result['pdfiumText'] == expected, 'PDFium content or order differs'
                    assert [word['text'] for word in words] == expected.split(), 'Word boundaries differ'
                    prefix, code, suffix = [word['box'] for word in words[:3]]
                    assert prefix[2] <= code[0] and code[2] <= suffix[0], 'Initial inline words overlap'
                    assert max(b[1] for b in [prefix, code, suffix]) - min(b[1] for b in [prefix, code, suffix]) < 4, 'Following text left the first line'
                    lines = 1
                    for previous, current in zip(words, words[1:]):
                        a, b = previous['box'], current['box']
                        if abs((a[1] + a[3]) / 2 - (b[1] + b[3]) / 2) < 5:
                            assert b[0] >= a[2] - .1, f"Overlapping words: {previous['text']} / {current['text']}"
                        else:
                            assert b[3] < a[1], f"Lines overlap or move upwards: {previous['text']} / {current['text']}"
                            lines += 1
                    assert lines >= 4, 'The fixture did not wrap'
                    assert all(19 <= w['box'][0] < w['box'][2] <= 221 for w in words), 'Text leaves the content box'
                    if builtin:
                        control = (folder / 'control.pdf').read_bytes()
                        assert digest(control) == item['controlSha256']
                        with pdfium.PdfDocument(control) as document:
                            page = document[0]
                            try:
                                _, reference = inspect_page(page)
                            finally:
                                page.close()
                        assert len(reference) == 4
                        if mode != 'fixed':
                            assert all(abs(a['box'][0] - b['box'][0]) < .05 for a, b in zip(words[:4], reference)), 'Built-in font spacing differs from whole-string painting'
                    result['lines'] = lines
                result['ok'] = True
            except Exception as error:
                result['error'] = str(error)
        report['ok'] = all(case['ok'] for case in report['cases'])
        assert report['ok'], '; '.join(f"{case['name']}: {case['error']}" for case in report['cases'] if not case['ok'])
        return report
    finally:
        (root / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    report = verify(parser.parse_args().directory)
    print(json.dumps({'ok': report['ok'], 'package': report['packageVersion'], 'cases': len(report['cases'])}))
