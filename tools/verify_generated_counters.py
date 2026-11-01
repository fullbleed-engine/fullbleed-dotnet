# SPDX-License-Identifier: MIT
"""Read saved C# counter PDFs and retain record boundaries and raw text order."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import re

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets/generated-counters-2.5.49.json'
MODES = ('direct', 'compiled', 'fixed', 'reflow', 'compact')


def normalize(text):
    return re.sub(r'\s+', '', text)


def verify(directory, *, negative_control=False, baseline=None):
    directory = Path(directory)
    source_bytes = SOURCE.read_bytes()
    source = json.loads(source_bytes)
    fixture_sha = sha256(source_bytes).hexdigest()
    report = json.loads((directory / 'renders.json').read_bytes())
    assert report['status'] == 'rendered-awaiting-independent-readback'
    inputs = (directory / 'inputs.json').read_bytes()
    assert report['fixtureSha256'] == sha256(inputs).hexdigest()
    # The retained public control predates repository LF normalization.
    # Its exact bytes remain hashed, and every parsed input must match.
    assert json.loads(inputs) == source
    if negative_control:
        assert report['packageVersion'] == '0.2.8'
    else:
        assert inputs == source_bytes and source['engineVersion'] == '2.5.49'
        assert tuple(map(int, report['packageVersion'].split('.'))) >= (0, 2, 9)
    modes = ('direct',) if negative_control else MODES
    assert tuple(report['modes']) == modes
    expected = {row['name']:row for row in source['cases']}
    wanted = {(name, mode) for name in expected for mode in modes}
    assert len(expected) == 59 and len(report['cases']) == len(wanted)
    assert {(r['name'],r['mode']) for r in report['cases']} == wanted
    records = []
    for row in report['cases']:
        name, mode = row['name'], row['mode']
        names = ['Ada'] if mode in ('direct', 'compiled') else ['Ada', 'Bea']
        assert row['names'] == names and row['filename'] == name+'-'+mode+'.pdf'
        if not negative_control:
            assert row['repeatPdfBytesMatch'] is True
        path = directory / row['filename']
        assert sha256(path.read_bytes()).hexdigest() == row['pdfSha256']
        reader = PdfReader(path, strict=True)
        assert len(reader.pages) == row['pages'] == expected[name]['pagesPerRecord'] * len(names)
        page_texts = [normalize(page.extract_text() or '') for page in reader.pages]
        raw_text = ''.join(page_texts)
        text = ''.join('Record'+value+expected[name]['expectedCounterText'] for value in names)
        record_pages = expected[name]['pagesPerRecord']
        label_geometry = []
        with pdfium.PdfDocument(path) as document:
            assert len(document) == len(reader.pages)
            independent = []
            for number in range(len(document)):
                page = document[number]
                try:
                    text_page = page.get_textpage()
                    try:
                        independent.append(normalize(text_page.get_text_range()))
                        if number % record_pages == 0:
                            label = 'Record ' + names[number // record_pages]
                            search = text_page.search(label)
                            try:
                                match = search.get_next()
                                assert match is not None and search.get_next() is None, (name, mode, label)
                                start, count = match
                                label_boxes = [text_page.get_charbox(i) for i in range(start, start+count)]
                                body_boxes = [text_page.get_charbox(i) for i in range(text_page.count_chars())
                                    if not start <= i < start+count and not text_page.get_text_range(i, 1).isspace()]
                                assert body_boxes and min(box[1] for box in label_boxes) > max(box[3] for box in body_boxes), (name, mode, 'record heading must be above the counter body')
                                label_geometry.append(True)
                            finally:
                                search.close()
                    finally:
                        text_page.close()
                    if name == 'block-after-increment':
                        bitmap = page.render(scale=96/72)
                        try:
                            bitmap.to_pil().save(directory / f'{name}-{mode}-pdfium-{number+1}.png')
                        finally:
                            bitmap.close()
                finally:
                    page.close()
            assert independent == page_texts, (name,mode,'PDF readers disagree')
        # Fixed batches paint bound runs after the static page form. Their PDF
        # stream order differs from visual order. Keep the raw reader text, then
        # check every record range, binding and counter sequence independently.
        record_results = []
        for index, value in enumerate(names):
            pages = page_texts[index*record_pages:(index+1)*record_pages]
            label = 'Record' + value
            raw_record = ''.join(pages)
            assert pages[0].count(label) == raw_record.count(label) == 1
            assert all('Record'+other not in raw_record for other in names if other != value)
            record_results.append(dict(name=value, pageOffset=index*record_pages, pages=record_pages,
                labelAboveBody=label_geometry[index], counterText=raw_record.replace(label, '', 1),
                rawExtractedText=raw_record))
        actual = ''.join('Record'+r['name']+r['counterText'] for r in record_results)
        previews = [] if negative_control else row['previews']
        assert len(previews) == (row['pages'] if name == 'block-after-increment' and not negative_control else 0)
        for number, preview in enumerate(previews, 1):
            assert preview['filename'] == f'{name}-{mode}-native-{number}.png'
            image_path = directory / preview['filename']
            assert sha256(image_path.read_bytes()).hexdigest() == preview['sha256']
            with Image.open(image_path) as image:
                assert min(image.convert('L').getextrema()) < 200
        records.append(dict(name=name, mode=mode, pages=row['pages'], pdfSha256=row['pdfSha256'],
            actual=actual, expected=text, matchesExpected=actual == text, independentReadersAgree=True,
            rawExtractedText=raw_text, recordRangesVerified=True, records=record_results, previews=previews))
    failures = [r['name']+'-'+r['mode'] for r in records if not r['matchesExpected']]
    result = dict(ok=not failures, negativeControl=negative_control, packageVersion=report['packageVersion'],
        platform=report.get('platform'), runtime=report['runtime'],
        fixtureSha256=fixture_sha, inputSha256=report['fixtureSha256'], documents=len(records),
        pages=sum(r['pages'] for r in records), failures=failures, cases=records,
        scope='59 reviewed core counter documents through C# direct, compiled, fixed and throughput/compact reflow saved-PDF paths. '
        'Counter sequences, record bindings and page ranges are read with pypdf and PDFium; heading placement is checked with glyph geometry. '
        'Fixed bindings are painted after static content, so raw PDF text-stream order is retained separately and is not claimed to be logical reading order. '
        'HTML list controls preserve Fullbleed behavior; '
        'this is not complete CSS or browser raster parity.')
    if negative_control:
        result['ok'] = len(failures) == 47 and len(records)-len(failures) == 12
    elif baseline is not None:
        before = json.loads((Path(baseline) / 'verification.json').read_bytes())
        assert before['ok'] and before['negativeControl'] and before['packageVersion'] == '0.2.8'
        assert before['fixtureSha256'] == fixture_sha and len(before['cases']) == 59 and len(before['failures']) == 47
        direct = {r['name']:r for r in records if r['mode'] == 'direct'}
        healthy = [r for r in before['cases'] if r['matchesExpected']]
        assert all((direct[r['name']]['actual'],direct[r['name']]['pages']) == (r['actual'],r['pages']) for r in healthy)
        result['publicControl'] = dict(packageVersion='0.2.8',
            correctedDirectCases=sum(not r['matchesExpected'] and direct[r['name']]['matchesExpected'] for r in before['cases']),
            unchangedHealthyControls=len(healthy), reportSha256=sha256((Path(baseline)/'verification.json').read_bytes()).hexdigest())
        assert result['publicControl']['correctedDirectCases'] == 47 and len(healthy) == 12
    (directory / 'verification.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf8')
    assert result['ok'], 'Counter readback failed; inspect verification.json'
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', nargs='?', type=Path, default=ROOT / 'artifacts/generated-counters')
    parser.add_argument('--negative-control', action='store_true')
    parser.add_argument('--baseline', type=Path)
    args = parser.parse_args()
    result = verify(args.directory, negative_control=args.negative_control, baseline=args.baseline)
    print(json.dumps({k:result[k] for k in ['ok','negativeControl','packageVersion','documents','pages','failures']}))
