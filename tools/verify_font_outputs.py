"""Independently check fonts, text, and pixels from an isolated NuGet consumer."""
import argparse
import hashlib
from importlib import metadata
from io import BytesIO
import json
from pathlib import Path
import re

from fontTools.ttLib import TTFont
from pypdf import PdfReader
import pypdfium2 as pdfium

from font_program_checks import check_font, fonts_in
from verify_document_outputs import verify as verify_document_outputs

ROOT = Path(__file__).resolve().parents[1]
CASES = [
    ('invoice', 1, 'PdfSha256', 'finalized', 'FinalizedPreviewSha256'),
    ('invoice-explicit', 1, 'ExplicitInvoicePdfSha256', 'finalized-explicit', 'ExplicitInvoicePreviewSha256'),
    ('bold', 1, 'BoldPdfSha256', 'finalized-bold', 'BoldPreviewSha256'),
    ('records', 2, 'CompiledPdfSha256', 'finalized-records', 'CompiledPreviewsSha256'),
    ('reflow', 2, 'ReflowPdfSha256', 'finalized-reflow', 'ReflowPreviewsSha256'),
    ('unicode', 1, 'UnicodePdfSha256', 'finalized-unicode', 'UnicodePreviewsSha256'),
]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def compact(text):
    return re.sub(r'\s+', '', text)


