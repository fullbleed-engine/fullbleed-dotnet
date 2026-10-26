"""Inspect the retained core table fixtures through packaged C# APIs."""
import argparse
from hashlib import sha256
from importlib.metadata import version
import json
from pathlib import Path

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets'
SOURCE = ASSETS / 'table-wrapping-2.5.37.json'
SOURCE_SHA256 = '06ea51d8e61c24141de56f7fc622c3d123d3d694e07cd7995e175aab3c00fafa'
# Byte-identical inputs and bounds expectations from the core 2.5.37 tests,
# retained by the Node integration at c5513fe4f7d0c82cfef084d76ff6df7ab3b0c710.


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


def normalized(text):
    return ''.join(text.split())


def inspect_pdf(path, fixture):
    reader = PdfReader(path, strict=True)
    extracted = normalized(''.join(page.extract_text() for page in reader.pages))
    boxes, all_text, second_left, second_right, pixels = [], '', None, None, []
    for page in reader.pages:
        fonts = [font.get_object() for font in page['/Resources']['/Font'].values()]
        if fixture['kind'] == 'child-width':
            assert len(fonts) == 1 and fonts[0]['/BaseFont'] == '/Helvetica'
            assert fonts[0]['/Subtype'] == '/Type1' and '/FontDescriptor' not in fonts[0]
        else:
            for font in fonts:
                if '/DescendantFonts' in font:
                    font = font['/DescendantFonts'][0].get_object()
                descriptor = font['/FontDescriptor'].get_object()
                assert any(key in descriptor for key in ['/FontFile', '/FontFile2', '/FontFile3'])
    with pdfium.PdfDocument(path) as document:
        for number, page in enumerate(document, 1):
            text = page.get_textpage()
            try:
                contents = text.get_text_range()
                all_text += normalized(contents)
                for index in range(text.count_chars()):
                    if text.get_text_range(index, 1).strip():
                        boxes.append(list(text.get_charbox(index)))
                if 'SECOND' in contents:
                    start = contents.index('SECOND')
                    second_left = text.get_charbox(start)[0]
                    second_right = text.get_charbox(start + len('SECOND') - 1)[2]
                bitmap = page.render(scale=1)
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
    checks = dict(one_page=len(reader.pages) == 1, visible_text=bool(boxes))
    if fixture['kind'] == 'wrapping':
        expected = normalized(fixture['expectedText'])
        checks.update(pypdf_content=extracted == expected, pdfium_content=all_text == expected,
                      left_inside_cell=bool(boxes) and min(b[0] for b in boxes) >= 23.9,
                      vertical_page_bounds=bool(boxes) and min(b[1] for b in boxes) >= 23.9 and max(b[3] for b in boxes) <= 336.1)
        right = max((box[2] for box in boxes), default=0)
        checks['authored_wrapping'] = right > 206 if fixture['intentionalOverflow'] else right <= 206
    else:
        checks.update(pypdf_content=extracted == 'FIRSTSECOND', pdfium_content=all_text == 'FIRSTSECOND')
        minimum = fixture['secondCellMinimumLeftPt']
        checks['authored_child_width'] = second_left is not None and second_right is not None and (
            second_left >= minimum - 1 if minimum is not None else second_right <= 206)
    return dict(pages=len(reader.pages), checks=checks, text=extracted, pdfiumText=all_text,
                glyphBounds=boxes, secondLeftPt=second_left, secondRightPt=second_right,
                pdfiumPixelsSha256=pixels,
                pdfiumFontScope='unembedded-helvetica' if fixture['kind'] == 'child-width' else 'embedded-test-font')


def verify(directory, require_correct=True):
    directory = Path(directory)
    assert digest(SOURCE) == SOURCE_SHA256, 'Table-wrapping reference inputs changed'
    source = json.loads(SOURCE.read_text(encoding='utf-8'))
    fixtures = source['cases']
    assert len(fixtures) == 35
    assert digest(directory / 'inputs.json') == SOURCE_SHA256
    rendered = json.loads((directory / 'renders.json').read_text(encoding='utf-8-sig'))
    assert rendered['sourceSha256'] == SOURCE_SHA256
    assert rendered['fontSha256'] == source['fontSha256']
    for name in ['FullbleedCJKTest-Regular.ttf', 'FullbleedCJKTest-OFL.txt', 'table-wrapping-font.json']:
        assert (directory / 'assets' / name).read_bytes() == (ASSETS / name).read_bytes()
    assert digest(ASSETS / 'FullbleedCJKTest-Regular.ttf') == source['fontSha256']
    expected = [(mode, case['name']) for mode in ['direct', 'compiled'] for case in fixtures]
    assert [(case['mode'], case['name']) for case in rendered['cases']] == expected
    records = []
    for record in rendered['cases']:
        fixture = next(case for case in fixtures if case['name'] == record['name'])
        assert record['directory'] == record['mode'] + '-' + record['name']
        folder = directory / record['directory']
        for field, name, key in [('html', 'input.html', 'htmlSha256'), ('css', 'style.css', 'cssSha256')]:
            data = (folder / name).read_bytes()
            assert data == fixture[field].encode('utf-8')
            assert sha256(data).hexdigest() == record[key]
        assert digest(folder / 'document.pdf') == record['pdfSha256']
        assert record['repeatPdfBytesIdentical']
        result = inspect_pdf(folder / 'document.pdf', fixture)
        assert result['pages'] == record['pages']
        for files, hashes in [('previewFiles', 'previewsSha256'), ('htmlPreviewFiles', 'htmlPreviewsSha256')]:
            assert len(record[files]) == result['pages']
            visible = []
            for name, expected_hash in zip(record[files], record[hashes], strict=True):
                path = (folder / name).resolve()
                assert path.is_relative_to(folder.resolve())
                assert digest(path) == expected_hash
                with Image.open(path) as image:
                    assert image.format == 'PNG'
                    visible.append(min(channel[0] for channel in image.convert('RGB').getextrema()) < 180)
            result['checks'][files + '_visible'] = all(visible)
        records.append({**record, **result, 'ok': all(result['checks'].values())})
    report = dict(schema='fullbleed.dotnet.table_wrapping.verification.v1',
                  ok=all(case['ok'] for case in records), packageVersion=rendered['packageVersion'],
                  sourceSha256=SOURCE_SHA256, fontSha256=source['fontSha256'], cases=records,
                  readers={name: version(name) for name in ['pypdf', 'pypdfium2', 'Pillow']},
                  scope=source['scope'])
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
