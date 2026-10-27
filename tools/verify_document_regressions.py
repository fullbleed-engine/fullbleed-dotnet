"""Independently inspect imported-PDF previews and generated quotation output."""
import argparse
from hashlib import sha256
from importlib.metadata import version
import json
from pathlib import Path

from PIL import Image, ImageChops
from pypdf import PdfReader
import pypdfium2 as pdfium

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'tests/FullBleed.DotNet.PackageSmoke/Assets'
PREVIEW_SOURCE = ASSETS / 'pdf-preview-subsets-2.5.39.json'
QUOTE_SOURCE = ASSETS / 'generated-quotes-2.5.38.json'
PREVIEW_SHA256 = '06335e162f8f9ad7b1376e33ca4ffca327aa8f661f05442e50db1421822c6ad6'
QUOTE_SHA256 = '2dc8b74a9cbc43cdaac5d12a2d9e3d6aa239ccb00298454aa7ce2958b6496614'

def digest(path):
    return sha256(path.read_bytes()).hexdigest()

def normalized(text):
    return ''.join(text.split())

def inspect(path, expected_pages):
    reader = PdfReader(path, strict=True)
    assert len(reader.pages) == expected_pages
    texts, pixels = [], []
    with pdfium.PdfDocument(path) as document:
        assert len(document) == expected_pages
        for index, page in enumerate(document):
            text = page.get_textpage()
            try:
                first = normalized(reader.pages[index].extract_text())
                second = normalized(text.get_text_range())
                assert first == second, (path.name, index, 'Independent readers disagree')
                texts.append(first)
                bitmap = page.render(scale=1)
                try:
                    image = bitmap.to_pil().convert('RGB')
                    assert ImageChops.difference(image, Image.new('RGB', image.size, 'white')).getbbox() is not None
                    pixels.append(sha256(image.tobytes()).hexdigest())
                    image.save(path.parent / ('pdfium-' + str(index + 1) + '.png'))
                finally:
                    bitmap.close()
            finally:
                text.close()
    return texts, pixels

def previews(folder, case):
    assert len(case['previewFiles']) == len(case['previewsSha256']) == case['pages']
    observed = []
    for name, declared in zip(case['previewFiles'], case['previewsSha256'], strict=True):
        path = (folder / name).resolve()
        assert path.is_relative_to(folder.resolve())
        assert digest(path) == declared
        with Image.open(path) as source:
            image = source.convert('RGB')
            assert image.width > 0 and image.height > 0
            assert ImageChops.difference(image, Image.new('RGB', image.size, 'white')).getbbox() is not None
        observed.append(declared)
    return observed

