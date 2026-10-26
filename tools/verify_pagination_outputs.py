"""Check packaged pagination using independent PDF readers and retained inputs."""
import argparse
from collections import Counter
from hashlib import sha256
from importlib.metadata import version
import json
from pathlib import Path
import re

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets/pagination-2.5.35.json'
SOURCE_SHA256 = '4c17251aecf65217ade0792108ec3b40e441728a14d9e0f0c486461bf2959072'
# Byte-identical inputs and content/bounds expectations from the published
# 2.5.35 playground checks, independent of this .NET candidate:
# https://github.com/fullbleed-engine/docs/blob/0ff865479d3084b5476973f5d7353ce48cbed4d6/tools/playground_pagination.py


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


def normalize(text):
    return re.sub(r'\s+', '', text)


def inspect_pdf(path, case):
    reader = PdfReader(path)
    texts = [page.extract_text() for page in reader.pages]
    checks = {'page_count': len(texts) == case['pages']}
    if case['kind'] == 'table':
        checks['every_row_once'] = Counter(re.findall(r'ROW-\s*(\d{3})', '\n'.join(texts))) == Counter(f'{i:03}' for i in range(1, 81))
        checks['repeated_headers'] = all('Record' in text and 'Details' in text for text in texts if 'ROW-' in text)
    elif case['kind'] == 'break':
        checks['both_markers'] = bool(texts) and 'BEFORE' in texts[0] and 'AFTER' in texts[-1]
        if case['pages'] == 2:
            checks['break_separates_markers'] = len(texts) == 2 and 'AFTER' not in texts[0] and 'BEFORE' not in texts[1]
    else:
        assert case['kind'] == 'footnote'
        checks['preceding_line_retained'] = len(texts) == 2 and 'First line stays here' in texts[0] and 'First line stays here' not in texts[1]
        checks['footnotes_follow_call'] = len(texts) == 2 and 'Second line has notes' not in texts[0] and all(value in texts[1] for value in ['Second line has notes', 'first note', 'second note'])
    outside = 0
    pdfium_text = []
    pixels = []
    with pdfium.PdfDocument(path) as document:
        for number, page in enumerate(document, 1):
            width, height = page.get_size()
            text = page.get_textpage()
            try:
                pdfium_text.append(text.get_text_range())
                for index in range(text.count_chars()):
                    if text.get_text_range(index, 1).strip():
                        left, bottom, right, top = text.get_charbox(index)
                        outside += int(left < -0.1 or bottom < -0.1 or right > width + 0.1 or top > height + 0.1)
                bitmap = page.render(scale=96 / 72)
                try:
                    image = bitmap.to_pil().convert('RGB')
                    pixels.append(sha256(image.tobytes()).hexdigest())
                    image.save(path.parent / f'pdfium-{number}.png')
                    image.close()
                finally:
                    bitmap.close()
            finally:
                text.close()
                page.close()
    checks['glyphs_inside_pages'] = outside == 0
    checks['independent_text_agrees'] = [normalize(t) for t in texts] == [normalize(t) for t in pdfium_text]
    return dict(pages=len(texts), offPageGlyphs=outside, checks=checks,
                text=texts, pdfiumText=pdfium_text, pdfiumPixelsSha256=pixels)


def verify(directory, require_correct=True):
    directory = Path(directory)
    assert digest(SOURCE) == SOURCE_SHA256, 'Pagination reference inputs changed'
    fixtures = json.loads(SOURCE.read_text(encoding='utf-8'))['cases']
    rendered = json.loads((directory / 'renders.json').read_text(encoding='utf-8-sig'))
    assert rendered['sourceSha256'] == SOURCE_SHA256
    expected = [(mode, case['name']) for mode in ['direct', 'compiled'] for case in fixtures]
    assert [(case['mode'], case['name']) for case in rendered['cases']] == expected
    records = []
    for record in rendered['cases']:
        fixture = next(case for case in fixtures if case['name'] == record['name'])
        assert record['directory'] == record['mode'] + '-' + record['name']
        folder = directory / record['directory']
        for field, name, key in [('html', 'input.html', 'htmlSha256'), ('css', 'style.css', 'cssSha256')]:
            data = (folder / name).read_bytes()
            assert data == fixture[field].encode('utf-8'), (record['name'], 'input changed', field)
            assert sha256(data).hexdigest() == record[key]
        assert digest(folder / 'document.pdf') == record['pdfSha256']
        assert record['repeatPdfBytesIdentical']
        result = inspect_pdf(folder / 'document.pdf', fixture)
        assert result['pages'] == record['pages']
        assert record['expectedPages'] == fixture['pages']
        for files, hashes in [('previewFiles', 'previewsSha256'), ('htmlPreviewFiles', 'htmlPreviewsSha256')]:
            assert len(record[files]) == result['pages']
            visible = []
            for name, expected_hash in zip(record[files], record[hashes], strict=True):
                path = (folder / name).resolve()
                assert path.is_relative_to(folder.resolve())
                assert digest(path) == expected_hash
                with Image.open(path) as image:
                    assert image.format == 'PNG'
                    extrema = image.convert('RGB').getextrema()
                    visible.append(any(low != high for low, high in extrema))
            result['checks'][files + '_visible'] = all(visible)
        records.append({**record, **result, 'ok': all(result['checks'].values())})
    report = dict(schema='fullbleed.dotnet.pagination.verification.v1',
                  ok=all(case['ok'] for case in records), packageVersion=rendered['packageVersion'],
                  sourceSha256=SOURCE_SHA256, cases=records,
                  readers={name: version(name) for name in ['pypdf', 'pypdfium2', 'Pillow']},
                  scope='Nine retained layouts through direct and compiled C# APIs. Checks cover table rows and repeated headers, forced breaks, footnotes, page bounds and previews; not universal CSS parity.')
    (directory / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    if require_correct:
        assert report['ok'], [(case['mode'], case['name'], case['checks']) for case in records if not case['ok']]
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--allow-failures', action='store_true')
    args = parser.parse_args()
    report = verify(args.directory.resolve(), require_correct=not args.allow_failures)
    print(json.dumps(dict(ok=report['ok'], package=report['packageVersion'], cases=len(report['cases']),
                          failed=[case['mode'] + '/' + case['name'] for case in report['cases'] if not case['ok']])))


if __name__ == '__main__':
    main()
