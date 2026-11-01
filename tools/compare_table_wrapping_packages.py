"""Calibrate table checks against the actual public 0.2.1 NuGet package."""
import argparse
from hashlib import sha256
import json
from pathlib import Path
import subprocess
from urllib.request import urlopen
import zipfile

from verify_table_wrapping import SOURCE, verify

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.2.1'
PACKAGE_SHA256 = 'ded49c84b7883329b09b1cafae52730fe9f1961a009135611e2b3826ae0fc7d1'
URL = f'https://api.nuget.org/v3-flatcontainer/fullbleed.dotnet/{VERSION}/fullbleed.dotnet.{VERSION}.nupkg'


def compare(previous, current):
    assert previous['packageVersion'] == VERSION and current['packageVersion'] != VERSION
    assert previous['sourceSha256'] == current['sourceSha256']
    assert current['ok'] and not previous['ok']
    fixtures = {case['name']: case for case in json.loads(SOURCE.read_text(encoding='utf-8'))['cases']}
    controls, failed = [], []
    for old, new in zip(previous['cases'], current['cases'], strict=True):
        for key in ['name', 'mode', 'htmlSha256', 'cssSha256']:
            assert old[key] == new[key], (key, old['name'])
        label = old['mode'] + '/' + old['name']
        # This shared fixture flag records the core 2.5.35 failure set, also
        # shipped by public Node 0.8.1 and the public .NET 0.2.1 package.
        if fixtures[old['name']]['public081Fails']:
            assert not old['ok'], (label, 'expected known core 2.5.35 defect')
            failed.append(label)
        else:
            assert old['ok']
            for key in ['pdfSha256', 'previewsSha256', 'htmlPreviewsSha256', 'pdfiumPixelsSha256', 'text', 'pdfiumText', 'glyphBounds']:
                assert old[key] == new[key], (key, label, 'healthy control changed')
            controls.append(label)
    assert len(failed) == 36 and len(controls) == 34
    return dict(ok=True, baselinePackage=VERSION, baselineEngine='2.5.35',
                candidatePackage=current['packageVersion'], baselineFailedCases=failed,
                candidateCases=len(current['cases']), candidateFailedCases=0,
                unchangedInputs=True, unchangedHealthyControls=controls, scope=current['scope'])


def compare_legacy(before, after):
    def files(directory):
        return {path.relative_to(directory).as_posix(): sha256(path.read_bytes()).hexdigest()
                for path in directory.rglob('*') if path.is_file() and path.suffix in ['.pdf', '.png']
                and path.relative_to(directory).parts[0] not in ['table-wrapping', 'document-regressions', 'text-decorations', 'preformatted-whitespace', 'radial-gradient-endpoints', 'page-margin-fonts', 'generated-counters']
                and not any(part.startswith('pdfium') for part in path.relative_to(directory).parts)}
    previous, current = files(before), files(after)
    assert len(previous) > 100, 'Missing existing package-output evidence'
    assert previous.keys() == current.keys(), 'Existing fixture output set changed'
    assert previous == current, [name for name in previous if previous[name] != current[name]]
    return dict(ok=True, unchangedFiles=len(previous), scope='Historical PDF and native PNG fixtures remain byte-identical to public 0.2.1; fixture families introduced in later releases are checked separately.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/table-wrapping-comparison')
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
        assert next(p for p in provenance['dependencies'] if p['name'] == 'fullbleed')['version'] == '2.5.35'
    (output / 'baseline-source.json').write_text(json.dumps(dict(
        url=URL, sha256=PACKAGE_SHA256, bytes=len(data), engine='2.5.35'), indent=2) + '\n', encoding='utf-8')
    baseline = args.baseline_consumer.resolve() if args.baseline_consumer else output / 'baseline'
    if not args.baseline_consumer:
        command = [args.shell, '-NoProfile', '-File', str(ROOT / 'scripts/package-smoke.ps1'), '-SkipPack',
                   '-PackageVersion', VERSION, '-PackageDirectory', str(packages), '-OutputDirectory', str(baseline)]
        with (output / 'baseline-consumer.log').open('w', encoding='utf-8') as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    identity = json.loads((baseline / 'package.json').read_text(encoding='utf-8-sig'))
    assert identity['sha256'] == PACKAGE_SHA256
    previous = verify(baseline / 'table-wrapping', require_correct=False)
    current = verify(args.candidate.resolve() / 'table-wrapping')
    report = compare(previous, current)
    report['legacyOutputComparison'] = compare_legacy(baseline, args.candidate.resolve())
    (output / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
