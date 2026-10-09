"""Reject known layout defects in the actual public 0.1.7 NuGet package."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from verify_engine_regressions import verify

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.1.7'
PACKAGE_SHA256 = '4334469bb70fe5896c479f9bb68025cd0fefafa280429dd367055f1818ecd1ee'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/engine-regression-comparison')
    parser.add_argument('--baseline-package', type=Path)
    parser.add_argument('--shell', default='pwsh')
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    packages = output / 'baseline-packages'
    packages.mkdir()
    if args.baseline_package:
        data = args.baseline_package.read_bytes()
    else:
        with urlopen(URL, timeout=120) as response:
            data = response.read()
    assert sha256(data).hexdigest() == PACKAGE_SHA256, 'Previous public package changed'
    package = packages / f'FullBleed.DotNet.{VERSION}.nupkg'
    package.write_bytes(data)
    with zipfile.ZipFile(package) as archive:
        provenance = json.loads(archive.read('native-provenance.json'))
        assert next(p for p in provenance['dependencies'] if p['name'] == 'fullbleed')['version'] == '2.5.13'
    (output / 'baseline-source.json').write_text(json.dumps(dict(
        url=URL, sha256=PACKAGE_SHA256, bytes=len(data), engine='2.5.13'), indent=2) + '\n', encoding='utf-8')
    command = [args.shell, '-NoProfile', '-File', str(ROOT / 'scripts/package-smoke.ps1'), '-SkipPack',
               '-PackageVersion', VERSION, '-PackageDirectory', str(packages), '-OutputDirectory', str(output / 'baseline')]
    with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
        subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    previous = verify(output / 'baseline/engine-regressions', require_correct=False)
    current = verify(args.candidate.resolve() / 'engine-regressions')
    assert previous['packageVersion'] == VERSION and current['packageVersion'] != VERSION
    assert previous['sourceSha256'] == current['sourceSha256']
    assert previous['fontSha256'] == current['fontSha256']
    assert set(previous['failedFamilies']) == {'counter', 'overflow', 'border-image', 'first-letter'}
    assert current['ok'] and not previous['ok']
    for old, new in zip(previous['cases'], current['cases'], strict=True):
        for key in ['name', 'mode', 'htmlSha256', 'cssSha256']:
            assert old[key] == new[key], (key, old['name'])
    failed = [f"{case['mode']}/{case['name']}" for case in previous['cases'] if not case['ok']]
    report = dict(ok=True, baselinePackage=VERSION, baselineEngine='2.5.13',
                  candidatePackage=current['packageVersion'], baselineFailedCases=failed,
                  failedFamilies=previous['failedFamilies'], candidateCases=len(current['cases']),
                  candidateFailedCases=0, unchangedInputs=True,
                  scope='Thirteen retained layouts through direct and compiled .NET APIs. Text, color and geometry checks cover these fixtures only.')
    (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
