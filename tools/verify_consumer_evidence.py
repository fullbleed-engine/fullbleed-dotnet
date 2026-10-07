"""Compare retained output from consumers of the same assembled package."""
import argparse
import hashlib
import json
from itertools import product
from pathlib import Path

from verify_font_outputs import verify as verify_font_outputs
from verify_font_families import verify as verify_font_families
from verify_inline_layout import verify as verify_inline_layout
from verify_standard_fonts import verify as verify_standard_fonts

ROOT = Path(__file__).resolve().parents[1]
RIDS = ['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64']
FRAMEWORKS = ['8.0', '9.0', '10.0']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--consumers', type=Path, default=ROOT / 'artifacts/consumer-evidence')
    parser.add_argument('--packages', type=Path, default=ROOT / 'artifacts/packages')
    args = parser.parse_args()
    manifest = json.loads((args.packages / 'package-manifest.json').read_text(encoding='utf-8'))
    records = []
    inputs = []
    families = []
    inline_cases = []
    standard_cases = []
    for rid, framework in product(RIDS, FRAMEWORKS):
        consumer = f'{rid}-net{framework}'
        directory = args.consumers / ('package-consumer-' + consumer)
        data = json.loads((directory / 'evidence.json').read_text(encoding='utf-8-sig'))
        package = json.loads((directory / 'package.json').read_text(encoding='utf-8-sig'))
        assert package['sha256'] == manifest['package']['sha256'], f'{consumer} consumed a different package'
        assert package['target_framework'] == f'net{framework}'
        assert package['sdk_version'].startswith(framework + '.')
        assert data['TargetFramework'] == f'.NETCoreApp,Version=v{framework}'
        assert data['FrameworkVersion'].startswith(framework + '.'), f'{consumer} ran on the wrong .NET runtime'
        assert data['BindingVersion'] == manifest['version']
        assert data['Runtime'] == rid
        assert data['PageCount'] == 1 and data['MissingGlyphs'] == 0 and data['CompiledRecords'] == 2
        assert hashlib.sha256((directory / 'invoice.pdf').read_bytes()).hexdigest() == data['PdfSha256']
        previews = list(directory.glob('invoice*.png'))
        assert len(previews) == 1
        assert hashlib.sha256(previews[0].read_bytes()).hexdigest() == data['PreviewSha256']
        finalized = list((directory / 'finalized').glob('invoice*.png'))
        assert len(finalized) == 1
        assert hashlib.sha256(finalized[0].read_bytes()).hexdigest() == data['FinalizedPreviewSha256']
        assert hashlib.sha256((directory / 'bold.pdf').read_bytes()).hexdigest() == data['BoldPdfSha256']
        bold_previews = list((directory / 'finalized-bold').glob('bold*.png'))
        assert len(bold_previews) == 1
        assert hashlib.sha256(bold_previews[0].read_bytes()).hexdigest() == data['BoldPreviewSha256']
        assert data['ReflowRecords'] == 2
        font_report = verify_font_outputs(directory)
        family_report = verify_font_families(directory / 'font-families')
        assert len(family_report['cases']) == 22
        families.append(family_report['cases'])
        inline_report = verify_inline_layout(directory / 'inline-layout')
        assert len(inline_report['cases']) == 54
        inline_cases.append(inline_report['cases'])
        standard_report = verify_standard_fonts(directory / 'standard-fonts')
        assert standard_report['packageVersion'] == manifest['version']
        standard_cases.append(standard_report['cases'])
        data['IndependentStandardFontChecks'] = {'status': 'passed', 'cases': len(standard_report['cases'])}
        data['IndependentInlineChecks'] = {'status': 'passed', 'cases': len(inline_report['cases'])}
        inputs.append(font_report['inputs'])
        data['IndependentDocumentChecks'] = font_report['documentChecks']
        data['IndependentFontChecks'] = {'status': 'passed', 'fixtures': len(font_report['fixtures']),
                                        'verifiedSourceFonts': font_report['verifiedSourceFonts']}
        data['IndependentFamilyChecks'] = {'status': 'passed', 'cases': len(family_report['cases'])}
        records.append(data)
    for field in ['PdfSha256', 'PreviewSha256', 'FinalizedPreviewSha256', 'BoldPdfSha256', 'BoldPreviewSha256',
                  'ExplicitInvoicePdfSha256', 'ExplicitInvoicePreviewSha256',
                  'CompiledPdfSha256', 'CompiledPreviewsSha256', 'ReflowPdfSha256', 'ReflowPreviewsSha256',
                  'UnicodePdfSha256', 'UnicodePreviewsSha256']:
        assert len({json.dumps(record[field], sort_keys=True) for record in records}) == 1, f'Platform or .NET runtime output differs: {field}'
    # System.Text.Json uses platform line endings for indented JSON. Each file's
    # raw hash is checked above; compare its exact parsed values across platforms.
    assert all(item == inputs[0] for item in inputs), 'Platform or .NET runtime fixture inputs differ'
    for cases in families:
        assert [item['name'] for item in cases] == [item['name'] for item in families[0]]
        for case, expected in zip(cases, families[0], strict=True):
            for key in ['pdfSha256', 'previewsSha256', 'pdfiumPixelsSha256', 'faces', 'text', 'htmlSha256', 'cssSha256']:
                assert case[key] == expected[key], (case['name'], 'platform or runtime differs', key)
    for cases in inline_cases:
        assert [item['name'] for item in cases] == [item['name'] for item in inline_cases[0]]
        for case, expected in zip(cases, inline_cases[0], strict=True):
            for key in ['pdfSha256', 'pdfiumPixelsSha256', 'pypdfText', 'pdfiumText', 'htmlSha256', 'cssSha256', 'previewFontScope', 'previewSha256']:
                assert case[key] == expected[key], (case['name'], 'platform or runtime differs', key)
    for cases in standard_cases:
        assert [item['name'] for item in cases] == [item['name'] for item in standard_cases[0]]
        for case, expected in zip(cases, standard_cases[0], strict=True):
            for key in ['pdfSha256', 'pdfiumPixelsSha256', 'text', 'faces', 'htmlSha256', 'cssSha256',
                        'previewSha256', 'htmlPreviewSha256']:
                assert case[key] == expected[key], (case['name'], 'platform or runtime differs', key)
    base14_previews = {}
    for record, cases in zip(records, inline_cases, strict=True):
        for case in cases:
            if case['previewFontScope'] == 'unembedded-base14':
                by_rid = base14_previews.setdefault(case['name'], {})
                expected = by_rid.setdefault(record['Runtime'], case['previewSha256'])
                assert case['previewSha256'] == expected, (case['name'], 'standard-font native preview differs between .NET runtimes on the same platform')
    result = {'schema': 'fullbleed.dotnet.package_consumers.v1', 'status': 'passed',
              'package_sha256': manifest['package']['sha256'], 'consumers': records,
              'unembeddedStandardFontNativePreviewsByRid': base14_previews,
              'standardFontFixtures': standard_cases[0],
              'scope': 'Northstar invoice PDF, HTML preview and finalized-PDF 96-DPI PNG match on .NET 8, 9 and 10 '
                       'across Windows x64, Linux x64, Intel macOS and Apple Silicon macOS. '
                       'Bold, fixed-record, reflow-record and Unicode fixture PDFs and saved-PDF previews also match. '
                       'pypdf, PDFium and FontTools independently verify text and embedded font programs in all six fixtures. '
                       'Twenty-two regular/italic family cases match explicit-face controls and agree across all consumers. '
                       'Fifty-four inline cases pass text/geometry checks; their PDF bytes and independent PDFium pixels agree across all consumers. '
                       'All 54 inline native previews agree across platforms, including eight unembedded Helvetica/Times cases. '
                       'Fifty-two standard-font cases agree in PDF bytes, independent PDFium pixels and native PNG bytes: '
                       'twelve unembedded Latin faces and an embedded control, each through ordinary, fixed, reflow and compact rendering. '
                       'These retained fixtures are not a universal platform-parity or conformance claim.'}
    path = ROOT / 'artifacts/cross-platform-evidence.json'
    path.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'{len(records)} isolated package consumers passed; fixture PDFs and native previews match across platforms.')


if __name__ == '__main__':
    main()
