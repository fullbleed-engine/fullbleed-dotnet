"""Retain a real public-package negative control for the tagged specimen."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from verify_tagged_structure import validator, verify

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.1.6'
PACKAGE_SHA256 = 'eff0aec8678dc2b974c8bd9f1a98cba5e4583f0d67e84ab1f9ae70b4b40c848d'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def compare(previous, current):
    assert previous['packageVersion'] == VERSION and current['packageVersion'] != VERSION
    assert previous['inputs'] == current['inputs']
    assert current['ok'] and not previous['ok']
    failed = {}
    for old, new in zip(previous['cases'], current['cases'], strict=True):
        assert old['name'] == new['name']
        assert old['text'] == new['text'] and old['previewSha256'] == new['previewSha256'], old['name']
        if old['name'] == 'none':
            assert old['pdfSha256'] == new['pdfSha256'], 'Ordinary control PDF changed'
        else:
            assert not old['veraPDF']['compliant'] and old['veraPDF']['failedChecks'] > 0
            assert new['veraPDF']['compliant'] and new['veraPDF']['failedChecks'] == 0
            failed[old['name']] = old['veraPDF']['failedChecks']
    assert set(failed) == {'ua1', 'ua2'}
    return {'ok': True, 'baselinePackage': VERSION, 'candidatePackage': current['packageVersion'],
            'baselineFailedChecks': failed, 'candidateFailedChecks': {'ua1': 0, 'ua2': 0},
            'ordinaryPdfUnchanged': True, 'textAndPreviewsUnchanged': True,
            'scope': 'One retained rich specimen, two PDF/UA profiles and an ordinary control. Machine checks, not full accessibility acceptance.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/tagged-structure-comparison')
    parser.add_argument('--verapdf-cp', type=Path)
    parser.add_argument('--shell', default='pwsh')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    packages = output / 'baseline-packages'
    packages.mkdir()
    with urlopen(URL, timeout=120) as response:
        data = response.read()
    assert sha256(data).hexdigest() == PACKAGE_SHA256, 'Previous public package changed'
    package = packages / f'FullBleed.DotNet.{VERSION}.nupkg'
    package.write_bytes(data)
    with zipfile.ZipFile(package) as archive:
        provenance = json.loads(archive.read('native-provenance.json'))
        assert next(p for p in provenance['dependencies'] if p['name'] == 'fullbleed')['version'] == '2.5.11'
    (output / 'baseline-source.json').write_text(json.dumps({'url': URL, 'sha256': PACKAGE_SHA256,
        'bytes': len(data), 'engine': '2.5.11'}, indent=2) + '\n', encoding='utf-8')
    command = [args.shell, '-NoProfile', '-File', str(ROOT / 'scripts/package-smoke.ps1'), '-SkipPack',
               '-PackageVersion', VERSION, '-PackageDirectory', str(packages), '-OutputDirectory', str(output / 'baseline')]
    with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
        subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    classpath = args.verapdf_cp or validator(ROOT / 'artifacts/verapdf')
    previous = verify(output / 'baseline/tagged-structure', classpath, require_compliant=False)
    current = verify(args.candidate.resolve() / 'tagged-structure', classpath)
    report = compare(previous, current)
    (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
