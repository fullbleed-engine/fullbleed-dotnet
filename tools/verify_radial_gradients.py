# SPDX-License-Identifier: MIT
"""Inspect radial endpoint colors and stitching bounds in saved C# package output."""
import argparse
from hashlib import sha256
import json
from pathlib import Path

from PIL import Image
from pypdf import PdfReader
from pypdf.generic import IndirectObject
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets/radial-gradient-endpoints-2.5.43.json'
MODES = ('direct', 'compiled')


def stitching_bounds(path):
    reader = PdfReader(path)
    assert len(reader.pages) == 1 and tuple(float(value) for value in reader.pages[0].mediabox) == (0, 0, 100, 100)
    invalid, count = [], 0
    for generation, entries in reader.xref.items():
        for number in entries:
            if not number:
                continue
            value = reader.get_object(IndirectObject(number, generation, reader))
            if not hasattr(value, 'get') or value.get('/FunctionType') != 3:
                continue
            count += 1
            domain = list(map(float, value['/Domain']))
            bounds = list(map(float, value['/Bounds']))
            valid = len(domain) == 2 and domain[0] < domain[1]
            valid = valid and all(domain[0] < bound < domain[1] for bound in bounds)
            valid = valid and all(left < right for left, right in zip(bounds, bounds[1:]))
            valid = valid and len(value['/Functions']) == len(bounds) + 1 and len(value['/Encode']) == 2 * len(value['/Functions'])
            if not valid:
                invalid.append(dict(object=number, generation=generation, domain=domain, bounds=bounds))
    return dict(functions=count, invalid=invalid)


def verify(directory, *, baseline=False):
    directory = Path(directory)
    source_bytes = SOURCE.read_bytes()
    source = json.loads(source_bytes)
    renders = json.loads((directory / 'renders.json').read_text(encoding='utf-8-sig'))
    assert source['referenceVersion'] == '2.5.43'
    assert (directory / 'inputs.json').read_bytes() == source_bytes
    assert renders['inputSha256'] == sha256(source_bytes).hexdigest()
    assert baseline == (renders['packageVersion'] == '0.2.6')
    fixtures = {row['name']: row for row in source['cases']}
    expected = {(mode, name) for name in fixtures for mode in MODES}
    actual = {(row['mode'], row['name']) for row in renders['cases']}
    assert len(fixtures) == 12 and len(renders['cases']) == len(actual) == len(expected) == 24 and actual == expected
    results = []
    for row in renders['cases']:
        fixture = fixtures[row['name']]
        assert row['directory'] == row['mode'] + '-' + row['name'] and row['repeatPdfBytesMatch']
        folder = directory / row['directory']
        html, css = (folder / 'input.html').read_text(encoding='utf-8'), (folder / 'style.css').read_text(encoding='utf-8')
        assert html == fixture['html'] and css == fixture['css']
        assert sha256((html + '\0' + css).encode()).hexdigest() == fixture['inputSha256']
        path = folder / 'document.pdf'
        digest = sha256(path.read_bytes()).hexdigest()
        assert digest == row['pdfSha256'] and row['pages'] == 1
        assert len(row['previewFiles']) == len(row['previewSha256']) == 1
        preview = (folder / row['previewFiles'][0]).resolve()
        assert preview.is_relative_to(folder.resolve()) and sha256(preview.read_bytes()).hexdigest() == row['previewSha256'][0]
        with Image.open(preview) as image:
            native = image.convert('RGB')
        with pdfium.PdfDocument(path) as document:
            assert len(document) == 1
            page = document[0]
            bitmap = page.render(scale=2)
            independent = bitmap.to_pil().convert('RGB')
            bitmap.close()
            page.close()
        images = {}
        for reader, image in (('native', native), ('pdfium', independent)):
            reference = fixture['referenceImages'][reader]
            assert image.size == tuple(reference['size']) == (200, 200)
            probes = []
            for x, y in fixture['probes']:
                pixel = image.getpixel((int(x * 2), int(y * 2)))
                error = max(abs(left - right) for left, right in zip(pixel, fixture['expected']))
                probes.append(dict(point=[x, y], pixel=list(pixel), maxChannelError=error))
            # The retained smooth control already has two-level PDFium rounding.
            tolerance = 2 if reader == 'pdfium' and row['name'] == 'smooth-control' else 1
            image_hash = sha256(image.tobytes()).hexdigest()
            images[reader] = dict(size=list(image.size), rgbSha256=image_hash, probes=probes,
                                  outerColorCorrect=all(probe['maxChannelError'] <= tolerance for probe in probes),
                                  matchesCoreReference=image_hash == reference['rgbSha256'])
            if reader == 'pdfium':
                image.save(folder / 'pdfium-144.png')
        bounds = stitching_bounds(path)
        color_ok = all(item['outerColorCorrect'] for item in images.values())
        reference_matches = digest == fixture['candidatePdfSha256'] and all(item['matchesCoreReference'] for item in images.values())
        results.append(dict(name=row['name'], mode=row['mode'], directory=row['directory'],
                            pdfSha256=digest, nativePreviewSha256=row['previewSha256'][0],
                            outerColorCorrect=color_ok, coreReferenceMatches=reference_matches,
                            stitchingBounds=bounds, images=images))
    observed_failures = {(row['mode'], row['name']) for row in results if not row['outerColorCorrect']}
    expected_failures = {(mode, name) for name, fixture in fixtures.items() if fixture['affected'] for mode in MODES}
    assert len(expected_failures) == 18
    calibrated = baseline and observed_failures == expected_failures
    passed = calibrated if baseline else all(row['outerColorCorrect'] and row['coreReferenceMatches'] and not row['stitchingBounds']['invalid'] for row in results)
    report = dict(ok=passed, baseline=baseline, packageVersion=renders['packageVersion'],
                  inputSha256=renders['inputSha256'], cases=results,
                  passingColorCases=24 - len(observed_failures), failingColorCases=len(observed_failures),
                  calibratedNegativeControl=calibrated,
                  scope='Twelve radial-gradient inputs through direct and compiled C# rendering. Independent PDFium pixels, native pixels and PDF bytes are compared with the retained public 2.5.43 core reference. Type 3 stitching bounds are checked separately. No general conformance claim.')
    (directory / 'verification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    assert report['ok'], 'Radial-gradient verification failed; inspect the retained report.'
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--negative-control', action='store_true')
    args = parser.parse_args()
    report = verify(args.directory, baseline=args.negative_control)
    print(json.dumps({key: report[key] for key in ('ok', 'baseline', 'packageVersion', 'passingColorCases', 'failingColorCases')}))
