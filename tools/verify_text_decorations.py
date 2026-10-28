"""Independently inspect C# text decorations in five rendering modes."""
import argparse
from hashlib import sha256
from importlib.metadata import version
import json
from pathlib import Path

from PIL import Image
from pypdf import PdfReader
import pypdfium2 as pdfium

from decoration_inspection import check_image, glyph_geometry

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets/text-decorations-2.5.40.json'
SOURCE_SHA256 = '9af0fec002199079c988b11a7da0d2fd43e2ea261499ff0a040b277d1c2d78a0'
MODES = ('direct', 'compiled', 'fixed', 'reflow', 'compact')


def digest(path):
    return sha256(path.read_bytes()).hexdigest()


def geometry_digest(value):
    return sha256(json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')).hexdigest()


def verify(directory, require_correct=True):
    assert digest(SOURCE) == SOURCE_SHA256
    source = json.loads(SOURCE.read_text(encoding='utf-8'))
    data = json.loads((directory / 'renders.json').read_text(encoding='utf-8'))
    assert data['sourceSha256'] == digest(directory / 'inputs.json') == SOURCE_SHA256
    font = ROOT / 'samples/FullBleed.DotNet.Showcase/Assets/fonts/Inter-Variable.ttf'
    assert digest(font) == data['fontSha256'] == source['fontSha256']
    modes = MODES if require_correct else ('direct',)
    if not require_correct:
        assert data['packageVersion'] == source['publicBaseline']['package']
    expected_pairs = [(case['name'], mode) for case in source['cases'] for mode in modes]
    assert [(case['name'], case['mode']) for case in data['cases']] == expected_pairs
    fixtures = {case['name']: case for case in source['cases']}
    cases = []
    for rendered in data['cases']:
        fixture = fixtures[rendered['name']]
        mode = rendered['mode']
        expected = fixture['expected'][mode]
        folder = directory / rendered['directory']
        assert rendered['directory'] == fixture['name'] + '-' + mode
        assert digest(folder / 'input.html') == rendered['htmlSha256'] == fixture['htmlSha256']
        assert digest(folder / 'style.css') == rendered['cssSha256'] == fixture['cssSha256']
        path = folder / 'document.pdf'
        assert digest(path) == rendered['pdfSha256']
        assert digest(folder / 'undecorated.pdf') == rendered['undecoratedSha256']
        assert rendered['repeatPdfBytesIdentical']
        text = [''.join(page.extract_text().split()) for page in PdfReader(path, strict=True).pages]
        assert text == expected['text'] and len(text) == rendered['pages'] == expected['pages']
        geometry = glyph_geometry(path.read_bytes())
        assert geometry == glyph_geometry((folder / 'undecorated.pdf').read_bytes())
        geometry_sha = geometry_digest(geometry)
        assert geometry_sha == expected['glyphGeometrySha256']
        images, errors = {}, []

        def inspect(image, label):
            try:
                checked = check_image(image, fixture)
                assert checked == expected['images'][label], 'Pixels differ from the qualified public core'
                images[label] = checked
            except AssertionError as error:
                errors.append(dict(image=label, error=str(error)))

        with pdfium.PdfDocument(path) as document:
            assert len(document) == expected['pages']
            for index in range(len(document)):
                page = document[index]
                bitmap = page.render(scale=2)
                image = bitmap.to_pil()
                image.save(folder / f'pdfium-{index + 1}.png')
                inspect(image, f'pdfium-{index + 1}')
                image.close()
                bitmap.close()
                page.close()
        assert len(rendered['previewFiles']) == len(rendered['previewsSha256']) == expected['pages']
        for index, (filename, expected_sha) in enumerate(zip(rendered['previewFiles'], rendered['previewsSha256'], strict=True)):
            preview = (folder / filename).resolve()
            assert preview.is_relative_to(folder.resolve()) and digest(preview) == expected_sha
            with Image.open(preview) as image:
                inspect(image, f'finalized-{index + 1}')
        if mode == 'direct':
            assert digest(folder / 'native.png') == rendered['nativeSha256']
            with Image.open(folder / 'native.png') as image:
                inspect(image, 'native')
        else:
            assert rendered['nativeSha256'] is None
        if 'controlHtml' in fixture:
            assert digest(folder / 'control.pdf') == rendered['controlSha256']
            with pdfium.PdfDocument(folder / 'control.pdf') as control:
                assert len(control) == expected['pages']
                for index in range(len(control)):
                    page = control[index]
                    bitmap = page.render(scale=2)
                    image = bitmap.to_pil().convert('RGB')
                    with Image.open(folder / f'pdfium-{index + 1}.png') as actual:
                        assert image.tobytes() == actual.convert('RGB').tobytes(), 'Collapsed whitespace changed paint'
                    image.close()
                    bitmap.close()
                    page.close()
        cases.append(dict(name=fixture['name'], mode=mode, pages=expected['pages'], text=text,
            pdfSha256=rendered['pdfSha256'], previewsSha256=rendered['previewsSha256'], nativeSha256=rendered['nativeSha256'],
            glyphGeometrySha256=geometry_sha, decorationLeavesGlyphsUnchanged=True,
            images=images, errors=errors, ok=not errors))
    failures = [case['name'] + '-' + case['mode'] for case in cases if not case['ok']]
    result = dict(schema='fullbleed.dotnet.text_decorations.verification.v1', ok=not failures,
        packageVersion=data['packageVersion'], sourceSha256=SOURCE_SHA256, cases=cases, failures=failures,
        readers={name: version(name) for name in ('pypdf', 'pypdfium2', 'pillow')},
        scope='Sixteen explicit-font decoration inputs across direct, compiled, fixed, reflow and compact rendering. Independent PDF text, glyph positions, PDFium pixels and native previews are checked against retained public-core output. A public 0.2.3 control is restricted to the sixteen direct cases. No general platform-parity or conformance claim.')
    (directory / 'verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    if require_correct:
        assert not failures, failures
    else:
        assert set(failures) == {name + '-direct' for name in source['publicBaseline']['failedDirectCases']}
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--allow-known-failures', action='store_true')
    args = parser.parse_args()
    report = verify(args.directory.resolve(), require_correct=not args.allow_known_failures)
    print(json.dumps(dict(ok=report['ok'], cases=len(report['cases']), failures=len(report['failures']))))
