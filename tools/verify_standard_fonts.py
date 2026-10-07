#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Inspect packed-consumer text, unembedded Latin faces and preview pixels."""
import argparse
from hashlib import sha256
import json
from pathlib import Path

from PIL import Image, ImageChops
from pypdf import PdfReader
import pypdfium2 as pdfium

FONTS = ['Helvetica', 'Helvetica-Bold', 'Helvetica-Oblique', 'Helvetica-BoldOblique',
         'Times-Roman', 'Times-Bold', 'Times-Italic', 'Times-BoldItalic',
         'Courier', 'Courier-Bold', 'Courier-Oblique', 'Courier-BoldOblique', 'Inter']
MODES = ['pdf', 'fixed', 'reflow', 'compact']


def preview(path):
    with Image.open(path) as source:
        pixels = source.convert('RGB')
    assert pixels.size == (300, 100), (str(path), pixels.size)
    bounds = ImageChops.difference(pixels, Image.new('RGB', pixels.size, 'white')).getbbox()
    return {'sha256': sha256(path.read_bytes()).hexdigest(), 'inkBounds': bounds,
            'pixelsSha256': sha256(pixels.tobytes()).hexdigest()}


def verify(root, allow_blank=False):
    rendered = json.loads((root / 'renders.json').read_text(encoding='utf-8'))
    assert rendered['ok'] and rendered['schema'] == 'fullbleed.dotnet.standard_fonts.v1'
    assert [row['name'] for row in rendered['cases']] == [f'{mode}-{font}' for mode in MODES for font in FONTS]
    cases = []
    for case in rendered['cases']:
        folder = root / case['name']
        data = (folder / 'document.pdf').read_bytes()
        assert sha256(data).hexdigest() == case['pdfSha256']
        assert sha256((folder / 'input.html').read_bytes()).hexdigest() == case['htmlSha256']
        assert sha256((folder / 'style.css').read_bytes()).hexdigest() == case['cssSha256']
        reader = PdfReader(folder / 'document.pdf')
        assert len(reader.pages) == 1
        text = ' '.join(reader.pages[0].extract_text().split())
        assert text == case['expectedText'], (case['name'], text)
        fonts = [ref.get_object() for ref in reader.pages[0]['/Resources']['/Font'].values()]
        names = [str(font['/BaseFont']).lstrip('/') for font in fonts]
        if case['font'] == 'Inter':
            assert all('Inter' in name for name in names) and names
            for font in fonts:
                for child in font.get('/DescendantFonts', [font]):
                    descriptor = child.get_object()['/FontDescriptor'].get_object()
                    assert '/FontFile2' in descriptor, 'Control must embed its font'
        else:
            assert set(names) == {case['font']}, (case['name'], names)
            assert all('/FontDescriptor' not in font for font in fonts), 'Standard faces must be unembedded'
        with pdfium.PdfDocument(data) as document:
            page = document[0]
            try:
                textpage = page.get_textpage()
                try:
                    independent_text = ' '.join(textpage.get_text_range().split())
                    assert independent_text == text, (case['name'], independent_text)
                finally:
                    textpage.close()
                bitmap = page.render(scale=1)
                independent = bitmap.to_pil().convert('RGB')
                assert ImageChops.difference(independent, Image.new('RGB', independent.size, 'white')).getbbox()
                independent.save(folder / 'pdfium-1.png')
                independent_hash = sha256(independent.tobytes()).hexdigest()
                independent.close()
                bitmap.close()
            finally:
                page.close()
        native = preview(folder / case['previewFile'])
        assert native['sha256'] == case['previewSha256']
        assert native == preview(folder / 'repeat' / case['previewFile'])
        assert allow_blank or native['inkBounds'], (case['name'], 'Blank native preview')
        direct = None
        if case['mode'] == 'pdf':
            paths = list((folder / 'html').glob('*.png'))
            assert len(paths) == 1
            direct = preview(paths[0])
            assert direct['sha256'] == case['htmlPreviewSha256']
            assert allow_blank or direct['inkBounds'], (case['name'], 'Blank HTML preview')
        assert case['repeatPdfBytesIdentical'] and case['repeatPreviewBytesIdentical']
        cases.append({**case, 'text': text, 'faces': names, 'nativeInkBounds': native['inkBounds'],
                      'htmlInkBounds': direct['inkBounds'] if direct else None,
                      'pdfiumPixelsSha256': independent_hash})
    report = {'ok': True, 'packageVersion': rendered['packageVersion'],
              'embeddedControlFontSha256': rendered['embeddedControlFontSha256'], 'cases': cases,
              'scope': 'Twelve unembedded Latin faces and an embedded Inter control through ordinary, fixed, '
                       'reflow and compact output. Text/font selection, visible and repeatable previews. '
                       'No original-font-design parity, universal glyph coverage or PDF conformance claim.'}
    (root / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--allow-blank', action='store_true', help='Inspect the old public package without accepting its previews')
    args = parser.parse_args()
    report = verify(args.directory, args.allow_blank)
    print(json.dumps({'ok': report['ok'], 'package': report['packageVersion'], 'cases': len(report['cases']),
                      'blank': sum(case['nativeInkBounds'] is None for case in report['cases'])}))
