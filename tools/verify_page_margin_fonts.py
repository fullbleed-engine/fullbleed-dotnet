# SPDX-License-Identifier: MIT
"""Inspect the actual C# package's page-font choices independently of pair parity."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import re

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets/page-margin-fonts-2.5.48.json'
MODES = ('direct', 'compiled', 'fixed', 'reflow', 'compact')


def inspect(path, folder, *, allow_unembedded=False):
    reader = PdfReader(path, strict=True)
    result = []
    with pdfium.PdfDocument(path) as document:
        assert len(reader.pages) == len(document)
        for number, page in enumerate(reader.pages):
            spans = []
            page.extract_text(visitor_text=lambda text, cm, tm, font, size: spans.append({
                'text': ' '.join(text.split()), 'face': str(font['/BaseFont']).split('+')[-1].lstrip('/'), 'size': size,
            }) if text.strip() and font else None)
            footers = [span for span in spans if span['text'].startswith('FOOTER')]
            assert len(footers) == 1 and re.fullmatch(r'FOOTER \d+ of \d+', footers[0]['text'])
            faces, unembedded = [], []
            for reference in page['/Resources']['/Font'].values():
                font = reference.get_object()
                for child in font.get('/DescendantFonts', [font]):
                    descriptor = child.get_object().get('/FontDescriptor')
                    if descriptor is None:
                        assert allow_unembedded, 'Unembedded fallback font'
                        unembedded.append(str(font['/BaseFont']))
                        continue
                    assert any(key in descriptor.get_object() for key in ['/FontFile', '/FontFile2', '/FontFile3'])
                faces.append(str(font['/BaseFont']).split('+')[-1].lstrip('/'))
            text = ' '.join(page.extract_text().split())
            independent_page = document[number]
            text_page = independent_page.get_textpage()
            assert text == ' '.join(text_page.get_text_range().split())
            width, height = independent_page.get_size()
            for index in range(text_page.count_chars()):
                char = text_page.get_text_range(index, 1)
                if not char or char.isspace():
                    continue
                left, bottom, right, top = text_page.get_charbox(index)
                assert min(left, bottom) >= -0.25 and right <= width + 0.25 and top <= height + 0.25
            bitmap = independent_page.render(scale=96 / 72)
            image = bitmap.to_pil().convert('RGB')
            assert min(image.convert('L').getextrema()) < 200, 'Blank preview'
            image.save(folder / f'pdfium-{number + 1}.png')
            result.append(dict(text=text, footer=footers[0], faces=sorted(faces), unembeddedFaces=sorted(unembedded),
                               pdfiumPixelsSha256=sha256(image.tobytes()).hexdigest()))
            image.close()
            bitmap.close()
            text_page.close()
            independent_page.close()
    return result


def verify(directory, *, baseline=False):
    directory = Path(directory)
    source_bytes = SOURCE.read_bytes()
    source = json.loads(source_bytes)
    renders = json.loads((directory / 'renders.json').read_text(encoding='utf-8-sig'))
    assert (directory / 'inputs.json').read_bytes() == source_bytes
    assert renders['inputSha256'] == sha256(source_bytes).hexdigest()
    assert baseline == (renders['packageVersion'] == '0.2.7')
    for font in source['fonts']:
        assert sha256((directory / 'fonts' / font['name']).read_bytes()).hexdigest() == font['sha256']
    fixtures = {row['name']: row for row in source['cases']}
    modes = ('direct',) if baseline else MODES
    expected = {(name, mode, kind) for name in fixtures for mode in modes for kind in ('alias', 'control')}
    assert len(fixtures) == 8 and len(renders['cases']) == len(expected)
    assert {(row['name'], row['mode'], row['kind']) for row in renders['cases']} == expected
    cases = []
    for row in renders['cases']:
        fixture = fixtures[row['name']]
        assert row['directory'] == row['mode'] + '-' + row['name'] + '/' + row['kind']
        assert row['repeatPdfBytesMatch']
        folder = directory / row['directory']
        count = 1 if row['mode'] in ('direct', 'compiled') else 2
        html = fixture['html'] if count == 1 else fixture['html'].replace('Alpha', '{{name}}')
        assert (folder / 'input.html').read_text(encoding='utf8') == html
        assert (folder / 'style.css').read_text(encoding='utf8') == fixture[row['kind'] + 'Css']
        assert json.loads((folder / 'records.json').read_bytes()) == (['Alpha'] if count == 1 else ['Alpha', 'Bravo'])
        path = folder / 'document.pdf'
        assert sha256(path.read_bytes()).hexdigest() == row['pdfSha256']
        pages = inspect(path, folder, allow_unembedded=baseline)
        assert len(pages) == row['pages'] == len(fixture['footerFaces']) * count
        assert len(row['previewFiles']) == len(row['previewSha256']) == len(pages)
        assert 'Record Alpha' in pages[0]['text'] and (count == 1 or 'Record Bravo' in pages[-1]['text'])
        previews = []
        for filename, digest in zip(row['previewFiles'], row['previewSha256'], strict=True):
            preview = (folder / filename).resolve()
            assert preview.is_relative_to(folder.resolve()) and sha256(preview.read_bytes()).hexdigest() == digest
            with Image.open(preview) as image:
                rgb = image.convert('RGB')
                assert min(rgb.convert('L').getextrema()) < 200
                previews.append(sha256(rgb.tobytes()).hexdigest())
        correct_faces = [page['footer']['face'] for page in pages] == fixture['footerFaces'] * count
        correct_sizes = all(abs(page['footer']['size'] - fixture['footerSize']) < 0.01 for page in pages)
        cases.append(dict(name=row['name'], mode=row['mode'], kind=row['kind'], directory=row['directory'],
                          pdfSha256=row['pdfSha256'], nativePreviewSha256=row['previewSha256'], nativePixelsSha256=previews,
                          pages=pages, expectedFooterFaces=correct_faces, expectedFooterSizes=correct_sizes))
    pairs = []
    for name in fixtures:
        for mode in modes:
            alias, control = [next(row for row in cases if (row['name'], row['mode'], row['kind']) == (name, mode, kind)) for kind in ('alias', 'control')]
            match = all(alias[key] == control[key] for key in ['pages', 'nativePreviewSha256', 'nativePixelsSha256'])
            pairs.append(dict(name=name, mode=mode, matchesExplicitControl=match, pdfBytesEqual=alias['pdfSha256'] == control['pdfSha256']))
    failures = [row['directory'] for row in cases if not row['expectedFooterFaces'] or not row['expectedFooterSizes']]
    if baseline:
        expected_failures = {f'direct-{name}/alias' for name in fixtures} | {'direct-fallback-alias/control'}
        passed = set(failures) == expected_failures
    else:
        passed = not failures and all(row['matchesExplicitControl'] for row in pairs)
    report = dict(ok=passed, baseline=baseline, packageVersion=renders['packageVersion'], inputSha256=renders['inputSha256'],
                  pairs=len(pairs), pages=sum(len(row['pages']) for row in cases), cases=cases, comparisons=pairs,
                  failedSelections=failures, scope='Eight page-font layouts with explicit-face controls, independent embedded-face, text, size and pixel checks. Weight descriptors select visibly distinct supplied faces; they do not verify physical bold synthesis. No general CSS or conformance claim.')
    (directory / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf8')
    assert report['ok'], 'Page-font verification failed; inspect the retained report.'
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--negative-control', action='store_true')
    args = parser.parse_args()
    report = verify(args.directory, baseline=args.negative_control)
    print(json.dumps({key: report[key] for key in ['ok', 'baseline', 'packageVersion', 'pairs', 'pages', 'failedSelections']}))
