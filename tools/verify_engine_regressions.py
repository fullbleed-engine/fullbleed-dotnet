#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Check .NET-generated PDFs against retained counter, color and Chrome geometry expectations."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from hashlib import sha256
from importlib.metadata import version
import json
from pathlib import Path
import re

from PIL import Image, ImageChops, ImageFilter
from pypdf import PdfReader
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets/engine-2.5.22.json'
COLORS = {'initial': (188, 108, 37), 'border': (224, 64, 32), 'background': (255, 224, 128)}


def color_bounds(image, color):
    difference = ImageChops.difference(image.convert('RGB'), Image.new('RGB', image.size, color))
    channels = [channel.point(lambda value: 255 if value < 24 else 0) for channel in difference.split()]
    mask = ImageChops.multiply(ImageChops.multiply(channels[0], channels[1]), channels[2])
    if color == COLORS['background']:
        mask = mask.filter(ImageFilter.MinFilter(3))
    bounds = mask.getbbox()
    return list(bounds) if bounds else None


def inspect(pdf, dpi):
    observations, images = [], []
    with pdfium.PdfDocument(pdf) as document:
        for page in document:
            text = page.get_textpage()
            try:
                chars = [[text.get_text_range(i, 1), *text.get_charbox(i)]
                         for i in range(text.count_chars()) if text.get_text_range(i, 1).strip()]
                observations.append(dict(size=list(page.get_size()), chars=chars))
                bitmap = page.render(scale=dpi / 72)
                try:
                    images.append(bitmap.to_pil().convert('RGB'))
                finally:
                    bitmap.close()
            finally:
                text.close()
                page.close()
    return observations, images


def check_case(case, pdf, previews, destination):
    checks = []
    def check(name, ok, **details):
        checks.append(dict(name=name, ok=bool(ok), **details))
    reader = PdfReader(pdf)
    check('page count', len(reader.pages) == case['pages'], actual=len(reader.pages), expected=case['pages'])
    observations, independent = inspect(pdf, case['previewDpi'])
    for index, image in enumerate(independent, 1):
        image.save(destination / f'pdfium-{index}.png')
    native = []
    for path in previews:
        with Image.open(path) as image:
            native.append(image.convert('RGB'))
    try:
        check('preview page count', len(native) == case['pages'])
        expected = case['expected']
        if case['family'] == 'counter':
            text = ''.join(page.extract_text() for page in reader.pages)
            labels = re.findall(r'CNT([\d.\-]+)END', re.sub(r'\s+', '', text))
            check('emitted counter labels', labels == expected['labels'], actual=labels, expected=expected['labels'])
        elif case['family'] in ('overflow', 'border-image'):
            for mode, images in [('preview', native), ('pdfium', independent)]:
                image = images[0]
                check(mode + ' dimensions', list(image.size) == expected['size'], actual=list(image.size))
                for probe in expected['probes']:
                    actual = list(image.getpixel((probe['x'], probe['y'])))
                    check(mode + ' interior color', max(abs(a-b) for a,b in zip(actual,probe['expected'])) <= 3,
                          x=probe['x'], y=probe['y'], actual=actual, expected=probe['expected'])
        elif case['family'] == 'first-letter':
            reference = expected['chromePages']
            same_text = len(observations) == len(reference) and all(
                ''.join(c[0] for c in actual['chars']) == ''.join(c[0] for c in wanted['chars'])
                for actual, wanted in zip(observations, reference))
            check('Chrome text and page sequence', same_text)
            check('Chrome page sizes', [p['size'] for p in observations] == [p['size'] for p in reference])
            delta = max((abs(a-b) for page, wanted in zip(observations,reference)
                         for actual, target in zip(page['chars'],wanted['chars'])
                         for a,b in zip(actual[1:],target[1:])), default=0) if same_text else None
            check('Chrome character geometry', delta is not None and delta <= expected['tolerancePt'],
                  maximumDeltaPt=delta, tolerancePt=expected['tolerancePt'])
            for mode, images in [('preview',native),('pdfium',independent)]:
                for index, (image, wanted) in enumerate(zip(images,reference), 1):
                    for name,color in COLORS.items():
                        bounds = color_bounds(image,color)
                        bounds = [n*72/case['previewDpi'] for n in bounds] if bounds else None
                        target = wanted['colorBoundsPt'][name]
                        delta = max(abs(a-b) for a,b in zip(bounds,target)) if bounds is not None and target is not None else 0 if bounds == target else None
                        check(mode+' Chrome '+name+' bounds', delta is not None and delta <= 1.5,
                              page=index, actualPt=bounds, expectedPt=target, maximumDeltaPt=delta)
        else:
            raise ValueError('Unknown fixture family: '+case['family'])
    finally:
        for image in [*native, *independent]:
            image.close()
    return dict(ok=all(item['ok'] for item in checks), checks=checks)


