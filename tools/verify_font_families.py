#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Check installed-binding font selection against explicit-face controls."""
import argparse
from hashlib import sha256
from importlib import metadata
from io import BytesIO
import json
from pathlib import Path

from fontTools.ttLib import TTFont
from pypdf import PdfReader
import pypdfium2 as pdfium


def digest(data):
    return sha256(data).hexdigest()


def faces(page, originals):
    selected = set()
    for ref in page['/Resources']['/Font'].values():
        font = ref.get_object()
        for descendant in font.get('/DescendantFonts', [font]):
            descriptor = descendant.get_object().get('/FontDescriptor')
            assert descriptor is not None, 'Expected an embedded face, found fallback'
            descriptor = descriptor.get_object()
            assert '/FontFile2' in descriptor, 'Expected TrueType font program'
            with TTFont(BytesIO(descriptor['/FontFile2'].get_data())) as parsed:
                face = parsed['name'].getDebugName(6)
                assert parsed.reader['name'] == originals[face], 'Embedded name/notice table differs'
                selected.add(face)
    return sorted(selected)


def verify(root):
    root = Path(root).resolve()
    inputs = json.loads((root / 'renders.json').read_text(encoding='utf-8'))
    assert inputs['ok'] and inputs['cases']
    originals = {}
    for item in inputs['fonts']:
        path = (root / item['path']).resolve()
        assert path.is_relative_to(root) and digest(path.read_bytes()) == item['sha256']
        with TTFont(path) as font:
            assert font['name'].getDebugName(6) == item['face']
            originals[item['face']] = font.reader['name']
    report = {**{key: value for key, value in inputs.items() if key != 'cases'}, 'ok': False, 'cases': [],
              'readers': {name: metadata.version(name) for name in ['pypdf', 'pypdfium2', 'fonttools', 'pillow']},
              'scope': 'Retained regular/italic cases and explicit-face controls; no complete CSS matching or conformance claim.'}
    checked = {}
    try:
        for item in inputs['cases']:
            folder = (root / item['name']).resolve()
            assert folder.is_relative_to(root)
            for path, key in [('input.html', 'htmlSha256'), ('style.css', 'cssSha256'), ('document.pdf', 'pdfSha256')]:
                assert digest((folder / path).read_bytes()) == item[key], (item['name'], key)
            data = (folder / 'document.pdf').read_bytes()
            reader = PdfReader(BytesIO(data), strict=True)
            assert len(reader.pages) == len(item['expectedText']) == len(item['previewsSha256'])
            actual_faces = [faces(page, originals) for page in reader.pages]
            assert all(value == [item['expectedFace']] for value in actual_faces), f"{item['name']}: expected {item['expectedFace']}, found {actual_faces}"
            texts = [' '.join(page.extract_text().split()) for page in reader.pages]
            assert texts == item['expectedText'], (item['name'], texts)
            pixels = []
            with pdfium.PdfDocument(data) as document:
                for index, expected in enumerate(item['previewsSha256']):
                    filename = item.get('previewFiles', [f'preview-{i + 1}.png' for i in range(len(item['previewsSha256']))])[index]
                    preview = (folder / filename).resolve()
                    assert preview.is_relative_to(folder) and digest(preview.read_bytes()) == expected
                    page = document[index]
                    text_page = page.get_textpage()
                    assert ' '.join(text_page.get_text_range().split()) == texts[index]
                    text_page.close()
                    bitmap = page.render(scale=96 / 72)
                    image = bitmap.to_pil()
                    pixels.append(digest(image.tobytes()))
                    image.save(folder / f'pdfium-{index + 1}.png')
                    image.close()
                    bitmap.close()
                    page.close()
            actual = {**item, 'faces': actual_faces, 'text': texts, 'pdfiumPixelsSha256': pixels}
            if item['control']:
                control = checked[item['control']]
                for key in ['previewsSha256', 'pdfiumPixelsSha256', 'text', 'faces']:
                    assert actual[key] == control[key], (item['name'], 'explicit-face control differs', key)
                actual['matchesExplicitFaceControl'] = True
            checked[item['name']] = actual
            report['cases'].append(actual)
        assert len(checked) == len(inputs['cases']), 'Duplicate case names'
        report['ok'] = True
    finally:
        (root / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    result = verify(args.directory)
    print(json.dumps({'ok': result['ok'], 'package': result['packageVersion'], 'cases': len(result['cases'])}))
