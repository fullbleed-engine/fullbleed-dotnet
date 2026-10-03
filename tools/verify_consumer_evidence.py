"""Compare retained output from consumers of the same assembled package."""
import hashlib
import json
from pathlib import Path

from verify_document_outputs import verify as verify_document_outputs

ROOT = Path(__file__).resolve().parents[1]
RIDS = ['win-x64', 'linux-x64', 'osx-x64', 'osx-arm64']


def main():
    manifest = json.loads((ROOT / 'artifacts/packages/package-manifest.json').read_text())
    records = []
    for rid in RIDS:
        directory = ROOT / 'artifacts/consumer-evidence' / ('package-consumer-' + rid)
        data = json.loads((directory / 'evidence.json').read_text(encoding='utf-8-sig'))
        package = json.loads((directory / 'package.json').read_text(encoding='utf-8-sig'))
        assert package['sha256'] == manifest['package']['sha256'], f'{rid} consumed a different package'
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
        data['IndependentDocumentChecks'] = verify_document_outputs(directory)
        records.append(data)
    for field in ['PdfSha256', 'PreviewSha256', 'FinalizedPreviewSha256']:
        assert len({record[field] for record in records}) == 1, f'Platform output differs: {field}'
    result = {'schema': 'fullbleed.dotnet.package_consumers.v1', 'status': 'passed',
              'package_sha256': manifest['package']['sha256'], 'consumers': records,
              'scope': 'Northstar invoice PDF, HTML preview and finalized-PDF 96-DPI PNG match on these four native runtimes. '
                       'pypdf independently verifies searchable styled text and two compiled records. '
                       'This fixture is not a universal platform-parity or conformance claim.'}
    path = ROOT / 'artifacts/cross-platform-evidence.json'
    path.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print('Four isolated package consumers passed; PDF text checks passed and PDF/PNG bytes match.')


if __name__ == '__main__':
    main()
