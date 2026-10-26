"""Reject pagination defects in the actual public 0.2.0 NuGet package."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from verify_pagination_outputs import verify
from review_engine_upgrade import review

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.2.0'
PACKAGE_SHA256 = '78bc785508dfa2d409a35b32d3ef087481c05c9212bbc2ed2928a587136b80f8'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def compare(previous, current):
    assert previous['packageVersion'] == VERSION and current['packageVersion'] != VERSION
    assert previous['sourceSha256'] == current['sourceSha256']
    assert current['ok'] and not previous['ok']
    controls = []
    failed = []
    for old, new in zip(previous['cases'], current['cases'], strict=True):
        for key in ['name', 'mode', 'htmlSha256', 'cssSha256']:
            assert old[key] == new[key], (key, old['name'])
        label = old['mode'] + '/' + old['name']
        if old['name'] == 'pagination-break-fixed':
            assert old['ok']
            for key in ['pdfSha256', 'previewsSha256', 'htmlPreviewsSha256', 'pdfiumPixelsSha256', 'text', 'pdfiumText']:
                assert old[key] == new[key], (key, label, 'healthy fixed-height control changed')
            controls.append(label)
        else:
            assert not old['ok'], (label, 'expected known old pagination defect')
            failed.append(label)
    assert len(failed) == 16 and len(controls) == 2
    return dict(ok=True, baselinePackage=VERSION, baselineEngine='2.5.22',
                candidatePackage=current['packageVersion'], baselineFailedCases=failed,
                candidateCases=len(current['cases']), candidateFailedCases=0,
                unchangedInputs=True, unchangedHealthyControls=controls,
                scope=current['scope'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/pagination-comparison')
    parser.add_argument('--baseline-package', type=Path)
    parser.add_argument('--baseline-consumer', type=Path)
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
        assert next(p for p in provenance['dependencies'] if p['name'] == 'fullbleed')['version'] == '2.5.22'
    (output / 'baseline-source.json').write_text(json.dumps(dict(
        url=URL, sha256=PACKAGE_SHA256, bytes=len(data), engine='2.5.22'), indent=2) + '\n', encoding='utf-8')
    baseline = args.baseline_consumer.resolve() if args.baseline_consumer else output / 'baseline'
    if not args.baseline_consumer:
        command = [args.shell, '-NoProfile', '-File', str(ROOT / 'scripts/package-smoke.ps1'), '-SkipPack',
                   '-PackageVersion', VERSION, '-PackageDirectory', str(packages), '-OutputDirectory', str(baseline)]
        with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    identity = json.loads((baseline / 'package.json').read_text(encoding='utf-8-sig'))
    assert identity['sha256'] == PACKAGE_SHA256
    previous = verify(baseline / 'pagination', require_correct=False)
    current = verify(args.candidate.resolve() / 'pagination')
    report = compare(previous, current)
    legacy = review(baseline, args.candidate.resolve(), output / 'legacy-upgrade-review.json')
    report['legacyUpgradeReview'] = {'unchangedFiles': legacy['unchangedFiles'], 'reviewedTaggedPdfs': len(legacy['changes'])}
    (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