def verify(directory, baseline=None, legacy=False, report_path=None, legacy_family=False):
    directory = Path(directory).resolve()
    evidence = json.loads((directory / 'evidence.json').read_text(encoding='utf-8-sig'))
    package = json.loads((directory / 'package.json').read_text(encoding='utf-8-sig'))
    inputs = json.loads((directory / 'font-inputs.json').read_text(encoding='utf-8'))
    assert sha((directory / 'font-inputs.json').read_bytes()) == evidence['FontInputsSha256']
    previous = json.loads((Path(baseline) / 'font-verification.json').read_text(encoding='utf-8')) if baseline else None
    if previous:
        assert previous['ok'] and previous['inputs'] == inputs, 'Before/after inputs differ'
    result = dict(schema='fullbleed.dotnet.font_verification.v1', ok=False,
                  packageVersion=evidence['BindingVersion'], packageSha256=package['sha256'],
                  runtime=evidence['Runtime'], framework=evidence['FrameworkVersion'],
                  inputs=inputs, legacyMetadataAllowed=legacy, legacyFamilyDefaultAllowed=legacy_family,
                  baselinePackageVersion=previous['packageVersion'] if previous else None,
                  readers={name: metadata.version(name) for name in ['pypdf', 'pypdfium2', 'fonttools', 'pillow']},
                  fixtures=[], scope='Retained synthetic documents: font programs, text and page pixels. No speed, general parity or conformance claim.')
    destination = report_path or directory / 'font-verification.json'
    try:
        result['documentChecks'] = verify_document_outputs(directory)
        sources = list((ROOT / 'samples/FullBleed.DotNet.Showcase/Assets/fonts').glob('*.ttf'))
        sources.append(ROOT / 'tests/FullBleed.DotNet.Tests/Assets/NotoSans-Regular.ttf')
        expected = {item['Name']: item['Sha256'] for item in inputs['Fonts']}
        assert len(expected) == len(sources) == 5
        originals = {}
        for source in sources:
            assert sha(source.read_bytes()) == expected[source.name]
            with TTFont(source) as font:
                key = font.reader['name']
                assert key not in originals
                originals[key] = source
        fonts_seen = set()
        for name, pages, pdf_key, png_folder, png_key in CASES:
            pdf = directory / (name + '.pdf')
            assert sha(pdf.read_bytes()) == evidence[pdf_key]
            preview_hashes = evidence[png_key]
            if isinstance(preview_hashes, str):
                preview_hashes = [preview_hashes]
            previews = sorted((directory / png_folder).glob('*.png'))
            assert len(previews) == len(preview_hashes) == pages
            assert [sha(file.read_bytes()) for file in previews] == preview_hashes
            reader = PdfReader(pdf, strict=True)
            assert len(reader.pages) == pages
            text = [page.extract_text() for page in reader.pages]
            assert all(value.strip() for value in text)
            if name == 'unicode':
                assert compact(text[0]) == compact(inputs['Unicode']['Text'])
            if name == 'reflow':
                for actual, record in zip(text, inputs['Reflow']['Records'], strict=True):
                    assert compact(actual) == compact(f'Record {record["Id"]} {record["Story"]}')
            checked, seen = [], set()
            for page in reader.pages:
                for font, descendant, stream in fonts_in(page['/Resources'], seen):
                    with TTFont(BytesIO(stream.get_data())) as embedded:
                        source = originals[embedded.reader['name']]
                    item = check_font(font, descendant, stream, source, legacy)
                    item['source'] = source.name
                    checked.append(item)
                    fonts_seen.add(source.name)
            assert checked
            if name in {'invoice', 'invoice-explicit'}:
                expected_faces = {'BebasNeue-Regular.ttf', 'DMSerifDisplay-Italic.ttf', 'Inter-Variable.ttf'}
                if name == 'invoice-explicit' or not legacy_family:
                    expected_faces.add('DMSerifDisplay-Regular.ttf')
                assert {item['source'] for item in checked} == expected_faces, (name, 'unexpected invoice font faces')
                if name == 'invoice-explicit':
                    assert text == result['fixtures'][0]['text'], 'Explicit invoice control changed text'
            if name == 'unicode':
                assert {item['source'] for item in checked} == {'NotoSans-Regular.ttf'}
            pixel_hashes = []
            rendered = directory / ('pdfium-' + name)
            rendered.mkdir(exist_ok=True)
            with pdfium.PdfDocument(pdf) as document:
                assert len(document) == pages
                for index in range(pages):
                    page = document[index]
                    text_page = page.get_textpage()
                    assert compact(text_page.get_text_range()) == compact(text[index]), (name, index)
                    text_page.close()
                    bitmap = page.render(scale=1.25)
                    image = bitmap.to_pil()
                    pixel_hashes.append(sha(image.tobytes()))
                    image.save(rendered / f'page-{index + 1}.png')
                    bitmap.close()
                    page.close()
            item = dict(name=name, pages=pages, pdfBytes=pdf.stat().st_size,
                        pdfSha256=evidence[pdf_key], nativePreviewSha256=preview_hashes,
                        pdfiumPixelSha256=pixel_hashes, text=text, embeddedFonts=checked)
            if previous:
                prior = next(value for value in previous['fixtures'] if value['name'] == name)
                for field in ['pages', 'text', 'nativePreviewSha256', 'pdfiumPixelSha256']:
                    assert item[field] == prior[field], (name, field)
                assert item['pdfBytes'] < prior['pdfBytes'], name
                item['beforePdfBytes'] = prior['pdfBytes']
                item['reductionPercent'] = round(100 * (1 - item['pdfBytes'] / prior['pdfBytes']), 2)
                item['baselineTextAndPixelsIdentical'] = True
            result['fixtures'].append(item)
        assert fonts_seen == {'BebasNeue-Regular.ttf', 'DMSerifDisplay-Italic.ttf', 'DMSerifDisplay-Regular.ttf',
                              'Inter-Variable.ttf', 'NotoSans-Regular.ttf'}, fonts_seen
        result['verifiedSourceFonts'] = sorted(fonts_seen)
        result['registeredButUnusedSourceFonts'] = sorted(set(expected) - fonts_seen)
        result['ok'] = True
    finally:
        Path(destination).write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--allow-legacy-metadata', action='store_true')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = verify(args.directory, args.baseline, args.allow_legacy_metadata, args.report)
    print(json.dumps({'ok': report['ok'], 'package': report['packageVersion'], 'fixtures': len(report['fixtures'])}))
