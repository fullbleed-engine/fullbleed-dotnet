"""Compare retained output from consumers of the same assembled package."""
import hashlib
import json
from itertools import product
from pathlib import Path

from verify_font_outputs import verify as verify_font_outputs

ROOT = Path(__file__).resolve().parents[1]
RIDS = ['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64']
FRAMEWORKS = ['8.0', '9.0', '10.0']


def main():
    manifest = json.loads((ROOT / 'artifacts/packages/package-manifest.json').read_text(encoding='utf-8'))
    records = []
    for rid, framework in product(RIDS, FRAMEWORKS):
        consumer = f'{rid}-net{framework}'
        directory = ROOT / 'artifacts/consumer-evidence' / ('package-consumer-' + consumer)
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
        data['IndependentDocumentChecks'] = font_report['documentChecks']
        data['IndependentFontChecks'] = {'status': 'passed', 'fixtures': len(font_report['fixtures']),
                                        'verifiedSourceFonts': font_report['verifiedSourceFonts']}
        records.append(data)
    for field in ['PdfSha256', 'PreviewSha256', 'FinalizedPreviewSha256', 'BoldPdfSha256', 'BoldPreviewSha256',
                  'CompiledPdfSha256', 'CompiledPreviewsSha256', 'ReflowPdfSha256', 'ReflowPreviewsSha256',
                  'UnicodePdfSha256', 'UnicodePreviewsSha256', 'FontInputsSha256']:
        assert len({json.dumps(record[field], sort_keys=True) for record in records}) == 1, f'Platform or .NET runtime output differs: {field}'
    result = {'schema': 'fullbleed.dotnet.package_consumers.v1', 'status': 'passed',
              'package_sha256': manifest['package']['sha256'], 'consumers': records,
              'scope': 'Northstar invoice PDF, HTML preview and finalized-PDF 96-DPI PNG match on .NET 8, 9 and 10 '
                       'across Windows x64, Linux x64, Intel macOS and Apple Silicon macOS. '
                       'Bold, fixed-record, reflow-record and Unicode fixture PDFs and saved-PDF previews also match. '
                       'pypdf, PDFium and FontTools independently verify text and embedded font programs in all five fixtures. '
                       'These retained fixtures are not a universal platform-parity or conformance claim.'}
    path = ROOT / 'artifacts/cross-platform-evidence.json'
    path.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'{len(records)} isolated package consumers passed; font/text checks passed and PDF/PNG bytes match.')


if __name__ == '__main__':
    main()