def verify(directory, require_correct=True):
    directory = directory.resolve()
    assert digest(PREVIEW_SOURCE) == PREVIEW_SHA256
    assert digest(QUOTE_SOURCE) == QUOTE_SHA256
    assert digest(directory / 'preview-inputs.json') == PREVIEW_SHA256
    assert digest(directory / 'quote-inputs.json') == QUOTE_SHA256
    preview_source = json.loads(PREVIEW_SOURCE.read_text(encoding='utf-8'))
    quote_source = json.loads(QUOTE_SOURCE.read_text(encoding='utf-8'))
    preview_data = json.loads((directory / 'previews.json').read_text(encoding='utf-8-sig'))
    quote_data = json.loads((directory / 'quotes.json').read_text(encoding='utf-8-sig'))
    assert preview_data['packageVersion'] == quote_data['packageVersion']
    assert preview_data['sourceSha256'] == PREVIEW_SHA256 and quote_data['sourceSha256'] == QUOTE_SHA256
    assert quote_data['fontSha256'] == quote_source['fontSha256']
    assert len(preview_data['cases']) == len(preview_source['cases']) == 39
    assert len(quote_data['cases']) == len(quote_source['cases']) * 3 == 123
    preview_cases, quote_cases = [], []
    failed_preview_pages = []
    by_name = {}
    for case, fixture in zip(preview_data['cases'], preview_source['cases'], strict=True):
        assert case['name'] == fixture['name'] and case['pages'] == fixture['pages'] and case['dpi'] == fixture['dpi']
        assert case['repeatPreviewBytesIdentical'] and case['sourceUnchanged']
        folder = (directory / case['directory']).resolve()
        assert folder.is_relative_to(directory)
        path = folder / 'document.pdf'
        assert digest(path) == case['pdfSha256'] == fixture['pdfSha256']
        text, pixels = inspect(path, case['pages'])
        assert text == fixture['expectedText'], case['name']
        observed = previews(folder, case)
        matching = [actual == expected for actual, expected in zip(observed, fixture['expectedNativePreviewSha256'], strict=True)]
        failed_preview_pages.extend(case['name'] + '-page-' + str(index + 1) + '.png' for index, correct in enumerate(matching) if not correct)
        row = dict(name=case['name'], pages=case['pages'], pdfSha256=case['pdfSha256'], previewsSha256=observed,
                   pdfiumPixelsSha256=pixels, text=text, previewMatches=matching, ok=all(matching))
        preview_cases.append(row)
        by_name[case['name']] = row
    equivalent = []
    for pair in preview_source['equivalentPreviews']:
        left, right = by_name[pair['left']], by_name[pair['right']]
        matches = left['previewsSha256'][pair['leftPage']] == right['previewsSha256'][pair['rightPage']]
        equivalent.append(dict(**pair, matches=matches))
    assert len(equivalent) == 12
    expected_keys = [(mode, case['name']) for mode in ['direct', 'compiled', 'reflow'] for case in quote_source['cases']]
    assert [(case['mode'], case['name']) for case in quote_data['cases']] == expected_keys
    authored = {case['name']: case for case in quote_source['cases']}
    for case in quote_data['cases']:
        fixture = authored[case['name']]
        mode = case['mode']
        assert case['repeatPdfBytesIdentical'] and case['pages'] == (2 if mode == 'reflow' else 1)
        folder = (directory / case['directory']).resolve()
        assert folder.is_relative_to(directory)
        html = fixture['html'].replace('Record Ada', 'Record {{name}}') if mode == 'reflow' else fixture['html']
        assert (folder / 'input.html').read_text(encoding='utf-8') == html
        assert (folder / 'style.css').read_text(encoding='utf-8') == fixture['css']
        assert digest(folder / 'input.html') == case['htmlSha256']
        assert digest(folder / 'style.css') == case['cssSha256']
        path = folder / 'document.pdf'
        assert digest(path) == case['pdfSha256']
        text, pixels = inspect(path, case['pages'])
        expected = [fixture['expectedText']]
        if mode == 'reflow':
            expected.append(fixture['expectedText'].replace('RecordAda', 'RecordBea', 1))
        observed = previews(folder, case)
        correct = text == expected
        direct_matches = case['pdfSha256'] == fixture['qualifiedDirectPdfSha256'] if mode == 'direct' else None
        quote_cases.append(dict(name=case['name'], mode=mode, pages=case['pages'], ok=correct,
            text=text, expectedText=expected, pdfSha256=case['pdfSha256'], previewsSha256=observed,
            pdfiumPixelsSha256=pixels, htmlSha256=case['htmlSha256'], cssSha256=case['cssSha256'],
            directPdfMatchesQualifiedCore=direct_matches))
    assert sum(row['pages'] for row in preview_cases) == 43
    assert sum(row['pages'] for row in quote_cases) == 164
    failed_quotes = [case['mode'] + '/' + case['name'] for case in quote_cases if not case['ok']]
    correct = not failed_preview_pages and not failed_quotes and all(pair['matches'] for pair in equivalent)
    result = dict(schema='fullbleed.dotnet.document_regressions.verification.v1', ok=correct,
        packageVersion=preview_data['packageVersion'], previewSourceSha256=PREVIEW_SHA256, quoteSourceSha256=QUOTE_SHA256,
        previewCases=preview_cases, quotationCases=quote_cases, equivalentPreviews=equivalent,
        failedPreviewPages=failed_preview_pages, failedQuotationCases=failed_quotes,
        readers=dict(pypdf=version('pypdf'), pypdfium2=version('pypdfium2')),
        scope='Thirty-nine imported PDFs with 43 native previews, plus 41 generated-quotation inputs in direct, compiled and two-record reflow modes. Independent text and raster inspection, source preservation, repeatability and native pair controls. No general raster-parity, accessibility or conformance claim.')
    (directory / 'verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    if require_correct:
        assert correct, dict(previewFailures=failed_preview_pages, quotationFailures=failed_quotes)
    return result

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--allow-known-failures', action='store_true')
    args = parser.parse_args()
    report = verify(args.directory, require_correct=not args.allow_known_failures)
    print(json.dumps(dict(ok=report['ok'], previewPdfs=len(report['previewCases']), previewPages=43,
        quotationCases=len(report['quotationCases']), quotationPages=164,
        failedPreviewPages=len(report['failedPreviewPages']), failedQuotationCases=len(report['failedQuotationCases']))))

if __name__ == '__main__':
    main()