def verify(out, *, require_correct=True):
    out = Path(out).resolve()
    source = json.loads(SOURCE.read_text(encoding='utf-8'))
    font = ROOT / 'tests/FullBleed.DotNet.Tests/Assets' / source['font']['file']
    license_file = SOURCE.parent / 'engine-regressions-font-OFL.txt'
    assert sha256(font.read_bytes()).hexdigest() == source['font']['sha256']
    assert sha256(license_file.read_bytes()).hexdigest() == source['font']['licenseSha256']
    fixtures = {case['name']: case for case in source['cases']}
    origin = json.loads((out / 'renders.json').read_text(encoding='utf-8'))
    assert origin['ok'] and origin['svgRaster']
    assert origin['sourceSha256'] == sha256(SOURCE.read_bytes()).hexdigest()
    assert origin['fontSha256'] == source['font']['sha256']
    records = origin['cases']
    assert len(records) == len(fixtures) * 2 == 26
    assert {(row['name'], row['mode']) for row in records} == {
        (name, mode) for name in fixtures for mode in ('direct', 'compiled')}
    result = dict(schema='fullbleed.dotnet.engine_regressions.verification.v1', ok=False,
                  checkedAt=datetime.now(timezone.utc).isoformat(), packageVersion=origin['packageVersion'],
                  sourceSha256=origin['sourceSha256'], fontSha256=origin['fontSha256'],
                  versions={name: version(name) for name in ('pypdf', 'pypdfium2', 'pillow')}, cases=[],
                  scope='Retained counter labels, interior colors and Chrome character geometry through direct and compiled native .NET output; not general CSS parity or standards conformance.')
    try:
        for record in records:
            case = fixtures[record['name']]
            folder = out / record['directory']
            assert folder.resolve().is_relative_to(out)
            assert record['repeatPdfBytesIdentical']
            for filename, key, text in [('input.html', 'htmlSha256', case['html']), ('style.css', 'cssSha256', case['css'])]:
                assert (folder / filename).read_bytes() == text.encode('utf-8')
                assert sha256((folder / filename).read_bytes()).hexdigest() == record[key]
            pdf = folder / 'document.pdf'
            assert sha256(pdf.read_bytes()).hexdigest() == record['pdfSha256']
            preview_groups = []
            for paths_key, hashes_key in [('previewFiles', 'previewsSha256'), ('htmlPreviewFiles', 'htmlPreviewsSha256')]:
                paths = [folder / name for name in record[paths_key]]
                assert all(path.resolve().is_relative_to(folder.resolve()) for path in paths)
                assert [sha256(path.read_bytes()).hexdigest() for path in paths] == record[hashes_key]
                preview_groups.append(paths)
            observation = check_case(case, pdf, preview_groups[0], folder)
            html_check = check_case(case, pdf, preview_groups[1], folder)
            observation['checks'].extend({**check, 'name': 'HTML ' + check['name']} for check in html_check['checks']
                                         if check['name'].startswith('preview'))
            observation['ok'] = all(check['ok'] for check in observation['checks'])
            pixels = []
            for page in range(record['pages']):
                with Image.open(folder / f'pdfium-{page + 1}.png') as image:
                    pixels.append(sha256(image.convert('RGB').tobytes()).hexdigest())
            result['cases'].append(dict(name=record['name'], family=case['family'], mode=record['mode'],
                pdfSha256=record['pdfSha256'], previewsSha256=record['previewsSha256'],
                htmlPreviewsSha256=record['htmlPreviewsSha256'], htmlSha256=record['htmlSha256'],
                cssSha256=record['cssSha256'], pdfiumPixelsSha256=pixels,
                text=[page.extract_text() for page in PdfReader(pdf).pages], **observation))
        result['failedFamilies'] = sorted({case['family'] for case in result['cases'] if not case['ok']})
        result['ok'] = not result['failedFamilies']
    finally:
        (out / 'verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    if require_correct:
        assert result['ok'], '; '.join(case['mode'] + '/' + case['name'] for case in result['cases'] if not case['ok'])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    result = verify(args.output)
    print(json.dumps(dict(ok=result['ok'], packageVersion=result['packageVersion'], cases=len(result['cases']))))


if __name__ == '__main__':
    main()
